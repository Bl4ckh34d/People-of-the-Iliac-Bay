using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class DefenseSceneProbe
    {
        public static IEnumerator Run(WorldStateStore store)
        {
            var game=GameManager.Instance; var room=game.PlayerEnterExit.Interior;
            GameObject body=null,otherRoom=null;
            try
            {
                var geometry=new InteriorGeometry(room);
                body=GameObjectHelper.CreateFoeGameObjects(room.transform.TransformPoint(geometry.LocalAnchors[0])+Vector3.up,
                    MobileTypes.Thief,1,MobileReactions.Hostile)[0];
                body.SetActive(true); yield return null;
                var target=body.GetComponent<DaggerfallEntityBehaviour>();
                body.GetComponent<EnemyMotor>().enabled=false; body.GetComponent<EnemySenses>().enabled=false;
                body.GetComponent<EnemyAttack>().enabled=false;
                target.Entity.MaxHealth=500; target.Entity.SetHealth(500,true);
                target.DamageHealthFromSource(game.PlayerEntityBehaviour,1,false,Vector3.zero);
                string id="native:"+body.GetComponent<DaggerfallEnemy>().LoadID;
                otherRoom=new GameObject("Defense alternate interior fixture");
                otherRoom.transform.position=room.transform.position+new Vector3(100,0,100);
                var interior=otherRoom.AddComponent<DaggerfallInterior>();
                var door=room.EntryDoor; door.buildingKey++;
                otherRoom.name=DaggerfallInterior.GetSceneName(game.PlayerGPS.CurrentLocation.MapTableData.MapId,door.buildingKey);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(DaggerfallInterior).GetField("entryDoor",flags).SetValue(interior,door);
                typeof(DaggerfallInterior).GetField("doorOwner",flags).SetValue(interior,room.DoorOwner);
                body.transform.SetParent(otherRoom.transform,false); body.transform.localPosition=new Vector3(2,1,3);
                target.DamageHealthFromSource(game.PlayerEntityBehaviour,1000,false,Vector3.zero);
                var record=store.GetDefenseCombatant(id);
                if(record==null || record.Death==null || record.Death.BuildingKey!=door.buildingKey ||
                    Math.Abs(record.Death.X-2)>.01 || Math.Abs(record.Death.Y)>.01 || Math.Abs(record.Death.Z-3)>.01)
                    throw new InvalidOperationException("Defense death used player room instead of target-owned scene; scene="+
                        (DefenseScene.For(target)!=null)+"; record="+(record!=null)+"; death="+(record!=null && record.Death!=null));
                float deadline=Time.realtimeSinceStartup+5;
                while(!target.CorpseLootContainer && Time.realtimeSinceStartup<deadline) yield return null;
                if(!target.CorpseLootContainer || target.CorpseLootContainer.transform.parent!=otherRoom.transform)
                    throw new InvalidOperationException("Delayed native corpse remained in player room instead of victim room.");
                UnityEngine.Object.Destroy(body); body=null;
                // An exterior-owned actor stays exterior even when its caster/player is indoors.
                body=GameObjectHelper.CreateFoeGameObjects(room.transform.TransformPoint(geometry.LocalAnchors[0])+Vector3.up,
                    MobileTypes.Thief,1,MobileReactions.Hostile)[0];
                body.SetActive(true); yield return null;
                target=body.GetComponent<DaggerfallEntityBehaviour>();
                body.GetComponent<EnemyMotor>().enabled=false; body.GetComponent<EnemyAttack>().enabled=false;
                body.GetComponent<EnemySenses>().enabled=false;
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                body.transform.SetParent(directory.transform,false); body.transform.localPosition=new Vector3(4,1,5);
                id="native:"+body.GetComponent<DaggerfallEnemy>().LoadID;
                target.DamageHealthFromSource(game.PlayerEntityBehaviour,1000,false,Vector3.zero);
                record=store.GetDefenseCombatant(id);
                if(record==null || record.Death==null || record.Death.BuildingKey!=-1 ||
                    Math.Abs(record.Death.X-4)>.01 || Math.Abs(record.Death.Z-5)>.01 || record.Witnesses.Count!=0)
                    throw new InvalidOperationException("Exterior death inherited indoor player context or witnesses.");
                Debug.Log("LIVING_CITIES_DEFENSE_SCENE_PASS: relocated target keeps its own interior coordinates and native corpse parent while player remains elsewhere.");
                Debug.Log("LIVING_CITIES_DEFENSE_EXTERIOR_CONTEXT_PASS: inactive exterior owner supplies death coordinates without indoor witnesses.");
            }
            finally
            {
                if(body) UnityEngine.Object.Destroy(body);
                if(otherRoom) UnityEngine.Object.Destroy(otherRoom);
            }
        }
    }
}
