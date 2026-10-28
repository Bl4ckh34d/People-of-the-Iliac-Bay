using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        // A surveyed exterior person joins an existing household in a real spare
        // bed. Membership does not invent a marriage, parent or child relation.
        public bool AssignExteriorNativeBed(string id, IEnumerable<HomeProfile> residentialHomes)
        {
            var person = IndexedNativePerson(id);
            Require(person != null && person.WorkKey == -1 && person.NativeLayoutKey == null && residentialHomes != null,
                "Invalid exterior native housing request.");
            if (!person.IsAlive || person.RetiredBedroomFlat) return false;
            var town = state.Settlements.Find(t => t.SettlementId == person.SettlementId);
            var existing = town.Households.Find(h => h.BuildingKey == person.HomeKey);
            if (existing != null && existing.Residents.Exists(r => r.Id == id)) return true;
            var homes = new SortedDictionary<int, HomeProfile>();
            foreach (var profile in residentialHomes)
            {
                ValidateNativeHome(profile);
                Require(!homes.ContainsKey(profile.BuildingKey), "Duplicate exterior native housing candidate.");
                homes.Add(profile.BuildingKey, profile);
            }
            foreach (var profile in homes.Values)
            {
                if (profile.BedCount <= 0 || !profile.AvailableForResidents ||
                    HousingReservedForRecovery(person.SettlementId, profile.BuildingKey) ||
                    person.HomeKey >= 0 && person.HomeKey != profile.BuildingKey) continue;
                var home = town.Households.Find(h => h.BuildingKey == profile.BuildingKey);
                if (home == null || home.BedCapacity != profile.BedCount || string.IsNullOrWhiteSpace(home.Surname)) continue;
                var used = FamilyBedSlots(home);
                foreach (var other in state.NativePeople)
                    if (other.Id != id && other.SettlementId == person.SettlementId && other.HomeKey == profile.BuildingKey)
                        used.Add(other.BedSlot);
                foreach (var lodger in HouseholdInnLodgers(person.SettlementId, profile.BuildingKey)) used.Add(lodger.BedSlot);
                for (int slot = 0; slot < profile.BedCount; slot++)
                {
                    if (used.Contains(slot) || person.HomeKey >= 0 && person.BedSlot != slot) continue;
                    int split = person.Name.IndexOf(' ');
                    string name = (split > 0 ? person.Name.Substring(0, split) : person.Name) + " " + home.Surname;
                    var member = new Resident { Id = id, FullName = name, HomeKey = profile.BuildingKey,
                        BedSlot = slot, Sex = person.Sex, Race = person.Race, IsAlive = person.IsAlive,
                        AgeGroup = ResidentAgeGroup.Adult,
                        AppearanceSeed = StableSeed.Combine(state.WorldSeed, (uint)person.SettlementId,
                            (uint)profile.BuildingKey, unchecked((uint)person.NativeHash)) };
                    var candidate = Snapshot();
                    var proposed = candidate.NativePeople.Find(p => p.Id == id);
                    proposed.HomeKey = profile.BuildingKey; proposed.BedSlot = slot;
                    proposed.BedCapacity = profile.BedCount; proposed.Name = name;
                    candidate.Settlements.Find(t => t.SettlementId == person.SettlementId).Households
                        .Find(h => h.BuildingKey == profile.BuildingKey).Residents.Add(member);
                    CopyValidated(candidate);
                    person.HomeKey = proposed.HomeKey; person.BedSlot = slot;
                    person.BedCapacity = profile.BedCount; person.Name = name;
                    member.NativeIdentity = person; home.Residents.Add(member);
                    return true;
                }
            }
            return false;
        }
    }
}
