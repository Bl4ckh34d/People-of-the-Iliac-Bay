using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HauntingContextTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            Action boundaries = () => {
                Check.Equal(DaggerfallHauntingStatus.Active, ConversationLifeContext.HauntingStatus(19 * 60));
                Check.Equal(DaggerfallHauntingStatus.Active, ConversationLifeContext.HauntingStatus(23 * 60 + 59));
                Check.Equal(DaggerfallHauntingStatus.Active, ConversationLifeContext.HauntingStatus(0));
                Check.Equal(DaggerfallHauntingStatus.Active, ConversationLifeContext.HauntingStatus(4 * 60 + 59));
                Check.Equal(DaggerfallHauntingStatus.Inactive, ConversationLifeContext.HauntingStatus(5 * 60));
                Check.Equal(DaggerfallHauntingStatus.Inactive, ConversationLifeContext.HauntingStatus(18 * 60 + 59));

                string active=ConversationLifeContext.Haunting(19 * 60);
                Check.True(active.Contains("currently active") && active.Contains("19:00") && active.Contains("05:00"));
                string inactive=ConversationLifeContext.Haunting(10 * 60 + 9);
                Check.True(inactive.Contains("currently inactive") && inactive.Contains("cannot ground a refusal"));
                Check.True(inactive.Contains("Do not claim the streets are universally safe"));
            };
            cases.Add(("Haunting context follows the current 19:00 to 05:00 restriction window", boundaries));
            Action invalidMinutes = () => {
                Check.Throws<ArgumentOutOfRangeException>(() => ConversationLifeContext.HauntingStatus(-1));
                Check.Throws<ArgumentOutOfRangeException>(() => ConversationLifeContext.Haunting(1440));
            };
            cases.Add(("Haunting context rejects minute values outside the game day", invalidMinutes));
        }
    }
}
