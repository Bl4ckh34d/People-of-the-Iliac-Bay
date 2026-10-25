using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public bool TryDeferPaidTravel(string actionId, long expectedRevision, double now)
        {
            int index = state.NpcActions.FindIndex(job => job.Id == actionId);
            if (index < 0 || expectedRevision == long.MaxValue || !TravelFinite(now)) return false;
            var current = state.NpcActions[index];
            NpcServiceContract contract;
            if (current.Revision != expectedRevision || current.PaidTravelHandoff != null || now < current.UpdatedAt ||
                !NpcPaidCompanionTravel.TryReadActiveTravel(this, current, now, out contract)) return false;
            var next = NpcActionExecutor.Copy(current);
            next.PaidTravelHandoff = new NpcPaidTravelHandoff
            {
                ContractId = contract.ContractId,
                DeferredAt = now,
            };
            next.UpdatedAt = now;
            next.Revision++;
            try { NpcActionExecutor.Validate(next); }
            catch (ArgumentException) { return false; }
            state.NpcActions[index] = next;
            IndexNpcAction(next);
            return true;
        }

        // Runtime calls this only after a destination exterior is ready and the source
        // body is absent. The following actor is created on the same main-thread turn.
        public bool TryResumePaidTravel(string actionId, long expectedRevision, double now)
        {
            int index = state.NpcActions.FindIndex(job => job.Id == actionId);
            if (index < 0 || expectedRevision == long.MaxValue || !TravelFinite(now)) return false;
            var current = state.NpcActions[index];
            var handoff = current.PaidTravelHandoff;
            NpcServiceContract contract;
            if (handoff == null || current.Revision != expectedRevision || now < current.UpdatedAt ||
                !NpcPaidCompanionTravel.TryReadActiveTravel(this, current, now, out contract) ||
                !string.Equals(handoff.ContractId, contract.ContractId, StringComparison.Ordinal)) return false;
            var next = NpcActionExecutor.Copy(current);
            next.PaidTravelHandoff = null;
            next.UpdatedAt = now;
            next.Revision++;
            try { NpcActionExecutor.Validate(next); }
            catch (ArgumentException) { return false; }
            state.NpcActions[index] = next;
            IndexNpcAction(next);
            return true;
        }

        // The road owner may end only an already-expired paid handoff before
        // settling it. This removes no location or routine data; it merely makes
        // the terminal action valid after its exterior body has stopped following.
        public bool TryReleaseExpiredPaidTravel(string actionId, long expectedRevision, double now)
        {
            int index = state.NpcActions.FindIndex(job => job.Id == actionId);
            if (index < 0 || expectedRevision == long.MaxValue || !TravelFinite(now)) return false;
            var current = state.NpcActions[index];
            var handoff = current.PaidTravelHandoff;
            if (handoff == null || current.Revision != expectedRevision || now < current.UpdatedAt ||
                NpcActionExecutor.IsTerminal(current) || !NpcPaidCompanionAction.IsPaidFollowJob(current) ||
                now < current.FinishAt) return false;
            var contract = ReadNpcServiceContracts().Read(handoff.ContractId);
            if (contract == null || contract.Status != NpcServiceContractStatus.Active ||
                contract.NpcId != current.ResidentId || contract.PayerId != NpcDirectedRelationship.PlayerTargetId ||
                contract.BlockStartAt != current.AcceptedAt || contract.BlockEndAt != current.FinishAt ||
                (contract.Coverage & NpcServiceCoverage.Travel) == 0) return false;
            var next = NpcActionExecutor.Copy(current);
            next.PaidTravelHandoff = null;
            next.UpdatedAt = now;
            next.Revision++;
            try { NpcActionExecutor.Validate(next); }
            catch (ArgumentException) { return false; }
            state.NpcActions[index] = next;
            IndexNpcAction(next);
            return true;
        }

        private static bool TravelFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
