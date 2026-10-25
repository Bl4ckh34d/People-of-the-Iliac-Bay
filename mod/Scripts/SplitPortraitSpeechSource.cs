using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FullSerializer;
using PeopleOfTheIliacBay.DFU;
using UnityEngine;

public enum SplitPortraitSpeechPlaybackStatus
{
    Unavailable,
    Playing
}

/// <summary>One read-only, owner-bound mouth cue result for a split portrait.</summary>
public readonly struct SplitPortraitSpeechRead
{
    public readonly SplitPortraitSpeechPlaybackStatus Status;
    public readonly PortraitMouthShape MouthShape;
    public readonly string RequestId;
    public readonly bool HasValidatedTimeline;

    public bool IsPlaying { get { return Status == SplitPortraitSpeechPlaybackStatus.Playing; } }

    internal SplitPortraitSpeechRead(SplitPortraitSpeechPlaybackStatus status, PortraitMouthShape mouthShape,
        string requestId, bool hasValidatedTimeline)
    {
        Status = status;
        MouthShape = mouthShape;
        RequestId = requestId;
        HasValidatedTimeline = hasValidatedTimeline;
    }
}

/// <summary>
/// Optional full-WAV sidecar reader. Call Read on Unity's main thread. It performs no per-frame
/// file reads or hashes; one bounded worker validates a sidecar per playback generation. Streaming returns X.
/// </summary>
public sealed class SplitPortraitSpeechSource : IDisposable
{
    private const int MaximumSidecarBytes = 2 * 1024 * 1024;
    private const long MaximumWaveHashBytes = 128L * 1024 * 1024;
    private const int MaximumSidecarProbesPerPlayback = 8;
    private const float SidecarRetrySeconds = 0.5f;

    private sealed class SidecarLoadResult
    {
        public PortraitSpeechOwnerKey Owner;
        public PortraitVisemeTimeline Timeline;
        public string Error;
    }

    private readonly BreezeTtsTrialBridge bridge;
    private readonly object sidecarGate = new object();
    private readonly PortraitSpeechLoadPolicy loadPolicy = new PortraitSpeechLoadPolicy();
    private bool sidecarLoadInFlight;
    private SidecarLoadResult completedSidecarLoad;
    private bool hasCurrentOwner;
    private PortraitSpeechOwnerKey currentOwner;
    private PortraitSpeechOwnerKey timelineOwner;
    private bool hasTimelineOwner;
    private PortraitVisemeTimeline timeline;
    private bool presentationRequired;
    private PortraitPreparedPresentation preparedPresentation;
    private readonly float[] rmsWindow = new float[512];
    private readonly PortraitAmplitudeFallback amplitudeFallback = new PortraitAmplitudeFallback(0.02, 0.01);
    private BreezeTtsTrialBridge.ActiveAudioEvidence currentEvidence;
    private AudioClip currentClip;
    private long generation;
    private bool disposed;

    public SplitPortraitSpeechSource(BreezeTtsTrialBridge ttsBridge)
    {
        if (!ttsBridge) throw new ArgumentNullException(nameof(ttsBridge));
        bridge = ttsBridge;
    }

    /// <summary>
    /// Read cue for caller's current actor/session binding. Any missing, stale, paused, stopped,
    /// streaming, or unvalidated owner returns X. A playing owner without a ready sidecar also returns X.
    /// </summary>
    public SplitPortraitSpeechRead Read(string personId, string conversationToken)
    {
        if (disposed || !bridge || string.IsNullOrWhiteSpace(personId) ||
            string.IsNullOrWhiteSpace(conversationToken))
        {
            ResetCurrentOwner();
            return Unavailable();
        }

        AudioSource output = bridge.OutputAudioSource;
        string requestId = bridge.ActiveRequestId;
        string activePersonId = bridge.ActivePersonId;
        string activeConversationToken = bridge.ActiveConversationToken;
        AudioClip clip = output == null ? null : output.clip;
        if (!output || !output.isPlaying || !clip || string.IsNullOrWhiteSpace(requestId) ||
            string.IsNullOrWhiteSpace(activePersonId) || string.IsNullOrWhiteSpace(activeConversationToken))
        {
            ResetCurrentOwner();
            return Unavailable();
        }

        if (!string.Equals(personId, activePersonId, StringComparison.Ordinal) ||
            !string.Equals(conversationToken, activeConversationToken, StringComparison.Ordinal))
        {
            ResetCurrentOwner();
            return Unavailable();
        }

        int clipInstanceId = clip.GetInstanceID();
        BreezeTtsTrialBridge.ActiveAudioEvidence evidence;
        bool sameOwner = hasCurrentOwner && currentOwner.ClipInstanceId == clipInstanceId &&
            string.Equals(currentOwner.RequestId, requestId, StringComparison.Ordinal) &&
            string.Equals(currentOwner.PersonId, activePersonId, StringComparison.Ordinal) &&
            string.Equals(currentOwner.ConversationToken, activeConversationToken, StringComparison.Ordinal) &&
            ReferenceEquals(currentClip, clip) && currentEvidence != null;

        if (sameOwner)
        {
            evidence = currentEvidence;
        }
        else
        {
            if (!bridge.TryReadActivePreparedPresentation(out evidence, out presentationRequired,
                out preparedPresentation) || evidence == null ||
                !string.Equals(evidence.RequestId, requestId, StringComparison.Ordinal) ||
                !string.Equals(evidence.PersonId, activePersonId, StringComparison.Ordinal) ||
                !string.Equals(evidence.ConversationToken, activeConversationToken, StringComparison.Ordinal))
            {
                ResetCurrentOwner();
                return Unavailable();
            }

            long nextGeneration = NextGeneration();
            currentOwner = new PortraitSpeechOwnerKey(requestId, activePersonId, activeConversationToken,
                clipInstanceId, nextGeneration);
            currentClip = clip;
            currentEvidence = evidence;
            hasCurrentOwner = true;
            timeline = null;
            hasTimelineOwner = false;
            amplitudeFallback.Reset();
        }

        if (!ReferenceEquals(output.clip, currentClip) || !output.isPlaying)
        {
            ResetCurrentOwner();
            return Unavailable();
        }

        if (clip.frequency != evidence.SampleRate || clip.samples != evidence.SampleFrames ||
            evidence.SampleRate <= 0 || evidence.SampleFrames <= 0 || evidence.Channels < 1 || evidence.Channels > 2)
        {
            timeline = null;
            hasTimelineOwner = false;
            return new SplitPortraitSpeechRead(SplitPortraitSpeechPlaybackStatus.Playing,
                PortraitMouthShape.X, requestId, false);
        }

        if (presentationRequired)
        {
            if (preparedPresentation != null && preparedPresentation.RequestId == evidence.RequestId &&
                preparedPresentation.PersonId == evidence.PersonId &&
                preparedPresentation.ConversationToken == evidence.ConversationToken &&
                preparedPresentation.Text == evidence.Text && preparedPresentation.SampleRate == evidence.SampleRate &&
                preparedPresentation.AudioFrames == evidence.SampleFrames)
            {
                timeline = preparedPresentation.Visemes;
                timelineOwner = currentOwner;
                hasTimelineOwner = timeline != null;
            }
            else
            {
                timeline = null;
                hasTimelineOwner = false;
            }
        }
        else
        {
            DrainSidecarLoad();
            if (!hasTimelineOwner || !timelineOwner.Matches(currentOwner))
                TryQueueSidecarLoad(evidence, currentOwner);
        }

        if (timeline != null && hasTimelineOwner && timelineOwner.Matches(currentOwner) &&
            timeline.SampleRate == evidence.SampleRate && timeline.AudioFrames == evidence.SampleFrames)
        {
            int cursor = output.timeSamples;
            return new SplitPortraitSpeechRead(SplitPortraitSpeechPlaybackStatus.Playing,
                timeline.ShapeAtSample(cursor), requestId, true);
        }

        try
        {
            output.GetOutputData(rmsWindow, 0);
            double rms = PortraitAmplitudeFallback.CalculateRms(rmsWindow);
            return new SplitPortraitSpeechRead(SplitPortraitSpeechPlaybackStatus.Playing,
                amplitudeFallback.ShapeForRms(rms), requestId, false);
        }
        catch (Exception)
        {
            amplitudeFallback.Reset();
            return new SplitPortraitSpeechRead(SplitPortraitSpeechPlaybackStatus.Playing,
                PortraitMouthShape.X, requestId, false);
        }
    }

    /// <summary>Drop active clip and cue binding. Bounded load history is scoped to playback generations.</summary>
    public void Reset()
    {
        ResetCurrentOwner();
    }

    public void Dispose()
    {
        lock (sidecarGate)
        {
            if (disposed) return;
            disposed = true;
            completedSidecarLoad = null;
        }
        ResetCurrentOwner();
    }

    private void TryQueueSidecarLoad(BreezeTtsTrialBridge.ActiveAudioEvidence evidence,
        PortraitSpeechOwnerKey owner)
    {
        if (evidence == null || string.IsNullOrWhiteSpace(evidence.RequestId) ||
            string.IsNullOrWhiteSpace(evidence.WavePath)) return;

        lock (sidecarGate)
        {
            if (sidecarLoadInFlight || !loadPolicy.TryBegin(owner,
                Time.realtimeSinceStartup, MaximumSidecarProbesPerPlayback)) return;
            sidecarLoadInFlight = true;
        }

        var captured = new PortraitVisemeAudioEvidence
        {
            RequestId = evidence.RequestId,
            PersonId = evidence.PersonId,
            ConversationToken = evidence.ConversationToken,
            Text = evidence.Text,
            SampleRate = evidence.SampleRate,
            AudioFrames = evidence.SampleFrames,
            Channels = evidence.Channels,
            WavePath = evidence.WavePath,
        };

        try
        {
            if (!ThreadPool.QueueUserWorkItem(_ => LoadSidecarWorker(captured, owner)))
                CompleteSidecarLoad(new SidecarLoadResult { Owner = owner, Error = "sidecar-worker-not-queued" });
        }
        catch (Exception error)
        {
            CompleteSidecarLoad(new SidecarLoadResult { Owner = owner,
                Error = "sidecar-worker-not-queued:" + error.GetType().Name });
        }
    }

    private void LoadSidecarWorker(PortraitVisemeAudioEvidence audio, PortraitSpeechOwnerKey owner)
    {
        var result = new SidecarLoadResult { Owner = owner };
        try
        {
            string wavePath = Path.GetFullPath(audio.WavePath);
            if (!string.Equals(Path.GetFileNameWithoutExtension(wavePath), audio.RequestId,
                StringComparison.Ordinal)) throw new InvalidDataException("wav-name-does-not-match-request");

            var waveInfo = new FileInfo(wavePath);
            if (!waveInfo.Exists || waveInfo.Length < 44 || waveInfo.Length > MaximumWaveHashBytes)
                throw new InvalidDataException("wav-size-out-of-bounds");

            string sidecarPath = Path.ChangeExtension(wavePath, ".visemes.json");
            var sidecarInfo = new FileInfo(sidecarPath);
            if (!sidecarInfo.Exists) throw new FileNotFoundException("sidecar-not-ready");
            if (sidecarInfo.Length <= 0 || sidecarInfo.Length > MaximumSidecarBytes)
                throw new InvalidDataException("sidecar-size-out-of-bounds");

            string json = ReadBoundedSidecar(sidecarPath);
            fsData sidecarData = fsJsonParser.Parse(json);
            if (sidecarData == null || !sidecarData.IsDictionary)
                throw new InvalidDataException("sidecar-json-root-not-object");

            var serializer = new fsSerializer();
            PortraitVisemeSidecarDocument document = null;
            fsResult deserializeResult = serializer.TryDeserialize(sidecarData, ref document);
            if (deserializeResult.Failed) throw new InvalidDataException("sidecar-json-deserialize-failed");

            using (var stream = new FileStream(wavePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                if (stream.Length < 44 || stream.Length > MaximumWaveHashBytes)
                    throw new InvalidDataException("wav-size-out-of-bounds");
                byte[] hash = algorithm.ComputeHash(stream);
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) hex.Append(value.ToString("x2"));
                audio.AudioSha256 = hex.ToString();
            }

            if (!PortraitVisemeSidecarValidator.TryValidate(document, audio, audio.PersonId,
                audio.ConversationToken, out result.Timeline, out result.Error))
                throw new InvalidDataException(result.Error ?? "sidecar-validation-failed");
        }
        catch (Exception error)
        {
            result.Timeline = null;
            result.Error = error is FileNotFoundException || error is DirectoryNotFoundException
                ? "sidecar-not-ready"
                : error is InvalidDataException ? error.Message : error.GetType().Name;
        }

        CompleteSidecarLoad(result);
    }

    private void CompleteSidecarLoad(SidecarLoadResult result)
    {
        lock (sidecarGate)
        {
            sidecarLoadInFlight = false;
            if (!disposed) completedSidecarLoad = result;
        }
    }

    private static string ReadBoundedSidecar(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length <= 0 || stream.Length > MaximumSidecarBytes)
                throw new InvalidDataException("sidecar-size-out-of-bounds");
            byte[] bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
            if (stream.ReadByte() != -1) throw new InvalidDataException("sidecar-size-out-of-bounds");
            using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true))
                return reader.ReadToEnd();
        }
    }

    private void DrainSidecarLoad()
    {
        SidecarLoadResult result;
        lock (sidecarGate)
        {
            result = completedSidecarLoad;
            completedSidecarLoad = null;
        }

        if (result == null) return;

        bool current = hasCurrentOwner && result.Owner.Matches(currentOwner);
        if (result.Error == "sidecar-not-ready" && current)
        {
            lock (sidecarGate)
                loadPolicy.Defer(result.Owner,
                    Time.realtimeSinceStartup + SidecarRetrySeconds, MaximumSidecarProbesPerPlayback);
            return;
        }

        lock (sidecarGate) loadPolicy.MarkFinal(result.Owner);
        if (!current || result.Timeline == null) return;

        timeline = result.Timeline;
        timelineOwner = result.Owner;
        hasTimelineOwner = true;
    }

    private long NextGeneration()
    {
        unchecked
        {
            generation++;
            if (generation <= 0) generation = 1;
            return generation;
        }
    }

    private void ResetCurrentOwner()
    {
        hasCurrentOwner = false;
        hasTimelineOwner = false;
        timeline = null;
        presentationRequired = false;
        preparedPresentation = null;
        currentEvidence = null;
        currentClip = null;
        amplitudeFallback.Reset();
    }

    private static SplitPortraitSpeechRead Unavailable()
    {
        return new SplitPortraitSpeechRead(SplitPortraitSpeechPlaybackStatus.Unavailable,
            PortraitMouthShape.X, null, false);
    }
}
