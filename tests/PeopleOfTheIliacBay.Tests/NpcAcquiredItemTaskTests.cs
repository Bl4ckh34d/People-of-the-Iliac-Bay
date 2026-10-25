using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcAcquiredItemTaskTests
    {
        private static NpcActionOption Retrieval()
        {
            return new NpcActionOption {Id=2,Kind=NpcActionKind.Transfer,Target=new NpcActionTarget {
                Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,ContainerId="home:10:1",
                ItemId="7001",Quantity=2,TransferDirection=NpcActionTransferDirection.HomeContainerToNpc},
                Label="take two arrows from your chest"};
        }
        private static NpcTaskPlan Plan(string resident)
        {
            return new NpcTaskPlan {Id="retrieval-task:1",ProposalId="request:1",ResidentId=resident,
                AcceptedAt=100,UpdatedAt=100,DeadlineAt=500,Steps=new List<NpcTaskStep> {
                    new NpcTaskStep {Kind=NpcActionKind.Transfer,Target=NpcActionExecutor.CopyTarget(Retrieval().Target)},
                    new NpcTaskStep {Kind=NpcActionKind.Transfer,ItemSourceStep=1,Target=new NpcActionTarget {
                        Kind=NpcActionTargetKind.Person,PersonId="player",ItemId="7001",Quantity=2,
                        TransferDirection=NpcActionTransferDirection.NpcToCounterparty}}
                }};
        }
        private static NpcActionJob Job(NpcTaskPlan task,int index,double now)
        {
            var step=task.Steps[index];return new NpcActionJob {Id=task.Id+":step:"+index,ProposalId=task.Id+":step:"+index,
                ResidentId=task.ResidentId,Kind=step.Kind,Target=NpcActionExecutor.CopyTarget(step.Target),
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=now,UpdatedAt=now,DeadlineAt=task.DeadlineAt};
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Home retrieval accepts ordinary chest wording and binds exact acquired UID",new Action(()=>{
                var handover=NpcActionSequenceChat.AcquiredItemHandoverSemantic(0,new NpcActionDecision {Option=Retrieval(),DurationSeconds=300});
                Check.True(handover!=null);
                Check.Equal("7001",handover.Target.ItemId);Check.Equal(2,handover.Target.Quantity);Check.Equal(1,handover.ItemSourceStep);
                var clauses=new List<string>{"take two iron arrows from your chest","give them to me"};
                Check.True(clauses!=null && clauses.Count==2);
                var parsed=NpcActionSequenceChat.ParseSemantic(clauses,new List<IList<NpcActionOption>> {
                    new[]{Retrieval()},new[]{handover}},new[]{"DIRECT|Transfer|2|2|300","DIRECT|Transfer|1|2|300"});
                Check.True(parsed!=null && parsed[1].Option.ItemSourceStep==1);
                Check.Equal("7001",parsed[1].Option.Target.ItemId);
                Check.True(NpcActionSequenceChat.AcquiredItemHandoverSemantic(0,new NpcActionDecision {Option=new NpcActionOption {
                    Id=1,Kind=NpcActionKind.Wait,Target=new NpcActionTarget {Quantity=1}}})==null);
            })));
            cases.Add(("Home retrieval result resolves only after exact transfer receipt and survives restore",new Action(()=>{
                var store=new WorldStateStore(71);var town=store.GetOrCreate(1,new[]{10,20},1);string resident=town.Households[0].Residents[0].Id;
                store.InitializeNpcInventory(resident,0,new NpcInventoryItem[0]);var task=Plan(resident);string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);Check.True(store.ResolveNpcTaskTarget(task,1)==null);
                var retrieval=Job(task,0,100);Check.True(store.TryDispatchNpcTaskStep(task.Id,task.Revision,100,retrieval,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(retrieval.Id,1,101,NpcActionObservation.Arrival()));retrieval=store.ReadNpcAction(retrieval.Id);
                Check.True(store.TryCompleteNpcActionEffect(retrieval.Id,retrieval.Revision,102,()=>store.CommitNpcInventoryTransaction(
                    retrieval.Id+":transfer",new[]{new NpcInventoryUpdate {PersonId=resident,ExpectedRevision=0,WalletGold=0,
                        Items=new List<NpcInventoryItem>{new NpcInventoryItem {Uid=7001,NativePayload="native exact arrows"}}}})));
                Check.Equal("7001",store.ReadNpcAction(retrieval.Id).AcquiredItemId);
                Check.True(store.ReconcileNpcTask(task.Id,2,102));var ready=store.ReadNpcTask(task.Id);
                Check.Equal("7001",store.ResolveNpcTaskTarget(ready,1).ItemId);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());ready=restored.ReadNpcTask(task.Id);
                var handover=Job(ready,1,103);handover.Target=restored.ResolveNpcTaskTarget(ready,1);
                Check.True(restored.TryDispatchNpcTaskStep(ready.Id,ready.Revision,103,handover,true,true,true,out reason),reason);
                var forged=store.Snapshot();forged.NpcActions[0].AcquiredItemId="7002";
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(forged));
            })));
        }
    }
}
