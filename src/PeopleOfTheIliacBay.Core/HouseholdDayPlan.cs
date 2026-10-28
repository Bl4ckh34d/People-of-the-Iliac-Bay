using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class HouseholdOutingSelection
    {
        public int SourceOutingIndex;
        public double DepartureSeconds;
    }
    [Serializable]
    public sealed class HouseholdDayMember
    {
        public string ResidentId,RoutineFingerprint;
        public List<HouseholdOutingSelection> Outings=new List<HouseholdOutingSelection>();
    }
    [Serializable]
    public sealed class HouseholdJointInn
    {
        public int InnKey;
        public ResidentDailyOuting First,Second;
        public string PayerId;
        // Conservative group reserve only. Actual menu and price are chosen at the counter.
        public int ReserveGold;
    }
    [Serializable]
    public sealed class HouseholdDayPlan
    {
        public string Id,Status="planned";
        public int SettlementId,HomeKey,Generation,AbsoluteDay,WakeMinute,SleepMinute;
        public double PlannedAt;
        public HouseholdDayMember First,Second;
        public List<string> ChildIds=new List<string>();
        public HouseholdJointInn JointInn;
        public bool JointSuppressed;
        public static string Identity(int settlement,int home,int generation,int day)
        {return string.Format(CultureInfo.InvariantCulture,"family-day:{0}:{1}:{2}:{3}",settlement,home,generation,day);}
        public static HouseholdDayPlan Copy(HouseholdDayPlan source)
        {
            if(source==null || source.SettlementId<0 || source.HomeKey<0 || source.Generation<0 || source.AbsoluteDay<0 ||
                source.WakeMinute<0 || source.SleepMinute>=1440 || source.WakeMinute>=source.SleepMinute ||
                !Finite(source.PlannedAt) || source.PlannedAt<0 || source.PlannedAt>=(source.AbsoluteDay+1d)*86400 ||
                source.Id!=Identity(source.SettlementId,source.HomeKey,source.Generation,source.AbsoluteDay) ||
                source.Status!="planned" && source.Status!="cancelled" && source.Status!="completed" ||
                source.ChildIds==null || source.ChildIds.Count>32 || source.JointSuppressed && source.JointInn==null)
                throw new ArgumentException("Invalid dated household plan.");
            var first=CopyMember(source.First);var second=CopyMember(source.Second);
            if(first.ResidentId==second.ResidentId)throw new ArgumentException("A household day requires two different parents.");
            if(source.JointInn?.PayerId!=null && source.JointInn.PayerId!=first.ResidentId && source.JointInn.PayerId!=second.ResidentId)
                throw new ArgumentException("Shared Inn payer must be one of the participating parents.");
            var ids=new HashSet<string>(StringComparer.Ordinal);var children=new List<string>();
            foreach(var child in source.ChildIds)
            {if(!IdentityText(child) || child==first.ResidentId || child==second.ResidentId || !ids.Add(child))
                throw new ArgumentException("Invalid household child identity.");children.Add(child);}
            return new HouseholdDayPlan{Id=source.Id,Status=source.Status,SettlementId=source.SettlementId,HomeKey=source.HomeKey,
                Generation=source.Generation,AbsoluteDay=source.AbsoluteDay,WakeMinute=source.WakeMinute,SleepMinute=source.SleepMinute,
                PlannedAt=source.PlannedAt,First=first,Second=second,ChildIds=children,JointInn=CopyJoint(source.JointInn),JointSuppressed=source.JointSuppressed};
        }
        internal static bool Equivalent(HouseholdDayPlan left,HouseholdDayPlan right)
        {
            if(left.Id!=right.Id || left.Status!=right.Status || left.PlannedAt!=right.PlannedAt || left.JointSuppressed!=right.JointSuppressed ||
                left.WakeMinute!=right.WakeMinute || left.SleepMinute!=right.SleepMinute ||
                !SameMember(left.First,right.First) || !SameMember(left.Second,right.Second) || left.ChildIds.Count!=right.ChildIds.Count)
                return false;
            for(int i=0;i<left.ChildIds.Count;i++)if(left.ChildIds[i]!=right.ChildIds[i])return false;
            if(left.JointInn==null || right.JointInn==null)return left.JointInn==right.JointInn;
            return left.JointInn.InnKey==right.JointInn.InnKey &&
                left.JointInn.PayerId==right.JointInn.PayerId && left.JointInn.ReserveGold==right.JointInn.ReserveGold &&
                JointFingerprint(left,left.JointInn.First)==JointFingerprint(right,right.JointInn.First) &&
                JointFingerprint(left,left.JointInn.Second)==JointFingerprint(right,right.JointInn.Second);
        }
        private static string JointFingerprint(HouseholdDayPlan plan,ResidentDailyOuting outing)
        {return RoutineRecoveryIdentity.Fingerprint(new ResidentRoutine{ResidentId=plan.Id,Itinerary=outing.Itinerary,
            ItineraryDepartureSeconds=outing.DepartureSeconds},plan.HomeKey,plan.WakeMinute,plan.SleepMinute);}
        private static bool SameMember(HouseholdDayMember left,HouseholdDayMember right)
        {
            if(left.ResidentId!=right.ResidentId || left.RoutineFingerprint!=right.RoutineFingerprint || left.Outings.Count!=right.Outings.Count)return false;
            for(int i=0;i<left.Outings.Count;i++)if(left.Outings[i].SourceOutingIndex!=right.Outings[i].SourceOutingIndex ||
                left.Outings[i].DepartureSeconds!=right.Outings[i].DepartureSeconds)return false;
            return true;
        }
        private static HouseholdDayMember CopyMember(HouseholdDayMember source)
        {
            if(source==null || !IdentityText(source.ResidentId) || source.RoutineFingerprint==null || source.RoutineFingerprint.Length!=64 ||
                source.Outings==null || source.Outings.Count>ResidentRoutineDay.MaximumOutings)
                throw new ArgumentException("Invalid household member plan.");
            foreach(char c in source.RoutineFingerprint)if(!(c>='0'&&c<='9' || c>='a'&&c<='f'))
                throw new ArgumentException("Invalid household routine fingerprint.");
            var result=new HouseholdDayMember{ResidentId=source.ResidentId,RoutineFingerprint=source.RoutineFingerprint};
            int previous=-1;double departure=-1;
            foreach(var outing in source.Outings)
            {
                if(outing==null || outing.SourceOutingIndex<=previous || outing.SourceOutingIndex>=ResidentRoutineDay.MaximumOutings ||
                    !Finite(outing.DepartureSeconds) || outing.DepartureSeconds<0 || outing.DepartureSeconds>=86400 ||
                    outing.DepartureSeconds<departure)throw new ArgumentException("Invalid household outing selection.");
                result.Outings.Add(new HouseholdOutingSelection{SourceOutingIndex=outing.SourceOutingIndex,DepartureSeconds=outing.DepartureSeconds});
                previous=outing.SourceOutingIndex;departure=outing.DepartureSeconds;
            }
            return result;
        }
        internal static HouseholdJointInn CopyJoint(HouseholdJointInn source)
        {
            if(source==null)return null;
            if(source.PayerId!=null && (!IdentityText(source.PayerId) || source.ReserveGold<2 || source.ReserveGold>510))throw new ArgumentException("Invalid shared Inn spending plan.");
            var first=CopyJointOuting(source.First,source.InnKey);var second=CopyJointOuting(source.Second,source.InnKey);
            var a=ResidentVisitJourney.Create(first.DepartureSeconds,first.Itinerary);
            var b=ResidentVisitJourney.Create(second.DepartureSeconds,second.Itinerary);
            var av=new List<ResidentVisitSnapshot>(a.ScheduledVisits())[0];var bv=new List<ResidentVisitSnapshot>(b.ScheduledVisits())[0];
            var ah=first.Itinerary.Stops[0].Route[0];var bh=second.Itinerary.Stops[0].Route[0];
            var ad=first.Itinerary.ExitRoute[0];var bd=second.Itinerary.ExitRoute[0];
            if(!Same(ah,bh) || !Same(ad,bd) || Math.Min(av.LeaveAt,bv.LeaveAt)-Math.Max(av.EnterAt,bv.EnterAt)<900)
                throw new ArgumentException("Joint Inn routes need the same real home, Inn entrance and shared visit window.");
            return new HouseholdJointInn{InnKey=source.InnKey,First=first,Second=second,PayerId=source.PayerId,
                ReserveGold=source.ReserveGold};
        }
        private static ResidentDailyOuting CopyJointOuting(ResidentDailyOuting source,int inn)
        {
            if(source==null || inn<0 || !Finite(source.DepartureSeconds) || source.DepartureSeconds<16*3600 ||
                source.DepartureSeconds>=86400)throw new ArgumentException("Invalid joint Inn departure.");
            var copy=ResidentVisitJourney.Copy(source.Itinerary);
            if(copy.ThroughRoute || copy.Walled || copy.Stops.Count!=1 || copy.Stops[0].BuildingKey!=inn ||
                copy.Stops[0].Purpose!=ResidentVisitPurpose.Social)throw new ArgumentException("Invalid joint Inn destination.");
            var journey=ResidentVisitJourney.Create(source.DepartureSeconds,copy);
            if(journey.EndsAt>=86400)throw new ArgumentException("Joint Inn outing crosses midnight.");
            foreach(var visit in journey.ScheduledVisits())if(visit.EnterAt<16*3600 ||
                visit.EnterAt<copy.Stops[0].OpenMinute*60d || visit.LeaveAt>copy.Stops[0].CloseMinute*60d)
                throw new ArgumentException("Joint Inn outing violates its service window.");
            return new ResidentDailyOuting{DepartureSeconds=source.DepartureSeconds,Itinerary=copy};
        }
        internal static bool Same(SavedRoutePoint a,SavedRoutePoint b)
        {return Math.Abs(a.X-b.X)<0.00001 && Math.Abs(a.Z-b.Z)<0.00001;}
        private static bool IdentityText(string value){return !string.IsNullOrWhiteSpace(value) && value.Length<=256;}
        private static bool Finite(double value){return !double.IsNaN(value) && !double.IsInfinity(value);}
    }
}
