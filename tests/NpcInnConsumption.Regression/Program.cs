using System;
using System.Collections.Generic;
using System.Text.Json;
using FullSerializer;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static int failures;

    private static int Main()
    {
        Run("native food service requires no keeper inventory",()=>{
            var f=CreateFixture(20,40);var state=f.Store.Snapshot();state.NpcInventories.RemoveAll(i=>i.PersonId==f.Keeper.Id);f.Store.Restore(state);
            True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id,f.Keeper.Id,102,NpcInnConsumptionKind.Food,4,"Bread",3,out var receipt),"native food service must not require a seller inventory");
            Equal(17,f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);True(receipt.FoodFulfilled,"receipt must record paid food");
            True(f.Store.ReadNpcInventory(f.Keeper.Id)==null,"service must not create a keeper inventory");
        });
        Run("NPC pays once for native tavern menu service across reload and checkpoint capture", PaidVisitPersistsOnce);
        Run("NPC with no funds records failed service without food or charge", NoFundsDoesNotFulfill);
        Run("inn menu choice is deterministic among affordable native offers", AffordableMenuChoice);
        Run("saved receipts cannot claim simultaneous food and drink fulfillment", RejectDualFulfillmentReceipt);
        Run("native menu category mismatch cannot settle or debit a visit", RejectMenuCategoryMismatch);
        Run("saved receipts and cursors retain the native menu category", RejectSavedMenuCategoryMismatch);
        Run("keeper refusal records no consumption and no charge", RefusalDoesNotFulfill);
        Run("purchase cannot commit before counter haggling or after table stage", PurchaseRequiresCounterStage);
        Run("counter payment waits for observed physical arrival within the arrival radius", CounterPaymentNeedsObservedArrival);
        Run("only the native keeper at the visited building can receive payment", RejectWrongKeeper);
        Run("counter callback requires a tavern purchase and actual keeper arrival", CounterCallbackRequiresArrival);
        Run("new visit does not inherit previous visit receipt", NewVisitHasFreshReceipt);
        Run("settled visit survives removal, unrelated wallet changes, and reload", SettledVisitSurvivesRemoval);
        Run("replayed visit cannot change quoted terms or reuse an older visit time", ReplayedTermsAndVisitFrontier);
        Run("retired visit records unavailable service and permits later visits", RetiredVisitRecordsUnavailable);
        Run("schema 44 active receipt seeds durable settlement cursor", LegacyActiveReceiptMigration);
        Run("failed inn service remains settled after visit removal and reload", FailedVisitSurvivesRemoval);
        Run("native FullSerializer retains fractional campaign-time payment identity", FractionalCampaignTimeNativeCodec);
        Run("native fifteen-digit campaign time retains ordinary Inn payment identity", NativeCampaignTimePrecision);
        Console.WriteLine($"NpcInnConsumption regression: {failures} failure(s).");
        return failures == 0 ? 0 : 1;
    }

    private static void PaidVisitPersistsOnce()
    {
        Fixture f = CreateFixture(20, 40);
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionKind.Drink, 1, "Beer", 5, out NpcInnConsumptionReceipt receipt), "purchase attempt should record");
        Equal(NpcInnConsumptionOutcome.Purchased, receipt.Outcome);
        Equal("Beer", receipt.ProductName);
        Equal(5, receipt.ChargedGold);
        True(receipt.DrinkFulfilled && !receipt.FoodFulfilled, "drink receipt should fulfill only drink");
        Equal(15, f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);
        Equal(40, f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);

        ObservedVisit checkpoint = f.Store.GetObservedVisit(f.Buyer.Id);
        checkpoint.ObservedAt = 101;
        f.Store.StoreObservedVisit(checkpoint);
        WorldState saved = JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(f.Store.Snapshot(), Json), Json);
        WorldStateStore restored = new WorldStateStore(0);
        restored.Restore(saved);
        NpcInnConsumptionReceipt restoredReceipt = restored.ReadNpcInnConsumption(f.Buyer.Id);
        True(restoredReceipt != null, "receipt should survive save/load and checkpoint replacement");
        Equal(receipt.VisitId, restoredReceipt.VisitId);
        True(!restored.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 103,
            NpcInnConsumptionKind.Drink, 1, "Beer", 5, out _), "replay should be rejected");
        Equal(15, restored.ReadNpcInventory(f.Buyer.Id).WalletGold);
        Equal(40, restored.ReadNpcInventory(f.Keeper.Id).WalletGold);
    }

    private static void NoFundsDoesNotFulfill()
    {
        Fixture f = CreateFixture(0, 40);
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out NpcInnConsumptionReceipt receipt), "unaffordable attempt should be durable");
        Equal(NpcInnConsumptionOutcome.InsufficientFunds, receipt.Outcome);
        Equal(0, receipt.ChargedGold);
        True(!receipt.FoodFulfilled && !receipt.DrinkFulfilled, "failed payment must not fulfill food or drink");
        Equal(0, f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);
        Equal(40, f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);
        True(!f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 103,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out _), "same visit cannot retry after durable no-funds outcome");
    }

    private static void AffordableMenuChoice()
    {
        int[] prices = { 1, 3, 5, 2, 4, 6, 7, 8, 9, 10, 11 };
        int first = NpcInnConsumption.SelectMenuIndex(prices, 3, 17);
        int second = NpcInnConsumption.SelectMenuIndex(prices, 3, 17);
        Equal(first, second);
        True(first == 0 || first == 1 || first == 3, "affordable wallet must exclude higher-price items");
        Equal(0, NpcInnConsumption.SelectMenuIndex(prices, 0, 19));
    }

    private static void RejectDualFulfillmentReceipt()
    {
        Fixture f = CreateFixture(20, 40);
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out _), "food purchase should succeed");
        WorldState invalid = f.Store.Snapshot();
        invalid.ObservedVisits[0].InnConsumption.DrinkFulfilled = true;
        bool rejected = false;
        try { new WorldStateStore(0).Restore(invalid); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "a food receipt must reject a simultaneous drink fulfillment claim");
    }

    private static void RejectMenuCategoryMismatch()
    {
        for (int index = 0; index < 11; index++)
        {
            Fixture f = CreateFixture(20, 40);
            var wrongKind = index < 4 ? NpcInnConsumptionKind.Food : NpcInnConsumptionKind.Drink;
            string before = JsonSerializer.Serialize(f.Store.Snapshot(), Json);
            True(!f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
                wrongKind, index, "Native menu item", 3, out var rejected),
                "mismatched kind must reject index " + index);
            True(rejected == null, "rejected purchase cannot expose a receipt");
            Equal(before, JsonSerializer.Serialize(f.Store.Snapshot(), Json));
            var rightKind = index < 4 ? NpcInnConsumptionKind.Drink : NpcInnConsumptionKind.Food;
            True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
                rightKind, index, "Native menu item", 3, out var paid),
                "invalid attempt must not settle the real visit");
            Equal(17, f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);
            Equal(40, f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);
            Equal(index >= 4, paid.FoodFulfilled);
        }
    }

    private static void RejectSavedMenuCategoryMismatch()
    {
        foreach (bool removeVisit in new[] { false, true })
        foreach (int gold in new[] { 0, 20 })
        foreach (int index in new[] { 0, 3, 4, 10 })
        {
            Fixture f = CreateFixture(gold, 40);
            var kind = index < 4 ? NpcInnConsumptionKind.Drink : NpcInnConsumptionKind.Food;
            True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
                kind, index, "Native menu item", 3, out _), "valid source purchase");
            if (removeVisit) f.Store.RemoveObservedVisit(f.Buyer.Id);
            WorldState malformed = f.Store.Snapshot();
            // Mutate the product index only; flags and both copies still agree.
            // This isolates native category validation from duplicate-receipt checks.
            int wrongIndex = index < 4 ? 4 : 0;
            malformed.NpcInnConsumptionCursors[0].Receipt.MenuIndex = wrongIndex;
            if (!removeVisit) malformed.ObservedVisits[0].InnConsumption.MenuIndex = wrongIndex;
            var restored = new WorldStateStore(99);
            string before = JsonSerializer.Serialize(restored.Snapshot(), Json);
            bool rejected = false;
            try { restored.Restore(malformed); }
            catch (ArgumentException) { rejected = true; }
            True(rejected, "category mismatch must reject active and retired paid/unaffordable receipts");
            Equal(before, JsonSerializer.Serialize(restored.Snapshot(), Json));
        }
    }

    private static void RefusalDoesNotFulfill()
    {
        Fixture f = CreateFixture(20, 40);
        True(f.Store.TryRecordNpcInnNoPurchase(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionOutcome.Refused, "keeper refused service", out NpcInnConsumptionReceipt receipt), "refusal should be durable");
        Equal(NpcInnConsumptionOutcome.Refused, receipt.Outcome);
        True(!receipt.FoodFulfilled && !receipt.DrinkFulfilled, "refusal must not fulfill food or drink");
        Equal(20, f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);
        Equal(40, f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);
        True(!f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 103,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out _), "refusal receipt should prevent later charge");
    }

    private static void PurchaseRequiresCounterStage()
    {
        Fixture f = CreateFixture(20, 40);
        True(!f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 100,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out _), "purchase must wait for haggling stop");
        True(f.Store.ReadNpcInnConsumption(f.Buyer.Id) == null, "early attempt must not record consumption");
        True(!f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 70,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out _), "purchase must not happen at the table");
        Equal(20, f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);
        Equal(40, f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);
    }

    private static void CounterCallbackRequiresArrival()
    {
        True(NpcInnConsumption.ShouldRunCounterPurchaseCallback(true, true, ShopVisitPhase.Haggling, true),
            "arrived tavern purchase should call service callback");
        True(!NpcInnConsumption.ShouldRunCounterPurchaseCallback(true, true, ShopVisitPhase.Haggling, false),
            "route must arrive before purchase callback");
        True(!NpcInnConsumption.ShouldRunCounterPurchaseCallback(true, true, ShopVisitPhase.Browsing, true),
            "table stop must not purchase");
        True(!NpcInnConsumption.ShouldRunCounterPurchaseCallback(false, true, ShopVisitPhase.Haggling, true),
            "ordinary shop haggling must not use inn service");
        True(!NpcInnConsumption.ShouldRunCounterPurchaseCallback(true, false, ShopVisitPhase.Haggling, true),
            "non-purchase inn visit must not use service");
    }

    private static void RejectWrongKeeper()
    {
        Fixture f = CreateFixture(20, 40);
        NativePerson otherKeeper = f.Store.RegisterNativePerson(1, f.Buyer.HomeKey, 901,
            "Other keeper", ResidentSex.Male, 0, 0, 182, 0);
        True(f.Store.InitializeNpcInventory(otherKeeper.Id, 30, Array.Empty<NpcInventoryItem>()), "other keeper wallet should initialize");
        True(!f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, otherKeeper.Id, 102,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out _), "payment must target the keeper of this visit's building");
        True(f.Store.ReadNpcInnConsumption(f.Buyer.Id) == null, "wrong keeper must not create a fulfillment receipt");
        Equal(20, f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);
        Equal(30, f.Store.ReadNpcInventory(otherKeeper.Id).WalletGold);
    }

    private static void CounterPaymentNeedsObservedArrival()
    {
        Fixture f = CreateFixture(20, 40);
        ObservedVisit beforeArrival = CounterCheckpoint(f.Buyer.Id, f.BuildingKey, arrived: false);
        Equal(ShopVisitPhase.Walking, beforeArrival.Evaluate(102).Phase);
        Equal(0.0, beforeArrival.Evaluate(102).Position.X);
        f.Store.StoreObservedVisit(beforeArrival);
        True(!f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out _), "remaining 0.1m route must not purchase before the follower reports arrival");

        ObservedVisit afterArrival = CounterCheckpoint(f.Buyer.Id, f.BuildingKey, arrived: true);
        Equal(ShopVisitPhase.Haggling, afterArrival.Evaluate(102).Phase);
        Equal(0.1, afterArrival.Evaluate(102).Position.X);
        f.Store.StoreObservedVisit(afterArrival);
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out NpcInnConsumptionReceipt receipt),
            "zero-length current counter leg after observed arrival should permit purchase");
        Equal(NpcInnConsumptionOutcome.Purchased, receipt.Outcome);
    }

    private static ObservedVisit CounterCheckpoint(string residentId, int buildingKey, bool arrived)
    {
        GuardPoint3 counter = Point(0.1, 0);
        return new ObservedVisit
        {
            ResidentId = residentId,
            SettlementId = 1,
            BuildingKey = buildingKey,
            ObservedAt = 102,
            PlannedEndAt = 99,
            ResumeUntil = 99,
            SecondsPerMetre = 1,
            Legs = new List<ObservedVisitLeg>
            {
                new ObservedVisitLeg
                {
                    Route = arrived ? new List<GuardPoint3> { Point(0.1, 0) } : new List<GuardPoint3> { Point(0, 0), Point(0.1, 0) },
                    DwellSeconds = 60,
                    AfterPhase = ShopVisitPhase.Haggling,
                },
                new ObservedVisitLeg
                {
                    Route = new List<GuardPoint3> { Point(0.1, 0), Point(1, 0) },
                    DwellSeconds = 60,
                    AfterPhase = ShopVisitPhase.Browsing,
                },
                new ObservedVisitLeg
                {
                    Route = new List<GuardPoint3> { Point(1, 0) },
                    AfterPhase = ShopVisitPhase.Complete,
                },
            },
        };
    }

    private static void NewVisitHasFreshReceipt()
    {
        Fixture f = CreateFixture(20, 40);
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out NpcInnConsumptionReceipt first), "first purchase should succeed");
        ObservedVisit next = CreateVisit(f.Buyer.Id, f.BuildingKey, 300, 500);
        f.Store.StoreObservedVisit(next);
        True(f.Store.ReadNpcInnConsumption(f.Buyer.Id) == null, "different planned visit must not inherit old receipt");
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 302,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out NpcInnConsumptionReceipt second), "next visit should allow a new purchase");
        True(first.VisitId != second.VisitId, "visit identifiers should differ");
        Equal(14, f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);
        Equal(40, f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);
    }

    private static void SettledVisitSurvivesRemoval()
    {
        Fixture f = CreateFixture(20, 40);
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionKind.Drink, 1, "Beer", 5, out NpcInnConsumptionReceipt first), "first sale should settle");
        True(f.Store.RemoveObservedVisit(f.Buyer.Id), "visit removal should succeed");
        NpcInventoryState buyer = f.Store.ReadNpcInventory(f.Buyer.Id);
        NpcInventoryState keeper = f.Store.ReadNpcInventory(f.Keeper.Id);
        True(f.Store.CommitNpcInventoryTransaction("unrelated-inn-wallet-update", new[]
        {
            new NpcInventoryUpdate { PersonId = f.Buyer.Id, ExpectedRevision = buyer.Revision,
                WalletGold = buyer.WalletGold, Items = buyer.Items },
            new NpcInventoryUpdate { PersonId = f.Keeper.Id, ExpectedRevision = keeper.Revision,
                WalletGold = keeper.WalletGold, Items = keeper.Items },
        }), "unrelated transaction should supersede LastTransactionId on both wallets");
        WorldState saved = JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(f.Store.Snapshot(), Json), Json);
        WorldStateStore restored = new WorldStateStore(0);
        restored.Restore(saved);
        restored.StoreObservedVisit(CreateVisit(f.Buyer.Id, f.BuildingKey, 100, 200));
        True(restored.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 103,
            NpcInnConsumptionKind.Drink, 1, "Beer", 5, out NpcInnConsumptionReceipt replay),
            "exact retry should return prior authoritative receipt");
        Equal(first.VisitId, replay.VisitId);
        Equal(first.CompletedAt, replay.CompletedAt);
        Equal(15, restored.ReadNpcInventory(f.Buyer.Id).WalletGold);
        Equal(40, restored.ReadNpcInventory(f.Keeper.Id).WalletGold);
    }

    private static void ReplayedTermsAndVisitFrontier()
    {
        Fixture f = CreateFixture(20, 40);
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionKind.Drink, 1, "Beer", 5, out _), "first sale should settle");
        True(f.Store.RemoveObservedVisit(f.Buyer.Id), "visit removal should succeed");
        f.Store.StoreObservedVisit(CreateVisit(f.Buyer.Id, f.BuildingKey, 100, 200));
        True(!f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 103,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out _), "changed offer must not reuse prior success");
        True(!f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 103,
            NpcInnConsumptionKind.Drink, 1, "Beer", 4, out _), "changed price must not reuse prior success");
        f.Store.StoreObservedVisit(CreateVisit(f.Buyer.Id, f.BuildingKey, 100, 199));
        True(!f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 103,
            NpcInnConsumptionKind.Drink, 1, "Beer", 5, out _), "earlier-ending visit must not charge after frontier");
        f.Store.StoreObservedVisit(CreateVisit(f.Buyer.Id, f.BuildingKey, 300, 500));
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 302,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out _), "later visit should buy normally");
        Equal(12, f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);
        Equal(40, f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);
    }

    private static void RetiredVisitRecordsUnavailable()
    {
        Fixture f = CreateFixture(20, 40);
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionKind.Drink, 1, "Beer", 5, out _), "first sale should settle");
        f.Store.StoreObservedVisit(CreateVisit(f.Buyer.Id, f.BuildingKey, 100, 199));
        True(f.Store.IsNpcInnConsumptionRetired(f.Buyer.Id), "older visit should be retired");
        True(f.Store.TryRecordNpcInnNoPurchase(f.Buyer.Id, null, 103,
            NpcInnConsumptionOutcome.Unavailable, "visit superseded by a later inn settlement",
            out NpcInnConsumptionReceipt receipt), "retired visit should record truthful non-sale");
        Equal(NpcInnConsumptionOutcome.Unavailable, receipt.Outcome);
        Equal(0, receipt.ChargedGold);
        True(!receipt.FoodFulfilled && !receipt.DrinkFulfilled, "retirement cannot fulfill service");
        WorldStateStore restored = new WorldStateStore(0);
        restored.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(f.Store.Snapshot(), Json), Json));
        Equal(NpcInnConsumptionOutcome.Unavailable, restored.ReadNpcInnConsumption(f.Buyer.Id).Outcome);
        restored.StoreObservedVisit(CreateVisit(f.Buyer.Id, f.BuildingKey, 300, 500));
        True(restored.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 302,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out _), "later visit should still buy");
        Equal(12, restored.ReadNpcInventory(f.Buyer.Id).WalletGold);
    }

    private static void LegacyActiveReceiptMigration()
    {
        Fixture f = CreateFixture(20, 40);
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionKind.Drink, 1, "Beer", 5, out NpcInnConsumptionReceipt first),
            "legacy fixture should contain one active receipt");
        WorldState legacy = f.Store.Snapshot();
        legacy.Version = 44;
        // This fixture has no experiences; serialize the inbox shape supported by schema 44.
        legacy.NpcExperienceInbox = new NpcExperienceInbox { Version = 3 };
        legacy.NpcInnConsumptionCursors = null;
        WorldStateStore migrated = new WorldStateStore(0);
        migrated.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(legacy, Json), Json));
        Equal(WorldState.CurrentVersion, migrated.Snapshot().Version);
        True(migrated.RemoveObservedVisit(f.Buyer.Id), "migrated visit removal should succeed");
        migrated.StoreObservedVisit(CreateVisit(f.Buyer.Id, f.BuildingKey, 100, 200));
        True(migrated.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 103,
            NpcInnConsumptionKind.Drink, 1, "Beer", 5, out NpcInnConsumptionReceipt replay),
            "migrated cursor should identify exact replay");
        Equal(first.CompletedAt, replay.CompletedAt);
        Equal(15, migrated.ReadNpcInventory(f.Buyer.Id).WalletGold);
    }

    private static void FailedVisitSurvivesRemoval()
    {
        Fixture f = CreateFixture(0, 40);
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 102,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out NpcInnConsumptionReceipt first),
            "no-funds attempt should settle");
        True(f.Store.RemoveObservedVisit(f.Buyer.Id), "visit removal should succeed");
        WorldStateStore restored = new WorldStateStore(0);
        restored.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(f.Store.Snapshot(), Json), Json));
        restored.StoreObservedVisit(CreateVisit(f.Buyer.Id, f.BuildingKey, 100, 200));
        True(restored.TryBuyNpcInnConsumption(f.Buyer.Id, f.Keeper.Id, 103,
            NpcInnConsumptionKind.Food, 4, "Bread", 3, out NpcInnConsumptionReceipt replay),
            "same failed quote should return prior failure");
        Equal(first.CompletedAt, replay.CompletedAt);
        Equal(NpcInnConsumptionOutcome.InsufficientFunds, replay.Outcome);
        Equal(0, restored.ReadNpcInventory(f.Buyer.Id).WalletGold);
        Equal(40, restored.ReadNpcInventory(f.Keeper.Id).WalletGold);
    }

    private static void FractionalCampaignTimeNativeCodec()
    {
        Fixture f=CreateFixture(20,40);
        double observedAt=12597570000d;
        f.Store.StoreObservedVisit(CreateVisit(f.Buyer.Id,f.BuildingKey,observedAt,observedAt+200.1234567));
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id,f.Keeper.Id,observedAt+2,
            NpcInnConsumptionKind.Drink,1,"Beer",5,out var receipt),"campaign-time purchase should settle");
        var serializer=new fsSerializer();fsData wire;
        serializer.TrySerialize(typeof(WorldState),f.Store.Snapshot(),out wire).AssertSuccessWithoutWarnings();
        var json=fsJsonPrinter.PrettyJson(wire);object decoded=null;
        serializer.TryDeserialize(fsJsonParser.Parse(json),typeof(WorldState),ref decoded).AssertSuccessWithoutWarnings();
        var state=(WorldState)decoded;var visit=state.ObservedVisits.Find(v=>v.ResidentId==f.Buyer.Id);
        Equal(receipt.VisitId,NpcInnConsumption.VisitId(visit));
        var restored=new WorldStateStore(0);restored.Restore(state);
        Equal(15,restored.ReadNpcInventory(f.Buyer.Id).WalletGold);
        True(!restored.TryBuyNpcInnConsumption(f.Buyer.Id,f.Keeper.Id,observedAt+3,
            NpcInnConsumptionKind.Drink,1,"Beer",5,out var ignored),"native payment cannot replay");
    }

    private static void NativeCampaignTimePrecision()
    {
        Fixture f=CreateFixture(20,40);
        double start=12597584430d,end=12597590223.563303;
        f.Store.StoreObservedVisit(CreateVisit(f.Buyer.Id,f.BuildingKey,start,end));
        True(f.Store.TryBuyNpcInnConsumption(f.Buyer.Id,f.Keeper.Id,start+2,
            NpcInnConsumptionKind.Drink,1,"Beer",5,out var receipt),"purchase should settle");
        // Unity/Mono's FullSerializer prints these doubles with fifteen digits.
        // The same DLL under .NET8 otherwise uses the host's longer formatting.
        double savedEnd=double.Parse(end.ToString("G15",System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture);
        var saved=f.Store.Snapshot();
        saved.ObservedVisits.Find(v=>v.ResidentId==f.Buyer.Id).PlannedEndAt=savedEnd;
        saved.NpcInnConsumptionCursors.Find(v=>v.BuyerId==f.Buyer.Id).PlannedEndAt=savedEnd;
        var restored=new WorldStateStore(0);restored.Restore(saved);
        Equal(receipt.VisitId,NpcInnConsumption.VisitId(restored.Snapshot().ObservedVisits.Find(v=>v.ResidentId==f.Buyer.Id)));
        Equal(15,restored.ReadNpcInventory(f.Buyer.Id).WalletGold);
        True(!restored.TryBuyNpcInnConsumption(f.Buyer.Id,f.Keeper.Id,start+3,
            NpcInnConsumptionKind.Drink,1,"Beer",5,out _),"native time rounding must not permit another charge");
    }

    private static Fixture CreateFixture(int buyerGold, int keeperGold)
    {
        var store = new WorldStateStore(17);
        SettlementPopulation town = store.GetOrCreate(1, new[] { 10, 20 }, 1);
        Resident buyer = town.Households[0].Residents[0];
        int destination = town.Households.Find(home => home.BuildingKey != buyer.HomeKey).BuildingKey;
        store.SetRoutines(new TownRoutineState
        {
            SettlementId = 1,
            Residents = new List<ResidentRoutine>
            {
                new ResidentRoutine
                {
                    ResidentId = buyer.Id,
                    DestinationKey = destination,
                    OpenMinute = 500,
                    CloseMinute = 600,
                    Route = new List<SavedRoutePoint> { Saved(0, 0), Saved(1, 0) },
                },
            },
        });
        NativePerson keeper = store.RegisterNativePerson(1, destination, 900, "Inn keeper",
            ResidentSex.Female, 0, 0, 182, 0);
        True(store.InitializeNpcInventory(buyer.Id, buyerGold, Array.Empty<NpcInventoryItem>()), "buyer wallet should initialize");
        True(store.InitializeNpcInventory(keeper.Id, keeperGold, Array.Empty<NpcInventoryItem>()), "keeper wallet should initialize");
        store.StoreObservedVisit(CreateVisit(buyer.Id, destination, 100, 200));
        return new Fixture { Store = store, Buyer = buyer, Keeper = keeper, BuildingKey = destination };
    }

    private static ObservedVisit CreateVisit(string residentId, int buildingKey, double observedAt, double plannedEndAt)
    {
        return new ObservedVisit
        {
            ResidentId = residentId,
            SettlementId = 1,
            BuildingKey = buildingKey,
            ObservedAt = observedAt,
            PlannedEndAt = plannedEndAt,
            ResumeUntil = plannedEndAt,
            SecondsPerMetre = 1,
            Legs = new List<ObservedVisitLeg>
            {
                new ObservedVisitLeg
                {
                    Route = new List<GuardPoint3> { Point(0, 0), Point(1, 0) },
                    DwellSeconds = 60,
                    AfterPhase = ShopVisitPhase.Haggling,
                },
                new ObservedVisitLeg
                {
                    Route = new List<GuardPoint3> { Point(1, 0), Point(2, 0) },
                    DwellSeconds = 60,
                    AfterPhase = ShopVisitPhase.Browsing,
                },
                new ObservedVisitLeg
                {
                    Route = new List<GuardPoint3> { Point(2, 0) },
                    AfterPhase = ShopVisitPhase.Complete,
                },
            },
        };
    }

    private static SavedRoutePoint Saved(double x, double z) { return new SavedRoutePoint { X = x, Z = z }; }
    private static GuardPoint3 Point(double x, double z) { return new GuardPoint3 { X = x, Z = z }; }
    private static void Run(string name, Action body)
    {
        try { body(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }

    private sealed class Fixture
    {
        public WorldStateStore Store;
        public Resident Buyer;
        public NativePerson Keeper;
        public int BuildingKey;
    }
}
