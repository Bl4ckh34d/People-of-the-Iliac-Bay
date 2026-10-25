using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class ScheduleRecovery
    {
        // The caller supplies measured time for the action rejoin, the next
        // optional point's route and stay, and the actual remaining itinerary.
        // A caller can then advance to the next optional point and test it again.
        public static bool ShouldSkipOptionalVisit(double now, double rejoinTravelSeconds,
            double visitTravelSeconds, double remainingStaySeconds, double remainingItinerarySeconds,
            double nextRequiredAt, double sleepAt)
        {
            if (!Finite(now) || now < 0 || !Finite(rejoinTravelSeconds) || rejoinTravelSeconds < 0 ||
                !Finite(visitTravelSeconds) || visitTravelSeconds < 0 || !Finite(remainingStaySeconds) ||
                remainingStaySeconds < 0 || !Finite(remainingItinerarySeconds) || remainingItinerarySeconds < 0 ||
                !Finite(nextRequiredAt) || nextRequiredAt < 0 || !Finite(sleepAt) || sleepAt < 0)
                throw new ArgumentOutOfRangeException("Schedule recovery times must be finite and non-negative.");

            double boundary = Math.Min(nextRequiredAt, sleepAt);
            return now + rejoinTravelSeconds + visitTravelSeconds + remainingStaySeconds +
                remainingItinerarySeconds > boundary;
        }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
