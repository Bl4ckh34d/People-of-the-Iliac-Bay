using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class GuardComplaintTarget
    {
        public string GuardId;
        public Vector3 Position;
    }

    public sealed partial class GuardPopulation
    {
        public bool TryFindNearestComplaintGuard(Vector3 origin, out string guardId, out Vector3 position,
            out List<SavedRoutePoint> route)
        {
            guardId = null;
            position = Vector3.zero;
            route = null;
            // Retargeting also runs while the player is inside a building. The
            // exterior town geometry and directory remain the coordinate frame
            // for persisted street routes, and OwnsTown() validates that frame.
            if (!OwnsTown() || geometry == null || Roster == null)
                return false;
            double now = Now();
            Roster.Advance(now);
            Vector3 localOrigin = directory.transform.InverseTransformPoint(origin);
            float nearest = float.PositiveInfinity;
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive || guard.Duty != GuardDuty.Posted || guard.Interior != null || guard.Refuge != null)
                    continue;
                var point = Roster.Position(guard.Id, now);
                Vector3 candidate = geometry.ScenePoint(new RoutePoint((float)point.X, (float)point.Z));
                float distance = (candidate - origin).sqrMagnitude;
                if (distance >= nearest) continue;
                var reachable = geometry.RouteBetweenPoints(new SavedRoutePoint { X = localOrigin.x, Z = localOrigin.z },
                    new SavedRoutePoint { X = point.X, Z = point.Z });
                if ((reachable == null || reachable.Count < 2) && distance > 2.25f * 2.25f) continue;
                if (reachable == null || reachable.Count < 2)
                    reachable = new List<SavedRoutePoint> { new SavedRoutePoint { X = localOrigin.x, Z = localOrigin.z },
                        new SavedRoutePoint { X = point.X, Z = point.Z } };
                nearest = distance;
                guardId = guard.Id;
                position = candidate;
                route = reachable;
            }
            return guardId != null && route != null;
        }

        public bool TryGetComplaintGuardPosition(string guardId, out Vector3 position)
        {
            position = Vector3.zero;
            if (!OwnsTown() || geometry == null || Roster == null || string.IsNullOrEmpty(guardId)) return false;
            var guard = Roster.Guards.Find(candidate => candidate.Id == guardId);
            if (guard == null || !guard.IsAlive || guard.Duty != GuardDuty.Posted || guard.Interior != null || guard.Refuge != null)
                return false;
            double now = Now();
            var point = Roster.Position(guardId, now);
            position = geometry.ScenePoint(new RoutePoint((float)point.X, (float)point.Z));
            return true;
        }

        public void ReportDialogueComplaintAt(Vector3 rememberedPosition)
        {
            if (!OwnsTown() || geometry == null || Roster == null) return;
            var player = GameManager.Instance.PlayerEntity;
            if (player.CrimeCommitted == PlayerEntity.Crimes.None)
                player.CrimeCommitted = PlayerEntity.Crimes.Trespassing;
            ReportNative(true, rememberedPosition);
        }
    }
}
