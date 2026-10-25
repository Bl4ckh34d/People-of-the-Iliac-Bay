using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Exact link between a prepaid, one-day service contract and its physical Follow action.</summary>
    public static class NpcPaidCompanionAction
    {
        public const string ServicePrefix = "paid-follow:";
        public const int DurationSeconds = (int)NpcServiceContract.BillingPeriodSeconds;
        public const double ReturnAllowanceSeconds = 60;

        public static bool ValidDecisionDuration(NpcActionOption option,int seconds)
        { return option != null && (option.PaidCompanion ? seconds == DurationSeconds : seconds >= 10 && seconds <= 900); }

        public static string ContractId(NpcActionJob job)
        {
            if (job == null || job.ServiceCode == null ||
                !job.ServiceCode.StartsWith(ServicePrefix, StringComparison.Ordinal)) return null;
            string id = job.ServiceCode.Substring(ServicePrefix.Length);
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128) return null;
            for (int i = 0; i < id.Length; i++) if (char.IsControl(id[i])) return null;
            return id;
        }

        public static bool IsPaidFollowJob(NpcActionJob job)
        {
            return ContractId(job) != null && job.Kind == NpcActionKind.Follow &&
                job.Target != null && job.Target.Kind == NpcActionTargetKind.Person &&
                job.Target.PersonId == NpcDirectedRelationship.PlayerTargetId &&
                job.ServicePrice == 0 && job.ServiceStep == 0 &&
                job.FinishAt == job.AcceptedAt + NpcServiceContract.BillingPeriodSeconds &&
                job.DeadlineAt == job.FinishAt + ReturnAllowanceSeconds;
        }
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>Stop billing before native settlement, so a refused payout can retry without charging for stopped service.</summary>
        public bool TrySuspendPaidFollowSettlement(string actionId,long expectedActionRevision,out string reason)
        {
            reason = "The terminal paid Follow action is no longer current.";
            var job = ReadNpcAction(actionId);
            if (job == null || job.Revision != expectedActionRevision ||
                !NpcActionExecutor.IsTerminal(job) || !NpcPaidCompanionAction.IsPaidFollowJob(job)) return false;
            var ticket = CaptureNpcServiceContractWriteTicket();
            var contract = ticket.SourceLedger.Read(NpcPaidCompanionAction.ContractId(job));
            if (contract == null || contract.NpcId != job.ResidentId ||
                contract.PayerId != NpcDirectedRelationship.PlayerTargetId ||
                contract.BlockStartAt != job.AcceptedAt || contract.BlockEndAt != job.FinishAt) return false;
            if (contract.Status == NpcServiceContractStatus.Terminated ||
                contract.Status == NpcServiceContractStatus.Suspended)
            { reason = null; return true; }
            NpcServiceContract paused;
            NpcServiceContractLedger candidate;
            if (!contract.TrySuspend(contract.Revision,Math.Min(job.UpdatedAt,contract.BlockEndAt),out paused) ||
                !NpcServiceContractLedger.TryReplace(ticket.SourceLedger,ticket.ExpectedLedgerRevision,paused,out candidate))
                return false;
            return TryCommitNpcServiceContracts(ticket,candidate,new NpcServiceTransferReceipt[0],
                new NpcServiceRenewalAuthorization[0],out reason);
        }

        /// <summary>Terminal paid jobs remain discoverable after their physical return has finished.</summary>
        public List<NpcActionJob> PendingPaidFollowSettlements()
        {
            var result = new List<NpcActionJob>();
            var ledger = ReadNpcServiceContracts();
            foreach (var job in state.NpcActions)
            {
                if (!NpcActionExecutor.IsTerminal(job) || !NpcPaidCompanionAction.IsPaidFollowJob(job)) continue;
                var contract = ledger.Read(NpcPaidCompanionAction.ContractId(job));
                if (contract != null && contract.Status != NpcServiceContractStatus.Terminated)
                    result.Add(NpcActionExecutor.Copy(job));
            }
            return result;
        }

        /// <summary>Bind only the freshly accepted Follow job to the already funded and active first block.</summary>
        public bool TryBindPaidFollowAction(string actionId, long expectedActionRevision,
            string contractId, out string reason)
        {
            reason = "The paid Follow action or service contract is no longer current.";
            if (string.IsNullOrWhiteSpace(actionId) || string.IsNullOrWhiteSpace(contractId)) return false;
            int index = state.NpcActions.FindIndex(item => item.Id == actionId);
            if (index < 0) return false;
            NpcActionJob current = state.NpcActions[index];
            if (current.Revision != expectedActionRevision || current.Revision == long.MaxValue ||
                current.Phase != NpcActionPhase.Accepted || current.Kind != NpcActionKind.Follow ||
                current.ServiceCode != null || current.Target == null ||
                current.Target.Kind != NpcActionTargetKind.Person ||
                current.Target.PersonId != NpcDirectedRelationship.PlayerTargetId ||
                current.UpdatedAt != current.AcceptedAt) return false;
            NpcServiceContract contract = ReadNpcServiceContracts().Read(contractId);
            if (contract == null || contract.Status != NpcServiceContractStatus.Active ||
                contract.NpcId != current.ResidentId ||
                contract.PayerId != NpcDirectedRelationship.PlayerTargetId ||
                (contract.Coverage & NpcServiceCoverage.Follow) == 0 ||
                contract.StandingRenewalPermission || contract.BlockStartAt != current.AcceptedAt ||
                contract.BlockEndAt != contract.BlockStartAt + NpcServiceContract.BillingPeriodSeconds ||
                contract.FundedGold != contract.RatePer24GameHours) return false;
            NpcActionJob bound = NpcActionExecutor.Copy(current);
            bound.ServiceCode = NpcPaidCompanionAction.ServicePrefix + contractId;
            bound.FinishAt = contract.BlockEndAt;
            bound.DeadlineAt = bound.FinishAt + NpcPaidCompanionAction.ReturnAllowanceSeconds;
            bound.Revision++;
            try { NpcActionExecutor.Validate(bound); }
            catch (ArgumentException) { return false; }
            state.NpcActions[index] = bound;
            IndexNpcAction(bound);
            reason = "Paid Follow accepted for one 24-game-hour block.";
            return true;
        }
    }
}
