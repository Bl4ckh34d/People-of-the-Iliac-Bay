using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcGoalChainLengthTests
    {
        private static ConversationContextBinding Binding()
        {
            return new ConversationContextBinding(ConversationActorCategory.Mobile,"resident:chain",null,"session:chain",1,1,
                "0123456789abcdef0123456789abcdef","","scene:chain",1);
        }

        private static NpcGoalProposal Parse(int count)
        {
            var sources=new List<string>();
            var output=new StringBuilder("{\"intent\":\"request\",\"steps\":[");
            for(int i=0;i<count;i++)
            {
                if(i>0)output.Append(',');
                bool move=(i%2)==0;
                string source=move?"go to point "+(i/2)+" step "+i:"wait there for eighteen seconds step "+i;
                sources.Add(source);
                output.Append("{\"kind\":\"").Append(move?"MoveTo":"Wait").Append("\",\"source\":\"").Append(source)
                    .Append("\",\"target\":\"").Append(move?"point "+(i/2):"there").Append("\",\"duration\":");
                if(move)output.Append("null");
                else output.Append("{\"amount\":18,\"unit\":\"game_seconds\",\"source\":\"eighteen seconds\"}");
                output.Append(",\"quantity\":null,\"missing\":[]}");
            }
            output.Append("],\"conditions\":[],\"exclusions\":[],\"missing\":[]}");
            string text=string.Join("; ",sources.ToArray());
            var parsed=NpcGoalChat.Parse(output.ToString(),Binding(),text);
            Check.True(parsed.Status==NpcGoalExtractionStatus.Valid,parsed.Status+": "+parsed.Reason+"; output="+output);
            Check.Equal(count,parsed.Proposal.Steps.Count);
            return parsed.Proposal;
        }

        private static NpcActionTarget Point(int index)
        {
            return new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="map:chain",
                X=10+index*10,Y=0,Z=5};
        }

        private static NpcTaskPlan Plan(NpcGoalProposal proposal,IList<NpcActionDecision> decisions,string residentId)
        {
            var task=new NpcTaskPlan {Id="task:chain:"+proposal.Steps.Count,ProposalId="proposal:chain:"+proposal.Steps.Count,
                ResidentId=residentId,AcceptedAt=100,UpdatedAt=100,DeadlineAt=80000};
            foreach(var decision in decisions)
                task.Steps.Add(new NpcTaskStep {Kind=decision.Option.Kind,Target=NpcActionExecutor.CopyTarget(decision.Option.Target),
                    DurationSeconds=decision.Option.Kind==NpcActionKind.Wait || decision.Option.Kind==NpcActionKind.Follow
                        ? decision.DurationSeconds : 0});
            return task;
        }

        private static NpcActionJob StepJob(NpcTaskPlan task,double now)
        {
            NpcTaskStep step=task.Steps[task.CurrentStepIndex];
            return new NpcActionJob {Id=NpcTaskContract.ActionId(task),ProposalId=NpcTaskContract.ActionId(task),
                ResidentId=task.ResidentId,Kind=step.Kind,Target=NpcActionExecutor.CopyTarget(step.Target),
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=now,UpdatedAt=now,DeadlineAt=task.DeadlineAt,
                FinishAt=step.DurationSeconds>0?now+step.DurationSeconds:0};
        }

        private static IList<NpcActionOption> Candidates(NpcGoalProposal proposal,int index,IList<NpcActionDecision> previous)
        {
            NpcGoalStep step=proposal.Steps[index];
            if(step.Kind=="MoveTo")
            {
                int targetIndex=index/2;
                return new[]{new NpcActionOption {Id=index+1,Kind=NpcActionKind.MoveTo,Label="native move candidate",
                    Target=Point(targetIndex)}};
            }
            NpcActionTarget prior=previous[index-1].Option.Target;
            return new[]{new NpcActionOption {Id=index+1,Kind=NpcActionKind.Wait,Label="native wait candidate",
                Target=NpcActionExecutor.CopyTarget(prior)}};
        }

        private static NpcBoundGoal Bind(NpcGoalProposal proposal)
        {
            var candidates=new List<IList<NpcActionOption>>();
            var ids=new List<int>();
            var prior=new List<NpcActionDecision>();
            for(int i=0;i<proposal.Steps.Count;i++)
            {
                IList<NpcActionOption> offered=Candidates(proposal,i,prior);
                NpcActionDecision decision;string reason;
                Check.True(NpcGoalBinding.TryBindStep(proposal,i,offered,i+1,prior,out decision,out reason),reason);
                candidates.Add(offered);ids.Add(i+1);prior.Add(decision);
            }
            NpcBoundGoal bound;string bindReason;
            Check.True(NpcGoalBinding.TryBind(proposal,candidates,ids,out bound,out bindReason),bindReason);
            return bound;
        }

        private static void ExecuteChain(int count)
        {
            NpcGoalProposal proposal=Parse(count);
            IList<NpcActionDecision> decisions=Bind(proposal).CopyDecisions();
            Check.Equal(count,decisions.Count);
            var store=new WorldStateStore(71);
            string resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;
            NpcTaskPlan task=Plan(proposal,decisions,resident);string reason;
            try { NpcTaskContract.Validate(task); }
            catch(ArgumentException error) { throw new Exception("Generated "+count+"-step task is invalid: "+error.Message); }
            bool accepted=ClassifiedAdmissionFixture.TryAcceptTask(store,task,true,out reason);
            Check.True(accepted,reason+"; task="+task.Id+"/"+task.Phase+"/"+task.Revision+"/"+task.CurrentStepIndex+
                "; resident="+resident+"; time="+task.AcceptedAt+"/"+task.UpdatedAt+"/"+task.DeadlineAt);
            int completedEffects=0;
            bool restoredAtMiddle=false;
            double now=100;
            while(task.CurrentStepIndex<count)
            {
                int stepIndex=task.CurrentStepIndex;
                NpcActionJob job=StepJob(task,now);
                Check.True(store.TryDispatchNpcTaskStep(task.Id,task.Revision,now,job,true,true,true,out reason),reason);
                task=store.ReadNpcTask(task.Id);
                if(stepIndex>0 && task.Steps[stepIndex].Kind==NpcActionKind.Wait)
                    Check.True(NpcTaskPositionBinding.HasCompletedMoveToReceipt(task.Steps[stepIndex-1],
                        store.ReadNpcAction(task.Id+":step:"+(stepIndex-1)),task.Steps[stepIndex]),
                        "Wait must start only from preceding saved MoveTo receipt.");
                NpcActionJob saved=store.ReadNpcAction(job.Id);
                Check.True(saved!=null && saved.Phase==NpcActionPhase.Accepted,"Step dispatch must create its own ordered action.");
                Check.True(store.ObserveNpcAction(saved.Id,saved.Revision,now+1,NpcActionObservation.Arrival()),
                    "Synthetic arrival failed at "+count+"-step index "+stepIndex+".");
                saved=store.ReadNpcAction(saved.Id);
                Check.True(store.ObserveNpcAction(saved.Id,saved.Revision,now+20,NpcActionObservation.Success("effect:"+stepIndex)),
                    "Synthetic success failed at "+count+"-step index "+stepIndex+".");
                Check.True(store.ReconcileNpcTask(task.Id,task.Revision,now+20),
                    "Synthetic task reconcile failed at "+count+"-step index "+stepIndex+".");
                task=store.ReadNpcTask(task.Id);
                completedEffects++;
                Check.Equal(stepIndex+1,task.CurrentStepIndex);
                Check.Equal(completedEffects,task.Steps.Take(task.CurrentStepIndex).Count(x=>x.CompletedEffectId!=null));
                Check.Equal(stepIndex+1,store.Snapshot().NpcActions.Count(x=>x.Phase==NpcActionPhase.Completed));

                if(!restoredAtMiddle && task.CurrentStepIndex==Math.Max(1,count/2))
                {
                    var independent=new WorldStateStore(99);
                    independent.Restore(store.Snapshot());
                    task=independent.ReadNpcTask(task.Id);
                    store=independent;
                    restoredAtMiddle=true;
                    Check.Equal(stepIndex+1,task.CurrentStepIndex);
                    for(int prior=0;prior<task.CurrentStepIndex;prior++)
                    {
                        var receipt=store.ReadNpcAction(task.Id+":step:"+prior);
                        Check.True(receipt!=null && receipt.Phase==NpcActionPhase.Completed,
                            "Snapshot restore must retain completed effect receipt.");
                        Check.Equal("effect:"+prior,receipt.CompletedEffectId);
                    }
                    Check.Equal(null,store.ActiveNpcAction(resident));
                }
                now+=30;
            }

            task=store.ReadNpcTask(task.Id);
            Check.True(restoredAtMiddle,"Every chain length must exercise independent snapshot restore.");
            Check.Equal(NpcTaskPhase.Completed,task.Phase);
            Check.Equal(count,task.CurrentStepIndex);
            Check.Equal(count,completedEffects);
            Check.Equal(null,store.ActiveNpcTask(resident));
            Check.Equal(null,store.ActiveNpcAction(resident));
            for(int i=0;i<count;i++)
            {
                var action=store.ReadNpcAction(task.Id+":step:"+i);
                Check.True(action!=null && action.Phase==NpcActionPhase.Completed,"All ordered action receipts must complete.");
                Check.Equal("effect:"+i,action.CompletedEffectId);
            }
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Goal chains of three four and five steps bind in order and resume after snapshot restore",new Action(()=>{
                ExecuteChain(3);ExecuteChain(4);ExecuteChain(5);
            })));
            cases.Add(("Goal extraction rejects nine task steps",new Action(()=>{
                var text=string.Join("; ",Enumerable.Range(0,9).Select(i=>"step "+i));
                var output=new StringBuilder("{\"intent\":\"request\",\"steps\":[");
                for(int i=0;i<9;i++)
                {
                    if(i>0)output.Append(',');
                    output.Append("{\"kind\":\"MoveTo\",\"source\":\"step ").Append(i)
                        .Append("\",\"target\":\"point ").Append(i).Append("\",\"duration\":null,\"quantity\":null,\"missing\":[]}");
                }
                output.Append("],\"conditions\":[],\"exclusions\":[],\"missing\":[]}");
                var result=NpcGoalChat.Parse(output.ToString(),Binding(),text);
                Check.Equal(NpcGoalExtractionStatus.Invalid,result.Status);
                Check.True(result.Reason.Contains("Too many steps."));
            })));
        }
    }
}
