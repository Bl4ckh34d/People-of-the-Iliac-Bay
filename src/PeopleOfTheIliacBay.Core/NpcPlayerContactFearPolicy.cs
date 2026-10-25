using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcPlayerContactFearPolicy
    {
        public static int Project(int currentDirectedFear, bool verifiedContact)
        {
            if (currentDirectedFear < 0 || currentDirectedFear > 100)
                throw new ArgumentOutOfRangeException(nameof(currentDirectedFear));
            if (!verifiedContact || currentDirectedFear == 0) return currentDirectedFear;
            return Math.Min(100, (int)Math.Round(currentDirectedFear * 1.25,
                MidpointRounding.AwayFromZero));
        }
    }
}
