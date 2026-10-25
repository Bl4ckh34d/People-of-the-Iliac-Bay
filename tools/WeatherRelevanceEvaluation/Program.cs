using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

namespace WeatherRelevanceEvaluation;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] RequiredCaseIds =
    {
        "greeting-how-are-you-in-snow",
        "work-question-in-snow",
        "name-question-in-snow",
        "direct-weather-question-in-snow",
        "apology-after-harassment-with-negative-disposition",
        "prior-weather-mention-then-opinion-and-name"
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--validate-suite")
            {
                SuiteFixture fixture = LoadSuite(args[1]);
                ValidateProductionPathOffline(fixture);
                ValidateDeviceGateOffline();
                Console.WriteLine($"suite/core/native-template/device-gate valid: {fixture.Cases.Count} cases; sha256={fixture.Hash}");
                return 0;
            }

            bool allowGpu = args.Length >= 3 && args[^1] == "--allow-gpu";
            int positionalCount = args.Length - (allowGpu ? 1 : 0);
            if (args.Length is < 2 or > 4 || positionalCount is < 2 or > 3 ||
                args.Skip(positionalCount).Any(x => x != "--allow-gpu"))
            {
                Console.Error.WriteLine("Usage: WeatherRelevanceEvaluation <suite.json> <new-output-directory> [http://127.0.0.1:8088] [--allow-gpu]");
                Console.Error.WriteLine("       WeatherRelevanceEvaluation --validate-suite <suite.json>");
                return 2;
            }

            SuiteFixture suite = LoadSuite(args[0]);
            string endpointArgument = positionalCount == 3 ? args[2] : "http://127.0.0.1:8088";
            Uri endpointRoot = ParseLocalRoot(endpointArgument);
            string outputPath = Path.GetFullPath(args[1]);
            if (Directory.Exists(outputPath) || File.Exists(outputPath))
                throw new InvalidOperationException("Output path already exists; choose a new directory for this one-pass evaluation.");
            Directory.CreateDirectory(outputPath);

            var connection = new LocalDialogueConnection(new Uri(endpointRoot, "/completion").ToString(), 120000);
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(120)
            };

            WriteNew(outputPath, "manifest.json", new
            {
                suitePath = Path.GetFullPath(args[0]),
                suiteSha256 = suite.Hash,
                caseCount = suite.Cases.Count,
                endpoint = endpointRoot.ToString(),
                startedUtc = DateTimeOffset.UtcNow,
                coreAssemblySha256 = HashBytes(File.ReadAllBytes(typeof(LocalDialogueCompletion).Assembly.Location)),
                evaluatorAssemblySha256 = HashBytes(File.ReadAllBytes(typeof(Program).Assembly.Location)),
                allowGpu,
                devicePermission = allowGpu ? "GPU layers allowed by explicit --allow-gpu" : "CPU-only; nonzero GPU layers are rejected",
                reviewPolicy = "One pass through production Core and native-template paths. Outputs are for human review; no keyword-based quality score is applied."
            });

            var props = await Exchange(client, HttpMethod.Get, new Uri(endpointRoot, "/props"), null);
            WriteNew(outputPath, "props.json", props);
            RequireSuccess(props, "props");
            var (modelPath, contextSize, deviceEvidence, observedGpuLayers) = CheckProps(props.ResponseBody, allowGpu);
            WriteNew(outputPath, "server.json", new
            {
                modelPath,
                contextSize,
                deviceEvidence,
                allowGpu,
                devicePermission = allowGpu ? "GPU layers allowed by explicit --allow-gpu" : "CPU-only; nonzero GPU layers are rejected",
                observedGpuLayers,
                propsResponseSha256 = props.ResponseSha256
            });

            var results = new List<CaseResult>();
            foreach (DialogueCase item in suite.Cases)
            {
                CaseResult result = await RunCase(client, connection, contextSize, item);
                results.Add(result);
                WriteNew(outputPath, $"{results.Count:00}-{item.Id}.json", result);

                Console.WriteLine($"--- {item.Id} ---");
                Console.WriteLine(result.RawReply.Length == 0 ? "[no reply extracted; inspect raw completion response]" : result.RawReply);
                Console.WriteLine(result.Error == null ? "recorded for human review" : $"stopped: {result.Error}");
                if (result.Error != null) break;
            }

            WriteNew(outputPath, "summary.json", new
            {
                suiteSha256 = suite.Hash,
                modelPath,
                deviceEvidence,
                allowGpu,
                devicePermission = allowGpu ? "GPU layers allowed by explicit --allow-gpu" : "CPU-only; nonzero GPU layers are rejected",
                observedGpuLayers,
                plannedCases = suite.Cases.Count,
                completedResponses = results.Count(x => x.Completion != null && x.Error == null),
                endedUtc = DateTimeOffset.UtcNow,
                cases = results.Select(x => new { x.Id, x.ReviewFocus, x.PromptTokenCount, x.RawReply, x.Error }).ToArray(),
                note = "No semantic pass/fail score. Review prompts and replies against each case's focus."
            });

            return results.Count == suite.Cases.Count && results.All(x => x.Error == null) ? 0 : 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.GetType().Name + ": " + e.Message);
            return 2;
        }
    }

    private static SuiteFixture LoadSuite(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string hash = HashBytes(bytes);
        string lockPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "suite-lock.sha256");
        string lockedHash = File.ReadAllText(lockPath).Trim();
        if (!string.Equals(hash, lockedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Suite hash differs from frozen suite-lock.sha256: {hash}");

        SuiteFile suite = JsonSerializer.Deserialize<SuiteFile>(bytes, Json) ?? throw new InvalidOperationException("Invalid suite JSON.");
        if (suite.Version != 1 || suite.Cases == null || suite.Cases.Count != RequiredCaseIds.Length)
            throw new InvalidOperationException("Weather relevance suite v1 requires exactly six cases.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < suite.Cases.Count; index++)
        {
            DialogueCase item = suite.Cases[index];
            if (item.Id != RequiredCaseIds[index] || !ids.Add(item.Id) ||
                string.IsNullOrWhiteSpace(item.PlayerText) || string.IsNullOrWhiteSpace(item.ConversationContext) ||
                string.IsNullOrWhiteSpace(item.ReviewFocus) || item.History == null ||
                item.Disposition is < -100 or > 100 ||
                !item.ConversationContext.Contains("Current weather outside: snow.", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid or out-of-order fixed case: " + item.Id);

            ValidateHistory(item);
        }

        DialogueCase apology = suite.Cases[4];
        if (apology.Disposition != -80 || apology.History.Count != 2 ||
            !apology.ConversationContext.Contains("Saved disposition toward the player remains -80", StringComparison.Ordinal))
            throw new InvalidOperationException("Apology case lost its saved negative disposition context.");

        DialogueCase sequential = suite.Cases[5];
        if (!sequential.History.Any(x => x.Speaker == "NPC" && x.Text.Contains("Snow", StringComparison.Ordinal)))
            throw new InvalidOperationException("Sequential case must include an earlier NPC weather mention.");

        return new SuiteFixture(suite.Cases, hash);
    }

    private static void ValidateHistory(DialogueCase item)
    {
        foreach (HistoryTurn turn in item.History)
        {
            if ((turn.Speaker != "Player" && turn.Speaker != "NPC") ||
                string.IsNullOrWhiteSpace(turn.Text) || turn.Text.Contains('\n') || turn.Text.Contains('\r'))
                throw new InvalidOperationException("Invalid history turn in " + item.Id);
        }
    }

    private static DialogueRequest CreateRequest(DialogueCase item)
    {
        string history = string.Join("\n", item.History.Select(x => x.Speaker + ": " + x.Text));
        if (history.Length > 0) history += "\n";
        return FreeTextDialogue.Create(item.PlayerText, "Eveline Talandro", "Breton", "female", "inn patron",
            item.Disposition, item.ConversationContext, history);
    }

    private static void ValidateProductionPathOffline(SuiteFixture fixture)
    {
        var connection = new LocalDialogueConnection("http://127.0.0.1:8088/completion", 120000);
        foreach (DialogueCase item in fixture.Cases)
        {
            DialogueRequest request = CreateRequest(item);
            DialogueCompletion completion = LocalDialogueCompletion.Build(connection, request);
            if (!completion.Prompt.Contains(item.PlayerText, StringComparison.Ordinal) ||
                !completion.Prompt.Contains(item.ConversationContext, StringComparison.Ordinal))
                throw new InvalidOperationException("Production Core prompt dropped current text or context: " + item.Id);
            if (item.History.Count > 0 &&
                !completion.Prompt.Contains(DialogueHistoryWindow.Messages(request.History), StringComparison.Ordinal))
                throw new InvalidOperationException("Production Core prompt dropped conversation history: " + item.Id);

            using JsonDocument corePayload = JsonDocument.Parse(completion.JsonBody);
            if (corePayload.RootElement.GetProperty("prompt").GetString() != completion.Prompt ||
                corePayload.RootElement.GetProperty("n_predict").GetInt32() != completion.ResponseTokens)
                throw new InvalidOperationException("Production Core completion payload is inconsistent: " + item.Id);

            string templateRequest = LocalDialogueNativeTemplate.Request(completion.Prompt);
            using JsonDocument template = JsonDocument.Parse(templateRequest);
            if (!template.RootElement.GetProperty("add_generation_prompt").GetBoolean() ||
                template.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean())
                throw new InvalidOperationException("Production native-template settings changed: " + item.Id);

            const string renderedProbe = "synthetic offline native prompt";
            string payloadText = LocalDialogueNativeTemplate.Payload(completion.JsonBody, renderedProbe);
            using JsonDocument payload = JsonDocument.Parse(payloadText);
            if (payload.RootElement.GetProperty("prompt").GetString() != renderedProbe ||
                payload.RootElement.GetProperty("n_predict").GetInt32() != completion.ResponseTokens)
                throw new InvalidOperationException("Production completion payload rejected the native prompt: " + item.Id);
        }
    }

    private static void ValidateDeviceGateOffline()
    {
        var zeroGpu = CheckProps("{\"model_path\":\"offline-fixture\",\"n_ctx\":4096,\"n_gpu_layers\":0}", allowGpu: false);
        if (zeroGpu.ObservedGpuLayers.Count != 1 || zeroGpu.ObservedGpuLayers[0] != "n_gpu_layers=0")
            throw new InvalidOperationException("CPU-only /props evidence was not recorded.");

        bool rejectedByDefault = false;
        try
        {
            CheckProps("{\"model_path\":\"offline-fixture\",\"n_ctx\":4096,\"n_gpu_layers\":12}", allowGpu: false);
        }
        catch (InvalidOperationException e) when (e.Message.Contains("GPU layers are not permitted", StringComparison.Ordinal))
        {
            rejectedByDefault = true;
        }
        if (!rejectedByDefault)
            throw new InvalidOperationException("Default CPU-only mode did not reject GPU-offloaded layers.");

        var permittedGpu = CheckProps("{\"model_path\":\"offline-fixture\",\"n_ctx\":4096,\"n_gpu_layers\":12}", allowGpu: true);
        if (permittedGpu.ObservedGpuLayers.Count != 1 || permittedGpu.ObservedGpuLayers[0] != "n_gpu_layers=12")
            throw new InvalidOperationException("Explicit GPU mode did not record observed GPU layers.");

        var unreportedDevice = CheckProps("{\"model_path\":\"offline-fixture\",\"n_ctx\":4096}", allowGpu: false);
        if (!unreportedDevice.DeviceEvidence.Contains("verify the server process independently", StringComparison.Ordinal))
            throw new InvalidOperationException("Missing GPU-layer fields were reported as CPU proof.");
    }

    private static async Task<CaseResult> RunCase(HttpClient client, LocalDialogueConnection connection,
        int contextSize, DialogueCase item)
    {
        var result = new CaseResult { Id = item.Id, ReviewFocus = item.ReviewFocus };
        string stage = "build";
        try
        {
            DialogueRequest request = CreateRequest(item);
            DialogueCompletion completion = LocalDialogueCompletion.Build(connection, request);
            result.CorePrompt = completion.Prompt;
            result.CorePayload = completion.JsonBody;
            result.ResponseTokenLimit = completion.ResponseTokens;
            int tokenBudget = contextSize - completion.ResponseTokens - 32;
            if (tokenBudget <= 0) throw new InvalidOperationException("Invalid model context budget.");

            stage = "apply-template";
            string templateRequest = LocalDialogueNativeTemplate.Request(completion.Prompt);
            result.ApplyTemplate = await Exchange(client, HttpMethod.Post,
                new Uri(connection.Endpoint, "/apply-template"), templateRequest);
            RequireSuccess(result.ApplyTemplate, stage);
            string renderedPrompt = LocalDialogueNativeTemplate.ReadPrompt(result.ApplyTemplate.ResponseBody);
            result.RenderedPrompt = renderedPrompt;

            stage = "tokenize";
            string tokenizeRequest = JsonSerializer.Serialize(new { content = renderedPrompt, add_special = true });
            result.Tokenize = await Exchange(client, HttpMethod.Post,
                new Uri(connection.Endpoint, "/tokenize"), tokenizeRequest);
            RequireSuccess(result.Tokenize, stage);
            using (JsonDocument tokenJson = JsonDocument.Parse(result.Tokenize.ResponseBody))
            {
                if (!tokenJson.RootElement.TryGetProperty("tokens", out JsonElement tokens) || tokens.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("Missing token list.");
                result.PromptTokenCount = tokens.GetArrayLength();
            }
            if (result.PromptTokenCount > tokenBudget)
                throw new InvalidOperationException($"Prompt token count {result.PromptTokenCount} exceeds budget {tokenBudget}.");

            stage = "completion";
            string completionPayload = LocalDialogueNativeTemplate.Payload(completion.JsonBody, renderedPrompt);
            result.Completion = await Exchange(client, HttpMethod.Post, completion.Endpoint, completionPayload);
            RequireSuccess(result.Completion, stage);
            using (JsonDocument completionJson = JsonDocument.Parse(result.Completion.ResponseBody))
                if (completionJson.RootElement.TryGetProperty("truncated", out JsonElement truncated) &&
                    truncated.ValueKind == JsonValueKind.True)
                    throw new InvalidOperationException("Completion was truncated.");

            result.RawReply = DialogueCompletionText.Extract(result.Completion.ResponseBody);
        }
        catch (Exception e)
        {
            result.Error = stage + ": " + e.GetType().Name + ": " + e.Message;
        }
        return result;
    }

    private static Uri ParseLocalRoot(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? root) || root.Scheme != Uri.UriSchemeHttp ||
            !root.IsLoopback || root.AbsolutePath != "/" || root.Query.Length != 0 || root.Fragment.Length != 0 ||
            !string.IsNullOrEmpty(root.UserInfo))
            throw new ArgumentException("Endpoint must be a local HTTP origin without credentials, path, query, or fragment.");
        return root;
    }

    private static (string ModelPath, int ContextSize, string DeviceEvidence, List<string> ObservedGpuLayers) CheckProps(string json, bool allowGpu)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement? model = FindProperty(document.RootElement, "model_path");
        string modelPath = model is { ValueKind: JsonValueKind.String } ? model.Value.GetString() ?? "unknown" : "not reported";
        JsonElement? context = FindProperty(document.RootElement, "n_ctx");
        if (context == null || !context.Value.TryGetInt32(out int contextSize) || contextSize <= 0)
            throw new InvalidOperationException("/props has no valid n_ctx; no dialogue case was sent.");

        string[] gpuKeys = { "n_gpu_layers", "n_gpu_layers_actual", "n_gpu_layers_requested", "gpu_layers", "offloaded_layers" };
        var observations = new List<string>();
        foreach (string key in gpuKeys)
        {
            JsonElement? value = FindProperty(document.RootElement, key);
            if (value == null) continue;
            if (value.Value.ValueKind != JsonValueKind.Number || !value.Value.TryGetInt32(out int layers))
                throw new InvalidOperationException("/props GPU layer field is not an integer: " + key);
            observations.Add(key + "=" + layers);
            if (layers != 0 && !allowGpu)
                throw new InvalidOperationException("/props reports GPU-offloaded layers, but GPU layers are not permitted without explicit --allow-gpu; no dialogue case was sent.");
        }

        string deviceEvidence = observations.Count == 0
            ? "No GPU-layer field exposed by /props; verify the server process independently."
            : string.Join(", ", observations);
        return (modelPath, contextSize, deviceEvidence, observations);
    }

    private static JsonElement? FindProperty(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name)) return property.Value;
                JsonElement? nested = FindProperty(property.Value, name);
                if (nested != null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                JsonElement? nested = FindProperty(child, name);
                if (nested != null) return nested;
            }
        }
        return null;
    }

    private static async Task<HttpExchange> Exchange(HttpClient client, HttpMethod method, Uri uri, string? body)
    {
        var exchange = new HttpExchange
        {
            Method = method.Method,
            Url = uri.ToString(),
            RequestBody = body,
            RequestSha256 = body == null ? null : HashText(body),
            StartedUtc = DateTimeOffset.UtcNow
        };
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.SendAsync(request);
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

    private static void RequireSuccess(HttpExchange exchange, string stage)
    {
        if (exchange.Status != 200 || exchange.Error != null)
            throw new InvalidOperationException(stage + " HTTP/transport failure; see the recorded exchange.");
    }

    private static void WriteNew(string directory, string file, object value)
    {
        using FileStream stream = new(Path.Combine(directory, file), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, Json);
    }

    private static string HashText(string text) => HashBytes(Encoding.UTF8.GetBytes(text));
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record SuiteFixture(List<DialogueCase> Cases, string Hash);
    private sealed class SuiteFile { public int Version { get; set; } public List<DialogueCase>? Cases { get; set; } }
    private sealed class DialogueCase
    {
        public string Id { get; set; } = string.Empty;
        public string PlayerText { get; set; } = string.Empty;
        public int Disposition { get; set; }
        public string ConversationContext { get; set; } = string.Empty;
        public List<HistoryTurn> History { get; set; } = new();
        public string ReviewFocus { get; set; } = string.Empty;
    }
    private sealed class HistoryTurn { public string Speaker { get; set; } = string.Empty; public string Text { get; set; } = string.Empty; }
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
        public string Id { get; set; } = string.Empty;
        public string ReviewFocus { get; set; } = string.Empty;
        public string? CorePrompt { get; set; }
        public string? CorePayload { get; set; }
        public int ResponseTokenLimit { get; set; }
        public int PromptTokenCount { get; set; }
        public string? RenderedPrompt { get; set; }
        public HttpExchange? ApplyTemplate { get; set; }
        public HttpExchange? Tokenize { get; set; }
        public HttpExchange? Completion { get; set; }
        public string RawReply { get; set; } = string.Empty;
        public string? Error { get; set; }
    }
}
