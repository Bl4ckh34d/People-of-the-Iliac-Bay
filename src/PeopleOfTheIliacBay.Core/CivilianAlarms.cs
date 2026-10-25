using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum CivilianReaction { Flee, Fight }
    [Serializable]
    public sealed class AlarmPerson
    {
        public string ResidentId;
        public bool DirectWitness;
        public bool HeardViolence, Awakened;
        public bool InnAlarm;
        public bool CorpseObserver;
        public double CorpseSeenAt;
        public CivilianReaction Reaction;
        public List<string> Warned = new List<string>();
        // Position belongs to the current town or interior origin, never the floating world origin.
        public bool HasPosition, ReachedRefuge;
        public bool RefugeSealed;
        public double RefugeUntil;
        public OutdoorBodyReport OutdoorReport;
        public bool StaticWitness, StatementDelivered;
        public string InterviewGuardId;
        public int BuildingKey = -1;
        public double X, Y, Z;
        public CivilianEscape Escape;
        // Last authoritative update; zero means an older save without this field.
        public double EscapeAdvancedAt;
    }
    [Serializable]
    public sealed class CivilianIncident
    {
        public long GenerationSerial;
        public CorpseState Corpse;
        public UnwitnessedScene Scene;
        public string Id, VictimId;
        // Null retains legacy player-crime semantics.
        public string ThreatId;
        public string KillerThreatId;
        // Set only by the explicit player-hit path, never inferred from null ThreatId.
        public bool PlayerAttributed;
        // Latches accusation for this incident so pre-report CrimeCommitted=None cannot resolve fear.
        public bool AccusationIssued;
        public bool CrimeResolved;
        public int SettlementId;
        public double StartedAt, ExpiresAt;
        public bool HasLocation;
        public int BuildingKey = -1;
        public double X, Y, Z;
        public List<AlarmPerson> People = new List<AlarmPerson>();
        public List<NoiseObservation> NoiseChecks = new List<NoiseObservation>();
    }
    public static class CivilianAlarms
    {
        private static bool ValidTime(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
        public static double ActiveUntil(CivilianIncident incident)
        {
            double until = incident.ExpiresAt;
            foreach (var person in incident.People)
            {
                if(person.RefugeSealed) until=Math.Max(until,person.RefugeUntil);
                if (person.CorpseObserver) until = Math.Max(until, person.CorpseSeenAt + 900);
                if (person.Escape != null) until = Math.Max(until, person.Escape.ReportAt + 300);
            }
            return until;
        }
        public static bool PersonActive(CivilianIncident incident, AlarmPerson person, double now)
        {
            if (!ValidTime(now) || now < incident.StartedAt) return false;
            if(person.OutdoorReport!=null && !person.DirectWitness && !person.HeardViolence && !person.InnAlarm && !person.CorpseObserver) return false;
            if(person.RefugeSealed && person.RefugeUntil>0)
                return now<person.RefugeUntil && !(incident.CrimeResolved && incident.Corpse!=null && incident.Corpse.Cleared);
            if (HasPersistentMurderFear(incident, person.ResidentId)) return true;
            if (incident.PlayerAttributed && incident.CrimeResolved && IsPersistentMurderWitness(incident, person.ResidentId)) return false;
            double until = incident.ExpiresAt;
            if (person.CorpseObserver) until = Math.Max(until, person.CorpseSeenAt + 900);
            if (person.Escape != null) until = Math.Max(until, person.Escape.ReportAt + 300);
            return now < until;
        }
        internal static bool HasPersistentMurderFear(CivilianIncident incident, string residentId)
        { return (incident.PlayerAttributed || incident.KillerThreatId!=null) && !incident.CrimeResolved && IsPersistentMurderWitness(incident, residentId); }
        internal static bool IsPersistentMurderWitness(CivilianIncident incident, string residentId)
        {
            var person = incident.People.Find(p => p.ResidentId == residentId);
            return person != null && (person.DirectWitness || incident.People.Exists(witness => witness.DirectWitness && witness.Warned.Contains(residentId)));
        }
        public static CivilianReaction ChooseReaction(Resident resident)
        {
            // Native merchant combat capability is not modeled yet. Their first response is flight.
            if (resident.Id != null && resident.Id.StartsWith("npc:", StringComparison.Ordinal)) return CivilianReaction.Flee;
            // Initial capability policy, until professions and courage are modeled.
            return resident.AgeGroup == ResidentAgeGroup.Child ||
                StableSeed.Combine(resident.AppearanceSeed, 937, 0, 0) % 4 != 0 ? CivilianReaction.Flee : CivilianReaction.Fight;
        }
    }
    public sealed partial class WorldStateStore
    {
        public CivilianIncident RecordHostileThreat(int settlementId,string threatId,double now)
        {
            Require(!string.IsNullOrWhiteSpace(threatId) && threatId.Length<=256 && ValidTime(now) && ValidTime(now+900) &&
                state.Settlements.Exists(s=>s.SettlementId==settlementId),"Invalid hostile threat.");
            string id="threat:"+settlementId+":"+threatId;
            var existing=state.AlarmIncidents.Find(a=>a.Id==id);
            if(existing!=null && Active(existing,now)) return existing;
            Require(state.NextIncidentSerial > 0 && state.NextIncidentSerial < long.MaxValue - 1,
                "Incident generation serial exhausted.");
            if(existing!=null)
            {
                state.AlarmIncidents.Remove(existing);
                foreach(var native in state.NativePeople) if(native.ResponseIncidentId==id) native.ResponseIncidentId=null;
            }
            var incident=new CivilianIncident { Id=id,ThreatId=threatId,SettlementId=settlementId,StartedAt=now,ExpiresAt=now+900,
                GenerationSerial=state.NextIncidentSerial++ };
            state.AlarmIncidents.Add(incident); return incident;
        }
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
            ResidentTrip visitingTrip;
            if(residentTripIndex.TryGetValue(victimId,out visitingTrip) && visitingTrip.HomeSettlement!=settlementId)
            {
                ActivitySnapshot present;
                Require(TryDestinationResidentState(victimId,settlementId,now,out present),
                    "Resident is not visiting this settlement.");
            }
            Require(victim.IsAlive, "Cannot invent a new killing for a dead resident.");
            Require(state.NextIncidentSerial > 0 && state.NextIncidentSerial < long.MaxValue - 1,
                "Incident generation serial exhausted.");
            var incident = new CivilianIncident { Id = "murder:" + victimId, VictimId = victimId,
                SettlementId = settlementId, StartedAt = now, ExpiresAt = now + 900,
                GenerationSerial=state.NextIncidentSerial++ };
            state.AlarmIncidents.Add(incident);
            return incident;
        }
        public void MarkPlayerMurder(string incidentId)
        {
            var incident = FindIncident(incidentId);
            Require(incident.ThreatId == null && incident.VictimId != null && incident.Corpse != null,
                "Only confirmed non-hostile deaths can be attributed to the player.");
            incident.PlayerAttributed = true;
            foreach (var person in incident.People)
                if (CivilianAlarms.IsPersistentMurderWitness(incident, person.ResidentId)) person.Reaction = CivilianReaction.Flee;
        }
        public void MarkMurderAccusationIssued(string incidentId)
        {
            var incident = FindIncident(incidentId);
            Require(incident.ThreatId == null && incident.VictimId != null && incident.PlayerAttributed && incident.Corpse != null,
                "Only a player-attributed murder can be accused.");
            incident.AccusationIssued = true;
            incident.CrimeResolved = false;
        }
        public void ResolveAccusedPlayerMurders()
        {
            foreach (var incident in state.AlarmIncidents)
                if (incident.PlayerAttributed && incident.AccusationIssued) incident.CrimeResolved = true;
        }
        public bool HasPersistentMurderFear(string residentId)
        {
            if (string.IsNullOrEmpty(residentId)) return false;
            return state.AlarmIncidents.Exists(incident => CivilianAlarms.HasPersistentMurderFear(incident, residentId));
        }
        public CivilianIncident PersistentMurderAlarm(string residentId)
        {
            if (string.IsNullOrEmpty(residentId)) return null;
            for (int i = state.AlarmIncidents.Count - 1; i >= 0; i--)
                if (CivilianAlarms.HasPersistentMurderFear(state.AlarmIncidents[i], residentId)) return state.AlarmIncidents[i];
            return null;
        }
        public AlarmPerson Witness(string incidentId, string residentId, double now)
        {
            var incident = FindIncident(incidentId);
            var resident = AlarmResident(incident.SettlementId, residentId);
            if (!Active(incident, now) || !resident.IsAlive || residentId == incident.VictimId) return null;
            var person = incident.People.Find(p => p.ResidentId == residentId);
            if (person == null)
            {
                person = new AlarmPerson { ResidentId = residentId, Reaction = incident.ThreatId!=null || incident.PlayerAttributed && !incident.CrimeResolved ?
                    CivilianReaction.Flee : CivilianAlarms.ChooseReaction(resident) };
                incident.People.Add(person);
            }
            if (incident.PlayerAttributed && !incident.CrimeResolved) person.Reaction = CivilianReaction.Flee;
            // Hearing alone never authorizes attacking an unidentified player.
            // Sight can restore the resident's capability before flight starts.
            if (!person.DirectWitness && person.HeardViolence && person.Escape == null)
                person.Reaction = CivilianAlarms.ChooseReaction(resident);
            person.DirectWitness = true;
            var native = state.NativePeople.Find(p => p.Id == residentId);
            if (native != null) native.ResponseIncidentId = incident.Id;
            return person;
        }
        public AlarmPerson AlertInnOccupant(string incidentId, string residentId, int buildingKey, double now)
        {
            var incident = FindIncident(incidentId);
            var resident = AlarmResident(incident.SettlementId, residentId);
            Require(buildingKey >= 0 && incident.ThreatId == null && incident.BuildingKey == buildingKey && ValidTime(now),
                "Invalid inn alarm.");
            if (!Active(incident, now) || !resident.IsAlive || residentId == incident.VictimId) return null;
            var person = incident.People.Find(p => p.ResidentId == residentId);
            if (person == null)
            {
                person = new AlarmPerson { ResidentId = residentId, Reaction = CivilianReaction.Flee };
                incident.People.Add(person);
            }
            person.InnAlarm = true;
            person.Reaction = CivilianReaction.Flee;
            var native = state.NativePeople.Find(p => p.Id == residentId);
            if (native != null) native.ResponseIncidentId = incident.Id;
            return person;
        }
        public bool Warn(string incidentId, string fromId, string toId, double now)
        {
            var incident = FindIncident(incidentId);
            var from = incident.People.Find(p => p.ResidentId == fromId);
            var target = AlarmResident(incident.SettlementId, toId);
            if (!Active(incident, now) || incident.PlayerAttributed && incident.CrimeResolved || from == null || !from.DirectWitness || from.Warned.Count >= 2 ||
                !AlarmResident(incident.SettlementId, fromId).IsAlive || !target.IsAlive || toId == incident.VictimId ||
                incident.People.Exists(p => p.ResidentId == toId)) return false;
            from.Warned.Add(toId);
            incident.People.Add(new AlarmPerson { ResidentId = toId, Reaction = incident.ThreatId!=null || incident.PlayerAttributed && !incident.CrimeResolved ?
                CivilianReaction.Flee : CivilianAlarms.ChooseReaction(target) });
            var native = state.NativePeople.Find(p => p.Id == toId);
            if (native != null) native.ResponseIncidentId = incident.Id;
            return true;
        }
        public CivilianIncident ActiveAlarm(string residentId, double now)
        {
            Require(ValidTime(now), "Invalid alarm query time.");
            for (int i = state.AlarmIncidents.Count - 1; i >= 0; i--)
            {
                var incident = state.AlarmIncidents[i];
                var person = incident.People.Find(p => p.ResidentId == residentId);
                if (person != null && CivilianAlarms.PersonActive(incident, person, now)) return incident;
            }
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
            var native = state.NativePeople.Find(p => p.SettlementId == townId && p.Id == id);
            if (native != null) return NativeAlarmResident(native);
            ResidentTrip trip;
            bool visiting=residentTripIndex.TryGetValue(id,out trip) && trip.VisitsSettlement(townId);
            bool historical=state.AlarmIncidents.Exists(a=>a.SettlementId==townId &&
                (a.VictimId==id || a.People.Exists(p=>p.ResidentId==id))) ||
                state.EntryReports.Exists(r=>r.SettlementId==townId && r.CitizenWitnesses.Contains(id));
            if(visiting || historical)
            {
                foreach(var origin in state.Settlements)
                    foreach(var home in origin.Households)
                    {
                        var resident=home.Residents.Find(r=>r.Id==id);
                        if(resident!=null) return resident;
                    }
                foreach(var retired in state.HouseholdHistory)
                {
                    var resident=retired.Household.Residents.Find(r=>r.Id==id);
                    if(resident!=null) return resident;
                }
            }
            var traveler=state.Travelers.Find(p=>p.Id==id);
            if(traveler!=null && state.TravelerVisits.Exists(v=>v.PersonId==id && v.SettlementId==townId))
                return TravelerAlarmResident(traveler);
            throw new ArgumentException("Incident resident does not belong to this settlement.");
        }
        private static Resident TravelerAlarmResident(TravelerPerson person)
        {
            return new Resident { Id=person.Id,FullName=person.Name,HomeKey=-1,IsAlive=person.IsAlive,
                Race=person.Race,Sex=person.Sex,AppearanceSeed=person.AppearanceSeed,AgeGroup=ResidentAgeGroup.Adult };
        }
        private static Resident NativeAlarmResident(NativePerson person)
        {
            return new Resident { Id = person.Id, HomeKey = person.HomeKey, IsAlive = person.IsAlive,
                Sex = person.Sex, AppearanceSeed = StableSeed.Combine(0, (uint)person.SettlementId,
                    (uint)person.WorkKey, unchecked((uint)person.NativeHash)) };
        }
        private static bool ValidTime(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
        private static bool Active(CivilianIncident incident, double now)
        {
            if (!ValidTime(now) || now < incident.StartedAt) return false;
            if (now < CivilianAlarms.ActiveUntil(incident)) return true;
            // Keep the incident available to escape/report paths only while an
            // unresolved player-murder witness still has persistent fear.
            foreach (var person in incident.People)
                if (CivilianAlarms.PersonActive(incident, person, now) &&
                    CivilianAlarms.HasPersistentMurderFear(incident, person.ResidentId)) return true;
            return false;
        }
        private static List<CivilianIncident> CopyAlarms(WorldState source)
        {
            Require(source.AlarmIncidents != null, "Missing alarm ledger.");
            var result = new List<CivilianIncident>(); var incidentIds = new HashSet<string>(); var victims = new HashSet<string>();
            var serials = new HashSet<long>();
            foreach (var alarm in source.AlarmIncidents)
            {
                bool threat=alarm!=null && alarm.ThreatId!=null;
                if (source.Version >= 47)
                    Require(alarm != null && alarm.GenerationSerial > 0 && serials.Add(alarm.GenerationSerial),
                        "Invalid or duplicate incident generation serial.");
                Require(alarm != null && incidentIds.Add(alarm.Id) && (threat ?
                    !string.IsNullOrWhiteSpace(alarm.ThreatId) && alarm.ThreatId.Length<=256 && alarm.Id=="threat:"+alarm.SettlementId+":"+alarm.ThreatId :
                    alarm.Id == "murder:" + alarm.VictimId && victims.Add(alarm.VictimId)), "Invalid or duplicate incident.");
                var town = source.Settlements.Find(t => t.SettlementId == alarm.SettlementId);
                Require(town != null, "Unknown incident settlement.");
                var residents = new Dictionary<string, Resident>();
                foreach (var home in town.Households) foreach (var resident in home.Residents) residents.Add(resident.Id, resident);
                // Incidents outlive trips. Resolve referenced original residents
                // even after their latest trip has been replaced or completed.
                foreach(var origin in source.Settlements)
                    if(origin.SettlementId!=alarm.SettlementId)
                        foreach(var home in origin.Households) foreach(var resident in home.Residents)
                            if(resident.Id==alarm.VictimId || alarm.People!=null && alarm.People.Exists(p=>p!=null && p.ResidentId==resident.Id))
                                residents.Add(resident.Id,resident);
                if (source.HouseholdHistory != null) foreach (var retired in source.HouseholdHistory)
                    foreach (var resident in retired.Household.Residents)
                        if (retired.SettlementId == alarm.SettlementId || resident.Id == alarm.VictimId ||
                            alarm.People != null && alarm.People.Exists(p => p != null && p.ResidentId == resident.Id))
                            residents.Add(resident.Id, resident);
                // Native staff without a generated household are also persistent victims and witnesses.
                if (source.NativePeople != null) foreach (var native in source.NativePeople)
                    if (native.SettlementId == alarm.SettlementId && !residents.ContainsKey(native.Id)) residents.Add(native.Id, NativeAlarmResident(native));
                if(source.Version>=9)
                {
                    Require(source.Travelers!=null && source.TravelerVisits!=null,"Missing traveler crime identities.");
                    foreach(var traveler in source.Travelers)
                    {
                        var validated=CopyTraveler(traveler);
                        if(validated.Id==alarm.VictimId && !validated.IsAlive)
                            Require(validated.DiedAt>=alarm.StartedAt && (alarm.Corpse==null || validated.DiedAt<=alarm.Corpse.DiedAt),
                                "Traveler death conflicts with incident chronology.");
                        if(source.TravelerVisits.Exists(v=>v!=null && v.PersonId==validated.Id && v.SettlementId==alarm.SettlementId))
                        {
                            Require(!residents.ContainsKey(validated.Id),"Traveler duplicates civilian crime identity.");
                            residents.Add(validated.Id,TravelerAlarmResident(validated));
                        }
                    }
                }
                Require((threat ? alarm.VictimId==null && alarm.Scene==null && alarm.Corpse==null && alarm.NoiseChecks!=null && alarm.NoiseChecks.Count==0 :
                    alarm.VictimId != null && residents.ContainsKey(alarm.VictimId)) && ValidTime(alarm.StartedAt) &&
                    ValidTime(alarm.ExpiresAt) && alarm.ExpiresAt == alarm.StartedAt + 900 && alarm.People != null, "Invalid incident data.");
                bool playerAttributed = source.Version >= 19 && alarm.PlayerAttributed;
                bool accusationIssued = source.Version >= 19 && alarm.AccusationIssued;
                bool crimeResolved = source.Version >= 19 && alarm.CrimeResolved;
                Require(alarm.KillerThreatId==null || !threat && !playerAttributed && !string.IsNullOrWhiteSpace(alarm.KillerThreatId) && alarm.KillerThreatId.Length<=256,"Invalid murderer identity.");
                Require(!threat || (!playerAttributed && !accusationIssued && !crimeResolved), "Hostile threat cannot be attributed as a player murder.");
                Require(!accusationIssued || playerAttributed, "Murder accusation lacks player attribution.");
                Require(!crimeResolved || accusationIssued || alarm.KillerThreatId!=null, "Resolved murder lacks an accusation.");
                var copied = new CivilianIncident { Id = alarm.Id, VictimId = alarm.VictimId, ThreatId=alarm.ThreatId,SettlementId = alarm.SettlementId,
                    KillerThreatId=alarm.KillerThreatId,
                    GenerationSerial=source.Version >= 47 ? alarm.GenerationSerial : 0,
                    PlayerAttributed = playerAttributed, AccusationIssued = accusationIssued, CrimeResolved = crimeResolved,
                    StartedAt = alarm.StartedAt, ExpiresAt = alarm.ExpiresAt, HasLocation = alarm.HasLocation,
                    BuildingKey = alarm.BuildingKey, X = alarm.X, Y = alarm.Y, Z = alarm.Z };
                Require(alarm.BuildingKey >= -1 && ValidCoordinate(alarm.X) && ValidCoordinate(alarm.Y) && ValidCoordinate(alarm.Z),
                    "Invalid saved crime location.");
                if(!threat)
                {
                    copied.Scene = CopyScene(alarm, residents[alarm.VictimId], source);
                    copied.Corpse = CopyCorpse(alarm, residents[alarm.VictimId], source);
                }
                Require(!playerAttributed || copied.Corpse != null, "Player attribution requires confirmed death.");
                copied.NoiseChecks = CopyNoiseChecks(alarm, residents);
                var people = new HashSet<string>();
                foreach (var person in alarm.People)
                {
                    Require(person != null && person.ResidentId != null && person.ResidentId != alarm.VictimId &&
                        residents.ContainsKey(person.ResidentId) && people.Add(person.ResidentId), "Invalid alarm recipient.");
                    Require(person.Reaction == CivilianReaction.Flee || person.Reaction == CivilianReaction.Fight, "Invalid reaction.");
                    Require(!threat || person.Reaction==CivilianReaction.Flee,"Hostile threat recipient must flee.");
                    Require(person.Warned != null && person.Warned.Count <= (person.DirectWitness ? 2 : 0), "Invalid warning budget.");
                    Require(!person.HeardViolence || (copied.NoiseChecks.Exists(n => n.ResidentId == person.ResidentId &&
                        (!n.Sleeping || n.Woke)) && (person.DirectWitness || person.Reaction == CivilianReaction.Flee)), "Invalid hearing response.");
                    bool persistentWitness = playerAttributed && !crimeResolved &&
                        (person.DirectWitness || alarm.People.Exists(witness => witness != null && witness.DirectWitness &&
                            witness.Warned != null && witness.Warned.Contains(person.ResidentId)));
                    Require(!persistentWitness || person.Reaction == CivilianReaction.Flee,
                        "Persistent player-murder witness must flee.");
                    Require(!person.Awakened || person.HeardViolence, "Awakening lacks hearing evidence.");
                    Require(residents[person.ResidentId].AgeGroup != ResidentAgeGroup.Child || person.Reaction == CivilianReaction.Flee, "Child cannot fight.");
                    Require(person.BuildingKey >= -1 && ValidCoordinate(person.X) && ValidCoordinate(person.Y) && ValidCoordinate(person.Z), "Invalid reaction position.");
                    var copy = new AlarmPerson { ResidentId = person.ResidentId, DirectWitness = person.DirectWitness,
                        HeardViolence = person.HeardViolence, Awakened = person.Awakened, InnAlarm = person.InnAlarm,
                        CorpseObserver = person.CorpseObserver, CorpseSeenAt = person.CorpseSeenAt,
                        Reaction = person.Reaction, HasPosition = person.HasPosition, ReachedRefuge = person.ReachedRefuge,
                        RefugeSealed=person.RefugeSealed, RefugeUntil=person.RefugeUntil, OutdoorReport=OutdoorBodyReport.Copy(person.OutdoorReport),
                        StaticWitness=person.StaticWitness, StatementDelivered=person.StatementDelivered, InterviewGuardId=person.InterviewGuardId,
                        BuildingKey = person.BuildingKey, X = person.X, Y = person.Y, Z = person.Z,
                        Escape = CivilianEscape.Copy(person.Escape), EscapeAdvancedAt=person.EscapeAdvancedAt };
                    Require(ValidTime(copy.RefugeUntil) && (copy.RefugeUntil==0 || copy.RefugeSealed && copy.RefugeUntil>=alarm.StartedAt+3600), "Invalid refuge deadline.");
                    Require(!copy.StatementDelivered || copy.DirectWitness,"Statement lacks direct witness evidence.");
                    Require(!copy.StaticWitness || source.NativePeople.Exists(p=>p.Id==copy.ResidentId),"Static witness lacks native identity.");
                    Require(copy.InterviewGuardId==null || copy.StaticWitness && source.Garrisons.Exists(g=>g.SettlementId==alarm.SettlementId && g.Guards.Exists(p=>p.Id==copy.InterviewGuardId)),"Unknown interviewing guard.");
                    Require(copy.OutdoorReport==null || copied.Corpse!=null && alarm.BuildingKey==-1 && copy.OutdoorReport.SeenAt>=copied.Corpse.DiedAt,"Invalid outdoor report owner.");
                    Require(copy.OutdoorReport==null || copy.OutdoorReport.DeliveredAt<0 || source.Version>=77 &&
                        copy.DirectWitness && copy.StatementDelivered && source.Garrisons.Exists(g=>g.SettlementId==alarm.SettlementId &&
                            g.Guards.Exists(p=>p.Id==copy.OutdoorReport.GuardId &&
                                (p.IsAlive || p.DiedAt>=copy.OutdoorReport.DeliveredAt))),"Invalid physical murder statement.");
                    Require(ValidTime(copy.EscapeAdvancedAt) && (copy.EscapeAdvancedAt==0 ||
                        copy.Escape!=null && copy.EscapeAdvancedAt>=copy.Escape.StartedAt &&
                        copy.EscapeAdvancedAt>=copy.Escape.RedirectedAt), "Invalid witness simulation time.");
                    Require(!person.InnAlarm || (!threat && alarm.BuildingKey >= 0 && person.Reaction == CivilianReaction.Flee), "Invalid inn alarm recipient.");
                    Require(!person.CorpseObserver || (!threat && copied.Corpse != null && alarm.BuildingKey >= 0 &&
                        ValidTime(person.CorpseSeenAt) && person.CorpseSeenAt >= copied.Corpse.DiedAt &&
                        person.Reaction == CivilianReaction.Flee), "Invalid corpse observer.");
                    Require(copy.Escape == null || ((person.DirectWitness || person.HeardViolence || person.InnAlarm || person.CorpseObserver) && person.HasPosition && person.Reaction == CivilianReaction.Flee &&
                        copy.Escape.BuildingKey == alarm.BuildingKey &&
                        copy.Escape.StartedAt >= alarm.StartedAt && (copy.Escape.StartedAt < alarm.ExpiresAt ||
                        person.CorpseObserver && copy.Escape.StartedAt >= person.CorpseSeenAt &&
                        copy.Escape.StartedAt < person.CorpseSeenAt + 900)), "Invalid witness escape ownership.");
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
