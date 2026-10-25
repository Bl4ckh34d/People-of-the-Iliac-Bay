namespace PeopleOfTheIliacBay.Core
{
    // The policy names possible providers. It does not admit a visit or grant an effect.
    public enum NpcNeedPurpose { Illness = 2, PotionSupply = 3, Banking = 4 }
    public enum NpcHomeAccess { Unknown = 0, Reachable = 1, Unreachable = 2 }
    public enum NpcNeedDestinationKind { None = 0, Home = 1, Inn = 2, Temple = 3, Guild = 4, Bank = 5 }

    public static class NpcNeedDestinationPolicy
    {
        public static NpcNeedDestinationKind SelectBanking(bool hasBank,bool bankOpen,bool homeAccessible)
        {
            if(hasBank)return bankOpen?NpcNeedDestinationKind.Bank:NpcNeedDestinationKind.None;
            return homeAccessible?NpcNeedDestinationKind.Home:NpcNeedDestinationKind.None;
        }

        public static NpcNeedDestinationKind Select(NpcNeedPurpose purpose, NpcHomeAccess homeAccess,
            bool homeFeasible, bool externalFeasible)
        {
            if (!IsSupported(purpose) || homeAccess == NpcHomeAccess.Unknown)
                return NpcNeedDestinationKind.None;
            if (homeAccess == NpcHomeAccess.Reachable)
                return homeFeasible ? NpcNeedDestinationKind.Home : NpcNeedDestinationKind.None;
            // Conflicting access and feasibility evidence cannot authorize an external fallback.
            if (homeAccess != NpcHomeAccess.Unreachable || homeFeasible || !externalFeasible)
                return NpcNeedDestinationKind.None;
            switch (purpose)
            {
                case NpcNeedPurpose.Illness: return NpcNeedDestinationKind.Temple;
                case NpcNeedPurpose.PotionSupply: return NpcNeedDestinationKind.Guild;
                case NpcNeedPurpose.Banking: return NpcNeedDestinationKind.Bank;
                default: return NpcNeedDestinationKind.None;
            }
        }

        private static bool IsSupported(NpcNeedPurpose purpose)
        {
            return purpose == NpcNeedPurpose.Illness ||
                purpose == NpcNeedPurpose.PotionSupply || purpose == NpcNeedPurpose.Banking;
        }

    }
}
