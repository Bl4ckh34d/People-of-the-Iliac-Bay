using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using PeopleMemoryTransfer;

if (args.Length > 0 && args[0] == "--child")
{
    Console.WriteLine(JsonSerializer.Serialize(args.Skip(2).ToArray()));
    if (args[1] == "ok") File.WriteAllText(args[4], "fixture child output");
    if (args[1] == "mark") File.WriteAllText(args[2], "started");
    if (args[1] == "fail") Console.Error.WriteLine("Fixture backend rejected transfer.");
    return args[1] == "fail" ? 9 : 0;
}

string root = Path.Combine(Path.GetTempPath(), "memory-gui-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string source = Path.Combine(root, "source with spaces", "SAVE4");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "mod_peopleoftheiliacbay.txt"), "{}");
    string package = Path.Combine(root, "package with spaces ü.zip");
    string saves = Path.Combine(root, "destination saves");
    Directory.CreateDirectory(saves);
    Directory.CreateDirectory(Path.Combine(saves, "SAVE0"));
    File.WriteAllText(Path.Combine(saves, "SAVE1"), "occupied");

    var export = TransferRequest.Export(Path.Combine(source, "..", "SAVE4"), package);
    Check(export.Source == Path.GetFullPath(source) && export.Target == Path.GetFullPath(package), "Export resolves source and package paths.");
    Check(TransferRequest.FindUnusedSlot(saves) == "SAVE2", "Slot suggestion skips both occupied files and directories.");
    File.WriteAllText(package, "existing package");
    var import = TransferRequest.Import(package, saves, "SAVE2");
    Check(import.Target == Path.Combine(saves, "SAVE2"), "Import preview resolves the exact selected slot.");
    Reject(() => TransferRequest.Import(package, saves, "../SAVE2"), "Traversal slot must be rejected.");
    Reject(() => TransferRequest.Import(package, saves, "SAVE0"), "Occupied directory must be rejected.");
    Reject(() => TransferRequest.Import(package, saves, "SAVE1"), "Occupied file must be rejected.");
    Reject(() => TransferRequest.Export("", package), "Empty source cannot resolve to the working directory.");
    Reject(() => TransferRequest.Export(source, package), "Existing package must not be overwritten.");
    File.Delete(package);

    string assembly = Assembly.GetExecutingAssembly().Location;
    var noArtifact = await TransferRunner.RunAsync(new BackendCommand("dotnet", new[] { assembly, "--child", "echo" }), export);
    Check(!noArtifact.Success, "Zero exit without the promised artifact must not report success.");
    var echo = new BackendCommand("dotnet", new[] { assembly, "--child", "ok" });
    var echoed = await TransferRunner.RunReviewedAsync(echo, export, () => true);
    Check(echoed is { Success: true } && File.Exists(package), "Confirmed review runs backend and reports success only with its artifact.");
    var arguments = JsonSerializer.Deserialize<string[]>(echoed!.Output.Trim())!;
    Check(arguments.SequenceEqual(new[] { "export", source, package }), "Child process receives exact spaced/Unicode arguments without shell parsing.");
    File.Delete(package);
    string marker = Path.Combine(root, "child-started");
    var cancelled = await TransferRunner.RunReviewedAsync(new BackendCommand("dotnet", new[] { assembly, "--child", "mark", marker }), export, () => false);
    Check(cancelled is null && !File.Exists(marker) && !File.Exists(package), "Cancelling review starts no subprocess and produces no package.");
    var failed = await TransferRunner.RunAsync(new BackendCommand("dotnet", new[] { assembly, "--child", "fail" }), import);
    Check(!failed.Success && failed.Output.Contains("Fixture backend rejected transfer."), "Nonzero backend exit retains error details.");
    Check(failed.Message.Contains("matching full Saves backup") && failed.Message.Contains("new slot"), "Failures provide safe recovery choices.");
    var missing = await TransferRunner.RunAsync(new BackendCommand(Path.Combine(root, "missing.exe")), export);
    Check(!missing.Success && missing.Message.Contains("matching full Saves backup"), "Missing bundled backend reports a recoverable failure.");

    if (args.Length > 0)
    {
        if (Process.GetProcessesByName("DaggerfallUnity").Length > 0)
            Console.WriteLine("SKIP actual backend: DaggerfallUnity is running; guard preserved.");
        else
        {
            var backend = args[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? new BackendCommand("dotnet", new[] { Path.GetFullPath(args[0]) })
                : new BackendCommand(Path.GetFullPath(args[0]));
            var exported = await TransferRunner.RunAsync(backend, export);
            Check(exported.Success && File.Exists(package), "Actual backend exports a temporary fixture.");
            var imported = await TransferRunner.RunAsync(backend, TransferRequest.Import(package, saves, "SAVE2"));
            Check(imported.Success && File.ReadAllText(Path.Combine(saves, "SAVE2", "mod_peopleoftheiliacbay.txt")) == "{}", "Actual backend imports fixture into the reviewed slot.");
            // A destination appearing after review must still be rejected by the backend.
            var stale = TransferRequest.Import(package, saves, "SAVE3");
            Directory.CreateDirectory(stale.Target);
            File.WriteAllText(Path.Combine(stale.Target, "sentinel"), "keep");
            var collision = await TransferRunner.RunAsync(backend, stale);
            Check(!collision.Success && File.ReadAllText(Path.Combine(stale.Target, "sentinel")) == "keep", "Backend rechecks an occupied slot after review without overwrite.");
            File.WriteAllText(Path.Combine(source, "mod_peopleoftheiliacbay.txt"), "{\"ConversationArchive\":{\"Version\":1,\"ArchiveId\":\"00112233445566778899aabbccddeeff\",\"Head\":\"" + new string('a',64) + "\"}}");
            string broken = Path.Combine(root, "broken.zip");
            var rejected = await TransferRunner.RunAsync(backend, TransferRequest.Export(source, broken));
            Check(!rejected.Success && !File.Exists(broken) && rejected.Message.Contains("matching full Saves backup"), "Missing history fails closed and presents recovery instructions.");
        }
    }
    Console.WriteLine("PASS guided memory transfer regression checks.");
    return 0;
}
finally { Directory.Delete(root, true); }

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}
static void Reject(Action action, string message)
{
    try { action(); }
    catch (ArgumentException) { Console.WriteLine("PASS " + message); return; }
    catch (IOException) { Console.WriteLine("PASS " + message); return; }
    throw new Exception(message);
}
