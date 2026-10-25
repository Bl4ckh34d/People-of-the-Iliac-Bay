namespace PeopleOfTheIliacBay.Core
{
    public static class DialogueReplyFallback
    {
        public const string UnavailableReply = "No new reply was received.";

        public static string Select(string verifiedFact)
        { return string.IsNullOrWhiteSpace(verifiedFact) ? UnavailableReply : verifiedFact; }
    }
}
