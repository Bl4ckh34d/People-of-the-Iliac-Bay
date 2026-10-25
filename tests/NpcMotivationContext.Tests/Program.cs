using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const string Branch = "branch:test";
    private const string Observer = "npc:observer";

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("projection preserves provenance and is deterministic", ProjectionPreservesProvenance),
            ("projection omits whole facts inside its character budget", ProjectionOmitsWholeFacts),
            ("speech claims cannot be promoted to known motivation facts", ClaimsStayClaims),
            ("unknown goal priority cannot leak into projection", UnknownGoalPriorityIsNeutral),
            ("snapshot rejects branch, observer, time and revision mismatches", SnapshotRejectsMismatchedEvidence),
            ("persistent motivation state accepts observed need and captures detached facts", StateAcceptsObservedNeed),
            ("claims and NPC intent cannot become verified health needs", StateRejectsClaimPromotionAndIntentAsNeed),
            ("state updates are CAS-bound, replay-safe and input-immutable", StateCasReplayAndRollback),
            ("correction requires newer native evidence and records game time", StateCorrectionRequiresFreshEvidence),
            ("capture binds the current branch but does not persist it or transient facts", StateCaptureIsBranchBoundAndTransient),
            ("authorized goal intent persists without claiming world completion", StatePersistsIntentWithoutWorldEffect),
            ("read-only view is detached from persisted state", StateReadOnlyViewIsDetached)
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
        }
        Console.WriteLine(cases.Count - failed + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void ProjectionPreservesProvenance()
    {
        var needB = KnownNeed("need:b", 4, "source:b");
        var needA = KnownNeed("need:a", 4, "source:a");
        var claim = new NpcMotivationNeed("need:claim", NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.AttributedClaim, 60, 3, Evidence("receipt:claim", NpcMotivationEvidenceKind.ConversationReceipt, 3));
        var snapshot = Capture(new[] { needB, needA, claim, UnknownNeed() });
        string first = NpcMotivationContextFormatter.ForModels(snapshot);
        string second = NpcMotivationContextFormatter.ForModels(snapshot);
        Equal(first, second);
        Contains(first, "state=attributedClaim revision=3 source=ConversationReceipt(receipt:claim,rev=3,t=8,observer=npc:observer)");
        Contains(first, "state=Unknown revision=1 source=none intensity=unknown");
        True(first.IndexOf("id=need:a", StringComparison.Ordinal) < first.IndexOf("id=need:b", StringComparison.Ordinal), "facts must have stable ID order");
        Contains(first, "not action authorization");
    }

    private static void ProjectionOmitsWholeFacts()
    {
        var longId = "need:" + new string('x', 120);
        var snapshot = Capture(new[] { KnownNeed(longId, 4, "source:long"), KnownNeed("need:a", 4, "source:a") });
        string projection = NpcMotivationContextFormatter.ForModels(snapshot, 1024);
        True(projection.Length <= 1024, "projection exceeded its budget");
        Contains(projection, "omittedFacts=needs=1");
        True(!projection.Contains(longId), "a whole fact should be omitted instead of truncating its ID or contents");
    }

    private static void ClaimsStayClaims()
    {
        Throws<ArgumentException>(() => new NpcMotivationNeed("need:forged", NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 60, 3, Evidence("receipt:claim", NpcMotivationEvidenceKind.ConversationReceipt, 3)));
        Throws<ArgumentException>(() => new NpcMotivationNeed("need:wrong-source", NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 60, 3, Evidence("authored:wrong", NpcMotivationEvidenceKind.AuthoredRecord, 3)));
    }

    private static void UnknownGoalPriorityIsNeutral()
    {
        Throws<ArgumentException>(() => new NpcMotivationGoal("goal:unknown", NpcMotivationGoalKind.Work,
            NpcMotivationGoalStatus.Unknown, NpcMotivationPriorityLevel.Emergency,
            NpcMotivationKnowledgeState.Unknown, 1, null));
        var goal = new NpcMotivationGoal("goal:unknown", NpcMotivationGoalKind.Work,
            NpcMotivationGoalStatus.Unknown, NpcMotivationPriorityLevel.Routine,
            NpcMotivationKnowledgeState.Unknown, 1, null);
        string text = NpcMotivationContextFormatter.ForModels(Capture(goals: new[] { goal }));
        Contains(text, "state=Unknown revision=1 source=none status=Unknown priority=unknown");
        True(!text.Contains("priority=Emergency"), "unknown priority must not survive as hidden context");
    }

    private static void SnapshotRejectsMismatchedEvidence()
    {
        Throws<ArgumentException>(() => Capture(new[] { new NpcMotivationNeed("need:branch", NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 50, 4, Evidence("source:branch", NpcMotivationEvidenceKind.NativeObservation, 4, branch: "branch:other")) }));
        Throws<ArgumentException>(() => Capture(new[] { new NpcMotivationNeed("need:observer", NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 50, 4, Evidence("source:observer", NpcMotivationEvidenceKind.NativeObservation, 4, observer: "npc:other")) }));
        Throws<ArgumentException>(() => Capture(new[] { new NpcMotivationNeed("need:future", NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 50, 4, Evidence("source:future", NpcMotivationEvidenceKind.NativeObservation, 4, time: 11)) }));
        Throws<ArgumentException>(() => new NpcMotivationNeed("need:revision", NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 50, 4, Evidence("source:revision", NpcMotivationEvidenceKind.NativeObservation, 3)));
    }

    private static void StateAcceptsObservedNeed()
    {
        var state = NpcMotivationState.Create(Observer);
        Equal(0, state.Needs.Count);
        Equal(0L, state.Revision);
        NpcMotivationStateUpdateProposal proposal = StateNeed(0, "operation:need-1", 1,
            NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.Known, 72,
            StateEvidence("observation:need-1", NpcMotivationStateEvidenceKind.NativeObservation, 8, 1));
        NpcMotivationState updated;
        True(NpcMotivationState.TryApply(state, proposal, out updated), "valid observation update should apply");
        Equal(1L, updated.Revision);
        Equal(1, updated.Needs.Count);
        Equal(72, updated.Needs[0].Intensity.Value);
        Equal(8L, updated.Needs[0].Revision);
        Equal(1, updated.AcceptedUpdates.Count);
        Equal(0L, state.Revision);
        Equal(0, state.AcceptedUpdates.Count);
        Equal(0, state.Needs.Count);
    }

    private static void StateRejectsClaimPromotionAndIntentAsNeed()
    {
        var claim = StateNeed(0, "operation:claim", 1, NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.AttributedClaim, 72,
            StateEvidence("receipt:claim", NpcMotivationStateEvidenceKind.ConversationReceipt, 1, 1));
        NpcMotivationState updated;
        True(NpcMotivationState.TryApply(NpcMotivationState.Create(Observer), claim, out updated), "a claim can be retained as a claim");
        Equal(NpcMotivationKnowledgeState.AttributedClaim, updated.Needs[0].KnowledgeState);
        var intentAsFact = StateNeed(0, "operation:intent-need", 1, NpcMotivationNeedKind.Health,
            NpcMotivationKnowledgeState.Known, 100,
            StateEvidence("decision:intent", NpcMotivationStateEvidenceKind.AuthorizedDecisionIntent, 1, 1));
        True(!NpcMotivationState.TryApply(NpcMotivationState.Create(Observer), intentAsFact, out updated), "own intent cannot verify health need");
        True(updated == null, "rejection must not publish state");
        var otherObserverEvidence = new NpcMotivationStateEvidence("npc:other", "observation:other", NpcMotivationStateEvidenceKind.NativeObservation, 1, 1);
        var misbound = StateNeed(0, "operation:misbound", 1, NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 50, otherObserverEvidence);
        True(!NpcMotivationState.TryApply(NpcMotivationState.Create(Observer), misbound, out updated), "another observer's evidence cannot be copied into this agenda");
        True(updated == null, "misbound evidence must not publish state");
    }

    private static void StateCasReplayAndRollback()
    {
        var original = NpcMotivationState.Create(Observer);
        var proposal = StateNeed(0, "operation:once", 1, NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 30,
            StateEvidence("observation:once", NpcMotivationStateEvidenceKind.NativeObservation, 1, 1));
        NpcMotivationState updated;
        True(NpcMotivationState.TryApply(original, proposal, out updated), "initial CAS should apply");
        True(!NpcMotivationState.TryApply(updated, proposal, out var replay), "operation replay should fail");
        True(replay == null, "replay must not publish output");
        var evidenceReplay = StateNeed(1, "operation:new-id-old-evidence", 2, NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 31, StateEvidence("observation:once", NpcMotivationStateEvidenceKind.NativeObservation, 1, 1));
        True(!NpcMotivationState.TryApply(updated, evidenceReplay, out var replayedEvidence), "same evidence cannot be replayed under a fresh operation ID");
        True(replayedEvidence == null, "evidence replay must not publish output");
        var stale = StateNeed(0, "operation:stale", 2, NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 40,
            StateEvidence("observation:stale", NpcMotivationStateEvidenceKind.NativeObservation, 2, 2));
        True(!NpcMotivationState.TryApply(updated, stale, out var rollback), "stale expected revision should fail");
        True(rollback == null, "stale update must not publish output");
        Equal(1L, updated.Revision);
        Equal(1, updated.AcceptedUpdates.Count);
    }

    private static void StateCorrectionRequiresFreshEvidence()
    {
        var state = NpcMotivationState.Create(Observer);
        NpcMotivationState.TryApply(state, StateNeed(0, "operation:first", 1, NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 60, StateEvidence("observation:first", NpcMotivationStateEvidenceKind.NativeObservation, 1, 1)), out state);
        var staleFact = StateNeed(1, "operation:correction-stale", 2, NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 20, StateEvidence("observation:old", NpcMotivationStateEvidenceKind.NativeObservation, 1, 1));
        True(!NpcMotivationState.TryApply(state, staleFact, out var unchanged), "correction cannot use old source revision/time");
        True(unchanged == null, "failed correction has no output");
        var corrected = StateNeed(1, "operation:correction", 2, NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 20, StateEvidence("observation:new", NpcMotivationStateEvidenceKind.NativeObservation, 2, 2));
        True(NpcMotivationState.TryApply(state, corrected, out var next), "fresh native correction should apply");
        Equal(20, next.Needs[0].Intensity.Value);
        Equal(2L, next.Needs[0].Revision);
        Equal(2d, next.Needs[0].UpdatedAtGameTime);
    }

    private static void StateCaptureIsBranchBoundAndTransient()
    {
        var state = NpcMotivationState.Create(Observer);
        NpcMotivationState.TryApply(state, StateNeed(0, "operation:claim", 1, NpcMotivationNeedKind.Social,
            NpcMotivationKnowledgeState.AttributedClaim, 55,
            StateEvidence("receipt:social", NpcMotivationStateEvidenceKind.ConversationReceipt, 1, 1)), out state);
        var clock = new NpcMotivationGameClock(3, 4, Evidence("clock", NpcMotivationEvidenceKind.WorldClock, 4, time: 3));
        var profile = new NpcMotivationProfileReference(Observer, 2, Evidence("profile", NpcMotivationEvidenceKind.PersonalityProfile, 2, time: 2));
        var relationship = new NpcMotivationRelationshipReference(Observer, "npc:target", 3,
            Evidence("relationship", NpcMotivationEvidenceKind.RelationshipLedger, 3, time: 2));
        var captured = NpcMotivationState.CaptureContext(state, Branch, clock, profile, relationship,
            new[] { new NpcMotivationCapabilityConstraint("temp:travel", NpcMotivationCapabilityKind.Travel,
                NpcMotivationAvailability.Unavailable, NpcMotivationKnowledgeState.Known, 1,
                Evidence("capability", NpcMotivationEvidenceKind.NativeObservation, 1, time: 2)) },
            null, null, null);
        Equal(Branch, captured.SaveBranchId);
        Equal(NpcMotivationKnowledgeState.AttributedClaim, captured.Needs[0].KnowledgeState);
        Equal(1, captured.Capabilities.Count);
        True(!StateJson(NpcMotivationState.CopyValidated(state)).Contains(Branch), "branch identity is transient and absent from saved state");
        var wrongBranchClock = new NpcMotivationGameClock(3, 4,
            new NpcMotivationEvidenceRef("branch:other", Observer, "clock", NpcMotivationEvidenceKind.WorldClock, 4, 3));
        Throws<ArgumentException>(() => NpcMotivationState.CaptureContext(state, Branch, wrongBranchClock, profile, relationship,
            null, null, null, null));
    }

    private static void StatePersistsIntentWithoutWorldEffect()
    {
        var state = NpcMotivationState.Create(Observer);
        var intent = NpcMotivationStateUpdateProposal.ForGoal(Observer, 0, "operation:intent", 1,
            "goal:help-family", NpcMotivationGoalKind.FamilyCare, NpcMotivationGoalStatus.Active,
            NpcMotivationPriorityLevel.Important, NpcMotivationKnowledgeState.Known,
            StateEvidence("decision:goal-intent", NpcMotivationStateEvidenceKind.AuthorizedDecisionIntent, 1, 1));
        True(NpcMotivationState.TryApply(state, intent, out var updated), "authorized active goal intent should persist");
        Equal(NpcMotivationGoalStatus.Active, updated.Goals[0].Status);
        var falseCompletion = NpcMotivationStateUpdateProposal.ForGoal(Observer, 1, "operation:fake-complete", 2,
            "goal:help-family", NpcMotivationGoalKind.FamilyCare, NpcMotivationGoalStatus.Fulfilled,
            NpcMotivationPriorityLevel.Important, NpcMotivationKnowledgeState.Known,
            StateEvidence("decision:fake-complete", NpcMotivationStateEvidenceKind.AuthorizedDecisionIntent, 2, 2));
        True(!NpcMotivationState.TryApply(updated, falseCompletion, out var unchanged), "decision intent cannot assert a completed external action");
        True(unchanged == null, "rejected completion must not publish state");
        Equal(NpcMotivationGoalStatus.Active, updated.Goals[0].Status);
    }

    private static void StateReadOnlyViewIsDetached()
    {
        var state = NpcMotivationState.Create(Observer);
        True(NpcMotivationState.TryApply(state, StateNeed(0, "operation:view", 1, NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Known, 30, StateEvidence("observation:view", NpcMotivationStateEvidenceKind.NativeObservation, 1, 1)), out var updated),
            "fixture update should apply");
        var view = updated.ReadOnlyView();
        True(((System.Collections.Generic.IList<NpcMotivationNeedView>)view.Needs).IsReadOnly, "view collection must be read-only");
        True(!typeof(NpcMotivationNeedView).GetProperty(nameof(NpcMotivationNeedView.Intensity)).SetMethod.IsPublic, "view rows must not expose public setters");
        Equal(30, updated.Needs[0].Intensity.Value);
    }

    private static NpcMotivationStateUpdateProposal StateNeed(long expected, string operation, double time,
        NpcMotivationNeedKind kind, NpcMotivationKnowledgeState knowledge, int? intensity, NpcMotivationStateEvidence evidence)
    {
        return NpcMotivationStateUpdateProposal.ForNeed(Observer, expected, operation, time,
            "need:persistent", kind, knowledge, intensity, evidence);
    }

    private static NpcMotivationStateEvidence StateEvidence(string id, NpcMotivationStateEvidenceKind kind, long revision, double time)
    {
        return new NpcMotivationStateEvidence(Observer, id, kind, revision, time);
    }

    private static string StateJson(NpcMotivationState state)
    {
        return System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }

    private static NpcMotivationContextSnapshot Capture(IEnumerable<NpcMotivationNeed> needs = null,
        IEnumerable<NpcMotivationGoal> goals = null)
    {
        var clock = new NpcMotivationGameClock(10, 2, Evidence("clock", NpcMotivationEvidenceKind.WorldClock, 2, time: 10));
        var profile = new NpcMotivationProfileReference(Observer, 3, Evidence("profile", NpcMotivationEvidenceKind.PersonalityProfile, 3));
        return NpcMotivationContextSnapshot.Capture(Branch, Observer, 5, clock, profile, null, needs, goals,
            null, null, null, null, null);
    }

    private static NpcMotivationNeed KnownNeed(string id, long revision, string evidenceId)
    {
        return new NpcMotivationNeed(id, NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.Known,
            50, revision, Evidence(evidenceId, NpcMotivationEvidenceKind.NativeObservation, revision));
    }

    private static NpcMotivationNeed UnknownNeed()
    {
        return new NpcMotivationNeed("need:unknown", NpcMotivationNeedKind.Rest,
            NpcMotivationKnowledgeState.Unknown, null, 1, null);
    }

    private static NpcMotivationEvidenceRef Evidence(string id, NpcMotivationEvidenceKind kind, long revision,
        string branch = Branch, string observer = Observer, double time = 8)
    {
        return new NpcMotivationEvidenceRef(branch, observer, id, kind, revision, time);
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Contains(string value, string expected)
    {
        if (!value.Contains(expected)) throw new InvalidOperationException("Missing expected text: " + expected);
    }
    private static void Equal(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal)) throw new InvalidOperationException("Values differ.");
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Values differ.");
    }
    private static void Equal(long expected, long actual)
    {
        if (expected != actual) throw new InvalidOperationException("Values differ.");
    }
    private static void Equal(double expected, double actual)
    {
        if (expected != actual) throw new InvalidOperationException("Values differ.");
    }
    private static void Throws<T>(Action body) where T : Exception
    {
        try { body(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
}
