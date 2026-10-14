using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class CrimeSceneProbe
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
                        if (home != null && home.Residents.Count == 1)
                        { building = home.BuildingKey; break; }
                    }
                    yield return null;
                }
                if (building < 0) throw new InvalidOperationException("No surveyed single-resident home for discovery test.");
                DaggerfallStaticDoors owner = null; StaticDoor door = default(StaticDoor);
                foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach (var item in candidate.Doors) if (item.doorType == DoorTypes.Building && item.buildingKey == building)
                    { owner = candidate; door = item; break; }
                if (!owner) throw new InvalidOperationException("No family entrance.");
                TownSmokeHarness.EnterInterior(owner.transform, door);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                if (Cursor.lockState != CursorLockMode.None || !Cursor.visible || game.PlayerMouseLook.enabled)
                    throw new InvalidOperationException("Automated test captured the desktop cursor.");
                var victims = UnityEngine.Object.FindObjectsOfType<ResidentActor>();
                ResidentActor victim = null;
                foreach (var actor in victims) if (actor.Resident.HomeKey == building) { victim = actor; break; }
                if (!victim) throw new InvalidOperationException("No resident present for discovery test.");
                string victimId = victim.Resident.Id;
                game.PlayerEntity.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Theft;
                game.WeaponManager.WeaponDamage(null, false, false, victim.transform, victim.transform.position, Vector3.forward);
                var store = (WorldStateStore)typeof(LivingCitiesMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                var scene = store.Snapshot().AlarmIncidents.Find(a => a.VictimId == victimId);
                if (scene == null || scene.Corpse == null || scene.Scene == null || scene.Scene.PlayerLeft || guards.Roster.Reports.Count != 0)
                    throw new InvalidOperationException("Unwitnessed death did not remain concealed until departure.");
                if (game.PlayerEntity.CrimeCommitted != DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Theft)
                    throw new InvalidOperationException("Concealment lost previously known crime.");
                yield return new WaitForSecondsRealtime(0.3f);
                if (streets.CorpseCount != 1) throw new InvalidOperationException("Confirmed dead resident has no persistent corpse view.");
                string bodyJson = SaveLoadManager.Serialize(typeof(WorldState), adapter.GetSaveData());
                var bodyRestore = new WorldStateStore(0);
                bodyRestore.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), bodyJson));
                var savedBody = bodyRestore.Snapshot().AlarmIncidents.Find(a => a.Id == scene.Id).Corpse;
                if (!savedBody.HasPosition || savedBody.BuildingKey != building || savedBody.Cleared)
                    throw new InvalidOperationException("Visible corpse position did not survive native serialization.");
                game.PlayerEntity.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
                game.PlayerEnterExit.TransitionExterior();
                if (!store.Snapshot().AlarmIncidents.Find(a => a.Id == scene.Id).Scene.PlayerLeft)
                    throw new InvalidOperationException("Departure event was lost before the next frame.");
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                var report = guards.Roster.Reports.Find(r => r.Id == "discovery:" + scene.Id);
                if (report == null || !report.Investigation || report.GuardIds.Count < 1 || report.GuardIds.Count > 4)
                    throw new InvalidOperationException("Departure did not dispatch finite investigators.");
                var responder = guards.Roster.Guards.Find(g => g.Id == report.GuardIds[0]);
                double arrival = responder.ArrivesAt;
                clock.RaiseTime((float)Math.Max(0, arrival - clock.ToSeconds() + 2));
                yield return new WaitForSecondsRealtime(1.2f);
                scene = store.Snapshot().AlarmIncidents.Find(a => a.VictimId == victimId);
                if (!scene.Scene.Discovered || !scene.Scene.Occupied(clock.ToSeconds()))
                    throw new InvalidOperationException("Physical guard arrival did not discover scene.");
                TownSmokeHarness.EnterInterior(owner.transform, door);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                if (streets.CorpseCount != 0 || !store.Snapshot().AlarmIncidents.Find(a => a.Id == scene.Id).Corpse.Cleared)
                    throw new InvalidOperationException("Guard arrival failed to clear the corpse.");
                if (!guards.Roster.Guards.Exists(g => g.Interior != null && g.IncidentId == report.Id))
                    throw new InvalidOperationException("Investigators missing on re-entry.");
                if (game.PlayerEntity.CrimeCommitted != DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Murder)
                    throw new InvalidOperationException("Investigators did not attempt custody on re-entry sighting.");
                string json = SaveLoadManager.Serialize(typeof(WorldState), adapter.GetSaveData());
                var restored = new WorldStateStore(0); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
                if (Math.Abs(restored.Snapshot().AlarmIncidents.Find(a => a.Id == scene.Id).Scene.DiscoveredAt - scene.Scene.DiscoveredAt) > 0.001)
                    throw new InvalidOperationException("Discovery deadline changed on serialization.");
                game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - crime scene investigation");
                deadline = Time.realtimeSinceStartup + 30;
                while (!savedEvent && Time.realtimeSinceStartup < deadline) yield return null;
                if (!savedEvent) throw new InvalidOperationException("Crime scene save timed out.");
                int saveKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - crime scene investigation");
                game.SaveLoadManager.Load(saveKey); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 40;
                while ((!loadedEvent || !streets.Ready || guards.Roster == null) && Time.realtimeSinceStartup < deadline) yield return null;
                yield return new WaitForSecondsRealtime(1.2f);
                store = (WorldStateStore)typeof(LivingCitiesMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                if (!loadedEvent || Math.Abs(store.Snapshot().AlarmIncidents.Find(a => a.Id == scene.Id).Scene.DiscoveredAt - scene.Scene.DiscoveredAt) > 0.001)
                    throw new InvalidOperationException("Real reload changed discovery timing.");
                if (streets.CorpseCount != 0 || !store.Snapshot().AlarmIncidents.Find(a => a.Id == scene.Id).Corpse.Cleared)
                    throw new InvalidOperationException("Real reload resurrected a cleared corpse.");
                DaggerfallUI.Instance.PopToHUD();
                var arrestGuard = UnityEngine.Object.FindObjectOfType<GuardActor>();
                deadline = Time.realtimeSinceStartup + 10;
                while (!arrestGuard && Time.realtimeSinceStartup < deadline)
                { yield return null; arrestGuard = UnityEngine.Object.FindObjectOfType<GuardActor>(); }
                if (!arrestGuard) throw new InvalidOperationException("No guard available for surrender contract. paused=" + GameManager.IsGamePaused + "; actors=" + guards.ActiveCount + "; all=" + Resources.FindObjectsOfTypeAll<GuardActor>().Length);
                arrestGuard.EndCombat(); DaggerfallUI.Instance.PopToHUD();
                game.PlayerEntity.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Murder;
                game.PlayerEntity.HaveShownSurrenderToGuardsDialogue = false;
                game.PlayerEntity.GodMode = false;
                int health = game.PlayerEntity.CurrentHealth;
                var attack = arrestGuard.GetComponent<EnemyAttack>();
                var apply = typeof(EnemyAttack).GetMethod("ApplyDamageToPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
                int bestDamage = 0;
                for (int attempt = 0; attempt < 256 && !game.PlayerEntity.HaveShownSurrenderToGuardsDialogue; attempt++)
                    bestDamage = Math.Max(bestDamage, (int)apply.Invoke(attack, new object[] { null }));
                if (game.PlayerEntity.CurrentHealth != health) throw new InvalidOperationException("Damage applied before surrender choice.");
                game.PlayerEntity.GodMode = true;
                var surrender = DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
                if (surrender == null || !game.PlayerEntity.HaveShownSurrenderToGuardsDialogue )
                    throw new InvalidOperationException("Guard did not offer surrender before damage. damage=" + bestDamage + "; shown=" + game.PlayerEntity.HaveShownSurrenderToGuardsDialogue + "; window=" + DaggerfallUI.UIManager.TopWindow.GetType().Name);
                typeof(EnemyAttack).GetMethod("SurrenderToGuardsDialogue_OnButtonClick", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(attack, new object[] { surrender, DaggerfallMessageBox.MessageBoxButtons.Yes });
                yield return null; yield return null;
                deadline = Time.realtimeSinceStartup + 10;
                while (!DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.DfCourtWindow) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!game.PlayerEntity.Arrested || !DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.DfCourtWindow))
                    throw new InvalidOperationException("Accepting surrender did not enter native court. arrested=" + game.PlayerEntity.Arrested + "; window=" + DaggerfallUI.UIManager.TopWindow.GetType().Name);
                Debug.Log("LIVING_CITIES_SURRENDER_PASS: actual roster guard opens surrender before damage; Yes enters native court.");
                DaggerfallUI.Instance.PopToHUD();
                clock = DaggerfallUnity.Instance.WorldTime.Now;

                game.PlayerEntity.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
                clock.RaiseTime((float)Math.Max(0, scene.Scene.DiscoveredAt + 3610 - clock.ToSeconds()));
                yield return new WaitForSecondsRealtime(1.2f);
                if (store.Snapshot().AlarmIncidents.Find(a => a.Id == scene.Id).Scene.Occupied(clock.ToSeconds()))
                    throw new InvalidOperationException("Scene occupation did not expire.");
                Debug.Log("LIVING_CITIES_TOWN_CRIME_SCENE_PASS: confirmed death, prior crime retained, departure dispatch, arrival discovery, re-entry custody trigger, real save/reload and occupation expiry.");
            }
            finally
            {
                SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad;
                guards.Clear(); flag.SetValue(adapter, previous); game.PlayerEntity.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            }
        }
    }
}
