using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcKnowledgeEvidenceOrigin
    {
        NativeObservation = 1,
        NativeOutcomeReceipt = 2,
        AuthoredRecord = 3,
        ConversationReceipt = 4,
        AttributedReport = 5
    }

    /// <summary>Immutable native evidence input to one ledger revision.</summary>
    public sealed class NpcKnowledgeUpdate
    {
        private NpcKnowledgeUpdate() { }
        public string ActorId { get; private set; }
        public string EntryId { get; private set; }
        public string PropositionId { get; private set; }
        public string ValueText { get; private set; }
        public NpcDisclosureEpistemicKind EpistemicKind { get; private set; }
        public NpcDisclosureProtection Protection { get; private set; }
        public NpcKnowledgeEvidenceOrigin EvidenceOrigin { get; private set; }
        public string SourceId { get; private set; }
        public string UpdateReceiptId { get; private set; }
        public string EvidenceReceiptId { get; private set; }
        public long SourceRevision { get; private set; }
        public double CapturedGameTime { get; private set; }
        public string AttributionId { get; private set; }

        /// <summary>Only a native observation, outcome receipt, or authored record may assert verified truth.</summary>
        public static NpcKnowledgeUpdate FromNativeEvidence(string actorId, string entryId, string propositionId,
            string valueText, NpcDisclosureProtection protection, NpcKnowledgeEvidenceOrigin evidenceOrigin,
            string sourceId, string updateReceiptId, long sourceRevision, double capturedGameTime,
            string evidenceReceiptId = null)
        {
            if (!IsNativeOrigin(evidenceOrigin)) throw new ArgumentException("Attributed sources cannot assert verified truth.");
            return Create(actorId, entryId, propositionId, valueText, NpcDisclosureEpistemicKind.VerifiedTruth,
                protection, evidenceOrigin, sourceId, updateReceiptId, sourceRevision, capturedGameTime, null, evidenceReceiptId);
        }

        /// <summary>Speech and reports remain attributed; they cannot become world truth through this path.</summary>
        public static NpcKnowledgeUpdate FromAttributedClaim(string actorId, string entryId, string propositionId,
            string valueText, NpcDisclosureEpistemicKind epistemicKind, NpcDisclosureProtection protection,
            NpcKnowledgeEvidenceOrigin evidenceOrigin, string sourceId, string updateReceiptId,
            long sourceRevision, double capturedGameTime, string attributionId, string evidenceReceiptId = null)
        {
            if (!IsAttributedOrigin(evidenceOrigin)) throw new ArgumentException("Native outcome evidence must use the verified-fact path.");
            if (epistemicKind != NpcDisclosureEpistemicKind.AttributedBelief &&
                epistemicKind != NpcDisclosureEpistemicKind.Rumor &&
                epistemicKind != NpcDisclosureEpistemicKind.PlayerClaim)
                throw new ArgumentException("Attributed updates must preserve an attributed epistemic kind.");
            return Create(actorId, entryId, propositionId, valueText, epistemicKind, protection,
                evidenceOrigin, sourceId, updateReceiptId, sourceRevision, capturedGameTime, attributionId, evidenceReceiptId);
        }

        private static NpcKnowledgeUpdate Create(string actorId, string entryId, string propositionId,
            string valueText, NpcDisclosureEpistemicKind epistemicKind, NpcDisclosureProtection protection,
            NpcKnowledgeEvidenceOrigin evidenceOrigin, string sourceId, string updateReceiptId,
            long sourceRevision, double capturedGameTime, string attributionId, string evidenceReceiptId)
        {
            if (!NpcKnowledgeValidation.ValidId(actorId, 240) || !NpcKnowledgeValidation.ValidId(entryId, 128) ||
                !NpcKnowledgeValidation.ValidId(propositionId, 128) || !NpcKnowledgeValidation.ValidText(valueText, 128) ||
                !NpcKnowledgeValidation.ValidId(sourceId, 128) || !NpcKnowledgeValidation.ValidId(updateReceiptId, 128) ||
                (evidenceReceiptId != null && !NpcKnowledgeValidation.ValidId(evidenceReceiptId, 128)) ||
                sourceRevision < 0 || !NpcKnowledgeValidation.Finite(capturedGameTime) || capturedGameTime < 0 ||
                !Enum.IsDefined(typeof(NpcDisclosureEpistemicKind), epistemicKind) ||
                !Enum.IsDefined(typeof(NpcDisclosureProtection), protection) ||
                !Enum.IsDefined(typeof(NpcKnowledgeEvidenceOrigin), evidenceOrigin) ||
                (attributionId != null && !NpcKnowledgeValidation.ValidId(attributionId, 240)))
                throw new ArgumentException("Invalid actor-owned knowledge update.");
            if ((epistemicKind == NpcDisclosureEpistemicKind.AttributedBelief ||
                 epistemicKind == NpcDisclosureEpistemicKind.Rumor ||
                 epistemicKind == NpcDisclosureEpistemicKind.PlayerClaim) && attributionId == null)
                throw new ArgumentException("Attributed knowledge requires an attribution identity.");
            if ((evidenceOrigin == NpcKnowledgeEvidenceOrigin.NativeOutcomeReceipt ||
                evidenceOrigin == NpcKnowledgeEvidenceOrigin.ConversationReceipt) && evidenceReceiptId == null)
                throw new ArgumentException("Receipt-backed evidence requires its actual evidence receipt identity.");
            return new NpcKnowledgeUpdate { ActorId = actorId, EntryId = entryId, PropositionId = propositionId,
                ValueText = valueText, EpistemicKind = epistemicKind, Protection = protection,
                EvidenceOrigin = evidenceOrigin, SourceId = sourceId, UpdateReceiptId = updateReceiptId,
                EvidenceReceiptId = evidenceReceiptId,
                SourceRevision = sourceRevision, CapturedGameTime = capturedGameTime, AttributionId = attributionId };
        }

        internal NpcKnowledgeEntryState ToEntry(string supersedesReceiptId)
        {
            return new NpcKnowledgeEntryState { EntryId = EntryId, ActorId = ActorId, PropositionId = PropositionId,
                ValueText = ValueText, EpistemicKind = EpistemicKind, Protection = Protection,
                EvidenceOrigin = EvidenceOrigin, SourceId = SourceId, UpdateReceiptId = UpdateReceiptId,
                EvidenceReceiptId = EvidenceReceiptId,
                SourceRevision = SourceRevision, CapturedGameTime = CapturedGameTime,
                AttributionId = AttributionId, SupersedesUpdateReceiptId = supersedesReceiptId };
        }
        internal static bool IsNativeOrigin(NpcKnowledgeEvidenceOrigin origin)
        {
            return origin == NpcKnowledgeEvidenceOrigin.NativeObservation ||
                origin == NpcKnowledgeEvidenceOrigin.NativeOutcomeReceipt || origin == NpcKnowledgeEvidenceOrigin.AuthoredRecord;
        }
        internal static bool IsAttributedOrigin(NpcKnowledgeEvidenceOrigin origin)
        { return origin == NpcKnowledgeEvidenceOrigin.ConversationReceipt || origin == NpcKnowledgeEvidenceOrigin.AttributedReport; }
    }

    /// <summary>Detached fact view for one topic of one actor, preserving the source receipt outside renderer text.</summary>
    public sealed class NpcKnowledgeFactView
    {
        internal NpcKnowledgeFactView(NpcKnowledgeEntryState entry)
        {
            Statement = NpcDisclosureStatement.Create(entry.EntryId, entry.ActorId, entry.PropositionId,
                entry.ValueText, entry.EpistemicKind, entry.Protection, entry.SourceId,
                entry.SourceRevision, entry.CapturedGameTime, entry.AttributionId,
                entry.EvidenceReceiptId);
            EvidenceOrigin = entry.EvidenceOrigin; UpdateReceiptId = entry.UpdateReceiptId;
            EvidenceReceiptId = entry.EvidenceReceiptId;
            SupersedesUpdateReceiptId = entry.SupersedesUpdateReceiptId;
        }
        public NpcDisclosureStatement Statement { get; private set; }
        public NpcKnowledgeEvidenceOrigin EvidenceOrigin { get; private set; }
        public string UpdateReceiptId { get; private set; }
        public string EvidenceReceiptId { get; private set; }
        public string SupersedesUpdateReceiptId { get; private set; }
    }

    /// <summary>A read-only, single-actor view with bounded point queries instead of a prompt-ready fact dump.</summary>
    public sealed class NpcKnowledgeSnapshot
    {
        private readonly Dictionary<string, NpcKnowledgeFactView> byProposition;
        internal NpcKnowledgeSnapshot(string actorId, string saveEpochId, long revision,
            IList<NpcKnowledgeEntryState> entries)
        {
            ActorId = actorId; SaveEpochId = saveEpochId; Revision = revision;
            byProposition = new Dictionary<string, NpcKnowledgeFactView>(StringComparer.Ordinal);
            foreach (NpcKnowledgeEntryState entry in entries)
                byProposition.Add(entry.PropositionId, new NpcKnowledgeFactView(entry));
        }
        public string ActorId { get; private set; }
        public string SaveEpochId { get; private set; }
        public long Revision { get; private set; }

        /// <summary>A missing topic is unknown; this API never searches another actor's ledger.</summary>
        public bool TryGet(string propositionId, out NpcKnowledgeFactView fact)
        {
            fact = null;
            if (!NpcKnowledgeValidation.ValidId(propositionId, 128)) return false;
            return byProposition.TryGetValue(propositionId, out fact);
        }

        /// <summary>Returns bounded topic IDs and epistemic labels only; fact values and source traces stay private.</summary>
        public IList<NpcDisclosurePublicTopic> PublicTopicMetadata(int maximum)
        {
            if (maximum < 1 || maximum > 64) throw new ArgumentOutOfRangeException("maximum");
            var result = new List<NpcDisclosurePublicTopic>();
            foreach (var pair in byProposition)
            {
                NpcDisclosureStatement statement = pair.Value.Statement;
                if (statement.Protection != NpcDisclosureProtection.Public ||
                    (statement.EpistemicKind != NpcDisclosureEpistemicKind.VerifiedTruth &&
                     statement.EpistemicKind != NpcDisclosureEpistemicKind.AttributedBelief &&
                     statement.EpistemicKind != NpcDisclosureEpistemicKind.Rumor &&
                     statement.EpistemicKind != NpcDisclosureEpistemicKind.PlayerClaim) ||
                    !NpcDisclosurePublicTopic.IsModelSelectableId(statement.PropositionId)) continue;
                result.Add(new NpcDisclosurePublicTopic(statement.PropositionId, statement.EpistemicKind));
                if (result.Count >= maximum) break;
            }
            return new ReadOnlyCollection<NpcDisclosurePublicTopic>(result);
        }
    }

    /// <summary>Pure copy-on-write actor-owned knowledge ledger. Persistence/locking belongs to its caller.</summary>
    public static class NpcKnowledgeLedger
    {
        public const int CurrentSchemaVersion = 1;
        public const int MaximumFacts = 256;
        public const int MaximumAppliedReceipts = 1024;

        public static NpcKnowledgeState Create(string actorId, string saveEpochId)
        {
            if (!NpcKnowledgeValidation.ValidId(actorId, 240) || !NpcKnowledgeValidation.ValidId(saveEpochId, 128))
                throw new ArgumentException("Invalid actor or save epoch for knowledge ledger.");
            return new NpcKnowledgeState { SchemaVersion = CurrentSchemaVersion, ActorId = actorId,
                SaveEpochId = saveEpochId, Revision = 0, LastUpdatedGameTime = 0,
                Entries = new List<NpcKnowledgeEntryState>(), AppliedUpdateReceiptIds = new List<string>() };
        }

        public static bool TryCapture(NpcKnowledgeState state, string actorId, string saveEpochId,
            out NpcKnowledgeSnapshot snapshot)
        {
            snapshot = null;
            if (state == null || !NpcKnowledgeValidation.ValidId(actorId, 240) ||
                !NpcKnowledgeValidation.ValidId(saveEpochId, 128)) return false;
            NpcKnowledgeState copy;
            try { copy = CopyValidated(state); }
            catch (ArgumentException) { return false; }
            if (!string.Equals(copy.ActorId, actorId, StringComparison.Ordinal) ||
                !string.Equals(copy.SaveEpochId, saveEpochId, StringComparison.Ordinal)) return false;
            snapshot = new NpcKnowledgeSnapshot(copy.ActorId, copy.SaveEpochId, copy.Revision, copy.Entries);
            return true;
        }

        public static bool TryApply(NpcKnowledgeState state, NpcKnowledgeUpdate update, string callerActorId,
            string expectedSaveEpochId, long expectedRevision, out NpcKnowledgeState updated)
        {
            updated = null;
            if (state == null || update == null || !NpcKnowledgeValidation.ValidId(callerActorId, 240) ||
                !NpcKnowledgeValidation.ValidId(expectedSaveEpochId, 128) || expectedRevision < 0) return false;
            NpcKnowledgeState copy;
            try { copy = CopyValidated(state); }
            catch (ArgumentException) { return false; }
            if (!string.Equals(copy.ActorId, callerActorId, StringComparison.Ordinal) ||
                !string.Equals(copy.ActorId, update.ActorId, StringComparison.Ordinal) ||
                !string.Equals(copy.SaveEpochId, expectedSaveEpochId, StringComparison.Ordinal) ||
                copy.Revision != expectedRevision || copy.Revision == long.MaxValue ||
                update.CapturedGameTime < copy.LastUpdatedGameTime ||
                copy.AppliedUpdateReceiptIds.Contains(update.UpdateReceiptId) ||
                !ValidUpdate(update)) return false;

            int existingIndex = -1;
            for (int i = 0; i < copy.Entries.Count; i++)
            {
                if (string.Equals(copy.Entries[i].PropositionId, update.PropositionId, StringComparison.Ordinal))
                    existingIndex = i;
                if (string.Equals(copy.Entries[i].EntryId, update.EntryId, StringComparison.Ordinal)) return false;
            }
            if (existingIndex < 0 && copy.Entries.Count >= MaximumFacts) return false;
            if (copy.AppliedUpdateReceiptIds.Count >= MaximumAppliedReceipts) return false;

            string supersedes = existingIndex >= 0 ? copy.Entries[existingIndex].UpdateReceiptId : null;
            NpcKnowledgeEntryState entry = update.ToEntry(supersedes);
            if (existingIndex >= 0) copy.Entries[existingIndex] = entry;
            else copy.Entries.Add(entry);
            copy.AppliedUpdateReceiptIds.Add(update.UpdateReceiptId);
            copy.Revision++;
            copy.LastUpdatedGameTime = update.CapturedGameTime;
            updated = copy;
            return true;
        }

        public static NpcKnowledgeState CopyValidated(NpcKnowledgeState state)
        {
            if (state == null || state.SchemaVersion != CurrentSchemaVersion ||
                !NpcKnowledgeValidation.ValidId(state.ActorId, 240) || !NpcKnowledgeValidation.ValidId(state.SaveEpochId, 128) ||
                state.Revision < 0 || !NpcKnowledgeValidation.Finite(state.LastUpdatedGameTime) || state.LastUpdatedGameTime < 0 ||
                state.Entries == null || state.AppliedUpdateReceiptIds == null || state.Entries.Count > MaximumFacts ||
                state.AppliedUpdateReceiptIds.Count > MaximumAppliedReceipts)
                throw new ArgumentException("Invalid persisted actor-owned knowledge state.");
            var copy = new NpcKnowledgeState { SchemaVersion = state.SchemaVersion, ActorId = state.ActorId,
                SaveEpochId = state.SaveEpochId, Revision = state.Revision, LastUpdatedGameTime = state.LastUpdatedGameTime,
                Entries = new List<NpcKnowledgeEntryState>(), AppliedUpdateReceiptIds = new List<string>() };
            var receiptSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < state.AppliedUpdateReceiptIds.Count; i++)
            {
                string receiptId = state.AppliedUpdateReceiptIds[i];
                if (!NpcKnowledgeValidation.ValidId(receiptId, 128) || !receiptSet.Add(receiptId))
                    throw new ArgumentException("Invalid or duplicate applied knowledge receipt.");
                copy.AppliedUpdateReceiptIds.Add(receiptId);
            }
            var propositions = new HashSet<string>(StringComparer.Ordinal);
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcKnowledgeEntryState entry in state.Entries)
            {
                ValidateEntry(state, entry, receiptSet);
                if (!propositions.Add(entry.PropositionId) || !entryIds.Add(entry.EntryId))
                    throw new ArgumentException("Duplicate current knowledge proposition or entry identity.");
                copy.Entries.Add(Clone(entry));
            }
            return copy;
        }

        private static bool ValidUpdate(NpcKnowledgeUpdate update)
        {
            try
            {
                // Re-run public factory invariants on an immutable update before committing it.
                NpcKnowledgeUpdate copy = update.EpistemicKind == NpcDisclosureEpistemicKind.VerifiedTruth
                    ? NpcKnowledgeUpdate.FromNativeEvidence(update.ActorId, update.EntryId, update.PropositionId,
                        update.ValueText, update.Protection, update.EvidenceOrigin, update.SourceId,
                        update.UpdateReceiptId, update.SourceRevision, update.CapturedGameTime, update.EvidenceReceiptId)
                    : NpcKnowledgeUpdate.FromAttributedClaim(update.ActorId, update.EntryId, update.PropositionId,
                        update.ValueText, update.EpistemicKind, update.Protection, update.EvidenceOrigin,
                        update.SourceId, update.UpdateReceiptId, update.SourceRevision,
                        update.CapturedGameTime, update.AttributionId, update.EvidenceReceiptId);
                return copy != null;
            }
            catch (ArgumentException) { return false; }
        }

        private static void ValidateEntry(NpcKnowledgeState state, NpcKnowledgeEntryState entry,
            HashSet<string> receiptSet)
        {
            if (entry == null || !NpcKnowledgeValidation.ValidId(entry.EntryId, 128) ||
                !string.Equals(entry.ActorId, state.ActorId, StringComparison.Ordinal) ||
                !NpcKnowledgeValidation.ValidId(entry.PropositionId, 128) || !NpcKnowledgeValidation.ValidText(entry.ValueText, 128) ||
                !Enum.IsDefined(typeof(NpcDisclosureEpistemicKind), entry.EpistemicKind) ||
                !Enum.IsDefined(typeof(NpcDisclosureProtection), entry.Protection) ||
                !Enum.IsDefined(typeof(NpcKnowledgeEvidenceOrigin), entry.EvidenceOrigin) ||
                !NpcKnowledgeValidation.ValidId(entry.SourceId, 128) || !NpcKnowledgeValidation.ValidId(entry.UpdateReceiptId, 128) ||
                (entry.EvidenceReceiptId != null && !NpcKnowledgeValidation.ValidId(entry.EvidenceReceiptId, 128)) ||
                entry.SourceRevision < 0 || !NpcKnowledgeValidation.Finite(entry.CapturedGameTime) ||
                entry.CapturedGameTime < 0 || entry.CapturedGameTime > state.LastUpdatedGameTime ||
                (entry.AttributionId != null && !NpcKnowledgeValidation.ValidId(entry.AttributionId, 240)) ||
                (entry.SupersedesUpdateReceiptId != null && !receiptSet.Contains(entry.SupersedesUpdateReceiptId)) ||
                !receiptSet.Contains(entry.UpdateReceiptId))
                throw new ArgumentException("Invalid persisted knowledge entry.");
            if ((entry.EvidenceOrigin == NpcKnowledgeEvidenceOrigin.NativeOutcomeReceipt ||
                entry.EvidenceOrigin == NpcKnowledgeEvidenceOrigin.ConversationReceipt) && entry.EvidenceReceiptId == null)
                throw new ArgumentException("Receipt-backed knowledge entry has no evidence receipt.");
            if (entry.EpistemicKind == NpcDisclosureEpistemicKind.VerifiedTruth)
            {
                if (!NpcKnowledgeUpdate.IsNativeOrigin(entry.EvidenceOrigin) || entry.AttributionId != null)
                    throw new ArgumentException("Verified truth requires native evidence origin and no attributed speaker.");
            }
            else if (entry.EpistemicKind == NpcDisclosureEpistemicKind.AttributedBelief ||
                entry.EpistemicKind == NpcDisclosureEpistemicKind.Rumor ||
                entry.EpistemicKind == NpcDisclosureEpistemicKind.PlayerClaim)
            {
                if (!NpcKnowledgeUpdate.IsAttributedOrigin(entry.EvidenceOrigin) ||
                    !NpcKnowledgeValidation.ValidId(entry.AttributionId, 240))
                    throw new ArgumentException("Attributed knowledge requires speech/report origin and attribution.");
            }
            else throw new ArgumentException("Unknown and deliberate claims are not persisted as actor knowledge facts.");
        }

        private static NpcKnowledgeEntryState Clone(NpcKnowledgeEntryState entry)
        {
            return new NpcKnowledgeEntryState { EntryId = entry.EntryId, ActorId = entry.ActorId,
                PropositionId = entry.PropositionId, ValueText = entry.ValueText,
                EpistemicKind = entry.EpistemicKind, Protection = entry.Protection,
                EvidenceOrigin = entry.EvidenceOrigin, SourceId = entry.SourceId,
                UpdateReceiptId = entry.UpdateReceiptId, EvidenceReceiptId = entry.EvidenceReceiptId,
                SourceRevision = entry.SourceRevision,
                CapturedGameTime = entry.CapturedGameTime, AttributionId = entry.AttributionId,
                SupersedesUpdateReceiptId = entry.SupersedesUpdateReceiptId };
        }
    }

    internal static class NpcKnowledgeValidation
    {
        internal static bool ValidId(string value, int maximum)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= maximum; }
        internal static bool ValidText(string value, int maximum)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= maximum; }
        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
