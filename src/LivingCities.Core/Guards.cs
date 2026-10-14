using System;
using System.Collections.Generic;
using System.Globalization;

namespace LivingCities.Core
{
    public enum SettlementScale { Hamlet, Town, City }
    public enum GuardDuty { Posted, Responding, Searching, Returning, Dead, Engaged }
    [Serializable]
    public sealed class GuardRecord
    {
        public string Id, IncidentId, ReplacedById;
        public uint AppearanceSeed;
        public bool IsAlive = true;
        public SavedRoutePoint Post, DeathPosition;
        public SavedRoutePoint EngagementPosition;
        public SavedRoutePoint LastSeenSuspect;
        public GuardInteriorState Interior;
        public double HealthFraction = 1;
        public GuardDuty Duty;
        public List<SavedRoutePoint> Route = new List<SavedRoutePoint>();
        public double DepartedAt, ArrivesAt, SearchUntil, DiedAt;
    }
    [Serializable]
    public sealed class GuardArrivalEvidence
    {
        public string GuardId;
        public double ScheduledAt;
        public bool Arrived;
    }
    [Serializable]
    public sealed class GuardReport
    {
        public List<GuardArrivalEvidence> Arrivals = new List<GuardArrivalEvidence>();
        public bool Investigation;
        public bool HasArrival;
        public double FirstArrivalAt;
        public string Id;
        public int? BuildingKey;
        public SavedRoutePoint Entrance;
        public List<string> GuardIds = new List<string>();
    }
    [Serializable]
    public sealed partial class GuardRoster
    {
        public int SettlementId, Target, NextIdentity;
        public uint Seed;
        public double LastRecruitment = -1;
        public List<GuardRecord> Guards = new List<GuardRecord>();
        public List<GuardReport> Reports = new List<GuardReport>();
        public List<GuardBuildingApproach> BuildingApproaches = new List<GuardBuildingApproach>();
        public static int TargetCount(SettlementScale scale, int residents)
        {
            Require(residents >= 0 && (scale == SettlementScale.Hamlet || scale == SettlementScale.Town || scale == SettlementScale.City), "Invalid settlement population.");
            if (scale == SettlementScale.Hamlet && residents < 40) return 0;
            return Math.Min(256, Math.Max(scale == SettlementScale.City ? 8 : 4, (int)Math.Ceiling(residents * 0.05)));
        }
        public static GuardRoster Create(int settlementId, uint seed, int count, IList<SavedRoutePoint> posts)
        {
            Require(settlementId >= 0 && count >= 0 && count <= 256 && posts != null && (count == 0 || posts.Count > 0), "Invalid garrison allocation.");
            foreach (var post in posts) CopyPoint(post);
            var roster = new GuardRoster { SettlementId = settlementId, Seed = seed, Target = count };
            for (int i = 0; i < count; i++) roster.Guards.Add(roster.NewGuard(posts[i % posts.Count]));
            return roster;
        }
        private GuardRecord NewGuard(SavedRoutePoint post)
        {
            int identity = NextIdentity++;
            return new GuardRecord { Id = string.Format(CultureInfo.InvariantCulture, "g:{0}:n:{1}", SettlementId, identity),
                AppearanceSeed = StableSeed.Combine(Seed, (uint)SettlementId, (uint)identity, 0x67756172), Post = CopyPoint(post) };
        }
        public List<GuardRecord> Dispatch(string incidentId, double now, IDictionary<string, List<SavedRoutePoint>> routes,
            int maximumResponders, double metresPerRealSecond, double gameSecondsPerRealSecond)
        {
            Require(!string.IsNullOrWhiteSpace(incidentId) && Time(now) && routes != null && maximumResponders >= 0 && maximumResponders <= 256 &&
                Finite(metresPerRealSecond) && metresPerRealSecond > 0 && Finite(gameSecondsPerRealSecond) && gameSecondsPerRealSecond > 0, "Invalid guard dispatch.");
            var report = Reports.Find(r => r.Id == incidentId);
            if (report != null && report.GuardIds.Count > 0) return ReportGuards(report);
            // Validate and copy all supplied routes before assigning any guard.
            var candidates = new List<GuardRecord>();
            var copies = new Dictionary<string, List<SavedRoutePoint>>();
            var lengths = new Dictionary<string, double>();
            foreach (var pair in routes)
            {
                var guard = Guards.Find(g => g.Id == pair.Key);
                Require(guard != null, "Route belongs to an unknown guard.");
                var route = CopyRoute(pair.Value);
                Require(route.Count >= 2, "Dispatch needs a reachable route.");
                double length = Length(route);
                double seconds = length / metresPerRealSecond * gameSecondsPerRealSecond;
                Require(Time(now + seconds + 300 + seconds), "Guard route travel time overflow.");
                copies.Add(pair.Key, route); lengths.Add(pair.Key, length);
            }
            var preview = CopyValidated(this);
            preview.Advance(now);
            foreach (var guard in preview.Guards)
            {
                if (!guard.IsAlive || guard.Duty != GuardDuty.Posted || !copies.ContainsKey(guard.Id)) continue;
                Require(Distance(guard.Post, copies[guard.Id][0]) < 0.05, "Route must begin at the guard's actual position.");
                candidates.Add(Find(guard.Id));
            }
            Advance(now);
            candidates.Sort((a, b) => { int order = lengths[a.Id].CompareTo(lengths[b.Id]); return order != 0 ? order : string.CompareOrdinal(a.Id, b.Id); });
            if (report == null) { report = new GuardReport { Id = incidentId }; Reports.Add(report); }
            for (int i = 0; i < Math.Min(maximumResponders, candidates.Count); i++)
            {
                var guard = candidates[i];
                guard.Route = copies[guard.Id]; guard.IncidentId = incidentId;
                guard.DepartedAt = now; guard.ArrivesAt = now + lengths[guard.Id] / metresPerRealSecond * gameSecondsPerRealSecond;
                guard.SearchUntil = guard.ArrivesAt + 300; guard.Duty = GuardDuty.Responding;
                report.GuardIds.Add(guard.Id);
            }
            return ReportGuards(report);
        }
        private List<GuardRecord> ReportGuards(GuardReport report)
        {
            var result = new List<GuardRecord>();
            foreach (var id in report.GuardIds) result.Add(Guards.Find(g => g.Id == id));
            return result;
        }
        public void Advance(double now)
        {
            Require(Time(now), "Invalid guard time.");
            foreach (var guard in Guards)
            {
                if (!guard.IsAlive) continue;
                AdvanceInterior(guard, now);
                if (guard.Duty == GuardDuty.Responding && now >= guard.ArrivesAt)
                {
                    guard.Duty = GuardDuty.Searching;
                    var report = Reports.Find(r => r.Id == guard.IncidentId);
                    if (report != null && report.Investigation)
                    {
                        report.Arrivals.Find(a => a.GuardId == guard.Id).Arrived = true;
                        if (!report.HasArrival || guard.ArrivesAt < report.FirstArrivalAt)
                        { report.HasArrival = true; report.FirstArrivalAt = guard.ArrivesAt; }
                    }
                }
                if (guard.Duty == GuardDuty.Searching && now >= guard.SearchUntil)
                {
                    double duration = guard.ArrivesAt - guard.DepartedAt;
                    guard.Route.Reverse(); guard.DepartedAt = guard.SearchUntil;
                    guard.ArrivesAt = guard.DepartedAt + duration; guard.Duty = GuardDuty.Returning;
                }
                if (guard.Duty == GuardDuty.Returning && now >= guard.ArrivesAt)
                { guard.Duty = GuardDuty.Posted; guard.Route.Clear(); guard.IncidentId = null; }
            }
        }
        public SavedRoutePoint Position(string id, double now)
        {
            Advance(now);
            var guard = Find(id);
            if (!guard.IsAlive) return CopyPoint(guard.DeathPosition);
            if (guard.Duty == GuardDuty.Engaged) return CopyPoint(guard.EngagementPosition);
            if (guard.Route.Count < 2 || guard.Duty == GuardDuty.Posted) return CopyPoint(guard.Post);
            if (guard.Duty == GuardDuty.Searching) return CopyPoint(guard.Route[guard.Route.Count - 1]);
            double duration = guard.ArrivesAt - guard.DepartedAt;
            double fraction = duration <= 0 ? 1 : Math.Max(0, Math.Min(1, (now - guard.DepartedAt) / duration));
            double along = Length(guard.Route) * fraction;
            for (int i = 1; i < guard.Route.Count; i++)
            {
                var a = guard.Route[i - 1]; var b = guard.Route[i]; double segment = Distance(a, b);
                if (segment <= 0) continue;
                if (along <= segment) return new SavedRoutePoint { X = a.X + (b.X - a.X) * along / segment, Z = a.Z + (b.Z - a.Z) * along / segment };
                along -= segment;
            }
            return CopyPoint(guard.Route[guard.Route.Count - 1]);
        }
        public bool Kill(string id, double now)
        {
            Require(Time(now), "Invalid guard death time.");
            var guard = Find(id); if (!guard.IsAlive) return false;
            var indoorDeath = InteriorPosition(id, now);
            if (guard.Interior != null)
            {
                guard.Interior.Position = indoorDeath; guard.Interior.Returning = false;
                guard.Interior.ReturnAt = guard.Interior.ExitAt = 0;
                guard.Interior.Trail = new List<GuardPoint3> { Point3(guard.Interior.Entrance), Point3(indoorDeath) };
            }
            guard.DeathPosition = Position(id, now);
            guard.IsAlive = false; guard.Duty = GuardDuty.Dead; guard.DiedAt = now; guard.HealthFraction = 0;
            guard.EngagementPosition = null;
            guard.LastSeenSuspect = null;
            return true;
        }
        public void Engage(string id, double now)
        {
            Require(Time(now), "Invalid engagement time.");
            var guard = Find(id);
            Require(guard.IsAlive && guard.IncidentId != null, "Engagement needs a living assigned guard.");
            var preview = CopyValidated(this);
            var position = preview.Position(id, now);
            Require(preview.Find(id).IncidentId != null && Time(now + 300), "Guard assignment has expired.");
            Advance(now);
            guard.EngagementPosition = position; guard.Duty = GuardDuty.Engaged; guard.SearchUntil = now + 300;
            guard.LastSeenSuspect = CopyPoint(guard.Route[guard.Route.Count - 1]);
        }
        public void ObserveEngagement(string id, SavedRoutePoint position, double healthFraction, double now, bool seesSuspect, SavedRoutePoint suspect = null)
        {
            var point = CopyPoint(position);
            var seen = suspect == null ? null : CopyPoint(suspect);
            Require(Time(now) && Time(now + 300) && Finite(healthFraction) && healthFraction > 0 && healthFraction <= 1, "Invalid guard observation.");
            var guard = Find(id);
            Require(guard.IsAlive && guard.Duty == GuardDuty.Engaged && guard.Interior == null, "Guard is not engaged outside.");
            guard.EngagementPosition = point; guard.HealthFraction = healthFraction;
            if (seesSuspect) { guard.SearchUntil = now + 300; if (seen != null) guard.LastSeenSuspect = seen; }
        }
        public void ReturnFromEngagement(string id, double now, List<SavedRoutePoint> route, double speed, double timeScale)
        {
            Require(Time(now) && Finite(speed) && speed > 0 && Finite(timeScale) && timeScale > 0, "Invalid guard return.");
            var guard = Find(id); var copy = CopyRoute(route);
            Require(guard.IsAlive && guard.Duty == GuardDuty.Engaged && guard.Interior == null && copy.Count >= 2 &&
                Distance(copy[0], guard.EngagementPosition) < 0.05 && Distance(copy[copy.Count - 1], guard.Post) < 0.05, "Return must join actual position to assigned post.");
            double arrival = now + Length(copy) / speed * timeScale;
            Require(Time(arrival), "Guard return time overflow.");
            guard.Route = copy; guard.DepartedAt = now; guard.ArrivesAt = arrival; guard.SearchUntil = now;
            guard.Duty = GuardDuty.Returning; guard.EngagementPosition = null; guard.LastSeenSuspect = null;
        }
        public int Recruit(double now)
        {
            Require(Time(now), "Invalid recruitment time.");
            if (LastRecruitment >= 0 && now < LastRecruitment + 86400) return 0;
            int alive = 0; foreach (var guard in Guards) if (guard.IsAlive) alive++;
            if (alive >= Target) return 0;
            foreach (var guard in Guards)
                if (!guard.IsAlive && guard.ReplacedById == null && now >= guard.DiedAt + 7 * 86400)
                {
                    var recruited = NewGuard(guard.Post); guard.ReplacedById = recruited.Id;
                    Guards.Add(recruited); LastRecruitment = now; return 1;
                }
            return 0;
        }
        private GuardRecord Find(string id)
        { var guard = Guards.Find(g => g.Id == id); if (guard == null) throw new ArgumentException("Unknown guard."); return guard; }
        private static double Length(List<SavedRoutePoint> route)
        { double result = 0; for (int i = 1; i < route.Count; i++) result += Distance(route[i - 1], route[i]); Require(Finite(result), "Invalid route length."); return result; }
        private static double Distance(SavedRoutePoint a, SavedRoutePoint b)
        { double x = a.X - b.X, z = a.Z - b.Z; return Math.Sqrt(x * x + z * z); }
        private static SavedRoutePoint CopyPoint(SavedRoutePoint point)
        { Require(point != null && Finite(point.X) && Finite(point.Z) && Math.Abs(point.X) <= 1000000 && Math.Abs(point.Z) <= 1000000, "Invalid guard coordinate."); return new SavedRoutePoint { X = point.X, Z = point.Z }; }
        private static List<SavedRoutePoint> CopyRoute(List<SavedRoutePoint> route)
        { Require(route != null && route.Count <= 10000, "Invalid guard route."); var result = new List<SavedRoutePoint>(); foreach (var point in route) result.Add(CopyPoint(point)); return result; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Time(double value) { return Finite(value) && value >= 0; }
        private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
        public static GuardRoster CopyValidated(GuardRoster source)
        {
            Require(source != null && source.SettlementId >= 0 && source.Target >= 0 && source.Target <= 256 && source.NextIdentity >= 0 &&
                source.Guards != null && source.Reports != null && (source.LastRecruitment == -1 || Time(source.LastRecruitment)), "Invalid garrison.");
            var copy = new GuardRoster { SettlementId = source.SettlementId, Target = source.Target, NextIdentity = source.NextIdentity,
                Seed = source.Seed, LastRecruitment = source.LastRecruitment };
            var ids = new HashSet<string>(); int alive = 0;
            foreach (var guard in source.Guards)
            {
                Require(guard != null && !string.IsNullOrWhiteSpace(guard.Id) && ids.Add(guard.Id) && Enum.IsDefined(typeof(GuardDuty), guard.Duty) &&
                    guard.IsAlive == (guard.Duty != GuardDuty.Dead) && Time(guard.DepartedAt) && Time(guard.ArrivesAt) &&
                    Time(guard.SearchUntil) && Time(guard.DiedAt), "Invalid guard identity or state.");
                if (guard.IsAlive) alive++;
                Require(Finite(guard.HealthFraction) && guard.HealthFraction >= 0 && guard.HealthFraction <= 1 &&
                    (!guard.IsAlive || guard.HealthFraction > 0), "Invalid guard health.");
                Require((guard.Duty == GuardDuty.Engaged) == (guard.EngagementPosition != null), "Invalid engagement position.");
                Require((guard.Duty == GuardDuty.Engaged) == (guard.LastSeenSuspect != null), "Invalid last-seen position.");
                Require(guard.Interior == null || source.Reports.Exists(r => r != null && r.Id == guard.IncidentId &&
                    r.BuildingKey == guard.Interior.BuildingKey), "Indoor guard has no matching building report.");
                var route = CopyRoute(guard.Route);
                if (guard.Duty == GuardDuty.Posted) Require(route.Count == 0 && guard.IncidentId == null, "Posted guard has a pending route.");
                else if (guard.IsAlive)
                    Require(route.Count >= 2 && guard.IncidentId != null && guard.ArrivesAt >= guard.DepartedAt &&
                        source.Reports.Exists(r => r != null && r.Id == guard.IncidentId && r.GuardIds != null && r.GuardIds.Contains(guard.Id)), "Invalid responding guard.");
                double searchUntil = guard.SearchUntil;
                // The unpublished discovery preview used six hours. Normalize only
                // that exact occupation floor; preserve active pursuit and history.
                if (source.Reports.Exists(r => r != null && r.Id == guard.IncidentId && r.Investigation) &&
                    (guard.Duty == GuardDuty.Responding || guard.Duty == GuardDuty.Searching ||
                    (guard.Duty == GuardDuty.Engaged && guard.Interior != null && !guard.Interior.Returning)) &&
                    searchUntil == guard.ArrivesAt + 21600)
                    searchUntil = guard.ArrivesAt + UnwitnessedScene.OccupationSeconds;
                if (guard.Duty == GuardDuty.Responding || guard.Duty == GuardDuty.Searching)
                    Require(searchUntil == guard.ArrivesAt + (source.Reports.Exists(r => r != null && r.Id == guard.IncidentId && r.Investigation) ? UnwitnessedScene.OccupationSeconds : 300) &&
                        Distance(CopyPoint(guard.Post), route[0]) < 0.05, "Invalid guard search deadline or route origin.");
                if (guard.Duty == GuardDuty.Returning)
                    Require(guard.DepartedAt == guard.SearchUntil && Distance(CopyPoint(guard.Post), route[route.Count - 1]) < 0.05, "Return route does not reach the assigned post.");
                Require(guard.IsAlive == (guard.DeathPosition == null), "Guard death location disagrees with life state.");
                copy.Guards.Add(new GuardRecord { Id = guard.Id, IncidentId = guard.IncidentId, ReplacedById = guard.ReplacedById,
                    Interior = CopyInterior(guard),
                    AppearanceSeed = guard.AppearanceSeed, IsAlive = guard.IsAlive, Post = CopyPoint(guard.Post), Duty = guard.Duty,
                    DeathPosition = guard.DeathPosition == null ? null : CopyPoint(guard.DeathPosition),
                    HealthFraction = guard.HealthFraction,
                    EngagementPosition = guard.EngagementPosition == null ? null : CopyPoint(guard.EngagementPosition),
                    LastSeenSuspect = guard.LastSeenSuspect == null ? null : CopyPoint(guard.LastSeenSuspect),
                    Route = route, DepartedAt = guard.DepartedAt, ArrivesAt = guard.ArrivesAt, SearchUntil = searchUntil, DiedAt = guard.DiedAt });
            }
            Require(alive <= source.Target && source.NextIdentity == source.Guards.Count, "Invalid guard allocation count.");
            var sequence = new Dictionary<string, int>();
            for (int i = 0; i < source.NextIdentity; i++)
            {
                string id = string.Format(CultureInfo.InvariantCulture, "g:{0}:n:{1}", source.SettlementId, i);
                Require(ids.Contains(id), "Guard sequence has missing or foreign identities."); sequence.Add(id, i);
            }
            var replacements = new HashSet<string>();
            foreach (var guard in copy.Guards)
                Require(guard.ReplacedById == null || (!guard.IsAlive && ids.Contains(guard.ReplacedById) &&
                    sequence[guard.ReplacedById] > sequence[guard.Id] && replacements.Add(guard.ReplacedById)), "Invalid replacement link.");
            var reports = new HashSet<string>();
            foreach (var report in source.Reports)
            {
                Require(report != null && !string.IsNullOrWhiteSpace(report.Id) && reports.Add(report.Id) && report.GuardIds != null && report.GuardIds.Count <= 256, "Invalid guard report.");
                Require(report.BuildingKey.HasValue == (report.Entrance != null), "Incomplete building report.");
                Require(!report.BuildingKey.HasValue || report.BuildingKey.Value >= 0, "Invalid report building.");
                Require(!report.Investigation || report.BuildingKey.HasValue, "Investigation requires a building.");
                Require(Time(report.FirstArrivalAt) && (report.HasArrival ? report.Investigation && report.GuardIds.Count > 0 : report.FirstArrivalAt == 0),
                    "Invalid investigation arrival evidence.");
                var saved = new GuardReport { Id = report.Id, BuildingKey = report.BuildingKey, Investigation = report.Investigation,
                    HasArrival = report.HasArrival, FirstArrivalAt = report.FirstArrivalAt,
                    Entrance = report.Entrance == null ? null : CopyPoint(report.Entrance) }; var responders = new HashSet<string>();
                foreach (var id in report.GuardIds)
                { Require(id != null && ids.Contains(id) && responders.Add(id), "Unknown or duplicate responder."); saved.GuardIds.Add(id); }
                var arrivalIds = new HashSet<string>();
                double firstArrival = double.MaxValue;
                foreach (var arrival in report.Arrivals ?? new List<GuardArrivalEvidence>())
                {
                    Require(arrival != null && report.Investigation && responders.Contains(arrival.GuardId) &&
                        arrivalIds.Add(arrival.GuardId) && Time(arrival.ScheduledAt), "Invalid responder arrival evidence.");
                    var guard = copy.Find(arrival.GuardId);
                    if (guard.IncidentId == report.Id && (guard.Duty == GuardDuty.Responding || guard.Duty == GuardDuty.Searching))
                        Require(arrival.ScheduledAt == guard.ArrivesAt && arrival.Arrived == (guard.Duty == GuardDuty.Searching),
                            "Arrival evidence disagrees with responder travel.");
                    Require(!arrival.Arrived || guard.IsAlive || guard.DiedAt >= arrival.ScheduledAt,
                        "Guard died before the recorded arrival.");
                    if (arrival.Arrived) firstArrival = Math.Min(firstArrival, arrival.ScheduledAt);
                    saved.Arrivals.Add(new GuardArrivalEvidence { GuardId = arrival.GuardId,
                        ScheduledAt = arrival.ScheduledAt, Arrived = arrival.Arrived });
                }
                Require(!report.Investigation || arrivalIds.Count == responders.Count, "Missing responder arrival evidence.");
                Require(report.HasArrival == (firstArrival != double.MaxValue) &&
                    report.FirstArrivalAt == (report.HasArrival ? firstArrival : 0), "Invalid first investigation arrival.");
                if (saved.BuildingKey.HasValue)
                    foreach (var guard in copy.Guards)
                        if (guard.IsAlive && guard.IncidentId == saved.Id && guard.Duty != GuardDuty.Returning)
                        {
                            Require(guard.Interior == null || guard.Interior.BuildingKey == saved.BuildingKey,
                                "Indoor guard occupies another report building.");
                            Require(Distance(guard.Route[guard.Route.Count - 1], saved.Entrance) < 0.05,
                                "Building response does not reach its reported entrance.");
                        }
                copy.Reports.Add(saved);
            }
            copy.CopyBuildingApproaches(source.BuildingApproaches);
            return copy;
        }
    }
    public sealed partial class WorldStateStore
    {
        public GuardRoster GetGuards(int settlementId) { return state.Garrisons.Find(g => g.SettlementId == settlementId); }
        public GuardRoster GetOrCreateGuards(int settlementId, int count, IList<SavedRoutePoint> posts)
        {
            var existing = state.Garrisons.Find(g => g.SettlementId == settlementId);
            if (existing != null) return existing;
            Require(state.Settlements.Exists(t => t.SettlementId == settlementId), "Garrison needs a known settlement.");
            var created = GuardRoster.Create(settlementId, state.WorldSeed, count, posts);
            state.Garrisons.Add(created); return created;
        }
    }
}
