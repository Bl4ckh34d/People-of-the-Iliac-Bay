using System;
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
        // Exercise the production attack-witness adapter with an existing static
        // body. No yields: the detached ledger and controlled sleep cannot leak
        // into ordinary simulation, animation or the player's save.
        private bool VerifyStationaryViolenceObserver(StationaryParticipant entry)
        {
            if(interior)return false;
            ResidentActor victim=null;
            foreach(var actor in actors.Values)
                if(actor && actor.Resident.IsAlive){victim=actor;break;}
            if(!victim)return false;
            var game=GameManager.Instance;var npc=entry.Npc;var original=worldStore;
            var playerBefore=game.PlayerMotor.transform.position;
            var concealment=game.PlayerEntity.MagicalConcealmentFlags;
            float surveyBefore=nextStationarySurvey;
            var baseline=original.Snapshot();
            baseline.AlarmIncidents.RemoveAll(row=>row.VictimId==victim.Resident.Id);
            baseline.SceneMemories.RemoveAll(row=>row.ObserverId==entry.Person.Id);
            var sleep=npc.GetComponent<SleepingPose>();bool addedSleep=!sleep;
            bool sleepBefore=sleep && sleep.Active;
            var active=typeof(SleepingPose).GetProperty("Active");
            GameObject wall=null;
            try
            {
                if(!sleep)sleep=npc.gameObject.AddComponent<SleepingPose>();
                nextStationarySurvey=float.MaxValue;
                game.PlayerEntity.MagicalConcealmentFlags=MagicalConcealmentFlags.None;
                var pose=npc.GetComponent<DirectionalKeeperPose>();
                var heading=pose?pose.Heading:npc.transform.forward;heading.y=0;heading.Normalize();
                var front=npc.transform.position+heading*3;
                var victimPoint=front+Vector3.Cross(heading,Vector3.up)*0.4f;
                game.PlayerMotor.transform.position=front;Physics.SyncTransforms();
                if(!entry.Observer.Sees(front,20) || !entry.Observer.Sees(victimPoint,20))return false;
                Action<string,bool,bool> observe=(label,expected,explicitAttacker)=>
                {
                    var scratch=new WorldStateStore(1);scratch.Restore(baseline);worldStore=scratch;
                    entry.Observer=NativeNpcSceneObserver.BindTo(npc,entry.Person,scratch,this);
                    double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    var alarm=scratch.RecordViolence(population.SettlementId,victim.Resident.Id,now);
                    ObserveExteriorStaticWitnesses(alarm,victimPoint,now,explicitAttacker?game.PlayerEntityBehaviour:null);
                    var witness=alarm.People.Find(row=>row.ResidentId==entry.Person.Id && row.DirectWitness);
                    bool memory=scratch.Snapshot().SceneMemories.Exists(row=>row.ObserverId==entry.Person.Id &&
                        row.Kind==NpcSceneEventKind.Assault && row.SubjectId==victim.Resident.Id);
                    if((witness!=null)!=expected || memory!=expected)
                        throw new InvalidOperationException("Static attack witness boundary failed: "+label+
                            "; witness="+(witness!=null)+"; memory="+memory+"; expected="+expected);
                    if(witness!=null && (!witness.StaticWitness || !witness.HasPosition || witness.BuildingKey!=-1))
                        throw new InvalidOperationException("Static attack witness lost its actual exterior position.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_ATTACK_CASE_PASS: "+label+"; witnessed="+expected);
                };
                observe("visible",true,false);
                active.SetValue(sleep,true,null);observe("sleeping",false,false);active.SetValue(sleep,false,null);
                game.PlayerMotor.transform.position=npc.transform.position-heading*3;Physics.SyncTransforms();
                observe("attacker-behind",false,false);
                game.PlayerMotor.transform.position=front;
                var victimFront=victimPoint;victimPoint=npc.transform.position-heading*3;Physics.SyncTransforms();
                observe("victim-behind",false,false);victimPoint=victimFront;
                game.PlayerMotor.transform.position=npc.transform.position+heading*30;Physics.SyncTransforms();
                observe("out-of-range",false,false);game.PlayerMotor.transform.position=front;
                game.PlayerEntity.MagicalConcealmentFlags=MagicalConcealmentFlags.InvisibleNormal;
                observe("invisible-attacker",false,false);game.PlayerEntity.MagicalConcealmentFlags=MagicalConcealmentFlags.None;
                npc.gameObject.SetActive(false);observe("inactive",false,false);npc.gameObject.SetActive(true);
                wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Owned static attack occlusion fixture";
                wall.transform.position=(npc.transform.position+front)*0.5f;wall.transform.localScale=new Vector3(1.2f,3,1.2f);
                Physics.SyncTransforms();observe("occluded",false,false);
                wall.SetActive(false);Physics.SyncTransforms();observe("visible-explicit-source",true,true);
                active.SetValue(sleep,true,null);observe("sleeping-explicit-source",false,true);
                active.SetValue(sleep,false,null);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_ATTACK_OBSERVER_PASS: person="+entry.Person.Id+
                    "; cases=10; actualBody=true; detachedLedger=true; attackAdapter=true; nativeDamageNotInvoked=true");
                return true;
            }
            finally
            {
                worldStore=original;nextStationarySurvey=surveyBefore;
                if(sleep)active.SetValue(sleep,sleepBefore,null);
                if(addedSleep && sleep)UnityEngine.Object.DestroyImmediate(sleep);
                if(wall)UnityEngine.Object.DestroyImmediate(wall);
                if(npc && !npc.gameObject.activeSelf)npc.gameObject.SetActive(true);
                game.PlayerEntity.MagicalConcealmentFlags=concealment;game.PlayerMotor.transform.position=playerBefore;
                Physics.SyncTransforms();entry.Observer=NativeNpcSceneObserver.BindTo(npc,entry.Person,original,this);
            }
        }
    }
}
