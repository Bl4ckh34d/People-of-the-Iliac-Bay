using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Native acceptance for the live decision-routing stage. Direct lifecycle checks
    // use deterministic transport; production free-text uses the owned local D3 service.
    internal static partial class DecisionRoutingProbe
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string ModelId = "decision-routing-native-probe";
        private const string ProbeSwitch = "-peopleoftheiliacbay-decision-routing-probe";

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), AppraisalReopenSwitch) >= 0)
            {
                IEnumerator appraisalReopen = RunAppraisalReopen(host, streets, character);
                while (appraisalReopen.MoveNext()) yield return appraisalReopen.Current;
                yield break;
            }
            if (!host || !streets || character != OwnedCharacter || GameManager.Instance == null || !GameManager.Instance.IsReady)
                throw new InvalidOperationException("Decision routing probe requires the owned native fixture and ready game.");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), ProbeSwitch) < 0)
                throw new InvalidOperationException("Decision routing probe requires explicit command-line opt-in.");

            var storeField = RequireField(typeof(PeopleOfTheIliacBayMod), "store", InstanceMembers);
            var store = storeField.GetValue(host) as WorldStateStore;
            if (store == null || DaggerfallUI.UIManager == null)
                throw new InvalidOperationException("Decision routing probe cannot access current native conversation state.");

            // Reuse the established probe's fixture actor selection and real Talk opener.
            ResidentActor actor = null;
            IEnumerator actorWait = (IEnumerator)RequireMethod(typeof(DecisionShadowProbe), "WaitForUnusedManagedAdult", StaticMembers, 4)
                .Invoke(null, new object[] { store, (Action<ResidentActor>)(value => actor = value), false, GameManager.Instance.PlayerMotor.transform.position });
            while (actorWait.MoveNext()) yield return actorWait.Current;
            if (!actor) throw new InvalidOperationException("No unused managed adult is available in the disposable fixture.");

            var game = GameManager.Instance;
            DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToMobileNPC(actor.Person);
            DaggerfallTalkWindow talk = null;
            IEnumerator talkWait = (IEnumerator)RequireMethod(typeof(DecisionShadowProbe), "WaitForTalk", StaticMembers, 2)
                .Invoke(null, new object[] { actor, (Action<DaggerfallTalkWindow>)(value => talk = value) });
            while (talkWait.MoveNext()) yield return talkWait.Current;
            if (talk == null) throw new InvalidOperationException("Native Talk did not open for selected resident.");
            MethodInfo beginConversation = RequireMethod(typeof(PeopleOfTheIliacBayMod), "BeginGroundedConversation", InstanceMembers, 0);
            if (!(bool)RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSessionOpen", InstanceMembers).GetValue(host))
                beginConversation.Invoke(host, null);
            if (!ReferenceEquals(RequireField(typeof(PeopleOfTheIliacBayMod), "conversationActor", InstanceMembers).GetValue(host), actor))
                throw new InvalidOperationException("Native Talk did not bind selected ResidentActor.");

            // Disable normal chat traffic. Only this probe's decision transport may run.
            var textEnabled = RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogueEnabled", InstanceMembers);
            var prewarmEnabled = RequireField(typeof(PeopleOfTheIliacBayMod), "dialoguePrewarmEnabled", InstanceMembers);
            var busyField = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationBusy", InstanceMembers);
            var greetingPendingField = RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending", InstanceMembers);
            // Keep the route gate enabled. Direct TryBegin calls below bypass
            // SubmitFreeText, so no ordinary reply or model request is started.
            textEnabled.SetValue(host, true);
            prewarmEnabled.SetValue(host, false);
            busyField.SetValue(host, true);
            greetingPendingField.SetValue(host, false);
            var textClient = RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogue", InstanceMembers).GetValue(host) as LocalDialogueClient;
            if (textClient) textClient.Cancel();

            var transport = new DeterministicTransport();
            var session = new LocalDecisionSession(transport) { ExpectedModelId = ModelId, Enabled = true };
            var connection = new LocalDialogueConnection("http://127.0.0.1:1/completion", 2000);
            RequireMethod(typeof(PeopleOfTheIliacBayMod), "InstallDecisionRoutingForProbe", InstanceMembers, 2)
                .Invoke(host, new object[] { session, connection });

            MethodInfo capture = RequireMethod(typeof(PeopleOfTheIliacBayMod), "CaptureConversationContext", InstanceMembers, 3);
            MethodInfo tryBegin = RequireMethod(typeof(PeopleOfTheIliacBayMod), "TryBeginDecisionRouting", InstanceMembers, 4);
            MethodInfo captureState = RequireMethod(typeof(DecisionShadowProbe), "CaptureState", StaticMembers, 5);
            MethodInfo assertUnchanged = RequireMethod(typeof(DecisionShadowProbe), "AssertUnchanged", StaticMembers, 2);
            object beforeRouting = captureState.Invoke(null, new object[] { host, store, talk, actor, game });
            object routeResult = null;
            int callbackThread = -1;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            MethodInfo pump = RequireMethod(typeof(PeopleOfTheIliacBayMod), "UpdateDecisionShadow", InstanceMembers, 0);
            long nextRevision = checked(Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                "conversationContextRequestRevision", InstanceMembers).GetValue(host)) + 1);
            const string firstTurn = "How fares the town today?";
            var snapshot = capture.Invoke(host, new object[] { talk, firstTurn, nextRevision }) as ConversationContextSnapshot;
            if (snapshot == null) throw new InvalidOperationException("Native context capture returned no snapshot.");
            object accepted = tryBegin.Invoke(host, new object[] { snapshot, talk, firstTurn,
                (Action<string>)(value => { callbackThread = Thread.CurrentThread.ManagedThreadId; routeResult = value; }) });
            if (!(accepted is bool) || !(bool)accepted) throw new InvalidOperationException("Live decision-routing stage rejected current native Talk.");

            yield return PumpUntil(pump, host, () => transport.CompletedCalls >= 1, 5f);
            yield return PumpUntil(pump, host, () => routeResult != null, 5f);
            if (!string.Equals(routeResult as string, "SOCIAL", StringComparison.Ordinal) || callbackThread != mainThread || transport.CompletedCalls != 1)
                throw new InvalidOperationException("Live route did not deliver exactly one SOCIAL result on the main thread.");

            // A valid HTTP response with invalid JSON is a technical route failure.
            routeResult = new object();
            callbackThread = -1;
            string failureTurn = "Tell me about this place.";
            transport.FailNext = true;
            nextRevision = checked(Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                "conversationContextRequestRevision", InstanceMembers).GetValue(host)) + 1);
            snapshot = capture.Invoke(host, new object[] { talk, failureTurn, nextRevision }) as ConversationContextSnapshot;
            accepted = tryBegin.Invoke(host, new object[] { snapshot, talk, failureTurn,
                (Action<string>)(value => { callbackThread = Thread.CurrentThread.ManagedThreadId; routeResult = value; }) });
            if (!(accepted is bool) || !(bool)accepted) throw new InvalidOperationException("Live decision-routing stage rejected technical-failure fixture.");
            yield return PumpUntil(pump, host, () => routeResult == null, 5f);
            if (routeResult != null || callbackThread != mainThread || transport.CompletedCalls != 2)
                throw new InvalidOperationException("Technical route failure did not deliver exactly one null fallback on the main thread.");
            object afterRouting = captureState.Invoke(null, new object[] { host, store, talk, actor, game });
            assertUnchanged.Invoke(null, new[] { beforeRouting, afterRouting });

            MethodInfo advanceSave = RequireMethod(typeof(PeopleOfTheIliacBayMod), "AdvanceConversationSaveEpoch", InstanceMembers, 0);
            MethodInfo sceneLoaded = RequireMethod(typeof(PeopleOfTheIliacBayMod), "OnConversationSceneLoaded", InstanceMembers, 2);
            MethodInfo disabled = RequireMethod(typeof(PeopleOfTheIliacBayMod), "OnDisable", InstanceMembers, 0);
            int invalidationCallbackCount = 0;
            int expectedCall = 2;
            for (int invalidation = 0; invalidation < 4; invalidation++)
            {
                if (DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow != talk)
                {
                    DaggerfallUI.Instance.PopToHUD();
                    busyField.SetValue(host, true);
                    game.TalkManager.TalkToMobileNPC(actor.Person);
                    talk = null;
                    talkWait = (IEnumerator)RequireMethod(typeof(DecisionShadowProbe), "WaitForTalk", StaticMembers, 2)
                        .Invoke(null, new object[] { actor, (Action<DaggerfallTalkWindow>)(value => talk = value) });
                    while (talkWait.MoveNext()) yield return talkWait.Current;
                    if (!(bool)RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSessionOpen", InstanceMembers).GetValue(host))
                        beginConversation.Invoke(host, null);
                }

                string cancelTurn = "DECISION-ROUTING-CANCEL-" + invalidation.ToString();
                nextRevision = checked(Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                    "conversationContextRequestRevision", InstanceMembers).GetValue(host)) + 1);
                snapshot = capture.Invoke(host, new object[] { talk, cancelTurn, nextRevision }) as ConversationContextSnapshot;
                if (snapshot == null) throw new InvalidOperationException("Cancellation fixture context capture returned no snapshot.");
                routeResult = null;
                transport.PrepareNextBlockedCall();
                accepted = tryBegin.Invoke(host, new object[] { snapshot, talk, cancelTurn,
                    (Action<string>)(value => { invalidationCallbackCount++; routeResult = value; }) });
                if (!(accepted is bool) || !(bool)accepted) throw new InvalidOperationException("Live routing rejected cancellation fixture.");
                expectedCall++;
                float blockedDeadline = Time.realtimeSinceStartup + 5f;
                while (!transport.IsBlocked(expectedCall) && Time.realtimeSinceStartup < blockedDeadline) yield return null;
                if (!transport.IsBlocked(expectedCall)) throw new InvalidOperationException("Deterministic route transport did not block for invalidation.");

                if (invalidation == 0) DaggerfallUI.Instance.PopToHUD();
                else if (invalidation == 1) advanceSave.Invoke(host, null);
                else if (invalidation == 2) sceneLoaded.Invoke(host, new object[] { actor.gameObject.scene, UnityEngine.SceneManagement.LoadSceneMode.Single });
                else disabled.Invoke(host, null);
                float cancelDeadline = Time.realtimeSinceStartup + 3f;
                while (!transport.WasCancelled(expectedCall) && Time.realtimeSinceStartup < cancelDeadline) yield return null;
                if (!transport.WasCancelled(expectedCall)) throw new InvalidOperationException("Conversation invalidation did not cancel blocked route transport.");
                transport.ReleaseBlockedCall();
                float returnDeadline = Time.realtimeSinceStartup + 3f;
                while (!transport.HasReturned(expectedCall) && Time.realtimeSinceStartup < returnDeadline) yield return null;
                float settleDeadline = Time.realtimeSinceStartup + 0.2f;
                while (Time.realtimeSinceStartup < settleDeadline)
                {
                    pump.Invoke(host, null);
                    yield return null;
                }
                if (invalidationCallbackCount != 0) throw new InvalidOperationException("Cancelled or stale route delivered callback.");
                if (invalidation == 0)
                {
                    busyField.SetValue(host, true);
                    game.TalkManager.TalkToMobileNPC(actor.Person);
                    talk = null;
                    talkWait = (IEnumerator)RequireMethod(typeof(DecisionShadowProbe), "WaitForTalk", StaticMembers, 2)
                        .Invoke(null, new object[] { actor, (Action<DaggerfallTalkWindow>)(value => talk = value) });
                    while (talkWait.MoveNext()) yield return talkWait.Current;
                    if (!(bool)RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSessionOpen", InstanceMembers).GetValue(host))
                        beginConversation.Invoke(host, null);
                }
            }
            if (invalidationCallbackCount != 0 || transport.CanceledCalls != 4)
                throw new InvalidOperationException("Close/save/scene/disable cancellation callbacks did not remain suppressed.");

            // Exercise production SubmitFreeText through the owned local D3 service,
            // then let the selected local Qwen service refine and answer normally.
            if (DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow != talk)
                throw new InvalidOperationException("Production free-text check lost selected native Talk.");
            AssertNoAdjacentDuplicateHistoryDays(talk, "before production free-text");
            if (textClient) textClient.Cancel();
            greetingPendingField.SetValue(host, false);
            busyField.SetValue(host, false);

            string expectedModelId = ResolveD3ModelId();
            var liveTransport = new RecordingTransport(new LocalDecisionTransport());
            var liveSession = new LocalDecisionSession(liveTransport) { ExpectedModelId = expectedModelId, Enabled = true };
            var liveConnection = new LocalDialogueConnection("http://127.0.0.1:8093/completion", 10000);
            RequireMethod(typeof(PeopleOfTheIliacBayMod), "InstallDecisionRoutingForProbe", InstanceMembers, 2)
                .Invoke(host, new object[] { liveSession, liveConnection });
            const string submittedTurn = "Tell me how your day is going.";
            int transcriptBefore = TranscriptCount(talk);
            long sequenceBefore = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                "conversationContextStageSequence", InstanceMembers).GetValue(host));
            MethodInfo submitFreeText = RequireMethod(typeof(PeopleOfTheIliacBayMod), "SubmitFreeText", InstanceMembers, 2);
            int routingCallsBefore = transport.CompletedCalls;
            if (routingCallsBefore != 6)
                throw new InvalidOperationException("Deterministic route transport count changed before production free-text submission.");

            submitFreeText.Invoke(host, new object[] { talk, submittedTurn });
            string payloadTurn = ActiveRequestTurn(liveSession);
            if (!string.Equals(payloadTurn, submittedTurn, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Live D3 request did not carry exact current player turn.");
            }
            float productionDeadline = Time.realtimeSinceStartup + 120f;
            float productionStarted = Time.realtimeSinceStartup;
            bool routeCallbackCompleted = false;
            bool rememberedReply = false;
            bool productionDiagnosticLogged = false;
            string selectedRoute = null;
            while (Time.realtimeSinceStartup < productionDeadline && (!routeCallbackCompleted || !rememberedReply))
            {
                pump.Invoke(host, null);
                long sequenceNow = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                    "conversationContextStageSequence", InstanceMembers).GetValue(host));
                RecordingTransport.Result recorded = liveTransport.LastResult;
                routeCallbackCompleted = IsSuccessfulRoute(recorded, expectedModelId) &&
                    string.Equals(recorded.OriginalTurn, submittedTurn, StringComparison.Ordinal) &&
                    sequenceNow >= sequenceBefore + 2 && liveSession.CallbackFailureCount == 0;
                if (routeCallbackCompleted)
                    selectedRoute = recorded.Parsed.SelectedOptionId;
                rememberedReply = HasRememberedReply(talk, submittedTurn, transcriptBefore);
                if (!productionDiagnosticLogged && Time.realtimeSinceStartup - productionStarted >= 10f)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_PROBE_DIAGNOSTIC_10S: " +
                        DescribeProductionState(liveTransport, sequenceBefore, host, liveSession, busyField, talk, transcriptBefore));
                    productionDiagnosticLogged = true;
                }
                yield return null;
            }
            if (!routeCallbackCompleted || string.IsNullOrEmpty(selectedRoute))
            {
                string diagnostic = DescribeProductionState(liveTransport, sequenceBefore, host, liveSession, busyField, talk, transcriptBefore);
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_PROBE_FAILURE: " + diagnostic);
                throw new InvalidOperationException("Production SubmitFreeText did not complete a valid live D3 route callback before Qwen refinement. " + diagnostic);
            }
            if (!rememberedReply)
                throw new InvalidOperationException("Live Qwen refinement did not append a remembered current reply within 120 seconds.");
            AssertNoAdjacentDuplicateHistoryDays(talk, "after production reply");
            if (transport.CompletedCalls != routingCallsBefore)
                throw new InvalidOperationException("Production route bypassed live D3 and used deterministic probe transport.");

            RequireMethod(typeof(PeopleOfTheIliacBayMod), "CancelDecisionShadow", InstanceMembers, 0).Invoke(host, null);
            session.Dispose();
            liveSession.Dispose();
            DaggerfallUI.Instance.PopToHUD();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_NATIVE_PASS: live-stage=true; success=SOCIAL; technical-failure=null; callbacks=2; main-thread=true; cancelled=4; invalidations=close-save-scene-disable; archive-dialogue-action-state-unchanged=true; submit-free-text=true; exact-turn=true; route-callback-before-qwen=true; qwen-reply=remembered; selected-route=" + selectedRoute);
        }

        private static string ResolveD3ModelId()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int modelArgument = Array.IndexOf(arguments, "-peopleoftheiliacbay-decision-model");
            string selected = modelArgument >= 0 && modelArgument + 1 < arguments.Length
                ? arguments[modelArgument + 1] : null;
            string path = Path.GetFullPath(string.IsNullOrWhiteSpace(selected)
                ? Path.Combine(Application.dataPath, "../../local-llm/models/d3-mini/vllm-d3-mini-Q4_K_M.gguf")
                : selected);
            if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".gguf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Owned selected D3 model fixture is missing.");
            return path;
        }

        private static string ActiveRequestTurn(LocalDecisionSession session)
        {
            FieldInfo activeField = RequireField(typeof(LocalDecisionSession), "active", InstanceMembers);
            object active = activeField.GetValue(session);
            if (active == null) throw new InvalidOperationException("Live D3 request did not remain active for wire-turn inspection.");
            object request = RequireField(active.GetType(), "Request", InstanceMembers).GetValue(active);
            if (request == null) throw new InvalidOperationException("Live D3 active request has no immutable decision request.");
            return request.GetType().GetProperty("OriginalTurn", InstanceMembers)?.GetValue(request, null) as string;
        }

        private static bool IsSuccessfulRoute(RecordingTransport.Result result, string expectedModelId)
        {
            if (result == null || result.StatusCode != 200 || result.TransportFailure != NpcDecisionFailureKind.None ||
                result.Parsed == null || !result.Parsed.IsValid || result.Parsed.FailureKind != NpcDecisionFailureKind.None ||
                !string.Equals(result.Parsed.ModelId, expectedModelId, StringComparison.Ordinal)) return false;
            return result.OfferedRoutes != null && result.OfferedRoutes.Contains(result.Parsed.SelectedOptionId, StringComparer.Ordinal) &&
                new[] { "SOCIAL", "ACTION_REQUEST", "OFFER_RESPONSE", "COMMITMENT_UPDATE" }
                    .Contains(result.Parsed.SelectedOptionId, StringComparer.Ordinal);
        }

        private static int TranscriptCount(DaggerfallTalkWindow talk)
        {
            var field = typeof(DaggerfallTalkWindow).GetField("listboxConversation", InstanceMembers);
            var transcript = field == null ? null : field.GetValue(talk) as DaggerfallWorkshop.Game.UserInterface.ListBox;
            if (transcript == null) throw new InvalidOperationException("Native Talk transcript is unavailable.");
            return transcript.Count;
        }

        private static void AssertNoAdjacentDuplicateHistoryDays(DaggerfallTalkWindow talk, string phase)
        {
            var field = typeof(DaggerfallTalkWindow).GetField("listboxConversation", InstanceMembers);
            var transcript = field == null ? null : field.GetValue(talk) as ListBox;
            if (transcript == null) throw new InvalidOperationException("Native Talk transcript is unavailable for history-date verification.");
            string previousDay = null;
            bool previousWasDay = false;
            for (int index = 0; index < transcript.Count; index++)
            {
                var row = transcript.GetItem(index);
                bool isDay = row != null && row.textLabel != null && row.textLabel.Name == "PeopleOfTheIliacBayHistoryDay";
                string day = isDay ? row.tag as string ?? row.textLabel.Text : null;
                if (isDay && previousWasDay && string.Equals(previousDay, day, StringComparison.Ordinal))
                    throw new InvalidOperationException("Adjacent duplicate history day rows remain " + phase + ": " + day);
                previousWasDay = isDay;
                previousDay = day;
            }
        }

        private static string DescribeProductionState(RecordingTransport recording, long stageBefore,
            PeopleOfTheIliacBayMod host, LocalDecisionSession liveSession, FieldInfo busyField,
            DaggerfallTalkWindow talk, int transcriptBefore)
        {
            long stageNow = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                "conversationContextStageSequence", InstanceMembers).GetValue(host));
            int transcriptNow = TranscriptCount(talk);
            RecordingTransport.Result result = recording.LastResult;
            string recorded = result == null ? "<none>" : "status=" + (result.StatusCode.HasValue ? result.StatusCode.Value.ToString() : "<none>") +
                "; transportFailure=" + result.TransportFailure + "; parsedValid=" + (result.Parsed != null && result.Parsed.IsValid) +
                "; model=" + (result.Parsed == null ? "<none>" : result.Parsed.ModelId ?? "<none>") +
                "; route=" + (result.Parsed == null ? "<none>" : result.Parsed.SelectedOptionId ?? "<none>") +
                "; parseFailure=" + (result.Parsed == null ? "<none>" : result.Parsed.FailureKind + "/" + result.Parsed.DiagnosticCode) +
                "; transportTurn=" + (result.OriginalTurn ?? "<none>");
            return "recordedResponse=" + recorded +
                "; stageBefore=" + stageBefore + "; stageNow=" + stageNow +
                "; callbackFailures=" + liveSession.CallbackFailureCount +
                "; conversationBusy=" + Convert.ToBoolean(busyField.GetValue(host)) +
                "; transcriptBefore=" + transcriptBefore + "; transcriptNow=" + transcriptNow +
                "; latestRows=" + DescribeLatestTranscriptRows(talk, 4);
        }

        private static string DescribeLatestTranscriptRows(DaggerfallTalkWindow talk, int maximumRows)
        {
            var field = typeof(DaggerfallTalkWindow).GetField("listboxConversation", InstanceMembers);
            var transcript = field == null ? null : field.GetValue(talk) as DaggerfallWorkshop.Game.UserInterface.ListBox;
            if (transcript == null) return "<unavailable>";
            var rows = new List<string>();
            int start = Math.Max(0, transcript.Count - maximumRows);
            for (int index = start; index < transcript.Count; index++)
            {
                var item = transcript.GetItem(index);
                string text = item == null || item.textLabel == null ? "" : item.textLabel.Text ?? "";
                text = text.Replace("\r", " ").Replace("\n", " ");
                if (text.Length > 220) text = text.Substring(0, 220);
                string name = item == null || item.textLabel == null ? "" : item.textLabel.Name ?? "";
                rows.Add("[" + index + ",color=" + (item == null ? "" : item.textColor.ToString()) +
                    ",name=" + name + ",text=" + text + "]");
            }
            return string.Join(" ", rows.ToArray());
        }

        private static bool HasRememberedReply(DaggerfallTalkWindow talk, string question, int firstNewRow)
        {
            var field = typeof(DaggerfallTalkWindow).GetField("listboxConversation", InstanceMembers);
            var transcript = field == null ? null : field.GetValue(talk) as DaggerfallWorkshop.Game.UserInterface.ListBox;
            if (transcript == null) return false;
            for (int index = Math.Max(0, firstNewRow); index + 1 < transcript.Count; index++)
            {
                var player = transcript.GetItem(index);
                var reply = transcript.GetItem(index + 1);
                if (player == null || player.textLabel == null || reply == null || reply.textLabel == null) continue;
                if (player.textLabel.Name == "PeopleOfTheIliacBayPendingEphemeral" ||
                    reply.textLabel.Name == "PeopleOfTheIliacBayPendingEphemeral") continue;
                if (player.textColor != DaggerfallUI.DaggerfallQuestionTextColor ||
                    !string.Equals(player.textLabel.Text, question, StringComparison.Ordinal)) continue;
                string answer = reply.textLabel.Text;
                return reply.textColor != DaggerfallUI.DaggerfallQuestionTextColor &&
                    reply.textLabel.Name != "PeopleOfTheIliacBayUnavailableReply" &&
                    !string.IsNullOrWhiteSpace(answer) && answer != DialogueReplyFallback.UnavailableReply;
            }
            return false;
        }

        private static IEnumerator PumpUntil(MethodInfo pump, PeopleOfTheIliacBayMod host, Func<bool> done, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                pump.Invoke(host, null);
                yield return null;
            }
            pump.Invoke(host, null);
        }

        private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags, int count)
        {
            MethodInfo method = type.GetMethods(flags).FirstOrDefault(value => value.Name == name && value.GetParameters().Length == count);
            if (method == null) throw new InvalidOperationException("Required decision-routing probe method missing: " + name);
            return method;
        }

        private static FieldInfo RequireField(Type type, string name, BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null) throw new InvalidOperationException("Required decision-routing probe field missing: " + name);
            return field;
        }

        private sealed class DeterministicTransport : ILocalDecisionTransport
        {
            private int calls;
            private int completed;
            private int blockedCall;
            private int cancelledCallCount;
            private int returned;
            private readonly ManualResetEventSlim release = new ManualResetEventSlim(false);
            private volatile string lastOriginalTurn;
            public volatile bool FailNext;
            private volatile bool blockNext;
            public int CompletedCalls => Volatile.Read(ref completed);
            public int CanceledCalls => Volatile.Read(ref cancelledCallCount);
            public bool IsBlocked(int call) => Volatile.Read(ref blockedCall) == call;
            public bool WasCancelled(int call) => Volatile.Read(ref cancelledCallCount) == call - 2;
            public bool HasReturned(int call) => Volatile.Read(ref returned) >= call;
            public string LastOriginalTurn => lastOriginalTurn;
            public void PrepareNextBlockedCall() { release.Reset(); blockNext = true; }
            public void ReleaseBlockedCall() => release.Set();

            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                int call = Interlocked.Increment(ref calls);
                if (blockNext)
                {
                    blockNext = false;
                    Volatile.Write(ref blockedCall, call);
                    WaitHandle.WaitAny(new[] { release.WaitHandle, cancellationToken.WaitHandle }, timeoutMilliseconds);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref cancelledCallCount);
                        Interlocked.Increment(ref returned);
                        Interlocked.Increment(ref completed);
                        return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "cancelled");
                    }
                }
                if (FailNext)
                {
                    FailNext = false;
                    Interlocked.Increment(ref completed);
                    Interlocked.Increment(ref returned);
                    return new LocalDecisionHttpResponse(200, "not-json", NpcDecisionFailureKind.None);
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Increment(ref completed);
                    Interlocked.Increment(ref returned);
                    return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "cancelled");
                }
                string payload = new UTF8Encoding(false, true).GetString(bytes);
                FullSerializer.fsData root = FullSerializer.fsJsonParser.Parse(payload);
                FullSerializer.fsData state = FullSerializer.fsJsonParser.Parse(root.AsDictionary["state"].AsString);
                lastOriginalTurn = state.AsDictionary["originalTurn"].AsString;
                var criteria = root.AsDictionary["questions"].AsDictionary["decision"].AsDictionary["criteria"].AsDictionary;
                var body = new StringBuilder("{\"model\":\"");
                body.Append(ModelId).Append("\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"SOCIAL\",\"probabilities\":{");
                bool first = true;
                foreach (string option in criteria.Keys.OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (!first) body.Append(',');
                    first = false;
                    body.Append('"').Append(option).Append("\":").Append(option == "SOCIAL" ? '1' : '0');
                }
                body.Append("}}}}");
                Interlocked.Increment(ref completed);
                Interlocked.Increment(ref returned);
                return new LocalDecisionHttpResponse(200, body.ToString(), NpcDecisionFailureKind.None);
            }
        }

        private sealed class RecordingTransport : ILocalDecisionTransport
        {
            internal sealed class Result
            {
                public readonly int? StatusCode;
                public readonly NpcDecisionFailureKind TransportFailure;
                public readonly LocalDecisionParsedReply Parsed;
                public readonly string OriginalTurn;
                public readonly string[] OfferedRoutes;
                public readonly string RequestJson;
                public readonly string ResponseBody;

                public Result(LocalDecisionHttpResponse response, LocalDecisionParsedReply parsed, string originalTurn,
                    string[] offeredRoutes, string requestJson)
                {
                    StatusCode = response == null ? (int?)null : response.StatusCode;
                    TransportFailure = response == null ? NpcDecisionFailureKind.Transport : response.FailureKind;
                    Parsed = parsed;
                    OriginalTurn = originalTurn;
                    OfferedRoutes = offeredRoutes;
                    RequestJson = requestJson;
                    ResponseBody = response == null ? null : response.Body;
                }
            }

            private readonly ILocalDecisionTransport inner;
            private readonly object sync = new object();
            private Result lastResult;
            private int completedCalls;

            public RecordingTransport(ILocalDecisionTransport inner)
            {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public Result LastResult
            {
                get { lock (sync) return lastResult; }
            }

            public int CompletedCalls => Volatile.Read(ref completedCalls);

            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds,
                CancellationToken cancellationToken)
            {
                ReadRequestDetails(bytes, out string originalTurn, out string[] offeredRoutes);
                string requestJson = new UTF8Encoding(false, true).GetString(bytes);
                LocalDecisionHttpResponse response = inner.Send(endpoint, bytes, timeoutMilliseconds, cancellationToken);
                LocalDecisionParsedReply parsed = response != null && response.FailureKind == NpcDecisionFailureKind.None &&
                    response.StatusCode == 200 ? LocalDecisionWire.Parse(response.Body) : null;
                lock (sync) lastResult = new Result(response, parsed, originalTurn, offeredRoutes, requestJson);
                Interlocked.Increment(ref completedCalls);
                return response;
            }

            private static void ReadRequestDetails(byte[] bytes, out string originalTurn, out string[] offeredRoutes)
            {
                originalTurn = null;
                offeredRoutes = null;
                try
                {
                    string payload = new UTF8Encoding(false, true).GetString(bytes);
                    FullSerializer.fsData root = FullSerializer.fsJsonParser.Parse(payload);
                    FullSerializer.fsData state = FullSerializer.fsJsonParser.Parse(root.AsDictionary["state"].AsString);
                    originalTurn = state.AsDictionary["originalTurn"].AsString;
                    FullSerializer.fsData criteria = root.AsDictionary["questions"].AsDictionary["decision"].AsDictionary["criteria"];
                    offeredRoutes = criteria.AsDictionary.Keys.ToArray();
                }
                catch { }
            }
        }
    }
}
