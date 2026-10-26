using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class ActionMotion
        {
            public int RouteFailures;
            public string JobId;
            public InteriorRoomFollower Indoor;
            public List<SavedRoutePoint> Outdoor;
            public int Next;
            public Vector3 Goal,LastProgress;
            public Vector3 NativeDestination;
            public bool NativeOutdoor,NativeFailed;
            public float RepathAt,CheckAt,SaveAt,ProgressAt;
            public bool MovementPaused;
            public float NextWaypointRejoinAt;
            public NpcActionWaypointProgress WaypointProgress=new NpcActionWaypointProgress();
            public bool RecoveryWaiting;
            public bool RecoverySelected;
            public int RecoveryOrigin=-1;
            public SelectedSeatAccess Seat;
            public SelectedSeatAccess DepartureSeat;
            public bool SeatEntry;
            public bool Urgent;
        }
        private readonly Dictionary<string,ActionMotion> actionMotions=new Dictionary<string,ActionMotion>();
        private static double ActionNow()
        {var clock=DaggerfallUnity.Instance.WorldTime.Now;return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));}
        private void ResetNpcActionMotion()
        {foreach(var motion in actionMotions.Values) {if(motion.Seat!=null) motion.Seat.Release();if(motion.DepartureSeat!=null)motion.DepartureSeat.Release();}actionMotions.Clear();ResetNpcCombatActions();}
        public void CaptureNpcActionPositions()
        {
            if(!Ready || worldStore==null || !directory || population==null) return;
            double now=ActionNow();
            foreach(var job in worldStore.PendingNpcActions())
            {
                var actor=FindActor(job.ResidentId);
                if(actor && actor.gameObject.activeInHierarchy && ActionSpaceMatches(job.LastPosition))
                    worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actor.transform.position-Vector3.up),job.ReturningToRoutine,Math.Max(now,job.UpdatedAt));
            }
        }
        public bool TryStopConversationAction(string residentId,out string reason)
        {
            var dungeonOwner=DungeonFollowOwner;
            if(dungeonOwner && dungeonOwner.TryStopDungeonFollow(residentId,out reason))return true;
            return StopConversationAction(residentId,out reason,"The player asked the NPC to stop.");
        }

        public bool WithdrawConversationActionForBoundary(string residentId,string receiptId)
        {
            if(worldStore==null)return false;
            var boundary=worldStore.ReadNpcConversationBoundary(residentId,ActionNow());
            if(boundary==null || boundary.ReceiptId!=receiptId ||
                boundary.Action==ConversationSpeechBoundaryAction.Continue)return false;
            var task=worldStore.ActiveNpcTask(residentId);
            var job=worldStore.ActiveNpcAction(residentId);
            // Withdrawal concerns player-requested work. Autonomous recovery and
            // institution/service duties keep their own admission and interruption rules.
            if(task!=null ? task.Admission==null || task.Admission.Kind!=NpcOperationAdmissionKind.Conversation :
                job==null || job.Admission==null || job.Admission.Kind!=NpcOperationAdmissionKind.Conversation)
                return false;
            string ignored;
            return StopConversationAction(residentId,out ignored,
                "The NPC withdrew cooperation after the player's hostile speech.");
        }

        private bool StopConversationAction(string residentId,out string reason,string cancellationReason)
        {
            reason="No active action can be stopped.";
            if(worldStore==null) return false;
            var task=worldStore.ActiveNpcTask(residentId);
            if(task!=null && task.Phase==NpcTaskPhase.Ready)
            {
                if(!worldStore.CancelNpcTask(task.Id,task.Revision,ActionNow(),cancellationReason))return false;
                reason="The remaining task steps were cancelled.";return true;
            }
            var job=task!=null && task.ActiveActionId!=null?worldStore.ReadNpcAction(task.ActiveActionId):worldStore.ActiveNpcAction(residentId);var actor=FindActor(residentId);
            if(task!=null && job!=null && NpcActionExecutor.IsTerminal(job))
            {
                worldStore.ReconcileNpcTask(task.Id,task.Revision,ActionNow());task=worldStore.ActiveNpcTask(residentId);
                if(task!=null && task.Phase==NpcTaskPhase.Ready)
                {worldStore.CancelNpcTask(task.Id,task.Revision,ActionNow(),cancellationReason);reason="The remaining task steps were cancelled.";return true;}
            }
            if(job==null || NpcActionExecutor.IsTerminal(job) || !actor || !ActionSpaceMatches(job.LastPosition)) return false;
            double now=ActionNow();
            if(!worldStore.ObserveNpcAction(job.Id,job.Revision,now,NpcActionObservation.Cancelled(cancellationReason))) return false;
            job=worldStore.ReadNpcAction(job.Id);
            RememberActionOutcome(job,now);
            string settlementError;
            if(!TrySettlePaidCompanionAction(job,NpcServiceEndReason.PlayerDismissal,out settlementError))
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_SETTLEMENT: "+settlementError);
            worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actor.transform.position-Vector3.up),true,now);
            if(task!=null) worldStore.CancelNpcTask(task.Id,task.Revision,now,cancellationReason);
            if(shopGeometry!=null) shopGeometry.ReleaseCounter(residentId);
            reason="Stopped and saved. The NPC will physically rejoin the ordinary routine.";return true;
        }

        private string ActionWorldSpace {get {return "settlement:"+population.SettlementId.ToString(CultureInfo.InvariantCulture);}}
        private NpcActionTarget ActionPosition(Vector3 world)
        {
            var local=(interior?interior.transform:directory.transform).InverseTransformPoint(world);
            return new NpcActionTarget {Kind=interior?NpcActionTargetKind.IndoorPoint:NpcActionTargetKind.WorldPoint,
                SettlementId=population.SettlementId,BuildingKey=interior?interior.EntryDoor.buildingKey:-1,
                WorldSpaceId=interior?null:ActionWorldSpace,X=local.x,Y=local.y,Z=local.z};
        }
        private bool ActionSpaceMatches(NpcActionTarget point)
        {
            return point!=null && (interior ? point.Kind==NpcActionTargetKind.IndoorPoint && point.SettlementId==population.SettlementId &&
                point.BuildingKey==interior.EntryDoor.buildingKey : point.Kind==NpcActionTargetKind.WorldPoint && point.WorldSpaceId==ActionWorldSpace);
        }
        private Vector3 ActionWorldPoint(NpcActionTarget point)
        {
            return interior?interior.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z)):
                geometry.ScenePoint(new RoutePoint(point.X,point.Z));
        }

        // The offered list is a capability list. Unsupported enum values never become options.
        public List<NpcActionOption> ConversationActionOptions(string residentId,string text)
        {return BuildConversationActionOptions(residentId);}

        // Semantic path lists only targets verified in the currently loaded scene.
        public List<NpcActionOption> ConversationActionCandidates(string residentId)
        {return BuildConversationActionOptions(residentId);}

        // The clause is passed to task callers for attribution only. Candidate
        // availability is intentionally independent of its wording.
        public List<NpcActionOption> ConversationActionCandidates(string residentId,string originalClause)
        {return ConversationActionCandidates(residentId);}

        private List<NpcActionOption> BuildConversationActionOptions(string residentId)
        {
            var result=new List<NpcActionOption>();
            var actor=FindActor(residentId);
            if(!Ready || !actor || !actor.gameObject.activeInHierarchy || !actor.Resident.IsAlive || actor.Resident.IsQuestProtected ||
                QuestOwned(actor.Resident) || actor.Resident.AgeGroup!=ResidentAgeGroup.Adult || IsAlarmed(residentId) && !CombatAlarmAllowsCandidate(actor))
            {
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-commerce-chat-probe")>=0)
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_OFFER_DENIED: actor; ready="+Ready+"; body="+(bool)actor+
                        (actor?"; active="+actor.gameObject.activeInHierarchy+"; alive="+actor.Resident.IsAlive+"; protected="+actor.Resident.IsQuestProtected+
                        "; quest="+QuestOwned(actor.Resident)+"; age="+actor.Resident.AgeGroup+"; alarm="+IsAlarmed(residentId):""));
                return result;
            }
            var player=GameManager.Instance.PlayerMotor.transform.position-Vector3.up;
            if(Vector3.Distance(actor.transform.position-Vector3.up,player)>8)
            {
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-commerce-chat-probe")>=0)
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_OFFER_DENIED: range; actor="+actor.transform.position.ToString("F3")+"; player="+player.ToString("F3"));
                return result;
            }
            if(IsAlarmed(residentId)) {OfferCombatActions(actor,null,result,true);return result;}
            return BuildSemanticActionCandidates(actor,result);
        }

        private List<NpcActionOption> BuildSemanticActionCandidates(ResidentActor actor,List<NpcActionOption> result)
        {
            OfferMurderReportAction(actor,result);
            OfferOwnedHealingItemAction(actor,null,result,true);
            OfferCompanionCastActions(actor,null,result,true);
            OfferCombatActions(actor,null,result,true);
            AddActionOption(result,NpcActionKind.Follow,new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},
                "follow the player locally through reachable building or native dungeon entrances; dungeon movement requires a verified native path",null,true);
            var paidOption=new NpcActionOption {Id=result.Count+1,Kind=NpcActionKind.Follow,PaidCompanion=true,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",Quantity=1},
                Label="hire this adventurer for one 24-game-hour day; wage depends on actual level"};
            int paidFee;string paidReason;
            if(TryQuotePaidCompanion(actor.Resident.Id,new NpcActionDecision {Option=paidOption,DurationSeconds=NpcPaidCompanionAction.DurationSeconds},out paidFee,out paidReason))
            {
                paidOption.Label="hire this adventurer for "+paidFee+" gold / 24 game hours to follow locally; no automatic renewal";
                result.Add(paidOption);
            }
            AddActionOption(result,NpcActionKind.Wait,ActionPosition(actor.transform.position-Vector3.up),"wait at this observed position",null,true);
            OfferBuildingExit(actor,result,null,true);
            AddActionOption(result,NpcActionKind.MoveTo,new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},
                "walk to the player's current position",null,true);

            int people=0;
            foreach(var pair in actors)
            {
                var other=pair.Value;
                if(pair.Key==actor.Resident.Id || !other || !other.gameObject.activeInHierarchy || !other.Resident.IsAlive ||
                    other.Resident.IsQuestProtected || QuestOwned(other.Resident) || IsAlarmed(pair.Key) ||
                    Vector3.Distance(actor.transform.position,other.transform.position)>20) continue;
                string name=other.Person?other.Person.NameNPC:other.Resident.Id;
                if(string.IsNullOrWhiteSpace(name)) continue;
                bool duplicate=false;
                foreach(var candidate in actors)
                {
                    var known=candidate.Value;
                    if(candidate.Key!=pair.Key && known && known.gameObject.activeInHierarchy &&
                        known.Person && string.Equals(known.Person.NameNPC,name,StringComparison.OrdinalIgnoreCase)) {duplicate=true;break;}
                }
                if(duplicate) continue;
                AddActionOption(result,NpcActionKind.Interact,new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId=pair.Key},
                    "approach and face "+name+" (no dialogue or transfer is included)",null,true);
                if(++people>=NpcActionCandidateCatalog.MaximumPerGroup || result.Count>=NpcActionCandidateCatalog.MaximumCandidates) break;
            }

            if(!interior && geometry!=null)
            {
                int buildings=0;
                // Keep the resident's own return door available even when it is
                // outside the first few entries in the native entrance table.
                AddSemanticBuildingCandidate(actor,result,actor.Resident.HomeKey,ref buildings);
                var plan=FindNpcActionPlan(actor.Resident.Id);
                if(plan!=null && plan.Routine!=null)
                {
                    if(plan.Routine.DestinationKey>=0)
                        AddSemanticBuildingCandidate(actor,result,plan.Routine.DestinationKey,ref buildings);
                    if(plan.Routine.Itinerary!=null && plan.Routine.Itinerary.Stops!=null)
                        foreach(var itinerary in ResidentRoutineDay.Itineraries(plan.Routine))
                    foreach(var stop in itinerary.Stops)
                        {
                            if(buildings>=NpcActionCandidateCatalog.MaximumPerGroup || result.Count>=NpcActionCandidateCatalog.MaximumCandidates) break;
                            if(stop!=null) AddSemanticBuildingCandidate(actor,result,stop.BuildingKey,ref buildings);
                        }
                }
                foreach(var pair in geometry.Entrances)
                {
                    if(result.Count>=NpcActionCandidateCatalog.MaximumCandidates || buildings>=NpcActionCandidateCatalog.MaximumPerGroup) break;
                    AddSemanticBuildingCandidate(actor,result,pair.Key,ref buildings);
                }
            }
            if(!travelerPlans.ContainsKey(actor.Resident.Id) && interior && indoorGeometry!=null && indoorGeometry.EntryGround.HasValue)
                AddActionOption(result,NpcActionKind.ChangeSchedule,ActionPosition(indoorGeometry.EntryGround.Value),
                    "leave this optional visit early and walk home",null,true);
            OfferSeatAction(actor,null,result,true);
            OfferBeerAction(actor,null,result,true);
            OfferNativeTempleTrainingAction(actor,null,result,true);
            OfferNativeRepairAction(actor,null,result,true);
            OfferInventoryActions(actor,null,result,false,true);
            OfferPlayerInventoryActions(actor,null,result,true);
            OfferNpcInventoryActions(actor,null,result,true);
            OfferHomeContainerDepositAction(actor,null,result,true);
            OfferHomeContainerRetrievalAction(actor,null,result,true);
            OfferObservedLawfulLootAction(actor,null,result);
            return result;
        }

        private void AddSemanticBuildingCandidate(ResidentActor actor,List<NpcActionOption> options,int buildingKey,ref int count)
        {
            if(count>=NpcActionCandidateCatalog.MaximumPerGroup || options.Count>=NpcActionCandidateCatalog.MaximumCandidates || geometry==null || !geometry.Entrances.ContainsKey(buildingKey)) return;
            foreach(var option in options)
                if(option.Kind==NpcActionKind.MoveTo && option.Target!=null && option.Target.Kind==NpcActionTargetKind.Building &&
                    option.Target.BuildingKey==buildingKey) return;
            BuildingSummary summary;
            if(!directory.GetBuildingSummary(buildingKey,out summary)) return;
            string name=GameManager.Instance.TalkManager.GetBuildingNameForBuildingKey(buildingKey);
            if(string.IsNullOrWhiteSpace(name)) return;
            var target=new NpcActionTarget {Kind=NpcActionTargetKind.Building,SettlementId=population.SettlementId,BuildingKey=buildingKey};
            if(!SemanticMoveTargetReachable(actor,target)) return;
            AddActionOption(options,NpcActionKind.MoveTo,target,"walk to the exterior entrance of "+name+" (building "+
                buildingKey.ToString(CultureInfo.InvariantCulture)+")",null,true);
            OfferBuildingEntry(actor,options,buildingKey,name,null,true);
            count++;
        }

        public bool TryCreateConversationBuildingMoveOption(string residentId,int settlementId,int buildingKey,
            string destinationName,out NpcActionOption option)
        {
            option=null;
            var actor=FindActor(residentId);
            if(!Ready || !actor || !actor.gameObject.activeInHierarchy || !actor.Resident.IsAlive ||
                actor.Resident.IsQuestProtected || QuestOwned(actor.Resident) || actor.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                IsAlarmed(residentId) || interior || geometry==null || population==null || settlementId!=population.SettlementId ||
                Vector3.Distance(actor.transform.position-Vector3.up,GameManager.Instance.PlayerMotor.transform.position-Vector3.up)>8)
                return false;
            BuildingSummary summary;
            if(buildingKey<0 || !directory.GetBuildingSummary(buildingKey,out summary) ||
                string.IsNullOrWhiteSpace(destinationName)) return false;
            var target=new NpcActionTarget {Kind=NpcActionTargetKind.Building,SettlementId=settlementId,BuildingKey=buildingKey,Quantity=1};
            if(!SemanticMoveTargetReachable(actor,target))return false;
            option=new NpcActionOption {Id=1,Kind=NpcActionKind.Lead,Target=target,
                Label="lead the player to the exterior entrance of "+destinationName};
            return true;
        }

        private bool SemanticMoveTargetReachable(ResidentActor actor,NpcActionTarget target)
        {
            var job=new NpcActionJob {ResidentId=actor.Resident.Id,Kind=NpcActionKind.MoveTo,Target=NpcActionExecutor.CopyTarget(target)};
            Vector3 point;float range;string reason;
            if(!ResolveActionTarget(job,out point,out range,out reason)) return false;
            var motion=new ActionMotion();
            return BuildActionRoute(actor,motion,point,range,out reason,preferNative:false);
        }
        private static void AddActionOption(List<NpcActionOption> options,NpcActionKind kind,NpcActionTarget target,string label,string text,bool semantic=true)
        {
            if(options.Count>=(semantic?NpcActionCandidateCatalog.MaximumCandidates:24)) return;
            options.Add(new NpcActionOption {Id=options.Count+1,Kind=kind,Target=target,Label=label});
        }

        public bool TryAcceptConversationAction(string residentId,string proposalId,NpcActionDecision decision,out string reason)
        {return TryAcceptConversationAction(residentId,proposalId,decision,null,null,out reason);}

        public bool TryAcceptConversationAction(string residentId,string proposalId,NpcActionDecision decision,
            NpcConversationCommandTurn commandTurn,out string reason)
        {
            if(commandTurn==null){reason="The player command is no longer current.";return false;}
            return TryAcceptConversationAction(residentId,proposalId,decision,null,commandTurn,out reason);
        }

        private bool TryAcceptConversationAction(string residentId,string proposalId,NpcActionDecision decision,
            NpcTaskPlan task,NpcConversationCommandTurn commandTurn,out string reason)
        {
            reason="No executable local action was selected.";
            bool healthTask=IsHealthCareTask(task);
            if(decision==null || decision.Option==null || decision.DurationSeconds<10 || decision.DurationSeconds>(healthTask?43200:900) || !Ready || worldStore==null) return false;
            if(decision.Option.PaidCompanion)
            {reason="Paid companion hire requires explicit player consent and native funding.";return false;}
            if(task==null && worldStore.ActiveNpcTask(residentId)!=null)
            {reason="The NPC is already carrying out an agreed task.";return false;}
            var actor=FindActor(residentId);var plan=FindNpcActionPlan(residentId);
            var kind=decision.Option.Kind;
            bool admitsThreatCombat=IsCombatAction(kind) && CombatAlarmAllowsAdmission(actor,decision.Option);
            if(!actor || plan==null || actor.IsSleeping && !healthTask || !actor.gameObject.activeInHierarchy || QuestOwned(actor.Resident) || IsAlarmed(residentId) && !admitsThreatCombat ||
                actor.Resident.AgeGroup!=ResidentAgeGroup.Adult || task==null && Vector3.Distance(actor.transform.position,GameManager.Instance.PlayerMotor.transform.position)>8)
            {reason="The NPC cannot take control of this local action now.";return false;}
            double now=ActionNow();
            if(kind==NpcActionKind.ChangeSchedule && IsTravelerActionPlan(plan))
            {reason="A traveler has no local household routine to abandon.";return false;}
            if(kind!=NpcActionKind.Follow && kind!=NpcActionKind.Wait && kind!=NpcActionKind.MoveTo && kind!=NpcActionKind.Enter && kind!=NpcActionKind.Exit && kind!=NpcActionKind.Lead && kind!=NpcActionKind.Interact && kind!=NpcActionKind.ReportMurder && kind!=NpcActionKind.ChangeSchedule && kind!=NpcActionKind.Purchase && kind!=NpcActionKind.TakeSeat && kind!=NpcActionKind.Transfer && !IsCombatAction(kind) &&
                !(healthTask && (kind==NpcActionKind.UseHealingItem || kind==NpcActionKind.RestToHeal || kind==NpcActionKind.CastSpell)) &&
                !(task==null && kind==NpcActionKind.UseHealingItem) &&
                !(task==null && kind==NpcActionKind.CastSpell && decision.Option.CompanionCast!=null))
            {reason="No native executor is available for that action.";return false;}
            var job=new NpcActionJob {Id="action:"+proposalId,ProposalId=proposalId,ResidentId=residentId,Kind=kind,
                Target=NpcActionExecutor.CopyTarget(decision.Option.Target),Phase=NpcActionPhase.Accepted,Revision=1,
                AcceptedAt=now,UpdatedAt=now,DeadlineAt=now+decision.DurationSeconds,
                LastPosition=ActionPosition(actor.transform.position-Vector3.up)};
            if(task==null && kind==NpcActionKind.CastSpell && decision.Option.CompanionCast!=null)
                job.CompanionCast=NpcCompanionCastSpec.CopyValidated(decision.Option.CompanionCast);
            if(kind==NpcActionKind.Follow || kind==NpcActionKind.Wait || kind==NpcActionKind.TakeSeat) {job.FinishAt=job.DeadlineAt;job.DeadlineAt+=60;}
            if(kind==NpcActionKind.RestToHeal) {job.RestDurationSeconds=decision.DurationSeconds;job.DeadlineAt=now+decision.DurationSeconds+600;}
            if(task!=null)
            {
                job.Id=NpcTaskContract.ActionId(task);job.ProposalId=job.Id;
                if(kind==NpcActionKind.CastSpell)
                    job.MagicHealing=NpcMagicHealingSpec.Copy(task.Steps[task.CurrentStepIndex].MagicHealing);
                job.DeadlineAt=Math.Min(job.DeadlineAt,task.DeadlineAt);
                if(job.FinishAt>=job.DeadlineAt) {reason="The agreed task has too little time remaining.";return false;}
            }
            if(kind==NpcActionKind.Purchase && !(IsNativeRepairTarget(job.Target)?PrepareNativeRepairAction(actor,job,out reason):
                IsNativeTempleTrainingTarget(job.Target)?PrepareNativeTempleTrainingAction(actor,job,out reason):PrepareBeerAction(actor,job,out reason))) {if(shopGeometry!=null) shopGeometry.ReleaseCounter(residentId);return false;}
            if(kind==NpcActionKind.UseHealingItem && !PrepareHealthHealingItem(actor,job,out reason))return false;
            if(kind==NpcActionKind.RestToHeal && !PrepareHealthRestAction(actor,job,out reason))return false;
            if(kind==NpcActionKind.CastSpell && !(job.CompanionCast!=null?
                PrepareCompanionCastAction(actor,job,out reason):PrepareHealthMagicAction(actor,job,out reason)))return false;
            if(IsCombatAction(kind) && !PrepareCombatAction(actor,job,out reason))return false;
            if(kind==NpcActionKind.Transfer && !(job.Target!=null && job.Target.TransferDirection==NpcActionTransferDirection.NpcToContainer?
                PrepareHomeContainerDepositAction(actor,job,out reason):job.Target!=null && job.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc?
                PrepareHomeContainerRetrievalAction(actor,job,out reason):job.Target!=null && job.Target.TransferDirection==NpcActionTransferDirection.ObservedLootToNpc?
                PrepareObservedLawfulLootAction(actor,job,out reason):PrepareInventoryAction(actor,job,out reason))) return false;
            if(kind==NpcActionKind.Transfer && task==null && IsCompanionHealthHandoff(actor,job,now))
                return TryCompleteCompanionHealthHandoff(actor,job,commandTurn,now,out reason);
            if(kind==NpcActionKind.Lead && (interior || job.Target==null ||
                job.Target.Kind!=NpcActionTargetKind.Building || job.Target.SettlementId!=population.SettlementId))
            {reason="Lead requires one verified building entrance in this settlement.";return false;}
            if(commandTurn!=null)
            {
                NpcActionJob prepared;
                if(!worldStore.TryPrepareNpcConversationAction(commandTurn,job,kind==NpcActionKind.ChangeSchedule,out prepared))
                {reason="The player command is no longer current.";if(shopGeometry!=null)shopGeometry.ReleaseCounter(residentId);return false;}
                job=prepared;
            }
            Vector3 target;float range;
            if(!ResolveActionTarget(job,out target,out range,out reason)) return false;
            var motion=new ActionMotion {JobId=job.Id,LastProgress=actor.transform.position,ProgressAt=Time.unscaledTime};
            if(kind==NpcActionKind.TakeSeat && !AcquireActionSeat(job,actor,motion,out reason)) return false;
            if(!BuildActionRoute(actor,motion,target,range,out reason,kind==NpcActionKind.Purchase)) {if(motion.Seat!=null) motion.Seat.Release();if(shopGeometry!=null) shopGeometry.ReleaseCounter(residentId);return false;}
            if(healthTask && task!=null)
            {
                double travelSeconds=motion.Indoor==null?0:motion.Indoor.Duration;
                double treatmentSeconds=kind==NpcActionKind.RestToHeal?job.RestDurationSeconds:0;
                job.DeadlineAt=Math.Min(task.DeadlineAt,Math.Max(job.DeadlineAt,now+travelSeconds+treatmentSeconds+600));
                if(job.DeadlineAt<=now+travelSeconds+treatmentSeconds)
                {reason="The recovery task cannot cover the verified route and treatment time.";return false;}
            }
            if(!healthTask && !ActionReturnFitsFixedCommitments(plan,actor,job,target,range,now,out reason))
            {if(motion.Seat!=null) motion.Seat.Release();if(shopGeometry!=null) shopGeometry.ReleaseCounter(residentId);return false;}
            if(kind==NpcActionKind.ChangeSchedule)
            {
                var state=Scheduled(plan,now);
                if(!interior || state.Activity!=Activity.Visit || plan.Resident.HomeKey==interior.EntryDoor.buildingKey ||
                    !ActiveSchedule(plan).ContainsVisitBuilding(interior.EntryDoor.buildingKey,now))
                {reason="Only an optional local visit may end early.";return false;}
                var outside=DailyReturnRoute(plan.Routine,interior.EntryDoor.buildingKey);
                if(outside==null || outside.Count<2 || motion.Indoor==null) {reason="No verified walking route home exists.";return false;}
                var remaining=motion.Indoor.RemainingRoute(interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up));
                var departure=VisitDeparture.Create(residentId,population.SettlementId,plan.Resident.HomeKey,interior.EntryDoor.buildingKey,
                    now,(Math.Floor(now/86400)+1)*86400,remaining,outside,actor.WalkingSpeed,DaggerfallUnity.Instance.WorldTime.TimeScale);
                bool departureAccepted;
                if(commandTurn==null)departureAccepted=worldStore.TryAcceptNpcDeparture(job,departure,true,out reason);
                else
                {
                    NpcConversationCommandCapability capability;
                    departureAccepted=worldStore.TryIssueNpcConversationDeparture(commandTurn,job,departure,out capability) &&
                        worldStore.TryAcceptNpcDepartureFromConversation(capability,job,departure,true,out reason);
                    if(!departureAccepted && capability==null)reason="The player command is no longer current.";
                }
                if(!departureAccepted) return false;
                RemovePhysicalShopVisit(residentId);worldStore.RemoveObservedVisit(residentId);
            }
            else
            {
                var state=Scheduled(plan,now);
                var future=Scheduled(plan,job.DeadlineAt);
                bool optional=healthTask || (state.Activity==Activity.Visit || state.Activity==Activity.Travel) && future.Activity!=Activity.Work && future.Activity!=Activity.Sleep;
                bool authorizedHomeAction=AwakeHomeActionFitsWindow(plan,actor,job,now);
                bool accepted;
                if(task!=null)accepted=worldStore.TryDispatchNpcTaskStep(task.Id,task.Revision,now,job,true,true,
                    optional || authorizedHomeAction,out reason);
                else if(commandTurn==null)accepted=worldStore.TryAcceptNpcAction(job,true,true,
                    optional || authorizedHomeAction,out reason);
                else
                {
                    NpcConversationCommandCapability capability;
                    accepted=worldStore.TryIssueNpcConversationAction(commandTurn,job,out capability) &&
                        worldStore.TryAcceptNpcActionFromConversation(capability,job,true,true,
                            optional || authorizedHomeAction,out reason);
                    if(!accepted && capability==null)reason="The player command is no longer current.";
                }
                if(!accepted) {if(motion.Seat!=null) motion.Seat.Release();if(shopGeometry!=null) shopGeometry.ReleaseCounter(residentId);return false;}
            }
            PhysicalShopVisit previousVisit;
            if(kind!=NpcActionKind.Wait && kind!=NpcActionKind.UseHealingItem && kind!=NpcActionKind.RestToHeal &&
                kind!=NpcActionKind.CastSpell && physicalShopVisits.TryGetValue(residentId,out previousVisit))
                motion.DepartureSeat=previousVisit.TakeSeatForDeparture(actor);
            RemovePhysicalShopVisit(residentId);
            if(kind==NpcActionKind.Follow)worldStore.RemoveObservedVisit(residentId);
            if(motion.Seat!=null) motion.Seat.Refresh();
            actionMotions[residentId]=motion;
            reason="Accepted and saved. Movement starts when the conversation closes; arrival has not happened yet.";
            return true;
        }

        private bool ResolveActionTarget(NpcActionJob job,out Vector3 target,out float arrival,out string reason)
        {
            target=Vector3.zero;arrival=0.25f;reason="Target is not available in this loaded place.";
            if(job.Target==null) return false;
            if(job.Kind==NpcActionKind.ReportMurder)
                return ResolveMurderReportGuard(job,out target,out arrival,out reason);
            if(IsCombatAction(job.Kind))
            {
                var enemy=NativeNpcCombatAssist.Find(job.Target.PersonId);
                if(!enemy || enemy.Entity==null || enemy.Entity.CurrentHealth<=0)return false;
                target=enemy.transform.position-Vector3.up;arrival=NativeNpcCombatAssist.MeleeRange;return true;
            }
            if(job.Kind==NpcActionKind.Enter || job.Kind==NpcActionKind.Exit)
                return ResolveBuildingAction(job,FindActor(job.ResidentId),out target,out reason);
            // A Wait bound to an Enter keeps the public door's exact identity in
            // its saved target. It becomes executable only after the matching
            // native interior is loaded, where the surveyed entry ground is the
            // only observed destination available to it.
            if(job.Kind==NpcActionKind.Wait && job.Target.Kind==NpcActionTargetKind.Building && interior &&
                job.Target.SettlementId==population.SettlementId && job.Target.BuildingKey==interior.EntryDoor.buildingKey &&
                job.Target.DoorId==BuildingActionDoorId(interior.EntryDoor) && job.Target.WorldSpaceId==ActionWorldSpace &&
                job.Target.DestinationSpaceId==BuildingActionCurrentSpace && indoorGeometry!=null && indoorGeometry.EntryGround.HasValue)
            {target=indoorGeometry.EntryGround.Value;return true;}
            if(job.Kind==NpcActionKind.TakeSeat)
            {
                var catalog=ActionSeatCatalog();var seat=catalog?catalog.Find(job.Target.ContainerId):null;
                if(seat==null || !seat.HasApproach || !ActionSpaceMatches(job.Target)) return false;
                target=seat.Approach;return true;
            }
            if(job.ServiceCode==BeerService && job.ServiceStep==1)
            {target=GameManager.Instance.PlayerMotor.transform.position-Vector3.up;arrival=1.35f;return true;}
            if(job.Target.Kind==NpcActionTargetKind.Person)
            {
                if(job.Target.PersonId=="player") target=GameManager.Instance.PlayerMotor.transform.position-Vector3.up;
                else
                {
                    var other=FindActor(job.Target.PersonId);
                    if(!other || !other.gameObject.activeInHierarchy || !other.Resident.IsAlive || QuestOwned(other.Resident)) return false;
                    target=other.transform.position-Vector3.up;
                }
                arrival=job.Kind==NpcActionKind.Follow?1.8f:1.35f;
                return true;
            }
            if(job.Target.Kind==NpcActionTargetKind.Building && !interior && job.Target.SettlementId==population.SettlementId)
            {
                TownGeometry.Entrance entrance;
                if(!geometry.Entrances.TryGetValue(job.Target.BuildingKey,out entrance)) return false;
                // The route graph ends at the doorway's ground projection.
                // SceneDoor preserves the native door object's vertical center
                // (about a metre above the floor), which MoveTo cannot reach
                // with its ordinary ground-level arrival radius.
                target=geometry.ScenePoint(new RoutePoint(entrance.LocalDoor.x,entrance.LocalDoor.z));return true;
            }
            if(!ActionSpaceMatches(job.Target)) return false;
            target=ActionWorldPoint(job.Target);return true;
        }

        private bool BuildActionRoute(ResidentActor actor,ActionMotion motion,Vector3 target,float range,out string reason,bool counter=false,
            bool preferNative=true)
        {
            reason="No safe local walking route reaches the target.";
            motion.WaypointProgress=new NpcActionWaypointProgress();
            motion.Indoor=null;motion.Outdoor=null;motion.NativeOutdoor=false;
            var actual=actor.transform.position-Vector3.up;motion.Goal=target;motion.RepathAt=Time.unscaledTime+1.5f;
            if(Vector3.Distance(actual,target)<=range) {motion.Indoor=null;motion.Outdoor=null;return true;}
            Vector3 destination=target;
            if(range>0.5f)
            {
                Vector3 away=actual-target;away.y=0;
                if(away.sqrMagnitude>0.001f) destination=target+away.normalized*(range*0.75f);
            }
            if(interior)
            {
                var route=range>0.5f?IndoorPersonApproachRoute(actual,target,destination,range):
                    counter && shopGeometry!=null?shopGeometry.CounterRouteBetween(interior.transform.InverseTransformPoint(actual),interior.transform.InverseTransformPoint(destination)):
                    shopGeometry!=null?shopGeometry.ActivityRouteBetween(interior.transform.InverseTransformPoint(actual),interior.transform.InverseTransformPoint(destination)):
                    indoorGeometry.RouteBetween(interior.transform.InverseTransformPoint(actual),interior.transform.InverseTransformPoint(destination));
                if(route==null || route.Count<2) return false;
                double length=0;for(int i=1;i<route.Count;i++) {var a=route[i-1];var b=route[i];length+=Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)+(a.Z-b.Z)*(a.Z-b.Z));}
                if(length<0.01) {motion.Indoor=null;motion.Outdoor=null;return true;}
                motion.Indoor=new InteriorRoomFollower(route,DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,0.2f);
            }
            else
            {
                var from=directory.transform.InverseTransformPoint(actual);var to=directory.transform.InverseTransformPoint(destination);
                motion.Outdoor=geometry.RouteBetweenPoints(new SavedRoutePoint {X=from.x,Z=from.z},new SavedRoutePoint {X=to.x,Z=to.z});
                if(motion.Outdoor.Count<2) {motion.Outdoor=null;return false;}motion.Next=1;
                // Other callers reuse ActionMotion for approaches with their own
                // tighter arrival tests. Keep those on their existing graph route.
                bool nativeCandidate=range<=0.5f &&
                    (motion.JobId!=null || medicalMotions.ContainsValue(motion));
                if(preferNative && nativeCandidate && !motion.NativeFailed && NativeActionPathComplete(actual,destination))
                {motion.NativeOutdoor=true;motion.NativeDestination=destination;}
            }
            return true;
        }

        private static bool NativeActionPathComplete(Vector3 actual,Vector3 destination)
        {
            var world=NativeNavigationWorld.Current;
            if(!world || !world.Ready)return false;
            NavMeshHit start,end;
            if(!NavMesh.SamplePosition(actual,out start,2,NavMesh.AllAreas) ||
                Vector3.Distance(actual,start.position)>0.55f ||
                !NavMesh.SamplePosition(destination,out end,0.4f,NavMesh.AllAreas))return false;
            var path=new NavMeshPath();
            return NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) &&
                path.status==NavMeshPathStatus.PathComplete;
        }

        // Called once per existing plan before schedule visibility/despawn or placement.
        // It owns only this existing actor; all movement still passes through CrowdMotion.
        private bool ApplyNpcActionOverlay(Plan plan,ref ResidentActor actor,double now,AlarmPerson nativeReaction)
        {
            if(roadTravelActors && roadTravelActors.OwnsDungeonFollow(plan.Resident.Id))return true;
            RetryPaidCompanionSettlements();
            bool nativeOwnsMovement=nativeReaction!=null || IsAlarmed(plan.Resident.Id);
            if(!IsTravelerActionPlan(plan) && !IsPaidDoorActionPlan(plan))ObserveHomeSleep(plan,actor,now);
            if(!nativeOwnsMovement && TryTreatNpcConditions(actor,now)){CancelCashForCare(plan.Resident.Id);return true;}
            if(ApplyNpcMedicalVisit(plan,ref actor,now,nativeOwnsMovement)){CancelCashForCare(plan.Resident.Id);return true;}
            bool healthOwnsRoutine=!nativeOwnsMovement && PrepareNpcHealthCare(plan,ref actor,now);
            if(healthOwnsRoutine)CancelCashForCare(plan.Resident.Id);
            if(!healthOwnsRoutine && ApplyNpcCashVisit(plan,ref actor,now,nativeOwnsMovement))return true;
            // Reconcile completed effects and deliver failures during danger, but
            // never dispatch the next Ready step before native reaction takes movement.
            AdvanceConversationTask(plan.Resident.Id,actor,now,!nativeOwnsMovement);
            var job=worldStore.ActiveNpcAction(plan.Resident.Id);
            if(IsPaidDoorActionPlan(plan))
            {
                var paidDoorJob=job??ReadPaidDoorAction(plan);
                if(PaidDoorPlanNeedsRetirement(paidDoorJob,now))
                    return RetirePaidDoorPlan(plan,ref actor,paidDoorJob,now);
                job=paidDoorJob;
            }
            if(job==null) {ReleaseActionSeat(plan.Resident.Id);actionMotions.Remove(plan.Resident.Id);
                bool retryDue=false;
                if(!nativeOwnsMovement && !healthOwnsRoutine && plan.Resident.IsAlive &&
                    !plan.Resident.IsQuestProtected && !QuestOwned(plan.Resident) &&
                    !worldStore.IsResidentAway(plan.Resident.Id,now))
                {
                    var care=worldStore.ReadNpcHealthCare(plan.Resident.Id);
                    retryDue=care!=null && care.Phase==NpcHealthCarePhase.Blocked &&
                        care.PausedPosition!=null && now>=care.RetryAt &&
                        worldStore.ActiveNpcTask(plan.Resident.Id)==null;
                }
                if(nativeOwnsMovement || !healthOwnsRoutine && !retryDue)return false;
                bool held=HoldNpcHealthCare(plan,ref actor,now);
                return held && (healthOwnsRoutine || actor);}
            if(!plan.Resident.IsAlive || plan.Resident.IsQuestProtected || QuestOwned(plan.Resident) || nativeOwnsMovement ||
                worldStore.IsResidentAway(plan.Resident.Id,now) && !(job.PaidTravelHandoff!=null &&
                    NpcPaidCompanionTravel.TryReadActiveTravel(worldStore,job,now,out _)))
            {
                EndAction(job,now,"Danger, native ownership, travel, or scene change interrupted this action.",false);
                ReleaseActionSeat(plan.Resident.Id);actionMotions.Remove(plan.Resident.Id);return false;
            }
            if(ApplyNpcFollowTransition(ref job,now))return true;
            if(HoldBuildingActionLanding(ref job,now))return true;
            if(ApplyNpcRoutineRecoveryTransition(ref job,ref actor,now))return true;
            if(!ActionSpaceMatches(job.LastPosition))
            {
                if(job.ReturningToRoutine)return true;
                EndAction(job,now,"The action cannot continue in this scene.",false);ReleaseActionSeat(plan.Resident.Id);actionMotions.Remove(plan.Resident.Id);return false;
            }
            // Timed following completes through the observed arrival branch below.
            // Expiry alone is neither a failure nor proof the actor reached the player.
            if(NpcActionExecutor.IsTerminal(job) && !job.ReturningToRoutine)
            {actionMotions.Remove(plan.Resident.Id);return healthOwnsRoutine && HoldNpcHealthCare(plan,ref actor,now);}
            if(!actor)
            {
                var position=ActionWorldPoint(job.LastPosition);
                if(Vector3.Distance(position,GameManager.Instance.PlayerMotor.transform.position)>140 || actors.Count>=80) return false;
                var go=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,interior?interior.transform:directory.transform);
                go.name="People of the Iliac Bay "+plan.Resident.Id;actor=go.AddComponent<ResidentActor>();
                actor.Initialize(plan.Resident,plan.SurnameSeed,navigation,onDeath:TravelerActionDeathHandler(plan),inventoryStore:worldStore);actor.SetRunningPilotAtlas(runningPilotAtlas);
                var crowd=actor.GetComponent<CrowdMotion>();if(crowd) crowd.RestorePlacement(position);else actor.transform.position=position+Vector3.up;
                actors[plan.Resident.Id]=actor;go.SetActive(true);
            }
            if(actor.IsConversing && job.Kind==NpcActionKind.RestToHeal && job.Phase==NpcActionPhase.Executing)
            {EndAction(job,now,"Conversation interrupted the continuous rest period.",false);return true;}
            ActionMotion motion;
            if(!actionMotions.TryGetValue(plan.Resident.Id,out motion) || motion.JobId!=job.Id)
            {motion=new ActionMotion {JobId=job.Id,LastProgress=actor.transform.position,ProgressAt=Time.unscaledTime};actionMotions[plan.Resident.Id]=motion;}
            if(GameManager.IsGamePaused || actor.IsConversing)
            {motion.MovementPaused=true;return true;}
            if(motion.MovementPaused)
            {
                // Conversation and game pauses are not blocked movement. Keep the
                // route and waypoint, but start fresh progress observation on resume.
                motion.MovementPaused=false;motion.ProgressAt=Time.unscaledTime;
                motion.LastProgress=actor.transform.position;motion.CheckAt=0;
                motion.WaypointProgress=new NpcActionWaypointProgress();
            }
            motion.Urgent=job.Kind==NpcActionKind.ReportMurder && !NpcActionExecutor.IsTerminal(job);
            if(motion.DepartureSeat!=null)
            {
                if(!LeaveRoutineSeatForAction(actor,job,motion,now))return true;
                job=worldStore.ReadNpcAction(job.Id);
            }
            if(IsCombatAction(job.Kind) && !NpcActionExecutor.IsTerminal(job))
                return ApplyCombatAction(actor,job,motion,now);
            NpcLeadProgress leadProgress=NpcLeadProgress.Walking;
            if(job.Kind==NpcActionKind.Lead && !NpcActionExecutor.IsTerminal(job))
            {
                leadProgress=ObserveLeadProgress(job,actor,now);
                if(leadProgress==NpcLeadProgress.Invalid || leadProgress==NpcLeadProgress.Expired)
                {EndAction(job,now,leadProgress==NpcLeadProgress.Expired?"The action deadline expired.":
                    "The saved guide destination is no longer valid in this scene.",true);return true;}
                if(leadProgress==NpcLeadProgress.WaitingForPlayer || leadProgress==NpcLeadProgress.Unobserved)
                {
                    motion.ProgressAt=Time.unscaledTime;motion.LastProgress=actor.transform.position;
                    actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);
                    if(leadProgress==NpcLeadProgress.WaitingForPlayer && Time.unscaledTime>=motion.SaveAt)
                    {motion.SaveAt=Time.unscaledTime+0.5f;
                        worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actor.transform.position-Vector3.up),false,now);}
                    return true;
                }
            }
            if(NpcActionExecutor.IsTerminal(job))
            {
                if(healthOwnsRoutine)return HoldNpcHealthCare(plan,ref actor,now);
                return ReturnActionToRoutine(plan,actor,job,motion,now);
            }
            if(job.Kind==NpcActionKind.TakeSeat)
            {
                string failure;
                if(motion.Seat==null && !AcquireActionSeat(job,actor,motion,out failure)) {EndAction(job,now,failure,true);return true;}
                if(!motion.Seat.Refresh()) {EndAction(job,now,"The selected seat reservation was lost.",true);return true;}
            }
            if(Time.unscaledTime>=motion.CheckAt)
            {
                motion.CheckAt=Time.unscaledTime+0.5f;
                if(now>=job.DeadlineAt) {EndAction(job,now,"The action deadline expired.",true);return true;}
                if(Vector3.Distance(actor.transform.position,motion.LastProgress)>=0.15f)
                {motion.LastProgress=actor.transform.position;motion.ProgressAt=Time.unscaledTime;}
                Vector3 goal;float range;string failure;
                if(!ResolveActionTarget(job,out goal,out range,out failure)) {EndAction(job,now,failure,true);return true;}
                bool nativeRoute=!interior && motion.NativeOutdoor && motion.Outdoor!=null;
                // The native endpoint may be sampled 0.4 m from the exact target,
                // and its agent stops another 0.1 m short of that endpoint.
                float arrivalRange=nativeRoute?Mathf.Max(range,0.55f):range;
                bool arrived=Vector3.Distance(actor.transform.position-Vector3.up,goal)<=arrivalRange &&
                    (job.Target.Kind!=NpcActionTargetKind.Person && !(job.ServiceCode==BeerService && job.ServiceStep==1) || CivilianResponses.Sees(actor.transform.position,goal+Vector3.up,arrivalRange+1));
                // Walking already proves the guide and player are valid and close;
                // the native standing point can be just outside the exact radius.
                if(job.Kind==NpcActionKind.Lead)arrived=arrived &&
                    (leadProgress==NpcLeadProgress.Arrived || nativeRoute && leadProgress==NpcLeadProgress.Walking);
                if(job.Kind==NpcActionKind.RestToHeal && IsHealthRestPoseAtTarget(job,actor,now))arrived=true;
                if(job.Kind==NpcActionKind.TakeSeat)
                {
                    if(arrived || Vector3.Distance(actor.transform.position-Vector3.up,motion.Seat.Slot.Ground)<0.15f) motion.SeatEntry=true;
                    arrived=motion.SeatEntry && Vector3.Distance(actor.transform.position-Vector3.up,motion.Seat.Slot.Ground)<0.10f;
                    if(motion.SeatEntry) {motion.Indoor=null;motion.Outdoor=null;motion.NativeOutdoor=false;}
                }
                if(arrived)
                {
                    motion.ProgressAt=Time.unscaledTime;
                    if(job.Phase==NpcActionPhase.Accepted || job.Phase==NpcActionPhase.Approaching)
                    {worldStore.ObserveNpcAction(job.Id,job.Revision,now,NpcActionObservation.Arrival());job=worldStore.ReadNpcAction(job.Id);}
                    if((job.Kind!=NpcActionKind.Follow && job.Kind!=NpcActionKind.Wait && job.Kind!=NpcActionKind.TakeSeat && job.Kind!=NpcActionKind.RestToHeal) || job.FinishAt>0 && now>=job.FinishAt)
                    {
                        if(job.Kind==NpcActionKind.Enter || job.Kind==NpcActionKind.Exit)
                        {
                            if(!ExecuteBuildingAction(job,ref actor,now,out failure))EndAction(job,now,failure,true);
                            return true;
                        }
                        if(job.Kind==NpcActionKind.Purchase)
                        {
                            if(job.ServiceCode==NpcShopCommerce.ServiceCode)
                            {if(!ExecuteShopPurchaseAction(job,actor,now,out failure)){EndAction(job,now,failure,true);return true;}}
                            else if(job.ServiceCode==NpcShopLootSale.ServiceCode)
                            {if(!ExecuteNativeLootSaleAction(job,actor,now,out failure)){EndAction(job,now,failure,true);return true;}}
                            else if(job.ServiceCode==NpcShopCommerce.RepairServiceCode)
                            {if(!ExecuteNativeRepairAction(job,actor,now,out failure)){EndAction(job,now,failure,true);return true;}}
                            else if(job.ServiceCode==NativeNpcTempleTraining.ServiceCode)
                            {bool completed;if(!ExecuteNativeTempleTrainingAction(job,actor,now,out completed,out failure)){EndAction(job,now,failure,true);return true;}if(!completed)return true;}
                            else
                            {
                                bool completed;
                                if(!ExecuteBeerStage(job,actor,motion,now,out completed,out failure)) {EndAction(job,now,failure,true);return true;}
                                if(!completed) return true;
                            }
                        }
                        // Capture the display name before a whole-stack transfer removes its source UID.
                        string itemName=ActionItemName(job,actor);
                        if(job.Kind==NpcActionKind.UseHealingItem && !NativeNpcHealingItem.TryExecute(worldStore,job,actor.gameObject,now,out failure))
                        {EndAction(job,now,failure,true);return true;}
                        if(job.Kind==NpcActionKind.RestToHeal && !ExecuteHealthRestAction(job,actor,now,out failure))
                        {EndAction(job,now,failure,true);return true;}
                        if(job.Kind==NpcActionKind.CastSpell)
                        {
                            var castingActor=actor;
                            if(!(job.CompanionCast!=null?ExecuteCompanionCastAction(castingActor,job,now,out failure):
                                NativeNpcMagicHealing.TryExecute(worldStore,job,castingActor.gameObject,now,
                                ()=>HealthMagicDutyStillAvailable(castingActor,job,now),out failure)))
                            {EndAction(job,now,failure,true);return true;}
                        }
                        if(job.Kind==NpcActionKind.Transfer && !ExecuteInventoryAction(job,actor,now,out failure))
                        {EndAction(job,now,failure,true);return true;}
                        if(job.Kind==NpcActionKind.ReportMurder && !DeliverMurderReport(job,actor,now,out failure))
                        {EndAction(job,now,failure,true);return true;}
                        if(job.Kind==NpcActionKind.ChangeSchedule) ReanchorActionDeparture(plan,actor,motion,now,true);
                        if(job.Kind!=NpcActionKind.TakeSeat) actor.Place(actor.transform.position-Vector3.up,goal-(actor.transform.position-Vector3.up),true);
                        if(job.Kind!=NpcActionKind.ReportMurder && job.Kind!=NpcActionKind.Purchase && job.Kind!=NpcActionKind.Transfer && job.Kind!=NpcActionKind.UseHealingItem && job.Kind!=NpcActionKind.RestToHeal && job.Kind!=NpcActionKind.CastSpell && !worldStore.ObserveNpcAction(job.Id,job.Revision,now,NpcActionObservation.Success(job.Id+":observed"))) return true;
                        job=worldStore.ReadNpcAction(job.Id);
                        RememberActionOutcome(job,now);
                        string settlementError;
                        if(!TrySettlePaidCompanionAction(job,NpcServiceEndReason.NaturalExpiry,out settlementError))
                            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_SETTLEMENT: "+settlementError);
                        NotifyActionOutcome(job,actor,itemName);
                        worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actor.transform.position-Vector3.up),job.Kind!=NpcActionKind.ChangeSchedule,now);
                        return true;
                    }
                    motion.Indoor=null;motion.Outdoor=null;motion.NativeOutdoor=false;
                    if(job.Kind==NpcActionKind.RestToHeal)UpdateHealthRestPose(job,actor,now);
                    else if(job.Kind!=NpcActionKind.TakeSeat) actor.Place(actor.transform.position-Vector3.up,goal-(actor.transform.position-Vector3.up),true);
                }
                else
                {
                    if(Time.unscaledTime-motion.ProgressAt>20 || !nativeRoute && motion.Outdoor!=null && motion.WaypointProgress.IsStalled(Time.unscaledTime,20))
                    {EndAction(job,now,"The walking route remained blocked.",true);return true;}
                    // A moving person can leave arrival range after a small step.
                    // Reaching the old approach endpoint must allow a fresh route even below the target-change threshold.
                    if(!motion.SeatEntry && Time.unscaledTime>=motion.RepathAt &&
                        (motion.Indoor==null && motion.Outdoor==null || motion.Indoor!=null && motion.Indoor.Arrived ||
                        !nativeRoute && motion.Outdoor!=null && motion.Next>=motion.Outdoor.Count ||
                        nativeRoute && Vector3.Distance(actor.transform.position-Vector3.up,motion.NativeDestination)<=0.55f ||
                        Vector3.Distance(goal,motion.Goal)>1))
                        if(!BuildActionRoute(actor,motion,goal,range,out failure,job.ServiceCode==BeerService && job.ServiceStep==0)) {EndAction(job,now,failure,true);return true;}
                    if(job.Phase==NpcActionPhase.Accepted || job.Phase==NpcActionPhase.Executing)
                    {worldStore.ObserveNpcAction(job.Id,job.Revision,now,NpcActionObservation.Approach());job=worldStore.ReadNpcAction(job.Id);}
                }
                if(Time.unscaledTime>=motion.SaveAt)
                {
                    motion.SaveAt=Time.unscaledTime+0.5f;
                    worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actor.transform.position-Vector3.up),false,now);
                    if(job.Kind==NpcActionKind.ChangeSchedule) ReanchorActionDeparture(plan,actor,motion,now,false);
                }
            }
            AdvanceActionMotion(actor,motion);return true;
        }
        private void AdvanceActionMotion(ResidentActor actor,ActionMotion motion)
        {
            if(motion.Seat!=null && motion.SeatEntry) {motion.Seat.Advance(actor);return;}
            if(interior && motion.Indoor!=null) {motion.Indoor.Advance(actor,interior,indoorGeometry,Time.unscaledTime);return;}
            if(motion.Outdoor==null) return;
            var actual=actor.transform.position-Vector3.up;
            if(motion.NativeOutdoor && !interior)
            {
                if(GameManager.IsGamePaused || actor.IsConversing)return;
                var world=NativeNavigationWorld.Current;
                if(world && world.Ready)
                {
                    // Keep one goal while the native agent follows its own path.
                    // Passing the current foot position also keeps Place's crowd
                    // fallback stationary if the native path fails this frame.
                    actor.ResumeFromSocialPause();
                    actor.Place(actual,motion.NativeDestination-actual,waitAtOccupiedTarget:true,
                        navigationDestination:motion.NativeDestination);
                    var native=actor.GetComponent<NativeResidentNavigation>();
                    // TryMove.Fail pauses the agent; a prior FailureReason can
                    // remain after a later collision wait, so inspect the agent.
                    if(native && native.Active && native.HasDestination &&
                        (native.Destination-motion.NativeDestination).sqrMagnitude<0.01f)return;
                }
                // Rebuild the existing guarded graph route from the body's new
                // position on the next check; do not keep an obsolete waypoint.
                motion.NativeOutdoor=false;motion.NativeFailed=true;motion.Outdoor=null;
                motion.RepathAt=Time.unscaledTime;motion.ProgressAt=Time.unscaledTime;
                motion.LastProgress=actor.transform.position;
                return;
            }
            while(motion.Next<motion.Outdoor.Count && Vector3.Distance(actual,geometry.ScenePoint(new RoutePoint(motion.Outdoor[motion.Next].X,motion.Outdoor[motion.Next].Z)))<0.2f) motion.Next++;
            if(motion.Next>=motion.Outdoor.Count) return;
            var next=motion.Outdoor[motion.Next];var target=geometry.ScenePoint(new RoutePoint(next.X,next.Z));
            motion.WaypointProgress.Observe(motion.Next,Vector3.Distance(actual,target),Time.unscaledTime);
            if(motion.WaypointProgress.IsStalled(Time.unscaledTime,1) && Time.unscaledTime>=motion.NextWaypointRejoinAt)
            {
                motion.NextWaypointRejoinAt=Time.unscaledTime+1.5f;
                var crowd=actor.GetComponent<CrowdMotion>();
                if(crowd)
                {
                    // Only intermediate guidance can be bypassed. Every offered
                    // rejoin is checked against actual support and swept bodies;
                    // the terminal destination and its arrival test stay exact.
                    var window=new List<GuardPoint3>();
                    for(int i=motion.Next;i<motion.Outdoor.Count && window.Count<5;i++)
                    {
                        var point=motion.Outdoor[i];var world=geometry.ScenePoint(new RoutePoint(point.X,point.Z));
                        window.Add(new GuardPoint3 {X=world.x,Y=world.y,Z=world.z});
                    }
                    int selected;
                    if(NpcActionWaypointRejoin.TrySelect(window,0,new GuardPoint3 {X=actual.x,Y=actual.y,Z=actual.z},
                        point=>crowd.CanTraverseTo(new Vector3((float)point.X,(float)point.Y,(float)point.Z)),out selected))
                    {
                        int previous=motion.Next;motion.Next+=selected;
                        next=motion.Outdoor[motion.Next];target=geometry.ScenePoint(new RoutePoint(next.X,next.Z));
                        motion.WaypointProgress.Observe(motion.Next,Vector3.Distance(actual,target),Time.unscaledTime);
                        motion.ProgressAt=Time.unscaledTime;motion.LastProgress=actor.transform.position;
                        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-move-to-wait-probe")>=0)
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOVE_TO_WAIT_REJOIN: resident="+actor.Resident.Id+"; fromIndex="+previous+
                                "; toIndex="+motion.Next+"; swept-clear=true; final-target-unchanged=true.");
                    }
                }
            }
            actor.ResumeFromSocialPause();actor.Place(target,target-actual,fleeing:motion.Urgent,waitAtOccupiedTarget:true);
        }
        private void EndAction(NpcActionJob job,double now,string reason,bool returnToRoutine)
        {
            if(shopGeometry!=null) shopGeometry.ReleaseCounter(job.ResidentId);
            bool changed=false;
            if(!NpcActionExecutor.IsTerminal(job))
            {changed=worldStore.ObserveNpcAction(job.Id,job.Revision,now,NpcActionObservation.Interrupted(reason));job=worldStore.ReadNpcAction(job.Id);}
            var actor=FindActor(job.ResidentId);
            if(changed) NotifyActionOutcome(job,actor,null);
            if(changed) RememberActionOutcome(job,now);
            string settlementError;
            if(NpcActionExecutor.IsTerminal(job) &&
                !TrySettlePaidCompanionAction(job,NpcServiceEndReason.NpcResigned,out settlementError))
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_SETTLEMENT: "+settlementError);
            if(actor && ActionSpaceMatches(job.LastPosition))
                worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actor.transform.position-Vector3.up),returnToRoutine,now);
            else if(job.ReturningToRoutine) worldStore.SaveNpcActionPosition(job.Id,job.Revision,job.LastPosition,false,now);
        }
        private bool ReturnActionToRoutine(Plan plan,ResidentActor actor,NpcActionJob job,ActionMotion motion,double now)
        {
            if(!job.ReturningToRoutine) return false;
            if(!LeaveActionSeat(actor,motion)) return true;
            if(IsTravelerActionPlan(plan))return ReturnTravelerActionToRoutine(plan,actor,job,motion,now);
            if(TryReleaseReturningAtOwnHome(plan,actor,job,now))return true;
            if(motion.RecoveryWaiting && Time.unscaledTime<motion.RepathAt)
            {actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);return true;}
            if(motion.RecoveryWaiting)
            {motion.RecoveryWaiting=false;motion.NativeOutdoor=false;motion.ProgressAt=Time.unscaledTime;motion.LastProgress=actor.transform.position;}
            if(Vector3.Distance(motion.LastProgress,actor.transform.position)>0.15f)
            {motion.LastProgress=actor.transform.position;motion.ProgressAt=Time.unscaledTime;}
            if(Time.unscaledTime-motion.ProgressAt>20)
            {
                // A blocked rejoin is a stationary recovery state, not permission to
                // snap back to the clock-derived schedule. Retry at most once per 30 s.
                motion.RecoveryWaiting=true;motion.RepathAt=Time.unscaledTime+30;motion.Indoor=null;motion.Outdoor=null;motion.NativeOutdoor=false;
                worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actor.transform.position-Vector3.up),true,now);
                actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);return true;
            }
            string recoveryReason;
            if(!motion.RecoverySelected)motion.NativeOutdoor=false;
            if(!motion.RecoverySelected && !SelectRoutineRecoveryOrigin(plan,actor,job,motion,now,out recoveryReason))
            {WaitForRoutineRecovery(actor,motion);return true;}
            // A still-feasible current visit can resume directly from the actual body.
            if(!job.ReturningToRoutine || worldStore.ActiveNpcAction(plan.Resident.Id)==null) return true;
            Vector3 goal=motion.Goal;
            if(Vector3.Distance(actor.transform.position-Vector3.up,goal)<(!interior && motion.NativeOutdoor && motion.Outdoor!=null?0.55f:0.35f))
            {
                if(interior && !TryRoutineRecoveryDoor(plan,motion.RecoveryOrigin,out _))
                {
                    if(!BeginUnplannedBuildingReturn(actor,job,now,out recoveryReason))WaitForRoutineRecovery(actor,motion);
                    return true;
                }
                if(TryReleaseHomeOnlyAtDoor(plan,job,now,motion.RecoveryOrigin,
                    actor.transform.position-Vector3.up,out recoveryReason))return true;
                if(!CompleteRoutineRecovery(plan,actor,job,now,motion.RecoveryOrigin,true,out recoveryReason))
                    WaitForRoutineRecovery(actor,motion);
                return true; // Scheduled was sampled before the doorway handoff this tick.
            }
            // Keep a valid return path and its waypoint progress. Rebuilding on
            // every timer tick resets Next and can prevent physical arrival.
            // A stalled recovery already clears both routes before retrying.
            if(Time.unscaledTime>=motion.RepathAt && motion.Indoor==null && motion.Outdoor==null)
            {
                string reason;
                if(!BuildRoutineRecoveryJoin(actor,motion,out reason)) WaitForRoutineRecovery(actor,motion);
            }
            if(Time.unscaledTime>=motion.SaveAt)
            {
                motion.SaveAt=Time.unscaledTime+0.5f;
                worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actor.transform.position-Vector3.up),true,now);
            }
            AdvanceActionMotion(actor,motion);return true;
        }
        private void ReanchorActionDeparture(Plan plan,ResidentActor actor,ActionMotion motion,double now,bool arrived)
        {
            if(!interior || motion.Indoor==null) return;
            var actual=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            var remaining=motion.Indoor.RemainingRoute(actual);
            if(remaining.Count<2) remaining.Add(new GuardPoint3 {X=actual.x,Y=actual.y,Z=actual.z});
            var outside=DailyReturnRoute(plan.Routine,interior.EntryDoor.buildingKey);
            if(outside==null || outside.Count<2) return;
            var departure=VisitDeparture.Create(plan.Resident.Id,population.SettlementId,plan.Resident.HomeKey,interior.EntryDoor.buildingKey,
                now,(Math.Floor(now/86400)+1)*86400,remaining,outside,actor.WalkingSpeed,DaggerfallUnity.Instance.WorldTime.TimeScale);
            worldStore.SaveVisitDeparture(departure);
        }
    }
}

