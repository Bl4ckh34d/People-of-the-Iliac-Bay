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
        private sealed class TravelerPlan
        {
            public TravelerPerson Person;
            public TravelerVisit Visit;
            public Resident Resident;
            public TravelerJourney Journey;
            public TravelerAdmission JourneyAdmission;
            public TravelerDiversion JourneyDiversion;
            public ResidentRoutine Routine;
            public Plan ActionPlan;
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
            public bool ObservedOutdoorDeparture;
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
                if(plan.JourneyAdmission!=plan.Visit.Admission || !ReferenceEquals(plan.JourneyDiversion,plan.Visit.Diversion))
                {
                    plan.Journey=worldStore.TravelerJourneyFor(plan.Visit.Id);
                    plan.JourneyAdmission=plan.Visit.Admission;
                    plan.JourneyDiversion=plan.Visit.Diversion;
                    plan.Routine.DestinationKey=plan.Visit.TavernKey;
                    plan.Routine.Route.Clear();
                    foreach(var point in plan.Visit.Route)plan.Routine.Route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
                    plan.RoomWalk=null;
                    plan.RoomFollower=null;
                    plan.RoomInterior=null;
                }
                ObserveTravelerArrivalBoundary(oldActor,plan,seconds);
                bool pendingArrival=IsPendingTravelerArrival(oldActor,plan,seconds);
                Vector3 departureEndpoint;
                bool pendingDeparture=PendingTravelerDeparture(oldActor,plan,seconds,out departureEndpoint);
                bool pendingRoomExit=Reaction(id,seconds)==null && interior && plan.RoomInterior==interior && plan.RoomFollower!=null &&
                    (plan.RoomFollower.Leaving && !plan.RoomFollower.Arrived || seconds>=plan.Journey.CheckOutAt &&
                    (!plan.RoomFollower.Arrived || !plan.RoomFollower.Leaving));
                if(plan.Person.IsAlive && ((seconds>=plan.Journey.EntryAt && seconds<plan.Journey.ExitAt) ||
                    Reaction(id,seconds)!=null || pendingArrival || pendingDeparture || pendingRoomExit ||
                    worldStore.TravelerAwaitingLodgingDecision(plan.Visit.Id,seconds) || TravelerActionOwnsVisit(id))) continue;
                if(oldActor) {oldActor.CaptureState();oldActor.gameObject.SetActive(false); Destroy(oldActor.gameObject);}
                actors.Remove(id); travelerPlans.Remove(id);
            }
            var currentVisits=new List<TravelerVisit>(travelerWindow);
            var required=new HashSet<string>();
            foreach(var job in worldStore.PendingNpcActions())
                if(TravelerActionRoutine.KeepsVisitAlive(true,job,false))required.Add(job.ResidentId);
            foreach(var task in worldStore.PendingNpcTasks())
                if(!NpcTaskContract.IsTerminal(task))required.Add(task.ResidentId);
            foreach(var visit in worldStore.TravelerVisitsFor(population.SettlementId))
                if(required.Contains(visit.PersonId) && !currentVisits.Exists(v=>v.PersonId==visit.PersonId))currentVisits.Add(visit);
            foreach(var visit in currentVisits)
            {
                TravelerPlan previous;
                travelerPlans.TryGetValue(visit.PersonId,out previous);
                Vector3 retainedExit;
                if(previous!=null && PendingTravelerDeparture(FindActor(visit.PersonId),previous,seconds,out retainedExit))continue;
                if(previous!=null && previous.JourneyAdmission==visit.Admission && (previous.Visit.Id==visit.Id ||
                    seconds>=previous.Journey.EntryAt && seconds<previous.Journey.ExitAt)) continue;
                var journey=worldStore.TravelerJourneyFor(visit.Id);
                if((seconds<journey.EntryAt || seconds>=journey.ExitAt) && !TravelerActionOwnsVisit(visit.PersonId) &&
                    !worldStore.TravelerAwaitingLodgingDecision(visit.Id,seconds)) continue;
                var person=worldStore.Traveler(visit.PersonId);
                if(!person.IsAlive) continue;
                var routine=new ResidentRoutine { ResidentId=person.Id,DestinationKey=visit.TavernKey };
                foreach(var point in visit.Route) routine.Route.Add(new SavedRoutePoint { X=point.X,Z=point.Z });
                if(previous!=null)
                {
                    ResidentActor priorActor;
                    if(actors.TryGetValue(person.Id,out priorActor) && priorActor)
                    { priorActor.CaptureState();priorActor.gameObject.SetActive(false); Destroy(priorActor.gameObject); }
                    actors.Remove(person.Id);
                }
                var resident=worldStore.TravelerOriginResident(person.Id) ?? new Resident { Id=person.Id,FullName=person.Name,Race=person.Race,Sex=person.Sex,
                    AppearanceSeed=person.AppearanceSeed,AgeGroup=ResidentAgeGroup.Adult,HomeKey=-1,IsAlive=person.IsAlive };
                travelerPlans[person.Id]=new TravelerPlan { Person=person,Visit=visit,Journey=journey,JourneyAdmission=visit.Admission,Routine=routine,
                    JourneyDiversion=visit.Diversion,Resident=resident,ActionPlan=new Plan {Resident=resident,Routine=routine} };
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
        private void UpdateTravelerActors(double seconds,bool catchingUp)
        {
            EnsureInnTravelerDemand(seconds);
            AdvanceTravelerLodgingFallback(seconds);
            RefreshTravelerPlans(seconds);
            foreach(var plan in travelerPlans.Values)
            {
                ResidentActor actor; actors.TryGetValue(plan.Person.Id,out actor);
                plan.Resident.IsAlive=plan.Person.IsAlive;
                var state=plan.Journey.Evaluate(seconds);
                // Rental preparation can admit a pending guest between the
                // half-second plan refreshes. Preserve its body before this
                // frame decides exterior visibility.
                if(catchingUp && state.Phase!=TravelerPhase.Arriving)
                {
                    plan.ObservedOutdoorArrival=false;
                    plan.PendingDoorwayArrival=false;
                }
                else ObserveTravelerArrivalBoundary(actor,plan,seconds);
                if(state.Phase==TravelerPhase.Arriving && actor && directory && actor.transform.parent==directory.transform)
                    plan.ObservedOutdoorArrival=true;
                var reaction=Reaction(plan.Person.Id,seconds);
                ObserveTravelerSleep(plan,actor,state,reaction,seconds);
                PrepareTravelerRoutineReturn(plan,seconds);
                if(ApplyNpcActionOverlay(plan.ActionPlan,ref actor,seconds,reaction) || TravelerActionOwnsVisit(plan.Person.Id))continue;
                if(PlaceTravelerInside(plan,actor,state,reaction,seconds)) continue;
                bool route=TravelerExteriorMaterialization.ShouldExist(plan.Visit.Admission,state.Phase,
                    plan.ObservedOutdoorArrival,catchingUp);
                Vector3 ground=geometry.ScenePoint(state.Position);
                bool continuingArrival=reaction==null && ContinueTravelerArrival(actor,plan,state,seconds,out ground);
                if(reaction==null && worldStore.TravelerAwaitingLodgingDecision(plan.Visit.Id,seconds))
                {
                    var door=plan.Visit.Route[plan.Visit.Route.Count-1];
                    ground=geometry.ScenePoint(new RoutePoint(door.X,door.Z));
                    continuingArrival=true;route=true;
                }
                route|=continuingArrival;
                Vector3 departureEndpoint=Vector3.zero;
                bool continuingDeparture=reaction==null && !continuingArrival &&
                    PendingTravelerDeparture(actor,plan,seconds,out departureEndpoint);
                if(continuingDeparture)
                {
                    ground=departureEndpoint;route=true;
                    var motion=actor.GetComponent<CrowdMotion>();if(motion)motion.ResumeFromPause();
                }
                if(reaction!=null && reaction.HasPosition)
                {
                    route=reaction.BuildingKey==-1 && !reaction.ReachedRefuge;
                    ground=directory.transform.TransformPoint(new Vector3((float)reaction.X,(float)reaction.Y,(float)reaction.Z));
                }
                Vector3 visibilityPoint=actor && (continuingArrival || continuingDeparture)?actor.transform.position-Vector3.up:ground;
                bool visible=!interior && !GameManager.Instance.IsPlayerInside && plan.Person.IsAlive && route &&
                    (visibilityPoint-GameManager.Instance.PlayerMotor.transform.position).sqrMagnitude<=(actor?165*165:140*140);
                if(!visible)
                {
                    if(actor) {actor.CaptureState(); actor.gameObject.SetActive(false); Destroy(actor.gameObject);}
                    actors.Remove(plan.Person.Id); continue;
                }
                if(!actor)
                {
                    if(actors.Count>=80) continue;
                    var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,directory.transform);
                    body.name="People of the Iliac Bay "+plan.Person.Id;
                    actor=body.AddComponent<ResidentActor>();
                    string id=plan.Person.Id;
                    actor.Initialize(plan.Resident,0,navigation,()=>worldStore.KillTraveler(id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()),worldStore);
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
                    Vector3 direction=continuingArrival || continuingDeparture ? ground-(actor.transform.position-Vector3.up) :
                        geometry.ScenePoint(next.Position)-ground;
                    RoutePoint endpoint;
                    Vector3? navigationDestination=plan.Journey.TryTravelEndpoint(
                        continuingArrival?TravelerPhase.Arriving:continuingDeparture?TravelerPhase.Leaving:state.Phase,out endpoint)
                        ? (Vector3?)geometry.ScenePoint(endpoint):null;
                    actor.Place(ground,direction,navigationDestination:navigationDestination);
                    if(!continuingArrival && state.Phase==TravelerPhase.Leaving)
                        plan.ObservedOutdoorDeparture=true;
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
            var phase=plan.Journey.Evaluate(seconds).Phase;
            if(phase==TravelerPhase.Arriving) plan.ObservedOutdoorArrival=true;
            if(plan.Visit.Admission!=TravelerAdmission.Admitted) return;
            // A real pending guest can already be waiting outside when room
            // preparation completes. Admission must preserve that body's door
            // approach even if its scheduled Arriving phase has already elapsed.
            if(seconds>=plan.Journey.CheckInAt && seconds<plan.Journey.CheckOutAt)
                plan.ObservedOutdoorArrival=true;
            if(phase!=TravelerPhase.Arriving && plan.ObservedOutdoorArrival && seconds>=plan.Journey.CheckInAt)
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
            if(pendingRoomExit)worldStore.ObserveTravelerDepartureDelay(plan.Visit.Id,seconds);
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
                            catch(ArgumentException) {Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_ROOM_ROUTE_MISSING: "+plan.Visit.Id);}
                        }
                        else Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_ROOM_ROUTE_MISSING: "+plan.Visit.Id);
                    }
                }
                if(plan.Bed==null || !plan.HasStanding) return false;
            }
            bool spawnedInside=false;
            if(!actor)
            {
                if(actors.Count>=80) return true;
                var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,interior.transform);
                body.name="People of the Iliac Bay "+plan.Person.Id;
                actor=body.AddComponent<ResidentActor>();
                string id=plan.Person.Id;
                actor.Initialize(plan.Resident,0,navigation,()=>worldStore.KillTraveler(id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()),worldStore);
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
                else actor.Place(interior.transform.TransformPoint(plan.LocalStanding),interior.transform.TransformDirection(plan.Bed.LocalHeadDirection),true,freeStanding:true);
            }
            return true;
        }
    }
}
