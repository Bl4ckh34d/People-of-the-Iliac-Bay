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
    public sealed class CorpsePropertyKeyClaim
    {
        public int SettlementId;
        public int BuildingKey;
        public bool Taken;
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
        public string InspectionGuardId;
        public double InspectionRemainingRealSeconds = 5;
        public OutdoorCorpseDiscovery Discovery;
        public bool PropertyKeyClaimsInitialized;
        public List<CorpsePropertyKeyClaim> PropertyKeyClaims;
    }
    public sealed partial class WorldStateStore
    {
        public bool InitializeCorpseHomeKey(string incidentId, int? confirmedKeeperWorkplace = null,
            Func<int,int,bool> allowPropertyKey = null)
        {
            var alarm = FindIncident(incidentId);
            Require(alarm.Corpse != null && !alarm.Corpse.Cleared, "Cannot issue keys from a missing or cleared corpse.");
            var native = state.NativePeople.Find(person => person.Id == alarm.VictimId);
            Require(!confirmedKeeperWorkplace.HasValue || native != null && confirmedKeeperWorkplace.Value >= 0 &&
                native.WorkKey == confirmedKeeperWorkplace.Value, "Confirmed keeper workplace does not match the victim.");
            if (alarm.Corpse.PropertyKeyClaimsInitialized) return false;
            var claims = new List<CorpsePropertyKeyClaim>();
            var lodging=InnLodgerHome(alarm.VictimId);
            if(lodging!=null) claims.Add(new CorpsePropertyKeyClaim {SettlementId=lodging.HomeSettlement,BuildingKey=lodging.HomeKey});
            // A visitor carries the key to the original home, never the building
            // where the corpse happens to be found. Retired generations retain it.
            foreach (var town in state.Settlements)
                foreach (var home in town.Households)
                    if (home.Residents.Exists(person => person.Id == alarm.VictimId))
                        claims.Add(new CorpsePropertyKeyClaim { SettlementId = town.SettlementId, BuildingKey = home.BuildingKey });
            foreach (var retired in state.HouseholdHistory)
                if (retired.Household.Residents.Exists(person => person.Id == alarm.VictimId))
                    claims.Add(new CorpsePropertyKeyClaim { SettlementId = retired.SettlementId, BuildingKey = retired.Household.BuildingKey });
            if (native != null && native.HomeKey >= 0 && native.BedSlot>=0 && native.BedSlot<native.BedCapacity && !claims.Exists(key =>
                key.SettlementId == native.SettlementId && key.BuildingKey == native.HomeKey))
                claims.Add(new CorpsePropertyKeyClaim { SettlementId = native.SettlementId, BuildingKey = native.HomeKey });
            if (confirmedKeeperWorkplace.HasValue && !claims.Exists(key =>
                key.SettlementId == native.SettlementId && key.BuildingKey == native.WorkKey))
                claims.Add(new CorpsePropertyKeyClaim { SettlementId = native.SettlementId, BuildingKey = native.WorkKey });
            if(allowPropertyKey!=null) claims.RemoveAll(key=>!allowPropertyKey(key.SettlementId,key.BuildingKey));
            return InitializeCorpsePropertyKeyClaims(incidentId, claims);
        }
        public bool InitializeCorpsePropertyKeyClaims(string incidentId, IEnumerable<CorpsePropertyKeyClaim> claims)
        {
            Require(claims != null, "Missing corpse property key claims.");
            var alarm = FindIncident(incidentId);
            var body = alarm.Corpse;
            Require(body != null && !body.Cleared, "Cannot issue property keys from a missing or cleared corpse.");
            var supplied = new List<CorpsePropertyKeyClaim>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var claim in claims)
            {
                Require(claim != null && claim.SettlementId >= 0 && claim.BuildingKey >= 0 && !claim.Taken &&
                    identities.Add(PropertyKeyIdentity(claim.SettlementId, claim.BuildingKey)),
                    "Invalid or duplicate corpse property key claim.");
                supplied.Add(new CorpsePropertyKeyClaim { SettlementId = claim.SettlementId,
                    BuildingKey = claim.BuildingKey, Taken = false });
            }
            if (body.PropertyKeyClaimsInitialized)
            {
                Require(body.PropertyKeyClaims != null && body.PropertyKeyClaims.Count == supplied.Count,
                    "Corpse property key claims are already initialized.");
                foreach (var claim in supplied)
                    Require(body.PropertyKeyClaims.Exists(existing => existing.SettlementId == claim.SettlementId &&
                        existing.BuildingKey == claim.BuildingKey), "Corpse property key claims are already initialized.");
                return false;
            }
            Require(body.PropertyKeyClaims == null || body.PropertyKeyClaims.Count == 0,
                "Uninitialized corpse has property key claims.");
            body.PropertyKeyClaims = supplied;
            body.PropertyKeyClaimsInitialized = true;
            return true;
        }
        public List<CorpsePropertyKeyClaim> GetAvailableCorpsePropertyKeyClaims(string incidentId,
            Func<string,int,int,bool> allowPropertyKey = null)
        {
            var alarm=FindIncident(incidentId);
            var body = alarm.Corpse;
            var available = new List<CorpsePropertyKeyClaim>();
            if (body == null || body.Cleared || !body.PropertyKeyClaimsInitialized) return available;
            Require(body.PropertyKeyClaims != null, "Missing initialized corpse property key claims.");
            foreach (var claim in body.PropertyKeyClaims)
                if (!claim.Taken && (allowPropertyKey==null || allowPropertyKey(alarm.VictimId,claim.SettlementId,claim.BuildingKey)))
                    available.Add(new CorpsePropertyKeyClaim { SettlementId = claim.SettlementId,
                        BuildingKey = claim.BuildingKey, Taken = false });
            return available;
        }
        public bool MarkCorpsePropertyKeyTaken(string incidentId, int settlementId, int buildingKey)
        {
            Require(settlementId >= 0 && buildingKey >= 0, "Invalid corpse property key identity.");
            var body = FindIncident(incidentId).Corpse;
            if (body == null || body.Cleared) return false;
            Require(body.PropertyKeyClaimsInitialized && body.PropertyKeyClaims != null,
                "Corpse property key claims are not initialized.");
            var claim = body.PropertyKeyClaims.Find(item => item.SettlementId == settlementId && item.BuildingKey == buildingKey);
            Require(claim != null, "Unknown corpse property key claim.");
            if (claim.Taken) return false;
            claim.Taken = true;
            return true;
        }
        private static string PropertyKeyIdentity(int settlementId, int buildingKey)
        {
            return settlementId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                buildingKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        private static GuardReport InspectionReport(CivilianIncident alarm, WorldState world, string guardId=null)
        {
            var roster=world.Garrisons==null ? null : world.Garrisons.Find(g=>g!=null && g.SettlementId==alarm.SettlementId);
            return roster==null || roster.Reports==null ? null : roster.Reports.Find(r=>r!=null &&
                (r.Id=="discovery:"+alarm.Id || r.Id=="inn-alarm:"+alarm.Id || r.Id=="civilian:"+alarm.Id) && r.OccupiesScene &&
                r.BuildingKey==alarm.BuildingKey && r.HasArrival && (guardId==null ||
                r.Arrivals.Exists(a=>a!=null && a.Arrived && a.GuardId==guardId)));
        }
        public bool AssignCorpseInspector(string incidentId,string reportId,double now)
        {
            var alarm=FindIncident(incidentId);
            Require(ValidTime(now) && (reportId=="discovery:"+alarm.Id || reportId=="inn-alarm:"+alarm.Id || reportId=="civilian:"+alarm.Id),"Invalid inspection report.");
            var body=alarm.Corpse;
            if(body==null || body.Cleared) return false;
            var roster=state.Garrisons.Find(g=>g.SettlementId==alarm.SettlementId);
            if(roster==null) return false;
            roster.Advance(now);
            var report=roster.Reports.Find(r=>r.Id==reportId && r.OccupiesScene && r.HasArrival && r.BuildingKey==alarm.BuildingKey);
            if(report==null || now<report.FirstArrivalAt || now>=report.InvestigationUntil) return false;
            var current=roster.Guards.Find(g=>g.Id==body.InspectionGuardId);
            if(current!=null && current.IncidentId==reportId && current.IsAlive && current.Interior!=null && !current.Interior.Returning &&
                current.Interior.BuildingKey==alarm.BuildingKey) return false;
            string selected=null; double earliest=double.MaxValue;
            foreach(var arrival in report.Arrivals)
            {
                var guard=roster.Guards.Find(g=>g.Id==arrival.GuardId);
                if(!arrival.Arrived || guard==null || guard.IncidentId!=reportId || !guard.IsAlive || guard.Interior==null || guard.Interior.Returning ||
                    guard.Interior.BuildingKey!=alarm.BuildingKey) continue;
                if(arrival.ScheduledAt<earliest || arrival.ScheduledAt==earliest && string.CompareOrdinal(guard.Id,selected)<0)
                { selected=guard.Id; earliest=arrival.ScheduledAt; }
            }
            if(selected==null) return false;
            body.InspectionGuardId=selected;
            // A replacement must perform its own five-second inspection. A missing
            // replacement leaves prior evidence intact and never clears the body.
            body.InspectionRemainingRealSeconds=5;
            return true;
        }
        public AlarmPerson NoticeInteriorCorpse(string incidentId, string observerId, int buildingKey, double now)
        {
            var alarm=FindIncident(incidentId); var body=alarm.Corpse;
            Require(body!=null && alarm.ThreatId==null && buildingKey>=0 && alarm.BuildingKey==buildingKey &&
                (body.HasPosition ? body.BuildingKey : alarm.BuildingKey)==buildingKey &&
                ValidTime(now) && now>=body.DiedAt,"Invalid interior corpse discovery.");
            var resident=AlarmResident(alarm.SettlementId,observerId);
            if(body.Cleared || !resident.IsAlive || observerId==alarm.VictimId) return null;
            var person=alarm.People.Find(p=>p.ResidentId==observerId);
            // Repeated rendering must not restart flight or extend the report deadline.
            if(person!=null && person.CorpseObserver && CivilianAlarms.PersonActive(alarm,person,now)) return person;
            if(person==null) { person=new AlarmPerson { ResidentId=observerId }; alarm.People.Add(person); }
            else if(!CivilianAlarms.PersonActive(alarm,person,now))
            { person.Escape=null; person.EscapeAdvancedAt=0; person.HasPosition=false; person.ReachedRefuge=false; }
            person.CorpseObserver=true; person.CorpseSeenAt=now; person.Reaction=CivilianReaction.Flee;
            var native=state.NativePeople.Find(p=>p.Id==observerId);
            if(native!=null) native.ResponseIncidentId=alarm.Id;
            return person;
        }
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
            var alarm=FindIncident(incidentId);
            var body = RequireOutdoorBody(alarm);
            Require(ValidTime(now), "Invalid outdoor observation time.");
            if (!body.Cleared && body.Discovery != null && !playerOnSite && now >= body.Discovery.SeenAt)
            { body.Cleared = true; body.ClearedAt = now; }
            ReleaseCleanedLodging(alarm);
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
                if(alarm.SettlementId==settlementId && corpse!=null && corpse.Cleared && now>=corpse.ClearedAt)
                    ReleaseCleanedLodging(alarm);
                if (alarm.SettlementId != settlementId || corpse == null || corpse.Cleared || now < corpse.DiedAt) continue;
                int building = corpse.HasPosition ? corpse.BuildingKey : alarm.BuildingKey;
                bool unattended = currentBuilding.HasValue && (building < 0 || currentBuilding.Value != building);
                if (building >= 0 && alarm.Scene == null && unattended && now >= corpse.DiedAt + 86400)
                {
                    corpse.Cleared = true; corpse.ClearedAt = now;
                    ReleaseCleanedLodging(alarm);
                }
                else visible.Add(alarm);
            }
            return visible;
        }
        public double AdvanceCrimeSceneInspection(string incidentId, string guardId, double realSeconds,
            bool paused, bool inCombat, bool atCorpse, double now)
        {
            Require(!string.IsNullOrWhiteSpace(guardId) && guardId.Length <= 128 && !double.IsNaN(realSeconds) && !double.IsInfinity(realSeconds) &&
                realSeconds >= 0 && realSeconds <= 60 && ValidTime(now), "Invalid crime scene inspection progress.");
            var alarm = FindIncident(incidentId);
            var body = alarm.Corpse;
            var inspection=InspectionReport(alarm,state,guardId);
            Require(inspection != null && body != null && body.InspectionGuardId == guardId,
                "Guard is not assigned to inspect this corpse.");
            Require(now >= inspection.FirstArrivalAt, "Inspection predates guard arrival.");
            if (body.Cleared) return 0;
            var roster = state.Garrisons.Find(g => g.SettlementId == alarm.SettlementId);
            var guard = roster == null ? null : roster.Guards.Find(g => g.Id == guardId);
            Require(guard != null && guard.IsAlive && guard.Interior != null && !guard.Interior.Returning &&
                guard.Interior.BuildingKey == alarm.BuildingKey, "Assigned guard is not inside the crime scene.");
            if (paused || inCombat || !atCorpse || realSeconds == 0) return body.InspectionRemainingRealSeconds;
            body.InspectionRemainingRealSeconds = Math.Max(0, body.InspectionRemainingRealSeconds - realSeconds);
            if (body.InspectionRemainingRealSeconds <= 0)
            {
                body.Cleared = true;
                body.ClearedAt = now;
                ReleaseCleanedLodging(alarm);
            }
            return body.InspectionRemainingRealSeconds;
        }
        public CorpseState GetCrimeSceneCorpseSnapshot(string incidentId)
        {
            var body = FindIncident(incidentId).Corpse;
            if (body == null) return null;
            OutdoorCorpseDiscovery discovery = body.Discovery == null ? null : new OutdoorCorpseDiscovery {
                ObserverId = body.Discovery.ObserverId, Guard = body.Discovery.Guard,
                PlayerPresent = body.Discovery.PlayerPresent, SeenAt = body.Discovery.SeenAt };
            return new CorpseState { DiedAt = body.DiedAt, ClearedAt = body.ClearedAt, Cleared = body.Cleared,
                HasPosition = body.HasPosition, BuildingKey = body.BuildingKey, X = body.X, Y = body.Y, Z = body.Z,
                CleanupVersion = body.CleanupVersion, InspectionGuardId = body.InspectionGuardId,
                InspectionRemainingRealSeconds = body.InspectionRemainingRealSeconds, Discovery = discovery,
                PropertyKeyClaimsInitialized = body.PropertyKeyClaimsInitialized,
                PropertyKeyClaims = CopyPropertyKeyClaims(body.PropertyKeyClaims) };
        }
        private static CorpseState CopyCorpse(CivilianIncident alarm, Resident victim, WorldState source)
        {
            var corpse = alarm.Corpse;
            if (corpse == null) return null;
            Require(corpse.CleanupVersion == 0 || corpse.CleanupVersion == 1, "Unknown corpse cleanup version.");
            Require(!victim.IsAlive && alarm.HasLocation && ValidTime(corpse.DiedAt) && corpse.DiedAt >= alarm.StartedAt &&
                ValidTime(corpse.ClearedAt) && (corpse.Cleared ? corpse.ClearedAt >= corpse.DiedAt : corpse.ClearedAt == 0) &&
                !double.IsNaN(corpse.InspectionRemainingRealSeconds) && !double.IsInfinity(corpse.InspectionRemainingRealSeconds) &&
                corpse.InspectionRemainingRealSeconds >= 0 &&
                corpse.InspectionRemainingRealSeconds <= 5 &&
                (corpse.InspectionGuardId == null ? corpse.InspectionRemainingRealSeconds == 5 || corpse.InspectionRemainingRealSeconds == 0 :
                    !string.IsNullOrWhiteSpace(corpse.InspectionGuardId) && corpse.InspectionGuardId.Length <= 128),
                "Invalid corpse chronology or ownership.");
            var inspection=corpse.InspectionGuardId==null ? null : InspectionReport(alarm,source,corpse.InspectionGuardId);
            Require(!corpse.Cleared || (inspection!=null ? corpse.InspectionRemainingRealSeconds==0 && corpse.ClearedAt>=inspection.FirstArrivalAt :
                alarm.Scene != null ? alarm.Scene.Discovered &&
                    (corpse.InspectionGuardId == null ? corpse.ClearedAt == alarm.Scene.DiscoveredAt :
                     corpse.InspectionRemainingRealSeconds == 0 && corpse.ClearedAt >= alarm.Scene.DiscoveredAt) :
                corpse.Discovery != null ? corpse.ClearedAt >= corpse.Discovery.SeenAt :
                ((corpse.HasPosition ? corpse.BuildingKey : alarm.BuildingKey) >= 0 || corpse.CleanupVersion == 0) &&
                corpse.ClearedAt >= corpse.DiedAt + 86400), "Corpse cleanup lacks discovery or elapsed day.");
            if (corpse.InspectionGuardId != null)
            {
                Require(inspection != null,
                    "Corpse inspection has no assigned arrival.");
            }
            Require(!corpse.HasPosition || (corpse.BuildingKey >= -1 && ValidCoordinate(corpse.X) &&
                ValidCoordinate(corpse.Y) && ValidCoordinate(corpse.Z)), "Invalid saved corpse position.");
            var keyClaims = CopyPropertyKeyClaims(corpse.PropertyKeyClaims);
            if (corpse.PropertyKeyClaimsInitialized)
            {
                Require(corpse.PropertyKeyClaims != null, "Missing initialized corpse property key claims.");
                var claimIdentities = new HashSet<string>(StringComparer.Ordinal);
                foreach (var claim in keyClaims)
                    Require(claim.SettlementId >= 0 && claim.BuildingKey >= 0 &&
                        claimIdentities.Add(PropertyKeyIdentity(claim.SettlementId, claim.BuildingKey)),
                        "Invalid or duplicate corpse property key claim.");
            }
            else Require(keyClaims == null || keyClaims.Count == 0, "Uninitialized corpse has property key claims.");
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
                    var visitor=source.Version>=9 && source.Travelers!=null ? source.Travelers.Find(p=>p!=null && p.Id==seen.ObserverId) : null;
                    bool traveling=visitor!=null && (visitor.IsAlive || visitor.DiedAt>=seen.SeenAt) &&
                        source.TravelerVisits!=null && source.TravelerVisits.Exists(v=>v!=null && v.PersonId==visitor.Id && v.SettlementId==alarm.SettlementId);
                    bool native=source.NativePeople!=null && source.NativePeople.Exists(p=>p!=null && p.Id==seen.ObserverId && p.SettlementId==alarm.SettlementId);
                    Require(town != null && (town.Households.Exists(h => h.Residents.Exists(r => r.Id == seen.ObserverId)) || native || traveling),
                        "Unknown corpse resident observer.");
                }
                discovery = new OutdoorCorpseDiscovery { ObserverId = seen.ObserverId, Guard = seen.Guard, SeenAt = seen.SeenAt,
                    PlayerPresent = seen.PlayerPresent };
            }
            return new CorpseState { DiedAt = corpse.DiedAt, ClearedAt = corpse.ClearedAt, Cleared = corpse.Cleared,
                HasPosition = corpse.HasPosition, BuildingKey = corpse.BuildingKey, X = corpse.X, Y = corpse.Y, Z = corpse.Z,
                CleanupVersion = corpse.Cleared ? corpse.CleanupVersion : 1, InspectionGuardId = corpse.InspectionGuardId,
                InspectionRemainingRealSeconds = corpse.InspectionGuardId == null ? 5 : corpse.InspectionRemainingRealSeconds,
                Discovery = discovery, PropertyKeyClaimsInitialized = corpse.PropertyKeyClaimsInitialized,
                PropertyKeyClaims = corpse.PropertyKeyClaimsInitialized ? keyClaims : null };
        }
        private static List<CorpsePropertyKeyClaim> CopyPropertyKeyClaims(List<CorpsePropertyKeyClaim> claims)
        {
            if (claims == null) return null;
            var copy = new List<CorpsePropertyKeyClaim>();
            foreach (var claim in claims)
            {
                Require(claim != null, "Null corpse property key claim.");
                copy.Add(new CorpsePropertyKeyClaim { SettlementId = claim.SettlementId,
                    BuildingKey = claim.BuildingKey, Taken = claim.Taken });
            }
            return copy;
        }
    }
}
