using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    // One NPC-owned item sold to the current open native shop. This does not
    // add a merchant ledger, stock simulator, or multi-item sale surface.
    public static class NpcShopLootSale
    {
        public const string ServiceCode = "shop-owned-loot-sale";
        public const string TargetContainerId = "native-shop-sale";

        // A sale is to the current merchant, not to a persisted shelf target.
        // Runtime supplies only shelves already verified as current native stock.
        public static bool TrySelectMerchantShelfLoadId(IEnumerable<ulong> verifiedLoadIds, out ulong loadId)
        {
            loadId = 0;
            if (verifiedLoadIds == null) return false;
            foreach (ulong candidate in verifiedLoadIds)
                if (candidate != 0 && (loadId == 0 || candidate < loadId)) loadId = candidate;
            return loadId != 0;
        }

        // Mirrors DFU FormulaHelper.CalculateTradePrice(cost, quality, true),
        // but uses the selling NPC's persisted Mercantile and Personality.
        public static bool TryPrice(int nativeCost, int quality, int mercantile, int personality, out int price)
        {
            price = 0;
            if (nativeCost < 1 || quality < 0 || quality > 20 || mercantile < 0 || mercantile > 100 ||
                personality < 0 || personality > 100)
                return false;
            long merchant = 5L * (quality - 10) + 50;
            long mercantileDelta = (((100 - merchant) << 8) / 200 + 128) * ((mercantile << 8) / 200 + 128) >> 8;
            long personalityDelta = (((100 - merchant) << 8) / 200 + 128) * ((personality << 8) / 200 + 128) >> 8;
            long total = (((179 * mercantileDelta >> 8) + (51 * personalityDelta >> 8)) * nativeCost) >> 8;
            if (total < 1 || total > NpcInventoryLimits.MaximumWalletGold)
                return false;
            price = (int)total;
            return true;
        }

        public static void ValidateService(NpcActionJob value)
        {
            ulong item;
            if (value == null || value.Kind != NpcActionKind.Purchase || value.ServiceCode != ServiceCode ||
                value.ServicePrice < 1 || value.ServicePrice > NpcInventoryLimits.MaximumWalletGold || value.ServiceStep != 0 ||
                value.Target == null || value.Target.Kind != NpcActionTargetKind.IndoorPoint ||
                value.Target.QuotedGold != value.ServicePrice || value.Target.ContainerId != TargetContainerId ||
                string.IsNullOrWhiteSpace(value.ResidentId) || string.IsNullOrWhiteSpace(value.Target.PersonId) ||
                value.Target.PersonId == value.ResidentId || value.Target.PersonId.Length > 240 ||
                !ulong.TryParse(value.Target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out item) || item == 0 ||
                value.Target.Quantity != 1)
                throw new ArgumentException("Invalid NPC owned-loot sale.");
        }
    }
}
