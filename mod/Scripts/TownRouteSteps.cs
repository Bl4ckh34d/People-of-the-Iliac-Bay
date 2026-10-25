using System;
using System.Collections;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class TownGeometry
    {
        public sealed class RouteResult { public List<SavedRoutePoint> Points; }

        // Old compaction removed straight doorway/grid controls. Reconstruct
        // those controls from scene geometry before smoothing cached itineraries;
        // protecting source[1] could otherwise preserve a 60m cardinal segment.
        // Caller owns publication and retains an active physical leg until arrival.
        public IEnumerator ReadSavedRouteSteps(IList<SavedRoutePoint> source,RouteResult result,Func<bool> valid)
        {
            result.Points=null;
            if(source==null || source.Count<2 || !valid()) yield break;
            var first=source[0];var last=source[source.Count-1];
            var operation=ReadGateRouteSteps(new SavedRoutePoint {X=first.X,Z=first.Z},
                new SavedRoutePoint {X=last.X,Z=last.Z},result,valid);
            try {while(operation.MoveNext()) {yield return null;if(!valid()) yield break;}}
            finally {var disposable=operation as IDisposable;if(disposable!=null)disposable.Dispose();}
        }

        public IEnumerator ReadRouteSteps(SavedRoutePoint from,SavedRoutePoint to,RouteResult result,Func<bool> valid)
        {return SharedRouteSteps(new RoutePoint(from.X,from.Z),new RoutePoint(to.X,to.Z),result,valid,
            ()=>ReadUncachedRouteSteps(from,to,result,valid));}

        private IEnumerator ReadUncachedRouteSteps(SavedRoutePoint from,SavedRoutePoint to,RouteResult result,Func<bool> valid)
        {
            result.Points=null;
            if(!valid()) yield break;
            yield return null;
            if(!valid()) yield break;
            var a=ScenePoint(new RoutePoint(from.X,from.Z));
            var b=ScenePoint(new RoutePoint(to.X,to.Z));
            List<SavedRoutePoint> native;
            if(TryNativeRoute(origin.InverseTransformPoint(a),origin.InverseTransformPoint(b),out native))
            {result.Points=native;yield break;}
            GridCell start,end;
            if(!FindEntranceCell(a,out start) || !FindEntranceCell(b,out end))
            { result.Points=new List<SavedRoutePoint>();yield break; }
            var search=GridPathfinder.Begin(this,start,end,30000);
            while(!search.IsComplete)
            {
                if(!valid()) yield break;
                search.Step(128);
                yield return null;
            }
            if(!valid()) yield break;
            var points=new List<SavedRoutePoint>();
            if(search.Path.Count==0) { result.Points=points;yield break; }
            var first=origin.InverseTransformPoint(a);var last=origin.InverseTransformPoint(b);
            points.Add(new SavedRoutePoint {X=first.x,Z=first.z});
            int work=0;
            foreach(var cell in search.Path)
            {
                var local=origin.InverseTransformPoint(SceneCell(cell));
                points.Add(new SavedRoutePoint {X=local.x,Z=local.z});
                if(++work%32==0) {yield return null;if(!valid()) yield break;}
            }
            points.Add(new SavedRoutePoint {X=last.x,Z=last.z});
            foreach(var point in points)
            {
                var ground=ScenePoint(new RoutePoint(point.X,point.Z));
                if(FurnitureSeatCatalog.Blocks(ground,ground,CrowdMotion.BodyRadius))
                {result.Points=new List<SavedRoutePoint>();yield break;}
                foreach(float height in new[]{0.4f,1f})
                    foreach(var collider in Physics.OverlapSphere(ScenePoint(new RoutePoint(point.X,point.Z))+Vector3.up*height,
                        0.2f,~0,QueryTriggerInteraction.Ignore))
                        if(IsStaticBlocker(collider)) {result.Points=new List<SavedRoutePoint>();yield break;}
                if(++work%16==0) {yield return null;if(!valid()) yield break;}
            }
            for(int i=1;i<points.Count;i++)
            {
                var p=ScenePoint(new RoutePoint(points[i-1].X,points[i-1].Z));
                var q=ScenePoint(new RoutePoint(points[i].X,points[i].Z));var delta=q-p;
                if(FurnitureSeatCatalog.Blocks(p,q,CrowdMotion.BodyRadius))
                {result.Points=new List<SavedRoutePoint>();yield break;}
                if(delta.magnitude>=0.001f)
                    foreach(float height in new[]{0.4f,1f})
                        foreach(var hit in Physics.SphereCastAll(p+Vector3.up*height,0.2f,delta.normalized,
                            delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                            if(IsStaticBlocker(hit.collider)) {result.Points=new List<SavedRoutePoint>();yield break;}
                if(++work%16==0) {yield return null;if(!valid()) yield break;}
            }
            var finish=FinishOutdoorRouteSteps(points,result,valid);
            while(finish.MoveNext()) {yield return null;if(!valid()) yield break;}
        }

        // Daily routines pass surveyed entrances directly so this sliced search uses
        // the exact cells and door endpoints used by RouteBetween.
        public IEnumerator ReadRouteSteps(Entrance from,Entrance to,RouteResult result,Func<bool> valid)
        {
            if(from==null||to==null)return ReadUncachedRouteSteps(from,to,result,valid);
            return SharedRouteSteps(new RoutePoint(from.LocalDoor.x,from.LocalDoor.z),new RoutePoint(to.LocalDoor.x,to.LocalDoor.z),
                result,valid,()=>ReadUncachedRouteSteps(from,to,result,valid));
        }

        private IEnumerator ReadUncachedRouteSteps(Entrance from,Entrance to,RouteResult result,Func<bool> valid)
        {
            result.Points=null;
            if(from==null || to==null || !valid()) yield break;
            yield return null;
            if(!valid()) yield break;
            List<SavedRoutePoint> native;
            if(TryNativeRoute(from.LocalDoor,to.LocalDoor,out native))
            {result.Points=native;yield break;}
            var search=GridPathfinder.Begin(this,from.Cell,to.Cell,30000);
            while(!search.IsComplete)
            {
                if(!valid()) yield break;
                search.Step(128);
                yield return null;
            }
            if(!valid()) yield break;
            var points=new List<SavedRoutePoint>();
            if(search.Path.Count==0) { result.Points=points;yield break; }
            points.Add(new SavedRoutePoint {X=from.LocalDoor.x,Z=from.LocalDoor.z});
            int work=0;
            foreach(var cell in search.Path)
            {
                var local=origin.InverseTransformPoint(SceneCell(cell));
                points.Add(new SavedRoutePoint {X=local.x,Z=local.z});
                if(++work%32==0) {yield return null;if(!valid()) yield break;}
            }
            points.Add(new SavedRoutePoint {X=to.LocalDoor.x,Z=to.LocalDoor.z});
            foreach(var point in points)
            {
                var ground=ScenePoint(new RoutePoint(point.X,point.Z));
                if(FurnitureSeatCatalog.Blocks(ground,ground,CrowdMotion.BodyRadius))
                {result.Points=new List<SavedRoutePoint>();yield break;}
                foreach(float height in new[]{0.4f,1f})
                    foreach(var collider in Physics.OverlapSphere(ScenePoint(new RoutePoint(point.X,point.Z))+Vector3.up*height,
                        0.2f,~0,QueryTriggerInteraction.Ignore))
                        if(IsStaticBlocker(collider)) {result.Points=new List<SavedRoutePoint>();yield break;}
                if(++work%16==0) {yield return null;if(!valid()) yield break;}
            }
            for(int i=1;i<points.Count;i++)
            {
                var p=ScenePoint(new RoutePoint(points[i-1].X,points[i-1].Z));
                var q=ScenePoint(new RoutePoint(points[i].X,points[i].Z));var delta=q-p;
                if(FurnitureSeatCatalog.Blocks(p,q,CrowdMotion.BodyRadius))
                {result.Points=new List<SavedRoutePoint>();yield break;}
                if(delta.magnitude>=0.001f)
                    foreach(float height in new[]{0.4f,1f})
                        foreach(var hit in Physics.SphereCastAll(p+Vector3.up*height,0.2f,delta.normalized,
                            delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                            if(IsStaticBlocker(hit.collider)) {result.Points=new List<SavedRoutePoint>();yield break;}
                if(++work%16==0) {yield return null;if(!valid()) yield break;}
            }
            var finish=FinishOutdoorRouteSteps(points,result,valid);
            while(finish.MoveNext()) {yield return null;if(!valid()) yield break;}
        }

        public IEnumerator ReadGateRouteSteps(SavedRoutePoint from,SavedRoutePoint to,RouteResult result,Func<bool> valid)
        {
            result.Points=null;
            var direct=new RouteResult();
            var directWork=ReadRouteSteps(from,to,direct,valid);
            try {while(directWork.MoveNext()) {yield return null;if(!valid()) yield break;}}
            finally {var disposable=directWork as IDisposable;if(disposable!=null)disposable.Dispose();}
            if(!valid()) yield break;
            if(direct.Points.Count>=2) {result.Points=direct.Points;yield break;}
            foreach(var crossing in gateCrossings)
            for(int direction=0;direction<2;direction++)
            {
                if(!valid()) yield break;
                var entry=crossing[direction];var exit=crossing[1-direction];
                double dx=exit.X-entry.X,dz=exit.Z-entry.Z;
                double midX=(entry.X+exit.X)*0.5,midZ=(entry.Z+exit.Z)*0.5;
                if((from.X-midX)*dx+(from.Z-midZ)*dz>=0 || (to.X-midX)*dx+(to.Z-midZ)*dz<=0) continue;
                var a=ScenePoint(new RoutePoint(entry.X,entry.Z));var b=ScenePoint(new RoutePoint(exit.X,exit.Z));
                var delta=b-a;bool blocked=false;
                if(FurnitureSeatCatalog.Blocks(a,b,CrowdMotion.BodyRadius)) continue;
                foreach(float height in new[]{0.4f,1f})
                {
                    foreach(var point in new[]{a,b})
                        foreach(var collider in Physics.OverlapSphere(point+Vector3.up*height,0.2f,~0,QueryTriggerInteraction.Ignore))
                            if(IsStaticBlocker(collider)) blocked=true;
                    foreach(var hit in Physics.SphereCastAll(a+Vector3.up*height,0.2f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                        if(IsStaticBlocker(hit.collider)) blocked=true;
                }
                if(blocked) continue;
                var before=new RouteResult();var after=new RouteResult();
                var beforeWork=ReadRouteSteps(from,entry,before,valid);
                try {while(beforeWork.MoveNext()) {yield return null;if(!valid()) yield break;}}
                finally {var disposable=beforeWork as IDisposable;if(disposable!=null)disposable.Dispose();}
                if(!valid()) yield break;
                var afterWork=ReadRouteSteps(exit,to,after,valid);
                try {while(afterWork.MoveNext()) {yield return null;if(!valid()) yield break;}}
                finally {var disposable=afterWork as IDisposable;if(disposable!=null)disposable.Dispose();}
                if(!valid()) yield break;
                if(before.Points.Count<2 || after.Points.Count<2) continue;
                before.Points.AddRange(after.Points);result.Points=before.Points;yield break;
            }
            if(valid()) result.Points=direct.Points;
        }
    }
}
