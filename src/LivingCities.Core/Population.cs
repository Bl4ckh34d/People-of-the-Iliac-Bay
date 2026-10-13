using System;
using System.Collections.Generic;
using System.Globalization;

namespace LivingCities.Core
{
    public enum ResidentAgeGroup { Adult, Child }
    public enum ResidentSex { Male, Female }
    public sealed class HomeProfile
    {
        public readonly int BuildingKey, BedCount;
        public readonly double FootprintArea;
        public HomeProfile(int buildingKey, int bedCount = -1, double footprintArea = 0)
        { BuildingKey = buildingKey; BedCount = bedCount; FootprintArea = footprintArea; }
    }
    [Serializable]
    public sealed class Resident
    {
        public string Id;
        public int HomeKey;
        public uint AppearanceSeed;
        public bool IsAlive = true;
        public bool IsQuestProtected;
        public ResidentAgeGroup AgeGroup;
        public ResidentSex Sex;
        public string PartnerId, MotherId, FatherId;
    }

    [Serializable]
    public sealed class Household
    {
        public int BuildingKey;
        public uint SurnameSeed;
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
        public static SettlementPopulation CreateFamilies(uint seed, int settlementId, IEnumerable<HomeProfile> homes, int maxResidents = 5)
        {
            if (settlementId < 0) throw new ArgumentOutOfRangeException(nameof(settlementId));
            if (maxResidents < 1 || maxResidents > 5) throw new ArgumentOutOfRangeException(nameof(maxResidents));
            if (homes == null) throw new ArgumentNullException(nameof(homes));
            var profiles = new SortedDictionary<int, HomeProfile>();
            foreach (var profile in homes)
            {
                if (profile == null) throw new ArgumentException("Null home profile.", nameof(homes));
                if (profile.BuildingKey < 0 || profile.BedCount < -1 || double.IsNaN(profile.FootprintArea) ||
                    double.IsInfinity(profile.FootprintArea) || profile.FootprintArea < 0)
                    throw new ArgumentOutOfRangeException(nameof(homes));
                if (profiles.ContainsKey(profile.BuildingKey)) throw new ArgumentException("Duplicate home key.", nameof(homes));
                profiles.Add(profile.BuildingKey, profile);
            }
            var population = new SettlementPopulation { SettlementId = settlementId };
            foreach (var pair in profiles)
            {
                var profile = pair.Value;
                double area = profile.FootprintArea == 0 ? 0.4 : Math.Max(0, Math.Min(1, (profile.FootprintArea - 30) / 220));
                double beds = profile.BedCount < 0 ? 0.4 : Math.Min(1, profile.BedCount / 6.0);
                double sizePreference = area * 0.75 + beds * 0.25;
                double roll = StableSeed.Combine(seed, (uint)settlementId, (uint)pair.Key, uint.MaxValue) / 4294967296.0;
                // Beds influence probability only. Even a small one-bed home can hold
                // two parents and three children sharing beds, but this is uncommon.
                double[] cumulative = { 0.32 - 0.18 * sizePreference, 0.70 - 0.31 * sizePreference,
                    0.92 - 0.25 * sizePreference, 0.99 - 0.08 * sizePreference };
                int count = 1;
                while (count < maxResidents && roll >= cumulative[count - 1]) count++;
                var household = new Household { BuildingKey = pair.Key,
                    SurnameSeed = StableSeed.Combine(seed, (uint)settlementId, (uint)pair.Key, 0xfffffffe) };
                for (int slot = 0; slot < count; slot++)
                {
                    uint appearance = StableSeed.Combine(seed, (uint)settlementId, (uint)pair.Key, (uint)slot);
                    household.Residents.Add(new Resident {
                        Id = string.Format(CultureInfo.InvariantCulture, "s:{0}:h:{1}:r:{2}", settlementId, pair.Key, slot),
                        HomeKey = pair.Key, AppearanceSeed = appearance,
                        AgeGroup = slot < 2 ? ResidentAgeGroup.Adult : ResidentAgeGroup.Child,
                        Sex = count > 1 && slot < 2 ? (slot == 0 ? ResidentSex.Male : ResidentSex.Female) :
                            ((appearance & 1) == 0 ? ResidentSex.Male : ResidentSex.Female) });
                }
                if (count >= 2)
                {
                    var father = household.Residents[0]; var mother = household.Residents[1];
                    father.PartnerId = mother.Id; mother.PartnerId = father.Id;
                    for (int i = 2; i < count; i++)
                    { household.Residents[i].FatherId = father.Id; household.Residents[i].MotherId = mother.Id; }
                }
                population.Households.Add(household);
            }
            return population;
        }
        public static SettlementPopulation Create(uint seed, int settlementId, IEnumerable<int> homeKeys, int maxResidents = 3)
        {
            if (maxResidents < 1 || maxResidents > 3) throw new ArgumentOutOfRangeException(nameof(maxResidents));
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
                var household = new Household { BuildingKey = key,
                    SurnameSeed = StableSeed.Combine(seed, (uint)settlementId, (uint)key, 0xfffffffe) };
                int count = 1 + (int)(StableSeed.Combine(seed, (uint)settlementId, (uint)key, uint.MaxValue) % (uint)maxResidents);
                for (int slot = 0; slot < count; slot++)
                {
                    household.Residents.Add(new Resident
                    {
                        Id = string.Format(CultureInfo.InvariantCulture, "s:{0}:h:{1}:r:{2}", settlementId, key, slot),
                        HomeKey = key,
                        AppearanceSeed = StableSeed.Combine(seed, (uint)settlementId, (uint)key, (uint)slot),
                        Sex = (StableSeed.Combine(seed, (uint)settlementId, (uint)key, (uint)slot) & 1) == 0 ? ResidentSex.Male : ResidentSex.Female
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
