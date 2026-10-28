using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Native, opt-in evidence for single-model complete-goal extraction and Dialogue consent.
    // Controlled callback cases below do not substitute for the live model acceptance.
    internal static class GoalDialogueWillingnessProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-goal-qwen-willingness-probe";
        private const string Character = "Hakon Ironhand";
        private const string SaveName = "People - Gothway Garden";
        private const string Turn = "Please wait here for eighteen seconds, then wait here for nineteen seconds.";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string saveName)
        {
            if (!host || !streets || character != Character || saveName != SaveName ||
                Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) < 0 &&
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-cancellation-probe") < 0 &&
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-binding-probe") < 0)
                throw new InvalidOperationException("Dialogue willingness probe requires explicit opt-in and the exact owned Gothway fixture.");
            if (!GameManager.Instance || !GameManager.Instance.IsReady)
                throw new InvalidOperationException("Dialogue willingness probe requires the ready native game fixture.");

            bool originalPrewarm = ReadBool(host, "dialoguePrewarmEnabled");
            bool originalGreeting = ReadBool(host, "returningGreetingPending");
            float originalGreetingRetry = Convert.ToSingle(Field(host.GetType(), "returningGreetingRetryAt").GetValue(host));
            bool originalDialogueEnabled = ReadBool(host, "localDialogueEnabled");
            Vector3 baselinePlayerPosition = GameManager.Instance.PlayerMotor.transform.position;
            WorldStateStore initialStore = Field(host.GetType(), "store").GetValue(host) as WorldStateStore;
            if (initialStore == null) throw new InvalidOperationException("Dialogue willingness probe cannot fingerprint the loaded baseline store.");
            string baselineLedger = StoreFingerprint(initialStore);
            Exception probeFailure = null;
            Exception restoreFailure = null;
            IEnumerator test = RunCases(host, streets, character, saveName, baselinePlayerPosition);
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = test.MoveNext(); if (more) current = test.Current; }
                    catch (Exception error) { probeFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                try { (test as IDisposable)?.Dispose(); }
                catch (Exception error) { if (probeFailure == null) probeFailure = error; }
                try
                {
                    LocalDialogueClient client = Field(host.GetType(), "localDialogue").GetValue(host) as LocalDialogueClient;
                    if (client) client.Cancel();
                    if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
                }
                catch (Exception error) { if (probeFailure == null) probeFailure = error; }
            }

            IEnumerator restore = null;
            try
            {
                try
                {
                    restore = ConversationMoodProbe.LoadSave(host, streets, character, saveName, true);
                    while (true)
                    {
                        bool more;
                        object current = null;
                        try { more = restore.MoveNext(); if (more) current = restore.Current; }
                        catch (Exception error) { restoreFailure = error; break; }
                        if (!more) break;
                        yield return current;
                    }
                }
                finally { (restore as IDisposable)?.Dispose(); }
                if (restoreFailure == null && Vector3.Distance(GameManager.Instance.PlayerMotor.transform.position,
                    baselinePlayerPosition) > 0.05f)
                    restoreFailure = new InvalidOperationException("Owned baseline reload did not restore player position.");
                if (restoreFailure == null)
                {
                    WorldStateStore restoredStore = Field(host.GetType(), "store").GetValue(host) as WorldStateStore;
                    if (restoredStore == null || StoreFingerprint(restoredStore) != baselineLedger)
                        restoreFailure = new InvalidOperationException("Owned baseline reload did not restore the exact action/task ledger.");
                }
            }
            finally
            {
                Field(host.GetType(), "dialoguePrewarmEnabled").SetValue(host, originalPrewarm);
                Field(host.GetType(), "returningGreetingPending").SetValue(host, originalGreeting);
                Field(host.GetType(), "returningGreetingRetryAt").SetValue(host, originalGreetingRetry);
                Field(host.GetType(), "localDialogueEnabled").SetValue(host, originalDialogueEnabled);
            }
            if (restoreFailure == null)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_BASELINE_RESTORED: character=" + character +
                    "; save=" + saveName + "; exact-owned-save-reloaded=true; actionTaskLedgerExact=true; playerPositionExact=true.");
            if (restoreFailure != null)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_BASELINE_RESTORE_FAIL: " + restoreFailure);
                if (probeFailure != null) Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_FAILURE_BEFORE_RESTORE: " + probeFailure);
                throw new InvalidOperationException("Dialogue willingness probe failed to restore the exact owned baseline.", restoreFailure);
            }
            if (probeFailure != null)
                throw new InvalidOperationException("Dialogue willingness probe failed after baseline reload.", probeFailure);

            if (IsBoundGoalMode())
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_BOUND_GOAL_PASS: singleModel=true; " +
                    "controlledDialogueOutageResolverGuard=true; controlledDecline=true; controlledUnavailable=true; " +
                    "duplicateAcceptCallbackOnce=true; closedStaleAcceptDiscarded=true; controlledGoal=true; " +
                    "completeOriginalExtraction=false; productionTargetBinding=true; productionDialogueWillingness=true; " +
                    "onceOnlySavedTwoStepTask=true; baselineRestored=true.");
            else
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_WILLINGNESS_PASS: singleModel=true; controlledDialogueOutageResolverGuard=true; " +
                    "controlledDecline=true; controlledUnavailable=true; duplicateAcceptCallbackOnce=true; closedStaleAcceptDiscarded=true; " +
                    "completeOriginalExtraction=true; liveDialogueWillingness=true; onceOnlySavedTwoStepTask=true; rawTraceCaptured=true.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string saveName, Vector3 originalPlayerPosition)
        {
            GameManager game = GameManager.Instance;
            WorldStateStore store = Field(host.GetType(), "store").GetValue(host) as WorldStateStore;
            if (store == null || DaggerfallUI.UIManager == null)
                throw new InvalidOperationException("Dialogue willingness probe cannot access native world/Talk state.");
            Field(host.GetType(), "dialoguePrewarmEnabled").SetValue(host, false);
            Field(host.GetType(), "returningGreetingPending").SetValue(host, false);
            Field(host.GetType(), "localDialogueEnabled").SetValue(host, false);
            LocalDialogueClient client = Field(host.GetType(), "localDialogue").GetValue(host) as LocalDialogueClient;
            if (client) client.Cancel();
            SetIfPresent(host, "goalFirstExtractionRequestJson", null);
            SetIfPresent(host, "goalFirstExtractionResponseJson", null);
            SetIfPresent(host, "goalFirstExtractionResult", null);
            DaggerfallUI.Instance.PopToHUD();

            if (IsBoundGoalMode())
            {
                // Actors can appear before the asynchronous entrance survey.
                // Await real route candidates before opening the modal Talk window.
                float readyDeadline = Time.realtimeSinceStartup + 60f;
                TownGeometry readyGeometry = null;
                while (Time.realtimeSinceStartup < readyDeadline)
                {
                    readyGeometry = Field(typeof(StreetPopulation), "geometry").GetValue(streets) as TownGeometry;
                    if (streets.Ready && readyGeometry != null && readyGeometry.Entrances.Count > 0 &&
                        game.StreamingWorld.GetCurrentBuildingDirectory() != null) break;
                    yield return null;
                }
                if (!streets.Ready || readyGeometry == null || readyGeometry.Entrances.Count == 0)
                    throw new InvalidOperationException("Controlled bound goal requires a completed native entrance survey.");
            }

            ResidentActor actor = null;
            IEnumerator actorWait = (IEnumerator)Method(typeof(NativeDialogueProbe), "WaitForUnusedManagedAdult", StaticMembers, 4)
                .Invoke(null, new object[] { store, (Action<ResidentActor>)(value => actor = value), true, originalPlayerPosition });
            while (actorWait.MoveNext()) yield return actorWait.Current;
            if (!actor || actor.Resident == null)
                throw new InvalidOperationException("No eligible managed adult is available for single-model Dialogue acceptance.");
            if (store.ActiveNpcAction(actor.Resident.Id) != null || store.ActiveNpcTask(actor.Resident.Id) != null)
                throw new InvalidOperationException("Selected Dialogue acceptance resident already has an active action/task.");

            TownGeometry geometry = Field(typeof(StreetPopulation), "geometry").GetValue(streets) as TownGeometry;
            BuildingDirectory directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            float contactBefore = Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position);
            if (contactBefore > 8f)
            {
                object[] args = { geometry, directory, actor.transform.position - Vector3.up, null };
                if (geometry == null || directory == null ||
                    !(bool)Method(typeof(NativeMoveToWaitProbe), "TryObserverGround", StaticMembers, 4).Invoke(null, args))
                    throw new InvalidOperationException("Owned fixture has no verified outdoor contact point near the selected resident.");
                game.PlayerMotor.transform.position = ((Vector3)args[3]) + Vector3.up;
                Physics.SyncTransforms();
            }
            if (Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position) > 8f ||
                !streets.ConversationActionCandidates(actor.Resident.Id).Any(candidate => candidate.Kind == NpcActionKind.Wait))
                throw new InvalidOperationException("Selected resident has no nearby native Wait candidate for the two-step request.");

            game.TalkManager.TalkToMobileNPC(actor.Person);
            DaggerfallTalkWindow talk = null;
            IEnumerator talkWait = (IEnumerator)Method(typeof(NativeDialogueProbe), "WaitForTalk", StaticMembers, 2)
                .Invoke(null, new object[] { actor, (Action<DaggerfallTalkWindow>)(value => talk = value) });
            while (talkWait.MoveNext()) yield return talkWait.Current;
            if (talk == null) throw new InvalidOperationException("Native Talk did not open for Dialogue willingness probe.");
            if (!ReadBool(host, "conversationSessionOpen") || !ReferenceEquals(Field(host.GetType(), "conversationActor").GetValue(host), actor))
                Method(host.GetType(), "BeginGroundedConversation", Members, 0).Invoke(host, null);
            if (!ReadBool(host, "conversationSessionOpen") || !ReferenceEquals(Field(host.GetType(), "conversationActor").GetValue(host), actor))
                throw new InvalidOperationException("Native Talk did not bind the selected fixture resident.");

            Field(host.GetType(), "localDialogueEnabled").SetValue(host, true);
            Field(host.GetType(), "returningGreetingPending").SetValue(host, false);
            Field(host.GetType(), "conversationBusy").SetValue(host, false);
            if (client) client.Cancel();

            RunControlledCompletion(host, talk, "The controlled decline control.", "DECLINE", "DECLINE");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_CONTROLLED_CASE_PASS: case=DECLINE; productionCompletionBoundary=true; committed=false.");
            RunControlledCompletion(host, talk, "The controlled unavailable control.", null, null);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_CONTROLLED_CASE_PASS: case=Unavailable; productionCompletionBoundary=true; committed=false.");
            RunDuplicateCompletion(host, talk);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_CONTROLLED_CASE_PASS: case=duplicate-ACCEPT; callbackCount=1; committed=false.");
            RunClosedStaleCompletion(host, talk, actor);
            talk = null;
            game.TalkManager.TalkToMobileNPC(actor.Person);
            DaggerfallTalkWindow reopened = null;
            IEnumerator reopenedWait = (IEnumerator)Method(typeof(NativeDialogueProbe), "WaitForTalk", StaticMembers, 2)
                .Invoke(null, new object[] { actor, (Action<DaggerfallTalkWindow>)(value => reopened = value) });
            while (reopenedWait.MoveNext()) yield return reopenedWait.Current;
            if (reopened == null) throw new InvalidOperationException("Native Talk did not reopen after stale callback control.");
            talk = reopened;
            if (!ReadBool(host, "conversationSessionOpen") || !ReferenceEquals(Field(host.GetType(), "conversationActor").GetValue(host), actor))
                Method(host.GetType(), "BeginGroundedConversation", Members, 0).Invoke(host, null);
            Field(host.GetType(), "returningGreetingPending").SetValue(host, false);
            Field(host.GetType(), "localDialogueEnabled").SetValue(host, true);
            Field(host.GetType(), "conversationBusy").SetValue(host, false);

            RunDialogueUnavailableControl(host, streets, store, actor, talk);

            if (IsBoundGoalMode())
            {
                IEnumerator controlled = RunControlledBoundGoal(host, streets, store, actor, talk);
                while (controlled.MoveNext()) yield return controlled.Current;
                yield break;
            }

            int transcriptStart = TranscriptCount(talk);
            int taskCountBefore = store.Snapshot().NpcTasks.Count;
            foreach (string name in new[] { "goalLastExtractionPrompt", "goalLastExtractionOutput", "goalLastExtractionStatus", "goalLastBindingStatus" })
                Field(host.GetType(), name).SetValue(host, null);
            SetIfPresent(host, "goalFirstExtractionRequestJson", null);
            SetIfPresent(host, "goalFirstExtractionResponseJson", null);
            SetIfPresent(host, "goalFirstExtractionResult", null);
            SetIfPresent(host, "dialogueWillingnessRequestJson", null);
            SetIfPresent(host, "dialogueWillingnessResponseJson", null);
            SetIfPresent(host, "dialogueWillingnessRawChoice", null);
            long dialogueDispatchBefore = ReadLong(host, "dialogueWillingnessDispatchCount");
            long dialogueCompletionBefore = ReadLong(host, "dialogueWillingnessCompletionCount");
            long goalWillingnessBefore = ReadLong(host, "goalWillingnessDispatchCount");
            Method(host.GetType(), "SubmitFreeText", Members, 2).Invoke(host, new object[] { talk, Turn });

            float deadline = Time.realtimeSinceStartup + 150f;
            float nextDiagnostic = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline && !HasReply(host, talk, transcriptStart, Turn))
            {
                if (Time.realtimeSinceStartup >= nextDiagnostic)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_PROGRESS: extraction=" + ReadString(host, "goalLastExtractionStatus") +
                        "; binding=" + ReadString(host, "goalLastBindingStatus") + "; dialogueDispatch=" +
                        (ReadLong(host, "dialogueWillingnessDispatchCount") - dialogueDispatchBefore) + "; dialogueCompletion=" +
                        (ReadLong(host, "dialogueWillingnessCompletionCount") - dialogueCompletionBefore) + "; rows=" + LatestRows(talk, 5));
                    nextDiagnostic = Time.realtimeSinceStartup + 30f;
                }
                yield return null;
            }
            if (!HasReply(host, talk, transcriptStart, Turn))
                throw new InvalidOperationException("Live single-model SubmitFreeText timed out without a native Talk reply.");

            string prompt = ReadString(host, "goalTwoPassRepairPrompt") ?? ReadString(host, "goalTwoPassParameterPrompt") ?? ReadString(host, "goalLastExtractionPrompt");
            string output = ReadString(host, "goalTwoPassRepairOutput") ?? ReadString(host, "goalTwoPassParameterOutput") ?? ReadString(host, "goalLastExtractionOutput");
            string extractionStatus = ReadString(host, "goalLastExtractionStatus");
            string bindingStatus = ReadString(host, "goalLastBindingStatus");
            string extractionRequest = ReadString(host, "goalTwoPassRepairRequest") ?? ReadString(host, "goalTwoPassParameterRequest") ?? ReadString(host, "goalFirstExtractionRequestJson");
            string extractionResponse = ReadString(host, "goalTwoPassRepairResponse") ?? ReadString(host, "goalTwoPassParameterResponse") ?? ReadString(host, "goalFirstExtractionResponseJson");
            string dialogueRequest = ReadString(host, "dialogueWillingnessRequestJson");
            string dialogueResponse = ReadString(host, "dialogueWillingnessResponseJson");
            string dialogueChoice = ReadString(host, "dialogueWillingnessRawChoice");
            NpcTaskPlan task = store.ActiveNpcTask(actor.Resident.Id);
            bool exactTask = task != null && task.Steps != null && task.Steps.Count == 2 &&
                task.CurrentStepIndex == 0 && task.Phase != NpcTaskPhase.Completed &&
                task.Phase != NpcTaskPhase.Failed && task.Phase != NpcTaskPhase.Cancelled &&
                task.Steps[0].Kind == NpcActionKind.Wait && task.Steps[0].DurationSeconds == 18d &&
                task.Steps[1].Kind == NpcActionKind.Wait && task.Steps[1].DurationSeconds == 19d;
            bool rawTrace = !string.IsNullOrWhiteSpace(prompt) && !string.IsNullOrWhiteSpace(output) &&
                !string.IsNullOrWhiteSpace(extractionRequest) && !string.IsNullOrWhiteSpace(extractionResponse) &&
                !string.IsNullOrWhiteSpace(dialogueRequest) && !string.IsNullOrWhiteSpace(dialogueResponse) &&
                !string.IsNullOrWhiteSpace(dialogueChoice);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_LIVE_TRACE: source=" + Escape(Turn) +
                "; extractionRequest=" + Escape(extractionRequest) + "; extractionResponseJson=" + Escape(extractionResponse) +
                "; extractionPrompt=" + Escape(prompt) + "; extractionResponse=" + Escape(output) +
                "; dialogueRequest=" + Escape(dialogueRequest) + "; dialogueResponse=" + Escape(dialogueResponse) +
                "; dialogueRawChoice=" + Escape(dialogueChoice) + "; extractionPromptSha256=" + Sha256(prompt) +
                "; extractionResponseSha256=" + Sha256(output) + "; dialogueRequestSha256=" + Sha256(dialogueRequest) +
                "; dialogueResponseSha256=" + Sha256(dialogueResponse) + "; singleModel=true" + "; completeOriginalExtraction=true; separateExtractionTrace=" + rawTrace.ToString().ToLowerInvariant() +
                "; goalWillingnessDispatchDelta=" + (ReadLong(host, "goalWillingnessDispatchCount") - goalWillingnessBefore) +
                "; dialogueDispatchDelta=" + (ReadLong(host, "dialogueWillingnessDispatchCount") - dialogueDispatchBefore) +
                "; dialogueCompletionDelta=" + (ReadLong(host, "dialogueWillingnessCompletionCount") - dialogueCompletionBefore) +
                "; task=" + Escape(DescribeTask(task)) + "; taskCountBefore=" + taskCountBefore +
                "; taskCountAfter=" + store.Snapshot().NpcTasks.Count + "; transcript=" + Escape(LatestRows(talk, 6)) +
                "; actualProductionSubmitFreeText=true; audibleSpeech=false.");
            if (extractionStatus != "Valid" || bindingStatus != "bound" ||
                !prompt.Contains(Turn) || !output.Contains("eighteen") || !output.Contains("nineteen") ||
                ReadLong(host, "goalWillingnessDispatchCount") != goalWillingnessBefore + 1 ||
                ReadLong(host, "dialogueWillingnessDispatchCount") != dialogueDispatchBefore + 1 ||
                ReadLong(host, "dialogueWillingnessCompletionCount") != dialogueCompletionBefore + 1 ||
                !string.Equals(ReadString(host, "dialogueWillingnessLastChoice"), "ACCEPT", StringComparison.Ordinal) ||
                store.Snapshot().NpcTasks.Count != taskCountBefore + 1 || !exactTask || !rawTrace)
                throw new InvalidOperationException("Single-model live extraction/Dialogue acceptance failed native exact-plan checks. " +
                    "extraction=" + extractionStatus + "; binding=" + bindingStatus + "; choice=" + dialogueChoice +
                    "; task=" + DescribeTask(task) + "; rows=" + LatestRows(talk, 6));

            yield return null;
        }

        private static IEnumerator RunControlledBoundGoal(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            WorldStateStore store, ResidentActor actor, DaggerfallTalkWindow talk)
        {
            NpcActionOption moveTo = streets.ConversationActionCandidates(actor.Resident.Id)
                .Where(candidate => candidate != null && candidate.Kind == NpcActionKind.MoveTo &&
                    !string.IsNullOrWhiteSpace(candidate.Label) && NpcTaskPositionBinding.IsStaticTarget(candidate.Target))
                .GroupBy(candidate => candidate.Label, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .SelectMany(group => group)
                .OrderBy(candidate => candidate.Label, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Id)
                .FirstOrDefault();
            if (moveTo == null)
                throw new InvalidOperationException("Bound-goal arm requires one current static MoveTo candidate with a unique nonempty label.");
            string boundTurn = "Please go to " + moveTo.Label + ", then wait there for nineteen seconds.";
            string moveToSource = "go to " + moveTo.Label;
            string boundGoalJson = "{\"intent\":\"request\",\"steps\":[" +
                "{\"kind\":\"MoveTo\",\"source\":\"" + LocalDialogueCompletion.EscapeJson(moveToSource) +
                "\",\"target\":\"" + LocalDialogueCompletion.EscapeJson(moveTo.Label) +
                "\",\"duration\":null,\"quantity\":null,\"missing\":[]}," +
                "{\"kind\":\"Wait\",\"source\":\"wait there for nineteen seconds\",\"target\":\"\"," +
                "\"duration\":{\"amount\":19,\"unit\":\"game_seconds\",\"source\":\"nineteen seconds\"}," +
                "\"quantity\":null,\"missing\":[]}],\"conditions\":[],\"exclusions\":[],\"missing\":[]}";
            LocalDialogueClient client = Field(host.GetType(), "localDialogue").GetValue(host) as LocalDialogueClient;
            if (!client) throw new InvalidOperationException("Bound-goal arm has no production Dialogue client.");
            var classificationTrace = new List<ClassificationEvidence>();
            Action<string, string, string> oldObserver = client.ForegroundClassificationTrace;
            client.ForegroundClassificationTrace = (request, response, result) =>
            {
                classificationTrace.Add(new ClassificationEvidence(request, response, result));
                if (oldObserver != null) oldObserver(request, response, result);
            };

            int transcriptStart = TranscriptCount(talk);
            int taskCountBefore = store.Snapshot().NpcTasks.Count;
            long goalWillingnessBefore = ReadLong(host, "goalWillingnessDispatchCount");
            long dialogueDispatchBefore = ReadLong(host, "dialogueWillingnessDispatchCount");
            long dialogueCompletionBefore = ReadLong(host, "dialogueWillingnessCompletionCount");
            foreach (string name in new[] { "goalLastExtractionStatus", "goalLastBindingStatus" })
                Field(host.GetType(), name).SetValue(host, null);
            SetIfPresent(host, "dialogueWillingnessRequestJson", null);
            SetIfPresent(host, "dialogueWillingnessResponseJson", null);
            SetIfPresent(host, "dialogueWillingnessRawChoice", null);

            try
            {
                object snapshot = Capture(host, talk, boundTurn);
                string history = (string)Method(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", StaticMembers, 1)
                    .Invoke(null, new object[] { talk });
                Field(host.GetType(), "conversationBusy").SetValue(host, true);
                MethodInfo continueExtraction = Method(host.GetType(), "ContinueGoalActionExtraction", Members,
                    new[] { typeof(DaggerfallTalkWindow), typeof(string), typeof(string), typeof(ConversationContextSnapshot), typeof(string) });
                continueExtraction.Invoke(host, new object[] { talk, boundTurn, history, snapshot, boundGoalJson });

                float deadline = Time.realtimeSinceStartup + 120f;
                float nextDiagnostic = Time.realtimeSinceStartup + 30f;
                    while (Time.realtimeSinceStartup < deadline && !HasReply(host, talk, transcriptStart, boundTurn))
                {
                        if (Time.realtimeSinceStartup >= nextDiagnostic)
                    {
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_BOUND_PROGRESS: binding=" +
                            ReadString(host, "goalLastBindingStatus") + "; willingnessDispatch=" +
                            (ReadLong(host, "dialogueWillingnessDispatchCount") - dialogueDispatchBefore) +
                            "; classifications=" + classificationTrace.Count + "; rows=" + LatestRows(talk, 5));
                        nextDiagnostic = Time.realtimeSinceStartup + 30f;
                    }
                    yield return null;
                }
                if (!HasReply(host, talk, transcriptStart, boundTurn))
                    throw new InvalidOperationException("Controlled valid goal timed out before native binding/willingness reply.");

                string bindingStatus = ReadString(host, "goalLastBindingStatus");
                string dialogueRequest = ReadString(host, "dialogueWillingnessRequestJson");
                string dialogueResponse = ReadString(host, "dialogueWillingnessResponseJson");
                string dialogueChoice = ReadString(host, "dialogueWillingnessRawChoice");
                NpcTaskPlan task = store.ActiveNpcTask(actor.Resident.Id);
                bool exactTask = task != null && task.Steps != null && task.Steps.Count == 2 && task.CurrentStepIndex == 0 &&
                    task.Phase != NpcTaskPhase.Completed && task.Phase != NpcTaskPhase.Failed && task.Phase != NpcTaskPhase.Cancelled &&
                    // Native tasks normalize non-timed MoveTo metadata to zero.
                    task.Steps[0].Kind == NpcActionKind.MoveTo && task.Steps[0].DurationSeconds == 0d &&
                    NpcTaskContract.TargetMatches(moveTo.Target, task.Steps[0].Target) &&
                    task.Steps[1].Kind == NpcActionKind.Wait && task.Steps[1].DurationSeconds == 19d &&
                    NpcTaskPositionBinding.IsBoundWaitAfterMoveTo(task.Steps[0], task.Steps[1]);
                var targetTraces = classificationTrace.Where(item => !string.IsNullOrEmpty(item.Request) &&
                    !string.Equals(item.Request, dialogueRequest, StringComparison.Ordinal)).ToList();
                bool targetEvidence = targetTraces.Count >= 2 && targetTraces.All(item =>
                    !string.IsNullOrWhiteSpace(item.Response) && !string.IsNullOrWhiteSpace(item.Result));
                bool dialogueEvidence = !string.IsNullOrWhiteSpace(dialogueRequest) && !string.IsNullOrWhiteSpace(dialogueResponse) &&
                    !string.IsNullOrWhiteSpace(dialogueChoice) && classificationTrace.Any(item =>
                        string.Equals(item.Request, dialogueRequest, StringComparison.Ordinal) &&
                        string.Equals(item.Response, dialogueResponse, StringComparison.Ordinal) &&
                        string.Equals(item.Result, dialogueChoice, StringComparison.Ordinal));
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_BOUND_TRACE: source=" + Escape(boundTurn) +
                    "; controlledGoalJson=" + Escape(boundGoalJson) + "; completeOriginalExtraction=false" +
                    "; targetBindingRequestResponse=" + Escape(DescribeClassifications(targetTraces)) +
                    "; targetBindingClassificationCount=" + targetTraces.Count +
                    "; dialogueRequest=" + Escape(dialogueRequest) + "; dialogueResponse=" + Escape(dialogueResponse) +
                    "; dialogueRawChoice=" + Escape(dialogueChoice) + "; dialogueRequestSha256=" + Sha256(dialogueRequest) +
                    "; dialogueResponseSha256=" + Sha256(dialogueResponse) + "; singleModel=true" +
                    "; binding=" + bindingStatus + "; targetEvidence=" + targetEvidence.ToString().ToLowerInvariant() +
                    "; dialogueEvidence=" + dialogueEvidence.ToString().ToLowerInvariant() +
                    "; goalWillingnessDispatchDelta=" + (ReadLong(host, "goalWillingnessDispatchCount") - goalWillingnessBefore) +
                    "; dialogueDispatchDelta=" + (ReadLong(host, "dialogueWillingnessDispatchCount") - dialogueDispatchBefore) +
                    "; dialogueCompletionDelta=" + (ReadLong(host, "dialogueWillingnessCompletionCount") - dialogueCompletionBefore) +
                    "; task=" + Escape(DescribeTask(task)) + "; taskCountBefore=" + taskCountBefore +
                    "; taskCountAfter=" + store.Snapshot().NpcTasks.Count + "; transcript=" + Escape(LatestRows(talk, 6)) +
                    "; actualProductionTargetSelection=true; actualProductionWillingness=true; audibleSpeech=false.");
                if (bindingStatus != "bound" || ReadLong(host, "goalWillingnessDispatchCount") != goalWillingnessBefore + 1 ||
                    ReadLong(host, "dialogueWillingnessDispatchCount") != dialogueDispatchBefore + 1 ||
                    ReadLong(host, "dialogueWillingnessCompletionCount") != dialogueCompletionBefore + 1 ||
                    !string.Equals(ReadString(host, "dialogueWillingnessLastChoice"), "ACCEPT", StringComparison.Ordinal) ||
                    !targetEvidence || !dialogueEvidence || store.Snapshot().NpcTasks.Count != taskCountBefore + 1 || !exactTask)
                    throw new InvalidOperationException("Controlled MoveTo+Wait goal failed native production binding/Dialogue/task assertions. binding=" +
                        bindingStatus + "; choice=" + dialogueChoice + "; targetTraceCount=" + targetTraces.Count +
                        "; task=" + DescribeTask(task) + "; rows=" + LatestRows(talk, 6));
            }
            finally
            {
                client.ForegroundClassificationTrace = oldObserver;
            }
            yield return null;
        }

        private sealed class ClassificationEvidence
        {
            internal readonly string Request, Response, Result;
            internal ClassificationEvidence(string request, string response, string result)
            { Request = request; Response = response; Result = result; }
        }

        private static string DescribeClassifications(IList<ClassificationEvidence> traces)
        {
            return string.Join("\n", traces.Select(item => "request=" + item.Request + "\nresponse=" + item.Response +
                "\nresult=" + item.Result).ToArray());
        }

        private static void RunControlledCompletion(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            string turn, string output, string expectedChoice)
        {
            int callbacks = 0;
            string observed = null;
            InvokeCompletion(host, talk, turn, output, choice => { callbacks++; observed = choice; });
            if (callbacks != 1 || observed != expectedChoice)
                throw new InvalidOperationException("Controlled Dialogue completion result mismatch. expected=" + (expectedChoice ?? "<null>") +
                    "; observed=" + (observed ?? "<null>") + "; callbacks=" + callbacks);
        }

        private static void RunDialogueUnavailableControl(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            WorldStateStore store, ResidentActor actor, DaggerfallTalkWindow talk)
        {
            const string turn = "Could you wait here for eighteen seconds?";
            var candidates = streets.ConversationActionCandidates(actor.Resident.Id)
                .Where(candidate => candidate != null && candidate.Kind == NpcActionKind.Wait && candidate.Target != null).ToList();
            if (candidates.Count != 1) throw new InvalidOperationException("Dialogue-outage control requires one current native Wait capability.");
            var proposal = new List<NpcActionDecision> { new NpcActionDecision { Option = candidates[0], DurationSeconds = 18 } };
            object snapshot = Capture(host, talk, turn);
            string history = (string)Method(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", StaticMembers, 1)
                .Invoke(null, new object[] { talk });
            FieldInfo dialogueEnabled = Field(host.GetType(), "localDialogueEnabled");
            bool dialogueBefore = ReadBool(host, "localDialogueEnabled");
            string stateBefore = StateFingerprint(store, actor.Resident.Id);
            int taskCountBefore = store.Snapshot().NpcTasks.Count;
            int acceptedCallbacks = 0;
            try
            {
                // Disabled dialogue must never accept a proposal without consent.
                dialogueEnabled.SetValue(host, false);
                LocalDialogueClient client = Field(host.GetType(), "localDialogue").GetValue(host) as LocalDialogueClient;
                if (client) client.Cancel();
                Method(host.GetType(), "ResolveProposedActionWillingness", Members, 7).Invoke(host,
                    new object[] { actor, talk, turn, history, snapshot, proposal,
                        (Action<List<NpcActionDecision>>)(_ => acceptedCallbacks++) });
            }
            finally
            {
                LocalDialogueClient client = Field(host.GetType(), "localDialogue").GetValue(host) as LocalDialogueClient;
                if (client) client.Cancel();
                dialogueEnabled.SetValue(host, dialogueBefore);
            }
            bool stateUnchanged = StateFingerprint(store, actor.Resident.Id) == stateBefore &&
                store.Snapshot().NpcTasks.Count == taskCountBefore;
            if (acceptedCallbacks != 0 || !stateUnchanged)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_DIALOGUE_OUTAGE_BYPASS_FAIL: " +
                    "dialogueEnabled=false; acceptedCallbacks=" + acceptedCallbacks + "; nativeStateUnchanged=" + stateUnchanged +
                    "; taskCountBefore=" + taskCountBefore + "; taskCountAfter=" + store.Snapshot().NpcTasks.Count + ".");
                throw new InvalidOperationException("Unavailable dialogue bypassed willingness and invoked native accept callback.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_DIALOGUE_OUTAGE_GUARD_PASS: " +
                "acceptedCallbacks=0; nativeStateUnchanged=true.");
        }

        private static string StateFingerprint(WorldStateStore store, string residentId)
        {
            NpcActionJob action = store.LatestNpcAction(residentId);
            NpcTaskPlan task = store.ActiveNpcTask(residentId);
            return (action == null ? "<no-action>" : action.Id + "/" + action.Phase + "/" + action.Revision) + "|" +
                (task == null ? "<no-task>" : task.Id + "/" + task.Phase + "/" + task.Revision + "/" + task.CurrentStepIndex);
        }

        private static string StoreFingerprint(WorldStateStore store)
        {
            var state = store.Snapshot();
            var rows = new StringBuilder();
            foreach (NpcActionJob action in state.NpcActions.OrderBy(value => value.Id, StringComparer.Ordinal))
                rows.Append("A|").Append(action.Id).Append('|').Append(action.ProposalId).Append('|').Append(action.ResidentId)
                    .Append('|').Append(action.Kind).Append('|').Append(action.Phase).Append('|').Append(action.Revision)
                    .Append('|').Append(action.CompletedEffectId).Append('\n');
            foreach (NpcTaskPlan task in state.NpcTasks.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                rows.Append("T|").Append(task.Id).Append('|').Append(task.ProposalId).Append('|').Append(task.ResidentId)
                    .Append('|').Append(task.Phase).Append('|').Append(task.Revision).Append('|').Append(task.CurrentStepIndex).Append('|');
                if (task.Steps != null)
                    foreach (NpcTaskStep step in task.Steps)
                        rows.Append(step.Kind).Append(':').Append(step.DurationSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                rows.Append('\n');
            }
            return rows.ToString();
        }

        private static void RunDuplicateCompletion(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk)
        {
            const string turn = "The controlled duplicate ACCEPT control.";
            object snapshot = Capture(host, talk, turn);
            long stage = BeginStage(host, snapshot, talk);
            int session = ReadInt(host, "memorySession");
            string person = ReadString(host, "conversationPersonId");
            int callbacks = 0;
            Action<string> complete = choice => { if (choice == "ACCEPT") callbacks++; };
            Complete(host, snapshot, talk, turn, session, person, stage, "ACCEPT", complete);
            Complete(host, snapshot, talk, turn, session, person, stage, "ACCEPT", complete);
            if (callbacks != 1) throw new InvalidOperationException("Duplicate controlled ACCEPT completion invoked callback more than once.");
        }

        private static void RunClosedStaleCompletion(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, ResidentActor actor)
        {
            const string turn = "The controlled closed stale ACCEPT control.";
            object snapshot = Capture(host, talk, turn);
            long stage = BeginStage(host, snapshot, talk);
            int session = ReadInt(host, "memorySession");
            string person = ReadString(host, "conversationPersonId");
            int callbacks = 0;
            DaggerfallUI.Instance.PopToHUD();
            Complete(host, snapshot, talk, turn, session, person, stage, "ACCEPT", choice =>
            { if (choice == "ACCEPT") callbacks++; });
            if (callbacks != 0 || ReadBool(host, "conversationSessionOpen"))
                throw new InvalidOperationException("Closed stale ACCEPT reached callback or retained the conversation session.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_DIALOGUE_CONTROLLED_CASE_PASS: case=closed-stale-ACCEPT; actor=" +
                (actor && actor.Resident != null ? actor.Resident.Id : "<missing>") + "; callbackCount=0; committed=false.");
        }

        private static void InvokeCompletion(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            string turn, string output, Action<string> complete)
        {
            object snapshot = Capture(host, talk, turn);
            long stage = BeginStage(host, snapshot, talk);
            Complete(host, snapshot, talk, turn, ReadInt(host, "memorySession"),
                ReadString(host, "conversationPersonId"), stage, output, complete);
        }

        private static object Capture(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk, string turn)
        {
            long revision = ReadLong(host, "conversationContextRequestRevision") + 1;
            object snapshot = Method(host.GetType(), "CaptureConversationContext", Members, 3)
                .Invoke(host, new object[] { talk, turn, revision });
            if (snapshot == null) throw new InvalidOperationException("Controlled Dialogue completion could not capture native Talk context.");
            return snapshot;
        }

        private static long BeginStage(PeopleOfTheIliacBayMod host, object snapshot, DaggerfallTalkWindow talk)
        {
            long stage = Convert.ToInt64(Method(host.GetType(), "BeginConversationContextStage", Members, 2)
                .Invoke(host, new object[] { snapshot, talk }));
            if (stage <= 0) throw new InvalidOperationException("Controlled Dialogue completion could not issue a native context stage.");
            return stage;
        }

        private static void Complete(PeopleOfTheIliacBayMod host, object snapshot, DaggerfallTalkWindow talk,
            string turn, int session, string person, long stage, string output, Action<string> complete)
        {
            Method(host.GetType(), "CompleteDialogueWillingness", Members, 9).Invoke(host,
                new object[] { snapshot, talk, turn, session, person, stage, 2, output, complete });
        }

        private static bool IsBoundGoalMode()
        { return Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-qwen-bound-probe") >= 0; }

        private static int TranscriptCount(DaggerfallTalkWindow talk)
        {
            var list = Field(typeof(DaggerfallTalkWindow), "listboxConversation").GetValue(talk) as ListBox;
            if (list == null) throw new InvalidOperationException("Native Talk transcript is unavailable.");
            return list.Count;
        }

        private static bool HasReply(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk, int start, string turn)
        {
            // The purple temporary thinking row follows the player immediately.
            // It is not a completed answer and must not terminate this probe.
            LocalDialogueClient client = Field(host.GetType(), "localDialogue").GetValue(host) as LocalDialogueClient;
            if (ReadBool(host, "conversationBusy") || (client && client.IsBusy)) return false;
            var list = Field(typeof(DaggerfallTalkWindow), "listboxConversation").GetValue(talk) as ListBox;
            if (list == null) return false;
            for (int i = Math.Max(0, start); i + 1 < list.Count; i++)
            {
                var question = list.GetItem(i);
                var answer = list.GetItem(i + 1);
                if (question != null && question.textLabel != null && answer != null && answer.textLabel != null &&
                    question.textColor == DaggerfallUI.DaggerfallQuestionTextColor && question.textLabel.Text == turn &&
                    answer.textColor != DaggerfallUI.DaggerfallQuestionTextColor && !string.IsNullOrWhiteSpace(answer.textLabel.Text))
                    return true;
            }
            return false;
        }

        private static string LatestRows(DaggerfallTalkWindow talk, int maximum)
        {
            var list = Field(typeof(DaggerfallTalkWindow), "listboxConversation").GetValue(talk) as ListBox;
            if (list == null) return "<unavailable>";
            var rows = new List<string>();
            for (int i = Math.Max(0, list.Count - maximum); i < list.Count; i++)
            {
                var row = list.GetItem(i);
                rows.Add("[" + i + "," + (row == null ? "" : row.textColor.ToString()) + "," +
                    (row == null || row.textLabel == null ? "" : row.textLabel.Text).Replace("\r", " ").Replace("\n", " ") + "]");
            }
            return string.Join(" ", rows.ToArray());
        }

        private static string DescribeTask(NpcTaskPlan task)
        {
            if (task == null) return "<none>";
            return task.Id + "/" + task.Phase + "/step=" + task.CurrentStepIndex + "/" + string.Join(",",
                task.Steps == null ? new string[0] : task.Steps.Select(step => step.Kind + ":" +
                    step.DurationSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        }

        private static string Escape(string value)
        {
            string encoded = JsonUtility.ToJson(new LogString { value = value ?? "" });
            return encoded.Length >= 13 ? encoded.Substring(10, encoded.Length - 12) : "\"\"";
        }

        private static string Sha256(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))).Replace("-", "").ToLowerInvariant();
        }

        private static bool ReadBool(PeopleOfTheIliacBayMod host, string name)
        { return Convert.ToBoolean(Field(host.GetType(), name).GetValue(host), System.Globalization.CultureInfo.InvariantCulture); }
        private static int ReadInt(PeopleOfTheIliacBayMod host, string name)
        { return Convert.ToInt32(Field(host.GetType(), name).GetValue(host), System.Globalization.CultureInfo.InvariantCulture); }
        private static long ReadLong(PeopleOfTheIliacBayMod host, string name)
        { return Convert.ToInt64(Field(host.GetType(), name).GetValue(host), System.Globalization.CultureInfo.InvariantCulture); }
        private static string ReadString(PeopleOfTheIliacBayMod host, string name)
        { return Field(host.GetType(), name).GetValue(host) as string; }
        private static void SetIfPresent(PeopleOfTheIliacBayMod host, string name, object value)
        { FieldInfo field = host.GetType().GetField(name, Members); if (field != null) field.SetValue(host, value); }

        private static FieldInfo Field(Type type, string name)
        { return type.GetField(name, Members) ?? throw new InvalidOperationException("Required Dialogue willingness probe field missing: " + name); }
        private static MethodInfo Method(Type type, string name, BindingFlags flags, int count)
        { return type.GetMethods(flags).FirstOrDefault(method => method.Name == name && method.GetParameters().Length == count) ??
            throw new InvalidOperationException("Required Dialogue willingness probe method missing: " + name); }
        private static MethodInfo Method(Type type, string name, BindingFlags flags, Type[] parameterTypes)
        { return type.GetMethod(name, flags, null, parameterTypes, null) ??
            throw new InvalidOperationException("Required Dialogue willingness probe overload missing: " + name); }

        [Serializable]
        private sealed class LogString { public string value; }
    }
}
