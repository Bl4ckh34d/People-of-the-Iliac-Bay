using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Questing;
using LivingCities.Core;
namespace LivingCities.DFU
{
    internal static class BedroomFlatReplacement
    {
        public static bool OrdinaryFaction(int factionId,DFLocation.BuildingTypes type)
        {
            if(QuestMachine.Instance.HasFactionListener(factionId) || Services.HasGuildService(factionId) ||
                Services.HasCustomMerchantService(factionId) || NativeBusinessServices.OffersKeeperService(factionId,type)) return false;
            FactionFile.FactionData faction;
            return !GameManager.Instance.PlayerEntity.FactionData.GetFactionData(factionId,out faction) ||
                faction.type!=(int)FactionFile.FactionTypes.Individual;
        }
        public static bool Eligible(StaticNPC npc,DFLocation.BuildingTypes type)
        {
            return npc && npc.Data.context==StaticNPC.Context.Building &&
                !npc.GetComponent<QuestResourceBehaviour>() && !npc.GetComponent<DaggerfallAction>() &&
                OrdinaryFaction(npc.Data.factionID,type);
        }
        public static void Apply(WorldStateStore store,DaggerfallInterior interior,DFLocation.BuildingTypes type,
            IDictionary<int,string> bedrooms)
        {
            if(store==null || !interior || bedrooms==null) return;
            foreach(var npc in interior.GetComponentsInChildren<StaticNPC>(true))
            {
                if(npc.Data.buildingKey!=interior.EntryDoor.buildingKey || !bedrooms.ContainsKey(npc.Data.hash) || !Eligible(npc,type)) continue;
                var data=npc.Data;
                var person=store.RegisterNativePerson(data.mapID,data.buildingKey,data.hash,npc.DisplayName,
                    data.gender==DaggerfallWorkshop.Game.Entity.Genders.Female ? ResidentSex.Female : ResidentSex.Male,
                    (int)data.race,data.factionID,data.billboardArchiveIndex,data.billboardRecordIndex);
                if(store.RetireOrdinaryBedroomFlat(person.Id) || !person.IsAlive)
                {
                    var sleep=npc.GetComponent<SleepingPose>();if(sleep && sleep.Active) sleep.Wake();
                    npc.gameObject.SetActive(false);
                }
            }
        }
    }
}
