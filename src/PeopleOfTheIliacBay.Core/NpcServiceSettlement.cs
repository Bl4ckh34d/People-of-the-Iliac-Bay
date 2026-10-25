using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        /// <summary>
        /// Trusted native-adapter boundary for a completed living-NPC wage/refund transfer.
        /// Receipt objects validate shape and exact candidate history; they do not themselves
        /// prove that the native player refund happened. The adapter must credit and verify
        /// the real player before this call, and roll that credit back on rejection.
        /// </summary>
        public bool TryCommitNpcServiceSettlement(NpcServiceContractWriteTicket ticket,
            NpcServiceContractLedger candidate, NpcInventoryUpdate earningsCredit,
            IList<NpcServiceTransferReceipt> completedNativeTransfers, out string error)
        {
            error = "Invalid or stale service settlement.";
            if (!IsCurrentNpcServiceContractWriteTicket(ticket) || candidate == null ||
                completedNativeTransfers == null || completedNativeTransfers.Count > 2 ||
                !candidate.IsDirectlyDerivedFrom(ticket.SourceLedger) ||
                !IsSingleContractTransition(ticket.SourceLedger, candidate)) return false;

            NpcServiceContract prior = null;
            NpcServiceContract updated = null;
            foreach (var oldContract in ticket.SourceLedger.Contracts)
            {
                var next = candidate.Read(oldContract.ContractId);
                if (next != null && !oldContract.HasSameSavedState(next))
                { prior = oldContract; updated = next; break; }
            }
            if (prior == null || updated == null || updated.EndReason == NpcServiceEndReason.Death ||
                updated.Status != NpcServiceContractStatus.Terminated &&
                !(prior.Status == NpcServiceContractStatus.Active &&
                    updated.Status == NpcServiceContractStatus.Active)) return false;

            int earnings = updated.ReleasedGold - prior.ReleasedGold;
            int refund = updated.RefundedGold - prior.RefundedGold;
            if (earnings < 0 || refund < 0 ||
                updated.FundedGold != prior.FundedGold ||
                (long)prior.EscrowGold - earnings - refund != updated.EscrowGold ||
                updated.TransferReceipts.Count != prior.TransferReceipts.Count +
                    (earnings > 0 ? 1 : 0) + (refund > 0 ? 1 : 0) ||
                updated.Status != NpcServiceContractStatus.Terminated &&
                    (earnings == 0 || refund != 0)) return false;

            bool alive;
            if (!KnownNpcIdentity(state, prior.NpcId, out alive) || !alive) return false;
            NpcServiceTransferReceipt wageProof = null;
            NpcServiceTransferReceipt refundProof = null;
            foreach (var proof in completedNativeTransfers)
            {
                if (proof == null || proof.ContractId != prior.ContractId ||
                    proof.FromOwnerId != "contract:" + prior.ContractId + ":escrow") return false;
                if (proof.Kind == NpcServiceTransferKind.Earnings && wageProof == null &&
                    proof.ToOwnerId == prior.NpcId && proof.Amount == earnings)
                    wageProof = proof;
                else if (proof.Kind == NpcServiceTransferKind.Refund && refundProof == null &&
                    proof.ToOwnerId == prior.PayerId && proof.Amount == refund)
                    refundProof = proof;
                else return false;
            }
            if ((earnings > 0) != (wageProof != null) ||
                (refund > 0) != (refundProof != null)) return false;

            List<NpcInventoryState> nextInventories = null;
            if (earnings > 0)
            {
                NpcInventoryState current = IndexedNpcInventory(prior.NpcId);
                if (current == null || earningsCredit == null ||
                    earningsCredit.PersonId != prior.NpcId ||
                    earningsCredit.ExpectedRevision != current.Revision ||
                    current.Revision >= NpcInventoryLimits.MaximumRevision ||
                    current.LastTransactionId == wageProof.ReceiptId ||
                    (long)current.WalletGold + earnings > NpcInventoryLimits.MaximumWalletGold ||
                    earningsCredit.WalletGold != current.WalletGold + earnings ||
                    !SameServiceSettlementItems(current.Items, earningsCredit.Items)) return false;
                var replacement = new NpcInventoryState {
                    PersonId = prior.NpcId, WalletGold = earningsCredit.WalletGold,
                    Revision = current.Revision + 1, LastTransactionId = wageProof.ReceiptId,
                    Items = earningsCredit.Items
                };
                try { ValidateNpcInventory(replacement, new HashSet<ulong>()); }
                catch (ArgumentException) { return false; }
                int index = state.NpcInventories.IndexOf(current);
                if (index < 0) return false;
                nextInventories = new List<NpcInventoryState>(state.NpcInventories);
                nextInventories[index] = CopyNpcInventory(replacement);
            }
            else if (earningsCredit != null) return false;

            NpcServiceContractLedgerState nextContracts;
            NpcServiceNativeEvidenceState nextEvidence;
            if (!TryPrepareNpcServiceContractCommit(ticket, candidate, completedNativeTransfers,
                new NpcServiceRenewalAuthorization[0], out nextContracts, out nextEvidence, out error))
                return false;

            // All validation and copying ended. These replacements cannot throw or reject;
            // no intermediate state is exposed to a caller on this synchronous thread.
            if (nextInventories != null) state.NpcInventories = nextInventories;
            state.NpcServiceEvidence = nextEvidence;
            state.NpcServiceContracts = nextContracts;
            if (nextInventories != null) npcInventoryIndexOwner = null;
            error = null;
            return true;
        }

        private static bool SameServiceSettlementItems(IList<NpcInventoryItem> current,
            IList<NpcInventoryItem> proposed)
        {
            if (current == null || proposed == null || current.Count != proposed.Count) return false;
            for (int i = 0; i < current.Count; i++)
                if (current[i] == null || proposed[i] == null ||
                    current[i].Uid != proposed[i].Uid ||
                    current[i].NativePayload != proposed[i].NativePayload) return false;
            return true;
        }
    }
}
