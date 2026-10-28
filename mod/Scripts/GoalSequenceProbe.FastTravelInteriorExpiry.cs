using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GoalSequenceProbe
    {
        // A real foreign companion must already have crossed the native door.
        // Only the clock advances here; no action, wage or position is injected.
        internal static IEnumerator VerifyFastTravelInteriorExpiry(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;
            RequireFastTravel(host && streets && streets.Ready && NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) &&
                !GameManager.IsGamePaused && game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior,
                "Interior expiry requires an unpaused owned test save inside the actual destination building.");
            var store=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
            var bindings=(IDictionary)Field(streets.GetType(),"paidDoorPlans").GetValue(streets);
            int map=game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            int building=game.PlayerEnterExit.Interior.EntryDoor.buildingKey;
            bool requireUnpaid=Environment.GetEnvironmentVariable("POTIB_FAST_TRAVEL_INTERIOR_UNPAID")=="1";
            NpcActionJob job=null;
            foreach(var candidate in store.PendingNpcActions())
                if(candidate.FastTravelFollowOwner && candidate.FollowTransition==null &&
                    candidate.LastPosition!=null && candidate.LastPosition.Kind==NpcActionTargetKind.IndoorPoint &&
                    candidate.LastPosition.SettlementId==map && candidate.LastPosition.BuildingKey==building &&
                    NpcCompanionFastTravel.CanFollow(store,candidate,FastTravelNow()) &&
                    (!requireUnpaid || !NpcPaidCompanionAction.IsPaidFollowJob(candidate)))
                {
                    RequireFastTravel(job==null,"Interior expiry fixture must contain exactly one matching actual fast-travel follower.");
                    job=candidate;
                }
            RequireFastTravel(job!=null,"No actual fast-travel follower owns a temporary door plan in this building.");
            string actionId=job.Id,residentId=job.ResidentId;
            ResidentActor actor=null;float initialReady=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<initialReady)
            {
                actor=streets.FindActor(residentId);
                if(actor && actor.gameObject.activeInHierarchy && actor.IsStandingStill && bindings.Contains(residentId) &&
                    actor.transform.parent==game.PlayerEnterExit.Interior.transform)break;
                yield return null;
            }
            RequireFastTravel(actor && actor.gameObject.activeInHierarchy && actor.Person && actor.IsStandingStill &&
                bindings.Contains(residentId) &&
                !actor.IsConversing && !actor.IsSleeping && !actor.IsSeated &&
                actor.transform.parent==game.PlayerEnterExit.Interior.transform,
                "The actual interior follower must have settled beside the player before testing expiry.");
            double finish=job.FinishAt,consentDeadline=job.DeadlineAt;
            string contractId=NpcPaidCompanionAction.ContractId(job);
            var originalContract=contractId==null?null:store.ReadNpcServiceContracts().Read(contractId);
            double ends=finish>0?Math.Min(finish,consentDeadline):consentDeadline;
            RequireFastTravel(ends>FastTravelNow(),"The existing Follow consent already ended before the expiry fixture started.");
            var position=actor.transform.position;
            actor.CaptureState();string inventory=FastTravelInventory(store,residentId),relation=FastTravelRelationship(store,residentId);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_INTERIOR_EXPIRY_BEGIN: person="+residentId+
                "; paid="+NpcPaidCompanionAction.IsPaidFollowJob(job)+"; deferredPaidHandoff="+(job.PaidTravelHandoff!=null)+
                "; originalFinish="+finish+"; originalDeadline="+consentDeadline+"; actualDoorBinding=true");
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(ends+1-FastTravelNow()));
            float deadline=Time.realtimeSinceStartup+5;
            float drift=0;
            while(Time.realtimeSinceStartup<deadline)
            {
                RequireFastTravel(actor && actor.gameObject.activeInHierarchy,"Expiry removed the visible companion body.");
                drift=Mathf.Max(drift,Vector3.Distance(position,actor.transform.position));
                yield return null;
            }
            job=store.ReadNpcAction(actionId);
            RequireFastTravel(job!=null && NpcActionExecutor.IsTerminal(job) && store.ActiveNpcAction(residentId)==null &&
                job.FinishAt==finish && job.DeadlineAt==consentDeadline && !NpcCompanionFastTravel.CanFollow(store,job,FastTravelNow()),
                "Interior expiry left the original Follow active or changed its agreed end.");
            if(originalContract!=null)
            {
                var ended=store.ReadNpcServiceContracts().Read(contractId);
                RequireFastTravel(ended!=null && ended.Status==NpcServiceContractStatus.Terminated &&
                    ended.BlockStartAt==originalContract.BlockStartAt && ended.BlockEndAt==originalContract.BlockEndAt &&
                    ended.FundedGold==originalContract.FundedGold,
                    "Paid interior expiry did not settle the original contract without renewal or a new wage.");
            }
            int bodies=0;foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(candidate && candidate.gameObject.activeInHierarchy && candidate.Resident!=null && candidate.Resident.Id==residentId)bodies++;
            actor.CaptureState();
            RequireFastTravel(bodies==1 && drift<=0.1f && actor.IsStandingStill &&
                FastTravelInventory(store,residentId)==inventory && FastTravelRelationship(store,residentId)==relation,
                "Expiry moved or duplicated the visible body, or changed its inventory, money or relationship.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_INTERIOR_EXPIRY_PASS: person="+residentId+
                "; map="+map+"; building="+building+"; requiredUnpaid="+requireUnpaid+
                "; controlledClockAdvance=true; originalConsentEnded=true; uniqueSameBody=true; maxDrift="+drift+
                "; sameInventoryAndRelationship=true; routineReturnNotExercised=true");
            if(Environment.GetEnvironmentVariable("POTIB_FAST_TRAVEL_INTERIOR_EXIT")=="1")
                yield return VerifyRetiredCompanionExterior(host,streets,store,actor,actionId,residentId,inventory,relation,finish,consentDeadline);
        }

        private static IEnumerator VerifyRetiredCompanionExterior(PeopleOfTheIliacBayMod host,StreetPopulation streets,
            WorldStateStore store,ResidentActor actor,string actionId,string residentId,string inventory,string relation,
            double finish,double consentDeadline)
        {
            var game=GameManager.Instance;var roads=host.GetComponent<RoadTravelActors>();
            NpcFollowTransitionProbe.PositionObserverAtExit(streets);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RETIRED_COMPANION_EXIT_BEGIN: person="+residentId+
                "; controlledObserverPlacement=true; npcPlaced=false; originalActionEnded=true");
            game.PlayerEnterExit.TransitionExterior();
            yield return NpcFollowTransitionProbe.WaitForScene(streets,false);
            float deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline && (game.IsPlayerInside || !game.StreamingWorld.IsReady ||
                game.StreamingWorld.IsRepositioningPlayer))yield return null;
            RequireFastTravel(!game.IsPlayerInside && game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer,
                "Retired companion exit did not reach the actual native exterior.");
            deadline=Time.realtimeSinceStartup+5;
            while(Time.realtimeSinceStartup<deadline)yield return null;
            int visible=0;foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(candidate && candidate.gameObject.activeInHierarchy && candidate.Resident!=null && candidate.Resident.Id==residentId)visible++;
            RequireFastTravel((!actor || !actor.gameObject.activeInHierarchy) && visible==0 && !roads.OwnsPaidCompanion(residentId),
                "The expired interior companion leaked into the exterior; visible="+visible+
                "; oldBodyActive="+(actor && actor.gameObject.activeInHierarchy)+"; roadOwner="+roads.OwnsPaidCompanion(residentId));
            var job=store.ReadNpcAction(actionId);
            RequireFastTravel(job!=null && NpcActionExecutor.IsTerminal(job) && store.ActiveNpcAction(residentId)==null &&
                job.FinishAt==finish && job.DeadlineAt==consentDeadline && job.FollowTransition==null &&
                FastTravelInventory(store,residentId)==inventory && FastTravelRelationship(store,residentId)==relation,
                "Leaving the building revived an expired action or changed the former companion's possessions or relationship.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RETIRED_COMPANION_EXIT_PASS: person="+residentId+
                "; nativeExit=true; noExteriorBody=true; noRoadOwner=true; actionRemainsEnded=true"+
                "; originalConsentDates=true; sameInventoryAndRelationship=true; routineReturnNotExercised=true");
        }
    }
}
