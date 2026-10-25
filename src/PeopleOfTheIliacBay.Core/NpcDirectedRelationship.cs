using System;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class NpcDirectedRelationship
    {
        public const string PlayerTargetId = "player";
        public const int VersionCurrent = 3;
        public const int MinimumDimension = 0;
        public const int MaximumDimension = 100;
        public const int MaximumUpdateDelta = 10;
        private const int UnknownTrustAnchor = 50;
        private const int MaximumTrustEvidenceCount = 1000000;

        public int Version = VersionCurrent;
        public string ObserverId;
        public string TargetId;
        public long Revision;
        public double LastGameTime = -1;
        public bool TrustKnown;
        // ReliabilityTrust is meaningful only when TrustKnown is true.
        public int ReliabilityTrust;
        // Evidence-count score, not a probability or model confidence estimate.
        public int TrustConfidence;
        public int TrustEvidenceCount;
        public int Resentment;
        public int InterpersonalFear;
        public bool HasFearRecoveryAnchor;
        public double FearRecoveryAnchorGameTime = -1;
        public double FearRecoveryHalfLifeSeconds;
        public double FearRecoveryBaselineRemainder;
        public bool HasResentmentRecoveryAnchor;
        public double ResentmentRecoveryAnchorGameTime = -1;
        public double ResentmentRecoveryHalfLifeSeconds;
        public double ResentmentRecoveryBaselineRemainder;

        public static NpcDirectedRelationship Create(string observerId, string targetId)
        {
            return CopyValidated(new NpcDirectedRelationship { ObserverId = observerId, TargetId = targetId });
        }

        public static NpcDirectedRelationship CopyValidated(NpcDirectedRelationship source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ValidateIdentity(source.ObserverId, nameof(source.ObserverId));
            ValidateIdentity(source.TargetId, nameof(source.TargetId));
            if (string.Equals(source.ObserverId, source.TargetId, StringComparison.Ordinal))
                throw new ArgumentException("Directed relationship cannot target its observer.", nameof(source.TargetId));
            if (source.Version != VersionCurrent) throw new ArgumentOutOfRangeException(nameof(source.Version));
            if (source.Revision < 0 || double.IsNaN(source.LastGameTime) || double.IsInfinity(source.LastGameTime) ||
                source.LastGameTime < -1 || source.Revision == 0 && source.LastGameTime != -1 ||
                source.Revision > 0 && source.LastGameTime < 0)
                throw new ArgumentException("Invalid relationship revision or game time.", nameof(source));
            RequireDimension(source.Resentment, nameof(source.Resentment));
            RequireDimension(source.InterpersonalFear, nameof(source.InterpersonalFear));
            if (source.HasFearRecoveryAnchor)
            {
                if (!Finite(source.FearRecoveryAnchorGameTime) || source.FearRecoveryAnchorGameTime < 0 ||
                    source.FearRecoveryAnchorGameTime > source.LastGameTime ||
                    !Finite(source.FearRecoveryHalfLifeSeconds) ||
                    source.FearRecoveryHalfLifeSeconds < NpcFearRecoveryPolicy.MinimumHalfLifeSeconds ||
                    source.FearRecoveryHalfLifeSeconds > NpcFearRecoveryPolicy.MaximumHalfLifeSeconds ||
                    !Finite(source.FearRecoveryBaselineRemainder) ||
                    source.FearRecoveryBaselineRemainder < -0.5 || source.FearRecoveryBaselineRemainder > 0.5 ||
                    source.InterpersonalFear + source.FearRecoveryBaselineRemainder < 0 ||
                    source.InterpersonalFear + source.FearRecoveryBaselineRemainder > MaximumDimension)
                    throw new ArgumentException("Invalid fear recovery anchor or rate.", nameof(source));
            }
            else if (source.FearRecoveryAnchorGameTime != -1 || source.FearRecoveryHalfLifeSeconds != 0 ||
                source.FearRecoveryBaselineRemainder != 0)
                throw new ArgumentException("Unknown fear recovery time cannot carry an anchor or rate.", nameof(source));
            if (source.HasResentmentRecoveryAnchor)
            {
                if (!Finite(source.ResentmentRecoveryAnchorGameTime) || source.ResentmentRecoveryAnchorGameTime < 0 ||
                    source.ResentmentRecoveryAnchorGameTime > source.LastGameTime ||
                    !Finite(source.ResentmentRecoveryHalfLifeSeconds) ||
                    source.ResentmentRecoveryHalfLifeSeconds < NpcResentmentRecoveryPolicy.MinimumHalfLifeSeconds ||
                    source.ResentmentRecoveryHalfLifeSeconds > NpcResentmentRecoveryPolicy.MaximumHalfLifeSeconds ||
                    !Finite(source.ResentmentRecoveryBaselineRemainder) ||
                    source.ResentmentRecoveryBaselineRemainder < -0.5 || source.ResentmentRecoveryBaselineRemainder > 0.5 ||
                    source.Resentment + source.ResentmentRecoveryBaselineRemainder < 0 ||
                    source.Resentment + source.ResentmentRecoveryBaselineRemainder > MaximumDimension)
                    throw new ArgumentException("Invalid resentment recovery anchor or rate.", nameof(source));
            }
            else if (source.ResentmentRecoveryAnchorGameTime != -1 || source.ResentmentRecoveryHalfLifeSeconds != 0 ||
                source.ResentmentRecoveryBaselineRemainder != 0)
                throw new ArgumentException("Unknown resentment recovery time cannot carry an anchor or rate.", nameof(source));
            if (source.TrustKnown)
            {
                RequireDimension(source.ReliabilityTrust, nameof(source.ReliabilityTrust));
                RequireDimension(source.TrustConfidence, nameof(source.TrustConfidence));
                if (source.TrustEvidenceCount < 1 || source.TrustEvidenceCount > MaximumTrustEvidenceCount ||
                    source.TrustConfidence != ConfidenceFor(source.TrustEvidenceCount))
                    throw new ArgumentException("Invalid known-trust evidence state.", nameof(source));
            }
            else if (source.ReliabilityTrust != 0 || source.TrustConfidence != 0 || source.TrustEvidenceCount != 0)
                throw new ArgumentException("Unknown trust cannot carry a value, confidence, or evidence count.", nameof(source));

            return new NpcDirectedRelationship
            {
                Version = source.Version,
                ObserverId = source.ObserverId,
                TargetId = source.TargetId,
                Revision = source.Revision,
                LastGameTime = source.LastGameTime,
                TrustKnown = source.TrustKnown,
                ReliabilityTrust = source.ReliabilityTrust,
                TrustConfidence = source.TrustConfidence,
                TrustEvidenceCount = source.TrustEvidenceCount,
                Resentment = source.Resentment,
                InterpersonalFear = source.InterpersonalFear,
                HasFearRecoveryAnchor = source.HasFearRecoveryAnchor,
                FearRecoveryAnchorGameTime = source.FearRecoveryAnchorGameTime,
                FearRecoveryHalfLifeSeconds = source.FearRecoveryHalfLifeSeconds,
                FearRecoveryBaselineRemainder = source.FearRecoveryBaselineRemainder,
                HasResentmentRecoveryAnchor = source.HasResentmentRecoveryAnchor,
                ResentmentRecoveryAnchorGameTime = source.ResentmentRecoveryAnchorGameTime,
                ResentmentRecoveryHalfLifeSeconds = source.ResentmentRecoveryHalfLifeSeconds,
                ResentmentRecoveryBaselineRemainder = source.ResentmentRecoveryBaselineRemainder
            };
        }

        public NpcDirectedRelationshipView ToView()
        {
            var copy = CopyValidated(this);
            return new NpcDirectedRelationshipView(copy);
        }

        public NpcDirectedRelationshipView ProjectFear(double now, NpcPersonalityProfile personality,
            NpcFearRecoveryPolicy policy = null)
        {
            var copy = CopyValidated(this);
            var selectedPolicy = policy ?? NpcFearRecoveryPolicy.ProvisionalDefault;
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (!ValidProjectionPersonality(copy.ObserverId, personality))
                throw new ArgumentException("Fear recovery personality must belong to the relationship observer.", nameof(personality));
            if (copy.HasFearRecoveryAnchor && now < copy.FearRecoveryAnchorGameTime)
                throw new ArgumentOutOfRangeException(nameof(now));
            int projected = copy.HasFearRecoveryAnchor
                ? selectedPolicy.Project(copy.InterpersonalFear + copy.FearRecoveryBaselineRemainder,
                    copy.FearRecoveryAnchorGameTime, now,
                    copy.FearRecoveryHalfLifeSeconds)
                : copy.InterpersonalFear;
            return new NpcDirectedRelationshipView(copy, projected, now);
        }

        public NpcDirectedRelationshipView ProjectRecovery(double now, NpcPersonalityProfile personality,
            NpcFearRecoveryPolicy fearPolicy = null, NpcResentmentRecoveryPolicy resentmentPolicy = null)
        {
            var copy = CopyValidated(this);
            NpcFearRecoveryPolicy selectedFearPolicy = fearPolicy ?? NpcFearRecoveryPolicy.ProvisionalDefault;
            NpcResentmentRecoveryPolicy selectedResentmentPolicy =
                resentmentPolicy ?? NpcResentmentRecoveryPolicy.ProvisionalDefault;
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (!ValidProjectionPersonality(copy.ObserverId, personality))
                throw new ArgumentException("Relationship recovery personality must belong to the relationship observer.", nameof(personality));
            if (copy.HasFearRecoveryAnchor && now < copy.FearRecoveryAnchorGameTime)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (copy.HasResentmentRecoveryAnchor && now < copy.ResentmentRecoveryAnchorGameTime)
                throw new ArgumentOutOfRangeException(nameof(now));
            int projectedFear = copy.HasFearRecoveryAnchor
                ? selectedFearPolicy.Project(copy.InterpersonalFear + copy.FearRecoveryBaselineRemainder,
                    copy.FearRecoveryAnchorGameTime, now, copy.FearRecoveryHalfLifeSeconds)
                : copy.InterpersonalFear;
            int projectedResentment = copy.HasResentmentRecoveryAnchor
                ? selectedResentmentPolicy.Project(copy.Resentment + copy.ResentmentRecoveryBaselineRemainder,
                    copy.ResentmentRecoveryAnchorGameTime, now, copy.ResentmentRecoveryHalfLifeSeconds)
                : copy.Resentment;
            return new NpcDirectedRelationshipView(copy, projectedFear, projectedResentment, now);
        }

        internal static NpcDirectedRelationship MigrateLegacy(NpcDirectedRelationship source,
            NpcAcceptedRelationshipOutcome latestPositiveFear, NpcPersonalityProfile personality,
            NpcFearRecoveryPolicy policy)
        {
            if (source == null || (source.Version != 1 && source.Version != 2))
                throw new ArgumentException("Legacy relationship row version is invalid.", nameof(source));
            var migrated = new NpcDirectedRelationship
            {
                Version = VersionCurrent,
                ObserverId = source.ObserverId,
                TargetId = source.TargetId,
                Revision = source.Revision,
                LastGameTime = source.LastGameTime,
                TrustKnown = source.TrustKnown,
                ReliabilityTrust = source.ReliabilityTrust,
                TrustConfidence = source.TrustConfidence,
                TrustEvidenceCount = source.TrustEvidenceCount,
                Resentment = source.Resentment,
                InterpersonalFear = source.InterpersonalFear,
                HasFearRecoveryAnchor = source.Version == 2 && source.HasFearRecoveryAnchor,
                FearRecoveryAnchorGameTime = source.Version == 2 ? source.FearRecoveryAnchorGameTime : -1,
                FearRecoveryHalfLifeSeconds = source.Version == 2 ? source.FearRecoveryHalfLifeSeconds : 0,
                FearRecoveryBaselineRemainder = source.Version == 2 ? source.FearRecoveryBaselineRemainder : 0
            };
            if (source.Version == 1 && latestPositiveFear != null)
            {
                migrated.HasFearRecoveryAnchor = true;
                migrated.FearRecoveryAnchorGameTime = latestPositiveFear.ObservedGameTime;
                migrated.FearRecoveryHalfLifeSeconds = (policy ?? NpcFearRecoveryPolicy.ProvisionalDefault)
                    .HalfLifeSeconds(personality, NpcFearSeverity.Moderate);
            }
            return CopyValidated(migrated);
        }

        /// <summary>
        /// Applies one observer-bound native outcome to a detached copy. Caller must verify event ownership,
        /// appraisal authorization, and exactly-once deduplication before treating this as a commit.
        /// </summary>
        public static bool TryApply(NpcDirectedRelationship current, NpcRelationshipUpdateProposal proposal,
            out NpcDirectedRelationship updated)
        {
            return TryApply(current, proposal, null, NpcFearRecoveryPolicy.ProvisionalDefault,
                NpcResentmentRecoveryPolicy.ProvisionalDefault, out updated);
        }

        public static bool TryApply(NpcDirectedRelationship current, NpcRelationshipUpdateProposal proposal,
            NpcPersonalityProfile personality, NpcFearRecoveryPolicy policy, out NpcDirectedRelationship updated)
        {
            return TryApply(current, proposal, personality, policy,
                NpcResentmentRecoveryPolicy.ProvisionalDefault, out updated);
        }

        public static bool TryApply(NpcDirectedRelationship current, NpcRelationshipUpdateProposal proposal,
            NpcPersonalityProfile personality, NpcFearRecoveryPolicy fearPolicy,
            NpcResentmentRecoveryPolicy resentmentPolicy, out NpcDirectedRelationship updated)
        {
            updated = null;
            if (current == null || proposal == null || proposal.Evidence == null) return false;
            NpcDirectedRelationship copy;
            NpcExperienceEvent evidence;
            try
            {
                copy = CopyValidated(current);
                evidence = NpcExperienceEvent.CopyValidated(proposal.Evidence);
            }
            catch (ArgumentException) { return false; }

            if (proposal.ExpectedRevision != copy.Revision || copy.Revision == long.MaxValue ||
                evidence.Origin != NpcExperienceEventOrigin.NativeOutcome || !evidence.EventKind.HasValue ||
                evidence.ResponsibleActorAttribution != NpcResponsibleActorAttribution.Known ||
                !string.Equals(evidence.ObserverId, copy.ObserverId, StringComparison.Ordinal) ||
                !string.Equals(evidence.ResponsibleActorId, copy.TargetId, StringComparison.Ordinal) ||
                evidence.ObservedGameTime < copy.LastGameTime ||
                !ValidDelta(proposal.TrustDelta) || !ValidDelta(proposal.ResentmentDelta) ||
                !ValidDelta(proposal.InterpersonalFearDelta) ||
                proposal.TrustDelta == 0 && proposal.ResentmentDelta == 0 && proposal.InterpersonalFearDelta == 0)
                return false;

            if (proposal.TrustDelta > 0 && !SupportsPositiveTrust(evidence.EventKind.Value)) return false;
            if (proposal.TrustDelta < 0 && !SupportsNegativeTrust(evidence.EventKind.Value)) return false;

            if (proposal.TrustDelta != 0)
            {
                int basis = copy.TrustKnown ? copy.ReliabilityTrust : UnknownTrustAnchor;
                copy.ReliabilityTrust = Clamp(basis + proposal.TrustDelta);
                copy.TrustKnown = true;
                if (copy.TrustEvidenceCount >= MaximumTrustEvidenceCount) return false;
                copy.TrustEvidenceCount++;
                copy.TrustConfidence = ConfidenceFor(copy.TrustEvidenceCount);
            }
            if (proposal.ResentmentDelta != 0 && !ApplyResentmentDelta(copy, evidence,
                proposal.ResentmentDelta, evidence.ObservedGameTime, personality,
                proposal.FearSeverity, resentmentPolicy)) return false;
            if (proposal.InterpersonalFearDelta != 0 && !ApplyFearDelta(copy, evidence,
                proposal.InterpersonalFearDelta, evidence.ObservedGameTime, personality,
                proposal.FearSeverity, fearPolicy)) return false;
            copy.Revision++;
            copy.LastGameTime = evidence.ObservedGameTime;
            updated = copy;
            return true;
        }

        /// <summary>
        /// Applies only a parser-produced, evidence-bound relationship appraisal. Severe speech caps are
        /// independently enforced here; the caller still owns branch, receipt, event-authority and CAS checks.
        /// Relationship time advances to capture time so a delayed event never rewinds current state.
        /// </summary>
        public static bool TryApplyAppraisal(NpcDirectedRelationship current, NpcExperienceEvent evidence,
            NpcExperienceAppraisal appraisal, out NpcDirectedRelationship updated)
        {
            return TryApplyAppraisal(current, evidence, appraisal, null,
                NpcFearRecoveryPolicy.ProvisionalDefault, NpcResentmentRecoveryPolicy.ProvisionalDefault,
                out updated);
        }

        public static bool TryApplyAppraisal(NpcDirectedRelationship current, NpcExperienceEvent evidence,
            NpcExperienceAppraisal appraisal, NpcPersonalityProfile personality, NpcFearRecoveryPolicy policy,
            out NpcDirectedRelationship updated)
        {
            return TryApplyAppraisal(current, evidence, appraisal, personality, policy,
                NpcResentmentRecoveryPolicy.ProvisionalDefault, out updated);
        }

        public static bool TryApplyAppraisal(NpcDirectedRelationship current, NpcExperienceEvent evidence,
            NpcExperienceAppraisal appraisal, NpcPersonalityProfile personality,
            NpcFearRecoveryPolicy fearPolicy, NpcResentmentRecoveryPolicy resentmentPolicy,
            out NpcDirectedRelationship updated)
        {
            updated = null;
            if (current == null || evidence == null || appraisal == null) return false;
            NpcDirectedRelationship copy;
            NpcExperienceEvent item;
            try
            {
                copy = CopyValidated(current);
                item = NpcExperienceEvent.CopyValidated(evidence);
            }
            catch (ArgumentException) { return false; }

            if (copy.Revision == long.MaxValue || appraisal.ExpectedRelationshipRevision != copy.Revision ||
                appraisal.ObservedGameTime != item.ObservedGameTime ||
                !Finite(appraisal.CapturedGameTime) || appraisal.CapturedGameTime < item.ObservedGameTime ||
                appraisal.CapturedGameTime < copy.LastGameTime ||
                !string.Equals(appraisal.ObserverId, copy.ObserverId, StringComparison.Ordinal) ||
                !string.Equals(appraisal.RelationshipTargetId, copy.TargetId, StringComparison.Ordinal) ||
                !string.Equals(appraisal.ObserverId, item.ObserverId, StringComparison.Ordinal) ||
                !string.Equals(appraisal.SubjectPersonId, item.SubjectPersonId, StringComparison.Ordinal) ||
                !string.Equals(appraisal.EvidenceDeduplicationKey, item.DeduplicationKey, StringComparison.Ordinal) ||
                !string.Equals(appraisal.CanonicalEvidenceId,
                    item.Origin == NpcExperienceEventOrigin.NativeOutcome ? item.OutcomeId : item.UtteranceId,
                    StringComparison.Ordinal) ||
                !ValidAppraisalRelationshipDeltas(item, appraisal) ||
                appraisal.TrustDelta == 0 && appraisal.ResentmentDelta == 0 && appraisal.FearDelta == 0)
                return false;

            if (appraisal.TrustDelta != 0)
            {
                int basis = copy.TrustKnown ? copy.ReliabilityTrust : UnknownTrustAnchor;
                copy.ReliabilityTrust = Clamp(basis + appraisal.TrustDelta);
                copy.TrustKnown = true;
                if (copy.TrustEvidenceCount >= MaximumTrustEvidenceCount) return false;
                copy.TrustEvidenceCount++;
                copy.TrustConfidence = ConfidenceFor(copy.TrustEvidenceCount);
            }
            if (appraisal.ResentmentDelta != 0 && !ApplyResentmentDelta(copy, item,
                appraisal.ResentmentDelta, appraisal.CapturedGameTime, personality,
                NpcFearSeverity.Moderate, resentmentPolicy)) return false;
            if (appraisal.FearDelta != 0 && !ApplyFearDelta(copy, item, appraisal.FearDelta,
                appraisal.CapturedGameTime, personality, NpcFearSeverity.Moderate, fearPolicy)) return false;
            copy.Revision++;
            copy.LastGameTime = appraisal.CapturedGameTime;
            updated = copy;
            return true;
        }

        public static bool TryApplyFearEvent(NpcDirectedRelationship current, NpcExperienceEvent evidence,
            int fearDelta, double commitGameTime, NpcPersonalityProfile personality, NpcFearSeverity severity,
            NpcFearRecoveryPolicy policy, out NpcDirectedRelationship updated)
        {
            updated = null;
            if (current == null || evidence == null || fearDelta < -25 || fearDelta > 25 || fearDelta == 0 ||
                !Finite(commitGameTime) || commitGameTime < 0) return false;
            NpcDirectedRelationship copy;
            NpcExperienceEvent item;
            try
            {
                copy = CopyValidated(current);
                item = NpcExperienceEvent.CopyValidated(evidence);
            }
            catch (ArgumentException) { return false; }
            if (copy.Revision == long.MaxValue || item.Origin != NpcExperienceEventOrigin.NativeOutcome ||
                item.ResponsibleActorAttribution != NpcResponsibleActorAttribution.Known ||
                !item.EventKind.HasValue || item.ObservedGameTime > commitGameTime ||
                commitGameTime < copy.LastGameTime ||
                !string.Equals(item.ObserverId, copy.ObserverId, StringComparison.Ordinal) ||
                !string.Equals(item.ResponsibleActorId, copy.TargetId, StringComparison.Ordinal) ||
                !ApplyFearDelta(copy, item, fearDelta, commitGameTime, personality, severity, policy)) return false;
            copy.Revision++;
            copy.LastGameTime = commitGameTime;
            updated = copy;
            return true;
        }

        // One verified conversation boundary is one relationship event, even when reaching its
        // resentment floor requires more than an ordinary appraisal's bounded delta.
        internal static bool TryApplyConversationBoundary(NpcDirectedRelationship current,
            NpcExperienceEvent evidence, NpcExperienceSpeechActKind kind,
            NpcPersonalityProfile personality, NpcRoleState role,
            out NpcDirectedRelationship updated, out int resentmentDelta, out int fearDelta)
        {
            updated = null;
            resentmentDelta = 0;
            fearDelta = 0;
            if (current == null || evidence == null) return false;
            NpcDirectedRelationship copy;
            NpcExperienceEvent item;
            try { copy = CopyValidated(current); item = NpcExperienceEvent.CopyValidated(evidence); }
            catch (ArgumentException) { return false; }
            if (copy.Revision == long.MaxValue || item.Origin != NpcExperienceEventOrigin.AttributedSpeech ||
                item.ClaimedEventKind.HasValue || item.ObservedGameTime < copy.LastGameTime ||
                !string.Equals(item.ObserverId, copy.ObserverId, StringComparison.Ordinal) ||
                !string.Equals(item.AttributedSpeakerId, copy.TargetId, StringComparison.Ordinal) ||
                !ValidProjectionPersonality(copy.ObserverId, personality) ||
                role != null && role.PersonId != copy.ObserverId) return false;
            var recovery = NpcResentmentRecoveryPolicy.ProvisionalDefault;
            var priorView = copy.ProjectRecovery(item.ObservedGameTime, personality);
            int prior = priorView.ProjectedResentment;
            int priorFear = priorView.ProjectedInterpersonalFear;
            resentmentDelta = Math.Max(0, 65 - prior);
            if (resentmentDelta > 0 && !ApplyResentmentDelta(copy, item, resentmentDelta,
                item.ObservedGameTime, personality, NpcFearSeverity.Severe, recovery)) return false;
            if (kind == NpcExperienceSpeechActKind.DirectThreat &&
                !NpcRolePolicy.SuppressPlayerFear(role) && !NpcRolePolicy.IsCombatExperienced(role))
            {
                fearDelta = Math.Max(0, 65 - priorFear);
                if (fearDelta > 0 && !ApplyFearDelta(copy, item, fearDelta, item.ObservedGameTime,
                    personality, NpcFearSeverity.Severe, NpcFearRecoveryPolicy.ProvisionalDefault)) return false;
            }
            copy.Revision++;
            copy.LastGameTime = item.ObservedGameTime;
            updated = copy;
            return true;
        }

        private static bool ApplyFearDelta(NpcDirectedRelationship copy, NpcExperienceEvent evidence,
            int fearDelta, double commitGameTime, NpcPersonalityProfile personality, NpcFearSeverity severity,
            NpcFearRecoveryPolicy policy)
        {
            if (!Finite(commitGameTime) || commitGameTime < evidence.ObservedGameTime ||
                commitGameTime < copy.LastGameTime || !Enum.IsDefined(typeof(NpcFearSeverity), severity) ||
                !ValidProjectionPersonality(copy.ObserverId, personality)) return false;
            NpcFearRecoveryPolicy selectedPolicy = policy ?? NpcFearRecoveryPolicy.ProvisionalDefault;
            if (fearDelta > 0)
            {
                int prior = copy.HasFearRecoveryAnchor
                    ? selectedPolicy.Project(copy.InterpersonalFear + copy.FearRecoveryBaselineRemainder,
                        copy.FearRecoveryAnchorGameTime,
                        commitGameTime, copy.FearRecoveryHalfLifeSeconds)
                    : copy.InterpersonalFear;
                double eventHalfLife = selectedPolicy.HalfLifeSeconds(personality, severity);
                int contribution = selectedPolicy.Project(fearDelta, evidence.ObservedGameTime,
                    commitGameTime, eventHalfLife);
                copy.InterpersonalFear = Clamp(prior + contribution);
                copy.HasFearRecoveryAnchor = true;
                copy.FearRecoveryAnchorGameTime = commitGameTime;
                copy.FearRecoveryHalfLifeSeconds = eventHalfLife;
                copy.FearRecoveryBaselineRemainder = 0;
                return true;
            }

            if (!copy.HasFearRecoveryAnchor)
            {
                copy.InterpersonalFear = Clamp(copy.InterpersonalFear + fearDelta);
                return true;
            }
            int projected = selectedPolicy.Project(copy.InterpersonalFear + copy.FearRecoveryBaselineRemainder,
                copy.FearRecoveryAnchorGameTime, commitGameTime, copy.FearRecoveryHalfLifeSeconds);
            int reduced = Clamp(projected + fearDelta);
            double fraction = selectedPolicy.DecayFraction(copy.FearRecoveryAnchorGameTime,
                commitGameTime, copy.FearRecoveryHalfLifeSeconds);
            double rebased = fraction <= 0 ? 0 : Math.Max(0, Math.Min(MaximumDimension, reduced / fraction));
            copy.InterpersonalFear = Clamp((int)Math.Round(rebased, MidpointRounding.AwayFromZero));
            copy.FearRecoveryBaselineRemainder = rebased - copy.InterpersonalFear;
            return true;
        }

        private static bool ApplyResentmentDelta(NpcDirectedRelationship copy, NpcExperienceEvent evidence,
            int resentmentDelta, double commitGameTime, NpcPersonalityProfile personality,
            NpcFearSeverity severity, NpcResentmentRecoveryPolicy policy)
        {
            if (!Finite(commitGameTime) || commitGameTime < evidence.ObservedGameTime ||
                commitGameTime < copy.LastGameTime || !Enum.IsDefined(typeof(NpcFearSeverity), severity) ||
                !ValidProjectionPersonality(copy.ObserverId, personality)) return false;
            NpcResentmentRecoveryPolicy selectedPolicy = policy ?? NpcResentmentRecoveryPolicy.ProvisionalDefault;

            if (resentmentDelta > 0)
            {
                int prior = copy.HasResentmentRecoveryAnchor
                    ? selectedPolicy.Project(copy.Resentment + copy.ResentmentRecoveryBaselineRemainder,
                        copy.ResentmentRecoveryAnchorGameTime, commitGameTime,
                        copy.ResentmentRecoveryHalfLifeSeconds)
                    : copy.Resentment;
                double eventHalfLife = selectedPolicy.HalfLifeSeconds(personality, severity);
                int contribution = selectedPolicy.Project(resentmentDelta, evidence.ObservedGameTime,
                    commitGameTime, eventHalfLife);
                copy.Resentment = Clamp(prior + contribution);
                copy.HasResentmentRecoveryAnchor = true;
                copy.ResentmentRecoveryAnchorGameTime = commitGameTime;
                copy.ResentmentRecoveryHalfLifeSeconds = eventHalfLife;
                copy.ResentmentRecoveryBaselineRemainder = 0;
                return true;
            }

            if (!copy.HasResentmentRecoveryAnchor)
            {
                copy.Resentment = Clamp(copy.Resentment + resentmentDelta);
                if (copy.Resentment > 0)
                {
                    copy.HasResentmentRecoveryAnchor = true;
                    copy.ResentmentRecoveryAnchorGameTime = commitGameTime;
                    copy.ResentmentRecoveryHalfLifeSeconds = selectedPolicy.HalfLifeSeconds(personality, severity);
                    copy.ResentmentRecoveryBaselineRemainder = 0;
                }
                return true;
            }

            int projected = selectedPolicy.Project(copy.Resentment + copy.ResentmentRecoveryBaselineRemainder,
                copy.ResentmentRecoveryAnchorGameTime, commitGameTime,
                copy.ResentmentRecoveryHalfLifeSeconds);
            int reduced = Clamp(projected + resentmentDelta);
            double fraction = selectedPolicy.DecayFraction(copy.ResentmentRecoveryAnchorGameTime,
                commitGameTime, copy.ResentmentRecoveryHalfLifeSeconds);
            double rebased = fraction <= 0 ? 0 : Math.Max(0, Math.Min(MaximumDimension, reduced / fraction));
            copy.Resentment = Clamp((int)Math.Round(rebased, MidpointRounding.AwayFromZero));
            copy.ResentmentRecoveryBaselineRemainder = rebased - copy.Resentment;
            return true;
        }

        internal static bool ValidProjectionPersonality(string observerId, NpcPersonalityProfile personality)
        {
            if (personality == null) return true;
            try
            {
                var copy = NpcPersonalityProfile.CopyValidated(personality);
                return string.Equals(copy.PersonId, observerId, StringComparison.Ordinal);
            }
            catch (ArgumentException) { return false; }
        }

        private static bool ValidAppraisalRelationshipDeltas(NpcExperienceEvent item, NpcExperienceAppraisal appraisal)
        {
            int trust = appraisal.TrustDelta, resentment = appraisal.ResentmentDelta, fear = appraisal.FearDelta;
            if (item.Origin == NpcExperienceEventOrigin.NativeOutcome)
            {
                if (!item.EventKind.HasValue || item.ResponsibleActorAttribution != NpcResponsibleActorAttribution.Known ||
                    !string.Equals(item.ResponsibleActorId, appraisal.RelationshipTargetId, StringComparison.Ordinal) ||
                    Math.Abs(trust) > MaximumUpdateDelta || Math.Abs(resentment) > MaximumUpdateDelta ||
                    Math.Abs(fear) > MaximumUpdateDelta || appraisal.AffinityDelta < -10 || appraisal.AffinityDelta > 5 ||
                    trust > 0 && !SupportsPositiveTrust(item.EventKind.Value) ||
                    trust < 0 && !SupportsNegativeTrust(item.EventKind.Value) ||
                    appraisal.SpeechActKind != NpcExperienceSpeechActKind.NotApplicable ||
                    !string.IsNullOrEmpty(appraisal.SpeechTargetPersonId)) return false;
                return true;
            }

            if (item.Origin != NpcExperienceEventOrigin.AttributedSpeech ||
                !string.Equals(item.AttributedSpeakerId, appraisal.RelationshipTargetId, StringComparison.Ordinal))
                return false;
            if (appraisal.SpeechActKind != NpcExperienceSpeechActKind.NotApplicable &&
                appraisal.SpeechActKind != NpcExperienceSpeechActKind.DirectInsult &&
                appraisal.SpeechActKind != NpcExperienceSpeechActKind.DirectThreat &&
                appraisal.SpeechActKind != NpcExperienceSpeechActKind.DirectHarassment &&
                appraisal.SpeechActKind != NpcExperienceSpeechActKind.SevereAbuse &&
                appraisal.SpeechActKind != NpcExperienceSpeechActKind.PersistentAbuse)
                return trust == 0 && resentment >= -2 && resentment <= 0 && fear >= -2 && fear <= 0 &&
                    appraisal.AffinityDelta >= 0 && appraisal.AffinityDelta <= 5 &&
                    string.IsNullOrEmpty(appraisal.SpeechTargetPersonId);
            if (string.IsNullOrWhiteSpace(appraisal.SpeechTargetPersonId) || trust > 0 ||
                 appraisal.SpeechActKind != NpcExperienceSpeechActKind.DirectInsult &&
                 appraisal.SpeechActKind != NpcExperienceSpeechActKind.DirectThreat &&
                 appraisal.SpeechActKind != NpcExperienceSpeechActKind.DirectHarassment &&
                 appraisal.SpeechActKind != NpcExperienceSpeechActKind.SevereAbuse &&
                 appraisal.SpeechActKind != NpcExperienceSpeechActKind.PersistentAbuse) return false;

            int cap = appraisal.SpeechActKind == NpcExperienceSpeechActKind.DirectThreat ? 25 : 10;
            int trustCap = appraisal.SpeechActKind == NpcExperienceSpeechActKind.DirectThreat ? 25 : 10;
            if (trust < -trustCap || resentment < -10 || resentment > cap || fear < -10 || fear > cap) return false;
            int affinityFloor = appraisal.SpeechActKind == NpcExperienceSpeechActKind.DirectThreat ?
                -NpcExperienceAppraisalContract.DirectThreatAffinityLossBound :
                appraisal.SpeechActKind == NpcExperienceSpeechActKind.DirectInsult ?
                -NpcExperienceAppraisalContract.DirectInsultAffinityLossBound : -10;
            if (appraisal.AffinityDelta < affinityFloor || appraisal.AffinityDelta > 0) return false;
            return true;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

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

        private static bool ValidDelta(int value)
        { return value >= -MaximumUpdateDelta && value <= MaximumUpdateDelta; }

        private static int ConfidenceFor(int evidenceCount)
        { return Math.Min(MaximumDimension, evidenceCount * MaximumUpdateDelta); }

        private static int Clamp(int value)
        { return Math.Max(MinimumDimension, Math.Min(MaximumDimension, value)); }

        private static void RequireDimension(int value, string parameterName)
        {
            if (value < MinimumDimension || value > MaximumDimension)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateIdentity(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 240)
                throw new ArgumentException("Invalid directed relationship identity.", parameterName);
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i]))
                    throw new ArgumentException("Invalid directed relationship identity.", parameterName);
        }
    }

    public sealed class NpcRelationshipUpdateProposal
    {
        public long ExpectedRevision;
        public NpcExperienceEvent Evidence;
        public int TrustDelta;
        public int ResentmentDelta;
        public int InterpersonalFearDelta;
        public NpcFearSeverity FearSeverity = NpcFearSeverity.Moderate;
    }

    /// <summary>Read-only scalar snapshot; no references to mutable relationship state.</summary>
    public sealed class NpcDirectedRelationshipView
    {
        internal NpcDirectedRelationshipView(NpcDirectedRelationship source)
        {
            ObserverId = source.ObserverId;
            TargetId = source.TargetId;
            Revision = source.Revision;
            LastGameTime = source.LastGameTime;
            TrustKnown = source.TrustKnown;
            ReliabilityTrust = source.ReliabilityTrust;
            TrustConfidence = source.TrustConfidence;
            TrustEvidenceCount = source.TrustEvidenceCount;
            Resentment = source.Resentment;
            StoredResentment = source.Resentment;
            ProjectedResentment = source.Resentment;
            InterpersonalFear = source.InterpersonalFear;
            StoredInterpersonalFear = source.InterpersonalFear;
            ProjectedInterpersonalFear = source.InterpersonalFear;
            HasFearRecoveryAnchor = source.HasFearRecoveryAnchor;
            FearRecoveryAnchorGameTime = source.FearRecoveryAnchorGameTime;
            FearRecoveryHalfLifeSeconds = source.FearRecoveryHalfLifeSeconds;
            FearRecoveryBaselineRemainder = source.FearRecoveryBaselineRemainder;
            HasResentmentRecoveryAnchor = source.HasResentmentRecoveryAnchor;
            ResentmentRecoveryAnchorGameTime = source.ResentmentRecoveryAnchorGameTime;
            ResentmentRecoveryHalfLifeSeconds = source.ResentmentRecoveryHalfLifeSeconds;
            ResentmentRecoveryBaselineRemainder = source.ResentmentRecoveryBaselineRemainder;
            PlayerFearSuppressed = false;
            RoleRevision = -1;
        }

        internal NpcDirectedRelationshipView(NpcDirectedRelationship source, int projectedFear, double projectedAt)
            : this(source, projectedFear, source.Resentment, projectedAt)
        {
        }

        internal NpcDirectedRelationshipView(NpcDirectedRelationship source, int projectedFear,
            int projectedResentment, double projectedAt)
            : this(source)
        {
            ProjectedInterpersonalFear = projectedFear;
            ProjectedResentment = projectedResentment;
            ProjectedAtGameTime = projectedAt;
        }

        public string ObserverId { get; private set; }
        public string TargetId { get; private set; }
        public long Revision { get; private set; }
        public double LastGameTime { get; private set; }
        public bool TrustKnown { get; private set; }
        public int ReliabilityTrust { get; private set; }
        public int TrustConfidence { get; private set; }
        public int TrustEvidenceCount { get; private set; }
        /// <summary>Unprojected persisted resentment for raw-state compatibility and diagnostics.</summary>
        public int Resentment { get; private set; }
        public int StoredResentment { get; private set; }
        public int ProjectedResentment { get; private set; }
        public int InterpersonalFear { get; private set; }
        public int StoredInterpersonalFear { get; private set; }
        public int ProjectedInterpersonalFear { get; private set; }
        /// <summary>True when verified rank suppresses only player-directed fear projections.</summary>
        public bool PlayerFearSuppressed { get; private set; }
        /// <summary>Role snapshot revision used to produce this view, or -1 when role evidence is absent.</summary>
        public long RoleRevision { get; private set; }
        public bool HasFearRecoveryAnchor { get; private set; }
        public double FearRecoveryAnchorGameTime { get; private set; }
        public double FearRecoveryHalfLifeSeconds { get; private set; }
        public double FearRecoveryBaselineRemainder { get; private set; }
        public bool HasResentmentRecoveryAnchor { get; private set; }
        public double ResentmentRecoveryAnchorGameTime { get; private set; }
        public double ResentmentRecoveryHalfLifeSeconds { get; private set; }
        public double ResentmentRecoveryBaselineRemainder { get; private set; }
        public double ProjectedAtGameTime { get; private set; } = -1;

        public NpcDirectedRelationshipView ProjectFear(double now, NpcPersonalityProfile personality,
            NpcFearRecoveryPolicy policy = null)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (!NpcDirectedRelationship.ValidProjectionPersonality(ObserverId, personality))
                throw new ArgumentException("Fear recovery personality must belong to the relationship observer.", nameof(personality));
            if (HasFearRecoveryAnchor && now < FearRecoveryAnchorGameTime)
                throw new ArgumentOutOfRangeException(nameof(now));
            var selectedPolicy = policy ?? NpcFearRecoveryPolicy.ProvisionalDefault;
            int projected = HasFearRecoveryAnchor
                ? selectedPolicy.Project(StoredInterpersonalFear + FearRecoveryBaselineRemainder,
                    FearRecoveryAnchorGameTime, now, FearRecoveryHalfLifeSeconds)
                : StoredInterpersonalFear;
            return new NpcDirectedRelationshipView(this, projected, now);
        }

        public NpcDirectedRelationshipView ProjectRecovery(double now, NpcPersonalityProfile personality,
            NpcFearRecoveryPolicy fearPolicy = null, NpcResentmentRecoveryPolicy resentmentPolicy = null)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (!NpcDirectedRelationship.ValidProjectionPersonality(ObserverId, personality))
                throw new ArgumentException("Relationship recovery personality must belong to the relationship observer.", nameof(personality));
            if (HasFearRecoveryAnchor && now < FearRecoveryAnchorGameTime ||
                HasResentmentRecoveryAnchor && now < ResentmentRecoveryAnchorGameTime)
                throw new ArgumentOutOfRangeException(nameof(now));
            var selectedFearPolicy = fearPolicy ?? NpcFearRecoveryPolicy.ProvisionalDefault;
            var selectedResentmentPolicy = resentmentPolicy ?? NpcResentmentRecoveryPolicy.ProvisionalDefault;
            int projectedFear = HasFearRecoveryAnchor
                ? selectedFearPolicy.Project(StoredInterpersonalFear + FearRecoveryBaselineRemainder,
                    FearRecoveryAnchorGameTime, now, FearRecoveryHalfLifeSeconds)
                : StoredInterpersonalFear;
            int projectedResentment = HasResentmentRecoveryAnchor
                ? selectedResentmentPolicy.Project(StoredResentment + ResentmentRecoveryBaselineRemainder,
                    ResentmentRecoveryAnchorGameTime, now, ResentmentRecoveryHalfLifeSeconds)
                : StoredResentment;
            return new NpcDirectedRelationshipView(this, projectedFear, projectedResentment, now);
        }

        private NpcDirectedRelationshipView(NpcDirectedRelationshipView source, int projectedFear,
            int projectedResentment, double projectedAt) : this(source, projectedFear, projectedAt)
        {
            ProjectedResentment = projectedResentment;
        }

        private NpcDirectedRelationshipView(NpcDirectedRelationshipView source, int projectedFear, double projectedAt)
        {
            ObserverId = source.ObserverId;
            TargetId = source.TargetId;
            Revision = source.Revision;
            LastGameTime = source.LastGameTime;
            TrustKnown = source.TrustKnown;
            ReliabilityTrust = source.ReliabilityTrust;
            TrustConfidence = source.TrustConfidence;
            TrustEvidenceCount = source.TrustEvidenceCount;
            Resentment = source.Resentment;
            StoredResentment = source.StoredResentment;
            ProjectedResentment = source.ProjectedResentment;
            InterpersonalFear = source.InterpersonalFear;
            StoredInterpersonalFear = source.StoredInterpersonalFear;
            ProjectedInterpersonalFear = source.PlayerFearSuppressed ? 0 : projectedFear;
            PlayerFearSuppressed = source.PlayerFearSuppressed;
            RoleRevision = source.RoleRevision;
            HasFearRecoveryAnchor = source.HasFearRecoveryAnchor;
            FearRecoveryAnchorGameTime = source.FearRecoveryAnchorGameTime;
            FearRecoveryHalfLifeSeconds = source.FearRecoveryHalfLifeSeconds;
            FearRecoveryBaselineRemainder = source.FearRecoveryBaselineRemainder;
            HasResentmentRecoveryAnchor = source.HasResentmentRecoveryAnchor;
            ResentmentRecoveryAnchorGameTime = source.ResentmentRecoveryAnchorGameTime;
            ResentmentRecoveryHalfLifeSeconds = source.ResentmentRecoveryHalfLifeSeconds;
            ResentmentRecoveryBaselineRemainder = source.ResentmentRecoveryBaselineRemainder;
            ProjectedAtGameTime = projectedAt;
        }

        internal NpcDirectedRelationshipView WithPlayerFearPolicy(bool suppressed, long roleRevision)
        {
            var copy = new NpcDirectedRelationshipView(this, ProjectedInterpersonalFear, ProjectedAtGameTime);
            copy.PlayerFearSuppressed = suppressed;
            copy.RoleRevision = roleRevision;
            if (suppressed)
            {
                copy.InterpersonalFear = 0;
                copy.ProjectedInterpersonalFear = 0;
            }
            return copy;
        }
    }
}
