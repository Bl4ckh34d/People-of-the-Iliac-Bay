using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Owner-specific native implementation. TryResolveLive must read the current actor,
    /// target, role, allegiance, hostility, duty, crime and quest sources. A false
    /// TryStart must mean that no native action began. The owner must recheck mutable
    /// native facts inside TryStart before changing state.
    /// </summary>
    public interface INpcCombatDutyNativePort
    {
        bool IsAvailable(NpcCombatDutyKind kind, string actorId, string targetId);
        bool TryResolveLive(NpcCombatDutyKind kind, string actorId, string targetId,
            out NpcCombatDutyAdmission evidence);
        bool TryStart(NpcCombatDutyAdmission evidence, out string nativeAttemptId);
        bool TryObserve(string nativeAttemptId, out NpcCombatDutyReceipt receipt);
    }

    public enum NpcCombatDutyStartStatus { Denied = 0, StartedUnobserved = 1 }

    /// <summary>A native start is pending evidence. It is never an action success receipt.</summary>
    public sealed class NpcCombatDutyStart
    {
        public NpcCombatDutyStartStatus Status { get; private set; }
        public NpcCombatDutyKind Kind { get; private set; }
        public string ActorId { get; private set; }
        public string TargetId { get; private set; }
        public string NativeAttemptId { get; private set; }
        public string Reason { get; private set; }
        public NpcCombatDutyReceipt Receipt { get { return null; } }

        internal NpcCombatDutyStart(NpcCombatDutyStartStatus status, NpcCombatDutyKind kind,
            string actorId, string targetId, string nativeAttemptId, string reason)
        {
            Status = status; Kind = kind; ActorId = actorId; TargetId = targetId;
            NativeAttemptId = nativeAttemptId; Reason = reason;
        }
    }

    /// <summary>Only the native owner may report an observed result for the exact attempt.</summary>
    public sealed class NpcCombatDutyReceipt
    {
        public NpcCombatDutyKind Kind;
        public string ActorId, TargetId, NativeAttemptId, ObservationSourceId;
    }

    /// <summary>Shared admission boundary. No native owner is registered by this class.</summary>
    public static class NpcCombatDutyRuntime
    {
        public static NpcCombatDutyStart TryStart(INpcCombatDutyNativePort port,
            NpcCombatDutyKind kind, string actorId, string targetId)
        {
            if (port == null || !port.IsAvailable(kind, actorId, targetId))
                return Denied(kind, actorId, targetId, "Native combat or duty adapter is unavailable.");
            NpcCombatDutyAdmission evidence;
            if (!port.TryResolveLive(kind, actorId, targetId, out evidence) || evidence == null ||
                evidence.Kind != kind || !string.Equals(evidence.ActorId, actorId, StringComparison.Ordinal) ||
                !string.Equals(evidence.TargetId, targetId, StringComparison.Ordinal))
                return Denied(kind, actorId, targetId, "Exact live actor and target evidence is unavailable.");
            string reason;
            if (!NpcCombatDutyPolicy.TryAdmit(evidence, out reason))
                return Denied(kind, actorId, targetId, reason);
            if (!port.IsAvailable(kind, actorId, targetId))
                return Denied(kind, actorId, targetId, "Native combat or duty adapter became unavailable.");
            string nativeAttemptId;
            if (!port.TryStart(evidence, out nativeAttemptId))
                return Denied(kind, actorId, targetId, "Native combat or duty owner rejected start.");
            // A successful native call can still have an unobserved outcome. Keep it
            // pending even if the owner failed to provide a usable attempt identity.
            return new NpcCombatDutyStart(NpcCombatDutyStartStatus.StartedUnobserved,
                kind, actorId, targetId, nativeAttemptId, "Native start awaits observation.");
        }

        public static bool TryObserve(INpcCombatDutyNativePort port, NpcCombatDutyStart start,
            out NpcCombatDutyReceipt receipt)
        {
            receipt = null;
            if (port == null || start == null || start.Status != NpcCombatDutyStartStatus.StartedUnobserved ||
                !ValidSource(start.NativeAttemptId)) return false;
            NpcCombatDutyReceipt observed;
            if (!port.TryObserve(start.NativeAttemptId, out observed) || observed == null ||
                observed.Kind != start.Kind ||
                !string.Equals(observed.ActorId, start.ActorId, StringComparison.Ordinal) ||
                !string.Equals(observed.TargetId, start.TargetId, StringComparison.Ordinal) ||
                !string.Equals(observed.NativeAttemptId, start.NativeAttemptId, StringComparison.Ordinal) ||
                !ValidSource(observed.ObservationSourceId))
                return false;
            receipt = new NpcCombatDutyReceipt
            {
                Kind = observed.Kind,
                ActorId = observed.ActorId,
                TargetId = observed.TargetId,
                NativeAttemptId = observed.NativeAttemptId,
                ObservationSourceId = observed.ObservationSourceId
            };
            return true;
        }

        private static NpcCombatDutyStart Denied(NpcCombatDutyKind kind, string actorId,
            string targetId, string reason)
        { return new NpcCombatDutyStart(NpcCombatDutyStartStatus.Denied, kind, actorId, targetId, null, reason); }

        private static bool ValidSource(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > NpcRoleState.MaximumSourceIdLength)
                return false;
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i])) return false;
            return true;
        }
    }
}
