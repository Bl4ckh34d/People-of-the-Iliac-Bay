using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Opt-in real SubmitFreeText + single dialogue model + external synthesis + Unity playback trial.</summary>
    internal static class BreezeTtsTrialProbe
    {
        private const string RunFlag = "-peopleoftheiliacbay-breeze-tts-probe";
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string BaselineSave = "People - Gothway Garden";
        private static readonly string[] PlayerTurns =
        {
            "How has the day been treating you?",
            "Tell me what you enjoy about your daily work."
        };

        [Serializable]
        private sealed class HealthReply { public string status = null; }
        [Serializable]
        private sealed class PlaybackMeasurement
        {
            public int schemaVersion = 0;
            public string requestId = null;
            public string conversationToken = null;
            public string personId = null;
            public string status = null;
            public long textReadyMonotonicTicks = 0;
            public long playbackStartMonotonicTicks = 0;
            public long timestampFrequency = 0;
            public long mixerCallbackFrames = 0;
            public long mixerCallbackSamples = 0;
            public long nonZeroOutputSamples = 0;
            public long peakOutputAmplitudeMicrounits = 0;
            public int expectedSampleFrames = 0;
            public int sourceFinalSample = 0;
            public int lastKnownSample = 0;
            public int sampleRate = 0;
            public int channels = 0;
            public long playbackEndMonotonicTicks = 0;
            public long expectedPlaybackMilliseconds = 0;
            public long observedPlaybackMilliseconds = 0;
        }

        private sealed class QwenCheck
        {
            public volatile bool Done;
            public string HealthJson;
            public string ModelJson;
            public string Error;
            public string TransientError;
        }

        private sealed class PilotEvidence
        {
            public string PersonId;
            public string SessionId;
            public int Turns;
            public int QwenTurns;
            public int ScriptedTurns;
            public int PlayedLines;
            public long MixerFrames;
            public string QwenModelId;
            public string D3ModelId;
            public string LastRequestId;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            ConversationMoodProbe.Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0,
                "Breeze pilot requires its explicit command-line flag.");
            ConversationMoodProbe.Require(character == OwnedCharacter && fixtureSave == BaselineSave,
                "Breeze pilot requires the owned disposable character and exact baseline save.");
            ConversationMoodProbe.Require(host && streets && streets.Ready && GameManager.Instance && GameManager.Instance.IsReady,
                "Breeze pilot requires the ready owned native fixture.");

            BreezeTtsTrialBridge bridge = ConversationMoodProbe.ReadHostField(host, "breezeTtsTrial") as BreezeTtsTrialBridge;
            LocalDialogueClient dialogue = ConversationMoodProbe.ReadHostField(host, "localDialogue") as LocalDialogueClient;
            LocalDialogueConnection qwenConnection = ReadQwenConnection(host);
            ConversationMoodProbe.Require(bridge && dialogue && dialogue.enabled && !dialogue.IsBusy &&
                Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host, "localDialogueEnabled"), CultureInfo.InvariantCulture) &&
                qwenConnection != null,
                "Breeze pilot requires the configured single dialogue client and speech bridge.");
            ConversationMoodProbe.Require(bridge.PublishedRequestCount == 0 && !bridge.HasPendingSpeech &&
                string.IsNullOrEmpty(bridge.LastQueuedRequestId),
                "Breeze pilot requires a fresh empty TTS trial directory; refusing stale requests.");

            GameManager game = GameManager.Instance;
            ConversationMoodProbe.Require(game.SaveLoadManager.FindSaveFolderByNames(character, fixtureSave) >= 0,
                "Exact Breeze pilot baseline fixture is missing.");
            double baselineClock = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            Vector3 baselinePosition = game.PlayerMotor.transform.position;
            bool originalGreetingPending = Convert.ToBoolean(
                ConversationMoodProbe.ReadHostField(host, "returningGreetingPending"), CultureInfo.InvariantCulture);
            float originalGreetingRetry = Convert.ToSingle(
                ConversationMoodProbe.ReadHostField(host, "returningGreetingRetryAt"), CultureInfo.InvariantCulture);
            var evidence = new List<PilotEvidence>();
            string[] activeSession = new string[1];
            Exception pilotFailure = null;
            IEnumerator pilot = RunPilotTurns(host, streets, game, bridge, dialogue, qwenConnection,
                evidence, activeSession);
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = pilot.MoveNext(); if (more) current = pilot.Current; }
                    catch (Exception error) { pilotFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                ConversationMoodProbe.Dispose(pilot, ref pilotFailure);
                if (!string.IsNullOrEmpty(activeSession[0])) bridge.EndConversation(activeSession[0]);
                if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
            }

            Exception closeFailure = null;
            IEnumerator close = ConversationMoodProbe.CloseTalk(host);
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = close.MoveNext(); if (more) current = close.Current; }
                    catch (Exception error) { closeFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally { ConversationMoodProbe.Dispose(close, ref closeFailure); }

            Exception restoreFailure = null;
            IEnumerator restore = ConversationMoodProbe.LoadSave(host, streets, character, fixtureSave, true);
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = restore.MoveNext(); if (more) current = restore.Current; }
                    catch (Exception error) { restoreFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally { ConversationMoodProbe.Dispose(restore, ref restoreFailure); }

            Exception baselineFailure = null;
            try
            {
                double restoredClock = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                ConversationMoodProbe.Require(Math.Abs(restoredClock - baselineClock) <= 2d,
                    "Breeze pilot did not restore baseline game clock.");
                ConversationMoodProbe.Require(GameManager.Instance.PlayerMotor != null &&
                    Vector3.Distance(GameManager.Instance.PlayerMotor.transform.position, baselinePosition) <= 0.05f,
                    "Breeze pilot did not restore baseline player position.");
                ConversationMoodProbe.Require(!bridge.HasPendingSpeech,
                    "Breeze pilot left queued or active speech after closing the conversation.");
            }
            catch (Exception error) { baselineFailure = error; }
            try
            {
                ConversationMoodProbe.ReadHostField(host, "returningGreetingPending");
                typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingPending", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(host, originalGreetingPending);
                typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingRetryAt", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(host, originalGreetingRetry);
            }
            catch (Exception error)
            {
                if (baselineFailure == null) baselineFailure = error;
                else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_GREETING_RESTORE_FAIL: " + error);
            }

            if (restoreFailure != null)
                throw new InvalidOperationException("Breeze pilot could not restore the exact baseline save.", restoreFailure);
            if (pilotFailure != null)
            {
                if (baselineFailure != null) Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_BASELINE_VERIFY_FAIL_AFTER_PILOT_FAILURE: " + baselineFailure);
                throw new InvalidOperationException("Breeze TTS native pilot failed; baseline reload was attempted.", pilotFailure);
            }
            if (closeFailure != null) throw new InvalidOperationException("Breeze pilot could not close native Talk cleanly.", closeFailure);
            if (baselineFailure != null) throw new InvalidOperationException("Breeze pilot baseline or cleanup verification failed.", baselineFailure);

            int totalTurns = 0;
            int totalQwenTurns = 0;
            int totalScriptedTurns = 0;
            int playedLines = 0;
            long mixerFrames = 0;
            for (int i = 0; i < evidence.Count; i++)
            {
                totalTurns += evidence[i].Turns;
                totalQwenTurns += evidence[i].QwenTurns;
                totalScriptedTurns += evidence[i].ScriptedTurns;
                playedLines += evidence[i].PlayedLines;
                mixerFrames += evidence[i].MixerFrames;
            }
            ConversationMoodProbe.Require(evidence.Count >= 1 && evidence.Count <= 2 && totalTurns == evidence.Count * 2 &&
                totalQwenTurns > 0 && totalQwenTurns + totalScriptedTurns == totalTurns &&
                playedLines >= totalTurns && mixerFrames > 0,
                "Breeze pilot did not complete two actual text turns and verified playback for each selected NPC.");
            Debug.Log("BREEZE_TTS_NATIVE_PILOT_PASS: source=actual-game-SubmitFreeText; NPCs=" + evidence.Count +
                "; real-text-turns=" + totalTurns + "; qwen-backed-turns=" + totalQwenTurns +
                "; scripted-native-turns=" + totalScriptedTurns + "; playback-measurements=" + playedLines +
                "; mixer-frames=" + mixerFrames + "; qwen-model-id=" + evidence[0].QwenModelId +
                "; single-dialogue-model=true; coordinator-synthesis-claim=false; " +
                "Unity-playback-start-and-completion-verified=true; pre-question-health-and-model-id-check=true; " +
                "exact-baseline-reload=true; pending-speech-cleanup=true.");
        }

        private static IEnumerator RunPilotTurns(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            GameManager game, BreezeTtsTrialBridge bridge, LocalDialogueClient dialogue,
            LocalDialogueConnection qwenConnection, List<PilotEvidence> evidence,
            string[] activeSession)
        {
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                game.PlayerEnterExit.TransitionExterior();
                DaggerfallUI.Instance.PopToHUD();
                float exteriorDeadline = Time.realtimeSinceStartup + 30f;
                while (game.PlayerEnterExit.IsPlayerInsideBuilding && Time.realtimeSinceStartup < exteriorDeadline)
                    yield return new WaitForSecondsRealtime(0.25f);
                ConversationMoodProbe.Require(!game.PlayerEnterExit.IsPlayerInsideBuilding,
                    "Breeze pilot could not enter the saved Gothway Garden exterior scene.");
            }

            WorldStateStore store = ConversationMoodProbe.ReadHostField(host, "store") as WorldStateStore;
            ConversationMoodProbe.Require(store != null, "Breeze pilot cannot read loaded native world store.");
            ResidentActor first = null;
            ResidentActor second = null;
            float selectionDeadline = Time.realtimeSinceStartup + 45f;
            while (!first && Time.realtimeSinceStartup < selectionDeadline)
            {
                store = ConversationMoodProbe.ReadHostField(host, "store") as WorldStateStore;
                if (store != null)
                {
                    // This native-greeting probe needs a genuinely new Talk
                    // transcript. A returning NPC's restored rows are history,
                    // not the base game's opening greeting.
                    first = FindUnusedAdultWithNoConversation(store, game.PlayerMotor.transform.position, null);
                    if (first) second = FindUnusedAdultWithNoConversation(store,
                        game.PlayerMotor.transform.position, first.Resident.Id);
                }
                if (!first) yield return new WaitForSecondsRealtime(0.25f);
            }
            ConversationMoodProbe.Require(first != null, "No eligible adult NPC in the exact owned fixture.");
            var actors = new List<ResidentActor> { first };
            if (second) actors.Add(second);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_ACTORS: count=" + actors.Count +
                "; npc-ids=" + string.Join(",", actors.ConvertAll(x => x.Resident.Id).ToArray()) +
                "; reason=eligible-neutral-managed-adults.");

            string previousQwenModelId = null;
            for (int actorIndex = 0; actorIndex < actors.Count; actorIndex++)
            {
                ResidentActor actor = actors[actorIndex];
                ConversationMoodProbe.Require(actor && actor.Resident != null && actor.Resident.IsAlive,
                    "Selected Breeze pilot actor became unavailable before opening Talk.");
                ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
                int countBeforeOpen = bridge.PublishedRequestCount;
                DaggerfallTalkWindow talk = null;
                yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
                ConversationMoodProbe.Require(talk != null, "Native Talk did not open for selected Breeze pilot resident.");
                string sessionId = Convert.ToString(ConversationMoodProbe.ReadHostField(host, "conversationContextSessionId"),
                    CultureInfo.InvariantCulture);
                activeSession[0] = sessionId;
                ConversationMoodProbe.Require(!string.IsNullOrWhiteSpace(sessionId) &&
                    Convert.ToString(ConversationMoodProbe.ReadHostField(host, "conversationPersonId"), CultureInfo.InvariantCulture) == actor.Resident.Id,
                    "Native conversation session did not bind selected resident identity.");
                var actorEvidence = new PilotEvidence { PersonId = actor.Resident.Id,
                    SessionId = sessionId, D3ModelId = "removed-single-llm" };
                evidence.Add(actorEvidence);

                // The exact native opening ListItem must publish and finish one
                // request before the pilot submits any player text.
                int openingRequestCount = countBeforeOpen;
                float openingDeadline = Time.realtimeSinceStartup + 360f;
                bool openingSettled = false;
                while (Time.realtimeSinceStartup < openingDeadline)
                {
                    int publishedNow = bridge.PublishedRequestCount;
                    if (publishedNow > openingRequestCount)
                    {
                        if (publishedNow != openingRequestCount + 1)
                            throw new InvalidOperationException("Talk opening published multiple speech requests before pilot text.");
                        string greetingRequestId = bridge.LastQueuedRequestId;
                        if (string.IsNullOrEmpty(greetingRequestId))
                            throw new InvalidOperationException("Talk opening published a greeting without a request ID.");
                        PlaybackMeasurement greeting = null;
                        yield return WaitForRealPlayback(bridge, greetingRequestId, actor.Resident.Id, sessionId,
                            null, value => greeting = value);
                        actorEvidence.PlayedLines++;
                        actorEvidence.MixerFrames += greeting.mixerCallbackFrames;
                        openingRequestCount = bridge.PublishedRequestCount;
                        continue;
                    }

                    bool greetingPending = Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host,
                        "returningGreetingPending"), CultureInfo.InvariantCulture);
                    bool conversationBusy = Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host,
                        "conversationBusy"), CultureInfo.InvariantCulture);
                    if (!greetingPending && !conversationBusy && !dialogue.IsBusy && !bridge.HasPendingSpeech)
                    {
                        yield return null;
                        if (bridge.PublishedRequestCount == openingRequestCount && !bridge.HasPendingSpeech &&
                            !dialogue.IsBusy && !Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host,
                                "returningGreetingPending"), CultureInfo.InvariantCulture) &&
                            !Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host,
                                "conversationBusy"), CultureInfo.InvariantCulture))
                        { openingSettled = true; break; }
                    }
                    else yield return null;
                }
                ConversationMoodProbe.Require(openingSettled,
                    "Native Talk greeting or reply work did not settle before the pilot prompt.");
                ConversationMoodProbe.Require(openingRequestCount == countBeforeOpen + 1,
                    "A new native Talk opening did not produce exactly one measured greeting request.");

                for (int turn = 0; turn < PlayerTurns.Length; turn++)
                {
                    string prompt = PlayerTurns[turn];
                    string qwenModelId = null;
                    yield return VerifyQwenReady(qwenConnection, previousQwenModelId, value => qwenModelId = value);
                    if (previousQwenModelId == null) previousQwenModelId = qwenModelId;
                    actorEvidence.QwenModelId = qwenModelId;
                    ConversationMoodProbe.Require(!dialogue.IsBusy && !bridge.HasPendingSpeech,
                        "A prior Qwen request or speech playback remained active before the next prompt.");

                    int publishedBefore = bridge.PublishedRequestCount;
                    string lastQueuedBefore = bridge.LastQueuedRequestId;
                    long replyRequestsBefore = ConversationMoodProbe.ReadLongProperty(dialogue, "ReplyRequestCount");
                    long replyCompletionsBefore = ConversationMoodProbe.ReadLongProperty(dialogue, "ReplyCompletionCount");
                    ListBox transcript = ReadTranscript(talk);
                    int transcriptBefore = transcript.Count;
                    MethodInfo submit = ConversationMoodProbe.RequireMethod(typeof(PeopleOfTheIliacBayMod), "SubmitFreeText", 2);
                    submit.Invoke(host, new object[] { talk, prompt });

                    float replyDeadline = Time.realtimeSinceStartup + 180f;
                    string replyText = null;
                    bool finalized = false;
                    string replyMismatch = "reply-not-yet-finalized";
                    string publishedRequestId = null;
                    float idleWithoutTtsSince = -1f;
                    bool qwenBackedTurn = false;
                    while (Time.realtimeSinceStartup < replyDeadline)
                    {
                        transcript = ReadTranscript(talk);
                        long turnRequestDelta = ConversationMoodProbe.ReadLongProperty(dialogue, "ReplyRequestCount") - replyRequestsBefore;
                        long turnCompletionDelta = ConversationMoodProbe.ReadLongProperty(dialogue, "ReplyCompletionCount") - replyCompletionsBefore;
                        bool hostIdle = !Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host,
                            "conversationBusy"), CultureInfo.InvariantCulture) && !dialogue.IsBusy;
                        bool oneNewRequest = bridge.PublishedRequestCount == publishedBefore + 1 &&
                            !string.IsNullOrEmpty(bridge.LastQueuedRequestId) && bridge.LastQueuedRequestId != lastQueuedBefore;
                        bool sourceCountersConsistent = turnRequestDelta == 1 && turnCompletionDelta == 1 ||
                            turnRequestDelta == 0 && turnCompletionDelta == 0;
                        if (hostIdle && oneNewRequest && sourceCountersConsistent)
                        {
                            qwenBackedTurn = turnRequestDelta == 1 && turnCompletionDelta == 1;
                            publishedRequestId = bridge.LastQueuedRequestId;
                            string finalizedPerson, finalizedSession;
                            ConversationMoodProbe.Require(bridge.TryReadRequestBinding(publishedRequestId,
                                out finalizedPerson, out finalizedSession, out replyText) &&
                                finalizedPerson == actor.Resident.Id && finalizedSession == sessionId &&
                                !string.IsNullOrWhiteSpace(replyText),
                                "Fresh TTS request lacks exact actor/session-bound finalized reply text.");
                            replyMismatch = null;
                            if (!TranscriptContainsFinalizedReply(transcript, transcriptBefore, prompt, replyText,
                                out replyMismatch))
                            {
                                yield return null;
                                continue;
                            }
                            finalized = true;
                            break;
                        }
                        if (hostIdle && !oneNewRequest)
                        {
                            if (idleWithoutTtsSince < 0f) idleWithoutTtsSince = Time.realtimeSinceStartup;
                            else if (Time.realtimeSinceStartup - idleWithoutTtsSince >= 15f)
                            {
                                replyMismatch = "host-idle-without-a-new-finalized-tts-request; request-delta=" +
                                    (bridge.PublishedRequestCount - publishedBefore) + "; reply-request-delta=" +
                                    turnRequestDelta + "; reply-completion-delta=" + turnCompletionDelta;
                                break;
                            }
                        }
                        else idleWithoutTtsSince = -1f;
                        yield return null;
                    }
                    if (!finalized)
                        Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_REPLY_MATCH_FAIL: npc=" + actor.Resident.Id +
                            "; session=" + sessionId + "; prompt=" + prompt + "; request=" + publishedRequestId +
                            "; request-count=" + bridge.PublishedRequestCount + "; reply-requests=" +
                            ConversationMoodProbe.ReadLongProperty(dialogue, "ReplyRequestCount") +
                            "; reply-completions=" + ConversationMoodProbe.ReadLongProperty(dialogue, "ReplyCompletionCount") +
                            "; conversation-busy=" + ConversationMoodProbe.ReadHostField(host, "conversationBusy") +
                            "; mismatch=" + replyMismatch + "; rows=" + DescribeTranscriptRows(transcript, transcriptBefore));
                    ConversationMoodProbe.Require(finalized,
                        "Actual SubmitFreeText did not produce one transcript-bound finalized speech line.");
                    ConversationMoodProbe.Require(replyText != LocalDialogueClient.Unavailable && replyText != LocalDialogueClient.ContextLimit,
                        "Actual routed reply was a technical sentinel rather than an NPC line.");
                    actorEvidence.Turns++;
                    if (qwenBackedTurn) actorEvidence.QwenTurns++;
                    else actorEvidence.ScriptedTurns++;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_TURN_SOURCE: person="+actor.Resident.Id+
                        "; session="+sessionId+"; turn="+(turn+1)+"; source="+(qwenBackedTurn?"qwen":"scripted-native")+
                        "; request="+publishedRequestId);

                    ConversationMoodProbe.Require(bridge.PublishedRequestCount == publishedBefore + 1 &&
                        !string.IsNullOrEmpty(publishedRequestId) && bridge.LastQueuedRequestId == publishedRequestId,
                        "Finalized reply did not publish exactly one matching TTS request.");

                    string requestId = publishedRequestId;
                    string boundPerson;
                    string boundSession;
                    string boundText;
                    ConversationMoodProbe.Require(bridge.TryReadRequestBinding(requestId, out boundPerson, out boundSession, out boundText) &&
                        boundPerson == actor.Resident.Id && boundSession == sessionId && boundText == replyText,
                        "Published TTS request did not preserve the exact final transcript text, NPC identity, and conversation session.");
                    PlaybackMeasurement measurement = null;
                    yield return WaitForRealPlayback(bridge, requestId, actor.Resident.Id, sessionId,
                        replyText, value => measurement = value);
                    actorEvidence.PlayedLines++;
                    actorEvidence.MixerFrames += measurement.mixerCallbackFrames;
                    actorEvidence.LastRequestId = requestId;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_TURN_PASS: npc=" + actor.Resident.Id +
                        "; turn=" + (turn + 1) + "; request=" + requestId + "; chars=" + replyText.Length +
                        "; request-text-match=true; playback-started=" + measurement.playbackStartMonotonicTicks +
                        "; status=" + measurement.status + "; mixer-frames=" + measurement.mixerCallbackFrames + ".");

                    // Leave one wall-clock second for log/measurement inspection; never advance game time as a wait substitute.
                    yield return new WaitForSecondsRealtime(1f);
                    ConversationMoodProbe.Require(!bridge.HasPendingSpeech,
                        "Completed request still owns pending audio before the next pilot stage.");
                }

                // Exercise the real transcript-row click path twice against the same
                // completed answer. This proves the UI reuses its recorded WAV and
                // records separate playback receipts; it does not synthesize again.
                if (actorIndex == 0)
                {
                    int replayReceipts = 0;
                    yield return VerifyTranscriptReplayAndCumulativeHighlight(host, talk, bridge,
                        actor.Resident.Id, sessionId, actorEvidence.LastRequestId,
                        value => replayReceipts += value);
                    actorEvidence.PlayedLines += replayReceipts;
                }

                // Only close after both actual replies reached measured completed playback.
                bridge.EndConversation(sessionId);
                yield return ConversationMoodProbe.CloseTalk(host);
                activeSession[0] = null;
                ConversationMoodProbe.Require(!bridge.HasPendingSpeech,
                    "Closing the fully played conversation left pending trial speech.");
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private static IEnumerator WaitForRealPlayback(BreezeTtsTrialBridge bridge, string requestId,
            string personId, string sessionId, string expectedText, Action<PlaybackMeasurement> completed)
        {
            ConversationMoodProbe.Require(bridge != null && !string.IsNullOrWhiteSpace(requestId),
                "Cannot wait on a missing TTS request ID.");
            string boundPerson, boundSession, boundText;
            ConversationMoodProbe.Require(bridge.TryReadRequestBinding(requestId, out boundPerson, out boundSession, out boundText) &&
                boundPerson == personId && boundSession == sessionId &&
                (expectedText == null || boundText == expectedText),
                "TTS request ownership or transcript text did not match the active native conversation.");
            bool observedPlaying = false;
            float deadline = Time.realtimeSinceStartup + 360f;
            while (Time.realtimeSinceStartup < deadline)
            {
                BreezeTtsTrialBridge.RequestState state = bridge.GetRequestState(requestId);
                if (state == BreezeTtsTrialBridge.RequestState.Playing) observedPlaying = true;
                if (state == BreezeTtsTrialBridge.RequestState.Cancelled || state == BreezeTtsTrialBridge.RequestState.Failed ||
                    state == BreezeTtsTrialBridge.RequestState.Unknown)
                    throw new InvalidOperationException("TTS request " + requestId + " ended without playback: " + state + ".");
                if (state == BreezeTtsTrialBridge.RequestState.Completed)
                {
                    string json;
                    ConversationMoodProbe.Require(bridge.TryReadLatestPlaybackMeasurement(requestId, out json) && !string.IsNullOrWhiteSpace(json),
                        "Completed TTS request has no native playback measurement.");
                    PlaybackMeasurement measurement = JsonUtility.FromJson<PlaybackMeasurement>(json);
                    ConversationMoodProbe.Require(measurement != null && measurement.schemaVersion == 1 &&
                        measurement.requestId == requestId && measurement.personId == personId &&
                        measurement.conversationToken == sessionId && measurement.status == "completed" &&
                        measurement.playbackStartMonotonicTicks > 0 && measurement.timestampFrequency > 0 &&
                        measurement.mixerCallbackFrames > 0 && measurement.mixerCallbackSamples > 0 &&
                        measurement.nonZeroOutputSamples > 0 && measurement.peakOutputAmplitudeMicrounits > 0 &&
                        measurement.expectedSampleFrames > 0 && measurement.sampleRate > 0 && measurement.channels > 0 &&
                        measurement.playbackEndMonotonicTicks > measurement.playbackStartMonotonicTicks &&
                        measurement.expectedPlaybackMilliseconds > 0 &&
                        measurement.observedPlaybackMilliseconds >= measurement.expectedPlaybackMilliseconds -
                            Math.Min(100, Math.Max(1, measurement.expectedPlaybackMilliseconds / 50)) &&
                        (observedPlaying || measurement.playbackStartMonotonicTicks > measurement.textReadyMonotonicTicks),
                        "Native TTS playback lacked full-duration completion or nonzero Unity audio output evidence.");
                    completed(measurement);
                    yield break;
                }
                yield return null;
            }
            throw new TimeoutException("TTS request " + requestId + " did not complete Unity playback within six minutes.");
        }

        private static IEnumerator VerifyTranscriptReplayAndCumulativeHighlight(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, BreezeTtsTrialBridge bridge, string personId, string sessionId,
            string requestId, Action<int> completedReplayCount)
        {
            ConversationMoodProbe.Require(!string.IsNullOrEmpty(requestId) && bridge.GetPlaybackIndex(requestId) == 1,
                "Replay check requires one initially completed, exact request.");
            ListBox transcript = ReadTranscript(talk);
            FieldInfo bindingsField = typeof(PeopleOfTheIliacBayMod).GetField("trialTranscriptBindings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            IDictionary bindings = bindingsField == null ? null : bindingsField.GetValue(host) as IDictionary;
            object binding = bindings == null ? null : bindings[transcript];
            ConversationMoodProbe.Require(binding != null, "Transcript has no request-to-row replay binding.");
            IDictionary requestByRow = ReadMember(binding, "RequestByRow") as IDictionary;
            IDictionary highlightByRow = ReadMember(binding, "HighlightByRow") as IDictionary;
            ConversationMoodProbe.Require(requestByRow != null && highlightByRow != null,
                "Transcript replay binding does not expose its exact row maps.");

            ListBox.ListItem targetRow = null;
            int targetCenterY = -1;
            int contentY = 0;
            for (int index = 0; index < transcript.Count; index++)
            {
                ListBox.ListItem row = transcript.GetItem(index);
                if (row == null || row.textLabel == null) continue;
                int rowHeight = row.textLabel.TextHeight;
                string boundRequest = requestByRow[row] as string;
                int centerY = contentY + rowHeight / 2;
                int clickY = centerY - transcript.ScrollIndex;
                if (boundRequest == requestId && row.textColor != DaggerfallUI.DaggerfallQuestionTextColor &&
                    clickY >= 0 && clickY < transcript.Rectangle.height)
                { targetRow = row; targetCenterY = clickY; }
                contentY += rowHeight + transcript.RowSpacing;
            }
            ConversationMoodProbe.Require(targetRow != null,
                "Exact replay-bound NPC answer row is not visible in the native transcript viewport.");

            MethodInfo findRow = typeof(PeopleOfTheIliacBayMod).GetMethod("FindClickedTranscriptRow",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo onClick = typeof(PeopleOfTheIliacBayMod).GetMethod("OnTrialTranscriptMouseClick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            ConversationMoodProbe.Require(findRow != null && onClick != null,
                "Native transcript click/replay handlers are unavailable.");
            int initialPlaybackIndex = bridge.GetPlaybackIndex(requestId);
            int completedCount = 0;
            for (int replay = 0; replay < 2; replay++)
            {
                Vector2 clickPosition = new Vector2(4f, targetCenterY);
                ListBox.ListItem resolvedRow = findRow.Invoke(null, new object[] { transcript, clickPosition }) as ListBox.ListItem;
                ConversationMoodProbe.Require(ReferenceEquals(resolvedRow, targetRow),
                    "Native transcript click coordinate did not resolve the exact mapped NPC row.");
                onClick.Invoke(host, new object[] { transcript, clickPosition });
                int expectedIndex = initialPlaybackIndex + replay + 1;
                ConversationMoodProbe.Require(bridge.GetPlaybackIndex(requestId) == expectedIndex,
                    "Transcript click did not create the next retained-WAV replay attempt.");
                PlaybackMeasurement measurement = null;
                yield return WaitForRealPlayback(bridge, requestId, personId, sessionId, null,
                    value => measurement = value);
                ConversationMoodProbe.Require(measurement != null && bridge.GetPlaybackIndex(requestId) == expectedIndex,
                    "Replay did not finish with a distinct playback receipt/index.");
                completedCount++;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_REPLAY_PASS: request=" + requestId +
                    "; transcript-row=exact-occurrence; playback-index=" + expectedIndex +
                    "; status=" + measurement.status + "; mixer-frames=" + measurement.mixerCallbackFrames + ".");

                if (replay == 1)
                    yield return WaitForCumulativeWordHighlight(transcript, highlightByRow, targetRow,
                        bridge, requestId, expectedIndex, personId, sessionId);
            }
            if (completedReplayCount != null) completedReplayCount(completedCount);
        }

        private static IEnumerator WaitForCumulativeWordHighlight(ListBox transcript, IDictionary highlightByRow,
            ListBox.ListItem row, BreezeTtsTrialBridge bridge, string requestId, int playbackIndex,
            string personId, string sessionId)
        {
            object overlay = highlightByRow[row];
            ConversationMoodProbe.Require(overlay != null,
                "Exact NPC transcript row has no cumulative word-highlight renderer.");
            Type type = overlay.GetType();
            FieldInfo timelineField = type.GetField("fullWavTimeline", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo receiptField = type.GetField("completedFullWavReceiptValid", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo observedIndexField = type.GetField("fullWavObservedPlaybackIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo lastFrameField = type.GetField("fullWavLastObservedSampleFrame", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo highWaterField = type.GetField("currentCumulativeTextEnd", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo progressSourceField = type.GetField("currentProgressSource", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo estimateUsedField = type.GetField("usedTextWeightedEstimateThisPlayback", BindingFlags.Instance | BindingFlags.NonPublic);
            ConversationMoodProbe.Require(timelineField != null && receiptField != null && observedIndexField != null &&
                lastFrameField != null && highWaterField != null && progressSourceField != null && estimateUsedField != null,
                "Cumulative word-highlight renderer state is unavailable to the native acceptance probe.");

            float deadline = Time.realtimeSinceStartup + 30f;
            WordAlignmentTimelineDocument timeline = null;
            bool receiptValid = false;
            int observedIndex = -1;
            long lastFrame = 0;
            int highWater = 0;
            string progressSource = "none";
            bool estimateUsed = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                timeline = timelineField.GetValue(overlay) as WordAlignmentTimelineDocument;
                receiptValid = Convert.ToBoolean(receiptField.GetValue(overlay), CultureInfo.InvariantCulture);
                observedIndex = Convert.ToInt32(observedIndexField.GetValue(overlay), CultureInfo.InvariantCulture);
                lastFrame = Convert.ToInt64(lastFrameField.GetValue(overlay), CultureInfo.InvariantCulture);
                highWater = Convert.ToInt32(highWaterField.GetValue(overlay), CultureInfo.InvariantCulture);
                progressSource = Convert.ToString(progressSourceField.GetValue(overlay), CultureInfo.InvariantCulture);
                estimateUsed = Convert.ToBoolean(estimateUsedField.GetValue(overlay), CultureInfo.InvariantCulture);
                if (receiptValid && observedIndex == playbackIndex && highWater >= row.textLabel.Text.Length) break;
                yield return null;
            }
            bool timelineValid = timeline != null && timeline.text == row.textLabel.Text &&
                timeline.coverage != null && timeline.words != null && timeline.words.Length > 0 &&
                timeline.coverage.matchedTokens == timeline.coverage.transcriptTokens &&
                timeline.words.Length == timeline.coverage.matchedTokens;
            ConversationMoodProbe.Require(receiptValid && observedIndex == playbackIndex &&
                lastFrame > 0 && highWater >= row.textLabel.Text.Length &&
                (progressSource == "completed-playback-receipt" || progressSource == "forced-word-alignment") &&
                (timeline == null || timelineValid),
                "Completed replay did not retain its exact receipt and cumulative all-text highlight state.");

            // At the validated terminal cursor, the renderer's cumulative predicate
            // makes every word eligible. Capture the native row for visual review;
            // this probe asserts receipt/timeline inputs, not screenshot pixel colors.
            string directory = Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay");
            Directory.CreateDirectory(directory);
            string screenshot = Path.Combine(directory, "breeze-word-highlight-replay-complete-" + requestId + ".png");
            ScreenCapture.CaptureScreenshot(screenshot);
            yield return null;
            string boundPerson, boundSession, boundText;
            ConversationMoodProbe.Require(bridge.TryReadRequestBinding(requestId, out boundPerson, out boundSession, out boundText) &&
                boundPerson == personId && boundSession == sessionId && boundText == row.textLabel.Text,
                "Completed cumulative highlight no longer matches the exact replay binding.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_WORD_HIGHLIGHT_CUMULATIVE_READY: request=" + requestId +
                "; person=" + personId + "; session=" + sessionId + "; replay-index=" + playbackIndex +
                "; progress-source=" + progressSource + "; estimated-progress-used=" + estimateUsed +
                "; validated-alignment=" + timelineValid + "; cumulative-text-end=" + highWater +
                "; text-length=" + row.textLabel.Text.Length + "; all-words-eligible=true; screenshot=" + screenshot +
                "; pixel-color-assertion=false.");
        }

        private static IEnumerator VerifyQwenReady(LocalDialogueConnection connection, string expectedPreviousModelId,
            Action<string> completed)
        {
            ConversationMoodProbe.Require(connection != null && connection.Endpoint != null && connection.Endpoint.Port == 8080,
                "Qwen readiness check requires the actual configured loopback port 8080 connection.");
            float readinessDeadline = Time.realtimeSinceStartup + 90f;
            int attempt = 0;
            while (Time.realtimeSinceStartup < readinessDeadline)
            {
                attempt++;
                var check = new QwenCheck();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Uri healthUri = new Uri(connection.Endpoint, "/health");
                        Uri modelsUri = new Uri(connection.Endpoint, "/v1/models");
                        check.HealthJson = GetJson(healthUri);
                        check.ModelJson = GetJson(modelsUri);
                    }
                    catch (Exception error)
                    {
                        string diagnostic = error.GetType().Name + ":" + error.Message;
                        if (IsTransientQwenReadinessError(error)) check.TransientError = diagnostic;
                        else check.Error = diagnostic;
                    }
                    finally { check.Done = true; }
                });
                float attemptDeadline = Math.Min(readinessDeadline, Time.realtimeSinceStartup + 10f);
                while (!check.Done && Time.realtimeSinceStartup < attemptDeadline) yield return null;
                if (!check.Done)
                {
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_QWEN_RELOAD_RETRY: attempt=" + attempt +
                        "; reason=readiness-check-timeout; retry-window-seconds=" +
                        Math.Max(0, (int)(readinessDeadline - Time.realtimeSinceStartup)) + ".");
                }
                else if (!string.IsNullOrEmpty(check.TransientError))
                {
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_QWEN_RELOAD_RETRY: attempt=" + attempt +
                        "; transient=" + check.TransientError + "; retry-window-seconds=" +
                        Math.Max(0, (int)(readinessDeadline - Time.realtimeSinceStartup)) + ".");
                }
                else
                {
                    ConversationMoodProbe.Require(string.IsNullOrEmpty(check.Error), "Qwen readiness check failed: " + check.Error);
                    HealthReply health = JsonUtility.FromJson<HealthReply>(check.HealthJson);
                    ConversationMoodProbe.Require(health != null && health.status == "ok",
                        "Qwen /health did not report status=ok.");
                    string modelShape;
                    string modelId = ReadSoleQwenModelId(check.ModelJson, out modelShape);
                    if (string.IsNullOrWhiteSpace(modelId))
                    {
                        string response = check.ModelJson ?? "<null>";
                        if (response.Length > 512) response = response.Substring(0, 512) + "...";
                        Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_QWEN_MODEL_PARSE_FAIL: shape=" + modelShape +
                            "; response=" + response);
                        throw new InvalidOperationException("Qwen /v1/models did not report exactly one active model ID; decoded-shape=" + modelShape + ".");
                    }
                    if (!string.IsNullOrEmpty(expectedPreviousModelId) &&
                        !string.Equals(expectedPreviousModelId, modelId, StringComparison.Ordinal))
                    {
                        Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_QWEN_MODEL_CHANGED: expected=" +
                            expectedPreviousModelId + "; actual=" + modelId + ".");
                        throw new InvalidOperationException("Qwen model ID changed during the pilot; expected=" +
                            expectedPreviousModelId + "; actual=" + modelId + ". Refusing another conversation prompt.");
                    }
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_QWEN_READY: endpoint=" + connection.Endpoint +
                        "; active-model-id=" + modelId + "; model-list-shape=" + modelShape + "; attempt=" + attempt + ".");
                    completed(modelId);
                    yield break;
                }
                if (Time.realtimeSinceStartup < readinessDeadline) yield return new WaitForSecondsRealtime(2f);
            }
            throw new TimeoutException("Qwen /health and /v1/models did not recover within the 90-second readiness window.");
        }

        private static bool IsTransientQwenReadinessError(Exception error)
        {
            for (Exception current = error; current != null; current = current.InnerException)
            {
                WebException web = current as WebException;
                if (web != null)
                {
                    if (web.Status == WebExceptionStatus.ConnectFailure || web.Status == WebExceptionStatus.Timeout ||
                        web.Status == WebExceptionStatus.ConnectionClosed) return true;
                    HttpWebResponse response = web.Response as HttpWebResponse;
                    if (response != null && response.StatusCode == HttpStatusCode.ServiceUnavailable) return true;
                }
                SocketException socket = current as SocketException;
                if (socket != null && (socket.SocketErrorCode == SocketError.ConnectionRefused ||
                    socket.SocketErrorCode == SocketError.TimedOut || socket.SocketErrorCode == SocketError.ConnectionReset))
                    return true;
            }
            return false;
        }

        private static string ReadSoleQwenModelId(string json, out string shape)
        {
            shape = "unparsed";
            if (string.IsNullOrWhiteSpace(json)) { shape = "empty-response"; return null; }
            try
            {
                FullSerializer.fsData root = FullSerializer.fsJsonParser.Parse(json);
                if (root == null || !root.IsDictionary) { shape = "root-not-object"; return null; }
                FullSerializer.fsData data;
                if (!root.AsDictionary.TryGetValue("data", out data)) { shape = "missing-data"; return null; }
                if (data == null || !data.IsList) { shape = "data-not-array"; return null; }
                if (data.AsList.Count != 1) { shape = "data-count=" + data.AsList.Count; return null; }
                FullSerializer.fsData row = data.AsList[0];
                if (row == null || !row.IsDictionary) { shape = "data[0]-not-object"; return null; }
                FullSerializer.fsData id;
                if (!row.AsDictionary.TryGetValue("id", out id)) { shape = "data[0]-missing-id"; return null; }
                if (id == null || !id.IsString || string.IsNullOrWhiteSpace(id.AsString))
                { shape = "data[0]-id-not-nonempty-string"; return null; }
                shape = "object/data-array[1]/id-string";
                return id.AsString;
            }
            catch (Exception error)
            {
                shape = "parse-error:" + error.GetType().Name;
                return null;
            }
        }

        private static string GetJson(Uri uri)
        {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET";
            request.Timeout = 3000;
            request.ReadWriteTimeout = 3000;
            request.KeepAlive = false;
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException(uri.AbsolutePath + " returned HTTP " + (int)response.StatusCode + ".");
                using (var reader = new StreamReader(response.GetResponseStream())) return reader.ReadToEnd();
            }
        }

        private static LocalDialogueConnection ReadQwenConnection(PeopleOfTheIliacBayMod host)
        {
            MethodInfo factory = ConversationMoodProbe.RequireMethod(typeof(PeopleOfTheIliacBayMod), "NewDialogueConnection", 0);
            return factory.Invoke(host, null) as LocalDialogueConnection;
        }

        private static ListBox ReadTranscript(DaggerfallTalkWindow talk)
        {
            FieldInfo field = typeof(DaggerfallTalkWindow).GetField("listboxConversation",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            ListBox transcript = field == null ? null : field.GetValue(talk) as ListBox;
            ConversationMoodProbe.Require(transcript != null, "Native Talk transcript is unavailable for exact reply binding.");
            return transcript;
        }

        private static bool TranscriptContainsFinalizedReply(ListBox transcript, int startIndex,
            string playerText, string finalizedText, out string mismatch)
        {
            mismatch = "missing-exact-player-row";
            bool foundPlayer = false;
            var reply = new StringBuilder();
            int replyRows = 0;
            string normalizedPlayer = NormalizeTranscriptText(playerText);
            for (int i = startIndex; i < transcript.Count; i++)
            {
                var row = transcript.GetItem(i);
                if (row == null || row.textLabel == null || string.IsNullOrWhiteSpace(row.textLabel.Text)) continue;
                if (row.textColor == DaggerfallUI.DaggerfallQuestionTextColor)
                {
                    string rowText = NormalizeTranscriptText(row.textLabel.Text);
                    if (rowText == normalizedPlayer) { foundPlayer = true; continue; }
                    if (foundPlayer) break;
                }
                else if (foundPlayer)
                {
                    if (replyRows > 0) reply.Append(' ');
                    reply.Append(NormalizeTranscriptText(row.textLabel.Text));
                    replyRows++;
                }
            }
            if (!foundPlayer) return false;
            if (replyRows == 0) { mismatch = "player-row-without-npc-reply-row"; return false; }
            string displayedText = NormalizeTranscriptText(reply.ToString());
            string normalizedFinal = NormalizeTranscriptText(finalizedText);
            if (!string.Equals(displayedText, normalizedFinal, StringComparison.Ordinal))
            {
                mismatch = "displayed-finalized-text-mismatch; displayed=" + BoundedText(displayedText) +
                    "; finalized=" + BoundedText(normalizedFinal);
                return false;
            }
            mismatch = null;
            return true;
        }

        private static string NormalizeTranscriptText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var normalized = new StringBuilder(value.Length);
            bool pendingSpace = false;
            foreach (char character in value)
            {
                if (char.IsWhiteSpace(character))
                {
                    pendingSpace = normalized.Length > 0;
                    continue;
                }
                if (pendingSpace) normalized.Append(' ');
                normalized.Append(character);
                pendingSpace = false;
            }
            return normalized.ToString();
        }

        private static string DescribeTranscriptRows(ListBox transcript, int startIndex)
        {
            if (transcript == null) return "<null>";
            var description = new StringBuilder();
            for (int i = Math.Max(0, startIndex); i < transcript.Count; i++)
            {
                var row = transcript.GetItem(i);
                if (row == null || row.textLabel == null) continue;
                if (description.Length > 0) description.Append(" || ");
                description.Append(i).Append(row.textColor == DaggerfallUI.DaggerfallQuestionTextColor ? ":player:" : ":npc:")
                    .Append(row.textLabel.Name).Append(':').Append(BoundedText(NormalizeTranscriptText(row.textLabel.Text)));
                if (description.Length > 3000) { description.Append(" || <truncated>"); break; }
            }
            return description.ToString();
        }

        private static string BoundedText(string value)
        {
            if (value == null) return "<null>";
            return value.Length <= 240 ? value : value.Substring(0, 240) + "...";
        }

        private static ResidentActor FindUnusedAdultWithNoConversation(WorldStateStore store,
            Vector3 playerPosition, string excludedPersonId)
        {
            ResidentActor best = null;
            float bestDistance = float.MaxValue;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach (ResidentActor candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!candidate || !candidate.gameObject.activeInHierarchy || !candidate.Person || candidate.Resident == null ||
                    candidate.Resident.Id == excludedPersonId || !candidate.Resident.IsAlive || candidate.Resident.IsQuestProtected ||
                    candidate.Resident.AgeGroup != ResidentAgeGroup.Adult || candidate.IsSleeping || candidate.IsSeated || candidate.IsConversing ||
                    store.ActiveNpcAction(candidate.Resident.Id) != null || store.ActiveNpcTask(candidate.Resident.Id) != null) continue;
                ConversationMoodView mood = store.CaptureConversationMood(candidate.Resident.Id);
                if (mood.Value != 0 || mood.AcceptedTurnSequence != 0) continue;
                if (store.ConversationTranscript(candidate.Resident.Id, now, 1).Count != 0) continue;
                float distance = Vector3.Distance(candidate.transform.position, playerPosition);
                if (distance < bestDistance) { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = target.GetType().GetProperty(name, flags);
            if (property != null) return property.GetValue(target, null);
            FieldInfo field = target.GetType().GetField(name, flags);
            return field == null ? null : field.GetValue(target);
        }
    }
}
