using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class RoutineStoreTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Routine source revision changes only after successful mutation or restore",()=>{
                var request=HouseholdDayPlanTests.Request();var store=HouseholdDayPlanTests.Store(request);
                int before=store.RoutineRevision;var town=store.GetRoutines(2);store.SetRoutines(town);
                Check.True(store.RoutineRevision>before);before=store.RoutineRevision;
                store.SetResidentRoutine(2,request.FirstRoutine);Check.True(store.RoutineRevision>before);
                before=store.RoutineRevision;var invalid=store.GetRoutines(2).Residents[0];invalid.ResidentId="unknown";
                Check.Throws<ArgumentException>(()=>store.SetResidentRoutine(2,invalid));Check.Equal(before,store.RoutineRevision);
                store.Restore(store.Snapshot());Check.True(store.RoutineRevision>before);
            }));
            cases.Add(("Personal outdoor timetable and appointments survive cache-free save restore exactly",()=>{
                var store=Filled();var person=store.GetOrCreate(100,new[]{10}).Households[0].Residents[0];
                var original=Visit(100,person.Id,30).Residents[0];
                original.Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=3,Z=4}};
                original.ItineraryDepartureSeconds=30000;
                original.Itinerary=new ResidentDestination {Stops=new List<ResidentVisitStop>{new ResidentVisitStop {
                    BuildingKey=30,Purpose=ResidentVisitPurpose.Social,WalkSeconds=10,DwellSeconds=600,Route=original.Route}},
                    ExitRoute=new List<SavedRoutePoint>{new SavedRoutePoint {X=3,Z=4},new SavedRoutePoint()},ExitWalkSeconds=10};
                var personal=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {Z=3},
                    new SavedRoutePoint {X=3,Z=3},new SavedRoutePoint {X=3,Z=4}};
                var reverse=new List<SavedRoutePoint>{new SavedRoutePoint {X=3,Z=4},new SavedRoutePoint {X=3,Z=3},
                    new SavedRoutePoint {Z=3},new SavedRoutePoint()};
                var upgraded=OutdoorRoutineMigration.Rebuild(original,new List<List<SavedRoutePoint>>{personal,reverse},
                    ResidentMovement.WalkSpeedFor(person.AppearanceSeed),2,true);
                store.SetRoutines(new TownRoutineState {SettlementId=100,ItineraryVersion=1,Residents=new List<ResidentRoutine>{upgraded}});
                var booking=new AppointmentRecord {Id="personal-appointment",ResidentId=person.Id,Purpose="meet",Status="accepted",
                    StartsAt=86400+9*3600,EndsAt=86400+9*3600+600,GraceSeconds=30,
                    Place=new AppointmentPlace {Kind="building",SettlementId=100,BuildingKey=30}};
                Check.True(store.TryReserveAppointment(booking,1000,out var reason),reason);
                var options=new System.Text.Json.JsonSerializerOptions {IncludeFields=true};
                var json=System.Text.Json.JsonSerializer.Serialize(store.Snapshot(),options);
                var restored=new WorldStateStore(0);restored.Restore(System.Text.Json.JsonSerializer.Deserialize<WorldState>(json,options),30005);
                var saved=restored.GetRoutines(100).Residents[0];
                Check.Equal(2,saved.RouteGeometryVersion);Check.Equal(OutdoorRoutineMigration.CurrentPersonalLaneVersion,saved.PersonalLaneVersion);
                int cacheCallbacks=0;
                var first=OutdoorRoutineMigration.RuntimeItinerary(upgraded,line=>{cacheCallbacks++;return new List<SavedRoutePoint>{line[0],line[line.Count-1]};});
                var second=OutdoorRoutineMigration.RuntimeItinerary(saved,line=>{cacheCallbacks++;return new List<SavedRoutePoint>{line[0],line[line.Count-1]};});
                Check.Equal(0,cacheCallbacks);
                var a=DailySchedule.ItineraryDay(person.HomeKey,first,30000,360,1320);
                var b=DailySchedule.ItineraryDay(person.HomeKey,second,30000,360,1320);
                foreach(double time in new[]{29999d,30001d,30005d,30020d,30610d,30630d})
                {var x=a.EvaluateSeconds(time);var y=b.EvaluateSeconds(time);Check.Equal(x.Activity,y.Activity);Check.Equal(x.BuildingKey,y.BuildingKey);
                    Check.Near(x.Position.X,y.Position.X);Check.Near(x.Position.Z,y.Position.Z);}
                Check.Near(first.Stops[0].WalkSeconds,second.Stops[0].WalkSeconds);
                Check.Near(first.ExitWalkSeconds,second.ExitWalkSeconds);Check.Equal(4,second.Stops[0].Route.Count);
                Check.True(AppointmentDayPlanner.TryCreate(person,upgraded,1,store.AppointmentsFor(person.Id),360,1320,out var oldDay,out reason),reason);
                Check.True(AppointmentDayPlanner.TryCreate(person,saved,1,restored.AppointmentsFor(person.Id),360,1320,out var newDay,out reason),reason);
                foreach(double time in new[]{86400+9*3600-5d,86400+9*3600,86400+9*3600+630d,86400+9*3600+650d})
                {var x=oldDay.EvaluateSeconds(time);var y=newDay.EvaluateSeconds(time);Check.Equal(x.Activity,y.Activity);
                    Check.Near(x.Position.X,y.Position.X);Check.Near(x.Position.Z,y.Position.Z);}
                Check.Near(booking.StartsAt,restored.FindAppointment(booking.Id).StartsAt);
                Check.Near(booking.EndsAt,restored.FindAppointment(booking.Id).EndsAt);
                Check.Near(booking.GraceSeconds,restored.FindAppointment(booking.Id).GraceSeconds);
            }));
            cases.Add(("Validated saved routines retain geometry migration version",()=>{
                var store=Filled();var resident=store.GetOrCreate(100,new[]{10}).Households[0].Residents[0];
                var town=Visit(100,resident.Id,30);town.Residents[0].RouteGeometryVersion=1;store.SetRoutines(town);
                Check.Equal(1,store.GetRoutines(100).Residents[0].RouteGeometryVersion);
                Check.Equal(1,store.GetResidentDialogueRoutine(100,resident.Id).RouteGeometryVersion);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.Equal(1,restored.GetRoutines(100).Residents[0].RouteGeometryVersion);
            }));
            cases.Add(("Schema 39 accepts routine read and write round trips", () => {
                var store = Filled();
                var resident = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                var routines = Visit(100, resident.Id, 30);
                store.SetRoutines(routines);
                Check.Equal(30, store.GetRoutines(100).Residents[0].DestinationKey);
                var restored = new WorldStateStore(0);
                restored.Restore(store.Snapshot());
                Check.Equal(new WorldState().Version, restored.Snapshot().Version);
                Check.Equal(30, restored.GetRoutines(100).Residents[0].DestinationKey);
            }));
            cases.Add(("Saved personal geometry rejects inconsistent versions and restores legacy personal paths without callbacks",()=>{
                var store=Filled();var person=store.GetOrCreate(100,new[]{10}).Households[0].Residents[0];
                var town=Visit(100,person.Id,30);store.SetRoutines(town);
                var invalid=Visit(100,person.Id,30).Residents[0];invalid.PersonalLaneVersion=1;invalid.RouteGeometryVersion=1;
                Check.Throws<ArgumentException>(()=>store.SetResidentRoutine(100,invalid));
                var personal=OutdoorRoutineMigration.Rebuild(town.Residents[0],new List<List<SavedRoutePoint>>{
                    new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {Z=2},new SavedRoutePoint {X=5}}},1.25,2,true);
                store.SetResidentRoutine(100,personal);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot(),1000);
                int callbacks=0;var route=OutdoorRoutineMigration.RuntimeRoute(restored.GetRoutines(100).Residents[0],line=>{callbacks++;return line;});
                Check.Equal(0,callbacks);Check.Equal(3,route.Count);Check.Near(2,route[1].Z);
                Check.Equal(OutdoorRoutineMigration.CurrentPersonalLaneVersion,restored.GetResidentDialogueRoutine(100,person.Id).PersonalLaneVersion);
                personal.PersonalLaneVersion=1;store.SetResidentRoutine(100,personal);
                restored.Restore(store.Snapshot(),1000);
                Check.Equal(1,restored.GetResidentDialogueRoutine(100,person.Id).PersonalLaneVersion);
                OutdoorRoutineMigration.RuntimeRoute(restored.GetRoutines(100).Residents[0],line=>{callbacks++;return line;});
                Check.Equal(0,callbacks);
            }));
            cases.Add(("Resident geometry publication is detached and rejects invalid updates atomically",()=>{
                var store=Filled();var resident=store.GetOrCreate(100,new[]{10}).Households[0].Residents[0];
                store.SetRoutines(Visit(100,resident.Id,30));
                var updated=Visit(100,resident.Id,30).Residents[0];updated.RouteGeometryVersion=1;
                store.SetResidentRoutine(100,updated);updated.Route[1].X=900;
                Check.Equal(5d,store.GetRoutines(100).Residents[0].Route[1].X);
                var invalid=Visit(100,resident.Id,31).Residents[0];invalid.RouteGeometryVersion=OutdoorRoutineMigration.CurrentVersion+1;
                Check.Throws<ArgumentException>(()=>store.SetResidentRoutine(100,invalid));
                Check.Equal(30,store.GetRoutines(100).Residents[0].DestinationKey);
                Check.Equal(1,store.GetRoutines(100).Residents[0].RouteGeometryVersion);
                Check.True(ReferenceEquals(resident,store.GetOrCreate(100,new[]{10}).Households[0].Residents[0]));
            }));
            cases.Add(("Dialogue routine reads one resident without exposing saved schedule", () => {
                var store = Filled();
                var resident = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                store.SetRoutines(Visit(100, resident.Id, 30));
                var dialogue = store.GetResidentDialogueRoutine(100, resident.Id);
                Check.Equal(30, dialogue.DestinationKey);
                dialogue.DestinationKey = 99;
                Check.Equal(30, store.GetResidentDialogueRoutine(100, resident.Id).DestinationKey);
                Check.True(store.GetResidentDialogueRoutine(100, "unknown") == null);
            }));
            cases.Add(("Dialogue routine retains visit timing and routes for the selected resident", () => {
                var store = Filled();
                var resident = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                var itinerary = new ResidentDestination { Stops = new List<ResidentVisitStop> {
                    new ResidentVisitStop { BuildingKey = 30, Purpose = ResidentVisitPurpose.Shop,
                        OpenMinute = 540, CloseMinute = 1080, WalkSeconds = 300, DwellSeconds = 1200,
                        Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 5 } } } },
                    ExitWalkSeconds = 300, ExitRoute = new List<SavedRoutePoint> {
                        new SavedRoutePoint { X = 5 }, new SavedRoutePoint() } };
                store.SetRoutines(new TownRoutineState { SettlementId = 100, ItineraryVersion = 1,
                    Residents = new List<ResidentRoutine> { new ResidentRoutine { ResidentId = resident.Id,
                        ItineraryDepartureSeconds = 8 * 3600, Itinerary = itinerary } } });
                var dialogue = store.GetResidentDialogueRoutine(100, resident.Id);
                Check.Equal(1, new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(
                    dialogue.ItineraryDepartureSeconds, dialogue.Itinerary).ScheduledVisits()).Count);
                dialogue.Itinerary.Stops[0].Route[1].X = 999;
                Check.Equal(5d, store.GetResidentDialogueRoutine(100, resident.Id).Itinerary.Stops[0].Route[1].X);
            }));
            cases.Add(("Routine reads ignore unrelated malformed settlement data", () => {
                var store = Filled();
                var resident = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                store.SetRoutines(Visit(100, resident.Id, 30));
                var unrelated = store.GetOrCreate(200, new[] { 20 });
                unrelated.Households[0].Residents[0].Id = "";

                Check.Equal(resident.Id, store.GetRoutines(100).Residents[0].ResidentId);
                store.SetRoutines(Visit(100, resident.Id, 31));
                Check.Equal(31, store.GetRoutines(100).Residents[0].DestinationKey);
            }));

            cases.Add(("Routine writes preserve unrelated towns and live residents", () => {
                var store = Filled();
                var resident = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                var otherResident = store.GetOrCreate(200, new[] { 20 }).Households[0].Residents[0];
                store.SetRoutines(Visit(100, resident.Id, 30));
                store.SetRoutines(Visit(200, otherResident.Id, 40));
                var otherTown = store.GetOrCreate(200, new[] { 20 });

                store.SetRoutines(Visit(100, resident.Id, 31));

                Check.True(ReferenceEquals(otherTown, store.GetOrCreate(200, new[] { 20 })));
                Check.True(ReferenceEquals(resident, store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0]));
                Check.Equal(31, store.GetRoutines(100).Residents[0].DestinationKey);
                Check.Equal(40, store.GetRoutines(200).Residents[0].DestinationKey);
            }));

            cases.Add(("Routine reads and writes isolate route lists and reject atomically", () => {
                var store = Filled();
                var resident = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                var source = Visit(100, resident.Id, 30);
                store.SetRoutines(source);
                source.Residents[0].Route[1].X = 99;
                Check.Equal(5d, store.GetRoutines(100).Residents[0].Route[1].X);

                var read = store.GetRoutines(100);
                read.Residents[0].Route[1].X = 88;
                Check.Equal(5d, store.GetRoutines(100).Residents[0].Route[1].X);

                var invalid = Visit(100, resident.Id, 31);
                invalid.Residents[0].Route[0].X = double.NaN;
                Check.Throws<ArgumentException>(() => store.SetRoutines(invalid));
                Check.Equal(30, store.GetRoutines(100).Residents[0].DestinationKey);
                Check.Equal(5d, store.GetRoutines(100).Residents[0].Route[1].X);
                Check.True(ReferenceEquals(resident, store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0]));
            }));
        }

        private static WorldStateStore Filled()
        {
            var store = new WorldStateStore(42);
            store.GetOrCreate(100, new[] { 10 });
            store.GetOrCreate(200, new[] { 20 });
            return store;
        }

        private static TownRoutineState Visit(int settlement, string residentId, int destination)
        {
            return new TownRoutineState { SettlementId = settlement, Residents = new List<ResidentRoutine> {
                new ResidentRoutine { ResidentId = residentId, DestinationKey = destination, OpenMinute = 600,
                    CloseMinute = 1000, Route = new List<SavedRoutePoint> {
                        new SavedRoutePoint(), new SavedRoutePoint { X = 5 } } } } };
        }
    }
}
