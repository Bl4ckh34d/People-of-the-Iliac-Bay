using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public enum SpeechPresentationMode
    {
        Thinking,
        InitialReveal,
        ReplayHighlight,
        FullText
    }

    /// <summary>Pure presentation state for immutable NPC transcript text.</summary>
    public sealed class SpeechPresentationState
    {
        private readonly string requestId;
        private readonly string conversationToken;
        private readonly string personId;
        private readonly string canonicalText;
        private readonly List<TextWeightedWordSpan> words = new List<TextWeightedWordSpan>();
        private int playbackIndex;
        private long totalSamples;
        private long lastSample;
        private int visibleEnd;

        public SpeechPresentationMode Mode { get; private set; }
        public string CanonicalText { get { return canonicalText; } }
        public string VisibleText { get { return canonicalText.Substring(0, visibleEnd); } }
        public int VisibleEndUtf16 { get { return visibleEnd; } }
        public int CurrentWordStartUtf16 { get; private set; }
        public int CurrentWordEndUtf16 { get; private set; }
        public int PlaybackIndex { get { return playbackIndex; } }

        public SpeechPresentationState(string requestId, string conversationToken, string personId, string exactText,
            bool existingTranscript = false)
        {
            this.requestId = requestId ?? string.Empty;
            this.conversationToken = conversationToken ?? string.Empty;
            this.personId = personId ?? string.Empty;
            canonicalText = exactText ?? string.Empty;
            Mode = SpeechPresentationMode.Thinking;
            CurrentWordStartUtf16 = -1;
            CurrentWordEndUtf16 = -1;
            if (existingTranscript) ResolveWithoutAudio();
        }

        public bool TryStartInitialPlayback(string boundRequestId, string boundConversationToken,
            string boundPersonId, string exactText, int nextPlaybackIndex, int sourceSampleRate,
            long sourceTotalSamples, IList<TextWeightedWordSpan> timedWords, bool validatedAlignment = false)
        {
            if (Mode != SpeechPresentationMode.Thinking || !Matches(boundRequestId, boundConversationToken,
                boundPersonId, exactText) || nextPlaybackIndex <= 0 || sourceSampleRate <= 0 ||
                sourceTotalSamples <= 0 || !TryCopyValidatedWords(exactText, sourceTotalSamples, timedWords,
                    validatedAlignment, out List<TextWeightedWordSpan> copied)) return false;

            words.Clear();
            words.AddRange(copied);
            playbackIndex = nextPlaybackIndex;
            totalSamples = sourceTotalSamples;
            lastSample = 0;
            visibleEnd = 0;
            Mode = SpeechPresentationMode.InitialReveal;
            CurrentWordStartUtf16 = -1;
            CurrentWordEndUtf16 = -1;
            return true;
        }

        public bool TryStartReplay(string boundRequestId, string boundConversationToken, string boundPersonId,
            string exactText, int nextPlaybackIndex, int sourceSampleRate, long sourceTotalSamples,
            IList<TextWeightedWordSpan> timedWords, bool validatedAlignment = false)
        {
            if ((Mode != SpeechPresentationMode.FullText && Mode != SpeechPresentationMode.ReplayHighlight) ||
                !Matches(boundRequestId, boundConversationToken, boundPersonId, exactText) ||
                nextPlaybackIndex <= playbackIndex || sourceSampleRate <= 0 || sourceTotalSamples <= 0 ||
                !TryCopyValidatedWords(exactText, sourceTotalSamples, timedWords, validatedAlignment,
                    out List<TextWeightedWordSpan> copied)) return false;

            words.Clear();
            words.AddRange(copied);
            playbackIndex = nextPlaybackIndex;
            totalSamples = sourceTotalSamples;
            lastSample = 0;
            visibleEnd = canonicalText.Length;
            Mode = SpeechPresentationMode.ReplayHighlight;
            CurrentWordStartUtf16 = -1;
            CurrentWordEndUtf16 = -1;
            return true;
        }

        public void AdvancePlayback(int currentPlaybackIndex, long sampleFrame)
        {
            if ((Mode != SpeechPresentationMode.InitialReveal && Mode != SpeechPresentationMode.ReplayHighlight) ||
                currentPlaybackIndex != playbackIndex || words.Count == 0) return;
            if (sampleFrame < lastSample) sampleFrame = lastSample;
            if (sampleFrame > totalSamples) sampleFrame = totalSamples;
            lastSample = sampleFrame;
            CurrentWordStartUtf16 = -1;
            CurrentWordEndUtf16 = -1;
            for (int index = 0; index < words.Count; index++)
            {
                TextWeightedWordSpan word = words[index];
                if (sampleFrame < word.StartSample) break;
                if (Mode == SpeechPresentationMode.InitialReveal && sampleFrame >= word.EndSample)
                    visibleEnd = Math.Max(visibleEnd, word.TextStartUtf16 + word.TextLengthUtf16);
                if (sampleFrame < word.EndSample)
                {
                    CurrentWordStartUtf16 = word.TextStartUtf16;
                    CurrentWordEndUtf16 = word.TextStartUtf16 + word.TextLengthUtf16;
                    if (Mode == SpeechPresentationMode.InitialReveal)
                        visibleEnd = Math.Max(visibleEnd, CurrentWordEndUtf16);
                    break;
                }
            }
        }

        public void CompletePlayback(int currentPlaybackIndex)
        {
            if (currentPlaybackIndex != playbackIndex ||
                (Mode != SpeechPresentationMode.InitialReveal && Mode != SpeechPresentationMode.ReplayHighlight)) return;
            visibleEnd = canonicalText.Length;
            CurrentWordStartUtf16 = -1;
            CurrentWordEndUtf16 = -1;
            Mode = SpeechPresentationMode.FullText;
            words.Clear();
        }

        /// <summary>Resolves no-TTS, pre-start failure, cancellation, or unsupported timing to exact text.</summary>
        public void ResolveWithoutAudio()
        {
            visibleEnd = canonicalText.Length;
            CurrentWordStartUtf16 = -1;
            CurrentWordEndUtf16 = -1;
            Mode = SpeechPresentationMode.FullText;
            words.Clear();
        }

        private bool Matches(string boundRequestId, string boundConversationToken, string boundPersonId, string exactText)
        {
            return string.Equals(requestId, boundRequestId, StringComparison.Ordinal) &&
                string.Equals(conversationToken, boundConversationToken, StringComparison.Ordinal) &&
                string.Equals(personId, boundPersonId, StringComparison.Ordinal) &&
                string.Equals(canonicalText, exactText, StringComparison.Ordinal);
        }

        private bool TryCopyValidatedWords(string text, long expectedTotalSamples,
            IList<TextWeightedWordSpan> timedWords, bool validatedAlignment, out List<TextWeightedWordSpan> copied)
        {
            copied = null;
            if (string.IsNullOrEmpty(text) || text.Length > 16384 || timedWords == null || timedWords.Count == 0)
                return false;
            if (!validatedAlignment && !TextWeightedWordProgress.TryCreate(text, expectedTotalSamples,
                out _)) return false;

            var result = new List<TextWeightedWordSpan>(timedWords.Count);
            int previousTextEnd = 0;
            long previousSampleEnd = 0;
            for (int index = 0; index < timedWords.Count; index++)
            {
                TextWeightedWordSpan word = timedWords[index];
                int textEnd = word.TextStartUtf16 + word.TextLengthUtf16;
                if (word.TextStartUtf16 < previousTextEnd || word.TextLengthUtf16 <= 0 ||
                    textEnd > text.Length || word.StartSample < previousSampleEnd ||
                    word.EndSample <= word.StartSample || word.EndSample > expectedTotalSamples ||
                    !IsSafeTextBoundary(text, word.TextStartUtf16) || !IsSafeTextBoundary(text, textEnd)) return false;
                result.Add(word);
                previousTextEnd = textEnd;
                previousSampleEnd = word.EndSample;
            }
            copied = result;
            return true;
        }

        private static bool IsSafeTextBoundary(string text, int index)
        {
            if (index < 0 || index > text.Length) return false;
            if (index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) &&
                char.IsLowSurrogate(text[index])) return false;
            if (index < text.Length)
            {
                UnicodeCategory nextCategory = CharUnicodeInfo.GetUnicodeCategory(text, index);
                if (nextCategory == UnicodeCategory.NonSpacingMark || nextCategory == UnicodeCategory.SpacingCombiningMark ||
                    nextCategory == UnicodeCategory.EnclosingMark) return false;
            }
            return true;
        }
    }
}
