using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class LocalDecisionPayload
    {
        private readonly byte[] bytes;
        public byte[] Bytes => (byte[])bytes.Clone();
        public string Sha256 { get; }

        internal LocalDecisionPayload(byte[] value, string sha256)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            bytes = (byte[])value.Clone();
            Sha256 = sha256;
        }
    }

    internal sealed class LocalDecisionParsedReply
    {
        private readonly IReadOnlyDictionary<string, double> probabilities;
        public bool IsValid { get; }
        public NpcDecisionFailureKind FailureKind { get; }
        public string DiagnosticCode { get; }
        public string SelectedOptionId { get; }
        public IReadOnlyDictionary<string, double> Probabilities => probabilities;
        public double? ReportedConfidence { get; }
        public string ModelId { get; }

        internal LocalDecisionParsedReply(bool valid, NpcDecisionFailureKind failureKind, string diagnosticCode,
            string selectedOptionId, IDictionary<string, double> values, double? confidence, string modelId)
        {
            IsValid = valid;
            FailureKind = failureKind;
            DiagnosticCode = diagnosticCode;
            SelectedOptionId = valid ? selectedOptionId : null;
            probabilities = valid && values != null
                ? new ReadOnlyDictionary<string, double>(new Dictionary<string, double>(values, StringComparer.Ordinal))
                : null;
            ReportedConfidence = valid ? confidence : null;
            ModelId = modelId;
        }
    }

    internal static class LocalDecisionWire
    {
        private const int MaxBytes = 128 * 1024;
        private const int MaxResponseChars = 128 * 1024;
        private const string QuestionId = "decision";

        public static LocalDecisionPayload Build(NpcDecisionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var state = new StringBuilder();
            state.Append('{');
            Property(state, "protocolVersion", request.ProtocolVersion); state.Append(',');
            Property(state, "purpose", request.Purpose); state.Append(',');
            Property(state, "requestId", request.RequestId.ToString("D")); state.Append(',');
            Property(state, "actorId", request.Binding.PersonId ?? request.Binding.EphemeralSpeakerToken); state.Append(',');
            Property(state, "originalTurn", request.OriginalTurn); state.Append(',');
            Property(state, "context", request.Context); state.Append(',');
            NumberProperty(state, "snapshotRevision", request.SnapshotRevision); state.Append(',');
            NumberProperty(state, "candidateSetRevision", request.CandidateSetRevision);
            state.Append('}');

            var json = new StringBuilder();
            json.Append("{\"state\":");
            Quote(json, state.ToString());
            json.Append(",\"questions\":{\"decision\":{\"type\":\"choice\",\"instructions\":");
            Quote(json, request.Instructions);
            json.Append(",\"criteria\":{");
            for (int i = 0; i < request.Options.Count; i++)
            {
                if (i != 0) json.Append(',');
                Quote(json, request.Options[i].Id);
                json.Append(':');
                Quote(json, request.Options[i].Description);
            }
            json.Append("}}}}");
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(json.ToString());
            if (bytes.Length > MaxBytes) throw new ArgumentException("Decision request exceeds the 128 KiB wire limit.", nameof(request));
            string hash;
            using (var sha = SHA256.Create())
                hash = Hex(sha.ComputeHash(bytes));
            return new LocalDecisionPayload(bytes, hash);
        }

        public static LocalDecisionParsedReply Parse(string response)
        {
            if (response == null || response.Length > MaxResponseChars)
                return Invalid(NpcDecisionFailureKind.MalformedJson, "response_size_or_null");
            object root;
            try
            {
                root = new StrictJsonParser(response).Parse();
            }
            catch (JsonParseException)
            {
                return Invalid(NpcDecisionFailureKind.MalformedJson, "malformed_json");
            }

            var rootObject = root as Dictionary<string, object>;
            if (rootObject == null || !OnlyKeys(rootObject, "model", "answers", "usage") ||
                !rootObject.ContainsKey("model") || !rootObject.ContainsKey("answers"))
                return Invalid(NpcDecisionFailureKind.InvalidSchema, "invalid_root");
            string modelId = rootObject["model"] as string;
            if (string.IsNullOrWhiteSpace(modelId)) return Invalid(NpcDecisionFailureKind.InvalidSchema, "invalid_model");
            if (rootObject.ContainsKey("usage") && !ValidUsage(rootObject["usage"]))
                return Invalid(NpcDecisionFailureKind.InvalidSchema, "invalid_usage");

            var answers = rootObject["answers"] as Dictionary<string, object>;
            if (answers == null || answers.Count != 1 || !answers.ContainsKey(QuestionId))
                return Invalid(NpcDecisionFailureKind.InvalidSchema, "unknown_or_missing_question");
            var answer = answers[QuestionId] as Dictionary<string, object>;
            if (answer == null || !OnlyKeys(answer, "type", "choice", "probabilities", "confidence") ||
                !answer.ContainsKey("type") || !answer.ContainsKey("choice") || !answer.ContainsKey("probabilities"))
                return Invalid(NpcDecisionFailureKind.InvalidSchema, "invalid_answer_shape");
            if (!string.Equals(answer["type"] as string, "choice", StringComparison.Ordinal))
                return Invalid(NpcDecisionFailureKind.InvalidSchema, "invalid_answer_type");
            string choice = answer["choice"] as string;
            if (choice == null) return Invalid(NpcDecisionFailureKind.InvalidSchema, "invalid_choice_type");

            var sourceProbabilities = answer["probabilities"] as Dictionary<string, object>;
            if (sourceProbabilities == null) return Invalid(NpcDecisionFailureKind.InvalidSchema, "missing_probabilities");
            var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var pair in sourceProbabilities)
            {
                if (pair.Value is double)
                    probabilities.Add(pair.Key, (double)pair.Value);
                else
                    return Invalid(NpcDecisionFailureKind.InvalidSchema, "invalid_probability_type");
            }
            double? confidence = null;
            if (answer.ContainsKey("confidence"))
            {
                if (!(answer["confidence"] is double)) return Invalid(NpcDecisionFailureKind.InvalidSchema, "invalid_confidence_type");
                confidence = (double)answer["confidence"];
            }
            return new LocalDecisionParsedReply(true, NpcDecisionFailureKind.None, "ok", choice, probabilities, confidence, modelId);
        }

        private static LocalDecisionParsedReply Invalid(NpcDecisionFailureKind kind, string diagnostic)
            => new LocalDecisionParsedReply(false, kind, diagnostic, null, null, null, null);

        private static bool ValidUsage(object value)
        {
            var usage = value as Dictionary<string, object>;
            if (usage == null) return false;
            foreach (var pair in usage)
            {
                if (pair.Key != "input_tokens" && pair.Key != "output_tokens" && pair.Key != "total_tokens") return false;
                if (!(pair.Value is double)) return false;
                double count = (double)pair.Value;
                if (count < 0 || count != Math.Truncate(count)) return false;
            }
            return true;
        }

        private static bool OnlyKeys(Dictionary<string, object> value, params string[] keys)
        {
            var allowed = new HashSet<string>(keys, StringComparer.Ordinal);
            return value.Keys.All(allowed.Contains);
        }

        private static void Property(StringBuilder builder, string key, string value)
        { Quote(builder, key); builder.Append(':'); Quote(builder, value); }

        private static void NumberProperty(StringBuilder builder, string key, long value)
        { Quote(builder, key); builder.Append(':').Append(value.ToString(CultureInfo.InvariantCulture)); }

        private static void Quote(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }

        private static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private sealed class JsonParseException : Exception { }

        private sealed class StrictJsonParser
        {
            private readonly string text;
            private int position;
            public StrictJsonParser(string value) { text = value; }

            public object Parse()
            {
                SkipWhitespace();
                object value = ReadValue(1);
                SkipWhitespace();
                if (position != text.Length) throw new JsonParseException();
                return value;
            }

            private object ReadValue(int depth)
            {
                if (position >= text.Length) throw new JsonParseException();
                char c = text[position];
                if (c == '{') return ReadObject(depth);
                if (c == '[') return ReadArray(depth);
                if (c == '"') return ReadString();
                if (c == 't') { Literal("true"); return true; }
                if (c == 'f') { Literal("false"); return false; }
                if (c == 'n') { Literal("null"); return null; }
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw new JsonParseException();
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                CheckDepth(depth);
                position++;
                SkipWhitespace();
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                if (Take('}')) return result;
                while (true)
                {
                    if (position >= text.Length || text[position] != '"') throw new JsonParseException();
                    string key = ReadString();
                    if (result.ContainsKey(key)) throw new JsonParseException();
                    SkipWhitespace();
                    Require(':');
                    SkipWhitespace();
                    result.Add(key, ReadValue(depth + 1));
                    SkipWhitespace();
                    if (Take('}')) return result;
                    Require(',');
                    SkipWhitespace();
                }
            }

            private List<object> ReadArray(int depth)
            {
                CheckDepth(depth);
                position++;
                SkipWhitespace();
                var result = new List<object>();
                if (Take(']')) return result;
                while (true)
                {
                    result.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (Take(']')) return result;
                    Require(',');
                    SkipWhitespace();
                }
            }

            private string ReadString()
            {
                Require('"');
                var result = new StringBuilder();
                while (position < text.Length)
                {
                    char c = text[position++];
                    if (c == '"')
                    {
                        string value = result.ToString();
                        if (value.Any(character => character < 0x20)) throw new JsonParseException();
                        return value;
                    }
                    if (c < 0x20) throw new JsonParseException();
                    if (c == '\\')
                    {
                        if (position >= text.Length) throw new JsonParseException();
                        char escaped = text[position++];
                        switch (escaped)
                        {
                            case '"': result.Append('"'); break;
                            case '\\': result.Append('\\'); break;
                            case '/': result.Append('/'); break;
                            case 'b': result.Append('\b'); break;
                            case 'f': result.Append('\f'); break;
                            case 'n': result.Append('\n'); break;
                            case 'r': result.Append('\r'); break;
                            case 't': result.Append('\t'); break;
                            case 'u':
                                char unicode = ReadHexChar();
                                if (char.IsHighSurrogate(unicode))
                                {
                                    if (position + 2 > text.Length || text[position] != '\\' || text[position + 1] != 'u') throw new JsonParseException();
                                    position += 2;
                                    char low = ReadHexChar();
                                    if (!char.IsLowSurrogate(low)) throw new JsonParseException();
                                    result.Append(unicode).Append(low);
                                }
                                else if (char.IsLowSurrogate(unicode)) throw new JsonParseException();
                                else result.Append(unicode);
                                break;
                            default: throw new JsonParseException();
                        }
                    }
                    else if (char.IsHighSurrogate(c))
                    {
                        if (position >= text.Length || !char.IsLowSurrogate(text[position])) throw new JsonParseException();
                        result.Append(c).Append(text[position++]);
                    }
                    else if (char.IsLowSurrogate(c)) throw new JsonParseException();
                    else result.Append(c);
                }
                throw new JsonParseException();
            }

            private char ReadHexChar()
            {
                if (position + 4 > text.Length) throw new JsonParseException();
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    char c = text[position++];
                    int digit = c >= '0' && c <= '9' ? c - '0' :
                        c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    if (digit < 0) throw new JsonParseException();
                    value = value * 16 + digit;
                }
                return (char)value;
            }

            private double ReadNumber()
            {
                int start = position;
                Take('-');
                if (Take('0'))
                {
                    if (position < text.Length && char.IsDigit(text[position])) throw new JsonParseException();
                }
                else
                {
                    if (!DigitOneToNine()) throw new JsonParseException();
                    while (Digit()) { }
                }
                if (Take('.'))
                {
                    if (!Digit()) throw new JsonParseException();
                    while (Digit()) { }
                }
                if (Take('e') || Take('E'))
                {
                    if (!Take('+')) Take('-');
                    if (!Digit()) throw new JsonParseException();
                    while (Digit()) { }
                }
                double value;
                int significandEnd = position;
                for (int i = start; i < position; i++)
                {
                    if (text[i] == 'e' || text[i] == 'E') { significandEnd = i; break; }
                }
                bool nonzeroSignificand = false;
                for (int i = start; i < significandEnd; i++)
                {
                    if (text[i] >= '1' && text[i] <= '9') { nonzeroSignificand = true; break; }
                }
                if (!double.TryParse(text.Substring(start, position - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
                    throw new JsonParseException();
                if (value == 0 && nonzeroSignificand) throw new JsonParseException();
                return value;
            }

            private bool DigitOneToNine()
            {
                if (position >= text.Length || text[position] < '1' || text[position] > '9') return false;
                position++;
                return true;
            }
            private bool Digit()
            {
                if (position >= text.Length || text[position] < '0' || text[position] > '9') return false;
                position++;
                return true;
            }
            private void Literal(string value)
            {
                if (position + value.Length > text.Length || string.CompareOrdinal(text, position, value, 0, value.Length) != 0)
                    throw new JsonParseException();
                position += value.Length;
            }
            private void CheckDepth(int depth) { if (depth > 16) throw new JsonParseException(); }
            private void SkipWhitespace()
            {
                while (position < text.Length && (text[position] == ' ' || text[position] == '\t' || text[position] == '\r' || text[position] == '\n')) position++;
            }
            private bool Take(char c)
            {
                if (position >= text.Length || text[position] != c) return false;
                position++;
                return true;
            }
            private void Require(char c) { if (!Take(c)) throw new JsonParseException(); }
        }
    }
}
