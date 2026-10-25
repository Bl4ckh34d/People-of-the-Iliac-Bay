using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        /// <summary>
        /// Commits one synchronous native self-heal in the bound health-care task.
        /// The native caller must restore native health and magicka if this returns false.
        /// </summary>
        internal bool TryCompleteNpcMagicHealingAction(string id,long expectedRevision,double now,
            NpcArchetype archetype,long expectedVitalsRevision,
            Func<NpcMagicObservedOutcome> applyNativeEffect)
        {
            int actionIndex=state.NpcActions.FindIndex(job=>job.Id==id);
            if(actionIndex<0 || applyNativeEffect==null || double.IsNaN(now) || double.IsInfinity(now))return false;
            NpcActionJob current=state.NpcActions[actionIndex];
            NpcMagicHealingSpec spec=current.MagicHealing;
            if(current.Kind!=NpcActionKind.CastSpell || spec==null ||
                current.Phase!=NpcActionPhase.Executing || current.Revision!=expectedRevision ||
                expectedRevision==long.MaxValue || current.TreatmentReceipt!=null ||
                current.FollowTransition!=null || current.RoutineRecoveryTransition!=null ||
                now<current.UpdatedAt || now<current.AcceptedAt || now>=current.DeadlineAt ||
                current.Admission==null || current.Admission.Kind!=NpcOperationAdmissionKind.TaskStep ||
                !NpcActionContract.CanTransition(current.Phase,NpcActionPhase.Completed,true))return false;
            Resident resident=ResolveResident(current.ResidentId);
            if(resident==null || !resident.IsAlive || resident.AgeGroup!=ResidentAgeGroup.Adult ||
                resident.IsQuestProtected)return false;
            NpcTaskPlan task=state.NpcTasks.Find(row=>row.Id==current.Admission.SourceId);
            NpcHealthCareRecord care=state.NpcHealthCares.Find(row=>row.PersonId==current.ResidentId);
            if(task==null || task.Phase!=NpcTaskPhase.Running || task.ActiveActionId!=id ||
                task.ResidentId!=current.ResidentId || task.GoalId!=NpcHealthNeedLifecycle.RecoveryGoalId ||
                task.GoalStartRevision<1 || task.CurrentStepIndex!=current.Admission.StepIndex ||
                task.CurrentStepIndex<0 || task.CurrentStepIndex>=task.Steps.Count ||
                task.Admission==null || task.Admission.Kind!=NpcOperationAdmissionKind.HealthCare ||
                !NpcMagicHealingSpec.Same(spec,task.Steps[task.CurrentStepIndex].MagicHealing) ||
                care==null || care.Phase!=NpcHealthCarePhase.Acting || care.TaskId!=task.Id ||
                care.PersonId!=current.ResidentId)return false;
            if(!HasCurrentMagicHealingGoal(current.ResidentId,task))return false;
            NpcVitalsView vitals=ReadNpcVitals(current.ResidentId,archetype);
            if(!vitals.IsAlive || !vitals.IsPersisted || vitals.Revision!=expectedVitalsRevision ||
                expectedVitalsRevision>=NpcSkillProgression.MaximumRawExperience ||
                vitals.CurrentHealth!=spec.HealthBefore || vitals.CurrentHealth>=vitals.MaximumHealth ||
                vitals.CurrentMagicka!=spec.MagickaBefore || vitals.CurrentMagicka<spec.MagickaCost ||
                now<vitals.CapturedAt)return false;

            NpcMagicObservedOutcome outcome;
            try {outcome=applyNativeEffect();} catch(Exception) {return false;}
            if(outcome==null || !outcome.NativeAdapterExecuted ||
                outcome.ActorId!=current.ResidentId || outcome.TargetId!=current.ResidentId ||
                outcome.SpellId!=spec.SpellId || outcome.EffectId!=spec.EffectId ||
                outcome.EffectKind!=NpcMagicEffectKind.Health ||
                outcome.MagickaBefore!=spec.MagickaBefore ||
                outcome.MagickaAfter!=spec.MagickaBefore-spec.MagickaCost ||
                outcome.EffectValueBefore!=spec.HealthBefore ||
                outcome.EffectValueAfter<=spec.HealthBefore ||
                outcome.EffectValueAfter>vitals.MaximumHealth)return false;
            NpcTreatmentVitalsStage stage;
            if(!TryStageTreatmentVitals(current.ResidentId,archetype,expectedVitalsRevision,
                spec.HealthBefore,outcome.EffectValueAfter,outcome.MagickaAfter,now,out stage))return false;
            if(actionIndex>=state.NpcActions.Count || !ReferenceEquals(state.NpcActions[actionIndex],current) ||
                !ReferenceEquals(state.NpcTasks.Find(row=>row.Id==task.Id),task) ||
                !ReferenceEquals(state.NpcHealthCares.Find(row=>row.PersonId==current.ResidentId),care) ||
                !HasCurrentMagicHealingGoal(current.ResidentId,task) ||
                !ReferenceEquals(IndexedNpcProgress(current.ResidentId),stage.Current))return false;
            NpcActionJob completed;
            try
            {
                NpcTreatmentReceipt receipt=NpcTreatmentReceipt.MagicSelfCast(current,outcome,now);
                if(!NpcActionExecutor.Advance(current,expectedRevision,now,
                    NpcActionObservation.TreatmentSuccess(id+":observed",receipt),out completed) ||
                    completed.Phase!=NpcActionPhase.Completed)return false;
            }
            catch(ArgumentException) {return false;}
            CommitTreatmentVitals(current.ResidentId,stage);
            state.NpcActions[actionIndex]=completed;IndexNpcAction(completed);
            return true;
        }

        private bool HasCurrentMagicHealingGoal(string personId,NpcTaskPlan task)
        {
            NpcMotivationStateView motivation=ReadNpcMotivationState(personId);
            if(motivation==null)return false;
            foreach(NpcMotivationGoalView goal in motivation.Goals)
                if(goal.Id==task.GoalId && goal.GoalStartRevision==task.GoalStartRevision &&
                    goal.Kind==NpcMotivationGoalKind.Recovery &&
                    goal.Status==NpcMotivationGoalStatus.Active &&
                    goal.KnowledgeState==NpcMotivationKnowledgeState.Known &&
                    goal.EvidenceKind==NpcMotivationStateEvidenceKind.NativeObservation)return true;
            return false;
        }
    }
}
