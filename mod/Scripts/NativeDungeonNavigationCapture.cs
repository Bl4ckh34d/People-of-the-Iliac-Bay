using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit development capture from a real named native dungeon scene.
    internal static class NativeDungeonNavigationCapture
    {
        public static IEnumerator Run(string folder)
        {
            var game=GameManager.Instance;
            if(string.IsNullOrEmpty(folder) || !game || game.PlayerEntity==null ||
                !NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) ||
                !game.PlayerEnterExit || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Dungeon capture requires an owned exterior test fixture and export folder.");

            DFLocation location;
            string name=Environment.GetEnvironmentVariable("POTIB_NAVMESH_LOCATION")??"Privateer's Hold";
            string region=Environment.GetEnvironmentVariable("POTIB_NAVMESH_REGION")??"Daggerfall";
            if(!DaggerfallUnity.Instance.ContentReader.GetLocation(region,name,out location) ||
                !location.Loaded || !location.HasDungeon)
                throw new InvalidOperationException("Requested native dungeon location is unavailable: "+region+"/"+name);
            int dungeonMapId=location.MapTableData.MapId;

            var pixel=MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude,location.MapTableData.Latitude);
            game.StreamingWorld.TeleportToCoordinates(pixel.X,pixel.Y,StreamingWorld.RepositionMethods.RandomStartMarker);
            yield return null;

            DaggerfallStaticDoors owner=null;
            StaticDoor entrance=default(StaticDoor);
            float deadline=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<deadline)
            {
                if(game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer &&
                    game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId==dungeonMapId)
                {
                    var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                    if(directory && directory.MapID==dungeonMapId)
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
            if(!owner)throw new InvalidOperationException("Requested native dungeon entrance did not load: "+name);

            game.PlayerEnterExit.TransitionDungeonInterior(owner.transform,entrance,game.PlayerGPS.CurrentLocation);
            deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return null;
                var dungeon=game.PlayerEnterExit.Dungeon;
                if(!game.PlayerEnterExit.IsPlayerInsideDungeon || !dungeon ||
                    dungeon.Summary.ID!=dungeonMapId || !dungeon.StartMarker ||
                    dungeon.GetComponentsInChildren<Collider>().Length==0)continue;
                DaggerfallUI.Instance.PopToHUD();
                yield return new WaitForSecondsRealtime(1);
                NativeDungeonModuleProbe.VerifyStaticInputs(dungeon.transform);
                if(Environment.GetEnvironmentVariable("POTIB_NAVMESH_DUNGEON_MODULES_ONLY")!="1")
                    yield return NativeNavigationWorld.ExportLoadedScene(folder);
                yield return NativeDungeonNavigationCatalog.ExportLoaded(folder);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_DUNGEON_CAPTURE_PASS: scene=dungeon-"+dungeonMapId+"; nativeEntrance=true");
                yield break;
            }
            throw new InvalidOperationException("Requested native dungeon transition did not complete: "+name);
        }
    }
}
