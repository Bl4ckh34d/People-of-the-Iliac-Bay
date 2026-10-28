using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const double Now = 72000d;
    private const double RestDuration = 8 * 3600d;
    private static readonly BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("rest quote rounds healing hours and nights up", RestQuoteRoundsUp),
            ("two-night Inn booking debits NPC wallet once and survives reload", TwoNightBookingSurvivesReload),
            ("zero-price Inn booking reserves without debiting", ZeroPriceBooking),
            ("insufficient NPC funds publish neither booking nor debit", InsufficientFundsAreAtomic),
            ("full bed does not debit the player or NPC", FullBedDoesNotChargeAnyone),
            ("ordinary local lodging booking cannot claim healthcare authority", OrdinaryLocalBookingRemainsRejected)
        };

        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void RestQuoteRoundsUp()
    {
        Equal(6, NpcHealthRestQuoteMath.RequiredGameHours(17, 3));
        Equal(1, NpcHealthRestQuoteMath.NightsFor(17, 3));
        Equal(6 * 3600d, NpcHealthRestQuoteMath.NextRestDurationSeconds(17, 3));
        Equal(9, NpcHealthRestQuoteMath.RequiredGameHours(25, 3));
        Equal(2, NpcHealthRestQuoteMath.NightsFor(25, 3));
        Equal(8 * 3600d, NpcHealthRestQuoteMath.NextRestDurationSeconds(25, 3));
        Equal(1, NpcHealthRestQuoteMath.NightsForDurationSeconds(7 * 3600d + 30 * 60d));
        Equal(2, NpcHealthRestQuoteMath.NightsForDurationSeconds(8 * 3600d + 1));
    }

    private static void ZeroPriceBooking()
    {
        var fixture = CreateFixture(0);
        object result;
        True(Reserve(fixture, 1, 0, out result), "a native free-night quote should reserve without wallet funds");
        Equal(0, fixture.Store.ReadNpcInventory(fixture.ResidentId).WalletGold);
        Equal(1L, fixture.Store.ReadNpcInventory(fixture.ResidentId).Revision);
        Equal(1, Bookings(fixture.Store).Count);
        Equal(0, (int)Field(result, "Price"));
    }

    private static void TwoNightBookingSurvivesReload()
    {
        var fixture = CreateFixture(100);
        object result;
        True(Reserve(fixture, 2, 14, out result), "accepted care task should reserve its exact Inn bed");
        Equal(86, fixture.Store.ReadNpcInventory(fixture.ResidentId).WalletGold);
        Equal(1L, fixture.Store.ReadNpcInventory(fixture.ResidentId).Revision);
        Equal(1, Bookings(fixture.Store).Count);
        Equal(2, (int)Field(result, "Nights"));
        Equal(14, (int)Field(result, "Price"));

        object replay;
        True(Reserve(fixture, 2, 14, out replay), "replaying the same task must return its original reservation");
        Equal(86, fixture.Store.ReadNpcInventory(fixture.ResidentId).WalletGold);
        Equal(1L, fixture.Store.ReadNpcInventory(fixture.ResidentId).Revision);
        Equal(1, Bookings(fixture.Store).Count);

        var restored = new WorldStateStore(0);
        restored.Restore(fixture.Store.Snapshot());
        Equal(86, restored.ReadNpcInventory(fixture.ResidentId).WalletGold);
        Equal(1, Bookings(restored).Count);
        object loaded = ActiveReservation(restored, fixture.ResidentId, fixture.Care.Id, Now + 1);
        True(loaded != null, "verified health reservation should reload");
        Equal("bed:4", (string)Field(loaded, "BedId"));
        Equal(Now + 2 * 86400d, (double)Field(loaded, "EndsAt"));
    }

    private static void InsufficientFundsAreAtomic()
    {
        var fixture = CreateFixture(13);
        object result;
        True(!Reserve(fixture, 2, 14, out result), "the NPC cannot afford the two-night native quote");
        Equal(null, result);
        Equal(13, fixture.Store.ReadNpcInventory(fixture.ResidentId).WalletGold);
        Equal(0L, fixture.Store.ReadNpcInventory(fixture.ResidentId).Revision);
        Equal(0, Bookings(fixture.Store).Count);
    }

    private static void FullBedDoesNotChargeAnyone()
    {
        var fixture = CreateFixture(100);
        True(fixture.Store.TryBookLodging(1, 99, "player-booking", "player", 1, true, 1,
            Now, Now + 2 * 86400d, "bed:4"), "the player fixture should reserve the bed first");
        object result;
        True(!Reserve(fixture, 2, 14, out result), "healthcare cannot displace a player's bed");
        Equal(null, result);
        Equal(100, fixture.Store.ReadNpcInventory(fixture.ResidentId).WalletGold);
        Equal(0L, fixture.Store.ReadNpcInventory(fixture.ResidentId).Revision);
        Equal(1, Bookings(fixture.Store).Count);
    }

    private static void OrdinaryLocalBookingRemainsRejected()
    {
        var store = new WorldStateStore(42);
        store.GetOrCreate(1, new[] { 10 }, 1);
        store.RegisterLodgingBeds(1, 99, Beds());
        bool rejected = false;
        try { store.TryBookLodging(1, 99, "ordinary-local", "visitor", 1, false, 1,
            Now, Now + 86400d, "bed:4"); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "the general lodging API cannot use a string prefix as healthcare authority");
        Equal(0, Bookings(store).Count);
    }

    private static Fixture CreateFixture(int gold)
    {
        var store = new WorldStateStore(42);
        var town = store.GetOrCreate(1, new[] { 10 }, 1);
        Resident resident = town.Households[0].Residents.Find(value => value.AgeGroup == ResidentAgeGroup.Adult);
        if (resident == null) throw new InvalidOperationException("Fixture lacks an adult resident.");
        True(store.InitializeNpcInventory(resident.Id, gold, new List<NpcInventoryItem>()), "NPC wallet should initialize");
        var vitals = store.ReadNpcVitals(resident.Id, NpcArchetype.Citizen);
        int careThreshold = (int)((long)vitals.MaximumHealth * 3 / 5);
        True(store.TryCaptureNpcVitals(resident.Id, NpcArchetype.Citizen,
            Math.Max(1, careThreshold - 1), vitals.CurrentMagicka, Now - 10, vitals.Revision), "injury should persist");
        True(store.TryBeginNpcHealthCare(resident.Id, NpcArchetype.Citizen, Now - 5), "health episode should begin");
        var care = store.ReadNpcHealthCare(resident.Id);
        string taskId = NpcHealthCare.MakeTaskId(care);
        var target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 1,
            BuildingKey = 99, ContainerId = "bed:4", X = 1, Y = 0, Z = 2 };
        var task = new NpcTaskPlan { Id = taskId, ProposalId = taskId, ResidentId = resident.Id,
            Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = Now - 4, UpdatedAt = Now - 4,
            DeadlineAt = Now + RestDuration + 1800d,
            Steps = new List<NpcTaskStep> { new NpcTaskStep { Kind = NpcActionKind.RestToHeal,
                Target = target, DurationSeconds = RestDuration } } };
        string reason;
        True(store.TryAcceptAndBindNpcHealthCareTask(resident.Id, care.Revision, task, Now - 4, out reason), "rest task should accept and bind: " + reason);
        care = store.ReadNpcHealthCare(resident.Id);
        store.RegisterLodgingBeds(1, 99, Beds());
        return new Fixture { Store = store, ResidentId = resident.Id, Care = care, Task = task };
    }

    private static List<LodgingBed> Beds()
    {
        return new List<LodgingBed> { new LodgingBed { Id = "bed:4" } };
    }

    private static bool Reserve(Fixture fixture, int nights, int price, out object result)
    {
        result = null;
        MethodInfo method = typeof(WorldStateStore).GetMethod("TryReserveNpcHealthRestLodging", AnyInstance);
        True(method != null, "Core atomic health lodging API must exist");
        Type requestType = typeof(WorldStateStore).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcHealthLodgingReservationRequest");
        True(requestType != null, "health lodging request type must exist");
        object request = Activator.CreateInstance(requestType, true);
        Put(request, "ResidentId", fixture.ResidentId);
        Put(request, "CareEpisodeId", fixture.Care.Id);
        Put(request, "TaskId", fixture.Task.Id);
        Put(request, "ExpectedCareRevision", fixture.Care.Revision);
        Put(request, "SettlementId", 1);
        Put(request, "BuildingKey", 99);
        Put(request, "BedId", "bed:4");
        Put(request, "StartsAt", Now);
        Put(request, "Nights", nights);
        Put(request, "Price", price);
        Put(request, "ExpectedInventoryRevision", fixture.Store.ReadNpcInventory(fixture.ResidentId).Revision);
        object[] args = { request, null };
        bool accepted = (bool)method.Invoke(fixture.Store, args);
        result = args[1];
        return accepted;
    }

    private static List<LodgingBooking> Bookings(WorldStateStore store)
    {
        return store.Snapshot().Lodgings.Find(value => value.SettlementId == 1 && value.BuildingKey == 99).Bookings;
    }

    private static object ActiveReservation(WorldStateStore store, string residentId, string careId, double now)
    {
        MethodInfo method = typeof(WorldStateStore).GetMethod("NpcHealthRestLodging", AnyInstance);
        True(method != null, "read-side active healthcare reservation API must exist");
        return method.Invoke(store, new object[] { residentId, careId, now });
    }

    private static void Put(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        True(field != null, "request field missing: " + name);
        field.SetValue(target, value);
    }

    private static object Field(object target, string name)
    {
        True(target != null, "reservation must be present");
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        True(field != null, "reservation field missing: " + name);
        return field.GetValue(target);
    }

    private static void True(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }

    private sealed class Fixture
    {
        public WorldStateStore Store;
        public string ResidentId;
        public NpcHealthCareRecord Care;
        public NpcTaskPlan Task;
    }
}
