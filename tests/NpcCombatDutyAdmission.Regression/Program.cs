using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int count;

    private static void Main()
    {
        foreach (NpcCombatDutyKind kind in new[] { NpcCombatDutyKind.Fight, NpcCombatDutyKind.Defend,
            NpcCombatDutyKind.Patrol, NpcCombatDutyKind.Guard })
        {
            var valid = Valid(kind);
            Admit(valid, kind + " complete evidence");
            Deny(Change(valid, x => x.LiveActorResolved = false), kind + " unresolved actor");
            Deny(Change(valid, x => x.ActorAlive = false), kind + " dead actor");
            Deny(Change(valid, x => x.LiveActorSourceId = null), kind + " unsourced actor");
            Deny(Change(valid, x => x.Role = null), kind + " missing role");
            Deny(Change(valid, x => x.Role.CombatSourceId = null), kind + " unsourced role");
            Deny(Change(valid, x => x.AllegianceSourceId = null), kind + " unsourced allegiance");
            Deny(Change(valid, x => x.AllegianceResolved = false), kind + " unresolved allegiance");
            Deny(Change(valid, x => x.CrimeOwnershipResolved = false), kind + " unresolved crime owner");
            Deny(Change(valid, x => x.QuestOwnershipResolved = false), kind + " unresolved quest owner");
            Deny(Change(valid, x => x.CrimeOrQuestOwnerBlocks = true), kind + " ownership conflict");
            Deny(Change(valid, x => x.ConsentSourceId = null), kind + " missing consent source");
            Deny(Change(valid, x => x.ConsentGranted = false), kind + " no consent");
            Deny(Change(valid, x => x.AbilityConfirmed = false), kind + " no ability");
            Deny(Change(valid, x => x.DutyPriorityClear = false), kind + " duty conflict");
            Deny(Change(valid, x => x.DutyPrioritySourceId = null), kind + " unsourced duty clearance");
        }

        foreach (NpcCombatDutyKind kind in new[] { NpcCombatDutyKind.Fight, NpcCombatDutyKind.Defend })
        {
            Deny(Change(Valid(kind), x => x.HostileTargetSourceId = null), kind + " unsourced hostility");
            Deny(Change(Valid(kind), x => x.HostileTargetConfirmed = false), kind + " unconfirmed hostility");
            Deny(Change(Valid(kind), x => x.HostileTargetId = "other"), kind + " hostility target mismatch");
            Deny(Change(Valid(kind), x => x.Role.CombatRole = NpcCombatRole.Civilian), kind + " civilian combat role");
        }
        Deny(Change(Valid(NpcCombatDutyKind.Defend), x => x.ProtectedSubjectSourceId = null), "unsourced protected subject");
        Deny(Change(Valid(NpcCombatDutyKind.Defend), x => x.ProtectedSubjectResolved = false), "unresolved protected subject");
        Deny(Change(Valid(NpcCombatDutyKind.Defend), x => x.ProtectedSubjectId = null), "missing protected subject");
        Admit(Change(Valid(NpcCombatDutyKind.Fight), x => { x.ProtectedSubjectId = null; x.ProtectedSubjectSourceId = null; }),
            "fight does not require protected subject");

        Deny(Change(Valid(NpcCombatDutyKind.Patrol), x => x.RouteOrPostSourceId = null), "unverified patrol route");
        Deny(Change(Valid(NpcCombatDutyKind.Guard), x => x.RouteOrPostSourceId = null), "unverified guard post");
        Deny(Change(Valid(NpcCombatDutyKind.Patrol), x => x.RouteOrPostVerified = false), "unresolved patrol route");
        Deny(Change(Valid(NpcCombatDutyKind.Guard), x => x.RouteOrPostVerified = false), "unresolved guard post");
        Deny(Change(Valid(NpcCombatDutyKind.Patrol), x => x.RouteOrPostId = "other"), "patrol route mismatch");
        Deny(Change(Valid(NpcCombatDutyKind.Guard), x => x.RouteOrPostId = "other"), "guard post mismatch");
        Deny(Change(Valid(NpcCombatDutyKind.Patrol), x => x.Role.CombatRole = NpcCombatRole.Warrior), "non-guard patrol role");
        Deny(Change(Valid(NpcCombatDutyKind.Guard), x => x.Role.CombatRole = NpcCombatRole.Warrior), "non-guard post role");
        Deny(Change(Valid(NpcCombatDutyKind.Guard), x =>
        {
            x.Role.ServiceBinding = NpcServiceBinding.Unknown;
            x.Role.ServiceSourceId = null;
        }), "unknown service binding");
        Admit(Change(Valid(NpcCombatDutyKind.Guard), x => x.Role.CombatRole = NpcCombatRole.PalaceGuard),
            "palace guard can hold verified post");
        Admit(Change(Valid(NpcCombatDutyKind.Fight), x =>
        {
            x.Role.CombatRole = NpcCombatRole.Warrior;
            x.Role.ServiceBinding = NpcServiceBinding.Unknown;
            x.Role.ServiceSourceId = null;
        }), "independent warrior can fight without guard service");
        Deny(Change(Valid(NpcCombatDutyKind.Fight), x => x.Role.PersonId = "other"), "actor role mismatch");
        Deny(Change(Valid(NpcCombatDutyKind.Fight), x => x.ActorId = "other"), "actor request mismatch");
        Deny(Change(Valid(NpcCombatDutyKind.Fight), x => x.AbilitySourceId = null), "unsourced ability");
        Deny(Change(Valid(NpcCombatDutyKind.Fight), x => x.CrimeOwnershipSourceId = null), "unsourced crime owner");
        Deny(Change(Valid(NpcCombatDutyKind.Fight), x => x.QuestOwnershipSourceId = null), "unsourced quest owner");
        Deny(Change(Valid(NpcCombatDutyKind.Fight), x => x.AllegianceId = null), "missing allegiance");
        Deny(Change(Valid(NpcCombatDutyKind.Fight), x => x.TargetId = "actor"), "self target");
        Deny(Change(Valid(NpcCombatDutyKind.Guard), x => x.TargetId = "actor"), "self post mismatch");
        Deny(Change(Valid(NpcCombatDutyKind.Fight), x => x.Kind = (NpcCombatDutyKind)99), "unknown kind");
        Deny(null, "null evidence");
        Console.WriteLine("PASS " + count + " combat/duty admission checks");
    }

    private static NpcCombatDutyAdmission Valid(NpcCombatDutyKind kind)
    {
        string target = kind == NpcCombatDutyKind.Patrol ? "route:1" :
            kind == NpcCombatDutyKind.Guard ? "post:1" : "enemy:1";
        return new NpcCombatDutyAdmission
        {
            Kind = kind, ActorId = "actor", TargetId = target, LiveActorResolved = true, ActorAlive = true,
            LiveActorSourceId = "native:live-actor",
            Role = new NpcRoleState { PersonId = "actor", Revision = 1, CombatRole = NpcCombatRole.Guard,
                CombatSourceId = "native:guard", ServiceBinding = NpcServiceBinding.NobleKingdom,
                ServiceSourceId = "native:service" },
            AllegianceResolved = true, AllegianceId = "town-watch", AllegianceSourceId = "native:faction",
            CrimeOwnershipResolved = true, CrimeOwnershipSourceId = "native:crime",
            QuestOwnershipResolved = true, QuestOwnershipSourceId = "native:quest",
            ConsentGranted = true, ConsentSourceId = "conversation:explicit-agreement",
            AbilityConfirmed = true, AbilitySourceId = "native:combat-capability",
            DutyPriorityClear = true, DutyPrioritySourceId = "native:duty-priority",
            HostileTargetConfirmed = true, HostileTargetId = target, HostileTargetSourceId = "native:hostility",
            ProtectedSubjectResolved = true, ProtectedSubjectId = "player", ProtectedSubjectSourceId = "native:protection",
            RouteOrPostVerified = true, RouteOrPostId = target, RouteOrPostSourceId = "native:route-or-post"
        };
    }

    private static NpcCombatDutyAdmission Change(NpcCombatDutyAdmission input, Action<NpcCombatDutyAdmission> change)
    {
        var copy = Valid(input.Kind);
        change(copy);
        return copy;
    }

    private static void Admit(NpcCombatDutyAdmission input, string name)
    {
        string reason;
        if (!NpcCombatDutyPolicy.TryAdmit(input, out reason) || reason != "")
            throw new Exception("Expected admission: " + name + "; reason=" + reason);
        count++;
    }

    private static void Deny(NpcCombatDutyAdmission input, string name)
    {
        string reason;
        if (NpcCombatDutyPolicy.TryAdmit(input, out reason) || string.IsNullOrWhiteSpace(reason))
            throw new Exception("Expected sourced denial: " + name);
        count++;
    }
}
