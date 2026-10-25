using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class PopulationSpawnBudget
    {
        private int remaining;
        private readonly int initial;
        private readonly double maximumMilliseconds;

        public PopulationSpawnBudget(int maximumCreates, double maximumMilliseconds=double.PositiveInfinity)
        {
            if (maximumCreates < 1) throw new ArgumentOutOfRangeException(nameof(maximumCreates));
            if (double.IsNaN(maximumMilliseconds) || maximumMilliseconds<=0)
                throw new ArgumentOutOfRangeException(nameof(maximumMilliseconds));
            remaining = maximumCreates;
            initial = maximumCreates;
            this.maximumMilliseconds=maximumMilliseconds;
        }

        public int Remaining => remaining;

        public bool TryConsume(double elapsedMilliseconds=0)
        {
            if(double.IsNaN(elapsedMilliseconds) || double.IsInfinity(elapsedMilliseconds) || elapsedMilliseconds<0)
                throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
            if (remaining == 0) return false;
            // Always allow the first body: existing actor updates must not starve arrivals.
            if(remaining<initial && elapsedMilliseconds>=maximumMilliseconds)return false;
            remaining--;
            return true;
        }
    }
}
