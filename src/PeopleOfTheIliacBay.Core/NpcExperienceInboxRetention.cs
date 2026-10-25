using System;
using System.Collections.Generic;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class NpcExperienceInboxRetentionResult
    {
        public int Examined { get; internal set; }
        public int Retired { get; internal set; }
        public int Pinned { get; internal set; }
        public int Deferred { get; internal set; }
        public int ReferenceRowsInspected { get; internal set; }
        public int ArchiveRowsScanned { get; internal set; }
        public int PendingMemoryRowsScanned { get; internal set; }
        public int ArchivedPending { get; internal set; }
        public bool ScanComplete { get; internal set; }
        public bool AllPinned { get; internal set; }
    }

    public sealed partial class WorldStateStore
    {
        private object inboxRetentionEpoch;
        private int inboxRetentionCursor;
        private int inboxRetentionCovered;
        private NpcExperienceInbox inboxRetentionSweepInbox;
        private int inboxRetentionSweepPinned, inboxRetentionSweepDeferred;
        private double inboxRetentionSweepNow;
        private SharedConversationArchive.Scan cueSourceScan;
        private long cueScanOrdinal;
        private string cueScanHead;
        private SharedConversationArchive.ScanRecord cueScanMatch;
        private int cueScanMatchCount;
        private bool cueScanAdvancedThisCall;
        private bool cuePendingAdvancedThisCall;
        private int cuePendingJournalCursor, cuePendingEntryCursor;
        private int cuePendingMatchCount, cuePendingMatchJournal, cuePendingMatchEntry;
        private object cuePendingEpochAtStart;
        private bool cuePendingScanComplete;
        private bool cuePendingPrefixNeeded;
        private long cuePendingPrefixOrdinal;

        /// <summary>Examines at most the requested inbox rows; successful publication has one full validation.</summary>
        public NpcExperienceInboxRetentionResult MaintainNpcExperienceInboxHistory(double now,
            int maximumExamined, int maximumGroups, bool underCapacityPressure = false)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (maximumExamined < 1 || maximumExamined > 64)
                throw new ArgumentOutOfRangeException(nameof(maximumExamined));
            if (maximumGroups < 1 || maximumGroups > 32)
                throw new ArgumentOutOfRangeException(nameof(maximumGroups));
            var result = new NpcExperienceInboxRetentionResult();
            cueScanAdvancedThisCall = false;
            cuePendingAdvancedThisCall = false;
            cuePendingPrefixNeeded = false;
            var inbox = state.NpcExperienceInbox;
            int count = inbox.Entries.Count;
            if (!ReferenceEquals(inboxRetentionEpoch, bindingEpoch))
            { inboxRetentionEpoch = bindingEpoch; inboxRetentionCursor = 0; inboxRetentionCovered = 0;
              cueSourceScan = null; inboxRetentionSweepInbox = null; }
            if (count == 0) { result.ScanComplete = true; return result; }
            if (inboxRetentionCursor >= count) inboxRetentionCursor %= count;
            if (!ReferenceEquals(inboxRetentionSweepInbox, inbox) || inboxRetentionCovered >= count)
            { inboxRetentionSweepInbox = inbox; inboxRetentionCovered = 0;
              inboxRetentionSweepPinned = 0; inboxRetentionSweepDeferred = 0;
              inboxRetentionSweepNow = now; }
            if (cueSourceScan != null && cueScanHead != state.ConversationArchive?.Head)
                cueSourceScan = null;
            if (cueSourceScan != null &&
                !ReferenceEquals(cuePendingEpochAtStart, pendingConversationMemoryEpoch))
                cueSourceScan = null;
            if (cueSourceScan != null && !cueSourceScan.IsComplete)
                AdvanceCueScan(inbox.Entries.Find(x => x.EnqueueOrdinal == cueScanOrdinal), result);

            // Build a reference index only if a scanned, otherwise eligible row
            // needs it. An all-pending inbox costs at most the examined budget.
            HashSet<NpcExperienceInboxEntry> protectedSpeech = null;
            HashSet<NpcExperienceInboxEntry> recent = null;
            var chosen = new List<NpcExperienceInboxEntry>(maximumGroups);
            ConversationCueArchiveRecord cueToPublish = null;
            int bounded = Math.Min(maximumExamined, count);
            for (int scanned = 0; scanned < bounded && chosen.Count < maximumGroups; scanned++)
            {
                int index = inboxRetentionCursor++;
                if (inboxRetentionCursor >= count) inboxRetentionCursor = 0;
                var row = inbox.Entries[index];
                result.Examined++;
                if (row.Status == NpcExperienceInboxStatus.Pending || row.UpdatedAt > now ||
                    !ProjectedSourceCanBeRetired(row) ||
                    row.Appraisal != null && row.Appraisal.AppliedMoodDelta != 0 &&
                    MoodRecovery.ValueAt(row.Appraisal.AppliedMoodDelta, row.UpdatedAt, now, 86400) != 0)
                { result.Pinned++; continue; }
                if (row.Origin == NpcExperienceEventOrigin.AttributedSpeech)
                {
                    if (protectedSpeech == null)
                        protectedSpeech = ProtectedSpeechContextRows(inbox.Entries, result);
                    if (protectedSpeech.Contains(row)) { result.Pinned++; continue; }
                }
                if (!underCapacityPressure && now - row.UpdatedAt < 7 * 86400)
                { result.Deferred++; continue; }
                if (!underCapacityPressure)
                {
                    if (recent == null) recent = RecentExperienceRows(inbox.Entries, result);
                    if (recent.Contains(row)) { result.Deferred++; continue; }
                }
                // One cue at a time preserves one exact immutable parent chain per publication.
                if (NeedsArchiveCue(row))
                {
                    if (cueToPublish != null) { result.Deferred++; continue; }
                    bool impossible;
                    if (!TryPrepareArchivedCue(row, result, out cueToPublish, out impossible))
                    { if (impossible) result.Pinned++; else result.Deferred++;
                      if (cuePendingPrefixNeeded) break;
                      continue; }
                }
                chosen.Add(row);
            }
            inboxRetentionCovered = Math.Min(count, inboxRetentionCovered + result.Examined);
            inboxRetentionSweepPinned += result.Pinned;
            inboxRetentionSweepDeferred += result.Deferred;
            result.ScanComplete = inboxRetentionCovered >= count;
            if (cuePendingPrefixNeeded)
            {
                PublishPendingConversationPrefix(result);
                return result;
            }
            if (chosen.Count == 0)
            { result.AllPinned = result.ScanComplete && inboxRetentionSweepNow == now &&
                  inboxRetentionSweepPinned == count && inboxRetentionSweepDeferred == 0;
              return result; }

            WorldState candidate;
            int archivedPending = 0;
            try
            {
                candidate = state.CandidateForExperienceRetention(IsKnownNpcExperienceIdentity);
                var ordinals = new HashSet<long>(chosen.Select(x => x.EnqueueOrdinal));
                candidate.NpcExperienceInbox.Entries.RemoveAll(x => ordinals.Contains(x.EnqueueOrdinal));
                candidate.NpcExperienceInbox.RetiredCount += chosen.Count;
                if (candidate.NpcExperienceInbox.DetailRevision == long.MaxValue) return result;
                candidate.NpcExperienceInbox.DetailRevision++;
                if (cueToPublish != null)
                {
                    EnsureConversationCueIndex(state.ConversationCueArchive);
                    if (cueScanMatchCount == 0 && cuePendingMatchCount == 1)
                    {
                        int prefixCount = cuePendingMatchEntry + 1;
                        CommitPendingConversationPrefix(candidate, cuePendingMatchJournal, prefixCount);
                        cueToPublish.SourceChunkHash = candidate.ConversationArchive.Head;
                        cueToPublish.SourceRecordIndex = prefixCount - 1;
                        archivedPending = prefixCount;
                    }
                    candidate.ConversationCueArchive = conversationCueArchive.Commit(
                        state.ConversationCueArchive, cueToPublish);
                }
                foreach (var row in chosen)
                    if (row.Appraisal != null && row.Appraisal.AppliedMoodDelta != 0)
                    {
                        var floor = candidate.NpcMoodCauseHistoryFloors.Find(x => x.PersonId == row.ObserverId);
                        if (floor == null) candidate.NpcMoodCauseHistoryFloors.Add(new NpcMoodCauseHistoryFloor {
                            PersonId = row.ObserverId, SupportedFromAt = now });
                        else floor.SupportedFromAt = Math.Max(floor.SupportedFromAt, now);
                    }
                candidate = CopyValidated(candidate);
            }
            catch (ArgumentException) { return result; }
            catch (InvalidOperationException) { return result; }
            catch (System.IO.IOException) { return result; }
            if (cueToPublish != null)
                PublishConversationCueIndex(candidate.ConversationCueArchive, cueToPublish);
            state.NpcExperienceInbox = candidate.NpcExperienceInbox;
            state.NpcMoodCauseHistoryFloors = candidate.NpcMoodCauseHistoryFloors;
            if (cueToPublish != null && cueScanMatchCount == 0 && cuePendingMatchCount == 1)
            {
                state.ConversationArchive = candidate.ConversationArchive;
                state.ConversationMemories = candidate.ConversationMemories;
                pendingConversationMemoryEpoch = new object();
            }
            if (cueToPublish != null) state.ConversationCueArchive = candidate.ConversationCueArchive;
            inboxRetentionCursor = Math.Min(inboxRetentionCursor, state.NpcExperienceInbox.Entries.Count);
            inboxRetentionCovered = 0;
            result.Retired = chosen.Count;
            result.ArchivedPending = archivedPending;
            if (cueToPublish != null) cueSourceScan = null;
            return result;
        }

        private bool TryPrepareArchivedCue(NpcExperienceInboxEntry row,
            NpcExperienceInboxRetentionResult result, out ConversationCueArchiveRecord cue,
            out bool impossible)
        {
            cue = null;
            impossible = false;
            if (conversationArchive == null || conversationCueArchive == null ||
                state.ConversationArchive == null || state.ConversationCueArchive == null ||
                !IsConsumedAttributedPlayerAppraisal(row, row.ObserverId))
            { impossible = true; return false; }
            string head = state.ConversationArchive.Head;
            if (cueSourceScan != null && cueScanOrdinal != row.EnqueueOrdinal) return false;
            if (cueSourceScan == null)
            {
                cueSourceScan = conversationArchive.OpenScan(state.ConversationArchive);
                cueScanOrdinal = row.EnqueueOrdinal;
                cueScanHead = head;
                cueScanMatch = null;
                cueScanMatchCount = 0;
                cuePendingJournalCursor = cuePendingEntryCursor = 0;
                cuePendingMatchCount = 0;
                cuePendingMatchJournal = cuePendingMatchEntry = -1;
                cuePendingEpochAtStart = pendingConversationMemoryEpoch;
                cuePendingScanComplete = false;
            }
            AdvanceCueScan(row, result);
            if (cueSourceScan == null || !cueSourceScan.IsComplete) return false;
            AdvancePendingCueScan(row, result);
            if (!cuePendingScanComplete) return false;
            if (cueScanMatchCount + cuePendingMatchCount != 1)
            { impossible = true; cueSourceScan = null; return false; }
            if (cuePendingMatchCount == 1 && cuePendingMatchEntry >= 128)
            { cuePendingPrefixNeeded = true; cuePendingPrefixOrdinal = row.EnqueueOrdinal;
              return false; }
            if (cueScanMatchCount == 1 && ReadConversationCuesAt(cueScanMatch.Address).Count != 0)
            { impossible = true; cueSourceScan = null; return false; }
            cue = new ConversationCueArchiveRecord {
                SourceChunkHash = cueScanMatchCount == 1 ? cueScanMatch.Address.ChunkHash :
                    new string('0', 64),
                SourceRecordIndex = cueScanMatchCount == 1 ? cueScanMatch.Address.RecordIndex : 0,
                PersonId = row.ObserverId,
                ReceiptId = row.ReceiptId, EpisodeId = row.EpisodeId,
                UtteranceId = row.UtteranceId, SpeakerId = row.AttributedSpeakerId,
                ObservedText = row.ObservedTurnText, ObservedAt = row.ObservedGameTime,
                Appraisal = NpcExperienceAppliedAppraisal.CopyValidated(row.Appraisal) };
            return true;
        }

        private void CommitPendingConversationPrefix(WorldState candidate, int journalIndex,
            int prefixCount)
        {
            var journal = state.ConversationMemories[journalIndex];
            var prefix = new NpcConversationMemory { PersonId = journal.PersonId,
                Entries = journal.Entries.Take(prefixCount).ToList() };
            candidate.ConversationArchive = conversationArchive.Commit(
                state.ConversationArchive, new[] { prefix });
            candidate.ConversationMemories = new List<NpcConversationMemory>();
            for (int j = 0; j < state.ConversationMemories.Count; j++)
            {
                var old = state.ConversationMemories[j];
                var detached = new NpcConversationMemory { PersonId = old.PersonId,
                    Entries = new List<NpcMemoryEntry>(old.Entries) };
                if (j == journalIndex) detached.Entries.RemoveRange(0, prefixCount);
                if (detached.Entries.Count > 0) candidate.ConversationMemories.Add(detached);
            }
        }

        private void PublishPendingConversationPrefix(NpcExperienceInboxRetentionResult result)
        {
            try
            {
                var candidate = state.CandidateForExperienceRetention(IsKnownNpcExperienceIdentity);
                CommitPendingConversationPrefix(candidate, cuePendingMatchJournal, 128);
                candidate = CopyValidated(candidate);
                state.ConversationArchive = candidate.ConversationArchive;
                state.ConversationMemories = candidate.ConversationMemories;
                pendingConversationMemoryEpoch = new object();
                result.ArchivedPending = 128;
                int target = state.NpcExperienceInbox.Entries.FindIndex(
                    x => x.EnqueueOrdinal == cuePendingPrefixOrdinal);
                if (target >= 0) inboxRetentionCursor = target;
                inboxRetentionCovered = 0;
                inboxRetentionSweepInbox = null;
                result.ScanComplete = false;
                cueSourceScan = null;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.IO.IOException) { }
        }

        private void AdvancePendingCueScan(NpcExperienceInboxEntry row,
            NpcExperienceInboxRetentionResult result)
        {
            if (cuePendingAdvancedThisCall) return;
            cuePendingAdvancedThisCall = true;
            if (cuePendingScanComplete) return;
            int remaining = 128 - result.ArchiveRowsScanned;
            while (remaining > 0 && cuePendingJournalCursor < state.ConversationMemories.Count)
            {
                var journal = state.ConversationMemories[cuePendingJournalCursor];
                if (cuePendingEntryCursor >= journal.Entries.Count)
                { cuePendingJournalCursor++; cuePendingEntryCursor = 0; continue; }
                int entryIndex = cuePendingEntryCursor++;
                var entry = journal.Entries[entryIndex];
                remaining--; result.PendingMemoryRowsScanned++;
                if (journal.PersonId == row.ObserverId &&
                    ConversationMemoryProvenance.IsSuccessfulSharedExchange(entry) &&
                    entry.PlayerText == row.ObservedTurnText &&
                    Math.Floor(row.ObservedTurnGameTime) == entry.At)
                { cuePendingMatchCount++; cuePendingMatchJournal = cuePendingJournalCursor;
                  cuePendingMatchEntry = entryIndex; }
            }
            cuePendingScanComplete = cuePendingJournalCursor >= state.ConversationMemories.Count;
        }

        private void AdvanceCueScan(NpcExperienceInboxEntry row,
            NpcExperienceInboxRetentionResult result)
        {
            if (cueScanAdvancedThisCall) return;
            cueScanAdvancedThisCall = true;
            if (row == null) { cueSourceScan = null; return; }
            try
            {
                var batch = cueSourceScan.ReadNextBatch(128);
                result.ArchiveRowsScanned += batch.Count;
                foreach (var record in batch)
                    if (IsExactArchivedSource(record, row, row.ObserverId))
                    { cueScanMatch = record; cueScanMatchCount++; }
            }
            catch (System.IO.IOException) { cueSourceScan = null; }
            catch (ArgumentException) { cueSourceScan = null; }
        }

        private static bool NeedsArchiveCue(NpcExperienceInboxEntry row)
        { return row.Origin == NpcExperienceEventOrigin.AttributedSpeech &&
            row.Status == NpcExperienceInboxStatus.Consumed && row.Appraisal != null &&
            row.AttributedSpeakerId == NpcDirectedRelationship.PlayerTargetId; }

        private bool ProjectedSourceCanBeRetired(NpcExperienceInboxEntry row)
        {
            if (row.Origin == NpcExperienceEventOrigin.AttributedSpeech)
            {
                if (row.ReceiptId == null || !row.ReceiptId.StartsWith("native-heard-v2-", StringComparison.Ordinal))
                    return true; // The old caller-built admission API is closed.
                int separator = row.ReceiptId.LastIndexOf('-');
                long ordinal;
                return separator >= 0 && long.TryParse(row.ReceiptId.Substring(separator + 1), out ordinal) &&
                    state.NpcObservedSpeechSources.Exists(x => x.PersonId == row.ObserverId &&
                        x.LastProjectedOrdinal >= ordinal);
            }
            switch (row.EventKind)
            {
                case NpcExperienceEventKind.StatusOnset:
                    if (row.SourceKind != NpcExperienceSourceKind.Poison ||
                        row.StatusOnsetSequence < 1) return false;
                    string suffix = StableObserverToken(row.ObserverId) + ":" +
                        row.StatusOnsetSequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return row.ReceiptId == "poison-receipt:" + suffix &&
                        row.EpisodeId == "poison-episode:" + suffix &&
                        row.OutcomeId == "poison-outcome:" + suffix &&
                        state.NpcPoisonOnsets.Exists(x =>
                        x.PersonId == row.ObserverId && x.LastProjectedSequence >= row.StatusOnsetSequence);
                case NpcExperienceEventKind.Injury:
                case NpcExperienceEventKind.Healing:
                    return row.SourceKind == NpcExperienceSourceKind.Health &&
                        row.SourceVitalsRevision > 0 && state.NpcActors.Exists(x =>
                        x.PersonId == row.ObserverId &&
                        x.HealthExperienceProjectedThroughVitalsRevision >= row.SourceVitalsRevision);
                case NpcExperienceEventKind.ItemReceived:
                    if (row.SourceKind != NpcExperienceSourceKind.ItemTransfer) return false;
                    var matchingAction = state.NpcActions.Find(x => x.ItemTransferReceipt != null &&
                        x.ItemTransferReceipt.TransactionId == row.ReceiptId);
                    if (matchingAction == null)
                        return row.EnqueueOrdinal > state.NpcExperienceInbox.LegacyImportedRevision;
                    if (matchingAction.ProjectedItemTransferReceipt == null) return false;
                    try
                    {
                        var eventEvidence = matchingAction.ItemTransferReceipt.ToNativeEvent(matchingAction.Id);
                        return eventEvidence.DeduplicationKey == row.DeduplicationKey &&
                            HasMatchingItemTransferOutcome(matchingAction, eventEvidence);
                    }
                    catch (ArgumentException) { return false; }
                case NpcExperienceEventKind.AppointmentAttended:
                case NpcExperienceEventKind.AppointmentNoShow:
                    return row.SourceKind == NpcExperienceSourceKind.Appointment &&
                        state.Appointments.Exists(x =>
                        x.ExperienceProjection == NpcAppointmentExperienceProjection.Projected &&
                        NpcAppointmentExperience.TryCreate(x, out var evidence) &&
                        evidence.DeduplicationKey == row.DeduplicationKey);
                default:
                    return false; // Unclassified imported native evidence stays visible.
            }
        }

        private HashSet<NpcExperienceInboxEntry> ProtectedSpeechContextRows(
            List<NpcExperienceInboxEntry> rows, NpcExperienceInboxRetentionResult result)
        {
            var protectedRows = new HashSet<NpcExperienceInboxEntry>();
            var speechByObserver = rows.Where(x => x.Origin == NpcExperienceEventOrigin.AttributedSpeech)
                .GroupBy(x => x.ObserverId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => group.OrderByDescending(x => x.ObservedGameTime)
                        .ThenByDescending(x => x.DeduplicationKey, StringComparer.Ordinal).ToList(),
                    StringComparer.Ordinal);
            result.ReferenceRowsInspected += rows.Count;
            foreach (var row in rows)
                if (row.Status == NpcExperienceInboxStatus.Pending)
                {
                    List<NpcExperienceInboxEntry> owned;
                    if (speechByObserver.TryGetValue(row.ObserverId, out owned))
                        foreach (var prior in SelectPreviousSpeechEntries(owned, row, true,
                            () => result.ReferenceRowsInspected++)) protectedRows.Add(prior);
                }
            foreach (var source in state.NpcObservedSpeechSources)
                if (source.Pending != null)
                {
                    List<NpcExperienceInboxEntry> owned;
                    if (speechByObserver.TryGetValue(source.PersonId, out owned))
                        foreach (var prior in SelectPreviousSpeechEntries(owned, source.Pending.Entry, true,
                            () => result.ReferenceRowsInspected++)) protectedRows.Add(prior);
                }
            foreach (var pair in speechByObserver)
            {
                var frontier = new NpcExperienceInboxEntry { ObserverId = pair.Key,
                    ObservedGameTime = double.MaxValue, DeduplicationKey = "\uffff" };
                foreach (var prior in SelectPreviousSpeechEntries(pair.Value, frontier, true,
                    () => result.ReferenceRowsInspected++)) protectedRows.Add(prior);
            }
            return protectedRows;
        }

        private static HashSet<NpcExperienceInboxEntry> RecentExperienceRows(
            List<NpcExperienceInboxEntry> rows, NpcExperienceInboxRetentionResult result)
        {
            result.ReferenceRowsInspected += rows.Count;
            return new HashSet<NpcExperienceInboxEntry>(rows
                .Where(x => x.Status != NpcExperienceInboxStatus.Pending)
                .GroupBy(x => x.ObserverId, StringComparer.Ordinal)
                .SelectMany(group => group.OrderByDescending(x => x.UpdatedAt)
                    .ThenByDescending(x => x.EnqueueOrdinal).Take(32)));
        }
    }
}
