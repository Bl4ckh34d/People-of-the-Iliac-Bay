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
        private bool DecisionWillingnessEnabled => decisionRoutingSession != null && decisionRoutingSession.Enabled &&
            decisionRoutingConnection != null && isActiveAndEnabled && options.Enabled && localDialogueEnabled && !saveFault;

        // Explicit experiment only. A configured D3 outage never silently
        // changes the decision backend or turns extraction into consent.
        private bool QwenWillingnessEnabled => !decisionPlannerEnabled && isActiveAndEnabled && options.Enabled &&
            localDialogueEnabled && localDialogue && !saveFault &&
            Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-qwen-willingness") >= 0;
        private long qwenWillingnessDispatchCount, qwenWillingnessCompletionCount;
        private string qwenWillingnessLastChoice;
        private string qwenWillingnessRequestJson, qwenWillingnessResponseJson, qwenWillingnessRawChoice;

        private bool TryBeginQwenWillingness(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window,
            string originalText, IList<NpcActionDecision> proposed, Action<string> complete)
        {
            if (!QwenWillingnessEnabled || complete == null || !IsCurrentConversationContext(snapshot, window)) return false;
            qwenWillingnessLastChoice = null;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-binding-probe") >= 0)
                qwenWillingnessRequestJson = qwenWillingnessResponseJson = qwenWillingnessRawChoice = null;
            string prompt;
            try
            {
                var proposal = new LocalDecisionWillingnessProposal(proposed);
                var request = LocalDecisionWillingness.Build(snapshot, originalText, ReadDecisionShadowHistory(window),
                    ReadCapturedConversationMemory(snapshot, window), proposal);
                prompt = LocalQwenWillingness.Build(request);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
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
                CompleteQwenWillingness(snapshot, window, originalText, session, person, stage,
                    proposed.Count, output, complete);
            }, LocalQwenWillingness.Grammar(), 16, classificationTrace: (request, response, result) =>
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-binding-probe") < 0) return;
                qwenWillingnessRequestJson = request;
                qwenWillingnessResponseJson = response;
                qwenWillingnessRawChoice = result;
            });
            if (submitted) { qwenWillingnessDispatchCount++; return true; }
            if (!TryConsumeConversationContextStage(snapshot, window, stage)) return true;
            return false;
        }

        // Shared completion boundary also permits controlled lifecycle checks
        // without replacing or retrying any real model response.
        private void CompleteQwenWillingness(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window,
            string originalText, int session, string person, long stage, int proposedCount,
            string output, Action<string> complete)
        {
            if (!QwenWillingnessEnabled || complete == null || !CurrentMemorySession(window, session) ||
                person != conversationPersonId) return;
            if (!TryConsumeConversationContextStage(snapshot, window, stage))
            { HandleRejectedConversationStage(snapshot, window, stage, originalText, "No new action was accepted."); return; }
            qwenWillingnessCompletionCount++;
            string choice = LocalQwenWillingness.ParseChoice(output);
            qwenWillingnessLastChoice = choice;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_QWEN_WILLINGNESS_RESULT: choice=" + (choice ?? "unconfirmed") +
                "; steps=" + proposedCount + "; nativeDispatch=false");
            try { complete(choice); }
            catch (Exception error) { Debug.LogException(error); throw; }
        }

        // A typed proposal is not agreement. This stage never commits a job.
        private bool TryBeginDecisionWillingness(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window,
            string originalText, IList<NpcActionDecision> proposed, Action<string> complete)
        {
            if (!DecisionWillingnessEnabled || complete == null) return false;
            if (!IsCurrentConversationContext(snapshot, window)) return false;
            NpcDecisionRequest request;
            try
            {
                var proposal = new LocalDecisionWillingnessProposal(proposed);
                request = LocalDecisionWillingness.Build(snapshot, originalText, ReadDecisionShadowHistory(window),
                    ReadCapturedConversationMemory(snapshot, window), proposal);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            int session = memorySession;
            string person = conversationPersonId;
            long stage = BeginConversationContextStage(snapshot, window);
            if (stage <= 0)
            {
                HandleRejectedConversationStage(snapshot, window, stage, originalText, "No new action was accepted.");
                return true;
            }
            bool submitted = decisionRoutingSession.TryRequest(decisionRoutingConnection, request,
                () => IsCurrentConversationContext(snapshot, window)
                    ? new LocalDecisionFreshness(snapshot.Binding, snapshot.Revision, request.CandidateSetRevision, request.Options)
                    : null,
                result =>
                {
                    if (!CurrentMemorySession(window, session) || person != conversationPersonId) return;
                    if (!TryConsumeConversationContextStage(snapshot, window, stage))
                    { HandleRejectedConversationStage(snapshot, window, stage, originalText, "No new action was accepted."); return; }
                    string choice = result.Status == NpcDecisionStatus.Ok ? result.SelectedOptionId : null;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_WILLINGNESS_RESULT: status=" + result.Status +
                        "; choice=" + (choice ?? "unconfirmed") + "; steps=" + proposed.Count +
                        "; latencyMs=" + result.LatencyMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
                        "; nativeDispatch=false");
                    // The session counts callback failures, but a native commit
                    // failure also needs a stack trace. Never retry a callback:
                    // its native effect may already have been saved.
                    try { complete(choice); }
                    catch (Exception error) { Debug.LogException(error); throw; }
                });
            if (submitted) return true;
            if (!TryConsumeConversationContextStage(snapshot, window, stage)) return true;
            return false;
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
            string originalText, string history, ConversationContextSnapshot snapshot, bool required,
            IList<NpcActionDecision> proposed, Action<List<NpcActionDecision>> accept)
        {
            // Legacy Qwen already assessed willingness. Interpretation-only mode
            // must never reuse this branch after a decision service outage.
            if (!required)
            {
                if (decisionPlannerEnabled)
                {
                    SubmitUnconfirmedWillingness(window, originalText, history, snapshot, null);
                    return;
                }
                accept(new List<NpcActionDecision>(proposed));
                return;
            }
            List<NpcActionDecision> frozen;
            LocalDecisionWillingnessProposal proposal;
            string reason;
            try
            {
                frozen = new LocalDecisionWillingnessProposal(proposed).CopyDecisions();
                foreach (var step in frozen)
                    if (!PrepareSemanticActionDecision(actor, step, out reason))
                    {
                        SubmitWillingnessTargetChanged(window, originalText, history, snapshot, reason);
                        return;
                    }
                proposal = new LocalDecisionWillingnessProposal(frozen);
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
            bool submitted = DecisionWillingnessEnabled
                ? TryBeginDecisionWillingness(snapshot, window, originalText, frozen, finish)
                : TryBeginQwenWillingness(snapshot, window, originalText, frozen, finish);
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
            if (!string.Equals(new LocalDecisionWillingnessProposal(decisions).Signature, expectedSignature, StringComparison.Ordinal))
            {
                reason = "The proposal changed while considering it. No action was accepted.";
                return false;
            }
            return true;
        }
    }
}
