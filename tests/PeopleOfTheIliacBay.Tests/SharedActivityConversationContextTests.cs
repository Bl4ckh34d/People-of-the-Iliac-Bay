using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class SharedActivityConversationContextTests
    {
        private static NpcActionTarget Outside(double x=0)
        { return new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=100,WorldSpaceId="settlement:100",X=x}; }
        private static NpcActionTarget Inside(int building=10)
        { return new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=building}; }

        private static WorldStateStore ActiveFollow(out NpcActionJob job)
        {
            var store=new WorldStateStore(42);
            var resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
            job=new NpcActionJob {Id="shared-follow",ProposalId="shared-follow-turn",ResidentId=resident.Id,
                Kind=NpcActionKind.Follow,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=1000,FinishAt=500,
                LastPosition=Outside()};
            string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason),reason);
            Check.True(store.ObserveNpcAction(job.Id,job.Revision,101,NpcActionObservation.Approach()));
            job=store.ReadNpcAction(job.Id);
            return store;
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            Action survivesSaveRestoreAndDoorwayTransition=()=>{
                NpcActionJob job;var store=ActiveFollow(out job);
                string context=SharedActivityConversationContext.Build(job,job.ResidentId,120,"inside the Red Petal");
                Check.True(!string.IsNullOrWhiteSpace(context));
                Check.True(context.Contains("active Follow action") && context.Contains("player"));
                Check.True(context.Contains("Do not invent an absence, reunion"),"Context must not invent absence or reunion.");
                Check.True(!context.Contains("continuous company"));
                Check.True(context.Contains("inside the Red Petal"));

                var options=new JsonSerializerOptions {IncludeFields=true};
                string saved=JsonSerializer.Serialize(store.Snapshot(),options);
                var restored=new WorldStateStore(1);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(saved,options));
                job=restored.ReadNpcAction(job.Id);
                context=SharedActivityConversationContext.Build(job,job.ResidentId,120,"outside the Red Petal");
                Check.True(!context.Contains("continuous company"));

                Check.True(restored.TryBeginNpcFollowTransition(job.Id,job.Revision,120,Inside(),
                    new List<NpcActionTarget>{Outside(),Outside(10)},2));
                job=restored.ReadNpcAction(job.Id);
                context=SharedActivityConversationContext.Build(job,job.ResidentId,121,"outside the Red Petal");
                Check.True(context.Contains("doorway transition is in progress"));
                Check.True(context.Contains("arrival is not confirmed"));
            };
            cases.Add(("Shared follow context survives save restore and doorway transition",survivesSaveRestoreAndDoorwayTransition));

            Action failsClosedForOtherInactiveOrSeparatedOwnership=()=>{
                NpcActionJob job;ActiveFollow(out job);
                Check.True(string.IsNullOrEmpty(SharedActivityConversationContext.Build(job,"another-resident",120,"same place")));

                var wrongTarget=NpcActionExecutor.Copy(job);wrongTarget.Target.PersonId="someone-else";
                Check.True(string.IsNullOrEmpty(SharedActivityConversationContext.Build(wrongTarget,job.ResidentId,120,"same place")));

                var wrongKind=NpcActionExecutor.Copy(job);wrongKind.Kind=NpcActionKind.Wait;
                Check.True(string.IsNullOrEmpty(SharedActivityConversationContext.Build(wrongKind,job.ResidentId,120,"same place")));

                var notStarted=NpcActionExecutor.Copy(job);notStarted.Phase=NpcActionPhase.Accepted;
                Check.True(string.IsNullOrEmpty(SharedActivityConversationContext.Build(notStarted,job.ResidentId,120,"same place")));

                var futureUpdate=NpcActionExecutor.Copy(job);futureUpdate.UpdatedAt=121;
                Check.True(string.IsNullOrEmpty(SharedActivityConversationContext.Build(futureUpdate,job.ResidentId,120,"same place")));

                Check.True(string.IsNullOrEmpty(SharedActivityConversationContext.Build(job,job.ResidentId,500,"same place")),
                    "Expired follow finish must allow normal reunion wording.");
                Check.True(string.IsNullOrEmpty(SharedActivityConversationContext.Build(job,job.ResidentId,1000,"same place")),
                    "Expired consent must allow normal reunion wording.");

                var cancelled=NpcActionExecutor.Copy(job);cancelled.Phase=NpcActionPhase.Cancelled;
                cancelled.FailureReason="Stop following.";
                Check.True(string.IsNullOrEmpty(SharedActivityConversationContext.Build(cancelled,job.ResidentId,120,"same place")));

                NpcActionJob completed;var completedStore=ActiveFollow(out completed);
                Check.True(completedStore.ObserveNpcAction(completed.Id,completed.Revision,120,NpcActionObservation.Arrival()));
                completed=completedStore.ReadNpcAction(completed.Id);
                Check.True(completedStore.ObserveNpcAction(completed.Id,completed.Revision,500,
                    NpcActionObservation.Success(completed.Id+":done")));
                completed=completedStore.ReadNpcAction(completed.Id);
                Check.True(string.IsNullOrEmpty(SharedActivityConversationContext.Build(completed,completed.ResidentId,500,"same place")));
            };
            cases.Add(("Shared follow context fails closed for other, inactive, or separated ownership",failsClosedForOtherInactiveOrSeparatedOwnership));

            Action activeApproachDoesNotInventContinuousPresence=()=>{
                NpcActionJob job;ActiveFollow(out job);
                var catchingUp=NpcActionExecutor.Copy(job);
                catchingUp.Phase=NpcActionPhase.Approaching;
                catchingUp.LastPosition=Outside(5000);
                string context=SharedActivityConversationContext.Build(catchingUp,job.ResidentId,120,"current Talk location");
                Check.True(!string.IsNullOrWhiteSpace(context));
                Check.True(context.Contains("currently") && context.Contains("Talk session"),"Context must state current Talk presence.");
                Check.True(context.Contains("do not prove uninterrupted historical presence"),"Active follow must not prove uninterrupted history.");
                Check.True(context.Contains("does not establish that you were apart"),"Doorway or Talk reentry alone must not prove separation.");
                Check.True(!context.Contains("continuous company") && !context.Contains("same outing"));
            };
            cases.Add(("Active follow does not invent uninterrupted historical presence",activeApproachDoesNotInventContinuousPresence));
        }
    }
}
