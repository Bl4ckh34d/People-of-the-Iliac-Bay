namespace LivingCities.Core
{
    // Fixed integer mixing: never depends on runtime string hashes or global RNG state.
    public static class StableSeed
    {
        public static uint Combine(uint seed, uint settlement, uint building, uint slot)
        {
            unchecked
            {
                uint value = Mix(seed ^ 0x9e3779b9u);
                value = Mix(value ^ settlement);
                value = Mix(value ^ building);
                return Mix(value ^ slot);
            }
        }

        private static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }
    }
}
