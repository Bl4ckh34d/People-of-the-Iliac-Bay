using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class ResidentRoomPlan
        {
            public DaggerfallInterior Interior;
            public double EnterAt,AttemptedAt,LeaveAt;
            public double ReceptionStartedAt,ReceptionArrivedAt,ReceptionServiceEndsAt,ReceptionServiceSeconds;
            public BedAnchor Bed;
            public Vector3 Standing;
            public TravelerRoomWalk Walk;
            public ResidentInnJourney Reception;
            public InteriorRoomFollower ReceptionFollower,RoomFollower,ExitFollower;
            public Vector3 Counter;
            public double SecondsPerMetre;
            public float NextExitRetryAt;
            public bool ExitStarted,ReceptionRouteUnavailable,ReceptionServiceLatched;
        }
        private readonly Dictionary<string,ResidentRoomPlan> residentRooms=new Dictionary<string,ResidentRoomPlan>();
        private bool ResidentInnSleeping(string id,double seconds)
        {
            ResidentRoomPlan plan; Resident resident;
            if(!residentRooms.TryGetValue(id,out plan) || plan.Walk==null || plan.RoomFollower==null ||
                !plan.RoomFollower.Arrived || plan.ExitStarted ||
                !destinationResidents.TryGetValue(id,out resident)) return false;
            var visit=worldStore.ResidentTripAt(id,seconds);
            if(visit.VisitPurpose!=ResidentVisitPurpose.Inn || visit.VisitPhase!=ResidentVisitPhase.Inside || visit.VisitEnterAt!=plan.EnterAt) return false;
            double hour=(seconds/3600+((int)(resident.AppearanceSeed%61)-30)/60.0)%24;
            return sleepHour>wakeHour ? hour>=sleepHour || hour<wakeHour : hour>=sleepHour && hour<wakeHour;
        }
        private bool PlaceResidentInn(ResidentActor actor,double seconds)
        {
            string id=actor.Resident.Id;
            var visit=worldStore.ResidentTripAt(id,seconds);
            if(visit.VisitPhase!=ResidentVisitPhase.Inside || visit.VisitPurpose!=ResidentVisitPurpose.Inn ||
                visit.BuildingKey!=interior.EntryDoor.buildingKey) return false;
            ResidentRoomPlan plan;
            if(!residentRooms.TryGetValue(id,out plan) || plan.Interior!=interior || plan.EnterAt!=visit.VisitEnterAt)
            {
                var stay=worldStore.ResidentInnStayAt(id,seconds);
                if(stay==null || stay.BuildingKey!=interior.EntryDoor.buildingKey) return false;
                plan=new ResidentRoomPlan {Interior=interior,EnterAt=visit.VisitEnterAt,AttemptedAt=Math.Max(visit.VisitEnterAt,stay.AttemptedAt),
                    LeaveAt=visit.VisitLeaveAt}; residentRooms[id]=plan;
                string bedId=worldStore.ResidentInnBed(id,seconds);
                plan.Bed=BedGeometry.Read(interior).Find(b=>"bed:"+b.RecordIndex==bedId);
                Vector3 standing;
                if(plan.Bed!=null && indoorGeometry.StandBesideBed(plan.Bed,out standing,true))
                {
                    plan.Standing=interior.transform.InverseTransformPoint(standing);
                    var exitRoute=indoorGeometry.RouteToExit(standing);
                    Vector3 currentLocal=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                    var roomRoute=indoorGeometry.RouteBetween(currentLocal,plan.Standing);
                    try
                    {
                        double rate=DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed;
                        plan.SecondsPerMetre=rate;
                        var scheduleRoute=new List<GuardPoint3>(exitRoute); scheduleRoute.Reverse();
                        plan.Walk=TravelerRoomWalk.Create(scheduleRoute,plan.AttemptedAt,plan.LeaveAt,rate);
                        if(roomRoute.Count>=2) plan.RoomFollower=new InteriorRoomFollower(roomRoute,rate);
                        if(exitRoute.Count>=2) plan.ExitFollower=new InteriorRoomFollower(exitRoute,rate);
                    }
                    catch(ArgumentException) {Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_RESIDENT_ROOM_ROUTE_MISSING: "+id);}
                    var reception=worldStore.ResidentInnReceptionAt(id,seconds);
                    if(reception!=null && plan.RoomFollower!=null && exitRoute.Count>=2)
                    {
                        var counter=reception.Route[reception.Route.Count-1];
                        plan.Counter=interior.transform.TransformPoint(new Vector3((float)counter.X,(float)counter.Y,(float)counter.Z));
                        var receptionStart=reception.Route[0];
                        var receptionPrefix=indoorGeometry.RouteBetween(currentLocal,
                            new Vector3((float)receptionStart.X,(float)receptionStart.Y,(float)receptionStart.Z));
                        var physicalReceptionRoute=new List<GuardPoint3>();
                        if(receptionPrefix.Count>=2)
                        {
                            physicalReceptionRoute.AddRange(receptionPrefix);
                            for(int i=1;i<reception.Route.Count;i++) physicalReceptionRoute.Add(reception.Route[i]);
                        }
                        var scheduledRoomRoute=indoorGeometry.RouteBetween(
                            new Vector3((float)counter.X,(float)counter.Y,(float)counter.Z),plan.Standing);
                        double serviceSeconds=Math.Max(reception.ServiceSeconds,stay.AttemptedAt-reception.CounterAt);
                        try
                        {
                            if(physicalReceptionRoute.Count>=2)
                            {
                                plan.Reception=ResidentInnJourney.Create(reception.Route,scheduledRoomRoute,exitRoute,reception.StartedAt,plan.LeaveAt,
                                    reception.SecondsPerMetre,serviceSeconds);
                                plan.ReceptionStartedAt=reception.StartedAt;
                                plan.ReceptionFollower=new InteriorRoomFollower(physicalReceptionRoute,reception.SecondsPerMetre);
                                plan.ReceptionServiceEndsAt=plan.Reception.ServiceEndsAt;
                                plan.ReceptionServiceSeconds=serviceSeconds;
                                plan.RoomFollower=new InteriorRoomFollower(scheduledRoomRoute,reception.SecondsPerMetre);
                                plan.ExitFollower=new InteriorRoomFollower(exitRoute,reception.SecondsPerMetre);
                            }
                            else
                            {
                                plan.RoomFollower=null;
                                plan.ReceptionRouteUnavailable=true;
                            }
                        }
                        catch(ArgumentException)
                        {
                            plan.ReceptionRouteUnavailable=true;
                            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_RESIDENT_RECEPTION_ROOM_ROUTE_MISSING: "+id);
                        }
                    }
                }
            }
            if(plan.Walk==null || seconds<plan.AttemptedAt || seconds>=plan.LeaveAt) return false;
            if(plan.ReceptionRouteUnavailable) return true;
            if(plan.Reception!=null)
            {
                if(seconds<plan.ReceptionStartedAt) return true;
                if(plan.ReceptionFollower==null) return true;
                if(!plan.ReceptionFollower.Arrived)
                {
                    plan.ReceptionFollower.Advance(actor,interior,indoorGeometry,Time.unscaledTime);
                }
                if(!plan.ReceptionFollower.Arrived) return true;
                if(!plan.ReceptionServiceLatched)
                {
                    plan.ReceptionServiceLatched=true;
                    plan.ReceptionArrivedAt=seconds;
                    plan.ReceptionServiceEndsAt=Math.Max(plan.Reception.ServiceEndsAt,seconds+plan.ReceptionServiceSeconds);
                    actor.Place(actor.transform.position-Vector3.up,plan.Counter-actor.transform.position,true);
                }
                if(seconds<plan.ReceptionServiceEndsAt) return true;
                if(plan.RoomFollower==null)
                    return true;
                if(!plan.RoomFollower.Arrived)
                {
                    plan.RoomFollower.Advance(actor,interior,indoorGeometry,Time.unscaledTime);
                    return true;
                }
            }
            if(plan.RoomFollower==null || plan.ExitFollower==null) return false;
            if(!plan.RoomFollower.Arrived)
            {
                plan.RoomFollower.Advance(actor,interior,indoorGeometry,Time.unscaledTime);
                return true;
            }
            double exitAt=plan.Reception!=null ? plan.Reception.ExitAt : plan.LeaveAt-plan.ExitFollower.Duration;
            if(!plan.ExitStarted && seconds>=exitAt) plan.ExitStarted=true;
            if(plan.ExitStarted && !plan.ExitFollower.Arrived)
            {
                plan.ExitFollower.Advance(actor,interior,indoorGeometry,Time.unscaledTime);
                return true;
            }
            if(plan.ExitStarted)
            {
                if(indoorGeometry.EntryGround.HasValue)
                    actor.Place(indoorGeometry.EntryGround.Value,Vector3.forward,true);
                return true;
            }
            var art=SleepEnabled && ResidentInnSleeping(id,seconds) && sleepingArt!=null ? sleepingArt.ForResident(actor.Resident) : null;
            if(art!=null) actor.Sleep(interior.transform,plan.Bed,art);
            else actor.Place(interior.transform.TransformPoint(plan.Standing),interior.transform.TransformDirection(plan.Bed.LocalHeadDirection),true,freeStanding:true);
            return true;
        }
        private bool ResidentInnExitPending(ResidentActor actor,double seconds)
        {
            ResidentRoomPlan plan;
            if(!actor || !actor.Resident.IsAlive || !interior || Reaction(actor.Resident.Id,seconds)!=null ||
                !residentRooms.TryGetValue(actor.Resident.Id,out plan) || plan.Interior!=interior || plan.ExitFollower==null ||
                plan.ExitFollower.Arrived) return false;
            return plan.ExitStarted || seconds>=plan.LeaveAt && !plan.ExitFollower.Arrived;
        }
        private bool ContinueResidentInnExit(ResidentActor actor,double seconds)
        {
            if(!ResidentInnExitPending(actor,seconds)) return false;
            ResidentRoomPlan plan=residentRooms[actor.Resident.Id];
            if(!plan.ExitStarted)
            {
                if(Time.unscaledTime<plan.NextExitRetryAt) return true;
                plan.NextExitRetryAt=Time.unscaledTime+1;
                var route=indoorGeometry.RouteToExit(actor.transform.position-Vector3.up);
                if(route.Count>=2)
                {
                    plan.ExitFollower=new InteriorRoomFollower(route,plan.SecondsPerMetre);
                    plan.ExitStarted=true;
                }
            }
            if(!plan.ExitStarted) return true;
            plan.ExitFollower.Advance(actor,interior,indoorGeometry,Time.unscaledTime);
            return true;
        }
    }
}
