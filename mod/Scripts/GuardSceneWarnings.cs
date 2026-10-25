using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using System;
using System.Collections.Generic;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private readonly Dictionary<string, int> sceneWarningSeconds = new Dictionary<string, int>();
        public bool OccupiesCrimeScene(int buildingKey, double now)
        {
            return Roster != null && directory && GameManager.Instance.PlayerGPS.HasCurrentLocation &&
                GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId == Roster.SettlementId &&
                Roster.Reports.Exists(r => r.BuildingKey == buildingKey && r.OccupiesScene && r.HasArrival &&
                    now >= r.FirstArrivalAt && now < r.FirstArrivalAt + UnwitnessedScene.OccupationSeconds &&
                    Roster.Guards.Exists(g=>g.IsAlive && r.GuardIds.Contains(g.Id) && g.Interior!=null &&
                        !g.Interior.Returning && g.Interior.BuildingKey==buildingKey));
        }
        private void AdvanceSceneWarnings()
        {
            var game = GameManager.Instance;
            if (Roster == null || game == null || !game.IsReady || !game.IsPlayingGame()) return;
            foreach (var report in Roster.Reports)
            {
                if (report.Warning == null) continue;
                bool inside = game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId == Roster.SettlementId &&
                    game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior &&
                    game.PlayerEnterExit.Interior.EntryDoor.buildingKey == report.BuildingKey;
                if (!inside)
                {
                    report.Warning.Advance(Time.unscaledDeltaTime, false, GameManager.IsGamePaused);
                    sceneWarningSeconds.Remove(report.Id);
                    continue;
                }
                int before;
                if (!sceneWarningSeconds.TryGetValue(report.Id, out before))
                {
                    before = report.Warning.DisplaySeconds;
                    if (report.Warning.Issued && before > 0) ShowSceneWarning(report, before);
                }
                report.Warning.Advance(Time.unscaledDeltaTime, true, GameManager.IsGamePaused);
                int remaining = report.Warning.DisplaySeconds;
                if (!GameManager.IsGamePaused && remaining > 0 && remaining < before)
                    ShowSceneWarning(report, remaining);
                else if (remaining > 0) sceneWarningSeconds[report.Id] = remaining;
                if (inside && report.Warning.Violated && game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None)
                    game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Trespassing;
            }
        }
        private void ShowSceneWarning(GuardReport report, int seconds)
        {
            sceneWarningSeconds[report.Id] = seconds;
            string time = seconds == 1 ? "second" : "seconds";
            DaggerfallUI.AddHUDText("Leave this crime scene. You have " + seconds + " " + time + " to exit the house.");
        }
    }
}
