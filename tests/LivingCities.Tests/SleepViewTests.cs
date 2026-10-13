using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class SleepViewTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Sleeping view follows bed axes from foot to head", () => {
                var head = new[] { -1.0, -1.0, 0.0, 1.0, 1.0 };
                var side = new[] { 0.0, -1.0, -1.0, -1.0, 0.0 };
                for (int i = 0; i < 5; i++) Check.Equal(i, SleepView.Select(head[i], side[i]).Index);
            }));
            cases.Add(("Opposite bed sides mirror while preserving head and foot view", () => {
                Check.Equal(1, SleepView.Select(-1, 1).Index);
                Check.True(SleepView.Select(-1, 1).Mirror);
                Check.True(!SleepView.Select(-1, -1).Mirror);
                Check.Equal(3, SleepView.Select(10, 10).Index);
                Check.Equal(3, SleepView.Select(0.1, 0.1).Index);
            }));
            cases.Add(("Overhead camera has stable fallback and invalid directions are rejected", () => {
                Check.Equal(2, SleepView.Select(0, 0).Index);
                Check.Throws<ArgumentException>(() => SleepView.Select(double.NaN, 1));
                Check.Throws<ArgumentException>(() => SleepView.Select(1, double.PositiveInfinity));
            }));
        }
    }
}
