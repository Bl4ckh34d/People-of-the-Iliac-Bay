using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // One-time owner inventory for a verified live Holy Order trainer. This is
    // personal property, not temple stock, and has no refill path.
    public static class NativeNpcTempleStartingBelongings
    {
        public static bool TryEnsure(WorldStateStore store,StaticNPC npc,NativePerson person,int buildingFactionId,out string reason)
        {
            reason="The native temple staff identity is not eligible for starting belongings.";
            if(store==null || !npc || person==null || buildingFactionId<=0 || npc.Data.context!=StaticNPC.Context.Building ||
                npc.GetComponent<QuestResourceBehaviour>()!=null || npc.GetComponent<DaggerfallAction>()!=null ||
                !npc.gameObject.activeInHierarchy || !person.IsAlive || person.RetiredBedroomFlat ||
                person.FactionId!=npc.Data.factionID || person.SettlementId!=npc.Data.mapID ||
                person.WorkKey!=npc.Data.buildingKey || person.NativeHash!=npc.Data.hash ||
                !Services.HasGuildService(npc.Data.factionID) ||
                Services.GetService((GuildNpcServices)npc.Data.factionID)!=GuildServices.Training) return false;
            GuildManager manager=GameManager.Instance==null?null:GameManager.Instance.GuildManager;
            if(manager==null || manager.GetGuildGroup(buildingFactionId)!=FactionFile.GuildGroups.HolyOrder)return false;
            if(store.HasNpcInventory(person.Id)){reason=null;return true;}
            var initial=new ItemCollection();
            initial.AddItem(ItemBuilder.CreateItem(ItemGroups.ReligiousItems,
                NpcStartingBelongings.TempleReligiousTemplate(store.WorldSeed,person.Id)),noStack:true);
            NativeNpcInventoryBinding binding;
            if(!NativeNpcInventoryBridge.TryOpen(store,person.Id,initial,
                NativeNpcInventoryBridge.InitialWalletGold(store,person.Id),out binding,out reason))return false;
            reason=null;return true;
        }
    }
}
