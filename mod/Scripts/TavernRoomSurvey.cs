using System;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Room identity comes from original bed records, not current door animation or actor positions.
    public static class TavernRoomSurvey
    {
        private sealed class DoorBoundary
        {
            public Matrix4x4 Inverse;
            public Bounds Bounds;
            public bool Blocks(Vector3 from,Vector3 to)
            {
                var a=Inverse.MultiplyPoint3x4(from+Vector3.up);
                var b=Inverse.MultiplyPoint3x4(to+Vector3.up);
                if(Bounds.Contains(a) || Bounds.Contains(b)) return true;
                var delta=b-a; float distance;
                return delta.sqrMagnitude>0.000001f && Bounds.IntersectRay(new Ray(a,delta.normalized),out distance) && distance<=delta.magnitude;
            }
        }
        public static List<LodgingRoom> Read(DaggerfallInterior interior)
        { Dictionary<int,string> markers; return Read(interior,out markers); }
        public static List<LodgingRoom> Read(DaggerfallInterior interior,out Dictionary<int,string> markerRooms)
        {
            var markers=interior.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest);
            var barriers=new List<DoorBoundary>();
            foreach(var door in interior.GetComponentsInChildren<DaggerfallActionDoor>())
            {
                var meshes=door.GetComponentsInChildren<MeshFilter>();
                if(meshes.Length==0) throw new InvalidOperationException("Door lacks surveyable mesh.");
                var closed=Matrix4x4.TRS(door.transform.position,door.ClosedRotation,door.transform.lossyScale);
                foreach(var mesh in meshes)
                {
                    if(!mesh.sharedMesh) throw new InvalidOperationException("Door mesh missing.");
                    var matrix=closed*door.transform.worldToLocalMatrix*mesh.transform.localToWorldMatrix;
                    var bounds=mesh.sharedMesh.bounds;
                    var scale=new Vector3(matrix.MultiplyVector(Vector3.right).magnitude,
                        matrix.MultiplyVector(Vector3.up).magnitude,matrix.MultiplyVector(Vector3.forward).magnitude);
                    if(scale.x<=0 || scale.y<=0 || scale.z<=0) throw new InvalidOperationException("Invalid door scale.");
                    bounds.Expand(new Vector3(0.5f/scale.x,0.5f/scale.y,0.5f/scale.z));
                    barriers.Add(new DoorBoundary { Inverse=matrix.inverse,Bounds=bounds });
                }
            }
            return Partition(interior,markers,barriers,out markerRooms);
        }
        // Model-only layout plus passive record data: no NPC, quest or save registration.
        public static List<LodgingRoom> ReadUnloaded(Transform owner,StaticDoor door,out Dictionary<int,string> markerRooms)
        {
            if(!owner || door.doorType!=DoorTypes.Building) throw new ArgumentException("Missing tavern entrance.");
            var temporary=new GameObject("Living Cities isolated tavern survey");
            var dfu=DaggerfallUnity.Instance;
            try
            {
                var shifted=door; shifted.ownerPosition+=Vector3.up*100;
                var interior=temporary.AddComponent<DaggerfallInterior>();
                if(!interior.DoLayoutAutomap(owner,shifted,ClimateSwaps.FromAPIClimateBase(GameManager.Instance.PlayerGPS.ClimateSettings.ClimateType)))
                    throw new InvalidOperationException("Tavern model survey failed.");
                temporary.transform.position=door.ownerPosition+(Vector3)door.buildingMatrix.GetColumn(3)+Vector3.up*100;
                temporary.transform.rotation=GameObjectHelper.QuaternionFromMatrix(door.buildingMatrix);
                var block=dfu.ContentReader.BlockFileReader.GetBlock(door.blockIndex);
                var records=block.RmbBlock.SubRecords[door.recordIndex].Interior;
                var barriers=new List<DoorBoundary>();
                foreach(var record in records.BlockDoorRecords)
                {
                    ModelData model;
                    if(!dfu.MeshReader.GetModelData((uint)(9000+record.DoorModelIndex%5),out model) || model.Vertices.Length==0)
                        throw new InvalidOperationException("Tavern door model unavailable.");
                    var bounds=new Bounds(model.Vertices[0],Vector3.zero);
                    foreach(var vertex in model.Vertices) bounds.Encapsulate(vertex);
                    bounds.Expand(Vector3.one*.5f);
                    var local=Matrix4x4.TRS(new Vector3(record.XPos,-record.YPos,record.ZPos)*MeshReader.GlobalScale,
                        Quaternion.Euler(0,-record.YRotation/BlocksFile.RotationDivisor,0),Vector3.one);
                    barriers.Add(new DoorBoundary { Inverse=(temporary.transform.localToWorldMatrix*local).inverse,Bounds=bounds });
                }
                var markers=new List<Vector3>();
                foreach(var record in records.BlockFlatObjectRecords)
                {
                    if(record.TextureArchive!=TextureReader.EditorFlatsTextureArchive || record.TextureRecord!=(int)DaggerfallInterior.InteriorMarkerTypes.Rest) continue;
                    var flat=GameObjectHelper.CreateDaggerfallBillboardGameObject(record.TextureArchive,record.TextureRecord,temporary.transform);
                    flat.transform.localPosition=new Vector3(record.XPos,-record.YPos,record.ZPos)*MeshReader.GlobalScale+
                        Vector3.up*(flat.GetComponent<Billboard>().Summary.Size.y*.5f);
                    markers.Add(flat.transform.position);
                }
                foreach(var renderer in temporary.GetComponentsInChildren<Renderer>()) renderer.enabled=false;
                Physics.SyncTransforms();
                return Partition(interior,markers.ToArray(),barriers,out markerRooms);
            }
            finally { temporary.SetActive(false); UnityEngine.Object.Destroy(temporary); Physics.SyncTransforms(); }
        }
        private static List<LodgingRoom> Partition(DaggerfallInterior interior,Vector3[] markers,List<DoorBoundary> barriers,
            out Dictionary<int,string> markerRooms)
        {
            markerRooms=new Dictionary<int,string>();
            Func<Vector3,Vector3,bool> allowed=(a,b)=>!barriers.Exists(d=>d.Blocks(a,b));
            var beds=BedGeometry.Read(interior); beds.Sort((a,b)=>a.RecordIndex.CompareTo(b.RecordIndex));
            var general=new InteriorGeometry(interior);
            var points=new Dictionary<int,Vector3>();
            foreach(var bed in beds)
            {
                Vector3 ground;
                if(!general.StandBesideBed(bed,out ground)) throw new InvalidOperationException("No safe floor beside lodging bed "+bed.RecordIndex);
                points.Add(bed.RecordIndex,ground);
            }
            var assigned=new HashSet<int>(); var result=new List<LodgingRoom>();
            foreach(var bed in beds)
            {
                if(assigned.Contains(bed.RecordIndex)) continue;
                var seed=points[bed.RecordIndex];
                var region=new InteriorGeometry(interior,0.24f,1.65f,seed,allowed);
                if(!region.IsComplete) throw new InvalidOperationException("Incomplete lodging room floor survey: "+bed.RecordIndex);
                var room=new LodgingRoom { Id="room:"+bed.RecordIndex };
                foreach(var candidate in beds)
                    if(!assigned.Contains(candidate.RecordIndex) && region.RouteBetween(interior.transform.InverseTransformPoint(seed),
                        interior.transform.InverseTransformPoint(points[candidate.RecordIndex])).Count>=2)
                    { assigned.Add(candidate.RecordIndex); room.Beds.Add("bed:"+candidate.RecordIndex); }
                if(room.Beds.Count==0) throw new InvalidOperationException("Lodging bed disconnected from its own floor.");
                for(int index=0;index<markers.Length;index++)
                    if(region.ContainsRestMarker(markers[index]))
                    {
                        if(markerRooms.ContainsKey(index)) throw new InvalidOperationException("Rental marker belongs to multiple bed groups: "+index);
                        markerRooms.Add(index,room.Id);
                    }
                result.Add(room);
            }
            if(markerRooms.Count!=markers.Length) throw new InvalidOperationException("Unmapped native rental marker; building="+interior.EntryDoor.buildingKey);
            return result;
        }
    }
}
