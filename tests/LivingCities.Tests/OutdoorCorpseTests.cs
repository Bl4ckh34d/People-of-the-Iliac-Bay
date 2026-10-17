using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class OutdoorCorpseTests
    {
        private static WorldStateStore Body(out CivilianIncident alarm, out string witness)
        {
            var store = new WorldStateStore(13);
            var town = store.GetOrCreate(1, new[] { 7, 8 }, 1);
            var victim = town.Households[0].Residents[0]; witness = town.Households[1].Residents[0].Id;
            alarm = store.RecordViolence(1, victim.Id, 100);
            store.LocateViolence(alarm.Id, -1, 1, 0, 2);
            Population.MarkDead(town, victim.Id); store.ConfirmCorpse(alarm.Id, 100, -1, 3, 0, 4);
            return store;
        }
        public static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Crime scene warning allows ten unpaused seconds and resets only on departure", () => {
                var warning = new CrimeSceneWarning(); Check.True(warning.Begin()); Check.Equal(10, warning.DisplaySeconds);
                warning.Advance(4, true, false); Check.Near(6, warning.Remaining); Check.Equal(6, warning.DisplaySeconds);
                warning.Advance(100, true, true); Check.Near(6, warning.Remaining); Check.Equal(6, warning.DisplaySeconds);
                warning.Advance(5.1, true, false); Check.Equal(1, warning.DisplaySeconds);
                var saved = CrimeSceneWarning.Copy(warning);
                saved.Advance(0.8, true, false); Check.Equal(1, saved.DisplaySeconds); Check.True(!saved.Violated);
                saved.Advance(0.11, true, false); Check.Equal(0, saved.DisplaySeconds); Check.True(saved.Violated);
                warning.Advance(1, true, false); Check.Equal(0, warning.DisplaySeconds);
                Check.True(!warning.Begin());
                saved.Advance(0, false, false); Check.True(!saved.Issued && !saved.Violated);
                Check.True(saved.Begin()); Check.Near(10, saved.Remaining);
                Check.Throws<ArgumentException>(() => saved.Advance(double.NaN, true, false));
                saved.Remaining = 11; Check.Throws<ArgumentException>(() => CrimeSceneWarning.Copy(saved));
            }));
            cases.Add(("Outdoor investigators patrol for one hour then return without timer renewal", () => {
                var roster = GuardRoster.Create(1, 9, 1, new[] { new SavedRoutePoint() });
                var guard = roster.Guards[0];
                var target = new SavedRoutePoint { X = 100 };
                var approaches = new Dictionary<string, List<SavedRoutePoint>> {
                    { guard.Id, new List<SavedRoutePoint> { guard.Post, target } } };
                var patrol = new List<SavedRoutePoint> { target, new SavedRoutePoint { X = 120 }, target };
                roster.DispatchOutdoorInvestigation("body", 100, approaches, patrol, 1, 2, 10);
                Check.Near(600, guard.ArrivesAt); Check.Near(4200, guard.SearchUntil);
                var position = roster.Position(guard.Id, 650);
                Check.Near(110, position.X);
                var copy = GuardRoster.CopyValidated(roster);
                copy.DispatchOutdoorInvestigation("body", 900, approaches, patrol, 1, 2, 10);
                Check.Near(4200, copy.Guards[0].SearchUntil);
                Check.True(copy.Reports[0].HasArrival); Check.Near(600, copy.Reports[0].FirstArrivalAt);
                Check.Near(110, copy.Position(guard.Id, 650).X);
                var corrupted = GuardRoster.CopyValidated(copy);
                corrupted.Guards[0].Route[corrupted.Guards[0].Route.Count - 1].X += 10;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(corrupted));
                copy.Advance(4200); Check.Equal(GuardDuty.Returning, copy.Guards[0].Duty);
                copy.Advance(4700); Check.Equal(GuardDuty.Posted, copy.Guards[0].Duty);
                Check.True(GuardRoster.CopyValidated(copy).Reports[0].HasArrival);
            }));
            cases.Add(("Outdoor investigation rejects disconnected patrols without assigning guards", () => {
                var roster = GuardRoster.Create(1, 9, 1, new[] { new SavedRoutePoint() }); var guard = roster.Guards[0];
                var routes = new Dictionary<string, List<SavedRoutePoint>> {
                    { guard.Id, new List<SavedRoutePoint> { guard.Post, new SavedRoutePoint { X = 100 } } } };
                Check.Throws<ArgumentException>(() => roster.DispatchOutdoorInvestigation("body", 100, routes,
                    new List<SavedRoutePoint> { new SavedRoutePoint { X = 10 }, new SavedRoutePoint { X = 20 }, new SavedRoutePoint { X = 10 } }, 1, 2, 10));
                Check.Equal(0, roster.Reports.Count); Check.Equal(GuardDuty.Posted, guard.Duty);
            }));
            cases.Add(("Unseen outdoor corpse survives elapsed days and player departure", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                store.ObserveOutdoorCorpse(alarm.Id, false, 900000);
                Check.Equal(1, store.ObserveCorpses(1, -1, 900000).Count);
                Check.True(!alarm.Corpse.Cleared && alarm.Corpse.Discovery == null);
            }));
            cases.Add(("Seen outdoor corpse waits for player departure then stays cleared after reload", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                store.NoticeOutdoorCorpse(alarm.Id, witness, false, 150);
                store.NoticeOutdoorCorpse(alarm.Id, witness, false, 151, true);
                Check.True(!alarm.Corpse.Discovery.PlayerPresent, "Later arrival must not retroactively accuse a passerby.");
                store.ObserveOutdoorCorpse(alarm.Id, true, 900000);
                Check.True(!alarm.Corpse.Cleared);
                store.ObserveOutdoorCorpse(alarm.Id, false, 900001);
                var copy = new WorldStateStore(0); copy.Restore(store.Snapshot());
                copy.ObserveOutdoorCorpse(alarm.Id, true, 900100);
                var body = copy.Snapshot().AlarmIncidents[0].Corpse;
                Check.True(body.Cleared); Check.Near(900001, body.ClearedAt);
                Check.Near(150, body.Discovery.SeenAt); Check.Equal(witness, body.Discovery.ObserverId);
            }));
            cases.Add(("Corpse discovery requires a living known observer and preserves first sighting", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                Check.Throws<ArgumentException>(() => store.NoticeOutdoorCorpse(alarm.Id, "unknown", false, 150));
                Check.Throws<ArgumentException>(() => store.NoticeOutdoorCorpse(alarm.Id, alarm.VictimId, false, 150));
                Check.Throws<ArgumentException>(() => store.NoticeOutdoorCorpse(alarm.Id, witness, false, 99));
                Check.True(alarm.Corpse.Discovery == null);
                var roster = store.GetOrCreateGuards(1, 1, new[] { new SavedRoutePoint() });
                store.NoticeOutdoorCorpse(alarm.Id, roster.Guards[0].Id, true, 160);
                store.NoticeOutdoorCorpse(alarm.Id, witness, false, 170);
                Check.Near(160, alarm.Corpse.Discovery.SeenAt); Check.True(alarm.Corpse.Discovery.Guard);
                store.ObserveOutdoorCorpse(alarm.Id, false, 159);
                Check.True(!alarm.Corpse.Cleared);
                roster.Kill(roster.Guards[0].Id, 180);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Near(160, restored.Snapshot().AlarmIncidents[0].Corpse.Discovery.SeenAt);
                var malformed = restored.Snapshot(); malformed.Garrisons[0].Guards[0] = null;
                Check.Throws<ArgumentException>(() => restored.Restore(malformed));
            }));
            cases.Add(("Outdoor corpse restore rejects invented observer and early cleanup transactionally", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                store.NoticeOutdoorCorpse(alarm.Id, witness, false, 150);
                store.ObserveOutdoorCorpse(alarm.Id, false, 160);
                var broken = store.Snapshot(); broken.AlarmIncidents[0].Corpse.Discovery.ObserverId = "invented";
                Check.Throws<ArgumentException>(() => store.Restore(broken));
                broken = store.Snapshot(); broken.AlarmIncidents[0].Corpse.ClearedAt = 149;
                Check.Throws<ArgumentException>(() => store.Restore(broken));
                Check.Near(160, store.Snapshot().AlarmIncidents[0].Corpse.ClearedAt);
            }));
            cases.Add(("Current outdoor bodies reject unseen cleanup while old cleared saves remain cleared", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                var corrupted = store.Snapshot();
                corrupted.AlarmIncidents[0].Corpse.Cleared = true;
                corrupted.AlarmIncidents[0].Corpse.ClearedAt = 90000;
                Check.Throws<ArgumentException>(() => store.Restore(corrupted));
                corrupted.AlarmIncidents[0].Corpse.CleanupVersion = 0;
                store.Restore(corrupted);
                Check.True(store.Snapshot().AlarmIncidents[0].Corpse.Cleared);
            }));
            cases.Add(("Outdoor discovery cannot clear an indoor corpse", () => {
                CivilianIncident alarm; string witness; var store = Body(out alarm, out witness);
                alarm.Corpse.BuildingKey = 7;
                Check.Throws<ArgumentException>(() => store.NoticeOutdoorCorpse(alarm.Id, witness, false, 150));
                Check.Throws<ArgumentException>(() => store.ObserveOutdoorCorpse(alarm.Id, false, 160));
                Check.True(!alarm.Corpse.Cleared);
            }));
        }
    }
}
