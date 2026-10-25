using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        // Existing NativeCorpseView cache is populated only for the active exterior.
        // Do not discover containers here: callers receive a loaded, bound corpse only.
        internal bool TryGetLawfulLootClaim(int settlementId,string corpseId,out DaggerfallLoot loot)
        {
            loot=null;
            if(!OwnsExterior || worldStore==null || settlementId!=directory.MapID ||
                string.IsNullOrWhiteSpace(corpseId)) return false;
            var record=worldStore.DefenseCorpsesFor(settlementId).Find(x=>x!=null && x.Death!=null &&
                x.Death.CorpseId==corpseId && x.Death.Lawful && x.Death.LootEntitled &&
                (x.Death.Cleanup==null || !x.Death.Cleanup.Cleared));
            if(record==null) return false;
            loot=NativeCorpseView(record);
            return loot && loot.gameObject.activeInHierarchy && loot.ContainerType==LootContainerTypes.CorpseMarker &&
                loot.LoadID!=0 && loot.corpseQuestUID==0;
        }
    }
}
