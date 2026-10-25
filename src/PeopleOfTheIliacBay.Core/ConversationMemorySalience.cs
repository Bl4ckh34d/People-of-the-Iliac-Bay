using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Immutable, person-scoped profile and accepted-appraisal context for one compaction group.</summary>
    public sealed class ConversationMemoryCompactionContextSnapshot
    {
        public const int MaximumCandidateCount = 8;

        private readonly ReadOnlyCollection<ConversationMemoryCompactionEmotionCue> emotionCues;

        public string PersonId { get; private set; }
        public string PersonalityDescription { get; private set; }
        public string ContextFingerprint { get; private set; }
        public IReadOnlyList<ConversationMemoryCompactionEmotionCue> EmotionCues { get { return emotionCues; } }

        internal ConversationMemoryCompactionContextSnapshot(string personId, string personalityDescription,
            IList<ConversationMemoryCompactionEmotionCue> cues, string contextFingerprint)
        {
            if (string.IsNullOrWhiteSpace(personId)) throw new ArgumentException("Person identity is required.", nameof(personId));
            if (string.IsNullOrWhiteSpace(contextFingerprint)) throw new ArgumentException("Context fingerprint is required.", nameof(contextFingerprint));
            PersonId = personId;
            PersonalityDescription = personalityDescription;
            ContextFingerprint = contextFingerprint;
            emotionCues = new ReadOnlyCollection<ConversationMemoryCompactionEmotionCue>(
                cues == null ? new List<ConversationMemoryCompactionEmotionCue>() :
                    new List<ConversationMemoryCompactionEmotionCue>(cues));
        }

        public bool TryGetEmotionCue(SharedConversationArchive.RecordAddress address,
            out ConversationMemoryCompactionEmotionCue cue)
        {
            cue = null;
            if (address == null) return false;
            foreach (ConversationMemoryCompactionEmotionCue candidate in emotionCues)
            {
                if (candidate != null && ConversationMemoryCompactionEmotionCue.SameAddress(candidate.Address, address))
                {
                    cue = candidate;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Recorded appraisal values bound to one exact archived player exchange.</summary>
    public sealed class ConversationMemoryCompactionEmotionCue
    {
        public SharedConversationArchive.RecordAddress Address { get; private set; }
        public string ReceiptId { get; private set; }
        public string EpisodeId { get; private set; }
        public string UtteranceId { get; private set; }
        public string ObserverId { get; private set; }
        public string SpeakerId { get; private set; }
        public double ObservedTurnGameTime { get; private set; }
        public string ObservedTurnText { get; private set; }
        public int MoodDelta { get; private set; }
        public int FearDelta { get; private set; }
        public int TrustDelta { get; private set; }
        public int ResentmentDelta { get; private set; }
        public int AffinityDelta { get; private set; }
        public int AppliedMoodDelta { get; private set; }
        public int AppliedAffinityDelta { get; private set; }

        internal ConversationMemoryCompactionEmotionCue(SharedConversationArchive.RecordAddress address,
            string receiptId, string episodeId, string utteranceId, string observerId, string speakerId,
            double observedTurnGameTime, string observedTurnText, NpcExperienceAppliedAppraisal appraisal)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (appraisal == null) throw new ArgumentNullException(nameof(appraisal));
            Address = address;
            ReceiptId = receiptId;
            EpisodeId = episodeId;
            UtteranceId = utteranceId;
            ObserverId = observerId;
            SpeakerId = speakerId;
            ObservedTurnGameTime = observedTurnGameTime;
            ObservedTurnText = observedTurnText;
            MoodDelta = appraisal.MoodDelta;
            FearDelta = appraisal.FearDelta;
            TrustDelta = appraisal.TrustDelta;
            ResentmentDelta = appraisal.ResentmentDelta;
            AffinityDelta = appraisal.AffinityDelta;
            AppliedMoodDelta = appraisal.AppliedMoodDelta;
            AppliedAffinityDelta = appraisal.AppliedAffinityDelta;
        }

        internal static bool SameAddress(SharedConversationArchive.RecordAddress first,
            SharedConversationArchive.RecordAddress second)
        {
            return first != null && second != null &&
                string.Equals(first.ArchiveId, second.ArchiveId, StringComparison.Ordinal) &&
                string.Equals(first.CheckpointHead, second.CheckpointHead, StringComparison.Ordinal) &&
                string.Equals(first.ChunkHash, second.ChunkHash, StringComparison.Ordinal) &&
                first.RecordIndex == second.RecordIndex &&
                string.Equals(first.PersonId, second.PersonId, StringComparison.Ordinal);
        }
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>
        /// Captures only the already-saved stable personality and consumed appraisals that can be bound to one
        /// exact archived source. Archived conversation times are floored to whole game seconds; the accepted
        /// receipt retains its precise native timestamp, and matching explicitly compares floor(receipt time)
        /// with the archived second.
        /// </summary>
        public ConversationMemoryCompactionContextSnapshot CaptureConversationMemoryCompactionContext(string personId,
            IReadOnlyList<SharedConversationArchive.ScanRecord> candidateRecords)
        {
            if (!NpcExperienceAppraisalContract.ValidIdentity(personId, 240))
                throw new ArgumentException("A valid person identity is required.", nameof(personId));
            if (candidateRecords == null) throw new ArgumentNullException(nameof(candidateRecords));
            int candidateCount = candidateRecords.Count;
            if (candidateCount < 0 || candidateCount > ConversationMemoryCompactionContextSnapshot.MaximumCandidateCount)
                throw new ArgumentOutOfRangeException(nameof(candidateRecords), "Compaction context is limited to eight monthly candidates.");

            ConversationArchiveCheckpointView currentCheckpoint = null;
            if (candidateCount > 0)
            {
                if (state.ConversationArchive == null)
                    throw new ArgumentException("Candidates cannot be bound without the store's saved archive checkpoint.", nameof(candidateRecords));
                currentCheckpoint = CaptureConversationArchiveCheckpoint();
            }

            string archiveId = null;
            string checkpointHead = null;
            var candidates = new List<SharedConversationArchive.ScanRecord>(candidateCount);
            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                SharedConversationArchive.ScanRecord record = candidateRecords[candidateIndex];
                if (record == null || record.Address == null || record.Entry == null ||
                    !NpcExperienceAppraisalContract.ValidIdentity(record.Address.PersonId, 240) ||
                    record.Address.PersonId != personId ||
                    record.Address.ArchiveId != currentCheckpoint.ArchiveId || record.Address.CheckpointHead != currentCheckpoint.Head ||
                    !ConversationArchiveCheckpoint.Hex(record.Address.ArchiveId, 32) ||
                    record.Address.CheckpointHead == null || record.Address.CheckpointHead.Length != 0 &&
                        !ConversationArchiveCheckpoint.Hex(record.Address.CheckpointHead, 64) ||
                    !ConversationArchiveCheckpoint.Hex(record.Address.ChunkHash, 64) || record.Address.RecordIndex < 0)
                    throw new ArgumentException("All source candidates must have exact valid addresses for this person.", nameof(candidateRecords));
                if (candidates.Any(existing => ConversationMemoryCompactionEmotionCue.SameAddress(
                    existing.Address, record.Address)))
                    throw new ArgumentException("Duplicate source candidate address.", nameof(candidateRecords));
                if (archiveId == null)
                {
                    archiveId = record.Address.ArchiveId;
                    checkpointHead = record.Address.CheckpointHead;
                }
                else if (archiveId != record.Address.ArchiveId || checkpointHead != record.Address.CheckpointHead)
                    throw new ArgumentException("Source candidates must belong to one immutable archive checkpoint.", nameof(candidateRecords));
                candidates.Add(record);
            }

            // ReadNpcPersonality returns a detached, validated profile and never generates one.
            NpcPersonalityProfile savedProfile = ReadNpcPersonality(personId);
            string personalityDescription = savedProfile == null ? null : savedProfile.Description();
            var matchedReceiptsByCandidate = new List<List<NpcExperienceInboxEntry>>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++) matchedReceiptsByCandidate.Add(new List<NpcExperienceInboxEntry>());
            NpcExperienceInbox savedInbox = state.NpcExperienceInbox;
            if (savedInbox == null || savedInbox.Entries == null)
                throw new InvalidOperationException("Saved NPC experience inbox is unavailable.");
            foreach (NpcExperienceInboxEntry rawReceipt in savedInbox.Entries)
            {
                if (rawReceipt == null || rawReceipt.Status != NpcExperienceInboxStatus.Consumed || rawReceipt.Appraisal == null ||
                    rawReceipt.Origin != NpcExperienceEventOrigin.AttributedSpeech || rawReceipt.ObserverId != personId) continue;
                NpcExperienceInboxEntry receipt;
                try { receipt = NpcExperienceInboxEntry.CopyValidated(rawReceipt); }
                catch (ArgumentException) { continue; }
                if (!IsConsumedAttributedPlayerAppraisal(receipt, personId)) continue;
                int matchedCandidate = -1;
                int matchCount = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!IsExactArchivedSource(candidates[i], receipt, personId)) continue;
                    matchedCandidate = i;
                    matchCount++;
                }
                // A receipt matching more than one archive row is ambiguous. It must not taint any candidate.
                if (matchCount == 1) matchedReceiptsByCandidate[matchedCandidate].Add(receipt);
            }

            var cues = new List<ConversationMemoryCompactionEmotionCue>();
            for (int i = 0; i < candidates.Count; i++)
            {
                List<NpcExperienceInboxEntry> receipts = matchedReceiptsByCandidate[i];
                var savedCues = ReadConversationCuesAt(candidates[i].Address).Where(x =>
                    x.ObservedText == candidates[i].Entry.PlayerText &&
                    Math.Floor(x.ObservedAt) == candidates[i].Entry.At).ToList();
                // Exactly one persisted appraisal may describe a source. Ambiguity never becomes a neutral cue.
                if (receipts.Count + savedCues.Count != 1) continue;
                if (receipts.Count == 1)
                {
                    NpcExperienceInboxEntry receipt = receipts[0];
                    cues.Add(new ConversationMemoryCompactionEmotionCue(candidates[i].Address,
                        receipt.ReceiptId, receipt.EpisodeId, receipt.UtteranceId, receipt.ObserverId,
                        receipt.AttributedSpeakerId, receipt.ObservedTurnGameTime, receipt.ObservedTurnText,
                        receipt.Appraisal));
                }
                else
                    cues.Add(savedCues[0].ToCue(candidates[i].Address.ArchiveId,
                        candidates[i].Address.CheckpointHead));
            }

            string fingerprint = ComputeContextFingerprint(personId, personalityDescription, candidates, cues);
            return new ConversationMemoryCompactionContextSnapshot(personId, personalityDescription, cues, fingerprint);
        }

        private static bool IsConsumedAttributedPlayerAppraisal(NpcExperienceInboxEntry receipt, string personId)
        {
            return receipt != null && receipt.Status == NpcExperienceInboxStatus.Consumed && receipt.Appraisal != null &&
                receipt.Origin == NpcExperienceEventOrigin.AttributedSpeech && receipt.ObserverId == personId &&
                receipt.SubjectPersonId == personId && receipt.AttributedSpeakerId == NpcDirectedRelationship.PlayerTargetId &&
                receipt.ObservedTurnObserverId == personId && receipt.ObservedTurnEpisodeId == receipt.EpisodeId &&
                receipt.ObservedTurnSpeakerId == NpcDirectedRelationship.PlayerTargetId &&
                receipt.ObservedTurnReceiptId == receipt.ReceiptId && receipt.ObservedTurnGameTime == receipt.ObservedGameTime &&
                !string.IsNullOrEmpty(receipt.UtteranceId) && !string.IsNullOrEmpty(receipt.ReceiptId) &&
                !string.IsNullOrEmpty(receipt.EpisodeId) && !string.IsNullOrEmpty(receipt.ObservedTurnText) &&
                !double.IsNaN(receipt.ObservedGameTime) && !double.IsInfinity(receipt.ObservedGameTime) && receipt.ObservedGameTime >= 0;
        }

        private static bool IsExactArchivedSource(SharedConversationArchive.ScanRecord candidate,
            NpcExperienceInboxEntry receipt, string personId)
        {
            NpcMemoryEntry source = candidate.Entry;
            return candidate.Address.PersonId == personId && ConversationMemoryProvenance.IsSuccessfulSharedExchange(source) &&
                source.PlayerText == receipt.ObservedTurnText &&
                // Conversation memory intentionally stores whole-second time precision (Math.Floor at write).
                Math.Floor(receipt.ObservedTurnGameTime) == source.At;
        }

        private static string ComputeContextFingerprint(string personId, string personalityDescription,
            IList<SharedConversationArchive.ScanRecord> candidates,
            IList<ConversationMemoryCompactionEmotionCue> cues)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("conversation-memory-salience-v1");
                WriteNullableString(writer, personId);
                WriteNullableString(writer, personalityDescription);
                var orderedCandidates = candidates.OrderBy(x => x.Address.ArchiveId, StringComparer.Ordinal)
                    .ThenBy(x => x.Address.CheckpointHead, StringComparer.Ordinal)
                    .ThenBy(x => x.Address.ChunkHash, StringComparer.Ordinal)
                    .ThenBy(x => x.Address.RecordIndex)
                    .ThenBy(x => x.Address.PersonId, StringComparer.Ordinal).ToList();
                writer.Write(orderedCandidates.Count);
                foreach (SharedConversationArchive.ScanRecord record in orderedCandidates)
                    WriteAddress(writer, record.Address);

                var orderedCues = cues.OrderBy(x => x.Address.ArchiveId, StringComparer.Ordinal)
                    .ThenBy(x => x.Address.CheckpointHead, StringComparer.Ordinal)
                    .ThenBy(x => x.Address.ChunkHash, StringComparer.Ordinal)
                    .ThenBy(x => x.Address.RecordIndex)
                    .ThenBy(x => x.Address.PersonId, StringComparer.Ordinal).ToList();
                writer.Write(orderedCues.Count);
                foreach (ConversationMemoryCompactionEmotionCue cue in orderedCues)
                {
                    WriteAddress(writer, cue.Address);
                    WriteNullableString(writer, cue.ReceiptId);
                    WriteNullableString(writer, cue.EpisodeId);
                    WriteNullableString(writer, cue.UtteranceId);
                    WriteNullableString(writer, cue.ObserverId);
                    WriteNullableString(writer, cue.SpeakerId);
                    writer.Write(cue.ObservedTurnGameTime.ToString("R", CultureInfo.InvariantCulture));
                    WriteNullableString(writer, cue.ObservedTurnText);
                    writer.Write(cue.MoodDelta);
                    writer.Write(cue.FearDelta);
                    writer.Write(cue.TrustDelta);
                    writer.Write(cue.ResentmentDelta);
                    writer.Write(cue.AffinityDelta);
                    writer.Write(cue.AppliedMoodDelta);
                    writer.Write(cue.AppliedAffinityDelta);
                }
                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void WriteAddress(BinaryWriter writer, SharedConversationArchive.RecordAddress address)
        {
            WriteNullableString(writer, address.ArchiveId);
            WriteNullableString(writer, address.CheckpointHead);
            WriteNullableString(writer, address.ChunkHash);
            writer.Write(address.RecordIndex);
            WriteNullableString(writer, address.PersonId);
        }

        private static void WriteNullableString(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null) writer.Write(value);
        }
    }
}
