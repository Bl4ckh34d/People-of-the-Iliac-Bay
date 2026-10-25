using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Verifies native live PCM acceptance with scripted text and the configured TTS coordinator.</summary>
    internal static class BreezeTtsStreamingAcceptanceProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-tts-streaming-acceptance-probe";
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string BaselineSave = "People - Gothway Garden";
        private const string FirstSentence = "The street is quiet tonight. ";
        private const string SecondSentence = "I will keep watch until dawn.";
        private const string CancellationSentence = "This scripted sample checks cancellation while native PCM is playing.";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable]
        private sealed class LivePcmEvidence
        {
            public int schemaVersion = 0;
            public string normalizer = null;
            public bool outputMatchesLiveSpool = false;
            public int sampleRate = 0;
            public int channels = 0;
            public int sampleWidthBytes = 0;
            public int sampleFrames = 0;
            public string outputPcmSha256 = null;
            public string wavSha256 = null;
        }

        [Serializable]
        private sealed class StreamEvidence
        {
            public int schemaVersion = 0;
            public string requestId = null;
            public string conversationToken = null;
            public string personId = null;
            public string finalizedText = null;
            public string finalizedTextSha256 = null;
            public string wavSha256 = null;
            public string profileId = null;
            public LivePcmEvidence livePcmNormalization = null;
        }

        private sealed class AudioSourceSnapshot
        {
            public AudioClip Clip;
            public float Volume;
            public float Pitch;
            public bool Loop;
            public bool PlayOnAwake;
            public float SpatialBlend;
            public bool Mute;
            public int Priority;
        }

        private sealed class RunState
        {
            public BreezeTtsStreamingPcmAdapter Adapter;
            public BreezeTtsStreamingPcmAdapter.PlaybackReceipt LastReceipt;
            public BreezeTtsTrialBridge Bridge;
            public AudioSource AudioSource;
            public AudioSourceSnapshot AudioSnapshot;
            public object OriginalMixerCounters;
            public object MixerCounters;
            public FieldInfo MixerCountersField;
            public FieldInfo MixerFramesField;
            public FieldInfo MixerSamplesField;
            public FieldInfo MixerNonZeroField;
            public bool CreatedAdapter;
            public string ResponseRoot;
            public string ActiveToken;
            public string PersonId;
            public Vector3 PlayerPosition;
            public Quaternion PlayerRotation;
            public bool GreetingPending;
            public float GreetingRetryAt;
        }

        private sealed class SubjectSearchCounts
        {
            public int Seen;
            public int Inactive;
            public int MissingActorData;
            public int Unmanaged;
            public int Dead;
            public int QuestProtected;
            public int NonAdult;
            public int Busy;
            public int MissingIdentity;
            public int InvalidSex;
            public int UnknownRace;
            public int Eligible;

            public string Summary()
            {
                return "seen=" + Seen + "; inactive=" + Inactive + "; missing-actor-data=" + MissingActorData +
                    "; unmanaged=" + Unmanaged + "; dead=" + Dead + "; quest-protected=" + QuestProtected +
                    "; non-adult=" + NonAdult + "; busy=" + Busy + "; missing-identity=" + MissingIdentity +
                    "; invalid-sex=" + InvalidSex + "; unknown-race=" + UnknownRace + "; eligible=" + Eligible;
            }
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            ConversationMoodProbe.Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0 &&
                character == OwnedCharacter && fixtureSave == BaselineSave && GameManager.Instance &&
                GameManager.Instance.IsReady && GameManager.Instance.PlayerEntity != null &&
                GameManager.Instance.PlayerEntity.Name == OwnedCharacter,
                "TTS streaming acceptance requires its opt-in flag and exact owned disposable fixture.");
            ConversationMoodProbe.Require(host != null && streets != null && streets.Ready,
                "TTS streaming acceptance requires the active mod and ready resident population.");
            ConversationMoodProbe.Require(GameManager.Instance.PlayerMotor != null,
                "TTS streaming acceptance requires the native player motor for exact position restoration.");
            ConversationMoodProbe.Require(GameManager.Instance.SaveLoadManager.FindSaveFolderByNames(
                character, fixtureSave) >= 0, "Exact TTS streaming acceptance baseline save is missing.");

            BreezeTtsTrialBridge bridge = ConversationMoodProbe.ReadHostField(host, "breezeTtsTrial") as BreezeTtsTrialBridge;
            bool bridgeFound = bridge != null;
            bool productionBridge = bridgeFound && !bridge.IsTrialMode;
            bool voiceEnabled = bridgeFound && bridge.VoiceEnabled;
            bool sentenceStreamingEnabled = bridgeFound && bridge.SocialStreamingEnabled;
            string configuredEndpoint = bridgeFound ? bridge.ConfiguredServerEndpoint : string.Empty;
            bool endpointConfigured = !string.IsNullOrWhiteSpace(configuredEndpoint);
            bool pendingSpeech = bridgeFound && bridge.HasPendingSpeech;
            AudioSource configuredAudioSource = bridgeFound ? bridge.OutputAudioSource : null;
            bool audioSourceAvailable = configuredAudioSource != null;
            bool audioSourcePlaying = audioSourceAvailable && configuredAudioSource.isPlaying;
            string audioClipName = !audioSourceAvailable || configuredAudioSource.clip == null ?
                "<null>" : configuredAudioSource.clip.name;
            int audioSourceInstanceId = audioSourceAvailable ? configuredAudioSource.GetInstanceID() : 0;
            string activeRequestId = bridgeFound ? bridge.ActiveRequestId ?? "<none>" : "<unavailable>";
            string mixerCounterState = DescribeMixerCounters(bridge);
            if (!bridgeFound || !productionBridge || !voiceEnabled || !sentenceStreamingEnabled ||
                !endpointConfigured || pendingSpeech || !audioSourceAvailable || audioSourcePlaying)
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TTS_STREAMING_ACCEPTANCE_GUARD: " +
                    "bridge-present=" + bridgeFound + "; production=" + productionBridge +
                    "; voice-enabled=" + voiceEnabled + "; sentence-streaming-enabled=" + sentenceStreamingEnabled +
                    "; endpoint-configured=" + endpointConfigured + "; endpoint=" +
                    (string.IsNullOrWhiteSpace(configuredEndpoint) ? "<empty>" : configuredEndpoint) +
                    "; pending-speech=" + pendingSpeech + "; audio-source-present=" + audioSourceAvailable +
                    "; audio-source-playing=" + audioSourcePlaying + "; clip=" + audioClipName +
                    "; play-on-awake=" + (audioSourceAvailable && configuredAudioSource.playOnAwake) +
                    "; source-id=" + audioSourceInstanceId + "; active-request=" + activeRequestId +
                    "; mixer-counters=" + mixerCounterState + ".");
            ConversationMoodProbe.Require(bridge != null && !bridge.IsTrialMode && bridge.VoiceEnabled &&
                bridge.SocialStreamingEnabled && !string.IsNullOrWhiteSpace(bridge.ConfiguredServerEndpoint) &&
                !bridge.HasPendingSpeech && bridge.OutputAudioSource != null && !bridge.OutputAudioSource.isPlaying,
                "TTS streaming acceptance requires idle production voice settings and a configured local SSE endpoint.");
            ConversationMoodProbe.Require(!configuredAudioSource.isPlaying,
                "TTS output AudioSource was not idle after bridge initialization.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TTS_STREAMING_SOURCE_IDLE_PASS: source-id=" +
                audioSourceInstanceId + "; clip=" + audioClipName + "; play-on-awake=" +
                configuredAudioSource.playOnAwake + "; is-playing=" + configuredAudioSource.isPlaying +
                "; active-request=" + activeRequestId + "; mixer-counters=" + mixerCounterState + ".");
            string responseRoot;
            ConversationMoodProbe.Require(bridge.TryGetStreamingResponseRoot(out responseRoot) &&
                Path.IsPathRooted(responseRoot), "Production bridge has no trusted streaming response root.");

            ResidentActor actor = null;
            SubjectSearchCounts searchCounts = null;
            float subjectWaitStarted = Time.realtimeSinceStartup;
            float subjectDeadline = subjectWaitStarted + 45f;
            while (actor == null && Time.realtimeSinceStartup < subjectDeadline)
            {
                actor = FindSubject(streets, out searchCounts);
                if (actor != null) break;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            ConversationMoodProbe.Require(actor != null && actor.Resident != null && actor.Resident.IsAlive &&
                actor.Person != null,
                "No active eligible adult resident spawned within 45 seconds. Last selection counts: " +
                (searchCounts == null ? "not scanned" : searchCounts.Summary()));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TTS_STREAMING_SUBJECT_SELECTED: person=" + actor.Resident.Id +
                "; name=" + actor.Person.NameNPC + "; wait-seconds=" +
                (Time.realtimeSinceStartup - subjectWaitStarted).ToString("F2", CultureInfo.InvariantCulture) +
                "; selection-counts=" + searchCounts.Summary() + ".");

            RunState state = new RunState
            {
                Bridge = bridge,
                AudioSource = bridge.OutputAudioSource,
                ResponseRoot = Path.GetFullPath(responseRoot),
                PersonId = actor.Resident.Id,
                PlayerPosition = GameManager.Instance.PlayerMotor.transform.position,
                PlayerRotation = GameManager.Instance.PlayerMotor.transform.rotation,
                GreetingPending = Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host,
                    "returningGreetingPending"), CultureInfo.InvariantCulture),
                GreetingRetryAt = Convert.ToSingle(ConversationMoodProbe.ReadHostField(host,
                    "returningGreetingRetryAt"), CultureInfo.InvariantCulture)
            };
            CaptureAudioSource(state);
            if (state.AudioSource.mute || state.AudioSource.volume <= 0f)
                throw new InvalidOperationException("Native TTS output source is muted or has zero volume.");

            state.Adapter = bridge.GetComponent<BreezeTtsStreamingPcmAdapter>();
            if (state.Adapter == null)
            {
                state.Adapter = bridge.gameObject.AddComponent<BreezeTtsStreamingPcmAdapter>();
                state.CreatedAdapter = true;
            }
            ConversationMoodProbe.Require(state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Idle,
                "TTS streaming adapter is not idle; refusing to replace active or prior playback state.");
            CaptureMixerCounter(state);
            Action<BreezeTtsStreamingPcmAdapter.PlaybackReceipt> finished = receipt => state.LastReceipt = receipt;
            state.Adapter.PlaybackFinished += finished;

            Exception failure = null;
            IEnumerator work = RunTwoStreamAcceptance(host, streets, actor, state);
            try
            {
                while (true)
                {
                    bool more = false;
                    object current = null;
                    try { more = work.MoveNext(); if (more) current = work.Current; }
                    catch (Exception error) { failure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                ConversationMoodProbe.Dispose(work, ref failure);
                if (!string.IsNullOrEmpty(state.ActiveToken))
                {
                    bridge.EndConversation(state.ActiveToken);
                    state.Adapter.CleanupConversationResponses(state.ActiveToken);
                    state.ActiveToken = null;
                }
                if (state.Adapter != null && state.Adapter.State != BreezeTtsStreamingPcmAdapter.PlaybackState.Idle &&
                    state.Adapter.State != BreezeTtsStreamingPcmAdapter.PlaybackState.Completed &&
                    state.Adapter.State != BreezeTtsStreamingPcmAdapter.PlaybackState.Cancelled &&
                    state.Adapter.State != BreezeTtsStreamingPcmAdapter.PlaybackState.Failed)
                    state.Adapter.CancelFromOwner("acceptance-probe-cleanup");
                RestoreAudioSource(state);
                RestoreMixerCounter(state);
                GameManager.Instance.PlayerMotor.transform.position = state.PlayerPosition;
                GameManager.Instance.PlayerMotor.transform.rotation = state.PlayerRotation;
                Physics.SyncTransforms();
                typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingPending", PrivateInstance)
                    .SetValue(host, state.GreetingPending);
                typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingRetryAt", PrivateInstance)
                    .SetValue(host, state.GreetingRetryAt);
                if (state.Adapter != null) state.Adapter.PlaybackFinished -= finished;
                if (state.CreatedAdapter && state.Adapter != null) UnityEngine.Object.Destroy(state.Adapter);
            }

            if (failure != null)
                throw new InvalidOperationException("Native scripted PCM streaming acceptance failed.", failure);
        }

        private static IEnumerator RunTwoStreamAcceptance(PeopleOfTheIliacBayMod host,
            StreetPopulation streets, ResidentActor actor, RunState state)
        {
            string firstToken = Guid.NewGuid().ToString("N");
            string firstStream = BeginStream(host, actor, state, firstToken);
            state.ActiveToken = firstToken;
            AppendSentence(state, firstStream, firstToken, 0, FirstSentence);
            long beforeFirstOutput = ReadCounter(state.MixerNonZeroField, state.MixerCounters);
            yield return WaitForFirstSentencePlayback(state, firstStream, firstToken, beforeFirstOutput);

            ConversationMoodProbe.Require(!File.Exists(RequestDirectory(state, firstStream, "complete.json")) &&
                state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Playing &&
                state.Adapter.CompletedSentenceText.IndexOf(FirstSentence.Trim(), StringComparison.Ordinal) >= 0,
                "First native sentence did not play completely while the input stream remained open.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TTS_STREAMING_FIRST_SENTENCE_AUDIBLE: stream=" + firstStream +
                "; person=" + state.PersonId + "; source=scripted-fixed-text; qwen-called=false; " +
                "; mixer-nonzero-samples=" + ReadCounter(state.MixerNonZeroField, state.MixerCounters) + ".");

            AppendSentence(state, firstStream, firstToken, 1, SecondSentence);
            yield return WaitForSecondSentencePlayback(state, firstStream, firstToken);
            string transcript = FirstSentence + SecondSentence;
            ConversationMoodProbe.Require(state.Bridge.CompleteStreamingReply(firstStream, firstToken, state.PersonId, transcript),
                "Production bridge rejected the completed exact scripted transcript.");
            yield return WaitForCompletedStream(state, firstStream, firstToken, transcript);
            VerifyReplay(state, firstStream, firstToken, transcript);

            state.Bridge.EndConversation(firstToken);
            state.Adapter.CleanupConversationResponses(firstToken);
            state.ActiveToken = null;
            yield return WaitForResponseCleanup(state, firstStream);

            string cancelToken = Guid.NewGuid().ToString("N");
            string cancelStream = BeginStream(host, actor, state, cancelToken);
            state.ActiveToken = cancelToken;
            AppendSentence(state, cancelStream, cancelToken, 0, CancellationSentence);
            long beforeCancelOutput = ReadCounter(state.MixerNonZeroField, state.MixerCounters);
            yield return WaitForAudibleStreamStart(state, beforeCancelOutput);
            ConversationMoodProbe.Require(!File.Exists(RequestDirectory(state, cancelStream, "complete.json")),
                "Cancellation stream was finalized before conversation close.");

            state.Bridge.EndConversation(cancelToken);
            state.ActiveToken = null;
            yield return WaitForCancelledTerminal(state, cancelStream, cancelToken);
            state.Adapter.CleanupConversationResponses(cancelToken);
            yield return WaitForResponseCleanup(state, cancelStream);
            ConversationMoodProbe.Require(state.LastReceipt != null && state.LastReceipt.Status == "cancelled" &&
                File.Exists(RequestDirectory(state, cancelStream, "cancel.json")),
                "Conversation close did not preserve a bound cancel receipt and request marker.");

            ConversationMoodProbe.Require(!state.Bridge.HasPendingSpeech && !state.AudioSource.isPlaying,
                "Native cancellation left speech queued or audio playing.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TTS_STREAMING_ACCEPTANCE_PASS: mode=scripted-native-pcm-not-llm-streaming; " +
                "person=" + state.PersonId + "; distinct-sentences=2; second-appended-after-first-audible=true; " +
                "completed-replay-pcm-hash-and-frames-matched=true; mixer-nonzero-samples=" +
                ReadCounter(state.MixerNonZeroField, state.MixerCounters) + "; conversation-close-cancel-cleanup=true; qwen-called=false.");
        }

        private static string BeginStream(PeopleOfTheIliacBayMod host, ResidentActor actor,
            RunState state, string token)
        {
            ConversationMoodProbe.Require(!state.Bridge.HasPendingSpeech && !state.AudioSource.isPlaying,
                "Previous stream or other TTS playback still owns the audio source.");
            BreezeTtsTrialBridge.VoiceIdentity identity = BuildNativeIdentity(actor);
            string id;
            ConversationMoodProbe.Require(state.Bridge.TryBeginStreamingReply(actor.Resident.Id,
                actor.Person.NameNPC, token, identity.sex.value, null, identity, out id) &&
                !string.IsNullOrWhiteSpace(id),
                "Configured production bridge rejected a scripted live PCM stream begin.");
            InstallFreshMixerCounter(state);
            state.ActiveToken = token;
            state.LastReceipt = null;
            ConversationMoodProbe.Require(state.Adapter.Begin(state.Bridge, state.ResponseRoot,
                id, token, actor.Resident.Id), "Native PCM adapter rejected the stream binding.");
            return id;
        }

        private static BreezeTtsTrialBridge.VoiceIdentity BuildNativeIdentity(ResidentActor actor)
        {
            string sex = actor.Resident.Sex == ResidentSex.Female ? "female" : "male";
            string age = actor.Resident.AgeGroup == ResidentAgeGroup.Child ? "child" :
                actor.Resident.AgeGroup == ResidentAgeGroup.Adult ? "adult" : "unknown";
            string race = actor.Person.Race.ToString();
            bool raceKnown = !string.IsNullOrWhiteSpace(race) &&
                !string.Equals(race, "unknown", StringComparison.OrdinalIgnoreCase);
            return new BreezeTtsTrialBridge.VoiceIdentity
            {
                schemaVersion = 1,
                sex = new BreezeTtsTrialBridge.VoiceIdentity.Attribute
                    { value = sex, known = true, confidence = "unknown", source = "saved-native-resident-sex" },
                race = new BreezeTtsTrialBridge.VoiceIdentity.Attribute
                    { value = raceKnown ? race : "unknown", known = raceKnown, confidence = "unknown", source = "saved-native-resident-race" },
                age = new BreezeTtsTrialBridge.VoiceIdentity.Attribute
                    { value = age, known = age != "unknown", confidence = age == "unknown" ? "unknown" : "high", source = "saved-resident-age-group" },
                origin = new BreezeTtsTrialBridge.VoiceIdentity.Attribute
                    { value = "unknown", known = false, confidence = "unknown", source = "no-explicit-home-provenance" }
            };
        }

        private static void AppendSentence(RunState state, string streamId, string token,
            int sequence, string sentence)
        {
            ConversationMoodProbe.Require(state.Adapter.TryQueueSentence(sequence, sentence),
                "Native adapter rejected scripted sentence queue entry " + sequence + ".");
            ConversationMoodProbe.Require(state.Bridge.TryAppendStreamingSentence(streamId, token,
                state.PersonId, sequence, sentence), "Production bridge rejected scripted sentence " + sequence + ".");
        }

        private static IEnumerator WaitForFirstSentencePlayback(RunState state, string streamId,
            string token, long previousNonZeroSamples)
        {
            float deadline = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Failed ||
                    state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Cancelled)
                    throw new InvalidOperationException("First scripted stream ended before its first sentence played. " +
                        MixerDiagnostic(state, streamId));
                if (state.Adapter.CompletedSentenceText.IndexOf(FirstSentence.Trim(), StringComparison.Ordinal) >= 0 &&
                    state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Playing &&
                    ReadCounter(state.MixerNonZeroField, state.MixerCounters) > previousNonZeroSamples)
                    break;
                yield return null;
            }
            ConversationMoodProbe.Require(Time.realtimeSinceStartup < deadline && state.AudioSource.isPlaying &&
                MixerCounterIsCurrent(state) &&
                state.Adapter.PlaybackCursorFrames > 0 &&
                ReadCounter(state.MixerSamplesField, state.MixerCounters) > 0 &&
                ReadCounter(state.MixerNonZeroField, state.MixerCounters) > previousNonZeroSamples &&
                !File.Exists(RequestDirectory(state, streamId, "complete.json")) &&
                state.Adapter.ConversationToken == token,
                "First sentence did not reach nonzero mixer output before the stream terminal marker. " +
                MixerDiagnostic(state, streamId));
        }

        private static IEnumerator WaitForSecondSentencePlayback(RunState state, string streamId, string token)
        {
            string expected = SecondSentence.Trim();
            float deadline = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Failed ||
                    state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Cancelled)
                    throw new InvalidOperationException("Second scripted stream ended before playback. " +
                        MixerDiagnostic(state, streamId));
                if (state.Adapter.CompletedSentenceText.IndexOf(expected, StringComparison.Ordinal) >= 0 &&
                    state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Playing) break;
                yield return null;
            }
            ConversationMoodProbe.Require(Time.realtimeSinceStartup < deadline && state.AudioSource.isPlaying &&
                MixerCounterIsCurrent(state) &&
                !File.Exists(RequestDirectory(state, streamId, "complete.json")) &&
                state.Adapter.ConversationToken == token,
                "Second sentence did not play while native stream input remained open. " +
                MixerDiagnostic(state, streamId));
        }

        private static IEnumerator WaitForCompletedStream(RunState state, string streamId,
            string token, string transcript)
        {
            float deadline = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < deadline &&
                state.Adapter.State != BreezeTtsStreamingPcmAdapter.PlaybackState.Completed)
            {
                if (state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Failed ||
                    state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Cancelled)
                    throw new InvalidOperationException("Completed scripted stream ended in " + state.Adapter.State + ".");
                yield return null;
            }
            BreezeTtsStreamingPcmAdapter.PlaybackReceipt receipt = state.LastReceipt;
            ConversationMoodProbe.Require(Time.realtimeSinceStartup < deadline && receipt != null &&
                receipt.Status == "completed" && receipt.StreamId == streamId &&
                receipt.ConversationToken == token && receipt.PersonId == state.PersonId &&
                !receipt.InterruptedMidSentence &&
                state.Adapter.CompletedSentenceText.IndexOf(FirstSentence.Trim(), StringComparison.Ordinal) >= 0 &&
                state.Adapter.CompletedSentenceText.IndexOf(SecondSentence.Trim(), StringComparison.Ordinal) >= 0 &&
                transcript == FirstSentence + SecondSentence,
                "Native adapter did not drain both exact completed sentence PCM segments.");
        }

        private static void VerifyReplay(RunState state, string streamId, string token, string transcript)
        {
            BreezeTtsTrialBridge.ActiveAudioEvidence audio;
            ConversationMoodProbe.Require(state.Bridge.TryReadStreamingFinalAudioEvidence(streamId,
                token, state.PersonId, out audio) && audio != null && audio.Text == transcript &&
                audio.SampleRate == 24000 && audio.Channels == 1,
                "Completed stream lacks a bound replay WAV for the exact transcript.");
            string trialRoot = Directory.GetParent(state.ResponseRoot).FullName;
            string evidencePath = Path.Combine(trialRoot, "measurements", streamId + ".evidence.json");
            FileInfo info = new FileInfo(evidencePath);
            ConversationMoodProbe.Require(info.Exists && info.Length >= 2 && info.Length <= 256 * 1024,
                "Coordinator final stream evidence is missing or outside the size limit.");
            string evidenceJson = File.ReadAllText(evidencePath);
            FullSerializer.fsData evidenceData = FullSerializer.fsJsonParser.Parse(evidenceJson);
            ConversationMoodProbe.Require(evidenceData != null && evidenceData.IsDictionary,
                "Coordinator final stream evidence root is not an object.");
            StreamEvidence evidence = null;
            FullSerializer.fsResult evidenceResult = new FullSerializer.fsSerializer().TryDeserialize(evidenceData, ref evidence);
            ConversationMoodProbe.Require(!evidenceResult.Failed && evidence != null,
                "Coordinator final stream evidence could not be deserialized with nested fields.");
            ConversationMoodProbe.Require(evidence != null && evidence.schemaVersion == 1 &&
                evidence.requestId == streamId && evidence.conversationToken == token &&
                evidence.personId == state.PersonId && evidence.finalizedText == transcript &&
                evidence.livePcmNormalization != null && evidence.livePcmNormalization.schemaVersion == 1 &&
                evidence.livePcmNormalization.outputMatchesLiveSpool &&
                evidence.livePcmNormalization.sampleRate == 24000 &&
                evidence.livePcmNormalization.channels == 1 &&
                evidence.livePcmNormalization.sampleWidthBytes == 2 &&
                !string.IsNullOrWhiteSpace(evidence.profileId),
                "Coordinator evidence does not bind native stream identity, configured profile, and live PCM.");

            byte[] wavBytes = File.ReadAllBytes(audio.WavePath);
            BreezeTtsTrialWave wave;
            ConversationMoodProbe.Require(BreezeTtsTrialProtocol.TryDecodePcm16Wave(wavBytes, out wave) &&
                wave != null && wave.SampleRate == 24000 && wave.Channels == 1 &&
                wave.SampleFrames == audio.SampleFrames && wave.SampleFrames == evidence.livePcmNormalization.sampleFrames,
                "Completed replay WAV format or sample frames differ from streamed PCM evidence.");
            string waveHash = Hash(wavBytes);
            ConversationMoodProbe.Require(string.Equals(waveHash, evidence.wavSha256, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(waveHash, evidence.livePcmNormalization.wavSha256, StringComparison.OrdinalIgnoreCase),
                "Completed replay WAV SHA-256 differs from coordinator final evidence.");
            int dataOffset, dataLength;
            ConversationMoodProbe.Require(TryFindWaveData(wavBytes, out dataOffset, out dataLength) &&
                dataLength == wave.SampleFrames * 2 &&
                string.Equals(Hash(wavBytes, dataOffset, dataLength), evidence.livePcmNormalization.outputPcmSha256,
                    StringComparison.OrdinalIgnoreCase),
                "Completed replay PCM hash or byte count differs from the exact live normalized spool.");
            ConversationMoodProbe.Require(wave.Samples != null && Array.Exists(wave.Samples, sample => Math.Abs(sample) > 0.000001f),
                "Completed replay WAV contains no nonzero PCM samples.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TTS_STREAMING_REPLAY_PASS: stream=" + streamId +
                "; sample-rate=" + wave.SampleRate + "; channels=" + wave.Channels +
                "; sample-frames=" + wave.SampleFrames + "; wav-sha256=" + waveHash +
                "; pcm-sha256=" + evidence.livePcmNormalization.outputPcmSha256 +
                "; profile=" + evidence.profileId + ".");
        }

        private static IEnumerator WaitForResponseCleanup(RunState state, string streamId)
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline &&
                (Directory.Exists(Path.Combine(state.ResponseRoot, streamId)) ||
                 File.Exists(Path.Combine(state.ResponseRoot, streamId + ".wav")) ||
                 File.Exists(Path.Combine(state.ResponseRoot, streamId + ".manifest.json")) ||
                 File.Exists(Path.Combine(state.ResponseRoot, streamId + ".alignment.json")) ||
                 File.Exists(Path.Combine(state.ResponseRoot, streamId + ".visemes.json"))))
                yield return null;
            ConversationMoodProbe.Require(!Directory.Exists(Path.Combine(state.ResponseRoot, streamId)) &&
                !File.Exists(Path.Combine(state.ResponseRoot, streamId + ".wav")) &&
                !File.Exists(Path.Combine(state.ResponseRoot, streamId + ".manifest.json")) &&
                !File.Exists(Path.Combine(state.ResponseRoot, streamId + ".alignment.json")) &&
                !File.Exists(Path.Combine(state.ResponseRoot, streamId + ".visemes.json")),
                "Closed completed conversation retained its exact streaming response artifacts: " +
                DescribeRemainingResponseArtifacts(state.ResponseRoot, streamId));
        }

        private static string DescribeRemainingResponseArtifacts(string responseRoot, string streamId)
        {
            var paths = new List<string>();
            string streamDirectory = Path.Combine(responseRoot, streamId);
            try
            {
                if (Directory.Exists(streamDirectory))
                {
                    string[] entries = Directory.GetFileSystemEntries(streamDirectory, "*", SearchOption.AllDirectories);
                    for (int index = 0; index < entries.Length && index < 32; index++) paths.Add(entries[index]);
                    if (entries.Length > 32) paths.Add("<more-than-32-stream-entries>");
                }
                string[] suffixes = { ".wav", ".manifest.json", ".alignment.json", ".visemes.json" };
                for (int index = 0; index < suffixes.Length; index++)
                {
                    string path = Path.Combine(responseRoot, streamId + suffixes[index]);
                    if (File.Exists(path)) paths.Add(path + " (" + new FileInfo(path).Length + " bytes)");
                }
            }
            catch (Exception error) { paths.Add("<enumeration-failed:" + error.GetType().Name + ">"); }
            return paths.Count == 0 ? "<no-stream-artifacts-found>" : string.Join("; ", paths.ToArray());
        }

        private static IEnumerator WaitForAudibleStreamStart(RunState state, long previousNonZeroSamples)
        {
            float deadline = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Failed ||
                    state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Cancelled)
                    throw new InvalidOperationException("Cancellation stream failed before audible playback. " +
                        MixerDiagnostic(state, state.Adapter.StreamId));
                if (state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Playing &&
                    ReadCounter(state.MixerNonZeroField, state.MixerCounters) > previousNonZeroSamples) break;
                yield return null;
            }
            ConversationMoodProbe.Require(Time.realtimeSinceStartup < deadline && state.AudioSource.isPlaying &&
                MixerCounterIsCurrent(state) &&
                state.Adapter.PlaybackCursorFrames > 0 &&
                ReadCounter(state.MixerNonZeroField, state.MixerCounters) > previousNonZeroSamples,
                "Cancellation stream did not reach nonzero mixer output. " +
                MixerDiagnostic(state, state.Adapter.StreamId));
        }

        private static IEnumerator WaitForCancelledTerminal(RunState state,
            string streamId, string token)
        {
            string trialRoot = Directory.GetParent(state.ResponseRoot).FullName;
            string processedPath = Path.Combine(trialRoot, "measurements", streamId + ".coordinator-processed.json");
            string cancelledPath = Path.Combine(trialRoot, "measurements", streamId + ".cancelled.json");
            string requestCancelPath = RequestDirectory(state, streamId, "cancel.json");
            string requestCompletePath = RequestDirectory(state, streamId, "complete.json");
            float deadline = Time.realtimeSinceStartup + 45f;
            long previousMixerFrames = -1;
            int stableMixerFrames = 0;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Failed)
                    throw new InvalidOperationException("Conversation-close cancellation changed to adapter failure. " +
                        CancellationDiagnostic(state, streamId, token, requestCancelPath, requestCompletePath,
                            processedPath, cancelledPath, stableMixerFrames));

                CancelRequestEvidence request = ReadCancelRequest(requestCancelPath);
                CoordinatorCancellationEvidence coordinator = ReadCoordinatorCancellation(processedPath, cancelledPath, streamId);
                BreezeTtsStreamingPcmAdapter.PlaybackReceipt receipt = state.LastReceipt;
                bool receiptMatches = receipt != null && receipt.Status == "cancelled" &&
                    receipt.StreamId == streamId && receipt.ConversationToken == token && receipt.PersonId == state.PersonId;
                bool audioStopped = state.AudioSource != null && !state.AudioSource.isPlaying &&
                    state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Cancelled;
                long mixerFrames = ReadCounter(state.MixerFramesField, state.MixerCounters);
                if (audioStopped && mixerFrames == previousMixerFrames) stableMixerFrames++;
                else stableMixerFrames = 0;
                previousMixerFrames = mixerFrames;

                if (request != null && request.schemaVersion == 1 && request.streamId == streamId &&
                    request.conversationToken == token && request.personId == state.PersonId &&
                    request.reason == "conversation-ended" && coordinator != null && receiptMatches &&
                    audioStopped && stableMixerFrames >= 3 && !File.Exists(requestCompletePath)) break;
                yield return null;
            }

            CancelRequestEvidence finalRequest = ReadCancelRequest(requestCancelPath);
            CoordinatorCancellationEvidence finalCoordinator = ReadCoordinatorCancellation(processedPath, cancelledPath, streamId);
            BreezeTtsStreamingPcmAdapter.PlaybackReceipt finalReceipt = state.LastReceipt;
            bool finalReceiptMatches = finalReceipt != null && finalReceipt.Status == "cancelled" &&
                finalReceipt.StreamId == streamId && finalReceipt.ConversationToken == token &&
                finalReceipt.PersonId == state.PersonId;
            ConversationMoodProbe.Require(Time.realtimeSinceStartup < deadline && finalRequest != null &&
                finalRequest.schemaVersion == 1 && finalRequest.streamId == streamId &&
                finalRequest.conversationToken == token && finalRequest.personId == state.PersonId &&
                finalRequest.reason == "conversation-ended" && finalCoordinator != null && finalReceiptMatches &&
                state.Adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Cancelled &&
                state.AudioSource != null && !state.AudioSource.isPlaying && stableMixerFrames >= 3 &&
                !File.Exists(requestCompletePath),
                "Conversation close lacked a bound cancelled request, durable coordinator receipt, or drained native audio. " +
                CancellationDiagnostic(state, streamId, token, requestCancelPath, requestCompletePath,
                    processedPath, cancelledPath, stableMixerFrames));
        }

        private static CancelRequestEvidence ReadCancelRequest(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length < 2 || info.Length > 8192) return null;
                return JsonUtility.FromJson<CancelRequestEvidence>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        private static CoordinatorCancellationEvidence ReadCoordinatorCancellation(
            string processedPath, string cancelledPath, string streamId)
        {
            try
            {
                FileInfo processedInfo = new FileInfo(processedPath);
                FileInfo cancelledInfo = new FileInfo(cancelledPath);
                if (!processedInfo.Exists || processedInfo.Length < 2 || processedInfo.Length > 4096 ||
                    !cancelledInfo.Exists || cancelledInfo.Length < 2 || cancelledInfo.Length > 4096) return null;
                CoordinatorProcessedEvidence processed = JsonUtility.FromJson<CoordinatorProcessedEvidence>(
                    File.ReadAllText(processedPath));
                CoordinatorCancelledEvidence cancelled = JsonUtility.FromJson<CoordinatorCancelledEvidence>(
                    File.ReadAllText(cancelledPath));
                if (processed == null || processed.schemaVersion != 1 || processed.streamId != streamId ||
                    processed.status != "cancelled" || cancelled == null || cancelled.schemaVersion != 1 ||
                    cancelled.streamId != streamId || cancelled.status != "cancelled" ||
                    cancelled.reason != "native-cancel-marker-present") return null;
                return new CoordinatorCancellationEvidence();
            }
            catch { return null; }
        }

        private static string CancellationDiagnostic(RunState state, string streamId, string token,
            string requestCancelPath, string requestCompletePath, string processedPath,
            string cancelledPath, int stableMixerFrames)
        {
            BreezeTtsStreamingPcmAdapter.PlaybackReceipt receipt = state == null ? null : state.LastReceipt;
            return "stream=" + streamId + "; token=" + token + "; person=" + (state == null ? "<none>" : state.PersonId) +
                "; adapter-state=" + (state == null || state.Adapter == null ? "<none>" : state.Adapter.State.ToString()) +
                "; receipt=" + (receipt == null ? "<none>" : receipt.Status + "/" + receipt.StreamId + "/" +
                    receipt.ConversationToken + "/" + receipt.PersonId) +
                "; audio-playing=" + (state != null && state.AudioSource != null && state.AudioSource.isPlaying) +
                "; mixer-stable-frames=" + stableMixerFrames + "; " + MixerDiagnostic(state, streamId) +
                "; cancel-request-exists=" + File.Exists(requestCancelPath) +
                "; complete-request-exists=" + File.Exists(requestCompletePath) +
                "; coordinator-processed-exists=" + File.Exists(processedPath) +
                "; coordinator-cancelled-exists=" + File.Exists(cancelledPath);
        }

        private static string RequestDirectory(RunState state, string streamId, string fileName)
        {
            string trialRoot = Directory.GetParent(state.ResponseRoot).FullName;
            return Path.Combine(trialRoot, "requests", streamId, fileName);
        }

        private static ResidentActor FindSubject(StreetPopulation streets, out SubjectSearchCounts counts)
        {
            ResidentActor found = null;
            counts = new SubjectSearchCounts();
            ResidentActor[] actors = UnityEngine.Object.FindObjectsOfType<ResidentActor>();
            for (int index = 0; index < actors.Length; index++)
            {
                ResidentActor actor = actors[index];
                counts.Seen++;
                if (!actor || !actor.gameObject.activeInHierarchy) { counts.Inactive++; continue; }
                if (actor.Resident == null || !actor.Person) { counts.MissingActorData++; continue; }
                if (string.IsNullOrWhiteSpace(actor.Resident.Id) || string.IsNullOrWhiteSpace(actor.Person.NameNPC))
                { counts.MissingIdentity++; continue; }
                if (streets.FindActor(actor.Resident.Id) != actor) { counts.Unmanaged++; continue; }
                if (!actor.Resident.IsAlive) { counts.Dead++; continue; }
                if (actor.Resident.IsQuestProtected) { counts.QuestProtected++; continue; }
                if (actor.Resident.AgeGroup != ResidentAgeGroup.Adult) { counts.NonAdult++; continue; }
                if (actor.IsSleeping || actor.IsSeated || actor.IsConversing) { counts.Busy++; continue; }
                if (actor.Resident.Sex != ResidentSex.Female && actor.Resident.Sex != ResidentSex.Male)
                { counts.InvalidSex++; continue; }
                string race = actor.Person.Race.ToString();
                if (string.IsNullOrWhiteSpace(race) || string.Equals(race, "unknown", StringComparison.OrdinalIgnoreCase))
                { counts.UnknownRace++; continue; }
                counts.Eligible++;
                if (found == null || string.Compare(actor.Resident.Id, found.Resident.Id,
                        StringComparison.Ordinal) < 0 ||
                    (string.Equals(actor.Resident.Id, found.Resident.Id, StringComparison.Ordinal) &&
                     string.Compare(actor.name, found.name, StringComparison.Ordinal) < 0))
                    found = actor;
            }
            return found;
        }

        private static void CaptureAudioSource(RunState state)
        {
            AudioSource source = state.AudioSource;
            state.AudioSnapshot = new AudioSourceSnapshot { Clip = source.clip, Volume = source.volume,
                Pitch = source.pitch, Loop = source.loop, PlayOnAwake = source.playOnAwake,
                SpatialBlend = source.spatialBlend, Mute = source.mute, Priority = source.priority };
        }

        private static void RestoreAudioSource(RunState state)
        {
            if (state.AudioSource == null) return;
            if (state.AudioSource.isPlaying) state.AudioSource.Stop();
            if (state.Adapter != null)
            {
                MethodInfo reset = typeof(BreezeTtsStreamingPcmAdapter).GetMethod("ResetAudioSource", PrivateInstance);
                if (reset != null) reset.Invoke(state.Adapter, null);
            }
            AudioSourceSnapshot saved = state.AudioSnapshot;
            if (saved == null) return;
            state.AudioSource.clip = saved.Clip;
            state.AudioSource.volume = saved.Volume;
            state.AudioSource.pitch = saved.Pitch;
            state.AudioSource.loop = saved.Loop;
            state.AudioSource.playOnAwake = saved.PlayOnAwake;
            state.AudioSource.spatialBlend = saved.SpatialBlend;
            state.AudioSource.mute = saved.Mute;
            state.AudioSource.priority = saved.Priority;
        }

        private static void CaptureMixerCounter(RunState state)
        {
            state.MixerCountersField = typeof(BreezeTtsTrialBridge).GetField("mixerCounters", PrivateInstance);
            Type counterType = typeof(BreezeTtsTrialBridge).GetNestedType("PlaybackCounters", BindingFlags.NonPublic);
            ConversationMoodProbe.Require(state.MixerCountersField != null && counterType != null,
                "Production Unity mixer playback counter is unavailable.");
            state.MixerFramesField = counterType.GetField("Frames", BindingFlags.Instance | BindingFlags.Public);
            state.MixerSamplesField = counterType.GetField("Samples", BindingFlags.Instance | BindingFlags.Public);
            state.MixerNonZeroField = counterType.GetField("NonZeroSamples", BindingFlags.Instance | BindingFlags.Public);
            ConversationMoodProbe.Require(state.MixerFramesField != null && state.MixerSamplesField != null &&
                state.MixerNonZeroField != null, "Production mixer callback counters changed shape.");
            state.OriginalMixerCounters = state.MixerCountersField.GetValue(state.Bridge);
            InstallFreshMixerCounter(state);
        }

        private static void InstallFreshMixerCounter(RunState state)
        {
            if (state.MixerCountersField == null) throw new InvalidOperationException("Mixer counter field was not captured.");
            state.MixerCounters = Activator.CreateInstance(state.MixerCountersField.FieldType, true);
            state.MixerCountersField.SetValue(state.Bridge, state.MixerCounters);
        }

        private static string MixerDiagnostic(RunState state, string streamId)
        {
            bool exactCounterAttached = MixerCounterIsCurrent(state);
            return "stream=" + (streamId ?? "<none>") +
                "; adapter-state=" + (state.Adapter == null ? "<none>" : state.Adapter.State.ToString()) +
                "; completed-text=" + ((state.Adapter == null ? null : state.Adapter.CompletedSentenceText) ?? string.Empty) +
                "; cursor=" + (state.Adapter == null ? -1 : state.Adapter.PlaybackCursorFrames) +
                "; expected-counter-attached=" + exactCounterAttached +
                "; expected-counter=" + ReadCounter(state.MixerFramesField, state.MixerCounters) + "/" +
                ReadCounter(state.MixerSamplesField, state.MixerCounters) + "/" +
                ReadCounter(state.MixerNonZeroField, state.MixerCounters) +
                "; current-counter=" + DescribeMixerCounters(state.Bridge);
        }

        private static bool MixerCounterIsCurrent(RunState state)
        {
            try
            {
                return state != null && state.MixerCountersField != null && state.MixerCounters != null &&
                    ReferenceEquals(state.MixerCountersField.GetValue(state.Bridge), state.MixerCounters);
            }
            catch { }
            return false;
        }

        private static void RestoreMixerCounter(RunState state)
        {
            if (state.MixerCountersField != null)
                state.MixerCountersField.SetValue(state.Bridge, state.OriginalMixerCounters);
        }

        private static long ReadCounter(FieldInfo field, object counters)
        { return field == null || counters == null ? 0 : Convert.ToInt64(field.GetValue(counters), CultureInfo.InvariantCulture); }

        private static string DescribeMixerCounters(BreezeTtsTrialBridge bridge)
        {
            if (bridge == null) return "<no-bridge>";
            try
            {
                FieldInfo countersField = typeof(BreezeTtsTrialBridge).GetField("mixerCounters", PrivateInstance);
                object counters = countersField == null ? null : countersField.GetValue(bridge);
                if (counters == null) return "<null>";
                Type counterType = counters.GetType();
                FieldInfo frames = counterType.GetField("Frames", BindingFlags.Instance | BindingFlags.Public);
                FieldInfo samples = counterType.GetField("Samples", BindingFlags.Instance | BindingFlags.Public);
                FieldInfo nonZero = counterType.GetField("NonZeroSamples", BindingFlags.Instance | BindingFlags.Public);
                return "frames=" + ReadCounter(frames, counters) + "; samples=" + ReadCounter(samples, counters) +
                    "; nonzero-samples=" + ReadCounter(nonZero, counters);
            }
            catch (Exception error)
            { return "<unavailable:" + error.GetType().Name + ">"; }
        }

        private static bool TryFindWaveData(byte[] wav, out int dataOffset, out int dataLength)
        {
            dataOffset = -1;
            dataLength = 0;
            if (wav == null || wav.Length < 44) return false;
            int cursor = 12;
            while (cursor <= wav.Length - 8)
            {
                long length = (long)wav[cursor + 4] | ((long)wav[cursor + 5] << 8) |
                    ((long)wav[cursor + 6] << 16) | ((long)wav[cursor + 7] << 24);
                if (length < 0 || length > wav.Length - cursor - 8) return false;
                if (wav[cursor] == (byte)'d' && wav[cursor + 1] == (byte)'a' &&
                    wav[cursor + 2] == (byte)'t' && wav[cursor + 3] == (byte)'a')
                {
                    if (dataOffset >= 0) return false;
                    dataOffset = cursor + 8;
                    dataLength = (int)length;
                }
                long next = cursor + 8 + length + (length & 1);
                if (next > wav.Length) return false;
                cursor = (int)next;
            }
            return dataOffset >= 0 && dataLength > 0;
        }

        private static string Hash(byte[] bytes)
        { return Hash(bytes, 0, bytes == null ? 0 : bytes.Length); }

        private static string Hash(byte[] bytes, int offset, int count)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes, offset, count);
                var result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                    result.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        [Serializable]
        private sealed class StreamStatus
        {
            public int schemaVersion = 0;
            public string streamId = null;
            public string conversationToken = null;
            public string personId = null;
            public string status = null;
        }

        [Serializable]
        private sealed class CancelRequestEvidence
        {
            public int schemaVersion = 0;
            public string streamId = null;
            public string conversationToken = null;
            public string personId = null;
            public string reason = null;
        }

        [Serializable]
        private sealed class CoordinatorProcessedEvidence
        {
            public int schemaVersion = 0;
            public string streamId = null;
            public string status = null;
        }

        [Serializable]
        private sealed class CoordinatorCancelledEvidence
        {
            public int schemaVersion = 0;
            public string streamId = null;
            public string status = null;
            public string reason = null;
        }

        private sealed class CoordinatorCancellationEvidence { }
    }
}
