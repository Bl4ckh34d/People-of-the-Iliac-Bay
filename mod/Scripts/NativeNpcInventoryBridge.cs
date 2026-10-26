using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class NativeNpcInventoryBinding
    {
        public string PersonId {get;internal set;}
        public ItemCollection Items {get;internal set;}
        public long Revision {get;internal set;}
    }
    // Bindings are short-lived leases on this save branch, not a global cache.
    // ItemCollections passed by callers remain their exact collection objects.
    public static partial class NativeNpcInventoryBridge
    {
        private static readonly FieldInfo customRegistry=typeof(ItemCollection).GetField("customItems",BindingFlags.Static|BindingFlags.NonPublic);
        private static readonly MethodInfo findStack=typeof(ItemCollection).GetMethod("FindExistingStack",BindingFlags.Instance|BindingFlags.NonPublic);

        public static int InitialWalletGold(WorldStateStore store,string personId)
        {return store.StartingNpcGold(personId,HomeLocationType,IsNobleFaction);}

        public static int CashWithdrawalGold(WorldStateStore store,string personId)
        {return store.NpcCashWithdrawalGold(personId,HomeLocationType,IsNobleFaction);}

        private static int HomeLocationType(int mapId)
        {
            var unity=DaggerfallWorkshop.DaggerfallUnity.Instance;
            if(unity==null || unity.ContentReader==null)return -1;
            int pixel=mapId & 0x000fffff;
            DaggerfallWorkshop.Utility.ContentReader.MapSummary summary;
            if(!unity.ContentReader.HasLocation(pixel%1000,pixel/1000,out summary) || summary.MapID!=mapId)return -1;
            DaggerfallConnect.DFLocation location;
            return unity.ContentReader.GetLocation(summary.RegionIndex,summary.MapIndex,out location)?(int)location.MapTableData.LocationType:-1;
        }
        private static bool IsNobleFaction(int factionId)
        {
            var game=DaggerfallWorkshop.Game.GameManager.Instance;
            DaggerfallConnect.Arena2.FactionFile.FactionData faction;
            return game!=null && game.PlayerEntity!=null && game.PlayerEntity.FactionData.GetFactionData(factionId,out faction) &&
                faction.sgroup==(int)DaggerfallConnect.Arena2.FactionFile.SocialGroups.Nobility;
        }

        public static List<NpcInventoryItem> CaptureItems(ItemCollection items)
        {
            if(items==null)throw new ArgumentNullException(nameof(items));
            if(items.Count>NpcInventoryLimits.MaximumItems)throw new InvalidOperationException("NPC inventory item limit exceeded.");
            var result=new List<NpcInventoryItem>();var ids=new HashSet<ulong>();long bytes=0;
            for(int i=0;i<items.Count;i++)
            {
                var item=items.GetItem(i);var data=item.GetSaveData();
                if(data==null || data.uid!=item.UID || data.uid==0 || !ids.Add(data.uid) || data.stackCount<=0)
                    throw new InvalidOperationException("Invalid native item identity or stack.");
                string payload=SaveLoadManager.Serialize(typeof(ItemData_v1),data,false);
                bytes+=payload.Length;
                if(payload.Length>NpcInventoryLimits.MaximumItemPayloadLength || bytes>NpcInventoryLimits.MaximumInventoryPayloadLength)
                    throw new InvalidOperationException("NPC inventory payload limit exceeded.");
                result.Add(new NpcInventoryItem{Uid=data.uid,NativePayload=payload});
            }
            return result;
        }
        public static bool CanAppendExactItem(ItemCollection destination,DaggerfallUnityItem item,out string reason)
        {
            reason="The NPC inventory cannot safely accept this exact item.";
            try
            {
                if(destination==null || item==null)return false;
                return CanAppendExactItem(CaptureItems(destination),item,out reason);
            }
            catch(Exception error){reason=error.Message;return false;}
        }
        public static bool CanAppendExactItem(IList<NpcInventoryItem> existing,DaggerfallUnityItem item,out string reason)
        {
            reason="The NPC inventory cannot safely accept this exact item.";
            try
            {
                if(existing==null || item==null)return false;
                var data=item.GetSaveData();
                if(data==null || data.uid!=item.UID || data.uid==0 || data.stackCount<=0)return false;
                string payload=SaveLoadManager.Serialize(typeof(ItemData_v1),data,false);
                if(!NpcInventoryLimits.CanAppendExactItem(existing,new NpcInventoryItem{Uid=data.uid,NativePayload=payload}))return false;
                reason=null;return true;
            }
            catch(Exception error){reason=error.Message;return false;}
        }
        public static bool TryOpen(WorldStateStore store,string personId,ItemCollection initialItems,int initialGold,
            out NativeNpcInventoryBinding binding,out string reason)
        {
            binding=null;reason=null;
            try
            {
                var saved=store.ReadNpcInventory(personId);
                if(saved==null)
                {
                    if(!store.InitializeNpcInventory(personId,initialGold,CaptureItems(initialItems??new ItemCollection())))
                    {reason="Unknown inventory owner or conflicting native UID.";return false;}
                    saved=store.ReadNpcInventory(personId);
                }
                var records=new List<ItemData_v1>();
                foreach(var record in saved.Items)
                {
                    var data=(ItemData_v1)SaveLoadManager.Deserialize(typeof(ItemData_v1),record.NativePayload);
                    if(data==null || data.uid!=record.Uid || data.stackCount<=0)throw new InvalidOperationException("Saved native item UID or stack is invalid.");
                    if(!string.IsNullOrEmpty(data.className))
                    {
                        var registry=customRegistry==null?null:customRegistry.GetValue(null) as IDictionary<string,Type>;
                        if(registry==null || !registry.ContainsKey(data.className))
                            throw new InvalidOperationException("Saved custom item class is unavailable: "+data.className);
                    }
                    records.Add(data);
                }
                var items=new ItemCollection();items.DeserializeItems(records.ToArray());
                if(items.Count!=records.Count)throw new InvalidOperationException("Native inventory decode changed item identities.");
                foreach(var record in records)if(items.GetItem(record.uid)==null)throw new InvalidOperationException("Native inventory decode lost an item UID.");
                binding=new NativeNpcInventoryBinding{PersonId=personId,Items=items,Revision=saved.Revision};return true;
            }
            catch(Exception error){reason=error.Message;return false;}
        }
        public static bool TryCapture(WorldStateStore store,NativeNpcInventoryBinding binding,out string reason)
        {
            reason=null;
            try
            {
                var saved=store.ReadNpcInventory(binding.PersonId);
                var items=CaptureItems(binding.Items);
                if(saved==null){reason="NPC inventory owner is unavailable.";return false;}
                // A service can change only the wallet while a live actor keeps
                // the same collection. Refresh that lease without overwriting
                // a changed item payload from another binding.
                if(saved.Revision!=binding.Revision)
                {
                    if(!SameItems(saved.Items,items)){reason="NPC inventory lease is stale.";return false;}
                    binding.Revision=saved.Revision;return true;
                }
                if(SameItems(saved.Items,items))return true;
                if(!store.CommitNpcInventoryTransaction("native-capture:"+binding.Revision,new[]{new NpcInventoryUpdate{
                    PersonId=binding.PersonId,ExpectedRevision=binding.Revision,WalletGold=saved.WalletGold,Items=items}}))
                {reason="Native inventory capture was rejected.";return false;}
                binding.Revision++;return true;
            }
            catch(Exception error){reason=error.Message;return false;}
        }
        public static bool TryPayService(WorldStateStore store,string personId,int gold,string transactionId,long expectedRevision,out string reason)
        {
            bool paid=store.TryPayNpcService(personId,gold,transactionId,expectedRevision);
            reason=paid?null:"NPC service payment rejected: funds, ownership, receipt or revision changed.";return paid;
        }
        public static bool TryMaterializeCorpseWallet(WorldStateStore store,NativeNpcInventoryBinding binding,out string reason)
        {
            reason=null;DaggerfallUnityItem gold=null;
            try
            {
                var saved=store.ReadNpcInventory(binding.PersonId);
                if(saved==null || saved.Revision!=binding.Revision)throw new InvalidOperationException("Corpse inventory lease is stale.");
                if(saved.WalletGold==0)return true;
                gold=ItemBuilder.CreateGoldPieces(saved.WalletGold);binding.Items.AddItem(gold,noStack:true);
                if(!store.CommitNpcInventoryTransaction("corpse-wallet:"+saved.Revision,new[]{new NpcInventoryUpdate{
                    PersonId=binding.PersonId,ExpectedRevision=saved.Revision,WalletGold=0,Items=CaptureItems(binding.Items)}}))
                    throw new InvalidOperationException("Corpse wallet conversion was rejected.");
                binding.Revision++;return true;
            }
            catch(Exception error){if(gold!=null)binding.Items.RemoveItem(gold);reason=error.Message;return false;}
        }
        public static bool TryTransfer(WorldStateStore store,NativeNpcInventoryBinding source,NativeNpcInventoryBinding target,
            ulong itemUid,int count,string transactionId,out string reason)
        {NpcActionTransferReceipt ignored;return Execute(store,source.Items,target.Items,source,target,null,0,0,itemUid,count,()=>true,transactionId,false,null,0,out ignored,out reason);}
        public static bool TryGiveToExternal(WorldStateStore store,NativeNpcInventoryBinding source,ItemCollection target,
            ulong itemUid,int count,Func<bool> accessIsValid,string transactionId,out string reason)
        {NpcActionTransferReceipt ignored;return Execute(store,source.Items,target,source,null,null,0,0,itemUid,count,accessIsValid,transactionId,false,null,0,out ignored,out reason);}
        public static bool TryCompleteActionHandover(WorldStateStore store,NpcActionJob job,double now,NativeNpcInventoryBinding source,
            ItemCollection target,Func<bool> accessIsValid,out string reason)
        {return TryCompleteActionTransfer(store,job,now,source,null,target,accessIsValid,out reason);}
        public static bool TryCompleteActionTransfer(WorldStateStore store,NpcActionJob job,double now,NativeNpcInventoryBinding source,
            NativeNpcInventoryBinding targetBinding,ItemCollection externalTarget,Func<bool> accessIsValid,out string reason)
        {
            return TryCompleteActionTransfer(store,job,now,source,null,targetBinding,externalTarget,accessIsValid,out reason);
        }
        public static bool TryCompleteActionTransfer(WorldStateStore store,NpcActionJob job,double now,NativeNpcInventoryBinding source,
            ItemCollection externalSource,NativeNpcInventoryBinding targetBinding,ItemCollection externalTarget,Func<bool> accessIsValid,out string reason)
        {
            reason="The action, item owner or inventory revision changed.";ulong uid;
            if(store==null || job==null || job.Target==null || (source==null)==(externalSource==null) ||
                (targetBinding==null)==(externalTarget==null)) return false;
            var current=store.ReadNpcAction(job.Id);
            if(current==null || current.Revision!=job.Revision) return false;
            job=current;
            if(job.Kind!=NpcActionKind.Transfer || job.Target==null ||
                !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid) || uid==0 || job.Target.Quantity<1 ||
                (int)job.Target.TransferDirection<0 || (int)job.Target.TransferDirection>4) return false;
            bool incoming=job.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc;
            bool containerDeposit=job.Target.TransferDirection==NpcActionTransferDirection.NpcToContainer;
            bool homeContainerRetrieval=job.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc;
            bool observedLoot=job.Target.TransferDirection==NpcActionTransferDirection.ObservedLootToNpc;
            if(incoming)
            {
                if(source!=null || externalSource==null || targetBinding==null || externalTarget!=null ||
                    job.Target.PersonId!="player" || targetBinding.PersonId!=job.ResidentId ||
                    targetBinding.Revision!=job.ExpectedInventoryRevision || job.ExpectedTargetInventoryRevision!=0) return false;
            }
            else if(homeContainerRetrieval || observedLoot)
            {
                if(source!=null || externalSource==null || targetBinding==null || externalTarget!=null ||
                    (homeContainerRetrieval && job.Target.Kind!=NpcActionTargetKind.IndoorPoint) ||
                    (observedLoot && job.Target.Kind!=NpcActionTargetKind.IndoorPoint && job.Target.Kind!=NpcActionTargetKind.WorldPoint) ||
                    string.IsNullOrWhiteSpace(job.Target.ContainerId) ||
                    job.Target.PersonId!=null || job.ExpectedInventoryRevision!=0 || targetBinding.PersonId!=job.ResidentId ||
                    targetBinding.Revision!=job.ExpectedTargetInventoryRevision) return false;
            }
            else
            {
                if(source==null || externalSource!=null || source.PersonId!=job.ResidentId || source.Revision!=job.ExpectedInventoryRevision) return false;
                if(containerDeposit)
                {
                    if(job.Target.Kind!=NpcActionTargetKind.IndoorPoint || string.IsNullOrWhiteSpace(job.Target.ContainerId) ||
                        job.Target.PersonId!=null || targetBinding!=null || externalTarget==null || job.ExpectedTargetInventoryRevision!=0) return false;
                }
                else if(targetBinding==null)
                {
                    if(externalTarget==null || job.Target.Kind!=NpcActionTargetKind.Person || job.Target.PersonId!="player" || job.ExpectedTargetInventoryRevision!=0) return false;
                }
                else if(externalTarget!=null || targetBinding.PersonId=="player" || targetBinding.PersonId!=job.Target.PersonId ||
                    targetBinding.PersonId==source.PersonId || targetBinding.Revision!=job.ExpectedTargetInventoryRevision) return false;
            }
            string failure=null;
            bool completed=store.TryCompleteNpcActionEffectWithReceipt(job.Id,job.Revision,now,()=>
            {
                NpcActionTransferReceipt receipt;
                bool applied=Execute(store,source==null?externalSource:source.Items,targetBinding==null?externalTarget:targetBinding.Items,
                    source,targetBinding,null,0,0,uid,job.Target.Quantity,accessIsValid,job.Id+":transfer",
                    containerDeposit || homeContainerRetrieval || observedLoot,
                    (incoming || source!=null && targetBinding!=null)?job:null,now,out receipt,out failure);
                return applied?NpcActionEffectResult.Success(receipt):NpcActionEffectResult.Failure();
            });
            if(failure!=null) reason=failure;else if(completed) reason=null;
            return completed;
        }
        public static bool TryReceiveFromExternal(WorldStateStore store,ItemCollection source,NativeNpcInventoryBinding target,
            ulong itemUid,int count,Func<bool> accessIsValid,string transactionId,out string reason)
        {NpcActionTransferReceipt ignored;return Execute(store,source,target.Items,null,target,null,0,0,itemUid,count,accessIsValid,transactionId,false,null,0,out ignored,out reason);}
        public static bool TryPurchase(WorldStateStore store,NativeNpcInventoryBinding buyer,ItemCollection actualStock,string sellerPersonId,
            long expectedSellerRevision,ulong itemUid,int count,int totalPrice,Func<bool> stockAccessIsValid,string transactionId,out string reason)
        {
            if(totalPrice<=0){reason="Purchase requires a positive verified total price.";return false;}
            NpcActionTransferReceipt ignored;return Execute(store,actualStock,buyer.Items,null,buyer,sellerPersonId,expectedSellerRevision,totalPrice,itemUid,count,stockAccessIsValid,transactionId,false,null,0,out ignored,out reason);
        }
        private static bool SameItems(List<NpcInventoryItem> a,List<NpcInventoryItem> b)
        {
            if(a.Count!=b.Count)return false;
            var indexed=new Dictionary<ulong,string>();foreach(var item in a)indexed.Add(item.Uid,item.NativePayload);
            foreach(var item in b){string payload;if(!indexed.TryGetValue(item.Uid,out payload) || payload!=item.NativePayload)return false;}return true;
        }
        private static NpcInventoryState CheckBinding(WorldStateStore store,NativeNpcInventoryBinding binding,string transactionId)
        {
            if(binding==null)return null;
            var saved=store.ReadNpcInventory(binding.PersonId);
            if(saved==null || saved.Revision!=binding.Revision || saved.LastTransactionId==transactionId || !SameItems(saved.Items,CaptureItems(binding.Items)))
                throw new InvalidOperationException("NPC inventory changed before transaction.");
            return saved;
        }
        private static bool Execute(WorldStateStore store,ItemCollection source,ItemCollection target,NativeNpcInventoryBinding sourceBinding,
            NativeNpcInventoryBinding targetBinding,string sellerId,long sellerRevision,int price,ulong uid,int count,Func<bool> access,
            string transactionId,bool preserveUid,NpcActionJob receiptJob,double observedGameTime,
            out NpcActionTransferReceipt transferReceipt,out string reason)
        {
            reason=null;transferReceipt=null;DaggerfallUnityItem original=null,moved=null,merged=null;int originalCount=0,mergedCount=0;bool touched=false,committed=false;
            try
            {
                if(store==null || source==null || target==null || ReferenceEquals(source,target) || access==null || !access() || string.IsNullOrWhiteSpace(transactionId))
                    throw new InvalidOperationException("Inventory transfer access is unavailable.");
                var from=CheckBinding(store,sourceBinding,transactionId);var to=CheckBinding(store,targetBinding,transactionId);
                if(from==null && to==null || from!=null && to!=null && from.PersonId==to.PersonId)throw new InvalidOperationException("Transfer requires distinct persistent ownership.");
                NpcInventoryState seller=null;
                if(price>0)
                {
                    if(store.NpcInventoryItemOwner(uid)!=null)
                        throw new InvalidOperationException("Shop stock UID already belongs to a persistent NPC inventory.");
                    seller=store.ReadNpcInventory(sellerId);
                    if(to==null || seller==null || seller.PersonId==to.PersonId || seller.Revision!=sellerRevision || seller.LastTransactionId==transactionId ||
                        to.WalletGold<price || seller.WalletGold>NpcInventoryLimits.MaximumWalletGold-price)
                        throw new InvalidOperationException("Purchase wallet, seller or revision is invalid.");
                }
                original=source.GetItem(uid);
                if(original==null || original.IsQuestItem || original.IsEquipped || original.IsSummoned || original is HouseholdPropertyKeyItem ||
                    count<1 || count>original.stackCount || target.GetItem(uid)!=null)
                    throw new InvalidOperationException("Item is missing, protected, equipped or has insufficient quantity.");
                if(preserveUid && count!=original.stackCount)
                    throw new InvalidOperationException("An exact-UID container deposit must move the complete stack.");
                if(count<original.stackCount && (!original.IsAStack() || !string.IsNullOrEmpty(original.GetSaveData().className)))
                    throw new InvalidOperationException("This native/custom item cannot safely split.");
                if(findStack==null)throw new InvalidOperationException("Native stack API is unavailable.");
                // Acquisitions retain the exact purchased native object/UID (or
                // native split UID). Later handovers use normal native merging.
                merged=price>0 || preserveUid?null:(DaggerfallUnityItem)findStack.Invoke(target,new object[]{original});
                originalCount=original.stackCount;mergedCount=merged==null?0:merged.stackCount;
                if(merged!=null && (merged.IsQuestItem || mergedCount>int.MaxValue-count))throw new InvalidOperationException("Target stack is protected or full.");
                touched=true;moved=count==originalCount?original:source.SplitStack(original,count);
                if(moved==null)throw new InvalidOperationException("Native stack split failed.");
                if(price>0 || preserveUid) {source.RemoveItem(moved);target.AddItem(moved,noStack:true);}
                else target.Transfer(moved,source);
                if(source.Contains(moved) || (merged==null ? !ReferenceEquals(target.GetItem(moved.UID),moved) : merged.stackCount!=mergedCount+count))
                    throw new InvalidOperationException("Native transfer did not produce the expected ownership.");
                if(receiptJob!=null)
                {
                    ulong retainedUid=merged==null?moved.UID:merged.UID;
                    var retained=target.GetItem(retainedUid);
                    int expectedCount=merged==null?count:mergedCount+count;
                    if(retained==null || retained.stackCount!=expectedCount)
                        throw new InvalidOperationException("Received item UID or retained stack count is not available for a durable receipt.");
                    var valuation=NpcExperienceNativeValuationStatus.Unknown;long nativeValue=0;
                    // Native base value is evidence, not a negotiated sale price or proof of gift intent.
                    // Custom items keep unknown valuation; their value semantics are not ours to infer.
                    if(moved.GetType()==typeof(DaggerfallUnityItem) && moved.TemplateIndex>=0 &&
                        moved.TemplateIndex<=ItemHelper.LastDFTemplate && moved.value>=0)
                    {
                        try
                        {
                            var impact=NpcExperienceImpactDetails.ForItemTransfer(count,
                                NpcExperienceNativeValuationStatus.Known,(long)moved.value*count);
                            valuation=impact.NativeValuationStatus;nativeValue=impact.NativeGoldValue;
                        }
                        catch(ArgumentException) { /* Outside the existing impact bounds stays unknown. */ }
                    }
                    string itemName=NativeItemReceiptLabel.Capture(moved.LongName);
                    transferReceipt=sourceBinding==null?
                        NpcActionTransferReceipt.CreatePlayerToNpc(receiptJob,transactionId,uid,moved.UID,
                            retainedUid,count,targetBinding.PersonId,"player",observedGameTime,valuation,nativeValue,itemName):
                        NpcActionTransferReceipt.CreateNpcToNpc(receiptJob,transactionId,uid,moved.UID,
                            retainedUid,count,targetBinding.PersonId,sourceBinding.PersonId,observedGameTime,valuation,nativeValue,itemName);
                }
                var updates=new List<NpcInventoryUpdate>();
                if(from!=null)updates.Add(new NpcInventoryUpdate{PersonId=from.PersonId,ExpectedRevision=from.Revision,WalletGold=from.WalletGold,Items=CaptureItems(source)});
                if(to!=null)updates.Add(new NpcInventoryUpdate{PersonId=to.PersonId,ExpectedRevision=to.Revision,WalletGold=to.WalletGold-price,Items=CaptureItems(target)});
                if(seller!=null)updates.Add(new NpcInventoryUpdate{PersonId=seller.PersonId,ExpectedRevision=seller.Revision,WalletGold=seller.WalletGold+price,Items=seller.Items});
                if(!store.CommitNpcInventoryTransaction(transactionId,updates))throw new InvalidOperationException("Inventory revision or UID conflict rejected the transaction.");
                committed=true;if(sourceBinding!=null)sourceBinding.Revision++;if(targetBinding!=null)targetBinding.Revision++;
                TryRecordExactPlayerGiftSource(store,source,target,sourceBinding,targetBinding,sellerId,
                    receiptJob,transactionId,uid,count,originalCount,merged,moved);
                return true;
            }
            catch(Exception error){transferReceipt=null;reason=error.Message;return false;}
            finally
            {
                if(touched && !committed)
                {
                    // Restore only the moved objects/counts. Never rebuild player
                    // collections or invalidate unrelated equipment references.
                    if(moved!=null){target.RemoveItem(moved);if(!ReferenceEquals(moved,original))source.RemoveItem(moved);}
                    if(merged!=null)merged.stackCount=mergedCount;
                    original.stackCount=originalCount;if(!source.Contains(original))source.AddItem(original,noStack:true);
                }
            }
        }
    }
}
