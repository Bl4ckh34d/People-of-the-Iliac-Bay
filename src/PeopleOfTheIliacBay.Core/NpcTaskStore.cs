using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        private WorldState npcTaskIndexOwner;
        private string dispatchingNpcTaskActionId;
        private Dictionary<string,NpcTaskPlan> npcTasksById,npcTasksByResident;
        private Dictionary<string,List<string>> npcTaskFailureReceiptsByResident;
        private void EnsureNpcTaskIndex()
        {
            if(npcTaskIndexOwner==state) return;
            npcTasksById=new Dictionary<string,NpcTaskPlan>(StringComparer.Ordinal);
            npcTasksByResident=new Dictionary<string,NpcTaskPlan>(StringComparer.Ordinal);
            npcTaskFailureReceiptsByResident=new Dictionary<string,List<string>>(StringComparer.Ordinal);
            foreach(var task in state.NpcTasks)
            {
                npcTasksById.Add(task.Id,task);
                if(!NpcTaskContract.IsTerminal(task))npcTasksByResident.Add(task.ResidentId,task);
                if(task.Phase==NpcTaskPhase.Failed && task.FailureReceipt!=null && !task.FailureReceipt.Delivered)
                    AddPendingTaskReceipt(task.ResidentId,task.Id);
            }
            npcTaskIndexOwner=state;
        }
        private void AddPendingTaskReceipt(string residentId,string taskId)
        {
            List<string> ids;
            if(!npcTaskFailureReceiptsByResident.TryGetValue(residentId,out ids))
            {ids=new List<string>();npcTaskFailureReceiptsByResident.Add(residentId,ids);}
            if(!ids.Contains(taskId))ids.Add(taskId);
        }
        private void PutNpcTask(NpcTaskPlan task)
        {
            EnsureNpcTaskIndex();int index=state.NpcTasks.FindIndex(t=>t.Id==task.Id);
            if(index<0) state.NpcTasks.Add(task);else state.NpcTasks[index]=task;
            npcTasksById[task.Id]=task;
            if(NpcTaskContract.IsTerminal(task))
            {
                NpcTaskPlan active;
                if(npcTasksByResident.TryGetValue(task.ResidentId,out active) && active.Id==task.Id)
                    npcTasksByResident.Remove(task.ResidentId);
            }
            else npcTasksByResident[task.ResidentId]=task;
            List<string> ids;
            if(npcTaskFailureReceiptsByResident.TryGetValue(task.ResidentId,out ids))
            {ids.Remove(task.Id);if(ids.Count==0)npcTaskFailureReceiptsByResident.Remove(task.ResidentId);}
            if(task.Phase==NpcTaskPhase.Failed && task.FailureReceipt!=null && !task.FailureReceipt.Delivered)
                AddPendingTaskReceipt(task.ResidentId,task.Id);
        }
        public NpcTaskPlan ReadNpcTask(string id)
        {EnsureNpcTaskIndex();NpcTaskPlan task;return id!=null && npcTasksById.TryGetValue(id,out task)?NpcTaskContract.Copy(task):null;}
        public NpcTaskPlan ActiveNpcTask(string residentId)
        {EnsureNpcTaskIndex();NpcTaskPlan task;return residentId!=null && npcTasksByResident.TryGetValue(residentId,out task)?NpcTaskContract.Copy(task):null;}
        public List<NpcTaskPlan> PendingNpcTasks()
        {EnsureNpcTaskIndex();var tasks=new List<NpcTaskPlan>();foreach(var task in npcTasksByResident.Values)tasks.Add(NpcTaskContract.Copy(task));return tasks;}
        public NpcTaskFailureReceipt PendingNpcTaskFailureReceipt(string residentId)
        {
            EnsureNpcTaskIndex();List<string> ids;NpcTaskPlan task;
            if(residentId==null || !npcTaskFailureReceiptsByResident.TryGetValue(residentId,out ids) || ids.Count==0 ||
                !npcTasksById.TryGetValue(ids[0],out task))return null;
            return NpcTaskContract.Copy(task).FailureReceipt;
        }
        // Add memory observation and mark receipt delivered in one saved-state mutation.
        public bool TryDeliverNpcTaskFailureReceipt(string id,long expectedRevision,string date,string place,out NpcTaskFailureReceipt receipt)
        {
            receipt=null;var task=ReadNpcTask(id);
            if(task==null || task.Phase!=NpcTaskPhase.Failed || task.Revision!=expectedRevision ||
                expectedRevision==long.MaxValue || task.FailureReceipt==null || task.FailureReceipt.Delivered)return false;
            RememberObservation(task.ResidentId,task.FailureReceipt.CreatedAt,date,place,task.FailureReceipt.Observation);
            task.FailureReceipt.Delivered=true;task.Revision++;NpcTaskContract.Validate(task);PutNpcTask(task);
            receipt=NpcTaskContract.Copy(task).FailureReceipt;return true;
        }
        private NpcTaskFailureReceipt CreateTaskFailureReceipt(NpcTaskPlan task,double now)
        {
            return NpcTaskFeedback.Create(task,now,index=>{
                var purchase=ReadNpcAction(task.Id+":step:"+index);
                if(purchase==null || !NpcTaskContract.ConfirmsAcquiredItem(task.Steps[index],purchase))return false;
                ulong uid;var inventory=ReadNpcInventory(task.ResidentId);
                return inventory!=null && ulong.TryParse(purchase.AcquiredItemId,out uid) && inventory.Items.Exists(item=>item.Uid==uid);
            });
        }
        public NpcActionTarget ResolveNpcTaskTarget(NpcTaskPlan task,int index)
        {
            if(task==null || index<0 || index>=task.Steps.Count)return null;
            var step=task.Steps[index];var target=NpcActionExecutor.CopyTarget(step.Target);
            if(step.ItemSourceStep==0)return target;
            if(step.ItemSourceStep<1 || step.ItemSourceStep>index)return null;
            var source=ReadNpcAction(task.Id+":step:"+(step.ItemSourceStep-1));
            if(source==null || source.ResidentId!=task.ResidentId ||
                !NpcTaskContract.ConfirmsAcquiredItem(task.Steps[step.ItemSourceStep-1],source))return null;
            ulong uid;var inventory=ReadNpcInventory(task.ResidentId);
            if(inventory==null || !ulong.TryParse(source.AcquiredItemId,out uid) || !inventory.Items.Exists(item=>item.Uid==uid))return null;
            target.ItemId=source.AcquiredItemId;return target;
        }
        public bool TryAcceptNpcTask(NpcTaskPlan proposed,bool consent,out string reason)
        { return TryAcceptNpcTaskCore(proposed, consent, null, out reason); }

        internal bool TryAcceptNpcTaskClassified(NpcTaskPlan proposed, bool consent,
            NpcOperationAdmission expectedSource, out string reason)
        { return TryAcceptNpcTaskCore(proposed, consent, expectedSource, out reason); }

        private bool TryAcceptNpcTaskCore(NpcTaskPlan proposed, bool consent,
            NpcOperationAdmission expectedSource, out string reason)
        {
            reason="Invalid task proposal.";
            try {NpcTaskContract.Validate(proposed);}catch(ArgumentException){return false;}
            if (expectedSource == null)
            {
                // Existing unclassified rows may finish, but a new task needs a live source.
                reason = "The task has no classified admission source.";
                return false;
            }
            else
            {
                if (!NpcOperationAdmission.Same(proposed.Admission, expectedSource)) return false;
                if (proposed.Steps.Exists(step => step.Kind == NpcActionKind.CastSpell) &&
                    expectedSource.Kind != NpcOperationAdmissionKind.HealthCare) return false;
                if (expectedSource.Kind == NpcOperationAdmissionKind.Conversation)
                {
                    if (expectedSource.SourceId != proposed.ResidentId ||
                        proposed.Id != NpcOperationAdmission.ConversationTaskId(NpcFrameworkScopeId,
                            proposed.ResidentId, expectedSource.SourceRevision)) return false;
                }
                else if (expectedSource.Kind == NpcOperationAdmissionKind.HealthCare)
                {
                    if (proposed.Id != NpcHealthCare.MakeTaskId(new NpcHealthCareRecord {
                        Id = expectedSource.SourceId, Revision = expectedSource.SourceRevision })) return false;
                }
                else return false;
            }
            if(proposed.Phase!=NpcTaskPhase.Ready || proposed.Revision!=1 || proposed.CurrentStepIndex!=0 || proposed.UpdatedAt!=proposed.AcceptedAt) return false;
            var resident=ResolveResident(proposed.ResidentId);
            if(resident==null || !NpcActionContract.CanAccept(resident.IsAlive,resident.IsQuestProtected,true,consent,true,out reason)) return false;
            if(proposed.GoalId!=null)
            {
                var motivation=FindNpcMotivation(proposed.ResidentId);
                var goal=motivation==null?null:motivation.Goals.Find(g=>g!=null && g.Id==proposed.GoalId);
                if(goal==null || goal.Status!=NpcMotivationGoalStatus.Active ||
                    goal.KnowledgeState!=NpcMotivationKnowledgeState.Known ||
                    goal.GoalStartRevision!=proposed.GoalStartRevision ||
                    !NpcMotivationState.HasAcceptedGoalOrigin(motivation,proposed.GoalId,proposed.GoalStartRevision,proposed.AcceptedAt))
                {reason="The goal generation is no longer active or available.";return false;}
            }
            var previousAction=ActiveNpcAction(proposed.ResidentId);
            if(ActiveNpcTask(proposed.ResidentId)!=null || previousAction!=null && !NpcActionExecutor.IsTerminal(previousAction))
            {reason="The NPC is already carrying out another action or task.";return false;}
            if(state.NpcTasks.Exists(t=>t.Id==proposed.Id || t.ResidentId==proposed.ResidentId && t.ProposalId==proposed.ProposalId))
            {reason="This task proposal was already handled.";return false;}
            if(!TryMakeNpcOperationCapacity(NpcOperationalHistoryPressureTarget.Tasks,proposed.AcceptedAt,out reason))return false;
            PutNpcTask(NpcTaskContract.Copy(proposed));reason="accepted";return true;
        }
        // The adapter resolves current access, resources and consent before each call. No stored promise grants native authority.
        public bool TryDispatchNpcTaskStep(string id,long expectedRevision,double now,NpcActionJob job,
            bool adapterAvailable,bool consent,bool scheduleCompatible,out string reason)
        {
            reason="The task step is no longer dispatchable.";var task=ReadNpcTask(id);
            if(!MutableTask(task,expectedRevision,now) || task.Phase!=NpcTaskPhase.Ready || now>=task.DeadlineAt || job==null) return false;
            var step=task.Steps[task.CurrentStepIndex];
            if(job.Id!=NpcTaskContract.ActionId(task) || job.ProposalId!=job.Id || job.ResidentId!=task.ResidentId || job.Kind!=step.Kind ||
                !NpcTaskContract.TargetMatches(ResolveNpcTaskTarget(task,task.CurrentStepIndex),job.Target) || job.AcceptedAt!=now || job.DeadlineAt>task.DeadlineAt ||
                step.ExecutionBudgetSeconds>0 && job.DeadlineAt!=Math.Min(now+step.ExecutionBudgetSeconds,task.DeadlineAt) ||
                job.FinishAt!=(step.Kind==NpcActionKind.RestToHeal?0:step.DurationSeconds>0?now+step.DurationSeconds:0) ||
                job.RestDurationSeconds!=(step.Kind==NpcActionKind.RestToHeal?step.DurationSeconds:0) || job.RestStartedAt!=0 ||
                (step.MagicHealing==null ? job.MagicHealing!=null : !NpcMagicHealingSpec.Same(step.MagicHealing,job.MagicHealing)) ||
                job.ServiceCode!=(step.Kind==NpcActionKind.Purchase?NpcShopCommerce.ServiceCodeForPurchaseTarget(step.Target):null)) return false;
            if (job.Admission != null) return false;
            var admitted = NpcActionExecutor.Copy(job);
            admitted.Admission = NpcOperationAdmission.FromTaskStep(task);
            bool accepted;
            dispatchingNpcTaskActionId=admitted.Id;
            try {accepted=TryAcceptNpcActionClassified(admitted,adapterAvailable,consent,scheduleCompatible,
                admitted.Admission,out reason);}
            finally {dispatchingNpcTaskActionId=null;}
            if(!accepted)return false;
            task.ActiveActionId=job.Id;task.Phase=NpcTaskPhase.Running;task.UpdatedAt=now;task.Revision++;PutNpcTask(task);return true;
        }
        private static bool MutableTask(NpcTaskPlan task,long revision,double now)
        {return task!=null && !NpcTaskContract.IsTerminal(task) && task.Revision==revision && revision<long.MaxValue && Finite(now) && now>=task.UpdatedAt;}
        public bool ReconcileNpcTask(string id,long expectedRevision,double now)
        {
            var task=ReadNpcTask(id);if(!MutableTask(task,expectedRevision,now))return false;
            var action=ReadNpcAction(task.ActiveActionId);
            if(action!=null && NpcActionExecutor.IsTerminal(action))
            {
                if(now<action.UpdatedAt)return false;
                if(action.Phase==NpcActionPhase.Completed)
                {
                    task.Steps[task.CurrentStepIndex].CompletedEffectId=action.CompletedEffectId;task.CurrentStepIndex++;task.ActiveActionId=null;
                    task.Phase=task.CurrentStepIndex==task.Steps.Count?NpcTaskPhase.Completed:NpcTaskPhase.Ready;
                }
                else {task.Phase=action.Phase==NpcActionPhase.Cancelled?NpcTaskPhase.Cancelled:NpcTaskPhase.Failed;task.FailureReason=action.FailureReason;}
            }
            else if(now>=task.DeadlineAt)
            {
                if(action!=null && !ObserveNpcAction(action.Id,action.Revision,now,NpcActionObservation.Interrupted("The task deadline expired.")))return false;
                task.Phase=NpcTaskPhase.Failed;task.FailureReason="The task deadline expired.";
            }
            else return false;
            task.UpdatedAt=now;task.Revision++;
            if(task.Phase==NpcTaskPhase.Failed)task.FailureReceipt=CreateTaskFailureReceipt(task,now);
            NpcTaskContract.Validate(task);PutNpcTask(task);return true;
        }

        // Runs from the existing entered-building handoff path while the player
        // remains outside. It records elapsed game time from the physical door
        // crossing without asserting an unobserved interior position.
        public bool TryCompleteOffscreenBuildingWait(string id,long expectedRevision,double now)
        {
            var task=ReadNpcTask(id);
            if(!MutableTask(task,expectedRevision,now) || task.Phase!=NpcTaskPhase.Ready ||
                task.CurrentStepIndex<1 || task.CurrentStepIndex>=task.Steps.Count || now>=task.DeadlineAt)return false;
            var previous=task.Steps[task.CurrentStepIndex-1];var wait=task.Steps[task.CurrentStepIndex];
            var enter=ReadNpcAction(task.Id+":step:"+(task.CurrentStepIndex-1));
            if(!NpcTaskPositionBinding.HasCompletedOffscreenBuildingEntryReceipt(previous,enter,wait))return false;
            double started=enter.BuildingTransitionReceipt.ObservedGameTime,finished=started+wait.DurationSeconds;
            if(double.IsNaN(finished) || double.IsInfinity(finished) || finished>=task.DeadlineAt || now<finished ||
                ReadNpcAction(NpcTaskContract.ActionId(task))!=null)return false;
            var accepted=new NpcActionJob {Id=NpcTaskContract.ActionId(task),ProposalId=NpcTaskContract.ActionId(task),
                ResidentId=task.ResidentId,Kind=NpcActionKind.Wait,Target=NpcActionExecutor.CopyTarget(wait.Target),
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=started,UpdatedAt=started,
                DeadlineAt=task.DeadlineAt,FinishAt=finished};
            NpcActionJob executing,completed;
            if(!NpcActionExecutor.Advance(accepted,accepted.Revision,started,NpcActionObservation.Arrival(),out executing) ||
                !NpcActionExecutor.Advance(executing,executing.Revision,now,
                    NpcActionObservation.Success(accepted.Id+":offscreen"),out completed))return false;
            state.NpcActions.Add(completed);IndexNpcAction(completed);
            task.Steps[task.CurrentStepIndex].CompletedEffectId=completed.CompletedEffectId;
            task.CurrentStepIndex++;task.ActiveActionId=null;task.Phase=task.CurrentStepIndex==task.Steps.Count?
                NpcTaskPhase.Completed:NpcTaskPhase.Ready;task.UpdatedAt=now;task.Revision++;
            NpcTaskContract.Validate(task);PutNpcTask(task);return true;
        }
        public bool CancelNpcTask(string id,long expectedRevision,double now,string reason)
        {
            var task=ReadNpcTask(id);if(!MutableTask(task,expectedRevision,now) || string.IsNullOrWhiteSpace(reason) || reason.Length>1000)return false;
            var action=ReadNpcAction(task.ActiveActionId);
            // A completed effect must be reconciled, never erased by cancellation.
            if(action!=null && (action.Phase==NpcActionPhase.Completed || now<action.UpdatedAt))return false;
            if(action!=null && !NpcActionExecutor.IsTerminal(action) && !ObserveNpcAction(action.Id,action.Revision,now,NpcActionObservation.Cancelled(reason)))return false;
            task.Phase=NpcTaskPhase.Cancelled;task.FailureReason=reason;task.UpdatedAt=now;task.Revision++;PutNpcTask(task);return true;
        }
        public bool FailNpcTask(string id,long expectedRevision,double now,string reason)
        {
            var task=ReadNpcTask(id);
            if(!MutableTask(task,expectedRevision,now) || task.Phase!=NpcTaskPhase.Ready ||
                string.IsNullOrWhiteSpace(reason) || reason.Length>1000)return false;
            task.Phase=NpcTaskPhase.Failed;task.FailureReason=reason;task.UpdatedAt=now;task.Revision++;
            task.FailureReceipt=CreateTaskFailureReceipt(task,now);NpcTaskContract.Validate(task);PutNpcTask(task);return true;
        }
        private static List<NpcTaskPlan> CopyNpcTasks(WorldState source,WorldState validated)
        {
            Require(source.NpcTasks!=null && source.NpcTasks.Count<=1000,"Invalid NPC task ledger.");
            var result=new List<NpcTaskPlan>();var ids=new HashSet<string>(StringComparer.Ordinal);
            var proposals=new HashSet<string>(StringComparer.Ordinal);var residents=new HashSet<string>(StringComparer.Ordinal);
            var knownOwners=new HashSet<string>(StringComparer.Ordinal);
            var actions=new Dictionary<string,NpcActionJob>(StringComparer.Ordinal);foreach(var action in validated.NpcActions)actions.Add(action.Id,action);
            foreach(var sourceTask in source.NpcTasks)
            {
                Require(sourceTask!=null && sourceTask.Steps!=null &&
                    (source.Version>=59 || !sourceTask.Steps.Exists(step=>step!=null && step.MagicHealing!=null)),
                    "Legacy task has future magic-healing metadata.");
                var task=NpcTaskContract.Copy(sourceTask);
                if(source.Version<46){task.GoalId=null;task.GoalStartRevision=0;}
                NpcTaskContract.Validate(task);
                Require(CachedKnownNpcIdentity(validated,task.ResidentId,knownOwners) && ids.Add(task.Id) && proposals.Add(task.ResidentId+"\n"+task.ProposalId),"Unknown or duplicate NPC task.");
                if(task.GoalId!=null)
                {
                    var motivation=validated.NpcMotivations.Find(m=>m.OwnerPersonId==task.ResidentId);
                    Require(NpcMotivationState.HasAcceptedGoalOrigin(motivation,task.GoalId,task.GoalStartRevision,task.AcceptedAt),
                        "NPC task has no accepted goal generation.");
                }
                if(!NpcTaskContract.IsTerminal(task))Require(residents.Add(task.ResidentId),"Multiple tasks own one NPC.");
                for(int i=0;i<task.Steps.Count;i++)
                {
                    var step=task.Steps[i];NpcActionJob action;bool exists=actions.TryGetValue(task.Id+":step:"+i,out action);
                    if(i<task.CurrentStepIndex || task.ActiveActionId==task.Id+":step:"+i)
                    {
                        var target=NpcActionExecutor.CopyTarget(step.Target);
                        if(step.ItemSourceStep>0)
                        {
                            NpcActionJob sourceAction;
                            var sourceStep=task.Steps[step.ItemSourceStep-1];
                            Require(actions.TryGetValue(task.Id+":step:"+(step.ItemSourceStep-1),out sourceAction) &&
                                NpcTaskContract.ConfirmsAcquiredItem(sourceStep,sourceAction),"Task has no confirmed acquired item.");
                            target.ItemId=sourceAction.AcquiredItemId;
                        }
                        Require(exists && action.ResidentId==task.ResidentId && action.ProposalId==action.Id && action.Kind==step.Kind &&
                            NpcTaskContract.TargetMatches(action.Target,target) && action.AcceptedAt>=task.AcceptedAt && action.DeadlineAt<=task.DeadlineAt &&
                            (step.ExecutionBudgetSeconds==0 || action.DeadlineAt==Math.Min(action.AcceptedAt+step.ExecutionBudgetSeconds,task.DeadlineAt)) &&
                            action.ServiceCode==(step.Kind==NpcActionKind.Purchase?NpcShopCommerce.ServiceCodeForPurchaseTarget(step.Target):null) &&
                            action.RestDurationSeconds==(step.Kind==NpcActionKind.RestToHeal?step.DurationSeconds:0) &&
                            (step.MagicHealing==null ? action.MagicHealing==null : NpcMagicHealingSpec.Same(step.MagicHealing,action.MagicHealing)) &&
                            action.FinishAt==(step.Kind==NpcActionKind.RestToHeal ?
                                (action.FinishAt==0?0:action.RestStartedAt+step.DurationSeconds) :
                                step.DurationSeconds>0?action.AcceptedAt+step.DurationSeconds:0),"Task action binding changed.");
                        if(i<task.CurrentStepIndex)Require(action.Phase==NpcActionPhase.Completed && action.CompletedEffectId==step.CompletedEffectId,"Task step has no confirmed effect.");
                        else if(NpcTaskContract.IsTerminal(task))Require(action.Phase==NpcActionPhase.Failed || action.Phase==NpcActionPhase.Cancelled,"Terminal task still owns an unfinished action.");
                    }
                    else Require(!exists,"Task has an undispatched action.");
                }
                result.Add(NpcTaskContract.Copy(task));
            }
            return result;
        }
    }
}
