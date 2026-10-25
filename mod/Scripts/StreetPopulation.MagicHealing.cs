using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void AddMagicHealthChoices(ResidentActor actor,NpcHealthCareRecord care,double now,
            NpcHealthCareChoiceContext choiceContext,
            List<NpcHealthCareCandidate> candidates,Dictionary<string,NpcTaskPlan> tasks)
        {
            if(!HealthMagicActorAvailable(actor,care.PersonId,now))return;
            // Discovery estimates capability. The shared arbiter still owns duty
            // interruption, and the native adapter rechecks the accepted task.
            var offers=NativeNpcMagicAction.DiscoverCareOptions(actor.gameObject,worldStore,
                ()=>HealthMagicActorAvailable(actor,care.PersonId,now));
            var spellChoices=new List<NpcHealthCareCandidate>();
            var spellTasks=new Dictionary<string,NpcTaskPlan>(StringComparer.Ordinal);
            foreach(var offer in offers)
            {
                if(offer.Need!=NpcMagicCareNeed.Heal)continue;
                int gain;
                if(!NativeNpcMagicHealing.TryGetConservativeHealingMagnitude(actor.gameObject,offer,out gain) || gain<=0 || gain>10000)
                    continue;
                var authorization=offer.Authorization;
                string key="spell:"+authorization.SpellId;
                if(spellTasks.ContainsKey(key))continue;
                var task=HealthTask(care,now);
                task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.CastSpell,
                    Target=ActionPosition(actor.transform.position-Vector3.up),
                    MagicHealing=new NpcMagicHealingSpec {SpellId=authorization.SpellId,
                        EffectId=authorization.EffectId,MagickaCost=authorization.MagickaCost,
                        MagickaBefore=authorization.MagickaBefore,HealthBefore=offer.ObservedConditionBefore}});
                spellChoices.Add(new NpcHealthCareCandidate {StableId=key,
                    Kind=NpcHealthCareCandidateKind.OwnedHealingSpell,CostMagicka=authorization.MagickaCost,
                    ExpectedHealthGain=gain,ExpectedTimeSeconds=1,
                    IsSafe=true,IsAccessible=true,SupplyKnown=true});
                spellTasks.Add(key,task);
            }
            // A spellbook has at most 128 entries. Rank every spell first so
            // later entries are not lost when rest and potion choices are added.
            // All candidates use the same independent score and tie breaker.
            var bestSpell=NpcHealthCareChoice.Select(choiceContext,spellChoices);
            if(bestSpell==null)return;
            candidates.Add(bestSpell.Candidate);
            tasks.Add(bestSpell.Candidate.StableId,spellTasks[bestSpell.Candidate.StableId]);
        }

        private bool HealthMagicActorAvailable(ResidentActor actor,string id,double now)
        {
            return worldStore!=null && actor && actor.gameObject.activeInHierarchy &&
                actor.Resident!=null && actor.Resident.Id==id && actor.Resident.IsAlive &&
                actor.Resident.AgeGroup==ResidentAgeGroup.Adult && !actor.Resident.IsQuestProtected &&
                !actor.IsConversing && !GameManager.IsGamePaused && !QuestOwned(actor.Resident) &&
                !IsAlarmed(id) && !worldStore.IsResidentAway(id,now);
        }

        private bool PrepareHealthMagicAction(ResidentActor actor,NpcActionJob job,out string reason)
        {
            reason="The exact healing spell or its resources are no longer available.";
            if(job==null || job.MagicHealing==null || !HealthMagicActorAvailable(actor,job.ResidentId,ActionNow()))
                return false;
            foreach(var offer in NativeNpcMagicAction.DiscoverCareOptions(actor.gameObject,worldStore,
                ()=>HealthMagicActorAvailable(actor,job.ResidentId,ActionNow())))
            {
                var spell=job.MagicHealing;
                if(offer.Need==NpcMagicCareNeed.Heal && offer.Authorization.SpellId==spell.SpellId &&
                    offer.Authorization.EffectId==spell.EffectId && offer.Authorization.MagickaCost==spell.MagickaCost &&
                    offer.Authorization.MagickaBefore==spell.MagickaBefore && offer.ObservedConditionBefore==spell.HealthBefore)
                {reason=null;return true;}
            }
            return false;
        }

        private bool HealthMagicDutyStillAvailable(ResidentActor actor,NpcActionJob job,double now)
        {
            if(job==null || !HealthMagicActorAvailable(actor,job.ResidentId,now))return false;
            var active=worldStore.ActiveNpcAction(job.ResidentId);
            var task=worldStore.ActiveNpcTask(job.ResidentId);
            var care=worldStore.ReadNpcHealthCare(job.ResidentId);
            var motivation=worldStore.ReadNpcMotivationState(job.ResidentId);
            var goal=ActiveRecoveryGoal(motivation);
            if(active==null || active.Id!=job.Id || active.Revision!=job.Revision ||
                active.Kind!=NpcActionKind.CastSpell || active.Phase!=NpcActionPhase.Executing ||
                task==null || task.ActiveActionId!=job.Id || !IsHealthCareTask(task) ||
                care==null || care.Phase!=NpcHealthCarePhase.Acting || care.TaskId!=task.Id ||
                goal==null || task.GoalId!=goal.Id || task.GoalStartRevision!=goal.GoalStartRevision ||
                !ActionSpaceMatches(job.Target))return false;
            var service=ChooseHealthService(job.ResidentId,care,actor,task,now);
            // A live service must already have yielded through shared admission.
            if(service.Ambiguous || service.ActiveDuty || service.Contract!=null &&
                service.Contract.SuspensionOwnerId!=care.Id)return false;
            Vector3 target;float range;string failure;
            return ResolveActionTarget(job,out target,out range,out failure) &&
                Vector3.Distance(actor.transform.position-Vector3.up,target)<=range;
        }
    }
}
