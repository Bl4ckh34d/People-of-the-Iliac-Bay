using System.Diagnostics;


namespace PeopleMemoryTransfer;

// This layer resolves user choices only. The CLI remains responsible for every archive
// validation and publication check, including changes after the user reviewed these paths.
internal sealed record TransferRequest
{
    public string Source { get; }
    public string Target { get; }
    public string[] Arguments { get; }
    public string Review { get; }

    private TransferRequest(string source, string target, string[] arguments, string review)
    { Source = source; Target = target; Arguments = arguments; Review = review; }

    public static TransferRequest Export(string source, string package)
    {
        source = Resolve(source, "Choose the source SAVE folder.");
        package = Resolve(package, "Choose a new package ZIP path.");
        if (!Directory.Exists(source)) throw new IOException("The source SAVE folder does not exist.");
        if (!File.Exists(Path.Combine(source, "mod_peopleoftheiliacbay.txt")))
            throw new IOException("Choose the SAVE folder containing mod_peopleoftheiliacbay.txt, not the parent Saves folder.");
        if (Occupied(package)) throw new IOException("The package path already exists. Choose a new ZIP name; nothing will be overwritten.");
        return new TransferRequest(source, package, new[] { "export", source, package },
            $"Export source SAVE folder:\r\n{source}\r\n\r\nNew portable package:\r\n{package}");
    }

    public static TransferRequest Import(string package, string saves, string slot)
    {
        package = Resolve(package, "Choose a portable package ZIP.");
        saves = Resolve(saves, "Choose your configured DFU Saves folder.");
        if (!File.Exists(package)) throw new IOException("The selected portable package does not exist.");
        if (!Directory.Exists(saves)) throw new IOException("Choose an existing configured Saves folder.");
        if ((string.IsNullOrEmpty(slot) || !slot.StartsWith("SAVE",StringComparison.Ordinal) ||
            slot.Length==4 || slot.Skip(4).Any(c=>c<'0' || c>'9')))
            throw new ArgumentException("Enter an unused SAVE number, for example SAVE12.");
        string target = Path.Combine(saves, slot!);
        if (Occupied(target)) throw new IOException("The selected save slot already exists. Choose an unused slot; nothing will be overwritten.");
        return new TransferRequest(package, target, new[] { "import", package, saves, slot! },
            $"Portable package:\r\n{package}\r\n\r\nDestination Saves folder:\r\n{saves}\r\n\r\nNew save slot:\r\n{target}");
    }

    public static string FindUnusedSlot(string saves)
    {
        saves = Resolve(saves, "Choose your configured DFU Saves folder.");
        if (!Directory.Exists(saves)) throw new IOException("Choose an existing configured Saves folder.");
        for (int index = 0; index < int.MaxValue; index++)
            if (!Occupied(Path.Combine(saves, "SAVE" + index))) return "SAVE" + index;
        throw new IOException("No unused SAVE number was found.");
    }

    private static string Resolve(string path, string message)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(message);
        return Path.GetFullPath(path);
    }
    private static bool Occupied(string path) => File.Exists(path) || Directory.Exists(path);
}
internal sealed record BackendCommand(string Executable, string[]? Prefix = null);
internal sealed record TransferResult(bool Success, string Output, string Message);
internal static class TransferRunner
{
    public static async Task<TransferResult?> RunReviewedAsync(BackendCommand backend, TransferRequest request, Func<bool> confirm)
    {
        if (!confirm()) return null;
        return await RunAsync(backend, request);
    }
    public const string Recovery = "If history is missing or corrupt, restore the matching full Saves backup, or import the matching portable package into a new slot. Keep the original save until recovery is complete. Do not delete PeopleMemory or discard the checkpoint.";

    public static async Task<TransferResult> RunAsync(BackendCommand backend, TransferRequest request)
    {
        try
        {
            var start = new ProcessStartInfo(backend.Executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in backend.Prefix ?? Array.Empty<string>()) start.ArgumentList.Add(argument);
            foreach (string argument in request.Arguments) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            process.Start();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string output = ((await stdout).TrimEnd() + Environment.NewLine + (await stderr).TrimEnd()).Trim();
            if (process.ExitCode != 0)
                return new TransferResult(false, output, "Transfer did not complete. Close Daggerfall Unity before retrying.\r\n\r\n" + Recovery);
            bool exists = request.Arguments[0] == "export" ? File.Exists(request.Target)
                : File.Exists(Path.Combine(request.Target, "mod_peopleoftheiliacbay.txt"));
            if (!exists)
                return new TransferResult(false, output, "The helper exited without the expected package or save slot. Transfer success could not be confirmed.\r\n\r\n" + request.Review + "\r\n\r\n" + Recovery);
            return new TransferResult(true, output, "Transfer completed.\r\n\r\n" + request.Review);
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new TransferResult(false, error.Message,
                "Could not run the transfer helper. Keep MemoryArchiveTool.exe beside MemoryArchiveGui.exe and retry.\r\n\r\n" + Recovery);
        }
    }
}
