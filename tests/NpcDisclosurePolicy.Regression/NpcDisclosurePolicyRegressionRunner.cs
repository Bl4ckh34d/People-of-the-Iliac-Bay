using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class NpcDisclosurePolicyRegressionRunner
{
    private static int assertions;
    private static void Equal<T>(T expected, T actual, string label)
    {
        assertions++;
        if (!Equals(expected, actual)) throw new InvalidOperationException(label + ": expected " + expected + ", got " + actual);
    }
    private static void True(bool value, string label)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(label);
    }

    private const string Actor = "npc-1";
    private const long SnapshotRevision = 12;
    private const double CapturedAt = 500;
    private static NpcDisclosureStatement Statement(string id, string proposition, string value,
        NpcDisclosureEpistemicKind kind, NpcDisclosureProtection protection = NpcDisclosureProtection.Public,
        string attribution = null, string actor = Actor, string source = "native-memory", long revision = 3, double time = 490)
    {
        return NpcDisclosureStatement.Create(id, actor, proposition, value, kind, protection,
            source, revision, time, attribution);
    }
    private static NpcDisclosureChoice Choice(string id, NpcDisclosureOutcome outcome, string[] statementIds,
        bool nativeEligible = true, bool deceptionIntent = false, long snapshotRevision = SnapshotRevision,
        string[] evidenceStatementIds = null, string audience = "player", string motiveId = null,
        string receiptId = null, string actor = Actor)
    {
        return NpcDisclosureChoice.Create(id, actor, outcome, statementIds, nativeEligible,
            deceptionIntent, "native-disclosure", 4, CapturedAt, snapshotRevision,
            evidenceStatementIds ?? statementIds, audience,
            motiveId ?? (outcome == NpcDisclosureOutcome.DeliberateDeception ? "native-motive" : null), receiptId ?? (id + "-receipt"));
    }
    private static NpcDisclosureRequest Request(NpcDisclosureStatement[] statements, NpcDisclosureChoice[] choices,
        string actor = Actor, long revision = SnapshotRevision, double capturedAt = CapturedAt,
        string audience = "player")
    {
        return NpcDisclosureRequest.Create(actor, revision, capturedAt, statements, choices, audience);
    }
    private static NpcDisclosureDecision Select(NpcDisclosureRequest request, string choiceId)
    {
        NpcDisclosureDecision decision;
        if (!NpcDisclosurePolicy.TrySelect(request, choiceId, out decision)) throw new InvalidOperationException("Expected eligible disclosure choice " + choiceId);
        return decision;
    }
    private static void Reject(NpcDisclosureRequest request, string choiceId, string label)
    {
        NpcDisclosureDecision decision;
        True(!NpcDisclosurePolicy.TrySelect(request, choiceId, out decision) && decision == null, label);
    }

    private static void Main()
    {
        var truth = NpcDisclosureStatement.Create("truth", Actor, "meeting-place", "east-gate",
            NpcDisclosureEpistemicKind.VerifiedTruth, NpcDisclosureProtection.Public,
            "native-memory", 3, 490, null, "truth-receipt");
        var secret = Statement("secret", "meeting-place", "east-gate", NpcDisclosureEpistemicKind.VerifiedTruth,
            NpcDisclosureProtection.QuestProtected);
        var belief = Statement("belief", "meeting-time", "dusk", NpcDisclosureEpistemicKind.AttributedBelief,
            attribution: "Mira");
        var rumor = Statement("rumor", "meeting-place", "west-gate", NpcDisclosureEpistemicKind.Rumor,
            attribution: "market-rumor");
        var playerClaim = Statement("player-claim", "meeting-place", "west-gate", NpcDisclosureEpistemicKind.PlayerClaim,
            attribution: "player");
        var unknown = Statement("unknown", "meeting-place", "north-gate", NpcDisclosureEpistemicKind.Unknown);

        var knownAnswer = Select(Request(new[] { truth }, new[] { Choice("answer", NpcDisclosureOutcome.TruthfulAnswer, new[] { "truth" }) }), "answer");
        Equal(NpcDisclosureOutcome.TruthfulAnswer, knownAnswer.RendererPayload.Outcome, "known answer outcome");
        Equal("east-gate", knownAnswer.RendererPayload.Statements[0].ValueId, "verified fact selected");

        var uncertainty = Select(Request(new[] { belief }, new[] { Choice("uncertain", NpcDisclosureOutcome.AttributedUncertainty, new[] { "belief" }) }), "uncertain");
        Equal(NpcDisclosureEpistemicKind.AttributedBelief, uncertainty.RendererPayload.Statements[0].EpistemicKind, "belief stays attributed");
        Equal("Mira", uncertainty.RendererPayload.Statements[0].AttributionId, "belief attribution preserved");
        var attributedClaims = Select(Request(new[] { rumor, playerClaim }, new[] {
            Choice("claims", NpcDisclosureOutcome.AttributedUncertainty, new[] { "rumor", "player-claim" }) }), "claims");
        Equal(NpcDisclosureEpistemicKind.Rumor, attributedClaims.RendererPayload.Statements[0].EpistemicKind, "rumor remains rumor");
        Equal(NpcDisclosureEpistemicKind.PlayerClaim, attributedClaims.RendererPayload.Statements[1].EpistemicKind, "player speech remains attributed claim");

        var partial = Select(Request(new[] { truth, secret, belief }, new[] {
            Choice("partial", NpcDisclosureOutcome.PartialAnswer, new[] { "truth", "belief" }) }), "partial");
        Equal(2, partial.RendererPayload.Statements.Count, "partial answer contains selected facts only");
        True(!Contains(partial, "secret"), "unselected protected truth withheld");

        var refusal = Select(Request(new[] { truth }, new[] { Choice("refuse", NpcDisclosureOutcome.Refusal, Array.Empty<string>()) }), "refuse");
        Equal(0, refusal.RendererPayload.Statements.Count, "refusal sends no facts to renderer");

        var lieClaim = Statement("lie", "meeting-place", "west-gate", NpcDisclosureEpistemicKind.DeliberateClaim);
        var validLie = Select(Request(new[] { truth, lieClaim }, new[] {
            Choice("lie-choice", NpcDisclosureOutcome.DeliberateDeception, new[] { "lie" }, deceptionIntent: true,
                evidenceStatementIds: new[] { "lie", "truth" }, motiveId: "protect-family", receiptId: "utterance-9") }), "lie-choice");
        Equal(1, validLie.RendererPayload.Statements.Count, "deception sends proposed claim only");
        True(!Contains(validLie, "truth"), "deception output excludes conflicting truth");
        Equal("player", validLie.Trace.AudienceId, "decision retains audience");
        Equal(Actor, validLie.Trace.ActorId, "decision trace binds the speaking actor");
        Equal("native-disclosure", validLie.Trace.DecisionSourceId, "decision retains source trace");
        Equal(4L, validLie.Trace.DecisionSourceRevision, "decision retains source revision");
        Equal("protect-family", validLie.Trace.MotiveId, "decision retains typed motive key");
        Equal("utterance-9", validLie.RendererPayload.UtteranceReceiptId, "renderer receives receipt for accepted statement");
        Equal("utterance-9", validLie.Trace.UtteranceReceiptId, "trace retains utterance receipt");
        True(ContainsReferences(validLie.Trace.EvidenceReferences, "truth"), "decision traces actor-known conflict evidence");
        Equal(Actor, validLie.Trace.EvidenceReferences[1].ActorId, "evidence reference remains speaker-bound");
        Equal("native-memory", validLie.Trace.EvidenceReferences[1].SourceId, "evidence reference retains its native source");
        Equal(3L, validLie.Trace.EvidenceReferences[1].SourceRevision, "evidence reference retains source revision");
        Equal("truth-receipt", validLie.Trace.EvidenceReferences[1].EvidenceReceiptId, "native receipt remains in audit trace");
        True(typeof(NpcDisclosureRendererStatement).GetProperty("EvidenceReceiptId") == null &&
            typeof(NpcDisclosureRendererStatement).GetProperty("SourceId") == null,
            "renderer statement shape omits evidence metadata");
        True(!Contains(validLie, "truth"), "conflict evidence id does not expose withheld truth fact");

        Reject(Request(new[] { truth, lieClaim }, new[] {
            Choice("no-intent", NpcDisclosureOutcome.DeliberateDeception, new[] { "lie" }) }), "no-intent", "deception requires permitted native intent");
        Reject(Request(new[] { truth, lieClaim }, new[] {
            Choice("not-eligible", NpcDisclosureOutcome.DeliberateDeception, new[] { "lie" }, nativeEligible: false, deceptionIntent: true) }), "not-eligible", "deception requires native eligibility");
        Reject(Request(new[] { lieClaim }, new[] {
            Choice("unknown-truth", NpcDisclosureOutcome.DeliberateDeception, new[] { "lie" }, deceptionIntent: true) }), "unknown-truth", "unknown truth cannot authorize deception");
        Reject(Request(new[] { rumor, lieClaim }, new[] {
            Choice("rumor-only", NpcDisclosureOutcome.DeliberateDeception, new[] { "lie" }, deceptionIntent: true) }), "rumor-only", "rumor cannot authorize deception");
        Reject(Request(new[] { playerClaim, lieClaim }, new[] {
            Choice("player-only", NpcDisclosureOutcome.DeliberateDeception, new[] { "lie" }, deceptionIntent: true) }), "player-only", "player claim cannot authorize deception");
        Reject(Request(new[] { truth, lieClaim }, new[] {
            Choice("unbound-conflict", NpcDisclosureOutcome.DeliberateDeception, new[] { "lie" }, deceptionIntent: true) }), "unbound-conflict", "conflicting knowledge must be explicitly evidence-bound");
        var sameClaim = Statement("same", "meeting-place", "east-gate", NpcDisclosureEpistemicKind.DeliberateClaim);
        Reject(Request(new[] { truth, sameClaim }, new[] {
            Choice("same-value", NpcDisclosureOutcome.DeliberateDeception, new[] { "same" }, deceptionIntent: true) }), "same-value", "a non-conflicting claim cannot be labeled a lie");
        Reject(Request(new[] { secret, lieClaim }, new[] {
            Choice("protected-conflict", NpcDisclosureOutcome.DeliberateDeception, new[] { "lie" }, deceptionIntent: true) }), "protected-conflict", "protected truth cannot authorize or leak through deception");
        Reject(Request(new[] { secret }, new[] {
            Choice("protected-answer", NpcDisclosureOutcome.TruthfulAnswer, new[] { "secret" }) }), "protected-answer", "protected fact cannot reach renderer");
        Reject(Request(new[] { unknown }, new[] {
            Choice("unknown-answer", NpcDisclosureOutcome.TruthfulAnswer, new[] { "unknown" }) }), "unknown-answer", "unknown fact cannot be asserted as truth");
        Reject(Request(new[] { belief }, new[] {
            Choice("wrong-kind", NpcDisclosureOutcome.TruthfulAnswer, new[] { "belief" }) }), "wrong-kind", "belief cannot be asserted as verified truth");
        Reject(Request(new[] { truth }, new[] {
            Choice("stale", NpcDisclosureOutcome.TruthfulAnswer, new[] { "truth" }, snapshotRevision: SnapshotRevision - 1) }), "stale", "stale choice revision rejected");
        Reject(Request(new[] { truth }, new[] {
            Choice("wrong-audience", NpcDisclosureOutcome.TruthfulAnswer, new[] { "truth" }, audience: "guard") }), "wrong-audience", "choice cannot change its captured audience");
        Reject(Request(new[] { truth }, new[] {
            Choice("wrong-speaker", NpcDisclosureOutcome.TruthfulAnswer, new[] { "truth" }, actor: "other") }), "wrong-speaker", "choice cannot borrow another actor's knowledge");
        Reject(Request(new[] { truth }, new[] {
            Choice("unknown-evidence", NpcDisclosureOutcome.TruthfulAnswer, new[] { "truth" }, evidenceStatementIds: new[] { "absent" }) }), "unknown-evidence", "unbound evidence reference rejected");
        Reject(Request(new[] { Statement("foreign", "x", "y", NpcDisclosureEpistemicKind.VerifiedTruth, actor: "other") }, new[] {
            Choice("foreign-choice", NpcDisclosureOutcome.TruthfulAnswer, new[] { "foreign" }) }), "foreign-choice", "foreign actor knowledge rejected");

        var selected = Select(Request(new[] { truth, belief }, new[] {
            Choice("copy", NpcDisclosureOutcome.PartialAnswer, new[] { "truth" }) }), "copy");
        True(!ReferenceEquals(truth, selected.RendererPayload.Statements[0]), "renderer facts detached from source snapshot");
        var originalFacts = new List<NpcDisclosureStatement> { truth };
        var detachedRequest = Request(originalFacts.ToArray(), new[] { Choice("detach", NpcDisclosureOutcome.TruthfulAnswer, new[] { "truth" }) });
        originalFacts.Clear();
        Equal(1, Select(detachedRequest, "detach").RendererPayload.Statements.Count, "request copies its source collection");
        Reject(Request(new[] { truth }, new[] { Choice("missing-fact", NpcDisclosureOutcome.TruthfulAnswer, new[] { "missing" }) }), "missing-fact", "unknown statement id rejected");
        Reject(Request(new[] { truth }, new[] {
            Choice("duplicate-choice", NpcDisclosureOutcome.Refusal, Array.Empty<string>(), receiptId: "duplicate-receipt-a"),
            Choice("duplicate-choice", NpcDisclosureOutcome.Refusal, Array.Empty<string>(), receiptId: "duplicate-receipt-b") }), "duplicate-choice", "ambiguous choice id rejected");
        ThrowsArgumentException(() => Request(new[] { truth }, new[] {
            Choice("receipt-a", NpcDisclosureOutcome.Refusal, Array.Empty<string>(), receiptId: "same-receipt"),
            Choice("receipt-b", NpcDisclosureOutcome.Refusal, Array.Empty<string>(), receiptId: "same-receipt") }), "duplicate receipt identity rejected");

        Console.WriteLine("Production-linked disclosure policy regressions passed: " + assertions + " assertions.");
    }

    private static bool Contains(NpcDisclosureDecision decision, string id)
    {
        for (int i = 0; i < decision.RendererPayload.Statements.Count; i++) if (decision.RendererPayload.Statements[i].Id == id) return true;
        return false;
    }
    private static bool ContainsReferences(IReadOnlyList<NpcDisclosureEvidenceReference> values, string id)
    {
        for (int i = 0; i < values.Count; i++) if (values[i].StatementId == id) return true;
        return false;
    }
    private static void ThrowsArgumentException(Action action, string label)
    {
        assertions++;
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException(label);
    }
}

