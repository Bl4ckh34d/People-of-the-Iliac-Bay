using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Caller samples these from the actual bodies after successful native placement.
    // A route target alone is never a materialization receipt.
    public sealed class HouseholdJointPlacement
    {
        public string ResidentId;
        public SavedRoutePoint Position;
    }
    public sealed partial class WorldStateStore
    {
        public bool TryPublishJointHouseholdDay(HouseholdDayPlan source,double now,out string reason)
        {
            reason="invalid shared day";HouseholdDayPlan plan;HouseholdJointProgress waiting;HouseholdJointSimulation simulation;
            try
            {
                plan=HouseholdDayPlan.Copy(source);waiting=HouseholdJointMovement.Create(plan,now).Snapshot();
                simulation=HouseholdJointSimulation.Begin(plan,waiting,now);
            }
            catch(ArgumentException error){reason=error.Message;return false;}
            var old=state.HouseholdJointProgress.Find(p=>p.SettlementId==plan.SettlementId && p.HomeKey==plan.HomeKey);
            if(old!=null && old.Active){reason="previous joint event still owns household";return false;}
            if(!TryPublishHouseholdDay(plan,now,out reason))return false;
            if(old!=null)state.HouseholdJointProgress[state.HouseholdJointProgress.IndexOf(old)]=waiting;
            else state.HouseholdJointProgress.Add(waiting);
            state.HouseholdJointSimulations.RemoveAll(s=>s.Base.SettlementId==plan.SettlementId && s.Base.HomeKey==plan.HomeKey);
            state.HouseholdJointSimulations.Add(simulation);householdJointControllers.Remove(plan.Id);BumpHouseholdJointRevision();return true;
        }
        public List<HouseholdJointSimulation> ReadHouseholdSimulationBatch(int start,int maximum,out int next)
        {
            Require(start>=0 && maximum>=1 && maximum<=32,"Invalid bounded household simulation batch.");
            var result=new List<HouseholdJointSimulation>();int count=state.HouseholdJointSimulations.Count;
            next=count==0?0:start%count;
            for(int i=0;i<Math.Min(maximum,count);i++)
            {var sim=state.HouseholdJointSimulations[next];next=(next+1)%count;if(sim.Active)result.Add(HouseholdJointSimulation.Copy(sim));}
            return result;
        }
        private bool SimulationOwnsHousehold(int settlement,int home)
        {return state.HouseholdJointSimulations.Exists(s=>s.Active && s.Base.SettlementId==settlement && s.Base.HomeKey==home);}
        public HouseholdJointSimulation ReadHouseholdJointSimulation(int settlement,int home)
        {
            var saved=state.HouseholdJointSimulations.Find(s=>s.Base.SettlementId==settlement && s.Base.HomeKey==home);
            return saved==null?null:HouseholdJointSimulation.Copy(saved);
        }
        public HouseholdJointSimulation AdvanceHouseholdJointLifecycle(string householdDayId,double now,
            Func<int,double,NpcInnServiceTariff> nativeCalendar,bool firstAvailable=true,bool secondAvailable=true,bool paused=false,
            Func<NpcInnServiceCatalog,NpcInnServiceSeatPair,bool> canReservePair=null)
        {
            var sim=AdvanceHouseholdJointSimulation(householdDayId,now,firstAvailable,secondAvailable,paused);
            if(paused || !firstAvailable || !secondAvailable || sim.PauseReason!=null && !sim.RequiresRecovery ||
                sim.Phase!=HouseholdJointPhase.AtInn && sim.Phase!=HouseholdJointPhase.Visiting)return sim;
            var plan=state.HouseholdDays.Find(p=>p.Id==householdDayId);
            double end=HouseholdServiceIdentity(plan,plan.First.ResidentId,Math.Max(sim.StartedAt,sim.PhaseSince)).EndsAt;
            var firstVisit=CapturedHouseholdInnVisit(sim,sim.Base.FirstResidentId);
            var secondVisit=CapturedHouseholdInnVisit(sim,sim.Base.SecondResidentId);
            bool observedPaid=firstVisit?.InnConsumption?.Outcome==NpcInnConsumptionOutcome.Purchased &&
                secondVisit?.InnConsumption?.Outcome==NpcInnConsumptionOutcome.Purchased;
            var service=observedPaid?ReadHouseholdInnServiceAdmission(householdDayId):AdvanceHouseholdInnService(householdDayId,now,nativeCalendar,canReservePair);
            double? observedExit=observedPaid?(double?)Math.Max(firstVisit.EndsAt+sim.InnClockOffset,secondVisit.EndsAt+sim.InnClockOffset):null;
            if(observedExit.HasValue && now<observedExit.Value)
            {
                var retained=state.HouseholdJointSimulations.Find(s=>s.Base.HouseholdDayId==householdDayId);
                var dwelling=HouseholdJointSimulation.Copy(retained);dwelling.AdvancedThrough=now;dwelling.Revision++;
                state.HouseholdJointSimulations[state.HouseholdJointSimulations.IndexOf(retained)]=dwelling;BumpHouseholdJointRevision();
                return HouseholdJointSimulation.Copy(dwelling);
            }
            double unavailableAt=Math.Min(end,Math.Max(sim.StartedAt,sim.PhaseSince)+30);
            if(service==null && !observedExit.HasValue && now<unavailableAt)return sim;
            if(service!=null && service.Outcome=="pending")return sim;
            var next=HouseholdJointMovement.ContinueService(plan,sim,service,now,unavailableAt,observedExit);
            var old=state.HouseholdJointSimulations.Find(s=>s.Base.HouseholdDayId==householdDayId);
            state.HouseholdJointSimulations[state.HouseholdJointSimulations.IndexOf(old)]=next;BumpHouseholdJointRevision();
            if(service!=null && next.Phase==HouseholdJointPhase.Returning)
            {
                var retained=state.HouseholdInnServices.Find(s=>s.Id==service.Id);
                var released=HouseholdInnServiceAdmission.Copy(retained);released.ReleasedAt=observedExit??released.ReturnAt;
                state.HouseholdInnServices[state.HouseholdInnServices.IndexOf(retained)]=released;
            }
            return next.Phase==HouseholdJointPhase.Returning?AdvanceHouseholdJointSimulation(householdDayId,now,firstAvailable,secondAvailable,paused):
                HouseholdJointSimulation.Copy(next);
        }
        public bool ReleaseCompletedHouseholdSimulation(string householdDayId,double now)
        {
            var saved=state.HouseholdJointSimulations.Find(s=>s.Base.HouseholdDayId==householdDayId);
            if(saved==null || !saved.Active || saved.Phase!=HouseholdJointPhase.Completed ||
                !ValidTime(now) || now<saved.AdvancedThrough || saved.Revision==int.MaxValue)return false;
            var actual=RequireSimulationAnchor(saved);if(actual.Sequence==int.MaxValue)return false;
            var released=HouseholdJointSimulation.Copy(saved);released.ReleasedHomeAt=now;released.Revision++;
            var retained=Core.HouseholdJointProgress.Copy(actual);retained.Sequence++;retained.RetiredAt=now;retained.RetirementReason="simulated-return-home";
            // This is simulated residence, not a new physical arrival. Preserve
            // both actual cursors and their observation time as historical facts.
            state.HouseholdJointSimulations[state.HouseholdJointSimulations.IndexOf(saved)]=HouseholdJointSimulation.Copy(released);
            state.HouseholdJointProgress[state.HouseholdJointProgress.IndexOf(actual)]=Core.HouseholdJointProgress.Copy(retained);
            householdJointControllers.Remove(householdDayId);BumpHouseholdJointRevision();return true;
        }
        public bool TryCancelMissedHouseholdDeparture(string householdDayId,double now)
        {
            var saved=state.HouseholdJointSimulations.Find(s=>s.Base.HouseholdDayId==householdDayId);
            var plan=state.HouseholdDays.Find(p=>p.Id==householdDayId);
            if(saved==null || !saved.Active || saved.Phase!=HouseholdJointPhase.Waiting || plan==null ||
                !ValidTime(now) || now<saved.AdvancedThrough || now<JointLeisureUntil(plan) || saved.Revision==int.MaxValue)return false;
            var actual=RequireSimulationAnchor(saved);if(actual.Sequence==int.MaxValue)return false;
            var cancelled=HouseholdJointSimulation.Copy(saved);cancelled.CancelledAt=now;cancelled.Revision++;
            var retired=Core.HouseholdJointProgress.Copy(actual);retired.Sequence++;retired.RetiredAt=now;retired.RetirementReason="missed-shared-departure";
            state.HouseholdJointSimulations[state.HouseholdJointSimulations.IndexOf(saved)]=HouseholdJointSimulation.Copy(cancelled);
            state.HouseholdJointProgress[state.HouseholdJointProgress.IndexOf(actual)]=Core.HouseholdJointProgress.Copy(retired);
            householdJointControllers.Remove(householdDayId);BumpHouseholdJointRevision();return true;
        }
        public HouseholdJointSimulation BeginHouseholdJointSimulation(string householdDayId,int expectedActualSequence,double now,
            IList<AppointmentPlaceRoute> pointRoutes=null)
        {
            var actual=state.HouseholdJointProgress.Find(p=>p.HouseholdDayId==householdDayId);
            if(actual!=null)now=HouseholdInnVisitProvenance.CanonicalInstant(now,actual.ObservedAt);
            Require(actual!=null && actual.Active && actual.Sequence==expectedActualSequence &&
                Core.HouseholdJointProgress.Time(now) && now>=actual.ObservedAt,"Shared simulation handoff has a stale actual source or clock.");
            var plan=ResolveJointSource(state,actual);var policy=Core.HouseholdJointProgress.Copy(actual);policy.ObservedAt=now;
            Require(JointDuties(state,plan,policy,pointRoutes)==JointDutyState.Compatible && JointSimulationParentsFree(actual,now),
                "Required parent duties prevent simulation handoff.");
            int index=state.HouseholdJointSimulations.FindIndex(s=>s.Base.SettlementId==actual.SettlementId && s.Base.HomeKey==actual.HomeKey);
            var old=index<0?null:state.HouseholdJointSimulations[index];
            if(old!=null && old.Active)
            {
                Require(Core.HouseholdJointProgress.Equivalent(actual,old.Base) && now>=old.AdvancedThrough,"Shared simulation already has a different owner.");
                return HouseholdJointSimulation.Copy(old);
            }
            var created=HouseholdJointSimulation.Begin(plan,actual,now);
            var firstVisit=CapturedHouseholdInnVisit(created,actual.FirstResidentId);
            var secondVisit=CapturedHouseholdInnVisit(created,actual.SecondResidentId);
            if(firstVisit!=null && secondVisit!=null && firstVisit.ObservedAt==secondVisit.ObservedAt)
                created.InnClockOffset=now-firstVisit.ObservedAt;
            if(old!=null && old.Base.HouseholdDayId==householdDayId)
            {Require(old.Revision<int.MaxValue,"Shared simulation revision exhausted.");created.Revision=old.Revision+1;}
            Require(index>=0 || state.HouseholdJointSimulations.Count<100000,"Shared simulation ledger is full.");
            if(index<0)state.HouseholdJointSimulations.Add(created);else state.HouseholdJointSimulations[index]=created;
            householdJointControllers.Remove(householdDayId);BumpHouseholdJointRevision();return HouseholdJointSimulation.Copy(created);
        }
        public HouseholdJointSimulation AdvanceHouseholdJointSimulation(string householdDayId,double now,
            bool firstAvailable=true,bool secondAvailable=true,bool paused=false,IList<AppointmentPlaceRoute> pointRoutes=null)
        {
            var saved=state.HouseholdJointSimulations.Find(s=>s.Base.HouseholdDayId==householdDayId);
            var actual=RequireSimulationAnchor(saved);
            now=HouseholdInnVisitProvenance.CanonicalInstant(now,saved.AdvancedThrough);
            Require(Core.HouseholdJointProgress.Time(now) && now>=saved.AdvancedThrough,"Shared simulation clock moved backward.");
            var canonical=state.HouseholdDays.Find(p=>p.Id==householdDayId);
            Require(canonical!=null && HouseholdJointMovement.SourceFingerprint(canonical)==saved.Base.SourceFingerprint,
                "Shared simulation lost its canonical route source.");
            HouseholdJointMovement.ValidateSimulation(canonical,saved);
            HouseholdJointSimulation next;
            bool homeward=saved.Phase==HouseholdJointPhase.Returning || saved.Phase==HouseholdJointPhase.Completed;
            if(saved.RequiresRecovery && !homeward)next=HoldHouseholdSimulation(saved,now,saved.PauseReason);
            else if(!JointSimulationParentsFree(actual,now))next=HoldHouseholdSimulation(saved,now,"partner-unavailable");
            else
            {
                HouseholdDayPlan plan=null;
                try{plan=ResolveJointSource(state,actual);}
                catch(ArgumentException){/* The retained route is not permission to use changed routines. */}
                if(plan==null && homeward)
                {plan=HouseholdDayPlan.Copy(canonical);plan.Status="planned";plan.JointSuppressed=false;}
                if(plan==null)next=HoldHouseholdSimulation(saved,now,"source-unavailable");
                else
                {
                    var policy=Core.HouseholdJointProgress.Copy(actual);
                    if(saved.Phase==HouseholdJointPhase.Returning || saved.Phase==HouseholdJointPhase.Completed)policy.Phase=saved.Phase;
                    // Composition checks the whole dated activity. A late tick
                    // must not erase an earlier valid trip at its closing time.
                    policy.ObservedAt=saved.AdvancedThrough;
                    // Finishing an admitted visit's egress after its planned
                    // leisure deadline is homeward work, not renewed leisure.
                    if(saved.Phase==HouseholdJointPhase.Visiting && policy.ObservedAt>=JointLeisureUntil(plan) &&
                        (state.HouseholdInnServices.Exists(s=>s.HouseholdDayId==householdDayId && s.Outcome!="pending") ||
                        CapturedHouseholdInnVisit(saved,actual.FirstResidentId)?.InnConsumption?.Outcome==NpcInnConsumptionOutcome.Purchased &&
                        CapturedHouseholdInnVisit(saved,actual.SecondResidentId)?.InnConsumption?.Outcome==NpcInnConsumptionOutcome.Purchased))
                        policy.Phase=HouseholdJointPhase.Returning;
                    var duty=JointDuties(state,plan,policy,pointRoutes);
                    if(duty==JointDutyState.PendingProof)next=HoldHouseholdSimulation(saved,now,"route-proof-unavailable");
                    else if(duty==JointDutyState.Conflict)next=HoldHouseholdSimulation(saved,now,"duties-changed");
                    else next=HouseholdJointSimulation.Advance(plan,saved,now,firstAvailable,secondAvailable,paused);
                }
            }
            if(next.Revision!=saved.Revision)
            {state.HouseholdJointSimulations[state.HouseholdJointSimulations.IndexOf(saved)]=next;BumpHouseholdJointRevision();}
            return HouseholdJointSimulation.Copy(next);
        }
        private static HouseholdJointSimulation HoldHouseholdSimulation(HouseholdJointSimulation saved,double now,string reason)
        {
            var next=HouseholdJointSimulation.Copy(saved);
            if(now==saved.AdvancedThrough && reason==saved.PauseReason)return next;
            Require(saved.Revision<int.MaxValue,"Shared simulation revision exhausted.");
            next.PauseReason=reason;
            if(next.Phase==HouseholdJointPhase.Visiting && (reason=="partner-unavailable" || reason=="game-paused" || reason=="route-proof-unavailable"))
                next.InnClockOffset+=now-saved.AdvancedThrough;
            next.AdvancedThrough=now;next.Revision++;return next;
        }
        public HouseholdJointProgress ReconstructHouseholdJointSimulation(string householdDayId,int expectedRevision,double now,
            HouseholdJointPlacement first,HouseholdJointPlacement second,IList<AppointmentPlaceRoute> pointRoutes=null)
        {
            var saved=state.HouseholdJointSimulations.Find(s=>s.Base.HouseholdDayId==householdDayId);
            Require(saved!=null,"Missing shared simulation to reconstruct.");
            var actual=state.HouseholdJointProgress.Find(p=>p.HouseholdDayId==householdDayId);
            if(!saved.Active)
            {
                Require(saved.Revision>0 && expectedRevision==saved.Revision-1 && saved.ReconstructedAt==now &&
                    actual!=null && actual.Sequence==saved.Base.Sequence+1 && actual.ObservedAt==now &&
                    SameMaterialized(first,actual.FirstResidentId,actual.First.Position) &&
                    SameMaterialized(second,actual.SecondResidentId,actual.Second.Position),"Stale shared reconstruction retry.");
                return Core.HouseholdJointProgress.Copy(actual);
            }
            actual=RequireSimulationAnchor(saved);
            now=HouseholdInnVisitProvenance.CanonicalInstant(now,saved.AdvancedThrough);
            Require(!saved.RequiresRecovery || saved.Phase==HouseholdJointPhase.Waiting ||
                saved.Phase==HouseholdJointPhase.Returning || saved.Phase==HouseholdJointPhase.Completed,
                "Moving shared recovery needs an explicit recovery decision before materialization.");
            Require(saved.Revision==expectedRevision && saved.Revision<int.MaxValue &&
                Core.HouseholdJointProgress.Time(now) && now>=saved.AdvancedThrough && JointSimulationParentsFree(actual,now),
                "Shared reconstruction has a stale revision, clock or parent priority.");
            HouseholdDayPlan plan;
            try{plan=ResolveJointSource(state,actual);}
            catch(ArgumentException)
            {
                Require(saved.Phase==HouseholdJointPhase.Returning || saved.Phase==HouseholdJointPhase.Completed,"Only homeward recovery retains obsolete routes.");
                plan=HouseholdDayPlan.Copy(state.HouseholdDays.Find(p=>p.Id==householdDayId));plan.Status="planned";plan.JointSuppressed=false;
            }
            var policy=Core.HouseholdJointProgress.Copy(actual);policy.ObservedAt=now;
            if(saved.Phase==HouseholdJointPhase.Returning || saved.Phase==HouseholdJointPhase.Completed)policy.Phase=saved.Phase;
            var duty=JointDuties(state,plan,policy,pointRoutes);
            Require(duty==JointDutyState.Compatible || duty==JointDutyState.Conflict &&
                (saved.Phase==HouseholdJointPhase.Waiting || saved.Phase==HouseholdJointPhase.Returning || saved.Phase==HouseholdJointPhase.Completed),
                "Changed parent duties require an explicit shared recovery before materialization.");
            var materialized=HouseholdJointMovement.ReconstructSimulation(plan,saved,now,first,second);
            var consumed=HouseholdJointSimulation.Copy(saved);consumed.ReconstructedAt=now;consumed.Revision++;
            // All validation/copies precede publication; neither owner changes on failure.
            var result=Core.HouseholdJointProgress.Copy(materialized);
            state.HouseholdJointProgress[state.HouseholdJointProgress.IndexOf(actual)]=materialized;
            state.HouseholdJointSimulations[state.HouseholdJointSimulations.IndexOf(saved)]=consumed;
            householdJointControllers.Remove(householdDayId);BumpHouseholdJointRevision();return result;
        }
        private static bool SameMaterialized(HouseholdJointPlacement placement,string id,SavedRoutePoint point)
        {return placement!=null && placement.ResidentId==id && placement.Position!=null && placement.Position.X==point.X && placement.Position.Z==point.Z;}
        private HouseholdJointProgress RequireSimulationAnchor(HouseholdJointSimulation saved)
        {
            Require(saved!=null && saved.Active,"Shared simulation is missing or consumed.");
            var actual=state.HouseholdJointProgress.Find(p=>p.HouseholdDayId==saved.Base.HouseholdDayId);
            Require(actual!=null && Core.HouseholdJointProgress.Equivalent(actual,saved.Base),"Shared simulation lost its exact physical anchor.");
            return actual;
        }
        private bool JointSimulationParentsFree(HouseholdJointProgress actual,double now)
        {
            foreach(string id in new[]{actual.FirstResidentId,actual.SecondResidentId})
            {
                var person=ResolveResident(id);var care=ReadNpcHealthCare(id);
                var visit=IndexedObservedVisit(id);
                if(person==null || !person.IsAlive || person.IsQuestProtected || person.NativeIdentity!=null ||
                    visit!=null && visit.HouseholdDayId!=actual.HouseholdDayId && visit.AffectsRoutineDuring(now,now+1) ||
                    state.NativePeople.Exists(p=>p.Id==id) || IsResidentAway(id,now) || ActiveAlarm(id,now)!=null || ActiveNpcAction(id)!=null || ActiveNpcTask(id)!=null ||
                    care!=null && care.Phase!=NpcHealthCarePhase.Recovered || HasNpcTreatmentVisit(id) || GetDialogueComplaint(id)!=null ||
                    state.NpcMagicCareTasks.Exists(t=>t.PersonId==id && t.Phase==NpcMagicCareTaskPhase.Reserved))return false;
            }
            return true;
        }
        private static List<HouseholdJointSimulation> CopyHouseholdJointSimulations(WorldState source,WorldState copied)
        {
            var result=new List<HouseholdJointSimulation>();if(source.Version<72)return result;
            Require(source.HouseholdJointSimulations!=null && source.HouseholdJointSimulations.Count<=100000,"Missing or unbounded household simulations.");
            var homes=new HashSet<string>(StringComparer.Ordinal);var parents=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in source.HouseholdJointSimulations)
            {
                Require(source.Version>=74 || entry.ModelVersion==1 && entry.ServiceAdmissionId==null && entry.ServiceReturnReason==null && entry.HomeArrival==null && !entry.ReleasedHomeAt.HasValue && !entry.CancelledAt.HasValue && entry.InnClockOffset==0,
                    "Old schema contains future household service continuation.");
                var sim=HouseholdJointSimulation.Copy(entry);var anchor=sim.Base;
                Require(homes.Add(anchor.SettlementId+":"+anchor.HomeKey),"Duplicate household simulation owner.");
                var actual=copied.HouseholdJointProgress.Find(p=>p.HouseholdDayId==anchor.HouseholdDayId);
                Require(actual!=null && actual.SourceFingerprint==anchor.SourceFingerprint &&
                    actual.FirstResidentId==anchor.FirstResidentId && actual.SecondResidentId==anchor.SecondResidentId &&
                    (sim.Active?Core.HouseholdJointProgress.Equivalent(actual,anchor):sim.CancelledAt.HasValue?
                        actual.Sequence==anchor.Sequence+1 && actual.RetirementReason=="missed-shared-departure" && actual.RetiredAt==sim.CancelledAt && actual.ObservedAt==anchor.ObservedAt:
                        sim.ReleasedHomeAt.HasValue?
                        actual.Sequence==anchor.Sequence+1 && actual.RetirementReason=="simulated-return-home" && actual.RetiredAt==sim.ReleasedHomeAt &&
                            actual.ObservedAt==anchor.ObservedAt:
                        actual.Sequence>anchor.Sequence && actual.ObservedAt>=sim.ReconstructedAt.Value),
                    "Household simulation does not match its retained physical owner.");
                if(sim.Active)Require(parents.Add(anchor.FirstResidentId) && parents.Add(anchor.SecondResidentId),"A parent has multiple active simulations.");
                var day=copied.HouseholdDays.Find(p=>p.Id==anchor.HouseholdDayId);
                Require(!sim.Active || day!=null && HouseholdJointMovement.SourceFingerprint(day)==anchor.SourceFingerprint,
                    "Active household simulation lost its canonical route source.");
                // Consumed history can be inert; an active owner must retain its exact route.
                if(day!=null && HouseholdJointMovement.SourceFingerprint(day)==anchor.SourceFingerprint)
                    HouseholdJointMovement.ValidateSimulation(day,sim);
                result.Add(sim);
            }
            return result;
        }
    }
}
