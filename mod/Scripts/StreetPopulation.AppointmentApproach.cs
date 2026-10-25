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
        private sealed class ProactiveContactMotion
        {
            public string ContactId;
            public ProactiveContactKind Kind;
            public double ApproachUntil;
            public ActionMotion Motion;
            public float NextTalkAttempt;
            public bool WaitingForConversation;
            public bool ArrivalReported;
            public bool ReturningToRoutine;
            public Vector3 InitialPosition;
            public int RecoveryOrigin=-1;
        }

        private readonly Dictionary<string,ProactiveContactMotion> proactiveContactMotions=
            new Dictionary<string,ProactiveContactMotion>(StringComparer.Ordinal);
        private string activeProactiveContactResident;

        // Runs at the existing half-second appointment-observation cadence. It
        // queries only already-materialized residents in the loaded scene.
        private void ObserveProactiveContacts(double now)
        {
            if(worldStore==null || GameManager.IsGamePaused || GameManager.Instance.PlayerEntity.IsInvisible ||
                GameManager.Instance.PlayerEntity.IsMagicallyConcealed || !GameManager.Instance.IsPlayerOnHUD ||
                GameManager.Instance.AreEnemiesNearby(false,false) ||
                worldStore.IsNpcProactiveContactSuppressed(now)) return;
            foreach(var pair in actors)
            {
                var actor=pair.Value;
                if(!actor || !actor.gameObject.activeInHierarchy) continue;
                Plan plan;
                if(!appointmentObservationPlans.TryGetValue(pair.Key,out plan)) continue;
                var scheduled=GetState(pair.Key,now);
                if(!plan.Resident.IsAlive || plan.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                    plan.Resident.IsQuestProtected || QuestOwned(plan.Resident) || actor.IsSleeping || actor.IsConversing ||
                    IsAlarmed(pair.Key) || worldStore.IsResidentAway(pair.Key,now) || shopDepartures.ContainsKey(pair.Key) ||
                    worldStore.ActiveNpcAction(pair.Key)!=null) continue;
                ProactiveContactOffer offer;
                if(!worldStore.TryGetProactiveContactOffer(pair.Key,now,out offer)) continue;
                if(!CanApproachContact(plan,actor,offer,now,false,scheduled)) continue;
                // A captured outdoor meeting is noticed only when the resident
                // has physically reached that exact point. Once noticed, the
                // resident may leave it to approach the visible player.
                if(offer.Kind==ProactiveContactKind.AppointmentMeeting && offer.Place!=null &&
                    offer.Place.Kind=="world-point" && offer.NoticedAt<0 && !AtCapturedMeetingPoint(actor,offer.Place))
                    continue;
                Vector3 player=GameManager.Instance.PlayerMotor.transform.position;
                if(Vector3.Distance(actor.transform.position,player)>20 ||
                    !CivilianResponses.Sees(actor.transform.position,player,20) ||
                    !CivilianResponses.Sees(player,actor.transform.position,20)) continue;

                double safeCutoff=SafeProactiveContactCutoff(plan,actor,offer,now);
                if(offer.NoticedAt<0)
                {
                    if(!worldStore.TryNoticeProactiveContactPlayer(offer,now,safeCutoff)) continue;
                    if(!worldStore.TryGetProactiveContactOffer(pair.Key,now,out offer) || offer.NoticedAt<0) continue;
                }
                if(offer.ApproachUntil<=now)
                {
                    if(offer.NoticedAt>=0)
                        worldStore.TryInterruptProactiveContactApproach(offer.ContactId,offer.Kind,offer.ResidentId,now);
                    continue;
                }

                ProactiveContactMotion motion;
                if(!proactiveContactMotions.TryGetValue(pair.Key,out motion) || motion.ContactId!=offer.ContactId)
                {
                    // Core arbitration permits one active approach. A meeting can
                    // preempt a lower-priority follow-up or greeting.
                    if(!string.IsNullOrEmpty(activeProactiveContactResident) && activeProactiveContactResident!=pair.Key &&
                        offer.Kind!=ProactiveContactKind.AppointmentMeeting) continue;
                    if(!string.IsNullOrEmpty(activeProactiveContactResident) && activeProactiveContactResident!=pair.Key)
                        BeginPreemptedContactRejoin(activeProactiveContactResident);
                    motion=new ProactiveContactMotion {ContactId=offer.ContactId,Kind=offer.Kind,
                        ApproachUntil=offer.ApproachUntil,InitialPosition=actor.transform.position-Vector3.up};
                    proactiveContactMotions[pair.Key]=motion;
                    activeProactiveContactResident=pair.Key;
                    if(offer.Kind==ProactiveContactKind.AppointmentMeeting)
                        SuspendAmbientVisitForApproach(pair.Key,actor);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_CONTACT_APPROACH: resident="+pair.Key+"; contact="+offer.ContactId+
                        "; kind="+offer.Kind+"; noticed="+offer.NoticedAt.ToString("G15",System.Globalization.CultureInfo.InvariantCulture)+
                        "; approachUntil="+offer.ApproachUntil.ToString("G15",System.Globalization.CultureInfo.InvariantCulture)+
                        "; resumed="+(offer.NoticedAt<now)+"; start="+(actor.transform.position-Vector3.up).ToString("F3"));
                }
                else motion.ApproachUntil=offer.ApproachUntil;
            }
        }

        private bool AtCapturedMeetingPoint(ResidentActor actor,AppointmentPlace place)
        {
            if(!actor || place==null || geometry==null || directory==null ||
                place.SettlementId!=population.SettlementId || place.Kind!="world-point") return false;
            Vector3 target=geometry.ScenePoint(new RoutePoint(place.X,place.Z));
            return Vector3.Distance(actor.transform.position-Vector3.up,target)<=1.35f;
        }

        private bool CanApproachContact(Plan plan,ResidentActor actor,ProactiveContactOffer offer,
            double now,bool requireVenue,ActivitySnapshot scheduled)
        {
            var game=GameManager.Instance;
            if(plan==null || actor==null || !actor.gameObject.activeInHierarchy || actor.IsSleeping || actor.IsConversing ||
                !plan.Resident.IsAlive || plan.Resident.AgeGroup!=ResidentAgeGroup.Adult || plan.Resident.IsQuestProtected ||
                QuestOwned(plan.Resident) || IsAlarmed(plan.Resident.Id) || worldStore==null ||
                worldStore.IsResidentAway(plan.Resident.Id,now) || shopDepartures.ContainsKey(plan.Resident.Id) ||
                worldStore.ActiveNpcAction(plan.Resident.Id)!=null || GameManager.IsGamePaused ||
                game.PlayerEntity.IsInvisible || game.PlayerEntity.IsMagicallyConcealed ||
                game.AreEnemiesNearby(false,false)) return false;

            bool sameScene=interior ? actor.transform.parent==interior.transform && game.PlayerEnterExit.IsPlayerInsideBuilding :
                directory && actor.transform.parent==directory.transform && !game.PlayerEnterExit.IsPlayerInsideBuilding;
            if(!sameScene) return false;
            if(requireVenue || offer.Kind==ProactiveContactKind.AppointmentMeeting)
            {
                if(offer.Place==null || offer.Place.SettlementId!=population.SettlementId) return false;
                if(offer.Place.Kind=="building")
                {
                    if(!interior || interior.EntryDoor.buildingKey!=offer.Place.BuildingKey) return false;
                }
                else if(offer.Place.Kind=="world-point")
                {
                    if(interior || !directory || geometry==null || scheduled.Activity!=Activity.Meeting ||
                        actor.transform.parent!=directory.transform)
                        return false;
                }
                else return false;
            }

            // Appointment meetings may interrupt an ambient waiting visit, but
            // never a real counter service. Other contacts only interrupt idle or
            // ordinary travel, never visits, seats, work, or shop activity.
            PhysicalShopVisit visit;
            bool hasVisit=physicalShopVisits.TryGetValue(plan.Resident.Id,out visit);
            if(shopGeometry!=null && shopGeometry.IsTavern && hasVisit &&
                visit.Snapshot.Phase==ShopVisitPhase.Haggling) return false;
            if(offer.Kind!=ProactiveContactKind.AppointmentMeeting)
            {
                if(actor.IsSeated || hasVisit || scheduled.Activity!=Activity.Home && scheduled.Activity!=Activity.Travel)
                    return false;
            }
            return true;
        }

        private double SafeProactiveContactCutoff(Plan plan,ResidentActor actor,ProactiveContactOffer offer,double now)
        {
            double proposed=Math.Min(now+120,(Math.Floor(now/86400d)*86400d)+sleepHour*60d);
            if(offer.Kind!=ProactiveContactKind.AppointmentMeeting || offer.AppointmentId==null)
                return SafeRoutineContactCutoff(plan,actor,now,proposed);

            var booking=worldStore.FindAppointment(offer.AppointmentId);
            if(booking==null) return now;
            double promised=AppointmentTiming.PlannedLeaveAt(booking);
            if(promised<=now || proposed<=promised || worldStore.IsResidentAway(plan.Resident.Id,proposed)) return Math.Max(now,promised);

            // Extending the already-reserved meeting window needs one detached
            // routine preflight; the live itinerary and appointment stay intact.
            var bookings=worldStore.AppointmentsFor(plan.Resident.Id);
            bool replaced=false;
            for(int i=0;i<bookings.Count;i++)
                if(bookings[i].Id==booking.Id)
                {
                    var candidate=CopyAppointmentProposal(bookings[i]);
                    candidate.PlayerNoticedAt=now;candidate.ApproachUntil=proposed;
                    bookings[i]=candidate;replaced=true;break;
                }
            if(!replaced) return promised;
            bool restricted=population.SettlementId==1291010263 && !GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0);
            int wake=restricted?Math.Max(wakeHour*60,300):wakeHour*60;
            int sleep=restricted?Math.Min(sleepHour*60,1140):sleepHour*60;
            IList<AppointmentPlaceRoute> pointRoutes=null;
            if(bookings.Exists(item=>item.Place!=null && item.Place.Kind=="world-point" &&
                (int)Math.Floor(item.StartsAt/86400d)==(int)Math.Floor(now/86400d) && item.Status!="cancelled"))
            {
                string routeReason;
                pointRoutes=BuildAppointmentPlaceRoutes(plan,bookings,(int)Math.Floor(now/86400d),out routeReason,true);
                int requiredPoints=bookings.FindAll(item=>item.Place!=null && item.Place.Kind=="world-point" &&
                    (int)Math.Floor(item.StartsAt/86400d)==(int)Math.Floor(now/86400d) && item.Status!="cancelled").Count;
                if(pointRoutes.Count!=requiredPoints) return promised;
            }
            DailySchedule preflight;string reason;
            return AppointmentDayPlanner.TryCreate(plan.Resident,plan.Routine,(int)Math.Floor(now/86400d),
                bookings,pointRoutes,wake,sleep,out preflight,out reason)?proposed:promised;
        }

        private double SafeRoutineContactCutoff(Plan plan,ResidentActor actor,double now,double proposed)
        {
            if(proposed<=now || actor==null || geometry==null)return now;
            if(interior)return proposed; // Return to the saved in-room position before resuming Home.
            int origin=-1;double returnSeconds=double.PositiveInfinity;
            var keys=new HashSet<int>{plan.Resident.HomeKey};
            if(plan.Routine.Itinerary!=null)
                foreach(var itinerary in ResidentRoutineDay.Itineraries(plan.Routine))
                    foreach(var stop in itinerary.Stops) keys.Add(stop.BuildingKey);
            else if(plan.Routine.DestinationKey>=0) keys.Add(plan.Routine.DestinationKey);
            var actual=directory.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            foreach(int key in keys)
            {
                if(!TryRoutineRecoveryDoor(plan,key,out var door))continue;
                var route=geometry.RouteBetweenPoints(new SavedRoutePoint {X=actual.x,Z=actual.z},door);
                if(route==null || route.Count<2)continue;
                double length=0;
                for(int i=1;i<route.Count;i++)
                {double x=route[i].X-route[i-1].X,z=route[i].Z-route[i-1].Z;length+=Math.Sqrt(x*x+z*z);}
                double seconds=length*DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed;
                if(seconds<returnSeconds){origin=key;returnSeconds=seconds;}
            }
            if(origin<0 || double.IsInfinity(returnSeconds))return now;

            // Budget a worst-case 20 m approach plus 20 m from the player back
            // toward the selected routine door. Then ask the real continuation
            // planner to preserve all fixed bookings, remaining visits, and sleep.
            double physicalReturn=returnSeconds+40*DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed;
            if(!CanContinueRoutineAfterContact(plan,actor,now,origin,now+physicalReturn))return now;
            double low=0,high=Math.Min(120,proposed-now);
            for(int i=0;i<8;i++)
            {
                double middle=(low+high)*0.5;
                if(CanContinueRoutineAfterContact(plan,actor,now,origin,now+middle+physicalReturn))low=middle;
                else high=middle;
            }
            return low>=15?now+low:now;
        }

        private bool CanContinueRoutineAfterContact(Plan plan,ResidentActor actor,double now,int origin,double departAt)
        {
            bool restricted=population.SettlementId==1291010263 && worldStore.RestrictHauntingRoutines(population.SettlementId,
                GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now);
            int wake=restricted?Math.Max(wakeHour*60,300):wakeHour*60;
            int sleep=restricted?Math.Min(sleepHour*60,1140):sleepHour*60;
            DailySchedule ordinary=restricted?plan.HauntedSchedule:plan.Schedule;
            var previous=CurrentRoutineRecovery(plan,now);
            if(previous!=null)ordinary=DailySchedule.ContinueDay(ordinary,previous);
            return TryCreateRoutineRecoveryWithPoints(plan,ordinary,(int)Math.Floor(now/86400d),now,origin,departAt,
                wake,sleep,DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,out _,out _,out _,true);
        }

        private void SuspendAmbientVisitForApproach(string residentId,ResidentActor actor)
        {
            PhysicalShopVisit visit;
            if(physicalShopVisits.TryGetValue(residentId,out visit)) visit.SuspendAndReplanFromActual(actor,interior);
            if(shopGeometry!=null) shopGeometry.ReleaseCounter(residentId);
        }

        private void BeginPreemptedContactRejoin(string residentId)
        {
            ProactiveContactMotion previous;
            ResidentActor actor;
            if(proactiveContactMotions.TryGetValue(residentId,out previous) &&
                actors.TryGetValue(residentId,out actor) && actor && actor.gameObject.activeInHierarchy)
            {
                previous.WaitingForConversation=false;
                previous.ReturningToRoutine=true;
                previous.InitialPosition=actor.transform.position-Vector3.up;
                previous.RecoveryOrigin=-1;previous.Motion=null;previous.NextTalkAttempt=0;
            }
            else proactiveContactMotions.Remove(residentId);
            if(activeProactiveContactResident==residentId)activeProactiveContactResident=null;
        }

        private bool ApplyProactiveContactApproach(Plan plan,ref ResidentActor actor,double now)
        {
            ProactiveContactMotion motion;
            if(!proactiveContactMotions.TryGetValue(plan.Resident.Id,out motion)) return false;
            if(worldStore.ActiveNpcAction(plan.Resident.Id)!=null)
            {ClearProactiveContactMotion(plan.Resident.Id);return false;}
            if(GameManager.IsGamePaused || !GameManager.Instance.IsPlayerOnHUD)return true;
            if(motion.WaitingForConversation)
            {
                if(actor && actor.IsConversing) return true;
                motion.WaitingForConversation=false;
                if(motion.Kind==ProactiveContactKind.AppointmentMeeting)
                {ClearProactiveContactMotion(plan.Resident.Id);return true;}
                motion.ReturningToRoutine=true;motion.Motion=null;
                motion.InitialPosition=actor?actor.transform.position-Vector3.up:motion.InitialPosition;
            }
            if(motion.ReturningToRoutine)
                return ApplyProactiveContactRoutineRejoin(plan,actor,motion,now);
            if(worldStore.IsNpcProactiveContactSuppressed(now))
            {StartProactiveContactRejoin(plan,actor,motion);return true;}

            ProactiveContactOffer offer;
            if(!actor || !actor.gameObject.activeInHierarchy ||
                !worldStore.TryGetProactiveContactOffer(plan.Resident.Id,now,out offer) ||
                offer.ContactId!=motion.ContactId || offer.Kind!=motion.Kind)
            {
                if(!actor){ClearProactiveContactMotion(plan.Resident.Id);return false;}
                if(actor && actor.IsConversing)return true;
                StartProactiveContactRejoin(plan,actor,motion);return true;
            }
            if(now>=motion.ApproachUntil || !CanApproachContact(plan,actor,offer,now,false,GetState(plan.Resident.Id,now)))
            {
                InterruptProactiveContact(offer,now);
                if(motion.Kind==ProactiveContactKind.AppointmentMeeting)
                {ClearProactiveContactMotion(plan.Resident.Id);return false;}
                StartProactiveContactRejoin(plan,actor,motion);return true;
            }

            Vector3 player=GameManager.Instance.PlayerMotor.transform.position;
            float distance=Vector3.Distance(actor.transform.position,player);
            bool sees=distance<=20 && !GameManager.Instance.PlayerEntity.IsInvisible &&
                !GameManager.Instance.PlayerEntity.IsMagicallyConcealed &&
                CivilianResponses.Sees(actor.transform.position,player,20) && CivilianResponses.Sees(player,actor.transform.position,20);
            if(!sees)
            {
                InterruptProactiveContact(offer,now);
                if(motion.Kind==ProactiveContactKind.AppointmentMeeting)
                {ClearProactiveContactMotion(plan.Resident.Id);return false;}
                StartProactiveContactRejoin(plan,actor,motion);return true;
            }

            if(distance<=1.35f)
            {
                motion.Motion=null;
                if(!motion.ArrivalReported)
                {
                    motion.ArrivalReported=true;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_CONTACT_ARRIVED: resident="+plan.Resident.Id+"; contact="+
                        motion.ContactId+"; kind="+motion.Kind+"; distance="+distance.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                }
                if(Time.unscaledTime>=motion.NextTalkAttempt)
                {
                    motion.NextTalkAttempt=Time.unscaledTime+0.5f;
                    var host=GetComponent<PeopleOfTheIliacBayMod>();
                    if(host && host.TryInitiateProactiveContactConversation(plan.Resident.Id,motion.ContactId))
                    {motion.WaitingForConversation=true;return true;}
                }
                actor.Place(actor.transform.position-Vector3.up,player-actor.transform.position,true,waitAtOccupiedTarget:true);
                return true;
            }

            Vector3 target=player-Vector3.up;
            if(motion.Motion==null || Time.unscaledTime>=motion.Motion.RepathAt ||
                Vector3.Distance(target,motion.Motion.Goal)>0.75f)
            {
                var routeMotion=new ActionMotion {LastProgress=actor.transform.position,ProgressAt=Time.unscaledTime};
                string reason;
                if(!BuildActionRoute(actor,routeMotion,target,1.35f,out reason))
                {
                    InterruptProactiveContact(offer,now);
                    if(motion.Kind==ProactiveContactKind.AppointmentMeeting)
                    {ClearProactiveContactMotion(plan.Resident.Id);return false;}
                    StartProactiveContactRejoin(plan,actor,motion);return true;
                }
                motion.Motion=routeMotion;
            }
            if(Vector3.Distance(actor.transform.position,motion.Motion.LastProgress)>=0.15f)
            {motion.Motion.LastProgress=actor.transform.position;motion.Motion.ProgressAt=Time.unscaledTime;}
            else if(Time.unscaledTime-motion.Motion.ProgressAt>12f)
            {
                InterruptProactiveContact(offer,now);
                if(motion.Kind==ProactiveContactKind.AppointmentMeeting)
                {ClearProactiveContactMotion(plan.Resident.Id);return false;}
                StartProactiveContactRejoin(plan,actor,motion);return true;
            }
            AdvanceActionMotion(actor,motion.Motion);
            return true;
        }

        private void InterruptProactiveContact(ProactiveContactOffer offer,double now)
        {worldStore.TryInterruptProactiveContactApproach(offer.ContactId,offer.Kind,offer.ResidentId,now);}

        private void StartProactiveContactRejoin(Plan plan,ResidentActor actor,ProactiveContactMotion motion)
        {
            if(!actor || motion==null)return;
            motion.WaitingForConversation=false;motion.ReturningToRoutine=true;motion.Motion=null;
            motion.InitialPosition=actor.transform.position-Vector3.up;motion.RecoveryOrigin=-1;
            motion.NextTalkAttempt=0;
            if(activeProactiveContactResident==plan.Resident.Id)activeProactiveContactResident=null;
        }

        private bool ApplyProactiveContactRoutineRejoin(Plan plan,ResidentActor actor,
            ProactiveContactMotion contact,double now)
        {
            if(!actor || !actor.gameObject.activeInHierarchy) return true;
            if(interior)
            {
                if(actor.transform.parent!=interior.transform)return true;
                if(Vector3.Distance(actor.transform.position-Vector3.up,contact.InitialPosition)<=0.35f)
                {ClearProactiveContactMotion(plan.Resident.Id);return true;}
                if(Time.unscaledTime<contact.NextTalkAttempt)
                {actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);return true;}
                if(contact.Motion==null || Time.unscaledTime>=contact.Motion.RepathAt)
                {
                    contact.Motion=new ActionMotion {LastProgress=actor.transform.position,ProgressAt=Time.unscaledTime};
                    if(!BuildActionRoute(actor,contact.Motion,contact.InitialPosition,0.25f,out _))
                    {contact.NextTalkAttempt=Time.unscaledTime+30;contact.Motion=null;return true;}
                }
                return AdvanceContactRejoin(actor,contact);
            }

            if(actor.transform.parent!=directory.transform || geometry==null)return true;
            if(contact.RecoveryOrigin<0)
            {
                if(Time.unscaledTime<contact.NextTalkAttempt)
                {actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);return true;}
                var keys=new HashSet<int>{plan.Resident.HomeKey};
                if(plan.Routine.Itinerary!=null)
                    foreach(var itinerary in ResidentRoutineDay.Itineraries(plan.Routine))
                    foreach(var stop in itinerary.Stops) keys.Add(stop.BuildingKey);
                else if(plan.Routine.DestinationKey>=0) keys.Add(plan.Routine.DestinationKey);
                float nearest=float.PositiveInfinity;
                foreach(int key in keys)
                {
                    if(!TryRoutineRecoveryDoor(plan,key,out var door))continue;
                    Vector3 target=geometry.ScenePoint(new RoutePoint(door.X,door.Z));
                    float distance=(target-(actor.transform.position-Vector3.up)).sqrMagnitude;
                    if(distance>=nearest)continue;
                    nearest=distance;contact.RecoveryOrigin=key;
                    contact.InitialPosition=target;
                }
                if(contact.RecoveryOrigin<0)
                {contact.NextTalkAttempt=Time.unscaledTime+30;actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);return true;}
            }

            if(Vector3.Distance(actor.transform.position-Vector3.up,contact.InitialPosition)<=0.35f)
            {
                if(Time.unscaledTime<contact.NextTalkAttempt)
                {actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);return true;}
                if(!TryPlanRoutineRecovery(plan,actor,now,contact.RecoveryOrigin,true,out var recovery,out var reason))
                {
                    contact.NextTalkAttempt=Time.unscaledTime+30;
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_CONTACT_REJOIN_WAIT: "+plan.Resident.Id+"; "+reason);
                    actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);return true;
                }
                worldStore.SaveRoutineRecovery(recovery);
                InvalidateRoutineRecovery(plan.Resident.Id);
                RemovePhysicalShopVisit(plan.Resident.Id);worldStore.RemoveObservedVisit(plan.Resident.Id);
                shopJourneys.Remove(plan.Resident.Id);shopDepartures.Remove(plan.Resident.Id);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_CONTACT_REJOINED: resident="+plan.Resident.Id+"; origin="+
                    contact.RecoveryOrigin+"; outside=true; created="+now.ToString("G15",System.Globalization.CultureInfo.InvariantCulture));
                ClearProactiveContactMotion(plan.Resident.Id);return true;
            }

            if(Time.unscaledTime<contact.NextTalkAttempt)
            {actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);return true;}
            if(contact.Motion==null || Time.unscaledTime>=contact.Motion.RepathAt)
            {
                var recoveryMotion=new ActionMotion {LastProgress=actor.transform.position,ProgressAt=Time.unscaledTime};
                if(!BuildActionRoute(actor,recoveryMotion,contact.InitialPosition,0.25f,out _))
                {contact.NextTalkAttempt=Time.unscaledTime+30;contact.Motion=null;return true;}
                contact.Motion=recoveryMotion;
            }
            return AdvanceContactRejoin(actor,contact);
        }

        private bool AdvanceContactRejoin(ResidentActor actor,ProactiveContactMotion contact)
        {
            var motion=contact.Motion;
            if(Vector3.Distance(actor.transform.position,motion.LastProgress)>=0.15f)
            {motion.LastProgress=actor.transform.position;motion.ProgressAt=Time.unscaledTime;}
            else if(Time.unscaledTime-motion.ProgressAt>12f)
            {contact.NextTalkAttempt=Time.unscaledTime+30;contact.Motion=null;actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);return true;}
            AdvanceActionMotion(actor,motion);return true;
        }

        private void ClearProactiveContactMotion(string residentId)
        {
            proactiveContactMotions.Remove(residentId);
            if(activeProactiveContactResident==residentId) activeProactiveContactResident=null;
        }

        private void ClearAppointmentApproaches()
        {proactiveContactMotions.Clear();activeProactiveContactResident=null;}
    }
}
