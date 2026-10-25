using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PeopleOfTheIliacBay.NpcFactorEvaluation
{
    /// <summary>Checks transport evidence before any frozen-request parsing or synthetic state commit.</summary>
    internal static class SelectedCaptureReceipt
    {
        internal static string Read(string sessionRoot, string responsePath, string caseId,
            string lane, string requestBody, string expectedModel, IReadOnlyDictionary<string, string> sourceHashes)
        {
            string full = Path.GetFullPath(responsePath);
            string root = Path.GetFullPath(sessionRoot) + Path.DirectorySeparatorChar;
            Require(full.StartsWith(root, StringComparison.OrdinalIgnoreCase), "Response is outside this session.");
            var response = new FileInfo(full);
            DirectoryInfo run = response.Directory?.Parent?.Parent;
            Require(run != null && run.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase), "Missing owned run directory.");
            string caseDirectory = Path.Combine(run.FullName, "cases", Hash(Encoding.UTF8.GetBytes(caseId)));
            Require(string.Equals(full, Path.Combine(caseDirectory, "response.bin"), StringComparison.OrdinalIgnoreCase),
                "Response path does not belong to the captured case.");
            // Reject redirected artifacts too: lexical containment alone does not bind a local file.
            for (var directory = response.Directory; directory != null && directory.FullName.Length >= root.Length; directory = directory.Parent)
                Require((directory.Attributes & FileAttributes.ReparsePoint) == 0, "Run directory is redirected.");
            byte[] raw = ReadBounded(full);
            byte[] request = ReadBounded(Path.Combine(caseDirectory, "request.json"));
            Require(request.SequenceEqual(Encoding.UTF8.GetBytes(requestBody)), "Request bytes differ from the captured request.");
            using (JsonDocument owner = JsonDocument.Parse(ReadBounded(Path.Combine(run.FullName, "owner.json"))))
            {
                Require(owner.RootElement.GetProperty("schema").GetString() == "selected-capture-run-v1", "Unknown run owner schema.");
                JsonElement sources = owner.RootElement.GetProperty("sourceHashes").GetProperty(caseId);
                Require(sources.EnumerateObject().Count() == sourceHashes.Count && sources.EnumerateObject().All(
                    p => sourceHashes.TryGetValue(p.Name, out string hash) && p.Value.GetString() == hash), "Run source ownership differs from capture.");
            }
            JsonElement start = default, terminal = default;
            int starts = 0, terminals = 0;
            foreach (string line in File.ReadLines(Path.Combine(run.FullName, "journal.jsonl")))
            {
                using (JsonDocument entry = JsonDocument.Parse(line))
                {
                    JsonElement value = entry.RootElement;
                    if (value.GetProperty("caseId").GetString() != caseId) continue;
                    if (value.GetProperty("event").GetString() == "attempt_started")
                    {
                        Require(starts == 0 && terminals == 0, "Duplicate or out-of-order attempt.");
                        start = value.Clone(); starts++;
                    }
                    else
                    {
                        Require(starts == 1 && terminals == 0 && value.GetProperty("event").GetString() == "completed",
                            "No unique completed transport receipt.");
                        terminal = value.Clone(); terminals++;
                    }
                }
            }
            Require(starts == 1 && terminals == 1, "No unique completed transport receipt.");
            Require(start.GetProperty("lane").GetString() == lane && terminal.GetProperty("lane").GetString() == lane,
                "Transport lane mismatch.");
            string caseHash = start.GetProperty("caseHash").GetString();
            Require(caseHash != null && caseHash.Length == 64 && terminal.GetProperty("caseHash").GetString() == caseHash,
                "Transport case binding mismatch.");
            string expectedRelative = "cases/" + Hash(Encoding.UTF8.GetBytes(caseId)) + "/";
            Require(start.GetProperty("requestPath").GetString() == expectedRelative + "request.json" &&
                terminal.GetProperty("responsePath").GetString() == expectedRelative + "response.bin", "Journal artifact path mismatch.");
            Require(start.GetProperty("requestSha256").GetString() == Hash(request) &&
                terminal.GetProperty("responseSha256").GetString() == Hash(raw), "Journal artifact hash mismatch.");
            Require(terminal.GetProperty("httpStatus").GetInt32() == 200 &&
                terminal.GetProperty("transportError").ValueKind == JsonValueKind.Null, "Transport failed.");
            using (JsonDocument envelope = JsonDocument.Parse(raw))
                Require(envelope.RootElement.GetProperty("model").GetString() == expectedModel &&
                    terminal.GetProperty("responseMetadata").GetProperty("model").GetString() == expectedModel,
                    "Response model identity mismatch.");
            return new UTF8Encoding(false, true).GetString(raw);
        }

        private static byte[] ReadBounded(string path)
        {
            var file = new FileInfo(path);
            Require(file.Exists && file.Length <= 4 * 1024 * 1024 && (file.Attributes & FileAttributes.ReparsePoint) == 0,
                "Missing, redirected or oversized capture artifact.");
            return File.ReadAllBytes(path);
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); }
        private static string Hash(byte[] bytes)
        { return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); }
    }
}
