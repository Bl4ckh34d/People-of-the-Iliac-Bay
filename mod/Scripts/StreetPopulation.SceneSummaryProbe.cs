using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Opt-in controlled native observation/serialization probe. Recall is
        // evaluated one day later without advancing the actual world clock.
        internal IEnumerator VerifySceneMemorySummaries()
        {
            bool mobile=false,stationary=false;float deadline=Time.realtimeSinceStartup+90;
            while((!mobile || !stationary) && Time.realtimeSinceStartup<deadline)
            {
                SurveyStationaryObservers();double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                if(!stationary)foreach(var entry in stationaryParticipants.Values)
                {
                    if(!StaticChatEligible(entry,now))continue;
                    if(!VerifySceneSummaryBody(entry.Npc.gameObject,entry.Person.Id,world=>
                        NativeNpcSceneObserver.BindTo(entry.Npc,entry.Person,world,this),"stationary"))continue;
                    stationary=true;break;
                }
                if(!mobile)foreach(var body in actors.Values)
                {
                    if(!body || !body.gameObject.activeInHierarchy || body.IsSleeping || !body.Resident.IsAlive)continue;
                    if(!VerifySceneSummaryBody(body.gameObject,body.Resident.Id,world=>{
                        NativeNpcSceneObserver.BindTo(body.gameObject,world);return body.GetComponent<NativeNpcSceneObserver>();},"mobile"))continue;
                    mobile=true;break;
                }
                if(!mobile || !stationary)yield return null;
            }
            if(!mobile || !stationary)throw new InvalidOperationException("Scene summary probe needs visible awake mobile and stationary bodies.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SCENE_SUMMARY_PASS: mobile=true; stationary=true; actualHealthObservation=true; nativeSerializer=true; controlledLaterRecall=true; worldClockUnchanged=true; fullDiskSaveAcceptance=false");
        }

        // The biography acceptance already owns a real home and a real disk
        // roundtrip. Share that run instead of adding another save/load fixture.
        internal NpcSceneMemory CaptureInteriorSceneMemoryForDiskProbe(ResidentActor body)
        {
            var game=GameManager.Instance;
            if(Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")!="dialogue-biography" ||
                !NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) || !game.PlayerEnterExit.IsPlayerInsideBuilding ||
                !body || !body.gameObject.activeInHierarchy || body.Resident==null || !body.Resident.IsAlive || body.IsSleeping ||
                worldStore.ResolveResident(body.Resident.Id)==null)
                throw new InvalidOperationException("Interior memory evidence needs the actual biography home and an awake existing resident.");
            var player=game.PlayerEntity;var position=game.PlayerMotor.transform.position;
            int health=player.CurrentHealth;bool god=player.GodMode;var concealment=player.MagicalConcealmentFlags;
            NativeNpcSceneObserver.BindTo(body.gameObject,worldStore);
            var observer=body.GetComponent<NativeNpcSceneObserver>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var update=typeof(NativeNpcSceneObserver).GetMethod("Update",flags);
            var sampleAt=typeof(NativeNpcSceneObserver).GetField("nextSample",flags);
            var before=worldStore.Snapshot().SceneMemories.FindAll(value=>value.ObserverId==body.Resident.Id);
            try
            {
                if(GameManager.IsGamePaused || !game.IsPlayingGame())
                    throw new InvalidOperationException("Interior memory producer needs ordinary HUD gameplay after biography Talk closes.");
                player.GodMode=false;player.MagicalConcealmentFlags=MagicalConcealmentFlags.None;
                var heading=body.transform.forward;heading.y=0;heading.Normalize();
                var front=body.transform.position+heading*2;
                game.PlayerMotor.transform.position=front;Physics.SyncTransforms();
                if(!observer.Sees(front,20))
                    throw new InvalidOperationException("Actual home resident cannot see the controlled health observation position.");
                player.SetHealth(player.MaxHealth,true);sampleAt.SetValue(observer,0f);update.Invoke(observer,null);
                player.SetHealth(player.MaxHealth-2,true);sampleAt.SetValue(observer,0f);update.Invoke(observer,null);
                var fact=worldStore.Snapshot().SceneMemories.FindLast(value=>value.ObserverId==body.Resident.Id &&
                    value.Kind==NpcSceneEventKind.Injury && !before.Exists(old=>old.EventId==value.EventId));
                if(fact==null || fact.SubjectId!=NpcDirectedRelationship.PlayerTargetId || fact.ResponsibleId!=null ||
                    fact.BuildingKey!=game.PlayerEnterExit.Interior.EntryDoor.buildingKey ||
                    fact.SettlementId!=game.PlayerGPS.CurrentMapID || string.IsNullOrEmpty(fact.Observation))
                    throw new InvalidOperationException("Actual interior health producer lost recipient, subject, unknown cause or native place.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INTERIOR_MEMORY_PRODUCER_PASS: person="+fact.ObserverId+"; source="+fact.EventId+
                    "; building="+fact.BuildingKey+"; at="+fact.At+"; valence="+fact.Valence+
                    "; actualHealthObservation=true; actualStore=true; causeUnknown=true; sharedBiographyDiskRun=true");
                return fact;
            }
            finally
            {
                player.SetHealth(health,true);player.GodMode=god;player.MagicalConcealmentFlags=concealment;
                game.PlayerMotor.transform.position=position;Physics.SyncTransforms();
            }
        }

        internal void VerifyInteriorSceneMemoryAfterDiskProbe(NpcSceneMemory expected)
        {
            var game=GameManager.Instance;
            var actual=expected==null?null:worldStore.Snapshot().SceneMemories.Find(value=>
                value.ObserverId==expected.ObserverId && value.EventId==expected.EventId);
            if(actual==null || actual.Kind!=expected.Kind || actual.SubjectId!=expected.SubjectId || actual.ResponsibleId!=null ||
                actual.SettlementId!=expected.SettlementId || actual.BuildingKey!=expected.BuildingKey || actual.At!=expected.At ||
                actual.Valence!=expected.Valence || actual.Observation!=expected.Observation || actual.ExpiresAt!=expected.ExpiresAt ||
                !game.PlayerEnterExit.IsPlayerInsideBuilding || game.PlayerEnterExit.Interior.EntryDoor.buildingKey!=expected.BuildingKey ||
                !worldStore.RecallSceneEvents(expected.ObserverId,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),2000).Contains(expected.Observation))
                throw new InvalidOperationException("Native disk load changed the actual interior event or lost its relevant recall.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INTERIOR_MEMORY_DISK_PASS: person="+actual.ObserverId+"; source="+actual.EventId+
                "; building="+actual.BuildingKey+"; nativeDiskReload=true; exactProvenance=true; exactValence=true; causeUnknown=true; recalled=true; sharedBiographyDiskRun=true");
        }

        private bool VerifySceneSummaryBody(GameObject body,string id,Func<WorldStateStore,NativeNpcSceneObserver> bind,string kind)
        {
            var game=GameManager.Instance;var player=game.PlayerEntity;
            var position=game.PlayerMotor.transform.position;int health=player.CurrentHealth;
            bool god=player.GodMode;var concealment=player.MagicalConcealmentFlags;
            var snapshot=worldStore.Snapshot();snapshot.SceneMemories.RemoveAll(x=>x.ObserverId==id);
            snapshot.SceneMemorySummaries.RemoveAll(x=>x.ObserverId==id);
            var scratch=new WorldStateStore(1);scratch.Restore(snapshot);var observer=bind(scratch);
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var update=typeof(NativeNpcSceneObserver).GetMethod("Update",flags);
            var sampleAt=typeof(NativeNpcSceneObserver).GetField("nextSample",flags);
            Action sample=()=>{sampleAt.SetValue(observer,0f);update.Invoke(observer,null);};
            try
            {
                player.GodMode=false;player.MagicalConcealmentFlags=MagicalConcealmentFlags.None;
                var pose=body.GetComponent<DirectionalKeeperPose>();var heading=pose?pose.Heading:body.transform.forward;
                heading.y=0;heading.Normalize();var front=body.transform.position+heading*3;
                game.PlayerMotor.transform.position=front;Physics.SyncTransforms();
                if(!observer.Sees(front,20))return false;
                player.SetHealth(player.MaxHealth,true);sample();
                player.SetHealth(player.MaxHealth-2,true);sample();
                var fact=scratch.Snapshot().SceneMemories.FindLast(x=>x.ObserverId==id && x.Kind==NpcSceneEventKind.Injury);
                if(fact==null || fact.SubjectId!=NpcDirectedRelationship.PlayerTargetId || fact.ResponsibleId!=null)
                    throw new InvalidOperationException(kind+" lacks a genuinely observed injury with unknown cause.");
                double later=fact.At+86401;
                string recalled=scratch.RecallSceneEvents(id,later,2000);
                var compact=scratch.Snapshot();
                var summary=compact.SceneMemorySummaries.Find(x=>x.ObserverId==id && x.Kind==NpcSceneEventKind.Injury);
                if(summary==null || summary.Sources.Count!=1 || summary.Sources[0].EventId!=fact.EventId ||
                    summary.Sources[0].At!=fact.At || summary.ResponsibleId!=null || summary.Valence!=fact.Valence ||
                    summary.SettlementId!=fact.SettlementId || summary.BuildingKey!=fact.BuildingKey || !recalled.Contains(fact.Observation))
                    throw new InvalidOperationException(kind+" summary changed observed evidence.");
                // Native DFU serializer, rather than a test JSON substitute.
                var encoded=SaveLoadManager.Serialize(typeof(WorldState),compact);
                var restored=new WorldStateStore(1);
                restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),encoded));
                if(restored.RecallSceneEvents(id,later,2000)!=recalled ||
                    restored.RecallSceneEvents(id,fact.ExpiresAt,2000).Contains(fact.Observation))
                    throw new InvalidOperationException(kind+" summary failed native serialization or original retention.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SCENE_SUMMARY_BODY_PASS: kind="+kind+"; person="+id+
                    "; source="+fact.EventId+"; at="+fact.At+"; expires="+fact.ExpiresAt+"; causeUnknown=true; nativeSerializer=true");
                return true;
            }
            finally
            {
                player.SetHealth(health,true);player.GodMode=god;player.MagicalConcealmentFlags=concealment;
                game.PlayerMotor.transform.position=position;Physics.SyncTransforms();bind(worldStore);
            }
        }
    }
}
