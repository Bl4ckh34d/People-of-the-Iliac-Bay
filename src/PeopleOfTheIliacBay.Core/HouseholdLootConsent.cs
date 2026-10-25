using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class HouseholdLootConsent
    {
        public static bool AllowsPrivateHomeContainer(int settlementId, int buildingKey, Household household,
            bool privateContainer, bool shop, bool inn, bool factionOwned, bool playerOwned,
            bool rentableRoom, bool publicInventory, Func<string, int?> actualDisposition)
        {
            if (settlementId < 0 || buildingKey < 0 || household == null ||
                household.BuildingKey != buildingKey || !privateContainer || shop || inn || factionOwned ||
                playerOwned || rentableRoom || publicInventory || actualDisposition == null)
                return false;

            foreach (var resident in household.Residents)
            {
                if (resident == null || !resident.IsAlive || resident.HomeKey != buildingKey ||
                    string.IsNullOrWhiteSpace(resident.Id)) continue;
                int? disposition = actualDisposition(resident.Id);
                if (disposition.HasValue && disposition.Value >= 75) return true;
            }
            return false;
        }
    }
}
