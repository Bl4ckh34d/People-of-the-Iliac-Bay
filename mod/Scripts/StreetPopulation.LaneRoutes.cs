using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private List<RoutePoint> OffsetStreetPath(IList<RoutePoint> route, uint seed)
        {
            if (geometry == null) return new List<RoutePoint>(route);
            return geometry.OffsetStreetRoute(route, seed);
        }

        private ResidentDestination OffsetStreetItinerary(ResidentDestination source, uint seed)
        {
            var copy = ResidentVisitJourney.Copy(source);
            foreach (var stop in copy.Stops)
            {
                double before = RouteLength(stop.Route);
                var route = OffsetSavedRoute(stop.Route, seed);
                double after = RouteLength(route);
                stop.Route = route;
                if (before > 0) stop.WalkSeconds *= after / before;
            }
            if (copy.Stops.Count > 0 && copy.Stops[0].WalkSeconds < copy.EntryGateSeconds)
                copy.Stops[0].WalkSeconds = copy.EntryGateSeconds;
            double exitBefore = RouteLength(copy.ExitRoute);
            var exit = OffsetSavedRoute(copy.ExitRoute, seed);
            double exitAfter = RouteLength(exit);
            copy.ExitRoute = exit;
            if (exitBefore > 0) copy.ExitWalkSeconds *= exitAfter / exitBefore;
            if (copy.ExitWalkSeconds < copy.ExitGateSeconds) copy.ExitWalkSeconds = copy.ExitGateSeconds;
            return copy;
        }

        private List<SavedRoutePoint> OffsetSavedRoute(List<SavedRoutePoint> route, uint seed)
        {
            var points = new List<RoutePoint>(route.Count);
            foreach (var point in route) points.Add(new RoutePoint(point.X, point.Z));
            var offset = OffsetStreetPath(points, seed);
            var result = new List<SavedRoutePoint>(offset.Count);
            foreach (var point in offset) result.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
            return result;
        }

        private static double RouteLength(IList<SavedRoutePoint> route)
        {
            double length = 0;
            for (int i = 1; i < route.Count; i++)
            {
                double dx = route[i].X - route[i - 1].X, dz = route[i].Z - route[i - 1].Z;
                length += Math.Sqrt(dx * dx + dz * dz);
            }
            return length;
        }
    }
}
