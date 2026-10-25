namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Directed resentment recovery using the shared fear calibration with a slower base rate.</summary>
    public sealed class NpcResentmentRecoveryPolicy
    {
        public const double ProvisionalDefaultHalfLifeSeconds = 14 * 24 * 60 * 60;
        public const double MinimumHalfLifeSeconds = NpcFearRecoveryPolicy.MinimumHalfLifeSeconds;
        public const double MaximumHalfLifeSeconds = NpcFearRecoveryPolicy.MaximumHalfLifeSeconds;

        private readonly NpcFearRecoveryPolicy calibration;

        private NpcResentmentRecoveryPolicy(NpcFearRecoveryPolicy calibration)
        {
            this.calibration = calibration;
        }

        public static NpcResentmentRecoveryPolicy ProvisionalDefault
        {
            get { return Create(ProvisionalDefaultHalfLifeSeconds, 0.70, 1.50); }
        }

        public static NpcResentmentRecoveryPolicy Create(double baseHalfLifeSeconds,
            double minimumMultiplier, double maximumMultiplier)
        {
            return new NpcResentmentRecoveryPolicy(NpcFearRecoveryPolicy.Create(baseHalfLifeSeconds,
                minimumMultiplier, maximumMultiplier));
        }

        public double HalfLifeSeconds(NpcPersonalityProfile personality, NpcFearSeverity severity)
        { return calibration.HalfLifeSeconds(personality, severity); }

        public int Project(int value, double anchorGameTime, double now, double halfLifeSeconds)
        { return calibration.Project(value, anchorGameTime, now, halfLifeSeconds); }

        public int Project(double value, double anchorGameTime, double now, double halfLifeSeconds)
        { return calibration.Project(value, anchorGameTime, now, halfLifeSeconds); }

        public double DecayFraction(double anchorGameTime, double now, double halfLifeSeconds)
        { return calibration.DecayFraction(anchorGameTime, now, halfLifeSeconds); }
    }
}
