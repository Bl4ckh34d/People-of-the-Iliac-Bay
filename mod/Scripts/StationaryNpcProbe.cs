using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public static class StationaryNpcProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            var adapter=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var streets=adapter.GetComponent<StreetPopulation>();
            game.PlayerEntity.GodMode=true; streets.ReactionsEnabled=true;
            if(game.IsPlayerInside) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            int killed=0,types=0;
            foreach(var type in new[]{DFLocation.BuildingTypes.Tavern,DFLocation.BuildingTypes.GuildHall,
                DFLocation.BuildingTypes.Temple,DFLocation.BuildingTypes.Bank,DFLocation.BuildingTypes.GeneralStore})
            {
                var buildings=directory.GetBuildingsOfType(type);
                if(buildings.Count==0) continue;
                int key=buildings[0].buildingKey;
                DaggerfallStaticDoors owner=null; StaticDoor entry=default(StaticDoor);
                foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach(var door in doors.Doors)
                        if(door.doorType==DoorTypes.Building && door.buildingKey==key) {owner=doors;entry=door;break;}
                if(!owner) throw new InvalidOperationException("Stationary NPC test entrance missing.");
                TownSmokeHarness.EnterInterior(owner.transform,entry);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                var room=game.PlayerEnterExit.Interior;
                var ids=new List<string>();
                foreach(var npc in room.GetComponentsInChildren<StaticNPC>())
                {
                    if(npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>()) continue;
                    FactionFile.FactionData faction;
                    if(game.PlayerEntity.FactionData.GetFactionData(npc.Data.factionID,out faction) &&
                        faction.type==(int)FactionFile.FactionTypes.Individual) continue;
                    var world=(WorldState)adapter.GetSaveData();
                    var person=world.NativePeople.Find(p=>p.WorkKey==key && p.NativeHash==npc.Data.hash && p.SettlementId==directory.MapID);
                    if(person==null) throw new InvalidOperationException("Unregistered ordinary static NPC: "+type+" flat="+
                        npc.Data.billboardArchiveIndex+"/"+npc.Data.billboardRecordIndex+" faction="+npc.Data.factionID);
                    game.WeaponManager.WeaponDamage(null,false,false,npc.transform,npc.transform.position,Vector3.forward);
                    world=(WorldState)adapter.GetSaveData();
                    if(world.NativePeople.Find(p=>p.Id==person.Id).IsAlive || npc.gameObject.activeSelf ||
                        !world.AlarmIncidents.Exists(a=>a.VictimId==person.Id && a.Corpse!=null))
                        throw new InvalidOperationException("Ordinary static NPC survived native hit or lost corpse: "+type);
                    ids.Add(person.Id); killed++;
                }
                game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(1);
                TownSmokeHarness.EnterInterior(owner.transform,entry); yield return new WaitForSecondsRealtime(1);
                var restored=(WorldState)adapter.GetSaveData();
                foreach(var npc in game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>(true))
                {
                    var person=restored.NativePeople.Find(p=>p.WorkKey==key && p.NativeHash==npc.Data.hash && ids.Contains(p.Id));
                    if(person!=null && (person.IsAlive || npc.gameObject.activeSelf))
                        throw new InvalidOperationException("Ordinary static NPC revived on interior re-entry: "+type);
                }
                if(ids.Count>0) types++;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIONARY_TYPE_PASS: "+type+"; killed="+ids.Count+"; re-entry retains death.");
                game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(1);
            }
            if(killed<3 || types<3) throw new InvalidOperationException("Stationary NPC test did not cover three building categories.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIONARY_PASS: native hits and persistent deaths; people="+killed+"; buildingTypes="+types);
        }
    }
}
