using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public static class StationaryNpcProbe
    {
        private sealed class LootProof
        {
            internal string PersonId,IncidentId;
            internal ulong ItemUid;
            internal bool Taken,Reentered;
        }
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            var adapter=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var streets=adapter.GetComponent<StreetPopulation>();
            game.PlayerEntity.GodMode=true; streets.ReactionsEnabled=true;
            if(game.IsPlayerInside) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            int killed=0,types=0,questLinked=0;
            var lootProof=new LootProof();
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
                    var life=adapter.EnsureStationaryLife(npc);
                    if(!life)continue;
                    var entity=npc.GetComponent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>();
                    if(entity && entity.Entity is DaggerfallWorkshop.Game.Entity.EnemyEntity && npc.GetComponent<EnemyDeath>())
                    {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIONARY_NATIVE_ENEMY_NOT_EXERCISED: reason=requires-real-combat-damage; nativeDeathOwner=true");continue;}
                    var world=(WorldState)adapter.GetSaveData();
                    var person=world.NativePeople.Find(p=>p.Id==life.Person.Id);
                    if(person==null) throw new InvalidOperationException("Unregistered static NPC: "+type+" flat="+
                        npc.Data.billboardArchiveIndex+"/"+npc.Data.billboardRecordIndex+" faction="+npc.Data.factionID);
                    var inventory=world.NpcInventories.Find(value=>value.PersonId==person.Id);
                    if(inventory==null)throw new InvalidOperationException("Static NPC lacks its actual persistent inventory.");
                    var inventoryOwner=npc.GetComponent<NativeNpcInventoryOwner>();
                    if(!inventoryOwner || inventoryOwner.Binding==null || inventoryOwner.Binding.PersonId!=person.Id ||
                        !ReferenceEquals(inventoryOwner.Binding.Items,entity.Entity.Items))
                        throw new InvalidOperationException("Static NPC native entity does not hold its exact owned inventory.");
                    bool quest=npc.GetComponent<QuestResourceBehaviour>() || QuestMachine.Instance.IsIndividualNPC(npc.Data.factionID);
                    game.WeaponManager.WeaponDamage(null,false,false,npc.transform,npc.transform.position,Vector3.forward);
                    world=(WorldState)adapter.GetSaveData();
                    if(world.NativePeople.Find(p=>p.Id==person.Id).IsAlive || npc.gameObject.activeSelf ||
                        !world.AlarmIncidents.Exists(a=>a.VictimId==person.Id && a.Corpse!=null))
                        throw new InvalidOperationException("Static NPC survived native hit or lost corpse: "+type);
                    var retained=world.NpcInventories.Find(value=>value.PersonId==person.Id);
                    if(retained==null || retained.WalletGold!=inventory.WalletGold || retained.Items.Count!=inventory.Items.Count ||
                        inventory.Items.Exists(item=>!retained.Items.Exists(value=>value.Uid==item.Uid && value.NativePayload==item.NativePayload)))
                        throw new InvalidOperationException("Static death duplicated or lost its exact actual inventory.");
                    if(!lootProof.Taken)
                    {
                        lootProof.PersonId=person.Id;
                        lootProof.IncidentId=world.AlarmIncidents.Find(a=>a.VictimId==person.Id && a.Corpse!=null).Id;
                        yield return OpenActualCorpse(lootProof);
                        var window=DaggerfallUI.Instance.InventoryWindow;var loot=window.LootTarget;
                        DaggerfallUnityItem item=null;
                        for(int i=0;i<loot.Items.Count;i++)
                        {
                            var candidate=loot.Items.GetItem(i);
                            if(retained.Items.Exists(value=>value.Uid==candidate.UID) && candidate.stackCount==1 &&
                                !candidate.IsQuestItem && !candidate.IsSummoned && candidate.ItemGroup!=ItemGroups.Currency &&
                                candidate.ItemGroup!=ItemGroups.Transportation &&
                                !candidate.IsOfTemplate(ItemGroups.MiscItems,(int)MiscItems.Map))
                            {item=candidate;break;}
                        }
                        if(item==null)throw new InvalidOperationException("Actual stationary corpse lacks an existing transferable single item.");
                        lootProof.ItemUid=item.UID;
                        var modes=typeof(DaggerfallInventoryWindow).GetNestedType("ActionModes",BindingFlags.NonPublic);
                        typeof(DaggerfallInventoryWindow).GetMethod("RemoteItemListScroller_OnItemClick",BindingFlags.Instance|BindingFlags.NonPublic,
                            null,new[]{typeof(DaggerfallUnityItem),modes},null).Invoke(window,new[]{(object)item,Enum.Parse(modes,"Remove")});
                        if(!game.PlayerEntity.Items.Contains(item) || loot.Items.Contains(item))
                            throw new InvalidOperationException("Native stationary corpse pickup did not transfer the exact existing item.");
                        window.CloseWindow();DaggerfallUI.Instance.PopToHUD();
                        yield return new WaitForSecondsRealtime(.75f);
                        VerifyLootOwnership(adapter,lootProof);lootProof.Taken=true;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIONARY_LOOT_PASS: person="+person.Id+"; uid="+item.UID+
                            "; nativeInventoryRemove=true; existingOwnedItem=true; playerOwns=true; corpseOwnerReleased=true");
                    }
                    if(quest)questLinked++;
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
                if(lootProof.Taken && !lootProof.Reentered && ids.Contains(lootProof.PersonId))
                {
                    yield return OpenActualCorpse(lootProof);
                    if(DaggerfallUI.Instance.InventoryWindow.LootTarget.Items.Contains(lootProof.ItemUid))
                        throw new InvalidOperationException("Looted stationary item returned to its actual corpse after re-entry.");
                    DaggerfallUI.Instance.InventoryWindow.CloseWindow();DaggerfallUI.Instance.PopToHUD();
                    yield return new WaitForSecondsRealtime(.75f);
                    VerifyLootOwnership(adapter,lootProof);lootProof.Reentered=true;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIONARY_LOOT_REENTRY_PASS: person="+lootProof.PersonId+
                        "; uid="+lootProof.ItemUid+"; playerOwnsOnce=true; corpseItemAbsent=true; npcOwnershipAbsent=true");
                }
                if(ids.Count>0) types++;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIONARY_TYPE_PASS: "+type+"; killed="+ids.Count+"; re-entry retains death.");
                game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(1);
            }
            if(killed<3 || types<3) throw new InvalidOperationException("Stationary NPC test did not cover three building categories.");
            if(!lootProof.Taken || !lootProof.Reentered)throw new InvalidOperationException("Stationary NPC test did not complete one actual corpse pickup and re-entry.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIONARY_PASS: native hits and persistent deaths; people="+killed+"; buildingTypes="+types+"; exactInventories=true; actualLootAndReentry=true; questLinked="+questLinked);
            if(questLinked==0)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIONARY_QUEST_NOT_EXERCISED: reason=no-actual-quest-linked-person-in-fixture");
        }
        private static IEnumerator OpenActualCorpse(LootProof proof)
        {
            var game=GameManager.Instance;float deadline=Time.realtimeSinceStartup+8;
            while(Time.realtimeSinceStartup<deadline)
            {
                foreach(var action in UnityEngine.Object.FindObjectsOfType<CorpseInteraction>())
                    if(action.IncidentId==proof.IncidentId)
                    {
                        game.PlayerMotor.transform.position=action.transform.position+Vector3.up;
                        action.Activate(new RaycastHit{distance=1});break;
                    }
                if(DaggerfallUI.UIManager.TopWindow!=DaggerfallUI.Instance.InventoryWindow)
                    foreach(var corpse in UnityEngine.Object.FindObjectsOfType<NativeStationaryCorpse>())
                    {
                        var binding=(NativeNpcInventoryBinding)typeof(NativeStationaryCorpse).GetField("binding",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(corpse);
                        if(binding==null || binding.PersonId!=proof.PersonId)continue;
                        game.PlayerMotor.transform.position=corpse.transform.position+Vector3.up;
                        corpse.Activate(new RaycastHit{distance=1});break;
                    }
                yield return null;
                if(DaggerfallUI.UIManager.TopWindow==DaggerfallUI.Instance.InventoryWindow && DaggerfallUI.Instance.InventoryWindow.LootTarget)yield break;
            }
            throw new InvalidOperationException("Actual stationary corpse did not expose its native loot window: "+proof.PersonId);
        }
        private static void VerifyLootOwnership(PeopleOfTheIliacBayMod adapter,LootProof proof)
        {
            var world=(WorldState)adapter.GetSaveData();var player=GameManager.Instance.PlayerEntity;int playerCopies=0;
            for(int i=0;i<player.Items.Count;i++)if(player.Items.GetItem(i).UID==proof.ItemUid)playerCopies++;
            if(playerCopies!=1 || world.NpcInventories.Exists(owner=>owner.Items.Exists(item=>item.Uid==proof.ItemUid)))
                throw new InvalidOperationException("Stationary loot UID does not have exactly one player owner and no NPC owner: "+proof.ItemUid);
        }
    }
}
