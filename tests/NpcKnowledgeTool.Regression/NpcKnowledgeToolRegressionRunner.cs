using System;
using PeopleOfTheIliacBay.Core;

internal static class NpcKnowledgeToolRegressionRunner
{
    private static int assertions;
    private static void True(bool value, string label)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(label);
    }
    private static void Equal<T>(T expected, T actual, string label)
    {
        assertions++;
        if (!Equals(expected, actual)) throw new InvalidOperationException(label + ": expected " + expected + ", got " + actual);
    }
    private static NpcKnowledgeSnapshot MakeSnapshot()
    {
        NpcKnowledgeState state = NpcKnowledgeLedger.Create("npc:reader", "scope:read");
        NpcKnowledgeState next;
        True(NpcKnowledgeLedger.TryApply(state, NpcKnowledgeUpdate.FromNativeEvidence(
            "npc:reader", "known-1", "home-town", "Glenpoint", NpcDisclosureProtection.Public,
            NpcKnowledgeEvidenceOrigin.NativeOutcomeReceipt, "scene-observation", "update-known", 4, 10, "evidence-known"),
            "npc:reader", "scope:read", 0, out next), "public verified fixture applies");
        state = next;
        True(NpcKnowledgeLedger.TryApply(state, NpcKnowledgeUpdate.FromAttributedClaim(
            "npc:reader", "report-1", "missing-suspect", "Mara", NpcDisclosureEpistemicKind.PlayerClaim,
            NpcDisclosureProtection.Public, NpcKnowledgeEvidenceOrigin.ConversationReceipt,
            "conversation", "update-claim", 2, 11, "player", "evidence-claim"),
            "npc:reader", "scope:read", 1, out next), "attributed claim fixture applies");
        state = next;
        True(NpcKnowledgeLedger.TryApply(state, NpcKnowledgeUpdate.FromNativeEvidence(
            "npc:reader", "secret-1", "quest-door-key", "moon-key", NpcDisclosureProtection.QuestProtected,
            NpcKnowledgeEvidenceOrigin.NativeObservation, "native-senses", "update-secret", 1, 12, "evidence-secret"),
            "npc:reader", "scope:read", 2, out next), "protected fixture applies");
        state = next;
        NpcKnowledgeSnapshot snapshot;
        True(NpcKnowledgeLedger.TryCapture(state, "npc:reader", "scope:read", out snapshot), "snapshot captured");
        return snapshot;
    }
    private static ConversationContextToolResult Read(NpcKnowledgeSnapshot snapshot, string propositionId,
        int page = 0, string expectedOwner = null, long expectedRevision = -1)
    {
        return ConversationContextTools.ReadNpcKnowledgeTopic(snapshot, "npc:reader", "player",
            new ConversationContextToolRequest { Tool = ConversationContextToolId.ReadTopic, Query = propositionId,
                Page = page, PageSize = 1, ExpectedOwnerId = expectedOwner, ExpectedRevision = expectedRevision });
    }
    private static void Main()
    {
        NpcKnowledgeSnapshot snapshot = MakeSnapshot();
        var publicResult = Read(snapshot, "home-town");
        Equal(ConversationContextToolStatus.Ok, publicResult.Status, "known public fact is readable");
        Equal("npc:reader", publicResult.OwnerId, "result binds owner actor");
        Equal("player", publicResult.AudienceId, "result binds native player audience");
        Equal(snapshot.Revision, publicResult.Revision, "result binds knowledge snapshot revision");
        Equal(1, publicResult.Rows.Count, "read returns at most one row");
        Equal("Glenpoint", publicResult.Rows[0].Name, "public verified value is selected");
        Equal("VerifiedTruth", publicResult.Rows[0].Category, "certainty remains explicit");
        True(!publicResult.Rows[0].Properties.Contains("evidence-known") &&
            !publicResult.Rows[0].Properties.Contains("scene-observation"), "source receipts and private trace metadata omitted");

        var attributedResult = Read(snapshot, "missing-suspect");
        Equal(ConversationContextToolStatus.Ok, attributedResult.Status, "public attributed claim can be retrieved");
        Equal("PlayerClaim", attributedResult.Rows[0].Category, "attributed claim never becomes verified truth");
        Equal("Mara", attributedResult.Rows[0].Name, "attributed value stays attached to claim kind");
        Equal("player", attributedResult.Rows[0].Properties, "claim attribution retained");

        var secret = Read(snapshot, "quest-door-key");
        Equal(ConversationContextToolStatus.NoResults, secret.Status, "quest-protected fact returns generic no-public-answer");
        Equal(0, secret.Rows.Count, "quest-protected value never reaches tool result");
        True(!ConversationContextTools.Format(secret).Contains("moon-key"), "formatted protected result contains no secret value");
        var unknown = Read(snapshot, "unknown-topic");
        Equal(ConversationContextToolStatus.NoResults, unknown.Status, "unknown and withheld are indistinguishable");
        Equal(ConversationContextTools.Format(unknown), ConversationContextTools.Format(secret),
            "unknown query cannot reveal existence of a protected topic");

        var topics = ConversationContextTools.PublicKnowledgeTopics(snapshot);
        Equal(2, topics.Count, "selector sees only two public permissible topics");
        string selectorPrompt = NpcKnowledgeLookup.Prompt("Where is home-town?", "recent exchange", topics);
        True(selectorPrompt.Contains("home-town") && selectorPrompt.Contains("VerifiedTruth"),
            "selector receives exact public topic IDs and epistemic labels");
        True(!selectorPrompt.Contains("Glenpoint") && !selectorPrompt.Contains("Mara") &&
            !selectorPrompt.Contains("moon-key") && !selectorPrompt.Contains("evidence-known"),
            "selector prompt excludes values, protected facts, and source trace");
        True(NpcKnowledgeLookup.Prompt(new string('x', NpcKnowledgeLookup.MaximumInputCharacters + 1), "", topics) == null,
            "oversized current turn is rejected instead of silently truncated");
        string selectorGrammar = NpcKnowledgeLookup.Grammar(topics);
        True(selectorGrammar.StartsWith("root ::= \"NONE\"", StringComparison.Ordinal) &&
            selectorGrammar.Contains("READ_TOPIC|home-town") && selectorGrammar.Contains("READ_TOPIC|missing-suspect") &&
            !selectorGrammar.Contains("quest-door-key"), "selector grammar contains only exact bounded IDs");
        Equal("home-town", NpcKnowledgeLookup.ParseSelection("READ_TOPIC|home-town", topics),
            "exact listed selector response accepted");
        Equal(string.Empty, NpcKnowledgeLookup.ParseSelection("NONE", topics), "NONE is valid");
        True(NpcKnowledgeLookup.ParseSelection("READ_TOPIC|quest-door-key", topics) == null &&
            NpcKnowledgeLookup.ParseSelection("READ_TOPIC|home-town|missing-suspect", topics) == null &&
            NpcKnowledgeLookup.ParseSelection("read_topic|home-town", topics) == null,
            "unlisted, multiple, or malformed selector responses rejected");

        NpcKnowledgeState revised = NpcKnowledgeLedger.Create("npc:reader", "scope:read");
        NpcKnowledgeState revisedOnce;
        True(NpcKnowledgeLedger.TryApply(revised, NpcKnowledgeUpdate.FromNativeEvidence(
            "npc:reader", "known-1", "home-town", "New Glenpoint", NpcDisclosureProtection.Public,
            NpcKnowledgeEvidenceOrigin.NativeOutcomeReceipt, "scene-observation", "update-known", 5, 13, "evidence-new"),
            "npc:reader", "scope:read", 0, out revisedOnce), "updated snapshot fixture applies");
        NpcKnowledgeSnapshot updatedSnapshot;
        True(NpcKnowledgeLedger.TryCapture(revisedOnce, "npc:reader", "scope:read", out updatedSnapshot),
            "updated snapshot captured");
        True(NpcKnowledgeLookup.IsSameSnapshot(snapshot, snapshot, "npc:reader"),
            "same actor, scope, and revision stays current");
        True(!NpcKnowledgeLookup.IsSameSnapshot(snapshot, updatedSnapshot, "npc:reader"),
            "changed knowledge revision invalidates delayed lookup");
        NpcKnowledgeSnapshot otherEpochSnapshot;
        True(NpcKnowledgeLedger.TryCapture(NpcKnowledgeLedger.Create("npc:reader", "scope:reloaded"),
            "npc:reader", "scope:reloaded", out otherEpochSnapshot), "other save epoch captured");
        True(!NpcKnowledgeLookup.IsSameSnapshot(snapshot, otherEpochSnapshot, "npc:reader"),
            "save epoch change invalidates delayed lookup even at same ledger revision");
        True(!NpcKnowledgeLookup.IsSameSnapshot(snapshot, snapshot, "npc:other"),
            "expected actor must match the captured ledger owner");

        var wrongActor = ConversationContextTools.ReadNpcKnowledgeTopic(snapshot, "npc:other", "player",
            new ConversationContextToolRequest { Tool = ConversationContextToolId.ReadTopic, Query = "home-town", PageSize = 1 });
        Equal(ConversationContextToolStatus.Unavailable, wrongActor.Status, "foreign actor cannot borrow knowledge snapshot");
        var wrongAudience = ConversationContextTools.ReadNpcKnowledgeTopic(snapshot, "npc:reader", "npc:other",
            new ConversationContextToolRequest { Tool = ConversationContextToolId.ReadTopic, Query = "home-town", PageSize = 1 });
        Equal(ConversationContextToolStatus.Unavailable, wrongAudience.Status, "reader is fixed to native player audience");
        Equal(ConversationContextToolStatus.NoResults,
            Read(snapshot, "*", page: 0).Status, "wildcard is treated as one exact unknown topic, never expanded");
        Equal(ConversationContextToolStatus.StalePage,
            Read(snapshot, "home-town", page: 0, expectedOwner: null, expectedRevision: 0).Status,
            "malformed first-page cursor rejected");
        Equal(ConversationContextToolStatus.StalePage,
            Read(snapshot, "home-town", page: 1, expectedOwner: "npc:other", expectedRevision: snapshot.Revision).Status,
            "foreign actor cursor rejected");
        Equal(ConversationContextToolStatus.InvalidRequest,
            ConversationContextTools.ReadNpcKnowledgeTopic(snapshot, "npc:reader", "player",
                new ConversationContextToolRequest { Tool = ConversationContextToolId.ReadTopic, Query = "home-town", PageSize = 2 }).Status,
            "read tool row limit is one");

        Console.WriteLine("Production-linked NPC knowledge tool regressions passed: " + assertions + " assertions.");
    }
}
