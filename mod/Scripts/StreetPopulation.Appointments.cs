using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using DaggerfallConnect;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class AppointmentScheduleCache
        {
            public int Day,Revision;
            public bool Restricted,OverridesRoutine;
            public DailySchedule Schedule;
        }
        private readonly Dictionary<string,AppointmentScheduleCache> appointmentSchedules=new Dictionary<string,AppointmentScheduleCache>();
        private readonly Dictionary<string,Plan> appointmentObservationPlans=new Dictionary<string,Plan>(StringComparer.Ordinal);
        private sealed class ResolvedAppointmentCache
        {
            public int Day,Revision;
            public List<AppointmentRecord> Records;
        }
        private readonly Dictionary<string,ResolvedAppointmentCache> resolvedAppointmentCache=
            new Dictionary<string,ResolvedAppointmentCache>(StringComparer.Ordinal);
        private readonly Dictionary<string,float> nextAppointmentDepartureRetry=
            new Dictionary<string,float>(StringComparer.Ordinal);
        private Action<PendingAppointmentObservation> pendingAppointmentObserver;
        private double appointmentObservationNow;
        private float nextAppointmentObservation;

        public List<int> AppointmentVenueKeys(string residentId)
        {
            var result=new List<int>();
            var plan=plans.Find(p=>p.Resident.Id==residentId);
            if(plan==null) return result;
            if(plan.Routine.Itinerary!=null)
            {
                foreach(var itinerary in ResidentRoutineDay.Itineraries(plan.Routine))
                    foreach(var stop in itinerary.Stops)
                    if(!result.Contains(stop.BuildingKey)) result.Add(stop.BuildingKey);
            }
            else if(plan.Routine.DestinationKey>=0) result.Add(plan.Routine.DestinationKey);
            return result;
        }

        public string AppointmentConversationPlan(string residentId,double now,double shift,Func<int,string> buildingName,ActivitySnapshot? observed=null)
        {
            var plan=plans.Find(p=>p.Resident.Id==residentId);
            if(plan==null) return null;
            var selected=ActiveSchedule(plan);
            var family=RuntimeFamily(plan,now);
            if(family!=null)return ConversationLifeContext.EffectiveSchedule(selected,now,observed??GetState(residentId,now),shift,buildingName);
            AppointmentScheduleCache cached;
            if(!appointmentSchedules.TryGetValue(residentId,out cached) ||
                !cached.OverridesRoutine || !ReferenceEquals(selected,cached.Schedule)) return null;
            return ConversationLifeContext.EffectiveSchedule(selected,now,observed??GetState(residentId,now),shift,buildingName);
        }

        public bool TryBookAppointment(AppointmentRecord appointment,out string reason)
        {
            reason="No verified local routine is available.";
            if(!Ready || worldStore==null || population==null || appointment==null || appointment.Place==null ||
                appointment.Place.Kind!="building" && appointment.Place.Kind!="world-point" ||
                appointment.Place.SettlementId!=population.SettlementId) return false;
            var plan=plans.Find(p=>p.Resident.Id==appointment.ResidentId);
            if(plan==null) return false;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int day=(int)Math.Floor(appointment.StartsAt/86400);
            // Replacing a day already in progress needs an anchored continuation,
            // never a new path that jumps the actor back onto another route.
            if(day<=(int)Math.Floor(now/86400))
            { reason="Today's physical routine is already under way; choose a future day."; return false; }
            if(worldStore.FindAppointment(appointment.Id)!=null)
                return worldStore.TryReserveAppointment(appointment,now,out reason);
            var candidate=CopyAppointmentProposal(appointment);
            if(candidate.PatienceSeconds==0)
            {
                int disposition=worldStore.TryGetActualConversationDisposition(candidate.ResidentId,out var actual)
                    ? actual : worldStore.ConversationDisposition(candidate.ResidentId);
                candidate.PatienceSeconds=AppointmentTiming.CreatePatienceSeconds(worldStore.WorldSeed,
                    candidate.ResidentId,candidate.Id,disposition);
            }
            IList<AppointmentPlaceRoute> pointRoutes=null;
            if(candidate.Place.Kind=="world-point")
            {
                if(candidate.Place.BuildingKey>=0 || !Finite(candidate.Place.X) || !Finite(candidate.Place.Y) || !Finite(candidate.Place.Z))
                { reason="The captured exterior meeting point is invalid."; return false; }
            }
            else
            {
                BuildingSummary venue;
                if(!directory.GetBuildingSummary(candidate.Place.BuildingKey,out venue))
                { reason="The meeting place is unavailable."; return false; }
                if(RMBLayout.IsShop(venue.BuildingType))
                {
                    int type=(int)venue.BuildingType;
                    double arrival=candidate.StartsAt%86400/60,leave=AppointmentTiming.ReservedUntil(candidate)%86400/60;
                    if(type<0 || type>=PlayerActivate.openHours.Length || arrival<PlayerActivate.openHours[type]*60 || leave>PlayerActivate.closeHours[type]*60)
                    { reason="The shop would be closed during that appointment."; return false; }
                }
            }
            var bookings=worldStore.AppointmentsFor(candidate.ResidentId);
            bookings.Add(candidate);
            if(bookings.Exists(item=>item.Place!=null && item.Place.Kind=="world-point"))
            {
                var routes=BuildAppointmentPlaceRoutes(plan,bookings,day,out reason);
                int pointCount=bookings.FindAll(item=>item.Place!=null && item.Place.Kind=="world-point" &&
                    (int)Math.Floor(item.StartsAt/86400d)==day && item.Status!="cancelled").Count;
                if(routes.Count!=pointCount) return false;
                pointRoutes=routes;
            }
            DailySchedule candidateSchedule;
            bool restricted=population.SettlementId==1291010263 && !GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0);
            int wake=restricted ? Math.Max(wakeHour*60,300) : wakeHour*60;
            int sleep=restricted ? Math.Min(sleepHour*60,1140) : sleepHour*60;
            if(!AppointmentDayPlanner.TryCreate(plan.Resident,plan.Routine,day,bookings,pointRoutes,wake,sleep,
                out candidateSchedule,out reason))
            {
                if(restricted && (reason.Contains("waking") || reason.Contains("day")))
                    reason="Daggerfall is haunted by the late King Lysandus. I will not be out during the haunting, from 19:00 until 05:00; the meeting and walking home must fit safely before it begins.";
                return false;
            }
            return worldStore.TryReserveAppointment(candidate,now,out reason);
        }

        private static AppointmentRecord CopyAppointmentProposal(AppointmentRecord source)
        {
            return new AppointmentRecord { Id=source.Id,ResidentId=source.ResidentId,Purpose=source.Purpose,
                Place=new AppointmentPlace {Kind=source.Place.Kind,SettlementId=source.Place.SettlementId,
                    BuildingKey=source.Place.BuildingKey,
                    X=source.Place.Kind=="world-point"?AppointmentPlace.CanonicalWorldPointCoordinate(source.Place.X):source.Place.X,
                    Y=source.Place.Kind=="world-point"?AppointmentPlace.CanonicalWorldPointCoordinate(source.Place.Y):source.Place.Y,
                    Z=source.Place.Kind=="world-point"?AppointmentPlace.CanonicalWorldPointCoordinate(source.Place.Z):source.Place.Z,
                    Detail=source.Place.Detail},
                StartsAt=source.StartsAt,EndsAt=source.EndsAt,GraceSeconds=source.GraceSeconds,
                PatienceSeconds=source.PatienceSeconds,Importance=source.Importance,Status=source.Status };
        }

        public bool TryCancelAppointment(string id,double now,out string reason)
        {
            reason="The appointment is already under way or unavailable.";
            var booking=worldStore.FindAppointment(id);
            if(booking==null || booking.OutcomeRecorded || Math.Floor(booking.StartsAt/86400)<=Math.Floor(now/86400)) return false;
            bool cancelled=worldStore.CancelAppointment(id,now,"The player cancelled our appointment in advance.");
            if(cancelled) reason="cancelled";
            return cancelled;
        }

        private DailySchedule AppointmentSchedule(Plan plan,DailySchedule ordinary,bool restricted)
        {
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int day=(int)Math.Floor(now/86400);
            AppointmentScheduleCache cached;
            if(appointmentSchedules.TryGetValue(plan.Resident.Id,out cached) && cached.Day==day &&
                cached.Revision==worldStore.AppointmentRevision && cached.Restricted==restricted) return cached.Schedule;
            var bookings=worldStore.AppointmentsFor(plan.Resident.Id);
            bookings.RemoveAll(a=>a.Place.SettlementId!=population.SettlementId || Math.Floor(a.StartsAt/86400)!=day || a.Status=="cancelled");
            DailySchedule selected=ordinary;
            if(bookings.Count>0)
            {
                string reason; DailySchedule candidate;
                int wake=restricted ? Math.Max(wakeHour*60,300) : wakeHour*60;
                int sleep=restricted ? Math.Min(sleepHour*60,1140) : sleepHour*60;
                var pointRoutes=BuildAppointmentPlaceRoutes(plan,bookings,day,out reason,true);
                if(bookings.Exists(item=>item.Place!=null && item.Place.Kind=="world-point") && pointRoutes.Count==0)
                {
                    if(EntranceSurveyReady)
                        Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_ROUTE_UNAVAILABLE: "+plan.Resident.Id+"; "+reason);
                    else
                    {
                        // Saved streets are still being surveyed after scene load.
                        // Avoid caching the ordinary schedule as if no booking existed.
                        return ordinary;
                    }
                }
                if(AppointmentDayPlanner.TryCreate(plan.Resident,plan.Routine,day,bookings,pointRoutes,wake,sleep,out candidate,out reason))
                    selected=candidate;
                else Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_PLAN_UNAVAILABLE: "+plan.Resident.Id+"; "+reason);
            }
            appointmentSchedules[plan.Resident.Id]=new AppointmentScheduleCache { Day=day,Revision=worldStore.AppointmentRevision,
                Restricted=restricted,OverridesRoutine=!ReferenceEquals(selected,ordinary),Schedule=selected };
            return selected;
        }

        private void ObserveAppointments(double now)
        {
            if(GameManager.IsGamePaused || Time.unscaledTime<nextAppointmentObservation) return;
            nextAppointmentObservation=Time.unscaledTime+0.5f;
            appointmentObservationPlans.Clear();
            foreach(var plan in plans)appointmentObservationPlans[plan.Resident.Id]=plan;
            appointmentObservationNow=now;
            if(pendingAppointmentObserver==null)pendingAppointmentObserver=ObservePendingAppointment;
            worldStore.VisitPendingAppointmentObservations(population.SettlementId,pendingAppointmentObserver);
            ObserveProactiveContacts(now);
            ObserveCompanionHealthContacts(now);
            RetryResolvedAppointmentDepartures(now);
        }

        private void ObservePendingAppointment(PendingAppointmentObservation booking)
        {
            double now=appointmentObservationNow;
            if(now<booking.StartsAt)return;
            Plan plan;
            if(!appointmentObservationPlans.TryGetValue(booking.ResidentId,out plan))return;
            var game=GameManager.Instance;
            ResidentActor actor;actors.TryGetValue(plan.Resident.Id,out actor);
            bool visible=actor && actor.gameObject.activeInHierarchy;
            bool point=booking.PlaceKind=="world-point";
            var scheduled=GetState(plan.Resident.Id,now);
            bool inVenue=!point && interior && interior.EntryDoor.buildingKey==booking.BuildingKey;
            Vector3 pointTarget=point && geometry!=null ? geometry.ScenePoint(new RoutePoint(booking.X,booking.Z)) : Vector3.zero;
            bool npcAtPoint=point && directory && visible && actor.transform.parent==directory.transform &&
                scheduled.Activity==Activity.Meeting && Vector3.Distance(actor.transform.position-Vector3.up,pointTarget)<=1.35f;
            bool playerAtPoint=point && !interior && directory &&
                Vector3.Distance(game.PlayerMotor.transform.position-Vector3.up,pointTarget)<=1.35f;
            bool npcPresent=visible && (point ? npcAtPoint : inVenue && scheduled.BuildingKey==booking.BuildingKey) &&
                plan.Resident.IsAlive && !actor.IsSleeping && !IsAlarmed(plan.Resident.Id);
            bool playerAtVenue=point ? playerAtPoint : inVenue;
            bool playerNear=npcPresent &&
                Vector3.Distance(game.PlayerMotor.transform.position,actor.transform.position)<=1.35f &&
                (!point || playerAtPoint) &&
                !game.PlayerEntity.IsInvisible && !game.PlayerEntity.IsMagicallyConcealed &&
                CivilianResponses.Sees(actor.transform.position,game.PlayerMotor.transform.position,20) &&
                CivilianResponses.Sees(game.PlayerMotor.transform.position,actor.transform.position,20);
            double arrivalDeadline=AppointmentTiming.ArrivalDeadline(booking.StartsAt,booking.EndsAt,
                booking.GraceSeconds,booking.PatienceSeconds);
            bool interrupted=!plan.Resident.IsAlive || IsAlarmed(plan.Resident.Id) ||
                (now>arrivalDeadline && !visible && !playerAtVenue);
            worldStore.ObserveAppointment(booking.Id,now,npcPresent,playerNear,playerAtVenue,interrupted);
            if((now>arrivalDeadline || interrupted || npcPresent && playerNear) && npcPresent && visible && inVenue)
            {
                var outcome=worldStore.FindAppointment(booking.Id);
                if(outcome!=null && outcome.Status=="player-absent" && outcome.OutcomeRecorded)
                    TryAttemptAppointmentDeparture(plan,actor,outcome,now);
            }
        }

        private void RetryResolvedAppointmentDepartures(double now)
        {
            if(!interior || worldStore==null || population==null)return;
            int day=(int)Math.Floor(now/86400d),revision=worldStore.AppointmentRevision;
            foreach(var plan in plans)
            {
                ResidentActor actor;
                if(!actors.TryGetValue(plan.Resident.Id,out actor) || !actor || !actor.gameObject.activeInHierarchy ||
                    actor.transform.parent!=interior.transform)
                    continue;
                ResolvedAppointmentCache cached;
                if(!resolvedAppointmentCache.TryGetValue(plan.Resident.Id,out cached) || cached.Day!=day || cached.Revision!=revision)
                {
                    var records=worldStore.AppointmentsFor(plan.Resident.Id);
                    records.RemoveAll(item=>!item.OutcomeRecorded || item.PatienceSeconds<=0 ||
                        item.Status!="player-absent" && item.Status!="attended" ||
                        item.Place==null || item.Place.Kind!="building" || item.Place.SettlementId!=population.SettlementId ||
                        (int)Math.Floor(item.StartsAt/86400d)!=day);
                    cached=new ResolvedAppointmentCache {Day=day,Revision=revision,Records=records};
                    resolvedAppointmentCache[plan.Resident.Id]=cached;
                }
                foreach(var record in cached.Records)
                {
                    if(IsAlarmed(plan.Resident.Id))continue;
                    if(record.Place.BuildingKey!=interior.EntryDoor.buildingKey ||
                        now+0.001<AppointmentTiming.PlannedLeaveAt(record))continue;
                    if(record.Status=="attended" && now+0.001<record.EndsAt)continue;
                    AlarmPerson position;bool home;
                    if(worldStore.TryVisitDeparturePosition(plan.Resident.Id,now,out position,out home))
                    {
                        AppointmentDepartureContinuation continuation;
                        if(!worldStore.TryGetAppointmentDepartureContinuation(plan.Resident.Id,now,out continuation) ||
                            continuation.AppointmentId!=record.Id || now>=continuation.ResumeRoutineAt ||
                            AppointmentRecoveryMatches(plan.Resident.Id,continuation,now))continue;
                    }
                    if(TryAttemptAppointmentDeparture(plan,actor,record,now))break;
                }
            }
        }

        private bool TryAttemptAppointmentDeparture(Plan plan,ResidentActor actor,AppointmentRecord appointment,double now)
        {
            float retry;
            if(appointment==null || nextAppointmentDepartureRetry.TryGetValue(appointment.Id,out retry) &&
                Time.unscaledTime<retry)return false;
            if(TryStartAppointmentDeparture(plan,actor,appointment,now))
            {nextAppointmentDepartureRetry.Remove(appointment.Id);return true;}
            nextAppointmentDepartureRetry[appointment.Id]=Time.unscaledTime+5f;
            return false;
        }

        private bool TryStartAppointmentDeparture(Plan plan,ResidentActor actor,AppointmentRecord appointment,double now)
        {
            if(!interior || indoorGeometry==null || !actor || actor.transform.parent!=interior.transform ||
                appointment==null || appointment.Status!="player-absent" && appointment.Status!="attended" ||
                !appointment.OutcomeRecorded || appointment.PatienceSeconds<=0 || IsAlarmed(plan.Resident.Id) ||
                appointment.Place==null || appointment.Place.BuildingKey!=interior.EntryDoor.buildingKey ||
                interior.EntryDoor.buildingKey==plan.Resident.HomeKey) return false;
            var inside=indoorGeometry.RouteToExit(actor.transform.position-Vector3.up);
            var outside=DailyReturnRoute(plan.Routine,interior.EntryDoor.buildingKey);
            if(inside==null || inside.Count<2 || outside==null || outside.Count<2) return false;
            try
            {
                var departure=VisitDeparture.Create(plan.Resident.Id,population.SettlementId,plan.Resident.HomeKey,
                    interior.EntryDoor.buildingKey,now,(Math.Floor(now/86400)+1)*86400,inside,outside,
                    actor.WalkingSpeed,DaggerfallUnity.Instance.WorldTime.TimeScale);
                bool restricted=population.SettlementId==1291010263 && worldStore.RestrictHauntingRoutines(population.SettlementId,
                    GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now);
                int wake=restricted?Math.Max(wakeHour*60,300):wakeHour*60;
                int sleep=restricted?Math.Min(sleepHour*60,1140):sleepHour*60;
                DailySchedule ordinary=restricted?plan.HauntedSchedule:plan.Schedule;
                var previous=CurrentRoutineRecovery(plan,now);
                if(previous!=null)ordinary=DailySchedule.ContinueDay(ordinary,previous);
                RoutineRecoveryPlan recovery;string reason;
                if(!TryCreateRoutineRecoveryWithPoints(plan,ordinary,(int)Math.Floor(now/86400d),now,
                    interior.EntryDoor.buildingKey,departure.Motion.ExitAt,wake,sleep,
                    DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,out recovery,out reason,out _,true))
                {
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_RETURN_UNAVAILABLE: "+plan.Resident.Id+"; "+reason);
                    return false;
                }
                departure=VisitDeparture.Copy(new VisitDeparture {ResidentId=departure.ResidentId,SettlementId=departure.SettlementId,
                    HomeKey=departure.HomeKey,Until=departure.Until,Motion=departure.Motion,AppointmentId=appointment.Id,
                    ResumeRoutineAt=departure.Motion.ExitAt});
                recovery=RoutineRecoveryPlan.Copy(recovery);
                // Store the full measured home route first as a valid fallback. The
                // schedule only takes over after the exact indoor exit and a matching
                // persisted recovery are both available.
                worldStore.SaveVisitDeparture(departure);
                worldStore.SaveRoutineRecovery(recovery);
                InvalidateRoutineRecovery(plan.Resident.Id);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_RECOVERY_HANDOFF: resident="+plan.Resident.Id+"; appointment="+appointment.Id+
                    "; source="+interior.EntryDoor.buildingKey+"; started="+departure.Motion.StartedAt.ToString("G15",System.Globalization.CultureInfo.InvariantCulture)+
                    "; indoorExit="+departure.Motion.ExitAt.ToString("G15",System.Globalization.CultureInfo.InvariantCulture)+
                    "; recoveryCreated="+recovery.CreatedAt.ToString("G15",System.Globalization.CultureInfo.InvariantCulture)+
                    "; recoveryDepart="+recovery.DepartAt.ToString("G15",System.Globalization.CultureInfo.InvariantCulture)+"; originOutside=true");
                RemovePhysicalShopVisit(plan.Resident.Id);
                worldStore.RemoveObservedVisit(plan.Resident.Id);
                shopDepartures.Remove(plan.Resident.Id);
                return true;
            }
            catch(ArgumentException error)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_RETURN_UNAVAILABLE: "+plan.Resident.Id+"; "+error.Message);
                return false;
            }
        }
    }
}
