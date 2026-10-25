using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class DailySchedule
    {
        private DailySchedule interludeSource,interludeSchedule;
        private double interludeFrom,interludeUntil;

        // A short diversion has its own real visits/routes. The rest of the day
        // stays authoritative, including the shop the resident intended to visit.
        public static DailySchedule Interlude(DailySchedule ordinary,DailySchedule temporary,double from,double until)
        {
            if(ordinary==null || temporary==null || !Finite(from)||!Finite(until)||from<0||until<=from ||
                Math.Floor(from/86400)!=Math.Floor((until-0.001)/86400) || ordinary.home!=temporary.home)
                throw new ArgumentException("Invalid temporary visit interval.");
            var result=HomeDay(ordinary.home,ordinary.wake,ordinary.sleep);
            result.interludeSource=ordinary;result.interludeSchedule=temporary;
            result.interludeFrom=from;result.interludeUntil=until;return result;
        }
        private bool InInterlude(double now) {return now>=interludeFrom && now<interludeUntil;}
        private DailySchedule InterludeAt(double now) {return InInterlude(now)?interludeSchedule:interludeSource;}
        private List<ResidentVisitSnapshot> InterludeVisits(double now)
        {
            var original=interludeSource.ScheduledVisits(now);
            if(Math.Floor(now/86400)!=Math.Floor(interludeFrom/86400))return original;
            var result=new List<ResidentVisitSnapshot>();
            double from=interludeFrom%86400,until=interludeUntil%86400==0?86400:interludeUntil%86400;
            foreach(var entry in original)
            {
                var visit=entry;
                if(visit.LeaveAt<=from || visit.EnterAt>=until){result.Add(visit);continue;}
                if(visit.EnterAt<from){visit.LeaveAt=from;result.Add(visit);}
                if(entry.LeaveAt>until){visit=entry;visit.EnterAt=until;visit.ArriveAt=Math.Max(visit.ArriveAt,until);result.Add(visit);}
            }
            foreach(var visit in interludeSchedule.ScheduledVisits(now))
                if(visit.LeaveAt>from && visit.EnterAt<until)result.Add(visit);
            result.Sort((a,b)=>a.EnterAt.CompareTo(b.EnterAt));
            return result;
        }
    }
}
