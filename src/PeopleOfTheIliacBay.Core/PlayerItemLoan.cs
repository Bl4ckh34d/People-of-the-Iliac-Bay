using System;

namespace PeopleOfTheIliacBay.Core
{
    // The player borrows through a zero-gold Take receipt and returns the
    // retained native UID and exact quantity through a Give receipt.
    public sealed class PlayerItemLoanView
    {
        public string OriginReceiptId { get; internal set; }
        public string NpcId { get; internal set; }
        public ulong RetainedItemUid { get; internal set; }
        public int Quantity { get; internal set; }
        public bool IsOpen { get; internal set; }
    }

    public static class PlayerItemLoanPolicy
    {
        public const int MinimumReliabilityTrust = 75;

        public static bool AdmitsNewLoan(NpcDirectedRelationshipView relationship)
        {
            return relationship != null && AdmitsNewLoan(relationship.TrustKnown,
                relationship.ReliabilityTrust);
        }

        public static bool AdmitsNewLoan(bool trustKnown, int reliabilityTrust)
        {
            return trustKnown && reliabilityTrust >= MinimumReliabilityTrust;
        }

        public static bool TryReadOpenLoan(PlayerCommerceReceiptLedgerState ledger, string npcId,
            string originReceiptId, ulong retainedItemUid, int quantity, out PlayerItemLoanView loan)
        {
            loan = null;
            if (ledger == null || ledger.Entries == null || string.IsNullOrWhiteSpace(npcId) ||
                string.IsNullOrWhiteSpace(originReceiptId) || retainedItemUid == 0 || quantity < 1)
                return false;
            PlayerCommerceReceiptLedgerEntry origin = null;
            foreach (var entry in ledger.Entries)
                if (entry != null && entry.ReceiptId == originReceiptId) { origin = entry; break; }
            if (origin == null || origin.LoanPurpose != LoanPurpose.Lend || origin.NpcId != npcId ||
                origin.RecipientItemUid != retainedItemUid || origin.Quantity != quantity)
                return false;
            foreach (var entry in ledger.Entries)
                if (entry != null && entry.LoanPurpose == LoanPurpose.Return &&
                    entry.OriginReceiptId == originReceiptId) return false;
            loan = new PlayerItemLoanView {
                OriginReceiptId = origin.ReceiptId, NpcId = origin.NpcId,
                RetainedItemUid = origin.RecipientItemUid, Quantity = origin.Quantity, IsOpen = true
            };
            return true;
        }

        // Native UI has an observed player-held UID and quantity, not a receipt id.
        // UID ownership is exact, so one open origin may match that concrete item.
        public static bool TryFindOpenLoan(PlayerCommerceReceiptLedgerState ledger, string npcId,
            ulong retainedItemUid, int quantity, out PlayerItemLoanView loan)
        {
            loan = null;
            if (ledger == null || ledger.Entries == null || string.IsNullOrWhiteSpace(npcId) ||
                retainedItemUid == 0 || quantity < 1) return false;
            foreach (var entry in ledger.Entries)
            {
                if (entry == null || entry.LoanPurpose != LoanPurpose.Lend || entry.NpcId != npcId ||
                    entry.RecipientItemUid != retainedItemUid || entry.Quantity != quantity) continue;
                if (TryReadOpenLoan(ledger, npcId, entry.ReceiptId, retainedItemUid, quantity, out loan))
                    return true;
            }
            return false;
        }
    }

    public sealed partial class WorldStateStore
    {
        public bool CanBeginPlayerItemLoan(string npcId)
        {
            return PlayerItemLoanPolicy.AdmitsNewLoan(ReadNpcRelationship(npcId,
                NpcDirectedRelationship.PlayerTargetId));
        }

        // Returning a recorded loan intentionally does not re-check trust.
        public bool TryReadOpenPlayerItemLoan(string npcId, string originReceiptId,
            ulong retainedItemUid, int quantity, out PlayerItemLoanView loan)
        {
            return PlayerItemLoanPolicy.TryReadOpenLoan(state.PlayerCommerceReceipts, npcId,
                originReceiptId, retainedItemUid, quantity, out loan);
        }

        public bool TryFindOpenPlayerItemLoan(string npcId, ulong retainedItemUid, int quantity,
            out PlayerItemLoanView loan)
        {
            return PlayerItemLoanPolicy.TryFindOpenLoan(state.PlayerCommerceReceipts, npcId,
                retainedItemUid, quantity, out loan);
        }
    }
}
