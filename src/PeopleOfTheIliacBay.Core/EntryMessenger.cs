using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class EntryMessenger
    {
        public string CitizenId, GuardId;
        public double StartedAt;
        public double TimeScale = 12;
        public List<SavedRoutePoint> Route;
        public bool Delivered, Cancelled;
        // Match civilian flight: 2.8 metres per real second.
        public double ArrivesAt
        {
            get
            {
                double length = 0;
                for (int i = 1; i < Route.Count; i++) length += Distance(Route[i - 1], Route[i]);
                return StartedAt + length / 2.8 * EffectiveTimeScale;
            }
        }
        public SavedRoutePoint Position(double now)
        {
            if (!Finite(now) || now < 0) throw new ArgumentException("Invalid messenger time.");
            double remaining = Math.Max(0, now - StartedAt) / EffectiveTimeScale * 2.8;
            for (int i = 1; i < Route.Count; i++)
            {
                var a = Route[i - 1]; var b = Route[i]; double length = Distance(a, b);
                if (length > 0 && remaining <= length)
                {
                    double t = remaining / length;
                    return new SavedRoutePoint { X = a.X + (b.X - a.X) * t, Z = a.Z + (b.Z - a.Z) * t };
                }
                remaining -= length;
            }
            var end = Route[Route.Count - 1]; return new SavedRoutePoint { X = end.X, Z = end.Z };
        }
        internal static double Distance(SavedRoutePoint a, SavedRoutePoint b)
        { double x = b.X - a.X, z = b.Z - a.Z; return Math.Sqrt(x * x + z * z); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private double EffectiveTimeScale { get { return TimeScale == 0 ? 12 : TimeScale; } }
        public void MigrateClockScale(double now, double previousRate, double currentRate)
        {
            if (EffectiveTimeScale == currentRate) return;
            double oldRate = previousRate;
            var interval = ClockRateMigration.RescaleInterval(StartedAt, ArrivesAt, now, oldRate, currentRate);
            StartedAt = interval.Start;
            TimeScale = currentRate;
        }
        internal static EntryMessenger Copy(EntryMessenger source)
        {
            if (source == null) return null;
            if (string.IsNullOrWhiteSpace(source.CitizenId) || string.IsNullOrWhiteSpace(source.GuardId) ||
                !Finite(source.StartedAt) || source.StartedAt < 0 || !Finite(source.TimeScale) || source.TimeScale < 0 || source.Route == null ||
                source.Route.Count < 2 || source.Route.Count > 10000 || source.Delivered && source.Cancelled)
                throw new ArgumentException("Invalid entry messenger.");
            var result = new EntryMessenger { CitizenId = source.CitizenId, GuardId = source.GuardId,
                StartedAt = source.StartedAt, TimeScale = source.TimeScale, Delivered = source.Delivered, Cancelled = source.Cancelled,
                Route = new List<SavedRoutePoint>() };
            foreach (var point in source.Route)
            {
                if (point == null || !Finite(point.X) || !Finite(point.Z) || Math.Abs(point.X) > 1000000 || Math.Abs(point.Z) > 1000000)
                    throw new ArgumentException("Invalid entry messenger route.");
                result.Route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
            }
            return result;
        }
    }
    public sealed partial class WorldStateStore
    {
        public void PlanEntryMessenger(string reportId, string citizenId, string guardId, double now, List<SavedRoutePoint> route, double timeScale = 2)
        {
            var report = state.EntryReports.Find(r => r.Id == reportId);
            Require(report != null && report.CitizenWitnesses.Contains(citizenId) && ValidTime(now) && now >= report.EnteredAt,
                "No witnessed entry for messenger.");
            Require(report.InformedGuards.Count == 0 && (report.Messenger == null || report.Messenger.Cancelled), "Entry already has a report or messenger.");
            Require(AlarmResident(report.SettlementId, citizenId).IsAlive, "Dead citizen cannot report entry.");
            var roster = state.Garrisons.Find(g => g.SettlementId == report.SettlementId);
            var guard = roster == null ? null : roster.Guards.Find(g => g.Id == guardId);
            Require(guard != null && guard.IsAlive && guard.Duty == GuardDuty.Posted && guard.Interior == null, "Guard is not at a reporting post.");
            Require(!state.EntryReports.Exists(r => r.Messenger != null && !r.Messenger.Delivered && !r.Messenger.Cancelled &&
                r.Messenger.CitizenId == citizenId), "Citizen is already carrying another report.");
            Require(Finite(timeScale) && timeScale > 0, "Invalid messenger time scale.");
            var messenger = EntryMessenger.Copy(new EntryMessenger { CitizenId = citizenId, GuardId = guardId, StartedAt = now, TimeScale = timeScale, Route = route });
            Require(EntryMessenger.Distance(messenger.Route[messenger.Route.Count - 1], guard.Post) <= 0.05, "Messenger route must reach guard post.");
            report.Messenger = messenger;
        }
        public List<EntryReport> AdvanceEntryMessengers(int settlement, double now)
        {
            Require(ValidTime(now), "Invalid entry report time.");
            var delivered = new List<EntryReport>();
            foreach (var report in state.EntryReports)
            {
                var messenger = report.Messenger;
                if (report.SettlementId != settlement || messenger == null || messenger.Delivered || messenger.Cancelled || now < messenger.StartedAt) continue;
                var roster = state.Garrisons.Find(g => g.SettlementId == settlement);
                var guard = roster == null ? null : roster.Guards.Find(g => g.Id == messenger.GuardId);
                if (!AlarmResident(settlement, messenger.CitizenId).IsAlive || guard == null || !guard.IsAlive ||
                    guard.Duty != GuardDuty.Posted || guard.Interior != null)
                { messenger.Cancelled = true; continue; }
                if (now < messenger.ArrivesAt) continue;
                if (EntryMessenger.Distance(messenger.Route[messenger.Route.Count - 1], guard.Post) > 0.05)
                { messenger.Cancelled = true; continue; }
                TellGuardAboutEntry(report.Id, messenger.CitizenId, messenger.GuardId, now);
                messenger.Delivered = true; delivered.Add(report);
            }
            return delivered;
        }
    }
}
