using System;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

internal static class Program
{
    private static int Main()
    {
        Check(SelfHealAdmits(), "exact self heal");
        Check(Admits("npc:mage", NpcMagicIntent.Cast, NpcMagicEffectKind.Health, "Heal-Health", 5, 10), "nonhostile self cast admits");
        Check(!Admits("npc:other", NpcMagicIntent.Heal, NpcMagicEffectKind.Health, "Heal-Health", 5, 10), "other target denied");
        Check(!Admits("npc:mage", NpcMagicIntent.Cast, NpcMagicEffectKind.Damage, "Damage-Health", 5, 10, true), "hostile cast denied");
        Check(!Admits("npc:mage", NpcMagicIntent.Cure, NpcMagicEffectKind.Condition, "Cure-Poison", 0, 10), "absent condition denied");
        Check(!Admits("npc:mage", NpcMagicIntent.Buff, NpcMagicEffectKind.TimedEffect, "Shield", 0, 10, false, false), "unavailable adapter denied");
        Check(!Admits("npc:mage", NpcMagicIntent.Heal, NpcMagicEffectKind.Health, "Heal-Health", 5, 11), "wrong native cost denied");
        Check(!Admits("npc:mage", NpcMagicIntent.Heal, NpcMagicEffectKind.Health, "other", 5, 10), "wrong native effect denied");
        Check(!Admits("npc:mage", NpcMagicIntent.Heal, NpcMagicEffectKind.Health, "Heal-Health", 5, 10, false, true, true, true), "quest protection denied");
        Check(!Admits("npc:mage", NpcMagicIntent.Heal, NpcMagicEffectKind.Health, "Heal-Health", 5, 10, false, true, false, false), "duty conflict denied");
        Check(Admits("npc:mage", NpcMagicIntent.Cure, NpcMagicEffectKind.Condition, "Cure-Poison", 1, 10), "observed poison admits cure");
        Check(Admits("npc:mage", NpcMagicIntent.Buff, NpcMagicEffectKind.TimedEffect, "Shield", 0, 10), "known shield admits buff");
        Console.WriteLine("12/12 passed");
        return 0;
    }

    private static bool SelfHealAdmits()
    { return Admits("npc:mage", NpcMagicIntent.Heal, NpcMagicEffectKind.Health, "Heal-Health", 5, 10); }

    private static bool Admits(string target, NpcMagicIntent intent, NpcMagicEffectKind kind,
        string nativeEffect, int before, int nativeCost, bool hostile = false,
        bool adapterAvailable = true, bool dutyAvailable = true, bool questProtected = false)
    {
        var request = new NpcMagicRequest { ActorId = "npc:mage", TargetId = target,
            SpellId = "native:spellbook:0", EffectId = kind == NpcMagicEffectKind.Condition ? "Cure-Poison" :
                kind == NpcMagicEffectKind.TimedEffect ? "Shield" : "Heal-Health", Intent = intent,
            EffectKind = kind, MagickaCost = 10, Hostile = hostile };
        var known = new NpcMagicKnownEffect { SpellId = request.SpellId,
            EffectId = nativeEffect, EffectKind = kind, MagickaCost = nativeCost, Hostile = false };
        NpcMagicAuthorization authorization;
        return NativeNpcMagicSelfCastGate.TryAuthorize(request, known, 20, before,
            adapterAvailable, dutyAvailable, questProtected, out authorization);
    }

    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); }
}
