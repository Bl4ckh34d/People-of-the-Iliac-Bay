using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.DFU
{
    [Serializable]
    public sealed class PortraitVisemeSidecarDocument
    {
        public int schemaVersion;
        public string status;
        public string timelineKind;
        public string requestId;
        public string text;
        public int audioSampleRate;
        public int audioFrames;
        public PortraitVisemeSidecarCue[] cues;
        public PortraitVisemeSidecarProvenance provenance;
    }

    [Serializable]
    public sealed class PortraitVisemeSidecarCue
    {
        public int startSample;
        public int endSample;
        public string shape;
    }

    [Serializable]
    public sealed class PortraitVisemeSidecarProvenance
    {
        public string audioSha256;
        public string dialogHintSha256;
        public string recognitionKind;
        public bool phonemeAlignment;
    }

    /// <summary>Bridge-owned identity and WAV facts, captured before background sidecar validation.</summary>
    public sealed class PortraitVisemeAudioEvidence
    {
        public string RequestId;
        public string PersonId;
        public string ConversationToken;
        public string Text;
        public int SampleRate;
        public int AudioFrames;
        public int Channels;
        public string AudioSha256;
        public string WavePath;
    }

    public static class PortraitVisemeSidecarValidator
    {
        private const int MaximumCueCount = 100000;
        public const string ExpectedTimelineKind = "recognizer-viseme-estimate";

        public static bool TryValidate(PortraitVisemeSidecarDocument document,
            PortraitVisemeAudioEvidence audio, string expectedPersonId, string expectedConversationToken,
            out PortraitVisemeTimeline timeline, out string error)
        {
            timeline = null;
            error = null;
            if (audio == null || string.IsNullOrWhiteSpace(audio.RequestId) ||
                string.IsNullOrWhiteSpace(audio.PersonId) || string.IsNullOrWhiteSpace(audio.ConversationToken) ||
                string.IsNullOrEmpty(audio.Text) || audio.SampleRate <= 0 || audio.AudioFrames <= 0 ||
                audio.Channels < 1 || audio.Channels > 2 || !IsSha256(audio.AudioSha256) ||
                string.IsNullOrWhiteSpace(expectedPersonId) || string.IsNullOrWhiteSpace(expectedConversationToken) ||
                !string.Equals(audio.PersonId, expectedPersonId, StringComparison.Ordinal) ||
                !string.Equals(audio.ConversationToken, expectedConversationToken, StringComparison.Ordinal))
                return Fail("invalid-bridge-audio-evidence", out error);

            if (document == null) return Fail("sidecar-json-deserialized-null", out error);
            if (document.schemaVersion != 1) return Fail("sidecar-schema-version-mismatch", out error);
            if (document.status != "ready") return Fail("sidecar-status-not-ready", out error);
            if (document.timelineKind != ExpectedTimelineKind) return Fail("sidecar-timeline-kind-mismatch", out error);
            if (!string.Equals(document.requestId, audio.RequestId, StringComparison.Ordinal))
                return Fail("sidecar-request-id-mismatch", out error);
            if (!string.Equals(document.text, audio.Text, StringComparison.Ordinal))
                return Fail("sidecar-text-does-not-match-bridge-text", out error);
            if (document.audioSampleRate != audio.SampleRate) return Fail("sidecar-sample-rate-mismatch", out error);
            if (document.audioFrames != audio.AudioFrames) return Fail("sidecar-frame-count-mismatch", out error);
            if (document.cues == null || document.cues.Length == 0 || document.cues.Length > MaximumCueCount)
                return Fail("sidecar-cue-count-out-of-bounds", out error);
            if (document.provenance == null) return Fail("sidecar-provenance-missing", out error);
            if (document.provenance.recognitionKind != "recognizer-estimate")
                return Fail("sidecar-recognition-kind-mismatch", out error);
            if (document.provenance.phonemeAlignment) return Fail("sidecar-claims-phoneme-alignment", out error);
            if (!FixedHashEquals(document.provenance.audioSha256, audio.AudioSha256))
                return Fail("sidecar-wav-sha256-mismatch", out error);
            if (!FixedHashEquals(document.provenance.dialogHintSha256, Sha256Utf8(audio.Text)))
                return Fail("sidecar-transcript-sha256-mismatch", out error);

            var cues = new List<PortraitVisemeSampleCue>(document.cues.Length);
            int previousEnd = 0;
            foreach (PortraitVisemeSidecarCue cue in document.cues)
            {
                if (cue == null || cue.startSample != previousEnd || cue.endSample <= cue.startSample ||
                    cue.endSample > audio.AudioFrames)
                    return Fail("sidecar-cues-not-contiguous-or-out-of-audio", out error);
                cues.Add(new PortraitVisemeSampleCue(cue.startSample, cue.endSample, cue.shape));
                previousEnd = cue.endSample;
            }
            if (previousEnd != audio.AudioFrames)
                return Fail("sidecar-cues-do-not-cover-audio", out error);

            return PortraitVisemeTimeline.TryCreateSamples(audio.SampleRate, audio.AudioFrames, cues,
                out timeline, out error);
        }

        public static string Sha256Utf8(string text)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                var result = new StringBuilder(bytes.Length * 2);
                foreach (byte value in bytes) result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;
            return true;
        }

        private static bool FixedHashEquals(string provided, string expected)
        {
            if (!IsSha256(provided) || !IsSha256(expected)) return false;
            int difference = 0;
            for (int index = 0; index < 64; index++)
                difference |= char.ToLowerInvariant(provided[index]) ^ char.ToLowerInvariant(expected[index]);
            return difference == 0;
        }

        private static bool Fail(string reason, out string error)
        {
            error = reason;
            return false;
        }
    }

    /// <summary>Two-state measured-RMS fallback; deliberately labeled as amplitude, never lip sync.</summary>
    public sealed class PortraitAmplitudeFallback
    {
        public const string ProvenanceLabel = "amplitude-approximation";
        private readonly double openThreshold;
        private readonly double closeThreshold;
        private PortraitMouthShape current = PortraitMouthShape.X;

        public PortraitAmplitudeFallback(double openThreshold, double closeThreshold)
        {
            if (double.IsNaN(openThreshold) || double.IsInfinity(openThreshold) || openThreshold <= 0 ||
                double.IsNaN(closeThreshold) || double.IsInfinity(closeThreshold) || closeThreshold < 0 ||
                closeThreshold >= openThreshold)
                throw new ArgumentOutOfRangeException("openThreshold");
            this.openThreshold = openThreshold;
            this.closeThreshold = closeThreshold;
        }

        public PortraitMouthShape ShapeForRms(double rms)
        {
            if (double.IsNaN(rms) || double.IsInfinity(rms) || rms < 0) return current;
            if (current == PortraitMouthShape.X && rms >= openThreshold) current = PortraitMouthShape.D;
            else if (current != PortraitMouthShape.X && rms <= closeThreshold) current = PortraitMouthShape.X;
            return current;
        }

        public void Reset()
        {
            current = PortraitMouthShape.X;
        }

        public static double CalculateRms(float[] samples)
        {
            if (samples == null || samples.Length == 0) return 0;
            double sumSquares = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                double sample = samples[index];
                if (double.IsNaN(sample) || double.IsInfinity(sample)) continue;
                sumSquares += sample * sample;
            }
            return Math.Sqrt(sumSquares / samples.Length);
        }
    }
}
