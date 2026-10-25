using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class EntryReportTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Guard post relocation preserves entry history and cancels an undelivered old route",()=>{
                foreach(bool delivered in new[] { false,true })
                {
                    var store=new WorldStateStore(42); var town=store.GetOrCreate(1,new[] { 4,8 },1);
                    string witness=town.Households[1].Residents[0].Id;
                    var roster=store.GetOrCreateGuards(1,1,new[] { new SavedRoutePoint { X=28 } });
                    var report=store.RecordEntry(1,4,100,new[] { witness },new string[0]);
                    store.PlanEntryMessenger(report.Id,witness,roster.Guards[0].Id,100,
                        new List<SavedRoutePoint> { new SavedRoutePoint(),new SavedRoutePoint { X=28 } },12);
                    if(delivered) store.AdvanceEntryMessengers(1,220);
                    roster.ReassignPost(roster.Guards[0].Id,new SavedRoutePoint { X=60 },221);
                    var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                    Check.Equal(0,restored.AdvanceEntryMessengers(1,222).Count);
                    var saved=restored.Snapshot().EntryReports[0];
                    Check.Equal(delivered,saved.Messenger.Delivered);
                    Check.Equal(!delivered,saved.Messenger.Cancelled);
                    Check.Equal(delivered ? 1 : 0,saved.InformedGuards.Count);
                }
            }));
            cases.Add(("Entry messenger travels a saved route before reporting and cannot deliver twice", () => {
                var store = new WorldStateStore(42); var town = store.GetOrCreate(1, new[] { 4, 8 }, 1);
                string witness = town.Households[1].Residents[0].Id;
                var roster = store.GetOrCreateGuards(1, 1, new List<SavedRoutePoint> { new SavedRoutePoint { X = 28 } });
                var report = store.RecordEntry(1, 4, 100, new[] { witness }, new string[0]);
                var route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 28 } };
                store.PlanEntryMessenger(report.Id, witness, roster.Guards[0].Id, 100, route,12);
                route[1].X = 999; // Caller mutation must not alter saved travel.
                Check.Equal(220.0, report.Messenger.ArrivesAt);
                Check.Equal(14.0, report.Messenger.Position(160).X);
                Check.Equal(0, store.AdvanceEntryMessengers(1, 219.9).Count);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Equal(1, restored.AdvanceEntryMessengers(1, 220).Count);
                Check.Equal(1, restored.Snapshot().EntryReports[0].InformedGuards.Count);
                Check.True(restored.Snapshot().EntryReports[0].Messenger.Delivered);
                Check.Equal(0, restored.AdvanceEntryMessengers(1, 400).Count);
                var malformed = restored.Snapshot(); malformed.EntryReports[0].Messenger.Route[1].X = double.NaN;
                Check.Throws<ArgumentException>(() => restored.Restore(malformed));
                Check.True(restored.Snapshot().EntryReports[0].Messenger.Delivered);
            }));
            cases.Add(("Dead messengers and departed guards cannot receive a phantom entry report", () => {
                foreach (int interruption in new[] { 0, 1, 2 })
                {
                    var store = new WorldStateStore(42); var town = store.GetOrCreate(1, new[] { 4, 8 }, 1);
                    var witness = town.Households[1].Residents[0];
                    var roster = store.GetOrCreateGuards(1, 1, new List<SavedRoutePoint> { new SavedRoutePoint { X = 28 } });
                    var report = store.RecordEntry(1, 4, 100, new[] { witness.Id }, new string[0]);
                    Check.Throws<ArgumentException>(() => store.PlanEntryMessenger(report.Id, witness.Id, roster.Guards[0].Id, 100,
                        new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 50 } }));
                    Check.True(report.Messenger == null);
                    store.PlanEntryMessenger(report.Id, witness.Id, roster.Guards[0].Id, 100,
                        new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 28 } },12);
                    if (interruption == 0) witness.IsAlive = false;
                    else if (interruption == 1) roster.Kill(roster.Guards[0].Id, 150);
                    else roster.Dispatch("other-crime", 150, new Dictionary<string, List<SavedRoutePoint>> {
                        { roster.Guards[0].Id, new List<SavedRoutePoint> { new SavedRoutePoint { X = 28 }, new SavedRoutePoint { X = 100 } } }
                    }, 1, 2.8, 12);
                    Check.Equal(0, store.AdvanceEntryMessengers(1, 220).Count);
                    Check.Equal(0, report.InformedGuards.Count);
                    Check.True(report.Messenger.Cancelled);
                    var copy = new WorldStateStore(0); copy.Restore(store.Snapshot());
                    Check.True(copy.Snapshot().EntryReports[0].Messenger.Cancelled);
                }
            }));
            cases.Add(("A citizen cannot carry simultaneous entry reports even in a restored save", () => {
                var store = new WorldStateStore(42); var town = store.GetOrCreate(1, new[] { 4, 8 }, 1);
                string witness = town.Households[1].Residents[0].Id;
                var roster = store.GetOrCreateGuards(1, 1, new List<SavedRoutePoint> { new SavedRoutePoint { X = 28 } });
                var first = store.RecordEntry(1, 4, 100, new[] { witness }, new string[0]);
                var second = store.RecordEntry(1, 4, 101, new[] { witness }, new string[0]);
                var route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 28 } };
                store.PlanEntryMessenger(first.Id, witness, roster.Guards[0].Id, 101, route);
                Check.Throws<ArgumentException>(() => store.PlanEntryMessenger(second.Id, witness, roster.Guards[0].Id, 101, route));
                var corrupt = store.Snapshot(); corrupt.EntryReports[1].Messenger = corrupt.EntryReports[0].Messenger;
                Check.Throws<ArgumentException>(() => store.Restore(corrupt));
                Check.True(store.Snapshot().EntryReports[1].Messenger == null);
            }));
            cases.Add(("Unseen entry creates no report and invalid witnesses reject transactionally", () => {
                var store = new WorldStateStore(42); var town = store.GetOrCreate(1, new[] { 4, 8 }, 1);
                Check.True(store.RecordEntry(1, 4, 100, new string[0], new string[0]) == null);
                Check.Equal(0, store.Snapshot().EntryReports.Count);
                string witness = town.Households[1].Residents[0].Id;
                Check.Throws<ArgumentException>(() => store.RecordEntry(1, 4, 100, new[] { witness, "unknown" }, new string[0]));
                Check.Equal(0, store.Snapshot().EntryReports.Count);
                town.Households[1].Residents[0].IsAlive = false;
                Check.Throws<ArgumentException>(() => store.RecordEntry(1, 4, 100, new[] { witness }, new string[0]));
            }));
            cases.Add(("Witnessed entry survives reload and later guard notification without invented observers", () => {
                var store = new WorldStateStore(42); var town = store.GetOrCreate(1, new[] { 4, 8 }, 1);
                string witness = town.Households[1].Residents[0].Id;
                var roster = store.GetOrCreateGuards(1, 1, new List<SavedRoutePoint> { new SavedRoutePoint() });
                string guard = roster.Guards[0].Id;
                var report = store.RecordEntry(1, 4, 100, new[] { witness }, new string[0]);
                Check.Equal(0, report.InformedGuards.Count);
                var copy = new WorldStateStore(0); copy.Restore(store.Snapshot());
                copy.TellGuardAboutEntry(report.Id, witness, guard, 120);
                var saved = copy.Snapshot().EntryReports[0];
                Check.Equal(1, saved.InformedGuards.Count); Check.Equal(100.0, saved.EnteredAt);
                copy.TellGuardAboutEntry(report.Id, witness, guard, 130);
                Check.Equal(1, copy.Snapshot().EntryReports[0].InformedGuards.Count);
                Check.Throws<ArgumentException>(() => copy.TellGuardAboutEntry(report.Id, town.Households[0].Residents[0].Id, guard, 140));
                var broken = copy.Snapshot(); broken.EntryReports[0].CitizenWitnesses.Add("unknown");
                Check.Throws<ArgumentException>(() => copy.Restore(broken));
                Check.Equal(1, copy.Snapshot().EntryReports[0].CitizenWitnesses.Count);
            }));
        }
    }
}
