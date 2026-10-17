using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class TravelerPlan
        {
            public TravelerPerson Person;
            public TravelerVisit Visit;
            public Resident Resident;
            public TravelerJourney Journey;
            public TravelerAdmission JourneyAdmission;
            public ResidentRoutine Routine;
            public DaggerfallInterior RoomInterior;
            public BedAnchor Bed;
            public Vector3 LocalStanding;
            public bool HasStanding;
            public TravelerRoomWalk RoomWalk;
            public InteriorRoomFollower RoomFollower;
            public double RoomSecondsPerMetre;
            public float NextRoomExitRetryAt;
            public bool ObservedOutdoorArrival;
            public bool PendingDoorwayArrival;
        }
        private readonly Dictionary<string,TravelerPlan> travelerPlans=new Dictionary<string,TravelerPlan>();
        private float nextTravelerRefresh;
        private double travelerWindowDay=-1;
        private long travelerWindowRevision=-1;
        private List<TravelerVisit> travelerWindow=new List<TravelerVisit>();
        public int TravelerPlanCount { get { return travelerPlans.Count; } }
        private void RefreshTravelerPlans(double seconds)
        {
            if(Time.unscaledTime<nextTravelerRefresh) return;
            nextTravelerRefresh=Time.unscaledTime+0.5f;
            double day=Math.Floor(seconds/86400);
            if(day!=travelerWindowDay || travelerWindowRevision!=worldStore.TravelerVisitRevision)
            {
                travelerWindow=worldStore.TravelerVisitsDuring(population.SettlementId,day*86400,(day+1)*86400);
                travelerWindowDay=day; travelerWindowRevision=worldStore.TravelerVisitRevision;
            }
            foreach(var id in new List<string>(travelerPlans.Keys))
            {
                var plan=travelerPlans[id];
                ResidentActor oldActor;
                actors.TryGetValue(id,out oldActor);
                if(plan.JourneyAdmission!=plan.Visit.Admission)
                {
                    plan.Journey=worldStore.TravelerJourneyFor(plan.Visit.Id);
                    plan.JourneyAdmission=plan.Visit.Admission;
                    plan.RoomWalk=null;
                    plan.RoomFollower=null;
                    plan.RoomInterior=null;
                }
                ObserveTravelerArrivalBoundary(oldActor,plan,seconds);
                bool pendingArrival=IsPendingTravelerArrival(oldActor,plan,seconds);
                bool pendingRoomExit=Reaction(id,seconds)==null && interior && plan.RoomInterior==interior && plan.RoomFollower!=null &&
                    (plan.RoomFollower.Leaving && !plan.RoomFollower.Arrived || seconds>=plan.Journey.CheckOutAt &&
                    (!plan.RoomFollower.Arrived || !plan.RoomFollower.Leaving));
                if(plan.Person.IsAlive && ((seconds>=plan.Journey.EntryAt && seconds<plan.Journey.ExitAt) ||
                    Reaction(id,seconds)!=null || pendingArrival || pendingRoomExit)) continue;
                if(oldActor) {oldActor.gameObject.SetActive(false); Destroy(oldActor.gameObject);}
                actors.Remove(id); travelerPlans.Remove(id);
            }
            foreach(var visit in travelerWindow)
            {
                TravelerPlan previous;
                travelerPlans.TryGetValue(visit.PersonId,out previous);
                if(previous!=null && previous.JourneyAdmission==visit.Admission && (previous.Visit.Id==visit.Id ||
                    seconds>=previous.Journey.EntryAt && seconds<previous.Journey.ExitAt)) continue;
                var journey=worldStore.TravelerJourneyFor(visit.Id);
                if(seconds<journey.EntryAt || seconds>=journey.ExitAt) continue;
                var person=worldStore.Traveler(visit.PersonId);
                if(!person.IsAlive) continue;
                var routine=new ResidentRoutine { ResidentId=person.Id };
                foreach(var point in visit.Route) routine.Route.Add(new SavedRoutePoint { X=point.X,Z=point.Z });
                if(previous!=null)
                {
                    ResidentActor priorActor;
                    if(actors.TryGetValue(person.Id,out priorActor) && priorActor)
                    { priorActor.gameObject.SetActive(false); Destroy(priorActor.gameObject); }
                    actors.Remove(person.Id);
                }
                travelerPlans[person.Id]=new TravelerPlan { Person=person,Visit=visit,Journey=journey,JourneyAdmission=visit.Admission,Routine=routine,
                    Resident=new Resident { Id=person.Id,FullName=person.Name,Race=person.Race,Sex=person.Sex,
                        AppearanceSeed=person.AppearanceSeed,AgeGroup=ResidentAgeGroup.Adult,HomeKey=-1,IsAlive=person.IsAlive } };
            }
        }
        private ActivitySnapshot TravelerState(TravelerPlan plan,double seconds)
        {
            var state=plan.Journey.Evaluate(seconds);
            bool paid=plan.Visit.Admission==TravelerAdmission.Admitted;
            bool inside=paid && seconds>=plan.Journey.CheckInAt && seconds<plan.Journey.LeaveInnAt;
            bool sleeping=state.Phase==TravelerPhase.Sleeping && plan.RoomFollower!=null &&
                plan.RoomFollower.Arrived && !plan.RoomFollower.Leaving;
            return new ActivitySnapshot(inside ? sleeping ? Activity.Sleep : Activity.Visit : Activity.Travel,
                inside ? plan.Visit.TavernKey : -1,state.Position,0);
        }
        private void UpdateTravelerActors(double seconds)
        {
            RefreshTravelerPlans(seconds);
            foreach(var plan in travelerPlans.Values)
            {
                ResidentActor actor; actors.TryGetValue(plan.Person.Id,out actor);
                plan.Resident.IsAlive=plan.Person.IsAlive;
                var state=plan.Journey.Evaluate(seconds);
                if(state.Phase==TravelerPhase.Arriving && actor && directory && actor.transform.parent==directory.transform)
                    plan.ObservedOutdoorArrival=true;
                var reaction=Reaction(plan.Person.Id,seconds);
                if(PlaceTravelerInside(plan,actor,state,reaction,seconds)) continue;
                bool route=state.Phase==TravelerPhase.Arriving || state.Phase==TravelerPhase.Leaving ||
                    plan.Visit.Admission!=TravelerAdmission.Admitted && state.Phase!=TravelerPhase.NotArrived && state.Phase!=TravelerPhase.Departed;
                Vector3 ground=geometry.ScenePoint(state.Position);
                bool continuingArrival=reaction==null && ContinueTravelerArrival(actor,plan,state,seconds,out ground);
                route|=continuingArrival;
                if(reaction!=null && reaction.HasPosition)
                {
                    route=reaction.BuildingKey==-1 && !reaction.ReachedRefuge;
                    ground=directory.transform.TransformPoint(new Vector3((float)reaction.X,(float)reaction.Y,(float)reaction.Z));
                }
                bool visible=!interior && !GameManager.Instance.IsPlayerInside && plan.Person.IsAlive && route &&
                    (ground-GameManager.Instance.PlayerMotor.transform.position).sqrMagnitude<=(actor?165*165:140*140);
                if(!visible)
                {
                    if(actor) {actor.gameObject.SetActive(false); Destroy(actor.gameObject);}
                    actors.Remove(plan.Person.Id); continue;
                }
                if(!actor)
                {
                    if(actors.Count>=80) continue;
                    var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,directory.transform);
                    body.name="Living Cities "+plan.Person.Id;
                    actor=body.AddComponent<ResidentActor>();
                    string id=plan.Person.Id;
                    actor.Initialize(plan.Resident,0,navigation,()=>worldStore.KillTraveler(id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()));
                    actor.SetRunningPilotAtlas(runningPilotAtlas);
                    actors.Add(id,actor);
                }
                if(reaction!=null && reaction.HasPosition)
                {
                    if(reaction.Escape!=null) PlaceEscape(actor,reaction,seconds,directory.transform,true);
                    else CivilianResponses.Move(actor,reaction,plan.Routine,directory.transform,geometry,null);
                }
                else
                {
                    var next=plan.Journey.Evaluate(seconds+0.1);
                    Vector3 direction=continuingArrival ? ground-(actor.transform.position-Vector3.up) :
                        geometry.ScenePoint(next.Position)-ground;
                    actor.Place(ground,direction);
                }
            }
        }
        private bool ContinueTravelerArrival(ResidentActor actor,TravelerPlan plan,TravelerSnapshot state,double seconds,out Vector3 ground)
        {
            ground=geometry.ScenePoint(state.Position);
            Vector3 doorway;
            if(state.Phase==TravelerPhase.NotArrived || state.Phase==TravelerPhase.Arriving ||
                !PendingTravelerDoorway(actor,plan,seconds,out doorway)) return false;
            var motion=actor.GetComponent<CrowdMotion>();
            if(motion) motion.ResumeFromPause();
            ground=doorway;
            return true;
        }
        private void ObserveTravelerArrivalBoundary(ResidentActor actor,TravelerPlan plan,double seconds)
        {
            if(!actor) return;
            if(!directory || interior || actor.transform.parent!=directory.transform)
            {
                plan.ObservedOutdoorArrival=false;
                plan.PendingDoorwayArrival=false;
                return;
            }
            if(plan.Visit.Admission!=TravelerAdmission.Admitted) return;
            var phase=plan.Journey.Evaluate(seconds).Phase;
            if(phase==TravelerPhase.Arriving) plan.ObservedOutdoorArrival=true;
            else if(plan.ObservedOutdoorArrival && seconds>=plan.Journey.CheckInAt)
            {
                Vector3 doorway;
                if(PendingTravelerDoorwayGeometry(actor,plan,out doorway) &&
                    Vector3.Distance(actor.transform.position,doorway+Vector3.up)>0.55f)
                    plan.PendingDoorwayArrival=true;
            }
        }
        private bool IsPendingTravelerArrival(ResidentActor actor,TravelerPlan plan,double seconds)
        {
            Vector3 doorway;
            return PendingTravelerDoorway(actor,plan,seconds,out doorway);
        }
        private bool PendingTravelerDoorway(ResidentActor actor,TravelerPlan plan,double seconds,out Vector3 doorway)
        {
            doorway=Vector3.zero;
            if(!plan.PendingDoorwayArrival || !actor || !directory || interior || actor.transform.parent!=directory.transform ||
                plan.Visit.Admission!=TravelerAdmission.Admitted || seconds<plan.Journey.CheckInAt ||
                !PendingTravelerDoorwayGeometry(actor,plan,out doorway)) return false;
            if(Vector3.Distance(actor.transform.position,doorway+Vector3.up)<=0.55f)
            {
                plan.PendingDoorwayArrival=false;
                return false;
            }
            // Fixed travel time can pass while crowd movement stalls. Keep the existing
            // outdoor body at the inn endpoint until it reaches the physical doorway.
            return true;
        }
        private bool PendingTravelerDoorwayGeometry(ResidentActor actor,TravelerPlan plan,out Vector3 doorway)
        {
            doorway=Vector3.zero;
            if(!actor || plan.Visit.Route==null || plan.Visit.Route.Count<2) return false;
            var last=plan.Visit.Route[plan.Visit.Route.Count-1];
            doorway=geometry.ScenePoint(new RoutePoint(last.X,last.Z));
            return true;
        }
        private bool PlaceTravelerInside(TravelerPlan plan,ResidentActor actor,TravelerSnapshot state,AlarmPerson reaction,double seconds)
        {
            if(!interior || !plan.Person.IsAlive) return false;
            bool reacting=reaction!=null && reaction.HasPosition;
            bool pendingRoomExit=!reacting && plan.RoomInterior==interior && plan.RoomFollower!=null &&
                (plan.RoomFollower.Leaving && !plan.RoomFollower.Arrived || seconds>=plan.Journey.CheckOutAt &&
                (!plan.RoomFollower.Arrived || !plan.RoomFollower.Leaving));
            bool present=reacting ? reaction.BuildingKey==interior.EntryDoor.buildingKey :
                plan.Visit.Admission==TravelerAdmission.Admitted && seconds>=plan.Journey.CheckInAt &&
                (seconds<plan.Journey.LeaveInnAt || pendingRoomExit) && plan.Visit.TavernKey==interior.EntryDoor.buildingKey;
            if(!present) return false;
            if(!reacting)
            {
                // After checkout, the old room only identifies the route already
                // walked; the guest waits at the entrance, never occupies the bed.
                string bedId=worldStore.TravelerBed(plan.Visit.Id,Math.Min(seconds,plan.Journey.CheckOutAt-0.01));
                if(bedId==null) return false;
                if(plan.RoomInterior!=interior)
                {
                    plan.RoomInterior=interior;
                    plan.Bed=BedGeometry.Read(interior).Find(b=>"bed:"+b.RecordIndex==bedId);
                    Vector3 standing=Vector3.zero;
                    plan.HasStanding=plan.Bed!=null && indoorGeometry.StandBesideBed(plan.Bed,out standing);
                    if(plan.HasStanding) plan.LocalStanding=interior.transform.InverseTransformPoint(standing);
                    plan.RoomWalk=null;
                    plan.RoomFollower=null;
                    if(plan.HasStanding)
                    {
                        var route=indoorGeometry.RouteToExit(standing);
                        if(route.Count>=2)
                        {
                            route.Reverse();
                            try
                            {
                                double secondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(plan.Resident.AppearanceSeed);
                                plan.RoomWalk=TravelerRoomWalk.Create(route,plan.Journey.CheckInAt,plan.Journey.CheckOutAt,secondsPerMetre);
                                plan.RoomFollower=new InteriorRoomFollower(route,secondsPerMetre);
                                plan.RoomSecondsPerMetre=secondsPerMetre;
                            }
                            catch(ArgumentException) {Debug.LogWarning("LIVING_CITIES_TRAVELER_ROOM_ROUTE_MISSING: "+plan.Visit.Id);}
                        }
                        else Debug.LogWarning("LIVING_CITIES_TRAVELER_ROOM_ROUTE_MISSING: "+plan.Visit.Id);
                    }
                }
                if(plan.Bed==null || !plan.HasStanding) return false;
            }
            bool spawnedInside=false;
            if(!actor)
            {
                if(actors.Count>=80) return true;
                var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,interior.transform);
                body.name="Living Cities "+plan.Person.Id;
                actor=body.AddComponent<ResidentActor>();
                string id=plan.Person.Id;
                actor.Initialize(plan.Resident,0,navigation,()=>worldStore.KillTraveler(id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()));
                actor.SetRunningPilotAtlas(runningPilotAtlas);
                actors.Add(id,actor); body.SetActive(true);
                if(indoorGeometry.EntryGround.HasValue)
                {
                    actor.transform.position=indoorGeometry.EntryGround.Value+Vector3.up;
                    actor.Place(indoorGeometry.EntryGround.Value,Vector3.forward,true);
                }
                spawnedInside=true;
            }
            if(plan.RoomFollower!=null && !plan.RoomFollower.Arrived && !plan.RoomFollower.Leaving)
            {
                Vector3 roomPoint=interior.transform.TransformPoint(plan.LocalStanding);
                bool scheduleAlreadyReached=seconds>=plan.Journey.CheckInAt+plan.RoomFollower.Duration+5;
                bool alreadyAtRoom=Vector3.Distance(actor.transform.position,roomPoint+Vector3.up)<=0.45f;
                if(alreadyAtRoom || spawnedInside && scheduleAlreadyReached)
                {
                    // New bodies may reconstruct a completed schedule at the room.
                    // Existing bodies always walk the surveyed route.
                    if(spawnedInside && scheduleAlreadyReached && !alreadyAtRoom)
                    {
                        actor.transform.position=roomPoint+Vector3.up;
                        var motion=actor.GetComponent<CrowdMotion>();
                        if(motion) motion.ResetPlacement();
                    }
                    plan.RoomFollower.RestoreAtDestination();
                }
            }
            if(ReactionsEnabled && !reacting)
            {
                var corpse=worldStore.ObserveCorpses(population.SettlementId,interior.EntryDoor.buildingKey,seconds)
                    .Find(a=>a.BuildingKey==interior.EntryDoor.buildingKey && a.ThreatId==null);
                if(corpse!=null)
                {
                    var observer=worldStore.NoticeInteriorCorpse(corpse.Id,plan.Person.Id,interior.EntryDoor.buildingKey,seconds);
                    if(observer!=null && CivilianAlarms.PersonActive(corpse,observer,seconds))
                    {
                        if(!observer.HasPosition)
                        {
                            var local=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                            observer.HasPosition=true; observer.BuildingKey=interior.EntryDoor.buildingKey;
                            observer.X=local.x; observer.Y=local.y; observer.Z=local.z;
                        }
                        reaction=observer; reacting=true;
                        PrepareIndoorEscapes(seconds);
                    }
                }
            }
            if(reacting)
            {
                if(!WakeResidentForAlarm(actor,reaction)) return true;
                if(reaction.Escape!=null) PlaceEscape(actor,reaction,seconds,interior.transform,false);
                else if(reaction.ReachedRefuge)
                    actor.Place(interior.transform.TransformPoint(new Vector3((float)reaction.X,(float)reaction.Y,(float)reaction.Z)),Vector3.forward,true);
                else
                {
                    var alarm=worldStore.ActiveAlarm(plan.Person.Id,seconds);
                    Vector3? danger=null;
                    if(alarm!=null && alarm.ThreatId!=null)
                    {
                        Vector3 target; var guards=GetComponent<GuardPopulation>();
                        danger=guards && guards.TryHostileThreatPosition(alarm.ThreatId,out target) ? target :
                            alarm.HasLocation && alarm.BuildingKey==interior.EntryDoor.buildingKey ?
                            interior.transform.TransformPoint(new Vector3((float)alarm.X,(float)alarm.Y,(float)alarm.Z)) : actor.transform.position;
                    }
                    CivilianResponses.Move(actor,reaction,plan.Routine,interior.transform,geometry,indoorGeometry,danger);
                }
            }
            else
            {
                if(plan.RoomFollower!=null)
                {
                    if(plan.RoomFollower.Arrived && !plan.RoomFollower.Leaving &&
                        seconds>=plan.Journey.CheckOutAt-plan.RoomFollower.Duration)
                        plan.RoomFollower.BeginReturn();
                    if(!plan.RoomFollower.Arrived && !plan.RoomFollower.Leaving && seconds>=plan.Journey.CheckOutAt &&
                        Time.unscaledTime>=plan.NextRoomExitRetryAt)
                    {
                        plan.NextRoomExitRetryAt=Time.unscaledTime+1;
                        var exitRoute=indoorGeometry.RouteToExit(actor.transform.position-Vector3.up);
                        if(exitRoute.Count>=2)
                        {
                            var exitFollower=new InteriorRoomFollower(exitRoute,plan.RoomSecondsPerMetre);
                            exitFollower.BeginExitFromCurrentPosition();
                            plan.RoomFollower=exitFollower;
                        }
                    }
                    if(!plan.RoomFollower.Arrived)
                    {
                        plan.RoomFollower.Advance(actor,interior,indoorGeometry,Time.unscaledTime);
                        return true;
                    }
                    if(plan.RoomFollower.Leaving)
                    {
                        if(indoorGeometry.EntryGround.HasValue)
                            actor.Place(indoorGeometry.EntryGround.Value,Vector3.forward,true);
                        return true;
                    }
                }
                else
                {
                    if(seconds>=plan.Journey.CheckOutAt) return false;
                    if(indoorGeometry.EntryGround.HasValue)
                        actor.Place(indoorGeometry.EntryGround.Value,Vector3.forward,true);
                    return true;
                }
                var art=SleepEnabled && state.Phase==TravelerPhase.Sleeping && sleepingArt!=null ? sleepingArt.ForResident(plan.Resident) : null;
                if(art!=null) actor.Sleep(interior.transform,plan.Bed,art);
                else actor.Place(interior.transform.TransformPoint(plan.LocalStanding),interior.transform.TransformDirection(plan.Bed.LocalHeadDirection),true);
            }
            return true;
        }
    }
}
