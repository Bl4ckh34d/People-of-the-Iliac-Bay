using System;
using System.Collections.Generic;
using System.Text.Json;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class ObservedVisitTests
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Observed visit follows route corners without teleporting", RouteCorners));
            cases.Add(("Observed visit applies dwell after physical arrival", DwellAndLargeElapsed));
            cases.Add(("Observed visit clips remaining route and round trips", RemainingRouteRoundTrip));
            cases.Add(("Observed visit store validates, copies, restores and removes records", StorePersistence));
            cases.Add(("Observed visit store accepts trip destination stops and historical dead owners", TripDestinationAndDeadOwner));
            cases.Add(("Observed visit rejects invalid bounds and disconnected legs", RejectInvalid));
        }

        private static void RouteCorners()
        {
            var visit = Create();
            var start = visit.Evaluate(100);
            Check.Equal(ShopVisitPhase.Walking, start.Phase);
            Check.Near(0, start.Position.X);

            var beforeCorner = visit.Evaluate(101.5);
            Check.Equal(ShopVisitPhase.Walking, beforeCorner.Phase);
            Check.Near(1.5, beforeCorner.Position.X);
            Check.Near(0, beforeCorner.Position.Z);

            var atCorner = visit.Evaluate(103);
            Check.Equal(ShopVisitPhase.Walking, atCorner.Phase);
            Check.Equal(0, atCorner.LegIndex);
            Check.Near(3, atCorner.Position.X);
            Check.Near(0, atCorner.Position.Z);

            var nextLeg = visit.Evaluate(112);
            Check.Equal(ShopVisitPhase.Leaving, nextLeg.Phase);
            Check.Equal(1, nextLeg.LegIndex);
            Check.Near(3, nextLeg.Position.X);
            Check.Near(4, nextLeg.Position.Z);
        }

        private static void DwellAndLargeElapsed()
        {
            var visit = Create();
            var duringDwell = visit.Evaluate(109);
            Check.Equal(ShopVisitPhase.Browsing, duringDwell.Phase);
            Check.Near(3, duringDwell.Position.X);
            Check.Near(4, duringDwell.Position.Z);

            var completed = visit.Evaluate(visit.EndsAt + 10_000_000);
            Check.True(completed.Completed);
            Check.Equal(ShopVisitPhase.Complete, completed.Phase);
            Check.Equal(1, completed.LegIndex);
            Check.Near(5, completed.Position.X);
            Check.Near(4, completed.Position.Z);

            Check.Throws<ArgumentOutOfRangeException>(() => visit.Evaluate(99.99));
        }

        private static void RemainingRouteRoundTrip()
        {
            var visit = Create();
            var remaining = visit.RemainingLegs(101);
            Check.Equal(2, remaining.Count);
            Check.Equal(3, remaining[0].Route.Count);
            Check.Near(1, remaining[0].Route[0].X);
            Check.Near(0, remaining[0].Route[0].Z);
            Check.Near(3, remaining[0].Route[1].X);
            Check.Near(0, remaining[0].Route[1].Z);
            Check.Near(3, remaining[0].Route[2].X);
            Check.Near(4, remaining[0].Route[2].Z);

            var inDwell = visit.RemainingLegs(109);
            Check.Equal(2, inDwell.Count);
            Check.Equal(1, inDwell[0].Route.Count);
            Check.Near(3, inDwell[0].DwellSeconds);

            var clipped = ObservedVisit.Copy(new ObservedVisit
            {
                ResidentId = visit.ResidentId,
                SettlementId = visit.SettlementId,
                BuildingKey = visit.BuildingKey,
                ObservedAt = 101,
                PlannedEndAt = visit.PlannedEndAt,
                ResumeUntil = visit.ResumeUntil,
                SecondsPerMetre = visit.SecondsPerMetre,
                Legs = remaining,
            });
            Check.Near(visit.EndsAt - 101, clipped.EndsAt - clipped.ObservedAt);
            var encoded = JsonSerializer.Serialize(clipped, Json);
            var restored = ObservedVisit.Copy(JsonSerializer.Deserialize<ObservedVisit>(encoded, Json));
            Check.Equal(encoded, JsonSerializer.Serialize(restored, Json));

            remaining[0].Route[0].X = 999;
            Check.Near(1, clipped.Legs[0].Route[0].X);
            var copied = ObservedVisit.Copy(clipped);
            copied.Legs[0].Route[0].X = -100;
            Check.Near(1, clipped.Legs[0].Route[0].X);

            Check.Equal(0, visit.RemainingLegs(visit.EndsAt).Count);
        }

        private static void RejectInvalid()
        {
            var disconnected = Create();
            disconnected.Legs[1].Route[0].Z = 0.25;
            Check.Throws<ArgumentException>(() => ObservedVisit.Copy(disconnected));

            var badPhase = Create();
            badPhase.Legs[0].AfterPhase = ShopVisitPhase.Leaving;
            Check.Throws<ArgumentException>(() => ObservedVisit.Copy(badPhase));

            var badFinalDwell = Create();
            badFinalDwell.Legs[1].DwellSeconds = 1;
            Check.Throws<ArgumentException>(() => ObservedVisit.Copy(badFinalDwell));

            var badIdentity = Create();
            badIdentity.BuildingKey = -1;
            Check.Throws<ArgumentException>(() => ObservedVisit.Copy(badIdentity));

            var tooLong = Create();
            tooLong.Legs[0].DwellSeconds = 7 * 86400;
            Check.Throws<ArgumentException>(() => ObservedVisit.Copy(tooLong));
        }

        private static void TripDestinationAndDeadOwner()
        {
            var store = new WorldStateStore(31);
            var home = store.GetOrCreate(100, new[] { 10 }, 1);
            var owner = home.Households[0].Residents[0];
            store.GetOrCreate(200, new[] { 20 }, 1);

            var trip = ResidentTrip.Create(owner.Id, 100, 200, 0, 600, 10,
                new[] { new RoutePoint(10, 10), new RoutePoint(11, 10) });
            trip.Destination = new ResidentDestination
            {
                ExitWalkSeconds = 60,
                ExitRoute = new List<SavedRoutePoint> { Saved(10, 0), Saved(0, 0) },
                Stops = new List<ResidentVisitStop>
                {
                    new ResidentVisitStop
                    {
                        BuildingKey = 55,
                        Purpose = ResidentVisitPurpose.Shop,
                        OpenMinute = 0,
                        CloseMinute = 0,
                        WalkSeconds = 60,
                        DwellSeconds = 120,
                        Route = new List<SavedRoutePoint> { Saved(0, 0), Saved(10, 0) },
                    },
                },
            };
            store.ScheduleResidentTrip(trip);
            var scheduledTrip = store.Snapshot().ResidentTrips[0];
            Check.True(scheduledTrip.Destination != null, "Scheduled trip lost its destination.");
            Check.Equal(200, scheduledTrip.DestinationSettlement);
            var scheduled = ResidentVisitJourney.Create(scheduledTrip.ArrivesAt, scheduledTrip.Destination).ScheduledVisits();
            double leaveAt = 0;
            foreach (var visit in scheduled) leaveAt = visit.LeaveAt;

            var observed = Create();
            observed.ResidentId = owner.Id;
            observed.SettlementId = 200;
            observed.BuildingKey = 55;
            observed.PlannedEndAt = leaveAt;
            observed.ObservedAt = leaveAt;
            observed.ResumeUntil = leaveAt;
            store.StoreObservedVisit(observed);

            Population.MarkDead(home, owner.Id);
            var json = JsonSerializer.Serialize(store.Snapshot(), Json);
            var restored = new WorldStateStore(0);
            restored.Restore(JsonSerializer.Deserialize<WorldState>(json, Json));
            Check.Equal(json, JsonSerializer.Serialize(restored.Snapshot(), Json));
            Check.Equal(55, restored.GetObservedVisit(owner.Id).BuildingKey);
        }

        private static void StorePersistence()
        {
            var store = new WorldStateStore(17);
            var town = store.GetOrCreate(1, new[] { 7, 8 }, 1);
            var resident = town.Households[0].Residents[0];
            var destination = town.Households.Find(home => home.BuildingKey != resident.HomeKey).BuildingKey;
            store.SetRoutines(new TownRoutineState
            {
                SettlementId = 1,
                Residents = new List<ResidentRoutine>
                {
                    new ResidentRoutine
                    {
                        ResidentId = resident.Id,
                        DestinationKey = destination,
                        OpenMinute = 500,
                        CloseMinute = 600,
                        Route = new List<SavedRoutePoint> { new SavedRoutePoint { X = 0, Z = 0 }, new SavedRoutePoint { X = 1, Z = 0 } },
                    },
                },
            });

            var observed = Create();
            observed.ResidentId = resident.Id;
            observed.SettlementId = 1;
            observed.BuildingKey = destination;
            store.StoreObservedVisit(observed);
            observed.Legs[0].Route[0].X = 999;

            var saved = store.GetObservedVisit(resident.Id);
            Check.Near(0, saved.Legs[0].Route[0].X);
            Check.True(store.TryObservedVisitState(resident.Id, 101, out var movement), "Observed visit state lookup failed.");
            Check.Equal(ShopVisitPhase.Walking, movement.Phase);
            Check.Near(1, movement.Position.X);
            Check.True(store.TryObservedVisitMetadata(resident.Id, out var settlement, out var building,
                out var observedAt, out var endsAt, out var plannedEndAt, out var resumeUntil), "Observed visit metadata lookup failed.");
            Check.Equal(1, settlement);
            Check.Equal(destination, building);
            Check.Near(100, observedAt);
            Check.Near(114, endsAt);
            Check.Near(90, plannedEndAt);
            Check.Near(95, resumeUntil);

            var json = JsonSerializer.Serialize(store.Snapshot(), Json);
            var restored = new WorldStateStore(0);
            restored.Restore(JsonSerializer.Deserialize<WorldState>(json, Json));
            Check.Equal(21, restored.Snapshot().Version);
            Check.Equal(json, JsonSerializer.Serialize(restored.Snapshot(), Json));
            Check.Near(1, restored.GetObservedVisit(resident.Id).Evaluate(101).Position.X);

            var replacement = restored.GetObservedVisit(resident.Id);
            replacement.ObservedAt = 102;
            restored.StoreObservedVisit(replacement);
            Check.Near(102, restored.GetObservedVisit(resident.Id).ObservedAt);
            Check.Equal(1, restored.Snapshot().ObservedVisits.Count);

            var invalidTarget = replacement;
            invalidTarget.BuildingKey = 999;
            Check.Throws<ArgumentException>(() => restored.StoreObservedVisit(invalidTarget));
            Check.Equal(destination, restored.GetObservedVisit(resident.Id).BuildingKey);

            var invalidSettlement = restored.GetObservedVisit(resident.Id);
            invalidSettlement.SettlementId = 99;
            Check.Throws<ArgumentException>(() => restored.StoreObservedVisit(invalidSettlement));

            var corrupt = restored.Snapshot();
            corrupt.ObservedVisits[0].ResidentId = "unknown-resident";
            Check.Throws<ArgumentException>(() => restored.Restore(corrupt));
            Check.Equal(destination, restored.GetObservedVisit(resident.Id).BuildingKey);

            var duplicate = restored.Snapshot();
            duplicate.ObservedVisits.Add(ObservedVisit.Copy(duplicate.ObservedVisits[0]));
            Check.Throws<ArgumentException>(() => restored.Restore(duplicate));
            Check.Equal(1, restored.Snapshot().ObservedVisits.Count);

            Check.True(restored.RemoveObservedVisit(resident.Id), "Observed visit removal missed active record.");
            Check.True(!restored.RemoveObservedVisit(resident.Id), "Observed visit removal reported duplicate success.");
            var legacy = restored.Snapshot();
            legacy.Version = 18;
            legacy.ObservedVisits = null;
            restored.Restore(legacy);
            Check.True(restored.GetObservedVisit(resident.Id) == null, "Legacy restore retained an observed visit.");
        }

        private static ObservedVisit Create()
        {
            return ObservedVisit.Copy(new ObservedVisit
            {
                ResidentId = "local-resident",
                SettlementId = 12,
                BuildingKey = 34,
                ObservedAt = 100,
                PlannedEndAt = 90,
                ResumeUntil = 95,
                SecondsPerMetre = 1,
                Legs = new List<ObservedVisitLeg>
                {
                    new ObservedVisitLeg
                    {
                        Route = new List<GuardPoint3> { Point(0, 0), Point(3, 0), Point(3, 4) },
                        DwellSeconds = 5,
                        AfterPhase = ShopVisitPhase.Browsing,
                    },
                    new ObservedVisitLeg
                    {
                        Route = new List<GuardPoint3> { Point(3, 4), Point(5, 4) },
                        DwellSeconds = 0,
                        AfterPhase = ShopVisitPhase.Complete,
                    },
                },
            });
        }

        private static GuardPoint3 Point(double x, double z)
        { return new GuardPoint3 { X = x, Y = 0, Z = z }; }

        private static SavedRoutePoint Saved(double x, double z)
        { return new SavedRoutePoint { X = x, Z = z }; }
    }
}
