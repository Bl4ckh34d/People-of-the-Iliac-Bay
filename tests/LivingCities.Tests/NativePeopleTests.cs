using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class NativePeopleTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Native person identity preserves name and appearance across repeated registration and reload", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 8 }, 1);
                var person = store.RegisterNativePerson(1, 4, -123, "Ysausa Masterham", ResidentSex.Female, 1, 0, 184, 18);
                Check.Equal(person.Id, store.RegisterNativePerson(1, 4, -123, "Temporary quest label", ResidentSex.Female, 1, 0, 184, 18).Id);
                Check.Equal("Ysausa Masterham", person.Name);
                Check.Equal(1, store.Snapshot().NativePeople.Count);
                var snapshot = store.Snapshot(); snapshot.NativePeople[0].Name = "Changed copy";
                Check.Equal("Ysausa Masterham", store.Snapshot().NativePeople[0].Name);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                var saved = restored.NativePeopleFor(1)[0];
                Check.Equal(person.Id, saved.Id); Check.Equal(184, saved.TextureArchive); Check.Equal(18, saved.TextureRecord);
                Check.Equal(ResidentSex.Female, saved.Sex);
                Check.Throws<ArgumentException>(() => restored.RegisterNativePerson(1, 4, -123, "X", ResidentSex.Male, 1, 0, 184, 18));
            }));
            cases.Add(("Native keeper reserves on-site bed first and bedless staff use free residential capacity", () => {
                var store = new WorldStateStore(42); var town = store.GetOrCreate(1, new[] { 8 }, 1);
                town.Households[0].BedCapacity = 3;
                var first = store.RegisterNativePerson(1, 4, 1, "First Keeper", ResidentSex.Male, 1, 41, 182, 1);
                var second = store.RegisterNativePerson(1, 4, 2, "Second Keeper", ResidentSex.Female, 1, 41, 182, 2);
                var third = store.RegisterNativePerson(1, 5, 3, "Third Keeper", ResidentSex.Male, 1, 41, 182, 3);
                var fourth = store.RegisterNativePerson(1, 5, 4, "Fourth Keeper", ResidentSex.Female, 1, 41, 182, 4);
                var homes = new[] { new HomeProfile(8, 3, 100) };
                Check.True(store.AssignNativeBed(first.Id, new HomeProfile(4, 1, 10), homes));
                Check.Equal(4, first.HomeKey); Check.Equal(0, first.BedSlot);
                Check.True(store.AssignNativeBed(second.Id, new HomeProfile(4, 1, 10), homes));
                Check.Equal(8, second.HomeKey); Check.Equal(1, second.BedSlot);
                Check.True(store.AssignNativeBed(third.Id, new HomeProfile(5, 0, 10), homes));
                Check.Equal(8, third.HomeKey); Check.Equal(2, third.BedSlot);
                Check.True(!store.AssignNativeBed(fourth.Id, new HomeProfile(5, 0, 10), homes));
                Check.Equal(-1, fourth.HomeKey);
                Check.True(store.AssignNativeBed(first.Id, new HomeProfile(4, 0, 10), new HomeProfile[0]));
                Check.Equal(4, first.HomeKey); // A later survey cannot relocate a saved person silently.
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Equal(1, restored.NativePeopleFor(1).Find(p => p.Id == second.Id).BedSlot);
            }));
            cases.Add(("Native person restore rejects duplicated bed reservations and preserves legacy population", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 8 }, 1);
                var first = store.RegisterNativePerson(1, 4, 1, "Keeper One", ResidentSex.Male, 1, 41, 182, 1);
                var second = store.RegisterNativePerson(1, 4, 2, "Keeper Two", ResidentSex.Female, 1, 41, 182, 2);
                store.AssignNativeBed(first.Id, new HomeProfile(4, 2, 10), new HomeProfile[0]);
                store.AssignNativeBed(second.Id, new HomeProfile(4, 2, 10), new HomeProfile[0]);
                var corrupt = store.Snapshot(); corrupt.NativePeople[1].BedSlot = corrupt.NativePeople[0].BedSlot;
                Check.Throws<ArgumentException>(() => store.Restore(corrupt));
                Check.Equal(1, store.NativePeopleFor(1)[1].BedSlot);
                var legacy = store.Snapshot(); legacy.NativePeople = null;
                var restored = new WorldStateStore(0); restored.Restore(legacy);
                Check.Equal(0, restored.NativePeopleFor(1).Count); Check.Equal(1, restored.Snapshot().Settlements.Count);
            }));
        }
    }
}
