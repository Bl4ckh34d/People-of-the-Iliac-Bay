using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class HouseholdDayRequest
    {
        public Household Household;
        public int SettlementId,AbsoluteDay,WakeMinute,SleepMinute;
        public uint WorldSeed;
        public double PlannedAt;
        public ResidentRoutine FirstRoutine,SecondRoutine;
        public HouseholdJointInn JointInn;
        // Full absolute intervals; the native caller retains duties, appointments,
        // care, observed travel and danger. No callback result is saved as evidence.
        public Func<string,double,double,bool> IsAvailable,CanCoverChildren;
    }
    public sealed class HouseholdDayRoute
    {
        public int FirstVisitIndex;
        public ResidentDailyOuting Outing;
    }
    public static class HouseholdDayPlanner
    {
        public const int JointVisitIndex=16384;
        // Owned route metadata for native motion/arrival caches. Omitted outings
        // do not renumber later source visits, and selected times remain exact.
        public static List<HouseholdDayRoute> RoutesFor(HouseholdDayPlan source,string residentId,ResidentRoutine routine)
        {
            var plan=HouseholdDayPlan.Copy(source);
            if(plan.Status!="planned")throw new ArgumentException("Household routes require an active dated plan.");
            Schedule(plan,residentId,routine);
            var member=residentId==plan.First.ResidentId?plan.First:plan.Second;
            var originals=ResidentRoutineDay.CopyOutings(routine);var result=new List<HouseholdDayRoute>();
            foreach(var selection in member.Outings)
            {
                int offset=0;for(int index=0;index<selection.SourceOutingIndex;index++)offset+=originals[index].Itinerary.Stops.Count;
                var outing=originals[selection.SourceOutingIndex];outing.DepartureSeconds=selection.DepartureSeconds;
                result.Add(new HouseholdDayRoute{FirstVisitIndex=offset,Outing=outing});
            }
            if(plan.JointInn!=null && !plan.JointSuppressed)
                result.Add(new HouseholdDayRoute{FirstVisitIndex=JointVisitIndex,
                    Outing=residentId==plan.First.ResidentId?plan.JointInn.First:plan.JointInn.Second});
            return result;
        }
        private sealed class Interval{internal double From,Until;}
        private sealed class Candidate{internal int Member,Index;internal ResidentDailyOuting Outing;}
        public static bool SharedDay(uint seed,int settlement,Household household,int day)
        {
            if(household==null || settlement<0 || household.BuildingKey<0 || household.Generation<0 || day<0)
                throw new ArgumentException("Invalid household/day selection.");
            uint basis=StableSeed.Combine(seed,(uint)settlement,(uint)household.BuildingKey,(uint)household.Generation);
            return StableSeed.Combine(basis,(uint)day,0x66616d69u,0x6c796461u)%100<10;
        }
        public static bool TryCreate(HouseholdDayRequest request,out HouseholdDayPlan plan,out string reason)
        {
            plan=null;reason="invalid household day request";
            if(request==null || request.Household==null || request.FirstRoutine==null || request.SecondRoutine==null ||
                request.SettlementId<0 || request.AbsoluteDay<0 || request.WakeMinute<0 || request.SleepMinute>=1440 ||
                request.WakeMinute>=request.SleepMinute || !Finite(request.PlannedAt) || request.PlannedAt<0 ||
                request.PlannedAt>=(request.AbsoluteDay+1d)*86400)return false;
            var home=request.Household;
            var a=home.Residents.Find(p=>p.Id==request.FirstRoutine.ResidentId);
            var b=home.Residents.Find(p=>p.Id==request.SecondRoutine.ResidentId);
            if(!Eligible(a,home) || !Eligible(b,home) || a.Id==b.Id || a.PartnerId!=b.Id || b.PartnerId!=a.Id)
            {reason="actual reciprocal parents are unavailable";return false;}
            var children=new List<string>();
            foreach(var child in home.Residents)
            {
                if(!child.IsAlive || child.AgeGroup!=ResidentAgeGroup.Child || child.HomeKey!=home.BuildingKey)continue;
                if(!(child.MotherId==a.Id && child.FatherId==b.Id || child.MotherId==b.Id && child.FatherId==a.Id))
                {reason="child parent links do not identify this pair";return false;}
                children.Add(child.Id);
            }
            children.Sort(StringComparer.Ordinal);
            double day=request.AbsoluteDay*86400d,localNow=Math.Max(0,request.PlannedAt-day);
            var result=new HouseholdDayPlan{Id=HouseholdDayPlan.Identity(request.SettlementId,home.BuildingKey,home.Generation,request.AbsoluteDay),
                SettlementId=request.SettlementId,HomeKey=home.BuildingKey,Generation=home.Generation,AbsoluteDay=request.AbsoluteDay,
                WakeMinute=request.WakeMinute,SleepMinute=request.SleepMinute,PlannedAt=request.PlannedAt,ChildIds=children,
                First=Member(a.Id,request.FirstRoutine,home.BuildingKey,request.WakeMinute,request.SleepMinute),
                Second=Member(b.Id,request.SecondRoutine,home.BuildingKey,request.WakeMinute,request.SleepMinute)};
            try
            {
                var routines=new[]{request.FirstRoutine,request.SecondRoutine};var members=new[]{result.First,result.Second};
                var occupied=new[]{new List<Interval>(),new List<Interval>()};var candidates=new List<Candidate>();
                var lastOriginalEnd=new double[2];
                for(int member=0;member<2;member++)
                {
                    var routine=routines[member];
                    if(routine.Itinerary==null && routine.Route.Count>=2){reason="legacy route needs explicit conversion";return false;}
                    var outings=ResidentRoutineDay.CopyOutings(routine);
                    for(int index=0;index<outings.Count;index++)
                    {
                        var outing=outings[index];double end=ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary).EndsAt;
                        if(outing.DepartureSeconds<localNow && end>localNow){reason="original outing is active";return false;}
                        if(end<=localNow)
                        {
                            members[member].Outings.Add(new HouseholdOutingSelection{SourceOutingIndex=index,DepartureSeconds=outing.DepartureSeconds});
                            occupied[member].Add(new Interval{From=outing.DepartureSeconds,Until=end});
                            lastOriginalEnd[member]=Math.Max(lastOriginalEnd[member],end);
                        }
                        else candidates.Add(new Candidate{Member=member,Index=index,Outing=outing});
                    }
                }
                if(request.JointInn!=null)
                {
                    if(!SharedDay(request.WorldSeed,request.SettlementId,home,request.AbsoluteDay))
                    {reason="this household day did not select shared leisure";return false;}
                    result.JointInn=HouseholdDayPlan.CopyJoint(request.JointInn);
                    var joint=new[]{result.JointInn.First,result.JointInn.Second};
                    for(int member=0;member<2;member++)
                    {
                        double start=joint[member].DepartureSeconds,end=ResidentVisitJourney.Create(start,joint[member].Itinerary).EndsAt;
                        if(start<localNow || !Fits(joint[member].Itinerary,start,request.WakeMinute,request.SleepMinute) ||
                            request.IsAvailable!=null && !request.IsAvailable(members[member].ResidentId,day+start,day+end) ||
                            !HomeAnchor(routines[member],joint[member].Itinerary.Stops[0].Route[0]))
                        {reason="joint outing conflicts with source, time or duties";return false;}
                        occupied[member].Add(new Interval{From=start,Until=end});
                    }
                }
                bool firstPriority=(StableSeed.Combine(request.WorldSeed,(uint)request.SettlementId,(uint)home.BuildingKey,
                    (uint)request.AbsoluteDay)&1)==0;
                string priority=string.CompareOrdinal(a.Id,b.Id)<0?a.Id:b.Id;
                if(!firstPriority)priority=priority==a.Id?b.Id:a.Id;
                candidates.Sort((x,y)=>{int order=x.Outing.DepartureSeconds.CompareTo(y.Outing.DepartureSeconds);
                    if(order!=0)return order;return members[x.Member].ResidentId==priority?-1:1;});
                foreach(var candidate in candidates)
                {
                    int member=candidate.Member,other=1-member;
                    double departure=Math.Max(lastOriginalEnd[member],Math.Max(candidate.Outing.DepartureSeconds,
                        Math.Max(localNow,request.WakeMinute*60d)));
                    if(candidate.Outing.Itinerary.Stops.Exists(stop=>stop.Purpose==ResidentVisitPurpose.Social))
                        departure=Math.Max(departure,16*3600);
                    // Bounded calendar search, not a native route search. Known
                    // conflicts jump directly to their end; unknown duties use
                    // quarter-hour trials and always retain the full interval.
                    for(int trial=0;trial<64 && departure<request.SleepMinute*60d;trial++)
                    {
                        double end=ResidentVisitJourney.Create(departure,candidate.Outing.Itinerary).EndsAt;
                        if(!Fits(candidate.Outing.Itinerary,departure,request.WakeMinute,request.SleepMinute))break;
                        double next=AfterConflicts(occupied[member],departure,end);
                        if(children.Count>0)next=Math.Max(next,AfterConflicts(occupied[other],departure,end));
                        if(next>departure){departure=next+60;continue;}
                        if(request.IsAvailable!=null && !request.IsAvailable(members[member].ResidentId,day+departure,day+end) ||
                            children.Count>0 && request.CanCoverChildren!=null &&
                            !request.CanCoverChildren(members[other].ResidentId,day+departure,day+end))
                        {departure+=900;continue;}
                        members[member].Outings.Add(new HouseholdOutingSelection{SourceOutingIndex=candidate.Index,DepartureSeconds=departure});
                        occupied[member].Add(new Interval{From=departure,Until=end});lastOriginalEnd[member]=end;break;
                    }
                }
                foreach(var member in members)member.Outings.Sort((x,y)=>x.SourceOutingIndex.CompareTo(y.SourceOutingIndex));
                result=HouseholdDayPlan.Copy(result);
                Schedule(result,a.Id,request.FirstRoutine);Schedule(result,b.Id,request.SecondRoutine);
            }
            catch(ArgumentException error){reason=error.Message;return false;}
            plan=result;reason="planned";return true;
        }
        public static DailySchedule Schedule(HouseholdDayPlan source,string residentId,ResidentRoutine routine)
        {
            var plan=HouseholdDayPlan.Copy(source);
            if(plan.Status!="planned" || routine==null || routine.ResidentId!=residentId)
                throw new ArgumentException("Household day is inactive or has the wrong source.");
            var member=plan.First.ResidentId==residentId?plan.First:plan.Second.ResidentId==residentId?plan.Second:null;
            if(member==null || member.RoutineFingerprint!=RoutineRecoveryIdentity.Fingerprint(routine,plan.HomeKey,plan.WakeMinute,plan.SleepMinute))
                throw new ArgumentException("Household day routine source changed.");
            var outings=ResidentRoutineDay.CopyOutings(routine);var children=new List<DailySchedule>();
            foreach(var selected in member.Outings)
            {
                if(selected.SourceOutingIndex>=outings.Count)throw new ArgumentException("Missing source household outing.");
                var original=outings[selected.SourceOutingIndex];int offset=0;
                for(int i=0;i<selected.SourceOutingIndex;i++)offset+=outings[i].Itinerary.Stops.Count;
                double localNow=Math.Max(0,plan.PlannedAt-plan.AbsoluteDay*86400d);
                bool history=selected.DepartureSeconds==original.DepartureSeconds &&
                    ResidentVisitJourney.Create(original.DepartureSeconds,original.Itinerary).EndsAt<=localNow;
                if(selected.DepartureSeconds<original.DepartureSeconds ||
                    !Fits(original.Itinerary,selected.DepartureSeconds,plan.WakeMinute,plan.SleepMinute,!history))
                    throw new ArgumentException("Household outing violates its original activity or service window.");
                children.Add(DailySchedule.ItineraryDay(plan.HomeKey,original.Itinerary,selected.DepartureSeconds,
                    plan.WakeMinute,plan.SleepMinute,offset));
            }
            if(plan.JointInn!=null && !plan.JointSuppressed)
            {
                var joint=plan.First.ResidentId==residentId?plan.JointInn.First:plan.JointInn.Second;
                if(!HomeAnchor(routine,joint.Itinerary.Stops[0].Route[0]))throw new ArgumentException("Joint outing has a different home doorway.");
                children.Add(DailySchedule.ItineraryDay(plan.HomeKey,joint.Itinerary,joint.DepartureSeconds,
                    plan.WakeMinute,plan.SleepMinute,JointVisitIndex));
            }
            // The date guard is intrinsic even if a consumer retains the object
            // across midnight. No joint outing leaks into a recurring day.
            return DailySchedule.ForDay(ResidentRoutineDay.Schedule(plan.HomeKey,routine,plan.WakeMinute,plan.SleepMinute),
                DailySchedule.OutingsDay(plan.HomeKey,children,plan.WakeMinute,plan.SleepMinute),plan.AbsoluteDay);
        }
        internal static void ValidateSources(HouseholdDayPlan plan,Household home,ResidentRoutine first,ResidentRoutine second,uint seed)
        {
            if(home==null || home.BuildingKey!=plan.HomeKey || home.Generation!=plan.Generation ||
                first==null || second==null || first.ResidentId!=plan.First.ResidentId || second.ResidentId!=plan.Second.ResidentId)
                throw new ArgumentException("Household day source is missing.");
            var a=home.Residents.Find(p=>p.Id==plan.First.ResidentId);var b=home.Residents.Find(p=>p.Id==plan.Second.ResidentId);
            if(!Eligible(a,home) || !Eligible(b,home) || a.PartnerId!=b.Id || b.PartnerId!=a.Id)
                throw new ArgumentException("Household day parents are unavailable.");
            var actualChildren=new List<string>();
            foreach(var child in home.Residents)
            {
                if(!child.IsAlive || child.AgeGroup!=ResidentAgeGroup.Child || child.HomeKey!=home.BuildingKey)continue;
                if(!(child.MotherId==a.Id && child.FatherId==b.Id || child.MotherId==b.Id && child.FatherId==a.Id))
                    throw new ArgumentException("Household day child links changed.");
                actualChildren.Add(child.Id);
            }
            actualChildren.Sort(StringComparer.Ordinal);var declared=new List<string>(plan.ChildIds);declared.Sort(StringComparer.Ordinal);
            if(actualChildren.Count!=declared.Count)throw new ArgumentException("Household day child identities changed.");
            for(int i=0;i<declared.Count;i++)if(actualChildren[i]!=declared[i])throw new ArgumentException("Household day child identities changed.");
            var schedules=new[]{Schedule(plan,a.Id,first),Schedule(plan,b.Id,second)};
            if(plan.JointInn!=null && !SharedDay(seed,plan.SettlementId,home,plan.AbsoluteDay))
                throw new ArgumentException("Unselected household day contains shared leisure.");
            double day=plan.AbsoluteDay*86400d,localNow=Math.Max(0,plan.PlannedAt-day);
            var members=new[]{plan.First,plan.Second};var routines=new[]{first,second};
            for(int member=0;member<2;member++)
            {
                var outings=ResidentRoutineDay.CopyOutings(routines[member]);
                for(int index=0;index<outings.Count;index++)
                {
                    var outing=outings[index];double end=ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary).EndsAt;
                    if(outing.DepartureSeconds<localNow && end>localNow)throw new ArgumentException("Original household outing is active.");
                    if(end<=localNow && !members[member].Outings.Exists(s=>s.SourceOutingIndex==index && s.DepartureSeconds==outing.DepartureSeconds))
                        throw new ArgumentException("Household plan rewrites completed history.");
                }
                foreach(var selected in members[member].Outings)
                {
                    var original=outings[selected.SourceOutingIndex];
                    double end=ResidentVisitJourney.Create(selected.DepartureSeconds,original.Itinerary).EndsAt;
                    if(end<=localNow)continue;
                    if(selected.DepartureSeconds<localNow || actualChildren.Count>0 &&
                        !schedules[1-member].IsHomeThroughout(day+selected.DepartureSeconds,day+end))
                        throw new ArgumentException("Household optional outing lacks full parental coverage.");
                }
                if(plan.JointInn!=null && !plan.JointSuppressed)
                {
                    var joint=member==0?plan.JointInn.First:plan.JointInn.Second;
                    if(joint.DepartureSeconds<localNow)throw new ArgumentException("Joint outing was planned retroactively.");
                }
            }
        }
        private static bool Eligible(Resident person,Household home)
        {return person!=null && person.IsAlive && person.AgeGroup==ResidentAgeGroup.Adult && person.HomeKey==home.BuildingKey &&
            !person.IsQuestProtected && person.NativeIdentity==null;}
        private static HouseholdDayMember Member(string id,ResidentRoutine routine,int home,int wake,int sleep)
        {return new HouseholdDayMember{ResidentId=id,RoutineFingerprint=RoutineRecoveryIdentity.Fingerprint(routine,home,wake,sleep)};}
        private static bool HomeAnchor(ResidentRoutine routine,SavedRoutePoint point)
        {
            var outings=ResidentRoutineDay.CopyOutings(routine);
            return outings.Count>0 && HouseholdDayPlan.Same(outings[0].Itinerary.Stops[0].Route[0],point);
        }
        private static bool Fits(ResidentDestination itinerary,double departure,int wake,int sleep,bool enforceSocial=true)
        {
            var journey=ResidentVisitJourney.Create(departure,itinerary);
            if(departure<wake*60d || journey.EndsAt>sleep*60d || journey.EndsAt>=86400)return false;
            foreach(var visit in journey.ScheduledVisits())
            {
                var stop=itinerary.Stops[visit.VisitIndex];
                if(visit.EnterAt<stop.OpenMinute*60d || visit.LeaveAt>stop.CloseMinute*60d ||
                    enforceSocial && stop.Purpose==ResidentVisitPurpose.Social && visit.EnterAt<16*3600)return false;
            }
            return true;
        }
        private static double AfterConflicts(List<Interval> intervals,double start,double end)
        {double next=start;foreach(var interval in intervals)if(start<interval.Until && end>interval.From)next=Math.Max(next,interval.Until);return next;}
        private static bool Finite(double value){return !double.IsNaN(value) && !double.IsInfinity(value);}
    }
}
