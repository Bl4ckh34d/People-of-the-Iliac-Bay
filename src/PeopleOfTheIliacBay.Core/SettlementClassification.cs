using System;
namespace PeopleOfTheIliacBay.Core
{
    public static class SettlementClassification
    {
        public static bool SupportsPopulation(int locationType) { return locationType>=0 && locationType<=2; }
        // Guard ownership includes native civilian sites without enabling resident
        // family schedules or adding them to the ordinary traveler-origin pool.
        public static bool SupportsGuards(int locationType)
        {
            switch(locationType)
            {
                case 0: case 1: case 2: case 3: case 5: case 6: case 8:return true;
                default:return false;
            }
        }
        public static SettlementScale GuardScale(int locationType)
        {
            if(SupportsPopulation(locationType))return FromLocationType(locationType);
            if(SupportsGuards(locationType))return SettlementScale.Hamlet;
            throw new ArgumentOutOfRangeException(nameof(locationType),"Location does not support civilian guards.");
        }
        // Original DFRegion.LocationTypes values. Keep the core independent of DFU.
        public static SettlementScale FromLocationType(int locationType)
        {
            switch(locationType)
            {
                case 0: return SettlementScale.City;
                case 1: return SettlementScale.Town;
                case 2: return SettlementScale.Hamlet;
                default: throw new ArgumentOutOfRangeException(nameof(locationType),"Location is not a town, village or city.");
            }
        }
    }
}
