using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FullSerializer;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;
using UnityEngine;

/// <summary>
/// Opt-in bridge for measuring a local, external Breeze TTS coordinator in game.
/// This pilot sends complete utterances and receives complete WAV files; it is not streaming.
/// </summary>
public sealed class BreezeTtsTrialBridge : MonoBehaviour
{
    public const string LegacySocialStreamingOptInFlag = "-peopleoftheiliacbay-allow-legacy-speech-streaming";
    public enum RequestState { Unknown, Pending, Loading, Ready, Playing, Completed, Cancelled, Failed }
    public event Action<string, string> StreamingCancellationRequested;

    public sealed class MixerPlaybackStartReceipt
    {
        public readonly string RequestId;
        public readonly string ConversationToken;
        public readonly string PersonId;
        public readonly string ExactText;
        public readonly int PlaybackIndex;
        public readonly int SampleRate;
        public readonly int SampleFrames;
        public readonly long FirstNonZeroMixerTicks;
        public readonly long FirstNonZeroMixerFrame;

        internal MixerPlaybackStartReceipt(string requestId, string conversationToken, string personId,
            string exactText, int playbackIndex, int sampleRate, int sampleFrames,
            long firstNonZeroMixerTicks, long firstNonZeroMixerFrame)
        {
            RequestId = requestId;
            ConversationToken = conversationToken;
            PersonId = personId;
            ExactText = exactText;
            PlaybackIndex = playbackIndex;
            SampleRate = sampleRate;
            SampleFrames = sampleFrames;
            FirstNonZeroMixerTicks = firstNonZeroMixerTicks;
            FirstNonZeroMixerFrame = firstNonZeroMixerFrame;
        }
    }

    public sealed class PrePlaybackTerminalReceipt
    {
        public readonly string RequestId;
        public readonly string ConversationToken;
        public readonly string PersonId;
        public readonly string ExactText;
        public readonly RequestState State;
        public readonly string Reason;

        internal PrePlaybackTerminalReceipt(string requestId, string conversationToken, string personId,
            string exactText, RequestState state, string reason)
        {
            RequestId = requestId;
            ConversationToken = conversationToken;
            PersonId = personId;
            ExactText = exactText;
            State = state;
            Reason = reason ?? string.Empty;
        }
    }

    public event Action<MixerPlaybackStartReceipt> MixerPlaybackStarted;
    public event Action<PrePlaybackTerminalReceipt> PrePlaybackTerminated;

    /// <summary>Binding and PCM metadata for one already validated WAV.</summary>
    public sealed class ActiveAudioEvidence
    {
        public readonly string RequestId;
        public readonly string PersonId;
        public readonly string ConversationToken;
        public readonly string Text;
        public readonly string WavePath;
        public readonly int SampleRate;
        public readonly int Channels;
        public readonly int SampleFrames;

        internal ActiveAudioEvidence(string requestId, string personId, string conversationToken,
            string text, string wavePath, int sampleRate, int channels, int sampleFrames)
        {
            RequestId = requestId;
            PersonId = personId;
            ConversationToken = conversationToken;
            Text = text;
            WavePath = wavePath;
            SampleRate = sampleRate;
            Channels = channels;
            SampleFrames = sampleFrames;
        }
    }

    /// <summary>Native, source-attributed speaker identity passed as evidence, never as model authority.</summary>
    [Serializable]
    public sealed class VoiceIdentity
    {
        [Serializable]
        public sealed class Attribute
        {
            public string value = "unknown";
            public bool known;
            public string source = "unknown";
            public string confidence = "unknown";
        }
        public int schemaVersion = 1;
        public Attribute sex = new Attribute();
        public Attribute race = new Attribute();
        public Attribute age = new Attribute();
        public Attribute origin = new Attribute();
    }

    [Serializable]
    private sealed class ProductionConfig
    {
        public int schemaVersion = 0;
        public string bridgeRoot = null;
        public string serverEndpoint = null;
    }

    private sealed class StreamingReply
    {
        public string StreamId;
        public string ConversationToken;
        public string PersonId;
        public string RequestDirectory;
        public int NextSequence;
        public int QueuedCharacters;
        public bool Terminal;
        public bool PlaybackDrained;
        public bool ConversationClosed;
        public string FinalTranscript;
        public int ConsumedSentenceSequence = -1;
        public readonly List<int> SentenceCharacterCounts = new List<int>();
    }

    [Serializable]
    private sealed class StreamingResponseStatus
    {
        public int schemaVersion = 0;
        public string streamId = null;
        public string conversationToken = null;
        public string personId = null;
        public string status = null;
        public int nextChunkSequence = 0;
        public long totalSampleFrames = 0;
        public int consumedSentenceSequence = -1;
        public string finalWavFile = null;
        public string errorCode = null;
    }

    [Serializable]
    private sealed class StreamingFinalEvidence
    {
        public int schemaVersion = 0;
        public string requestId = null;
        public string conversationToken = null;
        public string personId = null;
        public string finalizedText = null;
    }

    [Serializable]
    private sealed class StreamingFinalResponseManifest
    {
        public int schemaVersion = 0;
        public string requestId = null;
        public string conversationToken = null;
        public string personId = null;
        public string encoding = null;
        public string wavFile = null;
        public int sampleRate = 0;
        public int channels = 0;
        public int sampleFrames = 0;
    }

    public const string RunFlag = "-peopleoftheiliacbay-tts-trial";
    public const string ConfigFlag = "-peopleoftheiliacbay-tts-config";
    public const string DefaultConfigFileName = "breeze-tts.json";
    public const string StreamingMode = "streaming-live-pcm-v1";
    private const int MaximumQueuedRequests = 8;
    private const int MaximumStreamingCharacters = 480;
    private const int MaximumRequestsPerTrialDirectory = 256;
    private const float ResponsePollSeconds = 0.2f;
    private const double ResponseTimeoutSeconds = 300.0;
    private const int MaximumManifestCharacters = 4096;
    private const int MaximumFailureTerminalCharacters = 1024;
    private const string RequestDirectoryName = "requests";
    private const string ResponseDirectoryName = "responses";
    private const string MeasurementDirectoryName = "measurements";

    [Serializable]
    private sealed class ResponseManifest
    {
        public int schemaVersion = 0;
        public string requestId = null;
        public string conversationToken = null;
        public string personId = null;
        public string encoding = null;
        public string wavFile = null;
        public int sampleRate = 0;
        public int channels = 0;
        public int sampleFrames = 0;
        public bool presentationReady = false;
    }

    [Serializable]
    private sealed class ResponseFailureTerminal
    {
        public int schemaVersion = 0;
        public string requestId = null;
        public string conversationToken = null;
        public string personId = null;
        public string status = null;
    }

    [Serializable]
    private sealed class PlaybackMeasurement
    {
        public int schemaVersion = 1;
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
        public int callbackGapMarkers = 0;
        public int expectedSampleFrames = 0;
        public int sourceFinalSample = 0;
        public int lastKnownSample = 0;
        public int sampleRate = 0;
        public int channels = 0;
        public long playbackEndMonotonicTicks = 0;
        public long expectedPlaybackMilliseconds = 0;
        public long observedPlaybackMilliseconds = 0;
    }

    private sealed class RequestEntry
    {
        public string RequestId;
        public string ConversationToken;
        public string PersonId;
        public string DisplayName;
        public string Text;
        public long TextReadyTicks;
        public long QueuedTicks;
        public long PlaybackStartTicks;
        public int LastKnownSample;
        public long CancelledAtTicks;
        public bool ConversationClosed;
        public string WavePath;
        public string ManifestPath;
        public string WaveFile;
        public RequestState State;
        public int PlaybackIndex;
        public int SampleRate;
        public int Channels;
        public int SampleFrames;
        public bool Loading;
        public bool ResponseLoadFinished;
        public BreezeTtsTrialWave Wave;
        public bool PresentationRequired;
        public PortraitPreparedPresentation PreparedPresentation;
        public string LoadError;
        public PlaybackCounters Counters;
        public RequestEntry Original;
        public bool MixerPlaybackStartPublished;
        public bool PrePlaybackTerminalPublished;
    }

    private sealed class PlaybackCounters
    {
        public long Frames;
        public long Samples;
        public long NonZeroSamples;
        public long PeakAmplitudeMicrounits;
        public long LastCallbackTicks;
        public long FirstNonZeroMixerTicks;
        public long FirstNonZeroMixerFrame = -1;
        public int GapMarkers;
    }

    private sealed class StreamingMixerObservation
    {
        public string StreamId;
        public string ConversationToken;
        public string PersonId;
        public volatile string ExactText;
        public AudioClip Clip;
        public int SampleRate;
        public int SampleFrames;
        public int PlaybackIndex = 1;
        public volatile bool PlaybackActive;
        public bool StartPublished;
        public readonly PlaybackCounters Counters = new PlaybackCounters();
    }

    private struct LoadedResponse
    {
        public RequestEntry Entry;
        public BreezeTtsTrialWave Wave;
        public PortraitPreparedPresentation PreparedPresentation;
        public string Error;
    }

    private readonly Queue<RequestEntry> queue = new Queue<RequestEntry>();
    private readonly Queue<LoadedResponse> loadedResponses = new Queue<LoadedResponse>();
    private readonly Dictionary<string, RequestEntry> requestsById = new Dictionary<string, RequestEntry>(StringComparer.Ordinal);
    private readonly Dictionary<string, StreamingReply> streamingById = new Dictionary<string, StreamingReply>(StringComparer.Ordinal);
    private readonly object loadedResponseLock = new object();
    private string trialDirectory;
    private string requestDirectory;
    private string responseDirectory;
    private string measurementDirectory;
    private string activeConversationToken;
    private string activePersonId;
    private AudioSource audioSource;
    private AudioClip activeClip;
    private RequestEntry activeEntry;
    private volatile StreamingMixerObservation streamingMixerObservation;
    private RequestEntry pendingReplay;
    private float nextResponsePollTime;
    private float nextClosedArtifactSweepTime;
    private bool shuttingDown;
    private int publishedRequestCount;
    private string lastQueuedRequestId;
    private volatile PlaybackCounters mixerCounters;
    private StreamingReply activeStreamingReply;
    private bool isTrialMode;
    private bool voiceEnabled;
    private bool socialStreamingEnabled;
    private string configuredServerEndpoint;
    private string lastRejectedReason;

    public string LastQueuedRequestId { get { return lastQueuedRequestId; } }
    public string LastRejectedReason { get { return lastRejectedReason ?? string.Empty; } }
    public int PublishedRequestCount { get { return publishedRequestCount; } }
    public bool HasPendingSpeech { get { return activeEntry != null || queue.Count > 0 || pendingReplay != null ||
        activeStreamingReply != null && !activeStreamingReply.PlaybackDrained; } }
    public bool VoiceEnabled { get { return voiceEnabled; } }
    public bool SocialStreamingEnabled { get { return socialStreamingEnabled; } }
    public bool IsTrialMode { get { return isTrialMode; } }
    public string ConfiguredServerEndpoint { get { return configuredServerEndpoint ?? string.Empty; } }
    public AudioSource OutputAudioSource { get { return audioSource; } }
    public string ActiveRequestId { get { return activeEntry != null && activeEntry.State == RequestState.Playing ? activeEntry.RequestId : null; } }
    public string ActivePersonId { get { return activeEntry != null && activeEntry.State == RequestState.Playing ? activeEntry.PersonId : null; } }
    public string ActiveConversationToken { get { return activeEntry != null && activeEntry.State == RequestState.Playing ? activeEntry.ConversationToken : null; } }

    /// <summary>
    /// Creates the bridge only when the explicit trial flag and an absolute directory are supplied.
    /// Expected command line: -peopleoftheiliacbay-tts-trial "C:\\owned\\tts-trial".
    /// </summary>
    public static BreezeTtsTrialBridge TryCreateFromCommandLine(GameObject owner, string[] arguments, out string diagnostic)
    {
        diagnostic = null;
        if (owner == null || arguments == null) { diagnostic = "missing-owner-or-arguments"; return null; }

        int flagIndex = Array.IndexOf(arguments, RunFlag);
        if (flagIndex < 0) return null;
        if (flagIndex + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[flagIndex + 1]))
        { diagnostic = "trial-directory-argument-missing"; return null; }

        string directory = arguments[flagIndex + 1].Trim().Trim('"');
        if (!Path.IsPathRooted(directory)) { diagnostic = "trial-directory-must-be-absolute"; return null; }

        try
        {
            string fullDirectory = Path.GetFullPath(directory);
            string requestPath = Path.Combine(fullDirectory, RequestDirectoryName);
            string responsePath = Path.Combine(fullDirectory, ResponseDirectoryName);
            string measurementPath = Path.Combine(fullDirectory, MeasurementDirectoryName);
            Directory.CreateDirectory(requestPath);
            Directory.CreateDirectory(responsePath);
            Directory.CreateDirectory(measurementPath);
            var bridge = owner.GetComponent<BreezeTtsTrialBridge>();
            if (bridge == null) bridge = owner.AddComponent<BreezeTtsTrialBridge>();
            bridge.Initialize(fullDirectory, true);
            bridge.voiceEnabled = true;
            bridge.socialStreamingEnabled = false;
            diagnostic = "trial-bridge-enabled";
            return bridge;
        }
        catch (Exception error)
        {
            diagnostic = "trial-directory-unavailable:" + error.GetType().Name;
            return null;
        }
    }

    /// <summary>
    /// Loads an explicit user-owned configuration. The config file path is under DFU's
    /// persistent-data directory; bridgeRoot is user-editable and must be absolute.
    /// </summary>
    public static BreezeTtsTrialBridge TryCreateConfigured(GameObject owner, string[] arguments,
        string configDirectory, bool enableVoice, bool enableSocialStreaming, out string diagnostic)
    {
        diagnostic = null;
        if (!enableVoice) return null;
        if (owner == null || arguments == null || string.IsNullOrWhiteSpace(configDirectory))
        { diagnostic = "tts-config-owner-or-directory-missing"; return null; }

        try
        {
            string configPath = ConfigPath(arguments, configDirectory);
            string defaultRoot = Path.Combine(configDirectory, "BreezeTtsBridge");
            if (!File.Exists(configPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                var defaults = new ProductionConfig { schemaVersion = 1, bridgeRoot = defaultRoot,
                    serverEndpoint = "http://127.0.0.1:8094" };
                File.WriteAllText(configPath, JsonUtility.ToJson(defaults, true));
            }

            var info = new FileInfo(configPath);
            if (!info.Exists || info.Length < 2 || info.Length > 8192)
                throw new InvalidDataException("config-file-size-invalid");
            ProductionConfig config = JsonUtility.FromJson<ProductionConfig>(File.ReadAllText(configPath));
            if (config == null || config.schemaVersion != 1 || string.IsNullOrWhiteSpace(config.bridgeRoot))
                throw new InvalidDataException("config-schema-or-bridge-root-invalid");
            if (!Path.IsPathRooted(config.bridgeRoot))
                throw new InvalidDataException("bridgeRoot-must-be-absolute");
            string endpoint = string.IsNullOrWhiteSpace(config.serverEndpoint) ? string.Empty : config.serverEndpoint.Trim();
            if (!ValidLoopbackEndpoint(endpoint))
                throw new InvalidDataException("serverEndpoint-must-be-empty-or-loopback-http-base-url");

            string fullRoot = Path.GetFullPath(config.bridgeRoot);
            Directory.CreateDirectory(fullRoot);
            string requestPath = Path.Combine(fullRoot, RequestDirectoryName);
            string responsePath = Path.Combine(fullRoot, ResponseDirectoryName);
            string measurementPath = Path.Combine(fullRoot, MeasurementDirectoryName);
            Directory.CreateDirectory(requestPath);
            Directory.CreateDirectory(responsePath);
            Directory.CreateDirectory(measurementPath);
            var bridge = owner.GetComponent<BreezeTtsTrialBridge>();
            if (bridge == null) bridge = owner.AddComponent<BreezeTtsTrialBridge>();
            bridge.Initialize(fullRoot, false);
            bridge.voiceEnabled = true;
            bridge.configuredServerEndpoint = endpoint;
            bridge.socialStreamingEnabled = BreezeTtsStreamingPolicy.IsEnabled(enableVoice, enableSocialStreaming,
                !string.IsNullOrEmpty(endpoint), Array.IndexOf(arguments, LegacySocialStreamingOptInFlag) >= 0);
            diagnostic = string.IsNullOrEmpty(endpoint) && enableSocialStreaming
                ? "configured-tts-bridge-enabled;sentence-streaming-requires-server-endpoint" : "configured-tts-bridge-enabled";
            return bridge;
        }
        catch (Exception error)
        {
            diagnostic = "tts-config-unavailable:" + error.GetType().Name;
            return null;
        }
    }

    private static string ConfigPath(string[] arguments, string configDirectory)
    {
        int flagIndex = Array.IndexOf(arguments, ConfigFlag);
        if (flagIndex < 0) return Path.Combine(Path.GetFullPath(configDirectory), DefaultConfigFileName);
        if (flagIndex + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[flagIndex + 1]))
            throw new InvalidDataException("tts-config-override-path-missing");
        string path = arguments[flagIndex + 1].Trim().Trim('"');
        if (!Path.IsPathRooted(path)) throw new InvalidDataException("tts-config-override-must-be-absolute");
        return Path.GetFullPath(path);
    }

    private static bool ValidLoopbackEndpoint(string endpoint)
    {
        if (string.IsNullOrEmpty(endpoint)) return true;
        Uri parsed;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out parsed) || parsed.Scheme != Uri.UriSchemeHttp ||
            !string.IsNullOrEmpty(parsed.UserInfo) || (parsed.AbsolutePath != "/" && parsed.AbsolutePath.Length != 0) ||
            !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment)) return false;
        string host = parsed.Host;
        System.Net.IPAddress address;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            System.Net.IPAddress.TryParse(host, out address) && System.Net.IPAddress.IsLoopback(address);
    }

    /// <summary>Queues one finalized NPC line for the external coordinator.</summary>
    public bool NotifyFinalizedNpcText(string personId, string displayName, string text, string conversationLifetimeToken)
    { string ignored; return NotifyFinalizedNpcText(personId, displayName, text, conversationLifetimeToken, out ignored); }

    public bool NotifyFinalizedNpcText(string personId, string displayName, string text,
        string conversationLifetimeToken, out string requestId)
    { return NotifyFinalizedNpcText(personId, displayName, text, conversationLifetimeToken, null, out requestId); }

    public bool NotifyFinalizedNpcText(string personId, string displayName, string text,
        string conversationLifetimeToken, string voiceGender)
    { string ignored; return NotifyFinalizedNpcText(personId, displayName, text, conversationLifetimeToken, voiceGender, out ignored); }

    public bool NotifyFinalizedNpcText(string personId, string displayName, string text,
        string conversationLifetimeToken, string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext)
    { string ignored; return NotifyFinalizedNpcText(personId, displayName, text, conversationLifetimeToken, voiceGender, deliveryContext, out ignored); }

    public bool NotifyFinalizedNpcText(string personId, string displayName, string text,
        string conversationLifetimeToken, string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext,
        string sampleKind, BreezeTtsTrialControlledDirection controlledDirection)
    { string ignored; return NotifyFinalizedNpcText(personId, displayName, text, conversationLifetimeToken,
        voiceGender, deliveryContext, sampleKind, controlledDirection, null, out ignored); }

    public bool NotifyFinalizedNpcText(string personId, string displayName, string text,
        string conversationLifetimeToken, string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext,
        string sampleKind, BreezeTtsTrialControlledDirection controlledDirection, string contrastLabel)
    { string ignored; return NotifyFinalizedNpcText(personId, displayName, text, conversationLifetimeToken,
        voiceGender, deliveryContext, sampleKind, controlledDirection, contrastLabel, out ignored); }

    public bool NotifyFinalizedNpcText(string personId, string displayName, string text,
        string conversationLifetimeToken, string voiceGender, out string requestId)
    { return NotifyFinalizedNpcText(personId, displayName, text, conversationLifetimeToken, voiceGender, null, out requestId); }

    public bool NotifyFinalizedNpcText(string personId, string displayName, string text,
        string conversationLifetimeToken, string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext, out string requestId)
    { return NotifyFinalizedNpcText(personId, displayName, text, conversationLifetimeToken,
        voiceGender, deliveryContext, null, null, out requestId); }

    public bool NotifyFinalizedNpcText(string personId, string displayName, string text,
        string conversationLifetimeToken, string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext,
        string sampleKind, BreezeTtsTrialControlledDirection controlledDirection, out string requestId)
    { return NotifyFinalizedNpcText(personId, displayName, text, conversationLifetimeToken,
        voiceGender, deliveryContext, sampleKind, controlledDirection, null, out requestId); }

    public bool NotifyFinalizedNpcText(string personId, string displayName, string text,
        string conversationLifetimeToken, string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext,
        string sampleKind, BreezeTtsTrialControlledDirection controlledDirection, string contrastLabel, out string requestId)
    { return NotifyFinalizedNpcTextCore(personId, displayName, text, conversationLifetimeToken, voiceGender,
        deliveryContext, sampleKind, controlledDirection, contrastLabel, null, out requestId); }

    public bool NotifyFinalizedNpcText(string personId, string displayName, string text,
        string conversationLifetimeToken, string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext,
        VoiceIdentity voiceIdentity, out string requestId)
    { return NotifyFinalizedNpcTextCore(personId, displayName, text, conversationLifetimeToken, voiceGender,
        deliveryContext, null, null, null, voiceIdentity, out requestId); }

    private bool NotifyFinalizedNpcTextCore(string personId, string displayName, string text,
        string conversationLifetimeToken, string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext,
        string sampleKind, BreezeTtsTrialControlledDirection controlledDirection, string contrastLabel,
        VoiceIdentity voiceIdentity, out string requestId)
    {
        requestId = null;
        lastRejectedReason = null;
        if (shuttingDown) return RejectFinalizedNpcText("bridge-shutting-down");
        if (string.IsNullOrWhiteSpace(trialDirectory)) return RejectFinalizedNpcText("bridge-root-missing");
        if (!isTrialMode && !voiceEnabled) return RejectFinalizedNpcText("voice-disabled");
        if (activeStreamingReply != null && !activeStreamingReply.PlaybackDrained)
            return RejectFinalizedNpcText("active-stream-not-drained");
        if (string.IsNullOrWhiteSpace(personId) || string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(conversationLifetimeToken))
            return RejectFinalizedNpcText("required-binding-missing");
        if (personId.Length > 240 || displayName.Length > 96 || text.Length > BreezeTtsTrialProtocol.MaximumTextCharacters ||
            conversationLifetimeToken.Length > 128) return RejectFinalizedNpcText("binding-length-invalid");

        if (!string.Equals(activeConversationToken, conversationLifetimeToken, StringComparison.Ordinal) ||
            !string.Equals(activePersonId, personId, StringComparison.Ordinal))
        {
            CancelCurrentConversation("conversation-changed");
            activeConversationToken = conversationLifetimeToken;
            activePersonId = personId;
        }

        if (queue.Count >= MaximumQueuedRequests)
        {
            UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS trial queue full; finalized line was not queued.");
            return RejectFinalizedNpcText("queue-full");
        }
        if (isTrialMode && publishedRequestCount >= MaximumRequestsPerTrialDirectory)
        {
            UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS trial request limit reached for this run.");
            return RejectFinalizedNpcText("trial-request-limit");
        }

        requestId = Guid.NewGuid().ToString("N");
        long readyTicks = Stopwatch.GetTimestamp();
        string json;
        if (!BreezeTtsTrialProtocol.TryBuildRequestJson(requestId, conversationLifetimeToken, personId,
            displayName, text, readyTicks, Stopwatch.Frequency, voiceGender, deliveryContext,
            sampleKind, controlledDirection, contrastLabel, out json))
        { requestId = null; return RejectFinalizedNpcText("protocol-request-invalid"); }
        if (voiceIdentity != null)
        {
            if (voiceIdentity.schemaVersion != 1)
            { requestId = null; return RejectFinalizedNpcText("voice-identity-schema-invalid"); }
            json = json.Substring(0, json.Length - 1) + ",\"voiceIdentity\":" + VoiceIdentityJson(voiceIdentity) + "}";
        }

        try
        {
            BreezeTtsTrialProtocol.WriteAtomically(requestDirectory, requestId + ".request.json", json);
            var entry = new RequestEntry
            {
                RequestId = requestId,
                ConversationToken = conversationLifetimeToken,
                PersonId = personId,
                DisplayName = displayName,
                Text = text,
                TextReadyTicks = readyTicks,
                QueuedTicks = Stopwatch.GetTimestamp(),
                State = RequestState.Pending
            };
            queue.Enqueue(entry);
            requestsById.Add(requestId, entry);
            publishedRequestCount++;
            lastQueuedRequestId = requestId;
            return true;
        }
        catch (Exception error)
        {
            UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS trial request write failed: " + error.GetType().Name);
            requestId = null;
            return false;
        }
    }

    private bool RejectFinalizedNpcText(string reason)
    {
        lastRejectedReason = reason;
        UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TTS_REJECTED: reason=" + reason + ".");
        return false;
    }

    /// <summary>Starts one native-bound live TTS stream before the social LLM begins streaming.</summary>
    public bool TryBeginStreamingReply(string personId, string displayName, string conversationToken,
        string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext, VoiceIdentity voiceIdentity,
        out string streamId)
    {
        streamId = null;
        if (shuttingDown || !voiceEnabled || !socialStreamingEnabled || string.IsNullOrWhiteSpace(trialDirectory) ||
            string.IsNullOrWhiteSpace(personId) || personId.Length > 240 || string.IsNullOrWhiteSpace(displayName) ||
            displayName.Length > 96 || string.IsNullOrWhiteSpace(conversationToken) || conversationToken.Length > 128 ||
            (voiceGender != null && voiceGender != "female" && voiceGender != "male" && voiceGender != "unknown") ||
            (voiceIdentity != null && voiceIdentity.schemaVersion != 1)) return false;
        if (activeEntry != null || queue.Count > 0 || pendingReplay != null) return false;

        if (!string.Equals(activeConversationToken, conversationToken, StringComparison.Ordinal) ||
            !string.Equals(activePersonId, personId, StringComparison.Ordinal))
        {
            CancelCurrentConversation("conversation-changed");
            activeConversationToken = conversationToken;
            activePersonId = personId;
        }
        if (activeStreamingReply != null && !activeStreamingReply.PlaybackDrained) return false;

        string id = Guid.NewGuid().ToString("N");
        string requestStreamDirectory = Path.Combine(requestDirectory, id);
        try
        {
            Directory.CreateDirectory(requestStreamDirectory);
            var json = new System.Text.StringBuilder("{\"schemaVersion\":1,\"mode\":")
                .Append(Quote(StreamingMode)).Append(",\"streamId\":").Append(Quote(id))
                .Append(",\"conversationToken\":").Append(Quote(conversationToken))
                .Append(",\"personId\":").Append(Quote(personId))
                .Append(",\"displayName\":").Append(Quote(displayName))
                .Append(",\"voiceGender\":").Append(Quote(voiceGender ?? "unknown"))
                .Append(",\"sampleRate\":24000,\"channels\":1");
            if (deliveryContext != null) json.Append(",\"deliveryContext\":").Append(DeliveryContextJson(deliveryContext));
            if (voiceIdentity != null) json.Append(",\"voiceIdentity\":").Append(VoiceIdentityJson(voiceIdentity));
            json.Append('}');
            BreezeTtsTrialProtocol.WriteAtomically(requestStreamDirectory, "begin.json", json.ToString());
            var entry = new StreamingReply { StreamId = id, ConversationToken = conversationToken,
                PersonId = personId, RequestDirectory = requestStreamDirectory };
            streamingById.Add(id, entry);
            activeStreamingReply = entry;
            streamId = id;
            return true;
        }
        catch (Exception error)
        {
            UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS stream begin failed: " + error.GetType().Name);
            streamId = null;
            return false;
        }
    }

    /// <summary>Atomically publishes an exact, validated sentence from the active social stream.</summary>
    public bool TryAppendStreamingSentence(string streamId, string conversationToken, string personId,
        int sequence, string exactText)
    {
        StreamingReply entry;
        if (!TryGetActiveStream(streamId, conversationToken, personId, out entry) || sequence != entry.NextSequence ||
            string.IsNullOrWhiteSpace(exactText) || exactText.Length > 240 || ContainsUnsupportedText(exactText)) return false;
        RefreshConsumedSentenceSequence(entry);
        int outstandingCount = entry.NextSequence - (entry.ConsumedSentenceSequence + 1);
        if (outstandingCount >= MaximumQueuedRequests || entry.QueuedCharacters + exactText.Length > MaximumStreamingCharacters) return false;
        try
        {
            string file = sequence.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + ".json";
            string json = "{\"schemaVersion\":1,\"streamId\":" + Quote(entry.StreamId) +
                ",\"conversationToken\":" + Quote(entry.ConversationToken) +
                ",\"personId\":" + Quote(entry.PersonId) +
                ",\"sequence\":" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"exactText\":" + Quote(exactText) +
                ",\"textReadyMonotonicTicks\":" + Stopwatch.GetTimestamp().ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"timestampFrequency\":" + Stopwatch.Frequency.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
            BreezeTtsTrialProtocol.WriteAtomically(entry.RequestDirectory, file, json);
            entry.NextSequence++;
            entry.QueuedCharacters += exactText.Length;
            entry.SentenceCharacterCounts.Add(exactText.Length);
            return true;
        }
        catch (Exception error)
        {
            UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS sentence write failed: " + error.GetType().Name);
            return false;
        }
    }

    public bool CompleteStreamingReply(string streamId, string conversationToken, string personId, string finalTranscript)
    {
        if (!WriteStreamingTerminal(streamId, conversationToken, personId, "complete.json", finalTranscript, null))
            return false;
        BindStreamingMixerText(streamId, conversationToken, personId, finalTranscript, 0);
        return true;
    }

    public bool FailStreamingReply(string streamId, string conversationToken, string personId,
        string heardText, string reasonCode)
    { return WriteStreamingTerminal(streamId, conversationToken, personId, "failed.json", heardText, reasonCode); }

    public bool CancelStreamingReply(string streamId, string conversationToken, string personId, string reasonCode)
    { return WriteStreamingTerminal(streamId, conversationToken, personId, "cancel.json", null, reasonCode); }

    /// <summary>Called by the native PCM adapter after terminal audio is drained or cancelled.</summary>
    public bool MarkStreamingPlaybackDrained(string streamId, string conversationToken, string personId)
    {
        StreamingReply entry;
        if (!TryGetStream(streamId, conversationToken, personId, out entry) || !entry.Terminal) return false;
        entry.PlaybackDrained = true;
        if (ReferenceEquals(activeStreamingReply, entry)) activeStreamingReply = null;
        PruneClosedStreamingReplies();
        return true;
    }

    public bool TryGetStreamingResponseRoot(out string path)
    {
        path = responseDirectory;
        return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);
    }

    private bool WriteStreamingTerminal(string streamId, string conversationToken, string personId,
        string fileName, string text, string reasonCode)
    {
        StreamingReply entry;
        if (!TryGetActiveStream(streamId, conversationToken, personId, out entry)) return false;
        if (fileName == "complete.json" && (string.IsNullOrWhiteSpace(text) || text.Length > BreezeTtsTrialProtocol.MaximumTextCharacters || ContainsUnsupportedText(text))) return false;
        if (fileName == "failed.json" && text != null && (text.Length > MaximumStreamingCharacters || ContainsUnsupportedText(text))) return false;
        if (reasonCode != null && !IsReasonCode(reasonCode)) return false;
        try
        {
            string json = "{\"schemaVersion\":1,\"streamId\":" + Quote(entry.StreamId) +
                ",\"conversationToken\":" + Quote(entry.ConversationToken) +
                ",\"personId\":" + Quote(entry.PersonId);
            if (fileName == "complete.json") json += ",\"finalTranscript\":" + Quote(text);
            else if (fileName == "failed.json") json += ",\"heardText\":" + Quote(text ?? string.Empty);
            if (reasonCode != null) json += (fileName == "cancel.json" ? ",\"reason\":" : ",\"failureKind\":") + Quote(reasonCode);
            json += "}";
            BreezeTtsTrialProtocol.WriteAtomically(entry.RequestDirectory, fileName, json);
            entry.Terminal = true;
            if (fileName == "cancel.json")
            {
                var cancelRequested = StreamingCancellationRequested;
                if (cancelRequested != null)
                {
                    try { cancelRequested(entry.StreamId, entry.ConversationToken); }
                    catch (Exception error) { UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS stream stop callback failed: " + error.GetType().Name); }
                }
                else entry.PlaybackDrained = true;
                if (entry.PlaybackDrained && ReferenceEquals(activeStreamingReply, entry)) activeStreamingReply = null;
                PruneClosedStreamingReplies();
            }
            return true;
        }
        catch (Exception error)
        {
            UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS stream terminal write failed: " + error.GetType().Name);
            return false;
        }
    }

    private bool TryGetActiveStream(string streamId, string token, string personId, out StreamingReply entry)
    {
        return TryGetStream(streamId, token, personId, out entry) && ReferenceEquals(entry, activeStreamingReply) && !entry.Terminal;
    }

    private void RefreshConsumedSentenceSequence(StreamingReply entry)
    {
        if (entry == null || string.IsNullOrEmpty(responseDirectory)) return;
        try
        {
            string path = Path.Combine(responseDirectory, entry.StreamId, "stream.json");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 2 || info.Length > 16384) return;
            StreamingResponseStatus status = JsonUtility.FromJson<StreamingResponseStatus>(File.ReadAllText(path));
            if (status == null || status.schemaVersion != 1 || status.streamId != entry.StreamId ||
                status.conversationToken != entry.ConversationToken || status.personId != entry.PersonId ||
                status.consumedSentenceSequence < entry.ConsumedSentenceSequence ||
                status.consumedSentenceSequence >= entry.NextSequence) return;
            entry.ConsumedSentenceSequence = status.consumedSentenceSequence;
            entry.QueuedCharacters = 0;
            for (int i = entry.ConsumedSentenceSequence + 1; i < entry.SentenceCharacterCounts.Count; i++)
                entry.QueuedCharacters += entry.SentenceCharacterCounts[i];
        }
        catch { /* A partial/invalid coordinator update never frees queue capacity. */ }
    }

    private bool TryGetStream(string streamId, string token, string personId, out StreamingReply entry)
    {
        entry = null;
        return !string.IsNullOrEmpty(streamId) && streamingById.TryGetValue(streamId, out entry) &&
            string.Equals(entry.ConversationToken, token, StringComparison.Ordinal) &&
            string.Equals(entry.PersonId, personId, StringComparison.Ordinal);
    }

    private void CancelStreaming(StreamingReply entry, string reason)
    {
        if (entry == null) return;
        if (!IsReasonCode(reason)) reason = "conversation-ended";
        if (entry.Terminal)
        {
            var cancelTerminal = StreamingCancellationRequested;
            if (cancelTerminal != null)
            {
                try { cancelTerminal(entry.StreamId, entry.ConversationToken); }
                catch (Exception error) { UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS stream stop callback failed: " + error.GetType().Name); }
            }
            else entry.PlaybackDrained = true;
            if (entry.PlaybackDrained && ReferenceEquals(activeStreamingReply, entry)) activeStreamingReply = null;
            PruneClosedStreamingReplies();
            return;
        }
        string json = "{\"schemaVersion\":1,\"streamId\":" + Quote(entry.StreamId) +
            ",\"conversationToken\":" + Quote(entry.ConversationToken) +
            ",\"personId\":" + Quote(entry.PersonId) + ",\"reason\":" + Quote(reason) + "}";
        try
        {
            BreezeTtsTrialProtocol.WriteAtomically(entry.RequestDirectory, "cancel.json", json);
            entry.Terminal = true;
            var cancelRequested = StreamingCancellationRequested;
            if (cancelRequested != null)
            {
                try { cancelRequested(entry.StreamId, entry.ConversationToken); }
                catch (Exception error) { UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS stream stop callback failed: " + error.GetType().Name); }
            }
            else entry.PlaybackDrained = true;
            if (entry.PlaybackDrained && ReferenceEquals(activeStreamingReply, entry)) activeStreamingReply = null;
            PruneClosedStreamingReplies();
        }
        catch (Exception error) { UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS stream cancellation failed: " + error.GetType().Name); }
    }

    private void MarkStreamingConversationClosed(string conversationToken)
    {
        if (string.IsNullOrEmpty(conversationToken)) return;
        foreach (StreamingReply entry in streamingById.Values)
            if (string.Equals(entry.ConversationToken, conversationToken, StringComparison.Ordinal))
                entry.ConversationClosed = true;
        PruneClosedStreamingReplies();
    }

    private void PruneClosedStreamingReplies()
    {
        if (streamingById.Count == 0) return;
        List<string> remove = null;
        foreach (var pair in streamingById)
        {
            StreamingReply entry = pair.Value;
            if (!entry.ConversationClosed || !entry.PlaybackDrained) continue;
            if (activeEntry != null && activeEntry.RequestId == entry.StreamId) continue;
            if (pendingReplay != null && pendingReplay.Original != null && pendingReplay.Original.RequestId == entry.StreamId) continue;
            if (remove == null) remove = new List<string>();
            remove.Add(pair.Key);
        }
        if (remove == null) return;
        foreach (string streamId in remove) streamingById.Remove(streamId);
    }

    private static bool IsReasonCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
        for (int i = 0; i < value.Length; i++)
        { char c = value[i]; if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-') return false; }
        return true;
    }

    private static bool ContainsUnsupportedText(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char value = text[i];
            if (char.IsControl(value) && value != '\n' && value != '\r' && value != '\t') return true;
            if (char.IsHighSurrogate(value))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return true;
                i++;
            }
            else if (char.IsLowSurrogate(value)) return true;
        }
        return false;
    }

    private static string VoiceIdentityJson(VoiceIdentity identity)
    {
        if (identity == null || identity.schemaVersion != 1) return "{\"schemaVersion\":1}";
        return "{\"schemaVersion\":1,\"sex\":" + VoiceIdentityAttributeJson(identity.sex, "sex") +
            ",\"race\":" + VoiceIdentityAttributeJson(identity.race, "race") +
            ",\"age\":" + VoiceIdentityAttributeJson(identity.age, "age") +
            ",\"origin\":" + VoiceIdentityAttributeJson(identity.origin, "origin") + "}";
    }

    private static string VoiceIdentityAttributeJson(VoiceIdentity.Attribute value, string kind)
    {
        if (value == null) value = new VoiceIdentity.Attribute();
        string normalized = ValidMetadata(value.value) ? value.value : "unknown";
        string source = ValidMetadata(value.source) ? value.source : "unknown";
        string confidence = ValidMetadata(value.confidence) ? value.confidence : "unknown";
        bool known = value.known && normalized != "unknown";
        if (kind == "sex" && normalized != "male" && normalized != "female") { normalized = "unknown"; known = false; }
        if (kind == "age" && normalized != "adult" && normalized != "child" && normalized != "young-adult" &&
            normalized != "mature" && normalized != "elderly") { normalized = "unknown"; known = false; }
        if (kind == "origin" && !known) normalized = "unknown";
        if (kind == "age" && confidence != "high" && confidence != "medium" && confidence != "low") confidence = "unknown";
        if (kind == "age")
            return "{\"value\":" + Quote(normalized) + ",\"confidence\":" + Quote(confidence) +
                ",\"source\":" + Quote(source) + "}";
        return "{\"value\":" + Quote(normalized) + ",\"known\":" + (known ? "true" : "false") +
            ",\"source\":" + Quote(source) + "}";
    }

    private static bool ValidMetadata(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96 || ContainsUnsupportedText(value)) return false;
        for (int i = 0; i < value.Length; i++)
        { char c = value[i]; if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-' && c != '_' && c != ':' && c != '.') return false; }
        return true;
    }

    private static string DeliveryContextJson(BreezeTtsTrialDeliveryContext context)
    {
        var json = new System.Text.StringBuilder("{\"schemaVersion\":1,\"personId\":").Append(Quote(context.PersonId))
            .Append(",\"mood\":").Append(context.Mood)
            .Append(",\"dispositionKnown\":").Append(context.DispositionKnown ? "true" : "false")
            .Append(",\"disposition\":").Append(context.Disposition)
            .Append(",\"trustKnown\":").Append(context.TrustKnown ? "true" : "false")
            .Append(",\"trust\":").Append(context.Trust)
            .Append(",\"fear\":").Append(context.Fear)
            .Append(",\"resentment\":").Append(context.Resentment)
            .Append(",\"encounters\":").Append(context.Encounters).Append(",\"traits\":{");
        bool first = true;
        foreach (var trait in context.Traits)
        {
            if (!first) json.Append(',');
            first = false;
            json.Append(Quote(trait.Key)).Append(':').Append(Quote(trait.Value));
        }
        return json.Append("}}").ToString();
    }

    public RequestState GetRequestState(string requestId)
    {
        RequestEntry entry;
        if (string.IsNullOrEmpty(requestId) || !requestsById.TryGetValue(requestId, out entry))
            return RequestState.Unknown;
        if (activeEntry != null && activeEntry.RequestId == requestId) return activeEntry.State;
        if (pendingReplay != null && pendingReplay.RequestId == requestId) return pendingReplay.State;
        return entry.State;
    }

    public bool TryReadRequestBinding(string requestId, out string personId, out string conversationToken)
    {
        string ignored;
        return TryReadRequestBinding(requestId, out personId, out conversationToken, out ignored);
    }

    public bool TryReadRequestBinding(string requestId, out string personId, out string conversationToken, out string text)
    {
        RequestEntry entry;
        if (!string.IsNullOrEmpty(requestId) && requestsById.TryGetValue(requestId, out entry))
        {
            personId = entry.PersonId;
            conversationToken = entry.ConversationToken;
            text = entry.Text;
            return true;
        }
        personId = null;
        conversationToken = null;
        text = null;
        return false;
    }

    /// <summary>Reads binding and validated WAV metadata only while this full-WAV entry is playing.</summary>
    public bool TryReadActiveAudioEvidence(out ActiveAudioEvidence evidence)
    {
        evidence = null;
        RequestEntry entry = activeEntry;
        if (entry == null || entry.State != RequestState.Playing || audioSource == null ||
            !audioSource.isPlaying || audioSource.clip != activeClip || string.IsNullOrEmpty(responseDirectory) ||
            string.IsNullOrEmpty(entry.WaveFile) || string.IsNullOrEmpty(entry.WavePath) ||
            string.IsNullOrEmpty(entry.RequestId) || string.IsNullOrEmpty(entry.PersonId) ||
            string.IsNullOrEmpty(entry.ConversationToken) || string.IsNullOrEmpty(entry.Text) ||
            entry.SampleRate <= 0 || entry.Channels <= 0 || entry.SampleFrames <= 0) return false;

        string validatedPath;
        if (!BreezeTtsTrialProtocol.TryResolveResponseWavePath(responseDirectory, entry.WaveFile, out validatedPath)) return false;
        StringComparison pathComparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetFullPath(entry.WavePath), validatedPath, pathComparison)) return false;
        try
        {
            var info = new FileInfo(validatedPath);
            if (!info.Exists || info.Length < 44 || info.Length > BreezeTtsTrialProtocol.MaximumWaveBytes) return false;
        }
        catch { return false; }

        evidence = new ActiveAudioEvidence(entry.RequestId, entry.PersonId, entry.ConversationToken,
            entry.Text, validatedPath, entry.SampleRate, entry.Channels, entry.SampleFrames);
        return true;
    }

    /// <summary>
    /// Reads exact active full-WAV presentation binding. Prepared mode returns timelines validated before
    /// playback. Legacy mode returns presentationRequired=false for existing asynchronous loaders.
    /// </summary>
    public bool TryReadActivePreparedPresentation(out ActiveAudioEvidence evidence,
        out bool presentationRequired, out PortraitPreparedPresentation presentation)
    {
        evidence = null;
        presentationRequired = false;
        presentation = null;
        if (!TryReadActiveAudioEvidence(out evidence)) return false;

        RequestEntry entry = activeEntry;
        if (entry == null || entry.State != RequestState.Playing ||
            !string.Equals(entry.RequestId, evidence.RequestId, StringComparison.Ordinal) ||
            !string.Equals(entry.PersonId, evidence.PersonId, StringComparison.Ordinal) ||
            !string.Equals(entry.ConversationToken, evidence.ConversationToken, StringComparison.Ordinal) ||
            !string.Equals(entry.Text, evidence.Text, StringComparison.Ordinal) ||
            entry.SampleRate != evidence.SampleRate || entry.Channels != evidence.Channels ||
            entry.SampleFrames != evidence.SampleFrames)
        {
            evidence = null;
            return false;
        }

        presentationRequired = entry.PresentationRequired;
        if (!presentationRequired) return true;

        PortraitPreparedPresentation cached = entry.PreparedPresentation;
        if (cached != null && cached.RequestId == entry.RequestId && cached.PersonId == entry.PersonId &&
            cached.ConversationToken == entry.ConversationToken && cached.Text == entry.Text &&
            cached.SampleRate == entry.SampleRate && cached.AudioFrames == entry.SampleFrames &&
            cached.Visemes != null &&
            (cached.Words == null || cached.Words.SampleRate == entry.SampleRate &&
                cached.Words.AudioFrames == entry.SampleFrames) && cached.Visemes.SampleRate == entry.SampleRate &&
            cached.Visemes.AudioFrames == entry.SampleFrames)
            presentation = cached;
        return true;
    }

    /// <summary>
    /// Reads a completed social stream's final WAV binding. PCM playback identity remains owned
    /// by the streaming adapter; this manifest-backed WAV is retained for row replay.
    /// </summary>
    public bool TryReadStreamingFinalAudioEvidence(string streamId, string conversationToken,
        string personId, out ActiveAudioEvidence evidence)
    {
        evidence = null;
        StreamingReply entry;
        if (!TryGetStream(streamId, conversationToken, personId, out entry) ||
            string.IsNullOrEmpty(responseDirectory) || string.IsNullOrEmpty(measurementDirectory)) return false;

        try
        {
            string streamPath = Path.Combine(responseDirectory, streamId, "stream.json");
            string manifestPath = Path.Combine(responseDirectory, streamId + ".manifest.json");
            string evidencePath = Path.Combine(measurementDirectory, streamId + ".evidence.json");
            string streamJson, manifestJson, evidenceJson;
            if (!TryReadBoundedJson(streamPath, 16384, out streamJson) ||
                !TryReadBoundedJson(manifestPath, MaximumManifestCharacters, out manifestJson) ||
                !TryReadBoundedJson(evidencePath, 256 * 1024, out evidenceJson)) return false;

            StreamingResponseStatus status = JsonUtility.FromJson<StreamingResponseStatus>(streamJson);
            StreamingFinalResponseManifest manifest = JsonUtility.FromJson<StreamingFinalResponseManifest>(manifestJson);
            StreamingFinalEvidence finalEvidence = JsonUtility.FromJson<StreamingFinalEvidence>(evidenceJson);
            if (status == null || status.schemaVersion != 1 || status.streamId != streamId ||
                status.conversationToken != conversationToken || status.personId != personId || status.status != "audio_complete" ||
                manifest == null || manifest.schemaVersion != 1 || manifest.requestId != streamId ||
                manifest.conversationToken != conversationToken || manifest.personId != personId ||
                manifest.encoding != "pcm_s16le" || manifest.sampleRate < 8000 || manifest.sampleRate > 192000 ||
                manifest.channels < 1 || manifest.channels > 2 || manifest.sampleFrames < 1 ||
                manifest.wavFile != status.finalWavFile || finalEvidence == null || finalEvidence.schemaVersion != 1 ||
                finalEvidence.requestId != streamId || finalEvidence.conversationToken != conversationToken ||
                finalEvidence.personId != personId || string.IsNullOrWhiteSpace(finalEvidence.finalizedText) ||
                finalEvidence.finalizedText.Length > BreezeTtsTrialProtocol.MaximumTextCharacters ||
                ContainsUnsupportedText(finalEvidence.finalizedText)) return false;

            string wavePath;
            if (!BreezeTtsTrialProtocol.TryResolveResponseWavePath(responseDirectory, manifest.wavFile, out wavePath)) return false;
            var waveInfo = new FileInfo(wavePath);
            if (!waveInfo.Exists || waveInfo.Length < 44 || waveInfo.Length > BreezeTtsTrialProtocol.MaximumWaveBytes) return false;

            evidence = new ActiveAudioEvidence(streamId, personId, conversationToken, finalEvidence.finalizedText,
                wavePath, manifest.sampleRate, manifest.channels, manifest.sampleFrames);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Queues the retained completed stream WAV through the same decode and playback path as full-text TTS.</summary>
    public RequestState ReplayStreamingReply(string streamId, string conversationToken, string personId)
    {
        StreamingReply stream;
        if (!TryGetStream(streamId, conversationToken, personId, out stream)) return RequestState.Unknown;
        if (!stream.PlaybackDrained) return RequestState.Loading;
        ActiveAudioEvidence evidence;
        if (!TryReadStreamingFinalAudioEvidence(streamId, conversationToken, personId, out evidence))
            return RequestState.Failed;
        RequestEntry existingEntry;
        if (requestsById.TryGetValue(streamId, out existingEntry))
        {
            if (existingEntry.PersonId != personId || existingEntry.ConversationToken != conversationToken ||
                existingEntry.WavePath != evidence.WavePath || existingEntry.Text != evidence.Text)
                return RequestState.Failed;
            return ReplayRequest(streamId, conversationToken);
        }
        string manifestPath = Path.Combine(responseDirectory, streamId + ".manifest.json");
        var replayEntry = new RequestEntry
        {
            RequestId = evidence.RequestId,
            ConversationToken = evidence.ConversationToken,
            PersonId = evidence.PersonId,
            Text = evidence.Text,
            TextReadyTicks = Stopwatch.GetTimestamp(),
            QueuedTicks = Stopwatch.GetTimestamp(),
            WavePath = evidence.WavePath,
            ManifestPath = manifestPath,
            WaveFile = Path.GetFileName(evidence.WavePath),
            SampleRate = evidence.SampleRate,
            Channels = evidence.Channels,
            SampleFrames = evidence.SampleFrames,
            State = RequestState.Ready
        };
        requestsById.Add(streamId, replayEntry);
        return ReplayRequest(streamId, conversationToken);
    }

    private static bool TryReadBoundedJson(string path, int maximumBytes, out string json)
    {
        json = null;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 2 || info.Length > maximumBytes) return false;
        json = File.ReadAllText(path);
        return true;
    }

    private static bool TryReadPresentationReady(string json, out bool presentationReady)
    {
        presentationReady = false;
        try
        {
            fsData root = fsJsonParser.Parse(json);
            if (root == null || !root.IsDictionary) return false;
            fsData value;
            if (!root.AsDictionary.TryGetValue("presentationReady", out value)) return true;
            if (value == null || !value.IsBool) return false;
            presentationReady = value.AsBool;
            return true;
        }
        catch { return false; }
    }

    private static string Sha256Bytes(byte[] bytes)
    {
        using (SHA256 algorithm = SHA256.Create())
        {
            byte[] hash = algorithm.ComputeHash(bytes ?? new byte[0]);
            var result = new StringBuilder(hash.Length * 2);
            for (int index = 0; index < hash.Length; index++) result.Append(hash[index].ToString("x2"));
            return result.ToString();
        }
    }

    private static bool HasBoundPreparedPresentation(RequestEntry entry)
    {
        if (entry == null || !entry.PresentationRequired || entry.PreparedPresentation == null) return false;
        PortraitPreparedPresentation presentation = entry.PreparedPresentation;
        return presentation.RequestId == entry.RequestId && presentation.PersonId == entry.PersonId &&
            presentation.ConversationToken == entry.ConversationToken && presentation.Text == entry.Text &&
            presentation.SampleRate == entry.SampleRate && presentation.AudioFrames == entry.SampleFrames &&
            presentation.Visemes != null &&
            (presentation.Words == null || presentation.Words.SampleRate == entry.SampleRate &&
                presentation.Words.AudioFrames == entry.SampleFrames) &&
            presentation.Visemes.SampleRate == entry.SampleRate && presentation.Visemes.AudioFrames == entry.SampleFrames;
    }

    public bool TryReadPlaybackMeasurement(string requestId, out string json)
    {
        json = null;
        if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(measurementDirectory)) return false;
        try
        {
            string path = Path.Combine(measurementDirectory, requestId + ".playback.json");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 2 || info.Length > MaximumManifestCharacters) return false;
            json = File.ReadAllText(path);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Reads the receipt for the latest playback attempt, including replays of one request.</summary>
    public bool TryReadLatestPlaybackMeasurement(string requestId, out string json)
    {
        json = null;
        RequestEntry entry;
        if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(measurementDirectory) ||
            !requestsById.TryGetValue(requestId, out entry)) return false;
        try
        {
            string suffix = entry.PlaybackIndex <= 1 ? string.Empty :
                ".replay-" + entry.PlaybackIndex.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(measurementDirectory, requestId + suffix + ".playback.json");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 2 || info.Length > MaximumManifestCharacters) return false;
            json = File.ReadAllText(path);
            return true;
        }
        catch { return false; }
    }

    public int GetPlaybackIndex(string requestId)
    {
        RequestEntry entry;
        return !string.IsNullOrEmpty(requestId) && requestsById.TryGetValue(requestId, out entry)
            ? entry.PlaybackIndex : -1;
    }

    public bool BeginStreamingMixerObservation(string streamId, string conversationToken, string personId,
        AudioClip clip, int sampleRate)
    {
        if (string.IsNullOrWhiteSpace(streamId) || string.IsNullOrWhiteSpace(conversationToken) ||
            string.IsNullOrWhiteSpace(personId) || clip == null || sampleRate <= 0 || audioSource == null ||
            audioSource.clip != clip) return false;
        StreamingReply reply;
        string exactText = TryGetStream(streamId, conversationToken, personId, out reply)
            ? reply.FinalTranscript : null;
        streamingMixerObservation = new StreamingMixerObservation { StreamId = streamId,
            ConversationToken = conversationToken, PersonId = personId, Clip = clip, SampleRate = sampleRate };
        if (!string.IsNullOrEmpty(exactText)) streamingMixerObservation.ExactText = exactText;
        return true;
    }

    public bool MarkStreamingMixerPlaybackStarted(string streamId, string conversationToken, string personId)
    {
        StreamingMixerObservation observation = streamingMixerObservation;
        if (observation == null || observation.StreamId != streamId ||
            observation.ConversationToken != conversationToken || observation.PersonId != personId) return false;
        observation.PlaybackActive = true;
        return true;
    }

    public bool BindStreamingMixerText(string streamId, string conversationToken, string personId,
        string exactText, int totalSampleFrames)
    {
        StreamingReply reply;
        if (!TryGetStream(streamId, conversationToken, personId, out reply) ||
            string.IsNullOrWhiteSpace(exactText) || exactText.Length > BreezeTtsTrialProtocol.MaximumTextCharacters ||
            ContainsUnsupportedText(exactText) ||
            (!string.IsNullOrEmpty(reply.FinalTranscript) &&
                !string.Equals(reply.FinalTranscript, exactText, StringComparison.Ordinal))) return false;
        reply.FinalTranscript = exactText;
        StreamingMixerObservation observation = streamingMixerObservation;
        if (observation == null || observation.StreamId != streamId ||
            observation.ConversationToken != conversationToken || observation.PersonId != personId) return true;
        observation.SampleFrames = Math.Max(0, totalSampleFrames);
        observation.ExactText = exactText;
        return true;
    }

    public void EndStreamingMixerObservation(string streamId, string conversationToken, string personId)
    {
        StreamingMixerObservation observation = streamingMixerObservation;
        if (observation != null && observation.StreamId == streamId &&
            observation.ConversationToken == conversationToken && observation.PersonId == personId)
        {
            observation.PlaybackActive = false;
            streamingMixerObservation = null;
        }
    }

    public void StopStreamingMixerPlayback(string streamId, string conversationToken, string personId)
    {
        StreamingMixerObservation observation = streamingMixerObservation;
        if (observation != null && observation.StreamId == streamId &&
            observation.ConversationToken == conversationToken && observation.PersonId == personId)
            observation.PlaybackActive = false;
    }

    /// <summary>Replays only an already-produced WAV owned by this open conversation.</summary>
    public RequestState ReplayRequest(string requestId, string conversationLifetimeToken)
    {
        RequestEntry entry;
        if (string.IsNullOrEmpty(requestId) || !requestsById.TryGetValue(requestId, out entry))
            return RequestState.Unknown;
        if (activeEntry != null && activeEntry.State == RequestState.Playing && activeEntry.RequestId == requestId)
            return RequestState.Playing;
        if (entry.State == RequestState.Pending || entry.State == RequestState.Loading) return entry.State;
        if (entry.State == RequestState.Cancelled || entry.State == RequestState.Failed) return entry.State;
        if (pendingReplay != null) return RequestState.Loading;
        if (!string.Equals(entry.ConversationToken, conversationLifetimeToken, StringComparison.Ordinal) ||
            !IsCurrent(entry) || string.IsNullOrEmpty(entry.WavePath) || !File.Exists(entry.WavePath))
            return RequestState.Cancelled;

        if (activeEntry != null) FinishPlayback("interrupted-for-replay");
        var replay = new RequestEntry
        {
            RequestId = entry.RequestId,
            ConversationToken = entry.ConversationToken,
            PersonId = entry.PersonId,
            DisplayName = entry.DisplayName,
            Text = entry.Text,
            TextReadyTicks = entry.TextReadyTicks,
            WavePath = entry.WavePath,
            ManifestPath = entry.ManifestPath,
            WaveFile = entry.WaveFile,
            PlaybackIndex = ++entry.PlaybackIndex,
            SampleRate = entry.SampleRate,
            Channels = entry.Channels,
            SampleFrames = entry.SampleFrames,
            State = RequestState.Loading,
            Loading = true,
            PresentationRequired = entry.PresentationRequired,
            PreparedPresentation = entry.PreparedPresentation,
            Original = entry
        };
        pendingReplay = replay;
        BeginLoadWave(replay, entry.WavePath, entry.SampleRate, entry.Channels, entry.SampleFrames,
            replay.PresentationRequired, replay.PreparedPresentation);
        return RequestState.Loading;
    }

    /// <summary>Stops and cancels any work belonging to the supplied conversation lifetime.</summary>
    public void EndConversation(string conversationLifetimeToken)
    {
        if (string.IsNullOrEmpty(conversationLifetimeToken)) return;
        MarkConversationRequestsClosed(conversationLifetimeToken);
        if (string.Equals(activeConversationToken, conversationLifetimeToken, StringComparison.Ordinal))
        { CancelCurrentConversation("conversation-ended"); return; }
        if (activeStreamingReply != null && string.Equals(activeStreamingReply.ConversationToken,
            conversationLifetimeToken, StringComparison.Ordinal)) CancelStreaming(activeStreamingReply, "conversation-ended");
        MarkStreamingConversationClosed(conversationLifetimeToken);
    }

    /// <summary>Applies live settings. Turning voice off stops audio and cancels owned work immediately.</summary>
    public void SetFeatureOptions(bool enableVoice, bool enableSocialStreaming)
    {
        bool wasEnabled = voiceEnabled;
        voiceEnabled = enableVoice;
        bool legacyStreamingOptIn = Array.IndexOf(Environment.GetCommandLineArgs(),
            LegacySocialStreamingOptInFlag) >= 0;
        socialStreamingEnabled = BreezeTtsStreamingPolicy.IsEnabled(enableVoice, enableSocialStreaming,
            !string.IsNullOrEmpty(configuredServerEndpoint), legacyStreamingOptIn);
        if (wasEnabled && !voiceEnabled) CancelCurrentConversation("tts-disabled");
        else if (!socialStreamingEnabled && activeStreamingReply != null && !activeStreamingReply.PlaybackDrained)
            CancelStreaming(activeStreamingReply, "tts-streaming-disabled");
    }

    public void Shutdown()
    {
        if (shuttingDown) return;
        shuttingDown = true;
        CancelCurrentConversation("bridge-shutdown");
    }

    private void Initialize(string directory, bool trialMode)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory))
            throw new ArgumentException("Trial directory must be absolute.", "directory");

        string nextRequestDirectory = Path.Combine(directory, RequestDirectoryName);
        string nextResponseDirectory = Path.Combine(directory, ResponseDirectoryName);
        string nextMeasurementDirectory = Path.Combine(directory, MeasurementDirectoryName);
        Directory.CreateDirectory(nextRequestDirectory);
        Directory.CreateDirectory(nextResponseDirectory);
        Directory.CreateDirectory(nextMeasurementDirectory);
        requestDirectory = nextRequestDirectory;
        responseDirectory = nextResponseDirectory;
        measurementDirectory = nextMeasurementDirectory;
        publishedRequestCount = Directory.GetFiles(requestDirectory, "*.request.json").Length;
        trialDirectory = directory;
        isTrialMode = trialMode;

        audioSource = GetComponent<AudioSource>();
        bool createdOutputAudioSource = audioSource == null;
        if (createdOutputAudioSource) audioSource = gameObject.AddComponent<AudioSource>();
        UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TTS_AUDIO_SOURCE_INITIALIZE: phase=before-config; " +
            DescribeAudioSourceInitialization(createdOutputAudioSource) + ".");
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 1f;
        UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TTS_AUDIO_SOURCE_INITIALIZE: phase=before-owned-stop; " +
            DescribeAudioSourceInitialization(createdOutputAudioSource) + ".");
        if (createdOutputAudioSource) audioSource.Stop();
        UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TTS_AUDIO_SOURCE_INITIALIZE: phase=after-owned-stop; " +
            DescribeAudioSourceInitialization(createdOutputAudioSource) + ".");
    }

    private string DescribeAudioSourceInitialization(bool createdOutputAudioSource)
    {
        PlaybackCounters counters = mixerCounters;
        string counterState = counters == null ? "null" :
            "frames=" + Interlocked.Read(ref counters.Frames) +
            "; mixer-samples=" + Interlocked.Read(ref counters.Samples) +
            "; mixer-nonzero-samples=" + Interlocked.Read(ref counters.NonZeroSamples);
        return "owned-new-source=" + createdOutputAudioSource +
            "; source-id=" + (audioSource == null ? 0 : audioSource.GetInstanceID()) +
            "; clip=" + (audioSource == null || audioSource.clip == null ? "<null>" : audioSource.clip.name) +
            "; is-playing=" + (audioSource != null && audioSource.isPlaying) +
            "; play-on-awake=" + (audioSource != null && audioSource.playOnAwake) +
            "; active-request=" + (ActiveRequestId ?? "<none>") +
            "; mixer-counters=" + counterState;
    }

    private void Update()
    {
        if (shuttingDown || string.IsNullOrEmpty(trialDirectory)) return;
        DrainLoadedResponses();
        PublishStreamingMixerPlaybackStarted();
        if (Time.unscaledTime >= nextClosedArtifactSweepTime)
        {
            nextClosedArtifactSweepTime = Time.unscaledTime + 0.5f;
            CleanupClosedArtifacts();
        }

        if (activeEntry != null)
        {
            ObservePlaybackPosition(activeEntry);
            PublishMixerPlaybackStarted(activeEntry);
            if (audioSource != null && !audioSource.isPlaying)
            {
                long now = Stopwatch.GetTimestamp();
                FinishPlayback(HasPlayedExpectedDuration(activeEntry, now) ? "completed" : "playback-ended-early");
            }
            return;
        }

        if (pendingReplay != null)
        {
            if (pendingReplay.ResponseLoadFinished)
            {
                RequestEntry replay = pendingReplay;
                pendingReplay = null;
                if (replay.Wave == null)
                {
                    replay.Original.State = RequestState.Failed;
                    return;
                }
                StartPlayback(replay);
            }
            return;
        }

        if (queue.Count == 0) return;
        RequestEntry next = queue.Peek();
        if (!IsCurrent(next))
        {
            queue.Dequeue();
            PublishCancellation(next, "stale-before-response");
            return;
        }

        if (next.ResponseLoadFinished)
        {
            queue.Dequeue();
            if (next.Wave == null)
            {
                next.State = RequestState.Failed;
                WriteMeasurement(next, null, "response-invalid:" + (next.LoadError ?? "unknown"), 0);
                PublishCancellation(next, "invalid-response");
                return;
            }
            StartPlayback(next);
            return;
        }

        if (!next.Loading && Time.unscaledTime >= nextResponsePollTime)
        {
            if (Stopwatch.GetTimestamp() - next.QueuedTicks > Stopwatch.Frequency * ResponseTimeoutSeconds)
            {
                queue.Dequeue();
                next.State = RequestState.Failed;
                WriteMeasurement(next, null, "response-timeout", 0);
                PublishCancellation(next, "response-timeout");
                return;
            }
            nextResponsePollTime = Time.unscaledTime + ResponsePollSeconds;
            string manifestPath = Path.Combine(responseDirectory, next.RequestId + ".manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var info = new FileInfo(manifestPath);
                    if (!info.Exists || info.Length < 2 || info.Length > MaximumManifestCharacters)
                        throw new InvalidDataException("manifest-size");
                    string json = File.ReadAllText(manifestPath);
                    bool presentationReady;
                    if (!TryReadPresentationReady(json, out presentationReady))
                        throw new InvalidDataException("manifest-presentation-ready");
                    ResponseManifest manifest = JsonUtility.FromJson<ResponseManifest>(json);
                    if (manifest == null || manifest.schemaVersion != 1 || manifest.requestId != next.RequestId ||
                        manifest.conversationToken != next.ConversationToken || manifest.personId != next.PersonId ||
                        manifest.encoding != "pcm_s16le" || manifest.sampleRate < 8000 || manifest.sampleRate > 192000 ||
                        manifest.channels < 1 || manifest.channels > 2 || manifest.sampleFrames < 1)
                        throw new InvalidDataException("manifest-binding");
                    manifest.presentationReady = presentationReady;
                    string wavePath;
                    if (!BreezeTtsTrialProtocol.TryResolveResponseWavePath(responseDirectory, manifest.wavFile, out wavePath))
                        throw new InvalidDataException("wave-path");
                    next.ManifestPath = manifestPath;
                    next.WavePath = wavePath;
                    next.WaveFile = manifest.wavFile;
                    next.SampleRate = manifest.sampleRate;
                    next.Channels = manifest.channels;
                    next.SampleFrames = manifest.sampleFrames;
                    next.State = RequestState.Loading;
                    BeginLoadResponse(next, wavePath, manifest);
                }
                catch (Exception error)
                {
                    next.LoadError = error.GetType().Name + ":" + error.Message;
                    next.ResponseLoadFinished = true;
                    next.State = RequestState.Failed;
                }
            }
            else
            {
                string failurePath = Path.Combine(responseDirectory, next.RequestId + ".failed.json");
                if (File.Exists(failurePath))
                {
                    try
                    {
                        string json;
                        ResponseFailureTerminal terminal = TryReadBoundedJson(failurePath,
                            MaximumFailureTerminalCharacters, out json)
                            ? JsonUtility.FromJson<ResponseFailureTerminal>(json) : null;
                        if (terminal != null && BreezeTtsTrialProtocol.TryMatchFailureTerminal(
                            terminal.schemaVersion, terminal.requestId, terminal.conversationToken,
                            terminal.personId, terminal.status, next.RequestId,
                            next.ConversationToken, next.PersonId))
                        {
                            queue.Dequeue();
                            next.State = RequestState.Failed;
                            next.LoadError = "coordinator-failed";
                            WriteMeasurement(next, null, "coordinator-failed", 0);
                            UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS coordinator returned a bound failure; " +
                                "showing accepted text without playback. request=" + next.RequestId);
                            PublishPrePlaybackTerminal(next, "coordinator-failed");
                            return;
                        }
                    }
                    catch (Exception error)
                    {
                        // A transient sharing/read error or partial atomic publish must not latch this request.
                        // Keep polling at the existing bounded cadence until the receipt is readable or times out.
                        next.LoadError = "failure-terminal-read:" + error.GetType().Name;
                    }
                }
            }
        }
    }

    private void BeginLoadResponse(RequestEntry entry, string wavePath, ResponseManifest manifest)
    {
        entry.Loading = true;
        entry.PresentationRequired = manifest.presentationReady;
        BeginLoadWave(entry, wavePath, manifest.sampleRate, manifest.channels, manifest.sampleFrames,
            manifest.presentationReady, null);
    }

    private void BeginLoadWave(RequestEntry entry, string wavePath, int sampleRate, int channels, int sampleFrames,
        bool presentationRequired = false, PortraitPreparedPresentation reusablePresentation = null)
    {
        entry.Loading = true;
        bool queued = ThreadPool.QueueUserWorkItem(_ =>
        {
            var loaded = new LoadedResponse { Entry = entry };
            try
            {
                var waveInfo = new FileInfo(wavePath);
                if (!waveInfo.Exists || waveInfo.Length < 44 || waveInfo.Length > BreezeTtsTrialProtocol.MaximumWaveBytes)
                    throw new InvalidDataException("wave-size");
                byte[] waveBytes = File.ReadAllBytes(wavePath);
                BreezeTtsTrialWave wave;
                if (!BreezeTtsTrialProtocol.TryDecodePcm16Wave(waveBytes, out wave) ||
                    wave.SampleRate != sampleRate || wave.Channels != channels ||
                    wave.SampleFrames != sampleFrames)
                    throw new InvalidDataException("wave-metadata");

                if (presentationRequired)
                {
                    var audio = new PortraitVisemeAudioEvidence
                    {
                        RequestId = entry.RequestId,
                        PersonId = entry.PersonId,
                        ConversationToken = entry.ConversationToken,
                        Text = entry.Text,
                        SampleRate = sampleRate,
                        AudioFrames = sampleFrames,
                        Channels = channels,
                        AudioSha256 = Sha256Bytes(waveBytes),
                        WavePath = wavePath,
                    };
                    if (reusablePresentation != null)
                    {
                        if (!reusablePresentation.Matches(audio))
                            throw new InvalidDataException("prepared-presentation-replay-binding");
                        loaded.PreparedPresentation = reusablePresentation;
                    }
                    else
                    {
                        string alignmentPath = Path.ChangeExtension(wavePath, ".alignment.json");
                        string visemePath = Path.ChangeExtension(wavePath, ".visemes.json");
                        string preparationError;
                        if (!PortraitPreparedPresentationLoader.TryLoad(alignmentPath, visemePath, audio,
                            out loaded.PreparedPresentation, out preparationError))
                            throw new InvalidDataException("prepared-presentation:" + (preparationError ?? "invalid"));
                    }
                }
                loaded.Wave = wave;
            }
            catch (Exception error) { loaded.Error = error.GetType().Name + ":" + error.Message; }

            lock (loadedResponseLock) loadedResponses.Enqueue(loaded);
        });
        if (!queued)
        {
            entry.Loading = false;
            entry.ResponseLoadFinished = true;
            entry.LoadError = "worker-unavailable";
            entry.State = RequestState.Failed;
        }
    }

    private void DrainLoadedResponses()
    {
        lock (loadedResponseLock)
        {
            while (loadedResponses.Count > 0)
            {
                LoadedResponse result = loadedResponses.Dequeue();
                if (result.Entry == null) continue;
                if (result.Entry.State == RequestState.Cancelled || !IsCurrent(result.Entry)) continue;
                result.Entry.Wave = result.Wave;
                result.Entry.PreparedPresentation = result.PreparedPresentation;
                result.Entry.LoadError = result.Error;
                result.Entry.ResponseLoadFinished = true;
                result.Entry.Loading = false;
                bool prepared = !result.Entry.PresentationRequired ||
                    result.Entry.PreparedPresentation != null && result.Entry.PreparedPresentation.RequestId == result.Entry.RequestId &&
                    result.Entry.PreparedPresentation.PersonId == result.Entry.PersonId &&
                    result.Entry.PreparedPresentation.ConversationToken == result.Entry.ConversationToken &&
                    result.Entry.PreparedPresentation.Text == result.Entry.Text &&
                    result.Entry.PreparedPresentation.SampleRate == result.Entry.SampleRate &&
                    result.Entry.PreparedPresentation.AudioFrames == result.Entry.SampleFrames;
                if (!prepared)
                {
                    result.Entry.Wave = null;
                    if (result.Entry.LoadError == null) result.Entry.LoadError = "prepared-presentation-cache-missing";
                }
                result.Entry.State = result.Entry.Wave == null ? RequestState.Failed : RequestState.Ready;
            }
        }
    }

    private void StartPlayback(RequestEntry entry)
    {
        if (!IsCurrent(entry) || audioSource == null)
        {
            PublishCancellation(entry, "stale-before-playback");
            return;
        }

        if (entry.PresentationRequired && !HasBoundPreparedPresentation(entry))
        {
            entry.State = RequestState.Failed;
            if (entry.Original != null) entry.Original.State = RequestState.Failed;
            entry.LoadError = "prepared-presentation-cache-missing";
            WriteMeasurement(entry, null, "response-invalid:" + entry.LoadError, 0);
            PublishPrePlaybackTerminal(entry, entry.LoadError);
            return;
        }

        try
        {
            if (activeClip != null) Destroy(activeClip);
            activeClip = AudioClip.Create("Breeze TTS trial " + entry.RequestId, entry.Wave.SampleFrames,
                entry.Wave.Channels, entry.Wave.SampleRate, false);
            if (activeClip == null || !activeClip.SetData(entry.Wave.Samples, 0))
                throw new InvalidOperationException("unity-audio-clip");

            entry.Counters = new PlaybackCounters();
            if (entry.PlaybackIndex == 0) entry.PlaybackIndex = 1;
            activeEntry = entry;
            mixerCounters = entry.Counters;
            entry.State = RequestState.Playing;
            if (entry.Original != null) entry.Original.State = RequestState.Playing;
            audioSource.clip = activeClip;
            entry.PlaybackStartTicks = Stopwatch.GetTimestamp();
            entry.LastKnownSample = 0;
            audioSource.Play();
            WriteMeasurement(entry, entry.Counters, "playing", entry.PlaybackStartTicks, entry.Wave);
        }
        catch (Exception error)
        {
            mixerCounters = null;
            activeEntry = null;
            entry.State = RequestState.Failed;
            if (entry.Original != null) entry.Original.State = RequestState.Failed;
            WriteMeasurement(entry, entry.Counters, "playback-start-failed:" + error.GetType().Name, 0, entry.Wave);
            PublishPrePlaybackTerminal(entry, "playback-start-failed:" + error.GetType().Name);
            if (activeClip != null) Destroy(activeClip);
            activeClip = null;
            audioSource.clip = null;
        }
    }

    private void FinishPlayback(string status)
    {
        RequestEntry finished = activeEntry;
        if (finished == null) return;
        mixerCounters = null;
        ObservePlaybackPosition(finished);
        long playbackEndTicks = Stopwatch.GetTimestamp();
        int finalSample = audioSource != null && audioSource.clip != null ? audioSource.timeSamples : 0;
        WriteMeasurement(finished, finished.Counters, status, finished.PlaybackStartTicks, finished.Wave, finalSample,
            playbackEndTicks);
        RequestState finishedState = status == "interrupted-for-replay" ? RequestState.Ready :
            status == "completed" ? RequestState.Completed : RequestState.Failed;
        finished.State = finishedState;
        if (finished.Original != null) finished.Original.State = finishedState;
        if (finished.Counters == null || Interlocked.Read(ref finished.Counters.NonZeroSamples) <= 0)
            PublishPrePlaybackTerminal(finished, "playback-ended-without-nonzero-mixer-output");
        activeEntry = null;
        if (activeClip != null) Destroy(activeClip);
        activeClip = null;
        if (audioSource != null) audioSource.clip = null;
        finished.Wave = null;
    }

    private void ObservePlaybackPosition(RequestEntry entry)
    {
        if (entry == null || audioSource == null || audioSource.clip == null) return;
        int sample = audioSource.timeSamples;
        if (sample > entry.LastKnownSample) entry.LastKnownSample = sample;
    }

    private static bool HasPlayedExpectedDuration(RequestEntry entry, long nowTicks)
    {
        if (entry == null || entry.SampleRate <= 0 || entry.SampleFrames <= 0 || entry.PlaybackStartTicks <= 0)
            return false;
        long expectedTicks = (long)Math.Ceiling((double)entry.SampleFrames * Stopwatch.Frequency / entry.SampleRate);
        long observedTicks = Math.Max(0, nowTicks - entry.PlaybackStartTicks);
        // Allow 2% scheduling variance, capped at 100 ms. Final AudioSource.timeSamples can reset at natural end.
        long toleranceTicks = Math.Min(Stopwatch.Frequency / 10, Math.Max(1, expectedTicks / 50));
        return observedTicks + toleranceTicks >= expectedTicks;
    }

    private void CancelCurrentConversation(string reason)
    {
        string closingToken = activeConversationToken;
        if (activeStreamingReply != null &&
            (string.IsNullOrEmpty(closingToken) || string.Equals(activeStreamingReply.ConversationToken, closingToken, StringComparison.Ordinal)))
            CancelStreaming(activeStreamingReply, reason);
        if (audioSource != null && audioSource.isPlaying) audioSource.Stop();
        mixerCounters = null;
        if (activeEntry != null)
        {
            WriteMeasurement(activeEntry, activeEntry.Counters, "cancelled:" + reason,
                activeEntry.PlaybackStartTicks, activeEntry.Wave,
                audioSource != null ? audioSource.timeSamples : 0);
            activeEntry = null;
        }
        if (activeClip != null) Destroy(activeClip);
        activeClip = null;
        if (audioSource != null) audioSource.clip = null;
        pendingReplay = null;
        queue.Clear();
        lock (loadedResponseLock) loadedResponses.Clear();
        if (!string.IsNullOrEmpty(closingToken))
        {
            foreach (var pair in requestsById)
            {
                RequestEntry entry = pair.Value;
                if (!string.Equals(entry.ConversationToken, closingToken, StringComparison.Ordinal)) continue;
                entry.State = RequestState.Cancelled;
                entry.CancelledAtTicks = Stopwatch.GetTimestamp();
                entry.Wave = null;
                PublishCancellation(entry, reason);
                DeleteOwnedArtifacts(entry);
            }
        }
        activeConversationToken = null;
        activePersonId = null;
        MarkStreamingConversationClosed(closingToken);
    }

    private void CleanupClosedArtifacts()
    {
        long now = Stopwatch.GetTimestamp();
        List<string> remove = null;
        foreach (var pair in requestsById)
        {
            RequestEntry entry = pair.Value;
            if (entry.State != RequestState.Cancelled || entry.CancelledAtTicks == 0) continue;
            if (now - entry.CancelledAtTicks <= Stopwatch.Frequency * ResponseTimeoutSeconds)
            {
                DeleteOwnedArtifacts(entry);
                continue;
            }
            if (!entry.ConversationClosed) continue;
            DeleteOwnedArtifacts(entry);
            if (remove == null) remove = new List<string>();
            remove.Add(pair.Key);
        }
        if (remove == null) return;
        foreach (string requestId in remove) requestsById.Remove(requestId);
    }

    private void MarkConversationRequestsClosed(string conversationToken)
    {
        foreach (RequestEntry entry in requestsById.Values)
            if (string.Equals(entry.ConversationToken, conversationToken, StringComparison.Ordinal))
                entry.ConversationClosed = true;
    }

    private void DeleteOwnedArtifacts(RequestEntry entry)
    {
        if (entry == null) return;
        string manifestPath = Path.Combine(responseDirectory, entry.RequestId + ".manifest.json");
        if (string.IsNullOrEmpty(entry.WaveFile) && File.Exists(manifestPath))
        {
            try
            {
                var info = new FileInfo(manifestPath);
                if (info.Length >= 2 && info.Length <= MaximumManifestCharacters)
                {
                    ResponseManifest manifest = JsonUtility.FromJson<ResponseManifest>(File.ReadAllText(manifestPath));
                    if (manifest != null && manifest.schemaVersion == 1 && manifest.requestId == entry.RequestId &&
                        manifest.conversationToken == entry.ConversationToken && manifest.personId == entry.PersonId)
                        entry.WaveFile = manifest.wavFile;
                }
            }
            catch { }
        }

        string[] paths;
        if (!BreezeTtsTrialProtocol.TryBuildConversationCleanupPaths(requestDirectory, responseDirectory,
            entry.RequestId, entry.WaveFile, out paths)) return;
        foreach (string path in paths)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception error)
            { UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS trial cleanup failed: " + error.GetType().Name); }
        }
    }

    private bool IsCurrent(RequestEntry entry)
    {
        return entry != null && string.Equals(entry.ConversationToken, activeConversationToken, StringComparison.Ordinal) &&
            string.Equals(entry.PersonId, activePersonId, StringComparison.Ordinal);
    }

    private void PublishCancellation(RequestEntry entry, string reason)
    {
        if (entry == null || string.IsNullOrEmpty(requestDirectory)) return;
        PublishPrePlaybackTerminal(entry, reason);
        try
        {
            string json = "{\"schemaVersion\":1,\"requestId\":" + Quote(entry.RequestId) +
                ",\"conversationToken\":" + Quote(entry.ConversationToken) + ",\"reason\":" + Quote(reason) + "}";
            string markerPath = Path.Combine(requestDirectory, entry.RequestId + ".cancel.json");
            if (!File.Exists(markerPath)) BreezeTtsTrialProtocol.WriteAtomically(requestDirectory, entry.RequestId + ".cancel.json", json);
        }
        catch (Exception error)
        { UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS trial cancel marker failed: " + error.GetType().Name); }
    }

    private void PublishMixerPlaybackStarted(RequestEntry entry)
    {
        PlaybackCounters counters = entry == null ? null : entry.Counters;
        if (entry == null || counters == null || entry.MixerPlaybackStartPublished ||
            Interlocked.Read(ref counters.NonZeroSamples) <= 0) return;
        long firstTicks = Interlocked.Read(ref counters.FirstNonZeroMixerTicks);
        long firstFrame = Interlocked.Read(ref counters.FirstNonZeroMixerFrame);
        if (firstTicks <= 0 || firstFrame < 0) return;
        entry.MixerPlaybackStartPublished = true;
        Action<MixerPlaybackStartReceipt> callback = MixerPlaybackStarted;
        if (callback != null)
            callback(new MixerPlaybackStartReceipt(entry.RequestId, entry.ConversationToken, entry.PersonId,
                entry.Text, entry.PlaybackIndex, entry.SampleRate, entry.SampleFrames, firstTicks, firstFrame));
    }

    private void PublishStreamingMixerPlaybackStarted()
    {
        StreamingMixerObservation observation = streamingMixerObservation;
        if (observation == null || observation.StartPublished || string.IsNullOrEmpty(observation.ExactText) ||
            Interlocked.Read(ref observation.Counters.NonZeroSamples) <= 0) return;
        long firstTicks = Interlocked.Read(ref observation.Counters.FirstNonZeroMixerTicks);
        long firstFrame = Interlocked.Read(ref observation.Counters.FirstNonZeroMixerFrame);
        if (firstTicks <= 0 || firstFrame < 0) return;
        observation.StartPublished = true;
        Action<MixerPlaybackStartReceipt> callback = MixerPlaybackStarted;
        if (callback != null)
            callback(new MixerPlaybackStartReceipt(observation.StreamId, observation.ConversationToken,
                observation.PersonId, observation.ExactText, observation.PlaybackIndex,
                observation.SampleRate, Math.Max(observation.SampleFrames, (int)Math.Min(int.MaxValue, firstFrame + 1)),
                firstTicks, firstFrame));
    }

    private void PublishPrePlaybackTerminal(RequestEntry entry, string reason)
    {
        if (entry == null || entry.PrePlaybackTerminalPublished ||
            entry.MixerPlaybackStartPublished) return;
        entry.PrePlaybackTerminalPublished = true;
        Action<PrePlaybackTerminalReceipt> callback = PrePlaybackTerminated;
        if (callback != null) callback(new PrePlaybackTerminalReceipt(entry.RequestId,
            entry.ConversationToken, entry.PersonId, entry.Text, entry.State, reason));
    }

    private void WriteMeasurement(RequestEntry entry, PlaybackCounters counters, string status, long playbackStart,
        BreezeTtsTrialWave wave = null, int finalSample = 0, long playbackEnd = 0)
    {
        if (entry == null || string.IsNullOrEmpty(measurementDirectory)) return;
        var measurement = new PlaybackMeasurement
        {
            requestId = entry.RequestId,
            conversationToken = entry.ConversationToken,
            personId = entry.PersonId,
            status = status,
            textReadyMonotonicTicks = entry.TextReadyTicks,
            playbackStartMonotonicTicks = playbackStart,
            timestampFrequency = Stopwatch.Frequency,
            mixerCallbackFrames = counters == null ? 0 : Interlocked.Read(ref counters.Frames),
            mixerCallbackSamples = counters == null ? 0 : Interlocked.Read(ref counters.Samples),
            nonZeroOutputSamples = counters == null ? 0 : Interlocked.Read(ref counters.NonZeroSamples),
            peakOutputAmplitudeMicrounits = counters == null ? 0 : Interlocked.Read(ref counters.PeakAmplitudeMicrounits),
            callbackGapMarkers = counters == null ? 0 : Volatile.Read(ref counters.GapMarkers),
            expectedSampleFrames = wave == null ? 0 : wave.SampleFrames,
            sourceFinalSample = finalSample,
            lastKnownSample = entry.LastKnownSample,
            sampleRate = wave == null ? 0 : wave.SampleRate,
            channels = wave == null ? 0 : wave.Channels,
            playbackEndMonotonicTicks = playbackEnd,
            expectedPlaybackMilliseconds = wave == null || wave.SampleRate <= 0 ? 0 :
                (long)Math.Ceiling((double)wave.SampleFrames * 1000d / wave.SampleRate),
            observedPlaybackMilliseconds = playbackStart <= 0 || playbackEnd <= 0 ? 0 :
                (long)Math.Max(0, (playbackEnd - playbackStart) * 1000d / Stopwatch.Frequency)
        };
        try
        {
            string replaySuffix = entry.PlaybackIndex <= 1 ? string.Empty :
                ".replay-" + entry.PlaybackIndex.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            string suffix = status == "playing" ? replaySuffix + ".playback-started.json" : replaySuffix + ".playback.json";
            BreezeTtsTrialProtocol.WriteAtomically(measurementDirectory, entry.RequestId + suffix,
                JsonUtility.ToJson(measurement));
        }
        catch (Exception error)
        { UnityEngine.Debug.LogWarning("[PeopleOfTheIliacBay] TTS trial measurement write failed: " + error.GetType().Name); }
    }

    private static string Quote(string value)
    {
        string json = JsonUtility.ToJson(new StringWrapper { value = value });
        int colon = json.IndexOf(':');
        return colon < 0 ? "\"\"" : json.Substring(colon + 1, json.Length - colon - 2);
    }

    [Serializable]
    private sealed class StringWrapper { public string value; }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        PlaybackCounters counters = mixerCounters;
        StreamingMixerObservation streaming = streamingMixerObservation;
        if (counters == null && streaming != null && streaming.PlaybackActive) counters = streaming.Counters;
        if (counters == null || data == null || channels <= 0) return;
        long now = Stopwatch.GetTimestamp();
        long previous = Interlocked.Exchange(ref counters.LastCallbackTicks, now);
        if (previous != 0 && now - previous > Stopwatch.Frequency / 4)
            Interlocked.Increment(ref counters.GapMarkers);
        long firstFrameInBlock = Interlocked.Read(ref counters.Frames);
        Interlocked.Add(ref counters.Frames, data.Length / channels);
        Interlocked.Add(ref counters.Samples, data.Length);
        long nonZeroSamples = 0;
        long peakMicrounits = 0;
        for (int i = 0; i < data.Length; i++)
        {
            float value = data[i];
            if (float.IsNaN(value) || float.IsInfinity(value)) continue;
            float magnitude = Math.Abs(value);
            if (magnitude > 0.000001f) nonZeroSamples++;
            long scaledMagnitude = (long)(magnitude * 1000000f);
            if (scaledMagnitude > peakMicrounits) peakMicrounits = scaledMagnitude;
        }
        if (nonZeroSamples > 0)
        {
            Interlocked.Add(ref counters.NonZeroSamples, nonZeroSamples);
            Interlocked.CompareExchange(ref counters.FirstNonZeroMixerTicks, now, 0);
            Interlocked.CompareExchange(ref counters.FirstNonZeroMixerFrame, firstFrameInBlock, -1);
        }
        long currentPeak;
        do
        {
            currentPeak = Interlocked.Read(ref counters.PeakAmplitudeMicrounits);
            if (peakMicrounits <= currentPeak) break;
        }
        while (Interlocked.CompareExchange(ref counters.PeakAmplitudeMicrounits,
            peakMicrounits, currentPeak) != currentPeak);
    }

    private void OnDisable() { Shutdown(); }
    private void OnDestroy() { Shutdown(); }
    private void OnApplicationQuit() { Shutdown(); }
}
