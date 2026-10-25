using System;
using System.Collections.Generic;
using System.IO;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("physical encounter projects reciprocal source-keyed journals once", RecordedEncounter),
            ("source-keyed interaction survives shared archive save and restore", ArchiveSaveRestore),
            ("completed action without native proof cannot project a meeting", MissingProofStaysPending),
            ("restored terminal action becomes unavailable only after deadline", UnavailableHistory),
            ("bounded finalizer leaves early failures pending and advances at deadline", BoundedFinalization),
            ("legacy recorded flag and unproved terminal status migrate distinctly", LegacyMigration)
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void RecordedEncounter()
    {
        var store = CompletedInteraction(out string source, out string target);
        var job = store.ReadNpcAction(ActionId(store));
        var proof = NpcInteractionPhysicalEvidence.Create(job.Id, job.Revision, source, target, 12,
            "settlement:1", 0, 0, 0, "settlement:1", 0.5, 0, 0, true);
        True(store.TryRecordNpcInteractionMemory(job.Id, job.Revision, 12, "date", "settlement 1", proof, out var marked));
        Equal(NpcInteractionProjectionDisposition.Recorded, marked.InteractionProjectionDisposition);
        True(marked.InteractionMemoryRecorded);
        var detachedProof = marked.InteractionPhysicalEvidence;
        detachedProof.TargetX = 100;
        Equal(0.5, store.ReadNpcAction(job.Id).InteractionPhysicalEvidence.TargetX);
        var saved = store.Snapshot();
        var first = saved.ConversationMemories.Find(x => x.PersonId == source).Entries;
        var second = saved.ConversationMemories.Find(x => x.PersonId == target).Entries;
        Equal(1, first.Count); Equal(1, second.Count);
        Equal(job.Id, first[0].SourceActionId); Equal(job.Id, second[0].SourceActionId);
        True(!store.TryRecordNpcInteractionMemory(job.Id, marked.Revision, 12, "date", "settlement 1", proof, out _));
        var restored = new WorldStateStore(0);
        restored.Restore(saved);
        Equal(NpcInteractionProjectionDisposition.Recorded,
            restored.ReadNpcAction(job.Id).InteractionProjectionDisposition);
        Equal(1, restored.Snapshot().ConversationMemories.Find(x => x.PersonId == source).Entries.Count);
    }

    private static void MissingProofStaysPending()
    {
        var store = CompletedInteraction(out string source, out string target);
        var job = store.ReadNpcAction(ActionId(store));
        True(!store.TryRecordNpcInteractionMemory(job.Id, job.Revision, 12, "date", "settlement 1", out _));
        var wrong = NpcInteractionPhysicalEvidence.Create(job.Id, job.Revision, source, target, 12,
            "settlement:1", 0, 0, 0, "settlement:2", 0.5, 0, 0, true);
        True(!store.TryRecordNpcInteractionMemory(job.Id, job.Revision, 12, "date", "settlement 1", wrong, out _));
        Equal(NpcInteractionProjectionDisposition.Pending,
            store.ReadNpcAction(job.Id).InteractionProjectionDisposition);
        Equal(0, store.Snapshot().ConversationMemories.Count);
    }

    private static void ArchiveSaveRestore()
    {
        string temporaryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
            "npc-interaction-projection-" + Guid.NewGuid().ToString("N")));
        string tempParent = Path.GetFullPath(Path.GetTempPath());
        True(temporaryRoot.StartsWith(tempParent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase), "archive fixture must stay under its temporary root");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var store = CompletedInteraction(out string source, out string target,
                new WorldStateStore(71, temporaryRoot));
            var job = store.ReadNpcAction(ActionId(store));
            var proof = NpcInteractionPhysicalEvidence.Create(job.Id, job.Revision, source, target, 12,
                "settlement:1", 0, 0, 0, "settlement:1", 0.5, 0, 0, true);
            True(store.TryRecordNpcInteractionMemory(job.Id, job.Revision, 12, "date", "settlement 1",
                proof, out _));
            var saved = store.SnapshotForSave();
            Equal(0, saved.ConversationMemories.Count);
            var restored = new WorldStateStore(0, temporaryRoot);
            restored.Restore(saved);
            Equal(NpcInteractionProjectionDisposition.Recorded,
                restored.ReadNpcAction(job.Id).InteractionProjectionDisposition);
            var archived = new SharedConversationArchive(temporaryRoot).Load(saved.ConversationArchive);
            Equal(job.Id, archived.Find(x => x.PersonId == source).Entries[0].SourceActionId);
            Equal(job.Id, archived.Find(x => x.PersonId == target).Entries[0].SourceActionId);
            True(restored.RecallConversation(source, "approached", 20).Contains("physically approached"));
        }
        finally
        {
            Directory.Delete(temporaryRoot, true);
        }
    }

    private static void UnavailableHistory()
    {
        var store = CompletedInteraction(out _, out _);
        var saved = store.Snapshot();
        var restored = new WorldStateStore(0);
        restored.Restore(saved);
        var job = restored.ReadNpcAction(ActionId(restored));
        True(!restored.TryMarkNpcInteractionProjectionUnavailable(job.Id, job.Revision, 99,
            "Too early.", out _));
        Equal(NpcInteractionProjectionDisposition.Pending,
            restored.ReadNpcAction(job.Id).InteractionProjectionDisposition);
        True(restored.TryMarkNpcInteractionProjectionUnavailable(job.Id, job.Revision, 100,
            "Physical encounter evidence was not retained after load.", out var marked));
        Equal(NpcInteractionProjectionDisposition.Unavailable, marked.InteractionProjectionDisposition);
        Equal(0, restored.Snapshot().ConversationMemories.Count);
        True(!restored.TryMarkNpcInteractionProjectionUnavailable(job.Id, marked.Revision, 101,
            "Repeated", out _));
    }

    private static void BoundedFinalization()
    {
        var store = CompletedInteraction(out _, out _);
        var restored = new WorldStateStore(0);
        restored.Restore(store.Snapshot());
        Equal(0, restored.FinalizeUnavailableNpcInteractions(99, 1));
        Equal(NpcInteractionProjectionDisposition.Pending,
            restored.ReadNpcAction(ActionId(restored)).InteractionProjectionDisposition);
        Equal(1, restored.FinalizeUnavailableNpcInteractions(100, 1));
        Equal(0, restored.FinalizeUnavailableNpcInteractions(101, 1));
        Equal(NpcInteractionProjectionDisposition.Unavailable,
            restored.ReadNpcAction(ActionId(restored)).InteractionProjectionDisposition);
        Equal(0, restored.Snapshot().ConversationMemories.Count);
    }

    private static void LegacyMigration()
    {
        var store = CompletedInteraction(out string source, out string target);
        var legacy = store.Snapshot();
        legacy.Version = 47; legacy.NpcExperienceInbox = new NpcExperienceInbox { Version = 4 };
        legacy.NpcActions[0].Admission = null;
        var restored = new WorldStateStore(0);
        restored.Restore(legacy);
        Equal(NpcInteractionProjectionDisposition.Unavailable,
            restored.ReadNpcAction(ActionId(restored)).InteractionProjectionDisposition);
        Equal(0, restored.Snapshot().ConversationMemories.Count);

        var job = store.ReadNpcAction(ActionId(store));
        var proof = NpcInteractionPhysicalEvidence.Create(job.Id, job.Revision, source, target, 12,
            "settlement:1", 0, 0, 0, "settlement:1", 0.5, 0, 0, true);
        True(store.TryRecordNpcInteractionMemory(job.Id, job.Revision, 12, "date", "settlement 1", proof, out _));
        legacy = store.Snapshot(); legacy.Version = 47; legacy.NpcExperienceInbox = new NpcExperienceInbox { Version = 4 };
        legacy.NpcActions[0].Admission = null;
        legacy.NpcActions[0].InteractionProjectionDisposition = NpcInteractionProjectionDisposition.Pending;
        legacy.NpcActions[0].InteractionPhysicalEvidence = null;
        foreach (var journal in legacy.ConversationMemories)
            foreach (var entry in journal.Entries) entry.SourceActionId = null;
        restored.Restore(legacy);
        Equal(NpcInteractionProjectionDisposition.Recorded,
            restored.ReadNpcAction(ActionId(restored)).InteractionProjectionDisposition);
        Equal(1, restored.Snapshot().ConversationMemories.Find(x => x.PersonId == source).Entries.Count);
    }

    private static WorldStateStore CompletedInteraction(out string source, out string target,
        WorldStateStore store = null)
    {
        if (store == null) store = new WorldStateStore(71);
        var residents = store.GetOrCreate(1, new[] { 42 }, 2).Households[0].Residents;
        source = residents[0].Id; target = residents[1].Id;
        var job = new NpcActionJob { Id = "interact-projection", ProposalId = "interact-projection",
            ResidentId = source, Kind = NpcActionKind.Interact,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = target },
            Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 10, UpdatedAt = 10, DeadlineAt = 100,
            LastPosition = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint,
                WorldSpaceId = "settlement:1", X = 0, Z = 0 } };
        True(ClassifiedAdmissionFixture.TryAcceptAction(store, job, true, true, true, out string reason), reason);
        True(store.ObserveNpcAction(job.Id, 1, 11, NpcActionObservation.Arrival()));
        True(store.ObserveNpcAction(job.Id, 2, 12, NpcActionObservation.Success(job.Id + ":observed")));
        return store;
    }

    private static string ActionId(WorldStateStore store)
    { return store.Snapshot().NpcActions[0].Id; }

    private static void True(bool value, string message = "assertion failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!Equals(expected, actual)) throw new InvalidOperationException("expected " + expected + ", got " + actual); }
}
