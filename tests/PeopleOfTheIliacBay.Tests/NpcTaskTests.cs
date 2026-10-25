using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcTaskTests
    {
        private static WorldStateStore Store(out NpcTaskPlan task)
        {
            var store=new WorldStateStore(42);var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
            task=new NpcTaskPlan {Id="task:1",ProposalId="turn:1",ResidentId=person.Id,AcceptedAt=100,UpdatedAt=100,DeadlineAt=500};
            task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Wait,DurationSeconds=10,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"}});
            task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10}});
            return store;
        }
        private static NpcActionJob Job(NpcTaskPlan task,double now)
        {
            var step=task.Steps[task.CurrentStepIndex];
            return new NpcActionJob {Id=NpcTaskContract.ActionId(task),ProposalId=NpcTaskContract.ActionId(task),ResidentId=task.ResidentId,
                Kind=step.Kind,Target=NpcActionExecutor.CopyTarget(step.Target),Phase=NpcActionPhase.Accepted,Revision=1,
                AcceptedAt=now,UpdatedAt=now,DeadlineAt=task.DeadlineAt,FinishAt=step.DurationSeconds>0?now+step.DurationSeconds:0};
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Timed surveyed seat survives task dispatch and restores before the next step",()=>{
                NpcTaskPlan task;var store=Store(out task);string reason;
                task.Steps=new List<NpcTaskStep> {
                    new NpcTaskStep {Kind=NpcActionKind.TakeSeat,DurationSeconds=30,Target=new NpcActionTarget {
                        Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10,
                        ContainerId="seat:1",X=2,Y=0,Z=3}},
                    new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=new NpcActionTarget {
                        Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10,X=6,Y=0,Z=3}}};
                Check.True(NpcTaskContract.Supported(NpcActionKind.TakeSeat));
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store,task,true,out reason),reason);
                var seat=Job(task,100);
                Check.Equal(130d,seat.FinishAt);
                Check.True(store.TryDispatchNpcTaskStep(task.Id,1,100,seat,true,true,true,out reason),reason);
                var restored=new WorldStateStore(99);restored.Restore(store.Snapshot());
                task=restored.ReadNpcTask(task.Id);
                Check.Equal(30d,task.Steps[0].DurationSeconds);
                Check.Equal("seat:1",task.Steps[0].Target.ContainerId);
                Check.Equal(130d,restored.ReadNpcAction(seat.Id).FinishAt);
                task.Steps[0].DurationSeconds=0;
                Check.Throws<ArgumentException>(()=>NpcTaskContract.Validate(task));
            }));
            cases.Add(("NPC task permits quoted shop purchase but rejects unrelated service dispatch",()=>{
                NpcTaskPlan task;var store=Store(out task);string reason;
                task.Steps[0]=new NpcTaskStep {Kind=NpcActionKind.Purchase,Target=new NpcActionTarget {
                    Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10,PersonId="seller",ContainerId="7",ItemId="42",QuotedGold=9}};
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);
                var job=Job(task,100);job.ServiceCode="tavern-beer";job.ServicePrice=1;
                Check.True(!store.TryDispatchNpcTaskStep(task.Id,1,100,job,true,true,true,out reason));
                job.ServiceCode=NpcShopCommerce.ServiceCode;job.ServicePrice=9;
                Check.True(store.TryDispatchNpcTaskStep(task.Id,1,100,job,true,true,true,out reason),reason);
                var restored=new WorldStateStore(99);restored.Restore(store.Snapshot());
                Check.Equal(NpcTaskPhase.Running,restored.ReadNpcTask(task.Id).Phase);
            }));
            cases.Add(("NPC task accepts terminal recovery and owns dispatch against unrelated actions",()=>{
                NpcTaskPlan task;var store=Store(out task);string reason;var prior=Job(task,100);prior.Id="prior";prior.ProposalId="prior";
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, prior,true,true,true,out reason));
                Check.True(store.ObserveNpcAction(prior.Id,1,101,NpcActionObservation.Cancelled("Stopped.")));
                var point=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10};
                Check.True(store.SaveNpcActionPosition(prior.Id,2,point,true,101));
                task.AcceptedAt=101;task.UpdatedAt=101;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason));
                var unrelated=Job(task,102);unrelated.Id="unrelated";unrelated.ProposalId="unrelated";
                Check.True(!ClassifiedAdmissionFixture.TryAcceptAction(store,unrelated,true,true,true,out reason));
                var job=Job(task,102);job.LastPosition=point;
                Check.True(store.TryDispatchNpcTaskStep(task.Id,1,102,job,true,true,true,out reason));
                Check.True(!store.ReadNpcAction(prior.Id).ReturningToRoutine);
                Check.Equal(0d,store.ReadNpcAction(job.Id).LastPosition.X);
            }));
            cases.Add(("NPC task dispatch failure is terminal and cannot cancel confirmed progress",()=>{
                NpcTaskPlan task;var store=Store(out task);string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason));
                Check.True(!store.FailNpcTask(task.Id,2,101,"The target is unavailable."));
                Check.True(store.FailNpcTask(task.Id,1,101,"The target is unavailable."));
                Check.Equal(NpcTaskPhase.Failed,store.ReadNpcTask(task.Id).Phase);
                Check.True(!store.TryDispatchNpcTaskStep(task.Id,2,102,Job(task,102),true,true,true,out reason));
                var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
                Check.Equal("The target is unavailable.",restored.ReadNpcTask(task.Id).FailureReason);
            }));
            cases.Add(("NPC task persists bounded typed steps instead of dialogue promises",()=>{
                Check.True(typeof(WorldState).GetField("NpcTasks")!=null,"Save state must contain the task ledger.");
                Check.True(typeof(WorldStateStore).GetMethod("TryDispatchNpcTaskStep")!=null,"Task steps need validated action dispatch.");
            }));
            cases.Add(("NPC task advances only from stored confirmed completion and survives independent restore",()=>{
                NpcTaskPlan task;var store=Store(out task);string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason));
                var job=Job(task,100);Check.True(store.TryDispatchNpcTaskStep(task.Id,1,100,job,true,true,true,out reason));
                Check.True(!store.ReconcileNpcTask(task.Id,2,101));
                Check.True(!store.TryDispatchNpcTaskStep(task.Id,2,101,job,true,true,true,out reason));
                Check.True(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()));
                Check.True(!store.ObserveNpcAction(job.Id,2,109,NpcActionObservation.Success("effect:1")));
                Check.True(store.ObserveNpcAction(job.Id,2,110,NpcActionObservation.Success("effect:1")));
                var restored=new WorldStateStore(99);restored.Restore(store.Snapshot());
                Check.True(restored.ReconcileNpcTask(task.Id,2,110));
                Check.True(!restored.ReconcileNpcTask(task.Id,2,110));
                task=restored.ReadNpcTask(task.Id);Check.Equal(1,task.CurrentStepIndex);Check.Equal("effect:1",task.Steps[0].CompletedEffectId);
                job=Job(task,111);Check.True(restored.TryDispatchNpcTaskStep(task.Id,task.Revision,111,job,true,true,true,out reason));
                Check.True(restored.ObserveNpcAction(job.Id,1,112,NpcActionObservation.Arrival()));
                Check.True(restored.ObserveNpcAction(job.Id,2,113,NpcActionObservation.Success("effect:2")));
                Check.True(restored.ReconcileNpcTask(task.Id,4,113));Check.Equal(NpcTaskPhase.Completed,restored.ReadNpcTask(task.Id).Phase);
                Check.True(restored.ActiveNpcTask(task.ResidentId)==null);
                var saved=restored.Snapshot();saved.NpcTasks[0].Steps[0].Target.PersonId="other";
                Check.Equal("player",restored.ReadNpcTask(task.Id).Steps[0].Target.PersonId);
                Check.Throws<ArgumentException>(()=>store.Restore(saved));
            }));
            cases.Add(("Wait after static MoveTo resumes only after saved arrival receipt",()=>{
                NpcTaskPlan task;var store=Store(out task);string reason;
                var destination=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10,X=4,Y=0,Z=7};
                task.Steps=new List<NpcTaskStep> {
                    new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=NpcActionExecutor.CopyTarget(destination)},
                    new NpcTaskStep {Kind=NpcActionKind.Wait,DurationSeconds=30,Target=NpcActionExecutor.CopyTarget(destination)}};
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason),reason);
                var move=Job(task,100);
                Check.True(store.TryDispatchNpcTaskStep(task.Id,task.Revision,100,move,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(move.Id,move.Revision,101,NpcActionObservation.Arrival()));move=store.ReadNpcAction(move.Id);
                Check.True(store.ObserveNpcAction(move.Id,move.Revision,102,NpcActionObservation.Success(move.Id+":observed")));
                task=store.ReadNpcTask(task.Id);
                Check.True(store.ReconcileNpcTask(task.Id,task.Revision,102));task=store.ReadNpcTask(task.Id);
                Check.Equal(1,task.CurrentStepIndex);Check.True(task.Steps[0].CompletedEffectId!=null);
                var restored=new WorldStateStore(99);restored.Restore(store.Snapshot());task=restored.ReadNpcTask(task.Id);
                move=restored.ReadNpcAction(task.Id+":step:0");
                Check.True(NpcTaskPositionBinding.HasCompletedMoveToReceipt(task.Steps[0],move,task.Steps[1]));
                var wait=Job(task,110);
                Check.True(restored.TryDispatchNpcTaskStep(task.Id,task.Revision,110,wait,true,true,true,out reason),reason);
                Check.Equal(110d,restored.ReadNpcAction(wait.Id).AcceptedAt);
                Check.Equal(140d,restored.ReadNpcAction(wait.Id).FinishAt);
            }));
            cases.Add(("Unobserved Enter completes its exact building Wait from the physical door handoff",()=>{
                NpcTaskPlan task;var store=Store(out task);string reason;
                var doorway=new NpcActionTarget {Kind=NpcActionTargetKind.Building,SettlementId=100,BuildingKey=10,
                    DoorId="door:100:10:1:2:3",WorldSpaceId="world:100",DestinationSpaceId="interior:100:10"};
                task.Steps=new List<NpcTaskStep> {
                    new NpcTaskStep {Kind=NpcActionKind.Enter,Target=NpcActionExecutor.CopyTarget(doorway)},
                    new NpcTaskStep {Kind=NpcActionKind.Wait,DurationSeconds=30,Target=NpcActionExecutor.CopyTarget(doorway)}};
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store,task,true,out reason),reason);
                var enter=Job(task,100);
                Check.True(store.TryDispatchNpcTaskStep(task.Id,task.Revision,100,enter,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(enter.Id,enter.Revision,105,NpcActionObservation.Arrival()));
                enter=store.ReadNpcAction(enter.Id);
                var receipt=NpcBuildingTransitionReceipt.Create(enter,task.ResidentId,100,10,doorway.DoorId,
                    doorway.WorldSpaceId,doorway.DestinationSpaceId,106);
                Check.True(store.TryCompleteNpcBuildingTransition(enter.Id,enter.Revision,106,receipt));
                task=store.ReadNpcTask(task.Id);
                Check.True(store.ReconcileNpcTask(task.Id,task.Revision,106));
                task=store.ReadNpcTask(task.Id);Check.Equal(1,task.CurrentStepIndex);
                Check.True(!store.TryCompleteOffscreenBuildingWait(task.Id,task.Revision,135),
                    "The offscreen timer must not complete before its physical door-entry time plus duration.");
                Check.True(store.TryCompleteOffscreenBuildingWait(task.Id,task.Revision,136));
                task=store.ReadNpcTask(task.Id);Check.Equal(NpcTaskPhase.Completed,task.Phase);Check.Equal(2,task.CurrentStepIndex);
                var wait=store.ReadNpcAction(task.Id+":step:1");
                Check.Equal(106d,wait.AcceptedAt);Check.Equal(136d,wait.FinishAt);Check.Equal(wait.Id+":offscreen",wait.CompletedEffectId);
                Check.True(NpcTaskContract.TargetMatches(doorway,wait.Target));
                var landed=store.ReadNpcAction(enter.Id);
                Check.True(!landed.BuildingTransitionReceipt.DestinationObserved,
                    "Completing the offscreen wait must not invent an unseen interior landing.");
                var restored=new WorldStateStore(99);restored.Restore(store.Snapshot());
                Check.Equal(NpcTaskPhase.Completed,restored.ReadNpcTask(task.Id).Phase);
            }));
            cases.Add(("NPC task dispatch rechecks adapter consent schedule and exact target",()=>{
                NpcTaskPlan task;var store=Store(out task);string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason));var job=Job(task,100);
                Check.True(!store.TryDispatchNpcTaskStep(task.Id,1,100,job,false,true,true,out reason));
                Check.True(!store.TryDispatchNpcTaskStep(task.Id,1,100,job,true,false,true,out reason));
                Check.True(!store.TryDispatchNpcTaskStep(task.Id,1,100,job,true,true,false,out reason));
                job.Target.PersonId="unrelated";Check.True(!store.TryDispatchNpcTaskStep(task.Id,1,100,job,true,true,true,out reason));
                Check.Equal(0,store.Snapshot().NpcActions.Count);Check.Equal(1L,store.ReadNpcTask(task.Id).Revision);
                Check.True(!store.TryAcceptNpcTask(task,true,out reason));
            }));
            cases.Add(("NPC task cancellation preserves action recovery and never launches later steps",()=>{
                NpcTaskPlan task;var store=Store(out task);string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason));var job=Job(task,100);
                Check.True(store.TryDispatchNpcTaskStep(task.Id,1,100,job,true,true,true,out reason));
                Check.True(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Cancelled("Stopped by player.")));
                var position=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10,X=1};
                Check.True(store.SaveNpcActionPosition(job.Id,2,position,true,101));
                Check.True(store.CancelNpcTask(task.Id,2,101,"Stopped by player."));
                Check.Equal(NpcTaskPhase.Cancelled,store.ReadNpcTask(task.Id).Phase);
                Check.True(store.ReadNpcAction(job.Id).ReturningToRoutine);
                Check.True(!store.ReconcileNpcTask(task.Id,3,102));
                var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
                Check.Equal(NpcTaskPhase.Cancelled,restored.ReadNpcTask(task.Id).Phase);
            }));
            cases.Add(("NPC task failure deadline and legacy migration cannot invent completed effects",()=>{
                NpcTaskPlan task;var store=Store(out task);string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason));
                Check.True(store.ReconcileNpcTask(task.Id,1,500));Check.Equal(NpcTaskPhase.Failed,store.ReadNpcTask(task.Id).Phase);
                var saved=store.Snapshot();saved.Version=28;saved.NpcTasks=null;
                var restored=new WorldStateStore(1);restored.Restore(saved);Check.Equal(new WorldState().Version,restored.Snapshot().Version);Check.Equal(0,restored.PendingNpcTasks().Count);
                saved=store.Snapshot();saved.NpcTasks[0].Steps[0].CompletedEffectId="invented";
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));
                saved=store.Snapshot();saved.NpcTasks[0].Steps[0].Kind=NpcActionKind.Attack;
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));
            }));
            cases.Add(("NPC task restore rejects orphan actions and terminal tasks with live actions",()=>{
                NpcTaskPlan task;var store=Store(out task);string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store, task,true,out reason));var job=Job(task,100);
                Check.True(store.TryDispatchNpcTaskStep(task.Id,1,100,job,true,true,true,out reason));
                var saved=store.Snapshot();saved.NpcActions.Clear();Check.Throws<ArgumentException>(()=>store.Restore(saved));
                saved=store.Snapshot();saved.NpcTasks[0].Phase=NpcTaskPhase.Cancelled;saved.NpcTasks[0].FailureReason="stopped";
                Check.Throws<ArgumentException>(()=>store.Restore(saved));
            }));
        }
    }
}
