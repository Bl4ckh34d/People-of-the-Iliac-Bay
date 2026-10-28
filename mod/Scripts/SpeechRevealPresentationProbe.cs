using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Bounded native Talk presentation acceptance; uses one retained WAV, no service or model call.</summary>
    internal static class SpeechRevealPresentationProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-speech-reveal-presentation-probe";
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string BaselineSave = "People - Gothway Garden";
        private const string ReplyText = "Good day, traveller. The road ahead is long, but you are welcome to rest here for a while.";
        private const string ReplySha256 = "1a52a457dc298d1ad0c3f594d373f5dd0c45a021051838b17540709fbdbf9a06";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

        [Serializable]
        private sealed class ResponseManifest
        {
            public int schemaVersion = 1;
            public string requestId;
            public string conversationToken;
            public string personId;
            public string encoding = "pcm_s16le";
            public string wavFile;
            public int sampleRate;
            public int channels;
            public int sampleFrames;
        }

        [Serializable]
        private sealed class ResponseFailureTerminal
        {
            public int schemaVersion = 1;
            public string requestId;
            public string conversationToken;
            public string personId;
            public string status = "failed";
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave, string retainedWav)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0 && character == OwnedCharacter &&
                fixtureSave == BaselineSave && host != null && streets != null && streets.Ready,
                "Speech reveal probe requires opt-in and exact owned Gothway Garden fixture.");
            int wavRate, wavChannels, wavFrames;
            ValidateWav(retainedWav, out wavRate, out wavChannels, out wavFrames);

            GameManager game = GameManager.Instance;
            BreezeTtsTrialBridge previousBridge = Field(host, "breezeTtsTrial").GetValue(host) as BreezeTtsTrialBridge;
            Require(previousBridge == null || (!previousBridge.HasPendingSpeech &&
                (previousBridge.OutputAudioSource == null || !previousBridge.OutputAudioSource.isPlaying)),
                "Speech reveal probe requires existing voice bridge idle.");
            int baselineSlot = game.SaveLoadManager.FindSaveFolderByNames(character, fixtureSave);
            Require(baselineSlot >= 0, "Exact speech reveal baseline save missing.");

            bool previousBusy = Convert.ToBoolean(Field(host, "conversationBusy").GetValue(host));
            bool previousPrewarm = Convert.ToBoolean(Field(host, "dialoguePrewarmEnabled").GetValue(host));
            bool previousGreetingPending = Convert.ToBoolean(Field(host, "returningGreetingPending").GetValue(host));
            float previousGreetingRetry = Convert.ToSingle(Field(host, "returningGreetingRetryAt").GetValue(host));
            bool previousPrewarmQueued = Convert.ToBoolean(Field(host, "dialoguePrewarmQueued").GetValue(host));
            Vector3 previousPlayerPosition = game.PlayerMotor.transform.position;

            string root = Path.Combine(Path.GetTempPath(), "PeopleOfTheIliacBay-SpeechReveal-" + Guid.NewGuid().ToString("N"));
            string captureRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(retainedWav), "..",
                "speech-presentation-captures", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(captureRoot);
            string bridgeRoot = Path.Combine(root, "bridge");
            GameObject bridgeOwner = new GameObject("Disposable speech reveal probe bridge");
            bridgeOwner.hideFlags = HideFlags.HideAndDontSave;
            BreezeTtsTrialBridge bridge = null;
            DaggerfallTalkWindow talk = null;
            ListBox transcript = null;
            int baselineRows = 0;
            long baselineRevision = 0;
            bool opened = false;
            bool baselineReloaded = false;
            Action<BreezeTtsTrialBridge.MixerPlaybackStartReceipt> onsetObserver = null;
            SaveLoadManager.OnLoadEventHandler baselineReloadHandler = _ => baselineReloaded = true;
            try
            {
                // Prevent prewarm and returning-greeting model calls during the
                // native window setup. Voice remains disabled until the isolated
                // retained-response bridge is attached after Talk setup.
                Field(host, "conversationBusy").SetValue(host, true);
                Field(host, "dialoguePrewarmEnabled").SetValue(host, false);
                Field(host, "returningGreetingPending").SetValue(host, false);
                Field(host, "returningGreetingRetryAt").SetValue(host, Time.unscaledTime + 86400f);
                Field(host, "breezeTtsTrial").SetValue(host, null);
                ResidentActor actor = null;
                IEnumerator actorWait = ConversationMoodProbe.WaitForUnusedManagedAdult(host,
                    game.PlayerMotor.transform.position, value => actor = value, false);
                while (actorWait.MoveNext()) yield return actorWait.Current;
                Require(actor != null && actor.Person != null && actor.Resident != null && actor.Resident.IsAlive,
                    "No eligible managed adult resident available for native Talk probe.");
                ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
                DaggerfallUI.Instance.PopToHUD();
                game.TalkManager.TalkToMobileNPC(actor.Person);
                float talkDeadline = Time.realtimeSinceStartup + 15f;
                while (Time.realtimeSinceStartup < talkDeadline)
                {
                    talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                    if (talk != null && talk.IsSetup && (bool)Field(host, "conversationSessionOpen").GetValue(host) &&
                        ReferenceEquals(Field(host, "memoryWindow").GetValue(host), talk)) break;
                    yield return null;
                }
                Require(talk != null && talk.IsSetup && DaggerfallUI.UIManager.ContainsWindow(talk),
                    "Native Talk window did not open for selected resident.");
                opened = true;
                string diagnostic;
                bridge = BreezeTtsTrialBridge.TryCreateFromCommandLine(bridgeOwner,
                    new[] { BreezeTtsTrialBridge.RunFlag, bridgeRoot }, out diagnostic);
                Require(bridge != null && bridge.IsTrialMode && bridge.VoiceEnabled && bridge.PublishedRequestCount == 0,
                    "Isolated receipt bridge initialization failed: " + diagnostic);
                Field(host, "breezeTtsTrial").SetValue(host, bridge);
                bridge.SetFeatureOptions(false, false);
                Field(host, "conversationBusy").SetValue(host, false);
                transcript = Field(typeof(DaggerfallTalkWindow), "listboxConversation", Instance).GetValue(talk) as ListBox;
                Require(transcript != null, "Native Talk transcript unavailable.");
                baselineRows = transcript.Count;
                string baselineHistory = ReadHistory(talk);
                baselineRevision = Convert.ToInt64(Field(host, "conversationContextRequestRevision").GetValue(host));

                string playerText = "Could I rest here for a while?";
                ConversationContextSnapshot snapshot = host.CaptureConversationContext(talk, playerText, checked(baselineRevision + 1));
                Field(host, "conversationBusy").SetValue(host, true);
                Method(host, "UpdateConversationPendingIndicator").Invoke(host, new object[] { talk });
                yield return new WaitForSecondsRealtime(1.15f);
                ListBox.ListItem playerPending = FindPendingRow(transcript, playerText, true);
                string firstName = GameManager.Instance.TalkManager.NameNPC.Split(' ')[0];
                ListBox.ListItem npcPending = FindPendingRow(transcript, firstName, false);
                Require(playerPending != null && npcPending != null &&
                    npcPending.textColor == PeopleOfTheIliacBayMod.ConversationPendingTextColor &&
                    npcPending.textLabel.Text.IndexOf(firstName, StringComparison.Ordinal) >= 0,
                    "Talk pending overlay lacks visible player row or purple first-name thinking line.");
                Require(IndexOfRow(transcript, playerPending) < IndexOfRow(transcript, npcPending),
                    "Native pending player row is not above the purple NPC thinking row.");
                Require(!ReadHistory(talk).Contains(playerText) && ReadHistory(talk) == baselineHistory,
                    "Pending overlay leaked into canonical conversation history.");

                string requestId = null;
                BreezeTtsTrialBridge.MixerPlaybackStartReceipt observedOnset = null;
                onsetObserver = receipt =>
                {
                    if (receipt != null && receipt.RequestId == requestId) observedOnset = receipt;
                };
                bridge.SetFeatureOptions(true, false);
                bridge.MixerPlaybackStarted += onsetObserver;
                bool afterPresentationCalled = false;
                string historyAtAfterPresentation = null;
                bool accepted = (bool)Method(host, "TryDeferAcceptedSpeechAppend").Invoke(host,
                    new object[] { talk, snapshot, playerText, ReplyText,
                        (Action)(() => AppendNativePair(host, talk, playerText, ReplyText)),
                        (Action)(() => {
                            afterPresentationCalled = true;
                            historyAtAfterPresentation = ReadHistory(talk);
                        }) });
                Require(accepted, "Production accepted-speech deferral rejected isolated retained-WAV request.");
                object pending = Field(host, "pendingAcceptedSpeechReveal").GetValue(host);
                Require(pending != null && (bool)Field(pending.GetType(), "Resolved").GetValue(pending) == false &&
                    !afterPresentationCalled,
                    "Accepted reply or its dependent action callback did not remain pending before mixer onset.");
                requestId = (string)Field(pending.GetType(), "RequestId").GetValue(pending);
                SeedRetainedResponse(bridge, requestId, (string)Field(host, "conversationContextSessionId").GetValue(host),
                    (string)Field(host, "memoryPersonId").GetValue(host), retainedWav, wavRate, wavChannels, wavFrames);

                float onsetDeadline = Time.realtimeSinceStartup + 12f;
                while (Time.realtimeSinceStartup < onsetDeadline && Field(host, "pendingAcceptedSpeechReveal").GetValue(host) != null)
                    yield return null;
                Require(Field(host, "pendingAcceptedSpeechReveal").GetValue(host) == null &&
                    !Convert.ToBoolean(Field(host, "conversationPendingSpeechReveal").GetValue(host)) &&
                    afterPresentationCalled && historyAtAfterPresentation != null &&
                    historyAtAfterPresentation.Contains("Player: " + playerText) &&
                    historyAtAfterPresentation.Contains("NPC: " + ReplyText) &&
                    observedOnset != null && observedOnset.RequestId == requestId &&
                    observedOnset.ConversationToken == (string)Field(host, "conversationContextSessionId").GetValue(host) &&
                    observedOnset.PersonId == (string)Field(host, "memoryPersonId").GetValue(host) &&
                    observedOnset.ExactText == ReplyText && observedOnset.PlaybackIndex == 1 &&
                    observedOnset.FirstNonZeroMixerTicks > 0 && observedOnset.FirstNonZeroMixerFrame >= 0,
                    "Accepted reply did not append its canonical pair before the dependent action callback at exact mixer onset.");
                string acceptedHistory = ReadHistory(talk);
                Require(acceptedHistory.Contains("Player: " + playerText) && acceptedHistory.Contains("NPC: " + ReplyText),
                    "Onset did not append native player/NPC rows to canonical history.");
                StreamingWordHighlightOverlay overlay = FindOverlay(talk, requestId);
                Require(overlay != null, "Accepted reply did not attach production full-WAV word overlay.");
                ListBox.ListItem answerRow = Field(overlay.GetType(), "row").GetValue(overlay) as ListBox.ListItem;
                string canonicalReply = (string)Method(typeof(PeopleOfTheIliacBayMod), "CanonicalTranscriptText", Static)
                    .Invoke(null, new object[] { answerRow });
                Require(answerRow != null && canonicalReply == ReplyText &&
                    (string)Field(overlay.GetType(), "fullWavRequestId").GetValue(overlay) == requestId &&
                    (string)Field(overlay.GetType(), "rowText").GetValue(overlay) == ReplyText &&
                    answerRow.textColor != DaggerfallUI.DaggerfallQuestionTextColor &&
                    answerRow.textColor != PeopleOfTheIliacBayMod.ConversationPendingTextColor,
                    "Accepted answer row is not bound to the exact canonical full-WAV reply.");

                bool observedFirstReveal = false;
                float revealDeadline = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < revealDeadline)
                {
                    int end = Convert.ToInt32(Field(overlay.GetType(), "currentCumulativeTextEnd").GetValue(overlay));
                    int wordStart = Convert.ToInt32(Field(overlay.GetType(), "currentWordStartUtf16").GetValue(overlay));
                    int blueGlyphs = Convert.ToInt32(Field(overlay.GetType(), "lastDrawnBlueGlyphCount").GetValue(overlay));
                    if (wordStart > 0 && end > wordStart && end < ReplyText.Length && blueGlyphs > 0 &&
                        answerRow.textLabel.Text == ReplyText.Substring(0, end) && canonicalReply == ReplyText)
                    { observedFirstReveal = true; break; }
                    yield return null;
                }
                Require(observedFirstReveal,
                    "First full-WAV playback did not render a partial prefix with current blue glyph and normal past text.");
                Require(answerRow.textColor == DaggerfallUI.DaggerfallDefaultTextColor &&
                    answerRow.textLabel.TextColor == DaggerfallUI.DaggerfallDefaultTextColor,
                    "Past words must use normal yellow NPC text during initial reveal.");
                IEnumerator initialCapture = CaptureFrame(Path.Combine(captureRoot, "initial-reveal.png"));
                while (initialCapture.MoveNext()) yield return initialCapture.Current;

                StreamingWordHighlightOverlay.PrepareReplayForRow(answerRow);
                BreezeTtsTrialBridge.RequestState replayState = bridge.ReplayRequest(requestId,
                    (string)Field(host, "conversationContextSessionId").GetValue(host));
                Require(replayState == BreezeTtsTrialBridge.RequestState.Pending ||
                    replayState == BreezeTtsTrialBridge.RequestState.Loading || replayState == BreezeTtsTrialBridge.RequestState.Ready ||
                    replayState == BreezeTtsTrialBridge.RequestState.Playing,
                    "Retained full-WAV replay was not accepted.");
                float replayDeadline = Time.realtimeSinceStartup + 8f;
                bool replayCurrentBlue = false;
                while (Time.realtimeSinceStartup < replayDeadline)
                {
                    int wordStart = Convert.ToInt32(Field(overlay.GetType(), "currentWordStartUtf16").GetValue(overlay));
                    int blueGlyphs = Convert.ToInt32(Field(overlay.GetType(), "lastDrawnBlueGlyphCount").GetValue(overlay));
                    if (answerRow.textLabel.Text == ReplyText && wordStart > 0 && blueGlyphs > 0)
                    { replayCurrentBlue = true; break; }
                    yield return null;
                }
                Require(replayCurrentBlue && ReadHistory(talk) == acceptedHistory,
                    "Replay failed to preserve full canonical text with current blue word highlight.");
                Require(answerRow.textColor == DaggerfallUI.DaggerfallDefaultTextColor &&
                    answerRow.textLabel.TextColor == DaggerfallUI.DaggerfallDefaultTextColor,
                    "Past words must use normal yellow NPC text during retained replay.");
                IEnumerator replayCapture = CaptureFrame(Path.Combine(captureRoot, "replay.png"));
                while (replayCapture.MoveNext()) yield return replayCapture.Current;
                float replayEndDeadline = Time.realtimeSinceStartup + 15f;
                while (bridge.GetRequestState(requestId) == BreezeTtsTrialBridge.RequestState.Playing &&
                    Time.realtimeSinceStartup < replayEndDeadline) yield return null;
                Require(bridge.GetRequestState(requestId) != BreezeTtsTrialBridge.RequestState.Playing,
                    "Retained replay did not finish within its bounded deadline.");

                string fallbackPrompt = "Is the road safe?";
                long fallbackRevision = Convert.ToInt64(Field(host, "conversationContextRequestRevision").GetValue(host)) + 1;
                ConversationContextSnapshot fallbackSnapshot = host.CaptureConversationContext(talk, fallbackPrompt, fallbackRevision);
                Field(host, "conversationBusy").SetValue(host, true);
                Method(host, "UpdateConversationPendingIndicator").Invoke(host, new object[] { talk });
                int fallbackAppends = 0;
                ListBox.ListItem fallbackRow = null;
                bool fallbackAccepted = (bool)Method(host, "TryDeferAcceptedSpeechAppend").Invoke(host,
                    new object[] { talk, fallbackSnapshot, fallbackPrompt, ReplyText,
                        (Action)(() => {
                            AppendNativePair(host, talk, fallbackPrompt, ReplyText);
                            fallbackAppends++;
                            fallbackRow = transcript.GetItem(transcript.Count - 1);
                        }), null });
                Require(fallbackAccepted, "Terminal fallback speech request was not accepted.");
                object fallbackPending = Field(host, "pendingAcceptedSpeechReveal").GetValue(host);
                string fallbackRequestId = (string)Field(fallbackPending.GetType(), "RequestId").GetValue(fallbackPending);
                string fallbackToken = (string)Field(fallbackPending.GetType(), "ConversationToken").GetValue(fallbackPending);
                string fallbackPerson = (string)Field(fallbackPending.GetType(), "PersonId").GetValue(fallbackPending);
                string fallbackExactText = (string)Field(fallbackPending.GetType(), "ExactText").GetValue(fallbackPending);
                string responseRoot;
                Require(bridge.TryGetStreamingResponseRoot(out responseRoot), "Isolated bridge response root missing for terminal case.");
                string historyBeforeFallback = ReadHistory(talk);
                int priorReplyCount = CountOccurrences(historyBeforeFallback, "NPC: " + ReplyText);
                Action<BreezeTtsTrialBridge.PrePlaybackTerminalReceipt> fallbackHandler =
                    (Action<BreezeTtsTrialBridge.PrePlaybackTerminalReceipt>)Field(fallbackPending.GetType(), "TerminalHandler").GetValue(fallbackPending);
                object[] mismatchedReceipts =
                {
                    new object[] { "different-request", fallbackToken, fallbackPerson, fallbackExactText },
                    new object[] { fallbackRequestId, "different-conversation", fallbackPerson, fallbackExactText },
                    new object[] { fallbackRequestId, fallbackToken, "different-person", fallbackExactText },
                    new object[] { fallbackRequestId, fallbackToken, fallbackPerson, "different accepted text" }
                };
                foreach (object[] mismatch in mismatchedReceipts)
                {
                    object receipt = Activator.CreateInstance(typeof(BreezeTtsTrialBridge.PrePlaybackTerminalReceipt),
                        BindingFlags.Instance | BindingFlags.NonPublic, null,
                        new object[] { mismatch[0], mismatch[1], mismatch[2], mismatch[3],
                            BreezeTtsTrialBridge.RequestState.Failed, "mismatched-terminal-probe" }, null);
                    fallbackHandler(receipt as BreezeTtsTrialBridge.PrePlaybackTerminalReceipt);
                }
                Require(Field(host, "pendingAcceptedSpeechReveal").GetValue(host) != null && fallbackAppends == 0,
                    "Mismatched terminal receipt revealed or cleared accepted speech.");
                string failurePath = Path.Combine(responseRoot, fallbackRequestId + ".failed.json");
                byte[] failureBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ResponseFailureTerminal {
                    requestId = fallbackRequestId, conversationToken = fallbackToken, personId = fallbackPerson }));
                Field(bridge, "nextResponsePollTime").SetValue(bridge, 0f);
                using (var heldFailureReceipt = new FileStream(failurePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    heldFailureReceipt.Write(failureBytes, 0, failureBytes.Length);
                    heldFailureReceipt.Flush();
                    Method(bridge, "Update").Invoke(bridge, null);
                    Require(Field(host, "pendingAcceptedSpeechReveal").GetValue(host) != null && fallbackAppends == 0 &&
                        bridge.GetRequestState(fallbackRequestId) == BreezeTtsTrialBridge.RequestState.Pending,
                        "An inaccessible terminal file must leave the accepted reply pending for the next bounded poll.");
                }
                Field(bridge, "nextResponsePollTime").SetValue(bridge, 0f);
                Method(bridge, "Update").Invoke(bridge, null);
                float terminalDeadline = Time.realtimeSinceStartup + 6f;
                while (Time.realtimeSinceStartup < terminalDeadline && Field(host, "pendingAcceptedSpeechReveal").GetValue(host) != null)
                    yield return null;
                string historyAfterFallback = ReadHistory(talk);
                string failureMeasurement;
                Require(Field(host, "pendingAcceptedSpeechReveal").GetValue(host) == null &&
                    !Convert.ToBoolean(Field(host, "conversationPendingSpeechReveal").GetValue(host)) &&
                    !Convert.ToBoolean(Field(host, "conversationBusy").GetValue(host)) &&
                    fallbackAppends == 1 && fallbackRow != null && fallbackRow.textLabel != null &&
                    fallbackRow.textLabel.Text == ReplyText &&
                    CountOccurrences(historyAfterFallback, "NPC: " + ReplyText) == priorReplyCount + 1 &&
                    historyAfterFallback.Contains("Player: " + fallbackPrompt) &&
                    historyAfterFallback.Contains("NPC: " + ReplyText) &&
                    FindPendingRow(transcript, fallbackPrompt, true) == null &&
                    bridge.GetRequestState(fallbackRequestId) == BreezeTtsTrialBridge.RequestState.Failed &&
                    bridge.GetPlaybackIndex(fallbackRequestId) == 0 && FindOverlay(talk, fallbackRequestId) == null &&
                    bridge.TryReadPlaybackMeasurement(fallbackRequestId, out failureMeasurement) &&
                    failureMeasurement.Contains("\"status\":\"coordinator-failed\""),
                    "Bound terminal failure did not reveal full text once, clear pending UI, and skip playback.");

                string stalePrompt = "Can you hear me?";
                long staleRevision = Convert.ToInt64(Field(host, "conversationContextRequestRevision").GetValue(host)) + 1;
                ConversationContextSnapshot staleSnapshot = host.CaptureConversationContext(talk, stalePrompt, staleRevision);
                Field(host, "conversationBusy").SetValue(host, true);
                Method(host, "UpdateConversationPendingIndicator").Invoke(host, new object[] { talk });
                bool staleAccepted = (bool)Method(host, "TryDeferAcceptedSpeechAppend").Invoke(host,
                    new object[] { talk, staleSnapshot, stalePrompt, ReplyText,
                        (Action)(() => AppendNativePair(host, talk, stalePrompt, ReplyText)), null });
                Require(staleAccepted, "Closed-session stale-callback request was not accepted.");
                Require(bridge.PublishedRequestCount == 3,
                    "Speech reveal probe queued unexpected additional speech work.");
                object stalePending = Field(host, "pendingAcceptedSpeechReveal").GetValue(host);
                Action<BreezeTtsTrialBridge.PrePlaybackTerminalReceipt> staleHandler =
                    (Action<BreezeTtsTrialBridge.PrePlaybackTerminalReceipt>)Field(stalePending.GetType(), "TerminalHandler").GetValue(stalePending);
                string staleRequestId = (string)Field(stalePending.GetType(), "RequestId").GetValue(stalePending);
                string token = (string)Field(stalePending.GetType(), "ConversationToken").GetValue(stalePending);
                string personId = (string)Field(stalePending.GetType(), "PersonId").GetValue(stalePending);
                string staleText = (string)Field(stalePending.GetType(), "ExactText").GetValue(stalePending);
                string historyBeforeClose = ReadHistory(talk);
                int offerSession = Convert.ToInt32(Field(host, "memorySession").GetValue(host));
                var closeOffer = new NpcActionOffer(personId, offerSession, 1,
                    new NpcActionDecision { DurationSeconds = 60, Option = new NpcActionOption {
                        Id = 1, Kind = NpcActionKind.Follow, Label = "walk with the player",
                        Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player", Quantity = 1 } } });
                Field(host, "pendingConversationAction").SetValue(host, closeOffer);
                Field(host, "pendingConversationActionHistory").SetValue(host, historyBeforeClose);
                DaggerfallUI.Instance.PopToHUD();
                opened = false;
                object staleReceipt = Activator.CreateInstance(typeof(BreezeTtsTrialBridge.PrePlaybackTerminalReceipt),
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new object[] { staleRequestId, token, personId, staleText,
                        BreezeTtsTrialBridge.RequestState.Cancelled, "closed-session-probe" }, null);
                staleHandler(staleReceipt as BreezeTtsTrialBridge.PrePlaybackTerminalReceipt);
                float closeDeadline = Time.realtimeSinceStartup + 2f;
                while (Field(host, "pendingAcceptedSpeechReveal").GetValue(host) != null &&
                    Time.realtimeSinceStartup < closeDeadline) yield return null;
                NpcActionOption closedCandidate; int closedDuration;
                Require(!DaggerfallUI.UIManager.ContainsWindow(talk) &&
                    ReadHistory(talk) == historyBeforeClose && Field(host, "pendingAcceptedSpeechReveal").GetValue(host) == null &&
                    Field(host, "pendingConversationAction").GetValue(host) == null &&
                    Field(host, "pendingConversationActionHistory").GetValue(host) == null &&
                    !closeOffer.TryGetSemanticCandidate(personId, offerSession, 1000000, out closedCandidate, out closedDuration),
                    "Closing native Talk did not discard deferred speech and invalidate its pending action offer.");

            }
            finally
            {
                if (opened && talk != null && transcript != null)
                {
                    while (transcript.Count > baselineRows) transcript.RemoveItem(transcript.Count - 1);
                    if (DaggerfallUI.UIManager.ContainsWindow(talk)) DaggerfallUI.Instance.PopToHUD();
                }
                Field(host, "breezeTtsTrial").SetValue(host, previousBridge);
                Field(host, "conversationBusy").SetValue(host, previousBusy);
                Field(host, "dialoguePrewarmEnabled").SetValue(host, previousPrewarm);
                Field(host, "dialoguePrewarmQueued").SetValue(host, previousPrewarmQueued);
                Field(host, "returningGreetingPending").SetValue(host, previousGreetingPending);
                Field(host, "returningGreetingRetryAt").SetValue(host, previousGreetingRetry);
                game.PlayerMotor.transform.position = previousPlayerPosition;
                Physics.SyncTransforms();
                if (bridge != null && onsetObserver != null) bridge.MixerPlaybackStarted -= onsetObserver;
                if (bridge != null) bridge.Shutdown();
                UnityEngine.Object.DestroyImmediate(bridgeOwner);
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch (Exception error) { Debug.LogWarning("SPEECH_REVEAL_PROBE_CLEANUP: " + error.GetType().Name); }
                if (game.SaveLoadManager.FindSaveFolderByNames(character, fixtureSave) >= 0)
                {
                    SaveLoadManager.OnLoad += baselineReloadHandler;
                    game.SaveLoadManager.Load(baselineSlot);
                }
            }
            float restoreDeadline = Time.realtimeSinceStartup + 60f;
            while ((!baselineReloaded || !streets.Ready || DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) &&
                Time.realtimeSinceStartup < restoreDeadline) yield return null;
            SaveLoadManager.OnLoad -= baselineReloadHandler;
            Require(baselineReloaded && streets.Ready && !(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow),
                "Speech reveal probe baseline restore timed out.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SPEECH_REVEAL_BASELINE_RESTORED: save=" + fixtureSave + ".");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SPEECH_REVEAL_PRESENTATION_PASS: native-talk=true; isolated-retained-wav=true; pending-overlay=true; onset-bound-append=true; dependent-callback-after-append=true; prefix-current-blue-past-normal=true; replay-full-current-blue=true; terminal-failure-full-fallback=true; terminal-mismatch-rejected=true; failure-pending-cleared=true; failure-no-playback=true; closed-stale-discarded=true; talk-close-offer-invalidated=true; service-calls=0; model-calls=0; audible-output=not-claimed.");
        }

        private static void SeedRetainedResponse(BreezeTtsTrialBridge bridge, string requestId,
            string token, string personId, string sourceWav, int sampleRate, int channels, int frames)
        {
            string responseRoot;
            Require(bridge.TryGetStreamingResponseRoot(out responseRoot), "Isolated bridge response root missing.");
            string destination = Path.Combine(responseRoot, "accent-breton-female-v2.wav");
            File.Copy(sourceWav, destination, true);
            var manifest = new ResponseManifest { requestId = requestId, conversationToken = token,
                personId = personId, wavFile = Path.GetFileName(destination), sampleRate = sampleRate,
                channels = channels, sampleFrames = frames };
            File.WriteAllText(Path.Combine(responseRoot, requestId + ".manifest.json"),
                JsonUtility.ToJson(manifest), new UTF8Encoding(false));
        }

        private static IEnumerator CaptureFrame(string path)
        {
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
            Require(File.Exists(path), "Native speech presentation screenshot was not saved.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SPEECH_REVEAL_CAPTURE: path=" + path);
        }

        private static void ValidateWav(string path, out int sampleRate, out int channels, out int frames)
        {
            Require(Path.IsPathRooted(path) && File.Exists(path), "Approved retained WAV path missing.");
            byte[] bytes = File.ReadAllBytes(path);
            string digest;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) hex.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                digest = hex.ToString();
            }
            Require(digest == ReplySha256 && bytes.Length >= 44 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" &&
                Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE" && BitConverter.ToInt16(bytes, 20) == 1,
                "Retained WAV does not match approved Breton female v2 PCM sample.");
            channels = BitConverter.ToInt16(bytes, 22);
            sampleRate = BitConverter.ToInt32(bytes, 24);
            int bits = BitConverter.ToInt16(bytes, 34);
            int dataBytes = BitConverter.ToInt32(bytes, 40);
            Require(channels >= 1 && channels <= 2 && sampleRate >= 8000 && bits == 16 && dataBytes > 0 && dataBytes % (channels * 2) == 0,
                "Retained WAV PCM header is invalid.");
            frames = dataBytes / (channels * 2);
        }

        private static ListBox.ListItem FindPendingRow(ListBox transcript, string text, bool question)
        {
            for (int index = 0; index < transcript.Count; index++)
            {
                ListBox.ListItem row = transcript.GetItem(index);
                if (row != null && row.textLabel != null && row.textLabel.Name == "PeopleOfTheIliacBayPendingEphemeral" &&
                    row.textLabel.Text.Contains(text) && (row.textColor == DaggerfallUI.DaggerfallQuestionTextColor) == question) return row;
            }
            return null;
        }

        private static int IndexOfRow(ListBox transcript, ListBox.ListItem target)
        {
            for (int index = 0; index < transcript.Count; index++)
                if (ReferenceEquals(transcript.GetItem(index), target)) return index;
            return -1;
        }

        private static int CountOccurrences(string value, string fragment)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(fragment)) return 0;
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
            { count++; offset += fragment.Length; }
            return count;
        }

        private static StreamingWordHighlightOverlay FindOverlay(DaggerfallTalkWindow talk, string requestId)
        { return FindOverlay(talk.NativePanel, requestId); }

        private static StreamingWordHighlightOverlay FindOverlay(Panel panel, string requestId)
        {
            if (panel == null) return null;
            foreach (BaseScreenComponent component in panel.Components)
            {
                StreamingWordHighlightOverlay overlay = component as StreamingWordHighlightOverlay;
                if (overlay != null &&
                    (string)Field(overlay.GetType(), "fullWavRequestId").GetValue(overlay) == requestId) return overlay;
                Panel child = component as Panel;
                if (child != null)
                {
                    overlay = FindOverlay(child, requestId);
                    if (overlay != null) return overlay;
                }
            }
            return null;
        }

        private static void AppendNativePair(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk, string question, string answer)
        {
            Method(host, "AppendUnrecordedDialoguePair").Invoke(host,
                new object[] { talk, question, answer, true });
        }

        private static string ReadHistory(DaggerfallTalkWindow talk)
        { return (string)Method(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", Static).Invoke(null, new object[] { talk }); }

        private static FieldInfo Field(object target, string name) { return Field(target.GetType(), name); }
        private static FieldInfo Field(Type type, string name) { return Field(type, name, Instance); }
        private static FieldInfo Field(Type type, string name, BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null) throw new InvalidOperationException("Speech reveal probe field missing: " + name);
            return field;
        }

        private static MethodInfo Method(object target, string name)
        { return Method(target.GetType(), name, Instance); }
        private static MethodInfo Method(Type type, string name, BindingFlags flags)
        {
            MethodInfo method = type.GetMethod(name, flags);
            if (method == null) throw new InvalidOperationException("Speech reveal probe method missing: " + name);
            return method;
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
