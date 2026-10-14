using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class NativeKeeperResponseProbe
    {
        public static IEnumerator Run(string character, string keeperId)
        {
            yield return VerifyDoorPermissions();
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var streets = adapter.GetComponent<StreetPopulation>();
            var guards = adapter.GetComponent<GuardPopulation>();
            var storeField = typeof(LivingCitiesMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic);
            var store = (WorldStateStore)storeField.GetValue(adapter);
            var world = store.Snapshot(); var keeper = world.NativePeople.Find(p => p.Id == keeperId);
            var town = world.Settlements.Find(t => t.SettlementId == keeper.SettlementId);
            var victim = town.Households.Find(h => h.Residents.Exists(r => r.IsAlive)).Residents.Find(r => r.IsAlive);
            var room = game.PlayerEnterExit.Interior;
            var npc = Array.Find(room.GetComponentsInChildren<StaticNPC>(true), n => n.Data.hash == keeper.NativeHash);
            streets.ReactionsEnabled = true; game.PlayerEntity.GodMode = true;
            if (!npc || streets.KeeperServiceBlocked(npc)) throw new InvalidOperationException("Keeper response fixture is not initially available.");
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var alarm = store.RecordViolence(keeper.SettlementId, victim.Id, now);
            var local = room.transform.InverseTransformPoint(npc.transform.position);
            store.LocateViolence(alarm.Id, keeper.WorkKey, local.x, local.y, local.z);
            // Controlled direct sight, not a claim of native keeper death or natural shop killing acceptance.
            streets.ObserveKeeperViolence(alarm, npc.transform.position, now);
            if (!streets.KeeperServiceBlocked(npc)) throw new InvalidOperationException("Witnessed violence did not block keeper service.");
            var ui = DaggerfallUI.UIManager.TopWindow;
            game.TalkManager.TalkToStaticNPC(npc);
            typeof(PlayerActivate).GetMethod("StaticNPCClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game.PlayerActivate, new object[] { npc });
            if (DaggerfallUI.UIManager.TopWindow != ui) throw new InvalidOperationException("Alarmed keeper opened dialogue or trade.");
            var initial = npc.transform.position;
            yield return new WaitForSecondsRealtime(1);
            var person = alarm.People.Find(p => p.ResidentId == keeperId);
            if (person == null || person.Escape == null || Vector3.Distance(initial, npc.transform.position) < 0.1f)
            {
                var graph = (InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(streets);
                var outside = guards.WitnessRoute(keeper.WorkKey, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                int inside = person == null ? -1 : graph.RouteToExit(room.transform.TransformPoint(new Vector3((float)person.X, (float)person.Y, (float)person.Z))).Count;
                string floors = "";
                if (graph.EntryGround.HasValue) game.PlayerMotor.transform.position = graph.EntryGround.Value + Vector3.up;
                game.PlayerMouseLook.SetHorizontalFacing(initial - game.PlayerMotor.transform.position);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "LivingCities-keeper-route.png"));
                yield return new WaitForSecondsRealtime(0.5f);
                foreach (var hit in Physics.RaycastAll(initial + Vector3.up * 2, Vector3.down, 10, ~0, QueryTriggerInteraction.Ignore))
                    floors += hit.collider.name + "@" + room.transform.InverseTransformPoint(hit.point) + ";";
                var cells = (System.Collections.Generic.Dictionary<Vector2Int, Vector3>)typeof(InteriorGeometry).GetField("floors", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(graph);
                Vector3 ground = room.transform.TransformPoint(new Vector3((float)person.X, (float)person.Y, (float)person.Z));
                float nearest = float.PositiveInfinity;
                foreach (var cell in cells.Values) nearest = Mathf.Min(nearest, Vector3.Distance(ground, cell));
                string overlaps = "";
                foreach (var overlap in Physics.OverlapCapsule(ground + Vector3.up * 0.3f, ground + Vector3.up * 1.65f, 0.24f, ~0, QueryTriggerInteraction.Ignore))
                    overlaps += overlap.name + ":npc=" + (overlap.GetComponentInParent<StaticNPC>() != null) + ";";
                Vector3 marker, door, normal;
                var check = DaggerfallStaticDoors.GetDoorPosition(room.EntryDoor);
                if (room.FindClosestEnterMarker(check, out marker)) check = marker;
                string landing = room.FindClosestInteriorDoor(check, out door, out normal) ? room.transform.InverseTransformPoint(door + normal * 0.8f).ToString() : "missing";
                throw new InvalidOperationException("Keeper failed to follow a surveyed escape route; inside=" + inside + "; outside=" + (outside == null ? -1 : outside.Count) +
                    "; position=" + (person == null ? "missing" : person.X + "," + person.Y + "," + person.Z) + "; flat=" + room.transform.InverseTransformPoint(initial) +
                    "; moved=" + Vector3.Distance(initial, npc.transform.position) + "; pause=" + GameManager.IsGamePaused + "; entry=" +
                    (graph.EntryGround.HasValue ? room.transform.InverseTransformPoint(graph.EntryGround.Value).ToString() : "missing") + "; floors=" + floors +
                    "; cells=" + cells.Count + "; nearest=" + nearest + "; overlaps=" + overlaps + "; native landing=" + landing);
            }
            if (guards.Roster.Reports.Exists(r => r.Id == "civilian:" + alarm.Id && r.GuardIds.Count > 0))
                throw new InvalidOperationException("Keeper reported before reaching a guard.");
            bool saved = false, loaded = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.Save(character, "Living Cities - keeper response");
                float deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Keeper response save timed out.");
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, "Living Cities - keeper response");
                game.SaveLoadManager.Load(slot); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Keeper response reload timed out.");
                yield return new WaitForSecondsRealtime(1);
                store = (WorldStateStore)storeField.GetValue(adapter); alarm = store.NativeResponse(keeperId);
                room = game.PlayerEnterExit.Interior;
                npc = Array.Find(room.GetComponentsInChildren<StaticNPC>(true), n => n.Data.hash == keeper.NativeHash);
                if (!npc || alarm == null || !streets.KeeperServiceBlocked(npc)) throw new InvalidOperationException("Reload lost keeper response/service restriction.");
                person = alarm.People.Find(p => p.ResidentId == keeperId);
                var clock = DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((float)Math.Max(0, person.Escape.ExitAt - clock.ToSeconds() + 1));
                yield return new WaitForSecondsRealtime(0.5f);
                if (npc.gameObject.activeSelf) throw new InvalidOperationException("Keeper remained in shop after reaching exit.");
                clock.RaiseTime((float)Math.Max(0, person.Escape.ReportAt - clock.ToSeconds() + 1));
                yield return new WaitForSecondsRealtime(0.5f);
                if (!guards.Roster.Reports.Exists(r => r.Id == "civilian:" + alarm.Id && r.GuardIds.Count > 0))
                    throw new InvalidOperationException("Arrived native keeper did not notify finite guards.");
                clock.RaiseTime((float)Math.Max(0, CivilianAlarms.ActiveUntil(alarm) - clock.ToSeconds() + 1));
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Murder;
                yield return new WaitForSecondsRealtime(0.5f);
                if (!streets.KeeperServiceBlocked(npc)) throw new InvalidOperationException("Keeper resumed service before crime cleared.");
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
                yield return new WaitForSecondsRealtime(0.5f);
                if (streets.KeeperServiceBlocked(npc) || !npc.gameObject.activeSelf || npc.DisplayName != keeper.Name)
                    throw new InvalidOperationException("Keeper did not resume native identity/service after response cleared.");
                ui = DaggerfallUI.UIManager.TopWindow;
                typeof(PlayerActivate).GetMethod("StaticNPCClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game.PlayerActivate, new object[] { npc });
                yield return null;
                if (DaggerfallUI.UIManager.TopWindow == ui) throw new InvalidOperationException("Recovered keeper did not open native service.");
                DaggerfallUI.Instance.PopToHUD();
                var current = (SettlementPopulation)typeof(LivingCitiesMod).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                var ledger = store.Snapshot().AlarmIncidents;
                Resident hitResident = null; uint surname = 0;
                foreach (var home in current.Households)
                    foreach (var member in home.Residents)
                        if (hitResident == null && member.IsAlive && member.AgeGroup == ResidentAgeGroup.Adult &&
                            !ledger.Exists(a => a.VictimId == member.Id) && !streets.FindActor(member.Id))
                        { hitResident = member; surname = home.SurnameSeed; }
                if (hitResident == null) throw new InvalidOperationException("No fresh resident for native keeper witness hit.");
                var navigation = (CityNavigation)typeof(StreetPopulation).GetField("navigation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(streets);
                var actorMap = (System.Collections.Generic.Dictionary<string, ResidentActor>)typeof(StreetPopulation).GetField("actors", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(streets);
                var visitorObject = UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject, room.transform);
                try
                {
                    var visitor = visitorObject.AddComponent<ResidentActor>(); visitor.Initialize(hitResident, surname, navigation);
                    visitor.Place(npc.transform.position - Vector3.up + Vector3.right * 0.8f, Vector3.forward, true);
                    actorMap.Add(hitResident.Id, visitor); Physics.SyncTransforms();
                    game.WeaponManager.WeaponDamage(null, false, false, visitor.transform, visitor.transform.position, Vector3.forward);
                    var witnessed = store.NativeResponse(keeperId);
                    if (hitResident.IsAlive || witnessed == null || witnessed.VictimId != hitResident.Id ||
                        !witnessed.People.Exists(p => p.ResidentId == keeperId && p.DirectWitness) || !streets.KeeperServiceBlocked(npc))
                        throw new InvalidOperationException("Native weapon-hit path did not capture living keeper witness and suppress service.");
                    Debug.Log("LIVING_CITIES_KEEPER_WEAPON_PASS: native WeaponDamage killing captures keeper sight; controlled visitor placement.");
                }
                finally { actorMap.Remove(hitResident.Id); UnityEngine.Object.Destroy(visitorObject); }
                Debug.Log("LIVING_CITIES_KEEPER_RESPONSE_PASS: sight, service suppression, physical exit, real reload, delayed finite report and recovery; save=" + slot);
            }
            finally { SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad; }
            yield return VerifyKeeperDeath(character, keeperId);
        }
        private static IEnumerator VerifyKeeperDeath(string character, string keeperId)
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var streets = adapter.GetComponent<StreetPopulation>();
            var guards = adapter.GetComponent<GuardPopulation>();
            var field = typeof(LivingCitiesMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic);
            bool loaded = false, saved = false;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoad += onLoad; SaveLoadManager.OnSave += onSave;
            try
            {
                int fixture = game.SaveLoadManager.FindSaveFolderByNames(character, "Living Cities - shopkeeper identity");
                game.SaveLoadManager.Load(fixture); DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Keeper death fixture load timed out.");
                yield return new WaitForSecondsRealtime(1);
                var store = (WorldStateStore)field.GetValue(adapter);
                var keeper = store.NativePeopleFor(game.PlayerGPS.CurrentLocation.MapTableData.MapId).Find(p => p.Id == keeperId);
                var room = game.PlayerEnterExit.Interior;
                var npc = Array.Find(room.GetComponentsInChildren<StaticNPC>(true), n => n.Data.hash == keeper.NativeHash);
                var town = (SettlementPopulation)typeof(LivingCitiesMod).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                var home = town.Households.Find(h => h.HeadId == keeperId);
                var partner = streets.FindActor(home.Residents.Find(r => r.Id != keeperId).Id);
                if (!npc || !partner) throw new InvalidOperationException("Keeper death fixture lacks keeper or partner.");
                // Controlled partner sight tests identity/violence integration, not general stair navigation.
                partner.Place(npc.transform.position - Vector3.up + Vector3.right * 0.8f, Vector3.forward, true);
                streets.ReactionsEnabled = true; Physics.SyncTransforms();
                int reports = guards.Roster.Reports.Count;
                game.WeaponManager.WeaponDamage(null, false, false, npc.transform, npc.transform.position, Vector3.forward);
                if (keeper.IsAlive || home.Residents[0].IsAlive || npc.gameObject.activeSelf)
                    throw new InvalidOperationException("Native shopkeeper weapon hit did not persist death and hide service body.");
                var alarm = store.Snapshot().AlarmIncidents.Find(a => a.VictimId == keeperId);
                if (alarm == null || alarm.Corpse == null || !alarm.People.Exists(p => p.ResidentId == partner.Resident.Id && p.DirectWitness))
                    throw new InvalidOperationException("Native keeper death lost corpse or family witness.");
                if (!streets.KeeperServiceBlocked(npc) || guards.Roster.Reports.Count != reports)
                    throw new InvalidOperationException("Dead keeper permits service or creates instant guard notification.");
                yield return new WaitForSecondsRealtime(0.5f);
                if (streets.CorpseCount < 1) throw new InvalidOperationException("Native keeper corpse has no physical view.");
                game.SaveLoadManager.Save(character, "Living Cities - keeper death");
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Keeper death save timed out.");
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, "Living Cities - keeper death");
                loaded = false; game.SaveLoadManager.Load(slot); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Keeper death reload timed out.");
                yield return new WaitForSecondsRealtime(1);
                store = (WorldStateStore)field.GetValue(adapter);
                keeper = store.NativePeopleFor(keeper.SettlementId).Find(p => p.Id == keeperId);
                npc = Array.Find(game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>(true), n => n.Data.hash == keeper.NativeHash);
                if (keeper.IsAlive || !npc || npc.gameObject.activeSelf || !streets.KeeperServiceBlocked(npc) || streets.CorpseCount < 1)
                    throw new InvalidOperationException("Reload resurrected native keeper or lost physical corpse/service block.");
                var ui = DaggerfallUI.UIManager.TopWindow; game.TalkManager.TalkToStaticNPC(npc);
                if (DaggerfallUI.UIManager.TopWindow != ui) throw new InvalidOperationException("Dead native keeper opened dialogue after reload.");
                Debug.Log("LIVING_CITIES_KEEPER_DEATH_PASS: native weapon hit; household identity; family witness; no instant guards; corpse; blocked service; real reload; save=" + slot);
            }
            finally { SaveLoadManager.OnLoad -= onLoad; SaveLoadManager.OnSave -= onSave; }
        }
        private static IEnumerator VerifyDoorPermissions()
        {
            var origin = new Vector3(5000, 100, 5000);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var doorObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position = origin + Vector3.forward * 0.7f + Vector3.up;
                wall.transform.localScale = new Vector3(2, 2, 0.1f);
                doorObject.transform.position = origin + Vector3.forward * 1.4f + Vector3.up;
                doorObject.transform.localScale = new Vector3(1, 2, 0.1f);
                var door = doorObject.AddComponent<DaggerfallActionDoor>(); door.PlaySounds = false;
                yield return null; Physics.SyncTransforms();
                var body = doorObject.GetComponent<BoxCollider>();
                InteriorGeometry.OpenOrdinaryDoors(origin, origin + Vector3.forward * 2);
                if (body.isTrigger) throw new InvalidOperationException("Civilian opened door through intervening wall.");
                wall.SetActive(false); Physics.SyncTransforms(); door.CurrentLockValue = 10;
                InteriorGeometry.OpenOrdinaryDoors(origin, origin + Vector3.forward * 2);
                if (body.isTrigger) throw new InvalidOperationException("Civilian opened locked door.");
                door.CurrentLockValue = 0;
                var action = doorObject.AddComponent<DaggerfallAction>();
                InteriorGeometry.OpenOrdinaryDoors(origin, origin + Vector3.forward * 2);
                if (body.isTrigger) throw new InvalidOperationException("Civilian opened scripted door.");
                UnityEngine.Object.DestroyImmediate(action);
                InteriorGeometry.OpenOrdinaryDoors(origin, origin + Vector3.forward * 2);
                if (!body.isTrigger) throw new InvalidOperationException("Civilian did not open ordinary unlocked door.");
                Debug.Log("LIVING_CITIES_CIVILIAN_DOORS_PASS: wall, lock and script barriers retained; ordinary unlocked door opens.");
            }
            finally { UnityEngine.Object.Destroy(wall); UnityEngine.Object.Destroy(doorObject); }
        }
    }
}
