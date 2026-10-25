using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit development capture from the real Privateer's Hold dungeon scene.
    internal static class NativeDungeonNavigationCapture
    {
        private const int DungeonMapId=187853213;

        public static IEnumerator Run(string folder)
        {
            var game=GameManager.Instance;
            if(string.IsNullOrEmpty(folder) || !game || game.PlayerEntity==null ||
                !game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal) ||
                !game.PlayerEnterExit || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Dungeon capture requires an owned exterior test fixture and export folder.");

            DFLocation location;
            if(!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall","Privateer's Hold",out location) ||
                !location.Loaded || !location.HasDungeon || location.MapTableData.MapId!=DungeonMapId)
                throw new InvalidOperationException("Exact Privateer's Hold dungeon location is unavailable.");

            var pixel=MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude,location.MapTableData.Latitude);
            game.StreamingWorld.TeleportToCoordinates(pixel.X,pixel.Y,StreamingWorld.RepositionMethods.RandomStartMarker);
            yield return null;

            DaggerfallStaticDoors owner=null;
            StaticDoor entrance=default(StaticDoor);
            float deadline=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<deadline)
            {
                if(game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer &&
                    game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId==DungeonMapId)
                {
                    var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                    if(directory && directory.MapID==DungeonMapId)
                    {
                        foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                        {
                            foreach(var door in candidate.Doors??new StaticDoor[0])
                                if(door.doorType==DoorTypes.DungeonEntrance)
                                {owner=candidate;entrance=door;break;}
                            if(owner)break;
                        }
                        if(owner)break;
                    }
                }
                yield return null;
            }
            if(!owner)throw new InvalidOperationException("Privateer's Hold native dungeon entrance did not load.");

            game.PlayerEnterExit.TransitionDungeonInterior(owner.transform,entrance,game.PlayerGPS.CurrentLocation);
            deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return null;
                var dungeon=game.PlayerEnterExit.Dungeon;
                if(!game.PlayerEnterExit.IsPlayerInsideDungeon || !dungeon ||
                    dungeon.Summary.ID!=DungeonMapId || !dungeon.StartMarker ||
                    dungeon.GetComponentsInChildren<Collider>().Length==0)continue;
                DaggerfallUI.Instance.PopToHUD();
                yield return new WaitForSecondsRealtime(1);
                yield return NativeNavigationWorld.ExportLoadedScene(folder);
                yield return NativeDungeonNavigationCatalog.ExportLoaded(folder);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_DUNGEON_CAPTURE_PASS: scene=dungeon-"+DungeonMapId+"; nativeEntrance=true");
                yield break;
            }
            throw new InvalidOperationException("Privateer's Hold native dungeon transition did not complete.");
        }
    }
}
