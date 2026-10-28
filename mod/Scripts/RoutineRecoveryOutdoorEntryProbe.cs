using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in owned-player fixture. It observes the normal action executor and native door callback.
    public static class RoutineRecoveryOutdoorEntryProbe
    {
        private const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        private const BindingFlags MotionFields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;

        // Loads the owned day fixture, accepts a real MoveTo(player) action, then
        // waits for its ordinary terminal return before the native unrelated-door entry.
        public static IEnumerator Run(StreetPopulation streets,string expectedCharacter)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-routine-recovery-scenechange-probe")>=0 &&
                streets && !string.IsNullOrEmpty(expectedCharacter) && NativeProbeText.IsTestCharacter(expectedCharacter),
                "Scene-change fixture requires its explicit flag and owned test character.");
            var game=GameManager.Instance;game.SaveLoadManager.EnumerateSaves();
            int slot=game.SaveLoadManager.FindSaveFolderByNames(expectedCharacter,"People - Daggerfall day");
            Require(slot>=0,"Owned People - Daggerfall day fixture save missing.");
            bool loaded=false;SaveLoadManager.OnLoadEventHandler onLoad=data=>loaded=true;
            SaveLoadManager.OnLoad+=onLoad;
            try
            {
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
                float deadline=Time.realtimeSinceStartup+120;
                while((!loaded || !streets.Ready || !streets.EntranceSurveyReady) && Time.realtimeSinceStartup<deadline)yield return null;
                Require(loaded && streets.Ready && streets.EntranceSurveyReady && game.PlayerEntity.Name==expectedCharacter &&
                    !game.PlayerEnterExit.IsPlayerInsideBuilding,"Owned exterior fixture did not finish loading.");
            }
            finally {SaveLoadManager.OnLoad-=onLoad;}

            var motor=game.PlayerMotor;var controller=motor.GetComponent<CharacterController>();
            bool motorEnabled=motor.enabled,controllerEnabled=controller && controller.enabled;
            motor.enabled=false;if(controller)controller.enabled=false;
            try
            {
                var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",PrivateInstance).GetValue(streets);
                var actors=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",PrivateInstance).GetValue(streets);
                var geometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",PrivateInstance).GetValue(streets);
                var directory=(BuildingDirectory)typeof(StreetPopulation).GetField("directory",PrivateInstance).GetValue(streets);
                Require(store!=null && geometry!=null && directory!=null,"Scene-change outdoor runtime is unavailable.");
                ResidentActor selected=null;Vector3 observer=Vector3.zero;
                foreach(var candidate in actors.Values)
                {
                    if(!candidate || !candidate.gameObject.activeInHierarchy || candidate.IsSleeping || candidate.IsSeated ||
                        candidate.IsConversing || candidate.Resident==null || candidate.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                        !candidate.Resident.IsAlive || candidate.Resident.IsQuestProtected || store.ActiveNpcTask(candidate.Resident.Id)!=null ||
                        store.ActiveNpcAction(candidate.Resident.Id)!=null)continue;
                    Vector3 point;if(!TryPublicObserverPoint(geometry,directory.transform,candidate.transform.position-Vector3.up,out point))continue;
                    motor.transform.position=point+Vector3.up;Physics.SyncTransforms();yield return null;
                    var options=streets.ConversationActionOptions(candidate.Resident.Id,"Come here.");
                    var move=options.Find(option=>option.Kind==NpcActionKind.MoveTo && option.Target!=null &&
                        option.Target.Kind==NpcActionTargetKind.Person && option.Target.PersonId=="player");
                    string reason;
                    if(move==null || !NpcProbeCommandSource.TryAcceptAction(streets,candidate.Resident.Id,"recovery-scene-probe:"+Guid.NewGuid().ToString("N"),
                        new NpcActionDecision {Option=move,DurationSeconds=300},out reason))continue;
                    selected=candidate;observer=point;break;
                }
                Require(selected,"No nearby resident accepted a normal MoveTo(player) action from a public walkable observer point.");
                var job=store.ActiveNpcAction(selected.Resident.Id);
                Require(job!=null && job.Kind==NpcActionKind.MoveTo && job.Target.Kind==NpcActionTargetKind.Person && job.Target.PersonId=="player",
                    "Fixture action did not use the normal MoveTo(player) capability.");
                string actionId=job.Id;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_SCENE_CHANGE_ACTION: resident="+selected.Resident.Id+
                    "; action="+actionId+"; observer="+observer.ToString("F2"));
                float actionDeadline=Time.realtimeSinceStartup+180;
                while(Time.realtimeSinceStartup<actionDeadline)
                {
                    DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                    job=store.ReadNpcAction(actionId);
                    if(job==null || NpcActionExecutor.IsTerminal(job))break;
                    yield return null;
                }
                float returnDeadline=Time.realtimeSinceStartup+20;
                while(Time.realtimeSinceStartup<returnDeadline &&
                    (job==null || !NpcActionExecutor.IsTerminal(job) || !job.ReturningToRoutine))
                {DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);job=store.ReadNpcAction(actionId);yield return null;}
                Require(job!=null && NpcActionExecutor.IsTerminal(job) && job.ReturningToRoutine,
                    "Normal MoveTo(player) did not complete and enter routine return.");
                DaggerfallStaticDoors owner=null;StaticDoor unrelated=default(StaticDoor);int unrelatedKey=-1;
                var ownPlan=store.GetRoutines(directory.MapID).Residents.Find(value=>value.ResidentId==selected.Resident.Id);
                Require(ownPlan!=null,"Selected resident has no saved routine.");
                foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                {
                    if(doors.Doors==null)continue;
                    foreach(var door in doors.Doors)
                    {
                        BuildingSummary known;
                        bool ownRoutineBuilding=door.buildingKey==selected.Resident.HomeKey;
                        foreach(var stop in ownPlan.Itinerary==null?new List<ResidentVisitStop>():ownPlan.Itinerary.Stops)
                            if(stop.BuildingKey==door.buildingKey)ownRoutineBuilding=true;
                        if(door.doorType!=DoorTypes.Building || ownRoutineBuilding ||
                            !directory.GetBuildingSummary(door.buildingKey,out known))continue;
                        owner=doors;unrelated=door;unrelatedKey=door.buildingKey;break;
                    }
                    if(owner)break;
                }
                Require(owner && unrelatedKey>=0,"Fixture has no known unrelated native building door.");
                yield return Observe(streets,selected.Resident.Id,actionId,expectedCharacter,unrelatedKey,
                    ()=>EnterUnrelated(owner,unrelated));
            }
            finally {if(controller)controller.enabled=controllerEnabled;motor.enabled=motorEnabled;}
        }

        private static IEnumerator EnterUnrelated(DaggerfallStaticDoors owner,StaticDoor door)
        {
            TownSmokeHarness.EnterInterior(owner.transform,door);DaggerfallUI.Instance.PopToHUD();
            float deadline=Time.realtimeSinceStartup+45;
            while(!GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding && Time.realtimeSinceStartup<deadline)yield return null;
            Require(GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding,"Native unrelated door entry timed out.");
        }

        private static bool TryPublicObserverPoint(TownGeometry geometry,Transform origin,Vector3 actor,out Vector3 result)
        {
            result=Vector3.zero;var local=origin.InverseTransformPoint(actor);
            var from=new SavedRoutePoint {X=local.x,Z=local.z};
            // Probe a bounded ring around the already-real outdoor actor. A candidate
            // is accepted only when the town's production route finder verifies it.
            for(int radius=4;radius<=6;radius++)
            for(int direction=0;direction<16;direction++)
            {
                double angle=direction*Math.PI/8;float distance=radius*0.75f;
                var candidate=new Vector3(actor.x+(float)Math.Cos(angle)*distance,actor.y,actor.z+(float)Math.Sin(angle)*distance);
                var destination=origin.InverseTransformPoint(candidate);
                if(geometry.RouteBetweenPoints(from,new SavedRoutePoint {X=destination.x,Z=destination.z}).Count<2)continue;
                result=candidate;return true;
            }
            return false;
        }

        public static IEnumerator Observe(StreetPopulation streets,string residentId,string terminalActionId,
            string expectedCharacter,int unrelatedBuildingKey,Func<IEnumerator> enterUnrelatedViaNativeDoor)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-routine-recovery-scenechange-probe")>=0,
                "Routine recovery scene-change probe requires its explicit Player flag.");
            var game=GameManager.Instance;
            Require(streets && streets.Ready && !game.PlayerEnterExit.IsPlayerInsideBuilding &&
                !string.IsNullOrEmpty(residentId) && !string.IsNullOrEmpty(terminalActionId) &&
                !string.IsNullOrEmpty(expectedCharacter) && NativeProbeText.IsTestCharacter(expectedCharacter) &&
                game.PlayerEntity.Name==expectedCharacter && enterUnrelatedViaNativeDoor!=null,
                "Scene-change probe requires owned player outside and native unrelated-building callback.");

            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",PrivateInstance).GetValue(streets);
            var directory=(BuildingDirectory)typeof(StreetPopulation).GetField("directory",PrivateInstance).GetValue(streets);
            BuildingSummary summary;
            Require(directory && unrelatedBuildingKey>=0 && directory.GetBuildingSummary(unrelatedBuildingKey,out summary),
                "Scene-change target must be a known building in this settlement.");
            Require(!store.HasRoutineRecovery(residentId),"Scene-change probe found an existing recovery plan.");

            var actors=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",PrivateInstance).GetValue(streets);
            var motions=(IDictionary)typeof(StreetPopulation).GetField("actionMotions",PrivateInstance).GetValue(streets);
            var plan=store.GetRoutines(directory.MapID).Residents.Find(value=>value.ResidentId==residentId);
            var resident=store.ResolveResident(directory.MapID,residentId);
            Require(plan!=null && resident!=null,"Scene-change resident has no managed routine.");

            NpcActionJob terminal=null;ResidentActor actor=null;object motion=null;int ownOrigin=-1;
            Vector3 goal=Vector3.zero;float startDeadline=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<startDeadline)
            {
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                terminal=store.ReadNpcAction(terminalActionId);
                actors.TryGetValue(residentId,out actor);
                object candidate;
                if(terminal!=null && NpcActionExecutor.IsTerminal(terminal) && terminal.ReturningToRoutine &&
                    terminal.RoutineRecoveryTransition==null && terminal.LastPosition!=null &&
                    terminal.LastPosition.Kind==NpcActionTargetKind.WorldPoint && actor && actor.gameObject.activeInHierarchy &&
                    motions.Contains(residentId))
                {
                    candidate=motions[residentId];
                    bool selected=(bool)candidate.GetType().GetField("RecoverySelected",MotionFields).GetValue(candidate);
                    ownOrigin=(int)candidate.GetType().GetField("RecoveryOrigin",MotionFields).GetValue(candidate);
                    goal=(Vector3)candidate.GetType().GetField("Goal",MotionFields).GetValue(candidate);
                    var outdoor=candidate.GetType().GetField("Outdoor",MotionFields).GetValue(candidate) as IList;
                    if(selected && ownOrigin>=0 && outdoor!=null && outdoor.Count>=2 &&
                        Vector3.Distance(actor.transform.position-Vector3.up,goal)>0.75f)
                    {motion=candidate;break;}
                }
                yield return null;
            }
            Require(motion!=null,"NPC was not physically returning outdoors toward a selected routine node.");
            Require(ownOrigin!=unrelatedBuildingKey,"Fixture building must differ from NPC's recovery origin.");

            NpcActionJob capturedAction=null;NpcRoutineRecoveryTransition captured=null;int nativeDoor=-1;
            PlayerEnterExit.OnPreTransitionEventHandler observe=args=>
            {
                nativeDoor=args.StaticDoor.buildingKey;
                var pending=store.ReadNpcAction(terminalActionId);
                if(pending!=null && pending.RoutineRecoveryTransition!=null)
                {capturedAction=pending;captured=NpcRoutineRecoveryTransition.Copy(pending.RoutineRecoveryTransition);}
            };
            PlayerEnterExit.OnPreTransition+=observe;
            try {yield return enterUnrelatedViaNativeDoor();}
            finally {PlayerEnterExit.OnPreTransition-=observe;}

            float sceneDeadline=Time.realtimeSinceStartup+45;
            while((!game.PlayerEnterExit.Interior || !streets.Ready) && Time.realtimeSinceStartup<sceneDeadline)yield return null;
            var indoor=game.PlayerEnterExit.Interior;
            Require(indoor && nativeDoor==unrelatedBuildingKey && indoor.EntryDoor.buildingKey==unrelatedBuildingKey,
                "Callback did not enter requested unrelated building through native player door.");
            Require(captured!=null && capturedAction!=null,"Native entry did not persist the recovery doorway handoff.");
            var transit=captured;
            Require(transit.OriginBuildingKey==ownOrigin && transit.OriginBuildingKey!=nativeDoor &&
                transit.Destination.Kind==NpcActionTargetKind.WorldPoint && transit.Destination.SettlementId==directory.MapID &&
                transit.Destination.WorldSpaceId=="settlement:"+directory.MapID.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                transit.Destination.BuildingKey==-1 && transit.SourceRoute.Count>=2 &&
                NpcFollowTransition.SameSpace(transit.Destination,capturedAction.LastPosition) &&
                Distance(transit.SourceRoute[0],capturedAction.LastPosition)<0.01 &&
                Math.Abs(transit.SourceRoute[transit.SourceRoute.Count-1].X-transit.Destination.X)<0.01 &&
                Math.Abs(transit.SourceRoute[transit.SourceRoute.Count-1].Z-transit.Destination.Z)<0.01,
                "Handoff followed player door instead of NPC's fixed outdoor routine node.");
            SavedRoutePoint ownDoor;
            Require(TryRoutineDoor(plan,resident.HomeKey,ownOrigin,out ownDoor) &&
                Math.Abs(ownDoor.X-transit.Destination.X)<0.01 && Math.Abs(ownDoor.Z-transit.Destination.Z)<0.01,
                "Handoff WorldPoint does not match saved route node for OriginBuildingKey.");

            double metres=RouteLength(transit.SourceRoute);
            double seconds=metres*DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed;
            Require(metres>1 && Math.Abs((transit.ArrivesAt-transit.StartedAt)-seconds)<0.001 &&
                transit.ArrivesAt>Now()+0.5 && !store.HasRoutineRecovery(residentId),
                "Handoff time does not match measured source walk, or plan committed before route arrival.");
            RequireNoActor(indoor,residentId);

            float finishDeadline=Time.realtimeSinceStartup+120;RoutineRecoveryPlan saved=null;
            while(Time.realtimeSinceStartup<finishDeadline)
            {
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);RequireNoActor(indoor,residentId);
                if(Now()+0.001<transit.ArrivesAt)
                    Require(!store.HasRoutineRecovery(residentId),"Recovery plan committed before measured route arrival.");
                if(store.HasRoutineRecovery(residentId))
                {saved=store.Snapshot().RoutineRecoveries.Find(value=>value.ResidentId==residentId);if(saved!=null)break;}
                yield return null;
            }
            Require(saved!=null && saved.CreatedAt+0.001>=transit.ArrivesAt && saved.OriginOutside &&
                saved.OriginBuildingKey==transit.OriginBuildingKey,
                "Offscreen recovery did not commit after arrival at NPC's own exterior node.");
            var finished=store.ReadNpcAction(terminalActionId);
            Require(finished!=null && finished.RoutineRecoveryTransition==null && !finished.ReturningToRoutine &&
                finished.LastPosition.Kind==NpcActionTargetKind.WorldPoint &&
                Math.Abs(finished.LastPosition.X-transit.Destination.X)<0.01 && Math.Abs(finished.LastPosition.Z-transit.Destination.Z)<0.01,
                "Terminal return ownership did not settle at NPC's saved outdoor node.");
            RequireNoActor(indoor,residentId);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_SCENE_CHANGE_PASS: branch=outdoor-player-entry"+
                "; resident="+residentId+"; playerDoor="+nativeDoor+"; ownOrigin="+ownOrigin+
                "; routeMetres="+metres.ToString("F2")+"; measuredSeconds="+seconds.ToString("F2")+
                "; commitAfterArrival=true; insidePlayerBuilding=false; originOutside=true");
        }

        private static bool TryRoutineDoor(ResidentRoutine routine,int home,int building,out SavedRoutePoint door)
        {
            door=null;
            if(routine.Itinerary!=null)
            {
                if(building==home)
                {
                    foreach(var stop in routine.Itinerary.Stops)
                        if(stop.Route!=null && stop.Route.Count>0){door=stop.Route[0];return true;}
                    return false;
                }
                foreach(var stop in routine.Itinerary.Stops)
                {
                    if(stop.Route==null || stop.Route.Count==0)continue;
                    if(stop.BuildingKey==building){door=stop.Route[stop.Route.Count-1];return true;}
                }
            }
            else if(routine.Route!=null && routine.Route.Count>0)
            {
                if(building==home)door=routine.Route[0];
                else if(building==routine.DestinationKey)door=routine.Route[routine.Route.Count-1];
            }
            return door!=null;
        }

        private static double RouteLength(IList<NpcActionTarget> route)
        {
            double length=0;
            for(int i=1;i<route.Count;i++)
            {
                double x=route[i].X-route[i-1].X,y=route[i].Y-route[i-1].Y,z=route[i].Z-route[i-1].Z;
                length+=Math.Sqrt(x*x+y*y+z*z);
            }
            return length;
        }

        private static double Distance(NpcActionTarget a,NpcActionTarget b)
        {
            double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return Math.Sqrt(x*x+y*y+z*z);
        }

        private static double Now()
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;double whole=clock.ToSeconds();
            return whole+(clock.Second-Math.Floor(clock.Second));
        }

        private static void RequireNoActor(DaggerfallInterior room,string residentId)
        {
            foreach(var actor in room.GetComponentsInChildren<ResidentActor>(true))
                if(actor && actor.Resident!=null && actor.Resident.Id==residentId)
                    throw new InvalidOperationException("Returning resident materialized inside unrelated player building.");
        }

        private static void Require(bool value,string message)
        {if(!value)throw new InvalidOperationException(message);}
    }
}
