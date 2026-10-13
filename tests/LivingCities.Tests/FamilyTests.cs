using System;
using System.Collections.Generic;
using System.Linq;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class FamilyTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("New families include two parents and up to three children", () => {
                var town = Population.CreateFamilies(42, 100, Enumerable.Range(1, 1000).Select(k => new HomeProfile(k, 5, 100)));
                Check.True(town.Households.All(h => h.Residents.Count >= 1 && h.Residents.Count <= 5));
                Check.True(town.Households.Any(h => h.Residents.Count == 5));
                Check.True(town.Households.Count(h => h.Residents.Count <= 3) > 700);
                foreach (var home in town.Households)
                {
                    var adults = home.Residents.Where(r => r.AgeGroup == ResidentAgeGroup.Adult).ToList();
                    var children = home.Residents.Where(r => r.AgeGroup == ResidentAgeGroup.Child).ToList();
                    Check.Equal(Math.Min(2, home.Residents.Count), adults.Count);
                    Check.Equal(Math.Max(0, home.Residents.Count - 2), children.Count);
                    if (adults.Count == 1) { Check.True(adults[0].PartnerId == null); continue; }
                    Check.Equal(adults[1].Id, adults[0].PartnerId);
                    Check.Equal(adults[0].Id, adults[1].PartnerId);
                    var father = adults.Single(r => r.Sex == ResidentSex.Male);
                    var mother = adults.Single(r => r.Sex == ResidentSex.Female);
                    foreach (var child in children)
                    { Check.Equal(father.Id, child.FatherId); Check.Equal(mother.Id, child.MotherId); Check.True(child.PartnerId == null); }
                }
            }));
            cases.Add(("One bed permits one resident and larger homes stay within bed capacity", () => {
                var small = Population.CreateFamilies(42, 100, Enumerable.Range(1, 2000).Select(k => new HomeProfile(k, 1, 30)));
                var large = Population.CreateFamilies(42, 100, Enumerable.Range(1, 2000).Select(k => new HomeProfile(k, 6, 300)));
                Check.True(large.Households.Average(h => h.Residents.Count) > small.Households.Average(h => h.Residents.Count) + 0.3);
                Check.True(small.Households.All(h => h.Residents.Count == 1));
                Check.True(large.Households.All(h => h.Residents.Count <= 6));
            }));
            cases.Add(("No beds or unknown beds allocate no residents", () => {
                foreach (int beds in new[] { 0, -1 })
                    Check.True(Population.CreateFamilies(42, 100, Enumerable.Range(1, 50).Select(k => new HomeProfile(k, beds, 500)))
                        .Households.All(h => h.Residents.Count == 0));
            }));
            cases.Add(("Nine beds can house more than five without adding more than three children", () => {
                var homes = Population.CreateFamilies(42, 100, Enumerable.Range(1, 2000).Select(k => new HomeProfile(k, 9, 300))).Households;
                Check.True(homes.Any(h => h.Residents.Count == 9));
                Check.True(homes.All(h => h.Residents.Count <= 9));
                Check.True(homes.All(h => h.Residents.Count(r => r.AgeGroup == ResidentAgeGroup.Child) <= 3));
            }));
            cases.Add(("Family allocation preserves identity order and historical surname convention", () => {
                var profiles = new[] { new HomeProfile(20, 3, 100), new HomeProfile(10, 1, 30) };
                var a = Population.CreateFamilies(42, 100, profiles);
                var b = Population.CreateFamilies(42, 100, profiles.Reverse());
                Check.Equal(PopulationTests.Signature(a), PopulationTests.Signature(b));
                foreach (var home in a.Households)
                    Check.Equal(StableSeed.Combine(42, 100, (uint)home.BuildingKey, 0xfffffffe), home.SurnameSeed);
            }));
            cases.Add(("Actual beds are the only new household capacity limit", () => {
                for (int limit = 1; limit <= 9; limit++)
                {
                    var town = Population.CreateFamilies(7, 100, Enumerable.Range(1, 200).Select(k => new HomeProfile(k, limit)));
                    Check.True(town.Households.All(h => h.Residents.Count <= limit));
                    Check.True(town.Households.All(h => h.Residents.Count >= 1));
                }
                Check.Throws<ArgumentOutOfRangeException>(() => Population.CreateFamilies(1, 1, new[] { new HomeProfile(1, -2) }));
                Check.Throws<ArgumentOutOfRangeException>(() => Population.CreateFamilies(1, 1, new[] { new HomeProfile(1, 1, double.NaN) }));
            }));
        }
    }
}
