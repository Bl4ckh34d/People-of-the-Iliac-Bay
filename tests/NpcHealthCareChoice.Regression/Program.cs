using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("two-thirds health saves owned and purchased potions for later", SufficientHealthSavesPotions),
            ("ordinary injury favors safe free rest over spending a known potion", OrdinaryRestPreference),
            ("inn cost and affordable budget change the ranked choice", RestCostChangesChoice),
            ("critical health favors immediate effective treatment over hours of rest", CriticalTreatmentIsFast),
            ("unsafe inaccessible unknown ineffective and overbudget candidates are excluded", InfeasibleCandidatesAreExcluded),
            ("time budget filters slow candidates and no feasible candidate returns null", TimeBudget),
            ("equal scores use stable ID order and invalid numeric facts reject", DeterministicAndValidated),
            ("casting requires actual affordable magicka", SpellRequiresMagicka),
            ("ordinary care prefers free rest while critical care can cast immediately", SpellVersusRest),
            ("spell cost is not payable in gold or attachable to another treatment", SpellCostValidation),
            ("full health never spends magicka on healing", FullHealthDoesNotCast)
        };

        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void SufficientHealthSavesPotions()
    {
        var potion = Candidate("owned", NpcHealthCareCandidateKind.OwnedHealingItem, 0, 0, 20);
        var bought = Candidate("bought", NpcHealthCareCandidateKind.BuyHealingItem, 1, 1, 20);
        var bed = Candidate("home", NpcHealthCareCandidateKind.Rest, 0, 3600, 100);
        var ranked = NpcHealthCareChoice.Rank(Context(67, 100, 100), new[] { potion, bought, bed });
        Equal(1, ranked.Count);
        Equal("home", ranked[0].Candidate.StableId);
        Equal(null, NpcHealthCareChoice.Select(Context(67, 100, 100), new[] { potion, bought }));
        True(NpcHealthCareChoice.Select(Context(50, 100, 100), new[] { potion }) != null,
            "half health still admits the next potion");
        True(NpcHealthCareChoice.Select(Context(66, 100, 100), new[] { potion }) != null,
            "rounding cannot stop below two thirds");
    }

    private static void OrdinaryRestPreference()
    {
        var context = Context(80, 100, 50);
        var ranked = NpcHealthCareChoice.Rank(context, new[]
        {
            Candidate("potion:501", NpcHealthCareCandidateKind.OwnedHealingItem, 0, 0, 10),
            Candidate("home-bed:1", NpcHealthCareCandidateKind.Rest, 0, 3600, 20)
        });
        Equal("home-bed:1", ranked[0].Candidate.StableId);
        True(!ranked[0].IsUrgent, "ordinary injury should use ordinary preference weights");
        Equal(20, ranked[0].EffectiveHealthGain);
    }

    private static void RestCostChangesChoice()
    {
        var context = Context(60, 100, 300);
        var ranked = NpcHealthCareChoice.Rank(context, new[]
        {
            Candidate("potion:501", NpcHealthCareCandidateKind.OwnedHealingItem, 0, 0, 10),
            Candidate("inn-bed:1", NpcHealthCareCandidateKind.Rest, 260, 3600, 10)
        });
        Equal("potion:501", ranked[0].Candidate.StableId);
        context.AvailableGold = 259;
        ranked = NpcHealthCareChoice.Rank(context, new[]
        {
            Candidate("inn-bed:1", NpcHealthCareCandidateKind.Rest, 260, 3600, 20)
        });
        Equal(0, ranked.Count);
    }

    private static void CriticalTreatmentIsFast()
    {
        var context = Context(10, 100, 100);
        context.UrgentThreat = true;
        var ranked = NpcHealthCareChoice.Rank(context, new[]
        {
            Candidate("own-bed:1", NpcHealthCareCandidateKind.Rest, 0, 3600, 90),
            Candidate("potion:501", NpcHealthCareCandidateKind.OwnedHealingItem, 0, 0, 35),
            Candidate("buy:shop:1", NpcHealthCareCandidateKind.BuyHealingItem, 20, 30, 35)
        });
        Equal("potion:501", ranked[0].Candidate.StableId);
        True(ranked[0].IsUrgent, "urgent threat should activate urgent scoring");
    }

    private static void InfeasibleCandidatesAreExcluded()
    {
        var ranked = NpcHealthCareChoice.Rank(Context(80, 100, 10), new[]
        {
            Candidate("unsafe", NpcHealthCareCandidateKind.Rest, 0, 60, 20, safe: false),
            Candidate("inaccessible", NpcHealthCareCandidateKind.Rest, 0, 60, 20, accessible: false),
            Candidate("unknown", NpcHealthCareCandidateKind.BuyHealingItem, 1, 60, 20, supplyKnown: false),
            Candidate("ineffective", NpcHealthCareCandidateKind.OwnedHealingItem, 0, 0, 0),
            Candidate("unaffordable", NpcHealthCareCandidateKind.BuyHealingItem, 11, 0, 20)
        });
        Equal(0, ranked.Count);
    }

    private static void TimeBudget()
    {
        var context = Context(60, 100, 50);
        context.HasTimeBudget = true;
        context.TimeBudgetSeconds = 30;
        var ranked = NpcHealthCareChoice.Rank(context, new[]
        {
            Candidate("bed", NpcHealthCareCandidateKind.Rest, 0, 3600, 20),
            Candidate("owned", NpcHealthCareCandidateKind.OwnedHealingItem, 0, 10, 5)
        });
        Equal(1, ranked.Count);
        var selected = NpcHealthCareChoice.Select(context, new[]
        {
            Candidate("bed", NpcHealthCareCandidateKind.Rest, 0, 3600, 20),
            Candidate("owned", NpcHealthCareCandidateKind.OwnedHealingItem, 0, 10, 5)
        });
        Equal("owned", selected.Candidate.StableId);
    }

    private static void DeterministicAndValidated()
    {
        var policy = new NpcHealthCareChoicePolicy
        {
            OrdinaryRestPreference = 1,
            OrdinaryOwnedItemPreference = 1,
            OrdinaryBuyItemPreference = 1,
            OrdinaryTimePenaltyPerGameHour = 1,
            HealthBenefitWeight = 1,
            GoldPenaltyPerGold = 1
        };
        var ranked = NpcHealthCareChoice.Rank(Context(60, 100, 10), new[]
        {
            Candidate("z-item", NpcHealthCareCandidateKind.OwnedHealingItem, 0, 0, 5),
            Candidate("a-item", NpcHealthCareCandidateKind.OwnedHealingItem, 0, 0, 5)
        }, policy);
        Equal("a-item", ranked[0].Candidate.StableId);
        Equal("z-item", ranked[1].Candidate.StableId);
        var invalid = Context(90, 100, 10);
        invalid.TimeBudgetSeconds = double.NaN;
        invalid.HasTimeBudget = true;
        Throws<ArgumentException>(() => NpcHealthCareChoice.Rank(invalid, new[]
        {
            Candidate("item", NpcHealthCareCandidateKind.OwnedHealingItem, 0, 0, 5)
        }));
    }

    private static void SpellRequiresMagicka()
    {
        var context=Context(10,100,1000);
        context.CurrentMagicka=6;
        var spell=Candidate("spell:1",NpcHealthCareCandidateKind.OwnedHealingSpell,0,1,30);
        spell.CostMagicka=7;
        Equal(null,NpcHealthCareChoice.Select(context,new[] {spell}));
        context.CurrentMagicka=7;
        Equal("spell:1",NpcHealthCareChoice.Select(context,new[] {spell}).Candidate.StableId);
        // Returned candidates are detached from mutable discovery records.
        var selected=NpcHealthCareChoice.Select(context,new[] {spell});
        spell.CostMagicka=200;
        Equal(7,selected.Candidate.CostMagicka);
    }

    private static void SpellVersusRest()
    {
        var context=Context(60,100,50);context.CurrentMagicka=20;
        var spell=Candidate("spell:1",NpcHealthCareCandidateKind.OwnedHealingSpell,0,1,40);
        spell.CostMagicka=7;
        var rest=Candidate("home:bed",NpcHealthCareCandidateKind.Rest,0,3600,90);
        Equal("home:bed",NpcHealthCareChoice.Select(context,new[] {spell,rest}).Candidate.StableId);
        context.CurrentHealth=10;
        Equal("spell:1",NpcHealthCareChoice.Select(context,new[] {spell,rest}).Candidate.StableId);
        var cheap=Candidate("spell:cheap",NpcHealthCareCandidateKind.OwnedHealingSpell,0,1,40);
        cheap.CostMagicka=1;
        Equal("spell:cheap",NpcHealthCareChoice.Select(context,new[] {spell,cheap}).Candidate.StableId);
    }

    private static void SpellCostValidation()
    {
        var context=Context(10,100,50);context.CurrentMagicka=30;
        var spell=Candidate("spell:1",NpcHealthCareCandidateKind.OwnedHealingSpell,1,1,40);
        spell.CostMagicka=7;
        Throws<ArgumentException>(()=>NpcHealthCareChoice.Select(context,new[] {spell}));
        spell.CostGold=0;spell.CostMagicka=0;
        Throws<ArgumentException>(()=>NpcHealthCareChoice.Select(context,new[] {spell}));
        var potion=Candidate("potion:1",NpcHealthCareCandidateKind.OwnedHealingItem,0,1,40);
        potion.CostMagicka=1;
        Throws<ArgumentException>(()=>NpcHealthCareChoice.Select(context,new[] {potion}));
        context.CurrentMagicka=-1;
        Throws<ArgumentException>(()=>NpcHealthCareChoice.Select(context,new NpcHealthCareCandidate[0]));
    }

    private static void FullHealthDoesNotCast()
    {
        var context=Context(100,100,50);context.CurrentMagicka=30;
        var spell=Candidate("spell:1",NpcHealthCareCandidateKind.OwnedHealingSpell,0,1,40);
        spell.CostMagicka=7;
        Equal(null,NpcHealthCareChoice.Select(context,new[] {spell}));
    }

    private static NpcHealthCareChoiceContext Context(int hp, int maximum, int gold)
    {
        return new NpcHealthCareChoiceContext
        {
            CurrentHealth = hp, MaximumHealth = maximum, AvailableGold = gold
        };
    }

    private static NpcHealthCareCandidate Candidate(string id, NpcHealthCareCandidateKind kind,
        int cost, double seconds, int healthGain, bool safe = true, bool accessible = true, bool supplyKnown = true)
    {
        return new NpcHealthCareCandidate
        {
            StableId = id, Kind = kind, CostGold = cost, ExpectedTimeSeconds = seconds,
            ExpectedHealthGain = healthGain, IsSafe = safe, IsAccessible = accessible, SupplyKnown = supplyKnown
        };
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }

    private static void True(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
    }
}
