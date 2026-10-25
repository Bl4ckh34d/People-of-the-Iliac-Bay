using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum BystanderPosture { Resume, Watch, Retreat }

    public static class BystanderResponse
    {
        public static BystanderPosture Evaluate(double distance, uint appearanceSeed, BystanderPosture previous)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0 ||
                previous < BystanderPosture.Resume || previous > BystanderPosture.Retreat)
                throw new ArgumentException("Invalid bystander state.");
            // Stable per-person thresholds keep nearby civilians from switching together.
            double variation = (StableSeed.Combine(appearanceSeed, 0, 0, 0x62797374) % 1001) / 1000.0;
            double inner = 9.5 + variation;
            double outer = 19.5 + variation;
            if (previous == BystanderPosture.Retreat)
                return distance <= inner + 1.5 ? BystanderPosture.Retreat :
                    distance <= outer - 1.5 ? BystanderPosture.Watch : BystanderPosture.Resume;
            if (previous == BystanderPosture.Watch)
                return distance < inner - 1.5 ? BystanderPosture.Retreat :
                    distance > outer + 1.5 ? BystanderPosture.Resume : BystanderPosture.Watch;
            if (distance < inner) return BystanderPosture.Retreat;
            if (distance <= outer) return BystanderPosture.Watch;
            return BystanderPosture.Resume;
        }

        public static float RetreatDistance(uint appearanceSeed)
        {
            return 9.5f + (StableSeed.Combine(appearanceSeed, 0, 0, 0x62797374) % 1001) / 1000f + 0.35f;
        }
    }
}
