namespace PeopleOfTheIliacBay.Core
{
    public static class NpcPersonalityPolicy
    {
        public static NpcSocialBackground BackgroundForNativeSocialGroup(int group)
        {
            // Native FactionFile.SocialGroups values, not inferred from appearance or occupation.
            switch (group)
            {
                case 0: return NpcSocialBackground.Commoner;
                case 1: return NpcSocialBackground.Merchant;
                case 2: return NpcSocialBackground.Scholar;
                case 3: return NpcSocialBackground.Noble;
                case 4: return NpcSocialBackground.Underworld;
                case 6: return NpcSocialBackground.Supernatural;
                case 7: return NpcSocialBackground.GuildMember;
                default: return NpcSocialBackground.Unknown;
            }
        }

        public static bool CanGenerateRandom(bool protectedResident, bool questResource, bool authoredIndividual)
        // Quest flags protect native quest mechanics; they do not establish an authored personality.
        { return !authoredIndividual; }

        public const string AuthorityInstruction =
            "Story/lore/quest rules override traits. Native social reaction dominates the baseline; personal changes require evidence.";
    }
}
