using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class LocalDecisionFreshness
    {
        private readonly ReadOnlyCollection<NpcDecisionOption> options;
        public ConversationContextBinding Binding { get; }
        public long SnapshotRevision { get; }
        public long CandidateSetRevision { get; }
        public IReadOnlyList<NpcDecisionOption> Options => options;

        public LocalDecisionFreshness(ConversationContextBinding binding, long snapshotRevision,
            long candidateSetRevision, IEnumerable<NpcDecisionOption> currentOptions)
        {
            if (binding == null || snapshotRevision < 0 || candidateSetRevision < 0 || currentOptions == null)
                throw new ArgumentException("Invalid current decision freshness.");
            var copy = new List<NpcDecisionOption>();
            foreach (var option in currentOptions)
            {
                if (option == null) throw new ArgumentException("Current decision options cannot contain null.");
                copy.Add(new NpcDecisionOption(option.Id, option.Description, option.IsEffectful));
                if (copy.Count > 32) throw new ArgumentException("Too many current decision options.");
            }
            if (copy.Count == 0) throw new ArgumentException("Current decision options are required.");
            Binding = binding;
            SnapshotRevision = snapshotRevision;
            CandidateSetRevision = candidateSetRevision;
            options = new ReadOnlyCollection<NpcDecisionOption>(copy);
        }
    }

    internal sealed class LocalDecisionSession : IDisposable
    {
        private sealed class ActiveRequest
        {
            public readonly LocalDialogueConnection Connection;
            public readonly NpcDecisionRequest Request;
            public readonly Func<LocalDecisionFreshness> CaptureCurrent;
            public readonly Action<NpcDecisionResult> Callback;
            public readonly Uri Endpoint;
            public readonly byte[] PayloadBytes;
            public readonly string PayloadHash;
            public readonly string ExpectedModelId;
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public bool Valid = true;

            public ActiveRequest(LocalDialogueConnection connection, NpcDecisionRequest request,
                Func<LocalDecisionFreshness> captureCurrent, Action<NpcDecisionResult> callback, Uri endpoint,
                byte[] payloadBytes, string payloadHash, string expectedModelId)
            {
                Connection = connection;
                Request = request;
                CaptureCurrent = captureCurrent;
                Callback = callback;
                Endpoint = endpoint;
                PayloadBytes = (byte[])payloadBytes.Clone();
                PayloadHash = payloadHash;
                ExpectedModelId = expectedModelId;
            }
        }

        private sealed class CompletedRequest
        {
            public readonly ActiveRequest Active;
            public readonly LocalDecisionHttpResponse Response;
            public readonly LocalDecisionParsedReply Parsed;
            public readonly double LatencyMilliseconds;

            public CompletedRequest(ActiveRequest active, LocalDecisionHttpResponse response,
                LocalDecisionParsedReply parsed, double latencyMilliseconds)
            {
                Active = active;
                Response = response;
                Parsed = parsed;
                LatencyMilliseconds = latencyMilliseconds;
            }
        }

        private readonly object gate = new object();
        private readonly Queue<CompletedRequest> completed = new Queue<CompletedRequest>();
        private readonly ILocalDecisionTransport transport;
        private readonly int mainThreadId;
        private bool enabled;
        private bool disposed;
        private string expectedModelId;
        private long configurationGeneration;
        private ActiveRequest active;
        private int callbackFailureCount;

        public LocalDecisionSession() : this(null) { }

        public LocalDecisionSession(ILocalDecisionTransport transport)
        {
            this.transport = transport ?? new LocalDecisionTransport();
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public string ExpectedModelId
        {
            get { lock (gate) return expectedModelId; }
            set
            {
                EnsureMainThread();
                string normalized = ValidModelId(value) ? value : null;
                List<ActiveRequest> cancel;
                lock (gate)
                {
                    if (disposed || string.Equals(expectedModelId, normalized, StringComparison.Ordinal)) return;
                    expectedModelId = normalized;
                    if (expectedModelId == null) enabled = false;
                    AdvanceConfigurationGenerationLocked();
                    cancel = InvalidateAllLocked();
                }
                CancelOutsideLock(cancel);
            }
        }

        public bool Enabled
        {
            get { lock (gate) return !disposed && enabled && ValidModelId(expectedModelId); }
            set
            {
                EnsureMainThread();
                List<ActiveRequest> cancel = null;
                lock (gate)
                {
                    if (disposed) return;
                    bool next = value && ValidModelId(expectedModelId);
                    if (enabled == next) return;
                    enabled = next;
                    AdvanceConfigurationGenerationLocked();
                    if (!enabled) cancel = InvalidateAllLocked();
                }
                if (cancel != null) CancelOutsideLock(cancel);
            }
        }

        internal long ConfigurationGeneration
        {
            get { lock (gate) return configurationGeneration; }
        }

        internal int CallbackFailureCount
        {
            get { lock (gate) return callbackFailureCount; }
        }

        internal int PendingCompletionCount
        {
            get { lock (gate) return completed.Count; }
        }

        public bool TryRequest(LocalDialogueConnection connection, NpcDecisionRequest request,
            Func<LocalDecisionFreshness> captureCurrent, Action<NpcDecisionResult> callback)
        {
            EnsureMainThread();
            if (connection == null || request == null || captureCurrent == null || callback == null) return false;
            lock (gate)
                if (disposed || !enabled || !ValidModelId(expectedModelId)) return false;
            LocalDecisionPayload payload;
            Uri endpoint;
            try
            {
                endpoint = SystemOneEndpoint(connection.Endpoint);
                payload = LocalDecisionWire.Build(request);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            if (endpoint == null) return false;

            ActiveRequest active;
            lock (gate)
            {
                if (disposed || !enabled || !ValidModelId(expectedModelId) || this.active != null)
                    return false;
                active = new ActiveRequest(connection, request, captureCurrent, callback, endpoint,
                    payload.Bytes, payload.Sha256, expectedModelId);
                this.active = active;
            }

            bool queued;
            try { queued = ThreadPool.QueueUserWorkItem(_ => RunWorker(active)); }
            catch (NotSupportedException) { queued = false; }
            catch (InvalidOperationException) { queued = false; }
            if (queued) return true;

            lock (gate) InvalidateLocked(active);
            CancelOne(active);
            return false;
        }

        public bool Cancel(Guid requestId)
        {
            EnsureMainThread();
            ActiveRequest active;
            lock (gate)
            {
                if (disposed || this.active == null || this.active.Request.RequestId != requestId) return false;
                active = this.active;
                InvalidateLocked(active);
            }
            CancelOne(active);
            return true;
        }

        public void CancelAll()
        {
            EnsureMainThread();
            List<ActiveRequest> cancel;
            lock (gate)
            {
                if (disposed) return;
                cancel = InvalidateAllLocked();
            }
            CancelOutsideLock(cancel);
        }

        public void Pump()
        {
            EnsureMainThread();
            while (true)
            {
                CompletedRequest result;
                lock (gate)
                {
                    if (disposed || completed.Count == 0) return;
                    result = completed.Dequeue();
                    if (!IsCurrentLocked(result.Active)) continue;
                }

                NpcDecisionResult decision = BuildResult(result);
                lock (gate)
                {
                    if (!IsCurrentLocked(result.Active)) continue;
                    InvalidateLocked(result.Active);
                }
                if (decision == null) continue;
                try { result.Active.Callback(decision); }
                catch (Exception)
                {
                    lock (gate)
                        if (callbackFailureCount < int.MaxValue) callbackFailureCount++;
                }
                // A reentrant caller may already have queued the next response.
                // Leave it for the next frame rather than draining indefinitely.
                return;
            }
        }

        public void Dispose()
        {
            EnsureMainThread();
            List<ActiveRequest> cancel;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                enabled = false;
                cancel = InvalidateAllLocked();
                completed.Clear();
            }
            CancelOutsideLock(cancel);
        }

        private void RunWorker(ActiveRequest active)
        {
            var timer = Stopwatch.StartNew();
            LocalDecisionHttpResponse response;
            LocalDecisionParsedReply parsed = null;
            try
            {
                lock (gate)
                {
                    if (!IsCurrentLocked(active)) return;
                }
                response = transport.Send(active.Endpoint, (byte[])active.PayloadBytes.Clone(),
                    active.Connection.TimeoutMilliseconds, active.Cancellation.Token);
                if (response == null)
                    response = new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "empty_transport_response");
                if (response.FailureKind == NpcDecisionFailureKind.None && response.StatusCode == 200)
                    parsed = LocalDecisionWire.Parse(response.Body);
            }
            catch (WebException error)
            {
                response = WorkerFailure(active, error is WebException && error.Status == WebExceptionStatus.Timeout
                    ? NpcDecisionFailureKind.Timeout : NpcDecisionFailureKind.Transport,
                    error is WebException && error.Status == WebExceptionStatus.Timeout ? "timeout" : "transport_failure");
            }
            catch (TimeoutException)
            {
                response = WorkerFailure(active, NpcDecisionFailureKind.Timeout, "timeout");
            }
            catch (IOException)
            {
                response = WorkerFailure(active, NpcDecisionFailureKind.Transport, "io_failure");
            }
            catch (OperationCanceledException)
            {
                response = WorkerFailure(active, NpcDecisionFailureKind.Transport, "cancelled");
            }
            catch (ObjectDisposedException)
            {
                response = WorkerFailure(active, NpcDecisionFailureKind.Transport, "cancelled");
            }
            catch (InvalidOperationException)
            {
                response = WorkerFailure(active, NpcDecisionFailureKind.Transport, "transport_protocol_failure");
            }
            catch (ArgumentException)
            {
                response = WorkerFailure(active, NpcDecisionFailureKind.Transport, "invalid_transport_response");
            }
            catch (Exception)
            {
                response = WorkerFailure(active, NpcDecisionFailureKind.Transport, "worker_failure");
            }
            finally
            {
                active.Cancellation.Dispose();
            }
            timer.Stop();

            lock (gate)
            {
                if (!IsCurrentLocked(active)) return;
                completed.Enqueue(new CompletedRequest(active, response, parsed, timer.Elapsed.TotalMilliseconds));
            }
        }

        private NpcDecisionResult BuildResult(CompletedRequest completedResult)
        {
            ActiveRequest active = completedResult.Active;
            LocalDecisionHttpResponse response = completedResult.Response;
            double latency = completedResult.LatencyMilliseconds;
            LocalDecisionFreshness current;
            try { current = active.CaptureCurrent(); }
            catch (Exception)
            {
                return NpcDecisionResult.Stale(active.Request, "decision_binding_changed", active.PayloadHash,
                    active.ExpectedModelId, latency, response.StatusCode);
            }
            if (current == null || !NpcDecisionResult.IsCurrent(active.Request, current.Binding,
                current.SnapshotRevision, current.CandidateSetRevision, current.Options))
                return NpcDecisionResult.Stale(active.Request, "decision_binding_changed", active.PayloadHash,
                    active.ExpectedModelId, latency, response.StatusCode);

            if (response.FailureKind == NpcDecisionFailureKind.Timeout || response.FailureKind == NpcDecisionFailureKind.Transport)
                return NpcDecisionResult.Unavailable(active.Request, response.FailureKind,
                    SafeDiagnostic(response.DiagnosticCode, response.FailureKind == NpcDecisionFailureKind.Timeout ? "timeout" : "transport_failure"),
                    active.PayloadHash, active.ExpectedModelId, latency, response.StatusCode);
            if (!response.StatusCode.HasValue)
                return NpcDecisionResult.Unavailable(active.Request, NpcDecisionFailureKind.Transport, "missing_http_status",
                    active.PayloadHash, active.ExpectedModelId, latency, null);
            if (response.FailureKind == NpcDecisionFailureKind.HttpStatus)
                return NpcDecisionResult.Unavailable(active.Request, NpcDecisionFailureKind.HttpStatus, "http_status",
                    active.PayloadHash, active.ExpectedModelId, latency, response.StatusCode);
            if (response.StatusCode.HasValue && response.StatusCode.Value != 200)
                return NpcDecisionResult.Unavailable(active.Request, NpcDecisionFailureKind.HttpStatus, "http_status",
                    active.PayloadHash, active.ExpectedModelId, latency, response.StatusCode);
            if (response.FailureKind != NpcDecisionFailureKind.None)
                return NpcDecisionResult.Invalid(active.Request,
                    response.FailureKind == NpcDecisionFailureKind.MalformedJson ? NpcDecisionFailureKind.MalformedJson : NpcDecisionFailureKind.InvalidSchema,
                    SafeDiagnostic(response.DiagnosticCode, "invalid_response"), active.PayloadHash,
                    active.ExpectedModelId, latency, response.StatusCode);

            LocalDecisionParsedReply parsed = completedResult.Parsed ?? LocalDecisionWire.Parse(response.Body);
            if (!parsed.IsValid)
                return NpcDecisionResult.Invalid(active.Request, parsed.FailureKind,
                    SafeDiagnostic(parsed.DiagnosticCode, "invalid_response"), active.PayloadHash,
                    active.ExpectedModelId, latency, response.StatusCode);
            if (!string.Equals(parsed.ModelId, active.ExpectedModelId, StringComparison.Ordinal))
                return NpcDecisionResult.Invalid(active.Request, NpcDecisionFailureKind.InvalidSchema, "model_mismatch",
                    active.PayloadHash, active.ExpectedModelId, latency, response.StatusCode);
            return NpcDecisionResult.ValidateResponse(active.Request, parsed.SelectedOptionId, parsed.Probabilities,
                parsed.ReportedConfidence, active.PayloadHash, parsed.ModelId, latency, response.StatusCode,
                current.Binding, current.SnapshotRevision, current.CandidateSetRevision, current.Options);
        }

        private LocalDecisionHttpResponse WorkerFailure(ActiveRequest active, NpcDecisionFailureKind kind, string diagnostic)
        {
            if (active.Cancellation.IsCancellationRequested)
                return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "cancelled");
            return new LocalDecisionHttpResponse(null, null, kind, diagnostic);
        }

        private List<ActiveRequest> InvalidateAllLocked()
        {
            var cancel = new List<ActiveRequest>();
            if (active != null)
            {
                cancel.Add(active);
                InvalidateLocked(active);
            }
            completed.Clear();
            return cancel;
        }

        private void InvalidateLocked(ActiveRequest active)
        {
            active.Valid = false;
            if (ReferenceEquals(this.active, active))
            {
                this.active = null;
                completed.Clear();
            }
        }

        private bool IsCurrentLocked(ActiveRequest active)
        {
            return !disposed && enabled && ValidModelId(expectedModelId) && active.Valid &&
                string.Equals(expectedModelId, active.ExpectedModelId, StringComparison.Ordinal) &&
                ReferenceEquals(this.active, active);
        }

        private void AdvanceConfigurationGenerationLocked()
        {
            if (configurationGeneration < long.MaxValue) configurationGeneration++;
        }

        private static Uri SystemOneEndpoint(Uri endpoint)
        {
            if (endpoint == null || !endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttp ||
                !endpoint.IsLoopback || !string.IsNullOrEmpty(endpoint.UserInfo)) return null;
            var builder = new UriBuilder(endpoint) { Path = "/v1/systemone", Query = string.Empty, Fragment = string.Empty };
            return builder.Uri;
        }

        private static bool ValidModelId(string value)
            => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);

        private static string SafeDiagnostic(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(character =>
                !((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') ||
                  (character >= '0' && character <= '9') || character == '_' || character == '.' || character == ':' || character == '-')))
                return fallback;
            return value;
        }

        private static void CancelOutsideLock(IEnumerable<ActiveRequest> requests)
        {
            foreach (var request in requests) CancelOne(request);
        }

        private static void CancelOne(ActiveRequest active)
        {
            try { active.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        private void EnsureMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
                throw new InvalidOperationException("Local decision session operations require the creation thread.");
        }
    }
}
