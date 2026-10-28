using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.NpcFactorEvaluation
{
    /// <summary>Process-local capture tickets; HTTP inference remains owned by the external serial runner.</summary>
    internal sealed class SelectedFrameworkSession
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };
        private readonly string suite, root, repo, expectedQwen, expectedD3;
        private readonly Dictionary<string, string> sourceHashes;
        private SelectedAppraisalCaptureReport pairs;
        private IReadOnlyList<SelectedDecisionCaptureCase> decisions;
        private readonly Dictionary<string, SelectedContinuityCapture> episodes = new Dictionary<string, SelectedContinuityCapture>();

        private SelectedFrameworkSession(string suitePath, string output)
        {
            suite = Path.GetFullPath(suitePath);
            root = Path.GetFullPath(output);
            repo = FindRepository();
            if (!File.Exists(suite) || Directory.Exists(root))
                throw new InvalidOperationException("Use a real frozen suite and a new session output directory.");
            Directory.CreateDirectory(root);
            string runtime = Path.Combine(Directory.GetParent(repo).FullName, "local-llm");
            using (JsonDocument selected = JsonDocument.Parse(File.ReadAllText(Path.Combine(runtime, "active-model.json"))))
                expectedQwen = Path.GetFullPath(Path.Combine(runtime, selected.RootElement.GetProperty("modelPath").GetString()));
            using (JsonDocument selected = JsonDocument.Parse(File.ReadAllText(Path.Combine(runtime, "active-decision-model.json"))))
            {
                if (selected.RootElement.GetProperty("profile").GetString() != "nano")
                    throw new InvalidOperationException("This frozen suite requires selected D3 Nano.");
            }
            expectedD3 = Path.GetFullPath(Path.Combine(runtime, "models/d3-nano/vllm-d3-nano-Q8_0.gguf"));
            sourceHashes = new Dictionary<string, string>(StringComparer.Ordinal);
            var sources = Directory.GetFiles(Path.Combine(repo, "src/PeopleOfTheIliacBay.Core"), "*.cs")
                .Concat(Directory.GetFiles(Path.Combine(repo, "tools/NpcFactorEvaluation"), "*.cs"))
                .Concat(new[] { suite, Path.Combine(repo, "tools/archived/DecisionDiagnostics/LocalDecisionWire.cs"),
                    Path.Combine(repo, "tools/archived/DecisionDiagnostics/LocalSpeechActWire.cs"),
                    Path.Combine(repo, "mod/Scripts/LocalActionWillingness.cs"),
                    Path.Combine(repo, "tools/NpcFactorEvaluation/NpcFactorEvaluation.csproj"),
                    Path.Combine(repo, "tools/NpcFactorEvaluation/run_selected_capture_evaluation.py") });
            foreach (string path in sources.Distinct(StringComparer.OrdinalIgnoreCase))
                sourceHashes.Add(Path.GetRelativePath(repo, path).Replace('\\', '/'), Hash(File.ReadAllBytes(path)));
            SaveNew(Path.Combine(root, "session-owner.json"), new {
                protocol = "production-capture-session-v1", processId = Environment.ProcessId,
                suite, suiteSha256 = Hash(File.ReadAllBytes(suite)), expectedQwen, expectedD3,
                sourceHashes, processLossResume = "unsupported; preserve first outputs without rerolling",
                nativeGameWrites = false });
        }

        public static int Run(string[] args)
        {
            if (args.Length != 3) throw new ArgumentException("selected-session <suite.json> <new-output-directory>");
            var session = new SelectedFrameworkSession(args[1], args[2]);
            session.Emit(new { status = "ready", outputDirectory = session.root, processId = Environment.ProcessId });
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                try
                {
                    using (JsonDocument command = JsonDocument.Parse(line))
                    {
                        string op = command.RootElement.GetProperty("op").GetString();
                        if (op == "quit") { session.Emit(new { status = "closed" }); return 0; }
                        session.Execute(op, command.RootElement);
                    }
                }
                catch (Exception error) { session.Emit(new { status = "error", error = error.Message }); }
            }
            return 0;
        }

        private void Execute(string op, JsonElement command)
        {
            if (op == "selftest")
            {
                string diagnostic;
                if (!SelectedContinuityCapture.RunSelfTests(out diagnostic)) throw new InvalidOperationException(diagnostic);
                SelectedAppraisalCapture.SelfTest(suite, Path.Combine(root, "offline-pair-check"));
                Emit(new { status = "selftest-pass", continuity = diagnostic });
                return;
            }
            if (op == "pairs")
            {
                if (pairs != null) throw new InvalidOperationException("Pairs are already captured in this session.");
                pairs = SelectedAppraisalCapture.PreparePairs(suite, Path.Combine(root, "pairs"));
                string rows = Path.Combine(root, "pair-requests.jsonl");
                WriteRows(rows, pairs.Cases.Where(x => x.Status == "exported").Select(x =>
                    Row(x.CaseId, "qwen", LocalDialogueCompletion.BuildAppraisal(Connection(), x.Request).JsonBody)));
                Emit(new { status = "pairs-ready", pairs.Exported, pairs.Blocked, requestPath = rows,
                    reportPath = pairs.ReportPath });
                return;
            }
            if (op == "decisions")
            {
                if (decisions != null) throw new InvalidOperationException("Decision offers are already captured.");
                decisions = SelectedDecisionCapture.PrepareDecisions(suite, Path.Combine(root, "decisions"));
                string rows = Path.Combine(root, "decision-requests.jsonl");
                var ready = decisions.Where(x => x.Request != null).ToArray();
                WriteRows(rows, ready.Select(x => Row(x.CaseId, "d3nano", x.RequestJson)));
                Emit(new { status = "decisions-ready", exported = ready.Length,
                    blocked = decisions.Count - ready.Length, requestPath = rows });
                return;
            }
            if (op == "validatePair")
            {
                string id = command.GetProperty("caseId").GetString();
                var value = pairs == null ? null : pairs.Cases.FirstOrDefault(x => x.CaseId == id && x.Request != null);
                if (value == null) throw new InvalidOperationException("No captured pair case.");
                string path = ResultPath(id);
                RefuseExisting(path);
                string raw = ReadResponse(command, id, "qwen", LocalDialogueCompletion.BuildAppraisal(Connection(), value.Request).JsonBody);
                string content = CompletionContent(raw);
                NpcExperienceAppraisal appraisal;
                bool accepted = NpcExperienceAppraisalContract.TryParseResponse(value.Request, content, out appraisal);
                SaveNew(path, new { caseId = id, parserAccepted = accepted, committed = false,
                    semanticReview = "pending", rawSha256 = Hash(Encoding.UTF8.GetBytes(raw)),
                    contentSha256 = Hash(Encoding.UTF8.GetBytes(content)), appraisal });
                Emit(new { status = "pair-validated", caseId = id, parserAccepted = accepted, resultPath = path });
                return;
            }
            if (op == "validateDecision")
            {
                string id = command.GetProperty("caseId").GetString();
                var value = decisions == null ? null : decisions.FirstOrDefault(x => x.CaseId == id && x.Request != null);
                if (value == null) throw new InvalidOperationException("No captured decision case.");
                string path = ResultPath(id);
                RefuseExisting(path);
                string raw = ReadResponse(command, id, "d3nano", value.RequestJson);
                var parsed = SelectedDecisionCapture.ParseRecorded(value, raw);
                SaveNew(path, new { caseId = id, parsed, semanticReview = "pending",
                    nativeGameEvidence = false, validationScope = "frozen-request-only; not live freshness",
                    rawSha256 = Hash(Encoding.UTF8.GetBytes(raw)) });
                Emit(new { status = "decision-validated", caseId = id, accepted = parsed.Accepted,
                    parsed.SelectedOptionId, resultPath = path });
                return;
            }
            if (op == "startEpisode")
            {
                string id = command.GetProperty("episodeId").GetString();
                if (episodes.ContainsKey(id)) throw new InvalidOperationException("Episode already started; do not reroll it.");
                var episode = SelectedContinuityCapture.Start(id);
                episodes.Add(id, episode);
                Emit(PrepareNext(episode));
                return;
            }
            if (op == "classifyEpisode")
            {
                var episode = episodes[command.GetProperty("episodeId").GetString()];
                if (!episode.TryPrepareNext(out var current) || !episode.CurrentRequest.RequiresSpeechAppraisal ||
                    episode.CurrentRequest.SpeechClassification != null) throw new InvalidOperationException("No unclassified speech ticket.");
                var speech = NpcSpeechActRequest.ForAppraisal(episode.CurrentRequest);
                string raw = ReadResponse(command, current.CaseId + "-speech", "d3", LocalSpeechActWire.Build(speech));
                if (!LocalSpeechActWire.TryParse(speech, expectedD3, raw, out var classification) ||
                    !episode.BindCurrentSpeechClassification(classification)) throw new InvalidOperationException("Speech classification rejected.");
                Emit(new { status = "speech-classified", next = PrepareNext(episode) });
                return;
            }
            if (op == "commitEpisode")
            {
                var episode = episodes[command.GetProperty("episodeId").GetString()];
                SelectedContinuityPrompt current;
                if (!episode.TryPrepareNext(out current)) throw new InvalidOperationException("No pending episode ticket.");
                string path = ResultPath(current.CaseId);
                RefuseExisting(path);
                string raw = ReadResponse(command, current.CaseId, "qwen",
                    LocalDialogueCompletion.BuildAppraisal(Connection(), episode.CurrentRequest).JsonBody);
                var committed = episode.CommitCurrent(CompletionContent(raw));
                SaveNew(path, new { caseId = current.CaseId, committed,
                    rawSha256 = Hash(Encoding.UTF8.GetBytes(raw)), nativeGameWrites = false });
                Emit(new { status = "episode-commit", resultPath = path, committed, next = PrepareNext(episode) });
                return;
            }
            throw new ArgumentException("Unknown selected session command: " + op);
        }

        private object PrepareNext(SelectedContinuityCapture episode)
        {
            SelectedContinuityPrompt prompt;
            if (!episode.TryPrepareNext(out prompt))
                return new { status = episode.IsComplete ? "complete" : "stopped", episode.EpisodeId, episode.StopReason };
            string directory = Path.Combine(root, "continuity", episode.EpisodeId);
            Directory.CreateDirectory(directory);
            if (episode.CurrentRequest.RequiresSpeechAppraisal && episode.CurrentRequest.SpeechClassification == null)
            {
                string speechPath = Path.Combine(directory, prompt.CaseId + "-speech.requests.jsonl");
                WriteRows(speechPath, new[] { Row(prompt.CaseId + "-speech", "d3",
                    LocalSpeechActWire.Build(NpcSpeechActRequest.ForAppraisal(episode.CurrentRequest))) });
                return new { status = "episode-speech-ready", prompt.EpisodeId, prompt.CaseId, requestPath = speechPath };
            }
            string metadata = Path.Combine(directory, prompt.CaseId + ".capture.json");
            string rows = Path.Combine(directory, prompt.CaseId + ".requests.jsonl");
            SaveNew(metadata, prompt);
            WriteRows(rows, new[] { Row(prompt.CaseId, "qwen",
                LocalDialogueCompletion.BuildAppraisal(Connection(), episode.CurrentRequest).JsonBody) });
            return new { status = "episode-ready", prompt.EpisodeId, prompt.CaseId,
                requestPath = rows, capturePath = metadata };
        }

        private object Row(string id, string lane, string body)
        { return new { caseId = id, lane, requestBody = body,
            expectedModel = lane == "qwen" ? expectedQwen : expectedD3, sourceHashes }; }

        private string ReadResponse(JsonElement command, string id, string lane, string body)
            => SelectedCaptureReceipt.Read(root, command.GetProperty("responsePath").GetString(), id, lane,
                body, lane == "qwen" ? expectedQwen : expectedD3, sourceHashes);
        private string ResultPath(string id)
        {
            if (string.IsNullOrEmpty(id) || Path.GetFileName(id) != id || id.Contains(".."))
                throw new ArgumentException("Unsafe case identity.");
            string directory = Path.Combine(root, "core-validation");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, id + ".json");
        }
        private void Emit(object value)
        {
            string line = JsonSerializer.Serialize(value, JsonOptions);
            File.AppendAllText(Path.Combine(root, "session-events.jsonl"), line + "\n", new UTF8Encoding(false));
            Console.WriteLine(line);
            Console.Out.Flush();
        }
        private static string CompletionContent(string raw)
        {
            using (JsonDocument envelope = JsonDocument.Parse(raw))
                return envelope.RootElement.GetProperty("content").GetString() ?? string.Empty;
        }
        private static LocalDialogueConnection Connection()
        { return new LocalDialogueConnection("http://127.0.0.1:8080/completion", 120000); }
        private static void SaveNew(string path, object value)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                JsonSerializer.Serialize(stream, value, JsonOptions);
                stream.Flush(true);
            }
        }
        private static void WriteRows(string path, IEnumerable<object> rows)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                foreach (object row in rows) writer.WriteLine(JsonSerializer.Serialize(row, JsonOptions));
        }
        private static void RefuseExisting(string path)
        { if (File.Exists(path)) throw new InvalidOperationException("First Core outcome already exists; refusing overwrite."); }
        private static string Hash(byte[] bytes)
        { using (SHA256 hash = SHA256.Create()) return Convert.ToHexString(hash.ComputeHash(bytes)).ToLowerInvariant(); }
        private static string FindRepository()
        {
            for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "mod/PeopleOfTheIliacBay.dfmod.json"))) return directory.FullName;
            throw new InvalidOperationException("Run the selected session from the People of the Iliac Bay checkout.");
        }
    }
}
