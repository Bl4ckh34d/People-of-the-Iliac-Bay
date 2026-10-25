using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ScheduleRecoveryTests
    {
        private static void FeasibleOptionalPointIsRetained()
        {
            Check.True(!ScheduleRecovery.ShouldSkipOptionalVisit(100,10,20,30,100,300,400),
                "A point whose real route and stay fit the next appointment was skipped.");
            Check.True(!ScheduleRecovery.ShouldSkipOptionalVisit(100,10,20,30,140,300,400),
                "An exact-fit point should remain feasible.");
        }

        private static void InfeasibleOptionalPointCanBeSkippedBeforeAppointmentOrSleep()
        {
            Check.True(ScheduleRecovery.ShouldSkipOptionalVisit(100,10,20,30,141,300,400),
                "An optional point that misses the next required commitment should be skipped.");
            Check.True(ScheduleRecovery.ShouldSkipOptionalVisit(100,10,20,30,241,600,400),
                "An optional point that misses sleep should be skipped even when the next appointment is later.");
        }

        private static void InvalidOptionalPointTimingIsRejected()
        {
            Check.Throws<ArgumentOutOfRangeException>(() =>
                ScheduleRecovery.ShouldSkipOptionalVisit(100,-1,20,30,100,300,400));
        }

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Schedule recovery retains an optional point whose measured route and stay fit", FeasibleOptionalPointIsRetained));
            cases.Add(("Schedule recovery skips an infeasible optional point before appointment or sleep", InfeasibleOptionalPointCanBeSkippedBeforeAppointmentOrSleep));
            cases.Add(("Schedule recovery rejects invalid point timing", InvalidOptionalPointTimingIsRejected));
        }
    }
}
