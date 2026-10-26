using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private bool FamilyNeedsCash(HouseholdJointProgress progress)
        {return worldStore.NeedsNpcCashErrand(progress.FirstResidentId,ActionNow()) || worldStore.NeedsNpcCashErrand(progress.SecondResidentId,ActionNow()) ||
            worldStore.ReadNpcCashVisit(progress.FirstResidentId)!=null || worldStore.ReadNpcCashVisit(progress.SecondResidentId)!=null;}

        private bool WaitForCashPartner(Plan plan,ref ResidentActor actor,double now,bool danger)
        {
            var errand=worldStore.ReadNpcCashWait(plan.Resident.Id);
            if(errand==null || !MedicalPriorityAvailable(plan,now,danger) || HasActiveNpcHealthCare(plan.Resident.Id))return false;
            if(!ActionSpaceMatches(errand.PartnerWaitPosition))return true;
            EnsureCashActor(plan,ref actor,errand.PartnerWaitPosition);
            if(!actor)return true;
            actor.gameObject.SetActive(true);
            PhysicalShopVisit visit;
            if(physicalShopVisits.TryGetValue(plan.Resident.Id,out visit))
            {
                var paid=worldStore.ReadNpcInnConsumption(plan.Resident.Id);
                var waitingActor=actor;
                if(!actor.IsConversing && !GameManager.IsGamePaused && !actor.IsSeated && interior && shopGeometry!=null &&
                    paid!=null && paid.Outcome==NpcInnConsumptionOutcome.Purchased)
                    visit.Advance(actor,interior,indoorGeometry,shopGeometry,now,
                        ()=>TryCompleteInnPurchaseAtCounter(waitingActor,visit,now),()=>true);
                visit.Pause(now);
                SaveCashWaitingVisit(plan,actor,visit,now);
            }
            // Holding must preserve an existing chair and conversation posture.
            HouseholdJointBodyMotion.Hold(actor);
            var saved=worldStore.GetObservedVisit(plan.Resident.Id);
            if(!actor.IsConversing && saved!=null && saved.HouseholdSeatId!=null && interior &&
                saved.BuildingKey==interior.EntryDoor.buildingKey && shopGeometry?.Seating!=null)
            {
                SelectedSeatAccess seat;
                if(shopGeometry.Seating.TryAcquire(plan.Resident.Id,saved.HouseholdSeatId,out seat) &&
                    Vector3.Distance(actor.transform.position-Vector3.up,seat.Slot.Ground)<1.6f)seat.Advance(actor);
            }
            return true;
        }

        private void BindCashRendezvous(Plan plan,ResidentActor actor,NpcCashVisit visit)
        {
            string partnerId=plan.Resident.PartnerId;
            if(partnerId==null || !worldStore.HasActiveHouseholdJoint(population.SettlementId,plan.Resident.Id))return;
            var shared=worldStore.ReadHouseholdJointProgress(population.SettlementId,plan.Resident.HomeKey);
            if(shared==null || shared.Phase==HouseholdJointPhase.Waiting)return;
            var partner=plans.Find(p=>p.Resident.Id==partnerId);
            if(partner==null || !partner.Resident.IsAlive)return;
            var other=FindActor(partnerId);
            var waiting=other && other.gameObject.activeInHierarchy?ActionPosition(other.transform.position-Vector3.up):CashScheduledPosition(partner);
            if(waiting==null)return;
            JointInnBinding binding;
            if(jointInnBindings.TryGetValue(plan.Resident.HomeKey,out binding) && binding.Motion!=null)
                binding.Motion.PauseForCash(ActionNow());
            visit.WaitingPartnerId=partnerId;
            visit.Rendezvous=NpcActionExecutor.CopyTarget(visit.Position);
            if(visit.Rendezvous.Kind==NpcActionTargetKind.IndoorPoint && indoorGeometry?.EntryGround!=null)
                visit.Rendezvous=ActionPosition(indoorGeometry.EntryGround.Value);
            visit.PartnerWaitPosition=waiting;
            if(visit.Rendezvous.Kind==NpcActionTargetKind.IndoorPoint)visit.ReturnKey=visit.Rendezvous.BuildingKey;
            PhysicalShopVisit held;
            if(physicalShopVisits.TryGetValue(partnerId,out held))held.Pause(ActionNow());
        }

        private void KeepCashRendezvous(NpcCashVisit previous,NpcCashVisit next)
        {
            next.WaitingPartnerId=previous.WaitingPartnerId;next.Rendezvous=NpcActionExecutor.CopyTarget(previous.Rendezvous);
            next.PartnerWaitPosition=NpcActionExecutor.CopyTarget(previous.PartnerWaitPosition);next.ReturnKey=previous.ReturnKey;
            next.PlannedHouseholdDayId=previous.PlannedHouseholdDayId;next.RequiredGold=previous.RequiredGold;
        }

        private bool CompleteCashRendezvous(Plan plan,NpcCashVisit visit)
        {
            if(visit.WaitingPartnerId==null)return false;
            if(visit.Rendezvous.Kind==NpcActionTargetKind.IndoorPoint && interior && interior.EntryDoor.buildingKey==visit.Rendezvous.BuildingKey)
            {
                var actor=FindActor(plan.Resident.Id);JointInnBinding binding;
                if(actor && jointInnBindings.TryGetValue(plan.Resident.HomeKey,out binding) && binding.Motion!=null)
                {
                    var progress=worldStore.ReadHouseholdJointProgress(population.SettlementId,plan.Resident.HomeKey);
                    bool first=progress!=null && progress.FirstResidentId==plan.Resident.Id;
                    binding.Motion.ResumeAfterCash(actor,ActionNow());
                    if(first)binding.First=actor;else binding.Second=actor;
                    physicalShopVisits[plan.Resident.Id]=first?binding.Motion.FirstVisit:binding.Motion.SecondVisit;
                }
                else if(actor && !RebaseCashReturn(plan,actor,ActionNow()))return false;
            }
            SaveCashVisit(plan.Resident.Id,null);cashMotions.Remove(plan.Resident.Id);
            jointBodyBindings.Remove(plan.Resident.HomeKey);
            return true;
        }

        private void SaveCashWaitingVisit(Plan plan,ResidentActor actor,PhysicalShopVisit physical,double now)
        {
            if(!interior || physical.HouseholdDayId==null)return;
            var waiting=physical.Capture(actor,interior,indoorGeometry,now,population.SettlementId);
            var collector=worldStore.GetObservedVisit(plan.Resident.PartnerId);
            if(waiting==null || collector==null || collector.HouseholdDayId!=waiting.HouseholdDayId)return;
            collector.ObservedAt=now;
            var progress=worldStore.ReadHouseholdJointProgress(population.SettlementId,plan.Resident.HomeKey);
            if(progress==null)return;
            if(progress.FirstResidentId==plan.Resident.Id)worldStore.StoreHouseholdInnVisits(waiting,collector);
            else worldStore.StoreHouseholdInnVisits(collector,waiting);
            var errand=worldStore.ReadNpcCashVisit(plan.Resident.PartnerId);
            if(errand!=null){errand.PartnerWaitPosition=ActionPosition(actor.transform.position-Vector3.up);SaveCashVisit(plan.Resident.PartnerId,errand);}
        }

        private bool RebaseCashReturn(Plan plan,ResidentActor actor,double now)
        {
            var own=worldStore.GetObservedVisit(plan.Resident.Id);var other=worldStore.GetObservedVisit(plan.Resident.PartnerId);
            if(own==null || other==null || own.HouseholdDayId==null || own.HouseholdDayId!=other.HouseholdDayId)return true;
            var leg=own.Legs[0];var end=leg.Route[leg.Route.Count-1];
            var actual=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            var target=new Vector3((float)end.X,(float)end.Y,(float)end.Z);
            var route=shopGeometry.ActivityRouteBetween(actual,target);
            if(route.Count==0)return false;
            var last=route[route.Count-1];
            if(System.Math.Abs(last.X-end.X)+System.Math.Abs(last.Y-end.Y)+System.Math.Abs(last.Z-end.Z)>0.0001)
                route.Add(new GuardPoint3{X=end.X,Y=end.Y,Z=end.Z});
            leg.Route=route;own.ObservedAt=other.ObservedAt=now;
            var progress=worldStore.ReadHouseholdJointProgress(population.SettlementId,plan.Resident.HomeKey);
            if(progress==null)return true;
            if(progress.FirstResidentId==plan.Resident.Id)worldStore.StoreHouseholdInnVisits(own,other);
            else worldStore.StoreHouseholdInnVisits(other,own);
            return true;
        }
    }
}
