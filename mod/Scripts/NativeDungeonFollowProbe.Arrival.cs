using System;
using System.Collections;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        private const string RecoveryArrivalCheckpoint="People - Daggerfall recovery returning";

        private static IEnumerator VerifyRecoveryOriginArrival(WorldStateStore store,StreetPopulation streets,string residentId,bool resume)
        {
            var game=GameManager.Instance;NpcActionJob job=null;
            foreach(var candidate in store.PendingNpcActions())
                if(candidate.ResidentId==residentId && candidate.CompanionRecovery!=null &&
                    candidate.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.ReturningToOrigin)
                {Need(job==null,"Recovery checkpoint contains multiple return owners.");job=candidate;}
            Need(job!=null && NpcActionExecutor.IsTerminal(job) && job.CompanionRecovery.OverlandStartedAt>0 &&
                job.CompanionRecovery.RecruitmentOrigin.Kind==NpcActionTargetKind.IndoorPoint,
                "Arrival probe requires the actual expired companion returning to its recruitment interior.");
            string actionId=job.Id,inventory=InventorySignature(store.ReadNpcInventory(residentId));
            var origin=NpcActionExecutor.CopyTarget(job.CompanionRecovery.RecruitmentOrigin);
            double finish=job.FinishAt,consentDeadline=job.DeadlineAt;
            bool oldMotor=game.PlayerMotor.enabled;game.PlayerMotor.enabled=false;
            try
            {
                if(!resume)
                {
                    yield return StageRecoveryOriginObserver(streets,origin,job.CompanionRecovery.RecruitmentArrival);
                    yield return ConversationMoodProbe.SaveCheckpoint(game,game.PlayerEntity.Name,RecoveryArrivalCheckpoint);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_RECOVERY_ARRIVAL_CHECKPOINT: title="+RecoveryArrivalCheckpoint+
                        "; nativeDiskSave=true; observerTransportOnly=true; npcJourneyUnobserved=true");
                }
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                Need(game.PlayerEnterExit.IsPlayerInsideBuilding && streets.Ready && directory && directory.MapID==origin.SettlementId &&
                    game.PlayerEnterExit.Interior.EntryDoor.buildingKey==origin.BuildingKey,
                    "Arrival observer is not in the actual saved recruitment interior.");
                yield return ClearRecoveryArrivalObserver();
                job=store.ReadNpcAction(actionId);var recovery=job.CompanionRecovery;
                Need(Now()<recovery.OverlandArrivesAt && !streets.FindActor(residentId),
                    "Returning companion appeared at recruitment before its actual native journey ended; now="+Now()+
                    "; arrives="+recovery.OverlandArrivesAt+"; phase="+recovery.Phase+
                    "; actionPhase="+job.Phase+"; returningToRoutine="+job.ReturningToRoutine+
                    "; owner="+streets.RecoveredCompanionInteriorDiagnostic(residentId));
                double approachMetres=NpcFollowTransition.Length(recovery.RecruitmentApproach);
                double approachStarts=recovery.RecruitmentGateWalkSeconds>0?
                    NpcCompanionWaitPolicy.GateApproachStart(recovery.OverlandArrivesAt,recovery.RecruitmentGateWalkSeconds):recovery.OverlandArrivesAt;
                double arrivalAt=approachStarts+Math.Max(0.5,approachMetres*recovery.WalkSecondsPerUnit);
                Need(arrivalAt>Now() && arrivalAt-recovery.OverlandArrivesAt<86400,"Actual recruitment approach has an invalid arrival deadline.");
                Need(streets.CanReceiveRecoveredCompanionInterior(job,store),"Actual recruitment plan and free entry must be ready before the obstruction check.");
                var geometry=typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets) as InteriorGeometry;
                Need(geometry!=null && geometry.EntryGround.HasValue,"Actual recruitment entry geometry is missing.");
                var controller=game.PlayerMotor.GetComponent<CharacterController>();
                float blockingDeadline=Time.realtimeSinceStartup+10;
                while(Vector3.Distance(PlayerGround(),geometry.EntryGround.Value)>0.2f && Time.realtimeSinceStartup<blockingDeadline)
                {MoveObserverToward(geometry.EntryGround.Value,controller);yield return null;}
                Need(Vector3.Distance(PlayerGround(),geometry.EntryGround.Value)<=0.2f &&
                    !streets.CanReceiveRecoveredCompanionInterior(store.ReadNpcAction(actionId),store),
                    "Native observer did not physically block the actual recruitment entry.");
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(arrivalAt+1-Now()));
                for(int frame=0;frame<8;frame++)yield return null;
                job=store.ReadNpcAction(actionId);
                Need(job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.ReturningToOrigin && job.ReturningToRoutine &&
                    job.CompanionRecovery.MeasuredRoute!=null && !streets.FindActor(residentId),
                    "A blocked recruitment entry completed the return or materialized through the player.");
                // Observe as soon as the physical entry becomes free. The old
                // booking is expired, so the ordinary routine can leave on the
                // next frame; finishing a longer observer walk would miss it.
                yield return ClearRecoveryArrivalObserver(()=>streets.FindActor(residentId));
                float deadline=Time.realtimeSinceStartup+30;ResidentActor returned=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    job=store.ReadNpcAction(actionId);returned=streets.FindActor(residentId);
                    if(job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.ResumedRoutine && returned && returned.gameObject.activeInHierarchy)break;
                    yield return null;
                }
                Need(job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.ResumedRoutine && returned && returned.gameObject.activeInHierarchy,
                    "Timed recovery did not materialize at its actual recruitment interior; phase="+job.CompanionRecovery.Phase+
                    "; returningToRoutine="+job.ReturningToRoutine+"; nativeActor="+(bool)returned+"; streetsReady="+streets.Ready+
                    "; lastSpace="+job.LastPosition.WorldSpaceId+"; lastBuilding="+job.LastPosition.BuildingKey+
                    "; owner="+streets.RecoveredCompanionInteriorDiagnostic(residentId));
                Need(returned.Resident.Id==residentId && returned.Person && returned.transform.IsChildOf(game.PlayerEnterExit.Interior.transform),
                    "Returned person is not owned by its actual recruitment interior.");
                int bodies=0;foreach(var body in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if(body && body.gameObject.activeInHierarchy && body.Resident!=null && body.Resident.Id==residentId)bodies++;
                Need(bodies==1 && job.FinishAt==finish && job.DeadlineAt==consentDeadline && NpcActionExecutor.IsTerminal(job) && job.ReturningToRoutine,
                    "Recovery duplicated the body, renewed paid action or failed to hand off ordinary routine return.");
                returned.CaptureState();Need(InventorySignature(store.ReadNpcInventory(residentId))==inventory,
                    "Recovery arrival changed item UIDs, payload or wallet.");
                Need(NpcFollowTransition.SameSpace(job.LastPosition,origin),"Recovery did not commit the actual recruitment interior position.");
                // This fixture's original room booking expired during its real
                // journey. Its existing routine must leave through the same Inn,
                // rather than claim a bed or vanish without a portal receipt.
                deadline=Time.realtimeSinceStartup+8;
                while(Time.realtimeSinceStartup<deadline && store.ReadNpcAction(actionId).RoutineRecoveryTransition==null)yield return null;
                job=store.ReadNpcAction(actionId);var onward=job.RoutineRecoveryTransition;
                Need(onward!=null && onward.SourceRoute.Count>=2 && NpcFollowTransition.SameSpace(onward.SourceRoute[0],origin) &&
                    onward.Destination.Kind==NpcActionTargetKind.WorldPoint && onward.Destination.SettlementId==origin.SettlementId &&
                    NpcActionExecutor.IsTerminal(job) && job.ReturningToRoutine && !streets.FindActor(residentId),
                    "Returned traveler did not continue its expired visit through the actual native Inn exit.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_RECOVERY_ORIGIN_ARRIVAL_PASS: person="+residentId+
                    "; recruitmentMap="+origin.SettlementId+"; recruitmentBuilding="+origin.BuildingKey+
                    "; actualNativeInteriorBody=true; oneBody=true; sameInventory=true; paidActionNotRenewed=true; ordinaryRoutineReturnPending=true"+
                    "; blockedEntryRetainedJourney=true; actualInnExitReceipt=true"+
                    "; nativeEstimatedTravel=true; clockSkipped=true; approachMetres="+approachMetres+
                    "; checkpointReload="+resume+"; sourceJourneyObserved=false");
            }
            finally{game.PlayerMotor.enabled=oldMotor;}
        }

        private static IEnumerator StageRecoveryOriginObserver(StreetPopulation streets,NpcActionTarget origin,NpcActionTarget arrival)
        {
            var game=GameManager.Instance;
            if(game.PlayerEnterExit.IsPlayerInsideDungeon)
            {
                int sourceMap=game.PlayerEnterExit.Dungeon.Summary.ID;
                game.PlayerEnterExit.TransitionDungeonExterior();yield return WaitForExteriorFloor(sourceMap);
            }
            Need(!game.IsPlayerInside,"Recovery observer staging requires an actual native exterior.");
            var pixel=MapsFile.GetPixelFromPixelID(origin.SettlementId & 0x000fffff);
            game.StreamingWorld.TeleportToCoordinates(pixel.X,pixel.Y,StreamingWorld.RepositionMethods.RandomStartMarker);
            float deadline=Time.realtimeSinceStartup+30;DaggerfallStaticDoors owner=null;StaticDoor entrance=default(StaticDoor);
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return null;var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                if(!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer || !directory || directory.MapID!=origin.SettlementId)continue;
                float nearest=float.MaxValue;
                var expected=directory.transform.TransformPoint(new Vector3((float)arrival.X,(float)arrival.Y,(float)arrival.Z));
                foreach(var collection in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach(var raw in collection.Doors??new StaticDoor[0])
                        if(raw.doorType==DoorTypes.Building && raw.buildingKey==origin.BuildingKey)
                        {
                            var candidate=raw;candidate.ownerPosition=collection.transform.position;candidate.ownerRotation=collection.transform.rotation;
                            float distance=Vector3.Distance(StreetPopulation.FollowExteriorDoorGround(candidate),expected);
                            if(distance>=nearest)continue;nearest=distance;owner=collection;entrance=candidate;
                        }
                if(owner)break;
            }
            Need(owner,"Native recruitment building has no actual exterior door.");
            Vector3 ground;Need(StaticFloor(StreetPopulation.FollowExteriorDoorGround(entrance),null,out ground),"Recruitment observer door has no actual floor.");
            var controller=game.PlayerMotor.GetComponent<CharacterController>();bool enabled=controller.enabled;
            controller.enabled=false;game.PlayerMotor.transform.position=ground+Vector3.up;controller.enabled=enabled;Physics.SyncTransforms();
            game.PlayerEnterExit.TransitionInterior(owner.transform,entrance);
            yield return NpcFollowTransitionProbe.WaitForScene(streets,true);
        }

        private static IEnumerator ClearRecoveryArrivalObserver(Func<bool> arrivalObserved=null)
        {
            var game=GameManager.Instance;var controller=game.PlayerMotor.GetComponent<CharacterController>();
            var start=PlayerGround();Vector3 clear=Vector3.zero;bool found=false;
            foreach(float distance in new[]{1.5f,2f,2.5f,3f})
            {
                for(int index=0;index<16;index++)
                {
                    float angle=index*Mathf.PI/8;var direction=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                    if(StaticFloor(start+direction*distance,game.PlayerEnterExit.Interior.transform,out clear) &&
                        BodyClear(start,clear,controller.transform,true,controller.radius+0.02f))
                    {found=true;break;}
                }
                if(found)break;
            }
            Need(found,"Recruitment interior has no supported clear observer position away from its entry.");
            float deadline=Time.realtimeSinceStartup+10;
            while(Vector3.Distance(PlayerGround(),clear)>0.4f && Time.realtimeSinceStartup<deadline &&
                !(arrivalObserved!=null && arrivalObserved()))
            {MoveObserverToward(clear,controller);yield return null;}
            Need(Vector3.Distance(PlayerGround(),clear)<=0.4f || arrivalObserved!=null && arrivalObserved(),
                "Native observer did not clear the recruitment entry.");
        }
    }
}
