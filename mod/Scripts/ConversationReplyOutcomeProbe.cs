using System;
using System.Collections;
using System.Collections.Generic;
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
    // Opt-in native receipt seam probe. It exercises backend validation and the
    // direct reply helper only. It does not exercise semantic classification or
    // ordinary social generation; separately configured model prewarm is external.
    internal static class ConversationReplyOutcomeProbe
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;
        private const string BaselineSave = "People - Gothway Garden";
        private const string FixtureCharacter = "Hakon Ironhand";
        private const string RunFlag = "-peopleoftheiliacbay-conversation-reply-outcome-probe";

        private sealed class Api
        {
            internal Type ReplyType;
            internal MethodInfo FromSavedTask;
            internal MethodInfo Rejected;
            internal MethodInfo Unverified;
            internal MethodInfo MatchesSavedState;
            internal MethodInfo SubmitReply;
            internal MethodInfo SavedReply;
            internal PropertyInfo ReplyRequestCount;
        }

        private sealed class RunState
        {
            internal string ResidentId;
            internal string AcceptedTaskId;
            internal string AcceptedActionId;
            internal int CompletedSteps;
            internal int TotalSteps;
            internal string RejectedReply;
            internal string AcceptedReply;
            internal string UnverifiedReply;
            internal float PriorGreetingRetryAt;
            internal bool PriorGreetingPending;
            internal Vector3 PriorPlayerPosition;
            internal bool CapturedOriginalState;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string expectedCharacter)
        {
            RequireOwnedFixture(expectedCharacter);
            // Validate future helper APIs before opening Talk or touching state.
            Api api = RequireApi();
            Exception testFailure = null;
            RunState state = new RunState();
            if (host && GameManager.Instance && GameManager.Instance.PlayerMotor)
            {
                state.PriorGreetingRetryAt = Convert.ToSingle(RequireField(typeof(PeopleOfTheIliacBayMod),
                    "returningGreetingRetryAt", false).GetValue(host));
                state.PriorGreetingPending = Convert.ToBoolean(RequireField(typeof(PeopleOfTheIliacBayMod),
                    "returningGreetingPending", false).GetValue(host));
                state.PriorPlayerPosition = GameManager.Instance.PlayerMotor.transform.position;
                state.CapturedOriginalState = true;
            }
            IEnumerator test = DriveFlattened(RunCases(host, streets, expectedCharacter, api, state));
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try
                    {
                        more = test.MoveNext();
                        if (more) current = test.Current;
                    }
                    catch (Exception error) { testFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally { Dispose(test, ref testFailure); }

            Exception restoreFailure = null;
            IEnumerator restore = DriveFlattened(LoadBaseline(host, streets, expectedCharacter));
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try
                    {
                        more = restore.MoveNext();
                        if (more) current = restore.Current;
                    }
                    catch (Exception error) { restoreFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally { Dispose(restore, ref restoreFailure); }

            if (restoreFailure != null)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_REPLY_OUTCOME_BASELINE_RESTORE_FAIL: " + restoreFailure);
                throw new InvalidOperationException("Reply-outcome probe could not restore exact baseline fixture.", restoreFailure);
            }
            if (state.CapturedOriginalState && host && GameManager.Instance && GameManager.Instance.PlayerMotor)
            {
                RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending", false).SetValue(host, state.PriorGreetingPending);
                RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingRetryAt", false).SetValue(host, state.PriorGreetingRetryAt);
                if (Vector3.Distance(GameManager.Instance.PlayerMotor.transform.position, state.PriorPlayerPosition) > 0.05f)
                    throw new InvalidOperationException("Exact baseline load did not restore the probe's original player position.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REPLY_OUTCOME_BASELINE_RESTORED: character=" + expectedCharacter +
                "; save=" + BaselineSave + ".");
            if (testFailure != null)
                throw new InvalidOperationException("Reply-outcome probe failed after baseline restore.", testFailure);

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REPLY_OUTCOME_PASS: rejected-native-validation=once; accepted-task=" +
                state.AcceptedTaskId + "; accepted-action=" + state.AcceptedActionId +
                "; source-bound-task-and-action-replies=pass; wrong-proposal-and-unrelated-turn=blocked; " +
                "accepted-ack=once; rejected-ack=once; duplicate-helper-callback=noop; " +
                "stale-saved-receipt=unverified-once; reply-request-dispatches=0; task-held-during-talk=true; same-ui-store=true; " +
                "semantic-classifier=not-tested; ordinary-social-llm=not-tested.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, Api api, RunState state)
        {
            if (!host || !streets || !GameManager.Instance || !GameManager.Instance.IsReady ||
                GameManager.Instance.PlayerMotor == null || !streets.Ready)
                throw new InvalidOperationException("Reply-outcome probe requires ready owned native fixture.");

            GameManager game = GameManager.Instance;
            SaveLoadManager saves = game.SaveLoadManager;
            saves.EnumerateSaves();
            if (saves.FindSaveFolderByNames(character, BaselineSave) < 0)
                throw new InvalidOperationException("Exact baseline fixture save is missing; refusing reply-outcome probe.");

            FieldInfo storeField = RequireField(typeof(PeopleOfTheIliacBayMod), "store", false);
            FieldInfo sessionField = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSessionOpen", false);
            FieldInfo busyField = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationBusy", false);
            FieldInfo pendingGreetingField = RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending", false);
            FieldInfo retryGreetingField = RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingRetryAt", false);
            FieldInfo localDialogueField = RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogue", false);
            MethodInfo beginConversation = RequireMethod(typeof(PeopleOfTheIliacBayMod), "BeginGroundedConversation", false);
            MethodInfo capture = RequireMethod(typeof(PeopleOfTheIliacBayMod), "CaptureConversationContext", false,
                typeof(DaggerfallTalkWindow), typeof(string), typeof(long));
            MethodInfo historyMethod = RequireMethod(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", true,
                typeof(DaggerfallTalkWindow));
            MethodInfo acceptTask = RequireMethod(typeof(StreetPopulation), "TryAcceptConversationTask", false,
                typeof(string), typeof(string), typeof(List<NpcActionDecision>),
                typeof(NpcConversationCommandTurn), typeof(string).MakeByRefType());
            MethodInfo acceptAction = RequireMethod(typeof(StreetPopulation), "TryAcceptConversationAction", false,
                typeof(string), typeof(string), typeof(NpcActionDecision),
                typeof(NpcConversationCommandTurn), typeof(string).MakeByRefType());

            WorldStateStore store = storeField.GetValue(host) as WorldStateStore;
            if (store == null) throw new InvalidOperationException("Reply-outcome probe cannot read active fixture store.");
            ResidentActor actor = null;
            Vector3 playerStart = game.PlayerMotor.transform.position;
            float actorDeadline = Time.realtimeSinceStartup + 45f;
            while (Time.realtimeSinceStartup < actorDeadline)
            {
                actor = FindUnusedManagedAdult(store, playerStart);
                if (actor) break;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            if (!actor) throw new InvalidOperationException("No unused nearby managed adult spawned within 45 seconds.");
            if (Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position) > 8f)
            {
                Vector3 observer;
                if (!TrySurveyedOutdoorObserver(streets, actor.transform.position - Vector3.up, out observer))
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
            yield return OpenTalk(host, game, actor, pendingGreetingField, retryGreetingField, busyField,
                value => talk = value);
            if (talk == null || DaggerfallUI.UIManager.TopWindow != talk || !talk.IsSetup)
                throw new InvalidOperationException("Native Talk did not open on selected fixture resident.");
            if (!(bool)sessionField.GetValue(host)) beginConversation.Invoke(host, null);
            pendingGreetingField.SetValue(host, false);
            retryGreetingField.SetValue(host, Time.unscaledTime + 86400f);
            // BeginGroundedConversation intentionally clears busy. Reassert the
            // greeting gate, then wait for the real OnWindowChange callback to
            // mark this resident as conversing; never set the actor flag here.
            busyField.SetValue(host, true);
            float talkStateDeadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < talkStateDeadline &&
                (!actor || !actor.IsConversing || !(bool)sessionField.GetValue(host) ||
                 DaggerfallUI.UIManager.TopWindow != talk ||
                 game.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile ||
                 game.TalkManager.MobileNPC != actor.Person))
                yield return null;
            if (!actor || !actor.IsConversing || !(bool)sessionField.GetValue(host) ||
                DaggerfallUI.UIManager.TopWindow != talk ||
                game.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile || game.TalkManager.MobileNPC != actor.Person)
                throw new InvalidOperationException("Native Talk actor/session did not settle before probe: " +
                    DescribePrecondition(host, store, actor, talk, game));
            RequireQuietBeforeProbe(host, store, actor, talk, true);

            object localDialogue = localDialogueField.GetValue(host);
            if (localDialogue == null) throw new InvalidOperationException("Local dialogue client is unavailable.");
            int dispatchVersionBefore = ReadDispatchVersion(localDialogue);
            long replyCountBefore = ReadReplyRequestCount(api, localDialogue);

            // Native validator rejection, not a parser-only NONE. Duration is
            // intentionally below the backend's minimum (10 seconds).
            const string rejectedText = "Wait here for one second.";
            object rejectedSnapshot = CaptureNext(host, capture, talk, rejectedText);
            busyField.SetValue(host, false);
            NpcActionOption wait = FindCandidate(streets.ConversationActionCandidates(state.ResidentId), NpcActionKind.Wait);
            if (wait == null) throw new InvalidOperationException("No native Wait capability exists for rejection test.");
            string rejectReason = null;
            object[] rejectedArgs = { state.ResidentId, "reply-outcome-reject:" + Guid.NewGuid().ToString("N"),
                new NpcActionDecision { Option = wait, DurationSeconds = 1 }, SnapshotTurn(host, rejectedSnapshot), null };
            bool acceptedInvalid = (bool)acceptAction.Invoke(streets, rejectedArgs);
            rejectReason = Convert.ToString(rejectedArgs[4]);
            if (acceptedInvalid) throw new InvalidOperationException("Native validator accepted under-minimum Wait duration.");
            if (store.ActiveNpcAction(state.ResidentId) != null || store.ActiveNpcTask(state.ResidentId) != null)
                throw new InvalidOperationException("Rejected native action created active action/task state.");
            object rejectedReply = CreateRejected(api, NpcActionKind.Wait);
            RequireReply(rejectedReply, api, "Rejected", "None", NpcActionKind.Wait, null, store);
            state.RejectedReply = ReadString(rejectedReply, "NpcText");
            if (string.IsNullOrWhiteSpace(state.RejectedReply) || ContainsCompletionClaim(state.RejectedReply))
                throw new InvalidOperationException("Rejected receipt has missing or overclaiming native acknowledgment.");
            int rejectTranscriptBefore = CountOccurrences(ReadHistory(historyMethod, talk), state.RejectedReply);
            int rejectMemoryBefore = CountMemory(store, state.ResidentId, rejectedText, state.RejectedReply);
            long rejectRequestBefore = ReadReplyRequestCount(api, localDialogue);
            if (InvokeSubmit(api, host, talk, rejectedText + " altered", rejectedSnapshot, rejectedReply) ||
                CountOccurrences(ReadHistory(historyMethod, talk), state.RejectedReply) != rejectTranscriptBefore ||
                CountMemory(store, state.ResidentId, rejectedText, state.RejectedReply) != rejectMemoryBefore)
                throw new InvalidOperationException("Authoritative reply helper accepted player text different from the captured request.");
            bool rejectedHandled = InvokeSubmit(api, host, talk, rejectedText, rejectedSnapshot, rejectedReply);
            bool rejectedDuplicateHandled = InvokeSubmit(api, host, talk, rejectedText, rejectedSnapshot, rejectedReply);
            if (!rejectedHandled || rejectedDuplicateHandled)
                throw new InvalidOperationException("Rejected receipt was not handled exactly once.");
            yield return WaitForExactReply(host, store, talk, historyMethod, state.ResidentId,
                rejectedText, state.RejectedReply, rejectTranscriptBefore, rejectMemoryBefore, "Rejected receipt");
            if (ReadReplyRequestCount(api, localDialogue) != rejectRequestBefore)
                throw new InvalidOperationException("Rejected native acknowledgment invoked a reply Request.");

            // A real native task commit: derive Wait and Follow capabilities
            // from the loaded actor and pass them through existing backend checks.
            const string acceptedText = "Wait here for ten seconds, then follow me.";
            object acceptedSnapshot = CaptureNext(host, capture, talk, acceptedText);
            int staleTranscriptBefore = CountOccurrences(ReadHistory(historyMethod, talk), state.RejectedReply);
            int staleMemoryBefore = CountMemory(store, state.ResidentId, rejectedText, state.RejectedReply);
            if (InvokeSubmit(api, host, talk, rejectedText, rejectedSnapshot, rejectedReply) ||
                CountOccurrences(ReadHistory(historyMethod, talk), state.RejectedReply) != staleTranscriptBefore ||
                CountMemory(store, state.ResidentId, rejectedText, state.RejectedReply) != staleMemoryBefore)
                throw new InvalidOperationException("Late callback for an older captured request appended after a newer request capture.");
            List<NpcActionOption> candidates = streets.ConversationActionCandidates(state.ResidentId);
            NpcActionOption taskWait = FindCandidate(candidates, NpcActionKind.Wait);
            NpcActionOption follow = FindCandidate(candidates, NpcActionKind.Follow);
            if (taskWait == null || follow == null)
                throw new InvalidOperationException("Native task test requires current Wait and Follow capabilities.");
            var decisions = new List<NpcActionDecision>
            {
                new NpcActionDecision { Option = taskWait, DurationSeconds = 10 },
                new NpcActionDecision { Option = follow, DurationSeconds = 300 }
            };
            string taskReason = null;
            string proposalId = "reply-outcome:" + Guid.NewGuid().ToString("N");
            object[] taskArgs = { state.ResidentId, proposalId, decisions, SnapshotTurn(host, acceptedSnapshot), null };
            bool taskAccepted = (bool)acceptTask.Invoke(streets, taskArgs);
            taskReason = Convert.ToString(taskArgs[4]);
            if (!taskAccepted)
                throw new InvalidOperationException("Native backend rejected controlled Wait/Follow task: " + taskReason);
            NpcTaskPlan task = store.ActiveNpcTask(state.ResidentId);
            if (task == null || task.Steps == null || task.Steps.Count != 2 || task.CurrentStepIndex != 0 ||
                task.Phase == NpcTaskPhase.Completed || task.Phase == NpcTaskPhase.Failed || task.Phase == NpcTaskPhase.Cancelled)
                throw new InvalidOperationException("Backend did not retain accepted pending task at step zero.");
            NpcActionJob first = store.ReadNpcAction(task.Id + ":step:0");
            if (first == null || first.Phase != NpcActionPhase.Accepted)
                throw new InvalidOperationException("Accepted task first action is not still pending while Talk is open.");

            object acceptedReply = SavedReply(api, host, acceptedSnapshot, proposalId, true, true, null);
            RequireAcceptedTaskReply(acceptedReply, api, task.Id, store);
            RequireReply(SavedReply(api, host, acceptedSnapshot, proposalId + ":wrong", true, false, null),
                api, "Rejected", "None", null, null, store);
            RequireReply(SavedReply(api, host, rejectedSnapshot, proposalId, true, false, null),
                api, "Rejected", "None", null, null, store);
            state.AcceptedTaskId = task.Id;
            state.CompletedSteps = ReadInt(acceptedReply, "CompletedSteps");
            state.TotalSteps = ReadInt(acceptedReply, "TotalSteps");
            state.AcceptedReply = ReadString(acceptedReply, "NpcText");
            if (state.CompletedSteps != 0 || state.TotalSteps != 2 ||
                string.IsNullOrWhiteSpace(state.AcceptedReply) || ContainsCompletionClaim(state.AcceptedReply))
                throw new InvalidOperationException("Accepted task receipt does not describe pending intent truthfully.");

            int acceptedTranscriptBefore = CountOccurrences(ReadHistory(historyMethod, talk), state.AcceptedReply);
            int acceptedMemoryBefore = CountMemory(store, state.ResidentId, acceptedText, state.AcceptedReply);
            long acceptedRequestBefore = ReadReplyRequestCount(api, localDialogue);
            bool acceptedHandled = InvokeSubmit(api, host, talk, acceptedText, acceptedSnapshot, acceptedReply);
            bool acceptedDuplicateHandled = InvokeSubmit(api, host, talk, acceptedText, acceptedSnapshot, acceptedReply);
            if (!acceptedHandled || acceptedDuplicateHandled)
                throw new InvalidOperationException("Accepted task receipt was not handled exactly once.");
            yield return WaitForExactReply(host, store, talk, historyMethod, state.ResidentId,
                acceptedText, state.AcceptedReply, acceptedTranscriptBefore, acceptedMemoryBefore,
                "Accepted task receipt");
            if (ReadReplyRequestCount(api, localDialogue) != acceptedRequestBefore ||
                ReadDispatchVersion(localDialogue) != dispatchVersionBefore ||
                ReadReplyRequestCount(api, localDialogue) != replyCountBefore)
                throw new InvalidOperationException("Authoritative native acknowledgments dispatched an extra social reply request.");

            Vector3 actorPosition = actor.transform.position;
            Vector3 playerPosition = game.PlayerMotor.transform.position;
            int playerGold = game.PlayerEntity.GetGoldAmount();
            int playerInventoryCount = game.PlayerEntity.Items.Count;
            long taskRevision = task.Revision;
            float holdUntil = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < holdUntil) yield return null;
            task = store.ReadNpcTask(state.AcceptedTaskId);
            first = store.ReadNpcAction(state.AcceptedTaskId + ":step:0");
            if (DaggerfallUI.UIManager.TopWindow != talk || !DaggerfallUI.UIManager.ContainsWindow(talk) ||
                !actor || !actor.IsConversing || task == null || task.CurrentStepIndex != 0 ||
                task.Phase == NpcTaskPhase.Completed || task.Phase == NpcTaskPhase.Failed || task.Phase == NpcTaskPhase.Cancelled ||
                first == null || first.Phase != NpcActionPhase.Accepted ||
                actor.transform.position != actorPosition || game.PlayerMotor.transform.position != playerPosition ||
                game.PlayerEntity.GetGoldAmount() != playerGold || game.PlayerEntity.Items.Count != playerInventoryCount ||
                task.Revision != taskRevision)
                throw new InvalidOperationException("Accepted task caused effect, advanced, changed UI, or left same Talk session before close.");

            // Cancel only through the real native stop API, then deliberately
            // deliver the previously captured Accepted receipt. The helper must
            // downgrade that stale receipt to Unknown and append once, without
            // mutating the saved Cancelled task or dispatching another request.
            string stopReason = null;
            if (!streets.TryStopConversationAction(state.ResidentId, out stopReason))
                throw new InvalidOperationException("Native task cancellation boundary could not stop the still-current accepted task: " + stopReason);
            task = store.ReadNpcTask(state.AcceptedTaskId);
            first = store.ReadNpcAction(state.AcceptedTaskId + ":step:0");
            if (task == null || task.Phase != NpcTaskPhase.Cancelled || first == null || first.Phase != NpcActionPhase.Cancelled)
                throw new InvalidOperationException("Native stop API did not persist task and first action as Cancelled.");
            object cancelledReply = CreateSavedTaskReply(api, store, state.AcceptedTaskId);
            RequireReply(cancelledReply, api, "Cancelled", "Task", null, state.AcceptedTaskId, store);
            if (cancelledReply == null || !(bool)api.MatchesSavedState.Invoke(cancelledReply, new object[] { store }))
                throw new InvalidOperationException("Cancelled task receipt is not current in the native store.");

            const string cancelTurnText = "Stop that task now.";
            object cancelSnapshot = CaptureNext(host, capture, talk, cancelTurnText);
            object unverified = api.Unverified.Invoke(null, null);
            RequireReply(unverified, api, "Unknown", "None", null, null, store);
            state.UnverifiedReply = ReadString(unverified, "NpcText");
            if (string.IsNullOrWhiteSpace(state.UnverifiedReply) || ContainsCompletionClaim(state.UnverifiedReply))
                throw new InvalidOperationException("Unverified native reply has missing text or overclaims completion.");
            int unknownTranscriptBefore = CountOccurrences(ReadHistory(historyMethod, talk), state.UnverifiedReply);
            int unknownMemoryBefore = CountMemory(store, state.ResidentId, cancelTurnText, state.UnverifiedReply);
            long unknownRequestBefore = ReadReplyRequestCount(api, localDialogue);
            busyField.SetValue(host, true);
            bool staleHandled = InvokeSubmit(api, host, talk, cancelTurnText, cancelSnapshot, acceptedReply);
            bool staleDuplicateHandled = InvokeSubmit(api, host, talk, cancelTurnText, cancelSnapshot, acceptedReply);
            if (!staleHandled || staleDuplicateHandled)
                throw new InvalidOperationException("Stale accepted receipt was not downgraded exactly once.");
            yield return WaitForExactReply(host, store, talk, historyMethod, state.ResidentId,
                cancelTurnText, state.UnverifiedReply, unknownTranscriptBefore, unknownMemoryBefore,
                "Stale saved receipt");
            if ((bool)busyField.GetValue(host))
                throw new InvalidOperationException("Stale accepted receipt did not clear busy state after presentation.");
            task = store.ReadNpcTask(state.AcceptedTaskId);
            first = store.ReadNpcAction(state.AcceptedTaskId + ":step:0");
            if (task == null || task.Phase != NpcTaskPhase.Cancelled || first == null || first.Phase != NpcActionPhase.Cancelled ||
                !(bool)api.MatchesSavedState.Invoke(cancelledReply, new object[] { store }))
                throw new InvalidOperationException("Unverified acknowledgment changed the saved cancellation receipt.");
            if (ReadReplyRequestCount(api, localDialogue) != unknownRequestBefore ||
                ReadDispatchVersion(localDialogue) != dispatchVersionBefore)
                throw new InvalidOperationException("Stale-receipt acknowledgment dispatched an extra social reply request.");
            VerifyUnavailableHistoryProvenance(host, store, talk, historyMethod, state.ResidentId);
            const string actionText = "Wait here for ten seconds.";
            object actionSnapshot = CaptureNext(host, capture, talk, actionText);
            var actionOption = FindCandidate(streets.ConversationActionCandidates(state.ResidentId), NpcActionKind.Wait);
            if (actionOption == null)
                throw new InvalidOperationException("No native Wait capability exists for accepted action reply test.");
            string actionProposal = "reply-outcome-action:" + Guid.NewGuid().ToString("N");
            object[] actionArgs = { state.ResidentId, actionProposal,
                new NpcActionDecision { Option = actionOption, DurationSeconds = 10 },
                SnapshotTurn(host, actionSnapshot), null };
            if (!(bool)acceptAction.Invoke(streets, actionArgs))
                throw new InvalidOperationException("Native backend rejected controlled Wait action: " + Convert.ToString(actionArgs[4]));
            NpcActionJob savedAction = store.ActiveNpcAction(state.ResidentId);
            if (savedAction == null || savedAction.ProposalId != actionProposal || savedAction.Kind != NpcActionKind.Wait ||
                savedAction.Phase != NpcActionPhase.Accepted)
                throw new InvalidOperationException("Backend did not retain source-bound accepted Wait action.");
            state.AcceptedActionId = savedAction.Id;
            object actionReply = SavedReply(api, host, actionSnapshot, actionProposal, false, true, NpcActionKind.Wait);
            RequireReply(actionReply, api, "AcceptedPending", "Action", NpcActionKind.Wait, savedAction.Id, store);
            RequireReply(SavedReply(api, host, actionSnapshot, actionProposal + ":wrong", false, false,
                NpcActionKind.Wait), api, "Rejected", "None", NpcActionKind.Wait, null, store);
            RequireReply(SavedReply(api, host, cancelSnapshot, actionProposal, false, false,
                NpcActionKind.Wait), api, "Rejected", "None", NpcActionKind.Wait, null, store);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REPLY_OUTCOME_STALE_SAVED_RECEIPT_PASS: old=AcceptedPending; saved=Cancelled; " +
                "ack=Unknown-once; busy-cleared=true; no-request=true; no-state-mutation=true.");
        }

        private static Api RequireApi()
        {
            Type replyType = typeof(WorldStateStore).Assembly.GetType("PeopleOfTheIliacBay.Core.ConversationActionReply", false);
            if (replyType == null)
                throw new InvalidOperationException("ConversationActionReply API missing; refusing reply-outcome probe pass.");
            MethodInfo fromTask = RequireMethod(replyType, "TryFromSavedTask", true,
                typeof(WorldStateStore), typeof(string), replyType.MakeByRefType());
            MethodInfo rejected = RequireMethod(replyType, "Rejected", true, typeof(NpcActionKind?));
            MethodInfo unverified = RequireMethod(replyType, "Unverified", true);
            MethodInfo matches = RequireMethod(replyType, "MatchesSavedState", false, typeof(WorldStateStore));
            MethodInfo submit = RequireMethod(typeof(PeopleOfTheIliacBayMod), "SubmitAuthoritativeActionReply", false,
                typeof(DaggerfallTalkWindow), typeof(string), typeof(ConversationContextSnapshot), replyType);
            MethodInfo saved = RequireMethod(typeof(PeopleOfTheIliacBayMod), "SavedConversationActionReply", false,
                typeof(ConversationContextSnapshot), typeof(string), typeof(bool), typeof(bool), typeof(NpcActionKind?));
            PropertyInfo count = typeof(LocalDialogueClient).GetProperty("ReplyRequestCount", Members);
            if (count == null || count.PropertyType != typeof(long) || !count.CanRead)
                throw new InvalidOperationException("LocalDialogueClient.ReplyRequestCount missing; refusing unmeasured no-second-request claim.");
            return new Api { ReplyType = replyType, FromSavedTask = fromTask,
                Rejected = rejected, Unverified = unverified, MatchesSavedState = matches,
                SubmitReply = submit, SavedReply = saved, ReplyRequestCount = count };
        }

        private static object CreateRejected(Api api, NpcActionKind kind)
        {
            return api.Rejected.Invoke(null, new object[] { new NpcActionKind?(kind) });
        }

        private static object CreateSavedTaskReply(Api api, WorldStateStore store, string taskId)
        {
            object[] args = { store, taskId, null };
            if (!(bool)api.FromSavedTask.Invoke(null, args) || args[2] == null)
                throw new InvalidOperationException("Saved-task receipt factory refused actual backend-accepted task.");
            return args[2];
        }

        private static object SavedReply(Api api, PeopleOfTheIliacBayMod host, object snapshot,
            string proposalId, bool task, bool committed, NpcActionKind? kind)
        {
            try { return api.SavedReply.Invoke(host, new object[] { snapshot, proposalId, task, committed, kind }); }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Source-bound saved reply helper raised an exception.", error.InnerException ?? error); }
        }

        private static void RequireReply(object reply, Api api, string status, string source,
            NpcActionKind? actionKind, string sourceId, WorldStateStore store)
        {
            if (reply == null || ReadString(reply, "Status") != status || ReadString(reply, "Source") != source)
                throw new InvalidOperationException("Typed outcome receipt has unexpected status or source.");
            object actualKind = ReadMember(reply, "ActionKind");
            if (actionKind.HasValue && (actualKind == null || (NpcActionKind)actualKind != actionKind.Value) ||
                !actionKind.HasValue && actualKind != null)
                throw new InvalidOperationException("Typed outcome receipt has unexpected action kind.");
            if (sourceId != null && ReadString(reply, "SourceId") != sourceId)
                throw new InvalidOperationException("Typed outcome receipt is not bound to expected saved ID.");
            if (source == "None" && !string.IsNullOrEmpty(ReadString(reply, "SourceId")))
                throw new InvalidOperationException("No-change/rejected receipt unexpectedly claims saved source ID.");
            if (source != "None" && !(bool)api.MatchesSavedState.Invoke(reply, new object[] { store }))
                throw new InvalidOperationException("Typed receipt no longer matches persisted native state.");
        }

        private static void RequireAcceptedTaskReply(object reply, Api api, string taskId, WorldStateStore store)
        {
            string status = ReadString(reply, "Status");
            if (status != "AcceptedPending" && status != "InProgress")
                throw new InvalidOperationException("Backend-accepted task receipt has unexpected status: " + status);
            RequireReply(reply, api, status, "Task", null, taskId, store);
        }

        private static bool InvokeSubmit(Api api, PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            string playerText, object snapshot, object reply)
        {
            try { return (bool)api.SubmitReply.Invoke(host, new object[] { talk, playerText, snapshot, reply }); }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Authoritative reply helper raised an exception.", error.InnerException ?? error); }
        }

        private static IEnumerator WaitForExactReply(PeopleOfTheIliacBayMod host, WorldStateStore store,
            DaggerfallTalkWindow talk, MethodInfo historyMethod, string residentId, string playerText,
            string npcText, int transcriptBefore, int memoryBefore, string caseName)
        {
            // Accepted speech is presented at mixer onset (or a terminal TTS fallback).
            // The helper's true return means the exact request was handled, not that
            // the canonical Talk row and saved memory have already been appended.
            float deadline = Time.realtimeSinceStartup + 45f;
            int transcript = transcriptBefore, memory = memoryBefore;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (!host || talk == null || !talk.IsSetup || DaggerfallUI.UIManager.TopWindow != talk)
                    throw new InvalidOperationException(caseName + " lost the same native Talk before presentation.");
                transcript = CountOccurrences(ReadHistory(historyMethod, talk), npcText);
                memory = CountMemory(store, residentId, playerText, npcText);
                if (transcript == transcriptBefore + 1 && memory == memoryBefore + 1) yield break;
                if (transcript > transcriptBefore + 1 || memory > memoryBefore + 1)
                    throw new InvalidOperationException(caseName + " appended more than once to Talk or persistent store.");
                yield return null;
            }
            throw new InvalidOperationException(caseName + " did not append once to same native Talk and persistent store " +
                "within 45 seconds (Talk=" + (transcript - transcriptBefore) + ", saved=" +
                (memory - memoryBefore) + ").");
        }

        private static object CaptureNext(PeopleOfTheIliacBayMod host, MethodInfo capture,
            DaggerfallTalkWindow talk, string playerText)
        {
            long revision = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                "conversationContextRequestRevision", false).GetValue(host)) + 1;
            object snapshot = capture.Invoke(host, new object[] { talk, playerText, revision });
            MethodInfo current = RequireMethod(typeof(PeopleOfTheIliacBayMod), "IsCurrentConversationContext", false,
                typeof(ConversationContextSnapshot), typeof(DaggerfallTalkWindow));
            if (snapshot == null || !(bool)current.Invoke(host, new object[] { snapshot, talk }))
                throw new InvalidOperationException("Native snapshot capture did not produce a current same-window request.");
            RequireMethod(typeof(PeopleOfTheIliacBayMod), "CaptureConversationCommandTurn", false,
                typeof(ConversationContextSnapshot), typeof(string)).Invoke(host, new object[] { snapshot, playerText });
            SnapshotTurn(host, snapshot);
            return snapshot;
        }

        private static NpcConversationCommandTurn SnapshotTurn(PeopleOfTheIliacBayMod host, object snapshot)
        {
            var turn = RequireMethod(typeof(PeopleOfTheIliacBayMod), "CommandTurnFor", false,
                typeof(ConversationContextSnapshot)).Invoke(host, new[] { snapshot }) as NpcConversationCommandTurn;
            if (turn == null)
                throw new InvalidOperationException("Captured request has no exact live conversation command turn.");
            return turn;
        }

        private static void RequireQuietBeforeProbe(PeopleOfTheIliacBayMod host, WorldStateStore store,
            ResidentActor actor, DaggerfallTalkWindow talk, bool expectBusy)
        {
            bool busy = (bool)ReadField(host, "conversationBusy");
            bool returningGreeting = (bool)ReadField(host, "returningGreetingPending");
            if (!actor || !actor.IsConversing || DaggerfallUI.UIManager.TopWindow != talk ||
                talk == null || !talk.IsSetup || !(bool)ReadField(host, "conversationSessionOpen") ||
                GameManager.Instance.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile ||
                GameManager.Instance.TalkManager.MobileNPC != actor.Person ||
                store.ActiveNpcAction(actor.Resident.Id) != null || store.ActiveNpcTask(actor.Resident.Id) != null ||
                ReadField(host, "pendingConversationAction") != null || busy != expectBusy || returningGreeting)
                throw new InvalidOperationException("Reply-outcome probe requires clean actor with same open Talk session and no pending action/task/offer: " +
                    DescribePrecondition(host, store, actor, talk, GameManager.Instance));
        }

        private static string DescribePrecondition(PeopleOfTheIliacBayMod host, WorldStateStore store,
            ResidentActor actor, DaggerfallTalkWindow talk, GameManager game)
        {
            object pendingOffer = ReadField(host, "pendingConversationAction");
            NpcActionJob action = actor && actor.Resident != null ? store.ActiveNpcAction(actor.Resident.Id) : null;
            NpcTaskPlan task = actor && actor.Resident != null ? store.ActiveNpcTask(actor.Resident.Id) : null;
            return "actor=" + (actor ? actor.Resident.Id : "<missing>") +
                "; actorConversing=" + (actor && actor.IsConversing) +
                "; talkSetup=" + (talk != null && talk.IsSetup) +
                "; topIsTalk=" + (talk != null && DaggerfallUI.UIManager.TopWindow == talk) +
                "; session=" + ReadField(host, "conversationSessionOpen") +
                "; busy=" + ReadField(host, "conversationBusy") +
                "; expectBusy=true; returningGreetingPending=" + ReadField(host, "returningGreetingPending") +
                "; pendingOffer=" + (pendingOffer == null ? "<none>" : pendingOffer.GetType().FullName) +
                "; activeAction=" + (action == null ? "<none>" : action.Id + "/" + action.Phase) +
                "; activeTask=" + (task == null ? "<none>" : task.Id + "/" + task.Phase) +
                "; managerType=" + game.TalkManager.CurrentNPCType +
                "; managerSpeakerMatches=" + (actor && game.TalkManager.MobileNPC == actor.Person) + ".";
        }

        private static ResidentActor FindUnusedManagedAdult(WorldStateStore store, Vector3 playerPosition)
        {
            ResidentActor best = null;
            float bestDistance = float.MaxValue;
            foreach (ResidentActor candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!candidate || !candidate.gameObject.activeInHierarchy || !candidate.Person || candidate.Resident == null || !candidate.Resident.IsAlive ||
                    candidate.Resident.IsQuestProtected || candidate.Resident.AgeGroup != ResidentAgeGroup.Adult ||
                    candidate.IsSleeping || candidate.IsSeated || candidate.IsConversing)
                    continue;
                if (store.ActiveNpcAction(candidate.Resident.Id) != null || store.ActiveNpcTask(candidate.Resident.Id) != null ||
                    store.ConversationTranscript(candidate.Resident.Id, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).Count != 0)
                    continue;
                float distance = Vector3.Distance(candidate.transform.position, playerPosition);
                if (distance < bestDistance) { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        internal static bool TrySurveyedOutdoorObserver(StreetPopulation streets, Vector3 actorGround, out Vector3 observer)
        {
            observer = Vector3.zero;
            TownGeometry geometry = RequireField(typeof(StreetPopulation), "geometry", false).GetValue(streets) as TownGeometry;
            BuildingDirectory directory = RequireField(typeof(StreetPopulation), "directory", false).GetValue(streets) as BuildingDirectory;
            if (geometry == null || !directory) return false;
            Transform origin = directory.transform;
            Vector3 localActor = origin.InverseTransformPoint(actorGround);
            var from = new SavedRoutePoint { X = localActor.x, Z = localActor.z };
            for (int radius = 4; radius <= 6; radius++)
            for (int direction = 0; direction < 16; direction++)
            {
                double angle = direction * Math.PI / 8;
                float distance = radius * 0.75f;
                Vector3 candidate = new Vector3(actorGround.x + (float)Math.Cos(angle) * distance,
                    actorGround.y, actorGround.z + (float)Math.Sin(angle) * distance);
                Vector3 local = origin.InverseTransformPoint(candidate);
                if (geometry.RouteBetweenPoints(from, new SavedRoutePoint { X = local.x, Z = local.z }).Count < 2)
                    continue;
                observer = candidate;
                return true;
            }
            return false;
        }

        private static NpcActionOption FindCandidate(List<NpcActionOption> options, NpcActionKind kind)
        {
            if (options == null) return null;
            foreach (NpcActionOption option in options)
                if (option != null && option.Kind == kind && option.Target != null && option.ItemSourceStep == 0)
                    return option;
            return null;
        }

        private static IEnumerator OpenTalk(PeopleOfTheIliacBayMod host, GameManager game, ResidentActor actor,
            FieldInfo pendingField, FieldInfo retryField, FieldInfo busyField, Action<DaggerfallTalkWindow> complete)
        {
            bool priorPending = (bool)pendingField.GetValue(host);
            bool priorBusy = (bool)busyField.GetValue(host);
            float priorRetry = Convert.ToSingle(retryField.GetValue(host));
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
                        complete(talk);
                        yield break;
                    }
                    yield return null;
                }
                throw new InvalidOperationException("Native Talk did not bind selected managed resident.");
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

        private static IEnumerator LoadBaseline(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            GameManager game = GameManager.Instance;
            if (!host || !streets || !game || !game.IsReady) throw new InvalidOperationException("Baseline restore requires ready fixture.");
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
                while ((!loaded || !streets.Ready || (bool)RequireField(typeof(PeopleOfTheIliacBayMod),
                    "conversationSessionOpen", false).GetValue(host)) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (!loaded || !streets.Ready || (bool)RequireField(typeof(PeopleOfTheIliacBayMod),
                    "conversationSessionOpen", false).GetValue(host))
                    throw new InvalidOperationException("Exact baseline fixture did not finish loading.");
            }
            finally { SaveLoadManager.OnLoad -= onLoad; }
            yield return null;
        }

        private static int ReadDispatchVersion(object localDialogue)
        {
            FieldInfo dispatchField = RequireField(localDialogue.GetType(), "dispatch", false);
            object dispatch = dispatchField.GetValue(localDialogue);
            if (dispatch == null) throw new InvalidOperationException("Local dialogue dispatch state is missing.");
            return Convert.ToInt32(RequireField(dispatch.GetType(), "version", false).GetValue(dispatch));
        }

        private static long ReadReplyRequestCount(Api api, object localDialogue)
        { return Convert.ToInt64(api.ReplyRequestCount.GetValue(localDialogue, null)); }

        private static string ReadHistory(MethodInfo method, DaggerfallTalkWindow talk)
        { return Convert.ToString(method.Invoke(null, new object[] { talk })) ?? string.Empty; }

        private static int CountMemory(WorldStateStore store, string residentId, string playerText, string npcText)
        {
            int count = 0;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(residentId, double.MaxValue, 256))
                if (entry != null && entry.Source == "action" && entry.PlayerText == playerText && entry.NpcText == npcText)
                    count++;
            return count;
        }

        private static int CountOccurrences(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value)) return 0;
            int count = 0, offset = 0;
            while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            { count++; offset += value.Length; }
            return count;
        }

        private static void VerifyUnavailableHistoryProvenance(PeopleOfTheIliacBayMod host, WorldStateStore store,
            DaggerfallTalkWindow talk, MethodInfo historyMethod, string residentId)
        {
            const string identicalReply = "I cannot settle that right now.";
            string unavailableQuestion = "Unavailable provenance control " + Guid.NewGuid().ToString("N");
            string modelQuestion = "Ordinary model provenance control " + Guid.NewGuid().ToString("N");
            MethodInfo append = RequireMethod(typeof(PeopleOfTheIliacBayMod), "AppendRememberedDialoguePair", false,
                typeof(DaggerfallTalkWindow), typeof(string), typeof(string), typeof(string));
            int firstNewRow = TranscriptCount(talk);
            int sharedExchangesBefore = SharedExchangeCount(store, residentId);
            if (!ReferenceEquals(RequireField(typeof(PeopleOfTheIliacBayMod), "memoryWindow", false).GetValue(host), talk))
                throw new InvalidOperationException("Unavailable-row capture fixture is not bound to this Talk window.");
            try { append.Invoke(host, new object[] { talk, unavailableQuestion, identicalReply, "unavailable" }); }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Could not append unavailable-row provenance fixture.", error.InnerException ?? error); }
            RequireField(typeof(PeopleOfTheIliacBayMod), "memoryCursor", false).SetValue(host, firstNewRow);
            MethodInfo capture = RequireMethod(typeof(PeopleOfTheIliacBayMod), "CaptureNativeDialogueMemory", false);
            try { capture.Invoke(host, null); }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Could not scan unavailable-row provenance fixture.", error.InnerException ?? error); }
            if ((int)RequireField(typeof(PeopleOfTheIliacBayMod), "memoryCursor", false).GetValue(host) != TranscriptCount(talk))
                throw new InvalidOperationException("Unavailable-row capture fixture did not scan to the transcript end.");
            int unavailableScanned = CountMatchingMemory(store, residentId, unavailableQuestion, identicalReply, "unavailable");
            int falseNativePair = CountMatchingMemory(store, residentId, unavailableQuestion, identicalReply, "native");
            if (unavailableScanned != 1 || falseNativePair != 0 || SharedExchangeCount(store, residentId) != sharedExchangesBefore)
                throw new InvalidOperationException("Tagged unavailable row scan duplicated native speech or counted as a shared NPC exchange.");
            try { append.Invoke(host, new object[] { talk, modelQuestion, identicalReply, "model" }); }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Could not append same-text model provenance fixture.", error.InnerException ?? error); }
            if (SharedExchangeCount(store, residentId) != sharedExchangesBefore + 1)
                throw new InvalidOperationException("Same-text model reply did not retain normal shared-exchange accounting.");

            VerifyProvenanceRows(talk, historyMethod, unavailableQuestion, modelQuestion, identicalReply);
            int unavailableStored = 0, modelStored = 0, nativeDuplicate = 0;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(residentId, double.MaxValue, 256))
            {
                if (entry == null || entry.NpcText != identicalReply) continue;
                if (entry.PlayerText == unavailableQuestion && entry.Source == "unavailable") unavailableStored++;
                if (entry.PlayerText == modelQuestion && entry.Source == "model") modelStored++;
                if (entry.PlayerText == unavailableQuestion && entry.Source == "native") nativeDuplicate++;
            }
            if (unavailableStored != 1 || modelStored != 1 || nativeDuplicate != 0)
                throw new InvalidOperationException("Unavailable/model same-text provenance was not stored once with source intact.");

            // Run the same history restoration entry point used on Talk reopen.
            // It replays archive entries into presentation rows without new turns.
            MethodInfo begin = RequireMethod(typeof(PeopleOfTheIliacBayMod), "BeginDialogueMemory", false,
                typeof(DaggerfallTalkWindow));
            try { begin.Invoke(host, new object[] { talk }); }
            catch (TargetInvocationException error)
            { throw new InvalidOperationException("Could not replay saved provenance rows into native Talk history.", error.InnerException ?? error); }
            RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending", false).SetValue(host, false);
            RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingRetryAt", false).SetValue(host, Time.unscaledTime + 86400f);
            VerifyProvenanceRows(talk, historyMethod, unavailableQuestion, modelQuestion, identicalReply);
            unavailableStored = modelStored = nativeDuplicate = 0;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(residentId, double.MaxValue, 256))
            {
                if (entry == null || entry.NpcText != identicalReply) continue;
                if (entry.PlayerText == unavailableQuestion && entry.Source == "unavailable") unavailableStored++;
                if (entry.PlayerText == modelQuestion && entry.Source == "model") modelStored++;
                if (entry.PlayerText == unavailableQuestion && entry.Source == "native") nativeDuplicate++;
            }
            if (unavailableStored != 1 || modelStored != 1 || nativeDuplicate != 0)
                throw new InvalidOperationException("Replaying unavailable row changed its saved source or duplicated it as native speech.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REPLY_OUTCOME_UNAVAILABLE_PROVENANCE_PASS: identical-display-text=unavailable-plus-model; " +
                "live-history=source-aware; tagged-row-scanned=true; archive-replay=source-aware; no-native-duplicate=true.");
        }

        private static int TranscriptCount(DaggerfallTalkWindow talk)
        {
            FieldInfo field = typeof(DaggerfallTalkWindow).GetField("listboxConversation", Members);
            ListBox transcript = field == null ? null : field.GetValue(talk) as ListBox;
            if (transcript == null) throw new InvalidOperationException("Native conversation transcript is unavailable.");
            return transcript.Count;
        }

        private static int SharedExchangeCount(WorldStateStore store, string residentId)
        {
            FieldInfo stateField = RequireField(typeof(WorldStateStore), "state", false);
            WorldState world = stateField.GetValue(store) as WorldState;
            if (world == null || world.ConversationAttitudes == null) throw new InvalidOperationException("Conversation attitude ledger unavailable.");
            foreach (ConversationAttitude attitude in world.ConversationAttitudes)
                if (attitude != null && attitude.PersonId == residentId) return attitude.SharedExchangeCount;
            return 0;
        }

        private static int CountMatchingMemory(WorldStateStore store, string residentId,
            string playerText, string npcText, string source)
        {
            int count = 0;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(residentId, double.MaxValue, 256))
                if (entry != null && entry.PlayerText == playerText && entry.NpcText == npcText && entry.Source == source) count++;
            return count;
        }

        private static void VerifyProvenanceRows(DaggerfallTalkWindow talk, MethodInfo historyMethod,
            string unavailableQuestion, string modelQuestion, string identicalReply)
        {
            FieldInfo nativeList = typeof(DaggerfallTalkWindow).GetField("listboxConversation", Members);
            if (nativeList == null) throw new InvalidOperationException("Native conversation transcript field is unavailable.");
            ListBox transcript = nativeList.GetValue(talk) as ListBox;
            if (transcript == null) throw new InvalidOperationException("Native conversation transcript is unavailable.");
            int unavailableRows = 0, modelRows = 0;
            for (int index = 0; index < transcript.Count; index++)
            {
                var row = transcript.GetItem(index);
                if (row == null || row.textLabel == null || row.textLabel.Text != identicalReply) continue;
                if (row.textLabel.Name == "PeopleOfTheIliacBayUnavailableReply") unavailableRows++;
                else if (row.textLabel.Name != "PeopleOfTheIliacBayHistoryDay" && row.textLabel.Name != "PeopleOfTheIliacBayHistoryRule") modelRows++;
            }
            string history = ReadHistory(historyMethod, talk);
            string unavailableLine = DialogueHistoryWindow.UnavailableResponseLabel + identicalReply;
            int unavailablePlayers = CountOccurrences(history, "Player: " + unavailableQuestion);
            int modelPlayers = CountOccurrences(history, "Player: " + modelQuestion);
            string structured = DialogueHistoryWindow.Messages(history);
            string assistantReply = "<|im_start|>assistant\n" + identicalReply;
            string fallbackMetadata = "<|im_start|>system\nRecorded transport fallback, not NPC speech: " + identicalReply;
            if (unavailableRows != 1 || modelRows != 1 || unavailablePlayers != 1 || modelPlayers != 1 ||
                CountOccurrences(history, unavailableLine) != 1 || CountOccurrences(history, "NPC: " + identicalReply) != 1 ||
                CountOccurrences(structured, assistantReply) != 1 || CountOccurrences(structured, fallbackMetadata) != 1)
                throw new InvalidOperationException("Same displayed fallback/model wording lost its row-specific source in live or structured history.");
        }

        private static bool ContainsCompletionClaim(string text)
        {
            string lower = (text ?? string.Empty).ToLowerInvariant();
            return lower.Contains("completed") || lower.Contains("already arrived") ||
                lower.Contains("paid") || lower.Contains("purchased") || lower.Contains("delivered");
        }

        private static object ReadField(object target, string name)
        { return RequireField(target.GetType(), name, false).GetValue(target); }

        private static object ReadMember(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, Members);
            if (property != null) return property.GetValue(target, null);
            FieldInfo field = target.GetType().GetField(name, Members);
            if (field != null) return field.GetValue(target);
            throw new InvalidOperationException("Reply receipt has no member " + name + ".");
        }

        private static string ReadString(object target, string name)
        { return Convert.ToString(ReadMember(target, name)); }

        private static int ReadInt(object target, string name)
        { return Convert.ToInt32(ReadMember(target, name)); }

        private static MethodInfo RequireMethod(Type type, string name, bool isStatic, params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, Members, null, parameters, null);
            if (method == null || method.IsStatic != isStatic)
                throw new InvalidOperationException("Required reply-outcome API is missing: " + type.FullName + "." + name + ".");
            return method;
        }

        private static FieldInfo RequireField(Type type, string name, bool isStatic)
        {
            FieldInfo field = type.GetField(name, Members);
            if (field == null || field.IsStatic != isStatic)
                throw new InvalidOperationException("Required reply-outcome runtime field is missing: " + type.FullName + "." + name + ".");
            return field;
        }

        private static void RequireOwnedFixture(string expectedCharacter)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) < 0 ||
                !string.Equals(expectedCharacter, FixtureCharacter, StringComparison.Ordinal) ||
                !GameManager.Instance || GameManager.Instance.PlayerEntity.Name != FixtureCharacter)
                throw new InvalidOperationException("Reply-outcome probe requires explicit flag and exact disposable fixture.");
        }

        private static IEnumerator DriveFlattened(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            Exception failure = null;
            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator iterator = stack.Peek();
                    bool more;
                    object current = null;
                    try { more = iterator.MoveNext(); if (more) current = iterator.Current; }
                    catch (Exception error) { failure = error; break; }
                    if (!more)
                    {
                        stack.Pop();
                        Dispose(iterator, ref failure);
                        if (failure != null) break;
                        continue;
                    }
                    IEnumerator child = current as IEnumerator;
                    if (child != null && !(current is YieldInstruction) && !(current is CustomYieldInstruction))
                    { stack.Push(child); continue; }
                    yield return current;
                }
            }
            finally { while (stack.Count > 0) Dispose(stack.Pop(), ref failure); }
            if (failure != null) throw new InvalidOperationException("Nested reply-outcome coroutine failed.", failure);
        }

        private static void Dispose(IEnumerator iterator, ref Exception failure)
        {
            IDisposable disposable = iterator as IDisposable;
            if (disposable == null) return;
            try { disposable.Dispose(); }
            catch (Exception error) { if (failure == null) failure = error; }
        }
    }
}
