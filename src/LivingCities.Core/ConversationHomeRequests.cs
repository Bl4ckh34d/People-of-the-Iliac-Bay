using System.Text.RegularExpressions;

namespace LivingCities.Core
{
    public static class ConversationHomeRequests
    {
        private static readonly Regex HomeQuestion = new Regex(
            @"\bwhere\s+(?:do|did|would)\s+you\s+(?:live|stay|reside)\b|\bwhere\s+is\s+your\s+(?:home|house|place|(?:home\s+)?address)\b|\bwhat(?:'s|\s+is)\s+your\s+(?:home|house|(?:home\s+)?address)\b|\bwhere\s+can\s+i\s+find\s+your\s+(?:home|house)\b|\bwhich\s+house\s+is\s+yours\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ExplicitHomeTarget = new Regex(
            @"\byour\s+(?:home|house|address|place)\b|\bwhere\s+you\s+live\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HomeFollowup = new Regex(
            @"\b(?:it|there|that|this)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsHomeInformationRequest(string text)
        { return HomeQuestion.IsMatch(text ?? string.Empty); }

        public static bool IsExplicitHomeMapRequest(string text)
        { return ConversationKnowledge.WantsMap(text) && ExplicitHomeTarget.IsMatch(text ?? string.Empty); }

        public static bool IsFollowupHomeMapRequest(string text, bool homeTopicEstablished)
        { return homeTopicEstablished && ConversationKnowledge.WantsMap(text) && HomeFollowup.IsMatch(text ?? string.Empty); }

        // +20 is the existing effective-disposition threshold for comfort with a familiar face.
        public static bool CanShareHome(int effectiveDisposition)
        { return effectiveDisposition >= 20; }
    }
}
