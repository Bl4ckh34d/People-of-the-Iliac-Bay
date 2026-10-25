using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class PopulationSpawnBudget
    {
        private int remaining;

        public PopulationSpawnBudget(int maximumCreates)
        {
            if (maximumCreates < 1) throw new ArgumentOutOfRangeException(nameof(maximumCreates));
            remaining = maximumCreates;
        }

        public int Remaining => remaining;

        public bool TryConsume()
        {
            if (remaining == 0) return false;
            remaining--;
            return true;
        }
    }
}
