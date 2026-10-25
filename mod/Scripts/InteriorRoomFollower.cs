using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Advances room travel only when rendered body reaches each surveyed point.
    public sealed class InteriorRoomFollower
    {
        private List<GuardPoint3> route;
        private int next,trackedWaypoint=-1,retreatWaypoint=-1,preciseWaypoint=-1;
        private float lastTargetDistance,lastNativeDistanceWalked;
        private float stalledAt,nextDetourAt;
        private double nextStallReport;
        private bool detourProbeLogged;
        private readonly float arrivalRadius;
        private readonly SelectedSeatAccess seatAccess;

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

        public InteriorRoomFollower(IList<GuardPoint3> points,double secondsPerMetre,float arrivalRadius=0.15f,SelectedSeatAccess seatAccess=null)
        {
            if(points==null || points.Count<2 || double.IsNaN(secondsPerMetre) ||
                double.IsInfinity(secondsPerMetre) || secondsPerMetre<=0 || float.IsNaN(arrivalRadius) || arrivalRadius<0.05f || arrivalRadius>0.65f)
                throw new ArgumentException("Invalid physical room route.");
            this.arrivalRadius=arrivalRadius;
            this.seatAccess=seatAccess;

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
            retreatWaypoint=-1;
            next=route.Count-1;
            trackedWaypoint=-1;
            preciseWaypoint=-1;
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
            retreatWaypoint=-1;
            Arrived=false;
            Leaving=true;
            trackedWaypoint=-1;
            preciseWaypoint=-1;
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
            if(retreatWaypoint>=0 && next>retreatWaypoint)retreatWaypoint=-1;
            Vector3 currentWaypoint=WorldPoint(interior,route[next]);
            float targetDistance=Vector3.Distance(actor.transform.position,currentWaypoint+Vector3.up);
            var native=actor.GetComponent<NativeResidentNavigation>();
            if(trackedWaypoint!=next)
            {
                trackedWaypoint=next;
                preciseWaypoint=-1;
                lastTargetDistance=targetDistance;
                lastNativeDistanceWalked=native?native.DistanceWalked:0;
                stalledAt=(float)unscaledTime;
            }
            else if(lastTargetDistance-targetDistance>=0.12f ||
                native && native.Active && native.DistanceWalked-lastNativeDistanceWalked>=0.12f)
            {
                lastTargetDistance=targetDistance;
                lastNativeDistanceWalked=native?native.DistanceWalked:0;
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
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_ROOM_ROUTE_STALL: resident="+actor.Resident.Id+"; next="+next+"/"+route.Count+
                    "; body="+actual.ToString("F3")+"; target="+currentWaypoint.ToString("F3")+"; distance="+targetDistance+
                    "; neighbours="+people+"; targetColliders="+blockers+"; remaining="+remaining);
            }
            if(targetDistance>=0.15f && stalledAt>0 && unscaledTime-stalledAt>=0.8 && unscaledTime>=nextDetourAt && !(seatAccess!=null&&next==route.Count-1))
                TryDetour(actual,actor,interior,geometry,unscaledTime);

            var point=WorldPoint(interior,route[next]);
            var after=WorldPoint(interior,route[Math.Min(next+1,route.Count-1)]);
            InteriorGeometry.OpenOrdinaryDoors(actual,point);
            Vector3 direction=next==retreatWaypoint ? point-actual : after-point;
            if(direction.sqrMagnitude<0.0001f) direction=point-actual;
            var motion=actor.GetComponent<CrowdMotion>();
            if(motion) motion.ResumeFromPause();
            // Transit waypoints are guidance, not reserved activity places.
            // Waiting for an occupied intermediate point freezes the actor even
            // metres away instead of allowing normal crowd avoidance/replanning.
            // A reserved chair can legitimately be nearer than the street
            // comfort radius to another chair. Keep hard body separation and
            // exact furniture permission, without circling its seated neighbour.
            bool selectedSeatEndpoint=seatAccess!=null&&next==route.Count-1;
            if(selectedSeatEndpoint || Vector3.Distance(actor.transform.position,point+Vector3.up)<=0.55f)
                preciseWaypoint=next;
            // Native pathing reaches the surveyed point's neighbourhood. The
            // existing physical follower completes its last precise step and
            // owns every reserved seat approach.
            var world=NativeNavigationWorld.Current;
            bool useNative=preciseWaypoint!=next && world && world.Ready &&
                GameManager.Instance.PlayerGPS.HasCurrentLocation &&
                world.Scene=="interior-"+GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId+"-"+
                    interior.EntryDoor.buildingKey;
            actor.Place(point,direction,waitAtOccupiedTarget:next==route.Count-1,seatAccess:selectedSeatEndpoint?seatAccess:null,
                comfortWalking:!selectedSeatEndpoint,navigationDestination:useNative?point:(Vector3?)null,
                preferNative:useNative);
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
            if(detour.Count<2 && !detourProbeLogged &&
                (Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-tavern-survey-probe")>=0 ||
                 Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-physical-daily-routine")>=0))
            {
                detourProbeLogged=true;
                int expandedIndex;
                var expanded=geometry.FindLocalDetour(actual,crowd,CrowdMotion.Separation,rejoin,out expandedIndex,2000,finalWaypoint?0.01f:0.65f);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DETOUR_BUDGET_PROBE: bounded="+detour.Count+"; expanded="+expanded.Count+"; goal="+expandedIndex);
            }
            if(detour.Count<2 || rejoinIndex<0 || rejoinIndex>=rejoin.Count)
            {
                // A crowd can pin the body where forward/side steering has no
                // clear step. Finish a short retreat before retrying the same
                // route, rather than alternating backward/forward each frame.
                if(next==retreatWaypoint)return;
                var motion=actor.GetComponent<CrowdMotion>();
                var away=actual-WorldPoint(interior,route[next]);away.y=0;
                if(!motion || away.sqrMagnitude<0.0001f)return;
                away.Normalize();
                var target=interior.transform.InverseTransformPoint(actual+away*0.65f);
                foreach(var local in geometry.NearbyApproachPoints(target,0.35f,0.3f,12))
                {
                    var point=interior.transform.TransformPoint(local);
                    if(Vector3.Dot(point-actual,away)<0.35f || !motion.CanTraverseTo(point))continue;
                    route.Insert(next,new GuardPoint3 {X=local.x,Y=local.y,Z=local.z});
                    retreatWaypoint=next;trackedWaypoint=-1;preciseWaypoint=-1;stalledAt=(float)now;
                    return;
                }
                return;
            }
            if(finalWaypoint)
            {
                var intended=WorldPoint(interior,route[route.Count-1]);
                var chosen=WorldPoint(interior,rejoin[rejoinIndex]);
                if(Vector3.Distance(intended,chosen)>0.25f) return;
            }
            else for(int i=rejoinIndex+1;i<rejoin.Count;i++) detour.Add(rejoin[i]);
            route=detour;
            retreatWaypoint=-1;
            next=1;
            trackedWaypoint=-1;
            preciseWaypoint=-1;
            stalledAt=(float)now;
        }

        private static Vector3 WorldPoint(DaggerfallInterior interior,GuardPoint3 point)
        {
            return interior.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
        }
    }
}
