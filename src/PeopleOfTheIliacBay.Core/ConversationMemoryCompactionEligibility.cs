using System;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Game-time age gate for future conversation-memory compaction.</summary>
    public static class ConversationMemoryCompactionEligibility
    {
        // Native DFU calendar uses 30 days per month (AppointmentCalendar).
        // Game timestamps use 86,400 seconds per day (AppointmentDayPlanner).
        public const double GameSecondsPerDay=86400d;
        public const double NativeMonthGameSeconds=30d*GameSecondsPerDay;

        public static bool IsEligible(double recordAt,double now)
        {
            if(double.IsNaN(recordAt)||double.IsInfinity(recordAt)||recordAt<0)
                throw new ArgumentOutOfRangeException("recordAt");
            if(double.IsNaN(now)||double.IsInfinity(now)||now<0)
                throw new ArgumentOutOfRangeException("now");
            return now-recordAt>NativeMonthGameSeconds;
        }
    }
}
