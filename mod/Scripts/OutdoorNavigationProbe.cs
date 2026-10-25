using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in acceptance only. The normal actor update never runs this survey.
    public static class OutdoorNavigationProbe
    {
        public static void VerifyPersonalGeometryRestore(WorldState before,WorldState after,int settlement,string resident)
        {
            var a=before.Routines.Find(t=>t.SettlementId==settlement).Residents.Find(r=>r.ResidentId==resident);
            var b=after.Routines.Find(t=>t.SettlementId==settlement).Residents.Find(r=>r.ResidentId==resident);
            if(a.RouteGeometryVersion!=OutdoorRoutineMigration.CurrentVersion || b.RouteGeometryVersion!=a.RouteGeometryVersion ||
                a.PersonalLaneVersion!=OutdoorRoutineMigration.CurrentPersonalLaneVersion || b.PersonalLaneVersion!=a.PersonalLaneVersion)
                throw new InvalidOperationException("Native save restore lost personal outdoor geometry versions.");
            if(Math.Abs(a.ItineraryDepartureSeconds-b.ItineraryDepartureSeconds)>0.001 ||
                a.Itinerary.Stops.Count!=b.Itinerary.Stops.Count || Math.Abs(a.Itinerary.ExitWalkSeconds-b.Itinerary.ExitWalkSeconds)>0.001)
                throw new InvalidOperationException("Native save restore changed personal outdoor timetable.");
            int points=VerifySamePersonalPoints(a.Route,b.Route)+VerifySamePersonalPoints(a.Itinerary.ExitRoute,b.Itinerary.ExitRoute);
            for(int i=0;i<a.Itinerary.Stops.Count;i++)
            {
                var x=a.Itinerary.Stops[i];var y=b.Itinerary.Stops[i];
                if(x.BuildingKey!=y.BuildingKey || Math.Abs(x.WalkSeconds-y.WalkSeconds)>0.001 || Math.Abs(x.DwellSeconds-y.DwellSeconds)>0.001)
                    throw new InvalidOperationException("Native save restore changed personal outdoor visit boundary.");
                points+=VerifySamePersonalPoints(x.Route,y.Route);
            }
            // The loaded scene has no prepared lane cache. No callback may run.
            OutdoorRoutineMigration.RuntimeItinerary(b,route=>{throw new InvalidOperationException("Restored personal outdoor route was offset again.");});
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SAVED_PERSONAL_GEOMETRY_RESTORE_PASS: resident="+resident+"; points="+points+
                "; geometryVersion="+b.RouteGeometryVersion+"; personalLaneVersion="+b.PersonalLaneVersion+"; timetable=preserved; laneCallbacks=0");
        }
        private static int VerifySamePersonalPoints(IList<SavedRoutePoint> a,IList<SavedRoutePoint> b)
        {
            if(a.Count!=b.Count) throw new InvalidOperationException("Native save restore changed personal outdoor control count.");
            for(int i=0;i<a.Count;i++) if(Math.Abs(a[i].X-b[i].X)>0.0001 || Math.Abs(a[i].Z-b[i].Z)>0.0001)
                throw new InvalidOperationException("Native save restore changed personal outdoor control.");
            return a.Count;
        }

        // Attach to the existing physical daily probe. The witness consumes the
        // restored route and actual rendered body samples, never a fresh fixture.
        public sealed class SavedLegWitness
        {
            private readonly string resident;
            private readonly TownGeometry geometry;
            private readonly List<SavedRoutePoint> route;
            private Vector3 previous;
            private bool seen;
            private float moved,diagonalMoved,maxFrame;
            private double plannedDiagonal;
            private int frames;
            public SavedLegWitness(string resident,TownGeometry geometry,IList<SavedRoutePoint> savedRoute)
            {
                if(geometry==null || savedRoute==null || savedRoute.Count<2) throw new ArgumentException("Saved leg witness lacks route.");
                this.resident=resident;this.geometry=geometry;route=new List<SavedRoutePoint>();
                foreach(var point in savedRoute) route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
                for(int i=2;i<route.Count-1;i++)
                {
                    double dx=Math.Abs(route[i].X-route[i-1].X),dz=Math.Abs(route[i].Z-route[i-1].Z);
                    double x=(route[i].X+route[i-1].X)*0.5,z=(route[i].Z+route[i-1].Z)*0.5;
                    double firstX=x-route[0].X,firstZ=z-route[0].Z,lastX=x-route[route.Count-1].X,lastZ=z-route[route.Count-1].Z;
                    if(Math.Min(dx,dz)>0.025 && Math.Min(dx,dz)/Math.Max(dx,dz)>0.08 &&
                        firstX*firstX+firstZ*firstZ>9 && lastX*lastX+lastZ*lastZ>9)
                        plannedDiagonal+=Math.Sqrt(dx*dx+dz*dz);
                }
            }
            public void Record(Vector3 ground)
            {
                if(seen)
                {
                    var delta=ground-previous;delta.y=0;float step=delta.magnitude;
                    maxFrame=Math.Max(maxFrame,step);moved+=step;
                    if(step>1.5f) throw new InvalidOperationException("Saved outdoor navigation teleported resident "+resident+" by "+step+"m.");
                    if(FurnitureSeatCatalog.Blocks(previous,ground,CrowdMotion.BodyRadius))
                        throw new InvalidOperationException("Saved outdoor navigation entered furniture footprint: "+resident);
                    var first=geometry.ScenePoint(new RoutePoint(route[0].X,route[0].Z));
                    var last=geometry.ScenePoint(new RoutePoint(route[route.Count-1].X,route[route.Count-1].Z));
                    float small=Math.Min(Math.Abs(delta.x),Math.Abs(delta.z)),large=Math.Max(Math.Abs(delta.x),Math.Abs(delta.z));
                    // Final diagonal doorway joins cannot make a cardinal route pass.
                    if(small>0.0001f && small/Math.Max(0.0001f,large)>0.08f &&
                        Vector3.Distance(ground,first)>3 && Vector3.Distance(ground,last)>3) diagonalMoved+=step;
                }
                previous=ground;seen=true;frames++;
            }
            public void Complete()
            {
                if(!seen || moved<1) throw new InvalidOperationException("Saved outdoor navigation did not observe physical movement.");
                if(plannedDiagonal>=2 && diagonalMoved<0.25f)
                    throw new InvalidOperationException("Saved smoothed route was not used by actual outdoor movement: "+resident);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SAVED_OUTDOOR_MOVEMENT_PASS: resident="+resident+"; savedPoints="+route.Count+
                    "; plannedInteriorDiagonal="+plannedDiagonal.ToString("F2")+"; physicalInteriorDiagonal="+diagonalMoved.ToString("F2")+
                    "; moved="+moved.ToString("F2")+"; frames="+frames+"; maxFrame="+maxFrame.ToString("F3"));
            }
        }

        public static IEnumerator Run(BuildingDirectory directory,CityNavigation navigation,WorldState saved=null)
        {
            var geometry=new TownGeometry(directory,navigation,false);
            var operation=geometry.VerifyOutdoorNavigationSteps();
            while(operation.MoveNext()) yield return null;
            // Reproduce the real saved temple-leg regression without waiting
            // through every indoor dwell and physical walk in the full-day test.
            if(saved!=null)
            {
                var town=saved.Routines.Find(t=>t.SettlementId==directory.MapID);
                var routine=town==null ? null : town.Residents.Find(r=>r.ResidentId=="s:1291010263:h:197638:r:0");
                var population=saved.Settlements.Find(t=>t.SettlementId==directory.MapID);
                Resident person=null;
                if(population!=null) foreach(var home in population.Households)
                    foreach(var resident in home.Residents) if(routine!=null && resident.Id==routine.ResidentId) person=resident;
                if(person!=null && routine.Itinerary!=null)
                {
                    var routes=new List<List<SavedRoutePoint>>();
                    foreach(var stop in routine.Itinerary.Stops) routes.Add(stop.Route);
                    routes.Add(routine.Itinerary.ExitRoute);
                    for(int i=0;i<routes.Count;i++)
                    {
                        var result=new TownGeometry.RouteResult();
                        var repair=geometry.ReadSavedRouteSteps(routes[i],result,()=>true);
                        while(repair.MoveNext()) yield return null;
                        if(result.Points==null || result.Points.Count<2) throw new InvalidOperationException("Saved lane regression could not reconstruct leg "+i);
                        var lane=geometry.PrepareStreetRouteSteps(result.Points,person.AppearanceSeed,()=>true);
                        while(lane.MoveNext()) yield return null;
                        var line=new List<RoutePoint>();foreach(var p in result.Points) line.Add(new RoutePoint(p.X,p.Z));
                        var personal=geometry.OffsetStreetRoute(line,person.AppearanceSeed);
                        var points=new List<SavedRoutePoint>();foreach(var p in personal) points.Add(new SavedRoutePoint {X=p.X,Z=p.Z});
                        var verify=geometry.VerifySavedOutdoorRouteSteps(points);
                        while(verify.MoveNext()) yield return null;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SAVED_LANE_REPAIR_LEG: leg="+i+"; before="+routes[i].Count+"; after="+points.Count);
                    }
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SAVED_LANE_REPAIR_PASS: resident="+person.Id+"; legs="+routes.Count);
                }
            }
        }
    }
    public sealed partial class TownGeometry
    {
        public IEnumerator VerifySavedOutdoorRouteSteps(IList<SavedRoutePoint> route)
        {
            if(route==null || route.Count<2) throw new ArgumentException("Saved outdoor route is missing.");
            int sharp=0;
            for(int i=1;i<route.Count-1;i++)
            {
                // Versioned routes preserve these exact doorway-to-grid controls,
                // including approaches longer than the usual three-metre apron.
                if(i==1 || i==route.Count-2) continue;
                var before=route[i-1];var corner=route[i];var after=route[i+1];
                double ax=before.X-corner.X,az=before.Z-corner.Z,bx=after.X-corner.X,bz=after.Z-corner.Z;
                double a=Math.Sqrt(ax*ax+az*az),b=Math.Sqrt(bx*bx+bz*bz);
                if(a<1 || b<1) continue;
                double dot=(ax*bx+az*bz)/(a*b);
                if(dot<-0.2 || dot>0.2) continue;
                // Only street corners well beyond exact door approaches count.
                double fx=corner.X-route[0].X,fz=corner.Z-route[0].Z;
                double lx=corner.X-route[route.Count-1].X,lz=corner.Z-route[route.Count-1].Z;
                if(fx*fx+fz*fz<9 || lx*lx+lz*lz<9) continue;
                var entry=new SavedRoutePoint {X=corner.X+ax/a*Math.Min(4,a),Z=corner.Z+az/a*Math.Min(4,a)};
                var exit=new SavedRoutePoint {X=corner.X+bx/b*Math.Min(4,b),Z=corner.Z+bz/b*Math.Min(4,b)};
                bool clear=OutdoorShortcutClear(entry,exit);sharp++;yield return null;
                if(clear) throw new InvalidOperationException("Restored daily route retains avoidable cardinal street corner "+i+" of "+route.Count+".");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SAVED_OUTDOOR_GEOMETRY_PASS: savedPoints="+route.Count+"; necessarySharpCorners="+sharp);
        }

        public IEnumerator VerifyOutdoorNavigationSteps()
        {
            RoutePoint fixture=default(RoutePoint),normal=default(RoutePoint);
            LaneCrossSection width=default(LaneCrossSection);
            bool found=false;int examined=0;
            for(int z=8;z<Height-8 && !found && examined<256;z+=4)
            for(int x=8;x<Width-8 && !found && examined<256;x+=4)
            {
                if(navigation.GetNavGridWeightLocal(x,z)!=15) continue;
                examined++;yield return null;
                var local=origin.InverseTransformPoint(SceneCell(new GridCell(x,z)));
                var point=new RoutePoint(local.x,local.z);
                foreach(var cross in new[] {new RoutePoint(0,1),new RoutePoint(1,0)})
                {
                    var span=SurveyStreetCrossSection(point,cross);
                    if(span.Left+span.Right<3) continue;
                    double centerOffset=span.Center+(span.Left-span.Right)*0.5;
                    var center=new RoutePoint(point.X+cross.X*centerOffset,point.Z+cross.Z*centerOffset);
                    var tangent=new RoutePoint(cross.Z,-cross.X);
                    var a=new SavedRoutePoint {X=center.X-tangent.X*5,Z=center.Z-tangent.Z*5};
                    var b=new SavedRoutePoint {X=center.X+tangent.X*5,Z=center.Z+tangent.Z*5};
                    if(!OutdoorShortcutClear(a,b)) continue;
                    fixture=center;normal=cross;width=span;found=true;break;
                }
            }
            if(!found) throw new InvalidOperationException("Outdoor navigation probe found no supported broad road within 256 paved samples.");
            var along=new RoutePoint(normal.Z,-normal.X);
            var line=new List<RoutePoint> {new RoutePoint(fixture.X-along.X*5,fixture.Z-along.Z*5),fixture,
                new RoutePoint(fixture.X+along.X*5,fixture.Z+along.Z*5)};
            var controls=CorridorLaneRoutes.Controls(line);
            for(int i=1;i<controls.Count-1;i++)
            {SurveyStreetCrossSection(controls[i],CorridorLaneRoutes.NormalAt(controls,i));yield return null;}
            double minimum=100,maximum=-100;
            var acceptedLanes=new List<List<RoutePoint>>();
            for(uint seed=1;seed<=32;seed++)
            {
                var lane=CorridorLaneRoutes.Offset(line,seed,CachedStreetCrossSection,LaneSegmentClear);
                acceptedLanes.Add(lane);
                if(lane.Count<3) throw new InvalidOperationException("Native corridor lane lost its controls.");
                var repeat=CorridorLaneRoutes.Offset(line,seed,CachedStreetCrossSection,LaneSegmentClear);
                for(int i=0;i<lane.Count;i++)
                {
                    if(lane[i].X!=repeat[i].X || lane[i].Z!=repeat[i].Z)
                        throw new InvalidOperationException("Native corridor lane changed for stable identity.");
                    if(i>0 && !LaneSegmentClearCore(lane[i-1],lane[i]))
                        throw new InvalidOperationException("Native corridor lane lacks body clearance or floor support.");
                }
                if(lane[0].X!=line[0].X || lane[0].Z!=line[0].Z ||
                    lane[lane.Count-1].X!=line[2].X || lane[lane.Count-1].Z!=line[2].Z)
                    throw new InvalidOperationException("Native corridor lane moved exact approaches.");
                var middle=lane[lane.Count/2];
                double offset=(middle.X-fixture.X)*normal.X+(middle.Z-fixture.Z)*normal.Z;
                minimum=Math.Min(minimum,offset);maximum=Math.Max(maximum,offset);
                yield return null;
            }
            if(maximum-minimum<2) throw new InvalidOperationException("Native walkers did not spread across at least 2m of verified pavement.");
            // Benchmark identical accepted segment inputs, outside generation.
            // The oracle deliberately bypasses the new cache.
            var timer=System.Diagnostics.Stopwatch.StartNew();int segments=0;
            foreach(var lane in acceptedLanes)for(int i=1;i<lane.Count;i++)
            {if(!LaneSegmentClearCore(lane[i-1],lane[i]))throw new InvalidOperationException("Uncached clearance rejected lane.");segments++;}
            double uncachedMs=timer.Elapsed.TotalMilliseconds;
            long beforeSurveys=ClearanceCellSurveys,beforeExact=ClearanceExactChecks;
            timer.Restart();
            foreach(var lane in acceptedLanes)for(int i=1;i<lane.Count;i++)
                if(!LaneSegmentClear(lane[i-1],lane[i]))throw new InvalidOperationException("Shared clearance rejected repeated lane.");
            double sharedMs=timer.Elapsed.TotalMilliseconds;
            if(ClearanceCellSurveys!=beforeSurveys||ClearanceExactChecks!=beforeExact)
                throw new InvalidOperationException("Repeated lanes performed another static survey.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHARED_CLEARANCE_PASS: lanes=32; segments="+segments+"; uncachedMs="+uncachedMs.ToString("F2")+
                "; sharedMs="+sharedMs.ToString("F2")+"; cells="+ClearanceCellCount+"; hits="+ClearanceCacheHits+
                "; additionalSurveys="+(ClearanceCellSurveys-beforeSurveys)+"; additionalExact="+(ClearanceExactChecks-beforeExact));
            yield return null;
            VerifySharedGateOverlay();

            var from=new SavedRoutePoint {X=fixture.X-along.X*3-normal.X*0.3,Z=fixture.Z-along.Z*3-normal.Z*0.3};
            var to=new SavedRoutePoint {X=fixture.X+along.X*3+normal.X*0.3,Z=fixture.Z+along.Z*3+normal.Z*0.3};
            sharedRoutes.Clear();bool alive=true;
            var abandoned=ReadSavedRouteSteps(new[]{from,to},new RouteResult(),()=>alive);
            if(!abandoned.MoveNext()||pendingSharedRoutes.Count==0)throw new InvalidOperationException("Cancellation fixture did not acquire shared route.");
            ((IDisposable)abandoned).Dispose();
            if(pendingSharedRoutes.Count!=0)throw new InvalidOperationException("Disposed parent stranded shared route request.");
            abandoned=ReadSavedRouteSteps(new[]{from,to},new RouteResult(),()=>alive);
            abandoned.MoveNext();alive=false;
            while(abandoned.MoveNext())yield return null;
            if(pendingSharedRoutes.Count!=0)throw new InvalidOperationException("Cancelled parent stranded shared route request.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHARED_ROUTE_CANCELLATION_PASS: disposed-and-invalidated=clean");
            var result=new RouteResult();var routeWork=ReadRouteSteps(from,to,result,()=>true);
            while(routeWork.MoveNext()) yield return null;
            if(result.Points==null || result.Points.Count<2 || OutdoorRouteStraightPoints>=OutdoorRouteInputPoints)
                throw new InvalidOperationException("Native outdoor route did not remove redundant grid controls.");
            for(int i=1;i<result.Points.Count;i++)
                if(!LaneSegmentClear(new RoutePoint(result.Points[i-1].X,result.Points[i-1].Z),
                    new RoutePoint(result.Points[i].X,result.Points[i].Z)))
                    throw new InvalidOperationException("Native smoothed route lacks supported body clearance.");
            int input=OutdoorRouteInputPoints,output=OutdoorRouteOutputPoints,straight=OutdoorRouteStraightPoints,
                checks=OutdoorShortcutChecks,curves=OutdoorRoundedCorners;
            var reused=new RouteResult();var reuse=ReadRouteSteps(to,from,reused,()=>true);int reuseSlices=0;
            while(reuse.MoveNext()){reuseSlices++;yield return null;}
            if(reuseSlices!=0||reused.Points.Count!=result.Points.Count)
                throw new InvalidOperationException("Shared endpoint route did not reuse its completed reverse path.");
            var posts=new GuardPostResult();var postWork=SurveyGuardPostSteps(32,posts);
            while(postWork.MoveNext()) yield return null;
            if(posts.Posts==null || posts.Posts.Count==0 || PublicPostsAcrossStreetWidth==0)
                throw new InvalidOperationException("Native ordinary guard survey did not use verified broad road width.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_NAVIGATION_PASS: input="+input+" output="+output+" shortcutChecks="+checks+
                " stringPulledPoints="+straight+" roundedCorners="+curves+" crossSections="+StreetCrossSectionCount+" widest="+WidestVerifiedStreet.ToString("F2")+
                " walkerSpread="+(maximum-minimum).ToString("F2")+" fixtureWidth="+(width.Left+width.Right).ToString("F2")+
                " publicPosts="+posts.Posts.Count+" lateralPosts="+PublicPostsAcrossStreetWidth);
        }
        private void VerifySharedGateOverlay()
        {
            var openField=typeof(DaggerfallWorkshop.DaggerfallCityGate).GetField("isOpen",
                System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            if(openField==null)throw new InvalidOperationException("Native gate state field unavailable.");
            foreach(var gate in origin.GetComponentsInChildren<DaggerfallWorkshop.DaggerfallCityGate>())
            {
                bool original=(bool)openField.GetValue(gate);
                try
                {
                    gate.SetOpen(true);Physics.SyncTransforms();
                    var filter=gate.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh)continue;
                    var midpoint=origin.InverseTransformPoint(gate.transform.TransformPoint(filter.sharedMesh.bounds.center));
                    foreach(var axis in new[]{Vector3.right,Vector3.forward})
                    {
                        var from=new RoutePoint(midpoint.x-axis.x*11,midpoint.z-axis.z*11);
                        var to=new RoutePoint(midpoint.x+axis.x*11,midpoint.z+axis.z*11);
                        if(StreetGateApproach(from)||StreetGateApproach(to)||!SegmentTouchesGate(from,to)||!LaneSegmentClearCore(from,to))continue;
                        // Deliberately warm static evidence before closing the gate.
                        if(!SharedLaneClear(from,to)||!sharedClearance.Clear(from,to))continue;
                        gate.SetOpen(false);Physics.SyncTransforms();
                        if(LaneSegmentClearCore(from,to)||SharedLaneClear(from,to))
                            throw new InvalidOperationException("Closed native gate reused cached open clearance.");
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHARED_GATE_OVERLAY_PASS: warm=open; check=closed; endpoints=outside-apron");return;
                    }
                }
                finally{gate.SetOpen(original);Physics.SyncTransforms();}
            }
            throw new InvalidOperationException("No native gate crossing available for shared-cache closure test.");
        }
    }
}
