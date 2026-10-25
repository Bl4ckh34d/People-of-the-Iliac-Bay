using System;
using System.IO;

// Deployment records the checkout location separately from the shipped manifest.
// Editor tools therefore work regardless of the checkout directory's name.
public static class PeopleOfTheIliacBayPaths
{
    public static string Repository(string relativePath)
    {
        const string marker = "Assets/Game/Mods/PeopleOfTheIliacBay/Editor/repository-root.txt";
        string root = Path.GetFullPath(File.ReadAllText(marker).Trim());
        if (!File.Exists(Path.Combine(root, "mod/PeopleOfTheIliacBay.dfmod.json")))
            throw new InvalidOperationException("Mod checkout is unavailable. Run tools/Deploy.ps1 again.");
        return Path.Combine(root, relativePath);
    }
}
