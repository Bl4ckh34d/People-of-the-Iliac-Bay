using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private readonly Dictionary<string,float> healthCareChecks = new Dictionary<string,float>();
        private object healthCareEpoch;

        private bool HasActiveNpcHealthCare(string id)
        {
            var care = worldStore.ReadNpcHealthCare(id);
            return worldStore.HasNpcTreatmentVisit(id) || care != null && care.Phase != NpcHealthCarePhase.Recovered;
        }

        private static NpcArchetype HealthArchetype(WorldStateStore world,string id)
        {
            return world.ResolveNpcArchetype(id);
        }

        private bool IsHealthCareTask(NpcTaskPlan task)
        {
            if(task==null || worldStore==null)return false;
            var care=worldStore.ReadNpcHealthCare(task.ResidentId);
            return care!=null && care.Phase==NpcHealthCarePhase.Acting && care.TaskId==task.Id;
        }

        private static NpcMotivationGoalView ActiveRecoveryGoal(NpcMotivationStateView motivation)
        {
            if(motivation==null)return null;
            foreach(var goal in motivation.Goals)
                if(goal.Id==NpcHealthNeedLifecycle.RecoveryGoalId && goal.Kind==NpcMotivationGoalKind.Recovery &&
                    goal.Status==NpcMotivationGoalStatus.Active && goal.KnowledgeState==NpcMotivationKnowledgeState.Known &&
                    goal.EvidenceKind==NpcMotivationStateEvidenceKind.NativeObservation)return goal;
            return null;
        }

        private bool HasCurrentAppointmentDuty(string id,double now)
        {
            foreach(var appointment in worldStore.AppointmentsFor(id))
                if((appointment.Status=="accepted" || appointment.Status=="waiting" ||
                    appointment.Status=="attended") && appointment.StartsAt<=now &&
                    now<AppointmentTiming.PlannedLeaveAt(appointment))return true;
            return false;
        }

        // Called before the ordinary action/schedule overlay. It selects work but
        // never drives a second mover: all treatment tasks use the existing executor.
        private bool PrepareNpcHealthCare(Plan plan,ref ResidentActor actor,double now)
        {
            if(worldStore==null || plan==null || !plan.Resident.IsAlive || plan.Resident.IsQuestProtected ||
                plan.Resident.AgeGroup!=ResidentAgeGroup.Adult || QuestOwned(plan.Resident) ||
                IsAlarmed(plan.Resident.Id) || worldStore.IsResidentAway(plan.Resident.Id,now))return false;
            string id=plan.Resident.Id;
            var epoch=worldStore.CaptureNpcVitalsWriteEpoch();
            if(!ReferenceEquals(epoch,healthCareEpoch)) {healthCareChecks.Clear();healthCareEpoch=epoch;}
            var care=worldStore.ReadNpcHealthCare(id);
            bool owns=care!=null && care.Phase==NpcHealthCarePhase.Acting;
            float checkAt;
            if(GameManager.IsGamePaused || healthCareChecks.TryGetValue(id,out checkAt) && Time.unscaledTime<checkAt)
                return owns;
            healthCareChecks[id]=Time.unscaledTime+0.5f;

            if(actor && actor.gameObject.activeInHierarchy)
            {
                var binding=actor.GetComponent<NativeNpcVitalsBinding>();
                if(!binding)return owns;
                binding.Capture();
            }
            var archetype=HealthArchetype(worldStore,id);
            var vitals=worldStore.ReadNpcVitals(id,archetype);
            NpcHealthNeedLifecycle.Observe(worldStore,id,archetype,now);
            if(CombatActionOwnsInjuryResponse(id))return false;
            ReconcileHealthService(plan,actor,care,vitals,archetype,now);
            var motivation=worldStore.ReadNpcMotivationState(id);
            var recoveryGoal=ActiveRecoveryGoal(motivation);
            if(care==null || care.Phase==NpcHealthCarePhase.Recovered)
            {
                if(recoveryGoal==null || !actor || !actor.gameObject.activeInHierarchy || !vitals.IsPersisted ||
                    !NpcHealthNeedLifecycle.ShouldInterruptWork(vitals))return false;
                if(!worldStore.TryBeginNpcHealthCare(id,archetype,now,ActionPosition(actor.transform.position-Vector3.up)))return false;
                care=worldStore.ReadNpcHealthCare(id);owns=true;
            }

            var task=worldStore.ActiveNpcTask(id);
            if(task!=null && task.Phase==NpcTaskPhase.Running)
            {
                var step=worldStore.ReadNpcAction(task.ActiveActionId);
                if(NpcActionExecutor.IsTerminal(step))
                {worldStore.ReconcileNpcTask(task.Id,task.Revision,now);task=worldStore.ActiveNpcTask(id);}
            }
            // An ongoing rest can finish naturally; potion care releases work at two-thirds HP.
            bool resting=task!=null && task.Phase==NpcTaskPhase.Running &&
                task.CurrentStepIndex<task.Steps.Count && task.Steps[task.CurrentStepIndex].Kind==NpcActionKind.RestToHeal;
            if(vitals.CurrentHealth==vitals.MaximumHealth || NpcHealthNeedLifecycle.HasRecoveredForWork(vitals) && !resting)
            {
                NpcActionTarget observed=actor && actor.gameObject.activeInHierarchy?
                    ActionPosition(actor.transform.position-Vector3.up):null;
                if(NpcHealthCareRecoveryHandoff.TryComplete(worldStore,id,care.Revision,archetype,now,observed))
                {ReconcileHealthService(plan,actor,worldStore.ReadNpcHealthCare(id),vitals,archetype,now);
                    NpcHealthNeedLifecycle.Observe(worldStore,id,archetype,now);return false;}
                return owns;
            }
            if(!actor || !actor.gameObject.activeInHierarchy)return owns;
            if(actor.IsConversing)return owns;

            if(care.Phase==NpcHealthCarePhase.Acting)
            {
                var accepted=worldStore.ReadNpcTask(care.TaskId);
                if(accepted!=null && !NpcTaskContract.IsTerminal(accepted))return true;
                NpcActionTarget observed=ActionPosition(actor.transform.position-Vector3.up);
                if(accepted!=null && accepted.Phase==NpcTaskPhase.Completed && accepted.Steps.Count>0)
                {
                    var treated=worldStore.ReadNpcAction(accepted.Id+":step:"+(accepted.Steps.Count-1));
                    if(treated==null || !NpcActionExecutor.IsTerminal(treated) ||
                        !worldStore.SaveNpcActionPosition(treated.Id,treated.Revision,observed,true,now))return true;
                    if(worldStore.TryContinueNpcHealthCareAfterTreatment(id,care.Revision,archetype,now,observed))
                        care=worldStore.ReadNpcHealthCare(id);
                    else
                    {
                        worldStore.TryUpdateNpcHealthCarePosition(id,care.Revision,now,observed);
                        care=worldStore.ReadNpcHealthCare(id);
                        worldStore.TryBlockNpcHealthCare(id,care.Revision,now,
                            "The completed care task has no verified partial treatment to continue.");
                        return true;
                    }
                }
                else
                {
                    var latest=worldStore.ActiveNpcAction(id);
                    if(latest!=null && NpcActionExecutor.IsTerminal(latest))
                        worldStore.SaveNpcActionPosition(latest.Id,latest.Revision,observed,latest.ReturningToRoutine,now);
                    worldStore.TryUpdateNpcHealthCarePosition(id,care.Revision,now,observed);
                }
                care=worldStore.ReadNpcHealthCare(id);
                if(care.Phase==NpcHealthCarePhase.Acting)
                {
                    worldStore.TryBlockNpcHealthCare(id,care.Revision,now,
                        accepted==null?"The treatment task is unavailable.":accepted.FailureReason??"Treatment was interrupted.");
                    return true;
                }
            }
            if(care.Phase==NpcHealthCarePhase.Blocked && now<care.RetryAt)return false;

            string failure=null;
            NpcTaskPlan proposed=null;
            bool found=TryChooseHealthTask(actor,care,vitals,now,out proposed,out failure);
            if(!found && task==null)
            {
                worldStore.TryBlockNpcHealthCare(id,care.Revision,now,failure??"No safe accessible treatment is currently available.");
                return false;
            }
            var service=ChooseHealthService(id,care,actor,proposed,now);
            if(service.Ambiguous)return false;
            var selection=NpcGoalArbiter.Select(new NpcGoalArbitrationInput {
                PersonId=id,Motivation=motivation,EligibleResident=true,
                NativeAlarmActive=IsAlarmed(id),HealthEligible=NpcGoalArbiter.HealthEligibleForArbitration(vitals,care),
                HealthTaskFeasible=found,ActiveTask=task,ActiveTaskIsCare=IsHealthCareTask(task),
                ActiveAppointment=HasCurrentAppointmentDuty(id,now),ActiveServiceDuty=service.ActiveDuty,
                ServiceHealthAdmission=service.Admission,CareTreatmentPreviouslyAdmitted=care.HasAcceptedTreatment,
                CareRetryPending=care.Phase==NpcHealthCarePhase.Blocked && now<care.RetryAt
            });
            if(selection.Disposition!=NpcGoalWorkDisposition.BeginGoalTask &&
                selection.Disposition!=NpcGoalWorkDisposition.InterruptTask)return false;
            if(!found || selection.GoalId!=NpcHealthNeedLifecycle.RecoveryGoalId)return false;
            if(!PrepareHealthServiceInterruption(service,id,care,vitals,archetype,selection,actor,proposed,now))return false;

            // A completed action is reconciled above. Failed cancellation retains its old movement owner.
            if(task!=null)
            {
                const string reason="I must interrupt this task to recover from my injuries.";
                if(!worldStore.CancelNpcTask(task.Id,task.Revision,now,reason))return false;
                DaggerfallUI.AddHUDText(ActionRecipientName(actor)+": "+reason,4f);
            }
            var action=worldStore.ActiveNpcAction(id);
            if(action!=null && (care.TaskId==null || !action.Id.StartsWith(care.TaskId+":step:",StringComparison.Ordinal)))
            {
                EndAction(action,now,"Injury interrupted the ordinary activity.",false);
                actionMotions.Remove(id);ReleaseActionSeat(id);
            }
            // Candidate plans are estimates. Capture the live target again after an interruption.
            if(!TryChooseHealthTask(actor,care,worldStore.ReadNpcVitals(id,archetype),now,out proposed,out failure))
            {
                worldStore.TryBlockNpcHealthCare(id,care.Revision,now,failure??"The treatment target changed.");
                return false;
            }
            if(service.Admission==NpcGoalServiceHealthAdmission.PaidRest &&
                !IsNegotiatedPaidHealthRest(service.Contract,actor,proposed,now))
            {worldStore.TryBlockNpcHealthCare(id,care.Revision,now,"The paid-rest target changed before admission.");return false;}
            var currentMotivation=worldStore.ReadNpcMotivationState(id);
            var currentGoal=ActiveRecoveryGoal(currentMotivation);
            if(currentMotivation==null || currentMotivation.Revision!=selection.MotivationRevision ||
                currentGoal==null || currentGoal.GoalStartRevision!=selection.GoalStartRevision)return false;
            proposed.GoalId=currentGoal.Id;proposed.GoalStartRevision=currentGoal.GoalStartRevision;
            if(!worldStore.TryAcceptAndBindNpcHealthCareTask(id,care.Revision,proposed,now,out failure))
            {worldStore.TryBlockNpcHealthCare(id,care.Revision,now,failure);return false;}
            if(!TryConfirmHealthRestReservation(actor,proposed,now,out failure) || !DispatchConversationTaskStep(proposed,out failure))
                worldStore.FailNpcTask(proposed.Id,proposed.Revision,now,failure);
            return true;
        }

        private bool TryTreatNpcConditions(ResidentActor actor,double now)
        {
            if(!actor || actor.Resident==null || !HealthMagicActorAvailable(actor,actor.Resident.Id,now))return false;
            var effects=actor.GetComponent<DaggerfallWorkshop.Game.MagicAndEffects.EntityEffectManager>();
            if(!effects || effects.PoisonCount==0 && effects.DiseaseCount==0)return false;
            string id=actor.Resident.Id;float next;
            if(healthCareChecks.TryGetValue(id,out next) && Time.unscaledTime<next)return false;
            // A failed native chance roll still consumes the actual potion or magicka. Do not retry every frame.
            healthCareChecks[id]=Time.unscaledTime+5f;
            return NativeNpcConditionTreatment.TryTreatOwned(actor,worldStore,
                ()=>HealthMagicActorAvailable(actor,id,ActionNow()));
        }

        private bool TryChooseHealthTask(ResidentActor actor,NpcHealthCareRecord care,NpcVitalsView vitals,
            double now,out NpcTaskPlan chosen,out string reason)
        {
            chosen=null;reason="No safe accessible treatment is currently available.";
            var candidates=new List<NpcHealthCareCandidate>();
            var tasks=new Dictionary<string,NpcTaskPlan>(StringComparer.Ordinal);
            NpcTaskPlan task;string unavailable;
            if(TryCreateHealthRestTask(actor,care,now,out task,out unavailable))
            {
                int cost,gain;double seconds;
                if(TryGetHealthRestChoice(actor,task,now,out cost,out gain,out seconds))
                    AddHealthChoice("rest",task,NpcHealthCareCandidateKind.Rest,cost,gain,seconds,candidates,tasks);
            }
            if(TryCreateOwnedHealthTask(actor,care,now,out task,out unavailable))
                AddPotionHealthChoice("owned",actor,task,candidates,tasks);
            if(TryCreateGuildHealthTask(actor,care,now,out task,out unavailable))
                AddPotionHealthChoice("guild",actor,task,candidates,tasks);
            if(TryCreateShopHealthTask(actor,care,now,out task,out unavailable))
                AddPotionHealthChoice("shop",actor,task,candidates,tasks);
            var inventory=worldStore.ReadNpcInventory(care.PersonId);
            if(inventory==null)return false;
            var choiceContext=new NpcHealthCareChoiceContext {
                CurrentHealth=vitals.CurrentHealth,MaximumHealth=vitals.MaximumHealth,AvailableGold=inventory.WalletGold,
                CurrentMagicka=vitals.CurrentMagicka
            };
            AddMagicHealthChoices(actor,care,now,choiceContext,candidates,tasks);
            var selected=NpcHealthCareChoice.Select(choiceContext,candidates);
            if(selected==null)return false;
            chosen=tasks[selected.Candidate.StableId];
            chosen.DeadlineAt=Math.Min(now+86400,Math.Max(chosen.DeadlineAt,now+selected.Candidate.ExpectedTimeSeconds+600));
            reason=null;return true;
        }

        private static void AddHealthChoice(string id,NpcTaskPlan task,NpcHealthCareCandidateKind kind,
            int cost,int gain,double seconds,List<NpcHealthCareCandidate> choices,Dictionary<string,NpcTaskPlan> tasks)
        {
            if(task==null || gain<=0 || seconds<0 || seconds>NpcHealthCareChoice.MaximumCandidateTimeSeconds-600)return;
            choices.Add(new NpcHealthCareCandidate {StableId=id,Kind=kind,CostGold=cost,ExpectedHealthGain=gain,
                ExpectedTimeSeconds=seconds,IsSafe=true,IsAccessible=true,SupplyKnown=true});
            tasks.Add(id,task);
        }

        private void AddPotionHealthChoice(string id,ResidentActor actor,NpcTaskPlan task,
            List<NpcHealthCareCandidate> choices,Dictionary<string,NpcTaskPlan> tasks)
        {
            if(task==null || task.Steps.Count==0)return;
            var step=task.Steps[0];ulong uid;
            if(!ulong.TryParse(step.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid))return;
            DaggerfallUnityItem item=null;
            bool purchase=step.Kind==NpcActionKind.Purchase;
            if(!purchase)
            {
                var owner=actor.GetComponent<NativeNpcInventoryOwner>();
                if(owner && owner.Binding!=null)item=owner.Binding.Items.GetItem(uid);
            }
            else if(IsGuildPotionTarget(step.Target))
            {
                NativeNpcInventoryBinding seller;string failure;
                if(NativeNpcInventoryBridge.TryOpen(worldStore,step.Target.PersonId,null,
                    NativeNpcInventoryBridge.InitialWalletGold(worldStore,step.Target.PersonId),out seller,out failure))
                    item=seller.Items.GetItem(uid);
            }
            else
            {
                var stock=FindShopStock(step.Target);if(stock)item=stock.Items.GetItem(uid);
            }
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();int gain;
            if(!body || !NativeNpcHealingItem.TryGetConservativeHealingMagnitude(item,body.Entity,out gain))return;
            var route=new ActionMotion();string reason;
            // Both the quote and estimate use a physically reachable route. Route
            // duration is a planning estimate; only observed arrival permits use.
            if(!BuildActionRoute(actor,route,ActionWorldPoint(step.Target),0.25f,out reason,purchase))return;
            double seconds=route.Indoor==null?0:route.Indoor.Duration;
            if(purchase)seconds+=1;
            AddHealthChoice(id,task,purchase?NpcHealthCareCandidateKind.BuyHealingItem:NpcHealthCareCandidateKind.OwnedHealingItem,
                purchase?step.Target.QuotedGold:0,gain,seconds,choices,tasks);
        }

        private NpcTaskPlan HealthTask(NpcHealthCareRecord care,double now)
        {
            string id=NpcHealthCare.MakeTaskId(care);
            return new NpcTaskPlan {Id=id,ProposalId=id,ResidentId=care.PersonId,Phase=NpcTaskPhase.Ready,
                Revision=1,AcceptedAt=now,UpdatedAt=now,DeadlineAt=now+1800};
        }

        private bool PrepareHealthHealingItem(ResidentActor actor,NpcActionJob job,out string reason)
        {
            reason="The exact healing potion is no longer available.";
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            var vitals=actor.GetComponent<NativeNpcVitalsBinding>();ulong uid;
            if(!owner || owner.Binding==null || !vitals || job.Target==null ||
                !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid) ||
                !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason))return false;
            var item=owner.Binding.Items.GetItem(uid);
            if(!NativeNpcHealingItem.IsSupportedPotion(item))return false;
            vitals.Capture();
            var state=worldStore.ReadNpcVitals(job.ResidentId,HealthArchetype(worldStore,job.ResidentId));
            if(!state.IsPersisted || state.CurrentHealth<=0 || state.CurrentHealth>=state.MaximumHealth ||
                NpcHealthNeedLifecycle.HasRecoveredForWork(state))return false;
            job.ExpectedInventoryRevision=owner.Binding.Revision;
            reason=null;return true;
        }

        private void OfferOwnedHealingItemAction(ResidentActor actor,string text,List<NpcActionOption> options,bool semantic)
        {
            if(!actor || options==null || options.Count>=(semantic?NpcActionCandidateCatalog.MaximumCandidates:24))return;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            var vitals=actor.GetComponent<NativeNpcVitalsBinding>();ulong uid;
            if(!owner || owner.Binding==null || !vitals ||
                !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out string ignored) ||
                !NativeNpcHealingItem.TryFindOwned(owner.Binding,out uid))return;
            vitals.Capture();
            var state=worldStore.ReadNpcVitals(actor.Resident.Id,HealthArchetype(worldStore,actor.Resident.Id));
            if(!state.IsPersisted || state.CurrentHealth<=0 || state.CurrentHealth>=state.MaximumHealth ||
                NpcHealthNeedLifecycle.HasRecoveredForWork(state))return;
            var target=ActionPosition(actor.transform.position-Vector3.up);
            target.ItemId=uid.ToString(CultureInfo.InvariantCulture);
            options.Add(new NpcActionOption {Id=options.Count+1,Kind=NpcActionKind.UseHealingItem,Target=target,
                Label="drink one exact owned native healing potion while below two-thirds health"});
        }

        private bool TryCreateOwnedHealthTask(ResidentActor actor,NpcHealthCareRecord care,double now,out NpcTaskPlan task,out string reason)
        {
            task=null;reason="I do not have a usable healing potion.";
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();ulong uid;
            if(!owner || owner.Binding==null || !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason) ||
                !NativeNpcHealingItem.TryFindOwned(owner.Binding,out uid))return false;
            task=HealthTask(care,now);
            var target=ActionPosition(actor.transform.position-Vector3.up);target.ItemId=uid.ToString(CultureInfo.InvariantCulture);
            task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.UseHealingItem,Target=target});
            reason=null;return true;
        }

        private bool TryCreateShopHealthTask(ResidentActor actor,NpcHealthCareRecord care,double now,out NpcTaskPlan task,out string reason)
        {
            task=null;reason="No reachable shop with an affordable healing potion is available here.";
            NativePerson seller;
            if(!interior || !TryShopSeller(out seller))return false;
            var inventory=worldStore.ReadNpcInventory(care.PersonId);if(inventory==null)return false;
            NpcActionTarget selected=null;int best=int.MaxValue;
            foreach(var shelf in interior.GetComponentsInChildren<DaggerfallLoot>())
            {
                if(!VerifiedShopStock(shelf))continue;
                for(int i=0;i<shelf.Items.Count;i++)
                {
                    var item=shelf.Items.GetItem(i);int price;Vector3 point;
                    if(!NativeNpcHealingItem.IsSupportedPotion(item) || !QuoteShopItem(actor,item,1,out price) ||
                        price>inventory.WalletGold || price>=best || !TryShopApproach(actor,shelf,out point))continue;
                    selected=ActionPosition(point);selected.PersonId=seller.Id;
                    selected.ContainerId=shelf.LoadID.ToString(CultureInfo.InvariantCulture);
                    selected.ItemId=item.UID.ToString(CultureInfo.InvariantCulture);selected.QuotedGold=price;best=price;
                }
            }
            if(selected==null)return false;
            task=HealthPurchaseTask(care,now,selected);reason=null;return true;
        }

        private NpcTaskPlan HealthPurchaseTask(NpcHealthCareRecord care,double now,NpcActionTarget purchase)
        {
            var task=HealthTask(care,now);
            task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Purchase,Target=purchase});
            var use=NpcActionExecutor.CopyTarget(purchase);use.PersonId=null;use.ContainerId=null;use.QuotedGold=0;
            task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.UseHealingItem,Target=use,ItemSourceStep=1});
            return task;
        }

        private bool HoldNpcHealthCare(Plan plan,ref ResidentActor actor,double now)
        {
            var care=worldStore.ReadNpcHealthCare(plan.Resident.Id);
            if(care==null || care.Phase==NpcHealthCarePhase.Recovered)return false;
            if(!actor && care.PausedPosition!=null && ActionSpaceMatches(care.PausedPosition))
            {
                var point=ActionWorldPoint(care.PausedPosition);
                if(actors.Count<80 && Vector3.Distance(point,GameManager.Instance.PlayerMotor.transform.position)<140)
                {
                    var go=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,interior?interior.transform:directory.transform);
                    go.name="People of the Iliac Bay "+plan.Resident.Id;actor=go.AddComponent<ResidentActor>();
                    actor.Initialize(plan.Resident,plan.SurnameSeed,navigation,inventoryStore:worldStore);actor.SetRunningPilotAtlas(runningPilotAtlas);
                    var crowd=actor.GetComponent<CrowdMotion>();if(crowd)crowd.RestorePlacement(point);else actor.transform.position=point+Vector3.up;
                    actors[plan.Resident.Id]=actor;go.SetActive(true);
                }
            }
            if(actor && !actor.IsConversing && !GameManager.IsGamePaused)
                actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true,freeStanding:true);
            return true;
        }

        // The native alarm mover has already placed this actor. Reconcile only the
        // interrupted care action, then retain that observed location for retry.
        private void RememberNpcCareAlarmPosition(Plan plan,ResidentActor actor,double now)
        {
            if(worldStore==null || !actor || !actor.gameObject.activeInHierarchy)return;
            string id=plan.Resident.Id;
            var care=worldStore.ReadNpcHealthCare(id);
            if(care==null || care.Phase!=NpcHealthCarePhase.Acting || care.TaskId==null)return;
            AdvanceConversationTask(id,actor,now,false);
            var task=worldStore.ReadNpcTask(care.TaskId);
            if(task==null || !NpcTaskContract.IsTerminal(task))return;
            care=worldStore.ReadNpcHealthCare(id);
            worldStore.TryUpdateNpcHealthCarePosition(id,care.Revision,now,
                ActionPosition(actor.transform.position-Vector3.up));
        }
    }
}
