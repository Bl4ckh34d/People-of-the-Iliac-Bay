using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcFearSeverity
    {
        Mild = 1,
        Moderate = 2,
        Severe = 3,
        Grave = 4
    }

    /// <summary>Deterministic, bounded calibration for player-directed fear recovery.</summary>
    public sealed class NpcFearRecoveryPolicy
    {
        public const double ProvisionalDefaultHalfLifeSeconds = 7 * 24 * 60 * 60;
        public const double MinimumHalfLifeSeconds = 1;
        public const double MaximumHalfLifeSeconds = 365 * 24 * 60 * 60;

        private readonly double baseHalfLifeSeconds;
        private readonly double minimumMultiplier;
        private readonly double maximumMultiplier;

        private NpcFearRecoveryPolicy(double baseHalfLife, double minimum, double maximum)
        {
            baseHalfLifeSeconds = baseHalfLife;
            minimumMultiplier = minimum;
            maximumMultiplier = maximum;
        }

        public static NpcFearRecoveryPolicy ProvisionalDefault
        {
            get { return new NpcFearRecoveryPolicy(ProvisionalDefaultHalfLifeSeconds, 0.70, 1.50); }
        }

        public static NpcFearRecoveryPolicy Create(double baseHalfLifeSeconds,
            double minimumMultiplier, double maximumMultiplier)
        {
            if (!Finite(baseHalfLifeSeconds) || baseHalfLifeSeconds < MinimumHalfLifeSeconds ||
                baseHalfLifeSeconds > MaximumHalfLifeSeconds || !Finite(minimumMultiplier) ||
                !Finite(maximumMultiplier) || minimumMultiplier <= 0 || maximumMultiplier < minimumMultiplier ||
                baseHalfLifeSeconds * maximumMultiplier > MaximumHalfLifeSeconds)
                throw new ArgumentOutOfRangeException(nameof(baseHalfLifeSeconds));
            return new NpcFearRecoveryPolicy(baseHalfLifeSeconds, minimumMultiplier, maximumMultiplier);
        }

        public double HalfLifeSeconds(NpcPersonalityProfile personality, NpcFearSeverity severity)
        {
            if (!Enum.IsDefined(typeof(NpcFearSeverity), severity))
                throw new ArgumentOutOfRangeException(nameof(severity));

            double severityMultiplier;
            switch (severity)
            {
                case NpcFearSeverity.Mild: severityMultiplier = 0.85; break;
                case NpcFearSeverity.Moderate: severityMultiplier = 1.0; break;
                case NpcFearSeverity.Severe: severityMultiplier = 1.15; break;
                default: severityMultiplier = 1.30; break;
            }

            double personalityMultiplier = personality == null ? 1.0 : PersonalityMultiplier(personality);
            double multiplier = Math.Max(minimumMultiplier,
                Math.Min(maximumMultiplier, severityMultiplier * personalityMultiplier));
            return Math.Max(MinimumHalfLifeSeconds,
                Math.Min(MaximumHalfLifeSeconds, baseHalfLifeSeconds * multiplier));
        }

        public int Project(int value, double anchorGameTime, double now, double halfLifeSeconds)
        {
            if (value < 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value));
            return Project((double)value, anchorGameTime, now, halfLifeSeconds);
        }

        public int Project(double value, double anchorGameTime, double now, double halfLifeSeconds)
        {
            if (!Finite(value) || value < 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value));
            ValidateTime(anchorGameTime, nameof(anchorGameTime));
            ValidateTime(now, nameof(now));
            if (!Finite(halfLifeSeconds) || halfLifeSeconds < MinimumHalfLifeSeconds ||
                halfLifeSeconds > MaximumHalfLifeSeconds)
                throw new ArgumentOutOfRangeException(nameof(halfLifeSeconds));
            return (int)Math.Round(value * DecayFraction(anchorGameTime, now, halfLifeSeconds),
                MidpointRounding.AwayFromZero);
        }

        public double DecayFraction(double anchorGameTime, double now, double halfLifeSeconds)
        {
            ValidateTime(anchorGameTime, nameof(anchorGameTime));
            ValidateTime(now, nameof(now));
            if (now < anchorGameTime) throw new ArgumentOutOfRangeException(nameof(now));
            if (!Finite(halfLifeSeconds) || halfLifeSeconds < MinimumHalfLifeSeconds ||
                halfLifeSeconds > MaximumHalfLifeSeconds)
                throw new ArgumentOutOfRangeException(nameof(halfLifeSeconds));
            return Math.Exp(-Math.Log(2.0) * (now - anchorGameTime) / halfLifeSeconds);
        }

        private static double PersonalityMultiplier(NpcPersonalityProfile source)
        {
            NpcPersonalityProfile profile = NpcPersonalityProfile.CopyValidated(source);
            double multiplier = EmotionalStabilityMultiplier(profile.EmotionalStability) *
                SensitivityMultiplier(profile.Sensitivity) * SelfEsteemMultiplier(profile.SelfEsteem) *
                HumorMultiplier(profile.Humor);
            return Math.Max(0.70, Math.Min(1.50, multiplier));
        }

        private static double EmotionalStabilityMultiplier(string value)
        {
            switch (value)
            {
                case "resilient": return 0.90;
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
                case "insecure": return 1.05;
                case "confident": return 0.95;
                default: return 1.0;
            }
        }

        private static double HumorMultiplier(string value)
        {
            switch (value)
            {
                case "playful": return 0.97;
                case "wry":
                case "mischievous": return 0.99;
                case "solemn": return 1.03;
                default: return 1.0;
            }
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static void ValidateTime(double value, string parameterName)
        {
            if (!Finite(value) || value < 0) throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
