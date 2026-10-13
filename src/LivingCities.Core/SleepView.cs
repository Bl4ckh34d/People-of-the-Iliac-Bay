using System;

namespace LivingCities.Core
{
    public struct SleepView
    {
        public int Index;
        public bool Mirror;
        public static SleepView Select(double head, double side)
        {
            if (double.IsNaN(head) || double.IsInfinity(head) || double.IsNaN(side) || double.IsInfinity(side))
                throw new ArgumentException("Invalid view direction.");
            if (head == 0 && side == 0) return new SleepView { Index = 2 };
            double angle = Math.Atan2(Math.Abs(side), -head);
            return new SleepView { Index = Math.Min(4, (int)Math.Floor(angle * 4 / Math.PI + 0.5)), Mirror = side > 0 };
        }
    }
}
