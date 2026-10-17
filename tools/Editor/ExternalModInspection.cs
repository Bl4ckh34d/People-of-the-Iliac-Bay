using System;
using System.IO;
using UnityEngine;

// Read-only inspection of separately installed dependencies, never bundled assets.
public static class ExternalModInspection
{
    public static void Inspect()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        string output = Path.Combine(root, "work/external-mods/inspection");
        Directory.CreateDirectory(output);
        var names = new DaggerfallWorkshop.Game.Utility.NameHelper();
        for (int bank = 0; bank < 8; bank++)
            foreach (DaggerfallWorkshop.Game.Entity.Genders gender in new[] {
                DaggerfallWorkshop.Game.Entity.Genders.Male, DaggerfallWorkshop.Game.Entity.Genders.Female })
                for (int sample = 0; sample < 25; sample++)
                    if (string.IsNullOrWhiteSpace(names.FullName(
                        (DaggerfallWorkshop.Game.Utility.NameHelper.BankTypes)bank, gender)))
                        throw new InvalidOperationException("Empty installed name bank: " + bank);
        Debug.Log("EXTERNAL_NAMES_VERIFIED: 400 generated names across eight banks and both genders");
        foreach (string file in Directory.GetFiles(Path.Combine(root,
            "Player/DaggerfallUnity_Data/StreamingAssets/Mods"), "*.dfmod"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name == "livingcities" || name == "basicroads") continue;
            var bundle = AssetBundle.LoadFromFile(file);
            if (!bundle) throw new InvalidOperationException("Cannot load dependency: " + file);
            try
            {
                File.WriteAllLines(Path.Combine(output, name + "-assets.txt"), bundle.GetAllAssetNames());
                foreach (string asset in bundle.GetAllAssetNames())
                {
                    if (asset.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                        asset.EndsWith(".cs.txt", StringComparison.OrdinalIgnoreCase) ||
                        asset.EndsWith(".dll.bytes", StringComparison.OrdinalIgnoreCase) ||
                        asset.EndsWith("modsettings.json", StringComparison.OrdinalIgnoreCase))
                    {
                        var source = bundle.LoadAsset<TextAsset>(asset);
                        if (source)
                        {
                            string folder = Path.Combine(output, name);
                            Directory.CreateDirectory(folder);
                            if (asset.EndsWith(".dll.bytes", StringComparison.OrdinalIgnoreCase))
                                File.WriteAllBytes(Path.Combine(folder, Path.GetFileNameWithoutExtension(asset)), source.bytes);
                            else File.WriteAllText(Path.Combine(folder, Path.GetFileName(asset)), source.text);
                        }
                    }
                    if (!asset.EndsWith(".dfmod.json", StringComparison.OrdinalIgnoreCase)) continue;
                    var text = bundle.LoadAsset<TextAsset>(asset);
                    if (!text) throw new InvalidOperationException("Missing mod metadata: " + file);
                    File.WriteAllText(Path.Combine(output, name + ".json"), text.text);
                    Debug.Log("EXTERNAL_MOD_INSPECTED: " + name);
                }
            }
            finally { bundle.Unload(true); }
        }
        Debug.Log("EXTERNAL_MOD_INSPECTION_SUCCESS");
    }
}
