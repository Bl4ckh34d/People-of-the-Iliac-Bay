using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

namespace ConversationBoundaryEvaluation;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--validate-suite")
            {
                var fixture = LoadSuite(args[1]);
                ValidateWireOffline(fixture);
                Console.WriteLine($"suite/wire valid: {fixture.Cases.Count} cases; sha256={fixture.Hash}");
                return 0;
            }
            if (args.Length is < 2 or > 4 || args.Length == 4 && args[3] != "--allow-gpu")
            {
                Console.Error.WriteLine("Usage: ConversationBoundaryEvaluation <suite.json> <new-output-directory> [http://127.0.0.1:8088] [--allow-gpu]");
                Console.Error.WriteLine("       ConversationBoundaryEvaluation --validate-suite <suite.json>");
                return 2;
            }

            var suite = LoadSuite(args[0]);
            bool allowGpu = args.Length == 4;
            var endpointRoot = args.Length >= 3 ? args[2] : "http://127.0.0.1:8088";
            if (!Uri.TryCreate(endpointRoot, UriKind.Absolute, out var rootUri) ||
                rootUri.AbsolutePath != "/" || rootUri.Query.Length != 0 || rootUri.Fragment.Length != 0)
                throw new ArgumentException("Endpoint root must be a local HTTP origin without path, query, or fragment.");
            var connection = new LocalDialogueConnection(endpointRoot.TrimEnd('/') + "/completion", 120000);
            if (Directory.Exists(args[1]) || File.Exists(args[1]))
                throw new InvalidOperationException("Output path already exists; a model evaluation cannot be overwritten or rerun into it.");
            Directory.CreateDirectory(args[1]);
            var output = Path.GetFullPath(args[1]);
            WriteNew(output, "manifest.json", new {
                suitePath = Path.GetFullPath(args[0]), suiteSha256 = suite.Hash,
                caseCount = suite.Cases.Count, endpoint = connection.Endpoint.ToString(),
                startedUtc = DateTimeOffset.UtcNow,
                coreAssemblySha256 = HashBytes(File.ReadAllBytes(typeof(NpcSpeechActRequest).Assembly.Location)),
                evaluatorAssemblySha256 = HashBytes(File.ReadAllBytes(typeof(Program).Assembly.Location)),
                allowGpu,
                note = "One pass; no retries; production Core request/policy and linked mod wire/template. GPU use requires explicit --allow-gpu; default requires CPU-only."
            });

            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(120) };
            var props = await Exchange(client, HttpMethod.Get, new Uri(connection.Endpoint, "/props"), null);
            WriteNew(output, "props.json", props);
            if (props.Status != 200 || props.Error != null)
                throw new InvalidOperationException("/props failed; see props.json. No case was sent.");
            var (modelPath, contextSize, cpuEvidence) = CheckProps(props.ResponseBody, allowGpu);
            WriteNew(output, "server.json", new { modelPath, contextSize, cpuEvidence, propsResponseSha256 = props.ResponseSha256 });

            var results = new List<CaseResult>();
            for (int i = 0; i < suite.Cases.Count; i++)
            {
                var item = suite.Cases[i];
                var result = await RunCase(client, connection, contextSize, item);
                results.Add(result);
                WriteNew(output, $"{i + 1:00}-{item.Id}.json", result);
                Console.WriteLine($"{i + 1:00}/{suite.Cases.Count:00} {item.Id}: " +
                    (result.Error != null ? $"ERROR {result.Error}" : result.Pass ? "PASS" : $"FAIL {result.ActualLabel ?? "<unparsed>"}/{result.ActualAction}"));
                if (result.Error != null) break; // transport/protocol failure: no retries or later calls
            }
            var failed = results.Count(x => !x.Pass);
            WriteNew(output, "summary.json", new {
                suiteSha256 = suite.Hash, modelPath, cpuEvidence, allowGpu,
                completed = results.Count, planned = suite.Cases.Count,
                passed = results.Count - failed, failed,
                endedUtc = DateTimeOffset.UtcNow,
                results = results.Select(x => new { x.Id, x.ExpectedLabels, x.ExpectedAction, x.ActualLabel,
                    x.ActualAction, x.SemanticPass, x.ActionPass, x.Pass, x.Error }).ToArray()
            });
            return failed == 0 && results.Count == suite.Cases.Count ? 0 : 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.GetType().Name + ": " + e.Message);
            return 2;
        }
    }

    private static Fixture LoadSuite(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = HashBytes(bytes);
        var lockPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "suite-lock.sha256");
        var lockedHash = File.ReadAllText(lockPath).Trim();
        if (!string.Equals(hash, lockedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Suite hash differs from frozen suite-lock.sha256: {hash}");
        var suite = JsonSerializer.Deserialize<Suite>(bytes, Json) ?? throw new InvalidOperationException("Invalid suite JSON.");
        if (suite.Version != 1 || suite.Cases == null || suite.Cases.Count is < 24 or > 30)
            throw new InvalidOperationException("Suite v1 requires 24-30 cases.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in suite.Cases)
        {
            if (item.Id == null || item.Id.Length is < 2 or > 60 || item.Id.Any(c => !((c >= 'a' && c <= 'z') || char.IsAsciiDigit(c) || c == '-')) || !ids.Add(item.Id))
                throw new InvalidOperationException("Invalid or duplicate case id.");
            if (string.IsNullOrWhiteSpace(item.Category) || string.IsNullOrWhiteSpace(item.Utterance) || item.History == null ||
                item.AllowedLabels == null || item.AllowedLabels.Count == 0 || string.IsNullOrWhiteSpace(item.ExpectedAction))
                throw new InvalidOperationException("Incomplete case: " + item.Id);
            var (source, request) = CreateRequest(item);
            if (!Enum.TryParse<ConversationSpeechBoundaryAction>(item.ExpectedAction, out var expectedAction) ||
                expectedAction.ToString() != item.ExpectedAction)
                throw new InvalidOperationException("Invalid expected action: " + item.Id);
            foreach (string label in item.AllowedLabels)
            {
                if (!LocalSpeechActLabelWire.TryParse(request, label, out var classification) ||
                    ConversationSpeechBoundaryPolicy.Decide(source, classification) != expectedAction)
                    throw new InvalidOperationException($"Expected label/action violates production policy: {item.Id}: {label}/{item.ExpectedAction}");
            }
        }
        return new Fixture(suite.Cases, hash);
    }

    private static (object Source, NpcSpeechActRequest Request) CreateRequest(Case item)
    {
        var source = new object();
        string history = string.Join("\n", item.History.Select(t => {
            if (string.IsNullOrWhiteSpace(t.SpeakerId) || string.IsNullOrWhiteSpace(t.Text) || t.Text.Contains('\n'))
                throw new InvalidOperationException("Invalid history turn in " + item.Id);
            return t.SpeakerId + ": " + t.Text;
        }));
        var request = NpcSpeechActRequest.Create(source, "npc:listener", "player",
            new[] { "npc:spouse", "npc:child" }, item.Utterance, history,
            item.ListenerRefusesFurtherCommunication);
        return (source, request);
    }

    private static void ValidateWireOffline(Fixture fixture)
    {
        var connection = new LocalDialogueConnection("http://127.0.0.1:8088/completion", 120000);
        foreach (var item in fixture.Cases)
        {
            var (_, request) = CreateRequest(item);
            var completion = LocalDialogueCompletion.Classify(connection,
                LocalSpeechActLabelWire.Prompt(request), string.Empty,
                LocalSpeechActLabelWire.Grammar(), LocalSpeechActLabelWire.ResponseTokens);
            using var template = JsonDocument.Parse(LocalDialogueNativeTemplate.Request(completion.Prompt));
            if (!template.RootElement.GetProperty("add_generation_prompt").GetBoolean() ||
                template.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean())
                throw new InvalidOperationException("Production native-template request settings changed.");
            var rendered = LocalDialogueNativeTemplate.ReadPrompt("{\"prompt\":\"synthetic-native-prompt\"}");
            using var payload = JsonDocument.Parse(LocalDialogueNativeTemplate.Payload(completion.JsonBody, rendered));
            if (payload.RootElement.GetProperty("prompt").GetString() != rendered ||
                payload.RootElement.GetProperty("grammar").GetString() != LocalSpeechActLabelWire.Grammar() ||
                payload.RootElement.GetProperty("n_predict").GetInt32() != LocalSpeechActLabelWire.ResponseTokens)
                throw new InvalidOperationException("Production completion payload changed.");
        }
    }

    private static async Task<CaseResult> RunCase(HttpClient client, LocalDialogueConnection connection, int contextSize, Case item)
    {
        var result = new CaseResult {
            Id = item.Id, Category = item.Category, Utterance = item.Utterance,
            History = item.History, ListenerRefusesFurtherCommunication = item.ListenerRefusesFurtherCommunication,
            ExpectedLabels = item.AllowedLabels, ExpectedAction = item.ExpectedAction
        };
        string stage = "request";
        try
        {
            var (source, request) = CreateRequest(item);
            result.RequestState = request.State;
            result.WirePrompt = LocalSpeechActLabelWire.Prompt(request);
            result.Grammar = LocalSpeechActLabelWire.Grammar();
            var completion = LocalDialogueCompletion.Classify(connection, result.WirePrompt, string.Empty,
                result.Grammar, LocalSpeechActLabelWire.ResponseTokens);
            result.CompletionPrompt = completion.Prompt;
            result.CompletionOriginalPayload = completion.JsonBody;
            result.PromptSha256 = HashText(completion.Prompt);
            result.GrammarSha256 = HashText(result.Grammar);
            int budget = contextSize - completion.ResponseTokens - 32;
            result.ContextTokenBudget = budget;
            if (budget <= 0) throw new InvalidOperationException("Invalid context budget.");

            stage = "apply-template";
            string templateRequest = LocalDialogueNativeTemplate.Request(completion.Prompt);
            result.ApplyTemplate = await Exchange(client, HttpMethod.Post,
                new Uri(connection.Endpoint, "/apply-template"), templateRequest);
            RequireSuccess(result.ApplyTemplate, stage);
            string rendered = LocalDialogueNativeTemplate.ReadPrompt(result.ApplyTemplate.ResponseBody);
            result.RenderedPrompt = rendered;
            result.RenderedPromptSha256 = HashText(rendered);

            stage = "tokenize";
            string tokenizeRequest = "{\"content\":\"" + LocalDialogueCompletion.EscapeJson(rendered) + "\",\"add_special\":true}";
            result.Tokenize = await Exchange(client, HttpMethod.Post,
                new Uri(connection.Endpoint, "/tokenize"), tokenizeRequest);
            RequireSuccess(result.Tokenize, stage);
            using (var tokenJson = JsonDocument.Parse(result.Tokenize.ResponseBody))
            {
                if (!tokenJson.RootElement.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("Missing token list.");
                result.PromptTokenCount = tokens.GetArrayLength();
            }
            if (DialogueHistoryWindow.Fit(completion.Prompt, completion.History, budget,
                _ => result.PromptTokenCount, completion.StructuredHistory) == null)
                throw new InvalidOperationException($"Context limit: {result.PromptTokenCount}>{budget}.");

            stage = "completion";
            string payload = LocalDialogueNativeTemplate.Payload(completion.JsonBody, rendered);
            result.Completion = await Exchange(client, HttpMethod.Post, completion.Endpoint, payload);
            RequireSuccess(result.Completion, stage);
            using (var completionJson = JsonDocument.Parse(result.Completion.ResponseBody))
                if (completionJson.RootElement.TryGetProperty("truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True)
                    throw new InvalidOperationException("Completion was truncated.");
            result.RawContent = DialogueCompletionText.Extract(result.Completion.ResponseBody);
            result.RawContentSha256 = HashText(result.RawContent);
            if (LocalSpeechActLabelWire.TryParse(request, result.RawContent, out var classification))
            {
                result.ActualLabel = classification.Kind.ToString();
                result.ActualAction = ConversationSpeechBoundaryPolicy.Decide(source, classification).ToString();
            }
            result.SemanticPass = result.ActualLabel != null && item.AllowedLabels.Contains(result.ActualLabel, StringComparer.Ordinal);
            result.ActionPass = result.ActualAction == item.ExpectedAction;
            result.Pass = result.SemanticPass && result.ActionPass;
        }
        catch (Exception e)
        {
            result.Error = stage + ": " + e.GetType().Name + ": " + e.Message;
        }
        return result;
    }

    private static void RequireSuccess(HttpExchange? exchange, string stage)
    {
        if (exchange == null || exchange.Status != 200 || exchange.Error != null)
            throw new InvalidOperationException(stage + " HTTP/transport failure; see recorded exchange.");
    }

    private static (string ModelPath, int ContextSize, string CpuEvidence) CheckProps(string json, bool allowGpu)
    {
        using var document = JsonDocument.Parse(json);
        var model = FindProperty(document.RootElement, "model_path");
        if (model == null || model.Value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("/props has no model_path; no cases sent.");
        string modelPath = model.Value.GetString() ?? string.Empty;
        if (!modelPath.Contains("gemma-4-E4B", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("/props model_path is not gemma-4-E4B; no cases sent.");
        var context = FindProperty(document.RootElement, "n_ctx");
        if (context == null || !context.Value.TryGetInt32(out int contextSize) || contextSize <= 0)
            throw new InvalidOperationException("/props has no valid n_ctx; no cases sent.");
        var gpuKeys = new[] { "n_gpu_layers", "n_gpu_layers_actual", "n_gpu_layers_requested", "gpu_layers", "offloaded_layers" };
        var observations = new List<string>();
        foreach (string key in gpuKeys)
        {
            var value = FindProperty(document.RootElement, key);
            if (value == null) continue;
            if (value.Value.ValueKind != JsonValueKind.Number || !value.Value.TryGetInt32(out int layers))
                throw new InvalidOperationException("/props GPU layer field is not an integer: " + key);
            observations.Add(key + "=" + layers);
            if (layers != 0 && !allowGpu)
                throw new InvalidOperationException("/props reports GPU-offloaded layers (" + key + "=" + layers + "); no cases sent.");
        }
        return (modelPath, contextSize,
            observations.Count == 0 ? "GPU layer count not exposed by /props; independently verify server execution device. GPU permitted=" + allowGpu : string.Join(", ", observations));
    }

    private static JsonElement? FindProperty(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name)) return property.Value;
                var nested = FindProperty(property.Value, name);
                if (nested != null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindProperty(child, name);
                if (nested != null) return nested;
            }
        return null;
    }

    private static async Task<HttpExchange> Exchange(HttpClient client, HttpMethod method, Uri uri, string? body)
    {
        var exchange = new HttpExchange { Method = method.Method, Url = uri.ToString(), RequestBody = body,
            RequestSha256 = body == null ? null : HashText(body), StartedUtc = DateTimeOffset.UtcNow };
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            exchange.Status = (int)response.StatusCode;
            exchange.ResponseBody = await response.Content.ReadAsStringAsync();
            exchange.ResponseSha256 = HashText(exchange.ResponseBody);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            exchange.Error = e.GetType().Name + ": " + e.Message;
        }
        watch.Stop();
        exchange.ElapsedMilliseconds = watch.ElapsedMilliseconds;
        return exchange;
    }

    private static void WriteNew(string dir, string file, object value)
    {
        using var stream = new FileStream(Path.Combine(dir, file), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, Json);
    }

    private static string HashText(string text) => HashBytes(Encoding.UTF8.GetBytes(text));
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record Fixture(List<Case> Cases, string Hash);
    private sealed class Suite { public int Version { get; set; } public List<Case>? Cases { get; set; } }
    private sealed class Case
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Utterance { get; set; } = string.Empty;
        public List<Turn> History { get; set; } = new();
        public bool ListenerRefusesFurtherCommunication { get; set; }
        public List<string> AllowedLabels { get; set; } = new();
        public string ExpectedAction { get; set; } = string.Empty;
    }
    private sealed class Turn { public string SpeakerId { get; set; } = string.Empty; public string Text { get; set; } = string.Empty; }
    private sealed class HttpExchange
    {
        public string? Method { get; set; }
        public string? Url { get; set; }
        public string? RequestBody { get; set; }
        public string? RequestSha256 { get; set; }
        public DateTimeOffset StartedUtc { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public int? Status { get; set; }
        public string ResponseBody { get; set; } = string.Empty;
        public string? ResponseSha256 { get; set; }
        public string? Error { get; set; }
    }
    private sealed class CaseResult
    {
        public string? Id { get; set; }
        public string? Category { get; set; }
        public string? Utterance { get; set; }
        public List<Turn>? History { get; set; }
        public bool ListenerRefusesFurtherCommunication { get; set; }
        public List<string>? ExpectedLabels { get; set; }
        public string? ExpectedAction { get; set; }
        public string? RequestState { get; set; }
        public string? WirePrompt { get; set; }
        public string? Grammar { get; set; }
        public string? CompletionPrompt { get; set; }
        public string? CompletionOriginalPayload { get; set; }
        public string? PromptSha256 { get; set; }
        public string? GrammarSha256 { get; set; }
        public int ContextTokenBudget { get; set; }
        public HttpExchange? ApplyTemplate { get; set; }
        public string? RenderedPrompt { get; set; }
        public string? RenderedPromptSha256 { get; set; }
        public HttpExchange? Tokenize { get; set; }
        public int PromptTokenCount { get; set; }
        public HttpExchange? Completion { get; set; }
        public string? RawContent { get; set; }
        public string? RawContentSha256 { get; set; }
        public string? ActualLabel { get; set; }
        public string? ActualAction { get; set; }
        public bool SemanticPass { get; set; }
        public bool ActionPass { get; set; }
        public bool Pass { get; set; }
        public string? Error { get; set; }
    }
}
