using System;
using PeopleOfTheIliacBay.Core;

#if !MEMORY_COMPACTION_RUNTIME_TESTS
using System.IO;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;
using UnityEngine.SceneManagement;
#endif

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class ConversationMemoryCompactionRuntimeState
    {
        public readonly object Owner;
        public readonly long SaveEpoch;
        public readonly string ArchiveId;
        public readonly string Head;
        public readonly double GameTime;
        public readonly double WallTime;
        public readonly bool Ready;
        public readonly bool DialogueBusy;

        public ConversationMemoryCompactionRuntimeState(object owner, long saveEpoch, string archiveId,
            string head, double gameTime, double wallTime, bool ready, bool dialogueBusy)
        {
            Owner = owner;
            SaveEpoch = saveEpoch;
            ArchiveId = archiveId;
            Head = head;
            GameTime = gameTime;
            WallTime = wallTime;
            Ready = ready;
            DialogueBusy = dialogueBusy;
        }
    }

    internal sealed class ConversationMemoryCompactionRuntimeSelection
    {
        public readonly string RequestId;
        public readonly string PersonId;
        public readonly string Prompt;
        public readonly string Grammar;
        public readonly int ResponseTokens;

        public ConversationMemoryCompactionRuntimeSelection(string requestId, string personId,
            string prompt, string grammar, int responseTokens)
        {
            RequestId = requestId;
            PersonId = personId;
            Prompt = prompt;
            Grammar = grammar;
            ResponseTokens = responseTokens;
        }
    }

    internal interface IConversationMemoryCompactionRuntimeJob
    {
        bool ScanComplete { get; }
        bool HasMorePages { get; }
        bool IsCancelled { get; }
        void AdvanceScan(int maximumRecords);
        bool TryGetSelectionRequest(out ConversationMemoryCompactionRuntimeSelection request);
        bool AcceptSelection(string requestId, string response);
        void SkipSelection(string requestId);
        void Cancel();
    }

    internal interface IConversationMemoryCompactionDialogue
    {
        bool IsBusy { get; }
        bool IsBackgroundCurrent(int ticket);
        int TryClassifyBackground(string prompt, string grammar, int responseTokens, Action<string> complete);
        bool CancelBackground(int ticket);
    }

    /// <summary>Bounded, low-priority coordinator. Core owns source validation; this owns runtime lifetime.</summary>
    internal sealed class ConversationMemoryCompactionRuntimeScheduler
    {
        internal const double CalmIdleSeconds = 10d;
        internal const double ScanSpacingSeconds = 1d;
        internal const double NearbyRetrySeconds = 5d;
        internal const double InvalidRetryFirstSeconds = 30d;
        internal const double InvalidRetrySecondSeconds = 60d;
        internal const double CompletedCheckpointRescanGameSeconds = 86400d;
        internal const int MaximumInvalidResponses = 3;
        // After attempt 12, pause this checkpoint's pending work for 86,400 native game seconds.
        internal const int MaximumModelAttemptsPerBudgetWindow = 12;
        internal const double ModelAttemptBudgetWindowGameSeconds = 86400d;

        private readonly Func<ConversationMemoryCompactionRuntimeState> captureState;
        private readonly Func<ConversationMemoryCompactionRuntimeState, IConversationMemoryCompactionRuntimeJob> createJob;
        private readonly IConversationMemoryCompactionDialogue dialogue;
        private readonly Func<string, bool> isRelevantNpcNearby;
        private readonly Action<string> diagnostic;
        private IConversationMemoryCompactionRuntimeJob job;
        private ConversationMemoryCompactionRuntimeState jobStamp;
        private ConversationMemoryCompactionRuntimeSelection activeSelection;
        private int activeTicket;
        private long generation;
        private double quietSince = double.NaN;
        private double nextScanAt;
        private double nextOperationAt;
        private double retryCheckpointAtWall;
        private int invalidResponses;
        private string invalidResponseRequestId;
        private int modelAttemptsForCheckpoint;
        private double modelBudgetUntilGameTime;
        // The budget follows these checkpoint stamps across scheduler job recreation and idle cancellation.
        private ConversationMemoryCompactionRuntimeState attemptCheckpointStamp;
        private bool nearbyDeferred;
        private ConversationMemoryCompactionRuntimeState finishedStamp;
        private double finishedUntilGame;

        public ConversationMemoryCompactionRuntimeScheduler(
            Func<ConversationMemoryCompactionRuntimeState> captureState,
            Func<ConversationMemoryCompactionRuntimeState, IConversationMemoryCompactionRuntimeJob> createJob,
            IConversationMemoryCompactionDialogue dialogue,
            Func<string, bool> isRelevantNpcNearby,
            Action<string> diagnostic = null)
        {
            if (captureState == null) throw new ArgumentNullException(nameof(captureState));
            if (createJob == null) throw new ArgumentNullException(nameof(createJob));
            if (dialogue == null) throw new ArgumentNullException(nameof(dialogue));
            this.captureState = captureState;
            this.createJob = createJob;
            this.dialogue = dialogue;
            this.isRelevantNpcNearby = isRelevantNpcNearby ?? (personId => false);
            this.diagnostic = diagnostic;
        }

        public void Tick()
        {
            ConversationMemoryCompactionRuntimeState state;
            try { state = captureState(); }
            catch (Exception error)
            {
                Report("state-capture-" + error.GetType().Name);
                Cancel();
                return;
            }
            if (!ValidState(state))
            {
                Cancel();
                return;
            }

            if (!SameCheckpoint(attemptCheckpointStamp, state))
            {
                attemptCheckpointStamp = state;
                invalidResponses = 0;
                invalidResponseRequestId = null;
                modelAttemptsForCheckpoint = 0;
                modelBudgetUntilGameTime = 0;
                retryCheckpointAtWall = 0;
                finishedStamp = null;
                finishedUntilGame = 0;
            }

            if (job != null && !SameCheckpoint(jobStamp, state))
            {
                CancelJob(true);
                quietSince = double.NaN;
            }

            if (activeTicket > 0)
            {
                if (!dialogue.IsBackgroundCurrent(activeTicket))
                {
                    activeTicket = 0;
                    activeSelection = null;
                    generation++;
                    CancelJob(false);
                    quietSince = double.NaN;
                    retryCheckpointAtWall = Math.Max(retryCheckpointAtWall, state.WallTime + InvalidRetryFirstSeconds);
                    Report("background-preempted");
                    return;
                }

                if (!state.Ready || !SameCheckpoint(jobStamp, state))
                {
                    CancelJob(true);
                    quietSince = double.NaN;
                    return;
                }

                if (activeSelection != null && IsNear(activeSelection.PersonId))
                {
                    // A nearby-speaker pause must not use Core's SkipSelection path.
                    if (dialogue.CancelBackground(activeTicket))
                    {
                        activeTicket = 0;
                        activeSelection = null;
                        generation++;
                        nearbyDeferred = true;
                        nextOperationAt = state.WallTime + NearbyRetrySeconds;
                    }
                    return;
                }
                return;
            }

            if (!state.Ready)
            {
                CancelJob(true);
                quietSince = double.NaN;
                return;
            }

            // Experience appraisal and foreground requests run earlier / at higher priority.
            if (state.DialogueBusy || dialogue.IsBusy)
            {
                CancelJob(true);
                quietSince = double.NaN;
                return;
            }

            if (IsFinished(state)) return;
            if (state.WallTime < retryCheckpointAtWall) return;

            if (job == null)
            {
                if (IsModelAttemptBudgetPaused(state)) return;
                if (double.IsNaN(quietSince)) quietSince = state.WallTime;
                if (state.WallTime - quietSince < CalmIdleSeconds) return;
                try { job = createJob(state); }
                catch (Exception error)
                {
                    retryCheckpointAtWall = state.WallTime + InvalidRetryFirstSeconds;
                    Report("job-create-" + error.GetType().Name);
                    return;
                }
                if (job == null)
                {
                    retryCheckpointAtWall = state.WallTime + InvalidRetryFirstSeconds;
                    Report("job-unavailable");
                    return;
                }
                jobStamp = state;
                generation++;
                nextScanAt = state.WallTime;
                nextOperationAt = state.WallTime;
                nearbyDeferred = false;
                return;
            }

            if (job.IsCancelled)
            {
                CancelJob(false);
                return;
            }

            if (state.WallTime < nextOperationAt) return;

            if (!job.ScanComplete)
            {
                if (IsModelAttemptBudgetPaused(state)) return;
                if (state.WallTime < nextScanAt) return;
                try
                {
                    // Core caps the call at one validated archive chunk; never loop in Update.
                    job.AdvanceScan(128);
                    nextScanAt = state.WallTime + ScanSpacingSeconds;
                }
                catch (Exception error)
                {
                    CancelJob(false);
                    retryCheckpointAtWall = state.WallTime + InvalidRetryFirstSeconds;
                    Report("scan-" + error.GetType().Name);
                }
                return;
            }

            ConversationMemoryCompactionRuntimeSelection request;
            bool hasRequest;
            try { hasRequest = job.TryGetSelectionRequest(out request); }
            catch (Exception error)
            {
                CancelJob(false);
                retryCheckpointAtWall = state.WallTime + InvalidRetryFirstSeconds;
                Report("selection-" + error.GetType().Name);
                return;
            }

            if (!hasRequest || request == null)
            {
                if (job.HasMorePages)
                {
                    if (IsModelAttemptBudgetPaused(state)) return;
                    if (state.WallTime < nextScanAt) return;
                    try
                    {
                        // Each overflow page starts with one bounded pass on a later idle tick.
                        job.AdvanceScan(128);
                        nextScanAt = state.WallTime + ScanSpacingSeconds;
                    }
                    catch (Exception error)
                    {
                        CancelJob(false);
                        retryCheckpointAtWall = state.WallTime + InvalidRetryFirstSeconds;
                        Report("scan-page-" + error.GetType().Name);
                    }
                    return;
                }
                FinishJob(state);
                return;
            }

            if (IsNear(request.PersonId))
            {
                nearbyDeferred = true;
                nextOperationAt = state.WallTime + NearbyRetrySeconds;
                return;
            }

            if (IsModelAttemptBudgetPaused(state)) return;

            if (request.ResponseTokens < 1 || request.ResponseTokens > ConversationMemoryCompactionJob.MaximumResponseTokens ||
                string.IsNullOrWhiteSpace(request.Prompt) || string.IsNullOrWhiteSpace(request.Grammar))
            {
                SkipInvalidRequest(request, state);
                return;
            }

            long requestGeneration = ++generation;
            IConversationMemoryCompactionRuntimeJob capturedJob = job;
            ConversationMemoryCompactionRuntimeSelection capturedSelection = request;
            int ticket = 0;
            try
            {
                ticket = dialogue.TryClassifyBackground(request.Prompt, request.Grammar,
                    Math.Min(ConversationMemoryCompactionJob.MaximumResponseTokens, request.ResponseTokens),
                    response => CompleteSelection(requestGeneration, ticket, capturedJob, capturedSelection, response));
            }
            catch (Exception error)
            {
                RecordModelAttempt(state);
                RecordInvalidResponse(request, state);
                Report("request-" + error.GetType().Name);
                return;
            }

            if (ticket <= 0)
            {
                nextOperationAt = state.WallTime + 1d;
                return;
            }
            activeTicket = ticket;
            activeSelection = request;
            RecordModelAttempt(state);
        }

        public void Cancel()
        {
            generation++;
            CancelJob(true);
            activeSelection = null;
            activeTicket = 0;
            quietSince = double.NaN;
            // Lifecycle cancellation stops work but does not renew a spent same-checkpoint budget.
            nearbyDeferred = false;
        }

        private void CompleteSelection(long requestGeneration, int ticket,
            IConversationMemoryCompactionRuntimeJob capturedJob,
            ConversationMemoryCompactionRuntimeSelection request, string response)
        {
            if (requestGeneration != generation || ticket <= 0 || ticket != activeTicket ||
                !ReferenceEquals(capturedJob, job)) return;

            activeTicket = 0;
            activeSelection = null;
            ConversationMemoryCompactionRuntimeState current;
            try { current = captureState(); }
            catch { CancelJob(false); return; }
            // LocalDialogueClient.TryFinish runs before this callback, so dispatch-current is false here.
            if (!ValidState(current) || !current.Ready || !SameCheckpoint(jobStamp, current) || capturedJob.IsCancelled)
            {
                CancelJob(false);
                quietSince = double.NaN;
                return;
            }
            if (IsNear(request.PersonId))
            {
                nearbyDeferred = true;
                nextOperationAt = current.WallTime + NearbyRetrySeconds;
                return;
            }

            bool accepted = false;
            try { accepted = capturedJob.AcceptSelection(request.RequestId, response); }
            catch (Exception error) { Report("accept-" + error.GetType().Name); }
            if (accepted)
            {
                invalidResponses = 0;
                invalidResponseRequestId = null;
                nextOperationAt = current.WallTime + 1d;
                Report("selection-accepted");
                return;
            }
            RecordInvalidResponse(request, current);
        }

        private void RecordInvalidResponse(ConversationMemoryCompactionRuntimeSelection request,
            ConversationMemoryCompactionRuntimeState state)
        {
            if (!string.Equals(invalidResponseRequestId, request.RequestId, StringComparison.Ordinal))
            {
                invalidResponseRequestId = request.RequestId;
                invalidResponses = 0;
            }
            invalidResponses++;
            if (invalidResponses >= MaximumInvalidResponses)
            {
                try { job.SkipSelection(request.RequestId); }
                catch (Exception error)
                {
                    PauseModelAttemptBudget(state);
                    invalidResponses = 0;
                    invalidResponseRequestId = null;
                    Report("skip-" + error.GetType().Name);
                    return;
                }
                invalidResponses = 0;
                invalidResponseRequestId = null;
                nextOperationAt = state.WallTime + 1d;
                Report("selection-skipped-invalid");
                return;
            }
            nextOperationAt = state.WallTime + (invalidResponses == 1 ? InvalidRetryFirstSeconds : InvalidRetrySecondSeconds);
            Report("selection-invalid-retry-" + invalidResponses);
        }

        private void SkipInvalidRequest(ConversationMemoryCompactionRuntimeSelection request,
            ConversationMemoryCompactionRuntimeState state)
        {
            try { job.SkipSelection(request.RequestId); }
            catch (Exception error)
            {
                PauseModelAttemptBudget(state);
                invalidResponses = 0;
                invalidResponseRequestId = null;
                Report("skip-invalid-request-" + error.GetType().Name);
                return;
            }
            nextOperationAt = state.WallTime + 1d;
            Report("selection-invalid-request-skipped");
        }

        private void FinishJob(ConversationMemoryCompactionRuntimeState state)
        {
            if (job != null) job.Cancel();
            job = null;
            jobStamp = null;
            activeSelection = null;
            finishedStamp = state;
            finishedUntilGame = state.GameTime + CompletedCheckpointRescanGameSeconds;
            quietSince = double.NaN;
            if (nearbyDeferred) retryCheckpointAtWall = state.WallTime + 60d;
            nearbyDeferred = false;
            invalidResponses = 0;
            invalidResponseRequestId = null;
            Report("scan-finished");
        }

        private bool IsFinished(ConversationMemoryCompactionRuntimeState state)
        {
            if (!SameCheckpoint(finishedStamp, state))
            {
                finishedStamp = null;
                return false;
            }
            if (state.GameTime < finishedUntilGame) return true;
            finishedStamp = null;
            return false;
        }

        private bool IsModelAttemptBudgetPaused(ConversationMemoryCompactionRuntimeState state)
        {
            if (modelAttemptsForCheckpoint < MaximumModelAttemptsPerBudgetWindow) return false;
            if (state.GameTime < modelBudgetUntilGameTime) return true;

            modelAttemptsForCheckpoint = 0;
            modelBudgetUntilGameTime = 0;
            Report("attempt-budget-renewed");
            return false;
        }

        private void RecordModelAttempt(ConversationMemoryCompactionRuntimeState state)
        {
            if (modelAttemptsForCheckpoint < MaximumModelAttemptsPerBudgetWindow)
            {
                modelAttemptsForCheckpoint++;
                if (modelAttemptsForCheckpoint == MaximumModelAttemptsPerBudgetWindow)
                    PauseModelAttemptBudget(state);
            }
        }

        private void PauseModelAttemptBudget(ConversationMemoryCompactionRuntimeState state)
        {
            modelAttemptsForCheckpoint = MaximumModelAttemptsPerBudgetWindow;
            modelBudgetUntilGameTime = state.GameTime + ModelAttemptBudgetWindowGameSeconds;
            // Keep the pending Core request/page in RAM; only advance it after the game-time cooldown.
            Report("attempt-budget-paused");
        }

        private bool IsNear(string personId)
        {
            try { return !string.IsNullOrWhiteSpace(personId) && isRelevantNpcNearby(personId); }
            catch { return false; }
        }

        private void CancelJob(bool cancelOwnedTicket)
        {
            if (cancelOwnedTicket && activeTicket > 0 && dialogue.IsBackgroundCurrent(activeTicket))
            {
                try { dialogue.CancelBackground(activeTicket); }
                catch { }
            }
            activeTicket = 0;
            activeSelection = null;
            if (job != null)
            {
                try { job.Cancel(); }
                catch { }
            }
            job = null;
            jobStamp = null;
            nearbyDeferred = false;
            generation++;
        }

        private bool SameCheckpoint(ConversationMemoryCompactionRuntimeState first,
            ConversationMemoryCompactionRuntimeState second)
        {
            return first != null && second != null && ReferenceEquals(first.Owner, second.Owner) &&
                first.SaveEpoch == second.SaveEpoch &&
                string.Equals(first.ArchiveId, second.ArchiveId, StringComparison.Ordinal) &&
                string.Equals(first.Head, second.Head, StringComparison.Ordinal);
        }

        private static bool ValidState(ConversationMemoryCompactionRuntimeState state)
        {
            return state != null && state.Owner != null && !string.IsNullOrWhiteSpace(state.ArchiveId) &&
                state.Head != null && !double.IsNaN(state.GameTime) && !double.IsInfinity(state.GameTime) &&
                state.GameTime >= 0 && !double.IsNaN(state.WallTime) && !double.IsInfinity(state.WallTime);
        }

        private void Report(string code)
        {
            if (diagnostic == null) return;
            try { diagnostic(code); }
            catch { }
        }
    }

#if !MEMORY_COMPACTION_RUNTIME_TESTS
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private ConversationMemoryCompactionRuntimeScheduler conversationMemoryCompactionScheduler;
        private ConversationMemoryCompactionCache conversationMemoryCompactionCache;
        private string conversationMemoryCompactionCacheRoot;
        private GameObject conversationMemoryLastSpeaker;
        private string conversationMemoryLastSpeakerPersonId;
        private int conversationMemoryLastSpeakerSceneHandle = int.MinValue;
        private long conversationMemoryLastSpeakerSceneRevision;

        private void TickConversationMemoryCompaction()
        {
            if (store == null || !localDialogueEnabled || !localDialogue)
            {
                CancelConversationMemoryCompaction();
                return;
            }
            try
            {
                EnsureConversationMemoryCompactionCache();
                if (conversationMemoryCompactionCache == null) return;
                if (conversationMemoryCompactionScheduler == null)
                    conversationMemoryCompactionScheduler = new ConversationMemoryCompactionRuntimeScheduler(
                        CaptureConversationMemoryCompactionRuntimeState,
                        CreateConversationMemoryCompactionJob,
                        new LocalDialogueCompactionAdapter(localDialogue, NewDialogueConnection),
                        IsConversationMemorySpeakerNearby,
                        code => { if (options.DiagnosticsEnabled) Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_COMPACTION: " + code); });
                conversationMemoryCompactionScheduler.Tick();
            }
            catch (Exception error)
            {
                CancelConversationMemoryCompaction();
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_COMPACTION_RUNTIME_FAIL: " + error.GetType().Name);
            }
        }

        private void EnsureConversationMemoryCompactionCache()
        {
            string root = Path.Combine(ConversationArchiveRoot(), "DerivedCompaction");
            if (conversationMemoryCompactionCache != null &&
                string.Equals(conversationMemoryCompactionCacheRoot, root, StringComparison.OrdinalIgnoreCase)) return;
            CancelConversationMemoryCompaction();
            conversationMemoryCompactionCache = new ConversationMemoryCompactionCache(root);
            conversationMemoryCompactionCacheRoot = root;
        }

        private ConversationMemoryCompactionRuntimeState CaptureConversationMemoryCompactionRuntimeState()
        {
            WorldStateStore owner = store;
            var game = DaggerfallUnity.Instance;
            var manager = GameManager.Instance;
            double gameTime = game != null && game.WorldTime != null ? game.WorldTime.Now.ToSeconds() : 0d;
            ConversationArchiveCheckpointView checkpoint = null;
            if (owner != null && !saveFault)
            {
                try { checkpoint = owner.CaptureConversationArchiveCheckpoint(); }
                catch { }
            }
            bool ready = owner != null && checkpoint != null && !saveFault && isActiveAndEnabled && options.Enabled &&
                localDialogueEnabled && localDialogue && !experienceAppraisalsSuspended &&
                game != null && game.IsReady && game.WorldTime != null && manager != null && manager.IsReady &&
                manager.IsPlayingGame() && manager.PlayerEntity != null && manager.PlayerEntity.CurrentHealth > 0 &&
                !GameManager.IsGamePaused && !conversationSessionOpen && !conversationBusy && !dialogueWasOpen &&
                !manager.PlayerEntity.EnemyAlertActive &&
                !(DaggerfallUI.UIManager != null && DaggerfallUI.Instance != null &&
                    DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.TalkWindow)) &&
                !(breezeTtsTrial && breezeTtsTrial.HasPendingSpeech);
            return new ConversationMemoryCompactionRuntimeState(owner, conversationSaveEpoch,
                checkpoint == null ? null : checkpoint.ArchiveId, checkpoint == null ? null : checkpoint.Head,
                gameTime, Time.unscaledTime, ready, localDialogue && localDialogue.IsBusy);
        }

        private IConversationMemoryCompactionRuntimeJob CreateConversationMemoryCompactionJob(
            ConversationMemoryCompactionRuntimeState state)
        {
            WorldStateStore owner = store;
            if (owner == null || !ReferenceEquals(owner, state.Owner) || saveFault) return null;
            ConversationArchiveCheckpointView checkpoint = owner.CaptureConversationArchiveCheckpoint();
            if (checkpoint.ArchiveId != state.ArchiveId || checkpoint.Head != state.Head) return null;
            var archive = new SharedConversationArchive(ConversationArchiveRoot());
            var job = new ConversationMemoryCompactionJob(archive, checkpoint, state.GameTime,
                conversationMemoryCompactionCache, (personId, records) =>
                {
                    NpcSocialBackground ignored;
                    RefreshSavedRandomPersonalityEligibility(owner, personId, out ignored);
                    return owner.CaptureConversationMemoryCompactionContext(personId, records);
                });
            return new CoreConversationMemoryCompactionRuntimeJob(job);
        }

        private bool IsConversationMemorySpeakerNearby(string personId)
        {
            if (string.IsNullOrEmpty(personId) || !string.Equals(personId, conversationMemoryLastSpeakerPersonId, StringComparison.Ordinal) ||
                !conversationMemoryLastSpeaker) return false;
            int sceneHandle = SceneManager.GetActiveScene().handle;
            if (sceneHandle != conversationMemoryLastSpeakerSceneHandle ||
                ConversationSceneRevision(conversationMemoryLastSpeakerSceneHandle) != conversationMemoryLastSpeakerSceneRevision)
            {
                ClearConversationMemoryLastSpeaker();
                return false;
            }
            var game = GameManager.Instance;
            if (game == null || game.PlayerMotor == null || conversationMemoryLastSpeaker.scene.handle != sceneHandle) return false;
            return Vector3.Distance(conversationMemoryLastSpeaker.transform.position, game.PlayerMotor.transform.position) <= 8f;
        }

        private void TrackConversationMemoryLastSpeaker()
        {
            var manager = GameManager.Instance == null ? null : GameManager.Instance.TalkManager;
            if (manager == null || manager.CurrentNPCType != TalkManager.NPCType.Mobile || !manager.MobileNPC ||
                string.IsNullOrEmpty(memoryPersonId))
            {
                ClearConversationMemoryLastSpeaker();
                return;
            }
            conversationMemoryLastSpeaker = manager.MobileNPC.gameObject;
            conversationMemoryLastSpeakerPersonId = memoryPersonId;
            conversationMemoryLastSpeakerSceneHandle = SceneManager.GetActiveScene().handle;
            conversationMemoryLastSpeakerSceneRevision = ConversationSceneRevision(conversationMemoryLastSpeakerSceneHandle);
        }

        private void ClearConversationMemoryLastSpeaker()
        {
            conversationMemoryLastSpeaker = null;
            conversationMemoryLastSpeakerPersonId = null;
            conversationMemoryLastSpeakerSceneHandle = int.MinValue;
            conversationMemoryLastSpeakerSceneRevision = 0;
        }

        private void CancelConversationMemoryCompaction()
        {
            if (conversationMemoryCompactionScheduler != null) conversationMemoryCompactionScheduler.Cancel();
        }

        private ConversationMemoryCompactionCache GetConversationMemoryCompactionCache()
        {
            try
            {
                EnsureConversationMemoryCompactionCache();
                return conversationMemoryCompactionCache;
            }
            catch { return null; }
        }

        private sealed class CoreConversationMemoryCompactionRuntimeJob : IConversationMemoryCompactionRuntimeJob
        {
            private readonly ConversationMemoryCompactionJob job;
            public CoreConversationMemoryCompactionRuntimeJob(ConversationMemoryCompactionJob job) { this.job = job; }
            public bool ScanComplete { get { return job.ScanComplete; } }
            public bool HasMorePages { get { return job.HasMorePages; } }
            public bool IsCancelled { get { return job.IsCancelled; } }
            public void AdvanceScan(int maximumRecords) { job.AdvanceScan(maximumRecords); }
            public bool TryGetSelectionRequest(out ConversationMemoryCompactionRuntimeSelection request)
            {
                ConversationMemoryCompactionSelectionRequest coreRequest;
                if (!job.TryGetSelectionRequest(out coreRequest)) { request = null; return false; }
                request = new ConversationMemoryCompactionRuntimeSelection(coreRequest.RequestId, coreRequest.PersonId,
                    coreRequest.Prompt, coreRequest.Grammar, coreRequest.ResponseTokens);
                return true;
            }
            public bool AcceptSelection(string requestId, string response) { return job.AcceptSelection(requestId, response); }
            public void SkipSelection(string requestId) { job.SkipSelection(requestId); }
            public void Cancel() { job.Cancel(); }
        }

        private sealed class LocalDialogueCompactionAdapter : IConversationMemoryCompactionDialogue
        {
            private readonly LocalDialogueClient client;
            private readonly Func<LocalDialogueConnection> connection;
            public LocalDialogueCompactionAdapter(LocalDialogueClient client, Func<LocalDialogueConnection> connection)
            { this.client = client; this.connection = connection; }
            public bool IsBusy { get { return !client || client.IsBusy; } }
            public bool IsBackgroundCurrent(int ticket) { return client && client.IsBackgroundCurrent(ticket); }
            public int TryClassifyBackground(string prompt, string grammar, int responseTokens, Action<string> complete)
            {
                return !client ? 0 : client.TryClassifyBackground(connection(), prompt, complete,
                    grammar, responseTokens);
            }
            public bool CancelBackground(int ticket) { return client && client.CancelBackground(ticket); }
        }
    }
#endif
}
