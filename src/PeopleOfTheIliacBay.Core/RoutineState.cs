using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class SavedRoutePoint
    {
        public double X, Z;
    }
    [Serializable]
    public sealed class ResidentDailyOuting
    {
        public double DepartureSeconds;
        public ResidentDestination Itinerary;
    }
    [Serializable]
    public sealed class ResidentRoutine
    {
        public string ResidentId;
        public int RouteGeometryVersion;
        public int PersonalLaneVersion;
        public int DestinationKey = -1;
        public int OpenMinute, CloseMinute;
        public List<SavedRoutePoint> Route = new List<SavedRoutePoint>();
        public ResidentDestination Itinerary;
        public double ItineraryDepartureSeconds;
        public List<ResidentDailyOuting> AdditionalOutings=new List<ResidentDailyOuting>();
    }
    [Serializable]
    public sealed class TownRoutineState
    {
        public int SettlementId;
        public int ItineraryVersion;
        public List<ResidentRoutine> Residents = new List<ResidentRoutine>();
    }
}
