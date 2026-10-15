using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class TravelerSaveTests
    {
        private static TravelerVisit Visit(string id="visit") { return new TravelerVisit { Id=id,PersonId="traveler:a",SettlementId=1,TavernKey=7,
            DesiredArrival=43200,Nights=2,CommuteSeconds=600,Route=new List<SavedRoutePoint> { new SavedRoutePoint(),new SavedRoutePoint { X=30 } } }; }
        private static WorldStateStore Store()
        {
            var store=new WorldStateStore(42); store.GetOrCreate(1,new int[0]);
            store.RegisterLodging(1,7,new[] { new LodgingRoom { Id="room",Beds=new List<string> { "bed" } } });
            store.RegisterTraveler(new TravelerPerson { Id="traveler:a",Name="Test Visitor",HomeSettlement=2,AppearanceSeed=12,Sex=ResidentSex.Female,Race=1 });
            return store;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
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
                var snapshot=store.Snapshot(); Check.Equal(9,snapshot.Version);
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
            cases.Add(("Malformed traveler booking links fail restore atomically and old schemas gain empty visitors",()=>{
                var store=Store(); store.RegisterTravelerVisit(Visit()); store.TryAdmitTraveler("visit",43800);
                var bad=store.Snapshot(); bad.Lodgings[0].Bookings.Clear();
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
                Check.Equal(TravelerAdmission.Admitted,store.Snapshot().TravelerVisits[0].Admission);
                for(int version=1;version<=8;version++)
                {
                    var old=store.Snapshot(); old.Version=version; old.Travelers=null; old.TravelerVisits=null;
                    var copy=new WorldStateStore(0); copy.Restore(old);
                    Check.Equal(0,copy.Snapshot().Travelers.Count); Check.Equal(0,copy.Snapshot().TravelerVisits.Count);
                }
            }));
        }
    }
}
