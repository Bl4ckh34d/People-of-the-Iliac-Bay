using System;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Banking;
using DaggerfallWorkshop.Game.Entity;
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
        private bool IsOwnPermanentHomeContainer(ResidentActor actor,DaggerfallLoot loot)
        {
            if(!actor || !loot || !interior || !directory || population==null || actor.Resident==null ||
                interior.EntryDoor.buildingKey!=actor.Resident.HomeKey ||
                !loot.gameObject.activeInHierarchy || loot.LoadID==0 || loot.ContainerType!=LootContainerTypes.HouseContainers ||
                loot.playerOwned || loot.customDrop || loot.corpseQuestUID!=0 ||
                !loot.transform.IsChildOf(interior.transform) || !loot.GetComponent<SerializableLootContainer>() ||
                loot.GetComponentInParent<QuestResourceBehaviour>() || loot.GetComponentInParent<DaggerfallAction>()) return false;
            BuildingSummary summary;
            if(!directory.GetBuildingSummary(interior.EntryDoor.buildingKey,out summary) || summary.FactionId!=0 ||
                summary.BuildingType<DFLocation.BuildingTypes.House1 || summary.BuildingType>DFLocation.BuildingTypes.House6 ||
                RMBLayout.IsShop(summary.BuildingType) || summary.BuildingType==DFLocation.BuildingTypes.Tavern ||
                DaggerfallBankManager.IsHouseOwned(interior.EntryDoor.buildingKey)) return false;
            var household=population.Households.Find(home=>home.BuildingKey==interior.EntryDoor.buildingKey);
            return NpcContainerAccessPolicy.AllowsOwnPermanentHomeContainerAccess(actor.Resident,population.SettlementId,household,
                interior.EntryDoor.buildingKey,true,loot.playerOwned,false,false,summary.FactionId!=0,
                DaggerfallBankManager.IsHouseOwned(interior.EntryDoor.buildingKey),loot.ContainerType!=LootContainerTypes.HouseContainers,
                loot.customDrop,loot.corpseQuestUID!=0,loot.GetComponentInParent<QuestResourceBehaviour>() || loot.GetComponentInParent<DaggerfallAction>());
        }

        private bool PrepareNativeHomeContainerForAccess(ResidentActor actor,DaggerfallLoot loot)
        {
            if(!IsOwnPermanentHomeContainer(actor,loot))return false;
            var game=GameManager.Instance;var building=game.PlayerEnterExit.BuildingDiscoveryData;
            if(building.buildingKey!=actor.Resident.HomeKey)return false;
            int today=DaggerfallLoot.CreateStockedDate(DaggerfallUnity.Instance.WorldTime.Now);
            bool stock=NpcHomeContainerLifecycle.ShouldNativeStock(true,
                loot.ContainerType==LootContainerTypes.HouseContainers,loot.playerOwned,loot.customDrop,
                loot.corpseQuestUID!=0,loot.GetComponentInParent<QuestResourceBehaviour>() || loot.GetComponentInParent<DaggerfallAction>(),
                loot.Items.Count,loot.stockedDate,today);
            if(stock)
            {
                // Match DFU's first-access stocker, but only at its date boundary.
                loot.StockHouseContainer(building);
                var playerActivate=UnityEngine.Object.FindObjectOfType<PlayerActivate>();
                PlayerActivate.OnLootSpawned?.Invoke(playerActivate,new ContainerLootSpawnedEventArgs
                    {ContainerType=loot.ContainerType,Loot=loot.Items});
            }
            if(NpcHomeContainerLifecycle.ShouldProtectExistingContents(loot.Items.Count,loot.stockedDate,today))
            {
                // Once available to the NPC, native stock must not erase these
                // exact contents on a later day, including newly generated stock.
                loot.stockedDate=NpcHomeContainerLifecycle.MarkUsed(loot.stockedDate,today);
            }
            if(loot.Items.Count>0)loot.houseOwned=true;
            return true;
        }

        private void OfferHomeContainerRetrievalAction(ResidentActor actor,string text,System.Collections.Generic.List<NpcActionOption> options,bool semantic=true)
        {
            if(options.Count>=(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) || !actor || !interior ||
                actor.Resident.HomeKey!=interior.EntryDoor.buildingKey) return;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();string reason;
            if(!owner || owner.Binding==null || !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason)) return;
            System.Collections.Generic.List<NpcInventoryItem> receiverItems;
            try {receiverItems=NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items);}
            catch {return;}
            if(receiverItems.Count>=NpcInventoryLimits.MaximumItems)return;
            NpcActionOption candidate=null;int matches=0,semanticMatches=0;
            var lootItems=interior.GetComponentsInChildren<DaggerfallLoot>();
            for(int containerIndex=0;containerIndex<lootItems.Length && options.Count<(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) && (!semantic || semanticMatches<NpcActionCandidateCatalog.MaximumPerGroup);containerIndex++)
            {
                var loot=lootItems[containerIndex];if(semantic?!IsOwnPermanentHomeContainer(actor,loot):!PrepareNativeHomeContainerForAccess(actor,loot)) continue;
                bool duplicateContainer=false;
                for(int prior=0;prior<containerIndex;prior++) if(lootItems[prior] && lootItems[prior].LoadID==loot.LoadID) duplicateContainer=true;
                if(duplicateContainer) continue;
                Vector3 approach;if(!TryHomeContainerApproach(actor,loot,out approach)) continue;
                for(int itemIndex=0;itemIndex<loot.Items.Count && options.Count<(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) && (!semantic || semanticMatches<NpcActionCandidateCatalog.MaximumPerGroup);itemIndex++)
                {
                    var item=loot.Items.GetItem(itemIndex);int quantity=1;
                    if(!TransferableActionItem(item) ||
                        owner.Binding.Items.GetItem(item.UID)!=null ||
                        !NativeNpcInventoryBridge.CanAppendExactItem(receiverItems,item,out reason)) continue;
                    if(semantic) quantity=item.stackCount;
                    var entity=actor.GetComponent<DaggerfallEntityBehaviour>();
                    if(!entity || entity.Entity==null || entity.Entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()*quantity>entity.Entity.MaxEncumbrance) continue;
                    var target=ActionPosition(approach);target.ContainerId=loot.LoadID.ToString(CultureInfo.InvariantCulture);
                    target.ItemId=item.UID.ToString(CultureInfo.InvariantCulture);target.Quantity=quantity;
                    target.TransferDirection=NpcActionTransferDirection.HomeContainerToNpc;
                    if(semantic)
                    {
                        if(semanticMatches>=1) continue;
                        options.Add(new NpcActionOption {Id=options.Count+1,Kind=NpcActionKind.Transfer,Target=target,
                            Label="retrieve "+quantity.ToString(CultureInfo.InvariantCulture)+" x "+item.LongName+" from household storage in this home"});
                        semanticMatches++;continue;
                    }
                    matches++;
                    if(matches>1){candidate=null;continue;}
                    candidate=new NpcActionOption {Kind=NpcActionKind.Transfer,Target=target,
                        Label="retrieve "+quantity.ToString(CultureInfo.InvariantCulture)+" x "+item.LongName+" from household storage in this home"};
                }
            }
            if(matches==1 && candidate!=null)
            {candidate.Id=options.Count+1;options.Add(candidate);}
        }

        private DaggerfallLoot ResolveOwnHomeRetrievalContainer(ResidentActor actor,NpcActionTarget target)
        {
            if(!ActionSpaceMatches(target) || target==null || target.Kind!=NpcActionTargetKind.IndoorPoint || string.IsNullOrWhiteSpace(target.ContainerId)) return null;
            ulong loadId;if(!ulong.TryParse(target.ContainerId,NumberStyles.None,CultureInfo.InvariantCulture,out loadId) || loadId==0) return null;
            DaggerfallLoot found=null;int matches=0;
            foreach(var loot in interior.GetComponentsInChildren<DaggerfallLoot>())
                if(loot && loot.LoadID==loadId){matches++;found=loot;}
            return matches==1 && IsOwnPermanentHomeContainer(actor,found)?found:null;
        }

        private bool PrepareHomeContainerRetrievalAction(ResidentActor actor,NpcActionJob job,out string reason)
        {
            reason="The household container or exact item is no longer available.";
            var loot=job==null?null:ResolveOwnHomeRetrievalContainer(actor,job.Target);
            var owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;ulong uid;
            if(job==null || job.Target==null || job.Target.TransferDirection!=NpcActionTransferDirection.HomeContainerToNpc ||
                !loot || !owner || owner.Binding==null || !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid) ||
                job.Target.Quantity<1 || job.Target.PersonId!=null) return false;
            if(!NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason)) return false;
            var item=loot.Items.GetItem(uid);var entity=actor.GetComponent<DaggerfallEntityBehaviour>();
            if(!TransferableActionItem(item) || item.stackCount!=job.Target.Quantity || owner.Binding.Items.GetItem(uid)!=null ||
                !entity || entity.Entity==null || entity.Entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()*job.Target.Quantity>entity.Entity.MaxEncumbrance ||
                !NativeNpcInventoryBridge.CanAppendExactItem(owner.Binding.Items,item,out reason))
            {reason="The exact whole household item is protected, missing, too heavy or no longer matches the request.";return false;}
            job.ExpectedInventoryRevision=0;job.ExpectedTargetInventoryRevision=owner.Binding.Revision;return true;
        }

        private bool ExecuteHomeContainerRetrievalAction(NpcActionJob job,ResidentActor actor,double now,out string reason)
        {
            reason="The household container retrieval is no longer valid.";
            var loot=job==null?null:ResolveOwnHomeRetrievalContainer(actor,job.Target);
            var owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;
            if(!loot || !owner || owner.Binding==null || !ActionSpaceMatches(job.Target) ||
                Vector3.Distance(actor.transform.position-Vector3.up,ActionWorldPoint(job.Target))>0.5f) return false;
            int previousStockDate=loot.stockedDate;bool completed=false;
            try
            {
                completed=NativeNpcInventoryBridge.TryCompleteActionTransfer(worldStore,job,now,null,loot.Items,owner.Binding,null,()=>{
                ulong uid;var entity=actor.GetComponent<DaggerfallEntityBehaviour>();
                if(!actor.Resident.IsAlive || actor.Resident.IsQuestProtected || QuestOwned(actor.Resident) || IsAlarmed(job.ResidentId) ||
                    !ActionSpaceMatches(job.Target) || Vector3.Distance(actor.transform.position-Vector3.up,ActionWorldPoint(job.Target))>0.5f ||
                    ResolveOwnHomeRetrievalContainer(actor,job.Target)!=loot || !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid)) return false;
                var item=loot.Items.GetItem(uid);
                string capacityReason;
                bool valid=TransferableActionItem(item) && item.stackCount==job.Target.Quantity && owner.Binding.Items.GetItem(uid)==null &&
                    NativeNpcInventoryBridge.CanAppendExactItem(owner.Binding.Items,item,out capacityReason) &&
                    entity && entity.Entity!=null && entity.Entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()*job.Target.Quantity<=entity.Entity.MaxEncumbrance;
                if(valid)
                {
                    int usedDate=NpcHomeContainerLifecycle.MarkUsed(loot.stockedDate,
                        DaggerfallLoot.CreateStockedDate(DaggerfallUnity.Instance.WorldTime.Now));
                    if(usedDate!=loot.stockedDate)loot.stockedDate=usedDate;
                }
                return valid;
                },out reason);
                return completed;
            }
            finally {if(!completed)loot.stockedDate=previousStockDate;}
        }

        private bool TryHomeContainerApproach(ResidentActor actor,DaggerfallLoot loot,out Vector3 chosen)
        {
            chosen=Vector3.zero;
            if(!actor || !loot || !interior || indoorGeometry==null) return false;
            var local=interior.transform.InverseTransformPoint(loot.transform.position);
            var from=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            foreach(var point in indoorGeometry.NearbyApproachPoints(local,1.0f,2.5f,20))
            {
                if(Vector3.Distance(interior.transform.TransformPoint(point)+Vector3.up,loot.transform.position)>2.5f) continue;
                var route=indoorGeometry.RouteBetween(from,point);
                if(Vector3.Distance(from,point)>0.25f && (route==null || route.Count<2)) continue;
                chosen=interior.transform.TransformPoint(point);return true;
            }
            return false;
        }

        private void OfferHomeContainerDepositAction(ResidentActor actor,string text,System.Collections.Generic.List<NpcActionOption> options,bool semantic=true)
        {
            if(options.Count>=(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) || !actor || !interior ||
                actor.Resident.HomeKey!=interior.EntryDoor.buildingKey) return;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();string reason;
            if(!owner || owner.Binding==null || !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason)) return;
            var lootItems=interior.GetComponentsInChildren<DaggerfallLoot>();
            int semanticDeposits=0;
            for(int containerIndex=0;containerIndex<lootItems.Length && options.Count<(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) && (!semantic || semanticDeposits<NpcActionCandidateCatalog.MaximumPerGroup);containerIndex++)
            {
                var loot=lootItems[containerIndex];if(semantic?!IsOwnPermanentHomeContainer(actor,loot):!PrepareNativeHomeContainerForAccess(actor,loot)) continue;
                bool duplicateContainer=false;
                for(int prior=0;prior<containerIndex;prior++) if(lootItems[prior] && lootItems[prior].LoadID==loot.LoadID) duplicateContainer=true;
                if(duplicateContainer) continue;
                Vector3 approach;if(!TryHomeContainerApproach(actor,loot,out approach)) continue;
                for(int itemIndex=0;itemIndex<owner.Binding.Items.Count && options.Count<(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) && (!semantic || semanticDeposits<NpcActionCandidateCatalog.MaximumPerGroup);itemIndex++)
                {
                    var item=owner.Binding.Items.GetItem(itemIndex);int quantity=1;
                    if(!TransferableActionItem(item) ||
                        loot.Items.GetItem(item.UID)!=null) continue;
                    if(semantic) quantity=item.stackCount;
                    var target=ActionPosition(approach);target.ContainerId=loot.LoadID.ToString(CultureInfo.InvariantCulture);
                    target.ItemId=item.UID.ToString(CultureInfo.InvariantCulture);target.Quantity=quantity;
                    target.TransferDirection=NpcActionTransferDirection.NpcToContainer;
                    options.Add(new NpcActionOption {Id=options.Count+1,Kind=NpcActionKind.Transfer,Target=target,
                        Label="store "+quantity.ToString(CultureInfo.InvariantCulture)+" x "+item.LongName+" in my own permanent home container"});
                    if(semantic) semanticDeposits++;
                }
            }
        }

        private DaggerfallLoot ResolveOwnHomeContainer(ResidentActor actor,NpcActionTarget target)
        {
            if(!ActionSpaceMatches(target) || target==null || target.Kind!=NpcActionTargetKind.IndoorPoint || string.IsNullOrWhiteSpace(target.ContainerId)) return null;
            ulong loadId;if(!ulong.TryParse(target.ContainerId,NumberStyles.None,CultureInfo.InvariantCulture,out loadId) || loadId==0) return null;
            DaggerfallLoot found=null;int matches=0;
            foreach(var loot in interior.GetComponentsInChildren<DaggerfallLoot>())
                if(loot && loot.LoadID==loadId)
                {matches++;found=loot;}
            return matches==1 && IsOwnPermanentHomeContainer(actor,found)?found:null;
        }

        private bool PrepareHomeContainerDepositAction(ResidentActor actor,NpcActionJob job,out string reason)
        {
            reason="The NPC's own home container or exact item is unavailable.";
            var loot=job==null?null:ResolveOwnHomeContainer(actor,job.Target);var owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;
            ulong uid;
            if(job==null || job.Target==null || job.Target.TransferDirection!=NpcActionTransferDirection.NpcToContainer ||
                !loot || !owner || owner.Binding==null || !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid) ||
                job.Target.Quantity<1 || job.Target.PersonId!=null || loot.Items.GetItem(uid)!=null) return false;
            if(!NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason)) return false;
            var item=owner.Binding.Items.GetItem(uid);
            if(!TransferableActionItem(item) || job.Target.Quantity!=item.stackCount)
            {reason="The exact whole stack is missing, protected or no longer matches the saved request.";return false;}
            job.ExpectedInventoryRevision=owner.Binding.Revision;job.ExpectedTargetInventoryRevision=0;return true;
        }

        private bool ExecuteHomeContainerDepositAction(NpcActionJob job,ResidentActor actor,double now,out string reason)
        {
            reason="The home container deposit is no longer valid.";
            var loot=job==null?null:ResolveOwnHomeContainer(actor,job.Target);var owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;
            if(!loot || !owner || owner.Binding==null || !ActionSpaceMatches(job.Target) ||
                Vector3.Distance(actor.transform.position-Vector3.up,ActionWorldPoint(job.Target))>0.5f) return false;
            int previousStockDate=loot.stockedDate;bool completed=false;
            try
            {
                completed=NativeNpcInventoryBridge.TryCompleteActionTransfer(worldStore,job,now,owner.Binding,null,loot.Items,()=>{
                ulong uid;
                if(!actor.Resident.IsAlive || actor.Resident.IsQuestProtected || QuestOwned(actor.Resident) || IsAlarmed(job.ResidentId) ||
                    !ActionSpaceMatches(job.Target) || Vector3.Distance(actor.transform.position-Vector3.up,ActionWorldPoint(job.Target))>0.5f ||
                    ResolveOwnHomeContainer(actor,job.Target)!=loot || !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid) ||
                    loot.Items.GetItem(uid)!=null) return false;
                var item=owner.Binding.Items.GetItem(uid);
                bool valid=TransferableActionItem(item) && item.stackCount==job.Target.Quantity;
                if(valid)
                {
                    // A successful NPC deposit turns this particular home chest
                    // into persistent storage. DFU's next-day native stocker
                    // clears Items, so encode the opt-out in its saved date.
                    loot.stockedDate=NpcHomeContainerLifecycle.MarkPersistentStorage();
                }
                return valid;
                },out reason);
                return completed;
            }
            finally {loot.stockedDate=NpcHomeContainerLifecycle.RestoreStockDateAfterFailedDeposit(previousStockDate,loot.stockedDate,completed);}
        }
    }
}

