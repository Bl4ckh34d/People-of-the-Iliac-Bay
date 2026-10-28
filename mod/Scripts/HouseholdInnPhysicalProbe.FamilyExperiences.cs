using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class HouseholdInnPhysicalProbe
    {
        // Called only at the existing actual paired home-room handback. That
        // fixture owns a detached store and disables the full population update.
        // Exercise its unchanged production sharing tick, never rebind the host
        // or turn that detached fixture into a claimed live scheduler run.
        private static IEnumerator VerifyFamilyExperienceSharingAtHome(WorldStateStore store,HouseholdDayPlan source,StreetPopulation streets)
        {
            var game=GameManager.Instance;var room=game.PlayerEnterExit.Interior;
            if(streets.enabled || !room || room.EntryDoor.buildingKey!=source.HomeKey ||
                !ReferenceEquals(typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets),store))
                throw new InvalidOperationException("Family sharing probe requires the existing disabled, correctly owned home fixture.");
            var bodies=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",Private).GetValue(streets);
            ResidentActor first,second;
            if(!bodies.TryGetValue(source.First.ResidentId,out first) || !bodies.TryGetValue(source.Second.ResidentId,out second) ||
                !first || !second || !first.gameObject.activeInHierarchy || !second.gameObject.activeInHierarchy ||
                first.transform.parent!=room.transform || second.transform.parent!=room.transform || first.IsSleeping || second.IsSleeping ||
                !CivilianResponses.Sees(first.transform.position,second.transform.position,6))
                throw new InvalidOperationException("Family sharing needs both existing awake home bodies in actual mutual communication range; no placement was fabricated.");
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var a=streets.GetState(first.Resident.Id,now);var b=streets.GetState(second.Resident.Id,now);
            if(a.Activity!=Activity.Home || b.Activity!=Activity.Home || a.BuildingKey!=source.HomeKey || b.BuildingKey!=source.HomeKey ||
                !streets.HouseholdParentAvailable(first.Resident.Id,now) || !streets.HouseholdParentAvailable(second.Resident.Id,now))
                throw new InvalidOperationException("Family sharing home fixture has no free resolved Home interval for both existing parents.");
            var tick=typeof(StreetPopulation).GetMethod("TickFamilyExperienceSharing",Private);
            var free=typeof(StreetPopulation).GetMethod("FamilyConversationAvailable",Private);
            var population=(SettlementPopulation)typeof(StreetPopulation).GetField("population",Private).GetValue(streets);
            var home=population==null?null:population.Households.Find(x=>x.BuildingKey==source.HomeKey);
            if(tick==null || free==null || home==null ||
                !(bool)free.Invoke(streets,new object[]{first.Resident,home,now}) ||
                !(bool)free.Invoke(streets,new object[]{second.Resident,home,now}))
                throw new InvalidOperationException("Family sharing fixture retains an incompatible cached movement/placement owner; normal gate was not bypassed.");

            string reporter=first.Resident.Id,recipient=second.Resident.Id;
            // Match ordinary native conversation/threat initialization: use the
            // actual saved identity, eligibility, background and appearance seed.
            // No trait selection or relationship value is supplied by the probe.
            foreach(var person in new[]{first.Resident,second.Resident})
            {
                NpcSocialBackground background;
                if(!PeopleOfTheIliacBayMod.RefreshSavedRandomPersonalityEligibility(store,person.Id,out background))
                    throw new InvalidOperationException("Family sharing fixture has no ordinary random-personality authority for its actual parent.");
                store.GetOrCreateNpcPersonality(person.Id,person.AppearanceSeed,background);
            }
            var originalFirst=first.transform.position;var originalSecond=second.transform.position;
            int crimes=store.Snapshot().AlarmIncidents.Count;
            var priorRecipientBoundary=store.ReadNpcConversationBoundary(recipient,now);
            first.SetConversationFacing(true);
            try
            {
                NpcObservedSpeechSession session;NpcObservedSpeechTicket ticket=null;NpcSpeechActClassification classification;
                NpcConversationBoundaryRecord boundary=null;object semanticSource=new object();
                const string utterance="You are disgusting; I will keep making unwanted sexual remarks about you.";
                if(!store.TryBeginNpcObservedSpeechSession(reporter,"family-experience-native",out session) ||
                    !store.TryIssueNpcObservedSpeech(session,1,utterance,now,out ticket) ||
                    !NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.Create(semanticSource,reporter,"player",null,utterance,""),
                        "DirectHarassment",out classification) ||
                    !store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,semanticSource,classification,out boundary) ||
                    store.TryAcceptNpcObservedSpeechAfterAppend(ticket,ticket.Receipt,NpcExperienceAppliedEffectsMask.None)==NpcObservedSpeechAcceptanceResult.Rejected)
                    throw new InvalidOperationException("Controlled semantic harassment setup failed against the actual fixture family identity.");
                string eventId=boundary.ReceiptId;long relationshipRevision=store.NpcRelationshipLedgerRevision;
                if(store.Snapshot().SceneMemories.Exists(x=>x.ObserverId==recipient && x.EventId==eventId))
                    throw new InvalidOperationException("Family recipient learned the new boundary before communication.");
                float holdUntil=Time.realtimeSinceStartup+0.7f;
                while(Time.realtimeSinceStartup<holdUntil)
                {tick.Invoke(streets,new object[]{DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()});yield return null;}
                if(store.Snapshot().SceneMemories.Exists(x=>x.ObserverId==recipient && x.EventId==eventId))
                    throw new InvalidOperationException("A busy relative leaked the experience before a communication opportunity.");
                first.SetConversationFacing(false);
                float deadline=Time.realtimeSinceStartup+3;NpcSceneMemory report=null;
                while(report==null && Time.realtimeSinceStartup<deadline)
                {
                    tick.Invoke(streets,new object[]{DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()});
                    report=store.Snapshot().SceneMemories.Find(x=>x.ObserverId==recipient && x.EventId==eventId);
                    if(report==null)yield return null;
                }
                if(report==null || report.At!=Math.Floor(boundary.ObservedAt) || report.SubjectId!=reporter ||
                    report.ResponsibleId!="player" || report.Valence> -1 || report.Valence< -4 ||
                    !report.Observation.Contains("My partner ") || !report.Observation.Contains("told me") ||
                    !report.Observation.Contains("I did not witness") || !report.Observation.Contains("harassed me"))
                    throw new InvalidOperationException("Production family tick did not deliver the exact attributed harassment report.");
                string context=store.RecallSceneEvents(recipient,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),1200);
                if(!context.Contains(report.Observation))throw new InvalidOperationException("Normal bounded dialogue recall omitted the family report.");
                holdUntil=Time.realtimeSinceStartup+0.7f;
                while(Time.realtimeSinceStartup<holdUntil)
                {tick.Invoke(streets,new object[]{DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()});yield return null;}
                var snapshot=store.Snapshot();
                var recipientBoundary=store.ReadNpcConversationBoundary(recipient,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                if(snapshot.SceneMemories.FindAll(x=>x.ObserverId==recipient && x.EventId==eventId).Count!=1 ||
                    store.NpcRelationshipLedgerRevision!=relationshipRevision || snapshot.AlarmIncidents.Count!=crimes ||
                    (recipientBoundary==null)!=(priorRecipientBoundary==null) || recipientBoundary!=null &&
                    recipientBoundary.ReceiptId!=priorRecipientBoundary.ReceiptId ||
                    first.transform.position!=originalFirst || second.transform.position!=originalSecond)
                    throw new InvalidOperationException("Family report duplicated punishment, crime, refusal, memory or physical movement.");
                var reloaded=NativeReload(store);var retained=reloaded.Snapshot().SceneMemories.Find(x=>x.ObserverId==recipient && x.EventId==eventId);
                if(retained==null || retained.At!=report.At || retained.Valence!=report.Valence || retained.Observation!=report.Observation ||
                    reloaded.ShareFamilyPlayerExperiences(reporter,recipient,source.SettlementId,source.HomeKey,
                        DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())!=0)
                    throw new InvalidOperationException("Native serializer lost family provenance or retained-event deduplication.");
                var traits=store.ReadNpcPersonality(recipient);
                var savedTraits=reloaded.ReadNpcPersonality(recipient);
                if(traits==null || traits.SelectedTraits.Count!=3 || savedTraits==null ||
                    savedTraits.Description()!=traits.Description())
                    throw new InvalidOperationException("Native family report lost the parent's ordinary saved three-trait personality.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_EXPERIENCE_NATIVE_PASS: actualExistingHomePair=true; productionSharingTick=true; "+
                    "busyBeforeContactRejected=true; exactBoundaryReceipt=true; partnerHearsay=true; originalTime=true; ownValence="+report.Valence+
                    "; savedTraitCount="+(traits==null?0:traits.SelectedTraits.Count)+"; normalRecall=true; duplicatePunishment=false; crime=false; nativeSerializer=true; "+
                    "controlledSemanticSetup=true; fullPopulationScheduler=false; freeModelTone=false");
            }
            finally {if(first)first.SetConversationFacing(false);}
        }
    }
}
