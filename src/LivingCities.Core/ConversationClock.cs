using System.Globalization;
using System.Text.RegularExpressions;

namespace LivingCities.Core
{
    public static class ConversationClock
    {
        // Whole-question matching avoids stealing shop opening-time questions,
        // quoted speech or compound insults from the normal intent/tone path.
        private static readonly Regex Question = new Regex(
            @"\A\s*(?:please\s+)?(?:what\s+(?:time|date|day|year)\s+is\s+it|what(?:\s+is|'s)\s+(?:the\s+)?(?:current\s+)?(?:time|date|day|year)|(?:can|could|would)\s+you\s+(?:please\s+)?tell\s+me\s+(?:the\s+)?(?:current\s+)?(?:time|date|day|year)|tell\s+me\s+(?:the\s+)?(?:current\s+)?(?:time|date|day|year))(?:\s+please)?\s*[?.!]?\s*\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsQuestion(string text)
        { return text != null && text.Length <= 160 && Question.IsMatch(text); }

        public static string Answer(string time, string date, int year)
        { return "It is " + time + " on " + date + ", 3E" + year.ToString(CultureInfo.InvariantCulture) + "."; }
    }
}
