using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcPurchaseTaskTests
    {
        private static NpcTaskPlan Plan(string resident,string seller,string recipient="player")
        {
            return new NpcTaskPlan {Id="purchase-task:1",ProposalId="request:1",ResidentId=resident,
                AcceptedAt=100,UpdatedAt=100,DeadlineAt=500,
                Steps=new List<NpcTaskStep> {
                    new NpcTaskStep {Kind=NpcActionKind.Purchase,Target=new NpcActionTarget {
                        Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,PersonId=seller,
                        ContainerId="7",ItemId="42",Quantity=2,QuotedGold=9}},
                    new NpcTaskStep {Kind=NpcActionKind.Transfer,ItemSourceStep=1,Target=new NpcActionTarget {
                        Kind=NpcActionTargetKind.Person,PersonId=recipient,ItemId="42",Quantity=2,
                        TransferDirection=NpcActionTransferDirection.NpcToCounterparty}}
                }};
        }

        private static NpcActionJob Job(NpcTaskPlan task,int index,double now)
        {
            var step=task.Steps[index];
            return new NpcActionJob {Id=task.Id+":step:"+index,ProposalId=task.Id+":step:"+index,
                ResidentId=task.ResidentId,Kind=step.Kind,Target=NpcActionExecutor.CopyTarget(step.Target),
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=now,UpdatedAt=now,
                DeadlineAt=task.DeadlineAt,ServiceCode=step.Kind==NpcActionKind.Purchase?NpcShopCommerce.ServiceCode:null,
                ServicePrice=step.Kind==NpcActionKind.Purchase?step.Target.QuotedGold:0};
        }

        private static NpcActionTarget Resolve(WorldStateStore store,NpcTaskPlan task,int index)
        {
            return store.ResolveNpcTaskTarget(task,index);
        }

        private static void CompletePurchase(WorldStateStore store,NpcTaskPlan task,string seller,ulong acquiredUid)
        { CompletePurchase(store,task,seller,new List<NpcInventoryItem>{new NpcInventoryItem{Uid=acquiredUid,NativePayload="native acquired"}}); }

        private static void CompletePurchase(WorldStateStore store,NpcTaskPlan task,string seller,List<NpcInventoryItem> acquiredItems)
        {
            string reason;var purchase=Job(task,0,100);
            Check.True(store.TryDispatchNpcTaskStep(task.Id,task.Revision,100,purchase,true,true,true,out reason),reason);
            Check.True(store.ObserveNpcAction(purchase.Id,1,101,NpcActionObservation.Arrival()));
            purchase=store.ReadNpcAction(purchase.Id);
            Check.True(store.TryCompleteNpcActionEffect(purchase.Id,purchase.Revision,102,()=>
                store.CommitNpcInventoryTransaction(purchase.Id+":purchase",new[]{
                    new NpcInventoryUpdate {PersonId=task.ResidentId,ExpectedRevision=0,WalletGold=21,
                        Items=acquiredItems},
                    new NpcInventoryUpdate {PersonId=seller,ExpectedRevision=0,WalletGold=9,Items=new List<NpcInventoryItem>()}})));
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Purchase task resolves split acquired UID only after confirmed purchase",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{10,20},1);
                string resident=town.Households[0].Residents[0].Id,seller=town.Households[1].Residents[0].Id;var task=Plan(resident,seller);
                store.InitializeNpcInventory(resident,30,new NpcInventoryItem[0]);
                store.InitializeNpcInventory(seller,0,new[]{new NpcInventoryItem{Uid=42,NativePayload="stock item"}});
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);
                Check.True(Resolve(store,task,1)==null,"Uncompleted purchase cannot resolve transfer item.");
                CompletePurchase(store,task,seller,9001);
                var purchase=store.ReadNpcAction(task.Id+":step:0");
                Check.Equal("9001",purchase.AcquiredItemId);
                Check.Equal("42",task.Steps[1].Target.ItemId);
                var resolved=Resolve(store,store.ReadNpcTask(task.Id),1);
                Check.Equal("9001",resolved.ItemId);
                Check.Equal(2,resolved.Quantity);
                Check.Equal("player",resolved.PersonId);
            }));

            cases.Add(("Purchase task preserves named recipient while resolving acquired UID",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{10,20,30},1);
                string resident=town.Households[0].Residents[0].Id,seller=town.Households[1].Residents[0].Id,
                    recipient=town.Households[2].Residents[0].Id;
                var task=Plan(resident,seller,recipient);
                store.InitializeNpcInventory(resident,30,new NpcInventoryItem[0]);
                store.InitializeNpcInventory(seller,0,new[]{new NpcInventoryItem{Uid=42,NativePayload="stock item"}});
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store,task,true,out reason),reason);
                CompletePurchase(store,task,seller,9011);
                var resolved=Resolve(store,store.ReadNpcTask(task.Id),1);
                Check.Equal(recipient,resolved.PersonId);
                Check.Equal("9011",resolved.ItemId);
                Check.Equal(NpcActionTransferDirection.NpcToCounterparty,resolved.TransferDirection);
            }));

            cases.Add(("Purchase task restore reconciles completed purchase before resolved transfer dispatch",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{10,20},1);
                string resident=town.Households[0].Residents[0].Id,seller=town.Households[1].Residents[0].Id;var task=Plan(resident,seller);
                store.InitializeNpcInventory(resident,30,new NpcInventoryItem[0]);
                store.InitializeNpcInventory(seller,0,new[]{new NpcInventoryItem{Uid=42,NativePayload="stock item"}});
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);CompletePurchase(store,task,seller,9002);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.True(restored.ReconcileNpcTask(task.Id,2,102));
                var current=restored.ReadNpcTask(task.Id);var transfer=Job(current,1,103);
                transfer.Target=Resolve(restored,current,1);
                Check.True(restored.TryDispatchNpcTaskStep(task.Id,current.Revision,103,transfer,true,true,true,out reason),reason);
                var dispatched=restored.ReadNpcAction(transfer.Id);
                Check.Equal("9002",dispatched.Target.ItemId);
                var branch=new WorldStateStore(1);branch.Restore(restored.Snapshot());
                Check.Equal("9002",Resolve(branch,branch.ReadNpcTask(task.Id),1).ItemId);
            }));

            cases.Add(("Purchase task rejects wrong acquired UID and accepts resolved UID",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{10,20},1);
                string resident=town.Households[0].Residents[0].Id,seller=town.Households[1].Residents[0].Id;var task=Plan(resident,seller);
                store.InitializeNpcInventory(resident,30,new NpcInventoryItem[0]);
                store.InitializeNpcInventory(seller,0,new[]{new NpcInventoryItem{Uid=42,NativePayload="stock item"}});
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);CompletePurchase(store,task,seller,9003);
                Check.True(store.ReconcileNpcTask(task.Id,2,102));task=store.ReadNpcTask(task.Id);
                var wrong=Job(task,1,103);wrong.Target.ItemId="42";
                Check.True(!store.TryDispatchNpcTaskStep(task.Id,task.Revision,103,wrong,true,true,true,out reason));
                var correct=Job(task,1,103);correct.Target=Resolve(store,task,1);
                Check.True(store.TryDispatchNpcTaskStep(task.Id,task.Revision,103,correct,true,true,true,out reason),reason);
            }));

            cases.Add(("Purchase task cancellation after confirmed purchase preserves inventory and blocks transfer",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{10,20},1);
                string resident=town.Households[0].Residents[0].Id,seller=town.Households[1].Residents[0].Id;var task=Plan(resident,seller);
                store.InitializeNpcInventory(resident,30,new NpcInventoryItem[0]);
                store.InitializeNpcInventory(seller,0,new[]{new NpcInventoryItem{Uid=42,NativePayload="stock item"}});
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);CompletePurchase(store,task,seller,9004);
                Check.True(store.ReconcileNpcTask(task.Id,2,102));task=store.ReadNpcTask(task.Id);
                Check.True(store.CancelNpcTask(task.Id,task.Revision,103,"Cancelled after purchase."));
                Check.Equal((ulong)9004,store.ReadNpcInventory(resident).Items[0].Uid);
                Check.Equal(NpcTaskPhase.Cancelled,store.ReadNpcTask(task.Id).Phase);
                var transfer=Job(task,1,104);
                Check.True(!store.TryDispatchNpcTaskStep(task.Id,task.Revision,104,transfer,true,true,true,out reason));
                Check.True(store.ReadNpcAction(transfer.Id)==null);
            }));

            cases.Add(("Purchase task rejects duplicate and forward item dependencies",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{10,20},1);
                string resident=town.Households[0].Residents[0].Id,seller=town.Households[1].Residents[0].Id;
                var task=Plan(resident,seller);NpcTaskContract.Validate(task);
                task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Transfer,ItemSourceStep=1,Target=NpcActionExecutor.CopyTarget(task.Steps[1].Target)});
                Check.Throws<ArgumentException>(()=>NpcTaskContract.Validate(task));
                task=Plan(resident,seller);task.Steps[0].ItemSourceStep=1;
                Check.Throws<ArgumentException>(()=>NpcTaskContract.Validate(task));
            }));

            cases.Add(("Purchase task refuses missing or ambiguous acquired item results",()=>{
                foreach(var acquired in new[]{
                    new List<NpcInventoryItem>(),
                    new List<NpcInventoryItem>{new NpcInventoryItem{Uid=9101,NativePayload="first"},new NpcInventoryItem{Uid=9102,NativePayload="second"}}})
                {
                    var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{10,20},1);
                    string resident=town.Households[0].Residents[0].Id,seller=town.Households[1].Residents[0].Id;var task=Plan(resident,seller);
                    store.InitializeNpcInventory(resident,30,new NpcInventoryItem[0]);
                    store.InitializeNpcInventory(seller,0,new[]{new NpcInventoryItem{Uid=42,NativePayload="stock item"}});
                    string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);
                    CompletePurchase(store,task,seller,acquired);
                    Check.True(store.ReadNpcAction(task.Id+":step:0").AcquiredItemId==null);
                    Check.True(store.ReconcileNpcTask(task.Id,2,102));task=store.ReadNpcTask(task.Id);
                    Check.True(Resolve(store,task,1)==null);
                    var transfer=Job(task,1,103);
                    Check.True(!store.TryDispatchNpcTaskStep(task.Id,task.Revision,103,transfer,true,true,true,out reason));
                    Check.True(store.ReadNpcAction(transfer.Id)==null);
                }
            }));

            cases.Add(("Purchase task restore rejects transfer action with forged acquired UID",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{10,20},1);
                string resident=town.Households[0].Residents[0].Id,seller=town.Households[1].Residents[0].Id;var task=Plan(resident,seller);
                store.InitializeNpcInventory(resident,30,new NpcInventoryItem[0]);
                store.InitializeNpcInventory(seller,0,new[]{new NpcInventoryItem{Uid=42,NativePayload="stock item"}});
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);CompletePurchase(store,task,seller,9201);
                Check.True(store.ReconcileNpcTask(task.Id,2,102));task=store.ReadNpcTask(task.Id);
                var transfer=Job(task,1,103);transfer.Target=Resolve(store,task,1);
                Check.True(store.TryDispatchNpcTaskStep(task.Id,task.Revision,103,transfer,true,true,true,out reason),reason);
                var saved=store.Snapshot();saved.NpcActions[1].Target.ItemId="9202";
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(saved));
            }));
        }
    }
}
