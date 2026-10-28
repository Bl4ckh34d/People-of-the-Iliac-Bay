using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private const ulong PotionUid = 501;
    private const ulong OtherPotionUid = 502;
    private const ulong StockUid = 44;
    private const string Query = "What happened to your health? Did you use a healing potion?";
    private static int assertions;

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("own completed native treatment is recalled with health delta and unknown cause", OwnTreatment),
            ("native rest receipt is recalled with its real duration and health delta", RestTreatment),
            ("purchase and provider claims require the same task source dependency", PurchaseProvenance),
            ("an unrelated purchase action is not linked to healing by inference", UnlinkedPurchase),
            ("another resident's treatment is never exposed", PatientIsolation),
            ("future treatment is hidden until its native time", FutureTreatment),
            ("failed or unfinished treatment keeps the empty-journal result", FailedOrUnfinished),
            ("new treatment action branch makes captured memory stale", NewBranchStalesRead),
            ("care recall stays inside the configured output budget", BoundedOutput)
        };

        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed; " + assertions + " assertions.");
        return failed == 0 ? 0 : 1;
    }

    private static void OwnTreatment()
    {
        Fixture fixture = NewFixture();
        CompleteOwnedHealingItem(fixture, fixture.ResidentId, "healing:own", 4d);

        object read = Capture(fixture.Store, fixture.ResidentId, Query, 5d, 1000);
        string text = Text(read);
        True(text.Contains("healing item") || text.Contains("healing potion"), "Recall must name the treatment actually used.");
        True(text.Contains("health") && text.Contains(fixture.HealthBefore.ToString()) && text.Contains((fixture.HealthBefore + 5).ToString()),
            "Recall must state the native health improvement.");
        True(text.Contains("cause") && (text.Contains("unknown") || text.Contains("not recorded")),
            "Recall must leave the health-loss cause unknown.");
        True(!text.Contains(fixture.ResidentId), "Native identity keys must not appear in recall text.");
        True(!text.Contains("gold") && !text.Contains("bought"), "Owned item use must not imply a purchase.");
        True(Current(fixture.Store, read, fixture.ResidentId, 5d), "Unchanged treatment source should remain current.");
        string unrelated = fixture.Store.RecallConversation(fixture.ResidentId, "silver ring", 5d, 700);
        Equal(unrelated, Text(Capture(fixture.Store, fixture.ResidentId, "silver ring", 5d, 700)),
            "A non-care query must retain ordinary memory retrieval unchanged.");
    }

    private static void PurchaseProvenance()
    {
        Fixture fixture = NewFixture(withOwnedPotion: false);
        CompletePurchasedHealingItem(fixture, "healing:purchased", 9, 105d);
        string provider = fixture.Store.ResolveResident(fixture.SellerId).FullName;
        if (string.IsNullOrWhiteSpace(provider)) provider = "the shop";
        CompleteOwnedHealingItem(fixture, fixture.ResidentId, "healing:later-owned", 108d, healthGain: 3);
        string costText = Text(Capture(fixture.Store, fixture.ResidentId,
            "How much did you pay for the healing potion?", 109d, 1000));
        True(costText.Contains("game time 105") && costText.Contains(provider) && costText.Contains("9 gold"),
            "A later unlinked treatment must not hide the earlier purchase when asked for its cost.");
        object read = Capture(fixture.Store, fixture.ResidentId, Query, 109d, 1000);
        string text = Text(read);
        True(text.Contains("game time 108") && !text.Contains("bought") && !text.Contains("gold"),
            "A general health query should select the latest independently confirmed treatment.");
        True(text.Contains("health") && text.Contains((fixture.HealthBefore + 5).ToString()),
            "Purchase details must remain attached to an actual healing receipt.");
    }

    private static void RestTreatment()
    {
        Fixture fixture = NewFixture();
        NpcVitalsView vitals = fixture.Store.ReadNpcVitals(fixture.ResidentId, NpcArchetype.Citizen);
        const string id = "rest:recall";
        var action = new NpcActionJob
        {
            Id = id, ProposalId = id, ResidentId = fixture.ResidentId, Kind = NpcActionKind.RestToHeal,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 1, BuildingKey = 10,
                ContainerId = "bed:resident-stay:1", X = 2, Y = 0, Z = 4 },
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 2, UpdatedAt = 2, DeadlineAt = 100,
            RestDurationSeconds = 30, ExpectedVitalsRevision = vitals.Revision
        };
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptAction(fixture.Store, action, true, true, true, out reason), "Rest action should be accepted: " + reason);
        True(fixture.Store.ObserveNpcAction(action.Id, 1, 3, NpcActionObservation.Arrival()), "Rest should start at observed arrival.");
        action = fixture.Store.ReadNpcAction(action.Id);
        Equal(3d, action.RestStartedAt, "Receipt should use observed rest start.");
        CompleteRestAction(fixture.Store, action, vitals.Revision, fixture.HealthBefore + 5, action.FinishAt);
        string text = Text(Capture(fixture.Store, fixture.ResidentId, "Did you rest, and how was your health?", 34, 500));
        True(text.Contains("rested") && text.Contains("30 seconds"), "Recall should state the observed rest interval.");
        True(text.Contains(fixture.HealthBefore.ToString()) && text.Contains((fixture.HealthBefore + 5).ToString()),
            "Rest recall should report its actual health improvement.");
        True(!text.Contains("bought") && !text.Contains("gold"), "Rest must not imply a purchase.");
    }

    private static void UnlinkedPurchase()
    {
        Fixture fixture = NewFixture(withOwnedPotion: false);
        CompleteStandalonePurchase(fixture, 9, 12d);
        CompleteOwnedHealingItem(fixture, fixture.ResidentId, "healing:after-unlinked-purchase", 15d);
        string text = Text(Capture(fixture.Store, fixture.ResidentId, Query, 16d, 1000));
        True(text.Contains("healing item") && text.Contains("health"), "Native use remains recallable.");
        True(!text.Contains("bought") && !text.Contains("gold"),
            "A separate purchase without the task dependency cannot be attributed to the treatment.");
    }

    private static void PatientIsolation()
    {
        Fixture fixture = NewFixture();
        string other = fixture.OtherResidentId;
        Injure(fixture.Store, other, out int otherBefore);
        AddHealingInventory(fixture.Store, other, OtherPotionUid);
        CompleteOwnedHealingItem(fixture, other, "healing:other", 4d, otherBefore, OtherPotionUid);

        string expected = fixture.Store.RecallConversation(fixture.ResidentId, Query, 5d, 1000);
        object read = Capture(fixture.Store, fixture.ResidentId, Query, 5d, 1000);
        Equal(expected, Text(read), "An unrelated resident's treatment must not affect this person's empty recall.");
    }

    private static void FutureTreatment()
    {
        Fixture fixture = NewFixture();
        CompleteOwnedHealingItem(fixture, fixture.ResidentId, "healing:future", 104d);
        object read = Capture(fixture.Store, fixture.ResidentId, Query, 103d, 1000);
        string expected = fixture.Store.RecallConversation(fixture.ResidentId, Query, 103d, 1000);
        Equal(expected, Text(read), "A receipt from a later game time must not be visible yet.");
        True(Current(fixture.Store, read, fixture.ResidentId, 103d), "Future receipt has not become eligible yet.");
        True(!Current(fixture.Store, read, fixture.ResidentId, 104d), "Crossing receipt time invalidates the old read.");
    }

    private static void FailedOrUnfinished()
    {
        Fixture fixture = NewFixture();
        NpcActionJob action = AcceptHealingAction(fixture, fixture.ResidentId, "healing:unfinished", 2d);
        string expected = fixture.Store.RecallConversation(fixture.ResidentId, Query, 3d, 1000);
        object read = Capture(fixture.Store, fixture.ResidentId, Query, 3d, 1000);
        Equal(expected, Text(read), "An executing treatment has no confirmed effect to recall.");

        True(fixture.Store.ObserveNpcAction(action.Id, action.Revision, 4d,
            NpcActionObservation.Interrupted("The native treatment did not complete.")), "Unfinished action should fail.");
        True(!Current(fixture.Store, read, fixture.ResidentId, 4d), "Treatment action revision invalidates captured authority.");
        expected = fixture.Store.RecallConversation(fixture.ResidentId, Query, 4d, 1000);
        read = Capture(fixture.Store, fixture.ResidentId, Query, 4d, 1000);
        Equal(expected, Text(read), "A failed treatment remains absent from recall.");
    }

    private static void NewBranchStalesRead()
    {
        Fixture fixture = NewFixture();
        CompleteOwnedHealingItem(fixture, fixture.ResidentId, "healing:first", 4d);
        object read = Capture(fixture.Store, fixture.ResidentId, Query, 5d, 1000);
        True(Current(fixture.Store, read, fixture.ResidentId, 5d), "Initial treatment read should be current.");

        var restored = new WorldStateStore(0);
        restored.Restore(fixture.Store.Snapshot());
        True(!Current(restored, read, fixture.ResidentId, 5d),
            "Reloading the source branch must reject an earlier treatment recall snapshot.");

        AcceptHealingAction(fixture, fixture.ResidentId, "healing:new-branch", 6d);
        True(!Current(fixture.Store, read, fixture.ResidentId, 6d),
            "A later treatment action branch must invalidate a result derived from prior care sources.");
    }

    private static void BoundedOutput()
    {
        Fixture fixture = NewFixture();
        CompleteOwnedHealingItem(fixture, fixture.ResidentId, "healing:bounded", 4d);
        object read = Capture(fixture.Store, fixture.ResidentId, Query, 5d, 256);
        True(Text(read).Length <= 256, "Treatment recall must obey the existing memory output budget.");
        True(Text(read).Contains(fixture.HealthBefore.ToString()) && Text(read).Contains((fixture.HealthBefore + 5).ToString()),
            "The bounded receipt must preserve the exact observed health delta.");
    }

    private static Fixture NewFixture(bool withOwnedPotion = true)
    {
        var store = new WorldStateStore(42);
        var town = store.GetOrCreate(1, new[] { 10, 20, 30 }, 1);
        string resident = town.Households[0].Residents[0].Id;
        string seller = town.Households[1].Residents[0].Id;
        string other = town.Households[2].Residents[0].Id;

        var items = new List<NpcInventoryItem>();
        if (withOwnedPotion) items.Add(new NpcInventoryItem { Uid = PotionUid, NativePayload = "potion-stack:2" });
        True(store.InitializeNpcInventory(resident, 30, items), "Resident inventory should initialize.");
        True(store.InitializeNpcInventory(seller, 0, new[] { new NpcInventoryItem { Uid = StockUid, NativePayload = "shop potion stock" } }),
            "Shop stock should initialize.");
        Injure(store, resident, out int healthBefore);
        return new Fixture { Store = store, ResidentId = resident, SellerId = seller, OtherResidentId = other,
            HealthBefore = healthBefore, VitalsRevision = store.ReadNpcVitals(resident, NpcArchetype.Citizen).Revision,
            InventoryRevision = store.ReadNpcInventory(resident).Revision };
    }

    private static void Injure(WorldStateStore store, string resident, out int healthBefore)
    {
        var vitals = store.ReadNpcVitals(resident, NpcArchetype.Citizen);
        healthBefore = Math.Max(1, vitals.MaximumHealth - 8);
        True(store.TryCaptureNpcVitals(resident, NpcArchetype.Citizen, healthBefore,
            vitals.CurrentMagicka, 1, vitals.Revision), "Native injury fixture should persist.");
    }

    private static void AddHealingInventory(WorldStateStore store, string resident, ulong sourceUid = PotionUid)
    {
        True(store.InitializeNpcInventory(resident, 30,
            new[] { new NpcInventoryItem { Uid = sourceUid, NativePayload = "potion-stack:2" } }),
            "Other resident inventory should initialize.");
    }

    private static NpcActionJob AcceptHealingAction(Fixture fixture, string residentId, string actionId, double acceptedAt,
        ulong sourceUid = PotionUid)
    {
        NpcVitalsView vitals = fixture.Store.ReadNpcVitals(residentId, NpcArchetype.Citizen);
        NpcInventoryState inventory = fixture.Store.ReadNpcInventory(residentId);
        var job = new NpcActionJob
        {
            Id = actionId, ProposalId = actionId, ResidentId = residentId,
            Kind = NpcActionKind.UseHealingItem,
            Target = HealingTarget(sourceUid.ToString()), Phase = NpcActionPhase.Accepted, Revision = 1,
            AcceptedAt = acceptedAt, UpdatedAt = acceptedAt, DeadlineAt = acceptedAt + 100,
            ExpectedInventoryRevision = inventory.Revision
        };
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptAction(fixture.Store, job, true, true, true, out reason), "Treatment action should be accepted: " + reason);
        True(fixture.Store.ObserveNpcAction(job.Id, job.Revision, acceptedAt + 1, NpcActionObservation.Arrival()),
            "Treatment action should enter native execution.");
        return fixture.Store.ReadNpcAction(job.Id);
    }

    private static void CompleteOwnedHealingItem(Fixture fixture, string residentId, string actionId,
        double completedAt, int? knownHealthBefore = null, ulong sourceUid = PotionUid, int healthGain = 5)
    {
        if (fixture.Store.ReadNpcInventory(residentId).Items.Count == 0) AddHealingInventory(fixture.Store, residentId);
        int healthBefore = knownHealthBefore ?? fixture.Store.ReadNpcVitals(residentId, NpcArchetype.Citizen).CurrentHealth;
        if (knownHealthBefore == null && residentId != fixture.ResidentId) Injure(fixture.Store, residentId, out healthBefore);
        NpcVitalsView vitals = fixture.Store.ReadNpcVitals(residentId, NpcArchetype.Citizen);
        var action = AcceptHealingAction(fixture, residentId, actionId, completedAt - 2, sourceUid);
        CompleteHealingAction(fixture.Store, action, NpcArchetype.Citizen, vitals.Revision,
            fixture.Store.ReadNpcInventory(residentId).Revision, sourceUid, healthBefore, healthBefore + healthGain, completedAt);
    }

    private static void CompletePurchasedHealingItem(Fixture fixture, string taskId, int price, double completedAt)
    {
        var task = new NpcTaskPlan
        {
            Id = taskId, ProposalId = taskId + ":proposal", ResidentId = fixture.ResidentId,
            Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = 100, UpdatedAt = 100, DeadlineAt = 500,
            Steps = new List<NpcTaskStep>
            {
                new NpcTaskStep { Kind = NpcActionKind.Purchase, Target = new NpcActionTarget
                {
                    Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 1, BuildingKey = 10,
                    PersonId = fixture.SellerId, ContainerId = "700", ItemId = StockUid.ToString(),
                    Quantity = 1, QuotedGold = price
                } },
                new NpcTaskStep { Kind = NpcActionKind.UseHealingItem, ItemSourceStep = 1,
                    Target = HealingTarget(StockUid.ToString()) }
            }
        };
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptTask(fixture.Store, task, true, out reason), "Care task should be accepted: " + reason);
        taskId = task.Id;
        var purchase = new NpcActionJob
        {
            Id = taskId + ":step:0", ProposalId = taskId + ":step:0", ResidentId = fixture.ResidentId,
            Kind = NpcActionKind.Purchase, Target = NpcActionExecutor.CopyTarget(task.Steps[0].Target),
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 100, UpdatedAt = 100,
            DeadlineAt = 500, ServiceCode = NpcShopCommerce.ServiceCode, ServicePrice = price
        };
        True(fixture.Store.TryDispatchNpcTaskStep(taskId, 1, 100, purchase, true, true, true, out reason),
            "Purchase step should dispatch: " + reason);
        True(fixture.Store.ObserveNpcAction(purchase.Id, 1, 101, NpcActionObservation.Arrival()), "Purchase should execute.");
        purchase = fixture.Store.ReadNpcAction(purchase.Id);
        True(fixture.Store.TryCompleteNpcActionEffect(purchase.Id, purchase.Revision, 102, () =>
            fixture.Store.CommitNpcInventoryTransaction(purchase.Id + ":purchase", new[]
            {
                new NpcInventoryUpdate { PersonId = fixture.ResidentId, ExpectedRevision = 0, WalletGold = 30 - price,
                    Items = new List<NpcInventoryItem> { new NpcInventoryItem { Uid = PotionUid, NativePayload = "potion-stack:2" } } },
                new NpcInventoryUpdate { PersonId = fixture.SellerId, ExpectedRevision = 0, WalletGold = price,
                    Items = new List<NpcInventoryItem>() }
            })), "Native purchase receipt should commit.");
        True(fixture.Store.ReconcileNpcTask(taskId, 2, 103), "Purchase task step should reconcile.");

        task = fixture.Store.ReadNpcTask(taskId);
        NpcActionTarget treatmentTarget = fixture.Store.ResolveNpcTaskTarget(task, 1);
        Equal(PotionUid.ToString(), treatmentTarget.ItemId, "Task must resolve exact acquired UID.");
        NpcVitalsView vitals = fixture.Store.ReadNpcVitals(fixture.ResidentId, NpcArchetype.Citizen);
        var treatment = new NpcActionJob
        {
            Id = taskId + ":step:1", ProposalId = taskId + ":step:1", ResidentId = fixture.ResidentId,
            Kind = NpcActionKind.UseHealingItem, Target = treatmentTarget, Phase = NpcActionPhase.Accepted,
            Revision = 1, AcceptedAt = 104, UpdatedAt = 104, DeadlineAt = 500,
            ExpectedInventoryRevision = fixture.Store.ReadNpcInventory(fixture.ResidentId).Revision
        };
        True(fixture.Store.TryDispatchNpcTaskStep(taskId, task.Revision, 104, treatment, true, true, true, out reason),
            "Treatment step should dispatch: " + reason);
        True(fixture.Store.ObserveNpcAction(treatment.Id, 1, completedAt - 1, NpcActionObservation.Arrival()),
            "Treatment should enter execution.");
        treatment = fixture.Store.ReadNpcAction(treatment.Id);
        CompleteHealingAction(fixture.Store, treatment, NpcArchetype.Citizen, vitals.Revision,
            fixture.Store.ReadNpcInventory(fixture.ResidentId).Revision, PotionUid, fixture.HealthBefore,
            fixture.HealthBefore + 5, completedAt);
        task = fixture.Store.ReadNpcTask(taskId);
        True(fixture.Store.ReconcileNpcTask(taskId, task.Revision, completedAt + 1), "Treatment task step should reconcile.");
    }

    private static void CompleteStandalonePurchase(Fixture fixture, int price, double completedAt)
    {
        const string id = "purchase:unlinked";
        var target = new NpcActionTarget
        {
            Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 1, BuildingKey = 20,
            PersonId = fixture.SellerId, ContainerId = "700", ItemId = StockUid.ToString(),
            Quantity = 1, QuotedGold = price
        };
        var purchase = new NpcActionJob
        {
            Id = id, ProposalId = id, ResidentId = fixture.ResidentId, Kind = NpcActionKind.Purchase,
            Target = target, Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = completedAt - 3,
            UpdatedAt = completedAt - 3, DeadlineAt = completedAt + 100,
            ServiceCode = NpcShopCommerce.ServiceCode, ServicePrice = price,
            ExpectedInventoryRevision = fixture.Store.ReadNpcInventory(fixture.ResidentId).Revision
        };
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptAction(fixture.Store, purchase, true, true, true, out reason), "Standalone purchase should be accepted: " + reason);
        True(fixture.Store.ObserveNpcAction(purchase.Id, 1, completedAt - 2, NpcActionObservation.Arrival()), "Purchase should execute.");
        purchase = fixture.Store.ReadNpcAction(purchase.Id);
        True(fixture.Store.TryCompleteNpcActionEffect(purchase.Id, purchase.Revision, completedAt - 1, () =>
            fixture.Store.CommitNpcInventoryTransaction(purchase.Id + ":purchase", new[]
            {
                new NpcInventoryUpdate { PersonId = fixture.ResidentId, ExpectedRevision = 0, WalletGold = 30 - price,
                    Items = new List<NpcInventoryItem> { new NpcInventoryItem { Uid = PotionUid, NativePayload = "potion-stack:2" } } },
                new NpcInventoryUpdate { PersonId = fixture.SellerId, ExpectedRevision = 0, WalletGold = price,
                    Items = new List<NpcInventoryItem>() }
            })), "Standalone native purchase should commit.");
        Equal(PotionUid.ToString(), fixture.Store.ReadNpcAction(purchase.Id).AcquiredItemId,
            "Standalone purchase still has an authoritative acquired UID.");
    }

    private static void CompleteHealingAction(WorldStateStore store, NpcActionJob action, NpcArchetype archetype,
        long vitalsRevision, long inventoryRevision, ulong sourceUid, int healthBefore, int healthAfter, double now)
    {
        MethodInfo method = typeof(WorldStateStore).GetMethod("TryCompleteNpcHealingItemAction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        True(method != null, "Typed healing completion API must exist.");
        Type outcomeType = typeof(WorldStateStore).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcTreatmentNativeOutcome");
        True(outcomeType != null, "Native treatment outcome type must exist.");
        MethodInfo factory = outcomeType.GetMethod("Verified", BindingFlags.Static | BindingFlags.NonPublic);
        True(factory != null, "Verified treatment result factory must exist.");
        NpcVitalsView currentVitals = store.ReadNpcVitals(action.ResidentId, archetype);
        NpcInventoryState currentInventory = store.ReadNpcInventory(action.ResidentId);
        NpcInventoryItem sourceItem = null;
        foreach (NpcInventoryItem item in currentInventory.Items)
            if (item.Uid == sourceUid) { sourceItem = item; break; }
        True(sourceItem != null, "Treatment fixture must consume an item that is actually owned.");
        IList<NpcInventoryItem> remaining = sourceItem.NativePayload == "potion-stack:2"
            ? (IList<NpcInventoryItem>)new[] { new NpcInventoryItem { Uid = sourceUid, NativePayload = "potion-stack:1" } }
            : new List<NpcInventoryItem>();
        object outcome = factory.Invoke(null, new object[] { sourceUid, healthBefore, healthAfter, currentVitals.CurrentMagicka, remaining });
        var callbackCounter = new CallbackCounter();
        Type callbackType = typeof(Func<>).MakeGenericType(outcomeType);
        Expression body = Expression.Block(
            Expression.Call(Expression.Constant(callbackCounter), typeof(CallbackCounter).GetMethod(nameof(CallbackCounter.Increment))),
            Expression.Constant(outcome, outcomeType));
        Delegate callback = Expression.Lambda(callbackType, body).Compile();
        bool completed = (bool)method.Invoke(store, new object[]
        {
            action.Id, store.ReadNpcAction(action.Id).Revision, now, archetype,
            vitalsRevision, inventoryRevision, callback
        });
        True(completed, "Native health and item receipts should commit.");
        Equal(1, callbackCounter.Count, "Native outcome callback should run exactly once.");
    }

    private static void CompleteRestAction(WorldStateStore store, NpcActionJob action, long vitalsRevision,
        int healthAfter, double now)
    {
        MethodInfo method = typeof(WorldStateStore).GetMethod("TryCompleteNpcRestAction", BindingFlags.Instance | BindingFlags.NonPublic);
        True(method != null, "Typed rest completion API must exist.");
        Type outcomeType = typeof(WorldStateStore).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcTreatmentRestNativeOutcome");
        True(outcomeType != null, "Native rest outcome type must exist.");
        MethodInfo factory = outcomeType.GetMethod("Verified", BindingFlags.Static | BindingFlags.NonPublic);
        True(factory != null, "Verified rest outcome factory must exist.");
        NpcVitalsView current = store.ReadNpcVitals(action.ResidentId, NpcArchetype.Citizen);
        object outcome = factory.Invoke(null, new object[] { current.CurrentHealth, healthAfter, current.CurrentMagicka });
        var counter = new CallbackCounter();
        Type callbackType = typeof(Func<>).MakeGenericType(outcomeType);
        Expression body = Expression.Block(
            Expression.Call(Expression.Constant(counter), typeof(CallbackCounter).GetMethod(nameof(CallbackCounter.Increment))),
            Expression.Constant(outcome, outcomeType));
        Delegate callback = Expression.Lambda(callbackType, body).Compile();
        bool completed = (bool)method.Invoke(store, new object[]
        {
            action.Id, store.ReadNpcAction(action.Id).Revision, now, NpcArchetype.Citizen, vitalsRevision, callback
        });
        True(completed, "Native rest and health receipts should commit.");
        Equal(1, counter.Count, "Rest callback should run exactly once.");
    }

    private static NpcActionTarget HealingTarget(string itemId)
    {
        return new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint, WorldSpaceId = "settlement:1",
            X = 1, Y = 0, Z = 2, ItemId = itemId, Quantity = 1 };
    }

    private static object Capture(WorldStateStore store, string person, string query, double now, int maximumCharacters)
    {
        MethodInfo method = typeof(WorldStateStore).GetMethod("TryCaptureNpcMemoryRead");
        True(method != null, "Memory read capture API must exist.");
        object[] args = { person, query, now, maximumCharacters, null };
        True((bool)method.Invoke(store, args), "Memory read should capture.");
        return args[4];
    }

    private static bool Current(WorldStateStore store, object read, string person, double now)
    {
        return (bool)typeof(WorldStateStore).GetMethod("IsNpcMemoryReadCurrent").Invoke(store, new[] { read, person, (object)now });
    }

    private static string Text(object read) { return (string)read.GetType().GetProperty("Text").GetValue(read); }
    private static void True(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message = null)
    {
        assertions++;
        if (!Equals(expected, actual)) throw new InvalidOperationException(message ?? "Expected " + expected + ", got " + actual + ".");
    }

    private sealed class CallbackCounter { public int Count; public void Increment() { Count++; } }

    private sealed class Fixture
    {
        public WorldStateStore Store;
        public string ResidentId, SellerId, OtherResidentId;
        public int HealthBefore;
        public long VitalsRevision, InventoryRevision;
    }
}
