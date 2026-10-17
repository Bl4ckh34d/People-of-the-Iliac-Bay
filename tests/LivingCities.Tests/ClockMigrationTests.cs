using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class ClockMigrationTests
    {
        public static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Clock migration preserves active route position and real travel time", VerifyActiveInterval));
            cases.Add(("Clock migration leaves completed and future intervals stable and is idempotent", VerifyBoundariesAndIdempotence));
            cases.Add(("Legacy escape migration preserves position and remaining real time", VerifyEscapeMigration));
            cases.Add(("Legacy messenger migration preserves position and remaining real time", VerifyMessengerMigration));
            cases.Add(("Patrol migration preserves loaded position and scheduled end", VerifyPatrolMigration));
            cases.Add(("Refuge migration preserves loaded position and remaining travel time", VerifyRefugeMigration));
            cases.Add(("Legacy save migration validates and preserves active guard response", VerifyWorldStateGuardMigration));
        }

        private static void VerifyActiveInterval()
        {
            var interval = ClockRateMigration.RescaleInterval(100, 220, 160, 12, 2);
            double oldProgress = (160 - 100d) / (220 - 100d);
            double newProgress = (interval.Now - interval.Start) / (interval.End - interval.Start);
            Check.Near(oldProgress, newProgress);
            Check.Near(5, (interval.End - interval.Now) / 2);
            Check.Near(5, (220 - 160d) / 12);
        }

        private static void VerifyBoundariesAndIdempotence()
        {
            var future = ClockRateMigration.RescaleInterval(200, 320, 160, 12, 2);
            Check.Near(200, future.Start);
            Check.Near(220, future.End);
            var completed = ClockRateMigration.RescaleInterval(100, 150, 160, 12, 2);
            Check.Near(100, completed.Start);
            Check.Near(150, completed.End);
            var once = ClockRateMigration.RescaleInterval(100, 220, 160, 12, 2);
            var twice = ClockRateMigration.RescaleInterval(once.Start, once.End, once.Now, 2, 2);
            Check.Near(once.Start, twice.Start);
            Check.Near(once.End, twice.End);
        }

        private static void VerifyEscapeMigration()
        {
            var escape = Escape();
            var before = new AlarmPerson();
            escape.Advance(before, 130);
            double oldRemaining = (escape.ExitAt - 130) / 12;
            escape.MigrateClockScale(130, 12, 2);
            var after = new AlarmPerson();
            escape.Advance(after, 130);
            Check.Near(before.X, after.X);
            Check.Near(oldRemaining, (escape.ExitAt - 130) / 2);
            double start = escape.StartedAt, end = escape.ExitAt;
            escape.MigrateClockScale(130, 12, 2);
            Check.Near(start, escape.StartedAt);
            Check.Near(end, escape.ExitAt);

            escape = Escape();
            before = new AlarmPerson(); escape.Advance(before, 400);
            oldRemaining = (escape.ReportAt - 400) / 12;
            escape.MigrateClockScale(400, 12, 2);
            after = new AlarmPerson(); escape.Advance(after, 400);
            Check.Near(before.X, after.X);
            Check.Near(oldRemaining, (escape.ReportAt - 400) / 2);
        }

        private static CivilianEscape Escape()
        {
            return CivilianEscape.Copy(new CivilianEscape
            {
                BuildingKey = 1, StartedAt = 100, Speed = 2, TimeScale = 12,
                InteriorRoute = new List<GuardPoint3> { new GuardPoint3 { X = 0 }, new GuardPoint3 { X = 20 } },
                ExteriorRoute = new List<SavedRoutePoint> { new SavedRoutePoint { X = 20 }, new SavedRoutePoint { X = 120 } },
            });
        }

        private static void VerifyMessengerMigration()
        {
            var messenger = new EntryMessenger
            {
                CitizenId = "citizen", GuardId = "guard", StartedAt = 100, TimeScale = 12,
                Route = new List<SavedRoutePoint> { new SavedRoutePoint { X = 0 }, new SavedRoutePoint { X = 280 } },
            };
            double before = messenger.Position(160).X;
            double oldRemaining = (messenger.ArrivesAt - 160) / 12;
            messenger.MigrateClockScale(160, 12, 2);
            Check.Near(before, messenger.Position(160).X);
            Check.Near(oldRemaining, (messenger.ArrivesAt - 160) / 2);
            double start = messenger.StartedAt, end = messenger.ArrivesAt;
            messenger.MigrateClockScale(160, 12, 2);
            Check.Near(start, messenger.StartedAt);
            Check.Near(end, messenger.ArrivesAt);
        }

        private static void VerifyPatrolMigration()
        {
            var plan = PatrolPairPlan.Create("leader", "follower",
                new List<SavedRoutePoint> { new SavedRoutePoint { X = -10 }, new SavedRoutePoint() },
                new List<SavedRoutePoint> { new SavedRoutePoint { X = 2, Z = -20 }, new SavedRoutePoint { X = 2 } },
                new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 20 },
                    new SavedRoutePoint { X = 20, Z = 20 }, new SavedRoutePoint { Z = 20 }, new SavedRoutePoint() },
                100, 1000, 2, 12, 2);
            var before = plan.Position("leader", 250);
            plan.MigrateClockRate(250, 12, 2);
            var after = plan.Position("leader", 250);
            Check.Near(before.X, after.X); Check.Near(before.Z, after.Z); Check.Near(1000, plan.EndsAt);
            var start = plan.StartsAt; plan.MigrateClockRate(250, 12, 2); Check.Near(start, plan.StartsAt);

            plan = PatrolPairPlan.Create("leader", "follower",
                new List<SavedRoutePoint> { new SavedRoutePoint { X = -10 }, new SavedRoutePoint() },
                new List<SavedRoutePoint> { new SavedRoutePoint { X = 2, Z = -20 }, new SavedRoutePoint { X = 2 } },
                new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 20 },
                    new SavedRoutePoint { X = 20, Z = 20 }, new SavedRoutePoint { Z = 20 }, new SavedRoutePoint() },
                100, 1000, 2, 12, 2);
            before = plan.Position("leader", 1025);
            plan.MigrateClockRate(1025, 12, 2);
            after = plan.Position("leader", 1025);
            Check.Near(before.X, after.X); Check.Near(before.Z, after.Z);
        }

        private static void VerifyRefugeMigration()
        {
            var journey = GuardRefugeJourney.Create(7,
                new[] { new GuardPoint3(), new GuardPoint3 { X = 10 } },
                new[] { new GuardPoint3(), new GuardPoint3 { Z = 4 } }, 100, 2, 12);
            var before = journey.Evaluate(130);
            double oldRemaining = (journey.StartsAt + 14 * journey.SecondsPerMetre - 130) / 12;
            journey.MigrateClockRate(130, 12, 2);
            var after = journey.Evaluate(130);
            Check.Near(before.Position.X, after.Position.X); Check.Near(before.Position.Z, after.Position.Z);
            Check.Near(oldRemaining, (journey.StartsAt + 14 * journey.SecondsPerMetre - 130) / 2);
            double start = journey.StartsAt; journey.MigrateClockRate(130, 12, 2);
            Check.Near(start, journey.StartsAt);
        }

        private static void VerifyWorldStateGuardMigration()
        {
            var store = new WorldStateStore(42);
            store.GetOrCreate(1, new[] { 1 });
            var roster = store.GetOrCreateGuards(1, 1, new[] { new SavedRoutePoint() });
            string guardId = roster.Guards[0].Id;
            roster.Dispatch("clock-migration", 100,
                new Dictionary<string, List<SavedRoutePoint>> {
                    { guardId, new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 100 } } }
                }, 1, 1, 12);
            var legacy = store.Snapshot(); legacy.Version = 21;
            double before = roster.Position(guardId, 160).X;
            var restored = new WorldStateStore(0); restored.Restore(legacy, 160);
            var migrated = restored.Snapshot();
            var migratedRoster = migrated.Garrisons[0];
            var migratedGuard = migratedRoster.Guards[0];
            Check.Near(before, migratedRoster.Position(guardId, 160).X);
            Check.Near(350, migratedGuard.ArrivesAt);
            Check.Near(95, (migratedGuard.ArrivesAt - 160) / 2);
            Check.Equal(22, migrated.Version);
            restored.Restore(migrated, 160);
            Check.Near(350, restored.Snapshot().Garrisons[0].Guards[0].ArrivesAt);

            var gateStore = new WorldStateStore(42); gateStore.GetOrCreate(1, new[] { 1 });
            var gateRoster = gateStore.GetOrCreateGuards(1, 1, new[] { new SavedRoutePoint() });
            string gateGuard = gateRoster.Guards[0].Id;
            gateRoster.SchedulePostRecall(gateGuard,
                new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 5 } }, 200, 600, 1, 12);
            var wallDuty = GuardRefugeJourney.Create(7,
                new[] { new GuardPoint3(), new GuardPoint3 { X = 5 }, new GuardPoint3 { X = 10 } },
                new[] { new GuardPoint3(), new GuardPoint3 { Z = 8 } }, 200, 1, 12);
            wallDuty.WallDuty = true;
            wallDuty.ReturnExterior = new List<GuardPoint3> { new GuardPoint3 { X = 10 }, new GuardPoint3 { X = 5 } };
            wallDuty.Release(500); gateRoster.ScheduleWallDuty(gateGuard, wallDuty);
            legacy = gateStore.Snapshot(); legacy.Version = 21;
            var oldWall = legacy.Garrisons[0].Guards[0].PostRecall.WallDuty;
            double oldRelease = oldWall.ReleasedAt.Value;
            var oldPosition = oldWall.Evaluate(203).Position;
            var gateRestored = new WorldStateStore(0); gateRestored.Restore(legacy, 203);
            var migratedRecall = gateRestored.Snapshot().Garrisons[0].Guards[0].PostRecall;
            var migratedWall = migratedRecall.WallDuty;
            var newPosition = migratedWall.Evaluate(203).Position;
            Check.Near(oldPosition.X, newPosition.X); Check.Near(oldPosition.Z, newPosition.Z);
            Check.Near(migratedWall.StartsAt, migratedRecall.DepartsAt);
            Check.Near(212.5, migratedRecall.ArrivesAt);
            Check.Near(oldRelease, migratedWall.ReleasedAt.Value);
        }
    }
}
