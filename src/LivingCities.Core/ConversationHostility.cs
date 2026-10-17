namespace LivingCities.Core
{
    public enum ConversationHostilityAction { None, Warn, RefuseAndWarn, EndAndReport }

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
            if (disposition <= -100) return ConversationHostilityAction.EndAndReport;
            if (!ownPlace || disposition > -50) return ConversationHostilityAction.None;
            return factualRequest ? ConversationHostilityAction.RefuseAndWarn : ConversationHostilityAction.Warn;
        }
    }
}
