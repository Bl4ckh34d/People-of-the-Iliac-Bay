using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        internal IEnumerator VerifySceneObservers()
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
            DaggerfallUI.Instance.PopToHUD();GameManager.Instance.PauseGame(false);
            bool mobile=false,stationary=false;float deadline=Time.realtimeSinceStartup+90;
            while((!mobile || !stationary) && Time.realtimeSinceStartup<deadline)
            {
                SurveyStationaryObservers();
                if(!stationary)foreach(var entry in stationaryParticipants.Values)
                {
                    if(!StaticChatEligible(entry,clock.ToSeconds()))continue;
                    if(!VerifyObserverBody(entry.Npc.gameObject,entry.Person.Id,
                        world=>NativeNpcSceneObserver.BindTo(entry.Npc,entry.Person,world,this),"stationary"))continue;
                    stationary=true;break;
                }
                if(!mobile)foreach(var body in actors.Values)
                {
                    if(!body || !body.gameObject.activeInHierarchy || body.IsSleeping || !body.Resident.IsAlive)continue;
                    if(!VerifyObserverBody(body.gameObject,body.Resident.Id,world=>{
                        NativeNpcSceneObserver.BindTo(body.gameObject,world);return body.GetComponent<NativeNpcSceneObserver>();},"mobile"))continue;
                    mobile=true;break;
                }
                if(!mobile || !stationary)yield return null;
            }
            if(!mobile || !stationary)throw new InvalidOperationException("Observer fixture missing a visible native body: mobile="+mobile+"; stationary="+stationary);
            bool violence=false;
            foreach(var entry in stationaryParticipants.Values)
                if(StaticChatEligible(entry,clock.ToSeconds()) && VerifyStationaryViolenceObserver(entry))
                {violence=true;break;}
            if(!violence)throw new InvalidOperationException("No stationary attack witness has a clear view in this fixture.");
            deadline=Time.realtimeSinceStartup+75;
            while(Time.realtimeSinceStartup<deadline)
            {
                int available=0;
                foreach(var actor in actors.Values)
                    if(actor && actor.gameObject.activeInHierarchy && actor.Resident.IsAlive && !actor.IsSleeping &&
                        actor.GetComponent<DaggerfallEntityBehaviour>().Entity.CurrentHealth>2)available++;
                if(available>=3)break;
                yield return null;
            }
            VerifyEnemyAttackObservers();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SCENE_OBSERVERS_PASS: mobile=true; stationary=true; nativeVisibility=true; epochBoundary=true");
        }

        // Invoke the actual sample at controlled instants, against real Unity
        // colliders, native player health/concealment and existing NPC bodies.
        // A detached snapshot keeps these artificial outcomes out of the world ledger.
        private bool VerifyObserverBody(GameObject body,string id,Func<WorldStateStore,NativeNpcSceneObserver> bind,string kind)
        {
            var game=GameManager.Instance;var player=game.PlayerEntity;var playerBefore=game.PlayerMotor.transform.position;
            int healthBefore=player.CurrentHealth;bool godModeBefore=player.GodMode;var concealment=player.MagicalConcealmentFlags;
            var scratch=new WorldStateStore(1);scratch.Restore(worldStore.Snapshot());
            var observer=bind(scratch);GameObject wall=null;SleepingPose sleep=null;bool addedSleep=false;
            var privateFlags=BindingFlags.NonPublic|BindingFlags.Instance;
            var resident=body.GetComponent<ResidentActor>();
            var sleepField=typeof(ResidentActor).GetField("sleeping",privateFlags);
            var previousSleep=resident?sleepField.GetValue(resident):null;
            var update=typeof(NativeNpcSceneObserver).GetMethod("Update",privateFlags);
            var sampleAt=typeof(NativeNpcSceneObserver).GetField("nextSample",privateFlags);
            Action sample=()=>{sampleAt.SetValue(observer,0f);update.Invoke(observer,null);};
            Func<int> count=()=>scratch.Snapshot().SceneMemories.FindAll(m=>m.ObserverId==id &&
                (m.Kind==NpcSceneEventKind.Injury || m.Kind==NpcSceneEventKind.Healing)).Count;
            try
            {
                // The shared editor harness enables god mode. Native SetHealth
                // cannot produce the observed change until this fixture disables it.
                player.GodMode=false;
                player.MagicalConcealmentFlags=MagicalConcealmentFlags.None;
                var pose=body.GetComponent<DirectionalKeeperPose>();var heading=pose?pose.Heading:body.transform.forward;
                heading.y=0;heading.Normalize();var front=body.transform.position+heading*3;
                game.PlayerMotor.transform.position=front;Physics.SyncTransforms();
                if(!observer.Sees(front,20))return false;
                player.SetHealth(player.MaxHealth,true);sample();int baseline=count();
                player.SetHealth(player.MaxHealth-2,true);sample();
                if(count()!=baseline+1)throw new InvalidOperationException(kind+" failed visible injury observation.");
                var injury=scratch.Snapshot().SceneMemories.FindLast(m=>m.ObserverId==id && m.Kind==NpcSceneEventKind.Injury);
                if(injury.SubjectId!=NpcDirectedRelationship.PlayerTargetId || injury.ResponsibleId!=null)
                    throw new InvalidOperationException(kind+" invented a cause of observed injury.");
                player.SetHealth(player.MaxHealth,true);sample();
                if(count()!=baseline+2)throw new InvalidOperationException(kind+" failed visible healing observation.");

                foreach(var hidden in new[]{body.transform.position-heading*3,body.transform.position+heading*30})
                {
                    game.PlayerMotor.transform.position=hidden;Physics.SyncTransforms();sample();baseline=count();
                    if(observer.Sees(hidden,20))throw new InvalidOperationException(kind+" sees behind itself or beyond its range.");
                    player.SetHealth(player.MaxHealth-2,true);sample();
                    if(count()!=baseline)throw new InvalidOperationException(kind+" learned an injury outside its view.");
                    game.PlayerMotor.transform.position=front;Physics.SyncTransforms();sample();
                    if(count()!=baseline)throw new InvalidOperationException(kind+" inferred an unseen injury on returning into view.");
                    player.SetHealth(player.MaxHealth,true);sample();
                }

                body.SetActive(false);sample();baseline=count();
                player.SetHealth(player.MaxHealth-2,true);sample();
                if(observer.Sees(front,20) || count()!=baseline)throw new InvalidOperationException(kind+" observes while inactive.");
                body.SetActive(true);sample();
                if(count()!=baseline)throw new InvalidOperationException(kind+" inferred an injury on reactivation.");
                player.SetHealth(player.MaxHealth,true);sample();

                wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Owned observer occlusion fixture";
                wall.transform.position=(body.transform.position+front)*0.5f;wall.transform.localScale=new Vector3(1.2f,3,1.2f);
                Physics.SyncTransforms();sample();baseline=count();
                if(observer.Sees(front,20))throw new InvalidOperationException("Fixture wall does not occlude the native view.");
                player.SetHealth(player.MaxHealth-2,true);sample();
                if(count()!=baseline)throw new InvalidOperationException(kind+" learned an injury through a wall.");
                wall.SetActive(false);Physics.SyncTransforms();sample();
                if(count()!=baseline)throw new InvalidOperationException(kind+" inferred an unseen injury after occlusion ended.");
                player.SetHealth(player.MaxHealth,true);sample();

                player.MagicalConcealmentFlags=MagicalConcealmentFlags.InvisibleNormal;sample();baseline=count();
                player.SetHealth(player.MaxHealth-2,true);sample();
                if(count()!=baseline)throw new InvalidOperationException(kind+" saw an invisible player's injury.");
                player.MagicalConcealmentFlags=MagicalConcealmentFlags.None;sample();
                if(count()!=baseline)throw new InvalidOperationException(kind+" inferred an injury on reappearance.");
                player.SetHealth(player.MaxHealth,true);sample();

                sleep=body.GetComponent<SleepingPose>();addedSleep=!sleep;if(!sleep)sleep=body.AddComponent<SleepingPose>();
                // ResidentActor.Sleep normally stores this lazily-created pose.
                // Establish the same reference for this controlled state-only case.
                if(resident)sleepField.SetValue(resident,sleep);
                typeof(SleepingPose).GetProperty("Active").SetValue(sleep,true,null);sample();baseline=count();
                if(observer.Sees(front,20))throw new InvalidOperationException(kind+" sees while its sleeping state is active.");
                player.SetHealth(player.MaxHealth-2,true);sample();
                if(count()!=baseline)throw new InvalidOperationException(kind+" learned an injury while asleep.");
                typeof(SleepingPose).GetProperty("Active").SetValue(sleep,false,null);sample();
                if(count()!=baseline)throw new InvalidOperationException(kind+" inferred an injury on waking.");
                player.SetHealth(player.MaxHealth,true);sample();

                // A redundant binding within one epoch must retain continuity.
                baseline=count();bind(scratch);player.SetHealth(player.MaxHealth-2,true);sample();
                if(count()!=baseline+1)throw new InvalidOperationException(kind+" lost a witnessed change after an unchanged binding.");
                player.SetHealth(player.MaxHealth,true);sample();baseline=count();
                var saved=scratch.Snapshot();scratch.Restore(saved);
                if(observer.Sees(front,20))throw new InvalidOperationException(kind+" retained authority from an old epoch.");
                // Rebinding occurs before the next Update can notice the stale epoch.
                bind(scratch);player.SetHealth(player.MaxHealth-2,true);sample();
                if(count()!=baseline)throw new InvalidOperationException(kind+" fabricated an observed injury across a restore/rebind boundary.");
                player.SetHealth(player.MaxHealth,true);sample();
                if(count()!=baseline+1)throw new InvalidOperationException(kind+" did not observe real changes after rebinding.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SCENE_OBSERVER_BODY_PASS: kind="+kind+"; person="+id+
                    "; visibleInjuryHealing=true; occlusion=true; invisibility=true; behindAndDistance=true; inactive=true; controlledSleepState=true; unchangedBinding=true; restoredBinding=true");
                return true;
            }
            finally
            {
                if(sleep)typeof(SleepingPose).GetProperty("Active").SetValue(sleep,false,null);
                if(resident)sleepField.SetValue(resident,previousSleep);
                if(addedSleep && sleep)UnityEngine.Object.DestroyImmediate(sleep);
                if(wall)UnityEngine.Object.DestroyImmediate(wall);
                if(body && !body.activeSelf)body.SetActive(true);
                player.SetHealth(healthBefore,true);player.GodMode=godModeBefore;player.MagicalConcealmentFlags=concealment;
                game.PlayerMotor.transform.position=playerBefore;Physics.SyncTransforms();bind(worldStore);
            }
        }
    }
}
