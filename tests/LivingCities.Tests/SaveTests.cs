using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class SaveTests
    {
        private static WorldStateStore Filled()
        {
            var s = new WorldStateStore(42);
            s.GetOrCreate(100, new[] { 10, 20 });
            return s;
        }
        private static string Signature(WorldState s) => JsonSerializer.Serialize(s, new JsonSerializerOptions { IncludeFields = true });
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Version one saves migrate without regenerating residents", () => {
                var s = Filled(); var old = s.Snapshot(); old.Version = 1; old.Routines = null;
                string identity = old.Settlements[0].Households[0].Residents[0].Id;
                s.Restore(old);
                Check.Equal(5, s.Snapshot().Version);
                Check.Equal(identity, s.Snapshot().Settlements[0].Households[0].Residents[0].Id);
                Check.Equal(0, s.Snapshot().Routines.Count);
            }));
            cases.Add(("Saved routes survive reload without live scene geometry", () => {
                var s = Filled(); var resident = s.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                var routes = Routine(resident.Id); s.SetRoutines(routes);
                routes.Residents[0].Route[1].X = 999;
                var restored = new WorldStateStore(0); restored.Restore(JsonSerializer.Deserialize<WorldState>(Signature(s.Snapshot()), new JsonSerializerOptions { IncludeFields = true }));
                Check.Equal(5d, restored.GetRoutines(100).Residents[0].Route[1].X);
                var copy = restored.GetRoutines(100); copy.Residents.Clear();
                Check.Equal(1, restored.GetRoutines(100).Residents.Count);
                // Route commits must not detach the actor's live death record.
                resident.IsAlive = false;
                Check.True(!s.Snapshot().Settlements[0].Households[0].Residents[0].IsAlive);
            }));
            cases.Add(("Invalid saved routes fail transactionally", () => {
                foreach (int corrupt in new[] { 0, 1, 2, 3, 4, 5, 6 })
                {
                    var s = Filled(); var r = Routine(s.Snapshot().Settlements[0].Households[0].Residents[0].Id);
                    if (corrupt == 0) r.Residents[0].ResidentId = "missing";
                    if (corrupt == 1) r.Residents[0].Route[0].X = double.NaN;
                    if (corrupt == 2) r.Residents[0].CloseMinute = 0;
                    if (corrupt == 3) r.Residents.Add(r.Residents[0]);
                    if (corrupt == 4) r.Residents[0].Route = null;
                    if (corrupt == 5) r.SettlementId = 999;
                    if (corrupt == 6) r.Residents[0].DestinationKey = 10;
                    string before = Signature(s.Snapshot());
                    Check.Throws<ArgumentException>(() => s.SetRoutines(r));
                    Check.Equal(before, Signature(s.Snapshot()));
                }
            }));
            cases.Add(("Save serialization preserves identities and deaths", () => {
                var s = Filled(); var population = s.GetOrCreate(100, new[] { 99 });
                Population.MarkDead(population, population.Households[0].Residents[0].Id);
                string json = Signature(s.Snapshot());
                var restored = new WorldStateStore(999);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(json, new JsonSerializerOptions { IncludeFields = true }));
                Check.Equal(json, Signature(restored.Snapshot()));
                Check.True(!restored.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0].IsAlive);
            }));
            cases.Add(("Revisiting does not regenerate or resurrect residents", () => {
                var s = Filled(); var p = s.GetOrCreate(100, new[] { 999 });
                Population.MarkDead(p, p.Households[0].Residents[0].Id);
                Check.Equal(2, s.GetOrCreate(100, new[] { 999 }).Households.Count);
                Check.True(!s.GetOrCreate(100, new[] { 999 }).Households[0].Residents[0].IsAlive);
            }));
            cases.Add(("Snapshot mutations cannot alter live state", () => {
                var s = Filled(); string before = Signature(s.Snapshot()); var snap = s.Snapshot();
                snap.Settlements[0].Households[0].Residents[0].IsAlive = false;
                snap.Settlements.Clear();
                Check.Equal(before, Signature(s.Snapshot()));
            }));
            cases.Add(("Restore does not retain input references", () => {
                var s = Filled(); var snap = s.Snapshot(); var restored = new WorldStateStore(0);
                restored.Restore(snap); string before = Signature(restored.Snapshot());
                snap.Settlements[0].Households[0].Residents[0].Id = "changed";
                Check.Equal(before, Signature(restored.Snapshot()));
            }));
            Reject(cases, "future schema", s => s.Version = 99);
            Reject(cases, "null settlements", s => s.Settlements = null);
            Reject(cases, "null settlement", s => s.Settlements.Add(null));
            Reject(cases, "duplicate settlement", s => s.Settlements.Add(s.Settlements[0]));
            Reject(cases, "negative settlement", s => s.Settlements[0].SettlementId = -1);
            Reject(cases, "null homes", s => s.Settlements[0].Households = null);
            Reject(cases, "null home", s => s.Settlements[0].Households.Add(null));
            Reject(cases, "duplicate home", s => s.Settlements[0].Households.Add(s.Settlements[0].Households[0]));
            Reject(cases, "negative home", s => s.Settlements[0].Households[0].BuildingKey = -1);
            Reject(cases, "null residents", s => s.Settlements[0].Households[0].Residents = null);
            Reject(cases, "null resident", s => s.Settlements[0].Households[0].Residents[0] = null);
            Reject(cases, "empty identity", s => s.Settlements[0].Households[0].Residents[0].Id = " ");
            Reject(cases, "duplicate identity", s => s.Settlements[0].Households[1].Residents[0].Id = s.Settlements[0].Households[0].Residents[0].Id);
            Reject(cases, "wrong ownership", s => s.Settlements[0].Households[0].Residents[0].HomeKey = 900);
            Reject(cases, "overcrowded home", s => {
                var h = s.Settlements[0].Households[0];
                while (h.Residents.Count < 6) h.Residents.Add(new Resident { Id = "extra" + h.Residents.Count, HomeKey = h.BuildingKey });
            });
            Reject(cases, "dead protected resident", s => {
                var r = s.Settlements[0].Households[0].Residents[0]; r.IsQuestProtected = true; r.IsAlive = false;
            });
            cases.Add(("Null restore is rejected", () => Check.Throws<ArgumentNullException>(() => Filled().Restore(null))));
            cases.Add(("Restored seed governs newly visited towns", () => {
                var a = Filled(); var b = new WorldStateStore(900); b.Restore(a.Snapshot());
                Check.Equal(PopulationTests.Signature(a.GetOrCreate(200, new[] { 30 })), PopulationTests.Signature(b.GetOrCreate(200, new[] { 30 })));
            }));
        }
        private static TownRoutineState Routine(string residentId)
        {
            return new TownRoutineState { SettlementId = 100, Residents = new List<ResidentRoutine> {
                new ResidentRoutine { ResidentId = residentId, DestinationKey = 30, OpenMinute = 600, CloseMinute = 1000,
                    Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 5 } } } } };
        }
        private static void Reject(List<(string, Action)> cases, string name, Action<WorldState> corrupt)
        {
            cases.Add(("Restore rejects " + name + " without changing live state", () => {
                var s = Filled(); var incoming = s.Snapshot(); string before = Signature(s.Snapshot());
                corrupt(incoming);
                Check.Throws<ArgumentException>(() => s.Restore(incoming));
                Check.Equal(before, Signature(s.Snapshot()));
            }));
        }
    }
}
