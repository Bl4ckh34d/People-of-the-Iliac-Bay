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
        private void WaitForRoutineRecovery(ResidentActor actor,ActionMotion motion)
        {
            motion.RecoveryWaiting=true;motion.RepathAt=Time.unscaledTime+30;
            motion.Indoor=null;motion.Outdoor=null;
            actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);
        }

        private bool SelectRoutineRecoveryOrigin(Plan plan,ResidentActor actor,NpcActionJob job,ActionMotion motion,
            double now,out string reason)
        {
            reason="No verified routine door is reachable here.";
            if(TryReleaseReturningAtOwnHome(plan,actor,job,now))return true;
            if(interior)
            {
                if(indoorGeometry==null || !indoorGeometry.EntryGround.HasValue) return false;
                int building=interior.EntryDoor.buildingKey;
                RoutineRecoveryPlan current;
                if(TryPlanRoutineRecovery(plan,actor,now,building,false,out current,out reason) &&
                    current.Legs[0].BuildingKey==building && !current.Legs[0].ReturnsHome &&
                    current.Legs[0].EnterAt<=now && now<current.Legs[0].LeaveAt)
                    return CompleteRoutineRecovery(plan,actor,job,now,building,false,out reason);
                SavedRoutePoint point;
                if(!TryRoutineRecoveryDoor(plan,building,out point) &&
                    (geometry==null || !geometry.Entrances.ContainsKey(building))) return false;
                motion.RecoveryOrigin=building;motion.Goal=indoorGeometry.EntryGround.Value;
            }
            else
            {
                if(geometry==null) return false;
                var keys=new HashSet<int>{plan.Resident.HomeKey};
                if(plan.Routine.Itinerary!=null)
                    foreach(var itinerary in ResidentRoutineDay.Itineraries(plan.Routine))
                    foreach(var stop in itinerary.Stops) keys.Add(stop.BuildingKey);
                else keys.Add(plan.Routine.DestinationKey);
                var family=RuntimeFamily(plan,now);
                if(family!=null && family.Source.JointInn!=null)keys.Add(family.Source.JointInn.InnKey);
                double nearest=double.PositiveInfinity;SavedRoutePoint point;
                var actual=directory.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                var start=new SavedRoutePoint {X=actual.x,Z=actual.z};
                foreach(int key in keys)
                {
                    if(!TryRoutineRecoveryDoor(plan,key,out point)) continue;
                    Vector3 target=geometry.ScenePoint(new RoutePoint(point.X,point.Z));
                    var route=geometry.RouteBetweenPoints(start,point);
                    if(route==null || route.Count<2)continue;
                    var end=geometry.ScenePoint(new RoutePoint(route[route.Count-1].X,route[route.Count-1].Z));
                    if(Vector3.Distance(end,target)>0.4f)continue;
                    double distance=RouteLength(route);
                    if(distance>=nearest)continue;
                    nearest=distance;motion.RecoveryOrigin=key;motion.Goal=target;
                }
                if(motion.RecoveryOrigin<0)return false;
            }
            motion.RecoverySelected=true;motion.Indoor=null;motion.Outdoor=null;motion.RepathAt=0;
            return true;
        }

        private bool TryReleaseReturningAtOwnHome(Plan plan,ResidentActor actor,NpcActionJob job,double now)
        {
            if(plan==null || !actor || job==null || !interior || indoorGeometry==null ||
                interior.EntryDoor.buildingKey!=plan.Resident.HomeKey ||
                Scheduled(plan,now).Activity!=Activity.Home && Scheduled(plan,now).Activity!=Activity.Sleep)
                return false;
            if(!IsResidentOfOwnHome(plan.Resident,plan.Resident.HomeKey))return false;
            var position=ActionPosition(actor.transform.position-Vector3.up);
            if(!worldStore.SaveNpcActionPosition(job.Id,job.Revision,position,false,now))return false;
            actionMotions.Remove(plan.Resident.Id);
            return true;
        }

        private bool BuildRoutineRecoveryJoin(ResidentActor actor,ActionMotion motion,out string reason)
        {
            reason="The physical return route is blocked.";
            if(!interior) return BuildActionRoute(actor,motion,motion.Goal,0.25f,out reason);
            var route=indoorGeometry.RouteToExit(actor.transform.position-Vector3.up);
            if(route==null || route.Count<2)return false;
            motion.Indoor=new InteriorRoomFollower(route,DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,0.2f);
            motion.Outdoor=null;motion.RepathAt=Time.unscaledTime+30;
            return true;
        }

        private bool TryPlanRoutineRecovery(Plan plan,ResidentActor actor,double now,int origin,bool outside,
            out RoutineRecoveryPlan recovery,out string reason)
        {
            bool restricted=population.SettlementId==1291010263 && worldStore.RestrictHauntingRoutines(population.SettlementId,
                GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now);
            int wake=restricted?Math.Max(wakeHour*60,300):wakeHour*60;
            int sleep=restricted?Math.Min(sleepHour*60,1140):sleepHour*60;
            // Appointment composition renumbers child visits. Keep canonical optional
            // stop identities here; bookings are independently inserted as fixed legs.
            DailySchedule ordinary=restricted?plan.HauntedSchedule:plan.Schedule;
            var previous=CurrentRoutineRecovery(plan,now);
            if(previous!=null) ordinary=DailySchedule.ContinueDay(ordinary,previous);
            return TryCreateRoutineRecoveryWithPoints(plan,ordinary,(int)Math.Floor(now/86400d),now,origin,now,wake,sleep,
                DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,out recovery,out reason,out _,outside);
        }

        private bool CompleteRoutineRecovery(Plan plan,ResidentActor actor,NpcActionJob job,double now,int origin,
            bool outside,out string reason)
        {
            RoutineRecoveryPlan recovery;
            if(!TryPlanRoutineRecovery(plan,actor,now,origin,outside,out recovery,out reason)) return false;
            var position=ActionPosition(actor.transform.position-Vector3.up);
            if(outside)
            {
                SavedRoutePoint door;
                if(!TryRoutineRecoveryDoor(plan,origin,out door)) {reason="The source has no saved exterior doorway.";return false;}
                position=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=population.SettlementId,
                    WorldSpaceId=ActionWorldSpace,BuildingKey=-1,X=door.X,Z=door.Z};
            }
            worldStore.SaveRoutineRecovery(recovery);
            if(!worldStore.SaveNpcActionPosition(job.Id,job.Revision,position,false,now))
            {reason="The saved action changed during routine recovery.";return false;}
            InvalidateRoutineRecovery(plan.Resident.Id);
            RemovePhysicalShopVisit(plan.Resident.Id);worldStore.RemoveObservedVisit(plan.Resident.Id);
            shopJourneys.Remove(plan.Resident.Id);shopDepartures.Remove(plan.Resident.Id);
            actionMotions.Remove(plan.Resident.Id);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_COMMITTED: resident="+plan.Resident.Id+"; origin="+origin+
                "; outside="+outside+"; skipped="+recovery.SkippedStopIndices.Count+"; legs="+recovery.Legs.Count+
                "; created="+now.ToString("G15",System.Globalization.CultureInfo.InvariantCulture));
            if(outside && interior) RemoveDestinationActor(plan.Resident.Id);
            else if(!outside && interior && shopGeometry!=null)
            {
                double start,end;var schedule=ActiveSchedule(plan);
                if(schedule.TryVisitWindowForBuilding(now,origin,out start,out end))
                {
                    var journey=shopGeometry.Create(plan.Resident.Id,plan.Resident.AppearanceSeed,Math.Max(now,start),end);
                    shopJourneys[plan.Resident.Id]=journey;
                    if(journey!=null) physicalShopVisits[plan.Resident.Id]=new PhysicalShopVisit(journey)
                        {ResumeUntil=schedule.ReturnHomeAt(now)};
                }
            }
            return true;
        }

        private bool TryRoutineRecoveryDoor(Plan plan,int building,out SavedRoutePoint door)
        {
            door=null;var routine=plan.Routine;
            if(routine==null)return false;
            var family=RuntimeFamily(plan,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            if(family!=null && family.Source.JointInn!=null)
            {
                var joint=family.Source.First.ResidentId==plan.Resident.Id?family.Source.JointInn.First:family.Source.JointInn.Second;
                var path=joint.Itinerary.Stops[0].Route;
                if(building==plan.Resident.HomeKey){door=path[0];return true;}
                if(building==family.Source.JointInn.InnKey){door=path[path.Count-1];return true;}
            }
            if(routine.Itinerary!=null)
            {
                foreach(var itinerary in ResidentRoutineDay.Itineraries(routine))
                foreach(var stop in itinerary.Stops)
                {
                    if(stop.Route==null || stop.Route.Count==0)continue;
                    if(building==plan.Resident.HomeKey) {door=stop.Route[0];return true;}
                    if(stop.BuildingKey==building) {door=stop.Route[stop.Route.Count-1];return true;}
                }
            }
            else if(routine.Route!=null && routine.Route.Count>0)
            {
                if(building==plan.Resident.HomeKey)door=routine.Route[0];
                else if(building==routine.DestinationKey)door=routine.Route[routine.Route.Count-1];
            }
            if(door==null && building==plan.Resident.HomeKey && IsHomeOnlyWithoutVerifiedRoute(plan) &&
                geometry!=null && directory!=null && population!=null && population.SettlementId==directory.MapID)
            {
                TownGeometry.Entrance entrance;
                if(geometry.Entrances.TryGetValue(building,out entrance) && entrance!=null &&
                    entrance.BuildingKey==building)
                {
                    Vector3 local=entrance.LocalDoor;
                    door=new SavedRoutePoint {X=local.x,Z=local.z};
                }
            }
            return door!=null;
        }

        private bool IsResidentOfOwnHome(Resident resident,int homeKey)
        {
            if(resident==null || population==null || directory==null || population.SettlementId!=directory.MapID ||
                resident.HomeKey!=homeKey || population.Households==null)return false;
            foreach(var household in population.Households)
                if(household!=null && household.BuildingKey==homeKey && household.Residents!=null &&
                    household.Residents.Exists(item=>item!=null && item.Id==resident.Id))return true;
            return false;
        }

        private bool IsHomeOnlyWithoutVerifiedRoute(Plan plan)
        {
            if(plan==null || plan.Routine==null || plan.Resident==null || plan.Routine.Itinerary!=null ||
                plan.Routine.DestinationKey>=0 || plan.Routine.Route!=null && plan.Routine.Route.Count>0)return false;
            return true;
        }

        private bool HasFutureHomeOnlyCommitment(Plan plan,double now)
        {
            if(plan==null)return true;
            var schedule=ActiveSchedule(plan);
            double local=now-Math.Floor(now/86400d)*86400d;
            if(schedule!=null)
                foreach(var visit in schedule.ScheduledVisits(now))
                    if(visit.LeaveAt>local)return true;
            var bookings=worldStore.AppointmentsFor(plan.Resident.Id);
            if(bookings!=null)
                foreach(var booking in bookings)
                    if(booking!=null && !booking.OutcomeRecorded && booking.Status!="cancelled" &&
                        AppointmentTiming.ReservedUntil(booking)>now)return true;
            return false;
        }

        private bool CanReleaseHomeOnlyAtDoor(Plan plan,double now,out string reason)
        {
            reason="No verified home-only recovery door is available.";
            if(!IsHomeOnlyWithoutVerifiedRoute(plan) || geometry==null || population==null || directory==null ||
                !IsResidentOfOwnHome(plan.Resident,plan.Resident.HomeKey))return false;
            Activity activity=Scheduled(plan,now).Activity;
            return NpcHomeActionPolicy.CanReleaseHomeOnlyRecovery(activity,true,
                HasFutureHomeOnlyCommitment(plan,now),out reason);
        }

        private bool TryReleaseHomeOnlyAtDoor(Plan plan,NpcActionJob job,double now,int origin,
            Vector3 actual,out string reason)
        {
            reason="The resident has not reached the surveyed home doorway.";
            if(job==null || !IsVerifiedOwnHomeDoor(plan,origin,actual))return false;
            if(!CanReleaseHomeOnlyAtDoor(plan,now,out reason))return false;
            TownGeometry.Entrance entrance;geometry.Entrances.TryGetValue(origin,out entrance);
            Vector3 door=geometry.SceneDoor(entrance);
            var position=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=population.SettlementId,
                WorldSpaceId=ActionWorldSpace,BuildingKey=-1,X=door.x,Y=door.y,Z=door.z};
            if(!worldStore.SaveNpcActionPosition(job.Id,job.Revision,position,false,now))
            {reason="The saved action changed during home-only recovery.";return false;}
            actionMotions.Remove(plan.Resident.Id);
            return true;
        }

        private bool IsVerifiedOwnHomeDoor(Plan plan,int origin,Vector3 actual)
        {
            if(plan==null || plan.Resident==null || origin!=plan.Resident.HomeKey || geometry==null || population==null ||
                directory==null || population.SettlementId!=directory.MapID || !IsResidentOfOwnHome(plan.Resident,origin))return false;
            TownGeometry.Entrance entrance;
            if(!geometry.Entrances.TryGetValue(origin,out entrance) || entrance==null || entrance.BuildingKey!=origin)return false;
            Vector3 door=geometry.SceneDoor(entrance);
            return Vector3.Distance(actual,door)<=0.35f;
        }
    }
}
