using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
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
        { TryReportLegacyDialogueComplaintAt(rememberedPosition); }

        public bool TryReportLegacyDialogueComplaintAt(Vector3 rememberedPosition)
        {
            // Historic dialogue dislike has no verified crime source. Native
            // burglary/trespass reporting uses its own independent adapters.
            return false;
        }

        public bool TryReportConversationBoundaryAt(string receiptId, string personId,
            Vector3? rememberedPosition)
        { return TryReportConversationBoundaryAt(receiptId, personId, rememberedPosition, null); }

        private bool TryReportConversationBoundaryAt(string receiptId, string personId,
            Vector3? rememberedPosition, int? savedBuildingKey)
        {
            if (worldStore == null ||
                string.IsNullOrWhiteSpace(receiptId) || string.IsNullOrWhiteSpace(personId) ||
                rememberedPosition.HasValue &&
                    (float.IsNaN(rememberedPosition.Value.x) || float.IsInfinity(rememberedPosition.Value.x) ||
                     float.IsNaN(rememberedPosition.Value.y) || float.IsInfinity(rememberedPosition.Value.y) ||
                     float.IsNaN(rememberedPosition.Value.z) || float.IsInfinity(rememberedPosition.Value.z))) return false;
            double now = Now();
            worldStore.TryAbandonExpiredNpcConversationBoundaryReport(personId,now);
            var boundary = worldStore.ReadNpcConversationBoundary(personId, now);
            if (boundary == null || boundary.ReceiptId != receiptId ||
                boundary.SpeechKind!=NpcExperienceSpeechActKind.DirectThreat ||
                boundary.Action != ConversationSpeechBoundaryAction.EndAndReport ||
                (boundary.ReportStatus != NpcConversationBoundaryReportStatus.Pending &&
                 boundary.ReportStatus != NpcConversationBoundaryReportStatus.Started &&
                 boundary.ReportStatus != NpcConversationBoundaryReportStatus.Delivered)) return false;
            if (boundary.ReportStatus == NpcConversationBoundaryReportStatus.Delivered) return true;
            if (now >= boundary.ObservedAt + 1080d)
            {
                worldStore.TryMarkNpcConversationBoundaryReport(personId, receiptId,
                    NpcConversationBoundaryReportStatus.Abandoned);
                return false;
            }
            if (!OwnsTown() || geometry == null || Roster == null ||
                (savedBuildingKey.HasValue && (savedBuildingKey.Value < 0 || rememberedPosition.HasValue))) return false;
            // Native guards use Assault as the carrier; only its exact verbal receipt receives the court exception.
            PlayerEntity.Crimes charge = PlayerEntity.Crimes.Assault;
            var complaint = worldStore.GetDialogueComplaint(personId);
            if (complaint != null)
            {
                if (complaint.SourceReceiptId != receiptId || complaint.SettlementId != Roster.SettlementId ||
                    !rememberedPosition.HasValue) return false;
                Vector3 savedIncident = directory.transform.InverseTransformPoint(rememberedPosition.Value);
                if (Mathf.Abs(savedIncident.x - (float)complaint.CrimeX) > 0.05f ||
                    Mathf.Abs(savedIncident.y - (float)complaint.CrimeY) > 0.05f ||
                    Mathf.Abs(savedIncident.z - (float)complaint.CrimeZ) > 0.05f) return false;
            }
            if (boundary.ReportStatus == NpcConversationBoundaryReportStatus.Pending &&
                !worldStore.TryMarkNpcConversationBoundaryReport(personId, receiptId,
                    NpcConversationBoundaryReportStatus.Started)) return false;

            string incidentId = "dialogue:" + receiptId;
            try
            {
                Roster.Advance(now);
                List<GuardRecord> sent;
                if (rememberedPosition.HasValue)
                {
                    var target = Local(rememberedPosition.Value);
                    var routes = new Dictionary<string, List<SavedRoutePoint>>();
                    foreach (var guard in Roster.Guards)
                    {
                        if (!guard.IsAlive || guard.Duty != GuardDuty.Posted) continue;
                        var route = geometry.RouteBetweenPoints(Roster.Position(guard.Id, now), target);
                        if (route != null && route.Count >= 2) routes.Add(guard.Id, route);
                    }
                    sent = Roster.Dispatch(incidentId, now, routes,
                        Mathf.Clamp(MaximumResponders, 1, 8), 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale);
                }
                else
                {
                    int buildingKey;
                    if (savedBuildingKey.HasValue) buildingKey = savedBuildingKey.Value;
                    else
                    {
                        var interior = GameManager.Instance.PlayerEnterExit.Interior;
                        if (!GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding || !interior) return false;
                        buildingKey = interior.EntryDoor.buildingKey;
                    }
                    if (!Roster.BuildingApproaches.Exists(row => row.BuildingKey == buildingKey))
                        Roster.QueueBuildingReport(incidentId, buildingKey);
                    sent = Roster.DispatchBuilding(incidentId, buildingKey, now,
                        Mathf.Clamp(MaximumResponders, 1, 8), 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale);
                }
                var report = Roster.Reports.Find(row => row.Id == incidentId);
                if (report == null || sent == null || !sent.Exists(guard => guard != null && guard.IsAlive &&
                        guard.IncidentId == incidentId && report.GuardIds.Contains(guard.Id))) return false;
                var player = GameManager.Instance.PlayerEntity;
                // Native verbal charges are typed; a prior crime remains authoritative.
                bool verbalOnly=player.CrimeCommitted == PlayerEntity.Crimes.None;
                if (verbalOnly)
                    player.CrimeCommitted = charge;
                if (!worldStore.TryMarkNpcConversationBoundaryReport(personId, receiptId,
                    NpcConversationBoundaryReportStatus.Delivered)) return false;
                if(verbalOnly && player.CrimeCommitted==charge &&
                    !worldStore.TryBeginNpcVerbalCharge(personId,receiptId,Roster.SettlementId,now))
                {
                    player.CrimeCommitted=PlayerEntity.Crimes.None;
                    Debug.LogError("Verified verbal charge could not retain its court receipt; native Assault carrier withdrawn.");
                    return false;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_BOUNDARY_GUARDS_DISPATCHED: receipt=" + receiptId +
                    "; person=" + personId + "; charge=" + charge + "; responders=" + sent.Count);
                return true;
            }
            catch (Exception error)
            {
                Debug.LogWarning("Native dialogue boundary report remains pending: " + error.Message);
                return false;
            }
        }

        private void RetryConversationBoundaryReports(double now)
        {
            if (!OwnsTown() || worldStore == null || Roster == null) return;
            var started = worldStore.ReadStartedNpcConversationBoundaryReports(now);
            foreach (var report in new List<GuardReport>(Roster.Reports))
            {
                if (!report.Id.StartsWith("dialogue:", StringComparison.Ordinal) ||
                    !report.BuildingKey.HasValue || report.GuardIds.Count > 0) continue;
                string receiptId = report.Id.Substring("dialogue:".Length);
                NpcConversationBoundaryRecord boundary = started.Find(row => row.ReceiptId == receiptId);
                if (boundary == null)
                {
                    Roster.Reports.Remove(report);
                    continue;
                }
                if (now >= boundary.ObservedAt + 1080d)
                {
                    if (worldStore.TryMarkNpcConversationBoundaryReport(boundary.PersonId, receiptId,
                            NpcConversationBoundaryReportStatus.Abandoned))
                        Roster.Reports.Remove(report);
                    continue;
                }
                TryReportConversationBoundaryAt(receiptId, boundary.PersonId, null, report.BuildingKey);
            }
        }
    }
}
