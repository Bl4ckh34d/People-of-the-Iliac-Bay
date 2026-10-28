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
        // This guard covers stationary resolved endpoints only. Person targets can move
        // during an action, so their acceptance-time position is not a safe forecast.
        private bool ActionReturnFitsFixedCommitments(Plan plan,ResidentActor actor,NpcActionJob job,
            Vector3 target,float range,double now,out string reason)
        {
            reason="";
            if(IsTravelerActionPlan(plan))return true;
            if(job.Kind==NpcActionKind.ChangeSchedule || job.Target==null || job.Target.Kind==NpcActionTargetKind.Person)
                return true;

            int origin;double returnSeconds;
            if(interior)
            {
                if(indoorGeometry==null || !indoorGeometry.EntryGround.HasValue) return true;
                var route=indoorGeometry.RouteToExit(target);
                if(route==null || route.Count<2) return true;
                double length=0;
                for(int i=1;i<route.Count;i++)
                {
                    double x=route[i].X-route[i-1].X,y=route[i].Y-route[i-1].Y,z=route[i].Z-route[i-1].Z;
                    length+=Math.Sqrt(x*x+y*y+z*z);
                }
                origin=interior.EntryDoor.buildingKey;
                returnSeconds=ConservativeReturnSeconds(length,range,actor);
            }
            else if(!TryMeasureOutdoorActionReturn(plan,target,range,actor,out origin,out returnSeconds))
                return true;

            int day=(int)Math.Floor(now/86400d);
            bool restricted=population.SettlementId==1291010263 && worldStore.RestrictHauntingRoutines(
                population.SettlementId,GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now);
            int wake=restricted?Math.Max(wakeHour*60,300):wakeHour*60;
            int sleep=restricted?Math.Min(sleepHour*60,1140):sleepHour*60;
            DailySchedule ordinary=restricted?plan.HauntedSchedule:plan.Schedule;
            var previous=CurrentRoutineRecovery(plan,now);
            if(previous!=null) ordinary=DailySchedule.ContinueDay(ordinary,previous);
            RoutineRecoveryPlan recovery;
            if(RoutineRecoveryAcceptance.TryCreateAfterAction(population.SettlementId,plan.Resident,plan.Routine,ordinary,
                worldStore.AppointmentsFor(plan.Resident.Id),day,job.DeadlineAt,returnSeconds,origin,wake,sleep,
                DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,out recovery,out reason,out var failure,true)) return true;

            if(failure==RoutineRecoveryFailure.RequiredAppointmentUnreachable ||
                failure==RoutineRecoveryFailure.HomeReturnUnavailable)
            {
                reason="The measured return route cannot reach a fixed appointment or the resident's home.";
                return false;
            }
            // Missing route evidence or invalid imported routine data cannot prove
            // that a fixed commitment is unreachable. Keep the action available.
            reason="";return true;
        }

        private bool TryMeasureOutdoorActionReturn(Plan plan,Vector3 target,float range,ResidentActor actor,
            out int origin,out double seconds)
        {
            origin=-1;seconds=0;
            if(geometry==null || !directory) return false;
            var keys=new HashSet<int>{plan.Resident.HomeKey};
            if(plan.Routine.Itinerary!=null)
                foreach(var itinerary in ResidentRoutineDay.Itineraries(plan.Routine))
                    foreach(var stop in itinerary.Stops) keys.Add(stop.BuildingKey);
            else keys.Add(plan.Routine.DestinationKey);

            double nearest=double.PositiveInfinity;
            var from=directory.transform.InverseTransformPoint(target);
            var start=new SavedRoutePoint {X=from.x,Z=from.z};
            foreach(int key in keys)
            {
                SavedRoutePoint door;
                if(!TryRoutineRecoveryDoor(plan,key,out door))continue;
                var route=geometry.RouteBetweenPoints(start,door);
                if(route==null || route.Count<2)continue;
                var end=geometry.ScenePoint(new RoutePoint(route[route.Count-1].X,route[route.Count-1].Z));
                var expected=geometry.ScenePoint(new RoutePoint(door.X,door.Z));
                if(Vector3.Distance(end,expected)>0.4f)continue;
                double length=RouteLength(route);
                if(length>=nearest)continue;
                nearest=length;origin=key;
            }
            if(origin<0)return false;
            seconds=ConservativeReturnSeconds(nearest,range,actor);
            return seconds>0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds);
        }

        private static double ConservativeReturnSeconds(double length,float range,ResidentActor actor)
        {
            double rate=DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed;
            double walking=(length+Math.Max(0,range)+1)*rate;
            return walking+Math.Max(30,walking*0.25);
        }
    }
}
