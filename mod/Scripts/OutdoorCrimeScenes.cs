using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        public const float OutdoorSceneRadius = 40;
        private float nextOutdoorObservation;
        private void FlushOutdoorDeparture()
        {
            var game = GameManager.Instance;
            if (worldStore == null || population == null || game == null || !game.IsReady || !game.IsPlayingGame()) return;
            bool sameTown = game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId == population.SettlementId;
            if (sameTown && !game.PlayerEnterExit.IsPlayerInside && !directory) return; // Unknown origin during streaming.
            double now = DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach (var alarm in worldStore.OutdoorCorpses(population.SettlementId))
            {
                bool onSite = sameTown && !game.PlayerEnterExit.IsPlayerInside && directory &&
                    (CorpsePoint(alarm) - game.PlayerMotor.transform.position).sqrMagnitude <= OutdoorSceneRadius * OutdoorSceneRadius;
                worldStore.ObserveOutdoorCorpse(alarm.Id, onSite, now);
                var guards=GetComponent<GuardPopulation>();
                if(guards)guards.RequestCorpseDepartureInvestigation(alarm,now);
            }
        }
        private Vector3 CorpsePoint(CivilianIncident alarm)
        {
            var body = alarm.Corpse;
            return directory.transform.TransformPoint(body.HasPosition ? new Vector3((float)body.X, (float)body.Y, (float)body.Z) :
                new Vector3((float)alarm.X, (float)alarm.Y, (float)alarm.Z));
        }
        private void UpdateOutdoorScenes(double now)
        {
            if (!ReactionsEnabled || worldStore == null || GameManager.IsGamePaused || Time.unscaledTime < nextOutdoorObservation) return;
            nextOutdoorObservation = Time.unscaledTime + 0.5f;
            var game = GameManager.Instance; var guards = GetComponent<GuardPopulation>();
            bool outside = !game.PlayerEnterExit.IsPlayerInside && game.PlayerGPS.HasCurrentLocation &&
                game.PlayerGPS.CurrentLocation.MapTableData.MapId == population.SettlementId;
            foreach (var alarm in worldStore.OutdoorCorpses(population.SettlementId))
            {
                if (now < alarm.Corpse.DiedAt) continue;
                var point = CorpsePoint(alarm);
                bool onSite = outside && (game.PlayerMotor.transform.position - point).sqrMagnitude <= OutdoorSceneRadius * OutdoorSceneRadius;
                if (outside && !alarm.Corpse.Cleared)
                {
                    foreach (var actor in actors.Values)
                    {
                        if (!actor || !actor.Resident.IsAlive || !actor.gameObject.activeInHierarchy ||
                            !CivilianResponses.Sees(actor.transform.position, point + Vector3.up * 0.3f, 20)) continue;
                        bool seenPlayer = onSite && !game.PlayerEntity.IsInvisible &&
                            CivilianResponses.Sees(actor.transform.position, game.PlayerMotor.transform.position, 20);
                        worldStore.NoticeOutdoorCorpse(alarm.Id, actor.Resident.Id, false, now, seenPlayer);
                        // Discovery queues a physical report; no remote delivery.
                    }
                    if (guards) guards.ObserveOutdoorCorpse(alarm, point, onSite);
                }
                // Civilian reports stay in the witness ledger until physical contact.
                var seen = alarm.Corpse.Discovery;
                if (outside && seen != null && seen.Guard && guards)
                    guards.ReportRememberedCorpse(alarm, seen.ObserverId);
                worldStore.ObserveOutdoorCorpse(alarm.Id, onSite, now);
                if(guards)guards.RequestCorpseDepartureInvestigation(alarm,now);
            }
            UpdateDefenseCorpseScenes(now,outside);
        }
        // Called only after a future inventory adapter confirms an actual item transfer.
        // Merely activating or examining the placeholder body is harmless.
        public bool ReportCorpseLoot(string incidentId)
        {
            var game = GameManager.Instance;
            if (!ReactionsEnabled || !Ready || worldStore == null || game.PlayerEnterExit.IsPlayerInside) return false;
            var alarm = worldStore.OutdoorCorpses(population.SettlementId).Find(a => a.Id == incidentId);
            if (alarm == null || alarm.Corpse.Cleared || (CorpsePoint(alarm) - game.PlayerMotor.transform.position).sqrMagnitude > 9) return false;
            var observers = new List<Vector3>();
            if (!game.PlayerEntity.IsInvisible)
                foreach (var actor in actors.Values)
                    if (actor && actor.Resident.IsAlive && actor.gameObject.activeInHierarchy &&
                        CivilianResponses.Sees(actor.transform.position, game.PlayerMotor.transform.position, 20)) observers.Add(actor.transform.position);
            var guards = GetComponent<GuardPopulation>();
            if (guards) guards.CorpseInteractionObservers(observers);
            if (observers.Count == 0) return false;
            if (game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None || game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.Trespassing)
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Theft;
            if (guards) foreach (var observer in observers) guards.ReportCorpseTheft(alarm.Id, observer);
            return true;
        }
    }
    public sealed class CorpseInteraction : MonoBehaviour, IPlayerActivable
    {
        public StreetPopulation Streets;
        public string IncidentId;
        public void Activate(RaycastHit hit)
        {
            if (hit.distance > 3 || !Streets) return;
            Streets.OpenCorpseInventory(IncidentId);
        }
    }
}
