namespace LivingCities.Core
{
    public static class ResidentMovement
    {
        public const double WalkSpeed = 1.3;
        public static double FleeSpeed(uint appearanceSeed)
        {
            uint seed = StableSeed.Combine(appearanceSeed, 0, 0, 0x666c6565);
            // Keep the physical value stable through DFU's decimal JSON serializer.
            return System.Math.Round(WalkSpeed * (2.5 + (seed % 1001) / 1000.0), 4);
        }
    }
}
