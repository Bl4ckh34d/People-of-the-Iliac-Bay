using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class DecisionRoutingProbe
    {
        private static int appraisalReopenSpeechTurnsPassed;

        private sealed class SpeechStartObservation
        {
            internal BreezeTtsTrialBridge.MixerPlaybackStartReceipt Receipt;
            internal BreezeTtsTrialBridge.ActiveAudioEvidence Evidence;
        }

        private static BreezeTtsTrialBridge RequireSpeechAcceptanceBridge(PeopleOfTheIliacBayMod host)
        {
            BreezeTtsTrialBridge bridge = RequireField(typeof(PeopleOfTheIliacBayMod), "breezeTtsTrial", InstanceMembers)
                .GetValue(host) as BreezeTtsTrialBridge;
            Require(bridge && !bridge.IsTrialMode && bridge.VoiceEnabled && !bridge.SocialStreamingEnabled &&
                !string.IsNullOrWhiteSpace(bridge.ConfiguredServerEndpoint),
                "Speech acceptance requires the enabled production full-WAV Breeze bridge and configured endpoint.");
            RequireSpeechAcceptanceIdle(bridge, "before native appraisal-reopen");
            return bridge;
        }

        private static void RequireSpeechAcceptanceIdle(BreezeTtsTrialBridge bridge, string where)
        {
            Require(bridge && !bridge.HasPendingSpeech && string.IsNullOrEmpty(bridge.ActiveRequestId) &&
                bridge.OutputAudioSource != null && !bridge.OutputAudioSource.isPlaying,
                "Speech acceptance requires an idle production TTS bridge " + where + ".");
        }

        private static IEnumerator DrainExcludedOpeningSpeech(BreezeTtsTrialBridge bridge,
            string requestBeforeTalk, string personId, string journalPath)
        {
            string requestId = bridge.LastQueuedRequestId;
            if (string.Equals(requestId, requestBeforeTalk, StringComparison.Ordinal))
            {
                RequireSpeechAcceptanceIdle(bridge, "after opening Talk");
                AppendJournal(journalPath, "opening_speech_excluded", "\"queued\":false");
                yield break;
            }

            Require(!string.IsNullOrWhiteSpace(requestId),
                "Opening Talk changed the production TTS queue without a request ID.");
            Require(bridge.TryReadRequestBinding(requestId, out string boundPerson, out string token, out string text) &&
                boundPerson == personId && !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(text),
                "Opening greeting request did not bind to the selected resident and conversation.");
            float deadline = Time.realtimeSinceStartup + 300f;
            while (Time.realtimeSinceStartup < deadline &&
                (bridge.GetRequestState(requestId) != BreezeTtsTrialBridge.RequestState.Completed ||
                    bridge.HasPendingSpeech || bridge.OutputAudioSource == null || bridge.OutputAudioSource.isPlaying))
            {
                BreezeTtsTrialBridge.RequestState state = bridge.GetRequestState(requestId);
                Require(state != BreezeTtsTrialBridge.RequestState.Failed && state != BreezeTtsTrialBridge.RequestState.Cancelled,
                    "Opening greeting request failed before it drained.");
                yield return null;
            }
            Require(bridge.GetRequestState(requestId) == BreezeTtsTrialBridge.RequestState.Completed,
                "Opening greeting request did not complete before the speech-acceptance timeout.");
            RequireSpeechAcceptanceIdle(bridge, "after excluded opening greeting drain");
            AppendJournal(journalPath, "opening_speech_excluded", "\"queued\":true,\"requestId\":\"" +
                Json(requestId) + "\",\"personId\":\"" + Json(personId) + "\",\"conversationToken\":\"" +
                Json(token) + "\",\"text\":\"" + Json(text) + "\",\"completedAndDrained\":true");
        }

        private static IEnumerator SubmitLiveSocialTurnWithSpeechAcceptance(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, LocalDecisionSession session, RecordingTransport transport,
            string text, string expectedModelId, string journalPath, BreezeTtsTrialBridge bridge,
            int turnIndex, string expectedPersonId)
        {
            RequireSpeechAcceptanceIdle(bridge, "before accepted turn " + turnIndex.ToString(CultureInfo.InvariantCulture));
            string expectedToken = Convert.ToString(RequireField(typeof(PeopleOfTheIliacBayMod),
                "conversationContextSessionId", InstanceMembers).GetValue(host), CultureInfo.InvariantCulture);
            Require(!string.IsNullOrWhiteSpace(expectedToken) && ReadPerson(host) == expectedPersonId,
                "Speech acceptance turn lacks its current resident or conversation-token binding.");
            int transcriptBefore = TranscriptCount(talk);
            var observations = new List<SpeechStartObservation>();
            Action<BreezeTtsTrialBridge.MixerPlaybackStartReceipt> observer = receipt =>
            {
                if (receipt == null || receipt.PersonId != expectedPersonId) return;
                var observation = new SpeechStartObservation { Receipt = receipt };
                BreezeTtsTrialBridge.ActiveAudioEvidence evidence;
                if (bridge.TryReadActiveAudioEvidence(out evidence)) observation.Evidence = evidence;
                lock (observations) observations.Add(observation);
            };

            bridge.MixerPlaybackStarted += observer;
            try
            {
                var driver = new NestedEnumeratorDriver(SubmitLiveSocialTurn(host, talk, session, transport,
                    text, expectedModelId, journalPath, true));
                try
                {
                    object yielded;
                    while (driver.TryMoveNext(out yielded)) yield return yielded;
                    if (driver.Failure != null) throw driver.Failure;
                }
                finally { driver.Dispose(); }

                string acceptedText = ReadRememberedReply(talk, text, transcriptBefore);
                Require(!string.IsNullOrWhiteSpace(acceptedText) && acceptedText != "<missing>" &&
                    acceptedText != "<unavailable>",
                    "Speech acceptance could not bind the existing native accepted reply text.");
                float deadline = Time.realtimeSinceStartup + 300f;
                SpeechStartObservation matched = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    lock (observations)
                    {
                        if (observations.Count > 1)
                            throw new InvalidOperationException("One accepted reply produced multiple matching full-WAV mixer starts.");
                        if (observations.Count == 1) matched = observations[0];
                    }
                    if (matched != null) break;
                    yield return null;
                }
                Require(matched != null && matched.Receipt != null && matched.Evidence != null,
                    "Accepted reply produced no mixer-start receipt with active validated-WAV evidence.");

                BreezeTtsTrialBridge.MixerPlaybackStartReceipt start = matched.Receipt;
                BreezeTtsTrialBridge.ActiveAudioEvidence audio = matched.Evidence;
                Require(start.PersonId == expectedPersonId && start.ConversationToken == expectedToken &&
                    start.ExactText == acceptedText && !string.IsNullOrWhiteSpace(start.RequestId) &&
                    start.FirstNonZeroMixerTicks > 0 && start.FirstNonZeroMixerFrame >= 0 &&
                    start.SampleRate > 0 && start.SampleFrames > 0 &&
                    audio.RequestId == start.RequestId && audio.PersonId == expectedPersonId &&
                    audio.ConversationToken == expectedToken && audio.Text == acceptedText &&
                    audio.SampleRate == start.SampleRate && audio.SampleFrames == start.SampleFrames &&
                    audio.Channels > 0 && !string.IsNullOrWhiteSpace(audio.WavePath),
                    "Mixer-start receipt, validated WAV metadata, resident, token, or exact accepted text did not match.");

                string boundPerson, boundToken, boundText;
                Require(bridge.TryReadRequestBinding(start.RequestId, out boundPerson, out boundToken, out boundText) &&
                    boundPerson == expectedPersonId && boundToken == expectedToken && boundText == acceptedText,
                    "Completed speech request no longer resolves to its exact production binding.");
                deadline = Time.realtimeSinceStartup + 300f;
                while (Time.realtimeSinceStartup < deadline &&
                    (bridge.GetRequestState(start.RequestId) != BreezeTtsTrialBridge.RequestState.Completed ||
                        bridge.HasPendingSpeech || bridge.OutputAudioSource == null || bridge.OutputAudioSource.isPlaying))
                {
                    BreezeTtsTrialBridge.RequestState state = bridge.GetRequestState(start.RequestId);
                    Require(state != BreezeTtsTrialBridge.RequestState.Failed && state != BreezeTtsTrialBridge.RequestState.Cancelled,
                        "Accepted reply speech request failed or was cancelled before draining.");
                    yield return null;
                }
                Require(bridge.GetRequestState(start.RequestId) == BreezeTtsTrialBridge.RequestState.Completed &&
                    !bridge.HasPendingSpeech && bridge.OutputAudioSource != null && !bridge.OutputAudioSource.isPlaying,
                    "Accepted reply full-WAV request did not complete and drain before the next prompt.");
                long nonZeroSamples = ReadRetainedNonZeroSamples(bridge, start.RequestId);
                Require(nonZeroSamples > 0,
                    "Completed accepted reply has no retained nonzero Unity mixer sample count.");

                RecordingTransport.Result result = transport.LastResult;
                string route = result == null || result.Parsed == null ? "<none>" : result.Parsed.SelectedOptionId ?? "<none>";
                string model = result == null || result.Parsed == null ? "<none>" : result.Parsed.ModelId ?? "<none>";
                AppendJournal(journalPath, "appraisal_reopen_speech_turn", "\"turn\":" +
                    turnIndex.ToString(CultureInfo.InvariantCulture) + ",\"route\":\"" + Json(route) +
                    "\",\"model\":\"" + Json(model) + "\",\"qwenAcceptedText\":\"" + Json(acceptedText) +
                    "\",\"requestId\":\"" + Json(start.RequestId) + "\",\"personId\":\"" + Json(expectedPersonId) +
                    "\",\"conversationToken\":\"" + Json(expectedToken) + "\",\"exactTextMatched\":true," +
                    "\"firstNonZeroMixerTicks\":" + start.FirstNonZeroMixerTicks.ToString(CultureInfo.InvariantCulture) +
                    ",\"firstNonZeroMixerFrame\":" + start.FirstNonZeroMixerFrame.ToString(CultureInfo.InvariantCulture) +
                    ",\"nonZeroMixerSamples\":" + nonZeroSamples.ToString(CultureInfo.InvariantCulture) +
                    ",\"validatedWavePath\":\"" + Json(audio.WavePath) + "\",\"sampleRate\":" +
                    audio.SampleRate.ToString(CultureInfo.InvariantCulture) + ",\"channels\":" +
                    audio.Channels.ToString(CultureInfo.InvariantCulture) + ",\"sampleFrames\":" +
                    audio.SampleFrames.ToString(CultureInfo.InvariantCulture) +
                    ",\"requestCompleted\":true,\"playbackDrained\":true");
                appraisalReopenSpeechTurnsPassed++;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPRAISAL_REOPEN_SPEECH_TURN_PASS: turn=" +
                    turnIndex.ToString(CultureInfo.InvariantCulture) + "; route=" + route + "; model=" + model +
                    "; person=" + expectedPersonId + "; request=" + start.RequestId +
                    "; exactAcceptedText=true; nonzeroMixerSamples=" + nonZeroSamples.ToString(CultureInfo.InvariantCulture) +
                    "; completed=true; drained=true.");
            }
            finally { bridge.MixerPlaybackStarted -= observer; }
        }

        private static long ReadRetainedNonZeroSamples(BreezeTtsTrialBridge bridge, string requestId)
        {
            FieldInfo requestsField = typeof(BreezeTtsTrialBridge).GetField("requestsById", InstanceMembers);
            IDictionary requests = requestsField == null ? null : requestsField.GetValue(bridge) as IDictionary;
            object entry = requests == null ? null : requests[requestId];
            FieldInfo countersField = entry == null ? null : entry.GetType().GetField("Counters", InstanceMembers);
            object counters = countersField == null ? null : countersField.GetValue(entry);
            FieldInfo samplesField = counters == null ? null : counters.GetType().GetField("NonZeroSamples", InstanceMembers);
            Require(samplesField != null, "Bridge does not expose retained playback counters for the completed request.");
            return Convert.ToInt64(samplesField.GetValue(counters), CultureInfo.InvariantCulture);
        }
    }
}
