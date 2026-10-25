using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PeopleOfTheIliacBay.Core;

/// <summary>
/// Persistent JSON-lines harness around real Core state APIs. All observations
/// and inventory receipts arrive as explicit offline fixture commands.
/// </summary>
static class SessionBridge
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true
    };

    public static void Run()
    {
        var session = new Session();
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            Console.WriteLine(session.Handle(line));
        }
    }

    public static void RunSelfTests()
    {
        var s = new Session();
        using var caps = JsonDocument.Parse(s.Handle("{\"requestId\":\"caps\",\"command\":\"read_capabilities\"}"));
        Require(caps.RootElement.GetProperty("ok").GetBoolean(), "Capabilities command must work.");
        string buyer = caps.RootElement.GetProperty("result").GetProperty("participants").GetProperty("buyer").GetString()!;
        string seller = caps.RootElement.GetProperty("result").GetProperty("participants").GetProperty("seller").GetString()!;
        string recipient = caps.RootElement.GetProperty("result").GetProperty("participants").GetProperty("recipient").GetString()!;

        using var initial = JsonDocument.Parse(s.Handle("{\"command\":\"read_inventory\",\"personId\":\"" + buyer + "\"}"));
        Require(initial.RootElement.GetProperty("result").GetProperty("walletGold").GetInt32() == 30, "Seed buyer inventory must be readable.");
        using var malformed = JsonDocument.Parse(s.Handle("{bad json"));
        Require(!malformed.RootElement.GetProperty("ok").GetBoolean(), "Malformed command must return an error response.");
        using var survives = JsonDocument.Parse(s.Handle("{\"command\":\"read_inventory\",\"personId\":\"" + buyer + "\"}"));
        Require(survives.RootElement.GetProperty("result").GetProperty("walletGold").GetInt32() == 30, "Bad line must not reset session.");

        string plan = JsonSerializer.Serialize(new
        {
            id = "task:session-test", proposalId = "proposal:session-test", residentId = buyer,
            acceptedAt = 100, updatedAt = 100, deadlineAt = 1000,
            steps = new object[] {
                new { kind = "Purchase", durationSeconds = 0, target = new { kind = "IndoorPoint", personId = seller,
                    settlementId = 100, buildingKey = 20, containerId = "7", itemId = "42", quantity = 1, quotedGold = 9 } },
                new { kind = "Transfer", itemSourceStep = 1, durationSeconds = 0, target = new { kind = "Person", personId = recipient,
                    itemId = "42", quantity = 1, transferDirection = "NpcToCounterparty" } }
            }
        }, Json);
        using var noConsent = JsonDocument.Parse(s.Handle("{\"command\":\"accept_task\",\"consent\":false,\"plan\":" + plan + "}"));
        Require(!noConsent.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Core must reject without consent.");
        using var accepted = JsonDocument.Parse(s.Handle("{\"command\":\"accept_task\",\"consent\":true,\"plan\":" + plan + "}"));
        Require(accepted.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Caller task must be accepted with consent.");

        using var dispatch = JsonDocument.Parse(s.Handle("{\"command\":\"dispatch_next\",\"taskId\":\"task:session-test\",\"now\":101,\"adapterAvailable\":true,\"consent\":true,\"scheduleCompatible\":true}"));
        Require(dispatch.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Task step must dispatch through Core.");
        string purchaseId = dispatch.RootElement.GetProperty("result").GetProperty("action").GetProperty("id").GetString()!;
        using var arrival = JsonDocument.Parse(s.Handle("{\"command\":\"observe_arrival\",\"actionId\":\"" + purchaseId + "\",\"expectedRevision\":1,\"now\":102}"));
        Require(arrival.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Arrival must be stored through Core.");
        using var staleReceipt = JsonDocument.Parse(s.Handle("{\"command\":\"apply_purchase_receipt\",\"actionId\":\"" + purchaseId +
            "\",\"expectedRevision\":1,\"now\":103,\"buyerGoldAfter\":21,\"sellerGoldAfter\":9,\"itemUid\":9001,\"nativePayload\":\"fixture:item\"}"));
        Require(!staleReceipt.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Stale action revision must not commit receipt.");
        using var purchase = JsonDocument.Parse(s.Handle("{\"command\":\"apply_purchase_receipt\",\"actionId\":\"" + purchaseId +
            "\",\"expectedRevision\":2,\"now\":103,\"buyerGoldAfter\":21,\"sellerGoldAfter\":9,\"itemUid\":9001,\"nativePayload\":\"fixture:item\"}"));
        Require(purchase.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Explicit purchase receipt must commit.");
        using var reconcilePurchase = JsonDocument.Parse(s.Handle("{\"command\":\"reconcile\",\"taskId\":\"task:session-test\",\"expectedRevision\":2,\"now\":103}"));
        Require(reconcilePurchase.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Purchase action must reconcile.");

        using var state = JsonDocument.Parse(s.Handle("{\"command\":\"read_state\"}"));
        string snapshot = state.RootElement.GetProperty("result").GetProperty("snapshotJson").GetString()!;
        using var restore = JsonDocument.Parse(s.Handle(JsonSerializer.Serialize(new { command = "restore_snapshot", snapshotJson = snapshot }, Json)));
        Require(restore.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Serialized Core snapshot must restore.");
        using var ownerBefore = JsonDocument.Parse(s.Handle("{\"command\":\"read_inventory\",\"personId\":\"" + buyer + "\"}"));
        Require(ownerBefore.RootElement.GetProperty("result").GetProperty("items")[0].GetProperty("uid").GetUInt64() == 9001,
            "Snapshot restore must retain acquired exact UID at buyer.");

        using var transferDispatch = JsonDocument.Parse(s.Handle("{\"command\":\"dispatch_next\",\"taskId\":\"task:session-test\",\"now\":104,\"adapterAvailable\":true,\"consent\":true,\"scheduleCompatible\":true}"));
        string transferId = transferDispatch.RootElement.GetProperty("result").GetProperty("action").GetProperty("id").GetString()!;
        Require(transferDispatch.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Dependent handover must dispatch after restore.");
        using var transferArrival = JsonDocument.Parse(s.Handle("{\"command\":\"observe_arrival\",\"actionId\":\"" + transferId + "\",\"expectedRevision\":1,\"now\":105}"));
        Require(transferArrival.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Handover arrival must persist.");
        using var handover = JsonDocument.Parse(s.Handle("{\"command\":\"apply_transfer_receipt\",\"actionId\":\"" + transferId + "\",\"expectedRevision\":2,\"now\":106,\"accepted\":true,\"itemUid\":9001}"));
        Require(handover.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Explicit handover receipt must commit.");
        using var reconcileHandover = JsonDocument.Parse(s.Handle("{\"command\":\"reconcile\",\"taskId\":\"task:session-test\",\"expectedRevision\":4,\"now\":106}"));
        Require(reconcileHandover.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Handover must reconcile.");
        using var ownerAfter = JsonDocument.Parse(s.Handle("{\"command\":\"read_inventory\",\"personId\":\"" + recipient + "\"}"));
        Require(ownerAfter.RootElement.GetProperty("result").GetProperty("items")[0].GetProperty("uid").GetUInt64() == 9001,
            "Real Core inventory owner after receipt must be recipient.");

        using var duplicate = JsonDocument.Parse(s.Handle("{\"command\":\"accept_task\",\"consent\":true,\"plan\":" + plan + "}"));
        Require(!duplicate.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Core must reject duplicate task proposal.");

        var cancelPlan = JsonNode.Parse(plan)!.AsObject();
        cancelPlan["id"] = "task:session-cancel"; cancelPlan["proposalId"] = "proposal:session-cancel";
        using var secondAccept = JsonDocument.Parse(s.Handle("{\"command\":\"accept_task\",\"consent\":true,\"plan\":" + cancelPlan.ToJsonString() + "}"));
        Require(secondAccept.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Second independent task must be accepted.");
        using var secondDispatch = JsonDocument.Parse(s.Handle("{\"command\":\"dispatch_next\",\"taskId\":\"task:session-cancel\",\"now\":107,\"adapterAvailable\":true,\"consent\":true,\"scheduleCompatible\":true}"));
        Require(secondDispatch.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Second task must dispatch.");
        using var cancellation = JsonDocument.Parse(s.Handle("{\"command\":\"cancel\",\"taskId\":\"task:session-cancel\",\"expectedRevision\":2,\"now\":108,\"reason\":\"Fixture stop.\"}"));
        Require(cancellation.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean() &&
            cancellation.RootElement.GetProperty("result").GetProperty("task").GetProperty("phase").GetString() == "Cancelled",
            "Cancel command must use Core task and active-action transition.");
    }

    public static void RunWaitSelfTests() => Session.RunWaitSelfTests();

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Session
    {
        private WorldStateStore store;
        private readonly string buyerId, sellerId, recipientId;

        public Session()
        {
            store = new WorldStateStore(42);
            var residents = store.GetOrCreate(100, new[] { 10, 20, 30 }, 1).Households.SelectMany(h => h.Residents).ToList();
            if (residents.Count < 3) throw new InvalidOperationException("Session fixture needs three managed NPC residents.");
            buyerId = residents[0].Id; sellerId = residents[1].Id; recipientId = residents[2].Id;
            store.InitializeNpcInventory(buyerId, 30, Array.Empty<NpcInventoryItem>());
            store.InitializeNpcInventory(sellerId, 0, new[] { new NpcInventoryItem { Uid = 42, NativePayload = "fixture:shop-stock-item" } });
            store.InitializeNpcInventory(recipientId, 0, Array.Empty<NpcInventoryItem>());
        }

        public string Handle(string line)
        {
            string? requestId = null;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                requestId = OptionalString(root, "requestId");
                string command = RequiredString(root, "command");
                object result = command switch
                {
                    "read_capabilities" => ReadCapabilities(),
                    "read_state" => ReadState(),
                    "read_inventory" => ReadInventory(RequiredString(root, "personId")),
                    "accept_task" => AcceptTask(Required(root, "plan"), OptionalBool(root, "consent", false)),
                    "dispatch_next" => DispatchNext(root),
                    "observe_arrival" => ObserveArrival(root),
                    "observe_wait_completion" => ObserveWaitCompletion(root),
                    "apply_purchase_receipt" => PurchaseReceipt(root),
                    "apply_transfer_receipt" => TransferReceipt(root),
                    "reconcile" => Reconcile(root),
                    "cancel" => Cancel(root),
                    "restore_snapshot" => RestoreSnapshot(root),
                    _ => throw new ArgumentException("Unsupported session command: " + command)
                };
                return JsonSerializer.Serialize(new { requestId, ok = true, result }, Json);
            }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentException || ex is InvalidOperationException ||
                ex is FormatException || ex is OverflowException)
            { return JsonSerializer.Serialize(new { requestId, ok = false, error = ex.Message, nativeEffectObserved = false }, Json); }
        }

        private object ReadCapabilities() => new { participants = new { buyer = buyerId, seller = sellerId, recipient = recipientId },
            commands = new[] { "read_state", "read_inventory", "read_capabilities", "accept_task", "dispatch_next", "observe_arrival",
                "observe_wait_completion", "apply_purchase_receipt", "apply_transfer_receipt", "reconcile", "cancel", "restore_snapshot" },
            seeded = true, supportsPlayerInventory = false, fixtureMode = true, nativeEffectObserved = false };

        private object ReadState()
        {
            var snapshot = store.Snapshot();
            return new { participants = new { buyer = buyerId, seller = sellerId, recipient = recipientId },
                tasks = snapshot.NpcTasks.Select(t => new { t.Id, t.ProposalId, t.ResidentId, phase = t.Phase.ToString(), t.CurrentStepIndex,
                    t.ActiveActionId, t.Revision, t.DeadlineAt }).ToArray(),
                actions = snapshot.NpcActions.Select(a => new { a.Id, a.ResidentId, kind = a.Kind.ToString(), phase = a.Phase.ToString(),
                    a.Revision, a.FinishAt, a.DeadlineAt, a.CompletedEffectId, a.AcquiredItemId }).ToArray(),
                inventories = new[] { ReadInventoryValue(buyerId), ReadInventoryValue(sellerId), ReadInventoryValue(recipientId) },
                snapshotJson = JsonSerializer.Serialize(snapshot, Json), nativeEffectObserved = false };
        }

        private object ReadInventory(string personId) => ReadInventoryValue(personId);
        private object ReadInventoryValue(string personId)
        {
            var value = store.ReadNpcInventory(personId) ?? throw new ArgumentException("No Core inventory exists for personId.");
            return new { value.PersonId, value.WalletGold, value.Revision, value.LastTransactionId,
                items = value.Items.Select(i => new { i.Uid, i.NativePayload }).ToArray() };
        }

        private object AcceptTask(JsonElement input, bool consent)
        {
            var plan = ParsePlan(input);
            NpcTaskContract.Validate(plan);
            bool accepted = store.TryAcceptNpcTask(plan, consent, out string reason);
            return new { accepted, reason, task = accepted ? TaskState(store.ReadNpcTask(plan.Id)) : null, nativeEffectObserved = false };
        }

        private object DispatchNext(JsonElement root)
        {
            string taskId = RequiredString(root, "taskId");
            var task = store.ReadNpcTask(taskId) ?? throw new ArgumentException("Unknown Core task.");
            if (task.CurrentStepIndex >= task.Steps.Count) throw new ArgumentException("Task has no next step.");
            double now = RequiredDouble(root, "now"); var step = task.Steps[task.CurrentStepIndex];
            var target = store.ResolveNpcTaskTarget(task, task.CurrentStepIndex) ?? throw new ArgumentException("Core could not resolve the next task target.");
            var job = new NpcActionJob { Id = NpcTaskContract.ActionId(task), ProposalId = NpcTaskContract.ActionId(task),
                ResidentId = task.ResidentId, Kind = step.Kind, Target = target, Phase = NpcActionPhase.Accepted, Revision = 1,
                AcceptedAt = now, UpdatedAt = now, DeadlineAt = task.DeadlineAt,
                FinishAt = step.DurationSeconds > 0 ? now + step.DurationSeconds : 0 };
            if (step.Kind == NpcActionKind.Purchase)
            {
                job.ServiceCode = NpcShopCommerce.ServiceCode; job.ServicePrice = step.Target.QuotedGold;
                job.ExpectedInventoryRevision = InventoryRevision(task.ResidentId);
                job.ExpectedTargetInventoryRevision = InventoryRevision(target.PersonId);
            }
            else if (step.Kind == NpcActionKind.Transfer)
            {
                job.ExpectedInventoryRevision = InventoryRevision(task.ResidentId);
                if (target.TransferDirection == NpcActionTransferDirection.NpcToCounterparty)
                    job.ExpectedTargetInventoryRevision = InventoryRevision(target.PersonId);
            }
            bool accepted = store.TryDispatchNpcTaskStep(taskId, task.Revision, now, job,
                OptionalBool(root, "adapterAvailable", false), OptionalBool(root, "consent", false),
                OptionalBool(root, "scheduleCompatible", false), out string reason);
            return new { accepted, reason, action = accepted ? ActionState(store.ReadNpcAction(job.Id)) : null,
                task = TaskState(store.ReadNpcTask(taskId)), nativeEffectObserved = false };
        }

        private object ObserveArrival(JsonElement root)
        {
            string id = RequiredString(root, "actionId");
            bool accepted = store.ObserveNpcAction(id, RequiredLong(root, "expectedRevision"), RequiredDouble(root, "now"), NpcActionObservation.Arrival());
            return new { accepted, action = ActionState(store.ReadNpcAction(id)), nativeEffectObserved = false };
        }

        private object ObserveWaitCompletion(JsonElement root)
        {
            string id = RequiredString(root, "actionId");
            long expectedRevision = RequiredLong(root, "expectedRevision");
            double now = RequiredDouble(root, "now");
            var job = store.ReadNpcAction(id);
            string reason;
            bool eligible = !double.IsNaN(now) && !double.IsInfinity(now) && job != null && string.Equals(job.Id, id, StringComparison.Ordinal) &&
                job.Kind == NpcActionKind.Wait && job.Phase == NpcActionPhase.Executing &&
                job.Revision == expectedRevision && job.FinishAt > 0 && now >= job.UpdatedAt &&
                now >= job.FinishAt && now < job.DeadlineAt;
            bool completed = false;
            if (eligible)
            {
                completed = store.TryCompleteNpcActionEffect(id, expectedRevision, now, () => true);
                reason = completed ? "synthetic Wait completion observed by Core" : "Core rejected synthetic Wait completion";
            }
            else if (job == null) reason = "unknown action";
            else if (job.Kind != NpcActionKind.Wait) reason = "only Wait actions can complete through this fixture command";
            else if (job.Revision != expectedRevision) reason = "action revision is stale";
            else if (job.Phase != NpcActionPhase.Executing) reason = "Wait action is not executing";
            else if (double.IsNaN(now) || double.IsInfinity(now)) reason = "Wait completion time is not finite";
            else if (now >= job.DeadlineAt) reason = "Wait completion is at or after its deadline";
            else if (job.FinishAt <= 0 || now < job.FinishAt) reason = "Wait has not reached FinishAt";
            else reason = "Wait completion time precedes the latest Core observation";
            return new { accepted = completed, reason, fixtureReceipt = completed,
                action = ActionState(store.ReadNpcAction(id)), nativeEffectObserved = false };
        }

        private object PurchaseReceipt(JsonElement root)
        {
            string id = RequiredString(root, "actionId"); long revision = RequiredLong(root, "expectedRevision"); double now = RequiredDouble(root, "now");
            var job = store.ReadNpcAction(id) ?? throw new ArgumentException("Unknown Core action.");
            var buyer = store.ReadNpcInventory(job.ResidentId) ?? throw new ArgumentException("Buyer inventory is missing.");
            var seller = store.ReadNpcInventory(job.Target.PersonId) ?? throw new ArgumentException("Seller inventory is missing.");
            int buyerAfter = RequiredInt(root, "buyerGoldAfter"), sellerAfter = RequiredInt(root, "sellerGoldAfter");
            ulong uid = RequiredULong(root, "itemUid"); string payload = RequiredString(root, "nativePayload");
            ValidatePurchaseReceipt(job, buyer, seller, buyerAfter, sellerAfter, uid, payload);
            var acquired = new NpcInventoryItem { Uid = uid, NativePayload = payload };
            bool completed = store.TryCompleteNpcActionEffect(id, revision, now, () =>
                store.CommitNpcInventoryTransaction(id + ":purchase", new[] {
                    new NpcInventoryUpdate { PersonId = buyer.PersonId, ExpectedRevision = buyer.Revision,
                        WalletGold = buyerAfter, Items = buyer.Items.Concat(new[] { acquired }).ToList() },
                    new NpcInventoryUpdate { PersonId = seller.PersonId, ExpectedRevision = seller.Revision,
                        WalletGold = sellerAfter, Items = seller.Items.Where(i => i.Uid.ToString(CultureInfo.InvariantCulture) != job.Target.ItemId).ToList() }
                }));
            return new { accepted = completed, reason = completed ? "receipt applied" : "Core rejected receipt completion.",
                action = ActionState(store.ReadNpcAction(id)), buyer = ReadInventoryValue(buyer.PersonId), seller = ReadInventoryValue(seller.PersonId),
                receiptUsed = completed, fixtureReceipt = true, nativeEffectObserved = false };
        }

        private static void ValidatePurchaseReceipt(NpcActionJob job, NpcInventoryState buyer, NpcInventoryState seller,
            int buyerAfter, int sellerAfter, ulong uid, string payload)
        {
            if (job.Kind != NpcActionKind.Purchase || job.ServiceCode != NpcShopCommerce.ServiceCode || job.Phase != NpcActionPhase.Executing ||
                job.Target.Quantity != 1 || job.ExpectedInventoryRevision != buyer.Revision || job.ExpectedTargetInventoryRevision != seller.Revision)
                throw new ArgumentException("Purchase receipt does not match an executing one-item Core purchase and expected inventory revisions.");
            if (uid == 0 || string.IsNullOrWhiteSpace(payload) || buyer.Items.Any(i => i.Uid == uid) || seller.Items.Any(i => i.Uid == uid) ||
                buyerAfter != buyer.WalletGold - job.ServicePrice || sellerAfter != seller.WalletGold + job.ServicePrice ||
                (long)buyerAfter + sellerAfter != (long)buyer.WalletGold + seller.WalletGold)
                throw new ArgumentException("Purchase receipt violates bridge gold conservation, quote, or exact-item checks.");
            if (!seller.Items.Any(i => i.Uid.ToString(CultureInfo.InvariantCulture) == job.Target.ItemId))
                throw new ArgumentException("Quoted seller stock UID is not present in Core inventory.");
        }

        private object TransferReceipt(JsonElement root)
        {
            string id = RequiredString(root, "actionId"); long revision = RequiredLong(root, "expectedRevision"); double now = RequiredDouble(root, "now");
            var job = store.ReadNpcAction(id) ?? throw new ArgumentException("Unknown Core action.");
            bool acceptedReceipt = OptionalBool(root, "accepted", false);
            ulong receiptUid = RequiredULong(root, "itemUid");
            if (!acceptedReceipt) throw new ArgumentException("Transfer receipt must explicitly report accepted:true.");
            if (job.Kind != NpcActionKind.Transfer || job.Phase != NpcActionPhase.Executing ||
                job.Target.TransferDirection != NpcActionTransferDirection.NpcToCounterparty || job.Target.Kind != NpcActionTargetKind.Person ||
                job.Target.Quantity != 1 || job.Target.ItemId != receiptUid.ToString(CultureInfo.InvariantCulture))
                throw new ArgumentException("Transfer receipt must identify the executing one-item NPC-to-NPC Core action and exact UID.");
            var source = store.ReadNpcInventory(job.ResidentId) ?? throw new ArgumentException("Source inventory is missing.");
            var target = store.ReadNpcInventory(job.Target.PersonId) ?? throw new ArgumentException("Recipient inventory is missing.");
            if (job.ExpectedInventoryRevision != source.Revision || job.ExpectedTargetInventoryRevision != target.Revision)
                throw new ArgumentException("Transfer inventories changed after dispatch; receipt expected revisions are stale.");
            var item = source.Items.SingleOrDefault(i => i.Uid == receiptUid) ?? throw new ArgumentException("Exact transfer UID is not owned by source NPC.");
            bool completed = store.TryCompleteNpcActionEffect(id, revision, now, () =>
                store.CommitNpcInventoryTransaction(id + ":transfer", new[] {
                    new NpcInventoryUpdate { PersonId = source.PersonId, ExpectedRevision = source.Revision,
                        WalletGold = source.WalletGold, Items = source.Items.Where(i => i.Uid != receiptUid).ToList() },
                    new NpcInventoryUpdate { PersonId = target.PersonId, ExpectedRevision = target.Revision,
                        WalletGold = target.WalletGold, Items = target.Items.Concat(new[] { item }).ToList() }
                }));
            return new { accepted = completed, reason = completed ? "receipt applied" : "Core rejected receipt completion.",
                action = ActionState(store.ReadNpcAction(id)), source = ReadInventoryValue(source.PersonId), recipient = ReadInventoryValue(target.PersonId),
                receiptUsed = completed, fixtureReceipt = true, nativeEffectObserved = false };
        }

        private object Reconcile(JsonElement root)
        {
            string id = RequiredString(root, "taskId"); bool accepted = store.ReconcileNpcTask(id, RequiredLong(root, "expectedRevision"), RequiredDouble(root, "now"));
            return new { accepted, task = TaskState(store.ReadNpcTask(id)), nativeEffectObserved = false };
        }

        private object Cancel(JsonElement root)
        {
            string id = RequiredString(root, "taskId");
            bool accepted = store.CancelNpcTask(id, RequiredLong(root, "expectedRevision"), RequiredDouble(root, "now"), RequiredString(root, "reason"));
            return new { accepted, task = TaskState(store.ReadNpcTask(id)), nativeEffectObserved = false };
        }

        private object RestoreSnapshot(JsonElement root)
        {
            string json = RequiredString(root, "snapshotJson");
            var state = JsonSerializer.Deserialize<WorldState>(json, Json) ?? throw new ArgumentException("Snapshot JSON is empty.");
            store.Restore(state);
            return new { accepted = true, state = ReadState(), nativeEffectObserved = false };
        }

        private NpcTaskPlan ParsePlan(JsonElement input)
        {
            var steps = new List<NpcTaskStep>();
            foreach (var value in Required(input, "steps").EnumerateArray())
            {
                var target = Required(value, "target");
                steps.Add(new NpcTaskStep { Kind = ParseEnum<NpcActionKind>(RequiredString(value, "kind")),
                    DurationSeconds = OptionalDouble(value, "durationSeconds", 0), ItemSourceStep = OptionalInt(value, "itemSourceStep", 0),
                    Target = new NpcActionTarget { Kind = ParseEnum<NpcActionTargetKind>(RequiredString(target, "kind")),
                        PersonId = OptionalString(target, "personId"), WorldSpaceId = OptionalString(target, "worldSpaceId"),
                        SettlementId = OptionalInt(target, "settlementId", 0), BuildingKey = OptionalInt(target, "buildingKey", 0),
                        X = OptionalDouble(target, "x", 0), Y = OptionalDouble(target, "y", 0), Z = OptionalDouble(target, "z", 0),
                        ItemId = OptionalString(target, "itemId"), ContainerId = OptionalString(target, "containerId"),
                        Quantity = OptionalInt(target, "quantity", 0), QuotedGold = OptionalInt(target, "quotedGold", 0),
                        TransferDirection = OptionalEnum(target, "transferDirection", NpcActionTransferDirection.NpcToCounterparty) } });
            }
            return new NpcTaskPlan { Id = RequiredString(input, "id"), ProposalId = RequiredString(input, "proposalId"),
                ResidentId = RequiredString(input, "residentId"), Steps = steps, CurrentStepIndex = OptionalInt(input, "currentStepIndex", 0),
                Phase = OptionalEnum(input, "phase", NpcTaskPhase.Ready), Revision = OptionalLong(input, "revision", 1),
                AcceptedAt = RequiredDouble(input, "acceptedAt"), UpdatedAt = RequiredDouble(input, "updatedAt"), DeadlineAt = RequiredDouble(input, "deadlineAt") };
        }

        private long InventoryRevision(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return 0;
            var inventory = store.ReadNpcInventory(id);
            return inventory?.Revision ?? 0;
        }

        private object? TaskState(NpcTaskPlan? task) => task == null ? null : new { task.Id, task.ProposalId, task.ResidentId,
            phase = task.Phase.ToString(), task.CurrentStepIndex, task.ActiveActionId, task.Revision, task.DeadlineAt };
        private static object? ActionState(NpcActionJob? job) => job == null ? null : new { job.Id, job.ResidentId,
            kind = job.Kind.ToString(), phase = job.Phase.ToString(), job.Revision, job.FinishAt, job.DeadlineAt,
            job.CompletedEffectId, job.AcquiredItemId };

    public static void RunWaitSelfTests()
    {
        var session = new Session();
        using var capabilities = JsonDocument.Parse(session.Handle("{\"command\":\"read_capabilities\"}"));
        var participants = capabilities.RootElement.GetProperty("result").GetProperty("participants");
        string buyer = participants.GetProperty("buyer").GetString()!;
        string seller = participants.GetProperty("seller").GetString()!;
        string recipient = participants.GetProperty("recipient").GetString()!;

        string waitPlan = WaitPlan("task:bridge-wait", "proposal:bridge-wait", buyer, 10, 200);
        using var accepted = JsonDocument.Parse(session.Handle("{\"command\":\"accept_task\",\"consent\":true,\"plan\":" + waitPlan + "}"));
        Require(accepted.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Core must accept explicit Wait consent.");
        using var dispatch = JsonDocument.Parse(session.Handle("{\"command\":\"dispatch_next\",\"taskId\":\"task:bridge-wait\",\"now\":101,\"adapterAvailable\":true,\"consent\":true,\"scheduleCompatible\":true}"));
        JsonElement dispatched = dispatch.RootElement.GetProperty("result");
        Require(dispatched.GetProperty("accepted").GetBoolean(), "Wait task must dispatch through Core.");
        string waitId = dispatched.GetProperty("action").GetProperty("id").GetString()!;
        Require(dispatched.GetProperty("action").GetProperty("finishAt").GetDouble() == 111 &&
            dispatched.GetProperty("action").GetProperty("deadlineAt").GetDouble() == 200,
            "Action state must expose exact Core FinishAt and DeadlineAt.");

        using var arrival = JsonDocument.Parse(session.Handle("{\"command\":\"observe_arrival\",\"actionId\":\"" + waitId + "\",\"expectedRevision\":1,\"now\":102}"));
        Require(arrival.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean() &&
            arrival.RootElement.GetProperty("result").GetProperty("action").GetProperty("phase").GetString() == "Executing",
            "Wait arrival must persist as Executing before its duration completes.");

        using var early = JsonDocument.Parse(session.Handle("{\"command\":\"observe_wait_completion\",\"actionId\":\"" + waitId + "\",\"expectedRevision\":2,\"now\":110}"));
        Require(!early.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean() &&
            early.RootElement.GetProperty("result").GetProperty("action").GetProperty("phase").GetString() == "Executing",
            "Core bridge must reject Wait completion before FinishAt.");
        using var expired = JsonDocument.Parse(session.Handle("{\"command\":\"observe_wait_completion\",\"actionId\":\"" + waitId + "\",\"expectedRevision\":2,\"now\":200}"));
        Require(!expired.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean() &&
            expired.RootElement.GetProperty("result").GetProperty("action").GetProperty("phase").GetString() == "Executing",
            "Core bridge must reject Wait completion at its exclusive DeadlineAt without Core mutation.");
        using var stale = JsonDocument.Parse(session.Handle("{\"command\":\"observe_wait_completion\",\"actionId\":\"" + waitId + "\",\"expectedRevision\":1,\"now\":111}"));
        Require(!stale.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(),
            "Core bridge must reject stale Wait action revision.");
        using var finished = JsonDocument.Parse(session.Handle("{\"command\":\"observe_wait_completion\",\"actionId\":\"" + waitId + "\",\"expectedRevision\":2,\"now\":111}"));
        JsonElement finishResult = finished.RootElement.GetProperty("result");
        Require(finishResult.GetProperty("accepted").GetBoolean() && finishResult.GetProperty("fixtureReceipt").GetBoolean() &&
            !finishResult.GetProperty("nativeEffectObserved").GetBoolean() &&
            finishResult.GetProperty("action").GetProperty("phase").GetString() == "Completed" &&
            finishResult.GetProperty("action").GetProperty("completedEffectId").GetString() == waitId + ":observed",
            "At FinishAt, only Core may create the synthetic non-native completion receipt.");
        using var duplicate = JsonDocument.Parse(session.Handle("{\"command\":\"observe_wait_completion\",\"actionId\":\"" + waitId + "\",\"expectedRevision\":3,\"now\":112}"));
        Require(!duplicate.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(),
            "Duplicate Wait completion must not advance Core action twice.");
        var wrongKindSession = new Session();
        using var wrongKindPlanAccepted = JsonDocument.Parse(wrongKindSession.Handle("{\"command\":\"read_capabilities\"}"));
        string moveResident = wrongKindPlanAccepted.RootElement.GetProperty("result").GetProperty("participants").GetProperty("buyer").GetString()!;
        using var wrongKindAccepted = JsonDocument.Parse(wrongKindSession.Handle("{\"command\":\"accept_task\",\"consent\":true,\"plan\":" +
            MovePlan("task:bridge-move", "proposal:bridge-move", moveResident, 200) + "}"));
        Require(wrongKindAccepted.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "MoveTo isolation task must be accepted.");
        using var wrongKindDispatch = JsonDocument.Parse(wrongKindSession.Handle("{\"command\":\"dispatch_next\",\"taskId\":\"task:bridge-move\",\"now\":101,\"adapterAvailable\":true,\"consent\":true,\"scheduleCompatible\":true}"));
        Require(wrongKindDispatch.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(),
            "MoveTo isolation action must dispatch: " + wrongKindDispatch.RootElement.GetProperty("result").GetProperty("reason").GetString());
        string moveId = wrongKindDispatch.RootElement.GetProperty("result").GetProperty("action").GetProperty("id").GetString()!;
        using var moveArrival = JsonDocument.Parse(wrongKindSession.Handle("{\"command\":\"observe_arrival\",\"actionId\":\"" + moveId + "\",\"expectedRevision\":1,\"now\":102}"));
        using var wrongKind = JsonDocument.Parse(wrongKindSession.Handle("{\"command\":\"observe_wait_completion\",\"actionId\":\"" + moveId + "\",\"expectedRevision\":2,\"now\":103}"));
        Require(!wrongKind.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean() &&
            wrongKind.RootElement.GetProperty("result").GetProperty("action").GetProperty("phase").GetString() == "Executing",
            "MoveTo must not complete through the Wait-specific fixture receipt.");

        using var taskReconcile = JsonDocument.Parse(session.Handle("{\"command\":\"reconcile\",\"taskId\":\"task:bridge-wait\",\"expectedRevision\":2,\"now\":112}"));
        Require(taskReconcile.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean() &&
            taskReconcile.RootElement.GetProperty("result").GetProperty("task").GetProperty("phase").GetString() == "Completed",
            "Completed Wait must reconcile through Core task transition.");
        using var saved = JsonDocument.Parse(session.Handle("{\"command\":\"read_state\"}"));
        string snapshot = saved.RootElement.GetProperty("result").GetProperty("snapshotJson").GetString()!;

        using var cancelAccept = JsonDocument.Parse(session.Handle("{\"command\":\"accept_task\",\"consent\":true,\"plan\":" +
            WaitPlan("task:bridge-wait-cancel", "proposal:bridge-wait-cancel", recipient, 10, 500) + "}"));
        Require(cancelAccept.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Independent Wait cancellation task must be accepted.");
        using var cancelDispatch = JsonDocument.Parse(session.Handle("{\"command\":\"dispatch_next\",\"taskId\":\"task:bridge-wait-cancel\",\"now\":401,\"adapterAvailable\":true,\"consent\":true,\"scheduleCompatible\":true}"));
        string cancelledWaitId = cancelDispatch.RootElement.GetProperty("result").GetProperty("action").GetProperty("id").GetString()!;
        using var cancelArrival = JsonDocument.Parse(session.Handle("{\"command\":\"observe_arrival\",\"actionId\":\"" + cancelledWaitId + "\",\"expectedRevision\":1,\"now\":402}"));
        using var cancelledTask = JsonDocument.Parse(session.Handle("{\"command\":\"cancel\",\"taskId\":\"task:bridge-wait-cancel\",\"expectedRevision\":2,\"now\":403,\"reason\":\"fixture isolation\"}"));
        Require(cancelledTask.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Core must cancel independent Wait task.");
        using var cancelledCompletion = JsonDocument.Parse(session.Handle("{\"command\":\"observe_wait_completion\",\"actionId\":\"" + cancelledWaitId + "\",\"expectedRevision\":2,\"now\":411}"));
        Require(!cancelledCompletion.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean() &&
            cancelledCompletion.RootElement.GetProperty("result").GetProperty("action").GetProperty("phase").GetString() == "Cancelled" &&
            cancelledCompletion.RootElement.GetProperty("result").GetProperty("action").GetProperty("completedEffectId").ValueKind == JsonValueKind.Null,
            "Cancelled Wait must not be completed by a later fixture receipt.");

        using var restore = JsonDocument.Parse(session.Handle(JsonSerializer.Serialize(new { command = "restore_snapshot", snapshotJson = snapshot }, Json)));
        Require(restore.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean(), "Completed Wait snapshot must restore.");
        using var replay = JsonDocument.Parse(session.Handle("{\"command\":\"observe_wait_completion\",\"actionId\":\"" + waitId + "\",\"expectedRevision\":2,\"now\":111}"));
        Require(!replay.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean() &&
            replay.RootElement.GetProperty("result").GetProperty("action").GetProperty("phase").GetString() == "Completed",
            "Replay after restore must not complete a Wait twice.");
    }

    private static string WaitPlan(string id, string proposalId, string residentId, double duration, double deadline) =>
        JsonSerializer.Serialize(new { id, proposalId, residentId, acceptedAt = 100d, updatedAt = 100d, deadlineAt = deadline,
            steps = new[] { new { kind = "Wait", durationSeconds = duration,
                target = new { kind = "Person", personId = "player" } } } }, Json);

    private static string MovePlan(string id, string proposalId, string residentId, double deadline) =>
        JsonSerializer.Serialize(new { id, proposalId, residentId, acceptedAt = 100d, updatedAt = 100d, deadlineAt = deadline,
            steps = new[] { new { kind = "MoveTo", durationSeconds = 0d,
                target = new { kind = "WorldPoint", worldSpaceId = "fixture:road", x = 1d, y = 0d, z = 2d } } } }, Json);
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum
    { if (Enum.TryParse<T>(value, true, out var parsed)) return parsed; throw new ArgumentException("Invalid " + typeof(T).Name + ": " + value); }
    private static string RequiredString(JsonElement e, string key) => OptionalString(e, key) is string s && !string.IsNullOrWhiteSpace(s) ? s : throw new ArgumentException("Required string: " + key);
    private static string? OptionalString(JsonElement e, string key) => TryProperty(e, key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static bool OptionalBool(JsonElement e, string key, bool fallback) => TryProperty(e, key, out var p) ? p.GetBoolean() : fallback;
    private static int RequiredInt(JsonElement e, string key) => TryProperty(e, key, out var p) ? p.GetInt32() : throw new ArgumentException("Required integer: " + key);
    private static int OptionalInt(JsonElement e, string key, int fallback) => TryProperty(e, key, out var p) ? p.GetInt32() : fallback;
    private static long RequiredLong(JsonElement e, string key) => TryProperty(e, key, out var p) ? p.GetInt64() : throw new ArgumentException("Required integer: " + key);
    private static long OptionalLong(JsonElement e, string key, long fallback) => TryProperty(e, key, out var p) ? p.GetInt64() : fallback;
    private static ulong RequiredULong(JsonElement e, string key) => TryProperty(e, key, out var p) ? p.GetUInt64() : throw new ArgumentException("Required unsigned integer: " + key);
    private static double RequiredDouble(JsonElement e, string key) => TryProperty(e, key, out var p) ? p.GetDouble() : throw new ArgumentException("Required number: " + key);
    private static double OptionalDouble(JsonElement e, string key, double fallback) => TryProperty(e, key, out var p) ? p.GetDouble() : fallback;
    private static T OptionalEnum<T>(JsonElement e, string key, T fallback) where T : struct, Enum =>
        TryProperty(e, key, out var p) && p.ValueKind == JsonValueKind.String ? ParseEnum<T>(p.GetString()!) : fallback;
    private static JsonElement Required(JsonElement e, string key) => TryProperty(e, key, out var p) ? p : throw new ArgumentException("Required property: " + key);
    private static bool TryProperty(JsonElement e, string key, out JsonElement value)
    { foreach (var property in e.EnumerateObject()) if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; } value = default; return false; }
}
