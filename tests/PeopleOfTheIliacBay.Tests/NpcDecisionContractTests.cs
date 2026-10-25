using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcDecisionContractTests
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
        private static Assembly Core => typeof(WorldStateStore).Assembly;
        private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Decision request copies bounded offered options", RequestCopiesAndBounds));
            cases.Add(("Decision IDs use bounded ordinal ASCII option identifiers", OptionIdentifiers));
            cases.Add(("Decision result validates selected option and independent confidence", ValidResultAndConfidence));
            cases.Add(("Decision result rejects malformed probability vectors without action", InvalidVectors));
            cases.Add(("Decision result invalidates stale binding snapshot candidate set and options", StaleResults));
            cases.Add(("Decision result keeps archive-head advance within request binding", ArchiveHeadAdvance));
            cases.Add(("Decision failure factories expose no selected option or probability vector", FailureFactories));
        }

        private static Type Required(string name)
        {
            var type = Core.GetType("PeopleOfTheIliacBay.Core." + name, false);
            Check.True(type != null, "Missing decision contract type PeopleOfTheIliacBay.Core." + name);
            return type;
        }

        private static Type StatusType => Required("NpcDecisionStatus");
        private static Type FailureType => Required("NpcDecisionFailureKind");

        private static object EnumValue(Type type, string name) => Enum.Parse(type, name);

        private static ConversationContextBinding Binding(string personId = "resident:42", string session = "session-a",
            long requestRevision = 3, long saveEpoch = 7, string archiveId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            string archiveHead = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            string sceneId = "scene-a", long sceneRevision = 5,
            ConversationActorCategory category = ConversationActorCategory.Mobile)
            => new ConversationContextBinding(category, personId, null, session, requestRevision, saveEpoch,
                archiveId, archiveHead, sceneId, sceneRevision);

        private static ConversationContextSnapshot Snapshot(ConversationContextBinding binding = null)
        {
            var empty = ConversationContextFact<string>.Unknown();
            return new ConversationContextSnapshot(binding ?? Binding(), 100,
                empty, empty, ConversationContextFact<ConversationDialogueRole>.Unknown(),
                empty, empty, ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), empty, 0, 17,
                null, new List<ConversationContextCommitment>(), new List<ConversationContextSourceHandle>());
        }

        private static object Option(string id, string description = null, bool effectful = true)
        {
            var type = Required("NpcDecisionOption");
            return Invoke(() => Activator.CreateInstance(type, new object[] { id, description ?? ("Option " + id), effectful }));
        }

        private static object Request(ConversationContextSnapshot snapshot = null, long candidateRevision = 11,
            IEnumerable options = null)
        {
            var optionType = Required("NpcDecisionOption");
            var typedList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(optionType));
            foreach (var option in options ?? new object[] { Option("wait_here"), Option("clarify", "Ask a question", false) })
                typedList.Add(option);
            var requestType = Required("NpcDecisionRequest");
            return Invoke(() => Activator.CreateInstance(requestType, new object[] {
                Guid.NewGuid(), snapshot ?? Snapshot(), candidateRevision,
                "action-choice", "Please wait here.", "Captured context.", typedList
            }));
        }

        private static object Validate(object request, string selectedId = "wait_here",
            IDictionary<string, double> probabilities = null, double? confidence = 0.17,
            ConversationContextBinding binding = null, long? snapshotRevision = null,
            long candidateRevision = 11, IEnumerable currentOptions = null,
            string requestHash = Hash, string modelId = "local-model", double latencyMilliseconds = 12.5)
        {
            if (probabilities == null)
                probabilities = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["wait_here"] = 0.7,
                    ["clarify"] = 0.3
                };
            var requestType = Required("NpcDecisionRequest");
            var optionType = Required("NpcDecisionOption");
            var typedOptions = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(optionType));
            if (currentOptions == null)
            {
                foreach (var option in (IEnumerable)requestType.GetProperty("Options", PublicInstance).GetValue(request))
                    typedOptions.Add(option);
            }
            else
            {
                foreach (var option in currentOptions) typedOptions.Add(option);
            }
            var method = Required("NpcDecisionResult").GetMethod("ValidateResponse", PublicStatic);
            Check.True(method != null, "Missing NpcDecisionResult.ValidateResponse");
            var args = new object[] {
                request, selectedId, probabilities, confidence, requestHash, modelId, latencyMilliseconds, (int?)200,
                binding ?? (ConversationContextBinding)requestType.GetProperty("Binding", PublicInstance).GetValue(request),
                snapshotRevision ?? (long)requestType.GetProperty("SnapshotRevision", PublicInstance).GetValue(request),
                candidateRevision, typedOptions
            };
            return Invoke(() => method.Invoke(null, args));
        }

        private static object Invoke(Func<object> action)
        {
            try { return action(); }
            catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }

        private static object Property(object instance, string name)
            => instance.GetType().GetProperty(name, PublicInstance).GetValue(instance);

        private static T Value<T>(object instance, string name) => (T)Property(instance, name);

        private static void Status(object result, string value)
            => Check.Equal(value, Enum.GetName(StatusType, Property(result, "Status")));

        private static void NoActionEvidence(object result)
        {
            Check.Equal(null, Property(result, "SelectedOptionId"));
            var probabilities = Property(result, "Probabilities");
            Check.True(probabilities == null || ((ICollection)probabilities).Count == 0,
                "A failed/stale decision must expose no probability vector.");
            Check.Equal(null, Property(result, "ReportedConfidence"));
        }

        private static void RequestCopiesAndBounds()
        {
            var options = new List<object> { Option("wait_here"), Option("clarify", "Ask a question", false) };
            var firstOriginal = options[0];
            var request = Request(options: options);
            var copied = (IList)Property(request, "Options");
            options.Clear();
            Check.Equal(2, copied.Count);
            Check.True(!ReferenceEquals(firstOriginal, copied[0]), "Option collection must own defensive copies.");
            Check.Equal(0L, Value<long>(request, "SnapshotRevision"));
            Check.Equal(11L, Value<long>(request, "CandidateSetRevision"));
            Check.Equal(Binding().HasSameRequestBinding(Value<ConversationContextBinding>(request, "Binding")), true);
            Check.Throws<NotSupportedException>(() => copied[0] = Option("mutated"));

            Check.Throws<ArgumentException>(() => Request(options: new object[0]));
            Check.Equal(1, ((IList)Property(Request(options: new[] { Option("only-one") }), "Options")).Count);
            Check.Throws<ArgumentException>(() => Request(candidateRevision: -1));
            Check.Throws<ArgumentException>(() => Request(options: new[] { Option("same"), Option("same") }));
            Check.Throws<ArgumentException>(() => Request(options: new[] { Option("wait_here", new string('x', 513)), Option("clarify") }));
        }

        private static void OptionIdentifiers()
        {
            foreach (var id in new[] { "A", "wait_here", "A.B:C-9", new string('x', 128) })
                Option(id);
            foreach (var id in new[] { "", " has-space", "white space", "ümlaut", "slash/", new string('x', 129) })
                Check.Throws<ArgumentException>(() => Option(id));
            Check.True(Enum.GetNames(StatusType).SequenceEqual(new[] { "Ok", "Unavailable", "Invalid", "Stale" }));
            foreach (var value in new[] { "None", "Timeout", "Transport", "HttpStatus", "MalformedJson", "InvalidSchema", "InvalidProbabilityVector" })
                Check.True(Enum.IsDefined(FailureType, value), "Missing failure type " + value);
        }

        private static void ValidResultAndConfidence()
        {
            var request = Request();
            var probabilitiesSource = new Dictionary<string, double> { ["wait_here"] = 0.7, ["clarify"] = 0.3 };
            var result = Validate(request, probabilities: probabilitiesSource, confidence: 0.17);
            Status(result, "Ok");
            Check.Equal("wait_here", Value<string>(result, "SelectedOptionId"));
            Check.Near(0.7, ((IDictionary<string, double>)Property(result, "Probabilities"))["wait_here"]);
            Check.Near(0.17, Value<double?>(result, "ReportedConfidence").Value);
            Check.Equal(Hash, Value<string>(result, "RequestSha256"));
            Check.Equal("local-model", Value<string>(result, "ModelId"));
            Check.Equal(12.5, Value<double>(result, "LatencyMilliseconds"));
            Check.Equal((int?)200, Value<int?>(result, "HttpStatusCode"));
            probabilitiesSource["wait_here"] = 0.0;
            Check.Near(0.7, ((IDictionary<string, double>)Property(result, "Probabilities"))["wait_here"]);
            Check.Throws<NotSupportedException>(() => ((IDictionary<string, double>)Property(result, "Probabilities"))["wait_here"] = 0.2);

            var tied = Validate(request, selectedId: "clarify",
                probabilities: new Dictionary<string, double> { ["wait_here"] = 0.5, ["clarify"] = 0.5 }, confidence: 0.98);
            Status(tied, "Ok");
            Check.Equal("clarify", Value<string>(tied, "SelectedOptionId"));
            Check.Near(0.98, Value<double?>(tied, "ReportedConfidence").Value);
        }

        private static void InvalidVectors()
        {
            var request = Request();
            var badVectors = new[]
            {
                new Dictionary<string, double> { ["wait_here"] = 1.0 },
                new Dictionary<string, double> { ["wait_here"] = 0.7, ["clarify"] = 0.2, ["extra"] = 0.1 },
                new Dictionary<string, double> { ["wait_here"] = double.NaN, ["clarify"] = 0.0 },
                new Dictionary<string, double> { ["wait_here"] = double.PositiveInfinity, ["clarify"] = 0.0 },
                new Dictionary<string, double> { ["wait_here"] = -0.1, ["clarify"] = 1.1 },
                new Dictionary<string, double> { ["wait_here"] = 0.6, ["clarify"] = 0.3 },
                new Dictionary<string, double> { ["wait_here"] = 0.2, ["clarify"] = 0.8 }
            };
            foreach (var vector in badVectors)
            {
                var result = Validate(request, probabilities: vector);
                Status(result, "Invalid");
                Check.Equal("InvalidProbabilityVector", Enum.GetName(FailureType, Property(result, "FailureKind")));
                NoActionEvidence(result);
            }

            var unknown = Validate(request, selectedId: "invented");
            Status(unknown, "Invalid");
            Check.Equal("InvalidSchema", Enum.GetName(FailureType, Property(unknown, "FailureKind")));
            NoActionEvidence(unknown);
            foreach (var confidence in new double?[] { -0.01, 1.01, double.NaN, double.PositiveInfinity })
            {
                var result = Validate(request, confidence: confidence);
                Status(result, "Invalid");
                NoActionEvidence(result);
            }
            var badHash = Validate(request, requestHash: "not-a-sha256");
            Status(badHash, "Invalid");
            NoActionEvidence(badHash);
            var badLatency = Validate(request, latencyMilliseconds: double.NaN);
            Status(badLatency, "Invalid");
            NoActionEvidence(badLatency);
        }

        private static void StaleResults()
        {
            var request = Request();
            var original = Value<ConversationContextBinding>(request, "Binding");
            var staleBindings = new[]
            {
                Binding(category: ConversationActorCategory.Guard),
                Binding(personId: "resident:other"),
                Binding(session: "session-other"),
                Binding(requestRevision: 4),
                Binding(saveEpoch: 8),
                Binding(archiveId: "cccccccccccccccccccccccccccccccc"),
                Binding(sceneId: "scene-other"),
                Binding(sceneRevision: 6),
                new ConversationContextBinding(ConversationActorCategory.Mobile, null, "speaker:ephemeral", "session-a", 3, 7,
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "scene-a", 5)
            };
            foreach (var binding in staleBindings)
            {
                var result = Validate(request, binding: binding);
                Status(result, "Stale");
                NoActionEvidence(result);
            }
            var nextSnapshot = Snapshot(original).WithCommittedDisposition(1);
            var snapshotStale = Validate(request, snapshotRevision: nextSnapshot.Revision);
            Status(snapshotStale, "Stale");
            NoActionEvidence(snapshotStale);

            var candidateStale = Validate(request, candidateRevision: 12);
            Status(candidateStale, "Stale");
            NoActionEvidence(candidateStale);

            var reordered = new object[] { Option("clarify", "Ask a question", false), Option("wait_here") };
            var orderStale = Validate(request, currentOptions: reordered);
            Status(orderStale, "Stale");
            NoActionEvidence(orderStale);
            var changedDescription = new object[] { Option("wait_here", "Changed meaning"), Option("clarify", "Ask a question", false) };
            Status(Validate(request, currentOptions: changedDescription), "Stale");
            var changedEffect = new object[] { Option("wait_here", effectful: false), Option("clarify", "Ask a question", false) };
            Status(Validate(request, currentOptions: changedEffect), "Stale");
        }

        private static void ArchiveHeadAdvance()
        {
            var request = Request();
            var advanced = Binding(archiveHead: "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd");
            Check.True(Value<ConversationContextBinding>(request, "Binding").HasSameRequestBinding(advanced),
                "Archive-head advancement within the same request branch remains fresh.");
            var result = Validate(request, binding: advanced);
            Status(result, "Ok");
        }

        private static void FailureFactories()
        {
            var request = Request();
            var resultType = Required("NpcDecisionResult");
            var method = resultType.GetMethod("Unavailable", PublicStatic);
            Check.True(method != null, "Missing Unavailable factory");
            var failure = Invoke(() => method.Invoke(null, new object[] {
                request, EnumValue(FailureType, "Timeout"), "decision_timeout", Hash, "local-model", 60.0, (int?)null
            }));
            Status(failure, "Unavailable");
            Check.Equal("Timeout", Enum.GetName(FailureType, Property(failure, "FailureKind")));
            NoActionEvidence(failure);
            Check.Equal(Value<Guid>(request, "RequestId"), Value<Guid>(failure, "RequestId"));
            Check.True(Value<ConversationContextBinding>(request, "Binding").HasSameRequestBinding(Value<ConversationContextBinding>(failure, "Binding")));
            Check.Equal(0L, Value<long>(failure, "SnapshotRevision"));
            Check.Equal(11L, Value<long>(failure, "CandidateSetRevision"));
            Check.Equal("decision_timeout", Value<string>(failure, "DiagnosticCode"));
            var unknownMetadata = Invoke(() => method.Invoke(null, new object[] {
                request, EnumValue(FailureType, "Transport"), "transport_error", null, null, 2.5, (int?)null
            }));
            Status(unknownMetadata, "Unavailable");
            Check.Equal(null, Property(unknownMetadata, "RequestSha256"));
            Check.Equal(null, Property(unknownMetadata, "ModelId"));
            Check.Equal(2.5, Value<double>(unknownMetadata, "LatencyMilliseconds"));
            Check.Equal((int?)null, Value<int?>(unknownMetadata, "HttpStatusCode"));

            foreach (var untrustedModel in new[] { new string('x', 10000), "model" + (char)1 + "name" })
            {
                var scrubbedModel = Invoke(() => resultType.GetMethod("Invalid", PublicStatic).Invoke(null, new object[] {
                    request, EnumValue(FailureType, "InvalidSchema"), "bad_schema", Hash, untrustedModel, 3.0, (int?)400
                }));
                Status(scrubbedModel, "Invalid");
                Check.Equal(null, Property(scrubbedModel, "ModelId"));
                Check.Equal(Hash, Value<string>(scrubbedModel, "RequestSha256"));
                Check.Equal(3.0, Value<double>(scrubbedModel, "LatencyMilliseconds"));
                Check.Equal((int?)400, Value<int?>(scrubbedModel, "HttpStatusCode"));
                NoActionEvidence(scrubbedModel);
            }

            var invalidResponseHash = Validate(request, requestHash: new string('z', 64));
            Status(invalidResponseHash, "Invalid");
            Check.Equal(null, Property(invalidResponseHash, "RequestSha256"));
            Check.Equal("local-model", Value<string>(invalidResponseHash, "ModelId"));
            Check.Equal(12.5, Value<double>(invalidResponseHash, "LatencyMilliseconds"));
            Check.Equal((int?)200, Value<int?>(invalidResponseHash, "HttpStatusCode"));
            NoActionEvidence(invalidResponseHash);
        }
    }
}
