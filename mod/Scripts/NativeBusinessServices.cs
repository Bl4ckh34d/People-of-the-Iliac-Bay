using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
namespace PeopleOfTheIliacBay.DFU
{
    // Mirrors PlayerActivate.StaticNPCClick's service routing. A native source
    // building or a bed claim alone is never a business role.
    internal static class NativeBusinessServices
    {
        public static bool OffersKeeperService(int factionId,DFLocation.BuildingTypes type)
        {return (RMBLayout.IsShop(type) || type==DFLocation.BuildingTypes.Tavern) && OffersMerchantService(factionId);}
        public static bool OffersBankService(int factionId)
        {return OffersMerchantService(factionId);}
        private static bool OffersMerchantService(int factionId)
        {
            FactionFile.FactionData faction;
            return GameManager.Instance.PlayerEntity.FactionData.GetFactionData(factionId,out faction) &&
                faction.sgroup==(int)FactionFile.SocialGroups.Merchants &&
                !Services.HasGuildService(factionId) && !Services.HasCustomMerchantService(factionId) &&
                !QuestMachine.Instance.HasFactionListener(factionId);
        }
        public static bool IsBankClerk(StaticNPC npc)
        {return npc && !npc.IsChildNPC && npc.Data.context==StaticNPC.Context.Building &&
            !npc.GetComponent<QuestResourceBehaviour>() && !npc.GetComponent<DaggerfallAction>() && OffersBankService(npc.Data.factionID);}
        public static bool IsKeeper(StaticNPC npc,DFLocation.BuildingTypes type)
        {
            return npc && !npc.IsChildNPC && npc.Data.context==StaticNPC.Context.Building &&
                !npc.GetComponent<QuestResourceBehaviour>() && !npc.GetComponent<DaggerfallAction>() &&
                OffersKeeperService(npc.Data.factionID,type);
        }
    }
}
