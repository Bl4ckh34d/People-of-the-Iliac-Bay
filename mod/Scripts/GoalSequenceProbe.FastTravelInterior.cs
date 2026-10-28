using System;
using System.Collections;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GoalSequenceProbe
    {
        // Starts only after a real fast-travel companion followed through a
        // native building door. No hire, scene transport or booking is injected.
        internal static IEnumerator VerifyFastTravelInteriorReload(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            const string title="People - fast travel interior follow";
            var game=GameManager.Instance;
            RequireFastTravel(host && streets && streets.Ready && NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) &&
                game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior,
                "Interior reload requires an owned test save inside the actual destination building.");
            var store=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
            int map=game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            int building=game.PlayerEnterExit.Interior.EntryDoor.buildingKey;
            NpcActionJob job=null;
            foreach(var candidate in store.PendingNpcActions())
                if(candidate.FastTravelFollowOwner && candidate.FollowTransition==null &&
                    candidate.LastPosition!=null && candidate.LastPosition.Kind==NpcActionTargetKind.IndoorPoint &&
                    candidate.LastPosition.SettlementId==map && candidate.LastPosition.BuildingKey==building &&
                    NpcCompanionFastTravel.CanFollow(store,candidate,FastTravelNow()))
                {
                    RequireFastTravel(job==null,"Interior reload fixture must have exactly one actual fast-travel follower.");
                    job=candidate;
                }
            RequireFastTravel(job!=null && Math.Min(job.FinishAt,job.DeadlineAt)-FastTravelNow()>600,
                "No actual arrived fast-travel follower with enough existing consent in this building.");
            string actionId=job.Id,residentId=job.ResidentId;
            ResidentActor actor=null;float initialReady=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<initialReady)
            {
                actor=streets.FindActor(residentId);
                if(actor && actor.gameObject.activeInHierarchy && actor.Person &&
                    actor.transform.parent==game.PlayerEnterExit.Interior.transform)break;
                yield return null;
            }
            RequireFastTravel(actor && actor.gameObject.activeInHierarchy && actor.Person &&
                actor.transform.parent==game.PlayerEnterExit.Interior.transform,
                "Actual fast-travel follower must be visible inside before the disk reload.");
            double finish=job.FinishAt,consentDeadline=job.DeadlineAt;
            actor.CaptureState();string inventory=FastTravelInventory(store,residentId),relation=FastTravelRelationship(store,residentId);
            yield return SaveFastTravelCheckpoint(game,title);
            yield return ConversationMoodProbe.LoadSave(host,streets,game.PlayerEntity.Name,title,true);
            store=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
            float deadline=Time.realtimeSinceStartup+30;actor=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                actor=streets.FindActor(residentId);
                if(actor && actor.gameObject.activeInHierarchy)break;
                yield return null;
            }
            RequireFastTravel(actor && actor.gameObject.activeInHierarchy && actor.Person &&
                game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerGPS.CurrentLocation.MapTableData.MapId==map &&
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey==building && actor.transform.parent==game.PlayerEnterExit.Interior.transform,
                "Saved fast-travel follower did not restore in its actual building after its door transition had completed.");
            job=store.ReadNpcAction(actionId);
            RequireFastTravel(job!=null && job.ResidentId==residentId && job.FastTravelFollowOwner && job.FollowTransition==null &&
                job.LastPosition.Kind==NpcActionTargetKind.IndoorPoint && job.LastPosition.SettlementId==map && job.LastPosition.BuildingKey==building &&
                Math.Abs(job.FinishAt-finish)<0.001 && Math.Abs(job.DeadlineAt-consentDeadline)<0.001 &&
                NpcCompanionFastTravel.CanFollow(store,job,FastTravelNow()),
                "Interior reload changed the existing action, completed crossing or consent window.");
            int bodies=0;foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(candidate && candidate.gameObject.activeInHierarchy && candidate.Resident!=null && candidate.Resident.Id==residentId)bodies++;
            actor.CaptureState();
            RequireFastTravel(bodies==1 && FastTravelInventory(store,residentId)==inventory && FastTravelRelationship(store,residentId)==relation,
                "Interior reload duplicated the companion or changed inventory, money or relationship.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_INTERIOR_RELOAD_PASS: person="+residentId+
                "; map="+map+"; building="+building+"; actualDiskSaveLoad=true; completedDoorTransition=true; uniqueBody=true"+
                "; sameInventoryAndRelationship=true; originalActionAndConsent=true; checkpoint="+title);
        }
    }
}
