using System;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public static class DialogueCompletionText
    {
        public static string Extract(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            int field = json.IndexOf("\"content\"", StringComparison.Ordinal);
            if (field < 0) return "";
            int colon = json.IndexOf(':', field + 9);
            if (colon < 0) return "";
            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length || json[start] != '"') return "";
            var value = new StringBuilder();
            for (int index = start + 1; index < json.Length; index++)
            {
                char c = json[index];
                if (c == '"') return value.ToString();
                if (c != '\\') { value.Append(c); continue; }
                if (++index >= json.Length) return "";
                switch (json[index])
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
                        ushort code;
                        if (index + 4 >= json.Length || !ushort.TryParse(json.Substring(index + 1, 4),
                            NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) return "";
                        value.Append((char)code); index += 4; break;
                    default: return "";
                }
            }
            return "";
        }

        public static string ForClassicFont(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"')
                .Replace('—', '-').Replace('–', '-').Replace("…", "...").Replace("ß", "ss").Replace("æ", "ae").Replace("œ", "oe");
            try { text = text.Normalize(NormalizationForm.FormD); }
            catch (ArgumentException) { return ""; }
            var result = new StringBuilder();
            foreach (char c in text)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (c > 127) return ""; // Reject unreadable replies instead of displaying '?' runs.
                result.Append(c);
            }
            return result.ToString().Contains("???") ? "" : result.ToString();
        }
    }
}
