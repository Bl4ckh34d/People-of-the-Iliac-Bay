using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    [Serializable]
    public sealed class CorpseState
    {
        public double DiedAt, ClearedAt;
        public bool Cleared;
        public bool HasPosition;
        public int BuildingKey = -1;
        public double X, Y, Z;
    }
    public sealed partial class WorldStateStore
    {
        public void ConfirmCorpse(string incidentId, double now)
        {
            var alarm = FindIncident(incidentId);
            Require(!AlarmResident(alarm.SettlementId, alarm.VictimId).IsAlive && alarm.HasLocation &&
                ValidTime(now) && now >= alarm.StartedAt, "Corpse needs a confirmed positioned death.");
            if (alarm.Corpse == null) alarm.Corpse = new CorpseState { DiedAt = now };
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
                if (discovered || (alarm.Scene == null && unattended && now >= corpse.DiedAt + 86400))
                {
                    corpse.Cleared = true; corpse.ClearedAt = discovered ? alarm.Scene.DiscoveredAt : now;
                }
                else visible.Add(alarm);
            }
            return visible;
        }
        private static CorpseState CopyCorpse(CivilianIncident alarm, Resident victim)
        {
            var corpse = alarm.Corpse;
            if (corpse == null) return null;
            Require(!victim.IsAlive && alarm.HasLocation && ValidTime(corpse.DiedAt) && corpse.DiedAt >= alarm.StartedAt &&
                ValidTime(corpse.ClearedAt) && (corpse.Cleared ? corpse.ClearedAt >= corpse.DiedAt : corpse.ClearedAt == 0),
                "Invalid corpse chronology or ownership.");
            Require(!corpse.Cleared || (alarm.Scene != null ? alarm.Scene.Discovered && corpse.ClearedAt == alarm.Scene.DiscoveredAt :
                corpse.ClearedAt >= corpse.DiedAt + 86400), "Corpse cleanup lacks discovery or elapsed day.");
            Require(!corpse.HasPosition || (corpse.BuildingKey >= -1 && ValidCoordinate(corpse.X) &&
                ValidCoordinate(corpse.Y) && ValidCoordinate(corpse.Z)), "Invalid saved corpse position.");
            return new CorpseState { DiedAt = corpse.DiedAt, ClearedAt = corpse.ClearedAt, Cleared = corpse.Cleared,
                HasPosition = corpse.HasPosition, BuildingKey = corpse.BuildingKey, X = corpse.X, Y = corpse.Y, Z = corpse.Z };
        }
    }
}
