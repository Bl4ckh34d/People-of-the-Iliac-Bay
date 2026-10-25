using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string label)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(label);
    }

    private static void Main()
    {
        NpcKnowledgeState state = NpcKnowledgeLedger.Create("npc:reader", "scope:read"), next;
        Check(NpcKnowledgeLedger.TryApply(state, NpcKnowledgeUpdate.FromNativeEvidence("npc:reader", "home-fact", "home-town",
            "Glenpoint", NpcDisclosureProtection.Public, NpcKnowledgeEvidenceOrigin.NativeOutcomeReceipt,
            "receipt", "update-home", 1, 1, "evidence-home"), "npc:reader", "scope:read", 0, out next), "public fixture");
        Check(NpcKnowledgeLedger.TryApply(next, NpcKnowledgeUpdate.FromNativeEvidence("npc:reader", "key-fact", "quest-key",
            "moon-key", NpcDisclosureProtection.QuestProtected, NpcKnowledgeEvidenceOrigin.NativeObservation,
            "observation", "update-key", 1, 2, "evidence-key"), "npc:reader", "scope:read", 1, out state), "private fixture");
        NpcKnowledgeSnapshot snapshot;
        Check(NpcKnowledgeLedger.TryCapture(state, "npc:reader", "scope:read", out snapshot), "capture fixture");
        var topics = ConversationContextTools.PublicKnowledgeTopics(snapshot);
        ConversationContextToolRequest request;
        Check(NpcConversationLookup.TryParseSelection("NONE", topics, out request) && request == null, "NONE has no read");
        Check(NpcConversationLookup.TryParseSelection("READ_TOPIC|home-town", topics, out request) &&
            request.Tool == ConversationContextToolId.ReadTopic && request.Query == "home-town" && request.PageSize == 1,
            "existing public topic remains one exact read");
        Check(!NpcConversationLookup.TryParseSelection("READ_TOPIC|quest-key", topics, out request), "private topic cannot be selected");
        Check(!NpcConversationLookup.TryParseSelection("READ_TOPIC|unknown", topics, out request), "unlisted topic cannot be selected");
        Check(NpcConversationLookup.TryParseSelection("READ_OWN_INVENTORY|||0", null, out request),
            "own-inventory selection works without a knowledge ledger");
        Check(request.Tool == ConversationContextToolId.ReadOwnInventory && request.Page == 0 && request.PageSize == 12 &&
            request.ExpectedRevision == -1 && request.ExpectedOwnerId == null, "selector cannot manufacture owner or revision cursor");
        Check(NpcConversationLookup.TryParseSelection("READ_OWN_INVENTORY|sword|Weapons|0", topics, out request) &&
            request.Query == "sword" && request.Category == "Weapons", "bounded literal query and category retained");
        Check(NpcConversationLookup.TryParseSelection("READ_OWN_INVENTORY|" + new string('q', 64) + "|" + new string('c', 40) + "|0",
            topics, out request), "maximum filter sizes accepted");
        string[] rejected = {
            "READ_OWN_INVENTORY|" + new string('q', 65) + "||0",
            "READ_OWN_INVENTORY||" + new string('c', 41) + "|0",
            "READ_OWN_INVENTORY|||1", "READ_OWN_INVENTORY|||-1", "READ_OWN_INVENTORY|||00",
            "READ_OWN_INVENTORY|||512", "READ_OWN_INVENTORY|||0|npc:other",
            "READ_OWN_INVENTORY|sword\nNONE||0", "READ_OWN_INVENTORY|*||0",
            "READ_OWN_INVENTORY|sword|Weapons|0\nREAD_TOPIC|home-town", "NONE\nREAD_OWN_INVENTORY|||0",
            "READ_INVENTORY|npc:other|0", "READ_SHOP_STOCK|||0", "TRANSFER|sword|player|1",
            "READ_OWN_INVENTORY", "read_own_inventory|||0", "", null
        };
        foreach (string output in rejected)
            Check(!NpcConversationLookup.TryParseSelection(output, topics, out request) && request == null,
                "reject malformed, unauthorized or unbounded output: " + output);

        string grammar = NpcConversationLookup.Grammar(topics);
        Check(grammar.StartsWith("root ::= \"NONE\"", StringComparison.Ordinal) && grammar.Contains("READ_TOPIC|home-town") &&
            grammar.Contains("READ_OWN_INVENTORY|") && grammar.Contains("filter-char{0,64}") &&
            grammar.Contains("filter-char{0,40}") && !grammar.Contains("quest-key"), "grammar offers only bounded authorized choices");
        string prompt = NpcConversationLookup.Prompt("What do you carry?", "history", topics);
        Check(prompt.Contains("READ_OWN_INVENTORY|||0") && prompt.Contains("home-town") && prompt.Contains("VerifiedTruth") &&
            prompt.Contains("never authorizes") && !prompt.Contains("moon-key") && !prompt.Contains("Glenpoint"),
            "prompt exposes metadata and read authority, not private facts or stored values");
        Check(NpcConversationLookup.Prompt(new string('x', NpcKnowledgeLookup.MaximumInputCharacters + 1), "", topics) == null,
            "oversized player turn rejected");
        string history = "old-secret-prefix" + new string('z', NpcKnowledgeLookup.MaximumHistoryCharacters);
        Check(!NpcConversationLookup.Prompt("question", history, topics).Contains("old-secret-prefix"), "history tail remains bounded");
        Check(NpcConversationLookup.TryParseSelection("READ_OWN_INVENTORY|sword|Weapons|0", topics, out request), "filtered request");
        var rows = new List<ConversationContextInventoryItem> {
            new ConversationContextInventoryItem { Name = "Iron Sword", Category = "Weapons", Quantity = 1 },
            new ConversationContextInventoryItem { Name = "Leather Boots", Category = "Armor", Quantity = 2 }
        };
        var read = ConversationContextTools.PageInventory("npc:reader", 7, rows, request);
        Check(read.Status == ConversationContextToolStatus.Ok && read.Rows.Count == 1 && read.Rows[0].Name == "Iron Sword",
            "selected filters use production detached inventory read");
        Check(NpcConversationLookup.TryParseSelection("READ_OWN_INVENTORY|||0", topics, out request), "unfiltered request");
        for (int i = 0; i < 20; i++) rows.Add(new ConversationContextInventoryItem { Name = "Potion " + i, Category = "Potions", Quantity = 1 });
        read = ConversationContextTools.PageInventory("npc:reader", 7, rows, request);
        Check(read.Rows.Count == 12 && read.HasMore, "one read stays at first 12 rows and indicates truncation");
        Check(ConversationContextTools.Format(read).Length <= ConversationContextTools.MaximumContextCharacters, "formatted output bounded");
        read = ConversationContextTools.PageInventory("npc:reader", 7, new List<ConversationContextInventoryItem>(), request);
        Check(read.Status == ConversationContextToolStatus.Ok && ConversationContextTools.Format(read).Contains("contains no items"),
            "known empty remains distinct from unavailable");
        Check(ConversationContextTools.Unavailable(ConversationContextToolId.ReadOwnInventory, "npc:reader").Status ==
            ConversationContextToolStatus.Unavailable, "unsupported owner is unavailable, not empty");
        InventoryReadAuthority();
        InventoryReplyFreshness();
        MemoryAndMoodSelection();
        Console.WriteLine("Production-linked NPC conversation lookup regressions passed: " + assertions + " assertions.");
    }

    private static void MemoryAndMoodSelection()
    {
        ConversationContextToolRequest request;
        Check(NpcConversationLookup.TryParseSelection("RECALL_OWN_MEMORY|our previous conversation|0", null, out request) &&
            request.Tool == ConversationContextToolId.RecallMemory && request.Query == "our previous conversation" &&
            request.Page == 0 && request.ExpectedOwnerId == null && request.ExpectedRevision == -1,
            "memory selection searches only the runtime-bound speaker without model owner or revision");
        Check(NpcConversationLookup.TryParseSelection("RECALL_OWN_MEMORY||0", null, out request), "empty recall query requests bounded recent evidence");
        Check(NpcConversationLookup.TryParseSelection("READ_OWN_MOOD_CAUSES", null, out request) &&
            request.Tool.ToString() == "ReadMoodCauses" && request.ExpectedOwnerId == null,
            "mood read cannot nominate a culprit or a foreign speaker");
        foreach (string invalid in new[] { "RECALL_OWN_MEMORY|wedding|1", "RECALL_OWN_MEMORY|wedding|0|npc:other",
            "RECALL_OWN_MEMORY|" + new string('x', 161) + "|0", "RECALL_OWN_MEMORY|wedding\nREAD_OWN_MOOD_CAUSES|0",
            "READ_OWN_MOOD_CAUSES|player", "READ_OWN_MOOD_CAUSES\nRECALL_OWN_MEMORY||0", "WRITE_MEMORY|married" })
            Check(!NpcConversationLookup.TryParseSelection(invalid, null, out request) && request == null,
                "reject memory writes, foreign identities, pagination and multiple reads: " + invalid);
        string grammar = NpcConversationLookup.Grammar(null);
        Check(grammar.Contains("RECALL_OWN_MEMORY|") && grammar.Contains("READ_OWN_MOOD_CAUSES") &&
            grammar.Contains("{0,160}"), "grammar bounds the new read alternatives");
        Check(NpcConversationLookup.TryParseSelection("RECALL_OWN_MEMORY|" + new string('x',160) + "|0", null, out request),
            "maximum memory query remains bounded and accepted");
        Check(!NpcConversationLookup.TryParseSelection("RECALL_OWN_MEMORY||0", null, out request, false, true) && request == null,
            "runtime can disable memory selection when speaker has no durable identity");
        Check(!NpcConversationLookup.TryParseSelection("READ_OWN_MOOD_CAUSES", null, out request, true, false) && request == null,
            "runtime can disable mood selection independently");
        string disabled = NpcConversationLookup.Grammar(null, false, false);
        Check(!disabled.Contains("RECALL_OWN_MEMORY|") && !disabled.Contains("READ_OWN_MOOD_CAUSES") &&
            disabled.Contains("READ_OWN_INVENTORY|"), "disabled reads are absent without removing existing inventory choice");
        string prompt = NpcConversationLookup.Prompt("Why are you sad?", "", null);
        Check(prompt.Contains("need not involve the player") && prompt.Contains("do not require the speaker to disclose") &&
            prompt.Contains("not read other NPCs"), "selector preserves causal uncertainty and disclosure separation");
        Check(!NpcConversationLookup.Prompt("Why are you sad?", "", null, false, false).Contains("READ_OWN_MOOD_CAUSES"),
            "unavailable mood read absent from selector prompt");
    }

    private static void InventoryReadAuthority()
    {
        var saved = new NpcInventoryState { PersonId = "npc:reader", Revision = 7, WalletGold = 25, LastTransactionId = "original",
            Items = new List<NpcInventoryItem> { new NpcInventoryItem { Uid = 1, NativePayload = "sword-state" },
                new NpcInventoryItem { Uid = 2, NativePayload = "boots-state" } } };
        var native = new List<NpcInventoryItem> { new NpcInventoryItem { Uid = 2, NativePayload = "boots-state" },
            new NpcInventoryItem { Uid = 1, NativePayload = "sword-state" } };
        Check(NpcInventoryReadAuthority.Matches(saved, "npc:reader", 7, native), "exact native inventory accepted regardless of collection order");
        Check(!NpcInventoryReadAuthority.Matches(saved, "npc:other", 7, native), "foreign owner rejected");
        Check(!NpcInventoryReadAuthority.Matches(saved, "npc:reader", 6, native), "stale lease rejected instead of refreshed");
        native[0].NativePayload = "changed-boots";
        Check(!NpcInventoryReadAuthority.Matches(saved, "npc:reader", 7, native), "uncommitted native changes rejected instead of saved");
        Check(saved.Revision == 7 && saved.WalletGold == 25 && saved.LastTransactionId == "original" &&
            saved.Items[1].NativePayload == "boots-state" && native[0].NativePayload == "changed-boots", "read authority never mutates either snapshot");
        native[0] = new NpcInventoryItem { Uid = 1, NativePayload = "sword-state" };
        Check(!NpcInventoryReadAuthority.Matches(saved, "npc:reader", 7, native), "duplicate native UID rejected");
        Check(!NpcInventoryReadAuthority.Matches(null, "npc:reader", 7, native), "unsupported inventory rejected");
        Check(!NpcInventoryReadAuthority.Matches(saved, "npc:reader", 7, null), "missing native inventory rejected");
        saved.Items.Clear();
        Check(!NpcInventoryReadAuthority.Matches(saved, "npc:reader", 7, native), "different inventory size rejected");
        native.Clear();
        Check(NpcInventoryReadAuthority.Matches(saved, "npc:reader", 7, native), "known empty inventory accepted");
    }

    private static ConversationContextBinding Binding(string person = "npc:reader", long request = 5,
        long epoch = 3, string archive = "0123456789abcdef0123456789abcdef", string scene = "town")
    {
        return new ConversationContextBinding(ConversationActorCategory.Mobile, person, null, "session", request, epoch,
            archive, "", scene, 1);
    }

    private static void InventoryReplyFreshness()
    {
        var binding = Binding();
        var source = new ConversationContextSourceHandle("npc:reader", "npc-inventory", 7);
        var saved = new NpcInventoryState { PersonId = "npc:reader", Revision = 7,
            Items = new List<NpcInventoryItem> { new NpcInventoryItem { Uid = 1, NativePayload = "sword-count-1" } } };
        var native = new List<NpcInventoryItem> { new NpcInventoryItem { Uid = 1, NativePayload = "sword-count-1" } };
        NpcInventoryReadEvidence evidence, ignored;
        Check(NpcInventoryReadEvidence.TryCapture(binding, source, saved, 7, native, out evidence), "selected read captures immutable evidence");
        Check(evidence.AppliesTo(binding), "same request and successor binding requires buffered immediate publication");
        Check(!evidence.AppliesTo(Binding(request: 6)) && !evidence.AppliesTo(null), "unrelated ordinary turns retain normal publication behavior");
        Check(!evidence.AppliesTo(Binding()), "value-equal replacement binding does not acquire snapshot provenance");
        Check(evidence.Matches(Binding(), saved, 7, native), "unchanged evidence accepts reply after simulated generation");
        native[0].NativePayload = "sword-count-2";
        Check(!evidence.Matches(Binding(), saved, 7, native), "native mutation during generation rejects even at same revision");
        saved.Items[0].NativePayload = "sword-count-2";
        Check(!evidence.Matches(Binding(), saved, 7, native), "native and stored payload edits cannot rewrite captured evidence at same revision");
        saved.Revision = 8;
        Check(!evidence.Matches(Binding(), saved, 8, native), "committed inventory mutation during generation rejects reply");
        saved.Revision = 7;
        saved.Items[0].NativePayload = native[0].NativePayload = "sword-count-1";
        Check(evidence.Matches(Binding(), saved, 7, native), "read validation does not mutate source or evidence");
        Check(!evidence.Matches(Binding(person: "npc:other"), saved, 7, native), "replacement speaker rejects pending inventory reply");
        Check(!evidence.Matches(Binding(epoch: 4), saved, 7, native), "save reload rejects pending inventory reply");
        Check(!evidence.Matches(Binding(archive: "fedcba9876543210fedcba9876543210"), saved, 7, native), "save branch change rejects pending reply");
        Check(!evidence.Matches(Binding(request: 6), saved, 7, native), "new request rejects old inventory evidence");
        Check(!evidence.Matches(Binding(scene: "other-town"), saved, 7, native), "scene change rejects pending inventory reply");
        saved.PersonId = "npc:other";
        Check(!evidence.Matches(Binding(), saved, 7, native), "native contents cannot borrow a foreign persisted owner");
        saved.PersonId = "npc:reader";
        Check(!NpcInventoryReadEvidence.TryCapture(binding, new ConversationContextSourceHandle("npc:other", "npc-inventory", 7),
            saved, 7, native, out ignored), "foreign captured source cannot install evidence");
        Check(!NpcInventoryReadEvidence.TryCapture(binding, new ConversationContextSourceHandle("npc:reader", "npc-inventory", 6),
            saved, 7, native, out ignored), "changed source revision cannot install evidence");
        Check(!NpcInventoryReadEvidence.TryCapture(binding, new ConversationContextSourceHandle("npc:reader", "shop-stock", 7),
            saved, 7, native, out ignored), "unrelated source kind cannot install evidence");
        Check(!NpcInventoryReadEvidence.TryCapture(binding, source, saved, 8, native, out ignored), "mismatched native lease cannot install evidence");
        Check(!evidence.Matches(null, saved, 7, native) && !evidence.Matches(binding, null, 7, native), "missing context or persisted source rejects publication");
        saved.Items.Clear();
        native.Clear();
        Check(!evidence.Matches(binding, saved, 7, native), "clearing caller-owned lists cannot clear captured evidence");
        Check(NpcInventoryReadEvidence.TryCapture(binding, source, saved, 7, native, out evidence) &&
            evidence.Matches(binding, saved, 7, native), "empty inventory answer also retains freshness dependency");
        native.Add(new NpcInventoryItem { Uid = 2, NativePayload = "new-item" });
        Check(!evidence.Matches(binding, saved, 7, native), "new item during generation invalidates prior empty inventory answer");
    }
}
