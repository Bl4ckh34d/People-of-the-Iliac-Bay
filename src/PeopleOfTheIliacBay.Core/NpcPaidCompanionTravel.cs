using System;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class NpcPaidTravelHandoff
    {
        public string ContractId;
        public double DeferredAt;

        public static NpcPaidTravelHandoff Copy(NpcPaidTravelHandoff value)
        {
            return value == null ? null : new NpcPaidTravelHandoff
            {
                ContractId = value.ContractId,
                DeferredAt = value.DeferredAt,
            };
        }

        public static void Validate(NpcActionJob job)
        {
            var handoff = job == null ? null : job.PaidTravelHandoff;
            if (handoff == null) return;
            if (!NpcPaidCompanionTravel.IsCurrentTravelJob(job, handoff.ContractId, handoff.DeferredAt))
                throw new ArgumentException("Invalid paid companion travel handoff.");
        }
    }

    /// <summary>Validates service-backed exterior ownership without creating an NPC location or party record.</summary>
    public static class NpcPaidCompanionTravel
    {
        public static bool IsCurrentTravelJob(NpcActionJob job, string contractId, double now)
        {
            if (job == null || NpcActionExecutor.IsTerminal(job) || !Finite(now) ||
                !NpcPaidCompanionAction.IsPaidFollowJob(job) ||
                !string.Equals(NpcPaidCompanionAction.ContractId(job), contractId, StringComparison.Ordinal) ||
                now < job.AcceptedAt || now >= job.FinishAt)
                return false;
            return true;
        }

        public static bool TryReadActiveTravel(WorldStateStore store, NpcActionJob job, double now,
            out NpcServiceContract contract)
        {
            contract = null;
            if (store == null || job == null || !NpcPaidCompanionAction.IsPaidFollowJob(job) ||
                !Finite(now) || now < job.AcceptedAt || now >= job.FinishAt)
                return false;
            string contractId = NpcPaidCompanionAction.ContractId(job);
            contract = store.ReadNpcServiceContracts().Read(contractId);
            return contract != null && contract.Status == NpcServiceContractStatus.Active &&
                contract.NpcId == job.ResidentId &&
                contract.PayerId == NpcDirectedRelationship.PlayerTargetId &&
                (contract.Coverage & NpcServiceCoverage.Travel) != 0 &&
                contract.BlockStartAt == job.AcceptedAt && contract.BlockEndAt == job.FinishAt &&
                now >= contract.BlockStartAt && now < contract.BlockEndAt;
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
