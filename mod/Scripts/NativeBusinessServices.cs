using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
namespace LivingCities.DFU
{
    // Mirrors PlayerActivate.StaticNPCClick's service routing. A native source
    // building or a bed claim alone is never a business role.
    internal static class NativeBusinessServices
    {
        public static bool OffersKeeperService(int factionId,DFLocation.BuildingTypes type)
        {
            FactionFile.FactionData faction;
            return (RMBLayout.IsShop(type) || type==DFLocation.BuildingTypes.Tavern) &&
                GameManager.Instance.PlayerEntity.FactionData.GetFactionData(factionId,out faction) &&
                faction.sgroup==(int)FactionFile.SocialGroups.Merchants &&
                !Services.HasGuildService(factionId) && !Services.HasCustomMerchantService(factionId) &&
                !QuestMachine.Instance.HasFactionListener(factionId);
        }
        public static bool IsKeeper(StaticNPC npc,DFLocation.BuildingTypes type)
        {
            return npc && !npc.IsChildNPC && npc.Data.context==StaticNPC.Context.Building &&
                !npc.GetComponent<QuestResourceBehaviour>() && !npc.GetComponent<DaggerfallAction>() &&
                OffersKeeperService(npc.Data.factionID,type);
        }
    }
}
