using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class UnwitnessedScene
    {
        public const double OccupationSeconds = 3600;
        public bool PlayerLeft, Discovered;
        public double LeftAt, DiscoveredAt;
        public bool Reported;
        public string ReporterId, ReportGuardId;
        public SavedRoutePoint ReportGuardPosition;
        public double ReportedAt;
        public bool Occupied(double now) { return Discovered && now >= DiscoveredAt && now < DiscoveredAt + UnwitnessedScene.OccupationSeconds; }
    }
    public sealed partial class WorldStateStore
    {
        public bool ReportHeardViolence(string incidentId, string observerId, double now)
        {
            Require(ValidTime(now), "Invalid hearing report time.");
            var alarm = FindIncident(incidentId);
            var person = alarm.People.Find(p => p.ResidentId == observerId);
            Require(alarm.Scene != null && person != null && person.HeardViolence && !person.DirectWitness &&
                person.Reaction == CivilianReaction.Flee,
                "Neutral report needs an unidentified hearing observer.");
            if (alarm.Scene.Reported || alarm.Scene.Discovered || !Active(alarm, now) ||
                !AlarmResident(alarm.SettlementId, observerId).IsAlive || person.Escape == null ||
                now<person.Escape.ExitAt) return false;
            Require(person.Escape.BuildingKey == alarm.BuildingKey, "Hearer escaped another building.");
            var roster = state.Garrisons.Find(g => g.SettlementId == alarm.SettlementId);
            if (roster == null) return false;
            roster.Advance(now); person.Escape.Advance(person, now);
            var contacts=roster.GuardsInReportingRange(new SavedRoutePoint{X=person.X,Z=person.Z},now);
            if (contacts.Count==0) return false;
            alarm.Scene.Reported = true; alarm.Scene.ReporterId = observerId;
            alarm.Scene.ReportGuardId = contacts[0]; alarm.Scene.ReportedAt = now;
            alarm.Scene.ReportGuardPosition=roster.Position(contacts[0],now);
            return true;
        }
        public void RegisterUnwitnessedScene(string incidentId)
        {
            var alarm = FindIncident(incidentId); var victim = AlarmResident(alarm.SettlementId, alarm.VictimId);
            Require(!victim.IsAlive && alarm.HasLocation && alarm.BuildingKey >= 0 &&
                (VictimOwnsScene(alarm, victim, state) || alarm.People.Exists(p => p.HeardViolence)) &&
                alarm.People.TrueForAll(p => p.HeardViolence && !p.DirectWitness),
                "Scene needs a confirmed unwitnessed death in the resident's home.");
            if (alarm.Scene == null) alarm.Scene = new UnwitnessedScene();
        }
        // Called by a location observer, not by a short-lived alarm query. A player
        // may remain inside beyond the civilian response timeout without discovery.
        public List<CivilianIncident> ObserveCrimeScenes(int settlementId, int? currentBuilding, double now)
        {
            Require(settlementId >= 0 && (!currentBuilding.HasValue || currentBuilding.Value >= -1) && ValidTime(now), "Invalid scene observation.");
            var pending = new List<CivilianIncident>();
            foreach (var alarm in state.AlarmIncidents)
            {
                var scene = alarm.Scene;
                if (alarm.SettlementId != settlementId || scene == null || now < alarm.StartedAt || scene.Discovered) continue;
                if (!scene.PlayerLeft && currentBuilding.HasValue && currentBuilding != alarm.BuildingKey)
                { scene.PlayerLeft = true; scene.LeftAt = now; }
                if (scene.PlayerLeft || scene.Reported) pending.Add(alarm);
            }
            return pending;
        }
        public void DiscoverCrimeScene(string incidentId, double now)
        {
            var alarm = FindIncident(incidentId); var scene = alarm.Scene;
            Require(scene != null && (scene.PlayerLeft || scene.Reported) && ValidTime(now) && ValidTime(now + UnwitnessedScene.OccupationSeconds) &&
                now >= SceneReadyAt(scene),
                "Scene discovery requires departure and a valid guard arrival time.");
            if (scene.Discovered) return;
            var roster = state.Garrisons.Find(g => g.SettlementId == alarm.SettlementId);
            Require(roster != null, "Scene discovery requires municipal responders.");
            roster.Advance(now);
            var report = roster.Reports.Find(r => r.Id == "discovery:" + alarm.Id);
            Require(report != null && report.Investigation && report.BuildingKey == alarm.BuildingKey &&
                report.HasArrival && report.FirstArrivalAt >= SceneReadyAt(scene) && report.FirstArrivalAt <= now,
                "No investigation guard has reached this scene.");
            scene.Discovered = true; scene.DiscoveredAt = report.FirstArrivalAt;
            if (alarm.Corpse != null && alarm.Corpse.InspectionGuardId == null)
            {
                string inspector = null;
                foreach (var arrival in report.Arrivals)
                    if (arrival.Arrived && arrival.ScheduledAt == report.FirstArrivalAt &&
                        (inspector == null || string.CompareOrdinal(arrival.GuardId, inspector) < 0)) inspector = arrival.GuardId;
                Require(inspector != null, "Scene arrival has no living investigator.");
                alarm.Corpse.InspectionGuardId = inspector;
            }
        }
        private static bool VictimOwnsScene(CivilianIncident alarm, Resident victim, WorldState world)
        {
            if (alarm.BuildingKey < 0) return false;
            return alarm.BuildingKey == victim.HomeKey || world.NativePeople != null && world.NativePeople.Exists(p =>
                p.Id == victim.Id && p.SettlementId == alarm.SettlementId && p.WorkKey == alarm.BuildingKey);
        }
        private static double SceneReadyAt(UnwitnessedScene scene)
        { return scene.Reported ? scene.PlayerLeft ? Math.Min(scene.LeftAt, scene.ReportedAt) : scene.ReportedAt : scene.LeftAt; }
        private static UnwitnessedScene CopyScene(CivilianIncident alarm, Resident victim, WorldState world)
        {
            var scene = alarm.Scene;
            if (scene == null) return null;
            Require(!victim.IsAlive && alarm.HasLocation && alarm.BuildingKey >= 0 &&
                (VictimOwnsScene(alarm, victim, world) || alarm.People.Exists(p => p != null && p.HeardViolence)) &&
                alarm.People.TrueForAll(p => p != null && (p.HeardViolence || p.CorpseObserver)),
                "Invalid unwitnessed scene ownership.");
            Require(ValidTime(scene.ReportedAt), "Invalid hearing report time.");
            if (scene.Reported)
            {
                var reporter = alarm.People.Find(p => p != null && p.ResidentId == scene.ReporterId);
                var escape = reporter == null || reporter.Escape == null ? null : CivilianEscape.Copy(reporter.Escape);
                var roster = world.Garrisons == null ? null : world.Garrisons.Find(g => g != null && g.SettlementId == alarm.SettlementId);
                var guard = roster == null || roster.Guards == null ? null : roster.Guards.Find(g => g != null && g.Id == scene.ReportGuardId);
                Require(reporter != null && reporter.HeardViolence && escape != null &&
                    escape.BuildingKey == alarm.BuildingKey && scene.ReportedAt+0.001>=escape.ExitAt &&
                    scene.ReportedAt >= alarm.StartedAt && scene.ReportedAt < CivilianAlarms.ActiveUntil(alarm) &&
                    guard != null && (guard.IsAlive || guard.DiedAt >= scene.ReportedAt),
                    "Invalid saved physical hearing report.");
                if(scene.ReportGuardPosition!=null)
                {
                    var contact=scene.ReportGuardPosition;
                    var atReport=new AlarmPerson(); escape.Advance(atReport,scene.ReportedAt);
                    double dx=contact.X-atReport.X,dz=contact.Z-atReport.Z;
                    Require(!double.IsNaN(dx) && !double.IsInfinity(dx) && !double.IsNaN(dz) && !double.IsInfinity(dz) &&
                        dx*dx+dz*dz<=0.75*0.75+0.001,"Invalid saved guard contact position.");
                }
                else
                {
                    var endpoint=escape.ExteriorRoute[escape.ExteriorRoute.Count-1];
                    Require(escape.Arrived(scene.ReportedAt+0.001) && Math.Abs(endpoint.X-guard.Post.X)<0.5 && Math.Abs(endpoint.Z-guard.Post.Z)<0.5,
                        "Hearing report has no matching guard contact.");
                }
            }
            else Require(scene.ReporterId == null && scene.ReportGuardId == null && scene.ReportedAt == 0 && scene.ReportGuardPosition==null,
                "Unreported scene has invented contact.");
            Require(ValidTime(scene.LeftAt) && ValidTime(scene.DiscoveredAt) && ValidTime(scene.DiscoveredAt + UnwitnessedScene.OccupationSeconds) &&
                (scene.PlayerLeft ? scene.LeftAt >= alarm.StartedAt : scene.LeftAt == 0) &&
                (scene.Discovered ? (scene.PlayerLeft || scene.Reported) && scene.DiscoveredAt >= SceneReadyAt(scene) : scene.DiscoveredAt == 0),
                "Invalid scene chronology.");
            if (alarm.Corpse != null && alarm.Corpse.InspectionGuardId != null)
                Require(scene.Discovered || InspectionReport(alarm,world,alarm.Corpse.InspectionGuardId)!=null,
                    "Undiscovered scene has a corpse inspector without an arrived report.");
            return new UnwitnessedScene { PlayerLeft = scene.PlayerLeft, LeftAt = scene.LeftAt,
                Reported = scene.Reported, ReporterId = scene.ReporterId, ReportGuardId = scene.ReportGuardId, ReportedAt = scene.ReportedAt,
                ReportGuardPosition=scene.ReportGuardPosition==null ? null : new SavedRoutePoint{X=scene.ReportGuardPosition.X,Z=scene.ReportGuardPosition.Z},
                Discovered = scene.Discovered, DiscoveredAt = scene.DiscoveredAt };
        }
    }
}
