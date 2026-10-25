using System;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Absolute month indexing for Daggerfall Unity's 30-day calendar.</summary>
    public static class ConversationMemoryNativeMonth
    {
        public const double SecondsPerMonth = 30d * 24d * 60d * 60d;

        public static long IndexAt(double gameSeconds)
        {
            Validate(gameSeconds, nameof(gameSeconds));
            double index = Math.Floor(gameSeconds / SecondsPerMonth);
            if (index >= 9223372036854775808d) throw new ArgumentOutOfRangeException(nameof(gameSeconds));
            return (long)index;
        }

        public static double StartAt(long monthIndex)
        {
            if (monthIndex < 0) throw new ArgumentOutOfRangeException(nameof(monthIndex));
            return monthIndex * SecondsPerMonth;
        }

        private static void Validate(double gameSeconds, string name)
        {
            if (double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds) || gameSeconds < 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
