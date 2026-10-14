using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class InteriorEscapeProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var streets = adapter.GetComponent<StreetPopulation>(); var guards = adapter.GetComponent<GuardPopulation>();
            var flag = typeof(LivingCitiesMod).GetField("exteriorGuardsEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            bool previous = (bool)flag.GetValue(adapter); flag.SetValue(adapter, true); streets.ReactionsEnabled = true;
            bool savedEvent = false, loadedEvent = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => savedEvent = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loadedEvent = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.PlayerEntity.GodMode = true;
                var clock = DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((86400 + 20*3600 - (int)(clock.ToSeconds()%86400))%86400);
                var population = (SettlementPopulation)typeof(LivingCitiesMod).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                int building = -1; float deadline = Time.realtimeSinceStartup + 60;
                while (building < 0 && Time.realtimeSinceStartup < deadline)
                {
                    if (guards.Roster != null) foreach (var approach in guards.Roster.BuildingApproaches)
                    {
                        var home = population.Households.Find(h => h.BuildingKey == approach.BuildingKey);
                        if (home != null && home.Residents.Count >= 2 && home.Residents.Exists(r => CivilianAlarms.ChooseReaction(r) == CivilianReaction.Flee))
                        { building = home.BuildingKey; break; }
                    }
                    yield return null;
                }
                if (building < 0) throw new InvalidOperationException("No surveyed family home for escape test.");
                DaggerfallStaticDoors owner = null; StaticDoor door = default(StaticDoor);
                foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach (var item in candidate.Doors) if (item.doorType == DoorTypes.Building && item.buildingKey == building)
                    { owner = candidate; door = item; break; }
                if (!owner) throw new InvalidOperationException("No family entrance.");
                TownSmokeHarness.EnterInterior(owner.transform, door);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                ResidentActor witness = null, victim = null;
                foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if (CivilianAlarms.ChooseReaction(actor.Resident) == CivilianReaction.Flee) { witness = actor; break; }
                foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if (actor != witness) { victim = actor; break; }
                if (!victim || !witness) throw new InvalidOperationException("Need two present family residents.");
                var geometry = new InteriorGeometry(game.PlayerEnterExit.Interior);
                var origin = game.PlayerEnterExit.Interior.transform;
                Vector3 chosen = witness.transform.position - Vector3.up; float longest = 0;
                foreach (var anchor in geometry.LocalAnchors)
                {
                    Vector3 p = origin.TransformPoint(anchor); float length = Vector3.Distance(p, geometry.EntryGround.Value);
                    if (length > longest) { chosen = p; longest = length; }
                }
                var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.transform.position = chosen + Vector3.up;
                Physics.SyncTransforms();
                bool blocked = geometry.RouteToExit(chosen).Count == 0;
                UnityEngine.Object.Destroy(obstacle); yield return null;
                if (!blocked) throw new InvalidOperationException("Escape route accepts an overlapping obstacle.");
                // Controlled placement isolates actual path traversal from the daily anchor assignment.
                witness.Place(chosen, Vector3.forward, true); victim.Place(chosen, Vector3.forward, true);
                string id = witness.Resident.Id, name = witness.Person.NameNPC;
                game.WeaponManager.WeaponDamage(null, false, false, victim.transform, victim.transform.position, Vector3.forward);
                var store = (WorldStateStore)typeof(LivingCitiesMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                var alarm = store.ActiveAlarm(id, clock.ToSeconds());
                var person = alarm == null ? null : alarm.People.Find(p => p.ResidentId == id);
                if (person == null || person.Escape == null)
                {
                    var inside = geometry.RouteToExit(chosen);
                    var outside = guards.WitnessRoute(building, clock.ToSeconds());
                    throw new InvalidOperationException("Interior witness has no saved escape route: inside=" + inside.Count +
                        "; outside=" + (outside == null ? -1 : outside.Count) + "; reaction=" + (person == null ? "missing" : person.Reaction.ToString()) +
                        "; direct=" + (person != null && person.DirectWitness));
                }
                if (guards.Roster.Reports.Count != 0) throw new InvalidOperationException("Guards learned murder before witness escaped.");
                Vector3 before = witness.transform.position;
                yield return new WaitForSecondsRealtime(0.4f);
                if (!witness || Vector3.Distance(before, witness.transform.position) < 0.1f || witness.Person.NameNPC != name)
                    throw new InvalidOperationException("Witness did not physically follow escape route.");
                string json = SaveLoadManager.Serialize(typeof(WorldState), adapter.GetSaveData());
                double fleeSpeed = ResidentMovement.FleeSpeed(witness.Resident.AppearanceSeed);
                if (person.Escape.Speed != fleeSpeed || fleeSpeed < ResidentMovement.WalkSpeed * 2.5 ||
                    fleeSpeed > ResidentMovement.WalkSpeed * 3.5)
                    throw new InvalidOperationException("Runtime flight did not use the resident's stable randomized speed.");
                var restored = new WorldStateStore(0); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
                var saved = restored.ActiveAlarm(id, clock.ToSeconds()).People.Find(p => p.ResidentId == id);
                if (saved.Escape == null || saved.Escape.ReportAt != person.Escape.ReportAt || saved.Escape.Speed != fleeSpeed)
                    throw new InvalidOperationException("Escape timing or speed lost in native serialization: speed=" + fleeSpeed.ToString("R") +
                        "; saved=" + (saved.Escape == null ? "null" : saved.Escape.Speed.ToString("R")) +
                        "; timingDelta=" + (saved.Escape == null ? 0 : saved.Escape.ReportAt - person.Escape.ReportAt));
                double reportAt = person.Escape.ReportAt;
                game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - escaping witness");
                deadline = Time.realtimeSinceStartup + 30;
                while (!savedEvent && Time.realtimeSinceStartup < deadline) yield return null;
                if (!savedEvent) throw new InvalidOperationException("Escape save timed out.");
                int saveKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - escaping witness");
                game.SaveLoadManager.Load(saveKey); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loadedEvent || !streets.Ready || guards.Roster == null) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loadedEvent || !streets.Ready || guards.Roster == null) throw new InvalidOperationException("Escape reload timed out.");
                yield return null;
                clock = DaggerfallUnity.Instance.WorldTime.Now;
                store = (WorldStateStore)typeof(LivingCitiesMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                alarm = store.ActiveAlarm(id, clock.ToSeconds()); person = alarm == null ? null : alarm.People.Find(p => p.ResidentId == id);
                if (person == null || person.Escape == null || person.Escape.ReportAt != reportAt || person.Escape.Speed != fleeSpeed)
                    throw new InvalidOperationException("Real reload reset witness journey.");
                clock.RaiseTime((float)Math.Max(0, person.Escape.ExitAt - clock.ToSeconds() + 1));
                yield return null; yield return null;
                if (streets.FindActor(id) || person.BuildingKey != -1) throw new InvalidOperationException("Escaped witness remains inside.");
                if (clock.ToSeconds() < person.Escape.ReportAt && guards.Roster.Reports.Count != 0) throw new InvalidOperationException("Report arrived before witness reached guard.");
                game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(0.5f);
                witness = streets.FindActor(id);
                if (!witness || witness.Person.NameNPC != name) throw new InvalidOperationException("Escaped identity missing outside the door.");
                before = witness.transform.position; yield return new WaitForSecondsRealtime(0.4f);
                if (Vector3.Distance(before, witness.transform.position) < 0.1f) throw new InvalidOperationException("Escaped witness is not running toward guard outside.");
                clock.RaiseTime((float)Math.Max(0, person.Escape.ReportAt - clock.ToSeconds() + 1));
                yield return null; yield return null;
                var report = guards.Roster.Reports.Find(r => r.Id == "civilian:" + alarm.Id);
                if (report == null || report.BuildingKey != building || report.GuardIds.Count < 1 || report.GuardIds.Count > 4)
                    throw new InvalidOperationException("Escaped witness failed to report original crime building.");
                Debug.Log("LIVING_CITIES_TOWN_INTERIOR_ESCAPE_PASS: physical doorway route, delayed report, exterior identity/movement, finite building response and real save/load; building=" + building + "; save=" + saveKey);
            }
            finally
            {
                SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad;
                guards.Clear(); flag.SetValue(adapter, previous); game.PlayerEntity.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            }
        }
    }
}
