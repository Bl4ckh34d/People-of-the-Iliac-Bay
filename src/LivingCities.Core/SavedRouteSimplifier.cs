using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public static class SavedRouteSimplifier
    {
        public static List<SavedRoutePoint> Simplify(IEnumerable<SavedRoutePoint> route)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            var points = new List<SavedRoutePoint>();
            foreach (var point in route)
            {
                if (point == null || !Finite(point.X) || !Finite(point.Z))
                    throw new ArgumentException("Route points must have finite coordinates.", nameof(route));
                points.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
            }
            if (points.Count < 3) return points;

            // Remove consecutive duplicates in one pass. Keep both endpoints, even
            // when their coordinates match, so callers retain exact route endpoints.
            var unique = new List<SavedRoutePoint>(points.Count) { points[0] };
            for (int i = 1; i < points.Count - 1; i++)
            {
                if (!Same(unique[unique.Count - 1], points[i])) unique.Add(points[i]);
            }
            var last = points[points.Count - 1];
            if (unique.Count == 1) unique.Add(last);
            else if (Same(unique[unique.Count - 1], last)) unique[unique.Count - 1] = last;
            else unique.Add(last);

            var result = new List<SavedRoutePoint>(unique.Count);
            foreach (var point in unique)
            {
                while (result.Count >= 2 && CanRemoveMiddle(result[result.Count - 2], result[result.Count - 1], point))
                    result.RemoveAt(result.Count - 1);
                result.Add(point);
            }
            return result;
        }

        private static bool CanRemoveMiddle(SavedRoutePoint a, SavedRoutePoint b, SavedRoutePoint c)
        {
            double abX = b.X - a.X, abZ = b.Z - a.Z;
            double bcX = c.X - b.X, bcZ = c.Z - b.Z;
            if (abX == 0 && abZ == 0 || bcX == 0 && bcZ == 0) return false;
            double cross = abX * bcZ - abZ * bcX;
            if (cross != 0) return false;
            return abX * bcX + abZ * bcZ > 0;
        }

        private static bool Same(SavedRoutePoint a, SavedRoutePoint b) { return a.X == b.X && a.Z == b.Z; }
        private static bool Finite(double number) { return !double.IsNaN(number) && !double.IsInfinity(number); }
    }
}
