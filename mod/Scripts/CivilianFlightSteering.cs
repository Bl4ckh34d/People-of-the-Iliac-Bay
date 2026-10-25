using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Actor-owned cache disappears with its view; authoritative position stays in AlarmPerson.
    public sealed class CivilianFlightSteering : MonoBehaviour
    {
        private readonly List<Vector3> route=new List<Vector3>();
        private int next;
        private float repathAt;
        private Vector3 lastDanger;
        public static bool Safe(Vector3 from,Vector3 to,Vector3 danger)
        { return FlightSafety.SafeStep(from.x,from.z,to.x,to.z,danger.x,danger.z); }
        public Vector3 Target(Vector3 ground,Vector3 preferred,Vector3 danger,TownGeometry geometry,Transform origin)
        {
            if(Safe(ground,preferred,danger)) {route.Clear();return preferred;}
            while(next<route.Count && Vector3.Distance(ground,route[next])<0.2f) next++;
            if(Time.unscaledTime<repathAt && Vector3.Distance(lastDanger,danger)<1 && next<route.Count && Safe(ground,route[next],danger))
                return route[next];
            if(Time.unscaledTime<repathAt && Vector3.Distance(lastDanger,danger)<1 && next>=route.Count) return ground;
            route.Clear(); next=1; repathAt=Time.unscaledTime+0.75f; lastDanger=danger;
            var away=ground-danger; away.y=0;
            if(away.sqrMagnitude<0.001f) away=transform.forward;
            away.Normalize(); float best=float.NegativeInfinity;
            foreach(float angle in new[] {0f,45f,-45f,90f,-90f,135f,-135f})
            {
                var goal=ground+Quaternion.Euler(0,angle,0)*away*4;
                Vector3 directEnd;
                if(TryDirectRetreat(ground,goal,out directEnd) && Safe(ground,directEnd,danger))
                {
                    var gap=directEnd-danger;gap.y=0;float directScore=gap.magnitude-Vector3.Distance(ground,directEnd)*0.05f;
                    if(directScore>best) {best=directScore;route.Clear();route.Add(ground);route.Add(directEnd);}
                    continue;
                }
                var start=origin.InverseTransformPoint(ground); var end=origin.InverseTransformPoint(goal);
                var points=geometry.RouteBetweenPoints(new SavedRoutePoint {X=start.x,Z=start.z},new SavedRoutePoint {X=end.x,Z=end.z});
                if(points.Count<2) continue;
                var candidate=new List<Vector3>(); bool safe=true; float length=0;
                foreach(var point in points)
                {
                    var world=geometry.ScenePoint(new RoutePoint(point.X,point.Z));
                    var previous=candidate.Count==0 ? ground : candidate[candidate.Count-1];
                    if(!Safe(previous,world,danger)) {safe=false;break;}
                    length+=Vector3.Distance(previous,world); candidate.Add(world);
                }
                if(!safe || length>8 || length<0.25f) continue;
                var last=candidate[candidate.Count-1]; var delta=last-danger;delta.y=0;
                float score=delta.magnitude-length*0.05f;
                if(score>best) {best=score;route.Clear();route.AddRange(candidate);}
            }
            return next<route.Count ? route[next] : ground;
        }
        public static bool TryDirectRetreat(Vector3 from,Vector3 to,out Vector3 end)
        {
            end=from; var delta=to-from;delta.y=0;float distance=delta.magnitude;
            if(distance<0.1f || distance>4.1f) return false;
            // Validate the entire body corridor and continuous flat support. Never
            // bypass geometry just because the global grid is poorly aligned here.
            foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*0.3f,from+Vector3.up*1.65f,
                0.22f,delta.normalized,distance,~0,QueryTriggerInteraction.Ignore))
                if(CivilianResponses.Blocker(hit.collider)) return false;
            for(float along=0;along<=distance+0.249f;along+=0.25f)
            {
                var sample=from+delta.normalized*Mathf.Min(along,distance);bool support=false;
                foreach(var hit in Physics.RaycastAll(sample+Vector3.up*0.4f,Vector3.down,0.8f,~0,QueryTriggerInteraction.Ignore))
                    if(CivilianResponses.Blocker(hit.collider) && hit.normal.y>0.85f && Mathf.Abs(hit.point.y-from.y)<0.15f)
                    {end=hit.point;support=true;break;}
                if(!support) return false;
            }
            return true;
        }
    }
}
