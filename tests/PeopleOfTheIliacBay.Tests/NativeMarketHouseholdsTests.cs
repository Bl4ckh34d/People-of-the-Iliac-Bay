using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NativeMarketHouseholdsTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Exterior work reuses daily point routes with real commute and stationary work", () => {
                var create = typeof(DailySchedule).GetMethod("ExteriorWorkday");
                Check.True(create != null, "Missing exterior work schedule.");
                var schedule = (DailySchedule)create.Invoke(null, new object[] { 8,
                    new[] { new RoutePoint(0, 0), new RoutePoint(100, 0) },
                    new[] { new RoutePoint(100, 0), new RoutePoint(0, 0) },
                    new RoutePoint(100, 0), 360, 1080, 1.25d, 2d, 300, 1320 });
                Check.Equal(Activity.Home, schedule.EvaluateSeconds(350 * 60).Activity);
                Check.Equal(Activity.Travel, schedule.EvaluateSeconds(360 * 60 - 80).Activity);
                var atWork = schedule.EvaluateSeconds(360 * 60);
                Check.Equal(Activity.Work, atWork.Activity); Check.Equal(-1, atWork.BuildingKey);
                Check.Equal(100d, atWork.Position.X);
                Check.Equal(Activity.Work, schedule.EvaluateSeconds(1080 * 60 - 1).Activity);
                Check.Equal(Activity.Travel, schedule.EvaluateSeconds(1080 * 60).Activity);
                Check.Equal(Activity.Home, schedule.EvaluateSeconds(1080 * 60 + 160).Activity);
                Check.Equal(Activity.Sleep, schedule.EvaluateSeconds(1320 * 60).Activity);
                Check.True(!schedule.HasVisit, "Work post became an invented shop visit.");
            }));
            cases.Add(("Exterior native household reserves surveyed spare bed without replacing family or identity", () => {
                var assign = typeof(WorldStateStore).GetMethod("AssignExteriorNativeBed");
                Check.True(assign != null, "Missing exterior native household allocation.");
                var store = new WorldStateStore(42);
                var town = store.GetOrCreateFamilies(1, new[] { new HomeProfile(8, 3, 1) });
                var family = town.Households[0];
                family.Surname = "Oakcroft";
                int familyCount = family.Residents.Count;
                string head = family.HeadId;
                var seller = store.RegisterNativePerson(1, -1, 11, "Seller Kinghart", ResidentSex.Male, 1, 41, 182, 1);
                bool result = (bool)assign.Invoke(store, new object[] { seller.Id, new[] { new HomeProfile(8, 3, 1) } });
                Check.Equal(familyCount < 3, result);
                Check.Equal(familyCount + (result ? 1 : 0), family.Residents.Count);
                Check.Equal(head, family.HeadId);
                Check.Equal(1, store.NativePeopleFor(1).Count);
                Check.Equal(-1, seller.WorkKey);
                if (!result) return;
                Check.Equal(8, seller.HomeKey);
                Check.Equal(3, seller.BedCapacity);
                Check.Equal("Seller Oakcroft", seller.Name);
                Check.True(seller.BedSlot >= familyCount);
                Check.True(ReferenceEquals(seller, family.Residents[familyCount].NativeIdentity));
                Check.True(family.Residents[familyCount].PartnerId == null && family.Residents[familyCount].MotherId == null);
                Check.True((bool)assign.Invoke(store, new object[] { seller.Id, new HomeProfile[0] }));
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                var saved = restored.NativePersonInSettlement(1, seller.Id);
                Check.Equal(seller.HomeKey, saved.HomeKey); Check.Equal(seller.BedSlot, saved.BedSlot);
                Check.Equal(familyCount + 1, restored.Snapshot().Settlements[0].Households[0].Residents.Count);
                Check.True(ReferenceEquals(saved, restored.ResolveResident(seller.Id).NativeIdentity));
                Check.Equal("Seller Oakcroft", restored.ResolveResident(seller.Id).FullName);
            }));
            cases.Add(("Exterior housing rejects unsurveyed homes, unavailable beds and duplicate native bed claims", () => {
                var assign = typeof(WorldStateStore).GetMethod("AssignExteriorNativeBed");
                Check.True(assign != null, "Missing exterior native household allocation.");
                var store = new WorldStateStore(42);
                store.GetOrCreateFamilies(1, new[] { new HomeProfile(8, 1) });
                var seller = store.RegisterNativePerson(1, -1, 11, "Seller Kinghart", ResidentSex.Male, 1, 41, 182, 1);
                Check.True(!(bool)assign.Invoke(store, new object[] { seller.Id, new[] { new HomeProfile(77, 3), new HomeProfile(8, 1) } }));
                Check.True(!(bool)assign.Invoke(store, new object[] { seller.Id, new[] { new HomeProfile(8, 10) } }));
                Check.True(!(bool)assign.Invoke(store, new object[] { seller.Id, new[] { new HomeProfile(8, 1, 0, false) } }));
                Check.Equal(-1, seller.HomeKey); Check.Equal(-1, seller.BedSlot);
                Check.Equal(1, store.NativePeopleFor(1).Count);
            }));
            cases.Add(("Native household member keeps distinct bed and shared death state after restore", () => {
                var store = new WorldStateStore(42);
                var town = store.GetOrCreateFamilies(1, new[] { new HomeProfile(8, 8, 1) });
                var home = town.Households[0]; home.Surname = "Oakcroft";
                var first = store.RegisterNativePerson(1, -1, 11, "First Seller", ResidentSex.Male, 1, 41, 182, 1);
                var second = store.RegisterNativePerson(1, -1, 12, "Second Seller", ResidentSex.Female, 1, 41, 184, 1);
                Check.True(store.AssignExteriorNativeBed(first.Id, new[] { new HomeProfile(8, 8, 1) }));
                Check.True(store.AssignExteriorNativeBed(second.Id, new[] { new HomeProfile(8, 8, 1) }));
                Check.True(first.BedSlot != second.BedSlot);
                Check.True(Population.MarkDead(town, first.Id));
                Check.True(!first.IsAlive && second.IsAlive);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(!restored.ResolveResident(first.Id).IsAlive);
                Check.True(!restored.NativePersonInSettlement(1, first.Id).IsAlive);
                Check.True(ReferenceEquals(restored.ResolveResident(second.Id).NativeIdentity,
                    restored.NativePersonInSettlement(1, second.Id)));
                var corrupt = restored.Snapshot();
                corrupt.Settlements[0].Households[0].Residents.Find(r => r.Id == second.Id).BedSlot = first.BedSlot;
                Check.Throws<ArgumentException>(() => restored.Restore(corrupt));
                Check.Equal(second.BedSlot, restored.ResolveResident(second.Id).BedSlot);
            }));
        }
    }
}
