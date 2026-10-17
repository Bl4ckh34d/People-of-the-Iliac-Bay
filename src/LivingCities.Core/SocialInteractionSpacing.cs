namespace LivingCities.Core
{
    public static class SocialInteractionSpacing
    {
        public const double MinimumMeters=1.0;
        public const double MaximumMeters=1.3;
        public static bool Contains(double centerDistance)
        {
            return !double.IsNaN(centerDistance) && !double.IsInfinity(centerDistance) &&
                centerDistance>=MinimumMeters && centerDistance<=MaximumMeters;
        }
    }
}
