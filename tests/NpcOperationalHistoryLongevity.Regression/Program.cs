using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private const int FullCycles = 10001;
    private const int ShortCycles = 100;
    private const double FiveYearsSeconds = 5d * 365d * 24d * 60d * 60d;

    private sealed class MaintenanceMetrics
    {
        internal int Calls, PeakTaskRows, PeakActionRows;
        internal double TotalMilliseconds, LongestMilliseconds;
        internal void Record(double milliseconds)
        {
            Calls++;
            TotalMilliseconds += milliseconds;
            LongestMilliseconds = Math.Max(LongestMilliseconds, milliseconds);
        }
    }

    private static int Main(string[] args)
    {
        bool full = args.Length == 1 && args[0] == "--full";
        if (args.Length != 0 && !full)
        {
            Console.Error.WriteLine("Usage: NpcOperationalHistoryLongevity.Regression [--full]");
            return 2;
        }
        int failed = 0;
        Run("classified multi-year task/action turnover", () => MultiYearTurnover(full ? FullCycles : ShortCycles), ref failed);
        Run("live task step and undelivered failure remain pinned", PinnedDependencies, ref failed);
        Console.WriteLine((2 - failed) + "/2 passed" + (full ? " (full longevity)" : " (short diagnostic)"));
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test, ref int failed)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
    }

    private static WorldStateStore NewStore(out string resident)
    {
        var store = new WorldStateStore(31);
        resident = store.GetOrCreate(19, new[] { 7 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static NpcActionTarget WaitTarget()
    { return new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" }; }

    private static NpcTaskPlan Task(string resident, int number, double now)
    {
        string proposal = "longevity:task:" + number;
        var task = new NpcTaskPlan { Id = proposal, ProposalId = proposal, ResidentId = resident,
            Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = now, UpdatedAt = now,
            DeadlineAt = now + 100 };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, Target = WaitTarget(), DurationSeconds = 1 });
        return task;
    }

    private static NpcActionJob WaitAction(string resident, string proposal, double now)
    {
        return new NpcActionJob { Id = proposal, ProposalId = proposal, ResidentId = resident,
            Kind = NpcActionKind.Wait, Target = WaitTarget(), Phase = NpcActionPhase.Accepted,
            Revision = 1, AcceptedAt = now, UpdatedAt = now, DeadlineAt = now + 100,
            FinishAt = now + 1 };
    }

    private static string CompleteTask(WorldStateStore store, string resident, int number, double now)
    {
        var task = Task(resident, number, now);
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptTask(store, task, true, out reason), "task accept: " + reason);
        var step = new NpcActionJob { Id = NpcTaskContract.ActionId(task),
            ProposalId = NpcTaskContract.ActionId(task), ResidentId = resident,
            Kind = NpcActionKind.Wait, Target = WaitTarget(), Phase = NpcActionPhase.Accepted,
            Revision = 1, AcceptedAt = now + 1, UpdatedAt = now + 1,
            DeadlineAt = task.DeadlineAt, FinishAt = now + 2 };
        True(store.TryDispatchNpcTaskStep(task.Id, 1, now + 1, step, true, true, true, out reason),
            "task dispatch: " + reason);
        True(store.ObserveNpcAction(step.Id, 1, now + 1.25, NpcActionObservation.Arrival()), "task step arrival");
        True(store.ObserveNpcAction(step.Id, 2, now + 2.25,
            NpcActionObservation.Success(step.Id + ":observed")), "task step completion");
        True(store.ReconcileNpcTask(task.Id, 2, now + 2.25), "task reconciliation");
        Equal(NpcTaskPhase.Completed, store.ReadNpcTask(task.Id).Phase, "task completed");
        return task.Id;
    }

    private static string CompleteStandalone(WorldStateStore store, string resident, int number, double now)
    {
        var action = WaitAction(resident, "longevity:action:" + number, now);
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptAction(store, action, true, true, true, out reason),
            "action accept: " + reason);
        True(store.ObserveNpcAction(action.Id, 1, now + .25, NpcActionObservation.Arrival()),
            "standalone arrival");
        True(store.ObserveNpcAction(action.Id, 2, now + 1.25,
            NpcActionObservation.Success(action.Id + ":observed")), "standalone completion");
        Equal(NpcActionPhase.Completed, store.ReadNpcAction(action.Id).Phase, "action completed");
        return action.Id;
    }

    private static void Maintain(WorldStateStore store, double now, MaintenanceMetrics metrics = null)
    {
        var clock = Stopwatch.StartNew();
        var result = store.MaintainNpcOperationalHistory(now, 64, 32);
        clock.Stop();
        if (metrics != null) metrics.Record(clock.Elapsed.TotalMilliseconds);
        True(result.Examined <= 64, "maintenance exceeded examination budget");
        True(result.RemovedTaskGroups + result.RemovedStandaloneActions <= 32,
            "maintenance exceeded group budget");
    }

    private static void SampleRuntimeBudget(WorldStateStore store, double now, MaintenanceMetrics metrics)
    {
        var before = store.Snapshot();
        int calls = 0, fullValidationPasses = 0;
        double longest = 0, publicationMilliseconds = -1;
        while (calls < 512 && publicationMilliseconds < 0)
        {
            var clock = Stopwatch.StartNew();
            var result = store.MaintainNpcOperationalHistory(now, 4, 1);
            clock.Stop();
            double milliseconds = clock.Elapsed.TotalMilliseconds;
            metrics.Record(milliseconds);
            calls++;
            longest = Math.Max(longest, milliseconds);
            fullValidationPasses += result.FullValidationPasses;
            True(result.Examined <= 4 && result.RemovedTaskGroups + result.RemovedStandaloneActions <= 1,
                "runtime-size maintenance exceeded its budget");
            if (result.RemovedTaskGroups + result.RemovedStandaloneActions > 0)
                publicationMilliseconds = milliseconds;
        }
        True(publicationMilliseconds >= 0 && fullValidationPasses > 0,
            "runtime-size sample did not publish a settled group");
        Console.WriteLine("RUNTIME_SAMPLE retainedTasks=" + before.NpcTasks.Count +
            " retainedActions=" + before.NpcActions.Count + " calls=" + calls +
            " longestMs=" + Milliseconds(longest) +
            " publicationMs=" + Milliseconds(publicationMilliseconds) +
            " fullValidationPasses=" + fullValidationPasses);
    }

    private static string Milliseconds(double value)
    { return value.ToString("F3", CultureInfo.InvariantCulture); }

    private static void MultiYearTurnover(int cycles)
    {
        string resident; var store = NewStore(out resident);
        int acceptedTasks = 0, acceptedActions = 0;
        string firstTask = null, firstAction = null, latestAction = null;
        double cycleSpan = (FiveYearsSeconds + 100) / (cycles - 1);
        var totalClock = Stopwatch.StartNew();
        var metrics = new MaintenanceMetrics();
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            double now = 10 + cycle * cycleSpan;
            string taskId = CompleteTask(store, resident, cycle, now);
            if (cycle == 0) firstTask = taskId;
            acceptedTasks++; acceptedActions++;
            for (int j = 0; j < 9; j++)
            {
                latestAction = CompleteStandalone(store, resident, cycle * 9 + j, now + 4 + j * 4);
                if (cycle == 0 && j == 0) firstAction = latestAction;
                acceptedActions++;
            }
            var beforeMaintenance = store.Snapshot();
            metrics.PeakTaskRows = Math.Max(metrics.PeakTaskRows, beforeMaintenance.NpcTasks.Count);
            metrics.PeakActionRows = Math.Max(metrics.PeakActionRows, beforeMaintenance.NpcActions.Count);
            if (cycles == FullCycles && cycle == 500)
                SampleRuntimeBudget(store, now + 42, metrics);
            Maintain(store, now + 42, metrics);
            if ((cycle + 1) % (cycles == ShortCycles ? 25 : 1000) == 0 || cycle + 1 == cycles)
            {
                var saved = store.Snapshot();
                True(saved.NpcTasks.Count < 1000, "detailed task ledger reached old lifetime cap");
                True(saved.NpcActions.Count < 10000, "detailed action ledger reached old lifetime cap");
                Console.WriteLine("CHECK cycle=" + (cycle + 1) + " tasks=" + acceptedTasks +
                    " actions=" + acceptedActions + " retainedTasks=" + saved.NpcTasks.Count +
                    " retainedActions=" + saved.NpcActions.Count);
            }
        }
        Equal(cycles, acceptedTasks, "accepted task count");
        Equal(cycles * 10, acceptedActions, "accepted action count");
        True(cycles != FullCycles || acceptedTasks > 10000 && acceptedActions > 100000,
            "full run did not exceed both old caps by tenfold");
        True(10 + (cycles - 1) * cycleSpan + 42 > FiveYearsSeconds,
            "native game time did not exceed five years");
        True(store.ReadNpcTask(firstTask) == null, "old task detail was not retired");
        True(store.ReadNpcAction(firstAction) == null, "old standalone detail was not retired");
        Equal(latestAction, store.LatestNpcAction(resident).Id, "latest action changed after retention");

        var savedFinal = store.Snapshot();
        var restored = new WorldStateStore(32); restored.Restore(savedFinal);
        True(restored.ReadNpcTask(firstTask) == null && restored.ReadNpcAction(firstAction) == null,
            "evicted detail returned after restore");
        Equal(latestAction, restored.LatestNpcAction(resident).Id, "latest action lost on restore");
        double after = FiveYearsSeconds + 200;
        var sibling = new WorldStateStore(33); sibling.Restore(savedFinal);
        NpcConversationCommandSession session;
        True(restored.TryBeginNpcConversationCommandSession(resident, "longevity-branch-A", out session),
            "branch command session");
        var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, resident, null,
            "longevity-branch-A", 1, 1, restored.NpcFrameworkScopeId, "", "scene:longevity", 1);
        NpcConversationCommandTurn turn;
        True(restored.TryCaptureNpcConversationCommandTurn(session, binding, "Wait here.", out turn),
            "branch command turn");
        NpcActionJob prepared;
        True(restored.TryPrepareNpcConversationAction(turn,
            WaitAction(resident, "longevity:branch:A", after), false, out prepared),
            "branch action preparation");
        NpcConversationCommandCapability capability;
        True(restored.TryIssueNpcConversationAction(turn, prepared, out capability),
            "branch action issuance");
        string branchReason;
        True(!sibling.TryAcceptNpcActionFromConversation(capability, prepared, true, true, true, out branchReason),
            "sibling store accepted another branch's live capability");
        True(restored.TryAcceptNpcActionFromConversation(capability, prepared, true, true, true, out branchReason),
            "own branch rejected its live capability: " + branchReason);
        True(restored.TryCloseNpcConversationCommandSession(session), "branch session close");
        True(restored.ObserveNpcAction(prepared.Id, 1, after + .25, NpcActionObservation.Arrival()),
            "branch action arrival");
        True(restored.ObserveNpcAction(prepared.Id, 2, after + 1.25,
            NpcActionObservation.Success(prepared.Id + ":observed")), "branch action completion");
        string branchA = prepared.Id;
        string branchB = CompleteStandalone(sibling, resident, cycles * 9 + 1, after);
        True(branchA == branchB, "same saved source ordinal diverged between independent branches");
        True(restored.ReadNpcAction(firstAction) == null && sibling.ReadNpcAction(firstAction) == null,
            "old source replayed after branch restore");
        True(sibling.ReadNpcAction(branchB) != null && restored.ReadNpcAction(branchA) != null,
            "branch continuation lost its own action");
        totalClock.Stop();
        Console.WriteLine("LONGEVITY_METRICS cycles=" + cycles + " acceptedTasks=" + acceptedTasks +
            " acceptedActions=" + acceptedActions + " peakRetainedTasks=" + metrics.PeakTaskRows +
            " peakRetainedActions=" + metrics.PeakActionRows + " maintenanceCalls=" + metrics.Calls +
            " maintenanceTotalMs=" + Milliseconds(metrics.TotalMilliseconds) +
            " longestMaintenanceMs=" + Milliseconds(metrics.LongestMilliseconds) +
            " totalMs=" + Milliseconds(totalClock.Elapsed.TotalMilliseconds));
    }

    private static void PinnedDependencies()
    {
        string resident; var store = NewStore(out resident);
        var failed = Task(resident, 100001, 100);
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptTask(store, failed, true, out reason), reason);
        True(store.FailNpcTask(failed.Id, 1, 101, "Provider unavailable."), "failed task receipt not created");
        True(store.ReadNpcTask(failed.Id).FailureReceipt != null, "missing undelivered failure receipt");
        for (int i = 0; i < 12; i++)
        {
            double now = 200 + i * 200;
            CompleteTask(store, resident, i, now);
            Maintain(store, now + 42);
        }
        var active = Task(resident, 100002, 4000);
        True(ClassifiedAdmissionFixture.TryAcceptTask(store, active, true, out reason), reason);
        var step = new NpcActionJob { Id = NpcTaskContract.ActionId(active),
            ProposalId = NpcTaskContract.ActionId(active), ResidentId = resident,
            Kind = NpcActionKind.Wait, Target = WaitTarget(), Phase = NpcActionPhase.Accepted,
            Revision = 1, AcceptedAt = 4001, UpdatedAt = 4001,
            DeadlineAt = active.DeadlineAt, FinishAt = 4002 };
        True(store.TryDispatchNpcTaskStep(active.Id, 1, 4001, step, true, true, true, out reason), reason);
        for (int i = 0; i < 8; i++) Maintain(store, 4001 + 31d * 86400d + i);
        True(store.ReadNpcTask(failed.Id) != null, "undelivered failure was pruned");
        True(store.ReadNpcTask(active.Id) != null, "active task was pruned");
        True(store.ReadNpcAction(step.Id) != null, "active task-step action was pruned");
        var restored = new WorldStateStore(34); restored.Restore(store.Snapshot());
        True(restored.ReadNpcTask(failed.Id) != null && restored.ReadNpcTask(active.Id) != null &&
            restored.ReadNpcAction(step.Id) != null, "pinned dependencies did not survive restore");
    }

    private static void True(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Equal<T>(T expected, T actual, string message)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual); }
}
