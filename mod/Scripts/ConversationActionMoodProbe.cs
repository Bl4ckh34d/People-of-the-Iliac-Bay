using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Native acceptance for mood commits through receipt-grounded action wording.
    // The controlled path holds the exact production completion closure.
    internal static class ConversationActionMoodProbe
    {
        private const string RunFlag = "-peopleoftheiliacbay-conversation-action-mood-probe";
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string BaselineSave = "People - Gothway Garden";

        private enum Phase { PassThrough, ObserveLive, Control }
        private sealed class Evidence
        {
            internal string PersonId;
            internal string CheckpointTitle;
            internal string LiveReply;
            internal int InitialValue;
            internal long InitialSequence;
            internal double BaselineClock;
            internal Vector3 BaselinePlayerPosition;
            internal bool GreetingPending;
            internal float GreetingRetryAt;
        }

        private static Phase phase;
        private static Action<string> heldCompletion;
        private static DialogueRequest lastRequest;
        private static string lastLiveResult;

        // Called only around the actual action-wording LocalDialogue request.
        // ObserveLive passes the real callback through unchanged; Control holds
        // that callback so tests do not reimplement production validation.
        internal static Action<string> WrapCompletion(ConversationContextSnapshot snapshot,
            DialogueRequest request, Action<string> complete)
        {
            if (complete == null || phase == Phase.PassThrough ||
                Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) < 0)
                return complete;
            lastRequest = request;
            if (phase == Phase.ObserveLive)
                return answer => { lastLiveResult = answer; complete(answer); };
            heldCompletion = complete;
            return answer => { };
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            ConversationMoodProbe.Require(character == OwnedCharacter && fixtureSave == BaselineSave,
                "Action mood probe requires the owned disposable character and exact baseline save.");
            if (!host || !streets || !GameManager.Instance || !GameManager.Instance.IsReady || !streets.Ready)
                throw new InvalidOperationException("Action mood probe requires the ready owned native fixture.");

            Evidence evidence = new Evidence();
            evidence.GreetingPending = Convert.ToBoolean(ConversationMoodProbe.RequireField(
                typeof(PeopleOfTheIliacBayMod), "returningGreetingPending").GetValue(host), CultureInfo.InvariantCulture);
            evidence.GreetingRetryAt = Convert.ToSingle(ConversationMoodProbe.RequireField(
                typeof(PeopleOfTheIliacBayMod), "returningGreetingRetryAt").GetValue(host), CultureInfo.InvariantCulture);
            Exception probeFailure = null;
            phase = Phase.PassThrough;
            heldCompletion = null;
            lastRequest = null;
            lastLiveResult = null;

            IEnumerator cases = RunCases(host, streets, character, evidence);
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = cases.MoveNext(); if (more) current = cases.Current; }
                    catch (Exception error) { probeFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                ConversationMoodProbe.Dispose(cases, ref probeFailure);
                phase = Phase.PassThrough;
                heldCompletion = null;
                lastRequest = null;
                if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
                ConversationMoodProbe.CancelTransport(host);
            }

            Exception restoreFailure = null;
            Exception baselineFailure = null;
            Exception cleanupFailure = null;
            // The exact baseline is loaded, never overwritten. A forced Unity
            // coroutine/process termination can still interrupt this yielded
            // asynchronous restore; a generated checkpoint may remain if the
            // process is killed before its finally cleanup executes.
            try
            {
                IEnumerator restore = ConversationMoodProbe.LoadSave(host, streets, character, BaselineSave, false);
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
                finally { ConversationMoodProbe.Dispose(restore, ref restoreFailure); }

                baselineFailure = restoreFailure;
                if (baselineFailure == null)
                {
                    try
                    {
                        WorldStateStore restored = ConversationMoodProbe.ReadHostField(host, "store") as WorldStateStore;
                        ConversationMoodProbe.Require(restored != null, "Baseline load did not restore the native store.");
                        double clock = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                        ConversationMoodProbe.Require(Math.Abs(clock - evidence.BaselineClock) <= 2d,
                            "Exact baseline load did not restore the game clock.");
                        ConversationMoodProbe.Require(GameManager.Instance.PlayerMotor != null &&
                            Vector3.Distance(GameManager.Instance.PlayerMotor.transform.position,
                                evidence.BaselinePlayerPosition) <= 0.05f,
                            "Exact baseline load did not restore the player position.");
                        if (!string.IsNullOrEmpty(evidence.PersonId))
                            RequireMood(restored, evidence.PersonId, evidence.InitialValue, evidence.InitialSequence,
                                "Baseline rollback retained action mood state.");
                    }
                    catch (Exception error) { baselineFailure = error; }
                }
            }
            finally
            {
                try { ConversationMoodProbe.RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending")
                    .SetValue(host, evidence.GreetingPending); }
                catch (Exception error) { cleanupFailure = error; }
                try { ConversationMoodProbe.RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingRetryAt")
                    .SetValue(host, evidence.GreetingRetryAt); }
                catch (Exception error)
                {
                    if (cleanupFailure == null) cleanupFailure = error;
                    else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_CLEANUP_FAIL: " + error);
                }
                if (!string.IsNullOrEmpty(evidence.CheckpointTitle))
                {
                    try { ConversationMoodProbe.DeleteCheckpoint(GameManager.Instance, character, evidence.CheckpointTitle); }
                    catch (Exception error)
                    {
                        if (cleanupFailure == null) cleanupFailure = error;
                        else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_CHECKPOINT_CLEANUP_FAIL: " + error);
                    }
                }
            }

            if (restoreFailure != null)
            {
                if (cleanupFailure != null)
                    Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_CLEANUP_FAIL_AFTER_RESTORE_FAILURE: " + cleanupFailure);
                throw new InvalidOperationException("Action mood probe could not restore exact baseline.", restoreFailure);
            }
            if (probeFailure != null)
            {
                if (baselineFailure != null) Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_BASELINE_VERIFY_FAIL: " + baselineFailure);
                if (cleanupFailure != null) Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_CLEANUP_FAIL: " + cleanupFailure);
                throw new InvalidOperationException("Action mood probe failed after baseline restore.", probeFailure);
            }
            if (baselineFailure != null)
            {
                if (cleanupFailure != null)
                    Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_CLEANUP_FAIL_AFTER_BASELINE_FAILURE: " + cleanupFailure);
                throw new InvalidOperationException("Action mood baseline verification failed.", baselineFailure);
            }
            if (cleanupFailure != null) throw new InvalidOperationException("Action mood cleanup failed.", cleanupFailure);

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_BASELINE_RESTORED: character=" + character +
                "; save=" + BaselineSave + "; checkpoint-deleted=" + !string.IsNullOrEmpty(evidence.CheckpointTitle) + ".");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_LIVE_PASS: person=" + evidence.PersonId +
                "; actual-action-request=true; typed-result=true; final-delta-committed-once=true; " +
                "invalid-no-mutation=true; typed-sentinels-no-mutation=true; duplicate-rephrase-committed-once=true; " +
                "receipt-change-no-stale-delta=true; " +
                "stale-no-mutation=true.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_MOOD_DISK_ROLLBACK_PASS: title=" + evidence.CheckpointTitle +
                "; checkpoint-mood-restored=true; post-checkpoint-exchange-removed=true; baseline-rollback=true; " +
                "disk-reload=true.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, Evidence evidence)
        {
            GameManager game = GameManager.Instance;
            SaveLoadManager saves = game.SaveLoadManager;
            saves.EnumerateSaves();
            if (saves.FindSaveFolderByNames(character, BaselineSave) < 0)
                throw new InvalidOperationException("Exact baseline save missing; refusing action mood probe.");
            evidence.BaselineClock = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            evidence.BaselinePlayerPosition = game.PlayerMotor.transform.position;
            WorldStateStore store = ConversationMoodProbe.ReadHostField(host, "store") as WorldStateStore;
            if (store == null) throw new InvalidOperationException("Action mood probe cannot read active world store.");
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                game.PlayerEnterExit.TransitionExterior();
                DaggerfallUI.Instance.PopToHUD();
                float exteriorDeadline = Time.realtimeSinceStartup + 30f;
                while (game.PlayerEnterExit.IsPlayerInsideBuilding && Time.realtimeSinceStartup < exteriorDeadline)
                    yield return new WaitForSecondsRealtime(0.25f);
                if (game.PlayerEnterExit.IsPlayerInsideBuilding)
                    throw new InvalidOperationException("Could not enter the Gothway Garden exterior scene.");
            }

            ResidentActor actor = null;
            float actorDeadline = Time.realtimeSinceStartup + 45f;
            while (!actor && Time.realtimeSinceStartup < actorDeadline)
            {
                store = ConversationMoodProbe.ReadHostField(host, "store") as WorldStateStore;
                actor = store == null ? null : ConversationMoodProbe.FindUnusedManagedAdult(store,
                    game.PlayerMotor.transform.position);
                if (!actor) yield return new WaitForSecondsRealtime(0.25f);
            }
            if (!actor) throw new InvalidOperationException("No unused eligible adult resident became available within 45 seconds.");
            ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
            evidence.PersonId = actor.Resident.Id;
            ConversationMoodView initial = store.CaptureConversationMood(evidence.PersonId);
            evidence.InitialValue = initial.Value;
            evidence.InitialSequence = initial.AcceptedTurnSequence;
            ConversationMoodProbe.Require(initial.Value == 0 && initial.AcceptedTurnSequence == 0,
                "Owned baseline actor must begin with neutral mood and sequence zero.");

            DaggerfallTalkWindow talk = null;
            yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
            ConversationMoodProbe.Require(talk != null, "Native Talk did not bind selected action mood resident.");
            LocalDialogueClient client = ConversationMoodProbe.ReadHostField(host, "localDialogue") as LocalDialogueClient;
            if (!client) throw new InvalidOperationException("Native local dialogue client is unavailable.");

            // A real model request through SubmitConversationActionReply and its
            // typed unresolved receipt provides the live action-mood evidence.
            const string liveText = "Could you tell me how your day is going?";
            long requestsBefore = ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount");
            long completionsBefore = ConversationMoodProbe.ReadLongProperty(client, "ReplyCompletionCount");
            phase = Phase.ObserveLive;
            SubmitAction(host, talk, liveText, ConversationActionReply.Unresolved("No action has been agreed."));
            float liveDeadline = Time.realtimeSinceStartup + 150f;
            while ((ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount") <= requestsBefore ||
                ConversationMoodProbe.ReadLongProperty(client, "ReplyCompletionCount") <= completionsBefore ||
                store.CaptureConversationMood(evidence.PersonId).AcceptedTurnSequence <= evidence.InitialSequence) &&
                Time.realtimeSinceStartup < liveDeadline)
                yield return null;
            phase = Phase.PassThrough;
            long liveRequests = ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount") - requestsBefore;
            long liveCompletions = ConversationMoodProbe.ReadLongProperty(client, "ReplyCompletionCount") - completionsBefore;
            ConversationMoodProbe.Require(liveRequests == 1 && liveCompletions == 1 && lastRequest != null,
                "Real action mood request did not produce exactly one observed request/completion.");
            SocialDialogueResult liveResult = null;
            ConversationMoodProbe.Require(lastRequest.ActionReply != null && lastRequest.MoodEnabled &&
                lastRequest.MoodValue == initial.Value && SocialDialogueResult.TryParse(lastLiveResult, out liveResult),
                "Real action wording result was not typed or did not carry captured mood context.");
            ConversationMoodView afterLive = store.CaptureConversationMood(evidence.PersonId);
            evidence.LiveReply = liveResult.Reply;
            ConversationMoodProbe.Require(afterLive.AcceptedTurnSequence == evidence.InitialSequence + 1 &&
                afterLive.Value == Clamp(initial.Value + liveResult.MoodDelta),
                "Real action result did not commit its final mood delta exactly once.");
            ConversationMoodProbe.Require(ConversationMoodProbe.CountExchange(store, evidence.PersonId,
                liveText, liveResult.Reply, "action") == 1,
                "Live action callback did not journal exactly one final prose exchange with action provenance.");
            ConversationMoodProbe.Require(CountAnyExchange(store, evidence.PersonId, liveText, liveResult.Reply) == 1,
                "Live action callback persisted duplicate final prose under another provenance source.");

            // Malformed typed output becomes the ordinary unavailable fallback;
            // it must not change mood or persist the raw model payload.
            ConversationMoodView beforeInvalid = store.CaptureConversationMood(evidence.PersonId);
            Action<string> invalid = StartControlled(host, talk, "IMM09-action-invalid", client);
            ConversationMoodProbe.Require(lastRequest.MoodValue == beforeInvalid.Value &&
                LocalDialogue.Build(lastRequest).Prompt.Contains("Current persistent mood value: " +
                    beforeInvalid.Value.ToString(CultureInfo.InvariantCulture)),
                "Next action wording request did not ground its prompt in the committed mood value.");
            invalid("not a typed mood result");
            RequireMood(store, evidence.PersonId, beforeInvalid.Value, beforeInvalid.AcceptedTurnSequence,
                "Invalid action mood payload changed mood or accepted sequence.");
            ConversationMoodProbe.Require(ConversationMoodProbe.CountExchange(store, evidence.PersonId,
                "IMM09-action-invalid", "not a typed mood result", "action") == 0,
                "Invalid typed payload was journaled as model prose.");
            ConversationMoodProbe.Require(CountAnyExchange(store, evidence.PersonId,
                "IMM09-action-invalid", "not a typed mood result") == 0,
                "Rejected raw typed payload was persisted under another provenance source.");

            Action<string> unavailableSentinel = StartControlled(host, talk,
                "IMM09-action-unavailable-sentinel", client);
            unavailableSentinel(Encode(LocalDialogueClient.Unavailable, 9));
            RequireMood(store, evidence.PersonId, beforeInvalid.Value, beforeInvalid.AcceptedTurnSequence,
                "Typed unavailable sentinel applied mood.");
            ConversationMoodProbe.Require(ConversationMoodProbe.CountExchange(store, evidence.PersonId,
                "IMM09-action-unavailable-sentinel", "No new reply was received.", "unavailable") == 1,
                "Typed unavailable sentinel did not use the ordinary unavailable fallback.");

            Action<string> contextSentinel = StartControlled(host, talk,
                "IMM09-action-context-sentinel", client);
            contextSentinel(Encode(LocalDialogueClient.ContextLimit, -9));
            RequireMood(store, evidence.PersonId, beforeInvalid.Value, beforeInvalid.AcceptedTurnSequence,
                "Typed context-limit sentinel applied mood.");
            ConversationMoodProbe.Require(ConversationMoodProbe.CountExchange(store, evidence.PersonId,
                "IMM09-action-context-sentinel", "No new reply was received.", "unavailable") == 1,
                "Typed context-limit sentinel did not use the ordinary unavailable fallback.");

            Action<string> positive = StartControlled(host, talk, "IMM09-action-positive", client);
            positive("{\"reply\":\"I am glad we spoke.\",\"moodDelta\":3}");
            ConversationMoodView afterPositive = store.CaptureConversationMood(evidence.PersonId);
            ConversationMoodProbe.Require(afterPositive.Value == Clamp(beforeInvalid.Value + 3) &&
                afterPositive.AcceptedTurnSequence == beforeInvalid.AcceptedTurnSequence + 1 &&
                ConversationMoodProbe.CountExchange(store, evidence.PersonId, "IMM09-action-positive",
                    "I am glad we spoke.", "action") == 1,
                "Controlled action callback did not commit exactly one final reply and +3 mood delta.");
            ConversationMoodProbe.Require(CountAnyExchange(store, evidence.PersonId,
                "IMM09-action-positive", "I am glad we spoke.") == 1,
                "Accepted controlled action prose was duplicated under another provenance source.");
            positive("{\"reply\":\"I am glad we spoke.\",\"moodDelta\":3}");
            RequireMood(store, evidence.PersonId, afterPositive.Value, afterPositive.AcceptedTurnSequence,
                "Replayed action completion changed mood or sequence.");

            // Force the real duplicate/rephrase branch using the immediately
            // prior NPC reply. Only the distinct rephrase may be committed.
            const string duplicateText = "Could you say more about that?";
            const string duplicateReply = "I am glad we spoke.";
            Action<string> duplicate = StartControlled(host, talk, duplicateText, client);
            ConversationMoodProbe.Require(lastRequest.MoodValue == afterPositive.Value,
                "Action rephrase request did not carry the mood value after the accepted +3 turn.");
            long beforeDuplicateRequests = ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount");
            duplicate(Encode(duplicateReply, 2));
            float rephraseDeadline = Time.realtimeSinceStartup + 10f;
            while ((heldCompletion == null || ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount") < beforeDuplicateRequests + 1) &&
                Time.realtimeSinceStartup < rephraseDeadline) yield return null;
            Action<string> rephrase = heldCompletion;
            ConversationMoodProbe.Require(rephrase != null && !ReferenceEquals(rephrase, duplicate) &&
                lastRequest != null && lastRequest.AvoidDuplicateReply == duplicateReply,
                "Exact duplicate did not dispatch a distinct bounded mood-enabled rephrase request.");
            ConversationMoodProbe.CancelTransport(host);
            rephrase("{\"reply\":\"I have something new to add.\",\"moodDelta\":2}");
            ConversationMoodView afterRephrase = store.CaptureConversationMood(evidence.PersonId);
            ConversationMoodProbe.Require(afterRephrase.Value == Clamp(afterPositive.Value + 2) &&
                afterRephrase.AcceptedTurnSequence == afterPositive.AcceptedTurnSequence + 1 &&
                ConversationMoodProbe.CountExchange(store, evidence.PersonId, duplicateText,
                    "I have something new to add.", "action") == 1 &&
                ConversationMoodProbe.CountExchange(store, evidence.PersonId, duplicateText,
                    duplicateReply, "action") == 0,
                "Duplicate/rephrase flow did not commit only its final distinct prose and mood delta.");
            ConversationMoodProbe.Require(CountAnyExchange(store, evidence.PersonId, duplicateText,
                "I have something new to add.") == 1 && CountAnyExchange(store, evidence.PersonId,
                    duplicateText, duplicateReply) == 0,
                "Duplicate/rephrase flow persisted duplicate or first-attempt prose under another source.");

            // Change a real native action receipt while its exact wording
            // callback is held. Production must use the new canonical receipt
            // and discard the stale generated delta.
            NpcActionOption wait = FindWait(streets.ConversationActionCandidates(evidence.PersonId));
            if (wait == null) throw new InvalidOperationException("Selected resident has no current native Wait option.");
            string acceptReason = null;
            if (!NpcProbeCommandSource.TryAcceptAction(streets,evidence.PersonId,
                "action-mood-probe:" + Guid.NewGuid().ToString("N"),
                new NpcActionDecision { Option = wait, DurationSeconds = 900 }, out acceptReason))
                throw new InvalidOperationException("Native Wait receipt fixture was rejected: " + acceptReason);
            NpcActionJob acceptedAction = store.ActiveNpcAction(evidence.PersonId);
            ConversationActionReply acceptedReceipt = null;
            ConversationMoodProbe.Require(acceptedAction != null && acceptedAction.Kind == NpcActionKind.Wait &&
                acceptedAction.Phase == NpcActionPhase.Accepted &&
                ConversationActionReply.TryFromSavedAction(store, acceptedAction.Id, out acceptedReceipt),
                "Could not capture a fresh accepted native action receipt.");
            Action<string> changedReceipt = StartControlled(host, talk, "IMM09-action-receipt-changed", client,
                acceptedReceipt);
            string cancellationReason;
            ConversationMoodProbe.Require(streets.TryStopConversationAction(evidence.PersonId, out cancellationReason),
                "Native stop-action API rejected the receipt-change control: " + cancellationReason);
            ConversationActionReply cancelledReceipt = null;
            ConversationMoodProbe.Require(ConversationActionReply.TryFromSavedAction(store, acceptedAction.Id,
                out cancelledReceipt) && cancelledReceipt.Status == ConversationActionReplyStatus.Cancelled,
                "Store did not expose the changed native cancellation receipt.");
            changedReceipt("{\"reply\":\"The old accepted action reply.\",\"moodDelta\":7}");
            RequireMood(store, evidence.PersonId, afterRephrase.Value, afterRephrase.AcceptedTurnSequence,
                "Changed action receipt callback applied stale model mood.");
            ConversationMoodProbe.Require(ConversationMoodProbe.CountExchange(store, evidence.PersonId,
                "IMM09-action-receipt-changed", cancelledReceipt.NpcText, "action") == 1 &&
                ConversationMoodProbe.CountExchange(store, evidence.PersonId,
                    "IMM09-action-receipt-changed", "The old accepted action reply.", "action") == 0,
                "Changed action receipt did not replace stale wording with the current native receipt.");
            ConversationMoodProbe.Require(CountAnyExchange(store, evidence.PersonId,
                "IMM09-action-receipt-changed", cancelledReceipt.NpcText) == 1 &&
                CountAnyExchange(store, evidence.PersonId, "IMM09-action-receipt-changed",
                    "The old accepted action reply.") == 0,
                "Changed receipt persisted stale generated prose or duplicated its fresh receipt under another source.");

            // Held closure from the closed Talk session must remain inert.
            Action<string> stale = StartControlled(host, talk, "IMM09-action-stale", client);
            yield return ConversationMoodProbe.CloseTalk(host);
            stale("{\"reply\":\"Late action wording.\",\"moodDelta\":9}");
            RequireMood(store, evidence.PersonId, afterRephrase.Value, afterRephrase.AcceptedTurnSequence,
                "Late action completion from a closed session changed mood.");
            ConversationMoodProbe.Require(ConversationMoodProbe.CountExchange(store, evidence.PersonId,
                "IMM09-action-stale", "Late action wording.", "action") == 0,
                "Late action completion from a closed session was journaled.");
            ConversationMoodProbe.Require(CountAnyExchange(store, evidence.PersonId,
                "IMM09-action-stale", "Late action wording.") == 0,
                "Late closed-session action prose was journaled under another provenance source.");
            actor = streets.FindActor(evidence.PersonId);
            if (!actor) throw new InvalidOperationException("Selected resident disappeared before same-person reopen.");
            yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
            ConversationMoodProbe.Require(Convert.ToString(ConversationMoodProbe.ReadHostField(host,
                "conversationPersonId"), CultureInfo.InvariantCulture) == evidence.PersonId,
                "Reopened Talk did not bind the same resident.");

            yield return SaveCheckpoint(game, character, evidence);
            ConversationMoodView saved = store.CaptureConversationMood(evidence.PersonId);
            yield return ConversationMoodProbe.CloseTalk(host);
            actor = streets.FindActor(evidence.PersonId);
            if (!actor) throw new InvalidOperationException("Selected resident disappeared before disk mutation.");
            yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
            Action<string> postSave = StartControlled(host, talk, "IMM09-action-post-save", client);
            postSave("{\"reply\":\"After the checkpoint.\",\"moodDelta\":4}");
            ConversationMoodView mutated = store.CaptureConversationMood(evidence.PersonId);
            ConversationMoodProbe.Require(mutated.Value == Clamp(saved.Value + 4) &&
                mutated.AcceptedTurnSequence == saved.AcceptedTurnSequence + 1,
                "Post-checkpoint action callback did not create the expected mood mutation.");
            yield return ConversationMoodProbe.CloseTalk(host);
            yield return ConversationMoodProbe.LoadSave(host, streets, character, evidence.CheckpointTitle, true);
            WorldStateStore restored = ConversationMoodProbe.ReadHostField(host, "store") as WorldStateStore;
            RequireMood(restored, evidence.PersonId, saved.Value, saved.AcceptedTurnSequence,
                "Disk reload did not restore action mood checkpoint.");
            ConversationMoodProbe.Require(ConversationMoodProbe.CountExchange(restored, evidence.PersonId,
                "IMM09-action-post-save", "After the checkpoint.", "action") == 0 &&
                ConversationMoodProbe.CountExchange(restored, evidence.PersonId, duplicateText,
                    "I have something new to add.", "action") == 1,
                "Disk reload did not roll back the post-checkpoint action exchange.");
            ConversationMoodProbe.Require(CountAnyExchange(restored, evidence.PersonId,
                "IMM09-action-post-save", "After the checkpoint.") == 0 &&
                CountAnyExchange(restored, evidence.PersonId, duplicateText,
                    "I have something new to add.") == 1,
                "Disk reload left a rolled-back row or lost the final exchange under another provenance source.");
        }

        private static Action<string> StartControlled(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, string playerText, LocalDialogueClient client,
            ConversationActionReply receipt = null)
        {
            phase = Phase.Control;
            heldCompletion = null;
            lastRequest = null;
            SubmitAction(host, talk, playerText, receipt ??
                ConversationActionReply.Unresolved("No action has been agreed."));
            Action<string> callback = heldCompletion;
            ConversationMoodProbe.Require(callback != null && lastRequest != null && lastRequest.MoodEnabled &&
                lastRequest.ActionReply != null,
                "Mood-enabled production action completion was not captured for controlled response.");
            ConversationMoodProbe.CancelTransport(host);
            return callback;
        }

        private static void SubmitAction(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            string playerText, ConversationActionReply receipt)
        {
            object snapshot = ConversationMoodProbe.CaptureSnapshot(host, talk, playerText);
            ConversationMoodProbe.RequireField(typeof(PeopleOfTheIliacBayMod), "conversationBusy").SetValue(host, false);
            MethodInfo submit = ConversationMoodProbe.RequireMethod(typeof(PeopleOfTheIliacBayMod),
                "SubmitConversationActionReply", 4);
            object accepted = submit.Invoke(host, new object[] { talk, playerText, snapshot, receipt });
            ConversationMoodProbe.Require(Convert.ToBoolean(accepted, CultureInfo.InvariantCulture),
                "Production action wording rejected the fresh snapshot and typed receipt.");
        }

        private static IEnumerator SaveCheckpoint(GameManager game, string character, Evidence evidence)
        {
            evidence.CheckpointTitle = "People - IMM09 Action Mood " + Guid.NewGuid().ToString("N").Substring(0, 8);
            game.SaveLoadManager.EnumerateSaves();
            ConversationMoodProbe.Require(game.SaveLoadManager.FindSaveFolderByNames(character,
                evidence.CheckpointTitle) < 0, "Generated action mood checkpoint already exists.");
            bool saved = false;
            SaveLoadManager.OnSaveEventHandler handler = ignored => saved = true;
            SaveLoadManager.OnSave += handler;
            try
            {
                game.SaveLoadManager.Save(character, evidence.CheckpointTitle);
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                ConversationMoodProbe.Require(saved, "Action mood checkpoint save timed out.");
                game.SaveLoadManager.EnumerateSaves();
                ConversationMoodProbe.Require(game.SaveLoadManager.FindSaveFolderByNames(character,
                    evidence.CheckpointTitle) >= 0, "Action mood checkpoint missing after save.");
            }
            finally { SaveLoadManager.OnSave -= handler; }
        }

        private static int Clamp(int value) { return Math.Max(-100, Math.Min(100, value)); }
        private static int CountAnyExchange(WorldStateStore store, string personId, string playerText, string npcText)
        {
            int count = 0;
            double cutoff = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 86400000d;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(personId, cutoff))
                if (entry != null && string.Equals(entry.PlayerText, playerText, StringComparison.Ordinal) &&
                    string.Equals(entry.NpcText, npcText, StringComparison.Ordinal)) count++;
            return count;
        }

        private static NpcActionOption FindWait(System.Collections.Generic.IEnumerable<NpcActionOption> options)
        {
            if (options == null) return null;
            foreach (NpcActionOption option in options)
                if (option != null && option.Kind == NpcActionKind.Wait) return option;
            return null;
        }
        private static string Encode(string reply, int moodDelta)
        { return "{\"reply\":\"" + reply.Replace("\\", "\\\\").Replace("\"", "\\\"") +
            "\",\"moodDelta\":" + moodDelta.ToString(CultureInfo.InvariantCulture) + "}"; }

        private static void RequireMood(WorldStateStore store, string personId, int value, long sequence, string message)
        {
            ConversationMoodProbe.Require(store != null, message + " Store is unavailable.");
            ConversationMoodView mood = store.CaptureConversationMood(personId);
            ConversationMoodProbe.Require(mood.Value == value && mood.AcceptedTurnSequence == sequence,
                message + " expected=" + value + "/" + sequence + "; actual=" + mood.Value + "/" + mood.AcceptedTurnSequence);
        }
    }
}
