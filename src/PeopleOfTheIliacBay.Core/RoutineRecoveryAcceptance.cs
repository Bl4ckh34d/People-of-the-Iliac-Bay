using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public static class RoutineRecoveryAcceptance
    {
        public static bool TryCreateAfterAction(int settlementId,Resident resident,ResidentRoutine routine,
            DailySchedule ordinary,IList<AppointmentRecord> bookings,int day,double actionDeadline,double returnSeconds,
            int origin,int wake,int sleep,double secondsPerMetre,out RoutineRecoveryPlan plan,out string reason,
            bool originOutside=false)
        {return TryCreateAfterAction(settlementId,resident,routine,ordinary,bookings,day,actionDeadline,returnSeconds,
            origin,wake,sleep,secondsPerMetre,out plan,out reason,out _,originOutside);}

        public static bool TryCreateAfterAction(int settlementId,Resident resident,ResidentRoutine routine,
            DailySchedule ordinary,IList<AppointmentRecord> bookings,int day,double actionDeadline,double returnSeconds,
            int origin,int wake,int sleep,double secondsPerMetre,out RoutineRecoveryPlan plan,out string reason,
            out RoutineRecoveryFailure failure,bool originOutside=false)
        {
            plan=null;reason="The action return estimate is invalid.";failure=RoutineRecoveryFailure.Other;
            if(!RoutineRecoveryPlan.Finite(actionDeadline) || !RoutineRecoveryPlan.Finite(returnSeconds) || returnSeconds<0 ||
                !RoutineRecoveryPlan.Finite(actionDeadline+returnSeconds)) return false;
            double resumeAt=actionDeadline+returnSeconds;
            if(day>=0 && sleep>=0 && sleep<1440 && resumeAt>=day*86400d+sleep*60d)
            {reason="The measured physical return starts after protected sleep.";failure=RoutineRecoveryFailure.ProtectedSleep;return false;}
            return RoutineRecoveryPlanner.TryCreate(settlementId,resident,routine,ordinary,bookings,day,resumeAt,
                origin,resumeAt,wake,sleep,secondsPerMetre,out plan,out reason,out failure,originOutside);
        }
    }
}
