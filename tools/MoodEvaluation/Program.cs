using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.MoodEvaluation
{
    internal static class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, WriteIndented = false };

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "self-test") return SelfTest();
                if (args.Length == 1 && (args[0] == "help" || args[0] == "--help"))
                {
                    PrintHelp();
                    return 0;
                }
                if (args.Length > 0 && args[0] == "run")
                    return RunCommand(args).GetAwaiter().GetResult();
                PrintHelp();
                return 2;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Offline self-test: dotnet run --project tools/MoodEvaluation -- self-test");
            Console.WriteLine("Parent-only local model run: dotnet run --project tools/MoodEvaluation -- run --endpoint http://127.0.0.1:8080/completion --output work/mood-evaluation-20261021/run.jsonl");
        }

        private static int SelfTest()
        {
            MoodScenarioCorpus corpus = MoodScenarioCorpus.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "scenarios-v1.json"));
            Require(corpus.Scenarios.Count == 12 && corpus.Scenarios.All(item => item.Turns.Count == 3),
                "Corpus must contain exactly 12 three-turn scenarios.");
            Require(corpus.Scenarios.Single(item => item.Category == "stable-personality").InitialMood == 45,
                "Stable-personality scenario must begin at mood +45.");
            Require(corpus.Scenarios.Single(item => item.Category == "mixed-control").InitialMood == -45,
                "Mixed control must begin at mood -45.");
            Require(corpus.Scenarios.All(item => item.Turns.All(turn => turn.ExpectedReply == null)),
                "Corpus must not freeze NPC prose or add a semantic language grader.");

            string fixtureHash = HashFile(Path.Combine(AppContext.BaseDirectory, "fixtures", "scenarios-v1.json"));
            foreach (MoodScenario scenario in corpus.Scenarios)
            {
                WorldStateStore store = NewStore(corpus.NativeFixture.PersonId, scenario.InitialMood);
                var history = new StringBuilder("Conversation: " + corpus.NativeFixture.Clock + ", Gothway Garden.\n");
                for (int turnIndex = 0; turnIndex < scenario.Turns.Count; turnIndex++)
                {
                    MoodTurn turn = scenario.Turns[turnIndex];
                    ConversationMoodView mood = store.CaptureConversationMood(corpus.NativeFixture.PersonId);
                    BuiltTurn built = BuildTurn(corpus, scenario, turn, history.ToString(), mood.Value,
                        mood.AcceptedTurnSequence, turnIndex, 0);
                    DialogueCompletion completion = LocalDialogueCompletion.Build(
                        new LocalDialogueConnection("http://127.0.0.1:8080/completion", 10000), built.Request);
                    using (JsonDocument completionJson = JsonDocument.Parse(completion.JsonBody))
                        Require(completionJson.RootElement.GetProperty("grammar").GetString() == SocialDialogueResult.Grammar(),
                            "Production completion must include typed mood grammar.");
                    Require(completion.Prompt.Contains("Current persistent mood value: " + mood.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
                        "Production prompt must include current mood value.");
                    Require(completion.Prompt.Length > turn.PlayerText.Length && completion.History == history.ToString(),
                        "Production prompt must preserve current text and history.");
                    Require(completion.JsonBody.Contains("\"n_predict\":1024", StringComparison.Ordinal),
                        "Mood response token budget must use production configuration.");
                    if (scenario.NpcOutdoors)
                    {
                        if (scenario.Weather != null)
                            Require(built.Context.Contains("NPC weather:", StringComparison.Ordinal), "Verified outdoor weather must reach context formatter.");
                    }
                    else Require(!built.Context.Contains("NPC weather:", StringComparison.Ordinal), "Indoor synthetic variants must not receive NPC weather.");
                }
            }

            SocialDialogueResult parsed;
            Require(SocialDialogueResult.TryParse("{\"reply\":\"A brief reply.\",\"moodDelta\":3}", out parsed),
                "Production parser must accept valid typed result.");
            Require(!SocialDialogueResult.TryParse("{\"reply\":\"A brief reply.\",\"moodDelta\":3,\"moodDelta\":4}", out parsed),
                "Production parser must reject duplicate moodDelta.");
            WorldStateStore transaction = NewStore(corpus.NativeFixture.PersonId, 98);
            long committedSequence;
            Require(transaction.TryCommitAcceptedSocialExchange(corpus.NativeFixture.PersonId, 0, 3, 36000,
                "3 Midyear 405", "Gothway Garden", "Thank you.", "A brief reply.", out committedSequence),
                "Core social mood transaction must commit.");
            ConversationMoodView after = transaction.CaptureConversationMood(corpus.NativeFixture.PersonId);
            Require(after.Value == 100 && after.AcceptedTurnSequence == 1 && committedSequence == 1,
                "Core transaction must clamp mood and increment sequence once.");
            Require(transaction.ConversationTranscript(corpus.NativeFixture.PersonId, 36000, 64).Count == 1,
                "Core transaction must append exactly one memory exchange.");
            WorldStateStore actionTransaction = NewStore(corpus.NativeFixture.PersonId, 0);
            Require(actionTransaction.TryCommitAcceptedActionExchange(corpus.NativeFixture.PersonId, 0, -2, 36000,
                "3 Midyear 405", "Gothway Garden", "Wait here for me.", "I cannot agree to wait.", out committedSequence),
                "Core action mood transaction must commit the typed denied outcome.");
            List<NpcMemoryEntry> actionTranscript = actionTransaction.ConversationTranscript(corpus.NativeFixture.PersonId, 36000, 64);
            Require(actionTranscript.Count == 1 && actionTranscript[0].Source == "action",
                "Denied-action mood update must preserve action memory provenance.");

            Console.WriteLine("SELF-TEST PASS: 12 scenarios, production prompts/parser/transactions; fixture SHA-256=" + fixtureHash);
            return 0;
        }

        private static async Task<int> RunCommand(string[] args)
        {
            string endpointText = Option(args, "--endpoint");
            string outputPath = Option(args, "--output");
            if (endpointText == null || outputPath == null)
                throw new ArgumentException("run requires --endpoint and --output.");
            var connection = new LocalDialogueConnection(endpointText, 10000);
            MoodScenarioCorpus corpus = MoodScenarioCorpus.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "scenarios-v1.json"));
            string sourceFixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures", "scenarios-v1.json"));
            string fixtureHash = HashFile(sourceFixture);
            string absoluteOutput = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput));

            using (var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) })
            using (var writer = new StreamWriter(absoluteOutput, false, new UTF8Encoding(false)))
            {
                HttpEvent models = await SendAsync(client, HttpMethod.Get, new Uri(connection.Endpoint, "/v1/models"), null, CancellationToken.None).ConfigureAwait(false);
                WriteLine(writer, new { recordType = "run-start", corpusId = corpus.CorpusId, fixtureHash,
                    endpoint = connection.Endpoint.AbsoluteUri, nativeFixture = corpus.NativeFixture, modelMetadata = models });
                int mechanicalFailures = 0, diagnosticOutOfRange = 0, completionCount = 0, completionResponses = 0;
                int scenarioIndex = 0;
                foreach (MoodScenario scenario in corpus.Scenarios)
                {
                    WorldStateStore store = NewStore(corpus.NativeFixture.PersonId, scenario.InitialMood);
                    var history = new StringBuilder("Conversation: " + corpus.NativeFixture.Clock + ", Gothway Garden.\n");
                    for (int turnIndex = 0; turnIndex < scenario.Turns.Count; turnIndex++)
                    {
                        MoodTurn turn = scenario.Turns[turnIndex];
                        bool historyAppended = false;
                        var record = new TurnRecord
                        {
                            RecordType = "turn", CorpusId = corpus.CorpusId, FixtureHash = fixtureHash,
                            ScenarioId = scenario.Id, Category = scenario.Category, SceneVariation = scenario.SceneVariation,
                            TurnIndex = turnIndex + 1, PlayerText = turn.PlayerText,
                            ExpectedDeltaMinimum = turn.MinimumDiagnosticDelta, ExpectedDeltaMaximum = turn.MaximumDiagnosticDelta,
                            MoodBefore = store.CaptureConversationMood(corpus.NativeFixture.PersonId).Value,
                            SequenceBefore = store.CaptureConversationMood(corpus.NativeFixture.PersonId).AcceptedTurnSequence,
                            ModelMetadata = models
                        };
                        try
                        {
                            HttpEvent props = await SendAsync(client, HttpMethod.Get, new Uri(connection.Endpoint, "/props"), null, CancellationToken.None).ConfigureAwait(false);
                            record.Props = props;
                            int nContext = ParseContextSize(props.ResponseBody);
                            BuiltTurn built = BuildTurn(corpus, scenario, turn, history.ToString(), record.MoodBefore,
                                record.SequenceBefore, turnIndex, scenarioIndex);
                            record.Context = built.Context;
                            DialogueCompletion completion = LocalDialogueCompletion.Build(connection, built.Request);
                            int budget = nContext - completion.ResponseTokens - 32;
                            string fitted = DialogueHistoryWindow.Fit(completion.Prompt, completion.History, budget, prompt =>
                            {
                                string tokenRequest = "{\"content\":\"" + LocalDialogueCompletion.EscapeJson(prompt) + "\",\"add_special\":true}";
                                HttpEvent tokenEvent = SendAsync(client, HttpMethod.Post, new Uri(connection.Endpoint, "/tokenize"),
                                    tokenRequest, CancellationToken.None).GetAwaiter().GetResult();
                                record.Tokenize.Add(tokenEvent);
                                if (tokenEvent.StatusCode < 200 || tokenEvent.StatusCode >= 300)
                                    throw new InvalidOperationException("/tokenize returned HTTP " + tokenEvent.StatusCode.ToString(CultureInfo.InvariantCulture));
                                return ParseTokenCount(tokenEvent.ResponseBody);
                            }, completion.StructuredHistory);
                            record.TokenBudget = budget;
                            if (fitted == null) throw new InvalidOperationException("DialogueHistoryWindow.Fit returned null for current request.");
                            string requestJson = completion.JsonBody.Replace("\"prompt\":\"" + LocalDialogueCompletion.EscapeJson(completion.Prompt) + "\"",
                                "\"prompt\":\"" + LocalDialogueCompletion.EscapeJson(fitted) + "\"");
                            record.RequestPrompt = fitted;
                            record.RequestJson = requestJson;
                            var stopwatch = Stopwatch.StartNew();
                            HttpEvent response = await SendAsync(client, HttpMethod.Post, connection.Endpoint, requestJson, CancellationToken.None).ConfigureAwait(false);
                            stopwatch.Stop();
                            completionCount++;
                            if (response.StatusCode >= 200 && response.StatusCode < 300) completionResponses++;
                            record.CompletionResponse = response;
                            record.CompletionLatencyMs = stopwatch.Elapsed.TotalMilliseconds;
                            record.ExtractedContent = DialogueCompletionText.Extract(response.ResponseBody);
                            SocialDialogueResult result = null;
                            record.ParseAccepted = response.StatusCode >= 200 && response.StatusCode < 300 &&
                                SocialDialogueResult.TryParse(record.ExtractedContent, out result);
                            int previousTranscriptCount = store.ConversationTranscript(corpus.NativeFixture.PersonId, GameTime(scenarioIndex, turnIndex), 64).Count;
                            if (record.ParseAccepted)
                            {
                                record.Reply = result.Reply;
                                record.MoodDelta = result.MoodDelta;
                                record.DiagnosticWithinRange = result.MoodDelta >= turn.MinimumDiagnosticDelta && result.MoodDelta <= turn.MaximumDiagnosticDelta;
                                if (record.DiagnosticWithinRange == false) diagnosticOutOfRange++;
                                long sequence;
                                double at = GameTime(scenarioIndex, turnIndex);
                                record.CommitAccepted = built.Request.ActionReply == null
                                    ? store.TryCommitAcceptedSocialExchange(corpus.NativeFixture.PersonId, record.SequenceBefore,
                                        result.MoodDelta, at, "3 Midyear 405", "Gothway Garden", turn.PlayerText, result.Reply, out sequence)
                                    : store.TryCommitAcceptedActionExchange(corpus.NativeFixture.PersonId, record.SequenceBefore,
                                        result.MoodDelta, at, "3 Midyear 405", "Gothway Garden", turn.PlayerText, result.Reply, out sequence);
                                record.CommittedSequence = record.CommitAccepted ? sequence : 0;
                                record.CommitSource = built.Request.ActionReply == null ? "model" : "action";
                            }
                            else
                            {
                                result = null;
                                record.MechanicalError = response.StatusCode < 200 || response.StatusCode >= 300
                                    ? "completion-http-status" : "typed-response-rejected";
                                history.Append("Player: ").Append(turn.PlayerText).Append('\n')
                                    .Append(DialogueHistoryWindow.UnavailableResponseLabel).Append(DialogueReplyFallback.UnavailableReply).Append('\n');
                                historyAppended = true;
                            }
                            ConversationMoodView after = store.CaptureConversationMood(corpus.NativeFixture.PersonId);
                            int transcriptCount = store.ConversationTranscript(corpus.NativeFixture.PersonId, GameTime(scenarioIndex, turnIndex), 64).Count;
                            record.MoodAfter = after.Value;
                            record.SequenceAfter = after.AcceptedTurnSequence;
                            record.TranscriptCountAfter = transcriptCount;
                            if (record.ParseAccepted)
                            {
                                bool committedAtomically = record.CommitAccepted && after.AcceptedTurnSequence == record.SequenceBefore + 1 &&
                                    after.Value == Math.Max(-100, Math.Min(100, record.MoodBefore + record.MoodDelta.Value)) &&
                                    transcriptCount == previousTranscriptCount + 1;
                                if (!committedAtomically) record.MechanicalError = "mood-memory-transaction-invariant";
                                if (committedAtomically)
                                    history.Append("Player: ").Append(turn.PlayerText).Append('\n').Append("NPC: ").Append(record.Reply).Append('\n');
                                else
                                    history.Append("Player: ").Append(turn.PlayerText).Append('\n')
                                        .Append(DialogueHistoryWindow.UnavailableResponseLabel).Append(DialogueReplyFallback.UnavailableReply).Append('\n');
                                historyAppended = true;
                            }
                            else
                            {
                                bool unchanged = !record.CommitAccepted && after.Value == record.MoodBefore &&
                                    after.AcceptedTurnSequence == record.SequenceBefore && transcriptCount == previousTranscriptCount;
                                if (!unchanged) record.MechanicalError = "rejected-response-mutated-state";
                            }
                            if (turnIndex + 1 < scenario.Turns.Count)
                            {
                                MoodTurn nextTurn = scenario.Turns[turnIndex + 1];
                                BuiltTurn nextBuilt = BuildTurn(corpus, scenario, nextTurn, history.ToString(), after.Value,
                                    after.AcceptedTurnSequence, turnIndex + 1, scenarioIndex);
                                record.NextContext = nextBuilt.Context;
                                record.NextRequestPrompt = LocalDialogueCompletion.Build(connection, nextBuilt.Request).Prompt;
                                record.NextMoodValue = after.Value;
                                record.NextAcceptedSequence = after.AcceptedTurnSequence;
                            }
                        }
                        catch (Exception error)
                        {
                            record.MechanicalError = error.GetType().Name + ": " + error.Message;
                            if (!historyAppended)
                            {
                                history.Append("Player: ").Append(turn.PlayerText).Append('\n')
                                    .Append(DialogueHistoryWindow.UnavailableResponseLabel).Append(DialogueReplyFallback.UnavailableReply).Append('\n');
                                historyAppended = true;
                            }
                        }
                        if (record.MechanicalError != null) mechanicalFailures++;
                        WriteLine(writer, record);
                    }
                    scenarioIndex++;
                }
                WriteLine(writer, new { recordType = "run-summary", corpusId = corpus.CorpusId, fixtureHash,
                    completionCount, completionResponses, mechanicalFailures, diagnosticOutOfRange,
                    diagnosticOnly = true, modelMetadata = models });
                Console.WriteLine("Run finished: completions=" + completionCount.ToString(CultureInfo.InvariantCulture) +
                    "; completionResponses=" + completionResponses.ToString(CultureInfo.InvariantCulture) +
                    "; mechanicalFailures=" + mechanicalFailures.ToString(CultureInfo.InvariantCulture) +
                    "; diagnosticOutOfRange=" + diagnosticOutOfRange.ToString(CultureInfo.InvariantCulture) +
                    "; diagnosticOnly=true; output=" + absoluteOutput);
                return mechanicalFailures == 0 && completionCount == 36 ? 0 : 1;
            }
        }

        private static BuiltTurn BuildTurn(MoodScenarioCorpus corpus, MoodScenario scenario, MoodTurn turn,
            string history, int mood, long sequence, int turnIndex, int scenarioIndex)
        {
            double at = GameTime(scenarioIndex, turnIndex);
            string sceneId = "mood-eval-" + scenario.Id;
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, corpus.NativeFixture.PersonId,
                null, "mood-eval-session-" + scenario.Id, turnIndex + 1, 1,
                "0123456789abcdef0123456789abcdef", "", sceneId, turnIndex + 1);
            ConversationContextFact<string> clock = ConversationContextFact<string>.Verified(corpus.NativeFixture.Clock,
                ConversationContextFactSource.Derived, at);
            ConversationContextFact<string> name = ConversationContextFact<string>.Verified(corpus.Profile.Name,
                ConversationContextFactSource.Derived, at);
            ConversationContextFact<ConversationDialogueRole> role = ConversationContextFact<ConversationDialogueRole>.Verified(
                ConversationDialogueRole.ManagedResident, ConversationContextFactSource.Derived, at);
            ConversationContextFact<string> playerScene = ConversationContextFact<string>.Verified(
                "Gothway Garden, " + scenario.SceneVariation, ConversationContextFactSource.Derived, at);
            ConversationContextFact<string> npcScene = ConversationContextFact<string>.Verified(
                sceneId, ConversationContextFactSource.Derived, at);
            ConversationContextFact<bool> outdoors = ConversationContextFact<bool>.Verified(scenario.NpcOutdoors,
                ConversationContextFactSource.Derived, at);
            ConversationContextFact<ConversationNpcWeatherObservation> weather = scenario.NpcOutdoors && scenario.Weather != null
                ? ConversationContextFact<ConversationNpcWeatherObservation>.Verified(
                    new ConversationNpcWeatherObservation(scenario.Weather, sceneId, turnIndex + 1),
                    ConversationContextFactSource.NpcSceneObservation, at)
                : ConversationContextFact<ConversationNpcWeatherObservation>.Unknown();
            ConversationContextFact<string> personality = ConversationContextFact<string>.Verified(
                corpus.Profile.PersonalityTraits, ConversationContextFactSource.Derived, at);
            var place = new AppointmentPlace { Kind = "world-point", SettlementId = 206728581, BuildingKey = -1,
                X = 0, Y = 0, Z = 0, Detail = "Gothway Garden, synthetic evaluation location" };
            var location = new ConversationLocationSnapshot(at, place);
            var snapshot = new ConversationContextSnapshot(binding, at, clock, name, role, playerScene, npcScene,
                ConversationContextFact<ConversationNpcPosition>.Unknown(), ConversationContextFact<ConversationNpcActivity>.Unknown(),
                outdoors, weather, personality, corpus.Profile.Disposition, 123u, location,
                Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>(),
                ConversationContextPersonalState.Unknown(), new ConversationContextMood(mood, sequence));
            string context = turn.ActionStatus == null
                ? ConversationContextFormatter.ForSocialReply(snapshot, 3072)
                : ConversationContextFormatter.ForActionVoice(snapshot, 2400);
            ConversationActionReply actionReply = null;
            if (turn.ActionStatus == "Rejected")
                actionReply = ConversationActionReply.Rejected(Enum.Parse<NpcActionKind>(turn.ActionKind, false));
            string fallback = actionReply == null ? DialogueReplyFallback.UnavailableReply : actionReply.NpcText;
            var request = new DialogueRequest(turn.PlayerText, fallback, corpus.Profile.Name,
                corpus.Profile.Race, corpus.Profile.Gender, corpus.Profile.Role, corpus.Profile.Disposition,
                socialOnly: actionReply == null, conversationReaction: context, history: history,
                actionReply: actionReply, moodEnabled: true, moodValue: mood);
            return new BuiltTurn { Request = request, Context = context };
        }

        private static WorldStateStore NewStore(string personId, int initialMood)
        {
            var store = new WorldStateStore(1987);
            WorldState state = store.Snapshot();
            state.ConversationMoods.Add(new ConversationMood { PersonId = personId, Value = initialMood, AcceptedTurnSequence = 0 });
            store.Restore(state);
            return store;
        }

        private static double GameTime(int scenarioIndex, int turnIndex)
        { return 36000d + scenarioIndex * 300d + turnIndex * 60d; }

        private static async Task<HttpEvent> SendAsync(HttpClient client, HttpMethod method, Uri uri, string body,
            CancellationToken cancellationToken)
        {
            var result = new HttpEvent { Method = method.Method, Uri = uri.AbsoluteUri, RequestBody = body };
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var request = new HttpRequestMessage(method, uri))
                {
                    if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        result.StatusCode = (int)response.StatusCode;
                        result.ResponseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (HttpRequestException error) { result.Error = error.GetType().Name + ": " + error.Message; }
            catch (TaskCanceledException error) { result.Error = error.GetType().Name + ": " + error.Message; }
            stopwatch.Stop();
            result.LatencyMs = stopwatch.Elapsed.TotalMilliseconds;
            return result;
        }

        private static int ParseContextSize(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                int result;
                if (!FindInt(document.RootElement, "n_ctx", out result) || result < 1)
                    throw new InvalidOperationException("/props response has no positive n_ctx.");
                return result;
            }
        }

        private static bool FindInt(JsonElement element, string name, out int result)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name == name && property.Value.TryGetInt32(out result)) return true;
                    if (FindInt(property.Value, name, out result)) return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (JsonElement child in element.EnumerateArray())
                    if (FindInt(child, name, out result)) return true;
            result = 0;
            return false;
        }

        private static int ParseTokenCount(string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                if (FindArrayLength(document.RootElement, "tokens", out int count)) return count;
            }
            throw new InvalidOperationException("/tokenize response has no tokens array.");
        }

        private static bool FindArrayLength(JsonElement element, string name, out int count)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name == name && property.Value.ValueKind == JsonValueKind.Array)
                    { count = property.Value.GetArrayLength(); return true; }
                    if (FindArrayLength(property.Value, name, out count)) return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (JsonElement child in element.EnumerateArray())
                    if (FindArrayLength(child, name, out count)) return true;
            count = 0;
            return false;
        }

        private static string Option(string[] args, string name)
        {
            for (int index = 1; index + 1 < args.Length; index++)
                if (args[index] == name) return args[index + 1];
            return null;
        }

        private static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static void WriteLine(StreamWriter writer, object value)
        {
            writer.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
            writer.Flush();
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private sealed class BuiltTurn
        { public DialogueRequest Request; public string Context; }

        private sealed class HttpEvent
        {
            public string Method { get; set; }
            public string Uri { get; set; }
            public string RequestBody { get; set; }
            public int StatusCode { get; set; }
            public string ResponseBody { get; set; }
            public double LatencyMs { get; set; }
            public string Error { get; set; }
        }

        private sealed class TurnRecord
        {
            public string RecordType { get; set; }
            public string CorpusId { get; set; }
            public string FixtureHash { get; set; }
            public string ScenarioId { get; set; }
            public string Category { get; set; }
            public string SceneVariation { get; set; }
            public int TurnIndex { get; set; }
            public string PlayerText { get; set; }
            public int ExpectedDeltaMinimum { get; set; }
            public int ExpectedDeltaMaximum { get; set; }
            public int MoodBefore { get; set; }
            public int MoodAfter { get; set; }
            public long SequenceBefore { get; set; }
            public long SequenceAfter { get; set; }
            public long CommittedSequence { get; set; }
            public int TranscriptCountAfter { get; set; }
            public string Context { get; set; }
            public string RequestPrompt { get; set; }
            public string RequestJson { get; set; }
            public HttpEvent ModelMetadata { get; set; }
            public HttpEvent Props { get; set; }
            public List<HttpEvent> Tokenize { get; set; } = new List<HttpEvent>();
            public HttpEvent CompletionResponse { get; set; }
            public double CompletionLatencyMs { get; set; }
            public string ExtractedContent { get; set; }
            public bool ParseAccepted { get; set; }
            public bool CommitAccepted { get; set; }
            public string CommitSource { get; set; }
            public int? MoodDelta { get; set; }
            public string Reply { get; set; }
            public bool? DiagnosticWithinRange { get; set; }
            public string MechanicalError { get; set; }
            public string NextContext { get; set; }
            public string NextRequestPrompt { get; set; }
            public int? NextMoodValue { get; set; }
            public long? NextAcceptedSequence { get; set; }
            public int TokenBudget { get; set; }
        }
    }

    internal sealed class MoodScenarioCorpus
    {
        public int SchemaVersion { get; set; }
        public string CorpusId { get; set; }
        public NativeFixture NativeFixture { get; set; }
        public MoodProfile Profile { get; set; }
        public List<MoodScenario> Scenarios { get; set; }

        public static MoodScenarioCorpus Load(string path)
        {
            MoodScenarioCorpus corpus = JsonSerializer.Deserialize<MoodScenarioCorpus>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (corpus == null || corpus.SchemaVersion != 1 || corpus.CorpusId != "persistent-mood-behavior-v1" ||
                corpus.NativeFixture == null || corpus.Profile == null || corpus.Scenarios == null || corpus.Scenarios.Count != 12)
                throw new InvalidDataException("Mood scenario corpus header or scenario count is invalid.");
            if (string.IsNullOrWhiteSpace(corpus.NativeFixture.PersonId) || string.IsNullOrWhiteSpace(corpus.NativeFixture.Clock) ||
                string.IsNullOrWhiteSpace(corpus.NativeFixture.Provenance) || string.IsNullOrWhiteSpace(corpus.NativeFixture.ClockProvenance))
                throw new InvalidDataException("Native fixture identity or synthetic provenance is missing.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (MoodScenario scenario in corpus.Scenarios)
            {
                if (scenario == null || string.IsNullOrWhiteSpace(scenario.Id) || !ids.Add(scenario.Id) ||
                    string.IsNullOrWhiteSpace(scenario.Category) || string.IsNullOrWhiteSpace(scenario.SceneVariation) ||
                    scenario.InitialMood < -100 || scenario.InitialMood > 100 || scenario.Turns == null || scenario.Turns.Count != 3)
                    throw new InvalidDataException("Invalid mood scenario or scenario id.");
                foreach (MoodTurn turn in scenario.Turns)
                {
                    if (turn == null || string.IsNullOrWhiteSpace(turn.PlayerText) || turn.PlayerText.Length > 600 ||
                        turn.MinimumDiagnosticDelta < -10 || turn.MaximumDiagnosticDelta > 10 ||
                        turn.MinimumDiagnosticDelta > turn.MaximumDiagnosticDelta ||
                        (turn.ActionStatus != null && (turn.ActionStatus != "Rejected" || string.IsNullOrWhiteSpace(turn.ActionKind))))
                        throw new InvalidDataException("Invalid turn in scenario " + scenario.Id + ".");
                }
                if (scenario.NpcOutdoors && scenario.Weather == null)
                    throw new InvalidDataException("Outdoor scenario lacks verified synthetic NPC weather: " + scenario.Id);
                if (!scenario.NpcOutdoors && scenario.Weather != null)
                    throw new InvalidDataException("Indoor scenario must not inject NPC weather: " + scenario.Id);
            }
            string[] expectedCategories = { "gratitude", "insult", "apology-repair", "neutral-facts", "ambiguous-humor",
                "truthful-denied-action", "stable-personality", "indoor-bad-weather", "outdoor-bad-weather", "mood-max-injection" };
            foreach (string category in expectedCategories)
                if (!corpus.Scenarios.Any(item => item.Category == category))
                    throw new InvalidDataException("Missing scenario category " + category + ".");
            MoodScenario denied = corpus.Scenarios.Single(item => item.Category == "truthful-denied-action");
            if (denied.Turns[0].ActionStatus != "Rejected" || denied.Turns[0].ActionKind != "Wait")
                throw new InvalidDataException("Denied-action scenario needs a typed rejected Wait receipt.");
            return corpus;
        }
    }

    internal sealed class NativeFixture
    {
        public string PersonId { get; set; }
        public string Save { get; set; }
        public string Clock { get; set; }
        public string ClockProvenance { get; set; }
        public string Provenance { get; set; }
    }

    internal sealed class MoodProfile
    {
        public string Name { get; set; }
        public string Race { get; set; }
        public string Gender { get; set; }
        public string Role { get; set; }
        public string PersonalityTraits { get; set; }
        public int Disposition { get; set; }
    }

    internal sealed class MoodScenario
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public int InitialMood { get; set; }
        public bool NpcOutdoors { get; set; }
        public string Weather { get; set; }
        public string SceneVariation { get; set; }
        public List<MoodTurn> Turns { get; set; }
    }

    internal sealed class MoodTurn
    {
        public string PlayerText { get; set; }
        public string ActionStatus { get; set; }
        public string ActionKind { get; set; }
        public int MinimumDiagnosticDelta { get; set; }
        public int MaximumDiagnosticDelta { get; set; }
        public string ExpectedReply { get; set; }
    }
}
