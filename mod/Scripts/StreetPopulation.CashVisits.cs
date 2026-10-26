using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private readonly Dictionary<string,ActionMotion> cashMotions=new Dictionary<string,ActionMotion>();
        private readonly Dictionary<string,float> cashChecks=new Dictionary<string,float>();

        private void CancelCashForCare(string id)
        {
            if(worldStore.ReadNpcCashVisit(id)==null)return;
            SaveCashVisit(id,null);cashMotions.Remove(id);bankVisitors.Remove(id);
        }

        // Wallet zero is the durable pending need. Closed banks and higher
        // priorities leave it pending until an ordinary free, awake opportunity.
        private bool ApplyNpcCashVisit(Plan plan,ref ResidentActor actor,double now,bool danger)
        {
            string id=plan.Resident.Id;
            if(WaitForCashPartner(plan,ref actor,now,danger))return true;
            var visit=worldStore.ReadNpcCashVisit(id);
            if(visit==null)
            {
                if(GameManager.IsGamePaused || actor && (actor.IsSleeping || actor.IsConversing))return false;
                if(!worldStore.NeedsNpcCashErrand(id,now))return false;
                var scheduled=ActiveSchedule(plan).EvaluateSeconds(now);
                if(scheduled.Activity==Activity.Sleep || scheduled.Activity==Activity.Work)return false;
            }
            var inventory=worldStore.ReadNpcInventory(id);
            if(visit==null && inventory==null)return false;
            if(GameManager.IsGamePaused || actor && actor.IsConversing)
            {PhysicalShopVisit paused;if(bankVisitors.TryGetValue(id,out paused))paused.Pause(now);return visit!=null;}
            if(!MedicalPriorityAvailable(plan,now,danger) || HasActiveNpcHealthCare(id))
            {
                // Danger, tasks and medical care cancel this detour. A still
                // empty wallet retries from the actual position afterwards.
                if(visit!=null)SaveCashVisit(id,null);
                bankVisitors.Remove(id);cashMotions.Remove(id);return false;
            }
            if(visit==null)
            {
                if(geometry==null || !EntranceSurveyReady || NativeNpcInventoryBridge.CashWithdrawalGold(worldStore,id)<1)return false;
                var funding=worldStore.ReadPlannedHouseholdInnFunding(id,now);
                if(!PlannedCashWindowOpen(plan,funding,now))return false;
                if(funding!=null)
                {
                    float retry;if(cashChecks.TryGetValue(id,out retry) && Time.unscaledTime<retry)return false;
                    cashChecks[id]=Time.unscaledTime+10;
                }
                visit=ChooseCashVisit(plan,actor);if(visit==null)return false;
                if(funding!=null)
                {
                    // Keep the morning schedule while a closed bank cannot yet
                    // serve this planned errand; do not make the partner wait.
                    if(visit.WaitingForBank && now<funding.DepartureAt)return false;
                    if(!PlannedBankTripFits(plan,visit,funding,now))return false;
                    visit.PlannedHouseholdDayId=funding.HouseholdDayId;visit.RequiredGold=funding.RequiredGold;
                }
                BindCashRendezvous(plan,actor,visit);if(!SaveCashVisit(id,visit))return false;
                if(funding!=null)worldStore.RememberSceneEvent(id,"planned-inn-cash:"+funding.HouseholdDayId,
                    NpcSceneEventKind.CashVisit,now,population.SettlementId,visit.ProviderKey,
                    "I set off to collect enough money to pay for my partner and me at our planned Inn visit; I have not collected it yet.",0,id);
                RemovePhysicalShopVisit(id);shopJourneys.Remove(id);shopDepartures.Remove(id);
                if(visit.WaitingPartnerId==null)worldStore.RemoveObservedVisit(id);
                if(actor && interior && interior.EntryDoor.buildingKey==visit.ProviderKey && !visit.WaitingForBank)
                {
                    if(visit.Bank)BeginBankCounterVisit(plan,actor,visit,now);
                    else {visit.ServiceAt=now+60;SaveCashVisit(id,visit);}
                }
            }
            if(visit.CounterJourney!=null)return ApplyBankCounterVisit(plan,ref actor,visit,now);
            if(CashGoalMet(visit,inventory) && !visit.Returning)
            {visit.WaitingForBank=false;visit.Returning=true;visit.ServiceAt=0;visit.OutsideJourney=null;SaveCashVisit(id,visit);cashMotions.Remove(id);}
            if(visit.WaitingForBank)
            {
                float next;
                if(!cashChecks.TryGetValue(id,out next) || Time.unscaledTime>=next)
                {
                    cashChecks[id]=Time.unscaledTime+10;
                    if(ActiveSchedule(plan).EvaluateSeconds(now).Activity!=Activity.Sleep)
                    {
                        var available=ChooseCashVisit(plan,actor,visit.Position);
                        if(available!=null && !available.WaitingForBank)
                        {KeepCashRendezvous(visit,available);visit=available;SaveCashVisit(id,visit);cashMotions.Remove(id);}
                    }
                }
            }
            if(visit.Position.Kind==NpcActionTargetKind.WorldPoint &&
                (interior || !actor && Vector3.Distance(ActionWorldPoint(visit.Position),GameManager.Instance.PlayerMotor.transform.position)>140))
                return AdvanceUnobservedCashVisit(plan,visit,now);
            if(!ActionSpaceMatches(visit.Position))return AdvanceUnobservedCashExit(plan,visit,now);
            if(visit.OutsideJourney!=null)
            {
                if(visit.Position.Kind==NpcActionTargetKind.WorldPoint)
                {var sample=visit.OutsideJourney.Evaluate(Math.Max(now,visit.OutsideJourney.ObservedAt));
                    visit.Position.X=sample.Position.X;visit.Position.Z=sample.Position.Z;}
                visit.OutsideJourney=null;SaveCashVisit(id,visit);
            }
            EnsureCashActor(plan,ref actor,visit.Position);if(!actor)return true;
            if(visit.WaitingForBank)
            {
                if(visit.WaitingForBank && interior && interior.EntryDoor.buildingKey==plan.Resident.HomeKey)
                {actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);return true;}
            }
            if(visit.Bank && !visit.Returning && interior && interior.EntryDoor.buildingKey==visit.ProviderKey)
            {BeginBankCounterVisit(plan,actor,visit,now);return true;}
            if(!visit.Returning && CashGoalMet(visit,inventory))
            {visit.Returning=true;visit.ServiceAt=0;SaveCashVisit(id,visit);cashMotions.Remove(id);}
            if(visit.ServiceAt>0)
            {
                // Home withdrawals only. Banks use the clerk route and its dwell.
                actor.gameObject.SetActive(false);if(now<visit.ServiceAt)return true;
                var owner=actor.GetComponent<NativeNpcInventoryOwner>();
                if(owner && owner.Binding!=null && CashProviderAvailable(plan,actor,visit) &&
                    worldStore.TryCompleteNpcCashVisit(worldStore.CaptureNpcVitalsWriteEpoch(),id,owner.Binding.Revision,
                        visit.ProviderKey,CashWithdrawalAmount(id,visit),now))
                    owner.Binding.Revision++;
                visit=worldStore.ReadNpcCashVisit(id);visit.ServiceAt=0;visit.Returning=true;SaveCashVisit(id,visit);
                if(interior && interior.EntryDoor.buildingKey==plan.Resident.HomeKey && visit.Rendezvous==null)
                {SaveCashVisit(id,null);actor.gameObject.SetActive(true);actor.ResumeFromSocialPause();cashMotions.Remove(id);return true;}
                actor.gameObject.SetActive(true);cashMotions.Remove(id);return true;
            }
            ActionMotion motion;
            if(!cashMotions.TryGetValue(id,out motion)){motion=new ActionMotion();cashMotions[id]=motion;}
            Vector3 target;
            if(interior)
            {
                if(indoorGeometry==null || !indoorGeometry.EntryGround.HasValue)return true;
                target=indoorGeometry.EntryGround.Value;
                if(visit.Returning && visit.Rendezvous!=null && ActionSpaceMatches(visit.Rendezvous))target=ActionWorldPoint(visit.Rendezvous);
            }
            else
            {
                TownGeometry.Entrance door;
                if(!geometry.Entrances.TryGetValue(visit.Returning?visit.ReturnKey:visit.ProviderKey,out door))return true;
                target=geometry.ScenePoint(new RoutePoint(door.LocalDoor.x,door.LocalDoor.z));
                if(visit.Returning && visit.Rendezvous!=null && visit.Rendezvous.Kind==NpcActionTargetKind.WorldPoint)
                    target=geometry.ScenePoint(new RoutePoint(visit.Rendezvous.X,visit.Rendezvous.Z));
            }
            Vector3 actual=actor.transform.position-Vector3.up;
            if(Vector3.Distance(actual,target)<=0.6f)
            {
                if(visit.Returning && visit.Rendezvous!=null && ActionSpaceMatches(visit.Rendezvous))
                {CompleteCashRendezvous(plan,visit);return true;}
                if(interior)
                {
                    TownGeometry.Entrance door;
                    if(!geometry.Entrances.TryGetValue(interior.EntryDoor.buildingKey,out door))return true;
                    visit.Position=MedicalDoorPosition(door);SaveCashVisit(id,visit);
                    actor.CaptureState();actor.gameObject.SetActive(false);Destroy(actor.gameObject);actors.Remove(id);actor=null;
                    cashMotions.Remove(id);return true;
                }
                visit.Position=ActionPosition(actual);
                if(!visit.Returning)
                {
                    if(visit.WaitingForBank){actor.gameObject.SetActive(false);SaveCashVisit(id,visit);return true;}
                    if(CashProviderAvailable(plan,actor,visit))
                    {
                        if(visit.Bank){BeginBankCounterVisit(plan,actor,visit,now);return true;}
                        visit.ServiceAt=now+60;
                    }
                    else visit.Returning=true;
                    SaveCashVisit(id,visit);cashMotions.Remove(id);return true;
                }
                if(visit.Rendezvous!=null && visit.Rendezvous.Kind==NpcActionTargetKind.IndoorPoint)
                {
                    visit.Position=NpcActionExecutor.CopyTarget(visit.Rendezvous);SaveCashVisit(id,visit);
                    actor.CaptureState();actor.gameObject.SetActive(false);Destroy(actor.gameObject);actors.Remove(id);actor=null;
                    cashMotions.Remove(id);return true;
                }
                if(CompleteCashRendezvous(plan,visit))return true;
                RoutineRecoveryPlan recovery;string reason;
                if(TryPlanRoutineRecovery(plan,actor,now,visit.ReturnKey,true,out recovery,out reason))worldStore.SaveRoutineRecovery(recovery);
                else if(!CanReleaseHomeOnlyAtDoor(plan,now,out reason))return true;
                InvalidateRoutineRecovery(id);SaveCashVisit(id,null);cashMotions.Remove(id);cashChecks[id]=Time.unscaledTime+30;return true;
            }
            actor.gameObject.SetActive(true);string failure;
            if(motion.Indoor==null && motion.Outdoor==null)
            {
                if(Time.unscaledTime<motion.RepathAt)return true;
                if(!BuildActionRoute(actor,motion,target,0.4f,out failure))
                {
                    motion.RepathAt=Time.unscaledTime+10;actor.Place(actual,actor.transform.forward,true);
                    if(++motion.RouteFailures<3)return true;
                    motion.RouteFailures=0;motion.RepathAt=Time.unscaledTime+60;
                    return true;
                }
                motion.RouteFailures=0;
            }
            AdvanceActionMotion(actor,motion);
            if(Time.unscaledTime>=motion.SaveAt)
            {motion.SaveAt=Time.unscaledTime+1;visit.Position=ActionPosition(actor.transform.position-Vector3.up);SaveCashVisit(id,visit);}
            return true;
        }

        private NpcCashVisit ChooseCashVisit(Plan plan,ResidentActor actor,NpcActionTarget previous=null)
        {
            SavedRoutePoint home;
            if(!TryRoutineRecoveryDoor(plan,plan.Resident.HomeKey,out home))return null;
            var position=previous??(actor?ActionPosition(actor.transform.position-Vector3.up):CashScheduledPosition(plan));
            if(position==null)return null;
            var banks=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Bank);
            int selected=-1;float best=float.PositiveInfinity;
            foreach(var bank in banks)
            {
                TownGeometry.Entrance door;
                if(!CashBankOpen(bank.buildingKey) || !geometry.Entrances.TryGetValue(bank.buildingKey,out door))continue;
                var point=geometry.ScenePoint(new RoutePoint(door.LocalDoor.x,door.LocalDoor.z));
                float distance=position.BuildingKey==bank.buildingKey?0:(float)((door.LocalDoor.x-position.X)*(door.LocalDoor.x-position.X)+(door.LocalDoor.z-position.Z)*(door.LocalDoor.z-position.Z));
                if(distance>=best)continue;
                if(!interior && actor){var route=new ActionMotion();string reason;if(!BuildActionRoute(actor,route,point,0.4f,out reason))continue;}
                if(geometry.RouteBetweenPoints(new SavedRoutePoint{X=door.LocalDoor.x,Z=door.LocalDoor.z},home).Count<2)continue;
                selected=bank.buildingKey;best=distance;
            }
            var destination=NpcNeedDestinationPolicy.SelectBanking(banks.Count>0,selected>=0,true);
            bool waiting=destination==NpcNeedDestinationKind.None;
            return new NpcCashVisit {ProviderKey=waiting || destination==NpcNeedDestinationKind.Home?plan.Resident.HomeKey:selected,
                ReturnKey=plan.Resident.HomeKey,Bank=destination==NpcNeedDestinationKind.Bank,
                WaitingForBank=waiting,
                Position=position};
        }

        private bool CashProviderAvailable(Plan plan,ResidentActor actor,NpcCashVisit visit)
        {
            if(!visit.Bank)return visit.ProviderKey==plan.Resident.HomeKey &&
                directory.GetBuildingsOfType(DFLocation.BuildingTypes.Bank).Count==0;
            return CashBankOpen(visit.ProviderKey);
        }

        private bool CashBankOpen(int key)
        {BuildingSummary building;return directory.GetBuildingSummary(key,out building) && building.BuildingType==DFLocation.BuildingTypes.Bank &&
            GameManager.Instance.PlayerActivate && GameManager.Instance.PlayerActivate.BuildingIsUnlocked(building);}

        private bool SaveCashVisit(string id,NpcCashVisit visit)
        {return worldStore.SaveNpcCashVisit(worldStore.CaptureNpcVitalsWriteEpoch(),id,visit);}
    }
}
