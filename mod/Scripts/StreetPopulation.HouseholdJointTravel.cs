using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class JointBodyBinding
        {
            internal HouseholdJointMovement Movement;
            internal HouseholdJointBodyMotion Bodies;
            internal ResidentActor First,Second;
            internal int HouseholdRevision,RoutineRevision,AppointmentRevision,Sequence;
            internal double ValidUntil;
            internal float RetryAfter;
            internal IList<AppointmentPlaceRoute> PointRoutes;
        }
        private readonly Dictionary<int,JointBodyBinding> jointBodyBindings=new Dictionary<int,JointBodyBinding>();
        private readonly Dictionary<int,HouseholdJointProgress> jointFrameRecords=new Dictionary<int,HouseholdJointProgress>();
        private readonly Dictionary<string,ResidentActor> jointFrameEligible=new Dictionary<string,ResidentActor>(StringComparer.Ordinal);
        private void BeginHouseholdJointFrame(){jointFrameRecords.Clear();jointFrameEligible.Clear();}
        private bool TryHouseholdJointTravelState(Plan plan,out ActivitySnapshot result)
        {
            result=default(ActivitySnapshot);
            if(worldStore==null || population==null || plan==null ||
                !worldStore.HasActiveHouseholdJoint(population.SettlementId,plan.Resident.Id))return false;
            var record=worldStore.ReadHouseholdJointProgress(population.SettlementId,plan.Resident.HomeKey);
            if(record==null || !record.Active || !population.Households.Exists(h=>h.BuildingKey==record.HomeKey && h.Generation==record.Generation) ||
                record.FirstResidentId!=plan.Resident.Id && record.SecondResidentId!=plan.Resident.Id)return false;
            var sim=worldStore.ReadHouseholdJointSimulation(record.SettlementId,record.HomeKey);
            if(sim!=null && sim.Active)
            {
                if(sim.Phase==HouseholdJointPhase.Waiting)
                {
                    var waitingDay=worldStore.ReadHouseholdDay(record.SettlementId,record.HomeKey,record.AbsoluteDay);
                    if(waitingDay?.JointInn==null || DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()<
                        waitingDay.AbsoluteDay*86400d+Math.Max(waitingDay.JointInn.First.DepartureSeconds,waitingDay.JointInn.Second.DepartureSeconds))return false;
                    PhysicalShopVisit ordinary;
                    if(physicalShopVisits.TryGetValue(plan.Resident.Id,out ordinary) && ordinary.HouseholdDayId==null && ordinary.Snapshot.Phase!=ShopVisitPhase.Complete)return false;
                    var observed=worldStore.GetObservedVisit(plan.Resident.Id);double at=DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    if(observed!=null && observed.HouseholdDayId!=record.HouseholdDayId && observed.AffectsRoutineDuring(at,at+1))return false;
                    ResidentActor speaking;
                    if(actors.TryGetValue(plan.Resident.Id,out speaking) && speaking && speaking.IsConversing)
                    {
                        if(interior){result=new ActivitySnapshot(Activity.Visit,interior.EntryDoor.buildingKey,new RoutePoint(),0);return true;}
                        var actual=directory.transform.InverseTransformPoint(speaking.transform.position-Vector3.up);
                        result=new ActivitySnapshot(Activity.Travel,-1,new RoutePoint(actual.x,actual.z),0);return true;
                    }
                    result=new ActivitySnapshot(Activity.Home,record.HomeKey,new RoutePoint(),0);return true;
                }
                if(sim.Phase==HouseholdJointPhase.Completed)
                {result=new ActivitySnapshot(Activity.Home,record.HomeKey,new RoutePoint(),0);return true;}
                if(sim.Phase==HouseholdJointPhase.Outbound || sim.Phase==HouseholdJointPhase.Returning)
                {
                    var projected=plan.Resident.Id==record.FirstResidentId?sim.First.Position:sim.Second.Position;
                    result=new ActivitySnapshot(Activity.Travel,-1,new RoutePoint(projected.X,projected.Z),0);return true;
                }
                var source=worldStore.ReadHouseholdDay(record.SettlementId,record.HomeKey,record.AbsoluteDay);
                if(source?.JointInn==null)return false;
                result=new ActivitySnapshot(Activity.Visit,source.JointInn.InnKey,new RoutePoint(),0);return true;
            }
            if(record.Phase==HouseholdJointPhase.Waiting || record.Phase==HouseholdJointPhase.Completed || record.Phase==HouseholdJointPhase.Cancelled)return false;
            if(record.Phase==HouseholdJointPhase.Visiting)
            {
                var day=worldStore.ReadHouseholdDay(record.SettlementId,record.HomeKey,record.AbsoluteDay);
                if(day==null || day.JointInn==null || HouseholdJointMovement.SourceFingerprint(day)!=record.SourceFingerprint)return false;
                result=new ActivitySnapshot(Activity.Visit,day.JointInn.InnKey,new RoutePoint(),0);return true;
            }
            jointFrameRecords[record.HomeKey]=record;
            var point=record.FirstResidentId==plan.Resident.Id?record.First.Position:record.Second.Position;
            result=new ActivitySnapshot(Activity.Travel,-1,new RoutePoint(point.X,point.Z),0);return true;
        }
        private void RestoreHouseholdJointBody(Plan plan,ResidentActor actor)
        {
            ActivitySnapshot state;
            if(TryHouseholdJointTravelState(plan,out state) && state.Activity==Activity.Travel)
                actor.GetComponent<CrowdMotion>().RestorePlacement(geometry.ScenePoint(state.Position));
        }
        private bool DeferHouseholdJointBody(Plan plan,ResidentActor actor)
        {
            ActivitySnapshot ignored;
            if(!TryHouseholdJointTravelState(plan,out ignored) || ignored.Activity!=Activity.Travel)return false;
            jointFrameEligible[plan.Resident.Id]=actor;return true;
        }
        private void TickHouseholdJointBodies(double now)
        {
            // Both parents have now passed the same priority boundary. A parent
            // preempted by danger, care, reporting, action, contact or chat is not
            // offered. Its eligible partner holds rather than following time.
            foreach(var record in new List<HouseholdJointProgress>(jointFrameRecords.Values))
            {
                ResidentActor first,second;
                jointFrameEligible.TryGetValue(record.FirstResidentId,out first);
                jointFrameEligible.TryGetValue(record.SecondResidentId,out second);
                if(!first && !second)continue;
                JointBodyBinding binding;
                bool stale=!jointBodyBindings.TryGetValue(record.HomeKey,out binding) ||
                    binding.First!=first || binding.Second!=second || binding.Sequence!=record.Sequence ||
                    binding.HouseholdRevision!=worldStore.HouseholdDayRevision || binding.RoutineRevision!=worldStore.RoutineRevision ||
                    binding.AppointmentRevision!=worldStore.AppointmentRevision ||
                    (binding.Movement==null?Time.unscaledTime>=binding.RetryAfter:now>=binding.ValidUntil);
                if(stale)
                {
                    binding=new JointBodyBinding{First=first,Second=second,Sequence=record.Sequence,
                        HouseholdRevision=worldStore.HouseholdDayRevision,RoutineRevision=worldStore.RoutineRevision,
                        AppointmentRevision=worldStore.AppointmentRevision,RetryAfter=Time.unscaledTime+2};
                    var a=plans.Find(p=>p.Resident.Id==record.FirstResidentId);var b=plans.Find(p=>p.Resident.Id==record.SecondResidentId);
                    string reason;var routes=a!=null && b!=null?FamilyPointRoutes(a,b,FamilyBookings(a,b),record.AbsoluteDay,out reason):null;
                    var movement=worldStore.GetHouseholdJointMovement(record.SettlementId,record.HomeKey,routes,now);
                    if(movement==null){jointBodyBindings[record.HomeKey]=binding;HouseholdJointBodyMotion.Hold(first);HouseholdJointBodyMotion.Hold(second);continue;}
                    var source=worldStore.ReadHouseholdDay(record.SettlementId,record.HomeKey,record.AbsoluteDay);
                    double until=double.PositiveInfinity;
                    if(record.Phase!=HouseholdJointPhase.Returning && !movement.RequiresRecovery && source!=null && source.JointInn!=null)
                        foreach(var outing in new[]{source.JointInn.First,source.JointInn.Second})
                            foreach(var visit in ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary).ScheduledVisits())
                                until=Math.Min(until,record.AbsoluteDay*86400d+visit.LeaveAt);
                    binding.Movement=movement;binding.ValidUntil=until;binding.PointRoutes=routes;
                    binding.Bodies=new HouseholdJointBodyMotion(movement,first,second,geometry,directory.transform,
                        p=>worldStore.SaveHouseholdJointProgress(p,routes));jointBodyBindings[record.HomeKey]=binding;
                }
                if(binding.Movement==null){HouseholdJointBodyMotion.Hold(first);HouseholdJointBodyMotion.Hold(second);continue;}
                bool firstAvailable=first && JointBodyFree(record.FirstResidentId,now);
                bool secondAvailable=second && JointBodyFree(record.SecondResidentId,now);
                try
                {
                    binding.Bodies.Tick(now,firstAvailable,secondAvailable,GameManager.IsGamePaused);
                    var arrived=binding.Movement.Snapshot();
                    if(arrived.Phase==HouseholdJointPhase.AtInn && firstAvailable && secondAvailable && JointDoorBodyMatches(first,arrived.First) &&
                        JointDoorBodyMatches(second,arrived.Second) && !binding.Movement.RequiresRecovery && !GameManager.IsGamePaused)
                    {
                        var day=worldStore.ReadHouseholdDay(record.SettlementId,record.HomeKey,record.AbsoluteDay);
                        var entered=HouseholdJointMovement.Restore(day,arrived);entered.EnterInn(now);
                        worldStore.SaveHouseholdJointProgress(entered.Snapshot(),binding.PointRoutes);
                        binding.Movement=entered;
                        // Both real bodies reached the door. The unloaded Inn
                        // now owns continuation until its room is materialized.
                        worldStore.BeginHouseholdJointSimulation(record.HouseholdDayId,entered.Snapshot().Sequence,now,binding.PointRoutes);
                    }
                    binding.Sequence=binding.Movement.Snapshot().Sequence;
                }
                catch(Exception error)
                {
                    // The executor latches closed. Preserve the prior durable
                    // checkpoint; neither individual schedule may snap forward.
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_BODY_RECOVERY_REQUIRED: home="+record.HomeKey+"; "+error.Message);
                }
                if(first)RecognizePartnerKiller(first,now,directory.transform,-1);
                if(second)RecognizePartnerKiller(second,now,directory.transform,-1);
            }
        }
        private bool JointBodyFree(string id,double now)
        {return !worldStore.IsResidentAway(id,now) && !IsAlarmed(id) && worldStore.ActiveAlarm(id,now)==null &&
            worldStore.ActiveNpcAction(id)==null && worldStore.ActiveNpcTask(id)==null && !HasActiveNpcHealthCare(id) &&
            !shopDepartures.ContainsKey(id) && !HasDialogueComplaint(id);}
        private bool JointDoorBodyMatches(ResidentActor actor,HouseholdRouteCursor checkpoint)
        {
            if(!actor || actor.transform.parent!=directory.transform || actor.IsConversing || actor.IsSleeping || actor.IsSeated ||
                !actor.GetComponent<CrowdMotion>() || !actor.GetComponent<CrowdMotion>().HasPlacement)return false;
            var actual=directory.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            double x=actual.x-checkpoint.Position.X,z=actual.z-checkpoint.Position.Z;
            return x*x+z*z<=0.000001;
        }
        private void ClearHouseholdJointBodies()
        {
            // Every accepted physical step is already stored as one paired
            // checkpoint. Actor teardown is never new arrival evidence.
            jointBodyBindings.Clear();jointFrameRecords.Clear();jointFrameEligible.Clear();
        }
    }
}
