using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class DailyItineraryScheduleTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Walking schedules expose a stable leg destination instead of the moving clock sample", () =>
            {
                var schedule=DailySchedule.ItineraryDay(10,Itinerary(),9*3600,360,1320);
                var first=schedule.EvaluateSeconds(9*3600+10);
                var second=schedule.EvaluateSeconds(9*3600+100);
                Check.Equal(Activity.Travel,first.Activity);
                Check.True(first.TravelDestination.HasValue);
                Check.Equal(10d,first.TravelDestination.Value.X);
                Check.Equal(first.TravelDestination.Value.X,second.TravelDestination.Value.X);
                Check.True(first.Position.X!=second.Position.X);
                var last=schedule.EvaluateSeconds(schedule.ReturnHomeAt(9*3600)-10);
                Check.True(last.TravelDestination.HasValue);
                Check.Equal(0d,last.TravelDestination.Value.X);
            }));
            cases.Add(("Adjacent home-return outings survive native clock rounding but reject real overlap", () =>
            {
                var itinerary=Itinerary();
                itinerary.Stops[0].DwellSeconds+=0.123456789013;
                double departure=8*3600;
                double returnedAt=ResidentVisitJourney.Create(departure,itinerary).EndsAt;
                var first=DailySchedule.ItineraryDay(10,itinerary,departure,360,1320);
                // Native serialized departure and recomputed summed leg times can
                // differ below a microsecond even when they were exactly adjacent.
                var adjacent=DailySchedule.ItineraryDay(10,itinerary,returnedAt-1e-9,360,1320);
                var day=DailySchedule.OutingsDay(10,new[]{first,adjacent},360,1320);
                Check.True(day.HasVisit);
                var overlapping=DailySchedule.ItineraryDay(10,itinerary,returnedAt-0.01,360,1320);
                Check.Throws<ArgumentException>(()=>DailySchedule.OutingsDay(10,new[]{first,overlapping},360,1320));
            }));
            cases.Add(("Itinerary schedule maps travel, waits, visits, return, home and sleep", () =>
            {
                var schedule = DailySchedule.ItineraryDay(10, Itinerary(), 8 * 3600, 360, 1320);
                Check.Equal(Activity.Home, schedule.EvaluateSeconds(7.5 * 3600).Activity);
                var openingWait = schedule.EvaluateSeconds(8.5 * 3600);
                Check.Equal(Activity.Travel, openingWait.Activity);
                Check.Equal(-1, openingWait.BuildingKey);
                Check.Equal(20, openingWait.WaitingForBuildingKey.Value);
                Check.Near(10, openingWait.Position.X);
                var firstVisit = schedule.EvaluateSeconds(9 * 3600);
                Check.Equal(Activity.Visit, firstVisit.Activity);
                Check.Equal(20, firstVisit.BuildingKey);
                Check.True(!firstVisit.WaitingForBuildingKey.HasValue);
                var betweenVisits = schedule.EvaluateSeconds(10 * 3600 + 2 * 60);
                Check.Equal(Activity.Travel, betweenVisits.Activity);
                Check.True(!betweenVisits.WaitingForBuildingKey.HasValue);
                Check.Near(14, betweenVisits.Position.X);
                var secondVisit = schedule.EvaluateSeconds(10 * 3600 + 5 * 60);
                Check.Equal(Activity.Visit, secondVisit.Activity);
                Check.Equal(30, secondVisit.BuildingKey);
                Check.Equal(Activity.Travel, schedule.EvaluateSeconds(11 * 3600 + 5 * 60).Activity);
                Check.Equal(Activity.Home, schedule.EvaluateSeconds(11 * 3600 + 10 * 60).Activity);
                Check.Equal(Activity.Sleep, schedule.EvaluateSeconds(22 * 3600).Activity);
                Check.Equal(Activity.Sleep, schedule.EvaluateSeconds(86400).Activity);
            }));

            cases.Add(("Itinerary windows resolve by building before arrival", () =>
            {
                var schedule = DailySchedule.ItineraryDay(10, Itinerary(), 8 * 3600, 360, 1320);
                Check.True(schedule.ContainsVisitBuilding(20));
                Check.True(schedule.ContainsVisitBuilding(30));
                Check.True(!schedule.ContainsVisitBuilding(40));
                Check.True(schedule.TryVisitWindowForBuilding(7 * 3600, 20, out var start, out var end));
                Check.Near(9 * 3600, start);
                Check.Near(10 * 3600, end);
                Check.True(schedule.TryVisitWindowForBuilding(7 * 3600, 30, out start, out end));
                Check.Near(10 * 3600 + 5 * 60, start);
                Check.Near(11 * 3600 + 5 * 60, end);
                Check.True(!schedule.TryVisitWindowForBuilding(7 * 3600, 40, out start, out end));
            }));

            cases.Add(("Routine itinerary persists through save, restore and deep copies", () =>
            {
                var store = new WorldStateStore(42);
                var household = store.GetOrCreate(100, new[] { 10 }).Households[0];
                var resident = household.Residents.Find(person => person.AgeGroup == ResidentAgeGroup.Adult);
                var source = new TownRoutineState { SettlementId = 100, ItineraryVersion = 1,
                    Residents = new List<ResidentRoutine> { new ResidentRoutine { ResidentId = resident.Id,
                        Itinerary = Itinerary(), ItineraryDepartureSeconds = 8 * 3600 } } };
                store.SetRoutines(source);
                source.Residents[0].Itinerary.Stops[0].Route[1].X = 999;
                var snapshot = store.Snapshot();
                Check.Equal(new WorldState().Version, snapshot.Version);
                Check.Near(10, snapshot.Routines[0].Residents[0].Itinerary.Stops[0].Route[1].X);
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { IncludeFields = true });
                var decoded = JsonSerializer.Deserialize<WorldState>(json, new JsonSerializerOptions { IncludeFields = true });
                var restored = new WorldStateStore(0); restored.Restore(decoded);
                var copied = restored.GetRoutines(100);
                copied.Residents[0].Itinerary.ExitRoute[0].X = 123;
                Check.Near(20, restored.GetRoutines(100).Residents[0].Itinerary.ExitRoute[0].X);

                var legacy = snapshot; legacy.Version = 17;
                restored.Restore(legacy);
                Check.Equal(0, restored.GetRoutines(100).ItineraryVersion);
                Check.True(restored.GetRoutines(100).Residents[0].Itinerary == null);
            }));

            cases.Add(("Itinerary validation rejects children, inns, malformed routes and next-day return", () =>
            {
                WorldStateStore store = null;
                Household family = null;
                for (uint seed = 0; seed < 100 && family == null; seed++)
                {
                    store = new WorldStateStore(seed);
                    var candidate = store.GetOrCreateFamilies(100, new[] { new HomeProfile(10, 5, 100) }).Households[0];
                    if (candidate.Residents.Exists(person => person.AgeGroup == ResidentAgeGroup.Child)) family = candidate;
                }
                Check.True(family != null);
                var adult = family.Residents.Find(person => person.AgeGroup == ResidentAgeGroup.Adult);
                var child = family.Residents.Find(person => person.AgeGroup == ResidentAgeGroup.Child);
                Check.True(child != null);
                Check.Throws<ArgumentException>(() => store.SetRoutines(Routine(100, child.Id, Itinerary(), 8 * 3600)));
                var inn = Itinerary(); inn.Stops[0].Purpose = ResidentVisitPurpose.Inn;
                Check.Throws<ArgumentException>(() => store.SetRoutines(Routine(100, adult.Id, inn, 8 * 3600)));
                var disconnected = Itinerary(); disconnected.Stops[1].Route[0].X = 999;
                Check.Throws<ArgumentException>(() => store.SetRoutines(Routine(100, adult.Id, disconnected, 8 * 3600)));
                Check.Throws<ArgumentException>(() => DailySchedule.ItineraryDay(10, Itinerary(), 20 * 3600, 360, 1320));
                Check.Throws<ArgumentException>(() => DailySchedule.ItineraryDay(10, Itinerary(), 8 * 3600, 360, 10 * 60));
            }));
        }

        private static TownRoutineState Routine(int settlement, string resident, ResidentDestination itinerary, double departure)
        {
            return new TownRoutineState { SettlementId = settlement, ItineraryVersion = 1,
                Residents = new List<ResidentRoutine> { new ResidentRoutine { ResidentId = resident,
                    Itinerary = itinerary, ItineraryDepartureSeconds = departure } } };
        }

        private static ResidentDestination Itinerary()
        {
            return new ResidentDestination
            {
                Stops = new List<ResidentVisitStop>
                {
                    new ResidentVisitStop { BuildingKey = 20, Purpose = ResidentVisitPurpose.Shop,
                        OpenMinute = 9 * 60, CloseMinute = 18 * 60, WalkSeconds = 300, DwellSeconds = 3600,
                        Route = new List<SavedRoutePoint> { Point(0, 0), Point(10, 0) } },
                    new ResidentVisitStop { BuildingKey = 30, Purpose = ResidentVisitPurpose.Social,
                        OpenMinute = 9 * 60, CloseMinute = 18 * 60, WalkSeconds = 300, DwellSeconds = 3600,
                        Route = new List<SavedRoutePoint> { Point(10, 0), Point(20, 0) } }
                },
                ExitRoute = new List<SavedRoutePoint> { Point(20, 0), Point(10, 0), Point(0, 0) },
                ExitWalkSeconds = 300
            };
        }
        private static SavedRoutePoint Point(double x, double z) => new SavedRoutePoint { X = x, Z = z };
    }
}
