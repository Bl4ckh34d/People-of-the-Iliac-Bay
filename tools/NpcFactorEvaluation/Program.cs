using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.NpcFactorEvaluation
{
    internal static class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = false };
        private const string AnchorId = "s:206728581:h:131074:r:0";
        private const int AppraisalContextTokens = 4096;
        private const int AppraisalResponseTokens = 1024;
        private const int AppraisalPromptTokenBudget = AppraisalContextTokens - AppraisalResponseTokens;
        private const string ChoiceInstructions = "Select one supplied conversational option using only the NPC's actor-known facts, stable personality, current mood, relationship evidence and the player's request. Preserve uncertainty. Affinity is not reliability. A proposal is not an executed effect. Do not invent facts or effects.";

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "self-test") return SelfTest();
                if (args.Length >= 1 && args[0] == "selected-session") return SelectedFrameworkSession.Run(args);
                if (args.Length == 1 && (args[0] == "help" || args[0] == "--help")) { Help(); return 0; }
                if (args.Length >= 1 && args[0] == "prepare") return Prepare(args);
                if (args.Length >= 1 && args[0] == "prepare-appraisal") return PrepareAppraisal(args);
                if (args.Length >= 1 && args[0] == "validate-appraisal-prepared") return ValidatePreparedAppraisal(args);
                if (args.Length >= 1 && args[0] == "record-appraisal") return RecordAppraisal(args);
                if (args.Length >= 1 && args[0] == "record-appraisal-chatml-experiment") return AppraisalChatmlExperimentRecorder.Record(args);
                if (args.Length >= 1 && args[0] == "record-appraisal-speech-candidate") return SpeechCandidateExperimentRecorder.Record(args);
                if (args.Length >= 1 && args[0] == "compose-qwen") return ComposeQwen(args);
                if (args.Length >= 1 && args[0] == "continue-social") return ContinueSocial(args);
                if (args.Length >= 1 && args[0] == "continue-social-appraise-reopen") return ContinueSocialAppraiseReopen(args);
                if (args.Length >= 1 && args[0] == "record") return Record(args);
                Help(); return 2;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(args.Length == 1 && args[0] == "self-test" ? e.ToString() : e.Message);
                return 1;
            }
        }

        private static void Help()
        {
            Console.WriteLine("Offline checks: dotnet run --project tools/NpcFactorEvaluation -- self-test");
            Console.WriteLine("Prepare requests: dotnet run --project tools/NpcFactorEvaluation -- prepare --corpus v2 --output work/npc-factor-evaluation/run/requests.jsonl");
            Console.WriteLine("Prepare v2 typed snapshots: dotnet run --project tools/NpcFactorEvaluation -- prepare --corpus multi-turn-v2 --output work/npc-factor-evaluation/run/multi-turn-v2.jsonl --oracle-output work/npc-factor-evaluation/run/multi-turn-v2.oracle.jsonl");
            Console.WriteLine("Prepare production-Core appraisal prompts (v1 default, v2 cause-corrected, v3 context pairs): dotnet run --project tools/NpcFactorEvaluation -- prepare-appraisal --fixture-version v3 --output work/npc-factor-evaluation/run/appraisal-v3-prepared.jsonl");
            Console.WriteLine("Core-validate prepared appraisal bodies: dotnet run --no-build --project tools/NpcFactorEvaluation -- validate-appraisal-prepared --fixture-version v3 --prepared <prepared.jsonl>");
            Console.WriteLine("Core-validate appraisal responses: dotnet run --no-build --project tools/NpcFactorEvaluation -- record-appraisal --fixture-version v3 --prepared <prepared.jsonl> --responses <raw-results.jsonl> --output <validated.jsonl>");
            Console.WriteLine("Record ChatML framing experiment responses with Core parser only: dotnet run --no-build --project tools/NpcFactorEvaluation -- record-appraisal-chatml-experiment --prepared <v1-prepared.jsonl> --variant-requests <chatml-requests.jsonl> --manifest <paired-manifest.jsonl> --responses <raw-results.jsonl> --output <validated.jsonl> --model-path <pinned-model.gguf> --model-sha256 <sha256>");
            Console.WriteLine("Record D3 speech-candidate A/B responses with Core parser only: dotnet run --no-build --project tools/NpcFactorEvaluation -- record-appraisal-speech-candidate --prepared <v2-prepared.jsonl> --suite <speech-suite.json> --d3-results <speech-d3-results.jsonl> --variant-requests <variant-requests.jsonl> --manifest <pair-manifest.jsonl> --responses <raw-results.jsonl> --output <core-validation.jsonl> --model-path <pinned-model.gguf> --model-sha256 <sha256>");
            Console.WriteLine("Build Qwen stage from D3 results: dotnet run --no-build --project tools/NpcFactorEvaluation -- compose-qwen --prepared <requests.jsonl> --d3-results <d3.jsonl> --output <plans.jsonl>");
            Console.WriteLine("Offline continuation preflight; live requires --execute and exact model paths: dotnet run --no-build --project tools/NpcFactorEvaluation -- continue-social --episode-id interaction.trust-help-and-gratitude --arm-id trust-help.verified --output <new.jsonl> --expect-d3-model <path> --expect-qwen-model <path> [--execute]");
            Console.WriteLine("Opt-in 3-turn -> Core speech appraisal drain -> reopened turn: dotnet run --no-build --project tools/NpcFactorEvaluation -- continue-social-appraise-reopen --extra-player-text <observer-directed utterance> --reopen-player-text <text> --output <new.jsonl> --expect-d3-model <path> --expect-qwen-model <path> [--execute]");
            Console.WriteLine("Record serial responses: dotnet run --project tools/NpcFactorEvaluation -- record --prepared work/npc-factor-evaluation/run/requests.jsonl --responses work/npc-factor-evaluation/run/responses.jsonl --output work/npc-factor-evaluation/run/review.jsonl");
            Console.WriteLine("Offline commands make no network calls. continue-social contacts loopback servers only with --execute. A response row needs armId, rawRequest, rawResponse, selectedOptionId, and modelMetadata.");
        }

        private static int SelfTest()
        {
            Require(SelectedContinuityCapture.RunSelfTests(out string continuityDiagnostic), continuityDiagnostic);
            JsonObject v1 = ReadCorpus("paired-scenarios-v1.json");
            JsonObject v2 = ReadCorpus("paired-scenarios-v2.json");
            JsonObject acceptanceV1 = ReadFixture("appraisal-acceptance-v1.json");
            JsonObject acceptanceV2 = ReadFixture("appraisal-acceptance-v2.json");
            JsonObject multiTurn = ReadFixture("multi-turn-interactions-v2.json");
            CheckClosedLoopContinuation(multiTurn);
            IReadOnlyList<AppraisalFixtureCase> appraisalCases = AppraisalFixturePreparation.BuildCases(
                FixturePath("appraisal-live-scenarios-v1.json"));
            CheckCorpus(v1, 29, 58, null);
            CheckCorpus(v2, 35, 70, v1);
            CheckAppraisalAcceptanceScenarios(acceptanceV1, acceptanceV2);
            CheckMultiTurnFixture(multiTurn);
            Require(appraisalCases.Count == 32 && appraisalCases.All(item => item != null && item.Request != null &&
                item.Request.Prompt.Length > 0 && item.InputSha256 == HashText(item.Request.Prompt)),
                "Appraisal preparation must expose 32 exact production-Core prompts with matching SHA-256 values.");
            IReadOnlyList<AppraisalFixtureCase> repeatedAppraisalCases = AppraisalFixturePreparation.BuildCases(
                FixturePath("appraisal-live-scenarios-v1.json"));
            IReadOnlyList<AppraisalFixtureCase> causeCorrectedAppraisalCases = AppraisalFixturePreparation.BuildCases(
                FixturePath("appraisal-live-scenarios-v2.json"));
            Require(causeCorrectedAppraisalCases.Count == 32,
                "Cause-corrected appraisal v2 must preserve the complete 32-case profile/scenario design.");
            foreach (AppraisalFixtureCase item in causeCorrectedAppraisalCases)
            {
                string scenarioId = item.CaseId.Substring(item.CaseId.LastIndexOf('/') + 1);
                Require(item.InputSha256 == HashText(item.Request.Prompt) &&
                    !item.Request.Prompt.Contains(scenarioId, StringComparison.OrdinalIgnoreCase) &&
                    !item.Request.Prompt.Contains(item.Label, StringComparison.OrdinalIgnoreCase),
                    "V2 review labels and scenario IDs must remain outside exact production-Core prompt bytes.");
            }
            foreach (AppraisalFixtureCase item in causeCorrectedAppraisalCases.Where(item =>
                item.CaseId.EndsWith("/native-injury-health-episode", StringComparison.Ordinal) ||
                item.CaseId.EndsWith("/native-status-onset-unknown-cause", StringComparison.Ordinal)))
            {
                Require(item.Request.RelationshipTargetId == null &&
                    item.Request.Prompt.Contains("responsibleActorAttribution=Unknown,responsibleActor=unknown") &&
                    !item.Request.Prompt.Contains("responsibleActor=player"),
                    "Cause-unknown v2 injury/status evidence must not identify the player or create a directed relationship target.");
            }
            string appraisalV3ManifestPath = FixturePath("appraisal-live-scenarios-v3.json");
            IReadOnlyList<AppraisalFixtureCase> appraisalV3Cases = AppraisalContextFixturePreparation.BuildCorpusCases(appraisalV3ManifestPath);
            Require(appraisalV3Cases.Count == 38 && HashFile(appraisalV3ManifestPath) ==
                AppraisalContextFixturePreparation.GetCorpusManifestSha256(appraisalV3ManifestPath),
                "Pinned v3 appraisal corpus must combine exactly 38 cases under the exact manifest hash.");
            Require(appraisalV3Cases.Take(32).Select(item => item.CaseId).SequenceEqual(causeCorrectedAppraisalCases.Select(item => item.CaseId)) &&
                appraisalV3Cases.Take(32).Select(item => item.Request.Prompt).SequenceEqual(causeCorrectedAppraisalCases.Select(item => item.Request.Prompt)),
                "V3 must preserve the exact frozen v2 case order and prompt bytes before its six additive context cases.");
            CheckAppraisalContextPairs(appraisalV3Cases);
            Require(repeatedAppraisalCases.Count == appraisalCases.Count, "Appraisal fixture expansion must remain stable.");
            for (int appraisalIndex = 0; appraisalIndex < appraisalCases.Count; appraisalIndex++)
            {
                AppraisalFixtureCase item = appraisalCases[appraisalIndex];
                string scenarioId = item.CaseId.Substring(item.CaseId.LastIndexOf('/') + 1);
                Require(repeatedAppraisalCases[appraisalIndex].CaseId == item.CaseId &&
                    repeatedAppraisalCases[appraisalIndex].InputSha256 == item.InputSha256,
                    "Appraisal fixture IDs and exact Core prompts must be deterministic.");
                LocalDialogueConnection appraisalConnection = new LocalDialogueConnection("http://127.0.0.1:8080/completion", 300000);
                DialogueCompletion appraisalCompletion = LocalDialogueCompletion.BuildAppraisal(appraisalConnection, item.Request);
                Require(item.Request.Prompt.IndexOf(scenarioId, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.Request.Prompt.IndexOf(item.Label, StringComparison.OrdinalIgnoreCase) < 0 &&
                    appraisalCompletion.JsonBody.IndexOf(scenarioId, StringComparison.OrdinalIgnoreCase) < 0 &&
                    appraisalCompletion.JsonBody.IndexOf(item.Label, StringComparison.OrdinalIgnoreCase) < 0,
                    "Human review labels and scenario identifiers must not leak into appraisal prompt metadata.");
                JsonObject completionBody = JsonNode.Parse(appraisalCompletion.JsonBody) as JsonObject;
                Require(completionBody != null && Text(completionBody, "prompt") == item.Request.Prompt &&
                    !string.IsNullOrEmpty(Text(completionBody, "grammar")) && appraisalCompletion.ResponseTokens == 1024,
                    "Appraisal completion must use the exact Core prompt and bound response grammar.");
            }
            string id = v2["nativeFixtureAnchor"]?["personId"]?.GetValue<string>();
            Require(id == AnchorId, "The frozen corpus anchor must match the existing MoodEvaluation native identity.");
            string provenance = v2["nativeFixtureAnchor"]?["identityProvenance"]?.GetValue<string>();
            Require(provenance != null && provenance.Contains("MoodEvaluation fixture", StringComparison.Ordinal), "Identity provenance must state its native-probe source and synthetic limits.");
            Require(Pairs(v2).All(pair => pair["commonInput"]?["npc"]?["id"]?.GetValue<string>() == AnchorId), "Every scenario must use the sourced native-person identity anchor.");
            foreach (JsonObject pair in Pairs(v2).Where(item => item["id"]?.GetValue<string>().StartsWith("extensionv2.noble-tone", StringComparison.Ordinal) == true ||
                item["id"]?.GetValue<string>().StartsWith("extensionv2.authority-guard", StringComparison.Ordinal) == true))
            {
                string npcId = pair["commonInput"]?["npc"]?["id"]?.GetValue<string>();
                Require(npcId == AnchorId, "Noble contrast must use the anchored native-person identity.");
                JsonArray factList = pair["commonInput"]?["world"]?["nativeFacts"] as JsonArray;
                Require(factList != null && factList.Any(f => f?["kind"]?.GetValue<string>() == "speaker_social_rank" &&
                    f["source"]?.GetValue<string>() == "SyntheticAuthoredQuestPersona" && f["personId"]?.GetValue<string>() == AnchorId),
                    "Noble rank must be explicit authored identity evidence, not inferred from wealth, clothing or eloquence.");
            }

            int projected = 0;
            foreach (JsonObject pair in Pairs(v2))
            {
                JsonObject common = pair["commonInput"] as JsonObject;
                JsonArray arms = pair["arms"] as JsonArray;
                foreach (JsonObject arm in arms)
                {
                    JsonObject input = Merge(common, arm["contextPatch"] as JsonObject);
                    BuiltRequests built = BuildRequests(pair, arm, input);
                    JsonObject d3Wire = built.D3Payload as JsonObject;
                    Require(d3Wire != null, "D3 payload must come from the production LocalDecisionWire builder.");
                    JsonObject d3State = JsonNode.Parse(Text(d3Wire, "state")) as JsonObject;
                    Require(d3State != null && Text(d3State, "protocolVersion") == NpcDecisionRequest.CurrentProtocolVersion &&
                        Text(d3State, "actorId") == AnchorId && Text(d3State, "originalTurn") == built.CoreDecision.OriginalTurn,
                        "D3 wire state must preserve current Core decision contract fields.");
                    byte[] d3Bytes = Encoding.UTF8.GetBytes(built.D3RequestBody ?? string.Empty);
                    string d3Hash;
                    using (SHA256 sha = SHA256.Create())
                        d3Hash = BitConverter.ToString(sha.ComputeHash(d3Bytes)).Replace("-", string.Empty).ToLowerInvariant();
                    Require(string.Equals(d3Hash, built.DecisionHash, StringComparison.Ordinal),
                        "Prepared D3 request hash must match exact production wire bytes.");
                    string d3Response = OfflineDecisionResponse(built.CoreDecision);
                    LocalDecisionParsedReply parsedD3 = LocalDecisionWire.Parse(d3Response);
                    Require(parsedD3.IsValid, "Production D3 response parser must accept a valid offline choice fixture.");
                    NpcDecisionResult validatedD3 = NpcDecisionResult.ValidateResponse(built.CoreDecision,
                        parsedD3.SelectedOptionId, parsedD3.Probabilities, parsedD3.ReportedConfidence,
                        built.DecisionHash, parsedD3.ModelId, 1, 200, built.CoreDecision.Binding,
                        built.CoreDecision.SnapshotRevision, built.CoreDecision.CandidateSetRevision,
                        built.CoreDecision.Options);
                    Require(validatedD3.Status == NpcDecisionStatus.Ok,
                        "Prepared D3 choice must pass current Core response and freshness validation.");
                    Require(built.CoreDecision.ProtocolVersion == NpcDecisionRequest.CurrentProtocolVersion, "Decision projection must use current production protocol.");
                    Require(built.CoreDecision.Context.Length <= 16384 && built.CoreDecision.OriginalTurn.Length <= 8192, "Production decision projection exceeded Core bounds.");
                    Require(built.CoreDecision.Options.Count >= 1 && built.CoreDecision.Options.Count <= 32, "Production decision options exceeded Core bounds.");
                    Require(!built.CoreDecision.Context.Contains("nativeFacts", StringComparison.Ordinal), "Offline oracle nativeFacts leaked into D3 model context.");
                    Require(!built.QwenCompletion.Prompt.Contains("nativeFacts", StringComparison.Ordinal), "Offline oracle nativeFacts leaked into voice prompt.");
                    Require(built.QwenCompletion.ResponseTokens <= 1536, "Voice request exceeded production response-token bound.");
                    projected++;
                }
            }
            Require(projected == 70, "The v2 self-test must construct both production requests for every arm.");
            int multiProjected = 0;
            var multiRequestIds = new HashSet<Guid>();
            foreach (JsonObject episode in multiTurn["episodes"].AsArray())
            foreach (JsonObject arm in episode["arms"].AsArray())
            foreach (JsonObject turn in arm["turns"].AsArray())
            {
                JsonObject input = (JsonObject)turn["input"];
                BuiltRequests built = BuildMultiTurnRequests(episode, arm, turn, input);
                AssertModelHistoryProjection(built.QwenCompletion, input["history"]);
                DialogueCompletion conditioned = BuildMultiTurnQwenCompletion(built, input,
                    Text(input["options"]?[0] as JsonObject, "id"));
                AssertModelHistoryProjection(conditioned, input["history"]);
                Require(!conditioned.Prompt.Contains("oracle", StringComparison.OrdinalIgnoreCase) &&
                    !conditioned.Prompt.Contains("nativeEvents", StringComparison.Ordinal),
                    "Multi-turn evaluator oracle leaked into Qwen prompt.");
                Require(!built.CoreDecision.Context.Contains("oracle", StringComparison.OrdinalIgnoreCase) &&
                    !built.CoreDecision.Context.Contains("nativeEvents", StringComparison.Ordinal),
                    "Multi-turn evaluator oracle leaked into D3 context.");
                Require(conditioned.Prompt.Contains("Known personality:", StringComparison.Ordinal),
                    "Multi-turn voice prompt must use current Core's full personality description.");
                Require(conditioned.Prompt.Contains("D3 conversational preference candidate:", StringComparison.Ordinal) &&
                    conditioned.Prompt.Contains("not a native willingness result", StringComparison.Ordinal),
                    "Qwen stage must receive the validated typed choice only as a non-effectful preference.");
                Require(built.D3RequestBody != null && built.DecisionHash != null,
                    "Multi-turn request must use production LocalDecisionWire bytes and hash.");
                Require(multiRequestIds.Add(built.CoreDecision.RequestId), "Every turn must have its own deterministic Core decision request ID.");
                string response = OfflineDecisionResponse(built.CoreDecision);
                LocalDecisionParsedReply parsed = LocalDecisionWire.Parse(response);
                Require(parsed.IsValid, "Current production D3 parser must accept offline multi-turn response fixture.");
                NpcDecisionResult validated = NpcDecisionResult.ValidateResponse(built.CoreDecision, parsed.SelectedOptionId,
                    parsed.Probabilities, parsed.ReportedConfidence, built.DecisionHash, parsed.ModelId, 1, 200,
                    built.CoreDecision.Binding, built.CoreDecision.SnapshotRevision, built.CoreDecision.CandidateSetRevision,
                    built.CoreDecision.Options);
                Require(validated.Status == NpcDecisionStatus.Ok, "Multi-turn D3 answer must pass current Core binding/freshness validation.");
                multiProjected++;
            }
            Require(multiProjected == 20 && multiRequestIds.Count == 20, "Multi-turn self-test must construct all 20 frozen turns with distinct request IDs.");
            Console.WriteLine("SELF-TEST PASS: frozen contrast corpora, fixed-snapshot replay, Core-owned social continuation, appraisal acceptance and cause-corrected prompt bindings, production Core/D3 parser/ledger invariants; fake transport only, no inference performed.");
            return 0;
        }

        private static void CheckClosedLoopContinuation(JsonObject fixture)
        {
            JsonObject episode = fixture["episodes"].AsArray().OfType<JsonObject>()
                .Single(item => Text(item, "id") == "interaction.trust-help-and-gratitude");
            JsonObject arm = episode["arms"].AsArray().OfType<JsonObject>()
                .Single(item => Text(item, "id") == "trust-help.verified");
            JsonArray authoredTurns = arm["turns"] as JsonArray;
            Require(authoredTurns != null && authoredTurns.Count == 2,
                "The closed-loop fixture probe needs two authored social turns.");
            var turns = authoredTurns.OfType<JsonObject>().Select(turn => new SocialContinuationTurn(
                Text(turn, "turnId"), turn["input"] as JsonObject, "fixture-authored")).ToList();
            JsonObject seedNpc = turns[0].Input["npc"] as JsonObject;
            string expectedProfileMaterial = seedNpc["personality"].ToJsonString(JsonOptions) + "|0|" + Text(seedNpc, "id");
            Require(InitialProfileHashMaterial(turns[0].Input) == expectedProfileMaterial,
                "Closed-loop header profile hash must canonicalize numeric personalitySeed from the frozen fixture.");
            JsonObject probeInput = (JsonObject)turns[1].Input.DeepClone();
            probeInput["playerText"] = "How do you feel about our conversation now?";
            turns.Add(new SocialContinuationTurn("probe-follow-up", probeInput, "self-test-only"));

            var qwenReplies = new Queue<Tuple<string, int>>(new[]
            {
                Tuple.Create("I appreciate that you asked first.", 3),
                Tuple.Create("I feel a little more at ease.", -1),
                Tuple.Create("I am glad we talked it through.", 2)
            });
            var calls = new List<Tuple<string, string>>();
            SocialContinuationTransport fake = (endpoint, body, cancellationToken, appraisalRequest) =>
            {
                string request = new UTF8Encoding(false, true).GetString(body);
                calls.Add(Tuple.Create(endpoint.AbsolutePath, request));
                if (endpoint.AbsolutePath == "/v1/systemone")
                {
                    JsonObject wire = JsonNode.Parse(request) as JsonObject;
                    JsonObject state = JsonNode.Parse(Text(wire, "state")) as JsonObject;
                    string context = Text(state, "context");
                    Require(!context.Contains("oracle", StringComparison.OrdinalIgnoreCase) &&
                        !context.Contains("nativeEvents", StringComparison.Ordinal),
                        "Closed-loop D3 request must not expose evaluator oracle fields.");
                    JsonObject criteria = wire["questions"]?["decision"]?["criteria"] as JsonObject;
                    string selected = criteria?.FirstOrDefault().Key;
                    var probabilities = new JsonObject();
                    for (int index = 0; index < criteria.Count; index++)
                        probabilities[criteria.ElementAt(index).Key] = index == 0 ? 1 : 0;
                    string response = new JsonObject
                    {
                        ["model"] = "d3-test",
                        ["answers"] = new JsonObject
                        {
                            ["decision"] = new JsonObject
                            {
                                ["type"] = "choice", ["choice"] = selected, ["probabilities"] = probabilities
                            }
                        }
                    }.ToJsonString(JsonOptions);
                    return Task.FromResult(new SocialContinuationHttpResult(200,
                        Encoding.UTF8.GetBytes(response), 1, null));
                }

                Require(endpoint.AbsolutePath == "/completion", "Closed-loop runner called unexpected endpoint.");
                RequireStructuredMoodQwenRequest(body, "closed-loop fake transport");
                string reply = JsonSerializer.Serialize(new { reply = qwenReplies.Peek().Item1,
                    moodDelta = qwenReplies.Dequeue().Item2 }, JsonOptions);
                string envelope = new JsonObject { ["model"] = "qwen-test", ["content"] = reply }.ToJsonString(JsonOptions);
                return Task.FromResult(new SocialContinuationHttpResult(200, Encoding.UTF8.GetBytes(envelope), 1, null));
            };
            SocialContinuationReport report = RunSocialContinuationAsync(episode, arm, turns, fake,
                new Uri("http://127.0.0.1:8093/v1/systemone"), new Uri("http://127.0.0.1:8080/completion"),
                "d3-test", "qwen-test", _ => { }).GetAwaiter().GetResult();
            Require(report.CompletedTurns == 3 && report.Records.Count == 3 && calls.Count == 6 &&
                report.FinalMood.AcceptedTurnSequence == 3 && report.FinalMood.Value == 4,
                "Three accepted social turns must commit once each through Core state.");
            var speechSources = report.Store.Snapshot().NpcObservedSpeechSources;
            Require(speechSources.Count == 1 && speechSources[0].PersonId == report.PersonId &&
                speechSources[0].LastIssuedOrdinal == 3 && speechSources[0].LastAcceptedOrdinal == 3 &&
                speechSources[0].LastProjectedOrdinal == 3 && speechSources[0].Pending == null,
                "Three accepted social turns must carry three source-issued speech receipts.");
            JsonObject secondQwenBody = JsonNode.Parse(calls[3].Item2) as JsonObject;
            string secondQwenPrompt = Text(secondQwenBody, "prompt", string.Empty);
            bool secondD3Ordered = calls[2].Item1 == "/v1/systemone";
            bool secondQwenOrdered = calls[3].Item1 == "/completion";
            bool priorReplyProjected = secondQwenPrompt.Contains(
                "<|im_start|>assistant\nI appreciate that you asked first.<|im_end|>", StringComparison.Ordinal);
            bool committedMoodProjected = secondQwenPrompt.Contains("Persistent conversation mood: 3/100", StringComparison.Ordinal);
            string continuationDiagnostic = "second D3 endpoint=" + calls[2].Item1 +
                "; second Qwen endpoint=" + calls[3].Item1 +
                "; expected assistant history present=" + priorReplyProjected +
                "; expected committed mood present=" + committedMoodProjected +
                "; mood after turn 1=" + report.Records[0]["moodAfter"]?.ToJsonString(JsonOptions);
            Require(secondD3Ordered && secondQwenOrdered && priorReplyProjected && committedMoodProjected,
                "Second-turn requests must use first committed reply and mood from fresh Core state. " + continuationDiagnostic);

            int malformedCalls = 0;
            SocialContinuationTransport malformed = (endpoint, body, cancellationToken, appraisalRequest) =>
            {
                malformedCalls++;
                string response;
                if (endpoint.AbsolutePath == "/v1/systemone")
                {
                    JsonObject wire = JsonNode.Parse(Encoding.UTF8.GetString(body)) as JsonObject;
                    JsonObject criteria = wire["questions"]?["decision"]?["criteria"] as JsonObject;
                    string selected = criteria.First().Key;
                    var probabilities = new JsonObject();
                    for (int index = 0; index < criteria.Count; index++)
                        probabilities[criteria.ElementAt(index).Key] = index == 0 ? 1 : 0;
                    response = new JsonObject { ["model"] = "d3-test", ["answers"] = new JsonObject
                    {
                        ["decision"] = new JsonObject { ["type"] = "choice", ["choice"] = selected,
                            ["probabilities"] = probabilities }
                    }}.ToJsonString(JsonOptions);
                }
                else
                {
                    RequireStructuredMoodQwenRequest(body, "malformed-continuation fake transport");
                    response = "{\"model\":\"qwen-test\",\"content\":\"{\\\"reply\\\":\\\"unfinished\\\"}\"}";
                }
                return Task.FromResult(new SocialContinuationHttpResult(200, Encoding.UTF8.GetBytes(response), 1, null));
            };
            SocialContinuationReport stopped = RunSocialContinuationAsync(episode, arm, turns, malformed,
                new Uri("http://127.0.0.1:8093/v1/systemone"), new Uri("http://127.0.0.1:8080/completion"),
                "d3-test", "qwen-test", _ => { }).GetAwaiter().GetResult();
            Require(stopped.CompletedTurns == 0 && stopped.Records.Count == 1 && malformedCalls == 2 &&
                stopped.FinalMood.AcceptedTurnSequence == 0 && stopped.FinalMood.Value == 0,
                "Malformed Qwen output must halt without commit, history append, or later-turn calls.");

            CheckMalformedContinuationEnvelope(episode, arm, turns, "d3_numeric_model",
                "{\"model\":7,\"answers\":{}}", null, "d3_model_identity_mismatch", 1, "d3");
            CheckMalformedContinuationEnvelope(episode, arm, turns, "qwen_object_model", null,
                "{\"model\":{\"id\":\"qwen-test\"},\"content\":\"{\\\"reply\\\":\\\"Hello.\\\",\\\"moodDelta\\\":0}\"}",
                "qwen_model_identity_mismatch", 2, "qwen");
            CheckMalformedContinuationEnvelope(episode, arm, turns, "qwen_numeric_content", null,
                "{\"model\":\"qwen-test\",\"content\":17}", "qwen_completion_envelope_invalid", 2, "qwen");
            CheckMalformedContinuationEnvelope(episode, arm, turns, "qwen_object_content", null,
                "{\"model\":\"qwen-test\",\"content\":{\"reply\":\"Hello.\"}}",
                "qwen_completion_envelope_invalid", 2, "qwen");

            CheckInitialBaselineSeed(fixture, "interaction.appointment-and-private-cause", "appointment.player-absent-cause-unknown");
            CheckInitialBaselineSeed(fixture, "interaction.threat-quotation-and-repeated-abuse", "speech.direct-threat-repeated");
            CheckInitialBaselineSeed(fixture, "interaction.poison-discovery-and-attribution", "poison.discovered-source-unknown");
            CheckPostChatAppraisalReopen(fixture);
        }

        private static void CheckPostChatAppraisalReopen(JsonObject fixture)
        {
            JsonObject episode = fixture["episodes"].AsArray().OfType<JsonObject>()
                .Single(item => Text(item, "id") == "interaction.threat-quotation-and-repeated-abuse");
            JsonObject arm = episode["arms"].AsArray().OfType<JsonObject>()
                .Single(item => Text(item, "id") == "speech.direct-threat-repeated");
            var socialTurns = arm["turns"].AsArray().OfType<JsonObject>().Select(item =>
                new SocialContinuationTurn(Text(item, "turnId"), item["input"] as JsonObject, "fixture-authored")).ToList();
            JsonObject third = (JsonObject)socialTurns[1].Input.DeepClone();
            third["playerText"] = "I will hurt you if you refuse me.";
            socialTurns.Add(new SocialContinuationTurn("test-third-threat", third, "self-test-only"));
            string observer = Text(socialTurns[0].Input["npc"] as JsonObject, "id");
            var appraisalBodies = new List<string>();
            int appraisalCalls = 0, appraisalTokenizeCalls = 0, socialCalls = 0;

            SocialContinuationReport RunFakeSocial(CancellationToken token = default(CancellationToken))
            {
                socialCalls = 0;
                SocialContinuationTransport fake = (endpoint, body, ct, appraisalRequest) =>
                {
                    socialCalls++;
                    if (endpoint.AbsolutePath == "/v1/systemone")
                        return Task.FromResult(new SocialContinuationHttpResult(200,
                            Encoding.UTF8.GetBytes(FakeSocialDecisionResponse(body)), 1, null));
                    Require(appraisalRequest == null, "Social stages must not be mislabeled as appraisal transport.");
                    RequireStructuredMoodQwenRequest(body, "post-chat fake social transport");
                    string envelope = new JsonObject { ["model"] = "qwen-test",
                        ["content"] = "{\"reply\":\"I hear you.\",\"moodDelta\":0}" }.ToJsonString(JsonOptions);
                    return Task.FromResult(new SocialContinuationHttpResult(200, Encoding.UTF8.GetBytes(envelope), 1, null));
                };
                return RunSocialContinuationAsync(episode, arm, socialTurns, fake,
                    new Uri("http://127.0.0.1:8093/v1/systemone"), new Uri("http://127.0.0.1:8080/completion"),
                    "d3-test", "qwen-test", _ => { }, token, seedDispositionBaseline: true,
                    staggerReceiptTimes: true).GetAwaiter().GetResult();
            }

            SocialContinuationReport social = RunFakeSocial();
            Require(social.CompletedTurns == 3 && social.Store != null && social.FinalGameTime > 4000,
                "Appraisal/reopen self-test must start with three accepted turns in one Core-owned store.");
            int dispositionBefore = social.Store.TryGetActualConversationDisposition(observer, out int initialDisposition)
                ? initialDisposition : int.MaxValue;
            Require(dispositionBefore == socialTurns[0].Input["npc"]["disposition"].GetValue<int>(),
                "Synthetic native disposition baseline must be first EffectiveDisposition with offset zero.");
            socialCalls = 0;
            appraisalCalls = 0;

            SocialContinuationTransport appraisalFake = (endpoint, body, ct, appraisalRequest) =>
            {
                if (IsSpeechStageBody(body)) return FakeSpeechStageResponse(appraisalCalls == 2 ? "DirectThreat" : "Other");
                if (appraisalRequest != null && endpoint.AbsolutePath == "/tokenize")
                {
                    appraisalTokenizeCalls++;
                    string tokenizeText = new UTF8Encoding(false, true).GetString(body);
                    JsonObject tokenizeRequest = JsonNode.Parse(tokenizeText) as JsonObject;
                    Require(Text(tokenizeRequest, "content") == appraisalRequest.Prompt &&
                        tokenizeRequest?["add_special"]?.GetValue<bool>() == true,
                        "Each appraisal must tokenize the exact Core prompt with production add_special=true request shape.");
                    string tokenizeResponse = FakeTokenizerResponse(3072);
                    return Task.FromResult(new SocialContinuationHttpResult(200,
                        Encoding.UTF8.GetBytes(tokenizeResponse), 1, null));
                }
                if (appraisalRequest != null)
                {
                    appraisalCalls++;
                    string requestText = new UTF8Encoding(false, true).GetString(body);
                    JsonObject completion = JsonNode.Parse(requestText) as JsonObject;
                    Require(Text(completion, "prompt") == appraisalRequest.Prompt &&
                        Text(completion, "grammar") == NpcExperienceAppraisalContract.ResponseGrammar(appraisalRequest) &&
                        appraisalRequest.AlreadyAppliedEffectsMask == NpcExperienceAppliedEffectsMask.Mood &&
                        appraisalRequest.Prompt.IndexOf("oracle", StringComparison.OrdinalIgnoreCase) < 0,
                        "Appraisal stage must send exact Core completion grammar with the mood-already-applied mask and no oracle input.");
                    appraisalBodies.Add(requestText);
                    string response = FakeAppraisalResponse(appraisalRequest, appraisalCalls == 3);
                    string envelope = new JsonObject { ["model"] = "qwen-test", ["content"] = response }.ToJsonString(JsonOptions);
                    return Task.FromResult(new SocialContinuationHttpResult(200, Encoding.UTF8.GetBytes(envelope), 1, null));
                }
                if (endpoint.AbsolutePath == "/v1/systemone")
                {
                    socialCalls++;
                    return Task.FromResult(new SocialContinuationHttpResult(200, Encoding.UTF8.GetBytes(FakeSocialDecisionResponse(body)), 1, null));
                }
                socialCalls++;
                RequireStructuredMoodQwenRequest(body, "post-appraisal reopen fake transport");
                string reply = new JsonObject { ["reply"] = "We can speak again.", ["moodDelta"] = 0 }.ToJsonString(JsonOptions);
                string socialEnvelope = new JsonObject { ["model"] = "qwen-test", ["content"] = reply }.ToJsonString(JsonOptions);
                return Task.FromResult(new SocialContinuationHttpResult(200, Encoding.UTF8.GetBytes(socialEnvelope), 1, null));
            };
            var successRows = new List<JsonObject>();
            PostChatRunResult success = RunPostChatAppraisalAndReopenAsync(episode, arm, socialTurns, social,
                "Can we talk again?", appraisalFake,
                new Uri("http://127.0.0.1:8093/v1/systemone"), new Uri("http://127.0.0.1:8080/completion"),
                "d3-test", "qwen-test", row => successRows.Add((JsonObject)row.DeepClone()), CancellationToken.None).GetAwaiter().GetResult();
            JsonObject reopenStart = successRows.SingleOrDefault(row => Text(row, "recordType") == "reopened-social-phase-start");
            int finalDisposition = reopenStart?["effectiveDispositionFromCore"]?.GetValue<int>() ?? int.MaxValue;
            WorldState successfulState = social.Store.Snapshot();
            bool allAppraisalRowsCommitted = successRows.OfType<JsonObject>().Count(row =>
                Text(row, "recordType") == "post-chat-speech-appraisal" && Bool(row, "coreCommitAccepted") == true) == 3;
            int pendingAfterSuccess = successfulState.NpcExperienceInbox.ReadPending(observer, 100).Count;
            JsonObject[] appraisalJournalRows = successRows.Where(row => Text(row, "recordType") == "post-chat-speech-appraisal").ToArray();
            bool allTokenizeRecordsFit = appraisalJournalRows.Length == 3 && appraisalJournalRows.All(row =>
            {
                JsonObject tokenize = row["appraisalTokenize"] as JsonObject;
                string rawRequest = Text(tokenize, "rawRequest");
                string rawResponse = Text(tokenize, "rawResponse");
                JsonObject requestObject = TryObject(rawRequest);
                return Text(tokenize, "failureKind") == null &&
                    Text(requestObject, "content") == Text(row["appraisalRequest"] as JsonObject, "prompt") &&
                    requestObject?["add_special"]?.GetValue<bool>() == true &&
                    Text(tokenize, "requestBodySha256") == HashText(rawRequest) &&
                    Text(tokenize, "responseBodySha256") == HashText(rawResponse) &&
                        NullableInt(tokenize["tokenCount"]) == 3072 && NullableInt(tokenize["inputBudget"]) == 3072 &&
                    Bool(tokenize, "withinContextBudget") == true;
            });
            JsonObject failedRecord = successRows.LastOrDefault(row =>
                Text(row, "failureKind") != null || Bool(row, "coreCommitAccepted") == false);
            var successDiagnostic = new JsonObject
            {
                ["stopReason"] = success.StopReason,
                ["appraisalsCommitted"] = success.AppraisalsCommitted,
                ["reopenCompleted"] = success.ReopenCompleted,
                ["appraisalCalls"] = appraisalCalls,
                ["appraisalTokenizeCalls"] = appraisalTokenizeCalls,
                ["appraisalBodies"] = appraisalBodies.Count,
                ["socialCalls"] = socialCalls,
                ["dispositionBefore"] = dispositionBefore,
                ["reopenedDisposition"] = finalDisposition,
                ["finalDispositionFromStore"] = social.Store.TryGetActualConversationDisposition(observer, out int dispositionAfter)
                    ? dispositionAfter : (int?)null,
                ["pendingAfterSuccess"] = pendingAfterSuccess,
                ["allThreeCommitRowsAccepted"] = allAppraisalRowsCommitted,
                ["conditions"] = new JsonObject
                {
                    ["completed"] = success.StopReason == "completed",
                    ["threeCommitted"] = success.AppraisalsCommitted == 3,
                    ["reopenCompleted"] = success.ReopenCompleted,
                    ["threeAppraisalTokenizeAndCompletionTransports"] = appraisalTokenizeCalls == 3 && appraisalCalls == 3 && appraisalBodies.Count == 3,
                    ["threeExactTokenizeRecordsFit"] = allTokenizeRecordsFit,
                    ["twoReopenSocialRequests"] = socialCalls == 2,
                    ["dispositionDecreasedExactlyOne"] = finalDisposition == dispositionBefore - 1,
                    ["oneReopenedReceiptPending"] = pendingAfterSuccess == 1,
                    ["allThreeRowsReportCommit"] = allAppraisalRowsCommitted
                },
                ["failedRecord"] = failedRecord?.DeepClone(),
                ["rows"] = new JsonArray(successRows.Select(row => row.DeepClone()).ToArray())
            };
            Require(success.StopReason == "completed" && success.AppraisalsCommitted == 3 && success.ReopenCompleted &&
                appraisalTokenizeCalls == 3 && appraisalCalls == 3 && appraisalBodies.Count == 3 && socialCalls == 2 &&
                allTokenizeRecordsFit &&
                finalDisposition == dispositionBefore - 1 &&
                pendingAfterSuccess == 1 && allAppraisalRowsCommitted,
                "Three exact Core appraisals must mutate disposition through the same store before one fresh reopen; reopened turn remains pending. Actual: " +
                successDiagnostic.ToJsonString(JsonOptions));
            JsonObject lastAppraisal = successRows.Last(row => Text(row, "recordType") == "post-chat-speech-appraisal");
            Require(lastAppraisal["appraisalRequest"]?["alreadyAppliedEffectsMask"]?.GetValue<string>() == "Mood" &&
                lastAppraisal["proposal"]?["moodDelta"]?.GetValue<int>() == 0 &&
                Text(lastAppraisal, "stateBeforeSha256") != Text(lastAppraisal, "stateAfterSha256"),
                "The threat appraisal must preserve mood CAS rules and show its committed state mutation.");
            JsonObject reopenModelInput = reopenStart?["modelInput"] as JsonObject;
            Require(reopenModelInput?["npc"]?["disposition"]?.GetValue<int>() == finalDisposition &&
                (reopenModelInput?["history"] as JsonArray)?.Count == 6 &&
                reopenModelInput?["world"]?["npcKnowledge"]?.ToJsonString(JsonOptions) ==
                    socialTurns[0].Input["world"]?["npcKnowledge"]?.ToJsonString(JsonOptions) &&
                reopenModelInput?["world"]?["nativeFacts"]?.ToJsonString(JsonOptions) ==
                    socialTurns[0].Input["world"]?["nativeFacts"]?.ToJsonString(JsonOptions),
                "Reopened request must refresh committed state while retaining only first-snapshot knowledge and world facts.");

            AssertTokenizeFailure(FakeTokenizerResponse(3073), "appraisal_tokenize_over_budget");
            AssertTokenizeFailure("{\"tokens\":\"not-an-array\"}", "appraisal_tokenize_response_invalid");

            void AssertTokenizeFailure(string tokenizeResponse, string expectedStopReason)
            {
                SocialContinuationReport tokenSocial = RunFakeSocial();
                int tokenizeCalls = 0, completionCalls = 0, socialTransportCalls = 0;
                var tokenRows = new List<JsonObject>();
                SocialContinuationTransport tokenFailureFake = (endpoint, body, ct, appraisalRequest) =>
                {
                    if (IsSpeechStageBody(body)) return FakeSpeechStageResponse("Other");
                    if (appraisalRequest != null && endpoint.AbsolutePath == "/tokenize")
                    {
                        tokenizeCalls++;
                        return Task.FromResult(new SocialContinuationHttpResult(200,
                            Encoding.UTF8.GetBytes(tokenizeResponse), 1, null));
                    }
                    if (appraisalRequest != null)
                    {
                        completionCalls++;
                        throw new InvalidOperationException("Appraisal completion must not be sent after tokenizer failure.");
                    }
                    socialTransportCalls++;
                    return Task.FromResult(new SocialContinuationHttpResult(200,
                        Encoding.UTF8.GetBytes(endpoint.AbsolutePath == "/v1/systemone" ? FakeSocialDecisionResponse(body) :
                            new JsonObject { ["model"] = "qwen-test", ["content"] = "{}" }.ToJsonString(JsonOptions)), 1, null));
                };
                PostChatRunResult tokenFailure = RunPostChatAppraisalAndReopenAsync(episode, arm, socialTurns, tokenSocial,
                    "Can we talk again?", tokenFailureFake,
                    new Uri("http://127.0.0.1:8093/v1/systemone"), new Uri("http://127.0.0.1:8080/completion"),
                    "d3-test", "qwen-test", row => tokenRows.Add((JsonObject)row.DeepClone()), CancellationToken.None)
                    .GetAwaiter().GetResult();
                JsonObject tokenFailureRow = tokenRows.Single(row => Text(row, "recordType") == "post-chat-speech-appraisal");
                JsonObject tokenEvidence = tokenFailureRow["appraisalTokenize"] as JsonObject;
                Require(tokenFailure.StopReason == expectedStopReason && tokenFailure.AppraisalsCommitted == 0 &&
                    !tokenFailure.ReopenCompleted && tokenizeCalls == 1 && completionCalls == 0 && socialTransportCalls == 0 &&
                    tokenSocial.Store.Snapshot().NpcExperienceInbox.ReadPending(observer, 100).Count == 3 &&
                    Text(tokenEvidence, "failureKind") == expectedStopReason &&
                    Text(tokenEvidence, "requestBodySha256") == HashText(Text(tokenEvidence, "rawRequest")) &&
                    Text(tokenEvidence, "responseBodySha256") == HashText(Text(tokenEvidence, "rawResponse")) &&
                    Text(tokenFailureRow, "stateBeforeSha256") == Text(tokenFailureRow, "stateAfterSha256"),
                    "Tokenizer failure must preserve exact evidence, keep all speech pending, and prevent completion/reopen: " +
                    new JsonObject { ["stopReason"] = tokenFailure.StopReason, ["tokenizeCalls"] = tokenizeCalls,
                        ["completionCalls"] = completionCalls, ["socialTransportCalls"] = socialTransportCalls,
                        ["tokenEvidence"] = tokenEvidence?.DeepClone(), ["failureRow"] = tokenFailureRow.DeepClone() }
                        .ToJsonString(JsonOptions));
            }

            SocialContinuationReport failedSocial = RunFakeSocial();
            int failedAppraisalCalls = 0, failedSocialCalls = 0;
            SocialContinuationTransport failThirdAppraisal = (endpoint, body, ct, appraisalRequest) =>
            {
                if (IsSpeechStageBody(body)) return FakeSpeechStageResponse("Other");
                if (appraisalRequest != null && endpoint.AbsolutePath == "/tokenize")
                    return Task.FromResult(new SocialContinuationHttpResult(200,
                        Encoding.UTF8.GetBytes(FakeTokenizerResponse(48)), 1, null));
                if (appraisalRequest != null)
                {
                    failedAppraisalCalls++;
                    string content = failedAppraisalCalls == 3 ? "{}" : FakeAppraisalResponse(appraisalRequest, false);
                    string envelope = new JsonObject { ["model"] = "qwen-test", ["content"] = content }.ToJsonString(JsonOptions);
                    return Task.FromResult(new SocialContinuationHttpResult(200, Encoding.UTF8.GetBytes(envelope), 1, null));
                }
                failedSocialCalls++;
                return Task.FromResult(new SocialContinuationHttpResult(200,
                    Encoding.UTF8.GetBytes(endpoint.AbsolutePath == "/v1/systemone" ? FakeSocialDecisionResponse(body) :
                        new JsonObject { ["model"] = "qwen-test", ["content"] = "{\"reply\":\"Hello.\",\"moodDelta\":0}" }.ToJsonString(JsonOptions)), 1, null));
            };
            var failedRows = new List<JsonObject>();
            PostChatRunResult failed = RunPostChatAppraisalAndReopenAsync(episode, arm, socialTurns, failedSocial,
                "Can we talk again?", failThirdAppraisal,
                new Uri("http://127.0.0.1:8093/v1/systemone"), new Uri("http://127.0.0.1:8080/completion"),
                "d3-test", "qwen-test", row => failedRows.Add((JsonObject)row.DeepClone()), CancellationToken.None).GetAwaiter().GetResult();
            JsonObject failedLast = failedRows.Last(row => Text(row, "recordType") == "post-chat-speech-appraisal");
            Require(failed.StopReason == "core_appraisal_parser_rejected" && failed.AppraisalsCommitted == 2 &&
                !failed.ReopenCompleted && failedAppraisalCalls == 3 && failedSocialCalls == 0 &&
                Text(failedLast, "stateBeforeSha256") == Text(failedLast, "stateAfterSha256") &&
                failedSocial.Store.Snapshot().NpcExperienceInbox.ReadPending(observer, 100).Count == 1,
                "A malformed third appraisal must be journaled once, leave its event pending, and halt before reopen.");

            SocialContinuationReport cancelledSocial = RunFakeSocial();
            using (var cts = new CancellationTokenSource())
            {
                int cancelTokenizeCalls = 0, cancelAppraisalCalls = 0, cancelReopenCalls = 0;
                SocialContinuationTransport cancelTransport = (endpoint, body, ct, appraisalRequest) =>
                {
                    if (IsSpeechStageBody(body)) return FakeSpeechStageResponse("Other");
                    if (appraisalRequest != null && endpoint.AbsolutePath == "/tokenize")
                    {
                        cancelTokenizeCalls++;
                        return Task.FromResult(new SocialContinuationHttpResult(200,
                            Encoding.UTF8.GetBytes(FakeTokenizerResponse(48)), 1, null));
                    }
                    if (appraisalRequest != null)
                    {
                        cancelAppraisalCalls++;
                        cts.Cancel();
                        string envelope = new JsonObject { ["model"] = "qwen-test",
                            ["content"] = FakeAppraisalResponse(appraisalRequest, false) }.ToJsonString(JsonOptions);
                        return Task.FromResult(new SocialContinuationHttpResult(200, Encoding.UTF8.GetBytes(envelope), 1, null));
                    }
                    cancelReopenCalls++;
                    return Task.FromResult(new SocialContinuationHttpResult(200,
                        Encoding.UTF8.GetBytes(endpoint.AbsolutePath == "/v1/systemone" ? FakeSocialDecisionResponse(body) :
                            new JsonObject { ["model"] = "qwen-test", ["content"] = "{\"reply\":\"Hello.\",\"moodDelta\":0}" }.ToJsonString(JsonOptions)), 1, null));
                };
                var cancelRows = new List<JsonObject>();
                PostChatRunResult cancelled = RunPostChatAppraisalAndReopenAsync(episode, arm, socialTurns, cancelledSocial,
                    "Can we talk again?", cancelTransport,
                    new Uri("http://127.0.0.1:8093/v1/systemone"), new Uri("http://127.0.0.1:8080/completion"),
                    "d3-test", "qwen-test", row => cancelRows.Add((JsonObject)row.DeepClone()), cts.Token).GetAwaiter().GetResult();
                JsonObject cancelRow = cancelRows.Last(row => Text(row, "recordType") == "post-chat-speech-appraisal");
                Require(cancelled.StopReason == "cancelled_after_transport" && cancelled.AppraisalsCommitted == 0 &&
                    cancelTokenizeCalls == 1 && cancelAppraisalCalls == 1 && cancelReopenCalls == 0 &&
                    Text(cancelRow, "stateBeforeSha256") == Text(cancelRow, "stateAfterSha256") &&
                    cancelledSocial.Store.Snapshot().NpcExperienceInbox.ReadPending(observer, 100).Count == 3,
                    "Cancellation after transport must journal raw response but prevent parsing/commit/reopen.");
            }
        }

        private static void CheckInitialBaselineSeed(JsonObject fixture, string episodeId, string armId)
        {
            JsonObject episode = fixture["episodes"].AsArray().OfType<JsonObject>().Single(item => Text(item, "id") == episodeId);
            JsonObject arm = episode["arms"].AsArray().OfType<JsonObject>().Single(item => Text(item, "id") == armId);
            JsonArray authoredTurns = arm["turns"] as JsonArray;
            IReadOnlyList<SocialContinuationTurn> turns = authoredTurns.OfType<JsonObject>().Select(item =>
                new SocialContinuationTurn(Text(item, "turnId"), item["input"] as JsonObject, "fixture-authored")).ToArray();
            ValidateSocialContinuationInputs(turns);
            JsonObject initial = turns[0].Input;
            JsonObject npc = initial["npc"] as JsonObject;
            JsonObject sourceRelationship = npc["relationship"] as JsonObject;
            string personId = Text(npc, "id");
            WorldStateStore store = CreateSocialContinuationStore(initial, personId);
            NpcDirectedRelationshipView seeded = store.ReadNpcRelationship(personId, NpcDirectedRelationship.PlayerTargetId);
            JsonObject modelInput = BuildContinuationInput(initial, turns[0], store, personId,
                Double(initial["world"] as JsonObject, "gameTimeSeconds", 0));
            ConversationMoodView seededMood = store.CaptureConversationMood(personId,
                Double(initial["world"] as JsonObject, "gameTimeSeconds", 0));
            string expectedKnowledge = (initial["world"]?["npcKnowledge"] as JsonArray ?? new JsonArray()).ToJsonString(JsonOptions);
            BuiltRequests built = BuildMultiTurnRequests(episode, arm, new JsonObject { ["turnId"] = turns[0].TurnId },
                modelInput, "closed-loop-seed-self-test/" + armId, "closed-loop-seed-self-test", 0, 0);
            JsonObject firstOption = (modelInput["options"] as JsonArray)?[0] as JsonObject;
            string selectedOption = Text(firstOption, "id");
            DialogueCompletion qwen = BuildMultiTurnQwenCompletion(built, modelInput, selectedOption,
                new LocalDialogueConnection("http://127.0.0.1:8080/completion", 300000), structuredMood: true);
            JsonObject seedRecord = BuildInitialStateSeedRecord(initial);
            Require(seeded != null && seeded.Revision == 0 && seeded.LastGameTime == -1 &&
                seeded.TrustKnown == (sourceRelationship["trustKnown"]?.GetValue<bool>() ?? false) &&
                seeded.Resentment == sourceRelationship["resentment"].GetValue<int>() &&
                seeded.InterpersonalFear == sourceRelationship["interpersonalFear"].GetValue<int>() &&
                seededMood.Value == npc["mood"]["value"].GetValue<int>() &&
                seededMood.AcceptedTurnSequence == npc["mood"]["acceptedTurnSequence"].GetValue<long>() &&
                modelInput["world"]?["npcKnowledge"]?.ToJsonString(JsonOptions) == expectedKnowledge &&
                built.CoreDecision.Context.Contains("Actor-known evidence (synthetic fixture snapshot): " + expectedKnowledge,
                    StringComparison.Ordinal) &&
                qwen.Prompt.Contains(expectedKnowledge, StringComparison.Ordinal) &&
                qwen.Prompt.Contains("Persistent conversation mood: " + seededMood.Value.ToString(CultureInfo.InvariantCulture) + "/100",
                    StringComparison.Ordinal) &&
                seedRecord["sourceRelationshipRevision"]?.GetValue<long>() == sourceRelationship["revision"].GetValue<long>() &&
                seedRecord["seededRelationshipRevision"]?.GetValue<long>() == 0 &&
                seedRecord["acceptedRelationshipOutcomesSeeded"]?.GetValue<int>() == 0,
                "Initial typed relationship, mood, and actor knowledge must seed Core without fabricated outcome rows: " + episodeId + "/" + armId);
            if (seeded.TrustKnown)
                Require(seeded.ReliabilityTrust == sourceRelationship["reliabilityTrust"].GetValue<int>() &&
                    seeded.TrustConfidence == sourceRelationship["trustConfidence"].GetValue<int>() &&
                    seeded.TrustEvidenceCount == sourceRelationship["trustEvidenceCount"].GetValue<int>(),
                    "Known trust baseline must preserve authored value and evidence metadata: " + episodeId + "/" + armId);
        }

        private static void CheckMalformedContinuationEnvelope(JsonObject episode, JsonObject arm,
            IReadOnlyList<SocialContinuationTurn> turns, string caseId, string malformedD3Response,
            string malformedQwenResponse, string expectedFailure, int expectedCalls, string failedStage)
        {
            int calls = 0;
            SocialContinuationTransport transport = (endpoint, body, cancellationToken, appraisalRequest) =>
            {
                calls++;
                string response;
                if (endpoint.AbsolutePath == "/v1/systemone")
                    response = malformedD3Response ?? FakeSocialDecisionResponse(body);
                else
                {
                    Require(endpoint.AbsolutePath == "/completion", caseId + ": unexpected endpoint.");
                    RequireStructuredMoodQwenRequest(body, caseId);
                    response = malformedQwenResponse ?? "{\"model\":\"qwen-test\",\"content\":\"{\\\"reply\\\":\\\"Hello.\\\",\\\"moodDelta\\\":0}\"}";
                }
                return Task.FromResult(new SocialContinuationHttpResult(200, Encoding.UTF8.GetBytes(response), 1, null));
            };
            SocialContinuationReport report = RunSocialContinuationAsync(episode, arm, turns, transport,
                new Uri("http://127.0.0.1:8093/v1/systemone"), new Uri("http://127.0.0.1:8080/completion"),
                "d3-test", "qwen-test", _ => { }).GetAwaiter().GetResult();
            JsonObject row = report.Records.SingleOrDefault();
            JsonObject envelopeRecord = row?[failedStage] as JsonObject;
            string expectedRaw = failedStage == "d3" ? malformedD3Response : malformedQwenResponse;
            string actualRaw = Text(envelopeRecord, "rawResponse");
            string diagnostic = caseId + ": failure=" + report.StopReason + "; calls=" + calls +
                "; rawResponsePreserved=" + (actualRaw == expectedRaw) +
                "; stateUnchanged=" + (Text(row, "stateBeforeSha256") == Text(row, "stateAfterSha256")) +
                "; moodAfter=" + row?["moodAfter"]?.ToJsonString(JsonOptions);
            Require(report.CompletedTurns == 0 && report.Records.Count == 1 && calls == expectedCalls &&
                report.StopReason == expectedFailure && Text(row, "failureStage") == expectedFailure &&
                Bool(row, "coreCommitted") == false && actualRaw == expectedRaw &&
                report.FinalMood.AcceptedTurnSequence == 0 && report.FinalMood.Value == 0 &&
                Text(row, "stateBeforeSha256") == Text(row, "stateAfterSha256"),
                "Wrong-typed HTTP 200 envelope must be journaled and halt without commit. " + diagnostic);
            string rawBase64 = Text(envelopeRecord, "rawResponseBase64");
            Require(Convert.ToBase64String(Encoding.UTF8.GetBytes(expectedRaw)) == rawBase64,
                caseId + ": raw response bytes must remain in journal.");
        }

        private static string FakeSocialDecisionResponse(byte[] requestBody)
        {
            JsonObject wire = JsonNode.Parse(Encoding.UTF8.GetString(requestBody)) as JsonObject;
            JsonObject criteria = wire["questions"]?["decision"]?["criteria"] as JsonObject;
            string selected = criteria.First().Key;
            var probabilities = new JsonObject();
            for (int index = 0; index < criteria.Count; index++)
                probabilities[criteria.ElementAt(index).Key] = index == 0 ? 1 : 0;
            return new JsonObject { ["model"] = "d3-test", ["answers"] = new JsonObject
            {
                ["decision"] = new JsonObject { ["type"] = "choice", ["choice"] = selected,
                    ["probabilities"] = probabilities }
            }}.ToJsonString(JsonOptions);
        }

        private static void CheckAppraisalContextPairs(IReadOnlyList<AppraisalFixtureCase> cases)
        {
            JsonObject fixture = ReadFixture("appraisal-context-pairs-v3.json");
            JsonArray pairs = fixture["pairs"] as JsonArray;
            Require(pairs != null && pairs.Count == 3 && cases.Count == 38,
                "V3 must contain three context pairs appended to the unchanged 32-case v2 corpus.");
            var byId = cases.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
            var connection = new LocalDialogueConnection("http://127.0.0.1:8080/completion", 300000);
            foreach (JsonObject pair in pairs)
            {
                string pairId = Text(pair, "pairId");
                string contrast = Text(pair, "contrast");
                JsonArray arms = pair["arms"] as JsonArray;
                Require(arms != null && arms.Count == 2, "Each appraisal context contrast must have two arms: " + pairId);
                AppraisalFixtureCase[] matched = arms.Select(arm => byId[Text(arm as JsonObject, "caseId")]).ToArray();
                foreach (AppraisalFixtureCase item in matched)
                {
                    string body = LocalDialogueCompletion.BuildAppraisal(connection, item.Request).JsonBody;
                    string hiddenValue = Text(pair, "hiddenValue");
                    string caseId = item.CaseId;
                    Require(item.Request.MotivationContext != null &&
                        item.Request.MotivationContext.ObserverId == item.Request.ObserverId &&
                        item.Request.MotivationContext.SaveBranchId == item.Request.SaveBranchId &&
                        !item.Request.Prompt.Contains(pairId, StringComparison.OrdinalIgnoreCase) &&
                        !item.Request.Prompt.Contains(caseId, StringComparison.OrdinalIgnoreCase) &&
                        !item.Request.Prompt.Contains(item.Label, StringComparison.OrdinalIgnoreCase) &&
                        !body.Contains(pairId, StringComparison.OrdinalIgnoreCase) &&
                        !body.Contains(caseId, StringComparison.OrdinalIgnoreCase) &&
                        !body.Contains(item.Label, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(hiddenValue) ||
                         (!item.Request.Prompt.Contains(hiddenValue, StringComparison.OrdinalIgnoreCase) &&
                          !body.Contains(hiddenValue, StringComparison.OrdinalIgnoreCase))),
                        "Context review metadata must remain outside prompt and exact completion bytes: " + caseId);
                }

                if (contrast == "healthNeed")
                {
                    int[] intensities = matched.Select(item => item.Request.MotivationContext.Needs.Count == 1
                        ? item.Request.MotivationContext.Needs[0].Intensity.GetValueOrDefault(-1) : -1).OrderBy(x => x).ToArray();
                    Require(intensities.SequenceEqual(new[] { 20, 75 }) && matched.All(item => item.Request.MotivationContext.Duties.Count == 0),
                        "Health pair must vary only the supplied typed need intensity.");
                    Require(matched.All(item => item.Request.RelationshipTargetId == null &&
                        item.Request.Prompt.Contains("responsibleActorAttribution=Unknown,responsibleActor=unknown") &&
                        !item.Request.Prompt.Contains("responsibleActor=player")),
                        "Contextual health rows must preserve unknown injury cause.");
                }
                else if (contrast == "appointmentDuty")
                {
                    int[] dutyCounts = matched.Select(item => item.Request.MotivationContext.Duties.Count).OrderBy(x => x).ToArray();
                    Require(dutyCounts.SequenceEqual(new[] { 0, 1 }) &&
                        matched.Where(item => item.Request.MotivationContext.Duties.Count == 1).All(item =>
                            item.Request.MotivationContext.Duties[0].Kind == NpcMotivationDutyKind.Appointment &&
                            item.Request.MotivationContext.Duties[0].Status == NpcMotivationDutyStatus.Active &&
                            item.Request.MotivationContext.Duties[0].Scope == NpcMotivationDutyScope.Schedule),
                        "Appointment pair must supply one typed active schedule duty and one absent duty.");
                }
                else if (contrast == "knowledgeEpistemicState")
                {
                    string topicId = Text(pair, "publicTopicId");
                    NpcKnowledgeFactView[] facts = matched.Select(item =>
                    {
                        Require(item.Request.KnowledgeSnapshot.TryGet(topicId, out NpcKnowledgeFactView fact),
                            "Knowledge pair lost its actor-bound topic: " + item.CaseId);
                        return fact;
                    }).ToArray();
                    Require(facts.Any(item => item.Statement.EpistemicKind == NpcDisclosureEpistemicKind.VerifiedTruth &&
                            item.EvidenceOrigin == NpcKnowledgeEvidenceOrigin.NativeObservation) &&
                        facts.Any(item => item.Statement.EpistemicKind == NpcDisclosureEpistemicKind.Rumor &&
                            item.EvidenceOrigin == NpcKnowledgeEvidenceOrigin.AttributedReport &&
                            item.Statement.AttributionId != null) &&
                        facts.All(item => item.Statement.ValueId == Text(pair, "hiddenValue")) &&
                        matched.All(item => item.Request.Prompt.Contains(topicId + ":" +
                            (item.Request.KnowledgeSnapshot.TryGet(topicId, out NpcKnowledgeFactView fact)
                                ? fact.Statement.EpistemicKind.ToString() : "missing"), StringComparison.Ordinal)),
                        "Knowledge pair must preserve public verified-versus-attributed metadata only.");
                }
                else throw new InvalidDataException("Unexpected appraisal v3 context contrast: " + contrast);
            }
        }

        private static void CheckAppraisalAcceptanceScenarios(JsonObject fixture, JsonObject extension)
        {
            Require(fixture != null && fixture["fixtureId"]?.GetValue<string>() == "npc-experience-appraisal-acceptance-v1", "Appraisal acceptance fixture missing.");
            Require(extension != null && extension["fixtureId"]?.GetValue<string>() == "npc-experience-appraisal-acceptance-v2" &&
                extension["extendsFixtureId"]?.GetValue<string>() == "npc-experience-appraisal-acceptance-v1",
                "Appraisal acceptance v2 must extend and preserve v1.");
            JsonArray cases = fixture["cases"] as JsonArray;
            Require(cases != null && cases.Count == 6, "Expected six atomic-appraisal acceptance scenarios.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonObject scenario in cases)
            {
                Require(ids.Add(Text(scenario, "id")), "Duplicate appraisal acceptance scenario ID.");
                Require(scenario["invariants"] is JsonArray || scenario["arms"] is JsonArray || scenario["attempts"] is JsonArray,
                    "Every acceptance case must declare reviewable inputs and invariants.");
                Require(!ContainsProperty(scenario, "expectedDeltas"), "Do not prescribe fixed relationship deltas in acceptance fixtures.");
            }
            Require(ids.SetEquals(new[] {
                "appraisal.gift-known-vs-unknown-giver", "appraisal.native-rescue-vs-player-claim",
                "appraisal.confirmed-player-no-show-vs-interruption", "appraisal.direct-vs-quoted-threat",
                "appraisal.severe-affinity-loss-independent-of-positive-cap", "appraisal.atomic-branch-replay-stale-gates"
            }), "Appraisal acceptance case set is incomplete or unexpectedly changed.");
            JsonObject threat = cases.Cast<JsonObject>().Single(item => Text(item, "id") == "appraisal.direct-vs-quoted-threat");
            Require(threat["constructibleByCurrentSpeechEventFactory"]?.GetValue<bool>() == false &&
                Text(threat, "contractGap").Contains("Do not label a pure threat as Injury", StringComparison.Ordinal),
                "Threat fixture must expose the current speech-only event-construction gap instead of fabricating a world-event kind.");
            JsonObject atomic = cases.Cast<JsonObject>().Single(item => Text(item, "id") == "appraisal.atomic-branch-replay-stale-gates");
            Require(Text(atomic, "currentContractSupport").Contains("atomic appraisal consumer is an integration seam", StringComparison.Ordinal),
                "Do not claim an atomic transaction API that has not been located and verified.");
            Require(Enum.IsDefined(typeof(NpcExperienceEventKind), NpcExperienceEventKind.MoneyReceived) &&
                Enum.IsDefined(typeof(NpcExperienceEventKind), NpcExperienceEventKind.CombatHelp) &&
                Enum.IsDefined(typeof(NpcExperienceEventKind), NpcExperienceEventKind.AppointmentNoShow),
                "Acceptance cases reference current native experience event kinds.");
            Require(Enum.IsDefined(typeof(NpcExperienceSpeechActKind), NpcExperienceSpeechActKind.DirectThreat) &&
                Enum.IsDefined(typeof(NpcExperienceSpeechActKind), NpcExperienceSpeechActKind.QuotedThreat),
                "Direct and quoted threat speech categories must exist in the current response vocabulary.");
            VerifyGiftAndRescueParserContracts();
            VerifyNoShowWaitContract();
            VerifyCurrentLedgerAcceptanceContracts(extension);
        }

        private static bool ContainsProperty(JsonNode node, string property)
        {
            if (node is JsonObject obj)
                return obj.Any(item => item.Key == property || ContainsProperty(item.Value, property));
            if (node is JsonArray array)
                return array.Any(item => ContainsProperty(item, property));
            return false;
        }

        private static void VerifyGiftAndRescueParserContracts()
        {
            const string observer = "npc:observer";
            NpcPersonalityProfile profile = NpcPersonalityGenerator.Generate(observer, 17, NpcSocialBackground.Commoner);
            var relation = NpcDirectedRelationship.Create(observer, "player");
            relation.Revision = 1;
            relation.LastGameTime = 100;
            NpcDirectedRelationshipView view = relation.ToView();
            NpcExperienceEvent knownGift = NpcExperienceEvent.FromNativeOutcome(observer, observer, "receipt:gift", "episode:gift", "outcome:gift", NpcExperienceEventKind.MoneyReceived, 110, NpcResponsibleActorAttribution.Known, "player");
            NpcExperienceAppraisalRequest knownGiftRequest = NpcExperienceAppraisalContract.CreateRequest("branch:current", knownGift, profile, 0, 1, 100, 20, 1, 1, 120, view, null);
            Require(NpcExperienceAppraisalContract.TryParseResponse(knownGiftRequest, AppraisalResponse(knownGiftRequest, affinity: 1), out _),
                "A known gift may use available positive affinity credit.");
            Require(!NpcExperienceAppraisalContract.TryParseResponse(knownGiftRequest, AppraisalResponse(knownGiftRequest, trust: 1), out _),
                "MoneyReceived cannot create trust.");
            NpcExperienceEvent unknownGift = NpcExperienceEvent.FromNativeOutcome(observer, observer, "receipt:unknown-gift", "episode:gift", "outcome:unknown-gift", NpcExperienceEventKind.MoneyReceived, 110, NpcResponsibleActorAttribution.Unknown);
            NpcExperienceAppraisalRequest unknownGiftRequest = NpcExperienceAppraisalContract.CreateRequest("branch:current", unknownGift, profile, 0, 1, 100, 20, 1, 1, 120, null, null);
            Require(!NpcExperienceAppraisalContract.TryParseResponse(unknownGiftRequest, AppraisalResponse(unknownGiftRequest, affinity: 1), out _),
                "Unknown giver cannot receive a directed affinity effect.");

            NpcExperienceEvent rescue = NpcExperienceEvent.FromNativeOutcome(observer, observer, "receipt:rescue", "episode:rescue", "outcome:rescue", NpcExperienceEventKind.CombatHelp, 110, NpcResponsibleActorAttribution.Known, "player");
            NpcExperienceAppraisalRequest rescueRequest = NpcExperienceAppraisalContract.CreateRequest("branch:current", rescue, profile, 0, 1, 100, 20, 1, 1, 120, view, null);
            Require(NpcExperienceAppraisalContract.TryParseResponse(rescueRequest, AppraisalResponse(rescueRequest, trust: 1), out _),
                "A confirmed native CombatHelp outcome may support trust appraisal.");
            NpcExperienceEvent claim = NpcExperienceEvent.FromAttributedSpeech(observer, observer, "receipt:claim", "episode:claim", "utterance:claim", NpcExperienceEventKind.CombatHelp, 110, "player", "player");
            NpcObservedConversationTurn utterance = NpcObservedConversationTurn.Create(observer, "episode:claim", "player", "receipt:claim", "I rescued you.", 110);
            NpcExperienceAppraisalRequest claimRequest = NpcExperienceAppraisalContract.CreateRequest("branch:current", claim, profile, 0, 1, 100, 20, 1, 1, 120, view, null, false, utterance);
            Require(!NpcExperienceAppraisalContract.TryParseResponse(claimRequest, AppraisalResponse(claimRequest, trust: 1), out _),
                "An unverified speech claim cannot create trust.");
        }

        private static void VerifyCurrentLedgerAcceptanceContracts(JsonObject fixture)
        {
            const string observer = "npc:observer";
            const string player = "player";
            NpcPersonalityProfile profile = NpcPersonalityGenerator.Generate(observer, 23, NpcSocialBackground.Commoner);
            var relation = NpcDirectedRelationship.Create(observer, player).ToView();
            var gift = NpcExperienceEvent.FromNativeOutcome(observer, observer, "receipt:ledger-gift", "episode:ledger-gift",
                "outcome:ledger-gift", NpcExperienceEventKind.MoneyReceived, 110,
                NpcResponsibleActorAttribution.Known, player);
            var giftRequest = NpcExperienceAppraisalContract.CreateRequest("branch:current", gift, profile,
                0, 0, 100, 0, 0, 1, 110, relation, null);
            Require(NpcExperienceAppraisalContract.TryParseResponse(giftRequest,
                AppraisalResponse(giftRequest, affinity: 1), out NpcExperienceAppraisal giftAppraisal),
                "Known MoneyReceived may parse a bounded affinity proposal with available credit.");
            Require(NpcExperienceAppraisalContract.TryParseResponse(giftRequest,
                AppraisalResponse(giftRequest, mood: -2), out _),
                "An unwanted or awkward gift can be appraised without automatically affecting trust.");
            var rescue = NpcExperienceEvent.FromNativeOutcome(observer, observer, "receipt:ledger-rescue", "episode:ledger-rescue",
                "outcome:ledger-rescue", NpcExperienceEventKind.CombatHelp, 110,
                NpcResponsibleActorAttribution.Known, player);
            var rescueRequest = NpcExperienceAppraisalContract.CreateRequest("branch:current", rescue, profile,
                0, 0, 100, 0, 0, 0, 110, relation, null);
            Require(NpcExperienceAppraisalContract.TryParseResponse(rescueRequest,
                AppraisalResponse(rescueRequest, trust: 1), out NpcExperienceAppraisal rescueAppraisal),
                "Confirmed rescue outcome may support a bounded trust appraisal.");
            var empty = NpcRelationshipLedger.Create();
            Require(NpcRelationshipLedger.TryApplyAppraisal(empty, 0, "branch:current", "branch:current", rescue,
                rescueAppraisal, KnownAppraisalIdentity, out NpcRelationshipLedger acceptedGift, out _),
                "Current pure relationship reducer accepts a bound appraisal candidate.");
            Require(empty.Revision == 0 && empty.AcceptedOutcomes.Count == 0 && acceptedGift.Revision == 1,
                "Ledger appraisal must return a detached candidate without mutating the source.");
            Require(!NpcRelationshipLedger.TryApplyAppraisal(acceptedGift, 1, "branch:fork", "branch:current", rescue,
                rescueAppraisal, KnownAppraisalIdentity, out _, out _), "Wrong-branch appraisal must be rejected.");
            Require(!NpcRelationshipLedger.TryApplyAppraisal(acceptedGift, 0, "branch:current", "branch:current", rescue,
                rescueAppraisal, KnownAppraisalIdentity, out _, out _), "Stale ledger revision must be rejected.");
            Require(!NpcRelationshipLedger.TryApplyAppraisal(acceptedGift, 1, "branch:current", "branch:current", rescue,
                rescueAppraisal, KnownAppraisalIdentity, out _, out _), "Replayed canonical event must be rejected.");
            Require(acceptedGift.Revision == 1 && acceptedGift.AcceptedOutcomes.Count == 1,
                "Rejected replay/branch/stale attempts must leave the accepted candidate unchanged.");

            var speech = NpcExperienceEvent.FromAttributedSpeech(observer, observer, "receipt:direct-threat",
                "episode:direct-threat", "utterance:direct-threat", NpcExperienceEventKind.StatusOnset, 110, player);
            var observed = NpcObservedConversationTurn.Create(observer, "episode:direct-threat", player,
                "receipt:direct-threat", "I will kill your sister.", 110);
            var speechRequest = NpcExperienceAppraisalContract.CreateRequest("branch:current", speech, profile,
                0, 0, 100, 0, 0, 0, 110, relation, null, false, observed, null, new[] { "npc:family-member" });
            string speechTarget = "npc:family-member";
            int threatLoss = -NpcExperienceAppraisalContract.DirectThreatAffinityLossBound;
            string directResponse = AppraisalResponse(speechRequest, affinity: threatLoss,
                trust: -NpcExperienceAppraisalContract.DirectThreatTrustLossBound,
                resentment: NpcExperienceAppraisalContract.SevereResentmentAndFearBound,
                fear: NpcExperienceAppraisalContract.SevereResentmentAndFearBound,
                speechAct: NpcExperienceSpeechActKind.DirectThreat, speechTarget: speechTarget);
            Require(NpcExperienceAppraisalContract.TryParseResponse(speechRequest, directResponse,
                out NpcExperienceAppraisal directThreat),
                "The direct-threat negative bound remains independent from zero positive affinity credit.");
            var threatAttitude = new ConversationAttitude
            {
                PersonId = observer,
                Day = 0,
                PositiveCredit = 0,
                PositiveCreditUpdatedAt = 110
            };
            Require(ConversationAffinityEventPolicy.TryCreate("branch:current", speech, speechRequest,
                directThreat, threatAttitude, 0, out ConversationAffinityEventUpdate affinityUpdate, out _),
                "Current affinity reducer permits separately bounded negative speech loss at zero positive credit.");
            Require(affinityUpdate.AppliedDelta < 0, "Severe negative loss must not be clipped by positive credit.");
            Require(NpcRelationshipLedger.TryApplyAppraisal(NpcRelationshipLedger.Create(), 0, "branch:current", "branch:current",
                speech, directThreat, KnownAppraisalIdentity, out NpcRelationshipLedger acceptedThreat, out _),
                "Parser-produced direct-threat relationship candidate must pass current detached-ledger bounds.");
            Require(acceptedThreat.AcceptedOutcomes.Count == 1 && acceptedThreat.AcceptedOutcomes[0].SpeechTargetPersonId == speechTarget,
                "Accepted speech evidence preserves its target and canonical utterance record.");
            Require(!NpcExperienceAppraisalContract.TryParseResponse(speechRequest,
                AppraisalResponse(speechRequest, affinity: 1, speechAct: NpcExperienceSpeechActKind.Other), out _),
                "Positive affinity remains capped by zero available credit.");

            JsonArray cases = fixture["cases"] as JsonArray;
            Require(cases != null && cases.Count == 6, "Acceptance v2 must add six targeted cases without rewriting frozen v1.");
            Require(cases.Cast<JsonObject>().Any(item => Text(item, "id") == "appraisal-v2.branch-replay-stale-atomicity"),
                "Acceptance v2 must retain the explicit branch/replay/stale boundary scenario.");
            JsonObject personality = cases.Cast<JsonObject>().Single(item => Text(item, "id") == "appraisal-v2.emotional-stability-response-contrast");
            Require(Text(personality, "contrastFactor") == "NpcPersonalityProfile.EmotionalStability" &&
                Strings(personality["unsupportedTraits"]).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
                    new[] { "humor", "insecurity", "thin-skinned" }),
                "Personality contrast must use the current stable EmotionalStability axis and explicitly reject fabricated traits.");
            JsonArray personalityArms = personality["arms"] as JsonArray;
            Require(personalityArms != null && personalityArms.Count == 2 &&
                personalityArms.All(arm => (arm as JsonObject)?["stablePersonality"]?["manner"]?.GetValue<string>() == "dry-humored"),
                "Emotional-stability contrast must hold the existing Manner axis constant.");
            Require(Text(fixture["contractReferences"] as JsonObject, "scopeLimit").Contains("does not by itself commit mood", StringComparison.Ordinal),
                "Do not describe the pure relationship reducer as a cross-store atomic transaction.");
            JsonObject references = fixture["contractReferences"] as JsonObject;
            JsonArray storeMethods = references?["storeConsumerDeclarations"] as JsonArray;
            Require(storeMethods != null && storeMethods.Count == 2 &&
                Text(references, "storeConsumerStatus").Contains("intentional stubs that return false", StringComparison.Ordinal),
                "Acceptance fixture must identify current WorldStateStore capture/commit declarations as stubs.");
        }

        private static bool KnownAppraisalIdentity(string id)
        {
            return id == "npc:observer" || id == "npc:family-member" || id == "player";
        }

        private static void VerifyNoShowWaitContract()
        {
            const string observer = "npc:observer";
            var profile = NpcPersonalityGenerator.Generate(observer, 19, NpcSocialBackground.Commoner);
            var relation = NpcDirectedRelationship.Create(observer, "player");
            relation.Revision = 1;
            relation.LastGameTime = 100;
            var view = relation.ToView();
            NpcExperienceEvent noShow = NpcExperienceEvent.FromNativeOutcome(observer, observer, "receipt:no-show", "episode:no-show", "outcome:no-show", NpcExperienceEventKind.AppointmentNoShow, 110, NpcResponsibleActorAttribution.Known, "player");
            bool rejectedWithoutWait = false;
            try { NpcExperienceAppraisalContract.CreateRequest("branch:current", noShow, profile, 0, 1, 100, 20, 1, 1, 120, view, null, false); }
            catch (ArgumentException) { rejectedWithoutWait = true; }
            Require(rejectedWithoutWait, "No-show appraisal must require native waited-for evidence.");
            NpcExperienceAppraisalRequest waited = NpcExperienceAppraisalContract.CreateRequest("branch:current", noShow, profile, 0, 1, 100, 20, 1, 1, 120, view, null, true);
            Require(waited != null, "A confirmed wait allows the no-show request to be constructed.");

            var confirmedAbsence = new AppointmentRecord
            {
                Id = "actual-no-show",
                ResidentId = observer,
                StartsAt = 100,
                EndsAt = 200,
                PatienceSeconds = 600,
                Status = "player-absent",
                NpcArrivedAt = 100,
                ResolvedAt = 701,
                NpcLastSeenAt = 690,
                NpcWaitedSeconds = 30,
                OutcomeRecorded = true,
                NoShowPenalty = 1
            };
            Require(NpcAppointmentExperience.TryCreate(confirmedAbsence, out NpcExperienceEvent producedNoShow) &&
                producedNoShow.EventKind == NpcExperienceEventKind.AppointmentNoShow &&
                producedNoShow.ResponsibleActorId == "player",
                "The native appointment producer emits player no-show only after confirmed arrival and wait evidence.");
            var interruption = new AppointmentRecord
            {
                Id = "npc-interrupted",
                ResidentId = observer,
                StartsAt = 100,
                EndsAt = 200,
                PatienceSeconds = 600,
                Status = "interrupted",
                NpcArrivedAt = 100,
                ResolvedAt = 200,
                OutcomeRecorded = true,
                NoShowPenalty = 0
            };
            Require(!NpcAppointmentExperience.TryCreate(interruption, out NpcExperienceEvent interruptionEvidence) &&
                interruptionEvidence == null,
                "NPC interruption is not converted to a player-blame appointment event.");
        }

        private static string AppraisalResponse(NpcExperienceAppraisalRequest request, int mood = 0, int affinity = 0,
            int trust = 0, int resentment = 0, int fear = 0,
            NpcExperienceSpeechActKind speechAct = NpcExperienceSpeechActKind.NotApplicable, string speechTarget = "")
        {
            return JsonSerializer.Serialize(new
            {
                schemaVersion = NpcExperienceAppraisalContract.SchemaVersion,
                policyVersion = NpcExperienceAppraisalContract.PolicyVersion,
                saveBranchId = request.SaveBranchId,
                observerId = request.ObserverId,
                subjectPersonId = request.SubjectPersonId,
                evidenceDeduplicationKey = request.EvidenceDeduplicationKey,
                canonicalEvidenceId = request.CanonicalEvidenceId,
                relationshipTargetId = request.RelationshipTargetId ?? string.Empty,
                expectedMoodRevision = request.ExpectedMoodRevision,
                expectedAffinityRevision = request.ExpectedAffinityRevision,
                expectedRelationshipRevision = request.ExpectedRelationshipRevision,
                moodDelta = mood,
                affinityDelta = affinity,
                trustDelta = trust,
                resentmentDelta = resentment,
                fearDelta = fear,
                speechActKind = speechAct.ToString(),
                speechTargetPersonId = speechTarget,
                conversationOption = NpcExperienceConversationOption.NoAction.ToString()
            }, JsonOptions);
        }

        private static void CheckCorpus(JsonObject corpus, int expectedPairs, int expectedArms, JsonObject v1)
        {
            JsonArray pairs = Pairs(corpus);
            Require(pairs.Count == expectedPairs, "Unexpected pair count in " + corpus["corpusId"]?.GetValue<string>() + ".");
            int armCount = 0;
            var armIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonObject pair in pairs)
            {
                string pairId = Text(pair, "id");
                string factor = Text(pair, "factorId");
                Require(!string.IsNullOrWhiteSpace(pairId) && !string.IsNullOrWhiteSpace(factor) && !string.IsNullOrWhiteSpace(Text(pair, "factorRationale")), "Every pair needs a named, documented factor.");
                JsonObject common = pair["commonInput"] as JsonObject;
                JsonArray arms = pair["arms"] as JsonArray;
                Require(common != null && arms != null && arms.Count == 2, "Every scenario contrast must have one common input and two arms.");
                JsonObject[] merged = arms.Select(a => Merge(common, a["contextPatch"] as JsonObject)).ToArray();
                foreach (JsonObject arm in arms)
                {
                    armCount++;
                    string armId = Text(arm, "id");
                    Require(!string.IsNullOrWhiteSpace(armId) && armIds.Add(armId), "Arm IDs must be unique and non-empty.");
                    JsonArray offered = merged[Array.IndexOf(arms.ToArray(), arm)]["options"] as JsonArray;
                    var offeredIds = new HashSet<string>(offered?.Select(o => Text(o as JsonObject, "id")) ?? Array.Empty<string>(), StringComparer.Ordinal);
                    Require(offered != null && offeredIds.Count == offered.Count, pairId + ": offered choice IDs must be unique.");
                    foreach (string key in new[] { "expectedNativeEligibleOptionIds", "acceptableD3Choices" })
                    {
                        JsonArray expected = arm[key] as JsonArray;
                        Require(expected != null && expected.Count > 0, pairId + ": missing " + key + ".");
                        foreach (JsonNode optionId in expected)
                            Require(offeredIds.Contains(optionId.GetValue<string>()), pairId + ": " + key + " contains an unoffered option.");
                    }
                }
                IReadOnlyList<string> differences = ChangedLeafPaths(merged[0], merged[1]);
                Require(differences.Count > 0, pairId + ": arms do not change their factor.");
                Require(differences.All(path => AllowedFactorPath(factor, path)), pairId + ": contrast changes fields outside factor " + factor + ": " + string.Join(", ", differences));
            }
            Require(armCount == expectedArms, "Unexpected arm count in " + corpus["corpusId"]?.GetValue<string>() + ".");
            if (v1 != null)
            {
                JsonArray oldPairs = Pairs(v1);
                for (int i = 0; i < oldPairs.Count; i++)
                    Require(oldPairs[i].ToJsonString() == pairs[i].ToJsonString(), "V2 must preserve v1 frozen pair " + i + ".");
                Require(pairs.Skip(oldPairs.Count).All(pair => Text(pair as JsonObject, "id").StartsWith("extensionv2.", StringComparison.Ordinal)), "V2 appended IDs must keep the extensionv2 prefix.");
                foreach (JsonObject pair in pairs.Skip(oldPairs.Count))
                    Require(Text(pair, "adapterStatus") == "planned_adapter_only", "New contrasts must not claim current native option support.");
            }
        }

        private static void CheckMultiTurnFixture(JsonObject corpus)
        {
            Require(corpus != null && Text(corpus, "corpusId") == "npc-complex-interactions-v2" &&
                corpus["contextSchemaVersion"]?.GetValue<int>() == 2, "Typed multi-turn fixture identity/schema changed.");
            JsonArray episodes = corpus["episodes"] as JsonArray;
            Require(episodes != null && episodes.Count == 5, "Expected five frozen multi-turn episodes.");
            var episodeIds = new HashSet<string>(StringComparer.Ordinal);
            var armIds = new HashSet<string>(StringComparer.Ordinal);
            int arms = 0, turns = 0;
            foreach (JsonObject episode in episodes)
            {
                Require(episodeIds.Add(Text(episode, "id")), "Duplicate multi-turn episode ID.");
                Require(Text(episode, "adapterStatus") == "planned_adapter_only", "Multi-turn fixture must keep unsupported adapter status explicit.");
                JsonArray episodeArms = episode["arms"] as JsonArray;
                Require(episodeArms != null && episodeArms.Count == 2, "Each multi-turn episode must have matched arms.");
                int? armTurnCount = null;
                foreach (JsonObject arm in episodeArms)
                {
                    arms++;
                    Require(armIds.Add(Text(arm, "id")), "Duplicate multi-turn arm ID.");
                    JsonArray armTurns = arm["turns"] as JsonArray;
                    Require(armTurns != null && armTurns.Count > 0, "Every arm needs ordered turns.");
                    if (armTurnCount.HasValue) Require(armTurnCount.Value == armTurns.Count, "Matched arms need equal turn counts.");
                    armTurnCount = armTurns.Count;
                    var turnIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonObject turn in armTurns)
                    {
                        turns++;
                        Require(turnIds.Add(Text(turn, "turnId")), "Duplicate turn ID within arm.");
                        JsonObject input = turn["input"] as JsonObject ?? throw new InvalidDataException("Every turn needs a full model-facing input snapshot.");
                        JsonObject personality = input["npc"]?["personality"] as JsonObject;
                        Require(personality != null && personality.Count == 17, "Every multi-turn input needs the full 17-axis personality profile.");
                        Require(input["options"] is JsonArray && input["playerText"] != null, "Multi-turn model input lacks choices or player text.");
                        JsonObject npc = input["npc"] as JsonObject;
                        JsonObject relationship = npc?["relationship"] as JsonObject;
                        JsonObject mood = npc?["mood"] as JsonObject;
                        Require(npc?["disposition"] != null && npc?["personalitySeed"] != null &&
                            relationship?["revision"] != null && relationship?["lastGameTime"] != null &&
                            relationship?["trustKnown"] != null && relationship?["resentment"] != null &&
                            relationship?["interpersonalFear"] != null && mood?["value"] != null &&
                            mood?["acceptedTurnSequence"] != null && mood?["stateRevision"] != null &&
                            mood?["updatedAtGameSeconds"] != null,
                            "V2 snapshots must provide typed numeric disposition, mood, and directed relationship state.");
                        if (relationship?["trustKnown"]?.GetValue<bool>() == true)
                            Require(relationship["reliabilityTrust"] != null && relationship["trustEvidenceCount"]?.GetValue<int>() > 0,
                                "Known trust requires an explicit value and evidence count.");
                        else Require(relationship?["reliabilityTrust"] == null && relationship?["trustEvidenceCount"]?.GetValue<int>() == 0,
                            "Unknown trust cannot be replaced with a numeric anchor.");
                        Require(turn["oracle"] is JsonObject, "Every multi-turn turn needs its separate evaluator oracle.");
                        Require(!ContainsProperty(input, "oracle") && !ContainsProperty(input, "nativeEvents") && !ContainsProperty(input, "worldTruth"),
                            "Model-facing turn input contains evaluator-only oracle fields.");
                    }
                }
            }
            Require(arms == 10 && turns == 20, "Multi-turn suite arm/turn counts changed.");
        }

        private static bool AllowedFactorPath(string factor, string path)
        {
            if (factor == "enforcementCapability.authorityAndLocalGuards") return path.StartsWith("npc.authorityEvidence", StringComparison.Ordinal) || path.StartsWith("world.nativeFacts", StringComparison.Ordinal) || path.StartsWith("world.npcKnowledge", StringComparison.Ordinal);
            if (factor.StartsWith("speechAct.", StringComparison.Ordinal)) return path == "playerText" || path.StartsWith("world.nativeFacts", StringComparison.Ordinal) || path.StartsWith("world.npcKnowledge", StringComparison.Ordinal);
            if (factor == "extensionv2.personality.statusAttitude" || factor == "personality.status-attitude") return path.StartsWith("npc.personality.statusAttitude", StringComparison.Ordinal);
            if (factor.StartsWith("extensionv2.relationship.", StringComparison.Ordinal)) return path.StartsWith("world.nativeFacts", StringComparison.Ordinal) || path.StartsWith("world.npcKnowledge", StringComparison.Ordinal);
            if (factor.StartsWith("relationship.trust", StringComparison.Ordinal)) return path.StartsWith("npc.relationship.trust", StringComparison.Ordinal) || path.StartsWith("world.nativeFacts", StringComparison.Ordinal) || path.StartsWith("world.npcKnowledge", StringComparison.Ordinal);
            if (factor == "relationship.affinity") return path.StartsWith("npc.relationship.affinity", StringComparison.Ordinal);
            if (factor.StartsWith("relationship.", StringComparison.Ordinal)) return path.StartsWith("npc.relationship", StringComparison.Ordinal) || path.StartsWith("world.nativeFacts", StringComparison.Ordinal) || path.StartsWith("world.npcKnowledge", StringComparison.Ordinal);
            if (factor.StartsWith("obligation.", StringComparison.Ordinal)) return path.StartsWith("npc.obligation", StringComparison.Ordinal) || path.StartsWith("world.nativeFacts", StringComparison.Ordinal) || path.StartsWith("world.npcKnowledge", StringComparison.Ordinal);
            if (factor.StartsWith("economy.", StringComparison.Ordinal)) return path == "playerText" || path.StartsWith("world.nativeFacts", StringComparison.Ordinal) || path.StartsWith("world.npcKnowledge", StringComparison.Ordinal);
            if (factor.StartsWith("transfer.", StringComparison.Ordinal) || factor.StartsWith("promise.", StringComparison.Ordinal) || factor.StartsWith("wage.", StringComparison.Ordinal) || factor.StartsWith("appointment.", StringComparison.Ordinal) || factor.StartsWith("reliability.", StringComparison.Ordinal) || factor.StartsWith("resource.", StringComparison.Ordinal)) return path.StartsWith("world.nativeFacts", StringComparison.Ordinal) || path.StartsWith("world.npcKnowledge", StringComparison.Ordinal);
            if (factor == "request.instrument") return path == "playerText";
            if (factor == "mood.elapsedGameTime") return path == "world.gameTimeSeconds";
            if (factor == "player.actualRank") return path.StartsWith("world.npcKnowledge", StringComparison.Ordinal);
            if (factor == "recipient.knownNeed" || factor == "npc.responseAct" || factor == "obligation.knownFamilyDuty" || factor.StartsWith("npcKnowledge.", StringComparison.Ordinal)) return path.StartsWith("world.npcKnowledge", StringComparison.Ordinal);
            if (factor.StartsWith("perception.", StringComparison.Ordinal) || factor.StartsWith("npcnpc.", StringComparison.Ordinal) || factor.StartsWith("knowledge.", StringComparison.Ordinal) || factor.StartsWith("social.", StringComparison.Ordinal) || factor.StartsWith("commitment.", StringComparison.Ordinal) || factor.StartsWith("status.", StringComparison.Ordinal)) return path.StartsWith("world.nativeFacts", StringComparison.Ordinal) || path.StartsWith("world.npcKnowledge", StringComparison.Ordinal);
            if (factor.StartsWith("personality.", StringComparison.Ordinal))
            {
                string axis = factor.Substring("personality.".Length).Split('-', '.')[0];
                string field = axis == "emotional" ? "emotionalStability" : axis;
                if (factor.Contains("eloquence", StringComparison.Ordinal)) field = "eloquence";
                if (factor.Contains("integrity", StringComparison.Ordinal)) field = "integrity";
                if (factor.Contains("privacy", StringComparison.Ordinal)) field = "privacy";
                if (factor.Contains("manner", StringComparison.Ordinal)) field = "manner";
                return path == "npc.personality." + field;
            }
            if (factor == "mood.cooldown-game-time") return path == "world.gameTimeSeconds";
            return false;
        }

        private static IReadOnlyList<string> ChangedLeafPaths(JsonNode left, JsonNode right, string prefix = "")
        {
            var changes = new List<string>();
            if (left is JsonObject lo && right is JsonObject ro)
            {
                foreach (string key in lo.Select(p => p.Key).Union(ro.Select(p => p.Key), StringComparer.Ordinal))
                {
                    string path = prefix.Length == 0 ? key : prefix + "." + key;
                    changes.AddRange(ChangedLeafPaths(lo[key], ro[key], path));
                }
                return changes;
            }
            if ((left == null) != (right == null) || (left != null && right != null && left.ToJsonString() != right.ToJsonString())) changes.Add(prefix);
            return changes;
        }

        private sealed class BuiltRequests
        {
            public NpcDecisionRequest CoreDecision;
            public DialogueCompletion QwenCompletion;
            public object D3Payload;
            public string ActorKnownJson;
            public string DecisionHash;
            public string D3RequestBody;
            public ConversationContextSnapshot ContextSnapshot;
        }

        private static BuiltRequests BuildRequests(JsonObject pair, JsonObject arm, JsonObject input, string requestDiscriminator = null)
        {
            JsonObject npc = input["npc"] as JsonObject ?? new JsonObject();
            JsonObject world = input["world"] as JsonObject ?? new JsonObject();
            JsonArray options = input["options"] as JsonArray ?? throw new InvalidDataException("Scenario has no options.");
            string personId = Text(npc, "id");
            string playerText = input["playerText"]?.GetValue<string>() ?? string.Empty;
            JsonObject modelState = new JsonObject
            {
                ["npc"] = CloneObject(npc, "id", "role", "personality", "relationship", "mood", "obligation", "obligations", "authorityEvidence"),
                ["world"] = new JsonObject
                {
                    ["gameTimeSeconds"] = world["gameTimeSeconds"]?.DeepClone(),
                    ["scene"] = world["scene"]?.DeepClone(),
                    ["saveBranch"] = world["saveBranch"]?.DeepClone(),
                    ["npcKnowledge"] = world["npcKnowledge"]?.DeepClone() ?? new JsonArray()
                },
                ["history"] = input["history"]?.DeepClone() ?? new JsonArray(),
                ["playerText"] = playerText,
                ["availableTools"] = input["availableTools"]?.DeepClone() ?? new JsonArray(),
                ["options"] = options.DeepClone()
            };
            string context = modelState.ToJsonString(JsonOptions);
            string actorKnownJson = (world["npcKnowledge"] as JsonArray ?? new JsonArray()).ToJsonString(JsonOptions);
            double capturedTime = double.Parse(world["gameTimeSeconds"]?.ToJsonString() ?? "0", CultureInfo.InvariantCulture);
            string requestKey = requestDiscriminator ?? Text(arm, "id");
            string session = "npc-factor:" + requestKey;
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, personId, null,
                session, 0, 0, new string('a', 32), string.Empty, "synthetic-factor-scene", 0);
            var unknown = ConversationContextFact<string>.Unknown();
            var snapshot = new ConversationContextSnapshot(binding, capturedTime, unknown, unknown,
                ConversationContextFact<ConversationDialogueRole>.Unknown(), unknown, unknown,
                ConversationContextFact<ConversationNpcPosition>.Unknown(), ConversationContextFact<ConversationNpcActivity>.Unknown(),
                ConversationContextFact<bool>.Unknown(), ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), unknown,
                0, 0, null, Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>());
            var decisionOptions = options.Select(node => new NpcDecisionOption(Text(node as JsonObject, "id"), Text(node as JsonObject, "description"), false)).ToArray();
            var decision = new NpcDecisionRequest(DeterministicGuid(requestKey), snapshot, 0,
                "npc-factor-choice-review", ChoiceInstructions, playerText, context, decisionOptions);
            JsonObject personality = npc["personality"] as JsonObject ?? new JsonObject();
            JsonObject relationship = npc["relationship"] as JsonObject ?? new JsonObject();
            JsonObject mood = npc["mood"] as JsonObject ?? new JsonObject();
            string reaction = "Synthetic review context only; this does not authorize any game action. Stable personality: " + personality.ToJsonString(JsonOptions) +
                ". Current mood: " + mood.ToJsonString(JsonOptions) + ". Relationship evidence: " + relationship.ToJsonString(JsonOptions) +
                ". Actor-known evidence: " + actorKnownJson;
            var voiceRequest = new DialogueRequest(playerText, "I will consider what you said.", "NPC", "unknown", "unknown",
                Text(npc, "role", "unknown"), 0, true, reaction, SerializeHistory(input["history"]), string.Empty, null, null, false, 0);
            DialogueCompletion voice = LocalDialogueCompletion.Build(
                new LocalDialogueConnection("http://127.0.0.1:8080/completion", 10000), voiceRequest);
            LocalDecisionPayload d3Wire = LocalDecisionWire.Build(decision);
            string d3RequestBody = Encoding.UTF8.GetString(d3Wire.Bytes);
            JsonObject d3Payload = JsonNode.Parse(d3RequestBody) as JsonObject ??
                throw new InvalidDataException("Production LocalDecisionWire returned invalid JSON.");
            return new BuiltRequests { CoreDecision = decision, QwenCompletion = voice,
                D3Payload = d3Payload, D3RequestBody = d3RequestBody, DecisionHash = d3Wire.Sha256,
                ActorKnownJson = actorKnownJson };
        }

        private static BuiltRequests BuildMultiTurnRequests(JsonObject episode, JsonObject arm, JsonObject turn,
            JsonObject input, string requestDiscriminator = null, string continuationSessionId = null, long? continuationRequestRevision = null,
            long continuationCandidateSetRevision = 0)
        {
            BuiltRequests built = BuildRequests(episode, arm, input, requestDiscriminator ??
                Text(arm, "id") + "/" + Text(turn, "turnId"));
            JsonObject npc = input["npc"] as JsonObject ?? throw new InvalidDataException("Turn has no NPC snapshot.");
            JsonObject world = input["world"] as JsonObject ?? throw new InvalidDataException("Turn has no world snapshot.");
            JsonObject traits = npc["personality"] as JsonObject ?? throw new InvalidDataException("Turn has no personality snapshot.");
            string personId = Text(npc, "id");
            double at = double.Parse(world["gameTimeSeconds"]?.ToJsonString() ?? "0", CultureInfo.InvariantCulture);
            string scene = Text(world, "scene", "synthetic-scene");
            var profile = new NpcPersonalityProfile
            {
                PersonId = personId, Version = 2, Seed = checked((uint)npc["personalitySeed"].GetValue<int>()), Background = NpcSocialBackground.Commoner,
                Warmth = Text(traits, "warmth"), Patience = Text(traits, "patience"), Curiosity = Text(traits, "curiosity"),
                Privacy = Text(traits, "privacy"), Manner = Text(traits, "manner"), Outlook = Text(traits, "outlook"),
                EmotionalStability = Text(traits, "emotionalStability"), Ambition = Text(traits, "ambition"),
                Integrity = Text(traits, "integrity"), Courage = Text(traits, "courage"), Dutifulness = Text(traits, "dutifulness"),
                Vocabulary = Text(traits, "vocabulary"), Eloquence = Text(traits, "eloquence"), StatusAttitude = Text(traits, "statusAttitude"),
                Humor = Text(traits, "humor"), SelfEsteem = Text(traits, "selfEsteem"), Sensitivity = Text(traits, "sensitivity")
            };
            string description = NpcPersonalityProfile.CopyValidated(profile).Description();
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, personId, null,
                continuationSessionId ?? "multi-turn-" + Text(episode, "id"), continuationRequestRevision ?? 1,
                1, new string('b', 32), string.Empty,
                "npc-factor-" + Text(episode, "id"), 1);
            ConversationContextFact<string> clock = ConversationContextFact<string>.Verified(
                at.ToString("0.###", CultureInfo.InvariantCulture), ConversationContextFactSource.Derived, at);
            ConversationContextFact<ConversationDialogueRole> role = ConversationContextFact<ConversationDialogueRole>.Verified(
                ParseDialogueRole(Text(npc, "role")), ConversationContextFactSource.Derived, at);
            ConversationContextFact<string> sceneFact = ConversationContextFact<string>.Verified(scene, ConversationContextFactSource.Derived, at);
            ConversationContextFact<string> personality = ConversationContextFact<string>.Verified(description,
                ConversationContextFactSource.Derived, at);
            ConversationContextMood moodState = new ConversationContextMood(
                value: moodValue(npc["mood"]),
                acceptedTurnSequence: moodSequence(npc["mood"]),
                stateRevision: moodRevision(npc["mood"]));
            var commitments = new List<ConversationContextCommitment>();
            foreach (JsonObject commitment in (input["commitments"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                NpcActionKind? actionKind = null;
                if (!string.IsNullOrWhiteSpace(Text(commitment, "actionKind")))
                {
                    if (!Enum.TryParse(Text(commitment, "actionKind"), true, out NpcActionKind parsedAction) ||
                        !Enum.IsDefined(typeof(NpcActionKind), parsedAction))
                        throw new InvalidDataException("Fixture commitment has an unsupported typed action kind.");
                    actionKind = parsedAction;
                }
                commitments.Add(new ConversationContextCommitment(Text(commitment, "id"), Text(commitment, "kind"),
                    Text(commitment, "status"), commitment["revision"]?.GetValue<long>() ?? 0, actionKind,
                    OptionalDouble(commitment["startsAtGameTime"]), OptionalDouble(commitment["endsAtGameTime"])));
            }
            var snapshot = new ConversationContextSnapshot(binding, at, clock, ConversationContextFact<string>.Unknown(), role,
                sceneFact, sceneFact, ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), personality,
                npc["disposition"].GetValue<int>(), checked((uint)npc["personalitySeed"].GetValue<int>()), null,
                commitments, Array.Empty<ConversationContextSourceHandle>(), ConversationContextPersonalState.Unknown(), moodState);
            JsonObject relationship = npc["relationship"] as JsonObject ?? throw new InvalidDataException("Typed directed relationship is required.");
            var directed = NpcDirectedRelationship.Create(personId, NpcDirectedRelationship.PlayerTargetId);
            directed.Revision = relationship["revision"]?.GetValue<long>() ?? 0;
            directed.LastGameTime = relationship["lastGameTime"]?.GetValue<double>() ?? -1;
            directed.TrustKnown = relationship["trustKnown"]?.GetValue<bool>() ?? false;
            directed.ReliabilityTrust = directed.TrustKnown ? relationship["reliabilityTrust"].GetValue<int>() : 0;
            directed.TrustConfidence = relationship["trustConfidence"]?.GetValue<int>() ?? 0;
            directed.TrustEvidenceCount = relationship["trustEvidenceCount"]?.GetValue<int>() ?? 0;
            directed.Resentment = relationship["resentment"].GetValue<int>();
            directed.InterpersonalFear = relationship["interpersonalFear"].GetValue<int>();
            snapshot = snapshot.WithDirectedRelationship(directed.ToView());
            built.ContextSnapshot = snapshot;
            string context = ConversationContextFormatter.ForSocialReply(snapshot, 3072);
            string known = (world["npcKnowledge"] as JsonArray ?? new JsonArray()).ToJsonString(JsonOptions);
            var decisionOptions = ((JsonArray)input["options"]).Select(node => new NpcDecisionOption(
                Text(node as JsonObject, "id"), Text(node as JsonObject, "description"), false)).ToArray();
            var decision = new NpcDecisionRequest(built.CoreDecision.RequestId, snapshot, continuationCandidateSetRevision,
                "npc-factor-choice-review",
                ChoiceInstructions, input["playerText"].GetValue<string>(), context + "\nActor-known evidence (synthetic fixture snapshot): " + known,
                decisionOptions);
            LocalDecisionPayload wire = LocalDecisionWire.Build(decision);
            built.CoreDecision = decision;
            built.D3Payload = JsonNode.Parse(Encoding.UTF8.GetString(wire.Bytes));
            built.D3RequestBody = Encoding.UTF8.GetString(wire.Bytes);
            built.DecisionHash = wire.Sha256;
            string roleName = Text(npc, "role", "unknown");
            string history = SerializeModelHistory(input["history"]);
            string reaction = context + "\nActor-known evidence (synthetic fixture snapshot): " + known;
            var voiceRequest = new DialogueRequest(input["playerText"]?.GetValue<string>() ?? string.Empty,
                DialogueReplyFallback.UnavailableReply, "NPC", "unknown", "unknown", roleName, snapshot.EffectiveDisposition,
                true, reaction, history, string.Empty, null, null, false, 0);
            built.QwenCompletion = LocalDialogueCompletion.Build(
                new LocalDialogueConnection("http://127.0.0.1:8080/completion", 10000), voiceRequest);
            return built;
        }

        private static int moodValue(JsonNode mood) => mood?["value"]?.GetValue<int>() ?? throw new InvalidDataException("Mood value is required.");
        private static long moodSequence(JsonNode mood) => mood?["acceptedTurnSequence"]?.GetValue<long>() ?? throw new InvalidDataException("Mood turn sequence is required.");
        private static long moodRevision(JsonNode mood) => mood?["stateRevision"]?.GetValue<long>() ?? throw new InvalidDataException("Mood revision is required.");
        private static double? OptionalDouble(JsonNode value) => value == null ? (double?)null : value.GetValue<double>();

        private static ConversationDialogueRole ParseDialogueRole(string role)
        {
            switch (role)
            {
                case "managed_resident": return ConversationDialogueRole.ManagedResident;
                case "guard": return ConversationDialogueRole.Guard;
                case "shopkeeper": return ConversationDialogueRole.Shopkeeper;
                case "innkeeper": return ConversationDialogueRole.Innkeeper;
                case "traveler": return ConversationDialogueRole.Traveler;
                case "jester": return ConversationDialogueRole.Jester;
                case "bard_guild_member": return ConversationDialogueRole.BardGuildMember;
                case "royalty": return ConversationDialogueRole.Royalty;
                default: return ConversationDialogueRole.Unknown;
            }
        }

        private static int Prepare(string[] args)
        {
            string corpusName = Option(args, "--corpus");
            string output = Option(args, "--output");
            if (corpusName == "multi-turn-v2")
                return PrepareMultiTurn(output, Option(args, "--oracle-output"), Option(args, "--episode-limit"));
            if ((corpusName != "v1" && corpusName != "v2") || string.IsNullOrWhiteSpace(output)) throw new ArgumentException("prepare requires --corpus v1|v2 and --output.");
            JsonObject corpus = ReadCorpus("paired-scenarios-" + corpusName + ".json");
            string fixturePath = FixturePath("paired-scenarios-" + corpusName + ".json");
            string hash = HashFile(fixturePath);
            string fullOutput = Path.GetFullPath(output);
            string outputDirectory = Path.GetDirectoryName(fullOutput);
            if (!string.IsNullOrEmpty(outputDirectory)) Directory.CreateDirectory(outputDirectory);
            using (var stream = new FileStream(fullOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                foreach (JsonObject pair in Pairs(corpus))
                {
                    foreach (JsonObject arm in (JsonArray)pair["arms"])
                    {
                        JsonObject input = Merge((JsonObject)pair["commonInput"], arm["contextPatch"] as JsonObject);
                        BuiltRequests built = BuildRequests(pair, arm, input);
                        var record = new
                        {
                            recordType = "prepared-arm", corpusId = Text(corpus, "corpusId"), corpusSha256 = hash,
                            pairId = Text(pair, "id"), factorId = Text(pair, "factorId"), armId = Text(arm, "id"),
                            adapterStatus = Text(pair, "adapterStatus", "synthetic_contract_only"),
                            expectedNativeEligibleOptionIds = Strings(arm["expectedNativeEligibleOptionIds"]),
                            acceptableD3Choices = Strings(arm["acceptableD3Choices"]), qwenReview = arm["qwenReview"],
                            d3Request = built.D3Payload,
                            d3RequestBody = built.D3RequestBody,
                            d3RequestSha256 = built.DecisionHash,
                            coreDecisionBinding = new { requestId = built.CoreDecision.RequestId, protocol = built.CoreDecision.ProtocolVersion,
                                snapshotRevision = built.CoreDecision.SnapshotRevision, candidateSetRevision = built.CoreDecision.CandidateSetRevision,
                                personId = built.CoreDecision.Binding.PersonId },
                            qwenRequestBody = built.QwenCompletion.JsonBody,
                            requestPrompt = built.QwenCompletion.Prompt
                        };
                        writer.WriteLine(JsonSerializer.Serialize(record, JsonOptions));
                    }
                }
            }
            Console.WriteLine("Prepared offline requests for " + Text(corpus, "corpusId") + "; no model or network calls. Output: " + fullOutput);
            return 0;
        }

        private static int PrepareAppraisal(string[] args)
        {
            string outputPath = Option(args, "--output");
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("prepare-appraisal requires --output <jsonl-path>.");
            string fixtureVersion = AppraisalFixtureVersion(args);
            string fixturePath = AppraisalFixturePath(fixtureVersion);
            IReadOnlyList<AppraisalFixtureCase> cases = BuildAppraisalCases(fixtureVersion, fixturePath);
            string fixtureHash = AppraisalFixtureHash(fixtureVersion, fixturePath);
            string fixtureId = AppraisalFixtureId(fixtureVersion);
            LocalDialogueConnection connection = new LocalDialogueConnection("http://127.0.0.1:8080/completion", 300000);
            var rows = new List<string>(cases.Count);
            foreach (AppraisalFixtureCase item in cases)
            {
                DialogueCompletion completion = LocalDialogueCompletion.BuildAppraisal(connection, item.Request);
                rows.Add(JsonSerializer.Serialize(new
                {
                    recordType = "prepared-production-core-appraisal",
                    fixtureId,
                    fixtureSha256 = fixtureHash,
                    caseId = item.CaseId,
                    humanReview = new { label = item.Label },
                    requestPrompt = item.Request.Prompt,
                    requestPromptSha256 = item.InputSha256,
                    completionRequestBody = completion.JsonBody,
                    completionRequestSha256 = HashText(completion.JsonBody),
                    syntheticFixture = true,
                    nativeCommitAvailable = false
                }, JsonOptions));
            }
            WriteNewLines(outputPath, rows);
            Console.WriteLine("Prepared " + rows.Count + " production-Core appraisal completion request(s) from " + fixtureId + "; labels remain metadata, response grammar binds each request, and no synthetic case can commit native state. No model or network calls. Output: " + Path.GetFullPath(outputPath));
            return 0;
        }

        private static int ValidatePreparedAppraisal(string[] args)
        {
            string preparedPath = Option(args, "--prepared");
            if (preparedPath == null) throw new ArgumentException("validate-appraisal-prepared requires --prepared.");
            string fixtureVersion = AppraisalFixtureVersion(args);
            string fixturePath = AppraisalFixturePath(fixtureVersion);
            string fixtureHash = AppraisalFixtureHash(fixtureVersion, fixturePath);
            string fixtureId = AppraisalFixtureId(fixtureVersion);
            IReadOnlyList<AppraisalFixtureCase> cases = BuildAppraisalCases(fixtureVersion, fixturePath);
            List<JsonObject> rows = ReadJsonLines(preparedPath);
            int expectedCount = AppraisalExpectedCaseCount(fixtureVersion);
            if (cases.Count != expectedCount || rows.Count != cases.Count)
                throw new InvalidDataException("Prepared appraisal JSONL must contain exactly " + expectedCount + " cases from the current Core fixture.");
            var rowsById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonObject row in rows)
            {
                string id = Text(row, "caseId");
                if (id == null || !rowsById.TryAdd(id, row))
                    throw new InvalidDataException("Prepared appraisal case IDs must be present and unique.");
            }
            LocalDialogueConnection connection = new LocalDialogueConnection("http://127.0.0.1:8080/completion", 300000);
            foreach (AppraisalFixtureCase fixtureCase in cases)
            {
                if (!rowsById.TryGetValue(fixtureCase.CaseId, out JsonObject row))
                    throw new InvalidDataException("Missing current Core appraisal case " + fixtureCase.CaseId + ".");
                DialogueCompletion completion = LocalDialogueCompletion.BuildAppraisal(connection, fixtureCase.Request);
                string scenarioId = fixtureCase.CaseId.Substring(fixtureCase.CaseId.LastIndexOf('/') + 1);
                if (Text(row, "recordType") != "prepared-production-core-appraisal" ||
                    Text(row, "fixtureId") != fixtureId ||
                    Text(row, "fixtureSha256") != fixtureHash ||
                    Text(row, "caseId") != fixtureCase.CaseId ||
                    Text(row["humanReview"] as JsonObject, "label") != fixtureCase.Label ||
                    Text(row, "requestPrompt") != fixtureCase.Request.Prompt ||
                    Text(row, "requestPromptSha256") != fixtureCase.InputSha256 ||
                    Text(row, "completionRequestBody") != completion.JsonBody ||
                    Text(row, "completionRequestSha256") != HashText(completion.JsonBody) ||
                    Bool(row, "syntheticFixture") != true || Bool(row, "nativeCommitAvailable") != false ||
                    fixtureCase.Request.Prompt.IndexOf(scenarioId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fixtureCase.Request.Prompt.IndexOf(fixtureCase.Label, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    completion.JsonBody.IndexOf(scenarioId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    completion.JsonBody.IndexOf(fixtureCase.Label, StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidDataException(fixtureCase.CaseId + ": prepared row differs from current Core inputs or leaks review metadata.");
            }
            Console.WriteLine("Core verified all " + expectedCount + " prepared appraisal prompt and completion bytes against " + fixtureId + ". No model or network calls.");
            return 0;
        }

        private static int PrepareMultiTurn(string output, string oracleOutput, string episodeLimitText)
        {
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(oracleOutput))
                throw new ArgumentException("multi-turn-v2 requires --output and a physically separate --oracle-output.");
            const string fixtureName = "multi-turn-interactions-v2.json";
            string fixturePath = FixturePath(fixtureName);
            JsonObject corpus = ReadFixture(fixtureName);
            CheckMultiTurnFixture(corpus);
            int episodeLimit = string.IsNullOrWhiteSpace(episodeLimitText) ? int.MaxValue :
                int.Parse(episodeLimitText, NumberStyles.None, CultureInfo.InvariantCulture);
            Require(episodeLimit > 0, "--episode-limit must be a positive count.");
            string hash = HashFile(fixturePath);
            string fullOutput = Path.GetFullPath(output), fullOracle = Path.GetFullPath(oracleOutput);
            if (string.Equals(fullOutput, fullOracle, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Request and oracle outputs must be separate files.");
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput));
            Directory.CreateDirectory(Path.GetDirectoryName(fullOracle));
            var requestLines = new List<string>();
            var oracleLines = new List<string>();
            foreach (JsonObject episode in corpus["episodes"].AsArray().Take(episodeLimit))
            foreach (JsonObject arm in episode["arms"].AsArray())
            foreach (JsonObject turn in arm["turns"].AsArray())
            {
                JsonObject input = (JsonObject)turn["input"];
                BuiltRequests built = BuildMultiTurnRequests(episode, arm, turn, input);
                string requestId = Text(episode, "id") + "/" + Text(arm, "id") + "/" + Text(turn, "turnId");
                var record = new
                {
                    recordType = "prepared-multi-turn-request", contextSchemaVersion = 2,
                    corpusId = Text(corpus, "corpusId"), corpusSha256 = hash,
                    requestId, episodeId = Text(episode, "id"), factorIds = episode["factorIds"],
                    adapterStatus = Text(episode, "adapterStatus"), armId = Text(arm, "id"), turnId = Text(turn, "turnId"),
                    inputSha256 = HashText(input.ToJsonString(JsonOptions)), modelInput = input, options = input["options"],
                    d3Request = built.D3Payload, d3RequestBody = built.D3RequestBody, d3RequestSha256 = built.DecisionHash,
                    coreDecisionBinding = new { requestId = built.CoreDecision.RequestId, protocol = built.CoreDecision.ProtocolVersion,
                        snapshotRevision = built.CoreDecision.SnapshotRevision, candidateSetRevision = built.CoreDecision.CandidateSetRevision,
                        personId = built.CoreDecision.Binding.PersonId },
                    contextReplayMode = "fixed_snapshot_replay"
                };
                requestLines.Add(JsonSerializer.Serialize(record, JsonOptions));
                oracleLines.Add(JsonSerializer.Serialize(new { recordType = "evaluator-only-oracle", corpusId = Text(corpus, "corpusId"),
                    corpusSha256 = hash, requestId, episodeId = Text(episode, "id"), armId = Text(arm, "id"), turnId = Text(turn, "turnId"),
                    adapterStatus = Text(episode, "adapterStatus"), initialMechanicalState = arm["initialMechanicalState"], oracle = turn["oracle"] }, JsonOptions));
            }
            WriteNewLines(fullOutput, requestLines);
            WriteNewLines(fullOracle, oracleLines);
            Console.WriteLine("Prepared " + requestLines.Count + " offline multi-turn requests from " + Math.Min(episodeLimit, corpus["episodes"].AsArray().Count) +
                " matched episode(s); oracle rows are separate. No model or network calls. Requests: " + fullOutput);
            return 0;
        }

        private static int ComposeQwen(string[] args)
        {
            string preparedPath = Option(args, "--prepared"), d3Path = Option(args, "--d3-results"), outputPath = Option(args, "--output");
            if (string.IsNullOrWhiteSpace(preparedPath) || string.IsNullOrWhiteSpace(d3Path) || string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("compose-qwen requires --prepared, --d3-results, and --output.");
            List<JsonObject> prepared = ReadJsonLines(preparedPath);
            List<JsonObject> d3Rows = ReadJsonLines(d3Path);
            if (prepared.Count == 0 || prepared.Count != d3Rows.Count)
                throw new InvalidDataException("Prepared rows and D3 evidence must have equal nonzero row counts.");
            JsonObject corpus = ReadFixture("multi-turn-interactions-v2.json");
            CheckMultiTurnFixture(corpus);
            string corpusHash = HashFile(FixturePath("multi-turn-interactions-v2.json"));
            var fixtureTurns = new Dictionary<string, Tuple<JsonObject, JsonObject, JsonObject>>(StringComparer.Ordinal);
            foreach (JsonObject episode in corpus["episodes"].AsArray())
            foreach (JsonObject arm in episode["arms"].AsArray())
            foreach (JsonObject turn in arm["turns"].AsArray())
                fixtureTurns.Add(Text(episode, "id") + "/" + Text(arm, "id") + "/" + Text(turn, "turnId"),
                    Tuple.Create(episode, arm, turn));

            var output = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < prepared.Count; index++)
            {
                JsonObject row = prepared[index], d3Row = d3Rows[index];
                string requestId = Text(row, "requestId");
                if (requestId != Text(d3Row, "requestId") || !seen.Add(requestId) || !fixtureTurns.TryGetValue(requestId, out var fixture))
                    throw new InvalidDataException("Prepared/D3 request ordering or fixture identity mismatch.");
                JsonObject input = row["modelInput"] as JsonObject ?? throw new InvalidDataException("Prepared modelInput snapshot missing.");
                string fixtureInput = fixture.Item3["input"].ToJsonString(JsonOptions);
                if (Text(row, "corpusSha256") != corpusHash || input.ToJsonString(JsonOptions) != fixtureInput ||
                    HashText(fixtureInput) != Text(row, "inputSha256"))
                    throw new InvalidDataException(requestId + ": source fixture or modelInput snapshot mismatch.");
                BuiltRequests built = BuildMultiTurnRequests(fixture.Item1, fixture.Item2, fixture.Item3, input);
                if (built.D3RequestBody != Text(row, "d3RequestBody") || built.D3RequestBody != Text(d3Row, "rawRequest") ||
                    built.DecisionHash != Text(row, "d3RequestSha256"))
                    throw new InvalidDataException(requestId + ": reconstructed production D3 body differs from prepared bytes.");
                string rawResponse = Text(d3Row, "rawResponse", string.Empty);
                LocalDecisionParsedReply parsed = LocalDecisionWire.Parse(rawResponse);
                NpcDecisionResult validated = parsed.IsValid
                    ? NpcDecisionResult.ValidateResponse(built.CoreDecision, parsed.SelectedOptionId, parsed.Probabilities,
                        parsed.ReportedConfidence, built.DecisionHash, parsed.ModelId,
                        Double(d3Row, "elapsedMs", 0), NullableInt(d3Row["httpStatus"]), built.CoreDecision.Binding,
                        built.CoreDecision.SnapshotRevision, built.CoreDecision.CandidateSetRevision, built.CoreDecision.Options)
                    : null;
                bool accepted = validated != null && validated.Status == NpcDecisionStatus.Ok;
                string qwenBody = null;
                if (accepted)
                {
                    DialogueCompletion completion = BuildMultiTurnQwenCompletion(built, input, validated.SelectedOptionId);
                    qwenBody = completion.JsonBody;
                }
                var probabilities = parsed.IsValid && parsed.Probabilities != null
                    ? parsed.Probabilities.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) : null;
                output.Add(JsonSerializer.Serialize(new
                {
                    requestId,
                    validated = accepted,
                    selectedOptionId = accepted ? validated.SelectedOptionId : null,
                    decisionValidation = new
                    {
                        status = validated?.Status.ToString() ?? "Invalid",
                        failureKind = validated?.FailureKind.ToString() ?? parsed.FailureKind.ToString(),
                        diagnosticCode = validated?.DiagnosticCode ?? parsed.DiagnosticCode,
                        requestSha256 = built.DecisionHash,
                        modelId = parsed.ModelId,
                        probabilities,
                        confidence = parsed.ReportedConfidence,
                        latencyMs = Double(d3Row, "elapsedMs", 0),
                        httpStatus = NullableInt(d3Row["httpStatus"])
                    },
                    qwenRequestBody = qwenBody
                }, JsonOptions));
            }
            WriteNewLines(outputPath, output);
            Console.WriteLine("Core validated " + prepared.Count + " D3 stage result(s); Qwen bodies built only for current valid choices. No model or network calls.");
            return 0;
        }

        private static DialogueCompletion BuildMultiTurnQwenCompletion(BuiltRequests built, JsonObject input,
            string selectedOptionId, LocalDialogueConnection connection = null, bool structuredMood = false)
        {
            if (built?.ContextSnapshot == null || string.IsNullOrWhiteSpace(selectedOptionId))
                throw new InvalidDataException("Qwen stage requires a typed Core snapshot and validated D3 choice.");
            JsonObject option = (input["options"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .SingleOrDefault(candidate => Text(candidate, "id") == selectedOptionId);
            if (option == null) throw new InvalidDataException("Validated D3 option is absent from the current offered choices.");
            string context = ConversationContextFormatter.ForSocialReply(built.ContextSnapshot, 3072);
            string known = ((input["world"]?["npcKnowledge"] as JsonArray) ?? new JsonArray()).ToJsonString(JsonOptions);
            string reaction = context + "\nActor-known evidence (synthetic fixture snapshot): " + known +
                "\nD3 conversational preference candidate: " + selectedOptionId + " — " + Text(option, "description") +
                ". This is only a validated model preference among supplied conversational choices. It is not a native willingness result, an accepted task, or an executed game effect. Respond naturally to the player while preserving uncertainty and the NPC's own agency.";
            JsonObject npc = input["npc"] as JsonObject ?? throw new InvalidDataException("Prepared NPC snapshot missing.");
            string history = SerializeModelHistory(input["history"]);
            var request = new DialogueRequest(input["playerText"]?.GetValue<string>() ?? string.Empty,
                DialogueReplyFallback.UnavailableReply, "NPC", "unknown", "unknown", Text(npc, "role", "unknown"),
                built.ContextSnapshot.EffectiveDisposition, true, reaction, history, string.Empty, null, null,
                moodEnabled: structuredMood,
                moodValue: structuredMood ? moodValue(npc["mood"]) : 0);
            return LocalDialogueCompletion.Build(connection ??
                new LocalDialogueConnection("http://127.0.0.1:8080/completion", 10000), request);
        }

        private static void RequireStructuredMoodQwenRequest(byte[] body, string caseId)
        {
            JsonObject request = JsonNode.Parse(new UTF8Encoding(false, true).GetString(body)) as JsonObject;
            string prompt = Text(request, "prompt", string.Empty);
            string grammar = Text(request, "grammar", string.Empty);
            Require(grammar == SocialDialogueResult.Grammar(),
                caseId + ": continuation Qwen request must use the production SocialDialogueResult grammar.");
            Require(prompt.Contains("Current persistent mood value:", StringComparison.Ordinal) &&
                prompt.Contains("Choose a small moodDelta from -10 to 10", StringComparison.Ordinal) &&
                prompt.Contains("Return exactly one JSON object with fields reply", StringComparison.Ordinal),
                caseId + ": continuation Qwen prompt must include production mood instructions and structured reply contract.");
        }

        private sealed class SocialContinuationTurn
        {
            public readonly string TurnId;
            public readonly JsonObject Input;
            public readonly string Source;
            public SocialContinuationTurn(string turnId, JsonObject input, string source)
            { TurnId = turnId; Input = input; Source = source; }
        }

        private sealed class SocialContinuationHttpResult
        {
            public readonly int? Status;
            public readonly byte[] Body;
            public readonly double ElapsedMs;
            public readonly string Error;
            public SocialContinuationHttpResult(int? status, byte[] body, double elapsedMs, string error)
            { Status = status; Body = body ?? Array.Empty<byte>(); ElapsedMs = elapsedMs; Error = error; }
        }

        private delegate Task<SocialContinuationHttpResult> SocialContinuationTransport(Uri endpoint, byte[] body,
            CancellationToken cancellationToken, NpcExperienceAppraisalRequest appraisalRequest);

        private sealed class SocialContinuationReport
        {
            public int CompletedTurns;
            public string StopReason;
            public ConversationMoodView FinalMood;
            public WorldState FinalState;
            public WorldStateStore Store;
            public string PersonId;
            public double FinalGameTime;
            public readonly List<JsonObject> Records = new List<JsonObject>();
        }

        private sealed class PostChatRunResult
        {
            public int AppraisalsCommitted;
            public bool ReopenCompleted;
            public string StopReason = "completed";
        }

        private static readonly HttpClient ContinuationHttpClient = new HttpClient(
            new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMinutes(5) };
        private static readonly HttpClient ContinuationMetadataHttpClient = new HttpClient(
            new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(10) };

        private static int ContinueSocial(string[] args)
        {
            string episodeId = Option(args, "--episode-id");
            string armId = Option(args, "--arm-id");
            string outputPath = Option(args, "--output");
            JsonObject fixture = ReadFixture("multi-turn-interactions-v2.json");
            if (Text(fixture, "corpusId") != "npc-complex-interactions-v2" ||
                fixture["contextSchemaVersion"]?.GetValue<int>() != 2)
                throw new InvalidDataException("Closed-loop social mode requires the frozen typed multi-turn v2 corpus.");
            JsonObject episode = fixture["episodes"].AsArray().OfType<JsonObject>()
                .SingleOrDefault(item => Text(item, "id") == episodeId)
                ?? throw new ArgumentException("Unknown --episode-id in the frozen multi-turn-v2 fixture.");
            JsonObject arm = episode["arms"].AsArray().OfType<JsonObject>()
                .SingleOrDefault(item => Text(item, "id") == armId)
                ?? throw new ArgumentException("Unknown --arm-id for the selected episode.");
            JsonArray authored = arm["turns"] as JsonArray ?? throw new InvalidDataException("Selected arm has no turns.");
            if (authored.Count < 1 || authored.Count > 3)
                throw new InvalidDataException("Closed-loop social mode supports one to three authored turns.");
            var turns = authored.OfType<JsonObject>().Select(item => new SocialContinuationTurn(
                Text(item, "turnId"), item["input"] as JsonObject, "fixture-authored")).ToList();
            string extraText = Option(args, "--extra-player-text");
            if (!string.IsNullOrWhiteSpace(extraText))
            {
                if (turns.Count >= 3 || extraText.Length > 1024)
                    throw new ArgumentException("One extra player turn is allowed, up to three turns total and 1024 characters.");
                JsonObject extra = (JsonObject)turns[turns.Count - 1].Input.DeepClone();
                extra["playerText"] = extraText;
                turns.Add(new SocialContinuationTurn("caller-extra-1", extra, "caller-appended-player-text"));
            }
            ValidateSocialContinuationInputs(turns);
            string corpusHash = HashFile(FixturePath("multi-turn-interactions-v2.json"));
            string d3Model = Option(args, "--expect-d3-model");
            string qwenModel = Option(args, "--expect-qwen-model");
            if (!Flag(args, "--execute"))
            {
                Console.WriteLine(new JsonObject
                {
                    ["mode"] = "preflight-only", ["executeRequiredForNetwork"] = true,
                    ["episodeId"] = episodeId, ["armId"] = armId, ["turnCount"] = turns.Count,
                    ["corpusSha256"] = corpusHash, ["outputPath"] = outputPath,
                    ["expectedD3Model"] = d3Model, ["expectedQwenModel"] = qwenModel,
                    ["networkCalls"] = 0
                }.ToJsonString(JsonOptions));
                return 0;
            }
            if (string.IsNullOrWhiteSpace(outputPath) || string.IsNullOrWhiteSpace(d3Model) || string.IsNullOrWhiteSpace(qwenModel))
                throw new ArgumentException("--execute requires --output, --expect-d3-model, and --expect-qwen-model.");
            if (turns.Count != 3)
                throw new ArgumentException("Live closed-loop execution requires exactly three social turns; add --extra-player-text for this two-turn fixture arm.");
            Uri d3Endpoint = new LocalDialogueConnection(Option(args, "--d3-url") ??
                "http://127.0.0.1:8093/v1/systemone", 300000).Endpoint;
            Uri qwenEndpoint = new LocalDialogueConnection(Option(args, "--qwen-url") ??
                "http://127.0.0.1:8080/completion", 300000).Endpoint;
            if (d3Endpoint.Scheme != Uri.UriSchemeHttp || qwenEndpoint.Scheme != Uri.UriSchemeHttp ||
                !d3Endpoint.IsLoopback || !qwenEndpoint.IsLoopback)
                throw new InvalidOperationException("This evaluator mode permits unauthenticated loopback HTTP endpoints only.");
            SocialServerProbe d3Probe = ProbeSocialServer(d3Endpoint, d3Model);
            SocialServerProbe qwenProbe = ProbeSocialServer(qwenEndpoint, qwenModel);
            if (!d3Probe.Compatible || !qwenProbe.Compatible)
                throw new InvalidOperationException("Live server identity preflight failed: D3=" + d3Probe.Failure + "; Qwen=" + qwenProbe.Failure);
            string fullOutput = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput));
            using (var file = new FileStream(fullOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
            {
                var runHeader = new JsonObject
                {
                    ["recordType"] = "closed-loop-social-run",
                    ["runId"] = Guid.NewGuid().ToString("N"), ["corpusSha256"] = corpusHash,
                    ["episodeId"] = episodeId, ["armId"] = armId,
                    ["contextReplayMode"] = "core_state_closed_loop",
                    ["fixtureTurnCount"] = authored.Count, ["executedTurnCount"] = turns.Count,
                    ["extraPlayerTextPresent"] = !string.IsNullOrWhiteSpace(extraText),
                    ["d3Endpoint"] = d3Endpoint.ToString(), ["qwenEndpoint"] = qwenEndpoint.ToString(),
                    ["expectedD3Model"] = d3Model, ["expectedQwenModel"] = qwenModel,
                    ["d3ServerIdentity"] = d3Probe.Metadata?.DeepClone(),
                    ["qwenServerIdentity"] = qwenProbe.Metadata?.DeepClone(),
                    ["profileIdentitySha256"] = HashText(InitialProfileHashMaterial(turns[0].Input)),
                    ["initialStateSeed"] = BuildInitialStateSeedRecord(turns[0].Input),
                    ["nativeActionsEnabled"] = false, ["oracleSemanticsRead"] = false,
                    ["oracleFieldsIncludedInRequests"] = false, ["retryPolicy"] = "none"
                };
                writer.WriteLine(runHeader.ToJsonString(JsonOptions));
                writer.Flush();
                SocialContinuationReport report = RunSocialContinuationAsync(episode, arm, turns,
                    PostSocialContinuationAsync, d3Endpoint, qwenEndpoint, d3Model, qwenModel, record =>
                    {
                        writer.WriteLine(record.ToJsonString(JsonOptions));
                        writer.Flush();
                    }).GetAwaiter().GetResult();
                Console.WriteLine(new JsonObject
                {
                    ["completedTurns"] = report.CompletedTurns, ["stopReason"] = report.StopReason,
                    ["finalMoodValue"] = report.FinalMood.Value,
                    ["finalAcceptedTurnSequence"] = report.FinalMood.AcceptedTurnSequence,
                    ["finalMoodRevision"] = report.FinalMood.Revision,
                    ["outputPath"] = fullOutput
                }.ToJsonString(JsonOptions));
                if (report.StopReason != "completed") return 1;
            }
            return 0;
        }

        private static int ContinueSocialAppraiseReopen(string[] args)
        {
            const string episodeId = "interaction.threat-quotation-and-repeated-abuse";
            const string armId = "speech.direct-threat-repeated";
            string outputPath = Option(args, "--output");
            string reopenText = Option(args, "--reopen-player-text");
            JsonObject fixture = ReadFixture("multi-turn-interactions-v2.json");
            JsonObject episode = fixture["episodes"].AsArray().OfType<JsonObject>().Single(item => Text(item, "id") == episodeId);
            JsonObject arm = episode["arms"].AsArray().OfType<JsonObject>().Single(item => Text(item, "id") == armId);
            JsonArray authored = arm["turns"] as JsonArray ?? throw new InvalidDataException("Threat arm has no turns.");
            if (authored.Count != 2) throw new InvalidDataException("Approved threat probe expects exactly two frozen authored turns.");
            var turns = authored.OfType<JsonObject>().Select(item => new SocialContinuationTurn(
                Text(item, "turnId"), item["input"] as JsonObject, "fixture-authored")).ToList();
            string extraThreat = Option(args, "--extra-player-text");
            if (string.IsNullOrWhiteSpace(extraThreat))
                throw new ArgumentException("This bounded run requires --extra-player-text for the third, observer-directed speech receipt.");
            if (string.IsNullOrWhiteSpace(reopenText))
                throw new ArgumentException("This bounded run requires a caller-supplied non-action --reopen-player-text.");
            if (extraThreat.Length > 1024 || reopenText.Length > 1024)
                throw new ArgumentException("Player text exceeds the 1024-character Core bound.");
            JsonObject extra = (JsonObject)turns[1].Input.DeepClone();
            extra["playerText"] = extraThreat;
            turns.Add(new SocialContinuationTurn("caller-third-threat", extra, "caller-appended-player-text"));
            ValidateSocialContinuationInputs(turns);
            string d3Model = Option(args, "--expect-d3-model");
            string qwenModel = Option(args, "--expect-qwen-model");
            if (!Flag(args, "--execute"))
            {
                Console.WriteLine(new JsonObject
                {
                    ["mode"] = "preflight-only", ["executeRequiredForNetwork"] = true,
                    ["episodeId"] = episodeId, ["armId"] = armId, ["socialTurns"] = 3,
                    ["appraisalRowsExpected"] = 3, ["reopenTurn"] = true,
                    ["outputPath"] = outputPath, ["expectedD3Model"] = d3Model,
                    ["expectedQwenModel"] = qwenModel, ["networkCalls"] = 0,
                    ["oracleFieldsIncluded"] = false, ["retryPolicy"] = "none"
                }.ToJsonString(JsonOptions));
                return 0;
            }
            if (string.IsNullOrWhiteSpace(outputPath) || string.IsNullOrWhiteSpace(d3Model) || string.IsNullOrWhiteSpace(qwenModel))
                throw new ArgumentException("--execute requires --output, --expect-d3-model, and --expect-qwen-model.");
            Uri d3Endpoint = new LocalDialogueConnection(Option(args, "--d3-url") ??
                "http://127.0.0.1:8093/v1/systemone", 300000).Endpoint;
            Uri qwenEndpoint = new LocalDialogueConnection(Option(args, "--qwen-url") ??
                "http://127.0.0.1:8080/completion", 300000).Endpoint;
            SocialServerProbe d3Probe = ProbeSocialServer(d3Endpoint, d3Model);
            SocialServerProbe qwenProbe = ProbeSocialServer(qwenEndpoint, qwenModel);
            if (!d3Probe.Compatible || !qwenProbe.Compatible)
                throw new InvalidOperationException("Live server identity preflight failed: D3=" + d3Probe.Failure + "; Qwen=" + qwenProbe.Failure);
            if (NullableInt(qwenProbe.Metadata?["nContext"]) != AppraisalContextTokens)
                throw new InvalidOperationException("Post-chat appraisal requires pinned Qwen context n_ctx=4096 for the 3072+1024 token budget.");

            string fullOutput = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput));
            using (var file = new FileStream(fullOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
            using (var cancellation = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler cancelHandler = (sender, eventArgs) =>
                { eventArgs.Cancel = true; cancellation.Cancel(); };
                Console.CancelKeyPress += cancelHandler;
                try
                {
                    void Record(JsonObject row) { writer.WriteLine(row.ToJsonString(JsonOptions)); writer.Flush(); }
                    Record(new JsonObject
                    {
                        ["recordType"] = "closed-loop-social-appraise-reopen-run",
                        ["runId"] = Guid.NewGuid().ToString("N"),
                        ["corpusSha256"] = HashFile(FixturePath("multi-turn-interactions-v2.json")),
                        ["episodeId"] = episodeId, ["armId"] = armId,
                        ["contextReplayMode"] = "core_state_closed_loop_with_simulated_idle_appraisal",
                        ["d3Endpoint"] = d3Endpoint.ToString(), ["qwenEndpoint"] = qwenEndpoint.ToString(),
                        ["qwenTokenizeEndpoint"] = AppraisalTokenizeEndpoint(qwenEndpoint).ToString(),
                        ["appraisalContextTokens"] = AppraisalContextTokens,
                        ["appraisalResponseTokens"] = AppraisalResponseTokens,
                        ["appraisalPromptTokenBudget"] = AppraisalPromptTokenBudget,
                        ["expectedD3Model"] = d3Model, ["expectedQwenModel"] = qwenModel,
                        ["d3ServerIdentity"] = d3Probe.Metadata?.DeepClone(),
                        ["qwenServerIdentity"] = qwenProbe.Metadata?.DeepClone(),
                        ["profileIdentitySha256"] = HashText(InitialProfileHashMaterial(turns[0].Input)),
                        ["initialStateSeed"] = BuildInitialStateSeedRecord(turns[0].Input),
                        ["initialEffectiveDispositionBaseline"] = turns[0].Input["npc"]?["disposition"]?.DeepClone(),
                        ["syntheticDispositionBaselineOffset"] = 0,
                        ["extraPlayerThreat"] = extraThreat, ["reopenPlayerText"] = reopenText,
                        ["nativeActionsEnabled"] = false, ["oracleSemanticsRead"] = false,
                        ["oracleFieldsIncludedInRequests"] = false, ["retryPolicy"] = "none",
                        ["idleBarrier"] = "simulated_after_three_accepted_social_turns",
                        ["appraisalState"] = "same_world_state_store_as_social_turns"
                    });
                    SocialContinuationReport social = RunSocialContinuationAsync(episode, arm, turns,
                        PostSocialContinuationAsync, d3Endpoint, qwenEndpoint, d3Model, qwenModel, Record,
                        cancellation.Token, seedDispositionBaseline: true, staggerReceiptTimes: true).GetAwaiter().GetResult();
                    if (social.StopReason != "completed" || social.CompletedTurns != 3 || cancellation.IsCancellationRequested)
                    {
                        Record(new JsonObject { ["recordType"] = "post-chat-phase-stopped", ["phase"] = "social",
                            ["failureKind"] = cancellation.IsCancellationRequested ? "cancelled" : social.StopReason,
                            ["completedTurns"] = social.CompletedTurns, ["stateSha256"] = HashText(SerializeWorldState(social.FinalState)) });
                        return 1;
                    }
                    Record(new JsonObject
                    {
                        ["recordType"] = "simulated-idle-barrier", ["phase"] = "post-chat",
                        ["barrier"] = "evaluator_has_no_foreground_request_or_native_talk_window",
                        ["nativeSchedulerAcceptance"] = false, ["outstandingEvaluatorTransport"] = false,
                        ["state"] = StateEvidence(social.Store, social.PersonId, social.FinalGameTime)
                    });
                    PostChatRunResult phase = RunPostChatAppraisalAndReopenAsync(episode, arm, turns,
                        social, reopenText, PostSocialContinuationAsync, d3Endpoint,
                        qwenEndpoint, d3Model, qwenModel, Record, cancellation.Token).GetAwaiter().GetResult();
                    Console.WriteLine(new JsonObject
                    {
                        ["completedSocialTurns"] = social.CompletedTurns,
                        ["appraisalsCommitted"] = phase.AppraisalsCommitted,
                        ["reopenCompleted"] = phase.ReopenCompleted,
                        ["stopReason"] = phase.StopReason, ["outputPath"] = fullOutput
                    }.ToJsonString(JsonOptions));
                    return phase.StopReason == "completed" ? 0 : 1;
                }
                finally { Console.CancelKeyPress -= cancelHandler; }
            }
        }

        private static async Task<PostChatRunResult> RunPostChatAppraisalAndReopenAsync(JsonObject episode,
            JsonObject arm, IReadOnlyList<SocialContinuationTurn> socialTurns, SocialContinuationReport social,
            string reopenText, SocialContinuationTransport transport,
            Uri d3Endpoint, Uri qwenEndpoint, string expectedD3Model, string expectedQwenModel,
            Action<JsonObject> record, CancellationToken cancellationToken)
        {
            var result = new PostChatRunResult();
            WorldStateStore store = social?.Store;
            string personId = social?.PersonId;
            if (store == null || string.IsNullOrWhiteSpace(personId) || socialTurns == null || socialTurns.Count != 3)
                throw new ArgumentException("Post-chat appraisal requires the same live three-turn Core store.");
            double captureAt = social.FinalGameTime + 1;
            JsonObject idleBefore = StateEvidence(store, personId, captureAt);
            record(new JsonObject
            {
                ["recordType"] = "appraisal-phase-start", ["personId"] = personId,
                ["capturedGameTime"] = captureAt, ["idleBarrier"] = "simulated",
                ["pendingSpeechRows"] = store.Snapshot().NpcExperienceInbox.ReadPending(personId, 100).Count,
                ["stateBefore"] = idleBefore.DeepClone()
            });
            IReadOnlyList<NpcExperienceEvent> pending = store.Snapshot().NpcExperienceInbox.ReadPending(personId, 100);
            if (pending.Count != 3 || pending.Any(item => item.Origin != NpcExperienceEventOrigin.AttributedSpeech))
            {
                result.StopReason = "pending_speech_count_mismatch";
                record(new JsonObject { ["recordType"] = "appraisal-phase-stopped", ["failureKind"] = result.StopReason,
                    ["expectedPendingSpeechRows"] = 3, ["actualPendingSpeechRows"] = pending.Count,
                    ["onlyAttributedSpeechAccepted"] = true,
                    ["stateAfter"] = StateEvidence(store, personId, captureAt) });
                return result;
            }
            for (int index = 0; index < pending.Count; index++)
            {
                string stateBefore = SerializeWorldState(store.Snapshot());
                string beforeHash = HashText(stateBefore);
                var row = new JsonObject
                {
                    ["recordType"] = "post-chat-speech-appraisal",
                    ["phase"] = "simulated-idle", ["attemptIndex"] = index,
                    ["personId"] = personId, ["stateBeforeSha256"] = beforeHash,
                    ["stateBefore"] = stateBefore,
                    ["expectedOldestEvidenceDeduplicationKey"] = pending[index].DeduplicationKey,
                    ["expectedCanonicalEvidenceId"] = pending[index].Origin == NpcExperienceEventOrigin.AttributedSpeech
                        ? pending[index].UtteranceId : pending[index].OutcomeId,
                    ["capturedGameTime"] = captureAt, ["retryPolicy"] = "none"
                };
                string failure = null;
                NpcExperienceAppraisalTicket ticket = null;
                try
                {
                    if (cancellationToken.IsCancellationRequested) failure = "cancelled_before_capture";
                    else if (!store.TryCaptureNpcExperienceAppraisal(personId, captureAt, out ticket) || ticket?.Request == null)
                        failure = "core_capture_rejected";
                    else if (ticket.Request.EvidenceDeduplicationKey != pending[index].DeduplicationKey ||
                        ticket.Request.CanonicalEvidenceId != row["expectedCanonicalEvidenceId"]?.GetValue<string>())
                        failure = "core_capture_order_mismatch";
                    if (failure == null)
                    {
                        var speech = NpcSpeechActRequest.ForAppraisal(ticket.Request);
                        string speechPayload = LocalSpeechActWire.Build(speech);
                        var speechHttp = await transport(d3Endpoint, Encoding.UTF8.GetBytes(speechPayload), cancellationToken, null).ConfigureAwait(false);
                        string speechRaw = DecodeResponse(speechHttp.Body);
                        row["speechClassification"] = new JsonObject { ["rawRequest"] = speechPayload,
                            ["requestBodySha256"] = HashText(speechPayload), ["rawResponse"] = speechRaw,
                            ["responseBodySha256"] = HashText(speechRaw), ["httpStatus"] = speechHttp.Status,
                            ["elapsedMs"] = speechHttp.ElapsedMs };
                        if (cancellationToken.IsCancellationRequested) failure = "cancelled_after_speech_classification";
                        else if (speechHttp.Status != 200 || speechHttp.Error != null ||
                            !LocalSpeechActWire.TryParse(speech, expectedD3Model, speechRaw, out var classification) ||
                            !store.TryBindNpcExperienceSpeechClassification(ticket, classification)) failure = "speech_classification_rejected";
                    }
                    if (failure == null)
                    {
                        NpcExperienceAppraisalRequest appraisalRequest = ticket.Request;
                        if (!appraisalRequest.RequiresSpeechAppraisal ||
                            appraisalRequest.AlreadyAppliedEffectsMask != NpcExperienceAppliedEffectsMask.Mood)
                            failure = "unexpected_already_applied_effects_mask";
                        else
                        {
                            DialogueCompletion completion = LocalDialogueCompletion.BuildAppraisal(
                                new LocalDialogueConnection(qwenEndpoint.ToString(), 300000), appraisalRequest);
                            byte[] requestBytes = new UTF8Encoding(false, true).GetBytes(completion.JsonBody);
                            row["appraisalRequest"] = new JsonObject
                            {
                                ["endpoint"] = qwenEndpoint.ToString(),
                                ["bodySha256"] = HashText(completion.JsonBody),
                                ["promptSha256"] = HashText(appraisalRequest.Prompt),
                                ["rawRequest"] = completion.JsonBody,
                                ["rawRequestBase64"] = Convert.ToBase64String(requestBytes),
                                ["prompt"] = appraisalRequest.Prompt,
                                ["grammar"] = Text(TryObject(completion.JsonBody), "grammar"),
                                ["responseTokens"] = completion.ResponseTokens,
                                ["alreadyAppliedEffectsMask"] = appraisalRequest.AlreadyAppliedEffectsMask.ToString(),
                                ["evidenceDeduplicationKey"] = appraisalRequest.EvidenceDeduplicationKey,
                                ["canonicalEvidenceId"] = appraisalRequest.CanonicalEvidenceId,
                                ["observedGameTime"] = pending[index].ObservedGameTime,
                                ["capturedGameTime"] = appraisalRequest.CapturedGameTime,
                                ["currentMood"] = appraisalRequest.CurrentMood,
                                ["currentAffinity"] = appraisalRequest.CurrentAffinity,
                                ["relationshipTargetId"] = appraisalRequest.RelationshipTargetId,
                                ["expectedMoodRevision"] = appraisalRequest.ExpectedMoodRevision,
                                ["expectedAffinityRevision"] = appraisalRequest.ExpectedAffinityRevision,
                                ["expectedRelationshipRevision"] = appraisalRequest.ExpectedRelationshipRevision,
                                ["expectedMotivationRevision"] = appraisalRequest.ExpectedMotivationRevision,
                                ["expectedKnowledgeRevision"] = appraisalRequest.ExpectedKnowledgeRevision
                            };
                            byte[] tokenizeRequestBytes = BuildAppraisalTokenizeRequest(appraisalRequest.Prompt);
                            string tokenizeRequestText = new UTF8Encoding(false, true).GetString(tokenizeRequestBytes);
                            var tokenizeEvidence = new JsonObject
                            {
                                ["endpoint"] = AppraisalTokenizeEndpoint(qwenEndpoint).ToString(),
                                ["requestBodySha256"] = HashBytes(tokenizeRequestBytes),
                                ["requestPromptSha256"] = HashText(appraisalRequest.Prompt),
                                ["rawRequest"] = tokenizeRequestText,
                                ["rawRequestBase64"] = Convert.ToBase64String(tokenizeRequestBytes),
                                ["contextTokens"] = AppraisalContextTokens,
                                ["responseTokens"] = completion.ResponseTokens,
                                ["inputBudget"] = AppraisalPromptTokenBudget
                            };
                            row["appraisalTokenize"] = tokenizeEvidence;
                            row["appraisalHttp"] = null;
                            if (completion.ResponseTokens != AppraisalResponseTokens)
                            {
                                failure = "appraisal_response_budget_mismatch";
                                tokenizeEvidence["failureKind"] = failure;
                            }
                            else if (cancellationToken.IsCancellationRequested)
                            {
                                failure = "cancelled_before_tokenize";
                                tokenizeEvidence["failureKind"] = failure;
                            }
                            else
                            {
                                SocialContinuationHttpResult tokenizeHttp = await transport(AppraisalTokenizeEndpoint(qwenEndpoint),
                                    tokenizeRequestBytes, cancellationToken, appraisalRequest).ConfigureAwait(false);
                                string tokenizeRaw = DecodeResponse(tokenizeHttp.Body);
                                tokenizeEvidence["httpStatus"] = tokenizeHttp.Status;
                                tokenizeEvidence["elapsedMs"] = tokenizeHttp.ElapsedMs;
                                tokenizeEvidence["transportError"] = tokenizeHttp.Error;
                                tokenizeEvidence["rawResponse"] = tokenizeRaw;
                                tokenizeEvidence["rawResponseBase64"] = Convert.ToBase64String(tokenizeHttp.Body ?? Array.Empty<byte>());
                                tokenizeEvidence["responseBodySha256"] = HashBytes(tokenizeHttp.Body ?? Array.Empty<byte>());
                                bool tokenizerResponseValid = TryParseAppraisalTokenCount(tokenizeRaw, out int tokenCount, out string tokenizerError);
                                if (tokenizeHttp.Status != 200 || tokenizeHttp.Error != null)
                                    failure = "appraisal_tokenize_transport_error";
                                else if (!tokenizerResponseValid)
                                {
                                    failure = "appraisal_tokenize_response_invalid";
                                    tokenizeEvidence["parseError"] = tokenizerError;
                                }
                                else
                                {
                                    bool fits = tokenCount <= AppraisalPromptTokenBudget &&
                                        (long)tokenCount + completion.ResponseTokens <= AppraisalContextTokens;
                                    tokenizeEvidence["tokenCount"] = tokenCount;
                                    tokenizeEvidence["withinContextBudget"] = fits;
                                    if (!fits) failure = "appraisal_tokenize_over_budget";
                                }
                                if (cancellationToken.IsCancellationRequested && failure == null)
                                    failure = "cancelled_after_tokenize";
                                tokenizeEvidence["failureKind"] = failure;
                            }
                            if (failure == null)
                            {
                                if (cancellationToken.IsCancellationRequested) failure = "cancelled_before_transport";
                                else
                                {
                                    SocialContinuationHttpResult http = await transport(qwenEndpoint, requestBytes, cancellationToken, appraisalRequest).ConfigureAwait(false);
                                    string raw = DecodeResponse(http.Body);
                                    string model = ResponseModel(raw);
                                    string content = ResponseContent(raw);
                                    row["appraisalHttp"] = new JsonObject
                                    {
                                        ["httpStatus"] = http.Status, ["elapsedMs"] = http.ElapsedMs,
                                        ["transportError"] = http.Error, ["modelId"] = model,
                                        ["rawResponse"] = raw,
                                        ["rawResponseBase64"] = Convert.ToBase64String(http.Body ?? Array.Empty<byte>()),
                                        ["responseContent"] = content
                                    };
                                    if (cancellationToken.IsCancellationRequested) failure = "cancelled_after_transport";
                                    else if (http.Status != 200 || http.Error != null) failure = "appraisal_transport_error";
                                    else if (!string.Equals(model, expectedQwenModel, StringComparison.Ordinal)) failure = "appraisal_model_identity_mismatch";
                                    else if (content == null) failure = "appraisal_completion_envelope_invalid";
                                    else
                                    {
                                        bool parsed = NpcExperienceAppraisalContract.TryParseResponse(appraisalRequest, content,
                                            out NpcExperienceAppraisal parsedAppraisal);
                                        bool noAction = parsed && parsedAppraisal.ConversationOption == NpcExperienceConversationOption.NoAction;
                                        row["coreParserAccepted"] = parsed;
                                        row["coreNoActionAccepted"] = noAction;
                                        if (!noAction) failure = "core_appraisal_parser_rejected";
                                        else if (cancellationToken.IsCancellationRequested) failure = "cancelled_before_commit";
                                        else
                                        {
                                            bool committed = store.TryCommitNpcExperienceAppraisal(ticket, content);
                                            row["coreCommitAccepted"] = committed;
                                            if (!committed) failure = "core_appraisal_commit_rejected";
                                            else
                                            {
                                                result.AppraisalsCommitted++;
                                                row["proposal"] = new JsonObject
                                                {
                                                    ["moodDelta"] = parsedAppraisal.MoodDelta,
                                                    ["affinityDelta"] = parsedAppraisal.AffinityDelta,
                                                    ["trustDelta"] = parsedAppraisal.TrustDelta,
                                                    ["resentmentDelta"] = parsedAppraisal.ResentmentDelta,
                                                    ["fearDelta"] = parsedAppraisal.FearDelta,
                                                    ["speechActKind"] = parsedAppraisal.SpeechActKind.ToString(),
                                                    ["speechTargetPersonId"] = parsedAppraisal.SpeechTargetPersonId,
                                                    ["conversationOption"] = parsedAppraisal.ConversationOption.ToString()
                                                };
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { failure = "cancelled_transport_exception"; }
                catch (Exception error) { failure = "appraisal_exception:" + error.GetType().Name; row["exceptionMessage"] = error.Message; }
                row["failureKind"] = failure;
                string stateAfter = SerializeWorldState(store.Snapshot());
                row["stateAfterSha256"] = HashText(stateAfter);
                row["stateAfter"] = stateAfter;
                row["stateEvidenceAfter"] = StateEvidence(store, personId, captureAt);
                row["inboxAfter"] = InboxEvidence(store.Snapshot(), personId);
                record(row);
                if (failure != null)
                {
                    result.StopReason = failure;
                    record(new JsonObject { ["recordType"] = "post-chat-phase-stopped", ["phase"] = "appraisal",
                        ["failureKind"] = failure, ["attemptIndex"] = index,
                        ["appraisalsCommitted"] = result.AppraisalsCommitted,
                        ["noRetryOrReopen"] = true, ["stateAfter"] = StateEvidence(store, personId, captureAt) });
                    return result;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                result.StopReason = "cancelled_before_reopen";
                record(new JsonObject { ["recordType"] = "post-chat-phase-stopped", ["phase"] = "reopen",
                    ["failureKind"] = result.StopReason, ["appraisalsCommitted"] = result.AppraisalsCommitted,
                    ["noRetryOrReopen"] = true, ["stateAfter"] = StateEvidence(store, personId, captureAt) });
                return result;
            }
            if (store.Snapshot().NpcExperienceInbox.ReadPending(personId, 100).Count != 0)
            {
                result.StopReason = "pending_inbox_not_drained";
                record(new JsonObject { ["recordType"] = "post-chat-phase-stopped", ["phase"] = "reopen",
                    ["failureKind"] = result.StopReason, ["noReopen"] = true,
                    ["stateAfter"] = StateEvidence(store, personId, captureAt) });
                return result;
            }
            // Start from the first authored snapshot only. Never carry a later fixture snapshot's
            // knowledge, native facts, or oracle-adjacent state into the reopened request.
            JsonObject reopenInput = (JsonObject)socialTurns[0].Input.DeepClone();
            reopenInput["playerText"] = reopenText;
            double reopenAt = captureAt + 1;
            if (reopenInput["world"] is JsonObject reopenWorld) reopenWorld["gameTimeSeconds"] = reopenAt;
            var reopenSeed = new SocialContinuationTurn("reopened-player-text", reopenInput, "caller-reopened-turn");
            try
            {
                JsonObject modelInput = BuildContinuationInput(socialTurns[0].Input, reopenSeed, store, personId, reopenAt);
                record(new JsonObject
                {
                    ["recordType"] = "reopened-social-phase-start", ["personId"] = personId,
                    ["appraisalCommitCount"] = result.AppraisalsCommitted,
                    ["stateBefore"] = StateEvidence(store, personId, reopenAt),
                    ["modelInput"] = modelInput.DeepClone(),
                    ["effectiveDispositionFromCore"] = modelInput["npc"]?["disposition"]?.DeepClone(),
                    ["oracleFieldsIncluded"] = false
                });
                SocialContinuationReport reopened = await RunSocialContinuationAsync(episode, arm,
                    new[] { reopenSeed }, transport, d3Endpoint, qwenEndpoint, expectedD3Model,
                    expectedQwenModel, record, cancellationToken, existingStore: store,
                    allowSeedHistory: true).ConfigureAwait(false);
                result.ReopenCompleted = reopened.CompletedTurns == 1 && reopened.StopReason == "completed";
                if (!result.ReopenCompleted) result.StopReason = reopened.StopReason ?? "reopen_failed";
            }
            catch (Exception error)
            {
                result.StopReason = cancellationToken.IsCancellationRequested ? "cancelled_during_reopen" :
                    "reopen_exception:" + error.GetType().Name;
                record(new JsonObject { ["recordType"] = "post-chat-phase-stopped", ["phase"] = "reopen",
                    ["failureKind"] = result.StopReason, ["exceptionMessage"] = error.Message,
                    ["stateAfter"] = StateEvidence(store, personId, reopenAt), ["noRetry"] = true });
            }
            return result;
        }

        private static JsonObject StateEvidence(WorldStateStore store, string personId, double at)
        {
            string serialized = SerializeWorldState(store.Snapshot());
            ConversationMoodView mood = store.CaptureConversationMood(personId, at);
            NpcDirectedRelationshipView relationship = store.ReadNpcRelationship(personId, NpcDirectedRelationship.PlayerTargetId);
            NpcDirectedRelationshipView projectedRelationship = store.ReadNpcRelationship(personId,
                NpcDirectedRelationship.PlayerTargetId, at);
            store.TryGetActualConversationDisposition(personId, out int disposition);
            return new JsonObject
            {
                ["sha256"] = HashText(serialized), ["fullState"] = serialized,
                ["mood"] = MoodRecord(mood), ["relationshipLedgerRevision"] = store.NpcRelationshipLedgerRevision,
                ["relationship"] = relationship == null ? null : new JsonObject
                {
                    ["trustKnown"] = relationship.TrustKnown,
                    ["reliabilityTrust"] = relationship.TrustKnown ? JsonValue.Create(relationship.ReliabilityTrust) : null,
                    ["trustConfidence"] = relationship.TrustConfidence, ["trustEvidenceCount"] = relationship.TrustEvidenceCount,
                    ["resentment"] = relationship.Resentment, ["interpersonalFear"] = relationship.InterpersonalFear,
                    ["storedResentment"] = projectedRelationship?.StoredResentment,
                    ["projectedResentment"] = projectedRelationship?.ProjectedResentment,
                    ["hasResentmentRecoveryAnchor"] = projectedRelationship?.HasResentmentRecoveryAnchor,
                    ["resentmentRecoveryAnchorGameTime"] = projectedRelationship?.ResentmentRecoveryAnchorGameTime,
                    ["resentmentRecoveryHalfLifeSeconds"] = projectedRelationship?.ResentmentRecoveryHalfLifeSeconds,
                    ["resentmentRecoveryBaselineRemainder"] = projectedRelationship?.ResentmentRecoveryBaselineRemainder,
                    ["projectedInterpersonalFear"] = projectedRelationship?.ProjectedInterpersonalFear,
                    ["projectedAtGameTime"] = projectedRelationship?.ProjectedAtGameTime,
                    ["revision"] = relationship.Revision, ["lastGameTime"] = relationship.LastGameTime
                },
                ["effectiveDispositionFromCore"] = disposition,
                ["conversationAttitudes"] = JsonSerializer.SerializeToNode(store.Snapshot().ConversationAttitudes,
                    new JsonSerializerOptions { IncludeFields = true }),
                ["inbox"] = InboxEvidence(store.Snapshot(), personId)
            };
        }

        private static JsonArray InboxEvidence(WorldState state, string personId)
        {
            var rows = new JsonArray();
            foreach (NpcExperienceInboxEntry entry in state.NpcExperienceInbox.Entries
                .Where(item => item.ObserverId == personId).OrderBy(item => item.ObservedGameTime)
                .ThenBy(item => item.DeduplicationKey, StringComparer.Ordinal))
                rows.Add(new JsonObject
                {
                    ["deduplicationKey"] = entry.DeduplicationKey,
                    ["canonicalEvidenceId"] = entry.Origin == NpcExperienceEventOrigin.AttributedSpeech ? entry.UtteranceId : entry.OutcomeId,
                    ["observedGameTime"] = entry.ObservedGameTime,
                    ["status"] = entry.Status.ToString(), ["rowRevision"] = entry.Revision,
                    ["alreadyAppliedEffectsMask"] = entry.AlreadyAppliedEffectsMask.ToString(),
                    ["observedText"] = entry.ObservedTurnText,
                    ["appraisal"] = entry.Appraisal == null ? null : new JsonObject
                    {
                        ["moodDelta"] = entry.Appraisal.MoodDelta,
                        ["affinityDelta"] = entry.Appraisal.AffinityDelta,
                        ["trustDelta"] = entry.Appraisal.TrustDelta,
                        ["resentmentDelta"] = entry.Appraisal.ResentmentDelta,
                        ["fearDelta"] = entry.Appraisal.FearDelta,
                        ["appliedMoodDelta"] = entry.Appraisal.AppliedMoodDelta,
                        ["appliedAffinityDelta"] = entry.Appraisal.AppliedAffinityDelta
                    }
                });
            return rows;
        }

        private static string FakeAppraisalResponse(NpcExperienceAppraisalRequest request, bool applyThreat)
        {
            return new JsonObject
            {
                ["schemaVersion"] = NpcExperienceAppraisalContract.SchemaVersion,
                ["policyVersion"] = NpcExperienceAppraisalContract.PolicyVersion,
                ["saveBranchId"] = request.SaveBranchId,
                ["observerId"] = request.ObserverId, ["subjectPersonId"] = request.SubjectPersonId,
                ["evidenceDeduplicationKey"] = request.EvidenceDeduplicationKey,
                ["canonicalEvidenceId"] = request.CanonicalEvidenceId,
                ["relationshipTargetId"] = request.RelationshipTargetId ?? string.Empty,
                ["expectedMoodRevision"] = request.ExpectedMoodRevision,
                ["expectedAffinityRevision"] = request.ExpectedAffinityRevision,
                ["expectedRelationshipRevision"] = request.ExpectedRelationshipRevision,
                ["moodDelta"] = 0, ["affinityDelta"] = applyThreat ? -1 : 0,
                ["trustDelta"] = applyThreat ? -1 : 0,
                ["resentmentDelta"] = applyThreat ? 1 : 0, ["fearDelta"] = applyThreat ? 1 : 0,
                ["speechActKind"] = applyThreat ? NpcExperienceSpeechActKind.DirectThreat.ToString() : NpcExperienceSpeechActKind.Other.ToString(),
                ["speechTargetPersonId"] = applyThreat ? request.ObserverId : string.Empty,
                ["conversationOption"] = NpcExperienceConversationOption.NoAction.ToString()
            }.ToJsonString(JsonOptions);
        }

        private static bool IsSpeechStageBody(byte[] body)
        { return TryObject(Encoding.UTF8.GetString(body))?["questions"]?["decision"]?["criteria"]?["ReportedThreat"] != null; }

        private static Task<SocialContinuationHttpResult> FakeSpeechStageResponse(string label)
        {
            var probabilities = new JsonObject();
            foreach (var option in NpcSpeechActRequest.Options) probabilities[option.Id] = option.Id == label ? 1 : 0;
            string response = new JsonObject { ["model"] = "d3-test", ["answers"] = new JsonObject {
                ["decision"] = new JsonObject { ["type"] = "choice", ["choice"] = label, ["probabilities"] = probabilities } } }.ToJsonString(JsonOptions);
            return Task.FromResult(new SocialContinuationHttpResult(200, Encoding.UTF8.GetBytes(response), 1, null));
        }

        private static string FakeTokenizerResponse(int tokenCount)
        {
            if (tokenCount < 0) throw new ArgumentOutOfRangeException(nameof(tokenCount));
            return "{\"tokens\":[" + string.Join(",", Enumerable.Repeat("0", tokenCount)) + "]}";
        }

        private sealed class SocialServerProbe
        {
            public bool Compatible;
            public string Failure;
            public JsonObject Metadata;
        }

        private static SocialServerProbe ProbeSocialServer(Uri requestEndpoint, string expectedModelPath)
        {
            var result = new SocialServerProbe();
            Uri root = new Uri(requestEndpoint.GetLeftPart(UriPartial.Authority) + "/");
            Uri healthUri = new Uri(root, "health");
            Uri propsUri = new Uri(root, "props");
            SocialContinuationHttpResult health = SendSocialMetadata(HttpMethod.Get, healthUri);
            SocialContinuationHttpResult props = SendSocialMetadata(HttpMethod.Get, propsUri);
            string healthText = DecodeResponse(health.Body);
            string propsText = DecodeResponse(props.Body);
            JsonObject healthJson = TryObject(healthText);
            JsonObject propsJson = TryObject(propsText);
            string actualModelPath = Text(propsJson, "model_path");
            int? nContext = FindNestedInt(propsJson, "n_ctx");
            int? slots = NullableInt(propsJson?["total_slots"]);
            result.Metadata = new JsonObject
            {
                ["requestEndpoint"] = requestEndpoint.ToString(),
                ["healthEndpoint"] = healthUri.ToString(), ["healthHttpStatus"] = health.Status,
                ["healthRawResponse"] = healthText, ["healthTransportError"] = health.Error,
                ["propsEndpoint"] = propsUri.ToString(), ["propsHttpStatus"] = props.Status,
                ["propsRawResponse"] = propsText, ["propsTransportError"] = props.Error,
                ["expectedModelPath"] = expectedModelPath, ["actualModelPath"] = actualModelPath,
                ["nContext"] = nContext, ["totalSlots"] = slots
            };
            if (health.Status != 200 || healthJson == null || Text(healthJson, "status") != "ok")
                result.Failure = "health endpoint did not report HTTP 200 and status=ok";
            else if (props.Status != 200 || propsJson == null)
                result.Failure = "props endpoint did not return HTTP 200 JSON";
            else if (string.IsNullOrWhiteSpace(actualModelPath) ||
                !string.Equals(Path.GetFullPath(actualModelPath), Path.GetFullPath(expectedModelPath), StringComparison.OrdinalIgnoreCase))
                result.Failure = "props model_path differs from the caller-pinned model path";
            else if (nContext == null || nContext <= 0 || slots == null || slots <= 0)
                result.Failure = "props did not identify a positive context size and slot count";
            else result.Compatible = true;
            return result;
        }

        private static SocialContinuationHttpResult SendSocialMetadata(HttpMethod method, Uri uri)
        {
            var request = new HttpRequestMessage(method, uri);
            var started = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using (request)
                using (HttpResponseMessage response = ContinuationMetadataHttpClient.SendAsync(request).GetAwaiter().GetResult())
                {
                    byte[] bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    return new SocialContinuationHttpResult((int)response.StatusCode, bytes,
                        started.Elapsed.TotalMilliseconds, null);
                }
            }
            catch (Exception error)
            {
                return new SocialContinuationHttpResult(null, Array.Empty<byte>(), started.Elapsed.TotalMilliseconds,
                    error.GetType().Name + ": " + error.Message);
            }
        }

        private static JsonObject TryObject(string json)
        {
            try { return JsonNode.Parse(json) as JsonObject; }
            catch (JsonException) { return null; }
        }

        private static int? FindNestedInt(JsonObject root, string name)
        {
            if (root == null) return null;
            foreach (var property in root)
            {
                JsonNode value = property.Value;
                if (property.Key == name && value != null && int.TryParse(value.ToJsonString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int direct)) return direct;
                if (value is JsonObject nested)
                {
                    int? found = FindNestedInt(nested, name);
                    if (found != null) return found;
                }
                else if (value is JsonArray array)
                    foreach (JsonObject element in array.OfType<JsonObject>())
                    {
                        int? found = FindNestedInt(element, name);
                        if (found != null) return found;
                    }
            }
            return null;
        }

        private static bool Flag(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        private static void ValidateSocialContinuationInputs(IReadOnlyList<SocialContinuationTurn> turns,
            bool allowSeedHistory = false)
        {
            JsonObject first = turns[0].Input ?? throw new InvalidDataException("First turn input snapshot is missing.");
            JsonObject npc = first["npc"] as JsonObject ?? throw new InvalidDataException("First NPC snapshot is missing.");
            if (npc["personality"] == null)
                throw new InvalidDataException("Initial stable personality profile is missing.");
            JsonArray history = first["history"] as JsonArray;
            if (history == null || (!allowSeedHistory && history.Count != 0))
                throw new InvalidDataException(allowSeedHistory
                    ? "Reopened input must provide a typed history array."
                    : "Closed-loop seed currently requires empty authored history; later turns use Core-committed transcript only.");
            _ = ReadAuthoredRelationship(npc, Text(npc, "id"));
            JsonObject initialWorld = first["world"] as JsonObject ?? throw new InvalidDataException("Initial world snapshot is missing.");
            double initialTime = Double(initialWorld, "gameTimeSeconds", -1);
            JsonObject initialMood = npc["mood"] as JsonObject ?? throw new InvalidDataException("Initial mood is missing.");
            JsonNode moodUpdatedAt = initialMood["updatedAtGameSeconds"];
            if (initialTime < 0 || moodUpdatedAt == null || moodUpdatedAt.GetValue<double>() > initialTime)
                throw new InvalidDataException("Initial mood must be captured no later than the initial model-facing game time.");
            string personId = Text(npc, "id");
            if (string.IsNullOrWhiteSpace(personId) || personId.Length > 240) throw new InvalidDataException("Initial actor identity is missing or over Core bounds.");
            var turnIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SocialContinuationTurn turn in turns)
            {
                if (turn == null || string.IsNullOrWhiteSpace(turn.TurnId) || !turnIds.Add(turn.TurnId) || turn.Input == null ||
                    string.IsNullOrWhiteSpace(turn.Input["playerText"]?.GetValue<string>()))
                    throw new InvalidDataException("Continuation turns need unique IDs and nonempty player text.");
                JsonObject turnNpc = turn.Input["npc"] as JsonObject;
                if (Text(turnNpc, "id") != personId ||
                    turnNpc?["personality"]?.ToJsonString(JsonOptions) != npc["personality"]?.ToJsonString(JsonOptions))
                    throw new InvalidDataException("All continuation turns must retain the first authored actor and exact stable personality profile.");
                JsonArray options = turn.Input["options"] as JsonArray;
                if (options == null || options.Count < 1 || options.Count > 32 || options.Any(option => !(option is JsonObject)))
                    throw new InvalidDataException("Each social turn needs one to 32 typed conversational options.");
                var optionIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonObject option in options.OfType<JsonObject>())
                    if (string.IsNullOrWhiteSpace(Text(option, "id")) || !optionIds.Add(Text(option, "id")) ||
                        string.IsNullOrWhiteSpace(Text(option, "description")))
                        throw new InvalidDataException("Every conversational option needs an ID and description.");
            }
        }

        private static async Task<SocialContinuationHttpResult> PostSocialContinuationAsync(Uri endpoint, byte[] body,
            CancellationToken cancellationToken, NpcExperienceAppraisalRequest appraisalRequest)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            var started = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using (request)
                using (HttpResponseMessage response = await ContinuationHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    return new SocialContinuationHttpResult((int)response.StatusCode, bytes,
                        started.Elapsed.TotalMilliseconds, null);
                }
            }
            catch (Exception error)
            {
                return new SocialContinuationHttpResult(null, Array.Empty<byte>(), started.Elapsed.TotalMilliseconds,
                    error.GetType().Name + ": " + error.Message);
            }
        }

        private static async Task<SocialContinuationReport> RunSocialContinuationAsync(JsonObject episode,
            JsonObject arm, IReadOnlyList<SocialContinuationTurn> turns, SocialContinuationTransport transport,
            Uri d3Endpoint, Uri qwenEndpoint, string expectedD3Model, string expectedQwenModel,
            Action<JsonObject> record, CancellationToken cancellationToken = default(CancellationToken),
            bool seedDispositionBaseline = false, bool staggerReceiptTimes = false,
            WorldStateStore existingStore = null, bool allowSeedHistory = false)
        {
            if (episode == null || arm == null || turns == null || turns.Count < 1 || turns.Count > 3 ||
                transport == null || record == null || d3Endpoint == null || qwenEndpoint == null ||
                string.IsNullOrWhiteSpace(expectedD3Model) || string.IsNullOrWhiteSpace(expectedQwenModel))
                throw new ArgumentException("Closed-loop execution requires bounded turns, loopback endpoints, and explicit model paths.");
            ValidateSocialContinuationInputs(turns, allowSeedHistory);
            JsonObject initialInput = turns[0].Input;
            JsonObject initialNpc = initialInput["npc"] as JsonObject;
            string personId = Text(initialNpc, "id");
            double at = Double(initialInput["world"] as JsonObject, "gameTimeSeconds", 0);
            WorldStateStore store = existingStore ?? CreateSocialContinuationStore(initialInput, personId, seedDispositionBaseline);
            string sessionId = "npc-factor-continuation:" + Guid.NewGuid().ToString("N");
            // The isolated continuation is a current synthetic conversation source. Its
            // process-local ticket binds each exact submitted utterance to the accepted turn.
            if (!store.TryBeginNpcObservedSpeechSession(personId, sessionId,
                    out NpcObservedSpeechSession speechSession))
                throw new InvalidDataException("Could not begin the synthetic observed-speech session.");
            var report = new SocialContinuationReport();
            report.Store = store;
            report.PersonId = personId;
            for (int index = 0; index < turns.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    report.StopReason = "cancelled";
                    break;
                }
                SocialContinuationTurn turn = turns[index];
                double turnAt = at + (staggerReceiptTimes ? index : 0);
                ConversationMoodView moodBefore = store.CaptureConversationMood(personId, turnAt);
                long relationshipLedgerRevisionBefore = store.NpcRelationshipLedgerRevision;
                string stateBefore = SerializeWorldState(store.Snapshot());
                string stateBeforeHash = HashText(stateBefore);
                JsonObject modelInput = BuildContinuationInput(initialInput, turn, store, personId, turnAt);
                var turnNode = new JsonObject { ["turnId"] = turn.TurnId };
                string requestDiscriminator = Text(episode, "id") + "/" + Text(arm, "id") + "/closed-loop/" + turn.TurnId;
                BuiltRequests built = BuildMultiTurnRequests(episode, arm, turnNode, modelInput,
                    requestDiscriminator, sessionId, moodBefore.AcceptedTurnSequence, index);
                byte[] d3Request = new UTF8Encoding(false, true).GetBytes(built.D3RequestBody);
                SocialContinuationHttpResult d3Http = await transport(d3Endpoint, d3Request, cancellationToken, null).ConfigureAwait(false);
                string d3Text = DecodeResponse(d3Http.Body);
                string d3Model = ResponseModel(d3Text);
                var row = new JsonObject
                {
                    ["recordType"] = "closed-loop-social-turn", ["episodeId"] = Text(episode, "id"),
                    ["armId"] = Text(arm, "id"), ["turnId"] = turn.TurnId, ["turnSource"] = turn.Source,
                    ["personId"] = personId, ["playerText"] = turn.Input["playerText"]?.GetValue<string>(),
                    ["conversationSessionId"] = sessionId, ["turnIndex"] = index,
                    ["modelInputSha256"] = HashText(modelInput.ToJsonString(JsonOptions)),
                    ["coreContextSha256"] = HashText(built.CoreDecision.Context),
                    ["stateBeforeSha256"] = stateBeforeHash,
                    ["stateBefore"] = stateBefore,
                    ["moodBefore"] = MoodRecord(moodBefore),
                    ["relationshipLedgerRevisionBefore"] = relationshipLedgerRevisionBefore,
                    ["d3"] = new JsonObject
                    {
                        ["endpoint"] = d3Endpoint.ToString(), ["requestSha256"] = built.DecisionHash,
                        ["binding"] = new JsonObject
                        {
                            ["requestId"] = built.CoreDecision.RequestId.ToString("D"),
                            ["personId"] = built.CoreDecision.Binding.PersonId,
                            ["sessionId"] = built.CoreDecision.Binding.SessionId,
                            ["requestRevision"] = built.CoreDecision.Binding.RequestRevision,
                            ["saveEpoch"] = built.CoreDecision.Binding.SaveEpoch,
                            ["archiveId"] = built.CoreDecision.Binding.ArchiveId,
                            ["sceneId"] = built.CoreDecision.Binding.SceneId,
                            ["sceneRevision"] = built.CoreDecision.Binding.SceneRevision,
                            ["snapshotRevision"] = built.CoreDecision.SnapshotRevision,
                            ["candidateSetRevision"] = built.CoreDecision.CandidateSetRevision
                        },
                        ["rawRequest"] = Encoding.UTF8.GetString(d3Request),
                        ["rawRequestBase64"] = Convert.ToBase64String(d3Request), ["httpStatus"] = d3Http.Status,
                        ["elapsedMs"] = d3Http.ElapsedMs, ["transportError"] = d3Http.Error,
                        ["rawResponse"] = d3Text, ["rawResponseBase64"] = Convert.ToBase64String(d3Http.Body),
                        ["modelId"] = d3Model
                    }
                };
                string failure = null;
                NpcDecisionResult decision = null;
                if (cancellationToken.IsCancellationRequested) failure = "cancelled";
                else if (d3Http.Status != 200 || d3Http.Error != null) failure = "d3_transport_error";
                else if (!string.Equals(d3Model, expectedD3Model, StringComparison.Ordinal)) failure = "d3_model_identity_mismatch";
                else
                {
                    LocalDecisionParsedReply parsed = LocalDecisionWire.Parse(d3Text);
                    decision = parsed.IsValid
                        ? NpcDecisionResult.ValidateResponse(built.CoreDecision, parsed.SelectedOptionId, parsed.Probabilities,
                            parsed.ReportedConfidence, built.DecisionHash, parsed.ModelId, d3Http.ElapsedMs, d3Http.Status,
                            built.CoreDecision.Binding, built.CoreDecision.SnapshotRevision,
                            built.CoreDecision.CandidateSetRevision, built.CoreDecision.Options)
                        : null;
                    bool accepted = decision != null && decision.Status == NpcDecisionStatus.Ok;
                    row["d3"]["parserValid"] = parsed.IsValid;
                    row["d3"]["validationStatus"] = decision?.Status.ToString() ?? "Invalid";
                    row["d3"]["diagnosticCode"] = decision?.DiagnosticCode ?? parsed.DiagnosticCode;
                    row["d3"]["selectedOptionId"] = accepted ? decision.SelectedOptionId : null;
                    row["d3"]["probabilities"] = parsed.Probabilities == null ? null :
                        JsonSerializer.SerializeToNode(parsed.Probabilities, JsonOptions);
                    if (!accepted) failure = "d3_core_validation_rejected";
                    else
                    {
                        ConversationMoodView currentMood = store.CaptureConversationMood(personId, turnAt);
                        if (currentMood.AcceptedTurnSequence != moodBefore.AcceptedTurnSequence ||
                            currentMood.Revision != moodBefore.Revision ||
                            store.NpcRelationshipLedgerRevision != relationshipLedgerRevisionBefore)
                            failure = "d3_core_snapshot_stale";
                    }
                }
                if (failure == null && decision != null)
                {
                    if (cancellationToken.IsCancellationRequested) failure = "cancelled";
                    if (failure != null) { }
                    else
                    {
                    DialogueCompletion completion = BuildMultiTurnQwenCompletion(built, modelInput,
                        decision.SelectedOptionId, new LocalDialogueConnection(qwenEndpoint.ToString(), 300000),
                        structuredMood: true);
                    byte[] qwenRequest = new UTF8Encoding(false, true).GetBytes(completion.JsonBody);
                    SocialContinuationHttpResult qwenHttp = await transport(qwenEndpoint, qwenRequest, cancellationToken, null).ConfigureAwait(false);
                    string qwenText = DecodeResponse(qwenHttp.Body);
                    string qwenModel = ResponseModel(qwenText);
                    string content = ResponseContent(qwenText);
                    row["qwen"] = new JsonObject
                    {
                        ["endpoint"] = qwenEndpoint.ToString(), ["requestSha256"] = HashText(Encoding.UTF8.GetString(qwenRequest)),
                        ["rawRequest"] = Encoding.UTF8.GetString(qwenRequest),
                        ["rawRequestBase64"] = Convert.ToBase64String(qwenRequest), ["httpStatus"] = qwenHttp.Status,
                        ["elapsedMs"] = qwenHttp.ElapsedMs, ["transportError"] = qwenHttp.Error,
                        ["rawResponse"] = qwenText, ["rawResponseBase64"] = Convert.ToBase64String(qwenHttp.Body),
                        ["modelId"] = qwenModel, ["responseContent"] = content
                    };
                    if (cancellationToken.IsCancellationRequested) failure = "cancelled";
                    else if (qwenHttp.Status != 200 || qwenHttp.Error != null) failure = "qwen_transport_error";
                    else if (!string.Equals(qwenModel, expectedQwenModel, StringComparison.Ordinal)) failure = "qwen_model_identity_mismatch";
                    else if (content == null) failure = "qwen_completion_envelope_invalid";
                    else if (!SocialDialogueResult.TryParse(content, out SocialDialogueResult social))
                    {
                        row["qwen"]["socialParseAccepted"] = false;
                        failure = "social_dialogue_result_rejected";
                    }
                    else
                    {
                        row["qwen"]["socialParseAccepted"] = true;
                        row["qwen"]["reply"] = social.Reply;
                        row["qwen"]["moodDelta"] = social.MoodDelta;
                        string exactPlayerText = turn.Input["playerText"].GetValue<string>();
                        bool issued = store.TryIssueNpcObservedSpeech(speechSession,
                            built.CoreDecision.Binding.RequestRevision, exactPlayerText, turnAt,
                            out NpcObservedSpeechTicket speechTicket) &&
                            store.TryBindNpcObservedSpeechContext(speechTicket, built.CoreDecision.Binding);
                        NpcObservedSpeechReceipt receipt = issued ? speechTicket.Receipt : null;
                        long committedSequence = 0;
                        bool committed = issued && store.TryCommitAcceptedSocialExchangeWithObservedSpeech(personId,
                            moodBefore.AcceptedTurnSequence, moodBefore.Revision, social.MoodDelta, turnAt,
                            "Synthetic evaluation", Text(initialInput["world"] as JsonObject, "scene", "synthetic-scene"),
                            exactPlayerText, social.Reply, speechTicket, receipt,
                            NpcExperienceAppliedEffectsMask.Mood, out committedSequence);
                        if (!issued) failure = "core_speech_source_rejected";
                        else if (!committed) failure = "core_social_commit_rejected";
                        else
                        {
                            report.CompletedTurns++;
                            row["coreCommitAccepted"] = true;
                            row["committedSequence"] = committedSequence;
                            row["speechReceiptId"] = receipt.Evidence.ReceiptId;
                        }
                    }
                    }
                }
                else row["qwen"] = null;
                ConversationMoodView moodAfter = store.CaptureConversationMood(personId, turnAt);
                row["moodAfter"] = MoodRecord(moodAfter);
                row["relationshipLedgerRevisionAfter"] = store.NpcRelationshipLedgerRevision;
                string stateAfter = SerializeWorldState(store.Snapshot());
                row["stateAfterSha256"] = HashText(stateAfter);
                row["stateAfter"] = stateAfter;
                row["failureStage"] = failure;
                row["coreCommitted"] = failure == null;
                row["transcriptAfter"] = TranscriptRecord(store, personId, turnAt);
                report.Records.Add(row);
                record(row);
                if (failure != null)
                {
                    report.StopReason = failure;
                    break;
                }
            }
            report.FinalMood = store.CaptureConversationMood(personId, at);
            report.FinalState = store.Snapshot();
            report.FinalGameTime = at + (staggerReceiptTimes ? Math.Max(0, report.CompletedTurns - 1) : 0);
            report.FinalMood = store.CaptureConversationMood(personId, report.FinalGameTime);
            report.StopReason = report.StopReason ?? "completed";
            return report;
        }

        private static WorldStateStore CreateSocialContinuationStore(JsonObject input, string personId,
            bool seedDispositionBaseline = false)
        {
            JsonObject npc = input["npc"] as JsonObject;
            JsonObject traits = npc["personality"] as JsonObject;
            int seedValue = npc["personalitySeed"]?.GetValue<int>() ?? 0;
            if (seedValue < 0) throw new InvalidDataException("Synthetic profile seed must be nonnegative.");
            uint seed = checked((uint)seedValue);
            var world = new WorldState { WorldSeed = seed };
            var resident = new Resident { Id = personId, FullName = "Synthetic Resident Evaluation", HomeKey = 0,
                BedSlot = 0, AppearanceSeed = seed, IsAlive = true, Sex = ResidentSex.Female,
                AgeGroup = ResidentAgeGroup.Adult };
            var household = new Household { BuildingKey = 0, HeadId = personId, Surname = "Evaluation",
                BedCapacity = 1, Residents = new List<Resident> { resident } };
            world.Settlements.Add(new SettlementPopulation { SettlementId = 0,
                Households = new List<Household> { household } });
            world.NpcRelationships.Relationships.Add(CreateSyntheticBaselineRelationship(npc, personId));
            var profile = new NpcPersonalityProfile
            {
                PersonId = personId, Version = 2, Seed = seed, Background = NpcSocialBackground.Commoner,
                Warmth = Text(traits, "warmth"), Patience = Text(traits, "patience"), Curiosity = Text(traits, "curiosity"),
                Privacy = Text(traits, "privacy"), Manner = Text(traits, "manner"), Outlook = Text(traits, "outlook"),
                EmotionalStability = Text(traits, "emotionalStability"), Ambition = Text(traits, "ambition"),
                Integrity = Text(traits, "integrity"), Courage = Text(traits, "courage"), Dutifulness = Text(traits, "dutifulness"),
                Vocabulary = Text(traits, "vocabulary"), Eloquence = Text(traits, "eloquence"), StatusAttitude = Text(traits, "statusAttitude"),
                Humor = Text(traits, "humor"), SelfEsteem = Text(traits, "selfEsteem"), Sensitivity = Text(traits, "sensitivity")
            };
            world.NpcPersonalities.Add(NpcPersonalityProfile.CopyValidated(profile));
            if (seedDispositionBaseline)
            {
                int baseline = npc["disposition"]?.GetValue<int>() ??
                    throw new InvalidDataException("Opt-in appraisal/reopen mode requires an authored effective disposition.");
                if (baseline < -100 || baseline > 100)
                    throw new InvalidDataException("Authored effective disposition is outside Core bounds.");
                world.ConversationAttitudes.Add(new ConversationAttitude
                {
                    PersonId = personId, HasNativeBaseline = true, NativeBaseline = baseline, Offset = 0,
                    NativeFactionId = -1, HasNativeFactionId = false,
                    Day = Math.Floor(Double(input["world"] as JsonObject, "gameTimeSeconds", 0) / 86400d)
                });
            }
            JsonObject mood = npc["mood"] as JsonObject ?? throw new InvalidDataException("Initial mood is missing.");
            world.ConversationMoods.Add(new ConversationMood
            {
                PersonId = personId, Value = mood["value"].GetValue<int>(),
                AcceptedTurnSequence = mood["acceptedTurnSequence"].GetValue<long>(),
                Revision = mood["stateRevision"].GetValue<long>(), HasTimeAnchor = true,
                UpdatedAt = mood["updatedAtGameSeconds"].GetValue<double>()
            });
            var store = new WorldStateStore(seed);
            store.Restore(world);
            return store;
        }

        private static string InitialProfileHashMaterial(JsonObject input)
        {
            JsonObject npc = input["npc"] as JsonObject ?? throw new InvalidDataException("Initial NPC profile is missing.");
            int personalitySeed = npc["personalitySeed"]?.GetValue<int>() ?? 0;
            return npc["personality"].ToJsonString(JsonOptions) + "|" +
                personalitySeed.ToString(CultureInfo.InvariantCulture) + "|" + Text(npc, "id");
        }

        private static NpcDirectedRelationship ReadAuthoredRelationship(JsonObject npc, string personId)
        {
            JsonObject source = npc?["relationship"] as JsonObject ??
                throw new InvalidDataException("Initial directed relationship is missing.");
            bool known = source["trustKnown"]?.GetValue<bool>() ?? false;
            if (known != (source["reliabilityTrust"] != null))
                throw new InvalidDataException("Typed trust must include reliabilityTrust only when trustKnown is true.");
            var relationship = new NpcDirectedRelationship
            {
                ObserverId = personId,
                TargetId = NpcDirectedRelationship.PlayerTargetId,
                Revision = source["revision"]?.GetValue<long>() ?? 0,
                LastGameTime = source["lastGameTime"]?.GetValue<double>() ?? -1,
                TrustKnown = known,
                ReliabilityTrust = known ? source["reliabilityTrust"]?.GetValue<int>() ?? 0 : 0,
                TrustConfidence = source["trustConfidence"]?.GetValue<int>() ?? 0,
                TrustEvidenceCount = source["trustEvidenceCount"]?.GetValue<int>() ?? 0,
                Resentment = source["resentment"]?.GetValue<int>() ?? 0,
                InterpersonalFear = source["interpersonalFear"]?.GetValue<int>() ?? 0
            };
            try { return NpcDirectedRelationship.CopyValidated(relationship); }
            catch (ArgumentException error)
            { throw new InvalidDataException("Initial typed relationship is invalid under the current Core contract.", error); }
        }

        private static NpcDirectedRelationship CreateSyntheticBaselineRelationship(JsonObject npc, string personId)
        {
            NpcDirectedRelationship baseline = ReadAuthoredRelationship(npc, personId);
            baseline.Revision = 0;
            baseline.LastGameTime = -1;
            return NpcDirectedRelationship.CopyValidated(baseline);
        }

        private static JsonObject BuildInitialStateSeedRecord(JsonObject input)
        {
            JsonObject npc = input["npc"] as JsonObject ?? throw new InvalidDataException("Initial NPC snapshot is missing.");
            NpcDirectedRelationship authored = ReadAuthoredRelationship(npc, Text(npc, "id"));
            NpcDirectedRelationship seeded = CreateSyntheticBaselineRelationship(npc, Text(npc, "id"));
            return new JsonObject
            {
                ["relationshipSeedKind"] = "synthetic_baseline_without_imported_outcome_history",
                ["sourceRelationshipRevision"] = authored.Revision,
                ["sourceRelationshipLastGameTime"] = authored.LastGameTime,
                ["seededRelationshipRevision"] = seeded.Revision,
                ["seededRelationshipLastGameTime"] = seeded.LastGameTime,
                ["acceptedRelationshipOutcomesSeeded"] = 0,
                ["trustKnown"] = seeded.TrustKnown,
                ["reliabilityTrust"] = seeded.TrustKnown ? JsonValue.Create(seeded.ReliabilityTrust) : null,
                ["trustConfidence"] = seeded.TrustConfidence,
                ["trustEvidenceCount"] = seeded.TrustEvidenceCount,
                ["resentment"] = seeded.Resentment,
                ["interpersonalFear"] = seeded.InterpersonalFear,
                ["moodValueSeeded"] = npc["mood"]?["value"]?.DeepClone(),
                ["moodRevisionSeeded"] = npc["mood"]?["stateRevision"]?.DeepClone(),
                ["acceptedTurnSequenceSeeded"] = npc["mood"]?["acceptedTurnSequence"]?.DeepClone(),
                ["initialActorKnowledgeSha256"] = HashText((input["world"]?["npcKnowledge"] as JsonArray ?? new JsonArray()).ToJsonString(JsonOptions))
            };
        }

        private static JsonObject BuildContinuationInput(JsonObject initialInput, SocialContinuationTurn turn,
            WorldStateStore store, string personId, double at)
        {
            JsonObject input = (JsonObject)initialInput.DeepClone();
            input["playerText"] = turn.Input["playerText"]?.DeepClone();
            input["options"] = turn.Input["options"]?.DeepClone();
            ConversationMoodView mood = store.CaptureConversationMood(personId, at);
            JsonObject npc = input["npc"] as JsonObject;
            npc["mood"] = new JsonObject
            {
                ["value"] = mood.Value, ["acceptedTurnSequence"] = mood.AcceptedTurnSequence,
                ["stateRevision"] = mood.Revision, ["updatedAtGameSeconds"] = mood.UpdatedAt
            };
            NpcDirectedRelationshipView relationship = store.ReadNpcRelationship(personId,
                NpcDirectedRelationship.PlayerTargetId, at);
            if (relationship == null)
                throw new InvalidDataException("Core continuation branch lost its directed relationship seed.");
            JsonObject npcRelationship = new JsonObject
            {
                ["revision"] = relationship.Revision, ["lastGameTime"] = relationship.LastGameTime,
                ["trustKnown"] = relationship.TrustKnown,
                ["reliabilityTrust"] = relationship.TrustKnown ? JsonValue.Create(relationship.ReliabilityTrust) : null,
                ["trustConfidence"] = relationship.TrustConfidence, ["trustEvidenceCount"] = relationship.TrustEvidenceCount,
                ["resentment"] = relationship.ProjectedResentment,
                ["interpersonalFear"] = relationship.ProjectedInterpersonalFear
            };
            npc["relationship"] = npcRelationship;
            if (store.TryGetActualConversationDisposition(personId, out int disposition))
                npc["disposition"] = disposition;
            JsonObject world = input["world"] as JsonObject;
            if (world != null) world["gameTimeSeconds"] = at;
            var history = new JsonArray();
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(personId, at, 64))
            {
                if (!string.IsNullOrWhiteSpace(entry.PlayerText))
                    history.Add(new JsonObject { ["speaker"] = "player", ["text"] = entry.PlayerText });
                if (!string.IsNullOrWhiteSpace(entry.NpcText))
                    history.Add(new JsonObject { ["speaker"] = "npc", ["text"] = entry.NpcText });
            }
            input["history"] = history;
            return input;
        }

        private static JsonObject MoodRecord(ConversationMoodView mood) => new JsonObject
        {
            ["value"] = mood.Value, ["acceptedTurnSequence"] = mood.AcceptedTurnSequence,
            ["revision"] = mood.Revision, ["hasTimeAnchor"] = mood.HasTimeAnchor,
            ["updatedAtGameSeconds"] = mood.UpdatedAt
        };

        private static JsonArray TranscriptRecord(WorldStateStore store, string personId, double at)
        {
            var result = new JsonArray();
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(personId, at, 64))
                result.Add(new JsonObject { ["at"] = entry.At, ["playerText"] = entry.PlayerText,
                    ["npcText"] = entry.NpcText, ["source"] = entry.Source });
            return result;
        }

        private static string SerializeWorldState(WorldState state) =>
            JsonSerializer.Serialize(state, new JsonSerializerOptions { IncludeFields = true });

        private static string DecodeResponse(byte[] bytes)
        {
            try { return new UTF8Encoding(false, true).GetString(bytes ?? Array.Empty<byte>()); }
            catch (DecoderFallbackException) { return Encoding.UTF8.GetString(bytes ?? Array.Empty<byte>()); }
        }

        private static string ResponseModel(string raw)
        {
            return ResponseStringField(raw, "model");
        }

        private static string ResponseContent(string raw)
        {
            return ResponseStringField(raw, "content");
        }

        private static Uri AppraisalTokenizeEndpoint(Uri completionEndpoint)
        {
            return new Uri(completionEndpoint.GetLeftPart(UriPartial.Authority) + "/tokenize");
        }

        private static byte[] BuildAppraisalTokenizeRequest(string prompt)
        {
            string body = "{\"content\":\"" + LocalDialogueCompletion.EscapeJson(prompt) + "\",\"add_special\":true}";
            return new UTF8Encoding(false, true).GetBytes(body);
        }

        private static bool TryParseAppraisalTokenCount(string response, out int count, out string error)
        {
            count = 0;
            error = null;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(response))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object ||
                        !document.RootElement.TryGetProperty("tokens", out JsonElement tokens) ||
                        tokens.ValueKind != JsonValueKind.Array)
                    {
                        error = "tokenize response must contain a tokens array";
                        return false;
                    }
                    foreach (JsonElement token in tokens.EnumerateArray())
                    {
                        if (token.ValueKind != JsonValueKind.Number || !token.TryGetInt32(out _))
                        {
                            error = "tokenize tokens array must contain only 32-bit integers";
                            return false;
                        }
                        count++;
                    }
                    return true;
                }
            }
            catch (JsonException)
            {
                error = "tokenize response is invalid JSON";
                return false;
            }
        }

        private static string ResponseStringField(string raw, string field)
        {
            try
            {
                JsonObject envelope = JsonNode.Parse(raw) as JsonObject;
                return envelope?[field] is JsonValue value && value.TryGetValue<string>(out string text)
                    ? text : null;
            }
            catch (JsonException) { return null; }
        }

        private static double Double(JsonObject value, string key, double fallback)
        {
            JsonNode node = value?[key];
            if (node == null) return fallback;
            return double.Parse(node.ToJsonString(), CultureInfo.InvariantCulture);
        }

        private static int? NullableInt(JsonNode value) => value == null ? (int?)null : value.GetValue<int>();

        private static List<JsonObject> ReadJsonLines(string path)
        {
            var rows = new List<JsonObject>();
            int lineNumber = 0;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonObject row = JsonNode.Parse(line) as JsonObject;
                if (row == null) throw new InvalidDataException("JSONL row " + lineNumber.ToString(CultureInfo.InvariantCulture) + " must be an object.");
                rows.Add(row);
            }
            return rows;
        }

        private static string HashText(string value)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string HashBytes(byte[] value)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value ?? Array.Empty<byte>())).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string OfflineDecisionResponse(NpcDecisionRequest request)
        {
            string selected = request.Options[0].Id;
            var probabilities = request.Options.ToDictionary(option => option.Id,
                option => string.Equals(option.Id, selected, StringComparison.Ordinal) ? 1d : 0d,
                StringComparer.Ordinal);
            return JsonSerializer.Serialize(new
            {
                model = "offline-fixture",
                answers = new { decision = new { type = "choice", choice = selected, probabilities } }
            }, JsonOptions);
        }

        private static void WriteNewLines(string path, IEnumerable<string> lines)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) foreach (string line in lines) writer.WriteLine(line);
        }

        private static int Record(string[] args)
        {
            string preparedPath = Option(args, "--prepared"), responsePath = Option(args, "--responses"), outputPath = Option(args, "--output");
            if (preparedPath == null || responsePath == null || outputPath == null) throw new ArgumentException("record requires --prepared, --responses, and --output.");
            var prepared = ReadLines(preparedPath).Select(line => JsonNode.Parse(line) as JsonObject)
                .ToDictionary(row => Text(row, "requestId", Text(row, "armId")), StringComparer.Ordinal);
            var responses = ReadLines(responsePath).Select(line => JsonNode.Parse(line) as JsonObject).ToArray();
            Require(responses.Length == prepared.Count, "Response rows must match prepared arms exactly; preserve failed calls as rows with no selectedOptionId.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string fullOutput = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput));
            using (var stream = new FileStream(fullOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                foreach (JsonObject response in responses)
                {
                    string id = Text(response, "requestId", Text(response, "armId"));
                    if (!seen.Add(id) || !prepared.TryGetValue(id, out JsonObject request)) throw new InvalidDataException("Duplicate or unknown response armId: " + id);
                    string rawRequest = Text(response, "rawRequest"), rawResponse = Text(response, "rawResponse");
                    Require(rawRequest != null && rawRequest.Length <= 2_000_000 && rawResponse != null && rawResponse.Length <= 2_000_000, "Each raw request/response must be supplied and remain under the 2 MB record limit.");
                    string selected = Text(response, "selectedOptionId");
                    string[] offered = ExtractOfferedIds(request["d3Request"]?["state"]?.GetValue<string>());
                    string[] fixtureEligible = Strings(request["expectedNativeEligibleOptionIds"]).ToArray();
                    string mechanical = selected == null ? "unparsed_or_failed" : !offered.Contains(selected, StringComparer.Ordinal) ? "choice_not_offered" :
                        fixtureEligible.Length == 0 ? "fixture_gate_not_defined" : fixtureEligible.Contains(selected, StringComparer.Ordinal) ? "fixture_eligible" : "fixture_ineligible";
                    var result = new
                    {
                        recordType = "raw-review-record", corpusId = Text(request, "corpusId"), corpusSha256 = Text(request, "corpusSha256"),
                        pairId = Text(request, "pairId"), factorId = Text(request, "factorId"), armId = Text(request, "armId"),
                        episodeId = Text(request, "episodeId"), turnId = Text(request, "turnId"), requestId = id,
                        adapterStatus = Text(request, "adapterStatus"), rawRequest, rawResponse,
                        modelMetadata = response["modelMetadata"], selectedOptionId = selected,
                        mechanicalChoiceGate = new { result = mechanical, offeredOptionIds = offered, fixtureNativeEligibleOptionIds = fixtureEligible,
                            isCurrentNativeValidation = false }, qwenReview = request["qwenReview"],
                        manualSemanticReview = new { status = "pending", reviewer = (string)null, note = (string)null }
                    };
                    writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                }
            }
            Require(seen.Count == prepared.Count, "Missing response rows.");
            Console.WriteLine("Recorded raw requests/responses with a fixture-only choice gate. Semantic review remains manual; no output was graded or retried.");
            return 0;
        }

        private static int RecordAppraisal(string[] args)
        {
            string preparedPath = Option(args, "--prepared");
            string responsePath = Option(args, "--responses");
            string outputPath = Option(args, "--output");
            if (preparedPath == null || responsePath == null || outputPath == null)
                throw new ArgumentException("record-appraisal requires --prepared, --responses, and --output.");

            string fixtureVersion = AppraisalFixtureVersion(args);
            string fixtureId = AppraisalFixtureId(fixtureVersion);
            string fixturePath = AppraisalFixturePath(fixtureVersion);
            IReadOnlyList<AppraisalFixtureCase> cases = BuildAppraisalCases(fixtureVersion, fixturePath);
            string fixtureHash = AppraisalFixtureHash(fixtureVersion, fixturePath);
            int expectedCount = AppraisalExpectedCaseCount(fixtureVersion);
            if (cases.Count != expectedCount) throw new InvalidDataException("Appraisal fixture must expand to exactly " + expectedCount + " Core requests.");
            List<JsonObject> preparedRows = ReadJsonLines(preparedPath);
            List<JsonObject> responseRows = ReadJsonLines(responsePath);
            if (preparedRows.Count != cases.Count || responseRows.Count != cases.Count)
                throw new InvalidDataException("Prepared, response, and Core fixture rows must match all " + expectedCount + " appraisal cases.");

            var preparedById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonObject row in preparedRows)
            {
                string id = Text(row, "caseId");
                if (id == null || !preparedById.TryAdd(id, row))
                    throw new InvalidDataException("Prepared appraisal case IDs must be present and unique.");
            }
            var responseById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonObject row in responseRows)
            {
                string id = Text(row, "caseId");
                if (id == null || !responseById.TryAdd(id, row))
                    throw new InvalidDataException("Appraisal response case IDs must be present and unique.");
            }

            LocalDialogueConnection connection = new LocalDialogueConnection("http://127.0.0.1:8080/completion", 300000);
            var output = new List<string>(cases.Count);
            foreach (AppraisalFixtureCase fixtureCase in cases)
            {
                string id = fixtureCase.CaseId;
                if (!preparedById.TryGetValue(id, out JsonObject prepared) || !responseById.TryGetValue(id, out JsonObject response))
                    throw new InvalidDataException("Missing prepared or response row for appraisal case " + id + ".");
                DialogueCompletion completion = LocalDialogueCompletion.BuildAppraisal(connection, fixtureCase.Request);
                if (Text(prepared, "recordType") != "prepared-production-core-appraisal" ||
                    Text(prepared, "fixtureId") != fixtureId ||
                    Text(prepared, "fixtureSha256") != fixtureHash ||
                    Text(prepared, "requestPrompt") != fixtureCase.Request.Prompt ||
                    Text(prepared, "requestPromptSha256") != fixtureCase.InputSha256 ||
                    Text(prepared, "completionRequestBody") != completion.JsonBody ||
                    Text(prepared, "completionRequestSha256") != HashText(completion.JsonBody) ||
                    Text(response, "recordType") != "appraisal-model-response-v1" ||
                    Text(response, "requestPromptSha256") != fixtureCase.InputSha256 ||
                    Text(response, "rawRequest") != completion.JsonBody ||
                    Text(response, "rawRequestSha256") != HashText(completion.JsonBody))
                    throw new InvalidDataException(id + ": prepared or transmitted request differs from current Core completion bytes.");
                byte[] expectedRequestBytes = Encoding.UTF8.GetBytes(completion.JsonBody);
                byte[] recordedRequestBytes;
                try { recordedRequestBytes = Convert.FromBase64String(Text(response, "rawRequestBase64")); }
                catch (FormatException) { throw new InvalidDataException(id + ": transmitted request base64 is invalid."); }
                if (!expectedRequestBytes.SequenceEqual(recordedRequestBytes))
                    throw new InvalidDataException(id + ": transmitted request base64 differs from current Core completion bytes.");

                int? status = NullableInt(response["httpStatus"]);
                string transportError = Text(response, "transportError");
                string responseText = ReadCompletionContent(response);
                NpcExperienceAppraisal appraisal = null;
                bool parsed = status == 200 && string.IsNullOrEmpty(transportError) && responseText != null &&
                    AppraisalFixturePreparation.TryParseResponse(fixtureCase, responseText, out appraisal);
                string failureKind = parsed ? null : status != 200 ? "http_error" :
                    !string.IsNullOrEmpty(transportError) ? "transport_error" :
                    responseText == null ? "missing_model_content" : "core_parser_rejected";

                JsonObject result = new JsonObject
                {
                    ["recordType"] = "core-validated-appraisal-result",
                    ["caseId"] = id,
                    ["fixtureId"] = fixtureId,
                    ["fixtureSha256"] = fixtureHash,
                    ["requestPromptSha256"] = fixtureCase.InputSha256,
                    ["httpStatus"] = status,
                    ["transportError"] = transportError,
                    ["modelMetadata"] = response["modelMetadata"]?.DeepClone(),
                    ["coreValidated"] = parsed,
                    ["failureKind"] = failureKind,
                    ["humanReview"] = new JsonObject { ["label"] = fixtureCase.Label },
                    ["nativeCommitAvailable"] = false
                };
                if (parsed)
                {
                    result["appraisal"] = JsonSerializer.SerializeToNode(new
                    {
                        saveBranchId = appraisal.SaveBranchId,
                        observerId = appraisal.ObserverId,
                        subjectPersonId = appraisal.SubjectPersonId,
                        evidenceDeduplicationKey = appraisal.EvidenceDeduplicationKey,
                        canonicalEvidenceId = appraisal.CanonicalEvidenceId,
                        relationshipTargetId = appraisal.RelationshipTargetId ?? string.Empty,
                        expectedMoodRevision = appraisal.ExpectedMoodRevision,
                        expectedAffinityRevision = appraisal.ExpectedAffinityRevision,
                        expectedRelationshipRevision = appraisal.ExpectedRelationshipRevision,
                        moodDelta = appraisal.MoodDelta,
                        affinityDelta = appraisal.AffinityDelta,
                        trustDelta = appraisal.TrustDelta,
                        resentmentDelta = appraisal.ResentmentDelta,
                        fearDelta = appraisal.FearDelta,
                        speechActKind = appraisal.SpeechActKind.ToString(),
                        speechTargetPersonId = appraisal.SpeechTargetPersonId,
                        conversationOption = appraisal.ConversationOption.ToString()
                    }, JsonOptions);
                }
                else result["appraisal"] = null;
                output.Add(result.ToJsonString(JsonOptions));
            }
            WriteNewLines(outputPath, output);
            int validCount = output.Count(line => (JsonNode.Parse(line) as JsonObject)?["coreValidated"]?.GetValue<bool>() == true);
            Console.WriteLine("Core parser checked all " + expectedCount + " appraisal responses: " + validCount + " valid, " +
                (output.Count - validCount) + " rejected. No native state was committed. Output: " + Path.GetFullPath(outputPath));
            return 0;
        }

        private static string ReadCompletionContent(JsonObject response)
        {
            string rawText = Text(response, "rawResponse");
            string rawBase64 = Text(response, "rawResponseBase64");
            if (rawText == null || rawBase64 == null)
                throw new InvalidDataException("Raw response text and base64 evidence are required for each appraisal call.");
            byte[] rawBytes;
            try { rawBytes = Convert.FromBase64String(rawBase64); }
            catch (FormatException) { throw new InvalidDataException("Raw completion response base64 is invalid."); }
            if (rawBytes.Length > 2_000_000 || Encoding.UTF8.GetString(rawBytes) != rawText)
                throw new InvalidDataException("Raw completion response text does not match bounded base64 evidence.");
            string extracted = null;
            try
            {
                JsonObject envelope = JsonNode.Parse(rawText) as JsonObject;
                extracted = Text(envelope, "content");
            }
            catch (JsonException) { }
            if (Text(response, "responseText") != extracted)
                throw new InvalidDataException("Extracted completion content differs from the exact server response envelope.");
            return extracted;
        }

        private static string[] ExtractOfferedIds(string stateJson)
        {
            JsonObject state = JsonNode.Parse(stateJson) as JsonObject;
            return (state["options"] as JsonArray ?? new JsonArray()).Select(option => Text(option as JsonObject, "id")).ToArray();
        }

        private static string SerializeHistory(JsonNode history)
        {
            if (history is JsonArray array) return string.Join("\n", array.Select(item => item is JsonObject ? item.ToJsonString(JsonOptions) : item?.GetValue<string>() ?? string.Empty));
            return history?.ToJsonString(JsonOptions) ?? string.Empty;
        }

        // Fixture history is a frozen per-turn transcript snapshot. Project it
        // into the exact line contract production ReadConversationHistory emits;
        // this does not replay or synthesize earlier model turns.
        private static string SerializeModelHistory(JsonNode history)
        {
            JsonArray turns = history as JsonArray ?? throw new InvalidDataException(
                "Multi-turn model history must be a typed speaker/text snapshot.");
            var lines = new List<string>(turns.Count);
            foreach (JsonNode node in turns)
            {
                JsonObject turn = node as JsonObject ?? throw new InvalidDataException(
                    "Multi-turn history entry must be an object.");
                string speaker = Text(turn, "speaker");
                string text = turn["text"]?.GetValue<string>();
                string prefix;
                if (speaker == "player") prefix = "Player: ";
                else if (speaker == "npc") prefix = "NPC: ";
                else throw new InvalidDataException("Multi-turn history speaker must be player or npc.");
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidDataException("Multi-turn history text must be nonempty.");
                lines.Add(prefix + text.Replace("\r", " ").Replace("\n", " "));
            }
            return string.Join("\n", lines);
        }

        private static void AssertModelHistoryProjection(DialogueCompletion completion, JsonNode history)
        {
            JsonArray turns = history as JsonArray ?? throw new InvalidDataException(
                "Multi-turn history snapshot is not an array.");
            foreach (JsonObject turn in turns)
            {
                string role = Text(turn, "speaker") == "player" ? "user" : "assistant";
                string text = turn["text"]?.GetValue<string>()?.Replace("\r", " ").Replace("\n", " ")
                    .Replace("<|", "< |");
                string expected = "<|im_start|>" + role + "\n" + text + "<|im_end|>";
                Require(completion != null && completion.Prompt.Contains(expected, StringComparison.Ordinal),
                    "Frozen per-turn history snapshot must use production Player:/NPC: role projection in the Qwen prompt.");
            }
        }

        private static JsonObject CloneObject(JsonObject source, params string[] keys)
        {
            var result = new JsonObject();
            foreach (string key in keys) if (source[key] != null) result[key] = source[key].DeepClone();
            return result;
        }

        private static JsonObject Merge(JsonObject source, JsonObject patch)
        {
            JsonObject result = JsonNode.Parse(source.ToJsonString()) as JsonObject;
            MergeInto(result, patch);
            return result;
        }

        private static void MergeInto(JsonObject target, JsonObject patch)
        {
            if (patch == null) return;
            foreach (var item in patch)
            {
                if (item.Value is JsonObject patchObject && target[item.Key] is JsonObject targetObject) MergeInto(targetObject, patchObject);
                else target[item.Key] = item.Value?.DeepClone();
            }
        }

        private static IEnumerable<string> Strings(JsonNode node) => (node as JsonArray ?? new JsonArray()).Select(value => value.GetValue<string>());
        private static string Text(JsonObject node, string key, string fallback = null) => node?[key] == null ? fallback : node[key].GetValue<string>();
        private static bool? Bool(JsonObject node, string key) => node?[key] == null ? (bool?)null : node[key].GetValue<bool>();
        private static JsonArray Pairs(JsonObject corpus) => corpus["pairs"] as JsonArray ?? throw new InvalidDataException("Corpus has no pairs.");
        private static JsonObject ReadCorpus(string file) => ReadFixture(file);
        private static JsonObject ReadFixture(string file) => JsonNode.Parse(File.ReadAllText(FixturePath(file), Encoding.UTF8)) as JsonObject ?? throw new InvalidDataException("Fixture must be an object.");
        private static string FixturePath(string file) => Path.Combine(AppContext.BaseDirectory, "fixtures", file);
        private static string HashFile(string path) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty).ToLowerInvariant(); }
        private static Guid DeterministicGuid(string value) { using (SHA256 sha = SHA256.Create()) return new Guid(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Take(16).ToArray()); }
        private static string Option(string[] args, string name) { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
        private static string AppraisalFixtureVersion(string[] args)
        {
            string version = Option(args, "--fixture-version") ?? "v1";
            if (version != "v1" && version != "v2" && version != "v3")
                throw new ArgumentException("--fixture-version must be v1, v2 or v3.");
            return version;
        }
        private static string AppraisalFixturePath(string version) => FixturePath(version == "v3"
            ? "appraisal-live-scenarios-v3.json" : "appraisal-live-scenarios-" + version + ".json");
        private static IReadOnlyList<AppraisalFixtureCase> BuildAppraisalCases(string version, string fixturePath) =>
            version == "v3" ? AppraisalContextFixturePreparation.BuildCorpusCases(fixturePath) :
                AppraisalFixturePreparation.BuildCases(fixturePath);
        private static string AppraisalFixtureHash(string version, string fixturePath) => version == "v3"
            ? AppraisalContextFixturePreparation.GetCorpusManifestSha256(fixturePath) : HashFile(fixturePath);
        private static int AppraisalExpectedCaseCount(string version) => version == "v3" ? 38 : 32;
        private static string AppraisalFixtureId(string version) =>
            "npc-experience-appraisal-live-scenarios-" + version;
        private static string[] ReadLines(string path) => File.ReadAllLines(path, Encoding.UTF8).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}

