using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private enum HouseholdRefreshPhase{Active,PhysicalReturnPending}
        private sealed class RuntimeHouseholdDay
        {
            public HouseholdDayPlan Source,Effective;
            public Plan First,Second;
            public DailySchedule FirstSchedule,SecondSchedule;
            public int HouseholdRevision,AppointmentRevision,RoutineRevision;
            public bool Restricted;
            public HouseholdRefreshPhase Phase;
        }
        // One assignment publishes both parent schedules. The getter performs no
        // route copies, surveys, source hashing, or recursive schedule resolution.
        private readonly Dictionary<int,RuntimeHouseholdDay> householdSchedules=new Dictionary<int,RuntimeHouseholdDay>();
        private readonly HashSet<ResidentRoutine> preparedHouseholdRoutineSources=new HashSet<ResidentRoutine>();
        private readonly HashSet<string> pendingHouseholdInitialization=new HashSet<string>(StringComparer.Ordinal);
        private int nextHouseholdDay;
        private float nextHouseholdDayUpdate;
        private bool TryRuntimeHouseholdSchedule(Plan plan,double now,out DailySchedule schedule)
        {
            schedule=null;RuntimeHouseholdDay paired;
            if(plan==null || !householdSchedules.TryGetValue(plan.Resident.HomeKey,out paired) ||
                paired.Source.AbsoluteDay!=(int)Math.Floor(now/86400d))return false;
            if(paired.HouseholdRevision!=worldStore.HouseholdDayRevision || paired.AppointmentRevision!=worldStore.AppointmentRevision ||
                paired.RoutineRevision!=worldStore.RoutineRevision)paired.Phase=HouseholdRefreshPhase.PhysicalReturnPending;
            if(ReferenceEquals(paired.First,plan)){schedule=paired.FirstSchedule;return true;}
            if(ReferenceEquals(paired.Second,plan)){schedule=paired.SecondSchedule;return true;}
            return false;
        }
        private RuntimeHouseholdDay RuntimeFamily(Plan plan,double now)
        {
            DailySchedule ignored;if(!TryRuntimeHouseholdSchedule(plan,now,out ignored))return null;
            return householdSchedules[plan.Resident.HomeKey];
        }
        private bool RuntimeFamilyOwnsDay(Plan plan,double now)
        {return RuntimeFamily(plan,now)!=null || worldStore.HasPlannedHouseholdDay(population.SettlementId,
            plan.Resident.Id,(int)Math.Floor(now/86400d)) || worldStore.HasActiveHouseholdJoint(population.SettlementId,plan.Resident.Id);}

        private void UpdateHouseholdDays(double now,bool restoreOnly=false)
        {
            if(worldStore==null || population==null || !Ready || !restoreOnly && GameManager.IsGamePaused ||
                !restoreOnly && (!EntranceSurveyReady || Time.unscaledTime<nextHouseholdDayUpdate))return;
            if(!restoreOnly)nextHouseholdDayUpdate=Time.unscaledTime+0.5f;
            int count=population.Households.Count;if(count==0)return;
            int day=(int)Math.Floor(now/86400d);
            bool restricted=population.SettlementId==1291010263 && worldStore.RestrictHauntingRoutines(population.SettlementId,
                GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now);
            int budget=restoreOnly?count:Math.Min(2,count);
            for(int step=0;step<budget;step++)
            {
                if(nextHouseholdDay>=count)nextHouseholdDay=0;
                var home=population.Households[nextHouseholdDay++];RuntimeHouseholdDay previous;
                var joint=worldStore.ReadHouseholdJointProgress(population.SettlementId,home.BuildingKey);
                if(joint!=null && joint.Active)
                {
                    var jointFirst=plans.Find(p=>p.Resident.Id==joint.FirstResidentId);
                    var jointSecond=plans.Find(p=>p.Resident.Id==joint.SecondResidentId);
                    string pointReason;var pointRoutes=jointFirst!=null && jointSecond!=null?
                        FamilyPointRoutes(jointFirst,jointSecond,FamilyBookings(jointFirst,jointSecond),joint.AbsoluteDay,out pointReason):null;
                    worldStore.TryRetireHouseholdJointAtHome(population.SettlementId,home.BuildingKey,now,
                        id=>FamilyActorActuallyHome(id,home.BuildingKey,now),pointRoutes);
                }
                // Only a retryable native survey may hide cold-load actors.
                // Every terminal identity/source/capability failure releases it.
                foreach(var resident in home.Residents)pendingHouseholdInitialization.Remove(resident.Id);
                householdSchedules.TryGetValue(home.BuildingKey,out previous);
                if(previous!=null && previous.Source.AbsoluteDay!=day){householdSchedules.Remove(home.BuildingKey);previous=null;}
                if(previous!=null && previous.Phase==HouseholdRefreshPhase.Active && previous.HouseholdRevision==worldStore.HouseholdDayRevision &&
                    previous.AppointmentRevision==worldStore.AppointmentRevision && previous.RoutineRevision==worldStore.RoutineRevision)
                    continue;
                var source=worldStore.ReadHouseholdDay(population.SettlementId,home.BuildingKey,day);
                bool published=source!=null;
                if(previous==null && (source==null || source.Status!="planned"))
                    foreach(var resident in home.Residents)pendingHouseholdInitialization.Remove(resident.Id);
                string firstId=null,secondId=null;
                if(source!=null){firstId=source.First.ResidentId;secondId=source.Second.ResidentId;}
                else foreach(var child in home.Residents)
                    if(child.IsAlive && child.AgeGroup==ResidentAgeGroup.Child && child.MotherId!=null && child.FatherId!=null)
                    {firstId=child.MotherId;secondId=child.FatherId;break;}
                if(firstId==null || secondId==null)continue;
                var first=plans.Find(p=>p.Resident.Id==firstId);var second=plans.Find(p=>p.Resident.Id==secondId);
                if(first==null || second==null || !first.Resident.IsAlive || !second.Resident.IsAlive ||
                    first.Resident.IsQuestProtected || second.Resident.IsQuestProtected)continue;
                if(source==null)
                {
                    if(restoreOnly || !FamilySourcePrepared(first) || !FamilySourcePrepared(second) ||
                        !FamilyParentHome(first,now) || !FamilyParentHome(second,now))continue;
                    HouseholdJointInn jointCandidate;
                    if(!TryFamilyJointCandidate(home,first,second,day,now,restricted,out jointCandidate))continue;
                    var request=new HouseholdDayRequest{Household=home,SettlementId=population.SettlementId,
                        AbsoluteDay=day,WorldSeed=worldStore.WorldSeed,PlannedAt=now,
                        WakeMinute=restricted?Math.Max(wakeHour*60,300):wakeHour*60,
                        SleepMinute=restricted?Math.Min(sleepHour*60,1140):sleepHour*60,
                        FirstRoutine=first.Routine,SecondRoutine=second.Routine,
                        IsAvailable=FamilyIntervalAvailable,CanCoverChildren=FamilyIntervalAvailable,JointInn=jointCandidate};
                    string createReason;
                    if(!HouseholdDayPlanner.TryCreate(request,out source,out createReason))
                    {request.JointInn=null;if(!HouseholdDayPlanner.TryCreate(request,out source,out createReason))continue;}
                }
                if(source.Status!="planned")
                {if(previous!=null)previous.Phase=HouseholdRefreshPhase.PhysicalReturnPending;continue;}
                if(!FamilyGeometryPrepared(first) || !FamilyGeometryPrepared(second))continue;
                try
                {
                    if(published)
                    {
                        DailySchedule trustedFirst,trustedSecond;string sourceReason;
                        if(!worldStore.TryHouseholdDaySchedules(population.SettlementId,home.BuildingKey,day,
                            out trustedFirst,out trustedSecond,out sourceReason))continue;
                    }
                    var bookings=FamilyBookings(first,second);var routes=FamilyPointRoutes(first,second,bookings,day,out var routeReason);
                    if(routes==null)
                    {
                        if(previous==null && published && (!EntranceSurveyReady || geometry==null))
                        {pendingHouseholdInitialization.Add(firstId);pendingHouseholdInitialization.Add(secondId);}
                        continue;
                    }
                    HouseholdAppointmentDay composed;string reason;HouseholdDayCompositionFailure failure;
                    if(!HouseholdDayAppointments.TryCompose(source,home,first.Routine,second.Routine,worldStore.WorldSeed,
                        bookings,routes,now,out composed,out reason,out failure))
                    {
                        // A failed refresh retains the old paired projection.
                        // An active obligation must return through physical
                        // recovery rather than reverting to a recurring position.
                        if(previous!=null)previous.Phase=HouseholdRefreshPhase.PhysicalReturnPending;
                        if(!published || failure!=HouseholdDayCompositionFailure.PhysicalRecoveryRequired ||
                            !TryRepairHouseholdAtHome(home,source,first,second,bookings,routes,now,out composed))continue;
                    }
                    bool unpublished=!published;
                    if(unpublished)
                    {
                        if(!FamilyParentHome(first,now) || !FamilyParentHome(second,now))continue;
                        bool accepted=composed.Plan.JointInn!=null && !composed.Plan.JointSuppressed?
                            worldStore.TryPublishJointHouseholdDay(composed.Plan,now,out reason):worldStore.TryPublishHouseholdDay(composed.Plan,now,out reason);
                        if(!accepted)continue;
                        source=composed.Plan;
                    }
                    else
                    {
                        if(composed.Plan.JointSuppressed && !source.JointSuppressed)
                        {if(!worldStore.SuppressHouseholdJoint(source.Id,now))continue;source=worldStore.ReadHouseholdDay(population.SettlementId,home.BuildingKey,day);}
                    }
                    householdSchedules[home.BuildingKey]=new RuntimeHouseholdDay{Source=source,Effective=composed.Plan,
                        First=first,Second=second,FirstSchedule=composed.First,SecondSchedule=composed.Second,
                        HouseholdRevision=worldStore.HouseholdDayRevision,AppointmentRevision=worldStore.AppointmentRevision,
                        RoutineRevision=worldStore.RoutineRevision,Restricted=restricted};
                    pendingHouseholdInitialization.Remove(firstId);pendingHouseholdInitialization.Remove(secondId);
                    appointmentSchedules.Remove(firstId);appointmentSchedules.Remove(secondId);
                    InvalidateRoutineRecovery(firstId);InvalidateRoutineRecovery(secondId);
                    outdoorArrivalJourneys.Remove(firstId);outdoorArrivalJourneys.Remove(secondId);
                    if(unpublished)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_DAY_ACTIVATED: home="+home.BuildingKey+
                        "; day="+day+"; parents=2; sharedLeisure="+(source.JointInn!=null && !source.JointSuppressed)+"; publication=home; atomicPair=true");
                }
                catch(ArgumentException error)
                {
                    if(previous!=null)previous.Phase=HouseholdRefreshPhase.PhysicalReturnPending;
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_DAY_UNAVAILABLE: "+home.BuildingKey+"; "+error.Message);
                }
            }
        }
        private bool FamilySourcePrepared(Plan plan)
        {
            if(plan.Routine.Itinerary==null)return plan.Routine.DestinationKey<0;
            return preparedHouseholdRoutineSources.Contains(plan.Routine) && FamilyGeometryPrepared(plan);
        }
        private bool FamilyIntervalAvailable(string id,double from,double until)
        {
            // Fixed appointments are composed as a pair afterward. These are
            // the other known native obligations, including measured travel.
            if(worldStore.IsResidentAwayDuring(id,from,until) || worldStore.ActiveNpcAction(id)!=null ||
                worldStore.ActiveNpcTask(id)!=null || HasActiveNpcHealthCare(id) || shopDepartures.ContainsKey(id) ||
                worldStore.ActiveAlarm(id,from)!=null || IsAlarmed(id))return false;
            var observed=worldStore.GetObservedVisit(id);
            return observed==null || !observed.AffectsRoutineDuring(from,until);
        }
        private bool FamilyActorActuallyHome(string id,int home,double now)
        {
            ResidentActor actor;
            return !worldStore.IsResidentAway(id,now) && actors.TryGetValue(id,out actor) && actor && actor.gameObject.activeInHierarchy &&
                interior && interior.EntryDoor.buildingKey==home && actor.transform.parent==interior.transform;
        }
        private static bool FamilyGeometryPrepared(Plan plan)
        {
            if(plan.Routine.Itinerary==null)return plan.Routine.DestinationKey<0;
            return plan.Routine.RouteGeometryVersion==OutdoorRoutineMigration.CurrentVersion &&
                plan.Routine.PersonalLaneVersion==OutdoorRoutineMigration.CurrentPersonalLaneVersion;
        }
        private bool FamilyParentHome(Plan plan,double now,bool allowRecovery=false)
        {
            string id=plan.Resident.Id;
            if(worldStore.ActiveAlarm(id,now)!=null || IsAlarmed(id) || worldStore.IsResidentAway(id,now) ||
                worldStore.ActiveNpcAction(id)!=null || worldStore.ActiveNpcTask(id)!=null || HasActiveNpcHealthCare(id) ||
                shopDepartures.ContainsKey(id) || !allowRecovery && worldStore.HasRoutineRecovery(id))return false;
            var trip=worldStore.GetResidentDialogueTrip(id);if(trip!=null && trip.HomeAt>now)return false;
            var observed=worldStore.GetObservedVisit(id);
            if(observed!=null && observed.AffectsRoutineDuring(now,(Math.Floor(now/86400d)+1d)*86400d))return false;
            var actual=GetState(id,now);
            if(actual.BuildingKey!=plan.Resident.HomeKey || actual.Activity!=Activity.Home && actual.Activity!=Activity.Sleep)return false;
            ResidentActor actor;
            if(actors.TryGetValue(id,out actor) && actor && actor.gameObject.activeInHierarchy &&
                (actor.IsConversing || !interior || interior.EntryDoor.buildingKey!=plan.Resident.HomeKey ||
                 actor.transform.parent!=interior.transform))return false;
            return true;
        }
        private bool TryRepairHouseholdAtHome(Household home,HouseholdDayPlan source,Plan first,Plan second,
            IList<AppointmentRecord> bookings,IList<AppointmentPlaceRoute> routes,double now,out HouseholdAppointmentDay result)
        {
            result=null;if(!FamilyParentHome(first,now,true) || !FamilyParentHome(second,now,true))return false;
            if(RuntimeFamily(first,now)==null)
            {
                foreach(var member in new[]{first,second})
                {
                    var current=HouseholdDayPlanner.Schedule(source,member.Resident.Id,member.Routine).EvaluateSeconds(now);
                    if(current.BuildingKey!=home.BuildingKey || current.Activity!=Activity.Home && current.Activity!=Activity.Sleep)return false;
                }
            }
            var pair=new[]{first,second};var recoveries=new RoutineRecoveryPlan[2];
            for(int member=0;member<2;member++)
            {
                var person=pair[member].Resident;string reason;RoutineRecoveryFailure failure;
                if(!RoutineRecoveryPlanner.TryCreateWithHouseholdDay(source,home,first.Routine,second.Routine,worldStore.WorldSeed,
                    person.Id,bookings,routes,now,home.BuildingKey,now,
                    DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(person.AppearanceSeed),
                    out recoveries[member],out reason,out failure))return false;
            }
            worldStore.SaveHouseholdRoutineRecoveries(recoveries[0],recoveries[1]);
            result=new HouseholdAppointmentDay{Plan=source,
                First=HouseholdDayPlanner.Schedule(source,first.Resident.Id,first.Routine),
                Second=HouseholdDayPlanner.Schedule(source,second.Resident.Id,second.Routine)};
            return true;
        }
        private List<AppointmentRecord> FamilyBookings(Plan first,Plan second)
        {var bookings=worldStore.AppointmentsFor(first.Resident.Id);bookings.AddRange(worldStore.AppointmentsFor(second.Resident.Id));return bookings;}
        private List<AppointmentPlaceRoute> FamilyPointRoutes(Plan first,Plan second,IList<AppointmentRecord> bookings,int day,out string reason)
        {
            reason="ready";var result=new List<AppointmentPlaceRoute>();
            foreach(var member in new[]{first,second})
            {
                var needed=new List<AppointmentRecord>();
                foreach(var booking in bookings)if(RoutineRecoveryIdentity.Reserves(booking,member.Resident.Id,day) &&
                    booking.Place!=null && booking.Place.Kind=="world-point")needed.Add(booking);
                if(needed.Count==0)continue;
                var routes=BuildAppointmentPlaceRoutes(member,needed,day,out reason,true);
                if(routes.Count!=needed.Count)return null;
                result.AddRange(routes);
            }
            return result;
        }
    }
}
