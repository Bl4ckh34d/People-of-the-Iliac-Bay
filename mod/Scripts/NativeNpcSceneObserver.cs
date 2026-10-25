using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Real mobile and stationary bodies share perception and persistent identity.
    public sealed class NativeNpcSceneObserver : MonoBehaviour
    {
        private WorldStateStore store;
        private object epoch;
        private ResidentActor actor;
        private StaticNPC stationary;
        private NativePerson person;
        private StreetPopulation streets;
        internal string PersonId { get { return actor ? actor.Resident.Id : person.Id; } }
        private EntityEffectManager playerEffects;
        private float nextSample,lastSeenReal;
        private double lastSeenGame;
        private int lastPlayerHealth;
        private bool sawPlayer;
        private string lastPlace;
        public static void BindTo(GameObject owner,WorldStateStore world)
        {
            var body=owner.GetComponent<ResidentActor>();if(!body)return;
            var observer=owner.GetComponent<NativeNpcSceneObserver>();
            if(!observer)observer=owner.AddComponent<NativeNpcSceneObserver>();
            if(observer.store==world && observer.actor==body && world.IsNpcVitalsWriteEpochCurrent(observer.epoch))return;
            // A new world binding cannot compare this player's health with an
            // observation from before restore. An unchanged binding keeps continuity.
            observer.Detach();observer.sawPlayer=false;
            observer.store=world;observer.epoch=world.CaptureNpcVitalsWriteEpoch();observer.actor=body;
            observer.nextSample=Time.unscaledTime+(body.Resident.AppearanceSeed%1000)/1000f;
        }
        internal static NativeNpcSceneObserver BindTo(StaticNPC npc,NativePerson identity,WorldStateStore world,StreetPopulation population)
        {
            var observer=npc.GetComponent<NativeNpcSceneObserver>();
            if(!observer)observer=npc.gameObject.AddComponent<NativeNpcSceneObserver>();
            if(observer.store==world && observer.person==identity && world.IsNpcVitalsWriteEpochCurrent(observer.epoch))return observer;
            observer.Detach();observer.store=world;observer.epoch=world.CaptureNpcVitalsWriteEpoch();
            observer.stationary=npc;observer.person=identity;observer.streets=population;observer.actor=null;observer.sawPlayer=false;
            observer.nextSample=Time.unscaledTime+(unchecked((uint)identity.NativeHash)%1000)/1000f;
            return observer;
        }
        private bool Current()
        {return store!=null && store.IsNpcVitalsWriteEpochCurrent(epoch) && GameManager.Instance.IsReady &&
            (actor ? actor.Resident!=null && actor.Resident.IsAlive && !actor.IsSleeping :
            stationary && streets && streets.StaticObserverAwake(stationary,person));}
        internal bool Sees(Vector3 target,float range)
        {return isActiveAndEnabled && Current() && StreetPopulation.SeesSceneEvent(transform,target,range);}
        private void Update()
        {
            if(GameManager.IsGamePaused || Time.unscaledTime<nextSample)return;
            nextSample=Time.unscaledTime+1;
            if(!Current()){sawPlayer=false;return;}
            var game=GameManager.Instance;
            var effects=game.PlayerEntityBehaviour.GetComponent<EntityEffectManager>();
            if(playerEffects!=effects)
            {
                Detach();playerEffects=effects;if(playerEffects)playerEffects.OnCastReadySpell+=SawSpell;
            }
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            bool sees=!game.PlayerEntity.IsInvisible && Sees(game.PlayerMotor.transform.position,20);
            if(sees)
            {
                int health=game.PlayerEntity.CurrentHealth;
                if(sawPlayer && Time.unscaledTime-lastSeenReal<2.5f && now>=lastSeenGame && now-lastSeenGame<60 && health!=lastPlayerHealth)
                {
                    bool injury=health<lastPlayerHealth;int affinity;
                    int magnitude=Math.Min(5,Math.Max(1,(int)Math.Ceiling(5d*Math.Abs(health-lastPlayerHealth)/Math.Max(1,game.PlayerEntity.MaxHealth))));
                    int valence=NpcEventValence.PersonalOutcome(injury?-magnitude:magnitude,false,
                        store.TryGetActualConversationDisposition(PersonId,out affinity)?(int?)affinity:null);
                    NativeSceneRecollection.Remember(store,PersonId,"player-health:"+Guid.NewGuid().ToString("N"),
                        injury?NpcSceneEventKind.Injury:NpcSceneEventKind.Healing,
                        injury?"I saw the player become more injured. I cannot identify the cause from that change alone.":
                        "I saw the player's injuries improve. I cannot identify the healer from that change alone.",valence,NpcDirectedRelationship.PlayerTargetId);
                }
                lastPlayerHealth=health;lastSeenReal=Time.unscaledTime;lastSeenGame=now;
            }
            sawPlayer=sees;
            string place=game.PlayerEnterExit.IsPlayerInsideDungeon?"dungeon:"+(game.PlayerGPS.HasCurrentLocation?game.PlayerGPS.CurrentLocation.Name:"unknown"):
                game.PlayerEnterExit.IsPlayerInsideBuilding?"building:"+game.PlayerEnterExit.BuildingDiscoveryData.buildingKey:null;
            if(place!=lastPlace && place!=null && game.PlayerEnterExit.IsPlayerInsideDungeon)
                NativeSceneRecollection.Remember(store,PersonId,"dungeon:"+Guid.NewGuid().ToString("N"),NpcSceneEventKind.Journey,
                    sees?"I was inside this dungeon with the player. I do not know their purpose unless they told me.":"I was inside this dungeon.",0,
                    sees?NpcDirectedRelationship.PlayerTargetId:null);
            lastPlace=place;
        }
        private void SawSpell(EntityEffectBundle spell)
        {
            if(!Current() || GameManager.Instance.PlayerEntity.IsInvisible ||
                !Sees(GameManager.Instance.PlayerMotor.transform.position,20))return;
            string bucket=((long)(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()/300)).ToString();
            NativeSceneRecollection.Remember(store,PersonId,"player-cast:"+bucket,NpcSceneEventKind.Magic,
                "I saw the player cast magic. Seeing a cast alone does not tell me its effect, target or purpose.",0,NpcDirectedRelationship.PlayerTargetId);
        }
        private void Detach(){if(playerEffects)playerEffects.OnCastReadySpell-=SawSpell;playerEffects=null;}
        private void OnDisable(){Detach();sawPlayer=false;}
        private void OnDestroy(){Detach();}
    }
}
