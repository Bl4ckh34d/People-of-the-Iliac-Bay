using System;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallConnect.Arena2;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class AmbientShopPurchase
    {
        internal DaggerfallLoot Shelf;
        internal ulong Uid;
        internal int Price;
        internal string SellerId;
    }
    public sealed partial class StreetPopulation
    {
        private bool PrepareAmbientShopPurchase(ResidentActor actor,PhysicalShopVisit visit)
        {
            if(visit.PurchaseSelection!=null)return true;
            if(!BackgroundWork.TryStart())return false;
            NativePerson seller;
            if(!TryShopSeller(out seller)) {visit.PurchaseSelection=new AmbientShopPurchase();return true;}
            var shelves=interior.GetComponentsInChildren<DaggerfallLoot>();
            int today=DaggerfallLoot.CreateStockedDate(DaggerfallUnity.Instance.WorldTime.Now);
            foreach(var shelf in shelves)
            {
                // Native first-access stocking, at most one shelf per frame.
                // Never recreate already inspected stock or touch personal containers.
                if(shelf.ContainerType!=LootContainerTypes.ShopShelves || shelf.LoadID==0 || shelf.playerOwned || shelf.houseOwned ||
                    shelf.customDrop || shelf.corpseQuestUID!=0 || !shelf.GetComponent<SerializableLootContainer>() ||
                    shelf.GetComponentInParent<QuestResourceBehaviour>() || shelf.GetComponentInParent<DaggerfallAction>())continue;
                if(shelf.stockedDate<today)
                {
                    long started=RuntimeTiming.Start("ambient-shop-stock");
                    try {shelf.StockShopShelf(GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData);}
                    finally {RuntimeTiming.End("ambient-shop-stock",started);}
                    return false;
                }
            }
            var inventory=worldStore.ReadNpcInventory(actor.Resident.Id);
            var selected=new AmbientShopPurchase{SellerId=seller.Id};uint best=uint.MaxValue;
            if(inventory!=null)foreach(var shelf in shelves)
            {
                if(!VerifiedShopStock(shelf))continue;
                for(int i=0;i<shelf.Items.Count;i++)
                {
                    var item=shelf.Items.GetItem(i);int price;
                    if(!QuoteShopItem(actor,item,1,out price) || price>inventory.WalletGold)continue;
                    uint score=StableSeed.Combine(actor.Resident.AppearanceSeed,(uint)item.UID,(uint)(item.UID>>32),
                        unchecked((uint)Math.Floor(visit.OriginalEndAt)));
                    if(score>=best)continue;
                    best=score;selected.Shelf=shelf;selected.Uid=item.UID;selected.Price=price;
                }
            }
            visit.PurchaseSelection=selected;return true;
        }

        private bool TryCompleteAmbientShopPurchase(ResidentActor actor,PhysicalShopVisit visit,double now)
        {
            if(visit.CounterPurchaseResolved)return true;
            if(!PrepareAmbientShopPurchase(actor,visit))return false;
            var checkpoint=visit.Capture(actor,interior,indoorGeometry,now,population.SettlementId);
            if(checkpoint==null)return false;
            var choice=visit.PurchaseSelection;
            NativePerson seller;var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            string reason="No affordable available item.";bool purchased=false;
            if(choice.Shelf && owner && owner.Binding!=null && TryShopSeller(out seller) && seller.Id==choice.SellerId)
            {
                NativeNpcInventoryBinding sellerBinding;
                if(NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason) &&
                    NativeNpcInventoryBridge.TryOpen(worldStore,seller.Id,null,NativeNpcInventoryBridge.InitialWalletGold(worldStore,seller.Id),out sellerBinding,out reason))
                {
                    string transaction="routine-shop:"+actor.Resident.Id+":"+interior.EntryDoor.buildingKey+":"+
                        HouseholdInnVisitProvenance.WireTime(visit.OriginalEndAt);
                    purchased=NativeNpcInventoryBridge.TryPurchase(worldStore,owner.Binding,choice.Shelf.Items,seller.Id,
                        sellerBinding.Revision,choice.Uid,1,choice.Price,()=>{
                            int price;NativePerson current;
                            if(!actor || !actor.Resident.IsAlive || QuestOwned(actor.Resident) || !VerifiedShopStock(choice.Shelf) ||
                                !TryShopSeller(out current) || current.Id!=choice.SellerId || !shopGeometry.CounterPosition.HasValue)return false;
                            var counter=interior.transform.TransformPoint(shopGeometry.CounterPosition.Value);
                            // Merchant pivots are at sprite height; compare horizontal customer distance.
                            var gap=actor.transform.position-counter;gap.y=0;
                            return gap.magnitude<=SocialInteractionSpacing.MaximumMeters+0.6 &&
                                QuoteShopItem(actor,choice.Shelf.Items.GetItem(choice.Uid),1,out price) && price==choice.Price;
                        },transaction,out reason);
                }
            }
            // A failed decision also finishes this visit; never retry every frame
            // or buy a replacement item without going through another visit.
            visit.CounterPurchaseResolved=true;checkpoint.CounterPurchaseResolved=true;
            worldStore.StoreObservedVisit(checkpoint);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_AMBIENT_SHOP_PURCHASE: resident="+actor.Resident.Id+
                "; purchased="+purchased+"; item="+choice.Uid+"; gold="+(purchased?choice.Price:0)+"; reason="+reason);
            return true;
        }
    }
}
