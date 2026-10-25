using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public int HouseholdJointRevision {get;private set;}
        private void BumpHouseholdJointRevision(){if(HouseholdJointRevision<int.MaxValue)HouseholdJointRevision++;}
        private sealed class JointControllerCache
        {
            internal HouseholdJointProgress Record;
            internal HouseholdJointMovement Controller;
            internal int HouseholdRevision,RoutineRevision,AppointmentRevision;
            internal double LeisureUntil;
        }
        private readonly Dictionary<string,JointControllerCache> householdJointControllers=new Dictionary<string,JointControllerCache>(StringComparer.Ordinal);
        public HouseholdJointProgress ReadHouseholdJointProgress(int settlement,int home)
        {
            var record=state.HouseholdJointProgress.Find(p=>p.SettlementId==settlement && p.HomeKey==home);
            return record==null?null:Core.HouseholdJointProgress.Copy(record);
        }
        public bool HasActiveHouseholdJoint(int settlement,string resident)
        {return state.HouseholdJointProgress.Exists(p=>p.SettlementId==settlement && p.Active &&
            (p.FirstResidentId==resident || p.SecondResidentId==resident));}
        public List<HouseholdJointProgress> ReadActiveHouseholdJoints(int settlement)
        {
            var result=new List<HouseholdJointProgress>();foreach(var item in state.HouseholdJointProgress)
                if(item.SettlementId==settlement && item.Active)result.Add(Core.HouseholdJointProgress.Copy(item));
            return result;
        }
        public HouseholdJointMovement GetHouseholdJointMovement(int settlement,int home,IList<AppointmentPlaceRoute> pointRoutes=null,double? now=null)
        {
            var record=state.HouseholdJointProgress.Find(p=>p.SettlementId==settlement && p.HomeKey==home);
            if(record==null || record.Retired || SimulationOwnsHousehold(settlement,home))return null;
            try
            {
                var plan=ResolveJointSource(state,record);
                var policy=record;
                if(now.HasValue)
                {
                    if(!Core.HouseholdJointProgress.Time(now.Value) || now.Value<record.ObservedAt)return null;
                    policy=Core.HouseholdJointProgress.Copy(record);policy.ObservedAt=now.Value;
                }
                var duty=JointDuties(state,plan,policy,pointRoutes);
                if(duty==JointDutyState.PendingProof)return null;
                if(duty==JointDutyState.Conflict){plan=HouseholdDayPlan.Copy(plan);plan.Status="cancelled";}
                return HouseholdJointMovement.Restore(plan,record);
            }
            catch(ArgumentException){return null;}
        }
        // This releases an inert/cancelled reservation, not a fabricated arrival.
        // Keep the old phase and both observed positions as historical evidence.
        // Native callers must supply actual home placement for every survivor;
        // a recurring schedule projection is not that evidence.
        public bool TryRetireHouseholdJointAtHome(int settlement,int homeKey,double now,Func<string,bool> physicallyHome,
            IList<AppointmentPlaceRoute> pointRoutes=null)
        {
            var record=state.HouseholdJointProgress.Find(p=>p.SettlementId==settlement && p.HomeKey==homeKey);
            if(record==null || !record.Active || SimulationOwnsHousehold(settlement,homeKey) ||
                !Core.HouseholdJointProgress.Time(now) || now<record.ObservedAt || record.Sequence==int.MaxValue)return false;
            string reason="physical-recovery-at-home";
            HouseholdDayPlan suppress=null;
            if(record.Phase==HouseholdJointPhase.Waiting)
            {
                try
                {
                    var source=ResolveJointSource(state,record);
                    if(source.Status=="planned" && !source.JointSuppressed)
                    {
                        var policy=Core.HouseholdJointProgress.Copy(record);policy.ObservedAt=now;
                        if(JointDuties(state,source,policy,pointRoutes)!=JointDutyState.Conflict)return false;
                        suppress=source;
                    }
                    reason="event-cancelled-at-home";
                }
                catch(ArgumentException){reason="source-unavailable-at-home";}
            }
            if(record.Phase!=HouseholdJointPhase.Waiting)
            {
                var town=state.Settlements.Find(t=>t.SettlementId==settlement);
                var home=town?.Households.Find(h=>h.BuildingKey==homeKey && h.Generation==record.Generation);
                if(home==null)home=state.HouseholdHistory.Find(h=>h.SettlementId==settlement &&
                    h.Household.BuildingKey==homeKey && h.Household.Generation==record.Generation)?.Household;
                if(home==null)return false;
                bool survivor=false;
                foreach(string id in new[]{record.FirstResidentId,record.SecondResidentId})
                {
                    var person=home.Residents.Find(p=>p.Id==id);
                    if(person==null || person.IsAlive && (physicallyHome==null || !physicallyHome(id)))return false;
                    if(person.IsAlive)survivor=true;
                }
                if(!survivor)reason="participants-dead";
            }
            var copy=Core.HouseholdJointProgress.Copy(record);copy.RetirementReason=reason;copy.RetiredAt=now;copy.Sequence++;
            if(suppress!=null && now<suppress.AbsoluteDay*86400d+Math.Min(suppress.JointInn.First.DepartureSeconds,suppress.JointInn.Second.DepartureSeconds))
                SuppressHouseholdJoint(suppress.Id,now);
            int index=state.HouseholdJointProgress.IndexOf(record);state.HouseholdJointProgress[index]=copy;
            householdJointControllers.Remove(copy.HouseholdDayId);BumpHouseholdJointRevision();return true;
        }
        public void SaveHouseholdJointProgress(HouseholdJointProgress source,IList<AppointmentPlaceRoute> pointRoutes=null)
        {
            var copy=Core.HouseholdJointProgress.Copy(source);
            Require(!SimulationOwnsHousehold(copy.SettlementId,copy.HomeKey),"Simulation owns this household's shared progress.");
            int index=state.HouseholdJointProgress.FindIndex(p=>p.SettlementId==copy.SettlementId && p.HomeKey==copy.HomeKey);
            var old=index<0?null:state.HouseholdJointProgress[index];
            if(old!=null && old.HouseholdDayId==copy.HouseholdDayId && Core.HouseholdJointProgress.Equivalent(old,copy))return;
            HouseholdJointMovement accepted;
            if(old==null || old.HouseholdDayId!=copy.HouseholdDayId)
            {
                Require(old==null || !old.Active && (copy.Generation>old.Generation ||
                    copy.Generation==old.Generation && copy.AbsoluteDay>old.AbsoluteDay),"Shared household progress cannot erase an active or newer event.");
                var plan=ResolveJointSource(state,copy);
                Require(JointDuties(state,plan,copy,pointRoutes)==JointDutyState.Compatible,"Required parent duties prevent new shared travel.");
                accepted=HouseholdJointMovement.Create(plan,copy.CreatedAt);
                Require(Core.HouseholdJointProgress.Equivalent(copy,accepted.Snapshot()),"Shared progress must begin at the waiting home phase.");
            }
            else
            {
                JointControllerCache cached;
                if(householdJointControllers.TryGetValue(copy.HouseholdDayId,out cached) && ReferenceEquals(cached.Record,old) &&
                    cached.HouseholdRevision==HouseholdDayRevision && cached.RoutineRevision==RoutineRevision && cached.AppointmentRevision==AppointmentRevision &&
                    (copy.ObservedAt<cached.LeisureUntil || copy.Phase==HouseholdJointPhase.Returning || copy.Phase==HouseholdJointPhase.Completed || copy.Phase==HouseholdJointPhase.Cancelled))
                {
                    // Death/protection can change without a routine revision.
                    ResolveJointParents(state,copy);
                    accepted=cached.Controller.VerifiedNext(copy);
                }
                else
                {
                    var plan=ResolveJointSource(state,copy);
                    Require(JointDuties(state,plan,copy,pointRoutes)==JointDutyState.Compatible,"Changed parent duties require shared physical recovery or native route proof.");
                    accepted=HouseholdJointMovement.Restore(plan,old).VerifiedNext(copy);
                }
            }
            if(index<0)state.HouseholdJointProgress.Add(copy);else state.HouseholdJointProgress[index]=copy;
            if(copy.Phase==HouseholdJointPhase.Returning)
            {
                var service=state.HouseholdInnServices.Find(s=>s.HouseholdDayId==copy.HouseholdDayId && s.Materialized && !s.ReleasedAt.HasValue);
                if(service!=null){var released=HouseholdInnServiceAdmission.Copy(service);released.ReleasedAt=copy.ReturnStartedAt;
                    state.HouseholdInnServices[state.HouseholdInnServices.IndexOf(service)]=released;}
            }
            if(old!=null && old.HouseholdDayId!=copy.HouseholdDayId)householdJointControllers.Remove(old.HouseholdDayId);
            if(old!=null && old.HouseholdDayId!=copy.HouseholdDayId)
                state.HouseholdJointSimulations.RemoveAll(s=>s.Base.SettlementId==copy.SettlementId && s.Base.HomeKey==copy.HomeKey);
            householdJointControllers[copy.HouseholdDayId]=new JointControllerCache{Record=copy,Controller=accepted,
                HouseholdRevision=HouseholdDayRevision,RoutineRevision=RoutineRevision,AppointmentRevision=AppointmentRevision,
                LeisureUntil=JointLeisureUntil(state.HouseholdDays.Find(p=>p.Id==copy.HouseholdDayId))};
            BumpHouseholdJointRevision();
        }
        private static void ResolveJointParents(WorldState world,HouseholdJointProgress record)
        {
            var town=world.Settlements.Find(t=>t.SettlementId==record.SettlementId);
            var home=town?.Households.Find(h=>h.BuildingKey==record.HomeKey && h.Generation==record.Generation);
            Require(home!=null && home.Residents.Exists(p=>p.Id==record.FirstResidentId && p.IsAlive && !p.IsQuestProtected) &&
                home.Residents.Exists(p=>p.Id==record.SecondResidentId && p.IsAlive && !p.IsQuestProtected),"Shared parent identity is no longer available.");
            Require(!world.NativePeople.Exists(p=>p.Id==record.FirstResidentId || p.Id==record.SecondResidentId),"Native duty owns a shared participant.");
        }
        private static HouseholdDayPlan ResolveJointSource(WorldState world,HouseholdJointProgress record)
        {
            ResolveJointParents(world,record);
            var plan=world.HouseholdDays.Find(p=>p.Id==record.HouseholdDayId);
            Require(plan!=null && plan.JointInn!=null && HouseholdJointMovement.SourceFingerprint(plan)==record.SourceFingerprint,
                "Shared household progress source changed.");
            Household home;ResidentRoutine first,second;ResolveHouseholdDaySources(world,plan,out home,out first,out second);
            var validation=HouseholdDayPlan.Copy(plan);validation.Status="planned";validation.JointSuppressed=false;
            HouseholdDayPlanner.ValidateSources(validation,home,first,second,world.WorldSeed);
            return plan;
        }
        private enum JointDutyState {Compatible,Conflict,PendingProof}
        private static JointDutyState JointDuties(WorldState world,HouseholdDayPlan plan,HouseholdJointProgress record,
            IList<AppointmentPlaceRoute> pointRoutes)
        {
            // Observed homeward recovery remains recordable after preemption.
            // It must not be confused with permission for more outward travel.
            if(record.Retired || record.Phase==HouseholdJointPhase.Returning || record.Phase==HouseholdJointPhase.Completed ||
                record.Phase==HouseholdJointPhase.Cancelled)return JointDutyState.Compatible;
            if(plan.Status!="planned" || plan.JointSuppressed)return JointDutyState.Conflict;
            if(record.ObservedAt>=JointLeisureUntil(plan))return JointDutyState.Conflict;
            Household home;ResidentRoutine first,second;ResolveHouseholdDaySources(world,plan,out home,out first,out second);
            HouseholdAppointmentDay composed;string reason;HouseholdDayCompositionFailure failure;
            if(!HouseholdDayAppointments.TryCompose(plan,home,first,second,world.WorldSeed,world.Appointments,pointRoutes,
                record.ObservedAt,out composed,out reason,out failure))
                return failure==HouseholdDayCompositionFailure.RequiredAppointmentUnavailable?JointDutyState.PendingProof:JointDutyState.Conflict;
            return composed.Plan.JointSuppressed?JointDutyState.Conflict:JointDutyState.Compatible;
        }
        private static double JointLeisureUntil(HouseholdDayPlan plan)
        {
            double end=double.PositiveInfinity;
            foreach(var outing in new[]{plan.JointInn.First,plan.JointInn.Second})
                foreach(var visit in ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary).ScheduledVisits())
                    end=Math.Min(end,plan.AbsoluteDay*86400d+visit.LeaveAt);
            return end;
        }
        private static List<HouseholdJointProgress> CopyHouseholdJointProgress(WorldState source,WorldState copied)
        {
            var result=new List<HouseholdJointProgress>();if(source.Version<69)return result;
            Require(source.HouseholdJointProgress!=null && source.HouseholdJointProgress.Count<=100000,"Missing or unbounded shared progress.");
            var owners=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in source.HouseholdJointProgress)
            {
                Require(source.Version>=74 || entry.RetirementReason!="simulated-return-home" && entry.RetirementReason!="missed-shared-departure",
                    "Old schema contains future simulated household retirement.");
                var copy=source.Version<70?Core.HouseholdJointProgress.CopyLegacy69(entry):Core.HouseholdJointProgress.Copy(entry);
                Require(owners.Add(copy.SettlementId+":"+copy.HomeKey),"Duplicate shared household progress owner.");
                var town=copied.Settlements.Find(t=>t.SettlementId==copy.SettlementId);
                var home=town?.Households.Find(h=>h.BuildingKey==copy.HomeKey && h.Generation==copy.Generation);
                if(home==null)home=copied.HouseholdHistory.Find(h=>h.SettlementId==copy.SettlementId &&
                    h.Household.BuildingKey==copy.HomeKey && h.Household.Generation==copy.Generation)?.Household;
                Require(home!=null && home.Residents.Exists(p=>p.Id==copy.FirstResidentId) &&
                    home.Residents.Exists(p=>p.Id==copy.SecondResidentId),"Shared progress actors are not members of its household generation.");
                var day=copied.HouseholdDays.Find(p=>p.Id==copy.HouseholdDayId);
                Require(!copy.Active || day!=null,"Active shared progress lost its retained route source.");
                // Stale source history is inert, not rebound to different routes
                // or identities. Current route/phase corruption remains fatal.
                if(day!=null && HouseholdJointMovement.SourceFingerprint(day)==copy.SourceFingerprint)
                    HouseholdJointMovement.Restore(day,copy);
                result.Add(copy);
            }
            return result;
        }
    }
}
