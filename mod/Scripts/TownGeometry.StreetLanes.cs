using System;
using System.Collections.Generic;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class TownGeometry
    {
        private const double ResidentLaneWidth = 0.8;
        private const float LaneBodyRadius = 0.24f;
        private const float LaneBodyHeight = 1.55f;

        public List<RoutePoint> OffsetStreetRoute(IList<RoutePoint> centerline, uint residentSeed)
        {
            if (centerline == null || centerline.Count < 2) return centerline == null ? new List<RoutePoint>() : new List<RoutePoint>(centerline);
            return StreetLaneRoutes.Offset(centerline, residentSeed, ResidentLaneWidth, 2.0, LaneSegmentClear);
        }

        private bool LaneSegmentClear(RoutePoint from, RoutePoint to)
        {
            Vector3 a = ScenePoint(from), b = ScenePoint(to);
            Vector3 delta = b - a;
            float distance = delta.magnitude;
            if (distance < 0.001f) return false;

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
