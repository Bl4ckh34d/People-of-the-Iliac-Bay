using System;
using System.Collections.Generic;
using System.Linq;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class HearingReportTests
    {
        private static WorldStateStore Scene(out CivilianIncident alarm, out AlarmPerson hearer)
        {
            var store = new WorldStateStore(1);
            var town = store.GetOrCreate(1, new[] { 7, 8 }, 1);
            var victim = town.Households[0].Residents[0];
            alarm = store.RecordViolence(1, victim.Id, 100);
            store.LocateViolence(alarm.Id, 7, 0, 0, 0);
            hearer = store.HearViolence(alarm.Id, town.Households[1].Residents[0].Id, 7, 100, false, 0, 2, 0);
            hearer.HasPosition = true; hearer.BuildingKey = 7;
            hearer.Escape = CivilianEscape.Create(hearer, 100, new List<GuardPoint3> {
                new GuardPoint3(), new GuardPoint3 { X = 2 } }, new List<SavedRoutePoint> {
                new SavedRoutePoint { X = 2 }, new SavedRoutePoint() });
            Population.MarkDead(town, victim.Id); store.ConfirmCorpse(alarm.Id, 100);
            store.RegisterUnwitnessedScene(alarm.Id);
            var roster = store.GetOrCreateGuards(1, 1, new[] { new SavedRoutePoint() });
            roster.CacheBuildingApproach(7, new SavedRoutePoint { X = 100 }, new Dictionary<string,List<SavedRoutePoint>> {
                { roster.Guards[0].Id, new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 100 } } } });
            return store;
        }
        internal static void Add(List<(string,Action)> cases)
        {
            cases.Add(("Hearer must physically reach living posted guard before neutral report", () => {
                var store = Scene(out var alarm, out var person);
                Check.Equal(0, store.ObserveCrimeScenes(1, 7, 101).Count);
                Check.True(!store.ReportHeardViolence(alarm.Id, person.ResidentId, 101));
                Check.Equal(1, store.AdvanceEscapes(1, person.Escape.ReportAt).Count);
                Check.True(store.ReportHeardViolence(alarm.Id, person.ResidentId, person.Escape.ReportAt));
                Check.True(alarm.Scene.Reported && !alarm.Scene.PlayerLeft && !person.DirectWitness);
                Check.True(!store.ReportHeardViolence(alarm.Id, person.ResidentId, person.Escape.ReportAt + 1));
            }));
            cases.Add(("Reported indoor noise dispatches investigation before departure and cleans only on arrival", () => {
                var store = Scene(out var alarm, out var person); double time = person.Escape.ReportAt;
                store.AdvanceEscapes(1, time); store.ReportHeardViolence(alarm.Id, person.ResidentId, time);
                Check.Equal(1, store.ObserveCrimeScenes(1, 7, time).Count);
                var roster = store.GetGuards(1);
                var sent = roster.DispatchBuilding("discovery:"+alarm.Id, 7, time, 1, 2, 12, true);
                Check.Throws<ArgumentException>(()=>store.DiscoverCrimeScene(alarm.Id, time));
                Check.Equal(1, store.ObserveCorpses(1, 7, time).Count);
                double arrival = sent[0].ArrivesAt;
                store.DiscoverCrimeScene(alarm.Id, arrival);
                Check.Equal(0, store.ObserveCorpses(1, 7, arrival).Count);
                Check.True(alarm.Scene.Occupied(arrival + 3599));
                Check.True(!alarm.Scene.Occupied(arrival + 3600));
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.Snapshot().AlarmIncidents[0].Scene.Reported);
            }));
            cases.Add(("Dead or absent guard cannot receive a hearer's report", () => {
                var store = Scene(out var alarm, out var person); var roster = store.GetGuards(1);
                store.AdvanceEscapes(1, person.Escape.ReportAt);
                roster.Kill(roster.Guards[0].Id, 101);
                Check.True(!store.ReportHeardViolence(alarm.Id, person.ResidentId, person.Escape.ReportAt));
                Check.True(!alarm.Scene.Reported);
            }));
            cases.Add(("Corrupt saved hearing report contact rejects transactionally", () => {
                var store = Scene(out var alarm, out var person);
                store.AdvanceEscapes(1, person.Escape.ReportAt);
                store.ReportHeardViolence(alarm.Id, person.ResidentId, person.Escape.ReportAt);
                var broken = store.Snapshot(); broken.AlarmIncidents[0].Scene.ReportGuardId = "invented";
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
                Check.True(store.Snapshot().AlarmIncidents[0].Scene.ReportGuardId != "invented");
            }));
            cases.Add(("Witnessed violence cannot be disguised as a neutral hearing report", () => {
                var store = Scene(out var alarm, out var person);
                store.Witness(alarm.Id, person.ResidentId, 101);
                store.AdvanceEscapes(1, person.Escape.ReportAt);
                Check.Throws<ArgumentException>(()=>store.ReportHeardViolence(alarm.Id, person.ResidentId, person.Escape.ReportAt));
                Check.True(!alarm.Scene.Reported);
            }));
            cases.Add(("Hearing permits investigation of a visiting victim without inventing home ownership", () => {
                var store = new WorldStateStore(1); var town = store.GetOrCreate(1, new[] { 7, 8 }, 1);
                var victim = town.Households[1].Residents[0];
                var alarm = store.RecordViolence(1, victim.Id, 100);
                store.LocateViolence(alarm.Id, 7, 0, 0, 0);
                store.HearViolence(alarm.Id, town.Households[0].Residents[0].Id, 7, 100, false, 0, 2, 0);
                Population.MarkDead(town, victim.Id); store.RegisterUnwitnessedScene(alarm.Id);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.Snapshot().AlarmIncidents[0].Scene != null);
                Check.Equal(8, restored.Snapshot().Settlements[0].Households[1].Residents[0].HomeKey);
            }));
        }
    }
}
