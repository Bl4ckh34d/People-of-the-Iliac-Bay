using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("exact accepted speech stays rejected after detail pruning and restore", ExactAcceptedSpeechCannotReplayAfterDetailPruning),
            ("altered observed text or time rejects atomically without consuming issued speech", AlteredTextOrTimeRejectsAtomically),
            ("ordinary append rejects a pre-applied mood mask atomically", OrdinaryAppendRejectsPreappliedMood),
            ("forged subject speaker episode or utterance reject without partial publish", ForgedReceiptIdentityRejectsAtomically),
            ("new request and closed session reject stale speech tickets", NewRequestAndClosedSessionRejectStaleTickets),
            ("restore epoch invalidates an issued but unaccepted speech ticket", RestoreEpochInvalidatesOutstandingTicket),
            ("same request command acceptance preserves issued speech", SameRequestCommandAcceptancePreservesSpeech),
            ("ordinal gap and pending speech survive restore then project once", OrdinalGapPendingRetry),
            ("schema51 restore rejects an issued inbox row with missing source", MissingSourceRestoreRejectsAtomically),
            ("schema51 restore rejects forged retained source-backed speech identity", ForgedRetainedSourceDetailRejected),
            ("legacy migration initializes speech cursors and rejects future fields", LegacyMigrationRejectsFutureSpeechFields),
            ("new per-person threat ordinal supersedes older global legacy occurrence", LegacyFamilyThreatOccurrenceDoesNotBlockNewSource)
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void AlteredTextOrTimeRejectsAtomically()
    {
        var store = Create(out var observer);
        Require(store.TryBeginNpcObservedSpeechSession(observer, "session:exact-binding", out var session),
            "Speech session did not open.");
        Require(store.TryIssueNpcObservedSpeech(session, 1, "I have seen the bridge.", 10, out var ticket),
            "Speech source was not issued.");
        var receipt = ticket.Receipt;
        string before = SnapshotText(store);
        var changedText = NpcObservedSpeechReceipt.Create(receipt.Evidence,
            NpcObservedConversationTurn.Create(observer, receipt.Evidence.EpisodeId,
                receipt.Evidence.AttributedSpeakerId, receipt.Evidence.ReceiptId,
                "I have not seen the bridge.", receipt.Evidence.ObservedGameTime));
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, changedText, NpcExperienceAppliedEffectsMask.None) ==
            NpcObservedSpeechAcceptanceResult.Rejected, "Altered exact text was accepted.");
        Require(SnapshotText(store) == before, "Text mismatch partly published speech source or inbox state.");

        var original = receipt.Evidence;
        var changedTimeEvidence = NpcExperienceEvent.FromObservedSpeech(original.ObserverId,
            original.SubjectPersonId, original.ReceiptId, original.EpisodeId, original.UtteranceId,
            original.ObservedGameTime + 1, original.AttributedSpeakerId);
        var changedTime = NpcObservedSpeechReceipt.Create(changedTimeEvidence,
            NpcObservedConversationTurn.Create(observer, original.EpisodeId, original.AttributedSpeakerId,
                original.ReceiptId, receipt.Turn.Text, original.ObservedGameTime + 1));
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, changedTime, NpcExperienceAppliedEffectsMask.None) ==
            NpcObservedSpeechAcceptanceResult.Rejected, "Altered observation time was accepted.");
        Require(SnapshotText(store) == before, "Time mismatch partly published speech source or inbox state.");
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, receipt, NpcExperienceAppliedEffectsMask.None) ==
            NpcObservedSpeechAcceptanceResult.Projected, "Exact source receipt was lost after rejected mismatches.");
    }

    private static void NewRequestAndClosedSessionRejectStaleTickets()
    {
        var store = Create(out var observer);
        Require(store.TryBeginNpcObservedSpeechSession(observer, "session:stale-request", out var session),
            "Speech session did not open.");
        Require(store.TryIssueNpcObservedSpeech(session, 1, "First answer.", 10, out var oldTicket),
            "First speech request did not issue.");
        Require(store.TryIssueNpcObservedSpeech(session, 2, "Second answer.", 11, out var currentTicket),
            "Second speech request did not issue.");
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(oldTicket, oldTicket.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Rejected,
            "Superseded request accepted its old speech ticket.");
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(currentTicket, currentTicket.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Projected,
            "Current request did not accept exact speech.");
        Require(store.TryIssueNpcObservedSpeech(session, 3, "Unposted third answer.", 12, out var unposted),
            "Third unposted speech request did not issue.");
        Require(store.TryCloseNpcObservedSpeechSession(session), "Speech session did not close.");
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(unposted, unposted.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Rejected,
            "Closed session accepted an outstanding speech ticket.");
    }

    private static void OrdinaryAppendRejectsPreappliedMood()
    {
        var store = Create(out var observer);
        Require(store.TryBeginNpcObservedSpeechSession(observer, "session:ordinary-mask", out var session),
            "Speech session did not open.");
        Require(store.TryIssueNpcObservedSpeech(session, 1, "A plain answer.", 10, out var ticket),
            "Ordinary speech did not issue.");
        string before = SnapshotText(store);
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, ticket.Receipt,
            NpcExperienceAppliedEffectsMask.Mood) == NpcObservedSpeechAcceptanceResult.Rejected,
            "Ordinary speech claimed a pre-applied mood effect.");
        Require(SnapshotText(store) == before, "Rejected ordinary mood mask partly published source state.");
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, ticket.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Projected,
            "Exact ordinary speech was lost after mask rejection.");
    }

    private static void RestoreEpochInvalidatesOutstandingTicket()
    {
        var store = Create(out var observer);
        Require(store.TryBeginNpcObservedSpeechSession(observer, "session:old-epoch", out var session),
            "Speech session did not open.");
        Require(store.TryIssueNpcObservedSpeech(session, 1, "Unposted before restore.", 10, out var oldTicket),
            "Outstanding speech was not issued.");
        var save = store.Snapshot();
        store.Restore(save);
        string before = SnapshotText(store);
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(oldTicket, oldTicket.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Rejected,
            "Old-epoch ticket accepted after restoring the same store.");
        Require(SnapshotText(store) == before, "Old-epoch rejection partly published speech state.");
        Require(store.TryBeginNpcObservedSpeechSession(observer, "session:new-epoch", out var freshSession) &&
            store.TryIssueNpcObservedSpeech(freshSession, 2, "New answer after restore.", 11, out var freshTicket) &&
            freshTicket.Ordinal == 2 && freshTicket.Receipt.Evidence.ReceiptId != oldTicket.Receipt.Evidence.ReceiptId,
            "New epoch reused the outstanding prior receipt identity.");
    }

    private static void ForgedReceiptIdentityRejectsAtomically()
    {
        var store = Create(out var observer);
        string other = store.GetOrCreate(21, new[] { 200 }, 1).Households[0].Residents[0].Id;
        Require(store.TryBeginNpcObservedSpeechSession(observer, "session:forged-receipt", out var session),
            "Speech session did not open.");
        Require(store.TryIssueNpcObservedSpeech(session, 1, "The road is safe.", 10, out var ticket),
            "Speech source did not issue.");
        var issued = ticket.Receipt.Evidence;
        var forged = new[]
        {
            NpcExperienceEvent.FromObservedSpeech(observer, other, issued.ReceiptId,
                issued.EpisodeId, issued.UtteranceId, issued.ObservedGameTime, issued.AttributedSpeakerId),
            NpcExperienceEvent.FromObservedSpeech(observer, observer, issued.ReceiptId,
                issued.EpisodeId, issued.UtteranceId, issued.ObservedGameTime, other),
            NpcExperienceEvent.FromObservedSpeech(observer, observer, issued.ReceiptId,
                "episode:forged", issued.UtteranceId, issued.ObservedGameTime, issued.AttributedSpeakerId),
            NpcExperienceEvent.FromObservedSpeech(observer, observer, issued.ReceiptId,
                issued.EpisodeId, "utterance:forged", issued.ObservedGameTime, issued.AttributedSpeakerId)
        };
        string before = SnapshotText(store);
        foreach (var evidence in forged)
        {
            var turn = NpcObservedConversationTurn.Create(observer, evidence.EpisodeId,
                evidence.AttributedSpeakerId, evidence.ReceiptId, ticket.ExactText, evidence.ObservedGameTime);
            var candidate = NpcObservedSpeechReceipt.Create(evidence, turn);
            Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, candidate,
                NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Rejected,
                "Forged speech identity was accepted.");
            Require(SnapshotText(store) == before, "Forged receipt partly published source or inbox state.");
        }
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, ticket.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Projected,
            "Exact issued receipt was lost after rejecting forged identities.");
    }

    private static void SameRequestCommandAcceptancePreservesSpeech()
    {
        var store = Create(out var observer);
        const string sessionId = "session:shared-command-and-speech";
        Require(store.TryBeginNpcObservedSpeechSession(observer, sessionId, out var speechSession),
            "Speech session did not open.");
        Require(store.TryIssueNpcObservedSpeech(speechSession, 1, "I can wait here.", 10, out var speechTicket),
            "Speech for the shared request was not issued.");
        Require(store.TryBeginNpcConversationCommandSession(observer, sessionId, out var commandSession),
            "Command session did not open.");
        var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, observer, null,
            sessionId, 1, 1, store.NpcFrameworkScopeId, "", "scene:test", 1);
        Require(store.TryBindNpcObservedSpeechContext(speechTicket, binding),
            "Speech ticket did not bind its exact shared request context.");
        Require(store.TryCaptureNpcConversationCommandTurn(commandSession, binding, "Wait here.", out var commandTurn),
            "Same-request command turn did not capture.");
        var task = new NpcTaskPlan
        {
            Id = "proposal:shared-wait", ProposalId = "proposal:shared-wait", ResidentId = observer,
            Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = 10, UpdatedAt = 10, DeadlineAt = 500
        };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
        Require(store.TryPrepareNpcConversationTask(commandTurn, task, out var prepared),
            "Same-request task did not prepare.");
        Require(store.TryIssueNpcConversationTask(commandTurn, prepared, out var capability),
            "Same-request task did not issue.");
        Require(store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out var reason),
            "Same-request task did not accept: " + reason);
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(speechTicket, speechTicket.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Projected,
            "Accepted command incorrectly invalidated speech from the same request.");
        Require(store.TryCloseNpcObservedSpeechSession(speechSession), "Speech session did not close.");
        Require(store.TryCloseNpcConversationCommandSession(commandSession), "Command session did not close.");
    }

    private static void OrdinalGapPendingRetry()
    {
        var store = Create(out var observer);
        Require(store.TryBeginNpcObservedSpeechSession(observer, "session:pending", out var session),
            "Speech session did not open.");
        Require(store.TryIssueNpcObservedSpeech(session, 1, "Unpublished answer.", 10, out var abandoned),
            "First issued answer failed.");
        Require(abandoned.Ordinal == 1, "First source ordinal was not one.");
        Require(store.TryIssueNpcObservedSpeech(session, 2, "The posted answer.", 11, out var current),
            "Second issued answer failed after abandoned first answer.");
        var sourceBefore = Source(store, observer);
        Require(sourceBefore.LastIssuedOrdinal == 2 && sourceBefore.LastAcceptedOrdinal == 0 &&
            sourceBefore.LastProjectedOrdinal == 0 && sourceBefore.Pending == null,
            "Issuance gap was stored as a count instead of an ordinal.");

        // Induce one real queue failure without a production test hook. Restore validity immediately afterward.
        var live = LiveWorld(store);
        Require(live.NpcExperienceInbox.Entries.Count == 0, "Pending fixture requires an empty inbox.");
        live.NpcExperienceInbox.Revision = long.MaxValue;
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(current, current.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.AcceptedPending,
            "Accepted exact speech was lost when the detail queue was unavailable.");
        live.NpcExperienceInbox.Revision = live.NpcExperienceInbox.Entries.Count;
        var pending = Source(store, observer);
        Require(pending.LastIssuedOrdinal == 2 && pending.LastAcceptedOrdinal == 2 &&
            pending.LastProjectedOrdinal == 0 && pending.Pending != null && pending.Pending.Ordinal == 2 &&
            pending.Pending.Entry.ObservedTurnText == "The posted answer.",
            "Pending source lost the issued ordinal gap or exact accepted text.");

        var corruptPending = store.Snapshot();
        corruptPending.NpcObservedSpeechSources[0].Pending.Entry = null;
        bool corruptRejected = false;
        try { new WorldStateStore(0).Restore(corruptPending); }
        catch (ArgumentException) { corruptRejected = true; }
        Require(corruptRejected, "Restore accepted a pending speech source without exact receipt detail.");

        var restored = new WorldStateStore(0);
        restored.Restore(store.Snapshot());
        Require(restored.TryProjectPendingNpcObservedSpeech(observer),
            "Pending accepted speech did not project after restore.");
        Require(!restored.TryProjectPendingNpcObservedSpeech(observer),
            "Projected pending speech ran twice.");
        var projected = Source(restored, observer);
        Require(projected.LastIssuedOrdinal == 2 && projected.LastAcceptedOrdinal == 2 &&
            projected.LastProjectedOrdinal == 2 && projected.Pending == null,
            "Projection did not close the ordinal gap correctly.");
        Require(restored.Snapshot().NpcExperienceInbox.Entries.Count == 1 &&
            restored.Snapshot().NpcExperienceInbox.Entries[0].ObservedTurnText == "The posted answer.",
            "Retry did not publish exactly the accepted speech detail.");
    }

    private static void LegacyMigrationRejectsFutureSpeechFields()
    {
        var store = Create(out var observer);
        var legacy = store.Snapshot();
        legacy.Version = 50;
        legacy.NpcExperienceInbox.Version = 4;
        legacy.NpcExperienceInbox.RetiredCount = 0;
        legacy.NpcExperienceInbox.DetailRevision = 0;
        legacy.NpcExperienceInbox.LegacyImportedRevision = 0;
        legacy.NpcObservedSpeechSources.Clear();
        var restored = new WorldStateStore(0);
        restored.Restore(legacy);
        Require(restored.Snapshot().Version == WorldState.CurrentVersion &&
            restored.Snapshot().NpcObservedSpeechSources.Count == 0,
            "Legacy migration did not initialize an empty schema51 speech cursor list.");
        Require(restored.TryBeginNpcObservedSpeechSession(observer, "session:legacy", out var session) &&
            restored.TryIssueNpcObservedSpeech(session, 1, "New speech after migration.", 10, out var ticket) &&
            ticket.Ordinal == 1, "Migrated resident could not issue its first source speech.");

        var futureFields = store.Snapshot();
        futureFields.Version = 50;
        futureFields.NpcExperienceInbox.Version = 4;
        futureFields.NpcExperienceInbox.RetiredCount = 0;
        futureFields.NpcExperienceInbox.DetailRevision = 0;
        futureFields.NpcExperienceInbox.LegacyImportedRevision = 0;
        futureFields.NpcObservedSpeechSources.Add(new NpcObservedSpeechSourceState
        {
            PersonId = observer, LastIssuedOrdinal = 1
        });
        bool rejected = false;
        try { restored.Restore(futureFields); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Legacy save with future speech source fields was accepted.");
        Require(Source(restored, observer).LastIssuedOrdinal == 1,
            "Rejected future-field restore replaced the live migrated source.");
    }

    private static void MissingSourceRestoreRejectsAtomically()
    {
        var store = Create(out var observer);
        Require(store.TryBeginNpcObservedSpeechSession(observer, "session:source-lost", out var session),
            "Source-backed speech session did not open.");
        Require(store.TryIssueNpcObservedSpeech(session, 1, "Source-backed speech.", 10, out var ticket),
            "Source-backed speech was not issued.");
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, ticket.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Projected,
            "Source-backed speech did not project.");
        string before = SnapshotText(store);
        var sourceLost = store.Snapshot();
        sourceLost.NpcObservedSpeechSources.Clear();
        bool rejected = false;
        try { store.Restore(sourceLost); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Schema51 issued speech detail survived Restore without its source cursor.");
        Require(SnapshotText(store) == before, "Rejected source-lost Restore replaced live state.");
        Require(Source(store, observer).LastProjectedOrdinal == 1,
            "Rejected source-lost Restore erased the accepted cursor.");
    }

    private static void LegacyFamilyThreatOccurrenceDoesNotBlockNewSource()
    {
        var store = Create(out var observer);
        var oldReceipt = NpcObservedSpeechReceipt.Create(observer, 100, "Old accepted threat.", 10);
        var saved = store.Snapshot();
        saved.Version = 50;
        saved.NpcExperienceInbox.Version = 4;
        saved.NpcExperienceInbox.RetiredCount = 0;
        saved.NpcExperienceInbox.DetailRevision = 0;
        saved.NpcExperienceInbox.LegacyImportedRevision = 0;
        saved.ConversationAttitudes.Add(new ConversationAttitude
        {
            PersonId = observer,
            FamilyThreatClassification = new ConversationSpeechClassification
            {
                Kind = ConversationSpeechClassificationKind.FamilyThreat,
                Source = ConversationSpeechClassificationSource.AcceptedSemanticDecisionRoute,
                ObserverId = observer, SpeakerId = "player", ReceiptId = oldReceipt.Evidence.ReceiptId,
                Utterance = "Old accepted threat.", Occurrence = 100, ObservedGameTime = 10
            }
        });
        var restored = new WorldStateStore(0);
        restored.Restore(saved);
        const string newText = "A later accepted threat.";
        Require(restored.TryBeginNpcObservedSpeechSession(observer, "session:threat-migration", out var session),
            "Migrated speech session did not open.");
        Require(restored.TryIssueNpcObservedSpeech(session, 1, newText, 11, out var ticket),
            "Migrated source did not issue its first speech.");
        Require(ticket.Ordinal == 1, "Migrated source did not issue its first per-person ordinal.");
        Require(restored.TryRecordAcceptedFamilyThreatClassification(observer, newText, ticket, ticket.Receipt,
            ConversationSpeechClassificationSource.AcceptedSemanticDecisionRoute),
            "Older global occurrence incorrectly blocked newer per-person source speech.");
        var latest = restored.LatestAcceptedConversationFamilyThreat(observer, 11);
        Require(latest != null && latest.ReceiptId == ticket.Receipt.Evidence.ReceiptId &&
            latest.Occurrence == 1 && latest.Utterance == newText,
            "Migrated threat classification did not retain the new source occurrence.");
    }

    private static void ForgedRetainedSourceDetailRejected()
    {
        var store = Create(out var observer);
        string other = store.GetOrCreate(21, new[] { 200 }, 1).Households[0].Residents[0].Id;
        Require(store.TryBeginNpcObservedSpeechSession(observer, "session:retained-forgery", out var session),
            "Speech session did not open.");
        Require(store.TryIssueNpcObservedSpeech(session, 1, "Exact retained speech.", 10, out var ticket),
            "Source-backed speech did not issue.");
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, ticket.Receipt,
            NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Projected,
            "Source-backed speech did not project.");
        var original = ticket.Receipt.Evidence;
        var variants = new[]
        {
            NpcExperienceEvent.FromObservedSpeech(observer, other, original.ReceiptId,
                original.EpisodeId, original.UtteranceId, original.ObservedGameTime,
                original.AttributedSpeakerId),
            NpcExperienceEvent.FromObservedSpeech(observer, observer, original.ReceiptId,
                "episode:retained-forged", original.UtteranceId, original.ObservedGameTime,
                original.AttributedSpeakerId)
        };
        foreach (var forged in variants)
        {
            var turn = NpcObservedConversationTurn.Create(observer, forged.EpisodeId,
                forged.AttributedSpeakerId, forged.ReceiptId, ticket.ExactText, forged.ObservedGameTime);
            var forgedRow = NpcExperienceInboxEntry.FromObservedSpeech(
                NpcObservedSpeechReceipt.Create(forged, turn), NpcExperienceAppliedEffectsMask.None);
            var saved = store.Snapshot();
            saved.NpcExperienceInbox.Entries[0] = forgedRow;
            bool rejected = false;
            try { new WorldStateStore(0).Restore(saved); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Schema51 Restore accepted a forged retained v2 source identity.");
        }
        Require(Source(store, observer).LastProjectedOrdinal == 1,
            "Forged detached save damaged the live source cursor.");
    }

    private static NpcObservedSpeechSourceState Source(WorldStateStore store, string observer)
    {
        var row = store.Snapshot().NpcObservedSpeechSources.Find(item => item.PersonId == observer);
        Require(row != null, "Expected durable speech source row is missing.");
        return row;
    }

    private static WorldState LiveWorld(WorldStateStore store)
    {
        var field = typeof(WorldStateStore).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(field != null, "Core live WorldState field is unavailable.");
        var value = field.GetValue(store) as WorldState;
        Require(value != null, "Core live WorldState is unavailable.");
        return value;
    }

    private static WorldStateStore Create(out string observer)
    {
        var store = new WorldStateStore(109);
        observer = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static string SnapshotText(WorldStateStore store)
    {
        return JsonSerializer.Serialize(store.Snapshot(), new JsonSerializerOptions { IncludeFields = true });
    }

    private static void ExactAcceptedSpeechCannotReplayAfterDetailPruning()
    {
        var store = new WorldStateStore(109);
        string observer = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        const string exactText = "I will guard the crossing tonight.";
        Require(store.TryBeginNpcObservedSpeechSession(observer, "session:source-replay", out var session),
            "Observed-speech source session did not open.");
        Require(store.TryIssueNpcObservedSpeech(session, 1, exactText, 10, out var ticket),
            "Exact source speech was not issued for the request.");
        var receipt = ticket.Receipt;
        Require(receipt.Turn.Text == exactText && receipt.Turn.ObservedGameTime == 10,
            "Issued receipt lost the exact text or observation time.");
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, receipt, NpcExperienceAppliedEffectsMask.None) ==
            NpcObservedSpeechAcceptanceResult.Projected,
            "Initial exact observed-speech receipt was not accepted and projected.");
        var before = store.Snapshot();
        Require(before.NpcExperienceInbox.Version == NpcExperienceInbox.VersionCurrent &&
            before.NpcExperienceInbox.Revision == 1 && before.NpcExperienceInbox.Entries.Count == 1 &&
            before.NpcExperienceInbox.Entries[0].ToObservedTurn().Text == exactText,
            "Accepted v5 inbox did not retain the exact observed turn.");

        // Structural v5 retention-envelope fixture for cursor durability. The row is
        // pending, so live maintenance would pin it; the separate SpeechReplayV5
        // regression exercises an actually consumed and retired source row.
        var pruned = store.Snapshot();
        pruned.NpcExperienceInbox.Entries.RemoveAt(0);
        pruned.NpcExperienceInbox.RetiredCount = 1;
        pruned.NpcExperienceInbox.DetailRevision++;
        var restored = new WorldStateStore(0);
        restored.Restore(pruned);
        Require(restored.Snapshot().NpcExperienceInbox.Revision == 1 &&
            restored.Snapshot().NpcExperienceInbox.RetiredCount == 1,
            "Detail retirement rewound the absolute accepted ordinal.");
        Require(restored.ReadPendingNpcExperiences(observer, 10).Count == 0,
            "Pruned historical save still exposes the removed speech detail.");
        var retainedSource = Source(restored, observer);
        Require(retainedSource.LastIssuedOrdinal == 1 && retainedSource.LastAcceptedOrdinal == 1 &&
            retainedSource.LastProjectedOrdinal == 1 && retainedSource.Pending == null,
            "Detail pruning erased the durable accepted and projected source cursor.");

        Require(restored.TryAcceptNpcObservedSpeechAfterAppend(ticket, receipt, NpcExperienceAppliedEffectsMask.None) ==
            NpcObservedSpeechAcceptanceResult.Rejected,
            "Exact previously accepted speech ticket replayed after its retained detail was pruned.");
        Require(!restored.TryEnqueueNpcObservedSpeech(receipt, NpcExperienceAppliedEffectsMask.None),
            "Public speech enqueue accepted the previously projected source receipt.");
        Require(restored.Snapshot().NpcExperienceInbox.Entries.Count == 0,
            "Speech replay published a new inbox detail row.");
        Require(restored.TryBeginNpcObservedSpeechSession(observer, "session:after-restore", out var nextSession),
            "Restored source could not begin a new speech session.");
        Require(restored.TryIssueNpcObservedSpeech(nextSession, 2, "A later answer.", 11, out var nextTicket) &&
            nextTicket.Ordinal == 2 && nextTicket.Receipt.Evidence.ReceiptId != receipt.Evidence.ReceiptId,
            "Restored source reused the pruned accepted receipt identity.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
