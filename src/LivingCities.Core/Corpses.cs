using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    [Serializable]
    public sealed class OutdoorCorpseDiscovery
    {
        public string ObserverId;
        public bool Guard;
        public bool PlayerPresent;
        public double SeenAt;
    }
    [Serializable]
    public sealed class CorpseState
    {
        public double DiedAt, ClearedAt;
        public bool Cleared;
        public bool HasPosition;
        public int BuildingKey = -1;
        public double X, Y, Z;
        public int CleanupVersion;
        public OutdoorCorpseDiscovery Discovery;
    }
    public sealed partial class WorldStateStore
    {
        public List<CivilianIncident> OutdoorCorpses(int settlementId)
        {
            Require(settlementId >= 0, "Invalid corpse settlement.");
            return state.AlarmIncidents.FindAll(a => a.SettlementId == settlementId && a.Corpse != null && a.Scene == null &&
                (a.Corpse.HasPosition ? a.Corpse.BuildingKey : a.BuildingKey) == -1);
        }
        public void NoticeOutdoorCorpse(string incidentId, string observerId, bool guard, double now, bool playerPresent = false)
        {
            var alarm = FindIncident(incidentId);
            var body = RequireOutdoorBody(alarm);
            Require(ValidTime(now) && now >= body.DiedAt && observerId != null && observerId != alarm.VictimId,
                "Invalid outdoor corpse discovery.");
            if (guard)
            {
                var roster = state.Garrisons.Find(g => g.SettlementId == alarm.SettlementId);
                Require(roster != null && roster.Guards.Exists(g => g.Id == observerId && g.IsAlive), "Unknown or dead guard observer.");
            }
            else Require(AlarmResident(alarm.SettlementId, observerId).IsAlive, "Dead resident cannot discover a corpse.");
            if (body.Discovery == null && !body.Cleared)
                body.Discovery = new OutdoorCorpseDiscovery { ObserverId = observerId, Guard = guard, SeenAt = now, PlayerPresent = playerPresent };
        }
        public void ObserveOutdoorCorpse(string incidentId, bool playerOnSite, double now)
        {
            var body = RequireOutdoorBody(FindIncident(incidentId));
            Require(ValidTime(now), "Invalid outdoor observation time.");
            if (!body.Cleared && body.Discovery != null && !playerOnSite && now >= body.Discovery.SeenAt)
            { body.Cleared = true; body.ClearedAt = now; }
        }
        private static CorpseState RequireOutdoorBody(CivilianIncident alarm)
        {
            Require(alarm.Corpse != null && (alarm.Corpse.HasPosition ? alarm.Corpse.BuildingKey : alarm.BuildingKey) == -1 &&
                alarm.Scene == null, "Outdoor observation needs a confirmed exterior corpse.");
            return alarm.Corpse;
        }
        public void ConfirmCorpse(string incidentId, double now)
        {
            var alarm = FindIncident(incidentId);
            Require(!AlarmResident(alarm.SettlementId, alarm.VictimId).IsAlive && alarm.HasLocation &&
                ValidTime(now) && now >= alarm.StartedAt, "Corpse needs a confirmed positioned death.");
            if (alarm.Corpse == null) alarm.Corpse = new CorpseState { DiedAt = now, CleanupVersion = 1 };
        }
        public void ConfirmCorpse(string incidentId, double now, int buildingKey, double x, double y, double z)
        {
            Require(buildingKey >= -1 && ValidCoordinate(x) && ValidCoordinate(y) && ValidCoordinate(z), "Invalid corpse position.");
            ConfirmCorpse(incidentId, now);
            var body = FindIncident(incidentId).Corpse;
            if (body.HasPosition) return;
            body.HasPosition = true; body.BuildingKey = buildingKey; body.X = x; body.Y = y; body.Z = z;
        }
        public List<CivilianIncident> ObserveCorpses(int settlementId, int? currentBuilding, double now)
        {
            Require(settlementId >= 0 && (!currentBuilding.HasValue || currentBuilding.Value >= -1) && ValidTime(now),
                "Invalid corpse observation.");
            var visible = new List<CivilianIncident>();
            foreach (var alarm in state.AlarmIncidents)
            {
                var corpse = alarm.Corpse;
                if (alarm.SettlementId != settlementId || corpse == null || corpse.Cleared || now < corpse.DiedAt) continue;
                bool discovered = alarm.Scene != null && alarm.Scene.Discovered && now >= alarm.Scene.DiscoveredAt;
                int building = corpse.HasPosition ? corpse.BuildingKey : alarm.BuildingKey;
                bool unattended = currentBuilding.HasValue && (building < 0 || currentBuilding.Value != building);
                if (discovered || (building >= 0 && alarm.Scene == null && unattended && now >= corpse.DiedAt + 86400))
                {
                    corpse.Cleared = true; corpse.ClearedAt = discovered ? alarm.Scene.DiscoveredAt : now;
                }
                else visible.Add(alarm);
            }
            return visible;
        }
        private static CorpseState CopyCorpse(CivilianIncident alarm, Resident victim, WorldState source)
        {
            var corpse = alarm.Corpse;
            if (corpse == null) return null;
            Require(corpse.CleanupVersion == 0 || corpse.CleanupVersion == 1, "Unknown corpse cleanup version.");
            Require(!victim.IsAlive && alarm.HasLocation && ValidTime(corpse.DiedAt) && corpse.DiedAt >= alarm.StartedAt &&
                ValidTime(corpse.ClearedAt) && (corpse.Cleared ? corpse.ClearedAt >= corpse.DiedAt : corpse.ClearedAt == 0),
                "Invalid corpse chronology or ownership.");
            Require(!corpse.Cleared || (alarm.Scene != null ? alarm.Scene.Discovered && corpse.ClearedAt == alarm.Scene.DiscoveredAt :
                corpse.Discovery != null ? corpse.ClearedAt >= corpse.Discovery.SeenAt :
                ((corpse.HasPosition ? corpse.BuildingKey : alarm.BuildingKey) >= 0 || corpse.CleanupVersion == 0) &&
                corpse.ClearedAt >= corpse.DiedAt + 86400), "Corpse cleanup lacks discovery or elapsed day.");
            Require(!corpse.HasPosition || (corpse.BuildingKey >= -1 && ValidCoordinate(corpse.X) &&
                ValidCoordinate(corpse.Y) && ValidCoordinate(corpse.Z)), "Invalid saved corpse position.");
            OutdoorCorpseDiscovery discovery = null;
            if (corpse.Discovery != null)
            {
                RequireOutdoorBody(alarm);
                var seen = corpse.Discovery;
                Require(seen.ObserverId != null && seen.ObserverId != victim.Id && ValidTime(seen.SeenAt) && seen.SeenAt >= corpse.DiedAt,
                    "Invalid saved corpse discovery.");
                if (seen.Guard)
                {
                    Require(source.Garrisons != null, "Missing observer garrison ledger.");
                    var roster = source.Garrisons.Find(g => g != null && g.SettlementId == alarm.SettlementId);
                    Require(roster != null && roster.Guards != null && roster.Guards.Exists(g => g != null && g.Id == seen.ObserverId &&
                        (g.IsAlive || g.DiedAt >= seen.SeenAt)), "Unknown corpse guard observer or observer died before discovery.");
                }
                else
                {
                    var town = source.Settlements.Find(t => t.SettlementId == alarm.SettlementId);
                    Require(town != null && town.Households.Exists(h => h.Residents.Exists(r => r.Id == seen.ObserverId)),
                        "Unknown corpse resident observer.");
                }
                discovery = new OutdoorCorpseDiscovery { ObserverId = seen.ObserverId, Guard = seen.Guard, SeenAt = seen.SeenAt,
                    PlayerPresent = seen.PlayerPresent };
            }
            return new CorpseState { DiedAt = corpse.DiedAt, ClearedAt = corpse.ClearedAt, Cleared = corpse.Cleared,
                HasPosition = corpse.HasPosition, BuildingKey = corpse.BuildingKey, X = corpse.X, Y = corpse.Y, Z = corpse.Z,
                CleanupVersion = corpse.Cleared ? corpse.CleanupVersion : 1,
                Discovery = discovery };
        }
    }
}
