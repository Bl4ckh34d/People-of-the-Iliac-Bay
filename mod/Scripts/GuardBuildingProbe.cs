using System;
using System.Collections;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class GuardBuildingProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var flag = typeof(LivingCitiesMod).GetField("exteriorGuardsEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            bool enabled = (bool)flag.GetValue(adapter); flag.SetValue(adapter, true);
            var controller = adapter.GetComponent<GuardPopulation>();
            bool saved = false, loaded = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.PlayerEntity.GodMode = true;
                var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                float deadline = Time.realtimeSinceStartup + 45;
                GuardBuildingApproach approach = null;
                while (approach == null && Time.realtimeSinceStartup < deadline)
                {
                    if (controller.Roster != null)
                        foreach (var candidate in controller.Roster.BuildingApproaches)
                        {
                            BuildingSummary building;
                            if (directory.GetBuildingSummary(candidate.BuildingKey, out building) &&
                                (building.BuildingType == DFLocation.BuildingTypes.Alchemist ||
                                building.BuildingType == DFLocation.BuildingTypes.GeneralStore ||
                                building.BuildingType == DFLocation.BuildingTypes.PawnShop ||
                                building.BuildingType == DFLocation.BuildingTypes.Armorer))
                            { approach = candidate; break; }
                        }
                    yield return null;
                }
                if (approach == null) throw new InvalidOperationException("No surveyed shop approach available.");
                DaggerfallStaticDoors owner = null; StaticDoor door = default(StaticDoor);
                foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach (var item in candidate.Doors)
                        if (item.doorType == DoorTypes.Building && item.buildingKey == approach.BuildingKey)
                        { owner = candidate; door = item; break; }
                if (!owner) throw new InvalidOperationException("Surveyed shop has no native door.");
                TownSmokeHarness.EnterInterior(owner.transform, door);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                game.PlayerEntity.HaveShownSurrenderToGuardsDialogue = true;
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Theft;
                game.PlayerEntity.SpawnCityGuards(true); game.PlayerEntity.SpawnCityGuards(true);
                var roster = controller.Roster;
                GuardReport report = roster.Reports.Find(r => r.BuildingKey == approach.BuildingKey);
                if (report == null || report.GuardIds.Count < 1 || report.GuardIds.Count > 4)
                    throw new InvalidOperationException("Native indoor alarm did not dispatch finite building responders.");
                double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                var first = roster.Guards.Find(g => g.Id == report.GuardIds[0]);
                if (first.ArrivesAt <= now || UnityEngine.Object.FindObjectsOfType<GuardActor>().Length != 0)
                    throw new InvalidOperationException("Indoor guards appeared before their physical travel deadline.");
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(first.ArrivesAt - now + 1));
                yield return null; yield return null;
                GuardActor actor = null;
                foreach (var item in UnityEngine.Object.FindObjectsOfType<GuardActor>()) if (item.GuardId == first.Id) actor = item;
                if (!actor || !actor.IsInterior || first.Interior == null)
                    throw new InvalidOperationException("Arrived guard did not enter the reported shop: duty=" + first.Duty +
                        "; now=" + DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + "; arrival=" + first.ArrivesAt +
                        "; playing=" + game.IsPlayingGame() + "; inside=" + game.PlayerEnterExit.IsPlayerInsideBuilding +
                        "; landing=" + typeof(GuardPopulation).GetField("entryGround", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller));
                var landing = new InteriorGeometry(game.PlayerEnterExit.Interior).EntryGround;
                if (!landing.HasValue || Vector3.Distance(actor.transform.position, landing.Value + Vector3.up) > 3)
                    throw new InvalidOperationException("Guard did not enter through the interior doorway.");
                string id = first.Id;
                bool visible = false;
                foreach (var anchor in new InteriorGeometry(game.PlayerEnterExit.Interior).LocalAnchors)
                {
                    var spot = game.PlayerEnterExit.Interior.transform.TransformPoint(anchor) + Vector3.up;
                    float distance = Vector3.Distance(actor.transform.position, spot);
                    if (distance < 2 || distance > 8 || !CivilianResponses.Sees(actor.transform.position, spot, 20)) continue;
                    game.PlayerMotor.transform.position = spot; visible = true; break;
                }
                if (!visible) throw new InvalidOperationException("No visible reachable interior combat test position.");
                deadline = Time.realtimeSinceStartup + 5;
                while (!actor.CombatReady && Time.realtimeSinceStartup < deadline) yield return null;
                if (!actor.CombatReady) throw new InvalidOperationException("Indoor guard did not activate native combat on sight: crime=" +
                    game.PlayerEntity.CrimeCommitted + "; playing=" + game.IsPlayingGame() + "; invisible=" + game.PlayerEntity.IsInvisible +
                    "; sight=" + CivilianResponses.Sees(actor.transform.position, game.PlayerMotor.transform.position, 20));
                var entity = actor.GetComponent<DaggerfallEntityBehaviour>().Entity;
                entity.SetHealth(Math.Max(1, entity.MaxHealth / 2), true); controller.Capture();
                double health = first.HealthFraction;
                game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - shop guards");
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Shop guard save timed out.");
                int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - shop guards");
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || controller.Roster == null) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || controller.Roster == null) throw new InvalidOperationException("Direct indoor guard reload timed out.");
                first = controller.Roster.Guards.Find(g => g.Id == id);
                if (first.Interior == null || first.Interior.BuildingKey != approach.BuildingKey ||
                    Math.Abs(first.HealthFraction - health) > 0.02)
                    throw new InvalidOperationException("Indoor reload lost location or healed guard.");
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
                yield return null; yield return null;
                if (first.Interior != null && !first.Interior.Returning)
                    throw new InvalidOperationException("Cleared crime did not end the indoor search.");
                foreach (var item in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                    if (item.IsInterior && item.CombatReady) throw new InvalidOperationException("Cleared crime left an indoor guard hostile.");
                game.PlayerEnterExit.TransitionExterior(false);
                DaggerfallUI.Instance.PopToHUD(); yield return null;
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(3600);
                yield return null; yield return null;
                if (first.Interior != null || first.Duty != GuardDuty.Posted)
                    throw new InvalidOperationException("Guard failed to leave the interior and return to its post.");
                Debug.Log("LIVING_CITIES_TOWN_GUARD_BUILDING_PASS: native indoor alarm, finite delayed arrival, doorway entry, wounded interior save/load and return; save=" + key);
            }
            finally
            {
                SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad;
                controller.Clear(); flag.SetValue(adapter, enabled);
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
            }
        }
    }
}
