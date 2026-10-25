using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    // Explicit command-line opt-in only. Normal tests never contact model services.
    internal static class LiveSmoke
    {
        public static int Run(string[] args)
        {
            if (args.Length != 3 || args[0] != "--live")
            {
                Console.Error.WriteLine("Usage: --live LOOPBACK_ENDPOINT EXPECTED_MODEL_ID");
                return 2;
            }
            var connection = new LocalDialogueConnection(args[1], 8000);
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, "fixture:resident", null,
                "decision-wire-smoke", 1, 1, new string('a', 32), new string('b', 64), "fixture:inn", 1);
            var unknown = ConversationContextFact<string>.Unknown();
            var snapshot = new ConversationContextSnapshot(binding, 100, unknown, unknown,
                ConversationContextFact<ConversationDialogueRole>.Unknown(), unknown, unknown,
                ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), unknown, 0, 1,
                null, new List<ConversationContextCommitment>(), new List<ConversationContextSourceHandle>());
            var request = new NpcDecisionRequest(Guid.NewGuid(), snapshot, 1,
                "Choose whether the player currently requests waiting or is only having a conversation.",
                "Would you mind waiting here for two minutes?",
                "Synthetic fixture: an NPC stands in an inn. The NPC can wait. No pending offer or active commitment. No effects are allowed.",
                new[] { new NpcDecisionOption("wait", "Current request to wait here", false),
                    new NpcDecisionOption("social", "Conversation without a current request", false),
                    new NpcDecisionOption("clarify", "Meaning is unclear", false) });
            NpcDecisionResult delivered = null;
            int callbackCount = 0;
            int callbackThread = 0;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            using (var session = new LocalDecisionSession())
            {
                session.ExpectedModelId = args[2];
                session.Enabled = true;
                if (!session.TryRequest(connection, request,
                    () => new LocalDecisionFreshness(request.Binding, request.SnapshotRevision,
                        request.CandidateSetRevision, request.Options),
                    result => { delivered = result; callbackCount++; callbackThread = Thread.CurrentThread.ManagedThreadId; }))
                    throw new InvalidOperationException("Live decision request was not queued.");
                var deadline = Stopwatch.StartNew();
                while (delivered == null && deadline.ElapsedMilliseconds < 15000)
                {
                    session.Pump();
                    Thread.Sleep(2);
                }
                session.Pump();
                bool valid = delivered != null && delivered.Status == NpcDecisionStatus.Ok && callbackCount == 1 &&
                    callbackThread == mainThread && delivered.RequestId == request.RequestId &&
                    delivered.RequestSha256 == LocalDecisionWire.Build(request).Sha256 && delivered.ModelId == args[2];
                Console.WriteLine(JsonSerializer.Serialize(new { valid, status = delivered == null ? "missing" : delivered.Status.ToString(),
                    failure = delivered == null ? "missing" : delivered.FailureKind.ToString(),
                    choice = delivered == null ? null : delivered.SelectedOptionId,
                    requestHash = delivered == null ? null : delivered.RequestSha256,
                    modelId = delivered == null ? null : delivered.ModelId,
                    latencyMs = delivered == null ? 0 : delivered.LatencyMilliseconds,
                    callbackCount, callbackOnMainThread = callbackThread == mainThread,
                    nativeEffectObserved = false, semanticAgreementScored = false }));
                return valid ? 0 : 1;
            }
        }
    }
}
