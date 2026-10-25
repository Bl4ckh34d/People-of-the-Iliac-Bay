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
    /// Synchronous native payout of a living NPC's saved service escrow. The player's refund
    /// precedes the atomic Core ledger/evidence/wallet commit and is rolled back on refusal.
    /// Separate DFU and mod save files do not have crash-atomic persistence.
    /// </summary>
    internal static class NativeNpcServiceContractSettlement
    {
        internal delegate bool CommitSettlement(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            NpcServiceContractLedger candidate, NpcInventoryUpdate earningsCredit,
            IList<NpcServiceTransferReceipt> receipts, out string error);

        // Production callers cannot know the exact rounded dues until native time is captured here.
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
                if (commitSettlement == null || reason == NpcServiceEndReason.Death)
                    throw new InvalidOperationException("Death settlement requires a verified persistent estate owner and is unsupported.");
                double now;
                NpcServiceContract contract = Preflight(store, ticket, payer, contractId,
                    expectedContractRevision, out now);
                int earningsDue;
                int refundDue;
                CalculateDues(contract, now, out earningsDue, out refundDue);
                var wage = MakeReceipt(ticket, contract, NpcServiceTransferKind.Earnings,
                    earningsReceiptId, contract.NpcId, earningsDue, now);
                var refund = MakeReceipt(ticket, contract, NpcServiceTransferKind.Refund,
                    refundReceiptId, contract.PayerId, refundDue, now);
                NpcServiceContract updated;
                if (!contract.TrySettle(expectedContractRevision, now, reason, contract.NpcId,
                    wage, refund, out updated) || updated == null)
                    throw new InvalidOperationException("Service contract rejected the requested settlement.");
                NpcServiceContractLedger candidate = Replace(ticket, updated);
                NpcInventoryUpdate earningsCredit = PrepareEarningsCredit(store, contract, earningsDue);
                var receipts = new List<NpcServiceTransferReceipt>(2);
                if (wage != null) receipts.Add(wage);
                if (refund != null) receipts.Add(refund);

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
                earningsReceipt = wage;
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

        internal static bool TryReleaseEarnings(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            PlayerEntity payer, string contractId, long expectedContractRevision, string earningsReceiptId,
            out NpcServiceContractLedger committedLedger, out NpcServiceTransferReceipt earningsReceipt,
            out Exception failure)
        {
            committedLedger = null;
            earningsReceipt = null;
            if (!NativeNpcServiceContractFunding.TryEnterTransfer(out failure)) return false;
            try
            {
                double now;
                NpcServiceContract contract = Preflight(store, ticket, payer, contractId,
                    expectedContractRevision, out now);
                int earningsDue;
                int ignoredRefund;
                CalculateDues(contract, now, out earningsDue, out ignoredRefund);
                if (earningsDue <= 0)
                    throw new InvalidOperationException("No earned service gold is available for release.");
                var wage = MakeReceipt(ticket, contract, NpcServiceTransferKind.Earnings,
                    earningsReceiptId, contract.NpcId, earningsDue, now);
                NpcServiceContract updated;
                if (!contract.TryReleaseEarnings(expectedContractRevision, now, wage, out updated) || updated == null)
                    throw new InvalidOperationException("Service contract rejected earned-gold release.");
                NpcServiceContractLedger candidate = Replace(ticket, updated);
                NpcInventoryUpdate earningsCredit = PrepareEarningsCredit(store, contract, earningsDue);
                string error;
                if (!CommitToStore(store, ticket, candidate, earningsCredit,
                    new[] { wage }, out error))
                    throw new InvalidOperationException("Service earnings commit rejected: " + error);
                committedLedger = candidate;
                earningsReceipt = wage;
                failure = null;
                return true;
            }
            catch (Exception error)
            {
                failure = error;
                return false;
            }
            finally
            {
                NativeNpcServiceContractFunding.ExitTransfer();
            }
        }

        private static NpcServiceContract Preflight(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            PlayerEntity payer, string contractId, long expectedContractRevision, out double now)
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
                !NativeNpcServiceContractFunding.IsKnownLivingNpc(store.Snapshot(), contract.NpcId))
                throw new InvalidOperationException("Service settlement requires the current living NPC contract and payer.");
            return contract;
        }

        private static void CalculateDues(NpcServiceContract contract, double now,
            out int earningsDue, out int refundDue)
        {
            NpcServiceContract preview = contract;
            if (contract.Status == NpcServiceContractStatus.Active &&
                !contract.TryAccrue(contract.Revision, Math.Min(now, contract.BlockEndAt), out preview))
                throw new InvalidOperationException("Service time cannot be accrued for settlement.");
            earningsDue = checked(preview.EarnedGold - preview.ReleasedGold);
            refundDue = checked(preview.EscrowGold - earningsDue);
            if (earningsDue < 0 || refundDue < 0)
                throw new InvalidOperationException("Saved service escrow accounting is invalid.");
        }

        private static NpcServiceTransferReceipt MakeReceipt(NpcServiceContractWriteTicket ticket,
            NpcServiceContract contract, NpcServiceTransferKind kind, string receiptId,
            string recipient, int amount, double now)
        {
            if (amount == 0)
            {
                if (receiptId != null)
                    throw new InvalidOperationException("Zero-gold service transfer must not have a receipt ID.");
                return null;
            }
            if (receiptId == null)
                receiptId = (kind == NpcServiceTransferKind.Earnings ? "service-earnings:" : "service-refund:") +
                    contract.ContractId + ":" + contract.Revision.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(receiptId) || ticket.SourceLedger.HasReceiptId(receiptId))
                throw new InvalidOperationException("Service transfer receipt ID is missing or already used.");
            return NpcServiceTransferReceipt.Create(receiptId, contract.ContractId, kind,
                "contract:" + contract.ContractId + ":escrow", recipient, amount, now);
        }

        private static NpcServiceContractLedger Replace(NpcServiceContractWriteTicket ticket,
            NpcServiceContract updated)
        {
            NpcServiceContractLedger candidate;
            if (!NpcServiceContractLedger.TryReplace(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
                updated, out candidate) || candidate == null)
                throw new InvalidOperationException("Service ledger rejected the settlement candidate.");
            return candidate;
        }

        private static NpcInventoryUpdate PrepareEarningsCredit(WorldStateStore store,
            NpcServiceContract contract, int earningsDue)
        {
            if (earningsDue == 0) return null;
            NpcInventoryState inventory = store.ReadNpcInventory(contract.NpcId);
            if (inventory == null || inventory.Revision >= NpcInventoryLimits.MaximumRevision ||
                (long)inventory.WalletGold + earningsDue > NpcInventoryLimits.MaximumWalletGold)
                throw new InvalidOperationException("NPC wallet is missing, stale, or cannot receive earned gold.");
            return new NpcInventoryUpdate { PersonId = contract.NpcId,
                ExpectedRevision = inventory.Revision, WalletGold = checked(inventory.WalletGold + earningsDue),
                Items = inventory.Items };
        }

        private static bool CommitToStore(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            NpcServiceContractLedger candidate, NpcInventoryUpdate earningsCredit,
            IList<NpcServiceTransferReceipt> receipts, out string error)
        {
            return store.TryCommitNpcServiceSettlement(ticket, candidate, earningsCredit, receipts, out error);
        }
    }
}
