using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class GuardControllerProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var store = (WorldStateStore)typeof(LivingCitiesMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var population = store.Snapshot().Settlements.Find(t => t.SettlementId == directory.MapID);
            var type = typeof(LivingCitiesMod).Assembly.GetType("LivingCities.DFU.GuardPopulation");
            if (type == null) throw new InvalidOperationException("Finite guard controller is missing.");
            var controller = adapter.GetComponent<GuardPopulation>();
            controller.NightPatrols = false;
            bool priorDefense = controller.EnemyDefense;
            controller.EnemyDefense = false;
            var enabledField = typeof(LivingCitiesMod).GetField("exteriorGuardsEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            bool originallyEnabled = (bool)enabledField.GetValue(adapter);
            enabledField.SetValue(adapter, true);
            bool saved = false, loaded = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            try
            {
                // The preview now defaults on. Create the takeover fixture before
                // ownership so SpawnCityGuard cannot return an existing roster actor.
                controller.Clear();
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Assault;
                var transient = game.PlayerEntity.SpawnCityGuard(game.PlayerMotor.transform.position + Vector3.right * 8, Vector3.forward);
                type.GetMethod("SetTown").Invoke(controller, new object[] { directory, game.StreamingWorld.GetCurrentCityNavigation(), population, store });
                yield return null; yield return null;
                if (transient) throw new InvalidOperationException("Transient native guard survived municipal roster takeover.");
                var roster = (GuardRoster)type.GetProperty("Roster").GetValue(controller, null);
                if (roster == null || roster.Guards.Count != 15) throw new InvalidOperationException("Test town must receive fifteen persistent guards under the eight-percent policy.");
                float surveyDeadline = Time.realtimeSinceStartup + 20;
                while (roster.BuildingApproaches.Count == 0 && Time.realtimeSinceStartup < surveyDeadline) yield return null;
                if (roster.BuildingApproaches.Count == 0) throw new InvalidOperationException("Exterior guard approaches were not surveyed.");
                int surveyedBuilding = roster.BuildingApproaches[0].BuildingKey;
                if (roster.BuildingApproaches[0].Routes.Count == 0) throw new InvalidOperationException("Surveyed building has no reachable guard approach.");
                var target = roster.Guards[0].Post;
                var geometry = new TownGeometry(directory, game.StreamingWorld.GetCurrentCityNavigation(), false);
                var approach = roster.BuildingApproaches[0];
                var start = approach.Routes[0].Points[0];
                if (geometry.RouteBetweenPoints(start, approach.Entrance).Count < 2)
                    throw new InvalidOperationException("Saved survey approach is not currently reachable.");
                var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    obstacle.transform.position = geometry.ScenePoint(new RoutePoint(start.X, start.Z)) + Vector3.up * 0.4f;
                    obstacle.transform.localScale = Vector3.one * 0.1f;
                    Physics.SyncTransforms();
                    if (geometry.RouteBetweenPoints(start, approach.Entrance).Count != 0)
                        throw new InvalidOperationException("Guard route accepted a blocker overlapping its starting waypoint.");
                }
                finally { UnityEngine.Object.Destroy(obstacle); }
                yield return null;
                game.PlayerMotor.transform.position = geometry.ScenePoint(new RoutePoint(target.X, target.Z)) + Vector3.up;
                var clock = DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((float)((23 * 3600 - clock.ToSeconds() % 86400 + 86400) % 86400));
                controller.NightTorches = true; controller.TorchLightLimit = 1;
                yield return new WaitForSecondsRealtime(0.6f);
                int lit = 0, carried = 0;
                foreach (var guardView in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                {
                    var light = guardView.GetComponentInChildren<Light>();
                    if (light) { carried++; if (light.enabled) lit++; }
                }
                if (carried < 1 || lit != 1) throw new InvalidOperationException("Night guard torch cap did not select one nearest light.");
                controller.TorchLightLimit = 0;
                yield return new WaitForSecondsRealtime(0.4f);
                foreach (var guardView in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                    foreach (var light in guardView.GetComponentsInChildren<Light>())
                        if (light.enabled) throw new InvalidOperationException("Zero torch budget left a light enabled.");
                controller.TorchLightLimit = 8; controller.NightTorches = false;
                yield return new WaitForSecondsRealtime(0.4f);
                foreach (var guardView in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                    if (guardView.GetComponentInChildren<Light>()) throw new InvalidOperationException("Torch toggle left a carried torch active.");
                controller.NightTorches = true;
                clock.RaiseTime((float)((12 * 3600 - clock.ToSeconds() % 86400 + 86400) % 86400));
                yield return new WaitForSecondsRealtime(0.4f);
                foreach (var guardView in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                    if (guardView.GetComponentInChildren<Light>()) throw new InvalidOperationException("Daytime guard retained a torch.");
                Debug.Log("LIVING_CITIES_GUARD_TORCH_CONTROLLER_PASS: real night/day, nearest light cap, zero budget and toggle.");
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Assault;
                game.PlayerEntity.HaveShownSurrenderToGuardsDialogue = true;
                game.PlayerEntity.SpawnCityGuards(true);
                game.PlayerEntity.SpawnCityGuards(true);
                yield return null;
                var nativeReports=roster.Reports.FindAll(r=>r.Id.StartsWith("native:",StringComparison.Ordinal));
                if (nativeReports.Count != 1 || nativeReports[0].GuardIds.Count < 1 ||
                    nativeReports[0].GuardIds.Count > 4 || roster.Guards.Count != 15)
                    throw new InvalidOperationException("Repeated native reports did not stay within the finite response group.");
                var actors = UnityEngine.Object.FindObjectsOfType<GuardActor>();
                if (actors.Length == 0 || actors.Length > roster.Guards.Count) throw new InvalidOperationException("Physical roster views missing or duplicated.");
                foreach (var actor in actors)
                {
                    var record = roster.Guards.Find(g => g.Id == actor.GuardId);
                    if (record == null) throw new InvalidOperationException("Actor is outside the fixed roster.");
                }
                GuardActor fighter = null;
                float deadline = Time.realtimeSinceStartup + 5;
                while (Time.realtimeSinceStartup < deadline && !fighter)
                {
                    foreach (var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>()) if (actor.CombatReady) { fighter = actor; break; }
                    yield return null;
                }
                if (!fighter) throw new InvalidOperationException("Nearby responding guard did not enter native combat.");
                if (fighter.GetComponent<EnemySounds>().enabled || fighter.HaltWarnings != 1)
                    throw new InvalidOperationException("Criminal pursuit did not issue exactly one initial Halt warning.");
                var entity = fighter.GetComponent<DaggerfallEntityBehaviour>().Entity;
                game.PlayerEntity.HaveShownSurrenderToGuardsDialogue = false;
                var attack = fighter.GetComponent<EnemyAttack>();
                var apply = typeof(EnemyAttack).GetMethod("ApplyDamageToPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int attempt=0; attempt<256 && !game.PlayerEntity.HaveShownSurrenderToGuardsDialogue; attempt++)
                    apply.Invoke(attack, new object[] { null });
                var offer = DaggerfallUI.UIManager.TopWindow as DaggerfallWorkshop.Game.UserInterfaceWindows.DaggerfallMessageBox;
                if (offer == null) throw new InvalidOperationException("Refusal probe did not open native surrender.");
                yield return new WaitForSecondsRealtime(0.5f);
                if (!fighter.gameObject.activeInHierarchy || !fighter.CombatReady || !fighter.GetComponent<EnemyMotor>().IsHostile)
                    throw new InvalidOperationException("Surrender dialogue discarded the active guard pursuit.");
                typeof(EnemyAttack).GetMethod("SurrenderToGuardsDialogue_OnButtonClick", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(attack, new object[] { offer, DaggerfallWorkshop.Game.UserInterfaceWindows.DaggerfallMessageBox.MessageBoxButtons.No });
                yield return new WaitForSecondsRealtime(0.5f);
                if (!game.PlayerEntity.HaveShownSurrenderToGuardsDialogue)
                    throw new InvalidOperationException("Refused surrender reset during active pursuit.");
                yield return VerifyNearbyRecruitment(controller,fighter,geometry,directory);
                foreach (var joining in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                {
                    var assigned=roster.Guards.Find(g=>g.Id==joining.GuardId);
                    if(assigned==null || !assigned.IsAlive || assigned.IncidentId==null ||
                        assigned.Duty==GuardDuty.Posted || assigned.Duty==GuardDuty.Returning) continue;
                    joining.BeginCombat(clock.ToSeconds());
                    yield return null; yield return null;
                    if (!joining.CombatReady) throw new InvalidOperationException("Joining guard did not initialize native combat.");
                    for (int attempt=0; attempt<32; attempt++)
                    {
                        game.PlayerEntity.SetHealth(game.PlayerEntity.MaxHealth, true);
                        apply.Invoke(joining.GetComponent<EnemyAttack>(), new object[] { null });
                    }
                    if (DaggerfallUI.UIManager.TopWindow is DaggerfallWorkshop.Game.UserInterfaceWindows.DaggerfallMessageBox)
                        throw new InvalidOperationException("Joining guard repeated refused surrender. shown="+game.PlayerEntity.HaveShownSurrenderToGuardsDialogue+"; arrested="+game.PlayerEntity.Arrested);
                }
                Debug.Log("LIVING_CITIES_SURRENDER_REFUSAL_PASS: native offer retained pursuit; No persisted across frames and additional guard attacks.");
                entity.SetHealth(Math.Max(1, entity.MaxHealth / 2), true); controller.Capture();
                string woundedId = fighter.GuardId;
                double health = roster.Guards.Find(g => g.Id == woundedId).HealthFraction;
                game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - guard response");
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Guard response save timed out.");
                int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - guard response");
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || controller.Roster == null) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || controller.Roster == null) throw new InvalidOperationException("Guard response reload timed out.");
                roster = controller.Roster;
                if (!roster.BuildingApproaches.Exists(a => a.BuildingKey == surveyedBuilding))
                    throw new InvalidOperationException("Real reload lost the exterior approach cache.");
                if (roster.Guards.Count != 15 || Math.Abs(roster.Guards.Find(g => g.Id == woundedId).HealthFraction - health) > 0.02)
                    throw new InvalidOperationException("Real reload changed guard allocation or healed the wounded guard.");
                game.PlayerMotor.transform.position += Vector3.right * 100;
                var wounded = roster.Guards.Find(g => g.Id == woundedId);
                wounded.SearchUntil = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() - 1;
                deadline = Time.realtimeSinceStartup + 5;
                while (wounded.Duty == GuardDuty.Engaged && Time.realtimeSinceStartup < deadline) yield return null;
                if (wounded.Duty != GuardDuty.Returning && wounded.Duty != GuardDuty.Posted)
                    throw new InvalidOperationException("Lost-sight guard did not leave pursuit after search expiry.");
                directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                geometry = new TownGeometry(directory, game.StreamingWorld.GetCurrentCityNavigation(), false);
                yield return VerifyReacquisition(controller,wounded,geometry);
                var posted = roster.Guards.Find(g => g.IsAlive && g.Duty == GuardDuty.Posted && g.Id != woundedId);
                if (posted == null) throw new InvalidOperationException("Assault test requires an unassigned guard.");
                directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                geometry = new TownGeometry(directory, game.StreamingWorld.GetCurrentCityNavigation(), false);
                game.PlayerMotor.transform.position = geometry.ScenePoint(new RoutePoint(posted.Post.X, posted.Post.Z)) + Vector3.up;
                yield return null; yield return null;
                GuardActor assaulted = null;
                foreach (var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>()) if (actor.GuardId == posted.Id) assaulted = actor;
                if (!assaulted) throw new InvalidOperationException("Posted guard view missing.");
                assaulted.GetComponent<EnemyMotor>().MakeEnemyHostileToAttacker(game.PlayerEntityBehaviour);
                deadline = Time.realtimeSinceStartup + 3;
                while (!assaulted.CombatReady && Time.realtimeSinceStartup < deadline) yield return null;
                if (!assaulted.CombatReady || posted.Duty != GuardDuty.Engaged)
                    throw new InvalidOperationException("Attacked posted guard did not defend himself.");
                Debug.Log("LIVING_CITIES_TOWN_GUARD_CONTROLLER_PASS: fixed roster, bounded native reports, physical views, native combat, wounded real save/load, search expiry and posted self-defense; save=" + key);
            }
            finally
            {
                SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad;
                controller.Clear(); enabledField.SetValue(adapter, originallyEnabled);
                controller.EnemyDefense = priorDefense;
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
            }
        }

        private static IEnumerator VerifyReacquisition(GuardPopulation controller,GuardRecord guard,TownGeometry geometry)
        {
            var game=GameManager.Instance;
            for(int phase=0;phase<2;phase++)
            {
                if(phase==1)
                {
                    game.PlayerMotor.transform.position+=Vector3.right*100;
                    guard.SearchUntil=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()-1;
                    float returnDeadline=Time.realtimeSinceStartup+5;
                    while(guard.Duty==GuardDuty.Engaged && Time.realtimeSinceStartup<returnDeadline) yield return null;
                    if(guard.Duty==GuardDuty.Returning)
                        DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)Math.Max(0,guard.ArrivesAt-DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+1));
                    yield return null; yield return null;
                    if(guard.Duty!=GuardDuty.Posted) throw new InvalidOperationException("Reacquisition fixture did not return to post.");
                }
                var position=controller.Roster.Position(guard.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                bool refusalBefore=game.PlayerEntity.HaveShownSurrenderToGuardsDialogue;
                Vector3 scene=geometry.ScenePoint(new RoutePoint(position.X,position.Z))+Vector3.up;
                bool placed=false;
                foreach(var offset in new[]{Vector3.forward,Vector3.back,Vector3.left,Vector3.right})
                {
                    var point=new SavedRoutePoint {X=position.X+offset.x*2,Z=position.Z+offset.z*2};
                    var destination=geometry.ScenePoint(new RoutePoint(point.X,point.Z))+Vector3.up;
                    if(geometry.RouteBetweenPoints(position,point).Count<2 || !CivilianResponses.Sees(scene,destination,5)) continue;
                    game.PlayerMotor.transform.position=destination; placed=true; break;
                }
                if(!placed) throw new InvalidOperationException("Reacquisition fixture has no visible nearby player position.");
                float deadline=Time.realtimeSinceStartup+4;
                while(guard.Duty!=GuardDuty.Engaged && Time.realtimeSinceStartup<deadline) yield return null;
                if(guard.Duty!=GuardDuty.Engaged)
                    throw new InvalidOperationException("Known wanted player was ignored after pursuit expiry, phase="+phase);
                if(game.PlayerEntity.HaveShownSurrenderToGuardsDialogue!=refusalBefore)
                    throw new InvalidOperationException("Reacquisition unexpectedly changed native surrender state.");
            }
            Debug.Log("LIVING_CITIES_PURSUIT_REACQUIRE_PASS: visible wanted player reacquired after expiry and after completed return; native surrender state preserved.");
        }

        private static IEnumerator VerifyNearbyRecruitment(GuardPopulation controller,GuardActor caller,
            TownGeometry geometry,BuildingDirectory directory)
        {
            var roster=controller.Roster;
            var game=GameManager.Instance;
            var beforePlayer=game.PlayerMotor.transform.position;
            var available=roster.Guards.Find(g=>g.IsAlive && g.Duty==GuardDuty.Posted && g.Interior==null && g.Refuge==null);
            if(available==null) throw new InvalidOperationException("Recruitment probe requires an available roster guard.");
            game.PlayerMotor.transform.position=geometry.ScenePoint(new RoutePoint(available.Post.X,available.Post.Z))+Vector3.up;
            yield return null; yield return null;
            GuardActor candidate=null; SavedRoutePoint meeting=null;
            var from=directory.transform.InverseTransformPoint(caller.transform.position);
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())
            {
                var record=roster.Guards.Find(g=>g.Id==actor.GuardId);
                if(record==null || !record.IsAlive || record.Duty!=GuardDuty.Posted || record.Interior!=null || record.Refuge!=null) continue;
                var route=geometry.RouteBetweenPoints(record.Post,new SavedRoutePoint {X=from.x,Z=from.z});
                if(route.Count>=2)
                {
                    double dx=route[1].X-route[0].X,dz=route[1].Z-route[0].Z;
                    double length=Math.Sqrt(dx*dx+dz*dz);
                    if(length>3) route.Insert(1,new SavedRoutePoint {X=route[0].X+dx*3/length,Z=route[0].Z+dz*3/length});
                }
                foreach(var point in route)
                {
                    double dx=point.X-record.Post.X,dz=point.Z-record.Post.Z;
                    if(dx*dx+dz*dz<1 || dx*dx+dz*dz>36) continue;
                    var scene=geometry.ScenePoint(new RoutePoint(point.X,point.Z))+Vector3.up;
                    if(!CivilianResponses.Sees(scene,actor.transform.position,8)) continue;
                    candidate=actor; meeting=point; break;
                }
                if(candidate) break;
            }
            if(!candidate) throw new InvalidOperationException("Recruitment probe found no available guard with a nearby surveyed approach.");
            int identities=roster.Guards.Count;
            try
            {
                caller.transform.position=geometry.ScenePoint(new RoutePoint(meeting.X,meeting.Z))+Vector3.up;
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                caller.Capture(now);
                var source=roster.Guards.Find(g=>g.Id==caller.GuardId);
                source.LastSeenSuspect=new SavedRoutePoint {X=meeting.X,Z=meeting.Z};
                source.SearchUntil=now+120;
                typeof(GuardPopulation).GetMethod("RecruitNearbyPursuitGuards",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(controller,new object[]{now});
                var recruit=roster.Guards.Find(g=>g.Id==candidate.GuardId);
                if(recruit.Duty!=GuardDuty.Engaged || !recruit.IncidentId.StartsWith("pursuit:",StringComparison.Ordinal) ||
                    Math.Abs(recruit.LastSeenSuspect.X-meeting.X)>0.01 || Math.Abs(recruit.LastSeenSuspect.Z-meeting.Z)>0.01 ||
                    Math.Abs(recruit.SearchUntil-source.SearchUntil)>0.01 || roster.Guards.Count!=identities)
                    throw new InvalidOperationException("Nearby guard did not inherit pursuit knowledge and deadline within the finite roster.");
                GuardRoster.CopyValidated(roster);
                float readyDeadline=Time.realtimeSinceStartup+2;
                while(!candidate.CombatReady && Time.realtimeSinceStartup<readyDeadline) yield return null;
                if(!candidate.CombatReady || !candidate.GetComponent<EnemyMotor>().IsHostile ||
                    !game.PlayerEntity.HaveShownSurrenderToGuardsDialogue)
                    throw new InvalidOperationException("Recruited guard lost native hostility or reset refused surrender: ready="+
                        candidate.CombatReady+"; hostile="+candidate.GetComponent<EnemyMotor>().IsHostile+
                        "; refused="+game.PlayerEntity.HaveShownSurrenderToGuardsDialogue);
                Debug.Log("LIVING_CITIES_PURSUIT_RECRUIT_PASS: nearby available native guard joins with shared knowledge/deadline, fixed identities and retained refusal.");
            }
            finally { game.PlayerMotor.transform.position=beforePlayer; }
        }
    }
}
