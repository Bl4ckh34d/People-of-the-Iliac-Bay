using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var tests = new List<(string, Action)>
        {
            ("all four directions and exact payment", FourDirections),
            ("proposal requires exact source and fresh revisions", ExactSourceAndRevisions),
            ("proposal rejects missing consent service capacity and funds", AdmissionGuards),
            ("observation must show exact ownership quantity and balances", ObservedEffects),
            ("observation needs fresh inventory and wallet revisions", ObservedRevisions),
            ("no invented completion and no duplicate replay", CompletionAndReplay),
            ("personal trade requires adult eligibility and willingness, not a skill gate", PersonalTrade),
            ("bound haggling prices and receipt mood are deterministic", BoundHaggling),
            ("basket haggling fingerprints every mixed-trade line", BasketHaggling),
            ("player snapshot tokens bind owner container item quantity and gold", PlayerTokens),
            ("semantic commerce selection binds only visible exact candidates", SemanticSelection),
            ("commerce offer requires separate willingness and fresh state", CommerceOffer),
            ("receipt ledger records exact effect and rejects replay", ReceiptLedger),
            ("receipt ledger restore rejects branch conflicts and damaged entries", ReceiptRestore),
            ("Mercantile tally reserves once for confirmed paid trades", MercantileReservation)
            ,("loan receipts require high known trust and return exact recorded items", LoanReceipts)
            ,("commerce store migrates legacy schemas and copies receipt state", StoreMigration)
            ,("commerce store commits inventory and receipt together", AtomicStoreCommit)
            ,("free item transfers and loan returns commit without a wallet mutation", AtomicFreeTransfers)
            ,("trust and mood change bounded bargaining without replacing native skills", PersonalBargainingContext)
        };
        int failed = 0;
        foreach (var test in tests)
        {
            try { test.Item2(); Console.WriteLine("PASS " + test.Item1); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Item1 + ": " + error.Message); }
        }
        Console.WriteLine((tests.Count - failed) + "/" + tests.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void FourDirections()
    {
        foreach (PlayerCommerceKind kind in Enum.GetValues(typeof(PlayerCommerceKind)))
        {
            var order = Order(kind);
            var state = State(order);
            True(PlayerCommerceContract.CanBegin(order, state), kind + " rejected valid state");
            var result = PlayerCommerceContract.Confirm(order, state, Observation(order, state), null, out var receipt);
            Equal(PlayerCommerceConfirmation.Committed, result);
            Equal(order.TransactionId, receipt.TransactionId);
            Equal(order.ItemUid, receipt.SourceItemUid);
        }
    }

    private static void LoanReceipts()
    {
        var give = Order(PlayerCommerceKind.Take); give.LoanPurpose = LoanPurpose.Lend;
        give.OriginReceiptId = null;
        var giveState = State(give);
        Equal(PlayerCommerceConfirmation.Committed,
            PlayerCommerceContract.Confirm(give, giveState, Observation(give, giveState), null, out var lent));
        var ledger = new PlayerCommerceReceiptLedger("branch");
        True(ledger.TryCommit("branch", lent, "loan-origin", "loan-effect", 10), "Lend receipt rejected");
        var state = ledger.Snapshot();
        True(PlayerItemLoanPolicy.TryReadOpenLoan(state, "npc-1", "loan-origin", lent.RecipientItemUid,
            lent.Quantity, out _), "Exact open loan not found");
        True(PlayerItemLoanPolicy.TryFindOpenLoan(state, "npc-1", lent.RecipientItemUid,
            lent.Quantity, out var discovered) && discovered.OriginReceiptId == "loan-origin",
            "Return UI could not discover the exact open loan receipt.");
        True(!PlayerItemLoanPolicy.AdmitsNewLoan(false, 100), "Unknown trust admitted new loan");
        True(!PlayerItemLoanPolicy.AdmitsNewLoan(true, 74), "Low trust admitted new loan");
        True(PlayerItemLoanPolicy.AdmitsNewLoan(true, 75), "High trust rejected new loan");
        var take = Order(PlayerCommerceKind.Give); take.LoanPurpose = LoanPurpose.Return;
        take.TransactionId = "loan-return-transaction";
        take.OriginReceiptId = "loan-origin"; take.ItemUid = lent.RecipientItemUid;
        take.ExpectedPlayerInventoryToken = PlayerCommerceContract.InventorySnapshotToken("player", "backpack", lent.RecipientItemUid, 2);
        var takeState = State(take); takeState.SourceQuantity = 2; takeState.ItemUid = lent.RecipientItemUid;
        takeState.PlayerInventoryToken = take.ExpectedPlayerInventoryToken;
        var returned = Observation(take, takeState); returned.RecipientItemUid = lent.RecipientItemUid;
        returned.PlayerInventoryTokenAfter = PlayerCommerceContract.InventorySnapshotToken("player", "backpack",
            lent.RecipientItemUid, 0);
        var returnConfirmation = PlayerCommerceContract.Confirm(take, takeState, returned, null, out var returnReceipt);
        Equal(PlayerCommerceConfirmation.Committed, returnConfirmation, "Return receipt confirmation");
        True(ledger.TryCommit("branch", returnReceipt, "loan-return", "return-effect", 11),
            "Exact low-trust-safe return rejected");
        True(!PlayerItemLoanPolicy.TryReadOpenLoan(ledger.Snapshot(), "npc-1", "loan-origin",
            lent.RecipientItemUid, lent.Quantity, out _), "Returned loan remains open");
        var ordinaryGift = Order(PlayerCommerceKind.Give); ordinaryGift.LoanPurpose = LoanPurpose.None;
        ordinaryGift.OriginReceiptId = null;
        True(PlayerCommerceContract.CanBegin(ordinaryGift, State(ordinaryGift)), "Ordinary free Give became a loan");
    }

    private static void ExactSourceAndRevisions()
    {
        var order = Order(PlayerCommerceKind.Buy);
        var state = State(order);
        state.SourceOwnerId = "other-npc";
        True(!PlayerCommerceContract.CanBegin(order, state), "Wrong owner admitted");
        state = State(order); state.ItemUid++;
        True(!PlayerCommerceContract.CanBegin(order, state), "Wrong UID admitted");
        state = State(order); state.SourceInventoryRevision++;
        True(!PlayerCommerceContract.CanBegin(order, state), "Stale source admitted");
        state = State(order); state.TargetInventoryRevision++;
        True(!PlayerCommerceContract.CanBegin(order, state), "Stale target admitted");
        state = State(order); state.PayerWalletRevision++;
        True(!PlayerCommerceContract.CanBegin(order, state), "Stale payer admitted");
        state = State(order); state.PayeeWalletRevision++;
        True(!PlayerCommerceContract.CanBegin(order, state), "Stale payee admitted");
        state = State(order); state.RecipientItemUid = 0;
        True(!PlayerCommerceContract.CanBegin(order, state), "Existing destination quantity lacks UID");
    }

    private static void AdmissionGuards()
    {
        var order = Order(PlayerCommerceKind.Buy);
        var state = State(order); state.PlayerConsented = false;
        True(!PlayerCommerceContract.CanBegin(order, state), "Player consent absent");
        state = State(order); state.NpcConsented = false;
        True(!PlayerCommerceContract.CanBegin(order, state), "NPC consent absent");
        state = State(order); state.ServiceAvailable = false;
        True(!PlayerCommerceContract.CanBegin(order, state), "Service unavailable");
        state = State(order); state.ServiceCode = "wrong-service";
        True(!PlayerCommerceContract.CanBegin(order, state), "Wrong service");
        state = State(order); state.PayerOwnerId = "wrong-payer";
        True(!PlayerCommerceContract.CanBegin(order, state), "Wrong payer");
        state = State(order); state.SourceAccessible = false;
        True(!PlayerCommerceContract.CanBegin(order, state), "Source inaccessible");
        state = State(order); state.ReceiverCapacity = 1;
        True(!PlayerCommerceContract.CanBegin(order, state), "Receiver cannot carry quantity");
        state = State(order); state.SourceQuantity = 1;
        True(!PlayerCommerceContract.CanBegin(order, state), "Source lacks quantity");
        state = State(order); state.PayerGold = 29;
        True(!PlayerCommerceContract.CanBegin(order, state), "Payer lacks funds");
        order.QuotedTotalGold = 0;
        True(!PlayerCommerceContract.CanBegin(order, State(order)), "Paid trade without price");
        order = Order(PlayerCommerceKind.Give); order.QuotedTotalGold = 1;
        True(!PlayerCommerceContract.CanBegin(order, State(order)), "Gift charged gold");
        order = Order(PlayerCommerceKind.Buy); order.Quantity = 0;
        True(!PlayerCommerceContract.CanBegin(order, State(order)), "Zero quantity admitted");
        order = Order(PlayerCommerceKind.Buy); order.QuotedTotalGold = 101;
        True(!PlayerCommerceContract.CanBegin(order, State(order)), "Overdraw admitted");
    }

    private static void ObservedEffects()
    {
        var order = Order(PlayerCommerceKind.Sell); var state = State(order);
        var observed = Observation(order, state); observed.SourceOwnerId = "other";
        Rejected(order, state, observed, "Wrong source owner");
        observed = Observation(order, state); observed.TargetOwnerId = "other";
        Rejected(order, state, observed, "Wrong target owner");
        observed = Observation(order, state); observed.SourceItemUid++;
        Rejected(order, state, observed, "Wrong source UID");
        observed = Observation(order, state); observed.Quantity--;
        Rejected(order, state, observed, "Partial transfer");
        observed = Observation(order, state); observed.SourceQuantityAfter++;
        Rejected(order, state, observed, "Source quantity unchanged");
        observed = Observation(order, state); observed.RecipientQuantityAfter--;
        Rejected(order, state, observed, "Recipient quantity unchanged");
        observed = Observation(order, state); observed.PayerGoldAfter++;
        Rejected(order, state, observed, "Wrong debit");
        observed = Observation(order, state); observed.PayeeGoldAfter--;
        Rejected(order, state, observed, "Wrong credit");
        observed = Observation(order, state); observed.NativeItemTransferObserved = false;
        Rejected(order, state, observed, "Transfer unobserved");
        observed = Observation(order, state); observed.NativePaymentObserved = false;
        Rejected(order, state, observed, "Payment unobserved");
    }

    private static void ObservedRevisions()
    {
        var order = Order(PlayerCommerceKind.Take); var state = State(order);
        var observed = Observation(order, state); observed.SourceInventoryRevisionAfter++;
        Rejected(order, state, observed, "Source revision mismatch");
        observed = Observation(order, state); observed.TargetInventoryRevisionAfter++;
        Rejected(order, state, observed, "Fabricated player inventory revision");
        observed = Observation(order, state); observed.PayerWalletRevisionAfter++;
        Rejected(order, state, observed, "Free transfer changed payer wallet");
        observed = Observation(order, state); observed.PayeeWalletRevisionAfter++;
        Rejected(order, state, observed, "Fabricated player wallet revision");
        order = Order(PlayerCommerceKind.Buy); state = State(order);
        observed = Observation(order, state); observed.PayerWalletRevisionAfter++;
        Rejected(order, state, observed, "Fabricated player payer revision");
        observed = Observation(order, state); observed.PayeeWalletRevisionAfter--;
        Rejected(order, state, observed, "Paid transfer lacks payee revision");
    }

    private static void PlayerTokens()
    {
        Equal("inventory|6:player|8:backpack|900|1",
            PlayerCommerceContract.InventorySnapshotToken("player", "backpack", 900, 1));
        Equal("wallet|6:player|11:gold-pieces|100",
            PlayerCommerceContract.WalletSnapshotToken("player", "gold-pieces", 100));
        var order = Order(PlayerCommerceKind.Buy); var state = State(order);
        state.PlayerInventoryToken = "inventory|6:player|8:backpack|900|2";
        True(!PlayerCommerceContract.CanBegin(order, state), "Stale player inventory token admitted");
        state = State(order); state.PlayerWalletToken = "wallet|6:player|11:gold-pieces|99";
        True(!PlayerCommerceContract.CanBegin(order, state), "Stale player wallet token admitted");
        state = State(order); state.PlayerInventoryContainerId = "other-pack";
        True(!PlayerCommerceContract.CanBegin(order, state), "Wrong player container admitted");
        state = State(order); state.PlayerWalletContainerId = "other-wallet";
        True(!PlayerCommerceContract.CanBegin(order, state), "Wrong wallet container admitted");
        state = State(order);
        var observed = Observation(order, state);
        observed.PlayerInventoryTokenAfter = order.ExpectedPlayerInventoryToken;
        Rejected(order, state, observed, "Unchanged player inventory token");
        observed = Observation(order, state);
        observed.PlayerInventoryTokenAfter = "inventory|6:player|8:backpack|701|3";
        Rejected(order, state, observed, "Wrong player item UID token");
        observed = Observation(order, state);
        observed.PlayerInventoryTokenAfter = "inventory|5:other|8:backpack|900|3";
        Rejected(order, state, observed, "Wrong player owner token");
        observed = Observation(order, state);
        observed.PlayerWalletTokenAfter = order.ExpectedPlayerWalletToken;
        Rejected(order, state, observed, "Unchanged paid wallet token");
        observed = Observation(order, state);
        observed.PlayerWalletTokenAfter = "wallet|6:player|11:gold-pieces|71";
        Rejected(order, state, observed, "Wrong player gold token");
        observed = Observation(order, state);
        observed.PlayerWalletTokenAfter = "wallet|6:player|12:other-wallet|70";
        Rejected(order, state, observed, "Wrong wallet identity token");
        Equal(PlayerCommerceConfirmation.Committed,
            PlayerCommerceContract.Confirm(order, state, Observation(order, state), null, out var receipt));
        observed = Observation(order, state); observed.PlayerInventoryTokenAfter = "wrong";
        Equal(PlayerCommerceConfirmation.Rejected,
            PlayerCommerceContract.Confirm(order, state, observed, receipt, out _));
    }

    private static void SemanticSelection()
    {
        var order = Order(PlayerCommerceKind.Buy);
        var state = State(order);
        state.NpcWilling = false; state.NpcConsented = false;
        var candidates = new[] { new PlayerCommerceCandidate { Id = 7, Order = order,
            State = state, ItemLabel = "iron sword", VisibleToPlayer = true } };
        True(PlayerCommerceSelection.TryBind("DIRECT|Buy|7|2|30", candidates, out var selected),
            "Semantic exact choice rejected before willingness");
        True(selected != null, "Selection missing");
        var selectedCopy = selected.SelectedOrder;
        True(selectedCopy.ItemUid == order.ItemUid && !ReferenceEquals(selectedCopy, order),
            "Selection did not expose detached exact order evidence");
        selectedCopy.Quantity = 999;
        True(selected.SelectedOrder.Quantity == order.Quantity,
            "Selection caller could mutate the bound order");
        string prompt = PlayerCommerceSelection.Prompt("May I buy that sword?", candidates);
        True(prompt != null && !prompt.Contains("uid=") &&
            !prompt.Contains(order.ItemUid.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "Model prompt exposed a native item UID");
        True(!PlayerCommerceSelection.TryBind("DIRECT|Sell|7|2|30", candidates, out _),
            "Wrong direction admitted");
        True(!PlayerCommerceSelection.TryBind("DIRECT|Buy|7|1|30", candidates, out _),
            "Different quantity admitted");
        True(!PlayerCommerceSelection.TryBind("DIRECT|Buy|7|2|31", candidates, out _),
            "Different quote admitted");
        True(!PlayerCommerceSelection.TryBind("DIRECT|Buy|8|2|30", candidates, out _),
            "Unknown candidate admitted");
        True(!PlayerCommerceSelection.TryBind("NEGATED", candidates, out _),
            "Negated request admitted");
        candidates[0].VisibleToPlayer = false;
        True(!PlayerCommerceSelection.TryBind("DIRECT|Buy|7|2|30", candidates, out _),
            "Invisible item admitted");
        candidates[0].VisibleToPlayer = true;
        var duplicate = new[] { candidates[0], new PlayerCommerceCandidate { Id = 7,
            Order = Order(PlayerCommerceKind.Sell), State = State(Order(PlayerCommerceKind.Sell)),
            ItemLabel = "other", VisibleToPlayer = true } };
        True(!PlayerCommerceSelection.TryBind("DIRECT|Buy|7|2|30", duplicate, out _),
            "Duplicate ID admitted");
        foreach (PlayerCommerceKind kind in Enum.GetValues(typeof(PlayerCommerceKind)))
        {
            var exact = Order(kind);
            var visible = new[] { new PlayerCommerceCandidate { Id = 9, Order = exact,
                State = State(exact), ItemLabel = "iron sword", VisibleToPlayer = true } };
            string output = "DIRECT|" + kind + "|9|2|" + exact.QuotedTotalGold;
            True(PlayerCommerceSelection.TryBind(output, visible, out _),
                "Semantic " + kind + " candidate rejected");
        }
    }

    private static void CommerceOffer()
    {
        var order = Order(PlayerCommerceKind.Sell); order.Venue = PlayerCommerceVenue.Personal;
        order.ServiceCode = null;
        var state = State(order); state.PlayerMercantile = 120;
        state.NpcWilling = false; state.NpcConsented = false;
        var candidates = new[] { new PlayerCommerceCandidate { Id = 2, Order = order,
            State = state, ItemLabel = "iron sword", VisibleToPlayer = true } };
        True(PlayerCommerceSelection.TryBind("DIRECT|Sell|2|2|30", candidates, out var selected),
            "Factual candidate should be selectable without NPC willingness");
        True(!PlayerCommerceSelection.TryPrepareOffer(selected, state, false, true, 30, out _),
            "No willingness admitted");
        state = State(order); state.PlayerMercantile = 120;
        True(!PlayerCommerceSelection.TryPrepareOffer(selected, state, false, true, 30, out _),
            "Explicit unwillingness admitted");
        state = State(order); state.PlayerMercantile = 120; state.PlayerInventoryToken = "stale";
        True(!PlayerCommerceSelection.TryPrepareOffer(selected, state, true, true, 30, out _),
            "Stale inventory admitted");
        state = State(order); state.PlayerMercantile = 120;
        True(!PlayerCommerceSelection.TryPrepareOffer(selected, state, true, false, 30, out _),
            "Invisible current item admitted");
        True(!PlayerCommerceSelection.TryPrepareOffer(selected, state, true, true, 31, out _),
            "Repriced item admitted");
        True(PlayerCommerceSelection.TryPrepareOffer(selected, state, true, true, 30, out var prepared),
            "Current willingness and native state rejected");
        Equal(701UL, prepared.ItemUid);
        Equal(2, prepared.Quantity);
        Equal(30, prepared.QuotedTotalGold);
        order.ItemUid = 999; order.QuotedTotalGold = 999;
        Equal(701UL, prepared.ItemUid, "Candidate mutation changed prepared UID");
        Equal(30, prepared.QuotedTotalGold, "Candidate mutation changed prepared price");
    }

    private static void ReceiptLedger()
    {
        var ledger = new PlayerCommerceReceiptLedger("branch-a");
        var receipt = ConfirmedReceipt(PlayerCommerceKind.Buy);
        True(ledger.TryCommit("branch-a", receipt, "receipt-a", "effect-a", 120),
            "Verified purchase receipt rejected");
        var saved = ledger.Snapshot();
        Equal(PlayerCommerceReceiptLedgerState.CurrentVersion, saved.Version);
        Equal("branch-a", saved.SaveBranchId);
        Equal(1L, saved.Revision);
        Equal("commerce-1", saved.Entries[0].TransactionId);
        Equal("receipt-a", saved.Entries[0].ReceiptId);
        Equal("effect-a", saved.Entries[0].EffectId);
        Equal("npc-1", saved.Entries[0].NpcId);
        Equal(PlayerCommerceVenue.Merchant, saved.Entries[0].Venue);
        Equal(701UL, saved.Entries[0].SourceItemUid);
        Equal(900UL, saved.Entries[0].RecipientItemUid);
        Equal(2, saved.Entries[0].Quantity);
        Equal(30, saved.Entries[0].Gold);
        Equal(5L, saved.Entries[0].SourceInventoryRevisionAfter);
        Equal(0L, saved.Entries[0].TargetInventoryRevisionAfter);
        Equal(0L, saved.Entries[0].PayerWalletRevisionAfter);
        Equal(13L, saved.Entries[0].PayeeWalletRevisionAfter);
        Equal(120d, saved.Entries[0].CommittedGameTime);
        True(!ledger.TryCommit("branch-a", receipt, "receipt-a", "effect-a", 120),
            "Same transaction replay admitted");
        var second = ConfirmedReceipt(PlayerCommerceKind.Buy, "commerce-2", 1);
        True(!ledger.TryCommit("branch-a", second, "receipt-a", "effect-b", 121),
            "Receipt ID reuse admitted");
        True(!ledger.TryCommit("branch-a", second, "receipt-b", "effect-a", 121),
            "Effect ID reuse admitted");
        True(!ledger.TryCommit("other-branch", second, "receipt-b", "effect-b", 121),
            "Other branch admitted");
        True(ledger.TryCommit("branch-a", second, "receipt-b", "effect-b", 121),
            "Second distinct receipt rejected");
        saved.Entries[0].Gold = 999;
        Equal(30, ledger.Snapshot().Entries[0].Gold, "Snapshot alias changed ledger");
    }

    private static void ReceiptRestore()
    {
        var ledger = new PlayerCommerceReceiptLedger("branch-a");
        True(ledger.TryCommit("branch-a", ConfirmedReceipt(PlayerCommerceKind.Sell),
            "receipt-a", "effect-a", 120), "Initial receipt rejected");
        var saved = ledger.Snapshot();
        True(PlayerCommerceReceiptLedger.TryRestore(saved, "branch-a", out var restored),
            "Valid state rejected");
        Equal(1, restored.Snapshot().Entries.Count);
        True(!PlayerCommerceReceiptLedger.TryRestore(saved, "branch-b", out _),
            "Sibling branch restore admitted");
        var invalid = saved.Copy(); invalid.Version++;
        True(!PlayerCommerceReceiptLedger.TryRestore(invalid, "branch-a", out _),
            "Unknown schema admitted");
        invalid = saved.Copy(); invalid.Entries.Add(invalid.Entries[0].Copy()); invalid.Revision++;
        True(!PlayerCommerceReceiptLedger.TryRestore(invalid, "branch-a", out _),
            "Duplicate receipt admitted");
        invalid = saved.Copy(); invalid.Entries[0].Gold = 0;
        True(!PlayerCommerceReceiptLedger.TryRestore(invalid, "branch-a", out _),
            "Paid trade with zero gold admitted");
        invalid = saved.Copy(); invalid.Entries[0].CommittedGameTime = double.NaN;
        True(!PlayerCommerceReceiptLedger.TryRestore(invalid, "branch-a", out _),
            "Invalid committed time admitted");
    }

    private static void MercantileReservation()
    {
        var ledger = new PlayerCommerceReceiptLedger("branch-a");
        True(!ledger.TryReserveMercantileTally("branch-a", "unknown", out _),
            "Refusal without receipt reserved tally");
        True(ledger.TryCommit("branch-a", ConfirmedReceipt(PlayerCommerceKind.Give),
            "receipt-give", "effect-give", 120), "Gift receipt rejected");
        True(!ledger.TryReserveMercantileTally("branch-a", "commerce-1", out _),
            "Gift reserved Mercantile tally");
        True(ledger.TryCommit("branch-a", ConfirmedReceipt(PlayerCommerceKind.Buy, "commerce-2", 10),
            "receipt-buy", "effect-buy", 121), "Purchase receipt rejected");
        True(ledger.TryReserveMercantileTally("branch-a", "commerce-2", out var tally),
            "Confirmed purchase did not reserve tally");
        Equal("commerce-2", tally.TransactionId);
        Equal(PlayerCommerceKind.Buy, tally.Kind);
        True(!ledger.TryReserveMercantileTally("branch-a", "commerce-2", out _),
            "Mercantile tally reserved twice");
        True(PlayerCommerceReceiptLedger.TryRestore(ledger.Snapshot(), "branch-a", out var restored),
            "Reserved tally lost during restore");
        True(!restored.TryReserveMercantileTally("branch-a", "commerce-2", out _),
            "Restored tally reserved twice");
        True(ledger.TryCommit("branch-a", ConfirmedReceipt(PlayerCommerceKind.Take, "commerce-3", 20),
            "receipt-take", "effect-take", 122), "Take receipt rejected");
        True(!ledger.TryReserveMercantileTally("branch-a", "commerce-3", out _),
            "Take reserved Mercantile tally");
        True(ledger.TryCommit("branch-a", ConfirmedReceipt(PlayerCommerceKind.Sell, "commerce-4", 30),
            "receipt-sell", "effect-sell", 123), "Sale receipt rejected");
        True(ledger.TryReserveMercantileTally("branch-a", "commerce-4", out tally),
            "Confirmed sale did not reserve tally");
        Equal(PlayerCommerceKind.Sell, tally.Kind);
    }

    private static void StoreMigration()
    {
        foreach (int version in new[] { 55, 56, 57 })
        {
            var original = new WorldStateStore(42);
            var saved = original.Snapshot();
            saved.Version = version;
            saved.PlayerCommerceReceipts = null;
            if (version == 55) saved.PlayerItemProvenance = null;
            if (version < 57) saved.NpcMagicCareTasks = null;
            var restored = new WorldStateStore(42);
            restored.Restore(saved);
            var snapshot = restored.Snapshot();
            Equal(WorldState.CurrentVersion, snapshot.Version);
            Equal(snapshot.NpcFrameworkScopeId, snapshot.PlayerCommerceReceipts.SaveBranchId);
            Equal(0, snapshot.PlayerCommerceReceipts.Entries.Count);
            snapshot.PlayerCommerceReceipts.Entries.Add(new PlayerCommerceReceiptLedgerEntry());
            Equal(0, restored.Snapshot().PlayerCommerceReceipts.Entries.Count);
        }
        var store = new WorldStateStore(42);
        var invalid = store.Snapshot();
        invalid.PlayerCommerceReceipts.SaveBranchId = Guid.NewGuid().ToString("N");
        bool rejected = false;
        try { store.Restore(invalid); } catch (ArgumentException) { rejected = true; }
        True(rejected, "Sibling-branch ledger admitted");
        Equal(0, store.Snapshot().PlayerCommerceReceipts.Entries.Count);
    }

    private static void AtomicFreeTransfers()
    {
        foreach (bool loan in new[] { false, true })
        {
            var store = new WorldStateStore(42);
            string npc = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
            var item = new NpcInventoryItem { Uid = 701, NativePayload = "qty=2" };
            True(store.InitializeNpcInventory(npc, 100, loan ? new[] { item } : Array.Empty<NpcInventoryItem>()),
                "Free transfer fixture rejected");
            for (int step = 0; step < 2; step++)
            {
                bool npcSource = loan ? step == 0 : step == 1;
                var order = Order(npcSource ? PlayerCommerceKind.Take : PlayerCommerceKind.Give);
                order.TransactionId = "free-transfer-" + step;
                order.Venue = PlayerCommerceVenue.Personal;
                order.NpcId = npc;
                order.SourceOwnerId = npcSource ? npc : "player";
                order.TargetOwnerId = npcSource ? "player" : npc;
                order.ExpectedSourceInventoryRevision = npcSource ? step : 0;
                order.ExpectedTargetInventoryRevision = npcSource ? 0 : step;
                order.ExpectedPayerWalletRevision = step;
                order.ExpectedPayeeWalletRevision = 0;
                order.ExpectedPlayerInventoryToken = PlayerCommerceContract.InventorySnapshotToken("player", "backpack",
                    npcSource ? 0UL : 701UL, npcSource ? 0 : 2);
                order.LoanPurpose = !loan ? LoanPurpose.None : step == 0 ? LoanPurpose.Lend : LoanPurpose.Return;
                order.OriginReceiptId = loan && step == 1 ? "free-receipt-0" : null;
                var state = State(order);
                state.PayerOwnerId = npc;
                state.PayeeOwnerId = "player";
                state.SourceQuantity = 2;
                state.RecipientItemUid = 0;
                state.RecipientQuantity = 0;
                state.SourceInventoryRevision = order.ExpectedSourceInventoryRevision;
                state.TargetInventoryRevision = order.ExpectedTargetInventoryRevision;
                state.PayerWalletRevision = step;
                state.PayeeWalletRevision = 0;
                state.PlayerInventoryToken = order.ExpectedPlayerInventoryToken;
                var observed = Observation(order, state);
                observed.RecipientItemUid = 701;
                observed.PlayerInventoryTokenAfter = PlayerCommerceContract.InventorySnapshotToken("player", "backpack", 701,
                    npcSource ? 2 : 0);
                Equal(PlayerCommerceConfirmation.Committed,
                    PlayerCommerceContract.Confirm(order, state, observed, null, out var receipt), "Free receipt confirmation");
                var update = new NpcInventoryUpdate { PersonId = npc, ExpectedRevision = step, WalletGold = 100,
                    Items = npcSource ? new List<NpcInventoryItem>() : new List<NpcInventoryItem> { item } };
                True(store.TryCommitPlayerCommerce(update, receipt, "free-receipt-" + step, "free-effect-" + step, 120 + step),
                    "Zero-gold " + order.Kind + "/" + order.LoanPurpose + " rejected without a wallet change");
                Equal(100, store.ReadNpcInventory(npc).WalletGold);
                Equal((long)step + 1, store.ReadNpcInventory(npc).Revision);
                True(!store.TryReservePlayerCommerceMercantileTally(order.TransactionId, out _), "Free transfer trained Mercantile");
                var saved = store.Snapshot();
                var restored = new WorldStateStore(42);
                restored.Restore(saved);
                Equal(step + 1, restored.ReadPlayerCommerceReceipts().Entries.Count);
                Equal(100, restored.ReadNpcInventory(npc).WalletGold);
            }
            if (loan) True(!store.TryReadOpenPlayerItemLoan(npc, "free-receipt-0", 701, 2, out _), "Returned loan remained open");
        }
    }

    private static void AtomicStoreCommit()
    {
        var store = new WorldStateStore(42);
        var town = store.GetOrCreate(100, new[] { 10, 20 }, 1);
        string npc = town.Households[0].Residents[0].Id;
        string other = town.Households[1].Residents[0].Id;
        True(store.InitializeNpcInventory(npc, 100, new[] {
            new NpcInventoryItem { Uid = 701, NativePayload = "qty=3" }
        }), "NPC fixture inventory rejected");
        True(store.InitializeNpcInventory(other, 5, new[] {
            new NpcInventoryItem { Uid = 999, NativePayload = "foreign" }
        }), "Other NPC fixture rejected");
        var order = Order(PlayerCommerceKind.Buy);
        order.NpcId = npc; order.SourceOwnerId = npc;
        order.ExpectedSourceInventoryRevision = 0;
        order.ExpectedPayeeWalletRevision = 0;
        var before = State(order); before.PayeeOwnerId = npc;
        var observed = Observation(order, before);
        Equal(PlayerCommerceConfirmation.Committed,
            PlayerCommerceContract.Confirm(order, before, observed, null, out var receipt));
        var update = new NpcInventoryUpdate { PersonId = npc, ExpectedRevision = 0,
            WalletGold = 130, Items = new List<NpcInventoryItem> {
                new NpcInventoryItem { Uid = 701, NativePayload = "qty=1" }
            } };
        var bad = new NpcInventoryUpdate { PersonId = npc, ExpectedRevision = 0,
            WalletGold = 129, Items = update.Items };
        True(!store.TryCommitPlayerCommerce(bad, receipt, "receipt-1", "effect-1", 120),
            "Wrong NPC wallet committed");
        Equal(0, store.ReadNpcInventory(npc).Revision);
        Equal(0, store.ReadPlayerCommerceReceipts().Entries.Count);
        bad = new NpcInventoryUpdate { PersonId = npc, ExpectedRevision = 0,
            WalletGold = 130, Items = new List<NpcInventoryItem> {
                new NpcInventoryItem { Uid = 701, NativePayload = "qty=3" }
            } };
        True(!store.TryCommitPlayerCommerce(bad, receipt, "receipt-1", "effect-1", 120),
            "Unchanged NPC item committed");
        Equal(0, store.ReadPlayerCommerceReceipts().Entries.Count);
        bad = new NpcInventoryUpdate { PersonId = npc, ExpectedRevision = 0,
            WalletGold = 130, Items = new List<NpcInventoryItem> {
                new NpcInventoryItem { Uid = 701, NativePayload = "qty=1" },
                new NpcInventoryItem { Uid = 999, NativePayload = "stolen" }
            } };
        True(!store.TryCommitPlayerCommerce(bad, receipt, "receipt-1", "effect-1", 120),
            "Foreign NPC UID committed");
        Equal(0L, store.ReadNpcInventory(npc).Revision);
        Equal(0, store.ReadPlayerCommerceReceipts().Entries.Count);
        True(store.TryCommitPlayerCommerce(update, receipt, "receipt-1", "effect-1", 120),
            "Exact NPC update and receipt rejected");
        Equal(1L, store.ReadNpcInventory(npc).Revision);
        Equal(130, store.ReadNpcInventory(npc).WalletGold);
        Equal(1, store.ReadPlayerCommerceReceipts().Entries.Count);
        True(!store.TryCommitPlayerCommerce(update, receipt, "receipt-1", "effect-1", 120),
            "Transaction replay committed");
        True(store.TryReservePlayerCommerceMercantileTally(order.TransactionId, out var tally),
            "Paid receipt did not reserve skill tally");
        Equal(2L, tally.ReservationRevision);
        True(!store.TryReservePlayerCommerceMercantileTally(order.TransactionId, out _),
            "Skill tally reserved twice");
        var second = Order(PlayerCommerceKind.Buy);
        second.TransactionId = "commerce-2"; second.NpcId = npc;
        second.SourceOwnerId = npc; second.Quantity = 1;
        second.ExpectedSourceInventoryRevision = 1;
        second.ExpectedPayeeWalletRevision = 1;
        second.ExpectedPlayerInventoryToken = PlayerCommerceContract.InventorySnapshotToken(
            "player", "backpack", 900, 3);
        second.ExpectedPlayerWalletToken = PlayerCommerceContract.WalletSnapshotToken(
            "player", "gold-pieces", 70);
        var secondState = State(second);
        secondState.PayeeOwnerId = npc;
        secondState.SourceQuantity = 1; secondState.RecipientQuantity = 3;
        secondState.ReceiverCapacity = 1; secondState.PayerGold = 70;
        secondState.PayeeGold = 130;
        secondState.PlayerInventoryToken = second.ExpectedPlayerInventoryToken;
        secondState.PlayerWalletToken = second.ExpectedPlayerWalletToken;
        var secondObserved = Observation(second, secondState);
        secondObserved.PlayerInventoryTokenAfter = PlayerCommerceContract.InventorySnapshotToken(
            "player", "backpack", 900, 4);
        secondObserved.PlayerWalletTokenAfter = PlayerCommerceContract.WalletSnapshotToken(
            "player", "gold-pieces", 40);
        Equal(PlayerCommerceConfirmation.Committed,
            PlayerCommerceContract.Confirm(second, secondState, secondObserved, null,
                out var secondReceipt));
        var secondUpdate = new NpcInventoryUpdate { PersonId = npc,
            ExpectedRevision = 1, WalletGold = 160,
            Items = new List<NpcInventoryItem>() };
        True(!store.TryCommitPlayerCommerce(secondUpdate, secondReceipt, "receipt-1", "effect-2", 121),
            "Conflicting receipt ID committed");
        True(!store.TryCommitPlayerCommerce(secondUpdate, secondReceipt, "receipt-2", "effect-1", 121),
            "Conflicting effect ID committed");
        Equal(1L, store.ReadNpcInventory(npc).Revision);
        Equal(1, store.ReadPlayerCommerceReceipts().Entries.Count);
        True(store.TryCommitPlayerCommerce(secondUpdate, secondReceipt, "receipt-2", "effect-2", 121),
            "Next exact trade rejected");
        var loaded = new WorldStateStore(42);
        loaded.Restore(store.Snapshot());
        Equal(2, loaded.ReadPlayerCommerceReceipts().Entries.Count);
        True(!loaded.TryReservePlayerCommerceMercantileTally(order.TransactionId, out _),
            "Saved skill tally reserved again");
        True(!loaded.TryCommitPlayerCommerce(update, receipt, "receipt-2", "effect-2", 121),
            "Saved transaction replay committed with new receipt ID");
        var branch = store.Snapshot();
        branch.PlayerCommerceReceipts.SaveBranchId = Guid.NewGuid().ToString("N");
        bool rejected = false;
        try { loaded.Restore(branch); } catch (ArgumentException) { rejected = true; }
        True(rejected, "Branch mismatch restored over committed ledger");
        Equal(2, loaded.ReadPlayerCommerceReceipts().Entries.Count);
        var staleInventory = store.Snapshot();
        staleInventory.NpcInventories.Find(x => x.PersonId == npc).Revision = 1;
        rejected = false;
        try { loaded.Restore(staleInventory); } catch (ArgumentException) { rejected = true; }
        True(rejected, "Receipt ahead of saved NPC inventory restored");
        Equal(2L, loaded.ReadNpcInventory(npc).Revision);
        Equal(2, loaded.ReadPlayerCommerceReceipts().Entries.Count);
    }

    private static PlayerCommerceReceipt ConfirmedReceipt(PlayerCommerceKind kind,
        string transactionId = "commerce-1", long npcRevisionOffset = 0)
    {
        var order = Order(kind); order.TransactionId = transactionId;
        if (order.SourceOwnerId != "player") order.ExpectedSourceInventoryRevision += npcRevisionOffset;
        if (order.TargetOwnerId != "player") order.ExpectedTargetInventoryRevision += npcRevisionOffset;
        if (kind == PlayerCommerceKind.Buy) order.ExpectedPayeeWalletRevision += npcRevisionOffset;
        else order.ExpectedPayerWalletRevision += npcRevisionOffset;
        var state = State(order);
        Equal(PlayerCommerceConfirmation.Committed,
            PlayerCommerceContract.Confirm(order, state, Observation(order, state), null, out var receipt));
        return receipt;
    }

    private static void CompletionAndReplay()
    {
        var order = Order(PlayerCommerceKind.Give); var state = State(order);
        Equal(PlayerCommerceConfirmation.Rejected,
            PlayerCommerceContract.Confirm(order, state, null, null, out _));
        var observed = Observation(order, state);
        Equal(PlayerCommerceConfirmation.Committed,
            PlayerCommerceContract.Confirm(order, state, observed, null, out var receipt));
        Equal(PlayerCommerceConfirmation.Replay,
            PlayerCommerceContract.Confirm(order, state, observed, receipt, out _));
        var conflicting = Observation(order, state); conflicting.RecipientItemUid++;
        Equal(PlayerCommerceConfirmation.Rejected,
            PlayerCommerceContract.Confirm(order, state, conflicting, receipt, out _));
        observed = Observation(order, state); observed.TransactionId = "another";
        Rejected(order, state, observed, "Wrong transaction");
    }

    private static void PersonalTrade()
    {
        var order = Order(PlayerCommerceKind.Buy);
        order.Venue = PlayerCommerceVenue.Personal;
        order.ServiceCode = null;
        var state = State(order);
        state.ServiceAvailable = false;
        state.PlayerMercantile = 0;
        True(PlayerCommerceContract.CanBegin(order, state), "Eligible adult personal sale rejected");
        state.PlayerMercantile = 100;
        True(PlayerCommerceContract.CanBegin(order, state), "Mercantile must shape bargaining, not trade access");
        state = State(order); state.PlayerMercantile = 100; state.NpcWilling = false;
        True(!PlayerCommerceContract.CanBegin(order, state), "High Mercantile compelled NPC");
        state = State(order); state.PlayerMercantile = 100; state.NpcAdult = false;
        True(!PlayerCommerceContract.CanBegin(order, state), "Child trade admitted");
        state = State(order); state.PlayerMercantile = 100; state.NpcEligible = false;
        True(!PlayerCommerceContract.CanBegin(order, state), "Ineligible NPC admitted");
        state = State(order); state.PlayerMercantile = 120;
        True(PlayerCommerceContract.CanBegin(order, state), "Boosted Mercantile rejected");
        order = Order(PlayerCommerceKind.Sell); order.Venue = PlayerCommerceVenue.Personal;
        order.ServiceCode = null;
        state = State(order); state.PlayerMercantile = 100; state.NpcNeedsItem = false;
        True(!PlayerCommerceContract.CanBegin(order, state), "NPC purchase without need admitted");
        state = State(order); state.PlayerMercantile = 100; state.NpcValuesItem = false;
        True(!PlayerCommerceContract.CanBegin(order, state), "NPC purchase without value admitted");
        state = State(order); state.PlayerMercantile = 100;
        True(PlayerCommerceContract.CanBegin(order, state), "Personal purchase rejected");
        var observed = Observation(order, state);
        Equal(PlayerCommerceConfirmation.Committed,
            PlayerCommerceContract.Confirm(order, state, observed, null, out _));

        foreach (int blocked in new[] { 1, 2, 3, 4, 5 })
        {
            state = State(order); state.PlayerMercantile = 100;
            state.ItemEquipped = blocked == 1;
            state.ItemOccupationallyNecessary = blocked == 2;
            state.ItemUnique = blocked == 3;
            state.ItemKnownStolen = blocked == 4;
            state.ItemQuestProtected = blocked == 5;
            True(!PlayerCommerceContract.CanBegin(order, state), "Protected item admitted: " + blocked);
        }
    }

    private static void BoundHaggling()
    {
        var order = Order(PlayerCommerceKind.Buy);
        order.Venue = PlayerCommerceVenue.Personal;
        order.ServiceCode = null;
        order.Quantity = 1;
        order.QuotedTotalGold = 100;
        var binding = NpcCommerceHaggleBinding.Bind(order, 80);
        var poor = binding.CreateRound(100, 65, 1, 50, 50, 0);
        var result = NpcCommerceHagglingPolicy.Decide(poor);
        Equal(NpcCommerceHaggleDisposition.Reject, result.Disposition);
        Equal(-2, result.ProposalMoodDelta);
        Equal(100, result.PriceGold);
        var counter = binding.CreateRound(100, 90, 2, 100, 100, 100);
        result = NpcCommerceHagglingPolicy.Decide(counter);
        Equal(NpcCommerceHaggleDisposition.CounterOffer, result.Disposition);
        Equal(91, result.PriceGold);
        var accepted = binding.CreateRound(result.PriceGold, result.PriceGold, 3, 100, 100, 100);
        result = NpcCommerceHagglingPolicy.Decide(accepted);
        Equal(NpcCommerceHaggleDisposition.Accept, result.Disposition);
        Equal(91, result.PriceGold);
        Equal(1, NpcCommerceHagglingPolicy.CommittedTradeMoodDelta(binding, result.PriceGold));
        Equal("haggling:commerce-1:proposal:3", NpcCommerceHagglingPolicy.ProposalEffectId(binding.NegotiationId, 3));
        Equal("haggling:receipt-7:trade", NpcCommerceHagglingPolicy.TradeEffectId("receipt-7"));
        Throws(() => NpcCommerceHaggleBinding.Bind(order, 101), "Buy floor above native quote bound");
        Throws(() => NpcCommerceHagglingPolicy.Decide(binding.CreateRound(100, 101, 4, 50, 50, 0)),
            "Buy offer above bound native quote");

        order = Order(PlayerCommerceKind.Sell);
        order.Venue = PlayerCommerceVenue.Personal;
        order.ServiceCode = null;
        order.Quantity = 1;
        order.QuotedTotalGold = 80;
        var saleBinding = NpcCommerceHaggleBinding.Bind(order, 100);
        result = NpcCommerceHagglingPolicy.Decide(saleBinding.CreateRound(80, 105, 1, 50, 50, 0));
        Equal(NpcCommerceHaggleDisposition.Reject, result.Disposition);
        Equal(-1, result.ProposalMoodDelta);
        result = NpcCommerceHagglingPolicy.Decide(saleBinding.CreateRound(80, 88, 2, 100, 100, 100));
        Equal(NpcCommerceHaggleDisposition.Accept, result.Disposition);
        Equal(88, result.PriceGold);
        Equal(1, NpcCommerceHagglingPolicy.CommittedTradeMoodDelta(saleBinding, result.PriceGold));
    }

    private static void BasketHaggling()
    {
        var order = new PersonalBarterOrder
        {
            TransactionId = "basket-1", NpcId = "npc-1", ExpectedNpcRevision = 12,
            PlayerInventoryContainerId = "backpack", PlayerWalletContainerId = "gold-pieces",
            ExpectedPlayerWalletToken = "wallet|6:player|11:gold-pieces|100", NetGoldToNpc = 50,
            Lines = new List<PersonalBarterLine>
            {
                new PersonalBarterLine { Direction = PersonalBarterDirection.NpcToPlayer, ItemUid = 701, Quantity = 1 },
                new PersonalBarterLine { Direction = PersonalBarterDirection.PlayerToNpc, ItemUid = 702, Quantity = 2,
                    ExpectedPlayerInventoryToken = "inventory|6:player|8:backpack|702|2" },
            },
        };
        var binding = NpcCommerceHaggleBasketBinding.Bind(order, -10);
        True(binding.Matches(order), "Exact basket did not match bound quote");
        var result = NpcCommerceHagglingPolicy.Decide(binding.CreateRound(50, -15, 1, 50, 50, 0));
        Equal(NpcCommerceHaggleDisposition.Reject, result.Disposition);
        Equal(-2, result.ProposalMoodDelta);
        var acceptedRound = binding.CreateRound(50, 25, 2, 100, 100, 100);
        result = NpcCommerceHagglingPolicy.Decide(acceptedRound);
        Equal(NpcCommerceHaggleDisposition.Accept, result.Disposition);
        Equal(25, result.PriceGold);
        Equal(0, NpcCommerceHagglingPolicy.CommittedTradeMoodDelta(binding, result.PriceGold));
        True(NpcCommerceHagglingPolicy.TryResolveAcceptedBasketNetGold(binding, order, 50, acceptedRound, out var approved),
            "Fresh bound accepted basket did not resolve");
        Equal(25, approved);
        order.Lines[1].Quantity = 1;
        True(!binding.Matches(order), "Changed basket line reused old quote binding");
        True(!NpcCommerceHagglingPolicy.TryResolveAcceptedBasketNetGold(binding, order, 50, acceptedRound, out _),
            "Changed basket line resolved old accepted price");

        var oneWay = new PersonalBarterOrder
        {
            TransactionId = "basket-buy", NpcId = "npc-1", ExpectedNpcRevision = 12,
            PlayerInventoryContainerId = "backpack", PlayerWalletContainerId = "gold-pieces",
            ExpectedPlayerWalletToken = "wallet|6:player|11:gold-pieces|100", NetGoldToNpc = 20,
            Lines = new List<PersonalBarterLine>
            {
                new PersonalBarterLine { Direction = PersonalBarterDirection.NpcToPlayer, ItemUid = 703, Quantity = 1 },
            },
        };
        var oneWayBinding = NpcCommerceHaggleBasketBinding.Bind(oneWay, 15);
        True(oneWayBinding.Matches(oneWay), "One-way actual item and gold basket rejected");
    }

    private static void PersonalBargainingContext()
    {
        int neutral = NpcCommerceHagglingPolicy.MinimumBasketNetGold(1000, null, 0);
        Equal(neutral, NpcCommerceHagglingPolicy.MinimumBasketNetGold(1000, 50, 0));
        True(NpcCommerceHagglingPolicy.MinimumBasketNetGold(1000, 100, 0) < neutral, "Trust did not expand concessions");
        True(NpcCommerceHagglingPolicy.MinimumBasketNetGold(1000, 0, 0) > neutral, "Distrust did not reduce concessions");
        True(NpcCommerceHagglingPolicy.MinimumBasketNetGold(1000, null, 100) < neutral, "Good mood had no effect");
        True(NpcCommerceHagglingPolicy.MinimumBasketNetGold(1000, null, -100) > neutral, "Bad mood had no effect");
        int friend = NpcCommerceHagglingPolicy.MinimumBasketNetGold(1000, 100, 100);
        True(friend >= 650 && friend > 0, "Friendship removed the price floor");
        True(NpcCommerceHagglingPolicy.MinimumBasketNetGold(-1000, 100, 100) <
            NpcCommerceHagglingPolicy.MinimumBasketNetGold(-1000, 0, -100), "NPC payment direction reversed the benefit");
        Equal(0, NpcCommerceHagglingPolicy.MinimumBasketNetGold(0, 100, 100));
        int fineNeutral = NpcCommerceHagglingPolicy.MinimumBasketNetGold(100000, 50, 0);
        True(NpcCommerceHagglingPolicy.MinimumBasketNetGold(100000, 51, 0) < fineNeutral,
            "A single trust point was discarded before gold rounding");
        True(NpcCommerceHagglingPolicy.MinimumBasketNetGold(100000, 50, 1) < fineNeutral,
            "A single mood point was discarded before gold rounding");
        var round = new NpcCommerceHaggleBasketRound { NegotiationId = "social-basket", InitialNetGoldToNpc = 1000,
            CurrentNetGoldToNpc = 1000, NpcMinimumNetGoldToNpc = 800, PlayerNetGoldToNpc = 850,
            TotalItemQuantity = 1, Round = 1, PlayerMercantile = 60, PlayerPersonality = 50, Disposition = 0 };
        int ordinary = NpcCommerceHagglingPolicy.Decide(round).PriceGold;
        round.NpcTrust = 100; round.NpcMood = 100;
        int friendly = NpcCommerceHagglingPolicy.Decide(round).PriceGold;
        True(friendly < ordinary && friendly >= 800, "Relationship did not improve the counteroffer within its bound");
        round.NpcTrust = 0; round.NpcMood = -100;
        True(NpcCommerceHagglingPolicy.Decide(round).PriceGold > ordinary, "Poor relationship did not worsen counteroffers");
        round.NpcTrust = 100; round.NpcMood = 100; round.PlayerNetGoldToNpc = 799;
        Equal(NpcCommerceHaggleDisposition.Reject, NpcCommerceHagglingPolicy.Decide(round).Disposition);
        Throws(() => NpcCommerceHagglingPolicy.MinimumBasketNetGold(1000, 101, 0), "Invalid trust admitted");
    }

    private static PlayerCommerceOrder Order(PlayerCommerceKind kind)
    {
        bool npcSource = kind == PlayerCommerceKind.Buy || kind == PlayerCommerceKind.Take;
        bool paid = kind == PlayerCommerceKind.Buy || kind == PlayerCommerceKind.Sell;
        return new PlayerCommerceOrder
        {
            TransactionId = "commerce-1", Kind = kind, NpcId = "npc-1", ItemUid = 701,
            Venue = PlayerCommerceVenue.Merchant,
            Quantity = 2, SourceOwnerId = npcSource ? "npc-1" : "player",
            TargetOwnerId = npcSource ? "player" : "npc-1", QuotedTotalGold = paid ? 30 : 0,
            ServiceCode = paid ? "merchant" : null,
            ExpectedSourceInventoryRevision = npcSource ? 4 : 0,
            ExpectedTargetInventoryRevision = npcSource ? 0 : 8,
            ExpectedPayerWalletRevision = kind == PlayerCommerceKind.Buy ? 0 : 10,
            ExpectedPayeeWalletRevision = kind == PlayerCommerceKind.Buy ? 12 : 0,
            PlayerInventoryContainerId = "backpack", PlayerWalletContainerId = "gold-pieces",
            ExpectedPlayerInventoryToken = npcSource ? "inventory|6:player|8:backpack|900|1" :
                "inventory|6:player|8:backpack|701|3",
            ExpectedPlayerWalletToken = "wallet|6:player|11:gold-pieces|100",
            LoanPurpose = LoanPurpose.None,
            OriginReceiptId = null
        };
    }

    private static PlayerCommerceState State(PlayerCommerceOrder order) => new PlayerCommerceState
    {
        ItemUid = order.ItemUid, SourceOwnerId = order.SourceOwnerId,
        TargetOwnerId = order.TargetOwnerId, SourceQuantity = 3, RecipientItemUid = 900,
        RecipientQuantity = 1, ReceiverCapacity = 2,
        SourceInventoryRevision = order.ExpectedSourceInventoryRevision,
        TargetInventoryRevision = order.ExpectedTargetInventoryRevision,
        PayerWalletRevision = order.ExpectedPayerWalletRevision,
        PayeeWalletRevision = order.ExpectedPayeeWalletRevision,
        PlayerInventoryContainerId = "backpack", PlayerWalletContainerId = "gold-pieces",
        PlayerInventoryToken = order.ExpectedPlayerInventoryToken,
        PlayerWalletToken = order.ExpectedPlayerWalletToken,
        PayerGold = 100, PayeeGold = 100, SourceAccessible = true,
        ServiceAvailable = true, ServiceCode = order.ServiceCode,
        PayerOwnerId = order.Kind == PlayerCommerceKind.Buy ? "player" : "npc-1",
        PayeeOwnerId = order.Kind == PlayerCommerceKind.Buy ? "npc-1" : "player",
        PlayerConsented = true, NpcConsented = true,
        NpcAdult = true, NpcEligible = true, NpcWilling = true,
        NpcNeedsItem = true, NpcValuesItem = true
    };

    private static PlayerCommerceObservation Observation(PlayerCommerceOrder order, PlayerCommerceState state)
    {
        bool paid = order.QuotedTotalGold > 0;
        return new PlayerCommerceObservation
        {
            TransactionId = order.TransactionId, SourceItemUid = order.ItemUid,
            RecipientItemUid = state.RecipientItemUid, Quantity = order.Quantity,
            SourceOwnerId = order.SourceOwnerId, TargetOwnerId = order.TargetOwnerId,
            SourceQuantityAfter = state.SourceQuantity - order.Quantity,
            RecipientQuantityAfter = state.RecipientQuantity + order.Quantity,
            SourceInventoryRevisionAfter = state.SourceInventoryRevision +
                (order.SourceOwnerId == "player" ? 0 : 1),
            TargetInventoryRevisionAfter = state.TargetInventoryRevision +
                (order.TargetOwnerId == "player" ? 0 : 1),
            PayerGoldAfter = state.PayerGold - order.QuotedTotalGold,
            PayeeGoldAfter = state.PayeeGold + order.QuotedTotalGold,
            PayerWalletRevisionAfter = state.PayerWalletRevision +
                (paid && state.PayerOwnerId != "player" ? 1 : 0),
            PayeeWalletRevisionAfter = state.PayeeWalletRevision +
                (paid && state.PayeeOwnerId != "player" ? 1 : 0),
            PlayerInventoryTokenAfter = order.SourceOwnerId == "player" ?
                "inventory|6:player|8:backpack|701|1" : "inventory|6:player|8:backpack|900|3",
            PlayerWalletTokenAfter = order.Kind == PlayerCommerceKind.Buy ?
                "wallet|6:player|11:gold-pieces|70" : order.Kind == PlayerCommerceKind.Sell ?
                "wallet|6:player|11:gold-pieces|130" : "wallet|6:player|11:gold-pieces|100",
            NativeItemTransferObserved = true, NativePaymentObserved = paid
        };
    }

    private static void Rejected(PlayerCommerceOrder order, PlayerCommerceState state,
        PlayerCommerceObservation observed, string name)
    {
        Equal(PlayerCommerceConfirmation.Rejected,
            PlayerCommerceContract.Confirm(order, state, observed, null, out _), name);
    }
    private static void True(bool value, string name)
    { if (!value) throw new InvalidOperationException(name); }
    private static void Throws(Action action, string name)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException(name);
    }
    private static void Equal<T>(T expected, T actual, string name = null)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException((name ?? "Result") + ": expected " + expected + ", got " + actual); }
}
