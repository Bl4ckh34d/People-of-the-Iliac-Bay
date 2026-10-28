using System;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class MarketPurchase
        {internal ulong Uid;internal int Price;internal string SellerId,ItemName,SellerName;}

        private bool BeginMarketPurchase(Plan plan,ResidentActor actor,MarketVisit visit,NativeMarketCatalog catalog,double now)
        {
            visit.PurchaseResolved=true;
            if(visit.Slot.Post.Role==MarketPostRole.Performer || now+180>=visit.EndsAt ||
                StableSeed.Combine(plan.Resident.AppearanceSeed,(uint)visit.Day,7841,(uint)visit.Seen.Count)%3!=0)return false;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();string reason;
            if(!owner || owner.Binding==null || !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason))return false;
            var post=visit.Slot.Post;
            int hash=NativeExteriorNpcIdentity.Hash(post.Seller,directory.transform);
            string sellerId=NativeExteriorNpcIdentity.PersonId(directory.MapID,hash);
            var person=worldStore.NativePersonInSettlement(directory.MapID,sellerId);NativeVendorTradeStock stock;
            if(person==null || !NativeVendorTradeAdapter.TryOpenMarketStock(worldStore,post.Seller,person,out stock,out reason))return false;
            var saved=worldStore.ReadNpcInventory(actor.Resident.Id);MarketPurchase choice=null;uint best=uint.MaxValue;
            for(int index=0;index<stock.Items.Count;index++)
            {
                var item=stock.Items.GetItem(index);int price;
                if(!QuoteMarketItem(actor,stock,item,out price) || price>saved.WalletGold)continue;
                uint score=StableSeed.Combine(plan.Resident.AppearanceSeed,(uint)item.UID,(uint)(item.UID>>32),(uint)visit.Day);
                if(score>=best)continue;best=score;
                choice=new MarketPurchase {Uid=item.UID,Price=price,SellerId=person.Id,SellerName=person.Name,ItemName=item.LongName};
            }
            if(choice==null)return false;
            foreach(var slot in post.Slots)
            {
                if(!slot.Payment || !catalog.TryClaim(slot,plan.Resident.Id,actor.transform.position-Vector3.up,now,visit.EndsAt,actor))continue;
                visit.Slot=slot;visit.Payment=true;visit.Choice=choice;
                visit.Dwell=new MarketDwell(60+StableSeed.Combine(plan.Resident.AppearanceSeed,(uint)visit.Day,7841,1)%121);
                return true;
            }
            return false;
        }
        private bool QuoteMarketItem(ResidentActor actor,NativeVendorTradeStock stock,DaggerfallUnityItem item,out int price)
        {
            price=0;if(item==null || !stock.IsSaleItem(item))return false;
            var behaviour=actor.GetComponent<DaggerfallEntityBehaviour>();var entity=behaviour?behaviour.Entity:null;
            if(entity==null || entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()>entity.MaxEncumbrance)return false;
            const int quality=10;
            return NpcShopCommerce.TryPriceForBuyer(worldStore,actor.Resident.Id,FormulaHelper.CalculateCost(item.value,quality),1,quality,false,out price) && price>0;
        }
        private void CompleteMarketPurchase(Plan plan,ResidentActor actor,MarketVisit visit,double now)
        {
            if(!visit.Payment || visit.Choice==null)return;
            visit.Payment=false;var choice=visit.Choice;visit.Choice=null;
            var post=visit.Slot.Post;var person=worldStore.NativePersonInSettlement(directory.MapID,choice.SellerId);
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();NativeVendorTradeStock stock;string reason;
            bool bought=false;NativeVendorPurchaseReceipt receipt=null;
            string transaction="market-purchase:"+plan.Resident.Id+":"+visit.Day+":"+post.Id;
            if(owner && owner.Binding!=null && person!=null &&
                NativeVendorTradeAdapter.TryOpenMarketStock(worldStore,post.Seller,person,out stock,out reason) &&
                NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason))
            {
                bought=NativeVendorTradeAdapter.TryNpcPurchase(worldStore,stock,owner.Binding,choice.Uid,1,choice.Price,()=>{
                    int currentPrice;
                    return actor && actor.Resident.IsAlive && post.Available && !IsAlarmed(plan.Resident.Id) &&
                        Vector3.Distance(actor.transform.position-Vector3.up,visit.Slot.Ground)<=0.6f &&
                        Vector3.Distance(actor.transform.position,post.Seller.transform.position)<=4.5f &&
                        QuoteMarketItem(actor,stock,stock.Items.GetItem(choice.Uid),out currentPrice) && currentPrice==choice.Price;
                },transaction,out receipt,out reason);
            }
            string outcome=bought?"I bought "+choice.ItemName+" from "+choice.SellerName+" for "+choice.Price+
                " gold and am carrying that item until I return home.":"I considered buying "+choice.ItemName+" from "+choice.SellerName+", but did not complete a purchase.";
            if(bought)visit.Purchase=visit.Purchase==null?outcome:visit.Purchase+" "+outcome;
            if(bought)
            {
                visit.Receipt=receipt;
                var acquired=owner.Binding.Items.GetItem(receipt.RecipientItemUid) as NativeMarketGoodsItem;
                if(acquired!=null)
                {
                    var kind=MarketActivity.Goods(acquired.GoodsArchive,acquired.GoodsRecord);
                    if(kind==MarketGoodsKind.Fish || kind==MarketGoodsKind.Meat || kind==MarketGoodsKind.Bread || kind==MarketGoodsKind.Produce)
                        worldStore.RecordNpcOrdinaryPurchase(plan.Resident.Id,acquired.UID);
                }
            }
            NativeSceneRecollection.Remember(worldStore,plan.Resident.Id,transaction,NpcSceneEventKind.Shopping,outcome,
                bought?1:0,plan.Resident.Id,choice.SellerId);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_PURCHASE: buyer="+plan.Resident.Id+"; seller="+choice.SellerId+
                "; bought="+bought+"; source="+choice.Uid+"; received="+(receipt==null?0:receipt.RecipientItemUid)+"; gold="+(bought?choice.Price:0));
        }
    }
}
