using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private const int UseHealingItemValue = 12;
    private const ulong PotionUid = 501;

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("healing item kind is appended and accepts a stable exact item point", ActionContract),
            ("treatment task may source exactly one purchased item", PurchasedItemTaskDependency),
            ("stale action, vitals, or inventory revisions reject before native callback", StalePreflight),
            ("wrong source item and unrelated payload changes reject", WrongItemAndUnrelatedChanges),
            ("native effect without health improvement leaves ledgers unchanged", NoImprovement),
            ("one exact stack unit commits once and duplicate use is rejected", StackConsumptionIsExactlyOnce),
            ("treatment receipt and inventory outcome survive save reload", SaveReloadPreservesReceipt),
            ("child residents cannot complete potion or rest treatment", ChildTreatmentRejected),
            ("rest task binds a positive duration to a stable bed target", RestTaskContract),
            ("task rest dispatch leaves the clock unset until arrival and restores binding", RestTaskDispatchBinding),
            ("rest timer begins at observed arrival and receipt survives reload", RestStartsAtArrival),
            ("rest rejects changed baseline vitals before native callback", RestStaleVitals)
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

    private static void ActionContract()
    {
        Equal("UseHealingItem", Enum.GetName(typeof(NpcActionKind), UseHealingItemValue),
            "treatment kind must be the appended value 12");
        NpcActionExecutor.Validate(ActionJob(NewFixture(), "501"));
    }

    private static void PurchasedItemTaskDependency()
    {
        var task = new NpcTaskPlan
        {
            Id = "treatment-task:1", ProposalId = "treatment-request:1", ResidentId = "npc:treatment",
            Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = 100, UpdatedAt = 100, DeadlineAt = 500,
            Steps = new List<NpcTaskStep>
            {
                new NpcTaskStep { Kind = NpcActionKind.Purchase, Target = new NpcActionTarget
                {
                    Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 1, BuildingKey = 10,
                    PersonId = "npc:healer-shop", ContainerId = "700", ItemId = "44", Quantity = 1, QuotedGold = 9
                } },
                new NpcTaskStep { Kind = (NpcActionKind)UseHealingItemValue, ItemSourceStep = 1,
                    Target = HealingTarget("settlement:1", "44") }
            }
        };
        NpcTaskContract.Validate(task);

        task.Steps[1].Target.Quantity = 2;
        Throws<ArgumentException>(() => NpcTaskContract.Validate(task));
    }

    private static void StalePreflight()
    {
        var fixture = NewFixture();
        int callbacks;
        bool completed = Complete(fixture, expectedInventoryRevision: 1, expectedVitalsRevision: fixture.VitalsRevision,
            healthAfter: fixture.HealthBefore + 5, sourceUid: PotionUid, remaining: StackAfter(), callbacks: out callbacks);
        True(!completed, "stale inventory revision must reject treatment");
        Equal(0, callbacks, "store must reject stale input before invoking native callback");
        AssertUnchanged(fixture);

        fixture = NewFixture();
        completed = Complete(fixture, expectedInventoryRevision: fixture.InventoryRevision,
            expectedVitalsRevision: fixture.VitalsRevision + 1, healthAfter: fixture.HealthBefore + 5,
            sourceUid: PotionUid, remaining: StackAfter(), callbacks: out callbacks);
        True(!completed, "stale vitals revision must reject treatment");
        Equal(0, callbacks, "stale vitals must reject before native callback");
        AssertUnchanged(fixture);
    }

    private static void WrongItemAndUnrelatedChanges()
    {
        var fixture = NewFixture();
        int callbacks;
        bool completed = Complete(fixture, fixture.InventoryRevision, fixture.VitalsRevision,
            fixture.HealthBefore + 5, PotionUid + 1, StackAfter(), out callbacks);
        True(!completed, "callback result for different source UID must be rejected");
        Equal(1, callbacks, "a fresh valid action may run native verification before rejecting a mismatched result");
        AssertUnchanged(fixture);

        fixture = NewFixture(includeOtherItem: true);
        var changedOther = new List<NpcInventoryItem>
        {
            new NpcInventoryItem { Uid = PotionUid, NativePayload = "potion-stack:1" },
            new NpcInventoryItem { Uid = PotionUid + 1, NativePayload = "changed-unrelated-item" }
        };
        completed = Complete(fixture, fixture.InventoryRevision, fixture.VitalsRevision,
            fixture.HealthBefore + 5, PotionUid, changedOther, out callbacks);
        True(!completed, "only selected potion payload may change");
        AssertUnchanged(fixture);
    }

    private static void NoImprovement()
    {
        var fixture = NewFixture();
        int callbacks;
        bool completed = Complete(fixture, fixture.InventoryRevision, fixture.VitalsRevision,
            fixture.HealthBefore, PotionUid, StackAfter(), out callbacks);
        True(!completed, "no health improvement must fail treatment");
        Equal(1, callbacks, "native effect verification ran for fresh action");
        AssertUnchanged(fixture);
    }

    private static void StackConsumptionIsExactlyOnce()
    {
        var fixture = NewFixture();
        int callbacks;
        bool completed = Complete(fixture, fixture.InventoryRevision, fixture.VitalsRevision,
            fixture.HealthBefore + 5, PotionUid, StackAfter(), out callbacks);
        True(completed, "verified one-unit stack decrement should complete");
        Equal(1, callbacks, "native callback should run once");
        var action = fixture.Store.ReadNpcAction(fixture.ActionId);
        Equal(NpcActionPhase.Completed, action.Phase);
        Equal(1L, fixture.Store.ReadNpcInventory(fixture.ResidentId).Revision);
        Equal("potion-stack:1", fixture.Store.ReadNpcInventory(fixture.ResidentId).Items[0].NativePayload);
        Equal(1L, fixture.Store.ReadNpcVitals(fixture.ResidentId, NpcArchetype.Citizen).Revision - fixture.VitalsRevision);

        int duplicateCallbacks;
        bool duplicate = Complete(fixture, action.Revision, fixture.VitalsRevision + 1,
            fixture.HealthBefore + 6, PotionUid, StackAfter(), out duplicateCallbacks);
        True(!duplicate, "completed action cannot consume another item");
        Equal(0, duplicateCallbacks, "duplicate completion rejects before callback");
        Equal(1L, fixture.Store.ReadNpcInventory(fixture.ResidentId).Revision);
        Equal(action.CompletedEffectId, fixture.Store.ReadNpcAction(fixture.ActionId).CompletedEffectId);
    }

    private static void SaveReloadPreservesReceipt()
    {
        var fixture = NewFixture();
        True(Complete(fixture, fixture.InventoryRevision, fixture.VitalsRevision,
            fixture.HealthBefore + 5, PotionUid, StackAfter(), out _), "treatment should complete before save");
        var restored = new WorldStateStore(0);
        restored.Restore(fixture.Store.Snapshot());
        var action = restored.ReadNpcAction(fixture.ActionId);
        True(action != null && action.Phase == NpcActionPhase.Completed, "completed treatment action must restore");
        object receipt = TreatmentReceipt(action);
        True(receipt != null, "typed treatment receipt must restore");
        Equal(PotionUid, (ulong)ReadReceipt(receipt, "SourceItemUid"));
        Equal(fixture.ActionId + ":treatment", (string)ReadReceipt(receipt, "TransactionId"));
        Equal(fixture.HealthBefore, (int)ReadReceipt(receipt, "HealthBefore"));
        Equal(fixture.HealthBefore + 5, (int)ReadReceipt(receipt, "HealthAfter"));
        Equal(4d, (double)ReadReceipt(receipt, "ObservedGameTime"));
        Equal(fixture.ActionId + ":treatment", restored.ReadNpcInventory(fixture.ResidentId).LastTransactionId);
        Equal(fixture.HealthBefore + 5,
            restored.ReadNpcVitals(fixture.ResidentId, NpcArchetype.Citizen).CurrentHealth);
    }

    private static void RestTaskContract()
    {
        Equal("RestToHeal", Enum.GetName(typeof(NpcActionKind), 13),
            "rest action kind must append after healing item");
        var task = new NpcTaskPlan
        {
            Id = "rest-task:1", ProposalId = "rest-request:1", ResidentId = "npc:rest",
            Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = 100, UpdatedAt = 100, DeadlineAt = 500,
            Steps = new List<NpcTaskStep>
            {
                new NpcTaskStep { Kind = (NpcActionKind)13, Target = BedTarget(), DurationSeconds = 60 }
            }
        };
        NpcTaskContract.Validate(task);
        task.Steps[0].DurationSeconds = 0;
        Throws<ArgumentException>(() => NpcTaskContract.Validate(task));
    }

    private static void RestTaskDispatchBinding()
    {
        var store = new WorldStateStore(42);
        var town = store.GetOrCreate(1, new[] { 10 }, 1);
        string resident = town.Households[0].Residents[0].Id;
        const string taskId = "rest-task-dispatch:1";
        var task = new NpcTaskPlan
        {
            Id = taskId, ProposalId = taskId, ResidentId = resident,
            Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = 100, UpdatedAt = 100, DeadlineAt = 500,
            Steps = new List<NpcTaskStep>
            {
                new NpcTaskStep { Kind = (NpcActionKind)13, Target = BedTarget(), DurationSeconds = 50 }
            }
        };
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptTask(store, task, true, out reason), "rest task should be accepted: " + reason);
        var action = new NpcActionJob
        {
            Id = task.Id + ":step:0", ProposalId = task.Id + ":step:0", ResidentId = resident,
            Kind = (NpcActionKind)13, Target = BedTarget(),
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 100, UpdatedAt = 100,
            DeadlineAt = 500, RestDurationSeconds = 50,
            ExpectedVitalsRevision = store.ReadNpcVitals(resident, NpcArchetype.Citizen).Revision
        };
        True(store.TryDispatchNpcTaskStep(task.Id, 1, 100, action, true, true, true, out reason),
            "rest task action should dispatch: " + reason);
        var acceptedAction = store.ReadNpcAction(action.Id);
        Equal(0d, acceptedAction.FinishAt);
        Equal(0d, acceptedAction.RestStartedAt);
        True(store.ObserveNpcAction(action.Id, acceptedAction.Revision, 110, NpcActionObservation.Arrival()),
            "arrival should start the task rest interval");
        Equal(160d, store.ReadNpcAction(action.Id).FinishAt);

        var restored = new WorldStateStore(0);
        restored.Restore(store.Snapshot());
        Equal(160d, restored.ReadNpcAction(action.Id).FinishAt);
        Equal(NpcTaskPhase.Running, restored.ReadNpcTask(task.Id).Phase);
    }

    private static void RestStartsAtArrival()
    {
        var fixture = NewRestFixture();
        var accepted = fixture.Store.ReadNpcAction(fixture.ActionId);
        Equal(0d, accepted.RestStartedAt);
        Equal(0d, accepted.FinishAt);
        True(fixture.Store.ObserveNpcAction(fixture.ActionId, accepted.Revision, 10,
            NpcActionObservation.Arrival()), "rest should begin at observed bed arrival");
        var arrived = fixture.Store.ReadNpcAction(fixture.ActionId);
        Equal(10d, arrived.RestStartedAt);
        Equal(60d, arrived.FinishAt);

        True(fixture.Store.SaveNpcActionPosition(fixture.ActionId, arrived.Revision, BedTarget(), false, 20),
            "position updates may advance UpdatedAt while rest timer stays fixed");
        var repositioned = fixture.Store.ReadNpcAction(fixture.ActionId);
        Equal(20d, repositioned.UpdatedAt);
        Equal(10d, repositioned.RestStartedAt);
        Equal(60d, repositioned.FinishAt);
        True(CompleteRest(fixture, fixture.VitalsRevision, fixture.HealthBefore + 5, out int callbacks),
            "verified rest should commit after its full elapsed duration");
        Equal(1, callbacks);
        var completed = fixture.Store.ReadNpcAction(fixture.ActionId);
        Equal(NpcActionPhase.Completed, completed.Phase);
        object receipt = TreatmentReceipt(completed);
        Equal("Rest", Enum.GetName(ReadReceipt(receipt, "Kind").GetType(), ReadReceipt(receipt, "Kind")));
        Equal(0UL, (ulong)ReadReceipt(receipt, "SourceItemUid"));
        Equal(fixture.ActionId + ":treatment", (string)ReadReceipt(receipt, "TransactionId"));
        Equal(10d, (double)ReadReceipt(receipt, "RestStartedAt"));
        Equal(50d, (double)ReadReceipt(receipt, "RestDurationSeconds"));
        Equal(60d, (double)ReadReceipt(receipt, "ObservedGameTime"));

        var restored = new WorldStateStore(0);
        restored.Restore(fixture.Store.Snapshot());
        var restoredReceipt = TreatmentReceipt(restored.ReadNpcAction(fixture.ActionId));
        Equal(10d, (double)ReadReceipt(restoredReceipt, "RestStartedAt"));
        Equal(50d, (double)ReadReceipt(restoredReceipt, "RestDurationSeconds"));
    }

    private static void RestStaleVitals()
    {
        var fixture = NewRestFixture();
        var action = fixture.Store.ReadNpcAction(fixture.ActionId);
        True(fixture.Store.ObserveNpcAction(fixture.ActionId, action.Revision, 10,
            NpcActionObservation.Arrival()), "rest should begin");
        var vitals = fixture.Store.ReadNpcVitals(fixture.ResidentId, NpcArchetype.Citizen);
        True(fixture.Store.TryCaptureNpcVitals(fixture.ResidentId, NpcArchetype.Citizen,
            fixture.HealthBefore - 1, vitals.CurrentMagicka, 15, vitals.Revision),
            "intervening native health change should advance the vitals revision");
        int callbacks;
        True(!CompleteRest(fixture, fixture.VitalsRevision, fixture.HealthBefore + 5, out callbacks),
            "rest cannot complete against a changed pre-rest snapshot");
        Equal(0, callbacks);
        Equal(NpcActionPhase.Executing, fixture.Store.ReadNpcAction(fixture.ActionId).Phase);
    }

    private static void ChildTreatmentRejected()
    {
        var potion = NewFixture(childResident: true);
        bool potionCompleted = Complete(potion, potion.InventoryRevision, potion.VitalsRevision,
            potion.HealthBefore + 5, PotionUid, StackAfter(), out int potionCallbacks);
        True(!potionCompleted, "child resident cannot receive this treatment action");
        Equal(0, potionCallbacks, "child eligibility must reject before native callback");
        AssertUnchanged(potion);

        var rest = NewRestFixture(childResident: true);
        var action = rest.Store.ReadNpcAction(rest.ActionId);
        True(rest.Store.ObserveNpcAction(rest.ActionId, action.Revision, 10,
            NpcActionObservation.Arrival()), "child rest fixture should reach bed");
        True(!CompleteRest(rest, rest.VitalsRevision, rest.HealthBefore + 5, out int restCallbacks),
            "child resident cannot complete rest treatment");
        Equal(0, restCallbacks, "child rest must reject before native callback");
        Equal(NpcActionPhase.Executing, rest.Store.ReadNpcAction(rest.ActionId).Phase);
    }

    private static Fixture NewRestFixture(bool childResident = false)
    {
        string resident;
        var store = CreateFixtureStore(childResident, out resident);
        var initialVitals = store.ReadNpcVitals(resident, NpcArchetype.Citizen);
        int injuredHealth = Math.Max(1, initialVitals.MaximumHealth - 8);
        True(store.TryCaptureNpcVitals(resident, NpcArchetype.Citizen, injuredHealth,
            initialVitals.CurrentMagicka, 1, initialVitals.Revision), "rest fixture injury should persist");
        string actionId = "rest-action:1";
        var action = new NpcActionJob
        {
            Id = actionId, ProposalId = actionId, ResidentId = resident,
            Kind = (NpcActionKind)13, Target = BedTarget(),
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 2, UpdatedAt = 2,
            DeadlineAt = 100, RestDurationSeconds = 50,
            ExpectedVitalsRevision = store.ReadNpcVitals(resident, NpcArchetype.Citizen).Revision
        };
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptAction(store, action, true, true, true, out reason), "rest action should be accepted: " + reason);
        return new Fixture
        {
            Store = store, ResidentId = resident, ActionId = action.Id, HealthBefore = injuredHealth,
            VitalsRevision = store.ReadNpcVitals(resident, NpcArchetype.Citizen).Revision,
            InventoryRevision = 0
        };
    }

    private static NpcActionTarget BedTarget()
    {
        return new NpcActionTarget
        {
            Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 1, BuildingKey = 10,
            ContainerId = "bed:resident-stay:1", X = 2, Y = 0, Z = 4
        };
    }

    private static bool CompleteRest(Fixture fixture, long expectedVitalsRevision, int healthAfter,
        out int callbacks)
    {
        callbacks = 0;
        var method = typeof(WorldStateStore).GetMethod("TryCompleteNpcRestAction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        True(method != null, "internal typed rest completion API must exist");
        var outcomeType = typeof(WorldStateStore).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcTreatmentRestNativeOutcome");
        True(outcomeType != null, "runtime-only rest outcome must exist");
        var factory = outcomeType.GetMethod("Verified", BindingFlags.Static | BindingFlags.NonPublic);
        True(factory != null, "rest verification outcome factory must exist");
        var outcome = factory.Invoke(null, new object[]
        {
            fixture.HealthBefore, healthAfter,
            fixture.Store.ReadNpcVitals(fixture.ResidentId, NpcArchetype.Citizen).CurrentMagicka
        });
        var counter = new CallbackCounter();
        var callbackType = typeof(Func<>).MakeGenericType(outcomeType);
        var body = Expression.Block(
            Expression.Call(Expression.Constant(counter), typeof(CallbackCounter).GetMethod(nameof(CallbackCounter.Increment))),
            Expression.Constant(outcome, outcomeType));
        var callback = Expression.Lambda(callbackType, body).Compile();
        var action = fixture.Store.ReadNpcAction(fixture.ActionId);
        var result = method.Invoke(fixture.Store, new object[]
        {
            fixture.ActionId, action.Revision, 60d, NpcArchetype.Citizen,
            expectedVitalsRevision, callback
        });
        callbacks = counter.Count;
        return (bool)result;
    }

    private static Fixture NewFixture(bool includeOtherItem = false, bool childResident = false)
    {
        string resident;
        var store = CreateFixtureStore(childResident, out resident);
        var items = new List<NpcInventoryItem>
        {
            new NpcInventoryItem { Uid = PotionUid, NativePayload = "potion-stack:2" }
        };
        if (includeOtherItem)
            items.Add(new NpcInventoryItem { Uid = PotionUid + 1, NativePayload = "unrelated-item" });
        True(store.InitializeNpcInventory(resident, 30, items), "fixture inventory should initialize");

        var vitals = store.ReadNpcVitals(resident, NpcArchetype.Citizen);
        int injuredHealth = Math.Max(1, vitals.MaximumHealth - 8);
        True(store.TryCaptureNpcVitals(resident, NpcArchetype.Citizen, injuredHealth,
            vitals.CurrentMagicka, 1, vitals.Revision), "fixture injury should persist");

        const string actionId = "treatment-action:1";
        var action = ActionJob(new Fixture
        {
            Store = store, ResidentId = resident, ActionId = actionId,
            HealthBefore = injuredHealth, VitalsRevision = store.ReadNpcVitals(resident, NpcArchetype.Citizen).Revision,
            InventoryRevision = store.ReadNpcInventory(resident).Revision
        }, PotionUid.ToString());
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptAction(store, action, true, true, true, out reason), "fixture action should be accepted: " + reason);
        True(store.ObserveNpcAction(action.Id, action.Revision, 3, NpcActionObservation.Arrival()), "fixture action should enter execution");

        return new Fixture
        {
            Store = store, ResidentId = resident, ActionId = action.Id,
            HealthBefore = injuredHealth, VitalsRevision = store.ReadNpcVitals(resident, NpcArchetype.Citizen).Revision,
            InventoryRevision = store.ReadNpcInventory(resident).Revision
        };
    }

    private static WorldStateStore CreateFixtureStore(bool childResident, out string residentId)
    {
        var store = new WorldStateStore(42);
        if (!childResident)
        {
            residentId = store.GetOrCreate(1, new[] { 10 }, 1).Households[0].Residents[0].Id;
            return store;
        }

        var homes = new List<HomeProfile>();
        for (int i = 0; i < 32; i++) homes.Add(new HomeProfile(10 + i, 5, 300));
        var familyTown = store.GetOrCreateFamilies(1, homes);
        foreach (var household in familyTown.Households)
            foreach (var resident in household.Residents)
                if (resident.AgeGroup == ResidentAgeGroup.Child)
                {
                    residentId = resident.Id;
                    return store;
                }
        throw new InvalidOperationException("Child fixture requires one generated child resident.");
    }

    private static NpcActionJob ActionJob(Fixture fixture, string itemId)
    {
        return new NpcActionJob
        {
            Id = fixture.ActionId, ProposalId = fixture.ActionId, ResidentId = fixture.ResidentId,
            Kind = (NpcActionKind)UseHealingItemValue, Target = HealingTarget("settlement:1", itemId),
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 2, UpdatedAt = 2,
            DeadlineAt = 100, ExpectedInventoryRevision = fixture.InventoryRevision
        };
    }

    private static NpcActionTarget HealingTarget(string worldSpace, string itemId)
    {
        return new NpcActionTarget
        {
            Kind = NpcActionTargetKind.WorldPoint, WorldSpaceId = worldSpace,
            X = 1, Y = 0, Z = 2, ItemId = itemId, Quantity = 1
        };
    }

    private static List<NpcInventoryItem> StackAfter()
    {
        return new List<NpcInventoryItem> { new NpcInventoryItem { Uid = PotionUid, NativePayload = "potion-stack:1" } };
    }

    private static bool Complete(Fixture fixture, long expectedInventoryRevision, long expectedVitalsRevision,
        int healthAfter, ulong sourceUid, IList<NpcInventoryItem> remaining, out int callbacks)
    {
        callbacks = 0;
        var method = typeof(WorldStateStore).GetMethod("TryCompleteNpcHealingItemAction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        True(method != null, "internal typed treatment completion API must exist");
        var outcomeType = typeof(WorldStateStore).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcTreatmentNativeOutcome");
        True(outcomeType != null, "runtime-only typed native outcome must exist");
        var factory = outcomeType.GetMethod("Verified", BindingFlags.Static | BindingFlags.NonPublic);
        True(factory != null, "native verification outcome factory must exist");
        var outcome = factory.Invoke(null, new object[]
        {
            sourceUid, fixture.HealthBefore, healthAfter,
            fixture.Store.ReadNpcVitals(fixture.ResidentId, NpcArchetype.Citizen).CurrentMagicka,
            remaining
        });

        var callbackCounter = new CallbackCounter();
        var callbackType = typeof(Func<>).MakeGenericType(outcomeType);
        var callbackBody = Expression.Block(
            Expression.Call(Expression.Constant(callbackCounter), typeof(CallbackCounter).GetMethod(nameof(CallbackCounter.Increment))),
            Expression.Constant(outcome, outcomeType));
        var callback = Expression.Lambda(callbackType, callbackBody).Compile();
        var action = fixture.Store.ReadNpcAction(fixture.ActionId);
        var result = method.Invoke(fixture.Store, new object[]
        {
            fixture.ActionId, action.Revision, 4d, NpcArchetype.Citizen,
            expectedVitalsRevision, expectedInventoryRevision, callback
        });
        callbacks = callbackCounter.Count;
        return (bool)result;
    }

    private static void AssertUnchanged(Fixture fixture)
    {
        var inventory = fixture.Store.ReadNpcInventory(fixture.ResidentId);
        Equal(fixture.InventoryRevision, inventory.Revision);
        Equal(null, inventory.LastTransactionId);
        Equal("potion-stack:2", inventory.Items[0].NativePayload);
        var vitals = fixture.Store.ReadNpcVitals(fixture.ResidentId, NpcArchetype.Citizen);
        Equal(fixture.VitalsRevision, vitals.Revision);
        Equal(fixture.HealthBefore, vitals.CurrentHealth);
        var action = fixture.Store.ReadNpcAction(fixture.ActionId);
        Equal(NpcActionPhase.Executing, action.Phase);
        Equal(null, TreatmentReceipt(action));
    }

    private static object TreatmentReceipt(NpcActionJob action)
    {
        return action.GetType().GetField("TreatmentReceipt", BindingFlags.Instance | BindingFlags.Public)?.GetValue(action);
    }

    private static object ReadReceipt(object receipt, string field)
    {
        var value = receipt.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public);
        True(value != null, "receipt field missing: " + field);
        return value.GetValue(receipt);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message = null)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException((message == null ? "Expected " + expected + ", got " + actual : message));
    }

    private sealed class CallbackCounter
    {
        public int Count;
        public void Increment() { Count++; }
    }

    private sealed class Fixture
    {
        public WorldStateStore Store;
        public string ResidentId, ActionId;
        public int HealthBefore;
        public long VitalsRevision, InventoryRevision;
    }
}
