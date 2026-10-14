using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed partial class GuardPopulation
    {
        public bool OccupiesCrimeScene(int buildingKey, double now)
        {
            return Roster != null && directory && GameManager.Instance.PlayerGPS.HasCurrentLocation &&
                GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId == Roster.SettlementId &&
                Roster.Reports.Exists(r => r.BuildingKey == buildingKey && r.Investigation && r.HasArrival &&
                    now >= r.FirstArrivalAt && now < r.FirstArrivalAt + UnwitnessedScene.OccupationSeconds);
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
                report.Warning.Advance(Time.unscaledDeltaTime, inside, GameManager.IsGamePaused);
                if (inside && report.Warning.Violated && game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None)
                    game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Trespassing;
            }
        }
    }
}
