using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Records native combat facts only. Crime policy, inventory and quest death logic remain native.
    public sealed class DefenseCombatAdapter : MonoBehaviour
    {
        private Func<WorldStateStore> getStore;
        private Func<bool> available;
        private IDisposable hooks;
        private float nextPresenceCheck;
        public void Install(Func<WorldStateStore> store,Func<bool> enabled)
        {
            getStore=store; available=enabled;
            var type=Type.GetType("LivingCities.QuestHooks.NativeDefenseHooks, LivingCities.QuestHooks",true);
            hooks=(IDisposable)Activator.CreateInstance(type,new object[] {
                new Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,Action<Exception>>(BeginDamage) });
            EnemyDeath.OnEnemyDeath+=EnemyDied;
            PlayerEnterExit.OnPreTransition+=LeaveDefenseScene;
        }
        private bool CanObservePresence()
        {
            var game=GameManager.Instance;
            return getStore!=null && game && game.IsReady && game.IsPlayingGame() && !game.SaveLoadManager.LoadInProgress;
        }
        private void LeaveDefenseScene(PlayerEnterExit.TransitionEventArgs args)
        {
            if(CanObservePresence()) getStore().ObserveDefensePresence(null,-1,0,0,0,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),40);
        }
        private void Update()
        {
            if(Time.unscaledTime<nextPresenceCheck || !CanObservePresence()) return;
            nextPresenceCheck=Time.unscaledTime+0.25f;
            var game=GameManager.Instance;
            var room=game.PlayerEnterExit.IsPlayerInsideBuilding ? game.PlayerEnterExit.Interior : null;
            Component owner=room ? (Component)room : game.IsPlayerInside ? null : game.StreamingWorld.GetCurrentBuildingDirectory();
            var scene=DefenseScene.For(owner);
            var point=scene==null ? Vector3.zero : scene.Root.InverseTransformPoint(game.PlayerMotor.transform.position-Vector3.up);
            getStore().ObserveDefensePresence(scene==null ? (int?)null : scene.SettlementId,
                scene==null ? -1 : scene.BuildingKey,point.x,point.y,point.z,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),40);
        }
        private static string Identity(DaggerfallEntityBehaviour target)
        {
            if(!target || !(target.Entity is EnemyEntity) || target.GetComponent<GuardActor>() || target.GetComponent<ResidentActor>() || target.GetComponent<StaticNPC>()) return null;
            var enemy=(EnemyEntity)target.Entity; var native=target.GetComponent<DaggerfallEnemy>();
            if(enemy.MobileEnemy.ID==(int)MobileTypes.Knight_CityWatch || !native || native.LoadID==0) return null;
            return "native:"+native.LoadID.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        private Action<Exception> BeginDamage(DaggerfallEntityBehaviour target,DaggerfallEntityBehaviour source)
        {
            if(available==null || !available() || !target || target.Entity.CurrentHealth<=0) return null;
            if(target==GameManager.Instance.PlayerEntityBehaviour) return BeginAttackOnPlayer(source);
            string id=Identity(target); if(id==null) return null;
            var scene=DefenseScene.For(target); if(scene==null) return null;
            var game=GameManager.Instance; var store=getStore(); int town=scene.SettlementId;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var existing=store.GetDefenseCombatant(id);
            // Cross-settlement encounters need a separate death-location contract.
            // Retain earlier provenance without interrupting native combat or inventing a new identity.
            if(existing!=null && existing.SettlementId!=town) return null;
            var motor=target.GetComponent<EnemyMotor>();
            // A scripted former ally can attack while retaining its team flag.
            // Require recorded aggression before treating that ally as an attacker.
            bool hostile=motor && motor.IsHostile && (target.Entity.Team!=MobileTeams.PlayerAlly ||
                existing!=null && existing.UnprovokedAttackAt>=0 && !existing.PlayerProvoked);
            var record=store.RegisterDefenseCombatant(id,town,hostile ? CombatOrigin.HostileAttacker : CombatOrigin.PassiveEnemy,now);
            if(record.Death!=null) return null;
            bool player=source==game.PlayerEntityBehaviour;
            var witnesses=player && scene.PlayerPresent ? Witnesses(store,town,target.transform.position) : new List<DefenseWitness>();
            return error=> {
                if(error!=null || !target || getStore()!=store || record.Death!=null) return;
                if(player)
                {
                    store.RecordDefensePlayerHit(id,now,hostile);
                    foreach(var witness in witnesses) store.RecordDefenseWitness(id,witness.ObserverId,witness.Guard,now);
                }
                if(target.Entity.CurrentHealth<=0) ConfirmDeath(store,record,target,player);
            };
        }
        private Action<Exception> BeginAttackOnPlayer(DaggerfallEntityBehaviour source)
        {
            string id=Identity(source); if(id==null || source.Entity.CurrentHealth<=0) return null;
            var motor=source.GetComponent<EnemyMotor>(); if(!motor || !motor.IsHostile) return null;
            var scene=DefenseScene.For(source); if(scene==null || !scene.PlayerPresent) return null;
            var store=getStore(); int town=scene.SettlementId;
            var existing=store.GetDefenseCombatant(id);
            if(existing!=null && (existing.SettlementId!=town || existing.Death!=null)) return null;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            // A native hit is evidence of aggression; merely becoming hostile is not.
            // The store refuses to pardon previously recorded player provocation.
            store.RegisterDefenseCombatant(id,town,CombatOrigin.HostileAttacker,now);
            return error=> {
                if(error==null && getStore()==store && store.GetDefenseCombatant(id).Death==null)
                    store.RecordDefenseUnprovokedAttack(id,now);
            };
        }
        private List<DefenseWitness> Witnesses(WorldStateStore store,int town,Vector3 target)
        {
            var result=new List<DefenseWitness>(); var ids=new HashSet<string>();
            if(GameManager.Instance.PlayerEntity.IsInvisible) return result;
            Action<string,bool,Component> add=(id,guard,observer)=> {
                var scene=DefenseScene.For(observer);
                if(scene==null || scene.SettlementId!=town || !scene.PlayerPresent) return;
                var position=observer.transform.position;
                if(ids.Contains(id) || !CivilianResponses.Sees(position,target,20) ||
                    !CivilianResponses.Sees(position,GameManager.Instance.PlayerMotor.transform.position,20)) return;
                ids.Add(id); result.Add(new DefenseWitness { ObserverId=id,Guard=guard });
            };
            var guards=GetComponent<GuardPopulation>();
            var streets=GetComponent<StreetPopulation>();
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                if(guards.Roster!=null && guards.Roster.Guards.Exists(g=>g.Id==actor.GuardId && g.IsAlive)) add(actor.GuardId,true,actor);
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(actor.Resident!=null && actor.Resident.IsAlive && !actor.IsSleeping && streets.IsLivingResident(actor.Resident.Id)) add(actor.Resident.Id,false,actor);
            var people=store.NativePeopleFor(town);
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<StaticNPC>())
            {
                var pose=npc.GetComponent<SleepingPose>(); if(pose && pose.Active) continue;
                var person=people.Find(p=>p.NativeHash==npc.Data.hash && p.WorkKey==npc.Data.buildingKey && p.IsAlive);
                if(person!=null) add(person.Id,false,npc);
            }
            return result;
        }
        private static void ConfirmDeath(WorldStateStore store,DefenseCombatant record,DaggerfallEntityBehaviour target,bool player)
        {
            var scene=DefenseScene.For(target);
            if(scene==null || scene.SettlementId!=record.SettlementId) return;
            Vector3 point=scene.Root.InverseTransformPoint(target.transform.position-Vector3.up);
            store.ConfirmDefenseDeath(record.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),scene.BuildingKey,
                point.x,point.y,point.z,player,300,20);
        }
        private void EnemyDied(object sender,EventArgs args)
        {
            try { ObserveDeath(sender); }
            catch(Exception error) { Debug.LogWarning("LIVING_CITIES_DEFENSE_OBSERVATION_FAILED: "+error.Message); }
        }
        private void ObserveDeath(object sender)
        {
            if(available==null || !available()) return;
            var death=sender as EnemyDeath; if(!death) return;
            var target=death.GetComponent<DaggerfallEntityBehaviour>(); string id=Identity(target); if(id==null) return;
            var store=getStore(); var record=store.GetDefenseCombatant(id); if(record==null) return;
            var scene=DefenseScene.For(target);
            if(scene==null || record.SettlementId!=scene.SettlementId) return;
            if(record.Death==null) ConfirmDeath(store,record,target,false);
            var loot=target.CorpseLootContainer;
            if(loot && scene.Interior && loot.WorldContext==WorldContext.Interior && loot.transform.parent!=scene.Root)
                loot.transform.SetParent(scene.Root,true);
            if(loot && loot.LoadID!=0) store.BindDefenseCorpse(id,"loot:"+loot.LoadID.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        private void OnDestroy()
        {
            EnemyDeath.OnEnemyDeath-=EnemyDied;
            PlayerEnterExit.OnPreTransition-=LeaveDefenseScene;
            if(hooks!=null) hooks.Dispose();
        }
    }
}
