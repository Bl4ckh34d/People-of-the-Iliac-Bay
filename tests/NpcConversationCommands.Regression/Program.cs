using System;
using System.Collections.Generic;
using System.IO;

using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var tests = new List<(string, Action)>
        {
            ("accepted command advances durable cursor and cannot replay", AcceptedAndReplay),
            ("failed admission leaves accepted cursor and task ledger unchanged", FailedAdmission),
            ("candidate mutation and wrong owner reject", CandidateMutationAndWrongOwner),
            ("one turn cannot issue a changed second candidate", ChangedSecondCandidate),
            ("same source binding cannot mint another ordinal", RepeatedBinding),
            ("new turn and close invalidate older callbacks", TurnInvalidation),
            ("closed turn cannot replay after the same person reopens", CloseAndReopen),
            ("restore invalidates live capabilities without losing ordinals", RestoreInvalidation),
            ("action admission uses the same source ordinal", ActionAdmission),
            ("production conversation routes use exact-turn wrappers", RuntimeSourceAudit)
        };
        int failed = 0;
        foreach (var test in tests)
        {
            try { test.Item2(); Console.WriteLine("PASS " + test.Item1); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL " + test.Item1 + ": " + e.Message); }
        }
        Console.WriteLine((tests.Count - failed) + "/" + tests.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static WorldStateStore NewStore(out string owner)
    {
        var store = new WorldStateStore(42);
        owner = store.GetOrCreate(11, new[] { 23 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static ConversationContextBinding Binding(string person, long revision, string sessionId = "session:command-test")
    {
        return new ConversationContextBinding(ConversationActorCategory.Mobile, person, null,
            sessionId, revision, 1, new string('a', 32), "", "scene:test", 1);
    }

    private static NpcTaskPlan Task(string person, string id, WorldStateStore store = null,
        NpcConversationCommandTurn turn = null)
    {
        var task = new NpcTaskPlan { Id = id, ProposalId = id, ResidentId = person,
            Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = 100, UpdatedAt = 100,
            DeadlineAt = 500 };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
        if (store == null) return task;
        True(store.TryPrepareNpcConversationTask(turn, task, out var prepared), "task preparation failed");
        return prepared;
    }

    private static NpcActionJob ActionJob(string person, string id)
    {
        return new NpcActionJob { Id = id, ProposalId = id, ResidentId = person,
            Kind = NpcActionKind.Wait, Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person,
                PersonId = "player" }, Phase = NpcActionPhase.Accepted, Revision = 1,
            AcceptedAt = 100, UpdatedAt = 100, DeadlineAt = 500, FinishAt = 110 };
    }

    private static NpcConversationCommandSession Session(WorldStateStore store, string person,
        string sessionId = "session:command-test")
    {
        True(store.TryBeginNpcConversationCommandSession(person, sessionId, out var session), "session missing");
        return session;
    }

    private static NpcConversationCommandTurn Turn(WorldStateStore store, NpcConversationCommandSession session,
        string person, long revision, string text)
    {
        True(store.TryCaptureNpcConversationCommandTurn(session, Binding(person, revision, session.SessionId),
            text, out var turn), "turn missing");
        return turn;
    }

    private static NpcConversationCommandCursor Cursor(WorldStateStore store, string person)
    {
        var cursor = store.Snapshot().NpcConversationCommandCursors.Find(x => x.PersonId == person);
        True(cursor != null, "cursor missing"); return cursor;
    }

    private static void AcceptedAndReplay()
    {
        string person; var store = NewStore(out person);
        var turn = Turn(store, Session(store, person), person, 1, "Wait here.");
        Equal(1L, turn.Ordinal);
        var task = Task(person, "task:one", store, turn);
        True(store.TryIssueNpcConversationTask(turn, task, out var capability), "issue failed");
        True(!store.TryIssueNpcConversationTask(turn, task, out _), "second issue accepted");
        string reason;
        True(store.TryAcceptNpcTaskFromConversation(capability, task, true, out reason), reason);
        Equal(1L, Cursor(store, person).LastIssuedOrdinal);
        Equal(1L, Cursor(store, person).LastAcceptedOrdinal);
        True(!store.TryAcceptNpcTaskFromConversation(capability, task, true, out reason), "replay accepted");
        Equal(1, store.Snapshot().NpcTasks.Count);
    }

    private static void FailedAdmission()
    {
        string person; var store = NewStore(out person);
        var turn = Turn(store, Session(store, person), person, 1, "Please wait.");
        var task = Task(person, "task:rejected", store, turn);
        True(store.TryIssueNpcConversationTask(turn, task, out var capability), "issue failed");
        string reason;
        True(!store.TryAcceptNpcTaskFromConversation(capability, task, false, out reason), "consent failure accepted");
        Equal(0L, Cursor(store, person).LastAcceptedOrdinal);
        Equal(0, store.Snapshot().NpcTasks.Count);
        True(store.TryAcceptNpcTaskFromConversation(capability, task, true, out reason), reason);
        Equal(1L, Cursor(store, person).LastAcceptedOrdinal);
    }

    private static void CandidateMutationAndWrongOwner()
    {
        string person; var store = NewStore(out person);
        var turn = Turn(store, Session(store, person), person, 1, "Wait here.");
        var task = Task(person, "task:mutable", store, turn);
        True(store.TryIssueNpcConversationTask(turn, task, out var capability), "issue failed");
        task.Steps[0].Target.PersonId = "someone-else";
        string reason;
        True(!store.TryAcceptNpcTaskFromConversation(capability, task, true, out reason), "mutated target accepted");
        Equal(0L, Cursor(store, person).LastAcceptedOrdinal);
        Equal(0, store.Snapshot().NpcTasks.Count);
        var other = new WorldStateStore(7); other.Restore(store.Snapshot());
        task.Steps[0].Target.PersonId = "player";
        True(!other.TryAcceptNpcTaskFromConversation(capability, task, true, out reason), "other store accepted token");
    }

    private static void ChangedSecondCandidate()
    {
        string person; var store = NewStore(out person);
        var turn = Turn(store, Session(store, person), person, 1, "Wait at this spot.");
        var first = Task(person, "task:first-choice", store, turn);
        var changed = Task(person, "task:changed-choice", store, turn);
        True(store.TryIssueNpcConversationTask(turn, first, out var capability), "first issue failed");
        True(!store.TryIssueNpcConversationTask(turn, changed, out _), "changed second candidate issued");
        string reason;
        True(!store.TryAcceptNpcTaskFromConversation(capability, changed, true, out reason),
            "changed candidate accepted with first capability");
        Equal(0L, Cursor(store, person).LastAcceptedOrdinal);
        True(store.TryAcceptNpcTaskFromConversation(capability, first, true, out reason), reason);
        Equal(1L, Cursor(store, person).LastAcceptedOrdinal);
    }

    private static void RepeatedBinding()
    {
        string person; var store = NewStore(out person);
        var session = Session(store, person);
        var first = Turn(store, session, person, 1, "Wait here.");
        var task = Task(person, "task:first-turn", store, first);
        True(store.TryIssueNpcConversationTask(first, task, out var capability), "issue failed");
        True(!store.TryCaptureNpcConversationCommandTurn(session, Binding(person, 1),
            "Different words, same old binding.", out _), "old binding recaptured");
        Equal(1L, Cursor(store, person).LastIssuedOrdinal);
        True(!store.TryIssueNpcConversationTask(first, Task(person, "task:fresh-id"), out _),
            "fresh ID issued from old turn");
        string reason;
        True(store.TryAcceptNpcTaskFromConversation(capability, task, true, out reason), reason);
        True(!store.TryCaptureNpcConversationCommandTurn(session, Binding(person, 1),
            "Wait here.", out _), "accepted binding recaptured");
        Equal(1L, Cursor(store, person).LastAcceptedOrdinal);
    }

    private static void TurnInvalidation()
    {
        string person; var store = NewStore(out person);
        var session = Session(store, person);
        var old = Turn(store, session, person, 1, "Wait here.");
        var task = Task(person, "task:old", store, old);
        True(store.TryIssueNpcConversationTask(old, task, out var oldCapability), "old issue failed");
        var current = Turn(store, session, person, 2, "No, wait there.");
        Equal(2L, current.Ordinal);
        string reason;
        True(!store.TryAcceptNpcTaskFromConversation(oldCapability, task, true, out reason), "older callback accepted");
        True(store.TryCloseNpcConversationCommandTurn(current), "close failed");
        True(!store.TryIssueNpcConversationTask(current, Task(person, "task:closed"), out _), "closed turn issued");
        Equal(0L, Cursor(store, person).LastAcceptedOrdinal);
    }

    private static void CloseAndReopen()
    {
        string person; var store = NewStore(out person);
        var session = Session(store, person);
        var old = Turn(store, session, person, 1, "Wait here.");
        var task = Task(person, "task:closed", store, old);
        True(store.TryIssueNpcConversationTask(old, task, out var stale), "old issue failed");
        True(store.TryCloseNpcConversationCommandSession(session), "close failed");
        True(!store.TryCaptureNpcConversationCommandTurn(session, Binding(person, 2),
            "Old callback after close.", out _), "closed session recaptured old callback");
        var newSession = Session(store, person, "session:reopened");
        var reopened = Turn(store, newSession, person, 2, "Wait here.");
        Equal(2L, reopened.Ordinal);
        True(old.TurnDigest != reopened.TurnDigest || old.Ordinal != reopened.Ordinal,
            "reopened turn reused source identity");
        string reason;
        True(!store.TryAcceptNpcTaskFromConversation(stale, task, true, out reason), "closed callback accepted");
        var current = Task(person, "task:reopened", store, reopened);
        True(store.TryIssueNpcConversationTask(reopened, current, out var live), "reopen issue failed");
        True(store.TryAcceptNpcTaskFromConversation(live, current, true, out reason), reason);
        Equal(2L, Cursor(store, person).LastAcceptedOrdinal);
    }

    private static void RestoreInvalidation()
    {
        string person; var store = NewStore(out person);
        var session = Session(store, person);
        var old = Turn(store, session, person, 1, "Wait here.");
        var task = Task(person, "task:stale", store, old);
        True(store.TryIssueNpcConversationTask(old, task, out var capability), "issue failed");
        var saved = store.Snapshot();
        store.Restore(saved);
        string reason;
        True(!store.TryAcceptNpcTaskFromConversation(capability, task, true, out reason), "restored capability accepted");
        True(!store.TryCaptureNpcConversationCommandTurn(session, Binding(person, 2),
            "Old callback after restore.", out _), "old session recaptured after restore");
        Equal(1L, Cursor(store, person).LastIssuedOrdinal);
        Equal(0L, Cursor(store, person).LastAcceptedOrdinal);
        var fresh = Turn(store, Session(store, person, "session:restored"), person, 2, "Now wait here.");
        Equal(2L, fresh.Ordinal);
    }

    private static void ActionAdmission()
    {
        string person; var store = NewStore(out person);
        var turn = Turn(store, Session(store, person), person, 1, "Wait here.");
        True(store.TryPrepareNpcConversationAction(turn, ActionJob(person, "action:one"), false,
            out var action), "action preparation failed");
        True(store.TryIssueNpcConversationAction(turn, action, out var capability), "action issue failed");
        string reason;
        True(!store.TryAcceptNpcActionFromConversation(capability, action, true, false, true, out reason), "adapter failure accepted");
        Equal(0L, Cursor(store, person).LastAcceptedOrdinal);
        True(store.TryAcceptNpcActionFromConversation(capability, action, true, true, true, out reason), reason);
        Equal(1L, Cursor(store, person).LastAcceptedOrdinal);
        Equal(1, store.Snapshot().NpcActions.Count);
    }

    private static void RuntimeSourceAudit()
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "mod", "Scripts"))) root = root.Parent;
        True(root != null, "repository source unavailable");
        string scripts = Path.Combine(root.FullName, "mod", "Scripts");
        int calls = 0;
        foreach (string path in Directory.GetFiles(scripts, "PeopleOfTheIliacBayMod*.cs"))
        {
            string source = File.ReadAllText(path);
            foreach (string call in new[]{"streets.TryAcceptConversationAction(","streets.TryAcceptConversationTask("})
            for(int start=source.IndexOf(call,StringComparison.Ordinal); start>=0;
                start=source.IndexOf(call,start+call.Length,StringComparison.Ordinal))
            {
                int end = source.IndexOf(';', start);
                True(end > start && end - start < 700, "unbounded conversation admission statement: " + path);
                string statement = source.Substring(start, end - start);
                True(statement.Contains("CommandTurnFor(snapshot)"), "legacy conversation admission: " + path);
                calls++;
            }
        }
        True(calls > 0, "no conversation admission call sites audited");
        string entry = File.ReadAllText(Path.Combine(scripts, "PeopleOfTheIliacBayMod.ConversationKnowledge.cs"));
        True(entry.Contains("CaptureConversationCommandTurn(requestSnapshot, playerText)"), "canonical capture missing");
        string lifecycle = File.ReadAllText(Path.Combine(scripts, "PeopleOfTheIliacBayMod.ContextSnapshot.cs"));
        True(lifecycle.Contains("BeginConversationCommandSession();") &&
            lifecycle.Contains("EndConversationCommandSession();"), "session lifecycle missing");
        string action = File.ReadAllText(Path.Combine(scripts, "StreetPopulation.Actions.cs"));
        True(action.Contains("TryIssueNpcConversationAction") && action.Contains("TryAcceptNpcActionFromConversation") &&
            action.Contains("TryIssueNpcConversationDeparture") && action.Contains("TryAcceptNpcDepartureFromConversation"),
            "action or departure wrapper missing");
        string task = File.ReadAllText(Path.Combine(scripts, "StreetPopulation.Actions.Tasks.cs"));
        True(task.Contains("TryIssueNpcConversationTask") && task.Contains("TryAcceptNpcTaskFromConversation"),
            "task wrapper missing");
    }

    private static void True(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("expected " + expected + ", got " + actual); }
}
