using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public static partial class RoutineRecoveryPlanner
    {
        // The native owner supplies actual origin and measured departure. This
        // builds a continuation; it does not cancel or rewrite the shared day.
        public static bool TryCreateWithHouseholdDay(HouseholdDayPlan source,Household home,
            ResidentRoutine first,ResidentRoutine second,uint seed,string residentId,
            IList<AppointmentRecord> bookings,IList<AppointmentPlaceRoute> pointRoutes,
            double now,int origin,double departAt,double secondsPerMetre,
            out RoutineRecoveryPlan result,out string reason,out RoutineRecoveryFailure failure,
            bool originOutside=false,bool skipJoint=false)
        {
            result=null;reason="Invalid household recovery context.";failure=RoutineRecoveryFailure.Other;
            if(source==null || !Finite(now) || now<source.PlannedAt || Math.Floor(now/86400d)!=source.AbsoluteDay)return false;
            try
            {
                var plan=HouseholdDayPlan.Copy(source);HouseholdDayPlanner.ValidateSources(plan,home,first,second,seed);
                if(plan.Status!="planned" || residentId!=plan.First.ResidentId && residentId!=plan.Second.ResidentId)return false;
                var resident=home.Residents.Find(p=>p.Id==residentId);
                var routine=residentId==plan.First.ResidentId?first:second;
                var schedule=HouseholdDayPlanner.Schedule(plan,residentId,routine);
                var required=new DailySchedule[2];var routines=new[]{first,second};var members=new[]{plan.First,plan.Second};
                double day=plan.AbsoluteDay*86400d,wakeAt=day+plan.WakeMinute*60d;
                double timeRate=secondsPerMetre*ResidentMovement.WalkSpeedFor(resident.AppearanceSeed);
                for(int member=0;member<2;member++)
                {
                    var person=home.Residents.Find(p=>p.Id==members[member].ResidentId);
                    var atHome=DailySchedule.HomeDay(plan.HomeKey,plan.WakeMinute,plan.SleepMinute);
                    bool hasDuty=false;
                    if(bookings!=null)foreach(var booking in bookings)
                        if(RoutineRecoveryIdentity.Reserves(booking,person.Id,plan.AbsoluteDay)){hasDuty=true;break;}
                    if(!hasDuty){required[member]=atHome;continue;}
                    RoutineRecoveryPlan duty;RoutineRecoveryFailure dutyFailure;
                    // Use the same measured game-time rate as the physical
                    // continuation, retaining each parent's stable walking pace.
                    // This is a pure availability projection, never published.
                    if(!TryCreateCore(plan.SettlementId,person,routines[member],atHome,bookings,pointRoutes,plan.AbsoluteDay,
                        wakeAt,plan.HomeKey,wakeAt,plan.WakeMinute,plan.SleepMinute,timeRate/ResidentMovement.WalkSpeedFor(person.AppearanceSeed),
                        out duty,out reason,out dutyFailure,false,plan,true,null))
                    {failure=RoutineRecoveryFailure.RequiredAppointmentUnreachable;return false;}
                    required[member]=DailySchedule.ContinueDay(atHome,duty);
                }
                if(plan.JointInn!=null)for(int member=0;member<2;member++)
                {
                    var joint=member==0?plan.JointInn.First:plan.JointInn.Second;
                    if(!required[member].IsHomeThroughout(day+joint.DepartureSeconds,
                        day+ResidentVisitJourney.Create(joint.DepartureSeconds,joint.Itinerary).EndsAt))skipJoint=true;
                }
                // Losing a required childcare provider cancels optional outings,
                // including their complete outbound and return legs. The dated
                // source remains immutable; only this physical continuation skips.
                var excluded=new HashSet<int>();int own=residentId==plan.First.ResidentId?0:1;
                var originals=ResidentRoutineDay.CopyOutings(routine);
                foreach(var selection in members[own].Outings)
                {
                    var outing=originals[selection.SourceOutingIndex];double start=day+selection.DepartureSeconds,
                        end=day+ResidentVisitJourney.Create(selection.DepartureSeconds,outing.Itinerary).EndsAt;
                    if(required[own].IsHomeThroughout(start,end) &&
                        (plan.ChildIds.Count==0 || required[1-own].IsHomeThroughout(start,end)))continue;
                    int offset=0;for(int index=0;index<selection.SourceOutingIndex;index++)offset+=originals[index].Itinerary.Stops.Count;
                    for(int index=0;index<outing.Itinerary.Stops.Count;index++)excluded.Add(offset+index);
                }
                return TryCreateCore(plan.SettlementId,resident,routine,schedule,bookings,pointRoutes,plan.AbsoluteDay,
                    now,origin,departAt,plan.WakeMinute,plan.SleepMinute,secondsPerMetre,
                    out result,out reason,out failure,originOutside,plan,skipJoint,excluded);
            }
            catch(ArgumentException error){reason=error.Message;return false;}
        }
    }
}
