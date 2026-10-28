using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcSocialRank
    {
        Unknown,
        Commoner,
        Nobility,
        HighAuthority
    }

    public enum NpcCombatRole
    {
        Unknown,
        Civilian,
        Guard,
        Soldier,
        Warrior,
        Rogue,
        Assassin,
        Combatant,
        PalaceGuard = 8
    }

    public enum NpcServiceBinding
    {
        Unknown,
        NobleKingdom,
        Independent
    }

    [Serializable]
    public sealed class NpcRoleState
    {
        public const int MaximumPersonIdLength = 128;
        public const int MaximumSourceIdLength = 512;

        public string PersonId;
        public NpcSocialRank SocialRank;
        public NpcCombatRole CombatRole;
        public NpcServiceBinding ServiceBinding;
        public string RankSourceId;
        public string CombatSourceId;
        public string ServiceSourceId;
        public long Revision;

        /// <summary>Returns a detached role row after validating identity, evidence, enums and revision.</summary>
        public static NpcRoleState CopyValidated(NpcRoleState source)
        {
            if (source == null) throw new ArgumentException("Missing NPC role state.");
            Require(ValidIdentifier(source.PersonId, MaximumPersonIdLength), "Invalid NPC role identity.");
            Require(Enum.IsDefined(typeof(NpcSocialRank), source.SocialRank), "Invalid saved NPC social rank.");
            Require(Enum.IsDefined(typeof(NpcCombatRole), source.CombatRole), "Invalid saved NPC combat role.");
            Require(Enum.IsDefined(typeof(NpcServiceBinding), source.ServiceBinding), "Invalid saved NPC service binding.");
            Require(source.Revision > 0, "Invalid saved NPC role revision.");
            Require(ValidFactSource(source.SocialRank != NpcSocialRank.Unknown, source.RankSourceId),
                "Invalid saved NPC rank source.");
            Require(ValidFactSource(source.CombatRole != NpcCombatRole.Unknown, source.CombatSourceId),
                "Invalid saved NPC combat source.");
            Require(ValidFactSource(source.ServiceBinding != NpcServiceBinding.Unknown, source.ServiceSourceId),
                "Invalid saved NPC service source.");
            Require(source.SocialRank != NpcSocialRank.Unknown || source.CombatRole != NpcCombatRole.Unknown ||
                source.ServiceBinding != NpcServiceBinding.Unknown, "Saved NPC role contains no known facts.");

            return new NpcRoleState
            {
                PersonId = source.PersonId,
                SocialRank = source.SocialRank,
                CombatRole = source.CombatRole,
                ServiceBinding = source.ServiceBinding,
                RankSourceId = source.RankSourceId,
                CombatSourceId = source.CombatSourceId,
                ServiceSourceId = source.ServiceSourceId,
                Revision = source.Revision
            };
        }

        internal static bool ValidIdentifier(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength) return false;
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i])) return false;
            return true;
        }

        internal static bool ValidFactSource(bool hasFact, string sourceId)
        {
            return hasFact ? ValidIdentifier(sourceId, MaximumSourceIdLength) : string.IsNullOrEmpty(sourceId);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new ArgumentException(message);
        }
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>Reads a detached saved role row, or null when person has no role facts.</summary>
        public NpcRoleState ReadNpcRole(string personId)
        {
            bool alive;
            if (!NpcRoleState.ValidIdentifier(personId, NpcRoleState.MaximumPersonIdLength) ||
                !KnownNpcIdentity(state, personId, out alive)) return null;
            var saved = state.NpcRoles.Find(item => item.PersonId == personId);
            return saved == null ? null : NpcRoleState.CopyValidated(saved);
        }

        /// <summary>Merges explicit native role facts for a known NPC. Returns true only when saved facts change.</summary>
        public bool RecordNpcRoleEvidence(string personId, NpcSocialRank rank, NpcCombatRole combatRole,
            NpcServiceBinding serviceBinding, string rankSourceId, string combatSourceId, string serviceSourceId)
        {
            if (!NpcRoleState.ValidIdentifier(personId, NpcRoleState.MaximumPersonIdLength) ||
                !Enum.IsDefined(typeof(NpcSocialRank), rank) || !Enum.IsDefined(typeof(NpcCombatRole), combatRole) ||
                !Enum.IsDefined(typeof(NpcServiceBinding), serviceBinding) ||
                !NpcRoleState.ValidFactSource(rank != NpcSocialRank.Unknown, rankSourceId) ||
                !NpcRoleState.ValidFactSource(combatRole != NpcCombatRole.Unknown, combatSourceId) ||
                !NpcRoleState.ValidFactSource(serviceBinding != NpcServiceBinding.Unknown, serviceSourceId))
                return false;

            bool alive;
            if (!KnownNpcIdentity(state, personId, out alive)) return false;

            int index = state.NpcRoles.FindIndex(item => item.PersonId == personId);
            NpcRoleState current = null;
            if (index >= 0)
            {
                try { current = NpcRoleState.CopyValidated(state.NpcRoles[index]); }
                catch (ArgumentException) { return false; }
            }
            var updated = current ?? new NpcRoleState { PersonId = personId };
            bool changed = false;

            if (rank != NpcSocialRank.Unknown && updated.SocialRank != rank)
            {
                updated.SocialRank = rank;
                updated.RankSourceId = rankSourceId;
                changed = true;
            }
            if (combatRole != NpcCombatRole.Unknown && updated.CombatRole != combatRole)
            {
                updated.CombatRole = combatRole;
                updated.CombatSourceId = combatSourceId;
                changed = true;
            }
            if (serviceBinding != NpcServiceBinding.Unknown && updated.ServiceBinding != serviceBinding)
            {
                updated.ServiceBinding = serviceBinding;
                updated.ServiceSourceId = serviceSourceId;
                changed = true;
            }

            if (!changed || (current != null && current.Revision == long.MaxValue)) return false;
            updated.Revision = current == null ? 1 : current.Revision + 1;
            if (index < 0) state.NpcRoles.Add(updated);
            else state.NpcRoles[index] = updated;
            return true;
        }

        private static List<NpcRoleState> CopyNpcRoles(WorldState source, WorldState validated)
        {
            var result = new List<NpcRoleState>();
            Require(source.NpcRoles != null, "Missing NPC role state list.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var saved in source.NpcRoles)
            {
                var role = NpcRoleState.CopyValidated(saved);
                bool alive;
                Require(KnownNpcIdentity(validated, role.PersonId, out alive), "NPC role state references an unknown NPC.");
                Require(identities.Add(role.PersonId), "Duplicate NPC role identity.");
                result.Add(role);
            }
            return result;
        }
    }
}
