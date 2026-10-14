using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public enum CivilianReaction { Flee, Fight }
    [Serializable]
    public sealed class AlarmPerson
    {
        public string ResidentId;
        public bool DirectWitness;
        public CivilianReaction Reaction;
        public List<string> Warned = new List<string>();
        // Position belongs to the current town or interior origin, never the floating world origin.
        public bool HasPosition, ReachedRefuge;
        public int BuildingKey = -1;
        public double X, Y, Z;
        public CivilianEscape Escape;
    }
    [Serializable]
    public sealed class CivilianIncident
    {
        public CorpseState Corpse;
        public UnwitnessedScene Scene;
        public string Id, VictimId;
        public int SettlementId;
        public double StartedAt, ExpiresAt;
        public bool HasLocation;
        public int BuildingKey = -1;
        public double X, Y, Z;
        public List<AlarmPerson> People = new List<AlarmPerson>();
    }
    public static class CivilianAlarms
    {
        public static double ActiveUntil(CivilianIncident incident)
        {
            double until = incident.ExpiresAt;
            foreach (var person in incident.People)
                if (person.Escape != null) until = Math.Max(until, person.Escape.ReportAt + 300);
            return until;
        }
        public static CivilianReaction ChooseReaction(Resident resident)
        {
            // Initial capability policy, until professions and courage are modeled.
            return resident.AgeGroup == ResidentAgeGroup.Child ||
                StableSeed.Combine(resident.AppearanceSeed, 937, 0, 0) % 4 != 0 ? CivilianReaction.Flee : CivilianReaction.Fight;
        }
    }
    public sealed partial class WorldStateStore
    {
        public void LocateViolence(string incidentId, int buildingKey, double x, double y, double z)
        {
            Require(buildingKey >= -1 && ValidCoordinate(x) && ValidCoordinate(y) && ValidCoordinate(z), "Invalid crime location.");
            var incident = FindIncident(incidentId);
            Require(!incident.HasLocation || (incident.BuildingKey == buildingKey && incident.X == x &&
                incident.Y == y && incident.Z == z), "Crime location cannot change.");
            incident.HasLocation = true; incident.BuildingKey = buildingKey;
            incident.X = x; incident.Y = y; incident.Z = z;
        }
        public CivilianIncident RecordViolence(int settlementId, string victimId, double now)
        {
            Require(ValidTime(now), "Invalid incident time.");
            var victim = AlarmResident(settlementId, victimId);
            foreach (var existing in state.AlarmIncidents)
                if (existing.VictimId == victimId) return existing;
            Require(victim.IsAlive, "Cannot invent a new killing for a dead resident.");
            var incident = new CivilianIncident { Id = "murder:" + victimId, VictimId = victimId,
                SettlementId = settlementId, StartedAt = now, ExpiresAt = now + 900 };
            state.AlarmIncidents.Add(incident);
            return incident;
        }
        public AlarmPerson Witness(string incidentId, string residentId, double now)
        {
            var incident = FindIncident(incidentId);
            var resident = AlarmResident(incident.SettlementId, residentId);
            if (!Active(incident, now) || !resident.IsAlive || residentId == incident.VictimId) return null;
            var person = incident.People.Find(p => p.ResidentId == residentId);
            if (person == null)
            {
                person = new AlarmPerson { ResidentId = residentId, Reaction = CivilianAlarms.ChooseReaction(resident) };
                incident.People.Add(person);
            }
            person.DirectWitness = true;
            return person;
        }
        public bool Warn(string incidentId, string fromId, string toId, double now)
        {
            var incident = FindIncident(incidentId);
            var from = incident.People.Find(p => p.ResidentId == fromId);
            var target = AlarmResident(incident.SettlementId, toId);
            if (!Active(incident, now) || from == null || !from.DirectWitness || from.Warned.Count >= 2 ||
                !AlarmResident(incident.SettlementId, fromId).IsAlive || !target.IsAlive || toId == incident.VictimId ||
                incident.People.Exists(p => p.ResidentId == toId)) return false;
            from.Warned.Add(toId);
            incident.People.Add(new AlarmPerson { ResidentId = toId, Reaction = CivilianAlarms.ChooseReaction(target) });
            return true;
        }
        public CivilianIncident ActiveAlarm(string residentId, double now)
        {
            Require(ValidTime(now), "Invalid alarm query time.");
            for (int i = state.AlarmIncidents.Count - 1; i >= 0; i--)
                if (Active(state.AlarmIncidents[i], now) && state.AlarmIncidents[i].People.Exists(p => p.ResidentId == residentId))
                    return state.AlarmIncidents[i];
            return null;
        }
        private CivilianIncident FindIncident(string id)
        {
            var incident = state.AlarmIncidents.Find(a => a.Id == id);
            if (incident == null) throw new ArgumentException("Unknown incident.");
            return incident;
        }
        private Resident AlarmResident(int townId, string id)
        {
            var town = state.Settlements.Find(t => t.SettlementId == townId);
            if (town != null) foreach (var home in town.Households)
                foreach (var resident in home.Residents) if (resident.Id == id) return resident;
            throw new ArgumentException("Incident resident does not belong to this settlement.");
        }
        private static bool ValidTime(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
        private static bool Active(CivilianIncident incident, double now)
        { return ValidTime(now) && now >= incident.StartedAt && now < CivilianAlarms.ActiveUntil(incident); }
        private static List<CivilianIncident> CopyAlarms(WorldState source)
        {
            Require(source.AlarmIncidents != null, "Missing alarm ledger.");
            var result = new List<CivilianIncident>(); var incidentIds = new HashSet<string>(); var victims = new HashSet<string>();
            foreach (var alarm in source.AlarmIncidents)
            {
                Require(alarm != null && alarm.Id == "murder:" + alarm.VictimId && incidentIds.Add(alarm.Id) && victims.Add(alarm.VictimId), "Invalid or duplicate incident.");
                var town = source.Settlements.Find(t => t.SettlementId == alarm.SettlementId);
                Require(town != null, "Unknown incident settlement.");
                var residents = new Dictionary<string, Resident>();
                foreach (var home in town.Households) foreach (var resident in home.Residents) residents.Add(resident.Id, resident);
                Require(alarm.VictimId != null && residents.ContainsKey(alarm.VictimId) && ValidTime(alarm.StartedAt) &&
                    ValidTime(alarm.ExpiresAt) && alarm.ExpiresAt == alarm.StartedAt + 900 && alarm.People != null, "Invalid incident data.");
                var copied = new CivilianIncident { Id = alarm.Id, VictimId = alarm.VictimId, SettlementId = alarm.SettlementId,
                    StartedAt = alarm.StartedAt, ExpiresAt = alarm.ExpiresAt, HasLocation = alarm.HasLocation,
                    BuildingKey = alarm.BuildingKey, X = alarm.X, Y = alarm.Y, Z = alarm.Z };
                Require(alarm.BuildingKey >= -1 && ValidCoordinate(alarm.X) && ValidCoordinate(alarm.Y) && ValidCoordinate(alarm.Z),
                    "Invalid saved crime location.");
                copied.Scene = CopyScene(alarm, residents[alarm.VictimId]);
                copied.Corpse = CopyCorpse(alarm, residents[alarm.VictimId]);
                var people = new HashSet<string>();
                foreach (var person in alarm.People)
                {
                    Require(person != null && person.ResidentId != null && person.ResidentId != alarm.VictimId &&
                        residents.ContainsKey(person.ResidentId) && people.Add(person.ResidentId), "Invalid alarm recipient.");
                    Require(person.Reaction == CivilianReaction.Flee || person.Reaction == CivilianReaction.Fight, "Invalid reaction.");
                    Require(residents[person.ResidentId].AgeGroup != ResidentAgeGroup.Child || person.Reaction == CivilianReaction.Flee, "Child cannot fight.");
                    Require(person.Warned != null && person.Warned.Count <= (person.DirectWitness ? 2 : 0), "Invalid warning budget.");
                    Require(person.BuildingKey >= -1 && ValidCoordinate(person.X) && ValidCoordinate(person.Y) && ValidCoordinate(person.Z), "Invalid reaction position.");
                    var copy = new AlarmPerson { ResidentId = person.ResidentId, DirectWitness = person.DirectWitness,
                        Reaction = person.Reaction, HasPosition = person.HasPosition, ReachedRefuge = person.ReachedRefuge,
                        BuildingKey = person.BuildingKey, X = person.X, Y = person.Y, Z = person.Z,
                        Escape = CivilianEscape.Copy(person.Escape) };
                    Require(copy.Escape == null || (person.DirectWitness && person.HasPosition && person.Reaction == CivilianReaction.Flee &&
                        copy.Escape.BuildingKey == alarm.BuildingKey &&
                        copy.Escape.StartedAt >= alarm.StartedAt && copy.Escape.StartedAt < alarm.ExpiresAt), "Invalid witness escape ownership.");
                    var warned = new HashSet<string>();
                    foreach (var recipient in person.Warned)
                    {
                        Require(recipient != null && recipient != person.ResidentId && recipient != alarm.VictimId &&
                            residents.ContainsKey(recipient) && warned.Add(recipient) && alarm.People.Exists(p => p != null && p.ResidentId == recipient), "Invalid warning recipient.");
                        copy.Warned.Add(recipient);
                    }
                    copied.People.Add(copy);
                }
                result.Add(copied);
            }
            return result;
        }
        private static bool ValidCoordinate(double value) { return !double.IsNaN(value) && Math.Abs(value) <= 1000000; }
    }
}
