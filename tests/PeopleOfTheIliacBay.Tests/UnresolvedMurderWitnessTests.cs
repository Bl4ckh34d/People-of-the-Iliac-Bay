using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class UnresolvedMurderWitnessTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Only explicitly attributed player murders create persistent witness fear", () => {
                var store = World();
                var people = People(store);
                var neutral = store.RecordViolence(1, people[0].Id, 100);
                store.Witness(neutral.Id, people[1].Id, 100);
                Check.True(!store.HasPersistentMurderFear(people[1].Id));

                var hostile = store.RecordHostileThreat(1, "attacker", 100);
                store.Witness(hostile.Id, people[2].Id, 100);
                Check.Throws<ArgumentException>(() => store.MarkPlayerMurder(hostile.Id));
                Check.True(!store.HasPersistentMurderFear(people[2].Id));
            }));

            cases.Add(("Direct and warned player-murder witnesses persist until charged crime resolves", () => {
                var store = World();
                var town = store.GetOrCreate(1, new[] { 1, 2, 3, 4, 5 });
                var people = People(store);
                var victim = people[0];
                var direct = people[1];
                var warned = people[2];
                var lateDirect = people[3];
                var unrelated = people[4];
                var incident = store.RecordViolence(1, victim.Id, 100);
                store.LocateViolence(incident.Id, 7, 0, 0, 0);
                var directWitness = store.Witness(incident.Id, direct.Id, 100);
                Check.True(store.Warn(incident.Id, direct.Id, warned.Id, 101));
                Population.MarkDead(town, victim.Id);
                store.ConfirmCorpse(incident.Id, 102);
                store.MarkPlayerMurder(incident.Id);

                directWitness.HasPosition = true;
                directWitness.BuildingKey = 7;
                directWitness.Escape = CivilianEscape.Create(directWitness, 102,
                    new List<GuardPoint3> { new GuardPoint3(), new GuardPoint3() },
                    new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 200 } });

                Check.Equal(CivilianReaction.Flee, directWitness.Reaction);
                Check.Equal(CivilianReaction.Flee, incident.People.Find(p => p.ResidentId == warned.Id).Reaction);
                Check.True(store.HasPersistentMurderFear(direct.Id));
                Check.True(store.HasPersistentMurderFear(warned.Id));
                Check.True(!store.HasPersistentMurderFear(unrelated.Id));
                Check.Equal(CivilianReaction.Flee, store.Witness(incident.Id, lateDirect.Id, 103).Reaction);

                // Player crime can still be None while witness is on the way to report.
                store.ResolveAccusedPlayerMurders();
                Check.True(!incident.AccusationIssued && !incident.CrimeResolved);
                Check.True(store.HasPersistentMurderFear(direct.Id));
                var afterFiniteWindow = CivilianAlarms.ActiveUntil(incident) + 1;
                Check.True(afterFiniteWindow > incident.ExpiresAt);
                Check.True(store.AdvanceEscapes(1, afterFiniteWindow).Exists(a => a.Id == incident.Id));
                Check.Equal(incident.Id, store.ActiveAlarm(direct.Id, afterFiniteWindow).Id);
                Check.True(store.HasPersistentMurderFear(direct.Id));

                store.MarkMurderAccusationIssued(incident.Id);
                var restored = RoundTrip(store.Snapshot());
                Check.True(restored.HasPersistentMurderFear(direct.Id));
                Check.True(restored.HasPersistentMurderFear(warned.Id));
                restored.ResolveAccusedPlayerMurders();
                Check.True(!restored.HasPersistentMurderFear(direct.Id));
                Check.True(!restored.HasPersistentMurderFear(warned.Id));
                Check.True(restored.ActiveAlarm(direct.Id, 103) == null);
                Check.True(restored.Snapshot().AlarmIncidents[0].CrimeResolved);
            }));

            cases.Add(("Schema eighteen ignores absent persistent witness state", () => {
                var store = World();
                var people = People(store);
                var incident = store.RecordViolence(1, people[0].Id, 100);
                store.LocateViolence(incident.Id, -1, 0, 0, 0);
                store.Witness(incident.Id, people[1].Id, 100);
                Population.MarkDead(store.GetOrCreate(1, new[] { 1, 2, 3, 4, 5 }), people[0].Id);
                store.ConfirmCorpse(incident.Id, 101);
                store.MarkPlayerMurder(incident.Id);
                store.MarkMurderAccusationIssued(incident.Id);
                var legacy = store.Snapshot();
                legacy.Version = 18;
                var restored = new WorldStateStore(0);
                restored.Restore(legacy);
                Check.True(!restored.HasPersistentMurderFear(people[1].Id));
                var copied = restored.Snapshot().AlarmIncidents[0];
                Check.True(!copied.PlayerAttributed && !copied.AccusationIssued && !copied.CrimeResolved);
            }));

            cases.Add(("Resolved native murder witnesses clear response before ordinary alarm expiry", () => {
                var store = new WorldStateStore(41);
                var town = store.GetOrCreate(1, new[] { 1, 2 }, 2);
                var keeper = store.RegisterNativePerson(1, 7, 11, "Witness Keeper", ResidentSex.Female, 1, 0, 184, 1);
                var victim = town.Households[0].Residents[0];
                var incident = store.RecordViolence(1, victim.Id, 100);
                store.LocateViolence(incident.Id, 7, 0, 0, 0);
                var witness = store.Witness(incident.Id, keeper.Id, 100);
                Check.True(witness != null);
                Population.MarkDead(town, victim.Id);
                store.ConfirmCorpse(incident.Id, 101);
                store.MarkPlayerMurder(incident.Id);
                store.MarkMurderAccusationIssued(incident.Id);
                store.ResolveAccusedPlayerMurders();
                Check.True(CivilianAlarms.ActiveUntil(incident) > 102);
                Check.True(store.ClearNativeResponse(keeper.Id, 102));
                Check.True(store.NativePeopleFor(1)[0].ResponseIncidentId == null);
            }));
        }

        private static WorldStateStore World()
        {
            var store = new WorldStateStore(41);
            store.GetOrCreate(1, new[] { 1, 2, 3, 4, 5 });
            return store;
        }

        private static Resident[] People(WorldStateStore store)
        { return store.Snapshot().Settlements[0].Households.SelectMany(h => h.Residents).ToArray(); }

        private static WorldStateStore RoundTrip(WorldState state)
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { IncludeFields = true });
            var copy = JsonSerializer.Deserialize<WorldState>(json, new JsonSerializerOptions { IncludeFields = true });
            var restored = new WorldStateStore(0);
            restored.Restore(copy);
            return restored;
        }
    }
}
