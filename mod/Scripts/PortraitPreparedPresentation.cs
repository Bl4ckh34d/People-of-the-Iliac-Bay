using System;
using System.IO;
using FullSerializer;

namespace PeopleOfTheIliacBay.DFU
{
    [Serializable]
    public sealed class WordAlignmentTimelineDocument
    {
        public int schemaVersion = 0;
        public string status = null;
        public string timelineKind = null;
        public string text = null;
        public int audioSampleRate = 0;
        public long audioFrames = -1;
        public WordAlignmentCoverage coverage = null;
        public WordAlignmentWord[] words = null;
        public WordAlignmentProvenance provenance = null;
        public WordAlignmentStreamBinding streamBinding = null;
    }

    [Serializable]
    public sealed class WordAlignmentCoverage
    {
        public int matchedTokens = -1;
        public int transcriptTokens = -1;
    }

    [Serializable]
    public sealed class WordAlignmentProvenance
    {
        public string requestId = null;
        public string audioSha256 = null;
        public string transcriptSha256 = null;
    }

    [Serializable]
    public sealed class WordAlignmentStreamBinding
    {
        public int schemaVersion = 0;
        public string streamId = null;
        public string conversationToken = null;
        public string personId = null;
        public string scope = null;
        public int sentenceSequence = -1;
        public string textSha256 = null;
        public string audioSha256 = null;
        public int sampleRate = 0;
        public long sampleFrames = -1;
    }

    [Serializable]
    public sealed class WordAlignmentWord
    {
        public string text = null;
        public string alignedText = null;
        public int textStartUtf16 = -1;
        public int textLengthUtf16 = -1;
        public long startSample = -1;
        public long endSample = -1;
    }

    /// <summary>Validated word spans. Private copied arrays keep cached presentation data immutable.</summary>
    public sealed class PortraitWordAlignmentTimeline
    {
        private readonly int[] textStarts;
        private readonly int[] textEnds;
        private readonly long[] sampleStarts;
        private readonly long[] sampleEnds;

        public readonly string TimelineKind;
        public readonly string Text;
        public int SampleRate { get; private set; }
        public int AudioFrames { get; private set; }

        internal PortraitWordAlignmentTimeline(string timelineKind, string text, int sampleRate,
            int audioFrames, WordAlignmentWord[] words)
        {
            TimelineKind = timelineKind;
            Text = text;
            SampleRate = sampleRate;
            AudioFrames = audioFrames;
            textStarts = new int[words.Length];
            textEnds = new int[words.Length];
            sampleStarts = new long[words.Length];
            sampleEnds = new long[words.Length];
            for (int index = 0; index < words.Length; index++)
            {
                WordAlignmentWord word = words[index];
                textStarts[index] = word.textStartUtf16;
                textEnds[index] = word.textStartUtf16 + word.textLengthUtf16;
                sampleStarts[index] = word.startSample;
                sampleEnds[index] = word.endSample;
            }
        }

        /// <summary>Returns complete text through the word active at sampleFrame.</summary>
        public int GetCumulativeTextEnd(long sampleFrame)
        {
            if (sampleFrame < 0) return 0;
            int textEnd = 0;
            for (int index = 0; index < sampleStarts.Length; index++)
            {
                if (sampleStarts[index] > sampleFrame) break;
                textEnd = Math.Max(textEnd, textEnds[index]);
            }
            return textEnd;
        }

        public int GetCurrentWordStart(long sampleFrame)
        {
            if (sampleFrame < 0) return -1;
            for (int index = 0; index < sampleStarts.Length; index++)
            {
                if (sampleStarts[index] <= sampleFrame && sampleFrame < sampleEnds[index])
                    return textStarts[index];
                if (sampleStarts[index] > sampleFrame) break;
            }
            return -1;
        }
    }

    /// <summary>Exact-audio-bound visemes prepared before playback, plus optional word alignment.</summary>
    public sealed class PortraitPreparedPresentation
    {
        public readonly string RequestId;
        public readonly string PersonId;
        public readonly string ConversationToken;
        public readonly string Text;
        public readonly int SampleRate;
        public readonly int AudioFrames;
        public readonly string AudioSha256;
        public readonly PortraitWordAlignmentTimeline Words;
        public readonly PortraitVisemeTimeline Visemes;

        internal PortraitPreparedPresentation(PortraitVisemeAudioEvidence evidence,
            PortraitWordAlignmentTimeline words, PortraitVisemeTimeline visemes)
        {
            RequestId = evidence.RequestId;
            PersonId = evidence.PersonId;
            ConversationToken = evidence.ConversationToken;
            Text = evidence.Text;
            SampleRate = evidence.SampleRate;
            AudioFrames = evidence.AudioFrames;
            AudioSha256 = evidence.AudioSha256;
            Words = words;
            Visemes = visemes;
        }

        public bool Matches(PortraitVisemeAudioEvidence evidence)
        {
            return evidence != null && Visemes != null &&
                string.Equals(RequestId, evidence.RequestId, StringComparison.Ordinal) &&
                string.Equals(PersonId, evidence.PersonId, StringComparison.Ordinal) &&
                string.Equals(ConversationToken, evidence.ConversationToken, StringComparison.Ordinal) &&
                string.Equals(Text, evidence.Text, StringComparison.Ordinal) &&
                SampleRate == evidence.SampleRate && AudioFrames == evidence.AudioFrames &&
                string.Equals(AudioSha256, evidence.AudioSha256, StringComparison.OrdinalIgnoreCase) &&
                (Words == null || Words.SampleRate == SampleRate && Words.AudioFrames == AudioFrames) &&
                Visemes.SampleRate == SampleRate && Visemes.AudioFrames == AudioFrames;
        }
    }

    public static class PortraitWordAlignmentTimelineValidator
    {
        public static bool TryValidate(WordAlignmentTimelineDocument value,
            PortraitVisemeAudioEvidence evidence, bool requireFinalStreamBinding,
            out PortraitWordAlignmentTimeline timeline, out string error)
        {
            timeline = null;
            error = null;
            if (evidence == null || string.IsNullOrEmpty(evidence.RequestId) ||
                string.IsNullOrEmpty(evidence.PersonId) || string.IsNullOrEmpty(evidence.ConversationToken) ||
                string.IsNullOrEmpty(evidence.Text) || evidence.SampleRate <= 0 || evidence.AudioFrames <= 0 ||
                evidence.AudioFrames > int.MaxValue || !IsSha256(evidence.AudioSha256))
                return Fail("binding-miss:invalid-bridge-audio-evidence", out error);
            if (value == null || value.schemaVersion != 1 || value.status != "ready" ||
                value.timelineKind != "forced-word-alignment" || value.text != evidence.Text)
                return Fail("binding-miss:timeline-schema-or-text", out error);
            if (value.audioSampleRate != evidence.SampleRate || value.audioFrames != evidence.AudioFrames)
                return Fail("rate:timeline-rate-or-frame-count", out error);
            if (value.coverage == null || value.coverage.matchedTokens <= 0 ||
                value.coverage.matchedTokens != value.coverage.transcriptTokens || value.words == null ||
                value.words.Length != value.coverage.matchedTokens)
                return Fail("binding-miss:timeline-token-coverage", out error);

            string textHash = PortraitVisemeSidecarValidator.Sha256Utf8(evidence.Text);
            if (value.provenance == null || value.provenance.requestId != evidence.RequestId ||
                !FixedHashEquals(value.provenance.audioSha256, evidence.AudioSha256) ||
                !FixedHashEquals(value.provenance.transcriptSha256, textHash))
                return Fail("hash:timeline-audio-or-text-hash", out error);

            if (requireFinalStreamBinding)
            {
                WordAlignmentStreamBinding binding = value.streamBinding;
                if (binding == null || binding.schemaVersion != 1 || binding.streamId != evidence.RequestId ||
                    binding.conversationToken != evidence.ConversationToken || binding.personId != evidence.PersonId ||
                    binding.scope != "final" || binding.sampleRate != evidence.SampleRate ||
                    binding.sampleFrames != evidence.AudioFrames ||
                    !FixedHashEquals(binding.textSha256, textHash) ||
                    !FixedHashEquals(binding.audioSha256, evidence.AudioSha256))
                    return Fail("binding-miss:stream-final-timeline-binding", out error);
            }

            long previousSampleEnd = 0;
            int previousTextEnd = 0;
            for (int index = 0; index < value.words.Length; index++)
            {
                WordAlignmentWord word = value.words[index];
                if (word == null || string.IsNullOrEmpty(word.text) || word.textStartUtf16 < previousTextEnd ||
                    word.textLengthUtf16 <= 0 || (long)word.textStartUtf16 + word.textLengthUtf16 > evidence.Text.Length ||
                    word.startSample < previousSampleEnd || word.endSample <= word.startSample ||
                    word.endSample > value.audioFrames ||
                    !string.Equals(evidence.Text.Substring(word.textStartUtf16, word.textLengthUtf16),
                        word.text, StringComparison.Ordinal))
                    return Fail("binding-miss:timeline-word-spans", out error);
                previousSampleEnd = word.endSample;
                previousTextEnd = word.textStartUtf16 + word.textLengthUtf16;
            }

            timeline = new PortraitWordAlignmentTimeline(value.timelineKind, evidence.Text,
                evidence.SampleRate, (int)evidence.AudioFrames, value.words);
            return true;
        }

        public static bool TryDeserialize(string json, out WordAlignmentTimelineDocument document, out string error)
        {
            document = null;
            error = null;
            try
            {
                fsData data = fsJsonParser.Parse(json);
                if (data == null || !data.IsDictionary) return Fail("sidecar-json-root-not-object", out error);
                var serializer = new fsSerializer();
                fsResult result = serializer.TryDeserialize(data, ref document);
                return !result.Failed && document != null || Fail("sidecar-json-deserialize-failed", out error);
            }
            catch (Exception exception)
            {
                return Fail("sidecar-json-parse-failed:" + exception.GetType().Name, out error);
            }
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char c = value[index];
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F'))
                    return false;
            }
            return true;
        }

        private static bool FixedHashEquals(string supplied, string expected)
        {
            if (!IsSha256(supplied) || !IsSha256(expected)) return false;
            int difference = 0;
            for (int index = 0; index < 64; index++)
                difference |= char.ToLowerInvariant(supplied[index]) ^ char.ToLowerInvariant(expected[index]);
            return difference == 0;
        }

        private static bool Fail(string reason, out string error)
        {
            error = reason;
            return false;
        }
    }

    public static class PortraitPreparedPresentationLoader
    {
        private const int MaximumAlignmentBytes = 256 * 1024;
        private const int MaximumVisemeBytes = 2 * 1024 * 1024;

        public static bool TryLoad(string alignmentPath, string visemePath,
            PortraitVisemeAudioEvidence evidence, out PortraitPreparedPresentation presentation, out string error)
        {
            presentation = null;
            error = null;
            if (evidence == null) return Fail("binding-miss:invalid-bridge-audio-evidence", out error);

            string visemeJson;
            if (!TryReadBounded(visemePath, MaximumVisemeBytes, out visemeJson, out error)) return false;
            PortraitVisemeSidecarDocument visemeDocument;
            if (!TryDeserializeVisemes(visemeJson, out visemeDocument, out error)) return false;
            PortraitVisemeTimeline visemes;
            if (!PortraitVisemeSidecarValidator.TryValidate(visemeDocument, evidence,
                evidence.PersonId, evidence.ConversationToken, out visemes, out error)) return false;

            PortraitWordAlignmentTimeline words = null;
            if (!string.IsNullOrWhiteSpace(alignmentPath) && File.Exists(alignmentPath))
            {
                string alignmentJson;
                string ignoredError;
                WordAlignmentTimelineDocument wordDocument;
                PortraitWordAlignmentTimeline optionalWords;
                if (TryReadBounded(alignmentPath, MaximumAlignmentBytes, out alignmentJson, out ignoredError) &&
                    PortraitWordAlignmentTimelineValidator.TryDeserialize(alignmentJson, out wordDocument,
                        out ignoredError) &&
                    PortraitWordAlignmentTimelineValidator.TryValidate(wordDocument, evidence, false,
                        out optionalWords, out ignoredError))
                    words = optionalWords;
            }

            var result = new PortraitPreparedPresentation(evidence, words, visemes);
            if (!result.Matches(evidence)) return Fail("binding-miss:prepared-presentation-mismatch", out error);
            presentation = result;
            return true;
        }

        private static bool TryReadBounded(string path, int maximumBytes, out string json, out string error)
        {
            json = null;
            error = null;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length < 2 || stream.Length > maximumBytes)
                        return Fail("sidecar-size-out-of-bounds", out error);
                    byte[] bytes = new byte[(int)stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0) return Fail("sidecar-truncated", out error);
                        offset += read;
                    }
                    if (stream.ReadByte() != -1) return Fail("sidecar-size-out-of-bounds", out error);
                    using (var reader = new StreamReader(new MemoryStream(bytes), System.Text.Encoding.UTF8, true))
                        json = reader.ReadToEnd();
                }
                return true;
            }
            catch (FileNotFoundException) { return Fail("sidecar-not-ready", out error); }
            catch (DirectoryNotFoundException) { return Fail("sidecar-not-ready", out error); }
            catch (Exception exception) { return Fail("sidecar-read-failed:" + exception.GetType().Name, out error); }
        }

        private static bool TryDeserializeVisemes(string json, out PortraitVisemeSidecarDocument document,
            out string error)
        {
            document = null;
            error = null;
            try
            {
                fsData data = fsJsonParser.Parse(json);
                if (data == null || !data.IsDictionary) return Fail("sidecar-json-root-not-object", out error);
                var serializer = new fsSerializer();
                fsResult result = serializer.TryDeserialize(data, ref document);
                return !result.Failed && document != null || Fail("sidecar-json-deserialize-failed", out error);
            }
            catch (Exception exception)
            { return Fail("sidecar-json-parse-failed:" + exception.GetType().Name, out error); }
        }

        private static bool Fail(string reason, out string error)
        {
            error = reason;
            return false;
        }
    }
}
