namespace PeopleOfTheIliacBay.Core
{
    public static class ResidentMovement
    {
        public const double WalkSpeed = 1.3;
        public const double MinimumWalkSpeed = 4.2 / 3.6;
        public const double MaximumWalkSpeed = 6.4 / 3.6;
        public static double WalkSpeedFor(uint appearanceSeed)
        {
            uint seed=StableSeed.Combine(appearanceSeed,0,0,0x77616c6b);
            // Stable personal pace from 4.2 to 6.4 km/h; never resample on scene/load changes.
            double fraction=(seed%10001)/10000d;
            return System.Math.Round(MinimumWalkSpeed+(MaximumWalkSpeed-MinimumWalkSpeed)*fraction,4);
        }
        public static double FleeSpeed(uint appearanceSeed)
        {
            uint seed = StableSeed.Combine(appearanceSeed, 0, 0, 0x666c6565);
            // Keep the physical value stable through DFU's decimal JSON serializer.
            return System.Math.Round(WalkSpeed * (2.5 + (seed % 1001) / 1000.0), 4);
        }
    }
}
