namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Applies the small relationship recovery earned by one accepted, safe player speech appraisal.</summary>
    internal static class NpcConversationCalming
    {
        private const int MaximumDecreasePerAcceptedTurn = 2;
        private const int NeutralDecreasePerAcceptedTurn = 1;

        internal static NpcExperienceAppraisal ApplyAcceptedSpeech(NpcExperienceAppraisalRequest request,
            NpcExperienceAppraisal appraisal, bool mayRecover = true)
        {
            // A delayed appraisal can be recaptured against newer emotional state.
            // Suppress its negative proposals too, not only automatic safe-turn cooling.
            if (!mayRecover && request != null && appraisal != null && request.RequiresSpeechAppraisal &&
                request.RelationshipTargetId == NpcDirectedRelationship.PlayerTargetId)
                return new NpcExperienceAppraisal(request, appraisal.MoodDelta, appraisal.AffinityDelta,
                    appraisal.TrustDelta, System.Math.Max(0, appraisal.ResentmentDelta),
                    System.Math.Max(0, appraisal.FearDelta), appraisal.SpeechActKind,
                    appraisal.SpeechTargetPersonId, appraisal.ConversationOption);
            if (!IsEligible(request, appraisal)) return appraisal;

            int projectedResentment = request.Relationship.ProjectedResentment;
            int projectedFear = request.Relationship.ProjectedInterpersonalFear;
            int resentment = CalmDelta(appraisal.ResentmentDelta, projectedResentment);
            int fear = request.PlayerFearSuppressed || request.Relationship.PlayerFearSuppressed
                ? appraisal.FearDelta
                : CalmDelta(appraisal.FearDelta, projectedFear);

            if (resentment == appraisal.ResentmentDelta && fear == appraisal.FearDelta) return appraisal;
            return new NpcExperienceAppraisal(request, appraisal.MoodDelta, appraisal.AffinityDelta,
                appraisal.TrustDelta, resentment, fear, appraisal.SpeechActKind,
                appraisal.SpeechTargetPersonId, appraisal.ConversationOption);
        }

        private static bool IsEligible(NpcExperienceAppraisalRequest request,
            NpcExperienceAppraisal appraisal)
        {
            if (request == null || appraisal == null || !request.RequiresSpeechAppraisal ||
                request.SpeechClassification == null || request.SpeechClassification.IsDirectHostility ||
                request.SpeechClassification.Request == null ||
                request.SpeechClassification.Request.ListenerRefusesFurtherCommunication ||
                request.SpeechClassification.Kind != appraisal.SpeechActKind ||
                !string.Equals(request.RelationshipTargetId, NpcDirectedRelationship.PlayerTargetId,
                    System.StringComparison.Ordinal) || request.Relationship == null ||
                !string.IsNullOrEmpty(appraisal.SpeechTargetPersonId))
                return false;

            return appraisal.SaveBranchId == request.SaveBranchId &&
                appraisal.ObserverId == request.ObserverId &&
                appraisal.SubjectPersonId == request.SubjectPersonId &&
                appraisal.EvidenceDeduplicationKey == request.EvidenceDeduplicationKey &&
                appraisal.CanonicalEvidenceId == request.CanonicalEvidenceId &&
                appraisal.RelationshipTargetId == request.RelationshipTargetId &&
                appraisal.ExpectedMoodRevision == request.ExpectedMoodRevision &&
                appraisal.ExpectedAffinityRevision == request.ExpectedAffinityRevision &&
                appraisal.ExpectedRelationshipRevision == request.ExpectedRelationshipRevision;
        }

        private static int CalmDelta(int proposedDelta, int projectedValue)
        {
            if (projectedValue <= 0) return proposedDelta > 0 ? proposedDelta : 0;
            if (proposedDelta < 0) return System.Math.Max(-MaximumDecreasePerAcceptedTurn, proposedDelta);
            if (proposedDelta == 0) return -NeutralDecreasePerAcceptedTurn;
            return proposedDelta;
        }
    }

    public sealed partial class WorldStateStore
    {
        private bool CanSpeechRecoverCurrentBoundary(NpcExperienceAppraisalRequest request)
        {
            if (!request.RequiresSpeechAppraisal || request.RelationshipTargetId != NpcDirectedRelationship.PlayerTargetId)
                return true;
            var boundary = ReadNpcConversationBoundary(request.ObserverId, request.CapturedGameTime);
            if (boundary == null) return true;
            // Game time is paused in Talk. Only the saved observer-scoped speech
            // ordinal can distinguish a later safe turn from an earlier queued one.
            string receiptId = request.Evidence.ReceiptId;
            if (string.IsNullOrEmpty(receiptId)) return false;
            int separator = receiptId.LastIndexOf('-');
            long ordinal;
            return separator >= 0 && long.TryParse(receiptId.Substring(separator + 1),
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                out ordinal) && ordinal > boundary.Ordinal &&
                receiptId == NpcObservedSpeechReceipt.IssuedReceiptId(NpcFrameworkScopeId, request.ObserverId, ordinal);
        }
    }
}
