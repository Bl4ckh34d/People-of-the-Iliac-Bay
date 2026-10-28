using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Stores finite game-time fear after a native witness observes a local hostile threat.</summary>
    public sealed partial class WorldStateStore
    {
        private const double AcuteFearBaseHalfLifeSeconds = 2 * 60 * 60;
        private const double AcuteFearRefreshIntervalSeconds = 60;
        private static readonly NpcFearRecoveryPolicy AcuteFearRecoveryPolicy =
            NpcFearRecoveryPolicy.Create(AcuteFearBaseHalfLifeSeconds, 0.70, 1.50);

        /// <summary>Records a fresh native observation of non-player danger for a known living NPC.</summary>
        public bool TryObserveNpcAcuteFear(string observerId, string sourceId, NpcFearSeverity severity, double now)
        {
            if (!ValidIdentity(observerId, 128) || !ValidIdentity(sourceId, 512) ||
                !Finite(now) || now < 0 || !Enum.IsDefined(typeof(NpcFearSeverity), severity))
                return false;

            bool alive;
            if (!KnownNpcIdentity(state, observerId, out alive) || !alive) return false;

            int currentIndex = state.NpcAcuteFears.FindIndex(item => item.ObserverId == observerId);
            var current = currentIndex < 0 ? null : state.NpcAcuteFears[currentIndex];
            if (current != null && (now < current.ObservedGameTime || current.Revision == long.MaxValue)) return false;

            var personality = ActiveNpcPersonality(state, observerId);
            double newHalfLife = AcuteFearRecoveryPolicy.HalfLifeSeconds(personality, severity);
            double traitMultiplier = AcuteFearRecoveryPolicy.HalfLifeSeconds(personality, NpcFearSeverity.Moderate) /
                AcuteFearBaseHalfLifeSeconds;
            var role = ReadNpcRole(observerId);
            double rawBaseline = Math.Min(100, SeverityBaseline(severity) * traitMultiplier);
            double baseline = NpcRolePolicy.DangerFear(role, rawBaseline);

            double unroundedCurrent = 0;
            bool residualDominates = false;
            if (current != null)
            {
                unroundedCurrent = current.Intensity * AcuteFearRecoveryPolicy.DecayFraction(
                    current.ObservedGameTime, now, current.RecoveryHalfLifeSeconds);
                residualDominates = unroundedCurrent > baseline;

                // Rate limit by observer, not source. Stronger severity baselines can still take effect now.
                if (now - current.ObservedGameTime < AcuteFearRefreshIntervalSeconds &&
                    baseline <= current.Intensity)
                    return false;
            }

            double intensity = current == null ? baseline : Math.Max(unroundedCurrent, baseline);
            double selectedHalfLife = current != null && residualDominates
                ? Math.Max(current.RecoveryHalfLifeSeconds, newHalfLife)
                : newHalfLife;
            var updated = new NpcAcuteFearState
            {
                ObserverId = observerId,
                SourceId = sourceId,
                Intensity = intensity,
                ObservedGameTime = now,
                RecoveryHalfLifeSeconds = selectedHalfLife,
                Severity = severity,
                Revision = current == null ? 1 : current.Revision + 1
            };
            if (currentIndex < 0) state.NpcAcuteFears.Add(updated);
            else state.NpcAcuteFears[currentIndex] = updated;
            return true;
        }

        /// <summary>Reads a detached game-time projection, or null when no observation exists.</summary>
        public NpcAcuteFearView ReadNpcAcuteFear(string observerId, double now)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            bool alive;
            if (!KnownNpcIdentity(state, observerId, out alive)) return null;
            var saved = state.NpcAcuteFears.Find(item => item.ObserverId == observerId);
            if (saved == null) return null;
            var copy = NpcAcuteFearState.CopyValidated(saved);
            double decay = AcuteFearRecoveryPolicy.DecayFraction(
                copy.ObservedGameTime, now, copy.RecoveryHalfLifeSeconds);
            double currentFear = copy.Intensity * decay;
            if (NpcRolePolicy.IsCombatExperienced(ReadNpcRole(observerId)))
                currentFear = Math.Min(NpcRolePolicy.LowDangerFearMaximum, currentFear);
            return new NpcAcuteFearView(copy, now, currentFear);
        }

        private static List<NpcAcuteFearState> CopyNpcAcuteFears(WorldState source, WorldState validated)
        {
            var result = new List<NpcAcuteFearState>();
            Require(source.NpcAcuteFears != null, "Missing NPC acute-fear ledger.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var saved in source.NpcAcuteFears)
            {
                var record = NpcAcuteFearState.CopyValidated(saved);
                bool alive;
                Require(KnownNpcIdentity(validated, record.ObserverId, out alive),
                    "NPC acute fear references an unknown NPC identity.");
                Require(identities.Add(record.ObserverId), "Duplicate NPC acute-fear observer.");
                result.Add(record);
            }
            return result;
        }

        private static double SeverityBaseline(NpcFearSeverity severity)
        {
            switch (severity)
            {
                case NpcFearSeverity.Mild: return 25;
                case NpcFearSeverity.Moderate: return 45;
                case NpcFearSeverity.Severe: return 65;
                default: return 85;
            }
        }

        private static bool ValidIdentity(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength) return false;
            for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return false;
            return true;
        }

    }
}
