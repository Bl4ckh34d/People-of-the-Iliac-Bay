using System;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

internal static class Program
{
    private static int checks;

    private static void Main()
    {
        foreach (NpcCombatDutyKind kind in new[] { NpcCombatDutyKind.Fight, NpcCombatDutyKind.Defend,
            NpcCombatDutyKind.Patrol, NpcCombatDutyKind.Guard })
        {
            var port = new Port(kind);
            var result = NpcCombatDutyRuntime.TryStart(port, kind, "actor", port.Target);
            Check(result.Status == NpcCombatDutyStartStatus.StartedUnobserved, kind + " start is not a receipt");
            Check(port.Starts == 1, kind + " native start called once");
            Check(result.Receipt == null, kind + " has no invented receipt");
            Check(NpcCombatDutyRuntime.TryObserve(port, result, out var receipt) &&
                receipt.ActorId == "actor" && receipt.TargetId == port.Target && receipt.Kind == kind,
                kind + " exact observed receipt");
            port.LastReceipt.TargetId = "mutated-after-observation";
            Check(receipt.TargetId == port.Target, kind + " receipt is detached from native owner");

            port = new Port(kind) { Available = false };
            Denied(port, kind, "unavailable native adapter");
            port = new Port(kind) { Resolve = false };
            Denied(port, kind, "unresolved live evidence");
            port = new Port(kind) { WrongActor = true };
            Denied(port, kind, "different live actor");
            port = new Port(kind) { WrongTarget = true };
            Denied(port, kind, "different live target");
            port = new Port(kind) { QuestBlocks = true };
            Denied(port, kind, "quest owner priority");
            port = new Port(kind) { DutyBlocks = true };
            Denied(port, kind, "higher duty priority");
            port = new Port(kind) { Start = false };
            Denied(port, kind, "native start rejected");
            port = new Port(kind) { Observe = false };
            result = NpcCombatDutyRuntime.TryStart(port, kind, "actor", port.Target);
            Check(result.Status == NpcCombatDutyStartStatus.StartedUnobserved, kind + " pending observation");
            Check(!NpcCombatDutyRuntime.TryObserve(port, result, out _), kind + " no observation means no receipt");
            port = new Port(kind) { ReceiptTarget = "other" };
            result = NpcCombatDutyRuntime.TryStart(port, kind, "actor", port.Target);
            Check(!NpcCombatDutyRuntime.TryObserve(port, result, out _), kind + " mismatched receipt rejected");
            port = new Port(kind) { ReceiptSource = null };
            result = NpcCombatDutyRuntime.TryStart(port, kind, "actor", port.Target);
            Check(!NpcCombatDutyRuntime.TryObserve(port, result, out _), kind + " unsourced receipt rejected");
        }
        var noNativeOwner = NpcCombatDutyRuntime.TryStart(null, NpcCombatDutyKind.Patrol,
            "g:1:n:1", "route:1");
        Check(noNativeOwner.Status == NpcCombatDutyStartStatus.Denied &&
            !string.IsNullOrWhiteSpace(noNativeOwner.Reason), "unregistered native owner denies patrol");
        Check(!NpcCombatDutyRuntime.TryObserve(null, null, out _), "null port and start fail closed");
        Console.WriteLine("PASS " + checks + " runtime combat/duty checks");
    }

    private static void Denied(Port port, NpcCombatDutyKind kind, string name)
    {
        var result = NpcCombatDutyRuntime.TryStart(port, kind, "actor", port.Target);
        Check(result.Status == NpcCombatDutyStartStatus.Denied && port.Starts == 0 &&
            !string.IsNullOrWhiteSpace(result.Reason), kind + " " + name);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
    }

    private sealed class Port : INpcCombatDutyNativePort
    {
        private readonly NpcCombatDutyKind kind;
        internal readonly string Target;
        internal bool Available = true, Resolve = true, WrongActor, WrongTarget, QuestBlocks,
            DutyBlocks, Start = true, Observe = true;
        internal string ReceiptTarget, ReceiptSource = "native:observed-action";
        internal int Starts;
        internal NpcCombatDutyReceipt LastReceipt;

        internal Port(NpcCombatDutyKind kind)
        {
            this.kind = kind;
            Target = kind == NpcCombatDutyKind.Patrol ? "route:1" :
                kind == NpcCombatDutyKind.Guard ? "post:1" : "enemy:1";
        }

        public bool IsAvailable(NpcCombatDutyKind requestedKind, string actorId, string targetId)
        { return Available && requestedKind == kind && actorId == "actor" && targetId == Target; }

        public bool TryResolveLive(NpcCombatDutyKind requestedKind, string actorId, string targetId,
            out NpcCombatDutyAdmission evidence)
        {
            evidence = null;
            if (!Resolve) return false;
            evidence = new NpcCombatDutyAdmission
            {
                Kind = requestedKind, ActorId = WrongActor ? "other" : actorId,
                TargetId = WrongTarget ? "other" : targetId,
                LiveActorResolved = true, ActorAlive = true, LiveActorSourceId = "native:live-actor",
                Role = new NpcRoleState { PersonId = actorId, Revision = 1, CombatRole = NpcCombatRole.Guard,
                    CombatSourceId = "native:watch", ServiceBinding = NpcServiceBinding.NobleKingdom,
                    ServiceSourceId = "native:kingdom" },
                AllegianceResolved = true, AllegianceId = "town-watch", AllegianceSourceId = "native:allegiance",
                CrimeOwnershipResolved = true, CrimeOwnershipSourceId = "native:crime",
                QuestOwnershipResolved = true, QuestOwnershipSourceId = "native:quest",
                CrimeOrQuestOwnerBlocks = QuestBlocks,
                ConsentGranted = true, ConsentSourceId = "conversation:explicit-agreement",
                AbilityConfirmed = true, AbilitySourceId = "native:ability",
                DutyPriorityClear = !DutyBlocks, DutyPrioritySourceId = "native:duty",
                HostileTargetConfirmed = true, HostileTargetId = targetId,
                HostileTargetSourceId = "native:hostility",
                ProtectedSubjectResolved = true, ProtectedSubjectId = "player",
                ProtectedSubjectSourceId = "native:protected-subject",
                RouteOrPostVerified = true, RouteOrPostId = targetId,
                RouteOrPostSourceId = "native:route-or-post"
            };
            return true;
        }

        public bool TryStart(NpcCombatDutyAdmission evidence, out string nativeAttemptId)
        {
            nativeAttemptId = null;
            if (!Start) return false;
            Starts++;
            nativeAttemptId = "native:attempt-1";
            return true;
        }

        public bool TryObserve(string nativeAttemptId, out NpcCombatDutyReceipt receipt)
        {
            receipt = null;
            if (!Observe || nativeAttemptId != "native:attempt-1") return false;
            receipt = new NpcCombatDutyReceipt
            {
                Kind = kind, ActorId = "actor", TargetId = ReceiptTarget ?? Target,
                NativeAttemptId = nativeAttemptId, ObservationSourceId = ReceiptSource
            };
            LastReceipt = receipt;
            return true;
        }
    }
}
