using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public PlayerCommerceReceiptLedgerState ReadPlayerCommerceReceipts()
        {
            return state.PlayerCommerceReceipts.Copy();
        }

        // The detached candidate becomes visible through one state reference swap.
        // A failed ledger, owner, revision, wallet or inventory check leaves both
        // the NPC inventory and receipt ledger untouched.
        public bool TryCommitPlayerCommerce(NpcInventoryUpdate update,
            PlayerCommerceReceipt confirmedReceipt, string receiptId, string effectId,
            double committedGameTime)
        {
            if (update == null || confirmedReceipt == null ||
                !PlayerCommerceReceiptLedger.TryRestore(state.PlayerCommerceReceipts,
                    state.NpcFrameworkScopeId, out var ledger)) return false;
            var current = IndexedNpcInventory(update.PersonId);
            if (!confirmedReceipt.MatchesNpcUpdate(current, update) ||
                current.LastTransactionId == confirmedReceipt.TransactionId ||
                current.Revision >= NpcInventoryLimits.MaximumRevision ||
                !ledger.TryCommit(state.NpcFrameworkScopeId, confirmedReceipt, receiptId,
                    effectId, committedGameTime)) return false;
            var proposed = new NpcInventoryState {
                PersonId = update.PersonId,
                WalletGold = update.WalletGold,
                Revision = current.Revision + 1,
                LastTransactionId = confirmedReceipt.TransactionId,
                Items = update.Items
            };
            try { ValidateNpcInventory(proposed, new HashSet<ulong>()); }
            catch (ArgumentException) { return false; }
            foreach (var item in proposed.Items)
            {
                string owner;
                if (npcItemOwners.TryGetValue(item.Uid, out owner) && owner != proposed.PersonId)
                    return false;
            }
            var detachedInventory = CopyNpcInventory(proposed);
            var detachedLedger = ledger.Snapshot();
            if (!PlayerCommerceReceiptLedger.Validate(detachedLedger, state.NpcFrameworkScopeId))
                return false;
            var candidate = state.CandidateForPlayerCommerce();
            int index = candidate.NpcInventories.IndexOf(current);
            if (index < 0) return false;
            candidate.NpcInventories[index] = detachedInventory;
            candidate.PlayerCommerceReceipts = detachedLedger;
            state = candidate;
            return true;
        }

        // Persist reservation before the caller awards native skill experience.
        // Give, Take, refusal and repeat calls cannot reserve Mercantile.
        public bool TryReservePlayerCommerceMercantileTally(string transactionId,
            out PlayerCommerceMercantileTallyReservation reservation)
        {
            reservation = null;
            if (!PlayerCommerceReceiptLedger.TryRestore(state.PlayerCommerceReceipts,
                    state.NpcFrameworkScopeId, out var ledger) ||
                !ledger.TryReserveMercantileTally(state.NpcFrameworkScopeId, transactionId,
                    out var proposedReservation)) return false;
            var next = ledger.Snapshot();
            if (!PlayerCommerceReceiptLedger.Validate(next, state.NpcFrameworkScopeId))
                return false;
            var candidate = state.CandidateForPlayerCommerce();
            candidate.PlayerCommerceReceipts = next;
            state = candidate;
            reservation = proposedReservation;
            return true;
        }
    }
}
