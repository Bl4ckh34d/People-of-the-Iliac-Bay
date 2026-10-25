using System;
using PeopleOfTheIliacBay.Core;

internal static class NpcKnowledgeStoreRegressionRunner
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
    private static string KnownNpc(WorldStateStore store)
    { return store.GetOrCreate(709, new[] { 7009 }, 1).Households[0].Residents[0].Id; }
    private static NpcKnowledgeUpdate Evidence(string actor, string receipt, string topic, string value, double time)
    {
        return NpcKnowledgeUpdate.FromNativeEvidence(actor, "entry-" + receipt, topic, value,
            NpcDisclosureProtection.Public, NpcKnowledgeEvidenceOrigin.NativeOutcomeReceipt,
            "native-outcome", "update-" + receipt, 4, time, "evidence-" + receipt);
    }
    private static void Main()
    {
        var store = new WorldStateStore(709);
        string actor = KnownNpc(store);
        NpcKnowledgeSnapshot empty;
        True(store.TryCaptureNpcKnowledgeSnapshot(actor, out empty), "known actor gets empty detached snapshot");
        True(!empty.TryGet("unknown", out NpcKnowledgeFactView unknown), "missing topic remains unknown");
        Equal(0, store.Snapshot().NpcKnowledge.Count, "read/capture does not persistently allocate a ledger");
        True(!store.TryReadNpcKnowledge("npc:missing", "anything", out NpcKnowledgeFactView absentActor),
            "unknown actor cannot query the knowledge store");

        True(store.TryCaptureNpcKnowledgeWrite(actor, out NpcKnowledgeWriteTicket ticket), "known actor gets write ticket");
        NpcKnowledgeUpdate update = Evidence(actor, "one", "safe-route", "east-road", 10);
        True(store.TryCommitNpcKnowledge(ticket, update), "native evidence commits to actor ledger");
        Equal(1, store.Snapshot().NpcKnowledge.Count, "first successful write allocates one persisted ledger");
        True(store.TryReadNpcKnowledge(actor, "safe-route", out NpcKnowledgeFactView route), "store returns actor-owned fact");
        Equal("east-road", route.Statement.ValueId, "store returns committed value");
        Equal("evidence-one", route.EvidenceReceiptId, "evidence receipt provenance survives store commit");
        True(!store.TryReadNpcKnowledge(actor, "never-seen", out unknown), "missing topic read does not fabricate a fact");

        True(store.TryCaptureNpcKnowledgeWrite(actor, out NpcKnowledgeWriteTicket stale), "capture later ticket");
        True(store.TryCaptureNpcKnowledgeWrite(actor, out NpcKnowledgeWriteTicket competing), "capture concurrent same-revision ticket");
        True(store.TryCommitNpcKnowledge(competing, Evidence(actor, "two", "safe-route", "north-road", 11)),
            "first same-revision writer succeeds");
        True(!store.TryCommitNpcKnowledge(stale, Evidence(actor, "three", "safe-route", "west-road", 12)),
            "stale revision ticket rejected");
        True(!store.TryCommitNpcKnowledge(competing, Evidence(actor, "two", "safe-route", "north-road", 11)),
            "ticket replay rejected after commit");
        Equal("north-road", store.TryGetValueForTest(actor, "safe-route"),
            "rejected stale/replayed commits leave state unchanged");

        var detached = store.Snapshot();
        detached.NpcKnowledge[0].Entries[0].ValueText = "tampered detached snapshot";
        Equal("north-road", store.TryGetValueForTest(actor, "safe-route"),
            "mutating returned world snapshot cannot mutate live ledger");

        WorldState beforeRollback = store.Snapshot();
        True(store.TryCaptureNpcKnowledgeWrite(actor, out NpcKnowledgeWriteTicket rollbackTicket), "capture rollback ticket");
        True(store.TryCommitNpcKnowledge(rollbackTicket, Evidence(actor, "four", "safe-route", "south-road", 13)),
            "rollback fixture update commits");
        store.Restore(beforeRollback);
        Equal("north-road", store.TryGetValueForTest(actor, "safe-route"),
            "restore rolls ledger back to saved value");
        True(!store.TryCommitNpcKnowledge(rollbackTicket, Evidence(actor, "four", "safe-route", "south-road", 13)),
            "ticket from pre-restore epoch rejected");
        True(store.TryCaptureNpcKnowledgeWrite(actor, out NpcKnowledgeWriteTicket actorTicket), "capture current actor ticket after restore");
        True(!store.TryCommitNpcKnowledge(actorTicket, Evidence("npc:foreign", "five", "topic", "value", 14)),
            "ticket cannot write a foreign actor update");

        WorldState wrongScope = store.Snapshot();
        wrongScope.NpcKnowledge[0].SaveEpochId = "foreign-scope";
        bool rejected = false;
        try { store.Restore(wrongScope); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "restore rejects ledger with foreign framework scope");

        WorldState unknownOwner = store.Snapshot();
        unknownOwner.NpcKnowledge.Add(NpcKnowledgeLedger.Create("npc:unknown", unknownOwner.NpcFrameworkScopeId));
        rejected = false;
        try { store.Restore(unknownOwner); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "restore rejects ledger for an unknown NPC identity");

        WorldState duplicateOwner = store.Snapshot();
        duplicateOwner.NpcKnowledge.Add(NpcKnowledgeLedger.CopyValidated(duplicateOwner.NpcKnowledge[0]));
        rejected = false;
        try { store.Restore(duplicateOwner); }
        catch (ArgumentException) { rejected = true; }
        True(rejected, "restore rejects duplicate actor ledgers");

        WorldState legacy = store.Snapshot();
        legacy.Version = 37; legacy.NpcExperienceInbox = new NpcExperienceInbox { Version = 4 };
        string legacyScope = legacy.NpcFrameworkScopeId;
        store.Restore(legacy);
        Equal(0, store.Snapshot().NpcKnowledge.Count, "pre-38 save migration starts with no knowledge ledger rows");
        True(store.NpcFrameworkScopeId != legacyScope, "pre-38 save receives a new framework scope");

        Console.WriteLine("Production-linked NPC knowledge store regressions passed: " + assertions + " assertions.");
    }
}

internal static class TestKnowledgeQueries
{
    internal static string TryGetValueForTest(this WorldStateStore store, string actorId, string topicId)
    {
        if (!store.TryReadNpcKnowledge(actorId, topicId, out NpcKnowledgeFactView fact))
            return null;
        return fact.Statement.ValueId;
    }
}
