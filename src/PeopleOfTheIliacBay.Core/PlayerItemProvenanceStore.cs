using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        /// <summary>Historical native shop outcome; caller must be the post-commit trade hook.</summary>
        public bool TryRecordObservedNativeShopItem(PlayerItemProvenanceNativeShopReceipt receipt, bool stolen)
        {
            if (receipt == null || state.PlayerItemProvenance == null ||
                NpcInventoryItemOwner(receipt.PlayerItemUid) != null) return false;
            PlayerItemProvenanceState updated;
            bool accepted = stolen
                ? PlayerItemProvenanceLedger.TryRecordNativeShopTheft(state.PlayerItemProvenance,
                    NpcFrameworkScopeId, receipt, out updated)
                : PlayerItemProvenanceLedger.TryRecordNativeShopPurchase(state.PlayerItemProvenance,
                    NpcFrameworkScopeId, receipt, out updated);
            if (!accepted) return false;
            state.PlayerItemProvenance = updated;
            return true;
        }

        /// <summary>Record one native NPC-to-player transfer while its exact action is executing.</summary>
        public bool TryRecordObservedNpcGiftPlayerItem(PlayerItemProvenanceGiftReceipt receipt)
        {
            if (receipt == null || state.PlayerItemProvenance == null) return false;
            var action = ReadNpcAction(receipt.ActionId);
            ulong requestedUid;
            if (action == null || action.Phase != NpcActionPhase.Executing ||
                action.Kind != NpcActionKind.Transfer || action.Target == null ||
                action.Target.Kind != NpcActionTargetKind.Person || action.Target.PersonId != "player" ||
                action.Target.TransferDirection != NpcActionTransferDirection.NpcToCounterparty ||
                action.ResidentId != receipt.SourceOwnerId || action.Target.Quantity != receipt.Quantity ||
                !ulong.TryParse(action.Target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture,
                    out requestedUid) || requestedUid != receipt.SourceItemUid ||
                NpcInventoryItemOwner(receipt.PlayerItemUid) != null) return false;
            var source = ReadNpcInventory(receipt.SourceOwnerId);
            if (source == null || source.Revision != receipt.SourceInventoryRevisionAfter ||
                source.LastTransactionId != receipt.TransactionId) return false;
            PlayerItemProvenanceState updated;
            if (!PlayerItemProvenanceLedger.TryRecordNpcGift(state.PlayerItemProvenance,
                NpcFrameworkScopeId, receipt, out updated)) return false;
            state.PlayerItemProvenance = updated;
            return true;
        }

        /// <summary>Historical source only. This is insufficient for personal-trade authorization.</summary>
        public bool HasLegalPlayerItemSource(ulong uid)
        { return PlayerItemProvenanceLedger.HasLegalSource(state.PlayerItemProvenance, NpcFrameworkScopeId, uid); }

        /// <summary>Known theft evidence only; missing history does not establish theft.</summary>
        public bool IsKnownStolenPlayerItem(ulong uid)
        {
            if (uid == 0 || state.PlayerItemProvenance == null) return false;
            foreach (var entry in state.PlayerItemProvenance.Entries)
                if (entry.PlayerItemUid == uid)
                    return entry.SourceKind == PlayerItemProvenanceSourceKind.NativeShopTheft || entry.TheftReceiptId != null;
            return false;
        }

        /// <summary>Fails closed until native caller proves uninterrupted current player ownership.</summary>
        public bool IsKnownCleanPlayerItemNow(ulong uid, bool exactNativePlayerItemPresent,
            bool continuousOwnershipVerified)
        {
            return NpcInventoryItemOwner(uid) == null &&
                PlayerItemProvenanceLedger.KnownCleanProvenanceNow(state.PlayerItemProvenance,
                    NpcFrameworkScopeId, uid, exactNativePlayerItemPresent, continuousOwnershipVerified);
        }
    }
}
