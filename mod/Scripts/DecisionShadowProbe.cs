using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeopleOfTheIliacBay.DFU
{
    // Native capture-to-shadow acceptance using a deterministic in-process transport.
    // It never sends model traffic, appends dialogue, or dispatches a native action.
    internal static class DecisionShadowProbe
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string TestModelId = "decision-shadow-native-probe";
        private const string FirstTurn = "DECISION-SHADOW-NATIVE-CAPTURE-PROBE";
        private const string CaptureDirectorySwitch = "-peopleoftheiliacbay-decision-shadow-capture-directory";
        private const string WaitCandidateCaptureSwitch = "-peopleoftheiliacbay-decision-shadow-wait-candidate-capture";
        private const string RoutePurpose = LocalDecisionRouting.Purpose;
        private const string RouteContextProtocol = "native-speech-act-route-context-v2";

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            if (!host || !streets || character != OwnedCharacter || GameManager.Instance == null || !GameManager.Instance.IsReady)
                throw new InvalidOperationException("Decision shadow probe requires the owned native fixture and ready game.");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-decision-shadow-probe") < 0)
                throw new InvalidOperationException("Decision shadow probe requires explicit command-line opt-in.");
            string captureDirectory = ReadCaptureDirectory(Environment.GetCommandLineArgs());
            bool captureWaitCandidate = Array.IndexOf(Environment.GetCommandLineArgs(), WaitCandidateCaptureSwitch) >= 0;
            if (captureWaitCandidate && captureDirectory == null)
                throw new InvalidOperationException("Native Wait candidate capture requires a capture directory.");
            if (captureDirectory != null)
            {
                PrepareCaptureDirectory(captureDirectory, captureWaitCandidate);
                WriteRouteVocabulary(captureDirectory);
            }

            var storeField = RequireField(typeof(PeopleOfTheIliacBayMod), "store", InstanceMembers);
            var store = storeField.GetValue(host) as WorldStateStore;
            if (store == null || DaggerfallUI.UIManager == null)
                throw new InvalidOperationException("Decision shadow probe cannot access current native conversation state.");
            var game = GameManager.Instance;
            var manager = game.TalkManager;
            Vector3 originalPlayerPosition = game.PlayerMotor.transform.position;
            Quaternion originalPlayerRotation = game.PlayerMotor.transform.rotation;
            bool playerRelocated = false;
            ResidentActor actor = null;
            yield return WaitForUnusedManagedAdult(store, value => actor = value, captureWaitCandidate, originalPlayerPosition);
            if (!actor) throw new InvalidOperationException("No unused managed adult is available in the disposable fixture.");

            MethodInfo capture = RequireMethod(typeof(PeopleOfTheIliacBayMod), "CaptureConversationContext", InstanceMembers, 3);
            MethodInfo beginConversation = RequireMethod(typeof(PeopleOfTheIliacBayMod), "BeginGroundedConversation", InstanceMembers, 0);
            MethodInfo observe = RequireMethod(typeof(PeopleOfTheIliacBayMod), "ObserveDecisionShadow", InstanceMembers, 3);
            MethodInfo update = RequireMethod(typeof(PeopleOfTheIliacBayMod), "UpdateDecisionShadow", InstanceMembers, 0);
            MethodInfo install = RequireMethod(typeof(PeopleOfTheIliacBayMod), "InstallDecisionShadowForProbe", InstanceMembers, 2);
            MethodInfo current = RequireMethod(typeof(PeopleOfTheIliacBayMod), "IsCurrentConversationContext", InstanceMembers, 2);
            MethodInfo advanceSaveEpoch = RequireMethod(typeof(PeopleOfTheIliacBayMod), "AdvanceConversationSaveEpoch", InstanceMembers, 0);
            MethodInfo sceneLoaded = RequireMethod(typeof(PeopleOfTheIliacBayMod), "OnConversationSceneLoaded", InstanceMembers, 2);
            MethodInfo dispose = RequireMethod(typeof(PeopleOfTheIliacBayMod), "DisposeDecisionShadow", InstanceMembers, 0);

            var localDialogueEnabled = RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogueEnabled", InstanceMembers);
            var prewarmEnabled = RequireField(typeof(PeopleOfTheIliacBayMod), "dialoguePrewarmEnabled", InstanceMembers);
            var dialogueField = RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogue", InstanceMembers);
            // This dedicated fixture process must not issue ordinary dialogue or prewarm requests.
            localDialogueEnabled.SetValue(host, false);
            prewarmEnabled.SetValue(host, false);
            var localDialogue = dialogueField.GetValue(host) as LocalDialogueClient;
            if (localDialogue) localDialogue.Cancel();

            var transport = new DeterministicTransport(captureDirectory);
            var session = new LocalDecisionSession(transport);
            session.ExpectedModelId = TestModelId;
            session.Enabled = true;
            var observations = new List<LocalDecisionShadowObservation>();
            int callbackThread = -1;
            int creationThread = Thread.CurrentThread.ManagedThreadId;
            var shadow = new LocalDecisionShadow(session,
                new LocalDialogueConnection("http://127.0.0.1:1/completion", 2000),
                value => { callbackThread = Thread.CurrentThread.ManagedThreadId; observations.Add(value); });
            bool installed = false;
            DaggerfallTalkWindow talk = null;
            ConversationContextSnapshot firstSnapshot = null;
            NpcActionOption capturedWaitCandidate = null;
            string candidateCaptureTimeUtc = null;

            try
            {
                if (captureWaitCandidate && Vector3.Distance(actor.transform.position - Vector3.up, game.PlayerMotor.transform.position - Vector3.up) > 8f)
                {
                    // The owned fixture may be repositioned only to make an actual native candidate observable.
                    game.PlayerMotor.transform.position = actor.transform.position;
                    Physics.SyncTransforms();
                    playerRelocated = true;
                }
                DaggerfallUI.Instance.PopToHUD();
                manager.TalkToMobileNPC(actor.Person);
                yield return WaitForTalk(actor, value => talk = value);
                if (talk == null) throw new InvalidOperationException("Native Talk did not open for selected resident.");

                // Native OnWindowChange binds conversationActor. Begin only if the ordinary host did not.
                if (!(bool)RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSessionOpen", InstanceMembers).GetValue(host))
                    beginConversation.Invoke(host, null);
                if (!ReferenceEquals(RequireField(typeof(PeopleOfTheIliacBayMod), "conversationActor", InstanceMembers).GetValue(host), actor))
                    throw new InvalidOperationException("Native Talk did not bind the selected ResidentActor.");

                // Capture the real, range-gated native candidate before the baseline and snapshot.
                // Do not synthesize a target if the loaded scene does not offer exactly one Wait.
                if (captureWaitCandidate)
                {
                    List<NpcActionOption> candidates = streets.ConversationActionCandidates(actor.Resident.Id);
                    NpcActionOption[] waits = candidates.Where(value => value != null && value.Kind == NpcActionKind.Wait).ToArray();
                    if (waits.Length != 1) throw new InvalidOperationException("Native candidate capture requires exactly one actual Wait candidate.");
                    NpcActionContract.ValidateTarget(waits[0].Target);
                    capturedWaitCandidate = waits[0];
                    candidateCaptureTimeUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                }
                firstSnapshot = CaptureNext(host, talk, capture, FirstTurn);
                if (!(bool)current.Invoke(host, new object[] { firstSnapshot, talk }))
                    throw new InvalidOperationException("Captured native snapshot is not current before shadow routing.");
                ProbeState before = CaptureState(host, store, talk, actor, game);

                // The production observer shares free-text eligibility. Harness input is disabled,
                // and prewarm stays off, so this enables only the explicit injected shadow path.
                localDialogueEnabled.SetValue(host, true);
                install.Invoke(host, new object[] { session, shadow });
                installed = true;
                observe.Invoke(host, new object[] { firstSnapshot, talk, FirstTurn });
                yield return PumpUntil(host, update, () => observations.Count == 1, 5f);
                if (observations.Count != 1) throw new InvalidOperationException("Deterministic shadow response did not reach exactly one callback.");

                if (captureWaitCandidate)
                {
                    if (capturedWaitCandidate == null || string.IsNullOrWhiteSpace(transport.FirstPayloadSha256))
                        throw new InvalidOperationException("Wait candidate capture is not bound to a successfully sent first request.");
                    WriteNativeWaitCandidate(captureDirectory, capturedWaitCandidate, firstSnapshot, candidateCaptureTimeUtc,
                        transport.FirstPayloadSha256, playerRelocated);
                }

                LocalDecisionShadowObservation result = observations[0];
                if (result.Status != NpcDecisionStatus.Ok || result.SelectedOptionId != "SOCIAL" ||
                    result.ModelId != TestModelId || !result.IsShadow || result.NativeDispatchAttempted ||
                    callbackThread != creationThread ||
                    !result.Binding.HasSameRequestBinding(firstSnapshot.Binding) ||
                    result.RequestSha256 == null || transport.Count != 1 || transport.LastStatus != 200 ||
                    transport.LastEndpoint == null || transport.LastEndpoint.AbsolutePath != "/v1/systemone" ||
                    transport.LastPayloadText == null || !transport.LastPayloadText.Contains(FirstTurn))
                    throw new InvalidOperationException("Shadow result or deterministic request metadata did not match the native capture.");
                ProbeState after = CaptureState(host, store, talk, actor, game);
                AssertUnchanged(before, after);

                // A synthetic scene revision exercises live scene cancellation without loading or mutating a scene.
                ConversationContextSnapshot sceneSnapshot = CaptureNext(host, talk, capture, FirstTurn + "-SCENE");
                transport.PrepareNextBlockedCall();
                observe.Invoke(host, new object[] { sceneSnapshot, talk, FirstTurn + "-SCENE" });
                update.Invoke(host, null);
                yield return WaitForBlockedCall(transport, 2, 4f);
                if (!transport.IsBlocked(2)) throw new InvalidOperationException("Second deterministic request did not enter blocked transport.");
                Scene scene = actor.gameObject.scene;
                sceneLoaded.Invoke(host, new object[] { scene, LoadSceneMode.Single });
                yield return WaitForCancellation(transport, 2, 3f);
                if (!transport.WasCancelled(2) || observations.Count != 1 ||
                    (bool)current.Invoke(host, new object[] { sceneSnapshot, talk }))
                    throw new InvalidOperationException("Scene revision failed to cancel and stale the pending shadow request.");
                transport.ReleaseBlockedCall();
                yield return WaitForReturn(transport, 2, 3f);

                // Save epoch rotation is in-memory only here; it must cancel pending work and stale prior snapshot.
                ConversationContextSnapshot saveSnapshot = CaptureNext(host, talk, capture, FirstTurn + "-SAVE");
                transport.PrepareNextBlockedCall();
                observe.Invoke(host, new object[] { saveSnapshot, talk, FirstTurn + "-SAVE" });
                update.Invoke(host, null);
                yield return WaitForBlockedCall(transport, 3, 4f);
                if (!transport.IsBlocked(3)) throw new InvalidOperationException("Third deterministic request did not enter blocked transport.");
                advanceSaveEpoch.Invoke(host, null);
                yield return WaitForCancellation(transport, 3, 3f);
                if (!transport.WasCancelled(3) || observations.Count != 1 ||
                    (bool)current.Invoke(host, new object[] { saveSnapshot, talk }))
                    throw new InvalidOperationException("Save epoch failed to cancel and stale the pending shadow request.");
                transport.ReleaseBlockedCall();
                yield return WaitForReturn(transport, 3, 3f);

                // Closing native Talk must cancel pending shadow work; reopening creates a new session binding.
                ConversationContextSnapshot closeSnapshot = CaptureNext(host, talk, capture, FirstTurn + "-CLOSE");
                transport.PrepareNextBlockedCall();
                observe.Invoke(host, new object[] { closeSnapshot, talk, FirstTurn + "-CLOSE" });
                update.Invoke(host, null);
                yield return WaitForBlockedCall(transport, 4, 4f);
                if (!transport.IsBlocked(4)) throw new InvalidOperationException("Fourth deterministic request did not enter blocked transport.");
                DaggerfallUI.Instance.PopToHUD();
                talk = null;
                yield return WaitForCancellation(transport, 4, 3f);
                if (!transport.WasCancelled(4) || observations.Count != 1)
                    throw new InvalidOperationException("Closing Talk did not cancel pending shadow callback.");
                transport.ReleaseBlockedCall();
                yield return WaitForReturn(transport, 4, 3f);

                // Do not let the reopened fixture issue a normal returning-greeting completion.
                localDialogueEnabled.SetValue(host, false);
                manager.TalkToMobileNPC(actor.Person);
                yield return WaitForTalk(actor, value => talk = value);
                if (talk == null) throw new InvalidOperationException("Native Talk did not reopen for the same resident.");
                if (!(bool)RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSessionOpen", InstanceMembers).GetValue(host))
                    beginConversation.Invoke(host, null);
                ConversationContextSnapshot reopened = CaptureNext(host, talk, capture, FirstTurn + "-REOPEN");
                if (string.Equals(firstSnapshot.Binding.SessionId, reopened.Binding.SessionId, StringComparison.Ordinal) ||
                    !(bool)current.Invoke(host, new object[] { reopened, talk }))
                    throw new InvalidOperationException("Reopened Talk reused old session binding or failed current-context check.");

                // Exercise the actual SubmitFreeText production hook with its native clock fast path.
                // Clock replies are local, deterministic and do not invoke the dialogue model.
                // This intentionally changes the transcript once; only the shadow call must be inert.
                RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending", InstanceMembers).SetValue(host, false);
                localDialogueEnabled.SetValue(host, true);
                var submit = RequireMethod(typeof(PeopleOfTheIliacBayMod), "SubmitFreeText", InstanceMembers, 2);
                long priorRevision = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                    "conversationContextRequestRevision", InstanceMembers).GetValue(host), CultureInfo.InvariantCulture);
                long priorSubmitted = shadow.SubmittedCount;
                string clockQuestion = "What time is it?";
                ProbeState beforeClock = CaptureState(host, store, talk, actor, game);
                submit.Invoke(host, new object[] { talk, clockQuestion });
                long submittedRevision = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                    "conversationContextRequestRevision", InstanceMembers).GetValue(host), CultureInfo.InvariantCulture);
                if (submittedRevision != checked(priorRevision + 1) || shadow.SubmittedCount != priorSubmitted)
                    throw new InvalidOperationException("Native SubmitFreeText did not capture exactly one new turn before shadow pumping.");
                ProbeState afterClock = CaptureState(host, store, talk, actor, game);
                if (afterClock.Transcript == beforeClock.Transcript || !afterClock.Transcript.Contains(clockQuestion))
                    throw new InvalidOperationException("Native clock fast path did not append its expected player turn.");
                var transcriptField = typeof(DaggerfallTalkWindow).GetField("listboxConversation", InstanceMembers);
                var transcript = transcriptField == null ? null : transcriptField.GetValue(talk) as ListBox;
                bool clockAnswerFound = false;
                if (transcript != null)
                {
                    for (int index = 0; index < transcript.Count; index++)
                    {
                        var row = transcript.GetItem(index);
                        if (row != null && row.textLabel != null && row.textLabel.Text == clockQuestion &&
                            index + 1 < transcript.Count)
                        {
                            var answer = transcript.GetItem(index + 1);
                            clockAnswerFound = answer != null && answer.textLabel != null &&
                                !string.IsNullOrWhiteSpace(answer.textLabel.Text);
                            break;
                        }
                    }
                }
                if (!clockAnswerFound) throw new InvalidOperationException("Native clock fast path did not append a reply.");
                yield return PumpUntil(host, update, () => observations.Count == 2, 5f);
                if (observations.Count != 2 || shadow.SubmittedCount != priorSubmitted + 1 || transport.Count != 5)
                    throw new InvalidOperationException("Production SubmitFreeText did not submit exactly one bounded shadow request.");
                LocalDecisionShadowObservation clockResult = observations[1];
                if (clockResult.Status != NpcDecisionStatus.Ok || clockResult.SelectedOptionId != "SOCIAL" ||
                    clockResult.ModelId != TestModelId || clockResult.Binding.RequestRevision != submittedRevision ||
                    !string.Equals(clockResult.Binding.SessionId, reopened.Binding.SessionId, StringComparison.Ordinal) ||
                    !string.Equals(clockResult.Binding.PersonId, reopened.Binding.PersonId, StringComparison.Ordinal) ||
                    clockResult.Binding.ActorCategory != reopened.Binding.ActorCategory || clockResult.RequestSha256 == null ||
                    transport.LastStatus != 200 || transport.LastEndpoint == null ||
                    transport.LastEndpoint.AbsolutePath != "/v1/systemone" || transport.LastPayloadText == null ||
                    !transport.LastPayloadText.Contains(clockQuestion))
                    throw new InvalidOperationException("Production SubmitFreeText shadow metadata did not match its captured native turn.");
                ProbeState afterShadowClock = CaptureState(host, store, talk, actor, game);
                AssertUnchanged(afterClock, afterShadowClock);

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_SHADOW_NATIVE_PASS: real-native-snapshot=true; synthetic-turn=true; production-submit-clock-fast-path=true; deterministic-transport=true; callbacks=2; archive-disposition-position-transcript-actions-unchanged-during-shadow=true; scene-save-close-cancelled=true; reopen-session-rotated=true; native-wait-candidate=" + (captureWaitCandidate ? "captured; player-relocated=" + playerRelocated.ToString().ToLowerInvariant() : "disabled") + "; model-service=false; native-dispatch=false.");
            }
            finally
            {
                try { transport.ReleaseBlockedCall(); }
                catch (ObjectDisposedException) { }
                if (DaggerfallUI.UIManager != null && DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
                    DaggerfallUI.Instance.PopToHUD();
                if (playerRelocated && game != null && game.PlayerMotor != null)
                {
                    game.PlayerMotor.transform.position = originalPlayerPosition;
                    game.PlayerMotor.transform.rotation = originalPlayerRotation;
                    Physics.SyncTransforms();
                }
                if (installed)
                {
                    try { dispose.Invoke(host, null); }
                    catch (Exception) { shadow.Dispose(); }
                }
                else
                {
                    shadow.Dispose();
                    session.Dispose();
                }
                // Keep model dialogue and prewarm disabled in this dedicated fixture process.
                localDialogueEnabled.SetValue(host, false);
                prewarmEnabled.SetValue(host, false);
            }
        }

        private static ConversationContextSnapshot CaptureNext(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            MethodInfo capture, string playerText)
        {
            long revision = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                "conversationContextRequestRevision", InstanceMembers).GetValue(host));
            object value = capture.Invoke(host, new object[] { talk, playerText, checked(revision + 1) });
            var snapshot = value as ConversationContextSnapshot;
            if (snapshot == null) throw new InvalidOperationException("Native context capture returned no snapshot.");
            return snapshot;
        }

        private static IEnumerator WaitForTalk(ResidentActor actor, Action<DaggerfallTalkWindow> assign)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if (talk != null && talk.IsSetup && GameManager.Instance.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                    GameManager.Instance.TalkManager.MobileNPC == actor.Person)
                {
                    assign(talk);
                    yield break;
                }
                yield return null;
            }
            throw new InvalidOperationException("Native Talk setup timed out.");
        }

        private static IEnumerator WaitForUnusedManagedAdult(WorldStateStore store, Action<ResidentActor> assign,
            bool preferNearby, Vector3 playerPosition)
        {
            float deadline = Time.realtimeSinceStartup + 45f;
            while (Time.realtimeSinceStartup < deadline)
            {
                ResidentActor actor = FindUnusedManagedAdult(store, preferNearby, playerPosition);
                if (actor)
                {
                    assign(actor);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            throw new InvalidOperationException("No unused managed adult appeared in the disposable fixture within 45 seconds.");
        }

        private static IEnumerator PumpUntil(PeopleOfTheIliacBayMod host, MethodInfo update, Func<bool> done, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                update.Invoke(host, null);
                yield return null;
            }
            update.Invoke(host, null);
        }

        private static IEnumerator WaitForBlockedCall(DeterministicTransport transport, int call, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!transport.IsBlocked(call) && Time.realtimeSinceStartup < deadline) yield return null;
        }

        private static IEnumerator WaitForCancellation(DeterministicTransport transport, int call, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!transport.WasCancelled(call) && Time.realtimeSinceStartup < deadline) yield return null;
        }

        private static IEnumerator WaitForReturn(DeterministicTransport transport, int call, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!transport.HasReturned(call) && Time.realtimeSinceStartup < deadline) yield return null;
        }

        private static ResidentActor FindUnusedManagedAdult(WorldStateStore store, bool preferNearby, Vector3 playerPosition)
        {
            ResidentActor nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!candidate || !candidate.Person || candidate.Resident == null || !candidate.Resident.IsAlive ||
                    candidate.Resident.AgeGroup != ResidentAgeGroup.Adult || candidate.IsSleeping || candidate.IsConversing)
                    continue;
                if (store.ConversationTranscript(candidate.Resident.Id, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).Count == 0)
                {
                    if (!preferNearby) return candidate;
                    float distance = Vector3.Distance(candidate.transform.position - Vector3.up, playerPosition - Vector3.up);
                    if (distance < nearestDistance) { nearest = candidate; nearestDistance = distance; }
                }
            }
            return nearest;
        }

        private static string ReadCaptureDirectory(string[] arguments)
        {
            int found = -1;
            for (int index = 0; index < arguments.Length; index++)
            {
                if (!string.Equals(arguments[index], CaptureDirectorySwitch, StringComparison.OrdinalIgnoreCase)) continue;
                if (found >= 0) throw new InvalidOperationException("Decision shadow capture directory was specified more than once.");
                found = index;
            }
            if (found < 0) return null;
            if (found + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[found + 1]))
                throw new InvalidOperationException("Decision shadow capture directory requires an absolute path.");
            string path = arguments[found + 1];
            if (!Path.IsPathRooted(path)) throw new InvalidOperationException("Decision shadow capture directory must be absolute.");
            return Path.GetFullPath(path);
        }

        private static void PrepareCaptureDirectory(string path, bool includeWaitCandidate)
        {
            Directory.CreateDirectory(path);
            var names = new List<string> { "first-request.json", "first-request.meta.json", "clock-request.json", "clock-request.meta.json", "route-vocabulary.json" };
            if (includeWaitCandidate) names.Add("native-wait-candidate.json");
            foreach (string name in names)
                if (File.Exists(Path.Combine(path, name)))
                    throw new InvalidOperationException("Decision shadow capture refuses to overwrite existing file: " + name);
        }

        private static void WriteNativeWaitCandidate(string directory, NpcActionOption option,
            ConversationContextSnapshot snapshot, string captureTimeUtc, string sourceCaptureSha256, bool playerRelocated)
        {
            if (option == null || option.Kind != NpcActionKind.Wait || option.Target == null || snapshot == null ||
                snapshot.Binding == null || string.IsNullOrWhiteSpace(sourceCaptureSha256))
                throw new InvalidOperationException("Native Wait candidate sidecar is missing exact capture provenance.");
            NpcActionTarget target = option.Target;
            var json = new StringBuilder();
            json.Append('{');
            AppendJsonString(json, "schema"); json.Append(':'); AppendJsonString(json, "native-wait-candidate-v1"); json.Append(',');
            AppendJsonString(json, "schemaVersion"); json.Append(":1,");
            AppendJsonString(json, "purpose"); json.Append(':'); AppendJsonString(json, RoutePurpose); json.Append(',');
            AppendJsonString(json, "captureTimeGameSeconds"); json.Append(':').Append(snapshot.CapturedGameTime.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            AppendJsonString(json, "captureTimeUtc"); json.Append(':'); AppendJsonString(json, captureTimeUtc); json.Append(',');
            AppendJsonString(json, "nativeEffectObserved"); json.Append(":false,");
            AppendJsonString(json, "binding"); json.Append(":{");
            AppendJsonString(json, "actorCategory"); json.Append(':'); AppendJsonString(json, snapshot.Binding.ActorCategory.ToString()); json.Append(',');
            AppendJsonString(json, "identityKind"); json.Append(':'); AppendJsonString(json, snapshot.Binding.IdentityKind.ToString()); json.Append(',');
            AppendJsonString(json, "personId"); json.Append(':'); AppendOptionalJsonString(json, snapshot.Binding.PersonId); json.Append(',');
            AppendJsonString(json, "ephemeralSpeakerToken"); json.Append(':'); AppendOptionalJsonString(json, snapshot.Binding.EphemeralSpeakerToken); json.Append(',');
            AppendJsonString(json, "sessionId"); json.Append(':'); AppendJsonString(json, snapshot.Binding.SessionId); json.Append(',');
            AppendJsonString(json, "requestRevision"); json.Append(':').Append(snapshot.Binding.RequestRevision.ToString(CultureInfo.InvariantCulture)).Append(',');
            AppendJsonString(json, "saveEpoch"); json.Append(':').Append(snapshot.Binding.SaveEpoch.ToString(CultureInfo.InvariantCulture)).Append(',');
            AppendJsonString(json, "archiveId"); json.Append(':'); AppendJsonString(json, snapshot.Binding.ArchiveId); json.Append(',');
            AppendJsonString(json, "archiveHead"); json.Append(':'); AppendJsonString(json, snapshot.Binding.ArchiveHead); json.Append(',');
            AppendJsonString(json, "sceneId"); json.Append(':'); AppendJsonString(json, snapshot.Binding.SceneId); json.Append(',');
            AppendJsonString(json, "sceneRevision"); json.Append(':').Append(snapshot.Binding.SceneRevision.ToString(CultureInfo.InvariantCulture)).Append("},");
            AppendJsonString(json, "snapshotRevision"); json.Append(':').Append(snapshot.Revision.ToString(CultureInfo.InvariantCulture)).Append(',');
            AppendJsonString(json, "sourceCaptureSha256"); json.Append(':'); AppendJsonString(json, sourceCaptureSha256); json.Append(',');
            AppendJsonString(json, "playerRelocated"); json.Append(':').Append(playerRelocated ? "true" : "false").Append(',');
            AppendJsonString(json, "candidate"); json.Append(":{");
            AppendJsonString(json, "id"); json.Append(':').Append(option.Id.ToString(CultureInfo.InvariantCulture)).Append(',');
            AppendJsonString(json, "kind"); json.Append(':'); AppendJsonString(json, option.Kind.ToString()); json.Append(',');
            AppendJsonString(json, "label"); json.Append(':'); AppendJsonString(json, option.Label ?? string.Empty); json.Append(',');
            AppendJsonString(json, "target"); json.Append(":{");
            AppendJsonString(json, "kind"); json.Append(':'); AppendJsonString(json, target.Kind.ToString()); json.Append(',');
            AppendJsonString(json, "personId"); json.Append(':'); AppendOptionalJsonString(json, target.PersonId); json.Append(',');
            AppendJsonString(json, "containerId"); json.Append(':'); AppendOptionalJsonString(json, target.ContainerId); json.Append(',');
            AppendJsonString(json, "itemId"); json.Append(':'); AppendOptionalJsonString(json, target.ItemId); json.Append(',');
            AppendJsonString(json, "worldSpaceId"); json.Append(':'); AppendOptionalJsonString(json, target.WorldSpaceId); json.Append(',');
            AppendJsonString(json, "quantity"); json.Append(':').Append(target.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',');
            AppendJsonString(json, "quotedGold"); json.Append(':').Append(target.QuotedGold.ToString(CultureInfo.InvariantCulture)).Append(',');
            AppendJsonString(json, "transferDirection"); json.Append(':'); AppendJsonString(json, target.TransferDirection.ToString()); json.Append(',');
            AppendJsonString(json, "settlementId"); json.Append(':').Append(target.SettlementId.ToString(CultureInfo.InvariantCulture)).Append(',');
            AppendJsonString(json, "buildingKey"); json.Append(':').Append(target.BuildingKey.ToString(CultureInfo.InvariantCulture)).Append(',');
            AppendJsonString(json, "x"); json.Append(':').Append(target.X.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            AppendJsonString(json, "y"); json.Append(':').Append(target.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            AppendJsonString(json, "z"); json.Append(':').Append(target.Z.ToString("R", CultureInfo.InvariantCulture));
            json.Append("}}}");
            WriteNewFile(Path.Combine(directory, "native-wait-candidate.json"), new UTF8Encoding(false, true).GetBytes(json.ToString()));
        }

        private static void AppendOptionalJsonString(StringBuilder output, string value)
        { if (value == null) output.Append("null"); else AppendJsonString(output, value); }

        private static void WriteRouteVocabulary(string directory)
        {
            IReadOnlyList<NpcDecisionOption> routes = LocalDecisionRouting.RouteVocabulary();
            var json = new StringBuilder();
            json.Append('{');
            AppendJsonString(json, "purpose"); json.Append(':'); AppendJsonString(json, RoutePurpose); json.Append(',');
            AppendJsonString(json, "contextProtocol"); json.Append(':'); AppendJsonString(json, RouteContextProtocol); json.Append(',');
            AppendJsonString(json, "routes"); json.Append(":{");
            for (int index = 0; index < routes.Count; index++)
            {
                if (index > 0) json.Append(',');
                AppendJsonString(json, routes[index].Id); json.Append(':'); AppendJsonString(json, routes[index].Description);
            }
            json.Append("}}");
            WriteNewFile(Path.Combine(directory, "route-vocabulary.json"), new UTF8Encoding(false, true).GetBytes(json.ToString()));
        }

        private static void AppendJsonString(StringBuilder output, string value)
        {
            output.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        if (character < 0x20) output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else output.Append(character);
                        break;
                }
            }
            output.Append('"');
        }

        private static void WriteNewFile(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes, 0, bytes.Length);
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private sealed class ProbeState
        {
            public string ArchiveId;
            public string ArchiveHead;
            public string Transcript;
            public int Disposition;
            public Vector3 ActorPosition;
            public Vector3 PlayerPosition;
            public string Actions;
        }

        private static ProbeState CaptureState(PeopleOfTheIliacBayMod host, WorldStateStore store,
            DaggerfallTalkWindow talk, ResidentActor actor, GameManager game)
        {
            var archive = store.CaptureConversationArchiveCheckpoint();
            var transcriptField = typeof(DaggerfallTalkWindow).GetField("listboxConversation", InstanceMembers);
            var transcript = transcriptField == null ? null : transcriptField.GetValue(talk) as ListBox;
            if (transcript == null) throw new InvalidOperationException("Native Talk transcript is unavailable.");
            var rows = new StringBuilder();
            for (int index = 0; index < transcript.Count; index++)
            {
                var item = transcript.GetItem(index);
                rows.Append(item == null || item.textLabel == null ? string.Empty : item.textLabel.Text).Append('\n');
            }
            var reactionFieldMember = typeof(PeopleOfTheIliacBayMod).GetField("reactionToPlayerField", StaticMembers);
            var reactionField = reactionFieldMember == null ? null : reactionFieldMember.GetValue(null) as FieldInfo;
            if (reactionField == null) throw new InvalidOperationException("Native disposition field is unavailable.");
            var world = store.Snapshot();
            var actions = new StringBuilder();
            foreach (NpcActionJob job in world.NpcActions.OrderBy(value => value.Id, StringComparer.Ordinal))
                actions.Append(job.Id).Append('|').Append(job.ResidentId).Append('|').Append(job.Kind).Append('|')
                    .Append(job.Phase).Append('|').Append(job.Revision).Append('|').Append(job.CompletedEffectId).Append('\n');
            return new ProbeState
            {
                ArchiveId = archive.ArchiveId,
                ArchiveHead = archive.Head,
                Transcript = rows.ToString(),
                Disposition = Convert.ToInt32(reactionField.GetValue(game.TalkManager), CultureInfo.InvariantCulture),
                ActorPosition = actor.transform.position,
                PlayerPosition = game.PlayerMotor.transform.position,
                Actions = actions.ToString()
            };
        }

        private static void AssertUnchanged(ProbeState before, ProbeState after)
        {
            if (before.ArchiveId != after.ArchiveId || before.ArchiveHead != after.ArchiveHead ||
                before.Transcript != after.Transcript || before.Disposition != after.Disposition ||
                before.ActorPosition != after.ActorPosition || before.PlayerPosition != after.PlayerPosition ||
                before.Actions != after.Actions)
                throw new InvalidOperationException("Shadow observation changed archive, dialogue, disposition, position, or action state.");
        }

        private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags, int parameterCount)
        {
            MethodInfo method = type.GetMethods(flags).FirstOrDefault(candidate => candidate.Name == name &&
                candidate.GetParameters().Length == parameterCount);
            if (method == null) throw new InvalidOperationException("Required decision shadow method missing: " + name);
            return method;
        }

        private static FieldInfo RequireField(Type type, string name, BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null) throw new InvalidOperationException("Required decision shadow field missing: " + name);
            return field;
        }

        private sealed class DeterministicTransport : ILocalDecisionTransport
        {
            private readonly string captureDirectory;
            private readonly object gate = new object();
            private ManualResetEventSlim release = new ManualResetEventSlim(false);
            private ManualResetEventSlim entered = new ManualResetEventSlim(false);
            private int count;
            private int returned;
            private int blockedCall;
            private int cancelledCall;
            private Uri lastEndpoint;
            private string lastPayloadText;
            private string firstPayloadSha256;
            private int lastStatus;

            public DeterministicTransport(string captureDirectory)
            { this.captureDirectory = captureDirectory; }

            public int Count => Volatile.Read(ref count);
            public Uri LastEndpoint { get { lock (gate) return lastEndpoint; } }
            public string LastPayloadText { get { lock (gate) return lastPayloadText; } }
            public int LastStatus { get { lock (gate) return lastStatus; } }
            public string FirstPayloadSha256 { get { lock (gate) return firstPayloadSha256; } }
            public bool IsBlocked(int call) => Volatile.Read(ref blockedCall) == call && entered.IsSet;
            public bool WasCancelled(int call) => Volatile.Read(ref cancelledCall) == call;
            public bool HasReturned(int call) => Volatile.Read(ref returned) >= call;

            public void PrepareNextBlockedCall()
            {
                lock (gate)
                {
                    entered.Dispose();
                    release.Dispose();
                    entered = new ManualResetEventSlim(false);
                    release = new ManualResetEventSlim(false);
                }
            }

            public void ReleaseBlockedCall()
            {
                lock (gate) release.Set();
            }

            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds,
                CancellationToken cancellationToken)
            {
                int call = Interlocked.Increment(ref count);
                lock (gate)
                {
                    lastEndpoint = endpoint;
                    lastPayloadText = new UTF8Encoding(false, true).GetString(bytes);
                    if (call == 1) firstPayloadSha256 = Sha256(bytes);
                }
                if (captureDirectory != null && (call == 1 || call == 5))
                {
                    string stem = call == 1 ? "first-request" : "clock-request";
                    WriteNewFile(Path.Combine(captureDirectory, stem + ".json"), bytes);
                    string metadata = "{\"captureSource\":\"DecisionShadowProbe.DeterministicTransport.Send\"," +
                        "\"callOrdinal\":" + call.ToString(CultureInfo.InvariantCulture) + "," +
                        "\"endpoint\":\"" + endpoint.AbsoluteUri + "\"," +
                        "\"byteCount\":" + bytes.Length.ToString(CultureInfo.InvariantCulture) + "," +
                        "\"sha256\":\"" + Sha256(bytes) + "\"}";
                    WriteNewFile(Path.Combine(captureDirectory, stem + ".meta.json"), new UTF8Encoding(false).GetBytes(metadata));
                }
                if (call > 1)
                {
                    ManualResetEventSlim localEntered;
                    ManualResetEventSlim localRelease;
                    lock (gate) { localEntered = entered; localRelease = release; }
                    Volatile.Write(ref blockedCall, call);
                    localEntered.Set();
                    int signal = WaitHandle.WaitAny(new[] { localRelease.WaitHandle, cancellationToken.WaitHandle }, 5000);
                    if (signal == 1)
                    {
                        Volatile.Write(ref cancelledCall, call);
                        Interlocked.Increment(ref returned);
                        return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "cancelled");
                    }
                    if (signal == WaitHandle.WaitTimeout)
                    {
                        Interlocked.Increment(ref returned);
                        return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Timeout, "timeout");
                    }
                }
                string body = CreateResponseForRequest(bytes);
                Interlocked.Increment(ref returned);
                Volatile.Write(ref lastStatus, 200);
                return new LocalDecisionHttpResponse(200, body, NpcDecisionFailureKind.None);
            }

            private static string CreateResponseForRequest(byte[] bytes)
            {
                string payload = new UTF8Encoding(false, true).GetString(bytes);
                FullSerializer.fsData root = FullSerializer.fsJsonParser.Parse(payload);
                FullSerializer.fsData questions = root.AsDictionary["questions"];
                FullSerializer.fsData decision = questions.AsDictionary["decision"];
                FullSerializer.fsData criteria = decision.AsDictionary["criteria"];
                var optionIds = criteria.AsDictionary.Keys.OrderBy(value => value, StringComparer.Ordinal).ToList();
                if (!optionIds.Contains("SOCIAL", StringComparer.Ordinal))
                    throw new InvalidOperationException("Injected probe request does not contain the deterministic SOCIAL route.");

                var body = new StringBuilder();
                body.Append("{\"model\":"); AppendJsonString(body, TestModelId);
                body.Append(",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"SOCIAL\",\"probabilities\":{");
                for (int index = 0; index < optionIds.Count; index++)
                {
                    if (index > 0) body.Append(',');
                    AppendJsonString(body, optionIds[index]);
                    body.Append(':').Append(string.Equals(optionIds[index], "SOCIAL", StringComparison.Ordinal) ? '1' : '0');
                }
                body.Append("}}}}");
                return body.ToString();
            }
        }
    }
}
