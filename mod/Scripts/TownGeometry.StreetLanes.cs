using System;
using System.Collections.Generic;
using System.Diagnostics;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class TownGeometry
    {
        private const float LaneBodyRadius = 0.24f;
        private const float LaneBodyHeight = 1.55f;
        private readonly Dictionary<string,List<RoutePoint>> preparedStreetLanes=new Dictionary<string,List<RoutePoint>>();
        private readonly Queue<string> preparedStreetLaneOrder=new Queue<string>();
        private bool measureOutdoorMigration;
        public long PreparedLaneCacheHits {get;private set;}
        public long PreparedLaneCacheMisses {get;private set;}
        public long CorridorStepCalls {get;private set;}
        public long CorridorClearanceChecks {get;private set;}
        public double CorridorStepMilliseconds {get;private set;}
        public double CorridorStepMaximumMilliseconds {get;private set;}
        public double CorridorClearanceMilliseconds {get;private set;}
        public double CorridorClearanceMaximumMilliseconds {get;private set;}
        public void BeginOutdoorMigrationMeasurements()
        {
            measureOutdoorMigration=true;PreparedLaneCacheHits=PreparedLaneCacheMisses=0;CorridorStepCalls=CorridorClearanceChecks=0;
            CorridorStepMilliseconds=CorridorStepMaximumMilliseconds=CorridorClearanceMilliseconds=CorridorClearanceMaximumMilliseconds=0;
            ResetOutdoorCrossSectionMeasurements();
        }
        public void EndOutdoorMigrationMeasurements() {measureOutdoorMigration=false;}
        private static string StreetLaneKey(IList<RoutePoint> route,uint seed)
        {
            var key=new System.Text.StringBuilder(seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach(var point in route) key.Append(';').Append(point.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(point.Z.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            return key.ToString();
        }

        public List<RoutePoint> OffsetStreetRoute(IList<RoutePoint> centerline, uint residentSeed)
        {
            if (centerline == null || centerline.Count < 2) return centerline == null ? new List<RoutePoint>() : new List<RoutePoint>(centerline);
            // Width discovery runs in the sliced route survey. Restoring a saved
            // routine can reuse only verified cache entries until that survey ends.
            List<RoutePoint> prepared;
            return preparedStreetLanes.TryGetValue(StreetLaneKey(centerline,residentSeed),out prepared) ?
                new List<RoutePoint>(prepared) : new List<RoutePoint>(centerline);
        }

        public System.Collections.IEnumerator PrepareStreetRouteSteps(IList<SavedRoutePoint> source,uint seed,Func<bool> valid)
        {
            if(UsesNativeNavigation)yield break;
            var route=new List<RoutePoint>();foreach(var point in source) route.Add(new RoutePoint(point.X,point.Z));
            if(route.Count<2) yield break;
            string key=StreetLaneKey(route,seed);
            if(preparedStreetLanes.ContainsKey(key)) {if(measureOutdoorMigration) PreparedLaneCacheHits++;yield break;}
            if(measureOutdoorMigration) PreparedLaneCacheMisses++;
            var line=CorridorLaneRoutes.ApproachCenterline(route);var controls=CorridorLaneRoutes.Controls(line);
            for(int i=1;i<controls.Count-1 && controls.Count<=2048;i++)
            {if(!valid()) yield break;SurveyStreetCrossSection(controls[i],CorridorLaneRoutes.NormalAt(controls,i));yield return null;}
            var operation=CorridorLaneRoutes.Begin(line,seed,CachedStreetCrossSection,LaneSegmentClear);
            while(!operation.IsComplete)
            {
                if(!valid()) yield break;
                long started=measureOutdoorMigration ? Stopwatch.GetTimestamp() : 0;
                operation.Step(2);
                if(measureOutdoorMigration)
                {
                    double elapsed=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;
                    CorridorStepCalls++;CorridorStepMilliseconds+=elapsed;
                    CorridorStepMaximumMilliseconds=Math.Max(CorridorStepMaximumMilliseconds,elapsed);
                }
                yield return null;
            }
            var points=operation.Points;
            if(route.Count>=3) {points.Insert(0,route[0]);points.Add(route[route.Count-1]);}
            // Width selection and local collision contraction can introduce new
            // turns after the shared centerline was rounded. Validate soft turns
            // on the final personal geometry that actors will actually follow.
            var personal=new List<SavedRoutePoint>();
            foreach(var point in points) personal.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
            var rounding=OutdoorRouteSmoother.BeginRounding(personal,OutdoorShortcutClear);
            while(!rounding.IsComplete)
            {if(!valid()) yield break;rounding.Step(2);yield return null;}
            points=new List<RoutePoint>();
            foreach(var point in rounding.Points) points.Add(new RoutePoint(point.X,point.Z));
            if(!valid()) yield break;
            while(preparedStreetLanes.Count>=2048) preparedStreetLanes.Remove(preparedStreetLaneOrder.Dequeue());
            preparedStreetLanes.Add(key,points);preparedStreetLaneOrder.Enqueue(key);
        }

        private bool LaneSegmentClear(RoutePoint from, RoutePoint to)
        {
            if(!measureOutdoorMigration) return SharedLaneClear(from,to);
            long started=Stopwatch.GetTimestamp();
            try {return SharedLaneClear(from,to);}
            finally
            {
                double elapsed=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;
                CorridorClearanceChecks++;CorridorClearanceMilliseconds+=elapsed;
                CorridorClearanceMaximumMilliseconds=Math.Max(CorridorClearanceMaximumMilliseconds,elapsed);
            }
        }
        private bool LaneSegmentClearCore(RoutePoint from, RoutePoint to)
        {
            Vector3 a = ScenePoint(from), b = ScenePoint(to);
            if(FurnitureSeatCatalog.Blocks(a,b,CrowdMotion.BodyRadius)) return false;
            Vector3 delta = b - a;
            float distance = delta.magnitude;
            if (distance > 24.01f) return false;

            // Keep offsets on the surveyed walk surface, including across terrain seams.
            int samples = Math.Max(1, Mathf.CeilToInt(distance / 0.5f));
            Vector3 previous = a;
            for (int i = 0; i <= samples; i++)
            {
                Vector3 point = Vector3.Lerp(a, b, i / (float)samples);
                Vector3 ground = ScenePoint(new RoutePoint(
                    from.X + (to.X - from.X) * i / samples,
                    from.Z + (to.Z - from.Z) * i / samples));
                if (Mathf.Abs(point.y - ground.y) > 0.15f) return false;
                if(!OutdoorFloorSupported(point)) return false;
                // Lift the capsule slightly so its lower hemisphere clears the road surface.
                Vector3 bottom = point + Vector3.up * 0.48f;
                Vector3 top = point + Vector3.up * (LaneBodyHeight - LaneBodyRadius);
                foreach (var collider in Physics.OverlapCapsule(bottom, top, LaneBodyRadius, ~0, QueryTriggerInteraction.Ignore))
                    if (IsStaticBlocker(collider)) return false;
                if (i > 0)
                {
                    Vector3 previousBottom = previous + Vector3.up * 0.48f;
                    Vector3 previousTop = previous + Vector3.up * (LaneBodyHeight - LaneBodyRadius);
                    Vector3 direction = point - previous;
                    float span = direction.magnitude;
                    if (span > 0.001f)
                        foreach (var hit in Physics.CapsuleCastAll(previousBottom, previousTop, LaneBodyRadius,
                            direction / span, span, ~0, QueryTriggerInteraction.Ignore))
                            if (IsStaticBlocker(hit.collider)) return false;
                }
                previous = point;
            }
            return true;
        }
    }
}
