using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit test launcher only. Produces clean, crime-free outdoor previews
    // without importing old population state or pinning obsolete interior layouts.
    public static class FreshPreviewSaves
    {
        static string expectedCharacter;
        public static IEnumerator Run(StreetPopulation streets, GuardPopulation guards, string character)
        {
            var game = GameManager.Instance;
            if (!game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal) || game.PlayerEntity.Name != character)
                throw new InvalidOperationException("Fresh previews require a disposable test character.");
            expectedCharacter = character;
            if (game.PlayerGPS.CurrentLocation.Name != "Gothway Garden")
                throw new InvalidOperationException("Fresh previews must begin in Gothway Garden.");
            Noon();
            yield return Ready(streets, guards);
            Center();
            yield return SaveAndReload("People - Gothway Garden", streets, guards);
            Entrance(DFLocation.BuildingTypes.GeneralStore);
            yield return SaveAndReload("People - Shop entrance", streets, guards);
            Entrance(DFLocation.BuildingTypes.Tavern);
            yield return SaveAndReload("People - Inn entrance", streets, guards);

            DFLocation city;
            if (!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall", "Daggerfall", out city))
                throw new InvalidOperationException("Daggerfall location missing.");
            var pixel = MapsFile.LongitudeLatitudeToMapPixel(city.MapTableData.Longitude, city.MapTableData.Latitude);
            game.StreamingWorld.TeleportToCoordinates(pixel.X, pixel.Y);
            yield return null;
            float deadline = Time.realtimeSinceStartup + 90;
            while ((!game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId != city.MapTableData.MapId ||
                !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer) && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId != city.MapTableData.MapId)
                throw new InvalidOperationException("Fresh city arrival timed out.");
            yield return Ready(streets, guards);
            Center();
            yield return new WaitForSecondsRealtime(15);
            yield return SaveAndReload("People - Daggerfall day", streets, guards);
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((22 * 3600 - clock.ToSeconds() % 86400 + 86400) % 86400));
            yield return new WaitForSecondsRealtime(5);
            yield return SaveAndReload("People - Daggerfall night", streets, guards);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: fresh previews; character=" + game.PlayerEntity.Name + "; saves=5; reloads=5");
        }

        static void Noon()
        {
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((43200 - clock.ToSeconds() % 86400 + 86400) % 86400));
        }

        static IEnumerator Ready(StreetPopulation streets, GuardPopulation guards)
        {
            var game = GameManager.Instance;
            float deadline = Time.realtimeSinceStartup + 90;
            while ((!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer || !streets.Ready ||
                !guards.OwnsSettlement || guards.Roster.SettlementId != game.PlayerGPS.CurrentLocation.MapTableData.MapId) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            if (!streets.Ready || !guards.OwnsSettlement || guards.Roster.SettlementId != game.PlayerGPS.CurrentLocation.MapTableData.MapId)
                throw new InvalidOperationException("Fresh preview population readiness timed out.");
        }

        static void Center()
        {
            var game = GameManager.Instance;
            var nav = game.StreamingWorld.GetCurrentCityNavigation();
            int best = int.MaxValue; DFPosition selected = new DFPosition();
            for (int z = 0; z < nav.NavGridHeight; z++)
                for (int x = 0; x < nav.NavGridWidth; x++)
                    if (nav.GetNavGridWeightLocal(x, z) > 0)
                    {
                        int distance = Math.Abs(x - nav.NavGridWidth / 2) + Math.Abs(z - nav.NavGridHeight / 2);
                        if (distance < best) { best = distance; selected = new DFPosition(x, z); }
                    }
            if (best == int.MaxValue) throw new InvalidOperationException("No walkable preview position.");
            game.PlayerMotor.transform.position = nav.WorldToScenePosition(nav.NavGridToWorldPosition(selected)) + Vector3.up * 2;
        }

        static void Entrance(DFLocation.BuildingTypes type)
        {
            var game = GameManager.Instance;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var buildings = directory.GetBuildingsOfType(type);
            foreach (var building in buildings)
                foreach (var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                {
                    if (owner.Doors == null) continue;
                    for (int i = 0; i < owner.Doors.Length; i++)
                        if (owner.Doors[i].doorType == DoorTypes.Building && owner.Doors[i].buildingKey == building.buildingKey)
                        {
                            var normal = owner.GetDoorNormal(i); normal.y = 0; normal.Normalize();
                            game.PlayerMotor.transform.position = owner.GetDoorPosition(i) + normal * 2.5f + Vector3.up * 1.5f;
                            game.PlayerMotor.transform.rotation = Quaternion.LookRotation(-normal);
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_PREVIEW_ENTRANCE: type=" + type + "; building=" + building.buildingKey);
                            return;
                        }
                }
            throw new InvalidOperationException("Fresh preview entrance missing: " + type);
        }

        static IEnumerator SaveAndReload(string name, StreetPopulation streets, GuardPopulation guards)
        {
            var game = GameManager.Instance;
            yield return new WaitForSecondsRealtime(2);
            if (game.PlayerEntity.Name != expectedCharacter)
                throw new InvalidOperationException("Fresh preview character changed unexpectedly.");
            bool saved = false, loaded = false;
            int map = game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.Save(game.PlayerEntity.Name, name);
                float deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Fresh preview save timed out: " + name);
                int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, name);
                if (key < 0) throw new InvalidOperationException("Fresh preview not enumerated: " + name);
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while (!loaded && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded) throw new InvalidOperationException("Fresh preview reload timed out: " + name);
                yield return Ready(streets, guards);
                if (game.PlayerEntity.Name != expectedCharacter || game.IsPlayerInside || game.PlayerGPS.CurrentLocation.MapTableData.MapId != map ||
                    game.PlayerEntity.CrimeCommitted != DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Fresh preview must reload outdoors without a crime: " + name);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEST_SAVE: " + name + "; folder=" + game.SaveLoadManager.GetSaveFolder(key));
            }
            finally { SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad; }
        }
    }
}
