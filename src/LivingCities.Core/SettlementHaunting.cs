using System;

namespace LivingCities.Core
{
    public static class SettlementHaunting
    {
        public static bool StreetsDangerous(int settlementId, bool liftedCurse, double gameSeconds)
        {
            if (double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds) || gameSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(gameSeconds));
            // Original S0000977 uses LiftedCurse and a 05:00–19:00 daytime task.
            if (settlementId != 1291010263 || liftedCurse) return false;
            double daySeconds = gameSeconds % 86400;
            return daySeconds < 5*3600 || daySeconds >= 19*3600;
        }
    }
}
