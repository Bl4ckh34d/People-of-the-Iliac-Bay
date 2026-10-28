using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NativeNpcInventoryBridge
    {
        // Custody supplies actual live admission separately. This operation
        // never creates a cell, an inventory, an item or a transfer receipt.
        internal static bool TryConfiscateExact(WorldStateStore store,NativeNpcInventoryBinding source,
            NativeNpcInventoryBinding actualOwner,ItemCollection actualContainer,ulong uid,
            Func<bool> custodyStillValid,string transactionId,out string reason)
        {
            reason="Actual custody or confiscation ownership is unavailable.";
            if(store==null || source==null || custodyStillValid==null || !custodyStillValid() ||
                actualOwner!=null && actualContainer!=null)return false;
            var item=source.Items.GetItem(uid);
            if(item==null || item.IsEquipped || item.IsQuestItem || item.IsSummoned)return false;
            if(actualOwner!=null || actualContainer!=null)
            {
                NpcActionTransferReceipt ignored;
                return Execute(store,source.Items,actualOwner!=null?actualOwner.Items:actualContainer,
                    source,actualOwner,null,0,0,uid,item.stackCount,custodyStillValid,transactionId,
                    true,null,0,out ignored,out reason);
            }
            // Missing real disposal container means destruction, not a hidden
            // evidence inventory. Commit removal from the one actual NPC owner.
            bool removed=false,committed=false;
            try
            {
                var saved=CheckBinding(store,source,transactionId);
                if(saved==null || !custodyStillValid())return false;
                source.Items.RemoveItem(item);removed=true;
                if(!custodyStillValid() || !store.CommitNpcInventoryTransaction(transactionId,new[]{new NpcInventoryUpdate{
                    PersonId=source.PersonId,ExpectedRevision=saved.Revision,WalletGold=saved.WalletGold,
                    Items=CaptureItems(source.Items)}}))return false;
                committed=true;source.Revision++;reason=null;return true;
            }
            catch(Exception error){reason=error.Message;return false;}
            finally{if(removed && !committed)source.Items.AddItem(item,noStack:true);}
        }
    }
}
