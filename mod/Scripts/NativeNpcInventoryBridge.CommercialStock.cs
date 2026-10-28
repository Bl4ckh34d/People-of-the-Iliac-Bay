using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NativeNpcInventoryBridge
    {
        // NPC commerce preserves the vendor's displayed offer. Private sales and
        // player purchases continue to transfer the seller's actual possessions.
        internal static bool TryPurchaseCommercialStock(WorldStateStore store,NativeNpcInventoryBinding buyer,
            NativeNpcInventoryBinding seller,ulong itemUid,int count,int price,Func<bool> access,
            string transactionId,out string reason)
        {
            reason="The commercial stock, owner or access changed.";
            try
            {
                if(store==null || buyer==null || seller==null || seller.PersonId==buyer.PersonId ||
                    ReferenceEquals(seller.Items,buyer.Items) || access==null || !access())return false;
                var saved=CheckBinding(store,seller,transactionId);
                var source=seller.Items.GetItem(itemUid);
                if(saved==null || source==null || store.NpcInventoryItemOwner(itemUid)!=seller.PersonId ||
                    source.IsQuestItem || source.IsEquipped || source.IsSummoned || source.RepairData.IsBeingRepaired() || source is HouseholdPropertyKeyItem ||
                    count<1 || count>source.stackCount || count>1 && !source.IsAStack())return false;
                var data=source.GetSaveData();data.uid=DaggerfallUnity.NextUID;data.stackCount=count;
                var detached=new ItemCollection();detached.DeserializeItems(new[]{data});var acquired=detached.GetItem(data.uid);
                if(acquired==null || acquired.GetType()!=source.GetType() || acquired.UID==source.UID || acquired.stackCount!=count)return false;
                Func<bool> current=()=>ReferenceEquals(seller.Items.GetItem(itemUid),source) &&
                    source.stackCount>=count && !source.RepairData.IsBeingRepaired() && CheckBinding(store,seller,transactionId)!=null && access();
                if(!TryPurchaseCreatedItem(store,buyer,seller.PersonId,saved.Revision,acquired,price,current,transactionId,out reason))return false;
                seller.Revision++;
                return true;
            }
            catch(Exception error){reason=error.Message;return false;}
        }
    }
}
