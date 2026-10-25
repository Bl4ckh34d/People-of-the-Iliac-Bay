using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class InnAlarmProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var mod = UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var streets = mod.GetComponent<StreetPopulation>();
            var guards = mod.GetComponent<GuardPopulation>();
            if (game.PlayerEnterExit.IsPlayerInsideBuilding) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors owner = null; StaticDoor entry = default(StaticDoor);
            foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                if (candidate.Doors == null) continue;
                foreach (var door in candidate.Doors)
                    if (door.doorType == DoorTypes.Building &&
                        directory.GetBuildingSummary(door.buildingKey, out BuildingSummary summary) &&
                        summary.BuildingType == DFLocation.BuildingTypes.Tavern)
                    { owner = candidate; entry = door; break; }
                if (owner) break;
            }
            if (!owner) throw new InvalidOperationException("Inn alarm probe requires a tavern in the loaded settlement.");
            TownSmokeHarness.EnterInterior(owner.transform, entry);
            yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
            streets.ReactionsEnabled = true;
            var room = game.PlayerEnterExit.Interior;
            var store = (WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(mod);
            typeof(StreetPopulation).GetMethod("SurveyKeeperIdentities", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Invoke(streets, null);
            var native = store.NativePeopleFor(directory.MapID);
            var candidates = room.GetComponentsInChildren<StaticNPC>(true).Where(n =>
                n.gameObject.activeInHierarchy && n.Data.buildingKey == entry.buildingKey &&
                native.Exists(p => p.NativeHash == n.Data.hash && p.WorkKey == entry.buildingKey && p.IsAlive)).ToArray();
            StaticNPC victim = null, directObserver = null, otherRoom = null;
            foreach (var v in candidates)
                foreach (var w in candidates)
                {
                    if (w == v || !CivilianResponses.Sees(w.transform.position, v.transform.position, 20)) continue;
                    foreach (var remote in candidates)
                        if (remote != v && remote != w && Vector3.Distance(remote.transform.position, v.transform.position) >= 5 &&
                            !CivilianResponses.Sees(remote.transform.position, v.transform.position, 20))
                        { victim = v; directObserver = w; otherRoom = remote; break; }
                    if (victim) break;
                }
            if (!victim || !directObserver || !otherRoom)
                throw new InvalidOperationException("Inn alarm probe needs three living native occupants, with a direct observer and a separated room occupant.");
            var victimPerson = native.Find(p => p.NativeHash == victim.Data.hash && p.WorkKey == entry.buildingKey);
            var remotePerson = native.Find(p => p.NativeHash == otherRoom.Data.hash && p.WorkKey == entry.buildingKey);
            Vector3 towardVictim = (victim.transform.position - directObserver.transform.position).normalized;
            game.PlayerMotor.transform.position = victim.transform.position + towardVictim * 1.25f + Vector3.up * 0.1f;
            Physics.SyncTransforms();
            if (!CivilianResponses.Sees(directObserver.transform.position, victim.transform.position, 20) ||
                !CivilianResponses.Sees(directObserver.transform.position, game.PlayerMotor.transform.position, 20))
                throw new InvalidOperationException("Inn alarm probe could not establish a direct witness.");
            game.WeaponManager.WeaponDamage(null, false, false, victim.transform, victim.transform.position, Vector3.forward);
            yield return null;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var alarm = store.Snapshot().AlarmIncidents.Find(a => a.VictimId == victimPerson.Id);
            if (alarm == null || alarm.Corpse == null || !alarm.People.Exists(p => p.DirectWitness || p.HeardViolence))
                throw new InvalidOperationException("Real native attack did not confirm a witnessed inn murder.");
            var recipient = alarm.People.Find(p => p.ResidentId == remotePerson.Id);
            if (recipient == null || !recipient.InnAlarm || recipient.Reaction != CivilianReaction.Flee || recipient.BuildingKey != entry.buildingKey)
                throw new InvalidOperationException("Other-room occupant did not receive the inn-wide flee alarm.");
            var outside = guards ? guards.WitnessRoute(entry.buildingKey, now) : null;
            if (outside != null)
            {
                float deadline = Time.realtimeSinceStartup + 3;
                while (recipient.Escape == null && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    recipient = store.Snapshot().AlarmIncidents.Find(a => a.Id == alarm.Id).People.Find(p => p.ResidentId == remotePerson.Id);
                }
                if (recipient.Escape == null) throw new InvalidOperationException("Other-room occupant did not receive a planned escape despite a valid guard route.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_ALARM_PASS: real native murder, direct witness, separated room occupant alarmed and fleeing; escape=" +
                (recipient.Escape != null) + "; postedRoute=" + (outside != null) + "; victim=" + victimPerson.Id + "; recipient=" + remotePerson.Id);
            string travelerId="traveler:inn-crime-probe:"+Guid.NewGuid().ToString("N");
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            store.RegisterTraveler(new TravelerPerson { Id=travelerId,Name="Crime Scene Visitor",HomeSettlement=directory.MapID+1,
                Race=1,Sex=ResidentSex.Male,AppearanceSeed=1234 });
            var visit=new TravelerVisit { Id=travelerId+":visit",PersonId=travelerId,SettlementId=directory.MapID,
                TavernKey=entry.buildingKey,DesiredArrival=clock.ToSeconds(),CommuteSeconds=12,Nights=1,
                Route=new List<SavedRoutePoint> { new SavedRoutePoint(),new SavedRoutePoint{X=1} } };
            store.RegisterTravelerVisit(visit);
            clock.RaiseTime(13);
            float travelerDeadline=Time.realtimeSinceStartup+15;
            AlarmPerson arriving=null;
            while(Time.realtimeSinceStartup<travelerDeadline)
            {
                var current=store.Snapshot().AlarmIncidents.Find(a=>a.Id==alarm.Id);
                arriving=current.People.Find(p=>p.ResidentId==travelerId);
                if(arriving!=null && arriving.Escape!=null) break;
                yield return null;
            }
            if(arriving==null || !arriving.CorpseObserver || arriving.DirectWitness || arriving.Escape==null)
                throw new InvalidOperationException("Arriving inn traveler did not discover corpse and prepare neutral escape.");
            travelerDeadline=Time.realtimeSinceStartup+30;
            while(clock.ToSeconds()<arriving.Escape.ExitAt && Time.realtimeSinceStartup<travelerDeadline) yield return null;
            yield return null; yield return null;
            if(clock.ToSeconds()<arriving.Escape.ExitAt || streets.FindActor(travelerId))
                throw new InvalidOperationException("Arriving inn traveler failed to leave after corpse discovery.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_TRAVELER_CORPSE_PASS: arriving registered traveler discovers corpse, avoids room idle and exits on neutral report route.");
            var currentAlarm=store.Snapshot().AlarmIncidents.Find(a=>a.Id==alarm.Id);
            var direct=currentAlarm.People.Find(p=>p.DirectWitness && p.Escape!=null);
            if(direct==null) throw new InvalidOperationException("Witnessed inspection requires a real escaping eyewitness.");
            double advance=direct.Escape.ReportAt+1-clock.ToSeconds();
            if(advance>0) clock.RaiseTime((long)Math.Ceiling(advance));
            GuardReport murderReport=null; travelerDeadline=Time.realtimeSinceStartup+10;
            while(Time.realtimeSinceStartup<travelerDeadline)
            {
                murderReport=guards.Roster.Reports.Find(r=>r.Id=="civilian:"+alarm.Id && r.GuardIds.Count>0);
                if(murderReport!=null) break;
                yield return null;
            }
            if(murderReport==null || !murderReport.CrimeScene || murderReport.Investigation ||
                game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Murder)
                throw new InvalidOperationException("Eyewitness report lost Murder accusation or cleanup ownership.");
            var priorCrime=game.PlayerEntity.CrimeCommitted;
            var priorConcealment=game.PlayerEntity.MagicalConcealmentFlags;
            var previousBundles=game.PlayerEffectManager.EffectBundles;
            try
            {
                // Constant flags are cleared by DFU's effect manager. Use a real
                // sustained effect so the test cannot silently reacquire the player.
                var concealment=game.PlayerEffectManager.CreateSpellBundle("Invisibility-True",
                    ElementTypes.Magic,new EffectSettings { DurationBase=2000,DurationPerLevel=1 });
                var concealmentSettings=concealment.Settings;
                concealmentSettings.Name="People of the Iliac Bay inspection test concealment";
                concealment.Settings=concealmentSettings;
                game.PlayerEffectManager.AssignBundle(concealment,AssignBundleFlags.BypassSavingThrows|AssignBundleFlags.BypassChance);
                yield return null; yield return null;
                if(!game.PlayerEntity.IsInvisible) throw new InvalidOperationException("Lost-sight test effect did not activate.");
                double arrival=murderReport.Arrivals.Min(a=>a.ScheduledAt);
                advance=arrival+1-clock.ToSeconds();
                if(advance>0) clock.RaiseTime((long)Math.Ceiling(advance));
                GuardActor pursuing=null;
                travelerDeadline=Time.realtimeSinceStartup+10;
                while(Time.realtimeSinceStartup<travelerDeadline)
                {
                    pursuing=UnityEngine.Object.FindObjectsOfType<GuardActor>().FirstOrDefault(a=>a.GuardId==murderReport.GuardIds[0] &&
                        guards.Roster.Guards.Exists(g=>g.Id==a.GuardId && g.Interior!=null));
                    if(pursuing) break;
                    yield return null;
                }
                if(!pursuing) throw new InvalidOperationException("No arrived native guard for pursuit recovery probe.");
                // Start the actual native motor/attack state, then retain invisibility
                // as the controlled loss-of-sight condition. Never clear Murder.
                pursuing.BeginCombat(clock.ToSeconds());
                travelerDeadline=Time.realtimeSinceStartup+3;
                while(!pursuing.CombatReady && Time.realtimeSinceStartup<travelerDeadline) yield return null;
                if(!pursuing.CombatReady) throw new InvalidOperationException("Scene guard did not enter native combat.");
                clock.RaiseTime(301);
                travelerDeadline=Time.realtimeSinceStartup+5;
                while(pursuing.CombatReady && Time.realtimeSinceStartup<travelerDeadline) yield return null;
                if(pursuing.CombatReady || game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Murder)
                    throw new InvalidOperationException("Lost-sight pursuit did not end independently of Murder accusation.");
                bool inspecting=false,cleared=false,replacementInspected=false;
                string killedInspector=null;
                double occupationDeadline=murderReport.InvestigationUntil;
                travelerDeadline=Time.realtimeSinceStartup+90;
                while(Time.realtimeSinceStartup<travelerDeadline)
                {
                    if(!game.PlayerEntity.IsInvisible) throw new InvalidOperationException("Lost-sight test effect expired during inspection.");
                    var body=store.GetCrimeSceneCorpseSnapshot(alarm.Id);
                    if(body.InspectionGuardId!=null && body.InspectionRemainingRealSeconds<5 && body.InspectionRemainingRealSeconds>0)
                    {
                        inspecting=true;
                        if(killedInspector==null)
                        {
                            var assigned=UnityEngine.Object.FindObjectsOfType<GuardActor>().FirstOrDefault(a=>a.GuardId==body.InspectionGuardId);
                            if(!assigned) throw new InvalidOperationException("Inspecting guard has no native actor.");
                            killedInspector=assigned.GuardId;
                            assigned.GetComponent<DaggerfallEntityBehaviour>().Entity.SetHealth(0);
                        }
                        else if(body.InspectionGuardId!=killedInspector) replacementInspected=true;
                    }
                    if(body.Cleared) { cleared=true; break; }
                    yield return null;
                }
                if(!inspecting || !cleared)
                {
                    var failedBody=store.GetCrimeSceneCorpseSnapshot(alarm.Id);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INSPECTION_DIAGNOSTIC: arrival="+murderReport.HasArrival+"; inspector="+failedBody.InspectionGuardId+
                        "; remaining="+failedBody.InspectionRemainingRealSeconds+"; body="+failedBody.X+","+failedBody.Y+","+failedBody.Z);
                    var motions=(System.Collections.IDictionary)typeof(GuardPopulation).GetField("crimeSceneMotions",
                        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(guards);
                    foreach(var guard in guards.Roster.Guards.Where(g=>murderReport.GuardIds.Contains(g.Id)))
                    {
                        var pos=guard.Interior==null ? null : guard.Interior.Position;
                        var motion=motions[guard.Id]; string route="none";
                        if(motion!=null)
                        {
                            var points=(System.Collections.ICollection)motion.GetType().GetField("Route").GetValue(motion);
                            route=(points==null ? "null" : points.Count.ToString())+"/"+motion.GetType().GetField("Segment").GetValue(motion);
                        }
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INSPECTION_GUARD: "+guard.Id+"; duty="+guard.Duty+"; route="+route+
                            "; pos="+(pos==null ? "outside" : pos.X+","+pos.Y+","+pos.Z));
                        if(guard.Id==failedBody.InspectionGuardId && pos!=null)
                        {
                            var nav=(InteriorGeometry)typeof(GuardPopulation).GetField("interiorNavigation",
                                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(guards);
                            var scene=game.PlayerEnterExit.Interior.transform;
                            var ground=scene.TransformPoint(new Vector3((float)pos.X,(float)pos.Y-1,(float)pos.Z));
                            var clear=typeof(InteriorGeometry).GetMethod("Clear",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                            var actual=UnityEngine.Object.FindObjectsOfType<GuardActor>().FirstOrDefault(a=>a.GuardId==guard.Id);
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INSPECTION_CONNECTION: clear="+clear.Invoke(nav,new object[]{ground})+"; exit="+nav.RouteToExit(ground).Count+
                                "; ready="+(actual && actual.CombatReady)+"; hostile="+(actual && actual.GetComponent<EnemyMotor>().IsHostile)+"; paused="+GameManager.IsGamePaused+
                                "; invisible="+game.PlayerEntity.IsInvisible+"; remainingPursuit="+(guard.Interior.PursuitUntil-clock.ToSeconds()));
                            foreach(var hit in Physics.RaycastAll(ground+Vector3.up*0.4f,Vector3.down,0.8f,~0,QueryTriggerInteraction.Ignore))
                                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INSPECTION_FLOOR: "+hit.collider.name+"; y="+scene.InverseTransformPoint(hit.point).y+"; normal="+hit.normal.y);
                            int approachCount=0,reachable=0,visible=0;
                            foreach(var anchor in nav.ApproachPoints(new Vector3((float)failedBody.X,(float)failedBody.Y,(float)failedBody.Z),1.5f,2))
                            {
                                approachCount++;
                                if(nav.RouteToExit(scene.TransformPoint(anchor)).Count>=2) reachable++;
                                if(nav.HasClearLine(scene.TransformPoint(anchor+Vector3.up*1.25f),scene.TransformPoint(new Vector3((float)failedBody.X,(float)failedBody.Y+0.5f,(float)failedBody.Z)))) visible++;
                            }
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INSPECTION_TARGETS: candidates="+approachCount+"; reachable="+reachable+"; visible="+visible);
                        }
                    }
                    throw new InvalidOperationException("Witnessed inn murder did not receive physical inspection and cleanup.");
                }
                if(game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Murder) throw new InvalidOperationException("Inspection cleared the Murder accusation.");
                if(!replacementInspected || killedInspector==null || guards.Roster.Guards.Find(g=>g.Id==killedInspector).IsAlive ||
                    Math.Abs(murderReport.InvestigationUntil-occupationDeadline)>0.001)
                    throw new InvalidOperationException("Replacement inspector did not finish independently within the original occupation.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REPLACEMENT_INSPECTOR_PASS: native inspector death during inspection; another arrived responder physically inspected and cleared body; occupation deadline unchanged.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WITNESSED_INSPECTION_PASS: real eyewitness retained Murder; native combat lost-sight search expired; guard inspected and cleared corpse without clearing crime.");
            }
            finally
            {
                foreach(var bundle in game.PlayerEffectManager.EffectBundles)
                    if(!previousBundles.Contains(bundle)) game.PlayerEffectManager.RemoveBundle(bundle);
                game.PlayerEntity.CrimeCommitted=priorCrime; game.PlayerEntity.MagicalConcealmentFlags=priorConcealment;
            }
        }
    }
}
