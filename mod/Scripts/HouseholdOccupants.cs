using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private readonly Dictionary<GameObject, bool> replacedHouseFlats = new Dictionary<GameObject, bool>();
        private void RestoreHouseFlats()
        {
            foreach (var pair in replacedHouseFlats) if (pair.Key) pair.Key.SetActive(pair.Value);
            replacedHouseFlats.Clear();
        }
        private void ReconcileHouseFlats(DaggerfallInterior room)
        {
            if (!room || population == null || !directory) return;
            var home = population.Households.Find(h => h.BuildingKey == room.EntryDoor.buildingKey);
            BuildingSummary summary;
            if (home == null || home.BedCapacity < 0 || !directory.GetBuildingSummary(home.BuildingKey, out summary) ||
                summary.BuildingType < DFLocation.BuildingTypes.House1 || summary.BuildingType > DFLocation.BuildingTypes.House6 || summary.FactionId != 0) return;
            foreach (var npc in room.GetComponentsInChildren<StaticNPC>())
            {
                // Only unclaimed generic residential decoration is replaced by the
                // authoritative saved family. Native quest actors and services retain ownership.
                if (npc.Data.context != StaticNPC.Context.Building || npc.Data.buildingKey != home.BuildingKey ||
                    npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>() ||
                    QuestMachine.Instance.HasFactionListener(npc.Data.factionID) || npc.Data.factionID != 0) continue;
                if (!replacedHouseFlats.ContainsKey(npc.gameObject)) replacedHouseFlats.Add(npc.gameObject, npc.gameObject.activeSelf);
                npc.gameObject.SetActive(false);
            }
        }
    }
}
