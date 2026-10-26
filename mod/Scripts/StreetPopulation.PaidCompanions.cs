using System;
using PeopleOfTheIliacBay.Core;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        public const int PaidCompanionDailyGold = 120;
        private float paidCompanionSettlementRetryAt;

        /// <summary>Read-only quote. Caller must obtain a separate explicit native yes/no before hiring.</summary>
        public bool TryQuotePaidCompanion(string residentId, NpcActionDecision decision,
            out int dailyGold, out string reason)
        {
            dailyGold = PaidCompanionDailyGold;
            reason = "Only a willing, available adventurer can take this paid Follow contract.";
            if (!Ready || worldStore == null || population == null || decision == null ||
                decision.Option == null || !decision.Option.PaidCompanion ||
                decision.Option.Kind != NpcActionKind.Follow ||
                decision.Option.CompanionCast != null ||
                decision.Option.Target == null || decision.Option.Target.Kind != NpcActionTargetKind.Person ||
                decision.Option.Target.PersonId != NpcDirectedRelationship.PlayerTargetId ||
                decision.DurationSeconds != NpcPaidCompanionAction.DurationSeconds) return false;
            var actor = FindActor(residentId);
            if (!actor || !actor.gameObject.activeInHierarchy || actor.Resident == null ||
                !actor.Resident.IsAlive || actor.Resident.IsQuestProtected || QuestOwned(actor.Resident) ||
                actor.Resident.AgeGroup != ResidentAgeGroup.Adult || IsAlarmed(residentId) ||
                worldStore.ActiveNpcAction(residentId) != null || worldStore.ActiveNpcTask(residentId) != null)
                return false;
            var archetype = worldStore.ResolveNpcArchetype(residentId,NpcArchetype.Citizen);
            if (archetype != NpcArchetype.Warrior && archetype != NpcArchetype.Mage && archetype != NpcArchetype.Thief) return false;
            var role = worldStore.ReadNpcRole(residentId);
            if (role != null && (role.ServiceBinding == NpcServiceBinding.NobleKingdom ||
                role.CombatRole == NpcCombatRole.Guard || role.CombatRole == NpcCombatRole.PalaceGuard)) return false;
            if (worldStore.ReadNpcInventory(residentId) == null)
            { reason = "The NPC wallet is not available for the agreed wage."; return false; }
            double now = ActionNow();
            if (worldStore.IsResidentAwayDuring(residentId, now, now + NpcServiceContract.BillingPeriodSeconds))
            { reason = "An existing journey takes priority over the proposed day."; return false; }
            var state = worldStore.Snapshot();
            foreach (var appointment in state.Appointments)
                if (appointment.ResidentId == residentId && !appointment.OutcomeRecorded &&
                    appointment.Status != "cancelled" && appointment.StartsAt < now + NpcServiceContract.BillingPeriodSeconds &&
                    AppointmentTiming.ReservedUntil(appointment) > now)
                { reason = "An accepted appointment takes priority over the proposed day."; return false; }
            foreach (var contract in worldStore.ReadNpcServiceContracts().Contracts)
                if (contract.NpcId == residentId && contract.Status != NpcServiceContractStatus.Terminated)
                { reason = "The NPC already has an active or reserved service contract."; return false; }
            var player = GameManager.Instance == null ? null : GameManager.Instance.PlayerEntity;
            if (player == null || player.GetGoldAmount() < dailyGold)
            { reason = "The player lacks the 120 gold needed for one day of service."; return false; }
            reason = "120 gold for one 24-game-hour day of Follow and travel from acceptance. No automatic renewal. " +
                "The started day is non-refundable; dismissal ends service immediately.";
            return true;
        }

        /// <summary>Call synchronously after explicit native player consent and the NPC's accepted Follow decision.</summary>
        public bool TryHirePaidCompanion(string residentId, string proposalId, NpcActionDecision decision,
            NpcConversationCommandTurn commandTurn, out string reason)
        {
            int fee;
            reason = "The player command is no longer current.";
            if (commandTurn == null || !TryQuotePaidCompanion(residentId, decision, out fee, out reason)) return false;
            var actor = FindActor(residentId);
            var player = GameManager.Instance.PlayerEntity;
            var archetype = worldStore.ResolveNpcArchetype(residentId,NpcArchetype.Citizen);
            NpcServiceRole role = archetype == NpcArchetype.Mage ? NpcServiceRole.Mage : NpcServiceRole.Fighter;
            string contractId = "companion:" + Guid.NewGuid().ToString("N");
            TravelerPlan traveler;
            string home = travelerPlans.TryGetValue(residentId,out traveler)
                ?"inn:"+population.SettlementId+":"+traveler.Visit.TavernKey
                :"home:"+population.SettlementId+":"+actor.Resident.HomeKey;
            NpcServiceContractTerms terms;
            if (!NpcServiceContractTerms.TryCreate(contractId,NpcDirectedRelationship.PlayerTargetId,
                residentId,home,role,NpcServiceCoverage.Follow | NpcServiceCoverage.Travel |
                NpcServiceCoverage.Expedition,fee,false,NpcServiceRiskScope.BoundedExpedition,
                new NpcServiceProtectedTarget[0],NpcServiceRestTreatment.RestNotIncluded,
                NpcServiceResupplyResponsibility.SeparatelyNegotiated,new NpcServiceReservedObligation[0],
                id => id == residentId || id == NpcDirectedRelationship.PlayerTargetId,
                id => id == home,(id,category,revision) => false,
                (id,revision,kind,start,end) => false,out terms))
            { reason = "Current native service terms are invalid."; return false; }
            NpcServiceTransferReceipt funding;
            NpcServiceContractLedger funded;
            Exception failure;
            double now = ActionNow();
            if (!NativeNpcServiceContractFunding.TryReserveFromPlayer(worldStore,
                worldStore.CaptureNpcServiceContractWriteTicket(),player,terms,now,contractId+":fund",
                out funding,out funded,out failure))
            { reason = "The player payment did not complete: " + FailureText(failure); return false; }
            string acceptanceReason;
            // The ordinary adapter only prepares the movement owner. The funded
            // service binding below replaces its short window with the fixed day.
            var physicalFollow = new NpcActionDecision {DurationSeconds=300,
                Option=new NpcActionOption {Id=decision.Option.Id,Kind=NpcActionKind.Follow,
                    Label=decision.Option.Label,Target=NpcActionExecutor.CopyTarget(decision.Option.Target)}};
            if (!TryAcceptConversationAction(residentId,proposalId,physicalFollow,commandTurn,out acceptanceReason))
            {
                reason = "Follow was not accepted: " + acceptanceReason;
                return RefundFailedHire(contractId,player,false,reason,out reason);
            }
            var action = worldStore.ActiveNpcAction(residentId);
            if (action == null || action.Kind != NpcActionKind.Follow || action.ProposalId != proposalId)
            {
                reason = "Accepted Follow action changed before service started.";
                return RefundFailedHire(contractId,player,true,reason,out reason);
            }
            // Starting the paid day and binding the physical job are one store operation.
            // A failed binding leaves the reservation fully refundable.
            if (!worldStore.TryBindPaidFollowAction(action.Id,action.Revision,contractId,out reason))
                return RefundFailedHire(contractId,player,true,
                    "The prepaid service could not bind its accepted Follow action.",out reason);
            reason = "Paid Follow accepted and saved for one 24-game-hour block. Movement begins after conversation closes.";
            return true;
        }

        /// <summary>Idempotent terminal settlement. Caller maps physical action outcome to service end reason.</summary>
        public bool TrySettlePaidCompanionAction(NpcActionJob job,NpcServiceEndReason endReason,out string error)
        {
            error = null;
            string contractId = NpcPaidCompanionAction.ContractId(job);
            if (contractId == null) return true;
            if (worldStore == null || !NpcActionExecutor.IsTerminal(job) ||
                !NpcPaidCompanionAction.IsPaidFollowJob(job))
            { error = "Only a terminal paid Follow action can settle its contract."; return false; }
            if (!worldStore.TrySuspendPaidFollowSettlement(job.Id,job.Revision,out error)) return false;
            var ticket = worldStore.CaptureNpcServiceContractWriteTicket();
            var contract = ticket.SourceLedger.Read(contractId);
            if (contract == null) { error = "Paid Follow contract is missing."; return false; }
            if (contract.Status == NpcServiceContractStatus.Terminated) return true;
            if (contract.NpcId != job.ResidentId ||
                (contract.Status != NpcServiceContractStatus.Active &&
                 contract.Status != NpcServiceContractStatus.Suspended))
            { error = "Paid Follow contract is not active for this actor."; return false; }
            if (ActionNow() >= contract.BlockEndAt) endReason = NpcServiceEndReason.NaturalExpiry;
            NpcServiceContractLedger settled;
            NpcServiceTransferReceipt earnings,refund;
            Exception failure;
            var player = GameManager.Instance == null ? null : GameManager.Instance.PlayerEntity;
            if (!NativeNpcServiceContractSettlement.TrySettle(worldStore,ticket,
                player,contractId,contract.Revision,endReason,
                out settled,out earnings,out refund,out failure))
            { error = "Paid Follow settlement failed: " + FailureText(failure); return false; }
            return true;
        }

        // Called by the normal action update. Failed native transfers keep their saved
        // suspension and retry even after the physical action has returned to its routine.
        private void RetryPaidCompanionSettlements()
        {
            if (worldStore == null || Time.unscaledTime < paidCompanionSettlementRetryAt) return;
            paidCompanionSettlementRetryAt = Time.unscaledTime + 5;
            foreach (var job in worldStore.PendingPaidFollowSettlements())
            {
                string error;
                var reason = job.Phase == NpcActionPhase.Cancelled
                    ? NpcServiceEndReason.PlayerDismissal : NpcServiceEndReason.NpcResigned;
                if (!TrySettlePaidCompanionAction(job,reason,out error))
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_SETTLEMENT_RETRY: " + error);
            }
        }

        private bool RefundFailedHire(string contractId,DaggerfallWorkshop.Game.Entity.PlayerEntity player,
            bool stopAction,string original,out string reason)
        {
            if (stopAction)
            {
                string ignored;
                var currentContract = worldStore.ReadNpcServiceContracts().Read(contractId);
                var active = currentContract == null ? null : worldStore.ActiveNpcAction(currentContract.NpcId);
                if (active != null && !TryStopConversationAction(currentContract.NpcId,out ignored))
                { reason = original + " Action cancellation failed; prepaid service remains held."; return false; }
            }
            var ticket = worldStore.CaptureNpcServiceContractWriteTicket();
            var contract = ticket.SourceLedger.Read(contractId);
            if (contract == null || contract.Status == NpcServiceContractStatus.Terminated)
            { reason = original; return false; }
            var end = contract.Status == NpcServiceContractStatus.Reserved
                ? NpcServiceEndReason.CancelledBeforeService : NpcServiceEndReason.PlayerDismissal;
            NpcServiceContractLedger settled;
            NpcServiceTransferReceipt earnings,refund;
            Exception failure;
            if (!NativeNpcServiceContractSettlement.TrySettle(worldStore,ticket,player,contractId,
                contract.Revision,end,out settled,out earnings,out refund,out failure))
                reason = original + " The prepaid contract remains reserved; refund failed: " + FailureText(failure);
            else reason = original + " Player payment was refunded through the native service ledger.";
            return false;
        }

        private static string FailureText(Exception failure)
        { return failure == null ? "unknown native transfer refusal" : failure.Message; }
    }
}
