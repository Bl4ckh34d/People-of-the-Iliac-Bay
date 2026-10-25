using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var tests = new List<(string, Action)>
        {
            ("known theft stays distinct from unknown or purchased native ownership", KnownTheftQuery),
            ("unknown player item fails closed", Unknown),
            ("exact native NPC gift stores legal source only", Gift),
            ("merge and mismatched transaction deny source", MergeAndMismatch),
            ("source receipt replay and duplicate UID deny", Replay),
            ("theft taints exact UID and cannot be cleansed by replay", Theft),
            ("saved ledger retains scope and rejects foreign branch", Restore),
            ("historical legal source alone cannot prove current clean ownership", CurrentOwnership),
            ("world snapshot retains source, old saves migrate empty, foreign scope rejects", WorldSave),
            ("store projection requires executing exact transfer and committed source", StoreProjection),
            ("paid native shop item has exact historical source only", NativePurchase),
            ("native shop theft taints exact UID and cannot certify it", NativeShopTheft),
            ("shop receipt survives world save and rejects replay", ShopWorldSave)
        };
        int failed = 0;
        foreach (var test in tests)
        {
            try { test.Item2(); Console.WriteLine("PASS " + test.Item1); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Item1 + ": " + ex.Message); }
        }
        Console.WriteLine((tests.Count - failed) + "/" + tests.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private const string Scope = "0123456789abcdef0123456789abcdef";
    private static PlayerItemProvenanceState Empty() { return PlayerItemProvenanceLedger.Create(Scope); }
    private static PlayerItemProvenanceGiftReceipt Receipt()
    { return new PlayerItemProvenanceGiftReceipt { ActionId = "action:1", TransactionId = "action:1:transfer",
        SourceOwnerId = "npc:1", SourceItemUid = 31, PlayerItemUid = 41, Quantity = 1,
        SourceQuantityBefore = 1, SourceQuantityAfter = 0, PlayerQuantityBefore = 0,
        PlayerQuantityAfter = 1, SourceInventoryRevisionBefore = 3,
        SourceInventoryRevisionAfter = 4, NativeTransferObserved = true }; }
    private static PlayerItemProvenanceState Record(PlayerItemProvenanceState state,
        PlayerItemProvenanceGiftReceipt receipt)
    { PlayerItemProvenanceState updated;
      True(PlayerItemProvenanceLedger.TryRecordNpcGift(state, Scope, receipt, out updated), "gift refused");
      return updated; }

    private static void Unknown()
    { False(PlayerItemProvenanceLedger.HasLegalSource(Empty(), Scope, 41), "unknown UID clean"); }

    private static void Gift()
    { var state = Record(Empty(), Receipt());
      True(PlayerItemProvenanceLedger.HasLegalSource(state, Scope, 41), "exact gift source absent");
      False(PlayerItemProvenanceLedger.HasLegalSource(state, Scope, 31), "source UID became player item"); }

    private static void MergeAndMismatch()
    { var receipt = Receipt(); PlayerItemProvenanceState updated;
      receipt.PlayerQuantityBefore = 2;
      False(PlayerItemProvenanceLedger.TryRecordNpcGift(Empty(), Scope, receipt, out updated), "merged stack clean");
      receipt = Receipt(); receipt.TransactionId = "another:transfer";
      False(PlayerItemProvenanceLedger.TryRecordNpcGift(Empty(), Scope, receipt, out updated), "wrong transaction clean");
      receipt = Receipt(); receipt.SourceQuantityAfter = 1;
      False(PlayerItemProvenanceLedger.TryRecordNpcGift(Empty(), Scope, receipt, out updated), "missing source debit clean"); }

    private static void Replay()
    { var state = Record(Empty(), Receipt()); PlayerItemProvenanceState updated;
      False(PlayerItemProvenanceLedger.TryRecordNpcGift(state, Scope, Receipt(), out updated), "same receipt replayed");
      var receipt = Receipt(); receipt.ActionId = "action:2"; receipt.TransactionId = "action:2:transfer";
      False(PlayerItemProvenanceLedger.TryRecordNpcGift(state, Scope, receipt, out updated), "same player UID got new source"); }

    private static void Theft()
    { var state = Record(Empty(), Receipt()); PlayerItemProvenanceState stolen;
      True(PlayerItemProvenanceLedger.TryRecordTheft(state, Scope, 41, "native-theft:1", out stolen), "theft mark refused");
      False(PlayerItemProvenanceLedger.HasLegalSource(stolen, Scope, 41), "stolen item clean");
      PlayerItemProvenanceState replay;
      False(PlayerItemProvenanceLedger.TryRecordTheft(stolen, Scope, 41, "native-theft:1", out replay), "theft replay accepted"); }

    private static void Restore()
    { var state = Record(Empty(), Receipt()); var saved = PlayerItemProvenanceLedger.CopyValidated(state, Scope);
      True(PlayerItemProvenanceLedger.HasLegalSource(saved, Scope, 41), "saved gift lost");
      False(PlayerItemProvenanceLedger.HasLegalSource(saved, "fedcba9876543210fedcba9876543210", 41), "foreign branch clean");
      saved.Entries[0].SourceReceiptId = "tampered";
      Throws(() => PlayerItemProvenanceLedger.CopyValidated(saved, Scope), "tampered source accepted"); }

    private static void CurrentOwnership()
    { var state = Record(Empty(), Receipt());
      False(PlayerItemProvenanceLedger.KnownCleanProvenanceNow(state, Scope, 41, true, false),
          "historical source bypassed ownership continuity");
      True(PlayerItemProvenanceLedger.KnownCleanProvenanceNow(state, Scope, 41, true, true),
          "verified continuous ownership refused");
      False(PlayerItemProvenanceLedger.KnownCleanProvenanceNow(state, Scope, 41, false, true),
          "missing player item clean"); }

    private static void WorldSave()
    {
        var store = new WorldStateStore(27);
        var saved = store.Snapshot();
        var gift = new PlayerItemProvenanceGiftReceipt {
            ActionId = "action:1", TransactionId = "action:1:transfer", SourceOwnerId = "npc:1",
            SourceItemUid = 31, PlayerItemUid = 41, Quantity = 1,
            SourceQuantityBefore = 1, SourceQuantityAfter = 0,
            PlayerQuantityBefore = 0, PlayerQuantityAfter = 1,
            SourceInventoryRevisionBefore = 3, SourceInventoryRevisionAfter = 4,
            NativeTransferObserved = true };
        PlayerItemProvenanceState recorded;
        True(PlayerItemProvenanceLedger.TryRecordNpcGift(saved.PlayerItemProvenance,
            saved.NpcFrameworkScopeId, gift, out recorded), "world-scope gift refused");
        saved.PlayerItemProvenance = recorded;
        store.Restore(saved);
        True(store.HasLegalPlayerItemSource(41), "source lost on restore");
        var old = store.Snapshot(); old.Version = 55;
        store.Restore(old);
        False(store.HasLegalPlayerItemSource(41), "old save invented new provenance");
        var foreign = store.Snapshot(); foreign.PlayerItemProvenance.SaveScopeId = Scope;
        Throws(() => store.Restore(foreign), "foreign scope restored");
    }

    private static void StoreProjection()
    {
        var store = new WorldStateStore(27);
        var town = store.GetOrCreate(100, new[] { 10 });
        var actor = town.Households[0].Residents[0];
        var state = store.Snapshot();
        state.NpcInventories.Add(new NpcInventoryState { PersonId = actor.Id, Revision = 4,
            LastTransactionId = "action:1:transfer", WalletGold = 0 });
        state.NpcActions.Add(new NpcActionJob { Id = "action:1", ProposalId = "proposal:1",
            ResidentId = actor.Id, Kind = NpcActionKind.Transfer, Phase = NpcActionPhase.Executing,
            Revision = 3, AcceptedAt = 1, UpdatedAt = 2, DeadlineAt = 100,
            ExpectedInventoryRevision = 3,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player",
                ItemId = "31", Quantity = 1 } });
        store.Restore(state);
        var receipt = Receipt(); receipt.SourceOwnerId = actor.Id;
        True(store.TryRecordObservedNpcGiftPlayerItem(receipt), "exact store projection refused");
        True(store.HasLegalPlayerItemSource(41), "store source absent");
        False(store.TryRecordObservedNpcGiftPlayerItem(receipt), "store replay accepted");
        receipt = Receipt(); receipt.SourceOwnerId = actor.Id; receipt.SourceItemUid = 32;
        False(store.TryRecordObservedNpcGiftPlayerItem(receipt), "different source item accepted");
    }

    private static PlayerItemProvenanceNativeShopReceipt ShopReceipt(bool stolen = false)
    { return new PlayerItemProvenanceNativeShopReceipt {
        ReceiptId = stolen ? "native-shop-theft:41" : "native-shop-buy:41",
        ShopId = "native-shop:100:200", PlayerItemUid = 41, Quantity = 2,
        PlayerQuantityBefore = 0, PlayerQuantityAfter = 2,
        BasketQuantityBefore = 2, BasketQuantityAfter = 0,
        Price = stolen ? 0 : 25, PlayerGoldBefore = 40,
        PlayerGoldAfter = stolen ? 40 : 15, NativeTransactionObserved = true }; }

    private static void KnownTheftQuery()
    {
        var method = typeof(WorldStateStore).GetMethod("IsKnownStolenPlayerItem");
        True(method != null, "Missing known-theft query for native personal trade.");
        var unknown = new WorldStateStore(27);
        False((bool)method.Invoke(unknown, new object[] { 41UL }), "Unknown item was labeled stolen.");
        True(unknown.TryRecordObservedNativeShopItem(ShopReceipt(), false), "Paid native source rejected.");
        False((bool)method.Invoke(unknown, new object[] { 41UL }), "Purchased item was labeled stolen.");
        var stolen = new WorldStateStore(27);
        True(stolen.TryRecordObservedNativeShopItem(ShopReceipt(true), true), "Native theft source rejected.");
        True((bool)method.Invoke(stolen, new object[] { 41UL }), "Known theft was ignored.");
        stolen.Restore(stolen.Snapshot());
        True((bool)method.Invoke(stolen, new object[] { 41UL }), "Known theft disappeared after restore.");
    }

    private static void NativePurchase()
    {
        PlayerItemProvenanceState updated;
        var receipt = ShopReceipt();
        True(PlayerItemProvenanceLedger.TryRecordNativeShopPurchase(Empty(), Scope, receipt, out updated),
            "paid native purchase rejected");
        True(PlayerItemProvenanceLedger.HasLegalSource(updated, Scope, 41), "purchase source missing");
        False(PlayerItemProvenanceLedger.KnownCleanProvenanceNow(updated, Scope, 41, true, false),
            "historical purchase bypassed continuity");
        False(PlayerItemProvenanceLedger.TryRecordNativeShopPurchase(updated, Scope, receipt, out _),
            "purchase replay accepted");
        receipt = ShopReceipt(); receipt.PlayerGoldAfter = 16;
        False(PlayerItemProvenanceLedger.TryRecordNativeShopPurchase(Empty(), Scope, receipt, out _),
            "underpaid purchase certified");
        receipt = ShopReceipt(); receipt.PlayerQuantityBefore = 1;
        False(PlayerItemProvenanceLedger.TryRecordNativeShopPurchase(Empty(), Scope, receipt, out _),
            "merged purchase certified");
        True(PlayerItemProvenanceLedger.HasLegalSource(PlayerItemProvenanceLedger.CopyValidated(updated, Scope), Scope, 41),
            "purchase lost on save");
    }

    private static void NativeShopTheft()
    {
        PlayerItemProvenanceState stolen;
        True(PlayerItemProvenanceLedger.TryRecordNativeShopTheft(Empty(), Scope, ShopReceipt(true), out stolen),
            "exact native theft rejected");
        False(PlayerItemProvenanceLedger.HasLegalSource(stolen, Scope, 41), "stolen UID certified");
        False(PlayerItemProvenanceLedger.TryRecordNativeShopPurchase(stolen, Scope, ShopReceipt(), out _),
            "purchase cleansed stolen UID");
        False(PlayerItemProvenanceLedger.TryRecordNativeShopTheft(stolen, Scope, ShopReceipt(true), out _),
            "theft replay accepted");
        True(PlayerItemProvenanceLedger.TryRecordNativeShopTheft(Record(Empty(), Receipt()), Scope,
            new PlayerItemProvenanceNativeShopReceipt { ReceiptId="native-shop-theft:41", ShopId="native-shop:100:200",
                PlayerItemUid=41, Quantity=2, PlayerQuantityBefore=1, PlayerQuantityAfter=3,
                BasketQuantityBefore=2, BasketQuantityAfter=0, Price=0, PlayerGoldBefore=40,
                PlayerGoldAfter=40, NativeTransactionObserved=true }, out stolen), "theft merge failed to taint");
        False(PlayerItemProvenanceLedger.HasLegalSource(stolen, Scope, 41), "merged stolen item certified");
    }

    private static void ShopWorldSave()
    {
        var store = new WorldStateStore(27);
        True(store.TryRecordObservedNativeShopItem(ShopReceipt(), false), "shop store rejected receipt");
        var snapshot = store.Snapshot();
        store.Restore(snapshot);
        True(store.HasLegalPlayerItemSource(41), "paid source lost after save");
        False(store.IsKnownCleanPlayerItemNow(41, true, false), "history became current-clean");
        False(store.TryRecordObservedNativeShopItem(ShopReceipt(), false), "shop replay accepted");
        True(store.TryRecordObservedNativeShopItem(ShopReceipt(true), true), "observed theft did not taint");
        False(store.HasLegalPlayerItemSource(41), "theft left legal source");
        snapshot = store.Snapshot();
        store.Restore(snapshot);
        False(store.HasLegalPlayerItemSource(41), "theft taint lost on save");
    }

    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static void False(bool value, string message) { if (value) throw new Exception(message); }
    private static void Throws(Action action, string message)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception(message); }
}
