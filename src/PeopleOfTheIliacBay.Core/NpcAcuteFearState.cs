using System;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Mutable save DTO for one NPC's most recent observed non-player danger.</summary>
    [Serializable]
    public sealed class NpcAcuteFearState
    {
        public string ObserverId;
        public string SourceId;
        public double Intensity;
        public double ObservedGameTime;
        public double RecoveryHalfLifeSeconds;
        public NpcFearSeverity Severity;
        public long Revision;

        internal static NpcAcuteFearState CopyValidated(NpcAcuteFearState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(source.ObserverId) || source.ObserverId.Length > 128 ||
                HasControl(source.ObserverId))
                throw new ArgumentException("Acute fear requires a valid observer identity.", nameof(source));
            if (string.IsNullOrWhiteSpace(source.SourceId) || source.SourceId.Length > 512 ||
                HasControl(source.SourceId))
                throw new ArgumentException("Acute fear requires a valid source identity.", nameof(source));
            if (!Finite(source.Intensity) || source.Intensity < 0 || source.Intensity > 100)
                throw new ArgumentException("Acute fear intensity must be finite and between zero and one hundred.", nameof(source));
            if (!Finite(source.ObservedGameTime) || source.ObservedGameTime < 0)
                throw new ArgumentException("Acute fear observation time must be finite and nonnegative.", nameof(source));
            if (!Finite(source.RecoveryHalfLifeSeconds) ||
                source.RecoveryHalfLifeSeconds < NpcFearRecoveryPolicy.MinimumHalfLifeSeconds ||
                source.RecoveryHalfLifeSeconds > NpcFearRecoveryPolicy.MaximumHalfLifeSeconds)
                throw new ArgumentException("Acute fear recovery half-life is outside supported bounds.", nameof(source));
            if (!Enum.IsDefined(typeof(NpcFearSeverity), source.Severity))
                throw new ArgumentException("Acute fear severity is invalid.", nameof(source));
            if (source.Revision < 1)
                throw new ArgumentException("Acute fear revision must be positive.", nameof(source));

            return new NpcAcuteFearState { ObserverId = source.ObserverId, SourceId = source.SourceId,
                Intensity = source.Intensity, ObservedGameTime = source.ObservedGameTime,
                RecoveryHalfLifeSeconds = source.RecoveryHalfLifeSeconds, Severity = source.Severity,
                Revision = source.Revision };
        }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static bool HasControl(string value)
        {
            for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return true;
            return false;
        }
    }

    /// <summary>Detached read-only projection of recent fear from a witnessed local threat.</summary>
    public sealed class NpcAcuteFearView
    {
        public string ObserverId { get; private set; }
        public string SourceId { get; private set; }
        public double Intensity { get; private set; }
        public double ObservedGameTime { get; private set; }
        public double RecoveryHalfLifeSeconds { get; private set; }
        public NpcFearSeverity Severity { get; private set; }
        public long Revision { get; private set; }
        public double CurrentFear { get; private set; }
        public double ProjectedGameTime { get; private set; }

        internal NpcAcuteFearView(NpcAcuteFearState state, double projectedGameTime, double currentFear)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            ObserverId = state.ObserverId;
            SourceId = state.SourceId;
            Intensity = state.Intensity;
            ObservedGameTime = state.ObservedGameTime;
            RecoveryHalfLifeSeconds = state.RecoveryHalfLifeSeconds;
            Severity = state.Severity;
            Revision = state.Revision;
            CurrentFear = currentFear;
            ProjectedGameTime = projectedGameTime;
        }
    }
}
