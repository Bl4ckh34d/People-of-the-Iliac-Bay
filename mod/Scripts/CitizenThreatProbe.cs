using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using DaggerfallWorkshop.Game.MagicAndEffects;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public static class CitizenThreatProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            CheckRetreatCorridor(game.PlayerMotor.transform.position+Vector3.up*100);
            yield return CheckKeeper();
            game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(1);
            var mod=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>(); var streets=mod.GetComponent<StreetPopulation>(); var guards=mod.GetComponent<GuardPopulation>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var store=(WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",flags).GetValue(mod);
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
                var homeward=geometry.ScenePoint(schedule.EvaluateSeconds(time-2).Position)+Vector3.up-before; homeward.y=0;
                if(homeward.sqrMagnitude<0.001f) throw new InvalidOperationException("Threat fixture needs a moving route segment.");
                homeward.Normalize();
                // Attacker blocks the route home; innocent player stands on the safe side.
                game.PlayerMotor.transform.position=before-homeward*6;
                enemy=GameObjectHelper.CreateFoeGameObjects(before+homeward*2,MobileTypes.Thief,1,MobileReactions.Passive)[0];
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
                float initialGap=Vector3.Distance(actor.transform.position,enemy.transform.position);
                deadline=Time.realtimeSinceStartup+1.5f;
                while(Time.realtimeSinceStartup<deadline && actor && actor.gameObject.activeInHierarchy)
                {
                    if(Vector3.Distance(actor.transform.position,enemy.transform.position)<initialGap-0.2f)
                        throw new InvalidOperationException("Fleeing citizen moved toward the hostile instead of away.");
                    yield return null;
                }
                var person=alarm.People.Find(p=>p.ResidentId==resident.Id);
                if(person.Reaction!=CivilianReaction.Flee || (!person.ReachedRefuge && (!actor || Vector3.Distance(before,actor.transform.position)<0.5f)))
                    throw new InvalidOperationException("Citizen did not physically flee or reach home.");
                if(game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None) throw new InvalidOperationException("Threat response accused the innocent player.");
                if(actor && actor.gameObject.activeInHierarchy && Vector3.Distance(actor.transform.position,enemy.transform.position)<initialGap+0.5f)
                    throw new InvalidOperationException("Citizen failed to gain distance from the hostile blocking home: before="+before+
                        "; current="+actor.transform.position+"; enemy="+enemy.transform.position+"; initialGap="+initialGap);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FLIGHT_SAFETY_PASS: hostile blocks home, innocent player on opposite side; citizen increases threat clearance.");
                var json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
                var restored=new WorldStateStore(42,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
                if(restored.ActiveAlarm(resident.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).ThreatId!=alarm.ThreatId)
                    throw new InvalidOperationException("Native serializer lost citizen threat ownership.");
                // Controlled final approach exercises the real runtime arrival hook;
                // the hostile-blocking-home case above already verifies actual flight.
                var homePoint=routine.Route[0];
                person.X=homePoint.X;person.Z=homePoint.Z;
                var homeWorld=geometry.ScenePoint(new RoutePoint(homePoint.X,homePoint.Z));
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                person.Y=directory.transform.InverseTransformPoint(homeWorld).y;
                game.PlayerMotor.transform.position=homeWorld+Vector3.up*2+Vector3.right*5;
                yield return new WaitForSecondsRealtime(1);
                double arrived=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                BuildingSummary homeSummary;
                if(!person.ReachedRefuge || !person.RefugeSealed || !store.ShelterLocked(alarm.SettlementId,resident.HomeKey,arrived) ||
                    !directory.GetBuildingSummary(resident.HomeKey,out homeSummary) ||
                    game.PlayerObject.GetComponent<PlayerActivate>().BuildingIsUnlocked(homeSummary))
                    throw new InvalidOperationException("Physical home arrival did not seal the native shelter door.");
                var shelterJson=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
                restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),shelterJson));
                if(!restored.ShelterLocked(alarm.SettlementId,resident.HomeKey,arrived) ||
                    restored.ShelterLocked(alarm.SettlementId,resident.HomeKey,arrived+3600))
                    throw new InvalidOperationException("Native saved shelter lock lost expiry.");
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(3600);
                if(store.ShelterLocked(alarm.SettlementId,resident.HomeKey,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()))
                    throw new InvalidOperationException("Shelter remained sealed after one hour.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHELTER_LOCK_PASS: runtime home arrival, native locked door, native JSON and one-hour expiry.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CITIZEN_THREAT_PASS: actual hostile sighting, physical flight/home refuge, no murder charge, native serialization; guards disabled; resident="+resident.Id);
            }
            finally { field.SetValue(selected,original); guards.EnemyDefense=defense; if(enemy) UnityEngine.Object.Destroy(enemy); }
        }
        public static IEnumerator RunWelfare() { yield return CheckKeeper(true); }
        private static void CheckRetreatCorridor(Vector3 origin)
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                floor.transform.position=origin-Vector3.up*0.1f;floor.transform.localScale=new Vector3(12,0.2f,12);
                wall.transform.position=origin+new Vector3(2,1.5f,0);wall.transform.localScale=new Vector3(0.3f,3,3);
                wall.SetActive(false);Physics.SyncTransforms();Vector3 end;
                if(!CivilianFlightSteering.TryDirectRetreat(origin,origin+Vector3.right*4,out end))
                    throw new InvalidOperationException("Retreat corridor rejected a clear supported floor.");
                wall.SetActive(true);Physics.SyncTransforms();
                if(CivilianFlightSteering.TryDirectRetreat(origin,origin+Vector3.right*4,out end))
                    throw new InvalidOperationException("Retreat corridor crosses a solid wall.");
                wall.SetActive(false);floor.transform.localScale=new Vector3(3,0.2f,12);Physics.SyncTransforms();
                if(CivilianFlightSteering.TryDirectRetreat(origin,origin+Vector3.right*4,out end))
                    throw new InvalidOperationException("Retreat corridor crosses unsupported ground.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RETREAT_CORRIDOR_PASS: supported floor allowed; walls and drops rejected.");
            }
            finally
            {
                floor.SetActive(false);wall.SetActive(false);
                UnityEngine.Object.Destroy(floor);UnityEngine.Object.Destroy(wall);Physics.SyncTransforms();
            }
        }
        private static IEnumerator CheckKeeper(bool welfare=false)
        {
            var game=GameManager.Instance; var room=game.PlayerEnterExit.Interior;
            var mod=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>(); var guards=mod.GetComponent<GuardPopulation>();
            var store=(WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mod);
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
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_THREAT_PASS: native registered merchant sees hostile foe and physically flees without blaming player.");
                guards.EnemyDefense=true;
                var messenger=alarm.People.Find(p=>p.ResidentId==keeper.Id);
                deadline=Time.realtimeSinceStartup+10;
                while(messenger.Escape==null && Time.realtimeSinceStartup<deadline) yield return null;
                if(messenger.Escape==null) throw new InvalidOperationException("Keeper defense fixture has no checked escape to a guard.");
                clock.RaiseTime((float)Math.Max(0,messenger.Escape.ReportAt-clock.ToSeconds()+1));
                deadline=Time.realtimeSinceStartup+8; GuardReport defenseReport=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    defenseReport=guards.Roster.Reports.Find(r=>r.ThreatId==alarm.ThreatId && r.BuildingKey==room.EntryDoor.buildingKey && r.GuardIds.Count>0);
                    if(defenseReport!=null) break; yield return null;
                }
                if(defenseReport==null) throw new InvalidOperationException("Escaped keeper did not summon guards against indoor enemy.");
                if(game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None) throw new InvalidOperationException("Indoor enemy report blamed player.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_DEFENSE_REPORT_PASS: escaped native keeper contacts guard and summons finite building response against enemy.");
                var summoned=guards.Roster.Guards.Find(g=>g.Id==defenseReport.GuardIds[0]);
                if(summoned.ArrivesAt<=clock.ToSeconds() || summoned.Interior!=null)
                    throw new InvalidOperationException("Indoor defender skipped physical exterior travel.");
                var target=enemy.GetComponent<DaggerfallEntityBehaviour>(); target.Entity.MaxHealth=500; target.Entity.SetHealth(500,true);
                clock.RaiseTime((float)(summoned.ArrivesAt-clock.ToSeconds()+1));
                deadline=Time.realtimeSinceStartup+20; GuardActor responder=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    foreach(var candidate in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                        if(candidate.GuardId==summoned.Id && candidate.IsInterior) responder=candidate;
                    if(responder && responder.CombatReady && target.Entity.CurrentHealth<500) break;
                    yield return null;
                }
                if(!responder || !responder.CombatReady || target.Entity.CurrentHealth>=500 || responder.DefenseTarget!=target)
                    throw new InvalidOperationException("Summoned guard did not enter shop and damage reported enemy; duty="+summoned.Duty+
                        "; interior="+(summoned.Interior!=null)+"; actor="+(responder!=null)+"; combat="+(responder && responder.CombatReady)+
                        "; health="+target.Entity.CurrentHealth+"; sight="+(responder && CivilianResponses.Sees(responder.transform.position,target.transform.position,20))+
                        "; distance="+(responder ? Vector3.Distance(responder.transform.position,target.transform.position) : -1)+
                        "; known="+(summoned.Interior!=null && summoned.Interior.LastSeenSuspect!=null)+"; alarmLocation="+alarm.HasLocation);
                if(game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None || responder.GetComponent<EnemyMotor>().IsHostile)
                    throw new InvalidOperationException("Summoned defender attacked innocent player.");
                if(welfare)
                {
                    yield return WelfareEncounterProbe.Finish(store,guards,responder,summoned,defenseReport,target);
                    yield break;
                }
                // Exercise native assault notification while the guard already has an enemy target.
                var originalThreat=defenseReport.ThreatId;
                bool assaultVisible=false;
                foreach(var anchor in new InteriorGeometry(room).LocalAnchors)
                {
                    var spot=room.transform.TransformPoint(anchor)+Vector3.up;
                    float distance=Vector3.Distance(responder.transform.position,spot);
                    if(distance<2 || distance>8 || !CivilianResponses.Sees(responder.transform.position,spot,20)) continue;
                    game.PlayerMotor.transform.position=spot; assaultVisible=true; break;
                }
                if(!assaultVisible) throw new InvalidOperationException("No surveyed visible player position for indoor assault probe.");
                Physics.SyncTransforms();
                responder.GetComponent<EnemyMotor>().MakeEnemyHostileToAttacker(game.PlayerEntityBehaviour);
                deadline=Time.realtimeSinceStartup+5;
                while(Time.realtimeSinceStartup<deadline && (responder.DefenseTarget ||
                    responder.GetComponent<EnemySenses>().Target!=game.PlayerEntityBehaviour)) yield return null;
                if(responder.DefenseTarget || defenseReport.ThreatId!=null ||
                    responder.GetComponent<EnemySenses>().Target!=game.PlayerEntityBehaviour ||
                    game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Assault)
                    throw new InvalidOperationException("Indoor player assault did not take priority over enemy defense; defense="+(responder.DefenseTarget!=null)+
                        "; threat="+defenseReport.ThreatId+"; playerTarget="+(responder.GetComponent<EnemySenses>().Target==game.PlayerEntityBehaviour)+
                        "; crime="+game.PlayerEntity.CrimeCommitted+"; sight="+CivilianResponses.Sees(responder.transform.position,game.PlayerMotor.transform.position,20)+
                        "; hostile="+responder.GetComponent<EnemyMotor>().IsHostile);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_ASSAULT_PASS: native player assault transfers indoor defender to player crime response.");
                // Restore this disposable encounter before testing target-death stand-down.
                DaggerfallUI.Instance.PopToHUD(); game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
                responder.EndCombat(); defenseReport.ThreatId=originalThreat;
                var enemyPosition=enemy.transform.position;
                target.Entity.SetHealth(0); yield return null; yield return null;
                if((summoned.Interior!=null && !summoned.Interior.Returning) || responder.CombatReady || responder.DefenseTarget ||
                    game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Dead indoor attacker did not end defense without blaming player.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_ENEMY_DEATH_PASS: dead attacker ends indoor combat and starts return without a player crime.");
                guards.EnemyDefense=false;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_DEFENSE_ARRIVAL_PASS: finite guard travels, enters and damages reported hostile enemy without blaming player.");
                // The isolated combat/effect fixture needs its own living foe.
                if(enemy) UnityEngine.Object.Destroy(enemy);
                enemy=GameObjectHelper.CreateFoeGameObjects(enemyPosition,MobileTypes.Thief,1,MobileReactions.Passive)[0];
                enemy.SetActive(true); yield return null;
                enemy.GetComponent<EnemyMotor>().IsHostile=true; enemy.GetComponent<EnemyMotor>().enabled=false;
                enemy.GetComponent<EnemySenses>().enabled=false; enemy.GetComponent<EnemyAttack>().enabled=false;
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
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_DEFENDER_PASS: actual indoor native combat damages enemy, preserves interior target and leaves player innocent.");
                actor.EndCombat();
                var effects=actor.GetComponent<EntityEffectManager>();
                effects.AssignBundle(new EntityEffectBundle(new EffectBundleSettings {
                    Name="Guard persistence probe",TargetType=TargetTypes.ByTouch,ElementType=ElementTypes.Magic,
                    Effects=new[] { new EffectEntry { Key="ContinuousDamage-Health",Settings=new EffectSettings { DurationBase=10,DurationPerLevel=1,MagnitudeBaseMin=1,MagnitudeBaseMax=1,MagnitudePerLevel=1 } } }
                },game.PlayerEntityBehaviour),AssignBundleFlags.BypassSavingThrows|AssignBundleFlags.BypassChance);
                yield return new WaitForSecondsRealtime(0.2f);
                if(effects.EffectCount!=1) throw new InvalidOperationException("Probe damage effect was not applied; bundles="+effects.EffectCount);
                int rounds=effects.GetInstancedBundlesSaveData()[0].liveEffects[0].roundsRemaining;
                var capture=typeof(GuardActor).GetMethod("CaptureEffects");
                if(capture==null) throw new InvalidOperationException("Guard effect capture is missing.");
                capture.Invoke(actor,null);
                string json=SaveLoadManager.Serialize(typeof(GuardRoster),roster);
                roster=GuardRoster.CopyValidated((GuardRoster)SaveLoadManager.Deserialize(typeof(GuardRoster),json));
                UnityEngine.Object.Destroy(actor.gameObject); actor=null; yield return null;
                actor=GuardActor.CreateInterior(roster,id,room,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                yield return null;
                effects=actor.GetComponent<EntityEffectManager>();
                if(effects.EffectCount!=1 ||
                    effects.GetInstancedBundlesSaveData()[0].liveEffects[0].roundsRemaining!=rounds)
                    throw new InvalidOperationException("Guard recreation lost or restarted continuous damage.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_EFFECT_RECREATE_PASS: player continuous damage, native serialization and body recreation preserve active effect and rounds.");
                actor.Capture(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                capture.Invoke(actor,null);
                var savedEffects=roster.Guards[0].Effects;
                UnityEngine.Object.Destroy(actor.gameObject); actor=null; yield return null;
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(180);
                yield return null; yield return null;
                actor=GuardActor.CreateInterior(roster,id,room,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                effects=actor.GetComponent<EntityEffectManager>();
                var remaining=effects.GetInstancedBundlesSaveData()[0].liveEffects[0].roundsRemaining;
                uint elapsed=roster.Guards[0].Effects.MagicMinute-savedEffects.MagicMinute;
                if(elapsed<3 || remaining!=rounds-(int)elapsed)
                    throw new InvalidOperationException("Unloaded guard effect duration did not catch up exactly.");
                capture.Invoke(actor,null);
                UnityEngine.Object.Destroy(actor.gameObject); actor=null; yield return null;
                // Keep this isolated combat fixture in its room while testing
                // effect expiry independently of the normal search timeout.
                roster.Guards[0].SearchUntil=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+3600;
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(1200);
                yield return null; yield return null;
                actor=GuardActor.CreateInterior(roster,id,room,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                if(actor.GetComponent<EntityEffectManager>().EffectCount!=0 || roster.Guards[0].Effects!=null)
                    throw new InvalidOperationException("Expired guard effect returned after body recreation.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_EFFECT_ELAPSED_PASS: unloaded duration catches up and expired effect stays removed.");
            }
            finally { if(actor) { actor.EndCombat(); UnityEngine.Object.Destroy(actor.gameObject); } }
        }
    }
}
