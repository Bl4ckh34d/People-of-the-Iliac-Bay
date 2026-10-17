using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        public bool HasDialogueComplaint(string personId)
        { return worldStore != null && worldStore.GetDialogueComplaint(personId) != null; }

        public bool StartDialogueComplaint(ResidentActor actor, string personId)
        {
            if (!Ready || !directory || !actor || actor.Resident == null || !actor.Resident.IsAlive ||
                string.IsNullOrEmpty(personId) || actor.Resident.Id != personId ||
                GameManager.Instance.PlayerEnterExit.IsPlayerInside || geometry == null || worldStore == null)
                return false;
            var guardPopulation = GetComponent<GuardPopulation>();
            Vector3 origin = actor.transform.position - Vector3.up;
            string guardId;
            Vector3 guardPosition;
            List<SavedRoutePoint> route;
            if (!guardPopulation || !guardPopulation.TryFindNearestComplaintGuard(origin, out guardId, out guardPosition, out route))
                return false;
            Vector3 local = directory.transform.InverseTransformPoint(origin);
            Vector3 crime = directory.transform.InverseTransformPoint(GameManager.Instance.PlayerMotor.transform.position);
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            worldStore.SetDialogueComplaint(new DialogueComplaintRecord {
                PersonId = personId, SettlementId = population.SettlementId, StartedAt = now,
                LastUpdatedAt = now, ExpiresAt = now + 1080, X = local.x, Y = local.y, Z = local.z,
                CrimeX = crime.x, CrimeY = crime.y, CrimeZ = crime.z,
                GuardId = guardId, NextWaypoint = 1, Route = route
            });
            actor.SetConversationFacing(false);
            Debug.Log("LIVING_CITIES_DIALOGUE_COMPLAINT_STARTED: resident=" + personId + "; guard=" + guardId);
            return true;
        }

        private bool PlaceDialogueComplaint(ResidentActor actor)
        {
            if (!actor || actor.Resident == null || !directory || geometry == null || worldStore == null) return false;
            var complaint = worldStore.GetDialogueComplaint(actor.Resident.Id);
            if (complaint == null) return false;
            if (!actor.Resident.IsAlive || DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() >= complaint.ExpiresAt)
            {
                worldStore.RemoveDialogueComplaint(complaint.PersonId);
                actor.Place(actor.transform.position - Vector3.up, actor.transform.forward, true);
                return true;
            }

            Vector3 position = directory.transform.TransformPoint(new Vector3((float)complaint.X,
                (float)complaint.Y, (float)complaint.Z));
            var guards = GetComponent<GuardPopulation>();
            Vector3 guardPosition;
            if (!guards || !guards.TryGetComplaintGuardPosition(complaint.GuardId, out guardPosition))
            {
                string newGuard;
                List<SavedRoutePoint> newRoute;
                if (!guards || !guards.TryFindNearestComplaintGuard(position, out newGuard, out guardPosition, out newRoute))
                {
                    actor.Place(position, actor.transform.forward, true);
                    return true;
                }
                complaint.GuardId = newGuard;
                complaint.Route = newRoute;
                complaint.NextWaypoint = 1;
            }
            else if (RouteTargetMoved(complaint.Route, guardPosition))
            {
                string newGuard;
                Vector3 newGuardPosition;
                List<SavedRoutePoint> newRoute;
                if (guards.TryFindNearestComplaintGuard(position, out newGuard, out newGuardPosition, out newRoute))
                {
                    complaint.GuardId = newGuard; guardPosition = newGuardPosition;
                    complaint.Route = newRoute; complaint.NextWaypoint = 1;
                }
            }

            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            float elapsed = Mathf.Clamp((float)((now - complaint.LastUpdatedAt) / 12d), 0, 0.25f);
            complaint.LastUpdatedAt = now;
            while (complaint.NextWaypoint < complaint.Route.Count)
            {
                var targetPoint = complaint.Route[complaint.NextWaypoint];
                Vector3 target = geometry.ScenePoint(new RoutePoint((float)targetPoint.X, (float)targetPoint.Z));
                target.y = position.y;
                if (Vector3.Distance(position, target) > 0.25f) break;
                complaint.NextWaypoint++;
            }
            Vector3 destination = complaint.NextWaypoint < complaint.Route.Count ?
                geometry.ScenePoint(new RoutePoint((float)complaint.Route[complaint.NextWaypoint].X,
                    (float)complaint.Route[complaint.NextWaypoint].Z)) : guardPosition;
            destination.y = position.y;
            Vector3 move = Vector3.MoveTowards(position, destination, elapsed * 3.4f) - position;
            bool blocked = false;
            if (move.sqrMagnitude > 0.000001f)
                foreach (var hit in Physics.CapsuleCastAll(position + Vector3.up * 0.3f, position + Vector3.up * 1.65f,
                    0.22f, move.normalized, move.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    if (CivilianResponses.Blocker(hit.collider)) { blocked = true; break; }
            if (!blocked && move.sqrMagnitude > 0.000001f)
            {
                Vector3 proposed = position + move;
                bool supported = false;
                foreach (var hit in Physics.RaycastAll(proposed + Vector3.up * 0.4f, Vector3.down, 0.8f, ~0, QueryTriggerInteraction.Ignore))
                    if (CivilianResponses.Blocker(hit.collider) && hit.normal.y > 0.85f && Mathf.Abs(hit.point.y - position.y) < 0.3f)
                    { proposed.y = hit.point.y; supported = true; break; }
                if (supported) position = proposed; else blocked = true;
            }
            var saved = directory.transform.InverseTransformPoint(position);
            complaint.X = saved.x; complaint.Y = saved.y; complaint.Z = saved.z;
            worldStore.SetDialogueComplaint(complaint);
            actor.Place(position, destination - position, blocked || move.sqrMagnitude < 0.000001f,
                !blocked && move.sqrMagnitude > 0.000001f);

            Vector3 currentGuard;
            if (guards.TryGetComplaintGuardPosition(complaint.GuardId, out currentGuard) &&
                Vector3.Distance(position, currentGuard) <= 2.25f)
            {
                worldStore.RemoveDialogueComplaint(complaint.PersonId);
                guards.ReportDialogueComplaintAt(directory.transform.TransformPoint(new Vector3((float)complaint.CrimeX,
                    (float)complaint.CrimeY, (float)complaint.CrimeZ)));
                Debug.Log("LIVING_CITIES_DIALOGUE_COMPLAINT_ARRIVED: resident=" + complaint.PersonId +
                    "; guard=" + complaint.GuardId);
            }
            return true;
        }

        private bool RouteTargetMoved(List<SavedRoutePoint> route, Vector3 guardPosition)
        {
            if (route == null || route.Count < 2) return true;
            Vector3 endpoint = geometry.ScenePoint(new RoutePoint((float)route[route.Count - 1].X,
                (float)route[route.Count - 1].Z));
            return (endpoint - guardPosition).sqrMagnitude > 2.25f * 2.25f;
        }

        private void AdvanceOffscreenDialogueComplaints(double now)
        {
            if (worldStore == null || population == null || geometry == null || !GameManager.Instance.PlayerEnterExit.IsPlayerInside)
                return;
            var guards = GetComponent<GuardPopulation>();
            foreach (var complaint in worldStore.DialogueComplaints())
            {
                if (complaint.SettlementId != population.SettlementId) continue;
                if (now >= complaint.ExpiresAt || worldStore.ResolveResident(complaint.SettlementId, complaint.PersonId) == null ||
                    !worldStore.ResolveResident(complaint.SettlementId, complaint.PersonId).IsAlive)
                { worldStore.RemoveDialogueComplaint(complaint.PersonId); continue; }
                var resident = worldStore.ResolveResident(complaint.SettlementId, complaint.PersonId);
                Vector3 position = directory.transform.TransformPoint(new Vector3((float)complaint.X, (float)complaint.Y, (float)complaint.Z));
                Vector3 guardPosition;
                if (!guards || !guards.TryGetComplaintGuardPosition(complaint.GuardId, out guardPosition))
                {
                    string guardId;
                    List<SavedRoutePoint> route;
                    if (!guards || !guards.TryFindNearestComplaintGuard(position, out guardId, out guardPosition, out route)) continue;
                    complaint.GuardId = guardId; complaint.Route = route; complaint.NextWaypoint = 1;
                }
                else if (RouteTargetMoved(complaint.Route, guardPosition))
                {
                    string guardId;
                    List<SavedRoutePoint> route;
                    Vector3 movedGuard;
                    if (guards.TryFindNearestComplaintGuard(position, out guardId, out movedGuard, out route))
                    { complaint.GuardId = guardId; guardPosition = movedGuard; complaint.Route = route; complaint.NextWaypoint = 1; }
                }
                float elapsed = Mathf.Clamp((float)((now - complaint.LastUpdatedAt) / 12d), 0, 30f);
                complaint.LastUpdatedAt = now;
                while (complaint.NextWaypoint < complaint.Route.Count)
                {
                    var p = complaint.Route[complaint.NextWaypoint];
                    Vector3 waypoint = geometry.ScenePoint(new RoutePoint((float)p.X, (float)p.Z));
                    waypoint.y = position.y;
                    if (Vector3.Distance(position, waypoint) > 0.25f) break;
                    complaint.NextWaypoint++;
                }
                Vector3 next = complaint.NextWaypoint < complaint.Route.Count ?
                    geometry.ScenePoint(new RoutePoint((float)complaint.Route[complaint.NextWaypoint].X,
                        (float)complaint.Route[complaint.NextWaypoint].Z)) : guardPosition;
                next.y = position.y;
                position = Vector3.MoveTowards(position, next, elapsed * 3.4f);
                var local = directory.transform.InverseTransformPoint(position);
                complaint.X = local.x; complaint.Y = local.y; complaint.Z = local.z;
                if (Vector3.Distance(position, guardPosition) <= 2.25f)
                {
                    worldStore.RemoveDialogueComplaint(complaint.PersonId);
                    guards.ReportDialogueComplaintAt(directory.transform.TransformPoint(new Vector3((float)complaint.CrimeX,
                        (float)complaint.CrimeY, (float)complaint.CrimeZ)));
                    Debug.Log("LIVING_CITIES_DIALOGUE_COMPLAINT_ARRIVED: resident=" + complaint.PersonId +
                        "; guard=" + complaint.GuardId + "; offscreen=true");
                }
                else worldStore.SetDialogueComplaint(complaint);
            }
        }
    }
}
