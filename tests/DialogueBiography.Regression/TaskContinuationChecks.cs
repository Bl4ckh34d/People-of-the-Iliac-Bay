using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class TaskContinuationChecks
    {
        private static NpcTaskPlan Plan(WorldStateStore store,bool independent)
        {
            string person=store.GetOrCreate(7,new[]{13},1).Households[0].Residents[0].Id;
            var plan=new NpcTaskPlan {Id="task:continuation",ProposalId="proposal:continuation",ResidentId=person,
                AcceptedAt=10,UpdatedAt=10,DeadlineAt=900};
            for(int i=0;i<4;i++)plan.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.MoveTo,
                ContinueAfterFailure=independent && (i==1 || i==3),Target=new NpcActionTarget {
                    Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:7",SettlementId=7,X=i}});
            string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptTask(store,plan,true,out reason),reason);return plan;
        }
        private static NpcActionJob Dispatch(WorldStateStore store,NpcTaskPlan plan,double now)
        {
            var current=store.ReadNpcTask(plan.Id);var step=current.Steps[current.CurrentStepIndex];
            var job=new NpcActionJob {Id=NpcTaskContract.ActionId(current),ProposalId=NpcTaskContract.ActionId(current),ResidentId=current.ResidentId,
                Kind=step.Kind,Target=NpcActionExecutor.CopyTarget(step.Target),Phase=NpcActionPhase.Accepted,Revision=1,
                AcceptedAt=now,UpdatedAt=now,DeadlineAt=current.DeadlineAt};
            string reason;Check.True(!store.TryDispatchNpcTaskStep(current.Id,current.Revision,now,job,true,false,true,out reason),
                "Independent continuation must still require normal native consent.");
            Check.True(store.TryDispatchNpcTaskStep(current.Id,current.Revision,now,job,true,true,true,out reason),reason);
            return job;
        }
        internal static int Run()
        {
            {
                var store=new WorldStateStore(81);var plan=Plan(store,false);
                Check.True(store.FailNpcTask(plan.Id,1,11,"The first route is blocked."));
                var failed=store.ReadNpcTask(plan.Id);
                Check.Equal(NpcTaskPhase.Failed,failed.Phase);Check.Equal(0,failed.CurrentStepIndex);
                Check.Equal(0,store.Snapshot().NpcActions.Count);
                Check.True(failed.FailureReceipt.Message.Contains("Later steps were not attempted"));
            }
            {
                var store=new WorldStateStore(82);var plan=Plan(store,true);
                Check.True(store.FailNpcTask(plan.Id,1,11,"The first route is blocked."));
                Check.True(!store.FailNpcTask(plan.Id,1,11,"Stale callback must not skip independent work."));
                var job=Dispatch(store,plan,12);
                Check.True(store.ObserveNpcAction(job.Id,1,12,NpcActionObservation.Arrival()));
                Check.True(store.ObserveNpcAction(job.Id,2,13,NpcActionObservation.Success("actual:independent-arrival")));
                Check.True(store.ReconcileNpcTask(plan.Id,3,13));
                var current=store.ReadNpcTask(plan.Id);
                Check.Equal(3,current.CurrentStepIndex);Check.True(current.Steps[2].Blocked);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                ConversationActionReply reply;
                Check.True(ConversationActionReply.TryFromSavedTask(restored,plan.Id,out reply));Check.Equal(1,reply.CompletedSteps);
                Check.True(reply.NpcText.Contains("An earlier step failed"));
                Check.True(restored.CancelNpcTask(plan.Id,current.Revision,14,"The player cancelled remaining work."));
                Check.Equal(NpcTaskPhase.Cancelled,restored.ReadNpcTask(plan.Id).Phase);
                Check.True(restored.ActiveNpcTask(plan.ResidentId)==null);
            }
            {
                var store=new WorldStateStore(83);var plan=Plan(store,true);var job=Dispatch(store,plan,11);
                Check.True(store.ObserveNpcAction(job.Id,1,12,NpcActionObservation.Interrupted("Native route blocked.")));
                Check.True(store.ReconcileNpcTask(plan.Id,2,12));
                Check.Equal(1,store.ReadNpcTask(plan.Id).CurrentStepIndex);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.Equal(NpcActionPhase.Failed,restored.ReadNpcAction(job.Id).Phase);
                var corrupt=store.Snapshot();corrupt.NpcActions[0].Target.X=999;
                Check.Throws<ArgumentException>(()=>restored.Restore(corrupt));
            }
            {
                var store=new WorldStateStore(84);var plan=Plan(store,true);
                Check.True(store.ReconcileNpcTask(plan.Id,1,900));
                Check.Equal(NpcTaskPhase.Failed,store.ReadNpcTask(plan.Id).Phase);
                Check.Equal(0,store.ReadNpcTask(plan.Id).CurrentStepIndex);
            }
            {
                var store=new WorldStateStore(85);var plan=Plan(store,true);var job=Dispatch(store,plan,11);
                Check.True(store.ObserveNpcAction(job.Id,1,12,NpcActionObservation.Cancelled("The player cancelled.")));
                Check.True(store.ReconcileNpcTask(plan.Id,2,12));
                Check.Equal(NpcTaskPhase.Cancelled,store.ReadNpcTask(plan.Id).Phase);
                Check.Equal(0,store.ReadNpcTask(plan.Id).CurrentStepIndex);
                Check.True(store.ActiveNpcTask(plan.ResidentId)==null);
            }
            return 5;
        }
    }
}
