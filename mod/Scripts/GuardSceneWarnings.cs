using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed partial class GuardPopulation
    {
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
