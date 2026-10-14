using System;
using System.Collections.Generic;
using System.Globalization;

namespace LivingCities.Core
{
    public sealed partial class WorldStateStore
    {
        private static string NativeId(int town, int building, int hash)
        { return string.Format(CultureInfo.InvariantCulture, "npc:{0}:{1}:{2}", town, building, hash); }
        public List<NativePerson> NativePeopleFor(int town)
        { return state.NativePeople.FindAll(p => p.SettlementId == town); }
        public NativePerson RegisterNativePerson(int town, int building, int hash, string name, ResidentSex sex,
            int race, int faction, int archive, int record)
        {
            var person = new NativePerson { Id = NativeId(town, building, hash), Name = name,
                SettlementId = town, WorkKey = building, NativeHash = hash, Sex = sex,
                Race = race, FactionId = faction, TextureArchive = archive, TextureRecord = record };
            ValidateNativePerson(state, person);
            var existing = state.NativePeople.Find(p => p.Id == person.Id);
            if (existing != null)
            {
                Require(existing.Sex == sex && existing.Race == race && existing.FactionId == faction &&
                    existing.TextureArchive == archive && existing.TextureRecord == record, "Native identity metadata changed.");
                return existing; // A temporary quest label cannot rename the saved service identity.
            }
            state.NativePeople.Add(person); return person;
        }
        public bool AssignNativeBed(string id, HomeProfile onSite, IEnumerable<HomeProfile> residentialHomes)
        {
            var person = state.NativePeople.Find(p => p.Id == id);
            Require(person != null && onSite != null && onSite.BuildingKey == person.WorkKey && residentialHomes != null,
                "Invalid native housing request.");
            ValidateNativeHome(onSite);
            if (person.HomeKey >= 0) return true;
            var homes = new SortedDictionary<int, HomeProfile>();
            foreach (var home in residentialHomes)
            {
                ValidateNativeHome(home);
                Require(!homes.ContainsKey(home.BuildingKey), "Duplicate native housing candidate.");
                homes.Add(home.BuildingKey, home);
            }
            if (ReserveNativeBed(person, onSite, false)) return true;
            foreach (var home in homes.Values)
                if (home.BuildingKey != onSite.BuildingKey && ReserveNativeBed(person, home, true)) return true;
            return false;
        }
        private static void ValidateNativeHome(HomeProfile home)
        {
            Require(home != null && home.BuildingKey >= 0 && home.BedCount >= -1 &&
                !double.IsNaN(home.FootprintArea) && !double.IsInfinity(home.FootprintArea) && home.FootprintArea >= 0,
                "Invalid native housing profile.");
        }
        private bool ReserveNativeBed(NativePerson person, HomeProfile home, bool residential)
        {
            if (home.BedCount <= 0) return false;
            var town = state.Settlements.Find(t => t.SettlementId == person.SettlementId);
            var household = town.Households.Find(h => h.BuildingKey == home.BuildingKey);
            if (residential && household == null) return false;
            if (household != null && household.BedCapacity != home.BedCount) return false;
            int occupied = household == null ? 0 : household.Residents.Count;
            var used = new HashSet<int>();
            foreach (var existing in state.NativePeople)
                if (existing.SettlementId == person.SettlementId && existing.HomeKey == home.BuildingKey)
                {
                    if (existing.BedCapacity != home.BedCount) return false;
                    used.Add(existing.BedSlot);
                }
            for (int slot = occupied; slot < home.BedCount; slot++)
                if (!used.Contains(slot))
                {
                    person.HomeKey = home.BuildingKey; person.BedSlot = slot; person.BedCapacity = home.BedCount;
                    return true;
                }
            return false;
        }
        private static void ValidateNativePerson(WorldState world, NativePerson person)
        {
            Require(person != null && person.SettlementId >= 0 && person.WorkKey >= 0 &&
                person.Id == NativeId(person.SettlementId, person.WorkKey, person.NativeHash) &&
                !string.IsNullOrWhiteSpace(person.Name) && person.Name.Length <= 256 &&
                (person.Sex == ResidentSex.Male || person.Sex == ResidentSex.Female) &&
                person.Race >= 0 && person.Race <= 255 && person.FactionId >= 0 &&
                person.TextureArchive >= 0 && person.TextureRecord >= 0 &&
                world.Settlements.Exists(t => t.SettlementId == person.SettlementId), "Invalid native person identity.");
            Require(person.HomeKey == -1 ? person.BedSlot == -1 && person.BedCapacity == 0 :
                person.HomeKey >= 0 && person.BedCapacity > 0 && person.BedCapacity <= 16384 &&
                person.BedSlot >= 0 && person.BedSlot < person.BedCapacity, "Invalid native bed reservation.");
            if (person.HomeKey >= 0)
            {
                var town = world.Settlements.Find(t => t.SettlementId == person.SettlementId);
                var home = town.Households.Find(h => h.BuildingKey == person.HomeKey);
                Require(home != null || person.HomeKey == person.WorkKey, "Native home is neither work site nor known residence.");
                if (home != null) Require(home.BedCapacity == person.BedCapacity && person.BedSlot >= home.Residents.Count,
                    "Native bed overlaps saved family or capacity changed.");
            }
        }
        private static List<NativePerson> CopyNativePeople(WorldState world)
        {
            var result = new List<NativePerson>();
            if (world.NativePeople == null) return result;
            var ids = new HashSet<string>(); var beds = new HashSet<string>(); var capacities = new Dictionary<string, int>();
            foreach (var person in world.NativePeople)
            {
                ValidateNativePerson(world, person); Require(ids.Add(person.Id), "Duplicate native person.");
                if (person.HomeKey >= 0)
                {
                    string home = person.SettlementId + ":" + person.HomeKey;
                    Require(beds.Add(home + ":" + person.BedSlot), "Native people share a bed reservation.");
                    int capacity;
                    Require(!capacities.TryGetValue(home, out capacity) || capacity == person.BedCapacity, "Conflicting native bed capacities.");
                    capacities[home] = person.BedCapacity;
                }
                result.Add(new NativePerson { Id = person.Id, Name = person.Name, SettlementId = person.SettlementId,
                    WorkKey = person.WorkKey, NativeHash = person.NativeHash, Sex = person.Sex, Race = person.Race,
                    FactionId = person.FactionId, TextureArchive = person.TextureArchive, TextureRecord = person.TextureRecord,
                    IsAlive = person.IsAlive, HomeKey = person.HomeKey, BedSlot = person.BedSlot, BedCapacity = person.BedCapacity });
            }
            return result;
        }
    }
}
