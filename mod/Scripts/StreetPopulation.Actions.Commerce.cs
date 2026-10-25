using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private bool TryShopSeller(out NativePerson seller)
        {
            seller=null;
            if(!interior || indoorGeometry==null || shopGeometry==null || !RMBLayout.IsShop(interior.BuildingData.BuildingType) ||
                !PlayerActivate.IsBuildingOpen(interior.BuildingData.BuildingType) ||
                !GameManager.Instance.PlayerEnterExit.IsPlayerInsideOpenShop ||
                GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.buildingKey!=interior.EntryDoor.buildingKey) return false;
            foreach(var npc in interior.GetComponentsInChildren<StaticNPC>())
            {
                if(!npc.gameObject.activeInHierarchy || !NativeBusinessServices.IsKeeper(npc,interior.BuildingData.BuildingType)) continue;
                var person=KeeperPerson(npc);
                if(person==null || !person.IsAlive || KeeperSleeping(person,ActionNow()) || worldStore.NativeResponse(person.Id)!=null) continue;
                var family=population.Households.Find(h=>h.Residents.Exists(r=>r.Id==person.Id));
                var resident=family==null?null:family.Residents.Find(r=>r.Id==person.Id);
                if(resident!=null && (resident.IsQuestProtected || QuestOwned(resident))) continue;
                if(seller!=null) {seller=null;return false;}
                seller=person;
            }
            return seller!=null;
        }
        private bool VerifiedShopStock(DaggerfallLoot stock)
        {
            // Never initialize or regenerate stock in response to dialogue. Only
            // native shelves already opened/restocked by DFU are authoritative.
            return stock && stock.gameObject.activeInHierarchy && stock.GetComponentInParent<DaggerfallInterior>()==interior &&
                stock.ContainerType==LootContainerTypes.ShopShelves && stock.LoadID!=0 && !stock.playerOwned && !stock.houseOwned &&
                !stock.customDrop && stock.corpseQuestUID==0 && !stock.GetComponentInParent<QuestResourceBehaviour>() &&
                !stock.GetComponentInParent<DaggerfallAction>() && stock.GetComponent<SerializableLootContainer>() &&
                stock.stockedDate==DaggerfallLoot.CreateStockedDate(DaggerfallUnity.Instance.WorldTime.Now);
        }
        private DaggerfallLoot FindShopStock(NpcActionTarget target)
        {
            if(!ActionSpaceMatches(target)) return null;
            ulong id;if(!ulong.TryParse(target.ContainerId,NumberStyles.None,CultureInfo.InvariantCulture,out id) || id==0) return null;
            DaggerfallLoot found=null;
            foreach(var stock in interior.GetComponentsInChildren<DaggerfallLoot>())
                if(stock.LoadID==id && VerifiedShopStock(stock)) {if(found) return null;found=stock;}
            return found;
        }
        public bool TryQuoteSemanticShopPurchase(ResidentActor actor,NpcActionTarget target,out string reason)
        {
            reason="The exact shop stock or item is no longer available.";
            if(!actor || target==null) return false;
            var stock=FindShopStock(target);ulong uid;
            if(!stock || !ulong.TryParse(target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid)) return false;
            int price;
            if(!QuoteShopItem(actor,stock.Items.GetItem(uid),target.Quantity,out price))
            {reason="The exact item quantity is unavailable or no longer fits the NPC's inventory.";return false;}
            var inventory=worldStore.ReadNpcInventory(actor.Resident.Id);
            if(inventory.WalletGold<price) {reason="The NPC cannot afford the exact requested quantity.";return false;}
            target.QuotedGold=price;reason=null;return true;
        }
        private static bool PurchasableShopItem(DaggerfallUnityItem item)
        {
            return TransferableActionItem(item) && item.ItemGroup!=ItemGroups.Transportation &&
                string.IsNullOrEmpty(item.GetSaveData().className);
        }
        private bool QuoteShopItem(ResidentActor actor,DaggerfallUnityItem item,int quantity,out int price)
        {
            price=0;
            if(!PurchasableShopItem(item) || quantity<1 || quantity>item.stackCount) return false;
            var behaviour=actor.GetComponent<DaggerfallEntityBehaviour>();var entity=behaviour?behaviour.Entity:null;
            if(entity==null || entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()*quantity>entity.MaxEncumbrance) return false;
            int quality=GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.quality;
            int unitCost=FormulaHelper.CalculateCost(item.value,quality);
            uint minutes=DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToClassicDaggerfallTime();
            int holiday=FormulaHelper.GetHolidayId(minutes,GameManager.Instance.PlayerGPS.CurrentRegionIndex);
            bool sale=holiday==(int)DFLocation.Holidays.Merchants_Festival ||
                holiday==(int)DFLocation.Holidays.Warriors_Festival && item.ItemGroup==ItemGroups.Weapons;
            return NpcShopCommerce.TryPriceForBuyer(worldStore,actor.Resident.Id,unitCost,quantity,quality,sale,out price);
        }
        private bool TryShopApproach(ResidentActor actor,DaggerfallLoot stock,out Vector3 chosen)
        {
            chosen=Vector3.zero;
            var local=interior.transform.InverseTransformPoint(stock.transform.position);
            var from=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            foreach(var point in indoorGeometry.NearbyApproachPoints(local,1.6f,2.5f,16))
            {
                var world=interior.transform.TransformPoint(point);
                if(Vector3.Distance(world+Vector3.up,stock.transform.position)>2.5f ||
                    !ShopShelfVisible(world+Vector3.up,stock)) continue;
                var route=shopGeometry.ActivityRouteBetween(from,point);
                if(Vector3.Distance(from,point)>0.25f && route.Count<2) continue;
                chosen=world;return true;
            }
            return false;
        }
        private static bool ShopShelfVisible(Vector3 origin,DaggerfallLoot stock)
        {
            Vector3 delta=stock.transform.position-origin;
            foreach(var hit in Physics.RaycastAll(origin,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
            {
                if(hit.collider.GetComponentInParent<ResidentActor>() || hit.collider.GetComponentInParent<StaticNPC>() ||
                    hit.collider.GetComponentInParent<PlayerMotor>() || hit.collider.GetComponentInParent<DaggerfallLoot>()==stock) continue;
                return false;
            }
            return true;
        }
        private void OfferShopPurchaseAction(ResidentActor actor,string text,List<NpcActionOption> options,bool semantic=true)
        {
            int semanticStart=options.Count;
            bool trace=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-commerce-chat-probe")>=0;
            if(options.Count>=(semantic?NpcActionCandidateCatalog.MaximumCandidates:24)) {if(trace)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_OFFER_DENIED: request; count="+options.Count+"; text="+text);return;}
            NativePerson seller;if(!TryShopSeller(out seller)) {if(trace)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_OFFER_DENIED: seller; chat="+actor.IsConversing);return;}
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();string reason;
            if(!owner || owner.Binding==null) return;
            if(!NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason)) {if(trace)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_OFFER_DENIED: inventory; reason="+reason);return;}
            var inventory=worldStore.ReadNpcInventory(actor.Resident.Id);
            int price=0;
            foreach(var shelf in interior.GetComponentsInChildren<DaggerfallLoot>())
            {
                if(!VerifiedShopStock(shelf)) continue;
                for(int i=0;i<shelf.Items.Count;i++)
                {
                    var candidate=shelf.Items.GetItem(i);int count;
                    if(!PurchasableShopItem(candidate)) continue;
                    if(semantic)
                    {
                        count=1;
                        if(!QuoteShopItem(actor,candidate,count,out price) || inventory.WalletGold<price) continue;
                        int low=1,high=Math.Min(candidate.stackCount,inventory.WalletGold);
                        while(low<high)
                        {
                            int mid=low+(high-low+1)/2;int quote;
                            if(QuoteShopItem(actor,candidate,mid,out quote) && quote<=inventory.WalletGold) low=mid;
                            else high=mid-1;
                        }
                        if(!TryShopApproach(actor,shelf,out var semanticApproach)) continue;
                        var semanticTarget=ActionPosition(semanticApproach);semanticTarget.PersonId=seller.Id;
                        semanticTarget.ContainerId=shelf.LoadID.ToString(CultureInfo.InvariantCulture);
                        semanticTarget.ItemId=candidate.UID.ToString(CultureInfo.InvariantCulture);semanticTarget.Quantity=1;semanticTarget.QuotedGold=price;
                        options.Add(new NpcActionOption {Id=options.Count+1,Kind=NpcActionKind.Purchase,Target=semanticTarget,
                            MaximumQuantity=low>1?low:0,
                            Label="buy up to "+low.ToString(CultureInfo.InvariantCulture)+" x "+candidate.LongName+
                                " from this verified shop stock with my own wallet; one-item quoted price="+
                                price.ToString(CultureInfo.InvariantCulture)+" gold; current wallet="+
                                inventory.WalletGold.ToString(CultureInfo.InvariantCulture)+" gold; exact total price rechecked before purchase"});
                        if(options.Count>=(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) || options.Count-semanticStart>=NpcActionCandidateCatalog.MaximumPerGroup)return;
                        continue;
                    }
                }
            }
        }
        private bool PrepareShopPurchaseAction(ResidentActor actor,NpcActionJob job,out string reason)
        {
            reason="The exact shop stock or seller is unavailable.";
            var stock=FindShopStock(job.Target);NativePerson seller;ulong uid;
            if(!stock || !TryShopSeller(out seller) || job.Target.PersonId!=seller.Id ||
                !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid)) return false;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!owner || owner.Binding==null || !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason)) return false;
            int price;if(!QuoteShopItem(actor,stock.Items.GetItem(uid),job.Target.Quantity,out price)) return false;
            if(price!=job.Target.QuotedGold) {reason="The quoted shop price changed. A new purchase agreement is required.";return false;}
            var buyer=worldStore.ReadNpcInventory(actor.Resident.Id);
            if(buyer.WalletGold<price) {reason="The NPC cannot afford the requested shop items.";return false;}
            NativeNpcInventoryBinding sellerBinding;
            if(!NativeNpcInventoryBridge.TryOpen(worldStore,seller.Id,null,NativeNpcInventoryBridge.InitialWalletGold(worldStore,seller.Id),out sellerBinding,out reason)) return false;
            Vector3 point;if(!TryShopApproach(actor,stock,out point)) {reason="No safe route reaches the shop shelf.";return false;}
            var target=ActionPosition(point);target.PersonId=seller.Id;target.ContainerId=job.Target.ContainerId;
            target.ItemId=job.Target.ItemId;target.Quantity=job.Target.Quantity;target.QuotedGold=price;job.Target=target;
            job.ServiceCode=NpcShopCommerce.ServiceCode;job.ServicePrice=price;
            job.ExpectedInventoryRevision=buyer.Revision;job.ExpectedTargetInventoryRevision=sellerBinding.Revision;
            return true;
        }
        private bool ExecuteShopPurchaseAction(NpcActionJob job,ResidentActor actor,double now,out string reason)
        {
            if(IsGuildPotionTarget(job.Target)) return ExecuteGuildPotionPurchaseAction(job,actor,now,out reason);
            reason="The shop purchase is no longer available.";
            var stock=FindShopStock(job.Target);var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!stock || !owner || owner.Binding==null) return false;
            return NativeNpcInventoryBridge.TryCompleteActionPurchase(worldStore,job,now,owner.Binding,stock.Items,()=>{
                NativePerson seller;ulong uid;int price;
                return actor.Resident.IsAlive && !QuestOwned(actor.Resident) && !IsAlarmed(job.ResidentId) &&
                    VerifiedShopStock(stock) && TryShopSeller(out seller) && seller.Id==job.Target.PersonId &&
                    Vector3.Distance(actor.transform.position,stock.transform.position)<=2.5f && ShopShelfVisible(actor.transform.position,stock) &&
                    Vector3.Distance(actor.transform.position-Vector3.up,ActionWorldPoint(job.Target))<=0.5f &&
                    ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid) &&
                    QuoteShopItem(actor,stock.Items.GetItem(uid),job.Target.Quantity,out price) && price==job.ServicePrice;
            },out reason);
        }
        private string ShopPurchaseItemName(NpcActionJob job,ResidentActor actor)
        {
            ulong uid;if(!ulong.TryParse(job.Target.ItemId,out uid)) return null;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            var owned=owner && owner.Binding!=null?owner.Binding.Items.GetItem(uid):null;
            if(owned!=null) return owned.LongName;
            var stock=FindShopStock(job.Target);var item=stock?stock.Items.GetItem(uid):null;
            return item==null?null:item.LongName;
        }
    }
}

