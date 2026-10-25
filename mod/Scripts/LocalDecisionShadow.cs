using System;
using System.Collections.Generic;
using System.Threading;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Safe shadow telemetry. Deliberately excludes the request's player text and context.
    internal sealed class LocalDecisionShadowObservation
    {
        public Guid RequestId { get; }
        public ConversationContextBinding Binding { get; }
        public long SnapshotRevision { get; }
        public long CandidateSetRevision { get; }
        public NpcDecisionStatus Status { get; }
        public NpcDecisionFailureKind FailureKind { get; }
        public string SelectedOptionId { get; }
        public string RequestSha256 { get; }
        public string ModelId { get; }
        public double LatencyMilliseconds { get; }
        public int? HttpStatusCode { get; }
        public string DiagnosticCode { get; }
        public bool IsShadow => true;
        // This describes dispatch performed by the shadow coordinator, not a global game-world observation.
        public bool NativeDispatchAttempted => false;

        internal LocalDecisionShadowObservation(NpcDecisionResult result)
        {
            RequestId = result.RequestId;
            Binding = result.Binding;
            SnapshotRevision = result.SnapshotRevision;
            CandidateSetRevision = result.CandidateSetRevision;
            Status = result.Status;
            FailureKind = result.FailureKind;
            SelectedOptionId = result.SelectedOptionId;
            RequestSha256 = result.RequestSha256;
            ModelId = result.ModelId;
            LatencyMilliseconds = result.LatencyMilliseconds;
            HttpStatusCode = result.HttpStatusCode;
            DiagnosticCode = result.DiagnosticCode;
        }
    }

    internal sealed class LocalDecisionShadow : IDisposable
    {
        private sealed class PendingRequest
        {
            public readonly NpcDecisionRequest Request;
            public readonly Func<LocalDecisionFreshness> CaptureCurrent;
            public PendingRequest(NpcDecisionRequest request, Func<LocalDecisionFreshness> captureCurrent)
            { Request = request; CaptureCurrent = captureCurrent; }
        }

        private readonly LocalDecisionSession session;
        private readonly LocalDialogueConnection connection;
        private readonly Action<LocalDecisionShadowObservation> record;
        private readonly int mainThreadId;
        private PendingRequest pending;
        private NpcDecisionRequest activeRequest;
        private NpcDecisionRequest lastObserved;
        private long lastObservedConfigurationGeneration = -1;
        private bool disposed;
        private long submittedCount;
        private long completedCount;
        private long coalescedCount;
        private long cancelledCount;
        private long observerFailureCount;
        private LocalDecisionShadowObservation lastObservation;

        public long SubmittedCount => submittedCount;
        public long CompletedCount => completedCount;
        public long CoalescedCount => coalescedCount;
        public long CancelledCount => cancelledCount;
        public long ObserverFailureCount => observerFailureCount;
        public LocalDecisionShadowObservation LastObservation => lastObservation;

        public LocalDecisionShadow(LocalDecisionSession session, LocalDialogueConnection connection,
            Action<LocalDecisionShadowObservation> record)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.record = record ?? throw new ArgumentNullException(nameof(record));
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public bool Observe(NpcDecisionRequest request, Func<LocalDecisionFreshness> current)
        {
            EnsureMainThread();
            if (disposed || request == null || current == null) return false;
            long configurationGeneration = session.ConfigurationGeneration;
            if (lastObserved != null && lastObservedConfigurationGeneration == configurationGeneration &&
                SameLogicalRequest(lastObserved, request)) return false;
            if (pending != null) Increment(ref coalescedCount);
            pending = new PendingRequest(request, current);
            lastObserved = request;
            lastObservedConfigurationGeneration = configurationGeneration;
            return true;
        }

        public void Pump()
        {
            EnsureMainThread();
            if (disposed) return;
            PendingRequest next = pending;
            if (next != null)
            {
                if (activeRequest != null)
                {
                    if (session.Cancel(activeRequest.RequestId)) Increment(ref cancelledCount);
                    activeRequest = null;
                }
                if (session.TryRequest(connection, next.Request, next.CaptureCurrent, OnDecision))
                {
                    activeRequest = next.Request;
                    pending = null;
                    Increment(ref submittedCount);
                }
            }
            session.Pump();
        }

        public void CancelAll()
        {
            EnsureMainThread();
            if (disposed) return;
            if (pending != null)
            {
                pending = null;
                Increment(ref cancelledCount);
            }
            if (activeRequest != null)
            {
                if (session.Cancel(activeRequest.RequestId)) Increment(ref cancelledCount);
                activeRequest = null;
            }
            lastObserved = null;
            lastObservedConfigurationGeneration = -1;
        }

        public void Dispose()
        {
            EnsureMainThread();
            if (disposed) return;
            CancelAll();
            disposed = true;
        }

        private void OnDecision(NpcDecisionResult result)
        {
            if (disposed || result == null || activeRequest == null || result.RequestId != activeRequest.RequestId) return;
            activeRequest = null;
            Increment(ref completedCount);
            var observation = new LocalDecisionShadowObservation(result);
            lastObservation = observation;
            try { record(observation); }
            catch (Exception) { Increment(ref observerFailureCount); }
        }

        private static bool SameLogicalRequest(NpcDecisionRequest left, NpcDecisionRequest right)
        {
            if (left == null || right == null || !left.Binding.HasSameRequestBinding(right.Binding) ||
                left.SnapshotRevision != right.SnapshotRevision || left.CandidateSetRevision != right.CandidateSetRevision ||
                !Same(left.ProtocolVersion, right.ProtocolVersion) || !Same(left.Purpose, right.Purpose) ||
                !Same(left.Instructions, right.Instructions) || !Same(left.OriginalTurn, right.OriginalTurn) ||
                !Same(left.Context, right.Context) || left.Options.Count != right.Options.Count) return false;
            for (int i = 0; i < left.Options.Count; i++)
            {
                NpcDecisionOption a = left.Options[i];
                NpcDecisionOption b = right.Options[i];
                if (!Same(a.Id, b.Id) || !Same(a.Description, b.Description) || a.IsEffectful != b.IsEffectful) return false;
            }
            return true;
        }

        private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
        private static void Increment(ref long value) { if (value < long.MaxValue) value++; }

        private void EnsureMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
                throw new InvalidOperationException("Local decision shadow must be used on its creating thread.");
        }
    }
}
