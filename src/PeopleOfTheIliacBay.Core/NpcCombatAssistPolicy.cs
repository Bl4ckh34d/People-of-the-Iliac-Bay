namespace PeopleOfTheIliacBay.Core
{
    public enum NpcCombatAssistObservation { Interrupted, Fighting, TargetDefeated }

    /// <summary>Current facts for one already admitted native fight. No damage is simulated here.</summary>
    public sealed class NpcCombatAssistFacts
    {
        public bool ActorAlive, TargetPresent, ConsentCurrent, OwnershipClear, SameScene;
        public bool TargetHostile, TargetAttackingProtectedSubject, UnprovokedAttack;
        public bool NativeDeathObserved;
        public int TargetHealth;
        public double ElapsedSeconds;
    }

    public static class NpcCombatAssistPolicy
    {
        public const double MaximumSeconds = 120;

        public static NpcCombatAssistObservation Observe(NpcCombatAssistFacts facts)
        {
            if (facts == null || !facts.ActorAlive || !facts.TargetPresent && !facts.NativeDeathObserved ||
                !facts.ConsentCurrent || !facts.OwnershipClear || !facts.SameScene ||
                !facts.UnprovokedAttack || facts.TargetHealth < 0 && !facts.NativeDeathObserved ||
                facts.NativeDeathObserved && facts.TargetPresent && facts.TargetHealth > 0 ||
                double.IsNaN(facts.ElapsedSeconds) || double.IsInfinity(facts.ElapsedSeconds) ||
                facts.ElapsedSeconds < 0 || facts.ElapsedSeconds >= MaximumSeconds)
                return NpcCombatAssistObservation.Interrupted;
            // Native death is observable before the motor/senses clear their target.
            if (facts.NativeDeathObserved || facts.TargetHealth == 0) return NpcCombatAssistObservation.TargetDefeated;
            return facts.TargetHostile && facts.TargetAttackingProtectedSubject ?
                NpcCombatAssistObservation.Fighting : NpcCombatAssistObservation.Interrupted;
        }
    }
}
