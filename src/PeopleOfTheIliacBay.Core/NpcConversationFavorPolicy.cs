using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcConversationFavorPolicy
    {
        public const int MinimumSpontaneousTravelDisposition = 60;
        public const int MinimumSpontaneousTravelTrust = 60;
        public const int MaximumSpontaneousTravelResentment = 25;
        public const int MaximumSpontaneousTravelFear = 25;

        public static bool MaySpontaneouslyOfferTravel(bool familiarityKnown, bool familiar,
            int disposition, bool trustKnown, int trust, int resentment, int fear,
            bool hasHigherPriority)
        {
            if (disposition < -100 || disposition > 100 || trust < 0 || trust > 100 ||
                resentment < 0 || resentment > 100 || fear < 0 || fear > 100)
                throw new ArgumentOutOfRangeException();
            return familiarityKnown && familiar && disposition >= MinimumSpontaneousTravelDisposition &&
                trustKnown && trust >= MinimumSpontaneousTravelTrust &&
                resentment <= MaximumSpontaneousTravelResentment &&
                fear <= MaximumSpontaneousTravelFear && !hasHigherPriority;
        }

        public static bool HasHigherPriority(NpcMotivationContextSnapshot context)
        {
            if (context == null) return false;
            foreach (var goal in context.Goals)
                if (goal != null && goal.KnowledgeState == NpcMotivationKnowledgeState.Known &&
                    goal.Status == NpcMotivationGoalStatus.Active &&
                    goal.Priority >= NpcMotivationPriorityLevel.Important)
                    return true;
            foreach (var threat in context.Threats)
                if (threat != null && threat.KnowledgeState == NpcMotivationKnowledgeState.Known &&
                    threat.Severity.HasValue && threat.Severity.Value >= 40)
                    return true;
            return false;
        }
    }
}
