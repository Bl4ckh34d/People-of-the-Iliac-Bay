using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int checks;
    private static void Main()
    {
        Check(NpcCombatAssistPolicy.Observe(Valid()) == NpcCombatAssistObservation.Fighting,
            "A current exact native attacker remains in combat.");
        var facts = Valid(); facts.TargetHealth = 0;
        Check(NpcCombatAssistPolicy.Observe(facts) == NpcCombatAssistObservation.TargetDefeated,
            "Observed target death completes the fight.");
        facts = Valid(); facts.TargetPresent = false; facts.TargetHealth = 0;
        Interrupted(facts, "Disappearance does not invent a defeated target.");
        facts = Valid(); facts.TargetPresent = false; facts.TargetHealth = -1; facts.NativeDeathObserved = true;
        Check(NpcCombatAssistPolicy.Observe(facts) == NpcCombatAssistObservation.TargetDefeated,
            "Exact native death evidence survives native body destruction.");
        facts = Valid(); facts.NativeDeathObserved = true;
        Interrupted(facts, "A living exact body overrides stale death evidence.");
        facts = Valid(); facts.ActorAlive = false; Interrupted(facts, "Actor death interrupts.");
        facts = Valid(); facts.ConsentCurrent = false; Interrupted(facts, "Withdrawn consent interrupts.");
        facts = Valid(); facts.OwnershipClear = false; Interrupted(facts, "Quest or duty ownership interrupts.");
        facts = Valid(); facts.SameScene = false; Interrupted(facts, "Scene loss interrupts.");
        facts = Valid(); facts.TargetHostile = false; Interrupted(facts, "Pacification stops combat.");
        facts = Valid(); facts.TargetAttackingProtectedSubject = false;
        Interrupted(facts, "An unrelated native target is not pursued.");
        facts = Valid(); facts.UnprovokedAttack = false; Interrupted(facts, "Player provocation stops assistance.");
        facts = Valid(); facts.ElapsedSeconds = 120; Interrupted(facts, "Bounded fight expires.");
        facts = Valid(); facts.ElapsedSeconds = double.NaN; Interrupted(facts, "Invalid time interrupts.");
        facts = Valid(); facts.ElapsedSeconds = -1; Interrupted(facts, "Reversed time interrupts.");
        facts = Valid(); facts.TargetHealth = -1; Interrupted(facts, "Invalid target health is not death evidence.");
        facts = Valid(); facts.TargetHealth = 0; facts.ConsentCurrent = false;
        Interrupted(facts, "A canceled fight cannot receive a later completion.");
        Check(NpcCombatAssistPolicy.Observe(null) == NpcCombatAssistObservation.Interrupted, "Missing facts interrupt.");
        Console.WriteLine("PASS " + checks + " native combat assist policy checks");
    }
    private static NpcCombatAssistFacts Valid()
    {
        return new NpcCombatAssistFacts { ActorAlive = true, TargetPresent = true,
            ConsentCurrent = true, OwnershipClear = true, SameScene = true, TargetHealth = 30,
            TargetHostile = true, TargetAttackingProtectedSubject = true, UnprovokedAttack = true,
            ElapsedSeconds = 1 };
    }
    private static void Interrupted(NpcCombatAssistFacts facts, string message)
    { Check(NpcCombatAssistPolicy.Observe(facts) == NpcCombatAssistObservation.Interrupted, message); }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
}
