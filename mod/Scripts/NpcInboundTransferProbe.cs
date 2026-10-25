using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class NpcInboundTransferProbe
    {
        private static void Need(bool value,string reason){if(!value)throw new InvalidOperationException("Inbound transfer probe: "+reason);}
        private static int Count(ItemCollection items){int count=0;for(int i=0;i<items.Count;i++){var item=items.GetItem(i);if(item.ItemGroup==ItemGroups.Weapons && item.TemplateIndex==(int)Weapons.Arrow)count+=item.stackCount;}return count;}
        private static int Messages(string expected)
        {int count=0;foreach(var tokens in GameManager.Instance.PlayerEntity.Notebook.GetMessages())foreach(var line in DaggerfallConnect.Arena2.TextFile.GetTokenLines(tokens))if(line==expected)count++;return count;}
        public static IEnumerator Run(StreetPopulation streets,WorldStateStore store,ResidentActor actor,
            Action<NpcActionJob> completedTransfer = null)
        {
            var game=GameManager.Instance;
            Need(game.PlayerEntity.Name=="PeopleOfTheIliacBayTest-9a5f197b" && Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-probe")>=0,
                "explicit disposable action fixture required");
            var room=game.PlayerEnterExit.Interior;
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streets);
            Need(room && geometry!=null && actor && actor.gameObject.activeInHierarchy,"existing interior actor required");
            Need(NativeNpcShopPurchaseProbe.PositionObserver(game,geometry,room,actor,3f,5.5f),"safe nearby observer point unavailable");
            DaggerfallUnityItem source=null;
            for(int i=0;i<game.PlayerEntity.Items.Count;i++){var item=game.PlayerEntity.Items.GetItem(i);if(item.ItemGroup==ItemGroups.Weapons && item.TemplateIndex==(int)Weapons.Arrow && !item.IsQuestItem && !item.IsEquipped){source=item;break;}}
            Need(source!=null,"player has no existing unequipped arrow; probe never creates one");
            ulong sourceUid=source.UID;int sourceQuantity=source.stackCount;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();Need(owner && owner.Binding!=null,"existing native receiver inventory missing");
            var destinationArrowStacks=new Dictionary<ulong,int>();
            for(int i=0;i<owner.Binding.Items.Count;i++)
            {
                var item=owner.Binding.Items.GetItem(i);
                if(item.ItemGroup==ItemGroups.Weapons && item.TemplateIndex==(int)Weapons.Arrow)
                    destinationArrowStacks.Add(item.UID,item.stackCount);
            }
            Need(destinationArrowStacks.Count>0,"prior native handover did not leave an existing resident arrow stack for the inbound merge check");
            NpcActionOption wait=null,transfer=null;
            foreach(var option in streets.ConversationActionOptions(actor.Resident.Id,"Wait here for 10 seconds."))if(option.Kind==NpcActionKind.Wait)wait=option;
            foreach(var option in streets.ConversationActionOptions(actor.Resident.Id,"Take one "+source.LongName+" from me."))
                if(option.Kind==NpcActionKind.Transfer && option.Target.ItemId==sourceUid.ToString() && option.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc)transfer=option;
            Need(wait!=null && transfer!=null,"exact existing item is not currently offered");
            int playerBefore=Count(game.PlayerEntity.Items),npcBefore=Count(owner.Binding.Items),playerGold=game.PlayerEntity.GetGoldAmount(),npcGold=store.ReadNpcInventory(actor.Resident.Id).WalletGold;
            string reason;
            Need(NpcProbeCommandSource.TryAcceptTask(streets,actor.Resident.Id,"inbound-probe:"+Guid.NewGuid().ToString("N"),new List<NpcActionDecision>{
                new NpcActionDecision{Option=wait,DurationSeconds=10},new NpcActionDecision{Option=transfer,DurationSeconds=120}},out reason),reason);
            var task=store.ActiveNpcTask(actor.Resident.Id);string id=task.Id;
            var preview=new NpcActionJob{Kind=NpcActionKind.Transfer,Phase=NpcActionPhase.Completed,CompletedEffectId="preview",Target=transfer.Target};
            string expected=NpcActionFeedback.Describe(preview,actor.Person.NameNPC,source.LongName,null);Need(!string.IsNullOrEmpty(expected),"incoming feedback unavailable");
            int beforeMessages=Messages(expected);float until=Time.realtimeSinceStartup+90,walked=0,maxStep=0;Vector3 previous=actor.transform.position;
            bool sawWait=false,sawTransfer=false;
            do
            {
                yield return null;
                var first=store.ReadNpcAction(id+":step:0");var second=store.ReadNpcAction(id+":step:1");
                sawWait|=first!=null && first.Phase==NpcActionPhase.Executing;
                if(second!=null)
                {
                    Need(first.Phase==NpcActionPhase.Completed,"transfer dispatched before confirmed wait");
                    sawTransfer=true;float step=Vector3.Distance(previous,actor.transform.position);walked+=step;maxStep=Mathf.Max(maxStep,step);
                }
                previous=actor.transform.position;task=store.ReadNpcTask(id);
            }while(!NpcTaskContract.IsTerminal(task) && Time.realtimeSinceStartup<until);
            Need(task.Phase==NpcTaskPhase.Completed && sawWait && sawTransfer,"sequence did not complete: "+task.Phase+" "+task.FailureReason);
            var handed=store.ReadNpcAction(id+":step:1");var remaining=game.PlayerEntity.Items.GetItem(sourceUid);
            Need(handed!=null && handed.ItemTransferReceipt!=null,"completed player-to-NPC action has no durable transfer receipt");
            var receipt=handed.ItemTransferReceipt;
            int destinationQuantity=0;
            Need(handed.Target.ItemId==sourceUid.ToString() && handed.Target.Quantity==1 && handed.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc,
                "bound source target changed");
            Need(receipt.TransactionId==handed.Id+":transfer" && receipt.SourceItemUid==sourceUid &&
                receipt.MovedItemUid!=0 &&
                (sourceQuantity==1 ? receipt.MovedItemUid==sourceUid : receipt.MovedItemUid!=sourceUid) &&
                destinationArrowStacks.TryGetValue(receipt.RetainedDestinationItemUid,out destinationQuantity) &&
                receipt.MovedItemUid!=receipt.RetainedDestinationItemUid && receipt.Quantity==1 &&
                receipt.RecipientId==actor.Resident.Id && receipt.CounterpartyId=="player" &&
                receipt.ObservedGameTime>=handed.AcceptedAt && receipt.ObservedGameTime<handed.DeadlineAt &&
                receipt.NativeValuationStatus==NpcExperienceNativeValuationStatus.Known && receipt.NativeGoldValue==source.value,
                "receipt did not bind this action, split source, retained resident stack, recipient, quantity, and actual native value");
            Need((remaining==null?0:remaining.stackCount)==sourceQuantity-1 && Count(game.PlayerEntity.Items)==playerBefore-1 && Count(owner.Binding.Items)==npcBefore+1,
                "exact source and total ownership counts differ");
            var retained=owner.Binding.Items.GetItem(receipt.RetainedDestinationItemUid);
            Need(retained!=null && retained.stackCount==destinationQuantity+1,
                "incoming partial transfer did not merge into the original resident arrow UID");
            Need(walked>0.5f && maxStep<1.5f && Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)<=1.5f,"handover did not physically approach");
            Need(game.PlayerEntity.GetGoldAmount()==playerGold && store.ReadNpcInventory(actor.Resident.Id).WalletGold==npcGold,"handover changed wallets");
            Need(Messages(expected)==beforeMessages+1,"missing or duplicate incoming feedback");
            Need(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,handed,handed.UpdatedAt,null,game.PlayerEntity.Items,owner.Binding,null,()=>true,out reason),"completed incoming transfer replayed");
            var expectedEvent=receipt.ToNativeEvent(handed.Id);
            store.DrainNpcItemTransferReceipts(32);store.DrainNpcItemTransferReceipts(32);
            var matchingInbox=store.Snapshot().NpcExperienceInbox.Entries.Where(entry=>entry.ObserverId==actor.Resident.Id &&
                entry.DeduplicationKey==expectedEvent.DeduplicationKey).ToList();
            Need(matchingInbox.Count==1 && matchingInbox[0].Origin==NpcExperienceEventOrigin.NativeOutcome &&
                matchingInbox[0].ReceiptId==receipt.TransactionId && matchingInbox[0].EpisodeId==handed.Id+":item-transfer" &&
                matchingInbox[0].OutcomeId==handed.Id+":item-received" && matchingInbox[0].EventKind==NpcExperienceEventKind.ItemReceived &&
                matchingInbox[0].ResponsibleActorAttribution==NpcResponsibleActorAttribution.Known &&
                matchingInbox[0].ResponsibleActorId=="player" && matchingInbox[0].ReceivedItemUid==receipt.RetainedDestinationItemUid &&
                matchingInbox[0].TransferQuantity==1,
                "durable transfer receipt did not project exactly once as retained-UID ItemReceived evidence");
            for(int i=0;i<8;i++)yield return null;
            Need(Count(game.PlayerEntity.Items)==playerBefore-1 && Count(owner.Binding.Items)==npcBefore+1 && Messages(expected)==beforeMessages+1,"replay or recovery changed transfer counts/feedback");
            completedTransfer?.Invoke(handed);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INBOUND_TRANSFER_PASS: actual player collection and saved resident inventory; sourceUID="+sourceUid.ToString(CultureInfo.InvariantCulture)+
                "; movedUID="+receipt.MovedItemUid+"; retainedUID="+receipt.RetainedDestinationItemUid+
                "; existing target stack="+destinationQuantity+"->"+retained.stackCount+"; exact quantity=1; action receipt; ItemReceived once; physical approach; replay rejected; one feedback.");
        }
    }
}
