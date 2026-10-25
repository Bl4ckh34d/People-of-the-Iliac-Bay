using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Room identity comes from original bed records, not current door animation or actor positions.
    public static class TavernRoomSurvey
    {
        private static bool remoteSurveyActive;
        public sealed class Result
        {
            public List<LodgingRoom> Rooms;
            public Dictionary<int,string> Markers,Occupants;
            public Dictionary<int,string> BedroomOccupants;
            public int? KeeperHash;
            public List<GuardPoint3> ReceptionRoute;
            // Geometry draft only. RegisterInventory binds native identity,
            // destination region, menu and hours before sealing/publishing.
            public NpcInnServiceCatalog ServiceCatalog;
        }
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
        { Dictionary<int,string> occupants; return Read(interior,out markerRooms,out occupants); }
        public static List<LodgingRoom> Read(DaggerfallInterior interior,out Dictionary<int,string> markerRooms,out Dictionary<int,string> occupants)
        {Dictionary<int,string> bedrooms;return Read(interior,out markerRooms,out occupants,out bedrooms);}
        public static List<LodgingRoom> Read(DaggerfallInterior interior,out Dictionary<int,string> markerRooms,out Dictionary<int,string> occupants,
            out Dictionary<int,string> bedrooms)
        {
            var markers=interior.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest);
            var barriers=ReadDoorBoundaries(interior);
            return Partition(interior,markers,barriers,out markerRooms,out occupants,out bedrooms);
        }
        // Door animation must not turn a private room into a public activity area.
        public static Func<Vector3,Vector3,bool> PublicAreaBoundary(DaggerfallInterior interior)
        {
            var barriers=ReadDoorBoundaries(interior);
            return (from,to)=>!barriers.Exists(door=>door.Blocks(from,to));
        }
        private static List<DoorBoundary> ReadDoorBoundaries(DaggerfallInterior interior)
        {
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
            return barriers;
        }
        // Model-only layout plus passive record data: no NPC, quest or save registration.
        public static List<LodgingRoom> ReadUnloaded(Transform owner,StaticDoor door,out Dictionary<int,string> markerRooms)
        { Dictionary<int,string> occupants; return ReadUnloaded(owner,door,out markerRooms,out occupants); }
        public static List<LodgingRoom> ReadUnloaded(Transform owner,StaticDoor door,out Dictionary<int,string> markerRooms,out Dictionary<int,string> occupants)
        {
            var result=new Result();
            var operation=ReadUnloadedSteps(owner,door,result);
            try { while(operation.MoveNext()) { } }
            finally { (operation as IDisposable)?.Dispose(); }
            markerRooms=result.Markers; occupants=result.Occupants; return result.Rooms;
        }
        public static System.Collections.IEnumerator ReadUnloadedSteps(Transform owner,StaticDoor door,Result result)
        {
            return ReadUnloadedSteps(owner,door,result,ClimateSwaps.FromAPIClimateBase(GameManager.Instance.PlayerGPS.ClimateSettings.ClimateType));
        }
        public static System.Collections.IEnumerator ReadServiceSteps(Transform owner,StaticDoor door,
            DFLocation.ClimateBaseType climate,Result result,Func<bool> stillValid,bool bank=false)
        {
            if(!owner || result==null || stillValid==null)throw new ArgumentException("Missing unloaded Inn service source.");
            result.Rooms=null;result.Markers=null;result.Occupants=null;result.BedroomOccupants=null;
            result.KeeperHash=null;result.ReceptionRoute=null;result.ServiceCatalog=null;
            while(stillValid() && (remoteSurveyActive || !BackgroundWork.TryStart()))yield return null;
            if(!stillValid())yield break;
            remoteSurveyActive=true;System.Collections.IEnumerator operation=null;
            try
            {
                var pending=new Result();
                operation=ReadUnloadedSteps(owner,door,pending,ClimateSwaps.FromAPIClimateBase(climate),null,true,!bank,bank);
                while(owner && stillValid() && operation.MoveNext())yield return null;
                if(owner && stillValid() && pending.Rooms!=null)CopyResult(pending,result);
            }
            finally{try{(operation as IDisposable)?.Dispose();}finally{remoteSurveyActive=false;}}
        }
        private static void CopyResult(Result source,Result target)
        {
            target.Rooms=source.Rooms;target.Markers=source.Markers;target.Occupants=source.Occupants;
            target.BedroomOccupants=source.BedroomOccupants;target.KeeperHash=source.KeeperHash;
            target.ReceptionRoute=source.ReceptionRoute;target.ServiceCatalog=source.ServiceCatalog;
        }
        // Original records supply the entrance; no loaded settlement owner is needed.
        // Publish atomically and dispose hidden geometry on cancellation or failure.
        public static System.Collections.IEnumerator ReadRemoteSteps(UnloadedSettlementSurvey.Entrance entrance,
            DFLocation.ClimateBaseType climate,Result result,Func<bool> stillValid)
        {
            if(entrance==null || result==null || stillValid==null) throw new ArgumentNullException();
            result.Rooms=null; result.Markers=null; result.Occupants=null;result.KeeperHash=null;result.ReceptionRoute=null;result.ServiceCatalog=null;
            while(stillValid() && (remoteSurveyActive || !BackgroundWork.TryStart())) yield return null;
            if(!stillValid()) yield break;
            var owner=new GameObject("People of the Iliac Bay remote inn owner");
            remoteSurveyActive=true;
            System.Collections.IEnumerator operation=null;
            try
            {
                var door=entrance.NativeDoor; door.ownerPosition=new Vector3(0,1000,0);
                owner.transform.position=door.ownerPosition;
                var pending=new Result();
                operation=ReadUnloadedSteps(owner.transform,door,pending,ClimateSwaps.FromAPIClimateBase(climate),entrance.SourceBlock,true,true);
                while(stillValid() && operation.MoveNext()) yield return null;
                if(stillValid() && pending.Rooms!=null)
                    CopyResult(pending,result);
            }
            finally
            {
                try { (operation as IDisposable)?.Dispose(); }
                finally { owner.SetActive(false); UnityEngine.Object.Destroy(owner); remoteSurveyActive=false; }
            }
        }
        private static void LayoutRemoteModels(DaggerfallInterior interior,Transform owner,StaticDoor door,ClimateBases climate,DFBlock block)
        {
            if(block.Type!=DFBlock.BlockTypes.Rmb || block.Index!=door.blockIndex || block.RmbBlock.SubRecords==null ||
                door.recordIndex<0 || door.recordIndex>=block.RmbBlock.SubRecords.Length)
                throw new ArgumentException("Remote inn source block does not match entrance.");
            // DoLayoutAutomap privately resolves blocks from PlayerGPS. Supply
            // the destination records to the same model-only builder instead.
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var type=typeof(DaggerfallInterior);
            Action<string,object> set=(name,value)=>{
                var field=type.GetField(name,flags);
                if(field==null) throw new MissingFieldException(type.FullName,name);
                field.SetValue(interior,value);
            };
            set("dfUnity",DaggerfallUnity.Instance);set("climateBase",climate);set("entryDoor",door);set("doorOwner",owner);
            set("blockData",block);set("recordData",block.RmbBlock.SubRecords[door.recordIndex]);
            var models=type.GetMethod("AddModels",flags);
            if(models==null) throw new MissingMethodException(type.FullName,"AddModels");
            models.Invoke(interior,new object[]{new PlayerGPS.DiscoveredBuilding {buildingType=DFLocation.BuildingTypes.AllValid},true});
        }
        private static System.Collections.IEnumerator ReadUnloadedSteps(Transform owner,StaticDoor door,Result result,ClimateBases climate,DFBlock? sourceBlock=null,bool reception=false,bool service=false,bool bank=false)
        {
            if(!owner || door.doorType!=DoorTypes.Building) throw new ArgumentException("Missing tavern entrance.");
            var temporary=new GameObject("People of the Iliac Bay isolated tavern survey");
            var dfu=DaggerfallUnity.Instance;
            System.Collections.IEnumerator operation=null;
            try
            {
                var shifted=door; shifted.ownerPosition+=Vector3.up*100;
                var interior=temporary.AddComponent<DaggerfallInterior>();
                if(sourceBlock.HasValue) LayoutRemoteModels(interior,owner,shifted,climate,sourceBlock.Value);
                else if(!interior.DoLayoutAutomap(owner,shifted,climate))
                    throw new InvalidOperationException("Tavern model survey failed.");
                temporary.transform.position=door.ownerPosition+(Vector3)door.buildingMatrix.GetColumn(3)+Vector3.up*100;
                temporary.transform.rotation=GameObjectHelper.QuaternionFromMatrix(door.buildingMatrix);
                var block=sourceBlock??dfu.ContentReader.BlockFileReader.GetBlock(door.blockIndex);
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
                if(!bank)
                {
                    FurnitureSeatCatalog.ForSurvey(interior,block);
                    operation=PartitionSteps(interior,markers.ToArray(),barriers,result,block);
                    while(operation.MoveNext()) yield return null;
                }
                else result.Rooms=new List<LodgingRoom>();
                if(reception)
                {
                    operation=ReadReceptionSteps(interior,block,result,barriers,climate,service,bank);
                    while(operation.MoveNext()) yield return null;
                }
            }
            finally
            {
                try{(operation as IDisposable)?.Dispose();}
                finally{temporary.SetActive(false);UnityEngine.Object.Destroy(temporary);Physics.SyncTransforms();}
            }
        }
        private static System.Collections.IEnumerator ReadReceptionSteps(DaggerfallInterior interior,DFBlock block,Result result,
            List<DoorBoundary> barriers,ClimateBases climate,bool service,bool bank=false)
        {
            // Passive records identify the keeper without spawning a native NPC.
            var geometry=new InteriorGeometry(interior,deferSurvey:true);
            while(geometry.AdvanceSurvey()) yield return null;
            if(!geometry.IsComplete || !geometry.EntryGround.HasValue) yield break;
            foreach(var person in block.RmbBlock.SubRecords[interior.EntryDoor.recordIndex].Interior.BlockPeopleRecords)
            {
                FactionFile.FactionData faction;
                if(!GameManager.Instance.PlayerEntity.FactionData.GetFactionData(person.FactionID,out faction) ||
                    faction.sgroup!=(int)FactionFile.SocialGroups.Merchants) continue;
                if(service && !NativeBusinessServices.OffersKeeperService(person.FactionID,DFLocation.BuildingTypes.Tavern))continue;
                if(bank && !NativeBusinessServices.OffersBankService(person.FactionID))continue;
                var target=new Vector3(person.XPos,-person.YPos,person.ZPos)*MeshReader.GlobalScale;
                Vector3? counter=null;float distance=16;
                foreach(var anchor in bank?geometry.ApproachPoints(target,2.5f,2):geometry.LocalAnchors)
                {
                    float d=(anchor-target).sqrMagnitude;
                    // Reachable customer side of the counter, with personal space.
                    if(bank && (d<0.64f || geometry.RouteBetween(interior.transform.InverseTransformPoint(geometry.EntryGround.Value),anchor).Count<2))continue;
                    if(d<distance && Mathf.Abs(anchor.y-target.y)<2) {counter=anchor;distance=d;}
                }
                if(!counter.HasValue) continue;
                var route=geometry.RouteBetween(interior.transform.InverseTransformPoint(geometry.EntryGround.Value),counter.Value);
                if(route.Count<2) continue;
                result.KeeperHash=StaticNPC.GetPositionHash(person.XPos,person.YPos,person.ZPos);
                result.ReceptionRoute=route;
                if(service && NativeBusinessServices.OffersKeeperService(person.FactionID,DFLocation.BuildingTypes.Tavern))
                {
                    var seating=interior.GetComponent<FurnitureSeatCatalog>();
                    var activities=new ShopVisitGeometry(interior,geometry,target,seating,(a,b)=>!barriers.Exists(door=>door.Blocks(a,b)),block);
                    var draft=new NpcInnServiceCatalog{BuildingKey=interior.EntryDoor.buildingKey,BlockIndex=interior.EntryDoor.blockIndex,
                        RecordIndex=interior.EntryDoor.recordIndex,Climate=(int)climate,KeeperHash=result.KeeperHash.Value};
                    var capture=activities.CaptureInnServiceGeometry(draft,()=>interior && interior.gameObject.activeInHierarchy);
                    try{while(capture.MoveNext())yield return null;}finally{(capture as IDisposable)?.Dispose();}
                    if(draft.Pairs.Count>0)result.ServiceCatalog=draft;
                }
                yield break;
            }
        }
        private static List<LodgingRoom> Partition(DaggerfallInterior interior,Vector3[] markers,List<DoorBoundary> barriers,
            out Dictionary<int,string> markerRooms,out Dictionary<int,string> occupants,out Dictionary<int,string> bedrooms)
        {
            var result=new Result();
            var operation=PartitionSteps(interior,markers,barriers,result);
            while(operation.MoveNext()) { }
            markerRooms=result.Markers; occupants=result.Occupants;bedrooms=result.BedroomOccupants; return result.Rooms;
        }
        private static System.Collections.IEnumerator PartitionSteps(DaggerfallInterior interior,Vector3[] markers,
            List<DoorBoundary> barriers,Result output,DFBlock? sourceBlock=null)
        {
            var markerRooms=new Dictionary<int,string>();
            var occupants=new Dictionary<int,string>();
            var bedrooms=new Dictionary<int,string>();
            var nativePoints=new SortedDictionary<int,Vector3>();
            var entry=interior.EntryDoor;
            var block=sourceBlock??DaggerfallUnity.Instance.ContentReader.BlockFileReader.GetBlock(entry.blockIndex);
            foreach(var person in block.RmbBlock.SubRecords[entry.recordIndex].Interior.BlockPeopleRecords)
                nativePoints.Add(StaticNPC.GetPositionHash(person.XPos,person.YPos,person.ZPos),
                    interior.transform.TransformPoint(new Vector3(person.XPos,-person.YPos,person.ZPos)*MeshReader.GlobalScale));
            foreach(var npc in interior.GetComponentsInChildren<StaticNPC>(true))
                if(npc.Data.context==StaticNPC.Context.Building && npc.Data.buildingKey==entry.buildingKey &&
                    !npc.GetComponent<QuestResourceBehaviour>() && !nativePoints.ContainsKey(npc.Data.hash))
                    nativePoints.Add(npc.Data.hash,npc.transform.position);
            Func<Vector3,Vector3,bool> allowed=(a,b)=>!barriers.Exists(d=>d.Blocks(a,b));
            var beds=BedGeometry.Read(interior,block); beds.Sort((a,b)=>a.RecordIndex.CompareTo(b.RecordIndex));
            var general=new InteriorGeometry(interior,sampleFloorOnly:true);
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
                var region=new InteriorGeometry(interior,0.24f,1.65f,seed,allowed,deferSurvey:true);
                while(region.AdvanceSurvey()) yield return null;
                if(!region.IsComplete) throw new InvalidOperationException("Incomplete lodging room floor survey: "+bed.RecordIndex);
                var room=new LodgingRoom { Id="room:"+bed.RecordIndex };
                foreach(var candidate in beds)
                    if(!assigned.Contains(candidate.RecordIndex) && region.RouteBetween(interior.transform.InverseTransformPoint(seed),
                        interior.transform.InverseTransformPoint(points[candidate.RecordIndex])).Count>=2)
                    { assigned.Add(candidate.RecordIndex); room.Beds.Add("bed:"+candidate.RecordIndex); }
                if(room.Beds.Count==0) throw new InvalidOperationException("Lodging bed disconnected from its own floor.");
                // The entrance-connected common room is not a guest bedroom.
                bool common=general.EntryGround.HasValue && region.ContainsRestMarker(general.EntryGround.Value+Vector3.up*.5f);
                if(!common)
                {
                    int bedIndex=0;
                    foreach(var person in nativePoints)
                    {
                        if(!region.ContainsRestMarker(person.Value+Vector3.up*.5f)) continue;
                        bedrooms.Add(person.Key,room.Id);
                        if(occupants.ContainsKey(person.Key)) throw new InvalidOperationException("Native occupant belongs to multiple bedrooms.");
                        if(bedIndex>=room.Beds.Count)
                        {
                            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_INN_OCCUPANT_NO_BED: building="+entry.buildingKey+"; room="+room.Id+"; hash="+person.Key);
                            continue;
                        }
                        occupants.Add(person.Key,room.Beds[bedIndex++]);
                    }
                }
                for(int index=0;index<markers.Length;index++)
                    if(region.ContainsRestMarker(markers[index]))
                    {
                        if(markerRooms.ContainsKey(index)) throw new InvalidOperationException("Rental marker belongs to multiple bed groups: "+index);
                        markerRooms.Add(index,room.Id);
                    }
                result.Add(room);
                yield return null;
            }
            if(markerRooms.Count!=markers.Length) throw new InvalidOperationException("Unmapped native rental marker; building="+interior.EntryDoor.buildingKey);
            output.Rooms=result; output.Markers=markerRooms; output.Occupants=occupants;output.BedroomOccupants=bedrooms;
        }
    }
}
