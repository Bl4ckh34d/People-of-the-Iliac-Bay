using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Advances room travel only when rendered body reaches each surveyed point.
    public sealed class InteriorRoomFollower
    {
        private List<GuardPoint3> route;
        private int next,trackedWaypoint=-1;
        private float lastTargetDistance;
        private float stalledAt,nextDetourAt;
        private double nextStallReport;
        private bool detourProbeLogged;
        private readonly float arrivalRadius;

        public bool Arrived { get; private set; }
        public bool Leaving { get; private set; }
        public double Duration { get; private set; }
        public List<GuardPoint3> RemainingRoute(Vector3 actualLocal)
        {
            var remaining=new List<GuardPoint3> {new GuardPoint3 {X=actualLocal.x,Y=actualLocal.y,Z=actualLocal.z}};
            if(!Arrived) for(int i=next;i<route.Count;i++)
                remaining.Add(new GuardPoint3 {X=route[i].X,Y=route[i].Y,Z=route[i].Z});
            return remaining;
        }

        public InteriorRoomFollower(IList<GuardPoint3> points,double secondsPerMetre,float arrivalRadius=0.15f)
        {
            if(points==null || points.Count<2 || double.IsNaN(secondsPerMetre) ||
                double.IsInfinity(secondsPerMetre) || secondsPerMetre<=0 || float.IsNaN(arrivalRadius) || arrivalRadius<0.05f || arrivalRadius>0.65f)
                throw new ArgumentException("Invalid physical room route.");
            this.arrivalRadius=arrivalRadius;

            route=new List<GuardPoint3>(points.Count);
            double length=0;
            foreach(var point in points)
            {
                if(point==null || double.IsNaN(point.X) || double.IsInfinity(point.X) ||
                    double.IsNaN(point.Y) || double.IsInfinity(point.Y) ||
                    double.IsNaN(point.Z) || double.IsInfinity(point.Z))
                    throw new ArgumentException("Invalid physical room waypoint.");
                var copy=new GuardPoint3 { X=point.X,Y=point.Y,Z=point.Z };
                if(route.Count>0)
                {
                    var prior=route[route.Count-1];
                    double x=copy.X-prior.X,y=copy.Y-prior.Y,z=copy.Z-prior.Z;
                    length+=Math.Sqrt(x*x+y*y+z*z);
                }
                route.Add(copy);
            }
            Duration=length*secondsPerMetre;
            if(length<=0 || double.IsNaN(Duration) || double.IsInfinity(Duration))
                throw new ArgumentException("Invalid physical room route length.");
            next=1;
        }

        public void RestoreAtDestination()
        {
            Arrived=true;
            next=route.Count-1;
            trackedWaypoint=-1;
            stalledAt=0;
        }

        public void BeginReturn()
        {
            if(!Arrived || Leaving) return;
            route.Reverse();
            BeginExitFromCurrentPosition();
        }

        public void BeginExitFromCurrentPosition()
        {
            next=1;
            Arrived=false;
            Leaving=true;
            trackedWaypoint=-1;
            stalledAt=0;
            nextDetourAt=0;
        }

        public bool Advance(ResidentActor actor,DaggerfallInterior interior,InteriorGeometry geometry,double unscaledTime)
        {
            if(Arrived || !actor || !interior || geometry==null) return Arrived;

            Vector3 actual=actor.transform.position-Vector3.up;
            while(next<route.Count)
            {
                Vector3 waypoint=WorldPoint(interior,route[next]);
                float reach=next==route.Count-1 ? arrivalRadius : 0.15f;
                if(Vector3.Distance(actor.transform.position,waypoint+Vector3.up)>=reach) break;
                next++;
            }
            if(next>=route.Count)
            {
                Vector3 final=WorldPoint(interior,route[route.Count-1]);
                if(Vector3.Distance(actor.transform.position,final+Vector3.up)<arrivalRadius)
                {
                    Arrived=true;
                    return true;
                }
                next=route.Count-1;
            }
            Vector3 currentWaypoint=WorldPoint(interior,route[next]);
            float targetDistance=Vector3.Distance(actor.transform.position,currentWaypoint+Vector3.up);
            if(trackedWaypoint!=next)
            {
                trackedWaypoint=next;
                lastTargetDistance=targetDistance;
                stalledAt=(float)unscaledTime;
            }
            else if(lastTargetDistance-targetDistance>=0.12f)
            {
                lastTargetDistance=targetDistance;
                stalledAt=0;
            }
            else if(stalledAt<=0) stalledAt=(float)unscaledTime;
            if(stalledAt>0 && unscaledTime-stalledAt>=0.75 && unscaledTime>=nextStallReport)
            {
                nextStallReport=unscaledTime+10;
                var nearby=StreetPopulation.ShopDepartureNeighbours(actual,actor);
                string people="";foreach(var p in nearby) people+=p.ToString("F3")+" ";
                string remaining="";for(int i=next;i<route.Count;i++) remaining+=WorldPoint(interior,route[i]).ToString("F3")+" ";
                string blockers="";
                foreach(var hit in Physics.OverlapCapsule(currentWaypoint+Vector3.up*(0.51f+CrowdMotion.BodyRadius),
                    currentWaypoint+Vector3.up*1.65f,CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Ignore))
                    if(hit && !hit.transform.IsChildOf(actor.transform)) blockers+=hit.name+" ";
                Debug.LogWarning("LIVING_CITIES_ROOM_ROUTE_STALL: resident="+actor.Resident.Id+"; next="+next+"/"+route.Count+
                    "; body="+actual.ToString("F3")+"; target="+currentWaypoint.ToString("F3")+"; distance="+targetDistance+
                    "; neighbours="+people+"; targetColliders="+blockers+"; remaining="+remaining);
            }
            if(targetDistance>=0.15f && stalledAt>0 && unscaledTime-stalledAt>=0.8 && unscaledTime>=nextDetourAt)
                TryDetour(actual,actor,interior,geometry,unscaledTime);

            var point=WorldPoint(interior,route[next]);
            var after=WorldPoint(interior,route[Math.Min(next+1,route.Count-1)]);
            InteriorGeometry.OpenOrdinaryDoors(actual,point);
            Vector3 direction=after-point;
            if(direction.sqrMagnitude<0.0001f) direction=point-actual;
            var motion=actor.GetComponent<CrowdMotion>();
            if(motion) motion.ResumeFromPause();
            actor.Place(point,direction);
            return Arrived;
        }

        private void TryDetour(Vector3 actual,ResidentActor actor,DaggerfallInterior interior,
            InteriorGeometry geometry,double now)
        {
            nextDetourAt=(float)now+1.5f;
            bool finalWaypoint=next+1>=route.Count;
            var rejoin=new List<GuardPoint3>();
            if(finalWaypoint)
            {
                GuardPoint3 target=route[route.Count-1];
                rejoin.Add(target);
                var nearby=arrivalRadius>0.15f ? new List<Vector3>() :
                    geometry.NearbyApproachPoints(new Vector3((float)target.X,(float)target.Y,(float)target.Z),0.2f,0.15f,9);
                int added=0;
                foreach(var local in nearby)
                {
                    if(added>=8) break;
                    if((local-new Vector3((float)target.X,(float)target.Y,(float)target.Z)).sqrMagnitude<0.0001f) continue;
                    rejoin.Add(new GuardPoint3 { X=local.x,Y=local.y,Z=local.z });
                    added++;
                }
            }
            else for(int i=next+1;i<route.Count;i++) rejoin.Add(route[i]);
            var crowd=StreetPopulation.ShopDepartureNeighbours(actual,actor);
            int rejoinIndex;
            var detour=geometry.FindLocalDetour(actual,crowd,CrowdMotion.Separation,rejoin,out rejoinIndex,160,finalWaypoint?0.01f:0.65f);
            if(detour.Count<2 && !detourProbeLogged && Array.IndexOf(Environment.GetCommandLineArgs(),"-livingcities-tavern-survey-probe")>=0)
            {
                detourProbeLogged=true;
                int expandedIndex;
                var expanded=geometry.FindLocalDetour(actual,crowd,CrowdMotion.Separation,rejoin,out expandedIndex,2000,finalWaypoint?0.01f:0.65f);
                Debug.Log("LIVING_CITIES_DETOUR_BUDGET_PROBE: bounded="+detour.Count+"; expanded="+expanded.Count+"; goal="+expandedIndex);
            }
            if(detour.Count<2 || rejoinIndex<0 || rejoinIndex>=rejoin.Count) return;
            if(finalWaypoint)
            {
                var intended=WorldPoint(interior,route[route.Count-1]);
                var chosen=WorldPoint(interior,rejoin[rejoinIndex]);
                if(Vector3.Distance(intended,chosen)>0.25f) return;
            }
            else for(int i=rejoinIndex+1;i<rejoin.Count;i++) detour.Add(rejoin[i]);
            route=detour;
            next=1;
            trackedWaypoint=-1;
            stalledAt=(float)now;
        }

        private static Vector3 WorldPoint(DaggerfallInterior interior,GuardPoint3 point)
        {
            return interior.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
        }
    }
}
