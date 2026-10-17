using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class LivingCitiesMod
    {
        [Invoke(StateManager.StateTypes.Start, 11)]
        public static void InstallDispositionPricing(InitParams initParams)
        {
            var component = FindObjectOfType<LivingCitiesMod>();
            if (!component) return;
            var lifecycle = component.gameObject.AddComponent<DispositionPricingLifecycle>();
            lifecycle.Install(component.CreateDispositionPricingHooks());
        }

        private IDisposable CreateDispositionPricingHooks()
        {
            var installed = new List<IDisposable>();
            try
            {
                var hookType = Type.GetType("LivingCities.QuestHooks.NativeDispositionPricingHooks, LivingCities.QuestHooks", true);
                installed.Add((IDisposable)Activator.CreateInstance(hookType, new object[] {
                    new Func<StaticNPC, int, int>((npc, price) => AdjustNativeKeeperPurchasePrice(npc, price, false)),
                    new Func<int, int>(price => AdjustCurrentShelfPurchasePrice(price)),
                    new Func<StaticNPC, int, int>((npc, price) => AdjustNativeKeeperSalePrice(npc, price)),
                    new Func<StaticNPC, int, int>((npc, price) => AdjustNativeKeeperServicePrice(npc, price)),
                    new Func<StaticNPC, bool>(npc => CanServeNativeKeeper(npc)),
                    new Func<bool>(() => CanServeAtCurrentShelf()),
                    new Func<StaticNPC, int, int>((npc, price) => AdjustNativeKeeperPurchasePrice(npc, price, true)),
                    new Func<string, int, int>((input, alreadyRented) => {
                        int days;
                        return DispositionPricing.TryGetPaidTavernRoomDays(input, alreadyRented, out days) ? days : 0;
                    }) }));

                var serviceHookType = Type.GetType("LivingCities.QuestHooks.NativeDispositionServiceHooks, LivingCities.QuestHooks", true);
                installed.Add((IDisposable)Activator.CreateInstance(serviceHookType, new object[] {
                    new Func<StaticNPC, bool>(npc => CanServeNativeKeeper(npc)),
                    new Func<StaticNPC, int, int>((npc, price) => AdjustNativeKeeperServicePrice(npc, price)) }));
                return new CompositeDispositionPricingHooks(installed.ToArray());
            }
            catch
            {
                for (int i = installed.Count - 1; i >= 0; i--) installed[i].Dispose();
                throw;
            }
        }

        private int AdjustCurrentShelfPurchasePrice(int nativePrice)
        {
            if (nativePrice <= 0 || saveFault || !options.Enabled || !conversationDispositionEnabled || store == null)
                return nativePrice;
            var keeper = FindCurrentShopKeeper();
            return keeper ? AdjustNativeKeeperPurchasePrice(keeper, nativePrice, false) : nativePrice;
        }

        private bool CanServeAtCurrentShelf()
        {
            var keeper = FindCurrentShopKeeper();
            return !keeper || CanServeNativeKeeper(keeper);
        }

        private StaticNPC FindCurrentShopKeeper()
        {
            var game = GameManager.Instance;
            if (game == null || !game.PlayerEnterExit.IsPlayerInsideOpenShop || !game.PlayerEnterExit.Interior)
                return null;
            var building = game.PlayerEnterExit.BuildingDiscoveryData;
            if (!RMBLayout.IsShop(building.buildingType)) return null;
            var interior = game.PlayerEnterExit.Interior;
            StaticNPC matched = null;
            foreach (var npc in interior.GetComponentsInChildren<StaticNPC>(true))
            {
                if (!npc || !npc.gameObject.activeInHierarchy || npc.Data.context != StaticNPC.Context.Building ||
                    npc.Data.mapID != game.PlayerGPS.CurrentLocation.MapTableData.MapId || npc.Data.buildingKey != building.buildingKey ||
                    npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>()) continue;
                if(!NativeBusinessServices.IsKeeper(npc,building.buildingType)) continue;

                string personId = string.Format(CultureInfo.InvariantCulture, "npc:{0}:{1}:{2}",
                    npc.Data.mapID, npc.Data.buildingKey, npc.Data.hash);
                if (!store.NativePeopleFor(npc.Data.mapID).Exists(p => p.Id == personId && p.WorkKey == npc.Data.buildingKey &&
                    p.NativeHash == npc.Data.hash && p.FactionId == npc.Data.factionID && p.IsAlive)) continue;
                if (matched != null) return null; // Ambiguous owner: fail closed.
                matched = npc;
            }

            return matched;
        }

        private int AdjustNativeKeeperPurchasePrice(StaticNPC npc, int nativePrice, bool innRoom)
        {
            return AdjustNativeKeeperDispositionPrice(npc, nativePrice, innRoom, false);
        }

        private int AdjustNativeKeeperSalePrice(StaticNPC npc, int nativePrice)
        {
            return AdjustNativeKeeperDispositionPrice(npc, nativePrice, false, true);
        }

        private int AdjustNativeKeeperServicePrice(StaticNPC npc, int nativePrice)
        {
            return AdjustNativeKeeperDispositionPrice(npc, nativePrice, false, false);
        }

        private bool CanServeNativeKeeper(StaticNPC npc)
        {
            if (saveFault || !options.Enabled || !conversationDispositionEnabled) return true;
            int disposition;
            return !TryGetNativeKeeperDisposition(npc, out disposition) || DispositionPricing.AllowsPaidService(disposition);
        }

        private int AdjustNativeKeeperDispositionPrice(StaticNPC npc, int nativePrice, bool innRoom, bool sale)
        {
            if (nativePrice <= 0 || !TryGetNativeKeeperDisposition(npc, out int disposition))
                return nativePrice;

            var game = GameManager.Instance;
            var playerEnterExit = game.PlayerEnterExit;
            var building = playerEnterExit.BuildingDiscoveryData;
            if (innRoom && building.buildingType != DFLocation.BuildingTypes.Tavern) return nativePrice;
            return sale
                ? DispositionPricing.ApplySaleDispositionPrice(nativePrice, disposition)
                : DispositionPricing.ApplyPurchaseDispositionPrice(nativePrice, disposition);
        }

        private bool TryGetNativeKeeperDisposition(StaticNPC npc, out int disposition)
        {
            disposition = 0;
            if (saveFault || !options.Enabled || !conversationDispositionEnabled || !npc || store == null) return false;
            var game = GameManager.Instance;
            if (game == null || !game.PlayerEnterExit.IsPlayerInside) return false;
            var building = game.PlayerEnterExit.BuildingDiscoveryData;
            var data = npc.Data;
            if (data.context != StaticNPC.Context.Building || data.buildingKey <= 0 ||
                data.buildingKey != building.buildingKey ||
                data.mapID != game.PlayerGPS.CurrentLocation.MapTableData.MapId ||
                npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>()) return false;

            string personId = string.Format(CultureInfo.InvariantCulture, "npc:{0}:{1}:{2}",
                data.mapID, data.buildingKey, data.hash);
            var person = store.NativePeopleFor(data.mapID).Find(p => p.Id == personId && p.WorkKey == data.buildingKey &&
                p.NativeHash == data.hash && p.FactionId == data.factionID && p.IsAlive);
            return person != null && TryGetFreshConversationDisposition(personId, out disposition);
        }
    }

    internal sealed class DispositionPricingLifecycle : MonoBehaviour
    {
        private IDisposable hooks;

        public void Install(IDisposable installedHooks)
        {
            if (hooks != null) throw new InvalidOperationException("Disposition pricing already installed on lifecycle.");
            hooks = installedHooks ?? throw new ArgumentNullException(nameof(installedHooks));
        }

        private void OnDestroy()
        {
            if (hooks != null) hooks.Dispose();
            hooks = null;
        }
    }

    internal sealed class CompositeDispositionPricingHooks : IDisposable
    {
        private IDisposable[] hooks;

        public CompositeDispositionPricingHooks(IDisposable[] hooks)
        {
            this.hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
        }

        public void Dispose()
        {
            if (hooks == null) return;
            for (int i = hooks.Length - 1; i >= 0; i--)
                if (hooks[i] != null) hooks[i].Dispose();
            hooks = null;
        }
    }
}
