using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class PopulationSpawnBudgetTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Morning population activation is spread across frames", (Action)(() =>
            {
                var budget = new PopulationSpawnBudget(4);
                Check.True(budget.TryConsume());
                Check.True(budget.TryConsume());
                Check.True(budget.TryConsume());
                Check.True(budget.TryConsume());
                Check.True(!budget.TryConsume());
                Check.Equal(0, budget.Remaining);
            })));

            cases.Add(("Population spawn budget rejects invalid limits", (Action)(() =>
                Check.Throws<ArgumentOutOfRangeException>(() => new PopulationSpawnBudget(0)))));
        }
    }
}
