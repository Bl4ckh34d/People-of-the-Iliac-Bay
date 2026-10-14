using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    [Serializable]
    public sealed class UnwitnessedScene
    {
        public const double OccupationSeconds = 3600;
        public bool PlayerLeft, Discovered;
        public double LeftAt, DiscoveredAt;
        public bool Occupied(double now) { return Discovered && now >= DiscoveredAt && now < DiscoveredAt + UnwitnessedScene.OccupationSeconds; }
    }
    public sealed partial class WorldStateStore
    {
        public void RegisterUnwitnessedScene(string incidentId)
        {
            var alarm = FindIncident(incidentId); var victim = AlarmResident(alarm.SettlementId, alarm.VictimId);
            Require(!victim.IsAlive && alarm.HasLocation && VictimOwnsScene(alarm, victim, state) && alarm.People.Count == 0,
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
                if (scene.PlayerLeft) pending.Add(alarm);
            }
            return pending;
        }
        public void DiscoverCrimeScene(string incidentId, double now)
        {
            var alarm = FindIncident(incidentId); var scene = alarm.Scene;
            Require(scene != null && scene.PlayerLeft && ValidTime(now) && ValidTime(now + UnwitnessedScene.OccupationSeconds) && now >= scene.LeftAt,
                "Scene discovery requires departure and a valid guard arrival time.");
            if (scene.Discovered) return;
            var roster = state.Garrisons.Find(g => g.SettlementId == alarm.SettlementId);
            Require(roster != null, "Scene discovery requires municipal responders.");
            roster.Advance(now);
            var report = roster.Reports.Find(r => r.Id == "discovery:" + alarm.Id);
            Require(report != null && report.Investigation && report.BuildingKey == alarm.BuildingKey &&
                report.HasArrival && report.FirstArrivalAt >= scene.LeftAt && report.FirstArrivalAt <= now,
                "No investigation guard has reached this scene.");
            scene.Discovered = true; scene.DiscoveredAt = report.FirstArrivalAt;
        }
        private static bool VictimOwnsScene(CivilianIncident alarm, Resident victim, WorldState world)
        {
            if (alarm.BuildingKey < 0) return false;
            return alarm.BuildingKey == victim.HomeKey || world.NativePeople != null && world.NativePeople.Exists(p =>
                p.Id == victim.Id && p.SettlementId == alarm.SettlementId && p.WorkKey == alarm.BuildingKey);
        }
        private static UnwitnessedScene CopyScene(CivilianIncident alarm, Resident victim, WorldState world)
        {
            var scene = alarm.Scene;
            if (scene == null) return null;
            Require(!victim.IsAlive && alarm.HasLocation && VictimOwnsScene(alarm, victim, world) && alarm.People.Count == 0,
                "Invalid unwitnessed scene ownership.");
            Require(ValidTime(scene.LeftAt) && ValidTime(scene.DiscoveredAt) && ValidTime(scene.DiscoveredAt + UnwitnessedScene.OccupationSeconds) &&
                (scene.PlayerLeft ? scene.LeftAt >= alarm.StartedAt : scene.LeftAt == 0) &&
                (scene.Discovered ? scene.PlayerLeft && scene.DiscoveredAt >= scene.LeftAt : scene.DiscoveredAt == 0),
                "Invalid scene chronology.");
            return new UnwitnessedScene { PlayerLeft = scene.PlayerLeft, LeftAt = scene.LeftAt,
                Discovered = scene.Discovered, DiscoveredAt = scene.DiscoveredAt };
        }
    }
}
