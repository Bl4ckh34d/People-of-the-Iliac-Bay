using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private bool DialogueWillingnessEnabled => isActiveAndEnabled && options.Enabled &&
            localDialogueEnabled && localDialogue && !saveFault;
        private long dialogueWillingnessDispatchCount, dialogueWillingnessCompletionCount;
        private string dialogueWillingnessLastChoice;
        private string dialogueWillingnessRequestJson, dialogueWillingnessResponseJson, dialogueWillingnessRawChoice;

        private bool TryBeginDialogueWillingness(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window,
            string originalText, IList<NpcActionDecision> proposed, Action<string> complete)
        {
            if (!DialogueWillingnessEnabled || complete == null || !IsCurrentConversationContext(snapshot, window)) return false;
            dialogueWillingnessLastChoice = null;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-binding-probe") >= 0)
                dialogueWillingnessRequestJson = dialogueWillingnessResponseJson = dialogueWillingnessRawChoice = null;
            string prompt;
            try
            {
                var proposal = new LocalActionWillingnessProposal(proposed);
                var request = LocalActionWillingness.Build(snapshot, originalText, ReadConversationContextHistory(window),
                    ReadCapturedConversationMemory(snapshot, window), proposal);
                prompt = LocalDialogueWillingness.Build(request);
                var paidFollow=store==null?null:store.ReadPaidFollowForTravelTask(conversationPersonId,ConversationActionTime());
                if(paidFollow!=null)
                    prompt+="\nVerified existing paid travel service: contract "+NpcPaidCompanionAction.ContractId(paidFollow)+
                        "; paid until absolute game second "+paidFollow.FinishAt.ToString(CultureInfo.InvariantCulture)+
                        ". Local MoveTo and Wait travel guidance can use this same service, then resume following. No extra pay or longer service is offered. Decide willingness normally for this particular request.";
            }
            catch (ArgumentException error)
            { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_WILLINGNESS_PROPOSAL_REJECTED: " + error.Message); return false; }
            catch (InvalidOperationException error)
            { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_WILLINGNESS_CONTEXT_REJECTED: " + error.Message); return false; }
            int session = memorySession;
            string person = conversationPersonId;
            long stage = BeginConversationContextStage(snapshot, window);
            if (stage <= 0)
            {
                HandleRejectedConversationStage(snapshot, window, stage, originalText, "No new action was accepted.");
                return true;
            }
            bool submitted = localDialogue.Classify(NewDialogueConnection(), prompt, string.Empty, output =>
            {
                CompleteDialogueWillingness(snapshot, window, originalText, session, person, stage,
                    proposed.Count, output, complete);
            }, LocalDialogueWillingness.Grammar(), 16, classificationTrace: (request, response, result) =>
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-binding-probe") < 0) return;
                dialogueWillingnessRequestJson = request;
                dialogueWillingnessResponseJson = response;
                dialogueWillingnessRawChoice = result;
            });
            if (submitted) { dialogueWillingnessDispatchCount++; return true; }
            if (!TryConsumeConversationContextStage(snapshot, window, stage)) return true;
            return false;
        }

        // Shared completion boundary also permits controlled lifecycle checks
        // without replacing or retrying any real model response.
        private void CompleteDialogueWillingness(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window,
            string originalText, int session, string person, long stage, int proposedCount,
            string output, Action<string> complete)
        {
            if (!DialogueWillingnessEnabled || complete == null || !CurrentMemorySession(window, session) ||
                person != conversationPersonId) return;
            if (!TryConsumeConversationContextStage(snapshot, window, stage))
            { HandleRejectedConversationStage(snapshot, window, stage, originalText, "No new action was accepted."); return; }
            dialogueWillingnessCompletionCount++;
            string choice = LocalDialogueWillingness.ParseChoice(output);
            dialogueWillingnessLastChoice = choice;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_WILLINGNESS_RESULT: choice=" + (choice ?? "unconfirmed") +
                "; steps=" + proposedCount + "; nativeDispatch=false");
            try { complete(choice); }
            catch (Exception error) { Debug.LogException(error); throw; }
        }

        private void SubmitUnconfirmedWillingness(DaggerfallTalkWindow window, string originalText, string history,
            ConversationContextSnapshot snapshot, string choice)
        {
            string fact = choice == "DECLINE"
                ? "The NPC chooses not to accept the proposed action or ordered task. No action or task was saved. Explain the refusal naturally using the NPC's circumstances; do not invent effects."
                : "Agreement is not confirmed. No action or task was saved. Do not claim a personal refusal or acceptance. Ask the player to try again shortly, in character.";
            conversationResponseCue += " " + fact;
            SubmitSocialText(window, originalText, history, knowledgeResult: fact, snapshot: snapshot);
        }

        private void ResolveProposedActionWillingness(ResidentActor actor, DaggerfallTalkWindow window,
            string originalText, string history, ConversationContextSnapshot snapshot,
            IList<NpcActionDecision> proposed, Action<List<NpcActionDecision>> accept)
        {
            if(proposed!=null && proposed.Count>1)
            {ResolveTaskFailureSemantics(actor,window,originalText,history,snapshot,proposed,accept);return;}
            ResolveProposedActionWillingnessCore(actor,window,originalText,history,snapshot,proposed,accept);
        }

        private void ResolveProposedActionWillingnessCore(ResidentActor actor, DaggerfallTalkWindow window,
            string originalText, string history, ConversationContextSnapshot snapshot,
            IList<NpcActionDecision> proposed, Action<List<NpcActionDecision>> accept)
        {
            List<NpcActionDecision> frozen;
            LocalActionWillingnessProposal proposal;
            string reason;
            try
            {
                frozen = new LocalActionWillingnessProposal(proposed).CopyDecisions();
                foreach (var step in frozen)
                    if (!PrepareSemanticActionDecision(actor, step, out reason))
                    {
                        SubmitWillingnessTargetChanged(window, originalText, history, snapshot, reason);
                        return;
                    }
                proposal = new LocalActionWillingnessProposal(frozen);
            }
            catch (ArgumentException)
            { SubmitUnconfirmedWillingness(window, originalText, history, snapshot, null); return; }
            Action<string> finish = choice =>
            {
                if (!IsCurrentConversationContext(snapshot, window)) return;
                if (choice != "ACCEPT")
                { SubmitUnconfirmedWillingness(window, originalText, history, snapshot, choice); return; }
                var agreed = proposal.CopyDecisions();
                string changedReason;
                if (!PrepareUnchangedWillingnessProposal(actor, agreed, proposal.Signature, out changedReason))
                { SubmitWillingnessTargetChanged(window, originalText, history, snapshot, changedReason); return; }
                accept(agreed);
            };
            bool submitted = TryBeginDialogueWillingness(snapshot, window, originalText, frozen, finish);
            if (!submitted) finish(null);
        }

        private void SubmitWillingnessTargetChanged(DaggerfallTalkWindow window, string originalText, string history,
            ConversationContextSnapshot snapshot, string reason)
        {
            string fact = "The proposed action could not be accepted with the current target or resources: " + reason +
                " No action or task was saved. Explain the practical issue naturally without claiming refusal or completed effects.";
            conversationResponseCue += " " + fact;
            SubmitSocialText(window, originalText, history, knowledgeResult: fact, snapshot: snapshot);
        }

        private bool PrepareUnchangedWillingnessProposal(ResidentActor actor, List<NpcActionDecision> decisions,
            string expectedSignature, out string reason)
        {
            reason = "The proposed action is no longer available.";
            if (!actor || actor != conversationActor || actor.Resident == null || actor.Resident.Id != conversationPersonId)
                return false;
            foreach (var step in decisions)
                if (!PrepareSemanticActionDecision(actor, step, out reason)) return false;
            if (!string.Equals(new LocalActionWillingnessProposal(decisions).Signature, expectedSignature, StringComparison.Ordinal))
            {
                reason = "The proposal changed while considering it. No action was accepted.";
                return false;
            }
            return true;
        }
    }
}
