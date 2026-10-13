using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class SavedRoutePoint
    {
        public double X, Z;
    }
    [Serializable]
    public sealed class ResidentRoutine
    {
        public string ResidentId;
        public int DestinationKey = -1;
        public int OpenMinute, CloseMinute;
        public List<SavedRoutePoint> Route = new List<SavedRoutePoint>();
    }
    [Serializable]
    public sealed class TownRoutineState
    {
        public int SettlementId;
        public List<ResidentRoutine> Residents = new List<ResidentRoutine>();
    }
}
