using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class PendingEntry
        {
            public WorldStateStore Store;
            public int Settlement, Building;
            public double Time;
            public List<string> Citizens = new List<string>(), Guards = new List<string>();
            public EntryMessenger Messenger;
        }
        private PendingEntry pendingEntry;
        private readonly Dictionary<string, EntryMessenger> entryJourneys = new Dictionary<string, EntryMessenger>();
        private void InstallEntryAwareness()
        {
            PlayerEnterExit.OnPreTransition += CaptureEntry;
            PlayerEnterExit.OnPreTransition += CaptureObservedTransition;
            PlayerEnterExit.OnTransitionInterior += CommitEntry;
            PlayerEnterExit.OnFailedTransition += CancelEntry;
        }
        private void RemoveEntryAwareness()
        {
            PlayerEnterExit.OnPreTransition -= CaptureEntry;
            PlayerEnterExit.OnPreTransition -= CaptureObservedTransition;
            PlayerEnterExit.OnTransitionInterior -= CommitEntry;
            PlayerEnterExit.OnFailedTransition -= CancelEntry;
            pendingEntry = null;
        }
        private void CancelEntry(PlayerEnterExit.TransitionEventArgs args) { pendingEntry = null; }
        private void CaptureEntry(PlayerEnterExit.TransitionEventArgs args)
        {
            pendingEntry = null;
            var game = GameManager.Instance;
            if (!Ready || !ReactionsEnabled || !HouseholdLocksEnabled || worldStore == null || !directory ||
                game == null || !game.IsReady || game.SaveLoadManager.LoadInProgress || game.PlayerEnterExit.IsPlayerInside ||
                args.TransitionType != PlayerEnterExit.TransitionType.ToBuildingInterior || game.PlayerEntity.IsInvisible) return;
            BuildingSummary summary;
            if (!directory.GetBuildingSummary(args.StaticDoor.buildingKey, out summary) || HouseholdDoorAccess(summary) != false) return;
            var pending = new PendingEntry { Store = worldStore, Settlement = population.SettlementId,
                Building = summary.buildingKey, Time = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() };
            var target = game.PlayerMotor.transform.position;
            foreach (var actor in actors.Values)
                if (actor && actor.Resident != null && actor.Resident.IsAlive && actor.gameObject.activeInHierarchy &&
                    (Reaction(actor.Resident.Id, pending.Time) != null || GetState(actor.Resident.Id, pending.Time).Activity != Activity.Sleep) &&
                    TheftWitnessSight.CanSee(actor.transform, target, true, true)) pending.Citizens.Add(actor.Resident.Id);
            var guards = GetComponent<GuardPopulation>();
            if (guards) pending.Guards = guards.EntryObservers(target);
            if (guards && pending.Guards.Count == 0)
            {
                pending.Citizens.Sort(System.StringComparer.Ordinal);
                foreach (string id in pending.Citizens)
                {
                    if (entryJourneys.ContainsKey(id) || Reaction(id, pending.Time) != null) continue;
                    var actor = FindActor(id);
                    var plan = guards.EntryMessengerRoute(id, actor.transform.position, pending.Time);
                    if (plan != null && (pending.Messenger == null || plan.ArrivesAt < pending.Messenger.ArrivesAt)) pending.Messenger = plan;
                }
            }
            if (pending.Citizens.Count + pending.Guards.Count > 0) pendingEntry = pending;
        }
        private void CommitEntry(PlayerEnterExit.TransitionEventArgs args)
        {
            var pending = pendingEntry; pendingEntry = null;
            var game = GameManager.Instance;
            if (pending == null || game.SaveLoadManager.LoadInProgress || args.StaticDoor.buildingKey != pending.Building ||
                !game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId != pending.Settlement) return;
            var report = pending.Store.RecordEntry(pending.Settlement, pending.Building, pending.Time, pending.Citizens, pending.Guards);
            if (report == null) return;
            if (pending.Messenger != null && report.Messenger == null && report.InformedGuards.Count == 0)
            {
                var messenger = pending.Messenger;
                pending.Store.PlanEntryMessenger(report.Id, messenger.CitizenId, messenger.GuardId, messenger.StartedAt, messenger.Route,
                    DaggerfallUnity.Instance.WorldTime.TimeScale);
                entryJourneys[messenger.CitizenId] = report.Messenger;
            }
            if (report.InformedGuards.Count > 0 && game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None)
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Trespassing;
            var guards = GetComponent<GuardPopulation>();
            if (guards) guards.ReportEntry(report);
            Debug.Log("LIVING_CITIES_ENTRY_WITNESSED: " + report.Id + "; citizens=" + report.CitizenWitnesses.Count + "; guards=" + report.InformedGuards.Count);
        }
        private void UpdateEntryReports(double now)
        {
            if (GameManager.IsGamePaused) return;
            entryJourneys.Clear();
            if (!ReactionsEnabled || worldStore == null || population == null) return;
            var reports = worldStore.EntriesFor(population.SettlementId);
            foreach (var report in reports)
                if (report.Messenger != null && !report.Messenger.Delivered && !report.Messenger.Cancelled &&
                    Reaction(report.Messenger.CitizenId, now) != null) report.Messenger.Cancelled = true;
            var guards = GetComponent<GuardPopulation>();
            foreach (var report in worldStore.AdvanceEntryMessengers(population.SettlementId, now))
            {
                var player = GameManager.Instance.PlayerEntity;
                if (player.CrimeCommitted == PlayerEntity.Crimes.None) player.CrimeCommitted = PlayerEntity.Crimes.Trespassing;
                if (guards) guards.ReportEntry(report);
                Debug.Log("LIVING_CITIES_ENTRY_DELIVERED: " + report.Id);
            }
            foreach (var report in reports)
            {
                var messenger = report.Messenger;
                if (messenger != null && !messenger.Delivered && !messenger.Cancelled) entryJourneys[messenger.CitizenId] = messenger;
                if (guards && report.InformedGuards.Count > 0) guards.ReportEntry(report);
            }
        }
        private ActivitySnapshot EntryState(string id, double now, ActivitySnapshot scheduled)
        {
            AlarmPerson departure; bool home;
            if(worldStore!=null && worldStore.TryVisitDeparturePosition(id,now,out departure,out home))
            {
                if(home && scheduled.Activity==Activity.Sleep && scheduled.BuildingKey==departure.BuildingKey) return scheduled;
                return new ActivitySnapshot(home ? Activity.Home : departure.BuildingKey<0 ? Activity.Travel : Activity.Visit,
                    departure.BuildingKey,new RoutePoint(departure.X,departure.Z),0);
            }
            EntryMessenger messenger;
            if (!entryJourneys.TryGetValue(id, out messenger) || now < messenger.StartedAt || messenger.Cancelled || messenger.Delivered) return scheduled;
            var point = messenger.Position(now);
            return new ActivitySnapshot(Activity.Travel, -1, new RoutePoint(point.X, point.Z), 0);
        }
    }

    public sealed partial class GuardPopulation
    {
        public EntryMessenger EntryMessengerRoute(string citizenId, Vector3 origin, double now)
        {
            if (!OwnsExterior) return null;
            var local = Local(origin);
            EntryMessenger best = null;
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive || guard.Duty != GuardDuty.Posted || guard.Interior != null) continue;
                var route = geometry.RouteBetweenPoints(local, guard.Post);
                if (route.Count < 2) continue;
                var candidate = new EntryMessenger { CitizenId = citizenId, GuardId = guard.Id, StartedAt = now,
                    TimeScale = DaggerfallUnity.Instance.WorldTime.TimeScale, Route = route };
                if (best == null || candidate.ArrivesAt < best.ArrivesAt) best = candidate;
            }
            return best;
        }
        public List<string> EntryObservers(Vector3 target)
        {
            var result = new List<string>();
            if (!OwnsExterior) return result;
            foreach (var pair in actors)
                if (pair.Value && Roster.Guards.Exists(g => g.Id == pair.Key && g.IsAlive && g.Interior == null) &&
                    TheftWitnessSight.CanSee(pair.Value.transform, target, true, true)) result.Add(pair.Key);
            return result;
        }
        public void ReportEntry(EntryReport report)
        {
            if (!OwnsTown() || report == null || report.SettlementId != Roster.SettlementId || report.InformedGuards.Count == 0 ||
                GameManager.Instance.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None) return;
            if (Roster.Reports.Exists(r => r.Id == report.Id && r.GuardIds.Count > 0)) return;
            var sent = Roster.DispatchBuilding(report.Id, report.BuildingKey, Now(), Mathf.Clamp(MaximumResponders, 1, 8), 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale,
                false, new HashSet<string>(report.InformedGuards));
            if (sent.Count > 0) reportId = report.Id;
        }
    }
}
