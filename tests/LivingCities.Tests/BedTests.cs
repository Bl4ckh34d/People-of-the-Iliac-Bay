using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class BedTests
    {
        private static Household Family()
        {
            return new Household { BuildingKey = 7, Residents = new List<Resident> {
                new Resident { Id = "father", HomeKey = 7, PartnerId = "mother" },
                new Resident { Id = "mother", HomeKey = 7, PartnerId = "father" },
                new Resident { Id = "child-a", HomeKey = 7, AgeGroup = ResidentAgeGroup.Child, FatherId = "father", MotherId = "mother" },
                new Resident { Id = "child-b", HomeKey = 7, AgeGroup = ResidentAgeGroup.Child, FatherId = "father", MotherId = "mother" },
                new Resident { Id = "child-c", HomeKey = 7, AgeGroup = ResidentAgeGroup.Child, FatherId = "father", MotherId = "mother" } } };
        }
        private static string Signature(IEnumerable<BedAssignment> assignments)
        { return string.Join("|", assignments.OrderBy(a => a.ResidentId).Select(a => a.ResidentId + ":" + a.BedRecordIndex + ":" + a.SharingSlot + ":" + a.SharingCount)); }

        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Every family member including partners has an exclusive bed", () => {
                var beds = HomeBeds.Assign(Family(), new[] { 9, 3, 15, 20, 25 });
                var father = beds.Single(a => a.ResidentId == "father");
                var mother = beds.Single(a => a.ResidentId == "mother");
                Check.True(father.BedRecordIndex != mother.BedRecordIndex);
                Check.True(beds.All(a => a.SharingSlot == 0 && a.SharingCount == 1));
                Check.Equal(5, beds.Select(a => a.BedRecordIndex).Distinct().Count());
                Check.True(beds.Where(a => a.ResidentId.StartsWith("child")).All(a => a.BedRecordIndex != father.BedRecordIndex));
            }));
            cases.Add(("Legacy oversized home cannot create shared bed claims or lose saved identities", () => {
                var home = Family();
                var beds = HomeBeds.Assign(home, new[] { 42 });
                Check.Equal(1, beds.Count);
                Check.True(beds.All(a => a.BedRecordIndex == 42 && a.SharingCount == 1));
                Check.Equal(1, beds.Select(a => a.SharingSlot).Distinct().Count());
                Check.True(beds.All(a => a.SharingSlot >= 0 && a.SharingSlot < a.SharingCount));
                Check.Equal(5, home.Residents.Count);
            }));
            cases.Add(("Bed claims survive enumeration changes and serialized reload", () => {
                var home = Family(); var options = new JsonSerializerOptions { IncludeFields = true };
                string before = Signature(HomeBeds.Assign(home, new[] { 42, 7 }));
                var restored = JsonSerializer.Deserialize<Household>(JsonSerializer.Serialize(home, options), options);
                restored.Residents.Reverse();
                Check.Equal(before, Signature(HomeBeds.Assign(restored, new[] { 7, 42 })));
            }));
            cases.Add(("Deaths leave survivors in their existing bed slots", () => {
                var home = Family();
                var before = HomeBeds.Assign(home, new[] { 8, 9, 10, 11, 12 });
                home.Residents[0].IsAlive = false; home.Residents[2].IsAlive = false;
                var after = HomeBeds.Assign(home, new[] { 8, 9, 10, 11, 12 });
                Check.Equal(3, after.Count);
                Check.Equal(Signature(before.Where(a => a.ResidentId != "father" && a.ResidentId != "child-a")), Signature(after));
            }));
            cases.Add(("Missing beds produce no invented claims or population changes", () => {
                var home = Family(); Check.Equal(0, HomeBeds.Assign(home, new int[0]).Count);
                Check.Equal(5, home.Residents.Count);
                home.Residents.ForEach(r => r.IsAlive = false);
                Check.Equal(0, HomeBeds.Assign(home, new[] { 4 }).Count);
            }));
            cases.Add(("Visitors cannot consume household beds without booking authority", () => {
                var home = Family(); home.Residents.Add(new Resident { Id = "visitor", HomeKey = 88 });
                Check.Throws<ArgumentException>(() => HomeBeds.Assign(home, new[] { 2 }));
            }));
            cases.Add(("Duplicate identities and bed keys are rejected", () => {
                Check.Throws<ArgumentException>(() => HomeBeds.Assign(Family(), new[] { 2, 2 }));
                Check.Throws<ArgumentException>(() => HomeBeds.Assign(Family(), new[] { -1 }));
                var home = Family(); home.Residents.Add(home.Residents[0]);
                Check.Throws<ArgumentException>(() => HomeBeds.Assign(home, new[] { 2 }));
            }));
        }
    }
}
