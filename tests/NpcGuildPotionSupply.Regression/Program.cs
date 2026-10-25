using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private const int Settlement = 41;
    private const int PatientHome = 12;
    private const int ProviderWork = 88;
    private const NpcArchetype Archetype = NpcArchetype.Citizen;

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("potion offers require persisted eligible injury and affordable wallets", OfferEligibility),
            ("daily provider restock persists eight exact native item records", FirstRestockAndExactOffer),
            ("day rollover replaces only the prior generated stock", DailyRestockBound),
            ("same-day restock is idempotent across save reload", ReloadDoesNotRegenerate),
            ("unfinished care tasks reserve exact provider stock across restock", ReservationsSurviveRollover),
            ("unknown dead providers and invalid stock proposals fail closed", InvalidRestocksFailClosed)
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void OfferEligibility()
    {
        Fixture f = NewFixture();
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 100),
            "full health cannot create a potion offer");
        NpcVitalsView initial = f.Store.ReadNpcVitals(f.PatientId, Archetype);
        int aboveThreshold = Math.Max(1, (int)((long)initial.MaximumHealth * 3 / 4) + 1);
        CaptureHealth(f.Store, f.PatientId, aboveThreshold, 90);
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 100),
            "injury above care threshold cannot create a potion offer");

        int threshold = (int)((long)initial.MaximumHealth * 3 / 4);
        CaptureHealth(f.Store, f.PatientId, Math.Max(1, threshold), 95);
        True(NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 100),
            "persisted injury at care threshold can create an offer");
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 1000, 100),
            "buyer wallet must cover quoted price");
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 94),
            "future vitals cannot authorize a purchase");

        f.Store.ResolveResident(f.PatientId).IsQuestProtected = true;
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 100),
            "quest-protected patient cannot create an offer");
        f.Store.ResolveResident(f.PatientId).IsQuestProtected = false;
        f.Store.ResolveResident(f.PatientId).AgeGroup = ResidentAgeGroup.Child;
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 100),
            "child cannot create this adult care offer");

        Fixture dead = NewFixture();
        CaptureHealth(dead.Store, dead.PatientId, dead.Store.ReadNpcVitals(dead.PatientId, Archetype).MaximumHealth / 2, 90);
        Population.MarkDead(dead.Store.GetOrCreate(Settlement, new[] { PatientHome }, 1), dead.PatientId);
        True(!NpcGuildPotionSupply.CanOffer(dead.Store, dead.PatientId, Archetype, dead.ProviderId, 10, 100),
            "dead patient cannot create an offer");
        True(!NpcGuildPotionSupply.CanOffer(f.Store, "unknown-resident", Archetype, f.ProviderId, 10, 100),
            "unknown patient cannot create an offer");
    }

    private static void FirstRestockAndExactOffer()
    {
        Fixture f = NewFixture();
        Injure(f);
        True(NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 100), "patient qualifies");
        long sellerRevision = f.Store.ReadNpcInventory(f.ProviderId).Revision;
        List<NpcInventoryItem> stock = StockItems(1000, NpcGuildPotionSupply.DailyStockUnits);
        True(NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 5, 100, 10,
            sellerRevision, stock, out string reason), reason ?? "first daily restock should commit");

        NpcGuildPotionStockState record = NpcGuildPotionSupply.ReadStock(f.Store, f.ProviderId);
        Equal(5L, record.RestockedDay);
        Equal(NpcGuildPotionSupply.DailyStockUnits, record.ItemUids.Count);
        NpcInventoryState provider = f.Store.ReadNpcInventory(f.ProviderId);
        Equal(NpcGuildPotionSupply.DailyStockUnits, provider.Items.Count);
        Equal(sellerRevision + 1, provider.Revision);
        foreach (ulong uid in record.ItemUids)
            True(provider.Items.Exists(item => item.Uid == uid), "recorded stock UID must persist in provider inventory");

        ulong exactUid = record.ItemUids[3];
        True(NpcGuildPotionSupply.TryCreateOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 5, exactUid, 10, 100,
            out NpcGuildPotionOffer offer), "current generated item should form an offer");
        Equal(exactUid, offer.ItemUid);
        Equal(f.ProviderId, offer.SellerPersonId);
        Equal(10, offer.Price);
        Equal(NpcGuildPotionSupply.GuildPotionContainerId.ToString(), offer.ContainerId);
        Equal(provider.Revision, offer.SellerInventoryRevision);
        True(!NpcGuildPotionSupply.TryCreateOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 5, 999999, 10, 100,
            out _), "unowned UID cannot form an offer");
    }

    private static void DailyRestockBound()
    {
        Fixture f = NewFixture();
        Injure(f);
        NpcInventoryState provider = f.Store.ReadNpcInventory(f.ProviderId);
        var initial = new List<NpcInventoryItem>(provider.Items)
        {
            new NpcInventoryItem { Uid = 42, NativePayload = "native-unrelated-item" }
        };
        True(f.Store.CommitNpcInventoryTransaction("fixture-unrelated", new[]
        {
            new NpcInventoryUpdate { PersonId = f.ProviderId, ExpectedRevision = provider.Revision,
                WalletGold = provider.WalletGold, Items = initial }
        }), "unrelated provider item fixture should commit");

        long revision = f.Store.ReadNpcInventory(f.ProviderId).Revision;
        True(NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 9, 100, 10,
            revision, StockItems(1000, NpcGuildPotionSupply.DailyStockUnits), out _), "first day stock should commit");
        List<ulong> oldUids = new List<ulong>(NpcGuildPotionSupply.ReadStock(f.Store, f.ProviderId).ItemUids);
        revision = f.Store.ReadNpcInventory(f.ProviderId).Revision;
        True(NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 110, 10,
            revision, StockItems(2000, NpcGuildPotionSupply.DailyStockUnits), out _), "next day should replace prior generated stock");

        provider = f.Store.ReadNpcInventory(f.ProviderId);
        Equal(NpcGuildPotionSupply.DailyStockUnits + 1, provider.Items.Count);
        True(provider.Items.Exists(item => item.Uid == 42), "unrelated native provider item must remain");
        foreach (ulong oldUid in oldUids)
            True(!provider.Items.Exists(item => item.Uid == oldUid), "prior generated stock must expire on next restock");
        foreach (ulong uid in NpcGuildPotionSupply.ReadStock(f.Store, f.ProviderId).ItemUids)
            True(provider.Items.Exists(item => item.Uid == uid), "new daily stock UID must persist");
    }

    private static void ReloadDoesNotRegenerate()
    {
        Fixture f = NewFixture();
        Injure(f);
        True(NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 17, 100, 10,
            f.Store.ReadNpcInventory(f.ProviderId).Revision, StockItems(3000, NpcGuildPotionSupply.DailyStockUnits), out _),
            "initial stock should commit");

        var options = new JsonSerializerOptions { IncludeFields = true };
        WorldState wire = JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(f.Store.Snapshot(), options), options);
        var restored = new WorldStateStore(0);
        restored.Restore(wire);
        NpcInventoryState before = restored.ReadNpcInventory(f.ProviderId);
        NpcGuildPotionStockState record = NpcGuildPotionSupply.ReadStock(restored, f.ProviderId);
        True(!NpcGuildPotionSupply.TryRestockForOffer(restored, f.PatientId, Archetype, f.ProviderId, 17, 110, 10,
            before.Revision, StockItems(4000, NpcGuildPotionSupply.DailyStockUnits), out _),
            "reload retry must not regenerate stock on same day");
        Equal(before.Revision, restored.ReadNpcInventory(f.ProviderId).Revision);
        Equal(record.ItemUids.Count, restored.ReadNpcInventory(f.ProviderId).Items.Count);
        Equal(17L, NpcGuildPotionSupply.ReadStock(restored, f.ProviderId).RestockedDay);

        wire.Version = 43;
        wire.NpcGuildPotionStocks = null;
        // Build a genuine schema-43 fixture instead of attaching a schema-54
        // experience-inbox header to an older version number.
        wire.ConversationCueArchive = null;
        wire.NpcExperienceInbox = NpcExperienceInbox.Create();
        wire.NpcExperienceInbox.Version = 4;
        var migrated = new WorldStateStore(0);
        migrated.Restore(wire);
        True(NpcGuildPotionSupply.ReadStock(migrated, f.ProviderId) == null,
            "schema 43 without guild stock migrates to empty ledger");
    }

    private static void ReservationsSurviveRollover()
    {
        Fixture f = NewFixture();
        Injure(f);
        True(NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 9, 100, 10,
            f.Store.ReadNpcInventory(f.ProviderId).Revision, StockItems(11000, NpcGuildPotionSupply.DailyStockUnits), out _),
            "initial stock should commit");
        ulong reservedUid = NpcGuildPotionSupply.ReadStock(f.Store, f.ProviderId).ItemUids[0];

        SettlementPopulation secondTown = f.Store.GetOrCreate(142, new[] { 19 }, 1);
        string otherPatient = secondTown.Households[0].Residents[0].Id;
        True(f.Store.InitializeNpcInventory(otherPatient, 200, Array.Empty<NpcInventoryItem>()), "other buyer inventory should initialize");
        NpcVitalsView otherVitals = f.Store.ReadNpcVitals(otherPatient, Archetype);
        CaptureHealth(f.Store, otherPatient, otherVitals.MaximumHealth / 2, 90);
        True(ClassifiedAdmissionFixture.TryAcceptTask(f.Store, PurchaseTask(otherPatient, f.ProviderId, reservedUid), true, out _),
            "second patient can reserve the exact stock UID in a pending task");

        True(!NpcGuildPotionSupply.TryCreateOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 9,
            reservedUid, 10, 100, out _), "offer must hide stock reserved by another patient");
        True(NpcGuildPotionSupply.TryCreateOffer(f.Store, otherPatient, Archetype, f.ProviderId, 9,
            reservedUid, 10, 100, out _), "the reserving patient can reuse its exact stock UID");

        long sellerRevision = f.Store.ReadNpcInventory(f.ProviderId).Revision;
        True(NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 110, 10,
            sellerRevision, StockItems(12000, NpcGuildPotionSupply.DailyStockUnits), out _),
            "next day restock should keep the unfinished task's source item");
        NpcGuildPotionStockState after = NpcGuildPotionSupply.ReadStock(f.Store, f.ProviderId);
        True(after.ReservedItemUids.Contains(reservedUid), "previous UID remains explicitly reserved");
        NpcInventoryState provider = f.Store.ReadNpcInventory(f.ProviderId);
        True(provider.Items.Exists(item => item.Uid == reservedUid), "reserved native item remains in seller inventory");
        True(NpcGuildPotionSupply.TryCreateOffer(f.Store, otherPatient, Archetype, f.ProviderId, 10,
            reservedUid, 10, 110, out _), "same pending task can still use its carried reservation");
        True(!NpcGuildPotionSupply.TryCreateOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10,
            reservedUid, 10, 110, out _), "carried reservation remains hidden from other patients");
        Equal(NpcGuildPotionSupply.DailyStockUnits + 1, provider.Items.Count);
    }

    private static NpcTaskPlan PurchaseTask(string patientId, string providerId, ulong uid)
    {
        var target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint,
            SettlementId = Settlement, BuildingKey = ProviderWork, PersonId = providerId,
            ContainerId = NpcGuildPotionSupply.GuildPotionContainerId.ToString(),
            ItemId = uid.ToString(), Quantity = 1, QuotedGold = 10 };
        return new NpcTaskPlan { Id = "test:guild-potion:" + patientId,
            ProposalId = "test:guild-potion:" + patientId, ResidentId = patientId,
            Steps = new List<NpcTaskStep> { new NpcTaskStep { Kind = NpcActionKind.Purchase, Target = target } },
            Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = 100, UpdatedAt = 100, DeadlineAt = 1000 };
    }

    private static void InvalidRestocksFailClosed()
    {
        Fixture f = NewFixture();
        Injure(f);
        long revision = f.Store.ReadNpcInventory(f.ProviderId).Revision;
        True(!NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, "unknown-provider", 2, 100,
            10, 0, StockItems(5000, NpcGuildPotionSupply.DailyStockUnits), out _), "unknown provider cannot restock");
        f.Provider.IsAlive = false;
        True(!NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 2, 100,
            10, revision, StockItems(6000, NpcGuildPotionSupply.DailyStockUnits), out _), "dead provider cannot restock");
        f.Provider.IsAlive = true;
        True(!NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, f.ProviderId, -1, 100,
            10, revision, StockItems(7000, NpcGuildPotionSupply.DailyStockUnits), out _), "negative day cannot restock");
        True(!NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 2, 100,
            10, revision + 1, StockItems(8000, NpcGuildPotionSupply.DailyStockUnits), out _), "stale provider inventory revision cannot restock");
        True(!NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 2, 100,
            10, revision, StockItems(9000, NpcGuildPotionSupply.DailyStockUnits - 1), out _), "short restock cannot commit");
        True(!NpcGuildPotionSupply.TryRestockForOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 2, 100,
            10, revision, StockItems(10000, NpcGuildPotionSupply.DailyStockUnits, true), out _), "duplicate item UID cannot commit");
        True(!NpcGuildPotionSupply.CanOffer(f.Store, f.PatientId, Archetype, f.ProviderId, 10, 89),
            "vitals captured after observation time cannot authorize an offer");
        True(NpcGuildPotionSupply.ReadStock(f.Store, f.ProviderId) == null, "rejected attempts must create no stock marker");
    }

    private static void Injure(Fixture f)
    {
        NpcVitalsView vitals = f.Store.ReadNpcVitals(f.PatientId, Archetype);
        CaptureHealth(f.Store, f.PatientId, Math.Max(1, (int)((long)vitals.MaximumHealth * 3 / 4)), 90);
    }

    private static void CaptureHealth(WorldStateStore store, string personId, int health, double now)
    {
        NpcVitalsView before = store.ReadNpcVitals(personId, Archetype);
        True(store.TryCaptureNpcVitals(personId, Archetype, health, before.CurrentMagicka, now, before.Revision),
            "native vitals capture should accept current fixture sample");
    }

    private static List<NpcInventoryItem> StockItems(ulong firstUid, int count, bool duplicate = false)
    {
        var items = new List<NpcInventoryItem>();
        for (int i = 0; i < count; i++)
            items.Add(new NpcInventoryItem { Uid = duplicate && i == count - 1 ? firstUid : firstUid + (ulong)i,
                NativePayload = "native-potion:" + (firstUid + (ulong)i) });
        return items;
    }

    private static Fixture NewFixture()
    {
        var store = new WorldStateStore(123);
        store.GetOrCreate(Settlement, new[] { PatientHome }, 1);
        string patientId = store.Snapshot().Settlements[0].Households[0].Residents[0].Id;
        NativePerson provider = store.RegisterNativePerson(Settlement, ProviderWork, 600, "Guild Healer",
            ResidentSex.Female, 1, 88, 182, 1);
        True(store.InitializeNpcInventory(patientId, 200, Array.Empty<NpcInventoryItem>()), "buyer inventory should initialize");
        True(store.InitializeNpcInventory(provider.Id, 50, Array.Empty<NpcInventoryItem>()), "provider inventory should initialize");
        return new Fixture { Store = store, PatientId = patientId, Provider = provider, ProviderId = provider.Id };
    }

    private sealed class Fixture
    {
        public WorldStateStore Store;
        public string PatientId, ProviderId;
        public NativePerson Provider;
    }

    private static void True(bool value, string message = "assertion failed")
    { if (!value) throw new InvalidOperationException(message); }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new InvalidOperationException("expected " + expected + ", got " + actual);
    }
}
