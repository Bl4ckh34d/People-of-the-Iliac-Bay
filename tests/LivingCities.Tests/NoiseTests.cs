using System;
using System.Collections.Generic;
using System.Linq;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class NoiseTests
    {
        private static WorldStateStore World(out string victim, out string observer)
        {
            var store = new WorldStateStore(1);
            var town = store.GetOrCreate(1, new[] { 1, 2, 3 });
            var people = town.Households.SelectMany(h => h.Residents).ToArray();
            victim = people[0].Id; observer = people[1].Id;
            var alarm = store.RecordViolence(1, victim, 100);
            store.LocateViolence(alarm.Id, 1, 0, 0, 0);
            return store;
        }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Violence wakes more readily than theft or quiet steps; Stealth and distance matter", () => {
                Check.True(NoiseAwareness.WakeChance(NoiseLevel.Violence, 30, 2) > NoiseAwareness.WakeChance(NoiseLevel.Theft, 30, 2));
                Check.True(NoiseAwareness.WakeChance(NoiseLevel.Theft, 30, 2) > NoiseAwareness.WakeChance(NoiseLevel.QuietSteps, 30, 2));
                Check.True(NoiseAwareness.WakeChance(NoiseLevel.Violence, 100, 2) < NoiseAwareness.WakeChance(NoiseLevel.Violence, 0, 2));
                Check.True(NoiseAwareness.WakeChance(NoiseLevel.Violence, 0, 20) < NoiseAwareness.WakeChance(NoiseLevel.Violence, 0, 2));
                Check.Equal(0, NoiseAwareness.WakeChance(NoiseLevel.QuietSteps, 100, 20));
            }));
            cases.Add(("Awake indoor hearer flees without identifying attacker or gaining relay rights", () => {
                var store = World(out var victim, out var observer);
                var person = store.HearViolence("murder:" + victim, observer, 1, 100, false, 100, 8, 99);
                Check.True(person != null && person.HeardViolence && !person.DirectWitness && !person.Awakened);
                Check.Equal(CivilianReaction.Flee, person.Reaction);
                var another = store.Snapshot().Settlements[0].Households.SelectMany(h=>h.Residents).First(p=>p.Id != observer && p.Id != victim).Id;
                Check.True(!store.Warn("murder:" + victim, observer, another, 101));
            }));
            cases.Add(("Failed sleeping wake roll persists and cannot reroll on reload", () => {
                var store = World(out var victim, out var observer);
                Check.True(store.HearViolence("murder:" + victim, observer, 1, 100, true, 100, 8, 99) == null);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.HearViolence("murder:" + victim, observer, 1, 101, true, 0, 0, 0) == null);
                Check.Equal(1, restored.Snapshot().AlarmIncidents[0].NoiseChecks.Count);
                Check.True(restored.ActiveAlarm(observer, 101) == null);
            }));
            cases.Add(("Successful sleeper waking survives reload and promotes only on sight", () => {
                var store = World(out var victim, out var observer);
                var person = store.HearViolence("murder:" + victim, observer, 1, 100, true, 50, 2, 0);
                Check.True(person.Awakened && person.HeardViolence && !person.DirectWitness);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                var saved = restored.ActiveAlarm(observer, 101).People.Single();
                Check.True(saved.Awakened && !saved.DirectWitness);
                restored.Witness("murder:" + victim, observer, 101);
                Check.True(saved.DirectWitness && saved.HeardViolence && saved.Awakened);
            }));
            cases.Add(("Noise observation cannot cross building boundary or include victim", () => {
                var store = World(out var victim, out var observer);
                Check.True(store.HearViolence("murder:" + victim, observer, 2, 100, false, 0, 0, 0) == null);
                Check.True(store.HearViolence("murder:" + victim, victim, 1, 100, false, 0, 0, 0) == null);
                Check.Equal(0, store.Snapshot().AlarmIncidents[0].NoiseChecks.Count);
            }));
            cases.Add(("Noise validation rejects corrupt decisions and keeps existing state", () => {
                var store = World(out var victim, out var observer);
                store.HearViolence("murder:" + victim, observer, 1, 100, true, 100, 8, 99);
                var broken = store.Snapshot(); broken.AlarmIncidents[0].NoiseChecks[0].Woke = true;
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
                Check.True(!store.Snapshot().AlarmIncidents[0].NoiseChecks[0].Woke);
                broken = store.Snapshot(); broken.AlarmIncidents[0].NoiseChecks.Add(broken.AlarmIncidents[0].NoiseChecks[0]);
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
            }));
            cases.Add(("Legacy incidents load with no invented hearing or wake checks", () => {
                var store = World(out var victim, out var observer);
                store.Witness("murder:" + victim, observer, 100);
                var old = store.Snapshot(); old.AlarmIncidents[0].NoiseChecks = null;
                store.Restore(old);
                Check.Equal(0, store.Snapshot().AlarmIncidents[0].NoiseChecks.Count);
                Check.True(!store.ActiveAlarm(observer, 101).People[0].HeardViolence);
            }));
            cases.Add(("Invalid noise inputs reject without state mutation", () => {
                var store = World(out var victim, out var observer);
                Check.Throws<ArgumentException>(()=>NoiseAwareness.WakeChance((NoiseLevel)999, 50, 1));
                Check.Throws<ArgumentException>(()=>NoiseAwareness.WakeChance(NoiseLevel.Violence, -1, 1));
                Check.Throws<ArgumentException>(()=>store.HearViolence("murder:"+victim, observer, 1, 100, true, 0, double.NaN, 0));
                Check.Throws<ArgumentException>(()=>store.HearViolence("murder:"+victim, observer, 1, 100, true, 0, 0, 100));
                Check.Equal(0, store.Snapshot().AlarmIncidents[0].NoiseChecks.Count);
            }));
            cases.Add(("Late arrivals cannot hear old attacks and expired incidents do not wake sleepers", () => {
                var store = World(out var victim, out var observer);
                Check.True(store.HearViolence("murder:"+victim, observer, 1, 106, false, 0, 0, 0) == null);
                Check.True(store.HearViolence("murder:"+victim, observer, 1, 1000, true, 0, 0, 0) == null);
                Check.Equal(0, store.Snapshot().AlarmIncidents[0].NoiseChecks.Count);
            }));
            cases.Add(("Saved noise checks are independent and hearing cannot be invented without evidence", () => {
                var store = World(out var victim, out var observer);
                store.HearViolence("murder:"+victim, observer, 1, 100, true, 50, 2, 0);
                var copy = store.Snapshot(); copy.AlarmIncidents[0].NoiseChecks[0].Stealth = 100;
                Check.Equal(50, store.Snapshot().AlarmIncidents[0].NoiseChecks[0].Stealth);
                copy = store.Snapshot(); copy.AlarmIncidents[0].NoiseChecks.Clear();
                Check.Throws<ArgumentException>(()=>store.Restore(copy));
            }));
        }
    }
}
