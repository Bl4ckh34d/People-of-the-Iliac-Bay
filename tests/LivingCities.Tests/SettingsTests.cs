using System;
using System.Collections.Generic;
using System.Linq;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class SettingsTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Family setting permits five-person households", () => {
                var options = new RuntimeOptions(true, 5, 6, 22, 1, true);
                var town = Population.CreateFamilies(42, 100, Enumerable.Range(1, 200).Select(k => new HomeProfile(k, 3, 200)), options.HouseholdLimit);
                Check.True(town.Households.Any(h => h.Residents.Count == 5));
            }));
            cases.Add(("Household setting caps new population", () => {
                var homes = Enumerable.Range(1, 200).ToArray();
                Check.True(Population.Create(42, 100, homes, 1).Households.All(h => h.Residents.Count == 1));
                var pairs = Population.Create(42, 100, homes, 2);
                Check.True(pairs.Households.All(h => h.Residents.Count >= 1 && h.Residents.Count <= 2));
                Check.True(pairs.Households.Any(h => h.Residents.Count == 2));
            }));
            cases.Add(("Capacity changes do not remove or revive saved residents", () => {
                var store = new WorldStateStore(42); var homes = Enumerable.Range(1, 100).ToArray();
                var town = store.GetOrCreate(100, homes);
                Population.MarkDead(town, town.Households[0].Residents[0].Id);
                string before = PopulationTests.Signature(town);
                Check.Equal(before, PopulationTests.Signature(store.GetOrCreate(100, homes, 1)));
                Check.True(store.GetOrCreate(200, homes, 1).Households.All(h => h.Residents.Count == 1));
            }));
            cases.Add(("Direct population requests cannot exceed household bounds", () => {
                Check.Throws<ArgumentOutOfRangeException>(() => Population.Create(42, 100, new[] { 1 }, 0));
                Check.Throws<ArgumentOutOfRangeException>(() => Population.Create(42, 100, new[] { 1 }, 4));
            }));
            cases.Add(("Edited configuration is normalized into safe schedule bounds", () => {
                var lower = new RuntimeOptions(false, -2, -900, -900, 0, false);
                var upper = new RuntimeOptions(true, 99, 999, 999, 999, true);
                Check.Equal(1, lower.HouseholdLimit); Check.Equal(5, upper.HouseholdLimit);
                Check.Equal(1, lower.RefreshSeconds); Check.Equal(10, upper.RefreshSeconds);
                Check.True(!lower.Enabled && !lower.DiagnosticsEnabled && upper.Enabled && upper.DiagnosticsEnabled);
                var lowPlan = DailySchedule.HomeDay(1, lower.WakeHour * 60, lower.SleepHour * 60);
                var highPlan = DailySchedule.HomeDay(1, upper.WakeHour * 60, upper.SleepHour * 60);
                Check.Equal(Activity.Home, lowPlan.Evaluate(720).Activity);
                Check.Equal(Activity.Sleep, highPlan.Evaluate(0).Activity);
            }));
            cases.Add(("Normal settings preserve requested values", () => {
                var options = new RuntimeOptions(true, 2, 7, 21, 4, true);
                Check.Equal(2, options.HouseholdLimit); Check.Equal(7, options.WakeHour);
                Check.Equal(21, options.SleepHour); Check.Equal(4, options.RefreshSeconds);
            }));
        }
    }
}
