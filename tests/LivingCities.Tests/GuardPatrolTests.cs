using System;
using System.Collections.Generic;
using System.Text.Json;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class GuardPatrolTests
    {
        private static SavedRoutePoint P(double x, double z = 0) { return new SavedRoutePoint { X = x, Z = z }; }
        private static List<SavedRoutePoint> Path(params SavedRoutePoint[] p) { return new List<SavedRoutePoint>(p); }
        private static GuardRoster Roster()
        { return GuardRoster.Create(1, 42, 2, new[] { P(-10), P(2, -20) }); }
        private static void Start(GuardRoster roster)
        {
            roster.StartPatrol(PatrolPairPlan.Create(roster.Guards[0].Id, roster.Guards[1].Id,
                Path(P(-10), P(0)), Path(P(2, -20), P(2)),
                Path(P(0), P(20), P(20, 20), P(0, 20), P(0)), 100, 1000, 2, 10, 2), 100);
        }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Roster patrol positions survive world-shaped save without adding guard identities", () => {
                var roster = Roster(); Start(roster);
                Check.Near(10, roster.Position(roster.Guards[0].Id, 250).X);
                var options = new JsonSerializerOptions { IncludeFields = true };
                var copy = GuardRoster.CopyValidated(JsonSerializer.Deserialize<GuardRoster>(JsonSerializer.Serialize(roster, options), options));
                Check.Equal(2, copy.Guards.Count);
                Check.Near(12, copy.Position(copy.Guards[1].Id, 250).X);
                Check.Near(-10, copy.Position(copy.Guards[0].Id, 5000).X);
            }));
            cases.Add(("Building response starts at actual patrol position and returns to the assigned post", () => {
                var roster = Roster(); Start(roster); string id = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, P(100), new Dictionary<string, List<SavedRoutePoint>> { { id, Path(P(-10), P(100)) } });
                var sent = roster.DispatchBuilding("crime", 73, 250, 1, 2, 10);
                Check.Equal(1, sent.Count); Check.Near(10, sent[0].Route[0].X);
                Check.Near(10, roster.Position(id, 250).X); Check.Near(900, sent[0].ArrivesAt);
                Check.Near(2, roster.Position(roster.Guards[1].Id, 300).X);
                Check.Near(0, roster.Position(roster.Guards[1].Id, 300).Z);
                var copy = GuardRoster.CopyValidated(roster);
                copy.Advance(1200); Check.Near(1750, copy.Guards[0].ArrivesAt);
                Check.Near(100, copy.Position(id, 1200).X);
                Check.Near(-10, copy.Position(id, 1750).X);
                Check.Equal(GuardDuty.Posted, copy.Guards[0].Duty);
            }));
            cases.Add(("A patrol partner death freezes the death position and physically returns the survivor", () => {
                var roster = Roster(); Start(roster);
                roster.Kill(roster.Guards[0].Id, 250);
                Check.Near(10, roster.Guards[0].DeathPosition.X);
                Check.Near(2, roster.Position(roster.Guards[1].Id, 300).X);
                Check.Near(-20, roster.Position(roster.Guards[1].Id, 400).Z);
                GuardRoster.CopyValidated(roster);
            }));
            cases.Add(("Stopping a patrol preserves both current positions and returns without renewing the stop time", () => {
                var roster = Roster(); Start(roster); string leader = roster.Guards[0].Id;
                roster.StopPatrol(leader, 250);
                Check.Near(10, roster.Position(leader, 250).X);
                roster.StopPatrol(leader, 275);
                Check.Near(5, roster.Position(leader, 275).X);
                Check.Near(-10, roster.Position(leader, 350).X);
                Check.Near(-20, roster.Position(roster.Guards[1].Id, 400).Z);
                Check.Equal(0, roster.Patrols.Count);
            }));
            cases.Add(("Patrol start rejects duplicate participation and mismatched posts transactionally", () => {
                var roster = Roster(); Start(roster);
                Check.Throws<ArgumentException>(() => Start(roster));
                Check.Equal(2, roster.Guards.Count);
                Check.Near(10, roster.Position(roster.Guards[0].Id, 250).X);
                var invalid = Roster(); invalid.Guards[0].Post.X = -11;
                Check.Throws<ArgumentException>(() => Start(invalid));
                Check.Near(-11, invalid.Position(invalid.Guards[0].Id, 250).X);
            }));
            cases.Add(("Crime before patrol departure cancels the future pair without corrupting saves", () => {
                var roster = Roster(); Start(roster); string id = roster.Guards[0].Id;
                roster.Dispatch("early", 50, new Dictionary<string, List<SavedRoutePoint>> { { id, Path(P(-10), P(10)) } }, 1, 2, 10);
                Check.Equal(0, roster.Patrols.Count);
                Check.Near(-10, roster.Position(id, 50).X);
                Check.Near(-20, roster.Position(roster.Guards[1].Id, 500).Z);
                GuardRoster.CopyValidated(roster);
            }));
            cases.Add(("Patrol response validation rejects fabricated return endpoints and unknown saved partners", () => {
                var roster = Roster(); Start(roster);
                var broken = GuardRoster.CopyValidated(roster); broken.Patrols[0].Plan.FollowerId = "unknown";
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                broken = GuardRoster.CopyValidated(roster); broken.Patrols[0].InterruptedAt = 99;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                string id = roster.Guards[0].Id;
                roster.Dispatch("direct", 250, new Dictionary<string, List<SavedRoutePoint>> { { id, Path(P(10), P(100)) } }, 1, 2, 10);
                Check.Near(10, roster.Position(id, 250).X);
                broken = GuardRoster.CopyValidated(roster); broken.Guards[0].ReturnPath[broken.Guards[0].ReturnPath.Count - 1].X = -9;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                broken = GuardRoster.CopyValidated(roster); broken.Guards[0].ReturnRate = double.MaxValue;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                roster.Advance(1000); Check.Equal(GuardDuty.Returning, roster.Guards[0].Duty);
                Check.Near(-10, roster.Position(id, 1600).X);
            }));
        }
    }
}
