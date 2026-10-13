using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Player;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Explicit command-line test mode only; never starts a character during normal play.
    public sealed class TownSmokeHarness : MonoBehaviour
    {
        private bool saved, loaded;
        internal static void EnterInterior(Transform owner, StaticDoor door)
        {
            var game = GameManager.Instance;
            game.PlayerGPS.DiscoverBuilding(door.buildingKey);
            var enter = typeof(PlayerActivate).GetMethod("TransitionInterior",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (enter == null) throw new InvalidOperationException("Native interior entry handler missing.");
            // Native activation sets BuildingDiscoveryData and shop/tavern/residence
            // flags before layout. Bypassing it can silently hide vanilla occupants.
            enter.Invoke(game.PlayerActivate, new object[] { owner, door, false });
            if (game.PlayerEnterExit.BuildingDiscoveryData.buildingKey != door.buildingKey)
                throw new InvalidOperationException("Native entry did not set building discovery data.");
        }
        private void OnSaved(SaveData_v1 ignored) { saved = true; }
        private void OnLoaded(SaveData_v1 ignored) { loaded = true; }
        private void OnDestroy() { SaveLoadManager.OnSave -= OnSaved; SaveLoadManager.OnLoad -= OnLoaded; }
        private IEnumerator Start()
        {
            // Opt-in test process must keep advancing when the editor/chat has focus.
            Application.runInBackground = true;
            while (GameManager.Instance == null || !GameManager.Instance.IsReady) yield return null;
            yield return null;
            var game = GameManager.Instance;
            // This owned test process drives transitions and camera directly. Do not
            // let desktop clicks/keys accidentally activate doors during assertions.
            InputManager.Instance.ClearAllActions();
            InputManager.Instance.enabled = false;
            game.PlayerActivate.enabled = false;
            DFLocation location;
            if (!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall", "Gothway Garden", out location))
            { Debug.LogError("LIVING_CITIES_TOWN_FAIL: test settlement missing."); yield break; }
            DFPosition pixel = MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude, location.MapTableData.Latitude);
            string[] arguments = Environment.GetCommandLineArgs();
            int fixtureArgument = Array.IndexOf(arguments, "-livingcities-fixture-character");
            if (fixtureArgument >= 0)
            {
                if (fixtureArgument + 1 >= arguments.Length || !arguments[fixtureArgument + 1].StartsWith("LivingCitiesTest-", StringComparison.Ordinal))
                { Debug.LogError("LIVING_CITIES_TOWN_FAIL: invalid fixture character."); yield break; }
                yield return VerifyLoads(arguments[fixtureArgument + 1], pixel, location.MapTableData.MapId);
                yield break;
            }
            DaggerfallUnity.Settings.StartCellX = pixel.X;
            DaggerfallUnity.Settings.StartCellY = pixel.Y;
            DaggerfallUnity.Settings.StartInDungeon = false;
            var start = game.StartGameBehaviour;
            start.NoWorld = false;
            start.EnableVideos = false;
            start.GodMode = true;
            start.CharacterDocument = new CharacterDocument {
                name = "LivingCitiesTest-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                backStory = new List<string> { "Disposable Living Cities integration-test character." },
                biographyEffects = new List<string>() };
            start.StartMethod = StartGameBehaviour.StartMethods.NewCharacter;
            Debug.Log("LIVING_CITIES_TOWN_START: " + location.Name + "; pixel=" + pixel.X + "," + pixel.Y);
            float deadline = Time.realtimeSinceStartup + 90;
            BuildingDirectory directory = null;
            CityNavigation nav = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId == location.MapTableData.MapId &&
                    game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer)
                {
                    directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                    nav = game.StreamingWorld.GetCurrentCityNavigation();
                    if (directory != null && directory.BuildingCount > 0 && nav != null && nav.NavGridWidth > 0) break;
                }
                yield return null;
            }
            if (directory == null || nav == null)
            { Debug.LogError("LIVING_CITIES_TOWN_FAIL: town did not finish loading."); yield break; }
            yield return new WaitForSecondsRealtime(2);
            // Dismiss only the disposable new-character introduction, leaving the HUD.
            if (DaggerfallUI.UIManager.TopWindow is DaggerfallMessageBox)
                DaggerfallUI.UIManager.PopWindow();
            VerifyGeometry(directory, nav);
            int homes = 0, doors = 0, walkable = 0;
            int bestDistance = int.MaxValue;
            DFPosition centerCell = new DFPosition();
            for (int kind = (int)DFLocation.BuildingTypes.House1; kind <= (int)DFLocation.BuildingTypes.House6; kind++)
                homes += directory.GetBuildingsOfType((DFLocation.BuildingTypes)kind).Count;
            foreach (DaggerfallStaticDoors owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                if (owner.Doors != null) doors += owner.Doors.Length;
            for (int z = 0; z < nav.NavGridHeight; z++)
                for (int x = 0; x < nav.NavGridWidth; x++)
                    if (nav.GetNavGridWeightLocal(x, z) > 0)
                    {
                        walkable++;
                        int distance = Math.Abs(x - nav.NavGridWidth / 2) + Math.Abs(z - nav.NavGridHeight / 2);
                        if (distance < bestDistance) { bestDistance = distance; centerCell = new DFPosition(x, z); }
                    }
            if (homes < 1 || doors < 1 || walkable < 1)
            { Debug.LogError("LIVING_CITIES_TOWN_FAIL: incomplete town census."); yield break; }
            Debug.Log("LIVING_CITIES_TOWN_SURVEY_PASS: map=" + directory.MapID + "; buildings=" + directory.BuildingCount +
                "; homes=" + homes + "; doors=" + doors + "; walkable=" + walkable + "; grid=" + nav.NavGridWidth + "x" + nav.NavGridHeight);
            game.PlayerEntity.GodMode = true;
            game.PlayerMotor.transform.position = nav.WorldToScenePosition(nav.NavGridToWorldPosition(centerCell)) + Vector3.up * 2;
            yield return new WaitForSecondsRealtime(3);
            SaveLoadManager.OnSave += OnSaved;
            saved = false;
            game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - inside Gothway Garden");
            deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) { Debug.LogError("LIVING_CITIES_TOWN_FAIL: inside save timed out."); yield break; }
            yield return null;
            int insideKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - inside Gothway Garden");
            Debug.Log("LIVING_CITIES_TEST_SAVE: inside=" + game.SaveLoadManager.GetSaveFolder(insideKey));

            // One map pixel away, outside the settlement's location rect. Town geometry
            // may still be streamed nearby; reload this save to exercise a fresh scene.
            game.StreamingWorld.TeleportToCoordinates(pixel.X + 1, pixel.Y);
            deadline = Time.realtimeSinceStartup + 90;
            yield return null;
            while ((!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer || game.PlayerGPS.CurrentMapPixel.X != pixel.X + 1) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            if (!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer ||
                game.PlayerGPS.CurrentMapPixel.X != pixel.X + 1 || game.PlayerGPS.CurrentMapPixel.Y != pixel.Y)
            { Debug.LogError("LIVING_CITIES_TOWN_FAIL: outside reposition timed out."); yield break; }
            yield return new WaitForSecondsRealtime(3);
            saved = false;
            game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - outside Gothway Garden");
            deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) { Debug.LogError("LIVING_CITIES_TOWN_FAIL: outside save timed out."); yield break; }
            yield return null;
            int outsideKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - outside Gothway Garden");
            Debug.Log("LIVING_CITIES_TEST_SAVE: outside=" + game.SaveLoadManager.GetSaveFolder(outsideKey));
            Debug.Log("LIVING_CITIES_TOWN_SAVES_PASS: character=" + game.PlayerEntity.Name + "; insideKey=" + insideKey + "; outsideKey=" + outsideKey);
            yield return VerifyLoads(game.PlayerEntity.Name, pixel, location.MapTableData.MapId, true);
        }

        private IEnumerator VerifyLoads(string character, DFPosition pixel, int mapId, bool requireFamily = false)
        {
            var game = GameManager.Instance;
            var adapter = GetComponent<LivingCitiesMod>();
            SaveLoadManager.OnLoad += OnLoaded;
            string expectedPopulation = null;
            // Inside, outside, then town return exercise real save slots and streamed
            // geometry. Fixture-character mode also tests saves from an earlier process.
            foreach (bool inside in new[] { true, false, true })
            {
                game.SaveLoadManager.EnumerateSaves();
                string name = "Living Cities - " + (inside ? "inside" : "outside") + " Gothway Garden";
                int key = game.SaveLoadManager.FindSaveFolderByNames(character, name);
                if (key < 0) { Debug.LogError("LIVING_CITIES_TOWN_FAIL: missing fixture " + name); yield break; }
                loaded = false;
                game.SaveLoadManager.Load(key);
                // Match the game's LoadGame button: loading alone leaves its menu open.
                DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 90;
                while ((!loaded || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer) &&
                    Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer)
                { Debug.LogError("LIVING_CITIES_TOWN_FAIL: fixture load timed out."); yield break; }
                yield return new WaitForSecondsRealtime(2);
                bool correctPosition = game.PlayerGPS.CurrentMapPixel.X == pixel.X + (inside ? 0 : 1) &&
                    game.PlayerGPS.CurrentMapPixel.Y == pixel.Y;
                if (inside) correctPosition &= game.PlayerGPS.HasCurrentLocation &&
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId == mapId &&
                    game.StreamingWorld.GetCurrentBuildingDirectory() != null;
                else correctPosition &= !game.PlayerGPS.HasCurrentLocation ||
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId != mapId;
                if (!correctPosition || game.PlayerEntity.Name != character)
                { Debug.LogError("LIVING_CITIES_TOWN_FAIL: wrong character or location after load."); yield break; }
                if (inside) VerifyGeometry(game.StreamingWorld.GetCurrentBuildingDirectory(), game.StreamingWorld.GetCurrentCityNavigation());
                WorldState state = (WorldState)adapter.GetSaveData();
                SettlementPopulation town = state.Settlements.Find(value => value.SettlementId == mapId);
                if (town == null || town.Households.Count == 0)
                { Debug.LogError("LIVING_CITIES_TOWN_FAIL: town population missing after load."); yield break; }
                // Newly generated route caches may finish between loads; compare all
                // persistent population fields independently of that derived cache.
                state.Routines.Clear();
                string actual = JsonUtility.ToJson(state);
                if (expectedPopulation != null && expectedPopulation != actual)
                { Debug.LogError("LIVING_CITIES_TOWN_FAIL: population changed across fixture loads."); yield break; }
                expectedPopulation = actual;
                Debug.Log("LIVING_CITIES_TOWN_LOAD_PASS: " + (inside ? "inside" : "outside") + "; households=" + town.Households.Count);
            }
            yield return VerifyStreets();
            yield return VerifyFamilies(mapId, requireFamily);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-livingcities-quest-probe") >= 0)
                yield return QuestIdentityProbe.Run();
            Debug.Log("LIVING_CITIES_TOWN_TEST_PASS: real saves, repeated loads and street actors; character=" + character);
        }
        private IEnumerator VerifyFamilies(int mapId, bool required)
        {
            var adapter = GetComponent<LivingCitiesMod>();
            var streets = GetComponent<StreetPopulation>();
            var world = (WorldState)adapter.GetSaveData();
            var town = world.Settlements.Find(item => item.SettlementId == mapId);
            Household selected = null; Resident child = null; int children = 0;
            foreach (var home in town.Households)
                foreach (var resident in home.Residents)
                    if (resident.AgeGroup == ResidentAgeGroup.Child)
                    { children++; if (resident.IsAlive && (child == null || home.Residents.Count > selected.Residents.Count)) { selected = home; child = resident; } }
            if (child == null)
            {
                if (required) throw new InvalidOperationException("Fresh family fixture contains no children.");
                Debug.Log("LIVING_CITIES_TOWN_LEGACY_ADULTS_PASS: old households preserved without invented children.");
                yield break;
            }
            var game = GameManager.Instance;
            if (game.PlayerEnterExit.IsPlayerInside) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            var now = DaggerfallUnity.Instance.WorldTime.Now;
            double seconds = now.ToSeconds();
            for (int minute = 0; minute < 1440; minute += 10)
            {
                var state = streets.GetState(child.Id, seconds + minute * 60);
                if (state.BuildingKey != child.HomeKey || (state.Activity != Activity.Home && state.Activity != Activity.Sleep))
                    throw new InvalidOperationException("Child received an adult visit schedule.");
            }
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var profiles = HousingSurvey.Read(directory);
            if (profiles.Count != town.Households.Count || !profiles.Exists(p => p.BedCount > 0 && p.FootprintArea > 0))
                throw new InvalidOperationException("Runtime housing survey missing actual beds or footprints.");
            // Enter a family home in the morning; sleep poses are a later milestone.
            int daySeconds = (int)(now.ToSeconds() % 86400);
            now.RaiseTime((86400 + 7 * 3600 - daySeconds) % 86400);
            DaggerfallStaticDoors owner = null; StaticDoor entry = default(StaticDoor);
            foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach (var door in candidate.Doors)
                    if (door.doorType == DoorTypes.Building && door.buildingKey == selected.BuildingKey)
                    { owner = candidate; entry = door; break; }
            if (!owner) throw new InvalidOperationException("Family home has no door.");
            EnterInterior(owner.transform, entry);
            yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
            var actor = streets.FindActor(child.Id);
            if (!actor || !actor.gameObject.activeInHierarchy) throw new InvalidOperationException("Child missing from family interior.");
            var flat = actor.GetComponentInChildren<DaggerfallBillboard>();
            if (!flat || !DaggerfallWorkshop.Utility.TextureReader.IsChildNPCTexture(flat.Summary.Archive, flat.Summary.Record))
                throw new InvalidOperationException("Child rendered as adult instead of classic child flat.");
            string childName = actor.Person.NameNPC; string surname = childName.Substring(childName.IndexOf(' ') + 1);
            foreach (var resident in selected.Residents)
            {
                var relative = streets.FindActor(resident.Id);
                var state = streets.GetState(resident.Id, now.ToSeconds());
                bool expectedHome = resident.IsAlive && state.Activity != Activity.Travel && state.BuildingKey == selected.BuildingKey;
                if (expectedHome && (!relative || !relative.Person.NameNPC.EndsWith(" " + surname, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Family missing or household surname changed.");
                if (relative && relative.Person.Gender != (resident.Sex == ResidentSex.Female ? Genders.Female : Genders.Male))
                    throw new InvalidOperationException("Rendered sex does not match saved family role.");
            }
            Vector3 childCenter = flat.GetComponent<MeshRenderer>().bounds.center;
            var indoor = game.PlayerEnterExit.Interior;
            bool foundView = false;
            foreach (var anchor in new InteriorGeometry(indoor).LocalAnchors)
            {
                Vector3 floor = indoor.transform.TransformPoint(anchor);
                float distance = Vector3.Distance(floor + Vector3.up, childCenter);
                if (distance < 2 || distance > 6 || Physics.Linecast(floor + Vector3.up * 1.6f, childCenter, ~0, QueryTriggerInteraction.Ignore)) continue;
                game.PlayerMotor.transform.position = floor + Vector3.up;
                foundView = true; break;
            }
            if (!foundView) throw new InvalidOperationException("No unobstructed family screenshot viewpoint.");
            yield return new WaitForSecondsRealtime(0.5f);
            game.PlayerMouseLook.SetFacing(childCenter - Camera.main.transform.position);
            yield return new WaitForEndOfFrame();
            Vector3 view = Camera.main.WorldToViewportPoint(childCenter);
            if (view.z <= 0 || view.x < 0.1f || view.x > 0.9f || view.y < 0.1f || view.y > 0.9f ||
                Physics.Linecast(Camera.main.transform.position, childCenter, ~0, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Child is not visible from the actual test camera.");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "LivingCities-family.png"));
            yield return new WaitForEndOfFrame();
            // Exercise the native target/window boundary without depending on a
            // random disposition refusal or synthesizing a player click.
            game.TalkManager.TalkToMobileNPC(actor.Person);
            DaggerfallUI.Instance.PopToHUD();
            DaggerfallUI.UIManager.PushWindow(DaggerfallUI.Instance.TalkWindow);
            yield return null;
            if (DaggerfallUI.UIManager.TopWindow.GetType().Name != "ChildGreetingWindow")
                throw new InvalidOperationException("Child exposes unrestricted adult conversation.");
            DaggerfallUI.Instance.PopToHUD();
            // TalkManager retains the last mobile reference during static NPC talks.
            // Model that boundary explicitly, without injecting a fake quest NPC.
            var typeField = typeof(TalkManager).GetField("currentNPCType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (typeField == null) throw new InvalidOperationException("NPC conversation type probe unavailable.");
            var previousType = typeField.GetValue(game.TalkManager);
            typeField.SetValue(game.TalkManager, TalkManager.NPCType.Static);
            DaggerfallUI.UIManager.PushWindow(DaggerfallUI.Instance.TalkWindow);
            bool untouched = DaggerfallUI.UIManager.TopWindow == DaggerfallUI.Instance.TalkWindow;
            DaggerfallUI.Instance.PopToHUD(); typeField.SetValue(game.TalkManager, previousType);
            if (!untouched) throw new InvalidOperationException("Child conversation intercepts a later static NPC talk.");
            string before = JsonUtility.ToJson(selected);
            saved = false; game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - family home");
            float deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) throw new InvalidOperationException("Family save timed out.");
            yield return null;
            int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - family home");
            loaded = false; game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
            deadline = Time.realtimeSinceStartup + 90;
            while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!loaded || !streets.Ready) throw new InvalidOperationException("Family reload timed out.");
            yield return new WaitForSecondsRealtime(1);
            world = (WorldState)adapter.GetSaveData();
            var restored = world.Settlements.Find(t => t.SettlementId == mapId).Households.Find(h => h.BuildingKey == selected.BuildingKey);
            actor = streets.FindActor(child.Id);
            if (before != JsonUtility.ToJson(restored) || !actor || actor.Person.NameNPC != childName)
                throw new InvalidOperationException("Family roles or child identity changed after indoor reload.");
            Debug.Log("LIVING_CITIES_TOWN_FAMILY_PASS: children=" + children + "; selectedHousehold=" + selected.Residents.Count +
                "; child=" + child.Id + "; name=" + childName + "; schema=" + world.Version);
        }
        private IEnumerator VerifyStreets()
        {
            var streets = GetComponent<StreetPopulation>();
            Debug.Log("LIVING_CITIES_TOWN_ACTORS_START: " + GetComponent<LivingCitiesMod>().TestStatus());
            float deadline = Time.realtimeSinceStartup + 100;
            while (!streets.Ready && Time.realtimeSinceStartup < deadline) yield return null;
            if (!streets.Ready || streets.PlannedCount == 0) throw new InvalidOperationException("No scheduled street population: " + GetComponent<LivingCitiesMod>().TestStatus());
            var game = GameManager.Instance;
            var now = DaggerfallUnity.Instance.WorldTime.Now;
            double currentSeconds = now.ToSeconds() + (now.Second - Math.Floor(now.Second));
            string id = null; Vector3 ground = Vector3.zero;
            int offset;
            for (offset = 0; offset < 1440; offset++)
                if (streets.TrySample(currentSeconds + offset * 60, out id, out ground)) break;
            if (id == null) throw new InvalidOperationException("No traveling resident in next day.");
            if (offset > 0) now.RaiseTime(offset * 60);
            Vector3 observer = ground + Vector3.back * 5;
            foreach (var direction in new[] { Vector3.back, Vector3.right, Vector3.left, Vector3.forward })
            {
                var candidate = ground + direction * 5;
                if (!Physics.Linecast(candidate + Vector3.up, ground + Vector3.up, ~0, QueryTriggerInteraction.Ignore))
                { observer = candidate; break; }
            }
            game.PlayerEntity.GodMode = true;
            game.PlayerMotor.transform.position = observer + Vector3.up * 2;
            game.PlayerMouseLook.SetHorizontalFacing(ground - observer);
            yield return new WaitForSecondsRealtime(2);
            var actor = streets.FindActor(id);
            if (!actor || !actor.gameObject.activeInHierarchy || !actor.Person.Asset.GetComponent<MeshRenderer>().enabled)
                throw new InvalidOperationException("Scheduled resident is not rendered.");
            Vector3 before = actor.transform.position;
            var billboard = actor.Person.Asset as MobilePersonBillboard;
            var frameField = typeof(MobilePersonBillboard).GetField("currentFrame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (!billboard || frameField == null) throw new InvalidOperationException("Classic sprite animation probe unavailable.");
            var frames = new HashSet<int>();
            float animationDeadline = Time.realtimeSinceStartup + 2;
            while (Time.realtimeSinceStartup < animationDeadline)
            {
                frames.Add((int)frameField.GetValue(billboard));
                yield return new WaitForEndOfFrame();
            }
            if (!actor || Vector3.Distance(before, actor.transform.position) < 0.2f)
                throw new InvalidOperationException("Scheduled resident did not move.");
            if (frames.Count < 3) throw new InvalidOperationException("Moving resident sprite did not cycle walking frames.");
            game.PlayerMouseLook.SetHorizontalFacing(actor.transform.position - game.PlayerMotor.transform.position);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "LivingCities-streets.png"));
            yield return new WaitForSecondsRealtime(1);
            Debug.Log("LIVING_CITIES_TOWN_ACTORS_PASS: planned=" + streets.PlannedCount + "; active=" + streets.ActiveCount +
                "; observed=" + id + "; name=" + actor.Person.NameNPC + "; animationFrames=" + frames.Count + "; movement=" + Vector3.Distance(before, actor.transform.position));
            string expectedName = actor.Person.NameNPC;
            uint expectedAppearance = actor.Resident.AppearanceSeed;
            // Advance to this actual traveller's arrival, then follow the real door.
            double departureSeconds = now.ToSeconds() + (now.Second - Math.Floor(now.Second));
            ActivitySnapshot arrived = default(ActivitySnapshot);
            for (offset = 1; offset < 1440; offset++)
            {
                arrived = streets.GetState(id, departureSeconds + offset * 60);
                if (arrived.Activity == Activity.Visit || arrived.Activity == Activity.Home) break;
            }
            if (offset >= 1440) throw new InvalidOperationException("Resident has no scheduled arrival.");
            now.RaiseTime(offset * 60);
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors owner = null; StaticDoor selectedDoor = default(StaticDoor);
            foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach (var door in candidate.Doors)
                    if (door.doorType == DoorTypes.Building && door.buildingKey == arrived.BuildingKey)
                    { owner = candidate; selectedDoor = door; break; }
            if (!owner) throw new InvalidOperationException("Arrival building has no exterior door.");
            EnterInterior(owner.transform, selectedDoor);
            yield return new WaitForSecondsRealtime(1);
            DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
            actor = streets.FindActor(id);
            if (!game.PlayerEnterExit.IsPlayerInsideBuilding || !actor || !actor.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Arrived resident missing from destination interior.");
            if (actor.Person.NameNPC != expectedName || actor.Resident.AppearanceSeed != expectedAppearance)
                throw new InvalidOperationException("Resident identity changed during interior transition.");
            Debug.Log("LIVING_CITIES_TOWN_INTERIOR_PASS: resident=" + id + "; building=" + arrived.BuildingKey + "; name=" + expectedName);
            game.PlayerMouseLook.SetHorizontalFacing(actor.transform.position - game.PlayerMotor.transform.position);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "LivingCities-interior.png"));
            game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            EnterInterior(owner.transform, selectedDoor);
            yield return new WaitForSecondsRealtime(1);
            DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
            actor = streets.FindActor(id);
            if (!actor || !actor.gameObject.activeInHierarchy || actor.Person.NameNPC != expectedName)
                throw new InvalidOperationException("Resident missing or changed after interior re-entry.");
            int duplicates = 0;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<ResidentActor>())
                if (candidate.gameObject.activeInHierarchy && candidate.Resident.Id == id) duplicates++;
            if (duplicates != 1) throw new InvalidOperationException("Resident actor duplicated across transitions.");
            SaveLoadManager.OnSave += OnSaved;
            saved = false;
            game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - interior continuity");
            deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) throw new InvalidOperationException("Interior fixture save timed out.");
            yield return null;
            int interiorKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - interior continuity");
            loaded = false; game.SaveLoadManager.Load(interiorKey); DaggerfallUI.Instance.PopToHUD();
            deadline = Time.realtimeSinceStartup + 90;
            while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!loaded || !streets.Ready) throw new InvalidOperationException("Living resident interior fixture reload timed out.");
            yield return new WaitForSecondsRealtime(1);
            actor = streets.FindActor(id);
            if (!actor || !actor.gameObject.activeInHierarchy || actor.Person.NameNPC != expectedName || actor.Resident.AppearanceSeed != expectedAppearance)
                throw new InvalidOperationException("Living resident missing or changed after interior save/load.");
            actor.GetComponent<DaggerfallEntityBehaviour>().Entity.SetHealth(0);
            yield return null;
            if (actor.Resident.IsAlive) throw new InvalidOperationException("Actor death did not update resident.");
            saved = false;
            game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - resident death");
            deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) throw new InvalidOperationException("Death fixture save timed out.");
            yield return null;
            int deathKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - resident death");
            loaded = false; game.SaveLoadManager.Load(deathKey); DaggerfallUI.Instance.PopToHUD();
            deadline = Time.realtimeSinceStartup + 90;
            while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!loaded || !streets.Ready) throw new InvalidOperationException("Interior death fixture reload timed out.");
            yield return new WaitForSecondsRealtime(2);
            bool foundDead = false;
            foreach (var settlement in ((WorldState)GetComponent<LivingCitiesMod>().GetSaveData()).Settlements)
                foreach (var home in settlement.Households)
                    foreach (var resident in home.Residents)
                        if (resident.Id == id) foundDead = !resident.IsAlive;
            if (!foundDead || streets.FindActor(id)) throw new InvalidOperationException("Dead resident respawned after load.");
            Debug.Log("LIVING_CITIES_TOWN_CONTINUITY_PASS: re-entry, one actor, death and interior save/load; resident=" + id);
        }
        private static void VerifyGeometry(BuildingDirectory directory, CityNavigation navigation)
        {
            var geometry = new TownGeometry(directory, navigation);
            if (geometry.Entrances.Count < 2) throw new InvalidOperationException("Town has insufficient reachable entrances.");
            var entrances = new List<TownGeometry.Entrance>(geometry.Entrances.Values);
            int routes = 0;
            for (int i = 1; i < Math.Min(entrances.Count, 12); i++)
            {
                var path = GridPathfinder.Find(geometry, entrances[0].Cell, entrances[i].Cell, 30000);
                if (path.Count > 1) routes++;
            }
            if (routes == 0) throw new InvalidOperationException("No real town entrance routes found.");
            Debug.Log("LIVING_CITIES_TOWN_GEOMETRY_PASS: entrances=" + geometry.Entrances.Count +
                "; rejectedDoors=" + geometry.RejectedDoors + "; sampledRoutes=" + routes);
        }
    }
}
