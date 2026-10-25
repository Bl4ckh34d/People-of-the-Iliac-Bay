namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Maps verified native social-group evidence, never display names or faction type.</summary>
    public static class NpcNativeRoleProjection
    {
        public static NpcSocialRank SocialRank(bool factionResolved, int nativeSocialGroup, bool verifiedRoyal)
        {
            if (!factionResolved) return NpcSocialRank.Unknown;
            if (nativeSocialGroup == 3)
                return verifiedRoyal ? NpcSocialRank.HighAuthority : NpcSocialRank.Nobility;
            return nativeSocialGroup >= 0 && nativeSocialGroup <= 4
                ? NpcSocialRank.Commoner : NpcSocialRank.Unknown;
        }
    }
}
