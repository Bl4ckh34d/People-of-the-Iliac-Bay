using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit disposable fixture only. Exercises DFU's real disk save/load callbacks,
    // including inventory and action reconstruction; no in-memory branch stands in for Load.
    public static class NpcActionDiskProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static IEnumerator Run(StreetPopulation streets,WorldStateStore store,ResidentActor actor,string transferId,
            string feedback,string inboundTransferId)
        {
            var game=GameManager.Instance;
            string character=game.PlayerEntity.Name;
            if(!NativeProbeText.IsTestCharacter(character) ||
                Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-disk-probe")<0)
                throw new InvalidOperationException("Action disk probe requires an explicit disposable character.");
            string resident=actor.Resident.Id;
            // Move only the test observer into conversation range, never the resident.
            game.PlayerMotor.transform.position=actor.transform.position+Vector3.right*2;
            string reason="No wait option";NpcActionJob wait=null;
            NpcActionOption follow=streets.ConversationActionOptions(resident,"Follow me.").Find(o=>o.Kind==NpcActionKind.Follow);
            foreach(var option in streets.ConversationActionOptions(resident,"Wait here."))
                if(option.Kind==NpcActionKind.Wait && follow!=null && NpcProbeCommandSource.TryAcceptTask(streets,resident,"disk-probe:"+Guid.NewGuid().ToString("N"),
                    new List<NpcActionDecision>{new NpcActionDecision {Option=option,DurationSeconds=300},
                        new NpcActionDecision {Option=follow,DurationSeconds=10}},out reason))
                {wait=store.ActiveNpcAction(resident);break;}
            if(wait==null)throw new InvalidOperationException("Disk probe wait rejected: "+reason);
            float deadline=Time.realtimeSinceStartup+10;
            while(store.ReadNpcAction(wait.Id).Phase!=NpcActionPhase.Executing && Time.realtimeSinceStartup<deadline)yield return null;
            wait=store.ReadNpcAction(wait.Id);
            if(wait.Phase!=NpcActionPhase.Executing)throw new InvalidOperationException("Disk fixture wait did not start.");
            var task=store.ActiveNpcTask(resident);
            string taskPayload=SaveLoadManager.Serialize(typeof(NpcTaskPlan),task,false);
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();owner.Capture();
            string inventory=SaveLoadManager.Serialize(store.ReadNpcInventory(resident).GetType(),store.ReadNpcInventory(resident),false);
            var completed=store.ReadNpcAction(transferId);
            var inbound=store.ReadNpcAction(inboundTransferId);
            var inboundReceipt=inbound==null?null:inbound.ItemTransferReceipt;
            if(inbound==null || inbound.Phase!=NpcActionPhase.Completed || inboundReceipt==null ||
                inboundReceipt.TransactionId!=inbound.Id+":transfer" || inboundReceipt.RecipientId!=resident ||
                inboundReceipt.CounterpartyId!="player" || inboundReceipt.Quantity!=1)
                throw new InvalidOperationException("Disk probe requires the exact completed player-to-NPC action receipt from the inbound probe.");
            var inboundEvent=inboundReceipt.ToNativeEvent(inbound.Id);
            var inboxBefore=store.Snapshot().NpcExperienceInbox.Entries.FindAll(entry=>entry.ObserverId==resident &&
                entry.DeduplicationKey==inboundEvent.DeduplicationKey);
            if(inboxBefore.Count!=1 || inboxBefore[0].EventKind!=NpcExperienceEventKind.ItemReceived ||
                inboxBefore[0].ReceiptId!=inboundReceipt.TransactionId || inboxBefore[0].ReceivedItemUid!=inboundReceipt.RetainedDestinationItemUid ||
                inboxBefore[0].TransferQuantity!=inboundReceipt.Quantity)
                throw new InvalidOperationException("Inbound transfer evidence was not projected exactly once before disk save.");
            ulong inboundRetainedUid=inboundReceipt.RetainedDestinationItemUid;
            int inboundPlayerArrows=ArrowCount();
            var inboundResidentItem=store.ReadNpcInventory(resident).Items.Find(record=>record.Uid==inboundRetainedUid);
            var inboundRetainedNative=owner.Binding.Items.GetItem(inboundRetainedUid);
            if(inboundResidentItem==null || inboundRetainedNative==null)
                throw new InvalidOperationException("Retained incoming UID is missing from the resident inventory before disk save.");
            int inboundRetainedCount=inboundRetainedNative.stackCount;
            int arrows=ArrowCount(),messages=MessageCount(feedback);
            var ground=actor.transform.position;
            string name="People - action disk recovery";
            bool saved=false,loaded=false;
            SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnLoadEventHandler onLoad=ignored=>loaded=true;
            SaveLoadManager.OnSave+=onSave;SaveLoadManager.OnLoad+=onLoad;
            try
            {
                game.SaveLoadManager.Save(character,name);
                deadline=Time.realtimeSinceStartup+30;
                while(!saved && Time.realtimeSinceStartup<deadline)yield return null;
                if(!saved)throw new InvalidOperationException("Action disk save timed out.");
                game.SaveLoadManager.EnumerateSaves();
                int slot=game.SaveLoadManager.FindSaveFolderByNames(character,name);
                if(slot<0)throw new InvalidOperationException("Action disk slot missing.");
                // Change live state, then demand that Load restores the earlier running wait.
                if(!streets.TryStopConversationAction(resident,out reason))throw new InvalidOperationException("Pre-load cancellation failed: "+reason);
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
                deadline=Time.realtimeSinceStartup+90;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline)yield return null;
                if(!loaded || !streets.Ready)throw new InvalidOperationException("Action disk load timed out.");
                game.PlayerEntity.GodMode=true;
                store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
                for(int i=0;i<15;i++)yield return null;
                var restored=store.ReadNpcAction(wait.Id);var restoredTransfer=store.ReadNpcAction(transferId);
                var restoredInbound=store.ReadNpcAction(inboundTransferId);
                var restoredTask=store.ReadNpcTask(task.Id);
                if(restoredTask==null || SaveLoadManager.Serialize(typeof(NpcTaskPlan),restoredTask,false)!=taskPayload ||
                    store.ReadNpcAction(task.Id+":step:1")!=null)
                    throw new InvalidOperationException("Disk load changed the pending task or dispatched its queued step.");
                if(restored==null || restored.Phase!=NpcActionPhase.Executing || Math.Abs(restored.FinishAt-wait.FinishAt)>0.0001 ||
                    restored.ResidentId!=resident || DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()>=restored.FinishAt)
                    throw new InvalidOperationException("Disk load lost running wait identity, phase or remaining time.");
                actor=streets.FindActor(resident);
                if(!actor || Vector3.Distance(actor.transform.position,ground)>0.2f)
                    throw new InvalidOperationException("Disk load did not reconstruct waiting body at its saved position.");
                int bodies=0;foreach(var body in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if(body.gameObject.activeInHierarchy && body.Resident.Id==resident)bodies++;
                if(bodies!=1)throw new InvalidOperationException("Disk load duplicated the waiting NPC.");
                var restoredInventory=store.ReadNpcInventory(resident);
                var restoredOwner=actor.GetComponent<NativeNpcInventoryOwner>();
                if(!restoredOwner || restoredOwner.Binding==null || restoredOwner.Binding.Items.Count!=restoredInventory.Items.Count)
                    throw new InvalidOperationException("Restored live NPC inventory differs from its saved ledger.");
                foreach(var record in restoredInventory.Items)
                {
                    var liveItem=restoredOwner.Binding.Items.GetItem(record.Uid);
                    if(liveItem==null || SaveLoadManager.Serialize(liveItem.GetSaveData().GetType(),liveItem.GetSaveData(),false)!=record.NativePayload)
                        throw new InvalidOperationException("Restored live NPC item changed identity or payload: "+record.Uid);
                }
                store.DrainNpcItemTransferReceipts(32);store.DrainNpcItemTransferReceipts(32);
                var restoredInboxMatches=store.Snapshot().NpcExperienceInbox.Entries.FindAll(entry=>entry.ObserverId==resident &&
                    entry.DeduplicationKey==inboundEvent.DeduplicationKey);
                var restoredInboundReceipt=restoredInbound==null?null:restoredInbound.ItemTransferReceipt;
                var retainedIncomingItem=restoredOwner.Binding.Items.GetItem(inboundRetainedUid);
                string beforeReceiptJson=SaveLoadManager.Serialize(typeof(NpcActionTransferReceipt),inboundReceipt,false);
                string afterReceiptJson=restoredInboundReceipt==null?null:
                    SaveLoadManager.Serialize(typeof(NpcActionTransferReceipt),restoredInboundReceipt,false);
                var serializedExpectedReceipt=(NpcActionTransferReceipt)SaveLoadManager.Deserialize(
                    typeof(NpcActionTransferReceipt),beforeReceiptJson);
                if(serializedExpectedReceipt==null)
                    throw new InvalidOperationException("DFU native receipt serialization round-trip returned null.");
                bool inventorySame=inventory==SaveLoadManager.Serialize(restoredInventory.GetType(),restoredInventory,false);
                bool playerArrowsSame=ArrowCount()==arrows;
                bool feedbackSame=MessageCount(feedback)==messages;
                bool outgoingTransferSame=restoredTransfer!=null && restoredTransfer.Phase==NpcActionPhase.Completed &&
                    restoredTransfer.CompletedEffectId==completed.CompletedEffectId;
                if(!inventorySame || !playerArrowsSame || !feedbackSame || !outgoingTransferSame)
                    throw new InvalidOperationException("Disk load replay/inventory fields: inventorySame="+inventorySame+
                        " playerArrowsSame="+playerArrowsSame+" feedbackSame="+feedbackSame+
                        " outgoingTransferSame="+outgoingTransferSame+".");

                bool inboundPresent=restoredInbound!=null;
                bool inboundCompleted=inboundPresent && restoredInbound.Phase==NpcActionPhase.Completed;
                bool receiptSame=SameReceipt(serializedExpectedReceipt,restoredInboundReceipt);
                bool receiptJsonSame=beforeReceiptJson==afterReceiptJson;
                bool transactionSame=inboundReceipt!=null && restoredInboundReceipt!=null &&
                    serializedExpectedReceipt.TransactionId==restoredInboundReceipt.TransactionId;
                bool sourceUidSame=inboundReceipt!=null && restoredInboundReceipt!=null &&
                    serializedExpectedReceipt.SourceItemUid==restoredInboundReceipt.SourceItemUid;
                bool movedUidSame=inboundReceipt!=null && restoredInboundReceipt!=null &&
                    serializedExpectedReceipt.MovedItemUid==restoredInboundReceipt.MovedItemUid;
                bool retainedUidSame=inboundReceipt!=null && restoredInboundReceipt!=null &&
                    serializedExpectedReceipt.RetainedDestinationItemUid==restoredInboundReceipt.RetainedDestinationItemUid;
                bool quantitySame=inboundReceipt!=null && restoredInboundReceipt!=null &&
                    serializedExpectedReceipt.Quantity==restoredInboundReceipt.Quantity;
                bool recipientSame=inboundReceipt!=null && restoredInboundReceipt!=null &&
                    serializedExpectedReceipt.RecipientId==restoredInboundReceipt.RecipientId;
                bool counterpartySame=inboundReceipt!=null && restoredInboundReceipt!=null &&
                    serializedExpectedReceipt.CounterpartyId==restoredInboundReceipt.CounterpartyId;
                bool observedTimeSame=inboundReceipt!=null && restoredInboundReceipt!=null &&
                    serializedExpectedReceipt.ObservedGameTime==restoredInboundReceipt.ObservedGameTime;
                bool originalObservedTimeSame=inboundReceipt!=null && restoredInboundReceipt!=null &&
                    inboundReceipt.ObservedGameTime==restoredInboundReceipt.ObservedGameTime;
                bool valuationStatusSame=inboundReceipt!=null && restoredInboundReceipt!=null &&
                    serializedExpectedReceipt.NativeValuationStatus==restoredInboundReceipt.NativeValuationStatus;
                bool goldValueSame=inboundReceipt!=null && restoredInboundReceipt!=null &&
                    serializedExpectedReceipt.NativeGoldValue==restoredInboundReceipt.NativeGoldValue;
                bool targetItemSame=restoredInbound!=null && restoredInbound.Target.ItemId==inbound.Target.ItemId;
                bool targetQuantitySame=restoredInbound!=null && restoredInbound.Target.Quantity==inbound.Target.Quantity;
                bool inboxCountSame=restoredInboxMatches.Count==1;
                bool inboxReceiptSame=inboxCountSame && restoredInboxMatches[0].ReceiptId==inboundReceipt.TransactionId;
                bool inboxOutcomeSame=inboxCountSame && restoredInboxMatches[0].OutcomeId==inbound.Id+":item-received";
                bool inboxUidSame=inboxCountSame && restoredInboxMatches[0].ReceivedItemUid==inboundRetainedUid;
                bool inboxQuantitySame=inboxCountSame && restoredInboxMatches[0].TransferQuantity==inboundReceipt.Quantity;
                bool retainedItemPresent=retainedIncomingItem!=null;
                bool retainedCountSame=retainedItemPresent && retainedIncomingItem.stackCount==inboundRetainedCount;
                bool inboundPlayerArrowsSame=ArrowCount()==inboundPlayerArrows;
                string originalTime=inboundReceipt.ObservedGameTime.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                string serializedTime=serializedExpectedReceipt.ObservedGameTime.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                string restoredTime=restoredInboundReceipt==null?"<missing>":
                    restoredInboundReceipt.ObservedGameTime.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                string originalTimeBits=BitConverter.DoubleToInt64Bits(inboundReceipt.ObservedGameTime).ToString("X16");
                string serializedTimeBits=BitConverter.DoubleToInt64Bits(serializedExpectedReceipt.ObservedGameTime).ToString("X16");
                string restoredTimeBits=restoredInboundReceipt==null?"<missing>":
                    BitConverter.DoubleToInt64Bits(restoredInboundReceipt.ObservedGameTime).ToString("X16");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_DISK_INBOUND_FIELDS: present="+inboundPresent+
                    " completed="+inboundCompleted+" receiptSame="+receiptSame+" canonicalReceiptJsonSame="+receiptJsonSame+
                    " tx="+transactionSame+" sourceUid="+sourceUidSame+" movedUid="+movedUidSame+
                    " retainedUid="+retainedUidSame+" qty="+quantitySame+" recipient="+recipientSame+
                    " counterparty="+counterpartySame+" time="+observedTimeSame+" nativeSerializedPrecision="+
                    serializedTime+" originalTimeExact="+originalObservedTimeSame+" original/serialized/restoredTime="+
                    originalTime+"/"+serializedTime+"/"+restoredTime+
                    " original/serialized/restoredBits="+originalTimeBits+"/"+serializedTimeBits+"/"+restoredTimeBits+
                    " valuation="+valuationStatusSame+
                    " gold="+goldValueSame+" targetItem="+targetItemSame+" targetQty="+targetQuantitySame+
                    " inboxCount="+inboxCountSame+" inboxReceipt="+inboxReceiptSame+" inboxOutcome="+inboxOutcomeSame+
                    " inboxUid="+inboxUidSame+" inboxQty="+inboxQuantitySame+" retainedPresent="+retainedItemPresent+
                    " retainedCount="+retainedCountSame+" playerArrows="+inboundPlayerArrowsSame+".");
                if(!inboundPresent)throw new InvalidOperationException("Disk load lost the inbound action.");
                if(!inboundCompleted)throw new InvalidOperationException("Disk load changed inbound action phase.");
                if(!transactionSame)throw new InvalidOperationException("Disk load changed receipt transaction id.");
                if(!sourceUidSame)throw new InvalidOperationException("Disk load changed receipt source UID.");
                if(!movedUidSame)throw new InvalidOperationException("Disk load changed receipt moved UID.");
                if(!retainedUidSame)throw new InvalidOperationException("Disk load changed receipt retained destination UID.");
                if(!quantitySame)throw new InvalidOperationException("Disk load changed receipt quantity.");
                if(!recipientSame)throw new InvalidOperationException("Disk load changed receipt recipient.");
                if(!counterpartySame)throw new InvalidOperationException("Disk load changed receipt counterparty.");
                if(!observedTimeSame)throw new InvalidOperationException("Disk load changed receipt observed game time.");
                if(!valuationStatusSame)throw new InvalidOperationException("Disk load changed receipt valuation status.");
                if(!goldValueSame)throw new InvalidOperationException("Disk load changed receipt native gold value.");
                if(!receiptSame)throw new InvalidOperationException("Disk load changed an inbound receipt field.");
                if(!receiptJsonSame)throw new InvalidOperationException("Disk load changed canonical serialized inbound receipt JSON.");
                if(!targetItemSame)throw new InvalidOperationException("Disk load changed inbound target item id.");
                if(!targetQuantitySame)throw new InvalidOperationException("Disk load changed inbound target quantity.");
                if(!inboxCountSame)throw new InvalidOperationException("Disk load changed once-only ItemReceived inbox count.");
                if(!inboxReceiptSame)throw new InvalidOperationException("Disk load changed ItemReceived receipt binding.");
                if(!inboxOutcomeSame)throw new InvalidOperationException("Disk load changed ItemReceived outcome binding.");
                if(!inboxUidSame)throw new InvalidOperationException("Disk load changed ItemReceived UID binding.");
                if(!inboxQuantitySame)throw new InvalidOperationException("Disk load changed ItemReceived quantity binding.");
                if(!retainedItemPresent)throw new InvalidOperationException("Disk load lost the retained incoming native UID.");
                if(!retainedCountSame)throw new InvalidOperationException("Disk load changed retained incoming stack count.");
                if(!inboundPlayerArrowsSame)throw new InvalidOperationException("Disk load changed actual player arrow count.");
                if(NativeNpcInventoryBridge.TryCompleteActionTransfer(store,restoredInbound,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                    null,game.PlayerEntity.Items,restoredOwner.Binding,null,()=>true,out reason) ||
                    ArrowCount()!=inboundPlayerArrows || restoredOwner.Binding.Items.GetItem(inboundRetainedUid).stackCount!=inboundRetainedCount)
                    throw new InvalidOperationException("Restored completed inbound action replay changed actual inventory.");
                if(!streets.TryStopConversationAction(resident,out reason) || store.ReadNpcAction(wait.Id).Phase!=NpcActionPhase.Cancelled)
                    throw new InvalidOperationException("Reloaded wait cannot be cancelled: "+reason);
                if(store.ReadNpcTask(task.Id).Phase!=NpcTaskPhase.Cancelled || store.ReadNpcAction(task.Id+":step:1")!=null)
                    throw new InvalidOperationException("Reloaded task cancellation left the follow step active.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_DISK_PASS: real DFU disk save/load; live cancellation undone by load; running wait identity/time/position; exact two-step task restored with follow still queued; one body; exact NPC inventory; existing outgoing transfer/feedback unchanged; inbound action receipt/source/moved/retained UID and quantity preserved; actual player arrows unchanged; one ItemReceived inbox outcome; replay rejected; whole-task cancellation after reload.");
            }
            finally {SaveLoadManager.OnSave-=onSave;SaveLoadManager.OnLoad-=onLoad;}
        }
        private static int ArrowCount()
        {
            int count=0;var items=GameManager.Instance.PlayerEntity.Items;
            for(int i=0;i<items.Count;i++){var item=items.GetItem(i);if(item.ItemGroup==ItemGroups.Weapons && item.TemplateIndex==(int)Weapons.Arrow)count+=item.stackCount;}
            return count;
        }
        private static bool SameReceipt(NpcActionTransferReceipt first,NpcActionTransferReceipt second)
        {
            return first!=null && second!=null && first.TransactionId==second.TransactionId &&
                first.SourceItemUid==second.SourceItemUid && first.MovedItemUid==second.MovedItemUid &&
                first.RetainedDestinationItemUid==second.RetainedDestinationItemUid && first.Quantity==second.Quantity &&
                first.RecipientId==second.RecipientId && first.CounterpartyId==second.CounterpartyId &&
                first.ObservedGameTime==second.ObservedGameTime && first.NativeValuationStatus==second.NativeValuationStatus &&
                first.NativeGoldValue==second.NativeGoldValue;
        }
        private static int MessageCount(string expected)
        {
            int count=0;foreach(var tokens in GameManager.Instance.PlayerEntity.Notebook.GetMessages())
                foreach(var line in DaggerfallConnect.Arena2.TextFile.GetTokenLines(tokens))if(line==expected)count++;
            return count;
        }
    }
}
