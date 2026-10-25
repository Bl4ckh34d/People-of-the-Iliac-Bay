using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static readonly JsonSerializerOptions SnapshotJson = new JsonSerializerOptions { IncludeFields = true };

    private static int Main()
    {
        var cases = new List<(string Name, Action Run)>
        {
            ("task capacity preserves pin and admits classified work", TaskPressureAdmits),
            ("action capacity preserves latest action and admits classified work", ActionPressureAdmits),
            ("all-pinned task capacity preserves state and retry capability", AllPinnedTaskCapacity),
            ("all-pinned action capacity preserves state and retry capability", AllPinnedActionCapacity)
        };
        int failures = 0;
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
        }
        Console.WriteLine((cases.Count - failures) + "/" + cases.Count + " passed");
        return failures == 0 ? 0 : 1;
    }

    private static void TaskPressureAdmits()
    {
        string person;
        var store = SeedTasks(1000, false, out person);
        var before = SnapshotBytes(store);
        string reason;
        True(!store.TryAcceptNpcTask(NewTask(person), true, out reason), "unclassified task entered at cap");
        Equal(before, SnapshotBytes(store));

        var turn = Turn(store, person);
        NpcTaskPlan prepared;
        True(store.TryPrepareNpcConversationTask(turn, NewTask(person), out prepared), "task preparation failed");
        var forged = NpcTaskContract.Copy(prepared);
        forged.Admission.SourceRevision++;
        NpcConversationCommandCapability rejected;
        before = SnapshotBytes(store);
        True(!store.TryIssueNpcConversationTask(turn, forged, out rejected), "forged source issued capability");
        Equal(before, SnapshotBytes(store));

        NpcConversationCommandCapability capability;
        True(store.TryIssueNpcConversationTask(turn, prepared, out capability), "task issuance failed");
        before = SnapshotBytes(store);
        True(!store.TryAcceptNpcTaskFromConversation(capability, prepared, false, out reason),
            "missing consent admitted task");
        Equal(before, SnapshotBytes(store));
        True(store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason), reason);
        var after = store.Snapshot();
        Equal(1000, after.NpcTasks.Count);
        True(store.ReadNpcTask("task:old:0") != null, "undelivered failure receipt was evicted");
        True(store.ReadNpcTask("task:old:1") == null, "settled task was not pruned");
        True(store.ReadNpcTask(prepared.Id) != null, "new task was not admitted");
        Equal(turn.Ordinal, after.NpcConversationCommandCursors[0].LastAcceptedOrdinal);
    }

    private static void ActionPressureAdmits()
    {
        string person;
        var store = SeedActions(10000, out person);
        var before = SnapshotBytes(store);
        string reason;
        True(!store.TryAcceptNpcAction(NewAction(person), true, true, true, out reason),
            "unclassified action entered at cap");
        Equal(before, SnapshotBytes(store));

        var turn = Turn(store, person);
        NpcActionJob prepared;
        True(store.TryPrepareNpcConversationAction(turn, NewAction(person), false, out prepared),
            "action preparation failed");
        var forged = NpcActionExecutor.Copy(prepared);
        forged.Admission.SourceRevision++;
        NpcConversationCommandCapability rejected;
        before = SnapshotBytes(store);
        True(!store.TryIssueNpcConversationAction(turn, forged, out rejected), "forged action issued capability");
        Equal(before, SnapshotBytes(store));

        NpcConversationCommandCapability capability;
        True(store.TryIssueNpcConversationAction(turn, prepared, out capability), "action issuance failed");
        before = SnapshotBytes(store);
        True(!store.TryAcceptNpcActionFromConversation(capability, prepared, true, false, true, out reason),
            "missing consent admitted action");
        Equal(before, SnapshotBytes(store));
        True(store.TryAcceptNpcActionFromConversation(capability, prepared, true, true, true, out reason), reason);
        var after = store.Snapshot();
        Equal(10000, after.NpcActions.Count);
        True(store.ReadNpcAction("action:old:9999") != null, "latest action was evicted");
        True(store.ReadNpcAction("action:old:0") == null, "settled action was not pruned");
        True(store.ReadNpcAction(prepared.Id) != null, "new action was not admitted");
        Equal(turn.Ordinal, after.NpcConversationCommandCursors[0].LastAcceptedOrdinal);
    }

    private static void AllPinnedTaskCapacity()
    {
        string person;
        var store = SeedTasks(1000, true, out person);
        var turn = Turn(store, person);
        NpcTaskPlan prepared;
        True(store.TryPrepareNpcConversationTask(turn, NewTask(person), out prepared), "task preparation failed");
        NpcConversationCommandCapability capability;
        True(store.TryIssueNpcConversationTask(turn, prepared, out capability), "task issuance failed");
        string before = SnapshotBytes(store);
        string reason;
        var clock = Stopwatch.StartNew();
        True(!store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason),
            "all-pinned task ledger admitted new work");
        clock.Stop();
        Console.WriteLine("ALL_PINNED_1000_MS=" + clock.ElapsedMilliseconds);
        Equal("The task ledger is full: retained work is pinned by unresolved dependencies.", reason);
        Equal(before, SnapshotBytes(store));
        Equal(0L, store.Snapshot().NpcConversationCommandCursors[0].LastAcceptedOrdinal);

        NpcTaskFailureReceipt receipt;
        True(store.TryDeliverNpcTaskFailureReceipt("task:old:0", 2, "day 1", "settlement 11", out receipt),
            "pinned receipt could not be delivered");
        True(receipt != null && receipt.Delivered, "failure receipt did not become removable");
        True(store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason),
            "unchanged capability could not retry after one pin released: " + reason);
        True(store.ReadNpcTask("task:old:0") == null, "released historical task was not pruned");
        True(store.ReadNpcTask(prepared.Id) != null, "retried task was not admitted");
        Equal(turn.Ordinal, store.Snapshot().NpcConversationCommandCursors[0].LastAcceptedOrdinal);
    }

    private static void AllPinnedActionCapacity()
    {
        string person;
        var store = SeedPinnedActions(10000, out person);
        var turn = Turn(store, person);
        NpcActionJob prepared;
        True(store.TryPrepareNpcConversationAction(turn, NewAction(person), false, out prepared),
            "action preparation failed");
        NpcConversationCommandCapability capability;
        True(store.TryIssueNpcConversationAction(turn, prepared, out capability), "action issuance failed");
        string before = SnapshotBytes(store);
        string reason;
        var clock = Stopwatch.StartNew();
        True(!store.TryAcceptNpcActionFromConversation(capability, prepared, true, true, true, out reason),
            "all-pinned action ledger admitted new work");
        clock.Stop();
        Console.WriteLine("ALL_PINNED_10000_MS=" + clock.ElapsedMilliseconds);
        Equal("The action ledger is full: retained work is pinned by unresolved dependencies.", reason);
        Equal(before, SnapshotBytes(store));
        Equal(0L, store.Snapshot().NpcConversationCommandCursors[0].LastAcceptedOrdinal);
        True(!store.TryAcceptNpcActionFromConversation(capability, prepared, true, true, true, out reason),
            "all-pinned retry unexpectedly admitted action");
        Equal("The action ledger is full: retained work is pinned by unresolved dependencies.", reason);
        Equal(before, SnapshotBytes(store));
    }

    private static WorldStateStore SeedTasks(int count, bool allPinned, out string person)
    {
        var baseStore = NewStore(out person);
        var saved = baseStore.Snapshot();
        for (int i = 0; i < count; i++) saved.NpcTasks.Add(OldTask(person, i, allPinned || i == 0));
        var restored = new WorldStateStore(99);
        restored.Restore(saved);
        Equal(count, restored.Snapshot().NpcTasks.Count);
        return restored;
    }

    private static WorldStateStore SeedActions(int count, out string person)
    {
        var baseStore = NewStore(out person);
        var saved = baseStore.Snapshot();
        for (int i = 0; i < count; i++) saved.NpcActions.Add(OldAction(person, i));
        var restored = new WorldStateStore(99);
        restored.Restore(saved);
        Equal(count, restored.Snapshot().NpcActions.Count);
        return restored;
    }

    private static WorldStateStore SeedPinnedActions(int count, out string person)
    {
        var baseStore = new WorldStateStore(42);
        var town = baseStore.GetOrCreate(11, new[] { 23, 24 }, 1);
        person = town.Households[0].Residents[0].Id;
        string target = town.Households[1].Residents[0].Id;
        var saved = baseStore.Snapshot();
        for (int i = 0; i < count; i++) saved.NpcActions.Add(PendingInteraction(person, target, i));
        var restored = new WorldStateStore(99);
        restored.Restore(saved);
        Equal(count, restored.Snapshot().NpcActions.Count);
        return restored;
    }

    private static WorldStateStore NewStore(out string person)
    {
        var store = new WorldStateStore(42);
        person = store.GetOrCreate(11, new[] { 23 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static NpcConversationCommandTurn Turn(WorldStateStore store, string person)
    {
        NpcConversationCommandSession session;
        True(store.TryBeginNpcConversationCommandSession(person, "session:capacity", out session), "session open failed");
        var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, person, null,
            "session:capacity", 1, 1, store.NpcFrameworkScopeId, "", "scene:capacity", 1);
        NpcConversationCommandTurn turn;
        True(store.TryCaptureNpcConversationCommandTurn(session, binding, "Help me now.", out turn),
            "turn capture failed");
        return turn;
    }

    private static NpcTaskPlan NewTask(string person)
    {
        var task = new NpcTaskPlan { Id = "task:new", ProposalId = "proposal:new", ResidentId = person,
            Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = 1000, UpdatedAt = 1000, DeadlineAt = 1500 };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
        return task;
    }

    private static NpcTaskPlan OldTask(string person, int index, bool pinned)
    {
        string id = "task:old:" + index;
        var task = new NpcTaskPlan { Id = id, ProposalId = "proposal:old:" + index,
            ResidentId = person, Phase = pinned ? NpcTaskPhase.Failed : NpcTaskPhase.Cancelled,
            Revision = 2, AcceptedAt = 100, UpdatedAt = 101, DeadlineAt = 500,
            FailureReason = "Earlier work stopped." };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
        if (pinned) task.FailureReceipt = new NpcTaskFailureReceipt { Id = id + ":failure",
            TaskId = id, CreatedAt = 101, Message = "Earlier work failed.", Observation = "Earlier work failed." };
        return task;
    }

    private static NpcActionJob NewAction(string person)
    {
        return new NpcActionJob { Id = "action:new", ProposalId = "proposal:new", ResidentId = person,
            Kind = NpcActionKind.Wait,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" },
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 1000, UpdatedAt = 1000,
            DeadlineAt = 1500, FinishAt = 1010 };
    }

    private static NpcActionJob OldAction(string person, int index)
    {
        return new NpcActionJob { Id = "action:old:" + index, ProposalId = "proposal:old:" + index,
            ResidentId = person, Kind = NpcActionKind.Wait,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" },
            Phase = NpcActionPhase.Cancelled, Revision = 2, AcceptedAt = 100, UpdatedAt = 101,
            DeadlineAt = 500, FinishAt = 110, FailureReason = "Earlier work stopped." };
    }

    private static NpcActionJob PendingInteraction(string person, string target, int index)
    {
        string id = "action:pending:" + index;
        return new NpcActionJob { Id = id, ProposalId = "proposal:pending:" + index,
            ResidentId = person, Kind = NpcActionKind.Interact,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = target },
            Phase = NpcActionPhase.Completed, Revision = 3, AcceptedAt = 100, UpdatedAt = 102,
            DeadlineAt = 500, CompletedEffectId = id + ":observed" };
    }

    private static string SnapshotBytes(WorldStateStore store)
    { return JsonSerializer.Serialize(store.Snapshot(), SnapshotJson); }

    private static void True(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("expected " + expected + ", got " + actual); }
}
