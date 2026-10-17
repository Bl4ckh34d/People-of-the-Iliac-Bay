using System;
using System.Collections.Generic;
using System.Text.Json;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class ResidentTripTests
    {
        private static WorldStateStore Store(out Resident person)
        {
            var store=new WorldStateStore(12);
            person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
            return store;
        }
        private static ResidentTrip Trip(string id,double start=100)
        {
            return ResidentTrip.Create(id,100,200,start,600,100,
                new[]{new RoutePoint(10,10),new RoutePoint(11,10),new RoutePoint(11,11)});
        }
        private static ResidentTrip ObservedTrip(string id)
        {
            var trip=ResidentTrip.Create(id,100,200,100,600,100,
                new[]{new RoutePoint(10,10),new RoutePoint(11,10),new RoutePoint(11,11)});
            trip.Destination=new ResidentDestination {Stops=new List<ResidentVisitStop> {
                new ResidentVisitStop {BuildingKey=10,Purpose=ResidentVisitPurpose.Shop,OpenMinute=0,CloseMinute=0,
                    WalkSeconds=60,DwellSeconds=120,Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=0,Z=0},new SavedRoutePoint{X=10,Z=0}}},
                new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Guild,OpenMinute=0,CloseMinute=0,
                    WalkSeconds=60,DwellSeconds=120,Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=10,Z=0},new SavedRoutePoint{X=20,Z=0}}}},
                ExitRoute=new List<SavedRoutePoint>{new SavedRoutePoint{X=20,Z=0},new SavedRoutePoint{X=0,Z=0}},ExitWalkSeconds=120};
            return ResidentTrip.CopyValidated(trip);
        }
        private static ObservedVisit Checkpoint(ResidentTrip trip,double observedAt,double plannedEndAt,double routeMetres,double secondsPerMetre)
        {
            return ObservedVisit.Copy(new ObservedVisit {ResidentId=trip.ResidentId,SettlementId=trip.DestinationSettlement,
                BuildingKey=10,ObservedAt=observedAt,PlannedEndAt=plannedEndAt,ResumeUntil=trip.HomeAt,
                SecondsPerMetre=secondsPerMetre,Legs=new List<ObservedVisitLeg> {new ObservedVisitLeg {
                    Route=new List<GuardPoint3>{new GuardPoint3{X=0,Y=1,Z=0},new GuardPoint3{X=routeMetres,Y=1,Z=0}},
                    AfterPhase=ShopVisitPhase.Complete}}});
        }
        private static void ObservedVisitCompletesLate()
        {
            var trip=ObservedTrip("observed-late");var checkpoint=Checkpoint(trip,400,480,10,20);
            var before=trip.Evaluate(390,checkpoint);Check.Equal(ResidentTripPhase.Visiting,before.Phase);
            var inside=trip.Evaluate(500,checkpoint);Check.Equal(ResidentTripPhase.Visiting,inside.Phase);
            Check.Equal(10,inside.BuildingKey);Check.Equal(ResidentVisitPhase.Inside,inside.VisitPhase);
            Check.Near(5,inside.Position.X);Check.Near(1,inside.InteriorY);
            var completed=trip.Evaluate(checkpoint.EndsAt,checkpoint);
            Check.Equal(ResidentTripPhase.Visiting,completed.Phase);Check.Equal(-1,completed.BuildingKey);
            Check.Equal(ResidentVisitPurpose.Guild,completed.VisitPurpose);
            Check.Near(10,completed.Position.X);
            var later=trip.Evaluate(checkpoint.EndsAt+30,checkpoint);
            Check.Equal(-1,later.BuildingKey);Check.Equal(ResidentVisitPurpose.Guild,later.VisitPurpose);Check.Near(15,later.Position.X);
        }
        private static void ObservedVisitCompletesEarly()
        {
            var trip=ObservedTrip("observed-early");var checkpoint=Checkpoint(trip,400,480,1,10);
            Check.True(checkpoint.EndsAt<checkpoint.PlannedEndAt);
            var completed=trip.Evaluate(checkpoint.EndsAt,checkpoint);
            Check.Equal(ResidentTripPhase.Visiting,completed.Phase);Check.Equal(-1,completed.BuildingKey);
            Check.Equal(ResidentVisitPurpose.Guild,completed.VisitPurpose);
            Check.Near(10,completed.Position.X);
        }
        private static void ObservedVisitUpdatesStoreQueriesAndAbsence()
        {
            var store=Store(out var person);store.GetOrCreate(200,new[]{10},1);
            var trip=ObservedTrip(person.Id);store.ScheduleResidentTrip(trip);
            var checkpoint=Checkpoint(trip,400,480,10,20);store.StoreObservedVisit(checkpoint);
            var state=store.ResidentTripAt(person.Id,500);
            Check.Equal(10,state.BuildingKey);Check.Near(5,state.Position.X);
            Check.Equal(1,store.DestinationResidents(200,500).Count);
            Check.True(store.TryDestinationResidentState(person.Id,200,500,out var activity));
            Check.Equal(Activity.Visit,activity.Activity);Check.Near(5,activity.Position.X);
            Check.True(store.IsResidentAway(person.Id,1000));
            Check.Equal(1,store.RoadResidents(1000).Count);
            Check.Near(trip.HomeAt,store.ResidentTripResumeUntil(person.Id,-1));
            Check.True(!store.IsResidentAway(person.Id,1100));
            Check.Equal(ResidentTripPhase.Home,store.ResidentTripAt(person.Id,1100).Phase);
        }
        private static void ObservedVisitBlocksOverlapAndSurvivesRejectedReplacement()
        {
            var store=Store(out var person);store.GetOrCreate(200,new[]{10},1);
            var trip=ObservedTrip(person.Id);store.ScheduleResidentTrip(trip);
            var checkpoint=Checkpoint(trip,400,480,10,20);store.StoreObservedVisit(checkpoint);
            Check.Throws<ArgumentException>(()=>store.ScheduleResidentTrip(Trip(person.Id,trip.HomeAt+60)));
            Check.True(store.GetObservedVisit(person.Id)!=null);
            Check.Near(trip.HomeAt,store.Snapshot().ResidentTrips[0].HomeAt);
        }
        private static void ObservedVisitIsRemovedAfterSuccessfulTripReplacement()
        {
            var store=Store(out var person);store.GetOrCreate(200,new[]{10},1);
            var trip=ObservedTrip(person.Id);store.ScheduleResidentTrip(trip);
            store.StoreObservedVisit(Checkpoint(trip,400,480,10,20));
            store.ScheduleResidentTrip(Trip(person.Id,trip.HomeAt+120));
            Check.True(store.GetObservedVisit(person.Id)==null);
            Check.Equal(0,store.Snapshot().ObservedVisits.Count);
            Check.Equal(trip.HomeAt+120,store.Snapshot().ResidentTrips[0].DepartsAt);
        }
        private static void ObservedVisitIsRemovedAfterSuccessfulServiceExit()
        {
            var store=Store(out var person);store.GetOrCreate(200,new[]{10},1);
            var trip=ObservedTrip(person.Id);store.ScheduleResidentTrip(trip);
            store.StoreObservedVisit(Checkpoint(trip,400,480,10,20));
            store.SaveResidentServiceExit(person.Id,new ResidentServiceExit {VisitId="destination/0",SettlementId=200,
                BuildingKey=10,VisitEnterAt=360,VisitLeaveAt=480,StartedAt=400,SecondsPerMetre=10,
                Route=new List<GuardPoint3>{new GuardPoint3{X=0,Y=1,Z=0},new GuardPoint3{X=1,Y=1,Z=0}}});
            Check.True(store.GetObservedVisit(person.Id)==null);
            Check.Equal(0,store.Snapshot().ObservedVisits.Count);
            Check.Equal(1,store.Snapshot().ResidentTrips[0].ServiceExits.Count);
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Observed foreign shop visit holds interior position and resumes next itinerary stop at actual completion",ObservedVisitCompletesLate));
            cases.Add(("Observed foreign shop visit resumes at planned door time when actual route completes early",ObservedVisitCompletesEarly));
            cases.Add(("Observed foreign shop visit updates store queries and extends household absence",ObservedVisitUpdatesStoreQueriesAndAbsence));
            cases.Add(("Observed foreign visit extension blocks overlapping trip and survives rejected replacement",ObservedVisitBlocksOverlapAndSurvivesRejectedReplacement));
            cases.Add(("Successful resident trip replacement removes stale observed visit checkpoint",ObservedVisitIsRemovedAfterSuccessfulTripReplacement));
            cases.Add(("Successful service exit removes observed visit checkpoint",ObservedVisitIsRemovedAfterSuccessfulServiceExit));
            cases.Add(("Adjacent settlement trips allow both ends trimmed on one cardinal or diagonal road edge",()=>{
                foreach(var pair in new[]{
                    new[]{new RoutePoint(10.2,10),new RoutePoint(10.8,10)},
                    new[]{new RoutePoint(10.8,10),new RoutePoint(10.2,10)},
                    new[]{new RoutePoint(10,10.2),new RoutePoint(10,10.8)},
                    new[]{new RoutePoint(10.2,10.2),new RoutePoint(10.8,10.8)},
                    new[]{new RoutePoint(10.2,10.8),new RoutePoint(10.8,10.2)}})
                {
                    var store=Store(out var person);
                    var trip=ResidentTrip.Create(person.Id,100,200,100,600,100,pair);
                    double dx=pair[1].X-pair[0].X,dz=pair[1].Z-pair[0].Z;
                    Check.Near(Math.Sqrt(dx*dx+dz*dz)*100,trip.TravelSeconds);
                    store.ScheduleResidentTrip(trip);
                    var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                    var midpoint=restored.ResidentTripAt(person.Id,100+trip.TravelSeconds/2);
                    Check.Near((pair[0].X+pair[1].X)/2,midpoint.Position.X);
                    Check.Near((pair[0].Z+pair[1].Z)/2,midpoint.Position.Z);
                    Check.True(restored.IsResidentAway(person.Id,trip.ArrivesAt));
                    Check.True(!restored.IsResidentAway(person.Id,trip.HomeAt));
                }
            }));
            cases.Add(("Two trimmed road ends cannot invent an off-grid edge or hide a crossed road node",()=>{
                foreach(var pair in new[]{
                    new[]{new RoutePoint(10.2,10.1),new RoutePoint(10.8,10.1)},
                    new[]{new RoutePoint(10.2,10.3),new RoutePoint(10.8,10.9)},
                    new[]{new RoutePoint(10.9,10),new RoutePoint(11.1,10)},
                    new[]{new RoutePoint(10.9,10.9),new RoutePoint(11.1,11.1)},
                    new[]{new RoutePoint(10.2,10),new RoutePoint(10.8,10.2)}})
                    Check.Throws<ArgumentException>(()=>ResidentTrip.Create("invalid",100,200,100,600,100,pair));
            }));
            cases.Add(("Road endpoint trimming preserves road alignment and gate crossing time",()=>{
                var trip=ResidentTrip.Create("trim",100,200,5*3600,600,100,
                    new[]{new RoutePoint(10.25,10),new RoutePoint(11,10),new RoutePoint(11,11)});
                Check.Near(175,trip.TravelSeconds);
                trip.HomeRoute.Add(new SavedRoutePoint {X=0,Z=0}); trip.HomeRoute.Add(new SavedRoutePoint {X=100,Z=0});
                trip.HomeWalkSeconds=600; trip.HomeGateWalkSeconds=300; trip.HomeWalled=true;
                trip=ResidentTrip.CopyValidated(trip);
                Check.Near(6*3600-300,trip.HomeLeavesAt); Check.Near(6*3600+300,trip.RoadLeavesAt);
                Check.Throws<ArgumentException>(()=>ResidentTrip.Create("bad",100,200,0,600,100,
                    new[]{new RoutePoint(10.25,10.1),new RoutePoint(11,10)}));
                trip.HomeGateWalkSeconds=601; Check.Throws<ArgumentException>(()=>ResidentTrip.CopyValidated(trip));
            }));
            cases.Add(("Resident home connector walks both ways and keeps the household absent until the door",()=>{
                var store=Store(out var person); var trip=Trip(person.Id);
                trip.HomeRoute.Add(new SavedRoutePoint {X=-10,Z=2});
                trip.HomeRoute.Add(new SavedRoutePoint {X=0,Z=2}); trip.HomeWalkSeconds=600;
                store.ScheduleResidentTrip(trip);
                Check.Equal(ResidentTripPhase.LeavingHome,store.ResidentTripAt(person.Id,400).Phase);
                Check.Near(-5,store.ResidentTripAt(person.Id,400).Position.X);
                Check.Equal(Activity.Travel,store.LocalResidentState(person.Id,400,default(ActivitySnapshot)).Activity);
                Check.Equal(0,store.RoadResidents(400).Count);
                Check.Equal(ResidentTripPhase.Outbound,store.ResidentTripAt(person.Id,700).Phase);
                Check.Equal(ResidentTripPhase.ReturningHome,store.ResidentTripAt(person.Id,2000).Phase);
                Check.Near(-5,store.ResidentTripAt(person.Id,2000).Position.X);
                Check.True(store.IsResidentAway(person.Id,2299)); Check.True(!store.IsResidentAway(person.Id,2300));
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.Near(-5,restored.ResidentTripAt(person.Id,2000).Position.X);
                trip.HomeRoute[0].X=99; Check.Near(-5,store.ResidentTripAt(person.Id,400).Position.X);
            }));
            cases.Add(("Home gate timing postpones late departures and holds late returns outside",()=>{
                var trip=Trip("gate",17*3600+59*60);
                trip.HomeRoute.Add(new SavedRoutePoint {X=0,Z=0}); trip.HomeRoute.Add(new SavedRoutePoint {X=10,Z=0});
                trip.HomeWalkSeconds=120; trip.HomeWalled=true;
                trip=ResidentTrip.CopyValidated(trip);
                Check.Near(30*3600-120,trip.HomeLeavesAt);
                Check.Equal(ResidentTripPhase.Home,trip.Evaluate(20*3600).Phase);
                trip.DepartsAt=17*3600; trip.VisitSeconds=7200; trip=ResidentTrip.CopyValidated(trip);
                Check.Equal(ResidentTripPhase.WaitingHomeGate,trip.Evaluate(22*3600).Phase);
                Check.Equal(ResidentTripPhase.ReturningHome,trip.Evaluate(30*3600).Phase);
                Check.Equal(ResidentTripPhase.Home,trip.Evaluate(30*3600+120).Phase);
            }));
            cases.Add(("Road actor candidates use the resident identity only while alive and in transit",()=>{
                var store=Store(out var person); store.ScheduleResidentTrip(Trip(person.Id));
                Check.Equal(0,store.RoadResidents(99).Count);
                Check.Equal(person.Id,store.RoadResidents(150)[0].Id);
                Check.True(ReferenceEquals(person,store.RoadResidents(150)[0]));
                Check.Equal(0,store.RoadResidents(400).Count);
                Check.Equal(1,store.RoadResidents(950).Count);
                person.IsAlive=false; Check.Equal(0,store.RoadResidents(950).Count);
                Check.Equal(0,store.RoadResidents(1200).Count);
            }));
            cases.Add(("Resident trip follows road bends and excludes home until actual return",()=>{
                var store=Store(out var person); store.ScheduleResidentTrip(Trip(person.Id));
                Check.True(!store.IsResidentAway(person.Id,99));
                Check.True(store.IsResidentAway(person.Id,100));
                var point=store.ResidentTripAt(person.Id,250);
                Check.Equal(ResidentTripPhase.Outbound,point.Phase); Check.Equal(-1,point.SettlementId);
                Check.Near(11,point.Position.X); Check.Near(10.5,point.Position.Z);
                Check.Equal(ResidentTripPhase.Visiting,store.ResidentTripAt(person.Id,300).Phase);
                Check.Equal(200,store.ResidentTripAt(person.Id,300).SettlementId);
                Check.Equal(ResidentTripPhase.Returning,store.ResidentTripAt(person.Id,900).Phase);
                Check.Near(10.5,store.ResidentTripAt(person.Id,1050).Position.X);
                Check.True(store.IsResidentAway(person.Id,1099));
                Check.True(!store.IsResidentAway(person.Id,1100));
                Check.Equal(100,store.ResidentTripAt(person.Id,1100).SettlementId);
                Check.Equal(0,store.Snapshot().Travelers.Count);
                Check.Equal(1,store.Snapshot().Settlements[0].Households[0].Residents.Count);
                var home=store.Snapshot().Settlements[0].Households[0];
                var homeState=new ActivitySnapshot(Activity.Home,10,new RoutePoint(0,0),0);
                Check.True(HouseholdAccess.RoutineLocked(home,r=>store.LocalResidentState(r.Id,250,homeState)));
                Check.True(!HouseholdAccess.RoutineLocked(home,r=>store.LocalResidentState(r.Id,1100,homeState)));
            }));
            cases.Add(("Resident trips round-trip without changing identity and reject overlaps atomically",()=>{
                var store=Store(out var person); var trip=Trip(person.Id); store.ScheduleResidentTrip(trip);
                trip.Road[0].X=999;
                Check.Near(10,store.ResidentTripAt(person.Id,100).Position.X);
                Check.Throws<ArgumentException>(()=>store.ScheduleResidentTrip(Trip(person.Id,1000)));
                var json=new JsonSerializerOptions {IncludeFields=true};
                var restored=new WorldStateStore(1);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(store.Snapshot(),json),json));
                Check.Near(store.ResidentTripAt(person.Id,1050).Position.X,restored.ResidentTripAt(person.Id,1050).Position.X);
                var copy=restored.Snapshot(); copy.ResidentTrips[0].Road.Clear();
                Check.True(restored.IsResidentAway(person.Id,1050));
                restored.ScheduleResidentTrip(Trip(person.Id,1100));
                Check.Equal(1,restored.Snapshot().ResidentTrips.Count);
                Check.True(restored.IsResidentAway(person.Id,1100));
            }));
            cases.Add(("Resident travel refuses unknown dead and child identities and corrupt road records",()=>{
                var store=Store(out var person);
                Check.Throws<ArgumentException>(()=>store.ScheduleResidentTrip(Trip("unknown")));
                person.AgeGroup=ResidentAgeGroup.Child;
                Check.Throws<ArgumentException>(()=>store.ScheduleResidentTrip(Trip(person.Id)));
                person.AgeGroup=ResidentAgeGroup.Adult; person.IsAlive=false;
                Check.Throws<ArgumentException>(()=>store.ScheduleResidentTrip(Trip(person.Id)));
                person.IsAlive=true; store.ScheduleResidentTrip(Trip(person.Id));
                person.IsAlive=false;
                Check.Equal(ResidentTripPhase.Unavailable,store.ResidentTripAt(person.Id,250).Phase);
                var bad=store.Snapshot(); bad.ResidentTrips[0].Road[1].X=50;
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
                Check.Equal(ResidentTripPhase.Unavailable,store.ResidentTripAt(person.Id,250).Phase);
                Check.Throws<ArgumentException>(()=>ResidentTrip.Create(person.Id,100,200,0,600,100,
                    new[]{new RoutePoint(10,10),new RoutePoint(12,10)}));
            }));
            cases.Add(("Legacy saves gain empty resident journey ledger and reject duplicate ownership",()=>{
                var store=Store(out var person); var legacy=store.Snapshot(); legacy.Version=11; legacy.ResidentTrips=null;
                store.Restore(legacy); Check.Equal(0,store.Snapshot().ResidentTrips.Count);
                store.ScheduleResidentTrip(Trip(person.Id));
                var bad=store.Snapshot(); bad.ResidentTrips.Add(bad.ResidentTrips[0]);
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
            }));
            cases.Add(("Schema 12 trips without home connectors retain their original timing",()=>{
                var store=Store(out var person); store.ScheduleResidentTrip(Trip(person.Id));
                var old=store.Snapshot(); old.Version=12; old.ResidentTrips[0].HomeRoute=null;
                store.Restore(old);
                Check.Equal(21,store.Snapshot().Version);
                Check.Equal(ResidentTripPhase.Outbound,store.ResidentTripAt(person.Id,100).Phase);
                Check.Equal(ResidentTripPhase.Home,store.ResidentTripAt(person.Id,1100).Phase);
                var invalid=store.Snapshot(); invalid.ResidentTrips[0].HomeWalkSeconds=10;
                Check.Throws<ArgumentException>(()=>store.Restore(invalid));
                Check.Equal(ResidentTripPhase.Outbound,store.ResidentTripAt(person.Id,100).Phase);
            }));
        }
    }
}
