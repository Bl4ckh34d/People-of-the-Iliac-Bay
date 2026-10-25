using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class RoutineRecoveryScheduleCache
        {
            public ResidentRoutine SourceRoutineRef;
            public DailySchedule BaseScheduleRef, Schedule;
            public RoutineRecoveryPlan Plan;
            public int Day, AppointmentRevision, RecoveryRevision, HouseholdRevision;
            public bool Restricted;
        }

        private readonly Dictionary<string,RoutineRecoveryScheduleCache> routineRecoverySchedules =
            new Dictionary<string,RoutineRecoveryScheduleCache>(StringComparer.Ordinal);

        private DailySchedule RecoveredSchedule(Plan plan,DailySchedule ordinary,bool restricted)
        {
            if(plan==null || plan.Resident==null || worldStore==null || ordinary==null)
                return ordinary;
            string residentId=plan.Resident.Id;
            if(!worldStore.HasRoutineRecovery(residentId))
            { InvalidateRoutineRecovery(residentId); return ordinary; }

            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int day=(int)Math.Floor(now/86400d),savedDay;
            if(!worldStore.TryGetRoutineRecoveryDay(residentId,out savedDay))
            { InvalidateRoutineRecovery(residentId); return ordinary; }
            if(savedDay<day)
            {
                worldStore.RemoveRoutineRecovery(residentId);
                InvalidateRoutineRecovery(residentId);
                return ordinary;
            }
            if(savedDay!=day)
            { InvalidateRoutineRecovery(residentId); return ordinary; }

            RoutineRecoveryScheduleCache cached;
            if(routineRecoverySchedules.TryGetValue(residentId,out cached) &&
                ReferenceEquals(cached.SourceRoutineRef,plan.Routine) && ReferenceEquals(cached.BaseScheduleRef,ordinary) &&
                cached.Day==day && cached.Restricted==restricted &&
                cached.AppointmentRevision==worldStore.AppointmentRevision &&
                cached.HouseholdRevision==worldStore.HouseholdDayRevision &&
                cached.RecoveryRevision==worldStore.RoutineRecoveryRevision)
                return cached.Schedule;

            int wake=restricted?Math.Max(wakeHour*60,300):wakeHour*60;
            int sleep=restricted?Math.Min(sleepHour*60,1140):sleepHour*60;
            string routineFingerprint=RoutineRecoveryIdentity.Fingerprint(plan.Routine,plan.Resident.HomeKey,wake,sleep);
            string appointmentFingerprint=RoutineRecoveryIdentity.Appointments(
                worldStore.AppointmentsFor(residentId),residentId,day);
            var recovery=RuntimeFamily(plan,now)==null?null:worldStore.GetHouseholdRoutineRecovery(residentId,day);
            if(recovery==null)recovery=worldStore.GetRoutineRecovery(residentId,day,routineFingerprint,appointmentFingerprint);
            if(recovery==null)
            {
                routineRecoverySchedules[residentId]=new RoutineRecoveryScheduleCache
                {
                    SourceRoutineRef=plan.Routine,BaseScheduleRef=ordinary,Schedule=ordinary,
                    Day=day,Restricted=restricted,AppointmentRevision=worldStore.AppointmentRevision,
                    RecoveryRevision=worldStore.RoutineRecoveryRevision,HouseholdRevision=worldStore.HouseholdDayRevision
                };
                return ordinary;
            }

            DailySchedule schedule;
            try { schedule=DailySchedule.ContinueDay(ordinary,recovery); }
            catch(ArgumentException error)
            {
                routineRecoverySchedules[residentId]=new RoutineRecoveryScheduleCache
                {
                    SourceRoutineRef=plan.Routine,BaseScheduleRef=ordinary,Schedule=ordinary,
                    Day=day,Restricted=restricted,AppointmentRevision=worldStore.AppointmentRevision,
                    RecoveryRevision=worldStore.RoutineRecoveryRevision,HouseholdRevision=worldStore.HouseholdDayRevision
                };
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_UNAVAILABLE: "+residentId+"; "+error.Message);
                return ordinary;
            }
            routineRecoverySchedules[residentId]=new RoutineRecoveryScheduleCache
            {
                SourceRoutineRef=plan.Routine,BaseScheduleRef=ordinary,Schedule=schedule,Plan=recovery,
                Day=day,Restricted=restricted,AppointmentRevision=worldStore.AppointmentRevision,
                RecoveryRevision=worldStore.RoutineRecoveryRevision,HouseholdRevision=worldStore.HouseholdDayRevision
            };
            return schedule;
        }

        private RoutineRecoveryPlan CurrentRoutineRecovery(Plan plan,double now)
        {
            if(plan==null || plan.Resident==null || worldStore==null || !FiniteRecoveryTime(now)) return null;
            int day=(int)Math.Floor(now/86400d);
            if(day<0 || !worldStore.HasRoutineRecovery(plan.Resident.Id)) return null;
            DailySchedule active=ActiveSchedule(plan);
            RoutineRecoveryScheduleCache cached;
            if(!routineRecoverySchedules.TryGetValue(plan.Resident.Id,out cached) ||
                !ReferenceEquals(cached.SourceRoutineRef,plan.Routine) || !ReferenceEquals(cached.Schedule,active) ||
                cached.Day!=day || cached.AppointmentRevision!=worldStore.AppointmentRevision ||
                cached.HouseholdRevision!=worldStore.HouseholdDayRevision ||
                cached.RecoveryRevision!=worldStore.RoutineRecoveryRevision || cached.Plan==null ||
                cached.Plan.AbsoluteDay!=day || now<cached.Plan.CreatedAt || now>=(day+1d)*86400d)
                return null;
            // Private read-only consumers share the resident cache. Store reads and
            // ContinueDay already detach ownership; never clone all route points per frame.
            return cached.Plan;
        }

        private void InvalidateRoutineRecovery(string residentId)
        {
            if(!string.IsNullOrWhiteSpace(residentId)) routineRecoverySchedules.Remove(residentId);
        }

        private static bool FiniteRecoveryTime(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
