using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private List<RoutePoint> OffsetStreetPath(IList<RoutePoint> route, uint seed)
        {
            // Native agents handle separation. Do not bend their saved route
            // back into the legacy grid's personal lanes and walking times.
            if(geometry!=null && geometry.UsesNativeNavigation)
                return new List<RoutePoint>(route);
            if (geometry == null) return new List<RoutePoint>(route);
            return geometry.OffsetStreetRoute(route, seed);
        }

        private ResidentDestination OffsetStreetItinerary(ResidentRoutine source, uint seed)
        {
            return OutdoorRoutineMigration.RuntimeItinerary(source,route=>OffsetSavedRoute(route,seed));
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
