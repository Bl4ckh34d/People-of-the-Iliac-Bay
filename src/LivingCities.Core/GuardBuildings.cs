using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class GuardApproachRoute
    {
        public string GuardId;
        public List<SavedRoutePoint> Points = new List<SavedRoutePoint>();
    }
    [Serializable]
    public sealed class GuardBuildingApproach
    {
        public int BuildingKey;
        public SavedRoutePoint Entrance;
        public List<GuardApproachRoute> Routes = new List<GuardApproachRoute>();
    }
    public sealed partial class GuardRoster
    {
        public void QueueBuildingReport(string incidentId, int buildingKey)
        {
            Require(!string.IsNullOrWhiteSpace(incidentId) && buildingKey >= 0, "Invalid queued building report.");
            var report = Reports.Find(r => r.Id == incidentId);
            Require(report == null || report.BuildingKey == buildingKey && !report.Investigation,
                "Queued report cannot change destination or policy.");
            if (report == null) Reports.Add(new GuardReport {
                Id = incidentId, BuildingKey = buildingKey, AwaitingApproach = true });
        }
        // Exterior adapters survey collision-checked routes before the scene unloads.
        // A missing route is unavailable; never substitute straight-line travel.
        public void CacheBuildingApproach(int buildingKey, SavedRoutePoint entrance,
            IDictionary<string, List<SavedRoutePoint>> routes)
        {
            Require(routes != null, "Missing approach routes.");
            var candidate = new GuardBuildingApproach { BuildingKey = buildingKey, Entrance = entrance };
            foreach (var pair in routes)
                candidate.Routes.Add(new GuardApproachRoute { GuardId = pair.Key, Points = pair.Value });
            var validated = ValidateApproach(candidate);
            int index = BuildingApproaches.FindIndex(a => a.BuildingKey == buildingKey);
            if (index < 0) BuildingApproaches.Add(validated); else BuildingApproaches[index] = validated;
        }
        public List<GuardRecord> DispatchBuilding(string incidentId, int buildingKey, double now,
            int maximumResponders, double speed, double timeScale, bool investigation = false, ISet<string> informedGuards = null)
        {
            Require(buildingKey >= 0 && !string.IsNullOrWhiteSpace(incidentId) && Time(now) &&
                maximumResponders >= 0 && maximumResponders <= 256 && Finite(speed) && speed > 0 &&
                Finite(timeScale) && timeScale > 0, "Invalid building dispatch.");
            var existing = Reports.Find(r => r.Id == incidentId);
            Require(existing == null || existing.BuildingKey == buildingKey, "Report cannot change its destination.");
            Require(existing == null || existing.Investigation == investigation, "Report cannot change its investigation policy.");
            if (existing != null && existing.GuardIds.Count > 0) return ReportGuards(existing);
            var approach = BuildingApproaches.Find(a => a.BuildingKey == buildingKey);
            if (approach == null) return new List<GuardRecord>();
            approach = ValidateApproach(approach);
            Require(existing == null || existing.AwaitingApproach || Distance(existing.Entrance, approach.Entrance) < 0.05,
                "Pending report entrance cannot change.");
            var routes = new Dictionary<string, List<SavedRoutePoint>>();
            foreach (var guard in Guards)
            {
                if (!guard.IsAlive || (informedGuards != null && !informedGuards.Contains(guard.Id))) continue;
                // Recruits inherit the post, not the deceased guard's identity.
                var route = approach.Routes.Find(r => Distance(r.Points[0], guard.Post) < 0.05);
                if (route != null) routes.Add(guard.Id, route.Points);
            }
            var sent = Dispatch(incidentId, now, routes, maximumResponders, speed, timeScale);
            var report = Reports.Find(r => r.Id == incidentId);
            report.BuildingKey = buildingKey; report.Entrance = CopyPoint(approach.Entrance);
            report.AwaitingApproach = false;
            report.Investigation = investigation;
            if (investigation) foreach (var guard in sent)
            {
                guard.SearchUntil = guard.ArrivesAt + UnwitnessedScene.OccupationSeconds;
                report.Arrivals.Add(new GuardArrivalEvidence { GuardId = guard.Id, ScheduledAt = guard.ArrivesAt });
            }
            return sent;
        }
        private GuardBuildingApproach ValidateApproach(GuardBuildingApproach source)
        {
            Require(source != null && source.BuildingKey >= 0 && source.Routes != null, "Invalid building approach.");
            var result = new GuardBuildingApproach { BuildingKey = source.BuildingKey, Entrance = CopyPoint(source.Entrance) };
            var ids = new HashSet<string>();
            foreach (var route in source.Routes)
            {
                Require(route != null && route.GuardId != null && ids.Add(route.GuardId), "Duplicate approach guard.");
                var guard = Guards.Find(g => g.Id == route.GuardId);
                var points = CopyRoute(route.Points);
                Require(guard != null && points.Count >= 2 && Distance(points[0], guard.Post) < 0.05 &&
                    Distance(points[points.Count - 1], result.Entrance) < 0.05, "Approach must join guard post to building entrance.");
                result.Routes.Add(new GuardApproachRoute { GuardId = route.GuardId, Points = points });
            }
            return result;
        }
        private void CopyBuildingApproaches(List<GuardBuildingApproach> source)
        {
            // Additive schema-6 field: earlier saves have no surveyed approaches.
            if (source == null) return;
            var keys = new HashSet<int>();
            foreach (var approach in source)
            {
                var copy = ValidateApproach(approach);
                Require(keys.Add(copy.BuildingKey), "Duplicate building approach.");
                BuildingApproaches.Add(copy);
            }
        }
    }
}
