using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void VerifyPlayerAttackObservers()
        {
            ResidentActor victim=null,mobile=null;
            foreach(var actor in actors.Values)
            {
                if(!actor || !actor.Resident.IsAlive || actor.IsSleeping || !actor.gameObject.activeInHierarchy)continue;
                if(!victim)victim=actor;else {mobile=actor;break;}
            }
            if(!victim || !mobile)throw new InvalidOperationException("Player witness fixture needs two resident bodies.");
            VerifyPlayerAttackObserverBody(victim,mobile.transform,mobile.Resident.Id,mobile);
            foreach(var entry in stationaryParticipants.Values)
                if(StaticChatEligible(entry,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()) &&
                    VerifyPlayerAttackObserverBody(victim,entry.Npc.transform,entry.Person.Id,null))return;
            throw new InvalidOperationException("Player witness fixture needs a stationary body with clear forward sight.");
        }

        private bool VerifyPlayerAttackObserverBody(ResidentActor victim,Transform observer,string id,ResidentActor mobile)
        {
            var game=GameManager.Instance;var playerBefore=game.PlayerMotor.transform.position;
            var victimBefore=victim.transform.position;var concealment=game.PlayerEntity.MagicalConcealmentFlags;
            var baseline=worldStore.Snapshot();
            baseline.AlarmIncidents.RemoveAll(row=>row.VictimId==victim.Resident.Id);
            baseline.SceneMemories.RemoveAll(row=>row.ObserverId==id);
            var randomBefore=UnityEngine.Random.state;GameObject wall=null;bool sleeping=false;
            string kind=mobile?"mobile":"stationary";
            var pose=observer.GetComponent<DirectionalKeeperPose>();
            var heading=pose?pose.Heading:observer.forward;heading.y=0;heading.Normalize();
            var front=observer.position+heading*3;
            var victimFront=front+Vector3.Cross(heading,Vector3.up)*0.4f;
            if(!CivilianResponses.Sees(observer.position,front,20) || !CivilianResponses.Sees(observer.position,victimFront,20))
            {
                if(mobile)throw new InvalidOperationException("Mobile player witness fixture lacks a clear forward sightline.");
                return false;
            }
            try
            {
                Action reset=()=>{
                    observer.gameObject.SetActive(true);sleeping=false;if(wall)wall.SetActive(false);
                    game.PlayerEntity.MagicalConcealmentFlags=MagicalConcealmentFlags.None;
                    game.PlayerMotor.transform.position=front;victim.transform.position=victimFront;Physics.SyncTransforms();
                };
                int cases=0;
                Action<string,bool,bool,int> observe=(label,expectedDirect,expectedHeard,building)=>{
                    var scratch=new WorldStateStore(1);scratch.Restore(baseline);
                    double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    var alarm=scratch.RecordViolence(population.SettlementId,victim.Resident.Id,now);
                    scratch.LocateViolence(alarm.Id,building,0,0,0);
                    if(mobile)
                        new CivilianResponses(scratch,population.SettlementId).Murder(victim,new[]{victim,mobile},other=>sleeping,now,building);
                    else
                        CivilianResponses.Observe(scratch,alarm,id,observer,victim.transform.position,sleeping,now,building);
                    var person=alarm.People.Find(row=>row.ResidentId==id);
                    bool direct=person!=null && person.DirectWitness,heard=person!=null && person.HeardViolence;
                    bool memory=scratch.Snapshot().SceneMemories.Exists(row=>row.ObserverId==id && row.Kind==NpcSceneEventKind.Assault);
                    if(direct!=expectedDirect || heard!=expectedHeard || memory!=expectedDirect)
                        throw new InvalidOperationException("Player attack perception failed: "+kind+"/"+label+
                            "; direct="+direct+"; heard="+heard+"; assaultMemory="+memory);
                    cases++;Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PLAYER_ATTACK_CASE_PASS: "+kind+"/"+label+
                        "; direct="+direct+"; heard="+heard);
                };
                reset();observe("visible",true,false,-1);
                reset();game.PlayerMotor.transform.position=observer.position-heading*3;Physics.SyncTransforms();
                observe("player-behind",false,false,-1);
                reset();victim.transform.position=observer.position-heading*3;Physics.SyncTransforms();
                observe("victim-behind",false,false,-1);
                reset();game.PlayerEntity.MagicalConcealmentFlags=MagicalConcealmentFlags.InvisibleNormal;
                observe("invisible-player",false,false,-1);
                reset();game.PlayerMotor.transform.position=observer.position+heading*30;Physics.SyncTransforms();
                observe("distant-player",false,false,-1);
                reset();sleeping=true;observe("sleeping-outdoors",false,false,-1);
                reset();observer.gameObject.SetActive(false);observe("inactive",false,false,-1);
                reset();wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Owned player attack occlusion fixture";
                wall.transform.position=(observer.position+front)*0.5f;wall.transform.localScale=new Vector3(1.2f,3,1.2f);
                Physics.SyncTransforms();observe("occluded",false,false,-1);
                reset();game.PlayerMotor.transform.position=observer.position-heading*3;Physics.SyncTransforms();
                // Exercise the existing indoor hearing contract without claiming
                // that these exterior bodies have entered a real interior.
                observe("heard-without-seeing-player",false,true,population.Households[0].BuildingKey);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PLAYER_ATTACK_OBSERVER_PASS: kind="+kind+"; cases="+cases+
                    "; nativeBody=true; physicalInteriorNotLoaded=true; actualDamageNotInvoked=true");
                return true;
            }
            finally
            {
                if(wall)UnityEngine.Object.DestroyImmediate(wall);
                observer.gameObject.SetActive(true);victim.transform.position=victimBefore;
                game.PlayerEntity.MagicalConcealmentFlags=concealment;game.PlayerMotor.transform.position=playerBefore;
                UnityEngine.Random.state=randomBefore;Physics.SyncTransforms();
            }
        }
    }
}
