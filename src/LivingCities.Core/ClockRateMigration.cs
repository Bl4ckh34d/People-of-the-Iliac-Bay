using System;

namespace LivingCities.Core
{
    public struct ClockInterval
    {
        public double Start;
        public double End;
        public double Now;

        public ClockInterval(double start, double end, double now)
        { Start = start; End = end; Now = now; }
    }

    public static class ClockRateMigration
    {
        public static ClockInterval RescaleInterval(double start, double end, double now,
            double previousRate, double currentRate)
        {
            if (!Finite(start) || !Finite(end) || !Finite(now) || !Finite(previousRate) || !Finite(currentRate) ||
                start < 0 || end < start || now < 0 || previousRate <= 0 || currentRate <= 0)
                throw new ArgumentException("Invalid clock migration interval or rate.");

            if (previousRate == currentRate || now >= end)
                return new ClockInterval(start, end, now);

            double ratio = currentRate / previousRate;
            if (now <= start)
                return new ClockInterval(start, start + (end - start) * ratio, now);
            return new ClockInterval(now - (now - start) * ratio,
                now + (end - now) * ratio, now);
        }

        public static double RescaleDuration(double duration, double previousRate, double currentRate)
        {
            if (!Finite(duration) || duration < 0 || !Finite(previousRate) || previousRate <= 0 ||
                !Finite(currentRate) || currentRate <= 0)
                throw new ArgumentException("Invalid clock migration duration or rate.");
            return duration * currentRate / previousRate;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
