using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcTaskPhase { Ready, Running, Completed, Failed, Cancelled }

    [Serializable] public sealed class NpcTaskStep
    {
        public NpcActionKind Kind;
        public NpcActionTarget Target;
        public double DurationSeconds;
        // Optional bound for an untimed movement step. Zero retains the existing
        // 300-second adapter default; this is not a dwell or service duration.
        public int ExecutionBudgetSeconds;
        public string CompletedEffectId;
        // One-based earlier acquired-item step; zero means an ordinary exact item target.
        public int ItemSourceStep;
        public NpcMagicHealingSpec MagicHealing;
    }

    [Serializable] public sealed class NpcTaskPlan
    {
        public string Id, ProposalId, ResidentId;
        public NpcOperationAdmission Admission;
        // Optional immutable admission provenance. Zero means an unbound legacy or dialogue task.
        public string GoalId;
        public long GoalStartRevision;
        public List<NpcTaskStep> Steps = new List<NpcTaskStep>();
        public int CurrentStepIndex;
        public string ActiveActionId, FailureReason;
        // Optional for schema-29 saves written before durable failure feedback.
        public NpcTaskFailureReceipt FailureReceipt;
        public NpcTaskPhase Phase;
        public long Revision = 1;
        public double AcceptedAt, UpdatedAt, DeadlineAt;
    }

    [Serializable] public sealed class NpcTaskFailureReceipt
    {
        public string Id, TaskId, Message, Observation;
        public double CreatedAt;
        public bool Delivered;
    }

    public static class NpcTaskFeedback
    {
        private static string StepName(NpcActionKind kind)
        {
            switch(kind)
            {
                case NpcActionKind.Wait:return "Wait";
                case NpcActionKind.MoveTo:return "Travel";
                case NpcActionKind.Follow:return "Follow";
                case NpcActionKind.TakeSeat:return "Sit";
                case NpcActionKind.Interact:return "Approach";
                case NpcActionKind.ReportMurder:return "Report murder";
                case NpcActionKind.Transfer:return "Handover";
                case NpcActionKind.Purchase:return "Shop purchase";
                case NpcActionKind.UseHealingItem:return "Healing item";
                case NpcActionKind.RestToHeal:return "Rest";
                case NpcActionKind.CastSpell:return "Healing spell";
                case NpcActionKind.Lead:return "Lead";
                case NpcActionKind.Enter:return "Enter";
                case NpcActionKind.Exit:return "Exit";
                default:return "Action";
            }
        }
        private static string Clip(string value,int maximum)
        {
            if(string.IsNullOrWhiteSpace(value))return "the step could not continue";
            value=value.Trim();return value.Length<=maximum?value:value.Substring(0,maximum-1)+"…";
        }
        private static string Sentence(string value)
        {
            char last=value[value.Length-1];return last=='.' || last=='!' || last=='?'?value:value+".";
        }
        private static string CompletedStep(NpcTaskStep step,bool acquiredItemRemains)
        {
            if(step.Kind==NpcActionKind.Purchase)
                return "The shop purchase succeeded for "+step.Target.QuotedGold+" gold from the NPC's wallet; "+
                    (acquiredItemRemains?"the purchased item remains with the NPC.":"the acquired item could not be verified.");
            return "The "+StepName(step.Kind).ToLowerInvariant()+" step completed.";
        }
        public static NpcTaskFailureReceipt Create(NpcTaskPlan task,double now,Func<int,bool> acquiredItemRemains)
        {
            if(task==null || task.Phase!=NpcTaskPhase.Failed || string.IsNullOrWhiteSpace(task.FailureReason) ||
                task.CurrentStepIndex<0 || task.CurrentStepIndex>=task.Steps.Count)return null;
            var lines=new List<string>();
            lines.Add(task.CurrentStepIndex==0?"No steps were completed.":"Completed "+task.CurrentStepIndex+" of "+task.Steps.Count+" agreed task steps.");
            for(int i=0;i<task.CurrentStepIndex;i++)lines.Add(CompletedStep(task.Steps[i],
                task.Steps[i].Kind==NpcActionKind.Purchase && acquiredItemRemains!=null && acquiredItemRemains(i)));
            lines.Add("The "+StepName(task.Steps[task.CurrentStepIndex].Kind).ToLowerInvariant()+
                (task.ActiveActionId==null?" step could not start: ":" step did not complete: ")+Sentence(Clip(task.FailureReason,220)));
            if(task.CurrentStepIndex+1<task.Steps.Count)lines.Add("Later steps were not attempted.");
            string details=string.Join(" ",lines.ToArray());
            return new NpcTaskFailureReceipt {Id=task.Id+":failure",TaskId=task.Id,CreatedAt=now,
                Message=Clip("The agreed task did not finish. "+details,540),
                Observation=Clip("Observed task failure. "+details,580)};
        }
    }

    public static class NpcTaskContract
    {
        public const int MaximumSteps = 8;
        public static bool IsTerminal(NpcTaskPlan task) { return task!=null && task.Phase>=NpcTaskPhase.Completed; }
        public static string ActionId(NpcTaskPlan task) { return task.Id+":step:"+task.CurrentStepIndex; }
        public static bool Supported(NpcActionKind kind)
        { return kind==NpcActionKind.Wait || kind==NpcActionKind.MoveTo || kind==NpcActionKind.Follow || kind==NpcActionKind.TakeSeat || kind==NpcActionKind.Interact || kind==NpcActionKind.ReportMurder || kind==NpcActionKind.Transfer || kind==NpcActionKind.Purchase || kind==NpcActionKind.UseHealingItem || kind==NpcActionKind.RestToHeal || kind==NpcActionKind.CastSpell || kind==NpcActionKind.Lead || kind==NpcActionKind.Enter || kind==NpcActionKind.Exit; }
        public static bool SupportsExecutionBudget(NpcActionKind kind)
        { return kind==NpcActionKind.MoveTo || kind==NpcActionKind.Enter || kind==NpcActionKind.Exit ||
            kind==NpcActionKind.Lead || kind==NpcActionKind.Interact || kind==NpcActionKind.ReportMurder; }
        public static bool CanSourceAcquiredItem(NpcTaskStep step)
        {
            return step!=null && (step.Kind==NpcActionKind.Purchase || step.Kind==NpcActionKind.Transfer && step.Target!=null &&
                step.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc);
        }
        public static bool ConfirmsAcquiredItem(NpcTaskStep step,NpcActionJob action)
        {
            ulong uid;
            if(!CanSourceAcquiredItem(step) || action==null || action.Phase!=NpcActionPhase.Completed ||
                string.IsNullOrWhiteSpace(action.AcquiredItemId) || !ulong.TryParse(action.AcquiredItemId,out uid) || uid==0)return false;
            if(step.Kind==NpcActionKind.Purchase)return action.Kind==NpcActionKind.Purchase && action.ServiceCode==NpcShopCommerce.ServiceCode;
            return action.Kind==NpcActionKind.Transfer && action.ServiceCode==null &&
                action.Target!=null && action.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc &&
                action.Target.ItemId==action.AcquiredItemId && TargetMatches(action.Target,step.Target);
        }
        private static bool Number(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Id(string value,int maximum=240) { return !string.IsNullOrWhiteSpace(value) && value.Length<=maximum; }
        public static void Validate(NpcTaskPlan task)
        {
            if(task==null || !Id(task.Id,190) || !Id(task.ProposalId) || !Id(task.ResidentId) || task.Revision<1 ||
                (task.GoalId == null ? task.GoalStartRevision != 0 : !Id(task.GoalId,128) || task.GoalStartRevision < 1) ||
                task.Steps==null || task.Steps.Count<1 || task.Steps.Count>MaximumSteps || (int)task.Phase<0 || (int)task.Phase>4 ||
                task.CurrentStepIndex<0 || task.CurrentStepIndex>task.Steps.Count || !Number(task.AcceptedAt) || task.AcceptedAt<0 ||
                !Number(task.UpdatedAt) || task.UpdatedAt<task.AcceptedAt || !Number(task.DeadlineAt) ||
                task.DeadlineAt<=task.AcceptedAt || task.DeadlineAt-task.AcceptedAt>86400)
                throw new ArgumentException("Invalid NPC task.");
            if(task.Admission!=null) NpcOperationAdmission.CopyValidated(task.Admission);
            if((task.Phase==NpcTaskPhase.Completed)!=(task.CurrentStepIndex==task.Steps.Count) ||
                task.Phase==NpcTaskPhase.Running && task.ActiveActionId!=ActionId(task) ||
                (task.Phase==NpcTaskPhase.Ready || task.Phase==NpcTaskPhase.Completed) && task.ActiveActionId!=null ||
                task.ActiveActionId!=null && task.ActiveActionId!=ActionId(task) ||
                (task.Phase==NpcTaskPhase.Failed || task.Phase==NpcTaskPhase.Cancelled ? !Id(task.FailureReason,1000) : task.FailureReason!=null))
                throw new ArgumentException("Invalid NPC task progress.");
            if(task.FailureReceipt!=null && (task.Phase!=NpcTaskPhase.Failed || task.FailureReceipt.Id!=task.Id+":failure" ||
                task.FailureReceipt.TaskId!=task.Id ||
                !Number(task.FailureReceipt.CreatedAt) || task.FailureReceipt.CreatedAt<task.AcceptedAt ||
                task.FailureReceipt.CreatedAt>task.UpdatedAt || !Id(task.FailureReceipt.Message,600) ||
                !Id(task.FailureReceipt.Observation,600)))
                throw new ArgumentException("Invalid NPC task failure receipt.");
            for(int i=0;i<task.Steps.Count;i++)
            {
                var step=task.Steps[i];
                if(step==null || !Supported(step.Kind) || !Number(step.DurationSeconds) || step.DurationSeconds<0 ||
                    step.ExecutionBudgetSeconds!=0 && (!SupportsExecutionBudget(step.Kind) ||
                        step.ExecutionBudgetSeconds<10 || step.ExecutionBudgetSeconds>900) ||
                    step.DurationSeconds>=task.DeadlineAt-task.AcceptedAt ||
                    ((step.Kind==NpcActionKind.Wait || step.Kind==NpcActionKind.Follow || step.Kind==NpcActionKind.TakeSeat || step.Kind==NpcActionKind.RestToHeal) ? step.DurationSeconds<=0 : step.DurationSeconds!=0) ||
                    (i<task.CurrentStepIndex ? !Id(step.CompletedEffectId) : step.CompletedEffectId!=null))
                    throw new ArgumentException("Invalid NPC task step.");
                // Reuse the authoritative typed action checks, including transfer quantity and direction.
                if(step.ItemSourceStep<0 || step.ItemSourceStep>i)
                    throw new ArgumentException("Invalid task item dependency.");
                if (step.Kind == NpcActionKind.CastSpell ? step.MagicHealing == null : step.MagicHealing != null)
                    throw new ArgumentException("Magic healing metadata requires one cast step.");
                if (step.MagicHealing != null) NpcMagicHealingSpec.CopyValidated(step.MagicHealing);
                if(step.ItemSourceStep>0)
                {
                    var sourceStep=task.Steps[step.ItemSourceStep-1];
                    bool supportedDependency=step.Kind==NpcActionKind.Transfer && step.Target!=null &&
                        step.Target.TransferDirection==NpcActionTransferDirection.NpcToCounterparty ||
                        step.Kind==NpcActionKind.UseHealingItem && step.Target!=null && step.Target.Quantity==1 &&
                        sourceStep!=null && sourceStep.Kind==NpcActionKind.Purchase;
                    if(!supportedDependency || step.Target==null || !CanSourceAcquiredItem(sourceStep) ||
                        sourceStep.Target==null || step.Target.ItemId!=sourceStep.Target.ItemId ||
                        step.Target.Quantity!=sourceStep.Target.Quantity)
                        throw new ArgumentException("Invalid task item dependency.");
                }
                if(step.Kind==NpcActionKind.Wait && i>0 &&
                    !NpcTaskPositionBinding.IsBoundWaitAfterStaticArrival(task.Steps[i-1],step))
                    throw new ArgumentException("A later wait must bind to the preceding static arrival destination.");
                for(int previous=0;previous<i;previous++)
                    if(step.ItemSourceStep>0 && task.Steps[previous].ItemSourceStep==step.ItemSourceStep)
                        throw new ArgumentException("An acquired result can only be handed over once.");
                NpcActionExecutor.Validate(new NpcActionJob { Id=task.Id+":step:"+i, ProposalId=task.Id+":step:"+i,
                    ResidentId=task.ResidentId,Kind=step.Kind,Target=step.Target,Phase=NpcActionPhase.Accepted,Revision=1,
                    ServiceCode=step.Kind==NpcActionKind.Purchase?NpcShopCommerce.ServiceCodeForPurchaseTarget(step.Target):null,
                    ServicePrice=step.Kind==NpcActionKind.Purchase && step.Target!=null?step.Target.QuotedGold:0,
                    RestDurationSeconds=step.Kind==NpcActionKind.RestToHeal?step.DurationSeconds:0,
                    MagicHealing=step.MagicHealing,
                    AcceptedAt=task.AcceptedAt,UpdatedAt=task.AcceptedAt,DeadlineAt=task.DeadlineAt });
            }
        }
        public static NpcTaskPlan Copy(NpcTaskPlan value)
        {
            var copy=new NpcTaskPlan {Id=value.Id,ProposalId=value.ProposalId,ResidentId=value.ResidentId,
                Admission=value.Admission==null?null:NpcOperationAdmission.CopyValidated(value.Admission),
                GoalId=value.GoalId,GoalStartRevision=value.GoalStartRevision,
                CurrentStepIndex=value.CurrentStepIndex,ActiveActionId=value.ActiveActionId,FailureReason=value.FailureReason,
                Phase=value.Phase,Revision=value.Revision,AcceptedAt=value.AcceptedAt,UpdatedAt=value.UpdatedAt,DeadlineAt=value.DeadlineAt};
            if(value.FailureReceipt!=null)copy.FailureReceipt=new NpcTaskFailureReceipt {Id=value.FailureReceipt.Id,TaskId=value.FailureReceipt.TaskId,
                Message=value.FailureReceipt.Message,Observation=value.FailureReceipt.Observation,CreatedAt=value.FailureReceipt.CreatedAt,
                Delivered=value.FailureReceipt.Delivered};
            foreach(var step in value.Steps) copy.Steps.Add(new NpcTaskStep {Kind=step.Kind,Target=NpcActionExecutor.CopyTarget(step.Target),
                DurationSeconds=step.DurationSeconds,ExecutionBudgetSeconds=step.ExecutionBudgetSeconds,
                CompletedEffectId=step.CompletedEffectId,ItemSourceStep=step.ItemSourceStep,
                MagicHealing=step.MagicHealing==null?null:NpcMagicHealingSpec.CopyValidated(step.MagicHealing)});
            return copy;
        }
        public static bool TargetMatches(NpcActionTarget a,NpcActionTarget b)
        {
            return a!=null && b!=null && a.Kind==b.Kind && a.PersonId==b.PersonId && a.ContainerId==b.ContainerId &&
                a.ItemId==b.ItemId && a.WorldSpaceId==b.WorldSpaceId && a.DoorId==b.DoorId &&
                a.DestinationSpaceId==b.DestinationSpaceId && a.Quantity==b.Quantity && a.QuotedGold==b.QuotedGold &&
                a.TransferDirection==b.TransferDirection && a.SettlementId==b.SettlementId && a.BuildingKey==b.BuildingKey &&
                a.X==b.X && a.Y==b.Y && a.Z==b.Z;
        }
    }
}
