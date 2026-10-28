using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class GuardShifts
    {
        public static bool WallTrespassEnforced(double now,bool hauntingShelter)
        { Validate(now); return true; }
        public static bool HauntingShelterRequired(int settlement,bool liftedCurse,double now)
        { return settlement==1291010263 && !liftedCurse && At(now)==GuardShift.Night; }
        // Native DaggerfallDateTime uses 06:00 dawn and 18:00 dusk.
        public static GuardShift At(double now)
        {
            Validate(now);
            double hour = now % 86400 / 3600;
            return hour >= 6 && hour < 18 ? GuardShift.Day : GuardShift.Night;
        }
        public static bool OnDuty(GuardRecord guard, double now)
        { return guard != null && guard.IsAlive && guard.Shift == At(now); }
        public static double NextChange(double now)
        {
            Validate(now);
            double start = Math.Floor(now / 86400) * 86400;
            if (now < start + 21600) return start + 21600;
            if (now < start + 64800) return start + 64800;
            return start + 108000;
        }
        public static int PatrolPairTarget(int ordinaryStaff,int maximumPairs)
        {
            if (ordinaryStaff < 0 || maximumPairs < 0 || maximumPairs > 16)
                throw new ArgumentException("Invalid guard patrol staffing.");
            if (ordinaryStaff < 3) return 0;
            return Math.Min(maximumPairs,Math.Max(1,ordinaryStaff/6));
        }
        private static void Validate(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentException("Invalid guard shift time.");
        }
    }
    public sealed partial class GuardRoster
    {
        public void AssignShifts()
        {
            int day = 0, night = 0;
            foreach (var guard in Guards)
                if (guard.IsAlive) { if (guard.Shift == GuardShift.Day) day++; else if (guard.Shift == GuardShift.Night) night++; }
            foreach (var guard in Guards)
            {
                if (guard.Shift != GuardShift.Unassigned) continue;
                guard.Shift = day <= night ? GuardShift.Day : GuardShift.Night;
                if (guard.IsAlive) { if (guard.Shift == GuardShift.Day) day++; else night++; }
            }
        }
    }
}
