using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class TravelerSaveTests
    {
        private static TravelerVisit Visit(string id="visit") { return new TravelerVisit { Id=id,PersonId="traveler:a",SettlementId=1,TavernKey=7,
            DesiredArrival=43200,Nights=2,CommuteSeconds=600,Route=new List<SavedRoutePoint> { new SavedRoutePoint(),new SavedRoutePoint { X=30 } } }; }
        private static WorldStateStore Store()
        {
            var store=new WorldStateStore(42); store.GetOrCreate(1,new int[0]);
            store.RegisterLodgingBeds(1,7,new[] { new LodgingBed {Id="bed"} });
            store.RegisterTraveler(new TravelerPerson { Id="traveler:a",Name="Test Visitor",HomeSettlement=2,AppearanceSeed=12,Sex=ResidentSex.Female,Race=1 });
            return store;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            AddBoundActionAbsenceCase(cases);
            cases.Add(("Investigated inn refuses traveler without booking and reopens after occupation",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit());
                var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint()}); var guard=roster.Guards[0];
                roster.CacheBuildingApproach(7,new SavedRoutePoint{X=10},new Dictionary<string,List<SavedRoutePoint>> {
                    {guard.Id,new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=10}}} });
                roster.DispatchBuilding("inn-investigation",7,43700,1,1,10,true);
                roster.Advance(43800); roster.EnterBuilding(guard.Id,7,43800,new GuardPoint3());
                Check.True(!store.TryAdmitTraveler("visit",43800));
                Check.Equal(TravelerAdmission.Refused,store.Snapshot().TravelerVisits[0].Admission);
                Check.Equal(0,store.Snapshot().Lodgings[0].Bookings.Count);
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Equal(TravelerPhase.Leaving,restored.TravelerJourneyFor("visit").Evaluate(43801).Phase);
                var later=Visit("later"); later.DesiredArrival=48000; restored.RegisterTravelerVisit(later);
                Check.True(restored.TryAdmitTraveler("later",48600));
            }));
            cases.Add(("Saved refusal retains unresolved shelter search without reserving a bed",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit());
                double intendedExit=store.TravelerJourneyFor("visit").ExitAt;
                store.TryBookLodging(1,7,"full","player",1,true,1,43000,300000);
                long revision=store.TravelerVisitRevision;
                store.AdvanceTravelerAdmissions(1,44000,8);
                Check.True(store.TravelerVisitRevision>revision);
                Check.Equal(44400d,store.TravelerJourneyFor("visit").ExitAt);
                Check.True(store.TravelerBed("visit",44000)==null);
                store.Restore(store.Snapshot());
                Check.Equal(TravelerPhase.Leaving,store.TravelerJourneyFor("visit").Evaluate(44000).Phase);
                Check.Equal(1,store.TravelerVisitsDuring(1,86400,172800).Count);
                Check.True(store.TravelerAwaitingLodgingDecision("visit",86400));
                Check.True(store.IsResidentAway("traveler:a",86400));
                Check.True(store.TravelerBed("visit",86400)==null);
                Check.Equal(1,store.Snapshot().Lodgings[0].Bookings.Count);
                Check.Equal("full",store.Snapshot().Lodgings[0].Bookings[0].Id);
                Check.True(!store.TravelerAwaitingLodgingDecision("visit",intendedExit+1));
                Check.Equal(0,store.TravelerVisitsDuring(1,intendedExit+1,intendedExit+3600).Count);
            }));
            cases.Add(("Traveler physical bed requires an alive paid guest within the booked stay",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit());
                Check.True(store.TravelerBed("visit",44000)==null);
                store.AdvanceTravelerAdmissions(1,44000,8);
                Check.True(store.TravelerBed("visit",43799)==null);
                Check.Equal("bed",store.TravelerBed("visit",44000));
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Equal("bed",restored.TravelerBed("visit",44000));
                Check.True(restored.TravelerBed("visit",restored.TravelerJourneyFor("visit").CheckOutAt)==null);
                store.KillTraveler("traveler:a",45000);
                Check.True(store.TravelerBed("visit",45000)==null);
            }));
            cases.Add(("Paid traveler survives decimal serialization at real Daggerfall calendar epochs",()=>{
                var store=Store(); var visit=Visit(); visit.DesiredArrival=12597573702; visit.CommuteSeconds=6990.40364950249;
                store.RegisterTravelerVisit(visit); store.AdvanceTravelerAdmissions(1,12597581000,8);
                var saved=store.Snapshot();
                Func<double,double> round=value=>double.Parse(value.ToString("G15",System.Globalization.CultureInfo.InvariantCulture),System.Globalization.CultureInfo.InvariantCulture);
                saved.TravelerVisits[0].AttemptedAt=round(saved.TravelerVisits[0].AttemptedAt);
                saved.Lodgings[0].Bookings[0].StartsAt=round(saved.Lodgings[0].Bookings[0].StartsAt);
                store.Restore(saved);
                Check.Equal(TravelerAdmission.Admitted,store.Snapshot().TravelerVisits[0].Admission);
                var bad=store.Snapshot(); bad.Lodgings[0].Bookings[0].StartsAt+=1;
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
            }));
            cases.Add(("Traveler admission only allocates the inn whose native receipts were reconciled",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit());
                store.RegisterLodgingBeds(1,8,new[]{new LodgingBed {Id="other-bed"}});
                store.RegisterTraveler(new TravelerPerson { Id="traveler:b",Name="Other Visitor",HomeSettlement=3 });
                var other=Visit("other-visit"); other.PersonId="traveler:b"; other.TavernKey=8;
                store.RegisterTravelerVisit(other);
                Check.True(store.HasDueTravelerAdmission(1,7,50000));
                Check.Equal(1,store.AdvanceTravelerAdmissions(1,50000,1,7));
                Check.True(!store.HasDueTravelerAdmission(1,7,50000));
                Check.True(store.HasDueTravelerAdmission(1,8,50000));
                Check.Equal(TravelerAdmission.Pending,store.Snapshot().TravelerVisits.Find(v=>v.Id=="other-visit").Admission);
                Check.True(store.ReconcilePlayerLodging(1,8,"other-bed",43000,60000));
                Check.Equal(1,store.AdvanceTravelerAdmissions(1,50000,10,8));
                Check.Equal(TravelerAdmission.Refused,store.Snapshot().TravelerVisits.Find(v=>v.Id=="other-visit").Admission);
            }));
            cases.Add(("Traveler runtime window excludes history and respects entry and exit boundaries",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit());
                var journey=store.TravelerJourneyFor("visit");
                Check.Equal(0,store.TravelerVisitsDuring(1,0,journey.EntryAt).Count);
                Check.Equal(1,store.TravelerVisitsDuring(1,journey.EntryAt,journey.EntryAt+1).Count);
                Check.Equal(1,store.TravelerVisitsDuring(1,journey.ExitAt-1,journey.ExitAt).Count);
                Check.Equal(0,store.TravelerVisitsDuring(1,journey.ExitAt,journey.ExitAt+86400).Count);
                Check.Equal(0,store.TravelerVisitsDuring(2,0,journey.ExitAt).Count);
                Check.Throws<ArgumentException>(()=>store.TravelerVisitsDuring(1,2,1));
                long revision=store.TravelerVisitRevision;
                var later=Visit("later"); later.DesiredArrival=journey.ExitAt+86400;
                store.RegisterTravelerVisit(later);
                Check.True(store.TravelerVisitRevision>revision);
                revision=store.TravelerVisitRevision;
                store.Restore(store.Snapshot());
                Check.True(store.TravelerVisitRevision>revision);
                Check.Equal("later",store.TravelerVisitsDuring(1,later.DesiredArrival,later.DesiredArrival+86400)[0].Id);
            }));
            cases.Add(("Nonlocal travelers participate in civilian murder witnesses and corpse persistence",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit());
                store.RegisterTraveler(new TravelerPerson { Id="traveler:b",Name="Witness Visitor",HomeSettlement=3,AppearanceSeed=7 });
                var witnessVisit=Visit("witness"); witnessVisit.PersonId="traveler:b"; store.RegisterTravelerVisit(witnessVisit);
                var alarm=store.RecordViolence(1,"traveler:a",44000);
                store.LocateViolence(alarm.Id,-1,2,0,3);
                Check.True(store.Witness(alarm.Id,"traveler:b",44000).DirectWitness);
                Check.True(store.KillTraveler("traveler:a",44000));
                store.ConfirmCorpse(alarm.Id,44000);
                store.NoticeOutdoorCorpse(alarm.Id,"traveler:b",false,44001);
                var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                Check.True(!copy.Snapshot().Travelers.Find(p=>p.Id=="traveler:a").IsAlive);
                Check.Equal("traveler:b",copy.Snapshot().AlarmIncidents[0].Corpse.Discovery.ObserverId);
                Check.True(copy.Snapshot().AlarmIncidents[0].People[0].DirectWitness);
                Check.Equal(0,copy.Snapshot().Settlements[0].Households.Count);
                var invalid=copy.Snapshot(); invalid.TravelerVisits.RemoveAll(v=>v.PersonId=="traveler:b");
                Check.Throws<ArgumentException>(()=>copy.Restore(invalid));
            }));
            cases.Add(("Traveler crime participation requires a destination visit and dead visitors cannot witness",()=>{
                var store=Store();
                Check.Throws<ArgumentException>(()=>store.RecordViolence(1,"traveler:a",44000));
                store.RegisterTravelerVisit(Visit());
                var threat=store.RecordHostileThreat(1,"attacker",44000);
                var person=store.Witness(threat.Id,"traveler:a",44000);
                Check.Equal(CivilianReaction.Flee,person.Reaction);
                store.KillTraveler("traveler:a",44001);
                Check.True(store.Witness(threat.Id,"traveler:a",44002)==null);
                var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                Check.Equal(CivilianReaction.Flee,copy.Snapshot().AlarmIncidents[0].People[0].Reaction);
                store.GetOrCreate(9,new int[0]);
                Check.Throws<ArgumentException>(()=>store.RecordViolence(9,"traveler:a",44002));
            }));
            cases.Add(("Traveler murder cannot acquire a backdated death through mutation or restore",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit());
                var alarm=store.RecordViolence(1,"traveler:a",44000);
                store.LocateViolence(alarm.Id,-1,2,0,3);
                Check.Throws<ArgumentException>(()=>store.KillTraveler("traveler:a",43999));
                Check.True(store.Snapshot().Travelers[0].IsAlive);
                store.KillTraveler("traveler:a",44000); store.ConfirmCorpse(alarm.Id,44000);
                var bad=store.Snapshot(); bad.Travelers[0].DiedAt=43999;
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
                Check.Equal(44000d,store.Snapshot().Travelers[0].DiedAt);
            }));
            cases.Add(("Traveler catch-up admits earliest arrivals independent of registration order and batch size",()=>{
                var store=Store();
                store.RegisterTraveler(new TravelerPerson { Id="traveler:b",Name="Second Visitor",HomeSettlement=3 });
                var later=Visit("later"); later.PersonId="traveler:b"; later.DesiredArrival+=60;
                store.RegisterTravelerVisit(later); store.RegisterTravelerVisit(Visit("earlier"));
                Check.Equal(1,store.AdvanceTravelerAdmissions(1,50000,1));
                var snapshot=store.Snapshot();
                Check.Equal(TravelerAdmission.Admitted,snapshot.TravelerVisits.Find(v=>v.Id=="earlier").Admission);
                Check.Equal(TravelerAdmission.Pending,snapshot.TravelerVisits.Find(v=>v.Id=="later").Admission);
                Check.Equal(43800d,snapshot.TravelerVisits.Find(v=>v.Id=="earlier").AttemptedAt);
                var copy=new WorldStateStore(0); copy.Restore(snapshot);
                Check.Equal(1,copy.AdvanceTravelerAdmissions(1,50000,1));
                Check.Equal(TravelerAdmission.Refused,copy.Snapshot().TravelerVisits.Find(v=>v.Id=="later").Admission);
                Check.Equal(0,copy.AdvanceTravelerAdmissions(1,50000,1));
                Check.Equal(1,copy.Snapshot().Lodgings[0].Bookings.Count);
            }));
            cases.Add(("Traveler catch-up retains paid player priority and historical outcomes after long skips",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit());
                store.TryBookLodging(1,7,"paid","player",1,true,1,40000,45000);
                Check.Equal(0,store.AdvanceTravelerAdmissions(1,43799,10));
                Check.Equal(1,store.AdvanceTravelerAdmissions(1,20*86400,10));
                var snapshot=store.Snapshot();
                Check.Equal(TravelerAdmission.Refused,snapshot.TravelerVisits[0].Admission);
                Check.Equal(43800d,snapshot.TravelerVisits[0].AttemptedAt);
                Check.Equal("paid",snapshot.Lodgings[0].Bookings[0].Id);
                Check.Equal(0,store.AdvanceTravelerAdmissions(1,30*86400,10));
                Check.Throws<ArgumentException>(()=>store.AdvanceTravelerAdmissions(1,double.NaN,1));
                Check.Throws<ArgumentException>(()=>store.AdvanceTravelerAdmissions(1,50000,0));
            }));
            cases.Add(("Traveler catch-up uses stable ties and skips unsurveyed inns without mutating them",()=>{
                var store=Store();
                store.RegisterTraveler(new TravelerPerson { Id="traveler:b",Name="Second Visitor",HomeSettlement=3 });
                var b=Visit("b"); b.PersonId="traveler:b";
                store.RegisterTravelerVisit(b); store.RegisterTravelerVisit(Visit("a"));
                store.RegisterTraveler(new TravelerPerson { Id="traveler:c",Name="Unsupplied Visitor",HomeSettlement=4 });
                var c=Visit("c"); c.PersonId="traveler:c"; c.TavernKey=8; c.DesiredArrival=40000;
                store.RegisterTravelerVisit(c);
                Check.Equal(2,store.AdvanceTravelerAdmissions(1,50000,10));
                var snapshot=store.Snapshot();
                Check.Equal(TravelerAdmission.Admitted,snapshot.TravelerVisits.Find(v=>v.Id=="a").Admission);
                Check.Equal(TravelerAdmission.Refused,snapshot.TravelerVisits.Find(v=>v.Id=="b").Admission);
                Check.Equal(TravelerAdmission.Pending,snapshot.TravelerVisits.Find(v=>v.Id=="c").Admission);
            }));
            cases.Add(("Traveler death cannot precede admission and malformed chronology restores atomically",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit()); store.TryAdmitTraveler("visit",43800);
                Check.Throws<ArgumentException>(()=>store.KillTraveler("traveler:a",43799));
                Check.True(store.Snapshot().Travelers[0].IsAlive);
                var bad=store.Snapshot(); bad.Travelers[0].IsAlive=false; bad.Travelers[0].DiedAt=43799;
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
                Check.True(store.Snapshot().Travelers[0].IsAlive);
                Check.True(store.KillTraveler("traveler:a",43800));
                var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                Check.Equal(43800d,copy.Snapshot().Travelers[0].DiedAt);
            }));
            cases.Add(("Traveler admission reserves exact stay once and survives restoration",()=>{
                var store=Store(); var visit=Visit(); store.RegisterTravelerVisit(visit);
                visit.Route[1].X=999;
                Check.True(!store.TryAdmitTraveler("visit",43200));
                Check.Equal(TravelerAdmission.Pending,store.Snapshot().TravelerVisits[0].Admission);
                Check.True(store.TryAdmitTraveler("visit",43800));
                Check.True(store.TryAdmitTraveler("visit",43801));
                var snapshot=store.Snapshot(); Check.Equal(new WorldState().Version,snapshot.Version);
                Check.Equal(1,snapshot.Lodgings[0].Bookings.Count);
                var booking=snapshot.Lodgings[0].Bookings[0];
                Check.Equal("traveler:a",booking.GuestId); Check.Equal(43800d,booking.StartsAt);
                Check.Equal(30d,snapshot.TravelerVisits[0].Route[1].X);
                Check.True(store.IsLodgingBedReserved(1,7,"bed",50000,"player"));
                var copy=new WorldStateStore(0); copy.Restore(snapshot);
                copy.RegisterTravelerVisit(Visit()); Check.True(copy.TryAdmitTraveler("visit",50001));
                Check.Equal(1,copy.Snapshot().Lodgings[0].Bookings.Count);
            }));
            cases.Add(("Rejected traveler remains rejected after vacancy and reload",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit());
                store.TryBookLodging(1,7,"paid","player",1,true,1,40000,45000);
                Check.True(!store.TryAdmitTraveler("visit",43800));
                var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                Check.True(!copy.TryAdmitTraveler("visit",50000));
                Check.Equal(TravelerAdmission.Refused,copy.Snapshot().TravelerVisits[0].Admission);
                Check.Equal(1,copy.Snapshot().Lodgings[0].Bookings.Count);
            }));
            cases.Add(("Traveler death persists without revival or silently releasing a corpse's bed",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit()); store.TryAdmitTraveler("visit",43800);
                Check.True(store.KillTraveler("traveler:a",45000)); Check.True(!store.KillTraveler("traveler:a",45001));
                var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                Check.True(!copy.Snapshot().Travelers[0].IsAlive); Check.Equal(45000d,copy.Snapshot().Travelers[0].DiedAt);
                Check.True(copy.IsLodgingBedReserved(1,7,"bed",50000,"player"));
                copy.RegisterTraveler(new TravelerPerson { Id="traveler:a",Name="Test Visitor",HomeSettlement=2,AppearanceSeed=12,Sex=ResidentSex.Female,Race=1 });
                Check.True(!copy.Snapshot().Travelers[0].IsAlive);
                var later=Visit("later"); later.DesiredArrival=10*86400;
                Check.Throws<ArgumentException>(()=>copy.RegisterTravelerVisit(later));
            }));
            cases.Add(("Traveler local origins overlapping visits and changed identities are rejected",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit());
                Check.Throws<ArgumentException>(()=>store.RegisterTravelerVisit(Visit("overlap")));
                var local=new TravelerPerson { Id="traveler:local",Name="Local",HomeSettlement=1 };
                store.RegisterTraveler(local); var visit=Visit("local"); visit.PersonId=local.Id;
                Check.Throws<ArgumentException>(()=>store.RegisterTravelerVisit(visit));
                var changed=Visit(); changed.Nights=3;
                Check.Throws<ArgumentException>(()=>store.RegisterTravelerVisit(changed));
            }));
            cases.Add(("Malformed traveler booking links fail restore atomically",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit()); store.TryAdmitTraveler("visit",43800);
                var bad=store.Snapshot(); bad.Lodgings[0].Bookings.Clear();
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
                Check.Equal(TravelerAdmission.Admitted,store.Snapshot().TravelerVisits[0].Admission);
            }));
        }
        internal static void AddBoundActionAbsenceCase(List<(string Name,Action Body)> cases)
        {
            cases.Add(("An exactly observed current traveler visit does not self-block its local action",()=>{
                var store=Store();store.RegisterTravelerVisit(Visit());
                var journey=store.TravelerJourneyFor("visit");double now=journey.CheckInAt+1;
                Check.True(store.IsResidentAway("traveler:a",now),"The visitor must remain away from their origin.");
                var method=typeof(WorldStateStore).GetMethod("IsResidentAway",new[]{typeof(string),typeof(double),typeof(string)});
                Func<string,bool> blocked=visit=>method==null?store.IsResidentAway("traveler:a",now):
                    (bool)method.Invoke(store,new object[]{"traveler:a",now,visit});
                Check.True(!blocked("visit"),"The exact present destination visit wrongly interrupts an admitted local action.");
                Check.True(blocked(null),"Unbound absence must remain protected.");
                Check.True(blocked("another-visit"),"A different journey must remain protected.");
                store.KillTraveler("traveler:a",now);
                Check.True(blocked("visit"),"A dead traveler must not obtain local action ownership.");
            }));
        }
    }
}
