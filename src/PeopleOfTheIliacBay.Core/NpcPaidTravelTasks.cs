using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public NpcActionJob ReadPaidFollowForTravelTask(string personId,double now)
        {
            var follow=ActiveNpcAction(personId);NpcServiceContract contract;
            if(follow==null || NpcActionExecutor.IsTerminal(follow) || follow.FollowTransition!=null ||
                follow.PaidTravelHandoff!=null || follow.LastPosition==null || ActiveNpcTask(personId)!=null ||
                follow.CompanionRecovery!=null && follow.CompanionRecovery.Phase!=NpcCompanionRecoveryPhase.Following ||
                !NpcPaidCompanionTravel.TryReadActiveTravel(this,follow,now,out contract))return null;
            return follow;
        }

        public bool TryBindPaidFollowTravelTask(NpcTaskPlan proposed,out NpcTaskPlan bound,out string reason)
        {
            bound=null;reason="No active paid travel service covers this task.";
            if(proposed==null || proposed.PaidFollowActionId!=null)return false;
            var follow=ReadPaidFollowForTravelTask(proposed.ResidentId,proposed.AcceptedAt);
            if(follow==null)return false;
            try
            {
                var candidate=NpcTaskContract.Copy(proposed);candidate.PaidFollowActionId=follow.Id;
                candidate.DeadlineAt=Math.Min(candidate.DeadlineAt,follow.FinishAt);
                NpcTaskContract.Validate(candidate);
                if(!ValidPaidTravelTaskBinding(state,candidate))return false;
                bound=candidate;reason="Covered by the existing paid travel service.";return true;
            }
            catch(ArgumentException){return false;}
        }

        private static bool PaidFollowHeldByTravelTask(WorldState world,NpcActionJob follow)
        {
            return world.NpcTasks!=null && world.NpcTasks.Exists(task=>task!=null &&
                !NpcTaskContract.IsTerminal(task) && task.ResidentId==follow.ResidentId && task.PaidFollowActionId==follow.Id);
        }

        private static bool ValidPaidTravelTaskBinding(WorldState world,NpcTaskPlan task)
        {
            if(task.PaidFollowActionId==null)return true;
            var follow=world.NpcActions.Find(job=>job.Id==task.PaidFollowActionId);
            if(follow==null || follow.ResidentId!=task.ResidentId || !NpcPaidCompanionAction.IsPaidFollowJob(follow) ||
                task.AcceptedAt<follow.AcceptedAt || task.AcceptedAt>=follow.FinishAt || task.DeadlineAt>follow.FinishAt ||
                task.Admission!=null && task.Admission.Kind!=NpcOperationAdmissionKind.Conversation)return false;
            var contract=world.NpcServiceContracts.Contracts.Find(item=>item.ContractId==NpcPaidCompanionAction.ContractId(follow));
            if(contract==null || contract.NpcId!=task.ResidentId || contract.PayerId!="player" ||
                (contract.Coverage & (NpcServiceCoverage.Follow|NpcServiceCoverage.Travel))!=(NpcServiceCoverage.Follow|NpcServiceCoverage.Travel) ||
                contract.BlockStartAt!=follow.AcceptedAt || contract.BlockEndAt!=follow.FinishAt ||
                !NpcTaskContract.IsTerminal(task) && (NpcActionExecutor.IsTerminal(follow) || contract.Status!=NpcServiceContractStatus.Active))return false;
            return !task.Steps.Exists(step=>step.Kind!=NpcActionKind.MoveTo && step.Kind!=NpcActionKind.Wait || step.ItemSourceStep!=0);
        }

        private void ReleasePaidFollowTravelTask(NpcTaskPlan task)
        {
            int parentIndex=state.NpcActions.FindIndex(job=>job.Id==task.PaidFollowActionId);
            if(parentIndex<0)return;
            var follow=NpcActionExecutor.Copy(state.NpcActions[parentIndex]);var last=follow.LastPosition;
            for(int i=0;i<task.Steps.Count;i++)
            {
                var child=state.NpcActions.Find(job=>job.Id==task.Id+":step:"+i);
                if(child==null)continue;
                if(child.LastPosition!=null)last=child.LastPosition;
                if(child.ReturningToRoutine){child.ReturningToRoutine=false;child.Revision++;}
            }
            if(NpcActionExecutor.IsTerminal(follow))return;
            follow.LastPosition=NpcActionExecutor.CopyTarget(last);
            var contract=state.NpcServiceContracts.Contracts.Find(item=>item.ContractId==NpcPaidCompanionAction.ContractId(follow));
            var resident=ResolveResident(task.ResidentId);
            if(task.UpdatedAt>=follow.FinishAt || contract==null || contract.Status!=NpcServiceContractStatus.Active ||
                resident==null || !resident.IsAlive || resident.IsQuestProtected)
            {
                NpcActionJob ended;
                if(!NpcActionExecutor.Advance(follow,follow.Revision,task.UpdatedAt,
                    NpcActionObservation.Cancelled("The existing paid travel service is no longer available."),out ended))return;
                follow=ended;follow.ReturningToRoutine=follow.LastPosition!=null;
            }
            else {follow.UpdatedAt=Math.Max(follow.UpdatedAt,task.UpdatedAt);follow.Revision++;}
            NpcActionExecutor.Validate(follow);state.NpcActions[parentIndex]=follow;
        }
    }
}
