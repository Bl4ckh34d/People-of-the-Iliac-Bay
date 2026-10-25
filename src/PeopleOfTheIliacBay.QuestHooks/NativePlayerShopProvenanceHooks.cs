using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using HarmonyLib;

namespace PeopleOfTheIliacBay.QuestHooks
{
    /// <summary>Observes exact native trade outcomes. This hook never grants current-clean ownership.</summary>
    public sealed class NativePlayerShopProvenanceHooks : IDisposable
    {
        private const string Owner = "peopleoftheiliacbay.player-shop-provenance";
        private static NativePlayerShopProvenanceHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Action<string, string, ulong, int, int, int, int, int, bool> committed;
        private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly PropertyInfo mode = typeof(DaggerfallTradeWindow).GetProperty("WindowMode", Instance);
        private static readonly FieldInfo basket = typeof(DaggerfallTradeWindow).GetField("basketItems", Instance);
        private static readonly MethodInfo price = typeof(DaggerfallTradeWindow).GetMethod("GetTradePrice", Instance);
        private static readonly MethodInfo stack = typeof(ItemCollection).GetMethod("FindExistingStack", Instance);

        private sealed class ItemStamp
        {
            public DaggerfallUnityItem Source, Destination;
            public ulong DestinationUid;
            public int Count, DestinationCount;
        }
        private sealed class Observation
        {
            public string ShopId;
            public ItemCollection Basket, PlayerItems;
            public int Price, GoldBefore;
            public List<ItemStamp> Items = new List<ItemStamp>();
        }

        public NativePlayerShopProvenanceHooks(Action<string, string, ulong, int, int, int, int, int, bool> committed)
        {
            if (active != null) throw new InvalidOperationException("Shop provenance hooks already installed.");
            this.committed = committed ?? throw new ArgumentNullException(nameof(committed));
            if (mode == null || basket == null || price == null || stack == null ||
                mode.PropertyType != typeof(DaggerfallTradeWindow.WindowModes) ||
                basket.FieldType != typeof(ItemCollection) || price.ReturnType != typeof(int))
                throw new MissingMemberException("Native trade API changed.");
            var confirm = typeof(DaggerfallTradeWindow).GetMethod("ConfirmTrade_OnButtonClick", Instance);
            var steal = typeof(DaggerfallTradeWindow).GetMethod("DoSteal", Instance);
            if (confirm == null || steal == null || !confirm.IsVirtual ||
                confirm.ReturnType != typeof(void) || steal.ReturnType != typeof(void) ||
                confirm.GetParameters().Length != 2 ||
                confirm.GetParameters()[0].ParameterType != typeof(DaggerfallMessageBox) ||
                confirm.GetParameters()[1].ParameterType != typeof(DaggerfallMessageBox.MessageBoxButtons) ||
                steal.GetParameters().Length != 0)
                throw new MissingMethodException("Native trade commit API changed.");
            active = this;
            try
            {
                harmony.Patch(confirm, prefix: new HarmonyMethod(typeof(NativePlayerShopProvenanceHooks), nameof(BeforeBuy)),
                    postfix: new HarmonyMethod(typeof(NativePlayerShopProvenanceHooks), nameof(AfterBuy)));
                harmony.Patch(steal, prefix: new HarmonyMethod(typeof(NativePlayerShopProvenanceHooks), nameof(BeforeSteal)),
                    postfix: new HarmonyMethod(typeof(NativePlayerShopProvenanceHooks), nameof(AfterSteal)));
            }
            catch { active = null; harmony.UnpatchAll(Owner); throw; }
        }

        private static void BeforeBuy(DaggerfallTradeWindow __instance,
            DaggerfallMessageBox.MessageBoxButtons __1, out Observation __state)
        {
            __state = __1 == DaggerfallMessageBox.MessageBoxButtons.Yes ? Capture(__instance, false) : null;
        }
        private static void BeforeSteal(DaggerfallTradeWindow __instance, out Observation __state)
        { __state = Capture(__instance, true); }
        private static void AfterBuy(Observation __state) { Publish(__state, false); }
        private static void AfterSteal(Observation __state) { Publish(__state, true); }

        private static Observation Capture(DaggerfallTradeWindow window, bool theft)
        {
            try
            {
                if (active == null || window == null ||
                    (DaggerfallTradeWindow.WindowModes)mode.GetValue(window, null) != DaggerfallTradeWindow.WindowModes.Buy)
                    return null;
                var game = GameManager.Instance;
                if (game == null || game.PlayerEntity == null || game.PlayerEnterExit == null ||
                    game.PlayerGPS == null || !game.PlayerGPS.HasCurrentLocation) return null;
                int buildingKey = game.PlayerEnterExit.BuildingDiscoveryData.buildingKey;
                if (buildingKey <= 0) return null;
                var collection = (ItemCollection)basket.GetValue(window);
                var player = game.PlayerEntity.Items;
                if (collection == null || player == null || collection.Count == 0) return null;
                int paid = theft ? 0 : (int)price.Invoke(window, null);
                if (!theft && paid <= 0) return null;
                var observation = new Observation {
                    ShopId = "native-shop:" + game.PlayerGPS.CurrentLocation.MapTableData.MapId + ":" + buildingKey,
                    Basket = collection, PlayerItems = player, Price = paid,
                    GoldBefore = game.PlayerEntity.GetGoldAmount() };
                var seen = new HashSet<ulong>();
                for (int i = 0; i < collection.Count; i++)
                {
                    var item = collection.GetItem(i);
                    if (item == null || item.UID == 0 || item.stackCount < 1 || !seen.Add(item.UID)) return null;
                    var merged = (DaggerfallUnityItem)stack.Invoke(player, new object[] { item });
                    if (!theft && merged != null) continue;
                    if (merged == null && player.Contains(item.UID)) continue;
                    observation.Items.Add(new ItemStamp { Source = item, Destination = merged,
                        DestinationUid = merged == null ? item.UID : merged.UID,
                        Count = item.stackCount, DestinationCount = merged == null ? 0 : merged.stackCount });
                }
                return observation.Items.Count > 0 ? observation : null;
            }
            catch { return null; }
        }

        private static void Publish(Observation observation, bool theft)
        {
            var self = active;
            if (self == null || observation == null) return;
            try
            {
                var game = GameManager.Instance;
                if (game == null || game.PlayerEntity == null ||
                    !ReferenceEquals(game.PlayerEntity.Items, observation.PlayerItems)) return;
                int goldAfter = game.PlayerEntity.GetGoldAmount();
                if (theft ? goldAfter != observation.GoldBefore :
                    observation.GoldBefore - (long)goldAfter != observation.Price) return;
                foreach (var item in observation.Items)
                {
                    if (observation.Basket.Contains(item.Source.UID)) continue;
                    var destination = observation.PlayerItems.GetItem(item.DestinationUid);
                    if (destination == null ||
                        !ReferenceEquals(destination, item.Destination ?? item.Source) ||
                        destination.stackCount != item.DestinationCount + item.Count) continue;
                    self.committed((theft ? "native-shop-theft:" : "native-shop-buy:") + item.DestinationUid,
                        observation.ShopId, item.DestinationUid, item.Count, item.DestinationCount,
                        observation.Price, observation.GoldBefore, goldAfter, theft);
                }
            }
            catch (Exception error)
            { UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SHOP_PROVENANCE_REJECTED: " + error); }
        }

        public void Dispose()
        {
            if (active != this) return;
            active = null;
            harmony.UnpatchAll(Owner);
        }
    }
}
