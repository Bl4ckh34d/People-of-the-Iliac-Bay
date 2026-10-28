using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public enum SettlementScale { Hamlet, Town, City }
    public enum GuardDuty { Posted, Responding, Searching, Returning, Dead, Engaged, Refuge }
    public enum GuardShift { Unassigned, Day, Night }
    [Serializable]
    public sealed class GuardEffectSnapshot
    {
        public string Json;
        public uint MagicMinute;
    }
    [Serializable]
    public sealed class GuardRecord
    {
        public string Id, IncidentId, ReplacedById;
        public uint AppearanceSeed;
        public bool ArcheryAssigned, HasBow;
        public int ArrowsRemaining;
        public string BowSupportGuardId;
        public bool IsAlive = true;
        public SavedRoutePoint Post, DeathPosition;
        public SavedRoutePoint ReliefPost;
        public SavedRoutePoint EngagementPosition;
        public SavedRoutePoint LastSeenSuspect;
        public SavedRoutePoint ResponseOrigin;
        public List<SavedRoutePoint> ReturnPath;
        public double ReturnRate;
        public GuardInteriorState Interior;
        public GuardDoorPursuit DoorPursuit;
        public GuardRefugeJourney Refuge;
        public GuardPostRecall PostRecall;
        public double HealthFraction = 1;
        public GuardEffectSnapshot Effects;
        public GuardDuty Duty;
        public GuardShift Shift;
        public List<SavedRoutePoint> Route = new List<SavedRoutePoint>();
        public double DepartedAt, ArrivesAt, SearchUntil, DiedAt;
        // Brief physical inquiries freeze the current patrol phase without
        // changing arrival evidence or extending the fixed search deadline.
        public double SearchPauseUntil, SearchDelaySeconds;
        // A night-patrol guard keeps this through its physical return to post,
        // then begins its daytime wall-bed rest when a route is available.
        public double DayRestUntil;
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
        public string CleanupCorpseId;
        public string ThreatId;
        public List<SavedRoutePoint> SearchRoute;
        public int SearchLaps;
        public CrimeSceneWarning Warning;
        public List<GuardArrivalEvidence> Arrivals = new List<GuardArrivalEvidence>();
        public bool Investigation;
        public bool CrimeScene;
        public bool OccupiesScene { get { return Investigation || CrimeScene; } }
        public bool AwaitingApproach;
        public bool HasArrival;
        public double FirstArrivalAt;
        public double InvestigationUntil;
        public bool DepartureInvestigation;
        public double DepartureAt,DepartureReturnBudget;
        public bool HasCorpseInquiryWindow;
        public double CorpseInquiryStartedAt;
        public int CorpseSearchVersion;
        public CorpseSuspectKnowledge CorpseSuspect;
        public CorpseSuspectSighting SuspectSighting;
        public string Id;
        public int? BuildingKey;
        public SavedRoutePoint Entrance;
        public List<string> GuardIds = new List<string>();
        public List<string> HaltGuardIds = new List<string>();
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
        // Preserve the old quota's rounding before doubling. Gate duty is additional,
        // never a share deducted from ordinary town coverage.
        public const int MaximumBaseTarget = 700;
        public const int GateSlotsPerGate = 4;
        public const int MaximumGateCount = 256;
        public const int MaximumWallPosts = 128;
        public const int MaximumTarget = 2 * (MaximumBaseTarget + GateSlotsPerGate * MaximumGateCount + MaximumWallPosts);
        public static int TargetWithWallSlots(int baseTarget,int gates,int wallPosts)
        {
            Require(wallPosts>=0 && wallPosts<=MaximumWallPosts,"Invalid wall watch allocation.");
            return TargetWithGateSlots(baseTarget,gates)+2*wallPosts;
        }
        public static int TargetWithGateSlots(int baseTarget, int gates)
        {
            Require(baseTarget >= 0 && baseTarget <= MaximumBaseTarget && gates >= 0 && gates <= MaximumGateCount,
                "Invalid gate garrison allocation.");
            // Each shift retains the previous coverage. Even the smallest hamlet
            // needs two patrollers and one fixed post when a safe circuit exists.
            return 2 * (Math.Max(3, baseTarget) + gates * GateSlotsPerGate);
        }
        public static int TargetCount(SettlementScale scale, int residents)
        {
            Require(residents >= 0 && (scale == SettlementScale.Hamlet || scale == SettlementScale.Town || scale == SettlementScale.City), "Invalid settlement population.");
            if (scale == SettlementScale.Hamlet && residents < 40) return 2;
            return 2 * (int)Math.Min(350d, Math.Max(scale == SettlementScale.City ? 16d : 8d, Math.Ceiling(residents * 0.08)));
        }
        public static GuardRoster Create(int settlementId, uint seed, int count, IList<SavedRoutePoint> posts)
        {
            Require(settlementId >= 0 && count >= 0 && count <= MaximumTarget && posts != null && (count == 0 || posts.Count > 0), "Invalid garrison allocation.");
            foreach (var post in posts) CopyPoint(post);
            var roster = new GuardRoster { SettlementId = settlementId, Seed = seed, Target = count };
            for (int i = 0; i < count; i++) roster.Guards.Add(roster.NewGuard(posts[i % posts.Count]));
            return roster;
        }
        private GuardRecord NewGuard(SavedRoutePoint post)
        {
            int identity = NextIdentity++;
            var guard = new GuardRecord { Id = string.Format(CultureInfo.InvariantCulture, "g:{0}:n:{1}", SettlementId, identity),
                AppearanceSeed = StableSeed.Combine(Seed, (uint)SettlementId, (uint)identity, 0x67756172),
                Shift = identity % 2 == 0 ? GuardShift.Day : GuardShift.Night, Post = CopyPoint(post), ReliefPost = CopyPoint(post) };
            GuardArchery.EnsureLoadout(guard);
            return guard;
        }
        public int IncreaseTarget(int requested, IList<SavedRoutePoint> posts)
        {
            Require(requested >= 0 && requested <= MaximumTarget && posts != null,"Invalid garrison expansion.");
            if (requested <= Target) return 0;
            var unused = new List<SavedRoutePoint>();
            foreach (var post in posts)
            {
                var copy = CopyPoint(post);
                if (!Guards.Exists(g => Distance(g.Post,copy)<0.05) && !unused.Exists(p => Distance(p,copy)<0.05)) unused.Add(copy);
            }
            int added = requested-Target;
            Require(unused.Count >= added,"Expansion needs distinct unoccupied posts.");
            var preview = CopyValidated(this); preview.Target = requested;
            for (int i=0;i<added;i++) preview.Guards.Add(preview.NewGuard(unused[i * unused.Count / added]));
            CopyValidated(preview);
            int oldCount = Guards.Count;
            for (int i=oldCount;i<preview.Guards.Count;i++) Guards.Add(preview.Guards[i]);
            Target = preview.Target; NextIdentity = preview.NextIdentity;
            return added;
        }
        public bool TryGuardHalt(string id)
        {
            var guard = Find(id);
            if (!guard.IsAlive || guard.IncidentId == null || guard.Duty == GuardDuty.Posted || guard.Duty == GuardDuty.Returning) return false;
            var report = Reports.Find(r => r.Id == guard.IncidentId);
            if (report == null || !report.GuardIds.Contains(id)) return false;
            if (report.HaltGuardIds == null) report.HaltGuardIds = new List<string>();
            if (report.HaltGuardIds.Contains(id)) return false;
            report.HaltGuardIds.Add(id); return true;
        }
        public List<GuardRecord> Dispatch(string incidentId, double now, IDictionary<string, List<SavedRoutePoint>> routes,
            int maximumResponders, double metresPerRealSecond, double gameSecondsPerRealSecond)
        { return DispatchGuards(incidentId, null, now, routes, maximumResponders, metresPerRealSecond, gameSecondsPerRealSecond); }
        public List<GuardRecord> DispatchDefense(string incidentId, string threatId, double now,
            IDictionary<string, List<SavedRoutePoint>> routes, int maximumResponders, double metresPerRealSecond, double gameSecondsPerRealSecond)
        {
            Require(!string.IsNullOrWhiteSpace(threatId) && threatId.Length <= 128, "Invalid defense target.");
            return DispatchGuards(incidentId, threatId, now, routes, maximumResponders, metresPerRealSecond, gameSecondsPerRealSecond);
        }
        private List<GuardRecord> DispatchGuards(string incidentId, string threatId, double now,
            IDictionary<string, List<SavedRoutePoint>> routes, int maximumResponders, double metresPerRealSecond, double gameSecondsPerRealSecond)
        {
            Require(!string.IsNullOrWhiteSpace(incidentId) && Time(now) && routes != null && maximumResponders >= 0 && maximumResponders <= 256 &&
                Finite(metresPerRealSecond) && metresPerRealSecond > 0 && Finite(gameSecondsPerRealSecond) && gameSecondsPerRealSecond > 0, "Invalid guard dispatch.");
            Require(Finite(gameSecondsPerRealSecond / metresPerRealSecond) && gameSecondsPerRealSecond / metresPerRealSecond > 0,
                "Invalid guard travel rate.");
            var report = Reports.Find(r => r.Id == incidentId);
            Require(report == null || report.ThreatId == threatId, "Report target cannot change.");
            if (report != null && report.GuardIds.Count > 0) return ReportGuards(report);
            // Validate and copy all supplied routes before assigning any guard.
            var candidates = new List<GuardRecord>();
            var copies = new Dictionary<string, List<SavedRoutePoint>>();
            var lengths = new Dictionary<string, double>();
            var returns = new Dictionary<string, List<SavedRoutePoint>>();
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
                Require(Distance(preview.Position(guard.Id, now), copies[guard.Id][0]) < 0.05, "Route must begin at the guard's actual position.");
                var returnPath = ResponseReturn(guard, copies[guard.Id], preview.PatrolReturn(guard.Id, now));
                Require(returnPath.Count >= 2 && returnPath.Count <= 10000 && Distance(returnPath[returnPath.Count - 1], guard.Post) < 0.05 &&
                    Time(now + lengths[guard.Id] / metresPerRealSecond * gameSecondsPerRealSecond + 3600 +
                        Length(returnPath) / metresPerRealSecond * gameSecondsPerRealSecond), "Invalid guard response return.");
                returns.Add(guard.Id, returnPath);
                candidates.Add(Find(guard.Id));
            }
            Advance(now);
            candidates.Sort((a, b) => { int order = lengths[a.Id].CompareTo(lengths[b.Id]); return order != 0 ? order : string.CompareOrdinal(a.Id, b.Id); });
            if (report == null && threatId != null && (candidates.Count == 0 || maximumResponders == 0)) return new List<GuardRecord>();
            if (report == null) { report = new GuardReport { Id = incidentId, ThreatId = threatId }; Reports.Add(report); }
            for (int i = 0; i < Math.Min(maximumResponders, candidates.Count); i++)
            {
                var guard = candidates[i];
                guard.Route = copies[guard.Id]; guard.IncidentId = incidentId;
                guard.DayRestUntil=0;
                guard.SearchPauseUntil=0;guard.SearchDelaySeconds=0;
                guard.ResponseOrigin = CopyPoint(guard.Route[0]);
                guard.ReturnPath = returns[guard.Id]; guard.ReturnRate = gameSecondsPerRealSecond / metresPerRealSecond;
                InterruptPatrol(guard.Id, now);
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
                CancelInterruptedRecall(guard);
                if(guard.Duty!=GuardDuty.Searching)
                {guard.SearchPauseUntil=0;guard.SearchDelaySeconds=0;}
                if (!guard.IsAlive) continue;
                if (guard.Refuge != null)
                {
                    if (guard.Refuge.Evaluate(now).Phase == RefugePhase.Complete)
                    { guard.Refuge=null; guard.Duty=GuardDuty.Posted; }
                    else continue;
                }
                AdvanceInterior(guard, now);
                if(guard.Duty==GuardDuty.Responding && guard.Interior==null &&
                    now>=guard.SearchUntil && guard.SearchUntil<guard.ArrivesAt &&
                    Reports.Exists(report=>report.Id==guard.IncidentId && report.HasCorpseInquiryWindow))
                    BeginInterruptedResponseReturn(guard,Math.Max(guard.DepartedAt,guard.SearchUntil));
                if (guard.Duty == GuardDuty.Responding && now >= guard.ArrivesAt)
                {
                    guard.Duty = GuardDuty.Searching;
                    var report = Reports.Find(r => r.Id == guard.IncidentId);
                    if (report != null && report.OccupiesScene && guard.DoorPursuit==null)
                    {
                        report.Arrivals.Find(a => a.GuardId == guard.Id).Arrived = true;
                        if (!report.HasArrival || guard.ArrivesAt < report.FirstArrivalAt)
                        { report.HasArrival = true; report.FirstArrivalAt = guard.ArrivesAt; }
                    }
                }
                if (guard.Duty == GuardDuty.Searching && now >= guard.SearchUntil)
                {
                    var departureReturn=guard.DoorPursuit==null ? DepartureSearchReturn(guard,guard.SearchUntil) : null;
                    if(departureReturn!=null)guard.ReturnPath=departureReturn;
                    BeginReturn(guard, guard.SearchUntil);
                }
                if (guard.Duty == GuardDuty.Returning && now >= guard.ArrivesAt)
                { guard.Duty = GuardDuty.Posted; guard.Route.Clear(); guard.IncidentId = null;
                    guard.DoorPursuit=null;
                    guard.ResponseOrigin = null; guard.ReturnPath = null; guard.ReturnRate = 0; }
                AdvancePostRecall(guard,now);
                if (guard.DayRestUntil > 0 && now >= guard.DayRestUntil && guard.Refuge == null)
                    guard.DayRestUntil=0;
            }
            Patrols.RemoveAll(p => now >= PatrolFinishedAt(p));
            Reports.RemoveAll(r=>(r.Id.StartsWith("post:",StringComparison.Ordinal) ||
                r.Id.StartsWith("gate-recall:",StringComparison.Ordinal)) &&
                !Guards.Exists(g=>g.IncidentId==r.Id || g.PostRecall!=null && g.PostRecall.OutboundIncidentId==r.Id));
        }
        public SavedRoutePoint Position(string id, double now)
        {
            Advance(now);
            var guard = Find(id);
            if (!guard.IsAlive) return CopyPoint(guard.DeathPosition);
            if (guard.Refuge != null) return RefugeExteriorPosition(guard, now);
            if (guard.Duty == GuardDuty.Engaged) return CopyPoint(guard.EngagementPosition);
            if (guard.Duty==GuardDuty.Searching && guard.DoorPursuit!=null) return CopyPoint(guard.Route[guard.Route.Count-1]);
            if (guard.Route.Count < 2 || guard.Duty == GuardDuty.Posted) return PostedPosition(guard, now);
            if (guard.Duty == GuardDuty.Searching) return SearchPosition(Reports.Find(r => r.Id == guard.IncidentId), guard, now);
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
                guard.Interior.Position = indoorDeath; guard.Interior.Returning = false; guard.Interior.ExteriorPursuit=null;
                guard.Interior.ReturnAt = guard.Interior.ExitAt = 0;
                guard.Interior.Trail = new List<GuardPoint3> { Point3(guard.Interior.Entrance), Point3(indoorDeath) };
            }
            guard.DeathPosition = Position(id, now);
            InterruptPatrol(id, now);
            guard.IsAlive = false; guard.Duty = GuardDuty.Dead; guard.DiedAt = now; guard.HealthFraction = 0;
            guard.EngagementPosition = null;
            guard.LastSeenSuspect = null;
            return true;
        }
        public void RedirectDefense(string id,double now,List<SavedRoutePoint> route,List<SavedRoutePoint> home,double speed,double timeScale)
        {
            Require(Time(now) && Finite(speed) && speed>0 && Finite(timeScale) && timeScale>0,"Invalid defense redirection.");
            var guard=Find(id);var path=CopyRoute(route);var back=CopyRoute(home);
            var position=CopyValidated(this).Position(id,now);
            Require(guard.IsAlive && guard.Interior==null && guard.Refuge==null &&
                (guard.Duty==GuardDuty.Engaged || guard.Duty==GuardDuty.Responding || guard.Duty==GuardDuty.Searching) &&
                Reports.Exists(r=>r.Id==guard.IncidentId && r.ThreatId!=null),"Only active enemy defense can redirect.");
            Require(path.Count>=2 && back.Count>=2 && Distance(path[0],position)<0.05 &&
                Distance(path[path.Count-1],back[0])<0.05 && Distance(back[back.Count-1],guard.Post)<0.05,"Defense redirection must preserve actual position and return post.");
            double rate=timeScale/speed,arrival=now+Length(path)*rate;
            Require(Time(arrival+3600+Length(back)*rate),"Defense redirection time overflow.");
            guard.Route=path;guard.ResponseOrigin=CopyPoint(path[0]);guard.ReturnPath=back;guard.ReturnRate=rate;
            guard.DepartedAt=now;guard.ArrivesAt=arrival;guard.SearchUntil=arrival+300;
            guard.DoorPursuit=null;
            guard.EngagementPosition=null;guard.LastSeenSuspect=null;guard.Duty=GuardDuty.Responding;
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
            guard.ResponseOrigin = null; guard.ReturnPath = null; guard.ReturnRate = 0;
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
                    var recruited = NewGuard(guard.Post); recruited.Shift = guard.Shift;
                    recruited.ReliefPost = guard.ReliefPost == null ? null : CopyPoint(guard.ReliefPost); guard.ReplacedById = recruited.Id;
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
        private static double InvestigationDeadline(GuardReport report, double fallbackArrival)
        {
            if (report.InvestigationUntil > 0) return report.InvestigationUntil;
            double first = double.MaxValue;
            if (report.Arrivals != null)
                foreach (var arrival in report.Arrivals)
                    if (arrival != null) first = Math.Min(first, arrival.ScheduledAt);
            if (first == double.MaxValue) first = fallbackArrival;
            return first + UnwitnessedScene.OccupationSeconds;
        }
        public static GuardRoster CopyValidated(GuardRoster source)
        {
            Require(source != null && source.SettlementId >= 0 && source.Target >= 0 && source.Target <= MaximumTarget && source.NextIdentity >= 0 &&
                source.Guards != null && source.Reports != null && (source.LastRecruitment == -1 || Time(source.LastRecruitment)), "Invalid garrison.");
            var copy = new GuardRoster { SettlementId = source.SettlementId, Target = source.Target, NextIdentity = source.NextIdentity,
                Seed = source.Seed, LastRecruitment = source.LastRecruitment };
            var ids = new HashSet<string>(); int alive = 0;
            foreach (var guard in source.Guards)
            {
                Require(guard != null && !string.IsNullOrWhiteSpace(guard.Id) && ids.Add(guard.Id) && Enum.IsDefined(typeof(GuardDuty), guard.Duty) &&
                    Enum.IsDefined(typeof(GuardShift), guard.Shift) &&
                    guard.IsAlive == (guard.Duty != GuardDuty.Dead) && Time(guard.DepartedAt) && Time(guard.ArrivesAt) &&
                    Time(guard.SearchUntil) && Time(guard.DiedAt) && Time(guard.DayRestUntil) &&
                    Time(guard.SearchPauseUntil) && Time(guard.SearchDelaySeconds) && guard.SearchDelaySeconds<=1800 &&
                    (guard.SearchPauseUntil==0 ? guard.SearchDelaySeconds==0 : guard.SearchDelaySeconds>0), "Invalid guard identity or state.");
                if (guard.IsAlive) alive++;
                Require(guard.ArrowsRemaining>=0 && guard.ArrowsRemaining<=24 &&
                    (!guard.ArcheryAssigned || guard.HasBow || guard.ArrowsRemaining==0),"Invalid guard archery loadout.");
                Require(Finite(guard.HealthFraction) && guard.HealthFraction >= 0 && guard.HealthFraction <= 1 &&
                    (!guard.IsAlive || guard.HealthFraction > 0), "Invalid guard health.");
                Require((guard.Duty == GuardDuty.Engaged) == (guard.EngagementPosition != null), "Invalid engagement position.");
                Require((guard.Duty == GuardDuty.Engaged) == (guard.LastSeenSuspect != null), "Invalid last-seen position.");
                Require(guard.Interior == null || source.Reports.Exists(r => r != null && r.Id == guard.IncidentId &&
                    (r.BuildingKey == guard.Interior.BuildingKey || guard.DoorPursuit!=null &&
                    guard.DoorPursuit.BuildingKey==guard.Interior.BuildingKey)), "Indoor guard has no matching building report.");
                Require(guard.Interior==null || guard.Interior.ExteriorPursuit==null ||
                    guard.IsAlive && source.Reports.Exists(r=>r!=null && r.Id==guard.IncidentId && r.ThreatId==null &&
                        r.Entrance!=null && Distance(r.Entrance,CopyPoint(guard.Interior.ExteriorPursuit))<0.05),
                    "Invalid saved doorway pursuit destination.");
                var route = CopyRoute(guard.Route);
                var returnPath = guard.ReturnPath == null ? null : CopyRoute(guard.ReturnPath);
                Require(returnPath == null ? guard.ResponseOrigin == null && guard.ReturnRate == 0 :
                    guard.ResponseOrigin != null && Finite(guard.ReturnRate) && guard.ReturnRate > 0 && returnPath.Count >= 2 &&
                    Distance(returnPath[returnPath.Count - 1], guard.Post) < 0.05 &&
                    Time(guard.ArrivesAt + 3600 + Length(returnPath) * guard.ReturnRate), "Invalid saved response return.");
                if (guard.Duty == GuardDuty.Posted) Require(route.Count == 0 && guard.IncidentId == null && returnPath == null, "Posted guard has a pending route.");
                else if (guard.IsAlive && guard.Duty != GuardDuty.Refuge)
                    Require(route.Count >= 2 && guard.IncidentId != null && guard.ArrivesAt >= guard.DepartedAt &&
                        source.Reports.Exists(r => r != null && r.Id == guard.IncidentId && r.GuardIds != null && r.GuardIds.Contains(guard.Id)), "Invalid responding guard.");
                double searchUntil = guard.SearchUntil;
                var assignedReport = source.Reports.Find(r => r != null && r.Id == guard.IncidentId);
                Require(guard.DoorPursuit==null || DoorTargetMatches(assignedReport,guard.DoorPursuit.ThreatId),
                    "Witnessed door target differs from the assigned suspect.");
                bool investigation = guard.DoorPursuit==null && assignedReport != null && assignedReport.OccupiesScene;
                double investigationUntil = investigation ? InvestigationDeadline(assignedReport, guard.ArrivesAt) : 0;
                // The unpublished discovery preview used six hours. Normalize only
                // that exact occupation floor; preserve active pursuit and history.
                if (investigation &&
                    (guard.Duty == GuardDuty.Responding || guard.Duty == GuardDuty.Searching ||
                    (guard.Duty == GuardDuty.Engaged && guard.Interior != null && !guard.Interior.Returning)) &&
                    (searchUntil == guard.ArrivesAt + 21600 || assignedReport.InvestigationUntil == 0))
                    searchUntil = investigationUntil;
                if (guard.Duty == GuardDuty.Responding || guard.Duty == GuardDuty.Searching)
                    Require(searchUntil == (guard.DoorPursuit!=null ? guard.DoorPursuit.SearchUntil : investigation ? investigationUntil : guard.ArrivesAt + 300) &&
                        Distance(CopyPoint(guard.ResponseOrigin ?? guard.Post), route[0]) < 0.05 &&
                        (returnPath == null || Distance(returnPath[0], route[route.Count - 1]) < 0.05), "Invalid guard search deadline or route origin.");
                Require(guard.Duty!=GuardDuty.Searching || guard.SearchPauseUntil==0 ||
                    assignedReport!=null && assignedReport.HasCorpseInquiryWindow && guard.SearchPauseUntil<=searchUntil &&
                    guard.SearchDelaySeconds<=guard.SearchPauseUntil-guard.ArrivesAt,
                    "Inquiry hold exceeds its fixed case deadline.");
                if (guard.Duty == GuardDuty.Returning)
                    Require(guard.DepartedAt == guard.SearchUntil && Distance(CopyPoint(guard.Post), route[route.Count - 1]) < 0.05, "Return route does not reach the assigned post.");
                Require(guard.IsAlive == (guard.DeathPosition == null), "Guard death location disagrees with life state.");
                var refuge=guard.Refuge==null ? null : GuardRefugeJourney.CopyValidated(guard.Refuge);
                Require((guard.Duty==GuardDuty.Refuge)==(guard.IsAlive && refuge!=null) &&
                    (refuge==null || guard.Interior==null && route.Count==0 && guard.IncidentId==null && returnPath==null &&
                    Distance(guard.Post,new SavedRoutePoint {
                        X=refuge.ReturnExterior==null ? refuge.Exterior[0].X : refuge.ReturnExterior[refuge.ReturnExterior.Count-1].X,
                        Z=refuge.ReturnExterior==null ? refuge.Exterior[0].Z : refuge.ReturnExterior[refuge.ReturnExterior.Count-1].Z })<0.05),
                    "Invalid guard refuge state.");
                Require(guard.Effects==null || !string.IsNullOrWhiteSpace(guard.Effects.Json) && guard.Effects.Json.Length<=1048576,
                    "Invalid guard effect snapshot.");
                copy.Guards.Add(new GuardRecord { Id = guard.Id, IncidentId = guard.IncidentId, ReplacedById = guard.ReplacedById,
                    ArcheryAssigned=guard.ArcheryAssigned,HasBow=guard.HasBow,ArrowsRemaining=guard.ArrowsRemaining,
                    BowSupportGuardId=guard.BowSupportGuardId,
                    Effects = guard.Effects==null ? null : new GuardEffectSnapshot { Json=guard.Effects.Json,MagicMinute=guard.Effects.MagicMinute },
                    Refuge = refuge, PostRecall=CopyPostRecall(guard.PostRecall),
                    Interior = CopyInterior(guard),
                    DoorPursuit = CopyDoorPursuit(guard),
                    AppearanceSeed = guard.AppearanceSeed, IsAlive = guard.IsAlive, Post = CopyPoint(guard.Post), Duty = guard.Duty, Shift = guard.Shift,
                    ReliefPost = guard.ReliefPost == null ? null : CopyPoint(guard.ReliefPost),
                    DeathPosition = guard.DeathPosition == null ? null : CopyPoint(guard.DeathPosition),
                    HealthFraction = guard.HealthFraction,
                    EngagementPosition = guard.EngagementPosition == null ? null : CopyPoint(guard.EngagementPosition),
                    LastSeenSuspect = guard.LastSeenSuspect == null ? null : CopyPoint(guard.LastSeenSuspect),
                    ResponseOrigin = guard.ResponseOrigin == null ? null : CopyPoint(guard.ResponseOrigin),
                    ReturnPath = returnPath, ReturnRate = guard.ReturnRate,
                    Route = route, DepartedAt = guard.DepartedAt, ArrivesAt = guard.ArrivesAt, SearchUntil = searchUntil, DiedAt = guard.DiedAt,
                    SearchPauseUntil=guard.SearchPauseUntil,SearchDelaySeconds=guard.SearchDelaySeconds,
                    DayRestUntil = guard.DayRestUntil });
                GuardArchery.EnsureLoadout(copy.Guards[copy.Guards.Count-1]);
            }
            Require(alive <= source.Target && source.NextIdentity == source.Guards.Count, "Invalid guard allocation count.");
            var occupiedBeds = new Dictionary<int, HashSet<int>>();
            foreach (var guard in copy.Guards)
            {
                if (!guard.IsAlive) continue;
                var rest = guard.Refuge ?? guard.PostRecall?.WallDuty;
                if (rest == null || !rest.RestBedRecord.HasValue) continue;
                HashSet<int> beds;
                if (!occupiedBeds.TryGetValue(rest.BuildingKey, out beds))
                    occupiedBeds.Add(rest.BuildingKey, beds = new HashSet<int>());
                Require(beds.Add(rest.RestBedRecord.Value), "Duplicate saved guard bed reservation.");
            }
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
                Require(report.ThreatId == null || !string.IsNullOrWhiteSpace(report.ThreatId) && report.ThreatId.Length <= 128 &&
                    !report.Investigation &&
                    report.SearchRoute == null && report.Warning == null, "Invalid saved defense report.");
                Require(report.CleanupCorpseId==null || !string.IsNullOrWhiteSpace(report.CleanupCorpseId) && report.CleanupCorpseId.Length<=512 &&
                    report.ThreatId==null && !report.OccupiesScene && !report.BuildingKey.HasValue && report.SearchRoute==null,
                    "Cleanup report must not contain a suspect or investigation.");
                bool outdoorSearch = report.Investigation && !report.BuildingKey.HasValue;
                Require(report.CorpseSearchVersion==0 || report.CorpseSearchVersion==1 && outdoorSearch &&
                    report.Id.StartsWith("outdoor:",StringComparison.Ordinal) &&
                    report.GuardIds.Count<=MaximumCorpseSearchGuards,"Invalid bounded corpse search case.");
                Require(report.DepartureInvestigation ? outdoorSearch && Time(report.DepartureAt) &&
                    Time(report.DepartureReturnBudget) && report.DepartureReturnBudget<3600 :
                    report.DepartureAt==0 && report.DepartureReturnBudget==0,"Invalid departure investigation window.");
                Require(report.HasCorpseInquiryWindow ? outdoorSearch && Time(report.CorpseInquiryStartedAt) &&
                    Time(report.CorpseInquiryStartedAt+1800d):report.CorpseInquiryStartedAt==0,
                    "Invalid corpse inquiry window.");
                Require(!report.CrimeScene || report.BuildingKey.HasValue && report.ThreatId==null,"Invalid accused crime scene.");
                Require(report.Warning == null || (report.OccupiesScene && report.BuildingKey.HasValue), "Warning needs an indoor investigation.");
                Require(!report.AwaitingApproach || report.BuildingKey.HasValue && !report.Investigation &&
                    report.GuardIds.Count == 0 && report.Warning == null && report.Entrance == null,
                    "Invalid queued building report.");
                Require((report.BuildingKey.HasValue && !report.AwaitingApproach || outdoorSearch) == (report.Entrance != null), "Incomplete report destination.");
                Require(!report.BuildingKey.HasValue || report.BuildingKey.Value >= 0, "Invalid report building.");
                List<SavedRoutePoint> patrol = null;
                if (outdoorSearch)
                {
                    patrol = CopyRoute(report.SearchRoute);
                    Require(patrol.Count >= 3 && Length(patrol) > 0 && report.SearchLaps >= 1 && report.SearchLaps <= 1000 &&
                        Distance(patrol[0], report.Entrance) < 0.05 && Distance(patrol[patrol.Count - 1], report.Entrance) < 0.05,
                        "Invalid saved outdoor search route.");
                }
                else Require(report.SearchRoute == null && report.SearchLaps == 0, "Unexpected outdoor search route.");
                Require(Time(report.FirstArrivalAt) && (report.HasArrival ? report.OccupiesScene && report.GuardIds.Count > 0 : report.FirstArrivalAt == 0),
                    "Invalid investigation arrival evidence.");
                var saved = new GuardReport { Id = report.Id, BuildingKey = report.BuildingKey, Investigation = report.Investigation,
                    CrimeScene = report.CrimeScene,
                    CleanupCorpseId = report.CleanupCorpseId,
                    ThreatId = report.ThreatId,
                    AwaitingApproach = report.AwaitingApproach,
                    SearchRoute = patrol, SearchLaps = report.SearchLaps,
                    Warning = CrimeSceneWarning.Copy(report.Warning),
                    HasArrival = report.HasArrival, FirstArrivalAt = report.FirstArrivalAt,
                    InvestigationUntil = report.InvestigationUntil,
                    DepartureInvestigation=report.DepartureInvestigation,DepartureAt=report.DepartureAt,
                    DepartureReturnBudget=report.DepartureReturnBudget,
                    HasCorpseInquiryWindow=report.HasCorpseInquiryWindow,CorpseInquiryStartedAt=report.CorpseInquiryStartedAt,
                    CorpseSearchVersion=report.CorpseSearchVersion,
                    CorpseSuspect=CopyCorpseSuspect(report),SuspectSighting=CopyCorpseSighting(report),
                    Entrance = report.Entrance == null ? null : CopyPoint(report.Entrance) }; var responders = new HashSet<string>();
                foreach (var id in report.GuardIds)
                { Require(id != null && ids.Contains(id) && responders.Add(id), "Unknown or duplicate responder."); saved.GuardIds.Add(id); }
                var speakers = new HashSet<string>();
                foreach (var id in report.HaltGuardIds ?? new List<string>())
                { Require(id != null && responders.Contains(id) && speakers.Add(id),"Unknown or duplicate Halt speaker."); saved.HaltGuardIds.Add(id); }
                var arrivalIds = new HashSet<string>();
                double firstArrival = double.MaxValue;
                foreach (var arrival in report.Arrivals ?? new List<GuardArrivalEvidence>())
                {
                    Require(arrival != null && report.OccupiesScene && responders.Contains(arrival.GuardId) &&
                        arrivalIds.Add(arrival.GuardId) && Time(arrival.ScheduledAt), "Invalid responder arrival evidence.");
                    var guard = copy.Find(arrival.GuardId);
                    if (guard.DoorPursuit==null && guard.IncidentId == report.Id && (guard.Duty == GuardDuty.Responding || guard.Duty == GuardDuty.Searching))
                        Require(arrival.ScheduledAt == guard.ArrivesAt && arrival.Arrived == (guard.Duty == GuardDuty.Searching),
                            "Arrival evidence disagrees with responder travel.");
                    Require(!arrival.Arrived || guard.IsAlive || guard.DiedAt >= arrival.ScheduledAt,
                        "Guard died before the recorded arrival.");
                    if (arrival.Arrived) firstArrival = Math.Min(firstArrival, arrival.ScheduledAt);
                    saved.Arrivals.Add(new GuardArrivalEvidence { GuardId = arrival.GuardId,
                        ScheduledAt = arrival.ScheduledAt, Arrived = arrival.Arrived });
                }
                Require(!report.OccupiesScene || arrivalIds.Count == responders.Count, "Missing responder arrival evidence.");
                Require(report.HasArrival == (firstArrival != double.MaxValue) &&
                    report.FirstArrivalAt == (report.HasArrival ? firstArrival : 0), "Invalid first investigation arrival.");
                if (report.OccupiesScene && saved.Arrivals.Count > 0)
                {
                    double scheduledFirst = double.MaxValue;
                    foreach (var arrival in saved.Arrivals) scheduledFirst = Math.Min(scheduledFirst, arrival.ScheduledAt);
                    double deadline = report.DepartureInvestigation ? report.DepartureAt+3600-report.DepartureReturnBudget :
                        scheduledFirst + UnwitnessedScene.OccupationSeconds;
                    if(report.HasCorpseInquiryWindow)deadline=Math.Min(deadline,report.CorpseInquiryStartedAt+1800d);
                    Require(report.HasCorpseInquiryWindow || deadline>=scheduledFirst,"Investigation ends before its responder arrives.");
                    Require(Time(deadline) && (report.InvestigationUntil == 0 || Math.Abs(report.InvestigationUntil - deadline) < 0.001),
                        "Invalid investigation occupation deadline.");
                    saved.InvestigationUntil = deadline;
                }
                else Require(report.InvestigationUntil == 0, "Unexpected investigation occupation deadline.");
                if (saved.BuildingKey.HasValue || outdoorSearch)
                    foreach (var guard in copy.Guards)
                        if (guard.IsAlive && guard.IncidentId == saved.Id && guard.Duty != GuardDuty.Returning)
                        {
                            Require(guard.Interior == null || guard.Interior.BuildingKey ==
                                (guard.DoorPursuit==null ? saved.BuildingKey : guard.DoorPursuit.BuildingKey),
                                "Indoor guard occupies another report building.");
                            Require(Distance(guard.Route[guard.Route.Count - 1], guard.DoorPursuit==null ? saved.Entrance : guard.DoorPursuit.Entrance) < 0.05,
                                "Response does not reach its reported destination.");
                        }
                copy.Reports.Add(saved);
            }
            copy.CopyBuildingApproaches(source.BuildingApproaches);
            copy.CopyPatrols(source.Patrols);
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
