using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Controlled nonlethal native health change between the production
        // before/after attack callbacks. Existing bodies and real LOS; no yields.
        private void VerifyEnemyAttackObservers()
        {
            var bodies=new List<ResidentActor>();
            foreach(var actor in actors.Values)
                if(actor && actor.gameObject.activeInHierarchy && actor.Resident.IsAlive && !actor.IsSleeping &&
                    actor.GetComponent<DaggerfallEntityBehaviour>().Entity.CurrentHealth>2)bodies.Add(actor);
            if(bodies.Count<3)throw new InvalidOperationException("Enemy observation fixture needs three healthy mobile bodies.");
            var victim=bodies[0];var witness=bodies[1];var attacker=bodies[2];
            var target=victim.GetComponent<DaggerfallEntityBehaviour>();
            var source=attacker.GetComponent<DaggerfallEntityBehaviour>();
            var original=worldStore;var baseline=original.Snapshot();
            baseline.AlarmIncidents.RemoveAll(row=>row.VictimId==victim.Resident.Id);
            baseline.SceneMemories.RemoveAll(row=>row.ObserverId==victim.Resident.Id || row.ObserverId==witness.Resident.Id || row.ObserverId==attacker.Resident.Id);
            var positions=new[]{victim.transform.position,witness.transform.position,attacker.transform.position};
            var rotations=new[]{victim.transform.rotation,witness.transform.rotation,attacker.transform.rotation};
            var concealment=source.Entity.MagicalConcealmentFlags;
            int health=target.Entity.CurrentHealth;bool quiesce=target.Entity.Quiesce;
            float survey=nextStationarySurvey;
            var sleepField=typeof(ResidentActor).GetField("sleeping",BindingFlags.NonPublic|BindingFlags.Instance);
            var sleep=witness.GetComponent<SleepingPose>();bool addedSleep=!sleep;
            var previousSleep=sleepField.GetValue(witness);
            var sleepActive=typeof(SleepingPose).GetProperty("Active");
            GameObject wall=null;
            try
            {
                nextStationarySurvey=float.MaxValue;target.Entity.Quiesce=true;
                if(!sleep)sleep=witness.gameObject.AddComponent<SleepingPose>();
                sleepField.SetValue(witness,sleep);
                Vector3 heading=Vector3.zero;
                for(int i=0;i<8;i++)
                {
                    var candidate=Quaternion.Euler(0,i*45,0)*Vector3.forward;
                    var from=positions[0]-candidate*3;var to=positions[0]+candidate*3;
                    if(CivilianResponses.Sees(from,to,20)){heading=candidate;break;}
                }
                if(heading==Vector3.zero)throw new InvalidOperationException("Enemy observation fixture has no clear six-metre sightline.");
                Action reset=()=>{
                    victim.transform.position=positions[0];victim.transform.forward=heading;
                    witness.transform.position=positions[0]-heading*3;witness.transform.forward=heading;
                    attacker.transform.position=positions[0]+heading*3;
                    attacker.gameObject.SetActive(true);source.Entity.MagicalConcealmentFlags=MagicalConcealmentFlags.None;
                    sleepActive.SetValue(sleep,false,null);if(wall)wall.SetActive(false);
                    target.Entity.SetHealth(health,true);Physics.SyncTransforms();
                };
                int cases=0;
                Action<string,bool,bool,bool,bool,Action> observe=(label,knowsAttacker,sawAttack,damage,failed,after)=>{
                    var scratch=new WorldStateStore(1);scratch.Restore(baseline);worldStore=scratch;
                    foreach(var actor in bodies)NativeNpcSceneObserver.BindTo(actor.gameObject,scratch);
                    var finish=BeginEnemyResidentAttack(victim,source);
                    if(finish==null)throw new InvalidOperationException("Enemy attack callback unavailable: "+label);
                    if(after!=null)after();
                    if(damage)target.Entity.SetHealth(health-1,true);
                    finish(failed?new InvalidOperationException("Controlled failed native attack"):null);
                    var memories=scratch.Snapshot().SceneMemories;
                    var own=memories.FindLast(row=>row.ObserverId==victim.Resident.Id && row.Kind==NpcSceneEventKind.Assault);
                    var seen=memories.FindLast(row=>row.ObserverId==witness.Resident.Id && row.Kind==NpcSceneEventKind.Assault);
                    bool occurred=damage && !failed;
                    string expected=knowsAttacker?"I was attacked by "+attacker.Person.NameNPC+". I do not know why.":
                        "I was attacked, but I could not identify my attacker. I do not know why.";
                    if((own!=null)!=occurred || occurred && (!own.Observation.StartsWith(expected,StringComparison.Ordinal) ||
                        own.SubjectId!=victim.Resident.Id || own.ResponsibleId==NpcDirectedRelationship.PlayerTargetId))
                        throw new InvalidOperationException("Enemy attack victim memory failed: "+label+"; observation="+(own==null?"none":own.Observation));
                    if((seen!=null)!=(occurred && sawAttack))
                        throw new InvalidOperationException("Enemy attack bystander memory failed: "+label+"; memory="+(seen!=null));
                    if(seen!=null && (seen.SubjectId!=victim.Resident.Id || seen.ResponsibleId==NpcDirectedRelationship.PlayerTargetId))
                        throw new InvalidOperationException("Enemy attack misattributed subject or player responsibility.");
                    if(seen!=null && !seen.Observation.StartsWith("I saw "+attacker.Person.NameNPC+" attack "+victim.Person.NameNPC+".",StringComparison.Ordinal))
                        throw new InvalidOperationException("Enemy witness memory lost the actual participant names.");
                    if(memories.Exists(row=>row.ObserverId==attacker.Resident.Id && row.Kind==NpcSceneEventKind.Assault))
                        throw new InvalidOperationException("Attacker admitted as its own bystander witness.");
                    cases++;Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENEMY_ATTACK_CASE_PASS: "+label+"; victimIdentifies="+knowsAttacker+"; bystanderSees="+sawAttack);
                };
                reset();observe("visible",true,true,true,false,null);
                reset();victim.transform.forward=-heading;observe("victim-facing-away",false,true,true,false,null);
                reset();witness.transform.forward=-heading;observe("witness-facing-away",true,false,true,false,null);
                reset();sleepActive.SetValue(sleep,true,null);observe("sleeping-witness",true,false,true,false,null);
                reset();source.Entity.MagicalConcealmentFlags=MagicalConcealmentFlags.InvisibleNormal;
                observe("invisible-attacker",false,false,true,false,null);
                reset();attacker.gameObject.SetActive(false);observe("inactive-attacker",false,false,true,false,null);
                reset();witness.transform.position=positions[0]-heading*30;
                observe("distant-witness",true,false,true,false,null);
                reset();wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Owned enemy attack occlusion fixture";
                wall.transform.position=positions[0]+heading*1.5f;wall.transform.localScale=new Vector3(1.2f,3,1.2f);
                Physics.SyncTransforms();observe("occluded",false,false,true,false,null);
                reset();observe("no-damage",true,true,false,false,null);
                reset();observe("failed-attack",true,true,true,true,null);
                reset();observe("turn-after-hit",true,true,true,false,()=>{
                    victim.transform.forward=-heading;witness.transform.forward=-heading;
                    source.Entity.MagicalConcealmentFlags=MagicalConcealmentFlags.InvisibleNormal;
                });
                reset();victim.transform.forward=-heading;witness.transform.forward=-heading;
                observe("turn-after-unseen-hit",false,false,true,false,()=>{victim.transform.forward=heading;witness.transform.forward=heading;});
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENEMY_ATTACK_OBSERVERS_PASS: cases="+cases+
                    "; nativeBodies=true; nonPlayerSource=true; nativeHealth=true; actualCombatSwingNotInvoked=true");
            }
            finally
            {
                worldStore=original;nextStationarySurvey=survey;
                attacker.gameObject.SetActive(true);
                target.Entity.SetHealth(health,true);target.Entity.Quiesce=quiesce;
                source.Entity.MagicalConcealmentFlags=concealment;
                if(sleep)sleepActive.SetValue(sleep,false,null);sleepField.SetValue(witness,previousSleep);
                if(addedSleep && sleep)UnityEngine.Object.DestroyImmediate(sleep);
                if(wall)UnityEngine.Object.DestroyImmediate(wall);
                for(int i=0;i<3;i++){bodies[i].transform.position=positions[i];bodies[i].transform.rotation=rotations[i];}
                foreach(var actor in bodies)NativeNpcSceneObserver.BindTo(actor.gameObject,original);
                Physics.SyncTransforms();
            }
        }
    }
}
