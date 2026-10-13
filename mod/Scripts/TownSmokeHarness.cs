using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Player;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Explicit command-line test mode only; never starts a character during normal play.
    public sealed class TownSmokeHarness : MonoBehaviour
    {
        private bool saved, loaded;
        private void OnSaved(SaveData_v1 ignored) { saved = true; }
        private void OnLoaded(SaveData_v1 ignored) { loaded = true; }
        private void OnDestroy() { SaveLoadManager.OnSave -= OnSaved; SaveLoadManager.OnLoad -= OnLoaded; }
        private IEnumerator Start()
        {
            while (GameManager.Instance == null || !GameManager.Instance.IsReady) yield return null;
            yield return null;
            var game = GameManager.Instance;
            DFLocation location;
            if (!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall", "Gothway Garden", out location))
            { Debug.LogError("LIVING_CITIES_TOWN_FAIL: test settlement missing."); yield break; }
            DFPosition pixel = MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude, location.MapTableData.Latitude);
            string[] arguments = Environment.GetCommandLineArgs();
            int fixtureArgument = Array.IndexOf(arguments, "-livingcities-fixture-character");
            if (fixtureArgument >= 0)
            {
                if (fixtureArgument + 1 >= arguments.Length || !arguments[fixtureArgument + 1].StartsWith("LivingCitiesTest-", StringComparison.Ordinal))
                { Debug.LogError("LIVING_CITIES_TOWN_FAIL: invalid fixture character."); yield break; }
                yield return VerifyLoads(arguments[fixtureArgument + 1], pixel, location.MapTableData.MapId);
                yield break;
            }
            DaggerfallUnity.Settings.StartCellX = pixel.X;
            DaggerfallUnity.Settings.StartCellY = pixel.Y;
            DaggerfallUnity.Settings.StartInDungeon = false;
            var start = game.StartGameBehaviour;
            start.NoWorld = false;
            start.EnableVideos = false;
            start.GodMode = true;
            start.CharacterDocument = new CharacterDocument {
                name = "LivingCitiesTest-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                backStory = new List<string> { "Disposable Living Cities integration-test character." },
                biographyEffects = new List<string>() };
            start.StartMethod = StartGameBehaviour.StartMethods.NewCharacter;
            Debug.Log("LIVING_CITIES_TOWN_START: " + location.Name + "; pixel=" + pixel.X + "," + pixel.Y);
            float deadline = Time.realtimeSinceStartup + 90;
            BuildingDirectory directory = null;
            CityNavigation nav = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId == location.MapTableData.MapId &&
                    game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer)
                {
                    directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                    nav = game.StreamingWorld.GetCurrentCityNavigation();
                    if (directory != null && directory.BuildingCount > 0 && nav != null && nav.NavGridWidth > 0) break;
                }
                yield return null;
            }
            if (directory == null || nav == null)
            { Debug.LogError("LIVING_CITIES_TOWN_FAIL: town did not finish loading."); yield break; }
            yield return new WaitForSecondsRealtime(2);
            // Dismiss only the disposable new-character introduction, leaving the HUD.
            if (DaggerfallUI.UIManager.TopWindow is DaggerfallMessageBox)
                DaggerfallUI.UIManager.PopWindow();
            VerifyGeometry(directory, nav);
            int homes = 0, doors = 0, walkable = 0;
            int bestDistance = int.MaxValue;
            DFPosition centerCell = new DFPosition();
            for (int kind = (int)DFLocation.BuildingTypes.House1; kind <= (int)DFLocation.BuildingTypes.House6; kind++)
                homes += directory.GetBuildingsOfType((DFLocation.BuildingTypes)kind).Count;
            foreach (DaggerfallStaticDoors owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                if (owner.Doors != null) doors += owner.Doors.Length;
            for (int z = 0; z < nav.NavGridHeight; z++)
                for (int x = 0; x < nav.NavGridWidth; x++)
                    if (nav.GetNavGridWeightLocal(x, z) > 0)
                    {
                        walkable++;
                        int distance = Math.Abs(x - nav.NavGridWidth / 2) + Math.Abs(z - nav.NavGridHeight / 2);
                        if (distance < bestDistance) { bestDistance = distance; centerCell = new DFPosition(x, z); }
                    }
            if (homes < 1 || doors < 1 || walkable < 1)
            { Debug.LogError("LIVING_CITIES_TOWN_FAIL: incomplete town census."); yield break; }
            Debug.Log("LIVING_CITIES_TOWN_SURVEY_PASS: map=" + directory.MapID + "; buildings=" + directory.BuildingCount +
                "; homes=" + homes + "; doors=" + doors + "; walkable=" + walkable + "; grid=" + nav.NavGridWidth + "x" + nav.NavGridHeight);
            game.PlayerEntity.GodMode = true;
            game.PlayerMotor.transform.position = nav.WorldToScenePosition(nav.NavGridToWorldPosition(centerCell)) + Vector3.up * 2;
            yield return new WaitForSecondsRealtime(3);
            SaveLoadManager.OnSave += OnSaved;
            saved = false;
            game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - inside Gothway Garden");
            deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) { Debug.LogError("LIVING_CITIES_TOWN_FAIL: inside save timed out."); yield break; }
            yield return null;
            int insideKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - inside Gothway Garden");
            Debug.Log("LIVING_CITIES_TEST_SAVE: inside=" + game.SaveLoadManager.GetSaveFolder(insideKey));

            // One map pixel away, outside the settlement's location rect. Town geometry
            // may still be streamed nearby; reload this save to exercise a fresh scene.
            game.StreamingWorld.TeleportToCoordinates(pixel.X + 1, pixel.Y);
            deadline = Time.realtimeSinceStartup + 90;
            yield return null;
            while ((!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer || game.PlayerGPS.CurrentMapPixel.X != pixel.X + 1) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            if (!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer ||
                game.PlayerGPS.CurrentMapPixel.X != pixel.X + 1 || game.PlayerGPS.CurrentMapPixel.Y != pixel.Y)
            { Debug.LogError("LIVING_CITIES_TOWN_FAIL: outside reposition timed out."); yield break; }
            yield return new WaitForSecondsRealtime(3);
            saved = false;
            game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - outside Gothway Garden");
            deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) { Debug.LogError("LIVING_CITIES_TOWN_FAIL: outside save timed out."); yield break; }
            yield return null;
            int outsideKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - outside Gothway Garden");
            Debug.Log("LIVING_CITIES_TEST_SAVE: outside=" + game.SaveLoadManager.GetSaveFolder(outsideKey));
            Debug.Log("LIVING_CITIES_TOWN_SAVES_PASS: character=" + game.PlayerEntity.Name + "; insideKey=" + insideKey + "; outsideKey=" + outsideKey);
            yield return VerifyLoads(game.PlayerEntity.Name, pixel, location.MapTableData.MapId);
        }

        private IEnumerator VerifyLoads(string character, DFPosition pixel, int mapId)
        {
            var game = GameManager.Instance;
            var adapter = GetComponent<LivingCitiesMod>();
            SaveLoadManager.OnLoad += OnLoaded;
            string expectedPopulation = null;
            // Inside, outside, then town return exercise real save slots and streamed
            // geometry. Fixture-character mode also tests saves from an earlier process.
            foreach (bool inside in new[] { true, false, true })
            {
                game.SaveLoadManager.EnumerateSaves();
                string name = "Living Cities - " + (inside ? "inside" : "outside") + " Gothway Garden";
                int key = game.SaveLoadManager.FindSaveFolderByNames(character, name);
                if (key < 0) { Debug.LogError("LIVING_CITIES_TOWN_FAIL: missing fixture " + name); yield break; }
                loaded = false;
                game.SaveLoadManager.Load(key);
                float deadline = Time.realtimeSinceStartup + 90;
                while ((!loaded || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer) &&
                    Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer)
                { Debug.LogError("LIVING_CITIES_TOWN_FAIL: fixture load timed out."); yield break; }
                yield return new WaitForSecondsRealtime(2);
                bool correctPosition = game.PlayerGPS.CurrentMapPixel.X == pixel.X + (inside ? 0 : 1) &&
                    game.PlayerGPS.CurrentMapPixel.Y == pixel.Y;
                if (inside) correctPosition &= game.PlayerGPS.HasCurrentLocation &&
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId == mapId &&
                    game.StreamingWorld.GetCurrentBuildingDirectory() != null;
                else correctPosition &= !game.PlayerGPS.HasCurrentLocation ||
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId != mapId;
                if (!correctPosition || game.PlayerEntity.Name != character)
                { Debug.LogError("LIVING_CITIES_TOWN_FAIL: wrong character or location after load."); yield break; }
                if (inside) VerifyGeometry(game.StreamingWorld.GetCurrentBuildingDirectory(), game.StreamingWorld.GetCurrentCityNavigation());
                WorldState state = (WorldState)adapter.GetSaveData();
                SettlementPopulation town = state.Settlements.Find(value => value.SettlementId == mapId);
                if (town == null || town.Households.Count == 0)
                { Debug.LogError("LIVING_CITIES_TOWN_FAIL: town population missing after load."); yield break; }
                string actual = JsonUtility.ToJson(state);
                if (expectedPopulation != null && expectedPopulation != actual)
                { Debug.LogError("LIVING_CITIES_TOWN_FAIL: population changed across fixture loads."); yield break; }
                expectedPopulation = actual;
                Debug.Log("LIVING_CITIES_TOWN_LOAD_PASS: " + (inside ? "inside" : "outside") + "; households=" + town.Households.Count);
            }
            Debug.Log("LIVING_CITIES_TOWN_TEST_PASS: real saves and repeated loads; character=" + character);
        }
        private static void VerifyGeometry(BuildingDirectory directory, CityNavigation navigation)
        {
            var geometry = new TownGeometry(directory, navigation);
            if (geometry.Entrances.Count < 2) throw new InvalidOperationException("Town has insufficient reachable entrances.");
            var entrances = new List<TownGeometry.Entrance>(geometry.Entrances.Values);
            int routes = 0;
            for (int i = 1; i < Math.Min(entrances.Count, 12); i++)
            {
                var path = GridPathfinder.Find(geometry, entrances[0].Cell, entrances[i].Cell, 30000);
                if (path.Count > 1) routes++;
            }
            if (routes == 0) throw new InvalidOperationException("No real town entrance routes found.");
            Debug.Log("LIVING_CITIES_TOWN_GEOMETRY_PASS: entrances=" + geometry.Entrances.Count +
                "; rejectedDoors=" + geometry.RejectedDoors + "; sampledRoutes=" + routes);
        }
    }
}
