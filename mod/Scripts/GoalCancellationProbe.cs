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
    // Proves that closing native Talk cancels a pending willingness request and
    // rejects a deliberately late ACCEPT before any native commitment occurs.
    internal static class GoalCancellationProbe
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Character = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string Switch = "-peopleoftheiliacbay-goal-cancellation-probe";
        private const string Turn = "Would you wait here for eighteen seconds?";
        private const string TypedWait = "{\"intent\":\"request\",\"steps\":[{\"kind\":\"Wait\",\"source\":\"wait here for eighteen seconds\",\"target\":\"\",\"duration\":{\"amount\":18,\"unit\":\"game_seconds\",\"source\":\"eighteen seconds\"},\"quantity\":null,\"missing\":[]}],\"conditions\":[],\"exclusions\":[],\"missing\":[]}";

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            var args = Environment.GetCommandLineArgs();
            if (!host || !streets || character != Character || Array.IndexOf(args, Switch) < 0 ||
                Array.IndexOf(args, "-peopleoftheiliacbay-goal-extraction") < 0 ||
                !GameManager.Instance || !GameManager.Instance.IsReady || GameManager.Instance.PlayerEntity.Name != Character)
                throw new InvalidOperationException("Goal cancellation probe requires explicit opt-in, goal extraction, and its owned native fixture.");

            GameManager game = GameManager.Instance;
            var store = Field(host, "store").GetValue(host) as WorldStateStore;
            TownGeometry geometry = Field(streets, "geometry").GetValue(streets) as TownGeometry;
            BuildingDirectory directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            if (store == null || DaggerfallUI.UIManager == null)
                throw new InvalidOperationException("Goal cancellation probe cannot access native Talk state.");

            Vector3 originalPlayerPosition = game.PlayerMotor.transform.position;
            bool originalDialogueEnabled = (bool)Field(host, "localDialogueEnabled").GetValue(host);
            bool originalPrewarmEnabled = (bool)Field(host, "dialoguePrewarmEnabled").GetValue(host);
            bool originalConversationBusy = (bool)Field(host, "conversationBusy").GetValue(host);
            DaggerfallTalkWindow talk = null;
            ResidentActor actor = null;
            LocalDecisionSession session = null;
            string modelId = ResolveD3ModelId();
            var transport = new LateAcceptTransport(modelId);

            try
            {
                float readyDeadline = Time.realtimeSinceStartup + 60f;
                while (Time.realtimeSinceStartup < readyDeadline &&
                    (!streets.Ready || geometry == null || directory == null || geometry.Entrances.Count == 0))
                {
                    yield return null;
                    geometry = Field(streets, "geometry").GetValue(streets) as TownGeometry;
                    directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                }
                if (!streets.Ready || geometry == null || directory == null || geometry.Entrances.Count == 0)
                    throw new InvalidOperationException("Goal cancellation probe needs a loaded town with surveyed entrances.");

                object fixture = Method(typeof(NativeMoveToWaitProbe), "FindFixture", StaticMembers, 7).Invoke(null,
                    new object[] { streets, store, geometry, directory, game, new HashSet<string>(StringComparer.Ordinal), null });
                if (fixture != null) actor = Field(fixture.GetType(), "Actor").GetValue(fixture) as ResidentActor;
                if (!actor || store.ActiveNpcAction(actor.Resident.Id) != null || store.ActiveNpcTask(actor.Resident.Id) != null)
                    throw new InvalidOperationException("No ordinary actor with a current native Wait candidate is available.");
                Vector3 actorPositionBefore = actor.transform.position;

                DaggerfallUI.Instance.PopToHUD();
                game.TalkManager.TalkToMobileNPC(actor.Person);
                IEnumerator talkWait = (IEnumerator)Method(typeof(DecisionShadowProbe), "WaitForTalk", StaticMembers, 2)
                    .Invoke(null, new object[] { actor, (Action<DaggerfallTalkWindow>)(value => talk = value) });
                while (talkWait.MoveNext()) yield return talkWait.Current;
                if (talk == null) throw new InvalidOperationException("Native Talk did not open for the cancellation fixture.");
                if (!(bool)Field(host, "conversationSessionOpen").GetValue(host))
                    Method(host, "BeginGroundedConversation", Members, 0).Invoke(host, null);
                if (!ReferenceEquals(Field(host, "conversationActor").GetValue(host), actor))
                    throw new InvalidOperationException("Native Talk did not bind the selected ordinary actor.");

                Field(host, "localDialogueEnabled").SetValue(host, true);
                Field(host, "dialoguePrewarmEnabled").SetValue(host, false);
                Field(host, "conversationBusy").SetValue(host, true);
                var textClient = Field(host, "localDialogue").GetValue(host) as LocalDialogueClient;
                if (!textClient) throw new InvalidOperationException("Production Qwen target-binding client is unavailable.");
                textClient.Cancel();

                // Replace only the owned probe process decision session. Keep
                // production extraction, candidate selection, and Qwen binding.
                Method(host, "DisposeDecisionShadow", Members, 0).Invoke(host, null);
                session = new LocalDecisionSession(transport) { ExpectedModelId = modelId, Enabled = true };
                Field(host, "decisionRoutingSession").SetValue(host, session);
                Field(host, "decisionRoutingConnection").SetValue(host,
                    new LocalDialogueConnection("http://127.0.0.1:8093/completion", 10000));
                Field(host, "decisionShadowInitialized").SetValue(host, true);

                long requestRevision = Convert.ToInt64(Field(host, "conversationContextRequestRevision").GetValue(host)) + 1;
                object snapshot = Method(host, "CaptureConversationContext", Members, 3)
                    .Invoke(host, new object[] { talk, Turn, requestRevision });
                if (snapshot == null) throw new InvalidOperationException("Could not capture fresh Talk context for cancellation probe.");
                string history = (string)Method(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", StaticMembers, 1)
                    .Invoke(null, new object[] { talk });
                int transcriptStart = TranscriptCount(talk);
                string actionsBefore = SerializeGoalActions(store, actor.Resident.Id);
                string tasksBefore = SerializeGoalTasks(store, actor.Resident.Id);
                long dispatchesBefore = ReadLong(host, "goalWillingnessDispatchCount");

                // This authentic typed proposal enters the production binder;
                // its target still comes from the real Qwen client.
                Method(host, "ContinueGoalActionExtraction", Members, 5).Invoke(host,
                    new object[] { talk, Turn, history, snapshot, TypedWait });

                float enteredDeadline = Time.realtimeSinceStartup + 90f;
                while (Time.realtimeSinceStartup < enteredDeadline && !transport.Entered.WaitOne(0))
                {
                    Method(host, "UpdateDecisionShadow", Members, 0).Invoke(host, null);
                    yield return null;
                }
                if (!transport.Entered.WaitOne(0))
                    throw new InvalidOperationException("Production target binding did not reach controlled D3 willingness. " +
                        Diagnostic(host, store, actor, transport));
                if (!string.Equals(ReadString(host, "goalLastBindingStatus"), "bound", StringComparison.Ordinal))
                    throw new InvalidOperationException("Controlled D3 transport entered before production target binding completed.");
                if (ReadLong(host, "goalWillingnessDispatchCount") - dispatchesBefore != 1)
                    throw new InvalidOperationException("Expected exactly one production willingness dispatch before Talk closed.");
                AssertNoGoalCommitment(store, actor, actionsBefore, tasksBefore, "while willingness was blocked");
                if (HasReply(talk, transcriptStart, Turn))
                    throw new InvalidOperationException("Production appended a reply before willingness returned.");

                // Close the actual Talk window; its lifecycle must cancel the
                // pending D3 request without clearing or editing actor state.
                DaggerfallUI.Instance.PopToHUD();
                float closeDeadline = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < closeDeadline &&
                    (DaggerfallUI.UIManager.ContainsWindow(talk) || (bool)Field(host, "conversationSessionOpen").GetValue(host)))
                    yield return null;
                if (DaggerfallUI.UIManager.ContainsWindow(talk) || (bool)Field(host, "conversationSessionOpen").GetValue(host))
                    throw new InvalidOperationException("Native Talk did not close its conversation session.");
                float cancelDeadline = Time.realtimeSinceStartup + 5f;
                while (Time.realtimeSinceStartup < cancelDeadline && !transport.Cancelled.WaitOne(0)) yield return null;
                if (!transport.Cancelled.WaitOne(0))
                    throw new InvalidOperationException("Closing native Talk did not cancel the in-flight willingness token.");

                // Release an ACCEPT after cancellation. Transport intentionally
                // ignores cancellation once entered, exercising stale delivery.
                transport.Release.Set();
                float returnedDeadline = Time.realtimeSinceStartup + 15f;
                while (Time.realtimeSinceStartup < returnedDeadline && !transport.Returned.WaitOne(0)) yield return null;
                if (!transport.Returned.WaitOne(0) || !transport.ReturnedAccept)
                    throw new InvalidOperationException("Controlled transport did not return the deliberately late ACCEPT.");

                float pumpUntil = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < pumpUntil)
                {
                    Method(host, "UpdateDecisionShadow", Members, 0).Invoke(host, null);
                    yield return null;
                }
                AssertNoGoalCommitment(store, actor, actionsBefore, tasksBefore, "after late ACCEPT delivery");
                if (HasReply(talk, transcriptStart, Turn))
                    throw new InvalidOperationException("Late willingness result appended a reply to the closed Talk session.");
                if (ReadLong(host, "goalWillingnessDispatchCount") - dispatchesBefore != 1)
                    throw new InvalidOperationException("Closing Talk changed production willingness dispatch count unexpectedly.");

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_CANCELLATION_PASS: source=" + Turn +
                    "; extractionResponse=" + TypedWait +
                    "; targetBinding=production-qwen; bindingStatus=" + ReadString(host, "goalLastBindingStatus") +
                    "; willingnessDispatches=" + (ReadLong(host, "goalWillingnessDispatchCount") - dispatchesBefore) +
                    "; transportEntered=true; tokenCancelled=true; lateAcceptReturned=true; talkClosed=true;" +
                    "; goalActionAndTaskStateUnchanged=true; actorMoved=" +
                    (actor.transform.position != actorPositionBefore).ToString().ToLowerInvariant() + "; staleCommitment=false.");
            }
            finally
            {
                transport.Release.Set();
                if (DaggerfallUI.UIManager != null && DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
                    DaggerfallUI.Instance.PopToHUD();
                Method(host, "CancelDecisionShadow", Members, 0).Invoke(host, null);
                Method(host, "DisposeDecisionShadow", Members, 0).Invoke(host, null);
                if (textClientAlive(host)) textClientAlive(host).Cancel();
                Field(host, "conversationBusy").SetValue(host, originalConversationBusy);
                Field(host, "localDialogueEnabled").SetValue(host, originalDialogueEnabled);
                Field(host, "dialoguePrewarmEnabled").SetValue(host, originalPrewarmEnabled);
                if (game && game.PlayerMotor) game.PlayerMotor.transform.position = originalPlayerPosition;
                Physics.SyncTransforms();
                if (transport.Returned.WaitOne(0)) transport.Dispose();
                if (session != null) session.Dispose();
            }
        }

        private static LocalDialogueClient textClientAlive(PeopleOfTheIliacBayMod host)
        { return Field(host, "localDialogue").GetValue(host) as LocalDialogueClient; }

        private static string ResolveD3ModelId()
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                "../../local-llm/models/d3-mini/vllm-d3-mini-Q4_K_M.gguf"));
            if (!System.IO.File.Exists(path)) throw new InvalidOperationException("Owned D3-mini model fixture is missing.");
            return path;
        }

        private static void AssertNoGoalCommitment(WorldStateStore store, ResidentActor actor,
            string actionsBefore, string tasksBefore, string phase)
        {
            if (!string.Equals(actionsBefore, SerializeGoalActions(store, actor.Resident.Id), StringComparison.Ordinal) ||
                !string.Equals(tasksBefore, SerializeGoalTasks(store, actor.Resident.Id), StringComparison.Ordinal))
                throw new InvalidOperationException("A goal-proposal action/task changed " + phase + ".");
        }

        private static string SerializeGoalActions(WorldStateStore store, string residentId)
        {
            var rows = store.Snapshot().NpcActions.Where(value => value.ResidentId == residentId)
                .Where(value => value.ProposalId != null && value.ProposalId.StartsWith("goal:", StringComparison.Ordinal))
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .Select(value => value.Id + "|" + value.ProposalId + "|" + value.ResidentId + "|" + value.Kind + "|" + value.Phase + "|" +
                    value.Revision + "|" + value.CompletedEffectId);
            return string.Join("\n", rows.ToArray());
        }

        private static string SerializeGoalTasks(WorldStateStore store, string residentId)
        {
            var rows = store.Snapshot().NpcTasks.Where(value => value.ResidentId == residentId)
                .Where(value => value.ProposalId != null && value.ProposalId.StartsWith("goal:", StringComparison.Ordinal))
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .Select(value => value.Id + "|" + value.ProposalId + "|" + value.ResidentId + "|" + value.Phase + "|" + value.Revision + "|" + value.CurrentStepIndex);
            return string.Join("\n", rows.ToArray());
        }

        private static string Diagnostic(PeopleOfTheIliacBayMod host, WorldStateStore store,
            ResidentActor actor, LateAcceptTransport transport)
        {
            return "extractionStatus=" + ReadString(host, "goalLastExtractionStatus") +
                "; bindingStatus=" + ReadString(host, "goalLastBindingStatus") +
                "; willingnessDispatches=" + ReadLong(host, "goalWillingnessDispatchCount") +
                "; transportEntered=" + transport.Entered.WaitOne(0) +
                "; activeAction=" + (actor && store.ActiveNpcAction(actor.Resident.Id) != null) +
                "; activeTask=" + (actor && store.ActiveNpcTask(actor.Resident.Id) != null);
        }

        private static int TranscriptCount(DaggerfallTalkWindow talk)
        {
            var transcript = Field(talk, "listboxConversation").GetValue(talk) as ListBox;
            if (transcript == null) throw new InvalidOperationException("Native Talk transcript is unavailable.");
            return transcript.Count;
        }

        private static bool HasReply(DaggerfallTalkWindow talk, int start, string turn)
        {
            var transcript = Field(talk, "listboxConversation").GetValue(talk) as ListBox;
            if (transcript == null) return false;
            for (int index = Math.Max(0, start); index + 1 < transcript.Count; index++)
            {
                var question = transcript.GetItem(index);
                var answer = transcript.GetItem(index + 1);
                if (question != null && answer != null && question.textLabel != null && answer.textLabel != null &&
                    question.textColor == DaggerfallUI.DaggerfallQuestionTextColor && question.textLabel.Text == turn &&
                    answer.textColor != DaggerfallUI.DaggerfallQuestionTextColor && !string.IsNullOrWhiteSpace(answer.textLabel.Text))
                    return true;
            }
            return false;
        }

        private static long ReadLong(PeopleOfTheIliacBayMod host, string name)
        { return Convert.ToInt64(Field(host, name).GetValue(host)); }

        private static string ReadString(PeopleOfTheIliacBayMod host, string name)
        { return Field(host, name).GetValue(host) as string; }

        private static FieldInfo Field(object target, string name)
        {
            Type type = target as Type ?? target.GetType();
            return type.GetField(name, Members) ?? throw new InvalidOperationException("Required goal cancellation field missing: " + name);
        }

        private static MethodInfo Method(object target, string name, BindingFlags flags, int parameterCount)
        { return Method(target.GetType(), name, flags, parameterCount); }

        private static MethodInfo Method(Type type, string name, BindingFlags flags, int parameterCount)
        {
            return type.GetMethods(flags).FirstOrDefault(value => value.Name == name && value.GetParameters().Length == parameterCount) ??
                throw new InvalidOperationException("Required goal cancellation method missing: " + name);
        }

        private sealed class LateAcceptTransport : ILocalDecisionTransport, IDisposable
        {
            private readonly string modelId;
            internal readonly ManualResetEvent Entered = new ManualResetEvent(false);
            internal readonly ManualResetEvent Cancelled = new ManualResetEvent(false);
            internal readonly ManualResetEvent Release = new ManualResetEvent(false);
            internal readonly ManualResetEvent Returned = new ManualResetEvent(false);
            internal volatile bool ReturnedAccept;

            internal LateAcceptTransport(string modelId) { this.modelId = modelId; }

            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds,
                CancellationToken cancellationToken)
            {
                string payload = new UTF8Encoding(false, true).GetString(bytes);
                var root = FullSerializer.fsJsonParser.Parse(payload);
                var criteria = root.AsDictionary["questions"].AsDictionary["decision"].AsDictionary["criteria"].AsDictionary;
                if (!criteria.ContainsKey("ACCEPT"))
                    return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "accept_unavailable");
                var state = FullSerializer.fsJsonParser.Parse(root.AsDictionary["state"].AsString);
                if (!string.Equals(state.AsDictionary["purpose"].AsString, "npc-action-willingness-v1", StringComparison.Ordinal))
                    return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "unexpected_purpose");

                using (cancellationToken.Register(() => Cancelled.Set()))
                {
                    Entered.Set();
                    if (!Release.WaitOne(30000))
                        return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Timeout, "probe_release_timeout");
                    string body = AcceptedBody(criteria);
                    ReturnedAccept = true;
                    Returned.Set();
                    return new LocalDecisionHttpResponse(200, body, NpcDecisionFailureKind.None);
                }
            }

            private string AcceptedBody(IDictionary<string, FullSerializer.fsData> criteria)
            {
                var body = new StringBuilder("{\"model\":\"").Append(modelId.Replace("\\", "\\\\").Replace("\"", "\\\""))
                    .Append("\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"ACCEPT\",\"probabilities\":{");
                bool first = true;
                foreach (string option in criteria.Keys.OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (!first) body.Append(',');
                    first = false;
                    body.Append('"').Append(option).Append("\":").Append(option == "ACCEPT" ? '1' : '0');
                }
                return body.Append("}}}}").ToString();
            }

            public void Dispose()
            {
                Entered.Dispose();
                Cancelled.Dispose();
                Release.Dispose();
                Returned.Dispose();
            }
        }
    }
}
