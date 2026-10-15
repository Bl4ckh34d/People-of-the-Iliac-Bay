using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class DefenseLedgerProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance; var mod=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mod);
            var guards=mod.GetComponent<GuardPopulation>(); bool defense=guards.EnemyDefense; guards.EnemyDefense=false;
            GameObject body=null;
            string savedVictim=null; ulong savedCorpse=0,savedItem=0;
            try
            {
                game.PlayerEntity.GodMode=true;
                VerifyObserverIsolation(mod.GetComponent<DefenseCombatAdapter>());
                VerifyInvisibleWitnesses(mod.GetComponent<DefenseCombatAdapter>(),store);
                yield return VerifyDisintegrate(store);
                yield return VerifyActivatedAttacker(store);
                foreach(int scenario in new[] { 2,0,1 })
                {
                    bool hostile=scenario==0;
                    var room=game.PlayerEnterExit.Interior; var geometry=new InteriorGeometry(room);
                    if(geometry.LocalAnchors.Count==0) throw new InvalidOperationException("Defense probe has no indoor floor.");
                    body=GameObjectHelper.CreateFoeGameObjects(room.transform.TransformPoint(geometry.LocalAnchors[0])+Vector3.up,
                        MobileTypes.Thief,1,MobileReactions.Passive)[0];
                    body.SetActive(true); yield return null;
                    var target=body.GetComponent<DaggerfallEntityBehaviour>(); var motor=body.GetComponent<EnemyMotor>();
                    motor.IsHostile=hostile; motor.enabled=false; body.GetComponent<EnemySenses>().enabled=false; body.GetComponent<EnemyAttack>().enabled=false;
                    if(scenario==2) target.Entity.Team=MobileTeams.PlayerAlly;
                    target.Entity.MaxHealth=500; target.Entity.SetHealth(500,true);
                    var possession=ItemBuilder.CreateWeapon(Weapons.Dagger,WeaponMaterialTypes.Iron);
                    if(hostile)
                    {
                        target.Entity.Items.AddItem(possession);
                        var retained=ItemBuilder.CreateWeapon(Weapons.Dagger,WeaponMaterialTypes.Steel);
                        target.Entity.Items.AddItem(retained); savedItem=retained.UID;
                    }
                    string id="native:"+body.GetComponent<DaggerfallEnemy>().LoadID;
                    game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Theft;
                    target.DamageHealthFromSource(game.PlayerEntityBehaviour,5,false,Vector3.zero);
                    var record=store.GetDefenseCombatant(id);
                    if(record==null || record.PlayerProvoked==hostile || record.FirstPlayerHitAt<0)
                        throw new InvalidOperationException("Native player damage did not record original hostility/provocation.");
                    target.Entity.SetHealth(1,true);
                    var weapon=ItemBuilder.CreateWeapon(Weapons.Longsword,WeaponMaterialTypes.Daedric);
                    for(int attempt=0;attempt<100 && target.Entity.CurrentHealth>0;attempt++)
                        game.WeaponManager.WeaponDamage(weapon,false,false,target.transform,target.transform.position,Vector3.forward);
                    if(target.Entity.CurrentHealth>0) throw new InvalidOperationException("Weapon probe could not land a lethal native hit; scenario="+scenario+
                        "; type="+target.EntityType+"; skill="+game.PlayerEntity.Skills.GetLiveSkillValue(DaggerfallConnect.DFCareer.Skills.HandToHand));
                    float deadline=Time.realtimeSinceStartup+5;
                    while((record.Death==null || record.Death.CorpseId==null) && Time.realtimeSinceStartup<deadline) yield return null;
                    if(record.Death==null || record.Death.CorpseId==null || record.Death.LootEntitled!=hostile ||
                        record.Death.CorpseId!="loot:"+target.CorpseLootContainer.LoadID || !record.Death.KilledByPlayer)
                        throw new InvalidOperationException("Native death/corpse lost lawful-defense classification.");
                    if(game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Theft)
                        throw new InvalidOperationException("Defense ledger changed existing player crime.");
                    if(hostile)
                    {
                        savedVictim=id; savedCorpse=target.CorpseLootContainer.LoadID;
                        yield return VerifyImmediateLoot(target.CorpseLootContainer,possession);
                    }
                    var saved=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
                    var restored=new WorldStateStore(0); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),saved));
                    if(restored.GetDefenseCombatant(id).Death.LootEntitled!=hostile)
                        throw new InvalidOperationException("Native defense entitlement did not roundtrip.");
                    UnityEngine.Object.Destroy(body); body=null;
                }
                yield return VerifySavedLoot(mod,savedVictim,savedCorpse,savedItem);
                Debug.Log("LIVING_CITIES_NATIVE_DEFENSE_LEDGER_PASS: native sourced damage and lethal weapons, hostile/passive/ally provenance, real corpse binding, prior crime and native JSON.");
            }
            finally { if(body) UnityEngine.Object.Destroy(body); guards.EnemyDefense=defense; game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None; }
        }
        private static void VerifyObserverIsolation(DefenseCombatAdapter adapter)
        {
            var field=typeof(DefenseCombatAdapter).GetField("available",BindingFlags.Instance|BindingFlags.NonPublic);
            var original=field.GetValue(adapter);
            try
            {
                field.SetValue(adapter,new Func<bool>(()=> { throw new InvalidOperationException("expected observer fault"); }));
                typeof(DefenseCombatAdapter).GetMethod("EnemyDied",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(adapter,new object[] { null,EventArgs.Empty });
            }
            finally { field.SetValue(adapter,original); }
            Debug.Log("LIVING_CITIES_DEFENSE_OBSERVER_ISOLATION_PASS: failed death observer does not escape native event.");
        }
        private static IEnumerator VerifyImmediateLoot(DaggerfallLoot loot,DaggerfallUnityItem item)
        {
            var game=GameManager.Instance; var priorCrime=game.PlayerEntity.CrimeCommitted;
            if(!loot.Items.Contains(item)) throw new InvalidOperationException("Native corpse did not inherit victim inventory.");
            try
            {
                var inventory=new DaggerfallInventoryWindow(DaggerfallUI.UIManager) { LootTarget=loot };
                DaggerfallUI.UIManager.PushWindow(inventory); yield return null;
                var modes=typeof(DaggerfallInventoryWindow).GetNestedType("ActionModes",BindingFlags.NonPublic);
                typeof(DaggerfallInventoryWindow).GetMethod("RemoteItemListScroller_OnItemClick",BindingFlags.Instance|BindingFlags.NonPublic,
                    null,new[] { typeof(DaggerfallUnityItem),modes },null).Invoke(inventory,new[] { (object)item,Enum.Parse(modes,"Remove") });
                inventory.CloseWindow();
                if(!game.PlayerEntity.Items.Contains(item) || loot.Items.Contains(item) || game.PlayerEntity.CrimeCommitted!=priorCrime)
                    throw new InvalidOperationException("Immediate native corpse loot failed or changed existing crime.");
            }
            finally { DaggerfallUI.Instance.PopToHUD(); game.PlayerEntity.Items.RemoveItem(item); }
            Debug.Log("LIVING_CITIES_DEFENSE_LOOT_PASS: victim inventory reaches actual corpse and native inventory pickup before guards arrive preserves prior crime.");
        }
        private static IEnumerator VerifySavedLoot(LivingCitiesMod mod,string victim,ulong corpseId,ulong itemId)
        {
            var game=GameManager.Instance;
            if(!game.PlayerEntity.Name.StartsWith("LivingCitiesTest-",StringComparison.Ordinal))
                throw new InvalidOperationException("Defense save probe requires dedicated test character.");
            const string name="Living Cities - lawful defense loot";
            bool saved=false,loaded=false;
            SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnLoadEventHandler onLoad=ignored=>loaded=true;
            SaveLoadManager.OnSave+=onSave; SaveLoadManager.OnLoad+=onLoad;
            try
            {
                game.SaveLoadManager.Save(game.PlayerEntity.Name,name);
                float deadline=Time.realtimeSinceStartup+30;
                while(!saved && Time.realtimeSinceStartup<deadline) yield return null;
                if(!saved) throw new InvalidOperationException("Defense loot save timed out.");
                yield return null;
                int key=game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name,name);
                if(key<0) throw new InvalidOperationException("Defense loot slot missing.");
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline=Time.realtimeSinceStartup+60;
                while((!loaded || game.SaveLoadManager.LoadInProgress) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || game.SaveLoadManager.LoadInProgress) throw new InvalidOperationException("Defense loot reload timed out.");
                yield return new WaitForSecondsRealtime(2);
                var store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mod);
                var record=store.GetDefenseCombatant(victim);
                var corpse=Array.Find(UnityEngine.Object.FindObjectsOfType<DaggerfallLoot>(),loot=>loot.LoadID==corpseId);
                if(record==null || record.Death==null || !record.Death.LootEntitled || record.Death.CorpseId!="loot:"+corpseId || !corpse)
                    throw new InvalidOperationException("Actual slot reload lost defense rights or native corpse.");
                var item=corpse.Items.GetItem(itemId);
                if(item==null || game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Theft)
                    throw new InvalidOperationException("Actual slot reload lost corpse inventory or prior crime.");
                yield return VerifyImmediateLoot(corpse,item);
                Debug.Log("LIVING_CITIES_DEFENSE_SAVE_LOOT_PASS: actual game slot reload retains bound corpse, entitlement, inventory and prior crime; native pickup succeeds.");
            }
            finally { SaveLoadManager.OnSave-=onSave; SaveLoadManager.OnLoad-=onLoad; }
        }
        private static IEnumerator VerifyDisintegrate(WorldStateStore store)
        {
            var game=GameManager.Instance; GameObject body=null;
            try
            {
                foreach(int scenario in new[] { 0,1,2 })
                {
                    var room=game.PlayerEnterExit.Interior; var geometry=new InteriorGeometry(room);
                    body=GameObjectHelper.CreateFoeGameObjects(room.transform.TransformPoint(geometry.LocalAnchors[0])+Vector3.up,
                        MobileTypes.Thief,1,MobileReactions.Passive)[0];
                    body.SetActive(true); yield return null;
                    var target=body.GetComponent<DaggerfallEntityBehaviour>();
                    var motor=body.GetComponent<EnemyMotor>(); motor.IsHostile=scenario!=1; motor.enabled=false;
                    body.GetComponent<EnemySenses>().enabled=false; body.GetComponent<EnemyAttack>().enabled=false;
                    string id="native:"+body.GetComponent<DaggerfallEnemy>().LoadID;
                    var spell=new Disintegrate();
                    spell.Start(body.GetComponent<EntityEffectManager>(),scenario==2 ? null : game.PlayerEntityBehaviour);
                    spell.MagicRound();
                    float deadline=Time.realtimeSinceStartup+5;
                    DefenseCombatant record;
                    while(((record=store.GetDefenseCombatant(id))==null || record.Death==null || record.Death.CorpseId==null) &&
                        Time.realtimeSinceStartup<deadline) yield return null;
                    record=store.GetDefenseCombatant(id);
                    if(record==null || record.Death==null || record.Death.CorpseId==null ||
                        record.Death.KilledByPlayer!=(scenario!=2) || record.Death.LootEntitled!=(scenario==0))
                        throw new InvalidOperationException("Disintegrate lost source or corpse entitlement; scenario="+scenario);
                    UnityEngine.Object.Destroy(body); body=null;
                }
            }
            finally { if(body) UnityEngine.Object.Destroy(body); }
            Debug.Log("LIVING_CITIES_DEFENSE_DISINTEGRATE_PASS: hostile/passive player casts and unsourced death retain distinct attribution.");
        }
        private static IEnumerator VerifyActivatedAttacker(WorldStateStore store)
        {
            var game=GameManager.Instance; GameObject body=null;
            try
            {
                foreach(int scenario in new[] { 3,0,1,2 })
                {
                    var room=game.PlayerEnterExit.Interior; var geometry=new InteriorGeometry(room);
                    body=GameObjectHelper.CreateFoeGameObjects(room.transform.TransformPoint(geometry.LocalAnchors[0])+Vector3.up,
                        MobileTypes.Thief,1,MobileReactions.Passive)[0];
                    body.SetActive(true); yield return null;
                    var target=body.GetComponent<DaggerfallEntityBehaviour>(); var motor=body.GetComponent<EnemyMotor>();
                    motor.IsHostile=false; motor.enabled=false; body.GetComponent<EnemySenses>().enabled=false;
                    var attack=body.GetComponent<EnemyAttack>(); attack.enabled=false;
                    target.Entity.MaxHealth=500; target.Entity.SetHealth(500,true);
                    if(scenario==3) target.Entity.Team=MobileTeams.PlayerAlly;
                    string id="native:"+body.GetComponent<DaggerfallEnemy>().LoadID;
                    target.DamageHealthFromSource(scenario==2 ? game.PlayerEntityBehaviour : null,1,false,Vector3.zero);
                    motor.IsHostile=true;
                    if(scenario==1) game.PlayerEntityBehaviour.DamageHealthFromSource(target,1,false,Vector3.zero);
                    else
                    {
                        typeof(EnemyAttack).GetField("damage",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(attack,1);
                        typeof(EnemyAttack).GetMethod("SendDamageToPlayer",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(attack,null);
                    }
                    var record=store.GetDefenseCombatant(id);
                    if(record==null || record.Origin!=CombatOrigin.PassiveEnemy || (record.UnprovokedAttackAt>=0)!=(scenario!=2))
                        throw new InvalidOperationException("Activated attacker lost native attack evidence or pardoned provocation; scenario="+scenario);
                    target.DamageHealthFromSource(game.PlayerEntityBehaviour,1000,false,Vector3.zero);
                    float deadline=Time.realtimeSinceStartup+5;
                    while((record.Death==null || record.Death.CorpseId==null) && Time.realtimeSinceStartup<deadline) yield return null;
                    if(record.Death==null || record.Death.CorpseId==null || record.Death.LootEntitled!=(scenario!=2))
                        throw new InvalidOperationException("Activated attacker death lost provenance; scenario="+scenario);
                    UnityEngine.Object.Destroy(body); body=null;
                }
            }
            finally { if(body) UnityEngine.Object.Destroy(body); }
            Debug.Log("LIVING_CITIES_DEFENSE_ACTIVATION_PASS: native melee and sourced attacks establish aggression; player-provoked retaliation stays unlawful.");
        }
        private static void VerifyInvisibleWitnesses(DefenseCombatAdapter adapter,WorldStateStore store)
        {
            var game=GameManager.Instance; int town=game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            var people=store.NativePeopleFor(town);
            var npc=Array.Find(UnityEngine.Object.FindObjectsOfType<StaticNPC>(),n=>people.Exists(p=>p.NativeHash==n.Data.hash && p.IsAlive));
            if(!npc) throw new InvalidOperationException("Witness fixture has no known native resident.");
            var method=typeof(DefenseCombatAdapter).GetMethod("Witnesses",BindingFlags.Instance|BindingFlags.NonPublic);
            var oldPosition=game.PlayerMotor.transform.position; var oldFlags=game.PlayerEntity.MagicalConcealmentFlags;
            try
            {
                game.PlayerMotor.transform.position=npc.transform.position;
                game.PlayerEntity.MagicalConcealmentFlags=MagicalConcealmentFlags.None;
                var visible=(System.Collections.ICollection)method.Invoke(adapter,new object[] { store,town,npc.transform.position });
                if(visible.Count==0) throw new InvalidOperationException("Visible witness fixture did not observe player.");
                game.PlayerEntity.MagicalConcealmentFlags=MagicalConcealmentFlags.InvisibleTrue;
                var hidden=(System.Collections.ICollection)method.Invoke(adapter,new object[] { store,town,npc.transform.position });
                if(hidden.Count!=0) throw new InvalidOperationException("Invisible player incorrectly gained fight witnesses.");
            }
            finally { game.PlayerMotor.transform.position=oldPosition; game.PlayerEntity.MagicalConcealmentFlags=oldFlags; }
            Debug.Log("LIVING_CITIES_DEFENSE_INVISIBLE_WITNESS_PASS: visible control observed; invisible player not identified.");
        }
    }
}
