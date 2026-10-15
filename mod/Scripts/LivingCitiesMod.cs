using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using DaggerfallWorkshop.Game.UserInterface;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class LivingCitiesMod : MonoBehaviour, IHasModSaveData
    {
        private WorldStateStore store = new WorldStateStore(0);
        private readonly NativeTavernRentals nativeRentals = new NativeTavernRentals();
        private QuestOfferBindings questOffers;
        // Explicit eligible adapters use this service before their offer is displayed.
        // No general-purpose residency guessing or automatic quest rewriting is enabled.
        public QuestOfferBindings QuestOffers
        {
            get
            {
                if (saveFault) throw new InvalidOperationException("Cannot stage a quest while world restoration is incomplete.");
                if (questOffers == null) questOffers = CreateQuestOffers();
                return questOffers;
            }
        }
        private RuntimeOptions options = new RuntimeOptions(true, 6, 22, 1, true);
        private Mod mod;
        private StreetPopulation streets;
        private GuardPopulation guards;
        private bool exteriorGuardsEnabled;
        private IDisposable guardLifetimeHook;
        private bool streetPopulationEnabled;
        private bool questHouseholdsEnabled;
        private QuestOfferBindings CreateQuestOffers()
        { return new QuestOfferBindings(store, SelectKnownQuestBinding, () => !saveFault && options.Enabled && questHouseholdsEnabled); }
        private QuestResidentBinding SelectKnownQuestBinding(DaggerfallWorkshop.Game.Questing.Quest quest, string[] source)
        {
            if (saveFault || !options.Enabled || !questHouseholdsEnabled || current == null) return null;
            return KnownQuestBindings.Select(quest, source, current);
        }
        private SettlementPopulation current;
        private bool panel, saveFault;
        private bool uiRegistered, redirectingChildTalk;
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
            component.guards = host.AddComponent<GuardPopulation>();
            component.streets.QuestOwnsResident = id => component.questOffers != null && component.questOffers.OwnsResident(id);
            component.streets.SetSleepingArt(initParams.Mod);
            component.streets.InstallReactionHook();
            host.AddComponent<DefenseCombatAdapter>().Install(()=>component.store,
                ()=>!component.saveFault && component.options.Enabled && component.guards.OwnsSettlement);
            var lifetimeType = Type.GetType("LivingCities.QuestHooks.NativeGuardLifetimeHooks, LivingCities.QuestHooks", true);
            component.guardLifetimeHook = (IDisposable)Activator.CreateInstance(lifetimeType,
                new object[] { new Func<GameObject, bool>(body => body.GetComponent<GuardActor>() != null) });
            component.mod = initParams.Mod;
            initParams.Mod.SaveDataInterface = component;
            initParams.Mod.LoadSettingsCallback = component.LoadSettings;
            initParams.Mod.LoadSettings();
            DaggerfallUI.UIManager.OnWindowChange += component.OnWindowChange;
            component.uiRegistered = true;
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
            if (guardLifetimeHook != null) guardLifetimeHook.Dispose();
            ClearQuestOffers();
            StartGameBehaviour.OnNewGame -= ResetWorld;
            SaveLoadManager.OnStartLoad -= BeforeLoad;
            if (mod != null) mod.LoadSettingsCallback = null;
            if (uiRegistered) DaggerfallUI.UIManager.OnWindowChange -= OnWindowChange;
        }
        private void OnWindowChange(object sender, EventArgs args)
        {
            if (redirectingChildTalk || !(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)) return;
            if (GameManager.Instance.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile) return;
            var person = GameManager.Instance.TalkManager.MobileNPC;
            var actor = person ? person.GetComponent<ResidentActor>() : null;
            if (!actor || !actor.Resident.IsAlive) return;
            if (streets && streets.IsAlarmed(actor.Resident.Id))
            {
                redirectingChildTalk = true;
                try
                {
                    DaggerfallUI.UIManager.PopWindow();
                    var refusal = new DaggerfallWorkshop.Game.UserInterfaceWindows.DaggerfallMessageBox(DaggerfallUI.UIManager);
                    refusal.SetText(new[] { person.NameNPC, "\"Stay away from me!\"" });
                    refusal.ClickAnywhereToClose = true;
                    DaggerfallUI.UIManager.PushWindow(refusal);
                }
                finally { redirectingChildTalk = false; }
                return;
            }
            if (actor.Resident.AgeGroup != ResidentAgeGroup.Child) return;
            // Use the public window boundary only for this mod's child actors.
            // Native adult conversations and vanilla NPCs retain their own windows.
            redirectingChildTalk = true;
            try
            {
                DaggerfallUI.UIManager.PopWindow();
                DaggerfallUI.UIManager.PushWindow(new ChildGreetingWindow(DaggerfallUI.UIManager, person.NameNPC));
            }
            finally { redirectingChildTalk = false; }
        }
        private void LoadSettings(ModSettings settings, ModSettingsChange change)
        {
            if (guards) guards.Clear();
            exteriorGuardsEnabled = settings.GetBool("General", "ExperimentalExteriorGuards");
            if (guards)
            {
                guards.MaximumResponders = settings.GetInt("Guards", "MaximumResponders");
                guards.ActorRadius = settings.GetInt("Guards", "ActorRadius");
                guards.NightTorches = settings.GetBool("Guards", "NightTorches");
                guards.TorchLightLimit = settings.GetInt("Guards", "TorchLightLimit");
                guards.TorchRange = settings.GetInt("Guards", "TorchRange");
                guards.NightPatrols = settings.GetBool("Guards", "NightPatrols");
                guards.MaximumPatrolPairs = settings.GetInt("Guards", "MaximumPatrolPairs");
                guards.EnemyDefense = settings.GetBool("Guards", "EnemyDefense");
            }
            if (streets) streets.Clear();
            streetPopulationEnabled = settings.GetBool("General", "StreetPopulation");
            if (streets) streets.SleepEnabled = settings.GetBool("DailyRoutine", "SleepingPoses");
            if (streets) streets.ReactionsEnabled = settings.GetBool("General", "CivilianReactions");
            if (streets)
            {
                streets.HouseholdLocksEnabled = settings.GetBool("Awareness", "HouseholdLocks");
                streets.TheftAwarenessEnabled = settings.GetBool("Awareness", "TheftWitnesses");
                streets.TheftWitnessRange = Mathf.Clamp(settings.GetInt("Awareness", "WitnessRange"), 5, 40);
            }
            options = new RuntimeOptions(settings.GetBool("General", "Enabled"),
                settings.GetInt("DailyRoutine", "WakeHour"),
                settings.GetInt("DailyRoutine", "SleepHour"), settings.GetInt("Diagnostics", "RefreshSeconds"),
                settings.GetBool("Diagnostics", "EnableF8Panel"));
            if (!options.DiagnosticsEnabled) panel = false;
            questHouseholdsEnabled = settings.GetBool("General", "QuestHouseholds");
            if (questHouseholdsEnabled && !saveFault && questOffers == null)
                questOffers = CreateQuestOffers();
            nextCheck = 0;
            Debug.Log("LIVING_CITIES_SETTINGS_LOADED: enabled=" + options.Enabled + "; streets=" + streetPopulationEnabled);
        }
        private void ResetWorld()
        {
            if (guards) guards.Clear();
            ClearQuestOffers();
            if (streets) streets.Clear();
            store = new WorldStateStore(NewSeed()); current = null; saveFault = false; nextCheck = 0;
            if (questHouseholdsEnabled && mod != null) questOffers = CreateQuestOffers();
        }
        private void BeforeLoad(SaveData_v1 ignored)
        {
            if (guards) guards.Clear();
            ClearQuestOffers();
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
            if (guards) guards.Capture();
            return store.Snapshot();
        }
        public void RestoreSaveData(object saveData)
        {
            if (guards) guards.Clear();
            ClearQuestOffers();
            if (streets) streets.Clear();
            saveFault = true; current = null;
            store.Restore(saveData as WorldState);
            if (store.Snapshot().QuestBindings.Count > 0 || (questHouseholdsEnabled && mod != null))
                questOffers = CreateQuestOffers();
            saveFault = false; nextCheck = 0;
        }
        private void ClearQuestOffers()
        {
            if (questOffers == null) return;
            questOffers.Dispose(); questOffers = null;
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
                current = store.GetOrCreateFamilies(directory.MapID, HousingSurvey.Read(directory));
                Debug.Log("LIVING_CITIES_SETTLEMENT: " + directory.MapID + "; households=" + current.Households.Count);
            }
            RegionalIdentityAdapter.Initialize(current);
            nativeRentals.Refresh(store,directory);
            if (streets && streetPopulationEnabled)
                streets.SetTown(directory, game.StreamingWorld.GetCurrentCityNavigation(), current,
                    store, options.WakeHour, options.SleepHour);
            if (guards && exteriorGuardsEnabled && streets && streets.Ready)
                guards.SetTown(directory, game.StreamingWorld.GetCurrentCityNavigation(), current, store);
            int alive = 0, dead = 0, sleeping = 0, children = 0;
            long minute = (long)(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() / 60);
            foreach (Household home in current.Households)
            {
                Activity activity = DailySchedule.HomeDay(home.BuildingKey, options.WakeHour * 60, options.SleepHour * 60).Evaluate(minute).Activity;
                foreach (Resident resident in home.Residents)
                {
                    if (!resident.IsAlive) dead++;
                    else { alive++; if (activity == Activity.Sleep) sleeping++; if (resident.AgeGroup == ResidentAgeGroup.Child) children++; }
                }
            }
            diagnostic = directory.LocationData.Name + " | households " + current.Households.Count +
                "\nAlive " + alive + " | children " + children + " | dead " + dead + " | sleeping " + sleeping +
                "\nMinute " + (minute % 1440) + " | street actors " + (streets ? streets.ActiveCount : 0) +
                " | schedules " + (streets ? streets.PlannedCount : 0);
        }
        private void OnGUI()
        {
            if (!panel) return;
            GUI.Box(new Rect(12, 12, 550, 120), "Living Cities 0.3.9 — development diagnostics (F8)");
            GUI.Label(new Rect(24, 40, 530, 90), diagnostic + "\nFinite guards: " + (exteriorGuardsEnabled ? "experimental, actors " + guards.ActiveCount : "off") + ". Missing approaches delay physical response.");
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
