namespace LivingCities.Core
{
    public static class ResidentMovement
    {
        public const double WalkSpeed = 1.3;
        public static double WalkSpeedFor(uint appearanceSeed)
        {
            uint seed=StableSeed.Combine(appearanceSeed,0,0,0x77616c6b);
            // Stable personal pace, +/-15%; never resample on scene/load changes.
            return System.Math.Round(WalkSpeed*(0.85+(seed%3001)/10000d),4);
        }
        public static double FleeSpeed(uint appearanceSeed)
        {
            uint seed = StableSeed.Combine(appearanceSeed, 0, 0, 0x666c6565);
            // Keep the physical value stable through DFU's decimal JSON serializer.
            return System.Math.Round(WalkSpeed * (2.5 + (seed % 1001) / 1000.0), 4);
        }
    }
}
