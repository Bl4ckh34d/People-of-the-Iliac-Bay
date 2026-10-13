using System;
using System.Collections.Generic;
using System.Linq;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class AlarmTests
    {
        private static WorldStateStore World()
        {
            var store = new WorldStateStore(1);
            store.GetOrCreate(1, new[] { 1, 2, 3, 4, 5 });
            return store;
        }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Direct witness warns only two distinct people; secondhand cannot relay", () => {
                var store = World(); var people = store.Snapshot().Settlements[0].Households.SelectMany(h => h.Residents).ToArray();
                var alarm = store.RecordViolence(1, people[0].Id, 100);
                store.Witness(alarm.Id, people[1].Id, 100);
                Check.True(store.Warn(alarm.Id, people[1].Id, people[2].Id, 101));
                Check.True(!store.Warn(alarm.Id, people[1].Id, people[2].Id, 102));
                Check.True(!store.Warn(alarm.Id, people[2].Id, people[3].Id, 102));
                Check.True(store.Warn(alarm.Id, people[1].Id, people[3].Id, 103));
                Check.True(!store.Warn(alarm.Id, people[1].Id, people[4].Id, 104));
                Check.Equal(3, alarm.People.Count);
            }));
            cases.Add(("Repeated report cannot reset warnings or expiry; save restores spent budget", () => {
                var store = World(); var ids = store.Snapshot().Settlements[0].Households.SelectMany(h => h.Residents).Select(r => r.Id).ToArray();
                var alarm = store.RecordViolence(1, ids[0], 100);
                store.Witness(alarm.Id, ids[1], 100); store.Warn(alarm.Id, ids[1], ids[2], 101);
                store.Warn(alarm.Id, ids[1], ids[3], 102);
                Check.Equal(alarm.Id, store.RecordViolence(1, ids[0], 500).Id);
                var restored = new WorldStateStore(2); restored.Restore(store.Snapshot());
                Check.True(!restored.Warn(alarm.Id, ids[1], ids[4], 103));
                Check.True(restored.ActiveAlarm(ids[1], alarm.ExpiresAt) == null);
                Check.True(!restored.Warn(alarm.Id, ids[1], ids[4], alarm.ExpiresAt));
            }));
            cases.Add(("Children always flee; adult response is stable and includes fighters", () => {
                var child = new Resident { Id = "child", AgeGroup = ResidentAgeGroup.Child };
                for (uint i = 0; i < 100; i++) { child.AppearanceSeed = i; Check.Equal(CivilianReaction.Flee, CivilianAlarms.ChooseReaction(child)); }
                var reactions = Enumerable.Range(0, 100).Select(i => CivilianAlarms.ChooseReaction(new Resident { AppearanceSeed = (uint)i })).Distinct().ToArray();
                Check.True(reactions.Contains(CivilianReaction.Flee) && reactions.Contains(CivilianReaction.Fight));
            }));
            cases.Add(("Alarm restores transactionally and snapshots do not share warning lists", () => {
                var store = World(); var ids = store.Snapshot().Settlements[0].Households.SelectMany(h => h.Residents).Select(r => r.Id).ToArray();
                var alarm = store.RecordViolence(1, ids[0], 100); store.Witness(alarm.Id, ids[1], 100);
                var snapshot = store.Snapshot(); snapshot.AlarmIncidents[0].People[0].Warned.Add("unknown");
                Check.Throws<ArgumentException>(() => store.Restore(snapshot));
                Check.Equal(0, store.Snapshot().AlarmIncidents[0].People[0].Warned.Count);
                Check.Throws<ArgumentException>(() => store.RecordViolence(99, ids[0], 100));
            }));
            cases.Add(("Schema four migrates without invented alarms or trimmed households", () => {
                var store = World(); var old = store.Snapshot(); old.Version = 4; old.AlarmIncidents = null;
                store.Restore(old); Check.Equal(0, store.Snapshot().AlarmIncidents.Count);
                Check.True(store.Snapshot().Settlements[0].Households.All(h => h.BedCapacity == -1));
            }));
            cases.Add(("Nine-bed families round trip and overcrowding rejects without replacement", () => {
                var store = new WorldStateStore(42);
                var town = store.GetOrCreateFamilies(1, Enumerable.Range(0, 10000).Select(k => new HomeProfile(k, 9, 300)));
                Check.True(town.Households.Any(h => h.Residents.Count == 9));
                var saved = store.Snapshot(); var restored = new WorldStateStore(2); restored.Restore(saved);
                var home = saved.Settlements[0].Households.First(h => h.Residents.Count == 9); home.BedCapacity = 1;
                Check.Throws<ArgumentException>(() => restored.Restore(saved));
                Check.Equal(9, restored.Snapshot().Settlements[0].Households.First(h => h.BuildingKey == home.BuildingKey).BedCapacity);
            }));
        }
    }
}
