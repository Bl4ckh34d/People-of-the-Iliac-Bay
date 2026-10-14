using System;
using System.Collections.Generic;
using System.Text.Json;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class PatrolPairTests
    {
        private static SavedRoutePoint P(double x, double z = 0) { return new SavedRoutePoint { X = x, Z = z }; }
        private static List<SavedRoutePoint> Path(params SavedRoutePoint[] points) { return new List<SavedRoutePoint>(points); }
        private static PatrolPairPlan Plan()
        {
            return PatrolPairPlan.Create("leader", "follower", Path(P(-10), P(0)), Path(P(2, -20), P(2)),
                Path(P(0), P(20), P(20, 20), P(0, 20), P(0)), 100, 1000, 2, 10, 2);
        }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Patrol members walk separate joins and wait for the shared start", () => {
                var plan = Plan();
                Check.Near(200, plan.CircuitStartsAt);
                Check.Near(-5, plan.Position("leader", 125).X);
                Check.Near(0, plan.Position("leader", 175).X);
                Check.Near(-5, plan.Position("follower", 175).Z);
                Check.Near(0, plan.Position("follower", 200).Z);
            }));
            cases.Add(("Patrol pair maintains route spacing across corners and multiple laps", () => {
                var plan = Plan();
                Check.Near(10, plan.Position("leader", 250).X);
                Check.Near(12, plan.Position("follower", 250).X);
                Check.Near(20, plan.Position("leader", 300).X);
                Check.Near(2, plan.Position("follower", 300).Z);
                Check.Near(10, plan.Position("leader", 650).X);
                Check.Near(12, plan.Position("follower", 650).X);
            }));
            cases.Add(("Patrol interruption retraces traversed circuit and the individual join to the post", () => {
                var plan = Plan(); var route = plan.ReturnRoute("follower", 350);
                Check.Near(20, route[0].X); Check.Near(12, route[0].Z);
                Check.Near(2, route[route.Count - 1].X); Check.Near(-20, route[route.Count - 1].Z);
                route = plan.ReturnRoute("leader", 125);
                Check.Near(-5, route[0].X); Check.Near(-10, route[route.Count - 1].X);
            }));
            cases.Add(("Dawn ends the circuit with physical return rather than a post teleport", () => {
                var plan = Plan();
                Check.Near(0, plan.Position("leader", 1000).X);
                Check.Near(-5, plan.Position("leader", 1025).X);
                Check.Near(-10, plan.Position("leader", 1050).X);
                Check.Near(-10, plan.Position("leader", 5000).X);
                Check.Near(-20, plan.Position("follower", 5000).Z);
            }));
            cases.Add(("Patrol native-shaped save reconstruction preserves exact phase and return", () => {
                var original = Plan();
                var settings = new JsonSerializerOptions { IncludeFields = true };
                var copy = PatrolPairPlan.CopyValidated(JsonSerializer.Deserialize<PatrolPairPlan>(JsonSerializer.Serialize(original, settings), settings));
                foreach (double now in new[] { 100d, 125, 200, 650, 999, 1025, 5000 })
                {
                    Check.Near(original.Position("follower", now).X, copy.Position("follower", now).X);
                    Check.Near(original.Position("follower", now).Z, copy.Position("follower", now).Z);
                }
                copy.Circuit[1].X = 999; Check.Near(20, original.Circuit[1].X);
            }));
            cases.Add(("Patrol validates distinct members, circuit closure, join endpoints and timing", () => {
                var plan = Plan(); plan.FollowerId = plan.LeaderId;
                Check.Throws<ArgumentException>(() => PatrolPairPlan.CopyValidated(plan));
                plan = Plan(); plan.Circuit[plan.Circuit.Count - 1].X = 1;
                Check.Throws<ArgumentException>(() => PatrolPairPlan.CopyValidated(plan));
                plan = Plan(); plan.FollowerJoin[1].X = 3;
                Check.Throws<ArgumentException>(() => PatrolPairPlan.CopyValidated(plan));
                plan = Plan(); plan.EndsAt = 150;
                Check.Throws<ArgumentException>(() => PatrolPairPlan.CopyValidated(plan));
                plan = Plan(); plan.Speed = double.NaN;
                Check.Throws<ArgumentException>(() => PatrolPairPlan.CopyValidated(plan));
                Check.Throws<ArgumentException>(() => Plan().Position("unknown", 300));
            }));
            cases.Add(("Patrol input copies and late return routes preserve the actual current position", () => {
                var join = Path(P(-10), P(0));
                var plan = PatrolPairPlan.Create("leader", "follower", join, Path(P(2, -20), P(2)),
                    Path(P(0), P(20), P(20, 20), P(0, 20), P(0)), 100, 1000, 2, 10, 2);
                join[0].X = 99; Check.Near(-10, plan.LeaderJoin[0].X);
                var route = plan.ReturnRoute("leader", 1025);
                Check.Near(-5, route[0].X); Check.Near(-10, route[route.Count - 1].X);
                route[0].X = 99; Check.Near(-5, plan.Position("leader", 1025).X);
                Check.Throws<ArgumentException>(() => plan.Position("leader", double.NaN));
            }));
            cases.Add(("Patrol phase retains fractional seconds at native absolute game timestamps", () => {
                var plan = Plan(); double offset = 12000000000;
                plan.StartsAt += offset; plan.EndsAt += offset;
                plan = PatrolPairPlan.CopyValidated(plan);
                Check.Near(10.1, plan.Position("leader", offset + 250.5).X);
                Check.Near(12.1, plan.Position("follower", offset + 250.5).X);
            }));
            cases.Add(("Patrol rejects reversing circuits where paired guards would cross through each other", () => {
                Check.Throws<ArgumentException>(() => PatrolPairPlan.Create("leader", "follower",
                    Path(P(-10), P(0)), Path(P(2, -20), P(2)),
                    Path(P(0), P(20), P(0), P(20), P(0)), 100, 1000, 2, 10, 2));
            }));
            cases.Add(("Patrol shifted circuit closes without a pause or jump at each lap", () => {
                var plan = Plan();
                Check.Near(1, plan.Position("follower", 595).X);
                Check.Near(0, plan.Position("follower", 595).Z);
                Check.Near(1.9, plan.Position("follower", 599.5).X);
                Check.Near(2, plan.Position("follower", 600).X);
                Check.Near(2.1, plan.Position("follower", 600.5).X);
                Check.Near(0.1, plan.Position("leader", 600.5).X);
            }));
        }
    }
}
