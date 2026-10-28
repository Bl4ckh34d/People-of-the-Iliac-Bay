using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string, Action)>
        {
            ("valid care task is admitted and bound together", SuccessAndReload),
            ("bad care plan leaves task ledger unchanged", BadCarePlanLeavesNoTask),
            ("stale care revision and wrong episode leave task ledger unchanged", StaleCareLeavesNoTask),
            ("task admission rejection leaves care row unchanged", TaskRejectionLeavesCare),
            ("occupied task or action rejects atomic care without writes", OccupiedWorkRejects),
            ("blocked care retry admits only at exact retry boundary", BlockedRetryBoundary),
            ("duplicate atomic admission changes neither ledger", DuplicateAdmissionIsNoOp),
            ("current care marker survives restore and obsolete schema is rejected", CurrentMarkerAndObsoleteSchema),
            ("accepted treatment marker survives blocking but resets for a new episode", MarkerEpisodeLifecycle),
            ("fresh revision-one care cannot restore an accepted treatment marker", FreshMarkerRejected)
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

    private static WorldStateStore ReadyCare(out string owner, out NpcHealthCareRecord care)
    {
        var store = new WorldStateStore(42);
        owner = store.GetOrCreate(11, new[] { 23 }, 1).Households[0].Residents[0].Id;
        var baseline = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        int injured = Math.Max(1, (int)((long)baseline.MaximumHealth * 3 / 5));
        True(store.TryCaptureNpcVitals(owner, NpcArchetype.Citizen, injured, baseline.CurrentMagicka, 90, baseline.Revision),
            "injury capture failed");
        True(store.TryBeginNpcHealthCare(owner, NpcArchetype.Citizen, 100), "care did not begin");
        care = store.ReadNpcHealthCare(owner);
        return store;
    }

    private static NpcTaskPlan CareTask(string owner, NpcHealthCareRecord care, double now)
    {
        string id = NpcHealthCare.MakeTaskId(care);
        var task = new NpcTaskPlan { Id = id, ProposalId = id, ResidentId = owner,
            AcceptedAt = now, UpdatedAt = now, DeadlineAt = now + 900 };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.MoveTo,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint, WorldSpaceId = "settlement:11",
                SettlementId = 11, BuildingKey = -1, X = 3, Y = 0, Z = 4 } });
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.RestToHeal, DurationSeconds = 300,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 11,
                BuildingKey = 23, ContainerId = "bed:health-care:1", X = 1.25, Y = 0, Z = -2.5 } });
        return task;
    }

    private static void SuccessAndReload()
    {
        string owner; NpcHealthCareRecord care;
        var store = ReadyCare(out owner, out care);
        True(!care.HasAcceptedTreatment, "new care episode inherited accepted treatment");
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, 100), "recovery goal not observed");
        var goal = store.ReadNpcMotivationState(owner).Goals[0];
        var task = CareTask(owner, care, 101);
        task.GoalId = goal.Id; task.GoalStartRevision = goal.GoalStartRevision;
        string reason;
        True(store.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, task, 101, out reason), reason);
        Equal(NpcHealthCarePhase.Acting, store.ReadNpcHealthCare(owner).Phase);
        Equal(task.Id, store.ReadNpcHealthCare(owner).TaskId);
        True(store.ReadNpcHealthCare(owner).HasAcceptedTreatment, "atomic admission did not mark accepted treatment");
        Equal(NpcTaskPhase.Ready, store.ReadNpcTask(task.Id).Phase);
        var restored = new WorldStateStore(9); restored.Restore(store.Snapshot());
        Equal(task.Id, restored.ReadNpcHealthCare(owner).TaskId);
        True(restored.ReadNpcHealthCare(owner).HasAcceptedTreatment, "restore lost accepted treatment marker");
        Equal(task.Id, restored.ActiveNpcTask(owner).Id);
        Equal(goal.Id, restored.ReadNpcTask(task.Id).GoalId);
        Equal(goal.GoalStartRevision, restored.ReadNpcTask(task.Id).GoalStartRevision);
    }

    private static void BadCarePlanLeavesNoTask()
    {
        string owner; NpcHealthCareRecord care;
        var store = ReadyCare(out owner, out care);
        var task = CareTask(owner, care, 101);
        task.Steps.RemoveAt(1); // Valid task contract, but no treatment.
        string beforeTasks = TasksJson(store), beforeCare = CareJson(store);
        string reason;
        True(!store.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, task, 101, out reason), "movement-only care accepted");
        Equal(beforeTasks, TasksJson(store)); Equal(beforeCare, CareJson(store));
    }

    private static void StaleCareLeavesNoTask()
    {
        string owner; NpcHealthCareRecord care;
        var store = ReadyCare(out owner, out care);
        var task = CareTask(owner, care, 101);
        string beforeTasks = TasksJson(store), beforeCare = CareJson(store);
        string reason;
        True(!store.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision + 1, task, 101, out reason), "stale revision accepted");
        True(!store.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, task, 102, out reason), "time mismatch accepted");
        task.ResidentId = "other";
        True(!store.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, task, 101, out reason), "wrong owner accepted");
        task.ResidentId = owner;
        task.Id = "npc-health-care:wrong:attempt:1"; task.ProposalId = task.Id;
        True(!store.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, task, 101, out reason), "wrong episode accepted");
        Equal(beforeTasks, TasksJson(store)); Equal(beforeCare, CareJson(store));
    }

    private static void TaskRejectionLeavesCare()
    {
        string owner; NpcHealthCareRecord care;
        var store = ReadyCare(out owner, out care);
        var task = CareTask(owner, care, 101);
        task.GoalId = "goal:missing"; task.GoalStartRevision = 1;
        string beforeTasks = TasksJson(store), beforeCare = CareJson(store);
        string reason;
        True(!store.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, task, 101, out reason), "unavailable goal accepted");
        Equal(beforeTasks, TasksJson(store)); Equal(beforeCare, CareJson(store));
    }

    private static void DuplicateAdmissionIsNoOp()
    {
        string owner; NpcHealthCareRecord care;
        var store = ReadyCare(out owner, out care);
        var task = CareTask(owner, care, 101);
        string reason;
        True(store.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, task, 101, out reason), reason);
        string beforeTasks = TasksJson(store), beforeCare = CareJson(store);
        True(!store.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, task, 101, out reason), "duplicate admission accepted");
        Equal(beforeTasks, TasksJson(store)); Equal(beforeCare, CareJson(store));
    }

    private static void CurrentMarkerAndObsoleteSchema()
    {
        string owner; NpcHealthCareRecord care;
        var store = ReadyCare(out owner, out care);
        var task = CareTask(owner, care, 101);
        True(store.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, task, 101, out string reason), reason);
        var current = store.Snapshot();
        var currentRestored = new WorldStateStore(10);
        currentRestored.Restore(current);
        True(currentRestored.ReadNpcHealthCare(owner).HasAcceptedTreatment,
            "current restore lost accepted treatment marker");

        // Disposable development saves require the current schema. A copied
        // current marker cannot make an obsolete save eligible for restore.
        current.Version = 51;
        bool rejected = false;
        try { new WorldStateStore(11).Restore(current); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "obsolete save schema was accepted");
    }

    private static void MarkerEpisodeLifecycle()
    {
        string owner; NpcHealthCareRecord care;
        var store = ReadyCare(out owner, out care);
        var task = CareTask(owner, care, 101);
        True(store.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, task, 101, out string reason), reason);
        NpcTaskPlan accepted = store.ReadNpcTask(task.Id);
        True(store.FailNpcTask(accepted.Id, accepted.Revision, 102, "Treatment unavailable."),
            "fixture could not terminalize accepted care task");
        var acting = store.ReadNpcHealthCare(owner);
        True(store.TryBlockNpcHealthCare(owner, acting.Revision, 103, "Treatment unavailable."),
            "fixture could not block accepted care");
        var blocked = store.ReadNpcHealthCare(owner);
        True(blocked.HasAcceptedTreatment, "blocked care lost accepted treatment history");
        var blockedRestored = new WorldStateStore(12);
        blockedRestored.Restore(store.Snapshot());
        True(blockedRestored.ReadNpcHealthCare(owner).HasAcceptedTreatment,
            "blocked care restore lost accepted treatment history");

        var injured = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        True(store.TryCaptureNpcVitals(owner, NpcArchetype.Citizen, injured.MaximumHealth,
            injured.CurrentMagicka, 104, injured.Revision), "fixture full-health observation failed");
        True(store.TryCompleteNpcHealthCare(owner, blocked.Revision, NpcArchetype.Citizen, 104),
            "fixture care recovery failed");
        var recovered = store.ReadNpcHealthCare(owner);
        True(recovered.HasAcceptedTreatment, "recovered row lost episode treatment history");
        var full = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        int newInjury = Math.Max(1, (int)((long)full.MaximumHealth * 3 / 5));
        True(store.TryCaptureNpcVitals(owner, NpcArchetype.Citizen, newInjury,
            full.CurrentMagicka, 120, full.Revision), "fixture later injury observation failed");
        True(store.TryBeginNpcHealthCare(owner, NpcArchetype.Citizen, 120),
            "fixture new care episode failed");
        var next = store.ReadNpcHealthCare(owner);
        True(next.Id != recovered.Id && next.Revision == 1 && !next.HasAcceptedTreatment,
            "new care episode inherited prior accepted treatment");
    }

    private static void FreshMarkerRejected()
    {
        string owner; NpcHealthCareRecord care;
        var store = ReadyCare(out owner, out care);
        var invalid = store.Snapshot();
        invalid.NpcHealthCares[0].HasAcceptedTreatment = true;
        string before = CareJson(store);
        bool rejected = false;
        try { store.Restore(invalid); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "revision-one episode restored impossible accepted treatment");
        Equal(before, CareJson(store));
    }

    private static void OccupiedWorkRejects()
    {
        string owner; NpcHealthCareRecord care;
        var taskStore = ReadyCare(out owner, out care);
        var busyTask = new NpcTaskPlan { Id = "task:busy", ProposalId = "task:busy", ResidentId = owner,
            AcceptedAt = 101, UpdatedAt = 101, DeadlineAt = 500 };
        busyTask.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
        string reason;
        True(ClassifiedAdmissionFixture.TryAcceptTask(taskStore, busyTask, true, out reason), reason);
        var proposed = CareTask(owner, care, 102);
        string beforeTasks = TasksJson(taskStore), beforeCare = CareJson(taskStore);
        True(!taskStore.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, proposed, 102, out reason),
            "occupied task accepted");
        Equal(beforeTasks, TasksJson(taskStore)); Equal(beforeCare, CareJson(taskStore));

        var actionStore = ReadyCare(out owner, out care);
        var active = new NpcActionJob { Id = "action:busy", ProposalId = "action:busy", ResidentId = owner,
            Kind = NpcActionKind.Wait, Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" },
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 101, UpdatedAt = 101,
            DeadlineAt = 500, FinishAt = 111 };
        True(ClassifiedAdmissionFixture.TryAcceptAction(actionStore, active, true, true, true, out reason), reason);
        proposed = CareTask(owner, care, 102);
        beforeTasks = TasksJson(actionStore); beforeCare = CareJson(actionStore);
        True(!actionStore.TryAcceptAndBindNpcHealthCareTask(owner, care.Revision, proposed, 102, out reason),
            "occupied action accepted");
        Equal(beforeTasks, TasksJson(actionStore)); Equal(beforeCare, CareJson(actionStore));
    }

    private static void BlockedRetryBoundary()
    {
        string owner; NpcHealthCareRecord care;
        var store = ReadyCare(out owner, out care);
        True(store.TryBlockNpcHealthCare(owner, care.Revision, 101, "No treatment available."), "block failed");
        var blocked = store.ReadNpcHealthCare(owner);
        var early = CareTask(owner, blocked, blocked.RetryAt - 1);
        string beforeTasks = TasksJson(store), beforeCare = CareJson(store);
        string reason;
        True(!store.TryAcceptAndBindNpcHealthCareTask(owner, blocked.Revision, early, early.AcceptedAt, out reason),
            "early retry accepted");
        Equal(beforeTasks, TasksJson(store)); Equal(beforeCare, CareJson(store));
        var due = CareTask(owner, blocked, blocked.RetryAt);
        True(store.TryAcceptAndBindNpcHealthCareTask(owner, blocked.Revision, due, due.AcceptedAt, out reason), reason);
        Equal(NpcHealthCarePhase.Acting, store.ReadNpcHealthCare(owner).Phase);
        Equal(due.Id, store.ReadNpcHealthCare(owner).TaskId);
    }

    private static string TasksJson(WorldStateStore store)
    { return JsonSerializer.Serialize(store.Snapshot().NpcTasks, new JsonSerializerOptions { IncludeFields = true }); }
    private static string CareJson(WorldStateStore store)
    { return JsonSerializer.Serialize(store.Snapshot().NpcHealthCares, new JsonSerializerOptions { IncludeFields = true }); }
    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Expected " + expected + ", got " + actual); }
}
