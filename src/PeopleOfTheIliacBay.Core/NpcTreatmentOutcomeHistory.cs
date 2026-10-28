using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Read-only, bounded fact copied from one verified native treatment effect.</summary>
    [Serializable]
    public sealed class NpcTreatmentOutcome
    {
        public long Revision;
        public string ActionId,EffectId,ReceiptId,TaskId;
        public int TaskStepIndex;
        public NpcTreatmentKind Kind;
        public double ObservedGameTime,RestStartedAt,RestDurationSeconds;
        public int HealthBefore,HealthAfter;
        public ulong SourceItemUid;
        public string SpellId,SpellEffectId;
        public int MagickaCost,MagickaBefore,MagickaAfter;
        // Present only when the same task proves an exact acquired-item source.
        public string PurchaseActionId,PurchaseEffectId,SellerPersonId;
        public int PaidGold;
    }

    /// <summary>One person's latest 32 exact treatments; base records lost detail.</summary>
    [Serializable]
    public sealed class NpcTreatmentOutcomeHistory
    {
        public string PersonId;
        public long Revision,OutcomeBaseRevision;
        public double DroppedEarliestObservedGameTime;
        public List<NpcTreatmentOutcome> Outcomes=new List<NpcTreatmentOutcome>();
    }

    public sealed class NpcTreatmentOutcomeProjectionResult
    {
        public int Examined,Projected,Future,AlreadyProjected,Ineligible,Rejected;
    }

    public sealed partial class WorldStateStore
    {
        public const int MaximumNpcTreatmentOutcomesPerPerson=32;
        private WorldState treatmentProjectionCursorState;
        private int treatmentProjectionCursor;

        /// <summary>Bounded runtime scan; never projects evidence from a future native time.</summary>
        public NpcTreatmentOutcomeProjectionResult ProjectPendingNpcTreatmentOutcomes(double now,int maximumExamined)
        {
            if(double.IsNaN(now) || double.IsInfinity(now) || now<0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if(maximumExamined<1 || maximumExamined>128)
                throw new ArgumentOutOfRangeException(nameof(maximumExamined));
            var result=new NpcTreatmentOutcomeProjectionResult();
            if(!ReferenceEquals(treatmentProjectionCursorState,state))
            {
                treatmentProjectionCursorState=state;
                treatmentProjectionCursor=0;
            }
            int remaining=state.NpcActions.Count;
            while(remaining>0 && result.Examined<maximumExamined)
            {
                if(treatmentProjectionCursor>=state.NpcActions.Count)treatmentProjectionCursor=0;
                NpcActionJob action=state.NpcActions[treatmentProjectionCursor++];
                remaining--;
                result.Examined++;
                if(action==null || !IsTreatmentAction(action.Kind) || action.Phase!=NpcActionPhase.Completed ||
                    action.TreatmentReceipt==null)
                {result.Ineligible++;continue;}
                if(action.TreatmentOutcomeRevision>0)
                {result.AlreadyProjected++;continue;}
                if(action.TreatmentReceipt.ObservedGameTime>now || action.UpdatedAt>now)
                {result.Future++;continue;}
                if(TryProjectNpcTreatmentOutcome(action.Id,action.Revision))result.Projected++;
                else result.Rejected++;
            }
            return result;
        }

        /// <summary>Project a completed native receipt once before its action group can be pruned.</summary>
        public bool TryProjectNpcTreatmentOutcome(string actionId,long expectedActionRevision)
        {
            if(string.IsNullOrWhiteSpace(actionId) || expectedActionRevision<1 ||
                state.NpcTreatmentOutcomeHistories==null)return false;
            int index=state.NpcActions.FindIndex(value=>value!=null && value.Id==actionId);
            if(index<0)return false;
            NpcActionJob source=state.NpcActions[index];
            if(source.Revision!=expectedActionRevision)return false;
            NpcTreatmentOutcomeHistory previous=state.NpcTreatmentOutcomeHistories.Find(value=>value.PersonId==source.ResidentId);
            if(source.TreatmentOutcomeRevision>0)
            {
                if(previous==null || source.TreatmentOutcomeRevision>previous.Revision)return false;
                NpcTreatmentOutcome retained=previous.Outcomes.Find(value=>value.Revision==source.TreatmentOutcomeRevision);
                return retained==null?source.TreatmentOutcomeRevision<=previous.OutcomeBaseRevision:
                    NpcTreatmentOutcomeMatchesAction(retained,source,state);
            }
            if(source.TreatmentOutcomeRevision<0 || source.Phase!=NpcActionPhase.Completed ||
                source.TreatmentReceipt==null || !IsTreatmentAction(source.Kind) ||
                source.Revision==long.MaxValue)return false;
            try {NpcTreatmentReceipt.CopyValidated(source.TreatmentReceipt,source);}
            catch(ArgumentException) {return false;}
            if(previous!=null)
            {
                NpcTreatmentOutcome retained=previous.Outcomes.Find(value=>value.ActionId==source.Id);
                if(retained!=null)
                {
                    if(!NpcTreatmentOutcomeMatchesAction(retained,source,state))return false;
                    NpcActionJob repaired=NpcActionExecutor.Copy(source);
                    repaired.TreatmentOutcomeRevision=retained.Revision;
                    try {NpcActionExecutor.Validate(repaired);} catch(ArgumentException) {return false;}
                    state.NpcActions[index]=repaired;
                    IndexNpcAction(repaired);
                    return true;
                }
            }
            if(previous==null && state.NpcTreatmentOutcomeHistories.Count>=100000)return false;
            if(previous!=null && previous.Revision==long.MaxValue)return false;
            long nextRevision=previous==null?1:previous.Revision+1;
            NpcTreatmentOutcome outcome=BuildNpcTreatmentOutcome(source,nextRevision);
            if(outcome==null)return false;
            NpcTreatmentOutcomeHistory next=previous==null?
                new NpcTreatmentOutcomeHistory {PersonId=source.ResidentId}:
                CopyNpcTreatmentOutcomeHistory(previous);
            next.Revision=nextRevision;
            next.Outcomes.Add(outcome);
            if(next.Outcomes.Count>MaximumNpcTreatmentOutcomesPerPerson)
            {
                next.DroppedEarliestObservedGameTime=next.OutcomeBaseRevision==0?
                    next.Outcomes[0].ObservedGameTime:
                    Math.Min(next.DroppedEarliestObservedGameTime,next.Outcomes[0].ObservedGameTime);
                next.Outcomes.RemoveAt(0);
                next.OutcomeBaseRevision++;
            }
            NpcActionJob marked=NpcActionExecutor.Copy(source);
            marked.TreatmentOutcomeRevision=nextRevision;
            try {NpcActionExecutor.Validate(marked);ValidateNpcTreatmentOutcomeHistory(next);}
            catch(ArgumentException) {return false;}
            if(previous==null)state.NpcTreatmentOutcomeHistories.Add(next);
            else state.NpcTreatmentOutcomeHistories[state.NpcTreatmentOutcomeHistories.IndexOf(previous)]=next;
            state.NpcActions[index]=marked;
            IndexNpcAction(marked);
            return true;
        }

        private NpcTreatmentOutcome BuildNpcTreatmentOutcome(NpcActionJob source,long revision)
        {
            NpcTreatmentReceipt receipt=source.TreatmentReceipt;
            var outcome=new NpcTreatmentOutcome {Revision=revision,ActionId=source.Id,
                EffectId=source.CompletedEffectId,ReceiptId=receipt.TransactionId,Kind=receipt.Kind,
                ObservedGameTime=receipt.ObservedGameTime,RestStartedAt=receipt.RestStartedAt,
                RestDurationSeconds=receipt.RestDurationSeconds,HealthBefore=receipt.HealthBefore,
                HealthAfter=receipt.HealthAfter,SourceItemUid=receipt.SourceItemUid,
                SpellId=receipt.SpellId,SpellEffectId=receipt.EffectId,
                MagickaCost=receipt.MagickaCost,MagickaBefore=receipt.MagickaBefore,
                MagickaAfter=receipt.MagickaAfter,
                TaskStepIndex=-1};
            foreach(NpcTaskPlan taskRow in state.NpcTasks)
            {
                if(taskRow==null || taskRow.ResidentId!=source.ResidentId || taskRow.Steps==null)continue;
                for(int step=0;step<taskRow.Steps.Count;step++)
                {
                    if(source.Id!=taskRow.Id+":step:"+step.ToString(CultureInfo.InvariantCulture))continue;
                    NpcTaskPlan task=ReadNpcTask(taskRow.Id);
                    // The native effect remains reportable even if an optional
                    // task provenance edge cannot be established.
                    if(!TreatmentActionMatchesTask(source,receipt,task,step))return outcome;
                    outcome.TaskId=task.Id;
                    outcome.TaskStepIndex=step;
                    NpcActionJob purchase=source.Kind==NpcActionKind.UseHealingItem?
                        LinkedPurchaseForTreatment(source,receipt,task,step):null;
                    if(purchase!=null)
                    {
                        outcome.PurchaseActionId=purchase.Id;
                        outcome.PurchaseEffectId=purchase.CompletedEffectId;
                        outcome.SellerPersonId=purchase.Target.PersonId;
                        outcome.PaidGold=purchase.ServicePrice;
                    }
                    return outcome;
                }
            }
            return outcome;
        }

        private static NpcTreatmentOutcomeHistory CopyNpcTreatmentOutcomeHistory(NpcTreatmentOutcomeHistory source)
        {
            var copy=new NpcTreatmentOutcomeHistory {PersonId=source.PersonId,Revision=source.Revision,
                OutcomeBaseRevision=source.OutcomeBaseRevision,
                DroppedEarliestObservedGameTime=source.DroppedEarliestObservedGameTime};
            foreach(NpcTreatmentOutcome row in source.Outcomes)
                copy.Outcomes.Add(CopyNpcTreatmentOutcome(row));
            return copy;
        }

        private static NpcTreatmentOutcome CopyNpcTreatmentOutcome(NpcTreatmentOutcome source)
        {
            return new NpcTreatmentOutcome {Revision=source.Revision,ActionId=source.ActionId,
                EffectId=source.EffectId,ReceiptId=source.ReceiptId,TaskId=source.TaskId,
                TaskStepIndex=source.TaskStepIndex,Kind=source.Kind,ObservedGameTime=source.ObservedGameTime,
                RestStartedAt=source.RestStartedAt,RestDurationSeconds=source.RestDurationSeconds,
                HealthBefore=source.HealthBefore,HealthAfter=source.HealthAfter,SourceItemUid=source.SourceItemUid,
                SpellId=source.SpellId,SpellEffectId=source.SpellEffectId,
                MagickaCost=source.MagickaCost,MagickaBefore=source.MagickaBefore,
                MagickaAfter=source.MagickaAfter,
                PurchaseActionId=source.PurchaseActionId,PurchaseEffectId=source.PurchaseEffectId,
                SellerPersonId=source.SellerPersonId,PaidGold=source.PaidGold};
        }

        private static List<NpcTreatmentOutcomeHistory> CopyNpcTreatmentOutcomeHistories(WorldState source,WorldState validated)
        {
            var result=new List<NpcTreatmentOutcomeHistory>();
            if(source.NpcTreatmentOutcomeHistories==null || source.NpcTreatmentOutcomeHistories.Count>100000)
                throw new ArgumentException("Invalid NPC treatment outcome history ledger.");
            var people=new HashSet<string>(StringComparer.Ordinal);
            foreach(NpcTreatmentOutcomeHistory history in source.NpcTreatmentOutcomeHistories)
            {
                ValidateNpcTreatmentOutcomeHistory(history);
                bool alive;
                if(!people.Add(history.PersonId) || !KnownNpcIdentity(validated,history.PersonId,out alive))
                    throw new ArgumentException("Unknown or duplicate treatment outcome owner.");
                result.Add(CopyNpcTreatmentOutcomeHistory(history));
            }
            foreach(NpcActionJob action in validated.NpcActions)
            {
                NpcTreatmentOutcomeHistory history=result.Find(value=>value.PersonId==action.ResidentId);
                if(action.TreatmentOutcomeRevision==0)
                {
                    if(history!=null && history.Outcomes.Exists(value=>value.ActionId==action.Id))
                        throw new ArgumentException("Retained treatment outcome requires its action projection marker.");
                    continue;
                }
                if(history==null || action.TreatmentOutcomeRevision>history.Revision)
                    throw new ArgumentException("Treatment action projection marker has no owner coverage.");
                if(action.TreatmentOutcomeRevision<=history.OutcomeBaseRevision)continue;
                NpcTreatmentOutcome outcome=history.Outcomes.Find(value=>value.Revision==action.TreatmentOutcomeRevision);
                if(outcome==null || !NpcTreatmentOutcomeMatchesAction(outcome,action,validated))
                    throw new ArgumentException("Treatment action projection marker differs from its retained outcome.");
            }
            return result;
        }

        private static bool NpcTreatmentOutcomeMatchesAction(NpcTreatmentOutcome outcome,NpcActionJob action,
            WorldState validated)
        {
            NpcTreatmentReceipt receipt=action.TreatmentReceipt;
            if(receipt==null || outcome.ActionId!=action.Id || outcome.EffectId!=action.CompletedEffectId ||
                outcome.ReceiptId!=receipt.TransactionId || outcome.Kind!=receipt.Kind ||
                outcome.ObservedGameTime!=receipt.ObservedGameTime || outcome.HealthBefore!=receipt.HealthBefore ||
                outcome.HealthAfter!=receipt.HealthAfter || outcome.SourceItemUid!=receipt.SourceItemUid ||
                outcome.RestStartedAt!=receipt.RestStartedAt || outcome.RestDurationSeconds!=receipt.RestDurationSeconds)
                return false;
            if(outcome.SpellId!=receipt.SpellId || outcome.SpellEffectId!=receipt.EffectId ||
                outcome.MagickaCost!=receipt.MagickaCost || outcome.MagickaBefore!=receipt.MagickaBefore ||
                outcome.MagickaAfter!=receipt.MagickaAfter)return false;
            if(outcome.TaskId==null)return outcome.PurchaseActionId==null;
            NpcTaskPlan task=validated.NpcTasks.Find(value=>value.Id==outcome.TaskId);
            if(task==null || task.ResidentId!=action.ResidentId || outcome.TaskStepIndex<0 ||
                outcome.TaskStepIndex>=task.Steps.Count ||
                outcome.ActionId!=task.Id+":step:"+outcome.TaskStepIndex.ToString(CultureInfo.InvariantCulture) ||
                task.Steps[outcome.TaskStepIndex].Kind!=action.Kind)return false;
            if(outcome.PurchaseActionId==null)return true;
            int source=task.Steps[outcome.TaskStepIndex].ItemSourceStep-1;
            if(source<0 || source>=outcome.TaskStepIndex || task.Steps[source].Kind!=NpcActionKind.Purchase ||
                outcome.PurchaseActionId!=task.Id+":step:"+source.ToString(CultureInfo.InvariantCulture))return false;
            NpcActionJob purchase=validated.NpcActions.Find(value=>value.Id==outcome.PurchaseActionId);
            return purchase!=null && purchase.ResidentId==action.ResidentId &&
                purchase.Kind==NpcActionKind.Purchase && purchase.Phase==NpcActionPhase.Completed &&
                purchase.CompletedEffectId==outcome.PurchaseEffectId &&
                purchase.ServiceCode==NpcShopCommerce.ServiceCode && purchase.ServicePrice==outcome.PaidGold &&
                purchase.Target!=null && purchase.Target.PersonId==outcome.SellerPersonId &&
                purchase.AcquiredItemId==outcome.SourceItemUid.ToString(CultureInfo.InvariantCulture) &&
                purchase.UpdatedAt<=receipt.ObservedGameTime;
        }

        private static void ValidateNpcTreatmentOutcomeHistory(NpcTreatmentOutcomeHistory history)
        {
            if(history==null || string.IsNullOrWhiteSpace(history.PersonId) || history.PersonId.Length>240 ||
                history.Revision<1 || history.OutcomeBaseRevision<0 || history.OutcomeBaseRevision>=history.Revision ||
                double.IsNaN(history.DroppedEarliestObservedGameTime) ||
                double.IsInfinity(history.DroppedEarliestObservedGameTime) ||
                history.DroppedEarliestObservedGameTime<0 ||
                history.OutcomeBaseRevision==0 && history.DroppedEarliestObservedGameTime!=0 ||
                history.Outcomes==null || history.Outcomes.Count<1 ||
                history.Outcomes.Count>MaximumNpcTreatmentOutcomesPerPerson ||
                history.Revision-history.OutcomeBaseRevision!=history.Outcomes.Count)
                throw new ArgumentException("Invalid NPC treatment outcome coverage.");
            var actionIds=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<history.Outcomes.Count;i++)
            {
                NpcTreatmentOutcome row=history.Outcomes[i];
                if(row==null || row.Revision!=history.OutcomeBaseRevision+i+1 ||
                    string.IsNullOrWhiteSpace(row.ActionId) || row.ActionId.Length>240 || !actionIds.Add(row.ActionId) ||
                    row.EffectId!=row.ActionId+":observed" || row.ReceiptId!=row.ActionId+":treatment" ||
                    double.IsNaN(row.ObservedGameTime) || double.IsInfinity(row.ObservedGameTime) ||
                    row.ObservedGameTime<0 || row.HealthBefore<1 || row.HealthAfter<=row.HealthBefore ||
                    row.HealthAfter>10000 || row.TaskStepIndex< -1 || row.TaskStepIndex>7 ||
                    (row.TaskId==null)!=(row.TaskStepIndex== -1))
                    throw new ArgumentException("Invalid NPC treatment outcome.");
                if(row.TaskId!=null && (row.TaskId.Length>240 ||
                    row.ActionId!=row.TaskId+":step:"+row.TaskStepIndex.ToString(CultureInfo.InvariantCulture)))
                    throw new ArgumentException("Invalid treatment task provenance.");
                if(row.Kind==NpcTreatmentKind.Rest)
                {
                    if(row.SourceItemUid!=0 || row.RestStartedAt<0 || row.RestDurationSeconds<=0 ||
                        row.SpellId!=null || row.SpellEffectId!=null || row.MagickaCost!=0 ||
                        row.MagickaBefore!=0 || row.MagickaAfter!=0 ||
                        double.IsNaN(row.RestStartedAt) || double.IsInfinity(row.RestStartedAt) ||
                        double.IsNaN(row.RestDurationSeconds) || double.IsInfinity(row.RestDurationSeconds) ||
                        row.ObservedGameTime<row.RestStartedAt+row.RestDurationSeconds)
                        throw new ArgumentException("Invalid rest outcome.");
                }
                else if(row.Kind==NpcTreatmentKind.HealingItem)
                {
                    if(row.SourceItemUid==0 || row.RestStartedAt!=0 || row.RestDurationSeconds!=0 ||
                        row.SpellId!=null || row.SpellEffectId!=null || row.MagickaCost!=0 ||
                        row.MagickaBefore!=0 || row.MagickaAfter!=0)
                        throw new ArgumentException("Invalid item outcome.");
                }
                else if(row.Kind==NpcTreatmentKind.MagicSelfCast)
                {
                    if(row.SourceItemUid!=0 || row.RestStartedAt!=0 || row.RestDurationSeconds!=0 ||
                        string.IsNullOrWhiteSpace(row.SpellId) || row.SpellId.Length>240 ||
                        row.SpellEffectId!="Heal-Health" || row.MagickaCost<=0 ||
                        row.MagickaBefore<row.MagickaCost ||
                        row.MagickaAfter!=row.MagickaBefore-row.MagickaCost)
                        throw new ArgumentException("Invalid magic healing outcome.");
                }
                else throw new ArgumentException("Unknown treatment outcome kind.");
                bool hasPurchase=row.PurchaseActionId!=null || row.PurchaseEffectId!=null ||
                    row.SellerPersonId!=null || row.PaidGold!=0;
                if(hasPurchase && (row.Kind!=NpcTreatmentKind.HealingItem || row.TaskId==null ||
                    string.IsNullOrWhiteSpace(row.PurchaseActionId) || row.PurchaseActionId.Length>240 ||
                    row.PurchaseEffectId!=row.PurchaseActionId+":observed" ||
                    string.IsNullOrWhiteSpace(row.SellerPersonId) || row.SellerPersonId.Length>240 ||
                    row.PaidGold<1 || row.PaidGold>NpcInventoryLimits.MaximumWalletGold))
                    throw new ArgumentException("Invalid treatment purchase provenance.");
            }
        }
    }
}
