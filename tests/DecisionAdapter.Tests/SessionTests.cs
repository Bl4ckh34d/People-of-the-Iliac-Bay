using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class SessionTests
    {
        private const string ModelId = "session-test-model";

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("session is disabled by default and submits exact owned payload when enabled", DisabledAndEnabled));
            cases.Add(("empty expected model ID cannot enable or queue a request", EmptyModelId));
            cases.Add(("response model ID must match configured model exactly", ModelMismatch));
            cases.Add(("changing expected model invalidates active request", ModelChangeCancels));
            cases.Add(("transport and response failures stay typed and never select", FailureResultsAreTyped));
            cases.Add(("freshness owns defensive copy of current options", FreshnessCopiesOptions));
            cases.Add(("capture cancellation suppresses callback before delivery", CaptureCanCancel));
            cases.Add(("callback exceptions are counted and do not stop reentrant delivery", CallbackFailureDoesNotStrand));
            cases.Add(("all queued failures become stale when save or scene binding changes", FailedResponsesStillCheckFreshness));
            cases.Add(("missing or throwing freshness becomes stale and uses trusted model metadata", MissingFreshnessIsStale));
            cases.Add(("cancelling a queued completion clears bounded result storage", CancelClearsQueuedCompletion));
            cases.Add(("session freshness is captured on Pump and stale reply cannot select", StaleAtPump));
            cases.Add(("cancelled request cannot suppress or replace newer generation", CancelDoesNotAffectNewer));
            cases.Add(("cancel only matching request and disable cancels active work", CancelMatchingAndDisable));
            cases.Add(("Pump enforces constructor thread and callback can reenter", PumpThreadAndReentrancy));
            cases.Add(("loopback transport posts once to fixed systemone endpoint", LoopbackPost));
        }

        private static void DisabledAndEnabled()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
            using (var session = new LocalDecisionSession(transport))
            {
                int callbacks = 0;
                var request = Request();
                Check.True(!session.Enabled, "A new session must be disabled.");
                Check.True(!session.TryRequest(Connection(), request, Current, result => callbacks++));
                Check.Equal(0, transport.Count);
                Check.Equal(0, callbacks);

                session.ExpectedModelId = ModelId;
                session.Enabled = true;
                Check.True(session.TryRequest(Connection(), request, Current, result =>
                {
                    Check.Equal(NpcDecisionStatus.Ok, result.Status);
                    Check.Equal("wait_here", result.SelectedOptionId);
                    Check.Equal(LocalDecisionWire.Build(request).Sha256, result.RequestSha256);
                    callbacks++;
                }));
                PumpUntil(session, () => callbacks == 1);
                Check.Equal(1, transport.Count);
                Check.Equal(1, callbacks);
                Check.Equal("/v1/systemone", transport.LastEndpoint.AbsolutePath);
                Check.Equal(2000, transport.LastTimeout);
                Check.True(transport.LastBytes.SequenceEqual(LocalDecisionWire.Build(request).Bytes),
                    "Transport must receive exact bytes owned by this request.");
            }
        }

        private static void EmptyModelId()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
            using (var session = new LocalDecisionSession(transport))
            {
                int callbacks = 0;
                session.Enabled = true;
                Check.True(!session.Enabled, "An unconfigured model must keep session disabled.");
                Check.True(!session.TryRequest(Connection(), Request(), Current, result => callbacks++));
                Check.Equal(0, transport.Count);
                Check.Equal(0, callbacks);
                session.ExpectedModelId = "  ";
                session.Enabled = true;
                Check.True(!session.Enabled);
            }
        }

        private static void ModelMismatch()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here", "other-model"));
            using (var session = Enabled(transport))
            {
                NpcDecisionResult delivered = null;
                Check.True(session.TryRequest(Connection(), Request(), Current, result => delivered = result));
                PumpUntil(session, () => delivered != null);
                Check.Equal(NpcDecisionStatus.Invalid, delivered.Status);
                Check.Equal(NpcDecisionFailureKind.InvalidSchema, delivered.FailureKind);
                Check.Equal("model_mismatch", delivered.DiagnosticCode);
                Check.Equal(ModelId, delivered.ModelId);
                Check.Equal(null, delivered.SelectedOptionId);
            }
        }

        private static void ModelChangeCancels()
        {
            var transport = new QueuedTransport();
            using (var session = Enabled(transport))
            {
                int callbacks = 0;
                Check.True(session.TryRequest(Connection(), Request(), Current, result => callbacks++));
                Work work = transport.Take();
                session.ExpectedModelId = "new-model";
                work.Release.Set();
                WaitUntil(() => transport.Returned == 1);
                session.Pump();
                Check.True(callbacks == 0, "A model configuration change must invalidate old in-flight result.");
            }
        }

        private static void FailureResultsAreTyped()
        {
            var failures = new[]
            {
                Tuple.Create(new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Timeout, "timeout"),
                    NpcDecisionStatus.Unavailable, NpcDecisionFailureKind.Timeout),
                Tuple.Create(new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "io_failure"),
                    NpcDecisionStatus.Unavailable, NpcDecisionFailureKind.Transport),
                Tuple.Create(new LocalDecisionHttpResponse(503, null, NpcDecisionFailureKind.None, "http_status"),
                    NpcDecisionStatus.Unavailable, NpcDecisionFailureKind.HttpStatus),
                Tuple.Create(new LocalDecisionHttpResponse(200, "{", NpcDecisionFailureKind.None),
                    NpcDecisionStatus.Invalid, NpcDecisionFailureKind.MalformedJson),
            };
            foreach (var sample in failures)
            {
                var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => sample.Item1);
                using (var session = Enabled(transport))
                {
                    NpcDecisionResult delivered = null;
                    Check.True(session.TryRequest(Connection(), Request(), Current, result => delivered = result));
                    PumpUntil(session, () => delivered != null);
                    Check.Equal(sample.Item2, delivered.Status);
                    Check.Equal(sample.Item3, delivered.FailureKind);
                    Check.Equal(null, delivered.SelectedOptionId);
                    Check.True(delivered.Probabilities == null || delivered.Probabilities.Count == 0);
                }
            }
        }

        private static void FreshnessCopiesOptions()
        {
            var source = new List<NpcDecisionOption>
            {
                new NpcDecisionOption("wait_here", "Wait here", true),
                new NpcDecisionOption("clarify", "Ask for clarification", false),
            };
            var freshness = new LocalDecisionFreshness(Binding(), 0, 11, source);
            source.Clear();
            source.Add(new NpcDecisionOption("other", "Different option", true));
            Check.Equal(2, freshness.Options.Count);
            Check.Equal("wait_here", freshness.Options[0].Id);
            Check.Equal("clarify", freshness.Options[1].Id);
        }

        private static void CaptureCanCancel()
        {
            var transport = new QueuedTransport();
            using (var session = Enabled(transport))
            {
                int callbacks = 0;
                bool cancelledDuringCapture = false;
                NpcDecisionRequest request = Request();
                Check.True(session.TryRequest(Connection(), request, () =>
                {
                    cancelledDuringCapture = session.Cancel(request.RequestId);
                    return Freshness();
                }, result => callbacks++));
                Work work = transport.Take();
                work.Release.Set();
                WaitUntil(() => transport.Returned == 1 && session.PendingCompletionCount == 1);
                session.Pump();
                Check.True(cancelledDuringCapture, "Capture callback can cancel active request.");
                Check.Equal(0, callbacks);
            }
        }

        private static void CallbackFailureDoesNotStrand()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
            using (var session = Enabled(transport))
            {
                int callbacks = 0;
                Check.True(session.TryRequest(Connection(), Request(), Current, result =>
                {
                    Check.True(session.TryRequest(Connection(), Request("ffffffff-ffff-ffff-ffff-ffffffffffff"), Current,
                        nested => callbacks++));
                    throw new InvalidOperationException("synthetic callback fault");
                }));
                PumpUntil(session, () => callbacks == 1);
                Check.Equal(1, session.CallbackFailureCount);
                Check.Equal(1, callbacks);
            }
        }

        private static void FailedResponsesStillCheckFreshness()
        {
            var failures = new[]
            {
                Tuple.Create(new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Timeout, "timeout"), ModelId),
                Tuple.Create(new LocalDecisionHttpResponse(503, null, NpcDecisionFailureKind.None, "http_status"), ModelId),
                Tuple.Create(new LocalDecisionHttpResponse(200, "{", NpcDecisionFailureKind.None), ModelId),
                Tuple.Create(Success("wait_here", "untrusted-model"), "untrusted-model"),
            };
            foreach (var failure in failures)
            foreach (bool changeScene in new[] { false, true })
            {
                var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => failure.Item1);
                using (var session = Enabled(transport))
                {
                    NpcDecisionResult delivered = null;
                    Check.True(session.TryRequest(Connection(), Request(), () => changeScene
                        ? Freshness(sceneRevision: 6)
                        : Freshness(saveEpoch: 8), result => delivered = result));
                    WaitUntil(() => session.PendingCompletionCount == 1);
                    session.Pump();
                    Check.True(delivered != null);
                    Check.Equal(NpcDecisionStatus.Stale, delivered.Status);
                    Check.Equal("decision_binding_changed", delivered.DiagnosticCode);
                    Check.Equal(null, delivered.SelectedOptionId);
                    Check.Equal(ModelId, delivered.ModelId);
                }
            }
        }

        private static void CancelClearsQueuedCompletion()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
            using (var session = Enabled(transport))
            {
                for (int i = 0; i < 12; i++)
                {
                    string hex = (i + 1).ToString("x32");
                    NpcDecisionRequest request = Request(hex);
                    Check.True(session.TryRequest(Connection(), request, Current, result => { }));
                    WaitUntil(() => session.PendingCompletionCount == 1);
                    Check.True(!session.Cancel(Guid.NewGuid()), "Mismatched cancellation must not clear current result.");
                    Check.Equal(1, session.PendingCompletionCount);
                    Check.True(session.Cancel(request.RequestId));
                    Check.Equal(0, session.PendingCompletionCount);
                }
                Check.Equal(12, transport.Count);
            }
        }

        private static void MissingFreshnessIsStale()
        {
            foreach (bool throwFromCapture in new[] { false, true })
            {
                var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
                using (var session = Enabled(transport))
                {
                    NpcDecisionResult delivered = null;
                    Func<LocalDecisionFreshness> current = throwFromCapture
                        ? (Func<LocalDecisionFreshness>)(() => { throw new InvalidOperationException("stale source"); })
                        : () => null;
                    Check.True(session.TryRequest(Connection(), Request(), current, result => delivered = result));
                    PumpUntil(session, () => delivered != null);
                    Check.Equal(NpcDecisionStatus.Stale, delivered.Status);
                    Check.Equal(ModelId, delivered.ModelId);
                    Check.Equal(null, delivered.SelectedOptionId);
                }
            }
        }

        private static void StaleAtPump()
        {
            var transport = new QueuedTransport();
            using (var session = Enabled(transport))
            {
                int callbacks = 0;
                int captures = 0;
                int captureThread = -1;
                NpcDecisionResult delivered = null;
                Check.True(session.TryRequest(Connection(), Request(), () =>
                {
                    captures++;
                    captureThread = Thread.CurrentThread.ManagedThreadId;
                    return Freshness(snapshotRevision: 2);
                }, result => { delivered = result; callbacks++; }));
                Work work = transport.Take();
                work.Release.Set();
                WaitUntil(() => transport.Returned == 1 && session.PendingCompletionCount == 1);
                Check.Equal(0, captures);
                Check.Equal(0, callbacks);
                session.Pump();
                Check.Equal(1, captures);
                Check.Equal(Thread.CurrentThread.ManagedThreadId, captureThread);
                Check.Equal(1, callbacks);
                Check.Equal(NpcDecisionStatus.Stale, delivered.Status);
                Check.Equal(null, delivered.SelectedOptionId);
            }
        }

        private static void CancelDoesNotAffectNewer()
        {
            var transport = new QueuedTransport();
            using (var session = Enabled(transport))
            {
                int oldCallbacks = 0;
                int newCallbacks = 0;
                NpcDecisionRequest oldRequest = Request("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
                NpcDecisionRequest newRequest = Request("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
                Check.True(session.TryRequest(Connection(), oldRequest, Current, result => oldCallbacks++));
                Work oldWork = transport.Take();
                Check.True(!session.TryRequest(Connection(), newRequest, Current, result => newCallbacks++),
                    "Session permits one active decision request at a time.");
                session.Cancel(oldRequest.RequestId);
                Check.True(session.TryRequest(Connection(), newRequest, Current, result =>
                {
                    Check.Equal(NpcDecisionStatus.Ok, result.Status);
                    Check.Equal(newRequest.RequestId, result.RequestId);
                    newCallbacks++;
                }), "Cancel must release old client slot before the older worker exits.");
                Work newWork = transport.Take();
                newWork.Release.Set();
                WaitUntil(() => transport.Returned == 1 && session.PendingCompletionCount == 1);
                session.Pump();
                Check.Equal(1, newCallbacks);
                oldWork.Release.Set();
                WaitUntil(() => transport.Returned == 2);
                session.Pump();
                Check.Equal(0, oldCallbacks);
                Check.Equal(1, newCallbacks);
            }
        }

        private static void CancelMatchingAndDisable()
        {
            var transport = new QueuedTransport();
            using (var session = Enabled(transport))
            {
                int callbacks = 0;
                NpcDecisionRequest request = Request();
                Check.True(session.TryRequest(Connection(), request, Current, result => callbacks++));
                Work work = transport.Take();
                session.Cancel(Guid.NewGuid());
                work.Release.Set();
                WaitUntil(() => transport.Returned == 1 && session.PendingCompletionCount == 1);
                session.Pump();
                Check.True(callbacks == 1, "A nonmatching Cancel must leave request active.");

                NpcDecisionRequest next = Request("cccccccc-cccc-cccc-cccc-cccccccccccc");
                Check.True(session.TryRequest(Connection(), next, Current, result => callbacks++));
                Work nextWork = transport.Take();
                session.Enabled = false;
                Check.True(!session.Enabled);
                nextWork.Release.Set();
                WaitUntil(() => transport.Returned == 2);
                session.Pump();
                Check.True(callbacks == 1, "Disabling must cancel and suppress active callback.");
                Check.True(!session.TryRequest(Connection(), Request("dddddddd-dddd-dddd-dddd-dddddddddddd"), Current,
                    result => callbacks++));
            }
        }

        private static void PumpThreadAndReentrancy()
        {
            var transport = new ImmediateTransport((endpoint, bytes, timeout, token) => Success("wait_here"));
            using (var session = Enabled(transport))
            {
                Exception wrongThread = Task.Run(() =>
                {
                    try { session.Pump(); return null; }
                    catch (Exception error) { return error; }
                }).Result;
                Check.True(wrongThread is InvalidOperationException, "Pump must reject a non-constructor thread.");

                int callbacks = 0;
                NpcDecisionRequest first = Request();
                Check.True(session.TryRequest(Connection(), first, Current, result =>
                {
                    callbacks++;
                    Check.True(session.TryRequest(Connection(), Request("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), Current,
                        nested => callbacks++), "Callback must be able to reenter session after slot release.");
                }));
                PumpUntil(session, () => callbacks == 2);
                Check.Equal(2, callbacks);
            }
        }

        private static void LoopbackPost()
        {
            int port;
            using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            using (var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port))
            {
                listener.Start();
                var server = Task.Run(() =>
                {
                    using (var client = listener.AcceptTcpClient())
                    using (var stream = client.GetStream())
                    {
                        var bytes = new List<byte>();
                        var buffer = new byte[1024];
                        int headerEnd = -1;
                        while (headerEnd < 0)
                        {
                            int read = stream.Read(buffer, 0, buffer.Length);
                            if (read <= 0) throw new InvalidOperationException("Client closed before HTTP headers.");
                            bytes.AddRange(buffer.Take(read));
                            headerEnd = FindHeaderEnd(bytes);
                        }
                        string headers = Encoding.ASCII.GetString(bytes.Take(headerEnd).ToArray());
                        int contentLength = HeaderInt(headers, "Content-Length");
                        int bodyStart = headerEnd + 4;
                        while (bytes.Count - bodyStart < contentLength)
                        {
                            int read = stream.Read(buffer, 0, buffer.Length);
                            if (read <= 0) throw new InvalidOperationException("Client closed before request body.");
                            bytes.AddRange(buffer.Take(read));
                        }
                        string requestLine = headers.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
                        string body = Encoding.UTF8.GetString(bytes.Skip(bodyStart).Take(contentLength).ToArray());
                        string responseBody = "{\"model\":\"loopback-model\",\"answers\":{" +
                            "\"decision\":{\"type\":\"choice\",\"choice\":\"wait_here\",\"probabilities\":{" +
                            "\"wait_here\":1}}}}";
                        byte[] responseBytes = Encoding.UTF8.GetBytes(responseBody);
                        byte[] responseHeaders = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " +
                            responseBytes.Length + "\r\nConnection: close\r\n\r\n");
                        stream.Write(responseHeaders, 0, responseHeaders.Length);
                        stream.Write(responseBytes, 0, responseBytes.Length);
                        return Tuple.Create(requestLine, body);
                    }
                });
                var transport = new LocalDecisionTransport();
                byte[] payload = LocalDecisionWire.Build(Request()).Bytes;
                LocalDecisionHttpResponse response = transport.Send(new Uri("http://127.0.0.1:" + port + "/v1/systemone"),
                    payload, 3000, CancellationToken.None);
                Tuple<string, string> received = server.Result;
                Check.Equal("POST /v1/systemone HTTP/1.1", received.Item1);
                Check.True(Encoding.UTF8.GetString(payload) == received.Item2, "Loopback server must receive exact payload bytes.");
                Check.Equal(200, response.StatusCode);
                Check.Equal(NpcDecisionFailureKind.None, response.FailureKind);
                Check.True(response.Body.Contains("loopback-model"));
                listener.Stop();
            }
        }

        private static int FindHeaderEnd(List<byte> bytes)
        {
            for (int i = 0; i <= bytes.Count - 4; i++)
                if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10) return i;
            return -1;
        }

        private static int HeaderInt(string headers, string name)
        {
            foreach (string line in headers.Split(new[] { "\r\n" }, StringSplitOptions.None))
                if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                    return int.Parse(line.Substring(line.IndexOf(':') + 1).Trim());
            throw new InvalidOperationException("Missing HTTP header " + name);
        }

        private static LocalDecisionSession Enabled(ILocalDecisionTransport transport)
        {
            var session = new LocalDecisionSession(transport);
            session.ExpectedModelId = ModelId;
            session.Enabled = true;
            return session;
        }

        private static LocalDialogueConnection Connection() => new LocalDialogueConnection("http://127.0.0.1:8093/ignored", 2000);

        private static NpcDecisionRequest Request(string requestId = "01234567-89ab-cdef-0123-456789abcdef")
        {
            var binding = Binding();
            var empty = ConversationContextFact<string>.Unknown();
            var snapshot = new ConversationContextSnapshot(binding, 100, empty, empty,
                ConversationContextFact<ConversationDialogueRole>.Unknown(), empty, empty,
                ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), empty, 0, 17,
                null, new List<ConversationContextCommitment>(), new List<ConversationContextSourceHandle>());
            return new NpcDecisionRequest(Guid.Parse(requestId), snapshot, 11, "test-purpose", "Please wait here.",
                "Current safe context.", new[]
                {
                    new NpcDecisionOption("wait_here", "Wait here", true),
                    new NpcDecisionOption("clarify", "Ask for clarification", false)
                });
        }

        private static ConversationContextBinding Binding(string sessionId = "session-a", long saveEpoch = 7,
            long sceneRevision = 5)
            => new ConversationContextBinding(ConversationActorCategory.Mobile, "resident:42", null,
                sessionId, 3, saveEpoch, new string('a', 32), new string('b', 64), "scene-a", sceneRevision);

        private static LocalDecisionFreshness Freshness(long snapshotRevision = 0, string sessionId = "session-a",
            long saveEpoch = 7, long sceneRevision = 5)
            => new LocalDecisionFreshness(Binding(sessionId, saveEpoch, sceneRevision), snapshotRevision, 11, Request().Options);

        private static Func<LocalDecisionFreshness> Current => () => Freshness();

        private static LocalDecisionHttpResponse Success(string selected, string model = null)
        {
            string other = selected == "wait_here" ? "clarify" : "wait_here";
            string json = "{\"model\":\"" + (model ?? ModelId) + "\",\"answers\":{\"decision\":{" +
                "\"type\":\"choice\",\"choice\":\"" + selected + "\",\"probabilities\":{" +
                "\"" + selected + "\":0.8,\"" + other + "\":0.2}}}}";
            return new LocalDecisionHttpResponse(200, json, NpcDecisionFailureKind.None);
        }

        private static void PumpUntil(LocalDecisionSession session, Func<bool> done)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (!done() && DateTime.UtcNow < deadline)
            {
                session.Pump();
                Thread.Sleep(1);
            }
            session.Pump();
            Check.True(done(), "Session did not deliver result before timeout.");
        }

        private static void WaitUntil(Func<bool> done)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (!done() && DateTime.UtcNow < deadline) Thread.Sleep(1);
            Check.True(done(), "Worker did not finish before timeout.");
        }

        private sealed class ImmediateTransport : ILocalDecisionTransport
        {
            private readonly Func<Uri, byte[], int, CancellationToken, LocalDecisionHttpResponse> send;
            private int count;
            public ImmediateTransport(Func<Uri, byte[], int, CancellationToken, LocalDecisionHttpResponse> send) { this.send = send; }
            public int Count => Volatile.Read(ref count);
            public Uri LastEndpoint { get; private set; }
            public int LastTimeout { get; private set; }
            public byte[] LastBytes { get; private set; }
            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref count);
                LastEndpoint = endpoint;
                LastTimeout = timeoutMilliseconds;
                LastBytes = (byte[])bytes.Clone();
                return send(endpoint, bytes, timeoutMilliseconds, cancellationToken);
            }
        }

        private sealed class Work
        {
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
        }

        private sealed class QueuedTransport : ILocalDecisionTransport
        {
            private readonly ConcurrentQueue<Work> entered = new ConcurrentQueue<Work>();
            private int returned;
            public int Returned => Volatile.Read(ref returned);
            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                var work = new Work();
                entered.Enqueue(work);
                work.Release.Wait(TimeSpan.FromSeconds(4));
                Interlocked.Increment(ref returned);
                return Success("wait_here");
            }
            public Work Take()
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                Work work;
                while (DateTime.UtcNow < deadline)
                {
                    if (entered.TryDequeue(out work)) return work;
                    Thread.Sleep(1);
                }
                throw new InvalidOperationException("Transport worker did not enter Send.");
            }
        }
    }
}
