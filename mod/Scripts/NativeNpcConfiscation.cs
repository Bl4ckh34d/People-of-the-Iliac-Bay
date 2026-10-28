using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class NativeNpcConfiscationResult
    {
        internal int Returned,Stored,Destroyed;
    }

    internal static class NativeNpcConfiscation
    {
        // confirmedOwners may contain only exact UIDs from an actually observed
        // theft source. Missing evidence never turns an ordinary item into loot.
        // Resolvers return the existing living owner binding or actual shop stock.
        internal static bool TryApply(WorldStateStore store,NativeNpcInventoryBinding source,
            DaggerfallEntityBehaviour body,IDictionary<ulong,string> confirmedOwners,
            Func<string,NativeNpcInventoryBinding> livingOwner,Func<string,DaggerfallLoot> shopStock,
            DaggerfallLoot wallContainer,Func<bool> custodyStillValid,
            out NativeNpcConfiscationResult result,out string reason)
        {
            result=new NativeNpcConfiscationResult();reason="Actual admitted custody and NPC inventory are required.";
            if(store==null || source==null || !body || body.Entity==null ||
                !ReferenceEquals(source.Items,body.Entity.Items) || custodyStillValid==null || !custodyStillValid())return false;
            if(wallContainer && !IsActualWallContainer(wallContainer))wallContainer=null;
            var known=new Dictionary<ulong,string>();
            if(confirmedOwners!=null)foreach(var pair in confirmedOwners)known[pair.Key]=pair.Value;
            // The existing player provenance can survive an exact-UID transfer
            // into an NPC. A new native shop theft has an exact original shop.
            // Later theft markers on older legal-source entries prove theft but
            // do not establish that their historical owner was the robbed owner.
            var provenance=store.Snapshot().PlayerItemProvenance;
            if(provenance!=null)foreach(var entry in provenance.Entries)
                if(source.Items.Contains(entry.PlayerItemUid) &&
                    (entry.SourceKind==PlayerItemProvenanceSourceKind.NativeShopTheft || entry.TheftReceiptId!=null) &&
                    !known.ContainsKey(entry.PlayerItemUid))
                    known[entry.PlayerItemUid]=entry.SourceKind==PlayerItemProvenanceSourceKind.NativeShopTheft?entry.SourceOwnerId:null;
            var items=new List<DaggerfallUnityItem>();
            for(int i=0;i<source.Items.Count;i++)items.Add(source.Items.GetItem(i));
            foreach(var item in items)
            {
                string owner;bool stolen=known.TryGetValue(item.UID,out owner);
                if(item.ItemGroup!=ItemGroups.Weapons && !stolen)continue;
                // Native permanent quest/summoned ownership cannot be silently
                // destroyed by the generic inventory transfer API.
                if(item.IsQuestItem || item.IsSummoned){reason="Confiscated item requires its native quest/effect owner.";return false;}
                bool equipped=item.IsEquipped;
                if(equipped && !body.Entity.ItemEquipTable.UnequipItem(item))return false;
                if(!NativeNpcInventoryBridge.TryCapture(store,source,out reason))
                {if(equipped)body.Entity.ItemEquipTable.EquipItem(item,true,false);return false;}
                NativeNpcInventoryBinding targetOwner=null;DaggerfallLoot targetContainer=null;
                if(stolen && !string.IsNullOrEmpty(owner))
                {
                    if(owner.StartsWith("native-shop:",StringComparison.Ordinal))
                    {
                        if(shopStock!=null)targetContainer=shopStock(owner);
                        if(targetContainer && !IsActualShopContainer(targetContainer,owner))targetContainer=null;
                    }
                    else if(livingOwner!=null && IsLivingOwner(store,owner))targetOwner=livingOwner(owner);
                    if(targetOwner!=null && (targetOwner.PersonId!=owner || targetOwner.PersonId==source.PersonId))targetOwner=null;
                }
                bool returned=targetOwner!=null || targetContainer;
                if(!returned)targetContainer=wallContainer;
                bool requiresContainer=targetContainer;
                string transaction="native-confiscation:"+source.PersonId+":"+item.UID.ToString(CultureInfo.InvariantCulture)+":"+source.Revision;
                if(!NativeNpcInventoryBridge.TryConfiscateExact(store,source,targetOwner,targetContainer?targetContainer.Items:null,
                    item.UID,()=>custodyStillValid() && (targetOwner==null || IsLivingOwner(store,owner)) &&
                        (!requiresContainer || targetContainer && (returned?IsActualShopContainer(targetContainer,owner):IsActualWallContainer(targetContainer))),
                    transaction,out reason))
                {
                    if(equipped && source.Items.Contains(item))
                    {body.Entity.ItemEquipTable.EquipItem(item,true,false);string ignored;NativeNpcInventoryBridge.TryCapture(store,source,out ignored);}
                    return false;
                }
                if(returned)result.Returned++;else if(targetContainer)result.Stored++;else result.Destroyed++;
            }
            reason=null;return true;
        }
        private static bool IsLivingOwner(WorldStateStore store,string id)
        {
            var resident=store.ResolveResident(id);if(resident!=null)return resident.IsAlive;
            var world=store.Snapshot();var person=world.NativePeople.Find(value=>value.Id==id);
            if(person!=null)return person.IsAlive;
            var traveler=world.Travelers.Find(value=>value.Id==id);return traveler!=null && traveler.IsAlive;
        }
        private static bool IsActualWallContainer(DaggerfallLoot loot)
        {
            var game=GameManager.Instance;var room=game.PlayerEnterExit.Interior;
            if(!room || !game.PlayerEnterExit.IsPlayerInsideBuilding || !loot.transform.IsChildOf(room.transform) ||
                !loot.gameObject.activeInHierarchy || loot.LoadID==0 || loot.ContainerType!=LootContainerTypes.HouseContainers)return false;
            BuildingSummary building;var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            return directory && directory.GetBuildingSummary(room.EntryDoor.buildingKey,out building) &&
                building.BuildingType==DFLocation.BuildingTypes.Town23;
        }
        private static bool IsActualShopContainer(DaggerfallLoot loot,string shop)
        {
            var game=GameManager.Instance;var room=game.PlayerEnterExit.Interior;
            return loot && loot.LoadID!=0 && loot.ContainerType==LootContainerTypes.ShopShelves && loot.gameObject.activeInHierarchy && room &&
                game.PlayerEnterExit.IsPlayerInsideBuilding && loot.transform.IsChildOf(room.transform) &&
                shop=="native-shop:"+game.PlayerGPS.CurrentMapID+":"+room.EntryDoor.buildingKey;
        }
    }
}
