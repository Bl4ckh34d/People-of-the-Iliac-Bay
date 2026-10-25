using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string, Action)>
        {
            ("rotating batch preserves recent terminal tasks", TaskWindow),
            ("standalone action window preserves latest action", ActionWindow),
            ("recent windows rank completion time, not append order", CompletionOrderWindows),
            ("pinned group and undelivered failure survive", Pins),
            ("task and dispatched step evict atomically", TaskStepGroup),
            ("pressure releases settled recent action but keeps latest", PressureAction),
            ("future-dated rows stay pinned under capacity pressure", FutureRows),
            ("changing first live action does not starve later history", RevisionChurnDoesNotStarve),
            ("evicted legacy proposal cannot reenter after restore", ReplayAfterRestore),
            ("consumed conversation capability cannot replay after eviction or restore", CapabilityReplay),
            ("failed validation preserves every live ledger and successful prune leaves snapshot detached", TransactionIsolation),
            ("malformed selected action cannot be erased from live state", MalformedSelectedAction)
        };
        int failed = 0;
        foreach (var entry in cases)
        {
            try { entry.Item2(); Console.WriteLine("PASS " + entry.Item1); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + entry.Item1 + ": " + error.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static WorldStateStore Store(out string person)
    {
        var store = new WorldStateStore(13);
        person = store.GetOrCreate(2, new[] { 9 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static NpcTaskPlan CancelledTask(string person, int ordinal)
    {
        string id = "legacy-task-" + ordinal;
        var task = new NpcTaskPlan { Id = id, ProposalId = id, ResidentId = person,
            Phase = NpcTaskPhase.Cancelled, Revision = 2, CurrentStepIndex = 0,
            FailureReason = "Cancelled after observation.", AcceptedAt = ordinal,
            UpdatedAt = ordinal + 1, DeadlineAt = ordinal + 100 };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
        return task;
    }

    private static NpcActionJob CancelledAction(string person, int ordinal)
    {
        string id = "legacy-action-" + ordinal;
        return new NpcActionJob { Id = id, ProposalId = id, ResidentId = person,
            Kind = NpcActionKind.Wait, Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" },
            Phase = NpcActionPhase.Cancelled, Revision = 2, FailureReason = "Cancelled after observation.",
            AcceptedAt = ordinal, UpdatedAt = ordinal + 1, DeadlineAt = ordinal + 100,
            FinishAt = ordinal + 10 };
    }

    private static void TaskWindow()
    {
        string person; var store = Store(out person); var save = store.Snapshot();
        for (int i = 1; i <= 12; i++) save.NpcTasks.Add(CancelledTask(person, i));
        store.Restore(save);
        var first = store.MaintainNpcOperationalHistory(10000000, 4, 4);
        Equal(4, first.Examined); Equal(4, first.RemovedTaskGroups);
        var after = store.Snapshot(); Equal(8, after.NpcTasks.Count);
        Equal("legacy-task-5", after.NpcTasks[0].Id);
        var second = store.MaintainNpcOperationalHistory(10000000, 4, 4);
        Equal(0, second.RemovedTaskGroups);
        var restored = new WorldStateStore(14); restored.Restore(store.Snapshot());
        Equal(8, restored.Snapshot().NpcTasks.Count);
    }

    private static void ActionWindow()
    {
        string person; var store = Store(out person); var save = store.Snapshot();
        for (int i = 1; i <= 35; i++) save.NpcActions.Add(CancelledAction(person, i));
        store.Restore(save);
        var result = store.MaintainNpcOperationalHistory(10000000, 8, 8);
        Equal(3, result.RemovedStandaloneActions);
        Equal(32, store.Snapshot().NpcActions.Count);
        Equal("legacy-action-35", store.LatestNpcAction(person).Id);
    }

    private static void Pins()
    {
        string person; var store = Store(out person); var save = store.Snapshot();
        for (int i = 1; i <= 12; i++) save.NpcTasks.Add(CancelledTask(person, i));
        var failed = save.NpcTasks[0]; failed.Phase = NpcTaskPhase.Failed;
        failed.FailureReceipt = new NpcTaskFailureReceipt { Id = failed.Id + ":failure", TaskId = failed.Id,
            Message = "Failed.", Observation = "Observed failure.", CreatedAt = failed.UpdatedAt };
        store.Restore(save);
        var result = store.MaintainNpcOperationalHistory(10000000, 8, 8);
        True(store.ReadNpcTask(failed.Id) != null, "undelivered failure pruned");
        Equal(3, result.RemovedTaskGroups);
        True(result.Pinned > 0, "pin not reported");
    }

    private static void CompletionOrderWindows()
    {
        string person; var store = Store(out person); var save = store.Snapshot();
        for (int i = 1; i <= 10; i++) save.NpcTasks.Add(CancelledTask(person, i));
        save.NpcTasks[0].UpdatedAt = 1000; save.NpcTasks[0].DeadlineAt = 1200;
        for (int i = 1; i <= 34; i++) save.NpcActions.Add(CancelledAction(person, i));
        save.NpcActions[0].UpdatedAt = 1000; save.NpcActions[0].DeadlineAt = 1200;
        store.Restore(save);
        for (int i = 0; i < 3; i++) store.MaintainNpcOperationalHistory(10000000, 64, 4);
        True(store.ReadNpcTask("legacy-task-1") != null, "late completion lost from recent task window");
        True(store.ReadNpcAction("legacy-action-1") != null, "late completion lost from recent action window");
        True(store.ReadNpcTask("legacy-task-2") == null, "older task completion survived window");
        True(store.ReadNpcAction("legacy-action-2") == null, "older action completion survived window");
    }

    private static void ReplayAfterRestore()
    {
        string person; var store = Store(out person); var save = store.Snapshot();
        for (int i = 1; i <= 12; i++) save.NpcTasks.Add(CancelledTask(person, i));
        store.Restore(save);
        store.MaintainNpcOperationalHistory(10000000, 8, 8, true);
        var restored = new WorldStateStore(15); restored.Restore(store.Snapshot());
        string reason;
        var replay = CancelledTask(person, 1); replay.Phase = NpcTaskPhase.Ready;
        replay.Revision = 1; replay.UpdatedAt = replay.AcceptedAt; replay.FailureReason = null;
        True(!restored.TryAcceptNpcTask(replay, true, out reason), "old unclassified proposal replayed");
        True(restored.ReadNpcTask("legacy-task-1") == null, "evicted row returned");
    }

    private static void TaskStepGroup()
    {
        string person; var store = Store(out person); var save = store.Snapshot();
        var task = CancelledTask(person, 1);
        task.ActiveActionId = task.Id + ":step:0";
        save.NpcTasks.Add(task);
        var step = CancelledAction(person, 1); step.Id = task.ActiveActionId; step.ProposalId = step.Id;
        save.NpcActions.Add(step);
        save.NpcActions.Add(CancelledAction(person, 2)); // Keeps the step from being LatestNpcAction.
        store.Restore(save);
        var result = store.MaintainNpcOperationalHistory(1000, 1, 1, true,
            NpcOperationalHistoryPressureTarget.Tasks);
        Equal(1, result.RemovedTaskGroups); Equal(1, result.RemovedStepActions);
        True(store.ReadNpcTask(task.Id) == null && store.ReadNpcAction(step.Id) == null,
            "group was not removed atomically");
        Equal("legacy-action-2", store.LatestNpcAction(person).Id);
        var restored = new WorldStateStore(14); restored.Restore(store.Snapshot());
        True(restored.ReadNpcAction(step.Id) == null, "step resurrected after restore");
    }

    private static void PressureAction()
    {
        string person; var store = Store(out person); var save = store.Snapshot();
        save.NpcActions.Add(CancelledAction(person, 1));
        save.NpcActions.Add(CancelledAction(person, 2));
        store.Restore(save);
        var result = store.MaintainNpcOperationalHistory(100, 1, 1, true,
            NpcOperationalHistoryPressureTarget.Actions);
        Equal(1, result.RemovedStandaloneActions);
        True(store.ReadNpcAction("legacy-action-1") == null, "settled pressure candidate survived");
        Equal("legacy-action-2", store.LatestNpcAction(person).Id);
    }

    private static void CapabilityReplay()
    {
        string person; var store = Store(out person);
        NpcConversationCommandSession session;
        True(store.TryBeginNpcConversationCommandSession(person, "retention-session", out session), "session failed");
        var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, person, null,
            "retention-session", 1, 1, store.NpcFrameworkScopeId, "", "scene:test", 1);
        NpcConversationCommandTurn turn;
        True(store.TryCaptureNpcConversationCommandTurn(session, binding, "Wait, please.", out turn), "turn failed");
        var proposed = CancelledTask(person, 1); proposed.Phase = NpcTaskPhase.Ready;
        proposed.Revision = 1; proposed.UpdatedAt = proposed.AcceptedAt; proposed.FailureReason = null;
        NpcTaskPlan prepared;
        True(store.TryPrepareNpcConversationTask(turn, proposed, out prepared), "prepare failed");
        NpcConversationCommandCapability capability;
        True(store.TryIssueNpcConversationTask(turn, prepared, out capability), "issue failed");
        string reason;
        True(store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason), reason);
        True(store.CancelNpcTask(prepared.Id, 1, 2, "Cancelled after acceptance."), "cancel failed");
        var result = store.MaintainNpcOperationalHistory(100, 1, 1, true,
            NpcOperationalHistoryPressureTarget.Tasks);
        Equal(1, result.RemovedTaskGroups);
        True(!store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason), "consumed capability replayed");
        var branch = new WorldStateStore(14); branch.Restore(store.Snapshot());
        True(!branch.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason), "old capability crossed restore");
        True(branch.ReadNpcTask(prepared.Id) == null, "evicted task restored");
    }

    private static void FutureRows()
    {
        string person; var store = Store(out person); var save = store.Snapshot();
        save.NpcTasks.Add(CancelledTask(person, 20));
        save.NpcActions.Add(CancelledAction(person, 20));
        save.NpcActions.Add(CancelledAction(person, 21));
        store.Restore(save);
        var tasks = store.MaintainNpcOperationalHistory(10, 1, 1, true,
            NpcOperationalHistoryPressureTarget.Tasks);
        Equal(0, tasks.RemovedTaskGroups); True(tasks.Pinned > 0, "future task not pinned");
        var actions = store.MaintainNpcOperationalHistory(10, 2, 1, true,
            NpcOperationalHistoryPressureTarget.Actions);
        Equal(0, actions.RemovedStandaloneActions);
        True(store.ReadNpcAction("legacy-action-20") != null, "future action pruned");
    }

    private static void RevisionChurnDoesNotStarve()
    {
        string person; var store = Store(out person); var save = store.Snapshot();
        var active = CancelledAction(person, 1); active.Phase = NpcActionPhase.Accepted;
        active.Revision = 1; active.FailureReason = null; active.UpdatedAt = active.AcceptedAt;
        active.DeadlineAt = 10000;
        save.NpcActions.Add(active);
        for (int i = 2; i <= 36; i++) save.NpcActions.Add(CancelledAction(person, i));
        store.Restore(save);
        bool pruned = false;
        for (int i = 0; i < 8; i++)
        {
            var current = store.ReadNpcAction(active.Id);
            True(store.ObserveNpcAction(active.Id, current.Revision, i + 2,
                i % 2 == 0 ? NpcActionObservation.Approach() : NpcActionObservation.Arrival()),
                "active action revision did not advance");
            var result = store.MaintainNpcOperationalHistory(10000000, 1, 1);
            if (result.RemovedStandaloneActions > 0) { pruned = true; break; }
        }
        True(pruned, "live first row starved old settled actions");
        True(store.ReadNpcAction(active.Id) != null, "active row pruned");
    }

    private static void TransactionIsolation()
    {
        string person; var store = Store(out person);
        var first = ObserveRecovery(store, person, false, 10);
        var withTask = store.Snapshot();
        var oldTask = CancelledTask(person, 1);
        oldTask.AcceptedAt = 11; oldTask.UpdatedAt = 12; oldTask.DeadlineAt = 111;
        oldTask.GoalId = first.Id; oldTask.GoalStartRevision = first.GoalStartRevision;
        withTask.NpcTasks.Add(oldTask);
        store.Restore(withTask);
        double now = 20;
        for (int cycle = 0; cycle < 70; cycle++)
        {
            ObserveRecovery(store, person, true, now++);
            ObserveRecovery(store, person, false, now++);
        }
        var seeded = store.Snapshot();
        for (int ordinal = 2; ordinal <= 12; ordinal++)
        {
            var newer = CancelledTask(person, ordinal);
            newer.AcceptedAt = 100 + ordinal; newer.UpdatedAt = 101 + ordinal;
            newer.DeadlineAt = 200 + ordinal;
            seeded.NpcTasks.Add(newer);
        }
        seeded.NpcActions.Add(CancelledAction(person, 1));
        seeded.NpcActions.Add(CancelledAction(person, 2));
        store.Restore(seeded);
        var before = store.Snapshot();
        True(before.NpcMotivations[0].GoalOriginAnchors.Exists(anchor => anchor.Revision == first.GoalStartRevision),
            "old task did not pin compact goal origin");

        var field = typeof(WorldStateStore).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
        True(field != null, "reflection test could not access live state");
        var live = (WorldState)field.GetValue(store);
        live.NpcKnowledge.Add(null); // Foreign-ledger fault, detected only by final world validation.
        var options = new JsonSerializerOptions { IncludeFields = true };
        string damagedBefore = JsonSerializer.Serialize(live, options);
        var rejected = store.MaintainNpcOperationalHistory(10000000, 4, 1);
        Equal(NpcOperationalHistoryPressure.ValidationRejected, rejected.Pressure);
        Equal(0, rejected.RemovedTaskGroups);
        Equal(damagedBefore, JsonSerializer.Serialize(live, options));
        True(ReferenceEquals(live, field.GetValue(store)), "failed transaction replaced live world");

        live.NpcKnowledge.RemoveAt(live.NpcKnowledge.Count - 1);
        string detachedBefore = JsonSerializer.Serialize(before, options);
        var published = store.MaintainNpcOperationalHistory(10000000, 4, 1);
        Equal(1, published.RemovedTaskGroups);
        Equal(1, published.FullValidationPasses);
        True(store.ReadNpcTask(oldTask.Id) == null, "old task survived published prune");
        True(!store.Snapshot().NpcMotivations[0].GoalOriginAnchors.Exists(
            anchor => anchor.Revision == first.GoalStartRevision), "orphan goal origin survived");
        Equal(detachedBefore, JsonSerializer.Serialize(before, options));
        True(before.NpcTasks.Exists(task => task.Id == oldTask.Id), "detached snapshot changed");
    }

    private static NpcMotivationGoalView ObserveRecovery(WorldStateStore store, string person,
        bool healed, double now)
    {
        var vitals = store.ReadNpcVitals(person, NpcArchetype.Citizen);
        int health = healed ? vitals.MaximumHealth : Math.Max(1, vitals.MaximumHealth / 4);
        True(store.TryCaptureNpcVitals(person, NpcArchetype.Citizen, health, vitals.CurrentMagicka,
            now, vitals.Revision), "native vitals capture failed");
        True(NpcHealthNeedLifecycle.Observe(store, person, NpcArchetype.Citizen, now),
            "native recovery observation failed");
        return store.ReadNpcMotivationState(person).Goals[0];
    }

    private static void MalformedSelectedAction()
    {
        string person; var store = Store(out person); var saved = store.Snapshot();
        saved.NpcActions.Add(CancelledAction(person, 1));
        saved.NpcActions.Add(CancelledAction(person, 2));
        store.Restore(saved);
        var field = typeof(WorldStateStore).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
        var live = (WorldState)field.GetValue(store);
        live.NpcActions[0].ProposalId = null; // Would disappear if deletion preceded payload validation.
        var options = new JsonSerializerOptions { IncludeFields = true };
        string before = JsonSerializer.Serialize(live, options);
        var result = store.MaintainNpcOperationalHistory(10000000, 1, 1, true,
            NpcOperationalHistoryPressureTarget.Actions);
        Equal(NpcOperationalHistoryPressure.ValidationRejected, result.Pressure);
        Equal(0, result.RemovedStandaloneActions);
        Equal(before, JsonSerializer.Serialize(live, options));
        True(ReferenceEquals(live, field.GetValue(store)), "invalid action changed live world owner");
    }

    private static void True(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Expected " + expected + ", got " + actual); }
}
