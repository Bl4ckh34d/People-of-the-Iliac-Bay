using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class NativeVendorTradeStock
    {
        internal NativeMarketPost Post;
        internal NativePerson Person;
        internal NativeNpcInventoryBinding Seller;
        internal ItemCollection Items {get{return Seller.Items;}}
        internal bool IsSaleItem(DaggerfallUnityItem item){return NativeMarketStock.IsSaleItem(item,Post);}
    }
    internal sealed class NativeVendorPurchaseReceipt
    {
        internal string TransactionId;
        internal ulong SourceItemUid,RecipientItemUid;
        internal int Quantity,Price;
    }
    internal static class NativeVendorTradeAdapter
    {
        private static IUserInterfaceWindow activeWindow;
        private static string activePerson;
        internal static string ActivePersonId {get{return activeWindow!=null && DaggerfallUI.UIManager.ContainsWindow(activeWindow)?activePerson:null;}}
        internal static void SetActive(IUserInterfaceWindow window,string person){activeWindow=window;activePerson=person;}
        internal static void ClearActive(IUserInterfaceWindow window){if(ReferenceEquals(activeWindow,window)){activeWindow=null;activePerson=null;}}
        internal static bool TryOpenMarketStock(WorldStateStore store,StaticNPC npc,NativePerson person,
            out NativeVendorTradeStock stock,out string reason)
        {
            stock=null;reason="No observed market vendor is serving here.";
            int settlement,hash;
            if(store==null || !NativeExteriorNpcIdentity.TryRead(npc,out settlement,out hash) ||
                !NativeStaticNpcInventoryOwner.Matches(npc,person))return false;
            var directory=GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            var catalog=NativeMarketCatalog.For(directory);var post=catalog==null?null:catalog.Find(npc);
            if(post==null || !post.Available || post.Role==MarketPostRole.Unknown || post.Role==MarketPostRole.Performer)return false;
            if(!NativeMarketStock.EnsureInitialInventory(store,person,post,out reason))return false;
            NativeStaticNpcInventoryOwner owner;
            if(!NativeStaticNpcInventoryOwner.TryOpen(store,npc,person,NpcArchetype.Merchant,out owner,out reason) ||
                !NativeNpcInventoryBridge.TryCapture(store,owner.Binding,out reason))return false;
            stock=new NativeVendorTradeStock {Post=post,Person=person,Seller=owner.Binding};reason=null;return true;
        }

        internal static bool TryNpcPurchase(WorldStateStore store,NativeVendorTradeStock stock,NativeNpcInventoryBinding buyer,
            ulong uid,int quantity,int price,Func<bool> live,string transactionId,out NativeVendorPurchaseReceipt receipt,out string reason)
        {
            receipt=null;reason="The exact market item or buyer changed.";
            if(store==null || stock==null || buyer==null || buyer.PersonId==stock.Person.Id || live==null || !live())return false;
            var item=stock.Items.GetItem(uid);
            if(item==null || !stock.IsSaleItem(item) || quantity<1 || quantity>item.stackCount || price<=0)return false;
            var previous=new System.Collections.Generic.HashSet<ulong>();
            for(int index=0;index<buyer.Items.Count;index++)previous.Add(buyer.Items.GetItem(index).UID);
            if(!NativeNpcInventoryBridge.TryPurchaseCommercialStock(store,buyer,stock.Seller,uid,quantity,price,
                ()=>stock.Post.Available && stock.IsSaleItem(item) && live(),transactionId,out reason))return false;
            ulong received=0;
            for(int index=0;index<buyer.Items.Count;index++)
            {var added=buyer.Items.GetItem(index);if(!previous.Contains(added.UID) && added.stackCount==quantity){received=added.UID;break;}}
            if(received==0)throw new InvalidOperationException("Committed market purchase has no exact recipient UID.");
            receipt=new NativeVendorPurchaseReceipt {TransactionId=transactionId,SourceItemUid=uid,
                RecipientItemUid=received,Quantity=quantity,Price=price};return true;
        }
    }
}
