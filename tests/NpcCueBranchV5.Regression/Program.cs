using System;
using System.IO;
using System.Linq;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        string parent = Path.GetFullPath("work/life-core-20261023/temp");
        Directory.CreateDirectory(parent);
        string root = Path.Combine(parent, "cue-v5-branch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            HeadGrowthAndBranchMembership(root);
            Console.WriteLine("PASS cue survives head growth and reload; stale and foreign branch records reject");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL " + error);
            return 1;
        }
        finally { Directory.Delete(root, true); }
    }

    private static void HeadGrowthAndBranchMembership(string root)
    {
        var source = new WorldStateStore(109, root);
        string person = source.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        const string speech = "I remember the injury.";
        source.RememberConversation(person, 10.9, "day", "inn", speech, "I remember too.", "native");
        WorldState baseSave = source.SnapshotForSave();
        Check(baseSave.ConversationArchive.Head.Length == 64, "base archive was not committed");

        // The second branch grows from the same saved checkpoint but never receives the cue.
        var otherBranch = new WorldStateStore(0, root);
        otherBranch.Restore(baseSave);
        otherBranch.RememberConversation(person, 20, "day", "market", "other branch only", "hello", "native");
        WorldState otherSave = otherBranch.SnapshotForSave();

        var imported = baseSave;
        imported.Version = 53;
        imported.ConversationCueArchive = null;
        imported.NpcExperienceInbox.Version = 4;
        for (int index = 0; index < 7; index++)
        {
            string words = index == 0 ? speech : "later speech " + index;
            double at = index == 0 ? 10.9 : 30 + index;
            var evidence = NpcExperienceEvent.FromObservedSpeech(person, person,
                "receipt:legacy:" + index, "episode:legacy", "utterance:legacy:" + index,
                at, NpcDirectedRelationship.PlayerTargetId);
            var turn = NpcObservedConversationTurn.Create(person, evidence.EpisodeId,
                NpcDirectedRelationship.PlayerTargetId, evidence.ReceiptId, words, at);
            var row = NpcExperienceInboxEntry.FromObservedSpeech(evidence, turn,
                NpcExperienceAppliedEffectsMask.None);
            row.Status = NpcExperienceInboxStatus.Consumed;
            row.Revision = 2;
            row.UpdatedAt = at + 1;
            row.Appraisal = new NpcExperienceAppliedAppraisal();
            imported.NpcExperienceInbox.Entries.Add(row);
        }
        imported.NpcExperienceInbox.Revision = 7;
        source.Restore(imported);

        for (int attempt = 0; attempt < 32 && source.Snapshot().NpcExperienceInbox.RetiredCount == 0; attempt++)
            source.MaintainNpcExperienceInboxHistory(8 * 86400, 16, 4, true);
        WorldState retired = source.SnapshotForSave();
        Check(retired.NpcExperienceInbox.RetiredCount == 1 &&
              retired.ConversationCueArchive.Head.Length == 64, "actual cue retirement failed");
        string firstHead = retired.ConversationArchive.Head;
        var oldRecord = Record(root, retired.ConversationArchive, person, speech);
        AssertCue(source, person, oldRecord);

        source.RememberConversation(person, 40, "day", "inn", "later shared exchange", "later", "native");
        WorldState grown = source.SnapshotForSave();
        Check(grown.ConversationArchive.Head != firstHead, "archive head did not grow");
        Check(grown.ConversationCueArchive.Head == retired.ConversationCueArchive.Head,
            "archive growth rewrote immutable cue head");
        var newRecord = Record(root, grown.ConversationArchive, person, speech);
        Check(newRecord.Address.ChunkHash == oldRecord.Address.ChunkHash &&
              newRecord.Address.RecordIndex == oldRecord.Address.RecordIndex,
            "same source acquired a new content address");
        AssertCue(source, person, newRecord);
        RejectStaleAddress(source, person, oldRecord);

        var reloaded = new WorldStateStore(0, root);
        reloaded.Restore(grown);
        AssertCue(reloaded, person, Record(root, reloaded.Snapshot().ConversationArchive, person, speech));

        var foreignRecord = Record(root, otherSave.ConversationArchive, person, speech);
        RejectStaleAddress(reloaded, person, foreignRecord);
        var otherReloaded = new WorldStateStore(0, root);
        otherReloaded.Restore(otherSave);
        Check(otherReloaded.CaptureConversationMemoryCompactionContext(person,
            new[] { foreignRecord }).EmotionCues.Count == 0,
            "cue from sibling branch leaked into its unchanged checkpoint");
    }

    private static SharedConversationArchive.ScanRecord Record(string root,
        ConversationArchiveCheckpoint checkpoint, string person, string words)
    {
        var scan = new SharedConversationArchive(root).OpenScan(checkpoint);
        SharedConversationArchive.ScanRecord found = null;
        while (!scan.IsComplete)
            foreach (var item in scan.ReadNextBatch(128))
                if (item.Address.PersonId == person && item.Entry.PlayerText == words)
                {
                    Check(found == null, "ambiguous archive source");
                    found = item;
                }
        Check(found != null, "archive source missing");
        return found;
    }

    private static void AssertCue(WorldStateStore store, string person,
        SharedConversationArchive.ScanRecord record)
    {
        var context = store.CaptureConversationMemoryCompactionContext(person, new[] { record });
        Check(context.EmotionCues.Count == 1 &&
              context.EmotionCues[0].ReceiptId == "receipt:legacy:0" &&
              context.EmotionCues[0].Address.ChunkHash == record.Address.ChunkHash,
            "exact retired cue unavailable from current archive branch");
    }

    private static void RejectStaleAddress(WorldStateStore store, string person,
        SharedConversationArchive.ScanRecord record)
    {
        try { store.CaptureConversationMemoryCompactionContext(person, new[] { record }); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("foreign or stale archive head gained cue read authority");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
