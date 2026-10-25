using System;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public static class MoodRecovery
    {
        private const double ProvisionalDefaultHalfLifeSeconds = 4 * 60 * 60;
        private const int ProvisionalVariationPercent = 15;
        private const double MinimumPersonalityMultiplier = 0.70;
        private const double MaximumPersonalityMultiplier = 1.40;
        private const string HashChannel = "mood-recovery-v1";
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static double HalfLifeSeconds(uint worldSeed, string personId)
        {
            if (personId == null)
                throw new ArgumentNullException(nameof(personId));

            string input = worldSeed.ToString(CultureInfo.InvariantCulture) + "\0" + personId + "\0" + HashChannel;
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            ulong hash = FnvOffsetBasis;
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= FnvPrime;
                }
            }

            int variation = (int)(hash % (2 * ProvisionalVariationPercent + 1)) - ProvisionalVariationPercent;
            return ProvisionalDefaultHalfLifeSeconds * (100 + variation) / 100.0;
        }

        /// <summary>
        /// Returns the same seed/person variation as the legacy overload, adjusted by stable saved traits.
        /// The provisional trait multiplier is bounded and never reselected during reads or time projection.
        /// </summary>
        public static double HalfLifeSeconds(uint worldSeed, NpcPersonalityProfile personality)
        {
            var profile = NpcPersonalityProfile.CopyValidated(personality);
            double multiplier = EmotionalStabilityMultiplier(profile.EmotionalStability) *
                SensitivityMultiplier(profile.Sensitivity) * SelfEsteemMultiplier(profile.SelfEsteem) *
                HumorMultiplier(profile.Humor);
            multiplier = Math.Max(MinimumPersonalityMultiplier,
                Math.Min(MaximumPersonalityMultiplier, multiplier));
            return HalfLifeSeconds(worldSeed, profile.PersonId) * multiplier;
        }

        public static int ValueAt(int anchoredValue, double anchoredAt, double now, double halfLifeSeconds)
        {
            if (anchoredValue < -100 || anchoredValue > 100)
                throw new ArgumentOutOfRangeException(nameof(anchoredValue));
            ValidateTime(anchoredAt, nameof(anchoredAt));
            ValidateTime(now, nameof(now));
            if (!IsFinite(halfLifeSeconds) || halfLifeSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(halfLifeSeconds));

            if (anchoredValue == 0)
                return 0;

            double elapsed = Math.Max(0, now - anchoredAt);
            double remaining = anchoredValue * Math.Exp(-Math.Log(2.0) * elapsed / halfLifeSeconds);
            int rounded = (int)Math.Round(remaining, MidpointRounding.AwayFromZero);
            return Math.Max(-100, Math.Min(100, rounded));
        }

        private static void ValidateTime(double value, string parameterName)
        {
            if (!IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double EmotionalStabilityMultiplier(string value)
        {
            switch (value)
            {
                case "resilient": return 0.85;
                case "sensitive": return 1.10;
                case "volatile": return 1.15;
                default: return 1.0;
            }
        }

        private static double SensitivityMultiplier(string value)
        {
            switch (value)
            {
                case "thick-skinned": return 0.90;
                case "highly sensitive": return 1.15;
                default: return 1.0;
            }
        }

        private static double SelfEsteemMultiplier(string value)
        {
            switch (value)
            {
                case "insecure": return 1.10;
                case "confident": return 0.95;
                default: return 1.0;
            }
        }

        private static double HumorMultiplier(string value)
        {
            switch (value)
            {
                case "playful": return 0.95;
                case "wry": return 0.98;
                case "solemn": return 1.05;
                case "mischievous": return 0.98;
                default: return 1.0;
            }
        }
    }
}
