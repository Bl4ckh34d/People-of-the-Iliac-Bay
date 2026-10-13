using System;
using System.Collections.Generic;
using System.Globalization;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class Resident
    {
        public string Id;
        public int HomeKey;
        public uint AppearanceSeed;
        public bool IsAlive = true;
        public bool IsQuestProtected;
    }

    [Serializable]
    public sealed class Household
    {
        public int BuildingKey;
        public List<Resident> Residents = new List<Resident>();
    }

    [Serializable]
    public sealed class SettlementPopulation
    {
        public int SettlementId;
        public List<Household> Households = new List<Household>();
    }

    public static class Population
    {
        public static SettlementPopulation Create(uint seed, int settlementId, IEnumerable<int> homeKeys)
        {
            if (settlementId < 0) throw new ArgumentOutOfRangeException(nameof(settlementId));
            if (homeKeys == null) throw new ArgumentNullException(nameof(homeKeys));
            var homes = new SortedSet<int>();
            foreach (int key in homeKeys)
            {
                if (key < 0) throw new ArgumentOutOfRangeException(nameof(homeKeys));
                if (!homes.Add(key)) throw new ArgumentException("Duplicate home key", nameof(homeKeys));
            }
            var population = new SettlementPopulation { SettlementId = settlementId };
            foreach (int key in homes)
            {
                var household = new Household { BuildingKey = key };
                int count = 1 + (int)(StableSeed.Combine(seed, (uint)settlementId, (uint)key, uint.MaxValue) % 3);
                for (int slot = 0; slot < count; slot++)
                {
                    household.Residents.Add(new Resident
                    {
                        Id = string.Format(CultureInfo.InvariantCulture, "s:{0}:h:{1}:r:{2}", settlementId, key, slot),
                        HomeKey = key,
                        AppearanceSeed = StableSeed.Combine(seed, (uint)settlementId, (uint)key, (uint)slot)
                    });
                }
                population.Households.Add(household);
            }
            return population;
        }

        public static bool MarkDead(SettlementPopulation settlement, string residentId)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (residentId == null) throw new ArgumentNullException(nameof(residentId));
            foreach (Household home in settlement.Households)
            foreach (Resident resident in home.Residents)
            {
                if (resident.Id != residentId) continue;
                if (!resident.IsAlive || resident.IsQuestProtected) return false;
                resident.IsAlive = false;
                return true;
            }
            throw new KeyNotFoundException("No resident with ID " + residentId);
        }
    }
}
