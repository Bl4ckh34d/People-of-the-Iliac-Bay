using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private float nextKeeperSurvey;
        private DaggerfallInterior keeperRoom;
        private HomeProfile keeperHome;
        private List<HomeProfile> keeperHomes;
        private void SurveyKeeperIdentities()
        {
            if (!interior || worldStore == null || population == null || !directory) return;
            if (keeperRoom == interior && Time.unscaledTime < nextKeeperSurvey) return;
            if (keeperRoom != interior)
            {
                keeperRoom = interior; keeperHome = null; keeperHomes = null;
                BuildingSummary shop;
                if (directory.GetBuildingSummary(interior.EntryDoor.buildingKey, out shop) && RMBLayout.IsShop(shop.BuildingType))
                {
                    var dfu = DaggerfallUnity.Instance;
                    keeperHome = HousingSurvey.Read(dfu.ContentReader, directory.LocationData, new[] { shop }, dfu.MeshReader)[0];
                    keeperHomes = HousingSurvey.Read(directory);
                }
            }
            nextKeeperSurvey = Time.unscaledTime + 1;
            if (keeperHome == null) return;
            foreach (var npc in interior.GetComponentsInChildren<StaticNPC>())
            {
                var data = npc.Data;
                if (data.context != StaticNPC.Context.Building || data.buildingKey != keeperHome.BuildingKey || npc.IsChildNPC ||
                    npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>()) continue;
                FactionFile.FactionData faction;
                if (!GameManager.Instance.PlayerEntity.FactionData.GetFactionData(data.factionID, out faction) ||
                    faction.type == (int)FactionFile.FactionTypes.Individual || faction.sgroup != (int)FactionFile.SocialGroups.Merchants) continue;
                var keeper = worldStore.RegisterNativePerson(population.SettlementId, keeperHome.BuildingKey, data.hash, npc.DisplayName,
                    data.gender == DaggerfallWorkshop.Game.Entity.Genders.Female ? ResidentSex.Female : ResidentSex.Male,
                    (int)data.race, data.factionID, data.billboardArchiveIndex, data.billboardRecordIndex);
                if (keeper.HomeKey < 0 && worldStore.AssignNativeBed(keeper.Id, keeperHome, keeperHomes))
                    Debug.Log("LIVING_CITIES_KEEPER_HOME: " + keeper.Id + "; " + keeper.Name + "; home=" + keeper.HomeKey + "; bed=" + keeper.BedSlot);
            }
        }
    }
}
