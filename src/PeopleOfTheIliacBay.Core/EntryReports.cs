using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class EntryReport
    {
        public string Id;
        public int SettlementId, BuildingKey;
        public double EnteredAt;
        public List<string> CitizenWitnesses = new List<string>();
        public List<string> InformedGuards = new List<string>();
        public EntryMessenger Messenger;
    }
    public sealed partial class WorldStateStore
    {
        public List<EntryReport> EntriesFor(int settlement)
        { return state.EntryReports.FindAll(r => r.SettlementId == settlement); }
        public EntryReport RecordEntry(int settlement, int building, double now, IEnumerable<string> citizens, IEnumerable<string> guards)
        {
            Require(citizens != null && guards != null && ValidTime(now), "Invalid entry evidence.");
            // Game-second resolution keeps identity stable through native JSON rounding.
            now = Math.Floor(now);
            var report = new EntryReport { SettlementId = settlement, BuildingKey = building, EnteredAt = now,
                Id = "entry:" + settlement + ":" + building + ":" + now.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                CitizenWitnesses = new List<string>(citizens), InformedGuards = new List<string>(guards) };
            ValidateEntry(state, report, true);
            if (report.CitizenWitnesses.Count == 0 && report.InformedGuards.Count == 0) return null;
            var existing = state.EntryReports.Find(r => r.Id == report.Id);
            if (existing != null) return existing;
            state.EntryReports.Add(report);
            foreach(string witness in report.CitizenWitnesses)
                RememberSceneEvent(witness,report.Id,NpcSceneEventKind.Burglary,now,settlement,building,
                    "I witnessed the player entering this building without permission.");
            return report;
        }
        public void TellGuardAboutEntry(string reportId, string citizenId, string guardId, double now)
        {
            var report = state.EntryReports.Find(r => r.Id == reportId);
            Require(report != null && ValidTime(now) && now >= report.EnteredAt && report.CitizenWitnesses.Contains(citizenId),
                "No valid witnessed entry to report.");
            Require(AlarmResident(report.SettlementId, citizenId).IsAlive, "Dead witness cannot report.");
            var roster = state.Garrisons.Find(g => g.SettlementId == report.SettlementId);
            Require(roster != null && roster.Guards.Exists(g => g.Id == guardId && g.IsAlive), "Unknown or dead receiving guard.");
            if (!report.InformedGuards.Contains(guardId)) report.InformedGuards.Add(guardId);
        }
        private static void ValidateEntry(WorldState world, EntryReport report, bool living)
        {
            Require(report != null && ValidTime(report.EnteredAt) && report.EnteredAt == Math.Floor(report.EnteredAt) &&
                report.Id == "entry:" + report.SettlementId + ":" + report.BuildingKey + ":" + report.EnteredAt.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                "Invalid entry identity or time.");
            var town = world.Settlements.Find(t => t.SettlementId == report.SettlementId);
            Require(town != null && town.Households.Exists(h => h.BuildingKey == report.BuildingKey), "Entry destination is not a known household.");
            Require(report.CitizenWitnesses != null && report.InformedGuards != null, "Missing entry witnesses.");
            var ids = new HashSet<string>();
            foreach (string id in report.CitizenWitnesses)
                Require(id != null && ids.Add(id) && (town.Households.Exists(h => h.Residents.Exists(r => r.Id == id && (!living || r.IsAlive))) ||
                    ValidVisitingEntryWitness(world,report,id,living)),
                    "Unknown, duplicate or dead entry witness.");
            ids.Clear();
            var roster = world.Garrisons.Find(g => g.SettlementId == report.SettlementId);
            foreach (string id in report.InformedGuards)
                Require(id != null && ids.Add(id) && roster != null && roster.Guards.Exists(g => g.Id == id && (!living || g.IsAlive)),
                    "Unknown, duplicate or dead informed guard.");
            var messenger = EntryMessenger.Copy(report.Messenger);
            if (messenger != null)
                Require(messenger.StartedAt >= report.EnteredAt && report.CitizenWitnesses.Contains(messenger.CitizenId) &&
                    // A saved journey records the post at planning time. Guards
                    // can relocate later; delivery checks the current post.
                    roster != null && roster.Guards.Exists(g => g.Id == messenger.GuardId) &&
                    (!messenger.Delivered || report.InformedGuards.Contains(messenger.GuardId)), "Invalid entry messenger references.");
        }
        private static bool ValidVisitingEntryWitness(WorldState world,EntryReport report,string id,bool living)
        {
            foreach(var origin in world.Settlements)
            {
                var resident=TripResident(world,origin.SettlementId,id);
                if(resident==null) continue;
                if(!living) return true; // Historical evidence outlives the latest trip.
                if(!resident.IsAlive || world.ResidentTrips==null) return false;
                var trip=world.ResidentTrips.Find(t=>t.ResidentId==id && t.VisitsSettlement(report.SettlementId));
                if(trip==null) return false;
                var presence=trip.Evaluate(report.EnteredAt);
                return presence.Phase==ResidentTripPhase.Visiting && presence.SettlementId==report.SettlementId;
            }
            return false;
        }
        private static List<EntryReport> CopyEntries(WorldState world)
        {
            var result = new List<EntryReport>();
            // Additive field: old saves have no entry evidence.
            if (world.EntryReports == null) return result;
            var ids = new HashSet<string>();
            var activeMessengers = new HashSet<string>();
            foreach (var report in world.EntryReports)
            {
                ValidateEntry(world, report, false);
                if (report.Messenger != null && !report.Messenger.Delivered && !report.Messenger.Cancelled)
                    Require(activeMessengers.Add(report.Messenger.CitizenId), "Citizen carries simultaneous entry reports.");
                Require(ids.Add(report.Id) && report.CitizenWitnesses.Count + report.InformedGuards.Count > 0,
                    "Duplicate or unwitnessed entry report.");
                result.Add(new EntryReport { Id = report.Id, SettlementId = report.SettlementId, BuildingKey = report.BuildingKey,
                    EnteredAt = report.EnteredAt, CitizenWitnesses = new List<string>(report.CitizenWitnesses),
                    InformedGuards = new List<string>(report.InformedGuards), Messenger = EntryMessenger.Copy(report.Messenger) });
            }
            return result;
        }
    }
}
