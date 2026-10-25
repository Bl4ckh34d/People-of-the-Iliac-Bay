using System;
using System.Collections.Generic;
using System.Linq;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class PopulationTests
    {
        internal static string Signature(SettlementPopulation p) => p.SettlementId + ":" +
            string.Join("|", p.Households.Select(h => h.BuildingKey + ":" + h.SurnameSeed + ":" +
                string.Join(",", h.Residents.Select(r => $"{r.Id}/{r.HomeKey}/{r.AppearanceSeed}/{r.IsAlive}/{r.IsQuestProtected}/{r.AgeGroup}/{r.Sex}/{r.PartnerId}/{r.MotherId}/{r.FatherId}"))));

        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Household identities survive layout enumeration order", () =>
            {
                var a = Population.Create(42, 100, new[] { 20, 10 });
                var b = Population.Create(42, 100, new[] { 10, 20 });
                Check.Equal(Signature(a), Signature(b));
                Check.Equal(10, a.Households[0].BuildingKey);
            }));
            cases.Add(("Every home receives between one and three unique residents", () =>
            {
                var p = Population.Create(11, 1234, Enumerable.Range(1, 300));
                Check.Equal(300, p.Households.Count);
                Check.True(p.Households.All(h => h.Residents.Count >= 1 && h.Residents.Count <= 3));
                var residents = p.Households.SelectMany(h => h.Residents).ToList();
                Check.Equal(residents.Count, residents.Select(r => r.Id).Distinct().Count());
                Check.True(p.Households.All(h => h.Residents.All(r => r.HomeKey == h.BuildingKey)));
            }));
            cases.Add(("Adding an unrelated home does not change existing residents", () =>
            {
                var a = Population.Create(42, 100, new[] { 10 });
                var b = Population.Create(42, 100, new[] { 10, 20 });
                b.Households.RemoveAt(1);
                Check.Equal(Signature(a), Signature(b));
            }));
            cases.Add(("Residents in different settlements never share IDs", () =>
            {
                var a = Population.Create(42, 100, new[] { 10 });
                var b = Population.Create(42, 101, new[] { 10 });
                Check.True(!a.Households[0].Residents.Select(r => r.Id).Intersect(b.Households[0].Residents.Select(r => r.Id)).Any());
            }));
            cases.Add(("World seed changes appearance reproducibly", () =>
            {
                var a = Population.Create(42, 100, new[] { 10 }).Households[0].Residents[0];
                var b = Population.Create(43, 100, new[] { 10 }).Households[0].Residents[0];
                Check.True(a.AppearanceSeed != b.AppearanceSeed);
            }));
            cases.Add(("Duplicate home keys are rejected", () => Check.Throws<ArgumentException>(() => Population.Create(42, 100, new[] { 10, 10 }))));
            cases.Add(("Empty settlements create no residents", () => Check.Equal(0, Population.Create(42, 100, new int[0]).Households.Count)));
            cases.Add(("Invalid settlement and home keys are rejected", () =>
            {
                Check.Throws<ArgumentOutOfRangeException>(() => Population.Create(42, -1, new[] { 10 }));
                Check.Throws<ArgumentOutOfRangeException>(() => Population.Create(42, 100, new[] { -1 }));
            }));
            cases.Add(("Deaths affect only one person and are idempotent", () =>
            {
                var p = Population.Create(42, 100, new[] { 10, 20 });
                var r = p.Households[0].Residents[0];
                Check.True(Population.MarkDead(p, r.Id));
                Check.True(!r.IsAlive);
                Check.True(!Population.MarkDead(p, r.Id));
                Check.Equal(1, p.Households.SelectMany(h => h.Residents).Count(x => !x.IsAlive));
            }));
            cases.Add(("Quest-protected residents cannot be killed", () =>
            {
                var p = Population.Create(42, 100, new[] { 10 });
                var r = p.Households[0].Residents[0];
                r.IsQuestProtected = true;
                Check.True(!Population.MarkDead(p, r.Id));
                Check.True(r.IsAlive);
            }));
            cases.Add(("An unknown death target is rejected", () =>
                Check.Throws<KeyNotFoundException>(() => Population.MarkDead(Population.Create(42, 100, new[] { 10 }), "absent"))));
        }
    }
}
