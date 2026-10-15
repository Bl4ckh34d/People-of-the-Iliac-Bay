using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;
using System.Globalization;

namespace LivingCities.DFU
{
    // Resolve the victim's owning scene, never the player's current room.
    internal sealed class DefenseScene
    {
        public Transform Root;
        public BuildingDirectory Directory;
        public DaggerfallInterior Interior;
        private int settlementId;
        public int BuildingKey { get { return Interior ? Interior.EntryDoor.buildingKey : -1; } }
        public int SettlementId { get { return settlementId; } }

        public static DefenseScene For(Component target)
        {
            if(!target || Ancestor<DaggerfallDungeon>(target.transform)) return null;
            var room=Ancestor<DaggerfallInterior>(target.transform);
            var directory=room ? Ancestor<BuildingDirectory>(room.DoorOwner) : Ancestor<BuildingDirectory>(target.transform);
            int town=directory ? directory.MapID : 0;
            // Restored interiors can lack the exterior door owner. DFU's canonical
            // scene key retains both map and building identity independently of player state.
            if(room && !directory)
            {
                const string prefix="DaggerfallInterior [MapID=";
                if(!room.name.StartsWith(prefix,System.StringComparison.Ordinal)) return null;
                int end=room.name.IndexOf(',',prefix.Length);
                if(end<0 ||
                    !int.TryParse(room.name.Substring(prefix.Length,end-prefix.Length),NumberStyles.Integer,CultureInfo.InvariantCulture,out town) ||
                    room.name!=DaggerfallInterior.GetSceneName(town,room.EntryDoor.buildingKey)) return null;
            }
            // Unowned mod objects need an explicit scene contract; do not guess from player position.
            if(!room && !directory) return null;
            return new DefenseScene { Root=room ? room.transform : directory.transform,Directory=directory,Interior=room,settlementId=town };
        }

        private static T Ancestor<T>(Transform at) where T:Component
        {
            // DFU disables exterior parents while indoors and disables dead actors.
            // Hierarchy ownership is still valid when those objects are inactive.
            for(;at;at=at.parent)
            {
                var owner=at.GetComponent<T>(); if(owner) return owner;
            }
            return null;
        }

        public bool PlayerPresent
        {
            get
            {
                var game=GameManager.Instance;
                return Interior ? game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior==Interior :
                    !game.IsPlayerInside && game.StreamingWorld.GetCurrentBuildingDirectory()==Directory;
            }
        }
    }
}
