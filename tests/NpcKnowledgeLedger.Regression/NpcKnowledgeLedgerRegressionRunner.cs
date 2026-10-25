using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class NpcKnowledgeLedgerRegressionRunner
{
    private static int assertions;
    private const string Actor = "npc-1";
    private const string Epoch = "save-epoch-a";
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
    private static void ThrowsArgumentException(Action action, string label)
    {
        assertions++;
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException(label);
    }
    private static NpcKnowledgeUpdate Native(string receipt, string topic, string value, double time,
        NpcDisclosureProtection protection = NpcDisclosureProtection.Public, long sourceRevision = 2)
    {
        return NpcKnowledgeUpdate.FromNativeEvidence(Actor, "fact-" + receipt, topic, value, protection,
            NpcKnowledgeEvidenceOrigin.NativeObservation, "native-senses", receipt, sourceRevision, time, receipt);
    }
    private static NpcKnowledgeUpdate Claim(string receipt, string topic, string value, double time,
        NpcDisclosureEpistemicKind kind = NpcDisclosureEpistemicKind.PlayerClaim, string attribution = "player")
    {
        return NpcKnowledgeUpdate.FromAttributedClaim(Actor, "claim-" + receipt, topic, value, kind,
            NpcDisclosureProtection.Public, NpcKnowledgeEvidenceOrigin.ConversationReceipt,
            "conversation", receipt, 1, time, attribution, receipt);
    }
    private static NpcKnowledgeState Apply(NpcKnowledgeState state, NpcKnowledgeUpdate update,
        long revision, string actor = Actor, string epoch = Epoch)
    {
        NpcKnowledgeState next;
        if (!NpcKnowledgeLedger.TryApply(state, update, actor, epoch, revision, out next))
            throw new InvalidOperationException("Expected accepted knowledge update " + update.UpdateReceiptId);
        return next;
    }
    private static NpcKnowledgeSnapshot Snapshot(NpcKnowledgeState state, string actor = Actor, string epoch = Epoch)
    {
        NpcKnowledgeSnapshot snapshot;
        if (!NpcKnowledgeLedger.TryCapture(state, actor, epoch, out snapshot))
            throw new InvalidOperationException("Expected actor-owned snapshot");
        return snapshot;
    }
    private static void Reject(NpcKnowledgeState state, NpcKnowledgeUpdate update, long revision,
        string label, string actor = Actor, string epoch = Epoch)
    {
        NpcKnowledgeState next;
        True(!NpcKnowledgeLedger.TryApply(state, update, actor, epoch, revision, out next) && next == null, label);
    }

    private static void Main()
    {
        NpcKnowledgeState state = NpcKnowledgeLedger.Create(Actor, Epoch);
        state = Apply(state, Native("receipt-1", "safe-location", "east-gate", 100), 0);
        Equal(1L, state.Revision, "update advances ledger revision once");
        True(Snapshot(state).TryGet("safe-location", out NpcKnowledgeFactView fact), "actor can query own known fact");
        Equal(NpcDisclosureEpistemicKind.VerifiedTruth, fact.Statement.EpistemicKind, "native evidence creates verified truth");
        Equal("east-gate", fact.Statement.ValueId, "bounded exact value preserved");
        Equal("receipt-1", fact.UpdateReceiptId, "ledger update receipt retained for replay protection");
        Equal("receipt-1", fact.EvidenceReceiptId, "native source receipt retained");
        Equal("receipt-1", fact.Statement.EvidenceReceiptId, "native evidence receipt survives disclosure projection");
        Equal(2L, fact.Statement.SourceRevision, "native source revision retained");
        Equal(100d, fact.Statement.CapturedGameTime, "source capture time retained");

        state = Apply(state, Claim("receipt-2", "suspect", "merchant", 101), 1);
        True(Snapshot(state).TryGet("suspect", out NpcKnowledgeFactView claim), "actor can query attributed claim");
        Equal(NpcDisclosureEpistemicKind.PlayerClaim, claim.Statement.EpistemicKind, "player claim remains attributed speech");
        Equal("player", claim.Statement.AttributionId, "claim source is retained");
        Equal("receipt-2", claim.Statement.EvidenceReceiptId, "attributed claim receipt remains attached");
        True(!Snapshot(state).TryGet("unseen-topic", out NpcKnowledgeFactView missing), "missing topic means unknown");
        NpcKnowledgeSnapshot foreignSnapshot;
        True(!NpcKnowledgeLedger.TryCapture(state, "npc-2", Epoch, out foreignSnapshot) && foreignSnapshot == null,
            "foreign actor cannot query another ledger");

        state = Apply(state, Native("receipt-secret", "quest-door", "moon-key", 102,
            NpcDisclosureProtection.QuestProtected), 2);
        True(Snapshot(state).TryGet("quest-door", out NpcKnowledgeFactView protectedFact), "actor retains protected knowledge internally");
        var protectedChoice = NpcDisclosureChoice.Create("protected", Actor, NpcDisclosureOutcome.TruthfulAnswer,
            new[] { protectedFact.Statement.Id }, true, false, "native-dialogue", 1, 102, state.Revision,
            new[] { protectedFact.Statement.Id }, "player", null, "utterance-secret");
        var protectedRequest = NpcDisclosureRequest.Create(Actor, state.Revision, 102,
            new[] { protectedFact.Statement }, new[] { protectedChoice }, "player");
        NpcDisclosureDecision protectedDecision;
        True(!NpcDisclosurePolicy.TrySelect(protectedRequest, "protected", out protectedDecision),
            "existing disclosure policy withholds protected ledger fact");

        state = Apply(state, NpcKnowledgeUpdate.FromNativeEvidence(Actor, "authored-fact", "royal-lineage", "known-by-record",
            NpcDisclosureProtection.Confidential, NpcKnowledgeEvidenceOrigin.AuthoredRecord,
            "authored-quest-record", "authored-update-1", 1, 102.5, null), 3);
        True(Snapshot(state).TryGet("royal-lineage", out NpcKnowledgeFactView authoredFact), "authored record remains queryable by its actor");
        Equal(null, authoredFact.Statement.EvidenceReceiptId, "authored fact does not receive a fabricated evidence receipt");

        NpcKnowledgeState beforeCorrection = state;
        state = Apply(state, Native("receipt-3", "safe-location", "north-gate", 103), 4);
        True(Snapshot(state).TryGet("safe-location", out NpcKnowledgeFactView corrected), "correction becomes current actor knowledge");
        Equal("north-gate", corrected.Statement.ValueId, "correction replaces prior value");
        Equal("receipt-1", corrected.SupersedesUpdateReceiptId, "correction links prior ledger update receipt");
        True(Snapshot(beforeCorrection).TryGet("safe-location", out NpcKnowledgeFactView unchanged) &&
            unchanged.Statement.ValueId == "east-gate", "update leaves prior snapshot unchanged");
        Reject(state, Native("receipt-1", "safe-location", "east-gate", 104), 5, "old receipt replay rejected after correction");
        Reject(state, Native("receipt-4", "safe-location", "south-gate", 105), 4, "stale expected revision rejected");
        Reject(state, Native("receipt-5", "safe-location", "south-gate", 102), 5, "stale game time rejected");
        Reject(state, Native("receipt-6", "safe-location", "south-gate", 106), 5, "foreign save epoch rejected", epoch: "save-epoch-b");
        Reject(state, Native("receipt-7", "safe-location", "south-gate", 106), 5, "foreign actor rejected", actor: "npc-2");

        ThrowsArgumentException(() => NpcKnowledgeUpdate.FromAttributedClaim(Actor, "bad-kind", "secret", "false",
            NpcDisclosureEpistemicKind.VerifiedTruth, NpcDisclosureProtection.Public,
            NpcKnowledgeEvidenceOrigin.ConversationReceipt, "conversation", "speech-1", 1, 107, "player"),
            "conversation claim cannot become verified truth");
        ThrowsArgumentException(() => NpcKnowledgeUpdate.FromNativeEvidence(Actor, "bad-origin", "claim", "value",
            NpcDisclosureProtection.Public, NpcKnowledgeEvidenceOrigin.ConversationReceipt,
            "conversation", "speech-2", 1, 108), "speech origin cannot create verified truth");
        ThrowsArgumentException(() => Native("bad-value", "topic", new string('x', 129), 109), "oversized value rejected");

        var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
        string json = JsonSerializer.Serialize(state, jsonOptions);
        NpcKnowledgeState restoredRaw = JsonSerializer.Deserialize<NpcKnowledgeState>(json, jsonOptions);
        NpcKnowledgeState restored = NpcKnowledgeLedger.CopyValidated(restoredRaw);
        Equal(state.Revision, restored.Revision, "public-field save DTO round trips revision");
        True(Snapshot(restored).TryGet("safe-location", out NpcKnowledgeFactView restoredFact), "restored actor query works");
        Equal("north-gate", restoredFact.Statement.ValueId, "restored current fact preserved");
        Equal("receipt-1", restoredFact.SupersedesUpdateReceiptId, "restored correction link preserved");
        Reject(restored, Native("receipt-1", "safe-location", "east-gate", 110), restored.Revision,
            "restored receipt dedup prevents replay");
        restoredRaw.Entries[0].ValueText = "tampered";
        True(Snapshot(restored).TryGet("safe-location", out NpcKnowledgeFactView isolated) &&
            isolated.Statement.ValueId == "north-gate", "validated restore is detached from deserializer DTO");

        Console.WriteLine("Production-linked NPC knowledge ledger regressions passed: " + assertions + " assertions.");
    }
}
