using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcGoalChat
    {
        public const int MaximumSteps = 8;
        public const int MaximumTurnLength = 8192;
        private const int MaximumOutputLength = 32768;
        private const int MaximumParameterLength = 512;

        private static readonly string[] Kinds =
        {
            "Wait", "Follow", "MoveTo", "Purchase", "Transfer", "Interact", "TakeSeat",
            "ChangeSchedule", "Attack", "CastSpell", "Lockpick", "Climb", "Other"
        };

        internal static bool IsCanonicalKind(string kind)
        {
            return kind != null && Array.IndexOf(Kinds, kind) >= 0;
        }

        internal static bool OccursWithin(string value, string source)
        {
            return value.Length == 0 || source.IndexOf(value, StringComparison.Ordinal) >= 0;
        }

        public static string Grammar()
        {
            var grammar = new StringBuilder();
            grammar.Append("root ::= ").Append(Term("{\"intent\":" )).Append(" intent ")
                .Append(Term(",\"steps\":[")).Append(" steps ").Append(Term("],\"conditions\":["))
                .Append(" strings ").Append(Term("],\"exclusions\":[")).Append(" strings ")
                .Append(Term("],\"missing\":[")).Append(" strings ").Append(Term("]}")).Append('\n');
            grammar.Append("intent ::= ").Append(Term("\"request\"")).Append(" | ").Append(Term("\"none\""))
                .Append(" | ").Append(Term("\"clarify\"")).Append('\n');
            grammar.Append("steps ::= step (").Append(Term(",")).Append(" step)* | ").Append(Term("")).Append('\n');
            grammar.Append("step ::= ").Append(Term("{\"kind\":" )).Append(" kind ").Append(Term(",\"source\":"))
                .Append(" string ").Append(Term(",\"target\":" )).Append(" string ").Append(Term(",\"duration\":"))
                .Append(" nullable-duration ").Append(Term(",\"quantity\":" )).Append(" nullable-quantity ").Append(Term(",\"missing\":["))
                .Append(" strings ").Append(Term("]}" )).Append('\n');
            grammar.Append("kind ::= ");
            for (int i = 0; i < Kinds.Length; i++)
            {
                if (i > 0) grammar.Append(" | ");
                grammar.Append(Term("\"" + Kinds[i] + "\""));
            }
            grammar.Append('\n').Append("strings ::= string (").Append(Term(",")).Append(" string)* | ").Append(Term(""))
                .Append('\n').Append("nullable-duration ::= ").Append(Term("null"))
                .Append(" | ").Append(Term("{\"amount\":" )).Append(" positive-decimal ").Append(Term(",\"unit\":"))
                .Append(" time-unit ").Append(Term(",\"source\":" )).Append(" string ").Append(Term("}"))
                .Append('\n').Append("nullable-quantity ::= ").Append(Term("null"))
                .Append(" | ").Append(Term("{\"amount\":" )).Append(" positive-integer ").Append(Term(",\"source\":"))
                .Append(" string ").Append(Term("}"))
                .Append('\n').Append("time-unit ::= ").Append(Term("\"game_seconds\"")).Append(" | ").Append(Term("\"game_minutes\""))
                .Append(" | ").Append(Term("\"game_hours\""))
                .Append('\n').Append("positive-decimal ::= [1-9] [0-9]* (").Append(Term("."))
                .Append(" [0-9]+)? | ").Append(Term("0")).Append(" ").Append(Term("."))
                .Append(" [0-9]* [1-9] [0-9]*")
                .Append('\n').Append("positive-integer ::= [1-9] [0-9]*")
                .Append('\n').Append("string ::= ").Append(Term("\""))
                .Append(@" ( [^""\\\x00-\x1f] | ").Append(Term("\\"))
                .Append(" ( ").Append(Term("\"")).Append(" | ").Append(Term("\\"));
            foreach (string escape in new[] { "/", "b", "f", "n", "r", "t" })
                grammar.Append(" | ").Append(Term(escape));
            grammar.Append(" | ").Append(Term("u")).Append(" [0-9a-fA-F]{4} ) )* ").Append(Term("\""));
            return grammar.ToString();
        }

        private static string Term(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        public static string Prompt(string originalText, string attributedHistory, string candidateContext)
        {
            var prompt = new StringBuilder();
            prompt.Append("Extract the player's current-turn goal and constraints. Return only one JSON object matching this exact field order and schema:\n");
            prompt.Append("{\"intent\":\"request|none|clarify\",\"steps\":[{\"kind\":\"Wait|Follow|MoveTo|Purchase|Transfer|Interact|TakeSeat|ChangeSchedule|Attack|CastSpell|Lockpick|Climb|Other\",\"source\":\"exact current-turn quote\",\"target\":\"surface target or empty\",\"duration\":null,\"quantity\":null,\"missing\":[]}],\"conditions\":[],\"exclusions\":[],\"missing\":[]}\n");
            prompt.Append("Use request only when current turn asks for an action. Use none when no action is requested. Use clarify when intent or required meaning is unclear. None and clarify require empty steps. Preserve steps in original order, up to eight. Each step source must be an exact, ordered quote from current player turn. Copy target text exactly from that quote, or use empty string when absent. Duration is null when unstated; otherwise use {\"amount\":positive decimal,\"unit\":\"game_seconds|game_minutes|game_hours\",\"source\":\"exact duration quote\"}. All duration units mean in-game time, never real-world elapsed time. Quantity is null when unstated; otherwise use {\"amount\":positive integer,\"source\":\"exact quantity quote\"}. Translate each explicit duration or quantity expression into amount; preserve exact expression in source. Do not invent values or convert units. Keep quote exact even when translated amount seems uncertain. Put unsupported actions under kind Other. Preserve unknown requirements in missing arrays. Do not invent facts, IDs, coordinates, effects, willingness, consent, feasibility, or native decisions. Do not claim that an action happened.\n");
            prompt.Append("Attributed conversation history (context for references; current turn remains authoritative):\n").Append(attributedHistory ?? "").Append("\n");
            prompt.Append("Factual context only; contains no action candidate menu:\n").Append(candidateContext ?? "").Append("\n");
            prompt.Append("Exact current player turn:\n").Append(originalText ?? "").Append("\nJSON:");
            return prompt.ToString();
        }

        // Runtime extraction keeps the complete attributed transcript in the
        // separate history argument to LocalDialogueCompletion.Extract. That
        // makes the transcript the only windowable part of the prompt; the
        // current turn, factual context, and schema remain fixed.
        public static string ExtractionInstructions(string originalText, string factualContext)
        {
            var prompt = new StringBuilder();
            prompt.Append("Extract the player's current-turn goal and constraints. Return only one JSON object matching this exact field order and schema:\n");
            prompt.Append("{\"intent\":\"request|none|clarify\",\"steps\":[{\"kind\":\"Wait|Follow|MoveTo|Purchase|Transfer|Interact|TakeSeat|ChangeSchedule|Attack|CastSpell|Lockpick|Climb|Other\",\"source\":\"exact current-turn quote\",\"target\":\"surface target or empty\",\"duration\":null,\"quantity\":null,\"missing\":[]}],\"conditions\":[],\"exclusions\":[],\"missing\":[]}\n");
            prompt.Append("Use request only when current turn asks for an action. Use none when no action is requested. Use clarify when intent or required meaning is unclear. None and clarify require empty steps. Preserve steps in original order, up to eight. Each step source must be an exact, ordered quote from current player turn. Copy target text exactly from that quote, or use empty string when absent. Duration is null when unstated; otherwise use {\"amount\":positive decimal,\"unit\":\"game_seconds|game_minutes|game_hours\",\"source\":\"exact duration quote\"}. All duration units mean in-game time, never real-world elapsed time. Quantity is null when unstated; otherwise use {\"amount\":positive integer,\"source\":\"exact quantity quote\"}. Translate each explicit duration or quantity expression into amount; preserve exact expression in source. Do not invent values or convert units. Keep quote exact even when translated amount seems uncertain. Put unsupported actions under kind Other. Preserve unknown requirements in missing arrays. Do not invent facts, IDs, coordinates, effects, willingness, consent, feasibility, or native decisions. Do not claim that an action happened.\n");
            prompt.Append("Attributed conversation history is supplied separately before this fixed extraction request as context for references. The exact current player turn below is authoritative.\n");
            prompt.Append("Factual context only; contains no action candidate menu:\n").Append(factualContext ?? "").Append("\n");
            prompt.Append("Exact current player turn:\n").Append(originalText ?? "").Append("\nJSON:");
            return prompt.ToString();
        }

        public static NpcGoalExtractionResult Parse(string output, ConversationContextBinding binding, string originalText)
        {
            if (string.IsNullOrWhiteSpace(output))
                return new NpcGoalExtractionResult(NpcGoalExtractionStatus.Unavailable, null, "No extraction output.");
            if (binding == null || originalText == null)
                return Invalid("Missing request binding or original turn.");
            if (originalText.Length > MaximumTurnLength)
                return Invalid("Original turn exceeds length limit.");
            if (output.Length > MaximumOutputLength)
                return Invalid("Extraction output exceeds length limit.");
            if (!IsWellFormedUtf16(originalText))
                return Invalid("Original turn has invalid UTF16.");

            try
            {
                var reader = new FixedJsonReader(output);
                reader.Expect('{');
                reader.Field("intent");
                string intentText = reader.String(16);
                NpcGoalIntent intent;
                if (intentText == "request") intent = NpcGoalIntent.Request;
                else if (intentText == "none") intent = NpcGoalIntent.None;
                else if (intentText == "clarify") intent = NpcGoalIntent.Clarify;
                else throw new FormatException("Unknown intent.");

                reader.CommaField("steps");
                List<RawStep> rawSteps = reader.Steps();
                reader.CommaField("conditions");
                List<string> conditions = reader.StringArray(16, 1024);
                reader.CommaField("exclusions");
                List<string> exclusions = reader.StringArray(16, 1024);
                reader.CommaField("missing");
                List<string> missing = reader.StringArray(16, 512);
                reader.Expect('}');
                reader.End();

                if ((intent == NpcGoalIntent.Request && rawSteps.Count == 0) ||
                    (intent != NpcGoalIntent.Request && rawSteps.Count != 0))
                    throw new FormatException("Intent and steps conflict.");

                var steps = new List<NpcGoalStep>(rawSteps.Count);
                int searchFrom = 0;
                foreach (RawStep raw in rawSteps)
                {
                    string field = "steps[" + steps.Count + "]";
                    int start = originalText.IndexOf(raw.Source, searchFrom, StringComparison.Ordinal);
                    if (start < 0)
                        throw new FormatException(field + ".source is not an ordered exact span in current turn; sources must not overlap.");
                    if (!OccursWithin(raw.Target, raw.Source))
                        throw new FormatException(field + ".target is not quoted in " + field + ".source.");
                    if (raw.Duration != null && !OccursWithin(raw.Duration.Source, raw.Source))
                        throw new FormatException(field + ".duration.source is not quoted in " + field + ".source.");
                    if (raw.Quantity != null && !OccursWithin(raw.Quantity.Source, raw.Source))
                        throw new FormatException(field + ".quantity.source is not quoted in " + field + ".source.");
                    steps.Add(new NpcGoalStep(raw.Kind, new NpcGoalSourceSpan(start, raw.Source.Length, raw.Source),
                        raw.Target, raw.Duration, raw.Quantity, raw.Missing));
                    searchFrom = start + raw.Source.Length;
                }

                var proposal = new NpcGoalProposal(binding, originalText, intent, steps, conditions, exclusions, missing);
                return new NpcGoalExtractionResult(NpcGoalExtractionStatus.Valid, proposal, "");
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException)
            {
                return Invalid(ex.Message);
            }
        }

        private static NpcGoalExtractionResult Invalid(string reason)
        {
            return new NpcGoalExtractionResult(NpcGoalExtractionStatus.Invalid, null,
                NpcGoalExtractionResult.LimitReason(reason));
        }

        private static bool IsWellFormedUtf16(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return false;
                    i++;
                }
                else if (char.IsLowSurrogate(c)) return false;
            }
            return true;
        }

        private sealed class RawStep
        {
            public string Kind;
            public string Source;
            public string Target;
            public NpcGoalDuration Duration;
            public NpcGoalQuantity Quantity;
            public List<string> Missing;
        }

        private sealed class FixedJsonReader
        {
            private readonly string text;
            private int position;

            public FixedJsonReader(string text) { this.text = text; }

            public void Expect(char expected)
            {
                White();
                if (position >= text.Length || text[position] != expected)
                    throw new FormatException("Expected '" + expected + "'.");
                position++;
            }

            public void Field(string expected)
            {
                string actual = String(32);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    throw new FormatException("Unexpected or out-of-order field.");
                Expect(':');
            }

            public void CommaField(string expected)
            {
                Expect(',');
                Field(expected);
            }

            public List<RawStep> Steps()
            {
                var result = new List<RawStep>();
                Expect('[');
                White();
                if (TryConsume(']')) return result;
                while (true)
                {
                    if (result.Count >= MaximumSteps) throw new FormatException("Too many steps.");
                    result.Add(Step());
                    White();
                    if (TryConsume(']')) return result;
                    Expect(',');
                }
            }

            private RawStep Step()
            {
                Expect('{');
                Field("kind");
                string kind = String(32);
                if (Array.IndexOf(Kinds, kind) < 0) throw new FormatException("Unknown step kind.");
                CommaField("source");
                string source = String(MaximumTurnLength);
                if (source.Length == 0) throw new FormatException("Empty source quote.");
                CommaField("target");
                string target = String(MaximumParameterLength);
                CommaField("duration");
                NpcGoalDuration duration = Duration();
                CommaField("quantity");
                NpcGoalQuantity quantity = Quantity();
                CommaField("missing");
                List<string> missing = StringArray(8, 512);
                Expect('}');
                return new RawStep { Kind = kind, Source = source, Target = target, Duration = duration,
                    Quantity = quantity, Missing = missing };
            }

            private NpcGoalDuration Duration()
            {
                if (Null()) return null;
                Expect('{');
                Field("amount");
                decimal amount = PositiveDecimal();
                CommaField("unit");
                string unitText = String(32);
                NpcGoalTimeUnit unit;
                if (unitText == "game_seconds") unit = NpcGoalTimeUnit.GameSeconds;
                else if (unitText == "game_minutes") unit = NpcGoalTimeUnit.GameMinutes;
                else if (unitText == "game_hours") unit = NpcGoalTimeUnit.GameHours;
                else throw new FormatException("Unknown game-time unit.");
                CommaField("source");
                string source = String(MaximumParameterLength);
                Expect('}');
                return new NpcGoalDuration(amount, unit, source);
            }

            private NpcGoalQuantity Quantity()
            {
                if (Null()) return null;
                Expect('{');
                Field("amount");
                int amount = PositiveInteger();
                CommaField("source");
                string source = String(MaximumParameterLength);
                Expect('}');
                return new NpcGoalQuantity(amount, source);
            }

            private decimal PositiveDecimal()
            {
                string token = NumberToken(true);
                decimal amount;
                if (!decimal.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) || amount <= 0)
                    throw new FormatException("Expected positive decimal amount.");
                return amount;
            }

            private int PositiveInteger()
            {
                string token = NumberToken(false);
                int amount;
                if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out amount) || amount <= 0)
                    throw new FormatException("Expected positive integer amount.");
                return amount;
            }

            private string NumberToken(bool allowDecimal)
            {
                White();
                int start = position;
                if (position >= text.Length || text[position] < '0' || text[position] > '9')
                    throw new FormatException("Expected JSON number.");
                if (text[position] == '0')
                {
                    position++;
                    if (position < text.Length && text[position] >= '0' && text[position] <= '9')
                        throw new FormatException("Leading zero in JSON number.");
                }
                else
                {
                    while (position < text.Length && text[position] >= '0' && text[position] <= '9') position++;
                }
                if (allowDecimal && position < text.Length && text[position] == '.')
                {
                    position++;
                    int fractionStart = position;
                    while (position < text.Length && text[position] >= '0' && text[position] <= '9') position++;
                    if (position == fractionStart) throw new FormatException("Missing decimal fraction digits.");
                }
                if (position - start > 32) throw new FormatException("Number exceeds length limit.");
                return text.Substring(start, position - start);
            }

            private bool Null()
            {
                White();
                const string literal = "null";
                if (position + literal.Length <= text.Length &&
                    string.CompareOrdinal(text, position, literal, 0, literal.Length) == 0)
                {
                    position += literal.Length;
                    return true;
                }
                return false;
            }

            public List<string> StringArray(int maximumCount, int maximumLength)
            {
                var result = new List<string>();
                Expect('[');
                White();
                if (TryConsume(']')) return result;
                while (true)
                {
                    if (result.Count >= maximumCount) throw new FormatException("String array exceeds item limit.");
                    string value = String(maximumLength);
                    if (value.Length == 0) throw new FormatException("Empty array value.");
                    result.Add(value);
                    White();
                    if (TryConsume(']')) return result;
                    Expect(',');
                }
            }

            public string String(int maximumLength)
            {
                White();
                if (position >= text.Length || text[position++] != '"') throw new FormatException("Expected string.");
                var value = new StringBuilder();
                while (position < text.Length)
                {
                    char c = text[position++];
                    if (c == '"')
                    {
                        string decoded = value.ToString();
                        if (decoded.Length > maximumLength || !IsWellFormedUtf16(decoded))
                            throw new FormatException("String exceeds limit or has invalid UTF16.");
                        return decoded;
                    }
                    if (c < 0x20) throw new FormatException("Unescaped control character.");
                    if (c == '\\')
                    {
                        if (position >= text.Length) throw new FormatException("Incomplete string escape.");
                        char escape = text[position++];
                        switch (escape)
                        {
                            case '"': value.Append('"'); break;
                            case '\\': value.Append('\\'); break;
                            case '/': value.Append('/'); break;
                            case 'b': value.Append('\b'); break;
                            case 'f': value.Append('\f'); break;
                            case 'n': value.Append('\n'); break;
                            case 'r': value.Append('\r'); break;
                            case 't': value.Append('\t'); break;
                            case 'u': value.Append((char)Hex4()); break;
                            default: throw new FormatException("Invalid string escape.");
                        }
                    }
                    else value.Append(c);
                    if (value.Length > maximumLength) throw new FormatException("String exceeds length limit.");
                }
                throw new FormatException("Unterminated string.");
            }

            private int Hex4()
            {
                if (position + 4 > text.Length) throw new FormatException("Incomplete Unicode escape.");
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    char c = text[position++];
                    int digit = c >= '0' && c <= '9' ? c - '0' :
                        c >= 'a' && c <= 'f' ? c - 'a' + 10 :
                        c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    if (digit < 0) throw new FormatException("Invalid Unicode escape.");
                    value = (value << 4) | digit;
                }
                return value;
            }

            public void End()
            {
                White();
                if (position != text.Length) throw new FormatException("Trailing data after JSON object.");
            }

            private bool TryConsume(char value)
            {
                White();
                if (position < text.Length && text[position] == value)
                {
                    position++;
                    return true;
                }
                return false;
            }

            private void White()
            {
                while (position < text.Length && (text[position] == ' ' || text[position] == '\t' ||
                    text[position] == '\r' || text[position] == '\n')) position++;
            }
        }
    }
}
