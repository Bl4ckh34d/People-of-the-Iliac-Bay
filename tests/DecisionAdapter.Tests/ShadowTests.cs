using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class ShadowTests
    {
        private const string ModelId = "shadow-test-model";

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("shadow queues while feature disabled and sends only after caller enables session", FeatureOff));
            cases.Add(("ten observations coalesce to only latest one POST", CoalescesLatest));
            cases.Add(("same logical decision deduplicates across different request IDs", DeduplicatesLogicalRequest));
            cases.Add(("turn context options and native revisions remain distinct logical decisions", LogicalChangesAreDistinct));
            cases.Add(("new pending shadow cancels older active request and records only latest", ReplacesOlderActive));
            cases.Add(("shadow failures and stale results remain typed metadata without action", FailureAndStaleMetadata));
            cases.Add(("shadow CancelAll does not cancel a request it does not own", CancelDoesNotAffectUnownedSessionRequest));
            cases.Add(("session configuration changes permit logically identical observations again", ConfigurationChangeResetsDedup));
            cases.Add(("cancel clears pending active and dedup state", CancelAllClearsState));
            cases.Add(("dispose suppresses later callback and rejects new observation", DisposeSuppressesCallbacks));
            cases.Add(("record callback can reenter and callback exception is isolated", ObserverCanReenter));
        }

        private static void FeatureOff()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
            LocalDecisionSession session = Session(transport, enabled: false);
            var records = new List<LocalDecisionShadowObservation>();
            using (var shadow = new LocalDecisionShadow(session, Connection(), records.Add))
            {
                Check.True(shadow.Observe(Request("00000000-0000-0000-0000-000000000001"), Current));
                shadow.Pump();
                Check.Equal(0, transport.Count);
                Check.Equal(0L, shadow.SubmittedCount);
                Check.Equal(0, records.Count);
                session.Enabled = true;
                PumpUntil(shadow, () => records.Count == 1);
                Check.Equal(1, transport.Count);
                Check.Equal(1L, shadow.SubmittedCount);
                Check.Equal(1L, shadow.CompletedCount);
            }
            session.Dispose();
        }

        private static void CoalescesLatest()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
            using (var shadow = NewShadow(transport, out var session, out var records))
            {
                string latest = null;
                for (int i = 0; i < 10; i++)
                {
                    latest = Guid.NewGuid().ToString();
                    Check.True(shadow.Observe(Request(latest, turn: "turn-" + i), Current));
                }
                Check.Equal(0, transport.Count);
                Check.Equal(9L, shadow.CoalescedCount);
                PumpUntil(shadow, () => records.Count == 1);
                Check.Equal(1, transport.Count);
                Check.Equal(Guid.Parse(latest), records[0].RequestId);
                Check.Equal(1L, shadow.SubmittedCount);
                Check.Equal(1L, shadow.CompletedCount);
                Check.Equal(true, records[0].IsShadow);
                Check.Equal(false, records[0].NativeDispatchAttempted);
            }
        }

        private static void DeduplicatesLogicalRequest()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
            using (var shadow = NewShadow(transport, out var session, out var records))
            {
                Check.True(shadow.Observe(Request("00000000-0000-0000-0000-000000000010"), Current));
                Check.True(!shadow.Observe(Request("00000000-0000-0000-0000-000000000011"), Current),
                    "Request ID alone must not defeat logical deduplication.");
                Check.Equal(0L, shadow.CoalescedCount);
                PumpUntil(shadow, () => records.Count == 1);
                Check.Equal(1, transport.Count);
                Check.Equal(1L, shadow.SubmittedCount);
            }
        }

        private static void LogicalChangesAreDistinct()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
            using (var shadow = NewShadow(transport, out var session, out var records))
            {
                Check.True(shadow.Observe(Request("00000000-0000-0000-0000-000000000020"), Current));
                var changed = new[]
                {
                    Request("00000000-0000-0000-0000-000000000021", turn: "different turn"),
                    Request("00000000-0000-0000-0000-000000000022", context: "different context"),
                    Request("00000000-0000-0000-0000-000000000023", purpose: "different purpose"),
                    Request("00000000-0000-0000-0000-000000000024", instructions: "different instructions"),
                    Request("00000000-0000-0000-0000-000000000025", options: Options("Wait here, quietly", true)),
                    Request("00000000-0000-0000-0000-000000000026", options: Options("Wait here", false)),
                    Request("00000000-0000-0000-0000-000000000027", binding: Binding(requestRevision: 4)),
                    Request("00000000-0000-0000-0000-000000000028", binding: Binding(sessionId: "session-b")),
                    Request("00000000-0000-0000-0000-000000000029", snapshotRevision: 1),
                    Request("00000000-0000-0000-0000-00000000002a", candidateRevision: 12),
                };
                foreach (var request in changed)
                    Check.True(shadow.Observe(request, Current), "Changed logical content must not deduplicate.");
                Check.Equal(10L, shadow.CoalescedCount);
                Check.Equal(0, transport.Count);
                PumpUntil(shadow, () => records.Count == 1);
                Check.Equal(1, transport.Count);
                Check.Equal(changed[changed.Length - 1].RequestId, records[0].RequestId);
            }
        }

        private static void ReplacesOlderActive()
        {
            var transport = new BlockingTransport();
            using (var shadow = NewShadow(transport, out var session, out var records))
            {
                NpcDecisionRequest older = Request("00000000-0000-0000-0000-000000000030", turn: "older");
                NpcDecisionRequest latest = Request("00000000-0000-0000-0000-000000000031", turn: "latest");
                Check.True(shadow.Observe(older, Current));
                shadow.Pump();
                Work oldWork = transport.Take();
                Check.Equal(1L, shadow.SubmittedCount);

                Check.True(shadow.Observe(latest, Current));
                shadow.Pump();
                Work newWork = transport.Take();
                Check.Equal(1L, shadow.CancelledCount);
                Check.Equal(2L, shadow.SubmittedCount);
                newWork.Release.Set();
                PumpUntil(shadow, () => records.Count == 1);
                Check.Equal(latest.RequestId, records[0].RequestId);
                oldWork.Release.Set();
                WaitUntil(() => transport.Returned == 2);
                shadow.Pump();
                Check.Equal(1, records.Count);
                Check.Equal(1L, shadow.CompletedCount);
            }
        }

        private static void FailureAndStaleMetadata()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) =>
                new LocalDecisionHttpResponse(503, null, NpcDecisionFailureKind.None, "http_status"));
            using (var shadow = NewShadow(transport, out var session, out var records))
            {
                NpcDecisionRequest request = Request("00000000-0000-0000-0000-000000000040");
                Check.True(shadow.Observe(request, () => Freshness(request, saveEpoch: 8)));
                PumpUntil(shadow, () => records.Count == 1);
                LocalDecisionShadowObservation item = records[0];
                Check.Equal(NpcDecisionStatus.Stale, item.Status);
                Check.Equal(NpcDecisionFailureKind.None, item.FailureKind);
                Check.Equal(null, item.SelectedOptionId);
                Check.Equal(request.RequestId, item.RequestId);
                Check.Equal(true, item.IsShadow);
                Check.Equal(false, item.NativeDispatchAttempted);
                Check.True(item.RequestSha256 != null && item.RequestSha256.Length == 64);
                Check.Equal(ModelId, item.ModelId);
                Check.Equal(1L, shadow.CompletedCount);
                Check.Equal(null, item.GetType().GetProperty("OriginalTurn"));
                Check.Equal(null, item.GetType().GetProperty("Context"));
            }
        }

        private static void CancelAllClearsState()
        {
            var transport = new BlockingTransport();
            using (var shadow = NewShadow(transport, out var session, out var records))
            {
                NpcDecisionRequest active = Request("00000000-0000-0000-0000-000000000050", turn: "active");
                NpcDecisionRequest pending = Request("00000000-0000-0000-0000-000000000051", turn: "pending");
                Check.True(shadow.Observe(active, Current));
                shadow.Pump();
                Work work = transport.Take();
                Check.True(shadow.Observe(pending, Current));
                shadow.CancelAll();
                Check.Equal(2L, shadow.CancelledCount);
                Check.Equal(0L, shadow.CoalescedCount);
                Check.Equal(1L, shadow.SubmittedCount);
                work.Release.Set();
                WaitUntil(() => transport.Returned == 1);
                shadow.Pump();
                Check.Equal(0, records.Count);
                Check.True(shadow.Observe(Request("00000000-0000-0000-0000-000000000052"), Current),
                    "CancelAll clears dedup state and keeps coordinator reusable.");
                Check.Equal(1L, shadow.SubmittedCount);
            }
        }

        private static void CancelDoesNotAffectUnownedSessionRequest()
        {
            var transport = new BlockingTransport();
            LocalDecisionSession session = Session(transport, enabled: true);
            int externalCallbacks = 0;
            NpcDecisionRequest external = Request("00000000-0000-0000-0000-000000000053", turn: "external");
            Check.True(session.TryRequest(Connection(), external, () => Freshness(external), _ => externalCallbacks++));
            Work work = transport.Take();
            using (var shadow = new LocalDecisionShadow(session, Connection(), _ => { }))
                shadow.CancelAll();
            work.Release.Set();
            WaitUntil(() => transport.Returned == 1);
            DateTime deadline = DateTime.UtcNow.AddSeconds(4);
            while (externalCallbacks == 0 && DateTime.UtcNow < deadline)
            {
                session.Pump();
                Thread.Sleep(1);
            }
            session.Pump();
            Check.True(externalCallbacks == 1, "Cancelling an idle shadow must leave another session owner's request active.");
            session.Dispose();
        }

        private static void ConfigurationChangeResetsDedup()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
            using (var shadow = NewShadow(transport, out var session, out var records))
            {
                Check.True(shadow.Observe(Request("00000000-0000-0000-0000-000000000054"), Current));
                PumpUntil(shadow, () => records.Count == 1);
                session.Enabled = false;
                session.Enabled = true;
                Check.True(shadow.Observe(Request("00000000-0000-0000-0000-000000000055"), Current),
                    "Re-enabling must permit a fresh observation even when request content is unchanged.");
                PumpUntil(shadow, () => records.Count == 2);

                session.ExpectedModelId = "shadow-test-model-v2";
                session.ExpectedModelId = ModelId;
                Check.True(shadow.Observe(Request("00000000-0000-0000-0000-000000000056"), Current),
                    "Changing configured model identity must invalidate the prior dedup key.");
                PumpUntil(shadow, () => records.Count == 3);
                Check.Equal(3, transport.Count);
            }
        }

        private static void DisposeSuppressesCallbacks()
        {
            var transport = new BlockingTransport();
            LocalDecisionSession session = Session(transport, enabled: true);
            var records = new List<LocalDecisionShadowObservation>();
            var shadow = new LocalDecisionShadow(session, Connection(), records.Add);
            NpcDecisionRequest request = Request("00000000-0000-0000-0000-000000000060");
            Check.True(shadow.Observe(request, Current));
            shadow.Pump();
            Work work = transport.Take();
            shadow.Dispose();
            work.Release.Set();
            WaitUntil(() => transport.Returned == 1);
            shadow.Pump();
            Check.Equal(0, records.Count);
            Check.True(!shadow.Observe(Request("00000000-0000-0000-0000-000000000061"), Current));
            session.Dispose();
        }

        private static void ObserverCanReenter()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
            LocalDecisionSession session = Session(transport, enabled: true);
            var seen = new List<LocalDecisionShadowObservation>();
            LocalDecisionShadow shadow = null;
            bool throwOnce = true;
            shadow = new LocalDecisionShadow(session, Connection(), item =>
            {
                seen.Add(item);
                if (seen.Count == 1)
                {
                    Check.True(shadow.Observe(Request("00000000-0000-0000-0000-000000000071", turn: "reentrant"), Current));
                    if (throwOnce) { throwOnce = false; throw new InvalidOperationException("observer probe"); }
                }
            });
            using (shadow)
            {
                Check.True(shadow.Observe(Request("00000000-0000-0000-0000-000000000070"), Current));
                PumpUntil(shadow, () => seen.Count == 2);
                Check.Equal(2L, shadow.SubmittedCount);
                Check.Equal(2L, shadow.CompletedCount);
                Check.Equal(1L, shadow.ObserverFailureCount);
            }
            session.Dispose();
        }

        private static LocalDecisionShadow NewShadow(ILocalDecisionTransport transport,
            out LocalDecisionSession session, out List<LocalDecisionShadowObservation> records)
        {
            session = Session(transport, enabled: true);
            records = new List<LocalDecisionShadowObservation>();
            return new LocalDecisionShadow(session, Connection(), records.Add);
        }

        private static LocalDecisionSession Session(ILocalDecisionTransport transport, bool enabled)
        {
            var session = new LocalDecisionSession(transport) { ExpectedModelId = ModelId };
            session.Enabled = enabled;
            return session;
        }

        private static LocalDialogueConnection Connection() => new LocalDialogueConnection("http://127.0.0.1:8093/ignored", 2000);

        private static NpcDecisionRequest Request(string id, string turn = "Please wait here.", string context = "Current context.",
            string purpose = "action-decision", string instructions = "Choose one supported option.",
            IEnumerable<NpcDecisionOption> options = null, ConversationContextBinding binding = null,
            long snapshotRevision = 0, long candidateRevision = 11)
        {
            binding = binding ?? Binding();
            var empty = ConversationContextFact<string>.Unknown();
            var snapshot = new ConversationContextSnapshot(binding, 100, empty, empty,
                ConversationContextFact<ConversationDialogueRole>.Unknown(), empty, empty,
                ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), empty, 0, 17,
                null, new List<ConversationContextCommitment>(), new List<ConversationContextSourceHandle>());
            if (snapshotRevision > 0) snapshot = snapshot.WithCommittedDisposition(18);
            return new NpcDecisionRequest(Guid.Parse(id), snapshot, candidateRevision, purpose, instructions, turn,
                context, options ?? Options());
        }

        private static IEnumerable<NpcDecisionOption> Options(string description = "Wait here", bool effectful = true)
            => new[] { new NpcDecisionOption("wait_here", description, effectful), new NpcDecisionOption("clarify", "Ask a question", false) };

        private static ConversationContextBinding Binding(string sessionId = "session-a", long requestRevision = 3,
            long saveEpoch = 7, long sceneRevision = 5)
            => new ConversationContextBinding(ConversationActorCategory.Mobile, "resident:42", null,
                sessionId, requestRevision, saveEpoch, new string('a', 32), new string('b', 64), "scene-a", sceneRevision);

        private static LocalDecisionFreshness Freshness(NpcDecisionRequest request, long saveEpoch = 7)
        {
            var binding = saveEpoch == request.Binding.SaveEpoch ? request.Binding : Binding(request.Binding.SessionId,
                request.Binding.RequestRevision, saveEpoch, request.Binding.SceneRevision);
            return new LocalDecisionFreshness(binding, request.SnapshotRevision, request.CandidateSetRevision, request.Options);
        }

        private static Func<LocalDecisionFreshness> Current => () => Freshness(Request("10000000-0000-0000-0000-000000000001"));

        private static LocalDecisionHttpResponse Success(string selected)
        {
            const string body = "{\"model\":\"shadow-test-model\",\"answers\":{" +
                "\"decision\":{\"type\":\"choice\",\"choice\":\"wait_here\",\"probabilities\":{" +
                "\"wait_here\":0.8,\"clarify\":0.2}}}}";
            return new LocalDecisionHttpResponse(200, body, NpcDecisionFailureKind.None);
        }

        private static void PumpUntil(LocalDecisionShadow shadow, Func<bool> done)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(4);
            while (!done() && DateTime.UtcNow < deadline)
            {
                shadow.Pump();
                Thread.Sleep(1);
            }
            shadow.Pump();
            Check.True(done(), "Shadow coordinator did not complete before timeout.");
        }

        private static void WaitUntil(Func<bool> done)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(4);
            while (!done() && DateTime.UtcNow < deadline) Thread.Sleep(1);
            Check.True(done(), "Shadow worker did not finish before timeout.");
        }

        private sealed class ImmediateTransport : ILocalDecisionTransport
        {
            private readonly Func<Uri, byte[], int, CancellationToken, LocalDecisionHttpResponse> send;
            private int count;
            public ImmediateTransport(Func<Uri, byte[], int, CancellationToken, LocalDecisionHttpResponse> send) { this.send = send; }
            public int Count => Volatile.Read(ref count);
            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref count);
                return send(endpoint, (byte[])bytes.Clone(), timeoutMilliseconds, cancellationToken);
            }
        }

        private sealed class Work
        {
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
        }

        private sealed class BlockingTransport : ILocalDecisionTransport
        {
            private readonly ConcurrentQueue<Work> entered = new ConcurrentQueue<Work>();
            private int returned;
            public int Returned => Volatile.Read(ref returned);
            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                var work = new Work();
                entered.Enqueue(work);
                work.Release.Wait(TimeSpan.FromSeconds(5));
                Interlocked.Increment(ref returned);
                return Success("wait_here");
            }
            public Work Take()
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(4);
                Work work;
                while (DateTime.UtcNow < deadline)
                {
                    if (entered.TryDequeue(out work)) return work;
                    Thread.Sleep(1);
                }
                throw new InvalidOperationException("Shadow worker did not enter transport.");
            }
        }
    }
}
