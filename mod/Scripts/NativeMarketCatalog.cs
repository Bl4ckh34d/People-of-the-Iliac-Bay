using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class NativeMarketGoodsEvidence
    {
        internal MarketGoodsKind Kind;
        internal int Archive,Record;
        internal Vector3 LocalPosition;
        internal Transform Root;
        internal Vector3 WorldPosition {get{return Root.TransformPoint(LocalPosition);}}
    }
    internal sealed class NativeMarketPost
    {
        internal string Id;
        internal string ShowId;
        internal MarketPostRole Role;
        internal StaticNPC Seller;
        internal Transform Root;
        internal Vector3 LocalPost,LocalDisplay,LocalBase;
        internal Bounds LocalTable;
        internal bool HasTable;
        internal readonly List<NativeMarketGoodsEvidence> Goods=new List<NativeMarketGoodsEvidence>();
        internal readonly List<NativeMarketSlot> Slots=new List<NativeMarketSlot>();
        internal Vector3 PostWorldPosition {get{return Root.TransformPoint(LocalPost);}}
        internal Vector3 BaseWorldPosition {get{return Root.TransformPoint(LocalBase);}}
        internal Vector3 DisplayWorldPosition {get{return Root.TransformPoint(LocalDisplay);}}
        internal bool Available
        {
            get
            {
                if(!Seller || !Seller.gameObject.activeInHierarchy ||
                    Vector3.Distance(Seller.transform.position,PostWorldPosition)>=2.5f)return false;
                var actor=Seller.GetComponent<ResidentActor>();
                if(actor && (actor.Resident==null || !actor.Resident.IsAlive || actor.IsSleeping))return false;
                var pose=Seller.GetComponent<SleepingPose>();if(pose && pose.Active)return false;
                var commuter=Seller.GetComponent<NativeMarketCommuter>();
                return !commuter || commuter.AtPost && !commuter.InsideHome && commuter.Phase=="work" &&
                    commuter.Person!=null && commuter.Person.IsAlive;
            }
        }
    }
    internal sealed class NativeMarketSlot
    {
        internal string Id;
        internal NativeMarketPost Post;
        internal Vector3 LocalGround,LocalLook;
        internal bool Payment;
        internal Vector3 Ground {get{return Post.Root.TransformPoint(LocalGround);}}
        internal Vector3 Look {get{return Post.Root.TransformPoint(LocalLook);}}
    }

    // One source-backed catalog per loaded town. Layout positions survive origin
    // shifts. No sprite classifier, image model, invented seller or world point.
    internal sealed class NativeMarketCatalog : MonoBehaviour
    {
        internal readonly List<NativeMarketPost> Posts=new List<NativeMarketPost>();
        private sealed class Flat {internal string Id;internal int Hash,Archive,Record;internal Vector3 Ground;internal Transform Block;internal bool SalesRecord;}
        private sealed class Table {internal Vector3 Center;internal Bounds Bounds;}
        private sealed class Claim {internal string Person;internal NativeMarketSlot Slot;internal double Until;internal ResidentActor Body;}
        private readonly List<Claim> claims=new List<Claim>();
        private BuildingDirectory directory;
        private bool slotsPrepared;
        internal static NativeMarketCatalog For(BuildingDirectory source)
        {
            if(!source)return null;
            var result=source.GetComponent<NativeMarketCatalog>();
            if(result)return result;
            result=source.gameObject.AddComponent<NativeMarketCatalog>();result.directory=source;result.Read();return result;
        }
        internal NativeMarketPost Find(StaticNPC npc)
        {foreach(var post in Posts)if(post.Seller==npc)return post;return null;}
        internal NativeMarketPost Find(string id)
        {foreach(var post in Posts)if(post.Id==id)return post;return null;}
        private void Read()
        {
            var people=new List<Flat>();var goods=new List<NativeMarketGoodsEvidence>();var tables=new List<Table>();
            foreach(var blockObject in directory.GetComponentsInChildren<DaggerfallRMBBlock>())
            {
                string name=blockObject.name;int open=name.IndexOf('['),close=name.IndexOf(']');
                if(open<0||close<=open)continue;
                string layout=name.Substring(open+1,close-open-1);DFBlock block;
                if(!DaggerfallUnity.Instance.ContentReader.GetBlock(layout,out block)||block.Type!=DFBlock.BlockTypes.Rmb)continue;
                string prefix=layout+":"+Stable(transform.InverseTransformPoint(blockObject.transform.position));
                for(int sub=0;sub<block.RmbBlock.SubRecords.Length;sub++)
                {
                    var record=block.RmbBlock.SubRecords[sub];
                    // RMBLayout.AddExteriorBlockFlats deliberately does not rotate
                    // exterior flats with the building's model matrix.
                    foreach(var flat in record.Exterior.BlockFlatObjectRecords)
                    {
                        var local=new Vector3(record.XPos+flat.XPos,-flat.YPos-6,
                            BlocksFile.RMBDimension-record.ZPos+flat.ZPos)*MeshReader.GlobalScale;
                        var ground=blockObject.transform.TransformPoint(local);
                        if(RDBLayout.IsNPCFlat(flat.TextureArchive) && flat.FactionID!=0)
                            people.Add(new Flat {Id=prefix+":"+sub+":"+flat.Position,Hash=StaticNPC.GetPositionHash(flat.XPos,flat.YPos,flat.ZPos),
                                Archive=flat.TextureArchive,Record=flat.TextureRecord,Ground=ground,Block=blockObject.transform,
                                SalesRecord=string.Equals(layout,"MARKAA00.RMB",StringComparison.OrdinalIgnoreCase)&&sub==0 ||
                                    string.Equals(layout,"MARKAA01.RMB",StringComparison.OrdinalIgnoreCase)&&sub==2});
                        var kind=MarketActivity.Goods(flat.TextureArchive,flat.TextureRecord);
                        if(kind!=MarketGoodsKind.None)goods.Add(new NativeMarketGoodsEvidence {Kind=kind,Archive=flat.TextureArchive,
                            Record=flat.TextureRecord,Root=transform,LocalPosition=transform.InverseTransformPoint(ground)});
                    }
                    var parent=blockObject.transform.localToWorldMatrix*Matrix4x4.TRS(
                        new Vector3(record.XPos,0,BlocksFile.RMBDimension-record.ZPos)*MeshReader.GlobalScale,
                        Quaternion.Euler(0,-record.YRotation/BlocksFile.RotationDivisor,0),Vector3.one);
                    foreach(var model in record.Exterior.Block3dObjectRecords)ReadTable(model,parent,false,tables);
                }
                foreach(var model in block.RmbBlock.Misc3dObjectRecords)ReadTable(model,blockObject.transform.localToWorldMatrix,true,tables);
            }
            var bodies=directory.GetComponentsInChildren<StaticNPC>(true);
            foreach(var flat in people)
            {
                if(Posts.Count>=128)break;StaticNPC body=null;float best=1.5f;
                foreach(var candidate in bodies)
                {
                    if(candidate.Data.hash!=flat.Hash || candidate.Data.billboardArchiveIndex!=flat.Archive ||
                        candidate.Data.billboardRecordIndex!=flat.Record || !candidate.transform.IsChildOf(flat.Block) ||
                        candidate.IsChildNPC || candidate.GetComponent<QuestResourceBehaviour>() || candidate.GetComponent<DaggerfallAction>())continue;
                    // Native billboards move their pivot to their visual centre.
                    var gap=candidate.transform.position-flat.Ground;gap.y=0;float distance=gap.magnitude;
                    if(distance<best){best=distance;body=candidate;}
                }
                if(!body)
                {
                    if(MarketActivity.Performer(flat.Archive,flat.Record) && MarketProbeDiagnostics())
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_PERFORMER_REJECTED: id="+flat.Id+"; reason=no-matched-native-body; sourceGround="+flat.Ground.ToString("R"));
                    continue;
                }
                var post=new NativeMarketPost {Id=flat.Id,Seller=body,Root=transform,LocalPost=transform.InverseTransformPoint(body.transform.position),
                    LocalBase=transform.InverseTransformPoint(flat.Ground)};
                if(MarketActivity.Performer(flat.Archive,flat.Record))
                {
                    // Reused musician art alone cannot establish a performance.
                    // Require the authored base on real raised stage geometry.
                    bool raised=RaisedSurface(flat.Ground);
                    if(flat.Ground.y-flat.Block.position.y<0.6f || !raised)
                    {
                        if(MarketProbeDiagnostics())Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_PERFORMER_REJECTED: id="+flat.Id+
                            "; reason=unverified-raised-stage; baseAboveBlock="+(flat.Ground.y-flat.Block.position.y)+"; raisedCollision="+raised);
                        continue;
                    }
                    post.Role=MarketPostRole.Performer;post.LocalDisplay=transform.InverseTransformPoint(flat.Ground+Vector3.up);Posts.Add(post);continue;
                }
                // Raised terrace patrons drinking beside cups are not market
                // sellers. The verified prototype's sales posts are street-level.
                if(!flat.SalesRecord || flat.Ground.y-flat.Block.position.y>0.6f)continue;
                var counts=new Dictionary<MarketGoodsKind,int>();Vector3 total=Vector3.zero;
                foreach(var item in goods)
                {
                    var gap=item.WorldPosition-flat.Ground;gap.y=0;
                    if(gap.sqrMagnitude>12.25f || item.WorldPosition.y<flat.Ground.y+0.15f || item.WorldPosition.y>flat.Ground.y+1.8f)continue;
                    // Attribute each displayed object to its nearest authored adult
                    // person, so adjacent stands cannot both claim the same fish.
                    bool nearest=true;
                    foreach(var other in people)
                    {
                        if(other==flat || MarketActivity.Performer(other.Archive,other.Record))continue;
                        var otherGap=item.WorldPosition-other.Ground;otherGap.y=0;
                        if(otherGap.sqrMagnitude+0.001f<gap.sqrMagnitude){nearest=false;break;}
                    }
                    if(!nearest)continue;
                    post.Goods.Add(item);total+=item.WorldPosition;int count;counts.TryGetValue(item.Kind,out count);counts[item.Kind]=count+1;
                }
                if(post.Goods.Count==0)continue;
                var display=total/post.Goods.Count;Table table=null;int bestCount=0;float tableGap=float.MaxValue;
                foreach(var candidate in tables)
                {
                    int onTable=0;
                    foreach(var item in post.Goods)
                    {
                        var point=item.LocalPosition;
                        if(Vector3.Distance(candidate.Bounds.ClosestPoint(point),point)<0.65f)onTable++;
                    }
                    float gap=Vector3.Distance(candidate.Center,transform.InverseTransformPoint(flat.Ground));
                    if(onTable>bestCount || onTable==bestCount && onTable>0 && gap<tableGap)
                    {table=candidate;tableGap=gap;bestCount=onTable;}
                }
                // The prototype requires a real placed table, not loose clutter.
                if(table==null)continue;
                post.Goods.RemoveAll(item=>Vector3.Distance(table.Bounds.ClosestPoint(item.LocalPosition),item.LocalPosition)>=0.65f);
                counts.Clear();total=Vector3.zero;
                foreach(var item in post.Goods)
                {total+=item.WorldPosition;int count;counts.TryGetValue(item.Kind,out count);counts[item.Kind]=count+1;}
                display=total/post.Goods.Count;
                post.HasTable=true;post.LocalTable=table.Bounds;post.LocalDisplay=transform.InverseTransformPoint(display);
                MarketGoodsKind dominant=MarketGoodsKind.None;int highest=0;
                foreach(var pair in counts)if(pair.Value>highest){dominant=pair.Key;highest=pair.Value;}
                post.Role=MarketActivity.Role(dominant);Posts.Add(post);
            }
            Posts.Sort((a,b)=>string.CompareOrdinal(a.Id,b.Id));
            Posts.RemoveAll(post=>post.Role==MarketPostRole.Performer && !Posts.Exists(other=>
                other!=post && other.Role==MarketPostRole.Performer && Vector3.Distance(other.PostWorldPosition,post.PostWorldPosition)<5));
        }
        private void ReadTable(DFBlock.RmbBlock3dObjectRecord record,Matrix4x4 parent,bool prop,List<Table> result)
        {
            if(!FurnitureSeatCatalog.Tables.Contains(record.ModelIdNum))return;
            ModelData model;if(!DaggerfallUnity.Instance.MeshReader.GetModelData(record.ModelIdNum,out model)||model.Vertices==null||model.Vertices.Length==0)return;
            var matrix=parent*Matrix4x4.TRS(new Vector3(record.XPos,-record.YPos+(prop?-4:0),record.ZPos+(prop?BlocksFile.RMBDimension:0))*MeshReader.GlobalScale,
                Quaternion.Euler(-record.XRotation/BlocksFile.RotationDivisor,-record.YRotation/BlocksFile.RotationDivisor,-record.ZRotation/BlocksFile.RotationDivisor),
                RMBLayout.GetModelScaleVector(record));
            matrix=transform.worldToLocalMatrix*matrix;
            var bounds=new Bounds(matrix.MultiplyPoint3x4(model.Vertices[0]),Vector3.zero);
            foreach(var point in model.Vertices)bounds.Encapsulate(matrix.MultiplyPoint3x4(point));
            result.Add(new Table {Center=bounds.center,Bounds=bounds});
        }
        internal void PrepareSlots()
        {
            if(slotsPrepared || !NativeNavigationWorld.Current || !NativeNavigationWorld.Current.Ready ||
                NativeNavigationWorld.Current.Scene!="city-"+directory.MapID)return;
            slotsPrepared=true;
            foreach(var post in Posts)
            {
                if(post.Role==MarketPostRole.Performer){PrepareShow(post);continue;}
                var display=post.DisplayWorldPosition;var seller=post.PostWorldPosition;
                var normal=display-seller;normal.y=0;if(normal.sqrMagnitude<0.09f)continue;normal.Normalize();
                var side=Vector3.Cross(Vector3.up,normal);
                var localSeller=transform.InverseTransformPoint(seller);
                float radius=Mathf.Max(0.8f,Vector3.Distance(post.LocalTable.ClosestPoint(localSeller),localSeller)+0.6f);
                for(int index=0;index<5;index++)
                {
                    var desired=display+normal*radius+side*((index-2)*0.75f);desired.y=post.BaseWorldPosition.y;
                    AddSlot(post,desired,index==2?seller:display,index==2,false);
                }
            }
        }
        private void PrepareShow(NativeMarketPost post)
        {
            if(post.ShowId!=null)return;
            // A lone musician does not prove a show. Require other placed
            // performers on the same raised stage, then infer its public front
            // from reachable lower ground and unobstructed sight lines.
            Vector3 center=Vector3.zero;var group=new List<NativeMarketPost>();
            foreach(var other in Posts)if(other.Role==MarketPostRole.Performer &&
                Vector3.Distance(other.PostWorldPosition,post.PostWorldPosition)<5){group.Add(other);center+=other.PostWorldPosition;}
            if(group.Count<2)return;center/=group.Count;
            if(group[0]!=post)return;
            foreach(var performer in group)performer.ShowId=post.Id;
            Vector3 axis=group[group.Count-1].PostWorldPosition-group[0].PostWorldPosition;axis.y=0;
            if(axis.sqrMagnitude<0.1f)return;axis.Normalize();var normal=Vector3.Cross(Vector3.up,axis);
            var best=new List<NativeMarketSlot>();
            foreach(int sign in new[]{1,-1})
            {
                var candidate=new List<NativeMarketSlot>();
                for(int row=0;row<3;row++)for(int col=-3;col<=3;col++)
                {
                    var point=center+normal*(sign*(2.5f+row*0.8f))+axis*(col*0.8f);
                    NativeMarketSlot slot;if(TrySlot(post,point,center,false,true,out slot))candidate.Add(slot);
                }
                if(candidate.Count>best.Count)best=candidate;
            }
            foreach(var slot in best){slot.Id=post.Id+":show:"+post.Slots.Count;post.Slots.Add(slot);}
        }
        private void AddSlot(NativeMarketPost post,Vector3 point,Vector3 look,bool payment,bool show)
        {NativeMarketSlot slot;if(TrySlot(post,point,look,payment,show,out slot)){slot.Id=post.Id+":slot:"+post.Slots.Count;post.Slots.Add(slot);}}
        private bool TrySlot(NativeMarketPost post,Vector3 point,Vector3 look,bool payment,bool show,out NativeMarketSlot slot)
        {
            slot=null;NavMeshHit sample;
            if(!NavMesh.SamplePosition(point,out sample,show?3.5f:2,NavMesh.AllAreas))return MarketSlotRejected(post,point,Vector3.zero,"no-native-navmesh-sample");
            if(Horizontal(sample.position-point)>0.35f)return MarketSlotRejected(post,point,sample.position,"sample-outside-horizontal-tolerance");
            if(show && sample.position.y>=post.BaseWorldPosition.y-0.35f ||
                !show && Mathf.Abs(sample.position.y-post.BaseWorldPosition.y)>0.8f)return MarketSlotRejected(post,point,sample.position,"sample-outside-native-height");
            if(!ClearBody(sample.position,null,true))return MarketSlotRejected(post,point,sample.position,"body-space-blocked");
            if(!Visible(sample.position+Vector3.up*1.5f,look))return MarketSlotRejected(post,point,sample.position,"display-sightline-blocked");
            foreach(var existing in post.Slots)if(Vector3.Distance(existing.Ground,sample.position)<0.65f)return false;
            slot=new NativeMarketSlot {Post=post,LocalGround=transform.InverseTransformPoint(sample.position),
                LocalLook=transform.InverseTransformPoint(look),Payment=payment};return true;
        }
        private static bool MarketProbeDiagnostics()
        {return Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="market-visit";}
        private static bool MarketSlotRejected(NativeMarketPost post,Vector3 requested,Vector3 sampled,string reason)
        {
            if(MarketProbeDiagnostics())Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_SLOT_REJECTED: id="+post.Id+"; reason="+reason+
                "; requested="+requested.ToString("R")+"; sampled="+sampled.ToString("R"));
            return false;
        }
        internal bool TryClaim(NativeMarketSlot slot,string person,Vector3 from,double now,double until,ResidentActor customer=null)
        {
            claims.RemoveAll(c=>c.Until<=now);
            if(!slot.Post.Available || until<=now || !NativeNavigationWorld.Current || !NativeNavigationWorld.Current.Ready ||
                NativeNavigationWorld.Current.Scene!="city-"+directory.MapID || !ClearBody(slot.Ground,person))return false;
            foreach(var claim in claims)if(claim.Person!=person && Vector3.Distance(claim.Slot.Ground,slot.Ground)<0.75f)return false;
            var path=new NavMeshPath();NavMeshHit start;
            if(!NavMesh.SamplePosition(from,out start,0.5f,NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(start.position,slot.Ground,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete ||
                !NativeNavigationWorld.Current.ClearCityGatePath(path.corners))return false;
            claims.RemoveAll(c=>c.Person==person);
            claims.Add(new Claim {Person=person,Slot=slot,Until=until,Body=customer});return true;
        }
        internal void Release(string person){claims.RemoveAll(c=>c.Person==person);}
        internal bool HasCustomer(string postId,double now)
        {
            var post=Find(postId);
            foreach(var claim in claims)
            {
                if(claim.Until<=now || !claim.Body || !claim.Body.gameObject.activeInHierarchy || !claim.Body.Resident.IsAlive ||
                    !(claim.Slot.Post.Id==postId || post!=null && post.ShowId!=null && claim.Slot.Post.ShowId==post.ShowId))continue;
                var native=claim.Body.GetComponent<NativeResidentNavigation>();
                if(Vector3.Distance(claim.Body.transform.position-Vector3.up,claim.Slot.Ground)<=1.2f ||
                    native && native.HasDestination && Vector3.Distance(native.Destination,claim.Slot.Ground)<=0.5f)return true;
            }
            return false;
        }
        internal static bool ClearBody(Vector3 ground,string owner=null,bool ignoreResidents=false)
        {
            foreach(var collider in Physics.OverlapCapsule(ground+Vector3.up*0.3f,ground+Vector3.up*1.65f,0.26f,~0,QueryTriggerInteraction.Ignore))
            {
                if(collider.GetComponent<TerrainCollider>())continue;
                var resident=collider.GetComponentInParent<ResidentActor>();
                if(resident && (ignoreResidents || resident.Resident!=null && resident.Resident.Id==owner))continue;
                if(collider.GetComponentInParent<StaticNPC>() || collider.GetComponentInParent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>())return false;
                return false;
            }
            return true;
        }
        private static bool Visible(Vector3 from,Vector3 target)
        {
            var delta=target-from;
            foreach(var hit in Physics.RaycastAll(from,delta.normalized,Mathf.Max(0,delta.magnitude-0.15f),~0,QueryTriggerInteraction.Ignore))
            {
                if(hit.collider.GetComponentInParent<ResidentActor>() || hit.collider.GetComponentInParent<StaticNPC>())continue;
                return false;
            }
            return true;
        }
        private static float Horizontal(Vector3 point){point.y=0;return point.magnitude;}
        private static bool RaisedSurface(Vector3 ground)
        {
            foreach(var hit in Physics.RaycastAll(ground+Vector3.up*0.3f,Vector3.down,0.8f,~0,QueryTriggerInteraction.Ignore))
                if(hit.normal.y>0.7f && !hit.collider.GetComponent<TerrainCollider>() &&
                    !hit.collider.GetComponentInParent<StaticNPC>() && !hit.collider.GetComponentInParent<ResidentActor>())return true;
            return false;
        }
        private static string Stable(Vector3 p)
        {return p.x.ToString("F2",CultureInfo.InvariantCulture)+","+p.z.ToString("F2",CultureInfo.InvariantCulture);}
    }
}
