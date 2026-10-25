using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

// Exact production Core request/grammar/wire, synthetic evidence only. No game writes.
if (args.Length != 2) throw new ArgumentException("Usage: SpeechActEvaluation suite.json NEW-output-directory");
if (Process.GetProcessesByName("DaggerfallUnity").Length != 0) throw new InvalidOperationException("Close DFU before offline evaluation.");
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new InvalidOperationException("Output already exists; no retries or overwrite.");
Directory.CreateDirectory(output);
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
using var suite = JsonDocument.Parse(File.ReadAllText(args[0]));
var models = new Dictionary<string, string>();
foreach (var endpoint in new[] { ("d3", "http://127.0.0.1:8093"), ("qwen", "http://127.0.0.1:8080") })
{
    string props = await http.GetStringAsync(endpoint.Item2 + "/props");
    File.WriteAllText(Path.Combine(output, endpoint.Item1 + "-props.json"), props);
    models[endpoint.Item1] = JsonDocument.Parse(props).RootElement.GetProperty("model_path").GetString();
}
string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new { suiteSha256 = Sha(File.ReadAllText(args[0])), models,
    assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(NpcSpeechActRequest).Assembly.Location))),
    limitations = "Synthetic Core evidence. Exact compiled production wire, grammar and parser. No native game effects. Single canonical option order. No retries." }));
async Task<JsonDocument> Post(string id, string lane, string url, string body)
{
    File.WriteAllText(Path.Combine(output, id + "-" + lane + "-request.json"), body);
    File.AppendAllText(Path.Combine(output, "journal.jsonl"), JsonSerializer.Serialize(new { id, lane, phase = "started", hash = Sha(body), utc = DateTime.UtcNow }) + "\n");
    var clock = Stopwatch.StartNew();
    using var response = await http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
    string raw = await response.Content.ReadAsStringAsync();
    File.WriteAllText(Path.Combine(output, id + "-" + lane + "-response.json"), raw);
    File.AppendAllText(Path.Combine(output, "journal.jsonl"), JsonSerializer.Serialize(new { id, lane, phase = "completed", hash = Sha(raw), status = (int)response.StatusCode, elapsedMs = clock.Elapsed.TotalMilliseconds }) + "\n");
    response.EnsureSuccessStatusCode();
    return JsonDocument.Parse(raw);
}
int count = 0, accepted = 0, correct = 0, falseThreat = 0;
foreach (var item in suite.RootElement.GetProperty("cases").EnumerateArray())
{
    string id = item.GetProperty("id").GetString();
    if (id.StartsWith("de-", StringComparison.Ordinal)) continue;
    if (id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'))) throw new ArgumentException("Unsafe fixture ID");
    var input = item.GetProperty("input");
    string text = input.GetProperty("playerText").GetString();
    var evidence = NpcExperienceEvent.FromObservedSpeech("npc:observer", "npc:observer", "receipt:" + id, "episode:evaluation", "utterance:" + id, 110, "player");
    var context = new List<NpcObservedConversationTurn>();
    foreach (var turn in input.GetProperty("history").EnumerateArray())
        context.Add(NpcObservedConversationTurn.Create("npc:observer", evidence.EpisodeId, turn.GetProperty("speaker").GetString(), "prior:" + context.Count, turn.GetProperty("text").GetString(), 100 + context.Count));
    var relation = NpcDirectedRelationship.Create("npc:observer", "player");
    var request = NpcExperienceAppraisalContract.CreateRequest("speech-stage-evaluation", evidence,
        NpcPersonalityGenerator.Generate("npc:observer", 42, NpcSocialBackground.Commoner), 0, 0, 100, 0, 0, 2, 120,
        relation.ToView(), null, observedUtterance: NpcObservedConversationTurn.Create("npc:observer", evidence.EpisodeId,
            "player", evidence.ReceiptId, text, 110), conversationContext: context, knownFamilyIds: new[] { "npc:daughter" });
    var speech = NpcSpeechActRequest.ForAppraisal(request);
    using var classified = await Post(id, "d3", "http://127.0.0.1:8093/v1/systemone", LocalSpeechActWire.Build(speech));
    if (!LocalSpeechActWire.TryParse(speech, models["d3"], classified.RootElement.GetRawText(), out var classification))
        throw new InvalidOperationException("Exact production classifier rejected " + id);
    var bound = NpcExperienceAppraisalContract.WithSpeechClassification(request, classification);
    var completion = LocalDialogueCompletion.BuildAppraisal(new LocalDialogueConnection("http://127.0.0.1:8080/completion", 30000), bound);
    using var tokens = await Post(id, "tokenize", "http://127.0.0.1:8080/tokenize", JsonSerializer.Serialize(new { content = completion.Prompt, add_special = true }));
    int promptTokens = tokens.RootElement.GetProperty("tokens").GetArrayLength();
    if (promptTokens + completion.ResponseTokens + 32 > 4096) throw new InvalidOperationException("Context overflow " + id);
    using var appraisal = await Post(id, "qwen", completion.Endpoint.ToString(), completion.JsonBody);
    if (appraisal.RootElement.GetProperty("model").GetString() != models["qwen"]) throw new InvalidOperationException("Qwen model changed");
    string content = appraisal.RootElement.GetProperty("content").GetString();
    bool parsed = NpcExperienceAppraisalContract.TryParseResponse(bound, content, out var result);
    var allowed = item.GetProperty("expected").GetProperty("allowedChoices").EnumerateArray().Select(x => x.GetString())
        .Select(x => x == "QuotedOrReportedThreat" ? "ReportedThreat" : x == "JokingOrAmbiguousThreat" ? "AmbiguousThreat" : x).ToArray();
    bool semantic = allowed.Contains(classification.Kind.ToString());
    bool falseDirect = classification.Kind == NpcExperienceSpeechActKind.DirectThreat && !allowed.Contains("DirectThreat");
    count++; if (parsed) accepted++; if (semantic) correct++; if (falseDirect) falseThreat++;
    File.AppendAllText(Path.Combine(output, "results.jsonl"), JsonSerializer.Serialize(new { id, classification = classification.Kind.ToString(), parsed,
        semantic, falseDirect, promptTokens, affinity = result?.AffinityDelta, trust = result?.TrustDelta,
        resentment = result?.ResentmentDelta, fear = result?.FearDelta, applied = false }) + "\n");
    Console.WriteLine(id + " " + classification.Kind + " parsed=" + parsed);
}
File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { count, accepted, correct, falseThreat }));
Console.WriteLine(JsonSerializer.Serialize(new { count, accepted, correct, falseThreat }));
