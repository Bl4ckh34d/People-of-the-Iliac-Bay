using System;

namespace LivingCities.Core
{
    public static class SettlementHaunting
    {
        public static double? FirstSafeTime(int settlementId,bool liftedCurse,double since,double now)
        {
            // Validate both timestamps even for an unaffected settlement.
            bool startedDangerous=StreetsDangerous(settlementId,false,since);
            StreetsDangerous(settlementId,false,now);
            if(now<since) throw new ArgumentOutOfRangeException(nameof(now));
            if(!startedDangerous) return since;
            double dawn=Math.Floor(since/86400)*86400+5*3600;
            if(dawn<=since) dawn+=86400;
            if(now>=dawn) return dawn;
            // Native global variables expose no historical completion timestamp.
            // Do not invent an earlier quest resolution while the town was unloaded.
            return liftedCurse ? (double?)now : null;
        }
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
