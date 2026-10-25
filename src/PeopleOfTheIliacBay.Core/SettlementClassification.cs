using System;
namespace PeopleOfTheIliacBay.Core
{
    public static class SettlementClassification
    {
        public static bool SupportsPopulation(int locationType) { return locationType>=0 && locationType<=2; }
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
