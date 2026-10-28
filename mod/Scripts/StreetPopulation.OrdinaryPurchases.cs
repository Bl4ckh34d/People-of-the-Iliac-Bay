using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Call only after actual indoor home arrival, after movement/action overlays.
        private void ConsumeOrdinaryPurchasesAtHome(ResidentActor actor,ActivitySnapshot state)
        {
            if(!actor || !interior || state.BuildingKey!=actor.Resident.HomeKey ||
                interior.EntryDoor.buildingKey!=actor.Resident.HomeKey ||
                state.Activity!=Activity.Home && state.Activity!=Activity.Sleep ||
                actor.transform.parent!=interior.transform || !worldStore.HasNpcOrdinaryPurchases(actor.Resident.Id))return;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();string reason;
            if(!owner || owner.Binding==null || !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason))return;
            var before=worldStore.ReadNpcInventory(actor.Resident.Id);
            if(before==null || !before.Items.Exists(item=>item.OrdinaryPurchase))return;
            // A formerly ordinary item may acquire native quest/equipment ownership.
            foreach(var item in before.Items)
            {
                if(!item.OrdinaryPurchase)continue;
                var native=owner.Binding.Items.GetItem(item.Uid);
                if(native!=null && (native.IsEquipped || native.IsQuestItem || native.RepairData.IsBeingRepaired()))
                    worldStore.RetainNpcOrdinaryPurchase(actor.Resident.Id,item.Uid);
            }
            // Keep exact native objects until canonical transaction succeeds.
            int count=worldStore.ConsumeNpcOrdinaryPurchasesAtHome(actor.Resident.Id);
            if(count==0)return;
            var after=worldStore.ReadNpcInventory(actor.Resident.Id);
            foreach(var item in before.Items)
                if(item.OrdinaryPurchase && !after.Items.Exists(value=>value.Uid==item.Uid))
                {
                    var native=owner.Binding.Items.GetItem(item.Uid);
                    if(native!=null)owner.Binding.Items.RemoveItem(native);
                }
            owner.Binding.Revision=after.Revision;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOME_PURCHASE_CONSUMED: person="+actor.Resident.Id+"; exactItems="+count);
        }
    }
}
