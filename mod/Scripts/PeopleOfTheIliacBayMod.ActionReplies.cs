using System;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private ConversationContextBinding conversationActionReplyBinding;

        // The classifier decides intent and consent. Only saved native state may
        // acknowledge acceptance or effects; a second prose request cannot revise it.
        private bool SubmitAuthoritativeActionReply(DaggerfallTalkWindow window, string playerText,
            ConversationContextSnapshot snapshot, ConversationActionReply reply)
        {
            if (reply == null || snapshot == null || snapshot.Binding == null ||
                string.IsNullOrWhiteSpace(playerText) ||
                !string.Equals(playerText, conversationContextCapturedPlayerText, StringComparison.Ordinal) ||
                conversationActionReplyBinding != null &&
                    conversationActionReplyBinding.HasSameRequestBinding(snapshot.Binding))
                return false;
            if (!IsCurrentConversationContext(snapshot, window))
            {
                HandleRejectedConversationStage(snapshot, window, 0, playerText, null);
                return false;
            }
            // Another issued stage still owns the request. Do not consume it.
            if (conversationContextIssuedStageToken != 0) return false;

            if (reply.Source == ConversationActionReplySource.None)
            {
                if (reply.Status != ConversationActionReplyStatus.NoChange &&
                    reply.Status != ConversationActionReplyStatus.Rejected &&
                    reply.Status != ConversationActionReplyStatus.Unknown) return false;
            }
            else
            {
                if (!string.Equals(reply.PersonId, snapshot.Binding.PersonId, StringComparison.Ordinal)) return false;
                if (!reply.MatchesSavedState(store)) reply = ConversationActionReply.Unverified();
            }

            // Mark before invoking presentation callbacks. A reentrant/duplicate
            // completion for this request must not append or journal a second turn.
            conversationActionReplyBinding = snapshot.Binding;
            conversationBusy = false;
            if (appendConversationPair == null)
            {
                conversationContextRejectedRequestRevision = snapshot.Binding.RequestRevision;
                AppendRejectedPlayerTextOnly(window, playerText);
                UnityEngine.Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_ACTION_REPLY_UNAVAILABLE: native dialogue append method is unavailable.");
                return false;
            }
            var speechTicket = conversationObservedSpeechTicket;
            var speechReceipt = conversationObservedSpeech;
            AppendRememberedDialoguePairWithSpeech(window, playerText, reply.NpcText, "action",
                speechTicket, speechReceipt, ObservedSpeechMask(false));
            return true;
        }

        private ConversationActionReply SavedConversationActionReply(ConversationContextSnapshot snapshot,
            string proposalId, bool task, bool committed,
            NpcActionKind? rejectedKind = null)
        {
            var turn = CommandTurnFor(snapshot);
            if (store == null || snapshot == null || snapshot.Binding == null || turn == null)
                return committed ? ConversationActionReply.Unverified() : ConversationActionReply.Rejected(rejectedKind);
            string personId = snapshot.Binding.PersonId;
            string sourceId = task
                ? NpcOperationAdmission.ConversationTaskId(store.NpcFrameworkScopeId, personId, turn.Ordinal)
                : rejectedKind == NpcActionKind.ChangeSchedule
                    ? NpcOperationAdmission.ConversationDepartureId(store.NpcFrameworkScopeId, personId, turn.Ordinal)
                    : NpcOperationAdmission.ConversationActionId(store.NpcFrameworkScopeId, personId, turn.Ordinal);
            var savedTask = task ? store.ReadNpcTask(sourceId) : null;
            var savedAction = task ? null : store.ReadNpcAction(sourceId);
            bool matchesProposal = task
                ? savedTask != null && savedTask.ResidentId == personId && savedTask.ProposalId == proposalId
                : savedAction != null && savedAction.ResidentId == personId && savedAction.ProposalId == proposalId;
            ConversationActionReply reply;
            bool found = task
                ? ConversationActionReply.TryFromSavedTask(store, sourceId, out reply)
                : ConversationActionReply.TryFromSavedAction(store, sourceId, out reply);
            found = found && matchesProposal;
            // A successful native commit is always queryable by its exact ID.
            // If that invariant fails, never manufacture acceptance from prose.
            if (!found && committed)
                UnityEngine.Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_ACTION_REPLY_UNVERIFIED: committed source could not be verified; source=" + sourceId + ".");
            return found ? reply : committed ? ConversationActionReply.Unverified() : ConversationActionReply.Rejected(rejectedKind);
        }

        private ConversationActionReply SavedConversationCancellationReply(
            ConversationContextSnapshot snapshot, bool stopped)
        {
            if (snapshot == null) return ConversationActionReply.Unverified();
            // Prefer the containing task, including cancellation before its first
            // step dispatch. Never substitute some other latest action.
            foreach (var commitment in snapshot.Commitments)
            {
                ConversationActionReply reply;
                if (commitment != null && string.Equals(commitment.Kind, "task", StringComparison.OrdinalIgnoreCase) &&
                    ConversationActionReply.TryFromSavedTask(store, commitment.Id, out reply) &&
                    (!stopped || reply.Status == ConversationActionReplyStatus.Cancelled))
                    return stopped ? reply : reply.ForUnsuccessfulCancellation();
            }
            foreach (var commitment in snapshot.Commitments)
            {
                ConversationActionReply reply;
                if (commitment != null && string.Equals(commitment.Kind, "action-task", StringComparison.OrdinalIgnoreCase) &&
                    ConversationActionReply.TryFromSavedAction(store, commitment.Id, out reply) &&
                    (!stopped || reply.Status == ConversationActionReplyStatus.Cancelled))
                    return stopped ? reply : reply.ForUnsuccessfulCancellation();
            }
            return stopped || HasActiveSemanticCommitment(snapshot)
                ? ConversationActionReply.Unverified() : ConversationActionReply.NoChange();
        }
    }
}
