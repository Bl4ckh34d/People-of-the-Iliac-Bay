using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private const string ActionWordingSwitch = "-peopleoftheiliacbay-action-wording";
        private const string ActionWordingProbeSwitch = "-peopleoftheiliacbay-action-wording-probe";

        private sealed class PendingConversationActionWording
        {
            public DaggerfallTalkWindow Window;
            public ConversationContextSnapshot Snapshot;
            public ConversationActionReply Receipt;
            public string PlayerText;
            public string History;
            public string LastNpcText;
            public bool RephraseRequested;
            public int Session;
            public long SaveEpoch;
            public long StageToken;
            public bool MoodEnabled;
            public string MoodPersonId;
            public int MoodValue;
            public long MoodSequence;
            public long MoodRevision;
            public DialogueRequest MoodRequest;
            public NpcObservedSpeechTicket ObservedSpeechTicket;
            public NpcObservedSpeechReceipt ObservedSpeech;
            public bool AffinityAlreadyProcessed;
        }

        private PendingConversationActionWording pendingConversationActionWording;
        private ConversationContextBinding actionWordingReplyBinding;

        // Native probe can hold a real completion until close/save proves that
        // ownership was cleared before a deliberately late callback is released.
        private bool actionWordingProbeHoldCompletion = false;
        private Action actionWordingProbeReleaseCompletion;

        private bool SubmitConversationActionReply(DaggerfallTalkWindow window, string playerText,
            ConversationContextSnapshot snapshot, ConversationActionReply reply)
        {
            if (!HasCommandLineSwitch(ActionWordingSwitch) ||
                reply == null || reply.Status == ConversationActionReplyStatus.Unknown ||
                (!IsNoSourceWordingReceipt(reply) &&
                    reply.Source != ConversationActionReplySource.Action && reply.Source != ConversationActionReplySource.Task))
                return SubmitAuthoritativeActionReply(window, playerText, snapshot, reply);

            if (snapshot == null || snapshot.Binding == null || string.IsNullOrWhiteSpace(playerText) ||
                !string.Equals(playerText, conversationContextCapturedPlayerText, StringComparison.Ordinal) ||
                (!IsNoSourceWordingReceipt(reply) &&
                    !string.Equals(reply.PersonId, snapshot.Binding.PersonId, StringComparison.Ordinal)) ||
                pendingConversationActionWording != null ||
                actionWordingReplyBinding != null && actionWordingReplyBinding.HasSameRequestBinding(snapshot.Binding) ||
                conversationActionReplyBinding != null && conversationActionReplyBinding.HasSameRequestBinding(snapshot.Binding))
                return false;

            if (!IsCurrentConversationContext(snapshot, window) || conversationContextIssuedStageToken != 0)
            {
                HandleRejectedConversationStage(snapshot, window, 0, playerText, null);
                return false;
            }

            ConversationActionReply currentReceipt = reply;
            if (!IsNoSourceWordingReceipt(reply))
            {
                ConversationActionReply freshReceipt = ReadCurrentActionWordingReceipt(reply, snapshot.Binding.PersonId);
                if (freshReceipt == null)
                    return SubmitAuthoritativeActionReply(window, playerText, snapshot, ConversationActionReply.Unverified());
                if (!reply.MatchesSavedState(store)) currentReceipt = freshReceipt;
            }

            long stageToken = BeginConversationContextStage(snapshot, window);
            if (stageToken == 0)
                return SubmitAuthoritativeActionReply(window, playerText, snapshot, currentReceipt);

            string history = ReadConversationHistory(window);
            bool moodEnabled = snapshot.Binding.IdentityKind == ConversationIdentityKind.StablePerson &&
                snapshot.PersistentMood != null && !saveFault && store != null && memoryWindow == window &&
                !string.IsNullOrWhiteSpace(memoryPersonId) &&
                string.Equals(memoryPersonId, snapshot.Binding.PersonId, StringComparison.Ordinal) &&
                CurrentMemorySession(window, memorySession);
            var pending = new PendingConversationActionWording
            {
                Window = window,
                Snapshot = snapshot,
                Receipt = currentReceipt,
                PlayerText = playerText,
                History = history,
                LastNpcText = DialogueHistoryWindow.LastNpcText(history),
                Session = memorySession,
                SaveEpoch = snapshot.Binding.SaveEpoch,
                StageToken = stageToken,
                MoodEnabled = moodEnabled,
                MoodPersonId = moodEnabled ? memoryPersonId : null,
                MoodValue = moodEnabled ? snapshot.PersistentMood.Value : 0,
                MoodSequence = moodEnabled ? snapshot.PersistentMood.AcceptedTurnSequence : 0,
                MoodRevision = moodEnabled ? snapshot.PersistentMood.StateRevision : 0,
                ObservedSpeechTicket = conversationObservedSpeechTicket,
                ObservedSpeech = conversationObservedSpeech,
                AffinityAlreadyProcessed = conversationAffinityProcessedForTurn
            };

            // Claim request before model dispatch. Synchronous cache completion
            // and reentrant native callbacks then see the same single owner.
            pendingConversationActionWording = pending;
            actionWordingReplyBinding = snapshot.Binding;
            conversationActionReplyBinding = snapshot.Binding;
            actionWordingProbeReleaseCompletion = null;
            conversationBusy = true;
            if (!IsNoSourceWordingReceipt(currentReceipt))
            {
                string npcName;
                if (!snapshot.Name.TryGetVerified(out npcName)) npcName = "NPC";
                DaggerfallUI.AddHUDText(npcName + ": " + currentReceipt.NpcText);
            }

            bool dispatched = RequestActionWording(pending);
            if (HasCommandLineSwitch(ActionWordingProbeSwitch))
                UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_DISPATCH: accepted=" + dispatched);
            if (!dispatched) CompleteActionWording(pending, LocalDialogueClient.Unavailable);
            return true;
        }

        private bool RequestActionWording(PendingConversationActionWording pending)
        {
            if (pending == null || !ReferenceEquals(pending, pendingConversationActionWording) || localDialogue == null)
                return false;
            try
            {
                DialogueRequest request = new ConversationActionRenderRequest(pending.Snapshot, pending.PlayerText,
                    pending.Receipt, pending.History).ToDialogueRequest(
                        pending.RephraseRequested ? LocalDialogue.NormalizeContext(pending.LastNpcText,800) : null,
                        pending.MoodEnabled, pending.MoodValue);
                pending.MoodRequest = request;
                return localDialogue.Request(NewDialogueConnection(), request,
                    pending.MoodEnabled
                        ? ConversationActionMoodProbe.WrapCompletion(pending.Snapshot, request,
                            answer => OnActionWordingAnswer(pending, answer))
                        : answer => OnActionWordingAnswer(pending, answer));
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_UNAVAILABLE: " + exception.GetType().Name);
                return false;
            }
        }

        private static bool HasCommandLineSwitch(string value)
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), value) >= 0;
        }

        private static bool IsNoSourceWordingReceipt(ConversationActionReply reply)
        {
            return reply != null && reply.Source == ConversationActionReplySource.None &&
                (reply.Status == ConversationActionReplyStatus.NoChange ||
                 reply.Status == ConversationActionReplyStatus.Rejected);
        }

        private ConversationActionReply ReadCurrentActionWordingReceipt(ConversationActionReply requested, string personId)
        {
            if (requested == null || store == null || string.IsNullOrWhiteSpace(personId)) return null;
            ConversationActionReply currentReceipt = null;
            bool found = requested.Source == ConversationActionReplySource.Action
                ? ConversationActionReply.TryFromSavedAction(store, requested.SourceId, out currentReceipt)
                : requested.Source == ConversationActionReplySource.Task &&
                    ConversationActionReply.TryFromSavedTask(store, requested.SourceId, out currentReceipt);
            if (!found || currentReceipt == null || currentReceipt.Source != requested.Source ||
                !string.Equals(currentReceipt.SourceId, requested.SourceId, StringComparison.Ordinal) ||
                !string.Equals(currentReceipt.PersonId, personId, StringComparison.Ordinal)) return null;
            return currentReceipt;
        }

        private void OnActionWordingAnswer(PendingConversationActionWording pending, string answer)
        {
            if (pending == null || !ReferenceEquals(pending, pendingConversationActionWording)) return;
            if (HasCommandLineSwitch(ActionWordingProbeSwitch) && actionWordingProbeHoldCompletion)
            {
                if (actionWordingProbeReleaseCompletion == null)
                {
                    Action release = null;
                    release = () =>
                    {
                        if (ReferenceEquals(actionWordingProbeReleaseCompletion, release))
                            actionWordingProbeReleaseCompletion = null;
                        CompleteActionWording(pending, answer);
                    };
                    actionWordingProbeReleaseCompletion = release;
                }
                return;
            }
            CompleteActionWording(pending, answer);
        }

        private void CompleteActionWording(PendingConversationActionWording pending, string answer)
        {
            if (pending == null || !ReferenceEquals(pending, pendingConversationActionWording)) return;
            if (!CurrentMemorySession(pending.Window, pending.Session) ||
                pending.SaveEpoch != conversationSaveEpoch || pending.StageToken != conversationContextIssuedStageToken ||
                !IsCurrentConversationContext(pending.Snapshot, pending.Window) ||
                !string.Equals(pending.PlayerText, conversationContextCapturedPlayerText, StringComparison.Ordinal))
            {
                // If this still belongs to the same memory session, commit its
                // canonical turn before clearing ownership. Epoch/session changes
                // make the lifecycle helper discard it instead.
                FlushPendingActionWordingBeforeMemoryCapture(true);
                return;
            }

            ConversationActionReply currentReceipt = IsNoSourceWordingReceipt(pending.Receipt)
                ? pending.Receipt
                : ReadCurrentActionWordingReceipt(pending.Receipt, pending.Snapshot.Binding.PersonId);
            bool unchangedReceipt = IsNoSourceWordingReceipt(pending.Receipt) ||
                currentReceipt != null && pending.Receipt.MatchesSavedState(store);
            bool answerAvailable = !string.IsNullOrWhiteSpace(answer) &&
                answer != LocalDialogueClient.Unavailable && answer != LocalDialogueClient.ContextLimit;
            SocialDialogueResult moodResult = null;
            bool typedMoodValid = !pending.MoodEnabled;
            if (pending.MoodEnabled && answerAvailable)
            {
                string selectedReply = null;
                typedMoodValid = SocialDialogueResult.TryParse(answer, out moodResult) &&
                    pending.MoodRequest != null &&
                    LocalDialogue.TrySelectAnswer(pending.MoodRequest, moodResult == null ? null : moodResult.Reply,
                        out selectedReply);
                if (typedMoodValid)
                {
                    answer = selectedReply;
                    if (string.Equals(answer, LocalDialogueClient.Unavailable, StringComparison.Ordinal) ||
                        string.Equals(answer, LocalDialogueClient.ContextLimit, StringComparison.Ordinal))
                        answerAvailable = false;
                }
                else answerAvailable = false;
            }
            bool duplicateAnswer = answerAvailable && DialogueHistoryWindow.IsDuplicateReply(pending.LastNpcText, answer);
            if (duplicateAnswer && unchangedReceipt && !pending.RephraseRequested)
            {
                pending.RephraseRequested = true;
                if (RequestActionWording(pending)) return;
            }

            string response;
            string responseSource = "action";
            if (!answerAvailable || duplicateAnswer)
            {
                response = ActionWordingUnavailableText();
                responseSource = "unavailable";
            }
            else if (unchangedReceipt)
                response = answer.Trim();
            else
            {
                response = currentReceipt == null
                    ? ConversationActionReply.Unverified().NpcText
                    : currentReceipt.NpcText;
                if (DialogueHistoryWindow.IsDuplicateReply(pending.LastNpcText, response))
                {
                    response = ActionWordingUnavailableText();
                    responseSource = "unavailable";
                }
            }

            if (!TryConsumeConversationContextStage(pending.Snapshot, pending.Window, pending.StageToken))
            {
                FlushPendingActionWordingBeforeMemoryCapture(true);
                return;
            }
            bool committedMood = false;
            bool acceptedMoodCandidate = pending.MoodEnabled && moodResult != null && answerAvailable &&
                !duplicateAnswer && unchangedReceipt && responseSource == "action";
            if (acceptedMoodCandidate)
            {
                long committedSequence;
                bool currentMoodOwner = appendConversationPair != null && !saveFault && store != null && memoryWindow == pending.Window &&
                    CurrentMemorySession(pending.Window, pending.Session) &&
                    string.Equals(memoryPersonId, pending.MoodPersonId, StringComparison.Ordinal) &&
                    pending.Snapshot.Binding.IdentityKind == ConversationIdentityKind.StablePerson &&
                    string.Equals(pending.Snapshot.Binding.PersonId, pending.MoodPersonId, StringComparison.Ordinal);
                double committedAt = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                NpcExperienceAppliedEffectsMask mask = NpcExperienceAppliedEffectsMask.Mood;
                if (pending.AffinityAlreadyProcessed) mask |= NpcExperienceAppliedEffectsMask.Affinity;
                committedMood = currentMoodOwner && (pending.ObservedSpeech == null
                    ? store.TryCommitAcceptedActionExchange(pending.MoodPersonId, pending.MoodSequence,
                        pending.MoodRevision, moodResult.MoodDelta, committedAt, memoryDate,
                        ConversationMemoryPlace(), pending.PlayerText, response, out committedSequence)
                    : store.TryCommitAcceptedActionExchangeWithObservedSpeech(pending.MoodPersonId,
                        pending.MoodSequence, pending.MoodRevision, moodResult.MoodDelta, committedAt,
                        memoryDate, ConversationMemoryPlace(), pending.PlayerText, response,
                        pending.ObservedSpeechTicket, pending.ObservedSpeech, mask, out committedSequence));
                if (!currentMoodOwner) committedSequence = 0;
                if (!committedMood)
                {
                    // A concurrent accepted turn won the sequence race. Keep this turn visible
                    // using the authoritative receipt wording, never the stale generated text.
                    response = currentReceipt == null ? ConversationActionReply.Unverified().NpcText : currentReceipt.NpcText;
                    responseSource = "action";
                    if (DialogueHistoryWindow.IsDuplicateReply(pending.LastNpcText, response))
                    {
                        response = ActionWordingUnavailableText();
                        responseSource = "unavailable";
                    }
                }
            }
            pendingConversationActionWording = null;
            conversationBusy = false;
            if (appendConversationPair == null)
            {
                conversationContextRejectedRequestRevision = pending.Snapshot.Binding.RequestRevision;
                AppendRejectedPlayerTextOnly(pending.Window, pending.PlayerText);
                UnityEngine.Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_ACTION_REPLY_UNAVAILABLE: native dialogue append method is unavailable.");
                return;
            }
            if (committedMood && pending.ObservedSpeech != null &&
                ReferenceEquals(conversationObservedSpeech, pending.ObservedSpeech))
            {
                conversationObservedSpeech = null;
                conversationObservedSpeechTicket = null;
                conversationAffinityProcessedForTurn = false;
            }
            if (committedMood)
            {
                if (!TryDeferAcceptedSpeechAppend(pending.Window, pending.Snapshot, pending.PlayerText, response,
                    () => AppendUnrecordedDialoguePairWithSpeech(pending.Window, pending.PlayerText, response,
                        true, null, null, NpcExperienceAppliedEffectsMask.None)))
                    AppendUnrecordedDialoguePairWithSpeech(pending.Window, pending.PlayerText, response,
                        true, null, null, NpcExperienceAppliedEffectsMask.None);
            }
            else
                AppendRememberedDialoguePairWithSpeech(pending.Window, pending.PlayerText, response, responseSource,
                    pending.ObservedSpeechTicket, pending.ObservedSpeech,
                    pending.AffinityAlreadyProcessed ? NpcExperienceAppliedEffectsMask.Affinity : NpcExperienceAppliedEffectsMask.None);
        }

        private static string ActionWordingUnavailableText()
        {
            return "No new reply was received.";
        }

        private void FlushPendingActionWordingBeforeMemoryCapture(bool capture)
        {
            PendingConversationActionWording pending = pendingConversationActionWording;
            if (pending == null) return;
            if (!capture)
            {
                pendingConversationActionWording = null;
                if (conversationContextIssuedStageToken == pending.StageToken) conversationContextIssuedStageToken = 0;
                actionWordingProbeReleaseCompletion = null;
                conversationBusy = false;
                return;
            }

            bool sameMemoryOwner = pending.Window != null && memoryWindow == pending.Window &&
                memorySession == pending.Session && memoryPersonId != null && pending.Snapshot != null &&
                pending.Snapshot.Binding != null &&
                string.Equals(memoryPersonId, pending.Snapshot.Binding.PersonId, StringComparison.Ordinal) &&
                pending.SaveEpoch == conversationSaveEpoch && !saveFault && store != null;
            if (!sameMemoryOwner)
            {
                pendingConversationActionWording = null;
                if (conversationContextIssuedStageToken == pending.StageToken) conversationContextIssuedStageToken = 0;
                actionWordingProbeReleaseCompletion = null;
                conversationBusy = false;
                return;
            }

            ConversationActionReply currentReceipt = IsNoSourceWordingReceipt(pending.Receipt)
                ? pending.Receipt
                : ReadCurrentActionWordingReceipt(pending.Receipt, pending.Snapshot.Binding.PersonId);
            bool unchangedReceipt = IsNoSourceWordingReceipt(pending.Receipt) ||
                currentReceipt != null && pending.Receipt.MatchesSavedState(store);
            string fallback = currentReceipt == null ? ConversationActionReply.Unverified().NpcText
                : unchangedReceipt ? pending.Receipt.NpcText : currentReceipt.NpcText;
            string fallbackSource = "action";
            if (DialogueHistoryWindow.IsDuplicateReply(pending.LastNpcText, fallback))
            {
                fallback = ActionWordingUnavailableText();
                fallbackSource = "unavailable";
            }

            // Clear ownership before native transcript/archive append. A held
            // callback released after this point is a no-op.
            pendingConversationActionWording = null;
            if (conversationContextIssuedStageToken == pending.StageToken) conversationContextIssuedStageToken = 0;
            conversationBusy = false;
            if (appendConversationPair == null)
            {
                AppendRejectedPlayerTextOnly(pending.Window, pending.PlayerText);
                UnityEngine.Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_ACTION_REPLY_UNAVAILABLE: native dialogue append method is unavailable.");
                return;
            }
            AppendRememberedDialoguePairWithSpeech(pending.Window, pending.PlayerText, fallback, fallbackSource,
                pending.ObservedSpeechTicket, pending.ObservedSpeech,
                pending.AffinityAlreadyProcessed ? NpcExperienceAppliedEffectsMask.Affinity : NpcExperienceAppliedEffectsMask.None);
        }
    }
}
