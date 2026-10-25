namespace PeopleOfTheIliacBay.Core
{
    public enum NpcActionConversationRoute { None, Offer, Single, Decompose }
    public static class NpcActionRouting
    {
        // These are execution-state routes, not language or willingness rules.
        public static NpcActionConversationRoute Choose(string classifiedIntent,bool currentOffer,bool activeCommitment)
        {
            if(currentOffer)return NpcActionConversationRoute.Offer;
            if(classifiedIntent!="action")return NpcActionConversationRoute.None;
            return activeCommitment?NpcActionConversationRoute.Single:NpcActionConversationRoute.Decompose;
        }

        // Called only after the top-level classifier already routed this turn
        // as an action. Decomposition decides arity, not whether that route is
        // valid: NONE must still reach the single-action willingness check.
        public static NpcActionConversationRoute AfterDecomposition(NpcActionDecomposition decomposition)
        {
            if(decomposition==null)return NpcActionConversationRoute.None;
            switch(decomposition.Kind)
            {
                case NpcActionDecompositionKind.None:
                case NpcActionDecompositionKind.Single:
                    return NpcActionConversationRoute.Single;
                case NpcActionDecompositionKind.Chain:
                    return NpcActionConversationRoute.Decompose;
                default:
                    return NpcActionConversationRoute.None;
            }
        }
    }
}
