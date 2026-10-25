using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.NpcFactorEvaluation
{
    /// <summary>
    /// Validates an experiment-only prompt variant. Production record-appraisal keeps its exact-body gate.
    /// Candidate text is never added to the immutable Core request or treated as native evidence.
    /// </summary>
    internal static class SpeechCandidateExperimentRecorder
    {
        private const string ExperimentId = "npc-appraisal-d3-speech-candidate-ab-v1";
        private const string ExpectedPreparedSha256 = "69b9fc70a8f26c998ada698847bd373c44ce4101af55743544cbeb1aecf73c86";
        private const string ExpectedSuiteSha256 = "7a48f04a3ced01f891903d90452c28945683838de633b31d8b20f958a2f9456d";
        private const string ExpectedD3Sha256 = "73a384283e93412906ca3cee1d91dba405d52b2de414f489c8af437d76a60587";
        private const string ExactPrefix = "core-exact:";
        private const string CandidateNotePrefix = "\n\nPrior model suggestion from D3 (fallible model output, not an observation or ground truth): speech-act class = ";
        private const string CandidateNoteSuffix = ". Assess the supplied event yourself; do not mechanically copy this suggestion.";
        private static readonly HashSet<string> Scenarios = new HashSet<string>(StringComparer.Ordinal)
        {
            "observed-direct-threat", "observed-quoted-threat", "observed-hypothetical-threat"
        };
        private static readonly HashSet<string> CandidateLabels = new HashSet<string>(StringComparer.Ordinal)
        {
            "DirectThreat", "QuotedOrReportedThreat", "HypotheticalThreat"
        };
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = false };

        public static int Record(string[] args)
        {
            string preparedPath = Option(args, "--prepared"), suitePath = Option(args, "--suite"),
                d3Path = Option(args, "--d3-results"), variantsPath = Option(args, "--variant-requests"),
                manifestPath = Option(args, "--manifest"), responsesPath = Option(args, "--responses"),
                outputPath = Option(args, "--output"), modelPath = Option(args, "--model-path"),
                modelSha = Option(args, "--model-sha256");
            if (new[] { preparedPath, suitePath, d3Path, variantsPath, manifestPath, responsesPath,
                    outputPath, modelPath, modelSha }.Any(string.IsNullOrWhiteSpace) ||
                modelSha.Length != 64 || modelSha.Any(c => !Uri.IsHexDigit(c)))
                throw new ArgumentException("record-appraisal-speech-candidate requires prepared/suite/D3/request/manifest/response/output paths and pinned model path/hash.");

            string preparedHash = HashFile(preparedPath), suiteHash = HashFile(suitePath), d3Hash = HashFile(d3Path);
            Require(suiteHash == ExpectedSuiteSha256 && d3Hash == ExpectedD3Sha256,
                "Frozen suite or D3 evidence hash changed; create a new paired suite before running.");

            string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "appraisal-live-scenarios-v2.json");
            string fixtureHash = HashFile(fixturePath);
            IReadOnlyList<AppraisalFixtureCase> cases = AppraisalFixturePreparation.BuildCases(fixturePath);
            Require(cases.Count == 32, "Speech-candidate experiment requires the full v2 production-Core fixture.");
            Dictionary<string, AppraisalFixtureCase> casesById = cases.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
            Dictionary<string, JsonObject> preparedById = UniqueMap(ReadJsonLines(preparedPath), "prepared", "caseId");
            Require(preparedById.Count == 32, "Prepared v2 rows must cover all 32 Core cases.");

            foreach (AppraisalFixtureCase fixtureCase in cases)
            {
                Require(preparedById.TryGetValue(fixtureCase.CaseId, out JsonObject source),
                    "Missing prepared Core case: " + fixtureCase.CaseId);
                string body = LocalDialogueCompletion.BuildAppraisal(
                    new LocalDialogueConnection("http://127.0.0.1:8080/completion", 300000), fixtureCase.Request).JsonBody;
                Require(Text(source, "recordType") == "prepared-production-core-appraisal" &&
                    Text(source, "fixtureId") == "npc-experience-appraisal-live-scenarios-v2" &&
                    Text(source, "fixtureSha256") == fixtureHash &&
                    Text(source, "requestPrompt") == fixtureCase.Request.Prompt &&
                    Text(source, "requestPromptSha256") == fixtureCase.InputSha256 &&
                    Text(source, "completionRequestBody") == body &&
                    Text(source, "completionRequestSha256") == HashText(body),
                    fixtureCase.CaseId + ": prepared source differs from current Core v2 bytes.");
            }

            Dictionary<string, JsonObject> suiteById = ReadSuite(suitePath);
            Dictionary<string, JsonObject> d3ById = ReadCanonicalD3(d3Path, suiteById);
            string[] exactIds = suiteById.Keys.ToArray();
            Require(exactIds.Length == 12 && exactIds.All(id => d3ById.ContainsKey(id)),
                "Frozen suite and canonical D3 result must contain the same 12 exact speech cases.");
            Require(d3ById.Keys.All(id => exactIds.Contains(id, StringComparer.Ordinal)),
                "D3 evidence contains an extra exact speech candidate.");

            List<JsonObject> variantRows = ReadJsonLines(variantsPath);
            List<JsonObject> manifests = ReadJsonLines(manifestPath);
            List<JsonObject> responses = ReadJsonLines(responsesPath);
            Require(variantRows.Count == 24 && manifests.Count == 12 && responses.Count == 24,
                "Speech-candidate validation requires 12 pairs and 24 response rows.");
            Dictionary<string, JsonObject> variantsByKey = UniqueCompositeMap(variantRows, "variant request");
            Dictionary<string, JsonObject> manifestById = UniqueMap(manifests, "manifest", "caseId");
            Dictionary<string, JsonObject> responsesByKey = UniqueCompositeMap(responses, "response");
            Require(variantsByKey.Count == 24 && responsesByKey.Count == 24 && manifestById.Count == 12,
                "Pair, variant, and response identifiers must be unique.");

            var output = new List<string>(24);
            var seenPairIndices = new HashSet<int>();
            foreach (string caseId in exactIds)
            {
                Require(casesById.TryGetValue(caseId, out AppraisalFixtureCase fixtureCase),
                    "Exact speech case is not part of the current production-Core v2 fixture: " + caseId);
                JsonObject source = preparedById[caseId];
                JsonObject suiteCase = suiteById[caseId];
                JsonObject d3 = d3ById[caseId];
                string scenarioId = caseId.Substring(caseId.LastIndexOf('/') + 1);
                JsonObject suiteInput = suiteCase["input"] as JsonObject;
                Require(Scenarios.Contains(scenarioId) &&
                    Text(suiteInput?["npc"] as JsonObject, "id") == caseId.Substring(0, caseId.IndexOf('/')),
                    "Suite profile or scenario differs from prepared Core case: " + caseId);
                string utterance = Text(suiteInput, "playerText");
                Require(!string.IsNullOrWhiteSpace(utterance) && fixtureCase.Request.Prompt.Contains(utterance, StringComparison.Ordinal),
                    "Suite utterance does not appear in the exact Core request: " + caseId);
                string label = Text(d3, "choice");
                string note = CandidateNotePrefix + label + CandidateNoteSuffix;
                string baseText = Text(source, "completionRequestBody");
                string baseHash = HashText(baseText);
                JsonObject baseBody = JsonNode.Parse(baseText) as JsonObject;
                Require(baseBody != null && Text(baseBody, "prompt") == fixtureCase.Request.Prompt,
                    "Base appraisal body differs from current Core prompt: " + caseId);

                Require(manifestById.TryGetValue(caseId, out JsonObject manifest), "Missing pair manifest: " + caseId);
                int pairIndex = NullableInt(manifest["pairIndex"]) ?? -1;
                string[] expectedOrder = pairIndex % 2 == 0 ? new[] { "A", "B" } : new[] { "B", "A" };
                JsonArray armOrder = manifest["armOrder"] as JsonArray;
                Require(armOrder != null && armOrder.Select(item => item?.GetValue<string>()).SequenceEqual(expectedOrder),
                    "Pair arm order is not the declared counterbalance: " + caseId);
                Require(Text(manifest, "recordType") == "speech-candidate-pair-manifest-v1" &&
                    Text(manifest, "experimentId") == ExperimentId && Text(manifest, "candidateLabel") == label &&
                    Text(manifest, "candidateSourceCaseId") == ExactPrefix + caseId &&
                    Text(manifest, "candidateSourceArm") == "original" && Text(manifest, "candidateSourceModel") == "d3" &&
                    Text(manifest, "candidateSourceSha256") == CandidateSourceDigest(d3) &&
                    pairIndex >= 0 && pairIndex < 12 && seenPairIndices.Add(pairIndex) &&
                    Text(manifest, "legacyPreparedSha256") == ExpectedPreparedSha256 &&
                    Text(manifest, "sourcePreparedSha256") == preparedHash && Text(manifest, "sourceSuiteSha256") == suiteHash &&
                    Text(manifest, "sourceD3ResultsSha256") == d3Hash &&
                    IsSha256(Text(manifest, "semanticProjectionSha256")) &&
                    Text(manifest, "sourcePromptSha256") == fixtureCase.InputSha256 &&
                    Text(manifest, "baseBodySha256") == baseHash && Text(manifest, "candidateNote") == note &&
                    Text(manifest, "grammarSha256") == HashText(Text(baseBody, "grammar")) &&
                    NullableInt(manifest["nPredict"]) == 1024 && NumberIsZero(manifest["temperature"]) &&
                    StopSettingsMatch(manifest["stop"]) &&
                    Text(manifest, "modelPath") == modelPath &&
                    string.Equals(Text(manifest, "modelSha256"), modelSha.ToLowerInvariant(), StringComparison.Ordinal),
                    "Pair manifest does not match current Core and D3 evidence: " + caseId);

                JsonObject candidateBody = (JsonObject)baseBody.DeepClone();
                candidateBody["prompt"] = fixtureCase.Request.Prompt + note;
                string candidatePrompt = Text(candidateBody, "prompt");
                JsonObject aBody = JsonNode.Parse(baseText) as JsonObject;
                string expectedCandidateHash = Text(manifest, "candidateBodySha256");
                foreach (string arm in new[] { "A", "B" })
                {
                    string key = caseId + "|" + arm;
                    Require(variantsByKey.TryGetValue(key, out JsonObject variant), "Missing variant request: " + key);
                    string bodyText = Text(variant, "completionRequestBody");
                    string bodyHash = HashText(bodyText);
                    Require(Text(variant, "recordType") == "prepared-speech-candidate-appraisal-request-v1" &&
                        Text(variant, "experimentId") == ExperimentId && Text(variant, "caseId") == caseId &&
                        Text(variant, "arm") == arm && Text(variant, "candidateLabel") == label &&
                        Text(variant, "productionBodySha256") == baseHash &&
                        Text(variant, "candidateBodySha256") == expectedCandidateHash &&
                        Text(variant, "completionRequestSha256") == bodyHash,
                        "Variant metadata/hash mismatch: " + key);
                    JsonObject actualBody = JsonNode.Parse(bodyText) as JsonObject;
                    if (arm == "A")
                    {
                        Require(bodyText == baseText && Text(actualBody, "prompt") == fixtureCase.Request.Prompt,
                            "A arm must match exact production Core completion bytes: " + caseId);
                    }
                    else
                    {
                        Require(Text(actualBody, "prompt") == candidatePrompt &&
                            SameExceptPrompt(aBody, actualBody) &&
                            Text(manifest, "candidateBodySha256") == bodyHash &&
                            Text(actualBody, "grammar") == Text(aBody, "grammar"),
                            "B arm must change prompt only by the fixed advisory note: " + caseId);
                    }

                    Require(responsesByKey.TryGetValue(key, out JsonObject response), "Missing response row: " + key);
                    string rawRequest = Text(response, "rawRequest"), rawRequestHash = Text(response, "rawRequestSha256");
                    byte[] requestBytes;
                    try { requestBytes = Convert.FromBase64String(Text(response, "rawRequestBase64")); }
                    catch (FormatException) { throw new InvalidDataException(key + ": request base64 is malformed."); }
                    Require(Text(response, "recordType") == "speech-candidate-appraisal-model-response-v1" &&
                        Text(response, "experimentId") == ExperimentId && Text(response, "caseId") == caseId &&
                        Text(response, "arm") == arm && Text(response, "candidateLabel") == label &&
                        rawRequest == bodyText && rawRequestHash == bodyHash &&
                        requestBytes.SequenceEqual(Encoding.UTF8.GetBytes(bodyText)) &&
                        Text(response, "productionBodySha256") == baseHash &&
                        Text(response, "candidateBodySha256") == expectedCandidateHash &&
                        Text(response, "modelPath") == modelPath &&
                        string.Equals(Text(response, "modelSha256"), modelSha.ToLowerInvariant(), StringComparison.Ordinal),
                        key + ": response does not bind to exact A/B request bytes.");

                    int? status = NullableInt(response["httpStatus"]);
                    string transportError = Text(response, "transportError");
                    string rawResponse = Text(response, "rawResponse"), rawResponseBase64 = Text(response, "rawResponseBase64");
                    string responseText = null;
                    string failure = status != 200 ? "http_error" :
                        !string.IsNullOrEmpty(transportError) ? "transport_error" : null;
                    if (failure == null)
                    {
                        byte[] responseBytes;
                        try { responseBytes = Convert.FromBase64String(rawResponseBase64); }
                        catch (Exception error) when (error is FormatException || error is ArgumentNullException) { responseBytes = Array.Empty<byte>(); }
                        if (rawResponse == null || responseBytes.Length == 0 || responseBytes.Length > 2_000_000 ||
                            Encoding.UTF8.GetString(responseBytes) != rawResponse)
                            failure = "response_bytes_mismatch";
                        else
                        {
                            try
                            {
                                JsonObject envelope = JsonNode.Parse(rawResponse) as JsonObject;
                                responseText = Text(envelope, "content");
                                if (responseText != Text(response, "responseText")) failure = "completion_envelope_mismatch";
                                if (Text(response["modelMetadata"] as JsonObject, "model") != modelPath)
                                    throw new InvalidDataException(key + ": response model differs from pinned model path.");
                            }
                            catch (JsonException) { failure = "completion_envelope_invalid"; }
                            catch (InvalidOperationException) { failure = "completion_envelope_invalid"; }
                        }
                    }

                    NpcExperienceAppraisal appraisal = null;
                    bool parsed = failure == null && responseText != null &&
                        AppraisalFixturePreparation.TryParseResponse(fixtureCase, responseText, out appraisal);
                    if (!parsed && failure == null) failure = "core_parser_rejected";
                    var result = new JsonObject
                    {
                        ["recordType"] = "speech-candidate-appraisal-parser-result-v1",
                        ["experimentId"] = ExperimentId,
                        ["caseId"] = caseId,
                        ["arm"] = arm,
                        ["candidateLabel"] = label,
                        ["productionRequestExact"] = arm == "A" && bodyText == baseText,
                        ["coreParserValidated"] = parsed,
                        ["candidateAgreement"] = parsed ? (bool?)CandidateAgrees(label, appraisal.SpeechActKind) : null,
                        ["failureKind"] = parsed ? null : failure,
                        ["nativeCommitAvailable"] = false
                    };
                    if (parsed)
                    {
                        result["appraisal"] = JsonSerializer.SerializeToNode(new
                        {
                            appraisal.MoodDelta, appraisal.AffinityDelta, appraisal.TrustDelta,
                            appraisal.ResentmentDelta, appraisal.FearDelta,
                            speechActKind = appraisal.SpeechActKind.ToString(),
                            appraisal.SpeechTargetPersonId
                        }, JsonOptions);
                    }
                    else result["appraisal"] = null;
                    output.Add(result.ToJsonString(JsonOptions));
                }
            }
            Require(output.Count == 24, "Parser recorder must produce exactly 24 arm results.");
            Require(seenPairIndices.Count == 12, "Pair manifest must contain each counterbalanced index exactly once.");
            WriteNewLines(outputPath, output);
            int accepted = output.Count(line => (JsonNode.Parse(line) as JsonObject)?["coreParserValidated"]?.GetValue<bool>() == true);
            int exact = output.Count(line => (JsonNode.Parse(line) as JsonObject)?["productionRequestExact"]?.GetValue<bool>() == true);
            int agreement = output.Count(line => (JsonNode.Parse(line) as JsonObject)?["candidateAgreement"]?.GetValue<bool>() == true);
            int disagreement = output.Count(line => (JsonNode.Parse(line) as JsonObject)?["candidateAgreement"]?.GetValue<bool>() == false);
            Console.WriteLine("Speech-candidate experiment response parser checked 24 outputs: " +
                accepted.ToString(CultureInfo.InvariantCulture) + " parsed, " +
                (24 - accepted).ToString(CultureInfo.InvariantCulture) + " rejected; " +
                exact.ToString(CultureInfo.InvariantCulture) + " exact production request bodies; " +
                agreement.ToString(CultureInfo.InvariantCulture) + " candidate agreements, " +
                disagreement.ToString(CultureInfo.InvariantCulture) + " disagreements. Agreement is not semantic correctness. No native state committed.");
            return 0;
        }

        private static Dictionary<string, JsonObject> ReadSuite(string path)
        {
            JsonObject suite = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            Require(Text(suite, "suiteId") == "npc-speech-act-choice-diagnostic-v1" &&
                NullableInt(suite["schemaVersion"]) == 1 &&
                (Text(suite, "provenance") ?? string.Empty).Contains(ExpectedPreparedSha256, StringComparison.Ordinal),
                "Unexpected speech suite identity or source-preparation hash.");
            var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            JsonArray cases = suite["cases"] as JsonArray;
            if (cases == null) throw new InvalidDataException("Speech suite cases are missing.");
            foreach (JsonObject item in cases.OfType<JsonObject>().Where(row => Text(row, "id")?.StartsWith(ExactPrefix, StringComparison.Ordinal) == true))
            {
                string id = Text(item, "id").Substring(ExactPrefix.Length);
                string scenario = id.Substring(id.LastIndexOf('/') + 1);
                Require(Scenarios.Contains(scenario) && result.TryAdd(id, item), "Duplicate or unsupported exact speech suite case: " + id);
            }
            Require(result.Count == 12, "Speech suite must contain exactly 12 exact Core cases.");
            Require(result.Keys.GroupBy(id => id.Substring(0, id.IndexOf('/')), StringComparer.Ordinal).Count() == 4 &&
                result.Keys.GroupBy(id => id.Substring(0, id.IndexOf('/')), StringComparer.Ordinal).All(group => group.Count() == 3),
                "Speech suite must contain three exact speech cases for each of four profiles.");
            return result;
        }

        private static Dictionary<string, JsonObject> ReadCanonicalD3(string path, Dictionary<string, JsonObject> suite)
        {
            var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonObject row in ReadJsonLines(path))
            {
                if (Text(row, "model") != "d3" || Text(row, "arm") != "original") continue;
                string suiteId = Text(row, "id");
                if (suiteId == null || !suiteId.StartsWith(ExactPrefix, StringComparison.Ordinal)) continue;
                string id = suiteId.Substring(ExactPrefix.Length);
                Require(suite.ContainsKey(id) && result.TryAdd(id, row), "Unexpected or duplicate canonical D3 row: " + suiteId);
                Require(NullableInt(row["status"]) == 200 && string.IsNullOrEmpty(Text(row, "technicalError")) &&
                    row["unofferedChoice"]?.GetValue<bool>() == false && CandidateLabels.Contains(Text(row, "choice")),
                    "Canonical D3 row is not a valid offered speech candidate: " + suiteId);
                string choice = Text(row, "choice");
                JsonArray offered = row["options"] as JsonArray;
                Require(offered != null && offered.OfType<JsonObject>().Any(item => Text(item, "id") == choice),
                    "Canonical D3 speech candidate was not offered: " + suiteId);
            }
            return result;
        }

        private static bool SameExceptPrompt(JsonObject left, JsonObject right)
        {
            if (left == null || right == null || left.Count != right.Count) return false;
            foreach (KeyValuePair<string, JsonNode> pair in left)
            {
                if (pair.Key == "prompt") continue;
                if (!right.ContainsKey(pair.Key) ||
                    (pair.Value == null ? "null" : pair.Value.ToJsonString()) !=
                    (right[pair.Key] == null ? "null" : right[pair.Key].ToJsonString())) return false;
            }
            return true;
        }

        private static string CandidateSourceDigest(JsonObject row)
        {
            JsonArray options = row["options"] as JsonArray;
            string ids = string.Join("\u001f", (options ?? new JsonArray()).OfType<JsonObject>().Select(item => Text(item, "id") ?? string.Empty));
            string fields = string.Join("\n", new[]
            {
                Text(row, "id") ?? string.Empty, Text(row, "model") ?? string.Empty,
                Text(row, "arm") ?? string.Empty, Text(row, "choice") ?? string.Empty,
                NullableInt(row["status"])?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Text(row, "technicalError") ?? string.Empty,
                (row["unofferedChoice"]?.GetValue<bool>() ?? false).ToString().ToLowerInvariant(), ids
            });
            return HashText(fields);
        }

        private static bool NumberIsZero(JsonNode value)
        { return value != null && decimal.TryParse(value.ToJsonString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number) && number == 0m; }

        private static bool StopSettingsMatch(JsonNode value)
        {
            JsonArray stop = value as JsonArray;
            return stop != null && stop.Count == 2 &&
                stop[0]?.GetValue<string>() == "<|im_end|>" && stop[1]?.GetValue<string>() == "<|im_start|>";
        }

        private static bool CandidateAgrees(string candidate, NpcExperienceSpeechActKind parsed)
        {
            switch (candidate)
            {
                case "DirectThreat": return parsed == NpcExperienceSpeechActKind.DirectThreat;
                case "QuotedOrReportedThreat":
                    return parsed == NpcExperienceSpeechActKind.QuotedThreat || parsed == NpcExperienceSpeechActKind.ReportedThreat;
                case "HypotheticalThreat": return parsed == NpcExperienceSpeechActKind.HypotheticalThreat;
                default: throw new InvalidDataException("Unsupported D3 speech candidate: " + candidate);
            }
        }

        private static Dictionary<string, JsonObject> UniqueMap(IEnumerable<JsonObject> rows, string label, string key)
        {
            var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonObject row in rows)
            {
                string value = Text(row, key);
                if (value == null || !result.TryAdd(value, row)) throw new InvalidDataException(label + " identifiers must be present and unique.");
            }
            return result;
        }

        private static Dictionary<string, JsonObject> UniqueCompositeMap(IEnumerable<JsonObject> rows, string label)
        {
            var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonObject row in rows)
            {
                string id = Text(row, "caseId"), arm = Text(row, "arm"), key = id + "|" + arm;
                if (id == null || arm == null || !result.TryAdd(key, row)) throw new InvalidDataException(label + " composite identifiers must be present and unique.");
            }
            return result;
        }

        private static List<JsonObject> ReadJsonLines(string path)
        {
            var result = new List<JsonObject>();
            int lineNumber = 0;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonObject row = JsonNode.Parse(line) as JsonObject;
                if (row == null) throw new InvalidDataException("JSONL row " + lineNumber.ToString(CultureInfo.InvariantCulture) + " must be an object.");
                result.Add(row);
            }
            return result;
        }

        private static void WriteNewLines(string path, IEnumerable<string> lines)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                foreach (string line in lines) writer.WriteLine(line);
        }

        private static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));
        private static string HashText(string value) => HashBytes(Encoding.UTF8.GetBytes(value ?? string.Empty));
        private static bool IsSha256(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
        private static string HashBytes(byte[] bytes)
        { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant(); }
        private static string Text(JsonObject value, string key) => value == null || !value.TryGetPropertyValue(key, out JsonNode node) || node == null ? null : node.GetValue<string>();
        private static int? NullableInt(JsonNode value) => value == null ? (int?)null : value.GetValue<int>();
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        private static string Option(string[] args, string name)
        { for (int i = 1; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1]; return null; }
    }
}
