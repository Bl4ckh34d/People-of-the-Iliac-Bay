using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GoalSequenceProbe
    {
        internal static IEnumerator PrepareFastTravelInteriorCheckpoint(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;var roads=host.GetComponent<RoadTravelActors>();
            RequireFastTravel(NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) && streets && streets.Ready &&
                roads && !game.IsPlayerInside && !GameManager.IsGamePaused,
                "Interior entry requires an owned exterior checkpoint with an actual fast-travel follower.");
            var store=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
            var party=roads.CaptureFastTravelQuote();
            RequireFastTravel(party.Members.Count==1,"Interior entry requires exactly one actual active follower.");
            var actor=party.Members[0].Actor;var job=store.ReadNpcAction(party.Members[0].ActionId);
            RequireFastTravel(job.FastTravelFollowOwner && Math.Min(job.FinishAt,job.DeadlineAt)-FastTravelNow()>1200,
                "Interior entry must retain the actual fast-travel owner and enough original consent.");
            string id=job.ResidentId,action=job.Id;double finish=job.FinishAt,consentDeadline=job.DeadlineAt;
            actor.CaptureState();string inventory=FastTravelInventory(store,id),relation=FastTravelRelationship(store,id);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors owner=null;StaticDoor entrance=default(StaticDoor);Vector3 ground=Vector3.zero;
            float nearest=100;
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var raw in doors.Doors??new StaticDoor[0])
                {
                    BuildingSummary summary;
                    if(raw.doorType!=DoorTypes.Building || !directory.GetBuildingSummary(raw.buildingKey,out summary) ||
                        summary.BuildingType!=DFLocation.BuildingTypes.Tavern)continue;
                    var door=raw;door.ownerPosition=doors.transform.position;door.ownerRotation=doors.transform.rotation;
                    var normal=DaggerfallStaticDoors.GetDoorNormal(door);normal.y=0;
                    var expected=StreetPopulation.FollowExteriorDoorGround(door)+normal.normalized*3;
                    float distance=Vector3.Distance(actor.transform.position-Vector3.up,expected);
                    if(distance>=nearest)continue;
                    object[] floor={expected,null,Vector3.zero};
                    if(!(bool)Method(typeof(NativeDungeonFollowProbe),"StaticFloor",StaticMembers,3).Invoke(null,floor))continue;
                    if(!(bool)Method(typeof(NativeDungeonFollowProbe),"BodyClear",StaticMembers,5)
                        .Invoke(null,new object[]{floor[2],floor[2],game.PlayerMotor.transform,false,CrowdMotion.BodyRadius}))continue;
                    nearest=distance;owner=doors;entrance=door;ground=(Vector3)floor[2];
                }
            RequireFastTravel(owner,"No actual tavern door with supported exterior floor lies within 100 metres of the saved follower.");
            var controller=game.PlayerMotor.GetComponent<CharacterController>();
            RequireFastTravel(controller && controller.enabled,"Interior entry needs the native player controller.");
            bool oldMotor=game.PlayerMotor.enabled;game.PlayerMotor.enabled=false;
            try
            {
                DaggerfallUI.Instance.PopToHUD();
                controller.enabled=false;game.PlayerMotor.transform.position=ground+Vector3.up;controller.enabled=true;Physics.SyncTransforms();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_INTERIOR_ENTRY_SETUP: person="+id+"; building="+entrance.buildingKey+
                    "; controlledObserverPlacement=true; npcPlaced=false; originalFollow=true; approachDistance="+nearest);
                float deadline=Time.realtimeSinceStartup+60;
                while(Time.realtimeSinceStartup<deadline && Vector3.Distance(actor.transform.position-Vector3.up,ground)>5)
                    yield return null;
                RequireFastTravel(actor && Vector3.Distance(actor.transform.position-Vector3.up,ground)<=5 &&
                    NpcCompanionFastTravel.CanFollow(store,store.ReadNpcAction(action),FastTravelNow()),
                    "Actual follower did not reach the tavern approach through ordinary movement.");
                game.PlayerEnterExit.TransitionInterior(owner.transform,entrance);
                yield return NpcFollowTransitionProbe.WaitForScene(streets,true);
                yield return (IEnumerator)Method(typeof(NativeDungeonFollowProbe),"ClearRecoveryArrivalObserver",StaticMembers,1)
                    .Invoke(null,new object[]{null});
                deadline=Time.realtimeSinceStartup+30;actor=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    actor=streets.FindActor(id);job=store.ReadNpcAction(action);
                    if(actor && actor.gameObject.activeInHierarchy && job!=null && job.FollowTransition==null)break;
                    yield return null;
                }
                RequireFastTravel(actor && actor.gameObject.activeInHierarchy && job!=null && job.FollowTransition==null &&
                    job.LastPosition.Kind==NpcActionTargetKind.IndoorPoint && job.LastPosition.BuildingKey==entrance.buildingKey &&
                    job.FinishAt==finish && job.DeadlineAt==consentDeadline && NpcCompanionFastTravel.CanFollow(store,job,FastTravelNow()),
                    "Actual door entry did not preserve the existing follower and consent in the destination interior.");
                actor.CaptureState();RequireFastTravel(FastTravelInventory(store,id)==inventory && FastTravelRelationship(store,id)==relation,
                    "Actual entry changed the companion inventory, money or relationship.");
                yield return SaveFastTravelCheckpoint(game,"People - fast travel interior follow");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_INTERIOR_ENTRY_PASS: person="+id+"; building="+entrance.buildingKey+
                    "; nativeDoor=true; completedCrossing=true; originalConsent=true; sameInventoryAndRelationship=true; checkpointSaved=true");
            }
            finally{game.PlayerMotor.enabled=oldMotor;}
        }
    }
}
