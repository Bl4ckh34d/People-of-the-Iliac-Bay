using System;
using System.Collections;
using DaggerfallWorkshop.Game;

namespace PeopleOfTheIliacBay.DFU
{
    // Controlled native accepted-path probe. Typed extraction is injected;
    // same-model willingness, per-step binding and task effects use production paths.
    internal static class GoalChainLengthProbe
    {
        internal const string Switch = "-peopleoftheiliacbay-goal-chain-length-probe";

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, int stepCount)
        {
            if (stepCount != 3 && stepCount != 4 && stepCount != 5)
                throw new ArgumentOutOfRangeException("stepCount", "Goal chain length probe supports 3, 4, or 5 steps.");
            IEnumerator run = GoalSequenceProbe.Run(host, streets, character, stepCount);
            while (run.MoveNext()) yield return run.Current;
        }
    }
}
