using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private const string GoalId = NpcHealthNeedLifecycle.RecoveryGoalId;
    private static int Main()
    {
        var cases = new List<(string, Action)>
        {
            ("goal generation survives evidence updates and changes after fulfillment", GoalGenerationLifecycle),
            ("task binding survives updates and cancellation but never fulfills goal", BoundTaskLifecycle),
            ("stale or forged task generation is rejected", RejectStaleBinding),
            ("version 45 migration leaves tasks unbound", LegacyMigration),
            ("version 45 terminal goal receives conservative new anchor", LegacyTerminalMigration),
            ("observed cure preserves old task provenance across new injury", ObservedCureAndReinjury),
            ("save rejects missing historical generation", RejectTamperedSavedGeneration),
            ("save rejects mid-episode update as fabricated generation", RejectMidEpisodeGeneration),
            ("save rejects current goal rewound to an earlier lifetime", RejectCurrentGoalRewind),
            ("save rejects complete old goal row despite matching old receipt", RejectWholeGoalRowRewind),
            ("old task pins pruned true origin across a newer recovery generation", PrunedOriginTaskPin)
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Item2(); Console.WriteLine("PASS " + test.Item1); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL " + test.Item1 + ": " + e.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static WorldStateStore NewStore(out string owner)
    {
        var store = new WorldStateStore(7);
        owner = store.GetOrCreate(10, new[] { 20 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static NpcMotivationGoalView CommitGoal(WorldStateStore store, string owner, NpcMotivationGoalStatus status, double now)
    {
        var before = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        int maximum = before.MaximumHealth;
        int health = status == NpcMotivationGoalStatus.Fulfilled ? maximum :
            before.CurrentHealth == maximum || before.CurrentHealth > Math.Max(1, maximum / 4)
                ? Math.Max(1, maximum / 4) : Math.Max(1, maximum / 2);
        CaptureHealth(store, owner, health, now);
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, now), "native goal observation failed");
        Equal(status, store.ReadNpcMotivationState(owner).Goals[0].Status);
        return store.ReadNpcMotivationState(owner).Goals[0];
    }

    private static NpcTaskPlan Task(string owner, string id, double now, NpcMotivationGoalView goal)
    {
        var task = new NpcTaskPlan { Id = id, ProposalId = id, ResidentId = owner,
            AcceptedAt = now, UpdatedAt = now, DeadlineAt = now + 100,
            GoalId = goal == null ? null : goal.Id,
            GoalStartRevision = goal == null ? 0 : goal.GoalStartRevision };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
        return task;
    }

    private static void GoalGenerationLifecycle()
    {
        string owner; var store = NewStore(out owner);
        var active = CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 10);
        long first = active.GoalStartRevision;
        True(first > 0, "active goal needs generation");
        active = CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 11);
        Equal(first, active.GoalStartRevision);
        var fulfilled = CommitGoal(store, owner, NpcMotivationGoalStatus.Fulfilled, 12);
        Equal(first, fulfilled.GoalStartRevision);
        var newInjury = CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 13);
        True(newInjury.GoalStartRevision > first, "new injury needs new generation");
        var restored = new WorldStateStore(8); restored.Restore(store.Snapshot());
        Equal(newInjury.GoalStartRevision, restored.ReadNpcMotivationState(owner).Goals[0].GoalStartRevision);
    }

    private static void BoundTaskLifecycle()
    {
        string owner; var store = NewStore(out owner);
        var goal = CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 10);
        string reason;
        var bound = Task(owner, "task:bound", 11, goal);
        True(ClassifiedAdmissionFixture.TryAcceptTask(store, bound, true, out reason), reason);
        CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 12);
        var task = store.ReadNpcTask(bound.Id);
        Equal(goal.GoalStartRevision, task.GoalStartRevision);
        Equal(NpcMotivationGoalStatus.Active, store.ReadNpcMotivationState(owner).Goals[0].Status);
        True(store.CancelNpcTask(task.Id, task.Revision, 13, "External cure."), "cancel failed");
        CommitGoal(store, owner, NpcMotivationGoalStatus.Fulfilled, 14);
        CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 15);
        var restored = new WorldStateStore(8); restored.Restore(store.Snapshot());
        Equal(goal.GoalStartRevision, restored.ReadNpcTask(task.Id).GoalStartRevision);
        True(restored.ReadNpcMotivationState(owner).Goals[0].GoalStartRevision != goal.GoalStartRevision,
            "old task attached to new injury");
    }

    private static void RejectStaleBinding()
    {
        string owner; var store = NewStore(out owner);
        var goal = CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 10);
        CommitGoal(store, owner, NpcMotivationGoalStatus.Fulfilled, 11);
        CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 12);
        string reason;
        True(!ClassifiedAdmissionFixture.TryAcceptTask(store, Task(owner, "task:stale", 13, goal), true, out reason), "stale generation accepted");
        var current = store.ReadNpcMotivationState(owner).Goals[0];
        var forged = Task(owner, "task:forged", 13, current);
        forged.GoalStartRevision = 999;
        True(!ClassifiedAdmissionFixture.TryAcceptTask(store, forged, true, out reason), "forged generation accepted");
        Equal(0, store.Snapshot().NpcTasks.Count);
    }

    private static void LegacyMigration()
    {
        string owner; var store = NewStore(out owner);
        var goal = CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 10);
        string reason;
        var legacy = Task(owner, "task:legacy", 11, null);
        True(ClassifiedAdmissionFixture.TryAcceptTask(store, legacy, true, out reason), reason);
        var saved = store.Snapshot(); saved.Version = 45; saved.NpcMotivations[0].Version = 1;
        saved.ConversationCueArchive = null;
        saved.NpcExperienceInbox = NpcExperienceInbox.Create();
        saved.NpcExperienceInbox.Version = 4;
        saved.NpcTasks[0].Admission = null; saved.NpcConversationCommandCursors = null;
        saved.NpcMotivations[0].Goals[0].GoalStartRevision = 0;
        foreach (var receipt in saved.NpcMotivations[0].AcceptedUpdates)
        { receipt.GoalGenerationOrigin = false; receipt.GoalStartRevision = 0; }
        var restored = new WorldStateStore(8); restored.Restore(saved);
        Equal(0L, restored.ReadNpcTask(legacy.Id).GoalStartRevision);
        True(restored.ReadNpcMotivationState(owner).Goals[0].GoalStartRevision > 0, "migration lacks generation");
        True(goal.GoalStartRevision > 0, "source lacks generation");
    }

    private static void LegacyTerminalMigration()
    {
        string owner; var store = NewStore(out owner);
        CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 10);
        CommitGoal(store, owner, NpcMotivationGoalStatus.Fulfilled, 11);
        var saved = store.Snapshot(); saved.Version = 45; saved.NpcMotivations[0].Version = 1;
        saved.ConversationCueArchive = null;
        saved.NpcExperienceInbox = NpcExperienceInbox.Create();
        saved.NpcExperienceInbox.Version = 4;
        saved.NpcMotivations[0].Goals[0].GoalStartRevision = 0;
        foreach (var receipt in saved.NpcMotivations[0].AcceptedUpdates)
        { receipt.GoalGenerationOrigin = false; receipt.GoalStartRevision = 0; }
        var restored = new WorldStateStore(8); restored.Restore(saved);
        var terminal = restored.ReadNpcMotivationState(owner).Goals[0];
        Equal(NpcMotivationGoalStatus.Fulfilled, terminal.Status);
        Equal(terminal.Revision, terminal.GoalStartRevision);
        var newGoal = CommitGoal(restored, owner, NpcMotivationGoalStatus.Active, 12);
        True(newGoal.GoalStartRevision > terminal.GoalStartRevision, "new activation reused migrated terminal anchor");
    }

    private static void ObservedCureAndReinjury()
    {
        string owner; var store = NewStore(out owner);
        var original = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        int maximum = original.MaximumHealth;
        CaptureHealth(store, owner, Math.Max(1, maximum / 4), 100);
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, 100), "injury not observed");
        var goal = store.ReadNpcMotivationState(owner).Goals[0];
        string reason;
        var care = Task(owner, "task:care", 101, goal);
        True(ClassifiedAdmissionFixture.TryAcceptTask(store, care, true, out reason), reason);
        CaptureHealth(store, owner, Math.Max(1, maximum / 2), 110);
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, 110), "partial recovery not observed");
        Equal(goal.GoalStartRevision, store.ReadNpcMotivationState(owner).Goals[0].GoalStartRevision);
        CaptureHealth(store, owner, maximum, 120);
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, 120), "full cure not observed");
        Equal(NpcMotivationGoalStatus.Fulfilled, store.ReadNpcMotivationState(owner).Goals[0].Status);
        var task = store.ReadNpcTask(care.Id);
        True(store.CancelNpcTask(task.Id, task.Revision, 121, "No treatment needed after cure."), "cure cancellation failed");
        CaptureHealth(store, owner, Math.Max(1, maximum / 4), 130);
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, 130), "new injury not observed");
        var restored = new WorldStateStore(8); restored.Restore(store.Snapshot());
        Equal(goal.GoalStartRevision, restored.ReadNpcTask(care.Id).GoalStartRevision);
        True(restored.ReadNpcMotivationState(owner).Goals[0].GoalStartRevision > goal.GoalStartRevision,
            "new injury reused old goal generation");
    }

    private static void RejectTamperedSavedGeneration()
    {
        string owner; var store = NewStore(out owner);
        var goal = CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 10);
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptTask(store, Task(owner, "task:tampered", 11, goal), true, out reason), reason);
        var saved = store.Snapshot();
        saved.NpcTasks[0].GoalStartRevision = 999;
        bool rejected = false;
        try { new WorldStateStore(8).Restore(saved); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "unavailable historical generation loaded");
    }

    private static void RejectMidEpisodeGeneration()
    {
        string owner; var store = NewStore(out owner);
        var first = CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 10);
        var updated = CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 11);
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptTask(store, Task(owner, "task:mid-update", 12, first), true, out reason), reason);
        var saved = store.Snapshot();
        saved.NpcTasks[0].GoalStartRevision = updated.Revision;
        bool rejected = false;
        try { new WorldStateStore(8).Restore(saved); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "mid-episode goal update passed as generation origin");
    }

    private static void RejectCurrentGoalRewind()
    {
        string owner; var store = NewStore(out owner);
        var first = CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 10);
        CommitGoal(store, owner, NpcMotivationGoalStatus.Fulfilled, 11);
        CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 12);
        var saved = store.Snapshot();
        saved.NpcMotivations[0].Goals[0].GoalStartRevision = first.GoalStartRevision;
        bool rejected = false;
        try { new WorldStateStore(8).Restore(saved); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "current goal rewound to prior lifetime");
    }

    private static void CaptureHealth(WorldStateStore store, string owner, int health, double now)
    {
        var before = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        True(store.TryCaptureNpcVitals(owner, NpcArchetype.Citizen, health, before.CurrentMagicka, now, before.Revision),
            "native vitals capture failed");
    }

    private static void RejectWholeGoalRowRewind()
    {
        string owner; var store = NewStore(out owner);
        CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 10);
        var oldRow = store.Snapshot().NpcMotivations[0].Goals[0];
        CommitGoal(store, owner, NpcMotivationGoalStatus.Fulfilled, 11);
        CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 12);
        var saved = store.Snapshot();
        saved.NpcMotivations[0].Goals[0] = oldRow;
        bool rejected = false;
        try { new WorldStateStore(8).Restore(saved); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "entire obsolete goal row accepted as current state");
    }

    private static void PrunedOriginTaskPin()
    {
        string owner; var store = NewStore(out owner);
        var first = CommitGoal(store, owner, NpcMotivationGoalStatus.Active, 10);
        string reason;
        var oldPin = Task(owner, "task:old-pin", 11, first);
        True(ClassifiedAdmissionFixture.TryAcceptTask(store, oldPin, true, out reason), reason);
        var task = store.ReadNpcTask(oldPin.Id);
        True(store.CancelNpcTask(task.Id, task.Revision, 12, "Care episode ended."), "old task cancellation failed");

        double now = 20;
        for (int i = 0; i < 70; i++)
        {
            CommitGoal(store, owner, NpcMotivationGoalStatus.Fulfilled, now++);
            CommitGoal(store, owner, NpcMotivationGoalStatus.Active, now++);
        }
        var olderCurrentRow = store.Snapshot().NpcMotivations[0].Goals[0];
        CommitGoal(store, owner, NpcMotivationGoalStatus.Active, now++);
        var saved = store.Snapshot();
        var motivation = saved.NpcMotivations[0];
        True(motivation.ReceiptBaseRevision >= first.GoalStartRevision, "old origin still in receipt tail");
        True(!motivation.AcceptedUpdates.Exists(x => x.Revision == first.GoalStartRevision), "old origin receipt was not pruned");
        True(motivation.GoalOriginAnchors.Exists(x => x.Revision == first.GoalStartRevision && x.GoalGenerationOrigin),
            "task did not pin true origin anchor");
        True(motivation.Goals[0].GoalStartRevision > first.GoalStartRevision, "current goal did not enter a newer generation");
        var restored = new WorldStateStore(8); restored.Restore(saved);
        Equal(first.GoalStartRevision, restored.ReadNpcTask(task.Id).GoalStartRevision);
        Equal(motivation.Goals[0].GoalStartRevision, restored.ReadNpcMotivationState(owner).Goals[0].GoalStartRevision);

        var forgedOrigin = store.Snapshot();
        var anchor = forgedOrigin.NpcMotivations[0].GoalOriginAnchors.Find(x => x.Revision == first.GoalStartRevision);
        True(anchor != null, "missing old origin anchor");
        anchor.GoalGenerationOrigin = false;
        bool rejected = false;
        try { new WorldStateStore(8).Restore(forgedOrigin); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "forged continuation passed as pruned generation origin");

        var rewound = store.Snapshot();
        rewound.NpcMotivations[0].Goals[0] = olderCurrentRow;
        rejected = false;
        try { new WorldStateStore(8).Restore(rewound); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "obsolete current goal row passed after receipt compaction");
    }

    private static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("expected " + expected + ", got " + actual); }
}
