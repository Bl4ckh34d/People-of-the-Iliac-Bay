namespace PeopleOfTheIliacBay.Core
{
    public enum ConversationHostilityAction { None, Warn, RefuseAndWarn, EndAndReport, EndConversation }

    public static class ConversationHostility
    {
        public static string FirstPersonPlace(string descriptivePlace)
        {
            if (descriptivePlace == "the innkeeper's inn") return "my inn";
            if (descriptivePlace == "the shopkeeper's shop") return "my shop";
            if (descriptivePlace == "the NPC's home") return "my home";
            return "this place";
        }

        public static ConversationHostilityAction Decide(int disposition, bool ownPlace, bool factualRequest)
        {
            // Dislike is not evidence of an offense, even in the speaker's home.
            if (disposition <= -100) return ConversationHostilityAction.EndConversation;
            if (!ownPlace || disposition > -50) return ConversationHostilityAction.None;
            return factualRequest ? ConversationHostilityAction.RefuseAndWarn : ConversationHostilityAction.Warn;
        }
    }
}
