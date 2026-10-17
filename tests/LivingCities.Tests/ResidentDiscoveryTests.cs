using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class ResidentDiscoveryTests
    {
        private static List<SavedRoutePoint> Path(double a,double b)
        {return new List<SavedRoutePoint>{new SavedRoutePoint{X=a,Z=0},new SavedRoutePoint{X=b,Z=0}};}
        private static WorldStateStore Fixture(out Resident person,out ResidentTrip trip)
        {
            var store=new WorldStateStore(12);
            person=store.ReserveHousehold(100,new HomeProfile(10,1,100)).Residents[0];
            trip=ResidentTrip.Create(person.Id,100,200,100,600,100,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
            trip.HomeRoute=Path(0,10);trip.HomeWalkSeconds=20;
            trip.Destination=new ResidentDestination {Stops=new List<ResidentVisitStop>{
                new ResidentVisitStop{BuildingKey=20,Purpose=ResidentVisitPurpose.Shop,WalkSeconds=10,DwellSeconds=60,Route=Path(0,10)}},
                ExitRoute=Path(10,0),ExitWalkSeconds=10};
            trip=ResidentTrip.CopyValidated(trip);return store;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Discovery departure days are bounded by journey duration and never lie in the future",()=>{
                var days=ResidentDiscovery.CandidateDepartureDays(20*86400+10*3600,6*86400);
                Check.Equal(2,days.Count);Check.Equal(14,days[0]);Check.Equal(15,days[1]);
                days=ResidentDiscovery.CandidateDepartureDays(20*86400+10*3600,3600);
                Check.Equal(1,days.Count);Check.Equal(20,days[0]);
                days=ResidentDiscovery.CandidateDepartureDays(100,6*86400);
                Check.Equal(0,days.Count);
                Check.Throws<ArgumentException>(()=>ResidentDiscovery.CandidateDepartureDays(double.NaN,100));
                Check.Throws<ArgumentException>(()=>ResidentDiscovery.CandidateDepartureDays(100,0));
                var store=new WorldStateStore(1);Check.True(store.CanDiscoverResidentTrips(100));
                store.ReserveHousehold(100,new HomeProfile(10,1,100));Check.True(store.CanDiscoverResidentTrips(100));
                store.GetOrCreateFamilies(100,new[]{new HomeProfile(10,1,100)});Check.True(!store.CanDiscoverResidentTrips(100));
            }));
            cases.Add(("Discovery preserves one original outbound resident and future destination after restore",()=>{
                var store=Fixture(out var person,out var trip);
                Check.True(store.DiscoverOutboundResidentTrip(trip,150));
                Check.True(!store.DiscoverOutboundResidentTrip(trip,151));
                var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
                Check.Equal(1,restored.Snapshot().ResidentTrips.Count);
                Check.True(restored.IsResidentAway(person.Id,150));
                Check.Equal(ResidentTripPhase.Outbound,restored.ResidentTripAt(person.Id,150).Phase);
                Check.True(!restored.IsResidentAway(person.Id,trip.HomeAt));
                Check.True(!restored.DiscoverOutboundResidentTrip(trip,150));
            }));
            cases.Add(("Discovery rejects future local visiting returning and finished phases without mutation",()=>{
                foreach(double now in new[]{0d,100d,119d,220d,250d,310d,10000d})
                {
                    var store=Fixture(out var person,out var trip);
                    Check.True(!store.DiscoverOutboundResidentTrip(trip,now));
                    Check.Equal(0,store.Snapshot().ResidentTrips.Count);
                }
                var boundary=Fixture(out var p,out var t);Check.True(boundary.DiscoverOutboundResidentTrip(t,t.RoadLeavesAt));
            }));
            cases.Add(("Discovery cannot rewrite a populated hometown dead resident child or prior trip",()=>{
                var known=Fixture(out var p,out var t);known.GetOrCreateFamilies(100,new[]{new HomeProfile(10,1,100)});
                Check.True(!known.DiscoverOutboundResidentTrip(t,150));
                var dead=Fixture(out p,out t);p.IsAlive=false;Check.True(!dead.DiscoverOutboundResidentTrip(t,150));
                var child=Fixture(out p,out t);p.AgeGroup=ResidentAgeGroup.Child;Check.True(!child.DiscoverOutboundResidentTrip(t,150));
                var prior=Fixture(out p,out t);var old=ResidentTrip.CopyValidated(t);old.DepartsAt=0;
                prior.ScheduleResidentTrip(old);t.DepartsAt=1000;t=ResidentTrip.CopyValidated(t);
                Check.True(!prior.DiscoverOutboundResidentTrip(t,1050));
                var protectedPerson=Fixture(out p,out t);p.IsQuestProtected=true;
                Check.True(!protectedPerson.DiscoverOutboundResidentTrip(t,150));
            }));
            cases.Add(("Discovery requires complete connectors and valid current time",()=>{
                var store=Fixture(out var p,out var t);t.Destination=null;
                Check.True(!store.DiscoverOutboundResidentTrip(t,150));
                store=Fixture(out p,out t);t.HomeRoute.Clear();t.HomeWalkSeconds=0;
                Check.True(!store.DiscoverOutboundResidentTrip(t,150));
                Check.Throws<ArgumentException>(()=>store.DiscoverOutboundResidentTrip(t,double.NaN));
                Check.Throws<ArgumentException>(()=>store.DiscoverOutboundResidentTrip(t,-1));
            }));
        }
    }
}
