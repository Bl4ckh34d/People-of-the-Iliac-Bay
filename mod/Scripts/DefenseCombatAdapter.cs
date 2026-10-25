using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Binds native combat to resident bodies and records its facts. Crime policy remains native.
    public sealed class DefenseCombatAdapter : MonoBehaviour
    {
        private Func<WorldStateStore> getStore;
        private Func<bool> available;
        private IDisposable hooks;
        private float nextPresenceCheck;
        public void Install(Func<WorldStateStore> store,Func<bool> enabled)
        {
            getStore=store; available=enabled;
            var type=Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeDefenseHooks, PeopleOfTheIliacBay.QuestHooks",true);
            hooks=(IDisposable)Activator.CreateInstance(type,new object[] {
                new Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,Action<Exception>>(BeginDamage),
                new Func<EnemyAttack,DaggerfallUnityItem,Vector3,bool,int?>(AttackCivilian),
                new Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,Action<Exception>>(BeginHealing),
                new Func<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour,string,Action<Exception>>(BeginHostileMagic) });
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
            if(!target || target.GetComponent<GuardActor>() || target.GetComponent<StaticNPC>()) return null;
            var resident=target.GetComponent<ResidentActor>();
            if(resident!=null && resident.Resident!=null && resident.Resident.IsAlive && target.Entity is CivilianEntity)
                return resident.Resident.Id;
            if(!(target.Entity is EnemyEntity)) return null;
            var enemy=(EnemyEntity)target.Entity; var native=target.GetComponent<DaggerfallEnemy>();
            if(enemy.MobileEnemy.ID==(int)MobileTypes.Knight_CityWatch || !native || native.LoadID==0) return null;
            return "native:"+native.LoadID.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        private int? AttackCivilian(EnemyAttack attack,DaggerfallUnityItem weapon,Vector3 direction,bool bowAttack)
        {
            var senses=attack.GetComponent<EnemySenses>();var target=senses?senses.Target:null;
            var resident=target?target.GetComponent<ResidentActor>():null;
            if(!resident || !(target.Entity is CivilianEntity))return null;
            var source=attack.GetComponent<DaggerfallEntityBehaviour>();
            if(!source || !(source.Entity is EnemyEntity) || !resident.Resident.IsAlive || target.Entity.CurrentHealth<=0)return 0;
            // EnemyAttack casts all non-player targets to EnemyEntity. Use the same
            // native formula with the actual civilian instead of inventing combat stats.
            int damage=FormulaHelper.CalculateAttackDamage(source.Entity,target.Entity,false,0,weapon);
            // Match EnemyAttack.ApplyDamageToNonPlayer: a successful physical
            // hit ends normal concealment before strikes payloads and observation.
            if(source.Entity.IsMagicallyConcealedNormalPower && damage>0)
                EntityEffectManager.BreakNormalPowerConcealmentEffects(source);
            if(weapon!=null && weapon.IsEnchanted)
            {
                var effects=source.GetComponent<EntityEffectManager>();
                if(effects)damage=effects.DoItemEnchantmentPayloads(EnchantmentPayloadFlags.Strikes,weapon,
                    source.Entity.Items,target,damage);
            }
            var sounds=damage>0?target.GetComponent<EnemySounds>():source.GetComponent<EnemySounds>();
            if(damage>0)
            {
                if(sounds)sounds.PlayHitSound(weapon);
                target.DamageHealthFromSource(source,damage,true,target.transform.position);
            }
            else if(sounds)sounds.PlayMissSound(weapon);
            return damage;
        }
        private Action<Exception> BeginHealing(DaggerfallEntityBehaviour target,DaggerfallEntityBehaviour source)
        {
            if(!CanObservePresence() || !target || source!=GameManager.Instance.PlayerEntityBehaviour)return null;
            var vitals=target.GetComponent<NativeNpcVitalsBinding>();
            return vitals?vitals.BeginObservedPlayerHealing(getStore(),source):null;
        }
        private Action<Exception> BeginHostileMagic(DaggerfallEntityBehaviour target,DaggerfallEntityBehaviour source,string effectKey)
        {
            // Hook validates hostile native effect entry. Do not infer policy from its effect key.
            if(available==null||!available()||!target||target.Entity==null||target.Entity.CurrentHealth<=0||
                !source||source.Entity==null||source.Entity.CurrentHealth<=0||source.Entity.Team==MobileTeams.PlayerAlly||
                source==GameManager.Instance.PlayerEntityBehaviour)return null;
            var victim=target.GetComponent<ResidentActor>();
            bool protectedTarget=victim!=null||target.GetComponent<GuardActor>()||target.GetComponent<StaticNPC>()||
                target==GameManager.Instance.PlayerEntityBehaviour;
            if(!protectedTarget)return null;
            string id=Identity(source);if(id==null)return null;
            var scene=DefenseScene.For(source);var victimScene=DefenseScene.For(target);
            bool playerVictim=target==GameManager.Instance.PlayerEntityBehaviour;
            if(scene==null || (playerVictim?!scene.PlayerPresent:
                victimScene==null || scene.SettlementId!=victimScene.SettlementId || scene.Root!=victimScene.Root) ||
                !CivilianResponses.Sees(source.transform.position,target.transform.position,20))return null;
            var store=getStore();double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var existing=store.GetDefenseCombatant(id);
            if(existing!=null&&(existing.SettlementId!=scene.SettlementId||existing.Death!=null))return null;
            bool managed=source.GetComponent<ResidentActor>()!=null&&source.Entity is CivilianEntity;
            var record=store.RegisterDefenseCombatant(id,scene.SettlementId,managed?CombatOrigin.Civilian:CombatOrigin.HostileAttacker,now);
            return error=>{
                if(error!=null||getStore()!=store||record.Death!=null)return;
                var guards=GetComponent<GuardPopulation>();
                if(guards!=null)guards.ObserveBodyHostileMagic(target,source);
                if(!managed)store.RecordDefenseUnprovokedAttack(id,now);
            };
        }

        private Action<Exception> BeginDamage(DaggerfallEntityBehaviour target,DaggerfallEntityBehaviour source)
        {
            // A physical attack is no longer a word-against-word complaint, even if native Assault was already set.
            if(getStore!=null && target && source==GameManager.Instance.PlayerEntityBehaviour &&
                (target.GetComponent<ResidentActor>() || target.GetComponent<GuardActor>() || target.GetComponent<StaticNPC>()))
                getStore().ClearNpcVerbalCharge();
            var aggression=BeginAttackOnCivilian(target,source);
            var damage=BeginDamageCore(target,source);
            if(aggression==null)return damage;
            return error=>{aggression(error);if(damage!=null)damage(error);};
        }
        private Action<Exception> BeginAttackOnCivilian(DaggerfallEntityBehaviour target,DaggerfallEntityBehaviour source)
        {
            if(available==null||!available()||!target||target.Entity==null||target.Entity.CurrentHealth<=0||
                !source||source.Entity==null||source.Entity.Team==MobileTeams.PlayerAlly||target==GameManager.Instance.PlayerEntityBehaviour)return null;
            string id=Identity(source);if(id==null)return null;
            bool civilian=target.GetComponent<ResidentActor>()||target.GetComponent<GuardActor>()||target.GetComponent<StaticNPC>();
            var targetMotor=target.GetComponent<EnemyMotor>();
            if(!civilian && (!(target.Entity is EnemyEntity)||!targetMotor||targetMotor.IsHostile))return null;
            var scene=DefenseScene.For(source);var victimScene=DefenseScene.For(target);
            if(scene==null||victimScene==null||scene.SettlementId!=victimScene.SettlementId||scene.Root!=victimScene.Root)return null;
            var store=getStore();double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var existing=store.GetDefenseCombatant(id);
            if(existing!=null&&(existing.SettlementId!=scene.SettlementId||existing.Death!=null))return null;
            var motor=source.GetComponent<EnemyMotor>();
            var sourceResident=source.GetComponent<ResidentActor>();
            var origin=sourceResident!=null && source.Entity is CivilianEntity ? CombatOrigin.Civilian :
                motor&&motor.IsHostile ? CombatOrigin.HostileAttacker : CombatOrigin.PassiveEnemy;
            var record=store.RegisterDefenseCombatant(id,scene.SettlementId,origin,now);
            int health=target.Entity.CurrentHealth;
            return error=>{
                if(error!=null||getStore()!=store||!target||target.Entity.CurrentHealth>=health||record.Death!=null) return;
                var guards=GetComponent<GuardPopulation>();
                if(civilian&&guards!=null)guards.ObserveBodyAssault(target,source);
                if(origin!=CombatOrigin.Civilian)store.RecordDefenseUnprovokedAttack(id,now);
            };
        }
        private Action<Exception> BeginDamageCore(DaggerfallEntityBehaviour target,DaggerfallEntityBehaviour source)
        {
            if(available==null || !available() || !target || target.Entity.CurrentHealth<=0) return null;
            if(target==GameManager.Instance.PlayerEntityBehaviour) return BeginAttackOnPlayer(source);
            var attackedResident=target.GetComponent<ResidentActor>();
            // GuardActor has no hostile identity and cannot be an NPC murder
            // source. Do not create a civilian murder record which the hostile
            // registry will then reject. Lawful-force crowd response requires
            // its own non-crime contract; generic threat records force Flee.
            if(attackedResident && source && source.GetComponent<GuardActor>())
            {
                var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
                return streets?streets.BeginLawfulGuardResidentAttack(attackedResident,source):null;
            }
            if(attackedResident && source && source!=GameManager.Instance.PlayerEntityBehaviour && IsCorpseMurderSource(source))
            {
                var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
                return streets?streets.BeginEnemyResidentAttack(attackedResident,source):null;
            }
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
            var motor=source.GetComponent<EnemyMotor>(); var resident=source.GetComponent<ResidentActor>();
            bool managed=resident!=null && source.Entity is CivilianEntity;
            if(!managed && !(source.Entity is EnemyEntity)) return null;
            var scene=DefenseScene.For(source);
            // The player is not parented to the town/interior hierarchy. Its
            // presence comes from the exact loaded scene, as in guard perception.
            if(scene==null || !scene.PlayerPresent) return null;
            var store=getStore(); int town=scene.SettlementId;
            var existing=store.GetDefenseCombatant(id);
            if(existing!=null && (existing.SettlementId!=town || existing.Death!=null)) return null;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            // A native hit is evidence of aggression; merely becoming hostile is not.
            // The store refuses to pardon previously recorded player provocation.
            var record=store.RegisterDefenseCombatant(id,town,managed?CombatOrigin.Civilian:CombatOrigin.HostileAttacker,now);
            int health=GameManager.Instance.PlayerEntity.CurrentHealth;
            return error=> {
                if(error!=null||getStore()!=store||GameManager.Instance.PlayerEntity.CurrentHealth>=health||record.Death!=null)return;
                var guards=GetComponent<GuardPopulation>();
                if(guards!=null)guards.ObservePlayerAssault(source);
                if(!managed)store.RecordDefenseUnprovokedAttack(id,now);
            };
        }
        private static bool IsCorpseMurderSource(DaggerfallEntityBehaviour source)
        {
            if(source==null || source.Entity==null)return false;
            if(source.Entity is EnemyEntity)return true;
            var resident=source.GetComponent<ResidentActor>();
            return resident!=null && resident.Resident!=null && resident.Resident.IsAlive &&
                !resident.Resident.IsQuestProtected && source.Entity is CivilianEntity;
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
        public static void RelayDefenseWitnesses(WorldStateStore store,DefenseCombatant record,GuardActor recipient,double now)
        {
            var death=record.Death;
            if(death==null || !death.WelfareEligible || death.WelfareConsumed || death.WelfareExpired ||
                now<death.DiedAt || now>=death.WelfareExpiresAt || death.InformedGuards.Contains(recipient.GuardId)) return;
            var roster=store.GetGuards(record.SettlementId);
            var guard=roster==null ? null : roster.Guards.Find(g=>g.Id==recipient.GuardId && g.IsAlive);
            if(guard==null || !roster.Reports.Exists(r=>r.Id==guard.IncidentId && r.ThreatId==record.Id && r.GuardIds.Contains(guard.Id))) return;
            var destination=DefenseScene.For(recipient); if(destination==null || destination.SettlementId!=record.SettlementId) return;
            Func<string,Component,bool> contact=(id,observer)=> {
                var witness=record.Witnesses.Find(w=>!w.Guard && w.ObserverId==id && w.SeenAt>=record.FirstPlayerHitAt &&
                    w.SeenAt<=death.DiedAt && death.DiedAt-w.SeenAt<=300);
                var source=DefenseScene.For(observer);
                if(witness==null || source==null || source.Root!=destination.Root ||
                    !CivilianResponses.Sees(observer.transform.position,recipient.transform.position,4)) return false;
                store.InformDefenseGuard(record.Id,id,recipient.GuardId,now); return true;
            };
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(actor.Resident!=null && actor.Resident.IsAlive && !actor.IsSleeping && contact(actor.Resident.Id,actor)) return;
            var people=store.NativePeopleFor(record.SettlementId);
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<StaticNPC>())
            {
                var pose=npc.GetComponent<SleepingPose>(); if(pose && pose.Active) continue;
                var person=people.Find(p=>p.NativeHash==npc.Data.hash && p.WorkKey==npc.Data.buildingKey && p.IsAlive);
                if(person!=null && contact(person.Id,npc)) return;
            }
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
            catch(Exception error) { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DEFENSE_OBSERVATION_FAILED: "+error.Message); }
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
