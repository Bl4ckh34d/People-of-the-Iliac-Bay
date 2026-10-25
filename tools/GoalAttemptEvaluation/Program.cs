using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

// Interpretation only. Expected answers are scored after calls, never sent to the model.
if (args.Length != 3 && args.Length != 5) throw new ArgumentException("Usage: GoalAttemptEvaluation fixture.json NEW-run-directory exact-model-id [legacy|native port]");
string templateMode = args.Length == 5 ? args[3] : "legacy";
if (templateMode != "legacy" && templateMode != "native" && templateMode != "thinking") throw new ArgumentException("Unknown template mode.");
int port = args.Length == 5 ? int.Parse(args[4]) : 8080;
if (port < 1024 || port > 65535) throw new ArgumentException("Invalid loopback port.");
if (Process.GetProcessesByName("DaggerfallUnity").Length != 0) throw new InvalidOperationException("Close DFU for offline evaluation.");
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new InvalidOperationException("Run already exists.");
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(40) };
string endpoint = "http://127.0.0.1:" + port;
using var fixture = JsonDocument.Parse(File.ReadAllText(args[0]));
var cases = fixture.RootElement.GetProperty("cases").EnumerateArray().ToArray();
if (cases.Length < 1 || cases.Length > 32 || cases.Select(x => x.GetProperty("id").GetString()).Distinct().Count() != cases.Length)
    throw new ArgumentException("Invalid case count/IDs.");
await CheckModel();
using var props = JsonDocument.Parse(await http.GetStringAsync(endpoint + "/props"));
int context = props.RootElement.GetProperty("default_generation_settings").GetProperty("n_ctx").GetInt32();
Directory.CreateDirectory(output);
File.Copy(args[0], Path.Combine(output, "fixture.json"));
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new {
    model = args[2], context, templateMode, endpoint, temperature = 0, thinking = templateMode == "thinking",
    coreSha256 = Sha(File.ReadAllBytes(typeof(NpcGoalParameterAttempts).Assembly.Location)),
    fixtureSha256 = Sha(File.ReadAllBytes(args[0])), evaluatorSha256 = Sha(File.ReadAllBytes(typeof(Program).Assembly.Location)),
    attempts = NpcGoalParameterAttempts.MaximumAttempts, nativeExecution = false, started = DateTimeOffset.UtcNow }));
var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, "npc:fixture", null,
    "goal-attempt-evaluation", 4, 2, new string('a', 32), "", "scene", 3);
var connection = new LocalDialogueConnection(endpoint + "/completion", 30000);
var scores = new List<object>();
foreach (var item in cases)
{
    string id = item.GetProperty("id").GetString();
    if (id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'))) throw new ArgumentException("Unsafe case ID");
    string text = item.GetProperty("text").GetString();
    string history = item.TryGetProperty("history", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : "";
    var expected = item.GetProperty("expected");
    var attempts = new List<object>();
    string failure = null;
    string[] kinds = Array.Empty<string>();
    string intent = null;
    bool structureCorrect = false, finalPass = false, ready = false;
    try
    {
        string first = await Extract(id, "decomposition", NpcGoalTwoPassChat.DecompositionInstructions(text,
            fixture.RootElement.GetProperty("factualContext").GetString()), history, NpcGoalTwoPassChat.DecompositionGrammar(), 384);
        var parsed = NpcGoalTwoPassChat.ParseDecomposition(first, binding, text);
        if (parsed.Status != NpcGoalExtractionStatus.Valid) throw new InvalidOperationException(parsed.Reason);
        var decomposition = parsed.Decomposition;
        intent = decomposition.Intent.ToString();
        kinds = decomposition.Kinds.ToArray();
        structureCorrect = intent == expected.GetProperty("intent").GetString() &&
            kinds.SequenceEqual(expected.GetProperty("kinds").EnumerateArray().Select(x => x.GetString()));
        if (decomposition.Intent == NpcGoalIntent.Request)
        {
            var state = new NpcGoalParameterAttempts(decomposition);
            do
            {
                string prompt = state.Count == 0 ? NpcGoalTwoPassChat.ParameterInstructions(decomposition) :
                    NpcGoalTwoPassChat.RepairInstructions(decomposition, state.RepairAnchor,
                        state.Reason.Length > 160 ? state.Reason.Substring(0, 160) : state.Reason);
                string raw = await Extract(id, "parameters-" + (state.Count + 1), prompt, history,
                    NpcGoalTwoPassChat.ParameterGrammar(decomposition), 1536);
                state.Accept(raw);
                var errors = Score(state.Result.Proposal, expected);
                finalPass = structureCorrect && state.Ready && errors.Count == 0;
                attempts.Add(new { attempt = state.Count, status = state.Result.Status.ToString(), state.Reason,
                    state.Ready, errors, fieldPass = finalPass, proposal = raw });
            } while (state.CanRetry);
            ready = state.Ready;
        }
        else finalPass = structureCorrect;
    }
    catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException || ex is JsonException)
    { failure = ex.GetType().Name + ": " + ex.Message; }
    var row = new { id, stepCount = expected.GetProperty("kinds").GetArrayLength(), intent, kinds, structureCorrect,
        attempts, finalPass, ready, falseExecutable = ready && expected.GetProperty("intent").GetString() != "Request", failure };
    scores.Add(row);
    File.AppendAllText(Path.Combine(output, "results.jsonl"), JsonSerializer.Serialize(row) + "\n");
    Console.WriteLine($"{id}: structure={structureCorrect}; fields={finalPass}; attempts={attempts.Count}; failure={failure}");
}
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(scores, new JsonSerializerOptions { WriteIndented = true }));

async Task CheckModel()
{
    using var model = JsonDocument.Parse(await http.GetStringAsync(endpoint + "/v1/models"));
    var ids = model.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToArray();
    if (ids.Length != 1 || ids[0] != args[2]) throw new InvalidOperationException("Unexpected model identity.");
}
async Task<string> Extract(string id, string stage, string instructions, string history, string grammar, int tokens)
{
    await CheckModel();
    // Fixtures have no sliding history. Refuse overflow instead of truncating fixed facts or constraints.
    if (!string.IsNullOrEmpty(history)) throw new InvalidOperationException("Nonempty history requires production history fitting.");
    string prompt, body;
    var messages = new[] {
        new { role = "system", content = "You classify game dialogue. Return only the requested classification, never an answer or instructions." },
        new { role = "user", content = instructions }
    };
    bool thinking = templateMode == "thinking";
    if (templateMode != "legacy")
    {
        // Render the loaded GGUF template, not another model family's special tokens.
        // Keep the logical legacy system message so this arm isolates template effects.
        using var template = await Post(id, stage + "-template", "/apply-template", JsonSerializer.Serialize(new {
            messages, chat_template_kwargs = new { enable_thinking = thinking }, add_generation_prompt = true }));
        prompt = template.RootElement.GetProperty("prompt").GetString();
        body = JsonSerializer.Serialize(new { prompt, grammar, n_predict = tokens, temperature = 0, seed = 42,
            cache_prompt = false, stream = false });
    }
    else
    {
        var completion = LocalDialogueCompletion.Extract(connection, instructions, history, grammar, tokens);
        prompt = completion.Prompt;
        body = completion.JsonBody;
    }
    if (thinking)
    {
        tokens += 1536; // Account for reasoning separately from the original answer budget.
        // Raw grammar constrains the reasoning channel too. Chat JSON schema is applied to the final answer.
        body = JsonSerializer.Serialize(new { model = args[2], messages,
            response_format = new { type = "json_schema", json_schema = new { name = "goal", strict = true, schema = ThinkingSchema(stage == "decomposition") } }, max_tokens = tokens,
            temperature = 0, seed = 42, stream = false, chat_template_kwargs = new { enable_thinking = true } });
    }
    using var tokenResponse = await Post(id, stage + "-tokens", "/tokenize", JsonSerializer.Serialize(new { content = prompt, add_special = true }));
    if (tokenResponse.RootElement.GetProperty("tokens").GetArrayLength() + tokens + 32 > context)
        throw new InvalidOperationException("Fixed request exceeds context budget.");
    using var response = await Post(id, stage, thinking ? "/v1/chat/completions" : "/completion", body);
    if (response.RootElement.GetProperty("model").GetString() != args[2]) throw new InvalidOperationException("Response model changed.");
    if (thinking)
    {
        var choice = response.RootElement.GetProperty("choices")[0];
        if (choice.GetProperty("finish_reason").GetString() != "stop") throw new InvalidOperationException("Thinking response incomplete.");
        return choice.GetProperty("message").GetProperty("content").GetString();
    }
    if (response.RootElement.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean())
        throw new InvalidOperationException("Response context was truncated.");
    if (response.RootElement.TryGetProperty("stop_type", out var stopType) && stopType.GetString() == "limit")
        throw new InvalidOperationException("Response reached token limit.");
    return response.RootElement.GetProperty("content").GetString();
}
async Task<JsonDocument> Post(string id, string stage, string path, string body)
{
    string name = Path.Combine(output, id + "-" + stage);
    File.WriteAllText(name + "-request.json", body);
    File.AppendAllText(Path.Combine(output, "calls.jsonl"), JsonSerializer.Serialize(new { id, stage, phase = "started", sha256 = Sha(Encoding.UTF8.GetBytes(body)) }) + "\n");
    var watch = Stopwatch.StartNew();
    using var response = await http.PostAsync(endpoint + path, new StringContent(body, Encoding.UTF8, "application/json"));
    string raw = await response.Content.ReadAsStringAsync();
    File.WriteAllText(name + "-response.json", raw);
    File.AppendAllText(Path.Combine(output, "calls.jsonl"), JsonSerializer.Serialize(new { id, stage, phase = "completed", status = (int)response.StatusCode,
        elapsedMs = watch.Elapsed.TotalMilliseconds, sha256 = Sha(Encoding.UTF8.GetBytes(raw)) }) + "\n");
    response.EnsureSuccessStatusCode();
    return JsonDocument.Parse(raw);
}
static List<string> Score(NpcGoalProposal proposal, JsonElement expected)
{
    var errors = new List<string>();
    if (proposal == null) { errors.Add("No valid proposal."); return errors; }
    if (proposal.Intent.ToString() != expected.GetProperty("intent").GetString()) errors.Add("intent");
    var steps = expected.GetProperty("steps").EnumerateArray().ToArray();
    if (proposal.Steps.Count != steps.Length) { errors.Add("step count"); return errors; }
    for (int i = 0; i < steps.Length; i++)
    {
        var gold = steps[i]; var actual = proposal.Steps[i];
        if (actual.Kind != expected.GetProperty("kinds")[i].GetString()) errors.Add($"kind[{i}]");
        if (!gold.GetProperty("targets").EnumerateArray().Select(x => x.GetString()).Contains(actual.TargetText)) errors.Add($"target[{i}]");
        var duration = gold.GetProperty("duration");
        if (duration.ValueKind == JsonValueKind.Null ? actual.Duration != null : actual.Duration == null ||
            duration.GetProperty("amount").GetDecimal() != actual.Duration.Amount || duration.GetProperty("unit").GetString() != actual.Duration.Unit.ToString()) errors.Add($"duration[{i}]");
        var quantity = gold.GetProperty("quantity");
        if (quantity.ValueKind == JsonValueKind.Null ? actual.Quantity != null : actual.Quantity == null || quantity.GetDecimal() != actual.Quantity.Amount) errors.Add($"quantity[{i}]");
        if (actual.Missing.Count > 0) errors.Add($"unresolved[{i}]");
    }
    if (!proposal.Conditions.SequenceEqual(expected.GetProperty("conditions").EnumerateArray().Select(x => x.GetString()))) errors.Add("conditions");
    if (!proposal.Exclusions.SequenceEqual(expected.GetProperty("exclusions").EnumerateArray().Select(x => x.GetString()))) errors.Add("exclusions");
    if (proposal.Missing.Count > 0) errors.Add("unresolved root");
    return errors;
}
static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

static object ThinkingSchema(bool decomposition)
{
    object Text() => new { type = "string" };
    object List(object items) => new { type = "array", items };
    object Obj(Dictionary<string, object> properties) => new { type = "object", properties, required = properties.Keys.ToArray(), additionalProperties = false };
    var step = new Dictionary<string, object> { ["kind"] = new { type = "string", @enum = new[] {
        "Wait", "Follow", "MoveTo", "Purchase", "Transfer", "Interact", "TakeSeat", "ChangeSchedule", "Attack", "CastSpell", "Lockpick", "Climb", "Other" } } };
    if (!decomposition)
    {
        step["source"] = Text(); step["target"] = Text();
        step["duration"] = new { anyOf = new object[] { new { type = "null" }, Obj(new() {
            ["amount"] = new { type = "number", exclusiveMinimum = 0 },
            ["unit"] = new { type = "string", @enum = new[] { "game_seconds", "game_minutes", "game_hours" } }, ["source"] = Text() }) } };
        step["quantity"] = new { anyOf = new object[] { new { type = "null" }, Obj(new() {
            ["amount"] = new { type = "integer", minimum = 1 }, ["source"] = Text() }) } };
        step["missing"] = List(Text());
    }
    var root = new Dictionary<string, object> { ["intent"] = new { type = "string", @enum = new[] { "request", "none", "clarify" } }, ["steps"] = List(Obj(step)) };
    if (!decomposition) { root["conditions"] = List(Text()); root["exclusions"] = List(Text()); root["missing"] = List(Text()); }
    return Obj(root); // Actual Core still validates frozen kinds/count, sources, bounds and safe-fill repairs.
}
