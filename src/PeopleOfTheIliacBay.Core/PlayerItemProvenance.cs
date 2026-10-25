using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Historical source evidence. A legal source alone does not prove uninterrupted current ownership.</summary>
    [Serializable]
    public sealed class PlayerItemProvenanceState
    {
        public int SchemaVersion;
        public string SaveScopeId;
        public List<PlayerItemProvenanceEntry> Entries = new List<PlayerItemProvenanceEntry>();
    }

    [Serializable]
    public sealed class PlayerItemProvenanceEntry
    {
        public PlayerItemProvenanceSourceKind SourceKind;
        public ulong PlayerItemUid, SourceItemUid;
        public string SourceOwnerId, ActionId, SourceReceiptId, TheftReceiptId;
        public int Quantity;
        public long SourceInventoryRevisionAfter;
        public int PaidPrice;
    }

    public enum PlayerItemProvenanceSourceKind { NpcGift = 0, NativeShopPurchase = 1, NativeShopTheft = 2 }

    /// <summary>One exact item from a native trade-window basket after payment or theft transfer.</summary>
    public sealed class PlayerItemProvenanceNativeShopReceipt
    {
        public string ReceiptId, ShopId;
        public ulong PlayerItemUid;
        public int Quantity, BasketQuantityBefore, BasketQuantityAfter;
        public int PlayerQuantityBefore, PlayerQuantityAfter;
        public int Price, PlayerGoldBefore, PlayerGoldAfter;
        public bool NativeTransactionObserved;
    }

    /// <summary>Built only by the synchronous native NPC-to-player inventory adapter.</summary>
    public sealed class PlayerItemProvenanceGiftReceipt
    {
        public string ActionId, TransactionId, SourceOwnerId;
        public ulong SourceItemUid, PlayerItemUid;
        public int Quantity, SourceQuantityBefore, SourceQuantityAfter;
        public int PlayerQuantityBefore, PlayerQuantityAfter;
        public long SourceInventoryRevisionBefore, SourceInventoryRevisionAfter;
        public bool NativeTransferObserved;
    }

    public static class PlayerItemProvenanceLedger
    {
        public const int CurrentSchemaVersion = 2;
        public const int MaximumEntries = 4096;

        public static PlayerItemProvenanceState Create(string saveScopeId)
        {
            if (!Scope(saveScopeId)) throw new ArgumentException("Invalid player item provenance scope.");
            return new PlayerItemProvenanceState { SchemaVersion = CurrentSchemaVersion, SaveScopeId = saveScopeId };
        }

        public static PlayerItemProvenanceState CopyValidated(PlayerItemProvenanceState source, string saveScopeId)
        {
            if (source == null || !Scope(saveScopeId) ||
                (source.SchemaVersion != 1 && source.SchemaVersion != CurrentSchemaVersion) ||
                source.SaveScopeId != saveScopeId || source.Entries == null ||
                source.Entries.Count > MaximumEntries)
                throw new ArgumentException("Invalid player item provenance state.");
            var copy = Create(saveScopeId);
            var uids = new HashSet<ulong>();
            var actions = new HashSet<string>(StringComparer.Ordinal);
            var receipts = new HashSet<string>(StringComparer.Ordinal);
            var thefts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in source.Entries)
            {
                if (entry == null || entry.PlayerItemUid == 0 || entry.SourceItemUid == 0 ||
                    !Id(entry.SourceOwnerId) || entry.SourceOwnerId == "player" || !Id(entry.ActionId) ||
                    entry.Quantity < 1 ||
                    (entry.SourceKind == PlayerItemProvenanceSourceKind.NpcGift &&
                        (entry.SourceReceiptId != entry.ActionId + ":transfer" ||
                         entry.SourceInventoryRevisionAfter < 1 || entry.PaidPrice != 0)) ||
                    (entry.SourceKind == PlayerItemProvenanceSourceKind.NativeShopPurchase &&
                        (source.SchemaVersion == 1 || !NativeShopId(entry.SourceOwnerId) ||
                         entry.SourceItemUid != entry.PlayerItemUid || entry.SourceReceiptId != entry.ActionId ||
                         entry.ActionId != "native-shop-buy:" + entry.PlayerItemUid.ToString(CultureInfo.InvariantCulture) ||
                         entry.SourceInventoryRevisionAfter != 0 || entry.PaidPrice < 1)) ||
                    (entry.SourceKind == PlayerItemProvenanceSourceKind.NativeShopTheft &&
                        (source.SchemaVersion == 1 || !NativeShopId(entry.SourceOwnerId) ||
                         entry.SourceItemUid != entry.PlayerItemUid || entry.SourceReceiptId != entry.ActionId ||
                         entry.ActionId != "native-shop-theft:" + entry.PlayerItemUid.ToString(CultureInfo.InvariantCulture) ||
                         entry.SourceInventoryRevisionAfter != 0 || entry.PaidPrice != 0 ||
                         entry.TheftReceiptId != entry.SourceReceiptId)) ||
                    !Enum.IsDefined(typeof(PlayerItemProvenanceSourceKind), entry.SourceKind) ||
                    entry.TheftReceiptId != null && !Id(entry.TheftReceiptId) ||
                    !uids.Add(entry.PlayerItemUid) || !actions.Add(entry.ActionId) ||
                    !receipts.Add(entry.SourceReceiptId) ||
                    entry.TheftReceiptId != null && !thefts.Add(entry.TheftReceiptId))
                    throw new ArgumentException("Invalid player item provenance entry.");
                copy.Entries.Add(Copy(entry));
            }
            return copy;
        }

        public static bool TryRecordNpcGift(PlayerItemProvenanceState source, string saveScopeId,
            PlayerItemProvenanceGiftReceipt receipt, out PlayerItemProvenanceState updated)
        {
            updated = null;
            PlayerItemProvenanceState copy;
            try { copy = CopyValidated(source, saveScopeId); }
            catch (ArgumentException) { return false; }
            if (receipt == null || !receipt.NativeTransferObserved || !Id(receipt.ActionId) ||
                receipt.TransactionId != receipt.ActionId + ":transfer" ||
                !Id(receipt.SourceOwnerId) || receipt.SourceOwnerId == "player" ||
                receipt.SourceItemUid == 0 || receipt.PlayerItemUid == 0 ||
                receipt.Quantity < 1 || receipt.SourceQuantityBefore < receipt.Quantity ||
                receipt.SourceQuantityAfter != receipt.SourceQuantityBefore - receipt.Quantity ||
                receipt.PlayerQuantityBefore != 0 || receipt.PlayerQuantityAfter != receipt.Quantity ||
                receipt.SourceInventoryRevisionBefore < 0 ||
                receipt.SourceInventoryRevisionAfter != receipt.SourceInventoryRevisionBefore + 1 ||
                copy.Entries.Count >= MaximumEntries) return false;
            foreach (var entry in copy.Entries)
                if (entry.PlayerItemUid == receipt.PlayerItemUid || entry.ActionId == receipt.ActionId ||
                    entry.SourceReceiptId == receipt.TransactionId) return false;
            copy.Entries.Add(new PlayerItemProvenanceEntry {
                PlayerItemUid = receipt.PlayerItemUid, SourceItemUid = receipt.SourceItemUid,
                SourceOwnerId = receipt.SourceOwnerId, ActionId = receipt.ActionId,
                SourceReceiptId = receipt.TransactionId, Quantity = receipt.Quantity,
                SourceInventoryRevisionAfter = receipt.SourceInventoryRevisionAfter });
            updated = copy;
            return true;
        }

        public static bool TryRecordTheft(PlayerItemProvenanceState source, string saveScopeId,
            ulong playerItemUid, string nativeTheftReceiptId, out PlayerItemProvenanceState updated)
        {
            updated = null;
            PlayerItemProvenanceState copy;
            try { copy = CopyValidated(source, saveScopeId); }
            catch (ArgumentException) { return false; }
            if (playerItemUid == 0 || !Id(nativeTheftReceiptId)) return false;
            PlayerItemProvenanceEntry selected = null;
            foreach (var entry in copy.Entries)
            {
                if (entry.TheftReceiptId == nativeTheftReceiptId) return false;
                if (entry.PlayerItemUid == playerItemUid) selected = entry;
            }
            if (selected == null || selected.TheftReceiptId != null) return false;
            selected.TheftReceiptId = nativeTheftReceiptId;
            updated = copy;
            return true;
        }

        public static bool TryRecordNativeShopPurchase(PlayerItemProvenanceState source, string saveScopeId,
            PlayerItemProvenanceNativeShopReceipt receipt, out PlayerItemProvenanceState updated)
        { return TryRecordNativeShop(source, saveScopeId, receipt, false, out updated); }

        public static bool TryRecordNativeShopTheft(PlayerItemProvenanceState source, string saveScopeId,
            PlayerItemProvenanceNativeShopReceipt receipt, out PlayerItemProvenanceState updated)
        { return TryRecordNativeShop(source, saveScopeId, receipt, true, out updated); }

        private static bool TryRecordNativeShop(PlayerItemProvenanceState source, string saveScopeId,
            PlayerItemProvenanceNativeShopReceipt receipt, bool theft, out PlayerItemProvenanceState updated)
        {
            updated = null;
            PlayerItemProvenanceState copy;
            try { copy = CopyValidated(source, saveScopeId); }
            catch (ArgumentException) { return false; }
            if (receipt == null || !receipt.NativeTransactionObserved || !Id(receipt.ReceiptId) ||
                !NativeShopId(receipt.ShopId) ||
                receipt.PlayerItemUid == 0 || receipt.Quantity < 1 ||
                receipt.ReceiptId != (theft ? "native-shop-theft:" : "native-shop-buy:") +
                    receipt.PlayerItemUid.ToString(CultureInfo.InvariantCulture) ||
                receipt.BasketQuantityBefore != receipt.Quantity || receipt.BasketQuantityAfter != 0 ||
                receipt.PlayerQuantityBefore < 0 || receipt.PlayerQuantityAfter != receipt.PlayerQuantityBefore + receipt.Quantity ||
                receipt.PlayerGoldBefore < 0 || receipt.PlayerGoldAfter < 0 ||
                (theft ? receipt.Price != 0 || receipt.PlayerGoldAfter != receipt.PlayerGoldBefore :
                    receipt.Price < 1 || receipt.PlayerGoldBefore - (long)receipt.PlayerGoldAfter != receipt.Price ||
                    receipt.PlayerQuantityBefore != 0) || copy.Entries.Count >= MaximumEntries) return false;
            PlayerItemProvenanceEntry existing = null;
            foreach (var entry in copy.Entries)
            {
                if (entry.ActionId == receipt.ReceiptId || entry.SourceReceiptId == receipt.ReceiptId ||
                    entry.TheftReceiptId == receipt.ReceiptId) return false;
                if (entry.PlayerItemUid == receipt.PlayerItemUid) existing = entry;
            }
            if (existing != null)
            {
                if (!theft || existing.TheftReceiptId != null) return false;
                existing.TheftReceiptId = receipt.ReceiptId;
            }
            else
            {
                copy.Entries.Add(new PlayerItemProvenanceEntry {
                    SourceKind = theft ? PlayerItemProvenanceSourceKind.NativeShopTheft :
                        PlayerItemProvenanceSourceKind.NativeShopPurchase,
                    PlayerItemUid = receipt.PlayerItemUid, SourceItemUid = receipt.PlayerItemUid,
                    SourceOwnerId = receipt.ShopId, ActionId = receipt.ReceiptId,
                    SourceReceiptId = receipt.ReceiptId, TheftReceiptId = theft ? receipt.ReceiptId : null,
                    Quantity = receipt.Quantity, PaidPrice = receipt.Price });
            }
            updated = copy;
            return true;
        }

        public static bool HasLegalSource(PlayerItemProvenanceState source, string saveScopeId, ulong playerItemUid)
        {
            if (playerItemUid == 0) return false;
            PlayerItemProvenanceState copy;
            try { copy = CopyValidated(source, saveScopeId); }
            catch (ArgumentException) { return false; }
            foreach (var entry in copy.Entries)
                if (entry.PlayerItemUid == playerItemUid)
                    return entry.SourceKind != PlayerItemProvenanceSourceKind.NativeShopTheft && entry.TheftReceiptId == null;
            return false;
        }

        /// <summary>Caller must prove current native item membership and continuous ownership since the receipt.</summary>
        public static bool KnownCleanProvenanceNow(PlayerItemProvenanceState source, string saveScopeId,
            ulong playerItemUid, bool exactPlayerItemPresent, bool continuousOwnershipVerified)
        {
            return exactPlayerItemPresent && continuousOwnershipVerified &&
                HasLegalSource(source, saveScopeId, playerItemUid);
        }

        private static PlayerItemProvenanceEntry Copy(PlayerItemProvenanceEntry entry)
        {
            return new PlayerItemProvenanceEntry { SourceKind = entry.SourceKind, PaidPrice = entry.PaidPrice,
                PlayerItemUid = entry.PlayerItemUid,
                SourceItemUid = entry.SourceItemUid, SourceOwnerId = entry.SourceOwnerId,
                ActionId = entry.ActionId, SourceReceiptId = entry.SourceReceiptId,
                TheftReceiptId = entry.TheftReceiptId, Quantity = entry.Quantity,
                SourceInventoryRevisionAfter = entry.SourceInventoryRevisionAfter };
        }
        private static bool Id(string value) { return !string.IsNullOrWhiteSpace(value) && value.Length <= 240; }
        private static bool NativeShopId(string value)
        {
            if (!Id(value) || !value.StartsWith("native-shop:", StringComparison.Ordinal)) return false;
            var parts = value.Split(':');
            int map, building;
            return parts.Length == 3 &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out map) && map >= 0 &&
                int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out building) && building > 0;
        }
        private static bool Scope(string value)
        {
            Guid parsed;
            return value != null && Guid.TryParseExact(value, "N", out parsed);
        }
    }
}
