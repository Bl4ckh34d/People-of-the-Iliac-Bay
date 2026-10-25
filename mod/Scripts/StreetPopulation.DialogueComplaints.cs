using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        public bool HasDialogueComplaint(string personId)
        {
            if(worldStore==null)return false;
            worldStore.TryRetireUnsupportedDialogueComplaint(personId,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            return worldStore.GetDialogueComplaint(personId)!=null;
        }

        public bool StartDialogueComplaint(ResidentActor actor, string personId)
        { return StartDialogueComplaint(actor, personId, null); }

        public bool StartDialogueComplaint(ResidentActor actor, string personId, string receiptId)
        {
            if (!actor || actor.Resident == null || !actor.Resident.IsAlive ||
                string.IsNullOrEmpty(personId) || actor.Resident.Id != personId || worldStore == null ||
                string.IsNullOrWhiteSpace(receiptId))
                return false;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if (receiptId != null)
            {
                worldStore.TryAbandonExpiredNpcConversationBoundaryReport(personId,now);
                var boundary = worldStore.ReadNpcConversationBoundary(personId, now);
                if (boundary == null || boundary.ReceiptId != receiptId ||
                    boundary.SpeechKind!=NpcExperienceSpeechActKind.DirectThreat ||
                    boundary.Action != ConversationSpeechBoundaryAction.EndAndReport ||
                    (boundary.ReportStatus != NpcConversationBoundaryReportStatus.Pending &&
                     boundary.ReportStatus != NpcConversationBoundaryReportStatus.Started)) return false;
            }
            var current = worldStore.GetDialogueComplaint(personId);
            if (current != null && receiptId != null)
                return current.SourceReceiptId == receiptId;
            if (!Ready || !directory || GameManager.Instance.PlayerEnterExit.IsPlayerInside ||
                geometry == null) return false;
            var guardPopulation = GetComponent<GuardPopulation>();
            Vector3 origin = actor.transform.position - Vector3.up;
            string guardId;
            Vector3 guardPosition;
            List<SavedRoutePoint> route;
            Vector3 local = directory.transform.InverseTransformPoint(origin);
            if (!guardPopulation || !guardPopulation.TryFindNearestComplaintGuard(origin, out guardId,
                    out guardPosition, out route))
            {
                if (receiptId == null) return false;
                guardId = null;
                route = new List<SavedRoutePoint> {
                    new SavedRoutePoint { X = local.x, Z = local.z },
                    new SavedRoutePoint { X = local.x, Z = local.z } };
            }
            Vector3 crime = directory.transform.InverseTransformPoint(GameManager.Instance.PlayerMotor.transform.position);
            if (receiptId != null && !worldStore.TryMarkNpcConversationBoundaryReport(personId,
                    receiptId, NpcConversationBoundaryReportStatus.Started)) return false;
            var complaint = new DialogueComplaintRecord {
                PersonId = personId, SourceReceiptId = receiptId, SettlementId = population.SettlementId, StartedAt = now,
                LastUpdatedAt = now, ExpiresAt = now + 1080, X = local.x, Y = local.y, Z = local.z,
                CrimeX = crime.x, CrimeY = crime.y, CrimeZ = crime.z,
                GuardId = guardId, NextWaypoint = 1, Route = route
            };
            if (receiptId == null) worldStore.SetDialogueComplaint(complaint);
            else if (!worldStore.TryStartReceiptDialogueComplaint(complaint)) return false;
            actor.SetConversationFacing(false);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_COMPLAINT_STARTED: resident=" + personId + "; guard=" + guardId);
            return true;
        }

        private bool PlaceDialogueComplaint(ResidentActor actor,double now)
        {
            if (!actor || actor.Resident == null || !directory || geometry == null || worldStore == null) return false;
            worldStore.TryRetireUnsupportedDialogueComplaint(actor.Resident.Id,now);
            var complaint = worldStore.GetDialogueComplaint(actor.Resident.Id);
            if (complaint == null) return false;
            if (!actor.Resident.IsAlive || now >= complaint.ExpiresAt)
            {
                if (!AbandonDialogueComplaint(complaint)) return true;
                worldStore.RemoveDialogueComplaint(complaint.PersonId);
                actor.Place(actor.transform.position - Vector3.up, actor.transform.forward, true);
                return true;
            }

            Vector3 position = directory.transform.TransformPoint(new Vector3((float)complaint.X,
                (float)complaint.Y, (float)complaint.Z));
            var crowd=actor.GetComponent<CrowdMotion>();
            if(crowd && crowd.HasPlacement)position=actor.transform.position-Vector3.up;
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

            float elapsed = Mathf.Clamp((float)((now - complaint.LastUpdatedAt) / DaggerfallUnity.Instance.WorldTime.TimeScale), 0, 0.25f);
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
            // Arrival is independent of whether guard dispatch can currently
            // succeed. Do not keep steering into/around the listener while the
            // exact report remains pending (for example, no route to its scene).
            bool atGuard=Vector3.Distance(position,guardPosition)<=2.25f;
            Vector3 move = atGuard ? Vector3.zero : Vector3.MoveTowards(position, destination, elapsed * 3.4f) - position;
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
            actor.Place(position, (atGuard?guardPosition:destination) - position, blocked || move.sqrMagnitude < 0.000001f,
                !blocked && move.sqrMagnitude > 0.000001f);
            // Collision/separation may stop short of the requested step. Only
            // the actual body may establish saved progress or guard arrival.
            position=actor.transform.position-Vector3.up;
            var saved = directory.transform.InverseTransformPoint(position);
            complaint.X = saved.x; complaint.Y = saved.y; complaint.Z = saved.z;
            worldStore.SetDialogueComplaint(complaint);

            Vector3 currentGuard;
            if (guards.TryGetComplaintGuardPosition(complaint.GuardId, out currentGuard) &&
                Vector3.Distance(position, currentGuard) <= 2.25f)
            {
                Vector3 incident = directory.transform.TransformPoint(new Vector3((float)complaint.CrimeX,
                    (float)complaint.CrimeY, (float)complaint.CrimeZ));
                bool dispatched = complaint.SourceReceiptId == null ?
                    guards.TryReportLegacyDialogueComplaintAt(incident) :
                    guards.TryReportConversationBoundaryAt(complaint.SourceReceiptId, complaint.PersonId, incident);
                if (dispatched)
                {
                    worldStore.RemoveDialogueComplaint(complaint.PersonId);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_COMPLAINT_ARRIVED: resident=" + complaint.PersonId +
                        "; guard=" + complaint.GuardId);
                }
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
                if(worldStore.TryRetireUnsupportedDialogueComplaint(complaint.PersonId,now))continue;
                if (now >= complaint.ExpiresAt ||
                    worldStore.ResolveResident(complaint.SettlementId, complaint.PersonId) == null ||
                    !worldStore.ResolveResident(complaint.SettlementId, complaint.PersonId).IsAlive)
                {
                    if (AbandonDialogueComplaint(complaint)) worldStore.RemoveDialogueComplaint(complaint.PersonId);
                    continue;
                }
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
                float elapsed = Mathf.Clamp((float)((now - complaint.LastUpdatedAt) / DaggerfallUnity.Instance.WorldTime.TimeScale), 0, 30f);
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
                    Vector3 incident = directory.transform.TransformPoint(new Vector3((float)complaint.CrimeX,
                        (float)complaint.CrimeY, (float)complaint.CrimeZ));
                    bool dispatched = complaint.SourceReceiptId == null ?
                        guards.TryReportLegacyDialogueComplaintAt(incident) :
                        guards.TryReportConversationBoundaryAt(complaint.SourceReceiptId, complaint.PersonId, incident);
                    if (dispatched)
                    {
                        worldStore.RemoveDialogueComplaint(complaint.PersonId);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_COMPLAINT_ARRIVED: resident=" + complaint.PersonId +
                            "; guard=" + complaint.GuardId + "; offscreen=true");
                    }
                    else worldStore.SetDialogueComplaint(complaint);
                }
                else worldStore.SetDialogueComplaint(complaint);
            }
        }

        private bool AbandonDialogueComplaint(DialogueComplaintRecord complaint)
        {
            if (complaint.SourceReceiptId == null) return true;
            var boundary = worldStore.ReadNpcConversationBoundary(complaint.PersonId,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            if (boundary == null || boundary.ReceiptId != complaint.SourceReceiptId ||
                boundary.ReportStatus == NpcConversationBoundaryReportStatus.Delivered ||
                boundary.ReportStatus == NpcConversationBoundaryReportStatus.Abandoned) return true;
            return worldStore.TryMarkNpcConversationBoundaryReport(complaint.PersonId,
                complaint.SourceReceiptId, NpcConversationBoundaryReportStatus.Abandoned);
        }
    }
}
