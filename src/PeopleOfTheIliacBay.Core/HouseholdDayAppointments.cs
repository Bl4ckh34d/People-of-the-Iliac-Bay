using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum HouseholdDayCompositionFailure
    {None,InvalidContext,SourceChanged,RequiredAppointmentUnavailable,PhysicalRecoveryRequired}
    public sealed class HouseholdAppointmentDay
    {
        public HouseholdDayPlan Plan;
        public DailySchedule First,Second;
    }
    // Projects fixed appointments onto both parents at once. This pure result is
    // not permission to cancel physical travel or rewrite the durable plan.
    public static class HouseholdDayAppointments
    {
        public static bool TryCompose(HouseholdDayPlan source,Household home,ResidentRoutine first,ResidentRoutine second,
            uint seed,IList<AppointmentRecord> bookings,IList<AppointmentPlaceRoute> pointRoutes,double now,
            out HouseholdAppointmentDay result,out string reason)
        {HouseholdDayCompositionFailure ignored;return TryCompose(source,home,first,second,seed,bookings,pointRoutes,now,out result,out reason,out ignored);}
        public static bool TryCompose(HouseholdDayPlan source,Household home,ResidentRoutine first,ResidentRoutine second,
            uint seed,IList<AppointmentRecord> bookings,IList<AppointmentPlaceRoute> pointRoutes,double now,
            out HouseholdAppointmentDay result,out string reason,out HouseholdDayCompositionFailure failure)
        {
            result=null;reason="invalid household appointment context";failure=HouseholdDayCompositionFailure.InvalidContext;
            if(double.IsNaN(now) || double.IsInfinity(now) || now<0 || source==null ||
                now<source.PlannedAt || Math.Floor(now/86400d)!=source.AbsoluteDay)return false;
            try
            {
                var plan=HouseholdDayPlan.Copy(source);HouseholdDayPlanner.ValidateSources(plan,home,first,second,seed);
                var people=new[]{home.Residents.Find(p=>p.Id==plan.First.ResidentId),home.Residents.Find(p=>p.Id==plan.Second.ResidentId)};
                var routines=new[]{first,second};var required=new DailySchedule[2];
                for(int member=0;member<2;member++)
                    if(!AppointmentDayPlanner.TryCreateWithHouseholdDay(people[member],routines[member],plan.AbsoluteDay,
                        bookings,pointRoutes,plan.WakeMinute,plan.SleepMinute,plan,true,out required[member],out reason))
                    {failure=HouseholdDayCompositionFailure.RequiredAppointmentUnavailable;return false;}
                double day=plan.AbsoluteDay*86400d;
                if(plan.JointInn!=null && !plan.JointSuppressed)
                {
                    var joint=new[]{plan.JointInn.First,plan.JointInn.Second};bool conflict=false;
                    for(int member=0;member<2;member++)
                    {
                        double end=ResidentVisitJourney.Create(joint[member].DepartureSeconds,joint[member].Itinerary).EndsAt;
                        if(!required[member].IsHomeThroughout(day+joint[member].DepartureSeconds,day+end))conflict=true;
                    }
                    if(conflict)
                    {
                        if(day+Math.Min(joint[0].DepartureSeconds,joint[1].DepartureSeconds)<=now)
                        {reason="active or historical joint change requires physical recovery";failure=HouseholdDayCompositionFailure.PhysicalRecoveryRequired;return false;}
                        plan.JointSuppressed=true;
                    }
                }
                var members=new[]{plan.First,plan.Second};
                for(int member=0;member<2;member++)
                {
                    var originals=ResidentRoutineDay.CopyOutings(routines[member]);
                    for(int index=members[member].Outings.Count-1;index>=0;index--)
                    {
                        var selection=members[member].Outings[index];
                        double starts=day+selection.DepartureSeconds,
                            ends=day+ResidentVisitJourney.Create(selection.DepartureSeconds,originals[selection.SourceOutingIndex].Itinerary).EndsAt;
                        if(required[member].IsHomeThroughout(starts,ends) &&
                            (plan.ChildIds.Count==0 || required[1-member].IsHomeThroughout(starts,ends)))continue;
                        if(starts<=now){reason="active or historical optional change requires physical recovery";failure=HouseholdDayCompositionFailure.PhysicalRecoveryRequired;return false;}
                        members[member].Outings.RemoveAt(index);
                    }
                }
                HouseholdDayPlanner.ValidateSources(plan,home,first,second,seed);
                var schedules=new DailySchedule[2];
                for(int member=0;member<2;member++)
                    if(!AppointmentDayPlanner.TryCreateWithHouseholdDay(people[member],routines[member],plan.AbsoluteDay,
                        bookings,pointRoutes,plan.WakeMinute,plan.SleepMinute,plan,false,out schedules[member],out reason))
                    {failure=HouseholdDayCompositionFailure.RequiredAppointmentUnavailable;return false;}
                result=new HouseholdAppointmentDay{Plan=plan,First=schedules[0],Second=schedules[1]};reason="composed";failure=HouseholdDayCompositionFailure.None;return true;
            }
            catch(ArgumentException error){reason=error.Message;failure=HouseholdDayCompositionFailure.SourceChanged;return false;}
        }
    }
}
