using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Native runtime supplies collision-checked links from saved routine graph
    // nodes to one exact, captured appointment place. Routes remain transient;
    // serialized appointment identity stays in AppointmentPlace.
    public sealed class AppointmentPlaceRoute
    {
        public string AppointmentId;
        public AppointmentPlace Place;
        public List<AppointmentPlaceRouteConnection> Connections = new List<AppointmentPlaceRouteConnection>();
    }

    public sealed class AppointmentPlaceRouteConnection
    {
        public int AnchorBuildingKey;
        public List<SavedRoutePoint> Route = new List<SavedRoutePoint>();
    }

    internal static class AppointmentPlaceRouteValidation
    {
        public static bool Matches(AppointmentRecord appointment, AppointmentPlaceRoute route)
        {
            if (appointment == null || appointment.Place == null || route == null ||
                string.IsNullOrWhiteSpace(route.AppointmentId) || route.AppointmentId != appointment.Id || route.Place == null)
                return false;
            var place = appointment.Place;
            var captured = route.Place;
            return place.Kind == captured.Kind && place.SettlementId == captured.SettlementId &&
                place.BuildingKey == captured.BuildingKey && place.X == captured.X && place.Y == captured.Y &&
                place.Z == captured.Z;
        }
    }
}
