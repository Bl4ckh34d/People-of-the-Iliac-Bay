namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Deterministic policy for native-backed NPC rank, combat role and service facts.</summary>
    public static class NpcRolePolicy
    {
        public const double MinimumDangerFear = 0d;
        public const double MaximumDangerFear = 100d;
        public const double LowDangerFearScale = 0.1d;
        public const double LowDangerFearMaximum = 10d;
        public const double RetreatMaximumHealthRatio = 0.25d;
        public const int RetreatChancePercent = 35;
        public const int CityGuardRetreatChancePercent = 2;
        public const int MinimumEncounterRoll = 0;
        public const int MaximumEncounterRoll = 99;

        public static bool SuppressPlayerFear(NpcRoleState role)
        {
            return role != null && (role.SocialRank == NpcSocialRank.Nobility ||
                role.SocialRank == NpcSocialRank.HighAuthority);
        }

        public static bool IsCombatExperienced(NpcRoleState role)
        {
            if (role == null) return false;
            switch (role.CombatRole)
            {
                case NpcCombatRole.Guard:
                case NpcCombatRole.Soldier:
                case NpcCombatRole.Warrior:
                case NpcCombatRole.Rogue:
                case NpcCombatRole.Assassin:
                case NpcCombatRole.Combatant:
                case NpcCombatRole.PalaceGuard:
                    return true;
                default:
                    return false;
            }
        }

        // Direct civilian assistance is voluntary. Kingdom guards and palace guards
        // remain under their own duty controller.
        public static bool CanDirectCombatAssist(NpcRoleState role)
        {
            return IsCombatExperienced(role) && role.ServiceBinding == NpcServiceBinding.Independent &&
                role.CombatRole != NpcCombatRole.Guard && role.CombatRole != NpcCombatRole.Soldier &&
                role.CombatRole != NpcCombatRole.PalaceGuard;
        }

        public static bool FightsToDeath(NpcRoleState role)
        {
            return role != null && (role.CombatRole == NpcCombatRole.Soldier ||
                role.CombatRole == NpcCombatRole.PalaceGuard);
        }

        public static double DangerFear(NpcRoleState role, double rawIntensity)
        {
            if (double.IsNaN(rawIntensity) || double.IsInfinity(rawIntensity)) return MinimumDangerFear;
            double intensity = rawIntensity < MinimumDangerFear ? MinimumDangerFear :
                rawIntensity > MaximumDangerFear ? MaximumDangerFear : rawIntensity;
            if (!IsCombatExperienced(role)) return intensity;
            double reduced = intensity * LowDangerFearScale;
            return reduced > LowDangerFearMaximum ? LowDangerFearMaximum : reduced;
        }

        public static bool ShouldRetreat(NpcRoleState role, int currentHealth, int maximumHealth, int encounterRoll0to99)
        {
            if (role == null || currentHealth <= 0 || maximumHealth <= 0 || currentHealth > maximumHealth ||
                encounterRoll0to99 < MinimumEncounterRoll || encounterRoll0to99 > MaximumEncounterRoll ||
                !IsCombatExperienced(role) || role.CombatRole == NpcCombatRole.PalaceGuard ||
                role.CombatRole == NpcCombatRole.Soldier)
                return false;

            if ((double)currentHealth / maximumHealth > RetreatMaximumHealthRatio) return false;
            if (role.CombatRole == NpcCombatRole.Guard)
                return encounterRoll0to99 < CityGuardRetreatChancePercent;
            if (role.ServiceBinding != NpcServiceBinding.Independent) return false;
            return encounterRoll0to99 < RetreatChancePercent;
        }
    }
}
