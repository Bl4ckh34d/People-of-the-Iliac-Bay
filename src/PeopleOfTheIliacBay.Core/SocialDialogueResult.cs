using System;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class SocialDialogueResult
    {
        public const int MinimumMoodDelta = -10;
        public const int MaximumMoodDelta = 10;
        private const int MaximumJsonLength = 8192;

        public readonly string Reply;
        public readonly int MoodDelta;

        private SocialDialogueResult(string reply, int moodDelta)
        {
            Reply = reply;
            MoodDelta = moodDelta;
        }

        public static bool TryParse(string text, out SocialDialogueResult result)
        {
            result = null;
            if (string.IsNullOrEmpty(text) || text.Length > MaximumJsonLength) return false;
            try
            {
                var reader = new Reader(text);
                reader.Expect('{');
                string reply = null;
                int moodDelta = 0;
                bool hasReply = false;
                bool hasMoodDelta = false;
                reader.White();
                if (!reader.TryConsume('}'))
                {
                    while (true)
                    {
                        string key = reader.String();
                        reader.Expect(':');
                        if (string.Equals(key, "reply", StringComparison.Ordinal))
                        {
                            if (hasReply) return false;
                            reply = reader.String();
                            hasReply = true;
                        }
                        else if (string.Equals(key, "moodDelta", StringComparison.Ordinal))
                        {
                            if (hasMoodDelta || !reader.Integer(out moodDelta)) return false;
                            hasMoodDelta = true;
                        }
                        else return false;

                        reader.White();
                        if (reader.TryConsume('}')) break;
                        reader.Expect(',');
                        reader.White();
                    }
                }
                reader.End();
                if (!hasReply || !hasMoodDelta || moodDelta < MinimumMoodDelta || moodDelta > MaximumMoodDelta)
                    return false;
                var request = new DialogueRequest("", "Fallback.", "NPC", "", "", "", 0, true);
                string selected;
                if (!LocalDialogue.TrySelectAnswer(request, reply, out selected)) return false;
                result = new SocialDialogueResult(selected, moodDelta);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public string ToJson()
        {
            return "{\"reply\":\"" + LocalDialogueCompletion.EscapeJson(Reply) + "\",\"moodDelta\":" +
                MoodDelta.ToString(CultureInfo.InvariantCulture) + "}";
        }

        public static string Grammar()
        {
            var grammar = new StringBuilder();
            // Permit normal JSON formatting without constraining the model to compact serialization.
            grammar.Append("root ::= ws ").Append(Term("{")).Append(" ws ")
                .Append(Term("\"reply\"")).Append(" ws ").Append(Term(":")).Append(" ws string ws ")
                .Append(Term(",")).Append(" ws ").Append(Term("\"moodDelta\""))
                .Append(" ws ").Append(Term(":")).Append(" ws mood-delta ws ")
                .Append(Term("}")).Append(" ws\n");
            grammar.Append(@"ws ::= [ \t\n\r]*").Append('\n');
            grammar.Append("mood-delta ::= ");
            for (int value = MinimumMoodDelta; value <= MaximumMoodDelta; value++)
            {
                if (value != MinimumMoodDelta) grammar.Append(" | ");
                grammar.Append(Term(value.ToString(CultureInfo.InvariantCulture)));
            }
            grammar.Append('\n').Append("string ::= ").Append(Term("\""))
                .Append(@" ( [^""\\\x00-\x1f] | ").Append(Term("\\"))
                .Append(" ( ").Append(Term("\"" )).Append(" | ").Append(Term("\\"));
            foreach (string escape in new[] { "/", "b", "f", "n", "r", "t" })
                grammar.Append(" | ").Append(Term(escape));
            grammar.Append(" | ").Append(Term("u")).Append(" [0-9a-fA-F]{4} ) )* ").Append(Term("\""));
            return grammar.ToString();
        }

        private static string Term(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private sealed class Reader
        {
            private readonly string text;
            private int position;

            public Reader(string value) { text = value; }

            public string String()
            {
                White();
                Require('"');
                var value = new StringBuilder();
                while (position < text.Length)
                {
                    char current = text[position++];
                    if (current == '"') return value.ToString();
                    if (current < 0x20) throw new FormatException();
                    if (current == '\\')
                    {
                        if (position >= text.Length) throw new FormatException();
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
                            case 'u':
                                char unicode = HexChar();
                                if (char.IsHighSurrogate(unicode))
                                {
                                    if (position + 2 > text.Length || text[position] != '\\' || text[position + 1] != 'u')
                                        throw new FormatException();
                                    position += 2;
                                    char low = HexChar();
                                    if (!char.IsLowSurrogate(low)) throw new FormatException();
                                    value.Append(unicode).Append(low);
                                }
                                else if (char.IsLowSurrogate(unicode)) throw new FormatException();
                                else value.Append(unicode);
                                break;
                            default: throw new FormatException();
                        }
                    }
                    else if (char.IsHighSurrogate(current))
                    {
                        if (position >= text.Length || !char.IsLowSurrogate(text[position])) throw new FormatException();
                        value.Append(current).Append(text[position++]);
                    }
                    else if (char.IsLowSurrogate(current)) throw new FormatException();
                    else value.Append(current);
                }
                throw new FormatException();
            }

            public bool Integer(out int value)
            {
                White();
                int start = position;
                if (position < text.Length && text[position] == '-') position++;
                if (position >= text.Length || text[position] < '0' || text[position] > '9')
                {
                    value = 0;
                    return false;
                }
                if (text[position] == '0')
                {
                    position++;
                    if (position < text.Length && text[position] >= '0' && text[position] <= '9')
                    {
                        value = 0;
                        return false;
                    }
                }
                else
                {
                    while (position < text.Length && text[position] >= '0' && text[position] <= '9') position++;
                }
                return int.TryParse(text.Substring(start, position - start), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out value);
            }

            public void Expect(char value)
            {
                White();
                Require(value);
            }

            public bool TryConsume(char value)
            {
                White();
                if (position >= text.Length || text[position] != value) return false;
                position++;
                return true;
            }

            public void End()
            {
                White();
                if (position != text.Length) throw new FormatException();
            }

            public void White()
            {
                while (position < text.Length && (text[position] == ' ' || text[position] == '\t' ||
                    text[position] == '\r' || text[position] == '\n')) position++;
            }

            private char HexChar()
            {
                if (position + 4 > text.Length) throw new FormatException();
                int value = 0;
                for (int index = 0; index < 4; index++)
                {
                    char current = text[position++];
                    int digit = current >= '0' && current <= '9' ? current - '0' :
                        current >= 'a' && current <= 'f' ? current - 'a' + 10 :
                        current >= 'A' && current <= 'F' ? current - 'A' + 10 : -1;
                    if (digit < 0) throw new FormatException();
                    value = value * 16 + digit;
                }
                return (char)value;
            }

            private void Require(char value)
            {
                if (position >= text.Length || text[position] != value) throw new FormatException();
                position++;
            }
        }
    }
}
