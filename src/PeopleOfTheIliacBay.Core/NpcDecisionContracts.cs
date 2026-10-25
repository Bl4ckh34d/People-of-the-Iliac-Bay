using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcDecisionStatus { Ok, Unavailable, Invalid, Stale }

    public enum NpcDecisionFailureKind
    {
        None, Timeout, Transport, HttpStatus, MalformedJson, InvalidSchema, InvalidProbabilityVector
    }

    public sealed class NpcDecisionOption
    {
        public string Id { get; }
        public string Description { get; }
        public bool IsEffectful { get; }

        public NpcDecisionOption(string id, string description, bool isEffectful)
        {
            if (!ValidOptionId(id) || string.IsNullOrWhiteSpace(description) || description.Length > 512 ||
                description.Any(char.IsControl))
                throw new ArgumentException("Invalid NPC decision option.");
            Id = id;
            Description = description;
            IsEffectful = isEffectful;
        }

        private static bool ValidOptionId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
            foreach (char character in value)
            {
                if (!((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9') || character == '_' || character == '.' ||
                    character == ':' || character == '-')) return false;
            }
            return true;
        }
    }

    public sealed class NpcDecisionRequest
    {
        public const string CurrentProtocolVersion = "npc-decision-v2";
        private readonly ReadOnlyCollection<NpcDecisionOption> options;
        public Guid RequestId { get; }
        public ConversationContextBinding Binding { get; }
        public long SnapshotRevision { get; }
        public long CandidateSetRevision { get; }
        public string ProtocolVersion { get; }
        public string Purpose { get; }
        public string Instructions { get; }
        public string OriginalTurn { get; }
        public string Context { get; }
        public IReadOnlyList<NpcDecisionOption> Options => options;

        public NpcDecisionRequest(Guid requestId, ConversationContextSnapshot snapshot,
            long candidateSetRevision, string purpose, string originalTurn, string context,
            IEnumerable<NpcDecisionOption> options)
            : this(requestId, snapshot, candidateSetRevision, purpose, purpose, originalTurn, context, options)
        { }

        public NpcDecisionRequest(Guid requestId, ConversationContextSnapshot snapshot,
            long candidateSetRevision, string purpose, string instructions, string originalTurn, string context,
            IEnumerable<NpcDecisionOption> options)
        {
            if (requestId == Guid.Empty || snapshot == null || candidateSetRevision < 0 ||
                !BoundedText(purpose, 128) || !BoundedMultilineText(instructions, 8192) ||
                !BoundedMultilineText(originalTurn, 8192) || !BoundedMultilineText(context, 16384))
                throw new ArgumentException("Invalid NPC decision request.");
            var copied = CopyOptions(options, 1, 32);
            if (copied.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != copied.Count)
                throw new ArgumentException("NPC decision option IDs must be unique.");
            RequestId = requestId;
            Binding = snapshot.Binding;
            SnapshotRevision = snapshot.Revision;
            CandidateSetRevision = candidateSetRevision;
            ProtocolVersion = CurrentProtocolVersion;
            Purpose = purpose;
            Instructions = instructions;
            OriginalTurn = originalTurn;
            Context = context;
            this.options = new ReadOnlyCollection<NpcDecisionOption>(copied);
        }

        private static bool BoundedText(string value, int maximum)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
        }

        private static bool BoundedMultilineText(string value, int maximum)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= maximum &&
                !value.Any(character => char.IsControl(character) && character != '\r' && character != '\n' && character != '\t');
        }

        internal static List<NpcDecisionOption> CopyOptions(IEnumerable<NpcDecisionOption> values, int minimum, int maximum)
        {
            if (values == null) throw new ArgumentException("NPC decision options are required.");
            var copy = new List<NpcDecisionOption>();
            foreach (var value in values)
            {
                if (value == null) throw new ArgumentException("NPC decision options cannot contain null.");
                copy.Add(new NpcDecisionOption(value.Id, value.Description, value.IsEffectful));
                if (copy.Count > maximum) throw new ArgumentException("Too many NPC decision options.");
            }
            if (copy.Count < minimum) throw new ArgumentException("Too few NPC decision options.");
            return copy;
        }
    }

    public sealed class NpcDecisionResult
    {
        private const double ProbabilitySumTolerance = 1e-3;
        private const double MaximumTolerance = 1e-6;
        private readonly ReadOnlyDictionary<string, double> probabilities;

        public NpcDecisionStatus Status { get; }
        public NpcDecisionFailureKind FailureKind { get; }
        public Guid RequestId { get; }
        public ConversationContextBinding Binding { get; }
        public long SnapshotRevision { get; }
        public long CandidateSetRevision { get; }
        // Correlation metadata from the trusted caller; syntax checks do not prove body/model provenance.
        public string RequestSha256 { get; }
        public string ModelId { get; }
        public string SelectedOptionId { get; }
        public IReadOnlyDictionary<string, double> Probabilities => probabilities;
        public double? ReportedConfidence { get; }
        public double LatencyMilliseconds { get; }
        public int? HttpStatusCode { get; }
        public string DiagnosticCode { get; }

        private NpcDecisionResult(NpcDecisionRequest request, NpcDecisionStatus status,
            NpcDecisionFailureKind failureKind, string requestSha256, string modelId,
            double latencyMilliseconds, int? httpStatusCode, string diagnosticCode,
            string selectedOptionId, IDictionary<string, double> probabilityVector, double? reportedConfidence)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            RequestId = request.RequestId;
            Binding = request.Binding;
            SnapshotRevision = request.SnapshotRevision;
            CandidateSetRevision = request.CandidateSetRevision;
            Status = status;
            FailureKind = failureKind;
            RequestSha256 = ValidHash(requestSha256) ? requestSha256 : null;
            ModelId = ValidModelId(modelId) ? modelId : null;
            LatencyMilliseconds = latencyMilliseconds;
            HttpStatusCode = httpStatusCode;
            DiagnosticCode = diagnosticCode;
            SelectedOptionId = selectedOptionId;
            if (status == NpcDecisionStatus.Ok)
            {
                probabilities = probabilityVector == null ? null :
                    new ReadOnlyDictionary<string, double>(new Dictionary<string, double>(probabilityVector, StringComparer.Ordinal));
                ReportedConfidence = reportedConfidence;
            }
            else
            {
                probabilities = null;
                ReportedConfidence = null;
                SelectedOptionId = null;
            }
        }

        public static bool IsCurrent(NpcDecisionRequest request, ConversationContextBinding currentBinding,
            long currentSnapshotRevision, long currentCandidateSetRevision, IEnumerable<NpcDecisionOption> currentOptions)
        {
            return request != null && currentBinding != null && request.Binding.HasSameRequestBinding(currentBinding) &&
                currentSnapshotRevision == request.SnapshotRevision && currentCandidateSetRevision == request.CandidateSetRevision &&
                SameOptions(request.Options, currentOptions);
        }

        public static NpcDecisionResult ValidateResponse(NpcDecisionRequest request,
            string selectedOptionId, IReadOnlyDictionary<string, double> probabilityVector,
            double? reportedConfidence, string requestSha256, string modelId,
            double latencyMilliseconds, int? httpStatusCode,
            ConversationContextBinding currentBinding, long currentSnapshotRevision,
            long currentCandidateSetRevision, IEnumerable<NpcDecisionOption> currentOptions)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!ValidHash(requestSha256) || !ValidModelId(modelId) || !Finite(latencyMilliseconds) || latencyMilliseconds < 0 ||
                (httpStatusCode.HasValue && (httpStatusCode.Value < 100 || httpStatusCode.Value > 599)) ||
                (reportedConfidence.HasValue && (!Finite(reportedConfidence.Value) || reportedConfidence.Value < 0 || reportedConfidence.Value > 1)))
                return InvalidFromValidation(request, "invalid_response_metadata", requestSha256, modelId, latencyMilliseconds, httpStatusCode);

            if (httpStatusCode.HasValue && httpStatusCode.Value != 200)
                return Unavailable(request, NpcDecisionFailureKind.HttpStatus, "http_status", requestSha256, modelId, latencyMilliseconds, httpStatusCode);

            var offered = new HashSet<string>(request.Options.Select(option => option.Id), StringComparer.Ordinal);
            if (string.IsNullOrEmpty(selectedOptionId) || !offered.Contains(selectedOptionId))
                return Invalid(request, NpcDecisionFailureKind.InvalidSchema, "unoffered_choice", requestSha256, modelId, latencyMilliseconds, httpStatusCode);

            Dictionary<string, double> copiedProbabilities = null;
            if (probabilityVector != null)
            {
                copiedProbabilities = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var pair in probabilityVector)
                {
                    if (pair.Key == null || copiedProbabilities.ContainsKey(pair.Key))
                        return Invalid(request, NpcDecisionFailureKind.InvalidProbabilityVector, "invalid_probability_keys", requestSha256, modelId, latencyMilliseconds, httpStatusCode);
                    copiedProbabilities.Add(pair.Key, pair.Value);
                }
                if (copiedProbabilities.Count != offered.Count || copiedProbabilities.Keys.Any(key => !offered.Contains(key)) ||
                    copiedProbabilities.Values.Any(value => !Finite(value) || value < 0 || value > 1))
                    return Invalid(request, NpcDecisionFailureKind.InvalidProbabilityVector, "invalid_probability_keys_or_range", requestSha256, modelId, latencyMilliseconds, httpStatusCode);
                double sum = copiedProbabilities.Values.Sum();
                double maximum = copiedProbabilities.Values.Max();
                if (Math.Abs(sum - 1.0) > ProbabilitySumTolerance || copiedProbabilities[selectedOptionId] < maximum - MaximumTolerance)
                    return Invalid(request, NpcDecisionFailureKind.InvalidProbabilityVector, "invalid_probability_sum_or_choice", requestSha256, modelId, latencyMilliseconds, httpStatusCode);
            }

            if (!IsCurrent(request, currentBinding, currentSnapshotRevision, currentCandidateSetRevision, currentOptions))
                return Stale(request, "decision_binding_changed", requestSha256, modelId, latencyMilliseconds, httpStatusCode);

            return new NpcDecisionResult(request, NpcDecisionStatus.Ok, NpcDecisionFailureKind.None,
                requestSha256, modelId, latencyMilliseconds, httpStatusCode, "ok",
                selectedOptionId, copiedProbabilities, reportedConfidence);
        }

        public static NpcDecisionResult Unavailable(NpcDecisionRequest request,
            NpcDecisionFailureKind failureKind, string diagnosticCode, string requestSha256,
            string modelId, double latencyMilliseconds, int? httpStatusCode)
        {
            if (failureKind != NpcDecisionFailureKind.Timeout && failureKind != NpcDecisionFailureKind.Transport &&
                failureKind != NpcDecisionFailureKind.HttpStatus)
                throw new ArgumentException("Unavailable requires a transport failure kind.");
            ValidateFailureMetadata(request, diagnosticCode, latencyMilliseconds, httpStatusCode);
            return Failure(request, NpcDecisionStatus.Unavailable, failureKind, diagnosticCode,
                requestSha256, modelId, latencyMilliseconds, httpStatusCode);
        }

        public static NpcDecisionResult Invalid(NpcDecisionRequest request,
            NpcDecisionFailureKind failureKind, string diagnosticCode, string requestSha256,
            string modelId, double latencyMilliseconds, int? httpStatusCode)
        {
            if (failureKind != NpcDecisionFailureKind.MalformedJson && failureKind != NpcDecisionFailureKind.InvalidSchema &&
                failureKind != NpcDecisionFailureKind.InvalidProbabilityVector)
                throw new ArgumentException("Invalid requires a response-validation failure kind.");
            ValidateFailureMetadata(request, diagnosticCode, latencyMilliseconds, httpStatusCode);
            return Failure(request, NpcDecisionStatus.Invalid, failureKind, diagnosticCode,
                requestSha256, modelId, latencyMilliseconds, httpStatusCode);
        }

        public static NpcDecisionResult Stale(NpcDecisionRequest request, string diagnosticCode,
            string requestSha256, string modelId, double latencyMilliseconds, int? httpStatusCode)
        {
            ValidateFailureMetadata(request, diagnosticCode, latencyMilliseconds, httpStatusCode);
            return Failure(request, NpcDecisionStatus.Stale, NpcDecisionFailureKind.None, diagnosticCode,
                requestSha256, modelId, latencyMilliseconds, httpStatusCode);
        }

        private static NpcDecisionResult Failure(NpcDecisionRequest request, NpcDecisionStatus status,
            NpcDecisionFailureKind failureKind, string diagnosticCode, string requestSha256,
            string modelId, double latencyMilliseconds, int? httpStatusCode)
        {
            return new NpcDecisionResult(request, status, failureKind, requestSha256, modelId,
                latencyMilliseconds, httpStatusCode, diagnosticCode, null, null, null);
        }

        private static NpcDecisionResult InvalidFromValidation(NpcDecisionRequest request, string diagnosticCode,
            string requestSha256, string modelId, double latencyMilliseconds, int? httpStatusCode)
        {
            double safeLatency = Finite(latencyMilliseconds) && latencyMilliseconds >= 0 ? latencyMilliseconds : 0;
            int? safeStatus = httpStatusCode.HasValue && httpStatusCode.Value >= 100 && httpStatusCode.Value <= 599
                ? httpStatusCode : null;
            return Failure(request, NpcDecisionStatus.Invalid, NpcDecisionFailureKind.InvalidSchema,
                diagnosticCode, requestSha256, modelId, safeLatency, safeStatus);
        }
        private static void ValidateFailureMetadata(NpcDecisionRequest request, string diagnosticCode,
            double latencyMilliseconds, int? httpStatusCode)
        {
            if (request == null || !ValidDiagnostic(diagnosticCode) || !Finite(latencyMilliseconds) || latencyMilliseconds < 0 ||
                (httpStatusCode.HasValue && (httpStatusCode.Value < 100 || httpStatusCode.Value > 599)))
                throw new ArgumentException("Invalid NPC decision failure metadata.");
        }

        private static bool SameOptions(IReadOnlyList<NpcDecisionOption> expected, IEnumerable<NpcDecisionOption> actual)
        {
            if (actual == null) return false;
            List<NpcDecisionOption> current;
            try { current = NpcDecisionRequest.CopyOptions(actual, 1, 32); }
            catch (ArgumentException) { return false; }
            if (expected.Count != current.Count) return false;
            for (int index = 0; index < expected.Count; index++)
            {
                var left = expected[index];
                var right = current[index];
                if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal) ||
                    !string.Equals(left.Description, right.Description, StringComparison.Ordinal) || left.IsEffectful != right.IsEffectful)
                    return false;
            }
            return true;
        }

        private static bool ValidHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            return value.All(character => (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F'));
        }

        private static bool ValidModelId(string value)
            => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);

        private static bool ValidDiagnostic(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value.All(character =>
                (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9') || character == '_' || character == '.' || character == ':' || character == '-');
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
