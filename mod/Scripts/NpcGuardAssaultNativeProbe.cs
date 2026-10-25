using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in acceptance fixture. It invokes only native attacks/effect delivery;
    // production hooks must create every guard response.
    internal static class NpcGuardAssaultNativeProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static IEnumerator RunFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            if(Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")!="guard-assault" ||
                string.IsNullOrEmpty(character) || !character.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal))
                throw new InvalidOperationException("Guard assault probe requires POTIB_ROADMAP_PROBE=guard-assault and owned fixture character.");
            var game=GameManager.Instance;var guards=UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            Require(host&&streets&&guards&&game!=null&&game.PlayerEntity.Name==character,"Guard assault fixture is unavailable.");
            bool oldDefense=guards.EnemyDefense;bool oldGod=game.PlayerEntity.GodMode;
            bool oldStreets=streets.enabled;
            bool oldReactions=streets.ReactionsEnabled;
            var oldCrime=game.PlayerEntity.CrimeCommitted;Vector3 oldPlayer=game.PlayerMotor.transform.position;
            DaggerfallEntityBehaviour melee=null,magic=null,healer=null;ResidentActor resident=null;
            try
            {
                guards.EnemyDefense=true;game.PlayerEntity.GodMode=false;game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
                var clock=DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
                yield return WaitForReady(guards);
                var guard=FindGuard();Require(guard!=null,"No active exterior guard for assault fixture.");
                float residentDeadline=Time.realtimeSinceStartup+90;
                while(Time.realtimeSinceStartup<residentDeadline && (!streets.Ready || FindResident()==null))yield return null;
                resident=FindResident();Require(resident!=null,"No living ordinary resident for assault fixture.");
                streets.enabled=false;
                var directory=GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
                Require(directory!=null&&resident.transform.parent==directory.transform,"Fixture requires current exterior resident.");
                Stage(guard,resident,out var attackerPoint,out var playerPoint);
                var victim=resident.GetComponent<DaggerfallEntityBehaviour>();Require(victim&&resident.Resident.IsAlive,"Resident native body unavailable.");
                bool hasArmor=false;
                for(int i=0;i<victim.Entity.Items.Count;i++)
                {var item=victim.Entity.Items.GetItem(i);if(item.ItemGroup==DaggerfallWorkshop.Game.Items.ItemGroups.Armor && item.IsEquipped)hasArmor=true;}
                if(!hasArmor)foreach(var armor in victim.Entity.ArmorValues)
                    Require(armor==100,"Unarmored managed civilian retained incorrect native armor instead of 100.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_ASSAULT_BODY_STATS: resident="+resident.Resident.Id+
                    "; level="+victim.Entity.Level+"; hp="+victim.Entity.CurrentHealth+"/"+victim.Entity.MaxHealth+
                    "; agility="+victim.Entity.Stats.LiveAgility+"; dodging="+victim.Entity.Skills.GetLiveSkillValue(DaggerfallConnect.DFCareer.Skills.Dodging)+
                    "; armor="+string.Join(",",victim.Entity.ArmorValues)+"; guardLevel="+guard.GetComponent<DaggerfallEntityBehaviour>().Entity.Level);
                game.PlayerMotor.transform.position=playerPoint;

                var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
                Require(store!=null && store.ActiveAlarm(resident.Resident.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())==null &&
                    store.ActiveNpcAction(resident.Resident.Id)==null && store.ActiveNpcTask(resident.Resident.Id)==null,
                    "Lawful-flight fixture resident already has an alarm, action, or task.");
                Require(!guard.CombatReady && guard.DefenseTarget==null,"Lawful-flight fixture guard is already in combat.");
                int incidentsBefore=store.Snapshot().AlarmIncidents.Count;
                int reportsBefore=guards.Roster.Reports.Count;
                int healthBefore=victim.Entity.CurrentHealth;
                Require(healthBefore>1,"Lawful-flight fixture resident has no nonlethal hit margin.");
                streets.enabled=true;streets.ReactionsEnabled=true;
                guard.BeginDefenseCombat(victim,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                yield return RequireLawfulGuardFlight(guards,guard,resident,victim,store,healthBefore,incidentsBefore,reportsBefore);
                guard.EndCombat();streets.enabled=false;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LAWFUL_GUARD_FLIGHT_PASS: nativeGuardHit=true; nonlethal=true; residentFled=true; collisionSafe=true; playerCrime=false; incident=false; custody=false; guardCriminalized=false");

                melee=NpcMurderNativeProbe.Spawn(directory,attackerPoint);
                SetPassive(melee);int before=victim.Entity.CurrentHealth;
                yield return NativeNonlethalHit(melee,resident);
                Require(resident.Resident.IsAlive&&victim.Entity.CurrentHealth<before,"Passive native attack did not cause one nonlethal resident hit.");
                yield return RequireRecognition(guards,melee,true,"passive-native-melee");
                UnityEngine.Object.Destroy(melee.gameObject);melee=null;
                yield return new WaitForSecondsRealtime(3);

                magic=NpcMurderNativeProbe.Spawn(directory,AlternateSourcePoint(guard,victim,attackerPoint));
                SetPassive(magic);Deliver(magic,victim,Paralyze.EffectKey,0);
                yield return RequireRecognition(guards,magic,false,"hostile-paralyze-attempt");
                Vector3 magicPoint=magic.transform.position;
                UnityEngine.Object.Destroy(magic.gameObject);magic=null;
                yield return new WaitForSecondsRealtime(3);

                healer=NpcMurderNativeProbe.Spawn(directory,AlternateSourcePoint(guard,victim,magicPoint));
                SetPassive(healer);Deliver(healer,victim,HealHealth.EffectKey,100);
                yield return new WaitForSecondsRealtime(1.2f);
                Require(!GuardTargets(healer),"Benign native Heal created guard assault recognition.");

                game.PlayerMotor.transform.position=playerPoint;
                int playerBefore=game.PlayerEntity.CurrentHealth;
                Require(playerBefore>1,"Guard assault fixture player health is unsafe for a sourced hit.");
                var attackingResidentEntity=victim.Entity;
                int attackerHealthBefore=attackingResidentEntity.CurrentHealth;
                game.PlayerEntityBehaviour.DamageHealthFromSource(victim,1,true,game.PlayerMotor.transform.position);
                Require(game.PlayerEntity.CurrentHealth<playerBefore,"Managed resident did not cause actual sourced player damage.");
                yield return RequireRecognition(guards,victim,false,"managed-resident-player-hit");
                float combatDeadline=Time.realtimeSinceStartup+45;
                while(Time.realtimeSinceStartup<combatDeadline && attackingResidentEntity.CurrentHealth>=attackerHealthBefore)
                    yield return null;
                Require(attackingResidentEntity.CurrentHealth<attackerHealthBefore,
                    "Guard recognized the managed attacker but did not inflict actual native damage.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_MANAGED_ATTACKER_DAMAGE_PASS: before="+attackerHealthBefore+
                    "; after="+attackingResidentEntity.CurrentHealth+"; nativeCombat=true");
                Require(game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None,"Assault fixture charged innocent player.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_ASSAULT_NATIVE_PASS: passiveNativeHit=true; exactGuardTarget=true; approach=true; hostileNonHealthAttempt=true; benignHealIgnored=true; managedPlayerSource=true; actualManagedAttackerDamage=true; innocentPlayer=true");
            }
            finally
            {
                if(melee)UnityEngine.Object.Destroy(melee.gameObject);
                if(magic)UnityEngine.Object.Destroy(magic.gameObject);
                if(healer)UnityEngine.Object.Destroy(healer.gameObject);
                if(resident&&resident.gameObject.activeInHierarchy)resident.Place(resident.transform.position-Vector3.up,Vector3.forward,true);
                if(guards)guards.EnemyDefense=oldDefense;
                if(streets)streets.enabled=oldStreets;
                if(streets)streets.ReactionsEnabled=oldReactions;
                game.PlayerEntity.GodMode=oldGod;game.PlayerEntity.CrimeCommitted=oldCrime;game.PlayerMotor.transform.position=oldPlayer;
            }
        }
        private static IEnumerator WaitForReady(GuardPopulation guards)
        {
            float deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline&&(guards.Roster==null||FindGuard()==null))yield return null;
            Require(guards.Roster!=null&&FindGuard()!=null,"Guard roster did not become ready.");
        }
        private static GuardActor FindGuard()
        {
            foreach(var guard in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                if(guard&&guard.gameObject.activeInHierarchy&&!guard.IsInterior&&guard.GetComponent<DaggerfallEntityBehaviour>().Entity.CurrentHealth>0)return guard;
            return null;
        }
        private static ResidentActor FindResident()
        {
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(actor&&actor.gameObject.activeInHierarchy&&!actor.IsSleeping&&actor.Resident!=null&&actor.Resident.IsAlive&&
                    !actor.Resident.IsQuestProtected&&actor.GetComponent<DaggerfallEntityBehaviour>().Entity is CivilianEntity)return actor;
            return null;
        }
        private static void Stage(GuardActor guard,ResidentActor resident,out Vector3 attacker,out Vector3 player)
        {
            Vector3 basePoint=guard.transform.position;Vector3 victim=basePoint+guard.transform.right*3;
            attacker=victim+guard.transform.forward*2;player=basePoint-guard.transform.forward*5;
            Require(CivilianResponses.Sees(basePoint,attacker,20)&&CivilianResponses.Sees(attacker,victim,20)&&
                CivilianResponses.Sees(attacker,player,20)&&CivilianResponses.Sees(victim,player,20),
                "Fixture positions lack direct same-scene LOS.");
            var navigation=resident.GetComponent<NativeResidentNavigation>();if(navigation)navigation.ResetPlacement();
            resident.GetComponent<CrowdMotion>().RestorePlacement(victim-Vector3.up);
        }
        private static Vector3 AlternateSourcePoint(GuardActor guard,DaggerfallEntityBehaviour victim,Vector3 occupied)
        {
            foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
            {
                Vector3 point=victim.transform.position+direction*2;
                if(Vector3.Distance(point,occupied)<1.5f||!CivilianResponses.Sees(guard.transform.position,point,20)||
                    !CivilianResponses.Sees(point,victim.transform.position,20))continue;
                return point;
            }
            throw new InvalidOperationException("No separate visible source point for guard assault fixture.");
        }
        private static void SetPassive(DaggerfallEntityBehaviour body)
        {
            var motor=body.GetComponent<EnemyMotor>();motor.enabled=false;motor.IsHostile=false;
            body.GetComponent<EnemySenses>().enabled=false;body.GetComponent<EnemyAttack>().enabled=true;
            body.Entity.MaxHealth=500;body.Entity.SetHealth(500,true);
        }
        private static IEnumerator NativeNonlethalHit(DaggerfallEntityBehaviour attacker,ResidentActor victim)
        {
            attacker.Entity.Level=10;attacker.Entity.Skills.SetPermanentSkillValue(DaggerfallConnect.DFCareer.Skills.HandToHand,100);
            var senses=attacker.GetComponent<EnemySenses>();senses.Target=victim.GetComponent<DaggerfallEntityBehaviour>();
            var method=typeof(EnemyAttack).GetMethod("ApplyDamageToNonPlayer",BindingFlags.Instance|BindingFlags.NonPublic);
            Require(method!=null,"Native non-player attack entry changed.");
            int health=victim.GetComponent<DaggerfallEntityBehaviour>().Entity.CurrentHealth;
            for(int i=0;i<30&&victim.Resident.IsAlive&&victim.GetComponent<DaggerfallEntityBehaviour>().Entity.CurrentHealth==health;i++)
            {
                method.Invoke(attacker.GetComponent<EnemyAttack>(),new object[]{null,victim.transform.position-attacker.transform.position,false});
                yield return null;
            }
        }
        private static void Deliver(DaggerfallEntityBehaviour caster,DaggerfallEntityBehaviour victim,string key,int chance)
        {
            var settings=BaseEntityEffect.DefaultEffectSettings();settings.ChanceBase=chance;settings.ChancePlus=0;settings.ChancePerLevel=1;
            settings.DurationBase=1;settings.DurationPlus=0;settings.DurationPerLevel=1;
            var bundle=caster.GetComponent<EntityEffectManager>().CreateSpellBundle(key,ElementTypes.Magic,settings);
            Require(bundle!=null,"Native effect bundle unavailable: "+key);bundle.CasterEntityBehaviour=caster;
            var values=bundle.Settings;values.TargetType=TargetTypes.ByTouch;bundle.Settings=values;
            victim.GetComponent<EntityEffectManager>().AssignBundle(bundle,AssignBundleFlags.None);
        }
        private static IEnumerator RequireRecognition(GuardPopulation guards,DaggerfallEntityBehaviour source,bool requireApproach,string stage)
        {
            float deadline=Time.realtimeSinceStartup+45;bool exact=false,approached=false;
            var starts=new System.Collections.Generic.Dictionary<string,Vector3>();
            foreach(var guard in UnityEngine.Object.FindObjectsOfType<GuardActor>())if(guard)starts[guard.GuardId]=guard.transform.position;
            while(Time.realtimeSinceStartup<deadline)
            {
                foreach(var guard in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                {
                    if(!guard||!guard.gameObject.activeInHierarchy)continue;
                    var senses=guard.GetComponent<EnemySenses>();
                    exact|=guard.DefenseTarget==source&&senses&&senses.Target==source;
                    Vector3 start;if(starts.TryGetValue(guard.GuardId,out start))approached|=Vector3.Distance(start,guard.transform.position)>0.5f;
                }
                if(exact&&(!requireApproach||approached))
                {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_ASSAULT_STAGE_PASS: "+stage+"; exact=true; approach="+approached);yield break;}
                yield return null;
            }
            throw new InvalidOperationException("Guard did not recognize exact source: "+stage+"; exact="+exact+"; approach="+approached+"; reports="+(guards.Roster==null?-1:guards.Roster.Reports.Count));
        }
        private static IEnumerator RequireLawfulGuardFlight(GuardPopulation guards,GuardActor guard,ResidentActor resident,
            DaggerfallEntityBehaviour victim,WorldStateStore store,int healthBefore,int incidentsBefore,int reportsBefore)
        {
            float deadline=Time.realtimeSinceStartup+30;Vector3 start=resident.transform.position;Vector3 previous=start;
            bool moved=false,collision=false,hit=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                Require(resident&&resident.Resident.IsAlive&&victim&&victim.Entity.CurrentHealth>0,
                    "Lawful guard fixture turned a required nonlethal hit into death.");
                hit|=victim.Entity.CurrentHealth<healthBefore;
                Vector3 current=resident.transform.position;
                if(Vector3.Distance(current,previous)>0.001f)
                {
                    moved|=Vector3.Distance(current,start)>0.35f;
                    collision|=OverlapsStaticWall(resident);
                    previous=current;
                }
                bool clean=GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None &&
                    store.Snapshot().AlarmIncidents.Count==incidentsBefore && store.ActiveAlarm(resident.Resident.Id,
                        DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())==null && store.ActiveNpcAction(resident.Resident.Id)==null &&
                    store.ActiveNpcTask(resident.Resident.Id)==null && guards.Roster.Reports.Count==reportsBefore &&
                    guard.GetComponent<GuardAttackEvidence>()==null;
                if(hit&&moved&&guard.DefenseTarget==victim&&clean&&!collision)yield break;
                yield return null;
            }
            throw new InvalidOperationException("Lawful guard flight failed: hit="+hit+"; moved="+moved+"; collision="+collision+
                "; target="+(guard.DefenseTarget==victim)+"; crime="+GameManager.Instance.PlayerEntity.CrimeCommitted+
                "; incidents="+store.Snapshot().AlarmIncidents.Count+"/"+incidentsBefore+
                "; reports="+guards.Roster.Reports.Count+"/"+reportsBefore);
        }
        private static bool OverlapsStaticWall(ResidentActor actor)
        {
            Vector3 ground=actor.transform.position-Vector3.up;
            foreach(var hit in Physics.OverlapCapsule(ground+Vector3.up*0.3f,ground+Vector3.up*1.65f,0.2f,~0,QueryTriggerInteraction.Ignore))
                if(CivilianResponses.Blocker(hit))return true;
            return false;
        }
        private static bool GuardTargets(DaggerfallEntityBehaviour source)
        {
            foreach(var guard in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                if(guard&&guard.DefenseTarget==source)return true;
            return false;
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
