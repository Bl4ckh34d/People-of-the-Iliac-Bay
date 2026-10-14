using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public sealed partial class GuardRoster
    {
        public List<GuardRecord> DispatchOutdoorInvestigation(string incidentId, double now,
            IDictionary<string, List<SavedRoutePoint>> approaches, List<SavedRoutePoint> patrol,
            int maximumResponders, double speed, double timeScale)
        {
            Require(!string.IsNullOrWhiteSpace(incidentId) && Time(now) && approaches != null &&
                maximumResponders >= 0 && maximumResponders <= 256 && Finite(speed) && speed > 0 &&
                Finite(timeScale) && timeScale > 0, "Invalid outdoor investigation.");
            var loop = CopyRoute(patrol); double length = Length(loop);
            Require(loop.Count >= 3 && length > 0 && Distance(loop[0], loop[loop.Count - 1]) < 0.05,
                "Search patrol must return to its arrival point.");
            double duration = length / speed * timeScale;
            Require(Time(duration) && duration > 0 && duration <= UnwitnessedScene.OccupationSeconds,
                "Patrol must fit inside the search hour.");
            int laps = (int)Math.Min(1000, Math.Max(1, Math.Floor(UnwitnessedScene.OccupationSeconds / duration)));
            var existing = Reports.Find(r => r.Id == incidentId);
            Require(existing == null || (existing.Investigation && !existing.BuildingKey.HasValue &&
                SamePatrol(existing.SearchRoute, loop) && existing.SearchLaps == laps), "Investigation route cannot change.");
            if (existing != null && existing.GuardIds.Count > 0) return ReportGuards(existing);
            foreach (var route in approaches.Values)
            {
                var points = CopyRoute(route);
                Require(points.Count >= 2 && Distance(points[points.Count - 1], loop[0]) < 0.05 &&
                    Time(now + 2 * Length(points) / speed * timeScale + UnwitnessedScene.OccupationSeconds),
                    "Approach must reach search area with valid timing.");
            }
            var sent = Dispatch(incidentId, now, approaches, maximumResponders, speed, timeScale);
            var report = Reports.Find(r => r.Id == incidentId);
            report.Investigation = true; report.Entrance = CopyPoint(loop[0]);
            report.SearchRoute = loop; report.SearchLaps = laps;
            foreach (var guard in sent)
            {
                guard.SearchUntil = guard.ArrivesAt + UnwitnessedScene.OccupationSeconds;
                report.Arrivals.Add(new GuardArrivalEvidence { GuardId = guard.Id, ScheduledAt = guard.ArrivesAt });
            }
            return sent;
        }
        private static bool SamePatrol(List<SavedRoutePoint> a, List<SavedRoutePoint> b)
        {
            if (a == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (Distance(a[i], b[i]) > 0.000001) return false;
            return true;
        }
        private static SavedRoutePoint SearchPosition(GuardReport report, GuardRecord guard, double now)
        {
            if (report == null || report.SearchRoute == null) return CopyPoint(guard.Route[guard.Route.Count - 1]);
            double phase = (now - guard.ArrivesAt) * report.SearchLaps / UnwitnessedScene.OccupationSeconds;
            double along = (phase - Math.Floor(phase)) * Length(report.SearchRoute);
            for (int i = 1; i < report.SearchRoute.Count; i++)
            {
                var a = report.SearchRoute[i - 1]; var b = report.SearchRoute[i]; double distance = Distance(a, b);
                if (distance <= 0) continue;
                if (along <= distance) return new SavedRoutePoint {
                    X = a.X + (b.X - a.X) * along / distance, Z = a.Z + (b.Z - a.Z) * along / distance };
                along -= distance;
            }
            return CopyPoint(report.Entrance);
        }
    }
}
