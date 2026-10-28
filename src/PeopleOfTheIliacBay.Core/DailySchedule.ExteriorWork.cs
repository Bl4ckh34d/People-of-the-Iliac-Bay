using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class DailySchedule
    {
        // Same physical point-outing evaluator as meetings, with a work activity
        // at the exact authored post. No invented building key represents a stall.
        public static DailySchedule ExteriorWorkday(int homeKey, IEnumerable<RoutePoint> outbound,
            IEnumerable<RoutePoint> inbound, RoutePoint post, int opensMinute, int closesMinute,
            double metresPerRealSecond, double gameSecondsPerRealSecond, int wakeMinute, int sleepMinute)
        {
            if (!Finite(metresPerRealSecond) || metresPerRealSecond <= 0 ||
                !Finite(gameSecondsPerRealSecond) || gameSecondsPerRealSecond <= 0 ||
                opensMinute < 0 || closesMinute >= 1440 || opensMinute >= closesMinute)
                throw new ArgumentException("Invalid exterior work hours or walking speed.");
            var outPoints = CopyRoute(outbound); var inPoints = CopyRoute(inbound);
            double outward = Lengths(outPoints)[outPoints.Length - 1] / metresPerRealSecond * gameSecondsPerRealSecond;
            double inward = Lengths(inPoints)[inPoints.Length - 1] / metresPerRealSecond * gameSecondsPerRealSecond;
            var result = PointOutingDay(homeKey, outPoints, inPoints, post,
                opensMinute * 60d - outward, opensMinute * 60d, closesMinute * 60d,
                closesMinute * 60d + inward, wakeMinute, sleepMinute);
            result.pointOuting.AtTarget = Activity.Work;
            return result;
        }
    }
}
