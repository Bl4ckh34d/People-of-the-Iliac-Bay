using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        /// <summary>Atomically records a direct daily payment (or failed-start reversal) and the exact NPC wallet change.</summary>
        public bool TryCommitNpcServiceSettlement(NpcServiceContractWriteTicket ticket,
            NpcServiceContractLedger candidate, NpcInventoryUpdate walletUpdate,
            IList<NpcServiceTransferReceipt> completedNativeTransfers, out string error)
        {
            error = "Invalid or stale daily service payment.";
            if (!IsCurrentNpcServiceContractWriteTicket(ticket) || candidate == null ||
                completedNativeTransfers == null || completedNativeTransfers.Count > 1 ||
                !candidate.IsDirectlyDerivedFrom(ticket.SourceLedger) ||
                !IsSingleContractTransition(ticket.SourceLedger, candidate)) return false;
            if (completedNativeTransfers.Count == 0)
            {
                if (walletUpdate != null) return false;
                return TryCommitNpcServiceContracts(ticket,candidate,completedNativeTransfers,
                    new NpcServiceRenewalAuthorization[0],out error);
            }
            var proof=completedNativeTransfers[0];
            if (proof==null) return false;
            var updated=candidate.Read(proof.ContractId);
            var prior=ticket.SourceLedger.Read(proof.ContractId);
            if (updated==null || updated.TransferReceipts.Count!=(prior==null ? 1 : prior.TransferReceipts.Count+1)) return false;
            int delta;
            if(proof.Kind==NpcServiceTransferKind.Funding && proof.FromOwnerId==updated.PayerId &&
                proof.ToOwnerId==updated.NpcId && proof.Amount==updated.RatePer24GameHours &&
                updated.FundedGold-(prior==null ? 0 : prior.FundedGold)==proof.Amount)
                delta=proof.Amount;
            else if(proof.Kind==NpcServiceTransferKind.Refund && prior!=null &&
                prior.Status==NpcServiceContractStatus.Reserved && updated.Status==NpcServiceContractStatus.Terminated &&
                updated.EndReason==NpcServiceEndReason.CancelledBeforeService &&
                proof.FromOwnerId==updated.NpcId && proof.ToOwnerId==updated.PayerId &&
                proof.Amount==prior.FundedGold && updated.RefundedGold-prior.RefundedGold==proof.Amount)
                delta=-proof.Amount;
            else return false;
            bool alive;
            if(!KnownNpcIdentity(state,updated.NpcId,out alive) || !alive) return false;
            var current=IndexedNpcInventory(updated.NpcId);
            long balance=current==null ? -1 : (long)current.WalletGold+delta;
            if(current==null || walletUpdate==null || walletUpdate.PersonId!=updated.NpcId ||
                walletUpdate.ExpectedRevision!=current.Revision || current.Revision>=NpcInventoryLimits.MaximumRevision ||
                current.LastTransactionId==proof.ReceiptId || balance<0 || balance>NpcInventoryLimits.MaximumWalletGold ||
                walletUpdate.WalletGold!=balance || !SameServiceSettlementItems(current.Items,walletUpdate.Items)) return false;
            var replacement=new NpcInventoryState {PersonId=updated.NpcId,WalletGold=(int)balance,
                Revision=current.Revision+1,LastTransactionId=proof.ReceiptId,Items=walletUpdate.Items};
            try {ValidateNpcInventory(replacement,new HashSet<ulong>());}
            catch(ArgumentException) {return false;}
            int index=state.NpcInventories.IndexOf(current);
            if(index<0)return false;
            var inventories=new List<NpcInventoryState>(state.NpcInventories);
            inventories[index]=CopyNpcInventory(replacement);
            NpcServiceContractLedgerState contracts;
            NpcServiceNativeEvidenceState evidence;
            if(!TryPrepareNpcServiceContractCommit(ticket,candidate,completedNativeTransfers,
                new NpcServiceRenewalAuthorization[0],out contracts,out evidence,out error))return false;
            state.NpcInventories=inventories;
            state.NpcServiceContracts=contracts;
            state.NpcServiceEvidence=evidence;
            npcInventoryIndexOwner=null;
            error=null;
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
