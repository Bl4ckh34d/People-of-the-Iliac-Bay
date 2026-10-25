using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static readonly JsonSerializerOptions SnapshotJson = new JsonSerializerOptions { IncludeFields = true };
    private static int Main()
    {
        var cases = new List<(string, Action)>
        {
            ("conversation source yields deterministic, separated IDs", ConversationIds),
            ("persisted source metadata validates strictly", AdmissionShape),
            ("typed task admission binds the exact live turn", ConversationTaskAdmission),
            ("ordinary task step still yields to accepted appointment", OrdinaryTaskYieldsToAppointment),
            ("unclassified task and conversation proposal cannot enter", RejectUnclassifiedAdmissions),
            ("generic action and departure gates reject valid and self-stamped proposals", RejectGenericActionAndDeparture),
            ("legacy saved work may advance after the new gate closes", ContinueLegacySavedWork),
            ("failed typed admission preserves cursor and permits exact retry", FailedAdmissionRetry),
            ("restore rejects forged conversation task source and cursor", RejectForgedConversationSource),
            ("restore rejects duplicate conversation source across task and action", RejectDuplicateConversationSource),
            ("restore rejects an action without its task-step source", RejectOrphanTaskStep)
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

    private static void ConversationIds()
    {
        string scope = new string('a', 32), person = "resident:test";
        string task = NpcOperationAdmission.ConversationTaskId(scope, person, 7);
        Equal(task, NpcOperationAdmission.ConversationTaskId(scope, person, 7));
        True(task != NpcOperationAdmission.ConversationTaskId(scope, person, 8), "reused another turn's task ID");
        True(task != NpcOperationAdmission.ConversationTaskId(scope, "resident:other", 7), "reused another person's task ID");
        True(task != NpcOperationAdmission.ConversationActionId(scope, person, 7), "task/action IDs collided");
    }

    private static void AdmissionShape()
    {
        var good = new NpcOperationAdmission { Kind = NpcOperationAdmissionKind.Conversation,
            SourceId = "resident:test", SourceRevision = 7, SourceDigest = new string('a', 64) };
        var copy = NpcOperationAdmission.CopyValidated(good);
        True(copy != good && copy.SourceDigest == good.SourceDigest, "admission copy is not detached");
        copy.SourceRevision = 8;
        Equal(7L, good.SourceRevision);
        var invalid = NpcOperationAdmission.CopyValidated(good);
        invalid.StepIndex = 1;
        Throws(() => NpcOperationAdmission.CopyValidated(invalid), "conversation accepted task-step index");
        invalid = NpcOperationAdmission.CopyValidated(good);
        invalid.SourceDigest = "not-a-turn-digest";
        Throws(() => NpcOperationAdmission.CopyValidated(invalid), "conversation accepted malformed source digest");
    }

    private static void ConversationTaskAdmission()
    {
        string person; var store = NewStore(out person);
        var turn = Turn(store, person, 1, out _);
        NpcTaskPlan prepared;
        True(store.TryPrepareNpcConversationTask(turn, Task(person), out prepared), "typed task preparation failed");
        Equal(NpcOperationAdmission.ConversationTaskId(store.NpcFrameworkScopeId, person, turn.Ordinal), prepared.Id);
        Equal(NpcOperationAdmissionKind.Conversation, prepared.Admission.Kind);
        Equal(person, prepared.Admission.SourceId);
        Equal(turn.Ordinal, prepared.Admission.SourceRevision);
        Equal(turn.TurnDigest, prepared.Admission.SourceDigest);
        NpcConversationCommandCapability capability;
        True(store.TryIssueNpcConversationTask(turn, prepared, out capability), "typed capability issue failed");
        string reason;
        True(store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason), reason);
        Equal(prepared.Id, store.ReadNpcTask(prepared.Id).Id);
        True(!store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason), "consumed source replayed");
        var restored = new WorldStateStore(99); restored.Restore(store.Snapshot());
        Equal(prepared.Id, restored.ReadNpcTask(prepared.Id).Id);
    }

    private static void OrdinaryTaskYieldsToAppointment()
    {
        string person; var store = NewStore(out person);
        True(store.TryReserveAppointment(new AppointmentRecord { Id = "ordinary-meeting", ResidentId = person,
            Purpose = "meet", StartsAt = 102, EndsAt = 200, GraceSeconds = 30, Importance = 1,
            Place = new AppointmentPlace { Kind = "building", SettlementId = 11, BuildingKey = 23 } },
            90, out _), "appointment reserved");
        var turn = Turn(store, person, 1, out _);
        NpcTaskPlan prepared;
        True(store.TryPrepareNpcConversationTask(turn, Task(person), out prepared), "task prepared");
        NpcConversationCommandCapability capability;
        True(store.TryIssueNpcConversationTask(turn, prepared, out capability), "task capability issued");
        True(store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out _), "ordinary task accepted");
        NpcTaskPlan ready = store.ActiveNpcTask(person);
        var action = new NpcActionJob { Id = ready.Id + ":step:0", ProposalId = ready.Id + ":step:0",
            ResidentId = person, Kind = NpcActionKind.Wait, Target = ready.Steps[0].Target,
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 101, UpdatedAt = 101,
            DeadlineAt = 500, FinishAt = 111 };
        True(!store.TryDispatchNpcTaskStep(ready.Id, ready.Revision, 101, action, true, true, true,
            out string reason) && reason == "An accepted appointment takes priority.",
            "ordinary task cannot bypass accepted appointment: " + reason);
    }

    private static void FailedAdmissionRetry()
    {
        string person; var store = NewStore(out person);
        var turn = Turn(store, person, 1, out _);
        NpcTaskPlan prepared;
        True(store.TryPrepareNpcConversationTask(turn, Task(person), out prepared), "typed task preparation failed");
        NpcConversationCommandCapability capability;
        True(store.TryIssueNpcConversationTask(turn, prepared, out capability), "typed capability issue failed");
        string reason;
        True(!store.TryAcceptNpcTaskFromConversation(capability, prepared, false, out reason), "consent failure admitted task");
        var afterFailure = store.Snapshot();
        Equal(0, afterFailure.NpcTasks.Count);
        Equal(0L, afterFailure.NpcConversationCommandCursors[0].LastAcceptedOrdinal);
        True(store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason), reason);
        Equal(turn.Ordinal, store.Snapshot().NpcConversationCommandCursors[0].LastAcceptedOrdinal);
    }

    private static void RejectUnclassifiedAdmissions()
    {
        string person; var store = NewStore(out person);
        string reason;
        True(!store.TryAcceptNpcTask(Task(person), true, out reason), "generic task admission stayed open");
        Equal(0, store.Snapshot().NpcTasks.Count);
        var turn = Turn(store, person, 1, out _);
        NpcTaskPlan stamped;
        True(store.TryPrepareNpcConversationTask(turn, Task(person), out stamped), "typed task preparation failed");
        True(!store.TryAcceptNpcTask(stamped, true, out reason), "source-stamped generic task entered");
        NpcConversationCommandCapability capability;
        True(!store.TryIssueNpcConversationTask(turn, Task(person), out capability),
            "unprepared conversation task acquired a capability");
        Equal(0L, store.Snapshot().NpcConversationCommandCursors[0].LastAcceptedOrdinal);
    }

    private static void RejectGenericActionAndDeparture()
    {
        string person; var store = NewStore(out person);
        var action = WaitAction(person);
        NpcActionExecutor.Validate(action);
        string before = JsonSerializer.Serialize(store.Snapshot(), SnapshotJson);
        string reason;
        True(!store.TryAcceptNpcAction(action, true, true, true, out reason),
            "valid unclassified action entered");

        var resident = store.GetOrCreate(11, new[] { 23 }, 1).Households[0].Residents[0];
        var departure = VisitDeparture.Create(person, 11, resident.HomeKey, 24, 100, 500,
            new List<GuardPoint3> { new GuardPoint3(), new GuardPoint3 { X = 2 } },
            new List<SavedRoutePoint> { new SavedRoutePoint { X = 3 }, new SavedRoutePoint { X = 5 } }, 1, 10);
        VisitDeparture.Copy(departure);
        var leaving = WaitAction(person);
        leaving.Kind = NpcActionKind.ChangeSchedule;
        leaving.Target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint,
            SettlementId = 11, BuildingKey = 24, X = 2 };
        leaving.FinishAt = 0;
        NpcActionExecutor.Validate(leaving);
        True(!store.TryAcceptNpcDeparture(leaving, departure, true, out reason),
            "valid unclassified departure entered");
        Equal(before, JsonSerializer.Serialize(store.Snapshot(), SnapshotJson));

        var turn = Turn(store, person, 1, out _);
        NpcActionJob stampedAction, stampedDeparture;
        True(store.TryPrepareNpcConversationAction(turn, action, false, out stampedAction),
            "typed action preparation failed");
        True(store.TryPrepareNpcConversationAction(turn, leaving, true, out stampedDeparture),
            "typed departure preparation failed");
        before = JsonSerializer.Serialize(store.Snapshot(), SnapshotJson);
        True(!store.TryAcceptNpcAction(stampedAction, true, true, true, out reason),
            "source-stamped generic action entered");
        True(!store.TryAcceptNpcDeparture(stampedDeparture, departure, true, out reason),
            "source-stamped generic departure entered");
        Equal(before, JsonSerializer.Serialize(store.Snapshot(), SnapshotJson));
    }

    private static void ContinueLegacySavedWork()
    {
        string person; var store = NewStore(out person);
        var saved = store.Snapshot();
        saved.Version = 47;
        saved.ConversationCueArchive = null;
        saved.NpcExperienceInbox = NpcExperienceInbox.Create();
        saved.NpcExperienceInbox.Version = 4;
        saved.NpcActions.Add(WaitAction(person));
        var restored = new WorldStateStore(99);
        restored.Restore(saved);
        var legacy = restored.ReadNpcAction("action:legacy");
        True(legacy != null && legacy.Admission == null, "legacy action did not restore");
        True(restored.ObserveNpcAction(legacy.Id, legacy.Revision, 101, NpcActionObservation.Arrival()),
            "legacy accepted action could not advance");
    }

    private static void RejectForgedConversationSource()
    {
        string person; var store = NewStore(out person);
        var turn = Turn(store, person, 1, out _);
        NpcTaskPlan prepared;
        True(store.TryPrepareNpcConversationTask(turn, Task(person), out prepared), "typed task preparation failed");
        NpcConversationCommandCapability capability;
        True(store.TryIssueNpcConversationTask(turn, prepared, out capability), "typed capability issue failed");
        string reason;
        True(store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason), reason);

        var wrongOwner = store.Snapshot();
        wrongOwner.NpcTasks[0].Admission.SourceId = "resident:other";
        RejectRestoreUnchanged(wrongOwner, "wrong command owner restored");

        var missingAcceptance = store.Snapshot();
        missingAcceptance.NpcConversationCommandCursors[0].LastAcceptedOrdinal = 0;
        RejectRestoreUnchanged(missingAcceptance, "unaccepted command restored");

        var wrongId = store.Snapshot();
        wrongId.NpcTasks[0].Id = "task:forged-source-id";
        RejectRestoreUnchanged(wrongId, "noncanonical conversation task ID restored");

        Equal(prepared.Id, store.ReadNpcTask(prepared.Id).Id);
    }

    private static void RejectOrphanTaskStep()
    {
        string person; var store = NewStore(out person);
        var saved = store.Snapshot();
        saved.NpcActions.Add(new NpcActionJob { Id = "task:absent:step:0", ProposalId = "task:absent:step:0",
            ResidentId = person, Kind = NpcActionKind.Wait,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" },
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 100, UpdatedAt = 100,
            DeadlineAt = 500, FinishAt = 110,
            Admission = new NpcOperationAdmission { Kind = NpcOperationAdmissionKind.TaskStep,
                SourceId = "task:absent", SourceRevision = 1, StepIndex = 0 } });
        RejectRestoreUnchanged(saved, "orphan task-step action restored");
    }

    private static void RejectDuplicateConversationSource()
    {
        string person; var store = NewStore(out person);
        var turn = Turn(store, person, 1, out _);
        NpcTaskPlan prepared;
        True(store.TryPrepareNpcConversationTask(turn, Task(person), out prepared), "typed task preparation failed");
        NpcConversationCommandCapability capability;
        True(store.TryIssueNpcConversationTask(turn, prepared, out capability), "typed capability issue failed");
        string reason;
        True(store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason), reason);
        True(store.CancelNpcTask(prepared.Id, 1, 101, "Cancelled."), "task cancellation failed");
        var saved = store.Snapshot();
        saved.NpcActions.Add(new NpcActionJob {
            Id = NpcOperationAdmission.ConversationActionId(saved.NpcFrameworkScopeId, person, turn.Ordinal),
            ProposalId = "proposal:duplicate-turn", ResidentId = person, Kind = NpcActionKind.Wait,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" },
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 102, UpdatedAt = 102,
            DeadlineAt = 500, FinishAt = 112,
            Admission = NpcOperationAdmission.CopyValidated(prepared.Admission)
        });
        RejectRestoreUnchanged(saved, "one accepted conversation source authorized two operations");
    }

    private static void RejectRestoreUnchanged(WorldState saved, string message)
    {
        string before = JsonSerializer.Serialize(saved, SnapshotJson);
        Throws(() => new WorldStateStore(99).Restore(saved), message);
        Equal(before, JsonSerializer.Serialize(saved, SnapshotJson));
    }

    private static WorldStateStore NewStore(out string person)
    {
        var store = new WorldStateStore(42);
        person = store.GetOrCreate(11, new[] { 23 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static NpcConversationCommandTurn Turn(WorldStateStore store, string person, long revision,
        out NpcConversationCommandSession session)
    {
        string sessionId = "session:admission-test";
        True(store.TryBeginNpcConversationCommandSession(person, sessionId, out session), "session open failed");
        var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, person, null,
            sessionId, revision, 1, new string('a', 32), "", "scene:test", 1);
        NpcConversationCommandTurn turn;
        True(store.TryCaptureNpcConversationCommandTurn(session, binding, "Wait here.", out turn), "turn capture failed");
        return turn;
    }

    private static NpcTaskPlan Task(string person)
    {
        var task = new NpcTaskPlan { Id = "legacy:temporary", ProposalId = "legacy:temporary", ResidentId = person,
            Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = 100, UpdatedAt = 100, DeadlineAt = 500 };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
        return task;
    }

    private static NpcActionJob WaitAction(string person)
    {
        return new NpcActionJob { Id = "action:legacy", ProposalId = "proposal:legacy", ResidentId = person,
            Kind = NpcActionKind.Wait,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" },
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 100, UpdatedAt = 100,
            DeadlineAt = 500, FinishAt = 110 };
    }

    private static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("expected " + expected + ", got " + actual); }
    private static void Throws(Action action, string message)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception(message); }
}
