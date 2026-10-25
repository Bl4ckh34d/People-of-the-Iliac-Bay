using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public static class DailyActivityWindows
    {
        public static List<AdultDailyOutingSlot> Create(int wakeMinute,int sleepMinute,double latestHomeAt,uint seed)
        {
            if(wakeMinute<0 || sleepMinute>=1440 || wakeMinute>=sleepMinute || double.IsNaN(latestHomeAt) ||
                double.IsInfinity(latestHomeAt) || latestHomeAt<wakeMinute*60d || latestHomeAt>=sleepMinute*60d)
                throw new ArgumentException("Invalid local daily activity window.");
            var result=new List<AdultDailyOutingSlot>();
            double first=Math.Max(wakeMinute*60d,8*3600),span=latestHomeAt-first;
            const double gap=20*60,minimum=45*60;
            int count=(int)Math.Min(ResidentRoutineDay.MaximumOutings,Math.Max(0,Math.Floor((span+gap)/(minimum+gap))));
            if(count==0)return result;
            double length=(span-(count-1)*gap)/count;
            for(int i=0;i<count;i++)
            {
                uint rank=StableSeed.Combine(seed,(uint)i,0x74696d65u,0);
                double start=first+i*(length+gap),jitter=Math.Min(15*60,length/10)*(rank/4294967296d);
                result.Add(new AdultDailyOutingSlot{DepartureAt=start+Math.Floor(jitter),LatestHomeAt=start+length});
            }
            return result;
        }
    }
}
