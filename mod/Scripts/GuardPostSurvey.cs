using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed partial class TownGeometry
    {
        private sealed class PostSite { public SavedRoutePoint Point; public double Rank; }
        private readonly Dictionary<SavedRoutePoint,Vector3> postFacings=new Dictionary<SavedRoutePoint,Vector3>();
        public List<SavedRoutePoint> SurveyGuardPosts(int maximum)
        {
            var doors=new List<Vector3>();
            foreach(var owner in origin.GetComponentsInChildren<DaggerfallStaticDoors>())
                if(owner.Doors!=null) for(int i=0;i<owner.Doors.Length;i++)
                    if(owner.Doors[i].doorType==DoorTypes.Building) doors.Add(owner.GetDoorPosition(i));
            var gates=origin.GetComponentsInChildren<DaggerfallCityGate>();
            var candidates=new List<PostSite>();
            for(int z=4;z<Height-4;z+=2) for(int x=4;x<Width-4;x+=2)
            {
                var scene=SceneCell(new GridCell(x,z));
                float gateDistance=float.PositiveInfinity;
                foreach(var gate in gates)
                { var gap=scene-gate.transform.position; gap.y=0; gateDistance=Mathf.Min(gateDistance,gap.sqrMagnitude); }
                bool gateSite=gateDistance>=36 && gateDistance<=400 && navigation.GetNavGridWeightLocal(x,z)==15;
                if(!gateSite && !GuardPostSites.IsJunction(x,z,navigation.GetNavGridWeightLocal,8)) continue;
                float doorDistance=float.PositiveInfinity;
                foreach(var door in doors) { var gap=scene-door; gap.y=0; doorDistance=Mathf.Min(doorDistance,gap.sqrMagnitude); }
                if(doorDistance<25) continue;
                var local=origin.InverseTransformPoint(scene);
                // Prefer gate approaches and the edges of paved public spaces.
                candidates.Add(new PostSite { Point=new SavedRoutePoint { X=local.x,Z=local.z },
                    Rank=(gateSite ? -10000 : 0)+doorDistance });
            }
            candidates.Sort((a,b)=>{ int n=a.Rank.CompareTo(b.Rank); if(n!=0) return n;
                n=a.Point.X.CompareTo(b.Point.X); return n!=0 ? n : a.Point.Z.CompareTo(b.Point.Z); });
            var posts=new List<SavedRoutePoint>();
            foreach(var site in candidates)
            {
                if(posts.Exists(p=>Math.Pow(p.X-site.Point.X,2)+Math.Pow(p.Z-site.Point.Z,2)<36)) continue;
                var ground=ScenePoint(new RoutePoint(site.Point.X,site.Point.Z)); bool blocked=false;
                foreach(var collider in Physics.OverlapCapsule(ground+Vector3.up*0.7f,ground+Vector3.up*1.7f,
                    0.4f,~0,QueryTriggerInteraction.Ignore))
                    if(IsStaticBlocker(collider)) { blocked=true; break; }
                if(blocked || RouteBetweenPoints(site.Point,site.Point).Count<2) continue;
                posts.Add(site.Point); if(posts.Count>=maximum) break;
            }
            return posts;
        }
        public Vector3 GuardPostFacing(SavedRoutePoint point)
        {
            Vector3 cached;
            if(postFacings.TryGetValue(point,out cached)) return cached;
            Vector3 from=ScenePoint(new RoutePoint(point.X,point.Z));
            Vector3 best=Vector3.forward; float longest=-1;
            foreach(var direction in new[] { Vector3.forward,Vector3.right,Vector3.back,Vector3.left })
            {
                float distance=12;
                foreach(var hit in Physics.SphereCastAll(from+Vector3.up,0.4f,direction,12,~0,QueryTriggerInteraction.Ignore))
                    if(IsStaticBlocker(hit.collider)) distance=Mathf.Min(distance,hit.distance);
                if(distance>longest) { longest=distance; best=direction; }
            }
            postFacings[point]=best;
            return best;
        }
    }
}
