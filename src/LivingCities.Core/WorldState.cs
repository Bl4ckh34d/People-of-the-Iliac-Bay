using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class WorldState
    {
        public int Version = 1;
        public uint WorldSeed;
        public List<SettlementPopulation> Settlements = new List<SettlementPopulation>();
    }
    public sealed class WorldStateStore
    {
        private WorldState state;
        public WorldStateStore(uint seed) { state = new WorldState { WorldSeed = seed }; }
        public SettlementPopulation GetOrCreate(int settlementId, IEnumerable<int> homeKeys, int maxResidents = 3)
        {
            foreach (SettlementPopulation existing in state.Settlements)
                if (existing.SettlementId == settlementId) return existing;
            SettlementPopulation created = Population.Create(state.WorldSeed, settlementId, homeKeys, maxResidents);
            state.Settlements.Add(created);
            return created;
        }
        public WorldState Snapshot() { return CopyValidated(state); }
        public void Restore(WorldState incoming) { state = CopyValidated(incoming); }

        private static WorldState CopyValidated(WorldState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Require(source.Version == 1, "Unsupported save schema.");
            Require(source.Settlements != null, "Missing settlements.");
            var copy = new WorldState { WorldSeed = source.WorldSeed };
            var settlements = new HashSet<int>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (SettlementPopulation town in source.Settlements)
            {
                Require(town != null, "Null settlement.");
                Require(town.SettlementId >= 0 && settlements.Add(town.SettlementId), "Invalid or duplicate settlement.");
                Require(town.Households != null, "Missing households.");
                var townCopy = new SettlementPopulation { SettlementId = town.SettlementId };
                var homes = new HashSet<int>();
                foreach (Household home in town.Households)
                {
                    Require(home != null, "Null household.");
                    Require(home.BuildingKey >= 0 && homes.Add(home.BuildingKey), "Invalid or duplicate household.");
                    Require(home.Residents != null && home.Residents.Count <= 3, "Missing or overcrowded household residents.");
                    var homeCopy = new Household { BuildingKey = home.BuildingKey };
                    foreach (Resident resident in home.Residents)
                    {
                        Require(resident != null, "Null resident.");
                        Require(!string.IsNullOrWhiteSpace(resident.Id) && identities.Add(resident.Id), "Missing or duplicate resident identity.");
                        Require(resident.HomeKey == home.BuildingKey, "Resident belongs to a different home.");
                        Require(resident.IsAlive || !resident.IsQuestProtected, "Protected resident cannot be dead.");
                        homeCopy.Residents.Add(new Resident { Id = resident.Id, HomeKey = resident.HomeKey,
                            AppearanceSeed = resident.AppearanceSeed, IsAlive = resident.IsAlive, IsQuestProtected = resident.IsQuestProtected });
                    }
                    townCopy.Households.Add(homeCopy);
                }
                copy.Settlements.Add(townCopy);
            }
            return copy;
        }
        private static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
    }
}
