using System;

namespace LivingCities.Core
{
    public sealed class RuntimeOptions
    {
        public readonly bool Enabled, DiagnosticsEnabled;
        public readonly int HouseholdLimit, WakeHour, SleepHour, RefreshSeconds;
        public RuntimeOptions(bool enabled, int householdLimit, int wakeHour, int sleepHour, int refreshSeconds, bool diagnosticsEnabled)
        {
            Enabled = enabled; DiagnosticsEnabled = diagnosticsEnabled;
            HouseholdLimit = Clamp(householdLimit, 1, 3);
            WakeHour = Clamp(wakeHour, 4, 10);
            SleepHour = Clamp(sleepHour, 18, 23);
            RefreshSeconds = Clamp(refreshSeconds, 1, 10);
        }
        private static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
    }
}
