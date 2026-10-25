using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod : MonoBehaviour, IHasModSaveData
    {
        private const float GameplayTimeScale = 2f;
        private WorldStateStore store = new WorldStateStore(0);
        public readonly BasicRoadsNetwork RoadNetwork = new BasicRoadsNetwork();
        private readonly NativeTavernRentals nativeRentals = new NativeTavernRentals();
        private BankHouseListings bankListings;
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
        private RoadTravelActors roadActors;
        private LocalDialogueClient localDialogue;
        private BreezeTtsTrialBridge breezeTtsTrial;
        private bool npcVoiceEnabled;
        private bool socialSentenceStreamingEnabled;
        private bool explicitTtsTrialRequested;
        private string[] startupArguments;
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
        private ResidentActor conversationActor;
        private bool localDialogueEnabled;
        private bool decisionPlannerEnabled;
        private bool decisionPlannerSettingCaptured;
        private int localDialogueTimeoutMilliseconds = 120000;
        private float nextCheck;
        private string diagnostic = "Awaiting a loaded settlement.";
        public Type SaveDataType { get { return typeof(WorldState); } }

        [Invoke(StateManager.StateTypes.Start, 10)]
        public static void Init(InitParams initParams)
        {
            NativeNpcServiceContractFunding.BindMainThread();
            NativeNpcDiseases.Install(initParams.Mod);
            HouseholdPropertyKeyItem.Register();
            var host = new GameObject("People of the Iliac Bay");
            DontDestroyOnLoad(host);
            var component = host.AddComponent<PeopleOfTheIliacBayMod>();
            component.streets = host.AddComponent<StreetPopulation>();
            component.guards = host.AddComponent<GuardPopulation>();
            component.roadActors=host.AddComponent<RoadTravelActors>();
            component.localDialogue=host.AddComponent<LocalDialogueClient>();
            component.roadActors.Install(()=>component.store,()=>!component.saveFault && component.options.Enabled && component.streetPopulationEnabled);
            host.AddComponent<HauntingLifetime>().Install(()=>!component.saveFault && component.options.Enabled);
            component.streets.QuestOwnsResident = id => component.questOffers != null && component.questOffers.OwnsResident(id);
            component.streets.SetSleepingArt(initParams.Mod);
            SittingArt.Install(initParams.Mod);
            host.AddComponent<NativeNavigationWorld>().Install(initParams.Mod);
            StandingArt.Install(initParams.Mod);
            component.streets.InstallReactionHook();
            component.nativeRentals.Install(()=>component.store,()=>!component.saveFault && component.options.Enabled);
            component.bankListings=new BankHouseListings(()=>component.store,()=>!component.saveFault && component.options.Enabled);
            component.bankListings.Install();
            // Ordinary authored guest-bedroom flats are replaced by normal mobile
            // travelers. Do not allocate new permanent homes for the retired flats.
            host.AddComponent<DefenseCombatAdapter>().Install(()=>component.store,
                ()=>!component.saveFault && component.options.Enabled && component.guards.OwnsSettlement);
            host.AddComponent<NativeVerbalJustice>().Install(()=>component.store,
                ()=>!component.saveFault && component.options.Enabled);
            var lifetimeType = Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeGuardLifetimeHooks, PeopleOfTheIliacBay.QuestHooks", true);
            component.guardLifetimeHook = (IDisposable)Activator.CreateInstance(lifetimeType,
                new object[] { new Func<GameObject, bool>(body => body.GetComponent<GuardActor>() != null) });
            component.mod = initParams.Mod;
            initParams.Mod.SaveDataInterface = component;
            initParams.Mod.LoadSettingsCallback = component.LoadSettings;
            component.startupArguments = Environment.GetCommandLineArgs();
            component.explicitTtsTrialRequested = Array.IndexOf(component.startupArguments, BreezeTtsTrialBridge.RunFlag) >= 0;
            initParams.Mod.LoadSettings();
            component.InstallConversationHooks();
            component.InitializeDecisionShadow();
            DaggerfallUI.UIManager.OnWindowChange += component.OnWindowChange;
            component.uiRegistered = true;
            initParams.Mod.IsReady = true;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_READY: scheduled population preview; save interface registered.");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-town-test") >= 0)
                host.AddComponent<TownSmokeHarness>();
        }
        private void Awake()
        {
            ApplyGameplayTimeScale();
            RegisterConversationContextLifecycle();
            ResetWorld();
            StartGameBehaviour.OnNewGame += ResetWorld;
            SaveLoadManager.OnStartLoad += BeforeLoad;
        }
        private void OnDestroy()
        {
            CancelConversationMemoryCompaction();
            ClearConversationMemoryLastSpeaker();
            ShutdownSplitPortraitRuntime();
            if (breezeTtsTrial) breezeTtsTrial.Shutdown();
            EndDialogueMemory(false);
            AdvanceConversationSaveEpoch();
            UnregisterConversationContextLifecycle();
            DisposeDecisionShadow();
            SittingArt.Shutdown();
            StandingArt.Shutdown();
            RestoreAgenticToneChooser();
            if (agenticDialogueBackground) Destroy(agenticDialogueBackground);
            if (conversationHooks != null) conversationHooks.Dispose();
            ResetTravelPlanning();
            nativeRentals.Dispose();
            if(bankListings!=null) bankListings.Dispose();
            if (guardLifetimeHook != null) guardLifetimeHook.Dispose();
            ClearQuestOffers();
            StartGameBehaviour.OnNewGame -= ResetWorld;
            SaveLoadManager.OnStartLoad -= BeforeLoad;
            if (mod != null) mod.LoadSettingsCallback = null;
            if (uiRegistered) DaggerfallUI.UIManager.OnWindowChange -= OnWindowChange;
        }
        private void OnWindowChange(object sender, EventArgs args)
        {
            if (!DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.TalkWindow))
            {
                InvalidatePendingConversationAction();
                EndDialogueMemory(true);
                ClearPendingContact("talk-window-closed");
                ClearOpenedContact();
                if (localDialogue) localDialogue.Cancel();
                conversationSessionOpen = false;
                conversationBusy = false;
                if (conversationActor)
                {
                    conversationActor.SetConversationFacing(false);
                    conversationActor = null;
                }
            }
            if (redirectingChildTalk || !(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)) return;
            if (GameManager.Instance.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile) return;
            var person = GameManager.Instance.TalkManager.MobileNPC;
            var actor = person ? person.GetComponent<ResidentActor>() : null;
            if (!actor || !actor.Resident.IsAlive) return;
            if ((streets && streets.IsAlarmed(actor.Resident.Id)) ||
                store.HoldsPartnerGrudge(actor.Resident,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()))
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
            if (actor.Resident.AgeGroup != ResidentAgeGroup.Child)
            {
                if (conversationActor && conversationActor != actor) conversationActor.SetConversationFacing(false);
                conversationActor = actor;
                actor.SetConversationFacing(true);
                if(!IsPendingProactiveContactFor(actor)) ShowBereavementGreeting(actor.Resident);
                return;
            }
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
            ResetTravelPlanning();
            visitorOriginWeights=new VisitorOriginWeights(settings.GetInt("Travel", "NearbyOriginWeight"),
                settings.GetInt("Travel", "DistantOriginWeight"),settings.GetInt("Travel", "ForeignOriginWeight"));
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
            localDialogueEnabled = settings.GetBool("Dialogue", "EnableFreeText");
            bool requestedDecisionPlanner = settings.GetBool("Dialogue", "EnableDecisionPlanner");
            if (!decisionPlannerSettingCaptured)
            {
                decisionPlannerEnabled = requestedDecisionPlanner;
                decisionPlannerSettingCaptured = true;
            }
            else if (requestedDecisionPlanner != decisionPlannerEnabled)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SETTINGS_RESTART_REQUIRED: EnableDecisionPlanner takes effect after restart.");
            }
            localDialogueTimeoutMilliseconds = Mathf.Clamp(settings.GetInt("Dialogue", "TimeoutMilliseconds"), 100, 300000);
            dialoguePrewarmEnabled = settings.GetBool("Dialogue", "PrewarmModel");
            conversationDispositionEnabled = settings.GetBool("Dialogue", "ConversationDisposition");
            dispositionCommerceEnabled = settings.GetBool("Dialogue", "DispositionCommerce");
            conversationMapRequestsEnabled = settings.GetBool("Dialogue", "MapRequests");
            npcVoiceEnabled = settings.GetBool("Dialogue", "EnableNpcVoice");
            animatedPortraitsEnabled = settings.GetBool("Dialogue", "EnableAnimatedPortraits");
            socialSentenceStreamingEnabled = settings.GetBool("Dialogue", "EnableSocialSentenceStreaming");
            // This owned native mechanics probe requires no model or speech
            // inference. Keep the user's persisted settings untouched.
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-npc-healthcare-probe")>=0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-outdoor-scene-probe")>=0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-command-probe")>=0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-conversation-reply-outcome-probe")>=0)
            {
                localDialogueEnabled=false;dialoguePrewarmEnabled=false;
                npcVoiceEnabled=false;socialSentenceStreamingEnabled=false;
            }
            if (!localDialogueEnabled && localDialogue)
                CancelForegroundDialogueForSettings();
            RefreshTtsBridge();
            RefreshSplitPortraitRuntime();
            dialoguePrewarmQueued = false;
            if (!localDialogueEnabled && localDialogue) localDialogue.Cancel();
            if (!localDialogueEnabled || !options.Enabled) CancelDecisionShadow();
            if (questHouseholdsEnabled && !saveFault && questOffers == null)
                questOffers = CreateQuestOffers();
            nextCheck = 0;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SETTINGS_LOADED: enabled=" + options.Enabled + "; streets=" + streetPopulationEnabled);
        }

        private void RefreshTtsBridge()
        {
            if (breezeTtsTrial)
            {
                TryInstallGwenPortraitVisemes();
                if (!breezeTtsTrial.IsTrialMode)
                    breezeTtsTrial.SetFeatureOptions(npcVoiceEnabled, socialSentenceStreamingEnabled);
                return;
            }

            if (explicitTtsTrialRequested)
            {
                string trialDiagnostic;
                breezeTtsTrial = BreezeTtsTrialBridge.TryCreateFromCommandLine(gameObject,
                    startupArguments, out trialDiagnostic);
                TryInstallGwenPortraitVisemes();
                if (!string.IsNullOrEmpty(trialDiagnostic))
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TTS_TRIAL: " + trialDiagnostic);
                return;
            }
            if (!npcVoiceEnabled) return;

            string configDirectory = System.IO.Path.Combine(DaggerfallUnity.Settings.PersistentDataPath,
                "PeopleOfTheIliacBay");
            string diagnostic;
            breezeTtsTrial = BreezeTtsTrialBridge.TryCreateConfigured(gameObject, startupArguments,
                configDirectory, npcVoiceEnabled, socialSentenceStreamingEnabled, out diagnostic);
            TryInstallGwenPortraitVisemes();
            if (!string.IsNullOrEmpty(diagnostic))
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TTS_CONFIG: " + diagnostic);
        }

        // LocalDialogue cancellation invalidates its callback instead of completing it.
        // Settle the exact foreground stage first so settings changes cannot strand the
        // busy latch or leave a temporary player row behind. Native TTS remains enabled.
        private void CancelForegroundDialogueForSettings()
        {
            StreamingSocialReplySession stream = streamingSocialReply;
            bool heardPartialRetained = false;
            if (stream != null && !stream.Finished &&
                string.Equals(stream.ConversationToken, conversationContextSessionId, StringComparison.Ordinal) &&
                string.Equals(stream.PersonId, memoryPersonId, StringComparison.Ordinal))
            {
                // Keep the normal receipt handler attached. It records completed
                // audible sentences only; Closing prevents it from consuming the
                // pending social stage or committing a mood appraisal.
                stream.Closing = true;
                if (socialPcmAdapter != null && socialPcmAdapter.StreamId == stream.StreamId)
                    socialPcmAdapter.CancelFromOwner("ai-disabled");
                else if (breezeTtsTrial)
                    breezeTtsTrial.CancelStreamingReply(stream.StreamId, stream.ConversationToken,
                        stream.PersonId, "ai-disabled");
                // The receipt callback is authoritative: its Closing path checks
                // the session owner and actually appends/archives any heard pair,
                // even while a settings modal covers Talk.
                heardPartialRetained = stream.PartialTranscriptRetained;
                if (!stream.Finished)
                {
                    stream.Finished = true;
                    if (ReferenceEquals(streamingSocialReply, stream)) streamingSocialReply = null;
                }
            }

            ConversationContextSnapshot snapshot = conversationContextCurrentSnapshot;
            DaggerfallTalkWindow window = memoryWindow;
            long stage = conversationContextIssuedStageToken;
            if (!conversationBusy || snapshot == null || window == null) return;
            if (stage <= 0)
            {
                // A completed social generation already committed its transcript
                // and effects. For any other stranded busy state there is no
                // issued stage to reject, so clear only transient UI state.
                conversationBusy = false;
                CancelConversationPendingNarration();
                return;
            }

            string unansweredText = conversationContextCapturedPlayerText;
            bool unansweredTurn = !conversationPendingReturningGreeting;
            if (!HandleRejectedConversationStage(snapshot, window, stage, null, null)) return;
            CancelConversationPendingNarration();
            if (unansweredTurn && !heardPartialRetained && !string.IsNullOrWhiteSpace(unansweredText))
                AppendRejectedPlayerTextOnly(window, unansweredText);
        }

        private void TryInstallGwenPortraitVisemes()
        {
            if (!breezeTtsTrial || startupArguments == null ||
                Array.IndexOf(startupArguments, GwenPortraitVisemeOverlay.RunFlag) < 0) return;
            string result;
            GwenPortraitVisemeOverlay.TryInstall(mod, breezeTtsTrial, startupArguments, out result);
            if (!string.IsNullOrEmpty(result)) Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PORTRAIT_VISEMES: " + result);
        }
        private void ResetWorld()
        {
            CancelConversationMemoryCompaction();
            ClearConversationMemoryLastSpeaker();
            AdvanceConversationSaveEpoch();
            EndDialogueMemory(false);
            if(localDialogue) localDialogue.Cancel();
            conversationSessionOpen=false; dialogueWasOpen=false;
            bereavementGreetings.Clear();
            ResetTravelPlanning();
            if(roadActors) roadActors.Clear();
            if (guards) guards.Clear();
            ClearQuestOffers();
            if (streets) streets.Clear();
            store = new WorldStateStore(NewSeed(),ConversationArchiveRoot()); current = null; saveFault = false; nextCheck = 0;
            if (questHouseholdsEnabled && mod != null) questOffers = CreateQuestOffers();
        }
        private void BeforeLoad(SaveData_v1 ignored)
        {
            CancelConversationMemoryCompaction();
            ClearConversationMemoryLastSpeaker();
            AdvanceConversationSaveEpoch();
            EndDialogueMemory(false);
            if(localDialogue) localDialogue.Cancel();
            conversationSessionOpen=false; dialogueWasOpen=false;
            ApplyGameplayTimeScale();
            bereavementGreetings.Clear();
            ResetTravelPlanning();
            if(roadActors) roadActors.Clear();
            if (guards) guards.Clear();
            ClearQuestOffers();
            if (streets) streets.Clear();
            // DFU catches deserialization failures before calling RestoreSaveData. Stay
            // suspended until a valid restore so one character cannot inherit another's state.
            saveFault = true; current = null;
        }
        private static uint NewSeed() { return BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0); }
        internal static string ConversationArchiveRoot()
        {
            var manager=SaveLoadManager.Instance;
            string saves=manager!=null ? manager.UnitySavePath : DaggerfallUnity.Settings.MyDaggerfallUnitySavePath;
            if(string.IsNullOrEmpty(saves) || !System.IO.Directory.Exists(saves))
                saves=System.IO.Path.Combine(DaggerfallUnity.Settings.PersistentDataPath,"Saves");
            return System.IO.Path.Combine(saves,"PeopleMemory");
        }
        public object NewSaveData() { return new WorldState { WorldSeed = NewSeed() }; }
        public object GetSaveData()
        {
            CancelConversationMemoryCompaction();
            FlushPendingActionWordingBeforeMemoryCapture(true);
            CaptureNativeDialogueMemory();
            if (saveFault) throw new InvalidOperationException("People of the Iliac Bay save restore failed; refusing to overwrite population data.");
            if (guards) guards.Capture();
            if (streets) streets.CaptureObservedVisits();
            if (streets) { streets.CaptureNpcInventories(); streets.CaptureNpcActionPositions(); }
            if (roadActors) roadActors.CaptureNpcInventories();
            return store.SnapshotForSave();
        }
        public void RestoreSaveData(object saveData)
        {
            CancelConversationMemoryCompaction();
            ClearConversationMemoryLastSpeaker();
            AdvanceConversationSaveEpoch();
            EndDialogueMemory(false);
            if(localDialogue) localDialogue.Cancel();
            conversationSessionOpen=false; dialogueWasOpen=false;
            ApplyGameplayTimeScale();
            ResetTravelPlanning();
            if(roadActors) roadActors.Clear();
            if (guards) guards.Clear();
            ClearQuestOffers();
            if (streets) streets.Clear();
            saveFault = true; current = null;
            store.Restore(saveData as WorldState, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
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
            long frameStarted=RuntimeTiming.Start("mod-update");
            try { UpdateMeasured(); }
            finally { RuntimeTiming.End("mod-update",frameStarted); }
        }
        private void UpdateMeasured()
        {
            ApplyGameplayTimeScale();
            long stage=RuntimeTiming.Start("household-simulations");
            try { TickHouseholdSimulations(); }
            finally { RuntimeTiming.End("household-simulations",stage); }
            UpdateEmbeddedDialogue();
            UpdateDecisionShadow();
            AdvanceConversationRecallWarmup();
            stage=RuntimeTiming.Start("travel-planning");
            try { AdvanceTravelPlanning(); }
            finally { RuntimeTiming.End("travel-planning",stage); }
            long started=RuntimeTiming.Start("population-refresh");
            try { UpdatePopulation(); }
            finally { RuntimeTiming.End("population-refresh",started); }
            TickNpcHealthExperienceEpisodes();
            TickNativeCombatRoles();
            stage=RuntimeTiming.Start("experience-and-memory");
            try { TickNpcExperienceAppraisals();TickOperationalHistory();TickConversationMemoryCompaction(); }
            finally { RuntimeTiming.End("experience-and-memory",stage); }
        }
        private static void ApplyGameplayTimeScale()
        {
            var game = DaggerfallUnity.Instance;
            if (game != null && game.WorldTime != null && game.WorldTime.TimeScale != GameplayTimeScale)
                game.WorldTime.TimeScale = GameplayTimeScale;
        }
        private DaggerfallWorkshop.Game.Utility.CityNavigation pendingNavigation;
        private void UpdatePopulation()
        {
            nativeRentals.AdvanceSurvey();
            if (options.DiagnosticsEnabled && Input.GetKeyDown(KeyCode.F8)) panel = !panel;
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + options.RefreshSeconds;
            if (!options.Enabled) { if (streets) streets.Clear(); diagnostic = "Simulation disabled in mod settings. Saved residents are retained."; return; }
            if (saveFault) { diagnostic = "Population suspended: save restore failed or is in progress."; return; }
            GameManager game = GameManager.Instance;
            if (game == null || !game.IsReady || !game.IsPlayingGame() || !game.PlayerGPS.HasCurrentLocation) return;
            var readyNavigation=game.StreamingWorld.GetCurrentCityNavigation();
            // Only guard route preparation needs the native Start peer. Keep
            // population restoration available during the initial load callback.
            bool deferGuardRoutes=readyNavigation && readyNavigation!=pendingNavigation;
            pendingNavigation=readyNavigation;
            BuildingDirectory directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            if (directory == null || directory.BuildingCount == 0) { current = null; diagnostic = "Awaiting settlement layout."; return; }
            if (current == null || current.SettlementId != directory.MapID)
            {
                long familiesStarted=RuntimeTiming.Start();
                try
                {
                    long housingStarted=RuntimeTiming.Start("population-housing-survey");
                    try { recoveryHomes = HousingSurvey.Read(directory); }
                    finally { RuntimeTiming.End("population-housing-survey",housingStarted); }
                    recoveryScanIndex = 0;
                    long generationStarted=RuntimeTiming.Start("population-family-store");
                    try { current = store.GetOrCreateFamilies(directory.MapID, recoveryHomes); }
                    finally { RuntimeTiming.End("population-family-store",generationStarted); }
                }
                finally { RuntimeTiming.End("population-families",familiesStarted); }
                if(RuntimeTiming.Enabled)RuntimeTiming.Report("population-startup");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SETTLEMENT: " + directory.MapID + "; households=" + current.Households.Count);
            }
            if (AdvanceHouseholdRecoveryOne(directory, game))
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_RECOVERED: " + directory.MapID);
            long identitiesStarted=RuntimeTiming.Start();
            try { RegionalIdentityAdapter.Initialize(current); }
            finally { RuntimeTiming.End("population-identities",identitiesStarted); }
            long rentalStarted=RuntimeTiming.Start();
            try { nativeRentals.Refresh(store,directory); }
            finally { RuntimeTiming.End("tavern-inventory",rentalStarted); }
            if (streets && streetPopulationEnabled)
            {
                long streetsStarted=RuntimeTiming.Start();
                try {
                streets.SetTown(directory, game.StreamingWorld.GetCurrentCityNavigation(), current,
                    store, options.WakeHour, options.SleepHour);
                } finally { RuntimeTiming.End("population-street-setup",streetsStarted); }
            }
            if (guards && exteriorGuardsEnabled && streets && streets.Ready && !deferGuardRoutes)
            {
                long guardsStarted=RuntimeTiming.Start();
                try { guards.SetTown(directory, game.StreamingWorld.GetCurrentCityNavigation(), current, store); }
                finally { RuntimeTiming.End("population-guard-setup",guardsStarted); }
            }
            if(streetPopulationEnabled) BeginDailyTravel(directory);
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
            GUI.Box(new Rect(12, 12, 550, 120), "People of the Iliac Bay 0.3.9 — development diagnostics (F8)");
            GUI.Label(new Rect(24, 40, 530, 90), diagnostic + "\nFinite guards: " + (exteriorGuardsEnabled ? "experimental, actors " + guards.ActiveCount : "off") + ". Missing approaches delay physical response.");
        }
        public string TestStatus()
        {
            var game = GameManager.Instance;
            return "enabled=" + options.Enabled + "; streets=" + streetPopulationEnabled + "; saveFault=" + saveFault +
                "; ready=" + game.IsReady + "; playing=" + game.IsPlayingGame() + "; paused=" + GameManager.IsGamePaused + "; window=" +
                (DaggerfallUI.UIManager.TopWindow == null ? "null" : DaggerfallUI.UIManager.TopWindow.GetType().Name) +
                "; location=" + game.PlayerGPS.HasCurrentLocation + "; status=" + diagnostic;
        }
    }
}
