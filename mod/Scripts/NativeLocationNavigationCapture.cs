using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit development capture. Native StreamingWorld supplies terrain,
    // location overrides, climate replacements, and final RMB placement.
    internal static class NativeLocationNavigationCapture
    {
        public static IEnumerator Run(string folder, string region, string locationName)
        {
            var game = GameManager.Instance;
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(region) ||
                string.IsNullOrWhiteSpace(locationName) || !game || !game.StreamingWorld ||
                !game.PlayerEnterExit || game.PlayerEnterExit.IsPlayerInside ||
                game.PlayerEntity == null ||
                !game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-", StringComparison.Ordinal))
                throw new InvalidOperationException("Location capture requires an owned exterior test fixture and a target location.");

            DFLocation target;
            if (!DaggerfallUnity.Instance.ContentReader.GetLocation(region, locationName, out target) ||
                !target.Loaded || target.Exterior.ExteriorData.Width < 1 || target.Exterior.ExteriorData.Height < 1)
                throw new InvalidOperationException("Navigation capture target is not a loaded exterior location.");

            int mapId = target.MapTableData.MapId;
            int expectedBlocks = target.Exterior.ExteriorData.Width * target.Exterior.ExteriorData.Height;
            var pixel = MapsFile.LongitudeLatitudeToMapPixel(target.MapTableData.Longitude, target.MapTableData.Latitude);
            var world = game.StreamingWorld;
            var existingDirectory = world.GetCurrentBuildingDirectory();
            bool layoutComplete = false;
            StreamingWorld.OnUpdateLocationGameObjectEventHandler onLocation = (objectInScene, allowYield) =>
            {
                var directory = objectInScene ? objectInScene.GetComponent<BuildingDirectory>() : null;
                if (directory && directory.MapID == mapId) layoutComplete = true;
            };
            StreamingWorld.OnUpdateLocationGameObject += onLocation;
            try
            {
                world.TeleportToCoordinates(pixel.X, pixel.Y, StreamingWorld.RepositionMethods.RandomStartMarker);
                float deadline = Time.realtimeSinceStartup + 180f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    if (world.IsReady && !world.IsRepositioningPlayer &&
                        !game.PlayerEnterExit.IsPlayerInside && game.PlayerGPS.HasCurrentLocation &&
                        game.PlayerGPS.CurrentLocation.MapTableData.MapId == mapId)
                    {
                        var directory = world.GetCurrentBuildingDirectory();
                        var nativeLocation = directory ? directory.GetComponent<DaggerfallLocation>() : null;
                        var terrainObject = nativeLocation ? world.GetTerrainFromPixel(
                            nativeLocation.Summary.MapPixelX, nativeLocation.Summary.MapPixelY) : null;
                        var terrain = terrainObject ? terrainObject.GetComponent<Terrain>() : null;
                        var terrainCollider = terrainObject ? terrainObject.GetComponent<TerrainCollider>() : null;
                        bool completeBlocks = directory && directory.MapID == mapId &&
                            directory.GetComponentsInChildren<DaggerfallRMBBlock>().Length == expectedBlocks;
                        bool completeTerrain = terrainObject && terrainObject.activeInHierarchy &&
                            terrain && terrain.terrainData && terrainCollider &&
                            terrainCollider.enabled && terrainCollider.terrainData == terrain.terrainData;
                        if (completeBlocks && completeTerrain &&
                            (layoutComplete || directory == existingDirectory))
                        {
                            // A reused native location can already be laid out without a new event.
                            // Let completion callbacks and collider changes settle before export.
                            yield return new WaitForEndOfFrame();
                            yield return null;
                            if ((!layoutComplete && directory != existingDirectory) ||
                                world.GetCurrentBuildingDirectory() != directory ||
                                directory.GetComponentsInChildren<DaggerfallRMBBlock>().Length != expectedBlocks)
                                continue;
                            yield return NativeNavigationWorld.ExportLoadedScene(folder);
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_LOCATION_CAPTURE_PASS: scene=city-"+
                                mapId+"; blocks="+expectedBlocks+"; nativeTerrain=true; season="+
                                DaggerfallUnity.Instance.WorldTime.Now.SeasonValue+"; profile="+NativeNavigationWorld.Profile());
                            yield break;
                        }
                    }
                    yield return null;
                }
                throw new InvalidOperationException("Native location terrain and full RMB layout did not settle for map " + mapId + ".");
            }
            finally { StreamingWorld.OnUpdateLocationGameObject -= onLocation; }
        }
    }
}
