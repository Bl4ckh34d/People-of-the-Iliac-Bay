using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        /// <summary>
        /// Terminally defers the exact captured inbox receipt after five completed appraisal attempts.
        /// This records queue disposition only; it applies no appraisal or social effects.
        /// </summary>
        internal bool TryDeferNpcExperienceAppraisal(string observerId,
            NpcExperienceAppraisalTicket ticket, double deferredAtGameTime)
        {
            if (ticket == null || ticket.Owner != this || !ReferenceEquals(ticket.Epoch, bindingEpoch) ||
                ticket.Request == null || ticket.Entry == null || !Finite(deferredAtGameTime) ||
                deferredAtGameTime < ticket.Request.CapturedGameTime ||
                !string.Equals(ticket.Request.SaveBranchId, experienceBranchId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(observerId) ||
                !string.Equals(observerId, ticket.Request.ObserverId, StringComparison.Ordinal) ||
                !string.Equals(observerId, ticket.Entry.ObserverId, StringComparison.Ordinal) ||
                !IsKnownNpcExperienceIdentity(observerId))
                return false;

            NpcExperienceInboxEntry captured;
            NpcExperienceInbox currentInbox;
            try
            {
                captured = NpcExperienceInboxEntry.CopyValidated(ticket.Entry);
                currentInbox = NpcExperienceInbox.CopyValidated(state.NpcExperienceInbox,
                    IsKnownNpcExperienceIdentity);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }

            if (captured.Status != NpcExperienceInboxStatus.Pending || captured.Revision != 1 ||
                captured.Appraisal != null ||
                !string.Equals(captured.DeduplicationKey, ticket.Request.EvidenceDeduplicationKey, StringComparison.Ordinal) ||
                captured.AlreadyAppliedEffectsMask != ticket.Request.AlreadyAppliedEffectsMask)
                return false;

            NpcExperienceInboxEntry current = OldestExperience(observerId);
            if (current == null || current.Revision != captured.Revision ||
                !SameEvidence(current, captured.ToEvent()) || !SameDeferralEvidence(current, captured))
                return false;

            NpcExperienceInboxEntry deferred = null;
            foreach (var row in currentInbox.Entries)
            {
                if (row.ObserverId == observerId && row.DeduplicationKey == captured.DeduplicationKey)
                { deferred = row; break; }
            }
            if (deferred == null || deferred.Status != NpcExperienceInboxStatus.Pending ||
                deferred.Revision != captured.Revision || !SameDeferralEvidence(deferred, captured))
                return false;

            deferred.Status = NpcExperienceInboxStatus.Deferred;
            deferred.Revision = 2;
            deferred.UpdatedAt = deferredAtGameTime;
            deferred.DeferralReason = NpcExperienceDeferralReason.AppraisalAttemptLimit;
            deferred.DeferralAttempts = 5;
            deferred.Appraisal = null;
            if (currentInbox.DetailRevision == long.MaxValue) return false;
            currentInbox.DetailRevision++;

            NpcExperienceInbox nextInbox;
            try { nextInbox = NpcExperienceInbox.CopyValidated(currentInbox, IsKnownNpcExperienceIdentity); }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }

            state.NpcExperienceInbox = nextInbox;
            return true;
        }

        private static bool SameDeferralEvidence(NpcExperienceInboxEntry first, NpcExperienceInboxEntry second)
        {
            return first != null && second != null && first.Origin == second.Origin &&
                first.ObserverId == second.ObserverId && first.SubjectPersonId == second.SubjectPersonId &&
                first.ReceiptId == second.ReceiptId && first.EpisodeId == second.EpisodeId &&
                first.OutcomeId == second.OutcomeId && first.UtteranceId == second.UtteranceId &&
                first.AttributedSpeakerId == second.AttributedSpeakerId && first.EventKind == second.EventKind &&
                first.ObservedGameTime == second.ObservedGameTime && first.ResponsibleActorAttribution == second.ResponsibleActorAttribution &&
                first.ResponsibleActorId == second.ResponsibleActorId && first.DeduplicationKey == second.DeduplicationKey &&
                first.AlreadyAppliedEffectsMask == second.AlreadyAppliedEffectsMask &&
                first.HasKnownImmediateAffinityDelta == second.HasKnownImmediateAffinityDelta &&
                first.ImmediateAffinityDelta == second.ImmediateAffinityDelta && first.AffinityExclusion == second.AffinityExclusion &&
                first.ImpactDetailsKind == second.ImpactDetailsKind && first.HealthInitial == second.HealthInitial &&
                first.HealthFinal == second.HealthFinal && first.HealthMaximum == second.HealthMaximum &&
                first.HealthDamageTotal == second.HealthDamageTotal && first.HealthHealingTotal == second.HealthHealingTotal &&
                first.HealthChangeCount == second.HealthChangeCount && first.TransferQuantity == second.TransferQuantity &&
                first.TransferCurrency == second.TransferCurrency && first.NativeValuationStatus == second.NativeValuationStatus &&
                first.NativeGoldValue == second.NativeGoldValue && first.ReceivedItemUid == second.ReceivedItemUid &&
                first.ObservedTurnObserverId == second.ObservedTurnObserverId &&
                first.ObservedTurnEpisodeId == second.ObservedTurnEpisodeId &&
                first.ObservedTurnSpeakerId == second.ObservedTurnSpeakerId &&
                first.ObservedTurnReceiptId == second.ObservedTurnReceiptId && first.ObservedTurnText == second.ObservedTurnText &&
                first.ObservedTurnGameTime == second.ObservedTurnGameTime && first.Status == NpcExperienceInboxStatus.Pending &&
                second.Status == NpcExperienceInboxStatus.Pending && first.Revision == second.Revision &&
                first.UpdatedAt == second.UpdatedAt && first.DeferralReason == NpcExperienceDeferralReason.None &&
                second.DeferralReason == NpcExperienceDeferralReason.None && first.DeferralAttempts == 0 &&
                second.DeferralAttempts == 0 && first.Appraisal == null && second.Appraisal == null;
        }
    }
}
