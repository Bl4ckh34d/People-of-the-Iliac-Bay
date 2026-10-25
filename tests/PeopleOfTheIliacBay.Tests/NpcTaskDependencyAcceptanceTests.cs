using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcTaskDependencyAcceptanceTests
    {
        private static WorldStateStore CreateStore(out string residentId,out string sellerId)
        {
            var store=new WorldStateStore(8142);
            var town=store.GetOrCreate(1,new[]{10,20},1);
            residentId=town.Households[0].Residents[0].Id;
            sellerId=town.Households[1].Residents[0].Id;
            Check.True(store.InitializeNpcInventory(residentId,30,new NpcInventoryItem[0]));
            Check.True(store.InitializeNpcInventory(sellerId,0,new[]{new NpcInventoryItem {Uid=42,NativePayload="native stock payload"}}));
            return store;
        }

        private static NpcTaskPlan PurchaseAndHandover(string residentId,string sellerId,string id)
        {
            return new NpcTaskPlan {Id=id,ProposalId=id+"-proposal",ResidentId=residentId,
                AcceptedAt=100,UpdatedAt=100,DeadlineAt=500,
                Steps=new List<NpcTaskStep> {
                    new NpcTaskStep {Kind=NpcActionKind.Purchase,Target=new NpcActionTarget {
                        Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,PersonId=sellerId,
                        ContainerId="7",ItemId="42",Quantity=2,QuotedGold=9}},
                    new NpcTaskStep {Kind=NpcActionKind.Transfer,ItemSourceStep=1,Target=new NpcActionTarget {
                        Kind=NpcActionTargetKind.Person,PersonId="player",ItemId="42",Quantity=2,
                        TransferDirection=NpcActionTransferDirection.NpcToCounterparty}}
                }};
        }

        private static NpcInventoryState FindInventory(WorldState state,string personId)
        {
            return state.NpcInventories.Find(x=>x.PersonId==personId);
        }

        private static void AssertInventoryEqual(NpcInventoryState expected,NpcInventoryState actual)
        {
            Check.True(expected!=null && actual!=null);
            Check.Equal(expected.PersonId,actual.PersonId);
            Check.Equal(expected.WalletGold,actual.WalletGold);
            Check.Equal(expected.Revision,actual.Revision);
            Check.Equal(expected.LastTransactionId,actual.LastTransactionId);
            Check.Equal(expected.Items.Count,actual.Items.Count);
            for(int i=0;i<expected.Items.Count;i++)
            {
                Check.Equal(expected.Items[i].Uid,actual.Items[i].Uid);
                Check.Equal(expected.Items[i].NativePayload,actual.Items[i].NativePayload);
            }
        }

        private static void AssertRejectedDependency(WorldStateStore store,WorldState before,
            NpcTaskPlan task,string residentId,string sellerId)
        {
            string reason;
            Check.True(!ClassifiedAdmissionFixture.TryAcceptTask(store,task,true,out reason),"Mismatched dependency was accepted.");
            Check.True(!string.IsNullOrWhiteSpace(reason));
            var after=store.Snapshot();
            Check.Equal(before.NpcTasks.Count,after.NpcTasks.Count);
            Check.Equal(before.NpcActions.Count,after.NpcActions.Count);
            Check.True(store.ReadNpcTask(task.Id)==null,"Rejected proposal must not persist a task.");
            Check.True(store.ReadNpcAction(task.Id+":step:0")==null,"Rejected proposal must not create a source action.");
            Check.True(store.ReadNpcAction(task.Id+":step:1")==null,"Rejected proposal must not create a dependent action.");
            Check.True(store.ReadNpcAction(task.Id+":step:2")==null,"Rejected proposal must not create a third action.");
            AssertInventoryEqual(FindInventory(before,residentId),FindInventory(after,residentId));
            AssertInventoryEqual(FindInventory(before,sellerId),FindInventory(after,sellerId));
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Task store rejects acquired-item dependency with forged item ID before writes",()=>{
                string resident,seller;var store=CreateStore(out resident,out seller);
                var task=PurchaseAndHandover(resident,seller,"dependency-item-id:1");
                task.Steps[1].Target.ItemId="43";
                AssertRejectedDependency(store,store.Snapshot(),task,resident,seller);
            }));

            cases.Add(("Task store rejects acquired-item dependency with forged quantity before writes",()=>{
                string resident,seller;var store=CreateStore(out resident,out seller);
                var task=PurchaseAndHandover(resident,seller,"dependency-quantity:1");
                task.Steps[1].Target.Quantity=3;
                AssertRejectedDependency(store,store.Snapshot(),task,resident,seller);
            }));

            cases.Add(("Task store rejects three-step dependency index pointing to a non-acquisition",()=>{
                string resident,seller;var store=CreateStore(out resident,out seller);
                var task=PurchaseAndHandover(resident,seller,"dependency-index:1");
                task.Steps.Insert(1,new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=new NpcActionTarget {
                    Kind=NpcActionTargetKind.Building,SettlementId=1,BuildingKey=20}});
                task.Steps[2].ItemSourceStep=2;
                AssertRejectedDependency(store,store.Snapshot(),task,resident,seller);
            }));

            cases.Add(("Task store accepts exact acquired-item dependency without early action or inventory effects",()=>{
                string resident,seller;var store=CreateStore(out resident,out seller);
                var task=PurchaseAndHandover(resident,seller,"dependency-valid:1");
                var before=store.Snapshot();string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);
                var after=store.Snapshot();
                var stored=store.ReadNpcTask(task.Id);
                Check.True(stored!=null);
                Check.Equal(1,stored.Steps[1].ItemSourceStep);
                Check.Equal("42",stored.Steps[0].Target.ItemId);
                Check.Equal("42",stored.Steps[1].Target.ItemId);
                Check.Equal(2,stored.Steps[0].Target.Quantity);
                Check.Equal(2,stored.Steps[1].Target.Quantity);
                Check.Equal(before.NpcTasks.Count+1,after.NpcTasks.Count);
                Check.Equal(before.NpcActions.Count,after.NpcActions.Count);
                Check.True(store.ReadNpcAction(task.Id+":step:0")==null);
                Check.True(store.ReadNpcAction(task.Id+":step:1")==null);
                AssertInventoryEqual(FindInventory(before,resident),FindInventory(after,resident));
                AssertInventoryEqual(FindInventory(before,seller),FindInventory(after,seller));
            }));
        }
    }
}
