using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Detached controlled fixture. Uses the game's serializer, never kills a live actor.
    internal static class CorpseDeparturePersistenceProbe
    {
        internal static void Verify()
        {
            var store=new WorldStateStore(13);
            var town=store.GetOrCreate(1,new[]{7,8},1);
            var victim=town.Households[0].Residents[0];
            string bystander=town.Households[1].Residents[0].Id;
            var alarm=store.RecordViolence(1,victim.Id,100);
            store.LocateViolence(alarm.Id,-1,1,0,2);
            Population.MarkDead(town,victim.Id);
            store.ConfirmCorpse(alarm.Id,100,-1,3,0,4);
            store.ObserveOutdoorCorpse(alarm.Id,false,200);
            var posts=new List<SavedRoutePoint>();for(int i=0;i<8;i++)posts.Add(new SavedRoutePoint{X=i});
            var roster=store.GetOrCreateGuards(1,8,posts);
            var guard=roster.Guards[0];var target=new SavedRoutePoint{X=3,Z=4};
            var routes=new Dictionary<string,List<SavedRoutePoint>>{
                {guard.Id,new List<SavedRoutePoint>{guard.Post,target}}};
            Require(roster.DispatchDepartureInvestigation("outdoor:"+alarm.Id,200,200,routes,
                new List<SavedRoutePoint>{target,new SavedRoutePoint{X=13,Z=4},target},2,10).Count==1,
                "Unwitnessed departure failed to assign an available guard.");
            string earlyJson=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
            var earlyPhysical=new WorldStateStore(0);
            earlyPhysical.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),earlyJson));
            earlyPhysical.GetGuards(1).Advance(230);
            Require(earlyPhysical.ClearOutdoorCorpse(alarm.Id,guard.Id,3,4,230),
                "An arrived guard could not clear the body before the absence timeout.");
            bool earlyStatement;
            Require(earlyPhysical.TryQuestionCorpseCase(alarm.Id,guard.Id,bystander,false,2,true,230,out earlyStatement) &&
                !earlyStatement,"Early physical cleanup did not admit a grounded bystander inquiry.");
            earlyPhysical=RoundTrip(earlyPhysical);
            var earlyReport=earlyPhysical.GetGuards(1).Reports.Find(row=>row.Id=="outdoor:"+alarm.Id);
            var earlyBody=earlyPhysical.Snapshot().AlarmIncidents[0].Corpse;
            Require(earlyReport.HasCorpseInquiryWindow && earlyReport.CorpseInquiryStartedAt==230 &&
                earlyReport.InvestigationUntil==2030 && earlyBody.Inquiries[0].WindowVersion==1,
                "Native serialization lost the cleanup anchor or current-window inquiry provenance.");
            earlyPhysical.ObserveOutdoorCorpse(alarm.Id,true,400);
            Require(earlyPhysical.GetGuards(1).Reports[0].InvestigationUntil==2030,
                "Presence observation renewed an early inquiry window.");
            var witness=earlyPhysical.RegisterNativePerson(1,-1,998,"Controlled witness",ResidentSex.Male,0,0,182,0);
            earlyPhysical.Witness(alarm.Id,witness.Id,110);earlyPhysical.MarkPlayerMurder(alarm.Id);
            Require(earlyPhysical.TryQuestionCorpseCase(alarm.Id,guard.Id,witness.Id,true,2,true,401,out earlyStatement) &&
                earlyStatement && earlyPhysical.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness.Id,401),
                "A real controlled witness receipt did not identify the exact murderer.");
            Require(earlyPhysical.TryObserveCorpseSuspect(alarm.Id,guard.Id,CorpseSuspectKind.Player,"player",3,4,2,true,402),
                "A controlled visible suspect observation was rejected.");
            var searches=earlyPhysical.GetGuards(1);string previous=guard.Id;
            for(int i=1;i<6;i++)
            {
                double now=402+i;string candidate=searches.Guards[i].Id;
                var from=searches.Position(candidate,now);var contact=searches.Position(previous,now);
                double dx=from.X-contact.X,dz=from.Z-contact.Z;GuardRecord recruited;
                Require(searches.TryRecruitCorpseSearchGuard(previous,candidate,now,Math.Sqrt(dx*dx+dz*dz),true,
                    new List<SavedRoutePoint>{from,contact},new List<SavedRoutePoint>{contact,earlyReport.Entrance},
                    2,10,out recruited),"Controlled contact failed to recruit within the original case deadline.");
                previous=candidate;
            }
            earlyPhysical=RoundTrip(earlyPhysical);earlyReport=earlyPhysical.GetGuards(1).Reports[0];
            Require(earlyReport.GuardIds.Count==6 && earlyReport.InvestigationUntil==2030 && earlyReport.ThreatId==null &&
                earlyReport.CorpseSuspect!=null && earlyReport.CorpseSuspect.Kind==CorpseSuspectKind.Player &&
                earlyReport.SuspectSighting!=null && earlyReport.SuspectSighting.At==402 &&
                !earlyPhysical.Snapshot().AlarmIncidents[0].AccusationIssued,
                "Native serialization lost bounded membership, exact identity/sighting or fabricated a crime charge.");
            store=RoundTrip(store);
            store.ObserveOutdoorCorpse(alarm.Id,true,1999);
            Require(!store.Snapshot().AlarmIncidents[0].Corpse.Cleared,
                "A return before thirty minutes removed the body.");
            store.ObserveOutdoorCorpse(alarm.Id,false,2000);
            store=RoundTrip(store);
            store.ObserveOutdoorCorpse(alarm.Id,true,3800);
            var body=store.Snapshot().AlarmIncidents[0].Corpse;
            Require(body.Cleared && body.ClearedAt==3800 && body.Discovery==null,
                "Continuous absence cleanup or witness provenance did not survive native serialization.");
            // Separate first-departure case: interview during the 30-to-60 minute phase.
            var interview=RoundTrip(store);
            // The original first-departure deadline remains 3800, so it cannot be renewed by returning.
            interview.GetGuards(1).Advance(3800);
            Require(interview.GetGuards(1).Guards[0].Duty==GuardDuty.Posted,
                "The departure investigation did not finish by sixty minutes.");
            // Rewind only the detached fixture's guard simulation, using its saved pre-return snapshot.
            var inquiryStore=new WorldStateStore(13);
            inquiryStore.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),earlyJson));
            inquiryStore.GetGuards(1).Advance(2000);
            inquiryStore.ObserveOutdoorCorpse(alarm.Id,true,2000);
            bool statement;
            Require(inquiryStore.TryQuestionCorpseCase(alarm.Id,guard.Id,bystander,false,2,true,2000,out statement) && !statement,
                "A physical bystander inquiry was lost or fabricated witness evidence.");
            inquiryStore=RoundTrip(inquiryStore);
            Require(inquiryStore.Snapshot().AlarmIncidents[0].Corpse.Inquiries.Count==1 &&
                !inquiryStore.Snapshot().AlarmIncidents[0].PlayerAttributed,
                "Inquiry persistence invented player attribution.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CORPSE_DEPARTURE_PERSISTENCE_PASS: controlledFixture=true; nativeSerializer=true; " +
                "absence30=true; quickReturnPreserved=true; guardDeadline60=true; inquiryWithoutInventedWitness=true; " +
                "earlyCleanupAnchor=true; fixedInquiry30=true; sharedCaseSix=true; typedSuspect=true; physicalGameplay=false");
        }
        private static WorldStateStore RoundTrip(WorldStateStore store)
        {
            string json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
            var copy=new WorldStateStore(0);
            copy.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
            Require(json==SaveLoadManager.Serialize(typeof(WorldState),copy.Snapshot()),"Corpse departure native round trip changed state.");
            return copy;
        }
        private static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException(message);}
    }
}
