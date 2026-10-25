using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>
    /// Serializable sparse directed relationship state and its accepted native-outcome deduplication ledger.
    /// Identity existence and event ownership must be authorized by the caller; this DTO does not establish
    /// authority over the external world.
    /// </summary>
    [Serializable]
    public sealed class NpcRelationshipLedger
    {
        public const int VersionCurrent = 2;

        public int Version = VersionCurrent;
        public long Revision;
        public List<NpcDirectedRelationship> Relationships = new List<NpcDirectedRelationship>();
        public List<NpcAcceptedRelationshipOutcome> AcceptedOutcomes = new List<NpcAcceptedRelationshipOutcome>();

        public static NpcRelationshipLedger Create()
        {
            return CopyValidated(new NpcRelationshipLedger(), null);
        }

        /// <summary>Returns a detached validated snapshot, rechecking identities against the current world.</summary>
        public static NpcRelationshipLedger CopyValidated(NpcRelationshipLedger source,
            Func<string, bool> knownNpcIdentity)
        {
            return CopyValidated(source, knownNpcIdentity, null, NpcFearRecoveryPolicy.ProvisionalDefault);
        }

        public static NpcRelationshipLedger CopyValidated(NpcRelationshipLedger source,
            Func<string, bool> knownNpcIdentity, Func<string, NpcPersonalityProfile> personalityForObserver,
            NpcFearRecoveryPolicy fearRecoveryPolicy)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Version != 1 && source.Version != VersionCurrent)
                throw new ArgumentOutOfRangeException(nameof(source.Version));
            if (source.Revision < 0) throw new ArgumentOutOfRangeException(nameof(source.Revision));
            if (source.Relationships == null) throw new ArgumentException("Relationship rows are required.", nameof(source));
            if (source.AcceptedOutcomes == null) throw new ArgumentException("Accepted outcomes are required.", nameof(source));

            var accepted = new List<NpcAcceptedRelationshipOutcome>(source.AcceptedOutcomes.Count);
            var outcomeKeys = new HashSet<string>(StringComparer.Ordinal);
            var acceptedByPair = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var acceptedSource in source.AcceptedOutcomes)
            {
                var item = NpcAcceptedRelationshipOutcome.CopyValidated(acceptedSource);
                ValidateKnownIdentity(item.ObserverId, knownNpcIdentity);
                ValidateKnownTarget(item.TargetId, knownNpcIdentity);
                ValidateKnownTarget(item.SubjectPersonId, knownNpcIdentity);
                if (!outcomeKeys.Add(OutcomeKey(item.ObserverId, item.DeduplicationKey)))
                    throw new ArgumentException("Duplicate accepted canonical outcome.", nameof(source));
                string pair = PairKey(item.ObserverId, item.TargetId);
                int count;
                acceptedByPair.TryGetValue(pair, out count);
                acceptedByPair[pair] = checked(count + 1);
                accepted.Add(item);
            }

            var rows = new List<NpcDirectedRelationship>(source.Relationships.Count);
            var rowKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rowSource in source.Relationships)
            {
                if (rowSource == null) throw new ArgumentException("Null directed relationship row.", nameof(source));
                NpcAcceptedRelationshipOutcome safeLegacyFearAnchor =
                    FindSafeLegacyFearAnchor(rowSource, accepted);
                NpcPersonalityProfile profile = personalityForObserver == null ? null :
                    personalityForObserver(rowSource.ObserverId);
                NpcDirectedRelationship row = rowSource.Version == NpcDirectedRelationship.VersionCurrent
                    ? NpcDirectedRelationship.CopyValidated(rowSource)
                    : NpcDirectedRelationship.MigrateLegacy(rowSource, safeLegacyFearAnchor, profile, fearRecoveryPolicy);
                ValidateKnownIdentity(row.ObserverId, knownNpcIdentity);
                ValidateKnownTarget(row.TargetId, knownNpcIdentity);
                if (!rowKeys.Add(PairKey(row.ObserverId, row.TargetId)))
                    throw new ArgumentException("Duplicate directed relationship row.", nameof(source));
                rows.Add(row);
            }

            foreach (var item in accepted)
                if (!rowKeys.Contains(PairKey(item.ObserverId, item.TargetId)))
                    throw new ArgumentException("Accepted outcome has no corresponding relationship row.", nameof(source));

            if (source.Revision != accepted.Count)
                throw new ArgumentException("Ledger revision must match the retained accepted outcome history.", nameof(source));
            foreach (var row in rows)
            {
                int acceptedCount;
                acceptedByPair.TryGetValue(PairKey(row.ObserverId, row.TargetId), out acceptedCount);
                if (row.Revision != acceptedCount)
                    throw new ArgumentException("Relationship revision must match its retained accepted outcome history.", nameof(source));
            }

            return new NpcRelationshipLedger
            {
                Version = VersionCurrent,
                Revision = source.Revision,
                Relationships = rows,
                AcceptedOutcomes = accepted
            };
        }

        private static NpcAcceptedRelationshipOutcome FindSafeLegacyFearAnchor(NpcDirectedRelationship row,
            List<NpcAcceptedRelationshipOutcome> accepted)
        {
            int replayedFear = 0;
            int candidateIndex = -1;
            for (int i = 0; i < accepted.Count; i++)
            {
                var item = accepted[i];
                if (item.ObserverId != row.ObserverId || item.TargetId != row.TargetId) continue;
                if (item.FearDelta > 0)
                {
                    candidateIndex = replayedFear == 0 ? i : -1;
                    replayedFear = Math.Min(100, replayedFear + item.FearDelta);
                }
                else if (item.FearDelta < 0)
                {
                    candidateIndex = -1;
                    replayedFear = Math.Max(0, replayedFear + item.FearDelta);
                }
            }
            if (replayedFear != row.InterpersonalFear || candidateIndex < 0) return null;
            for (int i = candidateIndex + 1; i < accepted.Count; i++)
            {
                var item = accepted[i];
                if (item.ObserverId == row.ObserverId && item.TargetId == row.TargetId && item.FearDelta != 0)
                    return null;
            }
            return accepted[candidateIndex];
        }

        /// <summary>Reads an existing row without creating it. Returned scalar view is detached.</summary>
        public NpcDirectedRelationshipView Read(string observerId, string targetId)
        {
            if (Relationships == null) return null;
            for (int i = 0; i < Relationships.Count; i++)
            {
                var row = Relationships[i];
                if (row != null && string.Equals(row.ObserverId, observerId, StringComparison.Ordinal) &&
                    string.Equals(row.TargetId, targetId, StringComparison.Ordinal))
                    return row.ToView();
            }
            return null;
        }

        /// <summary>
        /// Atomically applies one observer-authorized native outcome exactly once to a detached copy.
        /// The caller must independently authorize the event and update appraisal before committing the result.
        /// </summary>
        public static bool TryApply(NpcRelationshipLedger current, NpcRelationshipLedgerUpdateProposal proposal,
            Func<string, bool> knownNpcIdentity, out NpcRelationshipLedger updated)
        {
            return TryApply(current, proposal, knownNpcIdentity, null,
                NpcFearRecoveryPolicy.ProvisionalDefault,
                NpcResentmentRecoveryPolicy.ProvisionalDefault, out updated);
        }

        public static bool TryApply(NpcRelationshipLedger current, NpcRelationshipLedgerUpdateProposal proposal,
            Func<string, bool> knownNpcIdentity, NpcPersonalityProfile personality,
            NpcFearRecoveryPolicy fearRecoveryPolicy, out NpcRelationshipLedger updated)
        {
            return TryApply(current, proposal, knownNpcIdentity, personality, fearRecoveryPolicy,
                NpcResentmentRecoveryPolicy.ProvisionalDefault, out updated);
        }

        public static bool TryApply(NpcRelationshipLedger current, NpcRelationshipLedgerUpdateProposal proposal,
            Func<string, bool> knownNpcIdentity, NpcPersonalityProfile personality,
            NpcFearRecoveryPolicy fearRecoveryPolicy, NpcResentmentRecoveryPolicy resentmentRecoveryPolicy,
            out NpcRelationshipLedger updated)
        {
            updated = null;
            if (current == null || proposal == null || proposal.RelationshipUpdate == null ||
                proposal.RelationshipUpdate.Evidence == null || string.IsNullOrWhiteSpace(proposal.ObserverId) ||
                string.IsNullOrWhiteSpace(proposal.TargetId)) return false;

            NpcRelationshipLedger snapshot;
            NpcExperienceEvent evidence;
            try
            {
                snapshot = CopyValidated(current, knownNpcIdentity);
                evidence = NpcExperienceEvent.CopyValidated(proposal.RelationshipUpdate.Evidence);
                ValidateKnownIdentity(proposal.ObserverId, knownNpcIdentity);
                ValidateKnownTarget(proposal.TargetId, knownNpcIdentity);
                ValidateKnownTarget(evidence.SubjectPersonId, knownNpcIdentity);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }

            if (proposal.ExpectedLedgerRevision != snapshot.Revision || snapshot.Revision == long.MaxValue ||
                evidence.Origin != NpcExperienceEventOrigin.NativeOutcome || !evidence.EventKind.HasValue ||
                evidence.ResponsibleActorAttribution != NpcResponsibleActorAttribution.Known ||
                !string.Equals(evidence.ObserverId, proposal.ObserverId, StringComparison.Ordinal) ||
                !string.Equals(evidence.ResponsibleActorId, proposal.TargetId, StringComparison.Ordinal) ||
                string.Equals(proposal.ObserverId, proposal.TargetId, StringComparison.Ordinal) ||
                HasOutcome(snapshot.AcceptedOutcomes, proposal.ObserverId, evidence.DeduplicationKey))
                return false;

            int rowIndex = FindRow(snapshot.Relationships, proposal.ObserverId, proposal.TargetId);
            NpcDirectedRelationship before = rowIndex < 0
                ? NpcDirectedRelationship.Create(proposal.ObserverId, proposal.TargetId)
                : snapshot.Relationships[rowIndex];

            NpcDirectedRelationship nextRow;
            if (!NpcDirectedRelationship.TryApply(before, proposal.RelationshipUpdate, personality,
                fearRecoveryPolicy, resentmentRecoveryPolicy, out nextRow)) return false;

            NpcAcceptedRelationshipOutcome accepted;
            try
            {
                accepted = NpcAcceptedRelationshipOutcome.Create(proposal.ObserverId, proposal.TargetId, evidence,
                    proposal.RelationshipUpdate);
            }
            catch (ArgumentException) { return false; }

            // Prepare every new collection before publishing a new aggregate. The input remains untouched on all paths.
            var nextRows = new List<NpcDirectedRelationship>(snapshot.Relationships);
            if (rowIndex < 0) nextRows.Add(nextRow);
            else nextRows[rowIndex] = nextRow;
            var nextAccepted = new List<NpcAcceptedRelationshipOutcome>(snapshot.AcceptedOutcomes);
            nextAccepted.Add(accepted);
            updated = new NpcRelationshipLedger
            {
                Version = VersionCurrent,
                Revision = snapshot.Revision + 1,
                Relationships = nextRows,
                AcceptedOutcomes = nextAccepted
            };
            return true;
        }

        /// <summary>
        /// Builds an immutable ledger candidate for one parser-produced appraisal. This pure layer validates
        /// evidence/appraisal binding and bounded relationship effects; the authoritative store must still
        /// verify the active branch, pending queue receipt/request, and native event ownership before commit.
        /// </summary>
        public static bool TryApplyAppraisal(NpcRelationshipLedger current, long expectedLedgerRevision,
            string expectedBranchId, string activeBranchId, NpcExperienceEvent evidence,
            NpcExperienceAppraisal appraisal, Func<string, bool> knownNpcIdentity,
            out NpcRelationshipLedger updated, out NpcDirectedRelationshipView appliedView)
        {
            return TryApplyAppraisal(current, expectedLedgerRevision, expectedBranchId, activeBranchId,
                evidence, appraisal, knownNpcIdentity, null, NpcFearRecoveryPolicy.ProvisionalDefault,
                NpcResentmentRecoveryPolicy.ProvisionalDefault, out updated, out appliedView);
        }

        public static bool TryApplyAppraisal(NpcRelationshipLedger current, long expectedLedgerRevision,
            string expectedBranchId, string activeBranchId, NpcExperienceEvent evidence,
            NpcExperienceAppraisal appraisal, Func<string, bool> knownNpcIdentity,
            NpcPersonalityProfile personality, NpcFearRecoveryPolicy fearRecoveryPolicy,
            out NpcRelationshipLedger updated, out NpcDirectedRelationshipView appliedView)
        {
            return TryApplyAppraisal(current, expectedLedgerRevision, expectedBranchId, activeBranchId,
                evidence, appraisal, knownNpcIdentity, personality, fearRecoveryPolicy,
                NpcResentmentRecoveryPolicy.ProvisionalDefault, out updated, out appliedView);
        }

        public static bool TryApplyAppraisal(NpcRelationshipLedger current, long expectedLedgerRevision,
            string expectedBranchId, string activeBranchId, NpcExperienceEvent evidence,
            NpcExperienceAppraisal appraisal, Func<string, bool> knownNpcIdentity,
            NpcPersonalityProfile personality, NpcFearRecoveryPolicy fearRecoveryPolicy,
            NpcResentmentRecoveryPolicy resentmentRecoveryPolicy,
            out NpcRelationshipLedger updated, out NpcDirectedRelationshipView appliedView)
        {
            updated = null;
            appliedView = null;
            if (current == null || evidence == null || appraisal == null ||
                string.IsNullOrWhiteSpace(expectedBranchId) || string.IsNullOrWhiteSpace(activeBranchId)) return false;

            NpcRelationshipLedger snapshot;
            NpcExperienceEvent item;
            try
            {
                snapshot = CopyValidated(current, knownNpcIdentity);
                item = NpcExperienceEvent.CopyValidated(evidence);
                ValidateKnownIdentity(item.ObserverId, knownNpcIdentity);
                ValidateKnownTarget(item.SubjectPersonId, knownNpcIdentity);
                ValidateKnownTarget(appraisal.RelationshipTargetId, knownNpcIdentity);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }

            if (expectedLedgerRevision != snapshot.Revision || snapshot.Revision == long.MaxValue ||
                !string.Equals(expectedBranchId, activeBranchId, StringComparison.Ordinal) ||
                !string.Equals(appraisal.SaveBranchId, expectedBranchId, StringComparison.Ordinal) ||
                !string.Equals(appraisal.ObserverId, item.ObserverId, StringComparison.Ordinal) ||
                !string.Equals(appraisal.SubjectPersonId, item.SubjectPersonId, StringComparison.Ordinal) ||
                !string.Equals(appraisal.EvidenceDeduplicationKey, item.DeduplicationKey, StringComparison.Ordinal) ||
                !string.Equals(appraisal.CanonicalEvidenceId,
                    item.Origin == NpcExperienceEventOrigin.NativeOutcome ? item.OutcomeId : item.UtteranceId,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(appraisal.RelationshipTargetId) ||
                string.Equals(item.ObserverId, appraisal.RelationshipTargetId, StringComparison.Ordinal) ||
                HasOutcome(snapshot.AcceptedOutcomes, item.ObserverId, item.DeduplicationKey)) return false;

            if (item.Origin == NpcExperienceEventOrigin.NativeOutcome)
            {
                if (!item.EventKind.HasValue || item.ResponsibleActorAttribution != NpcResponsibleActorAttribution.Known ||
                    !string.Equals(item.ResponsibleActorId, appraisal.RelationshipTargetId, StringComparison.Ordinal)) return false;
            }
            else if (item.Origin != NpcExperienceEventOrigin.AttributedSpeech ||
                !string.Equals(item.AttributedSpeakerId, appraisal.RelationshipTargetId, StringComparison.Ordinal)) return false;

            int rowIndex = FindRow(snapshot.Relationships, item.ObserverId, appraisal.RelationshipTargetId);
            NpcDirectedRelationship before = rowIndex < 0
                ? NpcDirectedRelationship.Create(item.ObserverId, appraisal.RelationshipTargetId)
                : snapshot.Relationships[rowIndex];
            NpcDirectedRelationship nextRow;
            if (!NpcDirectedRelationship.TryApplyAppraisal(before, item, appraisal, personality,
                fearRecoveryPolicy, resentmentRecoveryPolicy, out nextRow)) return false;

            NpcAcceptedRelationshipOutcome accepted;
            try { accepted = NpcAcceptedRelationshipOutcome.CreateForAppraisal(item, appraisal); }
            catch (ArgumentException) { return false; }

            var nextRows = new List<NpcDirectedRelationship>(snapshot.Relationships);
            if (rowIndex < 0) nextRows.Add(nextRow);
            else nextRows[rowIndex] = nextRow;
            var nextAccepted = new List<NpcAcceptedRelationshipOutcome>(snapshot.AcceptedOutcomes);
            nextAccepted.Add(accepted);
            updated = new NpcRelationshipLedger
            {
                Version = VersionCurrent,
                Revision = snapshot.Revision + 1,
                Relationships = nextRows,
                AcceptedOutcomes = nextAccepted
            };
            appliedView = nextRow.ProjectRecovery(appraisal.CapturedGameTime, personality,
                fearRecoveryPolicy, resentmentRecoveryPolicy);
            return true;
        }

        /// <summary>Stages one source-bound, receipt-bound social boundary reaction as one accepted outcome.</summary>
        public static bool TryStageConversationBoundary(NpcRelationshipLedger current,
            long expectedLedgerRevision, NpcObservedSpeechTicket ticket,
            NpcObservedSpeechReceipt receipt, object source, NpcSpeechActClassification classification,
            long boundaryOrdinal, long parentBoundaryOrdinal, NpcPersonalityProfile personality, NpcRoleState role,
            Func<string, bool> knownNpcIdentity, out NpcRelationshipLedger updated,
            out NpcDirectedRelationshipView appliedView)
        {
            updated = null;
            appliedView = null;
            if (current == null || ticket == null || receipt == null || source == null ||
                classification == null || classification.Request == null ||
                !ReferenceEquals(source, classification.Request.Source) ||
                boundaryOrdinal != ticket.Ordinal || boundaryOrdinal < 1 ||
                parentBoundaryOrdinal < 0 || parentBoundaryOrdinal >= boundaryOrdinal ||
                classification.Request.ObserverId != ticket.PersonId ||
                classification.Request.SpeakerId != NpcDirectedRelationship.PlayerTargetId ||
                classification.Request.Utterance != ticket.ExactText ||
                classification.Request.ListenerRefusesFurtherCommunication && parentBoundaryOrdinal==0 ||
                ConversationSpeechBoundaryPolicy.Decide(source, classification, personality) == ConversationSpeechBoundaryAction.Continue)
                return false;
            NpcRelationshipLedger snapshot;
            NpcObservedSpeechReceipt checkedReceipt;
            try
            {
                snapshot = CopyValidated(current, knownNpcIdentity);
                checkedReceipt = NpcObservedSpeechReceipt.CopyValidated(receipt);
                ValidateKnownIdentity(ticket.PersonId, knownNpcIdentity);
                if (role != null) role = NpcRoleState.CopyValidated(role);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            if(parentBoundaryOrdinal>0)
            {
                var standing=ticket.Owner==null?null:ticket.Owner.ReadNpcConversationBoundary(ticket.PersonId,ticket.ObservedGameTime);
                if(standing==null || standing.Ordinal!=parentBoundaryOrdinal || !standing.RefusesFurtherCommunication ||
                    standing.Action!=ConversationSpeechBoundaryAction.EndConversation ||
                    !snapshot.AcceptedOutcomes.Exists(row=>row.ObserverId==ticket.PersonId &&
                        row.SubjectPersonId==ticket.PersonId && row.TargetId==NpcDirectedRelationship.PlayerTargetId &&
                        row.AttributedSpeakerId==NpcDirectedRelationship.PlayerTargetId &&
                        row.Origin==NpcExperienceEventOrigin.AttributedSpeech &&
                        row.ConversationBoundaryOrdinal==parentBoundaryOrdinal &&
                        row.ReceiptId==standing.ReceiptId && row.ReceiptId==NpcObservedSpeechReceipt.IssuedReceiptId(
                            ticket.ScopeId,ticket.PersonId,parentBoundaryOrdinal) &&
                        row.SpeechActKind==standing.SpeechKind && row.ObservedGameTime==standing.ObservedAt &&
                        row.ObservedGameTime<=ticket.ObservedGameTime))return false;
            }
            NpcExperienceEvent evidence = checkedReceipt.Evidence;
            NpcObservedSpeechReceipt issued = ticket.IssuedReceipt;
            if (issued == null || expectedLedgerRevision != snapshot.Revision ||
                snapshot.Revision == long.MaxValue ||
                !string.Equals(evidence.ReceiptId, issued.Evidence.ReceiptId, StringComparison.Ordinal) ||
                !string.Equals(evidence.EpisodeId, issued.Evidence.EpisodeId, StringComparison.Ordinal) ||
                !string.Equals(evidence.UtteranceId, issued.Evidence.UtteranceId, StringComparison.Ordinal) ||
                !string.Equals(evidence.DeduplicationKey, issued.Evidence.DeduplicationKey, StringComparison.Ordinal) ||
                evidence.Origin != NpcExperienceEventOrigin.AttributedSpeech ||
                evidence.ClaimedEventKind.HasValue ||
                evidence.ObserverId != ticket.PersonId || evidence.SubjectPersonId != ticket.PersonId ||
                evidence.AttributedSpeakerId != NpcDirectedRelationship.PlayerTargetId ||
                evidence.ObservedGameTime != ticket.ObservedGameTime ||
                checkedReceipt.Turn.ObserverId != ticket.PersonId ||
                checkedReceipt.Turn.SpeakerId != NpcDirectedRelationship.PlayerTargetId ||
                checkedReceipt.Turn.Text != ticket.ExactText ||
                checkedReceipt.Turn.ReceiptId != issued.Turn.ReceiptId ||
                checkedReceipt.Turn.EpisodeId != issued.Turn.EpisodeId ||
                checkedReceipt.Turn.ObservedGameTime != ticket.ObservedGameTime ||
                HasOutcome(snapshot.AcceptedOutcomes, ticket.PersonId, evidence.DeduplicationKey)) return false;

            int rowIndex = FindRow(snapshot.Relationships, ticket.PersonId,
                NpcDirectedRelationship.PlayerTargetId);
            NpcDirectedRelationship before = rowIndex < 0
                ? NpcDirectedRelationship.Create(ticket.PersonId, NpcDirectedRelationship.PlayerTargetId)
                : snapshot.Relationships[rowIndex];
            NpcDirectedRelationship nextRow;
            int resentmentDelta, fearDelta;
            if (!NpcDirectedRelationship.TryApplyConversationBoundary(before, evidence, classification.Kind,
                personality, role, out nextRow, out resentmentDelta, out fearDelta)) return false;
            NpcAcceptedRelationshipOutcome accepted;
            try { accepted = NpcAcceptedRelationshipOutcome.CreateForConversationBoundary(evidence,
                classification.Kind, boundaryOrdinal, parentBoundaryOrdinal, resentmentDelta, fearDelta); }
            catch (ArgumentException) { return false; }
            var nextRows = new List<NpcDirectedRelationship>(snapshot.Relationships);
            if (rowIndex < 0) nextRows.Add(nextRow);
            else nextRows[rowIndex] = nextRow;
            var nextAccepted = new List<NpcAcceptedRelationshipOutcome>(snapshot.AcceptedOutcomes);
            nextAccepted.Add(accepted);
            updated = new NpcRelationshipLedger { Version = VersionCurrent, Revision = snapshot.Revision + 1,
                Relationships = nextRows, AcceptedOutcomes = nextAccepted };
            appliedView = nextRow.ProjectRecovery(ticket.ObservedGameTime, personality);
            return true;
        }

        private static bool HasOutcome(List<NpcAcceptedRelationshipOutcome> items, string observer, string key)
        {
            for (int i = 0; i < items.Count; i++)
                if (string.Equals(items[i].ObserverId, observer, StringComparison.Ordinal) &&
                    string.Equals(items[i].DeduplicationKey, key, StringComparison.Ordinal)) return true;
            return false;
        }

        private static int FindRow(List<NpcDirectedRelationship> rows, string observer, string target)
        {
            for (int i = 0; i < rows.Count; i++)
                if (string.Equals(rows[i].ObserverId, observer, StringComparison.Ordinal) &&
                    string.Equals(rows[i].TargetId, target, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static void ValidateKnownIdentity(string id, Func<string, bool> knownNpcIdentity)
        {
            ValidateIdentity(id);
            if (knownNpcIdentity == null || !knownNpcIdentity(id))
                throw new ArgumentException("NPC identity is not known to the current world.", nameof(id));
        }

        private static void ValidateKnownTarget(string id, Func<string, bool> knownNpcIdentity)
        {
            ValidateIdentity(id);
            if (!string.Equals(id, NpcDirectedRelationship.PlayerTargetId, StringComparison.Ordinal))
                ValidateKnownIdentity(id, knownNpcIdentity);
        }

        private static void ValidateIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 240)
                throw new ArgumentException("Invalid relationship ledger identity.", nameof(value));
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i])) throw new ArgumentException("Invalid relationship ledger identity.", nameof(value));
        }

        private static string PairKey(string observer, string target)
        { return observer.Length + ":" + observer + target.Length + ":" + target; }

        private static string OutcomeKey(string observer, string deduplicationKey)
        { return observer.Length + ":" + observer + deduplicationKey.Length + ":" + deduplicationKey; }
    }

    [Serializable]
    public sealed class NpcAcceptedRelationshipOutcome
    {
        public string ObserverId;
        public string TargetId;
        public string SubjectPersonId;
        public string ReceiptId;
        public string EpisodeId;
        public string OutcomeId;
        public NpcExperienceEventKind EventKind;
        public NpcExperienceEventOrigin Origin = NpcExperienceEventOrigin.NativeOutcome;
        public NpcExperienceEventKind ClaimedEventKind;
        public string AttributedSpeakerId;
        public NpcExperienceSpeechActKind SpeechActKind;
        public string SpeechTargetPersonId;
        // Nonzero only for the one authoritative boundary reaction to this issued speech.
        public long ConversationBoundaryOrdinal;
        public long ParentBoundaryOrdinal;
        public double ObservedGameTime;
        public double CapturedGameTime;
        public int TrustDelta;
        public int ResentmentDelta;
        public int FearDelta;
        public int AffinityDelta;
        public string DeduplicationKey;

        public static NpcAcceptedRelationshipOutcome Create(string observerId, string targetId, NpcExperienceEvent evidence,
            NpcRelationshipUpdateProposal update)
        {
            if (evidence == null || update == null) throw new ArgumentNullException(nameof(evidence));
            var validatedEvidence = NpcExperienceEvent.CopyValidated(evidence);
            if (validatedEvidence.Origin != NpcExperienceEventOrigin.NativeOutcome || !validatedEvidence.EventKind.HasValue ||
                validatedEvidence.ResponsibleActorAttribution != NpcResponsibleActorAttribution.Known ||
                !string.Equals(observerId, validatedEvidence.ObserverId, StringComparison.Ordinal) ||
                !string.Equals(targetId, validatedEvidence.ResponsibleActorId, StringComparison.Ordinal))
                throw new ArgumentException("Accepted relationship outcome requires matching known-actor native evidence.", nameof(evidence));
            return CopyValidated(new NpcAcceptedRelationshipOutcome
            {
                ObserverId = observerId,
                TargetId = targetId,
                SubjectPersonId = validatedEvidence.SubjectPersonId,
                ReceiptId = validatedEvidence.ReceiptId,
                EpisodeId = validatedEvidence.EpisodeId,
                OutcomeId = validatedEvidence.OutcomeId,
                EventKind = validatedEvidence.EventKind.Value,
                ObservedGameTime = validatedEvidence.ObservedGameTime,
                CapturedGameTime = validatedEvidence.ObservedGameTime,
                TrustDelta = update.TrustDelta,
                ResentmentDelta = update.ResentmentDelta,
                FearDelta = update.InterpersonalFearDelta,
                DeduplicationKey = validatedEvidence.DeduplicationKey
            });
        }

        /// <summary>Compatibility factory for validating serialized native evidence without applying deltas.</summary>
        public static NpcAcceptedRelationshipOutcome Create(string observerId, string targetId, NpcExperienceEvent evidence)
        {
            return Create(observerId, targetId, evidence, new NpcRelationshipUpdateProposal());
        }

        public static NpcAcceptedRelationshipOutcome CreateForAppraisal(NpcExperienceEvent evidence,
            NpcExperienceAppraisal appraisal)
        {
            if (evidence == null || appraisal == null) throw new ArgumentNullException(nameof(evidence));
            var item = NpcExperienceEvent.CopyValidated(evidence);
            return CopyValidated(new NpcAcceptedRelationshipOutcome
            {
                ObserverId = item.ObserverId,
                TargetId = appraisal.RelationshipTargetId,
                SubjectPersonId = item.SubjectPersonId,
                ReceiptId = item.ReceiptId,
                EpisodeId = item.EpisodeId,
                OutcomeId = item.Origin == NpcExperienceEventOrigin.NativeOutcome ? item.OutcomeId : item.UtteranceId,
                EventKind = item.EventKind.GetValueOrDefault(),
                Origin = item.Origin,
                ClaimedEventKind = item.ClaimedEventKind.GetValueOrDefault(),
                AttributedSpeakerId = item.AttributedSpeakerId,
                SpeechActKind = appraisal.SpeechActKind,
                SpeechTargetPersonId = item.Origin == NpcExperienceEventOrigin.AttributedSpeech
                    ? appraisal.SpeechTargetPersonId : null,
                ObservedGameTime = item.ObservedGameTime,
                CapturedGameTime = appraisal.CapturedGameTime,
                TrustDelta = appraisal.TrustDelta,
                ResentmentDelta = appraisal.ResentmentDelta,
                FearDelta = appraisal.FearDelta,
                AffinityDelta = appraisal.AffinityDelta,
                DeduplicationKey = item.DeduplicationKey
            });
        }

        internal static NpcAcceptedRelationshipOutcome CreateForConversationBoundary(
            NpcExperienceEvent evidence, NpcExperienceSpeechActKind kind,
            long ordinal, long parentOrdinal, int resentmentDelta, int fearDelta)
        {
            var item = NpcExperienceEvent.CopyValidated(evidence);
            return CopyValidated(new NpcAcceptedRelationshipOutcome
            {
                ObserverId = item.ObserverId,
                TargetId = NpcDirectedRelationship.PlayerTargetId,
                SubjectPersonId = item.SubjectPersonId,
                ReceiptId = item.ReceiptId,
                EpisodeId = item.EpisodeId,
                OutcomeId = item.UtteranceId,
                Origin = NpcExperienceEventOrigin.AttributedSpeech,
                AttributedSpeakerId = item.AttributedSpeakerId,
                SpeechActKind = kind,
                SpeechTargetPersonId = item.ObserverId,
                ConversationBoundaryOrdinal = ordinal,
                ParentBoundaryOrdinal = parentOrdinal,
                ObservedGameTime = item.ObservedGameTime,
                CapturedGameTime = item.ObservedGameTime,
                ResentmentDelta = resentmentDelta,
                FearDelta = fearDelta,
                DeduplicationKey = item.DeduplicationKey
            });
        }

        public static NpcAcceptedRelationshipOutcome CopyValidated(NpcAcceptedRelationshipOutcome source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ValidateIdentity(source.ObserverId, nameof(source.ObserverId));
            ValidateIdentity(source.TargetId, nameof(source.TargetId));
            ValidateIdentity(source.SubjectPersonId, nameof(source.SubjectPersonId));
            ValidateIdentity(source.ReceiptId, nameof(source.ReceiptId));
            ValidateIdentity(source.EpisodeId, nameof(source.EpisodeId));
            double capturedGameTime = source.CapturedGameTime == 0 && source.ObservedGameTime > 0
                ? source.ObservedGameTime : source.CapturedGameTime;
            if (double.IsNaN(source.ObservedGameTime) || double.IsInfinity(source.ObservedGameTime) || source.ObservedGameTime < 0 ||
                double.IsNaN(capturedGameTime) || double.IsInfinity(capturedGameTime) ||
                capturedGameTime < source.ObservedGameTime)
                throw new ArgumentException("Invalid accepted relationship outcome fields.", nameof(source));
            NpcExperienceEvent evidence;
            if (source.Origin == NpcExperienceEventOrigin.NativeOutcome)
            {
                ValidateIdentity(source.OutcomeId, nameof(source.OutcomeId));
                if (source.EventKind < NpcExperienceEventKind.MoneyReceived ||
                    source.EventKind > NpcExperienceEventKind.ItemReceived || source.AttributedSpeakerId != null ||
                    source.ClaimedEventKind != 0 || source.SpeechActKind != NpcExperienceSpeechActKind.NotApplicable ||
                    source.SpeechTargetPersonId != null || source.ConversationBoundaryOrdinal != 0 ||
                    source.ParentBoundaryOrdinal != 0)
                    throw new ArgumentException("Invalid native accepted-outcome fields.", nameof(source));
                evidence = NpcExperienceEvent.FromNativeOutcome(source.ObserverId, source.SubjectPersonId,
                    source.ReceiptId, source.EpisodeId, source.OutcomeId, source.EventKind, source.ObservedGameTime,
                    NpcResponsibleActorAttribution.Known, source.TargetId);
                if (Math.Abs(source.TrustDelta) > NpcDirectedRelationship.MaximumUpdateDelta ||
                    Math.Abs(source.ResentmentDelta) > NpcDirectedRelationship.MaximumUpdateDelta ||
                    Math.Abs(source.FearDelta) > NpcDirectedRelationship.MaximumUpdateDelta ||
                    source.AffinityDelta < -10 || source.AffinityDelta > 5 ||
                    source.TrustDelta > 0 && !SupportsPositiveTrust(source.EventKind) ||
                    source.TrustDelta < 0 && !SupportsNegativeTrust(source.EventKind))
                    throw new ArgumentException("Native accepted-outcome deltas exceed their policy bounds.", nameof(source));
            }
            else if (source.Origin == NpcExperienceEventOrigin.AttributedSpeech)
            {
                if (source.EventKind != 0) throw new ArgumentException("Attributed speech cannot serialize a native event kind.", nameof(source));
                ValidateIdentity(source.AttributedSpeakerId, nameof(source.AttributedSpeakerId));
                evidence = source.ClaimedEventKind == 0
                    ? NpcExperienceEvent.FromObservedSpeech(source.ObserverId, source.SubjectPersonId,
                        source.ReceiptId, source.EpisodeId, source.OutcomeId, source.ObservedGameTime,
                        source.AttributedSpeakerId)
                    : NpcExperienceEvent.FromAttributedSpeech(source.ObserverId, source.SubjectPersonId,
                        source.ReceiptId, source.EpisodeId, source.OutcomeId, source.ClaimedEventKind,
                        source.ObservedGameTime, source.AttributedSpeakerId);
                bool boundary = source.ConversationBoundaryOrdinal > 0;
                bool direct = source.SpeechActKind == NpcExperienceSpeechActKind.DirectInsult ||
                    source.SpeechActKind == NpcExperienceSpeechActKind.DirectThreat ||
                    source.SpeechActKind == NpcExperienceSpeechActKind.DirectHarassment ||
                    source.SpeechActKind == NpcExperienceSpeechActKind.SevereAbuse ||
                    source.SpeechActKind == NpcExperienceSpeechActKind.PersistentAbuse;
                bool calm = source.ParentBoundaryOrdinal == 0 &&
                    source.SpeechActKind != NpcExperienceSpeechActKind.NotApplicable && !direct &&
                    string.Equals(source.TargetId, source.AttributedSpeakerId, StringComparison.Ordinal) &&
                    string.IsNullOrEmpty(source.SpeechTargetPersonId) && source.TrustDelta == 0 &&
                    source.ResentmentDelta >= -2 && source.ResentmentDelta <= 0 &&
                    source.FearDelta >= -2 && source.FearDelta <= 0 &&
                    source.AffinityDelta >= 0 && source.AffinityDelta <= 5;
                int cap = source.SpeechActKind == NpcExperienceSpeechActKind.DirectThreat ? 25 : 10;
                int trustCap = cap;
                int affinityCap = source.SpeechActKind == NpcExperienceSpeechActKind.DirectThreat ?
                    NpcExperienceAppraisalContract.DirectThreatAffinityLossBound :
                    source.SpeechActKind == NpcExperienceSpeechActKind.DirectInsult ?
                    NpcExperienceAppraisalContract.DirectInsultAffinityLossBound : 10;
                if (boundary)
                {
                    bool initial = source.ParentBoundaryOrdinal == 0;
                    if (source.ConversationBoundaryOrdinal < 1 ||
                        source.ParentBoundaryOrdinal < 0 ||
                        source.ParentBoundaryOrdinal >= source.ConversationBoundaryOrdinal ||
                        initial && !direct || !initial && source.SpeechActKind == NpcExperienceSpeechActKind.RepairAttempt ||
                        source.ClaimedEventKind != 0 ||
                        !string.Equals(source.TargetId, NpcDirectedRelationship.PlayerTargetId, StringComparison.Ordinal) ||
                        !string.Equals(source.AttributedSpeakerId, NpcDirectedRelationship.PlayerTargetId, StringComparison.Ordinal) ||
                        !string.Equals(source.SubjectPersonId, source.ObserverId, StringComparison.Ordinal) ||
                        !string.Equals(source.SpeechTargetPersonId, source.ObserverId, StringComparison.Ordinal) ||
                        source.TrustDelta != 0 || source.AffinityDelta != 0 ||
                        source.ResentmentDelta < 0 || source.ResentmentDelta > 65 ||
                        source.FearDelta < 0 || source.FearDelta > 65 ||
                        source.SpeechActKind != NpcExperienceSpeechActKind.DirectThreat && source.FearDelta != 0)
                        throw new ArgumentException("Invalid conversation-boundary relationship outcome.", nameof(source));
                }
                else if (!calm && (source.ParentBoundaryOrdinal != 0 || !direct || !string.Equals(source.TargetId, source.AttributedSpeakerId, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(source.SpeechTargetPersonId) || source.TrustDelta > 0 ||
                    source.TrustDelta < -trustCap || source.ResentmentDelta < -10 || source.ResentmentDelta > cap ||
                    source.FearDelta < -10 || source.FearDelta > cap || source.AffinityDelta > 0 ||
                    source.AffinityDelta < -affinityCap))
                    throw new ArgumentException("Attributed speech exceeds direct-consequence policy bounds.", nameof(source));
            }
            else throw new ArgumentException("Invalid accepted relationship evidence origin.", nameof(source));
            if (!string.Equals(source.DeduplicationKey, evidence.DeduplicationKey, StringComparison.Ordinal))
                throw new ArgumentException("Accepted relationship outcome must contain matching verified native evidence.", nameof(source));
            return new NpcAcceptedRelationshipOutcome
            {
                ObserverId = source.ObserverId,
                TargetId = source.TargetId,
                SubjectPersonId = source.SubjectPersonId,
                ReceiptId = source.ReceiptId,
                EpisodeId = source.EpisodeId,
                OutcomeId = source.OutcomeId,
                EventKind = source.EventKind,
                Origin = source.Origin,
                ClaimedEventKind = source.ClaimedEventKind,
                AttributedSpeakerId = source.AttributedSpeakerId,
                SpeechActKind = source.SpeechActKind,
                SpeechTargetPersonId = source.SpeechTargetPersonId,
                ConversationBoundaryOrdinal = source.ConversationBoundaryOrdinal,
                ParentBoundaryOrdinal = source.ParentBoundaryOrdinal,
                ObservedGameTime = source.ObservedGameTime,
                CapturedGameTime = capturedGameTime,
                TrustDelta = source.TrustDelta,
                ResentmentDelta = source.ResentmentDelta,
                FearDelta = source.FearDelta,
                AffinityDelta = source.AffinityDelta,
                DeduplicationKey = evidence.DeduplicationKey
            };
        }

        private static void ValidateIdentity(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 240)
                throw new ArgumentException("Invalid accepted outcome identity.", parameterName);
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i]))
                    throw new ArgumentException("Invalid accepted outcome identity.", parameterName);
        }

        private static bool SupportsPositiveTrust(NpcExperienceEventKind kind)
        {
            return kind == NpcExperienceEventKind.AppointmentAttended || kind == NpcExperienceEventKind.CombatHelp ||
                kind == NpcExperienceEventKind.Healing || kind == NpcExperienceEventKind.ObligationFulfilled;
        }

        private static bool SupportsNegativeTrust(NpcExperienceEventKind kind)
        {
            return kind == NpcExperienceEventKind.Injury || kind == NpcExperienceEventKind.AppointmentNoShow ||
                kind == NpcExperienceEventKind.ObligationBreached;
        }
    }

    public sealed class NpcRelationshipLedgerUpdateProposal
    {
        public long ExpectedLedgerRevision;
        public string ObserverId;
        public string TargetId;
        public NpcRelationshipUpdateProposal RelationshipUpdate;
    }
}
