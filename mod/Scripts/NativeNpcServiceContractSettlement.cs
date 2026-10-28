using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Ends service without another wage transfer. A failed start reverses the original
    /// direct payment; the player's credit rolls back if the NPC wallet/ledger commit refuses.
    /// Separate DFU and mod save files do not have crash-atomic persistence.
    /// </summary>
    internal static class NativeNpcServiceContractSettlement
    {
        internal delegate bool CommitSettlement(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            NpcServiceContractLedger candidate, NpcInventoryUpdate earningsCredit,
            IList<NpcServiceTransferReceipt> receipts, out string error);

        // Native time determines expiry and the failed-start reversal timestamp.
        internal static bool TrySettle(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            PlayerEntity payer, string contractId, long expectedContractRevision, NpcServiceEndReason reason,
            out NpcServiceContractLedger committedLedger, out NpcServiceTransferReceipt earningsReceipt,
            out NpcServiceTransferReceipt refundReceipt, out Exception failure)
        {
            return TrySettleWithCommitter(store, ticket, payer, contractId, expectedContractRevision, reason,
                null, null, CommitToStore,
                out committedLedger, out earningsReceipt, out refundReceipt, out failure);
        }

        internal static bool TrySettle(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            PlayerEntity payer, string contractId, long expectedContractRevision, NpcServiceEndReason reason,
            string earningsReceiptId, string refundReceiptId,
            out NpcServiceContractLedger committedLedger, out NpcServiceTransferReceipt earningsReceipt,
            out NpcServiceTransferReceipt refundReceipt, out Exception failure)
        {
            return TrySettleWithCommitter(store, ticket, payer, contractId, expectedContractRevision, reason,
                earningsReceiptId, refundReceiptId, CommitToStore,
                out committedLedger, out earningsReceipt, out refundReceipt, out failure);
        }

        // Probe seam: force rejection after the actual player refund to verify exact native rollback.
        internal static bool TrySettleWithCommitter(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            PlayerEntity payer, string contractId, long expectedContractRevision, NpcServiceEndReason reason,
            string earningsReceiptId, string refundReceiptId, CommitSettlement commitSettlement,
            out NpcServiceContractLedger committedLedger, out NpcServiceTransferReceipt earningsReceipt,
            out NpcServiceTransferReceipt refundReceipt, out Exception failure)
        {
            committedLedger = null;
            earningsReceipt = null;
            refundReceipt = null;
            if (!NativeNpcServiceContractFunding.TryEnterTransfer(out failure)) return false;

            NativeNpcServiceContractFunding.CurrencySnapshot currency = null;
            bool refundAttempted = false;
            bool committed = false;
            try
            {
                if (commitSettlement == null || earningsReceiptId != null)
                    throw new InvalidOperationException("Service termination requires no additional earnings transfer.");
                double now;
                NpcServiceContract contract = Preflight(store, ticket, payer, contractId,
                    expectedContractRevision, reason==NpcServiceEndReason.Death, out now);
                int refundDue=contract.Status==NpcServiceContractStatus.Reserved ? contract.FundedGold : 0;
                var refund=refundDue==0 ? null : NpcServiceTransferReceipt.Create(
                    refundReceiptId ?? contract.ContractId+":rollback",contract.ContractId,NpcServiceTransferKind.Refund,
                    contract.NpcId,contract.PayerId,refundDue,now);
                NpcServiceContract updated;
                if(!contract.TrySettle(expectedContractRevision,now,reason,contract.NpcId,null,refund,out updated))
                    throw new InvalidOperationException("Service contract rejected termination.");
                NpcServiceContractLedger candidate=Replace(ticket,updated);
                NpcInventoryUpdate earningsCredit=null;
                if(refundDue>0)
                {
                    var inventory=store.ReadNpcInventory(contract.NpcId);
                    if(inventory==null || inventory.WalletGold<refundDue)
                        throw new InvalidOperationException("Companion wallet cannot reverse the failed-start payment.");
                    earningsCredit=new NpcInventoryUpdate {PersonId=contract.NpcId,ExpectedRevision=inventory.Revision,
                        WalletGold=inventory.WalletGold-refundDue,Items=inventory.Items};
                }
                var receipts=new List<NpcServiceTransferReceipt>(1);
                if(refund!=null)receipts.Add(refund);

                if (refundDue > 0)
                {
                    currency = NativeNpcServiceContractFunding.CaptureCurrency(payer);
                    int expectedCoins = checked(currency.GoldPieces + refundDue);
                    int expectedTotal = checked(currency.TotalGoldAmount + refundDue);
                    if (expectedTotal < expectedCoins)
                        throw new InvalidOperationException("Native combined gold is inconsistent with carried coins.");
                    refundAttempted = true;
                    payer.GoldPieces = expectedCoins;
                    NativeNpcServiceContractFunding.VerifyCreditedCurrency(payer, currency, refundDue);
                }

                string error;
                if (!commitSettlement(store, ticket, candidate, earningsCredit, receipts, out error))
                    throw new InvalidOperationException("Service settlement commit rejected after native refund: " + error);
                committed = true;
                committedLedger = candidate;
                earningsReceipt = null;
                refundReceipt = refund;
                failure = null;
                return true;
            }
            catch (Exception error)
            {
                failure = error;
                if (refundAttempted && !committed && currency != null)
                {
                    try { NativeNpcServiceContractFunding.RestoreCurrency(payer, currency); }
                    catch (Exception rollbackError)
                    {
                        failure = new AggregateException("Service settlement failed and exact currency rollback also failed.",
                            error, rollbackError);
                    }
                }
                return false;
            }
            finally
            {
                NativeNpcServiceContractFunding.ExitTransfer();
            }
        }

        private static NpcServiceContract Preflight(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            PlayerEntity payer, string contractId, long expectedContractRevision, bool allowDead, out double now)
        {
            now = -1;
            if (store == null || ticket == null || payer == null ||
                GameManager.Instance == null || !ReferenceEquals(payer, GameManager.Instance.PlayerEntity) ||
                DaggerfallUnity.Instance == null)
                throw new InvalidOperationException("Settlement requires the current native player, clock, and save store.");
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            now = clock.ToSeconds() + (clock.Second - Math.Floor(clock.Second));
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 ||
                !store.IsCurrentNpcServiceContractWriteTicket(ticket) ||
                ticket.SourceLedger == null || ticket.SourceLedger.Revision != ticket.ExpectedLedgerRevision ||
                !store.IsCurrentNpcServiceContractWriteTicket(store.CaptureNpcServiceContractWriteTicket()))
                throw new InvalidOperationException("Service settlement clock or write ticket is stale.");
            NpcServiceContract contract = ticket.SourceLedger.Read(contractId);
            if (contract == null || contract.Revision != expectedContractRevision ||
                contract.Status == NpcServiceContractStatus.Terminated ||
                contract.PayerId != NpcDirectedRelationship.PlayerTargetId ||
                !allowDead && !NativeNpcServiceContractFunding.IsKnownLivingNpc(store.Snapshot(), contract.NpcId))
                throw new InvalidOperationException("Service settlement requires the current living NPC contract and payer.");
            return contract;
        }

        private static NpcServiceContractLedger Replace(NpcServiceContractWriteTicket ticket,NpcServiceContract updated)
        {
            NpcServiceContractLedger candidate;
            if(!NpcServiceContractLedger.TryReplace(ticket.SourceLedger,ticket.ExpectedLedgerRevision,updated,out candidate))
                throw new InvalidOperationException("Service ledger rejected termination.");
            return candidate;
        }

        private static bool CommitToStore(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            NpcServiceContractLedger candidate, NpcInventoryUpdate earningsCredit,
            IList<NpcServiceTransferReceipt> receipts, out string error)
        {
            return store.TryCommitNpcServiceSettlement(ticket, candidate, earningsCredit, receipts, out error);
        }
    }
}
