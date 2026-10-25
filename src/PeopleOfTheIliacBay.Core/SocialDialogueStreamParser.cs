using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class SocialDialogueStreamResult
    {
        public readonly bool Complete;
        /// <summary>Final normalized reply only on complete success; otherwise best safe parser snapshot.</summary>
        public readonly string Reply;
        /// <summary>Complete validated sentence segments delivered to caller; does not prove audio playback.</summary>
        public readonly string EmittedText;
        public readonly IReadOnlyList<string> EmittedSegments;
        /// <summary>Null unless full structured result passes the existing social-result validator.</summary>
        public readonly int? MoodDelta;
        public readonly string FailureKind;

        public string ToSocialDialogueJson()
        {
            if (!Complete || !MoodDelta.HasValue) return null;
            return "{\"reply\":\"" + LocalDialogueCompletion.EscapeJson(Reply) + "\",\"moodDelta\":" +
                MoodDelta.Value.ToString(CultureInfo.InvariantCulture) + "}";
        }

        public static SocialDialogueStreamResult Failed(string failureKind)
        {
            return new SocialDialogueStreamResult(false, string.Empty, string.Empty,
                Array.Empty<string>(), null, string.IsNullOrEmpty(failureKind) ? "transport-error" : failureKind);
        }

        public static SocialDialogueStreamResult Failed(string failureKind, IReadOnlyList<string> emittedSegments)
        {
            IReadOnlyList<string> segments = emittedSegments ?? Array.Empty<string>();
            string emittedText = string.Join(" ", Copy(segments));
            return new SocialDialogueStreamResult(false, emittedText, emittedText, Copy(segments), null,
                string.IsNullOrEmpty(failureKind) ? "transport-error" : failureKind);
        }

        private static string[] Copy(IReadOnlyList<string> values)
        {
            var copy = new string[values.Count];
            for (int index = 0; index < values.Count; index++) copy[index] = values[index];
            return copy;
        }

        internal SocialDialogueStreamResult(bool complete, string reply, string emittedText,
            IReadOnlyList<string> emittedSegments, int? moodDelta, string failureKind)
        {
            Complete = complete;
            Reply = reply ?? string.Empty;
            EmittedText = emittedText ?? string.Empty;
            EmittedSegments = emittedSegments ?? Array.Empty<string>();
            MoodDelta = moodDelta;
            FailureKind = failureKind ?? string.Empty;
        }
    }

    /// <summary>Parses bounded llama.cpp SSE frames and a strict social reply JSON stream.</summary>
    public sealed class SocialDialogueStreamParser
    {
        public const int DefaultMaximumFrameCharacters = 16384;
        public const int DefaultMaximumResponseCharacters = 8192;
        private const string ReplyPrefix = "{\"reply\":\"";
        private static readonly DialogueRequest ValidationRequest =
            new DialogueRequest(string.Empty, "Fallback.", "NPC", string.Empty, string.Empty,
                string.Empty, 0, true);

        private readonly int maximumFrameCharacters;
        private readonly int maximumResponseCharacters;
        private readonly StringBuilder modelJson = new StringBuilder();
        private readonly StringBuilder reply = new StringBuilder();
        private readonly List<string> emittedSegments = new List<string>();
        private int prefixPosition;
        private int emittedCanonicalLength;
        private bool inReply = true;
        private bool escapePending;
        private int unicodeDigits;
        private int unicodeValue;
        private bool stopSeen;
        private bool doneSeen;
        private int streamCharacters;
        private string failureKind;

        public event Action<string> SentenceReady;

        public SocialDialogueStreamParser(int maximumFrameCharacters = DefaultMaximumFrameCharacters,
            int maximumResponseCharacters = DefaultMaximumResponseCharacters)
        {
            if (maximumFrameCharacters < 32 || maximumFrameCharacters > 65536)
                throw new ArgumentOutOfRangeException(nameof(maximumFrameCharacters));
            if (maximumResponseCharacters < 32 || maximumResponseCharacters > DefaultMaximumResponseCharacters)
                throw new ArgumentOutOfRangeException(nameof(maximumResponseCharacters));
            this.maximumFrameCharacters = maximumFrameCharacters;
            this.maximumResponseCharacters = maximumResponseCharacters;
        }

        /// <summary>Accepts one complete SSE line. llama.cpp emits one data line per event.</summary>
        public bool FeedSseLine(string line)
        {
            if (failureKind != null || doneSeen) return false;
            if (line == null) return Fail("protocol");
            if (line.Length > maximumFrameCharacters) return Fail("size-limit");
            if (line.Length == 0 || line[0] == ':') return true;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) return true;

            string data = line.Substring(5);
            if (data.StartsWith(" ", StringComparison.Ordinal)) data = data.Substring(1);
            streamCharacters += data.Length;
            if (streamCharacters > maximumResponseCharacters * 8) return Fail("size-limit");
            if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
            {
                doneSeen = true;
                return true;
            }

            string content;
            if (!TryExtractContent(data, out content)) return Fail("protocol");
            if (!AppendContent(content)) return false;
            if (HasBooleanField(data, "stop", true)) stopSeen = true;
            return true;
        }

        /// <summary>Returns complete result or exact already-emitted text with a failure reason.</summary>
        public SocialDialogueStreamResult Finish(string failure = null)
        {
            if (failureKind != null) return Snapshot(false, failureKind, null);
            if (failure != null) return Snapshot(false, failure, null);
            if (!doneSeen && !stopSeen) return Snapshot(false, "stream-incomplete", null);

            SocialDialogueResult result;
            if (inReply || escapePending || unicodeDigits != 0 || !SocialDialogueResult.TryParse(modelJson.ToString(), out result))
                return Snapshot(false, "invalid-final-result", null);
            if (!string.Equals(result.Reply, CurrentCanonicalReply(), StringComparison.Ordinal))
                return Snapshot(false, "invalid-final-result", null);
            return Snapshot(true, string.Empty, result.MoodDelta, result.Reply);
        }

        private bool AppendContent(string content)
        {
            if (modelJson.Length + content.Length > maximumResponseCharacters) return Fail("size-limit");
            for (int index = 0; index < content.Length; index++)
            {
                char value = content[index];
                modelJson.Append(value);
                if (inReply)
                {
                    if (prefixPosition < ReplyPrefix.Length)
                    {
                        // Whitespace is legal between JSON tokens, never within the reply key
                        // or after the opening quote of the reply value.
                        if ((prefixPosition == 0 || prefixPosition == 1 || prefixPosition == 8 || prefixPosition == 9) &&
                            (value == ' ' || value == '\t' || value == '\r' || value == '\n')) continue;
                        if (value != ReplyPrefix[prefixPosition++]) return Fail("invalid-structure");
                        continue;
                    }
                    if (!AppendReplyJsonCharacter(value)) return false;
                }
            }
            return failureKind == null;
        }

        private bool AppendReplyJsonCharacter(char value)
        {
            if (unicodeDigits > 0)
            {
                int hex = Hex(value);
                if (hex < 0) return Fail("invalid-structure");
                unicodeValue = unicodeValue * 16 + hex;
                unicodeDigits--;
                if (unicodeDigits == 0)
                {
                    if (char.IsSurrogate((char)unicodeValue)) return Fail("invalid-reply-character");
                    return AppendDecoded((char)unicodeValue);
                }
                return true;
            }
            if (escapePending)
            {
                escapePending = false;
                switch (value)
                {
                    case '"': return AppendDecoded('"');
                    case '\\': return AppendDecoded('\\');
                    case '/': return AppendDecoded('/');
                    case 'b': return AppendDecoded('\b');
                    case 'f': return AppendDecoded('\f');
                    case 'n': return AppendDecoded('\n');
                    case 'r': return AppendDecoded('\r');
                    case 't': return AppendDecoded('\t');
                    case 'u': unicodeDigits = 4; unicodeValue = 0; return true;
                    default: return Fail("invalid-structure");
                }
            }
            if (value == '"')
            {
                inReply = false;
                return EmitThrough(reply.Length);
            }
            if (value == '\\') { escapePending = true; return true; }
            if (value < 0x20 || char.IsSurrogate(value)) return Fail("invalid-reply-character");
            return AppendDecoded(value);
        }

        private bool AppendDecoded(char value)
        {
            reply.Append(value);
            if (IsWhitespace(value) && reply.Length > 1)
            {
                int end = reply.Length - 1;
                while (end > 0 && IsClosingQuote(reply[end - 1])) end--;
                if (end > 0 && IsSentenceTerminator(reply[end - 1]))
                    return EmitThrough(reply.Length - 1);
            }
            if (reply.Length > maximumResponseCharacters) return Fail("size-limit");
            return true;
        }

        private bool EmitThrough(int rawLength)
        {
            if (rawLength <= 0) return true;
            string candidate = reply.ToString(0, rawLength);
            string canonical;
            if (!LocalDialogue.TrySelectAnswer(ValidationRequest, candidate, out canonical))
                return Fail("invalid-reply");
            if (canonical.Length <= emittedCanonicalLength) return true;
            if (canonical.Length < emittedCanonicalLength ||
                !canonical.StartsWith(CurrentCanonicalReply(), StringComparison.Ordinal))
                return Fail("invalid-reply");
            string segment = canonical.Substring(emittedCanonicalLength).Trim();
            emittedCanonicalLength = canonical.Length;
            if (segment.Length == 0) return true;
            emittedSegments.Add(segment);
            Action<string> callback = SentenceReady;
            if (callback != null)
            {
                try { callback(segment); }
                catch { return Fail("consumer-error"); }
            }
            return true;
        }

        private SocialDialogueStreamResult Snapshot(bool complete, string failure, int? moodDelta, string finalReply = null)
        {
            string emitted = string.Join(" ", emittedSegments.ToArray());
            return new SocialDialogueStreamResult(complete, finalReply ?? CurrentCanonicalReply(), emitted,
                emittedSegments.ToArray(), moodDelta, failure);
        }

        private string CurrentCanonicalReply()
        {
            if (emittedCanonicalLength == 0) return string.Empty;
            string canonical;
            if (!LocalDialogue.TrySelectAnswer(ValidationRequest, reply.ToString(), out canonical))
                return string.Join(" ", emittedSegments.ToArray());
            return canonical;
        }

        private bool Fail(string reason)
        {
            if (failureKind == null) failureKind = reason;
            return false;
        }

        private static bool HasBooleanField(string json, string key, bool value)
        {
            string token = "\"" + key + "\"";
            int position = json.IndexOf(token, StringComparison.Ordinal);
            if (position < 0) return false;
            position += token.Length;
            while (position < json.Length && char.IsWhiteSpace(json[position])) position++;
            if (position >= json.Length || json[position++] != ':') return false;
            while (position < json.Length && char.IsWhiteSpace(json[position])) position++;
            string expected = value ? "true" : "false";
            return position + expected.Length <= json.Length &&
                string.CompareOrdinal(json, position, expected, 0, expected.Length) == 0 &&
                (position + expected.Length == json.Length || !char.IsLetterOrDigit(json[position + expected.Length]));
        }

        private static bool TryExtractContent(string json, out string content)
        {
            content = string.Empty;
            if (string.IsNullOrEmpty(json)) return false;
            int field = json.IndexOf("\"content\"", StringComparison.Ordinal);
            if (field < 0) return false;
            int colon = json.IndexOf(':', field + 9);
            if (colon < 0) return false;
            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length || json[start++] != '"') return false;

            var decoded = new StringBuilder();
            while (start < json.Length)
            {
                char value = json[start++];
                if (value == '"')
                {
                    content = decoded.ToString();
                    return true;
                }
                if (value < 0x20) return false;
                if (value != '\\')
                {
                    decoded.Append(value);
                    continue;
                }
                if (start >= json.Length) return false;
                switch (json[start++])
                {
                    case '"': decoded.Append('"'); break;
                    case '\\': decoded.Append('\\'); break;
                    case '/': decoded.Append('/'); break;
                    case 'b': decoded.Append('\b'); break;
                    case 'f': decoded.Append('\f'); break;
                    case 'n': decoded.Append('\n'); break;
                    case 'r': decoded.Append('\r'); break;
                    case 't': decoded.Append('\t'); break;
                    case 'u':
                        if (start + 4 > json.Length) return false;
                        int code = 0;
                        for (int digit = 0; digit < 4; digit++)
                        {
                            int hex = Hex(json[start++]);
                            if (hex < 0) return false;
                            code = code * 16 + hex;
                        }
                        decoded.Append((char)code);
                        break;
                    default: return false;
                }
            }
            return false;
        }

        private static bool IsWhitespace(char value)
        { return value == ' ' || value == '\t' || value == '\r' || value == '\n'; }

        private static bool IsClosingQuote(char value)
        { return value == '\'' || value == '"'; }

        private static bool IsSentenceTerminator(char value)
        { return value == '.' || value == '?' || value == '!'; }

        private static int Hex(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }
    }
}
