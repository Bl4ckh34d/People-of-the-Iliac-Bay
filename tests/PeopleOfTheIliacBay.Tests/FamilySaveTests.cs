using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class FamilySaveTests
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
        private static string Encode(WorldState state) => JsonSerializer.Serialize(state, Json);
        private static WorldState Family()
        {
            var home = new Household { BuildingKey = 10, SurnameSeed = 987654 };
            home.Residents.Add(new Resident { Id = "father", HomeKey = 10, AppearanceSeed = 1,
                Sex = ResidentSex.Male, PartnerId = "mother", IsAlive = false });
            home.Residents.Add(new Resident { Id = "mother", HomeKey = 10, AppearanceSeed = 2,
                Sex = ResidentSex.Female, PartnerId = "father" });
            for (int i = 0; i < 3; i++) home.Residents.Add(new Resident { Id = "child" + i, HomeKey = 10,
                AgeGroup = ResidentAgeGroup.Child, Sex = i == 1 ? ResidentSex.Female : ResidentSex.Male,
                AppearanceSeed = (uint)(8 + i), FatherId = "father", MotherId = "mother" });
            return new WorldState { WorldSeed = 42, Settlements = new List<SettlementPopulation> {
                new SettlementPopulation { SettlementId = 100, Households = new List<Household> { home } } } };
        }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Self household knowledge counts living co-residents separately from children and deceased partner", (Action)(() => {
                var store = new WorldStateStore(0); store.Restore(Family());
                string before = Encode(store.Snapshot());
                var known = store.ReadNpcHouseholdKnowledge("mother");
                Check.Equal(4, known.LivingHouseholdCount);
                Check.Equal(3, known.Children.Count);
                Check.Equal("father", known.Partner.PersonId);
                Check.True(!known.Partner.IsAlive);
                Check.Equal(before, Encode(store.Snapshot()));
                Check.True(store.ReadNpcHouseholdKnowledge("unmanaged") == null);
                Check.Equal(before, Encode(store.Snapshot()));
                var restored = new WorldStateStore(0); restored.Restore(Family());
                Check.Equal(known.ToContext(), restored.ReadNpcHouseholdKnowledge("mother").ToContext());
            })));
            cases.Add(("Captured household knowledge stays detached and distinguishes childless from unavailable", (Action)(() => {
                var store = new WorldStateStore(0); store.Restore(Family());
                var old = store.ReadNpcHouseholdKnowledge("mother");
                var state = Family(); state.Settlements[0].Households[0].Residents[2].IsAlive = false;
                store.Restore(state);
                var current = store.ReadNpcHouseholdKnowledge("mother");
                Check.Equal(4, old.LivingHouseholdCount);
                Check.Equal(3, current.LivingHouseholdCount);
                Check.Equal(3, current.Children.Count);
                Check.Equal(2, current.Children.Count(x => x.IsAlive));
                var child = store.ReadNpcHouseholdKnowledge("child1");
                Check.True(child != null && child.Partner == null && child.Children.Count == 0);
                Check.True(child.ToContext().Contains("no recorded partner"));
                Check.True(child.ToContext().Contains("does not prove childlessness"));
                Check.True(current.ToContext().Contains("Disclosure is optional"));
                Check.True(current.ToContext().Contains("Marriage ceremony/status is not separately recorded"));
            })));
            cases.Add(("Family creation keeps saved residents when settings or housing change", () => {
                var store = new WorldStateStore(42);
                var profiles = Enumerable.Range(1, 100).Select(k => new HomeProfile(k, 5, 200));
                var town = store.GetOrCreateFamilies(100, profiles);
                Check.True(town.Households.Any(h => h.Residents.Count == 5));
                var father = town.Households.First(h => h.Residents.Count == 5).Residents[0];
                father.IsAlive = false; string before = Encode(store.Snapshot());
                Check.True(object.ReferenceEquals(town, store.GetOrCreateFamilies(100, new[] { new HomeProfile(999) })));
                Check.Equal(before, Encode(store.Snapshot()));
                store.SetRoutines(Visit(father.Id)); father.IsAlive = true;
                Check.True(store.Snapshot().Settlements[0].Households.SelectMany(h => h.Residents).Single(r => r.Id == father.Id).IsAlive);
                var restored = new WorldStateStore(7); restored.Restore(store.Snapshot());
                Check.Equal(PopulationTests.Signature(store.GetOrCreateFamilies(200, profiles)),
                    PopulationTests.Signature(restored.GetOrCreateFamilies(200, profiles)));
            }));
            cases.Add(("Family save preserves five identities, parent links and deceased relatives", () => {
                var incoming = Family(); var store = new WorldStateStore(0);
                store.Restore(JsonSerializer.Deserialize<WorldState>(Encode(incoming), Json));
                Check.Equal(Encode(incoming), Encode(store.Snapshot()));
                var copy = store.Snapshot(); copy.Settlements[0].Households[0].Residents[2].FatherId = "changed";
                incoming.Settlements[0].Households[0].SurnameSeed = 0;
                Check.Equal("father", store.Snapshot().Settlements[0].Households[0].Residents[2].FatherId);
                Check.Equal(987654u, store.Snapshot().Settlements[0].Households[0].SurnameSeed);
            }));
            cases.Add(("Legacy saves retain adults and historical appearances without inventing family ties", () => {
                foreach (int version in new[] { 1, 2 })
                {
                    // Actual old field shape: no surname, sex, age or relationship fields.
                    string json = "{\"Version\":" + version + ",\"WorldSeed\":42,\"Settlements\":[{\"SettlementId\":100,\"Households\":[{\"BuildingKey\":10,\"Residents\":[{\"Id\":\"old0\",\"HomeKey\":10,\"AppearanceSeed\":2,\"IsAlive\":false},{\"Id\":\"old1\",\"HomeKey\":10,\"AppearanceSeed\":3}]}]}],\"Routines\":[]}";
                    var old = JsonSerializer.Deserialize<WorldState>(json, Json);
                    if (version == 1) old.Routines = null;
                    else old.Routines.Add(Visit("old1"));
                    var store = new WorldStateStore(0); store.Restore(old); var saved = store.Snapshot();
                    Check.Equal(new WorldState().Version, saved.Version); var home = saved.Settlements[0].Households[0];
                    Check.Equal(2, home.Residents.Count);
                    Check.Equal(StableSeed.Combine(42, 100, 10, 0xfffffffe), home.SurnameSeed);
                    Check.Equal("old0", home.Residents[0].Id); Check.Equal(2u, home.Residents[0].AppearanceSeed);
                    Check.True(!home.Residents[0].IsAlive); Check.Equal(ResidentSex.Male, home.Residents[0].Sex);
                    Check.Equal(ResidentSex.Female, home.Residents[1].Sex);
                    Check.True(home.Residents.All(r => r.AgeGroup == ResidentAgeGroup.Adult && r.PartnerId == null && r.MotherId == null && r.FatherId == null));
                    Check.True(store.ReadNpcHouseholdKnowledge("old1").ToContext().Contains("does not prove childlessness"));
                    Check.Equal(version == 1 ? 0 : 1, saved.Routines.Count);
                    if (version == 2) Check.Equal(12d, saved.Routines[0].Residents[0].Route[1].X);
                }
            }));
            var corruptions = new Dictionary<string, Action<Household>> {
                { "unknown age", h => h.Residents[2].AgeGroup = (ResidentAgeGroup)99 },
                { "unknown sex", h => h.Residents[0].Sex = (ResidentSex)99 },
                { "missing parent", h => h.Residents[2].FatherId = "absent" },
                { "missing mother", h => h.Residents[2].MotherId = null },
                { "self parent", h => h.Residents[2].FatherId = "child0" },
                { "child parent", h => h.Residents[2].FatherId = "child1" },
                { "wrong parent role", h => h.Residents[2].FatherId = "mother" },
                { "child partner", h => h.Residents[2].PartnerId = "child1" },
                { "self partner", h => h.Residents[0].PartnerId = "father" },
                { "unknown partner", h => h.Residents[0].PartnerId = "absent" },
                { "nonreciprocal partner", h => h.Residents[1].PartnerId = null },
                { "parent cycle", h => h.Residents[0].FatherId = "father" }
            };
            foreach (var item in corruptions)
                cases.Add(("Family restore rejects " + item.Key + " transactionally", () => {
                    var store = new WorldStateStore(0); store.Restore(Family()); string before = Encode(store.Snapshot());
                    var incoming = Family(); item.Value(incoming.Settlements[0].Households[0]);
                    Check.Throws<ArgumentException>(() => store.Restore(incoming)); Check.Equal(before, Encode(store.Snapshot()));
                }));
            cases.Add(("Children cannot restore adult visit routes", () => {
                var store = new WorldStateStore(0); store.Restore(Family()); string before = Encode(store.Snapshot());
                Check.Throws<ArgumentException>(() => store.SetRoutines(Visit("child0")));
                Check.Equal(before, Encode(store.Snapshot()));
                var home = new TownRoutineState { SettlementId = 100 };
                home.Residents.Add(new ResidentRoutine { ResidentId = "child0" }); store.SetRoutines(home);
                Check.Equal(-1, store.GetRoutines(100).Residents[0].DestinationKey);
            }));
        }
        private static TownRoutineState Visit(string id) => new TownRoutineState { SettlementId = 100,
            Residents = new List<ResidentRoutine> { new ResidentRoutine { ResidentId = id, DestinationKey = 20,
                OpenMinute = 600, CloseMinute = 900, Route = new List<SavedRoutePoint> {
                    new SavedRoutePoint(), new SavedRoutePoint { X = 12 } } } } };
    }
}
