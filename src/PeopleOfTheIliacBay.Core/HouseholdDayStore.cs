using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public int HouseholdDayRevision {get;private set;}
        private void BumpHouseholdDayRevision(){if(HouseholdDayRevision<int.MaxValue)HouseholdDayRevision++;}
        public HouseholdDayPlan ReadHouseholdDay(int settlement,int home,int day)
        {
            var plan=state.HouseholdDays.Find(p=>p.SettlementId==settlement && p.HomeKey==home && p.AbsoluteDay==day);
            return plan==null?null:HouseholdDayPlan.Copy(plan);
        }
        // Ownership is needed before native caches bind, without cloning routes
        // or treating this metadata lookup as proof that a schedule is executable.
        public bool HasPlannedHouseholdDay(int settlement,string residentId,int day)
        {
            return state.HouseholdDays.Exists(p=>p.SettlementId==settlement && p.AbsoluteDay==day &&
                p.Status=="planned" && (p.First.ResidentId==residentId || p.Second.ResidentId==residentId));
        }
        // One immutable paired publication. The native adapter still owns the
        // final actor/appointment/care/alarm gate and must publish both caches
        // together before activating this day's schedules.
        public bool TryPublishHouseholdDay(HouseholdDayPlan source,double now,out string reason)
        {
            reason="invalid household publication time";
            if(!Finite(now) || now<0)return false;
            HouseholdDayPlan plan;
            try{plan=HouseholdDayPlan.Copy(source);}catch(ArgumentException error){reason=error.Message;return false;}
            if(plan.Status!="planned" || now<plan.PlannedAt || now>=(plan.AbsoluteDay+1d)*86400)return false;
            if(state.HouseholdJointProgress.Exists(p=>p.SettlementId==plan.SettlementId && p.HomeKey==plan.HomeKey &&
                p.Active && p.HouseholdDayId!=plan.Id))
            {reason="shared household travel still owns the retained route source";return false;}
            Household home;ResidentRoutine first,second;
            try
            {
                ResolveHouseholdDaySources(state,plan,out home,out first,out second);
                HouseholdDayPlanner.ValidateSources(plan,home,first,second,state.WorldSeed);
            }
            catch(ArgumentException error){reason=error.Message;return false;}
            int index=state.HouseholdDays.FindIndex(p=>p.SettlementId==plan.SettlementId && p.HomeKey==plan.HomeKey);
            if(index>=0)
            {
                var current=state.HouseholdDays[index];
                if(current.AbsoluteDay==plan.AbsoluteDay && current.Generation==plan.Generation)
                {
                    bool same=current.Status=="planned" && HouseholdDayPlan.Equivalent(current,plan);
                    reason=same?"already published":"household day already settled";return same;
                }
                if(plan.Generation<current.Generation || plan.Generation==current.Generation &&
                    (plan.AbsoluteDay<current.AbsoluteDay || now<(current.AbsoluteDay+1d)*86400))
                {reason="household day cannot replace newer or unfinished history";return false;}
            }
            try
            {
                var members=new[]{plan.First,plan.Second};var routines=new[]{first,second};
                for(int member=0;member<2;member++)
                {
                    var routine=routines[member];
                    if(!PreservesHistoryAtPublication(plan,members[member],routine,now))
                    {reason="delayed publication would rewrite observed day history";return false;}
                    var current=ResidentRoutineDay.Schedule(home.BuildingKey,routine,plan.WakeMinute,plan.SleepMinute).EvaluateSeconds(now);
                    var replacement=HouseholdDayPlanner.Schedule(plan,routine.ResidentId,routine).EvaluateSeconds(now);
                    if(current.BuildingKey!=home.BuildingKey || current.Activity!=Activity.Home && current.Activity!=Activity.Sleep)
                    {reason="parent has not returned home";return false;}
                    if(replacement.BuildingKey!=home.BuildingKey || replacement.Activity!=Activity.Home && replacement.Activity!=Activity.Sleep)
                    {reason="replacement would start a parent away from home";return false;}
                    if(IsResidentAway(routine.ResidentId,now)){reason="parent is away";return false;}
                }
            }
            catch(ArgumentException error){reason=error.Message;return false;}
            if(index>=0 && state.HouseholdDays[index].Id!=plan.Id)
            {
                // A finished previous event no longer owns interior motion.
                // Retain its bounded payment cursors, not dangling routes into
                // a dated source that is about to be replaced.
                string previousId=state.HouseholdDays[index].Id;var obsolete=new List<string>();
                foreach(var visit in state.ObservedVisits)if(visit.HouseholdDayId==previousId)obsolete.Add(visit.ResidentId);
                foreach(string residentId in obsolete)RemoveObservedVisit(residentId);
            }
            if(index<0)state.HouseholdDays.Add(plan);else state.HouseholdDays[index]=plan;
            BumpHouseholdDayRevision();reason="published";return true;
        }
        private static bool PreservesHistoryAtPublication(HouseholdDayPlan plan,HouseholdDayMember member,ResidentRoutine routine,double now)
        {
            double localNow=Math.Max(0,now-plan.AbsoluteDay*86400d);
            var outings=ResidentRoutineDay.CopyOutings(routine);
            for(int index=0;index<outings.Count;index++)
            {
                var outing=outings[index];
                if(outing.DepartureSeconds<localNow && !member.Outings.Exists(s=>s.SourceOutingIndex==index &&
                    s.DepartureSeconds==outing.DepartureSeconds))return false;
            }
            if(plan.JointInn!=null)
            {
                var joint=plan.First.ResidentId==member.ResidentId?plan.JointInn.First:plan.JointInn.Second;
                if(joint.DepartureSeconds<localNow)return false;
            }
            return true;
        }
        public bool CancelHouseholdDay(string id,double now)
        {
            if(!Finite(now) || now<0)throw new ArgumentOutOfRangeException(nameof(now));
            var plan=state.HouseholdDays.Find(p=>p.Id==id);
            if(plan==null || plan.Status!="planned" || now<plan.PlannedAt)return false;
            plan.Status="cancelled";BumpHouseholdDayRevision();return true;
        }
        public bool SuppressHouseholdJoint(string id,double now)
        {
            if(!Finite(now) || now<0)throw new ArgumentOutOfRangeException(nameof(now));
            var plan=state.HouseholdDays.Find(p=>p.Id==id);
            if(plan==null || plan.Status!="planned" || plan.JointInn==null || plan.JointSuppressed || now<plan.PlannedAt)return false;
            double day=plan.AbsoluteDay*86400d;
            double starts=day+Math.Min(plan.JointInn.First.DepartureSeconds,plan.JointInn.Second.DepartureSeconds);
            // Started travel needs a separate physical continuation; a completed
            // outing must retain its history. This flag only cancels future plans.
            // Retain both routes even after cancellation; they remain real facts.
            if(now>=starts)return false;
            plan.JointSuppressed=true;BumpHouseholdDayRevision();return true;
        }
        public bool TryHouseholdDaySchedules(int settlement,int home,int day,out DailySchedule first,out DailySchedule second,out string reason)
        {
            first=second=null;reason="no active household plan for this date";
            var plan=state.HouseholdDays.Find(p=>p.SettlementId==settlement && p.HomeKey==home && p.AbsoluteDay==day && p.Status=="planned");
            if(plan==null)return false;
            try
            {
                Household household;ResidentRoutine a,b;ResolveHouseholdDaySources(state,plan,out household,out a,out b);
                HouseholdDayPlanner.ValidateSources(plan,household,a,b,state.WorldSeed);
                var sa=HouseholdDayPlanner.Schedule(plan,a.ResidentId,a);var sb=HouseholdDayPlanner.Schedule(plan,b.ResidentId,b);
                first=sa;second=sb;reason="resolved";return true;
            }
            catch(ArgumentException error){reason=error.Message;return false;}
        }
        private static void ResolveHouseholdDaySources(WorldState world,HouseholdDayPlan plan,
            out Household home,out ResidentRoutine first,out ResidentRoutine second)
        {
            var town=world.Settlements.Find(t=>t.SettlementId==plan.SettlementId);
            home=town?.Households.Find(h=>h.BuildingKey==plan.HomeKey && h.Generation==plan.Generation);
            var routines=world.Routines.Find(t=>t.SettlementId==plan.SettlementId);
            first=routines?.Residents.Find(r=>r.ResidentId==plan.First.ResidentId);
            second=routines?.Residents.Find(r=>r.ResidentId==plan.Second.ResidentId);
            Require(home!=null && first!=null && second!=null,"Missing household day source.");
            Require(!world.NativePeople.Exists(p=>p.Id==plan.First.ResidentId || p.Id==plan.Second.ResidentId),"Native duties own this parent.");
        }
        private static List<HouseholdDayPlan> CopyHouseholdDays(WorldState source,WorldState copied)
        {
            var result=new List<HouseholdDayPlan>();
            Require(source.HouseholdDays!=null && source.HouseholdDays.Count<=100000,"Missing or unbounded household days.");
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var record in source.HouseholdDays)
            {
                var plan=HouseholdDayPlan.Copy(record);
                Require(keys.Add(plan.SettlementId+":"+plan.HomeKey),"Duplicate household day owner.");
                var town=copied.Settlements.Find(t=>t.SettlementId==plan.SettlementId);
                var home=town?.Households.Find(h=>h.BuildingKey==plan.HomeKey && h.Generation==plan.Generation);
                if(home==null)home=copied.HouseholdHistory.Find(h=>h.SettlementId==plan.SettlementId &&
                    h.Household.BuildingKey==plan.HomeKey && h.Household.Generation==plan.Generation)?.Household;
                Require(home!=null && home.Residents.Exists(p=>p.Id==plan.First.ResidentId) &&
                    home.Residents.Exists(p=>p.Id==plan.Second.ResidentId) &&
                    plan.ChildIds.TrueForAll(id=>home.Residents.Exists(p=>p.Id==id && p.AgeGroup==ResidentAgeGroup.Child)),
                    "Household day identities are not members of this household generation.");
                // A later route migration, death or generation change may make
                // an old record stale. Keep its bounded identity for replay
                // protection; no consumer may resolve it against different data.
                var routines=copied.Routines.Find(t=>t.SettlementId==plan.SettlementId);
                var first=routines?.Residents.Find(r=>r.ResidentId==plan.First.ResidentId);
                var second=routines?.Residents.Find(r=>r.ResidentId==plan.Second.ResidentId);
                bool current=plan.Status=="planned" && first!=null && second!=null &&
                    home.Residents.Exists(p=>p.Id==plan.First.ResidentId && p.IsAlive && !p.IsQuestProtected) &&
                    home.Residents.Exists(p=>p.Id==plan.Second.ResidentId && p.IsAlive && !p.IsQuestProtected) &&
                    !copied.NativePeople.Exists(p=>p.Id==plan.First.ResidentId || p.Id==plan.Second.ResidentId) &&
                    plan.First.RoutineFingerprint==RoutineRecoveryIdentity.Fingerprint(first,plan.HomeKey,plan.WakeMinute,plan.SleepMinute) &&
                    plan.Second.RoutineFingerprint==RoutineRecoveryIdentity.Fingerprint(second,plan.HomeKey,plan.WakeMinute,plan.SleepMinute);
                if(current)HouseholdDayPlanner.ValidateSources(plan,home,first,second,copied.WorldSeed);
                result.Add(plan);
            }
            return result;
        }
    }
}
