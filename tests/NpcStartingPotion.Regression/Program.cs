using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("starting potion choice is reproducible for world, resident, and archetype", ReproducibleChoice),
            ("starting potion chance varies by identity and never guarantees a potion", MixedIdentityOutcomes),
            ("healers have higher potion odds while other archetypes remain possible", ArchetypeOdds),
            ("potion roll uses a domain independent of utility belongings", IndependentDomain),
            ("starting potion choice rejects invalid inputs", InvalidInputs)
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

    private static void ReproducibleChoice()
    {
        for (uint seed = 0; seed < 500; seed++)
        foreach (NpcArchetype archetype in Enum.GetValues(typeof(NpcArchetype)))
        {
            bool first = NpcStartingBelongings.ShouldStartWithHealingPotion(seed, "resident:" + seed, archetype);
            bool again = NpcStartingBelongings.ShouldStartWithHealingPotion(seed, "resident:" + seed, archetype);
            Equal(first, again);
        }
    }

    private static void MixedIdentityOutcomes()
    {
        int withPotion = 0, withoutPotion = 0;
        for (uint id = 0; id < 4096; id++)
        {
            if (NpcStartingBelongings.ShouldStartWithHealingPotion(73, "resident:" + id, NpcArchetype.Citizen))
                withPotion++;
            else
                withoutPotion++;
        }
        True(withPotion > 0, "identity sample should include potion recipients");
        True(withoutPotion > 0, "identity sample should include NPCs without a potion");
        True(withPotion < 4096, "starting potion chance must never guarantee every resident a potion");
    }

    private static void ArchetypeOdds()
    {
        const uint samples = 8192;
        var counts = new Dictionary<NpcArchetype, int>();
        foreach (NpcArchetype archetype in Enum.GetValues(typeof(NpcArchetype)))
        {
            int count = 0;
            for (uint id = 0; id < samples; id++)
                if (NpcStartingBelongings.ShouldStartWithHealingPotion(91, "resident:" + id, archetype)) count++;
            counts.Add(archetype, count);
            True(count > 0 && count < samples, archetype + " must have a nonzero, non-guaranteed chance");
        }

        True(counts[NpcArchetype.Healer] > counts[NpcArchetype.Mage], "healers should exceed mages in the stable sample");
        True(counts[NpcArchetype.Healer] > counts[NpcArchetype.Warrior], "healers should exceed warriors in the stable sample");
        True(counts[NpcArchetype.Healer] > counts[NpcArchetype.Citizen], "healers should exceed citizens in the stable sample");
    }

    private static void IndependentDomain()
    {
        bool potionWithNoUtility = false, noPotionWithNoUtility = false;
        bool potionWithUtility = false, noPotionWithUtility = false;
        for (uint id = 0; id < 8192; id++)
        {
            string personId = "domain-test:" + id;
            bool potion = NpcStartingBelongings.ShouldStartWithHealingPotion(112, personId, NpcArchetype.Citizen);
            bool utility = NpcStartingBelongings.Generate(112, personId, NpcArchetype.Citizen).Count > 0;
            if (potion && utility) potionWithUtility = true;
            else if (potion) potionWithNoUtility = true;
            else if (utility) noPotionWithUtility = true;
            else noPotionWithNoUtility = true;
        }
        True(potionWithUtility && potionWithNoUtility && noPotionWithUtility && noPotionWithNoUtility,
            "potion decision should vary independently from utility belonging presence");
    }

    private static void InvalidInputs()
    {
        Throws<ArgumentException>(() => NpcStartingBelongings.ShouldStartWithHealingPotion(1, "", NpcArchetype.Healer));
        Throws<ArgumentException>(() => NpcStartingBelongings.ShouldStartWithHealingPotion(1, new string('x', 241), NpcArchetype.Healer));
        Throws<ArgumentOutOfRangeException>(() => NpcStartingBelongings.ShouldStartWithHealingPotion(1, "resident", (NpcArchetype)99));
    }

    private static void True(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + "."); }
}
