using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

internal static class Program
{
    private static int Main()
    {
        var tests = new List<(string Name, Action Run)>
        {
            ("maps only standard native class careers", CareerMapping),
            ("stable pair roll survives repeated and save-reload identity", StableEncounterRoll),
            ("retreat requires real role policy and live native career", RetreatEligibility),
            ("current allies and changed careers cannot receive retreat", CurrentActorEligibility),
            ("default passive targets are not combat threats", CombatTargetEvidence),
            ("managed civilian target can supply native sight geometry", NativeEnemyTargetBoundary),
            ("retreat corridor starts at controller ground plane", ControllerGroundPlane),
            ("city watch uses the two-percent policy", CityWatchPolicy),
            ("palace guards and quest authority block added retreat", AuthorityAndDuty)
        };

        int failed = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
        }
        Console.WriteLine((tests.Count - failed) + "/" + tests.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void CareerMapping()
    {
        Equal(NpcCombatRole.Rogue, Map(8), "Rogue career");
        Equal(NpcCombatRole.Assassin, Map(11), "Assassin career");
        Equal(NpcCombatRole.Warrior, Map(16), "Warrior career");
        foreach (int career in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 9, 10, 12, 13, 14, 15, 17 })
            Equal(NpcCombatRole.Combatant, Map(career), "combat-capable career " + career);
        foreach (int career in new[] { -1, 18, 128 })
            Equal(NpcCombatRole.Unknown, Map(career), "unsupported/custom career " + career);
    }

    private static void StableEncounterRoll()
    {
        int first;
        True(NativeNpcCombatRoleDecision.TryGetEncounterRoll(733, 421UL, null, 917UL, null, out first),
            "native actor and target IDs form stable pair");
        int reloaded;
        True(NativeNpcCombatRoleDecision.TryGetEncounterRoll(733, 421UL, "ignored", 917UL, "ignored", out reloaded),
            "native IDs take precedence over transient labels");
        Equal(first, reloaded, "same world seed and saved pair keep same roll after reload");
        True(first >= 0 && first <= 99, "roll is in policy range");

        int ownerBound;
        True(NativeNpcCombatRoleDecision.TryGetEncounterRoll(733, 0UL, "native-person:stable-rogue", 0UL,
            NativeNpcCombatRoleDecision.PlayerTargetIdentity, out ownerBound),
            "explicit actor ID and player sentinel support temporary actor");
        True(!NativeNpcCombatRoleDecision.TryGetEncounterRoll(733, 0UL, null, 917UL, null, out ownerBound),
            "missing actor ID never gets a per-frame random decision");
        True(!NativeNpcCombatRoleDecision.TryGetEncounterRoll(733, 421UL, null, 0UL, null, out ownerBound),
            "missing target ID never gets a per-frame random decision");
        True(!NativeNpcCombatRoleDecision.TryGetEncounterRoll(733, 0UL, " ", 0UL,
            NativeNpcCombatRoleDecision.PlayerTargetIdentity, out ownerBound),
            "blank owner ID is not stable identity");
    }

    private static void RetreatEligibility()
    {
        var rogue = Role(NpcCombatRole.Rogue, NpcServiceBinding.Independent);
        True(NativeNpcCombatRoleDecision.ShouldApplyRetreat(rogue, NpcCombatRole.Rogue, false, false, 25, 100, 34),
            "wounded independent rogue with eligible stable roll follows core policy");
        True(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(rogue, NpcCombatRole.Rogue, false, false, 75, 100, 0),
            "healthy rogue keeps native action");
        True(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(rogue, NpcCombatRole.Assassin, false, false, 1, 100, 0),
            "stale role cannot override live native career");
        True(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(rogue, NpcCombatRole.Rogue, false, false, 25, 100, 35),
            "35 percent policy boundary stays exact");
    }

    private static void AuthorityAndDuty()
    {
        var unknownService = Role(NpcCombatRole.Rogue, NpcServiceBinding.Unknown);
        True(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(unknownService, NpcCombatRole.Rogue,
            false, false, 1, 100, 0), "unknown service does not imply independence");

        var palaceBound = Role(NpcCombatRole.Rogue, NpcServiceBinding.NobleKingdom);
        True(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(palaceBound, NpcCombatRole.Rogue,
            false, false, 1, 100, 0), "noble service outranks injury and roll");

        var palaceGuard = Role(NpcCombatRole.PalaceGuard, NpcServiceBinding.NobleKingdom);
        for (int roll = 0; roll <= 99; roll++)
            True(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(palaceGuard, NpcCombatRole.PalaceGuard,
                false, false, 1, 100, roll), "palace guard cannot flee at roll " + roll);
        True(NpcRolePolicy.FightsToDeath(palaceGuard), "palace guard is explicitly fights-to-death");

        var soldier = Role(NpcCombatRole.Soldier, NpcServiceBinding.Independent);
        True(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(soldier, NpcCombatRole.Soldier,
            false, false, 1, 100, 0), "soldier duty cannot enter independent flight branch");

        var independent = Role(NpcCombatRole.Assassin, NpcServiceBinding.Independent);
        True(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(independent, NpcCombatRole.Assassin,
            true, false, 1, 100, 0), "quest-owned actor keeps native behavior");
        True(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(independent, NpcCombatRole.Assassin,
            false, true, 1, 100, 0), "protected quest target keeps native targeting authority");
    }

    private static void CityWatchPolicy()
    {
        var cityWatch = Role(NpcCombatRole.Guard, NpcServiceBinding.NobleKingdom);
        True(NativeNpcCombatRoleDecision.ShouldApplyRetreat(cityWatch, NpcCombatRole.Guard,
            false, false, 1, 100, 0), "City Watch may retreat on the first eligible roll");
        True(NativeNpcCombatRoleDecision.ShouldApplyRetreat(cityWatch, NpcCombatRole.Guard,
            false, false, 1, 100, 1), "City Watch may retreat on the second eligible roll");
        True(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(cityWatch, NpcCombatRole.Guard,
            false, false, 1, 100, 2), "City Watch fights for remaining 98 percent of rolls");
        True(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(cityWatch, NpcCombatRole.Guard,
            false, false, 26, 100, 0), "City Watch does not retreat above severe-injury threshold");
    }

    private static void CurrentActorEligibility()
    {
        True(NativeNpcCombatRoleDecision.IsCurrentActorRoleEligible(
            NpcCombatRole.Rogue, NpcCombatRole.Rogue, false), "unchanged non-ally career stays eligible");
        True(!NativeNpcCombatRoleDecision.IsCurrentActorRoleEligible(
            NpcCombatRole.Rogue, NpcCombatRole.Assassin, false), "changed career loses saved-role authority");
        True(!NativeNpcCombatRoleDecision.IsCurrentActorRoleEligible(
            NpcCombatRole.Rogue, NpcCombatRole.Rogue, true), "player allies never get extra retreat authority");
    }

    private static void CombatTargetEvidence()
    {
        True(!NativeNpcCombatRoleDecision.IsAdmittedCombatTarget(true, false, false, 200),
            "passive player target does not authorize retreat");
        True(NativeNpcCombatRoleDecision.IsAdmittedCombatTarget(true, true, false, 0),
            "hostile player target is an admitted combat encounter");
        True(!NativeNpcCombatRoleDecision.IsAdmittedCombatTarget(false, false, false, 0),
            "non-player target without native pursuit evidence does not authorize retreat");
        True(NativeNpcCombatRoleDecision.IsAdmittedCombatTarget(false, false, true, 0),
            "detected non-player target can be a native combat encounter");
        True(NativeNpcCombatRoleDecision.IsAdmittedCombatTarget(false, false, false, 1),
            "native pursuit timer can admit a non-player combat encounter");
    }

    private static void NativeEnemyTargetBoundary()
    {
        True(NativeNpcCombatRoleDecision.CanSupplyNativeTargetController(true, false),
            "existing enemy target controller keeps current guard combat");
        True(NativeNpcCombatRoleDecision.CanSupplyNativeTargetController(false, true),
            "managed CivilianEntity capsule can supply native sight and motor geometry");
        True(!NativeNpcCombatRoleDecision.CanSupplyNativeTargetController(false, false),
            "target without native controller or body capsule is not delegated into native combat");
    }

    private static void ControllerGroundPlane()
    {
        Equal(4.70f, NativeNpcCombatRoleDecision.GroundPlaneYFromControllerBounds(4.70f),
            "steering samples the live controller bounds lower face without a root-height or skin offset");
    }

    private static NpcCombatRole Map(int careerIndex)
    {
        NpcCombatRole result;
        return NativeNpcCombatRoleDecision.TryMapCareer(careerIndex, out result) ? result : NpcCombatRole.Unknown;
    }

    private static NpcRoleState Role(NpcCombatRole combat, NpcServiceBinding service)
    {
        return new NpcRoleState
        {
            PersonId = "npc:stable-combatant",
            CombatRole = combat,
            CombatSourceId = "native:enemy-class-career",
            ServiceBinding = service,
            ServiceSourceId = service == NpcServiceBinding.Unknown ? null :
                service == NpcServiceBinding.Independent ? "user-policy:unbound-native-career" : "native:palace-stationing",
            Revision = 1
        };
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + "; expected=" + expected + "; actual=" + actual);
    }
}
