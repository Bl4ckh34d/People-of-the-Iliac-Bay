using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NpcFollowTransitionProbe
    {
        private static IEnumerator RunUnplannedBuildingReturn(StreetPopulation streets,string residentId,string character)
        {
            var game=GameManager.Instance;var store=Store(streets);var actor=streets.FindActor(residentId);
            NeedBuilding(actor && actor.gameObject.activeInHierarchy,"No existing patron for unplanned building return.");
            actor.CaptureInventory();var inventory=store.ReadNpcInventory(residentId);
            game.PlayerMotor.transform.position=actor.transform.position;Physics.SyncTransforms();yield return null;
            yield return RunDoorway(streets,residentId,character,()=>ExitFixture(streets),false);
            actor=streets.FindActor(residentId);
            var directory=(BuildingDirectory)typeof(StreetPopulation).GetField("directory",Private).GetValue(streets);
            var geometry=OutsideGeometry(streets);object plan=null;
            foreach(var candidate in (IEnumerable)typeof(StreetPopulation).GetField("plans",Private).GetValue(streets))
                if(((Resident)candidate.GetType().GetField("Resident").GetValue(candidate)).Id==residentId){plan=candidate;break;}
            NeedBuilding(plan!=null,"The genuine resident lost its routine plan.");
            DaggerfallStaticDoors owner=null;StaticDoor selected=default(StaticDoor);float best=float.PositiveInfinity;
            var from=directory.transform.InverseTransformPoint(game.PlayerMotor.transform.position-Vector3.up);
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            for(int index=0;doors.Doors!=null && index<doors.Doors.Length;index++)
            {
                var door=doors.Doors[index];TownGeometry.Entrance entrance;
                if(door.doorType!=DoorTypes.Building || !geometry.Entrances.TryGetValue(door.buildingKey,out entrance) ||
                    !(bool)typeof(StreetPopulation).GetMethod("CanEnterBuilding",Private).Invoke(streets,new object[] {actor,door.buildingKey}) ||
                    (bool)typeof(StreetPopulation).GetMethod("TryRoutineRecoveryDoor",Private).Invoke(streets,new object[] {plan,door.buildingKey,null}))continue;
                var normal=doors.GetDoorNormal(index);normal.y=0;
                var canonical=directory.transform.InverseTransformPoint(doors.GetDoorPosition(index)+normal.normalized*0.6f);
                if(new Vector2(canonical.x-entrance.LocalDoor.x,canonical.z-entrance.LocalDoor.z).sqrMagnitude>0.01f)continue;
                var path=geometry.RouteBetweenPoints(new SavedRoutePoint {X=from.x,Z=from.z},
                    new SavedRoutePoint {X=entrance.LocalDoor.x,Z=entrance.LocalDoor.z});
                if(path.Count<2)continue;
                float length=0;for(int i=1;i<path.Count;i++)length+=Vector2.Distance(new Vector2((float)path[i-1].X,(float)path[i-1].Z),
                    new Vector2((float)path[i].X,(float)path[i].Z));
                if(length>=best || length>60)continue;
                best=length;owner=doors;selected=door;
            }
            NeedBuilding(owner,"No nearby reachable open public building outside the resident's saved routine.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_UNPLANNED_BUILDING_FIXTURE: resident="+residentId+
                "; building="+selected.buildingKey+"; absentFromSavedRoutine=true; approach="+best);
            yield return RunDoorway(streets,residentId,character,()=>EnterFixture(streets,owner,selected),false);
            // Door arrival deliberately accepts a follower two metres behind the observer.
            // Walk farther inside before Stop so return measures a real route, not a doorway landing correction.
            var room=game.PlayerEnterExit.Interior;var roomGeometry=RoomGeometry(streets);
            var observerStart=room.transform.InverseTransformPoint(game.PlayerMotor.transform.position-Vector3.up);
            var anchors=new List<Vector3>(roomGeometry.LocalAnchors);
            anchors.Sort((a,b)=>(a-observerStart).sqrMagnitude.CompareTo((b-observerStart).sqrMagnitude));
            List<GuardPoint3> deeper=null;
            foreach(var anchor in anchors)
            {
                if(Math.Abs(anchor.y-observerStart.y)>0.4f ||
                    Vector3.Distance(room.transform.TransformPoint(anchor),roomGeometry.EntryGround.Value)<5)continue;
                var route=roomGeometry.RouteBetween(observerStart,anchor);
                if(route.Count>=2){deeper=route;break;}
            }
            NeedBuilding(deeper!=null,"No surveyed interior route provides a meaningful return walk.");
            yield return WalkObserver(IndoorWorldRoute(room,deeper));
            float followDeadline=Time.realtimeSinceStartup+30;
            do
            {
                actor=streets.FindActor(residentId);
                if(actor && Vector3.Distance(actor.transform.position-Vector3.up,roomGeometry.EntryGround.Value)>2.5f)break;
                yield return null;
            }while(Time.realtimeSinceStartup<followDeadline);
            NeedBuilding(actor && Vector3.Distance(actor.transform.position-Vector3.up,roomGeometry.EntryGround.Value)>2.5f,
                "Follower did not walk away from the unplanned building entrance.");
            string stopReason;
            NeedBuilding(streets.TryStopConversationAction(residentId,out stopReason),"Unplanned building Stop failed: "+stopReason);
            var job=store.ActiveNpcAction(residentId);string actionId=job.Id;
            NeedBuilding(job.Phase==NpcActionPhase.Cancelled && job.ReturningToRoutine,"Stop did not request routine recovery.");
            actor=streets.FindActor(residentId);NeedBuilding(actor,"Stopped follower is not physically inside the new building.");
            Vector3 previous=actor.transform.position;float walked=0,maxStep=0;bool sourceGone=false;
            float deadline=Time.realtimeSinceStartup+45;
            while(Time.realtimeSinceStartup<deadline)
            {
                job=store.ReadNpcAction(actionId);actor=streets.FindActor(residentId);
                if(actor && actor.gameObject.activeInHierarchy)
                {float step=Vector3.Distance(previous,actor.transform.position);walked+=step;maxStep=Math.Max(maxStep,step);previous=actor.transform.position;}
                if(job.RoutineRecoveryTransition!=null && job.RoutineRecoveryTransition.Destination.Kind==NpcActionTargetKind.WorldPoint)
                {sourceGone=!actor;break;}
                yield return null;
            }
            NeedBuilding(sourceGone && walked>0.5f && maxStep<1.5f,
                "Stopped NPC did not physically leave the unplanned building; walked="+walked+"; maxStep="+maxStep);
            NeedBuilding(game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior.EntryDoor.buildingKey==selected.buildingKey,
                "NPC routine return moved the player.");
            var restored=new WorldStateStore(0);
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
            NeedBuilding(restored.ReadNpcAction(actionId).RoutineRecoveryTransition.OriginBuildingKey==selected.buildingKey,
                "Native serializer lost the actual unplanned source door.");
            yield return ExitFixture(streets);
            bool outsideBody=false,trackingOutside=false;float outsideWalk=0,outsideMaxStep=0;
            Vector3 previousOutside=Vector3.zero;deadline=Time.realtimeSinceStartup+120;
            while(Time.realtimeSinceStartup<deadline)
            {
                actor=streets.FindActor(residentId);
                if(actor && actor.gameObject.activeInHierarchy)
                {
                    outsideBody=true;
                    if(trackingOutside)
                    {float step=Vector3.Distance(previousOutside,actor.transform.position);outsideWalk+=step;outsideMaxStep=Math.Max(outsideMaxStep,step);}
                    previousOutside=actor.transform.position;trackingOutside=true;
                    game.PlayerMotor.transform.position=actor.transform.position+Vector3.back*5+Vector3.up;
                    Physics.SyncTransforms();
                }
                if(store.ActiveNpcAction(residentId)==null)break;
                yield return null;
            }
            NeedBuilding(outsideBody && outsideWalk>0.5f && outsideMaxStep<1.5f && store.ActiveNpcAction(residentId)==null,
                "Unplanned building return did not physically rejoin the saved routine outside.");
            if(actor)actor.CaptureInventory();var after=store.ReadNpcInventory(residentId);
            NeedBuilding(after.WalletGold==inventory.WalletGold && after.Items.Count==inventory.Items.Count,"Return changed actual belongings.");
            foreach(var item in inventory.Items)NeedBuilding(after.Items.Exists(i=>i.Uid==item.Uid && i.NativePayload==item.NativePayload),
                "Return changed exact item identity.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_UNPLANNED_BUILDING_RETURN_PASS: actualUnknownDoor=true; sourceWalk="+walked+
                "; maxStep="+maxStep+"; outsideWalk="+outsideWalk+"; outsideMaxStep="+outsideMaxStep+
                "; playerNotMoved=true; nativeSerializer=true; sameNpcOutside=true; routineReleased=true; belongingsPreserved=true");
        }
    }
}
