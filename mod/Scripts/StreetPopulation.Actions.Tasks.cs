using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        public bool TryAcceptConversationTask(string residentId,string proposalId,List<NpcActionDecision> decisions,out string reason)
        {return TryAcceptConversationTaskCore(residentId,proposalId,decisions,null,true,out reason);}

        public bool TryAcceptConversationTask(string residentId,string proposalId,List<NpcActionDecision> decisions,
            NpcConversationCommandTurn commandTurn,out string reason)
        {
            if(commandTurn==null){reason="The player command is no longer current.";return false;}
            return TryAcceptConversationTaskCore(residentId,proposalId,decisions,commandTurn,false,out reason);
        }

        private bool TryAcceptConversationTaskCore(string residentId,string proposalId,List<NpcActionDecision> decisions,
            NpcConversationCommandTurn commandTurn,bool legacyAdmission,out string reason)
        {
            reason="No bounded local task was selected.";
            if(!Ready || worldStore==null || string.IsNullOrWhiteSpace(proposalId) || proposalId.Length>180 ||
                decisions==null || decisions.Count<2 || decisions.Count>NpcTaskContract.MaximumSteps)return false;
            var actor=FindActor(residentId);
            if(!actor || !actor.gameObject.activeInHierarchy ||
                Vector3.Distance(actor.transform.position,GameManager.Instance.PlayerMotor.transform.position)>8)
            {reason="The NPC must be nearby to agree to a task.";return false;}
            double now=ActionNow(),budget=60;
            var task=new NpcTaskPlan {Id="task:"+proposalId,ProposalId=proposalId,ResidentId=residentId,AcceptedAt=now,UpdatedAt=now};
            for(int i=0;i<decisions.Count;i++)
            {
                var decision=decisions[i];
                if(decision==null || decision.Option==null || !NpcTaskContract.Supported(decision.Option.Kind))return false;
                bool timed=decision.Option.Kind==NpcActionKind.Wait || decision.Option.Kind==NpcActionKind.Follow ||
                    decision.Option.Kind==NpcActionKind.TakeSeat;
                if(timed && (decision.DurationSeconds<10 || decision.DurationSeconds>900))return false;
                double duration=timed?decision.DurationSeconds:0;
                bool templeTraining=decision.Option.Kind==NpcActionKind.Purchase && decision.Option.Target!=null &&
                    decision.Option.Target.ContainerId==NativeNpcTempleTraining.TargetContainerId;
                budget+=timed?duration+60:templeTraining?NativeNpcTempleTraining.DurationSeconds+600:300;
                task.Steps.Add(new NpcTaskStep {Kind=decision.Option.Kind,Target=NpcActionExecutor.CopyTarget(decision.Option.Target),DurationSeconds=duration,ItemSourceStep=decision.Option.ItemSourceStep});
            }
            task.DeadlineAt=now+budget;
            if(legacyAdmission)
            {if(!worldStore.TryAcceptNpcTask(task,true,out reason))return false;}
            else
            {
                NpcTaskPlan prepared;
                if(!worldStore.TryPrepareNpcConversationTask(commandTurn,task,out prepared))
                {reason="The player command is no longer current.";return false;}
                task=prepared;
                NpcConversationCommandCapability capability;
                if(!worldStore.TryIssueNpcConversationTask(commandTurn,task,out capability))
                {reason="The player command is no longer current.";return false;}
                if(!worldStore.TryAcceptNpcTaskFromConversation(capability,task,true,out reason))return false;
            }
            if(!DispatchConversationTaskStep(task,out reason))
            {worldStore.FailNpcTask(task.Id,task.Revision,ActionNow(),reason);return false;}
            reason="The task is accepted and saved. Each later step will be checked again before it starts.";return true;
        }

        private bool DispatchConversationTaskStep(NpcTaskPlan task,out string reason)
        {
            var prior=task.CurrentStepIndex>0?worldStore.ReadNpcAction(task.Id+":step:"+(task.CurrentStepIndex-1)):null;
            if(NativeNpcShopPurchaseProbe.ShouldInjectTaskFailure(task,prior,out reason))return false;
            var step=task.Steps[task.CurrentStepIndex];
            if(step.Kind==NpcActionKind.Wait && task.CurrentStepIndex>0)
            {
                var previousStep=task.Steps[task.CurrentStepIndex-1];
                if(!NpcTaskPositionBinding.HasCompletedStaticArrivalReceipt(previousStep,prior,step) ||
                    previousStep.CompletedEffectId==null || previousStep.CompletedEffectId!=prior.CompletedEffectId)
                {reason="Waiting requires a completed static arrival at its saved destination.";return false;}
                var actor=FindActor(task.ResidentId);
                if(!actor || !actor.gameObject.activeInHierarchy ||
                    !ActionSpaceMatches(ActionPosition(actor.transform.position-Vector3.up)))
                {reason="The NPC is no longer in the saved waiting space.";return false;}
                Vector3 waitTarget;float arrivalRange;string targetFailure;
                var waitJob=new NpcActionJob {Kind=NpcActionKind.Wait,Target=NpcActionExecutor.CopyTarget(step.Target)};
                if(!ResolveActionTarget(waitJob,out waitTarget,out arrivalRange,out targetFailure))
                {reason="The saved waiting target is no longer reachable in this scene.";return false;}
                if(!NpcTaskPositionBinding.CanStartWaitAtResolvedArrival(
                    Vector3.Distance(actor.transform.position-Vector3.up,waitTarget),arrivalRange))
                {reason="The NPC has not reached the saved waiting point.";return false;}
            }
            var target=worldStore.ResolveNpcTaskTarget(task,task.CurrentStepIndex);
            if(target==null){reason="The earlier purchase produced no verified item for this handover.";return false;}
            var decision=new NpcActionDecision {DurationSeconds=step.DurationSeconds>0?(int)step.DurationSeconds:300,
                Option=new NpcActionOption {Kind=step.Kind,Target=target}};
            return TryAcceptConversationAction(task.ResidentId,NpcTaskContract.ActionId(task),decision,task,null,out reason);
        }

        private void DeliverConversationTaskFailure(string residentId,ResidentActor actor)
        {
            var pending=worldStore.PendingNpcTaskFailureReceipt(residentId);if(pending==null)return;
            var task=worldStore.ReadNpcTask(pending.TaskId);if(task==null)return;
            var date=new DaggerfallDateTime();date.FromSeconds((ulong)Math.Max(0d,Math.Floor(pending.CreatedAt)));
            string place="settlement "+population.SettlementId+(interior?", building "+interior.EntryDoor.buildingKey:" outdoors");
            NpcTaskFailureReceipt receipt;
            if(worldStore.TryDeliverNpcTaskFailureReceipt(task.Id,task.Revision,date.DateString()+", "+date.Year,place,out receipt))
            {
                string name=ActionRecipientName(actor);
                string message=(string.IsNullOrWhiteSpace(name)?"NPC":name)+": "+receipt.Message;
                DaggerfallUI.AddHUDText(message,4f);
                NativeNpcShopPurchaseProbe.ObserveTaskFailureHud(message);
            }
        }

        // Invoked by the existing actor plan update, never by a world-wide task scan.
        private void AdvanceConversationTask(string residentId,ResidentActor actor,double now,bool allowReadyDispatch)
        {
            DeliverConversationTaskFailure(residentId,actor);
            var task=worldStore.ActiveNpcTask(residentId);if(task==null)return;
            var bound=task.ActiveActionId==null?null:worldStore.ReadNpcAction(task.ActiveActionId);
            if(task.Phase==NpcTaskPhase.Running && NpcActionExecutor.IsTerminal(bound) || task.Phase==NpcTaskPhase.Ready && now>=task.DeadlineAt)
            {
                worldStore.ReconcileNpcTask(task.Id,task.Revision,now);
                task=worldStore.ActiveNpcTask(residentId);
                if(task==null){DeliverConversationTaskFailure(residentId,actor);return;}
            }
            if(task.Phase!=NpcTaskPhase.Ready || !allowReadyDispatch || GameManager.IsGamePaused || !actor ||
                !actor.gameObject.activeInHierarchy || actor.IsConversing)return;
            ActionMotion priorMotion;
            if(actionMotions.TryGetValue(residentId,out priorMotion) && priorMotion.Seat!=null &&
                !LeaveActionSeat(actor,priorMotion))return;
            string reason;
            if(!DispatchConversationTaskStep(task,out reason))
            {
                if(worldStore.FailNpcTask(task.Id,task.Revision,ActionNow(),reason))
                    DeliverConversationTaskFailure(residentId,actor);
            }
        }
    }
}
