using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private float nextSceneMemoryScan;
        private int sceneMemoryCursor;
        private readonly List<ResidentActor> sceneMemoryActors=new List<ResidentActor>();
        private void ObserveSceneMemories(double now)
        {
            if(GameManager.IsGamePaused || Time.unscaledTime<nextSceneMemoryScan || !BackgroundWork.TryStart())return;
            nextSceneMemoryScan=Time.unscaledTime+1;
            sceneMemoryActors.Clear();sceneMemoryActors.AddRange(actors.Values);
            var guards=GetComponent<GuardPopulation>();
            for(int index=0;index<Math.Min(4,sceneMemoryActors.Count);index++)
            {
                if(sceneMemoryCursor>=sceneMemoryActors.Count)sceneMemoryCursor=0;
                var actor=sceneMemoryActors[sceneMemoryCursor++];
                if(!actor || !actor.gameObject.activeInHierarchy || !actor.Resident.IsAlive || actor.IsSleeping)continue;
                string bucket=((long)(now/600)).ToString(CultureInfo.InvariantCulture);
                string place=interior?GameManager.Instance.TalkManager.GetBuildingNameForBuildingKey(interior.EntryDoor.buildingKey):"the street";
                int building=interior?interior.EntryDoor.buildingKey:-1;
                if(guards && guards.WitnessesPlayerPursuit(actor))
                    worldStore.RememberSceneEvent(actor.Resident.Id,"pursuit:"+population.SettlementId+":"+bucket,
                        NpcSceneEventKind.GuardPursuit,now,population.SettlementId,building,
                        "I saw a guard pursuing the player near "+place+". I do not know the reason from seeing the pursuit alone.",
                        subject:NpcDirectedRelationship.PlayerTargetId);
                string activity;
                if(TryClosedShopContext(actor,now,GameManager.Instance.TalkManager.GetBuildingNameForBuildingKey,out activity))
                    worldStore.RememberSceneEvent(actor.Resident.Id,"closed-shop:"+population.SettlementId+":"+bucket,
                        NpcSceneEventKind.ClosedShop,now,population.SettlementId,building,activity.Length>600?activity.Substring(0,600):activity);
                if(!interior || shopGeometry==null || !shopGeometry.IsTavern)continue;
                var seen=new List<string>();
                foreach(var other in sceneMemoryActors)
                {
                    if(other==actor || !other || !other.gameObject.activeInHierarchy || !other.Resident.IsAlive ||
                        !SeesSceneEvent(actor,other.transform.position,8))continue;
                    seen.Add(other.Person.NameNPC);if(seen.Count==5)break;
                }
                if(seen.Count>0)
                    worldStore.RememberSceneEvent(actor.Resident.Id,"company:"+population.SettlementId+":"+building+":"+bucket,
                        NpcSceneEventKind.Company,now,population.SettlementId,building,
                        "At "+place+" I noticed "+string.Join(", ",seen.ToArray())+" nearby. Their presence does not mean we spoke.");
            }
        }
        internal static bool SeesSceneEvent(ResidentActor observer,Vector3 target,float range)
        {
            if(!observer || observer.IsSleeping)return false;
            return SeesSceneEvent(observer.transform,target,range);
        }
        internal static bool SeesSceneEvent(Transform observer,Vector3 target,float range,Transform observedObject=null)
        {
            if(!observer)return false;
            var delta=target-observer.position;
            var pose=observer.GetComponent<DirectionalKeeperPose>();
            return delta.sqrMagnitude<=range*range && Vector3.Dot(pose?pose.Heading:observer.forward,delta.normalized)>=0 &&
                CivilianResponses.Sees(observer.position,target,range,observedObject);
        }
    }
    public sealed partial class GuardPopulation
    {
        internal bool WitnessesPlayerPursuit(ResidentActor observer)
        {return observer && !observer.IsSleeping && WitnessesPlayerPursuit(observer.transform);}
        internal bool WitnessesPlayerPursuit(Transform observer)
        {
            var game=GameManager.Instance;
            if(game.PlayerEntity.IsInvisible || !StreetPopulation.SeesSceneEvent(observer,game.PlayerMotor.transform.position,20))return false;
            foreach(var guard in actors.Values)
            {
                if(!guard || !guard.gameObject.activeInHierarchy)continue;
                var motor=guard.GetComponent<EnemyMotor>();var senses=guard.GetComponent<EnemySenses>();
                if(motor && motor.IsHostile && senses && senses.Target==game.PlayerEntityBehaviour && senses.DetectedTarget &&
                    StreetPopulation.SeesSceneEvent(observer,guard.transform.position,20))return true;
            }
            return false;
        }
    }
}
