using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class ResidentDepartureSurvey
    {
        public List<RoutePoint> HomeRoute,Road;
        public double Metres,GateMetres;
        public bool Walled;
        // Prepare while the settlement is loaded. A failed bridge is not a licence
        // to interpolate through walls or trees; the caller must choose another trip.
        public static IEnumerator ReadSteps(TownGeometry geometry,BuildingDirectory directory,int home,IReadOnlyList<RoutePoint> road,
            ResidentDepartureSurvey result,Func<bool> stillValid)
        {
            if(geometry==null || !directory || result==null || stillValid==null || road==null || road.Count<2)
                throw new ArgumentException("Road route and active settlement required.");
            result.HomeRoute=null; result.Road=null;
            if(!stillValid()) yield break;
            // Use the same model-cleared street/gate connections as trip planning.
            // Choosing a different exit and then drawing a straight bridge to the
            // road can cut diagonally back through a city wall.
            var surveyed=new UnloadedSettlementSurvey.Result();
            yield return UnloadedSettlementSurvey.ReadSteps(directory.LocationData,surveyed);
            if(!stillValid() || surveyed.Grid==null) yield break;
            var accesses=new UnloadedSettlementSurvey.RoadAccessResult();
            yield return surveyed.Grid.ReadRoadAccessSteps(road,accesses,stillValid);
            if(!stillValid() || accesses.Accesses==null) yield break;
            bool probe=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-resident-trip-probe")>=0;
            foreach(var access in accesses.Accesses)
            {
                if(!stillValid()) yield break;
                Vector3 ground; Transform parent;
                if(!RoadTravelActors.Wilderness(access.RoadPoint) || !RoadTravelActors.Ground(access.RoadPoint,out ground,out parent)) continue;
                foreach(var entrance in surveyed.Grid.Entrances)
                {
                    if(entrance.BuildingKey!=home) continue;
                    var candidate=new UnloadedSettlementSurvey.BuildingRoadRoute();
                    yield return surveyed.Grid.ReadRoadBuildingRouteSteps(access,entrance,candidate,stillValid);
                    if(!stillValid()) yield break;
                    if(candidate.Points==null) continue;
                    var local=new List<RoutePoint>(candidate.Points); local.Reverse();
                    bool clear=true;
                    for(int i=1;i<local.Count;i++)
                    {
                        if(Bridge(geometry.ScenePoint(local[i-1]),geometry.ScenePoint(local[i]),parent.GetComponent<Terrain>(),directory.transform,
                            probe ? (Action<string>)(message=>Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROAD_JOIN_REJECT: "+message)) : null)==null)
                        {clear=false;break;}
                        if(i%32==0) {yield return null;if(!stillValid()) yield break;}
                    }
                    if(!clear) continue;
                    var trimmed=new List<RoutePoint> {access.RoadPoint};
                    for(int i=1;i<road.Count;i++) trimmed.Add(road[i]);
                    result.HomeRoute=local;result.Road=trimmed;result.Metres=Length(local);
                    result.GateMetres=candidate.Metres-candidate.EntryGateMetres;result.Walled=candidate.Walled;
                    if(probe) Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROAD_JOIN_SURVEY: home="+home+"; candidatePoints="+local.Count+"; metres="+result.Metres);
                    yield break;
                }
            }
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
