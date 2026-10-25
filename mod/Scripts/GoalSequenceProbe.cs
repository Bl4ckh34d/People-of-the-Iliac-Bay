using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    // Native accepted-path proof for ordered typed MoveTo/Wait task chains.
    internal static class GoalSequenceProbe
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Character = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string SaveName = "People - Gothway Garden";
        private const string Switch = "-peopleoftheiliacbay-goal-sequence-probe";
        private const string TwoPassSwitch = "-peopleoftheiliacbay-goal-two-pass-probe";
        private const float MaximumSampleStep = 0.5f;
        private const int WaitGameSeconds = 18;
        private const float MoveDeadlineSeconds = 240f;
        private const float BindingDeadlineSeconds = 100f;

        private sealed class AlternateBuilding
        {
            internal string Name;
            internal NpcActionOption Option;
            internal Vector3 Target;
            internal float ArrivalRange;
            internal double RouteLength;
        }

        private sealed class MotionTrace
        {
            internal Vector3 Last;
            internal float Length;
            internal float MaximumStep;

            internal MotionTrace(Vector3 start) { Last = start; }

            internal void Sample(Vector3 position)
            {
                float step = Vector3.Distance(Last, position);
                Length += step;
                if (step > MaximumStep) MaximumStep = step;
                Last = position;
            }
        }

        private sealed class RunResult
        {
            internal LocalDecisionSession OwnedSession;
            internal string Pass;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character,
            int requestedStepCount = 2)
        {
            if (!host || !streets || character != Character || !GameManager.Instance || !GameManager.Instance.IsReady)
                throw new InvalidOperationException("Goal sequence probe requires explicit opt-in and the owned native fixture.");
            GameManager game = GameManager.Instance;
            string[] arguments = Environment.GetCommandLineArgs();
            if (Array.IndexOf(arguments, Switch) < 0 && Array.IndexOf(arguments, GoalChainLengthProbe.Switch) < 0 &&
                Array.IndexOf(arguments, TwoPassSwitch) < 0)
                throw new InvalidOperationException("Goal sequence probe requires its explicit command-line flag.");
            if (requestedStepCount < 2 || requestedStepCount > NpcTaskContract.MaximumSteps ||
                (requestedStepCount != 2 && requestedStepCount != 3 && requestedStepCount != 4 && requestedStepCount != 5))
                throw new ArgumentOutOfRangeException("requestedStepCount", "Native sequence probe supports 2, 3, 4, or 5 steps.");
            if (game.PlayerMotor == null || game.PlayerEntity.Name != Character ||
                game.PlayerEnterExit.IsPlayerInsideBuilding || !streets.Ready)
                throw new InvalidOperationException("Goal sequence probe requires the owned character in a loaded exterior town.");

            FieldInfo localDialogueField = Field(host.GetType(), "localDialogueEnabled");
            FieldInfo prewarmField = Field(host.GetType(), "dialoguePrewarmEnabled");
            FieldInfo busyField = Field(host.GetType(), "conversationBusy");
            FieldInfo greetingField = Field(host.GetType(), "returningGreetingPending");
            FieldInfo greetingRetryField = Field(host.GetType(), "returningGreetingRetryAt");
            FieldInfo plannerField = Field(host.GetType(), "decisionPlannerEnabled");
            FieldInfo routingSessionField = Field(host.GetType(), "decisionRoutingSession");
            FieldInfo routingConnectionField = Field(host.GetType(), "decisionRoutingConnection");
            FieldInfo shadowField = Field(host.GetType(), "decisionShadow");
            FieldInfo shadowSessionField = Field(host.GetType(), "decisionShadowSession");
            FieldInfo shadowInitializedField = Field(host.GetType(), "decisionShadowInitialized");
            FieldInfo memoryWindowField = Field(host.GetType(), "memoryWindow");
            FieldInfo conversationOpenField = Field(host.GetType(), "conversationSessionOpen");
            if (routingSessionField.GetValue(host) != null || shadowField.GetValue(host) != null ||
                shadowSessionField.GetValue(host) != null)
                throw new InvalidOperationException("Goal sequence recovery requires no pre-existing decision-session ownership.");
            if (Convert.ToBoolean(conversationOpenField.GetValue(host), CultureInfo.InvariantCulture) ||
                memoryWindowField.GetValue(host) != null || DaggerfallUI.UIManager != null &&
                DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
                throw new InvalidOperationException("Goal sequence recovery requires Talk to be closed before the probe starts.");

            bool originalDialogueEnabled = Convert.ToBoolean(localDialogueField.GetValue(host), CultureInfo.InvariantCulture);
            bool originalPrewarm = Convert.ToBoolean(prewarmField.GetValue(host), CultureInfo.InvariantCulture);
            bool originalBusy = Convert.ToBoolean(busyField.GetValue(host), CultureInfo.InvariantCulture);
            bool originalGreetingPending = Convert.ToBoolean(greetingField.GetValue(host), CultureInfo.InvariantCulture);
            float originalGreetingRetry = Convert.ToSingle(greetingRetryField.GetValue(host), CultureInfo.InvariantCulture);
            bool originalPlanner = Convert.ToBoolean(plannerField.GetValue(host), CultureInfo.InvariantCulture);
            bool originalShadowInitialized = Convert.ToBoolean(shadowInitializedField.GetValue(host), CultureInfo.InvariantCulture);
            object originalRoutingSession = routingSessionField.GetValue(host);
            object originalRoutingConnection = routingConnectionField.GetValue(host);
            Vector3 originalPlayer = game.PlayerMotor.transform.position;
            WorldStateStore initialStore = Field(host.GetType(), "store").GetValue(host) as WorldStateStore;
            if (initialStore == null) throw new InvalidOperationException("Goal sequence probe cannot fingerprint the loaded baseline store.");
            string baselineLedger = ActionTaskFingerprint(initialStore);

            Exception runFailure = null;
            Exception recoveryFailure = null;
            var result = new RunResult();
            IEnumerator cases = DriveFlattened(RunCases(host, streets, character, requestedStepCount, result));
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = cases.MoveNext(); if (more) current = cases.Current; }
                    catch (Exception error) { runFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                try { (cases as IDisposable)?.Dispose(); }
                catch (Exception error) { if (runFailure == null) runFailure = error; }
                try
                {
                    LocalDialogueClient client = Field(host.GetType(), "localDialogue").GetValue(host) as LocalDialogueClient;
                    if (client) client.Cancel();
                    if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
                }
                catch (Exception error) { if (recoveryFailure == null) recoveryFailure = error; }
            }

            IEnumerator close = DriveFlattened(ConversationMoodProbe.CloseTalk(host));
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = close.MoveNext(); if (more) current = close.Current; }
                    catch (Exception error) { if (recoveryFailure == null) recoveryFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                try { (close as IDisposable)?.Dispose(); }
                catch (Exception error) { if (recoveryFailure == null) recoveryFailure = error; }
                try
                {
                    LocalDialogueClient client = Field(host.GetType(), "localDialogue").GetValue(host) as LocalDialogueClient;
                    if (client) client.Cancel();
                }
                catch (Exception error) { if (recoveryFailure == null) recoveryFailure = error; }
                try
                {
                    if (result.OwnedSession != null) result.OwnedSession.Dispose();
                }
                catch (Exception error) { if (recoveryFailure == null) recoveryFailure = error; }
                try
                {
                    routingSessionField.SetValue(host, originalRoutingSession);
                    routingConnectionField.SetValue(host, originalRoutingConnection);
                    shadowInitializedField.SetValue(host, originalShadowInitialized);
                }
                catch (Exception error) { if (recoveryFailure == null) recoveryFailure = error; }
            }

            IEnumerator restore = null;
            try
            {
                restore = DriveFlattened(ConversationMoodProbe.LoadSave(host, streets, character, SaveName, true));
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = restore.MoveNext(); if (more) current = restore.Current; }
                    catch (Exception error) { if (recoveryFailure == null) recoveryFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                try { (restore as IDisposable)?.Dispose(); }
                catch (Exception error) { if (recoveryFailure == null) recoveryFailure = error; }
                try
                {
                    localDialogueField.SetValue(host, originalDialogueEnabled);
                    prewarmField.SetValue(host, originalPrewarm);
                    busyField.SetValue(host, originalBusy);
                    greetingField.SetValue(host, originalGreetingPending);
                    greetingRetryField.SetValue(host, originalGreetingRetry);
                    plannerField.SetValue(host, originalPlanner);
                    routingSessionField.SetValue(host, originalRoutingSession);
                    routingConnectionField.SetValue(host, originalRoutingConnection);
                    shadowInitializedField.SetValue(host, originalShadowInitialized);
                }
                catch (Exception error) { if (recoveryFailure == null) recoveryFailure = error; }
            }

            if (recoveryFailure == null && Vector3.Distance(game.PlayerMotor.transform.position, originalPlayer) > 0.05f)
                recoveryFailure = new InvalidOperationException("Exact owned baseline reload did not restore player position.");
            if (recoveryFailure == null)
            {
                WorldStateStore restoredStore = Field(host.GetType(), "store").GetValue(host) as WorldStateStore;
                if (restoredStore == null || ActionTaskFingerprint(restoredStore) != baselineLedger)
                    recoveryFailure = new InvalidOperationException("Exact owned baseline reload did not restore the captured action/task fingerprint.");
            }
            if (recoveryFailure == null)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_BASELINE_RESTORED: character=" + character +
                    "; save=" + SaveName + "; actionTaskFingerprintExact=true; playerPositionExact=true; hostFlagsRestored=true; sessionOwnershipRestored=true.");
            if (recoveryFailure != null)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_BASELINE_RESTORE_FAIL: " + recoveryFailure);
                if (runFailure != null) Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_FAILURE_BEFORE_RESTORE: " + runFailure);
                throw new InvalidOperationException("Goal sequence probe failed to restore exact owned baseline.", recoveryFailure);
            }
            if (runFailure != null)
                throw new InvalidOperationException("Goal sequence probe failed after exact baseline restore.", runFailure);
            if (string.IsNullOrWhiteSpace(result.Pass))
                throw new InvalidOperationException("Goal sequence probe completed without a final accepted-path result.");

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_PASS: " + result.Pass);
            if (requestedStepCount >= 3)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_CHAIN_LENGTH_PASS: " + result.Pass);
        }

        private static string ActionTaskFingerprint(WorldStateStore store)
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
                        rows.Append(step.Kind).Append(':').Append(step.DurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                rows.Append('\n');
            }
            return rows.ToString();
        }

        private static IEnumerator DriveFlattened(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator current = stack.Peek();
                    bool more;
                    object yielded = null;
                    more = current.MoveNext();
                    if (more) yielded = current.Current;
                    if (!more)
                    {
                        stack.Pop();
                        (current as IDisposable)?.Dispose();
                        continue;
                    }
                    IEnumerator nested = yielded as IEnumerator;
                    if (nested != null) stack.Push(nested);
                    else yield return yielded;
                }
            }
            finally
            {
                while (stack.Count > 0)
                {
                    try { (stack.Pop() as IDisposable)?.Dispose(); }
                    catch { }
                }
            }
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character,
            int requestedStepCount, RunResult result)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (!host || !streets || character != Character || !GameManager.Instance || !GameManager.Instance.IsReady ||
                Array.IndexOf(arguments, Switch) < 0 && Array.IndexOf(arguments, GoalChainLengthProbe.Switch) < 0 &&
                Array.IndexOf(arguments, TwoPassSwitch) < 0)
                throw new InvalidOperationException("Goal sequence probe requires explicit opt-in and the owned native fixture.");
            bool twoPass = Array.IndexOf(arguments, TwoPassSwitch) >= 0;
            if (requestedStepCount < 2 || requestedStepCount > NpcTaskContract.MaximumSteps ||
                (requestedStepCount != 2 && requestedStepCount != 3 && requestedStepCount != 4 && requestedStepCount != 5))
                throw new ArgumentOutOfRangeException("requestedStepCount", "Native sequence probe supports 2, 3, 4, or 5 steps.");

            GameManager game = GameManager.Instance;
            if (game.PlayerEntity.Name != Character || game.PlayerEnterExit.IsPlayerInsideBuilding || !streets.Ready)
                throw new InvalidOperationException("Goal sequence probe requires the owned character in a loaded exterior town.");
            var store = Field(typeof(PeopleOfTheIliacBayMod), "store").GetValue(host) as WorldStateStore;
            var geometry = Field(typeof(StreetPopulation), "geometry").GetValue(streets) as TownGeometry;
            BuildingDirectory directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            if (store == null || geometry == null || directory == null)
                throw new InvalidOperationException("Goal sequence probe cannot access current native town state.");

            float entranceDeadline = Time.realtimeSinceStartup + 60f;
            while (streets.Ready && geometry.Entrances.Count == 0 && Time.realtimeSinceStartup < entranceDeadline)
                yield return null;
            if (!streets.Ready || geometry.Entrances.Count == 0)
                throw new InvalidOperationException("No surveyed native Building entrance is available for sequence fixture.");

            Vector3 originalPlayer = game.PlayerMotor.transform.position;
            var rejectedResidents = new HashSet<string>(StringComparer.Ordinal);
            object[] fixtureArguments = { streets, store, geometry, directory, game, rejectedResidents, null };
            MethodInfo findFixture = Method(typeof(NativeMoveToWaitProbe), "FindFixture", StaticMembers, 7);
            object fixture = findFixture.Invoke(null, fixtureArguments);
            if (fixture == null)
                throw new InvalidOperationException("No normal reachable named-Building fixture is available: " +
                    (fixtureArguments[6] as string ?? "no fixture reason was returned."));

            var fixtureType = fixture.GetType();
            ResidentActor actor = Field(fixtureType, "Actor").GetValue(fixture) as ResidentActor;
            var moveOption = Field(fixtureType, "MoveOption").GetValue(fixture) as NpcActionOption;
            string buildingName = Field(fixtureType, "BuildingName").GetValue(fixture) as string;
            Vector3 expectedTarget = (Vector3)Field(fixtureType, "Target").GetValue(fixture);
            Vector3 observerGround = (Vector3)Field(fixtureType, "ObserverGround").GetValue(fixture);
            Vector3 actorStart = (Vector3)Field(fixtureType, "ActorStart").GetValue(fixture);
            float arrivalRange = Convert.ToSingle(Field(fixtureType, "ArrivalRange").GetValue(fixture), CultureInfo.InvariantCulture);
            float initialDistance = Convert.ToSingle(Field(fixtureType, "InitialDistance").GetValue(fixture), CultureInfo.InvariantCulture);
            float observerGap = Convert.ToSingle(Field(fixtureType, "ObserverGap").GetValue(fixture), CultureInfo.InvariantCulture);
            if (!actor || actor.Resident == null || moveOption == null || moveOption.Target == null ||
                moveOption.Kind != NpcActionKind.MoveTo || moveOption.Target.Kind != NpcActionTargetKind.Building ||
                string.IsNullOrWhiteSpace(buildingName) || arrivalRange <= 0f || initialDistance < 5f || initialDistance > 40f)
                throw new InvalidOperationException("Shared fixture did not return a normal reachable named-Building MoveTo.");
            AlternateBuilding alternate = null;
            var alternateDiagnostics = new List<string>();
            if (requestedStepCount >= 3)
            {
                while (fixture != null)
                {
                    fixtureType = fixture.GetType();
                    actor = Field(fixtureType, "Actor").GetValue(fixture) as ResidentActor;
                    moveOption = Field(fixtureType, "MoveOption").GetValue(fixture) as NpcActionOption;
                    buildingName = Field(fixtureType, "BuildingName").GetValue(fixture) as string;
                    expectedTarget = (Vector3)Field(fixtureType, "Target").GetValue(fixture);
                    observerGround = (Vector3)Field(fixtureType, "ObserverGround").GetValue(fixture);
                    actorStart = (Vector3)Field(fixtureType, "ActorStart").GetValue(fixture);
                    arrivalRange = Convert.ToSingle(Field(fixtureType, "ArrivalRange").GetValue(fixture), CultureInfo.InvariantCulture);
                    initialDistance = Convert.ToSingle(Field(fixtureType, "InitialDistance").GetValue(fixture), CultureInfo.InvariantCulture);
                    observerGap = Convert.ToSingle(Field(fixtureType, "ObserverGap").GetValue(fixture), CultureInfo.InvariantCulture);
                    string candidateDiagnostics;
                    alternate = FindAlternateBuilding(streets, geometry, directory, actor, moveOption,
                        expectedTarget, out candidateDiagnostics);
                    alternateDiagnostics.Add("actor=" + (actor == null || actor.Resident == null ? "<missing>" : actor.Resident.Id) + "; " + candidateDiagnostics);
                    if (alternate != null) break;
                    if (actor == null || actor.Resident == null) break;
                    rejectedResidents.Add(actor.Resident.Id);
                    fixtureArguments[5] = rejectedResidents;
                    fixture = findFixture.Invoke(null, fixtureArguments);
                }
                if (alternate == null)
                    throw new InvalidOperationException("No fixture actor has two currently offered named Building destinations with a native route under 120m. " +
                        string.Join(" | ", alternateDiagnostics.ToArray()) + "; fixtureReason=" + (fixtureArguments[6] as string ?? "<none>"));
            }
            string alternateName = alternate == null ? buildingName : alternate.Name;
            NpcActionOption alternateMove = alternate == null ? moveOption : alternate.Option;
            Vector3 alternateTarget = alternate == null ? expectedTarget : alternate.Target;
            string turn;
            string output = BuildTypedOutput(buildingName, alternateName, requestedStepCount, out turn);
            if (twoPass) output = null;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_FIXTURE: actorId=" + actor.Resident.Id +
                "; building=" + buildingName + "; buildingKey=" + moveOption.Target.BuildingKey +
                "; alternateBuilding=" + alternateName + "; alternateBuildingKey=" + alternateMove.Target.BuildingKey +
                "; controlledStepCount=" + requestedStepCount + "; extraction=" + (twoPass ? "live-two-pass" : "injected-typed-fixture") +
                "; liveExtraction=" + twoPass.ToString().ToLowerInvariant() + "; livePlanningProof=false" +
                "; target=" + expectedTarget.ToString("F3") + "; initialDistance=" + initialDistance.ToString("F3", CultureInfo.InvariantCulture) +
                "; arrivalRange=" + arrivalRange.ToString("F3", CultureInfo.InvariantCulture) +
                "; alternateArrivalRange=" + (alternate == null ? arrivalRange : alternate.ArrivalRange).ToString("F3", CultureInfo.InvariantCulture) +
                "; alternateRouteLength=" + (alternate == null ? 0d : alternate.RouteLength).ToString("F2", CultureInfo.InvariantCulture) +
                "; observerGap=" + observerGap.ToString("F3", CultureInfo.InvariantCulture) +
                "; observerStaged=" + observerGround.ToString("F3") + "; originalPlayer=" + originalPlayer.ToString("F3") +
                "; actorTeleport=false; customRoute=false; waitGameSeconds=" + WaitGameSeconds + ".");

            DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToMobileNPC(actor.Person);
            DaggerfallTalkWindow talk = null;
            IEnumerator talkWait = (IEnumerator)Method(typeof(DecisionShadowProbe), "WaitForTalk", StaticMembers, 2)
                .Invoke(null, new object[] { actor, (Action<DaggerfallTalkWindow>)(value => talk = value) });
            while (talkWait.MoveNext()) yield return talkWait.Current;
            if (talk == null) throw new InvalidOperationException("Native Talk did not open for the sequence fixture actor.");
            if (!(bool)Field(host.GetType(), "conversationSessionOpen").GetValue(host) ||
                !ReferenceEquals(Field(host.GetType(), "conversationActor").GetValue(host), actor))
                Method(host.GetType(), "BeginGroundedConversation", Members, 0).Invoke(host, null);
            if (!ReferenceEquals(Field(host.GetType(), "conversationActor").GetValue(host), actor))
                throw new InvalidOperationException("Native Talk did not bind the exact fixture actor.");

            Field(host.GetType(), "localDialogueEnabled").SetValue(host, true);
            Field(host.GetType(), "dialoguePrewarmEnabled").SetValue(host, false);
            Field(host.GetType(), "conversationBusy").SetValue(host, true);
            var textClient = Field(host.GetType(), "localDialogue").GetValue(host) as LocalDialogueClient;
            if (textClient) textClient.Cancel();

            var transport = new DeterministicAcceptTransport(turn);
            var session = new LocalDecisionSession(transport)
                { ExpectedModelId = "goal-sequence-native-probe", Enabled = true };
            result.OwnedSession = session;
            Method(host.GetType(), "InstallDecisionRoutingForProbe", Members, 2).Invoke(host,
                new object[] { session, new LocalDialogueConnection("http://127.0.0.1:8093/completion", 10000) });

            MethodInfo capture = Method(host.GetType(), "CaptureConversationContext", Members, 3);
            MethodInfo continueExtraction = Method(host.GetType(), "ContinueGoalActionExtraction", Members, 5);
            MethodInfo pump = Method(host.GetType(), "UpdateDecisionShadow", Members, 0);
            long revision = ReadLong(host, "conversationContextRequestRevision") + 1;
            object snapshot = capture.Invoke(host, new object[] { talk, turn, revision });
            if (snapshot == null) throw new InvalidOperationException("Could not capture current Talk context for typed sequence.");
            string history = (string)Method(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", StaticMembers, 1)
                .Invoke(null, new object[] { talk });
            int transcriptStart = TranscriptCount(talk);
            string actorId = actor.Resident.Id;
            if (store.ActiveNpcAction(actorId) != null || store.ActiveNpcTask(actorId) != null)
                throw new InvalidOperationException("Sequence fixture actor acquired a commitment before extraction.");
            long willingnessBefore = ReadLong(host, "goalWillingnessDispatchCount");
            long decompositionDispatchBefore = twoPass ? ReadLong(host, "goalTwoPassDecompositionDispatchCount") : 0;
            long decompositionCompletionBefore = twoPass ? ReadLong(host, "goalTwoPassDecompositionCompletionCount") : 0;
            long parameterDispatchBefore = twoPass ? ReadLong(host, "goalTwoPassParameterDispatchCount") : 0;
            long parameterCompletionBefore = twoPass ? ReadLong(host, "goalTwoPassParameterCompletionCount") : 0;
            long repairDispatchBefore = twoPass ? ReadLong(host, "goalTwoPassRepairDispatchCount") : 0;
            long repairCompletionBefore = twoPass ? ReadLong(host, "goalTwoPassRepairCompletionCount") : 0;

            if (twoPass)
            {
                Method(host.GetType(), "TryBeginGoalActionConversation", Members, 4).Invoke(host,
                    new object[] { talk, turn, history, snapshot });
            }
            else
            {
                NpcGoalExtractionResult preflight = NpcGoalChat.Parse(output, snapshot is ConversationContextSnapshot
                    ? ((ConversationContextSnapshot)snapshot).Binding : null, turn);
                if (preflight.Status != NpcGoalExtractionStatus.Valid || preflight.Proposal == null ||
                    preflight.Proposal.Steps.Count != requestedStepCount)
                    throw new InvalidOperationException("Typed fixture did not preserve requested step count: " + preflight.Reason);
                for (int i = 0; i < requestedStepCount; i++)
                {
                    var parsed = preflight.Proposal.Steps[i];
                    string expectedKind = i % 2 == 0 ? "MoveTo" : "Wait";
                    string expectedBuilding = ((i / 2) % 2) == 0 ? buildingName : alternateName;
                    if (parsed.Kind != expectedKind || parsed.Source == null || string.IsNullOrWhiteSpace(parsed.Source.Text))
                        throw new InvalidOperationException("Typed fixture changed action order or source span at step " + i + ".");
                    if (i % 2 == 0)
                    {
                        if (parsed.TargetText != expectedBuilding || parsed.Duration != null)
                            throw new InvalidOperationException("Typed MoveTo did not retain its exact current Building target at step " + i + ".");
                    }
                    else if (parsed.TargetText != "there" || parsed.Duration == null ||
                        parsed.Duration.Amount != WaitGameSeconds || parsed.Duration.Unit != NpcGoalTimeUnit.GameSeconds)
                        throw new InvalidOperationException("Typed Wait did not retain exact ordered source and game-seconds duration at step " + i + ".");
                }
                continueExtraction.Invoke(host, new object[] { talk, turn, history, snapshot, output });
            }
            float bindingDeadline = Time.realtimeSinceStartup + BindingDeadlineSeconds;
            NpcTaskPlan acceptedTask = null;
            bool twoPassStagesLogged = false;
            while (Time.realtimeSinceStartup < bindingDeadline && !HasReply(host, talk, transcriptStart, turn))
            {
                pump.Invoke(host, null);
                if (twoPass && string.Equals(ReadString(host, "goalLastBindingStatus"), "binding", StringComparison.Ordinal) &&
                    (ReadLong(host, "goalTwoPassDecompositionDispatchCount") - decompositionDispatchBefore != 1 ||
                     ReadLong(host, "goalTwoPassDecompositionCompletionCount") - decompositionCompletionBefore != 1 ||
                     ReadLong(host, "goalTwoPassParameterDispatchCount") - parameterDispatchBefore != 1 ||
                     ReadLong(host, "goalTwoPassParameterCompletionCount") - parameterCompletionBefore != 1))
                {
                    LogTwoPassStages(host, decompositionDispatchBefore, decompositionCompletionBefore,
                        parameterDispatchBefore, parameterCompletionBefore);
                    twoPassStagesLogged = true;
                    throw new InvalidOperationException("Live extraction entered target binding before both two-pass model stages completed exactly once.");
                }
                var active = store.ActiveNpcTask(actorId);
                if (active != null) acceptedTask = active;
                if (acceptedTask != null)
                {
                    if (transport.CallCount == 0 || transport.LastChoice != "ACCEPT")
                        throw new InvalidOperationException("Goal sequence task was saved before deterministic D3 ACCEPT.");
                    break;
                }
                if (store.ActiveNpcAction(actorId) != null && transport.CallCount == 0)
                    throw new InvalidOperationException("Native action started before the task reached deterministic D3 willingness.");
                yield return null;
            }
            pump.Invoke(host, null);
            // The model callback can save the task and append its reply in one
            // Unity Update before this observer resumes. Re-read after the loop
            // rather than treating the completed reply as evidence of no task.
            if (acceptedTask == null) acceptedTask = store.ActiveNpcTask(actorId);
            string extractionStatus = ReadString(host, "goalLastExtractionStatus");
            string bindingStatus = ReadString(host, "goalLastBindingStatus");
            int willingnessDispatches = (int)(ReadLong(host, "goalWillingnessDispatchCount") - willingnessBefore);
            if (twoPass && !twoPassStagesLogged) LogTwoPassStages(host, decompositionDispatchBefore, decompositionCompletionBefore,
                parameterDispatchBefore, parameterCompletionBefore);
            if (twoPass) LogTwoPassRepair(host, repairDispatchBefore, repairCompletionBefore);
            if (acceptedTask == null || acceptedTask.Steps == null ||
                acceptedTask.Steps.Count != requestedStepCount || willingnessDispatches != 1 || transport.CallCount != 1 ||
                !string.Equals(transport.LastChoice, "ACCEPT", StringComparison.Ordinal) ||
                !string.Equals(transport.LastPurpose, "npc-action-willingness-v1", StringComparison.Ordinal) ||
                !string.Equals(transport.LastTurn, turn, StringComparison.Ordinal) ||
                !string.Equals(extractionStatus, "Valid", StringComparison.Ordinal) ||
                !string.Equals(bindingStatus, "bound", StringComparison.Ordinal) ||
                twoPass && (ReadLong(host, "goalTwoPassDecompositionDispatchCount") - decompositionDispatchBefore != 1 ||
                    ReadLong(host, "goalTwoPassDecompositionCompletionCount") - decompositionCompletionBefore != 1 ||
                    ReadLong(host, "goalTwoPassParameterDispatchCount") - parameterDispatchBefore != 1 ||
                    ReadLong(host, "goalTwoPassParameterCompletionCount") - parameterCompletionBefore != 1))
            {
                string diagnostic = "extraction=" + (extractionStatus ?? "<null>") + "; binding=" + (bindingStatus ?? "<null>") +
                    "; willingnessDispatches=" + willingnessDispatches + "; d3Calls=" + transport.CallCount +
                    "; d3Purpose=" + (transport.LastPurpose ?? "<none>") + "; d3Choice=" + (transport.LastChoice ?? "<none>") +
                    "; task=" + (acceptedTask == null ? "<none>" : acceptedTask.Id + "/" + acceptedTask.Phase) +
                    "; requestedSteps=" + requestedStepCount + "; reply=" + (FindReply(talk, transcriptStart, turn) ?? "<none>") +
                    (twoPass ? "; liveTurn=" + turn : "; typedResponse=" + output);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_UNRESOLVED: actorId=" + actorId + "; " + diagnostic);
                throw new InvalidOperationException("Production goal continuation did not bind and accept complete " +
                    requestedStepCount + "-step sequence: " + diagnostic);
            }

            NpcTaskPlan task = store.ReadNpcTask(acceptedTask.Id);
            if (task == null) throw new InvalidOperationException("Accepted native task receipt could not be read back.");
            string taskId = task.Id;
            var moveAction = store.ReadNpcAction(task.Id + ":step:0");
            if (task.Phase != NpcTaskPhase.Running || task.CurrentStepIndex != 0 || moveAction == null ||
                moveAction.Kind != NpcActionKind.MoveTo || moveAction.Phase != NpcActionPhase.Accepted)
                throw new InvalidOperationException("Saved task did not begin with one accepted native MoveTo while Talk stayed open.");
            for (int i = 0; i < requestedStepCount; i++)
            {
                var step = task.Steps[i];
                if (i % 2 == 0)
                {
                    int expectedKey = ((i / 2) % 2) == 0 ? moveOption.Target.BuildingKey : alternateMove.Target.BuildingKey;
                    if (step.Kind != NpcActionKind.MoveTo || step.Target == null || step.Target.Kind != NpcActionTargetKind.Building ||
                        step.Target.BuildingKey != expectedKey || step.DurationSeconds != 0)
                        throw new InvalidOperationException("Saved MoveTo step changed current native Building target at index " + i + ".");
                }
                else if (step.Kind != NpcActionKind.Wait || step.Target == null || step.DurationSeconds != WaitGameSeconds ||
                    !NpcTaskPositionBinding.IsBoundWaitAfterMoveTo(task.Steps[i - 1], step) ||
                    step.Target.Kind != NpcActionTargetKind.Building || step.Target.BuildingKey != task.Steps[i - 1].Target.BuildingKey)
                    throw new InvalidOperationException("Saved Wait step lost exact previous MoveTo target or typed duration at index " + i + ".");
                if (i > 0 && store.ReadNpcAction(task.Id + ":step:" + i) != null)
                    throw new InvalidOperationException("A later native action was dispatched before Talk closed.");
            }
            if (Vector3.Distance(actor.transform.position - Vector3.up, actorStart) > 0.25f)
                throw new InvalidOperationException("Actor moved before native Talk closed for accepted sequence.");
            string reply = FindReply(talk, transcriptStart, turn);
            if (string.IsNullOrWhiteSpace(reply) || reply.IndexOf("I agree", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Accepted ordered task did not append its native acceptance receipt: " + (reply ?? "<none>"));

            float talkOpenUntil = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < talkOpenUntil)
            {
                pump.Invoke(host, null);
                task = store.ReadNpcTask(taskId);
                moveAction = store.ReadNpcAction(taskId + ":step:0");
                bool futureActionDispatched = false;
                for (int i = 1; i < requestedStepCount; i++)
                    if (store.ReadNpcAction(taskId + ":step:" + i) != null) futureActionDispatched = true;
                if (task == null || moveAction == null || task.Phase != NpcTaskPhase.Running || task.CurrentStepIndex != 0 ||
                    moveAction.Phase != NpcActionPhase.Accepted || futureActionDispatched ||
                    Vector3.Distance(actor.transform.position - Vector3.up, actorStart) > 0.25f)
                    throw new InvalidOperationException("Accepted sequence had native effects before Talk closed.");
                yield return null;
            }
            task = store.ReadNpcTask(taskId);
            moveAction = store.ReadNpcAction(taskId + ":step:0");
            if (task == null || moveAction == null) throw new InvalidOperationException("Accepted task disappeared before Talk close.");

            bool targetResolved = TryResolveTarget(streets, moveAction, out Vector3 resolvedTarget,
                out float resolvedRange, out string resolveReason);
            if (!targetResolved || Vector3.Distance(resolvedTarget, expectedTarget) > 0.25f ||
                Math.Abs(resolvedRange - arrivalRange) > 0.25f)
                throw new InvalidOperationException("Production MoveTo target no longer matches shared native fixture: resolved=" + targetResolved +
                    "; reason=" + (resolveReason ?? "<none>") + "; expected=" + expectedTarget.ToString("F3") +
                    "; actual=" + resolvedTarget.ToString("F3") + "; range=" + resolvedRange.ToString("F3", CultureInfo.InvariantCulture) + ".");

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_SAVED_PASS: actorId=" + actorId + "; taskId=" + task.Id +
                "; taskPhase=" + task.Phase + "; currentStep=" + task.CurrentStepIndex +
                "; stepCount=" + task.Steps.Count + "; stepKinds=" + string.Join(",", task.Steps.Select(s => s.Kind.ToString()).ToArray()) +
                "; targetKeys=" + string.Join(",", task.Steps.Select(s => s.Target.BuildingKey.ToString(CultureInfo.InvariantCulture)).ToArray()) +
                "; waitGameSeconds=" + WaitGameSeconds.ToString(CultureInfo.InvariantCulture) +
                "; willingnessDispatches=" + willingnessDispatches + "; d3Choice=" + transport.LastChoice +
                "; receipt=" + reply + "; actorPositionUnchangedWhileTalkOpen=true.");

            DaggerfallUI.Instance.PopToHUD();
            var trace = new MotionTrace(actor.transform.position - Vector3.up);
            double waitScale = DaggerfallUnity.Instance.WorldTime.TimeScale;
            if (waitScale <= 0d || double.IsNaN(waitScale) || double.IsInfinity(waitScale))
                throw new InvalidOperationException("World game-time scale is invalid for the accepted Wait probe: " + waitScale + ".");
            var waitStarts = new Dictionary<int, Vector3>();
            var waitDrifts = new Dictionary<int, float>();
            var effectIds = new HashSet<string>(StringComparer.Ordinal);
            var stepEffects = new Dictionary<int, string>();
            var resolvedTargets = new Dictionary<int, Vector3>();
            var resolvedArrivalRanges = new Dictionary<int, float>();
            resolvedTargets.Add(moveOption.Target.BuildingKey, expectedTarget);
            resolvedArrivalRanges.Add(moveOption.Target.BuildingKey, arrivalRange);
            if (alternateMove.Target.BuildingKey != moveOption.Target.BuildingKey)
            {
                resolvedTargets.Add(alternateMove.Target.BuildingKey, alternateTarget);
                resolvedArrivalRanges.Add(alternateMove.Target.BuildingKey, alternate.ArrivalRange);
            }
            var completedMoves = new HashSet<int>();
            var completedWaits = new HashSet<int>();
            float startRealtime = Time.realtimeSinceStartup;
            int moveCount = (requestedStepCount + 1) / 2;
            int waitCount = requestedStepCount / 2;
            float deadline = startRealtime + MoveDeadlineSeconds * moveCount +
                (float)(WaitGameSeconds * waitCount / waitScale * 2d) + 60f;
            float nextProgress = startRealtime + 5f;
            task = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (!actor || !actor.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Fixture actor disappeared during accepted ordered goal execution.");
                trace.Sample(actor.transform.position - Vector3.up);
                task = store.ReadNpcTask(taskId);
                if (task == null) throw new InvalidOperationException("Saved ordered goal receipt disappeared during physical execution.");
                if (task.Phase == NpcTaskPhase.Failed || task.Phase == NpcTaskPhase.Cancelled)
                    throw new InvalidOperationException("Ordered goal failed: task=" + task.Phase + "; reason=" + task.FailureReason +
                        "; motion=" + DescribeMotion(streets, actorId, actor) + ".");

                for (int i = 0; i < requestedStepCount; i++)
                {
                    NpcActionJob action = store.ReadNpcAction(taskId + ":step:" + i);
                    if (i > task.CurrentStepIndex && action != null)
                        throw new InvalidOperationException("Later native step started before its predecessor completed: index=" + i + ".");
                    if (i < task.CurrentStepIndex && (action == null || action.Phase != NpcActionPhase.Completed))
                        throw new InvalidOperationException("Task advanced without a completed ordered action receipt at index " + i + ".");
                    if (action == null) continue;
                    if (NpcActionExecutor.IsTerminal(action) && action.Phase != NpcActionPhase.Completed)
                        throw new InvalidOperationException("Native step failed: index=" + i + "; phase=" + action.Phase +
                            "; reason=" + action.FailureReason + ".");
                    if (action.Phase != NpcActionPhase.Completed) continue;
                    string priorEffect;
                    if (stepEffects.TryGetValue(i, out priorEffect))
                    {
                        if (!string.Equals(priorEffect, action.CompletedEffectId, StringComparison.Ordinal))
                            throw new InvalidOperationException("Completed action receipt changed after observation at index " + i + ".");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(action.CompletedEffectId) || !effectIds.Add(action.CompletedEffectId))
                        throw new InvalidOperationException("Action effect receipt was missing or replayed at index " + i + ".");
                    stepEffects.Add(i, action.CompletedEffectId);
                    if (i % 2 == 0)
                    {
                        int key = action.Target.BuildingKey;
                        Vector3 expected;
                        float currentRange;
                        string currentReason;
                        if (!TryResolveTarget(streets, action, out Vector3 currentTarget, out currentRange, out currentReason))
                            throw new InvalidOperationException("Completed MoveTo target cannot be resolved: " + currentReason + ".");
                        if (!resolvedTargets.TryGetValue(key, out expected) ||
                            Vector3.Distance(expected, currentTarget) > 0.25f ||
                            Vector3.Distance(actor.transform.position - Vector3.up, expected) > currentRange + 0.001f)
                            throw new InvalidOperationException("MoveTo completed outside its verified native Building arrival range at index " + i + ".");
                        completedMoves.Add(i);
                    }
                    else
                    {
                        NpcActionJob previousMove = store.ReadNpcAction(taskId + ":step:" + (i - 1));
                        if (previousMove == null || previousMove.Phase != NpcActionPhase.Completed ||
                            !NpcTaskPositionBinding.HasCompletedMoveToReceipt(task.Steps[i - 1], previousMove, task.Steps[i]) ||
                            action.AcceptedAt + 0.000001d < previousMove.UpdatedAt ||
                            Math.Abs(action.FinishAt - action.AcceptedAt - WaitGameSeconds) > 0.001d ||
                            action.UpdatedAt + 0.001d < action.FinishAt ||
                            action.UpdatedAt - action.AcceptedAt > WaitGameSeconds + Math.Max(2d, waitScale * 2d))
                            throw new InvalidOperationException("Wait lacks exact prior MoveTo receipt or typed game-second timing at index " + i + ".");
                        completedWaits.Add(i);
                    }
                }

                int current = task.CurrentStepIndex;
                if (task.Phase == NpcTaskPhase.Running && current < requestedStepCount && current % 2 == 1)
                {
                    int moveIndex = current - 1;
                    NpcActionJob priorMove = store.ReadNpcAction(taskId + ":step:" + moveIndex);
                    NpcActionJob waitAction = store.ReadNpcAction(taskId + ":step:" + current);
                    if (priorMove == null || priorMove.Phase != NpcActionPhase.Completed || waitAction == null ||
                        waitAction.Kind != NpcActionKind.Wait || !NpcTaskPositionBinding.HasCompletedMoveToReceipt(
                            task.Steps[moveIndex], priorMove, task.Steps[current]) ||
                        waitAction.AcceptedAt + 0.000001d < priorMove.UpdatedAt)
                        throw new InvalidOperationException("Wait was dispatched before exact prior MoveTo completion at index " + current + ".");
                    if (!waitStarts.ContainsKey(current))
                    {
                        Vector3 point = actor.transform.position - Vector3.up;
                        Vector3 expected = resolvedTargets[priorMove.Target.BuildingKey];
                        float expectedRange;
                        if (!resolvedArrivalRanges.TryGetValue(priorMove.Target.BuildingKey, out expectedRange) ||
                            Vector3.Distance(point, expected) > expectedRange + 0.001f)
                            throw new InvalidOperationException("Wait started outside exact native Building arrival range at index " + current + ".");
                        waitStarts.Add(current, point);
                        waitDrifts.Add(current, 0f);
                    }
                    if (!NpcActionExecutor.IsTerminal(waitAction) && ActionTime() <= waitAction.FinishAt)
                    {
                        float drift = Vector3.Distance(actor.transform.position - Vector3.up, waitStarts[current]);
                        if (drift > waitDrifts[current]) waitDrifts[current] = drift;
                    }
                }

                if (task.Phase == NpcTaskPhase.Completed && task.CurrentStepIndex == requestedStepCount) break;

                if (Time.realtimeSinceStartup >= nextProgress)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_PROGRESS: actorId=" + actorId + "; taskId=" + task.Id +
                        "; elapsedReal=" + (Time.realtimeSinceStartup - startRealtime).ToString("F1", CultureInfo.InvariantCulture) +
                        "; task=" + task.Phase + "/" + task.CurrentStepIndex + "; stepCount=" + requestedStepCount +
                        "; actual=" + (actor.transform.position - Vector3.up).ToString("F3") +
                        "; walk=" + trace.Length.ToString("F2", CultureInfo.InvariantCulture) +
                        "; maxStep=" + trace.MaximumStep.ToString("F3", CultureInfo.InvariantCulture) +
                        "; motion=" + DescribeMotion(streets, actorId, actor) + ".");
                    nextProgress += 5f;
                }
                yield return null;
            }
            task = store.ReadNpcTask(taskId);
            if (task == null || task.Phase != NpcTaskPhase.Completed || task.CurrentStepIndex != requestedStepCount ||
                completedMoves.Count != moveCount || completedWaits.Count != waitCount || effectIds.Count != requestedStepCount ||
                trace.Length < Math.Max(2f, initialDistance - arrivalRange - 1f) || trace.MaximumStep > MaximumSampleStep ||
                waitDrifts.Values.Any(value => value > MaximumSampleStep))
                throw new InvalidOperationException("Physical ordered goal evidence failed: task=" + (task == null ? "<missing>" : task.Phase.ToString()) +
                    "; stepIndex=" + (task == null ? -1 : task.CurrentStepIndex) + "; expectedSteps=" + requestedStepCount +
                    "; completedMoves=" + completedMoves.Count + "; completedWaits=" + completedWaits.Count +
                    "; effects=" + effectIds.Count + "; walk=" + trace.Length.ToString("F2", CultureInfo.InvariantCulture) +
                    "; maxStep=" + trace.MaximumStep.ToString("F3", CultureInfo.InvariantCulture) +
                    "; waitDriftMax=" + (waitDrifts.Count == 0 ? 0f : waitDrifts.Values.Max()).ToString("F3", CultureInfo.InvariantCulture) + ".");

            const float routineHardLimitSeconds = 600f;
            const float routineStallLimitSeconds = 60f;
            const float routineProgressMetres = 0.5f;
            float routineStarted = Time.realtimeSinceStartup;
            float routineDeadline = routineStarted + routineHardLimitSeconds;
            float nextRoutineDiagnostic = Time.realtimeSinceStartup + 20f;
            float lastRoutineProgressAt = routineStarted;
            float bestRoutineRouteRemaining = float.PositiveInfinity;
            Vector3 lastRoutineProgressPosition = actor.transform.position - Vector3.up;
            bool routineStallWatchArmed = false;
            while (Time.realtimeSinceStartup < routineDeadline &&
                (store.ActiveNpcTask(actorId) != null || store.ActiveNpcAction(actorId) != null))
            {
                float routeRemaining;
                bool routeDistanceKnown;
                Vector3 routinePosition;
                string routineProgress = DescribeRoutineProgress(streets, geometry, store, actorId, actor,
                    out routeRemaining, out routeDistanceKnown, out routinePosition);
                var activeReturn = store.ActiveNpcAction(actorId);
                if (activeReturn != null && activeReturn.ReturningToRoutine)
                {
                    if (!routineStallWatchArmed)
                    {
                        bestRoutineRouteRemaining = routeDistanceKnown ? routeRemaining : float.PositiveInfinity;
                        lastRoutineProgressPosition = routinePosition;
                        lastRoutineProgressAt = Time.realtimeSinceStartup;
                        routineStallWatchArmed = true;
                    }
                    else
                    {
                        bool physicalProgress = Vector3.Distance(lastRoutineProgressPosition, routinePosition) >= routineProgressMetres;
                        bool routeProgress = routeDistanceKnown && bestRoutineRouteRemaining - routeRemaining >= routineProgressMetres;
                        if (routeProgress) bestRoutineRouteRemaining = routeRemaining;
                        if (physicalProgress || routeProgress)
                        {
                            lastRoutineProgressPosition = routinePosition;
                            lastRoutineProgressAt = Time.realtimeSinceStartup;
                        }
                    }
                    if (Time.realtimeSinceStartup - lastRoutineProgressAt >= routineStallLimitSeconds)
                        throw new InvalidOperationException("Routine return made no " + routineProgressMetres.ToString("F1", CultureInfo.InvariantCulture) +
                            "m physical displacement or route-remaining progress for " + routineStallLimitSeconds.ToString("F0", CultureInfo.InvariantCulture) +
                            "s; " + routineProgress + ".");
                }
                if (Time.realtimeSinceStartup >= nextRoutineDiagnostic)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_ROUTINE_PROGRESS: actorId=" + actorId +
                        "; taskActive=" + (store.ActiveNpcTask(actorId) != null) +
                        "; actionActive=" + (store.ActiveNpcAction(actorId) != null) +
                        "; elapsedReal=" + (Time.realtimeSinceStartup - routineStarted).ToString("F1", CultureInfo.InvariantCulture) +
                        "; bestRouteRemainingM=" + (routeDistanceKnown ? bestRoutineRouteRemaining.ToString("F2", CultureInfo.InvariantCulture) : "unknown") +
                        "; noPhysicalOrRouteProgressSeconds=" + (routineStallWatchArmed ?
                            (Time.realtimeSinceStartup - lastRoutineProgressAt).ToString("F1", CultureInfo.InvariantCulture) : "unarmed") +
                        "; " + routineProgress + ".");
                    nextRoutineDiagnostic = Time.realtimeSinceStartup + 20f;
                }
                yield return null;
            }
            task = store.ReadNpcTask(taskId);
            if (task == null || task.Phase != NpcTaskPhase.Completed || task.CurrentStepIndex != requestedStepCount ||
                store.ActiveNpcTask(actorId) != null || store.ActiveNpcAction(actorId) != null)
                throw new InvalidOperationException("Completed ordered goal did not physically release routine ownership within " +
                    routineHardLimitSeconds.ToString("F0", CultureInfo.InvariantCulture) + "s; " +
                    DescribeRoutineProgress(streets, geometry, store, actorId, actor, out _, out _, out _) + ".");

            string pass = "actorId=" + actorId + "; taskId=" + task.Id +
                "; taskPhase=" + task.Phase + "; stepCount=" + requestedStepCount + "; building=" + buildingName +
                "; alternateBuilding=" + alternateName + "; buildingKey=" + moveOption.Target.BuildingKey +
                "; actorTeleport=false; collisionBypass=false; walk=" + trace.Length.ToString("F2", CultureInfo.InvariantCulture) +
                "m; maxStep=" + trace.MaximumStep.ToString("F3", CultureInfo.InvariantCulture) +
                "m; completedEffects=" + string.Join(",", task.Steps.Select(step => step.CompletedEffectId).ToArray()) +
                "; waitGameSeconds=" + WaitGameSeconds.ToString(CultureInfo.InvariantCulture) +
                "; waitDriftMax=" + (waitDrifts.Count == 0 ? 0f : waitDrifts.Values.Max()).ToString("F3", CultureInfo.InvariantCulture) +
                "m; routineOwnerReleased=true; extraction=" + (twoPass ? "live-two-pass" : "injected-typed-fixture") +
                "; d3Acceptance=deterministic-ACCEPT; liveExtraction=" + twoPass.ToString().ToLowerInvariant() +
                (twoPass ? "; decompositionDispatchCount=1; decompositionCompletionCount=1; parameterDispatchCount=1; parameterCompletionCount=1; repairDispatchCount=" +
                    (ReadLong(host, "goalTwoPassRepairDispatchCount") - repairDispatchBefore) + "; repairCompletionCount=" +
                    (ReadLong(host, "goalTwoPassRepairCompletionCount") - repairCompletionBefore) +
                    "; stagesValidBeforeBinding=true; targetBinding=production-local-model; modelSemanticEvidence=fixture-specific; universalSemanticClaim=false" : "; livePlanningProof=false") + ".";
            result.Pass = pass;
        }

        private static string BuildTypedOutput(string buildingName, string alternateName, int stepCount, out string turn)
        {
            var turnParts = new List<string>();
            var stepObjects = new List<string>();
            int movementCount = (stepCount + 1) / 2;
            for (int i = 0; i < stepCount; i++)
            {
                if (i % 2 == 0)
                {
                    int movementIndex = i / 2;
                    string target = movementIndex % 2 == 0 ? buildingName : alternateName;
                    string source = movementIndex == 0 ? "Go to " + target :
                        movementIndex == movementCount - 1 && target == buildingName ? "return to " + target : "go to " + target;
                    turnParts.Add(source);
                    stepObjects.Add("{\"kind\":\"MoveTo\",\"source\":\"" + JsonEscape(source) + "\",\"target\":\"" +
                        JsonEscape(target) + "\",\"duration\":null,\"quantity\":null,\"missing\":[]}");
                }
                else
                {
                    const string waitSource = "wait there for eighteen seconds";
                    turnParts.Add(waitSource);
                    stepObjects.Add("{\"kind\":\"Wait\",\"source\":\"" + waitSource + "\",\"target\":\"there\"," +
                        "\"duration\":{\"amount\":" + WaitGameSeconds.ToString(CultureInfo.InvariantCulture) +
                        ",\"unit\":\"game_seconds\",\"source\":\"eighteen seconds\"},\"quantity\":null,\"missing\":[]}");
                }
            }
            turn = string.Join(", then ", turnParts.ToArray()) + ".";
            return "{\"intent\":\"request\",\"steps\":[" + string.Join(",", stepObjects.ToArray()) +
                "],\"conditions\":[],\"exclusions\":[],\"missing\":[]}";
        }

        private static void LogTwoPassStages(PeopleOfTheIliacBayMod host, long dispatchBefore,
            long completionBefore, long parameterDispatchBefore, long parameterCompletionBefore)
        {
            string decompositionPrompt = ReadString(host, "goalTwoPassDecompositionPrompt");
            string decompositionOutput = ReadString(host, "goalTwoPassDecompositionOutput");
            string decompositionRequest = ReadString(host, "goalTwoPassDecompositionRequest");
            string decompositionResponse = ReadString(host, "goalTwoPassDecompositionResponse");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_TWO_PASS_STAGE: pass=decomposition; dispatchCount=" +
                (ReadLong(host, "goalTwoPassDecompositionDispatchCount") - dispatchBefore) + "; completionCount=" +
                (ReadLong(host, "goalTwoPassDecompositionCompletionCount") - completionBefore) +
                "; traceComplete=" + TraceComplete(decompositionPrompt, decompositionOutput, decompositionRequest, decompositionResponse) +
                "; prompt=" + OneLine(decompositionPrompt) + "; output=" + OneLine(decompositionOutput) +
                "; request=" + OneLine(decompositionRequest) + "; response=" + OneLine(decompositionResponse) + ".");
            string parameterPrompt = ReadString(host, "goalTwoPassParameterPrompt");
            string parameterOutput = ReadString(host, "goalTwoPassParameterOutput");
            string parameterRequest = ReadString(host, "goalTwoPassParameterRequest");
            string parameterResponse = ReadString(host, "goalTwoPassParameterResponse");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_TWO_PASS_STAGE: pass=parameters; dispatchCount=" +
                (ReadLong(host, "goalTwoPassParameterDispatchCount") - parameterDispatchBefore) + "; completionCount=" +
                (ReadLong(host, "goalTwoPassParameterCompletionCount") - parameterCompletionBefore) +
                "; traceComplete=" + TraceComplete(parameterPrompt, parameterOutput, parameterRequest, parameterResponse) +
                "; prompt=" + OneLine(parameterPrompt) + "; output=" + OneLine(parameterOutput) +
                "; request=" + OneLine(parameterRequest) + "; response=" + OneLine(parameterResponse) + ".");
        }

        private static void LogTwoPassRepair(PeopleOfTheIliacBayMod host, long dispatchBefore, long completionBefore)
        {
            long dispatched = ReadLong(host, "goalTwoPassRepairDispatchCount") - dispatchBefore;
            long completed = ReadLong(host, "goalTwoPassRepairCompletionCount") - completionBefore;
            if (dispatched < 0 || dispatched >= NpcGoalParameterAttempts.MaximumAttempts || completed != dispatched)
                throw new InvalidOperationException("Parameter repair exceeded two corrections or continued before its current completion.");
            string prompt = ReadString(host, "goalTwoPassRepairPrompt");
            string output = ReadString(host, "goalTwoPassRepairOutput");
            string request = ReadString(host, "goalTwoPassRepairRequest");
            string response = ReadString(host, "goalTwoPassRepairResponse");
            if (dispatched > 0 && TraceComplete(prompt, output, request, response) != "true")
                throw new InvalidOperationException("Parameter repair lacks a fresh real-model trace.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_TWO_PASS_REPAIR: dispatchCount=" + dispatched +
                "; completionCount=" + completed + "; prompt=" + OneLine(prompt) + "; output=" + OneLine(output) +
                "; request=" + OneLine(request) + "; response=" + OneLine(response) + ".");
        }

        private static string OneLine(string value)
        { return (value ?? "<null>").Replace("\r", "\\r").Replace("\n", "\\n"); }

        private static string TraceComplete(params string[] values)
        {
            foreach (string value in values) if (string.IsNullOrWhiteSpace(value)) return "false";
            return "true";
        }

        private static AlternateBuilding FindAlternateBuilding(StreetPopulation streets, TownGeometry geometry,
            BuildingDirectory directory, ResidentActor actor, NpcActionOption firstMove, Vector3 firstTarget,
            out string diagnostics)
        {
            diagnostics = "offeredBuildingCandidates=0; distinctCandidates=0; resolvedCandidates=0; routedCandidates=0; selected=<none>";
            if (!streets || geometry == null || directory == null || !actor || firstMove == null) return null;
            var candidates = streets.ConversationActionCandidates(actor.Resident.Id);
            if (candidates == null) return null;
            Vector3 firstLocal = directory.transform.InverseTransformPoint(firstTarget);
            int offered = 0;
            int distinct = 0;
            int resolved = 0;
            int routed = 0;
            double shortest = double.MaxValue;
            AlternateBuilding selected = null;
            var candidateDetails = new List<string>();
            var seenKeys = new HashSet<int>();
            foreach (var candidate in candidates)
            {
                if (candidate == null || candidate.Kind != NpcActionKind.MoveTo || candidate.Target == null ||
                    candidate.Target.Kind != NpcActionTargetKind.Building) continue;
                offered++;
                if (candidate.Target.BuildingKey == firstMove.Target.BuildingKey) continue;
                if (!seenKeys.Add(candidate.Target.BuildingKey))
                {
                    candidateDetails.Add("key=" + candidate.Target.BuildingKey + ":duplicate");
                    continue;
                }
                distinct++;
                string name = GameManager.Instance.TalkManager.GetBuildingNameForBuildingKey(candidate.Target.BuildingKey);
                if (string.IsNullOrWhiteSpace(name))
                {
                    candidateDetails.Add("key=" + candidate.Target.BuildingKey + ":unnamed");
                    continue;
                }
                var probe = new NpcActionJob { Kind = NpcActionKind.MoveTo, Target = candidate.Target };
                if (!TryResolveTarget(streets, probe, out Vector3 target, out float arrival, out string reason) || arrival <= 0f)
                {
                    candidateDetails.Add("key=" + candidate.Target.BuildingKey + ":unresolved=" + (reason ?? "invalid-arrival"));
                    continue;
                }
                resolved++;
                Vector3 local = directory.transform.InverseTransformPoint(target);
                var route = geometry.RouteBetweenPoints(new SavedRoutePoint { X = firstLocal.x, Z = firstLocal.z },
                    new SavedRoutePoint { X = local.x, Z = local.z });
                if (route == null || route.Count < 2)
                {
                    candidateDetails.Add("key=" + candidate.Target.BuildingKey + ":no-route");
                    continue;
                }
                routed++;
                double routeLength = RouteLength(route);
                float straightDistance = Vector3.Distance(firstTarget, target);
                bool legal = straightDistance >= arrival + 2f && routeLength <= 120d;
                candidateDetails.Add("key=" + candidate.Target.BuildingKey + ":route=" + routeLength.ToString("F2", CultureInfo.InvariantCulture) +
                    "m; straight=" + straightDistance.ToString("F2", CultureInfo.InvariantCulture) + "m; arrival=" +
                    arrival.ToString("F2", CultureInfo.InvariantCulture) + "m; legal=" + legal);
                if (!legal || routeLength >= shortest) continue;
                shortest = routeLength;
                selected = new AlternateBuilding { Name = name, Option = candidate, Target = target,
                    ArrivalRange = arrival, RouteLength = routeLength };
            }
            diagnostics = "offeredBuildingCandidates=" + offered + "; distinctCandidates=" + distinct +
                "; resolvedCandidates=" + resolved + "; routedCandidates=" + routed +
                "; selected=" + (selected == null ? "<none>" : selected.Option.Target.BuildingKey + "/" + shortest.ToString("F2", CultureInfo.InvariantCulture) + "m") +
                "; candidates=[" + string.Join(" | ", candidateDetails.ToArray()) + "]";
            return selected;
        }

        private static double RouteLength(IList<SavedRoutePoint> route)
        {
            double length = 0d;
            if (route == null) return length;
            for (int i = 1; i < route.Count; i++)
            {
                double dx = route[i].X - route[i - 1].X;
                double dz = route[i].Z - route[i - 1].Z;
                length += Math.Sqrt(dx * dx + dz * dz);
            }
            return length;
        }

        private static string JsonEscape(string value)
        {
            var builder = new StringBuilder();
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20) builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }

        private static bool TryResolveTarget(StreetPopulation streets, NpcActionJob job,
            out Vector3 target, out float arrivalRange, out string reason)
        {
            object[] arguments = { job, Vector3.zero, 0f, null };
            try
            {
                if (!(bool)Method(typeof(StreetPopulation), "ResolveActionTarget", Members, 4).Invoke(streets, arguments))
                {
                    target = Vector3.zero;
                    arrivalRange = 0f;
                    reason = arguments[3] as string ?? "Target cannot be resolved.";
                    return false;
                }
                target = (Vector3)arguments[1];
                arrivalRange = (float)arguments[2];
                reason = null;
                return true;
            }
            catch (Exception error)
            {
                target = Vector3.zero;
                arrivalRange = 0f;
                reason = error.GetBaseException().Message;
                return false;
            }
        }

        private static string DescribeMotion(StreetPopulation streets, string residentId, ResidentActor actor)
        {
            try
            {
                return (string)Method(typeof(NativeMoveToWaitProbe), "DescribeActionMotion", StaticMembers, 3)
                    .Invoke(null, new object[] { streets, residentId, actor });
            }
            catch (Exception error) { return "diagnostic-error=" + error.GetBaseException().GetType().Name; }
        }

        private static string DescribeRoutineProgress(StreetPopulation streets, TownGeometry geometry, WorldStateStore store,
            string residentId, ResidentActor actor, out float routeRemaining, out bool routeDistanceKnown, out Vector3 actual)
        {
            routeRemaining = float.PositiveInfinity;
            routeDistanceKnown = false;
            var job = store.ActiveNpcAction(residentId);
            actual = actor ? actor.transform.position - Vector3.up : Vector3.zero;
            string action = job == null ? "action=<none>" : "actionId=" + job.Id + "; actionKind=" + job.Kind +
                "; actionRevision=" + job.Revision + "; actionPhase=" + job.Phase +
                "; returning=" + job.ReturningToRoutine + "; positionKind=" +
                (job.LastPosition == null ? "<none>" : job.LastPosition.Kind.ToString()) +
                "; lastPosition=" + (job.LastPosition == null ? "<none>" :
                    job.LastPosition.X.ToString("F2", CultureInfo.InvariantCulture) + "," +
                    job.LastPosition.Y.ToString("F2", CultureInfo.InvariantCulture) + "," +
                    job.LastPosition.Z.ToString("F2", CultureInfo.InvariantCulture)) +
                "; transition=" + (job.RoutineRecoveryTransition == null ? "none" :
                    job.RoutineRecoveryTransition.OriginBuildingKey + "->" +
                    (job.RoutineRecoveryTransition.Destination == null ? "<none>" : job.RoutineRecoveryTransition.Destination.Kind.ToString()));
            string recovery = "recoveryMotion=missing";
            try
            {
                var motionsField = typeof(StreetPopulation).GetField("actionMotions", Members);
                var motions = motionsField == null ? null : motionsField.GetValue(streets) as System.Collections.IDictionary;
                if (motions != null && motions.Contains(residentId))
                {
                    object motion = motions[residentId];
                    var type = motion.GetType();
                    bool selected = (bool)type.GetField("RecoverySelected", Members).GetValue(motion);
                    bool waiting = (bool)type.GetField("RecoveryWaiting", Members).GetValue(motion);
                    int origin = (int)type.GetField("RecoveryOrigin", Members).GetValue(motion);
                    Vector3 goal = (Vector3)type.GetField("Goal", Members).GetValue(motion);
                    float goalGap = actor ? Vector3.Distance(actual, goal) : float.PositiveInfinity;
                    bool goalGapKnown = actor && selected && !float.IsNaN(goalGap) && !float.IsInfinity(goalGap);
                    var outdoor = type.GetField("Outdoor", Members).GetValue(motion) as IList<SavedRoutePoint>;
                    int next = (int)type.GetField("Next", Members).GetValue(motion);
                    string routeCount = outdoor == null ? "null" : outdoor.Count.ToString(CultureInfo.InvariantCulture);
                    string nextPoint = outdoor != null && next >= 0 && next < outdoor.Count ?
                        outdoor[next].X.ToString("F2", CultureInfo.InvariantCulture) + "," +
                        outdoor[next].Z.ToString("F2", CultureInfo.InvariantCulture) : "none";
                    string remaining = "unknown";
                    if (outdoor != null && next >= 0 && next < outdoor.Count && geometry != null)
                    {
                        double length = Vector3.Distance(actual, geometry.ScenePoint(new RoutePoint(outdoor[next].X, outdoor[next].Z)));
                        for (int i = next + 1; i < outdoor.Count; i++)
                            length += Vector3.Distance(geometry.ScenePoint(new RoutePoint(outdoor[i - 1].X, outdoor[i - 1].Z)),
                                geometry.ScenePoint(new RoutePoint(outdoor[i].X, outdoor[i].Z)));
                        remaining = length.ToString("F2", CultureInfo.InvariantCulture);
                        routeRemaining = (float)length;
                        routeDistanceKnown = !float.IsNaN(routeRemaining) && !float.IsInfinity(routeRemaining);
                    }
                    recovery = "recoverySelected=" + selected + "; recoveryWaiting=" + waiting +
                        "; recoveryOrigin=" + origin + "; recoveryGoal=" + goal.ToString("F3") +
                        "; goalGap=" + (goalGapKnown ? goalGap.ToString("F2", CultureInfo.InvariantCulture) : "unknown") +
                        "; routeCount=" + routeCount + "; next=" + next + "; nextPoint=" + nextPoint +
                        "; routeRemainingM=" + remaining;
                }
            }
            catch (Exception error) { recovery = "recoveryMotion=diagnostic-error:" + error.GetBaseException().GetType().Name; }
            return "actorGround=" + actual.ToString("F3") + "; " + action + "; " + recovery +
                "; actionMotion=" + DescribeMotion(streets, residentId, actor);
        }

        private static double ActionTime()
        {
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            return clock.ToSeconds() + (clock.Second - Math.Floor(clock.Second));
        }

        private static bool HasReply(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk, int start, string turn)
        {
            LocalDialogueClient client = Field(host.GetType(), "localDialogue").GetValue(host) as LocalDialogueClient;
            if (Convert.ToBoolean(Field(host.GetType(), "conversationBusy").GetValue(host), CultureInfo.InvariantCulture) ||
                (client && client.IsBusy)) return false;
            return FindReply(talk, start, turn) != null;
        }

        private static string FindReply(DaggerfallTalkWindow talk, int start, string turn)
        {
            var list = Field(typeof(DaggerfallTalkWindow), "listboxConversation").GetValue(talk) as ListBox;
            if (list == null) return null;
            for (int index = Math.Max(0, start); index + 1 < list.Count; index++)
            {
                var question = list.GetItem(index);
                var answer = list.GetItem(index + 1);
                if (question == null || question.textLabel == null || answer == null || answer.textLabel == null) continue;
                if (question.textColor == DaggerfallUI.DaggerfallQuestionTextColor && question.textLabel.Text == turn &&
                    answer.textColor != DaggerfallUI.DaggerfallQuestionTextColor && !string.IsNullOrWhiteSpace(answer.textLabel.Text))
                    return answer.textLabel.Text;
            }
            return null;
        }

        private static int TranscriptCount(DaggerfallTalkWindow talk)
        {
            var list = Field(typeof(DaggerfallTalkWindow), "listboxConversation").GetValue(talk) as ListBox;
            if (list == null) throw new InvalidOperationException("Native Talk transcript is unavailable.");
            return list.Count;
        }

        private static long ReadLong(PeopleOfTheIliacBayMod host, string name)
        { return Convert.ToInt64(Field(host.GetType(), name).GetValue(host), CultureInfo.InvariantCulture); }

        private static string ReadString(PeopleOfTheIliacBayMod host, string name)
        { return Field(host.GetType(), name).GetValue(host) as string; }

        private static MethodInfo Method(Type type, string name, BindingFlags flags, int parameterCount)
        {
            return type.GetMethods(flags).FirstOrDefault(value => value.Name == name &&
                value.GetParameters().Length == parameterCount) ??
                throw new InvalidOperationException("Required goal sequence probe method missing: " + name);
        }

        private static FieldInfo Field(Type type, string name)
        {
            return type.GetField(name, Members) ??
                throw new InvalidOperationException("Required goal sequence probe field missing: " + name);
        }

        private sealed class DeterministicAcceptTransport : ILocalDecisionTransport
        {
            private readonly string expectedTurn;
            private int calls;
            internal int CallCount { get { return Volatile.Read(ref calls); } }
            internal string LastChoice { get; private set; }
            internal string LastPurpose { get; private set; }
            internal string LastTurn { get; private set; }

            internal DeterministicAcceptTransport(string turn) { expectedTurn = turn; }

            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds,
                CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "cancelled");
                string payload = new UTF8Encoding(false, true).GetString(bytes);
                var root = FullSerializer.fsJsonParser.Parse(payload);
                var criteria = root.AsDictionary["questions"].AsDictionary["decision"].AsDictionary["criteria"].AsDictionary;
                var state = FullSerializer.fsJsonParser.Parse(root.AsDictionary["state"].AsString);
                LastPurpose = state.AsDictionary["purpose"].AsString;
                LastTurn = state.AsDictionary["originalTurn"].AsString;
                if (!criteria.ContainsKey("ACCEPT") || LastPurpose != "npc-action-willingness-v1" || LastTurn != expectedTurn)
                    return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport,
                        "deterministic fixture precondition failed");

                var body = new StringBuilder("{\"model\":\"goal-sequence-native-probe\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"ACCEPT\",\"probabilities\":{");
                bool first = true;
                foreach (string option in criteria.Keys.OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (!first) body.Append(',');
                    first = false;
                    body.Append('"').Append(option).Append("\":").Append(option == "ACCEPT" ? '1' : '0');
                }
                body.Append("}}}}");
                LastChoice = "ACCEPT";
                Interlocked.Increment(ref calls);
                return new LocalDecisionHttpResponse(200, body.ToString(), NpcDecisionFailureKind.None);
            }
        }
    }
}
