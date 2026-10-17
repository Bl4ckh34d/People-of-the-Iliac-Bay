using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Banking;
using HarmonyLib;

namespace LivingCities.QuestHooks
{
    /// <summary>Adapts saved Living Cities sale listings to DFU's native bank flow.</summary>
    public sealed class NativeBankHouseHooks : IDisposable
    {
        private const string Owner = "livingcities.bank-house-listings";
        private static NativeBankHouseHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<bool> enabled;
        private readonly Func<BuildingDirectory, List<BuildingSummary>, List<BuildingSummary>> reconcileListings;
        private readonly Action<BuildingSummary, int, TransactionResult> purchased;

        public NativeBankHouseHooks(Func<bool> enabled,
            Func<BuildingDirectory, List<BuildingSummary>, List<BuildingSummary>> reconcileListings,
            Action<BuildingSummary, int, TransactionResult> purchased)
        {
            if (enabled == null) throw new ArgumentNullException(nameof(enabled));
            if (reconcileListings == null) throw new ArgumentNullException(nameof(reconcileListings));
            if (purchased == null) throw new ArgumentNullException(nameof(purchased));
            if (active != null) throw new InvalidOperationException("Bank house hooks are already installed.");
            this.enabled = enabled;
            this.reconcileListings = reconcileListings;
            this.purchased = purchased;
            active = this;
            try
            {
                var getListings = AccessTools.Method(typeof(BuildingDirectory), "GetHousesForSale");
                var purchaseHouse = AccessTools.Method(typeof(DaggerfallBankManager), "PurchaseHouse",
                    new[] { typeof(BuildingSummary), typeof(int) });
                if (getListings == null || purchaseHouse == null)
                    throw new MissingMethodException("DFU bank house API changed.");
                harmony.Patch(getListings, postfix: new HarmonyMethod(
                    typeof(NativeBankHouseHooks).GetMethod(nameof(AfterGetHousesForSale), AccessTools.all)));
                harmony.Patch(purchaseHouse, postfix: new HarmonyMethod(
                    typeof(NativeBankHouseHooks).GetMethod(nameof(AfterPurchaseHouse), AccessTools.all)));
            }
            catch
            {
                active = null;
                harmony.UnpatchAll(Owner);
                throw;
            }
        }

        private static void AfterGetHousesForSale(BuildingDirectory __instance, ref List<BuildingSummary> __result)
        {
            var self = active;
            if (self == null || !self.IsEnabled()) return;
            try { __result = self.reconcileListings(__instance, __result); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("LIVING_CITIES_BANK_LISTING_HOOK_FAILED: " + error); }
        }

        private static void AfterPurchaseHouse(BuildingSummary __0, int __1, TransactionResult __result)
        {
            var self = active;
            if (self == null || !self.IsEnabled() || __result != TransactionResult.PURCHASED_HOUSE) return;
            try { self.purchased(__0, __1, __result); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("LIVING_CITIES_BANK_PURCHASE_HOOK_FAILED: " + error); }
        }

        private bool IsEnabled()
        {
            try { return enabled(); }
            catch { return false; }
        }

        public void Dispose()
        {
            if (active == this) active = null;
            harmony.UnpatchAll(Owner);
        }
    }
}
