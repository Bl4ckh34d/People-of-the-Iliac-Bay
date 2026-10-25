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
    // Native acceptance for goal extraction, parameter binding, then D3 willingness.
    internal static class GoalBindingProbe
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Character = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string Switch = "-peopleoftheiliacbay-goal-binding-probe";
        private const string MismatchSourceTurn = "Would you wait here for ten seconds?";
        private const string Turn = "Would you wait here for eighteen seconds?";
        private const string StaleDurationOutput = "{\"intent\":\"request\",\"steps\":[{\"kind\":\"Wait\",\"source\":\"wait here for ten seconds\",\"target\":\"\",\"duration\":{\"amount\":60,\"unit\":\"game_seconds\",\"source\":\"60 seconds\"},\"quantity\":null,\"missing\":[]}],\"conditions\":[],\"exclusions\":[],\"missing\":[]}";
        private const string AuthenticWaitOutput = "{\"intent\":\"request\",\"steps\":[{\"kind\":\"Wait\",\"source\":\"wait here for eighteen seconds\",\"target\":\"\",\"duration\":{\"amount\":18,\"unit\":\"game_seconds\",\"source\":\"eighteen seconds\"},\"quantity\":null,\"missing\":[]}],\"conditions\":[],\"exclusions\":[],\"missing\":[]}";

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            if (!host || !streets || character != Character || !GameManager.Instance || !GameManager.Instance.IsReady ||
                Array.IndexOf(Environment.GetCommandLineArgs(), Switch) < 0)
                throw new InvalidOperationException("Goal binding probe requires explicit opt-in and the owned native fixture.");
            GameManager game = GameManager.Instance;
            var store = Field(typeof(PeopleOfTheIliacBayMod), "store").GetValue(host) as WorldStateStore;
            if (store == null || DaggerfallUI.UIManager == null)
                throw new InvalidOperationException("Goal binding probe cannot access native Talk state.");

            Vector3 originalPlayer = game.PlayerMotor.transform.position;
            ResidentActor actor = null;
            TownGeometry geometry = Field(typeof(StreetPopulation), "geometry").GetValue(streets) as TownGeometry;
            BuildingDirectory directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            float readyDeadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < readyDeadline &&
                (!streets.Ready || geometry == null || directory == null || geometry.Entrances.Count == 0))
            {
                yield return null;
                geometry = Field(typeof(StreetPopulation), "geometry").GetValue(streets) as TownGeometry;
                directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            }
            if (streets.Ready && geometry != null && directory != null && geometry.Entrances.Count > 0)
            {
                object fixture = Method(typeof(NativeMoveToWaitProbe), "FindFixture", StaticMembers, 7).Invoke(null,
                    new object[] { streets, store, geometry, directory, game, new HashSet<string>(StringComparer.Ordinal), null });
                if (fixture != null) actor = Field(fixture.GetType(), "Actor").GetValue(fixture) as ResidentActor;
            }
            if (!actor)
            {
                IEnumerator actorWait = (IEnumerator)Method(typeof(DecisionShadowProbe), "WaitForUnusedManagedAdult", StaticMembers, 4)
                    .Invoke(null, new object[] { store, (Action<ResidentActor>)(value => actor = value), true, originalPlayer });
                while (actorWait.MoveNext()) yield return actorWait.Current;
            }
            if (!actor) throw new InvalidOperationException("No unused managed adult is available for goal binding probe.");

            if (geometry != null && directory != null)
            {
                object[] observerArgs = { geometry, directory, actor.transform.position - Vector3.up, null };
                object found = Method(typeof(NativeMoveToWaitProbe), "TryObserverGround", StaticMembers, 4).Invoke(null, observerArgs);
                if (found is bool && (bool)found)
                {
                    game.PlayerMotor.transform.position = ((Vector3)observerArgs[3]) + Vector3.up;
                    Physics.SyncTransforms();
                }
            }
            DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToMobileNPC(actor.Person);
            DaggerfallTalkWindow talk = null;
            IEnumerator talkWait = (IEnumerator)Method(typeof(DecisionShadowProbe), "WaitForTalk", StaticMembers, 2)
                .Invoke(null, new object[] { actor, (Action<DaggerfallTalkWindow>)(value => talk = value) });
            while (talkWait.MoveNext()) yield return talkWait.Current;
            if (talk == null) throw new InvalidOperationException("Native Talk did not open for goal binding probe.");
            if (!(bool)Field(host.GetType(), "conversationSessionOpen").GetValue(host))
                Method(host.GetType(), "BeginGroundedConversation", Members, 0).Invoke(host, null);
            if (!ReferenceEquals(Field(host.GetType(), "conversationActor").GetValue(host), actor))
                throw new InvalidOperationException("Native Talk did not bind the selected resident.");

            Field(host.GetType(), "localDialogueEnabled").SetValue(host, true);
            Field(host.GetType(), "dialoguePrewarmEnabled").SetValue(host, false);
            Field(host.GetType(), "conversationBusy").SetValue(host, true);
            var textClient = Field(host.GetType(), "localDialogue").GetValue(host) as LocalDialogueClient;
            if (textClient) textClient.Cancel();

            MethodInfo capture = Method(host.GetType(), "CaptureConversationContext", Members, 3);
            MethodInfo continueExtraction = Method(host.GetType(), "ContinueGoalActionExtraction", Members, 5);
            MethodInfo pump = Method(host.GetType(), "UpdateDecisionShadow", Members, 0);
            MethodInfo captureState = Method(typeof(DecisionShadowProbe), "CaptureState", StaticMembers, 5);
            long revision = Convert.ToInt64(Field(host.GetType(), "conversationContextRequestRevision").GetValue(host)) + 1;
            object mismatchSnapshot = capture.Invoke(host, new object[] { talk, MismatchSourceTurn, revision });
            if (mismatchSnapshot == null) throw new InvalidOperationException("Could not capture current Talk for ungrounded duration quote case.");

            long d3BeforeMismatch = ReadLong(host, "goalWillingnessDispatchCount");
            int mismatchTranscriptStart = TranscriptCount(talk);
            object mismatchBefore = captureState.Invoke(null, new object[] { host, store, talk, actor, game });
            string tasksBefore = SerializeTasks(store);
            continueExtraction.Invoke(host, new object[] { talk, MismatchSourceTurn,
                Method(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", StaticMembers, 1).Invoke(null, new object[] { talk }),
                mismatchSnapshot, StaleDurationOutput });
            if (ReadLong(host, "goalWillingnessDispatchCount") != d3BeforeMismatch)
                throw new InvalidOperationException("Extraction with an ungrounded duration quote reached D3 willingness.");
            object mismatchAfter = captureState.Invoke(null, new object[] { host, store, talk, actor, game });
            AssertNoActionsChanged(mismatchBefore, mismatchAfter);
            if (!string.Equals(tasksBefore, SerializeTasks(store), StringComparison.Ordinal))
                throw new InvalidOperationException("Ungrounded duration quote changed native task records.");
            AssertNoActiveActionOrTask(actor, store, "ungrounded duration quote");
            string noChangeReply = FindReply(talk, mismatchTranscriptStart, MismatchSourceTurn);
            if (noChangeReply != "I haven't agreed to do anything new.")
                throw new InvalidOperationException("Duration provenance rejection did not append typed no-change receipt: " +
                    (noChangeReply ?? "<no reply>"));
            string mismatchPrompt = ReadString(host, "goalLastExtractionPrompt");
            string mismatchOutput = ReadString(host, "goalLastExtractionOutput");
            string mismatchExtractionStatus = ReadString(host, "goalLastExtractionStatus");
            string mismatchBindingStatus = ReadString(host, "goalLastBindingStatus");
            if (HasValidBoundGoal(host))
                throw new InvalidOperationException("Ungrounded duration quote was reported as bound.");
            if (mismatchOutput != StaleDurationOutput)
                throw new InvalidOperationException("Deterministic mismatch response was not preserved exactly: " + (mismatchOutput ?? "<null>"));
            if (!string.Equals(mismatchExtractionStatus, "Invalid", StringComparison.Ordinal) ||
                !string.Equals(mismatchBindingStatus, "not-started", StringComparison.Ordinal))
                throw new InvalidOperationException("Ungrounded duration quote did not stop at typed extraction validation: extraction=" +
                    (mismatchExtractionStatus ?? "<null>") + "; binding=" + (mismatchBindingStatus ?? "<null>") + ".");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_BINDING_MISMATCH_PASS: source=" + MismatchSourceTurn +
                "; extractionPrompt=" + (mismatchPrompt ?? "<null>") + "; extractionResponse=" + mismatchOutput +
                "; extractionStatus=" + mismatchExtractionStatus + "; bindingStatus=" + mismatchBindingStatus +
                "; typedStage=" + LatestTranscriptRows(talk, 4) + "; d3WillingnessDelta=0; activeAction=false; activeTask=false.");

            // A well-formed extraction can still omit a required parameter.
            // It must not inherit a candidate's unrelated default duration.
            string omittedDuration = AuthenticWaitOutput.Replace(
                "{\"amount\":18,\"unit\":\"game_seconds\",\"source\":\"eighteen seconds\"}", "null");
            long missingRevision = ReadLong(host, "conversationContextRequestRevision") + 1;
            object missingSnapshot = capture.Invoke(host, new object[] { talk, Turn, missingRevision });
            if (missingSnapshot == null) throw new InvalidOperationException("Could not capture omitted-duration control.");
            long beforeMissingWillingness = ReadLong(host, "goalWillingnessDispatchCount");
            string beforeMissingTasks = SerializeTasks(store);
            object beforeMissingActions = captureState.Invoke(null, new object[] { host, store, talk, actor, game });
            continueExtraction.Invoke(host, new object[] { talk, Turn,
                Method(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", StaticMembers, 1).Invoke(null, new object[] { talk }),
                missingSnapshot, omittedDuration });
            AssertNoActionsChanged(beforeMissingActions, captureState.Invoke(null, new object[] { host, store, talk, actor, game }));
            if (ReadLong(host, "goalWillingnessDispatchCount") != beforeMissingWillingness ||
                SerializeTasks(store) != beforeMissingTasks || ReadString(host, "goalLastExtractionStatus") != "Valid" ||
                ReadString(host, "goalLastBindingStatus") != "unresolved")
                throw new InvalidOperationException("Omitted Wait duration reached willingness, state mutation, or a default parameter.");
            AssertNoActiveActionOrTask(actor, store, "omitted Wait duration");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_BINDING_MISSING_DURATION_PASS: protocol-valid=true; duration=null; " +
                "default-not-used=true; d3WillingnessDelta=0; activeAction=false; activeTask=false.");

            IEnumerator repairControl = RunParameterRepairControl(host, store, actor, talk, "duration", Turn,
                omittedDuration, "steps[0].duration", new[] { "Wait" });
            while (repairControl.MoveNext()) yield return repairControl.Current;
            const string quantityTurn = "Please buy two loaves of bread.";
            const string missingQuantity = "{\"intent\":\"request\",\"steps\":[{\"kind\":\"Purchase\",\"source\":\"buy two loaves of bread\",\"target\":\"bread\",\"duration\":null,\"quantity\":null,\"missing\":[\"quantity\"]}],\"conditions\":[],\"exclusions\":[],\"missing\":[]}";
            IEnumerator quantityControl = RunParameterRepairControl(host, store, actor, talk, "quantity", quantityTurn,
                missingQuantity, "steps[0].missing", new[] { "Purchase" });
            while (quantityControl.MoveNext()) yield return quantityControl.Current;

            // Complete the deterministic short Wait first. The later live model
            // request must not inherit an active commitment and bypass extraction.
            IEnumerator acceptedWait = RunDeterministicAcceptedWait(host, store, actor, talk, Turn, AuthenticWaitOutput,
                Method(host.GetType(), "UpdateDecisionShadow", Members, 0));
            while (acceptedWait.MoveNext()) yield return acceptedWait.Current;
            string deterministicActorId = actor.Resident.Id;

            ResidentActor liveActor = null;
            IEnumerator nextActorWait = (IEnumerator)Method(typeof(DecisionShadowProbe), "WaitForUnusedManagedAdult", StaticMembers, 4)
                .Invoke(null, new object[] { store, (Action<ResidentActor>)(value => liveActor = value), true, originalPlayer });
            while (nextActorWait.MoveNext()) yield return nextActorWait.Current;
            if (!liveActor || liveActor.Resident == null || liveActor.Resident.Id == deterministicActorId)
                throw new InvalidOperationException("Live extraction phase did not select a second managed adult after deterministic Wait.");
            if (store.ActiveNpcAction(liveActor.Resident.Id) != null || store.ActiveNpcTask(liveActor.Resident.Id) != null)
                throw new InvalidOperationException("Second live-probe actor already has an active commitment.");
            actor = liveActor;
            // Native TalkToMobileNPC can open across arbitrary distances; a real
            // user conversation must still have a nearby, observable action target.
            float previousContactDistance = Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position);
            if (previousContactDistance > 8f)
            {
                object[] observerArgs = { geometry, directory, actor.transform.position - Vector3.up, null };
                if (geometry == null || directory == null ||
                    !(bool)Method(typeof(NativeMoveToWaitProbe), "TryObserverGround", StaticMembers, 4).Invoke(null, observerArgs))
                    throw new InvalidOperationException("Owned live goal fixture cannot reach verified ground near its second actor.");
                game.PlayerMotor.transform.position = ((Vector3)observerArgs[3]) + Vector3.up;
                Physics.SyncTransforms();
            }
            int waitCandidates = streets.ConversationActionCandidates(actor.Resident.Id).Count(value => value.Kind == NpcActionKind.Wait);
            float liveContactDistance = Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position);
            if (liveContactDistance > 8f || waitCandidates == 0)
                throw new InvalidOperationException("Owned live goal fixture has no current nearby native Wait target. distance=" +
                    liveContactDistance + "; waitCandidates=" + waitCandidates);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_BINDING_LIVE_CONTACT: previousDistance=" + previousContactDistance +
                "; currentDistance=" + liveContactDistance + "; waitCandidates=" + waitCandidates + ".");
            DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToMobileNPC(actor.Person);
            DaggerfallTalkWindow reopenedTalk = null;
            IEnumerator reopenWait = (IEnumerator)Method(typeof(DecisionShadowProbe), "WaitForTalk", StaticMembers, 2)
                .Invoke(null, new object[] { actor, (Action<DaggerfallTalkWindow>)(value => reopenedTalk = value) });
            while (reopenWait.MoveNext()) yield return reopenWait.Current;
            if (reopenedTalk == null) throw new InvalidOperationException("Native Talk did not reopen after deterministic Wait completion.");
            if (!(bool)Field(host.GetType(), "conversationSessionOpen").GetValue(host) ||
                !ReferenceEquals(Field(host.GetType(), "conversationActor").GetValue(host), actor))
                Method(host.GetType(), "BeginGroundedConversation", Members, 0).Invoke(host, null);
            if (!ReferenceEquals(Field(host.GetType(), "conversationActor").GetValue(host), actor))
                throw new InvalidOperationException("Reopened native Talk did not bind the second resident for live extraction.");
            talk = reopenedTalk;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_BINDING_LIVE_ACTOR_SELECTED: deterministicAcceptedActorId=" + deterministicActorId +
                "; liveExtractionActorId=" + actor.Resident.Id + "; acceptedActorReturningAction=" +
                (store.ActiveNpcAction(deterministicActorId) != null) + "; liveActorActiveAction=" +
                (store.ActiveNpcAction(actor.Resident.Id) != null) + ".");

            // Re-capture after the no-change and completed deterministic turns.
            // Production SubmitFreeText owns this separate live-model stage.
            string model = ResolveD3ModelId();
            var transport = new RecordingTransport(new LocalDecisionTransport());
            var session = new LocalDecisionSession(transport) { ExpectedModelId = model, Enabled = true };
            Method(host.GetType(), "InstallDecisionRoutingForProbe", Members, 2).Invoke(host,
                new object[] { session, new LocalDialogueConnection("http://127.0.0.1:8093/completion", 10000) });
            Field(host.GetType(), "conversationBusy").SetValue(host, false);
            int transcriptStart = TranscriptCount(talk);
            long willingnessBefore = ReadLong(host, "goalWillingnessDispatchCount");
            NpcActionJob actionBeforeLive = store.LatestNpcAction(actor.Resident.Id);
            string actionIdBeforeLive = actionBeforeLive == null ? null : actionBeforeLive.Id;
            // The preceding deterministic turn uses the same wording. Require
            // a new production trace instead of accepting its cached diagnostics.
            Field(host.GetType(), "goalLastExtractionPrompt").SetValue(host, null);
            Field(host.GetType(), "goalLastExtractionOutput").SetValue(host, null);
            Field(host.GetType(), "goalLastExtractionStatus").SetValue(host, null);
            Field(host.GetType(), "goalLastBindingStatus").SetValue(host, null);
            Method(host.GetType(), "SubmitFreeText", Members, 2).Invoke(host, new object[] { talk, Turn });

            float started = Time.realtimeSinceStartup;
            float deadline = started + 100f;
            float nextDiagnostic = started + 20f;
            while (Time.realtimeSinceStartup < deadline && !HasReply(talk, transcriptStart, Turn))
            {
                pump.Invoke(host, null);
                if (Time.realtimeSinceStartup >= nextDiagnostic)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_BINDING_LIVE_PROGRESS: elapsed=" +
                        (Time.realtimeSinceStartup - started).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                        "; extractionPrompt=" + ReadString(host, "goalLastExtractionPrompt") +
                        "; extractionResponse=" + ReadString(host, "goalLastExtractionOutput") +
                        "; extractionStatus=" + ReadString(host, "goalLastExtractionStatus") +
                        "; bindingStatus=" + ReadString(host, "goalLastBindingStatus") +
                        "; d3WillingnessDispatches=" + (ReadLong(host, "goalWillingnessDispatchCount") - willingnessBefore) +
                        "; purposes=" + transport.DescribeResults() +
                        "; transcript=" + LatestTranscriptRows(talk, 4));
                    nextDiagnostic = Time.realtimeSinceStartup + 20f;
                }
                yield return null;
            }
            pump.Invoke(host, null);
            if (!HasReply(talk, transcriptStart, Turn))
                throw new InvalidOperationException("Live production goal request did not produce a reply. " +
                    LiveDiagnostic(host, transport, talk, transcriptStart, willingnessBefore));
            string livePrompt = ReadString(host, "goalLastExtractionPrompt");
            string liveOutput = ReadString(host, "goalLastExtractionOutput");
            string repairOutput = ReadString(host, "goalTwoPassRepairOutput");
            if (!string.IsNullOrEmpty(repairOutput)) liveOutput = repairOutput;
            string liveExtractionStatus = ReadString(host, "goalLastExtractionStatus");
            string liveBindingStatus = ReadString(host, "goalLastBindingStatus");
            if (string.IsNullOrEmpty(liveOutput) || string.IsNullOrEmpty(livePrompt) || livePrompt.IndexOf(Turn, StringComparison.Ordinal) < 0 ||
                string.Equals(liveOutput, StaleDurationOutput, StringComparison.Ordinal))
                throw new InvalidOperationException("Production SubmitFreeText did not preserve goal extraction output. " +
                    LiveDiagnostic(host, transport, talk, transcriptStart, willingnessBefore));
            bool parsedGoal = HasValidBoundGoal(host);
            int willingnessDispatches = (int)(ReadLong(host, "goalWillingnessDispatchCount") - willingnessBefore);
            if (parsedGoal && willingnessDispatches < 1)
                throw new InvalidOperationException("Valid bound goal did not dispatch D3 willingness. " +
                    LiveDiagnostic(host, transport, talk, transcriptStart, willingnessBefore));
            if (parsedGoal && transport.Results.Count == 0)
                throw new InvalidOperationException("Bound goal produced no recorded D3 transport result. " +
                    LiveDiagnostic(host, transport, talk, transcriptStart, willingnessBefore));
            var liveAction = store.LatestNpcAction(actor.Resident.Id);
            bool savedLiveWait = liveAction != null && liveAction.Id != actionIdBeforeLive && liveAction.Kind == NpcActionKind.Wait;
            bool d3Accepted = transport.Results.Any(result =>
                result.Purpose == "npc-action-willingness-v1" && IsValidResult(result, model) &&
                string.Equals(result.Parsed.SelectedOptionId, "ACCEPT", StringComparison.Ordinal));
            decimal? expectedWaitGameSeconds = null;
            bool extractedWaitGoal = parsedGoal && TryExtractedWaitGameSeconds(host, liveOutput, Turn, out expectedWaitGameSeconds);
            double? savedWaitGameSeconds = savedLiveWait ? (double?)(liveAction.FinishAt - liveAction.AcceptedAt) : null;
            double timeScale = DaggerfallUnity.Instance.WorldTime.TimeScale;
            bool? savedWaitMatchesTypedDuration = null;
            if (savedLiveWait && d3Accepted && expectedWaitGameSeconds.HasValue)
            {
                savedWaitMatchesTypedDuration = Math.Abs(savedWaitGameSeconds.Value - (double)expectedWaitGameSeconds.Value) <= 0.001d;
                if (!savedWaitMatchesTypedDuration.Value)
                    throw new InvalidOperationException("Saved live Wait duration differs from extracted game-time value. " +
                        "savedGameSeconds=" + savedWaitGameSeconds.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                        "; typedGameSeconds=" + expectedWaitGameSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "; timeScale=" + timeScale.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ".");
            }
            if (d3Accepted && extractedWaitGoal && !savedLiveWait)
                throw new InvalidOperationException("D3 accepted bound Wait but native action was not saved. " +
                    LiveDiagnostic(host, transport, talk, transcriptStart, willingnessBefore));
            string purposes = transport.DescribeResults();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_BINDING_LIVE_PASS: source=" + Turn +
                "; actorId=" + actor.Resident.Id + "; deterministicAcceptedActorId=" + deterministicActorId +
                "; extractionPrompt=" + (livePrompt ?? "<null>") + "; extractionResponse=" + liveOutput +
                "; extractionStatus=" + liveExtractionStatus + "; typedStage=" + liveBindingStatus +
                "; boundGoal=" + parsedGoal.ToString().ToLowerInvariant() +
                "; willingnessDispatches=" + willingnessDispatches + "; purposes=" + purposes +
                "; d3Accepted=" + d3Accepted.ToString().ToLowerInvariant() +
                "; savedWaitId=" + (savedLiveWait ? liveAction.Id : "<none>") +
                "; savedWaitAcceptedAt=" + (savedLiveWait ? liveAction.AcceptedAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "<none>") +
                "; savedWaitFinishAt=" + (savedLiveWait ? liveAction.FinishAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "<none>") +
                "; savedWaitGameSeconds=" + (savedWaitGameSeconds.HasValue ? savedWaitGameSeconds.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "<none>") +
                "; typedWaitGameSeconds=" + (expectedWaitGameSeconds.HasValue ? expectedWaitGameSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "<none>") +
                "; savedWaitMatchesTypedDuration=" + (savedWaitMatchesTypedDuration.HasValue ? savedWaitMatchesTypedDuration.Value.ToString().ToLowerInvariant() : "<unchecked>") +
                "; worldTimeScale=" + timeScale.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; transcript=" + LatestTranscriptRows(talk, 4) + "; activeAction=" + (store.ActiveNpcAction(actor.Resident.Id) != null) +
                "; activeTask=" + (store.ActiveNpcTask(actor.Resident.Id) != null) + ".");

            Method(host.GetType(), "CancelDecisionShadow", Members, 0).Invoke(host, null);
            session.Dispose();
            DaggerfallUI.Instance.PopToHUD();
        }

        private static IEnumerator RunParameterRepairControl(PeopleOfTheIliacBayMod host, WorldStateStore store,
            ResidentActor actor, DaggerfallTalkWindow talk, string caseName, string turn, string incomplete,
            string feedbackPath, string[] kinds)
        {
            // Inject only the initial incomplete extraction. Parameter repair is a real
            // Qwen call through the normal one-pass continuation. Willingness declines
            // deterministically if repair succeeds, keeping this control effect-free.
            var transport = new DeterministicAcceptTransport("DECLINE");
            var session = new LocalDecisionSession(transport) { ExpectedModelId = "goal-binding-native-probe", Enabled = true };
            Method(host.GetType(), "InstallDecisionRoutingForProbe", Members, 2).Invoke(host,
                new object[] { session, new LocalDialogueConnection("http://127.0.0.1:1/completion", 2000) });
            object snapshot = Method(host.GetType(), "CaptureConversationContext", Members, 3).Invoke(host,
                new object[] { talk, turn, ReadLong(host, "conversationContextRequestRevision") + 1 });
            if (snapshot == null) throw new InvalidOperationException("Could not capture one-pass repair control.");
            long dispatchBefore = ReadLong(host, "goalTwoPassRepairDispatchCount");
            long completionBefore = ReadLong(host, "goalTwoPassRepairCompletionCount");
            long willingnessBefore = ReadLong(host, "goalWillingnessDispatchCount");
            string tasksBefore = SerializeTasks(store);
            NpcActionJob before = store.LatestNpcAction(actor.Resident.Id);
            string beforeId = before == null ? null : before.Id;
            int transcriptStart = TranscriptCount(talk);
            Field(host.GetType(), "goalTwoPassRepairPrompt").SetValue(host, null);
            Field(host.GetType(), "goalTwoPassRepairOutput").SetValue(host, null);
            string history = (string)Method(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", StaticMembers, 1)
                .Invoke(null, new object[] { talk });
            Field(host.GetType(), "conversationBusy").SetValue(host, true);
            Method(host.GetType(), "ContinueGoalActionInitialExtraction", Members, 5).Invoke(host,
                new object[] { talk, turn, history, snapshot, incomplete });
            MethodInfo pump = Method(host.GetType(), "UpdateDecisionShadow", Members, 0);
            float deadline = Time.realtimeSinceStartup + 100f;
            while (Time.realtimeSinceStartup < deadline && !HasReply(talk, transcriptStart, turn))
            { pump.Invoke(host, null); yield return null; }
            pump.Invoke(host, null);
            string prompt = ReadString(host, "goalTwoPassRepairPrompt");
            string output = ReadString(host, "goalTwoPassRepairOutput");
            long dispatches = ReadLong(host, "goalTwoPassRepairDispatchCount") - dispatchBefore;
            long completions = ReadLong(host, "goalTwoPassRepairCompletionCount") - completionBefore;
            long willingness = ReadLong(host, "goalWillingnessDispatchCount") - willingnessBefore;
            var frozen = new NpcGoalDecomposition(((ConversationContextSnapshot)snapshot).Binding, turn,
                NpcGoalIntent.Request, kinds);
            var parsed = NpcGoalTwoPassChat.ParseParameterRepair(output, frozen, incomplete);
            string issue = parsed.Proposal == null ? parsed.Reason : NpcGoalTwoPassChat.ParameterRepairIssue(parsed.Proposal);
            bool repaired = parsed.Status == NpcGoalExtractionStatus.Valid && parsed.Proposal != null && issue == "";
            NpcActionJob after = store.LatestNpcAction(actor.Resident.Id);
            if (!HasReply(talk, transcriptStart, turn) || dispatches != 1 || completions != 1 ||
                string.IsNullOrEmpty(prompt) || !prompt.Contains(turn) || !prompt.Contains(feedbackPath) ||
                string.IsNullOrEmpty(output) || (after == null ? null : after.Id) != beforeId ||
                SerializeTasks(store) != tasksBefore || willingness > 1 ||
                (!repaired && willingness != 0) || (transport.CallCount > 0 && transport.LastChoice != "DECLINE"))
                throw new InvalidOperationException("One-pass parameter repair violated its one-attempt or no-effect boundary. " +
                    "dispatches=" + dispatches + "; completions=" + completions + "; willingness=" + willingness +
                    "; prompt=" + prompt + "; output=" + output);
            AssertNoActiveActionOrTask(actor, store, "one-pass repair control");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_PARAMETER_REPAIR_PASS: case=" + caseName + "; initialExtraction=injected-missing-parameter; " +
                "repair=real-Qwen; attempts=1; completions=1; repaired=" + repaired.ToString().ToLowerInvariant() +
                "; willingness=" + willingness + "; controlledChoiceConfigured=DECLINE; observedChoice=" +
                (transport.LastChoice ?? "<not-dispatched>") + "; activeAction=false; activeTask=false; " +
                "repairPrompt=" + prompt + "; repairOutput=" + output + ".");
        }

        private static IEnumerator RunDeterministicAcceptedWait(PeopleOfTheIliacBayMod host, WorldStateStore store,
            ResidentActor actor, DaggerfallTalkWindow talk, string turn, string output, MethodInfo pump)
        {
            string model = "goal-binding-native-probe";
            var transport = new DeterministicAcceptTransport();
            var session = new LocalDecisionSession(transport) { ExpectedModelId = model, Enabled = true };
            Method(host.GetType(), "InstallDecisionRoutingForProbe", Members, 2).Invoke(host,
                new object[] { session, new LocalDialogueConnection("http://127.0.0.1:1/completion", 2000) });

            MethodInfo capture = Method(host.GetType(), "CaptureConversationContext", Members, 3);
            long revision = ReadLong(host, "conversationContextRequestRevision") + 1;
            object snapshot = capture.Invoke(host, new object[] { talk, turn, revision });
            if (snapshot == null) throw new InvalidOperationException("Deterministic accepted Wait could not capture fresh native Talk context.");
            string history = (string)Method(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", StaticMembers, 1)
                .Invoke(null, new object[] { talk });
            NpcActionJob before = store.LatestNpcAction(actor.Resident.Id);
            string beforeId = before == null ? null : before.Id;
            long dispatchesBefore = ReadLong(host, "goalWillingnessDispatchCount");
            int transcriptStart = TranscriptCount(talk);
            Field(host.GetType(), "conversationBusy").SetValue(host, true);
            Method(host.GetType(), "ContinueGoalActionExtraction", Members, 5).Invoke(host,
                new object[] { talk, turn, history, snapshot, output });

            float started = Time.realtimeSinceStartup;
            float deadline = started + 75f;
            NpcActionJob accepted = null;
            while (Time.realtimeSinceStartup < deadline && !HasReply(talk, transcriptStart, turn))
            {
                pump.Invoke(host, null);
                NpcActionJob latest = store.LatestNpcAction(actor.Resident.Id);
                if (latest != null && latest.Id != beforeId)
                {
                    if (transport.CallCount == 0)
                        throw new InvalidOperationException("Production goal continuation saved action before D3 willingness dispatch.");
                    if (!string.Equals(transport.LastChoice, "ACCEPT", StringComparison.Ordinal))
                        throw new InvalidOperationException("Deterministic willingness transport did not return ACCEPT before action save.");
                    accepted = latest;
                    break;
                }
                yield return null;
            }
            pump.Invoke(host, null);
            // A single Unity update can commit the action and append its reply before
            // the observer resumes, causing the loop's HasReply condition to exit.
            if (accepted == null)
            {
                NpcActionJob latest = store.LatestNpcAction(actor.Resident.Id);
                if (latest != null && latest.Id != beforeId) accepted = latest;
            }
            string extractionStatus = ReadString(host, "goalLastExtractionStatus");
            string bindingStatus = ReadString(host, "goalLastBindingStatus");
            long dispatches = ReadLong(host, "goalWillingnessDispatchCount") - dispatchesBefore;
            if (accepted == null || accepted.Kind != NpcActionKind.Wait || dispatches != 1 ||
                !string.Equals(extractionStatus, "Valid", StringComparison.Ordinal) ||
                !string.Equals(bindingStatus, "bound", StringComparison.Ordinal) || transport.CallCount != 1 ||
                !string.Equals(transport.LastChoice, "ACCEPT", StringComparison.Ordinal))
                throw new InvalidOperationException("Production continuation did not deterministically bind and accept one Wait. " +
                    "extraction=" + extractionStatus + "; binding=" + bindingStatus + "; willingnessDispatches=" + dispatches +
                    "; d3Calls=" + transport.CallCount + "; d3Choice=" + (transport.LastChoice ?? "<none>") +
                    "; latest=" + (accepted == null ? "<none>" : accepted.Id + "/" + accepted.Kind) +
                    "; transcript=" + LatestTranscriptRows(talk, Math.Max(4, TranscriptCount(talk) - transcriptStart)));

            decimal? typedSeconds = null;
            if (!TryExtractedWaitGameSeconds(host, output, turn, out typedSeconds) || !typedSeconds.HasValue)
                throw new InvalidOperationException("Authentic-source Wait output did not parse to typed game seconds.");
            double savedSeconds = accepted.FinishAt - accepted.AcceptedAt;
            if (Math.Abs(savedSeconds - (double)typedSeconds.Value) > 0.001d)
                throw new InvalidOperationException("Production accepted Wait did not preserve typed game-time value. saved=" +
                    savedSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; typed=" + typedSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            if (store.ActiveNpcAction(actor.Resident.Id) == null || store.ActiveNpcAction(actor.Resident.Id).Id != accepted.Id)
                throw new InvalidOperationException("Accepted Wait did not remain active immediately after its commit receipt.");
            string reply = FindReply(talk, transcriptStart, turn);
            if (string.IsNullOrWhiteSpace(reply) || reply.IndexOf("I agree", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Accepted Wait did not append an acceptance receipt: " + (reply ?? "<none>"));

            DaggerfallUI.Instance.PopToHUD();
            double scale = DaggerfallUnity.Instance.WorldTime.TimeScale;
            if (scale <= 0d || double.IsNaN(scale) || double.IsInfinity(scale))
                throw new InvalidOperationException("Cannot verify native Wait completion while world time scale is not positive: " + scale + ".");
            float waitDeadline = Time.realtimeSinceStartup + (float)Math.Min(60d,
                Math.Max(15d, (savedSeconds / scale) + 10d));
            while (Time.realtimeSinceStartup < waitDeadline && !NpcActionExecutor.IsTerminal(accepted))
            {
                accepted = store.ReadNpcAction(accepted.Id);
                if (accepted == null) throw new InvalidOperationException("Deterministic accepted Wait receipt disappeared before completion.");
                yield return null;
            }
            accepted = store.ReadNpcAction(accepted.Id) ?? accepted;
            bool completed = accepted.Phase == NpcActionPhase.Completed && accepted.UpdatedAt + 0.001d >= accepted.FinishAt;
            if (!completed)
                throw new InvalidOperationException("Native deterministic Wait did not complete after Talk closed. id=" + accepted.Id +
                    "; phase=" + accepted.Phase + "; finishAt=" + accepted.FinishAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; updatedAt=" + accepted.UpdatedAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; timeScale=" + scale.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ".");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_BINDING_DETERMINISTIC_ACCEPT_PASS: source=" + turn +
                "; actorId=" + actor.Resident.Id +
                "; extractionResponse=" + output + "; extractionStatus=" + extractionStatus + "; bindingStatus=" + bindingStatus +
                "; willingnessDispatches=" + dispatches + "; d3Calls=" + transport.CallCount + "; d3Choice=" + transport.LastChoice +
                "; actionId=" + accepted.Id + "; phase=" + accepted.Phase +
                "; acceptedAt=" + accepted.AcceptedAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; finishAt=" + accepted.FinishAt.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; savedGameSeconds=" + savedSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; typedGameSeconds=" + typedSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "; worldTimeScale=" + scale.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; completed=" + completed.ToString().ToLowerInvariant() + "; receipt=" + reply + ".");
            session.Dispose();
        }

        private static bool HasValidBoundGoal(PeopleOfTheIliacBayMod host)
        {
            string status = ReadString(host, "goalLastBindingStatus");
            return string.Equals(status, "bound", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "valid", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryExtractedWaitGameSeconds(PeopleOfTheIliacBayMod host, string output, string turn,
            out decimal? gameSeconds)
        {
            gameSeconds = null;
            object snapshotValue = Field(host.GetType(), "conversationContextCurrentSnapshot").GetValue(host);
            var snapshot = snapshotValue as ConversationContextSnapshot;
            if (snapshot == null) return false;
            NpcGoalExtractionResult parsed = NpcGoalChat.Parse(output, snapshot.Binding, turn);
            if (parsed.Status != NpcGoalExtractionStatus.Valid || parsed.Proposal == null || parsed.Proposal.Steps.Count != 1 ||
                parsed.Proposal.Steps[0].Kind != "Wait") return false;
            if (parsed.Proposal.Steps[0].Duration == null) return true;
            var duration = parsed.Proposal.Steps[0].Duration;
            decimal multiplier;
            switch (duration.Unit)
            {
                case NpcGoalTimeUnit.GameSeconds: multiplier = 1m; break;
                case NpcGoalTimeUnit.GameMinutes: multiplier = 60m; break;
                case NpcGoalTimeUnit.GameHours: multiplier = 3600m; break;
                default: return false;
            }
            try { gameSeconds = checked(duration.Amount * multiplier); return true; }
            catch (OverflowException) { return false; }
        }

        private static bool IsValidResult(RecordingTransport.Result result, string model)
        {
            return result != null && result.StatusCode == 200 && result.Failure == NpcDecisionFailureKind.None &&
                result.Parsed != null && result.Parsed.IsValid && result.Parsed.FailureKind == NpcDecisionFailureKind.None &&
                string.Equals(result.Parsed.ModelId, model, StringComparison.Ordinal);
        }

        private static void AssertNoActionsChanged(object before, object after)
        {
            FieldInfo actions = Field(before.GetType(), "Actions");
            if (!string.Equals((string)actions.GetValue(before), (string)actions.GetValue(after), StringComparison.Ordinal))
                throw new InvalidOperationException("Ungrounded duration quote changed native action records.");
        }

        private static void AssertNoActiveActionOrTask(ResidentActor actor, WorldStateStore store, string phase)
        {
            if (actor && (store.ActiveNpcAction(actor.Resident.Id) != null || store.ActiveNpcTask(actor.Resident.Id) != null))
                throw new InvalidOperationException("Native action or task became active during " + phase + ".");
        }

        private static string SerializeTasks(WorldStateStore store)
        {
            var tasks = store.Snapshot().NpcTasks;
            return string.Join("\n", tasks.OrderBy(task => task.Id, StringComparer.Ordinal)
                .Select(task => task.Id + "|" + task.ResidentId + "|" + task.Phase + "|" + task.Revision).ToArray());
        }

        private static string LiveDiagnostic(PeopleOfTheIliacBayMod host, RecordingTransport transport,
            DaggerfallTalkWindow talk, int transcriptStart, long willingnessBefore)
        {
            return "extractionPrompt=" + (ReadString(host, "goalLastExtractionPrompt") ?? "<null>") +
                "; extractionResponse=" + (ReadString(host, "goalLastExtractionOutput") ?? "<null>") +
                "; extractionStatus=" + (ReadString(host, "goalLastExtractionStatus") ?? "<null>") +
                "; bindingStatus=" + (ReadString(host, "goalLastBindingStatus") ?? "<null>") +
                "; willingnessDispatches=" + (ReadLong(host, "goalWillingnessDispatchCount") - willingnessBefore) +
                "; purposes=" + transport.DescribeResults() +
                "; transcript=" + LatestTranscriptRows(talk, Math.Max(4, TranscriptCount(talk) - transcriptStart));
        }

        private static string ResolveD3ModelId()
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../local-llm/models/d3-mini/vllm-d3-mini-Q4_K_M.gguf"));
            if (!System.IO.File.Exists(path)) throw new InvalidOperationException("Owned D3-mini model fixture is missing.");
            return path;
        }

        private static int TranscriptCount(DaggerfallTalkWindow talk)
        {
            var list = Field(typeof(DaggerfallTalkWindow), "listboxConversation").GetValue(talk) as ListBox;
            if (list == null) throw new InvalidOperationException("Native Talk transcript is unavailable.");
            return list.Count;
        }

        private static string LatestTranscriptRows(DaggerfallTalkWindow talk, int maximumRows)
        {
            var list = Field(typeof(DaggerfallTalkWindow), "listboxConversation").GetValue(talk) as ListBox;
            if (list == null) return "<unavailable>";
            var rows = new List<string>();
            for (int i = Math.Max(0, list.Count - maximumRows); i < list.Count; i++)
            {
                var row = list.GetItem(i);
                string text = row == null || row.textLabel == null ? "" : row.textLabel.Text ?? "";
                rows.Add("[" + i + "," + (row == null ? "" : row.textColor.ToString()) + "," +
                    text.Replace("\r", " ").Replace("\n", " ") + "]");
            }
            return string.Join(" ", rows.ToArray());
        }

        private static bool HasReply(DaggerfallTalkWindow talk, int start, string turn)
        { return FindReply(talk, start, turn) != null; }

        private static string FindReply(DaggerfallTalkWindow talk, int start, string turn)
        {
            var list = Field(typeof(DaggerfallTalkWindow), "listboxConversation").GetValue(talk) as ListBox;
            if (list == null) return null;
            for (int i = Math.Max(0, start); i + 1 < list.Count; i++)
            {
                var question = list.GetItem(i);
                var answer = list.GetItem(i + 1);
                if (question == null || question.textLabel == null || answer == null || answer.textLabel == null) continue;
                if (question.textColor == DaggerfallUI.DaggerfallQuestionTextColor && question.textLabel.Text == turn &&
                    answer.textColor != DaggerfallUI.DaggerfallQuestionTextColor && !string.IsNullOrWhiteSpace(answer.textLabel.Text))
                    return answer.textLabel.Text;
            }
            return null;
        }

        private static long ReadLong(PeopleOfTheIliacBayMod host, string name)
        { return Convert.ToInt64(Field(host.GetType(), name).GetValue(host)); }

        private static string ReadString(PeopleOfTheIliacBayMod host, string name)
        {
            FieldInfo field = host.GetType().GetField(name, Members);
            return field == null ? null : field.GetValue(host) as string;
        }

        private static MethodInfo Method(Type type, string name, BindingFlags flags, int count)
        {
            return type.GetMethods(flags).FirstOrDefault(value => value.Name == name && value.GetParameters().Length == count) ??
                throw new InvalidOperationException("Required goal binding probe method missing: " + name);
        }

        private static FieldInfo Field(Type type, string name)
        {
            return type.GetField(name, Members) ?? throw new InvalidOperationException("Required goal binding probe field missing: " + name);
        }

        private sealed class RecordingTransport : ILocalDecisionTransport
        {
            internal sealed class Result
            {
                internal readonly string Purpose;
                internal readonly string OriginalTurn;
                internal readonly int? StatusCode;
                internal readonly NpcDecisionFailureKind Failure;
                internal readonly LocalDecisionParsedReply Parsed;

                internal Result(string purpose, string originalTurn, LocalDecisionHttpResponse response)
                {
                    Purpose = purpose;
                    OriginalTurn = originalTurn;
                    StatusCode = response == null ? (int?)null : response.StatusCode;
                    Failure = response == null ? NpcDecisionFailureKind.Transport : response.FailureKind;
                    Parsed = response != null && response.StatusCode == 200 && response.FailureKind == NpcDecisionFailureKind.None
                        ? LocalDecisionWire.Parse(response.Body) : null;
                }
            }

            private readonly ILocalDecisionTransport inner;
            private readonly List<Result> results = new List<Result>();
            internal RecordingTransport(ILocalDecisionTransport inner) { this.inner = inner; }
            internal IReadOnlyList<Result> Results { get { lock (results) return results.ToArray(); } }
            internal string DescribeResults()
            {
                lock (results)
                {
                    return string.Join(",", results.Select(result => result.Purpose + ":" + result.OriginalTurn +
                        "=" + (result.StatusCode.HasValue ? result.StatusCode.Value.ToString() : "<none>") + "/" + result.Failure + "/" +
                        (result.Parsed == null ? "<unparsed>" : result.Parsed.ModelId + "/" + result.Parsed.FailureKind + "/" +
                            (result.Parsed.SelectedOptionId ?? "<none>"))).ToArray());
                }
            }

            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds,
                CancellationToken cancellationToken)
            {
                string payload = new UTF8Encoding(false, true).GetString(bytes);
                var root = FullSerializer.fsJsonParser.Parse(payload);
                var state = FullSerializer.fsJsonParser.Parse(root.AsDictionary["state"].AsString);
                string purpose = state.AsDictionary["purpose"].AsString;
                string originalTurn = state.AsDictionary["originalTurn"].AsString;
                LocalDecisionHttpResponse response = inner.Send(endpoint, bytes, timeoutMilliseconds, cancellationToken);
                lock (results) results.Add(new Result(purpose, originalTurn, response));
                return response;
            }
        }

        private sealed class DeterministicAcceptTransport : ILocalDecisionTransport
        {
            private readonly string choice;
            internal DeterministicAcceptTransport(string choice = "ACCEPT") { this.choice = choice; }
            internal int CallCount { get; private set; }
            internal string LastChoice { get; private set; }

            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds,
                CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "cancelled");
                string payload = new UTF8Encoding(false, true).GetString(bytes);
                var root = FullSerializer.fsJsonParser.Parse(payload);
                var criteria = root.AsDictionary["questions"].AsDictionary["decision"].AsDictionary["criteria"].AsDictionary;
                if (!criteria.ContainsKey(choice))
                    return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, choice + " unavailable");
                var state = FullSerializer.fsJsonParser.Parse(root.AsDictionary["state"].AsString);
                string purpose = state.AsDictionary["purpose"].AsString;
                if (!string.Equals(purpose, "npc-action-willingness-v1", StringComparison.Ordinal))
                    return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "unexpected purpose: " + purpose);
                var body = new StringBuilder("{\"model\":\"goal-binding-native-probe\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"")
                    .Append(choice).Append("\",\"probabilities\":{");
                bool first = true;
                foreach (string option in criteria.Keys.OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (!first) body.Append(',');
                    first = false;
                    body.Append('"').Append(option).Append("\":").Append(option == choice ? '1' : '0');
                }
                body.Append("}}}}");
                CallCount++;
                LastChoice = choice;
                return new LocalDecisionHttpResponse(200, body.ToString(), NpcDecisionFailureKind.None);
            }
        }
    }
}
