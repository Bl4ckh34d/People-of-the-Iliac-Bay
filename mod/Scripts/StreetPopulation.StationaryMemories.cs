using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class StationaryParticipant
        {
            internal StaticNPC Npc;
            internal NativePerson Person;
            internal NativeNpcSceneObserver Observer;
        }
        private readonly Dictionary<string,StationaryParticipant> stationaryParticipants=new Dictionary<string,StationaryParticipant>();
        private Transform stationaryScene;
        private float nextStationarySurvey,nextStationaryMemory;
        private int stationaryMemoryCursor;
        private readonly List<StationaryParticipant> stationaryMemoryScratch=new List<StationaryParticipant>();

        private void BindStationaryObserver(StaticNPC npc,NativePerson person)
        {
            var observer=NativeNpcSceneObserver.BindTo(npc,person,worldStore,this);
            StationaryParticipant entry;
            if(!stationaryParticipants.TryGetValue(person.Id,out entry))
                stationaryParticipants.Add(person.Id,entry=new StationaryParticipant());
            entry.Npc=npc;entry.Person=person;entry.Observer=observer;
        }
        internal bool StaticObserverAwake(StaticNPC npc,NativePerson person)
        {
            if(!npc || !npc.gameObject.activeInHierarchy || person==null || !person.IsAlive || person.RetiredBedroomFlat || worldStore==null)return false;
            var sleeping=npc.GetComponent<SleepingPose>();
            return !(sleeping && sleeping.Active) && !KeeperSleeping(person,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
        }
        private void SurveyStationaryObservers()
        {
            if(!directory || worldStore==null || population==null)return;
            Transform scene=interior?interior.transform:directory.transform;
            if(stationaryScene==scene && Time.unscaledTime<nextStationarySurvey)return;
            if(stationaryScene!=scene){stationaryParticipants.Clear();stationaryScene=scene;}
            nextStationarySurvey=Time.unscaledTime+30;
            foreach(var npc in scene.GetComponentsInChildren<StaticNPC>(true))
            {
                if(!RDBLayout.IsNPCFlat(npc.Data.billboardArchiveIndex) || replacedHouseFlats.ContainsKey(npc.gameObject) ||
                    npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>())continue;
                var data=npc.Data;
                if(interior && (data.context!=StaticNPC.Context.Building || data.buildingKey!=interior.EntryDoor.buildingKey))continue;
                int key=interior?data.buildingKey:-1;
                int hash=interior?data.hash:NativeExteriorNpcIdentity.Hash(npc,directory.transform);
                var person=worldStore.RegisterNativePerson(population.SettlementId,key,hash,npc.DisplayName,
                    data.gender==Genders.Female?ResidentSex.Female:ResidentSex.Male,(int)data.race,data.factionID,
                    data.billboardArchiveIndex,data.billboardRecordIndex);
                if(!person.RetiredBedroomFlat)BindStationaryObserver(npc,person);
            }
        }
        private void ObserveStationaryMemories(double now)
        {
            if(GameManager.IsGamePaused || Time.unscaledTime<nextStationaryMemory || !BackgroundWork.TryStart())return;
            nextStationaryMemory=Time.unscaledTime+1;
            stationaryMemoryScratch.Clear();stationaryMemoryScratch.AddRange(stationaryParticipants.Values);
            var guards=GetComponent<GuardPopulation>();
            string bucket=((long)(now/600)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            for(int i=0;i<Math.Min(4,stationaryMemoryScratch.Count);i++)
            {
                if(stationaryMemoryCursor>=stationaryMemoryScratch.Count)stationaryMemoryCursor=0;
                var witness=stationaryMemoryScratch[stationaryMemoryCursor++];
                if(!StaticObserverAwake(witness.Npc,witness.Person))continue;
                if(guards && guards.WitnessesPlayerPursuit(witness.Npc.transform))
                    NativeSceneRecollection.Remember(worldStore,witness.Person.Id,"pursuit:"+population.SettlementId+":"+bucket,
                        NpcSceneEventKind.GuardPursuit,"I saw a guard pursuing the player. I do not know why.",subject:NpcDirectedRelationship.PlayerTargetId);
                if(!interior || shopGeometry==null || !shopGeometry.IsTavern)continue;
                var seen=new List<string>();
                foreach(var actor in actors.Values)
                    if(actor && actor.Resident.IsAlive && actor.gameObject.activeInHierarchy && witness.Observer.Sees(actor.transform.position,8))
                    {seen.Add(actor.Person.NameNPC);if(seen.Count==5)break;}
                foreach(var other in stationaryMemoryScratch)
                    if(seen.Count<5 && other!=witness && StaticObserverAwake(other.Npc,other.Person) && witness.Observer.Sees(other.Npc.transform.position,8))
                        seen.Add(other.Person.Name);
                if(seen.Count>0)NativeSceneRecollection.Remember(worldStore,witness.Person.Id,
                    "company:"+population.SettlementId+":"+interior.EntryDoor.buildingKey+":"+bucket,NpcSceneEventKind.Company,
                    "I noticed "+string.Join(", ",seen.ToArray())+" nearby. Their presence does not mean we spoke.",0);
            }
        }
    }
}
