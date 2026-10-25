using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Atomic save-owned capture for a native poison-effect assignment. Does not alter social state.</summary>
    public sealed partial class WorldStateStore
    {
        public bool TryCaptureNpcPoisonOnset(object expectedVitalsEpoch, string observerId,
            string nativeEffectKey, double observedGameTime, out NpcExperienceEvent evidence)
        {
            // Compatibility signature: caller-supplied poison claims have no assignment authority.
            evidence = null;
            return false;
        }
        private bool EligibleResident(string observerId)
        {
            bool alive;
            if (string.IsNullOrWhiteSpace(observerId) || !KnownNpcIdentity(state, observerId, out alive) || !alive ||
                state.Settlements == null) return false;
            foreach (var settlement in state.Settlements)
            {
                if (settlement == null || settlement.Households == null) continue;
                foreach (var household in settlement.Households)
                {
                    if (household == null || household.Residents == null) continue;
                    foreach (var resident in household.Residents)
                    {
                        if (resident != null && string.Equals(resident.Id, observerId, StringComparison.Ordinal))
                            return resident.IsAlive && !resident.IsQuestProtected &&
                                resident.AgeGroup == ResidentAgeGroup.Adult;
                    }
                }
            }
            return false;
        }

        private static string StableObserverToken(string observerId)
        {
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(observerId));
                var result = new StringBuilder(32);
                for (int i = 0; i < 16; i++) result.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static bool FiniteGameTime(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
