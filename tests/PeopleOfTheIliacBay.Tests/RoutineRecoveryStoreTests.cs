using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class RoutineRecoveryStoreTests
    {
        public static void Add(List<(string Name, Action Body)> tests)
        {
            tests.Add(("Routine recovery plan survives exact save restore and schema 29 migrates empty", SaveRestoreAndLegacy));
            tests.Add(("Routine recovery store returns detached plans", MutationIsolation));
            tests.Add(("Routine recovery lookup rejects stale day and fingerprints", StaleLookup));
            tests.Add(("Routine recovery save replaces resident slot", Replace));
            tests.Add(("Routine recovery JSON preserves an exterior origin before the first leg", OriginOutsideRoundTrip));
            tests.Add(("Routine recovery rejects malformed plans and stale routine identity", RejectInvalid));
            tests.Add(("Dead resident recovery history does not invalidate save restore", DeadResidentHistory));
            tests.Add(("Routine recovery routes must retain saved edge provenance", RouteProvenance));
            tests.Add(("Changed routine leaves historical recovery inert without blocking restore", ChangedRoutineHistory));
            tests.Add(("Large saved multi-edge recovery path survives provenance validation", LargeSavedRoute));
        }

        private static WorldStateStore Fixture(out Resident resident, out ResidentRoutine routine)
        {
            var store = new WorldStateStore(42);
            resident = store.GetOrCreateFamilies(31, new[] { new HomeProfile(17, 3, 220) }).Households[0].Residents[0];
            routine = new ResidentRoutine { ResidentId = resident.Id, DestinationKey = 30,
                OpenMinute = 0, CloseMinute = 1439,
                Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 10 } },
                ItineraryDepartureSeconds = 9 * 3600 };
            routine.Itinerary = new ResidentDestination
            {
                Stops = new List<ResidentVisitStop>
                {
                    new ResidentVisitStop { BuildingKey = 30, Purpose = ResidentVisitPurpose.Social,
                        OpenMinute = 0, CloseMinute = 0, WalkSeconds = 20, DwellSeconds = 60,
                        Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 10 } } },
                    new ResidentVisitStop { BuildingKey = 31, Purpose = ResidentVisitPurpose.Shop,
                        OpenMinute = 0, CloseMinute = 0, WalkSeconds = 20, DwellSeconds = 60,
                        Route = new List<SavedRoutePoint> { new SavedRoutePoint { X = 10 },
                            new SavedRoutePoint { X = 15, Z = 5 }, new SavedRoutePoint { X = 20 } } },
                },
                ExitRoute = new List<SavedRoutePoint> { new SavedRoutePoint { X = 20 },
                    new SavedRoutePoint { X = 15, Z = 5 }, new SavedRoutePoint { X = 10 }, new SavedRoutePoint() },
                ExitWalkSeconds = 20,
            };
            store.SetRoutines(new TownRoutineState { SettlementId = 31, ItineraryVersion = 1,
                Residents = new List<ResidentRoutine> { routine } });
            return store;
        }

        private static RoutineRecoveryPlan Plan(Resident resident, ResidentRoutine routine, string appointmentFingerprint = "appointments-a")
        {
            const int wake = 360, sleep = 1320;
            string fingerprint = RoutineRecoveryIdentity.Fingerprint(routine, resident.HomeKey, wake, sleep);
            double depart = 86400 + 9 * 3600 + 10;
            double arrive = depart + 20;
            double leave = arrive + 60;
            return new RoutineRecoveryPlan
            {
                ResidentId = resident.Id,
                SettlementId = 31,
                AbsoluteDay = 1,
                HomeBuildingKey = resident.HomeKey,
                OriginBuildingKey = resident.HomeKey,
                RoutineFingerprint = fingerprint,
                AppointmentFingerprint = appointmentFingerprint,
                WakeMinute = wake,
                SleepMinute = sleep,
                CreatedAt = 86400 + 9 * 3600,
                DepartAt = depart,
                EndsAt = leave + 20,
                SkippedStopIndices = new List<int> { 0 },
                Legs = new List<RoutineRecoveryLeg>
                {
                    new RoutineRecoveryLeg { OriginalStopIndex = 1, BuildingKey = 31,
                        Purpose = ResidentVisitPurpose.Shop, ReturnsHome = false, WalkAt = depart,
                        ArriveAt = arrive, EnterAt = arrive, LeaveAt = leave,
                        Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 10 },
                            new SavedRoutePoint { X = 15, Z = 5 }, new SavedRoutePoint { X = 20 } } },
                    new RoutineRecoveryLeg { OriginalStopIndex = -1, BuildingKey = resident.HomeKey,
                        Purpose = ResidentVisitPurpose.Shop, ReturnsHome = true, WalkAt = leave,
                        ArriveAt = leave + 20, EnterAt = leave + 20, LeaveAt = leave + 20,
                        Route = new List<SavedRoutePoint> { new SavedRoutePoint { X = 20 },
                            new SavedRoutePoint { X = 15, Z = 5 }, new SavedRoutePoint { X = 10 }, new SavedRoutePoint() } },
                },
            };
        }

        private static void SaveRestoreAndLegacy()
        {
            var store = Fixture(out var resident, out var routine);
            var plan = Plan(resident, routine);
            store.SaveRoutineRecovery(plan);
            var options = new JsonSerializerOptions { IncludeFields = true };
            string json = JsonSerializer.Serialize(store.Snapshot(), options);
            var restored = new WorldStateStore(0);
            restored.Restore(JsonSerializer.Deserialize<WorldState>(json, options));
            var found = restored.GetRoutineRecovery(resident.Id, 1, plan.RoutineFingerprint, plan.AppointmentFingerprint);
            Check.True(found != null);
            Check.Equal(plan.SettlementId, found.SettlementId);
            Check.Equal(plan.SkippedStopIndices[0], found.SkippedStopIndices[0]);
            Check.Equal(plan.Legs[0].BuildingKey, found.Legs[0].BuildingKey);
            Check.Near(plan.Legs[0].Route[1].X, found.Legs[0].Route[1].X);

            var legacy = restored.Snapshot();
            legacy.Version = 29;
            legacy.RoutineRecoveries = null;
            var migrated = new WorldStateStore(0);
            migrated.Restore(legacy);
            Check.Equal(null, migrated.GetRoutineRecovery(resident.Id, 1, plan.RoutineFingerprint, plan.AppointmentFingerprint));
        }

        private static void OriginOutsideRoundTrip()
        {
            var store = Fixture(out var resident, out var routine);
            var plan = Plan(resident, routine);
            plan.OriginOutside = true;
            plan.Legs[1].WalkAt += 10;
            store.SaveRoutineRecovery(plan);
            string json = JsonSerializer.Serialize(store.Snapshot(), new JsonSerializerOptions { IncludeFields = true });
            var snapshot = JsonSerializer.Deserialize<WorldState>(json, new JsonSerializerOptions { IncludeFields = true });
            var restored = new WorldStateStore(0);
            restored.Restore(snapshot);
            var found = restored.GetRoutineRecovery(resident.Id, 1, plan.RoutineFingerprint, plan.AppointmentFingerprint);
            Check.True(found != null);
            var beforeDeparture = found.Evaluate(plan.DepartAt - 1);
            Check.Equal(Activity.Travel, beforeDeparture.Activity);
            Check.Near(plan.Legs[0].Route[0].X, beforeDeparture.Position.X);
            Check.Near(plan.Legs[0].Route[0].Z, beforeDeparture.Position.Z);
            var laterGap = found.Evaluate(plan.Legs[0].LeaveAt + 5);
            Check.Equal(Activity.Visit, laterGap.Activity);
            Check.Equal(plan.Legs[0].BuildingKey, laterGap.BuildingKey);

            string legacyJson = json.Replace(",\"OriginOutside\":true", "");
            var legacyStore = new WorldStateStore(0);
            legacyStore.Restore(JsonSerializer.Deserialize<WorldState>(legacyJson, new JsonSerializerOptions { IncludeFields = true }));
            var legacy = legacyStore.GetRoutineRecovery(resident.Id, 1, plan.RoutineFingerprint, plan.AppointmentFingerprint);
            Check.Equal(Activity.Home, legacy.Evaluate(plan.DepartAt - 1).Activity);
        }

        private static void MutationIsolation()
        {
            var store = Fixture(out var resident, out var routine);
            var plan = Plan(resident, routine);
            store.SaveRoutineRecovery(plan);
            plan.Legs[0].Route[1].X = 999;
            var found = store.GetRoutineRecovery(resident.Id, 1, plan.RoutineFingerprint, plan.AppointmentFingerprint);
            Check.Near(10, found.Legs[0].Route[1].X);
            found.SkippedStopIndices.Clear();
            found.Legs[0].Route[1].X = 888;
            var again = store.GetRoutineRecovery(resident.Id, 1, plan.RoutineFingerprint, plan.AppointmentFingerprint);
            Check.Equal(1, again.SkippedStopIndices.Count);
            Check.Near(10, again.Legs[0].Route[1].X);
        }

        private static void StaleLookup()
        {
            var store = Fixture(out var resident, out var routine);
            var plan = Plan(resident, routine);
            store.SaveRoutineRecovery(plan);
            Check.Equal(null, store.GetRoutineRecovery(resident.Id, 2, plan.RoutineFingerprint, plan.AppointmentFingerprint));
            Check.Equal(null, store.GetRoutineRecovery(resident.Id, 1, "other-routine", plan.AppointmentFingerprint));
            Check.Equal(null, store.GetRoutineRecovery(resident.Id, 1, plan.RoutineFingerprint, "other-appointments"));
        }

        private static void Replace()
        {
            var store = Fixture(out var resident, out var routine);
            var first = Plan(resident, routine, "appointments-a");
            var second = Plan(resident, routine, "appointments-b");
            second.CreatedAt += 1;
            store.SaveRoutineRecovery(first);
            store.SaveRoutineRecovery(second);
            Check.Equal(null, store.GetRoutineRecovery(resident.Id, 1, first.RoutineFingerprint, first.AppointmentFingerprint));
            Check.Near(second.CreatedAt, store.GetRoutineRecovery(resident.Id, 1,
                second.RoutineFingerprint, second.AppointmentFingerprint).CreatedAt);
            Check.True(store.RemoveRoutineRecovery(resident.Id));
            Check.Equal(null, store.GetRoutineRecovery(resident.Id, 1, second.RoutineFingerprint, second.AppointmentFingerprint));
        }

        private static void RejectInvalid()
        {
            var store = Fixture(out var resident, out var routine);
            var invalid = Plan(resident, routine);
            invalid.SkippedStopIndices.Add(0);
            Check.Throws<ArgumentException>(() => store.SaveRoutineRecovery(invalid));

            var valid = Plan(resident, routine);
            store.SaveRoutineRecovery(valid);
            var snapshot = store.Snapshot();
            snapshot.RoutineRecoveries[0].HomeBuildingKey++;
            Check.Throws<ArgumentException>(() => store.Restore(snapshot));
            Check.True(store.GetRoutineRecovery(resident.Id, 1, valid.RoutineFingerprint, valid.AppointmentFingerprint) != null);

            var changedRoutine = new ResidentRoutine { ResidentId = routine.ResidentId,
                DestinationKey = routine.DestinationKey, OpenMinute = routine.OpenMinute,
                CloseMinute = routine.CloseMinute - 1, ItineraryDepartureSeconds = routine.ItineraryDepartureSeconds,
                Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 10 } },
                Itinerary = ResidentVisitJourney.Copy(routine.Itinerary) };
            store.SetResidentRoutine(31, changedRoutine);
            Check.Throws<ArgumentException>(() => store.SaveRoutineRecovery(valid));
        }

        private static void DeadResidentHistory()
        {
            var store = Fixture(out var resident, out var routine);
            var plan = Plan(resident, routine);
            store.SaveRoutineRecovery(plan);
            Population.MarkDead(store.CurrentPopulation(31), resident.Id);
            var restored = new WorldStateStore(0);
            restored.Restore(store.Snapshot());
            var found = restored.GetRoutineRecovery(resident.Id, 1,
                plan.RoutineFingerprint, plan.AppointmentFingerprint);
            Check.True(found != null);
            Check.True(!restored.ResolveResident(31, resident.Id).IsAlive);
        }

        private static void RouteProvenance()
        {
            var store = Fixture(out var resident, out var routine);
            var translated = Plan(resident, routine);
            foreach (var leg in translated.Legs)
                foreach (var point in leg.Route) { point.X += 1000; point.Z += 1000; }
            Check.Throws<ArgumentException>(() => store.SaveRoutineRecovery(translated));

            var shortcut = Plan(resident, routine);
            shortcut.Legs[0].Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 20 } };
            Check.Throws<ArgumentException>(() => store.SaveRoutineRecovery(shortcut));

            var valid = Plan(resident, routine);
            store.SaveRoutineRecovery(valid);
            var restored = new WorldStateStore(0);
            restored.Restore(store.Snapshot());
            Check.True(restored.GetRoutineRecovery(resident.Id, 1,
                valid.RoutineFingerprint, valid.AppointmentFingerprint) != null);
        }

        private static void ChangedRoutineHistory()
        {
            var store = Fixture(out var resident, out var routine);
            var oldPlan = Plan(resident, routine);
            store.SaveRoutineRecovery(oldPlan);
            var changed = new ResidentRoutine { ResidentId = routine.ResidentId,
                DestinationKey = routine.DestinationKey, OpenMinute = routine.OpenMinute,
                CloseMinute = routine.CloseMinute - 1, ItineraryDepartureSeconds = routine.ItineraryDepartureSeconds,
                Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 10 } },
                Itinerary = ResidentVisitJourney.Copy(routine.Itinerary) };
            store.SetResidentRoutine(31, changed);
            var restored = new WorldStateStore(0);
            restored.Restore(store.Snapshot());
            string currentFingerprint = RoutineRecoveryIdentity.Fingerprint(changed, resident.HomeKey, 360, 1320);
            Check.Equal(null, restored.GetRoutineRecovery(resident.Id, 1,
                currentFingerprint, oldPlan.AppointmentFingerprint));
        }

        private static void LargeSavedRoute()
        {
            var store = Fixture(out var resident, out var routine);
            var secondEdge = new List<SavedRoutePoint> { new SavedRoutePoint { X = 10 } };
            for (int i = 1; i < 2000; i++)
                secondEdge.Add(new SavedRoutePoint { X = 10 + 10d * i / 1999,
                    Z = i == 1999 ? 0 : i % 2 == 0 ? 0.05 : -0.05 });
            routine.Itinerary.Stops[1].Route = secondEdge;
            var combined = new List<SavedRoutePoint>(routine.Itinerary.Stops[0].Route);
            combined.AddRange(secondEdge.GetRange(1, secondEdge.Count - 1));
            routine.Itinerary.ExitRoute = new List<SavedRoutePoint>(combined);
            routine.Itinerary.ExitRoute.Reverse();
            routine.Itinerary.ExitWalkSeconds = 20;
            store.SetResidentRoutine(31, routine);

            var plan = Plan(resident, routine);
            plan.Legs[0].Route = combined;
            var home = new List<SavedRoutePoint>(combined);
            home.Reverse();
            plan.Legs[1].Route = home;
            store.SaveRoutineRecovery(plan);
            var restored = new WorldStateStore(0);
            restored.Restore(store.Snapshot());
            var copy = restored.GetRoutineRecovery(resident.Id, 1,
                plan.RoutineFingerprint, plan.AppointmentFingerprint);
            Check.Equal(combined.Count, copy.Legs[0].Route.Count);
            Check.Near(combined[1000].X, copy.Legs[0].Route[1000].X);
            Check.Near(combined[1000].Z, copy.Legs[0].Route[1000].Z);
        }
    }
}
