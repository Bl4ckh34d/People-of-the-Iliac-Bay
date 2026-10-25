using System;
using System.Collections.Generic;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Detached replacement for one NPC's player-directed affinity row.</summary>
    public sealed class ConversationAffinityEventUpdate
    {
        internal ConversationAffinityEventUpdate(string branch, string observer, string eventKey,
            string canonicalEventId, double committedGameTime, long expectedRevision, long nextRevision,
            int proposedDelta, int appliedDelta, ConversationAttitude sourceAttitude,
            ConversationAttitude nextAttitude)
        {
            SaveBranchId = branch; ObserverId = observer; EvidenceDeduplicationKey = eventKey;
            CanonicalEvidenceId = canonicalEventId; CommittedGameTime = committedGameTime;
            ExpectedRevision = expectedRevision; NextRevision = nextRevision;
            ProposedDelta = proposedDelta; AppliedDelta = appliedDelta;
            SourceAttitude = Copy(sourceAttitude); NextAttitude = Copy(nextAttitude);
        }

        public string SaveBranchId { get; private set; }
        public string ObserverId { get; private set; }
        public string EvidenceDeduplicationKey { get; private set; }
        public string CanonicalEvidenceId { get; private set; }
        public double CommittedGameTime { get; private set; }
        public long ExpectedRevision { get; private set; }
        public long NextRevision { get; private set; }
        public int ProposedDelta { get; private set; }
        public int AppliedDelta { get; private set; }
        public ConversationAttitude SourceAttitude { get; private set; }
        public ConversationAttitude NextAttitude { get; private set; }

        internal static ConversationAttitude Copy(ConversationAttitude source)
        {
            if (source == null) return null;
            return new ConversationAttitude
            {
                PersonId = source.PersonId, Offset = source.Offset, NativeBaseline = source.NativeBaseline,
                HasNativeBaseline = source.HasNativeBaseline, NativeFactionId = source.NativeFactionId,
                HasNativeFactionId = source.HasNativeFactionId, Day = source.Day,
                PositiveToday = source.PositiveToday, NegativeToday = source.NegativeToday,
                PositiveCredit = source.PositiveCredit, PositiveCreditUpdatedAt = source.PositiveCreditUpdatedAt,
                PositiveProgressionStage = source.PositiveProgressionStage,
                PositiveStageProgress = source.PositiveStageProgress, PositiveEarned = source.PositiveEarned,
                PositiveThisEncounter = source.PositiveThisEncounter, Insults = source.Insults,
                EncounterCount = source.EncounterCount, LastEncounterAt = source.LastEncounterAt,
                SharedExchangeCount = source.SharedExchangeCount, FriendlyInitiatedCount = source.FriendlyInitiatedCount,
                LastFriendlyInitiatedAt = source.LastFriendlyInitiatedAt,
                Utterances = source.Utterances == null ? new List<string>() : new List<string>(source.Utterances)
            };
        }
    }

    /// <summary>
    /// Pure event-affinity reducer. The caller must persist the returned attitude and event journal in the
    /// same transaction as mood, relationship and inbox changes. Positive gains reuse the conversation
    /// credit/encounter governor; negative proposals are bounded by the existing -200 engine endpoint only.
    /// </summary>
    public static class ConversationAffinityEventPolicy
    {
        private const int MaximumNegativeAppraisalDelta = 50;
        private const int MaximumPositiveAppraisalDelta = 5;

        public static bool TryCreate(string saveBranchId, NpcExperienceEvent evidence,
            NpcExperienceAppraisalRequest request, NpcExperienceAppraisal appraisal,
            ConversationAttitude current, long currentRevision,
            out ConversationAffinityEventUpdate update, out string rejection)
        {
            update = null; rejection = null;
            if (!ValidIdentity(saveBranchId, 128) || evidence == null || request == null || appraisal == null ||
                currentRevision < 0)
                return Reject("invalid-affinity-input", out rejection);

            NpcExperienceEvent fact;
            try { fact = NpcExperienceEvent.CopyValidated(evidence); }
            catch (ArgumentException) { return Reject("invalid-event-evidence", out rejection); }

            string canonicalId = fact.Origin == NpcExperienceEventOrigin.NativeOutcome ? fact.OutcomeId : fact.UtteranceId;
            if (fact.Origin != NpcExperienceEventOrigin.NativeOutcome && fact.Origin != NpcExperienceEventOrigin.AttributedSpeech)
                return Reject("unsupported-event-origin", out rejection);
            if (request.SaveBranchId != saveBranchId || appraisal.SaveBranchId != saveBranchId ||
                request.ObserverId != fact.ObserverId || appraisal.ObserverId != fact.ObserverId ||
                request.SubjectPersonId != fact.SubjectPersonId || appraisal.SubjectPersonId != fact.SubjectPersonId ||
                request.EvidenceDeduplicationKey != fact.DeduplicationKey ||
                appraisal.EvidenceDeduplicationKey != fact.DeduplicationKey ||
                request.CanonicalEvidenceId != canonicalId || appraisal.CanonicalEvidenceId != canonicalId ||
                appraisal.ObservedGameTime != fact.ObservedGameTime || appraisal.CapturedGameTime != request.CapturedGameTime ||
                appraisal.RelationshipTargetId != request.RelationshipTargetId)
                return Reject("stale-or-mismatched-affinity-context", out rejection);
            if (appraisal.ExpectedAffinityRevision != currentRevision || request.ExpectedAffinityRevision != currentRevision)
                return Reject("stale-affinity-revision", out rejection);
            if (ActualAffinity(current) != request.CurrentAffinity)
                return Reject("stale-affinity-value", out rejection);

            if (request.RelationshipTargetId != NpcDirectedRelationship.PlayerTargetId ||
                fact.Origin == NpcExperienceEventOrigin.NativeOutcome &&
                    (fact.ResponsibleActorAttribution != NpcResponsibleActorAttribution.Known ||
                     fact.ResponsibleActorId != NpcDirectedRelationship.PlayerTargetId) ||
                fact.Origin == NpcExperienceEventOrigin.AttributedSpeech &&
                    fact.AttributedSpeakerId != NpcDirectedRelationship.PlayerTargetId)
                return Reject("player-attribution-required", out rejection);
            if (appraisal.AffinityDelta == 0 || appraisal.AffinityDelta < -MaximumNegativeAppraisalDelta ||
                appraisal.AffinityDelta > MaximumPositiveAppraisalDelta)
                return Reject("affinity-delta-out-of-policy-bounds", out rejection);
            if (appraisal.AffinityDelta < -10 &&
                (fact.Origin != NpcExperienceEventOrigin.AttributedSpeech ||
                 !IsSevereSpeechTarget(request, appraisal.SpeechTargetPersonId) ||
                 appraisal.SpeechActKind == NpcExperienceSpeechActKind.DirectThreat &&
                    appraisal.AffinityDelta < -NpcExperienceAppraisalContract.DirectThreatAffinityLossBound ||
                 appraisal.SpeechActKind == NpcExperienceSpeechActKind.DirectInsult &&
                    appraisal.AffinityDelta < -NpcExperienceAppraisalContract.DirectInsultAffinityLossBound ||
                 appraisal.SpeechActKind != NpcExperienceSpeechActKind.DirectThreat &&
                    appraisal.SpeechActKind != NpcExperienceSpeechActKind.DirectInsult))
                return Reject("severe-negative-speech-policy-mismatch", out rejection);

            ConversationAttitude next = ConversationAffinityEventUpdate.Copy(current);
            if (next == null)
                next = new ConversationAttitude { PersonId = fact.ObserverId, Day = Math.Floor(request.CapturedGameTime / 86400d) };
            if (!ValidAttitude(next, fact.ObserverId)) return Reject("invalid-affinity-row", out rejection);
            if (currentRevision == long.MaxValue) return Reject("affinity-revision-exhausted", out rejection);
            if (request.CapturedGameTime < next.PositiveCreditUpdatedAt || request.CapturedGameTime < next.Day * 86400d)
                return Reject("affinity-time-regression", out rejection);
            if (request.PositiveAffinityCredit != AvailableCredit(next, request.CapturedGameTime))
                return Reject("stale-positive-credit-budget", out rejection);

            double eventDay = Math.Floor(request.CapturedGameTime / 86400d);
            if (eventDay > next.Day)
            {
                next.Day = eventDay; next.PositiveToday = 0; next.NegativeToday = 0;
                next.Utterances.Clear();
            }
            int before = next.Offset;
            int delta = appraisal.AffinityDelta;
            if (delta > 0)
            {
                WorldStateStore.UpdatePositiveCredit(next, request.CapturedGameTime);
                delta = Math.Min(delta, Math.Min(5 - next.PositiveThisEncounter,
                    Math.Min((int)Math.Floor(next.PositiveCredit), 100 - next.Offset)));
                if (delta > 0)
                {
                    next.PositiveToday = Math.Min(1000000, next.PositiveToday + delta);
                    next.PositiveThisEncounter = Math.Min(5, next.PositiveThisEncounter + delta);
                    next.PositiveCredit = Math.Max(0, next.PositiveCredit - delta);
                    next.PositiveEarned = Math.Min(1000000, next.PositiveEarned + delta);
                    int beyondInitial = Math.Max(0, next.PositiveEarned - 5);
                    next.PositiveProgressionStage = Math.Min(1000000, beyondInitial / 5);
                    next.PositiveStageProgress = beyondInitial % 5;
                }
            }
            else
            {
                next.Offset = Math.Max(-200, Math.Min(100, next.Offset + delta));
                delta = next.Offset - before;
                if (delta < 0) next.NegativeToday = Math.Min(1000000, next.NegativeToday - delta);
            }
            if (delta > 0) next.Offset = before + delta;

            bool changed = delta != 0 || next.Day != (current == null ? next.Day : current.Day) ||
                next.PositiveCreditUpdatedAt != (current == null ? 0 : current.PositiveCreditUpdatedAt);
            update = new ConversationAffinityEventUpdate(saveBranchId, fact.ObserverId, fact.DeduplicationKey,
                canonicalId, request.CapturedGameTime, currentRevision, changed ? currentRevision + 1 : currentRevision,
                appraisal.AffinityDelta, delta, current, next);
            return true;
        }

        private static int AvailableCredit(ConversationAttitude item, double now)
        {
            var projection = ConversationAffinityEventUpdate.Copy(item);
            WorldStateStore.UpdatePositiveCredit(projection, now);
            return (int)Math.Floor(projection.PositiveCredit);
        }

        private static bool ValidAttitude(ConversationAttitude item, string observer)
        {
            return item != null && item.PersonId == observer && ValidIdentity(item.PersonId, 240) &&
                item.Offset >= -200 && item.Offset <= 100 && Finite(item.Day) && item.Day >= 0 &&
                item.Day == Math.Floor(item.Day) &&
                (!item.HasNativeBaseline || item.NativeBaseline >= -100 && item.NativeBaseline <= 100) &&
                (!item.HasNativeFactionId || item.NativeFactionId >= 0) &&
                item.PositiveToday >= 0 && item.PositiveToday <= 1000000 && item.NegativeToday >= 0 && item.NegativeToday <= 1000000 &&
                Finite(item.PositiveCredit) && item.PositiveCredit >= 0 && item.PositiveCredit <= 5 &&
                Finite(item.PositiveCreditUpdatedAt) && item.PositiveCreditUpdatedAt >= 0 &&
                item.PositiveProgressionStage >= 0 && item.PositiveProgressionStage <= 1000000 &&
                Finite(item.PositiveStageProgress) && item.PositiveStageProgress >= 0 && item.PositiveStageProgress < 5 &&
                item.PositiveEarned >= 0 && item.PositiveEarned <= 1000000 &&
                item.PositiveProgressionStage == Math.Max(0, item.PositiveEarned - 5) / 5 &&
                item.PositiveStageProgress == Math.Max(0, item.PositiveEarned - 5) % 5 &&
                item.PositiveThisEncounter >= 0 && item.PositiveThisEncounter <= 5 &&
                item.Insults >= 0 && item.Insults <= 1000000 && item.EncounterCount >= 0 && item.EncounterCount <= 1000000 &&
                Finite(item.LastEncounterAt) && item.LastEncounterAt >= 0 &&
                item.SharedExchangeCount >= 0 && item.SharedExchangeCount <= 1000000 &&
                item.FriendlyInitiatedCount >= 0 && item.FriendlyInitiatedCount <= 1000000 &&
                Finite(item.LastFriendlyInitiatedAt) && item.LastFriendlyInitiatedAt >= 0 &&
                (item.FriendlyInitiatedCount > 0 || item.LastFriendlyInitiatedAt == 0) &&
                item.Utterances != null && item.Utterances.Count <= 32 &&
                item.Utterances.TrueForAll(x => x != null && x.Length <= 240);
        }

        private static int ActualAffinity(ConversationAttitude item)
        {
            if (item == null || !item.HasNativeBaseline) return item == null ? 0 : item.Offset;
            return ConversationEffects.Reaction(item.NativeBaseline, item.Offset);
        }

        private static bool IsSevereSpeechTarget(NpcExperienceAppraisalRequest request, string target)
        {
            return target == request.ObserverId || request.KnownFamilyIds.Contains(target);
        }

        private static bool ValidIdentity(string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) return false;
            for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return false;
            return true;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Reject(string reason, out string rejection) { rejection = reason; return false; }
    }
}
