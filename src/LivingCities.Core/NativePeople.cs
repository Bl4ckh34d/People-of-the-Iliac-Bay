using System;
using System.Collections.Generic;
using System.Globalization;

namespace LivingCities.Core
{
    public sealed partial class WorldStateStore
    {
        public Household CreateKeeperHousehold(string id, HomeProfile profile)
        {
            ValidateNativeHome(profile);
            var person = state.NativePeople.Find(p => p.Id == id);
            Require(person != null && profile.BuildingKey == person.WorkKey, "Unknown keeper household.");
            var town = state.Settlements.Find(t => t.SettlementId == person.SettlementId);
            var existing = town.Households.Find(h => h.BuildingKey == profile.BuildingKey);
            if (existing != null) return existing.HeadId == person.Id ? existing : null;
            if (!person.IsAlive || person.HomeKey != person.WorkKey || person.BedSlot != 0 || person.BedCapacity != profile.BedCount || profile.BedCount <= 0) return null;
            var staff = state.NativePeople.FindAll(p => p != person && p.SettlementId == person.SettlementId && p.HomeKey == person.HomeKey);
            var slots = new List<int>();
            for (int slot = 0; slot < profile.BedCount; slot++) if (!staff.Exists(p => p.BedSlot == slot)) slots.Add(slot);
            int split = person.Name.LastIndexOf(' ');
            if (split <= 0 || split == person.Name.Length - 1) return null;
            var family = Population.CreateFamilies(state.WorldSeed, person.SettlementId,
                new[] { new HomeProfile(profile.BuildingKey, slots.Count, profile.FootprintArea) }).Households[0];
            family.BedCapacity = profile.BedCount;
            // Two beds in a shop explicitly permit the keeper and a partner.
            if (slots.Count >= 2 && family.Residents.Count == 1)
            {
                var partner = new Resident { Id = "family:" + person.Id + ":partner", HomeKey = profile.BuildingKey,
                    AppearanceSeed = StableSeed.Combine(state.WorldSeed, (uint)person.SettlementId, (uint)person.WorkKey, 1),
                    Sex = ResidentSex.Female };
                family.Residents.Add(partner);
                family.Residents[0].Sex = ResidentSex.Male;
                family.Residents[0].PartnerId = partner.Id; partner.PartnerId = family.Residents[0].Id;
            }
            int headIndex = family.Residents.Count > 1 && person.Sex == ResidentSex.Female ? 1 : 0;
            var head = family.Residents[headIndex]; string previousId = head.Id;
            head.Id = person.Id; head.Sex = person.Sex; head.FullName = person.Name; head.IsAlive = person.IsAlive;
            head.AppearanceSeed = StableSeed.Combine(0, (uint)person.SettlementId, (uint)person.WorkKey, unchecked((uint)person.NativeHash));
            foreach (var member in family.Residents)
            {
                member.Race = person.Race;
                if (member.PartnerId == previousId) member.PartnerId = person.Id;
                if (member.FatherId == previousId) member.FatherId = person.Id;
                if (member.MotherId == previousId) member.MotherId = person.Id;
            }
            family.Residents.RemoveAt(headIndex); family.Residents.Insert(0, head);
            for (int index = 0; index < family.Residents.Count; index++) family.Residents[index].BedSlot = slots[index];
            family.HeadId = person.Id; family.Surname = person.Name.Substring(split + 1);
            head.NativeIdentity = person;
            var candidate = Snapshot();
            candidate.Settlements.Find(t => t.SettlementId == person.SettlementId).Households.Add(family);
            CopyValidated(candidate); // Commit only after all identity, relationship and bed checks pass.
            town.Households.Add(family); return family;
        }
        public CivilianIncident NativeResponse(string id)
        {
            var person = state.NativePeople.Find(p => p.Id == id);
            return person == null || person.ResponseIncidentId == null ? null :
                state.AlarmIncidents.Find(a => a.Id == person.ResponseIncidentId);
        }
        // Caller also requires that the game's crime response has cleared.
        public bool ClearNativeResponse(string id, double now)
        {
            Require(ValidTime(now), "Invalid response time.");
            var person = state.NativePeople.Find(p => p.Id == id);
            Require(person != null, "Unknown native person.");
            var alarm = NativeResponse(id);
            if (alarm != null && now < CivilianAlarms.ActiveUntil(alarm)) return false;
            person.ResponseIncidentId = null; return true;
        }
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
            var occupied = household == null ? new HashSet<int>() : FamilyBedSlots(household);
            var used = new HashSet<int>();
            foreach (var existing in state.NativePeople)
                if (existing.SettlementId == person.SettlementId && existing.HomeKey == home.BuildingKey)
                {
                    if (existing.BedCapacity != home.BedCount) return false;
                    used.Add(existing.BedSlot);
                }
            for (int slot = 0; slot < home.BedCount; slot++)
                if (!used.Contains(slot) && !occupied.Contains(slot))
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
            Require(person.ResponseIncidentId == null || world.AlarmIncidents.Exists(a => a.Id == person.ResponseIncidentId &&
                a.SettlementId == person.SettlementId && a.People.Exists(p => p.ResidentId == person.Id)), "Invalid native response binding.");
            if (person.HomeKey >= 0)
            {
                var town = world.Settlements.Find(t => t.SettlementId == person.SettlementId);
                var home = town.Households.Find(h => h.BuildingKey == person.HomeKey);
                Require(home != null || person.HomeKey == person.WorkKey, "Native home is neither work site nor known residence.");
                if (home != null) Require(home.BedCapacity == person.BedCapacity && (home.HeadId == person.Id ?
                    person.BedSlot == 0 && home.Residents[0].Id == person.Id && (home.Residents[0].BedSlot == -1 || home.Residents[0].BedSlot == 0) &&
                    home.Residents[0].Sex == person.Sex && home.Residents[0].IsAlive == person.IsAlive && home.Residents[0].FullName == person.Name &&
                    home.Residents[0].Race == person.Race : !FamilyBedSlots(home).Contains(person.BedSlot)),
                    "Native bed overlaps saved family or capacity changed.");
            }
        }
        private static HashSet<int> FamilyBedSlots(Household home)
        {
            var slots = new HashSet<int>();
            for (int index = 0; index < home.Residents.Count; index++)
                slots.Add(home.Residents[index].BedSlot >= 0 ? home.Residents[index].BedSlot : index);
            return slots;
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
                    IsAlive = person.IsAlive, HomeKey = person.HomeKey, BedSlot = person.BedSlot, BedCapacity = person.BedCapacity,
                    ResponseIncidentId = person.ResponseIncidentId });
            }
            return result;
        }
    }
}
