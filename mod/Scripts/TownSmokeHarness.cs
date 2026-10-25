using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit command-line test mode only; never starts a character during normal play.
    public sealed class TownSmokeHarness : MonoBehaviour
    {
        private bool saved, loaded;
        private string preparationProgress;
        private void OnGUI()
        {
            if(string.IsNullOrEmpty(preparationProgress))return;
            GUI.Box(new Rect(12,12,Math.Min(Screen.width-24,900),58),preparationProgress);
        }
        private void Awake() { ReleaseTestCursor(); }
        private static Transform watchedTarget;
        private static Vector3 watchedOffset;
        internal static void Watch(Transform target, Vector3 offset = default(Vector3))
        { watchedTarget = target; watchedOffset = offset; }
        private void LateUpdate()
        {
            ReleaseTestCursor();
            if (watchedTarget && watchedTarget.gameObject.activeInHierarchy && Camera.main)
                SleepProbe.AimCamera(watchedTarget.position + watchedOffset);
        }
        private void OnApplicationFocus(bool focused) { ReleaseTestCursor(); }
        private static void ReleaseTestCursor()
        {
            // This component exists only in the explicitly launched test player.
            // Transitions can reset native cursor settings, so keep ownership here.
            var game = GameManager.Instance;
            if (game != null && game.IsReady && game.PlayerMouseLook) game.PlayerMouseLook.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
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
        private IEnumerator PrepareOutdoorFixture(PeopleOfTheIliacBayMod adapter,StreetPopulation streets,string character,string saveName)
        {
            var game=GameManager.Instance;
            if(!NativeProbeText.IsTestCharacter(character) || game.PlayerEntity.Name!=character)
                throw new InvalidOperationException("Outdoor preparation requires the explicitly selected test character.");
            game.PlayerEntity.GodMode=true;
            string legacyName=saveName+" legacy routes";
            game.SaveLoadManager.EnumerateSaves();
            if(game.SaveLoadManager.FindSaveFolderByNames(character,legacyName)<0)
                yield return SavePreparedFixture(character,legacyName);
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double now=clock.ToSeconds();
            clock.RaiseTime((float)(Math.Floor(now/86400d)*86400d+86400+5*3600-now));
            float start=Time.realtimeSinceStartup,deadline=start+900,nextReport=0;
            int prepared=0,total=0;
            while(Time.realtimeSinceStartup<deadline)
            {
                if(Time.realtimeSinceStartup>=nextReport || streets.OutdoorRoutesPrepared)
                {
                    CountPreparedRoutes(adapter,out prepared,out total);
                    preparationProgress="Preparing saved outdoor routes: "+prepared+" / "+total+"\n"+
                        Math.Round(Time.realtimeSinceStartup-start)+" seconds. This fixture will be saved for reuse.";
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FIXTURE_PREPARATION: prepared="+prepared+"; total="+total+"; seconds="+(Time.realtimeSinceStartup-start));
                    nextReport=Time.realtimeSinceStartup+15;
                }
                if(streets.OutdoorRoutesPrepared)break;
                yield return null;
            }
            if(!streets.OutdoorRoutesPrepared)throw new InvalidOperationException("Outdoor fixture preparation timed out.");
            // Publication at home can finish just after route work completes.
            yield return new WaitForSecondsRealtime(2);
            CountPreparedRoutes(adapter,out prepared,out total);
            if(prepared==0)throw new InvalidOperationException("No personal routes prepared; refusing to overwrite fixture.");
            if(prepared<total)Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_FIXTURE_DEFERRED_ROUTES: "+(total-prepared)+" routes still require safe reconstruction or home publication.");
            preparationProgress="Saving prepared test fixture: "+prepared+" / "+total+" personal routes.";
            yield return SavePreparedFixture(character,saveName);
            game.SaveLoadManager.EnumerateSaves();
            int slot=game.SaveLoadManager.FindSaveFolderByNames(character,saveName);
            if(slot<0)throw new InvalidOperationException("Prepared fixture save missing.");
            loaded=false;game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
            deadline=Time.realtimeSinceStartup+90;
            while((!loaded||!streets.Ready)&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!loaded||!streets.Ready)throw new InvalidOperationException("Prepared fixture reload timed out.");
            int restored,restoredTotal;CountPreparedRoutes(adapter,out restored,out restoredTotal);
            if(restored<prepared||restoredTotal!=total)throw new InvalidOperationException("Prepared route versions were not preserved on reload.");
            preparationProgress=null;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FIXTURE_PREPARED_PASS: saved="+saveName+"; prepared="+restored+"; total="+total+"; legacy="+legacyName);
        }
        private static void CountPreparedRoutes(PeopleOfTheIliacBayMod adapter,out int prepared,out int total)
        {
            prepared=0;total=0;
            var state=(WorldState)adapter.GetSaveData();
            int map=GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory().MapID;
            var town=state.Routines.Find(t=>t.SettlementId==map);
            if(town==null)return;
            foreach(var routine in town.Residents)
            {
                if(routine.Itinerary==null&&routine.Route.Count<2)continue;
                total++;
                if(routine.PersonalLaneVersion==OutdoorRoutineMigration.CurrentPersonalLaneVersion)prepared++;
            }
        }
        private IEnumerator SavePreparedFixture(string character,string name)
        {
            saved=false;SaveLoadManager.OnSave+=OnSaved;
            try
            {
                GameManager.Instance.SaveLoadManager.Save(character,name);
                float deadline=Time.realtimeSinceStartup+30;
                while(!saved&&Time.realtimeSinceStartup<deadline)yield return null;
                if(!saved)throw new InvalidOperationException("Fixture save timed out: "+name);
            }
            finally{SaveLoadManager.OnSave-=OnSaved;}
        }
        private void OnSaved(SaveData_v1 ignored) { saved = true; }
        private void OnLoaded(SaveData_v1 ignored)
        {
            // This harness exists only in an explicitly launched test process.
            // Restore protection after every fixture load, before any time skip.
            GameManager.Instance.PlayerEntity.GodMode=true;
            loaded = true;
        }
        private void OnDestroy() { watchedTarget=null; SaveLoadManager.OnSave -= OnSaved; SaveLoadManager.OnLoad -= OnLoaded; }
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
            string[] startupArguments = Environment.GetCommandLineArgs();
            if (Array.IndexOf(startupArguments, BreezeTtsTrialCapacityProbe.RunFlag) >= 0)
            {
                yield return BreezeTtsTrialCapacityProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: isolated Breeze TTS request capacity and closed-registry cleanup.");
                yield break;
            }
            DFLocation location;
            if (!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall", "Gothway Garden", out location))
            { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: test settlement missing."); yield break; }
            DFPosition pixel = MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude, location.MapTableData.Latitude);
            string[] arguments = Environment.GetCommandLineArgs();
            int fixtureArgument = Array.IndexOf(arguments, "-peopleoftheiliacbay-fixture-character");
            if (fixtureArgument >= 0)
            {
                if (fixtureArgument + 1 >= arguments.Length || !arguments[fixtureArgument + 1].StartsWith("PeopleOfTheIliacBayTest-", StringComparison.Ordinal))
                { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: invalid fixture character."); yield break; }
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
            string freshCharacter = "PeopleOfTheIliacBayTest-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            start.CharacterDocument = new CharacterDocument {
                name = freshCharacter,
                backStory = new List<string> { "Disposable People of the Iliac Bay integration-test character." },
                biographyEffects = new List<string>() };
            start.StartMethod = StartGameBehaviour.StartMethods.NewCharacter;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_START: " + location.Name + "; pixel=" + pixel.X + "," + pixel.Y);
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
            { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: town did not finish loading."); yield break; }
            yield return new WaitForSecondsRealtime(2);
            // Dismiss only the disposable new-character introduction, leaving the HUD.
            if (DaggerfallUI.UIManager.TopWindow is DaggerfallMessageBox)
                DaggerfallUI.UIManager.PopWindow();
            if (Array.IndexOf(arguments, "-peopleoftheiliacbay-fresh-previews") >= 0)
            {
                yield return FreshPreviewSaves.Run(GetComponent<StreetPopulation>(), GetComponent<GuardPopulation>(), freshCharacter);
                yield break;
            }
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
            { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: incomplete town census."); yield break; }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_SURVEY_PASS: map=" + directory.MapID + "; buildings=" + directory.BuildingCount +
                "; homes=" + homes + "; doors=" + doors + "; walkable=" + walkable + "; grid=" + nav.NavGridWidth + "x" + nav.NavGridHeight);
            game.PlayerEntity.GodMode = true;
            game.PlayerMotor.transform.position = nav.WorldToScenePosition(nav.NavGridToWorldPosition(centerCell)) + Vector3.up * 2;
            yield return new WaitForSecondsRealtime(3);
            SaveLoadManager.OnSave += OnSaved;
            saved = false;
            game.SaveLoadManager.Save(game.PlayerEntity.Name, "People of the Iliac Bay - inside Gothway Garden");
            deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: inside save timed out."); yield break; }
            yield return null;
            int insideKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "People of the Iliac Bay - inside Gothway Garden");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEST_SAVE: inside=" + game.SaveLoadManager.GetSaveFolder(insideKey));

            // One map pixel away, outside the settlement's location rect. Town geometry
            // may still be streamed nearby; reload this save to exercise a fresh scene.
            game.StreamingWorld.TeleportToCoordinates(pixel.X + 1, pixel.Y);
            deadline = Time.realtimeSinceStartup + 90;
            yield return null;
            while ((!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer || game.PlayerGPS.CurrentMapPixel.X != pixel.X + 1) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            if (!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer ||
                game.PlayerGPS.CurrentMapPixel.X != pixel.X + 1 || game.PlayerGPS.CurrentMapPixel.Y != pixel.Y)
            { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: outside reposition timed out."); yield break; }
            yield return new WaitForSecondsRealtime(3);
            saved = false;
            game.SaveLoadManager.Save(game.PlayerEntity.Name, "People of the Iliac Bay - outside Gothway Garden");
            deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: outside save timed out."); yield break; }
            yield return null;
            int outsideKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "People of the Iliac Bay - outside Gothway Garden");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEST_SAVE: outside=" + game.SaveLoadManager.GetSaveFolder(outsideKey));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_SAVES_PASS: character=" + game.PlayerEntity.Name + "; insideKey=" + insideKey + "; outsideKey=" + outsideKey);
            yield return VerifyLoads(game.PlayerEntity.Name, pixel, location.MapTableData.MapId, true);
        }

        private IEnumerator VerifyLoads(string character, DFPosition pixel, int mapId, bool requireFamily = false)
        {
            var game = GameManager.Instance;
            var adapter = GetComponent<PeopleOfTheIliacBayMod>();
            var streets = GetComponent<StreetPopulation>();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-decision-shadow-probe") >= 0)
                DisableDialogueServicesForDecisionShadowProbe(adapter);
            SaveLoadManager.OnLoad += OnLoaded;
            var restartArgs=Environment.GetCommandLineArgs();
            if(Environment.GetEnvironmentVariable("POTIB_QUEST_PERSON_LOCATION_PROBE")=="1" &&
                Array.IndexOf(restartArgs,"-peopleoftheiliacbay-quest-binding-probe")>=0)
            {
                const string ownedCharacter="PeopleOfTheIliacBayTest-9a5f197b";
                const string ownedSave="People - Gothway Garden";
                int saveArgument=Array.IndexOf(restartArgs,"-peopleoftheiliacbay-fixture-save");
                if(character!=ownedCharacter || saveArgument<0 || saveArgument+1>=restartArgs.Length ||
                    restartArgs[saveArgument+1]!=ownedSave)
                    throw new InvalidOperationException("Quest person location requires the exact owned Gothway Garden character and save.");
                game.SaveLoadManager.EnumerateSaves();
                int fixture=game.SaveLoadManager.FindSaveFolderByNames(character,ownedSave);
                if(fixture<0)throw new InvalidOperationException("Owned Gothway Garden quest fixture is missing.");
                loaded=false;game.SaveLoadManager.Load(fixture);DaggerfallUI.Instance.PopToHUD();
                float deadline=Time.realtimeSinceStartup+90;
                while((!loaded || !streets.Ready || !game.StreamingWorld.IsReady ||
                    game.StreamingWorld.IsRepositioningPlayer) && Time.realtimeSinceStartup<deadline)yield return null;
                if(!loaded || !streets.Ready || !game.StreamingWorld.IsReady ||
                    game.StreamingWorld.IsRepositioningPlayer ||
                    game.PlayerEntity.Name!=ownedCharacter || game.PlayerEnterExit.IsPlayerInside ||
                    !game.StreamingWorld.GetCurrentBuildingDirectory())
                    throw new InvalidOperationException("Owned Gothway Garden quest fixture did not load an exterior town.");
                yield return QuestBindingProbeHost.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: scoped native quest person location; character="+character);
                yield break;
            }
            if(Array.IndexOf(restartArgs,NpcVerbalCourtNativeProbe.RunFlag)>=0)
            {
                int saveArgument=Array.IndexOf(restartArgs,"-peopleoftheiliacbay-fixture-save");
                if(saveArgument<0 || saveArgument+1>=restartArgs.Length)throw new InvalidOperationException("Verbal court requires the owned fixture save.");
                yield return NpcVerbalCourtNativeProbe.Run(adapter,streets,character,restartArgs[saveArgument+1]);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native verbal complaint and court.");
                yield break;
            }
            int restartRead=Array.IndexOf(restartArgs,"-peopleoftheiliacbay-commerce-restart-read");
            if(restartRead>=0)
            {
                if(restartRead+1>=restartArgs.Length)throw new InvalidOperationException("Commerce restart checkpoint missing.");
                yield return NpcCommerceRestartProbe.Resume(streets,character,restartArgs[restartRead+1]);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: independent-process commerce restart.");
                yield break;
            }
            if(Array.IndexOf(restartArgs,"-peopleoftheiliacbay-npc-starting-belongings-scene-probe")>=0)
            {
                yield return NativeNpcStartingBelongingsSceneProbe.RunFixture(adapter,streets,character);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native starting belongings first spawn and disk reload.");
                yield break;
            }
            if(Array.IndexOf(restartArgs,"-peopleoftheiliacbay-container-deposit-probe")>=0)
            {
                yield return NativeNpcHomeContainerDepositProbe.RunFixture(adapter,streets,character);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native own-home wait, gift, deposit, and household retrieval through normal acceptance, physical arrival, save/reload, and replay rejection.");
                yield break;
            }
            if(Array.IndexOf(restartArgs,"-peopleoftheiliacbay-temple-training-probe")>=0)
            {
                yield return NativeNpcTempleTrainingProbe.RunFixture(adapter,streets,character);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native temple training receipt timer.");
                yield break;
            }
            if(Array.IndexOf(restartArgs,"-peopleoftheiliacbay-task-failure-probe")>=0)
            {
                yield return NativeNpcShopPurchaseProbe.RunFixture(adapter,streets,character,false,true);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native task failure feedback.");
                yield break;
            }
            if(Array.IndexOf(restartArgs,"-peopleoftheiliacbay-routine-recovery-physical-probe")>=0)
            {
                bool dayProbe=Array.IndexOf(restartArgs,"-peopleoftheiliacbay-routine-recovery-day-probe")>=0;
                yield return NativeNpcShopPurchaseProbe.RunFixture(adapter,streets,character,false,false,true,dayProbe);
                if(dayProbe)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: physical routine recovery day.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: physical routine recovery.");
                yield break;
            }
            if(Array.IndexOf(restartArgs,"-peopleoftheiliacbay-routine-recovery-scenechange-probe")>=0)
            {
                yield return RoutineRecoveryOutdoorEntryProbe.Run(streets,character);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: outdoor routine recovery survived unrelated native scene entry.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-loot-sale-probe")>=0)
            {
                yield return NativeNpcShopPurchaseProbe.RunFixture(adapter,streets,character,false,false,false,false,true);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native shop owned-loot sale.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-commerce-probe")>=0)
            {
                yield return NativeNpcShopPurchaseProbe.RunFixture(adapter,streets,character,
                    Array.IndexOf(restartArgs,"-peopleoftheiliacbay-commerce-restart-probe")>=0);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native shop commerce.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-inn-lodger-home-probe")>=0)
            {
                yield return BedroomReplacementProbe.Run(adapter,streets,character);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: bedroom replacements.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-keeper-standing-probe")>=0)
            {
                yield return KeeperStandingProbe.Run(streets,character);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: directional keeper idle preview.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-appointment-probe")>=0)
            {
                yield return AppointmentRuntimeProbe.Run(adapter,streets,character);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: appointment ledger and schedule.");
                yield break;
            }
            if(Array.IndexOf(restartArgs,"-peopleoftheiliacbay-appointment-no-show-probe")>=0)
            {
                yield return AppointmentRuntimeProbe.Run(adapter,streets,character,true);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: physically witnessed appointment no-show consequences.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-business-identity-probe")>=0)
            {
                yield return BusinessIdentityProbe.Run(adapter,streets,character);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: business identities.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-occupied-approach-probe")>=0)
            {
                yield return OccupiedApproachProbe.Run(adapter,streets,character);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: occupied approach and free browsing.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-movement-clock-probe")>=0)
            {
                yield return MovementClockProbe.Run(adapter,streets,character);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: physical movement at 2x clock.");
                yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-memory-archive-probe")>=0)
            {
                yield return SharedMemoryArchiveProbe.Run(adapter,character,
                    Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-memory-archive-read")>=0);
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-memory-archive-model-probe")>=0)
                    yield return SharedMemoryRecallModelProbe.Run(adapter);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: shared archive timeline probe.");
                yield break;
            }
            bool gateGuardProbe=Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-gate-guard-probe")>=0;
            bool crossRegionTripProbe=Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-cross-region-trip-probe")>=0;
            bool residentTripProbe=Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-resident-trip-probe")>=0;
            bool dailyItineraryProbe=Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-daily-itinerary-probe")>=0;
            bool tavernSurveyProbe=Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-tavern-survey-probe")>=0;
            bool followTransitionProbe=Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-follow-transition-probe")>=0;
            bool leadProbe=Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-lead-probe")>=0;
            bool outdoorArrivalProbe=Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-outdoor-arrival-probe")>=0;
            bool outdoorNavigationProbe=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-outdoor-navigation-probe")>=0;
            bool locationBlockProbe=Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-location-block-probe")>=0;
            bool outdoorSceneProbe=Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-outdoor-scene-probe")>=0;
            bool morningLoiterProbe=Array.IndexOf(Environment.GetCommandLineArgs(),MorningLoiterProbe.RunFlag)>=0;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-haunting-dawn-probe") >= 0 || RuntimeTiming.Enabled || outdoorSceneProbe || locationBlockProbe || residentTripProbe || crossRegionTripProbe || dailyItineraryProbe || followTransitionProbe || leadProbe || outdoorArrivalProbe || outdoorNavigationProbe || morningLoiterProbe || ((gateGuardProbe || tavernSurveyProbe) && Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-fixture-save")>=0))
            {
                game.SaveLoadManager.EnumerateSaves();
                var testArguments=Environment.GetCommandLineArgs();
                int saveArgument=Array.IndexOf(testArguments,"-peopleoftheiliacbay-fixture-save");
                string citySave=saveArgument>=0 && saveArgument+1<testArguments.Length ? testArguments[saveArgument+1] : "People of the Iliac Bay - Daggerfall city preview";
                int fixture = game.SaveLoadManager.FindSaveFolderByNames(character, citySave);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CITY_FIXTURE: "+citySave);
                if (fixture < 0) throw new InvalidOperationException("Daggerfall preview fixture missing.");
                loaded = false; game.SaveLoadManager.Load(fixture); DaggerfallUI.Instance.PopToHUD();
                float limit = Time.realtimeSinceStartup + (Array.IndexOf(testArguments,"-peopleoftheiliacbay-editor-profile")>=0?240:60);
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < limit) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Daggerfall preview load timed out.");
                if(locationBlockProbe)
                {
                    yield return IncrementalLocationBlocksProbe.Run(character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: incremental native location block parity.");
                    yield break;
                }
                if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-prepare-outdoor-fixture")>=0)
                    yield return PrepareOutdoorFixture(adapter,streets,character,citySave);
                else if(morningLoiterProbe) yield return MorningLoiterProbe.Run(streets);
                else if(outdoorSceneProbe) yield return CorpseResponseNativeProbe.Run(adapter,streets,character);
                else if(gateGuardProbe) yield return GateGuardProbe.Run(FindObjectOfType<GuardPopulation>());
                else if(tavernSurveyProbe)yield return TavernSurveyProbe.Run();
                else if(leadProbe) yield return NativeNpcLeadProbe.Run(adapter,streets,character);
                else if(followTransitionProbe) yield return NpcFollowTransitionProbe.RunFixture(adapter,streets,character);
                else if(outdoorNavigationProbe)
                {
                    if(Environment.GetEnvironmentVariable("POTIB_NAVMESH_TRIAL")=="1")yield return NativeNavigationTrial.Run();
                    else yield return OutdoorNavigationProbe.Run(game.StreamingWorld.GetCurrentBuildingDirectory(),game.StreamingWorld.GetCurrentCityNavigation(),(WorldState)adapter.GetSaveData());
                }
                else if(outdoorArrivalProbe)
                {
                    yield return OutdoorArrivalContinuityProbe.Run(adapter,streets);
                }
                else if(dailyItineraryProbe)
                {
                    if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-indoor-crowd-probe")>=0)
                    {
                        yield return IndoorCrowdProgressProbe.RunExistingVenue(streets);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: independent dense crowd observation.");
                        yield break;
                    }
                    if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-indoor-catchup-probe")>=0)
                    {
                        yield return IndoorCatchupProbe.Run(streets);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: indoor time skip.");
                        yield break;
                    }
                    // An indoor-only acceptance run needs the restored itinerary
                    // and surveyed doors, not every citizen's outdoor migration.
                    if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-daily-interior-only")<0 &&
                        Array.IndexOf(testArguments,"-peopleoftheiliacbay-household-body-probe")<0)
                        yield return DailyItineraryProbe.Run(adapter,streets);
                    if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-household-body-probe")>=0)
                        yield return HouseholdJointBodyProbe.Run(adapter,streets);
                    if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-household-inn-physical-probe")>=0)
                        yield return HouseholdInnPhysicalProbe.Run(adapter,streets);
                    if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-physical-daily-routine")>=0)
                        yield return PhysicalDailyRoutineProbe.Run(adapter,streets);
                }
                else if(RuntimeTiming.Enabled)
                {
                    if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-profile-only")>=0)
                    {
                        if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-scene-observers")>=0)
                            yield return streets.VerifySceneObservers();
                        else if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-street-encounter")>=0)
                            yield return streets.VerifyStreetEncounter();
                        else if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-static-trade")>=0)
                            yield return StaticPersonalTradeNativeProbe.RunFixture(adapter,streets,character);
                        else if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-murder-report")>=0)
                            yield return streets.VerifyMurderReport();
                        else if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-inn-diversion")>=0)
                            yield return streets.VerifyInnDiversionTransition();
                        else if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-morning-shop")>=0)
                            yield return streets.VerifyMorningClothingShop();
                        else if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-leisure")>=0)
                            yield return streets.VerifyClosedShopLeisure();
                        else if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-wardrobe")>=0)
                            Debug.Log(NativeNpcOutfitProbe.Run());
                        else if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-crowd")>=0)
                            yield return NativePopulationPlaytestProbe.Run(streets);
                        else if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-shopping")>=0)
                            yield return ShopVisitProbe.RunLocal(Array.IndexOf(testArguments,"-peopleoftheiliacbay-shop-buyer-only")>=0);
                        else if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-arrival")>=0)
                            yield return OutdoorArrivalContinuityProbe.Run(adapter,streets);
                        else yield return LoiterPerformanceProbe.Run(streets);
                        if(Array.IndexOf(testArguments,"-peopleoftheiliacbay-verify-playtest")>=0)
                        {
                            Debug.Log(NativeNpcOutfitProbe.Run());
                            Debug.Log(NativeNpcInventoryOwnerProbe.Run());
                            yield return PersonalTradeNativeProbe.Run(adapter,streets);
                            yield return IncrementalLocationBlocksProbe.Run(character);
                        }
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_EDITOR_PROFILE_COMPLETE");
                        yield break;
                    }
                    yield return PerformanceProbe.Run();
                    yield return DailyItineraryProbe.Run(adapter,streets);
                    yield return MurderWitnessProbe.Run(adapter,streets);
                }
                else if(crossRegionTripProbe) yield return CrossRegionTripProbe.Run();
                else if(residentTripProbe) yield return ResidentTripProbe.Run();
                else yield return HauntingDawnProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: targeted city probe.");
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-dialogue-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-dialogue-refusal-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-semantic-conversation-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-decision-shadow-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-decision-routing-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-decision-routing-appraisal-reopen-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-decision-willingness-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), GoalQwenWillingnessProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-binding-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-sequence-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-cancellation-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-two-pass-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), GoalChainLengthProbe.Switch) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-context-snapshot-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-context-speaker-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), KeeperWorkDutyProbe.Flag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), NativeNpcPoisonExperienceProbe.Flag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), NativeNpcPoisonAppraisalProbe.Flag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-conversation-reply-outcome-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-friendship-progression-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-npc-identity-integration-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-social-dialogue-stream-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-breeze-tts-settings-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), BreezeTtsMixerReceiptProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-breeze-tts-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-experience-appraisal-transport-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), NativeSocialSkillTallyProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-experience-inbox-deferred-serialization-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), BreezeTtsStreamingAcceptanceProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), SpeechRevealPresentationProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), NpcAcuteFearProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-native-combat-role-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-citywatch-wall-retreat-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), NpcResentmentRecoveryProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), NativeTopicTtsProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), SplitPortraitRuntimeProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), LiveDialoguePresentationProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-tts-contrast-manifest") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-conversation-mood-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), MemoryArchitectureProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), SocialDuplicateReplyProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-conversation-action-mood-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-dialogue-availability-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-action-wording-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-appointment-point-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-move-to-wait-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-enter-wait-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), NativeNpcRepairDropoffProbe.RunFlag) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-npc-interaction-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-npc-vitals-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-npc-healthcare-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-refuge-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-regional-probe") >= 0)
            {
                game.SaveLoadManager.EnumerateSaves();
                var dialogueArguments=Environment.GetCommandLineArgs();
                string[] ownedNativeProbeFlags = {
                    "-peopleoftheiliacbay-npc-healthcare-probe",
                    SocialDuplicateReplyProbe.RunFlag,
                    "-peopleoftheiliacbay-npc-identity-integration-probe",
                    "-peopleoftheiliacbay-social-dialogue-stream-probe",
                    "-peopleoftheiliacbay-breeze-tts-settings-probe",
                    BreezeTtsMixerReceiptProbe.RunFlag,
                    SpeechRevealPresentationProbe.RunFlag,
                    NpcAcuteFearProbe.RunFlag,
                    "-peopleoftheiliacbay-native-combat-role-probe",
                    "-peopleoftheiliacbay-citywatch-wall-retreat-probe",
                    NpcResentmentRecoveryProbe.RunFlag,
                    KeeperWorkDutyProbe.Flag,
                    NativeNpcPoisonExperienceProbe.Flag,
                    NativeNpcPoisonAppraisalProbe.Flag,
                    "-peopleoftheiliacbay-experience-appraisal-transport-probe",
                    NativeSocialSkillTallyProbe.RunFlag,
                    "-peopleoftheiliacbay-experience-inbox-deferred-serialization-probe",
                    "-peopleoftheiliacbay-decision-routing-appraisal-reopen-probe",
                    BreezeTtsStreamingAcceptanceProbe.RunFlag,
                    NativeTopicTtsProbe.RunFlag,
                    SplitPortraitRuntimeProbe.RunFlag,
                    LiveDialoguePresentationProbe.RunFlag,
                    MemoryArchitectureProbe.RunFlag
                };
                int ownedNativeProbeCount = 0;
                foreach (string probeFlag in ownedNativeProbeFlags)
                    if (Array.IndexOf(dialogueArguments, probeFlag) >= 0) ownedNativeProbeCount++;
                if (ownedNativeProbeCount > 1)
                    throw new InvalidOperationException("Controlled native probes must run one at a time.");
                if (ownedNativeProbeCount == 1 &&
                    (Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-fixture-character") < 0 ||
                    Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-fixture-save") < 0 ||
                    Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-fixture-save") + 1 >= dialogueArguments.Length ||
                    dialogueArguments[Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-fixture-save") + 1] != "People - Gothway Garden" ||
                    character != "PeopleOfTheIliacBayTest-9a5f197b"))
                    throw new InvalidOperationException("Controlled native probes require the exact owned character and Gothway Garden save.");
                int dialogueSaveArgument=Array.IndexOf(dialogueArguments,"-peopleoftheiliacbay-fixture-save");
                if ((Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-breeze-tts-probe") >= 0 ||
                    Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-breeze-tts-settings-probe") >= 0 ||
                    Array.IndexOf(dialogueArguments, BreezeTtsStreamingAcceptanceProbe.RunFlag) >= 0 ||
                    Array.IndexOf(dialogueArguments, SpeechRevealPresentationProbe.RunFlag) >= 0 ||
                    Array.IndexOf(dialogueArguments, NativeTopicTtsProbe.RunFlag) >= 0 ||
                    Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-tts-contrast-manifest") >= 0) &&
                    (dialogueSaveArgument < 0 || dialogueSaveArgument + 1 >= dialogueArguments.Length ||
                    dialogueArguments[dialogueSaveArgument + 1] != "People - Gothway Garden"))
                    throw new InvalidOperationException("Breeze TTS pilot requires explicit -peopleoftheiliacbay-fixture-save \"People - Gothway Garden\" on the owned disposable character.");
                int fixture = game.SaveLoadManager.FindSaveFolderByNames(character, dialogueSaveArgument>=0 ? dialogueArguments[dialogueSaveArgument+1] : "People of the Iliac Bay - inside Gothway Garden");
                if (fixture < 0) fixture = game.SaveLoadManager.FindSaveFolderByNames(character, "People - Gothway Garden");
                if (fixture < 0) throw new InvalidOperationException("Exterior fixture missing.");
                loaded = false; game.SaveLoadManager.Load(fixture); DaggerfallUI.Instance.PopToHUD();
                float limit = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < limit) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Exterior fixture load timed out.");
                if (ownedNativeProbeCount == 1 && game.PlayerEntity.Name != character)
                    throw new InvalidOperationException("Loaded controlled fixture belongs to a different character.");
                if (Array.IndexOf(dialogueArguments, SplitPortraitRuntimeProbe.RunFlag) >= 0)
                {
                    yield return SplitPortraitRuntimeProbe.Run(adapter, streets, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native split portrait ownership, composition and no-model idle animation.");
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, LiveDialoguePresentationProbe.RunFlag) >= 0)
                {
                    yield return LiveDialoguePresentationProbe.Run(adapter, streets, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-experience-appraisal-transport-probe") >= 0)
                {
                    yield return adapter.RunNpcExperienceAppraisalTransportProbe(character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: typed appraisal request-bound transport and isolated commit.");
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, NativeSocialSkillTallyProbe.RunFlag) >= 0)
                {
                    NativeSocialSkillTallyProbe.Run(adapter, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-experience-inbox-deferred-serialization-probe") >= 0)
                {
                    NpcExperienceCommitProbe.VerifyDeferredInboxSerialization();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: deferred experience inbox FullSerializer roundtrip.");
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-breeze-tts-probe") >= 0)
                {
                    yield return BreezeTtsTrialProbe.Run(adapter, streets, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-npc-identity-integration-probe") >= 0)
                {
                    yield return NpcIdentityIntegrationProbe.Run(adapter, streets, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native identity integration; event profile, action snapshot, and exact baseline restore.");
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-social-dialogue-stream-probe") >= 0)
                {
                    yield return SocialDialogueStreamingProbe.Run(adapter, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native social dialogue streaming transport.");
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-breeze-tts-settings-probe") >= 0)
                {
                    yield return BreezeTtsSettingsProbe.Run(adapter, streets, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: production Breeze settings, greeting, cancellation, and reopen.");
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, BreezeTtsMixerReceiptProbe.RunFlag) >= 0)
                {
                    if (dialogueSaveArgument < 0 || dialogueSaveArgument + 1 >= dialogueArguments.Length ||
                        dialogueArguments[dialogueSaveArgument + 1] != "People - Gothway Garden" ||
                        character != "PeopleOfTheIliacBayTest-9a5f197b")
                        throw new InvalidOperationException("Mixer receipt probe requires the exact owned character and Gothway Garden save.");
                    yield return BreezeTtsMixerReceiptProbe.Run(character, dialogueArguments[dialogueSaveArgument + 1]);
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, NpcAcuteFearProbe.RunFlag) >= 0)
                {
                    yield return NpcAcuteFearProbe.Run(adapter, streets, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-native-combat-role-probe") >= 0)
                {
                    yield return NativeNpcCombatRoleProbe.Run(adapter, streets, adapter.NativeCombatWorldSeed);
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-citywatch-wall-retreat-probe") >= 0)
                {
                    yield return CityWatchWallRetreatProbe.Run(adapter, adapter.GetComponent<GuardPopulation>(), adapter.NativeCombatWorldSeed);
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, NpcResentmentRecoveryProbe.RunFlag) >= 0)
                {
                    yield return NpcResentmentRecoveryProbe.Run(adapter, streets, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, SpeechRevealPresentationProbe.RunFlag) >= 0)
                {
                    int wavArgument = Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-speech-reveal-retained-wav");
                    if (wavArgument < 0 || wavArgument + 1 >= dialogueArguments.Length)
                        throw new InvalidOperationException("Speech reveal probe requires the approved retained WAV path.");
                    yield return SpeechRevealPresentationProbe.Run(adapter, streets, character,
                        dialogueArguments[dialogueSaveArgument + 1], dialogueArguments[wavArgument + 1]);
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, BreezeTtsStreamingAcceptanceProbe.RunFlag) >= 0)
                {
                    yield return BreezeTtsStreamingAcceptanceProbe.Run(adapter, streets, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, NativeTopicTtsProbe.RunFlag) >= 0)
                {
                    yield return NativeTopicTtsProbe.Run(adapter, streets, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    yield break;
                }
                if (Array.IndexOf(dialogueArguments, "-peopleoftheiliacbay-tts-contrast-manifest") >= 0)
                {
                    yield return BreezeTtsContrastProbe.Run(adapter, streets, character,
                        dialogueArguments[dialogueSaveArgument + 1]);
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-wording-probe")>=0)
                {
                    yield return ActionWordingProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: receipt-grounded dynamic action wording and stale completion lifecycle.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-conversation-reply-outcome-probe")>=0)
                {
                    yield return ConversationReplyOutcomeProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native authoritative action acknowledgment.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-friendship-progression-probe")>=0)
                {
                    int saveArgument=Array.IndexOf(dialogueArguments,"-peopleoftheiliacbay-fixture-save");
                    if(saveArgument<0 || saveArgument+1>=dialogueArguments.Length)
                        throw new InvalidOperationException("Friendship progression probe requires its explicit owned city save.");
                    yield return FriendshipProgressionProbe.RunFixture(adapter,streets,character,dialogueArguments[saveArgument+1]);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native friendship progression, disk reload, and exact baseline restore.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-conversation-mood-probe")>=0)
                {
                    int saveArgument=Array.IndexOf(dialogueArguments,"-peopleoftheiliacbay-fixture-save");
                    if(saveArgument<0 || saveArgument+1>=dialogueArguments.Length)
                        throw new InvalidOperationException("Conversation mood probe requires its explicit owned city save.");
                    yield return ConversationMoodProbe.Run(adapter,streets,character,dialogueArguments[saveArgument+1]);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: persistent conversation mood, live typed reply, stale callback, disk rollback, and baseline restoration.");
                    yield break;
                }
                if(Array.IndexOf(dialogueArguments,MemoryArchitectureProbe.RunFlag)>=0)
                {
                    int saveArgument=Array.IndexOf(dialogueArguments,"-peopleoftheiliacbay-fixture-save");
                    if(saveArgument<0 || saveArgument+1>=dialogueArguments.Length ||
                        dialogueArguments[saveArgument+1]!="People - Gothway Garden")
                        throw new InvalidOperationException("Memory architecture probe requires explicit -peopleoftheiliacbay-fixture-save \"People - Gothway Garden\".");
                    yield return MemoryArchitectureProbe.Run(adapter,streets,character,dialogueArguments[saveArgument+1]);
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),SocialDuplicateReplyProbe.RunFlag)>=0)
                {
                    int saveArgument=Array.IndexOf(dialogueArguments,"-peopleoftheiliacbay-fixture-save");
                    if(saveArgument<0 || saveArgument+1>=dialogueArguments.Length)
                        throw new InvalidOperationException("Social duplicate probe requires its explicit owned city save.");
                    yield return SocialDuplicateReplyProbe.Run(adapter,streets,character,dialogueArguments[saveArgument+1]);
                    if(SocialDuplicateReplyProbe.ExpectedRed)
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_EXPECTED_RED: social duplicate guard missing.");
                    else
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: exact social duplicate repair and observed-speech commit.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-conversation-action-mood-probe")>=0)
                {
                    int saveArgument=Array.IndexOf(dialogueArguments,"-peopleoftheiliacbay-fixture-save");
                    if(saveArgument<0 || saveArgument+1>=dialogueArguments.Length)
                        throw new InvalidOperationException("Conversation action mood probe requires its explicit owned city save.");
                    yield return ConversationActionMoodProbe.Run(adapter,streets,character,dialogueArguments[saveArgument+1]);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: receipt-grounded action mood commit, duplicate rephrase, stale completion, disk rollback, and baseline restoration.");
                    yield break;
                }
                if(Array.IndexOf(dialogueArguments,"-peopleoftheiliacbay-dialogue-availability-probe")>=0)
                {
                    int saveArgument=Array.IndexOf(dialogueArguments,"-peopleoftheiliacbay-fixture-save");
                    if(saveArgument<0 || saveArgument+1>=dialogueArguments.Length)
                        throw new InvalidOperationException("Dialogue availability probe requires its explicit owned city save.");
                    yield return DialogueAvailabilityProbe.Run(adapter,streets,character,dialogueArguments[saveArgument+1]);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native dialogue availability, isolated outage, recovery, transcript disk reload, and exact baseline restore.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-decision-shadow-probe")>=0)
                {
                    yield return DecisionShadowProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native decision shadow callback without world dispatch.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-decision-routing-appraisal-reopen-probe")>=0)
                {
                    yield return DecisionRoutingProbe.Run(adapter,streets,character);
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-decision-routing-probe")>=0)
                {
                    yield return DecisionRoutingProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: live decision routing deterministic native callbacks.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-decision-willingness-probe")>=0)
                {
                    yield return DecisionWillingnessProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: live D3 action willingness and native Wait lifecycle.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-goal-sequence-probe")>=0)
                {
                    yield return GoalSequenceProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: ordered goal physical execution.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-goal-cancellation-probe")>=0)
                {
                    yield return GoalCancellationProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: pending goal Talk-close cancellation.");
                    yield break;
                }
                var goalChainArguments = Environment.GetCommandLineArgs();
                if(Array.IndexOf(goalChainArguments,"-peopleoftheiliacbay-goal-two-pass-probe")>=0)
                {
                    int stepsArgument=Array.IndexOf(goalChainArguments,"-peopleoftheiliacbay-goal-chain-steps");
                    int stepCount;
                    if(stepsArgument<0 || stepsArgument+1>=goalChainArguments.Length ||
                        !int.TryParse(goalChainArguments[stepsArgument+1],NumberStyles.None,CultureInfo.InvariantCulture,out stepCount) ||
                        (stepCount!=3 && stepCount!=4 && stepCount!=5))
                        throw new InvalidOperationException("Two-pass goal probe requires an explicit supported step count: 3, 4, or 5.");
                    yield return GoalSequenceProbe.Run(adapter,streets,character,stepCount);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: live two-pass goal chain length="+stepCount+".");
                    yield break;
                }
                if(Array.IndexOf(goalChainArguments,GoalChainLengthProbe.Switch)>=0)
                {
                    int stepsArgument = Array.IndexOf(goalChainArguments,"-peopleoftheiliacbay-goal-chain-steps");
                    int stepCount;
                    if(stepsArgument<0 || stepsArgument+1>=goalChainArguments.Length ||
                        !int.TryParse(goalChainArguments[stepsArgument+1],NumberStyles.None,CultureInfo.InvariantCulture,out stepCount) ||
                        (stepCount!=3 && stepCount!=4 && stepCount!=5))
                        throw new InvalidOperationException("Goal chain length probe requires an explicit supported step count: 3, 4, or 5.");
                    yield return GoalChainLengthProbe.Run(adapter,streets,character,stepCount);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: controlled goal chain length="+stepCount+".");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),GoalQwenWillingnessProbe.RunFlag)>=0)
                {
                    if(dialogueSaveArgument<0 || dialogueSaveArgument+1>=dialogueArguments.Length ||
                        dialogueArguments[dialogueSaveArgument+1]!="People - Gothway Garden" ||
                        character!="PeopleOfTheIliacBayTest-9a5f197b")
                        throw new InvalidOperationException("Qwen willingness probe requires the exact owned character and Gothway Garden save.");
                    yield return GoalQwenWillingnessProbe.Run(adapter,streets,character,dialogueArguments[dialogueSaveArgument+1]);
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-goal-binding-probe")>=0)
                {
                    yield return GoalBindingProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: typed goal extraction and native binding.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-context-speaker-probe")>=0)
                {
                    yield return ConversationContextSpeakerProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: live conversation speaker source bindings.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),KeeperWorkDutyProbe.Flag)>=0)
                {
                    yield return KeeperWorkDutyProbe.RunKeeperWorkDuty(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native Tavern keeper work duty context.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),NativeNpcPoisonExperienceProbe.Flag)>=0)
                {
                    if(dialogueSaveArgument<0 || dialogueSaveArgument+1>=dialogueArguments.Length)
                        throw new InvalidOperationException("Native poison experience probe requires an explicit fixture save.");
                    yield return NativeNpcPoisonExperienceProbe.Run(adapter,streets,character,
                        dialogueArguments[dialogueSaveArgument+1]);
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),NativeNpcPoisonAppraisalProbe.Flag)>=0)
                {
                    if(dialogueSaveArgument<0 || dialogueSaveArgument+1>=dialogueArguments.Length)
                        throw new InvalidOperationException("Native poison appraisal probe requires an explicit fixture save.");
                    yield return NativeNpcPoisonAppraisalProbe.Run(adapter,streets,character,
                        dialogueArguments[dialogueSaveArgument+1]);
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-context-snapshot-probe")>=0)
                {
                    if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-context-lifecycle-probe")>=0)
                        yield return ConversationContextLifecycleProbe.Run(adapter,streets,character);
                    yield return ConversationContextSnapshotProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: context snapshot binding and source separation.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-move-to-wait-probe")>=0)
                {
                    yield return NativeMoveToWaitProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: named-building MoveTo then physically gated Wait.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-enter-wait-probe")>=0)
                {
                    yield return NativeEnterWaitProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: physical public-building Enter then offscreen Wait.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),NativeNpcRepairDropoffProbe.RunFlag)>=0)
                {
                    yield return NativeNpcRepairDropoffProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native repair drop-off, elapsed completion, and same-keeper collection.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-appointment-point-probe")>=0)
                {
                    yield return AppointmentPointProbe.Run(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: exact outdoor appointment point, physical arrival, native opener and disk restore.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-npc-interaction-probe")>=0)
                {
                    yield return NativeNpcInteractionProbe.Run(streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: reciprocal factual NPC interaction memory.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-npc-vitals-probe")>=0)
                {
                    yield return NativeNpcVitalsProbe.RunFixture(streets,character);
                    Debug.Log(Environment.GetEnvironmentVariable("POTIB_PLAYER_HEALING_PROBE")=="1"?
                        "PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native player healing attribution.":
                        "PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native NPC vitals and death lifecycle.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-npc-healthcare-probe")>=0)
                {
                    yield return NpcHomeSleepNativeProbe.Run(adapter,streets,character);
                    yield return NpcConditionsNativeProbe.Run(adapter,streets,character);
                    yield return NpcMedicalVisitNativeProbe.Run(adapter,streets,character);
                    yield return NpcHealthCareNativeProbe.RunFixture(adapter,streets,character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native NPC healing potion effect, consumption and rollback.");
                    yield break;
                }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-dialogue-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-dialogue-refusal-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-semantic-conversation-probe") >= 0)
                {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-semantic-conversation-probe") >= 0)
                {
                    yield return SemanticConversationProbe.Run(adapter, streets, character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: semantic proactive-contact pause and native conversation persistence.");
                    yield break;
                }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-dialogue-refusal-probe") >= 0)
                {
                    yield return DialogueRefusalProbe.Run(adapter, streets, character);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: durable family-threat dialogue refusal.");
                    yield break;
                }
                    if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-knowledge-browse-only")>=0)
                        yield return KnowledgeBrowsingProbe.ApproachSpeaker(adapter,streets);
                    ResidentActor person = null;
                    float actorDeadline = Time.realtimeSinceStartup + 15;
                    while (!person && Time.realtimeSinceStartup < actorDeadline)
                    {
                        foreach (var candidate in FindObjectsOfType<ResidentActor>())
                            if (candidate.Person && candidate.Resident.IsAlive &&
                                candidate.Resident.AgeGroup == ResidentAgeGroup.Adult)
                            { person = candidate; break; }
                        if (!person) yield return new WaitForSecondsRealtime(0.5f);
                    }
                    if (!person) throw new InvalidOperationException("Dialogue probe has no talkable resident.");
                    var dialogueGuards = adapter.GetComponent<GuardPopulation>();
                    float garrisonDeadline = Time.realtimeSinceStartup + 60;
                    while(dialogueGuards && !dialogueGuards.OwnsSettlement && Time.realtimeSinceStartup < garrisonDeadline)
                        yield return null;
                    if(!dialogueGuards || !dialogueGuards.OwnsSettlement)
                        throw new InvalidOperationException("Dialogue crime probe requires completed garrison setup before pausing in Talk.");
                    double clockStart=DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    float realStart=Time.realtimeSinceStartup;
                    yield return new WaitForSecondsRealtime(4f);
                    double elapsedGame=DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()-clockStart;
                    float elapsedReal=Time.realtimeSinceStartup-realStart;
                    if(DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.TimeScale!=2f ||
                        Math.Abs(elapsedGame-elapsedReal*2)>1.5)
                        throw new InvalidOperationException("World clock did not run at 2x realtime after loaded save: game="+elapsedGame+"; real="+elapsedReal);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CLOCK_2X_PASS: game="+elapsedGame+"; realtime="+elapsedReal);
                    game.TalkManager.TalkToMobileNPC(person.Person);
                    yield return new WaitForSecondsRealtime(0.3f);
                    var talk = DaggerfallUI.UIManager.TopWindow as DaggerfallWorkshop.Game.UserInterface.DaggerfallTalkWindow;
                    if (talk == null) throw new InvalidOperationException("Dialogue probe did not open native Talk window.");
                    var dialogueFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var transcriptField = talk.GetType().GetField("listboxConversation", dialogueFlags);
                    var transcript = transcriptField == null ? null : transcriptField.GetValue(talk) as DaggerfallWorkshop.Game.UserInterface.ListBox;
                    if (transcript == null) throw new InvalidOperationException("Native Talk transcript is unavailable.");
                    var submit = typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText", dialogueFlags);
                    if (submit == null) throw new InvalidOperationException("Dialogue submission adapter is unavailable.");
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-dialogue-clock-only") >= 0)
                    {
                        yield return DialogueClockProbe.Run(adapter, talk, transcript);
                        DaggerfallUI.Instance.PopToHUD();
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: one AI-on routed native clock question.");
                        yield break;
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-knowledge-browse-only") >= 0)
                    {
                        yield return KnowledgeBrowsingProbe.Run(adapter, talk, submit, transcript);
                        DaggerfallUI.Instance.PopToHUD();
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: knowledge browsing.");
                        yield break;
                    }
                    DaggerfallWorkshop.Game.UserInterface.TextBox input = null;
                    foreach (var component in talk.NativePanel.Components)
                    {
                        var field = component as DaggerfallWorkshop.Game.UserInterface.TextBox;
                        if (field != null && field.Name == "PeopleOfTheIliacBaySayInput") { input = field; break; }
                    }
                    if (input == null || !input.Enabled)
                        throw new InvalidOperationException("Free-text input missing from native Talk window.");
                    if (!talk.IsSetup || talk.NativePanel.Components[talk.NativePanel.Components.Count - 1] != input)
                        throw new InvalidOperationException("Free-text input was not added after Talk window setup.");
                    if (input.Position != new Vector2(123, 8) || input.Size != new Vector2(124, 38))
                        throw new InvalidOperationException("Editor is not in the classic upper question panel.");
                    input.SetFocus();
                    if (!input.HasFocus() || DaggerfallUI.Instance.ProcessHotKeySequences() !=
                        HotkeySequence.HotkeySequenceProcessStatus.Disabled)
                        throw new InvalidOperationException("Typing in free-text input still dispatches native Talk shortcuts.");
                    talk.SetFocus(null);
                    if (DaggerfallUI.Instance.ProcessHotKeySequences() ==
                        HotkeySequence.HotkeySequenceProcessStatus.Disabled)
                        throw new InvalidOperationException("Native Talk shortcuts stayed disabled after input lost focus.");
                    input.SetFocus();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FOCUS_PASS: shortcuts suppressed only while typing.");
                    input.Text = "Hello";
                    yield return new WaitForSecondsRealtime(2f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay-dialogue-input.png"));
                    yield return new WaitForSecondsRealtime(1f);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_PROBE_PASS: post-setup input present; position=" + input.Rectangle);
                    var scroller = input as WrappedDialogueInput;
                    if (scroller == null) throw new InvalidOperationException("Dialogue input has no clipping viewport.");
                    string shortDraft = input.Text;
                    input.Text = "A long question that must wrap inside the upper panel without covering the portrait or neighbouring panels. This suffix must remain visible while typing more text and using several rows in the original question area.";
                    yield return new WaitForSecondsRealtime(0.3f);
                    if (scroller.LineCount < 4 || scroller.VisibleFirstLine == 0 || scroller.VisibleCaretX > input.Size.x - 4)
                        throw new InvalidOperationException("Long dialogue input failed to wrap and scroll around caret.");
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "People-dialogue-scroll.png"));
                    yield return new WaitForSecondsRealtime(0.5f);
                    var setCaret = typeof(DaggerfallWorkshop.Game.UserInterface.TextBox).GetMethod("SetCursorPosition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    setCaret.Invoke(input, new object[] { 0 });
                    yield return new WaitForSecondsRealtime(0.2f);
                    if (scroller.VisibleFirstLine != 0 || scroller.VisibleCaretX != 0)
                        throw new InvalidOperationException("Dialogue input Home did not return viewport to beginning.");
                    input.Text = shortDraft;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_SCROLL_PASS: wrapped upper panel; vertical overflow bounded; end/home caret visible.");
                    var nativeQuestion = (DaggerfallWorkshop.Game.UserInterface.TextLabel)typeof(DaggerfallWorkshop.Game.UserInterface.DaggerfallTalkWindow)
                        .GetField("textlabelPlayerSays", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(talk);
                    nativeQuestion.Text = "Where is the nearest temple?";
                    yield return null; yield return null;
                    if (input.Text != nativeQuestion.Text || nativeQuestion.Enabled)
                        throw new InvalidOperationException("Native question preview did not become editable text.");
                    input.Text = "My edited question";
                    yield return null; yield return null;
                    if (input.Text != "My edited question") throw new InvalidOperationException("Native preview overwrote edited draft.");
                    input.Text = shortDraft;
                    string question = input.Text;
                    input.Text = string.Empty;
                    float started = Time.realtimeSinceStartup;
                    string answer = null;
                    yield return KnowledgeBrowsingProbe.SubmitAndWait(adapter, talk, submit, transcript, question,
                        value => answer = value, 120f);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_DIALOGUE_MODEL_MEANING_CHECK: finalized reply for submitted turn.");
                    if (string.IsNullOrWhiteSpace(answer) || answer == FreeTextDialogue.FallbackAnswer ||
                        answer.Contains("considering another question"))
                        throw new InvalidOperationException("Town dialogue model meaning check failed: live turn returned a known fallback instead of a model reply: " + answer);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_REPLY_PASS: seconds=" + (Time.realtimeSinceStartup - started).ToString("F2") +
                        "; answer=" + answer);
                    yield return new WaitForSecondsRealtime(0.5f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay-dialogue-reply.png"));
                    yield return new WaitForSecondsRealtime(1f);
                    yield return DialogueHistoryProbe.Run(adapter, talk);
                    yield return DialogueMemoryProbe.Run(adapter,talk,person.Person);
                    yield return DialogueClockProbe.Run(adapter, talk, transcript);
                    yield return DialogueHomeProbe.Run(adapter,talk,submit,transcript,person.Resident.Id);
                    yield return GroundedConversationProbe.Run(adapter, talk, submit, transcript, person.Resident.Id);
                    HouseholdGreetingProbe.Run(streets);
                    yield return HouseholdConsentProbe.Run(adapter, streets);
                    yield return DispositionPricingProbe.Run();
                    yield return DialogueHostilityProbe.Run();
                    yield return RosyInteriorCompatibility.VerifyTransitionLifetime();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native free-text field and live model reply.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-regional-probe") >= 0)
                {
                    yield return RegionalIdentityProbe.Run();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: live regional identities.");
                }
                else yield return DaggerfallRefugeProbe.Run();
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-keeper-response-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-inn-alarm-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-shop-visit-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-crowd-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-citizen-threat-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-welfare-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-defense-ledger-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-indoor-noise-probe") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-indoor-stairs-probe") >= 0)
            {
                game.SaveLoadManager.EnumerateSaves();
                int fixture = game.SaveLoadManager.FindSaveFolderByNames(character, "People of the Iliac Bay - shopkeeper identity");
                if (fixture < 0) throw new InvalidOperationException("Keeper identity fixture missing.");
                loaded = false; game.SaveLoadManager.Load(fixture); DaggerfallUI.Instance.PopToHUD();
                float limit = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < limit) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Keeper fixture load timed out.");
                yield return new WaitForSecondsRealtime(1);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-inn-alarm-probe") >= 0)
                {
                    yield return InnAlarmProbe.Run();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: inn-wide civilian alarm.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-defense-ledger-probe") >= 0)
                {
                    yield return DefenseLedgerProbe.Run();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native defense ledger.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-citizen-threat-probe") >= 0)
                {
                    yield return CitizenThreatProbe.Run();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: citizen threat flight.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-welfare-probe") >= 0)
                {
                    yield return CitizenThreatProbe.RunWelfare();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: welfare encounter chain.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-crowd-probe") >= 0)
                {
                    yield return CrowdProbe.Run();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: crowd movement.");
                    yield break;
                }
                if(Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-shop-visit-probe") >= 0)
                {
                    yield return ShopVisitProbe.Run();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: shop visit activities.");
                    yield break;
                }
                var keeper = ((WorldState)adapter.GetSaveData()).NativePeople.Find(p => p.WorkKey == 131343);
                if (keeper == null) throw new InvalidOperationException("Saved keeper missing.");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-indoor-stairs-probe") >= 0)
                    yield return InteriorStairsProbe.Run(keeper.Id);
                else if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-indoor-noise-probe") >= 0)
                    yield return IndoorNoiseProbe.Run(keeper.Id);
                else yield return NativeKeeperResponseProbe.Run(character, keeper.Id);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: native keeper response; character=" + character);
                yield break;
            }
            string expectedPopulation = null;
            // Inside, outside, then town return exercise real save slots and streamed
            // geometry. Fixture-character mode also tests saves from an earlier process.
            foreach (bool inside in new[] { true, false, true })
            {
                game.SaveLoadManager.EnumerateSaves();
                string name = "People of the Iliac Bay - " + (inside ? "inside" : "outside") + " Gothway Garden";
                int key = game.SaveLoadManager.FindSaveFolderByNames(character, name);
                if (key < 0) { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: missing fixture " + name); yield break; }
                loaded = false;
                game.SaveLoadManager.Load(key);
                // Match the game's LoadGame button: loading alone leaves its menu open.
                DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 90;
                while ((!loaded || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer) &&
                    Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer)
                { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: fixture load timed out."); yield break; }
                yield return new WaitForSecondsRealtime(2);
                bool correctPosition = game.PlayerGPS.CurrentMapPixel.X == pixel.X + (inside ? 0 : 1) &&
                    game.PlayerGPS.CurrentMapPixel.Y == pixel.Y;
                if (inside) correctPosition &= game.PlayerGPS.HasCurrentLocation &&
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId == mapId &&
                    game.StreamingWorld.GetCurrentBuildingDirectory() != null;
                else correctPosition &= !game.PlayerGPS.HasCurrentLocation ||
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId != mapId;
                if (!correctPosition || game.PlayerEntity.Name != character)
                { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: wrong character or location after load."); yield break; }
                if (inside) VerifyGeometry(game.StreamingWorld.GetCurrentBuildingDirectory(), game.StreamingWorld.GetCurrentCityNavigation());
                WorldState state = (WorldState)adapter.GetSaveData();
                SettlementPopulation town = state.Settlements.Find(value => value.SettlementId == mapId);
                if (town == null || town.Households.Count == 0)
                { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: town population missing after load."); yield break; }
                // Newly generated route caches may finish between loads; compare all
                // persistent population fields independently of that derived cache.
                state.Routines.Clear();
                string actual = JsonUtility.ToJson(state);
                if (expectedPopulation != null && expectedPopulation != actual)
                { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAIL: population changed across fixture loads."); yield break; }
                expectedPopulation = actual;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_LOAD_PASS: " + (inside ? "inside" : "outside") + "; households=" + town.Households.Count);
            }
            string[] probeArguments = Environment.GetCommandLineArgs();
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-running-pilot-probe") >= 0)
            {
                if (game.PlayerEnterExit.IsPlayerInside)
                    throw new InvalidOperationException("Running pilot probe requires an exterior fixture.");
                yield return RunningPilotProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: Archive 385 running pilot preview.");
                yield break;
            }
            if(Array.IndexOf(probeArguments,"-peopleoftheiliacbay-stationary-probe")>=0)
            {
                yield return StationaryNpcProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: ordinary stationary NPCs.");
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-city-traveler-probe") >= 0)
            {
                yield return DaggerfallTravelerProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: Daggerfall traveler gates; character="+character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-wall-quarter-probe") >= 0)
            {
                yield return DaggerfallTravelerProbe.Run(false,true);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: wall quarter beds; character="+character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-gate-guard-probe") >= 0)
            {
                yield return DaggerfallTravelerProbe.Run(true);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: Daggerfall gate guards; character="+character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-tavern-survey-probe") >= 0)
            {
                yield return TavernSurveyProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: tavern survey; character="+character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-housing-probe") >= 0)
            {
                int key = game.SaveLoadManager.FindSaveFolderByNames(character, "People of the Iliac Bay - family home");
                if (key < 0) throw new InvalidOperationException("Missing family fixture for housing audit.");
                loaded = false; game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Housing fixture load timed out.");
                var actualDirectory = game.StreamingWorld.GetCurrentBuildingDirectory();
                var room = game.PlayerEnterExit.Interior;
                BuildingSummary summary;
                if (!room || !actualDirectory.GetBuildingSummary(room.EntryDoor.buildingKey, out summary))
                    throw new InvalidOperationException("Loaded housing fixture has no building metadata.");
                var profiles = HousingSurvey.Read(actualDirectory);
                var savedTown = ((WorldState)adapter.GetSaveData()).Settlements.Find(t => t.SettlementId == mapId);
                foreach (var tavern in actualDirectory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern))
                    if (profiles.Exists(p => p.BuildingKey == tavern.buildingKey) || savedTown.Households.Exists(h => h.BuildingKey == tavern.buildingKey))
                        throw new InvalidOperationException("Tavern guest beds entered the household survey.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSING_LIVE_PASS: familySave=" + key + "; building=" + summary.buildingKey +
                    "; type=" + summary.BuildingType + "; model=" + summary.ModelID + "; tavernsExcluded=" +
                    actualDirectory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern).Count);
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                var clock = DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((86400 + 7*3600 - (int)(clock.ToSeconds()%86400))%86400);
                const int residentialKey = 196864;
                BuildingSummary residential;
                if (!actualDirectory.GetBuildingSummary(residentialKey, out residential) ||
                    residential.BuildingType != DFLocation.BuildingTypes.House2 || residential.ModelID != 320)
                    throw new InvalidOperationException("Residential fixture metadata changed.");
                var activation = game.PlayerObject.GetComponent<PlayerActivate>();
                if (streets.HouseholdRoutineLocked(residentialKey) != false || !activation.BuildingIsUnlocked(residential))
                    throw new InvalidOperationException("Awake residential household did not unlock native door.");
                bool absentChecked = false;
                for (int minute = 9*60; minute < 18*60 && !absentChecked; minute += 10)
                {
                    double at = Math.Floor((double)clock.ToSeconds()/86400)*86400 + minute*60;
                    foreach (var home in savedTown.Households)
                    {
                        if (home.Residents.Count == 0 || home.Residents.Exists(r => !r.IsAlive || r.IsQuestProtected || streets.IsAlarmed(r.Id))) continue;
                        BuildingSummary absentHouse;
                        if (!actualDirectory.GetBuildingSummary(home.BuildingKey, out absentHouse) || absentHouse.FactionId != 0) continue;
                        bool away = true;
                        foreach (var resident in home.Residents)
                        {
                            var location = streets.GetState(resident.Id, at);
                            if (location.BuildingKey == home.BuildingKey) { away = false; break; }
                        }
                        if (!away) continue;
                        clock.RaiseTime((float)(at-clock.ToSeconds()));
                        if (streets.HouseholdRoutineLocked(home.BuildingKey) != true || activation.BuildingIsUnlocked(absentHouse))
                            throw new InvalidOperationException("Empty daytime household did not lock native door.");
                        bool locksEnabled = streets.HouseholdLocksEnabled;
                        try
                        {
                            streets.HouseholdLocksEnabled = false;
                            if (!activation.BuildingIsUnlocked(absentHouse))
                                throw new InvalidOperationException("Disabling household locks did not restore daytime native access.");
                        }
                        finally { streets.HouseholdLocksEnabled = locksEnabled; }
                        yield return EntryProbe.Run(adapter, streets, home.BuildingKey);
                        absentChecked = true; break;
                    }
                }
                if (!absentChecked) throw new InvalidOperationException("No fully absent daytime household found for lock test.");
                clock.RaiseTime((86400 + 2*3600 - (int)(clock.ToSeconds()%86400))%86400);
                if (streets.HouseholdRoutineLocked(residentialKey) != true || activation.BuildingIsUnlocked(residential))
                    throw new InvalidOperationException("Sleeping residential household did not lock native door.");
                clock.RaiseTime(5*3600);
                if (!activation.BuildingIsUnlocked(residential))
                    throw new InvalidOperationException("Waking household did not release routine lock.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_LOCK_PASS: actual native door follows awake, sleeping and waking owners.");
                DaggerfallStaticDoors owner = null; StaticDoor entry = default(StaticDoor);
                foreach (var candidate in actualDirectory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach (var door in candidate.Doors)
                        if (door.doorType == DoorTypes.Building && door.buildingKey == residentialKey) { owner = candidate; entry = door; }
                if (!owner) throw new InvalidOperationException("Residential fixture door missing.");
                EnterInterior(owner.transform, entry);
                yield return new WaitForSecondsRealtime(1.2f); DaggerfallUI.Instance.PopToHUD();
                var family = savedTown.Households.Find(h => h.BuildingKey == residentialKey);
                if (family == null || family.Residents.Count != 3 || family.BedCapacity != 3 ||
                    family.Residents.FindAll(r => r.AgeGroup == ResidentAgeGroup.Child).Count != 1)
                    throw new InvalidOperationException("Residential fixture is not a three-person family.");
                foreach (var resident in family.Residents)
                    if (!streets.FindActor(resident.Id)) throw new InvalidOperationException("Residential family actor missing.");
                int nativeOccupants = 0;
                foreach (var npc in game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>())
                {
                    nativeOccupants++;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSE_NATIVE_NPC: " + npc.DisplayName + "; gender=" + npc.Data.gender +
                        "; flat=" + npc.Data.billboardArchiveIndex + ":" + npc.Data.billboardRecordIndex +
                        "; position=" + npc.transform.localPosition + "; faction=" + npc.Data.factionID);
                }
                foreach (var resident in family.Residents)
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSE_RESIDENT: " + streets.FindActor(resident.Id).Person.NameNPC +
                        "; position=" + streets.FindActor(resident.Id).transform.localPosition);
                if (nativeOccupants + family.Residents.Count > family.BedCapacity)
                    throw new InvalidOperationException("Native house occupants duplicate the generated family beyond bed capacity.");
                var homeGeometry = new InteriorGeometry(game.PlayerEnterExit.Interior);
                if (!homeGeometry.EntryGround.HasValue ||
                    homeGeometry.EntryGround.Value.y - game.PlayerEnterExit.Interior.transform.position.y > 0.3f)
                    throw new InvalidOperationException("Residential entrance graph incorrectly starts on the attic floor.");
                foreach (var resident in family.Residents)
                {
                    var familyActor = streets.FindActor(resident.Id);
                    if (!homeGeometry.EntryGround.HasValue || Mathf.Abs(familyActor.transform.position.y - 1 - homeGeometry.EntryGround.Value.y) > 0.3f ||
                        homeGeometry.RouteToExit(familyActor.transform.position - Vector3.up).Count < 2)
                        throw new InvalidOperationException("Awake family spawned above the entrance floor or cannot reach the exit.");
                }
                var mother = family.Residents.Find(r => r.AgeGroup == ResidentAgeGroup.Adult && r.Sex == ResidentSex.Female);
                var motherActor = streets.FindActor(mother.Id);
                if (motherActor.Person.Gender != Genders.Female || motherActor.Person.PersonFaceRecordId < 0 || motherActor.Person.PersonFaceRecordId >= 96)
                    throw new InvalidOperationException("Female family resident received a male dialogue identity or portrait.");
                game.TalkManager.TalkToMobileNPC(motherActor.Person);
                yield return new WaitForSecondsRealtime(0.3f);
                if (game.TalkManager.MobileNPC != motherActor.Person || !(DaggerfallUI.UIManager.TopWindow is DaggerfallWorkshop.Game.UserInterface.DaggerfallTalkWindow))
                    throw new InvalidOperationException("Native family dialogue opened for a different person.");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay-female-dialogue.png"));
                yield return new WaitForSecondsRealtime(0.5f);
                adapter.ProbeBereavementGreeting(mother.Id);
                DaggerfallUI.Instance.PopToHUD();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_DIALOGUE_PASS: " + motherActor.Person.NameNPC + "; female portrait=" + motherActor.Person.PersonFaceRecordId);
                var hiddenForTheft = new HashSet<GameObject>();
                var currentRoom = game.PlayerEnterExit.Interior;
                foreach (var npc in currentRoom.GetComponentsInChildren<StaticNPC>()) hiddenForTheft.Add(npc.gameObject);
                foreach (var npc in currentRoom.GetComponentsInChildren<MobilePersonNPC>()) hiddenForTheft.Add(npc.gameObject);
                foreach (var npc in currentRoom.GetComponentsInChildren<GuardActor>()) hiddenForTheft.Add(npc.gameObject);
                var theftWitness = streets.FindActor(family.Residents[0].Id);
                var oldPosition = theftWitness.transform.position;
                bool oldAlive = theftWitness.Resident.IsAlive;
                try
                {
                    foreach (var obj in hiddenForTheft) obj.SetActive(false);
                    if (streets.HasTheftWitness() != false || DaggerfallWorkshop.Game.Formulas.FormulaHelper.CalculateShopliftingChance(game.PlayerEntity, 10, 10) != 0)
                        throw new InvalidOperationException("Empty interior theft retained a detection roll.");
                    bool awarenessEnabled = streets.TheftAwarenessEnabled;
                    try
                    {
                        streets.TheftAwarenessEnabled = false;
                        if (streets.HasTheftWitness() != null || DaggerfallWorkshop.Game.Formulas.FormulaHelper.CalculateShopliftingChance(game.PlayerEntity, 10, 10) <= 0)
                            throw new InvalidOperationException("Disabling theft awareness did not restore native probability.");
                    }
                    finally { streets.TheftAwarenessEnabled = awarenessEnabled; }
                    theftWitness.gameObject.SetActive(true);
                    theftWitness.transform.position = game.PlayerMotor.transform.position - Vector3.up;
                    Physics.SyncTransforms();
                    if (streets.HasTheftWitness() != true || DaggerfallWorkshop.Game.Formulas.FormulaHelper.CalculateShopliftingChance(game.PlayerEntity, 10, 10) <= 0)
                        throw new InvalidOperationException("Living nearby theft witness did not retain skill check.");
                    theftWitness.Resident.IsAlive = false;
                    if (streets.HasTheftWitness() != false)
                        throw new InvalidOperationException("Dead resident counted as theft witness.");
                }
                finally
                {
                    theftWitness.Resident.IsAlive = oldAlive; theftWitness.transform.position = oldPosition;
                    foreach (var obj in hiddenForTheft) obj.SetActive(true);
                    Physics.SyncTransforms();
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_THEFT_RUNTIME_PASS: empty interior zero detection, living witness skill check, dead witness excluded.");
                yield return TheftTransferProbe.Run(streets, false);
                saved = false; SaveLoadManager.OnSave += OnSaved;
                game.SaveLoadManager.Save(character, "People of the Iliac Bay - residential family home");
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                SaveLoadManager.OnSave -= OnSaved;
                if (!saved) throw new InvalidOperationException("Residential fixture save timed out.");
                int residentialSave = game.SaveLoadManager.FindSaveFolderByNames(character, "People of the Iliac Bay - residential family home");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RESIDENTIAL_FIXTURE_PASS: save=" + residentialSave + "; building=" + residentialKey + "; beds=3; adults=2; children=1");
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                DaggerfallStaticDoors shopOwner = null; StaticDoor shopEntry = default(StaticDoor);
                var keeperGuards = adapter.GetComponent<GuardPopulation>();
                deadline = Time.realtimeSinceStartup + 60;
                while (!keeperGuards.CanInvestigate(131343) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!keeperGuards.CanInvestigate(131343)) throw new InvalidOperationException("Keeper test store lacks surveyed guard approach.");
                foreach (var candidate in actualDirectory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach (var door in candidate.Doors)
                        if (door.doorType == DoorTypes.Building && door.buildingKey == 131343) { shopOwner = candidate; shopEntry = door; }
                if (!shopOwner) throw new InvalidOperationException("General store theft fixture door missing.");
                EnterInterior(shopOwner.transform, shopEntry);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                yield return TheftTransferProbe.Run(streets);
                var nativePeople = ((WorldState)adapter.GetSaveData()).NativePeople.FindAll(p => p.SettlementId == mapId && p.WorkKey == 131343);
                int physicalBeds = BedGeometry.Read(game.PlayerEnterExit.Interior).Count;
                if (nativePeople.Count != 1 || nativePeople[0].HomeKey != 131343 || nativePeople[0].BedSlot != 0 ||
                    physicalBeds < 1 || nativePeople[0].BedCapacity != physicalBeds)
                    throw new InvalidOperationException("Shop keeper reservation does not match physical beds: people=" + nativePeople.Count +
                        "; physical=" + physicalBeds + "; saved=" + (nativePeople.Count == 0 ? -1 : nativePeople[0].BedCapacity));
                var keeper = nativePeople[0];
                var shopNpcs = game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>();
                if (Array.Find(shopNpcs, n => n.Data.hash == keeper.NativeHash && n.DisplayName == keeper.Name) == null)
                    throw new InvalidOperationException("Registered keeper identity does not match native service NPC.");
                string keeperId = keeper.Id, keeperName = keeper.Name;
                var keeperFamily = ((WorldState)adapter.GetSaveData()).Settlements.Find(t => t.SettlementId == mapId).Households.Find(h => h.BuildingKey == 131343);
                if (keeperFamily == null || keeperFamily.HeadId != keeperId || keeperFamily.Residents.Count != 2 ||
                    keeperFamily.Residents.Count > physicalBeds || streets.FindActor(keeperId))
                    throw new InvalidOperationException("Keeper is not unique native head of a bed-limited family.");
                var partner = keeperFamily.Residents.Find(r => r.Id != keeperId);
                var partnerActor = streets.FindActor(partner.Id);
                if (!partnerActor || partner.PartnerId != keeperId || partnerActor.Person.NameNPC != partner.FullName ||
                    !partner.FullName.EndsWith(" " + keeperFamily.Surname, StringComparison.Ordinal) || partner.Race != keeper.Race ||
                    partnerActor.Person.Gender == (keeper.Sex == ResidentSex.Female ? Genders.Female : Genders.Male))
                    throw new InvalidOperationException("Native keeper family name, partner identity, race or gender disagrees with live actor.");
                string partnerId = partner.Id, partnerName = partner.FullName;
                saved = false; SaveLoadManager.OnSave += OnSaved;
                game.SaveLoadManager.Save(character, "People of the Iliac Bay - shopkeeper identity");
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                SaveLoadManager.OnSave -= OnSaved;
                if (!saved) throw new InvalidOperationException("Keeper identity save timed out.");
                int keeperSave = game.SaveLoadManager.FindSaveFolderByNames(character, "People of the Iliac Bay - shopkeeper identity");
                loaded = false; game.SaveLoadManager.Load(keeperSave); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Keeper identity reload timed out.");
                keeper = ((WorldState)adapter.GetSaveData()).NativePeople.Find(p => p.Id == keeperId);
                if (keeper == null || keeper.Name != keeperName || keeper.HomeKey != 131343 || keeper.BedSlot != 0 ||
                    ((WorldState)adapter.GetSaveData()).NativePeople.FindAll(p => p.Id == keeperId).Count != 1)
                    throw new InvalidOperationException("Keeper name/bed changed or duplicated on real reload.");
                keeperFamily = ((WorldState)adapter.GetSaveData()).Settlements.Find(t => t.SettlementId == mapId).Households.Find(h => h.BuildingKey == 131343);
                partner = keeperFamily == null ? null : keeperFamily.Residents.Find(r => r.Id == partnerId);
                yield return new WaitForSecondsRealtime(0.5f);
                partnerActor = streets.FindActor(partnerId);
                if (partner == null || partner.FullName != partnerName || partner.PartnerId != keeperId || !partnerActor ||
                    partnerActor.Person.NameNPC != partnerName || streets.FindActor(keeperId))
                    throw new InvalidOperationException("Real reload lost family identity or duplicated native head.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_FAMILY_PASS: " + keeperName + " and " + partnerName + "; two people/two beds; native head preserved; real reload.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_IDENTITY_PASS: " + keeperName + "; beds=" + physicalBeds + "; reserved slot0; save=" + keeperSave + "; native identity preserved on reload.");
                yield return NativeKeeperResponseProbe.Run(character, keeperId);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: live housing classification; character=" + character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-crime-scene-probe") >= 0)
            {
                yield return CrimeSceneProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: crime scene discovery; character=" + character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-outdoor-scene-probe") >= 0)
            {
                yield return OutdoorSceneProbe.Run(adapter, streets, adapter.GetComponent<GuardPopulation>());
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: outdoor crime scene; character=" + character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-interior-escape-probe") >= 0)
            {
                yield return InteriorEscapeProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: fixture loads and interior witness escape; character=" + character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-guard-witness-probe") >= 0)
            {
                yield return GuardWitnessProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: fixture loads and witness guard report; character=" + character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-guard-building-probe") >= 0)
            {
                yield return GuardBuildingProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: fixture loads and indoor guard response; character=" + character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-guard-controller-probe") >= 0)
            {
                yield return GuardControllerProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: fixture loads and finite guard controller; character=" + character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-guard-patrol-probe") >= 0)
            {
                yield return GuardPatrolProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: paired guard patrols; character=" + character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-guard-defense-probe") >= 0)
            {
                yield return GuardDefenseProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: guard enemy defense; character=" + character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-guard-actor-probe") >= 0)
            {
                yield return GuardActorProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: fixture loads and physical guard actor; character=" + character);
                yield break;
            }
            if (!requireFamily && Array.IndexOf(probeArguments, "-peopleoftheiliacbay-response-probe") >= 0 &&
                Array.IndexOf(probeArguments, "-peopleoftheiliacbay-bed-probe") < 0 && Array.IndexOf(probeArguments, "-peopleoftheiliacbay-sleep-probe") < 0)
            {
                int key = game.SaveLoadManager.FindSaveFolderByNames(character, "People of the Iliac Bay - family home");
                if (key < 0) throw new InvalidOperationException("Focused response probe requires the saved family fixture.");
                loaded = false; game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Response family load timed out.");
                yield return new WaitForSecondsRealtime(1);
                yield return CivilianResponseProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: fixture loads and focused civilian response; character=" + character);
                yield break;
            }
            if (Array.IndexOf(probeArguments, "-peopleoftheiliacbay-delivery-probe") >= 0 &&
                Array.IndexOf(probeArguments, "-peopleoftheiliacbay-quest-binding-probe") < 0 &&
                Array.IndexOf(probeArguments, "-peopleoftheiliacbay-quest-probe") < 0 &&
                Array.IndexOf(probeArguments, "-peopleoftheiliacbay-sleep-probe") < 0 &&
                Array.IndexOf(probeArguments, "-peopleoftheiliacbay-bed-probe") < 0)
            {
                // Focused quest regressions reuse inside/outside save validation without
                // repeatedly driving unrelated walking, family-camera and death fixtures.
                yield return VanillaDeliveryProbe.Run();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: fixture loads and focused vanilla delivery; character=" + character);
                yield break;
            }
            yield return VerifyStreets();
            yield return VerifyFamilies(mapId, requireFamily);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-response-probe") >= 0)
                yield return CivilianResponseProbe.Run();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-sleep-probe") >= 0)
                yield return SleepProbe.Run();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-bed-probe") >= 0)
                yield return BedGeometryProbe.Run();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-quest-probe") >= 0)
                yield return QuestIdentityProbe.Run();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-quest-binding-probe") >= 0)
                yield return QuestBindingProbeHost.Run();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-delivery-probe") >= 0)
                yield return VanillaDeliveryProbe.Run();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_TEST_PASS: real saves, repeated loads and street actors; character=" + character);
        }

        private static void DisableDialogueServicesForDecisionShadowProbe(PeopleOfTheIliacBayMod adapter)
        {
            if (!adapter) throw new InvalidOperationException("Decision shadow probe needs PeopleOfTheIliacBayMod.");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic;
            var type = typeof(PeopleOfTheIliacBayMod);
            var textEnabled = type.GetField("localDialogueEnabled", flags);
            var prewarmEnabled = type.GetField("dialoguePrewarmEnabled", flags);
            var dialogueField = type.GetField("localDialogue", flags);
            if (textEnabled == null || prewarmEnabled == null || dialogueField == null)
                throw new InvalidOperationException("Cannot disable dialogue model calls for decision shadow probe.");
            textEnabled.SetValue(adapter, false);
            prewarmEnabled.SetValue(adapter, false);
            var dialogue = dialogueField.GetValue(adapter) as LocalDialogueClient;
            if (dialogue) dialogue.Cancel();
        }
        private IEnumerator VerifyFamilies(int mapId, bool required)
        {
            var adapter = GetComponent<PeopleOfTheIliacBayMod>();
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
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_LEGACY_ADULTS_PASS: old households preserved without invented children.");
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
            if (required || town.Households.TrueForAll(h => h.BedCapacity >= 0))
            {
                foreach (var home in town.Households)
                {
                    var profile = profiles.Find(p => p.BuildingKey == home.BuildingKey);
                    if (profile == null || home.BedCapacity != Math.Max(0, profile.BedCount) || home.Residents.Count > home.BedCapacity)
                        throw new InvalidOperationException("Fresh population exceeds real bed capacity.");
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_CAPACITY_PASS: every fresh household fits actual bed count.");
            }
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
            SleepProbe.AimCamera(childCenter);
            yield return new WaitForEndOfFrame();
            Vector3 view = Camera.main.WorldToViewportPoint(childCenter);
            if (view.z <= 0 || view.x < 0.1f || view.x > 0.9f || view.y < 0.1f || view.y > 0.9f ||
                Physics.Linecast(Camera.main.transform.position, childCenter, ~0, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Child is not visible from the actual test camera.");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay-family.png"));
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
            saved = false; game.SaveLoadManager.Save(game.PlayerEntity.Name, "People of the Iliac Bay - family home");
            float deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) throw new InvalidOperationException("Family save timed out.");
            yield return null;
            int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "People of the Iliac Bay - family home");
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
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_FAMILY_PASS: children=" + children + "; selectedHousehold=" + selected.Residents.Count +
                "; child=" + child.Id + "; name=" + childName + "; schema=" + world.Version);
        }
        private IEnumerator VerifyStreets()
        {
            var streets = GetComponent<StreetPopulation>();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_ACTORS_START: " + GetComponent<PeopleOfTheIliacBayMod>().TestStatus());
            float deadline = Time.realtimeSinceStartup + 100;
            while (!streets.Ready && Time.realtimeSinceStartup < deadline) yield return null;
            if (!streets.Ready || streets.PlannedCount == 0) throw new InvalidOperationException("No scheduled street population: " + GetComponent<PeopleOfTheIliacBayMod>().TestStatus());
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
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay-streets.png"));
            yield return new WaitForSecondsRealtime(1);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_ACTORS_PASS: planned=" + streets.PlannedCount + "; active=" + streets.ActiveCount +
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
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_INTERIOR_PASS: resident=" + id + "; building=" + arrived.BuildingKey + "; name=" + expectedName);
            game.PlayerMouseLook.SetHorizontalFacing(actor.transform.position - game.PlayerMotor.transform.position);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay-interior.png"));
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
            game.SaveLoadManager.Save(game.PlayerEntity.Name, "People of the Iliac Bay - interior continuity");
            deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) throw new InvalidOperationException("Interior fixture save timed out.");
            yield return null;
            int interiorKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "People of the Iliac Bay - interior continuity");
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
            game.SaveLoadManager.Save(game.PlayerEntity.Name, "People of the Iliac Bay - resident death");
            deadline = Time.realtimeSinceStartup + 30;
            while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
            if (!saved) throw new InvalidOperationException("Death fixture save timed out.");
            yield return null;
            int deathKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "People of the Iliac Bay - resident death");
            loaded = false; game.SaveLoadManager.Load(deathKey); DaggerfallUI.Instance.PopToHUD();
            deadline = Time.realtimeSinceStartup + 90;
            while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!loaded || !streets.Ready) throw new InvalidOperationException("Interior death fixture reload timed out.");
            yield return new WaitForSecondsRealtime(2);
            bool foundDead = false;
            foreach (var settlement in ((WorldState)GetComponent<PeopleOfTheIliacBayMod>().GetSaveData()).Settlements)
                foreach (var home in settlement.Households)
                    foreach (var resident in home.Residents)
                        if (resident.Id == id) foundDead = !resident.IsAlive;
            if (!foundDead || streets.FindActor(id)) throw new InvalidOperationException("Dead resident respawned after load.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_CONTINUITY_PASS: re-entry, one actor, death and interior save/load; resident=" + id);
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
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_GEOMETRY_PASS: entrances=" + geometry.Entrances.Count +
                "; rejectedDoors=" + geometry.RejectedDoors + "; sampledRoutes=" + routes);
        }
    }
}
