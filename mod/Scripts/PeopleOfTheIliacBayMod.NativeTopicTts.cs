using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private sealed class NativeTopicTtsRowBinding
        {
            public ListBox Transcript;
            public DaggerfallTalkWindow Talk;
            public string RequestId;
            public string ConversationToken;
            public string PersonId;
            public UnityEngine.Object Speaker;
            public StreamingWordHighlightOverlay Highlight;
        }

        private DaggerfallTalkWindow nativeTopicTtsWindow;
        private ListBox nativeTopicTtsTranscript;
        private UnityEngine.Object nativeTopicTtsSpeaker;
        private string nativeTopicTtsPersonId, nativeTopicTtsToken;
        private int nativeTopicTtsCursor;
        private string nativeTopicTtsObservedToken;
        private readonly HashSet<ListBox.ListItem> nativeTopicTtsObservedRows =
            new HashSet<ListBox.ListItem>();
        private readonly Dictionary<ListBox.ListItem, NativeTopicTtsRowBinding> nativeTopicTtsReplayByRow =
            new Dictionary<ListBox.ListItem, NativeTopicTtsRowBinding>();

        // AI-off conversations still get a short-lived, identity-bound TTS
        // session. It owns only its bridge token/cursor and does not restore or
        // rewrite the player's native transcript or conversation state.
        private void UpdateNativeTopicTtsSession(DaggerfallTalkWindow topTalk)
        {
            if (conversationSessionOpen && topTalk != null && memoryWindow == topTalk)
            {
                EndNativeTopicTtsSession();
                CaptureNativeDialogueMemory();
                return;
            }
            bool aiReplyPathOwnsTalk = localDialogueEnabled && localDialogue;
            if (aiReplyPathOwnsTalk)
            {
                EndNativeTopicTtsSession();
                return;
            }

            if (!breezeTtsTrial || topTalk == null || !topTalk.IsSetup ||
                DaggerfallUI.UIManager == null || DaggerfallUI.UIManager.TopWindow != topTalk)
            {
                if (nativeTopicTtsWindow != null && DaggerfallUI.UIManager != null &&
                    DaggerfallUI.UIManager.ContainsWindow(nativeTopicTtsWindow)) return;
                EndNativeTopicTtsSession();
                return;
            }

            TalkManager manager = GameManager.Instance == null ? null : GameManager.Instance.TalkManager;
            UnityEngine.Object speaker = CurrentConversationSpeakerObject();
            string personId = NativeTopicTtsPersonId(manager);
            ListBox transcript = conversationListField == null ? null : conversationListField.GetValue(topTalk) as ListBox;
            if (manager == null || !speaker || string.IsNullOrWhiteSpace(personId) || transcript == null)
            {
                EndNativeTopicTtsSession();
                return;
            }

            if (nativeTopicTtsWindow != topTalk || nativeTopicTtsTranscript != transcript ||
                !ReferenceEquals(nativeTopicTtsSpeaker, speaker) || nativeTopicTtsPersonId != personId)
            {
                EndNativeTopicTtsSession();
                nativeTopicTtsWindow = topTalk;
                nativeTopicTtsTranscript = transcript;
                nativeTopicTtsSpeaker = speaker;
                nativeTopicTtsPersonId = personId;
                nativeTopicTtsToken = Guid.NewGuid().ToString("N");
                nativeTopicTtsCursor = transcript.Count; // opening/restored rows are not topic-answer deltas.
                nativeTopicTtsObservedToken = nativeTopicTtsToken;
                nativeTopicTtsObservedRows.Clear();
                CaptureNativeOpeningTtsRow(topTalk, transcript, personId, nativeTopicTtsToken, speaker);
            }

            if (transcript.Count < nativeTopicTtsCursor)
            {
                // A reset or restored transcript is not new native speech.
                nativeTopicTtsCursor = transcript.Count;
                return;
            }
            CaptureNewNativeTopicAnswerRows(topTalk, transcript, nativeTopicTtsCursor, transcript.Count,
                personId, nativeTopicTtsToken, true);
            nativeTopicTtsCursor = transcript.Count;
        }

        private void CaptureNativeOpeningTtsRow(DaggerfallTalkWindow talk, ListBox transcript,
            string personId, string token, UnityEngine.Object speaker)
        {
            // Only a fresh, one-row vanilla opening transcript qualifies. A
            // restored conversation has history rows and is deliberately skipped.
            if (!breezeTtsTrial || talk == null || transcript == null || transcript.Count != 1 ||
                string.IsNullOrWhiteSpace(personId) || personId.StartsWith("quest-person:", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(token) || talk != nativeTopicTtsWindow ||
                transcript != nativeTopicTtsTranscript || token != nativeTopicTtsToken ||
                personId != nativeTopicTtsPersonId || !ReferenceEquals(speaker, nativeTopicTtsSpeaker) ||
                !ReferenceEquals(speaker, CurrentConversationSpeakerObject())) return;
            TalkManager manager = GameManager.Instance == null ? null : GameManager.Instance.TalkManager;
            if (manager == null) return;
            if (manager.CurrentNPCType == TalkManager.NPCType.Mobile && manager.MobileNPC)
            {
                ResidentActor actor = manager.MobileNPC.GetComponent<ResidentActor>();
                if (!actor || actor.Resident == null || actor.Resident.IsQuestProtected) return;
            }
            ListBox.ListItem row = FindNativeOpeningGreeting(transcript);
            if (row == null || row.textLabel == null || !nativeTopicTtsObservedRows.Add(row)) return;

            BreezeTtsTrialBridge.VoiceIdentity identity = CurrentTtsVoiceIdentityFor(personId);
            string voiceGender = identity == null || identity.sex == null || !identity.sex.known
                ? "unknown" : identity.sex.value;
            string requestId;
            if (!NotifyNpcSpeechWithPresentation(personId, manager.NameNPC, row.textLabel.Text,
                token, voiceGender, identity, out requestId)) return;
            AttachNativeTopicTtsReplayRow(talk, transcript, row, requestId, token, personId, speaker);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_OPENING_TTS_QUEUED: person=" + personId +
                "; session=" + token + "; source=fresh-native-opening-row; request=" + requestId);
        }

        private string NativeTopicTtsPersonId(TalkManager manager)
        {
            if (manager == null) return null;
            if (manager.CurrentNPCType == TalkManager.NPCType.Mobile && manager.MobileNPC)
            {
                ResidentActor actor = manager.MobileNPC.GetComponent<ResidentActor>();
                return actor && actor.Resident != null && actor.Resident.IsAlive ? actor.Resident.Id : null;
            }
            if (manager.CurrentNPCType != TalkManager.NPCType.Static || !manager.StaticNPC) return null;

            StaticNPC npc = manager.StaticNPC;
            QuestResourceBehaviour quest = npc.GetComponent<QuestResourceBehaviour>();
            if (quest && quest.QuestUID != 0 && quest.TargetSymbol != null)
                return "quest-person:" + quest.QuestUID + ":" + quest.TargetSymbol.Name;
            string castleKey;
            if (npc.Data.context != StaticNPC.Context.Building && !TryNativeCastleLayoutKey(npc,out castleKey)) return null;
            return StaticConversationPersonId(npc);
        }

        private void CaptureNewNativeTopicAnswerRows(DaggerfallTalkWindow talk, ListBox transcript,
            int firstNewRow, int endRow, string personId, string token, bool standaloneSession)
        {
            if (!breezeTtsTrial || talk == null || transcript == null || firstNewRow < 0 ||
                endRow > transcript.Count || endRow - firstNewRow < 2 || string.IsNullOrWhiteSpace(personId) ||
                string.IsNullOrWhiteSpace(token)) return;
            if (nativeTopicTtsObservedToken != token)
            {
                nativeTopicTtsObservedToken = token;
                nativeTopicTtsObservedRows.Clear();
            }

            int begin = Math.Max(0, firstNewRow);
            for (int index = begin; index + 1 < endRow; index++)
            {
                ListBox.ListItem question = transcript.GetItem(index);
                ListBox.ListItem answer = transcript.GetItem(index + 1);
                if (question == null || answer == null || question.textLabel == null || answer.textLabel == null ||
                    question.textColor != DaggerfallUI.DaggerfallQuestionTextColor ||
                    answer.textColor == DaggerfallUI.DaggerfallQuestionTextColor ||
                    string.IsNullOrWhiteSpace(question.textLabel.Text) || string.IsNullOrWhiteSpace(answer.textLabel.Text) ||
                    IsPendingTranscriptItem(question) || IsPendingTranscriptItem(answer) ||
                    question.textLabel.Name == UnavailableTranscriptRowName ||
                    answer.textLabel.Name == UnavailableTranscriptRowName ||
                    question.textLabel.Name == "PeopleOfTheIliacBayHistoryDay" ||
                    question.textLabel.Name == "PeopleOfTheIliacBayHistoryRule" ||
                    answer.textLabel.Name == "PeopleOfTheIliacBayHistoryDay" ||
                    answer.textLabel.Name == "PeopleOfTheIliacBayHistoryRule" ||
                    answer.textLabel.Name == "PeopleOfTheIliacBayReturnGreeting")
                    continue;
                if (!nativeTopicTtsObservedRows.Add(answer)) continue;

                bool bindingCurrent;
                if (standaloneSession)
                    bindingCurrent = nativeTopicTtsWindow == talk && nativeTopicTtsTranscript == transcript &&
                        nativeTopicTtsToken == token && nativeTopicTtsPersonId == personId &&
                        ReferenceEquals(nativeTopicTtsSpeaker, CurrentConversationSpeakerObject());
                else
                    bindingCurrent = conversationSessionOpen && memoryWindow == talk &&
                        conversationContextSessionId == token && memoryPersonId == personId &&
                        ReferenceEquals(conversationContextSessionSpeaker, CurrentConversationSpeakerObject());
                if (!bindingCurrent) continue;

                BreezeTtsTrialBridge.VoiceIdentity identity = CurrentTtsVoiceIdentityFor(personId);
                string voiceGender = identity == null || identity.sex == null || !identity.sex.known
                    ? "unknown" : identity.sex.value;
                string requestId;
                if (!NotifyNpcSpeechWithPresentation(personId, GameManager.Instance.TalkManager.NameNPC,
                    answer.textLabel.Text, token, voiceGender, identity, out requestId)) continue;

                if (standaloneSession)
                    AttachNativeTopicTtsReplayRow(talk, transcript, answer, requestId, token, personId,
                        nativeTopicTtsSpeaker);
                else
                    AttachTrialReplayToRows(talk, new[] { answer }, requestId, token);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_TOPIC_TTS_QUEUED: person=" + personId +
                    "; session=" + token + "; source=native-question-answer-row; request=" + requestId);
            }
        }

        private void AttachNativeTopicTtsReplayRow(DaggerfallTalkWindow talk, ListBox transcript,
            ListBox.ListItem row, string requestId, string token, string personId, UnityEngine.Object speaker)
        {
            if (talk == null || transcript == null || row == null || row.textLabel == null ||
                talk != nativeTopicTtsWindow || transcript != nativeTopicTtsTranscript ||
                token != nativeTopicTtsToken || personId != nativeTopicTtsPersonId ||
                !ReferenceEquals(speaker, nativeTopicTtsSpeaker)) return;
            for (int index = 0; index < transcript.Count; index++)
                if (ReferenceEquals(transcript.GetItem(index), row))
                {
                    string boundPerson, boundToken, boundText;
                    if (!breezeTtsTrial || !breezeTtsTrial.TryReadRequestBinding(requestId,
                        out boundPerson, out boundToken, out boundText) || boundPerson != personId ||
                        boundToken != token || boundText != row.textLabel.Text)
                    {
                        StreamingWordHighlightOverlay.ReportDiagnostic("binding-miss", requestId,
                            "native-topic-row-request-person-token-text-mismatch");
                        return;
                    }
                    NativeTopicTtsRowBinding previous;
                    if (nativeTopicTtsReplayByRow.TryGetValue(row, out previous) && previous != null &&
                        previous.Highlight != null)
                        previous.Highlight.Detach();
                    StreamingWordHighlightOverlay highlight = StreamingWordHighlightOverlay.AttachFullWav(
                        transcript, row, breezeTtsTrial, requestId, token, personId, boundText);
                    nativeTopicTtsReplayByRow[row] = new NativeTopicTtsRowBinding { Transcript = transcript,
                        Talk = talk, RequestId = requestId, ConversationToken = token, PersonId = personId,
                        Speaker = speaker, Highlight = highlight };
                    transcript.OnMouseClick -= OnNativeTopicTtsReplayClick;
                    transcript.OnMouseClick += OnNativeTopicTtsReplayClick;
                    return;
                }
        }

        private void OnNativeTopicTtsReplayClick(BaseScreenComponent sender, Vector2 clickPosition)
        {
            ListBox transcript = sender as ListBox;
            if (transcript == null) return;
            ListBox.ListItem clicked = FindClickedTranscriptRow(transcript, clickPosition);
            NativeTopicTtsRowBinding binding;
            if (clicked == null || !nativeTopicTtsReplayByRow.TryGetValue(clicked, out binding) || binding == null ||
                binding.Talk != nativeTopicTtsWindow || binding.Transcript != nativeTopicTtsTranscript ||
                binding.ConversationToken != nativeTopicTtsToken || binding.PersonId != nativeTopicTtsPersonId ||
                !ReferenceEquals(binding.Speaker, nativeTopicTtsSpeaker) ||
                !ReferenceEquals(binding.Speaker, CurrentConversationSpeakerObject()) || !breezeTtsTrial ||
                DaggerfallUI.UIManager == null || DaggerfallUI.UIManager.TopWindow != binding.Talk) return;
            StreamingWordHighlightOverlay.PrepareReplayForRow(clicked);
            BreezeTtsTrialBridge.RequestState state = breezeTtsTrial.ReplayRequest(binding.RequestId,
                binding.ConversationToken);
            if (state == BreezeTtsTrialBridge.RequestState.Pending)
                DaggerfallUI.AddHUDText("That line's voice is still being prepared. Replay will not start a new request.");
            else if (state == BreezeTtsTrialBridge.RequestState.Loading)
                DaggerfallUI.AddHUDText("Loading the recorded voice for replay.");
            else if (state == BreezeTtsTrialBridge.RequestState.Unknown ||
                state == BreezeTtsTrialBridge.RequestState.Cancelled ||
                state == BreezeTtsTrialBridge.RequestState.Failed)
                DaggerfallUI.AddHUDText("That line's recorded voice is unavailable.");
        }

        private void EndNativeTopicTtsSession()
        {
            ForgetNpcSpeechPresentation(nativeTopicTtsToken);
            if (nativeTopicTtsTranscript != null)
                nativeTopicTtsTranscript.OnMouseClick -= OnNativeTopicTtsReplayClick;
            foreach (NativeTopicTtsRowBinding binding in nativeTopicTtsReplayByRow.Values)
                if (binding != null && binding.Highlight != null) binding.Highlight.Detach();
            nativeTopicTtsReplayByRow.Clear();
            if (breezeTtsTrial && !string.IsNullOrEmpty(nativeTopicTtsToken))
                breezeTtsTrial.EndConversation(nativeTopicTtsToken);
            nativeTopicTtsWindow = null;
            nativeTopicTtsTranscript = null;
            nativeTopicTtsSpeaker = null;
            nativeTopicTtsPersonId = nativeTopicTtsToken = null;
            nativeTopicTtsCursor = 0;
            nativeTopicTtsObservedToken = null;
            nativeTopicTtsObservedRows.Clear();
        }
    }
}
