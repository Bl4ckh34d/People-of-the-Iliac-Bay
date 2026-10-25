using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Utility.AssetInjection;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in acceptance only. All NPC movement belongs to the ordinary saved
    // action/task overlay; this probe never creates or repositions an NPC.
    internal static class NativeInteriorModuleProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const int SeatSeconds=120;

        internal static IEnumerator Run()
        {
            var game=GameManager.Instance;
            bool flag=Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="interior-module" ||
                Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-interior-module-probe")>=0;
            bool modulesOnly=Environment.GetEnvironmentVariable("POTIB_NAVMESH_MODULES_ONLY")=="1" ||
                Environment.GetEnvironmentVariable("POTIB_NAVMESH_TEMPLATES_ONLY")=="1";
            if(!flag || !modulesOnly || !game || !game.PlayerEnterExit || !game.StreamingWorld || !game.PlayerGPS || game.PlayerEntity==null ||
                !game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal))
                throw new InvalidOperationException("Interior module probe requires its owned fixture and modules-only loader flag.");
            var source=game.PlayerEnterExit.Interior;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            DFBlock sourceBlock;
            if(!game.PlayerEnterExit.IsPlayerInsideBuilding || !directory ||
                !NativeInteriorNavigationModules.TryGetBlock(source,out sourceBlock))
                throw new InvalidOperationException("Interior module probe must begin inside an actual captured native interior.");
            int map=directory.MapID,sourceKey=source.EntryDoor.buildingKey;
            string identity=NativeInteriorNavigationModules.InteriorKey(sourceBlock,source.EntryDoor.recordIndex);
            yield return Ready(source,map);
            Describe(source,map,identity,"source");

            DaggerfallStaticDoors doorOwner;StaticDoor target;
            FindSecond(directory,source,identity,out doorOwner,out target);
            game.PlayerEnterExit.TransitionExterior();
            float deadline=Time.realtimeSinceStartup+20;
            while(game.PlayerEnterExit.IsPlayerInside && Time.realtimeSinceStartup<deadline)yield return null;
            if(game.PlayerEnterExit.IsPlayerInside || !doorOwner)
                throw new InvalidOperationException("Interior module probe could not return to its native exterior door layout.");
            TownSmokeHarness.EnterInterior(doorOwner.transform,target);
            DaggerfallInterior room=null;
            deadline=Time.realtimeSinceStartup+25;
            while(Time.realtimeSinceStartup<deadline)
            {
                room=game.PlayerEnterExit.Interior;
                if(game.PlayerEnterExit.IsPlayerInsideBuilding && room && room.EntryDoor.buildingKey==target.buildingKey)break;
                yield return null;
            }
            DFBlock actual;
            if(!room || room.EntryDoor.buildingKey==sourceKey || room.EntryDoor.buildingKey!=target.buildingKey ||
                !NativeInteriorNavigationModules.TryGetBlock(room,out actual) ||
                NativeInteriorNavigationModules.InteriorKey(actual,room.EntryDoor.recordIndex)!=identity)
                throw new InvalidOperationException("Second native interior did not preserve the captured model identity.");
            DaggerfallUI.Instance.PopToHUD();
            yield return Ready(room,map);
            Describe(room,map,identity,"second");

            var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            InteriorGeometry geometry=null;ShopVisitGeometry activities=null;WorldStateStore store=null;
            deadline=Time.realtimeSinceStartup+60;
            while(Time.realtimeSinceStartup<deadline)
            {
                if(streets && streets.Ready && Field<DaggerfallInterior>(streets,"interior")==room)
                {
                    geometry=Field<InteriorGeometry>(streets,"indoorGeometry");
                    activities=Field<ShopVisitGeometry>(streets,"shopGeometry");
                    store=Field<WorldStateStore>(streets,"worldStore");
                    if(geometry!=null && geometry.IsComplete && geometry.EntryGround.HasValue && store!=null)break;
                }
                yield return null;
            }
            if(geometry==null || !geometry.IsComplete || !geometry.EntryGround.HasValue || store==null)
                throw new InvalidOperationException("Second interior's ordinary population and existing room survey did not become ready.");
            var ids=OrdinaryIds(store,map);
            ResidentActor actor=null;
            deadline=Time.realtimeSinceStartup+60;
            while(Time.realtimeSinceStartup<deadline && !actor)
            {
                var actors=Field<Dictionary<string,ResidentActor>>(streets,"actors");
                if(actors!=null)foreach(var candidate in actors.Values)
                {
                    if(!candidate || !candidate.gameObject.activeInHierarchy || !ids.Contains(candidate.Resident.Id) ||
                        candidate.IsSleeping || candidate.IsSeated || candidate.IsConversing ||
                        candidate.Resident.IsQuestProtected || candidate.Resident.NativeIdentity!=null ||
                        store.ActiveNpcAction(candidate.Resident.Id)!=null || store.ActiveNpcTask(candidate.Resident.Id)!=null)continue;
                    NavMeshHit onFloor;
                    if(!NavMesh.SamplePosition(candidate.transform.position-Vector3.up,out onFloor,0.4f,NavMesh.AllAreas) ||
                        Vector3.Distance(candidate.transform.position-Vector3.up,onFloor.position)>0.4f)continue;
                    actor=candidate;break;
                }
                if(!actor)yield return null;
            }
            if(!actor)throw new InvalidOperationException("Second native room has no live ordinary standing resident; populate the fixture through normal routines.");

            var observerBefore=game.PlayerMotor.transform.position;
            string activeTask=null;
            try
            {
                TownSmokeHarness.Watch(actor.transform);
                var start=actor.transform.position-Vector3.up;
                bool stairs=HasUpperFloor(room,geometry);
                var destination=FindDestination(room,geometry,start,stairs);
                PlaceObserver(room,geometry,start,destination);
                var first=MoveOption(room,map,destination);
                var second=MoveOption(room,map,start);
                var decisions=new List<NpcActionDecision> {
                    new NpcActionDecision {Option=first,DurationSeconds=300},
                    new NpcActionDecision {Option=second,DurationSeconds=300}};
                activeTask=Accept(streets,store,actor,decisions,"Walk to the surveyed room point, then return to this exact point.");
                yield return WalkTask(streets,store,actor,room,activeTask,start,destination,stairs);
                activeTask=null;

                var catalog=activities==null?null:activities.Seating;
                if(!catalog)catalog=room.GetComponent<FurnitureSeatCatalog>();
                int seats=catalog?catalog.Slots.Count:0;
                bool seated=false;
                if(seats>0)
                {
                    NpcActionOption offered=null;FurnitureSeatSlot slot=null;
                    deadline=Time.realtimeSinceStartup+20;
                    while(Time.realtimeSinceStartup<deadline)
                    {
                        offered=streets.ConversationActionOptions(actor.Resident.Id,"Take a seat for 120 seconds.")
                            .Find(option=>option.Kind==NpcActionKind.TakeSeat);
                        slot=offered==null?null:catalog.Find(offered.Target.ContainerId);
                        NavMeshPath route;
                        if(slot!=null && NativeSeatNavigation.TryApproach(catalog,slot,actor.transform.position-Vector3.up,out route))break;
                        offered=null;yield return null;
                    }
                    if(offered==null || slot==null)
                        throw new InvalidOperationException("Real furniture exists, but its ordinary seat adapter offered no accessible free seat on the reused module.");
                    start=actor.transform.position-Vector3.up;
                    PlaceObserver(room,geometry,start,slot.Approach,slot.Ground);
                    decisions=new List<NpcActionDecision> {
                        new NpcActionDecision {Option=offered,DurationSeconds=SeatSeconds},
                        new NpcActionDecision {Option=MoveOption(room,map,start),DurationSeconds=300}};
                    activeTask=Accept(streets,store,actor,decisions,"Take the offered native seat for 120 seconds, then stand and return here.");
                    yield return SeatTask(store,actor,catalog,slot,activeTask,start);
                    activeTask=null;seated=true;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_MODULE_PASS: sourceBuilding="+sourceKey+
                    "; secondBuilding="+room.EntryDoor.buildingKey+"; identity="+identity+
                    "; ordinaryResident="+actor.Resident.Id+"; walkedBothWays=true; stairs="+(stairs?"walked":"not-applicable")+
                    "; actualSeats="+seats+"; seat="+(seated?"occupied-and-left":"not-applicable")+
                    "; exactBypassed=true; runtimeBake=false; npcCreated=false; npcRepositioned=false");
            }
            finally
            {
                TownSmokeHarness.Watch(null);
                if(activeTask!=null && actor)
                {string ignored;streets.TryStopConversationAction(actor.Resident.Id,out ignored);}
                game.PlayerMotor.transform.position=observerBefore;Physics.SyncTransforms();
            }
        }

        private static T Field<T>(object value,string name) where T:class
        {
            var field=value.GetType().GetField(name,Private);
            if(field==null)throw new MissingFieldException(value.GetType().FullName,name);
            return field.GetValue(value) as T;
        }

        private static IEnumerator Ready(DaggerfallInterior room,int map)
        {
            string scene="interior-"+map+"-"+room.EntryDoor.buildingKey;
            float deadline=Time.realtimeSinceStartup+15;
            while(Time.realtimeSinceStartup<deadline)
            {
                var world=NativeNavigationWorld.Current;
                if(world && world.Ready && world.Scene==scene && world.ModuleCount==1)yield break;
                yield return null;
            }
            throw new InvalidOperationException("Reusable native interior module did not load: "+scene);
        }

        private static void Describe(DaggerfallInterior room,int map,string identity,string phase)
        {
            var world=NativeNavigationWorld.Current;
            var manifest=Field<NativeNavigationManifest>(world,"manifest");
            List<NativeNavigationModule> selected;string reason;
            if(!NativeInteriorNavigationModules.TrySelect(room.transform,world.Scene,manifest,out selected,out reason) || selected.Count!=1)
                throw new InvalidOperationException("Loaded room has no exact hashed interior module: "+reason);
            var mod=Field<Mod>(world,"mod");
            if(mod==null || !mod.GetAsset<NavMeshData>(selected[0].asset))
                throw new InvalidOperationException("Hashed native interior module has no installed baked asset.");
            string scene="interior-"+map+"-"+room.EntryDoor.buildingKey;
            bool exact=manifest.modules.Exists(module=>module!=null && module.scene==scene && module.variant==NativeNavigationWorld.Variant());
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_MODULE_MODE: phase="+phase+"; scene="+scene+
                "; mode=modular; exactAvailable="+exact+"; exactBypassed=true; identity="+identity+
                "; asset="+selected[0].asset+"; modules="+world.ModuleCount+"; runtimeBake=false");
        }

        private static void FindSecond(BuildingDirectory directory,DaggerfallInterior source,string identity,
            out DaggerfallStaticDoors selectedOwner,out StaticDoor selected)
        {
            selectedOwner=null;selected=default(StaticDoor);
            var location=directory.LocationData;
            WorldDataVariants.SetLastLocationKeyTo(location.RegionIndex,location.LocationIndex);
            var blocks=RMBLayout.GetLocationBuildingData(location);
            int width=location.Exterior.ExteriorData.Width;
            string requested=Environment.GetEnvironmentVariable("POTIB_NAVMESH_INTERIOR_MODULE_TARGET");
            int explicitKey;bool explicitTarget=int.TryParse(requested,out explicitKey);
            if(!string.IsNullOrEmpty(requested) && !explicitTarget)
                throw new InvalidOperationException("Interior module target must be a native numeric building key.");
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>(true))
                foreach(var door in owner.Doors??new StaticDoor[0])
                {
                    if(door.doorType!=DoorTypes.Building || door.buildingKey==source.EntryDoor.buildingKey ||
                        explicitTarget && door.buildingKey!=explicitKey)continue;
                    int x,z,record;BuildingDirectory.ReverseBuildingKey(door.buildingKey,out x,out z,out record);
                    int index=z*width+x;BuildingSummary summary;
                    if(x<0 || x>=width || z<0 || index<0 || blocks==null || index>=blocks.Length ||
                        record!=door.recordIndex || blocks[index].Index!=door.blockIndex ||
                        !directory.GetBuildingSummary(door.buildingKey,out summary) ||
                        summary.BuildingType!=source.BuildingData.BuildingType ||
                        NativeInteriorNavigationModules.InteriorKey(blocks[index],record)!=identity)continue;
                    if(!selectedOwner || door.buildingKey<selected.buildingKey){selectedOwner=owner;selected=door;}
                }
            if(!selectedOwner)
                throw new InvalidOperationException("No second native building in this loaded location matches the source interior model identity: "+identity);
        }

        private static HashSet<string> OrdinaryIds(WorldStateStore store,int map)
        {
            var result=new HashSet<string>(StringComparer.Ordinal);
            foreach(var settlement in store.Snapshot().Settlements)
                if(settlement.SettlementId==map)foreach(var household in settlement.Households)
                    foreach(var resident in household.Residents)
                        if(resident.IsAlive && !resident.IsQuestProtected && resident.NativeIdentity==null &&
                            resident.AgeGroup==ResidentAgeGroup.Adult)result.Add(resident.Id);
            return result;
        }

        private static bool HasUpperFloor(DaggerfallInterior room,InteriorGeometry geometry)
        {
            float entry=geometry.EntryGround.Value.y;
            foreach(var anchor in geometry.LocalAnchors)
                if(Math.Abs(room.transform.TransformPoint(anchor).y-entry)>=1.5f)return true;
            DFBlock block;
            if(!NativeInteriorNavigationModules.TryGetBlock(room,out block))
                throw new InvalidOperationException("Actual native block vanished before stair applicability check.");
            foreach(var bed in BedGeometry.Read(room,block))
                if(room.transform.TransformPoint(bed.LocalCenter).y-entry>=1.5f)return true;
            return false;
        }

        private static Vector3 FindDestination(DaggerfallInterior room,InteriorGeometry geometry,Vector3 start,bool stairs)
        {
            NavMeshHit from;
            if(!NavMesh.SamplePosition(start,out from,0.4f,NavMesh.AllAreas))throw new InvalidOperationException("Ordinary resident starts off the reused interior NavMesh.");
            foreach(var anchor in geometry.LocalAnchors)
            {
                var point=room.transform.TransformPoint(anchor);
                if(Vector3.Distance(start,point)<5.5f || stairs && Math.Abs(point.y-start.y)<1.5f)continue;
                var route=geometry.RouteBetween(room.transform.InverseTransformPoint(start),anchor);
                NavMeshHit to;var native=new NavMeshPath();
                if(route.Count<2 || !NavMesh.SamplePosition(point,out to,0.4f,NavMesh.AllAreas) ||
                    Vector3.Distance(point,to.position)>0.4f ||
                    !NavMesh.CalculatePath(from.position,to.position,NavMesh.AllAreas,native) || native.status!=NavMeshPathStatus.PathComplete)continue;
                return point;
            }
            throw new InvalidOperationException(stairs?"Actual upper floor exists, but no surveyed stair destination is connected on the reused module.":
                "Reused native room has no complete ordinary five-metre walking destination.");
        }

        private static void PlaceObserver(DaggerfallInterior room,InteriorGeometry geometry,Vector3 actor,
            Vector3 destination,Vector3? seatGround=null)
        {
            var route=geometry.RouteBetween(room.transform.InverseTransformPoint(actor),room.transform.InverseTransformPoint(destination));
            foreach(var local in geometry.LocalAnchors)
            {
                var point=room.transform.TransformPoint(local);float distance=Vector3.Distance(point,actor);
                if(distance<2 || distance>6 || Math.Abs(point.y-actor.y)>0.35f ||
                    Vector3.Distance(point,destination)<=1.5f || seatGround.HasValue && Vector3.Distance(point,seatGround.Value)<=1.6f)continue;
                bool onRoute=false;
                for(int i=1;i<route.Count;i++)
                {
                    var a=room.transform.TransformPoint(new Vector3((float)route[i-1].X,(float)route[i-1].Y,(float)route[i-1].Z));
                    var b=room.transform.TransformPoint(new Vector3((float)route[i].X,(float)route[i].Y,(float)route[i].Z));
                    var segment=b-a;
                    float progress=segment.sqrMagnitude<0.000001f?0:Mathf.Clamp01(Vector3.Dot(point-a,segment)/segment.sqrMagnitude);
                    if(Vector3.Distance(point,a+segment*progress)<=1){onRoute=true;break;}
                }
                if(onRoute)continue;
                GameManager.Instance.PlayerMotor.transform.position=point+Vector3.up;
                Physics.SyncTransforms();return;
            }
            throw new InvalidOperationException("No surveyed nearby observer point is clear of both route legs and the selected chair approach/egress.");
        }

        private static NpcActionOption MoveOption(DaggerfallInterior room,int map,Vector3 point)
        {
            var local=room.transform.InverseTransformPoint(point);
            return new NpcActionOption {Id=1,Kind=NpcActionKind.MoveTo,Label="walk to this surveyed native interior point",
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=map,
                    BuildingKey=room.EntryDoor.buildingKey,X=local.x,Y=local.y,Z=local.z}};
        }

        private static string Accept(StreetPopulation streets,WorldStateStore store,ResidentActor actor,
            List<NpcActionDecision> decisions,string text)
        {
            string reason;
            if(!NpcProbeCommandSource.TryAcceptTask(streets,actor.Resident.Id,"native-interior-module:"+Guid.NewGuid().ToString("N"),decisions,text,out reason))
                throw new InvalidOperationException("Ordinary interior module task admission failed: "+reason);
            var task=store.ActiveNpcTask(actor.Resident.Id);
            if(task==null || task.Steps.Count!=2)throw new InvalidOperationException("Interior module task was not saved through the ordinary task adapter.");
            return task.Id;
        }

        private static IEnumerator WalkTask(StreetPopulation streets,WorldStateStore store,ResidentActor actor,
            DaggerfallInterior room,string id,Vector3 start,Vector3 destination,bool stairs)
        {
            var previous=actor.transform.position-Vector3.up;
            float outward=0,returned=0,min=previous.y,max=previous.y;
            var navigation=actor.GetComponent<NativeResidentNavigation>();float nativeBefore=navigation?navigation.DistanceWalked:0;
            bool reached=false,roomFollower=false;
            float deadline=Time.realtimeSinceStartup+150;
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return null;
                RequireActor(actor);
                var point=actor.transform.position-Vector3.up;
                float step=Vector3.Distance(previous,point);
                if(step>0.75f)throw new InvalidOperationException("Ordinary module resident jumped rather than walking: "+step);
                CheckBody(room,previous,point);previous=point;min=Math.Min(min,point.y);max=Math.Max(max,point.y);
                if(reached)returned+=step;else outward+=step;
                var first=store.ReadNpcAction(id+":step:0");
                var task=store.ReadNpcTask(id);
                RequireTask(task);
                if(!reached && first!=null && first.Phase==NpcActionPhase.Completed)
                {
                    if(Vector3.Distance(point,destination)>0.4f)throw new InvalidOperationException("Ordinary task completed away from its surveyed module destination.");
                    reached=true;
                }
                var motions=Field<IDictionary>(streets,"actionMotions");
                if(motions!=null && motions.Contains(actor.Resident.Id))
                {
                    var motion=motions[actor.Resident.Id];
                    var indoor=motion.GetType().GetField("Indoor",BindingFlags.Instance|BindingFlags.Public);
                    if(indoor!=null && indoor.GetValue(motion) is InteriorRoomFollower)roomFollower=true;
                }
                if(task.Phase!=NpcTaskPhase.Completed)continue;
                navigation=actor.GetComponent<NativeResidentNavigation>();
                float nativeMetres=navigation?navigation.DistanceWalked-nativeBefore:0;
                if(!reached || outward<5 || returned<5 || nativeMetres<2 || !roomFollower ||
                    Vector3.Distance(point,start)>0.35f || stairs && max-min<1.3f)
                    throw new InvalidOperationException("Ordinary reused-module walking evidence incomplete: out="+outward+
                        "; back="+returned+"; native="+nativeMetres+"; height="+(max-min)+"; roomFollower="+roomFollower);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_MODULE_WALK_PASS: resident="+actor.Resident.Id+
                    "; outwardMetres="+outward+"; returnMetres="+returned+"; nativeMetres="+nativeMetres+
                    "; verticalMetres="+(max-min)+"; roomFollower=true; stairsRequired="+stairs);
                yield break;
            }
            throw new InvalidOperationException("Ordinary reused-module resident did not complete both walking legs within 150 seconds.");
        }

        private static IEnumerator SeatTask(WorldStateStore store,ResidentActor actor,FurnitureSeatCatalog catalog,
            FurnitureSeatSlot seat,string id,Vector3 start)
        {
            float deadline=Time.realtimeSinceStartup+150;double seatedAt=-1,lastSeated=-1;
            var previous=actor.transform.position-Vector3.up;bool stood=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return null;RequireActor(actor);
                var point=actor.transform.position-Vector3.up;
                if(Vector3.Distance(previous,point)>0.75f)throw new InvalidOperationException("Ordinary seat transition jumped on the reused module.");
                previous=point;
                var task=store.ReadNpcTask(id);RequireTask(task);
                var first=store.ReadNpcAction(id+":step:0");
                var second=store.ReadNpcAction(id+":step:1");
                if(actor.IsSeated)
                {
                    if(!seat.Furniture.Contains(point,0.05f) || !catalog.Owns(actor.Resident.Id,seat.Id,Time.unscaledTime))
                        throw new InvalidOperationException("Seated ordinary actor did not occupy its reserved real furniture.");
                    double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    if(seatedAt<0)seatedAt=now;lastSeated=now;
                }
                if(second!=null)
                {
                    if(first==null || first.Phase!=NpcActionPhase.Completed || first.UpdatedAt<first.FinishAt ||
                        actor.IsSeated || catalog.Owns(actor.Resident.Id,seat.Id,Time.unscaledTime) || seat.Furniture.Contains(point,0.02f))
                        throw new InvalidOperationException("Seat task advanced before actual timed occupancy and physical standing completed.");
                    stood=true;
                }
                if(task.Phase!=NpcTaskPhase.Completed)continue;
                if(!stood || seatedAt<0 || lastSeated-seatedAt<5 || Vector3.Distance(point,start)>0.35f)
                    throw new InvalidOperationException("Ordinary reused-module seat evidence lacks sustained sitting, standing or physical return.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_MODULE_SEAT_PASS: resident="+actor.Resident.Id+
                    "; seat="+seat.Id+"; seatedGameSeconds="+(lastSeated-seatedAt)+"; physicalStand=true; claimReleased=true; returned=true");
                yield break;
            }
            throw new InvalidOperationException("Ordinary reused-module seat task did not complete within 150 seconds.");
        }

        private static void RequireActor(ResidentActor actor)
        {if(!actor || !actor.gameObject.activeInHierarchy || !actor.Resident.IsAlive)
            throw new InvalidOperationException("Ordinary resident disappeared during reusable interior acceptance.");}
        private static void RequireTask(NpcTaskPlan task)
        {if(task==null || task.Phase==NpcTaskPhase.Failed || task.Phase==NpcTaskPhase.Cancelled)
            throw new InvalidOperationException("Ordinary interior module task failed: "+(task==null?"missing":task.FailureReason));}
        private static void CheckBody(DaggerfallInterior room,Vector3 from,Vector3 to)
        {
            foreach(var collider in Physics.OverlapCapsule(to+Vector3.up*0.76f,to+Vector3.up*1.65f,
                CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Ignore))
                if(collider && collider.transform.IsChildOf(room.transform) && !NativeNavigationGeometry.IsDynamicNavigationBody(collider))
                    throw new InvalidOperationException("Ordinary module resident entered static room geometry: "+collider.name);
            var delta=to-from;if(delta.sqrMagnitude<0.000001f)return;
            foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*0.76f,from+Vector3.up*1.65f,CrowdMotion.BodyRadius,
                delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(hit.collider && hit.collider.transform.IsChildOf(room.transform) && !NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider))
                    throw new InvalidOperationException("Ordinary module resident crossed a static room wall: "+hit.collider.name);
        }
    }
}
