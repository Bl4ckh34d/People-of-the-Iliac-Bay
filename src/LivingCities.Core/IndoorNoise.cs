using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public enum NoiseLevel { QuietSteps, Theft, Violence }
    [Serializable]
    public sealed class NoiseObservation
    {
        public string ResidentId;
        public bool Sleeping, Woke;
        public int Stealth, Roll;
        public double Distance, ObservedAt;
    }
    public static class NoiseAwareness
    {
        public static int WakeChance(NoiseLevel level, int stealth, double distance)
        {
            if (!Enum.IsDefined(typeof(NoiseLevel), level) || stealth < 0 || stealth > 100 ||
                double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0)
                throw new ArgumentException("Invalid noise inputs.");
            double chance = level == NoiseLevel.Violence ? 95 - stealth * 0.15 - distance * 1.5 :
                level == NoiseLevel.Theft ? 55 - stealth * 0.35 - distance * 4 : 30 - stealth * 0.25 - distance * 5;
            return (int)Math.Max(0, Math.Min(100, Math.Floor(chance)));
        }
    }
    public sealed partial class WorldStateStore
    {
        public AlarmPerson HearViolence(string incidentId, string residentId, int buildingKey,
            double now, bool sleeping, int stealth, double distance, int roll)
        {
            int chance = NoiseAwareness.WakeChance(NoiseLevel.Violence, stealth, distance);
            Require(ValidTime(now) && buildingKey >= 0 && roll >= 0 && roll < 100, "Invalid noise observation.");
            var incident = FindIncident(incidentId);
            var resident = AlarmResident(incident.SettlementId, residentId);
            if (!Active(incident, now) || !resident.IsAlive || residentId == incident.VictimId ||
                !incident.HasLocation || incident.BuildingKey != buildingKey) return null;
            var previous = incident.NoiseChecks.Find(n => n.ResidentId == residentId);
            if (previous != null) return !previous.Sleeping || previous.Woke ?
                incident.People.Find(p => p.ResidentId == residentId) : null;
            // Noise is situational. A newly arriving observer must not hear an old
            // attack simply because its aftermath remains an active incident.
            if (now > incident.StartedAt + 5) return null;
            bool woke = sleeping && roll < chance;
            incident.NoiseChecks.Add(new NoiseObservation { ResidentId = residentId,
                Sleeping = sleeping, Woke = woke, Stealth = stealth, Roll = roll,
                Distance = distance, ObservedAt = now });
            if (sleeping && !woke) return null;
            var person = incident.People.Find(p => p.ResidentId == residentId);
            if (person == null)
            {
                person = new AlarmPerson { ResidentId = residentId, Reaction = CivilianReaction.Flee };
                incident.People.Add(person);
            }
            person.HeardViolence = true; person.Awakened = woke;
            if (!person.DirectWitness) person.Reaction = CivilianReaction.Flee;
            var native = state.NativePeople.Find(p => p.Id == residentId);
            if (native != null) native.ResponseIncidentId = incident.Id;
            return person;
        }
        private static List<NoiseObservation> CopyNoiseChecks(CivilianIncident incident, Dictionary<string, Resident> residents)
        {
            var result = new List<NoiseObservation>(); var recipients = new HashSet<string>();
            if (incident.NoiseChecks == null) return result; // Optional legacy field.
            foreach (var check in incident.NoiseChecks)
            {
                Require(check != null && check.ResidentId != null && check.ResidentId != incident.VictimId &&
                    residents.ContainsKey(check.ResidentId) && recipients.Add(check.ResidentId) &&
                    incident.HasLocation && incident.BuildingKey >= 0, "Invalid noise recipient.");
                int chance = NoiseAwareness.WakeChance(NoiseLevel.Violence, check.Stealth, check.Distance);
                Require(check.Roll >= 0 && check.Roll < 100 && ValidTime(check.ObservedAt) &&
                    check.ObservedAt >= incident.StartedAt && check.ObservedAt <= incident.StartedAt + 5 &&
                    check.Woke == (check.Sleeping && check.Roll < chance), "Invalid saved wake decision.");
                var person = incident.People.Find(p => p != null && p.ResidentId == check.ResidentId);
                if (!check.Sleeping || check.Woke)
                    Require(person != null && person.HeardViolence && person.Awakened == check.Woke,
                        "Accepted noise observation lacks matching reaction.");
                else Require(person == null || (!person.HeardViolence && !person.Awakened), "Failed wake decision cannot invent hearing.");
                result.Add(new NoiseObservation { ResidentId = check.ResidentId, Sleeping = check.Sleeping,
                    Woke = check.Woke, Stealth = check.Stealth, Roll = check.Roll, Distance = check.Distance, ObservedAt = check.ObservedAt });
            }
            return result;
        }
    }
}
