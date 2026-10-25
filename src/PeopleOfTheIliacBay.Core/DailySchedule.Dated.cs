using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class DailySchedule
    {
        private DailySchedule dateSource,dateSchedule;
        private int selectedDay;
        public static DailySchedule ForDay(DailySchedule ordinary,DailySchedule selected,int absoluteDay)
        {
            if(ordinary==null || selected==null || absoluteDay<0 || ordinary.home!=selected.home ||
                ordinary.wake!=selected.wake || ordinary.sleep!=selected.sleep ||
                ordinary.dateSource!=null || selected.dateSource!=null)
                throw new ArgumentException("A dated schedule needs one matching home/day and unnested sources.");
            var result=HomeDay(ordinary.home,ordinary.wake,ordinary.sleep);
            result.dateSource=ordinary;result.dateSchedule=selected;result.selectedDay=absoluteDay;return result;
        }
        private DailySchedule ScheduleOn(double now)
        {return Math.Floor(now/86400d)==selectedDay?dateSchedule:dateSource;}
        public List<ResidentVisitSnapshot> ScheduledVisits(double absoluteSeconds)
        {
            if(interludeSource!=null)return InterludeVisits(absoluteSeconds);
            if(!Finite(absoluteSeconds) || absoluteSeconds<0)throw new ArgumentOutOfRangeException(nameof(absoluteSeconds));
            if(recoveryPlan!=null && !RecoveryApplies(absoluteSeconds))return recoverySource.ScheduledVisits(absoluteSeconds);
            return dateSource==null?ScheduledVisits():ScheduleOn(absoluteSeconds).ScheduledVisits(absoluteSeconds);
        }
        public bool ContainsVisitBuilding(int buildingKey,double absoluteSeconds)
        {
            if(interludeSource!=null)return interludeSource.ContainsVisitBuilding(buildingKey,absoluteSeconds)||
                Math.Floor(absoluteSeconds/86400)==Math.Floor(interludeFrom/86400) && interludeSchedule.ContainsVisitBuilding(buildingKey,absoluteSeconds);
            if(!Finite(absoluteSeconds) || absoluteSeconds<0)throw new ArgumentOutOfRangeException(nameof(absoluteSeconds));
            if(recoveryPlan!=null && !RecoveryApplies(absoluteSeconds))return recoverySource.ContainsVisitBuilding(buildingKey,absoluteSeconds);
            return dateSource==null?ContainsVisitBuilding(buildingKey):ScheduleOn(absoluteSeconds).ContainsVisitBuilding(buildingKey,absoluteSeconds);
        }
    }
}
