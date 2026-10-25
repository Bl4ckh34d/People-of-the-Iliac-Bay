using System;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Proposed combat or watch duty. This value does not create an action or native effect.</summary>
    public enum NpcCombatDutyKind { Fight = 1, Defend = 2, Patrol = 3, Guard = 4 }

    /// <summary>Fresh, independently resolved facts for one proposed action and exact target.</summary>
    public sealed class NpcCombatDutyAdmission
    {
        public NpcCombatDutyKind Kind;
        public string ActorId, TargetId;
        public bool LiveActorResolved, ActorAlive;
        public string LiveActorSourceId;
        public NpcRoleState Role;
        public bool AllegianceResolved;
        public string AllegianceId, AllegianceSourceId;
        public bool CrimeOwnershipResolved, QuestOwnershipResolved, CrimeOrQuestOwnerBlocks;
        public string CrimeOwnershipSourceId, QuestOwnershipSourceId;
        public bool ConsentGranted, AbilityConfirmed, DutyPriorityClear;
        public string ConsentSourceId, AbilitySourceId, DutyPrioritySourceId;
        public bool HostileTargetConfirmed, ProtectedSubjectResolved, RouteOrPostVerified;
        public string HostileTargetId, HostileTargetSourceId;
        public string ProtectedSubjectId, ProtectedSubjectSourceId;
        public string RouteOrPostId, RouteOrPostSourceId;
    }

    /// <summary>Fail-closed admission. Callers must resolve every source from current native state.</summary>
    public static class NpcCombatDutyPolicy
    {
        public static bool TryAdmit(NpcCombatDutyAdmission evidence, out string reason)
        {
            reason = "Missing combat or duty evidence.";
            if (evidence == null || !Enum.IsDefined(typeof(NpcCombatDutyKind), evidence.Kind) ||
                !Id(evidence.ActorId) || !Id(evidence.TargetId)) return false;
            if (!evidence.LiveActorResolved || !evidence.ActorAlive || !Source(evidence.LiveActorSourceId))
            { reason = "Actor is not verified alive in current state."; return false; }

            NpcRoleState role;
            try { role = NpcRoleState.CopyValidated(evidence.Role); }
            catch (ArgumentException)
            { reason = "Actor role lacks verified source evidence."; return false; }
            if (!string.Equals(role.PersonId, evidence.ActorId, StringComparison.Ordinal) ||
                role.CombatRole == NpcCombatRole.Unknown)
            { reason = "Verified combat role does not match actor."; return false; }
            if (!evidence.AllegianceResolved || !Id(evidence.AllegianceId) || !Source(evidence.AllegianceSourceId))
            { reason = "Actor allegiance lacks source evidence."; return false; }
            if (!evidence.CrimeOwnershipResolved || !Source(evidence.CrimeOwnershipSourceId) ||
                !evidence.QuestOwnershipResolved || !Source(evidence.QuestOwnershipSourceId) ||
                evidence.CrimeOrQuestOwnerBlocks)
            { reason = "Crime or quest ownership is unresolved or has priority."; return false; }
            if (!evidence.ConsentGranted || !Source(evidence.ConsentSourceId))
            { reason = "Explicit consent is missing."; return false; }
            if (!evidence.AbilityConfirmed || !Source(evidence.AbilitySourceId))
            { reason = "Actor ability is unverified."; return false; }
            if (!evidence.DutyPriorityClear || !Source(evidence.DutyPrioritySourceId))
            { reason = "Higher-priority duty is unresolved or active."; return false; }

            if (evidence.Kind == NpcCombatDutyKind.Fight || evidence.Kind == NpcCombatDutyKind.Defend)
            {
                if (!NpcRolePolicy.IsCombatExperienced(role))
                { reason = "Actor has no verified combat role."; return false; }
                if (string.Equals(evidence.ActorId, evidence.TargetId, StringComparison.Ordinal) ||
                    !evidence.HostileTargetConfirmed ||
                    !string.Equals(evidence.TargetId, evidence.HostileTargetId, StringComparison.Ordinal) ||
                    !Source(evidence.HostileTargetSourceId))
                { reason = "Exact target lacks verified hostility."; return false; }
                if (evidence.Kind == NpcCombatDutyKind.Defend &&
                    (!evidence.ProtectedSubjectResolved || !Id(evidence.ProtectedSubjectId) ||
                     !Source(evidence.ProtectedSubjectSourceId)))
                { reason = "Protected subject is unverified."; return false; }
            }
            else
            {
                if ((role.CombatRole != NpcCombatRole.Guard && role.CombatRole != NpcCombatRole.PalaceGuard) ||
                    role.ServiceBinding == NpcServiceBinding.Unknown)
                { reason = "Actor has no verified guard service role."; return false; }
                if (!evidence.RouteOrPostVerified ||
                    !string.Equals(evidence.TargetId, evidence.RouteOrPostId, StringComparison.Ordinal) ||
                    !Source(evidence.RouteOrPostSourceId))
                { reason = evidence.Kind == NpcCombatDutyKind.Patrol ?
                    "Patrol route is unverified." : "Guard post is unverified."; return false; }
            }
            reason = "";
            return true;
        }

        private static bool Id(string value)
        { return NpcRoleState.ValidIdentifier(value, NpcRoleState.MaximumPersonIdLength); }

        private static bool Source(string value)
        { return NpcRoleState.ValidIdentifier(value, NpcRoleState.MaximumSourceIdLength); }
    }
}
