using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public static class CitizenThreatProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            yield return CheckKeeper();
            game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(1);
            var mod=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>(); var streets=mod.GetComponent<StreetPopulation>(); var guards=mod.GetComponent<GuardPopulation>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",flags).GetValue(mod);
            var plans=(IEnumerable)typeof(StreetPopulation).GetField("plans",flags).GetValue(streets);
            object selected=null; Resident resident=null; ResidentRoutine routine=null;
            foreach(var candidate in plans)
            {
                var person=(Resident)candidate.GetType().GetField("Resident").GetValue(candidate);
                var route=(ResidentRoutine)candidate.GetType().GetField("Routine").GetValue(candidate);
                if(person.IsAlive && !person.IsQuestProtected && person.NativeIdentity==null && person.AgeGroup==ResidentAgeGroup.Adult && route.Route.Count>=2)
                { selected=candidate; resident=person; routine=route; break; }
            }
            if(selected==null) throw new InvalidOperationException("No travelling citizen for threat fixture.");
            var points=new List<RoutePoint>(); foreach(var p in routine.Route) points.Add(new RoutePoint(p.X,p.Z));
            var schedule=DailySchedule.Workday(resident.HomeKey,routine.DestinationKey,points,545,1080,5,360,1320);
            var field=selected.GetType().GetField("Schedule"); var original=field.GetValue(selected); bool defense=guards.EnemyDefense;
            GameObject enemy=null;
            try
            {
                field.SetValue(selected,schedule); guards.EnemyDefense=false;
                game.PlayerEntity.GodMode=true; game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),time=Math.Floor(now/86400)*86400+542*60;
                if(time<=now) time+=86400;
                var geometry=new TownGeometry(game.StreamingWorld.GetCurrentBuildingDirectory(),game.StreamingWorld.GetCurrentCityNavigation(),false);
                game.PlayerMotor.transform.position=geometry.ScenePoint(schedule.EvaluateSeconds(time).Position)+Vector3.up*2+Vector3.right*8;
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(time-now)); DaggerfallUI.Instance.PopToHUD();
                float deadline=Time.realtimeSinceStartup+10; ResidentActor actor=null;
                while(Time.realtimeSinceStartup<deadline) { actor=streets.FindActor(resident.Id); if(actor && actor.gameObject.activeInHierarchy) break; yield return null; }
                if(!actor || !actor.gameObject.activeInHierarchy) throw new InvalidOperationException("Threat fixture citizen did not render.");
                Vector3 before=actor.transform.position;
                enemy=GameObjectHelper.CreateFoeGameObjects(before+Vector3.right*2,MobileTypes.Thief,1,MobileReactions.Passive)[0];
                enemy.SetActive(true); yield return null;
                enemy.GetComponent<EnemyMotor>().IsHostile=true; enemy.GetComponent<EnemyMotor>().enabled=false;
                enemy.GetComponent<EnemySenses>().enabled=false; enemy.GetComponent<EnemyAttack>().enabled=false;
                CivilianIncident alarm=null; deadline=Time.realtimeSinceStartup+5;
                while(Time.realtimeSinceStartup<deadline)
                {
                    alarm=store.ActiveAlarm(resident.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                    if(alarm!=null && alarm.ThreatId!=null) break; yield return null;
                }
                if(alarm==null || alarm.ThreatId==null || alarm.VictimId!=null) throw new InvalidOperationException("Hostile sighting did not create a non-murder alarm.");
                yield return new WaitForSecondsRealtime(1.5f);
                var person=alarm.People.Find(p=>p.ResidentId==resident.Id);
                if(person.Reaction!=CivilianReaction.Flee || (!person.ReachedRefuge && (!actor || Vector3.Distance(before,actor.transform.position)<0.5f)))
                    throw new InvalidOperationException("Citizen did not physically flee or reach home.");
                if(game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None) throw new InvalidOperationException("Threat response accused the innocent player.");
                var json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
                var restored=new WorldStateStore(42); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
                if(restored.ActiveAlarm(resident.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).ThreatId!=alarm.ThreatId)
                    throw new InvalidOperationException("Native serializer lost citizen threat ownership.");
                Debug.Log("LIVING_CITIES_CITIZEN_THREAT_PASS: actual hostile sighting, physical flight/home refuge, no murder charge, native serialization; guards disabled; resident="+resident.Id);
            }
            finally { field.SetValue(selected,original); guards.EnemyDefense=defense; if(enemy) UnityEngine.Object.Destroy(enemy); }
        }
        private static IEnumerator CheckKeeper()
        {
            var game=GameManager.Instance; var room=game.PlayerEnterExit.Interior;
            var mod=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>(); var guards=mod.GetComponent<GuardPopulation>();
            var store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mod);
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
            game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None; DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
            NativePerson keeper=null; StaticNPC npc=null;
            foreach(var person in store.NativePeopleFor(game.PlayerGPS.CurrentLocation.MapTableData.MapId))
            {
                if(!person.IsAlive || person.WorkKey!=room.EntryDoor.buildingKey) continue;
                foreach(var candidate in room.GetComponentsInChildren<StaticNPC>())
                    if(candidate.Data.hash==person.NativeHash) { keeper=person; npc=candidate; break; }
                if(npc) break;
            }
            if(!npc) throw new InvalidOperationException("Native keeper threat fixture absent.");
            Vector3 position=npc.transform.position+Vector3.right*1.5f;
            for(int direction=0;direction<8;direction++)
            {
                float angle=direction*Mathf.PI/4;
                var candidate=npc.transform.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*1.5f;
                if(CivilianResponses.Sees(npc.transform.position,candidate,20)) { position=candidate; break; }
            }
            var enemy=GameObjectHelper.CreateFoeGameObjects(position,MobileTypes.Thief,1,MobileReactions.Passive)[0];
            bool defense=guards.EnemyDefense; guards.EnemyDefense=false;
            try
            {
                enemy.SetActive(true); yield return null;
                enemy.GetComponent<EnemyMotor>().IsHostile=true; enemy.GetComponent<EnemyMotor>().enabled=false;
                enemy.GetComponent<EnemySenses>().enabled=false; enemy.GetComponent<EnemyAttack>().enabled=false;
                Vector3 before=npc.transform.position;
                float deadline=Time.realtimeSinceStartup+8; CivilianIncident alarm=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    alarm=store.NativeResponse(keeper.Id);
                    if(alarm!=null && alarm.ThreatId!=null && (!npc.gameObject.activeInHierarchy || Vector3.Distance(before,npc.transform.position)>0.5f)) break;
                    yield return null;
                }
                if(alarm==null || alarm.ThreatId==null || alarm.People.Find(p=>p.ResidentId==keeper.Id).Reaction!=CivilianReaction.Flee ||
                    (npc.gameObject.activeInHierarchy && Vector3.Distance(before,npc.transform.position)<=0.5f))
                    throw new InvalidOperationException("Native keeper did not flee a visible hostile foe.");
                if(game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None) throw new InvalidOperationException("Keeper threat response accused player.");
                Debug.Log("LIVING_CITIES_KEEPER_THREAT_PASS: native registered merchant sees hostile foe and physically flees without blaming player.");
                yield return CheckIndoorDefender(enemy,room.transform,room.EntryDoor.buildingKey);
            }
            finally { guards.EnemyDefense=defense; UnityEngine.Object.Destroy(enemy); }
        }
        private static IEnumerator CheckIndoorDefender(GameObject enemy,Transform room,int buildingKey)
        {
            var game=GameManager.Instance;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var roster=GuardRoster.Create(1,42,1,new[] { new SavedRoutePoint() });
            string id=roster.Guards[0].Id;
            roster.CacheBuildingApproach(buildingKey,new SavedRoutePoint { X=1 },
                new Dictionary<string,List<SavedRoutePoint>> { { id,new List<SavedRoutePoint> { new SavedRoutePoint(),new SavedRoutePoint { X=1 } } } });
            roster.DispatchBuilding("indoor-defense-probe",buildingKey,now-20,1,2,12,threatId:"probe:enemy");
            Vector3 local=room.InverseTransformPoint(enemy.transform.position+Vector3.forward*1.5f);
            roster.EnterBuilding(id,buildingKey,now,new GuardPoint3 { X=local.x,Y=local.y,Z=local.z });
            var target=enemy.GetComponent<DaggerfallEntityBehaviour>();
            target.Entity.MaxHealth=500; target.Entity.SetHealth(500,true);
            GuardActor actor=null;
            try
            {
                actor=GuardActor.CreateInterior(roster,id,room,now);
                actor.BeginDefenseCombat(target,now);
                float deadline=Time.realtimeSinceStartup+15;
                while(Time.realtimeSinceStartup<deadline && target.Entity.CurrentHealth==500)
                { actor.Capture(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()); yield return null; }
                if(!actor.CombatReady || target.Entity.CurrentHealth>=500)
                    throw new InvalidOperationException("Indoor guard did not engage and damage hostile enemy.");
                if(game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None || actor.GetComponent<EnemyMotor>().IsHostile ||
                    actor.GetComponent<EnemySenses>().Target!=target)
                    throw new InvalidOperationException("Indoor defender targeted innocent player.");
                var copy=GuardRoster.CopyValidated(roster);
                if(copy.Guards[0].Interior.LastSeenSuspect==null || copy.Reports[0].ThreatId!="probe:enemy")
                    throw new InvalidOperationException("Indoor enemy observation did not persist.");
                Debug.Log("LIVING_CITIES_INDOOR_DEFENDER_PASS: actual indoor native combat damages enemy, preserves interior target and leaves player innocent.");
            }
            finally { if(actor) { actor.EndCombat(); UnityEngine.Object.Destroy(actor.gameObject); } }
        }
    }
}
