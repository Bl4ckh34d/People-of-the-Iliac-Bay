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
                if (roster == null || roster.Guards.Count != 10) throw new InvalidOperationException("Test town must receive ten persistent guards.");
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
                int responding = 0;
                foreach (var guard in roster.Guards) if (guard.Duty != GuardDuty.Posted) responding++;
                if (responding < 1 || responding > 4 || roster.Reports.Count != 1 || roster.Guards.Count != 10)
                    throw new InvalidOperationException("Repeated native reports did not stay within the finite response group.");
                var actors = UnityEngine.Object.FindObjectsOfType<GuardActor>();
                if (actors.Length == 0 || actors.Length > 10) throw new InvalidOperationException("Physical roster views missing or duplicated.");
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
                var entity = fighter.GetComponent<DaggerfallEntityBehaviour>().Entity;
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
                if (roster.Guards.Count != 10 || Math.Abs(roster.Guards.Find(g => g.Id == woundedId).HealthFraction - health) > 0.02)
                    throw new InvalidOperationException("Real reload changed guard allocation or healed the wounded guard.");
                game.PlayerMotor.transform.position += Vector3.right * 100;
                var wounded = roster.Guards.Find(g => g.Id == woundedId);
                wounded.SearchUntil = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() - 1;
                deadline = Time.realtimeSinceStartup + 5;
                while (wounded.Duty == GuardDuty.Engaged && Time.realtimeSinceStartup < deadline) yield return null;
                if (wounded.Duty != GuardDuty.Returning && wounded.Duty != GuardDuty.Posted)
                    throw new InvalidOperationException("Lost-sight guard did not leave pursuit after search expiry.");
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
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
            }
        }
    }
}
