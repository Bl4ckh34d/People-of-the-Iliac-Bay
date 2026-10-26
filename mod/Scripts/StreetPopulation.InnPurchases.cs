using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private static readonly FieldInfo nativeTavernMenu = typeof(DaggerfallTavernWindow).GetField("tavernMenu",
            BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo nativeTavernPrices = typeof(DaggerfallTavernWindow).GetField("tavernFoodAndDrinkPrices",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private bool TryCompleteInnPurchaseAtCounter(ResidentActor actor, PhysicalShopVisit physical, double now)
        {
            return TryCompleteInnPurchaseAtCounter(worldStore,actor,physical,interior,indoorGeometry,shopGeometry,
                population.SettlementId,now,KeeperPerson,KeeperServiceBlocked,HouseholdInnPointRoutes(actor));
        }

        internal static bool TryCompleteInnPurchaseAtCounter(WorldStateStore worldStore,ResidentActor actor,
            PhysicalShopVisit physical,DaggerfallInterior interior,InteriorGeometry indoorGeometry,ShopVisitGeometry shopGeometry,
            int settlement,double now,Func<StaticNPC,NativePerson> keeperResolver,Func<StaticNPC,bool> keeperBlocked,
            IList<AppointmentPlaceRoute> pointRoutes=null,Action<NpcInnConsumptionReceipt> receiptApplied=null)
        {
            if (worldStore==null || keeperResolver==null || keeperBlocked==null || !actor || physical == null || !interior || shopGeometry == null || !shopGeometry.IsTavern ||
                !physical.Journey.Purchases) return false;

            bool Unavailable(NativePerson declinedKeeper,string reason)
            {
                NpcInnConsumptionReceipt declined;
                bool recorded=worldStore.TryRecordNpcInnNoPurchase(actor.Resident.Id,declinedKeeper==null?null:declinedKeeper.Id,
                    now,NpcInnConsumptionOutcome.Unavailable,reason,out declined,pointRoutes);
                if(recorded && receiptApplied!=null)receiptApplied(declined);return recorded;
            }
            ObservedVisit checkpoint = physical.Capture(actor, interior, indoorGeometry, now, settlement);
            if (checkpoint == null) return false;
            worldStore.StoreObservedVisit(checkpoint);
            NpcInnConsumptionReceipt settled = worldStore.ReadNpcInnConsumption(actor.Resident.Id);
            if (settled != null)
            {
                if(receiptApplied!=null)receiptApplied(settled);
                return true;
            }
            if (worldStore.IsNpcInnConsumptionRetired(actor.Resident.Id))
                return Unavailable(null,"visit superseded by a later inn settlement");

            StaticNPC keeperNpc = shopGeometry.TavernKeeper;
            NativePerson keeper = keeperResolver(keeperNpc);
            if (!keeperNpc || keeper == null || !keeper.IsAlive || !keeperNpc.gameObject.activeInHierarchy ||
                keeperBlocked(keeperNpc))
                return Unavailable(keeper,"keeper unavailable");

            NativeNpcInventoryOwner buyerOwner = actor.GetComponent<NativeNpcInventoryOwner>();
            if (!buyerOwner || buyerOwner.Binding == null)
                return Unavailable(keeper,"buyer wallet unavailable");

            string inventoryReason;
            if (!NativeNpcInventoryBridge.TryCapture(worldStore, buyerOwner.Binding, out inventoryReason))
                return Unavailable(keeper,"buyer wallet changed");

            string[] menu;
            byte[] prices;
            if (!TryReadNativeTavernMenu(keeperNpc, out menu, out prices))
                return Unavailable(keeper,"native menu unavailable");

            NpcInventoryState buyer = worldStore.ReadNpcInventory(actor.Resident.Id);
            if (buyer == null)
                return Unavailable(keeper,"buyer wallet unavailable");

            uint visitSeed = StableSeed.Combine(actor.Resident.AppearanceSeed, (uint)settlement,
                (uint)interior.EntryDoor.buildingKey, unchecked((uint)Math.Floor(physical.OriginalEndAt)));
            uint gameMinutes = DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToClassicDaggerfallTime();
            int holiday = FormulaHelper.GetHolidayId(gameMinutes, 0);
            int[] pricedMenu = new int[prices.Length];
            for (int index = 0; index < prices.Length; index++)
                pricedMenu[index] = holiday == 37 ? Math.Max(1, prices[index] / 2) : prices[index];
            int menuIndex=worldStore.PlannedHouseholdInnMenu(physical.HouseholdDayId,actor.Resident.Id);
            if(menuIndex<0)menuIndex = NpcInnConsumption.SelectMenuIndex(pricedMenu, buyer.WalletGold, visitSeed);
            int price = pricedMenu[menuIndex];
            // DFU halves tavern menu prices on holiday 37; New Life's player-only
            // free purchase is excluded so an NPC must still pay its real wallet.
            NpcInnConsumptionKind kind = menuIndex < 4 ? NpcInnConsumptionKind.Drink : NpcInnConsumptionKind.Food;
            NpcInnConsumptionReceipt receipt;
            if (worldStore.TryBuyNpcInnConsumption(actor.Resident.Id, keeper.Id, now, kind, menuIndex,
                menu[menuIndex], price, out receipt,pointRoutes))
            {
                if(receiptApplied!=null)receiptApplied(receipt);
                return true;
            }

            return Unavailable(keeper,"purchase could not be recorded");
        }

        private bool RecordNoInnPurchase(ResidentActor actor, NativePerson keeper, double now,
            NpcInnConsumptionOutcome outcome, string reason)
        {
            NpcInnConsumptionReceipt ignored;
            return worldStore.TryRecordNpcInnNoPurchase(actor.Resident.Id, keeper == null ? null : keeper.Id,
                now, outcome, reason, out ignored,HouseholdInnPointRoutes(actor));
        }
        private IList<AppointmentPlaceRoute> HouseholdInnPointRoutes(ResidentActor actor)
        {
            var visit=worldStore.GetObservedVisit(actor.Resident.Id);
            if(visit==null || visit.HouseholdDayId==null)return null;
            var progress=worldStore.ReadHouseholdJointProgress(population.SettlementId,actor.Resident.HomeKey);
            if(progress==null || progress.HouseholdDayId!=visit.HouseholdDayId)return null;
            var first=plans.Find(p=>p.Resident.Id==progress.FirstResidentId);
            var second=plans.Find(p=>p.Resident.Id==progress.SecondResidentId);
            if(first==null || second==null)return null;
            string reason;return FamilyPointRoutes(first,second,FamilyBookings(first,second),progress.AbsoluteDay,out reason);
        }

        private static bool TryReadNativeTavernMenu(StaticNPC keeper, out string[] menu, out byte[] prices)
        {
            menu=null;prices=null;
            return keeper && TryReadNativeTavernMenuTemplate(out menu,out prices,keeper);
        }
        // Native instance initializers provide the same base menu even when no
        // merchant is loaded. Never show/Setup this read-only window instance.
        internal static bool TryReadNativeTavernMenuTemplate(out string[] menu,out byte[] prices,StaticNPC keeper=null)
        {
            menu = null;
            prices = null;
            try
            {
                if (DaggerfallUI.UIManager == null || nativeTavernMenu == null || nativeTavernPrices == null)
                    return false;
                var window = new DaggerfallTavernWindow(DaggerfallUI.UIManager, keeper);
                var nativeNames = nativeTavernMenu.GetValue(null) as string[];
                var nativePrices = nativeTavernPrices.GetValue(window) as byte[];
                if (nativeNames == null || nativePrices == null || nativeNames.Length != nativePrices.Length ||
                    nativeNames.Length != 11) return false;
                menu = (string[])nativeNames.Clone();
                prices = (byte[])nativePrices.Clone();
                for (int i = 0; i < menu.Length; i++)
                    if (string.IsNullOrWhiteSpace(menu[i]) || prices[i] < 1) return false;
                return true;
            }
            catch (Exception error)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_INN_MENU_READ_FAILED: " + error.Message);
                return false;
            }
        }
    }
}
