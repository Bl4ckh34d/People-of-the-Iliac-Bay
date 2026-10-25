using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in integration probe for receipt-grounded action wording. Native state
    // remains authoritative; this probe checks the renderer's UI/archive boundary.
    internal static class ActionWordingProbe
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string BaselineSave = "People - Gothway Garden";
        private const string RunFlag = "-peopleoftheiliacbay-action-wording-probe";

        private sealed class Api
        {
            internal FieldInfo Store;
            internal FieldInfo SessionOpen;
            internal FieldInfo Busy;
            internal FieldInfo GreetingPending;
            internal FieldInfo GreetingRetry;
            internal FieldInfo HoldCompletion;
            internal FieldInfo ReleaseCompletion;
            internal FieldInfo PendingWording;
            internal FieldInfo LocalDialogue;
            internal FieldInfo PendingPlayerRow;
            internal FieldInfo PendingPlayerText;
            internal FieldInfo PendingRow;
            internal FieldInfo PendingTranscript;
            internal FieldInfo PendingLabel;
            internal FieldInfo PendingTracking;
            internal FieldInfo PendingStartedAt;
            internal FieldInfo PendingOverlayWindow;
            internal FieldInfo TtsBridge;
            internal FieldInfo TrialTranscriptBindings;
            internal MethodInfo Submit;
            internal MethodInfo CompleteWording;
            internal MethodInfo UnavailableText;
            internal MethodInfo FromSavedAction;
            internal MethodInfo MatchesSavedState;
            internal MethodInfo Capture;
            internal MethodInfo ReadHistory;
            internal PropertyInfo RequestCount;
            internal PropertyInfo CompletionCount;
            internal PropertyInfo PublishedTtsRequestCount;
            internal ListBox.ListItem LastObservedPendingRow;
            internal ListBox.ListItem LastObservedPendingPlayerRow;
            internal ListBox LastObservedPendingTranscript;
        }

        private sealed class FixtureState
        {
            internal string ResidentId;
            internal string ActionId;
            internal string FirstWording;
            internal string FirstStatus;
            internal bool OriginalGreetingPending;
            internal float OriginalGreetingRetry;
            internal Vector3 OriginalPlayerPosition;
            internal bool CapturedHostState;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            RequireOwnedFixture(character);
            Api api = RequireApi();
            var state = new FixtureState();
            GameManager game = GameManager.Instance;
            if (host && game && game.PlayerMotor)
            {
                state.OriginalGreetingPending = Convert.ToBoolean(api.GreetingPending.GetValue(host));
                state.OriginalGreetingRetry = Convert.ToSingle(api.GreetingRetry.GetValue(host));
                state.OriginalPlayerPosition = game.PlayerMotor.transform.position;
                state.CapturedHostState = true;
            }

            Exception probeFailure = null;
            IEnumerator probe = RunCases(host, streets, character, api, state);
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = probe.MoveNext(); if (more) current = probe.Current; }
                    catch (Exception error) { probeFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally { Dispose(probe, ref probeFailure); }

            // Close the real Talk window before delivering any deliberately held
            // completion. On a failed probe, discard rather than run a held
            // callback: the normal close/load path owns canonical fallback.
            if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
            api.HoldCompletion.SetValue(host, false);
            api.ReleaseCompletion.SetValue(host, null);

            Exception restoreFailure = null;
            IEnumerator restore = RestoreBaseline(host, streets, character);
            try
            {
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
            finally { Dispose(restore, ref restoreFailure); }

            if (restoreFailure != null)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_BASELINE_RESTORE_FAIL: " + restoreFailure);
                throw new InvalidOperationException("Action wording probe could not restore the exact baseline fixture.", restoreFailure);
            }
            if (state.CapturedHostState && host && GameManager.Instance && GameManager.Instance.PlayerMotor)
            {
                api.GreetingPending.SetValue(host, state.OriginalGreetingPending);
                api.GreetingRetry.SetValue(host, state.OriginalGreetingRetry);
                if (Vector3.Distance(GameManager.Instance.PlayerMotor.transform.position, state.OriginalPlayerPosition) > 0.05f)
                    throw new InvalidOperationException("Baseline load did not restore the probe's original player position.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_BASELINE_RESTORED: character=" + character +
                "; save=" + BaselineSave + ".");
            if (probeFailure != null)
                throw new InvalidOperationException("Action wording probe failed after baseline restore.", probeFailure);

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_PASS: action=" + state.ActionId +
                "; first-status=" + state.FirstStatus + "; first-llm-wording=\"" + EscapeLog(state.FirstWording) +
                "\"; fresh-request=1; duplicate-wrapper=noop; injected-duplicate-control=passed; " +
                "close-fallback=once; save-fallback=once; late-results=noop; model-prose-is-not-authoritative=true; " +
                "disk-reload=not-tested.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, Api api, FixtureState state)
        {
            GameManager game = GameManager.Instance;
            if (!host || !streets || !game || !game.IsReady || !game.PlayerMotor || !streets.Ready ||
                game.PlayerEntity == null || game.PlayerEntity.Name != character)
                throw new InvalidOperationException("Action wording probe requires the ready owned native fixture.");
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
                throw new InvalidOperationException("Action wording probe requires the Gothway Garden exterior fixture.");

            SaveLoadManager saves = game.SaveLoadManager;
            saves.EnumerateSaves();
            if (saves.FindSaveFolderByNames(character, BaselineSave) < 0)
                throw new InvalidOperationException("Exact baseline fixture save is missing; refusing action wording probe.");

            WorldStateStore store = api.Store.GetValue(host) as WorldStateStore;
            if (store == null) throw new InvalidOperationException("Active native world store is unavailable.");
            ResidentActor actor = FindUnusedManagedAdult(store, game.PlayerMotor.transform.position);
            float actorDeadline = Time.realtimeSinceStartup + 45f;
            while (!actor && Time.realtimeSinceStartup < actorDeadline)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                store = api.Store.GetValue(host) as WorldStateStore;
                actor = store == null ? null : FindUnusedManagedAdult(store, game.PlayerMotor.transform.position);
            }
            if (!actor) throw new InvalidOperationException("No unused nearby managed adult spawned within 45 seconds.");
            if (Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position) > 8f)
            {
                Vector3 observer;
                if (!ConversationReplyOutcomeProbe.TrySurveyedOutdoorObserver(streets,
                    actor.transform.position - Vector3.up, out observer))
                    throw new InvalidOperationException("No nearby actor or surveyed safe player observer point is available.");
                var motor = game.PlayerMotor;
                CharacterController controller = motor.GetComponent<CharacterController>();
                bool motorEnabled = motor.enabled;
                bool controllerEnabled = controller && controller.enabled;
                motor.enabled = false;
                if (controller) controller.enabled = false;
                try
                {
                    motor.transform.position = observer + Vector3.up;
                    Physics.SyncTransforms();
                    yield return null;
                }
                finally
                {
                    if (controller) controller.enabled = controllerEnabled;
                    motor.enabled = motorEnabled;
                }
                if (!actor || Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position) > 8f ||
                    Vector3.Distance(actor.transform.position - Vector3.up, observer) < 0.01f)
                    throw new InvalidOperationException("Player-only observer staging failed or actor position changed.");
            }
            state.ResidentId = actor.Resident.Id;

            DaggerfallTalkWindow talk = null;
            FieldInfo pendingGreeting = api.GreetingPending;
            FieldInfo retryGreeting = api.GreetingRetry;
            FieldInfo busy = api.Busy;
            yield return OpenTalk(host, game, actor, pendingGreeting, retryGreeting, busy, value => talk = value);
            if (talk == null || DaggerfallUI.UIManager.TopWindow != talk || !talk.IsSetup)
                throw new InvalidOperationException("Native Talk did not open on the selected managed resident.");
            if (!Convert.ToBoolean(api.SessionOpen.GetValue(host)))
                BeginConversation(host);
            pendingGreeting.SetValue(host, false);
            retryGreeting.SetValue(host, Time.unscaledTime + 86400f);
            busy.SetValue(host, true);
            float talkDeadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < talkDeadline &&
                (!actor || !actor.IsConversing || DaggerfallUI.UIManager.TopWindow != talk ||
                 game.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile || game.TalkManager.MobileNPC != actor.Person))
                yield return null;
            if (!actor || !actor.IsConversing || DaggerfallUI.UIManager.TopWindow != talk ||
                game.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile || game.TalkManager.MobileNPC != actor.Person)
                throw new InvalidOperationException("Native Talk did not bind the selected actor before wording test.");

            RequireNoPriorActionOrTask(store, actor.Resident.Id);
            object localDialogue = api.LocalDialogue.GetValue(host);
            if (localDialogue == null) throw new InvalidOperationException("Local dialogue client is unavailable.");
            long requestCountBefore = ReadRequestCount(api, localDialogue);
            long completionCountBefore = Convert.ToInt64(api.CompletionCount.GetValue(localDialogue, null));

            // Exercise a typed unresolved receipt before any accepted-action
            // fixture. The renderer must use its reason as context only; this
            // probe deliberately makes no claim about the model's semantic reply.
            const string unresolvedTurn = "Could you buy bread for me?";
            const string unresolvedReason = "Requested item quantity is unresolved.";
            ConversationActionReply unresolvedReceipt = ConversationActionReply.Unresolved(unresolvedReason);
            if (unresolvedReceipt.Status != ConversationActionReplyStatus.NoChange ||
                unresolvedReceipt.Source != ConversationActionReplySource.None ||
                unresolvedReceipt.UnresolvedReason != unresolvedReason)
                throw new InvalidOperationException("Typed unresolved control did not retain its no-change receipt and reason.");
            object unresolvedSnapshot = CaptureNext(host, api.Capture, talk, unresolvedTurn);
            string unresolvedHistoryBefore = ReadHistory(api.ReadHistory, talk);
            int unresolvedPlayerHistoryBefore = CountOccurrences(unresolvedHistoryBefore, "Player: " + unresolvedTurn);
            Vector3 unresolvedPlayerPositionBefore = game.PlayerMotor.transform.position;
            Vector3 unresolvedNpcPositionBefore = actor.transform.position;
            int unresolvedPlayerArchiveBefore = CountPlayerTurn(store, actor.Resident.Id, unresolvedTurn);
            int unresolvedPlayerGoldBefore = game.PlayerEntity.GetGoldAmount();
            string unresolvedPlayerItemsBefore = PlayerItems(game);
            string unresolvedNpcItemsBefore = NpcItems(store, actor.Resident.Id);
            if (store.ActiveNpcAction(actor.Resident.Id) != null || store.ActiveNpcTask(actor.Resident.Id) != null)
                throw new InvalidOperationException("Unresolved control fixture already owns active native work.");
            bool unresolvedSubmitted = Submit(api, host, talk, unresolvedTurn, unresolvedSnapshot, unresolvedReceipt);
            if (!unresolvedSubmitted || ReadRequestCount(api, localDialogue) != requestCountBefore + 1)
                throw new InvalidOperationException("Unresolved control did not dispatch exactly one real wording request.");

            NpcMemoryEntry unresolvedEntry = null;
            float unresolvedDeadline = Time.realtimeSinceStartup + 150f;
            while (Time.realtimeSinceStartup < unresolvedDeadline && unresolvedEntry == null)
            {
                unresolvedEntry = FindSingleTurn(store, actor.Resident.Id, unresolvedTurn);
                if (unresolvedEntry != null &&
                    Convert.ToInt64(api.CompletionCount.GetValue(localDialogue, null)) < completionCountBefore + 1)
                    unresolvedEntry = null;
                if (unresolvedEntry != null) break;
                yield return null;
            }
            // Let the renderer's next-frame indicator cleanup run before checking
            // that no transient ownership remains.
            yield return null;
            if (unresolvedEntry == null || string.IsNullOrWhiteSpace(unresolvedEntry.NpcText) ||
                Convert.ToInt64(api.CompletionCount.GetValue(localDialogue, null)) != completionCountBefore + 1 ||
                ReadRequestCount(api, localDialogue) != requestCountBefore + 1)
                throw new InvalidOperationException("Unresolved control did not produce one request, one nonempty real completion, and one archived reply.");
            string unresolvedHistory = ReadHistory(api.ReadHistory, talk);
            if (unresolvedEntry.Source != "action" || CountPlayerTurn(store, actor.Resident.Id, unresolvedTurn) != unresolvedPlayerArchiveBefore + 1 ||
                CountOccurrences(unresolvedHistory, "Player: " + unresolvedTurn) != unresolvedPlayerHistoryBefore + 1 ||
                CountOccurrences(unresolvedHistory, unresolvedEntry.NpcText) !=
                    CountOccurrences(unresolvedHistoryBefore, unresolvedEntry.NpcText) + 1 ||
                CountTranscriptPair(talk, unresolvedTurn, unresolvedEntry.NpcText, null) != 1)
                throw new InvalidOperationException("Unresolved control did not produce one visible and archived action-provenance reply for exactly one player turn.");
            if (store.ActiveNpcAction(actor.Resident.Id) != null || store.ActiveNpcTask(actor.Resident.Id) != null ||
                PlayerItems(game) != unresolvedPlayerItemsBefore || game.PlayerEntity.GetGoldAmount() != unresolvedPlayerGoldBefore ||
                NpcItems(store, actor.Resident.Id) != unresolvedNpcItemsBefore ||
                Vector3.Distance(game.PlayerMotor.transform.position, unresolvedPlayerPositionBefore) > 0.01f ||
                Vector3.Distance(actor.transform.position, unresolvedNpcPositionBefore) > 0.01f)
                throw new InvalidOperationException("Unresolved wording changed native action/task ownership or player/NPC inventory state.");
            if (api.PendingWording.GetValue(host) != null || api.PendingRow.GetValue(host) != null ||
                api.PendingLabel.GetValue(host) != null || Convert.ToBoolean(api.PendingTracking.GetValue(host)) ||
                api.PendingOverlayWindow.GetValue(host) != null || Convert.ToBoolean(api.Busy.GetValue(host)))
                throw new InvalidOperationException("Unresolved control left pending wording/UI ownership or conversation busy state active.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_UNRESOLVED_PASS: request=\"" + unresolvedTurn +
                "\"; reply=\"" + EscapeLog(unresolvedEntry.NpcText) + "\"; receipt-reason=\"" +
                EscapeLog(unresolvedReceipt.UnresolvedReason) + "\"; status=" + unresolvedReceipt.Status +
                "; receipt-source=" + unresolvedReceipt.Source + "; archive-source=" + unresolvedEntry.Source +
                "; one-request-one-completion=true; one-player-turn=true; native-action-task-inventory-effects=none; pending-ownership=cleared; " +
                "semantic-quality=not-asserted.");

            // Later action-specific request/completion baselines exclude the
            // accepted unresolved control above.
            requestCountBefore = ReadRequestCount(api, localDialogue);
            completionCountBefore = Convert.ToInt64(api.CompletionCount.GetValue(localDialogue, null));

            // This is an explicitly injected native consent fixture: the probe
            // obtains the current Wait capability and commits it through the real
            // StreetPopulation validator, without treating generated prose as assent.
            NpcActionOption wait = FindWait(streets.ConversationActionCandidates(actor.Resident.Id));
            if (wait == null) throw new InvalidOperationException("Selected resident has no current native Wait option.");
            string acceptReason = null;
            string proposalId = "action-wording-probe:" + Guid.NewGuid().ToString("N");
            if (!NpcProbeCommandSource.TryAcceptAction(streets,actor.Resident.Id, proposalId,
                new NpcActionDecision { Option = wait, DurationSeconds = 10 }, out acceptReason))
                throw new InvalidOperationException("Native Wait consent fixture was rejected: " + acceptReason);
            NpcActionJob action = store.ActiveNpcAction(actor.Resident.Id);
            if (action == null || action.Kind != NpcActionKind.Wait || action.Phase != NpcActionPhase.Accepted)
                throw new InvalidOperationException("Native accepted Wait is not pending before wording.");
            state.ActionId = action.Id;
            string actionBefore = ActionSourceIdentity(action);

            const string firstTurn = "Could you tell me where things stand with the wait we agreed on?";
            object firstSnapshot = CaptureNext(host, api.Capture, talk, firstTurn);
            object firstReceipt = SavedActionReply(api, store, action.Id);
            if (ReadMember(firstReceipt, "Status").ToString() != "AcceptedPending")
                throw new InvalidOperationException("Fresh saved Wait receipt is not AcceptedPending before the first wording request.");
            bool firstSubmitted = Submit(api, host, talk, firstTurn, firstSnapshot, firstReceipt);
            long firstRequestCount = ReadRequestCount(api, localDialogue);
            bool firstDuplicate = Submit(api, host, talk, firstTurn, firstSnapshot, firstReceipt);
            if (!firstSubmitted || firstDuplicate || firstRequestCount != requestCountBefore + 1 ||
                ReadRequestCount(api, localDialogue) != firstRequestCount)
                throw new InvalidOperationException("First accepted Wait wording did not dispatch exactly one fresh LocalDialogueClient.Request or duplicate submission was not rejected.");

            NpcMemoryEntry firstEntry = null;
            float firstDeadline = Time.realtimeSinceStartup + 150f;
            while (Time.realtimeSinceStartup < firstDeadline && firstEntry == null)
            {
                firstEntry = FindSingleTurn(store, actor.Resident.Id, firstTurn);
                if (firstEntry != null) break;
                yield return null;
            }
            if (firstEntry == null || firstEntry.Source != "action" || string.IsNullOrWhiteSpace(firstEntry.NpcText))
                throw new InvalidOperationException("Real-Qwen action wording did not append one non-empty reply within 150 seconds.");
            state.FirstWording = firstEntry.NpcText;
            state.FirstStatus = ReadMember(firstReceipt, "Status").ToString();
            if (Convert.ToInt64(api.CompletionCount.GetValue(localDialogue, null)) != completionCountBefore + 1)
                throw new InvalidOperationException("No fresh nonempty model completion was observed; a request count alone does not prove transport.");
            RequireSingleUiStorePair(talk, api.ReadHistory, store, actor.Resident.Id, firstTurn, firstEntry.NpcText);
            RequireActionSourcePreserved(store, actor.Resident.Id, action.Id, actionBefore, "first wording callback");

            // Inject the exact previous NPC sentence at the completion boundary.
            // This control proves duplicate handling and provenance; it does not
            // measure model wording quality.
            const string duplicateTurn = "Can you update me about our wait?";
            object duplicateSnapshot = CaptureNext(host, api.Capture, talk, duplicateTurn);
            object duplicateReceipt = SavedActionReply(api, store, action.Id);
            int duplicateHistoryBefore = CountOccurrences(ReadHistory(api.ReadHistory, talk), "Player: " + duplicateTurn);
            string duplicateHistoryBeforeText = ReadHistory(api.ReadHistory, talk);
            int duplicateArchiveBefore = CountPlayerTurn(store, actor.Resident.Id, duplicateTurn);
            long duplicateRequestsBefore = ReadRequestCount(api, localDialogue);
            long duplicateCompletionsBefore = Convert.ToInt64(api.CompletionCount.GetValue(localDialogue, null));
            string unavailableText = GetActionWordingUnavailableText(api);
            int unavailableSpeechBefore = CountOccurrences(duplicateHistoryBeforeText, "NPC: " + unavailableText);
            int unavailableRowBefore = CountOccurrences(duplicateHistoryBeforeText,
                DialogueHistoryWindow.UnavailableResponseLabel + unavailableText);
            api.ReleaseCompletion.SetValue(host, null);
            api.HoldCompletion.SetValue(host, true);
            bool duplicateSubmitted = Submit(api, host, talk, duplicateTurn, duplicateSnapshot, duplicateReceipt);
            if (!duplicateSubmitted || ReadRequestCount(api, localDialogue) != duplicateRequestsBefore + 1)
                throw new InvalidOperationException("Injected duplicate control did not start exactly one held real request.");
            yield return WaitForHeldCompletion(host, api, 150f, "injected duplicate control");
            Action heldDuplicateCompletion = api.ReleaseCompletion.GetValue(host) as Action;
            if (heldDuplicateCompletion == null || api.PendingWording.GetValue(host) == null ||
                Convert.ToInt64(api.CompletionCount.GetValue(localDialogue, null)) != duplicateCompletionsBefore + 1)
                throw new InvalidOperationException("Injected duplicate control did not reach held completion after a real model response.");
            api.HoldCompletion.SetValue(host, false);
            api.ReleaseCompletion.SetValue(host, null);
            InvokeDuplicateCompletion(api, host, EncodeMoodResult(state.FirstWording, 0));
            if (ReadRequestCount(api, localDialogue) != duplicateRequestsBefore + 2)
                throw new InvalidOperationException("Exact duplicate did not trigger exactly one bounded rephrase request.");

            NpcMemoryEntry duplicateEntry = null;
            float duplicateDeadline = Time.realtimeSinceStartup + 150f;
            while (Time.realtimeSinceStartup < duplicateDeadline && duplicateEntry == null)
            {
                duplicateEntry = FindSingleTurn(store, actor.Resident.Id, duplicateTurn);
                if (duplicateEntry != null && duplicateEntry.Source != "unavailable" &&
                    Convert.ToInt64(api.CompletionCount.GetValue(localDialogue, null)) < duplicateCompletionsBefore + 2)
                    duplicateEntry = null;
                if (duplicateEntry != null) break;
                yield return null;
            }
            if (duplicateEntry == null || string.IsNullOrWhiteSpace(duplicateEntry.NpcText) ||
                CountPlayerTurn(store, actor.Resident.Id, duplicateTurn) != duplicateArchiveBefore + 1 ||
                CountOccurrences(ReadHistory(api.ReadHistory, talk), "Player: " + duplicateTurn) != duplicateHistoryBefore + 1)
                throw new InvalidOperationException("Injected duplicate control did not append exactly one player/archive turn.");
            int duplicateUiRows = CountTranscriptPair(talk, duplicateTurn, duplicateEntry.NpcText, null);
            if (duplicateEntry.Source == "unavailable")
            {
                int unavailableUiRows = CountTranscriptPair(talk, duplicateTurn, duplicateEntry.NpcText,
                    "PeopleOfTheIliacBayUnavailableReply");
                string unavailableLine = DialogueHistoryWindow.UnavailableResponseLabel + duplicateEntry.NpcText;
                if (duplicateEntry.NpcText != unavailableText || unavailableUiRows != 1 || duplicateUiRows != 1 ||
                    CountOccurrences(ReadHistory(api.ReadHistory, talk), unavailableLine) != unavailableRowBefore + 1 ||
                    CountOccurrences(ReadHistory(api.ReadHistory, talk), "NPC: " + duplicateEntry.NpcText) != unavailableSpeechBefore)
                    throw new InvalidOperationException("Duplicate rephrase fallback lost unavailable-row provenance or appeared as NPC speech.");
            }
            else if (duplicateEntry.Source != "action" || duplicateEntry.NpcText == state.FirstWording ||
                duplicateUiRows != 1 || CountTranscriptPair(talk, duplicateTurn, duplicateEntry.NpcText,
                    "PeopleOfTheIliacBayUnavailableReply") != 0 ||
                CountOccurrences(ReadHistory(api.ReadHistory, talk), "NPC: " + duplicateEntry.NpcText) != 1)
                throw new InvalidOperationException("Duplicate rephrase did not produce one distinct action-sourced NPC reply.");
            if (Convert.ToInt64(api.CompletionCount.GetValue(localDialogue, null)) != duplicateCompletionsBefore + 2)
                throw new InvalidOperationException("Injected duplicate control did not observe exactly one real rephrase completion.");
            RequireActionSourcePreserved(store, actor.Resident.Id, action.Id, actionBefore, "injected duplicate control");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_DUPLICATE_CONTROL_PASS: injected-exact-duplicate=true; " +
                "one-rephrase-request=true; one-player-turn=true; outcome=" + duplicateEntry.Source + "; model-quality-not-claimed=true.");

            // Hold the next real Qwen completion at the final callback boundary.
            // Closing Talk must flush only the verified canonical receipt. Running
            // the held callback afterward must not append or journal a second row.
            const string closeTurn = "Please confirm the plan before we part.";
            object closeSnapshot = CaptureNext(host, api.Capture, talk, closeTurn);
            object closeReceipt = SavedActionReply(api, store, action.Id);
            string closeCanonical = ReadString(closeReceipt, "NpcText");
            string closeHistoryBeforeText = ReadHistory(api.ReadHistory, talk);
            bool closeCanonicalDuplicate = DialogueHistoryWindow.IsDuplicateReply(
                DialogueHistoryWindow.LastNpcText(closeHistoryBeforeText), closeCanonical);
            int closeHistoryBefore = CountOccurrences(closeHistoryBeforeText, closeCanonical);
            int closeArchiveBefore = CountPair(store, actor.Resident.Id, closeTurn, closeCanonical);
            int closePlayerArchiveBefore = CountPlayerTurn(store, actor.Resident.Id, closeTurn);
            int closeUnavailableBefore = CountOccurrences(closeHistoryBeforeText,
                DialogueHistoryWindow.UnavailableResponseLabel + closeCanonical);
            int closePlayerHistoryBefore = CountOccurrences(closeHistoryBeforeText, "Player: " + closeTurn);
            int closeSpeechBefore = CountOccurrences(closeHistoryBeforeText, "NPC: " + closeCanonical);
            long closeRequestsBefore = ReadRequestCount(api, localDialogue);
            string closeActionBefore = ActionSourceIdentity(store.ReadNpcAction(action.Id));
            api.ReleaseCompletion.SetValue(host, null);
            api.HoldCompletion.SetValue(host, true);
            bool closeSubmitted = Submit(api, host, talk, closeTurn, closeSnapshot, closeReceipt);
            bool closeDuplicate = Submit(api, host, talk, closeTurn, closeSnapshot, closeReceipt);
            if (!closeSubmitted || closeDuplicate || ReadRequestCount(api, localDialogue) != closeRequestsBefore + 1)
                throw new InvalidOperationException("Close-race wording request was not dispatched exactly once.");
            yield return WaitForHeldCompletion(host, api, 150f, "Talk-close");
            Action closeLate = api.ReleaseCompletion.GetValue(host) as Action;
            if (closeLate == null) throw new InvalidOperationException("Held Talk-close model completion was not observable.");
            string closeHistoryBeforeIndicator = ReadHistory(api.ReadHistory, talk);
            yield return WaitForPendingIndicator(host, api, talk, closeHistoryBeforeIndicator, "Talk-close");
            RequireActionSourcePreserved(store, actor.Resident.Id, action.Id, closeActionBefore, "held Talk-close callback");
            DaggerfallUI.Instance.PopToHUD();
            float closeDeadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < closeDeadline && Convert.ToBoolean(api.SessionOpen.GetValue(host))) yield return null;
            if (Convert.ToBoolean(api.SessionOpen.GetValue(host)))
                throw new InvalidOperationException("Closing Talk did not end the conversation before held completion delivery.");
            string closeHistoryBeforeIndicatorRemoval = ReadHistory(api.ReadHistory, talk);
            yield return null;
            RequirePendingIndicatorRemoved(host, api, talk, closeHistoryBeforeIndicatorRemoval, "Talk close");
            if (api.PendingWording.GetValue(host) != null)
                throw new InvalidOperationException("Talk close left pending action wording ownership active.");
            RequireActionFallback(talk, api, store, actor.Resident.Id, closeTurn, closeCanonical,
                closeCanonicalDuplicate, closeHistoryBefore, closeArchiveBefore, closePlayerArchiveBefore,
                closeUnavailableBefore, closePlayerHistoryBefore, closeSpeechBefore, closeHistoryBeforeText,
                GetActionWordingUnavailableText(api), "Talk close");
            WorldState closeSavedBeforeLate = host.GetSaveData() as WorldState;
            if (closeSavedBeforeLate == null) throw new InvalidOperationException("GetSaveData returned no snapshot after Talk-close fallback.");
            string closeHistoryAfterFlush = ReadHistory(api.ReadHistory, talk);
            int closeArchiveAfterFlush = CountPlayerTurn(store, actor.Resident.Id, closeTurn);
            string closeActionAfterFlush = ActionSourceIdentity(store.ReadNpcAction(action.Id));
            long closeRequestCountAfterFlush = ReadRequestCount(api, localDialogue);
            // Take mutable effect snapshots immediately before the synchronous
            // stale callback. No GetSaveData or frame yield occurs between these
            // snapshots and invoking the callback.
            string closeActionStateBeforeLate = ActionSignature(store.ReadNpcAction(action.Id));
            string closePlayerItemsBeforeLate = PlayerItems(game);
            int closePlayerGoldBeforeLate = game.PlayerEntity.GetGoldAmount();
            string closeNpcItemsBeforeLate = NpcItems(store, actor.Resident.Id);
            closeLate();
            if (api.PendingWording.GetValue(host) != null)
                throw new InvalidOperationException("Late Talk-close callback reclaimed cleared wording ownership.");
            if (ReadHistory(api.ReadHistory, talk) != closeHistoryAfterFlush)
                throw new InvalidOperationException("Late Talk-close callback changed native conversation history.");
            if (CountPlayerTurn(store, actor.Resident.Id, closeTurn) != closeArchiveAfterFlush)
                throw new InvalidOperationException("Late Talk-close callback appended another archive turn.");
            if (ActionSignature(store.ReadNpcAction(action.Id)) != closeActionStateBeforeLate)
                throw new InvalidOperationException("Late Talk-close callback changed native action state.");
            if (PlayerItems(game) != closePlayerItemsBeforeLate || game.PlayerEntity.GetGoldAmount() != closePlayerGoldBeforeLate)
                throw new InvalidOperationException("Late Talk-close callback changed player inventory or gold.");
            if (NpcItems(store, actor.Resident.Id) != closeNpcItemsBeforeLate)
                throw new InvalidOperationException("Late Talk-close callback changed the actor inventory or wallet.");
            RequireActionSourcePreserved(store, actor.Resident.Id, action.Id, closeActionAfterFlush, "late Talk-close callback");
            if (ReadRequestCount(api, localDialogue) != closeRequestCountAfterFlush)
                throw new InvalidOperationException("Late Talk-close callback dispatched another model request.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_CLOSE_LATE_RESULT_PASS: request=held-after-real-completion; " +
                "canonical-ui-store=once; late-result=noop; action-source-preserved=true.");

            // Reopen the same speaker. The accepted Wait source may now be
            // AcceptedPending, InProgress, or Completed from normal game time; use
            // its newly verified saved receipt and do not create a second action.
            actor = FindActor(state.ResidentId);
            if (!actor || !actor.Person || !actor.Resident.IsAlive)
                throw new InvalidOperationException("Same fixture actor is unavailable for the save-flush wording case.");
            talk = null;
            yield return OpenTalk(host, game, actor, pendingGreeting, retryGreeting, busy, value => talk = value);
            if (talk == null || DaggerfallUI.UIManager.TopWindow != talk || !talk.IsSetup)
                throw new InvalidOperationException("Could not reopen native Talk with the same actor for save-flush case.");
            if (!Convert.ToBoolean(api.SessionOpen.GetValue(host))) BeginConversation(host);
            pendingGreeting.SetValue(host, false);
            retryGreeting.SetValue(host, Time.unscaledTime + 86400f);
            busy.SetValue(host, true);
            action = store.ReadNpcAction(state.ActionId);
            if (action == null) throw new InvalidOperationException("Original Wait source disappeared before save-flush case.");
            string saveTurn = "Before I save, tell me where things stand with that wait.";
            object saveSnapshot = CaptureNext(host, api.Capture, talk, saveTurn);
            object saveReceipt = SavedActionReply(api, store, state.ActionId);
            string saveCanonical = ReadString(saveReceipt, "NpcText");
            string saveHistoryBeforeText = ReadHistory(api.ReadHistory, talk);
            bool saveCanonicalDuplicate = DialogueHistoryWindow.IsDuplicateReply(
                DialogueHistoryWindow.LastNpcText(saveHistoryBeforeText), saveCanonical);
            int saveHistoryBefore = CountOccurrences(saveHistoryBeforeText, saveCanonical);
            int saveArchiveBefore = CountPair(store, actor.Resident.Id, saveTurn, saveCanonical);
            int savePlayerArchiveBefore = CountPlayerTurn(store, actor.Resident.Id, saveTurn);
            int saveUnavailableBefore = CountOccurrences(saveHistoryBeforeText,
                DialogueHistoryWindow.UnavailableResponseLabel + saveCanonical);
            int savePlayerHistoryBefore = CountOccurrences(saveHistoryBeforeText, "Player: " + saveTurn);
            int saveSpeechBefore = CountOccurrences(saveHistoryBeforeText, "NPC: " + saveCanonical);
            localDialogue = api.LocalDialogue.GetValue(host);
            long saveRequestsBefore = ReadRequestCount(api, localDialogue);
            string saveActionBefore = ActionSourceIdentity(store.ReadNpcAction(state.ActionId));
            api.ReleaseCompletion.SetValue(host, null);
            api.HoldCompletion.SetValue(host, true);
            bool saveSubmitted = Submit(api, host, talk, saveTurn, saveSnapshot, saveReceipt);
            bool saveDuplicate = Submit(api, host, talk, saveTurn, saveSnapshot, saveReceipt);
            if (!saveSubmitted || saveDuplicate || ReadRequestCount(api, localDialogue) != saveRequestsBefore + 1)
                throw new InvalidOperationException("Save-flush wording request was not dispatched exactly once.");
            yield return WaitForHeldCompletion(host, api, 150f, "save");
            Action saveLate = api.ReleaseCompletion.GetValue(host) as Action;
            if (saveLate == null) throw new InvalidOperationException("Held save-flush model completion was not observable.");
            string saveHistoryBeforeIndicator = ReadHistory(api.ReadHistory, talk);
            yield return WaitForPendingIndicator(host, api, talk, saveHistoryBeforeIndicator, "save");
            RequireActionSourcePreserved(store, actor.Resident.Id, state.ActionId, saveActionBefore, "held GetSaveData callback");
            WorldState saveSnapshotBeforeLate = host.GetSaveData() as WorldState;
            if (saveSnapshotBeforeLate == null) throw new InvalidOperationException("GetSaveData returned no snapshot after flushing pending wording.");
            RequireActionFallback(talk, api, store, actor.Resident.Id, saveTurn, saveCanonical,
                saveCanonicalDuplicate, saveHistoryBefore, saveArchiveBefore, savePlayerArchiveBefore,
                saveUnavailableBefore, savePlayerHistoryBefore, saveSpeechBefore, saveHistoryBeforeText,
                GetActionWordingUnavailableText(api), "GetSaveData");
            string saveHistoryBeforeIndicatorRemoval = ReadHistory(api.ReadHistory, talk);
            yield return null;
            RequirePendingIndicatorRemoved(host, api, talk, saveHistoryBeforeIndicatorRemoval, "GetSaveData");
            string saveHistoryAfterFlush = ReadHistory(api.ReadHistory, talk);
            int saveArchiveAfterFlush = CountPlayerTurn(store, actor.Resident.Id, saveTurn);
            string saveActionBeforeLate = ActionSourceIdentity(store.ReadNpcAction(state.ActionId));
            long saveRequestCountAfterFlush = ReadRequestCount(api, localDialogue);
            string saveActionStateBeforeLate = ActionSignature(store.ReadNpcAction(state.ActionId));
            string savePlayerItemsBeforeLate = PlayerItems(game);
            int savePlayerGoldBeforeLate = game.PlayerEntity.GetGoldAmount();
            string saveNpcItemsBeforeLate = NpcItems(store, actor.Resident.Id);
            if (api.PendingWording.GetValue(host) != null)
                throw new InvalidOperationException("GetSaveData left pending action wording ownership active.");
            saveLate();
            if (api.PendingWording.GetValue(host) != null)
                throw new InvalidOperationException("Late save-flush callback reclaimed cleared wording ownership.");
            if (ReadHistory(api.ReadHistory, talk) != saveHistoryAfterFlush)
                throw new InvalidOperationException("Late save-flush callback changed native conversation history.");
            if (CountPlayerTurn(store, actor.Resident.Id, saveTurn) != saveArchiveAfterFlush)
                throw new InvalidOperationException("Late save-flush callback appended another archive turn.");
            if (ActionSignature(store.ReadNpcAction(state.ActionId)) != saveActionStateBeforeLate)
                throw new InvalidOperationException("Late save-flush callback changed native action state.");
            if (PlayerItems(game) != savePlayerItemsBeforeLate || game.PlayerEntity.GetGoldAmount() != savePlayerGoldBeforeLate)
                throw new InvalidOperationException("Late save-flush callback changed player inventory or gold.");
            if (NpcItems(store, actor.Resident.Id) != saveNpcItemsBeforeLate)
                throw new InvalidOperationException("Late save-flush callback changed the actor inventory or wallet.");
            RequireActionSourcePreserved(store, actor.Resident.Id, state.ActionId, saveActionBeforeLate, "late save-flush callback");
            if (ReadRequestCount(api, localDialogue) != saveRequestCountAfterFlush)
                throw new InvalidOperationException("Late save-flush callback dispatched another model request.");
            api.HoldCompletion.SetValue(host, false);
            api.ReleaseCompletion.SetValue(host, null);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_SAVE_LATE_RESULT_PASS: source-status=" +
                ReadMember(saveReceipt, "Status") + "; canonical-ui-store=once; late-result=noop; " +
                "GetSaveData-flush=true; disk-reload=not-tested.");
        }

        private static Api RequireApi()
        {
            Type host = typeof(PeopleOfTheIliacBayMod);
            Type reply = typeof(ConversationActionReply);
            Type snapshot = typeof(ConversationContextSnapshot);
            var api = new Api
            {
                Store = NeedField(host, "store"),
                SessionOpen = NeedField(host, "conversationSessionOpen"),
                Busy = NeedField(host, "conversationBusy"),
                GreetingPending = NeedField(host, "returningGreetingPending"),
                GreetingRetry = NeedField(host, "returningGreetingRetryAt"),
                HoldCompletion = NeedField(host, "actionWordingProbeHoldCompletion"),
                ReleaseCompletion = NeedField(host, "actionWordingProbeReleaseCompletion"),
                PendingWording = NeedField(host, "pendingConversationActionWording"),
                LocalDialogue = NeedField(host, "localDialogue"),
                PendingPlayerRow = NeedField(host, "conversationPendingPlayerRow"),
                PendingPlayerText = NeedField(host, "conversationPendingPlayerText"),
                PendingRow = NeedField(host, "conversationPendingRow"),
                PendingTranscript = NeedField(host, "conversationPendingTranscript"),
                PendingLabel = NeedField(host, "conversationPendingLabel"),
                PendingTracking = NeedField(host, "conversationPendingTracking"),
                PendingStartedAt = NeedField(host, "conversationPendingStartedAt"),
                PendingOverlayWindow = NeedField(host, "conversationPendingOverlayWindow"),
                TtsBridge = NeedField(host, "breezeTtsTrial"),
                TrialTranscriptBindings = NeedField(host, "trialTranscriptBindings"),
                Submit = NeedMethod(host, "SubmitConversationActionReply", false,
                    typeof(DaggerfallTalkWindow), typeof(string), snapshot, reply),
                CompleteWording = NeedMethod(host, "CompleteActionWording", false,
                    NeedField(host, "pendingConversationActionWording").FieldType, typeof(string)),
                UnavailableText = NeedMethod(host, "ActionWordingUnavailableText", true),
                FromSavedAction = NeedMethod(reply, "TryFromSavedAction", true,
                    typeof(WorldStateStore), typeof(string), reply.MakeByRefType()),
                MatchesSavedState = NeedMethod(reply, "MatchesSavedState", false, typeof(WorldStateStore)),
                Capture = NeedMethod(host, "CaptureConversationContext", false,
                    typeof(DaggerfallTalkWindow), typeof(string), typeof(long)),
                ReadHistory = NeedMethod(host, "ReadConversationHistory", true, typeof(DaggerfallTalkWindow))
            };
            PropertyInfo count = typeof(LocalDialogueClient).GetProperty("ReplyRequestCount", Members);
            if (count == null || !count.CanRead || count.PropertyType != typeof(long))
                throw new InvalidOperationException("LocalDialogueClient.ReplyRequestCount is required to verify a fresh wording request.");
            api.RequestCount = count;
            api.CompletionCount = typeof(LocalDialogueClient).GetProperty("ReplyCompletionCount", Members);
            if (api.CompletionCount == null || !api.CompletionCount.CanRead || api.CompletionCount.PropertyType != typeof(long))
                throw new InvalidOperationException("Fresh nonempty reply completion telemetry is unavailable.");
            if (api.HoldCompletion.FieldType != typeof(bool) || api.ReleaseCompletion.FieldType != typeof(Action))
                throw new InvalidOperationException("Deterministic held-wording probe gate API has an unexpected field type.");
            if (api.PendingPlayerRow.FieldType != typeof(ListBox.ListItem) ||
                api.PendingPlayerText.FieldType != typeof(string) ||
                api.PendingRow.FieldType != typeof(ListBox.ListItem) ||
                api.PendingTranscript.FieldType != typeof(ListBox) || api.PendingLabel.FieldType != typeof(TextLabel) ||
                api.PendingTracking.FieldType != typeof(bool) || api.PendingStartedAt.FieldType != typeof(float) ||
                api.PendingOverlayWindow.FieldType != typeof(DaggerfallTalkWindow))
                throw new InvalidOperationException("Pending transcript row probe API has an unexpected field type.");
            api.PublishedTtsRequestCount = typeof(BreezeTtsTrialBridge).GetProperty("PublishedRequestCount", Members);
            if (api.PublishedTtsRequestCount == null || api.PublishedTtsRequestCount.PropertyType != typeof(int))
                throw new InvalidOperationException("TTS request telemetry is required for pending-row exclusion checks.");
            return api;
        }

        private static IEnumerator WaitForPendingIndicator(PeopleOfTheIliacBayMod host, Api api,
            DaggerfallTalkWindow talk, string historyBefore, string stage)
        {
            float deadline = Time.realtimeSinceStartup + 40f;
            ListBox.ListItem row = null;
            ListBox transcript = null;
            TextLabel label = null;
            object localDialogue = api.LocalDialogue.GetValue(host);
            if (localDialogue == null) throw new InvalidOperationException("Local dialogue client disappeared before pending-row check.");
            long requestsBefore = ReadRequestCount(api, localDialogue);
            long completionsBefore = Convert.ToInt64(api.CompletionCount.GetValue(localDialogue, null));
            int localDialogueComponentsBefore = host.GetComponents<LocalDialogueClient>().Length;
            BreezeTtsTrialBridge bridge = api.TtsBridge.GetValue(host) as BreezeTtsTrialBridge;
            int publishedTtsRequestsBefore = bridge == null ? -1 : Convert.ToInt32(api.PublishedTtsRequestCount.GetValue(bridge, null));
            ListBox.ListItem playerRow = null;
            string playerText = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                playerRow = api.PendingPlayerRow.GetValue(host) as ListBox.ListItem;
                transcript = api.PendingTranscript.GetValue(host) as ListBox;
                playerText = api.PendingPlayerText.GetValue(host) as string;
                if (playerRow != null && transcript != null && ContainsRow(transcript, playerRow)) break;
                if (api.PendingRow.GetValue(host) != null)
                    throw new InvalidOperationException("NPC pending indicator appeared before its current player-text row during " + stage + ".");
                yield return null;
            }
            // A held model completion can exceed the one-second narration delay.
            // At this inspection point both rows may already exist; exact identity
            // and transcript ordering are checked below instead of requiring the
            // NPC row to still be absent.
            if (playerRow == null || transcript == null || string.IsNullOrWhiteSpace(playerText) ||
                !ContainsRow(transcript, playerRow) || playerRow.textLabel == null ||
                playerRow.textLabel.Text != playerText ||
                playerRow.textLabel.Name != "PeopleOfTheIliacBayPendingEphemeral" ||
                playerRow.textColor != DaggerfallUI.DaggerfallQuestionTextColor)
                throw new InvalidOperationException("Submitted player row was absent or changed in the pending transcript during " + stage +
                    "; " + DescribePendingRows(host, api, transcript, playerRow, null));
            while (Time.realtimeSinceStartup < deadline)
            {
                row = api.PendingRow.GetValue(host) as ListBox.ListItem;
                transcript = api.PendingTranscript.GetValue(host) as ListBox;
                label = api.PendingLabel.GetValue(host) as TextLabel;
                bool tracking = Convert.ToBoolean(api.PendingTracking.GetValue(host));
                float startedAt = Convert.ToSingle(api.PendingStartedAt.GetValue(host));
                if (tracking && row != null && transcript != null && label != null &&
                    api.PendingOverlayWindow.GetValue(host) == talk &&
                    Time.unscaledTime - startedAt >= 1f &&
                    ContainsRow(transcript, row) && !string.IsNullOrWhiteSpace(label.Text)) break;
                yield return null;
            }
            if (row == null || transcript == null || label == null || !Convert.ToBoolean(api.PendingTracking.GetValue(host)) ||
                api.PendingOverlayWindow.GetValue(host) != talk || string.IsNullOrWhiteSpace(label.Text) ||
                !ContainsRow(transcript, row) || !ReferenceEquals(row.textLabel, label) ||
                label.Name != "PeopleOfTheIliacBayPendingEphemeral" ||
                row.textColor != PeopleOfTheIliacBayMod.ConversationPendingTextColor ||
                row.highlightedTextColor != PeopleOfTheIliacBayMod.ConversationPendingTextColor ||
                row.selectedTextColor != PeopleOfTheIliacBayMod.ConversationPendingTextColor ||
                row.highlightedSelectedTextColor != PeopleOfTheIliacBayMod.ConversationPendingTextColor ||
                !IsFixedPendingLine(label.Text, GameManager.Instance.TalkManager.NameNPC,
                    host.CurrentPendingSpeakerSex(GameManager.Instance.TalkManager)))
                throw new InvalidOperationException("Literal pending NPC row did not appear with exact text and NPC styling during " + stage +
                    "; " + DescribePendingRows(host, api, transcript, playerRow, row));
            if (!ContainsRow(transcript, playerRow) || !ReferenceEquals(api.PendingPlayerRow.GetValue(host), playerRow) ||
                !string.Equals(playerRow.textLabel.Text, playerText, StringComparison.Ordinal) ||
                !ComesBefore(transcript, playerRow, row))
                throw new InvalidOperationException("Pending NPC row did not follow its exact current player turn during " + stage +
                    "; " + DescribePendingRows(host, api, transcript, playerRow, row));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PENDING_ROWS_BOUND_PASS: stage=" + stage + "; " +
                DescribePendingRows(host, api, transcript, playerRow, row));
            string rendered = label.Text;
            api.LastObservedPendingRow = row;
            api.LastObservedPendingPlayerRow = playerRow;
            api.LastObservedPendingTranscript = transcript;
            AssertPendingRowExcluded(host, api, playerRow, localDialogue, requestsBefore, completionsBefore,
                localDialogueComponentsBefore, bridge, publishedTtsRequestsBefore, stage);
            AssertPendingRowExcluded(host, api, row, localDialogue, requestsBefore, completionsBefore,
                localDialogueComponentsBefore, bridge, publishedTtsRequestsBefore, stage);
            if (stage == "Talk-close")
                ScreenCapture.CaptureScreenshot(Path.Combine(Application.persistentDataPath,
                    "PeopleOfTheIliacBay-action-wording-pending-indicator.png"));
            yield return null;
            if (!ReferenceEquals(api.PendingPlayerRow.GetValue(host), playerRow) ||
                !ReferenceEquals(api.PendingRow.GetValue(host), row) ||
                !ReferenceEquals(api.PendingTranscript.GetValue(host), transcript) ||
                !ReferenceEquals(api.PendingLabel.GetValue(host), label) || label.Text != rendered ||
                !ContainsRow(transcript, playerRow) || !ContainsRow(transcript, row) ||
                ReadHistory(api.ReadHistory, talk) != historyBefore)
                throw new InvalidOperationException("Literal pending row changed, disappeared, or entered native conversation history during " + stage + ".");
            AssertPendingRowExcluded(host, api, row, localDialogue, requestsBefore, completionsBefore,
                localDialogueComponentsBefore, bridge, publishedTtsRequestsBefore, stage);
        }

        private static void RequirePendingIndicatorRemoved(PeopleOfTheIliacBayMod host, Api api,
            DaggerfallTalkWindow talk, string historyBefore, string stage)
        {
            if (api.PendingPlayerRow.GetValue(host) != null || api.PendingRow.GetValue(host) != null ||
                api.PendingTranscript.GetValue(host) != null ||
                api.PendingLabel.GetValue(host) != null ||
                Convert.ToBoolean(api.PendingTracking.GetValue(host)) || api.PendingOverlayWindow.GetValue(host) != null ||
                ReadHistory(api.ReadHistory, talk) != historyBefore)
                throw new InvalidOperationException("Literal pending row survived or changed native history after " + stage + ".");
            if (api.LastObservedPendingRow != null && api.LastObservedPendingTranscript != null &&
                ContainsRow(api.LastObservedPendingTranscript, api.LastObservedPendingRow))
                throw new InvalidOperationException("Literal pending row remained in its original transcript after " + stage + ".");
            if (api.LastObservedPendingPlayerRow != null && api.LastObservedPendingTranscript != null &&
                ContainsRow(api.LastObservedPendingTranscript, api.LastObservedPendingPlayerRow))
                throw new InvalidOperationException("Ephemeral player row remained in its original transcript after " + stage + ".");
            api.LastObservedPendingRow = null;
            api.LastObservedPendingPlayerRow = null;
            api.LastObservedPendingTranscript = null;
        }

        private static bool ContainsRow(ListBox transcript, ListBox.ListItem expected)
        {
            if (transcript == null || expected == null) return false;
            for (int index = 0; index < transcript.Count; index++)
                if (ReferenceEquals(transcript.GetItem(index), expected)) return true;
            return false;
        }

        private static bool ComesBefore(ListBox transcript, ListBox.ListItem first, ListBox.ListItem second)
        {
            if (transcript == null || first == null || second == null) return false;
            int firstIndex = -1, secondIndex = -1;
            for (int index = 0; index < transcript.Count; index++)
            {
                if (ReferenceEquals(transcript.GetItem(index), first)) firstIndex = index;
                if (ReferenceEquals(transcript.GetItem(index), second)) secondIndex = index;
            }
            return firstIndex >= 0 && secondIndex > firstIndex;
        }

        private static string DescribePendingRows(PeopleOfTheIliacBayMod host, Api api, ListBox transcript,
            ListBox.ListItem playerRow, ListBox.ListItem pendingRow)
        {
            string capturedText = api.PendingPlayerText.GetValue(host) as string;
            int playerIndex = FindRowIndex(transcript, playerRow);
            int pendingIndex = FindRowIndex(transcript, pendingRow);
            string rows = string.Empty;
            if (transcript != null)
            {
                int start = Math.Max(0, transcript.Count - 6);
                for (int index = start; index < transcript.Count; index++)
                {
                    ListBox.ListItem item = transcript.GetItem(index);
                    if (item == null) continue;
                    rows += " [" + index + ",id=" + RowIdentity(item) + ",name=" +
                        (item.textLabel == null ? "<null>" : item.textLabel.Name) + ",color=" + item.textColor +
                        ",text=" + (item.textLabel == null ? "<null>" : ShortText(item.textLabel.Text)) + "]";
                }
            }
            return "pendingTracking=" + Convert.ToBoolean(api.PendingTracking.GetValue(host)) +
                "; pendingPlayerText=" + ShortText(capturedText) +
                "; playerRow=" + DescribeRow(playerRow) + "; playerIndex=" + playerIndex +
                "; npcRow=" + DescribeRow(pendingRow) + "; npcIndex=" + pendingIndex +
                "; exactPlayerBeforeNpc=" + (playerIndex >= 0 && pendingIndex > playerIndex) +
                "; recentRows=" + rows;
        }

        private static string DescribeRow(ListBox.ListItem row)
        {
            return row == null ? "<null>" : "id=" + RowIdentity(row) + ",name=" +
                (row.textLabel == null ? "<null>" : row.textLabel.Name) + ",color=" + row.textColor +
                ",text=" + (row.textLabel == null ? "<null>" : ShortText(row.textLabel.Text));
        }

        private static string ShortText(string text)
        {
            if (text == null) return "<null>";
            text = text.Replace("\r", " ").Replace("\n", " ");
            return text.Length <= 120 ? "\"" + text + "\"" : "\"" + text.Substring(0, 120) + "…\"";
        }

        private static string RowIdentity(ListBox.ListItem row)
        { return row == null ? "<null>" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(row).ToString(); }

        private static int FindRowIndex(ListBox transcript, ListBox.ListItem expected)
        {
            if (transcript == null || expected == null) return -1;
            for (int index = 0; index < transcript.Count; index++)
                if (ReferenceEquals(transcript.GetItem(index), expected)) return index;
            return -1;
        }

        private static bool IsFixedPendingLine(string text, string speakerName, ResidentSex? sex)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int index = 0; index < ConversationPendingText.FixedVariantCount; index++)
                if (string.Equals(text, ConversationPendingText.SelectPendingText(speakerName, index, -1, sex), StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static void AssertPendingRowExcluded(PeopleOfTheIliacBayMod host, Api api,
            ListBox.ListItem row, object localDialogue, long requestsBefore, long completionsBefore,
            int componentsBefore, BreezeTtsTrialBridge bridge, int publishedTtsRequestsBefore, string stage)
        {
            if (ReadRequestCount(api, localDialogue) != requestsBefore ||
                Convert.ToInt64(api.CompletionCount.GetValue(localDialogue, null)) != completionsBefore ||
                host.GetComponents<LocalDialogueClient>().Length != componentsBefore)
                throw new InvalidOperationException("Pending presentation dispatched/created a model client during " + stage + ".");
            if (bridge != null && Convert.ToInt32(api.PublishedTtsRequestCount.GetValue(bridge, null)) != publishedTtsRequestsBefore)
                throw new InvalidOperationException("Pending presentation published a TTS request during " + stage + ".");

            System.Collections.IDictionary bindings = api.TrialTranscriptBindings.GetValue(host) as System.Collections.IDictionary;
            if (bindings == null) throw new InvalidOperationException("TTS replay binding map is unavailable for pending-row exclusion check.");
            foreach (System.Collections.DictionaryEntry binding in bindings)
            {
                if (binding.Value == null) continue;
                FieldInfo requestByRowField = binding.Value.GetType().GetField("RequestByRow", Members);
                System.Collections.IDictionary requestByRow = requestByRowField == null ? null :
                    requestByRowField.GetValue(binding.Value) as System.Collections.IDictionary;
                if (requestByRow != null && requestByRow.Contains(row))
                    throw new InvalidOperationException("Pending presentation row was mapped to TTS replay during " + stage + ".");
            }
        }

        private static IEnumerator WaitForHeldCompletion(PeopleOfTheIliacBayMod host, Api api, float seconds, string stage)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline && api.ReleaseCompletion.GetValue(host) == null)
                yield return null;
            if (api.ReleaseCompletion.GetValue(host) == null)
                throw new InvalidOperationException("No real LocalDialogueClient completion reached the hold gate during " + stage + " case.");
        }

        private static object CaptureNext(PeopleOfTheIliacBayMod host, MethodInfo capture,
            DaggerfallTalkWindow talk, string text)
        {
            long revision = Convert.ToInt64(NeedField(typeof(PeopleOfTheIliacBayMod), "conversationContextRequestRevision").GetValue(host)) + 1;
            try
            {
                object snapshot = capture.Invoke(host, new object[] { talk, text, revision });
                if (snapshot == null) throw new InvalidOperationException("Context capture returned no snapshot.");
                return snapshot;
            }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Could not capture fresh wording request context.", error.InnerException ?? error); }
        }

        private static object SavedActionReply(Api api, WorldStateStore store, string actionId)
        {
            object[] args = { store, actionId, null };
            try
            {
                if (!(bool)api.FromSavedAction.Invoke(null, args) || args[2] == null)
                    throw new InvalidOperationException("Saved-action receipt factory did not return the accepted Wait source.");
            }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Saved action receipt factory failed.", error.InnerException ?? error); }
            object receipt = args[2];
            if (ReadString(receipt, "SourceId") != actionId || ReadString(receipt, "PersonId") == null ||
                ReadMember(receipt, "Status").ToString() == "Unknown" ||
                !(bool)api.MatchesSavedState.Invoke(receipt, new object[] { store }))
                throw new InvalidOperationException("Fresh action wording input is not a source-bound verified receipt.");
            return receipt;
        }

        private static bool Submit(Api api, PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            string text, object snapshot, object receipt)
        {
            try { return (bool)api.Submit.Invoke(host, new object[] { talk, text, snapshot, receipt }); }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Receipt-grounded wording wrapper raised an exception.", error.InnerException ?? error); }
        }

        private static IEnumerator OpenTalk(PeopleOfTheIliacBayMod host, GameManager game, ResidentActor actor,
            FieldInfo pendingField, FieldInfo retryField, FieldInfo busyField, Action<DaggerfallTalkWindow> ready)
        {
            bool priorPending = Convert.ToBoolean(pendingField.GetValue(host));
            float priorRetry = Convert.ToSingle(retryField.GetValue(host));
            bool priorBusy = Convert.ToBoolean(busyField.GetValue(host));
            bool opened = false;
            try
            {
                pendingField.SetValue(host, false);
                retryField.SetValue(host, Time.unscaledTime + 86400f);
                busyField.SetValue(host, true);
                DaggerfallUI.Instance.PopToHUD();
                game.TalkManager.TalkToMobileNPC(actor.Person);
                float deadline = Time.realtimeSinceStartup + 25f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    DaggerfallTalkWindow talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                    if (talk != null && talk.IsSetup && game.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                        game.TalkManager.MobileNPC == actor.Person)
                    {
                        pendingField.SetValue(host, false);
                        retryField.SetValue(host, Time.unscaledTime + 86400f);
                        busyField.SetValue(host, true);
                        opened = true;
                        ready(talk);
                        yield break;
                    }
                    yield return null;
                }
                throw new InvalidOperationException("Native Talk did not bind the selected fixture resident.");
            }
            finally
            {
                if (!opened)
                {
                    pendingField.SetValue(host, priorPending);
                    retryField.SetValue(host, priorRetry);
                    busyField.SetValue(host, priorBusy);
                }
            }
        }

        private static IEnumerator RestoreBaseline(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            GameManager game = GameManager.Instance;
            if (!host || !streets || !game || !game.IsReady)
                throw new InvalidOperationException("Baseline restore requires the ready native fixture.");
            SaveLoadManager saves = game.SaveLoadManager;
            saves.EnumerateSaves();
            int slot = saves.FindSaveFolderByNames(character, BaselineSave);
            if (slot < 0) throw new InvalidOperationException("Exact baseline fixture disappeared before restoration.");
            bool loaded = false;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnLoad += onLoad;
            try
            {
                saves.Load(slot);
                DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 90f;
                while ((!loaded || !streets.Ready || Convert.ToBoolean(NeedField(typeof(PeopleOfTheIliacBayMod),
                    "conversationSessionOpen").GetValue(host))) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (!loaded || !streets.Ready || Convert.ToBoolean(NeedField(typeof(PeopleOfTheIliacBayMod),
                    "conversationSessionOpen").GetValue(host)))
                    throw new InvalidOperationException("Exact baseline fixture did not finish loading.");
            }
            finally { SaveLoadManager.OnLoad -= onLoad; }
            yield return null;
        }

        private static ResidentActor FindUnusedManagedAdult(WorldStateStore store, Vector3 playerPosition)
        {
            ResidentActor best = null;
            float bestDistance = float.MaxValue;
            foreach (ResidentActor candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!candidate || !candidate.gameObject.activeInHierarchy || !candidate.Person || candidate.Resident == null ||
                    !candidate.Resident.IsAlive || candidate.Resident.IsQuestProtected || candidate.Resident.AgeGroup != ResidentAgeGroup.Adult ||
                    candidate.IsSleeping || candidate.IsSeated || candidate.IsConversing) continue;
                if (store.ActiveNpcAction(candidate.Resident.Id) != null || store.ActiveNpcTask(candidate.Resident.Id) != null ||
                    store.ConversationTranscript(candidate.Resident.Id, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(), 256).Count != 0) continue;
                float distance = Vector3.Distance(candidate.transform.position, playerPosition);
                if (distance < bestDistance) { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        private static ResidentActor FindActor(string residentId)
        {
            foreach (ResidentActor actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if (actor && actor.Resident != null && actor.Resident.Id == residentId) return actor;
            return null;
        }

        private static NpcActionOption FindWait(List<NpcActionOption> options)
        {
            if (options == null) return null;
            foreach (NpcActionOption option in options)
                if (option != null && option.Kind == NpcActionKind.Wait && option.Target != null && option.ItemSourceStep == 0)
                    return option;
            return null;
        }

        private static NpcMemoryEntry FindSingleTurn(WorldStateStore store, string residentId, string playerText)
        {
            NpcMemoryEntry match = null;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(residentId,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 2, 256))
            {
                if (entry == null || entry.Source == "observed" || entry.PlayerText != playerText) continue;
                if (match != null) throw new InvalidOperationException("Wording request produced more than one archive row for one player turn.");
                match = entry;
            }
            return match;
        }

        private static int CountPair(WorldStateStore store, string residentId, string playerText, string npcText)
        {
            int count = 0;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(residentId,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 2, 256))
                if (entry != null && entry.Source != "observed" && entry.PlayerText == playerText && entry.NpcText == npcText) count++;
            return count;
        }

        private static void RequireSingleUiStorePair(DaggerfallTalkWindow talk, MethodInfo readHistory,
            WorldStateStore store, string residentId, string playerText, string npcText)
        {
            int ui = CountOccurrences(ReadHistory(readHistory, talk), npcText);
            int archive = CountPair(store, residentId, playerText, npcText);
            NpcMemoryEntry row = FindSingleTurn(store, residentId, playerText);
            if (ui != 1 || archive != 1 || row == null || row.NpcText != npcText)
                throw new InvalidOperationException("Rendered action wording is not exactly one same-text native Talk/archive pair.");
        }

        private static int CountPlayerTurn(WorldStateStore store, string residentId, string playerText)
        {
            int count = 0;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(residentId,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 2, 256))
                if (entry != null && entry.Source != "observed" && entry.PlayerText == playerText) count++;
            return count;
        }

        private static int CountTranscriptPair(DaggerfallTalkWindow talk, string playerText, string npcText, string requiredName)
        {
            FieldInfo field = typeof(DaggerfallTalkWindow).GetField("listboxConversation", Members);
            ListBox transcript = field == null ? null : field.GetValue(talk) as ListBox;
            if (transcript == null) throw new InvalidOperationException("Native conversation transcript is unavailable for provenance check.");
            int count = 0;
            for (int index = 0; index + 1 < transcript.Count; index++)
            {
                ListBox.ListItem player = transcript.GetItem(index);
                ListBox.ListItem reply = transcript.GetItem(index + 1);
                if (player == null || player.textLabel == null || player.textLabel.Text != playerText ||
                    reply == null || reply.textLabel == null || reply.textLabel.Text != npcText) continue;
                if (requiredName == null || reply.textLabel.Name == requiredName) count++;
            }
            return count;
        }

        private static void InvokeDuplicateCompletion(Api api, PeopleOfTheIliacBayMod host, string answer)
        {
            object pending = api.PendingWording.GetValue(host);
            if (pending == null) throw new InvalidOperationException("Duplicate control lost pending wording ownership before injection.");
            try { api.CompleteWording.Invoke(host, new[] { pending, (object)answer }); }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Could not inject exact duplicate completion.", error.InnerException ?? error); }
        }

        private static string EncodeMoodResult(string reply, int moodDelta)
        {
            return "{\"reply\":\"" + reply.Replace("\\", "\\\\").Replace("\"", "\\\"") +
                "\",\"moodDelta\":" + moodDelta.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
        }

        private static void RequireCanonicalFallback(DaggerfallTalkWindow talk, Api api, WorldStateStore store,
            string residentId, string playerText, string canonical, int uiBefore, int archiveBefore, string where)
        {
            string history = ReadHistory(api.ReadHistory, talk);
            int uiAfter = CountOccurrences(history, canonical);
            int archiveAfter = CountPair(store, residentId, playerText, canonical);
            NpcMemoryEntry row = FindSingleTurn(store, residentId, playerText);
            if (uiAfter != uiBefore + 1 || archiveAfter != archiveBefore + 1 || row == null || row.NpcText != canonical)
                throw new InvalidOperationException(where + " did not flush exactly one canonical receipt to the same UI/archive pair.");
        }

        private static void RequireActionFallback(DaggerfallTalkWindow talk, Api api, WorldStateStore store,
            string residentId, string playerText, string canonical, bool duplicate, int uiCanonicalBefore,
            int archiveCanonicalBefore, int archivePlayerBefore, int unavailableBefore, int playerHistoryBefore,
            int speechBefore, string historyBefore, string unavailableText, string where)
        {
            if (!duplicate)
            {
                RequireCanonicalFallback(talk, api, store, residentId, playerText, canonical,
                    uiCanonicalBefore, archiveCanonicalBefore, where);
                return;
            }

            NpcMemoryEntry row = FindSingleTurn(store, residentId, playerText);
            string history = ReadHistory(api.ReadHistory, talk);
            if (row == null || row.Source != "unavailable" || row.NpcText != unavailableText ||
                CountPlayerTurn(store, residentId, playerText) != archivePlayerBefore + 1 ||
                CountOccurrences(history, "Player: " + playerText) != playerHistoryBefore + 1 ||
                CountOccurrences(history, DialogueHistoryWindow.UnavailableResponseLabel + row.NpcText) !=
                    CountOccurrences(historyBefore, DialogueHistoryWindow.UnavailableResponseLabel + row.NpcText) + 1 ||
                CountOccurrences(history, "NPC: " + canonical) != speechBefore ||
                CountTranscriptPair(talk, playerText, row.NpcText, "PeopleOfTheIliacBayUnavailableReply") != 1)
                throw new InvalidOperationException(where + " repeated the prior sentence as NPC speech instead of one source-marked unavailable row.");
        }

        private static string GetActionWordingUnavailableText(Api api)
        {
            try { return api.UnavailableText.Invoke(null, null) as string; }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Could not read the production unavailable-row text.", error.InnerException ?? error); }
        }

        private static void RequireActionSourcePreserved(WorldStateStore store, string residentId, string actionId,
            string before, string stage)
        {
            // Phase, revision, deadline progress, and position can change through
            // normal NPC simulation while a model request is pending. Preserve
            // the committed source identity without freezing that lifecycle.
            NpcActionJob current = store.ReadNpcAction(actionId);
            NpcActionJob latest = store.LatestNpcAction(residentId);
            if (current == null || latest == null || latest.Id != actionId ||
                current.ResidentId != residentId || ActionSourceIdentity(current) != before)
                throw new InvalidOperationException("Wording replaced or removed the accepted native action source during " + stage + ".");
        }

        private static string ActionSourceIdentity(NpcActionJob job)
        {
            if (job == null) return "<missing>";
            return job.Id + "|" + job.ProposalId + "|" + job.ResidentId + "|" + job.Kind;
        }

        private static string ActionSignature(NpcActionJob job)
        {
            if (job == null) return "<missing>";
            return job.Id + "|" + job.ResidentId + "|" + job.Kind + "|" + job.Phase + "|" + job.Revision + "|" +
                job.CompletedEffectId + "|" + job.FailureReason + "|" + job.DeadlineAt.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string PlayerItems(GameManager game)
        {
            var values = new List<string>();
            var items = game.PlayerEntity.Items;
            if (items != null) for (int index = 0; index < items.Count; index++)
            {
                var item = items.GetItem(index);
                if (item != null) values.Add(item.UID + ":" + item.stackCount);
            }
            values.Sort(StringComparer.Ordinal);
            return string.Join("|", values.ToArray());
        }

        private static string NpcItems(WorldStateStore store, string residentId)
        {
            NpcInventoryState inventory = store.ReadNpcInventory(residentId);
            if (inventory == null) return "<none>";
            var values = new List<string>();
            foreach (NpcInventoryItem item in inventory.Items) values.Add(item.Uid + ":" + item.NativePayload);
            values.Sort(StringComparer.Ordinal);
            return inventory.WalletGold + "|" + string.Join("|", values.ToArray());
        }

        private static object BeginConversation(PeopleOfTheIliacBayMod host)
        {
            MethodInfo begin = NeedMethod(typeof(PeopleOfTheIliacBayMod), "BeginGroundedConversation", false);
            try { return begin.Invoke(host, null); }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Could not begin native grounded conversation.", error.InnerException ?? error); }
        }

        private static string ReadHistory(MethodInfo method, DaggerfallTalkWindow talk)
        {
            try { return Convert.ToString(method.Invoke(null, new object[] { talk })) ?? string.Empty; }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Could not read native Talk history.", error.InnerException ?? error); }
        }

        private static long ReadRequestCount(Api api, object client)
        { return Convert.ToInt64(api.RequestCount.GetValue(client, null)); }

        private static int CountOccurrences(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value)) return 0;
            int count = 0, offset = 0;
            while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            { count++; offset += value.Length; }
            return count;
        }

        private static void RequireNoPriorActionOrTask(WorldStateStore store, string residentId)
        {
            if (store.ActiveNpcAction(residentId) != null || store.ActiveNpcTask(residentId) != null)
                throw new InvalidOperationException("Selected resident already owns active native work.");
        }

        private static void RequireOwnedFixture(string character)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) < 0 || character != OwnedCharacter ||
                GameManager.Instance == null || GameManager.Instance.PlayerEntity == null ||
                GameManager.Instance.PlayerEntity.Name != OwnedCharacter)
                throw new InvalidOperationException("Action wording probe requires its explicit opt-in flag and exact owned fixture character.");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-action-wording") >= 0)
                throw new InvalidOperationException("Action wording settings regression requires no -peopleoftheiliacbay-action-wording feature flag.");
        }

        private static FieldInfo NeedField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, Members);
            if (field == null) throw new InvalidOperationException("Action wording probe API field missing: " + type.FullName + "." + name);
            return field;
        }

        private static MethodInfo NeedMethod(Type type, string name, bool isStatic, params Type[] parameterTypes)
        {
            MethodInfo method = type.GetMethod(name, Members, null, parameterTypes, null);
            if (method == null || method.IsStatic != isStatic)
                throw new InvalidOperationException("Action wording probe API method missing: " + type.FullName + "." + name);
            return method;
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) throw new InvalidOperationException("Cannot read member from null action receipt.");
            Type type = target.GetType();
            FieldInfo field = type.GetField(name, Members);
            if (field != null) return field.GetValue(target);
            PropertyInfo property = type.GetProperty(name, Members);
            if (property != null) return property.GetValue(target, null);
            throw new InvalidOperationException("Action wording probe receipt member missing: " + name);
        }

        private static string ReadString(object target, string name)
        { return Convert.ToString(ReadMember(target, name)); }

        private static string EscapeLog(string value)
        { return (value ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "'" ); }

        private static void Dispose(IEnumerator enumerator, ref Exception failure)
        {
            IDisposable disposable = enumerator as IDisposable;
            if (disposable == null) return;
            try { disposable.Dispose(); }
            catch (Exception error) { if (failure == null) failure = error; }
        }
    }
}
