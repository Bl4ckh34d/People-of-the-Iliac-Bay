using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Banking;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private IDisposable householdLootConsentHook;

        private void InstallHouseholdLootConsentHook()
        {
            var host = GetComponent<LivingCitiesMod>();
            if (!host) return;
            var hookType = Type.GetType("LivingCities.QuestHooks.NativeHouseholdLootConsentHooks, LivingCities.QuestHooks", true);
            householdLootConsentHook = (IDisposable)Activator.CreateInstance(hookType,
                new object[] { new Func<DaggerfallInventoryWindow, bool>(window => CanTakeHouseholdLoot(host, window)) });
        }

        private bool CanTakeHouseholdLoot(LivingCitiesMod host, DaggerfallInventoryWindow window)
        {
            var game = GameManager.Instance;
            if (!host || window == null || !Ready || population == null || worldStore == null || directory == null ||
                game == null || !game.IsReady || !game.PlayerEnterExit.IsPlayerInsideBuilding ||
                !game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId != population.SettlementId)
                return false;

            var room = game.PlayerEnterExit.Interior;
            var loot = window.LootTarget;
            if (!room || !loot || loot.ContainerType != LootContainerTypes.HouseContainers ||
                !loot.houseOwned || loot.playerOwned || !loot.transform.IsChildOf(room.transform)) return false;

            int buildingKey = room.EntryDoor.buildingKey;
            BuildingSummary summary;
            if (!directory.GetBuildingSummary(buildingKey, out summary) || summary.FactionId != 0 ||
                summary.BuildingType < DFLocation.BuildingTypes.House1 ||
                summary.BuildingType > DFLocation.BuildingTypes.House6 || RMBLayout.IsShop(summary.BuildingType) ||
                summary.BuildingType == DFLocation.BuildingTypes.Tavern || DaggerfallBankManager.IsHouseOwned(buildingKey))
                return false;

            Household household = population.Households.Find(candidate => candidate.BuildingKey == buildingKey);
            if (household == null) return false;
            return HouseholdLootConsent.AllowsPrivateHomeContainer(population.SettlementId, buildingKey, household,
                true, false, false, false, false, false, false, id => {
                    int disposition;
                    return host.TryGetFreshConversationDisposition(id, out disposition) ? (int?)disposition : null;
                });
        }

        private void DisposeHouseholdLootConsentHook()
        {
            if (householdLootConsentHook == null) return;
            householdLootConsentHook.Dispose();
            householdLootConsentHook = null;
        }
    }
}
