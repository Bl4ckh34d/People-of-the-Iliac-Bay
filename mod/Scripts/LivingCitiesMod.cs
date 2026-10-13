using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class LivingCitiesMod : MonoBehaviour, IHasModSaveData
    {
        private WorldStateStore store = new WorldStateStore(0);
        private RuntimeOptions options = new RuntimeOptions(true, 3, 6, 22, 1, true);
        private Mod mod;
        private StreetPopulation streets;
        private bool streetPopulationEnabled;
        private SettlementPopulation current;
        private bool panel, saveFault;
        private float nextCheck;
        private string diagnostic = "Awaiting a loaded settlement.";
        public Type SaveDataType { get { return typeof(WorldState); } }

        [Invoke(StateManager.StateTypes.Start, 10)]
        public static void Init(InitParams initParams)
        {
            var host = new GameObject("Living Cities");
            DontDestroyOnLoad(host);
            var component = host.AddComponent<LivingCitiesMod>();
            component.streets = host.AddComponent<StreetPopulation>();
            component.mod = initParams.Mod;
            initParams.Mod.SaveDataInterface = component;
            initParams.Mod.LoadSettingsCallback = component.LoadSettings;
            initParams.Mod.LoadSettings();
            initParams.Mod.IsReady = true;
            Debug.Log("LIVING_CITIES_READY: scheduled population preview; save interface registered.");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-livingcities-town-test") >= 0)
                host.AddComponent<TownSmokeHarness>();
        }
        private void Awake()
        {
            ResetWorld();
            StartGameBehaviour.OnNewGame += ResetWorld;
            SaveLoadManager.OnStartLoad += BeforeLoad;
        }
        private void OnDestroy()
        {
            StartGameBehaviour.OnNewGame -= ResetWorld;
            SaveLoadManager.OnStartLoad -= BeforeLoad;
            if (mod != null) mod.LoadSettingsCallback = null;
        }
        private void LoadSettings(ModSettings settings, ModSettingsChange change)
        {
            if (streets) streets.Clear();
            streetPopulationEnabled = settings.GetBool("General", "StreetPopulation");
            options = new RuntimeOptions(settings.GetBool("General", "Enabled"),
                settings.GetInt("Population", "HouseholdLimit"), settings.GetInt("DailyRoutine", "WakeHour"),
                settings.GetInt("DailyRoutine", "SleepHour"), settings.GetInt("Diagnostics", "RefreshSeconds"),
                settings.GetBool("Diagnostics", "EnableF8Panel"));
            if (!options.DiagnosticsEnabled) panel = false;
            nextCheck = 0;
            Debug.Log("LIVING_CITIES_SETTINGS_LOADED: enabled=" + options.Enabled + "; streets=" + streetPopulationEnabled);
        }
        private void ResetWorld()
        {
            if (streets) streets.Clear();
            store = new WorldStateStore(NewSeed()); current = null; saveFault = false; nextCheck = 0;
        }
        private void BeforeLoad(SaveData_v1 ignored)
        {
            if (streets) streets.Clear();
            // DFU catches deserialization failures before calling RestoreSaveData. Stay
            // suspended until a valid restore so one character cannot inherit another's state.
            saveFault = true; current = null;
        }
        private static uint NewSeed() { return BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0); }
        public object NewSaveData() { return new WorldState { WorldSeed = NewSeed() }; }
        public object GetSaveData()
        {
            if (saveFault) throw new InvalidOperationException("Living Cities save restore failed; refusing to overwrite population data.");
            return store.Snapshot();
        }
        public void RestoreSaveData(object saveData)
        {
            if (streets) streets.Clear();
            saveFault = true; current = null;
            store.Restore(saveData as WorldState);
            saveFault = false; nextCheck = 0;
        }
        private void Update()
        {
            if (options.DiagnosticsEnabled && Input.GetKeyDown(KeyCode.F8)) panel = !panel;
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + options.RefreshSeconds;
            if (!options.Enabled) { if (streets) streets.Clear(); diagnostic = "Simulation disabled in mod settings. Saved residents are retained."; return; }
            if (saveFault) { diagnostic = "Population suspended: save restore failed or is in progress."; return; }
            GameManager game = GameManager.Instance;
            if (game == null || !game.IsReady || !game.IsPlayingGame() || !game.PlayerGPS.HasCurrentLocation) return;
            BuildingDirectory directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            if (directory == null || directory.BuildingCount == 0) { current = null; diagnostic = "Awaiting settlement layout."; return; }
            if (current == null || current.SettlementId != directory.MapID)
            {
                var homes = new List<int>();
                for (int kind = (int)DFLocation.BuildingTypes.House1; kind <= (int)DFLocation.BuildingTypes.House6; kind++)
                    foreach (BuildingSummary home in directory.GetBuildingsOfType((DFLocation.BuildingTypes)kind))
                        homes.Add(home.buildingKey);
                current = store.GetOrCreate(directory.MapID, homes, options.HouseholdLimit);
                Debug.Log("LIVING_CITIES_SETTLEMENT: " + directory.MapID + "; households=" + current.Households.Count);
            }
            if (streets && streetPopulationEnabled)
                streets.SetTown(directory, game.StreamingWorld.GetCurrentCityNavigation(), current,
                    store, options.WakeHour, options.SleepHour);
            int alive = 0, dead = 0, sleeping = 0;
            long minute = (long)(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() / 60);
            foreach (Household home in current.Households)
            {
                Activity activity = DailySchedule.HomeDay(home.BuildingKey, options.WakeHour * 60, options.SleepHour * 60).Evaluate(minute).Activity;
                foreach (Resident resident in home.Residents)
                {
                    if (!resident.IsAlive) dead++;
                    else { alive++; if (activity == Activity.Sleep) sleeping++; }
                }
            }
            diagnostic = directory.LocationData.Name + " | households " + current.Households.Count +
                "\nAlive " + alive + " | dead " + dead + " | sleeping " + sleeping +
                "\nMinute " + (minute % 1440) + " | street actors " + (streets ? streets.ActiveCount : 0) +
                " | schedules " + (streets ? streets.PlannedCount : 0);
        }
        private void OnGUI()
        {
            if (!panel) return;
            GUI.Box(new Rect(12, 12, 510, 120), "Living Cities 0.2.0 — development diagnostics (F8)");
            GUI.Label(new Rect(24, 40, 490, 90), diagnostic + "\nGothway preview: adult visits/interior presence; beds/families pending.");
        }
        public string TestStatus()
        {
            var game = GameManager.Instance;
            return "enabled=" + options.Enabled + "; streets=" + streetPopulationEnabled + "; saveFault=" + saveFault +
                "; playing=" + game.IsPlayingGame() + "; paused=" + GameManager.IsGamePaused + "; window=" +
                (DaggerfallUI.UIManager.TopWindow == null ? "null" : DaggerfallUI.UIManager.TopWindow.GetType().Name) +
                "; location=" + game.PlayerGPS.HasCurrentLocation + "; status=" + diagnostic;
        }
    }
}
