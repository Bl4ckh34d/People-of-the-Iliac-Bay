using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect.Arena2;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Optional semantic metadata for custom meshes. Native models are discovered automatically.
    public sealed class SeatFurnitureMetadata : MonoBehaviour
    {
        public string StableId;
        public bool Bench;
        public Bounds UsableSurface;
        public Transform ActivityTarget;
        public Vector3 LocalForward;
    }

    public sealed class FurnitureSeatSlot
    {
        public string Id;
        public string TableId;
        public Vector3 Ground, Surface, Facing, Approach;
        public bool HasApproach;
        public bool HasBackrest;
        public bool HasActivityTarget;
        internal SeatFurniture Furniture;
    }

    internal sealed class SeatTable
    {
        internal string Id;
        internal Matrix4x4 Matrix;
        internal Bounds Bounds;
        internal Vector3 Center {get{return Matrix.MultiplyPoint3x4(Bounds.center);}}
    }

    internal sealed class SeatFurniture
    {
        internal const int MaxClearanceColliders=4;
        internal string Id;
        internal Matrix4x4 Matrix,Inverse;
        internal Bounds Bounds;
        internal Transform Owner;
        internal Vector3[] LocalVertices;
        internal readonly HashSet<Vector3Int> Vertices=new HashSet<Vector3Int>();
        internal readonly Dictionary<Collider,SeatMeshClearance> Clearance=new Dictionary<Collider,SeatMeshClearance>();
        internal bool Contains(Vector3 world,float margin=0)
        {
            var local=Inverse.MultiplyPoint3x4(world);var b=Bounds;b.Expand(margin*2);return b.Contains(local);
        }
        internal bool Blocks(Vector3 from,Vector3 to,float radius)
        {
            var a=Inverse.MultiplyPoint3x4(from);var b=Inverse.MultiplyPoint3x4(to);
            if(Mathf.Min(a.y,b.y)>Bounds.max.y+0.15f || Mathf.Max(a.y,b.y)+1.7f<Bounds.min.y) return false;
            float margin=radius*Mathf.Max(Inverse.MultiplyVector(Vector3.right).magnitude,Inverse.MultiplyVector(Vector3.forward).magnitude);
            var bounds=Bounds;bounds.Expand(new Vector3(margin*2,0,margin*2));
            a.y=b.y=bounds.center.y;
            return bounds.Contains(a)||bounds.Contains(b)||bounds.IntersectRay(new Ray(a,(b-a).normalized),out float distance)&&distance<=(b-a).magnitude;
        }
        internal bool Allows(Collider collider,Vector3 from,Vector3 to,float radius,bool seatTransition=false)
        {
            if(!collider || collider.GetComponentInParent<ResidentActor>() || collider.GetComponentInParent<DaggerfallWorkshop.Game.StaticNPC>() ||
                collider.GetComponentInParent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>()) return false;
            if(Owner && collider.transform.IsChildOf(Owner)) return true;
            var mesh=collider as MeshCollider;if(!mesh || !mesh.sharedMesh || !mesh.sharedMesh.isReadable || Vertices.Count==0) return false;
            SeatMeshClearance cached;
            if(!Clearance.TryGetValue(collider,out cached))
            {
                if(Clearance.Count>=MaxClearanceColliders)return false;
                cached=new SeatMeshClearance(mesh,this);Clearance[collider]=cached;
            }
            return cached.HasSeatTriangles && (seatTransition?cached.ClearSeatTransition(from,to):cached.Clear(from,to,radius));
        }
        internal bool IsSurface(RaycastHit hit)
        {
            if(Owner&&hit.collider.transform.IsChildOf(Owner))return true;
            var mesh=hit.collider as MeshCollider;if(!mesh||!mesh.sharedMesh||!mesh.sharedMesh.isReadable||hit.triangleIndex<0)return false;
            SeatMeshClearance cached;
            if(!Clearance.TryGetValue(hit.collider,out cached))
            {
                if(Clearance.Count>=MaxClearanceColliders)return false;
                cached=new SeatMeshClearance(mesh,this);Clearance[hit.collider]=cached;
            }
            return cached.IsSeatTriangle(hit.triangleIndex);
        }
        internal static Vector3Int Key(Vector3 p)
        {return new Vector3Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.y*1000),Mathf.RoundToInt(p.z*1000));}
    }

    // One catalog per loaded scene root; no whole-world scans during actor ticks.
    public sealed class FurnitureSeatCatalog : MonoBehaviour
    {
        private static readonly HashSet<FurnitureSeatCatalog> loaded=new HashSet<FurnitureSeatCatalog>();
        internal static readonly HashSet<uint> Chairs=new HashSet<uint>{41100,41101,41102,41103};
        internal static readonly HashSet<uint> Benches=new HashSet<uint>{41105,41106,41107,41126,51108,51109,43307};
        internal static readonly HashSet<uint> Tables=new HashSet<uint>{41108,41109,41110,41111,41112,41130,51103,51104};
        public readonly List<FurnitureSeatSlot> Slots=new List<FurnitureSeatSlot>();
        internal readonly List<SeatFurniture> Furniture=new List<SeatFurniture>();
        public readonly List<Vector3> TableTargets=new List<Vector3>();
        private readonly List<SeatTable> tables=new List<SeatTable>();
        private SeatOccupancy reservations;
        private Matrix4x4 surveyedRoot;
        private bool prepared;
        private void Awake() {loaded.Add(this);}
        private void OnDestroy() {loaded.Remove(this);}
        public static FurnitureSeatCatalog ForInterior(DaggerfallInterior room)
        {
            var existing=room.GetComponent<FurnitureSeatCatalog>();if(existing)return existing;
            return BuildInterior(room,DaggerfallUnity.Instance.ContentReader.BlockFileReader.GetBlock(room.EntryDoor.blockIndex),true);
        }
        internal static FurnitureSeatCatalog ForSurvey(DaggerfallInterior room,DFBlock block)
        {
            if(room.GetComponent<FurnitureSeatCatalog>())throw new InvalidOperationException("Survey furniture must be prepared before room geometry.");
            return BuildInterior(room,block,false);
        }
        private static FurnitureSeatCatalog BuildInterior(DaggerfallInterior room,DFBlock block,bool register)
        {
            var door=room.EntryDoor;
            if(block.Type!=DFBlock.BlockTypes.Rmb || block.Index!=door.blockIndex || door.recordIndex<0 ||
                block.RmbBlock.SubRecords==null || door.recordIndex>=block.RmbBlock.SubRecords.Length)
                throw new ArgumentException("Furniture source does not match the native interior.");
            var result=room.gameObject.AddComponent<FurnitureSeatCatalog>();
            // Awake is synchronous; remove a passive survey before any work or
            // yield can expose it to the live scene's collision registry.
            if(!register)loaded.Remove(result);
            var records=block.RmbBlock.SubRecords[door.recordIndex].Interior.Block3dObjectRecords;
            foreach(var record in records)
            {
                uint id=record.ModelIdNum;if(!Chairs.Contains(id)&&!Benches.Contains(id)&&!Tables.Contains(id))continue;
                ModelData model;if(!DaggerfallUnity.Instance.MeshReader.GetModelData(id,out model)||model.Vertices==null||model.Vertices.Length==0)continue;
                Bounds bounds=Bound(model.Vertices);
                Vector3 position=new Vector3(record.XPos,-record.YPos,record.ZPos)*MeshReader.GlobalScale;
                if(record.ObjectType==3)position=new Vector3(record.XPos,record.YPos,record.ZPos)*MeshReader.GlobalScale-Vector3.up*bounds.min.y;
                var rotation=Quaternion.Euler(-record.XRotation/BlocksFile.RotationDivisor,-record.YRotation/BlocksFile.RotationDivisor,-record.ZRotation/BlocksFile.RotationDivisor);
                var matrix=room.transform.localToWorldMatrix*Matrix4x4.TRS(position,rotation,RMBLayout.GetModelScaleVector(record));
                if(Tables.Contains(id))result.AddTable("inn:"+door.buildingKey+":"+id,matrix,bounds);
                else result.Add("inn:"+door.buildingKey+":"+id,matrix,bounds,model.Vertices,model.Normals,Benches.Contains(id),null,null);
            }
            result.ReadLoadedMeshes(room.transform,"inn:"+door.buildingKey);
            result.Finish();return result;
        }
        public static FurnitureSeatCatalog ForLoadedScene(Transform root,string stableSceneId)
        {
            if(!root || string.IsNullOrWhiteSpace(stableSceneId))throw new ArgumentException("A loaded scene and stable identity are required.");
            var existing=root.GetComponent<FurnitureSeatCatalog>();if(existing)return existing;
            var result=root.gameObject.AddComponent<FurnitureSeatCatalog>();result.ReadExteriorRecords(root,stableSceneId);result.ReadLoadedMeshes(root,stableSceneId);result.Finish();return result;
        }
        private void ReadExteriorRecords(Transform root,string scene)
        {
            foreach(var blockObject in root.GetComponentsInChildren<DaggerfallRMBBlock>())
            {
                string name=blockObject.name;int open=name.IndexOf('['),close=name.IndexOf(']');if(open<0||close<=open)continue;
                DFBlock block;if(!DaggerfallUnity.Instance.ContentReader.GetBlock(name.Substring(open+1,close-open-1),out block)||block.Type!=DFBlock.BlockTypes.Rmb)continue;
                foreach(var record in block.RmbBlock.SubRecords)
                {
                    var position=new Vector3(record.XPos,0,BlocksFile.RMBDimension-record.ZPos)*MeshReader.GlobalScale;
                    var parent=blockObject.transform.localToWorldMatrix*Matrix4x4.TRS(position,Quaternion.Euler(0,-record.YRotation/BlocksFile.RotationDivisor,0),Vector3.one);
                    foreach(var model in record.Exterior.Block3dObjectRecords)AddExteriorRecord(scene,parent,model,false);
                }
                foreach(var model in block.RmbBlock.Misc3dObjectRecords)AddExteriorRecord(scene,blockObject.transform.localToWorldMatrix,model,true);
            }
        }
        private void AddExteriorRecord(string scene,Matrix4x4 parent,DFBlock.RmbBlock3dObjectRecord record,bool prop)
        {
            uint id=record.ModelIdNum;if(!Chairs.Contains(id)&&!Benches.Contains(id)&&!Tables.Contains(id))return;
            ModelData model;if(!DaggerfallUnity.Instance.MeshReader.GetModelData(id,out model)||model.Vertices==null||model.Vertices.Length==0)return;
            var position=new Vector3(record.XPos,-record.YPos+(prop?-4:0),record.ZPos+(prop?BlocksFile.RMBDimension:0))*MeshReader.GlobalScale;
            var rotation=Quaternion.Euler(-record.XRotation/BlocksFile.RotationDivisor,-record.YRotation/BlocksFile.RotationDivisor,-record.ZRotation/BlocksFile.RotationDivisor);
            var matrix=parent*Matrix4x4.TRS(position,rotation,RMBLayout.GetModelScaleVector(record));var bounds=Bound(model.Vertices);
            if(Tables.Contains(id))AddTable(scene+":"+id,matrix,bounds);else Add(scene+":"+id,matrix,bounds,model.Vertices,model.Normals,Benches.Contains(id),null,null);
        }
        private void ReadLoadedMeshes(Transform root,string scene)
        {
            foreach(var owner in root.GetComponentsInChildren<Transform>())
            {
                if(Furniture.Count>=256)break;
                var metadata=owner.GetComponent<SeatFurnitureMetadata>();uint id;
                bool native=NativeId(owner.name,out id);
                if(!metadata && (!native || !Chairs.Contains(id)&&!Benches.Contains(id)&&!Tables.Contains(id)))continue;
                var vertices=new List<Vector3>();var normals=new List<Vector3>();
                foreach(var filter in owner.GetComponentsInChildren<MeshFilter>())
                {
                    if(!filter.sharedMesh||!filter.sharedMesh.isReadable)continue;
                    var points=filter.sharedMesh.vertices;var directions=filter.sharedMesh.normals;if(points.Length!=directions.Length)continue;
                    var matrix=owner.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                    for(int i=0;i<points.Length;i++){vertices.Add(matrix.MultiplyPoint3x4(points[i]));normals.Add(matrix.MultiplyVector(directions[i]).normalized);}
                }
                if(vertices.Count==0)continue;var bounds=Bound(vertices.ToArray());
                if(native&&Tables.Contains(id)){AddTable(scene+":"+id,owner.localToWorldMatrix,bounds);continue;}
                // A native record can be replaced by a differently sized mod mesh.
                // Replace its inferred geometry with the actual loaded geometry.
                for(int i=Furniture.Count-1;i>=0;i--)if((Furniture[i].Matrix.MultiplyPoint3x4(Vector3.zero)-owner.position).sqrMagnitude<0.0025f)
                {var old=Furniture[i];Slots.RemoveAll(s=>s.Furniture==old);Furniture.RemoveAt(i);}
                Add(scene+":"+(metadata&&!string.IsNullOrWhiteSpace(metadata.StableId)?metadata.StableId:id.ToString()),owner.localToWorldMatrix,bounds,vertices.ToArray(),normals.ToArray(),
                    metadata?metadata.Bench:Benches.Contains(id),owner,metadata);
            }
        }
        private static bool NativeId(string name,out uint id)
        {
            id=0;const string prefix="DaggerfallMesh [ID=";
            if(!name.StartsWith(prefix,StringComparison.Ordinal))return false;
            int end=name.IndexOf(']',prefix.Length);return end>prefix.Length&&uint.TryParse(name.Substring(prefix.Length,end-prefix.Length),out id);
        }
        private void AddTable(string identity,Matrix4x4 matrix,Bounds bounds)
        {
            // Replacement meshes retain native origin identity while supplying
            // their actual geometry. This also removes duplicate table targets.
            var origin=matrix.MultiplyPoint3x4(Vector3.zero);
            foreach(var table in tables)
                if((table.Matrix.MultiplyPoint3x4(Vector3.zero)-origin).sqrMagnitude<0.0025f)
                {table.Matrix=matrix;table.Bounds=bounds;return;}
            if(tables.Count>=256)return;
            tables.Add(new SeatTable{Id=identity+":table:"+Stable(transform.InverseTransformPoint(origin)),Matrix=matrix,Bounds=bounds});
        }
        private void Add(string identity,Matrix4x4 matrix,Bounds bounds,Vector3[] vertices,Vector3[] normals,bool bench,Transform owner,SeatFurnitureMetadata metadata)
        {
            if(Furniture.Count>=256)return;
            var furniture=new SeatFurniture {Id=identity+":"+Stable(transform.InverseTransformPoint(matrix.MultiplyPoint3x4(bounds.center))),Matrix=matrix,Inverse=matrix.inverse,Bounds=bounds,Owner=owner,LocalVertices=vertices};
            foreach(var vertex in vertices)furniture.Vertices.Add(SeatFurniture.Key(matrix.MultiplyPoint3x4(vertex)));
            Furniture.Add(furniture);
            Bounds surface;
            if(metadata&&metadata.UsableSurface.size.x>0&&metadata.UsableSurface.size.z>0)surface=metadata.UsableSurface;
            else if(!Surface(vertices,normals,bounds,out surface))return;
            var axis=surface.size.x>=surface.size.z?Vector3.right:Vector3.forward;
            Bounds upper=new Bounds();bool upperFound=false;
            foreach(var point in vertices)if(point.y>surface.center.y+0.18f)
            {if(!upperFound){upper=new Bounds(point,Vector3.zero);upperFound=true;}else upper.Encapsulate(point);}
            var front=upperFound?surface.center-upper.center:Vector3.zero;front.y=0;
            bool backrest=front.sqrMagnitude>0.0064f;
            if(metadata&&metadata.LocalForward.sqrMagnitude>0.001f){front=metadata.LocalForward;backrest=true;}
            float scale=matrix.MultiplyVector(axis).magnitude;
            var offsets=SeatOccupancy.Offsets((surface.size.x>=surface.size.z?surface.size.x:surface.size.z)*scale,bench);
            for(int i=0;i<offsets.Count;i++)
            {
                var local=surface.center+axis*(float)(offsets[i]/scale);local.y=bounds.min.y;
                Slots.Add(new FurnitureSeatSlot {Id=furniture.Id+"/"+i,Ground=matrix.MultiplyPoint3x4(local),Surface=matrix.MultiplyPoint3x4(new Vector3(local.x,surface.center.y,local.z)),HasBackrest=backrest,HasActivityTarget=metadata&&metadata.ActivityTarget,
                    Facing=backrest?matrix.MultiplyVector(front):metadata&&metadata.ActivityTarget?metadata.ActivityTarget.position-matrix.MultiplyPoint3x4(local):matrix.MultiplyVector(Vector3.forward),Furniture=furniture});
            }
        }
        private void Finish()
        {
            TableTargets.Clear();foreach(var table in tables)TableTargets.Add(table.Center);
            var ids=new List<string>();
            foreach(var slot in Slots)
            {
                float distance=16;
                foreach(var table in slot.HasBackrest||slot.HasActivityTarget?new List<Vector3>():TableTargets)
                {var delta=table-slot.Ground;float square=delta.x*delta.x+delta.z*delta.z;if(square<distance&&Math.Abs(delta.y)<1.8f){distance=square;slot.Facing=delta;}}
                slot.Facing=new Vector3(slot.Facing.x,0,slot.Facing.z).normalized;ids.Add(slot.Id);
                SeatTable closest=null;float nearest=1.2f*1.2f;
                foreach(var table in tables)
                {
                    var delta=table.Center-slot.Ground;delta.y=0;
                    if(delta.sqrMagnitude>16 || Math.Abs(table.Center.y-slot.Ground.y)>1.8f ||
                        Vector3.Dot(slot.Facing,delta.normalized)<0.25f)continue;
                    var local=table.Matrix.inverse.MultiplyPoint3x4(slot.Ground);
                    var edge=table.Matrix.MultiplyPoint3x4(table.Bounds.ClosestPoint(local));
                    var gap=edge-slot.Ground;gap.y=0;
                    if(gap.sqrMagnitude<nearest || gap.sqrMagnitude==nearest && closest!=null &&
                        string.CompareOrdinal(table.Id,closest.Id)<0)
                    {closest=table;nearest=gap.sqrMagnitude;}
                }
                slot.TableId=closest==null?null:closest.Id;
            }
            reservations=new SeatOccupancy(ids);
            surveyedRoot=transform.localToWorldMatrix;prepared=true;
        }
        public void RefreshSceneTransform()
        {
            if(!prepared||surveyedRoot==transform.localToWorldMatrix)return;
            var change=transform.localToWorldMatrix*surveyedRoot.inverse;
            foreach(var slot in Slots){slot.Ground=change.MultiplyPoint3x4(slot.Ground);slot.Surface=change.MultiplyPoint3x4(slot.Surface);slot.Approach=change.MultiplyPoint3x4(slot.Approach);slot.Facing=change.MultiplyVector(slot.Facing).normalized;}
            foreach(var furniture in Furniture)
            {
                furniture.Matrix=change*furniture.Matrix;furniture.Inverse=furniture.Matrix.inverse;furniture.Clearance.Clear();furniture.Vertices.Clear();
                foreach(var point in furniture.LocalVertices)furniture.Vertices.Add(SeatFurniture.Key(furniture.Matrix.MultiplyPoint3x4(point)));
            }
            for(int i=0;i<TableTargets.Count;i++)TableTargets[i]=change.MultiplyPoint3x4(TableTargets[i]);
            foreach(var table in tables)table.Matrix=change*table.Matrix;
            surveyedRoot=transform.localToWorldMatrix;
        }
        public FurnitureSeatSlot Find(string id) {RefreshSceneTransform();foreach(var slot in Slots)if(slot.Id==id)return slot;return null;}
        public bool SetApproach(FurnitureSeatSlot slot,Vector3 candidate,Action<string> rejected=null)
        {
            bool Reject(string reason){if(rejected!=null)rejected(reason);return false;}
            if(slot==null||Vector3.Distance(candidate,slot.Ground)>1.6f)return Reject("distance");
            if(BlocksGeometry(candidate,slot.Ground,CrowdMotion.BodyRadius,slot.Furniture))return Reject("other furniture footprint");
            var delta=slot.Ground-candidate;
            foreach(var collider in Physics.OverlapCapsule(slot.Ground+Vector3.up*(0.51f+CrowdMotion.BodyRadius),slot.Ground+Vector3.up*1.65f,CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Ignore))
                if(CivilianResponses.Blocker(collider)&&!slot.Furniture.Allows(collider,candidate,slot.Ground,CrowdMotion.BodyRadius,true))
                {
                    SeatMeshClearance evidence;slot.Furniture.Clearance.TryGetValue(collider,out evidence);
                    return Reject("seat capsule: "+collider.name+"; seatTriangles="+(evidence!=null&&evidence.HasSeatTriangles)+"; supported="+(evidence!=null&&evidence.Supported));
                }
            foreach(var hit in Physics.CapsuleCastAll(candidate+Vector3.up*(0.51f+CrowdMotion.BodyRadius),candidate+Vector3.up*1.65f,CrowdMotion.BodyRadius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(CivilianResponses.Blocker(hit.collider)&&!slot.Furniture.Allows(hit.collider,candidate,slot.Ground,CrowdMotion.BodyRadius,true))return Reject("approach sweep: "+hit.collider.name);
            slot.Approach=candidate;slot.HasApproach=true;return true;
        }
        public bool TryClaim(string owner,string id,double now) {return Find(id)!=null&&reservations.TryClaim(owner,id,now);}
        public bool TryAcquire(string owner,string id,out SelectedSeatAccess access)
        {var slot=Find(id);access=null;if(slot==null||!slot.HasApproach||!TryClaim(owner,id,Time.unscaledTime))return false;access=new SelectedSeatAccess(this,slot,owner);return true;}
        public bool CanShareTable(string firstId,string secondId)
        {
            var first=Find(firstId);var second=Find(secondId);
            return first!=null && second!=null && first.HasApproach && second.HasApproach &&
                SharedTableSeats.CanShare(TablePosition(first),TablePosition(second));
        }
        private static TableSeatPosition TablePosition(FurnitureSeatSlot slot)
        {return new TableSeatPosition{Id=slot.Id,TableId=slot.TableId,X=slot.Ground.x,Z=slot.Ground.z,
            Height=slot.Surface.y,FacingX=slot.Facing.x,FacingZ=slot.Facing.z};}
        internal bool TryClaimPair(string firstOwner,string firstId,string secondOwner,string secondId)
        {return CanShareTable(firstId,secondId) && reservations.TryClaimPair(firstOwner,firstId,secondOwner,secondId,Time.unscaledTime);}
        public bool TryAcquirePair(string firstOwner,string firstId,string secondOwner,string secondId,out SelectedSeatPairAccess access)
        {
            access=null;if(!TryClaimPair(firstOwner,firstId,secondOwner,secondId))return false;
            access=new SelectedSeatPairAccess(this,new SelectedSeatAccess(this,Find(firstId),firstOwner),
                new SelectedSeatAccess(this,Find(secondId),secondOwner));return true;
        }
        public bool Owns(string owner,string id,double now) {return reservations!=null&&reservations.Owns(owner,id,now);}
        public bool Available(string owner,string id,double now) {return reservations!=null&&reservations.Available(owner,id,now);}
        public void Release(string owner) {if(reservations!=null)reservations.Release(owner);}
        internal bool BlocksGeometry(Vector3 from,Vector3 to,float radius,SeatFurniture allowed=null)
        {
            if(!loaded.Contains(this))
            {RefreshSceneTransform();foreach(var furniture in Furniture)if(furniture!=allowed && furniture.Blocks(from,to,radius))return true;}
            return Blocks(from,to,radius,allowed);
        }
        internal static bool Blocks(Vector3 from,Vector3 to,float radius,SeatFurniture allowed=null)
        {
            foreach(var catalog in loaded)if(catalog&&catalog.gameObject.activeInHierarchy)
            {catalog.RefreshSceneTransform();foreach(var furniture in catalog.Furniture)if(furniture!=allowed&&furniture.Blocks(from,to,radius))return true;}
            return false;
        }
        private static string Stable(Vector3 p)
        {var v=SeatFurniture.Key(p);return v.x.ToString(CultureInfo.InvariantCulture)+","+v.y.ToString(CultureInfo.InvariantCulture)+","+v.z.ToString(CultureInfo.InvariantCulture);}
        private static Bounds Bound(Vector3[] vertices){var b=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)b.Encapsulate(v);return b;}
        private static bool Surface(Vector3[] vertices,Vector3[] normals,Bounds bounds,out Bounds surface)
        {
            surface=new Bounds();if(normals==null||vertices.Length!=normals.Length)return false;
            var planes=new Dictionary<int,Bounds>();
            for(int i=0;i<vertices.Length;i++)
            {
                var v=vertices[i];if(normals[i].y<0.9f||v.y-bounds.min.y<0.25f||v.y-bounds.min.y>1.05f)continue;
                int key=Mathf.RoundToInt(v.y*100);Bounds b;if(!planes.TryGetValue(key,out b))b=new Bounds(v,Vector3.zero);else b.Encapsulate(v);planes[key]=b;
            }
            float area=0;foreach(var b in planes.Values)if(b.size.x*b.size.z>area){surface=b;area=b.size.x*b.size.z;}
            return area>=0.12f;
        }
    }
}
