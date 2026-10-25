using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Approximate text-weighted word interval on a known audio sample clock.</summary>
    public struct TextWeightedWordSpan : IEquatable<TextWeightedWordSpan>
    {
        public int TextStartUtf16 { get; private set; }
        public int TextLengthUtf16 { get; private set; }
        public long StartSample { get; private set; }
        public long EndSample { get; private set; }

        public TextWeightedWordSpan(int textStartUtf16, int textLengthUtf16, long startSample, long endSample)
        {
            TextStartUtf16 = textStartUtf16;
            TextLengthUtf16 = textLengthUtf16;
            StartSample = startSample;
            EndSample = endSample;
        }

        public bool Equals(TextWeightedWordSpan other)
        {
            return TextStartUtf16 == other.TextStartUtf16 && TextLengthUtf16 == other.TextLengthUtf16 &&
                StartSample == other.StartSample && EndSample == other.EndSample;
        }

        public override bool Equals(object obj)
        { return obj is TextWeightedWordSpan && Equals((TextWeightedWordSpan)obj); }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = TextStartUtf16;
                hash = hash * 397 ^ TextLengthUtf16;
                hash = hash * 397 ^ StartSample.GetHashCode();
                return hash * 397 ^ EndSample.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Splits supported ASCII transcript text into deterministic display estimates.
    /// These intervals use known WAV duration and text weights; they are not measured word alignment.
    /// </summary>
    public sealed class TextWeightedWordProgress
    {
        private const int MaximumTextLength = 4096;
        private const int MaximumWords = 512;
        private readonly ReadOnlyCollection<TextWeightedWordSpan> words;

        public long TotalSamples { get; private set; }
        public IList<TextWeightedWordSpan> Words { get { return words; } }

        private TextWeightedWordProgress(long totalSamples, List<TextWeightedWordSpan> spans)
        {
            TotalSamples = totalSamples;
            words = spans.AsReadOnly();
        }

        public static bool TryCreate(string text, long totalSamples, out TextWeightedWordProgress progress)
        {
            progress = null;
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextLength || totalSamples <= 0)
                return false;
            for (int index = 0; index < text.Length; index++)
            {
                char value = text[index];
                if (!IsSupportedCharacter(value)) return false;
            }

            var tokens = new List<Token>();
            int position = 0;
            long totalWeight = 0;
            while (position < text.Length)
            {
                while (position < text.Length && IsAsciiWhitespace(text[position])) position++;
                if (position >= text.Length) break;
                int start = position;
                int alphanumericCount = 0;
                while (position < text.Length && !IsAsciiWhitespace(text[position]))
                {
                    if (IsAsciiAlphaNumeric(text[position])) alphanumericCount++;
                    position++;
                }
                int length = position - start;
                if (alphanumericCount == 0) return false;

                int punctuationPause = TrailingPauseWeight(text, start, length);
                int weight = alphanumericCount + punctuationPause;
                tokens.Add(new Token(start, length, weight));
                totalWeight += weight;
                if (tokens.Count > MaximumWords) return false;
            }

            if (tokens.Count == 0 || totalSamples < tokens.Count || totalWeight <= 0) return false;

            var spans = new List<TextWeightedWordSpan>(tokens.Count);
            long previousEnd = 0;
            long cumulativeWeight = 0;
            for (int index = 0; index < tokens.Count; index++)
            {
                cumulativeWeight += tokens[index].Weight;
                long end = index == tokens.Count - 1
                    ? totalSamples
                    : WeightedBoundary(totalSamples, cumulativeWeight, totalWeight);
                long maximumEnd = totalSamples - (tokens.Count - index - 1);
                if (end > maximumEnd) end = maximumEnd;
                if (end <= previousEnd) end = previousEnd + 1;
                if (end > maximumEnd) return false;
                spans.Add(new TextWeightedWordSpan(tokens[index].Start, tokens[index].Length, previousEnd, end));
                previousEnd = end;
            }

            progress = new TextWeightedWordProgress(totalSamples, spans);
            return true;
        }

        /// <summary>Returns end of last estimated word whose estimated onset has passed.</summary>
        public int GetCumulativeTextEnd(long sampleFrame)
        {
            if (sampleFrame <= 0 || words.Count == 0) return 0;
            int textEnd = 0;
            for (int index = 0; index < words.Count; index++)
            {
                TextWeightedWordSpan span = words[index];
                if (span.StartSample > sampleFrame) break;
                textEnd = span.TextStartUtf16 + span.TextLengthUtf16;
            }
            return textEnd;
        }

        private static long WeightedBoundary(long totalSamples, long cumulativeWeight, long totalWeight)
        {
            long quotient = totalSamples / totalWeight;
            long remainder = totalSamples % totalWeight;
            return quotient * cumulativeWeight + (remainder * cumulativeWeight) / totalWeight;
        }

        private static int TrailingPauseWeight(string text, int start, int length)
        {
            int end = start + length - 1;
            while (end >= start && IsClosingPunctuation(text[end])) end--;
            if (end < start) return 0;
            char value = text[end];
            if (value == '.' || value == '?' || value == '!') return 2;
            if (value == ',' || value == ';' || value == ':') return 1;
            if (value == '\u2013' || value == '\u2014' || value == '\u2026') return 1;
            return 0;
        }

        private static bool IsSupportedCharacter(char value)
        {
            if (value <= 0x7e) return value >= 0x20 || IsAsciiWhitespace(value);
            return value == '\u2018' || value == '\u2019' || value == '\u201c' || value == '\u201d' ||
                value == '\u2013' || value == '\u2014' || value == '\u2026';
        }

        private static bool IsAsciiWhitespace(char value)
        { return value == ' ' || value == '\t' || value == '\r' || value == '\n'; }

        private static bool IsAsciiAlphaNumeric(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z') ||
                (value >= '0' && value <= '9');
        }

        private static bool IsClosingPunctuation(char value)
        { return value == '\'' || value == '"' || value == ')' || value == ']' || value == '}' ||
            value == '\u2019' || value == '\u201d'; }

        private struct Token
        {
            public readonly int Start;
            public readonly int Length;
            public readonly int Weight;

            public Token(int start, int length, int weight)
            { Start = start; Length = length; Weight = weight; }
        }
    }
}
