using System;
using System.Collections.Generic;
using System.Text.Json;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class QuestBindingTests
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
        private static string Encode(WorldStateStore store) => JsonSerializer.Serialize(store.Snapshot(), Json);
        private static WorldStateStore Filled()
        {
            var store = new WorldStateStore(42);
            store.GetOrCreate(100, new[] { 10, 20 });
            return store;
        }
        private static QuestResidentBinding Candidate(WorldStateStore store, ulong uid = 1001, int home = 10)
        {
            var town = store.GetOrCreate(100, new[] { 10, 20 });
            return new QuestResidentBinding { QuestUid = uid, QuestName = "_DELIVERY_", PersonSymbol = "_recipient_",
                SettlementId = 100, HomeKey = home, ResidentId = town.Households.Find(h => h.BuildingKey == home).Residents[0].Id,
                PersonName = home == 10 ? "Anna Ashfield" : "Robin Coppercroft",
                HomeName = home == 10 ? "The Ashfield Residence" : "The Coppercroft Residence", RuleId = "test/permanent-resident/v1" };
        }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Rejected offer leaves world and future offers unchanged", () => {
                var store = Filled(); string before = Encode(store);
                var proposal = Candidate(store); var plan = store.PrepareQuestBindings(new[] { proposal });
                Check.Equal(before, Encode(store));
                proposal.PersonName = "Mutated input";
                var view = plan.Bindings; view[0].PersonName = "Mutated preview";
                Check.Equal("Anna Ashfield", plan.Bindings[0].PersonName);
                Check.Equal(before, Encode(store));
                var other = Candidate(store); other.PersonName = "Different unpublished name";
                Check.Equal(other.PersonName, store.PrepareQuestBindings(new[] { other }).Bindings[0].PersonName);
            }));
            cases.Add(("Accepted bindings survive reload and preserve actor death references", () => {
                var store = Filled(); var actor = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                var proposal = Candidate(store); var plan = store.PrepareQuestBindings(new[] { proposal });
                proposal.PersonName = "Changed after preparation";
                store.CommitQuestBindings(plan); actor.IsAlive = false;
                var restored = new WorldStateStore(0);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(Encode(store), Json));
                Check.Equal(Encode(store), Encode(restored));
                Check.Equal("Anna Ashfield", restored.Snapshot().QuestBindings[0].PersonName);
                Check.Equal("test/permanent-resident/v1", restored.Snapshot().QuestBindings[0].RuleId);
                Check.True(!restored.Snapshot().Settlements[0].Households[0].Residents[0].IsAlive);
                var copy = restored.Snapshot(); copy.QuestBindings[0].HomeName = "Altered";
                Check.Equal("The Ashfield Residence", restored.Snapshot().QuestBindings[0].HomeName);
            }));
            cases.Add(("Death between offer and acceptance rejects entire batch", () => {
                var store = Filled(); var a = Candidate(store); var b = Candidate(store, 1002, 20);
                var plan = store.PrepareQuestBindings(new[] { a, b });
                store.GetOrCreate(100, new[] { 10 }).Households[1].Residents[0].IsAlive = false;
                string before = Encode(store);
                Check.Throws<ArgumentException>(() => store.CommitQuestBindings(plan));
                Check.Equal(before, Encode(store));
                Check.Equal(0, store.Snapshot().QuestBindings.Count);
            }));
            cases.Add(("Concurrent conflicting offers cannot partially commit", () => {
                var store = Filled(); var winner = Candidate(store);
                var rival = Candidate(store, 1002); rival.PersonName = "Another Ashfield";
                var plan = store.PrepareQuestBindings(new[] { Candidate(store, 1003, 20), rival });
                store.CommitQuestBindings(store.PrepareQuestBindings(new[] { winner }));
                string before = Encode(store);
                Check.Throws<ArgumentException>(() => store.CommitQuestBindings(plan));
                Check.Equal(before, Encode(store));
            }));
            cases.Add(("Compatible concurrent quests share the resident without duplicating contracts", () => {
                var store = Filled(); var plan = store.PrepareQuestBindings(new[] { Candidate(store) });
                store.CommitQuestBindings(plan); store.CommitQuestBindings(plan);
                store.CommitQuestBindings(store.PrepareQuestBindings(new[] { Candidate(store, 1002) }));
                Check.Equal(2, store.Snapshot().QuestBindings.Count);
            }));
            cases.Add(("Published quest cannot rename or retarget its recipient", () => {
                foreach (Action<QuestResidentBinding> alter in new Action<QuestResidentBinding>[] {
                    b => b.PersonName = "Different", b => b.HomeName = "Different",
                    b => b.QuestName = "_OTHER_", b => b.RuleId = "other-rule" })
                {
                    var store = Filled(); store.CommitQuestBindings(store.PrepareQuestBindings(new[] { Candidate(store) }));
                    var changed = Candidate(store); alter(changed); string before = Encode(store);
                    Check.Throws<ArgumentException>(() => store.PrepareQuestBindings(new[] { changed }));
                    Check.Throws<ArgumentException>(() => store.PrepareQuestBindings(new[] { Candidate(store, 1001, 20) }));
                    Check.Equal(before, Encode(store));
                }
            }));
            cases.Add(("One household cannot receive conflicting published residence labels", () => {
                var store = Filled(); var a = Candidate(store); var b = Candidate(store, 1002);
                b.HomeName = "The Different Residence";
                Check.Throws<ArgumentException>(() => store.PrepareQuestBindings(new[] { a, b }));
                Check.Equal(0, store.Snapshot().QuestBindings.Count);
            }));
            cases.Add(("One quest UID cannot refer to different source quests across symbols", () => {
                var store = Filled(); var a = Candidate(store); var b = Candidate(store, 1001, 20);
                b.PersonSymbol = "_other_"; b.QuestName = "_OTHER_";
                Check.Throws<ArgumentException>(() => store.PrepareQuestBindings(new[] { a, b }));
            }));
            cases.Add(("Offer from another store or before restore cannot commit", () => {
                var store = Filled(); var plan = store.PrepareQuestBindings(new[] { Candidate(store) });
                var other = Filled();
                Check.Throws<ArgumentException>(() => other.CommitQuestBindings(plan));
                store.Restore(store.Snapshot());
                Check.Throws<ArgumentException>(() => store.CommitQuestBindings(plan));
                Check.Equal(0, store.Snapshot().QuestBindings.Count);
            }));
            cases.Add(("Duplicate proposal symbols fail without mutation", () => {
                var store = Filled(); var a = Candidate(store);
                Check.Throws<ArgumentException>(() => store.PrepareQuestBindings(new[] { a, a }));
                Check.Equal(0, store.Snapshot().QuestBindings.Count);
            }));
            cases.Add(("Legacy schema three migrates family deaths and routes with empty bindings", () => {
                var store = Filled(); var old = store.Snapshot(); old.Version = 3; old.QuestBindings = null;
                var actor = old.Settlements[0].Households[0].Residents[0]; actor.IsAlive = false;
                old.Routines.Add(new TownRoutineState { SettlementId = 100, Residents = new List<ResidentRoutine> {
                    new ResidentRoutine { ResidentId = actor.Id } } });
                store.Restore(old); var saved = store.Snapshot();
                Check.Equal(4, saved.Version); Check.Equal(0, saved.QuestBindings.Count);
                Check.Equal(actor.Id, saved.Routines[0].Residents[0].ResidentId);
                Check.True(!saved.Settlements[0].Households[0].Residents[0].IsAlive);
                Check.Equal(old.Settlements[0].Households[0].SurnameSeed, saved.Settlements[0].Households[0].SurnameSeed);
            }));
            var corruptions = new Dictionary<string, Action<QuestResidentBinding>> {
                { "quest UID beyond native serializer range", b => b.QuestUid = ulong.MaxValue },
                { "missing quest name", b => b.QuestName = " " }, { "missing symbol", b => b.PersonSymbol = null },
                { "missing provenance", b => b.RuleId = "" }, { "missing person name", b => b.PersonName = null },
                { "missing home name", b => b.HomeName = " " }, { "unknown resident", b => b.ResidentId = "missing" },
                { "wrong home", b => b.HomeKey = 20 }, { "wrong settlement", b => b.SettlementId = 999 }
            };
            foreach (var item in corruptions)
                cases.Add(("Binding rejects " + item.Key + " on offer and restore transactionally", () => {
                    var store = Filled(); var bad = Candidate(store); item.Value(bad); string before = Encode(store);
                    Check.Throws<ArgumentException>(() => store.PrepareQuestBindings(new[] { bad }));
                    var incoming = store.Snapshot(); incoming.QuestBindings.Add(bad);
                    Check.Throws<ArgumentException>(() => store.Restore(incoming));
                    Check.Equal(before, Encode(store));
                }));
            cases.Add(("Restore rejects duplicate or missing binding ledger transactionally", () => {
                var store = Filled(); store.CommitQuestBindings(store.PrepareQuestBindings(new[] { Candidate(store) }));
                string before = Encode(store); var bad = store.Snapshot(); bad.QuestBindings.Add(bad.QuestBindings[0]);
                Check.Throws<ArgumentException>(() => store.Restore(bad));
                bad.QuestBindings = null;
                Check.Throws<ArgumentException>(() => store.Restore(bad));
                Check.Equal(before, Encode(store));
            }));
        }
    }
}
