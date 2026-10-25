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
    /// Validates paired ChatML experiment transport and response bindings. This path deliberately
    /// does not apply the production exact-completion-body gate used by record-appraisal.
    /// </summary>
    internal static class AppraisalChatmlExperimentRecorder
    {
        private const string ExperimentId = "npc-appraisal-v1-qwen-chatml-wrapper-v1";
        private const string Prefix = "<|im_start|>user\n";
        private const string Suffix = "<|im_end|>\n<|im_start|>assistant\n";
        private static readonly HashSet<string> AllowedScenarios = new HashSet<string>(StringComparer.Ordinal)
        {
            "native-known-gift", "native-confirmed-help", "native-confirmed-no-show",
            "observed-direct-threat", "observed-quoted-threat", "observed-hypothetical-threat"
        };

        public static int Record(string[] args)
        {
            string preparedPath = Option(args, "--prepared");
            string requestsPath = Option(args, "--variant-requests");
            string manifestPath = Option(args, "--manifest");
            string responsesPath = Option(args, "--responses");
            string outputPath = Option(args, "--output");
            string pinnedModelPath = Option(args, "--model-path");
            string pinnedModelHash = Option(args, "--model-sha256");
            if (preparedPath == null || requestsPath == null || manifestPath == null || responsesPath == null || outputPath == null ||
                pinnedModelPath == null || pinnedModelHash == null || pinnedModelHash.Length != 64 || pinnedModelHash.Any(c => !Uri.IsHexDigit(c)))
                throw new ArgumentException("record-appraisal-chatml-experiment requires source/variant/manifest/response/output paths and pinned model path/hash.");

            string fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "appraisal-live-scenarios-v1.json");
            byte[] fixtureBytes = File.ReadAllBytes(fixturePath);
            string fixtureHash = HashBytes(fixtureBytes);
            IReadOnlyList<AppraisalFixtureCase> cases = AppraisalFixturePreparation.BuildCases(fixturePath);
            if (cases.Count != 32) throw new InvalidDataException("ChatML experiment must use all 32 frozen v1 source cases.");

            List<JsonObject> preparedRows = ReadJsonLines(preparedPath);
            List<JsonObject> variantRows = ReadJsonLines(requestsPath);
            List<JsonObject> manifestRows = ReadJsonLines(manifestPath);
            List<JsonObject> responseRows = ReadJsonLines(responsesPath);
            if (preparedRows.Count != 32 || variantRows.Count != 24 || manifestRows.Count != 24 || responseRows.Count != 24)
                throw new InvalidDataException("ChatML validation requires 32 source rows and exactly 24 paired request, manifest, and response rows.");

            var casesById = cases.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
            var preparedById = UniqueMap(preparedRows, "prepared");
            var variantsById = UniqueMap(variantRows, "variant request");
            var manifestById = UniqueMap(manifestRows, "pair manifest");
            var responsesById = UniqueMap(responseRows, "response");
            if (preparedById.Count != 32 || variantsById.Count != 24 || manifestById.Count != 24 || responsesById.Count != 24)
                throw new InvalidDataException("ChatML rows require unique case IDs.");

            foreach (AppraisalFixtureCase fixtureCase in cases)
            {
                if (!preparedById.TryGetValue(fixtureCase.CaseId, out JsonObject source))
                    throw new InvalidDataException("Missing production source row: " + fixtureCase.CaseId);
                string productionBody = LocalDialogueCompletion.BuildAppraisal(
                    new LocalDialogueConnection("http://127.0.0.1:8080/completion", 300000), fixtureCase.Request).JsonBody;
                if (Text(source, "recordType") != "prepared-production-core-appraisal" ||
                    Text(source, "fixtureId") != "npc-experience-appraisal-live-scenarios-v1" ||
                    Text(source, "fixtureSha256") != fixtureHash ||
                    Text(source, "requestPrompt") != fixtureCase.Request.Prompt ||
                    Text(source, "requestPromptSha256") != fixtureCase.InputSha256 ||
                    Text(source, "completionRequestBody") != productionBody ||
                    Text(source, "completionRequestSha256") != HashText(productionBody))
                    throw new InvalidDataException(fixtureCase.CaseId + ": original prepared bytes differ from current Core.");
            }

            var outputLines = new List<string>(24);
            int accepted = 0;
            foreach (string caseId in casesById.Keys.OrderBy(id => id, StringComparer.Ordinal))
            {
                string scenarioId = caseId.Substring(caseId.LastIndexOf('/') + 1);
                bool shouldRun = AllowedScenarios.Contains(scenarioId);
                if (!shouldRun)
                {
                    if (variantsById.ContainsKey(caseId) || manifestById.ContainsKey(caseId) || responsesById.ContainsKey(caseId))
                        throw new InvalidDataException(caseId + ": cause-unknown v1 case must be excluded from this experiment.");
                    continue;
                }
                if (!variantsById.TryGetValue(caseId, out JsonObject variant) ||
                    !manifestById.TryGetValue(caseId, out JsonObject manifest) ||
                    !responsesById.TryGetValue(caseId, out JsonObject response))
                    throw new InvalidDataException("Missing ChatML pair or response: " + caseId);

                AppraisalFixtureCase fixtureCase = casesById[caseId];
                JsonObject source = preparedById[caseId];
                string originalBody = Text(source, "completionRequestBody");
                string variantBody = Text(variant, "completionRequestBody");
                string originalBodyHash = HashText(originalBody);
                if (Text(variant, "recordType") != "prepared-chatml-appraisal-request-v1" ||
                    Text(variant, "experimentId") != ExperimentId ||
                    Text(variant, "fixtureId") != Text(source, "fixtureId") ||
                    Text(variant, "fixtureSha256") != fixtureHash ||
                    Text(variant, "originalPromptSha256") != fixtureCase.InputSha256 ||
                    Text(variant, "originalBodySha256") != originalBodyHash ||
                    Text(manifest, "recordType") != "appraisal-chatml-paired-manifest-v1" ||
                    Text(manifest, "experimentId") != ExperimentId ||
                    Text(manifest, "originalPromptSha256") != fixtureCase.InputSha256 ||
                    Text(manifest, "originalBodySha256") != originalBodyHash ||
                    Text(manifest, "variantPromptSha256") != Text(variant, "variantPromptSha256") ||
                    Text(manifest, "variantBodySha256") != Text(variant, "variantBodySha256") ||
                    Text(variant, "variantBodySha256") != HashText(variantBody) ||
                    !string.Equals(Text(manifest, "modelPath"), pinnedModelPath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Text(manifest, "modelSha256"), pinnedModelHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(caseId + ": pair manifest hashes do not match prepared request.");

                string expectedPrompt = Prefix + fixtureCase.Request.Prompt + Suffix;
                JsonDocument originalDocument = JsonDocument.Parse(originalBody);
                JsonDocument variantDocument = JsonDocument.Parse(variantBody);
                string actualPrompt = variantDocument.RootElement.GetProperty("prompt").GetString();
                if (actualPrompt != expectedPrompt || HashText(actualPrompt) != Text(variant, "variantPromptSha256"))
                    throw new InvalidDataException(caseId + ": variant prompt is not only the expected ChatML wrapper.");
                JsonObject originalObject = JsonNode.Parse(originalBody) as JsonObject;
                JsonObject variantObject = JsonNode.Parse(variantBody) as JsonObject;
                if (!SameExceptPrompt(originalObject, variantObject) ||
                    actualPrompt.Contains(Text(source["humanReview"] as JsonObject, "label") ?? "\0", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(caseId + ": non-prompt completion fields changed or label leaked into prompt.");
                if (Text(manifest, "modelSha256") == null || Text(manifest, "modelSha256").Length != 64 ||
                    Text(manifest, "modelPath") == null)
                    throw new InvalidDataException(caseId + ": pinned model identity is missing from pair manifest.");

                string expectedBodyBytes = variantBody;
                string rawRequest = Text(response, "rawRequest");
                if (Text(response, "recordType") != "appraisal-chatml-model-response-v1" ||
                    Text(response, "caseId") != caseId ||
                    Text(response, "requestPromptSha256") != fixtureCase.InputSha256 ||
                    Text(response, "originalBodySha256") != originalBodyHash ||
                    Text(response, "variantBodySha256") != Text(variant, "variantBodySha256") ||
                    rawRequest != expectedBodyBytes ||
                    Text(response, "rawRequestSha256") != HashText(expectedBodyBytes))
                    throw new InvalidDataException(caseId + ": response evidence does not match exact variant request bytes.");
                byte[] requestBytes = Convert.FromBase64String(Text(response, "rawRequestBase64"));
                if (!requestBytes.SequenceEqual(Encoding.UTF8.GetBytes(expectedBodyBytes)))
                    throw new InvalidDataException(caseId + ": transmitted ChatML bytes do not match the pair manifest.");

                int? httpStatus = NullableInt(response["httpStatus"]);
                string transportError = Text(response, "transportError");
                string modelPath = Text(response["modelMetadata"] as JsonObject, "model");
                string failureKind = null;
                string responseText = null;
                if (!string.IsNullOrEmpty(transportError)) failureKind = "transport_error";
                else if (httpStatus != 200) failureKind = "http_status_" + (httpStatus.HasValue ? httpStatus.Value.ToString(CultureInfo.InvariantCulture) : "missing");
                else
                {
                    if (!string.Equals(modelPath, Text(manifest, "modelPath"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(caseId + ": completion server model differs from pinned baseline model.");
                    responseText = ReadCompletionContent(response);
                    if (responseText == null) failureKind = "completion_envelope_error";
                }
                NpcExperienceAppraisal appraisal = null;
                bool parsed = failureKind == null && responseText != null &&
                    AppraisalFixturePreparation.TryParseResponse(fixtureCase, responseText, out appraisal);
                if (parsed) accepted++;
                if (!parsed && failureKind == null) failureKind = "core_parser_rejected";
                var result = new JsonObject
                {
                    ["recordType"] = "appraisal-chatml-core-response-check-v1",
                    ["experimentId"] = ExperimentId,
                    ["caseId"] = caseId,
                    ["originalPromptSha256"] = fixtureCase.InputSha256,
                    ["originalBodySha256"] = originalBodyHash,
                    ["variantBodySha256"] = Text(variant, "variantBodySha256"),
                    ["modelPath"] = modelPath,
                    ["modelSha256"] = Text(manifest, "modelSha256"),
                    ["coreResponseValidated"] = parsed,
                    ["failureKind"] = parsed ? null : failureKind,
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
                    });
                }
                else result["appraisal"] = null;
                outputLines.Add(result.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
                originalDocument.Dispose();
                variantDocument.Dispose();
            }
            if (outputLines.Count != 24) throw new InvalidDataException("ChatML Core validator did not process exactly 24 cause-valid cases.");
            WriteNewLines(outputPath, outputLines);
            Console.WriteLine("Core response-only validation checked 24 ChatML outputs: " + accepted.ToString(CultureInfo.InvariantCulture) +
                " accepted, " + (24 - accepted).ToString(CultureInfo.InvariantCulture) + " rejected. No state committed; production body gate was not claimed.");
            return 0;
        }

        private static bool SameExceptPrompt(JsonObject original, JsonObject variant)
        {
            if (original == null || variant == null || original.Count != variant.Count) return false;
            foreach (KeyValuePair<string, JsonNode> pair in original)
            {
                if (pair.Key == "prompt") continue;
                if (!variant.ContainsKey(pair.Key)) return false;
                string left = pair.Value == null ? "null" : pair.Value.ToJsonString();
                string right = variant[pair.Key] == null ? "null" : variant[pair.Key].ToJsonString();
                if (left != right) return false;
            }
            return true;
        }

        private static Dictionary<string, JsonObject> UniqueMap(IEnumerable<JsonObject> rows, string label)
        {
            var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonObject row in rows)
            {
                string id = Text(row, "caseId");
                if (id == null || !result.TryAdd(id, row)) throw new InvalidDataException(label + " case IDs must be present and unique.");
            }
            return result;
        }

        private static string ReadCompletionContent(JsonObject response)
        {
            string rawText = Text(response, "rawResponse");
            string rawBase64 = Text(response, "rawResponseBase64");
            if (rawText == null || rawBase64 == null) return null;
            byte[] rawBytes = Convert.FromBase64String(rawBase64);
            if (rawBytes.Length > 2_000_000 || Encoding.UTF8.GetString(rawBytes) != rawText)
                throw new InvalidDataException("Raw response text differs from bounded response bytes.");
            JsonObject envelope;
            try { envelope = JsonNode.Parse(rawText) as JsonObject; }
            catch (JsonException) { return null; }
            string content = Text(envelope, "content");
            if (content != Text(response, "responseText")) throw new InvalidDataException("Completion content differs from raw server envelope.");
            return content;
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

        private static string HashBytes(byte[] bytes)
        { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant(); }
        private static string HashText(string text) => HashBytes(Encoding.UTF8.GetBytes(text ?? string.Empty));
        private static string Text(JsonObject obj, string key) => obj == null || !obj.TryGetPropertyValue(key, out JsonNode node) || node == null ? null : node.GetValue<string>();
        private static int? NullableInt(JsonNode value) => value == null ? (int?)null : value.GetValue<int>();
        private static string Option(string[] args, string key)
        { for (int i = 1; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1]; return null; }
    }
}
