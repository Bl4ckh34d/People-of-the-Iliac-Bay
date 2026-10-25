using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private sealed class StreamingTranscriptBinding
        {
            public ListBox Transcript;
            public DaggerfallTalkWindow Talk;
            public string StreamId;
            public string ConversationToken;
            public string PersonId;
        }

        private sealed class StreamingSocialReplySession
        {
            public string StreamId;
            public string ConversationToken;
            public string PersonId;
            public string PlayerText;
            public DaggerfallTalkWindow Talk;
            public ConversationContextSnapshot Snapshot;
            public int MemorySession;
            public long ReplyStage;
            public Action<string> HandleAnswer;
            public Action<BreezeTtsTrialBridge.MixerPlaybackStartReceipt> MixerStartedHandler;
            public string PendingValidatedSocialJson;
            public bool HasFirstMixerReceipt;
            public bool PresentationResolved;
            public int NextSentenceSequence;
            public string PreviousNpcReply;
            public readonly List<string> ReceivedSegments = new List<string>();
            public readonly List<string> HeldPrefixSegments = new List<string>();
            public readonly List<string> AcceptedSentences = new List<string>();
            public string ResponseRoot;
            public bool GenerationComplete;
            public bool Closing;
            public bool StageConsumed;
            public bool PartialTranscriptRetained;
            public bool Finished;
        }

        private BreezeTtsStreamingPcmAdapter socialPcmAdapter;
        private StreamingSocialReplySession streamingSocialReply;
        private bool suppressFullWavTtsForNextAppend;
        private string streamingCleanupToken;
        private readonly Dictionary<ListBox.ListItem, StreamingTranscriptBinding> streamingReplayByRow =
            new Dictionary<ListBox.ListItem, StreamingTranscriptBinding>();
        private readonly HashSet<ListBox> streamingReplayTranscripts = new HashSet<ListBox>();

        private bool TryBeginStreamingSocialReply(DaggerfallTalkWindow talk, string playerText,
            ConversationContextSnapshot snapshot, long replyStage, Action<string> handleAnswer,
            out StreamingSocialReplySession session)
        {
            session = null;
            // Selected read evidence must survive complete generation before any speech is published.
            if (HasSelectedConversationInventoryRead(snapshot) || HasSelectedConversationMemoryRead(snapshot)) return false;
            if (!breezeTtsTrial || !breezeTtsTrial.SocialStreamingEnabled || !moodStreamingEligible(
                talk, playerText, snapshot) || string.IsNullOrWhiteSpace(conversationContextSessionId) ||
                string.IsNullOrWhiteSpace(memoryPersonId) || !string.Equals(snapshot.Binding.PersonId,
                    memoryPersonId, StringComparison.Ordinal)) return false;

            string streamId;
            if (!breezeTtsTrial.TryBeginStreamingReply(memoryPersonId,
                GameManager.Instance.TalkManager.NameNPC, conversationContextSessionId,
                CurrentTrialVoiceGender(), CurrentTrialDeliveryContext(), CurrentTtsVoiceIdentity(), out streamId))
                return false;

            string responseRoot;
            if (string.IsNullOrEmpty(streamId) || !breezeTtsTrial.TryGetStreamingResponseRoot(out responseRoot))
            {
                breezeTtsTrial.CancelStreamingReply(streamId, conversationContextSessionId, memoryPersonId,
                    "response-root-unavailable");
                return false;
            }

            if (socialPcmAdapter == null) socialPcmAdapter = gameObject.GetComponent<BreezeTtsStreamingPcmAdapter>();
            if (socialPcmAdapter == null) socialPcmAdapter = gameObject.AddComponent<BreezeTtsStreamingPcmAdapter>();
            socialPcmAdapter.PlaybackFinished -= OnStreamingSocialPlaybackFinished;
            if (!socialPcmAdapter.Begin(breezeTtsTrial, responseRoot, streamId,
                conversationContextSessionId, memoryPersonId))
            {
                breezeTtsTrial.CancelStreamingReply(streamId, conversationContextSessionId, memoryPersonId,
                    "pcm-adapter-unavailable");
                return false;
            }

            session = new StreamingSocialReplySession { StreamId = streamId,
                ConversationToken = conversationContextSessionId, PersonId = memoryPersonId,
                PlayerText = playerText, Talk = talk, Snapshot = snapshot, MemorySession = memorySession,
                ReplyStage = replyStage, HandleAnswer = handleAnswer, ResponseRoot = responseRoot,
                PreviousNpcReply = DialogueHistoryWindow.LastNpcText(ReadConversationHistory(talk)) };
            StreamingSocialReplySession capturedSession = session;
            session.MixerStartedHandler = receipt => OnStreamingSocialMixerStarted(capturedSession, receipt);
            breezeTtsTrial.MixerPlaybackStarted += session.MixerStartedHandler;
            streamingSocialReply = session;
            socialPcmAdapter.PlaybackFinished += OnStreamingSocialPlaybackFinished;
            return true;
        }

        private bool moodStreamingEligible(DaggerfallTalkWindow talk, string playerText,
            ConversationContextSnapshot snapshot)
        {
            return localDialogueEnabled && localDialogue != null && talk != null && memoryWindow == talk &&
                !string.IsNullOrWhiteSpace(playerText) && snapshot != null && snapshot.PersistentMood != null &&
                !saveFault && store != null && !string.IsNullOrEmpty(memoryPersonId) &&
                string.Equals(snapshot.Binding.PersonId, memoryPersonId, StringComparison.Ordinal);
        }

        private bool AcceptStreamingSocialSentence(StreamingSocialReplySession session, string exactSentence)
        {
            if (!IsCurrentStreamingSession(session) || socialPcmAdapter == null ||
                session.ReceivedSegments.Count >= 8 || string.IsNullOrWhiteSpace(exactSentence)) return false;

            session.ReceivedSegments.Add(exactSentence);
            string cumulativeText = string.Join(" ", session.ReceivedSegments.ToArray());
            if (DialogueHistoryWindow.IsPotentialDuplicatePrefix(session.PreviousNpcReply, cumulativeText))
            {
                session.HeldPrefixSegments.Add(exactSentence);
                return true;
            }

            if (!FlushHeldStreamingSocialSegments(session) || !QueueStreamingSocialSentence(session, exactSentence))
                return false;
            return true;
        }

        private bool QueueStreamingSocialSentence(StreamingSocialReplySession session, string exactSentence)
        {
            if (!IsCurrentStreamingSession(session) || socialPcmAdapter == null ||
                session.NextSentenceSequence >= 8 || string.IsNullOrWhiteSpace(exactSentence) ||
                !socialPcmAdapter.TryQueueSentence(session.NextSentenceSequence, exactSentence)) return false;

            int sequence = session.NextSentenceSequence;
            if (!breezeTtsTrial.TryAppendStreamingSentence(session.StreamId, session.ConversationToken,
                session.PersonId, sequence, exactSentence))
            {
                socialPcmAdapter.CancelFromOwner("sentence-write-rejected");
                return false;
            }
            session.NextSentenceSequence++;
            session.AcceptedSentences.Add(exactSentence);
            return true;
        }

        private bool FlushHeldStreamingSocialSegments(StreamingSocialReplySession session)
        {
            if (session == null || session.HeldPrefixSegments.Count == 0) return true;
            for (int index = 0; index < session.HeldPrefixSegments.Count; index++)
                if (!QueueStreamingSocialSentence(session, session.HeldPrefixSegments[index])) return false;
            session.HeldPrefixSegments.Clear();
            return true;
        }

        private void CompleteStreamingSocialGeneration(StreamingSocialReplySession session,
            SocialDialogueStreamResult result)
        {
            if (!IsCurrentStreamingSession(session)) return;
            string socialJson = result == null ? null : result.ToSocialDialogueJson();
            bool emittedSegmentsMatch = result != null && result.EmittedSegments != null &&
                session.ReceivedSegments.Count == result.EmittedSegments.Count;
            if (emittedSegmentsMatch)
                for (int index = 0; index < session.ReceivedSegments.Count; index++)
                    if (!string.Equals(session.ReceivedSegments[index], result.EmittedSegments[index], StringComparison.Ordinal))
                    { emittedSegmentsMatch = false; break; }
            bool completeMatchesText = result != null && result.Complete && !string.IsNullOrEmpty(socialJson) &&
                string.Equals(result.Reply, result.EmittedText, StringComparison.Ordinal) &&
                emittedSegmentsMatch;
            if (completeMatchesText && DialogueHistoryWindow.IsDuplicateReply(session.PreviousNpcReply, result.Reply))
            {
                Action<string> handleAnswer = session.HandleAnswer;
                AbandonStreamingSocialReply(session, "exact-reply-duplicate");
                if (handleAnswer != null) handleAnswer(socialJson);
                return;
            }
            if (completeMatchesText && !FlushHeldStreamingSocialSegments(session))
                completeMatchesText = false;
            if (completeMatchesText && breezeTtsTrial.CompleteStreamingReply(session.StreamId,
                session.ConversationToken, session.PersonId, result.EmittedText))
            {
                session.GenerationComplete = true;
                session.PendingValidatedSocialJson = socialJson;
                if (session.HasFirstMixerReceipt) PresentValidatedStreamingAnswer(session);
                if (CurrentMemorySession(session.Talk, session.MemorySession) && !session.PresentationResolved &&
                    socialPcmAdapter != null && socialPcmAdapter.State != BreezeTtsStreamingPcmAdapter.PlaybackState.Completed)
                    conversationBusy = true;
                return;
            }

            string reason = result == null || string.IsNullOrEmpty(result.FailureKind) ? "invalid-stream-result" : result.FailureKind;
            string heard = socialPcmAdapter == null ? string.Empty : socialPcmAdapter.CompletedSentenceText;
            breezeTtsTrial.FailStreamingReply(session.StreamId, session.ConversationToken, session.PersonId,
                heard, reason);
            if (socialPcmAdapter != null)
                socialPcmAdapter.MarkTerminal("failed", reason);
            else
                HandleStreamingSocialFailureWithoutPlayback(session);
        }

        private bool IsCurrentStreamingSession(StreamingSocialReplySession session)
        {
            return session != null && !session.Finished && ReferenceEquals(streamingSocialReply, session) &&
                session.ConversationToken == conversationContextSessionId && session.PersonId == memoryPersonId &&
                CurrentMemorySession(session.Talk, session.MemorySession);
        }

        private void OnStreamingSocialPlaybackFinished(BreezeTtsStreamingPcmAdapter.PlaybackReceipt receipt)
        {
            StreamingSocialReplySession session = streamingSocialReply;
            if (session == null || receipt == null || receipt.StreamId != session.StreamId ||
                receipt.ConversationToken != session.ConversationToken || receipt.PersonId != session.PersonId) return;
            if (!session.GenerationComplete) FinishIncompleteStreamingSocialReply(session, receipt);
            else if (!session.PresentationResolved && !string.IsNullOrWhiteSpace(session.PendingValidatedSocialJson))
                PresentValidatedStreamingAnswer(session);
            RemoveStreamingMixerStartedHandler(session);
            session.Finished = true;
            if (ReferenceEquals(streamingSocialReply, session)) streamingSocialReply = null;
        }

        private void OnStreamingSocialMixerStarted(StreamingSocialReplySession session,
            BreezeTtsTrialBridge.MixerPlaybackStartReceipt receipt)
        {
            if (!IsCurrentStreamingSession(session) || receipt == null || receipt.RequestId != session.StreamId ||
                receipt.ConversationToken != session.ConversationToken || receipt.PersonId != session.PersonId ||
                !string.Equals(receipt.ExactText, string.Join(" ", session.AcceptedSentences), StringComparison.Ordinal)) return;
            session.HasFirstMixerReceipt = true;
            if (session.GenerationComplete) PresentValidatedStreamingAnswer(session);
        }

        private void PresentValidatedStreamingAnswer(StreamingSocialReplySession session)
        {
            if (!IsCurrentStreamingSession(session) || session.Closing || session.PresentationResolved ||
                string.IsNullOrWhiteSpace(session.PendingValidatedSocialJson) || session.HandleAnswer == null) return;
            session.PresentationResolved = true;
            suppressFullWavTtsForNextAppend = true;
            try { session.HandleAnswer(session.PendingValidatedSocialJson); }
            finally { suppressFullWavTtsForNextAppend = false; }
        }

        private void RemoveStreamingMixerStartedHandler(StreamingSocialReplySession session)
        {
            if (session != null && breezeTtsTrial != null && session.MixerStartedHandler != null)
            {
                breezeTtsTrial.MixerPlaybackStarted -= session.MixerStartedHandler;
                session.MixerStartedHandler = null;
            }
        }

        private void FinishIncompleteStreamingSocialReply(StreamingSocialReplySession session,
            BreezeTtsStreamingPcmAdapter.PlaybackReceipt receipt)
        {
            if (session == null || receipt == null || session.StageConsumed) return;
            bool currentMemory = CurrentMemorySession(session.Talk, session.MemorySession);
            if (currentMemory && !session.Closing && session.Snapshot != null &&
                !IsCurrentConversationContext(session.Snapshot, session.Talk)) currentMemory = false;

            string heard = receipt.CompletedSentenceText ?? string.Empty;
            if (currentMemory && heard.Length > 0)
            {
                if (!session.Closing && session.Snapshot != null)
                {
                    if (!TryConsumeConversationContextStage(session.Snapshot, session.Talk, session.ReplyStage))
                        currentMemory = false;
                    else
                    {
                        session.StageConsumed = true;
                        conversationBusy = false;
                    }
                }
            }
            if (currentMemory && heard.Length > 0)
            {
                bool interruptedMidSentence = receipt.InterruptedMidSentence;
                string displayed = interruptedMidSentence
                    ? heard + " [Interrupted mid-sentence; only complete spoken sentences were retained.]"
                    : heard;
                bool pairAppended = AppendPartialHeardDialoguePair(session.Talk, session.PlayerText, displayed);
                if (pairAppended)
                {
                    RememberDialogueExchange(session.PlayerText, displayed,
                        interruptedMidSentence ? "interrupted-audio" : "stream-audio-partial");
                    session.PartialTranscriptRetained = !saveFault && store != null &&
                        memoryWindow == session.Talk && !string.IsNullOrEmpty(memoryPersonId) &&
                        string.Equals(memoryPersonId, session.PersonId, StringComparison.Ordinal);
                }
            }
            else if (currentMemory && !session.Closing && session.HandleAnswer != null)
            {
                session.HandleAnswer(LocalDialogueClient.Unavailable);
            }
        }

        private void HandleStreamingSocialFailureWithoutPlayback(StreamingSocialReplySession session)
        {
            if (session != null && !session.GenerationComplete && session.HandleAnswer != null)
                session.HandleAnswer(LocalDialogueClient.Unavailable);
        }

        private bool AppendPartialHeardDialoguePair(DaggerfallTalkWindow talk, string question, string answer)
        {
            if (talk == null || appendConversationPair == null || conversationListField == null) return false;
            if (conversationPendingOverlayWindow == talk) RemoveConversationPendingOverlay();
            CaptureNativeDialogueMemory();
            var beforeTranscript = conversationListField.GetValue(talk) as ListBox;
            int beforeCount = beforeTranscript == null ? 0 : beforeTranscript.Count;
            var priorOffer=pendingConversationAction;
            bool retainOffer=priorOffer!=null && memoryWindow==talk &&
                pendingConversationActionHistory==ReadConversationHistory(talk);
            appendConversationPair.Invoke(talk, new object[] { question, answer });
            var transcript = conversationListField.GetValue(talk) as ListBox;
            if (transcript == null || transcript.Count <= beforeCount) return false;
            if(retainOffer && ReferenceEquals(priorOffer,pendingConversationAction))
                pendingConversationActionHistory=ReadConversationHistory(talk);
            conversationLastTranscriptCount = transcript.Count;
            if (memoryWindow == talk) memoryCursor = transcript.Count;
            return true;
        }

        private void AttachStreamingReplayToAppendedRows(DaggerfallTalkWindow talk, int firstNewRow,
            string streamId, string token, string personId)
        {
            if (talk == null || firstNewRow < 0 || string.IsNullOrEmpty(streamId) ||
                token != conversationContextSessionId || personId != memoryPersonId) return;
            ListBox transcript = conversationListField == null ? null : conversationListField.GetValue(talk) as ListBox;
            if (transcript == null || firstNewRow >= transcript.Count) return;
            var binding = new StreamingTranscriptBinding { Transcript = transcript, Talk = talk, StreamId = streamId,
                ConversationToken = token, PersonId = personId };
            StreamingSocialReplySession session = streamingSocialReply;
            bool attached = false;
            string exactReply = session != null && session.StreamId == streamId && session.AcceptedSentences != null
                ? string.Join(" ", session.AcceptedSentences) : null;
            for (int index = firstNewRow; index < transcript.Count; index++)
            {
                ListBox.ListItem row = transcript.GetItem(index);
                if (row == null || row.textLabel == null || IsPendingTranscriptItem(row) ||
                    row.textColor == DaggerfallUI.DaggerfallQuestionTextColor ||
                    string.IsNullOrWhiteSpace(row.textLabel.Text) || string.IsNullOrEmpty(exactReply) ||
                    !string.Equals(row.textLabel.Text, exactReply, StringComparison.Ordinal)) continue;
                streamingReplayByRow[row] = binding;
                RegisterCanonicalTranscriptText(row, exactReply);
                if (session != null && session.StreamId == streamId && socialPcmAdapter != null &&
                    !string.IsNullOrEmpty(session.ResponseRoot))
                    StreamingWordHighlightOverlay.Attach(transcript, row, socialPcmAdapter,
                        session.ResponseRoot, streamId, token, personId, session.AcceptedSentences, breezeTtsTrial,
                        initialReveal: true);
                attached = true;
            }
            if (attached && streamingReplayTranscripts.Add(transcript))
                transcript.OnMouseClick += OnStreamingReplayMouseClick;
        }

        private void OnStreamingReplayMouseClick(BaseScreenComponent sender, Vector2 clickPosition)
        {
            ListBox transcript = sender as ListBox;
            if (transcript == null) return;
            ListBox.ListItem row = FindClickedTranscriptRow(transcript, clickPosition);
            StreamingTranscriptBinding binding;
            if (row == null || !streamingReplayByRow.TryGetValue(row, out binding) || binding == null ||
                binding.Talk != memoryWindow || binding.ConversationToken != conversationContextSessionId ||
                binding.PersonId != memoryPersonId || !DaggerfallUI.UIManager.ContainsWindow(binding.Talk) ||
                !breezeTtsTrial) return;
            StreamingWordHighlightOverlay.PrepareReplayForRow(row);
            BreezeTtsTrialBridge.RequestState state = breezeTtsTrial.ReplayStreamingReply(
                binding.StreamId, binding.ConversationToken, binding.PersonId);
            if (state == BreezeTtsTrialBridge.RequestState.Loading)
                DaggerfallUI.AddHUDText("The recorded voice is still playing or being prepared. Replay will not start a new request.");
            else if (state == BreezeTtsTrialBridge.RequestState.Unknown || state == BreezeTtsTrialBridge.RequestState.Failed ||
                state == BreezeTtsTrialBridge.RequestState.Cancelled)
                DaggerfallUI.AddHUDText("That line's recorded voice is unavailable.");
        }

        private void EndStreamingReplayForConversation(string conversationToken)
        {
            if (string.IsNullOrEmpty(conversationToken)) return;
            var removeRows = new List<ListBox.ListItem>();
            foreach (var pair in streamingReplayByRow)
                if (pair.Value != null && pair.Value.ConversationToken == conversationToken)
                    removeRows.Add(pair.Key);
            for (int index = 0; index < removeRows.Count; index++)
            {
                streamingReplayByRow.Remove(removeRows[index]);
                canonicalNpcTextByRow.Remove(removeRows[index]);
            }
            var removeTranscripts = new List<ListBox>();
            foreach (ListBox transcript in streamingReplayTranscripts)
            {
                bool stillUsed = false;
                foreach (var pair in streamingReplayByRow)
                    if (pair.Value != null && ReferenceEquals(pair.Value.Transcript, transcript))
                    { stillUsed = true; break; }
                if (!stillUsed) removeTranscripts.Add(transcript);
            }
            for (int index = 0; index < removeTranscripts.Count; index++)
            {
                removeTranscripts[index].OnMouseClick -= OnStreamingReplayMouseClick;
                streamingReplayTranscripts.Remove(removeTranscripts[index]);
            }
            if (socialPcmAdapter != null) socialPcmAdapter.CleanupConversationResponses(conversationToken);
        }

        private void FinishStreamingSocialReplyBeforeMemoryCapture()
        {
            StreamingSocialReplySession session = streamingSocialReply;
            string closingToken = conversationContextSessionId;
            streamingCleanupToken = closingToken;
            if (session != null && !session.Finished)
            {
                session.Closing = true;
                RemoveStreamingMixerStartedHandler(session);
                if (socialPcmAdapter != null && socialPcmAdapter.StreamId == session.StreamId)
                    socialPcmAdapter.CancelFromOwner("conversation-ended");
                else if (breezeTtsTrial)
                    breezeTtsTrial.CancelStreamingReply(session.StreamId, session.ConversationToken,
                        session.PersonId, "conversation-ended");
            }
        }

        private void CleanupClosedStreamingConversation()
        {
            string token = streamingCleanupToken;
            streamingCleanupToken = null;
            EndStreamingReplayForConversation(token);
        }

        private void AbandonStreamingSocialReply(StreamingSocialReplySession session, string reason)
        {
            if (session == null) return;
            RemoveStreamingMixerStartedHandler(session);
            session.Finished = true;
            if (ReferenceEquals(streamingSocialReply, session)) streamingSocialReply = null;
            if (socialPcmAdapter != null && socialPcmAdapter.StreamId == session.StreamId)
                socialPcmAdapter.CancelFromOwner(reason);
            else if (breezeTtsTrial)
                breezeTtsTrial.CancelStreamingReply(session.StreamId, session.ConversationToken,
                    session.PersonId, reason);
        }
    }
}
