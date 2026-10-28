using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public bool HasNpcOrdinaryPurchases(string personId)
        {
            var inventory=IndexedNpcInventory(personId);
            return inventory!=null && inventory.Items.Exists(item=>item.OrdinaryPurchase);
        }
        // Only the ordinary shopping adapter may mark its exact received item.
        // Native templates, gifts and task purchases do not imply this provenance.
        public bool RecordNpcOrdinaryPurchase(string personId,ulong uid)
        {
            var inventory=IndexedNpcInventory(personId);
            var item=inventory==null?null:inventory.Items.Find(value=>value.Uid==uid);
            if(item==null)return false;item.OrdinaryPurchase=true;return true;
        }

        public void RetainNpcOrdinaryPurchase(string personId,ulong uid)
        {
            var inventory=IndexedNpcInventory(personId);
            var item=inventory==null?null:inventory.Items.Find(value=>value.Uid==uid);
            if(item!=null)item.OrdinaryPurchase=false;
        }

        private bool OrdinaryPurchaseBecameTaskItem(string personId,ulong uid)
        {
            string id=uid.ToString(CultureInfo.InvariantCulture);
            foreach(var task in state.NpcTasks)
                if(task.ResidentId==personId && task.Steps.Exists(step=>step.Target!=null && step.Target.ItemId==id))return true;
            foreach(var action in state.NpcActions)
                if(action.ResidentId==personId && (action.AcquiredItemId==id || action.Target!=null && action.Target.ItemId==id))return true;
            return false;
        }

        public int ConsumeNpcOrdinaryPurchasesAtHome(string personId)
        {
            var inventory=ReadNpcInventory(personId);
            // A live task/action may have reserved even an ordinary purchase.
            if(inventory==null || ActiveNpcAction(personId)!=null || ActiveNpcTask(personId)!=null)return 0;
            foreach(var item in inventory.Items)
                if(item.OrdinaryPurchase && OrdinaryPurchaseBecameTaskItem(personId,item.Uid))
                {RetainNpcOrdinaryPurchase(personId,item.Uid);item.OrdinaryPurchase=false;}
            int count=inventory.Items.RemoveAll(item=>item.OrdinaryPurchase);
            if(count==0)return 0;
            return CommitNpcInventoryTransaction("home-consumption:"+personId+":"+inventory.Revision,
                new[]{new NpcInventoryUpdate {PersonId=personId,ExpectedRevision=inventory.Revision,
                    WalletGold=inventory.WalletGold,Items=inventory.Items}})?count:0;
        }
    }
}
