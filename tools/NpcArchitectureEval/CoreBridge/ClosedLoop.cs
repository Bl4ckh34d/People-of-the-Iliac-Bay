using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PeopleOfTheIliacBay.Core;

/// <summary>
/// Offline harness around persisted Core task/action/inventory APIs. Receipt data
/// is explicitly synthetic fixture input; this bridge never invokes a game adapter.
/// </summary>
static class ClosedLoop
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void RunInput(string input)
    {
        List<Proposal>? proposals;
        try { proposals = JsonSerializer.Deserialize<List<Proposal>>(input, Json); }
        catch (JsonException ex) { Console.Error.WriteLine("Invalid closed-loop JSON: " + ex.Message); Environment.ExitCode = 2; return; }
        if (proposals == null) { Console.Error.WriteLine("Closed-loop input must be a JSON array."); Environment.ExitCode = 2; return; }
        Console.WriteLine(JsonSerializer.Serialize(proposals.Select(RunProposal), Json));
    }

    public static void RunSelfTests()
    {
        var happy = Fixture("happy");
        var result = Execute(happy, Mutation.None);
        Require(result.Accepted && result.TaskPhase == "Completed" && result.CurrentStepIndex == 2,
            "Happy path must complete both Core task steps.");
        Require(result.PurchaseActionPhase == "Completed" && result.HandoverActionPhase == "Completed",
            "Both task actions must be completed in Core.");
        Require(result.AcquiredUid == 9001 && result.ItemOwner == result.RecipientId,
            "Resolved acquired UID must transfer to the NPC recipient.");
        Require(result.BuyerGoldAfter == 21 && result.SellerGoldAfter == 9 && result.TotalGoldBefore == result.TotalGoldAfter,
            "Fixture purchase receipt must conserve gold and match quoted price.");
        Require(!result.NativeEffectObserved, "Bridge must never claim a native effect.");

        var noConsent = Fixture("no-consent");
        Require(!AcceptTask(noConsent, consent: false, out var consentStore) && consentStore.Snapshot().NpcTasks.Count == 0,
            "Core must reject acceptance without consent and write no task.");

        var duplicate = Fixture("duplicate");
        var duplicateStore = Seed(duplicate, out var actors);
        var duplicateTask = BuildPlan(duplicate, actors.Buyer.Id, actors.Seller.Id, actors.Recipient.Id);
        Require(duplicateStore.TryAcceptNpcTask(duplicateTask, true, out _), "Initial acceptance must work.");
        Require(duplicateStore.CancelNpcTask(duplicateTask.Id, 1, 101, "Fixture cancellation."), "Task cancellation must work.");
        var replay = NpcTaskContract.Copy(duplicateTask); replay.Id += ":replay";
        Require(!duplicateStore.TryAcceptNpcTask(replay, true, out _), "Duplicate resident proposal must be rejected after terminalization.");

        var stale = Fixture("stale-revision");
        var staleStore = Seed(stale, out var staleActors);
        var staleTask = BuildPlan(stale, staleActors.Buyer.Id, staleActors.Seller.Id, staleActors.Recipient.Id);
        Require(staleStore.TryAcceptNpcTask(staleTask, true, out _), "Stale-revision setup acceptance.");
        var staleJob = BuildJob(staleTask, staleStore, 0, 101);
        Require(!staleStore.TryDispatchNpcTaskStep(staleTask.Id, 0, 101, staleJob, true, true, true, out _),
            "Stale task revision must not dispatch.");
        Require(staleStore.ReadNpcTask(staleTask.Id).Revision == 1 && staleStore.Snapshot().NpcActions.Count == 0,
            "Stale dispatch must not change either ledger.");

        var missingReceipt = Fixture("missing-receipt");
        var missingStore = Seed(missingReceipt, out var missingActors);
        var missingTask = BuildPlan(missingReceipt, missingActors.Buyer.Id, missingActors.Seller.Id, missingActors.Recipient.Id);
        Require(missingStore.TryAcceptNpcTask(missingTask, true, out _), "Missing-receipt setup acceptance.");
        var missingJob = BuildJob(missingTask, missingStore, 0, 101);
        Require(missingStore.TryDispatchNpcTaskStep(missingTask.Id, 1, 101, missingJob, true, true, true, out _), "Purchase dispatch.");
        Require(missingStore.ObserveNpcAction(missingJob.Id, 1, 102, NpcActionObservation.Arrival()), "Purchase arrival.");
        Require(!missingStore.TryCompleteNpcActionEffect(missingJob.Id, 2, 103, () => false), "Absent fixture receipt must not complete effect.");
        Require(missingStore.ReadNpcTask(missingTask.Id).CurrentStepIndex == 0 &&
            missingStore.ReadNpcAction(missingJob.Id).Phase == NpcActionPhase.Executing,
            "Failed receipt must not advance task or action.");

        var conservation = Fixture("bad-conservation");
        Require(!ValidateReceipt(conservation, 21, 8, 9001, "fixture:native-item"),
            "Bridge must reject non-conserving gold receipt outside Core.");
        var conservationStore = Seed(conservation, out var conservationActors);
        Require(conservationStore.ReadNpcInventory(conservationActors.Buyer.Id).WalletGold == 30 &&
            conservationStore.ReadNpcInventory(conservationActors.Seller.Id).WalletGold == 0,
            "Rejected receipt must leave Core inventories unchanged.");

        var staleInventory = Fixture("stale-inventory");
        var staleInvStore = Seed(staleInventory, out var staleInvActors);
        Require(!staleInvStore.CommitNpcInventoryTransaction("stale", new[] {
            new NpcInventoryUpdate { PersonId = staleInvActors.Buyer.Id, ExpectedRevision = 1, WalletGold = 29, Items = new() }
        }), "Core inventory transaction must reject stale expected revision.");

        var cancelAfterPurchase = Fixture("cancel-after-purchase");
        var cancelled = Execute(cancelAfterPurchase, Mutation.CancelAfterPurchase);
        Require(cancelled.TaskPhase == "Cancelled" && cancelled.BuyerGoldAfter == 21 && cancelled.ItemOwner == cancelled.BuyerId,
            "Cancellation after purchase must retain committed purchase and prevent later handover.");

        var badReceipt = Fixture("bad-receipt");
        var receiptStore = Seed(badReceipt, out var receiptActors);
        var receiptTask = BuildPlan(badReceipt, receiptActors.Buyer.Id, receiptActors.Seller.Id, receiptActors.Recipient.Id);
        Require(receiptStore.TryAcceptNpcTask(receiptTask, true, out _), "Bad-receipt setup acceptance.");
        var receiptJob = BuildJob(receiptTask, receiptStore, 0, 101);
        Require(receiptStore.TryDispatchNpcTaskStep(receiptTask.Id, 1, 101, receiptJob, true, true, true, out _), "Bad-receipt purchase dispatch.");
        Require(receiptStore.ObserveNpcAction(receiptJob.Id, 1, 102, NpcActionObservation.Arrival()), "Bad-receipt arrival.");
        var beforeBuyer = receiptStore.ReadNpcInventory(receiptActors.Buyer.Id);
        var beforeSeller = receiptStore.ReadNpcInventory(receiptActors.Seller.Id);
        Require(!ApplyPurchaseReceipt(receiptStore, receiptJob, receiptTask.Steps[0], receiptActors.Seller.Id,
            new Receipt { BuyerGoldAfter = 21, SellerGoldAfter = 9, ItemUid = 42, NativePayload = "fixture:native-item" }),
            "Receipt reusing the stock UID must fail Core transaction.");
        Require(receiptStore.ReadNpcInventory(receiptActors.Buyer.Id).Revision == beforeBuyer.Revision &&
            receiptStore.ReadNpcInventory(receiptActors.Seller.Id).Revision == beforeSeller.Revision,
            "Rejected duplicate-UID receipt must be atomic.");

        var cancelledBefore = Fixture("cancel-running");
        var cancelStore = Seed(cancelledBefore, out var cancelActors);
        var cancelTask = BuildPlan(cancelledBefore, cancelActors.Buyer.Id, cancelActors.Seller.Id, cancelActors.Recipient.Id);
        Require(cancelStore.TryAcceptNpcTask(cancelTask, true, out _), "Cancellation setup acceptance.");
        var cancelJob = BuildJob(cancelTask, cancelStore, 0, 101);
        Require(cancelStore.TryDispatchNpcTaskStep(cancelTask.Id, 1, 101, cancelJob, true, true, true, out _), "Cancellation dispatch.");
        Require(cancelStore.CancelNpcTask(cancelTask.Id, 2, 102, "Fixture stop."), "Cancel running task via Core API.");
        Require(cancelStore.ReadNpcTask(cancelTask.Id).Phase == NpcTaskPhase.Cancelled &&
            cancelStore.ReadNpcAction(cancelJob.Id).Phase == NpcActionPhase.Cancelled && cancelStore.Snapshot().NpcActions.Count == 1,
            "Cancellation must cancel current action and create no dependent action.");
    }

    public static object RunProposal(Proposal proposal)
    {
        WorldStateStore? store = null;
        Actors? actors = null;
        object? initial = null;
        NpcTaskPlan? plan = null;
        bool contractValid = false, accepted = false, fixtureReceiptUsed = false, handoverReceiptUsed = false;
        string? acceptReason = null, error = null;
        ulong acquiredUid = proposal.Receipt?.ItemUid ?? 0;
        try
        {
            store = Seed(proposal, out actors);
            plan = BuildPlan(proposal, actors.Buyer.Id, actors.Seller.Id, actors.Recipient.Id);
            NpcTaskContract.Validate(plan);
            contractValid = true;
            accepted = store.TryAcceptNpcTask(plan, proposal.Consent, out acceptReason);
            initial = ReadInventories(store, actors);
            if (!accepted) return BuildOutput(proposal.Id, store, actors, initial, contractValid, accepted, acquiredUid,
                fixtureReceiptUsed, handoverReceiptUsed, acceptReason);

            if (!IsPurchaseHandover(plan)) throw new ArgumentException("Closed-loop input currently supports one Purchase followed by one NPC-to-NPC Transfer.");
            DispatchAndArrive(store, plan, 0, 101);
            var purchase = store.ReadNpcAction(NpcTaskContract.ActionId(plan));
            var receipt = proposal.Receipt!;
            bool receiptValid = ValidateReceipt(proposal, receipt.BuyerGoldAfter, receipt.SellerGoldAfter,
                receipt.ItemUid, receipt.NativePayload);
            if (!receiptValid) throw new ArgumentException("Fixture receipt violates bridge gold or item conservation checks.");
            bool purchaseApplied = ApplyPurchaseReceipt(store, purchase, plan.Steps[0], actors.Seller.Id, receipt);
            if (!purchaseApplied) throw new InvalidOperationException("Core rejected the explicit fixture purchase receipt.");
            fixtureReceiptUsed = true;
            var task = store.ReadNpcTask(plan.Id);
            if (!store.ReconcileNpcTask(plan.Id, task.Revision, 103)) throw new InvalidOperationException("Core failed to reconcile completed purchase.");

            // Snapshot restore is a real Core serialization boundary before dependency resolution.
            var restored = new WorldStateStore(43); restored.Restore(store.Snapshot()); store = restored;
            task = store.ReadNpcTask(plan.Id);
            var resolved = store.ResolveNpcTaskTarget(task, 1);
            if (resolved == null || resolved.ItemId != acquiredUid.ToString(CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Core did not resolve the dependency to the acquired UID.");
            DispatchAndArrive(store, task, 1, 104);
            var handover = store.ReadNpcAction(NpcTaskContract.ActionId(task));
            var handoverReceipt = proposal.HandoverReceipt!;
            handoverReceiptUsed = true;
            bool handoverApplied = store.TryCompleteNpcActionEffect(handover.Id, handover.Revision, 105, () =>
                ApplyHandoverReceipt(store, handover, actors.Recipient.Id, handoverReceipt));
            if (!handoverApplied) throw new InvalidOperationException("Core rejected the explicit fixture handover receipt.");
            task = store.ReadNpcTask(plan.Id);
            if (!store.ReconcileNpcTask(plan.Id, task.Revision, 106)) throw new InvalidOperationException("Core failed to reconcile completed handover.");
            return BuildOutput(proposal.Id, store, actors, initial, contractValid, accepted, acquiredUid,
                fixtureReceiptUsed, handoverReceiptUsed, error);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException)
        {
            error = ex.Message;
            return store != null && actors != null
                ? BuildOutput(proposal.Id, store, actors, initial ?? ReadInventories(store, actors), contractValid, accepted,
                    acquiredUid, fixtureReceiptUsed, handoverReceiptUsed, error)
                : new { id = proposal.Id, accepted, contractValid, error, task = (object?)null,
                    actions = Array.Empty<object>(), inventoriesBefore = (object?)null, inventoriesAfter = (object?)null,
                    fixtureReceiptUsed, handoverReceiptUsed, nativeEffectObserved = false };
        }
    }

    private static object BuildOutput(string? id, WorldStateStore store, Actors actors, object? inventoriesBefore,
        bool contractValid, bool accepted, ulong acquiredUid, bool fixtureReceiptUsed, bool handoverReceiptUsed, string? error)
    {
        var task = store.ReadNpcTask("npc-task:" + id);
        var buyer = store.ReadNpcInventory(actors.Buyer.Id); var seller = store.ReadNpcInventory(actors.Seller.Id);
        var recipient = store.ReadNpcInventory(actors.Recipient.Id);
        var item = acquiredUid == 0 ? null : buyer.Items.Concat(seller.Items).Concat(recipient.Items).FirstOrDefault(i => i.Uid == acquiredUid);
        object? taskOutput = task == null ? null : new { phase = task.Phase.ToString(), task.CurrentStepIndex,
                task.Revision, task.ActiveActionId };
        return new { id, accepted, contractValid, reason = error, task = taskOutput, actions = store.Snapshot().NpcActions.Select(a => new { a.Id, phase = a.Phase.ToString(), a.Revision,
                a.CompletedEffectId, a.AcquiredItemId }).ToArray(), inventoriesBefore,
            inventoriesAfter = ReadInventories(store, actors), buyerId = actors.Buyer.Id, sellerId = actors.Seller.Id,
            recipientId = actors.Recipient.Id, itemOwner = acquiredUid == 0 ? null : store.NpcInventoryItemOwner(acquiredUid), acquiredUid = item?.Uid,
            totalGoldBefore = 30, totalGoldAfter = buyer.WalletGold + seller.WalletGold + recipient.WalletGold,
            fixtureReceiptUsed, handoverReceiptUsed, nativeEffectObserved = false };
    }

    private static object[] ReadInventories(WorldStateStore store, Actors actors) => new[] { actors.Buyer, actors.Seller, actors.Recipient }
        .Select(person => { var inv = store.ReadNpcInventory(person.Id); return (object)new { personId = person.Id,
            walletGold = inv?.WalletGold, revision = inv?.Revision, uids = inv?.Items.Select(i => i.Uid).ToArray() ?? Array.Empty<ulong>(),
            lastTransactionId = inv?.LastTransactionId }; }).ToArray();

    private static void DispatchAndArrive(WorldStateStore store, NpcTaskPlan task, int index, double now)
    {
        task = store.ReadNpcTask(task.Id);
        var job = BuildJob(task, store, index, now);
        if (!store.TryDispatchNpcTaskStep(task.Id, task.Revision, now, job, true, true, true, out string reason))
            throw new InvalidOperationException("Core refused task dispatch: " + reason);
        if (!store.ObserveNpcAction(job.Id, job.Revision, now + 1, NpcActionObservation.Arrival()))
            throw new InvalidOperationException("Core refused arrival observation.");
    }

    private static NpcActionJob BuildJob(NpcTaskPlan task, WorldStateStore store, int index, double now)
    {
        var step = task.Steps[index]; var target = store.ResolveNpcTaskTarget(task, index);
        var job = new NpcActionJob { Id = task.Id + ":step:" + index, ProposalId = task.Id + ":step:" + index,
            ResidentId = task.ResidentId, Kind = step.Kind, Target = target, Phase = NpcActionPhase.Accepted, Revision = 1,
            AcceptedAt = now, UpdatedAt = now, DeadlineAt = task.DeadlineAt,
            FinishAt = step.DurationSeconds > 0 ? now + step.DurationSeconds : 0 };
        if (step.Kind == NpcActionKind.Purchase)
        {
            job.ServiceCode = NpcShopCommerce.ServiceCode; job.ServicePrice = step.Target.QuotedGold;
            job.ExpectedInventoryRevision = store.ReadNpcInventory(task.ResidentId).Revision;
            job.ExpectedTargetInventoryRevision = store.ReadNpcInventory(step.Target.PersonId).Revision;
        }
        else if (step.Kind == NpcActionKind.Transfer)
        {
            job.ExpectedInventoryRevision = store.ReadNpcInventory(task.ResidentId).Revision;
            job.ExpectedTargetInventoryRevision = store.ReadNpcInventory(target.PersonId).Revision;
        }
        return job;
    }

    private static bool ApplyPurchaseReceipt(WorldStateStore store, NpcActionJob purchase, NpcTaskStep step, string sellerId, Receipt receipt)
    {
        var buyer = store.ReadNpcInventory(purchase.ResidentId); var seller = store.ReadNpcInventory(sellerId);
        if (receipt.ItemUid == 0 || string.IsNullOrWhiteSpace(receipt.NativePayload)) return false;
        var item = new NpcInventoryItem { Uid = receipt.ItemUid, NativePayload = receipt.NativePayload };
        return store.TryCompleteNpcActionEffect(purchase.Id, purchase.Revision, 103, () =>
            store.CommitNpcInventoryTransaction(purchase.Id + ":purchase", new[] {
                new NpcInventoryUpdate { PersonId = buyer.PersonId, ExpectedRevision = buyer.Revision,
                    WalletGold = receipt.BuyerGoldAfter, Items = buyer.Items.Concat(new[] { item }).ToList() },
                new NpcInventoryUpdate { PersonId = seller.PersonId, ExpectedRevision = seller.Revision,
                    WalletGold = receipt.SellerGoldAfter, Items = seller.Items.Where(i => i.Uid.ToString(CultureInfo.InvariantCulture) != step.Target.ItemId).ToList() }
            }));
    }

    private static bool ApplyHandoverReceipt(WorldStateStore store, NpcActionJob transfer, string recipientId, HandoverReceipt receipt)
    {
        if (receipt.Accept != true) return false;
        var buyer = store.ReadNpcInventory(transfer.ResidentId); var recipient = store.ReadNpcInventory(recipientId);
        ulong uid = ulong.Parse(transfer.Target.ItemId, CultureInfo.InvariantCulture);
        var item = buyer.Items.SingleOrDefault(i => i.Uid == uid);
        if (item == null) return false;
        if (receipt.RecipientWalletGold != null && receipt.RecipientWalletGold != recipient.WalletGold) return false;
        return store.CommitNpcInventoryTransaction(transfer.Id + ":transfer", new[] {
            new NpcInventoryUpdate { PersonId = buyer.PersonId, ExpectedRevision = buyer.Revision,
                WalletGold = buyer.WalletGold, Items = buyer.Items.Where(i => i.Uid != uid).ToList() },
            new NpcInventoryUpdate { PersonId = recipient.PersonId, ExpectedRevision = recipient.Revision,
                WalletGold = recipient.WalletGold, Items = recipient.Items.Concat(new[] { item }).ToList() }
        });
    }

    private static bool ValidateReceipt(Proposal proposal, int buyerAfter, int sellerAfter, ulong itemUid, string payload)
    {
        int quoted = proposal.Steps?.FirstOrDefault()?.Target?.QuotedGold ?? 0;
        return itemUid > 0 && !string.IsNullOrWhiteSpace(payload) && buyerAfter == 30 - quoted && sellerAfter == quoted &&
            buyerAfter + sellerAfter == 30;
    }

    private static bool IsPurchaseHandover(NpcTaskPlan task) => task.Steps.Count == 2 && task.Steps[0].Kind == NpcActionKind.Purchase &&
        task.Steps[1].Kind == NpcActionKind.Transfer && task.Steps[1].ItemSourceStep == 1 &&
        task.Steps[0].Target.PersonId != task.ResidentId && task.Steps[1].Target.PersonId != task.ResidentId;

    private static bool AcceptTask(Proposal proposal, bool consent, out WorldStateStore store)
    {
        store = Seed(proposal, out var actors);
        var task = BuildPlan(proposal, actors.Buyer.Id, actors.Seller.Id, actors.Recipient.Id);
        return store.TryAcceptNpcTask(task, consent, out _);
    }

    private static WorldStateStore Seed(Proposal proposal, out Actors actors)
    {
        var store = new WorldStateStore(42);
        var residents = store.GetOrCreate(100, new[] { 10, 20, 30 }, 1).Households.SelectMany(h => h.Residents).ToList();
        if (residents.Count < 3) throw new InvalidOperationException("Fixture requires three managed Core residents.");
        actors = new Actors(residents[0], residents[1], residents[2]);
        store.InitializeNpcInventory(actors.Buyer.Id, 30, Array.Empty<NpcInventoryItem>());
        store.InitializeNpcInventory(actors.Seller.Id, 0, new[] { new NpcInventoryItem { Uid = 42, NativePayload = "fixture:shop-stock-item" } });
        store.InitializeNpcInventory(actors.Recipient.Id, 0, Array.Empty<NpcInventoryItem>());
        return store;
    }

    private static NpcTaskPlan BuildPlan(Proposal proposal, string buyerId, string sellerId, string recipientId)
    {
        if (string.IsNullOrWhiteSpace(proposal.Id) || proposal.Id.Length > 120) throw new ArgumentException("Proposal id is required and limited to 120 characters.");
        if (proposal.Steps == null || proposal.Steps.Count != 2) throw new ArgumentException("Exactly two task steps are required.");
        var purchase = proposal.Steps[0]; var transfer = proposal.Steps[1];
        if (!Enum.TryParse<NpcActionKind>(purchase.Kind, true, out var purchaseKind) || !Enum.TryParse<NpcActionKind>(transfer.Kind, true, out var transferKind))
            throw new ArgumentException("Unknown task step kind.");
        if (purchaseKind != NpcActionKind.Purchase || transferKind != NpcActionKind.Transfer) throw new ArgumentException("Only Purchase then Transfer is supported by the closed-loop receipt driver.");
        if (purchase.DurationSeconds != 0 || transfer.DurationSeconds != 0 || purchase.Target == null || transfer.Target == null ||
            !string.Equals(purchase.Target.Kind, "IndoorPoint", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(transfer.Target.Kind, "Person", StringComparison.OrdinalIgnoreCase) || transfer.ItemSourceStep != 1 ||
            purchase.Target.ParticipantRef != "seller" || transfer.Target.ParticipantRef != "recipient" ||
            purchase.Target.SettlementId != 100 || purchase.Target.BuildingKey != 20 || purchase.Target.ContainerId != "7" ||
            purchase.Target.ItemId != "42" || purchase.Target.Quantity != 1 || transfer.Target.ItemId != "42" ||
            transfer.Target.Quantity != 1 || transfer.Target.TransferDirection != "NpcToCounterparty" ||
            purchase.Target.UnsupportedProperties?.Count > 0 || transfer.Target.UnsupportedProperties?.Count > 0)
            throw new ArgumentException("Bounded fixture targets must explicitly name seller stock UID 42 and one NPC-to-NPC handover to recipient.");
        int gold = purchase.Target.QuotedGold;
        if (gold < 1 || gold > NpcInventoryLimits.MaximumWalletGold) throw new ArgumentException("Quoted gold is outside the fixture range.");
        if (proposal.Receipt == null || proposal.HandoverReceipt?.Accept == null) throw new ArgumentException("Both explicit fixture receipts are required; handover accept must be explicit.");
        var task = new NpcTaskPlan { Id = "npc-task:" + proposal.Id, ProposalId = "npc-proposal:" + proposal.Id,
            ResidentId = buyerId, Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = 100, UpdatedAt = 100, DeadlineAt = 1000,
            Steps = new List<NpcTaskStep> {
                new() { Kind = purchaseKind, Target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint,
                    SettlementId = purchase.Target.SettlementId, BuildingKey = purchase.Target.BuildingKey, PersonId = sellerId,
                    ContainerId = purchase.Target.ContainerId, ItemId = purchase.Target.ItemId, Quantity = purchase.Target.Quantity, QuotedGold = gold } },
                new() { Kind = transferKind, ItemSourceStep = 1, Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person,
                    PersonId = recipientId, ItemId = transfer.Target.ItemId, Quantity = transfer.Target.Quantity,
                    TransferDirection = Enum.Parse<NpcActionTransferDirection>(transfer.Target.TransferDirection, true) } }
            } };
        return task;
    }

    private static Proposal Fixture(string id) => new() { Id = id, Consent = true, Steps = new() {
        new StepInput { Kind = "Purchase", DurationSeconds = 0, Target = new TargetInput { Kind = "IndoorPoint", ParticipantRef = "seller",
            SettlementId = 100, BuildingKey = 20, ContainerId = "7", ItemId = "42", Quantity = 1, QuotedGold = 9 } },
        new StepInput { Kind = "Transfer", DurationSeconds = 0, ItemSourceStep = 1, Target = new TargetInput { Kind = "Person",
            ParticipantRef = "recipient", ItemId = "42", Quantity = 1, TransferDirection = "NpcToCounterparty" } }
    }, Receipt = new Receipt { BuyerGoldAfter = 21, SellerGoldAfter = 9, ItemUid = 9001, NativePayload = "fixture:native-item" },
        HandoverReceipt = new HandoverReceipt { Accept = true } };

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed record Actors(Resident Buyer, Resident Seller, Resident Recipient);
    private enum Mutation { None, CancelAfterPurchase }

    private static Result Execute(Proposal proposal, Mutation mutation)
    {
        var store = Seed(proposal, out var actors);
        var task = BuildPlan(proposal, actors.Buyer.Id, actors.Seller.Id, actors.Recipient.Id);
        Require(store.TryAcceptNpcTask(task, true, out _), "Task acceptance.");
        var receipt = proposal.Receipt ?? throw new InvalidOperationException("Fixture receipt missing.");
        DispatchAndArrive(store, task, 0, 101);
        var purchase = store.ReadNpcAction(task.Id + ":step:0");
        Require(ValidateReceipt(proposal, receipt.BuyerGoldAfter, receipt.SellerGoldAfter, receipt.ItemUid, receipt.NativePayload), "Receipt validation.");
        Require(ApplyPurchaseReceipt(store, purchase, task.Steps[0], actors.Seller.Id, receipt), "Purchase receipt commit.");
        task = store.ReadNpcTask(task.Id); Require(store.ReconcileNpcTask(task.Id, task.Revision, 103), "Purchase reconcile.");
        if (mutation == Mutation.CancelAfterPurchase)
        {
            task = store.ReadNpcTask(task.Id); Require(store.CancelNpcTask(task.Id, task.Revision, 104, "Fixture cancellation."), "Cancel after purchase.");
            var cancelledBuyer = store.ReadNpcInventory(actors.Buyer.Id); var cancelledSeller = store.ReadNpcInventory(actors.Seller.Id);
            return new Result { Accepted = true, TaskPhase = store.ReadNpcTask(task.Id).Phase.ToString(), CurrentStepIndex = store.ReadNpcTask(task.Id).CurrentStepIndex,
                PurchaseActionPhase = store.ReadNpcAction(purchase.Id).Phase.ToString(), HandoverActionPhase = "NotDispatched", AcquiredUid = receipt.ItemUid,
                ItemOwner = store.NpcInventoryItemOwner(receipt.ItemUid), BuyerId = actors.Buyer.Id, RecipientId = actors.Recipient.Id,
                BuyerGoldAfter = cancelledBuyer.WalletGold, SellerGoldAfter = cancelledSeller.WalletGold, TotalGoldBefore = 30, TotalGoldAfter = cancelledBuyer.WalletGold + cancelledSeller.WalletGold,
                NativeEffectObserved = false };
        }
        var restored = new WorldStateStore(43); restored.Restore(store.Snapshot()); store = restored;
        task = store.ReadNpcTask(task.Id); DispatchAndArrive(store, task, 1, 104); var transfer = store.ReadNpcAction(task.Id + ":step:1");
        Require(store.TryCompleteNpcActionEffect(transfer.Id, transfer.Revision, 105, () => ApplyHandoverReceipt(store, transfer, actors.Recipient.Id, proposal.HandoverReceipt ?? new HandoverReceipt())), "Handover receipt.");
        task = store.ReadNpcTask(task.Id); Require(store.ReconcileNpcTask(task.Id, task.Revision, 106), "Handover reconcile.");
        var buyer = store.ReadNpcInventory(actors.Buyer.Id); var seller = store.ReadNpcInventory(actors.Seller.Id);
        return new Result { Accepted = true, TaskPhase = store.ReadNpcTask(task.Id).Phase.ToString(), CurrentStepIndex = store.ReadNpcTask(task.Id).CurrentStepIndex,
            PurchaseActionPhase = store.ReadNpcAction(purchase.Id).Phase.ToString(), HandoverActionPhase = store.ReadNpcAction(transfer.Id).Phase.ToString(),
            AcquiredUid = receipt.ItemUid, ItemOwner = store.NpcInventoryItemOwner(receipt.ItemUid), BuyerId = actors.Buyer.Id, RecipientId = actors.Recipient.Id,
            BuyerGoldAfter = buyer.WalletGold, SellerGoldAfter = seller.WalletGold, TotalGoldBefore = 30,
            TotalGoldAfter = buyer.WalletGold + seller.WalletGold + store.ReadNpcInventory(actors.Recipient.Id).WalletGold, NativeEffectObserved = false };
    }

    public sealed class Proposal
    {
        public string? Id { get; set; }
        public bool Consent { get; set; }
        public List<StepInput>? Steps { get; set; }
        public Receipt? Receipt { get; set; }
        public HandoverReceipt? HandoverReceipt { get; set; }
    }
    public sealed class StepInput { public string? Kind { get; set; } public double DurationSeconds { get; set; } public int ItemSourceStep { get; set; } public string? TargetRef { get; set; } public TargetInput? Target { get; set; } }
    public sealed class TargetInput
    {
        public string? Kind { get; set; }
        public string? ParticipantRef { get; set; }
        public string? ItemId { get; set; }
        public string? ContainerId { get; set; }
        public string? TransferDirection { get; set; }
        public int SettlementId { get; set; }
        public int BuildingKey { get; set; }
        public int QuotedGold { get; set; }
        public int Quantity { get; set; }
        public string? TargetRef { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? UnsupportedProperties { get; set; }
    }
    public sealed class Receipt { public int BuyerGoldAfter { get; set; } public int SellerGoldAfter { get; set; } public ulong ItemUid { get; set; } public string NativePayload { get; set; } = ""; }
    public sealed class HandoverReceipt { public bool? Accept { get; set; } public int? RecipientWalletGold { get; set; } }
    private sealed class Result
    {
        public bool Accepted; public string TaskPhase = ""; public int CurrentStepIndex; public string PurchaseActionPhase = "";
        public string HandoverActionPhase = ""; public ulong AcquiredUid; public string? ItemOwner; public string BuyerId = "";
        public string RecipientId = ""; public int BuyerGoldAfter; public int SellerGoldAfter; public int TotalGoldBefore;
        public int TotalGoldAfter; public bool NativeEffectObserved;
    }
}
