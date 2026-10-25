using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "npc-speech-replay-v5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ExactSourceSurvivesRealRetirement(root);
            Console.WriteLine("PASS v5 source-backed speech retirement, cue, JSON restore and branch replay");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL v5 source-backed speech replay: " + error);
            return 1;
        }
        finally { Directory.Delete(root, true); }
    }

    private static void ExactSourceSurvivesRealRetirement(string root)
    {
        // Controlled Core-issued source tickets. This does not establish native DFU audio observation.
        var store = new WorldStateStore(109, root);
        string person = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        store.GetOrCreateNpcPersonality(person, 109, NpcSocialBackground.Commoner);
        Require(store.TryBeginNpcObservedSpeechSession(person, "speech-replay:v5", out var session),
            "Could not begin source-bound speech session.");
        NpcObservedSpeechTicket firstTicket = null;
        NpcObservedSpeechReceipt firstReceipt = null;
        const int acceptedTurns = 8; // More than the six recent speech context rows.
        for (int number = 1; number <= acceptedTurns; number++)
        {
            double at = 100 + number;
            string text = "Observed player line " + number + ".";
            Require(store.TryIssueNpcObservedSpeech(session, number, text, at, out var ticket),
                "Source speech issue failed at turn " + number + ".");
            if (number == 1) { firstTicket = ticket; firstReceipt = ticket.Receipt; }
            var mood = store.CaptureConversationMood(person);
            Require(store.TryCommitAcceptedSocialExchangeWithObservedSpeech(person,
                mood.AcceptedTurnSequence, mood.Revision, 0, at, "date", "inn", text,
                "I understand.", ticket, ticket.Receipt, NpcExperienceAppliedEffectsMask.Mood,
                out long committedSequence) && committedSequence == number,
                "Exact source speech and conversation did not commit together.");
            Require(store.TryCaptureNpcExperienceAppraisal(person, at, out var appraisal),
                "Source speech was not available for appraisal.");
            Require(NpcSpeechActClassification.TryAccept(
                NpcSpeechActRequest.ForAppraisal(appraisal.Request), "Other", out var classification) &&
                store.TryBindNpcExperienceSpeechClassification(appraisal, classification),
                "Deterministic speech classification failed.");
            Require(store.TryCommitNpcExperienceAppraisal(appraisal, NeutralSpeech(appraisal.Request)),
                "Neutral speech appraisal failed.");
        }
        Require(store.TryCloseNpcObservedSpeechSession(session), "Speech session did not close.");
        var archived = store.SnapshotForSave();
        Require(archived.NpcExperienceInbox.Version == NpcExperienceInbox.VersionCurrent &&
            archived.NpcExperienceInbox.Revision == acceptedTurns &&
            archived.NpcExperienceInbox.Entries.Count == acceptedTurns &&
            archived.NpcExperienceInbox.Entries.All(x => x.Status == NpcExperienceInboxStatus.Consumed),
            "Fixture did not retain eight consumed v5 source rows.");
        Require(archived.ConversationArchive != null &&
            !string.IsNullOrWhiteSpace(archived.ConversationArchive.Head),
            "Conversation source did not reach the real archive.");
        var firstSource = FindArchivedTurn(root, archived, "Observed player line 1.");

        int retired = 0;
        for (int attempt = 0; attempt < 128 && retired == 0; attempt++)
            retired += store.MaintainNpcExperienceInboxHistory(10 * 86400, 16, 4, true).Retired;
        Require(retired >= 1, "Real maintenance did not retire the oldest source-backed speech.");
        var after = store.SnapshotForSave();
        var inbox = after.NpcExperienceInbox;
        Require(inbox.Revision == acceptedTurns && inbox.RetiredCount >= 1 &&
            inbox.Revision == inbox.RetiredCount + inbox.Entries.Count &&
            !inbox.Entries.Any(row => row.ReceiptId == firstReceipt.Evidence.ReceiptId),
            "Retirement did not remove the exact oldest speech row without rewinding its ordinal.");
        var cues = store.CaptureConversationMemoryCompactionContext(person,
            new[] { firstSource }).EmotionCues;
        Require(cues.Count == 1 && cues[0].ReceiptId == firstReceipt.Evidence.ReceiptId,
            "Retired appraisal was not preserved as an exact conversation cue.");

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(after, Json);
        Require(json.Length > 0, "JSON checkpoint was empty.");
        WorldState decoded = JsonSerializer.Deserialize<WorldState>(json, Json);
        Require(decoded != null, "JSON checkpoint did not decode.");
        var restored = new WorldStateStore(0, root);
        restored.Restore(decoded);
        var fork = new WorldStateStore(0, root);
        fork.Restore(decoded);
        foreach (var branch in new[] { restored, fork })
        {
            var saved = branch.Snapshot();
            Require(saved.NpcExperienceInbox.Revision == acceptedTurns &&
                saved.NpcExperienceInbox.RetiredCount == inbox.RetiredCount &&
                !saved.NpcExperienceInbox.Entries.Any(x => x.ReceiptId == firstReceipt.Evidence.ReceiptId),
                "JSON branch restored retired detail or lost absolute source frontier.");
            Require(branch.CaptureConversationMemoryCompactionContext(person,
                new[] { firstSource }).EmotionCues.Single().ReceiptId == firstReceipt.Evidence.ReceiptId,
                "JSON branch lost the immutable appraisal cue.");
            Require(branch.TryAcceptNpcObservedSpeechAfterAppend(firstTicket, firstReceipt,
                NpcExperienceAppliedEffectsMask.Mood) == NpcObservedSpeechAcceptanceResult.Rejected &&
                !branch.TryEnqueueNpcObservedSpeech(firstReceipt, NpcExperienceAppliedEffectsMask.Mood),
                "Old exact source receipt replayed after real retirement.");
            Require(branch.Snapshot().NpcExperienceInbox.Revision == acceptedTurns,
                "Rejected replay changed the accepted ordinal.");
        }
        Require(restored.TryBeginNpcObservedSpeechSession(person, "speech-replay:next", out var nextSession),
            "Restored source could not begin a new session.");
        Require(restored.TryIssueNpcObservedSpeech(nextSession, acceptedTurns + 1,
                "Observed player line 9.", 120, out var nextTicket) &&
            nextTicket.Ordinal == acceptedTurns + 1 &&
            nextTicket.Receipt.Evidence.ReceiptId != firstReceipt.Evidence.ReceiptId,
            "Restored source could not issue a distinct next ordinal.");
        Require(fork.TryAcceptNpcObservedSpeechAfterAppend(nextTicket, nextTicket.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Rejected,
            "One branch accepted another branch's live ticket.");
        Require(restored.TryAcceptNpcObservedSpeechAfterAppend(nextTicket, nextTicket.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Projected &&
            restored.Snapshot().NpcExperienceInbox.Revision == acceptedTurns + 1 &&
            fork.Snapshot().NpcExperienceInbox.Revision == acceptedTurns,
            "Fresh speech source failed to advance only its own branch.");
        Console.WriteLine("v5 speech replay evidence: accepted=" + acceptedTurns +
            " retired=" + inbox.RetiredCount + " retained=" + inbox.Entries.Count +
            " jsonBytes=" + json.Length);
    }

    private static SharedConversationArchive.ScanRecord FindArchivedTurn(string root,
        WorldState snapshot, string text)
    {
        var scan = new SharedConversationArchive(root).OpenScan(snapshot.ConversationArchive);
        SharedConversationArchive.ScanRecord found = null;
        while (!scan.IsComplete)
            foreach (var row in scan.ReadNextBatch(128))
                if (row.Entry.PlayerText == text)
                {
                    Require(found == null, "Archive contained duplicate exact speech source.");
                    found = row;
                }
        Require(found != null, "Archive omitted exact speech source.");
        return found;
    }

    private static string NeutralSpeech(NpcExperienceAppraisalRequest request)
    {
        return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
            ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
            ",\"saveBranchId\":\"" + request.SaveBranchId +
            "\",\"observerId\":\"" + request.ObserverId +
            "\",\"subjectPersonId\":\"" + request.SubjectPersonId +
            "\",\"evidenceDeduplicationKey\":\"" + request.EvidenceDeduplicationKey +
            "\",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId +
            "\",\"relationshipTargetId\":\"" + (request.RelationshipTargetId ?? "") +
            "\",\"expectedMoodRevision\":" + request.ExpectedMoodRevision +
            ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision +
            ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision +
            ",\"moodDelta\":0,\"affinityDelta\":0,\"trustDelta\":0,\"resentmentDelta\":0,\"fearDelta\":0," +
            "\"speechActKind\":\"Other\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
