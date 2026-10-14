using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Disposable test-session survey. Does not alter saves or enable city actors.
    public static class DaggerfallRefugeProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            DFLocation city;
            if (!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall", "Daggerfall", out city))
                throw new InvalidOperationException("Daggerfall city missing.");
            DFPosition pixel = MapsFile.LongitudeLatitudeToMapPixel(city.MapTableData.Longitude, city.MapTableData.Latitude);
            game.PlayerEntity.GodMode = true;
            game.StreamingWorld.TeleportToCoordinates(pixel.X, pixel.Y);
            yield return null;
            float deadline = Time.realtimeSinceStartup + 90;
            while ((!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer ||
                !game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId != city.MapTableData.MapId) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            if (!game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId != city.MapTableData.MapId)
                throw new InvalidOperationException("Daggerfall survey arrival timed out.");
            yield return new WaitForSecondsRealtime(2);
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry = new TownGeometry(directory, game.StreamingWorld.GetCurrentCityNavigation());
            int candidates = 0, connected = 0;
            foreach (var entrance in geometry.Entrances.Values)
            {
                BuildingSummary building;
                if (!directory.GetBuildingSummary(entrance.BuildingKey, out building) ||
                    building.BuildingType != DFLocation.BuildingTypes.Town23) continue;
                candidates++;
                bool reached = false;
                foreach (var other in geometry.Entrances.Values)
                {
                    if (other.BuildingKey == entrance.BuildingKey ||
                        Vector3.Distance(other.LocalDoor, entrance.LocalDoor) > 100) continue;
                    var route = geometry.RouteBetween(other, entrance);
                    if (route.Count < 2) continue;
                    connected++; reached = true;
                    Debug.Log("LIVING_CITIES_REFUGE_ROUTE: key=" + entrance.BuildingKey + "; model=" + building.ModelID +
                        "; from=" + other.BuildingKey + "; points=" + route.Count + "; door=" + entrance.LocalDoor);
                    break;
                }
                if (!reached) Debug.Log("LIVING_CITIES_REFUGE_UNREACHED: key=" + entrance.BuildingKey + "; model=" + building.ModelID);
                yield return null;
            }
            Debug.Log("LIVING_CITIES_REFUGE_SURVEY: groundEntrances=" + candidates + "; connected=" + connected +
                "; rejectedDoors=" + geometry.RejectedDoors);
            if (connected == 0) throw new InvalidOperationException("No reachable Daggerfall wall entrance found.");
            Debug.Log("LIVING_CITIES_TOWN_TEST_PASS: Daggerfall exterior refuge survey only; no interior safety claim.");
        }
    }
}
