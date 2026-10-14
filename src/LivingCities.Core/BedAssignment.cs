using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public sealed class BedAssignment
    {
        public string ResidentId;
        public int BedRecordIndex, SharingSlot, SharingCount;
    }

    public static class HomeBeds
    {
        public static List<BedAssignment> Assign(Household home, IEnumerable<int> bedRecordIndices)
        {
            if (home == null || home.Residents == null) throw new ArgumentNullException(nameof(home));
            if (bedRecordIndices == null) throw new ArgumentNullException(nameof(bedRecordIndices));
            var beds = new SortedDictionary<int, List<Resident>>();
            foreach (int index in bedRecordIndices)
            {
                if (index < 0 || beds.ContainsKey(index)) throw new ArgumentException("Invalid or duplicate bed record.", nameof(bedRecordIndices));
                beds.Add(index, new List<Resident>());
            }
            var residents = new SortedDictionary<string, Resident>(StringComparer.Ordinal);
            foreach (var resident in home.Residents)
            {
                if (resident == null || string.IsNullOrEmpty(resident.Id) || resident.HomeKey != home.BuildingKey || residents.ContainsKey(resident.Id))
                    throw new ArgumentException("Invalid household resident or duplicate identity.", nameof(home));
                residents.Add(resident.Id, resident);
            }
            var result = new List<BedAssignment>();
            if (beds.Count == 0) return result;
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            var records = new List<int>(beds.Keys);
            foreach (var resident in home.Residents)
                if (resident.BedSlot >= 0)
                {
                    assigned.Add(resident.Id);
                    if (resident.BedSlot >= records.Count) continue;
                    var occupants = beds[records[resident.BedSlot]];
                    if (occupants.Count != 0) throw new ArgumentException("Duplicate explicit resident bed.", nameof(home));
                    occupants.Add(resident);
                }
            Resident head;
            if (home.HeadId != null && residents.TryGetValue(home.HeadId, out head) && assigned.Add(head.Id))
            {
                foreach (var occupants in beds.Values)
                { if (occupants.Count != 0) throw new ArgumentException("Head bed already occupied.", nameof(home)); occupants.Add(head); break; }
            }
            // Allocate against permanent membership, including deceased residents.
            // This reserves their old slots so survivors do not shift on death/load.
            // Replacement households have new identities and receive new assignments.
            foreach (var age in new[] { ResidentAgeGroup.Adult, ResidentAgeGroup.Child })
                foreach (var resident in residents.Values)
                {
                    if (resident.AgeGroup != age || !assigned.Add(resident.Id)) continue;
                    List<Resident> target = null;
                    foreach (var occupants in beds.Values)
                        if (occupants.Count == 0) { target = occupants; break; }
                    if (target == null) continue;
                    target.Add(resident);
                }
            foreach (var bed in beds)
                for (int slot = 0; slot < bed.Value.Count; slot++)
                    if (bed.Value[slot].IsAlive) result.Add(new BedAssignment {
                        ResidentId = bed.Value[slot].Id, BedRecordIndex = bed.Key,
                        SharingSlot = slot, SharingCount = bed.Value.Count });
            return result;
        }
    }
}
