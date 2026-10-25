using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        try
        {
            RetiredDetailDoesNotRewindAcceptedOrdinal();
            LegacyFutureFieldsAreRejectedBeforeNormalization();
            PreInboxSchemaRejectsCarriedHistory();
            LegacyHealthCannotBorrowLaterProjection();
            ForeignPoisonKeyPreservesExactPending();
            PendingMemorySourceBecomesImmutableCue(1, false);
            PendingMemorySourceBecomesImmutableCue(130, false);
            PendingMemorySourceBecomesImmutableCue(1, true);
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error); return 1; }
        Console.WriteLine("PASS 8/8 inbox ordinal, legacy loss guards, source guards, ordered cues, IO retry");
        return 0;
    }

    private static void RetiredDetailDoesNotRewindAcceptedOrdinal()
    {
        var source = new WorldStateStore(42);
        string person = source.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
        var ticket = source.TryIssueNpcPoisonAssignment(source.CaptureNpcVitalsWriteEpoch(),
            person, new object(), new object());
        if (source.TryRecordNpcPoisonAssignment(ticket, "poison:classic", 10,
            out _) != NpcPoisonCaptureDisposition.Queued)
            throw new InvalidOperationException("Fixture poison source was not accepted.");
        var saved = source.Snapshot();
        if (saved.NpcExperienceInbox.Revision != 1 || saved.NpcExperienceInbox.Entries.Count != 1)
            throw new InvalidOperationException("Fixture inbox did not contain one accepted entry.");
        NpcExperienceAppraisalTicket appraisal;
        if (!source.TryCaptureNpcExperienceAppraisal(person, 11, out appraisal) ||
            !source.TryCommitNpcExperienceAppraisal(appraisal, Neutral(appraisal.Request)))
            throw new InvalidOperationException("Fixture poison outcome was not consumed.");
        if (!source.TryCaptureNpcMoodCauseRead(person, 8 * 86400 + 20, 600, out var oldRead))
            throw new InvalidOperationException("Mood-cause read receipt was not captured.");
        var maintenance = source.MaintainNpcExperienceInboxHistory(8 * 86400 + 20, 16, 4, true);
        if (maintenance.Retired != 1)
            throw new InvalidOperationException("Settled typed poison detail was not retired.");
        if (source.IsNpcMemoryReadCurrent(oldRead, person, 8 * 86400 + 20))
            throw new InvalidOperationException("Retired detail left an old mood-cause read receipt current.");
        saved = source.Snapshot();
        var restored = new WorldStateStore(0);
        restored.Restore(saved);
        if (restored.Snapshot().NpcExperienceInbox.Revision != 1 ||
            restored.Snapshot().NpcExperienceInbox.RetiredCount != 1 ||
            restored.Snapshot().NpcExperienceInbox.Entries.Count != 0)
            throw new InvalidOperationException("Retiring detail rewound the accepted enqueue ordinal.");
        var next = restored.TryIssueNpcPoisonAssignment(restored.CaptureNpcVitalsWriteEpoch(),
            person, new object(), new object());
        if (restored.TryRecordNpcPoisonAssignment(next, "poison:classic", 8 * 86400 + 21,
            out _) != NpcPoisonCaptureDisposition.Queued ||
            restored.Snapshot().NpcExperienceInbox.Revision != 2)
            throw new InvalidOperationException("A fresh assignment did not advance the durable ordinal.");
    }

    private static void LegacyFutureFieldsAreRejectedBeforeNormalization()
    {
        var store = new WorldStateStore(77);
        store.GetOrCreate(100, new[] { 10 }, 1);
        var legacy = store.Snapshot();
        legacy.Version = 35;
        legacy.NpcExperienceInbox.Version = 1;
        legacy.NpcExperienceInbox.RetiredCount = 1;
        ExpectRestoreFailure(legacy);
        legacy.NpcExperienceInbox.RetiredCount = 0;
        // A raw future ordinal must fail before the schema35–37 normalizer copies rows.
        string person = legacy.Settlements[0].Households[0].Residents[0].Id;
        var evidence = NpcExperienceEvent.FromNativeOutcome(person, person,
            "r:old", "e:old", "o:old", NpcExperienceEventKind.Injury, 1,
            NpcResponsibleActorAttribution.Unknown, null,
            NpcExperienceImpactDetails.ForHealthEpisode(10, 9, 10, 1, 0, 1));
        var row = NpcExperienceInboxEntry.FromNativeEvent(evidence);
        row.EnqueueOrdinal = 999;
        legacy.NpcExperienceInbox.Entries.Add(row);
        legacy.NpcExperienceInbox.Revision = 1;
        ExpectRestoreFailure(legacy);
    }

    private static void PreInboxSchemaRejectsCarriedHistory()
    {
        var store = new WorldStateStore(76);
        string person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
        var legacy = store.Snapshot();
        legacy.Version = 34;
        legacy.ConversationCueArchive = null;
        legacy.NpcMoodCauseHistoryFloors.Clear();
        new WorldStateStore(0).Restore(legacy); // Empty initializer is valid in a pre-inbox save.

        legacy.NpcExperienceInbox.RetiredCount = 1;
        ExpectRestoreFailure(legacy);
        if (legacy.NpcExperienceInbox.RetiredCount != 1)
            throw new InvalidOperationException("Pre-inbox rejection mutated the supplied snapshot.");
        legacy.NpcExperienceInbox.RetiredCount = 0;
        legacy.NpcExperienceInbox.Revision = 1;
        legacy.NpcExperienceInbox.Entries.Add(NpcExperienceInboxEntry.FromNativeEvent(
            NpcExperienceEvent.FromNativeOutcome(person, person, "r:pre35", "e:pre35", "o:pre35",
                NpcExperienceEventKind.Injury, 1, NpcResponsibleActorAttribution.Unknown, null,
                NpcExperienceImpactDetails.ForHealthEpisode(10, 9, 10, 1, 0, 1))));
        ExpectRestoreFailure(legacy);
        if (legacy.NpcExperienceInbox.Revision != 1 || legacy.NpcExperienceInbox.Entries.Count != 1)
            throw new InvalidOperationException("Pre-inbox rejection discarded source history in place.");
    }

    private static void ExpectRestoreFailure(WorldState saved)
    {
        try { new WorldStateStore(0).Restore(saved); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Legacy future field was silently normalized away.");
    }

    private static void LegacyHealthCannotBorrowLaterProjection()
    {
        var store = new WorldStateStore(78);
        string person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
        store.GetOrCreateNpcPersonality(person, 109, NpcSocialBackground.Commoner);
        var imported = store.Snapshot();
        imported.Version = 53;
        imported.ConversationCueArchive = null;
        imported.NpcExperienceInbox.Version = 4;
        var old = NpcExperienceInboxEntry.FromNativeEvent(
            NpcExperienceEvent.FromNativeOutcome(person, person, "old-health-receipt",
                "old-health-episode", "old-health-outcome", NpcExperienceEventKind.Injury,
                1, NpcResponsibleActorAttribution.Unknown, null,
                NpcExperienceImpactDetails.ForHealthEpisode(10, 9, 10, 1, 0, 1)));
        old.Status = NpcExperienceInboxStatus.Consumed;
        old.Revision = 2;
        old.UpdatedAt = 2;
        old.Appraisal = new NpcExperienceAppliedAppraisal();
        imported.NpcExperienceInbox.Entries.Add(old);
        imported.NpcExperienceInbox.Revision = 1;
        store.Restore(imported);

        var epoch = store.CaptureNpcVitalsWriteEpoch();
        var vitals = store.ReadNpcVitals(person, NpcArchetype.Citizen);
        if (!store.TryCaptureObservedNpcVitalsChange(epoch, person, NpcArchetype.Citizen,
            vitals.CurrentHealth, vitals.CurrentMagicka, vitals.CurrentHealth - 1,
            vitals.CurrentMagicka, 100, vitals.Revision))
            throw new InvalidOperationException("Real later health source was rejected.");
        if (store.FlushNpcHealthExperiences(160, 60, 1).Count != 1 ||
            store.ReadNpcProgress(person).HealthExperienceProjectedThroughVitalsRevision <= 0)
            throw new InvalidOperationException("Later health source was not projected.");
        if (!store.TryCaptureNpcExperienceAppraisal(person, 161, out var ticket) ||
            !store.TryCommitNpcExperienceAppraisal(ticket, Neutral(ticket.Request)))
            throw new InvalidOperationException("Later health event did not become consumed.");
        store.MaintainNpcExperienceInboxHistory(8 * 86400, 16, 4, true);
        var saved = store.Snapshot();
        if (!saved.NpcExperienceInbox.Entries.Any(x => x.ReceiptId == "old-health-receipt"))
            throw new InvalidOperationException("Unclassified imported health detail borrowed a later projection cursor.");
    }

    private static void ForeignPoisonKeyPreservesExactPending()
    {
        var first = new WorldStateStore(79);
        string person = first.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
        var firstTicket = first.TryIssueNpcPoisonAssignment(first.CaptureNpcVitalsWriteEpoch(),
            person, new object(), new object());
        if (first.TryRecordNpcPoisonAssignment(firstTicket, "poison:classic", 10, out _) !=
            NpcPoisonCaptureDisposition.Queued)
            throw new InvalidOperationException("Poison collision source fixture failed.");
        var legacy = first.Snapshot();
        legacy.Version = 49;
        legacy.ConversationCueArchive = null;
        legacy.NpcExperienceInbox.Version = 4;
        legacy.NpcExperienceInbox.RetiredCount = 0;
        legacy.NpcExperienceInbox.DetailRevision = 0;
        legacy.NpcExperienceInbox.LegacyImportedRevision = 0;
        legacy.NpcExperienceInbox.Entries[0].EnqueueOrdinal = 0;
        legacy.NpcExperienceInbox.Entries[0].SourceKind = NpcExperienceSourceKind.LegacyUnknown;
        legacy.NpcExperienceInbox.Entries[0].ReceiptId = "foreign-legacy-poison-receipt";
        legacy.NpcPoisonOnsets.Clear();
        var restored = new WorldStateStore(0);
        restored.Restore(legacy);
        var ticket = restored.TryIssueNpcPoisonAssignment(restored.CaptureNpcVitalsWriteEpoch(),
            person, new object(), new object());
        if (restored.TryRecordNpcPoisonAssignment(ticket, "poison:classic", 11, out _) !=
            NpcPoisonCaptureDisposition.Pending)
            throw new InvalidOperationException("Foreign retained key silently degraded a real poison assignment.");
        var saved = restored.Snapshot();
        if (saved.NpcPoisonOnsets.Single(x => x.PersonId == person).Pending?.Sequence != 1 ||
            saved.NpcExperienceInbox.Entries.Single().ReceiptId != "foreign-legacy-poison-receipt")
            throw new InvalidOperationException("Poison collision did not preserve source and historical detail.");
    }

    private static string Neutral(NpcExperienceAppraisalRequest r)
    {
        return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
            ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
            ",\"saveBranchId\":\"" + r.SaveBranchId + "\",\"observerId\":\"" + r.ObserverId +
            "\",\"subjectPersonId\":\"" + r.SubjectPersonId +
            "\",\"evidenceDeduplicationKey\":\"" + r.EvidenceDeduplicationKey +
            "\",\"canonicalEvidenceId\":\"" + r.CanonicalEvidenceId +
            "\",\"relationshipTargetId\":\"" + (r.RelationshipTargetId ?? "") +
            "\",\"expectedMoodRevision\":" + r.ExpectedMoodRevision +
            ",\"expectedAffinityRevision\":" + r.ExpectedAffinityRevision +
            ",\"expectedRelationshipRevision\":" + r.ExpectedRelationshipRevision +
            ",\"moodDelta\":0,\"affinityDelta\":0,\"trustDelta\":0,\"resentmentDelta\":0," +
            "\"fearDelta\":0,\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\"," +
            "\"conversationOption\":\"NoAction\"}";
    }

    private static void PendingMemorySourceBecomesImmutableCue(int predecessors, bool missingArchive)
    {
        string root = Path.Combine(Path.GetTempPath(), "inbox-cue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new WorldStateStore(109, root);
            string person = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
            for (int i = 0; i < predecessors; i++)
                store.RememberConversation(person, 10.1 + i, "day", "inn", "before " + i,
                    "before.", "native");
            double sourceAt = 10.1 + predecessors;
            store.RememberConversation(person, sourceAt, "day", "inn", "I remember the injury.",
                "I remember too.", "native");
            store.RememberConversation(person, sourceAt + 1, "day", "inn", "after the injury.",
                "after.", "native");
            var imported = store.Snapshot();
            imported.Version = 53;
            imported.ConversationCueArchive = null;
            imported.NpcExperienceInbox.Version = 4;
            for (int i = 0; i < 7; i++)
            {
                string words = i == 0 ? "I remember the injury." : "later speech " + i;
                double at = i == 0 ? Math.Floor(sourceAt) + 0.9 : sourceAt + 10 + i;
                var evidence = NpcExperienceEvent.FromObservedSpeech(person, person,
                    "receipt:legacy:" + i, "episode:legacy", "utterance:legacy:" + i,
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
            store.Restore(imported);
            if (!store.TryCaptureNpcMemoryRead(person, "injury", 8 * 86400, 600, out var oldMemoryRead))
                throw new InvalidOperationException("Conversation read receipt was not captured.");
            if (missingArchive)
            {
                store.SnapshotForSave();
                if (!store.TryCaptureNpcMemoryRead(person, "injury", 8 * 86400, 600,
                    out oldMemoryRead))
                    throw new InvalidOperationException("Fault fixture could not recapture archived memory.");
                string path = Directory.GetFiles(root, "*.pim", SearchOption.AllDirectories).Single();
                byte[] original = File.ReadAllBytes(path);
                var before = store.Snapshot();
                string beforeJson = JsonSerializer.Serialize(before,
                    new JsonSerializerOptions { IncludeFields = true });
                if (!store.TryCaptureNpcMoodCauseRead(person, 8 * 86400, 600, out var oldMoodRead))
                    throw new InvalidOperationException("Fault fixture could not capture a read receipt.");
                File.Delete(path);
                var failed = store.MaintainNpcExperienceInboxHistory(8 * 86400, 16, 4, true);
                var after = store.Snapshot();
                if (failed.Retired != 0 || failed.ArchivedPending != 0 ||
                    JsonSerializer.Serialize(after,
                        new JsonSerializerOptions { IncludeFields = true }) != beforeJson ||
                    after.NpcExperienceInbox.Revision != before.NpcExperienceInbox.Revision ||
                    after.NpcExperienceInbox.RetiredCount != before.NpcExperienceInbox.RetiredCount ||
                    after.NpcExperienceInbox.DetailRevision != before.NpcExperienceInbox.DetailRevision ||
                    after.ConversationArchive.Head != before.ConversationArchive.Head ||
                    after.ConversationCueArchive.Head != before.ConversationCueArchive.Head ||
                    !store.IsNpcMemoryReadCurrent(oldMoodRead, person, 8 * 86400) ||
                    !store.IsNpcMemoryReadCurrent(oldMemoryRead, person, 8 * 86400))
                    throw new InvalidOperationException("Missing archive source mutated committed state or a read receipt.");
                File.WriteAllBytes(path, original);
            }
            int retired = 0, archivedPending = 0, pendingScanned = 0;
            for (int i = 0; i < 32 && retired == 0; i++)
            {
                var result = store.MaintainNpcExperienceInboxHistory(8 * 86400, 16, 4, true);
                retired += result.Retired;
                archivedPending += result.ArchivedPending;
                pendingScanned += result.PendingMemoryRowsScanned;
            }
            int expectedPendingArchive = missingArchive ? 0 : predecessors + 1;
            if (retired != 1 || (!missingArchive && pendingScanned < 1) ||
                archivedPending != expectedPendingArchive)
                throw new InvalidOperationException("Exact pending conversation was not archived with its cue.");
            if (store.IsNpcMemoryReadCurrent(oldMemoryRead, person, 8 * 86400))
                throw new InvalidOperationException("Archived prefix left an old conversation read receipt current.");
            var saved = store.SnapshotForSave();
            if (saved.NpcExperienceInbox.RetiredCount != 1 ||
                saved.ConversationCueArchive == null || saved.ConversationCueArchive.Head.Length != 64)
                throw new InvalidOperationException("Cue and retired row did not share the save checkpoint.");
            var scan = new SharedConversationArchive(root).OpenScan(saved.ConversationArchive);
            SharedConversationArchive.ScanRecord record = null;
            while (!scan.IsComplete)
                foreach (var candidate in scan.ReadNextBatch(128))
                    if (candidate.Entry.PlayerText == "I remember the injury.")
                    {
                        if (record != null) throw new InvalidOperationException("Duplicate matching conversation source.");
                        record = candidate;
                    }
            if (record == null) throw new InvalidOperationException("Conversation source was absent from archive.");
            var ordered = new SharedConversationArchive(root).Load(saved.ConversationArchive)
                .Single(x => x.PersonId == person).Entries.Select(x => x.PlayerText).ToArray();
            var expected = Enumerable.Range(0, predecessors).Select(i => "before " + i)
                .Concat(new[] { "I remember the injury.", "after the injury." });
            if (!ordered.SequenceEqual(expected))
                throw new InvalidOperationException("Pending cue publication changed conversation chronology.");
            var context = store.CaptureConversationMemoryCompactionContext(person,
                new[] { record });
            if (context.EmotionCues.Count != 1 || context.EmotionCues[0].ReceiptId != "receipt:legacy:0")
                throw new InvalidOperationException("Retired speech cue was absent from exact archive context.");
            var restored = new WorldStateStore(0, root);
            restored.Restore(saved);
            if (restored.CaptureConversationMemoryCompactionContext(person,
                new[] { record }).EmotionCues.Count != 1)
                throw new InvalidOperationException("Retired cue did not survive save/restore.");
        }
        finally { Directory.Delete(root, true); }
    }
}
