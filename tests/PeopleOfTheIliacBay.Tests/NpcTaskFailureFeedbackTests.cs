using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcTaskFailureFeedbackTests
    {
        private static FieldInfo Field(Type type,string name)
        { return type.GetField(name,BindingFlags.Public|BindingFlags.Instance); }

        private static object Deliver(WorldStateStore store,string taskId,long revision,out bool delivered)
        {
            var method=typeof(WorldStateStore).GetMethod("TryDeliverNpcTaskFailureReceipt");
            Check.True(method!=null,"Failure receipt delivery must be persisted by the task store.");
            var args=new object[]{taskId,revision,"day 1","settlement 100",null};
            delivered=(bool)method.Invoke(store,args);
            return args[4];
        }

        private static NpcTaskPlan FailedTask(out WorldStateStore store)
        {
            store=new WorldStateStore(42);var resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
            var task=new NpcTaskPlan {Id="task:failure",ProposalId="turn:failure",ResidentId=resident.Id,
                AcceptedAt=100,UpdatedAt=100,DeadlineAt=500};
            task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Wait,DurationSeconds=10,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"}});
            task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.MoveTo,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10}});
            string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);
            Check.True(store.FailNpcTask(task.Id,task.Revision+1,101,"No safe local walking route reaches the target.")==false,
                "Stale task revision must not create feedback.");
            Check.True(store.FailNpcTask(task.Id,task.Revision,101,"No safe local walking route reaches the target."));
            return store.ReadNpcTask(task.Id);
        }

        private static string ReceiptText(object receipt,string field)
        {
            Check.True(receipt!=null,"Failed task must retain a feedback receipt.");
            var value=Field(receipt.GetType(),field);
            Check.True(value!=null,"Receipt must persist " + field + ".");
            return (string)value.GetValue(receipt);
        }

        private static NpcActionJob PurchaseJob(NpcTaskPlan task,int index,double now)
        {
            var step=task.Steps[index];return new NpcActionJob {Id=task.Id+":step:"+index,ProposalId=task.Id+":step:"+index,
                ResidentId=task.ResidentId,Kind=step.Kind,Target=NpcActionExecutor.CopyTarget(step.Target),Phase=NpcActionPhase.Accepted,
                Revision=1,AcceptedAt=now,UpdatedAt=now,DeadlineAt=task.DeadlineAt,
                ServiceCode=step.Kind==NpcActionKind.Purchase?NpcShopCommerce.ServiceCode:null,
                ServicePrice=step.Kind==NpcActionKind.Purchase?step.Target.QuotedGold:0};
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Failed NPC task stores factual feedback with no invented completed steps",()=>{
                WorldStateStore store;var task=FailedTask(out store);
                var receiptField=Field(typeof(NpcTaskPlan),"FailureReceipt");
                Check.True(receiptField!=null,"Task must persist one optional failure receipt.");
                var receipt=receiptField.GetValue(task);
                Check.True(ReceiptText(receipt,"Message").Contains("No steps were completed"));
                Check.True(ReceiptText(receipt,"Message").Contains("Later steps were not attempted"));
                Check.True(ReceiptText(receipt,"Observation").Contains("No steps were completed"));
                Check.Equal(0,task.CurrentStepIndex);
                Check.Equal(null,task.Steps[0].CompletedEffectId);
            }));

            cases.Add(("NPC task failure receipt delivers once and remains consumed after restore",()=>{
                WorldStateStore store;var task=FailedTask(out store);bool delivered;
                object receipt=Deliver(store,task.Id,task.Revision,out delivered);
                Check.True(delivered);Check.True(receipt!=null);
                var memory=store.Snapshot().ConversationMemories.Find(m=>m.PersonId==task.ResidentId);
                Check.True(memory.Entries.Exists(e=>e.Observation==ReceiptText(receipt,"Observation")));
                receipt=Deliver(store,task.Id,store.ReadNpcTask(task.Id).Revision,out delivered);
                Check.True(!delivered);Check.True(receipt==null);
                var restored=new WorldStateStore(99);restored.Restore(store.Snapshot());
                receipt=Deliver(restored,task.Id,restored.ReadNpcTask(task.Id).Revision,out delivered);
                Check.True(!delivered);Check.True(receipt==null);
                memory=restored.Snapshot().ConversationMemories.Find(m=>m.PersonId==task.ResidentId);
                Check.Equal(1,memory.Entries.FindAll(e=>e.Observation.Contains("No steps were completed")).Count);
            }));

            cases.Add(("NPC task restore rejects failure receipt on nonfailed state",()=>{
                WorldStateStore store;var task=FailedTask(out store);var saved=store.Snapshot();
                saved.NpcTasks[0].Phase=NpcTaskPhase.Ready;
                saved.NpcTasks[0].CurrentStepIndex=0;
                saved.NpcTasks[0].FailureReason=null;
                Check.Throws<ArgumentException>(()=>new WorldStateStore(1).Restore(saved));
                saved=store.Snapshot();saved.NpcTasks[0].FailureReceipt=null;
                var migrated=new WorldStateStore(1);migrated.Restore(saved);
                Check.Equal(NpcTaskPhase.Failed,migrated.ReadNpcTask(task.Id).Phase);
                Check.True(migrated.PendingNpcTaskFailureReceipt(task.ResidentId)==null,
                    "Older failed tasks without optional receipt remain valid and do not invent feedback.");
            }));

            cases.Add(("Failed handover reports completed purchase and preserves saved item and gold",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10,20},1);
                string resident=town.Households[0].Residents[0].Id,seller=town.Households[1].Residents[0].Id;
                store.InitializeNpcInventory(resident,30,new NpcInventoryItem[0]);
                store.InitializeNpcInventory(seller,0,new[]{new NpcInventoryItem {Uid=42,NativePayload="native stock"}});
                var task=new NpcTaskPlan {Id="task:purchase-failure",ProposalId="request:purchase-failure",ResidentId=resident,
                    AcceptedAt=100,UpdatedAt=100,DeadlineAt=500};
                task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Purchase,Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,
                    SettlementId=100,BuildingKey=20,PersonId=seller,ContainerId="7",ItemId="42",Quantity=1,QuotedGold=9}});
                task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Transfer,ItemSourceStep=1,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,
                    PersonId="player",ItemId="42",Quantity=1,TransferDirection=NpcActionTransferDirection.NpcToCounterparty}});
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);
                var purchase=PurchaseJob(task,0,100);
                Check.True(store.TryDispatchNpcTaskStep(task.Id,task.Revision,100,purchase,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(purchase.Id,1,101,NpcActionObservation.Arrival()));
                purchase=store.ReadNpcAction(purchase.Id);
                Check.True(store.TryCompleteNpcActionEffect(purchase.Id,purchase.Revision,102,()=>store.CommitNpcInventoryTransaction(
                    purchase.Id+":purchase",new[]{
                        new NpcInventoryUpdate {PersonId=resident,ExpectedRevision=0,WalletGold=21,Items=new List<NpcInventoryItem>{new NpcInventoryItem {Uid=9001,NativePayload="purchased item"}}},
                        new NpcInventoryUpdate {PersonId=seller,ExpectedRevision=0,WalletGold=9,Items=new List<NpcInventoryItem>()}})));
                Check.True(store.ReconcileNpcTask(task.Id,2,102));task=store.ReadNpcTask(task.Id);
                Check.True(store.FailNpcTask(task.Id,task.Revision,103,"No safe local walking route reaches the player."));
                var failed=store.ReadNpcTask(task.Id);var receipt=Field(typeof(NpcTaskPlan),"FailureReceipt").GetValue(failed);
                string message=ReceiptText(receipt,"Message");
                Check.True(message.Contains("Completed 1 of 2"));Check.True(message.Contains("purchase succeeded for 9 gold"));
                Check.True(message.Contains("item remains with the NPC"));Check.True(message.Contains("handover step could not start"));
                Check.Equal(1,failed.CurrentStepIndex);Check.Equal("9001",store.ReadNpcAction(task.Id+":step:0").AcquiredItemId);
                Check.Equal((ulong)9001,store.ReadNpcInventory(resident).Items[0].Uid);
                Check.Equal(21,store.ReadNpcInventory(resident).WalletGold);Check.Equal(9,store.ReadNpcInventory(seller).WalletGold);
                var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
                Check.Equal((ulong)9001,restored.ReadNpcInventory(resident).Items[0].Uid);
                Check.Equal(21,restored.ReadNpcInventory(resident).WalletGold);Check.Equal(9,restored.ReadNpcInventory(seller).WalletGold);
            }));

            cases.Add(("Later failure does not claim earlier handed over item remains with NPC",()=>{
                var store=new WorldStateStore(43);var town=store.GetOrCreate(100,new[]{10,20},1);
                string resident=town.Households[0].Residents[0].Id,seller=town.Households[1].Residents[0].Id;
                store.InitializeNpcInventory(resident,30,new List<NpcInventoryItem>());
                store.InitializeNpcInventory(seller,0,new[]{new NpcInventoryItem {Uid=42,NativePayload="native stock"}});
                var task=new NpcTaskPlan {Id="task:later-failure",ProposalId="request:later-failure",ResidentId=resident,
                    AcceptedAt=100,UpdatedAt=100,DeadlineAt=500};
                task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Purchase,Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,
                    SettlementId=100,BuildingKey=20,PersonId=seller,ContainerId="7",ItemId="42",Quantity=1,QuotedGold=9}});
                task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Transfer,ItemSourceStep=1,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,
                    PersonId="player",ItemId="42",Quantity=1,TransferDirection=NpcActionTransferDirection.NpcToCounterparty}});
                task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,
                    SettlementId=100,BuildingKey=10}});
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);
                var purchase=PurchaseJob(task,0,100);
                Check.True(store.TryDispatchNpcTaskStep(task.Id,task.Revision,100,purchase,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(purchase.Id,1,101,NpcActionObservation.Arrival()));purchase=store.ReadNpcAction(purchase.Id);
                Check.True(store.TryCompleteNpcActionEffect(purchase.Id,purchase.Revision,102,()=>store.CommitNpcInventoryTransaction(
                    purchase.Id+":purchase",new[]{
                        new NpcInventoryUpdate {PersonId=resident,ExpectedRevision=0,WalletGold=21,Items=new List<NpcInventoryItem>{new NpcInventoryItem {Uid=9002,NativePayload="purchased item"}}},
                        new NpcInventoryUpdate {PersonId=seller,ExpectedRevision=0,WalletGold=9,Items=new List<NpcInventoryItem>()}})));
                Check.True(store.ReconcileNpcTask(task.Id,2,102));task=store.ReadNpcTask(task.Id);
                var transfer=PurchaseJob(task,1,103);transfer.Target=store.ResolveNpcTaskTarget(task,1);
                transfer.ExpectedInventoryRevision=store.ReadNpcInventory(resident).Revision;
                Check.True(store.TryDispatchNpcTaskStep(task.Id,task.Revision,103,transfer,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(transfer.Id,1,104,NpcActionObservation.Arrival()));transfer=store.ReadNpcAction(transfer.Id);
                Check.True(store.TryCompleteNpcActionEffect(transfer.Id,transfer.Revision,105,()=>store.CommitNpcInventoryTransaction(
                    transfer.Id+":transfer",new[]{new NpcInventoryUpdate {PersonId=resident,ExpectedRevision=1,WalletGold=21,
                        Items=new List<NpcInventoryItem>()}})));
                Check.True(store.ReconcileNpcTask(task.Id,4,105));task=store.ReadNpcTask(task.Id);
                Check.True(store.FailNpcTask(task.Id,task.Revision,106,"No safe local walking route reaches the destination."));
                var message=ReceiptText(Field(typeof(NpcTaskPlan),"FailureReceipt").GetValue(store.ReadNpcTask(task.Id)),"Message");
                Check.True(message.Contains("handover step completed"));
                Check.True(message.Contains("acquired item could not be verified"));
                Check.True(!message.Contains("item remains with the NPC"));
                Check.Equal(0,store.ReadNpcInventory(resident).Items.Count);Check.Equal(21,store.ReadNpcInventory(resident).WalletGold);
            }));
        }
    }
}
