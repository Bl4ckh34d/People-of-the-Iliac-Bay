using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NativeNpcShopPurchaseProbe
    {
        private static readonly List<string> taskFailureHudMessages=new List<string>();

        internal static bool ShouldInjectTaskFailure(NpcTaskPlan task,NpcActionJob prior,out string reason)
        {
            reason=null;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-task-failure-probe")<0 || task==null ||
                task.ProposalId==null || !task.ProposalId.StartsWith("native-task-failure-probe:",StringComparison.Ordinal) ||
                task.CurrentStepIndex!=1 || task.Steps.Count!=3 || prior==null || prior.Kind!=NpcActionKind.Purchase ||
                prior.Phase!=NpcActionPhase.Completed || string.IsNullOrWhiteSpace(prior.AcquiredItemId) ||
                task.Steps[0].CompletedEffectId==null)
                return false;
            reason="The agreed handover could not start after the confirmed purchase ("+task.Id+").";
            return true;
        }

        internal static void ObserveTaskFailureHud(string text)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-task-failure-probe")>=0)
                taskFailureHudMessages.Add(text);
        }

        private static void ClearTaskFailureHudMessages()
        { taskFailureHudMessages.Clear(); }

        private static IEnumerator VerifyTaskFailureAfterPurchase(GameManager game,PeopleOfTheIliacBayMod host,WorldStateStore store,
            ResidentActor actor,NpcActionJob purchase,DaggerfallUnityItem bought,NativeNpcInventoryOwner owner,int stockBefore,
            int buyerGoldBefore,int sellerGoldBefore,int playerGoldBefore,string playerItemsBefore)
        {
            var task=store.ActiveNpcTask(actor.Resident.Id);
            Require(task!=null && task.Steps.Count==3 && task.Steps[0].Kind==NpcActionKind.Purchase &&
                task.Steps[1].Kind==NpcActionKind.Transfer && task.Steps[1].ItemSourceStep==1 &&
                task.Steps[2].Kind==NpcActionKind.Follow,"Task failure fixture did not save purchase, dependent handover and queued follow steps.");
            var stock=FindStock(game,purchase.Target.ContainerId);
            Require(stock,"Purchased stock shelf disappeared before failure verification.");
            float deadline=Time.realtimeSinceStartup+30f;
            while(Time.realtimeSinceStartup<deadline)
            {
                task=store.ReadNpcTask(task.Id);
                if(task!=null && task.Phase==NpcTaskPhase.Failed && task.FailureReceipt!=null && task.FailureReceipt.Delivered)break;
                yield return null;
            }
            task=store.ReadNpcTask(task.Id);
            Require(task!=null && task.Phase==NpcTaskPhase.Failed && task.CurrentStepIndex==1 &&
                task.Steps[0].CompletedEffectId!=null && task.FailureReceipt!=null && task.FailureReceipt.Delivered,
                "Confirmed purchase task did not fail and deliver its saved receipt.");
            var receipt=task.FailureReceipt;
            Require(receipt.Message.Contains("Completed 1 of 3 agreed task steps") &&
                receipt.Message.Contains("The shop purchase succeeded for "+purchase.ServicePrice+" gold") &&
                receipt.Message.Contains("the purchased item remains with the NPC") &&
                receipt.Message.Contains("handover step could not start") && receipt.Message.Contains("Later steps were not attempted"),
                "Failure receipt did not describe actual purchase, failed handover and unattempted follow step.");
            Require(owner.Binding.Items.GetItem(bought.UID)!=null && bought.UID.ToString()==purchase.AcquiredItemId &&
                owner.Binding.Items.GetItem(bought.UID).stackCount==bought.stackCount,
                "Purchased native item did not remain in the NPC inventory.");
            Require(store.ReadNpcInventory(actor.Resident.Id).WalletGold==buyerGoldBefore-purchase.ServicePrice &&
                store.ReadNpcInventory(purchase.Target.PersonId).WalletGold==sellerGoldBefore+purchase.ServicePrice &&
                (stock.Items.GetItem(ulong.Parse(purchase.Target.ItemId))==null?0:stock.Items.GetItem(ulong.Parse(purchase.Target.ItemId)).stackCount)==stockBefore-1,
                "Purchase wallet or stock changes were not retained exactly once.");
            Require(game.PlayerEntity.GetGoldAmount()==playerGoldBefore && EncodeProbeItems(game.PlayerEntity.Items)==playerItemsBefore &&
                store.ReadNpcAction(task.Id+":step:1")==null && store.ReadNpcAction(task.Id+":step:2")==null,
                "Failed or queued handover/follow changed player ownership or dispatched a later action.");
            Require(taskFailureHudMessages.Count==1 && taskFailureHudMessages[0].Contains(receipt.Message),
                "English HUD did not receive the saved failure receipt exactly once.");
            var liveWorld=(WorldState)host.GetSaveData();
            var archive=new SharedConversationArchive(PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            var liveMemory=archive.Load(liveWorld.ConversationArchive).Find(m=>m.PersonId==actor.Resident.Id);
            int liveObservationCount=liveMemory==null?0:liveMemory.Entries.FindAll(e=>e.Source=="observed" && e.Observation==receipt.Observation).Count;
            string livePurchaseRecall=store.RecallConversation(actor.Resident.Id,"purchased item remains with the NPC",FixtureNow(),5000);
            string liveFailureRecall=store.RecallConversation(actor.Resident.Id,"handover step could not start",FixtureNow(),5000);
            string liveLaterRecall=store.RecallConversation(actor.Resident.Id,"later steps were not attempted",FixtureNow(),5000);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TASK_FAILURE_MEMORY_DIAGNOSTIC: live-count="+liveObservationCount+
                "; purchase-recall="+livePurchaseRecall+"; handover-recall="+liveFailureRecall+"; later-recall="+liveLaterRecall);
            Require(liveObservationCount==1 && livePurchaseRecall.Contains("purchased item remains with the NPC") &&
                liveFailureRecall.Contains("handover step could not start") && liveLaterRecall.Contains("Later steps were not attempted"),
                "Actual shared journal or bounded native recall omitted confirmed purchase, failed handover, or skipped later-step fact.");
            string encoded=SaveLoadManager.Serialize(typeof(WorldState),liveWorld);
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),encoded));
            var restoredTask=restored.ReadNpcTask(task.Id);
            var restoredReceipt=restoredTask==null?null:restoredTask.FailureReceipt;
            Require(restoredTask!=null && restoredTask.Phase==NpcTaskPhase.Failed && restoredReceipt!=null && restoredReceipt.Delivered &&
                restoredReceipt.Message==receipt.Message && restored.PendingNpcTaskFailureReceipt(actor.Resident.Id)==null,
                "Native serialized restore lost delivered receipt state or made it pending again.");
            var restoredWorld=restored.Snapshot();
            var restoredMemory=archive.Load(restoredWorld.ConversationArchive).Find(m=>m.PersonId==actor.Resident.Id);
            int restoredObservationCount=restoredMemory==null?0:restoredMemory.Entries.FindAll(e=>e.Source=="observed" && e.Observation==receipt.Observation).Count;
            string restoredPurchaseRecall=restored.RecallConversation(actor.Resident.Id,"purchased item remains with the NPC",FixtureNow(),5000);
            string restoredFailureRecall=restored.RecallConversation(actor.Resident.Id,"handover step could not start",FixtureNow(),5000);
            string restoredLaterRecall=restored.RecallConversation(actor.Resident.Id,"later steps were not attempted",FixtureNow(),5000);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TASK_FAILURE_MEMORY_DIAGNOSTIC: restored-count="+restoredObservationCount+
                "; purchase-recall="+restoredPurchaseRecall+"; handover-recall="+restoredFailureRecall+"; later-recall="+restoredLaterRecall);
            Require(restoredObservationCount==1 && restoredPurchaseRecall.Contains("purchased item remains with the NPC") &&
                restoredFailureRecall.Contains("handover step could not start") && restoredLaterRecall.Contains("Later steps were not attempted") &&
                restored.ReadNpcInventory(actor.Resident.Id).Items.Exists(item=>item.Uid==bought.UID) &&
                restored.ReadNpcInventory(actor.Resident.Id).WalletGold==buyerGoldBefore-purchase.ServicePrice,
                "Native serialized restore lost factual recall, duplicated journal entry, or changed purchase ownership.");
            for(int i=0;i<10;i++)yield return null;
            Require(taskFailureHudMessages.Count==1,"Delivered receipt emitted duplicate HUD feedback after serialized restore.");
            RoutineRecoveryNativeProbe.Verify(liveWorld,actor.Resident.Id,game.StreamingWorld.GetCurrentBuildingDirectory().MapID,
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TASK_FAILURE_NATIVE_PASS: real-stock-purchase retained-item retained-gold handover-start-failure queued-follow-skipped english-hud once memory once serialized-restore no-redelivery");
        }

        private static DaggerfallLoot FindStock(GameManager game,string containerId)
        {
            foreach(var loot in game.PlayerEnterExit.Interior.GetComponentsInChildren<DaggerfallLoot>())
                if(loot.LoadID.ToString()==containerId)return loot;
            return null;
        }
    }
}
