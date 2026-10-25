using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

/// <summary>
/// Reads the trial coordinator's bounded PCM stream and feeds the bridge AudioSource.
/// Chunk/frame positions are transport data. They are not word alignment.
/// </summary>
public sealed class BreezeTtsStreamingPcmAdapter : MonoBehaviour
{
    public enum PlaybackState { Idle, Buffering, Playing, Draining, Completed, Cancelled, Failed }

    public sealed class PlaybackReceipt
    {
        public string StreamId;
        public string ConversationToken;
        public string PersonId;
        public string Status;
        public string CompletedSentenceText;
        public bool InterruptedMidSentence;
        public long PlaybackCursorFrames;
        public long UnderrunFrames;
    }

    [Serializable]
    private sealed class StreamManifest
    {
        public int schemaVersion = 0;
        public string streamId = null;
        public string conversationToken = null;
        public string personId = null;
        public string encoding = null;
        public int sampleRate = 0;
        public int channels = 0;
        public string status = null;
        public int nextChunkSequence = -1;
        public int consumedSentenceSequence = -1;
        public long totalSampleFrames = -1;
        public string errorCode = null;
    }

    [Serializable]
    private sealed class ChunkManifest
    {
        public int schemaVersion = 0;
        public string streamId = null;
        public string conversationToken = null;
        public string personId = null;
        public int chunkSequence = -1;
        public int sentenceSequence = -1;
        public long firstSampleFrame = -1;
        public int sampleFrames = -1;
        public int channels = 0;
        public int sampleRate = 0;
        public string encoding = null;
        public int byteLength = -1;
        public string sha256 = null;
    }

    private sealed class PcmBlock
    {
        public int ChunkSequence;
        public int SentenceSequence;
        public long FirstSampleFrame;
        public float[] Samples;
        public int Offset;
    }

    private sealed class OutputRange
    {
        public int ChunkSequence;
        public int SentenceSequence;
        public long OutputStart;
        public long OutputEnd;
        public long SourceStart;
        public long SourceEnd;
    }

    private sealed class OwnedStreamResponse
    {
        public string StreamId;
        public string ConversationToken;
        public string PersonId;
        public string ResponseRoot;
        public int ExpectedChunkCount;
        public bool TerminalObserved;
        public bool CleanupRequested;
    }

    private const int SampleRate = 24000;
    private const int Channels = 1;
    private const int ChunkFrameLimit = 4800;
    private const int RingFrameLimit = SampleRate * 2;
    private const int PlaybackClipFrameLimit = SampleRate * 180;
    private const int ManifestByteLimit = 8192;
    private const int SentenceLimit = 8;
    private const int SentenceCharacterLimit = 480;
    private const float PollSeconds = 0.025f;
    private const float CleanupWaitSeconds = 5f;

    private readonly object audioGate = new object();
    private readonly Queue<PcmBlock> blocks = new Queue<PcmBlock>();
    private readonly List<OutputRange> outputRanges = new List<OutputRange>();
    private readonly SortedDictionary<int, string> sentenceText = new SortedDictionary<int, string>();
    private readonly SortedDictionary<int, long> sentenceOutputEnds = new SortedDictionary<int, long>();
    private readonly SortedDictionary<int, long> sentenceOutputStarts = new SortedDictionary<int, long>();
    private readonly SortedDictionary<int, long> sentenceFirstSourceFrames = new SortedDictionary<int, long>();
    private readonly SortedDictionary<int, long> sentenceConsumedSourceFrames = new SortedDictionary<int, long>();
    private readonly SortedDictionary<int, int> sentenceLastChunk = new SortedDictionary<int, int>();
    private readonly SortedDictionary<int, string> queuedSentenceText = new SortedDictionary<int, string>();
    private readonly Dictionary<int, long> renderedChunkOutputEnds = new Dictionary<int, long>();
    private readonly Dictionary<string, OwnedStreamResponse> ownedResponses =
        new Dictionary<string, OwnedStreamResponse>(StringComparer.Ordinal);

    private BreezeTtsTrialBridge bridge;
    private AudioSource source;
    private AudioClip streamClip;
    private string responseRoot;
    private string streamDirectory;
    private string chunksDirectory;
    private string streamId;
    private string conversationToken;
    private string personId;
    private string terminalStatus;
    private string failureCode;
    private string completedSentenceText = string.Empty;
    private string outputRootCanonical;
    private int nextChunkSequence;
    private int highestAckedChunk = -1;
    private int consumedSentenceSequence = -1;
    private long nextSourceSampleFrame;
    private long renderedOutputFrames;
    private long lastSentenceEndSourceFrame;
    private long underrunFrames;
    private long bufferedFrames;
    private long completedOutputFrames;
    private long finalOutputEndFrame;
    private long expectedTotalSampleFrames;
    private long terminalPlaybackCursorFrames = -1;
    private float nextPollTime;
    private float nextOwnedResponseCleanupSweep;
    private float cancellationCleanupDeadline;
    private float streamStartedAt;
    private bool clipStarted;
    private volatile bool terminalSeen;
    private volatile bool responseTerminalObserved;
    private volatile int latestObservedSentenceSequence = -1;
    private bool cancellationRequested;
    private volatile bool seekRequested;
    private bool deleteFinalAudioOnCleanup;
    private bool finishedNotified;
    private int expectedChunkCount;
    private PlaybackState state;

    public event Action<PlaybackReceipt> PlaybackFinished;

    public PlaybackState State { get { return state; } }
    public string StreamId { get { return streamId; } }
    public string ConversationToken { get { return conversationToken; } }
    public string PersonId { get { return personId; } }
    public string CompletedSentenceText { get { return completedSentenceText ?? string.Empty; } }
    public int ActiveSampleRate { get { return SampleRate; } }
    public long ExpectedTotalSampleFrames { get { return expectedTotalSampleFrames; } }
    public long PlaybackCursorFrames
    {
        get
        {
            if (source != null && source.clip == streamClip &&
                (source.isPlaying || terminalPlaybackCursorFrames < 0))
                return source.timeSamples;
            return terminalPlaybackCursorFrames >= 0 ? terminalPlaybackCursorFrames : 0;
        }
    }
    public long UnderrunFrames { get { return underrunFrames; } }

    public bool Begin(BreezeTtsTrialBridge owner, string streamResponseRoot,
        string id, string token, string actorId)
    {
        if (owner == null || owner.OutputAudioSource == null || string.IsNullOrWhiteSpace(streamResponseRoot) ||
            !Path.IsPathRooted(streamResponseRoot) || string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(actorId) ||
            (state != PlaybackState.Idle && state != PlaybackState.Completed && state != PlaybackState.Cancelled && state != PlaybackState.Failed))
            return false;

        try
        {
            outputRootCanonical = Path.GetFullPath(streamResponseRoot);
            responseRoot = outputRootCanonical;
            streamDirectory = Path.GetFullPath(Path.Combine(responseRoot, id));
            if (!IsDirectChild(responseRoot, streamDirectory)) return false;
            chunksDirectory = Path.Combine(streamDirectory, "chunks");
            bridge = owner;
            source = owner.OutputAudioSource;
            ResetAudioSource();
            streamId = id;
            conversationToken = token;
            personId = actorId;
            ownedResponses[id] = new OwnedStreamResponse { StreamId = id, ConversationToken = token,
                PersonId = actorId, ResponseRoot = outputRootCanonical };
            terminalStatus = null;
            failureCode = null;
            nextChunkSequence = 0;
            highestAckedChunk = -1;
            consumedSentenceSequence = -1;
            nextSourceSampleFrame = 0;
            renderedOutputFrames = 0;
            finalOutputEndFrame = 0;
            expectedTotalSampleFrames = 0;
            terminalPlaybackCursorFrames = -1;
            lastSentenceEndSourceFrame = 0;
            completedOutputFrames = 0;
            bufferedFrames = 0;
            underrunFrames = 0;
            completedSentenceText = string.Empty;
            cancellationRequested = false;
            terminalSeen = false;
            responseTerminalObserved = false;
            seekRequested = false;
            deleteFinalAudioOnCleanup = false;
            clipStarted = false;
            finishedNotified = false;
            sentenceText.Clear();
            sentenceLastChunk.Clear();
            sentenceOutputEnds.Clear();
            sentenceOutputStarts.Clear();
            sentenceFirstSourceFrames.Clear();
            sentenceConsumedSourceFrames.Clear();
            queuedSentenceText.Clear();
            renderedChunkOutputEnds.Clear();
            latestObservedSentenceSequence = -1;
            expectedChunkCount = 0;
            lock (audioGate)
            {
                blocks.Clear();
                outputRanges.Clear();
            }
            streamClip = AudioClip.Create("People of the Iliac Bay live TTS", PlaybackClipFrameLimit,
                Channels, SampleRate, true, ReadPcm, SetPcmPosition);
            if (streamClip == null) throw new InvalidOperationException("stream-audio-clip");
            source.loop = false;
            source.playOnAwake = false;
            source.clip = streamClip;
            if (!owner.BeginStreamingMixerObservation(streamId, conversationToken, personId, streamClip, SampleRate))
                throw new InvalidOperationException("stream-mixer-binding");
            state = PlaybackState.Buffering;
            streamStartedAt = Time.unscaledTime;
            nextPollTime = 0;
            cancellationCleanupDeadline = 0;
            owner.StreamingCancellationRequested += OnBridgeCancellation;
            return true;
        }
        catch (Exception error)
        {
            Debug.LogWarning("[PeopleOfTheIliacBay] Live TTS adapter start failed: " + error.GetType().Name);
            ResetAudioSource();
            state = PlaybackState.Failed;
            return false;
        }
    }

    public bool TryQueueSentence(int sequence, string exactText)
    {
        if (state == PlaybackState.Idle || terminalSeen || string.IsNullOrWhiteSpace(exactText) ||
            sequence < 0 || exactText.Length > 240 || sentenceText.ContainsKey(sequence)) return false;
        PollStreamManifest();
        ReleaseConsumedSentenceText();
        if (queuedSentenceText.Count >= SentenceLimit || SumQueuedSentenceCharacters() + exactText.Length > SentenceCharacterLimit)
            return false;
        sentenceText.Add(sequence, exactText);
        queuedSentenceText.Add(sequence, exactText);
        return true;
    }

    public void MarkTerminal(string status, string errorCode = null)
    {
        if (state == PlaybackState.Idle || terminalSeen) return;
        if (status != "audio_complete" && status != "failed" && status != "cancelled") return;
        terminalStatus = status;
        failureCode = errorCode;
        terminalSeen = true;
        if (status == "failed" || status == "cancelled") state = PlaybackState.Draining;
    }

    public PlaybackReceipt StopNow(string reason = "conversation-ended")
    {
        if (state == PlaybackState.Idle) return null;
        PollStreamManifest();
        PollAvailableChunks();
        CloseLatestConsumedSentence();
        cancellationRequested = true;
        terminalStatus = "cancelled";
        terminalSeen = true;
        UpdatePlayedSentenceText();
        long cursor = PlaybackCursorFrames;
        terminalPlaybackCursorFrames = cursor;
        if (source != null && source.isPlaying) source.Stop();
        state = PlaybackState.Cancelled;
        if (bridge != null) bridge.StopStreamingMixerPlayback(streamId, conversationToken, personId);
        PlaybackReceipt receipt = CreateReceipt("cancelled", cursor);
        NotifyFinished(receipt);
        RequestCleanupAfterTerminal();
        if (bridge != null) bridge.StreamingCancellationRequested -= OnBridgeCancellation;
        return receipt;
    }

    public void CancelFromOwner(string reason = "conversation-ended")
    {
        if (bridge == null || string.IsNullOrEmpty(streamId) ||
            !bridge.CancelStreamingReply(streamId, conversationToken, personId, reason))
            StopNow(reason);
    }

    /// <summary>Deletes exact response artifacts for a closed conversation; measurements stay intact.</summary>
    public void CleanupConversationResponses(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        var snapshot = new List<OwnedStreamResponse>(ownedResponses.Values);
        foreach (OwnedStreamResponse response in snapshot)
        {
            if (response == null || response.ConversationToken != token) continue;
            response.CleanupRequested = true;
            if (response.TerminalObserved) CleanupOwnedResponse(response);
        }
    }

    public bool TryGetPlaybackPosition(out int sampleRate, out long sampleFrame)
    {
        sampleRate = SampleRate;
        sampleFrame = PlaybackCursorFrames;
        return state == PlaybackState.Playing || state == PlaybackState.Draining ||
            state == PlaybackState.Completed;
    }

    /// <summary>Reads the active sentence's local 24 kHz frame cursor for validated sentence timelines.</summary>
    public bool TryGetSentencePlaybackPosition(int sentenceSequence, out int sampleRate, out long sampleFrame)
    {
        sampleRate = SampleRate;
        sampleFrame = 0;
        bool terminalCursorAvailable = (state == PlaybackState.Cancelled || state == PlaybackState.Failed) &&
            terminalPlaybackCursorFrames >= 0;
        if (sentenceSequence < 0 ||
            (!terminalCursorAvailable && state != PlaybackState.Playing && state != PlaybackState.Draining)) return false;

        long cursor = PlaybackCursorFrames;
        lock (audioGate)
        {
            long start;
            if (!sentenceOutputStarts.TryGetValue(sentenceSequence, out start) || cursor < start) return false;
            long end;
            if (sentenceOutputEnds.TryGetValue(sentenceSequence, out end) && cursor >= end) return false;
            long firstSourceFrame;
            if (!sentenceFirstSourceFrames.TryGetValue(sentenceSequence, out firstSourceFrame)) return false;
            long lastConsumedSourceFrame;
            if (!sentenceConsumedSourceFrames.TryGetValue(sentenceSequence, out lastConsumedSourceFrame))
                lastConsumedSourceFrame = firstSourceFrame - 1;
            var mappings = new List<PcmPlaybackFrameRange>();
            for (int index = 0; index < outputRanges.Count; index++)
            {
                OutputRange range = outputRanges[index];
                if (range.SentenceSequence == sentenceSequence)
                    mappings.Add(new PcmPlaybackFrameRange(range.OutputStart, range.OutputEnd,
                        range.SourceStart, range.SourceEnd, range.SentenceSequence));
            }
            long sourceCursorFrame;
            if (!PcmPlaybackFrameMapper.TryMapSentenceCursor(mappings, sentenceSequence,
                cursor, firstSourceFrame, lastConsumedSourceFrame, out sourceCursorFrame)) return false;
            sentenceConsumedSourceFrames[sentenceSequence] = sourceCursorFrame;
            sampleFrame = sourceCursorFrame - firstSourceFrame;
            return true;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextOwnedResponseCleanupSweep)
        {
            nextOwnedResponseCleanupSweep = Time.unscaledTime + 0.25f;
            SweepOwnedResponseCleanup();
        }
        if (seekRequested)
        {
            seekRequested = false;
            CancelFromOwner("audio-seek");
        }
        if (cancellationCleanupDeadline > 0 && Time.unscaledTime >= cancellationCleanupDeadline)
            CleanupExactResponseFiles();
        if (cancellationRequested)
        {
            if (!responseTerminalObserved && Time.unscaledTime >= nextPollTime)
            {
                nextPollTime = Time.unscaledTime + PollSeconds;
                PollStreamManifest();
            }
            if (bridge != null) bridge.MarkStreamingPlaybackDrained(streamId, conversationToken, personId);
            OwnedStreamResponse pendingCleanup;
            if (ownedResponses.TryGetValue(streamId ?? string.Empty, out pendingCleanup) &&
                pendingCleanup.CleanupRequested && pendingCleanup.TerminalObserved)
                CleanupOwnedResponse(pendingCleanup);
            return;
        }
        if (state == PlaybackState.Idle || state == PlaybackState.Completed ||
            state == PlaybackState.Cancelled || state == PlaybackState.Failed) return;

        if (Time.unscaledTime - streamStartedAt > 320f)
        {
            Fail("stream-playback-timeout");
            return;
        }

        if (bridge == null || !IsBoundToCurrentDirectory())
        {
            Fail("response-root-changed");
            return;
        }

        if (Time.unscaledTime >= nextPollTime)
        {
            nextPollTime = Time.unscaledTime + PollSeconds;
            if (!terminalSeen) PollStreamManifest();
            PollAvailableChunks();
        }

        if (!clipStarted && ShouldStartPlayback())
        {
            source.timeSamples = 0;
            source.Play();
            clipStarted = true;
            state = PlaybackState.Playing;
            if (bridge == null || !bridge.MarkStreamingMixerPlaybackStarted(streamId, conversationToken, personId))
            {
                Fail("stream-mixer-start-binding");
                return;
            }
        }

        if (clipStarted)
        {
            AcknowledgePlayedChunks();
            UpdatePlayedSentenceText();
            if (renderedOutputFrames >= PlaybackClipFrameLimit)
            {
                Fail("playback-frame-limit");
                return;
            }
            if (terminalSeen && HasDrainedAudio())
                FinishTerminalPlayback();
        }
        else if (terminalSeen && nextChunkSequence >= expectedChunkCount && BufferedFrameCount() == 0)
            FinishTerminalPlayback();

        if (cancellationRequested && Time.unscaledTime >= cancellationCleanupDeadline)
            CleanupExactResponseFiles();
    }

    private void PollStreamManifest()
    {
        string path = Path.Combine(streamDirectory ?? string.Empty, "stream.json");
        if (!File.Exists(path)) return;
        try
        {
            StreamManifest value = JsonUtility.FromJson<StreamManifest>(
                ReadSharedTextBounded(path, 2, ManifestByteLimit));
            if (value == null || value.schemaVersion != 1 || value.streamId != streamId ||
                value.conversationToken != conversationToken || value.personId != personId ||
                value.encoding != "pcm_s16le" || value.sampleRate != SampleRate || value.channels != Channels ||
                value.nextChunkSequence < 0 || value.totalSampleFrames < 0 ||
                (value.status != "open" && value.status != "audio_complete" && value.status != "failed" && value.status != "cancelled"))
                throw new InvalidDataException("stream-manifest-binding");

            if (value.consumedSentenceSequence > consumedSentenceSequence)
            {
                consumedSentenceSequence = value.consumedSentenceSequence;
                ReleaseConsumedSentenceText();
            }
            expectedChunkCount = Math.Max(expectedChunkCount, value.nextChunkSequence);
            expectedTotalSampleFrames = Math.Max(expectedTotalSampleFrames, value.totalSampleFrames);
            if (value.status != "open")
            {
                responseTerminalObserved = true;
                OwnedStreamResponse owned;
                if (ownedResponses.TryGetValue(streamId, out owned))
                {
                    owned.TerminalObserved = true;
                    owned.ExpectedChunkCount = Math.Max(owned.ExpectedChunkCount, value.nextChunkSequence);
                }
                terminalStatus = value.status;
                failureCode = value.errorCode;
                terminalSeen = true;
                if (value.totalSampleFrames < nextSourceSampleFrame)
                    throw new InvalidDataException("stream-terminal-length");
                if (value.status == "failed" || value.status == "cancelled") state = PlaybackState.Draining;
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning("[PeopleOfTheIliacBay] Live TTS stream manifest rejected: " + error.GetType().Name);
            Fail("invalid-stream-manifest");
        }
    }

    private void PollAvailableChunks()
    {
        if (terminalSeen && terminalStatus == "cancelled") return;
        while (nextChunkSequence < 100000 && BufferedFrameCount() < RingFrameLimit)
        {
            string stem = nextChunkSequence.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
            string pcmPath = Path.Combine(chunksDirectory, stem + ".pcm");
            string metadataPath = Path.Combine(chunksDirectory, stem + ".json");
            if (!File.Exists(pcmPath) || !File.Exists(metadataPath))
            {
                // The producer may wait for this consumed sentence before publishing the next one.
                // Close the latest sentence from the manifest's consumed-sequence boundary even
                // when the next chunk has not been published yet.
                CloseLatestConsumedSentence();
                return;
            }

            try
            {
                byte[] bytes = ReadSharedBytesBounded(pcmPath, 2, ChunkFrameLimit * 2);
                byte[] metadataBytes = ReadSharedBytesBounded(metadataPath, 2, ManifestByteLimit);
                if (bytes.Length < 2 || bytes.Length > ChunkFrameLimit * 2 || (bytes.Length & 1) != 0 ||
                    metadataBytes.Length < 2 || metadataBytes.Length > ManifestByteLimit)
                    throw new InvalidDataException("chunk-size");
                string metadataJson = Encoding.UTF8.GetString(metadataBytes);
                if (metadataJson.Length > 0 && metadataJson[0] == '\ufeff') metadataJson = metadataJson.Substring(1);
                ChunkManifest meta = JsonUtility.FromJson<ChunkManifest>(metadataJson);
                if (meta == null || meta.schemaVersion != 1 || meta.streamId != streamId ||
                    meta.conversationToken != conversationToken || meta.personId != personId ||
                    meta.chunkSequence != nextChunkSequence || meta.sentenceSequence < 0 ||
                    meta.firstSampleFrame != nextSourceSampleFrame || meta.sampleFrames < 1 ||
                    meta.sampleFrames > ChunkFrameLimit || meta.channels != Channels || meta.sampleRate != SampleRate ||
                    meta.encoding != "pcm_s16le" || meta.byteLength != bytes.Length ||
                    bytes.Length != meta.sampleFrames * 2 || !string.Equals(meta.sha256, Sha256(bytes), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("chunk-binding-or-hash");

                var samples = new float[meta.sampleFrames];
                for (int index = 0; index < samples.Length; index++)
                {
                    short value = unchecked((short)(bytes[index * 2] | (bytes[index * 2 + 1] << 8)));
                    samples[index] = value / 32768f;
                }
                if (meta.sentenceSequence > latestObservedSentenceSequence)
                {
                    if (latestObservedSentenceSequence >= 0)
                    {
                        int previousChunk;
                        if (sentenceLastChunk.TryGetValue(latestObservedSentenceSequence, out previousChunk))
                            CloseSentence(latestObservedSentenceSequence, previousChunk);
                    }
                    latestObservedSentenceSequence = meta.sentenceSequence;
                }
                lock (audioGate)
                {
                    sentenceLastChunk[meta.sentenceSequence] = meta.chunkSequence;
                    blocks.Enqueue(new PcmBlock { ChunkSequence = meta.chunkSequence,
                        SentenceSequence = meta.sentenceSequence, FirstSampleFrame = meta.firstSampleFrame, Samples = samples });
                    bufferedFrames += samples.Length;
                }
                nextSourceSampleFrame += meta.sampleFrames;
                nextChunkSequence++;
            }
            catch (Exception error)
            {
                Debug.LogWarning("[PeopleOfTheIliacBay] Live TTS chunk rejected: " + error.GetType().Name);
                Fail("invalid-pcm-chunk");
                return;
            }
        }
        CloseLatestConsumedSentence();
    }

    private void CloseLatestConsumedSentence()
    {
        if (latestObservedSentenceSequence < 0 || latestObservedSentenceSequence > consumedSentenceSequence ||
            nextChunkSequence < expectedChunkCount) return;
        int lastChunk;
        if (sentenceLastChunk.TryGetValue(latestObservedSentenceSequence, out lastChunk))
            CloseSentence(latestObservedSentenceSequence, lastChunk);
    }

    private void ReadPcm(float[] data)
    {
        if (data == null) return;
        int offset = 0;
        lock (audioGate)
        {
            while (offset < data.Length && blocks.Count > 0)
            {
                PcmBlock block = blocks.Peek();
                int count = Math.Min(data.Length - offset, block.Samples.Length - block.Offset);
                long sourceStart = block.FirstSampleFrame + block.Offset;
                long sourceEnd = sourceStart + count;
                Array.Copy(block.Samples, block.Offset, data, offset, count);
                if (!sentenceOutputStarts.ContainsKey(block.SentenceSequence))
                    sentenceOutputStarts[block.SentenceSequence] = renderedOutputFrames + offset;
                if (!sentenceFirstSourceFrames.ContainsKey(block.SentenceSequence))
                    sentenceFirstSourceFrames[block.SentenceSequence] = sourceStart;
                outputRanges.Add(new OutputRange { ChunkSequence = block.ChunkSequence,
                    SentenceSequence = block.SentenceSequence, OutputStart = renderedOutputFrames + offset,
                    OutputEnd = renderedOutputFrames + offset + count,
                    SourceStart = sourceStart, SourceEnd = sourceEnd });
                block.Offset += count;
                bufferedFrames -= count;
                offset += count;
                if (block.Offset >= block.Samples.Length)
                {
                    blocks.Dequeue();
                    long outputEnd = renderedOutputFrames + offset;
                    renderedChunkOutputEnds[block.ChunkSequence] = outputEnd;
                    int lastChunk;
                    if (sentenceLastChunk.TryGetValue(block.SentenceSequence, out lastChunk) &&
                        lastChunk == block.ChunkSequence && IsSentenceClosed(block.SentenceSequence))
                        sentenceOutputEnds[block.SentenceSequence] = outputEnd;
                }
            }
            if (offset < data.Length)
            {
                Array.Clear(data, offset, data.Length - offset);
                underrunFrames += data.Length - offset;
            }
            renderedOutputFrames += data.Length;
            if (offset > 0)
                finalOutputEndFrame = Math.Max(finalOutputEndFrame, renderedOutputFrames - (data.Length - offset));
        }
    }

    private void SetPcmPosition(int position)
    {
        if (position == 0) return;
        // Seeking would break the source-sample to playback-cursor mapping.
        seekRequested = true;
    }

    private void AcknowledgePlayedChunks()
    {
        long cursor = PlaybackCursorFrames;
        int nextAck = highestAckedChunk;
        lock (audioGate)
        {
            for (int index = 0; index < outputRanges.Count; index++)
            {
                OutputRange range = outputRanges[index];
                if (cursor > range.OutputStart && sentenceFirstSourceFrames.ContainsKey(range.SentenceSequence))
                {
                    long consumedFrames = Math.Min(cursor, range.OutputEnd) - range.OutputStart;
                    long consumedSourceEnd = range.SourceStart + consumedFrames;
                    long previousSourceEnd;
                    if (!sentenceConsumedSourceFrames.TryGetValue(range.SentenceSequence, out previousSourceEnd) ||
                        consumedSourceEnd > previousSourceEnd)
                        sentenceConsumedSourceFrames[range.SentenceSequence] = consumedSourceEnd;
                }
                if (range.OutputEnd <= cursor && range.ChunkSequence > nextAck)
                    nextAck = range.ChunkSequence;
            }
        }
        // File publication must stay outside audioGate: ReadPcm runs on Unity's audio thread.
        if (nextAck <= highestAckedChunk || !WriteAck(nextAck)) return;
        lock (audioGate)
        {
            highestAckedChunk = Math.Max(highestAckedChunk, nextAck);
            outputRanges.RemoveAll(range => range.ChunkSequence <= highestAckedChunk);
        }
    }

    private bool WriteAck(int sequence)
    {
        try
        {
            string json = "{\"schemaVersion\":1,\"streamId\":" + Quote(streamId) +
                ",\"conversationToken\":" + Quote(conversationToken) +
                ",\"personId\":" + Quote(personId) +
                ",\"consumedChunkSequence\":" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
            BreezeTtsTrialProtocol.ReplaceAtomically(streamDirectory, "ack.json", json);
            return true;
        }
        catch (Exception error)
        {
            Debug.LogWarning("[PeopleOfTheIliacBay] Live TTS acknowledgement failed: " + error.GetType().Name);
            return false;
        }
    }

    private void UpdatePlayedSentenceText()
    {
        long cursor = PlaybackCursorFrames;
        lock (audioGate)
        {
            var completed = new List<int>();
            foreach (var pair in sentenceOutputEnds)
                if (pair.Value <= cursor && sentenceText.ContainsKey(pair.Key)) completed.Add(pair.Key);
            completed.Sort();
            for (int index = 0; index < completed.Count; index++)
            {
                string text;
                long endFrame;
                if (sentenceText.TryGetValue(completed[index], out text))
                {
                    if (completedSentenceText.Length > 0) completedSentenceText += " ";
                    completedSentenceText += text;
                }
                if (sentenceOutputEnds.TryGetValue(completed[index], out endFrame))
                    lastSentenceEndSourceFrame = Math.Max(lastSentenceEndSourceFrame, endFrame);
                sentenceText.Remove(completed[index]);
                sentenceOutputEnds.Remove(completed[index]);
                sentenceOutputStarts.Remove(completed[index]);
                sentenceFirstSourceFrames.Remove(completed[index]);
                sentenceConsumedSourceFrames.Remove(completed[index]);
            }
            completedOutputFrames = Math.Max(completedOutputFrames, cursor);
        }
    }

    private bool ShouldStartPlayback()
    {
        long buffered = BufferedFrameCount();
        return buffered >= SampleRate / 5 || (terminalSeen && buffered > 0);
    }

    private bool HasDrainedAudio()
    {
        long cursor = PlaybackCursorFrames;
        lock (audioGate)
        {
            if (blocks.Count > 0) return false;
        }
        return terminalSeen && nextChunkSequence >= expectedChunkCount && cursor >= finalOutputEndFrame;
    }

    private void FinishTerminalPlayback()
    {
        string status = terminalStatus == "audio_complete" ? "completed" :
            terminalStatus == "failed" ? "failed" : "cancelled";
        UpdatePlayedSentenceText();
        long cursor = PlaybackCursorFrames;
        terminalPlaybackCursorFrames = cursor;
        if (source != null && source.isPlaying) source.Stop();
        state = status == "completed" ? PlaybackState.Completed :
            status == "failed" ? PlaybackState.Failed : PlaybackState.Cancelled;
        if (bridge != null) bridge.StopStreamingMixerPlayback(streamId, conversationToken, personId);
        NotifyFinished(CreateReceipt(status, cursor));
        if (bridge != null) bridge.MarkStreamingPlaybackDrained(streamId, conversationToken, personId);
        if (bridge != null) bridge.StreamingCancellationRequested -= OnBridgeCancellation;
        CleanupExactResponseFiles();
    }

    private void Fail(string code)
    {
        if (terminalSeen && state == PlaybackState.Failed) return;
        terminalSeen = true;
        cancellationRequested = true;
        terminalStatus = "failed";
        failureCode = code;
        UpdatePlayedSentenceText();
        long cursor = PlaybackCursorFrames;
        terminalPlaybackCursorFrames = cursor;
        if (source != null && source.isPlaying) source.Stop();
        state = PlaybackState.Failed;
        if (bridge != null) bridge.StopStreamingMixerPlayback(streamId, conversationToken, personId);
        PlaybackReceipt receipt = CreateReceipt("failed", cursor);
        NotifyFinished(receipt);
        RequestCleanupAfterTerminal();
        if (bridge != null) bridge.CancelStreamingReply(streamId, conversationToken, personId, code);
        if (bridge != null) bridge.MarkStreamingPlaybackDrained(streamId, conversationToken, personId);
        if (bridge != null) bridge.StreamingCancellationRequested -= OnBridgeCancellation;
    }

    private PlaybackReceipt CreateReceipt(string status, long cursorOverride = -1)
    {
        long cursor = cursorOverride >= 0 ? cursorOverride : PlaybackCursorFrames;
        bool midSentence = HasUncompletedAudibleSentence(cursor);
        return new PlaybackReceipt { StreamId = streamId, ConversationToken = conversationToken,
            PersonId = personId, Status = status, CompletedSentenceText = completedSentenceText,
            InterruptedMidSentence = midSentence, PlaybackCursorFrames = cursor, UnderrunFrames = underrunFrames };
    }

    private bool HasUncompletedAudibleSentence(long cursor)
    {
        lock (audioGate)
            foreach (var pair in sentenceText)
            {
                long start;
                if (!sentenceOutputStarts.TryGetValue(pair.Key, out start) || start >= cursor) continue;
                long end;
                if (!sentenceOutputEnds.TryGetValue(pair.Key, out end) || cursor < end) return true;
            }
        return false;
    }

    private void NotifyFinished(PlaybackReceipt receipt)
    {
        if (finishedNotified) return;
        finishedNotified = true;
        Action<PlaybackReceipt> callback = PlaybackFinished;
        if (callback != null)
        {
            try { callback(receipt); }
            catch (Exception error) { Debug.LogWarning("[PeopleOfTheIliacBay] Live TTS completion callback failed: " + error.GetType().Name); }
        }
    }

    private void OnBridgeCancellation(string id, string token)
    {
        if (id != streamId || token != conversationToken || finishedNotified) return;
        StopNow("bridge-cancelled");
    }

    private void RequestCleanupAfterTerminal()
    {
        cancellationCleanupDeadline = Time.unscaledTime + CleanupWaitSeconds;
        if (!terminalSeen) return;
        // Coordinator sees the bridge cancel marker before this cleanup window.
    }

    private void CleanupExactResponseFiles()
    {
        if (string.IsNullOrEmpty(streamDirectory) || string.IsNullOrEmpty(chunksDirectory) ||
            !IsDirectChild(outputRootCanonical, streamDirectory) || !responseTerminalObserved) return;
        try
        {
            if (Directory.Exists(chunksDirectory))
            {
                for (int sequence = 0; sequence < expectedChunkCount; sequence++)
                {
                    string stem = sequence.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
                    TryDeleteExactFile(Path.Combine(chunksDirectory, stem + ".pcm"));
                    TryDeleteExactFile(Path.Combine(chunksDirectory, stem + ".json"));
                }
                if (Directory.GetFileSystemEntries(chunksDirectory).Length == 0) Directory.Delete(chunksDirectory, false);
            }
            TryDeleteExactFile(Path.Combine(streamDirectory, "ack.json"));
            OwnedStreamResponse response;
            if (ownedResponses.TryGetValue(streamId, out response) &&
                (deleteFinalAudioOnCleanup || response.CleanupRequested))
            {
                if (deleteFinalAudioOnCleanup) response.CleanupRequested = true;
                if (response.TerminalObserved) CleanupOwnedResponse(response);
            }
            cancellationRequested = false;
            cancellationCleanupDeadline = 0;
        }
        catch (Exception error)
        { Debug.LogWarning("[PeopleOfTheIliacBay] Live TTS response cleanup deferred: " + error.GetType().Name); }
    }

    private void CleanupOwnedResponse(OwnedStreamResponse response)
    {
        if (response == null || !response.TerminalObserved || string.IsNullOrEmpty(response.ResponseRoot) ||
            !Guid.TryParseExact(response.StreamId, "N", out Guid ignored)) return;
        try
        {
            string root = Path.GetFullPath(response.ResponseRoot);
            string streamDirectory = Path.GetFullPath(Path.Combine(root, response.StreamId));
            if (!IsDirectChild(root, streamDirectory)) return;
            string chunksDirectory = Path.Combine(streamDirectory, "chunks");
            if (Directory.Exists(chunksDirectory))
            {
                for (int sequence = 0; sequence < response.ExpectedChunkCount; sequence++)
                {
                    string stem = sequence.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
                    TryDeleteExactFile(Path.Combine(chunksDirectory, stem + ".pcm"));
                    TryDeleteExactFile(Path.Combine(chunksDirectory, stem + ".json"));
                }
                if (Directory.GetFileSystemEntries(chunksDirectory).Length == 0) Directory.Delete(chunksDirectory, false);
            }
            string sentencesDirectory = Path.GetFullPath(Path.Combine(streamDirectory, "sentences"));
            if (Directory.Exists(sentencesDirectory) && IsDirectChild(streamDirectory, sentencesDirectory))
            {
                for (int sequence = 0; sequence < 256; sequence++)
                {
                    string stem = response.StreamId + "." + sequence.ToString("D4",
                        System.Globalization.CultureInfo.InvariantCulture);
                    TryDeleteExactFile(Path.Combine(sentencesDirectory, stem + ".wav"));
                    TryDeleteExactFile(Path.Combine(sentencesDirectory, stem + ".alignment.json"));
                    TryDeleteExactFile(Path.Combine(sentencesDirectory, stem + ".visemes.json"));
                }
                // Final-scope sidecars are exact known names in the same response directory.
                TryDeleteExactFile(Path.Combine(sentencesDirectory, response.StreamId + ".alignment.json"));
                TryDeleteExactFile(Path.Combine(sentencesDirectory, response.StreamId + ".visemes.json"));
                if (Directory.GetFileSystemEntries(sentencesDirectory).Length == 0)
                    Directory.Delete(sentencesDirectory, false);
            }
            TryDeleteExactFile(Path.Combine(streamDirectory, "ack.json"));
            TryDeleteExactFile(Path.Combine(streamDirectory, "stream.json"));
            if (Directory.Exists(streamDirectory) && Directory.GetFileSystemEntries(streamDirectory).Length == 0)
                Directory.Delete(streamDirectory, false);
            string[] suffixes = { ".wav", ".manifest.json", ".alignment.json", ".visemes.json" };
            for (int index = 0; index < suffixes.Length; index++)
            {
                string path = Path.GetFullPath(Path.Combine(root, response.StreamId + suffixes[index]));
                if (IsDirectChild(root, path)) TryDeleteExactFile(path);
            }

            // Postprocessing may still have a temporary staging directory inside the owned
            // response tree. Keep the response binding so the bounded Update sweep can retry
            // exact-file cleanup after that worker releases it.
            if (Directory.Exists(streamDirectory) && Directory.GetFileSystemEntries(streamDirectory).Length != 0)
                return;
            if (Directory.Exists(streamDirectory)) Directory.Delete(streamDirectory, false);
            for (int index = 0; index < suffixes.Length; index++)
            {
                string path = Path.GetFullPath(Path.Combine(root, response.StreamId + suffixes[index]));
                if (IsDirectChild(root, path) && File.Exists(path)) return;
            }
            response.CleanupRequested = false;
            OwnedStreamResponse current;
            if (ownedResponses.TryGetValue(response.StreamId, out current) && ReferenceEquals(current, response))
                ownedResponses.Remove(response.StreamId);
        }
        catch (Exception error)
        { Debug.LogWarning("[PeopleOfTheIliacBay] Live TTS conversation cleanup deferred: " + error.GetType().Name); }
    }

    private void SweepOwnedResponseCleanup()
    {
        if (ownedResponses.Count == 0) return;
        var snapshot = new List<OwnedStreamResponse>(ownedResponses.Values);
        for (int index = 0; index < snapshot.Count; index++)
        {
            OwnedStreamResponse response = snapshot[index];
            if (response != null && response.CleanupRequested && response.TerminalObserved)
                CleanupOwnedResponse(response);
        }
    }

    private static void TryDeleteExactFile(string path)
    { if (File.Exists(path)) File.Delete(path); }

    private bool IsBoundToCurrentDirectory()
    {
        if (string.IsNullOrEmpty(responseRoot) || string.IsNullOrEmpty(streamDirectory) ||
            !Path.IsPathRooted(responseRoot) || !Path.IsPathRooted(streamDirectory)) return false;
        return IsDirectChild(responseRoot, streamDirectory);
    }

    private static bool IsDirectChild(string root, string candidate)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(candidate)) return false;
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullCandidate = Path.GetFullPath(candidate);
        return string.Equals(Path.GetDirectoryName(fullCandidate), fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void EnqueueBlock(PcmBlock block)
    {
        lock (audioGate)
        {
            blocks.Enqueue(block);
            bufferedFrames += block.Samples.Length;
            if (outputRanges.Count > 4096) throw new InvalidDataException("output-range-limit");
        }
    }

    private static string ReadSharedTextBounded(string path, int minimumBytes, int maximumBytes)
    {
        byte[] bytes = ReadSharedBytesBounded(path, minimumBytes, maximumBytes);
        string text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '\ufeff' ? text.Substring(1) : text;
    }

    private static byte[] ReadSharedBytesBounded(string path, int minimumBytes, int maximumBytes)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
        {
            long length = stream.Length;
            if (length < minimumBytes || length > maximumBytes)
                throw new InvalidDataException("shared-file-size");
            byte[] bytes = new byte[(int)length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0) throw new EndOfStreamException("shared-file-truncated");
                offset += read;
            }
            if (stream.ReadByte() >= 0) throw new InvalidDataException("shared-file-grew-during-read");
            return bytes;
        }
    }

    private long BufferedFrameCount()
    { lock (audioGate) return bufferedFrames; }

    private int SumSentenceCharacters()
    {
        int total = 0;
        foreach (string value in sentenceText.Values) total += value == null ? 0 : value.Length;
        return total;
    }

    private int SumQueuedSentenceCharacters()
    {
        int total = 0;
        foreach (string value in queuedSentenceText.Values) total += value == null ? 0 : value.Length;
        return total;
    }

    private void ReleaseConsumedSentenceText()
    {
        if (queuedSentenceText.Count == 0) return;
        var released = new List<int>();
        foreach (var pair in queuedSentenceText)
            if (pair.Key <= consumedSentenceSequence) released.Add(pair.Key);
        for (int index = 0; index < released.Count; index++) queuedSentenceText.Remove(released[index]);
    }

    private void CloseSentence(int sequence, int finalChunkSequence)
    {
        lock (audioGate)
        {
            sentenceLastChunk[sequence] = finalChunkSequence;
            long outputEnd;
            if (renderedChunkOutputEnds.TryGetValue(finalChunkSequence, out outputEnd))
                sentenceOutputEnds[sequence] = outputEnd;
        }
    }

    private bool IsSentenceClosed(int sequence)
    {
        return (terminalSeen && sequence == latestObservedSentenceSequence) ||
            (sequence < latestObservedSentenceSequence);
    }

    private static string Sha256(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create())
        {
            byte[] digest = hash.ComputeHash(bytes);
            var result = new StringBuilder(digest.Length * 2);
            for (int index = 0; index < digest.Length; index++) result.Append(digest[index].ToString("x2"));
            return result.ToString();
        }
    }

    private static string Quote(string value)
    { return "\"" + LocalDialogueCompletion.EscapeJson(value ?? string.Empty) + "\""; }

    private void ResetAudioSource()
    {
        if (bridge != null && !string.IsNullOrEmpty(streamId))
            bridge.EndStreamingMixerObservation(streamId, conversationToken, personId);
        if (source != null)
        {
            if (source.isPlaying) source.Stop();
            if (source.clip == streamClip) source.clip = null;
        }
        if (streamClip != null) Destroy(streamClip);
        streamClip = null;
    }

    private void OnDestroy()
    {
        if (bridge != null) bridge.StreamingCancellationRequested -= OnBridgeCancellation;
        if (state != PlaybackState.Idle && state != PlaybackState.Completed && state != PlaybackState.Cancelled && state != PlaybackState.Failed)
            CancelFromOwner("adapter-destroyed");
        ResetAudioSource();
        CleanupExactResponseFiles();
    }
}
