using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class SavedRouteSimplifierTests
    {
        public static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Saved route simplifier removes duplicate and forward-collinear interior points", new Action(() =>
            {
                var input = Route((0, 0), (0, 0), (1, 0), (2, 0), (2, 1), (2, 1), (2, 2), (3, 2));
                var simplified = SavedRouteSimplifier.Simplify(input);
                EqualPoints(Route((0, 0), (2, 0), (2, 2), (3, 2)), simplified);
                Check.True(!object.ReferenceEquals(input[0], simplified[0]));
                Check.True(!object.ReferenceEquals(input[3], simplified[1]));
                input[3].X = 900;
                Check.Near(2, simplified[1].X);
            })));

            cases.Add(("Saved route simplification preserves distance and distance-sampled positions", new Action(() =>
            {
                var original = Route((0, 0), (1, 0), (2, 0), (2, 1), (2, 2), (3, 2), (4, 2));
                var simplified = SavedRouteSimplifier.Simplify(original);
                Check.Near(Length(original), Length(simplified));
                for (int sample = 0; sample <= 40; sample++)
                {
                    double fraction = sample / 40d;
                    var before = AtDistance(original, fraction * Length(original));
                    var after = AtDistance(simplified, fraction * Length(simplified));
                    Check.Near(before.X, after.X);
                    Check.Near(before.Z, after.Z);
                }
            })));

            cases.Add(("Saved route simplifier preserves corners, backtracking and endpoints", new Action(() =>
            {
                var nearCorner = Route((0, 0), (1, 0), (2, 1), (3, 1));
                EqualPoints(nearCorner, SavedRouteSimplifier.Simplify(nearCorner));
                var tinyCorner = Route((0, 0), (1, 0), (2, 0.000000000001));
                EqualPoints(tinyCorner, SavedRouteSimplifier.Simplify(tinyCorner));
                var reversing = Route((0, 0), (1, 0), (0, 0), (0, 1));
                EqualPoints(reversing, SavedRouteSimplifier.Simplify(reversing));
                var endpointDuplicates = Route((0, 0), (0, 0));
                EqualPoints(endpointDuplicates, SavedRouteSimplifier.Simplify(endpointDuplicates));
            })));

            cases.Add(("Saved route simplifier rejects malformed points and copies empty routes", new Action(() =>
            {
                Check.Throws<ArgumentNullException>(() => SavedRouteSimplifier.Simplify(null));
                Check.Throws<ArgumentException>(() => SavedRouteSimplifier.Simplify(new List<SavedRoutePoint> { null }));
                Check.Throws<ArgumentException>(() => SavedRouteSimplifier.Simplify(Route((0, 0), (double.NaN, 1))));
                Check.Throws<ArgumentException>(() => SavedRouteSimplifier.Simplify(Route((0, 0), (1, double.PositiveInfinity))));
                var empty = new List<SavedRoutePoint>();
                var copy = SavedRouteSimplifier.Simplify(empty);
                Check.True(copy != null && copy.Count == 0 && !object.ReferenceEquals(empty, copy));
            })));
        }

        private static List<SavedRoutePoint> Route(params (double X, double Z)[] points)
        {
            var route = new List<SavedRoutePoint>();
            foreach (var point in points) route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
            return route;
        }
        private static void EqualPoints(IList<SavedRoutePoint> expected, IList<SavedRoutePoint> actual)
        {
            Check.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            { Check.Near(expected[i].X, actual[i].X); Check.Near(expected[i].Z, actual[i].Z); }
        }
        private static double Length(IList<SavedRoutePoint> route)
        {
            double length = 0;
            for (int i = 1; i < route.Count; i++) length += Distance(route[i - 1], route[i]);
            return length;
        }
        private static SavedRoutePoint AtDistance(IList<SavedRoutePoint> route, double distance)
        {
            for (int i = 1; i < route.Count; i++)
            {
                double segment = Distance(route[i - 1], route[i]);
                if (distance <= segment)
                {
                    double fraction = segment == 0 ? 0 : distance / segment;
                    return new SavedRoutePoint
                    {
                        X = route[i - 1].X + (route[i].X - route[i - 1].X) * fraction,
                        Z = route[i - 1].Z + (route[i].Z - route[i - 1].Z) * fraction
                    };
                }
                distance -= segment;
            }
            var last = route[route.Count - 1];
            return new SavedRoutePoint { X = last.X, Z = last.Z };
        }
        private static double Distance(SavedRoutePoint a, SavedRoutePoint b)
        {
            double x = b.X - a.X, z = b.Z - a.Z;
            return Math.Sqrt(x * x + z * z);
        }
    }
}
