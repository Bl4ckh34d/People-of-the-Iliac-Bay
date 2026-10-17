using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class ResidentDepartureSurvey
    {
        public List<RoutePoint> HomeRoute,Road;
        public double Metres,GateMetres;
        public bool Walled;
        // Prepare while the settlement is loaded. A failed bridge is not a licence
        // to interpolate through walls or trees; the caller must choose another trip.
        public static ResidentDepartureSurvey Read(TownGeometry geometry,BuildingDirectory directory,int home,IReadOnlyList<RoutePoint> road)
        {
            if(road==null || road.Count<2) throw new ArgumentException("Road route required.");
            var exits=geometry.SurveyTravelerRoutes(home);
            bool probe=Array.IndexOf(Environment.GetCommandLineArgs(),"-livingcities-resident-trip-probe")>=0;
            if(probe) Debug.Log("LIVING_CITIES_ROAD_JOIN_SURVEY: exits="+exits.Count+"; location="+directory.GetComponent<DaggerfallLocation>().Summary.MapPixelX+","+directory.GetComponent<DaggerfallLocation>().Summary.MapPixelY);
            ResidentDepartureSurvey best=null;
            for(int step=1;step<=9;step++)
            {
                double f=step*0.05;
                var join=new RoutePoint(road[0].X+(road[1].X-road[0].X)*f,road[0].Z+(road[1].Z-road[0].Z)*f);
                Vector3 ground; Transform parent;
                if(!RoadTravelActors.Wilderness(join) || !RoadTravelActors.Ground(join,out ground,out parent))
                { if(probe) Debug.Log("LIVING_CITIES_ROAD_JOIN_SKIP: "+join.X+","+join.Z); continue; }
                foreach(var exit in exits)
                {
                    var local=new List<RoutePoint>(exit.Points); local.Reverse();
                    double gate=Length(local);
                    Vector3 from=geometry.ScenePoint(local[local.Count-1]);
                    var bridge=Bridge(from,ground,parent.GetComponent<Terrain>(),directory.transform);
                    if(bridge==null) { if(probe) Debug.Log("LIVING_CITIES_ROAD_JOIN_BLOCKED: from="+from+"; to="+ground+"; distance="+Vector3.Distance(from,ground)); continue; }
                    local.AddRange(bridge); double length=Length(local);
                    if(best!=null && best.Metres<=length) continue;
                    var trimmed=new List<RoutePoint> {join};
                    for(int i=1;i<road.Count;i++) trimmed.Add(road[i]);
                    best=new ResidentDepartureSurvey {HomeRoute=local,Road=trimmed,Metres=length,GateMetres=gate,Walled=exit.Walled};
                }
            }
            return best;
        }
        private static double Length(List<RoutePoint> points)
        {
            double total=0;
            for(int i=1;i<points.Count;i++) { double x=points[i].X-points[i-1].X,z=points[i].Z-points[i-1].Z; total+=Math.Sqrt(x*x+z*z); }
            return total;
        }
        internal static List<RoutePoint> Bridge(Vector3 from,Vector3 to,Terrain terrain,Transform origin,Action<string> rejected=null)
        {
            float distance=Vector3.Distance(from,to);
            if(!terrain || distance>512) return null;
            var result=new List<RoutePoint>(); var previous=from;
            int count=Math.Max(1,Mathf.CeilToInt(distance/2));
            for(int i=1;i<=count;i++)
            {
                var point=Vector3.Lerp(from,to,(float)i/count);
                point.y=terrain.SampleHeight(point)+terrain.transform.position.y;
                if(Mathf.Abs(point.y-previous.y)>1.2f) {if(rejected!=null) rejected("slope "+previous+" -> "+point); return null;}
                var delta=point-previous;
                foreach(float height in new[]{0.5f,1.4f})
                {
                    foreach(var collider in Physics.OverlapSphere(point+Vector3.up*height,0.25f,~0,QueryTriggerInteraction.Ignore))
                        if(Blocker(collider)) {if(rejected!=null) rejected("overlap "+collider.name+" at "+point+" height="+height); return null;}
                    if(delta.sqrMagnitude>0.0001f)
                        foreach(var hit in Physics.SphereCastAll(previous+Vector3.up*height,0.25f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                            if(Blocker(hit.collider)) {if(rejected!=null) rejected("sweep "+hit.collider.name+" from "+previous+" to "+point+" height="+height); return null;}
                }
                var local=origin.InverseTransformPoint(point); result.Add(new RoutePoint(local.x,local.z)); previous=point;
            }
            return result;
        }
        private static bool Blocker(Collider collider)
        { return collider.GetComponentInParent<DaggerfallEntityBehaviour>()==null && collider.GetComponentInParent<MobilePersonNPC>()==null && collider.GetComponentInParent<PlayerMotor>()==null; }
    }
}
