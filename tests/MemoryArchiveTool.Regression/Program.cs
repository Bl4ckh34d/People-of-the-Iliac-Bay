using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using PeopleOfTheIliacBay.Core;

internal static class PortableRegression
{
    private static readonly JsonSerializerOptions SaveJson = new JsonSerializerOptions { IncludeFields = true };
    private static int assertions;
    private static int failures;
    private static readonly List<string> FixtureRoots = new List<string>();

    private static int Main()
    {
        Run("cue ancestry survives export, import and Core restore", CueRoundTrip);
        Run("missing or altered cue chunks reject export and import", MissingAndAlteredCue);
        Run("foreign cue branches and destination collisions cannot publish a save", BranchAndCollision);
        Run("older manifest and manifestless packages remain importable without cues", LegacyPackages);
        Console.WriteLine("MemoryArchiveTool regression: " + failures + " failure(s), " + assertions + " assertions.");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
        finally
        {
            foreach (string root in FixtureRoots)
                if (Directory.Exists(root) && Path.GetFileName(root).StartsWith("memory-archive-tool-fixture-", StringComparison.Ordinal))
                    Directory.Delete(root, true);
            FixtureRoots.Clear();
        }
    }

    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException(message); }

    private static int Tool(params string[] args)
    {
        var method = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        if (method == null) throw new InvalidOperationException("Portable tool entry point missing.");
        string previousTmp = Environment.GetEnvironmentVariable("TMP");
        string previousTemp = Environment.GetEnvironmentVariable("TEMP");
        string staging = FixtureRoots.Last();
        Environment.SetEnvironmentVariable("TMP", staging);
        Environment.SetEnvironmentVariable("TEMP", staging);
        try { return (int)method.Invoke(null, new object[] { args })!; }
        finally
        {
            Environment.SetEnvironmentVariable("TMP", previousTmp);
            Environment.SetEnvironmentVariable("TEMP", previousTemp);
        }
    }

    private static string NewRoot()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "memory-archive-tool-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        FixtureRoots.Add(root);
        return root;
    }

    private static (string Root, string Save, WorldState Snapshot) Fixture(bool cue)
    {
        string root = NewRoot();
        string save = Path.Combine(root, "SAVE1");
        string memory = Path.Combine(root, "PeopleMemory");
        Directory.CreateDirectory(save);
        var store = new WorldStateStore(17, memory);
        store.RememberConversation("alice", 100, "day", "inn", "Good evening.", "Welcome.", "model");
        WorldState snapshot = store.SnapshotForSave();
        if (cue)
        {
            var archive = new SharedConversationCueArchive(memory);
            for (int ordinal = 1; ordinal <= 2; ordinal++)
            {
                var record = new ConversationCueArchiveRecord {
                    SourceChunkHash = snapshot.ConversationArchive.Head, SourceRecordIndex = 0,
                    PersonId = "alice", ReceiptId = "receipt:" + ordinal, EpisodeId = "episode:1",
                    UtteranceId = "utterance:" + ordinal, SpeakerId = "player", ObservedText = "Good evening.",
                    ObservedAt = 100, Appraisal = new NpcExperienceAppliedAppraisal { MoodDelta = 1, AppliedMoodDelta = 1 }
                };
                snapshot.ConversationCueArchive = archive.Commit(snapshot.ConversationCueArchive, record);
            }
        }
        File.WriteAllText(Path.Combine(save, "mod_peopleoftheiliacbay.txt"), JsonSerializer.Serialize(snapshot, SaveJson));
        return (root, save, snapshot);
    }

    private static void CueRoundTrip()
    {
        var f = Fixture(true);
        string memoryRoot = Path.Combine(f.Root, "PeopleMemory");
        string parentFile = Directory.GetFiles(memoryRoot, "*.pic", SearchOption.AllDirectories)
            .Single(path => !Path.GetFileName(path).StartsWith(f.Snapshot.ConversationCueArchive.Head, StringComparison.Ordinal));
        string parentHash = Path.GetFileNameWithoutExtension(parentFile);
        new SharedConversationCueArchive(memoryRoot).Commit(new ConversationCueArchiveCheckpoint {
            ArchiveId = f.Snapshot.ConversationArchive.ArchiveId, Head = parentHash
        }, new ConversationCueArchiveRecord {
            SourceChunkHash = f.Snapshot.ConversationArchive.Head, SourceRecordIndex = 0,
            PersonId = "alice", ReceiptId = "receipt:sibling", EpisodeId = "episode:sibling",
            UtteranceId = "utterance:sibling", SpeakerId = "player", ObservedText = "Sibling branch.",
            ObservedAt = 100, Appraisal = new NpcExperienceAppliedAppraisal()
        });
        Check(Directory.GetFiles(memoryRoot, "*.pic", SearchOption.AllDirectories).Length == 3,
            "fixture contains a valid sibling cue branch");
        string zip = Path.Combine(f.Root, "portable.zip");
        string destination = Path.Combine(f.Root, "destination");
        Check(Tool("export", f.Save, zip) == 0, "export with cue must succeed");
        using (var package = ZipFile.OpenRead(zip))
        {
            Check(package.Entries.Count(x => x.FullName.EndsWith(".pic", StringComparison.Ordinal)) == 2,
                "exact cue ancestry must be packaged");
            Check(package.Entries.Count(x => x.FullName.EndsWith(".pim", StringComparison.Ordinal)) == 1,
                "conversation ancestry must stay packaged");
            using var manifest = JsonDocument.Parse(package.GetEntry("manifest.json")!.Open());
            Check(manifest.RootElement.GetProperty("formatVersion").GetInt32() == 2, "cue package needs manifest v2");
            Check(manifest.RootElement.GetProperty("cueHead").GetString() == f.Snapshot.ConversationCueArchive.Head,
                "manifest binds exact cue head");
            Check(manifest.RootElement.GetProperty("cueAncestry").GetArrayLength() == 2,
                "manifest binds exact cue ancestry");
        }
        Check(Tool("import", zip, destination, "SAVE2") == 0, "cue import must succeed");
        var restored = new WorldStateStore(0, Path.Combine(destination, "PeopleMemory"));
        var imported = JsonSerializer.Deserialize<WorldState>(File.ReadAllText(Path.Combine(destination,
            "SAVE2", "mod_peopleoftheiliacbay.txt")), SaveJson)!;
        restored.Restore(imported);
        Check(restored.Snapshot().ConversationCueArchive.Head == f.Snapshot.ConversationCueArchive.Head,
            "Core Restore retains cue checkpoint");
        Check(new SharedConversationCueArchive(Path.Combine(destination, "PeopleMemory"))
            .Load(restored.Snapshot().ConversationCueArchive).Count == 2, "restored cue ancestry remains readable");
    }

    private static void MissingAndAlteredCue()
    {
        var f = Fixture(true);
        string cuePath = Directory.GetFiles(Path.Combine(f.Root, "PeopleMemory"), "*.pic", SearchOption.AllDirectories)
            .Single(path => !Path.GetFileName(path).StartsWith(f.Snapshot.ConversationCueArchive.Head, StringComparison.Ordinal));
        string zip = Path.Combine(f.Root, "portable.zip");
        File.Delete(cuePath);
        Check(Tool("export", f.Save, zip) == 1 && !File.Exists(zip),
            "nonempty cue head with missing source chunk cannot produce package");

        f = Fixture(true);
        zip = Path.Combine(f.Root, "portable.zip");
        Check(Tool("export", f.Save, zip) == 0, "valid cue package setup");
        string missing = RewritePackage(zip, stage => {
            string parent = Directory.GetFiles(Path.Combine(stage, "PeopleMemory"), "*.pic", SearchOption.AllDirectories)
                .Single(path => !Path.GetFileName(path).StartsWith(f.Snapshot.ConversationCueArchive.Head, StringComparison.Ordinal));
            File.Delete(parent);
            string parentDirectory = Path.GetDirectoryName(parent)!;
            if (Directory.GetFileSystemEntries(parentDirectory).Length == 0)
                Directory.Delete(parentDirectory);
            File.Delete(Path.Combine(stage, "manifest.json"));
        });
        string missingTarget = Path.Combine(f.Root, "missing-target");
        Check(Tool("import", missing, missingTarget, "SAVE2") == 1,
            "manifestless package cannot hide a missing cue ancestor");
        Check(!Directory.Exists(Path.Combine(missingTarget, "SAVE2")), "missing cue ancestor cannot publish save");
        string altered = RewritePackage(zip, stage => {
            string pic = Directory.GetFiles(Path.Combine(stage, "PeopleMemory"), "*.pic", SearchOption.AllDirectories).First();
            File.WriteAllBytes(pic, new byte[] { 1, 2, 3, 4 });
            UpdateManifestFile(stage, pic);
        });
        string target = Path.Combine(f.Root, "altered-target");
        Check(Tool("import", altered, target, "SAVE2") == 1, "tampered cue fails despite rewritten manifest checksum");
        Check(!Directory.Exists(Path.Combine(target, "SAVE2")), "failed import cannot publish save slot");
    }

    private static void BranchAndCollision()
    {
        var f = Fixture(true);
        string zip = Path.Combine(f.Root, "portable.zip");
        Check(Tool("export", f.Save, zip) == 0, "branch setup export");
        string extra = RewritePackage(zip, stage => {
            string pic = Directory.GetFiles(Path.Combine(stage, "PeopleMemory"), "*.pic", SearchOption.AllDirectories).First();
            string branch = Path.Combine(Path.GetDirectoryName(pic)!, new string('a', 64) + ".pic");
            File.Copy(pic, branch);
            UpdateManifestFile(stage, branch);
        });
        string branchTarget = Path.Combine(f.Root, "branch-target");
        Check(Tool("import", extra, branchTarget, "SAVE2") == 1, "unreferenced cue branch must reject");
        Check(!Directory.Exists(Path.Combine(branchTarget, "SAVE2")), "branch rejection cannot publish save");

        string collisionTarget = Path.Combine(f.Root, "collision-target");
        string sourcePic = Directory.GetFiles(Path.Combine(f.Root, "PeopleMemory"), "*.pic", SearchOption.AllDirectories).First();
        string relative = Path.GetRelativePath(Path.Combine(f.Root, "PeopleMemory"), sourcePic);
        string collision = Path.Combine(collisionTarget, "PeopleMemory", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(collision)!);
        File.WriteAllBytes(collision, new byte[] { 1, 2, 3, 4 });
        Check(Tool("import", zip, collisionTarget, "SAVE2") == 1, "immutable cue collision must reject");
        Check(!Directory.Exists(Path.Combine(collisionTarget, "SAVE2")), "collision cannot publish save");
        Check(Directory.GetFiles(Path.Combine(collisionTarget, "PeopleMemory"), "*.pim", SearchOption.AllDirectories).Length == 0,
            "cue collision must be preflighted before either ancestry is copied");
    }

    private static void LegacyPackages()
    {
        var f = Fixture(false);
        f.Snapshot.Version = 53;
        f.Snapshot.ConversationCueArchive = null;
        File.WriteAllText(Path.Combine(f.Save, "mod_peopleoftheiliacbay.txt"), JsonSerializer.Serialize(f.Snapshot, SaveJson));
        string zip = Path.Combine(f.Root, "portable.zip");
        Check(Tool("export", f.Save, zip) == 0, "legacy save can export");
        string oldManifest = RewritePackage(zip, stage => {
            string file = Path.Combine(stage, "manifest.json");
            var node = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            node["formatVersion"] = 1;
            node.Remove("cueHead"); node.Remove("cueAncestry");
            File.WriteAllText(file, node.ToJsonString());
        });
        Check(Tool("import", oldManifest, Path.Combine(f.Root, "old-manifest-target"), "SAVE2") == 0,
            "v1 manifest remains importable without cues");
        string noManifest = RewritePackage(zip, stage => File.Delete(Path.Combine(stage, "manifest.json")));
        Check(Tool("import", noManifest, Path.Combine(f.Root, "manifestless-target"), "SAVE2") == 0,
            "manifestless legacy package remains importable without cues");

        var current = Fixture(true);
        string cueZip = Path.Combine(current.Root, "current.zip");
        Check(Tool("export", current.Save, cueZip) == 0, "current cue package setup");
        string falseV1 = RewritePackage(cueZip, stage => {
            string file = Path.Combine(stage, "manifest.json");
            var node = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            node["formatVersion"] = 1;
            node.Remove("cueHead"); node.Remove("cueAncestry");
            File.WriteAllText(file, node.ToJsonString());
        });
        string downgradeTarget = Path.Combine(current.Root, "false-v1-target");
        Check(Tool("import", falseV1, downgradeTarget, "SAVE2") == 1,
            "v1 manifest cannot silently ignore a current nonempty cue checkpoint");
        Check(!Directory.Exists(Path.Combine(downgradeTarget, "SAVE2")), "false v1 cannot publish save");
    }

    private static string RewritePackage(string original, Action<string> edit)
    {
        string stage = Path.Combine(Path.GetDirectoryName(original)!, "rewrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        ZipFile.ExtractToDirectory(original, stage);
        edit(stage);
        string result = Path.Combine(Path.GetDirectoryName(original)!, Guid.NewGuid().ToString("N") + ".zip");
        ZipFile.CreateFromDirectory(stage, result, CompressionLevel.Optimal, false);
        Directory.Delete(stage, true);
        return result;
    }

    private static void UpdateManifestFile(string stage, string file)
    {
        string manifestFile = Path.Combine(stage, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestFile))!.AsObject();
        string path = Path.GetRelativePath(stage, file).Replace(Path.DirectorySeparatorChar, '/');
        byte[] bytes = File.ReadAllBytes(file);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var files = manifest["files"]!.AsArray();
        var row = files.FirstOrDefault(x => x!["path"]!.GetValue<string>() == path);
        if (row == null)
            files.Add(new JsonObject { ["path"] = path, ["length"] = bytes.LongLength, ["sha256"] = hash });
        else
        { row["length"] = bytes.LongLength; row["sha256"] = hash; }
        File.WriteAllText(manifestFile, manifest.ToJsonString());
    }
}
