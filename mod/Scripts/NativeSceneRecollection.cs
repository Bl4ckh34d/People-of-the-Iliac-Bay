using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeSceneRecollection
    {
        internal static void Remember(WorldStateStore store,string person,string id,NpcSceneEventKind kind,string observation,
            int? valence=null,string subject=null,string responsible=null)
        {
            if(store==null || string.IsNullOrEmpty(person))return;
            try
            {
                var game=GameManager.Instance;
                string place=game.PlayerEnterExit.IsPlayerInsideDungeon?"inside a dungeon":
                    game.PlayerEnterExit.IsPlayerInsideBuilding?game.TalkManager.GetBuildingNameForBuildingKey(game.PlayerEnterExit.BuildingDiscoveryData.buildingKey):
                    game.PlayerGPS.HasCurrentLocation?game.PlayerGPS.CurrentLocation.Name:"the wilderness";
                string text=(observation+" Location: "+place+".").Replace('\r',' ').Replace('\n',' ').Replace('\t',' ');
                if(text.Length>600)text=text.Substring(0,600);
                store.RememberSceneEvent(person,id,kind,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                    game.PlayerGPS.HasCurrentLocation?game.PlayerGPS.CurrentLocation.MapTableData.MapId:0,
                    game.PlayerEnterExit.IsPlayerInsideBuilding?game.PlayerEnterExit.BuildingDiscoveryData.buildingKey:-1,text,valence,subject,responsible);
            }
            catch(Exception error){Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SCENE_MEMORY_FAILED: "+error.Message);}
        }
    }
}
