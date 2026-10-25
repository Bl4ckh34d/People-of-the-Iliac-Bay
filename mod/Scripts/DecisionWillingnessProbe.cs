using System;
using System.Collections;
using System.Collections.Generic;
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
    // Native acceptance for action willingness, followed by one real D3/Qwen turn.
    internal static class DecisionWillingnessProbe
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Character = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string Switch = "-peopleoftheiliacbay-decision-willingness-probe";
        private const string Turn = "Would you wait here for ten seconds?";

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            if (!host || !streets || character != Character || !GameManager.Instance || !GameManager.Instance.IsReady ||
                Array.IndexOf(Environment.GetCommandLineArgs(), Switch) < 0)
                throw new InvalidOperationException("Decision willingness probe requires explicit opt-in and the owned native fixture.");
            var game = GameManager.Instance;
            var store = Field(typeof(PeopleOfTheIliacBayMod), "store", Members).GetValue(host) as WorldStateStore;
            if (store == null || DaggerfallUI.UIManager == null)
                throw new InvalidOperationException("Decision willingness probe cannot access native Talk state.");

            Vector3 originalPlayer = game.PlayerMotor.transform.position;
            ResidentActor actor = null;
            List<NpcActionDecision> orderedProposal = null;
            string orderedTurn = null;
            var geometry = Field(typeof(StreetPopulation), "geometry", Members).GetValue(streets) as TownGeometry;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            float geometryDeadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < geometryDeadline &&
                (!streets.Ready || geometry == null || directory == null || geometry.Entrances.Count == 0))
            {
                yield return null;
                geometry = Field(typeof(StreetPopulation), "geometry", Members).GetValue(streets) as TownGeometry;
                directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            }
            if (streets.Ready && geometry != null && directory != null && geometry.Entrances.Count > 0)
            {
                MethodInfo findFixture = Method(typeof(NativeMoveToWaitProbe), "FindFixture", StaticMembers, 7);
                object fixture = findFixture.Invoke(null, new object[] { streets, store, geometry, directory, game,
                    new HashSet<string>(StringComparer.Ordinal), null });
                if (fixture != null)
                {
                    actor = Field(fixture.GetType(), "Actor", Members).GetValue(fixture) as ResidentActor;
                    orderedProposal = Field(fixture.GetType(), "Decisions", Members).GetValue(fixture) as List<NpcActionDecision>;
                    string buildingName = Field(fixture.GetType(), "BuildingName", Members).GetValue(fixture) as string;
                    if (string.IsNullOrWhiteSpace(buildingName)) throw new InvalidOperationException("Native two-step fixture lacks a verified building name.");
                    orderedTurn = "Go to " + buildingName + " then wait here for ten seconds.";
                }
            }
            if (!actor)
            {
                IEnumerator actorWait = (IEnumerator)Method(typeof(DecisionShadowProbe), "WaitForUnusedManagedAdult", StaticMembers, 4)
                    .Invoke(null, new object[] { store, (Action<ResidentActor>)(value => actor = value), true, originalPlayer });
                while (actorWait.MoveNext()) yield return actorWait.Current;
            }
            if (!actor) throw new InvalidOperationException("No unused managed adult is available for willingness probe.");
            Vector3 fixtureActorPosition = actor.transform.position;
            if (orderedProposal == null)
            {
                if (geometry == null || directory == null || !streets.Ready)
                    throw new InvalidOperationException("No ready town geometry is available to stage a safe observer position.");
                object[] observerArgs = { geometry, directory, actor.transform.position - Vector3.up, null };
                object foundObserver = Method(typeof(NativeMoveToWaitProbe), "TryObserverGround", StaticMembers, 4)
                    .Invoke(null, observerArgs);
                if (!(foundObserver is bool) || !(bool)foundObserver)
                    throw new InvalidOperationException("Selected resident has no verified clear observer ground point.");
                Vector3 observerGround = (Vector3)observerArgs[3];
                game.PlayerMotor.transform.position = observerGround + Vector3.up;
                Physics.SyncTransforms();
                float observerGap = Vector3.Distance(game.PlayerMotor.transform.position - Vector3.up,
                    actor.transform.position - Vector3.up);
                if (observerGap < 2f || observerGap > 4f)
                    throw new InvalidOperationException("Verified observer point did not retain a 2–4 meter actor gap.");
            }
            DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToMobileNPC(actor.Person);
            DaggerfallTalkWindow talk = null;
            IEnumerator talkWait = (IEnumerator)Method(typeof(DecisionShadowProbe), "WaitForTalk", StaticMembers, 2)
                .Invoke(null, new object[] { actor, (Action<DaggerfallTalkWindow>)(value => talk = value) });
            while (talkWait.MoveNext()) yield return talkWait.Current;
            if (talk == null) throw new InvalidOperationException("Native Talk did not open for the selected resident.");
            if (!(bool)Field(host.GetType(), "conversationSessionOpen", Members).GetValue(host))
                Method(host.GetType(), "BeginGroundedConversation", Members, 0).Invoke(host, null);
            if (!ReferenceEquals(Field(host.GetType(), "conversationActor", Members).GetValue(host), actor))
                throw new InvalidOperationException("Native Talk did not bind the selected resident.");

            var waitOptions = streets.ConversationActionCandidates(actor.Resident.Id)
                .Where(option => option != null && option.Kind == NpcActionKind.Wait && option.Target != null).ToList();
            if (waitOptions.Count != 1)
                throw new InvalidOperationException("Willingness probe requires exactly one actual native Wait capability.");
            NpcActionOption waitOption = waitOptions[0];
            NpcActionContract.ValidateTarget(waitOption.Target);
            var proposal = new List<NpcActionDecision> { new NpcActionDecision { Option = waitOption, DurationSeconds = 10 } };
            if (orderedProposal != null && (orderedProposal.Count != 2 || orderedProposal[0].Option.Kind != NpcActionKind.MoveTo ||
                orderedProposal[1].Option.Kind != NpcActionKind.Wait ||
                !NpcTaskPositionBinding.IsBoundWaitAfterMoveTo(
                    new NpcTaskStep { Kind = NpcActionKind.MoveTo, Target = orderedProposal[0].Option.Target },
                    new NpcTaskStep { Kind = NpcActionKind.Wait, Target = orderedProposal[1].Option.Target })))
                throw new InvalidOperationException("Native two-step fixture did not preserve exact MoveTo then bound Wait proposal.");

            FieldInfo textEnabled = Field(host.GetType(), "localDialogueEnabled", Members);
            FieldInfo prewarmEnabled = Field(host.GetType(), "dialoguePrewarmEnabled", Members);
            FieldInfo busy = Field(host.GetType(), "conversationBusy", Members);
            textEnabled.SetValue(host, true);
            prewarmEnabled.SetValue(host, false);
            busy.SetValue(host, true);
            var textClient = Field(host.GetType(), "localDialogue", Members).GetValue(host) as LocalDialogueClient;
            if (textClient) textClient.Cancel();

            var transport = new DeterministicTransport(orderedProposal == null
                ? new[] { "ACTION_REQUEST", "ACCEPT", "DECLINE", "INVALID" }
                : new[] { "ACTION_REQUEST", "ACCEPT", "DECLINE", "INVALID", "ACCEPT" });
            var deterministicSession = new LocalDecisionSession(transport) { ExpectedModelId = "willingness-native-probe", Enabled = true };
            var connection = new LocalDialogueConnection("http://127.0.0.1:1/completion", 2000);
            Method(host.GetType(), "InstallDecisionRoutingForProbe", Members, 2)
                .Invoke(host, new object[] { deterministicSession, connection });
            MethodInfo capture = Method(host.GetType(), "CaptureConversationContext", Members, 3);
            MethodInfo beginRoute = Method(host.GetType(), "TryBeginDecisionRouting", Members, 4);
            MethodInfo begin = Method(host.GetType(), "TryBeginDecisionWillingness", Members, 5);
            MethodInfo pump = Method(host.GetType(), "UpdateDecisionShadow", Members, 0);
            MethodInfo captureState = Method(typeof(DecisionShadowProbe), "CaptureState", StaticMembers, 5);
            MethodInfo assertUnchanged = Method(typeof(DecisionShadowProbe), "AssertUnchanged", StaticMembers, 2);
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            bool callbackThreadsCorrect = true;
            int callbacks = 0;
            string routeOnly = null;
            yield return DeterministicRoute(host, capture, beginRoute, pump, captureState, assertUnchanged,
                store, talk, actor, game, transport, value => { callbackThreadsCorrect &= Thread.CurrentThread.ManagedThreadId == mainThreadId; routeOnly = value; callbacks++; });
            if (routeOnly != "ACTION_REQUEST" || callbacks != 1)
                throw new InvalidOperationException("Deterministic D3 route did not return ACTION_REQUEST without committing an action.");
            string accept = null;
            yield return DeterministicChoice(host, capture, begin, pump, captureState, assertUnchanged,
                store, talk, actor, game, proposal, transport, Turn, value => { callbackThreadsCorrect &= Thread.CurrentThread.ManagedThreadId == mainThreadId; accept = value; callbacks++; });
            if (!string.Equals(accept, "ACCEPT", StringComparison.Ordinal) || callbacks != 2)
                throw new InvalidOperationException("Deterministic willingness ACCEPT did not reach exactly one callback.");
            string decline = null;
            yield return DeterministicChoice(host, capture, begin, pump, captureState, assertUnchanged,
                store, talk, actor, game, proposal, transport, Turn, value => { callbackThreadsCorrect &= Thread.CurrentThread.ManagedThreadId == mainThreadId; decline = value; callbacks++; });
            if (!string.Equals(decline, "DECLINE", StringComparison.Ordinal) || callbacks != 3)
                throw new InvalidOperationException("Deterministic willingness DECLINE did not reach exactly one callback.");
            string invalid = "pending";
            yield return DeterministicChoice(host, capture, begin, pump, captureState, assertUnchanged,
                store, talk, actor, game, proposal, transport, Turn, value => { callbackThreadsCorrect &= Thread.CurrentThread.ManagedThreadId == mainThreadId; invalid = value; callbacks++; });
            if (invalid != null || callbacks != 4)
                throw new InvalidOperationException("Invalid willingness choice did not become one null technical-failure callback.");
            if (orderedProposal != null)
            {
                string orderedAccept = null;
                yield return DeterministicChoice(host, capture, begin, pump, captureState, assertUnchanged,
                    store, talk, actor, game, orderedProposal, transport, orderedTurn,
                    value => { callbackThreadsCorrect &= Thread.CurrentThread.ManagedThreadId == mainThreadId; orderedAccept = value; callbacks++; });
                if (orderedAccept != "ACCEPT" || callbacks != 5)
                    throw new InvalidOperationException("Valid native MoveTo/Wait proposal did not receive deterministic willingness ACCEPT.");
            }
            NpcActionJob latestBeforeCancel = store.LatestNpcAction(actor.Resident.Id);
            string latestIdBeforeCancel = latestBeforeCancel == null ? null : latestBeforeCancel.Id;
            int cancellationCall = transport.CallCount + 1;
            transport.PrepareNextBlockedCall();
            long cancelRevision = Convert.ToInt64(Field(host.GetType(), "conversationContextRequestRevision", Members).GetValue(host)) + 1;
            object cancelSnapshot = capture.Invoke(host, new object[] { talk, Turn, cancelRevision });
            int cancellationCallbacks = 0;
            object cancellationAccepted = begin.Invoke(host, new object[] { cancelSnapshot, talk, Turn, proposal,
                (Action<string>)(_ => cancellationCallbacks++) });
            if (!(cancellationAccepted is bool) || !(bool)cancellationAccepted)
                throw new InvalidOperationException("Willingness stage rejected the current Talk cancellation fixture.");
            float blockDeadline = Time.realtimeSinceStartup + 5f;
            while (!transport.IsBlocked(cancellationCall) && Time.realtimeSinceStartup < blockDeadline) yield return null;
            if (!transport.IsBlocked(cancellationCall)) throw new InvalidOperationException("Willingness transport did not block for Talk-close invalidation.");
            DaggerfallUI.Instance.PopToHUD();
            float cancelDeadline = Time.realtimeSinceStartup + 3f;
            while (!transport.WasCancelled(cancellationCall) && Time.realtimeSinceStartup < cancelDeadline) yield return null;
            if (!transport.WasCancelled(cancellationCall)) throw new InvalidOperationException("Closing native Talk did not cancel the pending willingness request.");
            transport.ReleaseBlockedCall();
            float returnDeadline = Time.realtimeSinceStartup + 3f;
            while (!transport.HasReturned(cancellationCall) && Time.realtimeSinceStartup < returnDeadline) yield return null;
            float settleDeadline = Time.realtimeSinceStartup + 0.2f;
            while (Time.realtimeSinceStartup < settleDeadline) { pump.Invoke(host, null); yield return null; }
            NpcActionJob latestAfterCancel = store.LatestNpcAction(actor.Resident.Id);
            if (cancellationCallbacks != 0 ||
                (latestAfterCancel == null ? null : latestAfterCancel.Id) != latestIdBeforeCancel ||
                store.ActiveNpcAction(actor.Resident.Id) != null || store.ActiveNpcTask(actor.Resident.Id) != null)
                throw new InvalidOperationException("Closed Talk delivered a willingness callback or changed native action/task state.");
            // The live SubmitFreeText phase needs a current Talk window. Reopen it
            // only after confirming the closed-window callback stayed suppressed.
            busy.SetValue(host, true);
            game.TalkManager.TalkToMobileNPC(actor.Person);
            talk = null;
            talkWait = (IEnumerator)Method(typeof(DecisionShadowProbe), "WaitForTalk", StaticMembers, 2)
                .Invoke(null, new object[] { actor, (Action<DaggerfallTalkWindow>)(value => talk = value) });
            while (talkWait.MoveNext()) yield return talkWait.Current;
            if (talk == null) throw new InvalidOperationException("Native Talk did not reopen after willingness cancellation.");
            if (!(bool)Field(host.GetType(), "conversationSessionOpen", Members).GetValue(host))
                Method(host.GetType(), "BeginGroundedConversation", Members, 0).Invoke(host, null);
            if (transport.CompletedCalls != (orderedProposal == null ? 4 : 5))
                throw new InvalidOperationException("Deterministic willingness transport call count changed.");
            if (!callbackThreadsCorrect) throw new InvalidOperationException("Willingness callback escaped the main thread.");

            if (store.ActiveNpcTask(actor.Resident.Id) != null || store.ActiveNpcAction(actor.Resident.Id) != null)
                throw new InvalidOperationException("Willingness evaluation committed an action before native acceptance.");

            deterministicSession.Dispose();
            string expectedModel = ResolveD3ModelId();
            var liveTransport = new RecordingTransport(new LocalDecisionTransport());
            var liveSession = new LocalDecisionSession(liveTransport) { ExpectedModelId = expectedModel, Enabled = true };
            Method(host.GetType(), "InstallDecisionRoutingForProbe", Members, 2)
                .Invoke(host, new object[] { liveSession, new LocalDialogueConnection("http://127.0.0.1:8093/completion", 10000) });
            textEnabled.SetValue(host, true);
            busy.SetValue(host, false);
            int transcriptBefore = TranscriptCount(talk);
            NpcActionJob actionBeforeLiveTurn = store.LatestNpcAction(actor.Resident.Id);
            string actionIdBeforeLiveTurn = actionBeforeLiveTurn == null ? null : actionBeforeLiveTurn.Id;
            Vector3 liveWaitStart = Vector3.zero;
            float maxLiveWaitDrift = 0f;
            bool liveWaitStarted = false;
            Method(host.GetType(), "SubmitFreeText", Members, 2).Invoke(host, new object[] { talk, Turn });

            float liveStartedAt = Time.realtimeSinceStartup;
            float deadline = liveStartedAt + 120f;
            float nextLiveDiagnostic = liveStartedAt + 20f;
            bool routeSeen = false, willingnessCall = false, replySeen = false;
            string route = null, willingness = null;
            int inspectedResults = 0;
            var purposeOrder = new List<string>();
            while (Time.realtimeSinceStartup < deadline && (!routeSeen || !replySeen))
            {
                pump.Invoke(host, null);
                IReadOnlyList<RecordingTransport.Result> recordedResults = liveTransport.Results;
                while (inspectedResults < recordedResults.Count)
                {
                    RecordingTransport.Result result = recordedResults[inspectedResults++];
                    purposeOrder.Add(result.Purpose);
                    if (!string.Equals(result.OriginalTurn, Turn, StringComparison.Ordinal)) continue;
                    if (string.Equals(result.Purpose, LocalDecisionRouting.Purpose, StringComparison.Ordinal))
                    {
                        routeSeen = ValidResult(result, expectedModel);
                        if (routeSeen) route = result.Choice;
                    }
                    if (string.Equals(result.Purpose, "npc-action-willingness-v1", StringComparison.Ordinal))
                    {
                        willingnessCall = true;
                        if (ValidResult(result, expectedModel)) willingness = result.Choice;
                    }
                }
                NpcActionJob observedAction = store.LatestNpcAction(actor.Resident.Id);
                if (observedAction != null && observedAction.Id != actionIdBeforeLiveTurn && observedAction.Kind == NpcActionKind.Wait)
                {
                    if (!liveWaitStarted) { liveWaitStarted = true; liveWaitStart = actor.transform.position; }
                    float liveDrift = Vector3.Distance(liveWaitStart, actor.transform.position);
                    if (liveDrift > maxLiveWaitDrift) maxLiveWaitDrift = liveDrift;
                }
                replySeen = HasReply(talk, transcriptBefore, Turn);
                float elapsed = Time.realtimeSinceStartup - liveStartedAt;
                if (!replySeen && Time.realtimeSinceStartup >= nextLiveDiagnostic)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_WILLINGNESS_LIVE_PROGRESS: elapsed=" + elapsed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                        "; " + LiveStateDiagnostic(host, liveSession, store, talk, actor, transcriptBefore));
                    nextLiveDiagnostic = Time.realtimeSinceStartup + 30f;
                }
                yield return null;
            }
            if (!routeSeen) throw new InvalidOperationException("Production SubmitFreeText did not produce a valid live D3 route. " +
                LiveStateDiagnostic(host, liveSession, store, talk, actor, transcriptBefore));
            if (!replySeen) throw new InvalidOperationException("Production SubmitFreeText did not produce a reply within the 120-second live deadline. " +
                LiveStateDiagnostic(host, liveSession, store, talk, actor, transcriptBefore));
            int routePurposeIndex = purposeOrder.IndexOf(LocalDecisionRouting.Purpose);
            int willingnessPurposeIndex = purposeOrder.IndexOf("npc-action-willingness-v1");
            if (routePurposeIndex < 0 || willingnessCall && willingnessPurposeIndex <= routePurposeIndex)
                throw new InvalidOperationException("Recorded live wire purpose order did not place D3 routing before willingness.");
            Method(host.GetType(), "CancelDecisionShadow", Members, 0).Invoke(host, null);
            liveSession.Dispose();

            // Preserve the actual model outcome. Only a no-effect live turn may
            // enter this clearly labelled synthetic ACCEPT mechanics check.
            NpcActionJob waitAction = store.ActiveNpcAction(actor.Resident.Id);
            NpcActionJob latestAfterLive = store.LatestNpcAction(actor.Resident.Id);
            bool liveCreatedAction = latestAfterLive != null && latestAfterLive.Id != actionIdBeforeLiveTurn;
            if (!liveWaitStarted && liveCreatedAction && latestAfterLive.Kind == NpcActionKind.Wait)
            {
                liveWaitStarted = true;
                liveWaitStart = actor.transform.position;
            }
            bool syntheticAccept = !liveWaitStarted && waitAction == null && !liveCreatedAction &&
                store.ActiveNpcTask(actor.Resident.Id) == null;
            Vector3 syntheticWaitStart = Vector3.zero;
            if (syntheticAccept)
            {
                var syntheticTransport = new DeterministicTransport(new[] { "ACCEPT" });
                var syntheticSession = new LocalDecisionSession(syntheticTransport)
                    { ExpectedModelId = "willingness-native-probe", Enabled = true };
                Method(host.GetType(), "InstallDecisionRoutingForProbe", Members, 2)
                    .Invoke(host, new object[] { syntheticSession, new LocalDialogueConnection("http://127.0.0.1:1/completion", 2000) });
                MethodInfo captureFresh = Method(host.GetType(), "CaptureConversationContext", Members, 3);
                long syntheticRevision = Convert.ToInt64(Field(host.GetType(), "conversationContextRequestRevision", Members).GetValue(host)) + 1;
                object syntheticSnapshot = captureFresh.Invoke(host, new object[] { talk, Turn, syntheticRevision });
                if (syntheticSnapshot == null) throw new InvalidOperationException("Synthetic acceptance could not capture current native Talk.");
                string history = (string)Method(host.GetType(), "ReadConversationHistory", StaticMembers, 1).Invoke(null, new object[] { talk });
                MethodInfo resolveWillingness = Method(host.GetType(), "ResolveProposedActionWillingness", Members, 8);
                bool syntheticComplete = false;
                bool syntheticAccepted = false;
                string syntheticFailure = null;
                resolveWillingness.Invoke(host, new object[] { actor, talk, Turn, history, syntheticSnapshot, true, proposal,
                    (Action<List<NpcActionDecision>>)(agreed =>
                    {
                        if (agreed == null || agreed.Count != 1)
                        { syntheticFailure = "Synthetic ACCEPT returned an unexpected decision count."; syntheticComplete = true; return; }
                        syntheticWaitStart = actor.transform.position;
                        string reason;
                        syntheticAccepted = NpcProbeCommandSource.TryAcceptAction(streets,actor.Resident.Id,
                            "synthetic-willingness:" + Guid.NewGuid().ToString("N"), agreed[0], out reason);
                        if (!syntheticAccepted) syntheticFailure = reason;
                        syntheticComplete = true;
                    }) });
                float syntheticDeadline = Time.realtimeSinceStartup + 10f;
                while (!syntheticComplete && Time.realtimeSinceStartup < syntheticDeadline)
                { Method(host.GetType(), "UpdateDecisionShadow", Members, 0).Invoke(host, null); yield return null; }
                syntheticSession.Dispose();
                if (!syntheticComplete || !syntheticAccepted || syntheticTransport.CompletedCalls != 1)
                    throw new InvalidOperationException("Explicit synthetic ACCEPT through ResolveProposedActionWillingness did not reach native action acceptance: " +
                        (syntheticFailure ?? "callback timeout or missing deterministic response."));
                waitAction = store.ActiveNpcAction(actor.Resident.Id);
            }
            DaggerfallUI.Instance.PopToHUD();
            if (liveWaitStarted && waitAction == null)
                waitAction = store.LatestNpcAction(actor.Resident.Id);
            if (waitAction == null || waitAction.Kind != NpcActionKind.Wait)
                throw new InvalidOperationException("No valid native Wait action followed production or explicit synthetic acceptance.");
            Vector3 waitStart = syntheticAccept ? syntheticWaitStart : liveWaitStart;
            double acceptedAt = waitAction.AcceptedAt;
            double scheduledWaitSeconds = waitAction.FinishAt - acceptedAt;
            double requestedWaitSeconds = proposal[0].DurationSeconds;
            bool requestedDurationMatched = Math.Abs(scheduledWaitSeconds - requestedWaitSeconds) <= 0.001d;
            double worldClockScale = DaggerfallUnity.Instance.WorldTime.TimeScale;
            if (double.IsNaN(scheduledWaitSeconds) || double.IsInfinity(scheduledWaitSeconds) || scheduledWaitSeconds < 10d || scheduledWaitSeconds > 900d ||
                double.IsNaN(worldClockScale) || double.IsInfinity(worldClockScale) || worldClockScale <= 0d)
                throw new InvalidOperationException("Saved Wait duration or world-clock scale is invalid: scheduled=" + scheduledWaitSeconds + "; scale=" + worldClockScale + ".");
            double waitEntryGameTime = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_WILLINGNESS_WAIT_PROGRESS: event=entered; id=" + waitAction.Id +
                "; phase=" + waitAction.Phase + "; gameTime=" + waitEntryGameTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; acceptedAt=" + acceptedAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; finishAt=" + waitAction.FinishAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; requestedSeconds=" + requestedWaitSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; requestedDurationMatched=" + requestedDurationMatched.ToString().ToLowerInvariant() +
                "; scheduledSeconds=" + scheduledWaitSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; worldClockScale=" + worldClockScale.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            float waitStartedAt = Time.realtimeSinceStartup;
            double waitRealtimeBudget = scheduledWaitSeconds / worldClockScale + 45d;
            float waitDeadline = waitStartedAt + (float)Math.Min(795d, waitRealtimeBudget);
            float nextWaitDiagnostic = waitStartedAt + 20f;
            while (Time.realtimeSinceStartup < waitDeadline && !NpcActionExecutor.IsTerminal(waitAction))
            {
                waitAction = store.ReadNpcAction(waitAction.Id);
                if (waitAction == null) throw new InvalidOperationException("Wait receipt disappeared before physical completion.");
                if (Time.realtimeSinceStartup >= nextWaitDiagnostic)
                {
                    double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_WILLINGNESS_WAIT_PROGRESS: event=waiting; id=" + waitAction.Id +
                        "; phase=" + waitAction.Phase + "; gameTime=" + now.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                        "; acceptedAt=" + acceptedAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                        "; finishAt=" + waitAction.FinishAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                        "; elapsedSeconds=" + (now - acceptedAt).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                    nextWaitDiagnostic = Time.realtimeSinceStartup + 30f;
                }
                yield return null;
            }
            float drift = Math.Max(maxLiveWaitDrift, Vector3.Distance(waitStart, actor.transform.position));
            double actualWaitSeconds = waitAction.UpdatedAt - acceptedAt;
            double finishElapsed = waitAction.FinishAt - acceptedAt;
            double endGameTime = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            double pollOvershootAllowance = Math.Max(2d, worldClockScale * 2d);
            if (waitAction.Phase != NpcActionPhase.Completed || Math.Abs(finishElapsed - scheduledWaitSeconds) > 0.001d ||
                actualWaitSeconds < scheduledWaitSeconds - 0.001d || actualWaitSeconds > scheduledWaitSeconds + pollOvershootAllowance || drift > 0.5f)
                throw new InvalidOperationException("Native Wait duration/physical evidence failed: phase=" + waitAction.Phase +
                    "; acceptedAt=" + acceptedAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; finishAt=" + waitAction.FinishAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; updatedAt=" + waitAction.UpdatedAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; currentGameTime=" + endGameTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; requestedSeconds=" + requestedWaitSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; requestedDurationMatched=" + requestedDurationMatched.ToString().ToLowerInvariant() +
                    "; scheduledSeconds=" + finishElapsed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; actualSeconds=" + actualWaitSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; pollOvershootAllowance=" + pollOvershootAllowance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; drift=" + drift.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "m.");
            // Match the physical MoveTo/Wait probe: returning to a routine door
            // includes real walking, rather than an immediate ownership reset.
            float releaseDeadline = Time.realtimeSinceStartup + 150f;
            float nextReleaseDiagnostic = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < releaseDeadline &&
                (store.ActiveNpcAction(actor.Resident.Id) != null || store.ActiveNpcTask(actor.Resident.Id) != null))
            {
                if (Time.realtimeSinceStartup >= nextReleaseDiagnostic)
                {
                    NpcActionJob recoveryAction = store.ActiveNpcAction(actor.Resident.Id);
                    NpcTaskPlan recoveryTask = store.ActiveNpcTask(actor.Resident.Id);
                    Vector3 recoveryPosition = actor.transform.position;
                    double recoveryTime = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_WILLINGNESS_ROUTINE_RELEASE_PROGRESS: gameTime=" +
                        recoveryTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                        "; activeAction=" + (recoveryAction == null ? "none" : recoveryAction.Id + "/" + recoveryAction.Phase +
                            "/returningToRoutine=" + recoveryAction.ReturningToRoutine) +
                        "; activeTask=" + (recoveryTask == null ? "none" : recoveryTask.Id + "/" + recoveryTask.Phase) +
                        "; actorPosition=" + recoveryPosition +
                        "; playerGap=" + Vector3.Distance(game.PlayerMotor.transform.position, recoveryPosition).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                        "; motion=" + DescribeActionRecoveryMotion(streets, actor));
                    nextReleaseDiagnostic = Time.realtimeSinceStartup + 20f;
                }
                yield return null;
            }
            NpcActionJob retainedAction = store.ActiveNpcAction(actor.Resident.Id);
            NpcTaskPlan retainedTask = store.ActiveNpcTask(actor.Resident.Id);
            if (retainedAction != null || retainedTask != null)
            {
                NpcActionJob latest = store.LatestNpcAction(actor.Resident.Id);
                Vector3 actorPosition = actor.transform.position;
                float playerGap = Vector3.Distance(game.PlayerMotor.transform.position, actorPosition);
                double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                throw new InvalidOperationException("Completed Wait retained action ownership instead of returning to routine: activeAction=" +
                    (retainedAction == null ? "none" : retainedAction.Id + "/" + retainedAction.Kind + "/" + retainedAction.Phase +
                        "/returningToRoutine=" + retainedAction.ReturningToRoutine + "/acceptedAt=" + retainedAction.AcceptedAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                        "/finishAt=" + retainedAction.FinishAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)) +
                    "; activeTask=" + (retainedTask == null ? "none" : retainedTask.Id + "/" + retainedTask.Phase) +
                    "; latest=" + (latest == null ? "none" : latest.Id + "/" + latest.Phase + "/returningToRoutine=" + latest.ReturningToRoutine) +
                    "; currentGameTime=" + now.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; actorPosition=" + actorPosition + "; fixtureActorPosition=" + fixtureActorPosition +
                    "; actorDelta=" + Vector3.Distance(actorPosition, fixtureActorPosition).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; playerGap=" + playerGap.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; motion=" + DescribeActionRecoveryMotion(streets, actor) +
                    "; gamePaused=" + GameManager.IsGamePaused + "; conversing=" + actor.IsConversing + ".");
            }
            game.PlayerMotor.transform.position = originalPlayer;
            Physics.SyncTransforms();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_WILLINGNESS_NATIVE_PASS: native-snapshot=true; verified-wait-candidate=true; ordered-two-step=" + (orderedProposal == null ? "unavailable" : "available") + "; deterministic-route=ACTION_REQUEST; route-no-effect=true; deterministic-willingness=ACCEPT-DECLINE-invalid-null; callbacks=" + callbacks + "; main-thread=" + callbackThreadsCorrect.ToString().ToLowerInvariant() +
                "; cancellation=close-no-callback; synthetic-accept=" + syntheticAccept.ToString().ToLowerInvariant() +
                "; requested-duration-seconds=" + requestedWaitSeconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) +
                "; requested-duration-matched=" + requestedDurationMatched.ToString().ToLowerInvariant() +
                "; physical-wait=completed; wait-scheduled=" + finishElapsed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                "; wait-actual=" + actualWaitSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                "; wait-drift=" + drift.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; routine-owner-released=true; submit-free-text=true; route-purpose=" + LocalDecisionRouting.Purpose +
                "; route=" + route + "; willingness-purpose=npc-action-willingness-v1; willingness=" + (willingness ?? "not-observed") +
                "; wire-purpose-order=" + string.Join(",", purposeOrder.ToArray()) +
                "; willingness-call=" + willingnessCall.ToString().ToLowerInvariant() + "; qwen-reply=true; turn=Would-you-wait-here-for-ten-seconds");
        }

        private static IEnumerator DeterministicChoice(PeopleOfTheIliacBayMod host, MethodInfo capture, MethodInfo begin,
            MethodInfo pump, MethodInfo captureState, MethodInfo assertUnchanged, WorldStateStore store,
            DaggerfallTalkWindow talk, ResidentActor actor, GameManager game, IList<NpcActionDecision> proposal,
            DeterministicTransport transport, string turn, Action<string> completed)
        {
            long revision = Convert.ToInt64(Field(host.GetType(), "conversationContextRequestRevision", Members).GetValue(host)) + 1;
            object snapshot = capture.Invoke(host, new object[] { talk, turn, revision });
            if (snapshot == null) throw new InvalidOperationException("Willingness native snapshot capture returned null.");
            object before = captureState.Invoke(null, new object[] { host, store, talk, actor, game });
            string result = "pending";
            object accepted = begin.Invoke(host, new object[] { snapshot, talk, turn, proposal,
                (Action<string>)(value => { result = value; completed(value); }) });
            if (!(accepted is bool) || !(bool)accepted)
                throw new InvalidOperationException("Willingness stage rejected current native Talk.");
            float deadline = Time.realtimeSinceStartup + 10f;
            while (result == "pending" && Time.realtimeSinceStartup < deadline)
            {
                pump.Invoke(host, null);
                yield return null;
            }
            if (result == "pending") throw new InvalidOperationException("Deterministic willingness response timed out.");
            object after = captureState.Invoke(null, new object[] { host, store, talk, actor, game });
            assertUnchanged.Invoke(null, new[] { before, after });
        }

        private static IEnumerator DeterministicRoute(PeopleOfTheIliacBayMod host, MethodInfo capture, MethodInfo begin,
            MethodInfo pump, MethodInfo captureState, MethodInfo assertUnchanged, WorldStateStore store,
            DaggerfallTalkWindow talk, ResidentActor actor, GameManager game, DeterministicTransport transport,
            Action<string> completed)
        {
            long revision = Convert.ToInt64(Field(host.GetType(), "conversationContextRequestRevision", Members).GetValue(host)) + 1;
            object snapshot = capture.Invoke(host, new object[] { talk, Turn, revision });
            if (snapshot == null) throw new InvalidOperationException("Route native snapshot capture returned null.");
            object before = captureState.Invoke(null, new object[] { host, store, talk, actor, game });
            string result = "pending";
            object accepted = begin.Invoke(host, new object[] { snapshot, talk, Turn,
                (Action<string>)(value => { result = value; completed(value); }) });
            if (!(accepted is bool) || !(bool)accepted)
                throw new InvalidOperationException("Decision routing rejected the native Talk snapshot.");
            float deadline = Time.realtimeSinceStartup + 10f;
            while (result == "pending" && Time.realtimeSinceStartup < deadline)
            {
                pump.Invoke(host, null);
                yield return null;
            }
            if (result == "pending") throw new InvalidOperationException("Deterministic route response timed out.");
            object after = captureState.Invoke(null, new object[] { host, store, talk, actor, game });
            assertUnchanged.Invoke(null, new[] { before, after });
        }

        private static string ResolveD3ModelId()
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../local-llm/models/d3-mini/vllm-d3-mini-Q4_K_M.gguf"));
            if (!System.IO.File.Exists(path)) throw new InvalidOperationException("Owned D3-mini model fixture is missing.");
            return path;
        }

        private static bool ValidResult(RecordingTransport.Result result, string model)
        { return result != null && result.StatusCode == 200 && result.Parsed != null && result.Parsed.IsValid &&
            result.Parsed.FailureKind == NpcDecisionFailureKind.None && string.Equals(result.Parsed.ModelId, model, StringComparison.Ordinal); }

        private static int TranscriptCount(DaggerfallTalkWindow talk)
        { var field = typeof(DaggerfallTalkWindow).GetField("listboxConversation", Members); var list = field == null ? null : field.GetValue(talk) as DaggerfallWorkshop.Game.UserInterface.ListBox; return list == null ? 0 : list.Count; }

        private static bool HasReply(DaggerfallTalkWindow talk, int start, string turn)
        {
            var field = typeof(DaggerfallTalkWindow).GetField("listboxConversation", Members);
            var list = field == null ? null : field.GetValue(talk) as DaggerfallWorkshop.Game.UserInterface.ListBox;
            if (list == null) return false;
            for (int i = Math.Max(0, start); i + 1 < list.Count; i++)
            {
                var question = list.GetItem(i); var answer = list.GetItem(i + 1);
                if (question == null || question.textLabel == null || answer == null || answer.textLabel == null) continue;
                if (question.textColor == DaggerfallUI.DaggerfallQuestionTextColor && question.textLabel.Text == turn &&
                    answer.textColor != DaggerfallUI.DaggerfallQuestionTextColor && !string.IsNullOrWhiteSpace(answer.textLabel.Text)) return true;
            }
            return false;
        }

        private static string LiveStateDiagnostic(PeopleOfTheIliacBayMod host, LocalDecisionSession session,
            WorldStateStore store, DaggerfallTalkWindow talk, ResidentActor actor, int transcriptStart)
        {
            var names = new[] { "conversationContextStageSequence", "conversationContextIssuedStageToken",
                "conversationContextRejectedRequestRevision", "conversationContextRequestRevision", "conversationContextCapturedPlayerText",
                "conversationBusy", "conversationSessionOpen", "memorySession", "conversationPersonId" };
            var values = new List<string>();
            foreach (string name in names)
            {
                try
                {
                    object value = Field(host.GetType(), name, Members).GetValue(host);
                    string rendered = value == null ? "null" : value.ToString();
                    if (rendered.Length > 120) rendered = rendered.Substring(0, 120) + "…";
                    values.Add(name + "=" + rendered);
                }
                catch { values.Add(name + "=<unavailable>"); }
            }
            bool current = false;
            try
            {
                object snapshot = Field(host.GetType(), "conversationContextCurrentSnapshot", Members).GetValue(host);
                current = snapshot != null && (bool)Method(host.GetType(), "IsCurrentConversationContext", Members, 2)
                    .Invoke(host, new[] { snapshot, talk });
                if (snapshot != null)
                {
                    object binding = snapshot.GetType().GetProperty("Binding", Members).GetValue(snapshot, null);
                    object revision = binding.GetType().GetProperty("RequestRevision", Members).GetValue(binding, null);
                    values.Add("currentSnapshotRequestRevision=" + revision);
                }
            }
            catch { values.Add("currentSnapshotRequestRevision=<unavailable>"); }
            NpcActionJob latest = actor ? store.LatestNpcAction(actor.Resident.Id) : null;
            values.Add("isCurrentContext=" + current.ToString().ToLowerInvariant());
            values.Add("callbackFailures=" + session.CallbackFailureCount + "; pendingCompletions=" + session.PendingCompletionCount);
            values.Add("latestAction=" + (latest == null ? "none" : latest.Id + "/" + latest.Kind + "/" + latest.Phase +
                "/acceptedAt=" + latest.AcceptedAt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                "/finishAt=" + latest.FinishAt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
            values.Add("activeAction=" + (actor && store.ActiveNpcAction(actor.Resident.Id) != null ? store.ActiveNpcAction(actor.Resident.Id).Id : "none") +
                "; activeTask=" + (actor && store.ActiveNpcTask(actor.Resident.Id) != null ? store.ActiveNpcTask(actor.Resident.Id).Id : "none"));
            var transcript = typeof(DaggerfallTalkWindow).GetField("listboxConversation", Members)?.GetValue(talk) as DaggerfallWorkshop.Game.UserInterface.ListBox;
            if (transcript != null)
            {
                var rows = new List<string>();
                int first = Math.Max(Math.Max(0, transcriptStart), transcript.Count - 8);
                for (int index = first; index < transcript.Count; index++)
                {
                    var item = transcript.GetItem(index);
                    string text = item == null || item.textLabel == null ? "" : item.textLabel.Text ?? "";
                    if (text.Length > 120) text = text.Substring(0, 120) + "…";
                    rows.Add(index + ":" + (item == null ? "null" : item.textColor.ToString()) + ":" + text.Replace(";", ","));
                }
                values.Add("transcriptCount=" + transcript.Count + "; rows=[" + string.Join(" | ", rows.ToArray()) + "]");
            }
            else values.Add("transcript=<unavailable>");
            return string.Join("; ", values.ToArray());
        }

        private static MethodInfo Method(Type type, string name, BindingFlags flags, int count)
        { return type.GetMethods(flags).FirstOrDefault(value => value.Name == name && value.GetParameters().Length == count) ?? throw new InvalidOperationException("Required willingness probe method missing: " + name); }
        private static FieldInfo Field(Type type, string name, BindingFlags flags)
        { return type.GetField(name, flags) ?? throw new InvalidOperationException("Required willingness probe field missing: " + name); }

        private static string DescribeActionRecoveryMotion(StreetPopulation streets, ResidentActor actor)
        {
            if (!actor || actor.Resident == null) return "actor-missing";
            string residentId = actor.Resident.Id;
            object motion = null;
            var motionsField = typeof(StreetPopulation).GetField("actionMotions", Members);
            var motions = motionsField == null ? null : motionsField.GetValue(streets) as System.Collections.IDictionary;
            if (motions != null && motions.Contains(residentId)) motion = motions[residentId];
            if (motion == null) return "missing";
            Type type = motion.GetType();
            string goal = ReadMotionField(type, motion, "Goal");
            string recoveryOrigin = ReadMotionField(type, motion, "RecoveryOrigin");
            string recoveryWaiting = ReadMotionField(type, motion, "RecoveryWaiting");
            string recoverySelected = ReadMotionField(type, motion, "RecoverySelected");
            string progressAt = ReadMotionField(type, motion, "ProgressAt");
            string repathAt = ReadMotionField(type, motion, "RepathAt");
            string nativeMotion = "unavailable";
            try
            {
                MethodInfo describe = typeof(NativeMoveToWaitProbe).GetMethod("DescribeActionMotion", StaticMembers);
                if (describe != null)
                    nativeMotion = Convert.ToString(describe.Invoke(null, new object[] { streets, residentId, actor }), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) { nativeMotion = "diagnostic-error=" + ex.GetType().Name; }
            return "goal=" + goal + "; recoveryOrigin=" + recoveryOrigin + "; recoveryWaiting=" + recoveryWaiting +
                "; recoverySelected=" + recoverySelected + "; progressAt=" + progressAt + "; repathAt=" + repathAt +
                "; actionMotion=" + nativeMotion;
        }

        private static string ReadMotionField(Type type, object motion, string name)
        {
            FieldInfo field = type.GetField(name, Members);
            if (field == null) return "unknown";
            object value = field.GetValue(motion);
            if (value is float) return ((float)value).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private sealed class DeterministicTransport : ILocalDecisionTransport
        {
            private int calls;
            private int completed;
            private int blockedCall;
            private int cancelledCall;
            private int returned;
            private volatile bool blockNext;
            private readonly ManualResetEventSlim release = new ManualResetEventSlim(false);
            private readonly string[] choices;
            public int CompletedCalls { get { return Volatile.Read(ref completed); } }
            public int CallCount { get { return Volatile.Read(ref calls); } }
            internal DeterministicTransport(string[] choices) { this.choices = choices; }
            internal void PrepareNextBlockedCall() { release.Reset(); blockNext = true; }
            internal bool IsBlocked(int call) { return Volatile.Read(ref blockedCall) == call; }
            internal bool WasCancelled(int call) { return Volatile.Read(ref cancelledCall) == call; }
            internal bool HasReturned(int call) { return Volatile.Read(ref returned) >= call; }
            internal void ReleaseBlockedCall() { release.Set(); }
            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                int index = Interlocked.Increment(ref calls);
                if (blockNext)
                {
                    blockNext = false;
                    Volatile.Write(ref blockedCall, index);
                    WaitHandle.WaitAny(new[] { release.WaitHandle, cancellationToken.WaitHandle }, timeoutMilliseconds);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Volatile.Write(ref cancelledCall, index);
                        Interlocked.Increment(ref returned);
                        return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "cancelled");
                    }
                }
                string payload = new UTF8Encoding(false, true).GetString(bytes);
                var root = FullSerializer.fsJsonParser.Parse(payload);
                var criteria = root.AsDictionary["questions"].AsDictionary["decision"].AsDictionary["criteria"].AsDictionary;
                if (index > choices.Length) throw new InvalidOperationException("Deterministic willingness response fixture was exhausted.");
                string selected = choices[index - 1];
                var body = new StringBuilder("{\"model\":\"willingness-native-probe\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"");
                body.Append(selected).Append("\",\"probabilities\":{");
                bool first = true;
                foreach (string option in criteria.Keys.OrderBy(value => value, StringComparer.Ordinal))
                { if (!first) body.Append(','); first = false; body.Append('\"').Append(option).Append("\":").Append(option == selected ? '1' : '0'); }
                body.Append("}}}}");
                Interlocked.Increment(ref returned);
                Interlocked.Increment(ref completed);
                return new LocalDecisionHttpResponse(200, body.ToString(), NpcDecisionFailureKind.None);
            }
        }

        private sealed class RecordingTransport : ILocalDecisionTransport
        {
            internal sealed class Result
            {
                internal int? StatusCode; internal LocalDecisionParsedReply Parsed; internal string Purpose; internal string OriginalTurn; internal string Choice;
            }
            private readonly ILocalDecisionTransport inner;
            private readonly List<Result> results = new List<Result>();
            internal IReadOnlyList<Result> Results { get { lock (results) return results.ToArray(); } }
            internal RecordingTransport(ILocalDecisionTransport value) { inner = value; }
            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                string payload = new UTF8Encoding(false, true).GetString(bytes);
                var root = FullSerializer.fsJsonParser.Parse(payload);
                var state = FullSerializer.fsJsonParser.Parse(root.AsDictionary["state"].AsString);
                LocalDecisionHttpResponse response = inner.Send(endpoint, bytes, timeoutMilliseconds, cancellationToken);
                var result = new Result { StatusCode = response == null ? (int?)null : response.StatusCode,
                    Purpose = state.AsDictionary["purpose"].AsString, OriginalTurn = state.AsDictionary["originalTurn"].AsString,
                    Parsed = response != null && response.FailureKind == NpcDecisionFailureKind.None && response.StatusCode == 200 ? LocalDecisionWire.Parse(response.Body) : null };
                result.Choice = result.Parsed == null ? null : result.Parsed.SelectedOptionId;
                lock (results) results.Add(result);
                return response;
            }
        }
    }
}
