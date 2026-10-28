using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

static class Program
{
    static int Main()
    {
        var cases = new (string, Action)[] {
            ("catalog distinguishes actual goods and scenery", () => {
                Equal(MarketGoodsKind.Fish, MarketActivity.Goods(211, 11));
                Equal(MarketGoodsKind.Meat, MarketActivity.Goods(211, 40));
                Equal(MarketGoodsKind.Bread, MarketActivity.Goods(211, 31));
                Equal(MarketGoodsKind.Produce, MarketActivity.Goods(213, 1));
                Equal(MarketGoodsKind.None, MarketActivity.Goods(210, 28));
                Equal(MarketGoodsKind.None, MarketActivity.Goods(182, 37));
                Equal(MarketGoodsKind.None, MarketActivity.Goods(211, 36));
                Equal(true, MarketActivity.Performer(182, 37));
                Equal(true, MarketActivity.Performer(182, 6));
                Equal(false, MarketActivity.Performer(182, 17));
            }),
            ("nearest slot excludes occupied slots and previous table", () => {
                var slots = new List<MarketVisitSlot> {
                    new MarketVisitSlot("occupied", "a", 1, 0),
                    new MarketVisitSlot("previous", "b", 2, 0),
                    new MarketVisitSlot("nearest", "c", 3, 0),
                    new MarketVisitSlot("far", "d", 4, 0) };
                Equal("nearest", MarketActivity.Nearest(slots, 0, 0, "b", s => s.Id != "occupied").Id);
                Equal(null, MarketActivity.Nearest(slots, 0, 0, null, s => false));
            }),
            ("dwell starts after physical arrival and stays finite", () => {
                var stay = new MarketDwell(50);
                Equal(false, stay.Advance(1000, false));
                Equal(false, stay.Advance(2000, true));
                Equal(false, stay.Advance(2049.9, true));
                Equal(true, stay.Advance(2050, true));
                Equal(2000d, stay.ArrivedAt);
            }),
            ("leaving slot does not complete dwell offscreen", () => {
                var stay = new MarketDwell(50);
                stay.Advance(100, true);
                Equal(false, stay.Advance(200, false));
                Equal(false, stay.Advance(210, true));
                Equal(true, stay.Advance(260, true));
            }),
            ("identity and day give stable varied stays", () => {
                var durations = new HashSet<double>();
                for (uint seed = 1; seed <= 100; seed++) {
                    double value = MarketActivity.DwellSeconds(seed, 10, false);
                    Equal(value, MarketActivity.DwellSeconds(seed, 10, false));
                    if (value < 120 || value > 480) throw new Exception("Browse stay outside bounds.");
                    durations.Add(value);
                }
                if (durations.Count < 20) throw new Exception("Crowd uses synchronized stay.");
            })
        };
        int passed = 0;
        foreach (var test in cases) try { test.Item2(); passed++; Console.WriteLine("PASS " + test.Item1); }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + test.Item1 + ": " + error.Message); }
        Console.WriteLine(passed + "/" + cases.Length + " passed");
        return passed == cases.Length ? 0 : 1;
    }
    static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception("Expected " + expected + ", got " + actual); }
}
