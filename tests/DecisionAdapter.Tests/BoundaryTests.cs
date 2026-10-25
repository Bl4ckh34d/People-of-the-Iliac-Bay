using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class BoundaryTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("a successful decision requires a confirmed HTTP 200 status", MissingStatus));
            cases.Add(("adapter rejects unoffered choices and inconsistent probability vectors", InvalidSelection));
            cases.Add(("each Pump delivers at most one callback even with immediate reentrant work", PumpBudget));
        }

        private static void MissingStatus()
        {
            var result = Evaluate(new LocalDecisionHttpResponse(null, Body("wait", "\"wait\":1,\"social\":0"), NpcDecisionFailureKind.None));
            Check.True(result.Status != NpcDecisionStatus.Ok, "No confirmed HTTP status must never become a successful decision.");
            Check.Equal(null, result.SelectedOptionId);
        }

        private static void InvalidSelection()
        {
            foreach (var fields in new[] { Tuple.Create("other", "\"wait\":1,\"social\":0"),
                Tuple.Create("wait", "\"wait\":0.2,\"social\":0.8"), Tuple.Create("wait", "\"wait\":1") })
            {
                var result = Evaluate(new LocalDecisionHttpResponse(200, Body(fields.Item1, fields.Item2), NpcDecisionFailureKind.None));
                Check.Equal(NpcDecisionStatus.Invalid, result.Status);
                Check.Equal(null, result.SelectedOptionId);
                Check.Equal(null, result.Probabilities);
            }
        }

        private static string Body(string choice, string probabilities)
            => "{\"model\":\"boundary-model\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"" + choice +
                "\",\"probabilities\":{" + probabilities + "}}}}";

        private static NpcDecisionRequest MakeRequest()
        {
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, "fixture:boundary", null,
                "session", 1, 1, new string('a', 32), new string('b', 64), "inn", 1);
            var unknown = ConversationContextFact<string>.Unknown();
            var snapshot = new ConversationContextSnapshot(binding, 100, unknown, unknown,
                ConversationContextFact<ConversationDialogueRole>.Unknown(), unknown, unknown,
                ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), unknown, 0, 1,
                null, new List<ConversationContextCommitment>(), new List<ConversationContextSourceHandle>());
            return new NpcDecisionRequest(Guid.NewGuid(), snapshot, 1, "route", "Please wait.", "Fixture.",
                new[] { new NpcDecisionOption("wait", "Wait", true), new NpcDecisionOption("social", "Conversation", false) });
        }

        private static LocalDecisionFreshness Current(NpcDecisionRequest request)
            => new LocalDecisionFreshness(request.Binding, request.SnapshotRevision, 1, request.Options);

        private static NpcDecisionResult Evaluate(LocalDecisionHttpResponse response)
        {
            var request = MakeRequest();
            var transport = new FixedTransport(response);
            NpcDecisionResult delivered = null;
            using (var session = new LocalDecisionSession(transport))
            {
                session.ExpectedModelId = "boundary-model";
                session.Enabled = true;
                Check.True(session.TryRequest(new LocalDialogueConnection("http://127.0.0.1:8093", 1000), request,
                    () => Current(request), result => delivered = result));
                var wait = Stopwatch.StartNew();
                while (delivered == null && wait.ElapsedMilliseconds < 3000) { session.Pump(); Thread.Sleep(1); }
                Check.True(delivered != null, "Adapter must deliver one typed result.");
                session.Pump();
                Check.Equal(1, transport.Count);
            }
            return delivered;
        }

        private static void PumpBudget()
        {
            var transport = new FixedTransport(new LocalDecisionHttpResponse(200,
                Body("wait", "\"wait\":1,\"social\":0"), NpcDecisionFailureKind.None));
            using (var session = new LocalDecisionSession(transport))
            {
                session.ExpectedModelId = "boundary-model";
                session.Enabled = true;
                var connection = new LocalDialogueConnection("http://127.0.0.1:8093", 1000);
                var first = MakeRequest();
                int callbacks = 0;
                bool followUpQueued = false;
                bool followUpReady = false;
                Check.True(session.TryRequest(connection, first, () => Current(first), result =>
                {
                    callbacks++;
                    var second = MakeRequest();
                    followUpQueued = session.TryRequest(connection, second, () => Current(second), next => callbacks++);
                    followUpReady = WaitForQueue(session);
                }));
                Check.True(WaitForQueue(session));
                session.Pump();
                Check.True(followUpQueued && followUpReady);
                Check.Equal(1, callbacks);
                session.Pump();
                Check.Equal(2, callbacks);
            }
        }

        private static bool WaitForQueue(LocalDecisionSession session)
        {
            var watch = Stopwatch.StartNew();
            while (session.PendingCompletionCount == 0 && watch.ElapsedMilliseconds < 3000) Thread.Sleep(1);
            return session.PendingCompletionCount != 0;
        }

        private sealed class FixedTransport : ILocalDecisionTransport
        {
            private readonly LocalDecisionHttpResponse response;
            public int Count;
            public FixedTransport(LocalDecisionHttpResponse response) { this.response = response; }
            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds, CancellationToken token)
            { Interlocked.Increment(ref Count); return response; }
        }
    }
}
