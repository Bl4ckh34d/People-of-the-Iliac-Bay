using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public enum HouseholdInnServiceState {Pending,Ready,Unavailable,Blocked}
    internal static class HouseholdInnVisitProvenance
    {
        // Match DFU FullSerializer's native double representation. The CLR's
        // round-trip format retains digits which the Unity/Mono writer drops.
        internal static string WireTime(double value){return value.ToString("G15",CultureInfo.InvariantCulture);}
        internal static double CanonicalInstant(double value,double minimum)
        {return value<minimum && WireTime(value)==WireTime(minimum)?minimum:value;}
        internal static void Validate(string dayId,string fingerprint)
        {
            if(dayId==null && fingerprint==null)return;
            if(string.IsNullOrWhiteSpace(dayId) || dayId.Length>256 || dayId.IndexOf('\n')>=0 || dayId.IndexOf('\r')>=0 ||
                fingerprint==null || fingerprint.Length!=64)throw new ArgumentException("Incomplete shared Inn visit provenance.");
            foreach(char c in fingerprint)if(!(c>='0' && c<='9' || c>='a' && c<='f'))
                throw new ArgumentException("Invalid shared Inn visit fingerprint.");
        }
    }
    public sealed partial class WorldStateStore
    {
        public HouseholdInnServiceState ReadHouseholdInnService(int settlement,int home,double now,IList<AppointmentPlaceRoute> pointRoutes=null)
        {
            if(!ValidTime(now))return HouseholdInnServiceState.Unavailable;
            var progress=state.HouseholdJointProgress.Find(p=>p.SettlementId==settlement && p.HomeKey==home);
            if(progress==null || progress.Phase!=HouseholdJointPhase.Visiting || progress.Retired || now<progress.EnteredAt)
                return HouseholdInnServiceState.Unavailable;
            try
            {
                var plan=ResolveJointSource(state,progress);var policy=HouseholdJointProgress.Copy(progress);policy.ObservedAt=now;
                var duties=JointDuties(state,plan,policy,pointRoutes);
                if(duties==JointDutyState.PendingProof)return HouseholdInnServiceState.Blocked;
                if(duties!=JointDutyState.Compatible)return HouseholdInnServiceState.Unavailable;
                bool pending=false;
                foreach(string member in new[]{progress.FirstResidentId,progress.SecondResidentId})
                {
                    var visit=IndexedObservedVisit(member);
                    if(visit==null){pending=true;continue;}
                    if(visit.HouseholdDayId!=plan.Id || visit.HouseholdSourceFingerprint!=progress.SourceFingerprint)
                        return HouseholdInnServiceState.Unavailable;
                    ValidateHouseholdInnVisit(state,visit,true);
                    if(visit.ObservedAt>now){pending=true;continue;}
                    var receipt=ReadNpcInnConsumption(member);
                    if(receipt==null || receipt.CompletedAt>now){pending=true;continue;}
                    if(receipt.Outcome!=NpcInnConsumptionOutcome.Purchased)return HouseholdInnServiceState.Unavailable;
                }
                return pending?HouseholdInnServiceState.Pending:HouseholdInnServiceState.Ready;
            }
            catch(ArgumentException){return HouseholdInnServiceState.Unavailable;}
        }
        private static bool HasEnteredHouseholdInnOwner(WorldState world,ObservedVisit visit)
        {
            foreach(var progress in world.HouseholdJointProgress)
            {
                if(!progress.Active || progress.EnteredAt<=0 || progress.SettlementId!=visit.SettlementId ||
                    visit.ObservedAt<progress.EnteredAt || progress.FirstResidentId!=visit.ResidentId && progress.SecondResidentId!=visit.ResidentId)continue;
                var plan=world.HouseholdDays.Find(p=>p.Id==progress.HouseholdDayId);
                if(plan?.JointInn!=null && plan.JointInn.InnKey==visit.BuildingKey)return true;
            }
            return false;
        }
        private static void ValidateHouseholdInnVisit(WorldState world,ObservedVisit visit,bool writing,HouseholdJointProgress reconstructedOwner=null)
        {
            HouseholdInnVisitProvenance.Validate(visit.HouseholdDayId,visit.HouseholdSourceFingerprint);
            var plan=world.HouseholdDays.Find(p=>p.Id==visit.HouseholdDayId);
            Require(plan!=null && plan.JointInn!=null && plan.SettlementId==visit.SettlementId &&
                plan.JointInn.InnKey==visit.BuildingKey && (plan.First.ResidentId==visit.ResidentId || plan.Second.ResidentId==visit.ResidentId) &&
                HouseholdJointMovement.SourceFingerprint(plan)==visit.HouseholdSourceFingerprint,"Shared Inn visit does not match its retained family source.");
            var progress=reconstructedOwner??world.HouseholdJointProgress.Find(p=>p.HouseholdDayId==plan.Id);
            Require(progress!=null && progress.SourceFingerprint==visit.HouseholdSourceFingerprint && progress.EnteredAt>0 &&
                visit.ObservedAt>=progress.EnteredAt,"Shared Inn visit lacks an accepted paired entry.");
            var outing=visit.ResidentId==plan.First.ResidentId?plan.JointInn.First:plan.JointInn.Second;
            var timed=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary).ScheduledVisits())[0];
            Require(HouseholdInnVisitProvenance.WireTime(visit.PlannedEndAt)==HouseholdInnVisitProvenance.WireTime(plan.AbsoluteDay*86400d+timed.LeaveAt) &&
                visit.ObservedAt<=progress.EnteredAt+7*86400d,"Shared Inn visit has a different dated visit window.");
            if(writing)
            {
                Require(reconstructedOwner!=null || !world.HouseholdJointSimulations.Exists(s=>s.Active && s.Base.HouseholdDayId==plan.Id),
                    "Simulation owns the paired Inn checkpoint; an old physical controller cannot capture it.");
                var home=world.Settlements.Find(t=>t.SettlementId==plan.SettlementId)?.Households.Find(h=>h.BuildingKey==plan.HomeKey && h.Generation==plan.Generation);
                var owner=home?.Residents.Find(p=>p.Id==visit.ResidentId);
                Require(progress.Phase==HouseholdJointPhase.Visiting && !progress.Retired && plan.Status=="planned" && !plan.JointSuppressed &&
                    owner!=null && owner.IsAlive && !owner.IsQuestProtected,"Shared Inn visit is no longer an executable live visit.");
            }
            else
            {
                Require(progress.Phase==HouseholdJointPhase.Visiting || progress.Phase==HouseholdJointPhase.Returning || progress.Phase==HouseholdJointPhase.Completed,
                    "Historical shared Inn visit has no entered phase.");
                Require(progress.ReturnStartedAt==0 || visit.ObservedAt<=progress.ReturnStartedAt,
                    "Historical shared Inn checkpoint was written after departure.");
                Require(!progress.Retired || visit.ObservedAt<=progress.RetiredAt,"Shared Inn checkpoint follows retirement.");
            }
        }
        private bool CanSupersedeEqualHouseholdInnCursor(ObservedVisit visit,NpcInnConsumptionCursor previous)
        {
            // Keep the payment high-water mark monotone. Only a separately
            // entered, source-bound family event can disambiguate equal ends.
            // An older ordinary visit cannot use this exception to replay.
            if(visit.HouseholdDayId==null || visit.PlannedEndAt!=previous.PlannedEndAt ||
                visit.HouseholdDayId==previous.HouseholdDayId && visit.HouseholdSourceFingerprint==previous.HouseholdSourceFingerprint)return false;
            try
            {
                ValidateHouseholdInnVisit(state,visit,true);
                var progress=state.HouseholdJointProgress.Find(p=>p.HouseholdDayId==visit.HouseholdDayId);
                return progress.EnteredAt>previous.Receipt.CompletedAt;
            }
            catch(ArgumentException){return false;}
        }
        private bool CanSettleHouseholdInn(ObservedVisit visit,double now,IList<AppointmentPlaceRoute> pointRoutes)
        {
            if(visit==null)return false;
            if(visit.HouseholdDayId==null)return true;
            if(now>=visit.PlannedEndAt)return false;
            try
            {
                ValidateHouseholdInnVisit(state,visit,true);
                var progress=state.HouseholdJointProgress.Find(p=>p.HouseholdDayId==visit.HouseholdDayId);
                var current=ResolveJointSource(state,progress);
                var policy=HouseholdJointProgress.Copy(progress);policy.ObservedAt=now;
                return now>=progress.EnteredAt && now>=visit.ObservedAt && JointDuties(state,current,policy,pointRoutes)==JointDutyState.Compatible;
            }
            catch(ArgumentException){return false;}
        }
    }
}
