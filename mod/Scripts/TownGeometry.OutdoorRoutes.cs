using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class TownGeometry
    {
        private const int MaximumStreetCrossSections=8192;
        private readonly Dictionary<string,LaneCrossSection> streetCrossSections=new Dictionary<string,LaneCrossSection>();
        private readonly Queue<string> streetCrossSectionOrder=new Queue<string>();
        private bool streetGridRead;
        private Vector3 streetGridZero,streetGridX,streetGridZ;
        private List<Bounds> streetGateAprons;
        public int OutdoorRouteInputPoints {get;private set;}
        public int OutdoorRouteOutputPoints {get;private set;}
        public int OutdoorRouteStraightPoints {get;private set;}
        public int OutdoorShortcutChecks {get;private set;}
        public int OutdoorRoundedCorners {get;private set;}
        public int StreetCrossSectionCount {get {return streetCrossSections.Count;}}
        public double WidestVerifiedStreet {get;private set;}
        public long StreetCrossSectionCacheHits {get;private set;}
        public long StreetCrossSectionCacheMisses {get;private set;}
        public double StreetCrossSectionSurveyMilliseconds {get;private set;}
        public double StreetCrossSectionSurveyMaximumMilliseconds {get;private set;}
        private void ResetOutdoorCrossSectionMeasurements()
        {StreetCrossSectionCacheHits=StreetCrossSectionCacheMisses=0;StreetCrossSectionSurveyMilliseconds=StreetCrossSectionSurveyMaximumMilliseconds=0;}

        private bool OutdoorFloorSupported(Vector3 point)
        {
            foreach(var hit in Physics.RaycastAll(point+Vector3.up*0.45f,Vector3.down,0.65f,~0,QueryTriggerInteraction.Ignore))
                if(IsStaticBlocker(hit.collider) && hit.normal.y>=0.7f && Mathf.Abs(hit.point.y-point.y)<=0.15f) return true;
            return false;
        }
        private bool OutdoorShortcutClear(SavedRoutePoint from,SavedRoutePoint to)
        {
            var a=new RoutePoint(from.X,from.Z);var b=new RoutePoint(to.X,to.Z);
            double dx=b.X-a.X,dz=b.Z-a.Z,distance=Math.Sqrt(dx*dx+dz*dz);
            if(distance>24.01) return false;
            int samples=Math.Max(1,(int)Math.Ceiling(distance/0.35));
            for(int i=0;i<=samples;i++)
            {
                var point=new RoutePoint(a.X+dx*i/samples,a.Z+dz*i/samples);
                // Require the whole body footprint to remain on the native
                // walkable map, not just the endpoints on opposite wall sides.
                foreach(var offset in new[] {Vector3.zero,Vector3.right*LaneBodyRadius,Vector3.left*LaneBodyRadius,
                    Vector3.forward*LaneBodyRadius,Vector3.back*LaneBodyRadius})
                    if(StreetWeight(ScenePoint(point)+offset)<=0) return false;
            }
            return LaneSegmentClear(a,b);
        }
        private int StreetWeight(Vector3 point)
        {
            // Native WorldToNavGridPosition clamps and scales by Width-1 although
            // NavGridToWorldPosition uses one full tile per index. Reconstruct the
            // native tile basis so paved width follows the actual RMB tile edge.
            // Store it in town-local space so floating-origin shifts remain safe.
            if(!streetGridRead)
            {
                var zero=SceneCell(new GridCell(0,0));
                streetGridZero=origin.InverseTransformPoint(zero);
                streetGridX=origin.InverseTransformVector(SceneCell(new GridCell(1,0))-zero);
                streetGridZ=origin.InverseTransformVector(SceneCell(new GridCell(0,1))-zero);
                streetGridRead=true;
            }
            var local=origin.InverseTransformPoint(point)-streetGridZero;
            float x=Vector3.Dot(local,streetGridX)/streetGridX.sqrMagnitude;
            float z=Vector3.Dot(local,streetGridZ)/streetGridZ.sqrMagnitude;
            if(x<0 || z<0 || x>=Width || z>=Height) return 0;
            return navigation.GetNavGridWeightLocal(Mathf.FloorToInt(x+0.0001f),Mathf.FloorToInt(z+0.0001f));
        }
        private bool PavedStreetPoint(RoutePoint point)
        {
            Vector3 scene=ScenePoint(point);
            foreach(var offset in new[] {Vector3.zero,Vector3.right*LaneBodyRadius,Vector3.left*LaneBodyRadius,
                Vector3.forward*LaneBodyRadius,Vector3.back*LaneBodyRadius})
            {int weight=StreetWeight(scene+offset);if(weight!=15 && weight!=4) return false;}
            return LaneSegmentClear(point,point);
        }
        private static string CrossSectionKey(RoutePoint point,RoutePoint normal)
        {
            // Reverse-route subdivision can differ by a few floating-point bits.
            // Quantize well below the 1cm margin removed from surveyed widths.
            return point.X.ToString("F4",CultureInfo.InvariantCulture)+","+point.Z.ToString("F4",CultureInfo.InvariantCulture)+
                ":"+normal.X.ToString("F6",CultureInfo.InvariantCulture)+","+normal.Z.ToString("F6",CultureInfo.InvariantCulture);
        }
        private LaneCrossSection CachedStreetCrossSection(RoutePoint point,RoutePoint normal)
        {
            if(StreetGateApproach(point)) return new LaneCrossSection(0,0);
            bool reverse=normal.X<0 || normal.X==0 && normal.Z<0;
            if(reverse) normal=new RoutePoint(-normal.X,-normal.Z);
            LaneCrossSection width;
            if(!streetCrossSections.TryGetValue(CrossSectionKey(point,normal),out width))
            {if(measureOutdoorMigration) StreetCrossSectionCacheMisses++;return new LaneCrossSection(0,0);}
            if(measureOutdoorMigration) StreetCrossSectionCacheHits++;
            return reverse ? new LaneCrossSection(width.Right,width.Left,-width.Center) : width;
        }
        private LaneCrossSection SurveyStreetCrossSection(RoutePoint point,RoutePoint normal)
        {
            if(StreetGateApproach(point)) return new LaneCrossSection(0,0);
            long started=measureOutdoorMigration ? Stopwatch.GetTimestamp() : 0;
            bool reverse=normal.X<0 || normal.X==0 && normal.Z<0;
            if(reverse) normal=new RoutePoint(-normal.X,-normal.Z);
            string key=CrossSectionKey(point,normal);LaneCrossSection width;
            if(!streetCrossSections.TryGetValue(key,out width))
            {
                if(measureOutdoorMigration) StreetCrossSectionCacheMisses++;
                var center=point;bool paved=PavedStreetPoint(point);
                int centerWeight=StreetWeight(ScenePoint(point));
                // Grid routes can lie exactly on a road tile boundary. Move at
                // most 1m to a supported, fully paved footprint before measuring.
                // Never use this to discover a separate road across open terrain.
                if(!paved && (centerWeight==15 || centerWeight==4))
                    for(int step=1;step<=2 && !paved;step++)
                    foreach(int sign in new[] {1,-1})
                    {
                        double shift=step*sign*0.5;
                        var candidate=new RoutePoint(point.X+normal.X*shift,point.Z+normal.Z*shift);
                        if(!PavedStreetPoint(candidate) || !LaneSegmentClear(point,candidate)) continue;
                        center=candidate;width.Center=shift;paved=true;break;
                    }
                if(paved && normal.X*normal.X+normal.Z*normal.Z>0.99)
                {
                    width.Left=SurveyStreetSide(center,normal,1);
                    width.Right=SurveyStreetSide(center,normal,-1);
                }
                while(streetCrossSections.Count>=MaximumStreetCrossSections) streetCrossSections.Remove(streetCrossSectionOrder.Dequeue());
                streetCrossSections.Add(key,width);streetCrossSectionOrder.Enqueue(key);
                WidestVerifiedStreet=Math.Max(WidestVerifiedStreet,width.Left+width.Right);
            }
            else if(measureOutdoorMigration) StreetCrossSectionCacheHits++;
            if(measureOutdoorMigration)
            {
                double elapsed=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;
                StreetCrossSectionSurveyMilliseconds+=elapsed;
                StreetCrossSectionSurveyMaximumMilliseconds=Math.Max(StreetCrossSectionSurveyMaximumMilliseconds,elapsed);
            }
            return reverse ? new LaneCrossSection(width.Right,width.Left,-width.Center) : width;
        }
        private bool StreetGateApproach(RoutePoint point)
        {
            if(streetGateAprons==null)
            {
                streetGateAprons=new List<Bounds>();
                foreach(var gate in origin.GetComponentsInChildren<DaggerfallCityGate>())
                {
                    var mesh=gate.GetComponent<MeshFilter>();
                    if(!mesh || !mesh.sharedMesh) continue;
                    var source=mesh.sharedMesh.bounds;var bounds=new Bounds();bool first=true;
                    foreach(float x in new[] {source.min.x,source.max.x})
                    foreach(float y in new[] {source.min.y,source.max.y})
                    foreach(float z in new[] {source.min.z,source.max.z})
                    {
                        var local=origin.InverseTransformPoint(gate.transform.TransformPoint(new Vector3(x,y,z)));
                        local.y=0;if(first) {bounds=new Bounds(local,Vector3.zero);first=false;}else bounds.Encapsulate(local);
                    }
                    bounds.Expand(new Vector3(6,0,6));streetGateAprons.Add(bounds);
                }
            }
            foreach(var bounds in streetGateAprons)
                if(point.X>=bounds.min.x && point.X<=bounds.max.x && point.Z>=bounds.min.z && point.Z<=bounds.max.z) return true;
            return false;
        }
        private double SurveyStreetSide(RoutePoint point,RoutePoint normal,int sign)
        {
            double width=0;var previous=point;
            for(int step=1;step<=12;step++)
            {
                double distance=step*0.5;
                var candidate=new RoutePoint(point.X+normal.X*sign*distance,point.Z+normal.Z*sign*distance);
                if(!PavedStreetPoint(candidate) || !LaneSegmentClear(previous,candidate)) break;
                width=distance;previous=candidate;
            }
            return Math.Max(0,width-0.01);
        }
        private IEnumerator FinishOutdoorRouteSteps(List<SavedRoutePoint> source,RouteResult result,Func<bool> valid)
        {
            var operation=OutdoorRouteSmoother.Begin(source,OutdoorShortcutClear);
            while(!operation.IsComplete)
            {if(!valid()) yield break;operation.Step(2);yield return null;}
            if(!valid()) yield break;
            var straight=operation.Points;
            var rounding=OutdoorRouteSmoother.BeginRounding(straight,OutdoorShortcutClear);
            while(!rounding.IsComplete)
            {if(!valid()) yield break;rounding.Step(2);yield return null;}
            var rounded=rounding.Points;
            var route=new List<RoutePoint>();foreach(var point in rounded) route.Add(new RoutePoint(point.X,point.Z));
            var controls=CorridorLaneRoutes.Controls(CorridorLaneRoutes.ApproachCenterline(route));
            for(int i=1;i<controls.Count-1 && controls.Count<=2048;i++)
            {
                if(!valid()) yield break;
                SurveyStreetCrossSection(controls[i],CorridorLaneRoutes.NormalAt(controls,i));
                yield return null;
            }
            OutdoorRouteInputPoints=source.Count;OutdoorRouteOutputPoints=rounded.Count;
            OutdoorRouteStraightPoints=straight.Count;
            OutdoorShortcutChecks=operation.WorkDone;OutdoorRoundedCorners=rounding.RoundedCorners;
            if(valid()) result.Points=rounded;
        }
    }
}
