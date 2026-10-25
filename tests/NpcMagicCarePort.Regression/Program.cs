using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var facts = Facts();
        var offers = NpcMagicCarePort.Discover(facts);
        Check(offers.Length == 3, "exact heal, poison and disease offers");
        Check(offers[0].Need == NpcMagicCareNeed.Heal && offers[0].Authorization.EffectId == "Heal-Health",
            "injury offer is exact heal");
        Check(offers[1].Need == NpcMagicCareNeed.CurePoison && offers[1].Authorization.EffectId == "Cure-Poison",
            "poison offer is exact cure");
        Check(offers[2].Need == NpcMagicCareNeed.CureDisease && offers[2].Authorization.EffectId == "Cure-Disease",
            "disease offer is exact cure");
        facts.CurrentHealth = facts.MaximumHealth;
        Check(NpcMagicCarePort.Discover(facts).Length == 2, "full health suppresses heal");
        facts.PoisonCount = facts.DiseaseCount = 0;
        Check(NpcMagicCarePort.Discover(facts).Length == 0, "absent conditions suppress cure");
        facts = Facts(); facts.CurrentMagicka = 2;
        Check(NpcMagicCarePort.Discover(facts).Length == 0, "insufficient magicka suppresses all");
        facts = Facts(); facts.DutyAvailable = false;
        Check(NpcMagicCarePort.Discover(facts).Length == 0, "duty conflict suppresses all");
        facts = Facts(); facts.QuestProtected = true;
        Check(NpcMagicCarePort.Discover(facts).Length == 0, "quest protection suppresses all");
        facts = Facts(); facts.NativeAdapterAvailable = false;
        Check(NpcMagicCarePort.Discover(facts).Length == 0, "absent native adapter suppresses all");
        facts = Facts(); facts.KnownEffects[0].Hostile = true;
        Check(NpcMagicCarePort.Discover(facts).Length == 2, "hostile spell is not treatment");
        facts = Facts(); facts.KnownEffects[0].EffectKind = NpcMagicEffectKind.TimedEffect;
        Check(NpcMagicCarePort.Discover(facts).Length == 2, "mismatched effect kind is not treatment");
        facts = Facts(); facts.KnownEffects[0].SpellId = "";
        Check(NpcMagicCarePort.Discover(facts).Length == 2, "missing exact spell identity is not treatment");
        Console.WriteLine("13/13 passed");
        return 0;
    }

    private static NpcMagicCareFacts Facts()
    {
        return new NpcMagicCareFacts {
            ActorId = "npc:mage", CurrentHealth = 10, MaximumHealth = 20,
            CurrentMagicka = 30, PoisonCount = 1, DiseaseCount = 1,
            DutyAvailable = true, NativeAdapterAvailable = true,
            KnownEffects = new[] {
                Known("Heal-Health", NpcMagicEffectKind.Health),
                Known("Cure-Poison", NpcMagicEffectKind.Condition),
                Known("Cure-Disease", NpcMagicEffectKind.Condition),
                Known("Shield", NpcMagicEffectKind.TimedEffect),
                Known("Teleport", NpcMagicEffectKind.TimedEffect)
            }
        };
    }

    private static NpcMagicKnownEffect Known(string effect, NpcMagicEffectKind kind)
    {
        return new NpcMagicKnownEffect {
            SpellId = "native:spellbook:" + effect, EffectId = effect,
            EffectKind = kind, MagickaCost = 10, Hostile = false
        };
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
