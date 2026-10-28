using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // A real player killing of one naturally placed partner. Normal native
    // witnessing must create grief and correctly attribute the actual player.
    internal static class HouseholdGriefNativeProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            if(!host || !streets || !NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) || game.PlayerEntity.IsInvisible)
                throw new InvalidOperationException("Native family grief needs the visible player in the owned disposable fixture.");
            if(game.IsPlayerInside){game.PlayerEnterExit.TransitionExterior();yield return new WaitForSecondsRealtime(1);}
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var store=(WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",Private).GetValue(host);
            Household family=null;string first=null,second=null;double selectedAt=0;
            float deadline=Time.realtimeSinceStartup+120;
            while(family==null && Time.realtimeSinceStartup<deadline)
            {
                if(streets.Ready && streets.EntranceSurveyReady && directory)
                {
                    double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    var town=store.CurrentPopulation(directory.MapID);
                    if(town!=null)for(double at=Math.Max(now+1,Math.Floor(now/86400d)*86400+7*3600);at<Math.Floor(now/86400d)*86400+17.5*3600 && family==null;at+=300)
                        foreach(var home in town.Households)
                        {
                            BuildingSummary summary;
                            if(!directory.GetBuildingSummary(home.BuildingKey,out summary) || summary.FactionId!=0 ||
                                summary.BuildingType<DFLocation.BuildingTypes.House1 || summary.BuildingType>DFLocation.BuildingTypes.House6)continue;
                            foreach(var a in home.Residents)
                            {
                                var b=home.Residents.Find(x=>x.Id==a.PartnerId && x.PartnerId==a.Id && x.IsAlive && x.AgeGroup==ResidentAgeGroup.Adult);
                                if(!a.IsAlive || a.AgeGroup!=ResidentAgeGroup.Adult || a.IsQuestProtected || b==null || b.IsQuestProtected ||
                                    !streets.HouseholdParentAvailable(a.Id,at) || !streets.HouseholdParentAvailable(b.Id,at))continue;
                                var sa=streets.GetState(a.Id,at);var sb=streets.GetState(b.Id,at);
                                if(sa.Activity!=Activity.Home || sb.Activity!=Activity.Home || sa.BuildingKey!=home.BuildingKey || sb.BuildingKey!=home.BuildingKey)continue;
                                family=home;first=a.Id;second=b.Id;selectedAt=at;break;
                            }
                            if(family!=null)break;
                        }
                }
                if(family==null)yield return new WaitForSecondsRealtime(.5f);
            }
            if(family==null)throw new InvalidOperationException("No actual reciprocal parents have a normal awake daylight home interval for grief observation.");
            double current=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if(selectedAt>current)DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(selectedAt-current));
            if(streets.HouseholdRoutineLocked(family.BuildingKey)!=false)
                throw new InvalidOperationException("Native family grief home is not actually open with its awake parents.");
            HouseholdInnPhysicalProbe.ActivateFamilyHome(directory,family.BuildingKey,true);yield return null;
            var greeting=DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;if(greeting!=null)greeting.CloseWindow();
            deadline=Time.realtimeSinceStartup+45;ResidentActor aBody=null,bBody=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                var room=game.PlayerEnterExit.Interior;aBody=streets.FindActor(first);bBody=streets.FindActor(second);
                if(room && room.EntryDoor.buildingKey==family.BuildingKey && Awake(aBody,room) && Awake(bBody,room))break;
                yield return null;
            }
            var homeRoom=game.PlayerEnterExit.Interior;
            if(!homeRoom || homeRoom.EntryDoor.buildingKey!=family.BuildingKey || !Awake(aBody,homeRoom) || !Awake(bBody,homeRoom))
                throw new InvalidOperationException("Native grief did not obtain the two normal existing awake parent bodies.");
            ResidentActor victim=null,survivor=null;var geometry=new InteriorGeometry(homeRoom,CrowdMotion.BodyRadius);
            foreach(var observer in new[]{aBody,bBody})
            {
                var other=observer==aBody?bBody:aBody;
                if(!StreetPopulation.SeesSceneEvent(observer,other.transform.position,20))continue;
                foreach(var anchor in geometry.LocalAnchors)
                {
                    var player=homeRoom.transform.TransformPoint(anchor)+Vector3.up;
                    float distance=Vector3.Distance(player,other.transform.position);
                    if(distance<1 || distance>3 || !StreetPopulation.SeesSceneEvent(observer,player,20))continue;
                    game.PlayerMotor.transform.position=player;Physics.SyncTransforms();victim=other;survivor=observer;break;
                }
                if(victim)break;
            }
            if(!victim || !survivor || !StreetPopulation.SeesSceneEvent(survivor,victim.transform.position,20) ||
                !StreetPopulation.SeesSceneEvent(survivor,game.PlayerMotor.transform.position,20))
                throw new InvalidOperationException("Actual parent placement has no normal direct view of both partner and visible attacking player.");
            string lost=victim.Resident.Id,observerId=survivor.Resident.Id;
            if(store.KnowsResidentDeath(observerId,lost) || store.BereavementFor(survivor.Resident,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())!=null)
                throw new InvalidOperationException("Living actual partner already had a death/grief record before the native attack.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_GRIEF_FIXTURE: home="+family.BuildingKey+"; victim="+lost+
                "; observer="+observerId+"; actualHomePair=true; observerMovedOnly=true; controlledPlayerAttack=true; injectedNpcPlacement=false");
            // The existing native mobile-NPC weapon path performs the actual
            // death. No IsAlive assignment, corpse or witness receipt is injected.
            game.WeaponManager.WeaponDamage(null,false,false,victim.transform,victim.transform.position,
                (victim.transform.position-game.PlayerMotor.transform.position).normalized);
            deadline=Time.realtimeSinceStartup+5;CivilianIncident incident=null;Bereavement grief=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                incident=store.PersistentMurderAlarm(observerId)??store.ActiveAlarm(observerId,now);
                if(incident!=null && (incident.VictimId!=lost || incident.Corpse==null))incident=null;
                grief=store.BereavementFor(store.ResolveResident(observerId),now);
                if(incident!=null && grief!=null)break;
                yield return null;
            }
            double observedAt=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var receipt=incident==null?null:incident.People.Find(x=>x.ResidentId==observerId);
            var rows=(List<NpcSceneMemory>)typeof(WorldStateStore).GetMethod("SceneMemoriesFor",Private).Invoke(store,new object[]{observerId,false});
            var memories=rows==null?new List<NpcSceneMemory>():rows.FindAll(x=>x.SubjectId==lost && x.Kind==NpcSceneEventKind.Bereavement);
            if(victim.Resident.IsAlive || store.ResolveResident(lost).IsAlive || incident==null || !incident.PlayerAttributed ||
                receipt==null || receipt.DeathKnowledge!=KnownDeathEvidence.Witness || !receipt.BereavementRecorded ||
                !receipt.DirectWitness || receipt.DeathKnownAt<incident.Corpse.DiedAt || receipt.DeathKnownAt>observedAt ||
                grief==null || grief.ResidentId!=lost || !grief.WitnessedPlayerKilling || grief.Relationship!="partner" ||
                !store.KnowsResidentDeath(observerId,lost) || !store.HoldsPartnerGrudge(store.ResolveResident(observerId),observedAt) ||
                memories.Count!=1 || memories[0].ResponsibleId!=NpcDirectedRelationship.PlayerTargetId)
                throw new InvalidOperationException("Actual player killing did not create exact partner witness knowledge, attributed grief and the existing player grudge.");
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
            var restoredGrief=restored.BereavementFor(restored.ResolveResident(observerId),observedAt);
            var retained=restored.Snapshot().SceneMemories.FindAll(x=>x.ObserverId==observerId && x.SubjectId==lost && x.Kind==NpcSceneEventKind.Bereavement);
            if(restoredGrief==null || !restoredGrief.WitnessedPlayerKilling || restoredGrief.KnownAt!=grief.KnownAt ||
                !restored.HoldsPartnerGrudge(restored.ResolveResident(observerId),observedAt) || retained.Count!=1 ||
                retained[0].EventId!=memories[0].EventId || retained[0].At!=memories[0].At || retained[0].ResponsibleId!=NpcDirectedRelationship.PlayerTargetId)
                throw new InvalidOperationException("Native serializer lost exact actual partner death knowledge, grief, attribution or deduplication.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_GRIEF_NATIVE_PASS: actualHomePair=true; actualNativePlayerAttack=true; actualPartnerDeath=true; "+
                "directWitness=true; knowledgeBound=true; grief=true; playerBlame=true; playerGrudge=true; exactMemoryOnce=true; nativeSerializer=true; "+
                "injectedCorpse=false; injectedWitness=false; forcedGreeting=false; controlledClock=true");
        }
        private static bool Awake(ResidentActor actor,DaggerfallInterior room)
        {return actor && actor.Resident.IsAlive && actor.gameObject.activeInHierarchy && actor.transform.parent==room.transform && !actor.IsSleeping;}
    }
}
