using System;
using System.Collections.Generic;
using System.Threading;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Trusted synchronous seam for funding a typed service reservation from the real player purse.
    /// This protects against ordinary refusal, stale writes, and exceptions in one main-thread call.
    /// Player save data and mod save data still have separate crash boundaries.
    /// </summary>
    internal static class NativeNpcServiceContractFunding
    {
        internal delegate bool CommitReservation(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            NpcServiceContractLedger candidate, NpcServiceTransferReceipt receipt, out string error);

        internal sealed class CreditEntry
        {
            internal DaggerfallUnityItem Item;
            internal ulong Uid;
            internal int Value;
        }

        internal sealed class CurrencySnapshot
        {
            internal int GoldPieces;
            internal int TotalGoldAmount;
            internal List<CreditEntry> Credits;
        }

        private static int mainThreadId;
        private static int operationActive;

        internal static void BindMainThread()
        {
            int current = Thread.CurrentThread.ManagedThreadId;
            int previous = Interlocked.CompareExchange(ref mainThreadId, current, 0);
            if (previous != 0 && previous != current)
                throw new InvalidOperationException("Service funding main thread was already bound to another thread.");
        }

        internal static bool TryEnterTransfer(out Exception failure)
        {
            failure = null;
            int currentMainThread = Interlocked.CompareExchange(ref mainThreadId, 0, 0);
            if (currentMainThread == 0 || Thread.CurrentThread.ManagedThreadId != currentMainThread)
            {
                failure = new InvalidOperationException("Service transfer must run on the bound Unity main thread.");
                return false;
            }
            if (Interlocked.CompareExchange(ref operationActive, 1, 0) != 0)
            {
                failure = new InvalidOperationException("A service transfer transaction is already active.");
                return false;
            }
            return true;
        }

        internal static void ExitTransfer()
        {
            Interlocked.Exchange(ref operationActive, 0);
        }

        /// <summary>
        /// Reserve one funded period. Terms must come from trusted native validation; model text is never accepted here.
        /// Keep this operation synchronous. Never yield between debit, ledger commit, and rollback.
        /// </summary>
        internal static bool TryReserveFromPlayer(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            PlayerEntity payer, NpcServiceContractTerms terms, double gameTime, string receiptId,
            out NpcServiceTransferReceipt committedReceipt, out NpcServiceContractLedger committedCandidate,
            out Exception failure)
        {
            return TryReserveFromPlayerWithCommitter(store, ticket, payer, terms, gameTime, receiptId,
                CommitToStore, out committedReceipt, out committedCandidate, out failure);
        }

        // Internal seam lets the owned native probe force a post-debit store refusal through the same rollback path.
        internal static bool TryReserveFromPlayerWithCommitter(WorldStateStore store,
            NpcServiceContractWriteTicket ticket, PlayerEntity payer, NpcServiceContractTerms terms,
            double gameTime, string receiptId, CommitReservation commitReservation,
            out NpcServiceTransferReceipt committedReceipt, out NpcServiceContractLedger committedCandidate,
            out Exception failure)
        {
            committedReceipt = null;
            committedCandidate = null;
            failure = null;
            if (!TryEnterTransfer(out failure))
                return false;

            CurrencySnapshot currency = null;
            bool debitAttempted = false;
            bool ledgerCommitted = false;
            try
            {
                if (store == null || ticket == null || payer == null || terms == null || commitReservation == null ||
                    GameManager.Instance == null || !ReferenceEquals(payer, GameManager.Instance.PlayerEntity))
                    throw new InvalidOperationException("Funding requires the current native player and save store.");
                if (terms.PayerId != NpcDirectedRelationship.PlayerTargetId ||
                    string.IsNullOrWhiteSpace(terms.NpcId) || terms.NpcId == NpcDirectedRelationship.PlayerTargetId ||
                    string.IsNullOrWhiteSpace(terms.HomeReference) || terms.RatePer24GameHours <= 0 ||
                    double.IsNaN(gameTime) || double.IsInfinity(gameTime) || gameTime < 0)
                    throw new InvalidOperationException("Funding terms do not name a valid player-paid contract.");
                if (!IsKnownLivingNpc(store.Snapshot(), terms.NpcId))
                    throw new InvalidOperationException("Service terms do not identify a current living NPC.");

                NpcServiceContractWriteTicket currentTicket = store.CaptureNpcServiceContractWriteTicket();
                if (!store.IsCurrentNpcServiceContractWriteTicket(ticket) ||
                    !store.IsCurrentNpcServiceContractWriteTicket(currentTicket) ||
                    ticket.ExpectedLedgerRevision != currentTicket.ExpectedLedgerRevision ||
                    ticket.SourceLedger == null || ticket.SourceLedger.Version != NpcServiceContractLedger.VersionCurrent ||
                    ticket.SourceLedger.Revision != ticket.ExpectedLedgerRevision ||
                    currentTicket.SourceLedger.Revision != currentTicket.ExpectedLedgerRevision)
                    throw new InvalidOperationException("Service funding ticket is stale or malformed.");
                if (ticket.SourceLedger.Read(terms.ContractId) != null || ticket.SourceLedger.HasReceiptId(receiptId))
                    throw new InvalidOperationException("Service contract or funding receipt already exists.");

                NpcServiceTransferReceipt funding = NpcServiceTransferReceipt.Create(receiptId, terms.ContractId,
                    NpcServiceTransferKind.Funding, terms.PayerId,
                    terms.NpcId, terms.RatePer24GameHours, gameTime);
                NpcServiceContractLedger candidate;
                if (!NpcServiceContractLedger.TryReserve(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
                    terms, funding, out candidate) || candidate == null)
                    throw new InvalidOperationException("Service ledger rejected funding reservation before debit.");

                int available = payer.GetGoldAmount();
                if (available < terms.RatePer24GameHours)
                    throw new InvalidOperationException("Player lacks the gold and letters of credit required for this contract.");
                currency = CaptureCurrency(payer);
                int expectedGoldAfterDebit = checked(available - terms.RatePer24GameHours);
                debitAttempted = true;
                int remainder = payer.DeductGoldAmount(terms.RatePer24GameHours);
                if (remainder != 0 || payer.GetGoldAmount() != expectedGoldAfterDebit)
                    throw new InvalidOperationException("Native player debit did not match the agreed contract amount.");

                string commitError;
                if (!commitReservation(store, ticket, candidate, funding, out commitError))
                    throw new InvalidOperationException("Service reservation commit rejected after debit: " + commitError);

                ledgerCommitted = true;
                committedReceipt = funding;
                committedCandidate = candidate;
                return true;
            }
            catch (Exception error)
            {
                failure = error;
                if (debitAttempted && !ledgerCommitted && currency != null)
                {
                    try { RestoreCurrency(payer, currency); }
                    catch (Exception rollbackError)
                    {
                        failure = new AggregateException("Service funding failed and exact currency rollback also failed.",
                            error, rollbackError);
                    }
                }
                return false;
            }
            finally
            {
                ExitTransfer();
            }
        }

        private static bool CommitToStore(WorldStateStore store, NpcServiceContractWriteTicket ticket,
            NpcServiceContractLedger candidate, NpcServiceTransferReceipt receipt, out string error)
        {
            var inventory=store.ReadNpcInventory(receipt.ToOwnerId);
            if(inventory==null || (long)inventory.WalletGold+receipt.Amount>NpcInventoryLimits.MaximumWalletGold)
            {error="Companion wallet cannot receive the daily payment.";return false;}
            var credit=new NpcInventoryUpdate {PersonId=inventory.PersonId,ExpectedRevision=inventory.Revision,
                WalletGold=inventory.WalletGold+receipt.Amount,Items=inventory.Items};
            return store.TryCommitNpcServiceSettlement(ticket,candidate,credit,new[]{receipt},out error);
        }

        internal static bool IsKnownLivingNpc(WorldState state, string personId)
        {
            if (state == null || string.IsNullOrWhiteSpace(personId)) return false;
            if (state.NativePeople != null && state.NativePeople.Exists(person => person != null &&
                person.Id == personId && person.IsAlive)) return true;
            if (state.Settlements != null)
            {
                foreach (SettlementPopulation settlement in state.Settlements)
                {
                    if (settlement == null || settlement.Households == null) continue;
                    foreach (Household household in settlement.Households)
                    {
                        if (household == null || household.Residents == null) continue;
                        if (household.Residents.Exists(resident => resident != null && resident.Id == personId && resident.IsAlive))
                            return true;
                    }
                }
            }
            return state.Travelers != null && state.Travelers.Exists(traveler => traveler != null &&
                traveler.Id == personId && traveler.IsAlive);
        }

        internal static CurrencySnapshot CaptureCurrency(PlayerEntity player)
        {
            DaggerfallUnity unity = DaggerfallUnity.Instance;
            if (!unity || unity.ItemHelper == null || player.Items == null)
                throw new InvalidOperationException("Native player inventory is unavailable for currency capture.");
            int creditGroup = unity.ItemHelper.GetGroupIndex(ItemGroups.MiscItems, (int)MiscItems.Letter_of_credit);
            var snapshot = new CurrencySnapshot { GoldPieces = player.GoldPieces,
                TotalGoldAmount = player.GetGoldAmount(), Credits = new List<CreditEntry>() };
            var ids = new HashSet<ulong>();
            for (int i = 0; i < player.Items.Count; i++)
            {
                DaggerfallUnityItem item = player.Items.GetItem(i);
                if (item != null && item.ItemGroup == ItemGroups.MiscItems && item.GroupIndex == creditGroup)
                {
                    if (!ids.Add(item.UID)) throw new InvalidOperationException("Duplicate LOC identity in player inventory.");
                    snapshot.Credits.Add(new CreditEntry { Item = item, Uid = item.UID, Value = item.value });
                }
            }
            return snapshot;
        }

        internal static void RestoreCurrency(PlayerEntity player, CurrencySnapshot snapshot)
        {
            if (player == null || snapshot == null || player.Items == null)
                throw new InvalidOperationException("Native player inventory unavailable during currency rollback.");
            player.GoldPieces = snapshot.GoldPieces;
            foreach (CreditEntry credit in snapshot.Credits)
            {
                credit.Item.value = credit.Value;
                DaggerfallUnityItem current = FindByUid(player.Items, credit.Uid);
                if (current == null) player.Items.AddItem(credit.Item, noStack: true);
                else if (!ReferenceEquals(current, credit.Item))
                    throw new InvalidOperationException("LOC UID was taken by a different item during currency rollback.");
                if (credit.Item.UID != credit.Uid || !ReferenceEquals(FindByUid(player.Items, credit.Uid), credit.Item))
                    throw new InvalidOperationException("Could not restore exact LOC item ownership and identity.");
            }
            foreach (CreditEntry credit in snapshot.Credits)
                if (credit.Item.value != credit.Value)
                    throw new InvalidOperationException("Could not restore exact LOC value.");
            if (player.GoldPieces != snapshot.GoldPieces)
                throw new InvalidOperationException("Could not restore exact carried gold amount.");
            if (player.GetGoldAmount() != snapshot.TotalGoldAmount)
                throw new InvalidOperationException("Could not restore exact combined gold and LOC value.");
        }

        internal static void VerifyCreditedCurrency(PlayerEntity player, CurrencySnapshot snapshot, int credit)
        {
            if (player == null || snapshot == null || player.Items == null || credit <= 0 ||
                player.GoldPieces != checked(snapshot.GoldPieces + credit) ||
                player.GetGoldAmount() != checked(snapshot.TotalGoldAmount + credit))
                throw new InvalidOperationException("Native player refund did not match the agreed contract amount.");
            foreach (CreditEntry entry in snapshot.Credits)
            {
                if (entry.Item.UID != entry.Uid || entry.Item.value != entry.Value ||
                    !ReferenceEquals(FindByUid(player.Items, entry.Uid), entry.Item))
                    throw new InvalidOperationException("Native player refund changed a letter of credit.");
            }
        }

        private static DaggerfallUnityItem FindByUid(ItemCollection items, ulong uid)
        {
            for (int i = 0; i < items.Count; i++)
            {
                DaggerfallUnityItem item = items.GetItem(i);
                if (item != null && item.UID == uid) return item;
            }
            return null;
        }
    }
}
