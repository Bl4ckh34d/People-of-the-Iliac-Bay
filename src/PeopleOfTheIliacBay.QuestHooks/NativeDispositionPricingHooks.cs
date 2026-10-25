using System;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using HarmonyLib;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;

namespace PeopleOfTheIliacBay.QuestHooks
{
    /// <summary>Applies saved personal disposition to native trade and paid room pricing.</summary>
    public sealed class NativeDispositionPricingHooks : IDisposable
    {
        private const string Owner = "peopleoftheiliacbay.disposition-prices";
        private static NativeDispositionPricingHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<StaticNPC, int, int> adjustShopBuy;
        private readonly Func<int, int> adjustShelfShopBuy;
        private readonly Func<StaticNPC, int, int> adjustSalePrice;
        private readonly Func<StaticNPC, int, int> adjustServicePrice;
        private readonly Func<StaticNPC, bool> canServe;
        private readonly Func<bool> canShelfServe;
        private readonly Func<StaticNPC, int, int> adjustRoomRental;
        private readonly Func<string, int, int> paidRoomDays;
        private readonly Func<bool> isEnabled;
        private static RoomQuoteContext activeRoomQuote;

        private sealed class FoodQuoteContext
        {
            public byte[] Prices;
            public int Index;
            public byte OriginalPrice;
        }

        private sealed class RoomQuoteContext
        {
            public DaggerfallTavernWindow Window;
            public StaticNPC Keeper;
            public int Days;
            public int Cost;
            public int Quality;
        }

        public NativeDispositionPricingHooks(Func<StaticNPC, int, int> adjustShopBuy,
            Func<int, int> adjustShelfShopBuy,
            Func<StaticNPC, int, int> adjustSalePrice,
            Func<StaticNPC, int, int> adjustServicePrice,
            Func<StaticNPC, bool> canServe,
            Func<bool> canShelfServe,
            Func<StaticNPC, int, int> adjustRoomRental, Func<string, int, int> paidRoomDays)
            : this(adjustShopBuy, adjustShelfShopBuy, adjustSalePrice, adjustServicePrice,
                canServe, canShelfServe, adjustRoomRental, paidRoomDays, () => true)
        {
        }

        public NativeDispositionPricingHooks(Func<StaticNPC, int, int> adjustShopBuy,
            Func<int, int> adjustShelfShopBuy,
            Func<StaticNPC, int, int> adjustSalePrice,
            Func<StaticNPC, int, int> adjustServicePrice,
            Func<StaticNPC, bool> canServe,
            Func<bool> canShelfServe,
            Func<StaticNPC, int, int> adjustRoomRental, Func<string, int, int> paidRoomDays,
            Func<bool> isEnabled)
        {
            if (adjustShopBuy == null) throw new ArgumentNullException(nameof(adjustShopBuy));
            if (adjustShelfShopBuy == null) throw new ArgumentNullException(nameof(adjustShelfShopBuy));
            if (adjustSalePrice == null) throw new ArgumentNullException(nameof(adjustSalePrice));
            if (adjustServicePrice == null) throw new ArgumentNullException(nameof(adjustServicePrice));
            if (canServe == null) throw new ArgumentNullException(nameof(canServe));
            if (canShelfServe == null) throw new ArgumentNullException(nameof(canShelfServe));
            if (adjustRoomRental == null) throw new ArgumentNullException(nameof(adjustRoomRental));
            if (paidRoomDays == null) throw new ArgumentNullException(nameof(paidRoomDays));
            if (isEnabled == null) throw new ArgumentNullException(nameof(isEnabled));
            if (active != null) throw new InvalidOperationException("Disposition price hooks already installed.");
            this.adjustShopBuy = adjustShopBuy;
            this.adjustShelfShopBuy = adjustShelfShopBuy;
            this.adjustSalePrice = adjustSalePrice;
            this.adjustServicePrice = adjustServicePrice;
            this.canServe = canServe;
            this.canShelfServe = canShelfServe;
            this.adjustRoomRental = adjustRoomRental;
            this.paidRoomDays = paidRoomDays;
            this.isEnabled = isEnabled;
            active = this;
            try
            {
                var tradePrice = AccessTools.Method(typeof(DaggerfallTradeWindow), "GetTradePrice");
                var roomQuote = AccessTools.Method(typeof(DaggerfallTavernWindow), "InputMessageBox_OnGotUserInput");
                var confirmTrade = AccessTools.Method(typeof(DaggerfallTradeWindow), "ConfirmTrade_OnButtonClick");
                var confirmRoom = AccessTools.Method(typeof(DaggerfallTavernWindow), "ConfirmRenting_OnButtonClick");
                var foodPurchase = AccessTools.Method(typeof(DaggerfallTavernWindow), "FoodAndDrink_OnItemPicked");
                var formulaPrice = AccessTools.Method(typeof(FormulaHelper), nameof(FormulaHelper.CalculateTradePrice),
                    new[] { typeof(int), typeof(int), typeof(bool) });
                if (tradePrice == null || roomQuote == null || confirmTrade == null || confirmRoom == null || foodPurchase == null || formulaPrice == null)
                    throw new MissingMethodException("DFU purchase pricing API changed.");
                harmony.Patch(tradePrice, postfix: new HarmonyMethod(typeof(NativeDispositionPricingHooks)
                    .GetMethod(nameof(AfterShopTradePrice), AccessTools.all)));
                harmony.Patch(roomQuote,
                    prefix: new HarmonyMethod(typeof(NativeDispositionPricingHooks)
                        .GetMethod(nameof(BeforeRoomQuote), AccessTools.all)),
                    finalizer: new HarmonyMethod(typeof(NativeDispositionPricingHooks)
                        .GetMethod(nameof(AfterRoomQuote), AccessTools.all)));
                var tradeConfirm = new HarmonyMethod(typeof(NativeDispositionPricingHooks)
                    .GetMethod(nameof(BeforeTradeConfirm), AccessTools.all)) { priority = Priority.First };
                harmony.Patch(confirmTrade, prefix: tradeConfirm);
                var roomConfirm = new HarmonyMethod(typeof(NativeDispositionPricingHooks)
                    .GetMethod(nameof(BeforeRoomConfirm), AccessTools.all)) { priority = Priority.First };
                harmony.Patch(confirmRoom, prefix: roomConfirm);
                harmony.Patch(foodPurchase,
                    prefix: new HarmonyMethod(typeof(NativeDispositionPricingHooks)
                        .GetMethod(nameof(BeforeFoodPurchase), AccessTools.all)),
                    finalizer: new HarmonyMethod(typeof(NativeDispositionPricingHooks)
                        .GetMethod(nameof(AfterFoodPurchase), AccessTools.all)));
                harmony.Patch(formulaPrice, postfix: new HarmonyMethod(typeof(NativeDispositionPricingHooks)
                    .GetMethod(nameof(AfterNativeRoomTradePrice), AccessTools.all)));
            }
            catch
            {
                active = null;
                harmony.UnpatchAll(Owner);
                throw;
            }
        }

        private static void AfterShopTradePrice(DaggerfallTradeWindow __instance, ref int __result)
        {
            var self = active;
            if (self == null || !self.isEnabled() || __instance == null || !IsActiveWindowOrPopupChain(__instance)) return;
            try
            {
                var owner = ResolveTradeServiceOwner(__instance);
                var modeProperty = AccessTools.Property(typeof(DaggerfallTradeWindow), "WindowMode");
                if (modeProperty == null) return;
                var mode = (DaggerfallTradeWindow.WindowModes)modeProperty.GetValue(__instance, null);
                switch (mode)
                {
                    case DaggerfallTradeWindow.WindowModes.Buy:
                        if (owner)
                            __result = self.adjustShopBuy(owner, __result);
                        else if (__instance.PreviousWindow == null)
                            __result = self.adjustShelfShopBuy(__result);
                        break;
                    case DaggerfallTradeWindow.WindowModes.Sell:
                    case DaggerfallTradeWindow.WindowModes.SellMagic:
                        if (owner) __result = self.adjustSalePrice(owner, __result);
                        break;
                    case DaggerfallTradeWindow.WindowModes.Repair:
                    case DaggerfallTradeWindow.WindowModes.Identify:
                        if (owner) __result = self.adjustServicePrice(owner, __result);
                        break;
                }
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_BUY_PRICE_HOOK_FAILED: " + error);
            }
        }

        private static bool BeforeTradeConfirm(DaggerfallTradeWindow __instance,
            DaggerfallMessageBox.MessageBoxButtons __1)
        {
            var self = active;
            if (self == null || !self.isEnabled() || __instance == null || __1 != DaggerfallMessageBox.MessageBoxButtons.Yes ||
                !IsActiveWindowOrPopupChain(__instance)) return true;
            try
            {
                var modeProperty = AccessTools.Property(typeof(DaggerfallTradeWindow), "WindowMode");
                if (modeProperty == null) return true;
                var mode = (DaggerfallTradeWindow.WindowModes)modeProperty.GetValue(__instance, null);
                if (mode != DaggerfallTradeWindow.WindowModes.Buy && mode != DaggerfallTradeWindow.WindowModes.Sell &&
                    mode != DaggerfallTradeWindow.WindowModes.SellMagic && mode != DaggerfallTradeWindow.WindowModes.Repair &&
                    mode != DaggerfallTradeWindow.WindowModes.Identify) return true;

                var owner = ResolveTradeServiceOwner(__instance);
                bool allowed = owner ? self.canServe(owner) :
                    mode == DaggerfallTradeWindow.WindowModes.Buy && __instance.PreviousWindow == null && self.canShelfServe();
                if (allowed) return true;

                __instance.CloseWindow();
                DaggerfallUI.MessageBox("The keeper refuses to do business with you.");
                return false;
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_TRADE_REFUSAL_FAILED: " + error);
                return true;
            }
        }

        private static bool BeforeRoomConfirm(DaggerfallTavernWindow __instance,
            DaggerfallMessageBox.MessageBoxButtons __1, StaticNPC ___merchantNPC, int ___tradePrice)
        {
            var self = active;
            if (self == null || !self.isEnabled() || __instance == null || __1 != DaggerfallMessageBox.MessageBoxButtons.Yes ||
                ___tradePrice <= 0 || !___merchantNPC) return true;
            try
            {
                if (self.canServe(___merchantNPC)) return true;
                __instance.CloseWindow();
                DaggerfallUI.MessageBox("The keeper refuses to rent you a room.");
                return false;
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_ROOM_REFUSAL_FAILED: " + error);
                return true;
            }
        }

        private static bool BeforeFoodPurchase(DaggerfallTavernWindow __instance, int index,
            StaticNPC ___merchantNPC, byte[] ___tavernFoodAndDrinkPrices, out FoodQuoteContext __state)
        {
            __state = null;
            var self = active;
            if (self == null || !self.isEnabled() || __instance == null || !___merchantNPC || ___tavernFoodAndDrinkPrices == null ||
                index < 0 || index >= ___tavernFoodAndDrinkPrices.Length) return true;
            try
            {
                uint gameMinutes = DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToClassicDaggerfallTime();
                int holiday = FormulaHelper.GetHolidayId(gameMinutes, 0);
                if (holiday == (int)DFLocation.Holidays.New_Life) return true; // Native free purchase stays free.
                if (!self.canServe(___merchantNPC))
                {
                    __instance.CloseWindow();
                    DaggerfallUI.MessageBox("The keeper refuses to serve you.");
                    return false;
                }

                int adjusted = self.adjustServicePrice(___merchantNPC, ___tavernFoodAndDrinkPrices[index]);
                adjusted = Math.Max(1, Math.Min(byte.MaxValue, adjusted));
                __state = new FoodQuoteContext
                {
                    Prices = ___tavernFoodAndDrinkPrices,
                    Index = index,
                    OriginalPrice = ___tavernFoodAndDrinkPrices[index],
                };
                ___tavernFoodAndDrinkPrices[index] = (byte)adjusted;
                return true;
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_TAVERN_SERVICE_FAILED: " + error);
                return true;
            }
        }

        private static Exception AfterFoodPurchase(Exception __exception, FoodQuoteContext __state)
        {
            if (__state != null && __state.Prices != null && __state.Index >= 0 && __state.Index < __state.Prices.Length)
                __state.Prices[__state.Index] = __state.OriginalPrice;
            return __exception;
        }

        private static void BeforeRoomQuote(DaggerfallTavernWindow __instance, string input,
            StaticNPC ___merchantNPC, RoomRental_v1 ___rentedRoom, out RoomQuoteContext __state)
        {
            var self = active;
            __state = activeRoomQuote;
            activeRoomQuote = null;
            if (self == null || !self.isEnabled() || __instance == null || !___merchantNPC || GameManager.Instance == null)
                return;
            try
            {
                int daysAlreadyRented = 0;
                if (___rentedRoom != null)
                {
                    daysAlreadyRented = (int)((___rentedRoom.expiryTime - DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()) /
                        DaggerfallDateTime.SecondsPerDay);
                    if (daysAlreadyRented < 0) daysAlreadyRented = 0;
                }
                int daysToRent = self.paidRoomDays(input, daysAlreadyRented);
                if (daysToRent < 1) return;

                var order = GameManager.Instance.GuildManager.GetGuild(FactionFile.GuildGroups.KnightlyOrder);
                if (order != null && order.FreeTavernRooms()) return;
                activeRoomQuote = new RoomQuoteContext
                {
                    Window = __instance,
                    Keeper = ___merchantNPC,
                    Days = daysToRent,
                    Cost = FormulaHelper.CalculateRoomCost(daysToRent),
                    Quality = GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.quality,
                };
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_ROOM_PRICE_HOOK_FAILED: " + error);
            }
        }

        private static Exception AfterRoomQuote(Exception __exception, RoomQuoteContext __state)
        {
            activeRoomQuote = __state;
            return __exception;
        }

        private static void AfterNativeRoomTradePrice(int cost, int shopQuality, bool selling, ref int __result)
        {
            var self = active;
            var quote = activeRoomQuote;
            if (self == null || !self.isEnabled() || quote == null || selling || cost != quote.Cost || shopQuality != quote.Quality || __result <= 0)
                return;
            try
            {
                __result = self.adjustRoomRental(quote.Keeper, __result);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_ROOM_PRICE_HOOK_FAILED: " + error);
            }
        }

        private static bool IsActiveWindowOrPopupChain(DaggerfallTradeWindow tradeWindow)
        {
            IUserInterfaceWindow window = DaggerfallUI.UIManager == null ? null : DaggerfallUI.UIManager.TopWindow;
            for (int depth = 0; window != null && depth < 8; depth++)
            {
                if (ReferenceEquals(window, tradeWindow)) return true;
                var popup = window as DaggerfallPopupWindow;
                if (popup == null) return false;
                window = popup.PreviousWindow;
            }
            return false;
        }

        private static StaticNPC ResolveTradeServiceOwner(DaggerfallTradeWindow tradeWindow)
        {
            var previous = tradeWindow.PreviousWindow;
            var merchant = previous as DaggerfallMerchantServicePopupWindow;
            if (merchant != null)
                return (StaticNPC)AccessTools.Field(typeof(DaggerfallMerchantServicePopupWindow), "merchantNPC").GetValue(merchant);

            var repair = previous as DaggerfallMerchantRepairPopupWindow;
            if (repair != null)
                return (StaticNPC)AccessTools.Field(typeof(DaggerfallMerchantRepairPopupWindow), "merchantNPC").GetValue(repair);

            var guild = previous as DaggerfallGuildServicePopupWindow;
            if (guild != null)
                return (StaticNPC)AccessTools.Field(typeof(DaggerfallGuildServicePopupWindow), "serviceNPC").GetValue(guild);

            return null;
        }

        public void Dispose()
        {
            if (active == this) active = null;
            harmony.UnpatchAll(Owner);
        }
    }
}
