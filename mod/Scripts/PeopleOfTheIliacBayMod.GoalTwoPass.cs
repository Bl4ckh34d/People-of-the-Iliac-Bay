using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private long goalTwoPassDecompositionDispatchCount;
        private long goalTwoPassDecompositionCompletionCount;
        private long goalTwoPassParameterDispatchCount;
        private long goalTwoPassParameterCompletionCount;
        private long goalTwoPassRepairDispatchCount;
        private long goalTwoPassRepairCompletionCount;
        private string goalTwoPassDecompositionPrompt, goalTwoPassDecompositionOutput;
        private string goalTwoPassDecompositionRequest, goalTwoPassDecompositionResponse;
        private string goalTwoPassParameterPrompt, goalTwoPassParameterOutput;
        private string goalTwoPassParameterRequest, goalTwoPassParameterResponse;
        private string goalTwoPassRepairPrompt, goalTwoPassRepairOutput;
        private string goalTwoPassRepairRequest, goalTwoPassRepairResponse;

        private bool GoalTwoPassEnabled => GoalExtractionEnabled;

        private bool GoalTwoPassProbeEnabled =>
            Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-two-pass-probe") >= 0 ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-binding-probe") >= 0;

        private bool TryBeginGoalActionTwoPass(DaggerfallTalkWindow window, string text, string history,
            ConversationContextSnapshot snapshot)
        {
            if (!IsCurrentConversationContext(snapshot, window)) return false;
            int session = memorySession;
            string person = conversationPersonId;
            if (!IsCurrentGoalTwoPassRequest(window, text, snapshot, session, person))
            {
                RejectGoalAction(window, text, snapshot, "The captured request no longer matches the current turn.");
                return true;
            }

            goalLastExtractionStatus = "pending-decomposition";
            goalLastBindingStatus = "not-started";
            if (GoalTwoPassProbeEnabled)
            {
                goalTwoPassDecompositionPrompt = null;
                goalTwoPassDecompositionOutput = null;
                goalTwoPassDecompositionRequest = null;
                goalTwoPassDecompositionResponse = null;
                goalTwoPassParameterPrompt = null;
                goalTwoPassParameterOutput = null;
                goalTwoPassParameterRequest = null;
                goalTwoPassParameterResponse = null;
                goalTwoPassRepairPrompt = null;
                goalTwoPassRepairOutput = null;
                goalTwoPassRepairRequest = null;
                goalTwoPassRepairResponse = null;
            }

            long stage = BeginConversationContextStage(snapshot, window);
            if (stage <= 0)
            {
                HandleRejectedConversationStage(snapshot, window, stage, text, "No action was accepted.");
                return true;
            }

            string prompt = NpcGoalTwoPassChat.DecompositionInstructions(text,
                ConversationContextFormatter.ForSemanticClassifier(snapshot, 2048));
            Action<string, string, string> trace = (request, response, result) =>
            {
                if (!GoalTwoPassProbeEnabled) return;
                goalTwoPassDecompositionPrompt = prompt;
                goalTwoPassDecompositionRequest = request;
                goalTwoPassDecompositionResponse = response;
                goalTwoPassDecompositionOutput = result;
            };

            if (localDialogue.Extract(NewDialogueConnection(), prompt, history,
                NpcGoalTwoPassChat.DecompositionGrammar(), output =>
                {
                    Increment(ref goalTwoPassDecompositionCompletionCount);
                    if (!CurrentMemorySession(window, session) ||
                        !string.Equals(conversationPersonId, person, StringComparison.Ordinal)) return;
                    if (!IsCurrentGoalTwoPassRequest(window, text, snapshot, session, person) ||
                        !TryConsumeConversationContextStage(snapshot, window, stage))
                    {
                        HandleRejectedConversationStage(snapshot, window, stage, text, "No action was accepted.");
                        return;
                    }
                    ContinueGoalActionDecomposition(window, text, history, snapshot, output, session, person);
                }, 384, trace))
            {
                Increment(ref goalTwoPassDecompositionDispatchCount);
                return true;
            }

            if (!TryConsumeConversationContextStage(snapshot, window, stage)) return true;
            RejectGoalAction(window, text, snapshot, "Decomposition could not start.");
            return true;
        }

        private void ContinueGoalActionDecomposition(DaggerfallTalkWindow window, string text, string history,
            ConversationContextSnapshot snapshot, string output, int session, string person)
        {
            if (!IsCurrentGoalTwoPassRequest(window, text, snapshot, session, person)) return;
            if (output == LocalDialogueClient.Unavailable || output == LocalDialogueClient.ContextLimit)
            {
                goalLastExtractionStatus = "Unavailable";
                RejectGoalAction(window, text, snapshot, "Decomposition service is unavailable.");
                return;
            }

            NpcGoalDecompositionResult decompositionResult = NpcGoalTwoPassChat.ParseDecomposition(
                output, snapshot.Binding, text);
            goalLastExtractionStatus = decompositionResult.Status.ToString();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_TWO_PASS_DECOMPOSITION: status=" + decompositionResult.Status +
                "; intent=" + (decompositionResult.Decomposition == null ? "none" : decompositionResult.Decomposition.Intent.ToString()) +
                "; steps=" + (decompositionResult.Decomposition == null ? 0 : decompositionResult.Decomposition.Kinds.Count));
            if (decompositionResult.Status != NpcGoalExtractionStatus.Valid || decompositionResult.Decomposition == null)
            {
                RejectGoalAction(window, text, snapshot, decompositionResult.Reason);
                return;
            }
            if (decompositionResult.Decomposition.Intent != NpcGoalIntent.Request)
            {
                goalLastExtractionStatus = decompositionResult.Decomposition.Intent.ToString();
                RejectGoalAction(window, text, snapshot, "No executable action request was decomposed.");
                return;
            }
            BeginGoalActionParameterPass(window, text, history, snapshot, decompositionResult.Decomposition, session, person);
        }

        private void BeginGoalActionParameterPass(DaggerfallTalkWindow window, string text, string history,
            ConversationContextSnapshot snapshot, NpcGoalDecomposition decomposition, int session, string person)
        {
            DispatchGoalActionParameterPass(window, text, history, snapshot, decomposition, session, person,
                new NpcGoalParameterAttempts(decomposition));
        }

        // The one-pass extractor enters the same budget with its initial result already counted.
        private void BeginGoalActionParameterRepair(DaggerfallTalkWindow window, string text, string history,
            ConversationContextSnapshot snapshot, NpcGoalDecomposition decomposition, int session, string person,
            string previousOutput, string validationReason)
        {
            var attempts = new NpcGoalParameterAttempts(decomposition);
            attempts.Accept(previousOutput);
            if (!attempts.CanRetry)
            {
                RejectGoalAction(window, text, snapshot, "No unresolved parameter repair remains.");
                return;
            }
            DispatchGoalActionParameterPass(window, text, history, snapshot, decomposition, session, person, attempts);
        }

        private void DispatchGoalActionParameterPass(DaggerfallTalkWindow window, string text, string history,
            ConversationContextSnapshot snapshot, NpcGoalDecomposition decomposition, int session, string person,
            NpcGoalParameterAttempts attempts)
        {
            bool repair = attempts.Count > 0;
            string previousOutput = attempts.RepairAnchor;
            string validationReason = attempts.Reason;
            if (!IsCurrentGoalTwoPassRequest(window, text, snapshot, session, person) ||
                decomposition == null || !ReferenceEquals(decomposition.Binding, snapshot.Binding) ||
                !string.Equals(decomposition.OriginalText, text, StringComparison.Ordinal))
            {
                RejectGoalAction(window, text, snapshot, repair
                    ? "The original request changed before parameter repair."
                    : "The original request changed between extraction passes.");
                return;
            }

            string prompt;
            try
            {
                prompt = repair
                    ? NpcGoalTwoPassChat.RepairInstructions(decomposition, previousOutput,
                        LimitParameterRepairReason(validationReason))
                    : NpcGoalTwoPassChat.ParameterInstructions(decomposition);
            }
            catch (ArgumentException)
            {
                RejectGoalAction(window, text, snapshot, "Parameter feedback could not be represented safely.");
                return;
            }
            if (string.IsNullOrEmpty(prompt))
            {
                RejectGoalAction(window, text, snapshot, "Parameter feedback could not be represented safely.");
                return;
            }
            long stage = BeginConversationContextStage(snapshot, window);
            if (stage <= 0)
            {
                HandleRejectedConversationStage(snapshot, window, stage, text, "No action was accepted.");
                return;
            }
            Action<string, string, string> trace = (request, response, result) =>
            {
                if (!GoalTwoPassProbeEnabled &&
                    !(repair && Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-binding-probe") >= 0)) return;
                if (repair)
                {
                    goalTwoPassRepairPrompt = prompt;
                    goalTwoPassRepairRequest = request;
                    goalTwoPassRepairResponse = response;
                    goalTwoPassRepairOutput = result;
                }
                else
                {
                    goalTwoPassParameterPrompt = prompt;
                    goalTwoPassParameterRequest = request;
                    goalTwoPassParameterResponse = response;
                    goalTwoPassParameterOutput = result;
                }
            };

            if (localDialogue.Extract(NewDialogueConnection(), prompt, history,
                NpcGoalTwoPassChat.ParameterGrammar(decomposition), output =>
                {
                    if (!CurrentMemorySession(window, session) ||
                        !string.Equals(conversationPersonId, person, StringComparison.Ordinal)) return;
                    if (!IsCurrentGoalTwoPassRequest(window, text, snapshot, session, person) ||
                        !TryConsumeConversationContextStage(snapshot, window, stage))
                    {
                        HandleRejectedConversationStage(snapshot, window, stage, text, "No action was accepted.");
                        return;
                    }
                    if (repair) Increment(ref goalTwoPassRepairCompletionCount);
                    else Increment(ref goalTwoPassParameterCompletionCount);
                    if (output == LocalDialogueClient.Unavailable || output == LocalDialogueClient.ContextLimit)
                    {
                        goalLastExtractionStatus = "Unavailable";
                        RejectGoalAction(window, text, snapshot, repair
                            ? "Parameter repair service is unavailable; native validation still fails: " +
                                LimitParameterRepairReason(validationReason)
                            : "Parameter extraction service is unavailable.");
                        return;
                    }

                    attempts.Accept(output);
                    NpcGoalExtractionResult extraction = attempts.Result;
                    goalLastExtractionStatus = extraction.Status.ToString();
                    NpcGoalExtractionResult observed = repair ? null : NpcGoalChat.Parse(output,
                        decomposition.Binding, decomposition.OriginalText);
                    if (!repair && observed.Status == NpcGoalExtractionStatus.Valid && observed.Proposal != null &&
                        observed.Proposal.Intent != NpcGoalIntent.Request)
                    {
                        goalLastExtractionStatus = observed.Proposal.Intent.ToString();
                        RejectGoalAction(window, text, snapshot,
                            "Parameter pass returned explicit " + observed.Proposal.Intent + " intent.");
                        return;
                    }

                    if (attempts.CanRetry)
                    {
                        DispatchGoalActionParameterPass(window, text, history, snapshot, decomposition, session, person,
                            attempts);
                        return;
                    }
                    if (!attempts.Ready)
                    {
                        goalLastExtractionStatus = extraction.Status.ToString();
                        RejectGoalAction(window, text, snapshot,
                            "Parameter attempt budget exhausted: " + LimitParameterRepairReason(attempts.Reason));
                        return;
                    }
                    ContinueGoalActionExtraction(window, text, history, snapshot, output, extraction);
                }, 1536, trace))
            {
                if (repair) Increment(ref goalTwoPassRepairDispatchCount);
                else Increment(ref goalTwoPassParameterDispatchCount);
                return;
            }

            if (!TryConsumeConversationContextStage(snapshot, window, stage)) return;
            RejectGoalAction(window, text, snapshot, repair
                ? "Parameter repair could not start; native validation still fails: " +
                    LimitParameterRepairReason(validationReason)
                : "Parameter extraction could not start.");
        }

        private static string LimitParameterRepairReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "Parameter output failed native validation.";
            if (reason.Length <= 160) return reason;
            int length = 160;
            if (char.IsHighSurrogate(reason[length - 1]) && length < reason.Length && char.IsLowSurrogate(reason[length]))
                length--;
            return reason.Substring(0, length);
        }

        private bool IsCurrentGoalTwoPassRequest(DaggerfallTalkWindow window, string text,
            ConversationContextSnapshot snapshot, int session, string person)
        {
            return CurrentMemorySession(window, session) &&
                string.Equals(conversationPersonId, person, StringComparison.Ordinal) &&
                string.Equals(conversationContextCapturedPlayerText, text, StringComparison.Ordinal) &&
                IsCurrentConversationContext(snapshot, window);
        }

        private static void Increment(ref long counter)
        {
            if (counter < long.MaxValue) counter++;
        }
    }
}
