using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class RoutineRecoveryLifecycleTests
    {
        private static WorldStateStore Fixture(out Resident resident, out ResidentRoutine routine)
        {
            var store = new WorldStateStore(42);
            resident = store.GetOrCreateFamilies(31, new[] { new HomeProfile(17, 3, 220) }).Households[0].Residents
                .Find(item => item.AgeGroup == ResidentAgeGroup.Adult);
            routine = new ResidentRoutine { ResidentId = resident.Id, DestinationKey = 30,
                OpenMinute = 0, CloseMinute = 1439, ItineraryDepartureSeconds = 9 * 3600,
                Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 10 } },
                Itinerary = new ResidentDestination { Stops = new List<ResidentVisitStop>
                    { new ResidentVisitStop { BuildingKey = 30, Purpose = ResidentVisitPurpose.Shop,
                        WalkSeconds = 20, DwellSeconds = 60,
                        Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 10 } } } },
                    ExitRoute = new List<SavedRoutePoint> { new SavedRoutePoint { X = 10 }, new SavedRoutePoint() },
                    ExitWalkSeconds = 20 } };
            store.SetRoutines(new TownRoutineState { SettlementId = 31, ItineraryVersion = 1,
                Residents = new List<ResidentRoutine> { routine } });
            return store;
        }

        private static RoutineRecoveryPlan Plan(Resident resident, ResidentRoutine routine)
        {
            const int wake = 360, sleep = 1320;
            double depart = 86400 + 9 * 3600 + 10;
            double arrive = depart + 20;
            double leave = arrive + 60;
            return new RoutineRecoveryPlan { ResidentId = resident.Id, SettlementId = 31,
                AbsoluteDay = 1, HomeBuildingKey = resident.HomeKey, OriginBuildingKey = resident.HomeKey,
                RoutineFingerprint = RoutineRecoveryIdentity.Fingerprint(routine, resident.HomeKey, wake, sleep),
                AppointmentFingerprint = RoutineRecoveryIdentity.Appointments(null, resident.Id, 1),
                WakeMinute = wake, SleepMinute = sleep, CreatedAt = 86400 + 9 * 3600,
                DepartAt = depart, EndsAt = leave + 20,
                Legs = new List<RoutineRecoveryLeg>
                {
                    new RoutineRecoveryLeg { OriginalStopIndex = 0, BuildingKey = 30,
                        Purpose = ResidentVisitPurpose.Social, WalkAt = depart, ArriveAt = arrive,
                        EnterAt = arrive, LeaveAt = leave,
                        Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 10 } } },
                    new RoutineRecoveryLeg { OriginalStopIndex = -1, BuildingKey = resident.HomeKey,
                        Purpose = ResidentVisitPurpose.Social, ReturnsHome = true, WalkAt = leave,
                        ArriveAt = leave + 20, EnterAt = leave + 20, LeaveAt = leave + 20,
                        Route = new List<SavedRoutePoint> { new SavedRoutePoint { X = 10 }, new SavedRoutePoint() } },
                } };
        }

        private static void RecoveryIndexRevisionTracksLifecycle()
        {
            var store = Fixture(out var resident, out var routine);
            int initialRevision = store.RoutineRecoveryRevision;
            Check.True(!store.HasRoutineRecovery(resident.Id));
            Check.True(!store.TryGetRoutineRecoveryDay(resident.Id, out var missingDay));
            Check.Equal(-1, missingDay);

            var first = Plan(resident, routine);
            store.SaveRoutineRecovery(first);
            int savedRevision = store.RoutineRecoveryRevision;
            Check.True(savedRevision > initialRevision);
            Check.True(store.HasRoutineRecovery(resident.Id));
            Check.True(store.TryGetRoutineRecoveryDay(resident.Id, out var day));
            Check.Equal(1, day);

            var replacement = Plan(resident, routine);
            replacement.CreatedAt += 1;
            store.SaveRoutineRecovery(replacement);
            Check.True(store.RoutineRecoveryRevision > savedRevision);
            Check.Near(replacement.CreatedAt, store.GetRoutineRecovery(resident.Id, 1,
                replacement.RoutineFingerprint, replacement.AppointmentFingerprint).CreatedAt);
            Check.Equal(null, store.GetRoutineRecovery(resident.Id, 2,
                replacement.RoutineFingerprint, replacement.AppointmentFingerprint));

            int replacedRevision = store.RoutineRecoveryRevision;
            Check.True(store.RemoveRoutineRecovery(resident.Id));
            Check.True(store.RoutineRecoveryRevision > replacedRevision);
            Check.True(!store.HasRoutineRecovery(resident.Id));
            int removedRevision = store.RoutineRecoveryRevision;
            Check.True(!store.RemoveRoutineRecovery(resident.Id));
            Check.Equal(removedRevision, store.RoutineRecoveryRevision);

            store.SaveRoutineRecovery(first);
            var snapshot = store.Snapshot();
            var restored = new WorldStateStore(0);
            int beforeRestore = restored.RoutineRecoveryRevision;
            restored.Restore(snapshot);
            Check.True(restored.RoutineRecoveryRevision > beforeRestore);
            Check.True(restored.HasRoutineRecovery(resident.Id));
            Check.True(restored.TryGetRoutineRecoveryDay(resident.Id, out var restoredDay));
            Check.Equal(1, restoredDay);
        }

        public static void Add(List<(string Name, Action Body)> tests)
        {
            tests.Add(("Routine recovery index revision changes on save, removal, and restore", RecoveryIndexRevisionTracksLifecycle));
        }
    }
}
