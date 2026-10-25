using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallConnect;
using DaggerfallConnect.FallExe;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;
using UnityEditor;
using UnityEngine;

public static class PeopleOfTheIliacBayBuild
{
    private static string[] splitPortraitPackageFiles;
    private static string splitPortraitTextureDirectory;
    private static Type splitPortraitDecodedAssetType;
    private static readonly HashSet<string> splitPortraitResolvedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static void VerifyDynamicSittingOnly()
    {
        var assembly = System.Reflection.Assembly.Load(File.ReadAllBytes("Assets/Game/Mods/PeopleOfTheIliacBay/BuildText/PeopleOfTheIliacBay.Runtime.dll.bytes"));
        VerifyDynamicSitting(assembly, path => File.ReadAllText(path));
    }
    private static void VerifyDynamicSitting(System.Reflection.Assembly assembly,Func<string,string> read)
    {
        var artType=assembly.GetType("PeopleOfTheIliacBay.DFU.SittingArt",true);
        var parse=artType.GetMethod("ReadManifest");
        Require(parse!=null,"Runtime sitting manifest parser exists");
        int count=0;
        foreach(string path in Directory.GetFiles("Assets/Game/Mods/PeopleOfTheIliacBay/Textures","sit-*.json"))
        {
            int archive=int.Parse(Path.GetFileNameWithoutExtension(path).Substring(4));
            object definition=parse.Invoke(null,new object[]{read(path),archive});
            var frames=(Array)definition.GetType().GetField("frames").GetValue(definition);
            Require(frames!=null&&frames.Length==8,"Runtime DLL retains sitting frames for "+archive);
            count++;
        }
        Require(count==24,"All native civilian sitting manifests verified");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DYNAMIC_SITTING_PASS: compiled DLL; bundled manifests="+count+"; frames="+count*8);
    }
    // Editor-owned fixture objects only. Does not build or install a bundle and
    // does not access the running Player process or any character save files.
    public static void VerifyNativeInventoryOnly()
    {
        var info=JsonUtility.FromJson<ModInfo>(File.ReadAllText("Assets/Game/Mods/PeopleOfTheIliacBay/PeopleOfTheIliacBay.dfmod.json"));
        var fixtureMod=new Mod();fixtureMod.ModInfo.Files=info.Files;
        typeof(Mod).GetMethod("GetTypesInEditor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
            .Invoke(fixtureMod,null);
        VerifyPropertyKeyItemTemplate();
        Debug.Log(NativeNpcInventoryProbe.Run());
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_INVENTORY_EDITOR_SUCCESS");
    }
    private static void RequirePlayerClosed()
    {
        var players = System.Diagnostics.Process.GetProcessesByName("DaggerfallUnity");
        bool running = players.Length != 0;
        foreach (var player in players) player.Dispose();
        if (running) throw new InvalidOperationException("Daggerfall Unity is running; build/install will not replace a loaded mod. Close the game first.");
    }

    public static void VerifyAndBuildQuestBindingProbe()
    {
        VerifyAndBuild();
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        const string staging = "Assets/Game/Mods/PeopleOfTheIliacBayQuestBindingProbe";
        Directory.CreateDirectory(staging);
        File.Copy(PeopleOfTheIliacBayPaths.Repository("probes/quest-binding/bin/Release/net472/QuestBindingProbe.dll"), staging + "/QuestBindingProbe.dll.bytes", true);
        var info = new ModInfo { ModTitle = "People of the Iliac Bay Quest Binding Probe", ModVersion = "0.0.1", ModAuthor = "Daniel",
            DFUnity_Version = "1.1.1", ModDescription = "Opt-in quest binding feasibility test. Not gameplay.",
            GUID = "17f7edbf-2400-47b3-bd80-d83cbbcdbe69" };
        File.WriteAllText(staging + "/QuestBindingProbe.dfmod.json", JsonUtility.ToJson(info, true));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string output = PeopleOfTheIliacBayPaths.Repository("work/QuestBindingProbeBuild"); Directory.CreateDirectory(output);
        var build = new AssetBundleBuild { assetBundleName = "peopleoftheiliacbayquestbindingprobe.dfmod", assetNames = new[] {
            staging + "/QuestBindingProbe.dll.bytes", staging + "/QuestBindingProbe.dfmod.json" } };
        Require(BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64) != null, "Quest binding probe bundle");
        RequirePlayerClosed();
        File.Copy(Path.Combine(output, "peopleoftheiliacbayquestbindingprobe.dfmod"),
            Path.Combine(root, "Player/DaggerfallUnity_Data/StreamingAssets/Mods/peopleoftheiliacbayquestbindingprobe.dfmod"), true);
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_QUEST_BINDING_PROBE_BUILT");
    }
    public static void BuildGuardProbe()
    {
        RequirePlayerClosed();
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        const string staging = "Assets/Game/Mods/PeopleOfTheIliacBayGuardProbe";
        Directory.CreateDirectory(staging);
        File.Copy(PeopleOfTheIliacBayPaths.Repository("work/Harmony/net472/0Harmony.dll"), staging + "/0Harmony.dll.bytes", true);
        File.Copy(PeopleOfTheIliacBayPaths.Repository("probes/guard/bin/Release/net472/GuardProbe.dll"), staging + "/GuardProbe.dll.bytes", true);
        var info = new ModInfo { ModTitle = "People of the Iliac Bay Guard Probe", ModVersion = "0.0.1", ModAuthor = "Daniel",
            DFUnity_Version = "1.1.1", ModDescription = "Temporary opt-in feasibility test. Not gameplay.",
            GUID = "88cbbd21-cad5-4f82-b360-2046b18845d4" };
        File.WriteAllText(staging + "/GuardProbe.dfmod.json", JsonUtility.ToJson(info, true));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string output = PeopleOfTheIliacBayPaths.Repository("work/GuardProbeBuild");
        Directory.CreateDirectory(output);
        var build = new AssetBundleBuild { assetBundleName = "peopleoftheiliacbayguardprobe.dfmod", assetNames = new[] {
            staging + "/0Harmony.dll.bytes", staging + "/GuardProbe.dll.bytes", staging + "/GuardProbe.dfmod.json" } };
        Require(BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64) != null, "Guard probe bundle");
        RequirePlayerClosed();
        File.Copy(Path.Combine(output, "peopleoftheiliacbayguardprobe.dfmod"),
            Path.Combine(root, "Player/DaggerfallUnity_Data/StreamingAssets/Mods/peopleoftheiliacbayguardprobe.dfmod"), true);
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_PROBE_BUILT");
    }

    public static void VerifyAndBuild()
    {
        RequirePlayerClosed();
        VerifyPackage(true);
    }

    // Builds and verifies the same package without touching the running Player.
    // Installation retains its process check immediately before replacement.
    public static void VerifyAndPackage()
    {
        VerifyPackage(false);
    }

    private static void VerifyPackage(bool install)
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        VerifySerialization();
        VerifyNpcProfileProgress();
        VerifyAlarmSerialization();
        VerifyGuardPersistence();
        VerifyDefenseLedger();
        VerifyLodging();
        VerifyWitnessRedirect();
        VerifyTravelers();
        VerifySettings();
        VerifyPropertyKeyItemTemplate();
        Debug.Log(NativeNpcInventoryProbe.Run());
        VerifyHousing();
        VerifyBeds();
        VerifySleepingArt();
        VerifySleepingAlignment();
        VerifyActivityFacing();
        VerifyActivitySight();
        Require(!StreetPopulation.IsVisitDestination(DaggerfallConnect.DFLocation.BuildingTypes.House1), "House1 is not an open shop destination");
        Require(typeof(IHasModSaveData).IsAssignableFrom(typeof(PeopleOfTheIliacBayMod)), "Save adapter contract");
        Require(typeof(BuildingDirectory).GetMethod("GetBuildingsOfType") != null, "Building enumeration hook");
        Require(typeof(CityNavigation).GetMethod("GetNavGridWeightLocal", new[] { typeof(int), typeof(int) }) != null, "Street grid hook");
        Require(typeof(PopulationManager).GetEvent("OnMobileNPCEnable") != null, "Mobile lifecycle hook");
        Require(typeof(PlayerEntity).GetMethod("SpawnCityGuards") != null, "Guard spawn entry");
        Require(!typeof(PlayerEntity).GetMethod("SpawnCityGuards").IsVirtual, "Guard spawn limitation changed; reassess interception");
        VerifyAdapter();

        const string manifestPath = "Assets/Game/Mods/PeopleOfTheIliacBay/PeopleOfTheIliacBay.dfmod.json";
        var info = JsonUtility.FromJson<ModInfo>(File.ReadAllText(manifestPath));
        Require(info.Files.Contains("Assets/Game/Mods/PeopleOfTheIliacBay/Libraries/0Harmony.dll.bytes") &&
            info.Files.Contains("Assets/Game/Mods/PeopleOfTheIliacBay/Libraries/PeopleOfTheIliacBay.QuestHooks.dll.bytes"),
            "Editor virtual mod must discover both quest hook libraries from the manifest");
        const string staging = "Assets/Game/Mods/PeopleOfTheIliacBay/BuildText";
        Directory.CreateDirectory(staging);
        var assets = new List<string> { manifestPath };
        assets.AddRange(NativeNavigationBake.BakeCaptured());
        var scriptPaths = new List<string>();
        string portraitManifestAsset = null;
        foreach (string source in info.Files)
        {
            // Virtual mods list cached navigation assets too. Package only the
            // validated set returned by BakeCaptured above, once each.
            if(source.StartsWith("Assets/Game/Mods/PeopleOfTheIliacBay/Navigation/",StringComparison.Ordinal))continue;
            if (source.Contains("/Libraries/") && (source.EndsWith(".dll.bytes") || source.EndsWith("/Harmony-LICENSE.txt")))
            { assets.Add(source); continue; }
            if (source.EndsWith("/modsettings.json")) { assets.Add(source); continue; }
            if (source == "Assets/Game/Mods/PeopleOfTheIliacBay/ItemTemplates.json") { assets.Add(source); continue; }
            if (source == "Assets/Game/Mods/PeopleOfTheIliacBay/Textures/portrait-split-manifest.json.bytes")
            { Require(portraitManifestAsset == null,"Portrait runtime manifest is listed once"); portraitManifestAsset=source; assets.Add(source); continue; }
            if (source.EndsWith(".png.bytes") && source.Contains("/Textures/")) { assets.Add(source); continue; }
            if(source.StartsWith("Assets/Game/Mods/PeopleOfTheIliacBay/Textures/idle-",StringComparison.Ordinal)&&source.EndsWith(".json",StringComparison.Ordinal))
            {
                int archive=int.Parse(Path.GetFileNameWithoutExtension(source).Substring(5));
                var definition=StandingArt.ReadManifest(File.ReadAllText(source),archive);
                string image=source.Substring(0,source.Length-5)+".png.bytes";
                Require(info.Files.Contains(image)&&File.Exists(image),"Standing manifest has a bundled atlas");
                var texture=new Texture2D(2,2);
                try {Require(texture.LoadImage(File.ReadAllBytes(image))&&texture.width==definition.cellSize[0]*4&&texture.height==definition.cellSize[1]*2,"Standing atlas dimensions");}
                finally {UnityEngine.Object.DestroyImmediate(texture);}
                assets.Add(source);continue;
            }
            if (source.StartsWith("Assets/Game/Mods/PeopleOfTheIliacBay/Textures/sit-", StringComparison.Ordinal) && source.EndsWith(".json", StringComparison.Ordinal))
            {
                var definition = JsonUtility.FromJson<SittingArt.Manifest>(File.ReadAllText(source));
                Require(source.EndsWith("/sit-" + definition.archive + ".json", StringComparison.Ordinal), "Sitting manifest archive matches its filename");
                SittingArt.Validate(definition, definition.archive);
                string atlas = source.Substring(0, source.Length - ".json".Length) + ".png.bytes";
                Require(info.Files.Contains(atlas) && File.Exists(atlas), "Sitting manifest has its bundled atlas");
                assets.Add(source); continue;
            }
            Require(source.EndsWith(".cs") && !source.Contains("/Editor/"), "Only runtime sources may ship");
            scriptPaths.Add(Path.Combine(Application.dataPath,source.Substring("Assets/".Length)));
        }
        string runtimeAssembly = staging + "/PeopleOfTheIliacBay.Runtime.dll.bytes";
        if(File.Exists(runtimeAssembly)) File.Delete(runtimeAssembly);
        var compiler=typeof(PeopleOfTheIliacBayBuild).Assembly.GetType("DaggerfallWorkshop.Game.Utility.ModAssemblyBuilder",true);
        Require((bool)compiler.GetMethod("Compile",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)
            .Invoke(null,new object[] { runtimeAssembly,scriptPaths.ToArray() }),"DFU precompiled runtime assembly");
        Require(portraitManifestAsset != null,"Portrait runtime manifest is bundled");
        var portraitAssembly = System.Reflection.Assembly.Load(File.ReadAllBytes(runtimeAssembly));
        VerifySplitPortraitRuntimePackage(portraitAssembly, portraitManifestAsset, info.Files.ToArray());
        assets.Add(runtimeAssembly);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        // Exercise DFU's real virtual-mod loader, which only discovers libraries listed in Files.
        var virtualMod = new Mod(); virtualMod.ModInfo.Files = info.Files;
        var loadedTypes = (Type[])typeof(Mod).GetMethod("GetTypesInEditor", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(virtualMod, null);
        Require(Array.Exists(loadedTypes, type => type.FullName == "PeopleOfTheIliacBay.QuestHooks.NativeQuestOfferHooks"),
            "Native editor loader resolves quest hook library");
        string output = PeopleOfTheIliacBayPaths.Repository("dist");
        Directory.CreateDirectory(output);
        var build = new AssetBundleBuild { assetBundleName = "peopleoftheiliacbay.dfmod", assetNames = assets.ToArray() };
        Require(BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64) != null, "Bundle build");
        string package = Path.Combine(output, "peopleoftheiliacbay.dfmod");
        AssetBundle bundle = AssetBundle.LoadFromFile(package);
        Require(bundle != null, "Bundle reopen");
        try
        {
            Require(bundle.GetAllAssetNames().Length == assets.Count, "Bundle asset count");
            foreach (string asset in assets)
                Require(asset.EndsWith(".asset",StringComparison.Ordinal)?bundle.LoadAsset<UnityEngine.AI.NavMeshData>(asset)!=null:
                    bundle.LoadAsset<TextAsset>(asset)!=null,"Missing bundled asset: "+asset);
            var compiled=System.Reflection.Assembly.Load(bundle.LoadAsset<TextAsset>(runtimeAssembly).bytes);
            VerifyDynamicSitting(compiled,path=>bundle.LoadAsset<TextAsset>(path.Replace('\\','/')).text);
            int idleCount=0;
            foreach(string path in assets)
            {
                if(!path.Contains("/Textures/idle-")||!path.EndsWith(".json"))continue;
                int archive=int.Parse(Path.GetFileNameWithoutExtension(path).Substring(5));
                object parsed=compiled.GetType("PeopleOfTheIliacBay.DFU.StandingArt",true).GetMethod("ReadManifest").Invoke(null,new object[]{bundle.LoadAsset<TextAsset>(path).text,archive});
                Require(parsed!=null,"Dynamic standing manifest parses");idleCount++;
            }
            Require(idleCount==24,"All 24 standing manifests bundled and parsed in runtime DLL");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DYNAMIC_STANDING_PASS: 24 atlases, 192 bearings, runtime DLL manifests.");
        }
        finally { bundle.Unload(true); }
        if (!install)
        {
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PACKAGE_SUCCESS: serializer, adapter lifecycle, contracts and bundle verified; not installed.");
            return;
        }
        RequirePlayerClosed();
        File.Copy(package, Path.Combine(root, "Player/DaggerfallUnity_Data/StreamingAssets/Mods/peopleoftheiliacbay.dfmod"), true);
        string evidence = PeopleOfTheIliacBayPaths.Repository("work");
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "unity-build-result.json"),
            "{\"fullSerializerRoundTrip\":true,\"adapterSaveLifecycle\":true,\"publicHookContracts\":true,\"assetCount\":" + assets.Count +
            ",\"nativeSettings\":true,\"installed\":true,\"physicalActors\":false}");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BUILD_SUCCESS: serializer, adapter lifecycle, contracts and bundle verified; installed.");
    }

    private static void VerifySplitPortraitRuntimePackage(System.Reflection.Assembly runtimeAssembly,
        string manifestAssetPath, string[] files)
    {
        Require(CountPath(files, manifestAssetPath) == 1, "Portrait runtime manifest appears exactly once in mod Files");
        splitPortraitPackageFiles = files;
        splitPortraitTextureDirectory = Path.GetFullPath(Path.Combine(Application.dataPath,
            "Game/Mods/PeopleOfTheIliacBay/Textures"));
        splitPortraitResolvedAssets.Clear();
        try
        {
            splitPortraitDecodedAssetType = runtimeAssembly.GetType("PeopleOfTheIliacBay.DFU.SplitPortraitDecodedAsset", true);
            Type manifestType = runtimeAssembly.GetType("PeopleOfTheIliacBay.DFU.SplitPortraitManifest", true);
            Type validatorType = runtimeAssembly.GetType("PeopleOfTheIliacBay.DFU.SplitPortraitManifestValidator", true);
            object manifest = SaveLoadManager.Deserialize(manifestType, File.ReadAllText(Path.Combine(
                splitPortraitTextureDirectory, Path.GetFileName(manifestAssetPath))));
            var resolveMethod = typeof(PeopleOfTheIliacBayBuild).GetMethod("ResolveSplitPortraitAsset",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var name = Expression.Parameter(typeof(string), "name");
            Type resolverType = typeof(Func<,>).MakeGenericType(typeof(string), splitPortraitDecodedAssetType);
            Delegate resolver = Expression.Lambda(resolverType,
                Expression.Convert(Expression.Call(resolveMethod, name), splitPortraitDecodedAssetType), name).Compile();
            var arguments = new object[] { manifest, resolver, null };
            bool valid = (bool)validatorType.GetMethod("TryValidate").Invoke(null, arguments);
            Require(valid, "Portrait runtime manifest and decoded textures validate: " + Convert.ToString(arguments[2]));

            Array portraits = (Array)manifestType.GetField("portraits").GetValue(manifest);
            Require(portraits != null && portraits.Length > 0, "Portrait registry contains entries");
            int expectedPortraitTextures = 0;
            foreach (object portrait in portraits)
            {
                foreach (object emotion in (Array)portrait.GetType().GetField("emotions").GetValue(portrait))
                {
                    expectedPortraitTextures += ((Array)emotion.GetType().GetField("eyes").GetValue(emotion)).Length;
                    expectedPortraitTextures += ((Array)emotion.GetType().GetField("mouths").GetValue(emotion)).Length;
                    Array gazes = (Array)emotion.GetType().GetField("gazes").GetValue(emotion);
                    if (gazes != null)
                        foreach (object gaze in gazes)
                            expectedPortraitTextures += ((Array)gaze.GetType().GetField("eyes").GetValue(gaze)).Length;
                }
            }
            Require(portraits.Length > 0 && splitPortraitResolvedAssets.Count == expectedPortraitTextures,
                "Every declared eye, mouth and gaze texture resolves exactly once");
            foreach (string path in files)
            {
                string fileName = Path.GetFileName(path);
                if (!path.Contains("/Textures/") || !fileName.StartsWith("portrait-", StringComparison.Ordinal) ||
                    !fileName.EndsWith(".png.bytes", StringComparison.Ordinal)) continue;
                Require(splitPortraitResolvedAssets.Contains(fileName), "No unreferenced portrait texture is listed: " + fileName);
                Require(CountPath(files, path) == 1, "Portrait texture appears exactly once in mod Files: " + path);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PORTRAIT_PACKAGE_PASS: entries=" + portraits.Length +
                "; textures=" + splitPortraitResolvedAssets.Count + "; hashes and dimensions verified.");
        }
        finally
        {
            splitPortraitPackageFiles = null;
            splitPortraitTextureDirectory = null;
            splitPortraitDecodedAssetType = null;
            splitPortraitResolvedAssets.Clear();
        }
    }

    private static object ResolveSplitPortraitAsset(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName) || Path.GetFileName(assetName) != assetName ||
            !assetName.EndsWith(".png.bytes", StringComparison.Ordinal) || splitPortraitPackageFiles == null ||
            CountPath(splitPortraitPackageFiles, "Assets/Game/Mods/PeopleOfTheIliacBay/Textures/" + assetName) != 1)
            return null;
        string path = Path.Combine(splitPortraitTextureDirectory, assetName);
        if (!File.Exists(path)) return null;
        byte[] bytes = File.ReadAllBytes(path);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
        try
        {
            if (!texture.LoadImage(bytes)) return null;
            Color32[] pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length != texture.width * texture.height) return null;
            byte[] rgba = new byte[pixels.Length * 4];
            int offset = 0;
            for (int topY = 0; topY < texture.height; topY++)
            {
                int unityY = texture.height - 1 - topY;
                for (int x = 0; x < texture.width; x++)
                {
                    Color32 pixel = pixels[unityY * texture.width + x];
                    rgba[offset++] = pixel.r; rgba[offset++] = pixel.g;
                    rgba[offset++] = pixel.b; rgba[offset++] = pixel.a;
                }
            }
            object decoded = Activator.CreateInstance(splitPortraitDecodedAssetType);
            splitPortraitDecodedAssetType.GetField("encodedBytes").SetValue(decoded, bytes);
            splitPortraitDecodedAssetType.GetField("rgbaSha256").SetValue(decoded, Sha256(rgba));
            splitPortraitDecodedAssetType.GetField("width").SetValue(decoded, texture.width);
            splitPortraitDecodedAssetType.GetField("height").SetValue(decoded, texture.height);
            splitPortraitResolvedAssets.Add(assetName);
            return decoded;
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    private static string Sha256(byte[] bytes)
    {
        using (SHA256 algorithm = SHA256.Create())
        {
            byte[] hash = algorithm.ComputeHash(bytes);
            var result = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash) result.Append(value.ToString("x2"));
            return result.ToString();
        }
    }

    private static int CountPath(string[] paths, string expected)
    {
        int count = 0;
        if (paths != null)
            foreach (string path in paths)
                if (string.Equals(path, expected, StringComparison.Ordinal)) count++;
        return count;
    }

    private static void VerifyPropertyKeyItemTemplate()
    {
        const string templatePath = "Assets/Game/Mods/PeopleOfTheIliacBay/ItemTemplates.json";
        var info = JsonUtility.FromJson<ModInfo>(File.ReadAllText("Assets/Game/Mods/PeopleOfTheIliacBay/PeopleOfTheIliacBay.dfmod.json"));
        Require(info.Files.Contains(templatePath), "Property key template is listed in mod assets");
        string json = File.ReadAllText(templatePath);
        Require(HouseholdPropertyKeyItem.TemplateIndex == 0x4C430001 &&
            HouseholdPropertyKeyItem.TemplateIndex > ItemHelper.LastDFTemplate &&
            json.Contains("\"index\": 1279459329") && json.Contains("\"name\": \"Property Key\"") &&
            json.Contains("\"playerTextureArchive\": 432") && json.Contains("\"playerTextureRecord\": 20"),
            "Property key template uses distinct custom item identity and Skeleton Key icon");
        Require(!json.Contains("\"artifactIndexBitfield\"") && !json.Contains("MagicItemTemplates"),
            "Property key is not an artifact or master key");

        // Batch editor verification does not start ModManager's player loader.
        // Import the packaged JSON into the native helper before constructing items.
        var templates = SaveLoadManager.Deserialize(typeof(List<ItemTemplate>), json) as List<ItemTemplate>;
        Require(templates != null && templates.Count == 1, "Property key JSON deserializes through native serializer");
        var templateField = typeof(ItemHelper).GetField("itemTemplates",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Require(templateField != null, "Native item template storage exists for editor fixture");
        var nativeTemplates = (List<ItemTemplate>)templateField.GetValue(DaggerfallUnity.Instance.ItemHelper);
        nativeTemplates.RemoveAll(t => t.index == HouseholdPropertyKeyItem.TemplateIndex);
        nativeTemplates.Add(templates[0]);
        HouseholdPropertyKeyItem.Register();
        Require(Array.IndexOf(DaggerfallUnity.Instance.ItemHelper.GetCustomItemsForGroup(ItemGroups.MiscItems),
            HouseholdPropertyKeyItem.TemplateIndex) < 0,
            "Property key is registered for direct creation, not random loot or shop stock");
        var key = (HouseholdPropertyKeyItem)ItemBuilder.CreateItem(ItemGroups.MiscItems, HouseholdPropertyKeyItem.TemplateIndex);
        key.enchantmentPoints = 1234567;
        key.message = 7654321;
        Require(key.GetType() == typeof(HouseholdPropertyKeyItem) && !key.IsArtifact &&
            key.ItemGroup == ItemGroups.MiscItems && key.value == 0 &&
            key.customMagic == null && key.legacyMagic == null,
            "Property key is a mundane custom misc item, not a priced artifact");
        Require(HouseholdPropertyKeyItem.IsMatching(key, 1234567, 7654321) &&
            !HouseholdPropertyKeyItem.IsMatching(key, 1234568, 7654321) &&
            !HouseholdPropertyKeyItem.IsMatching(key, 1234567, 7654322),
            "Property key requires exact settlement and building identity");

        var inventory = new ItemCollection();
        inventory.AddItem(key, noStack: true);
        string serialized = SaveLoadManager.Serialize(typeof(ItemData_v1[]), inventory.SerializeItems());
        var itemData = SaveLoadManager.Deserialize(typeof(ItemData_v1[]), serialized) as ItemData_v1[];
        Require(itemData != null && itemData.Length == 1 &&
            itemData[0].className == typeof(HouseholdPropertyKeyItem).ToString(),
            "Native item save records stable custom class name");
        var restored = new ItemCollection();
        restored.DeserializeItems(itemData);
        var restoredKey = restored.GetItem(0);
        Require(restoredKey != null && restoredKey.GetType() == typeof(HouseholdPropertyKeyItem) &&
            HouseholdPropertyKeyItem.IsMatching(restoredKey, 1234567, 7654321),
            "Native item serialization restores custom property key class and exact identity");
        Require(!key.IsStackable(), "Property keys cannot merge across property identities");
        var source = new ItemCollection(); var target = new ItemCollection();
        source.AddItem(key);
        int transferred = 0;
        var hookType = Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeItemTransferHooks, PeopleOfTheIliacBay.QuestHooks", true);
        using ((IDisposable)Activator.CreateInstance(hookType, new object[] {
            new Func<DaggerfallUnityItem,ItemCollection,ItemCollection,bool>((item,from,to) => ReferenceEquals(from,source)),
            new Func<DaggerfallUnityItem,ItemCollection,ItemCollection,bool>((item,from,to) => true),
            new Action<DaggerfallUnityItem,ItemCollection,ItemCollection>((item,from,to) => {
                Require(!from.Contains(item) && to.Contains(item), "Transfer callback runs only after actual movement");
                transferred++;
            }),
            new Action<DaggerfallUnityItem,ItemCollection,ItemCollection>((item,from,to) => {
                throw new InvalidOperationException("Accepted fixture transfer unexpectedly rolled back.");
            })
        }))
        {
            target.Transfer(key,source);
            Require(transferred == 1, "Individual corpse key transfer reports once");
            source.AddItem(new HouseholdPropertyKeyItem(1234567,7654322));
            source.AddItem(new HouseholdPropertyKeyItem(1234567,7654323));
            target.TransferAll(source);
            Require(transferred == 3 && source.Count == 0 && target.Count == 3,
                "Take-all reports each distinct property key without stacking");
        }
    }
    public static void VerifyActivitySight()
    {
        var target=GameObject.CreatePrimitive(PrimitiveType.Cube);
        var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            Vector3 origin=new Vector3(10000,10000,10000);
            target.transform.position=origin+Vector3.forward*3;
            blocker.transform.position=origin+Vector3.forward*1.5f;
            Physics.SyncTransforms();
            Require(!InteriorActivitySight.Visible(origin,target.transform.position,target.transform),
                "Activity anchors must reject a shelf hidden behind a wall");
            blocker.transform.position=origin+Vector3.forward*5;
            Physics.SyncTransforms();
            Require(InteriorActivitySight.Visible(origin,target.transform.position,target.transform),
                "A shelf's own collider is the intended visible target");
            blocker.transform.position=origin+Vector3.forward*1.5f;
            blocker.AddComponent<CrowdMotion>();
            Physics.SyncTransforms();
            Require(InteriorActivitySight.Visible(origin,target.transform.position,target.transform),
                "Transient pedestrians must not invalidate cached activity anchors");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTIVITY_SIGHT_PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(blocker); }
    }
    public static void VerifyActivityFacing()
    {
        var body = new GameObject("Activity facing regression");
        try
        {
            var actor = body.AddComponent<ResidentActor>();
            var person = body.AddComponent<MobilePersonNPC>();
            person.Asset = body.AddComponent<ChildHomeBillboard>();
            typeof(ResidentActor).GetProperty("Person").GetSetMethod(true).Invoke(actor, new object[] { person });
            for (int i = 0; i < 40; i++)
            {
                body.transform.position = Vector3.up + Vector3.right * (i % 2 == 0 ? 0.02f : -0.02f);
                actor.Place(Vector3.zero, Vector3.forward, true);
                Require(Vector3.Dot(body.transform.forward, Vector3.forward) > 0.999f,
                    "Small placement corrections must not turn a busy resident away from their activity");
            }
            body.transform.position = Vector3.up - Vector3.right * 0.1f;
            float beforeWalkingYaw=body.transform.eulerAngles.y;
            Vector3 beforeWalkingPosition=body.transform.position;
            actor.Place(Vector3.zero, Vector3.forward, false);
            var firstDisplacement=body.transform.position-beforeWalkingPosition;
            VerifyWalkingFacing(body.transform,beforeWalkingYaw,firstDisplacement,Vector3.forward,
                "Walking turn follows actual movement instead of the scheduled heading");
            // Exercise the real native animation state without loading scene textures.
            var native = body.AddComponent<MobilePersonBillboard>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var animations = new MobileAnimation[8];
            for (int i = 0; i < animations.Length; i++) animations[i] = new MobileAnimation { FramePerSecond = 4, NumFrames = 4 };
            typeof(MobilePersonBillboard).GetField("moveAnims", flags).SetValue(native, animations);
            typeof(MobilePersonBillboard).GetField("idleAnims", flags).SetValue(native, new[] { new MobileAnimation { FramePerSecond = 1, NumFrames = 1 } });
            person.Asset = native;
            body.transform.position = Vector3.up;
            actor.Place(Vector3.zero, Vector3.forward, true);
            Require(!native.IsIdle, "Busy adults must retain native directional records instead of forced frontal idle art");
            Require((float)typeof(MobilePersonBillboard).GetField("animSpeed", flags).GetValue(native) == 0,
                "Busy directional pose must not animate walking in place");
            Require((int)typeof(MobilePersonBillboard).GetField("currentFrame", flags).GetValue(native) == 1,
                "Standing must hold the inspected feet-together passing frame, not the wide-stride frame");
            body.transform.position = Vector3.up - Vector3.right * 0.1f;
            actor.Place(Vector3.zero, Vector3.right, false);
            Require((float)typeof(MobilePersonBillboard).GetField("animSpeed", flags).GetValue(native) == 4,
                "Leaving the activity must restore native walking animation");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                floor.transform.position = Vector3.down * 0.1f;
                floor.transform.localScale = new Vector3(10, 0.2f, 10);
                Physics.SyncTransforms();
                var crowd = body.AddComponent<CrowdMotion>(); crowd.Configure(1);
                typeof(ResidentActor).GetField("crowd", flags).SetValue(actor, crowd);
                actor.Place(Vector3.back * 2, Vector3.forward, false);
                float beforeApproachYaw=body.transform.eulerAngles.y;
                var before = body.transform.position;
                actor.Place(Vector3.zero, Vector3.left, true);
                var displacement = body.transform.position - before;
                Require(displacement.sqrMagnitude > 0.01f && Vector3.Distance(body.transform.position,Vector3.up)>0.15f,
                    "Approaching activity fixture must still be physically short of its scheduled stop");
                VerifyWalkingFacing(body.transform,beforeApproachYaw,displacement,Vector3.left,
                    "A standing scheduled browser still approaching its stop must smoothly face movement, not the future shelf target");
                var beforeRejectedReconstruction = body.transform.position;
                actor.Place(Vector3.right * 30, Vector3.right, false);
                Require(body.activeSelf && Vector3.Distance(body.transform.position,beforeRejectedReconstruction)<0.001f,
                    "An already placed body must remain visible when schedule reconstruction has no supported destination");
            }
            finally { UnityEngine.Object.DestroyImmediate(floor); }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTIVITY_FACING_PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(body); }
    }

    private static void VerifyWalkingFacing(Transform body,float beforeYaw,Vector3 displacement,Vector3 scheduledDirection,string message)
    {
        displacement.y=0;
        Require(displacement.sqrMagnitude>0.000001f,"Walking-facing fixture did not move");
        float movementYaw=Mathf.Atan2(displacement.x,displacement.z)*Mathf.Rad2Deg;
        float scheduledYaw=Quaternion.LookRotation(scheduledDirection).eulerAngles.y;
        bool smoothing=!GameManager.IsGamePaused;
        float expected=(float)WalkingHeading.StepDegrees(beforeYaw,movementYaw,8,1080,Time.unscaledDeltaTime,smoothing);
        float expectedScheduled=(float)WalkingHeading.StepDegrees(beforeYaw,scheduledYaw,8,1080,Time.unscaledDeltaTime,smoothing);
        float actual=body.eulerAngles.y;
        float responseSeparation=Mathf.Abs(Mathf.DeltaAngle(expected,expectedScheduled));
        Debug.Log(string.Format("PEOPLE_OF_THE_ILIAC_BAY_WALKING_FACING_SAMPLE: dt={0:F6}s movementYaw={1:F3} scheduledYaw={2:F3} expectedYaw={3:F3} expectedScheduledYaw={4:F3} actualYaw={5:F3} separation={6:F3}deg",
            Time.unscaledDeltaTime,movementYaw,scheduledYaw,expected,expectedScheduled,actual,responseSeparation));
        Require(Mathf.Abs(Mathf.DeltaAngle(expected,actual))<0.1f,message+" must use the damped response to actual displacement");
        if(responseSeparation>0.1f)
            Require(Mathf.Abs(Mathf.DeltaAngle(actual,expectedScheduled))>0.1f,message+" must not use the future scheduled target");
        else
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALKING_FACING_DISTINCTION_INCONCLUSIVE: editor timestep leaves movement and scheduled responses within tolerance; this sample validates output/cap only.");
        if(smoothing)
            Require(Mathf.Abs(Mathf.DeltaAngle(beforeYaw,actual))<=1080*Time.unscaledDeltaTime+0.1f,
                message+" must respect the bounded turn rate");
    }

    public static void VerifySaveState()
    {
        VerifySerialization();
        VerifyNpcProfileProgress();
        VerifyAdapter();
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SAVE_STATE_VERIFIED: FullSerializer, family migration and adapter lifecycle passed; no bundle installed.");
    }
    public static void VerifyBeds()
    {
        var meshReader = DaggerfallUnity.Instance.MeshReader;
        for (uint id = 41000; id <= 41002; id++)
        {
            ModelData model;
            Require(meshReader.GetModelData(id, out model), "Original bed mesh");
            Bounds mattress = BedGeometry.Mattress(model);
            Require(mattress.size.x > 2 && mattress.size.x < 2.2f && mattress.size.z > 1 && mattress.size.z < 1.6f,
                "Mattress extents exclude frame and pillow");
            if (id == 41002) Require(mattress.center.y < 0, "Canopy is not mattress");
            var record = new DFBlock.RmbBlock3dObjectRecord { ModelIdNum = id, XPos = 100, YPos = -200, ZPos = 300,
                YRotation = 512, ObjectType = 0 };
            var bed = BedGeometry.FromRecord(record, 7, model);
            Vector3 expected = new Vector3(2.5f, 5, 7.5f) + Quaternion.Euler(0, -90, 0) * mattress.center;
            Require(Vector3.Distance(bed.LocalCenter, expected) < 0.0001f && bed.RecordIndex == 7 && bed.ModelId == id,
                "Upstairs rotated bed follows original record transform");
            Require(Vector3.Dot(bed.LocalHeadDirection, Vector3.forward) > 0.999f &&
                Math.Abs(bed.Length - mattress.size.x) < 0.0001f, "Head direction and dimensions");
            record.ObjectType = 3; record.YPos = 200;
            bed = BedGeometry.FromRecord(record, 7, model);
            float bottom = float.PositiveInfinity;
            foreach (var vertex in model.Vertices) bottom = Math.Min(bottom, vertex.y);
            Require(Math.Abs(bed.LocalCenter.y - (5 - bottom + mattress.center.y)) < 0.0001f,
                "Prop bed bottom offset follows DFU layout");
        }
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BEDS_VERIFIED: original mattresses, canopy exclusion, rotation and prop offsets passed.");
    }
    private static void VerifySleepingAlignment()
    {
        var room = new GameObject("Sleep alignment room");
        var native = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var cameraObject = new GameObject("Sleep alignment camera");
        var texture = new Texture2D(8, 2);
        try
        {
            room.transform.rotation = Quaternion.Euler(0, 37, 0);
            cameraObject.tag = "MainCamera"; cameraObject.AddComponent<Camera>();
            var pose = native.AddComponent<SleepingPose>();
            var atlas = new SleepingArt.Atlas { Texture = texture, Views = new[] {
                new Rect(0,0,1,1), new Rect(0,0,1,1), new Rect(0,0,1,1), new Rect(0,0,1,1), new Rect(0,0,1,1) } };
            pose.BeginNative(room.transform, new BedAnchor { LocalCenter = Vector3.zero,
                LocalHeadDirection = Vector3.forward, Length = 2, Width = 1 }, atlas);
            var drawing = native.transform.Find("Sleeping blanket");
            foreach (var offset in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
            {
                cameraObject.transform.position = native.transform.position + offset * 2 + Vector3.up;
                pose.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
                Require(Mathf.Abs(Vector3.Dot(drawing.forward, Vector3.up)) > 0.999f &&
                    Vector3.Dot(drawing.up, native.transform.forward) > 0.999f,
                    "Sleeping composite stays horizontal along bed axis as camera circles");
                Require(Mathf.Abs(drawing.localScale.x/drawing.localScale.y-0.25f)<0.001f,
                    "Full-length sleeper preserves rotated source pixel proportions");
            }
            pose.Wake();
            Require(native.GetComponent<Renderer>().enabled && !drawing.gameObject.activeSelf,
                "Fixed sleeper restores native renderer on waking");
            texture.Resize(2,8);
            atlas.Views=new[] { new Rect(0,0,1,1) };
            pose.BeginNative(room.transform,new BedAnchor { LocalCenter=Vector3.zero,LocalHeadDirection=Vector3.forward,Length=2,Width=1 },atlas);
            pose.SendMessage("LateUpdate",SendMessageOptions.DontRequireReceiver);
            Require(Mathf.Abs(drawing.localScale.x/drawing.localScale.y-0.25f)<0.001f && pose.ViewIndex==0,
                "Single overhead drawing preserves source proportions and head orientation");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SLEEP_ALIGNMENT_PASS: fixed mattress plane and head axis from four camera positions");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(native); UnityEngine.Object.DestroyImmediate(room);
            UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    private static void VerifySleepingArt()
    {
        var sheets=new Dictionary<string,Texture2D>();
        var footprints=new Dictionary<string,Rect[]>();
        try
        {
            int checkedEntries=0;
            foreach(var entry in SleepArtCatalog.Entries)
            {
                Texture2D image;
                if(!sheets.TryGetValue(entry.File,out image))
                {
                    image=new Texture2D(2,2);
                    Require(image.LoadImage(File.ReadAllBytes("Assets/Game/Mods/PeopleOfTheIliacBay/Textures/"+entry.File)),"Catalogue PNG decode: "+entry.File);
                    sheets.Add(entry.File,image);
                    footprints.Add(entry.File,entry.Cell<0?new[]{SleepingArt.Footprint(image)}:SleepingArt.GridFootprints(image));
                }
                var rect=footprints[entry.File][entry.Cell<0?0:entry.Cell];
                float ratio=rect.height*image.height/(rect.width*image.width);
                Require(ratio>1.7f && ratio<3.2f,"Sleeper proportions: "+entry.Archive+"/"+entry.Record);
                if(entry.Cell>=0)
                {
                    float x=(entry.Cell%4)/4f,y=(1-entry.Cell/4)/2f;
                    Require(rect.xMin>x && rect.xMax<x+0.25f && rect.yMin>y && rect.yMax<y+0.5f,"Atlas cell stays separate: "+entry.Archive+"/"+entry.Record);
                }
                checkedEntries++;
            }
            Require(checkedEntries==326,"Explicit humanoid sleeping coverage count, including provider-specific appearances");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SLEEP_CATALOGUE_PASS: "+checkedEntries+" mapped source appearances; shared textures and separate overhead footprints.");
        }
        finally { foreach(var image in sheets.Values) UnityEngine.Object.DestroyImmediate(image); }
        var overhead=new Texture2D(2,2);
        try
        {
            Require(overhead.LoadImage(File.ReadAllBytes("Assets/Game/Mods/PeopleOfTheIliacBay/Textures/sleep-topdown-flat-182-0.png.bytes")),"Overhead sleeper PNG decode");
            var footprint=SleepingArt.Footprint(overhead);
            Require(footprint.height*overhead.height>footprint.width*overhead.width*2 &&
                Array.Exists(overhead.GetPixels32(),p=>p.a==0),"Single overhead sleeper is elongated and transparent");
        }
        finally { UnityEngine.Object.DestroyImmediate(overhead); }
        foreach (int archive in new[] { 385, 453 }) VerifySleepingAtlas(archive);
        foreach (int record in new[] { 0, 44 })
        {
            var texture = new Texture2D(2, 2);
            try
            {
                Require(texture.LoadImage(File.ReadAllBytes("Assets/Game/Mods/PeopleOfTheIliacBay/Textures/sleep-flat-182-" + record + ".png.bytes")), "Native sleeping atlas decode");
                Require(SleepingArt.FindViews(texture).Length == 5 && Array.Exists(texture.GetPixels32(), p => p.a == 0), "Native sleeper has five separated views and real transparency");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SLEEP_ART_VERIFIED: two transparent five-view atlases with distinct side/end footprints.");
    }
    private static void VerifySleepingAtlas(int archive)
    {
        var texture = new Texture2D(2, 2);
        try
        {
            Require(texture.LoadImage(File.ReadAllBytes("Assets/Game/Mods/PeopleOfTheIliacBay/Textures/sleep-mobile-" + archive + ".png.bytes")), "Sleeping PNG decode: " + archive);
            var views = SleepingArt.FindViews(texture);
            Require(views.Length == 5 && views[2].width > views[0].width && views[2].width > views[4].width,
                "Atlas has distinct broadside and foreshortened end views");
            for (int i = 1; i < views.Length; i++) Require(views[i].yMax < views[i - 1].yMin, "Atlas rows do not overlap");
            // The generated PNG has occasional 1/255 alpha outside the sprites.
            // DFU's billboard shader clips below 0.5; verify the rendered boundary.
            for (int x = 0; x < texture.width; x++)
                Require(texture.GetPixel(x, 0).a < 0.5f && texture.GetPixel(x, texture.height - 1).a < 0.5f, "Atlas top/bottom are clipped");
            for (int y = 0; y < texture.height; y++)
                Require(texture.GetPixel(0, y).a < 0.5f && texture.GetPixel(texture.width - 1, y).a < 0.5f, "Atlas sides are clipped");
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }
    public static void AuditFixtureHousing()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var reader = new ContentReader(Path.Combine(root, "GameFiles/arena2"));
        DFLocation location;
        Require(reader.GetLocation("Daggerfall", "Gothway Garden", out location), "Audit location");
        for (int y = 0; y < location.Exterior.ExteriorData.Height; y++)
        for (int x = 0; x < location.Exterior.ExteriorData.Width; x++)
        {
            string name = reader.BlockFileReader.CheckName(reader.MapFileReader.GetRmbBlockName(location, x, y));
            var block = reader.BlockFileReader.GetBlock(name);
            foreach (var summary in RMBLayout.GetBuildingData(block, x, y))
            {
                if (summary.BuildingType != DFLocation.BuildingTypes.Tavern && (summary.BuildingType < DFLocation.BuildingTypes.House1 || summary.BuildingType > DFLocation.BuildingTypes.House6)) continue;
                var profiles = HousingSurvey.Read(reader, location, new[] { summary }, null);
                if (profiles[0].BedCount < 3) continue;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSING_AUDIT: key=" + summary.buildingKey + "; type=" + summary.BuildingType +
                    "; block=" + name + "; model=" + summary.ModelID + "; beds=" + profiles[0].BedCount);
            }
        }
    }
    public static void VerifyHousing()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var reader = new ContentReader(Path.Combine(root, "GameFiles/arena2"));
        DFLocation location;
        Require(reader.GetLocation("Daggerfall", "Gothway Garden", out location), "Housing fixture location");
        bool found = false;
        for (int y = 0; y < location.Exterior.ExteriorData.Height; y++)
        for (int x = 0; x < location.Exterior.ExteriorData.Width; x++)
        {
            string name = reader.BlockFileReader.CheckName(reader.MapFileReader.GetRmbBlockName(location, x, y));
            if (name != "BANKAM02.RMB") continue;
            var block = reader.BlockFileReader.GetBlock(name);
            var summaries = RMBLayout.GetBuildingData(block, x, y);
            // Independent original-asset census records BANKAM02 homes 0..7 with one bed each.
            var homes = new List<BuildingSummary>();
            for (int i = 0; i < 8; i++) homes.Add(summaries[i]);
            var profiles = HousingSurvey.Read(reader, location, homes, null);
            Require(profiles.Count == 8, "Housing profile count");
            for (int i = 0; i < profiles.Count; i++)
                Require(profiles[i].BuildingKey == summaries[i].buildingKey && profiles[i].BedCount == 1 &&
                    profiles[i].FootprintArea == 0, "Original bank block home beds and unknown size fallback");
            homes.Reverse();
            var reordered = HousingSurvey.Read(reader, location, homes, null);
            Require(reordered[0].BuildingKey == profiles[0].BuildingKey, "Stable housing enumeration");
            found = true;
        }
        Require(found, "Independent census block exists in test town");
        Require(HousingSurvey.Footprint(new[] { new Vector3(-2, 8, -3), new Vector3(2, -4, 2) }) == 20,
            "House footprint uses XZ bounds in square metres");
        Require(HousingSurvey.Footprint(new Vector3[0]) == 0, "Missing footprint is unknown");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSING_VERIFIED: original bed census, building keys, order and footprint passed.");
    }
    private static void VerifyNpcProfileProgress()
    {
        var store=new WorldStateStore(42);
        var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
        var baseline=store.ReadNpcProfile(person.Id,NpcArchetype.Thief);
        Require(store.Snapshot().NpcActors.Count==0,"Seeded reads do not materialize save records");
        Require(store.AwardNpcSkillExperience(person.Id,NpcArchetype.Thief,13,1000,0) &&
            store.AwardNpcSkillExperience(person.Id,NpcArchetype.Thief,16,700,1),"Sparse actor experience mutations");
        string serialized=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
        var restored=new WorldStateStore(0);
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),serialized));
        Require(restored.Snapshot().Version==new WorldState().Version && restored.Snapshot().NpcActors.Count==1 &&
            restored.ReadNpcProgress(person.Id).Skills.Count==2 &&
            restored.ReadNpcSkill(person.Id,NpcArchetype.Thief,13)==store.ReadNpcSkill(person.Id,NpcArchetype.Thief,13) &&
            restored.ReadNpcProfile(person.Id,NpcArchetype.Thief).MaximumHealth==baseline.MaximumHealth,
            "Native actor profile/progression independent restore");
        Require(serialized==SaveLoadManager.Serialize(typeof(WorldState),restored.Snapshot()),"Native actor progression serializer round trip");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_PROFILE_SAVE_PASS: schema"+new WorldState().Version+"; sparse skill experience; deterministic base; native independent restore.");
    }

    private static void VerifySerialization()
    {
        var store = new WorldStateStore(uint.MaxValue);
        var town = store.GetOrCreate(100, new[] { 10, 20 });
        store.SetRoutines(new TownRoutineState { SettlementId = 100, Residents = new List<ResidentRoutine> {
            new ResidentRoutine { ResidentId = town.Households[0].Residents[0].Id, DestinationKey = 30, OpenMinute = 600, CloseMinute = 1000,
                Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 10.5, Z = 20 } } } } });
        store.CommitQuestBindings(store.PrepareQuestBindings(new[] { new QuestResidentBinding {
            QuestUid = long.MaxValue, QuestName = "_SERIALIZER_", PersonSymbol = "_recipient_", SettlementId = 100,
            HomeKey = 10, ResidentId = town.Households[0].Residents[0].Id, PersonName = "Anna Ashfield",
            HomeName = "The Ashfield Residence", RuleId = "serializer/permanent-resident/v1" } }));
        Population.MarkDead(town, town.Households[0].Residents[0].Id);
        string serialized = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        var restored = new WorldStateStore(0);
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), serialized));
        Require(serialized == SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot()), "FullSerializer round trip");
        Require(!restored.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0].IsAlive, "Death persistence");
        Require(restored.Snapshot().QuestBindings[0].QuestUid == long.MaxValue &&
            restored.Snapshot().QuestBindings[0].PersonName == "Anna Ashfield", "Published quest contract and largest native-serializable UID persistence");

        var profiles = new List<HomeProfile>();
        for (int key = 1; key <= 100; key++) profiles.Add(new HomeProfile(key, 5, 200));
        var familyTown = store.GetOrCreateFamilies(200, profiles);
        var family = familyTown.Households.Find(home => home.Residents.Count == 5);
        Require(family != null, "Five-person family fixture");
        family.Residents[0].IsAlive = false;
        serialized = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), serialized));
        Require(serialized == SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot()), "Family FullSerializer round trip");
        var savedFamily = restored.GetOrCreateFamilies(200, profiles).Households.Find(home => home.BuildingKey == family.BuildingKey);
        Require(savedFamily.Residents.Count == 5 && savedFamily.Residents[2].FatherId == family.Residents[0].Id &&
            savedFamily.Residents[2].AgeGroup == ResidentAgeGroup.Child && !savedFamily.Residents[0].IsAlive &&
            savedFamily.Residents[1].PartnerId == family.Residents[0].Id && savedFamily.SurnameSeed == family.SurnameSeed,
            "Persistent family roles and deceased parent");
        for (int version = 1; version <= 2; version++)
        {
            string legacy = "{\"Version\":" + version + ",\"WorldSeed\":42,\"Settlements\":[{\"SettlementId\":100,\"Households\":[{\"BuildingKey\":10,\"Residents\":[{\"Id\":\"legacy\",\"HomeKey\":10,\"AppearanceSeed\":3,\"IsAlive\":false}]}]}],\"Routines\":[]}";
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), legacy));
            var migrated = restored.Snapshot(); var home = migrated.Settlements[0].Households[0]; var resident = home.Residents[0];
            Require(migrated.Version == new WorldState().Version && migrated.QuestBindings.Count == 0 && resident.Id == "legacy" && resident.AppearanceSeed == 3 && !resident.IsAlive &&
                resident.AgeGroup == ResidentAgeGroup.Adult && resident.Sex == ResidentSex.Female && resident.PartnerId == null &&
                home.SurnameSeed == StableSeed.Combine(42, 100, 10, 0xfffffffe), "Legacy FullSerializer migration " + version);
        }
        string schemaThree = "{\"Version\":3,\"WorldSeed\":42,\"Settlements\":[{\"SettlementId\":100,\"Households\":[{\"BuildingKey\":10,\"SurnameSeed\":876,\"Residents\":[{\"Id\":\"legacy-family\",\"HomeKey\":10,\"AppearanceSeed\":3,\"Sex\":1,\"IsAlive\":false}]}]}],\"Routines\":[]}";
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), schemaThree));
        Require(restored.Snapshot().Version == new WorldState().Version && restored.Snapshot().QuestBindings.Count == 0 &&
            restored.Snapshot().Settlements[0].Households[0].SurnameSeed == 876 &&
            !restored.Snapshot().Settlements[0].Households[0].Residents[0].IsAlive, "Schema three without a binding field migrates without regeneration");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_QUEST_LEDGER_SERIALIZATION_PASS: committed contracts, largest native-serializable UID, deaths and schemas 1-3.");
    }
    private static void VerifyWitnessRedirect()
    {
        var person=new AlarmPerson {HasPosition=true,BuildingKey=7,Reaction=CivilianReaction.Flee};
        var route=CivilianEscape.Create(person,100,new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3 {X=3}},
            new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=12}},3,12);
        var redirected=route.RedirectExterior(124,new List<SavedRoutePoint>{new SavedRoutePoint {X=3},new SavedRoutePoint {X=3,Z=6}});
        var json=SaveLoadManager.Serialize(typeof(CivilianEscape),redirected);
        var restored=CivilianEscape.Copy((CivilianEscape)SaveLoadManager.Deserialize(typeof(CivilianEscape),json));
        restored.Advance(person,136);
        Require(Math.Abs(person.X-3)<.001 && Math.Abs(person.Z-3)<.001 && restored.ReportAt==148,
            "Redirected witness retains position timing and speed through native serialization");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WITNESS_REDIRECT_SAVE_PASS: native route redirect serialization.");
    }
    private static void VerifyLodging()
    {
        var store=new WorldStateStore(42); store.GetOrCreate(1,new int[0]);
        store.RegisterLodging(1,7,new[] { new LodgingRoom { Id="suite",Beds=new List<string> { "bed:0","bed:1" } } });
        Require(store.TryBookLodging(1,7,"paid-player","player",1,true,1,100,864100),"Player room reservation");
        var json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
        var restored=new WorldStateStore(0); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
        Require(restored.IsLodgingBedReserved(1,7,"bed:1",200,"traveler"),"Entire private room survives native serialization");
        Require(!restored.TryBookLodging(1,7,"traveler","visitor",2,false,1,200,300),"Paid player booking cannot be displaced");
        Require(restored.EarliestLodgingStart(1,7,1,200,86400)==864100,"Native booking checkout remains authoritative");
        var occupant=restored.RegisterNativePerson(1,7,42,"Inn Occupant",ResidentSex.Male,1,0,182,0);
        Require(restored.TryClaimLodgingBed(1,7,occupant.Id,"bed:0",864100),"Native occupant claims expired room");
        json=SaveLoadManager.Serialize(typeof(WorldState),restored.Snapshot());
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
        Require(restored.NativeLodgingBed(occupant.Id)=="bed:0" && restored.IsLodgingBedReserved(1,7,"bed:1",864101,"player"),
            "Native occupant bed and room exclusion survive serialization");
        Require(restored.AvailableLodgingRoom(1,7,1,864101,864200)==null,"Native occupied room is not offered");
        var expanded=restored.Snapshot().Lodgings[0].Rooms;
        expanded.Add(new LodgingRoom {Id="staff",NonRentable=true,Beds=new List<string>{"staff-bed"}});
        restored.RegisterLodging(1,7,expanded);
        json=SaveLoadManager.Serialize(typeof(WorldState),restored.Snapshot());
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
        Require(restored.Snapshot().Lodgings[0].Rooms.Find(r=>r.Id=="staff").NonRentable &&
            restored.AvailableLodgingRoom(1,7,1,864101,864200)==null,"Non-rental bedroom survives native serialization without becoming available");
        var legacy=restored.Snapshot(); legacy.Version=7; legacy.Lodgings=null;
        restored.Restore(legacy); Require(restored.Snapshot().Lodgings.Count==0,"Schema seven gains empty lodging ledger");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LODGING_SAVE_PASS: native paid-room serialization, protected player interval and schema-seven migration.");
    }
    private static void VerifyTravelers()
    {
        var precise=new WorldStateStore(42); precise.GetOrCreate(1,new int[0]);
        precise.RegisterLodging(1,7,new[]{new LodgingRoom { Id="room",Beds=new List<string>{"bed"} }});
        precise.RegisterTraveler(new TravelerPerson {Id="traveler:precise",Name="Calendar Guest",HomeSettlement=2,AppearanceSeed=12});
        precise.RegisterTravelerVisit(new TravelerVisit {Id="precise-visit",PersonId="traveler:precise",SettlementId=1,TavernKey=7,Nights=2,
            DesiredArrival=12597573702,CommuteSeconds=6990.40364950249,
            Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=30}}});
        precise.AdvanceTravelerAdmissions(1,12597581000,8);
        precise.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),precise.Snapshot())));
        Require(precise.Snapshot().TravelerVisits[0].Admission==TravelerAdmission.Admitted,"Real-calendar fractional traveler paid booking round trip");
        var store=new WorldStateStore(42); store.GetOrCreate(1,new int[0]);
        store.RegisterLodging(1,7,new[] { new LodgingRoom { Id="room",Beds=new List<string> { "bed" } } });
        store.RegisterTraveler(new TravelerPerson { Id="traveler:test",Name="Travel Test",HomeSettlement=2,AppearanceSeed=12 });
        store.RegisterTravelerVisit(new TravelerVisit { Id="visit",PersonId="traveler:test",SettlementId=1,TavernKey=7,Nights=2,
            DesiredArrival=43200,CommuteSeconds=600,Route=new List<SavedRoutePoint> { new SavedRoutePoint(),new SavedRoutePoint { X=30 } } });
        Require(store.TryAdmitTraveler("visit",43800),"Traveler admitted to paid room");
        store.RegisterTraveler(new TravelerPerson { Id="traveler:witness",Name="Travel Witness",HomeSettlement=3,AppearanceSeed=13 });
        store.RegisterTravelerVisit(new TravelerVisit { Id="witness-visit",PersonId="traveler:witness",SettlementId=1,TavernKey=7,Nights=2,
            DesiredArrival=43200,CommuteSeconds=600,Route=new List<SavedRoutePoint> { new SavedRoutePoint(),new SavedRoutePoint { X=30 } } });
        var incident=store.RecordViolence(1,"traveler:test",45000);
        store.LocateViolence(incident.Id,-1,2,0,3);
        Require(store.Witness(incident.Id,"traveler:witness",45000).DirectWitness,"Traveler witnesses native save fixture");
        store.KillTraveler("traveler:test",45000);
        store.ConfirmCorpse(incident.Id,45000);
        store.NoticeOutdoorCorpse(incident.Id,"traveler:witness",false,45001);
        string json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
        var restored=new WorldStateStore(0); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
        Require(restored.Snapshot().Version == new WorldState().Version && !restored.Snapshot().Travelers[0].IsAlive &&
            restored.Snapshot().TravelerVisits[0].Admission==TravelerAdmission.Admitted &&
            restored.IsLodgingBedReserved(1,7,"bed",50000,"player"),"Traveler native serialization preserves death and booking");
        Require(restored.Snapshot().AlarmIncidents[0].Corpse.Discovery.ObserverId=="traveler:witness" &&
            restored.Snapshot().AlarmIncidents[0].People[0].DirectWitness,"Traveler corpse and witness survive native serialization");
        var old=restored.Snapshot(); old.Version=8; old.Travelers=null; old.TravelerVisits=null; old.AlarmIncidents.Clear();
        restored.Restore(old); Require(restored.Snapshot().Travelers.Count==0 && restored.Snapshot().TravelerVisits.Count==0,"Schema eight gains empty travelers");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_SAVE_PASS: native schema-nine traveler identity, visit, death, admission and room ownership.");
    }
    private static void VerifyDefenseLedger()
    {
        var store=new WorldStateStore(42); store.GetOrCreate(1,new[] { 7 },1);
        var roster=store.GetOrCreateGuards(1,1,new[] { new SavedRoutePoint() });
        string guard=roster.Guards[0].Id;
        roster.DispatchDefense("response:12345","native:12345",12000000100,
            new Dictionary<string,List<SavedRoutePoint>> { [guard]=new List<SavedRoutePoint> {
                new SavedRoutePoint(),new SavedRoutePoint { X=1 } } },1,2.8,12);
        store.RegisterDefenseCombatant("native:12345",1,CombatOrigin.PassiveEnemy,12000000100);
        store.RecordDefenseUnprovokedAttack("native:12345",12000000105);
        store.RecordDefensePlayerHit("native:12345",12000000110,true);
        store.RecordDefenseWitness("native:12345",guard,true,12000000111);
        store.ConfirmDefenseDeath("native:12345",12000000120,7,1,2,3,true,300,100);
        store.BindDefenseCorpse("native:12345","loot:12345");
        Require(store.CompleteDefenseWelfare("native:12345",guard,12000000130,true,true,0.5).PotionGranted,"Defense gift fixture");
        string json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
        var restored=new WorldStateStore(0); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
        var record=restored.GetDefenseCombatant("native:12345");
        Require(record.Origin==CombatOrigin.PassiveEnemy && record.UnprovokedAttackAt==12000000105 &&
            record.Death.LootEntitled && record.Death.CorpseId=="loot:12345" && record.Death.WelfareExpiresAt==12000000420 &&
            record.Death.WelfareConsumed && record.Death.PotionGranted && record.Witnesses[0].ObserverId==guard &&
            !restored.CompleteDefenseWelfare(record.Id,guard,12000000131,true,true,0.5).Completed,"Native defense ledger roundtrip");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DEFENSE_LEDGER_SERIALIZATION_PASS: provenance, corpse entitlement, expiry and consumed gift survive native JSON.");
    }
    private static void VerifyIndoorNoisePersistence()
    {
        var store = new WorldStateStore(1);
        var town = store.GetOrCreate(1, new[] { 1, 2, 3 });
        var people = new List<Resident>();
        foreach (var home in town.Households) people.AddRange(home.Residents);
        var incident = store.RecordViolence(1, people[0].Id, 12000000100);
        store.LocateViolence(incident.Id, 1, 0, 0, 0);
        store.HearViolence(incident.Id, people[1].Id, 1, incident.StartedAt, true, 100, 8, 99);
        store.HearViolence(incident.Id, people[2].Id, 1, incident.StartedAt, true, 50, 2, 0);
        string json = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        var restored = new WorldStateStore(0);
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        Require(json == SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot()), "Native hearing/wake decisions round trip");
        Require(restored.HearViolence(incident.Id, people[1].Id, 1, incident.StartedAt + 1, true, 0, 0, 0) == null,
            "Failed native wake cannot reroll after reload");
        var awakened = restored.ActiveAlarm(people[2].Id, incident.StartedAt + 1).People[0];
        Require(awakened.Awakened && awakened.HeardViolence && !awakened.DirectWitness,
            "Native waking preserves hearing without attacker identification");
        // Exercise actual missing serialized fields, not only null in memory.
        var legacy = store.Snapshot(); legacy.AlarmIncidents[0].People.Clear(); legacy.AlarmIncidents[0].NoiseChecks.Clear();
        var parsed = FullSerializer.fsJsonParser.Parse(SaveLoadManager.Serialize(typeof(WorldState), legacy));
        parsed.AsDictionary["AlarmIncidents"].AsList[0].AsDictionary.Remove("NoiseChecks");
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), FullSerializer.fsJsonPrinter.CompressedJson(parsed)));
        Require(restored.Snapshot().AlarmIncidents[0].NoiseChecks.Count == 0 && restored.Snapshot().AlarmIncidents[0].People.Count == 0,
            "Legacy native save lacks invented hearing");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_NOISE_PERSISTENCE_PASS: failed/successful wake, no reroll, hearing/sight distinction and absent legacy field");
    }
    private static void VerifyHearingReportPersistence()
    {
        var store = new WorldStateStore(1);
        var town = store.GetOrCreate(1, new[] { 7, 8 }, 1);
        var victim = town.Households[0].Residents[0];
        var alarm = store.RecordViolence(1, victim.Id, 12000000100);
        store.LocateViolence(alarm.Id, 7, 0, 0, 0);
        var person = store.HearViolence(alarm.Id, town.Households[1].Residents[0].Id, 7, alarm.StartedAt, false, 0, 2, 0);
        person.HasPosition = true; person.BuildingKey = 7;
        person.Escape = CivilianEscape.Create(person, alarm.StartedAt,
            new List<GuardPoint3> { new GuardPoint3(), new GuardPoint3 { X = 2 } },
            new List<SavedRoutePoint> { new SavedRoutePoint { X = 2 }, new SavedRoutePoint() });
        Population.MarkDead(town, victim.Id); store.ConfirmCorpse(alarm.Id, alarm.StartedAt);
        store.RegisterUnwitnessedScene(alarm.Id);
        var roster = store.GetOrCreateGuards(1, 1, new[] { new SavedRoutePoint() });
        double time = person.Escape.ReportAt;
        store.AdvanceEscapes(1, time);
        Require(store.ReportHeardViolence(alarm.Id, person.ResidentId, time), "Native hearing report reaches posted guard");
        string json = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        var restored = new WorldStateStore(0);
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        var scene = restored.Snapshot().AlarmIncidents[0].Scene;
        Require(scene.Reported && !scene.PlayerLeft && !scene.Discovered && scene.ReporterId == person.ResidentId &&
            scene.ReportGuardId == roster.Guards[0].Id && Math.Abs(scene.ReportedAt-time) <= 0.001,
            "Native physical hearing report ownership survives before departure");
        Require(restored.ObserveCrimeScenes(1, 7, time+1).Count == 1,
            "Neutral native investigation begins without player departure");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HEARING_REPORT_PERSISTENCE_PASS: real contact, fractional flight time, neutral scene before departure and native reload");
    }
    public static void VerifyGuardPersistence()
    {
        VerifyRefugePersistence();
        VerifyRegionalNames();
        AuditFixtureHousing();
        VerifyCrimeScenePersistence();
        VerifyOutdoorCorpsePersistence();
        VerifyGuardHooks();
        VerifyHouseholdDoorHook();
        VerifyTheftHook();
        VerifyNativeServiceHook();
        VerifyTheftSight();
        VerifyEntryPersistence();
        VerifyConversationMemoryPersistence();
        VerifyNativePeoplePersistence();
        VerifyCivilianHitScopes();
        VerifyIndoorNoisePersistence();
        VerifyHearingReportPersistence();
        var store = new WorldStateStore(42);
        store.GetOrCreate(99, new[] { 1, 2 });
        var posts = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 10 },
            new SavedRoutePoint { X = 20 }, new SavedRoutePoint { X = 30 } };
        var roster = store.GetOrCreateGuards(99, 4, posts);
        var routes = new Dictionary<string, List<SavedRoutePoint>>();
        foreach (var guard in roster.Guards)
            routes.Add(guard.Id, new List<SavedRoutePoint> { new SavedRoutePoint { X = guard.Post.X }, new SavedRoutePoint { X = 100 } });
        roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 }, routes);
        var sent = roster.DispatchBuilding("native-theft", 73, 100, 2, 2, 12);
        Require(sent.Count == 2 && sent[0].Id == roster.Guards[3].Id, "Native nearest finite guard selection");
        roster.Kill(sent[0].Id, 310);
        Require(Math.Abs(roster.Position(sent[0].Id, 500).X - 65) < 0.001, "Native guard death location");
        string json = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        var restored = new WorldStateStore(0);
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        Require(json == SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot()), "Native garrison save round trip");
        var saved = restored.GetOrCreateGuards(99, 200, new List<SavedRoutePoint>());
        Require(saved.Reports[0].BuildingKey == 73 && saved.Reports[0].Entrance.X == 100 &&
            saved.BuildingApproaches.Count == 1 && saved.BuildingApproaches[0].Routes.Count == 4,
            "Native serializer preserves building destination and exterior approach routes");
        Require(saved.Guards.Count == 4 && saved.Dispatch("native-theft", 500, routes, 4, 2, 12).Count == 2,
            "Native reload cannot reroll or replenish a report");
        Require(saved.Recruit(310 + 7 * 86400 - 1) == 0 && saved.Recruit(310 + 7 * 86400) == 1 &&
            saved.Recruit(310 + 7 * 86400) == 0, "Native bounded recruitment");
        json = SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot());
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        Require(json == SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot()), "Recruitment and replacement lineage persist");
        var legacy = restored.Snapshot(); legacy.Version = 5; legacy.Garrisons = null;
        restored.Restore(legacy);
        Require(restored.Snapshot().Version == new WorldState().Version && restored.Snapshot().Garrisons.Count == 0 &&
            restored.Snapshot().Settlements.Count == 1, "Schema five garrison migration preserves residents");
        VerifyPatrolPairPlan();
        VerifyGuardPatrolRoster();
        VerifyGuardDefense();
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARDS_PERSISTENCE_PASS: finite identities, dispatch, timed death location, native save, recruitment and schema-five migration.");
    }
    private static void VerifyRegionalNames()
    {
        var names=new NameHelper();
        uint previous=DFRandom.Seed;
        try
        {
            foreach(var bank in new[] { NameHelper.BankTypes.Breton,NameHelper.BankTypes.Redguard,NameHelper.BankTypes.Nord })
            {
                var home=new Household { SurnameSeed=42 };
                home.Residents.Add(new Resident { AppearanceSeed=1,Sex=ResidentSex.Male });
                home.Residents.Add(new Resident { AppearanceSeed=2,Sex=ResidentSex.Female });
                RegionalIdentity.Initialize(home,2,(seed,sex)=>{
                    DFRandom.Seed=seed;
                    return names.FirstName(bank,sex==ResidentSex.Male ? Genders.Male : Genders.Female);
                },seed=>{ DFRandom.Seed=seed; return names.Surname(bank); });
                Require(!string.IsNullOrWhiteSpace(home.Surname),"Native regional family name "+bank);
                foreach(var resident in home.Residents)
                    Require(resident.FullName.EndsWith(" "+home.Surname) && resident.FullName.Length>home.Surname.Length+1,"Native regional full name "+bank);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REGIONAL_NAMES_PASS: "+bank+" "+home.Residents[0].FullName+" / "+home.Residents[1].FullName);
            }
        }
        finally { DFRandom.Seed=previous; }
    }
    private static void VerifyRefugePersistence()
    {
        var policyStore=new WorldStateStore(42);
        Require(policyStore.RestrictHauntingRoutines(1291010263,false,600),"Initial haunted routine policy");
        var policyJson=SaveLoadManager.Serialize(typeof(WorldState),policyStore.Snapshot());
        var policyRestored=new WorldStateStore(0);
        policyRestored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),policyJson));
        Require(policyRestored.RestrictHauntingRoutines(1291010263,true,1200),"Saved same-day haunting policy");
        Require(!policyRestored.RestrictHauntingRoutines(1291010263,true,86400),"Next-day curse resolution");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_DAY_SAVE_PASS: native world serialization retains daily curfew policy.");
        var roster=GuardRoster.Create(1291010263,42,2,new[] { new SavedRoutePoint() });
        var id=roster.Guards[0].Id;
        roster.TakeRefuge(id,GuardRefugeJourney.Create(256,
            new[] { new GuardPoint3(),new GuardPoint3 { X=10 } },
            new[] { new GuardPoint3(),new GuardPoint3 { Z=4 } },100,2,12),100);
        foreach (double now in new[] { 130d,172d,200d })
        {
            roster.Advance(now);
            string json=SaveLoadManager.Serialize(typeof(GuardRoster),roster);
            roster=GuardRoster.CopyValidated((GuardRoster)SaveLoadManager.Deserialize(typeof(GuardRoster),json));
            Require(roster.Guards[0].Refuge!=null && roster.Guards[0].Refuge.BuildingKey==256,"Saved refuge identity");
        }
        bool overlapRejected=false;
        try
        {
            roster.TakeRefuge(roster.Guards[1].Id,GuardRefugeJourney.Create(256,
                new[] { new GuardPoint3(),new GuardPoint3 { X=10 } },
                new[] { new GuardPoint3(),new GuardPoint3 { Z=4 } },200,2,12),200);
        }
        catch (ArgumentException) { overlapRejected=true; }
        Require(overlapRejected && roster.Guards[1].Duty==GuardDuty.Posted,"Serialized refuge reservation");
        roster.Guards[0].Refuge.Release(200);
        string released=SaveLoadManager.Serialize(typeof(GuardRoster),roster);
        roster=GuardRoster.CopyValidated((GuardRoster)SaveLoadManager.Deserialize(typeof(GuardRoster),released));
        Require(roster.Guards[0].Refuge.Evaluate(212).Phase==RefugePhase.Exiting,"Saved refuge exit");
        roster.Advance(284);
        Require(roster.Guards[0].Duty==GuardDuty.Posted && roster.Guards[0].Refuge==null,"Saved refuge return");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REFUGE_PERSISTENCE_PASS: native serializer preserves approach, entry, shelter and release.");
    }
    private static void VerifyPatrolPairPlan()
    {
        var leaderJoin = new List<SavedRoutePoint> { new SavedRoutePoint { X = -10 }, new SavedRoutePoint() };
        var followerJoin = new List<SavedRoutePoint> { new SavedRoutePoint { X = 2, Z = -20 }, new SavedRoutePoint { X = 2 } };
        var circuit = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 20 },
            new SavedRoutePoint { X = 20, Z = 20 }, new SavedRoutePoint { Z = 20 }, new SavedRoutePoint() };
        double offset = 12000000000;
        var plan = PatrolPairPlan.Create("leader", "follower", leaderJoin, followerJoin, circuit,
            offset + 100, offset + 1000, 2, 10, 2);
        string json = SaveLoadManager.Serialize(typeof(PatrolPairPlan), plan);
        var copy = PatrolPairPlan.CopyValidated((PatrolPairPlan)SaveLoadManager.Deserialize(typeof(PatrolPairPlan), json));
        Require(json == SaveLoadManager.Serialize(typeof(PatrolPairPlan), copy), "Native paired patrol round trip");
        Require(Math.Abs(copy.Position("follower", offset + 250.5).X - 12.1) < 0.000001,
            "Native fractional patrol phase");
        Require(Math.Abs(copy.Position("leader", offset + 1025).X + 5) < 0.000001,
            "Patrol dawn return must move rather than teleport");
        var route = copy.ReturnRoute("follower", offset + 350);
        Require(Math.Abs(route[0].Z - 12) < 0.000001 && route[route.Count - 1].Z == -20,
            "Patrol interruption retains actual position and post");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PATROL_PAIR_PLAN_PASS: shared join, spaced phase, native fractional save and physical return.");
    }
    private static void VerifyGuardDefense()
    {
        var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 7 }, 1);
        var roster = store.GetOrCreateGuards(1, 1, new[] { new SavedRoutePoint() });
        string id = roster.Guards[0].Id;
        roster.DispatchDefense("defense:probe", "native:987654", 10,
            new Dictionary<string,List<SavedRoutePoint>> { { id, new List<SavedRoutePoint> {
                new SavedRoutePoint(), new SavedRoutePoint { X = 10 } } } },1,2,12);
        roster.Engage(id,70); roster.ObserveEngagement(id,new SavedRoutePoint { X = 11 },0.5,71,true,new SavedRoutePoint { X = 12 });
        var restored = new WorldStateStore(1);
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
        var copy=restored.GetGuards(1);
        Require(copy.Reports[0].ThreatId=="native:987654" && copy.Guards[0].Duty==GuardDuty.Engaged &&
            copy.Guards[0].HealthFraction==0.5 && copy.Guards[0].EngagementPosition.X==11,
            "Native world save retains defensive enemy target, health and actual position");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_DEFENSE_SAVE_PASS: enemy target, engagement position and wounded health survive native world serialization.");
    }
    private static void VerifyGuardPatrolRoster()
    {
        var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 7 }, 1);
        var posts = new[] { new SavedRoutePoint { X = -10 }, new SavedRoutePoint { X = 2, Z = -20 } };
        var roster = store.GetOrCreateGuards(1, 2, posts);
        var circuit = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 20 },
            new SavedRoutePoint { X = 20, Z = 20 }, new SavedRoutePoint { Z = 20 }, new SavedRoutePoint() };
        roster.StartPatrol(PatrolPairPlan.Create(roster.Guards[0].Id, roster.Guards[1].Id,
            new List<SavedRoutePoint> { posts[0], new SavedRoutePoint() },
            new List<SavedRoutePoint> { posts[1], new SavedRoutePoint { X = 2 } }, circuit, 100, 1000, 2, 10, 2), 100);
        string json = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        store.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        roster = store.GetGuards(1); string id = roster.Guards[0].Id;
        Require(json == SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot()) &&
            Math.Abs(roster.Position(id, 250).X - 10) < 0.000001, "Native world patrol phase");
        roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
            new Dictionary<string, List<SavedRoutePoint>> { { id, new List<SavedRoutePoint> { posts[0], new SavedRoutePoint { X = 100 } } } });
        var sent = roster.DispatchBuilding("patrol-shop", 73, 250, 1, 2, 10);
        Require(sent.Count == 1 && sent[0].Route[0].X == 10 && sent[0].ArrivesAt == 900,
            "Native patrol dispatch must start at actual position");
        json = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        store.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        roster = store.GetGuards(1); roster.Advance(1200);
        Require(roster.Guards[0].Duty == GuardDuty.Returning && roster.Guards[0].ArrivesAt == 1750 &&
            roster.Position(id, 1750).X == -10 && roster.Guards.Count == 2, "Native return reaches post without creating guards");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_PATROL_ROSTER_PASS: world save, actual origin, stopped partner, timed response and physical post return.");
    }
    private static void VerifyOutdoorCorpsePersistence()
    {
        var store = new WorldStateStore(42); var town = store.GetOrCreate(99, new[] { 7, 8 }, 1);
        var victim = town.Households[0].Residents[0]; var witness = town.Households[1].Residents[0];
        var alarm = store.RecordViolence(99, victim.Id, 100); store.LocateViolence(alarm.Id, -1, 100, 0, 0);
        Population.MarkDead(town, victim.Id); store.ConfirmCorpse(alarm.Id, 100, -1, 100, 0, 0);
        store.NoticeOutdoorCorpse(alarm.Id, witness.Id, false, 150);
        store.ObserveOutdoorCorpse(alarm.Id, false, 160);
        var roster = store.GetOrCreateGuards(99, 1, new[] { new SavedRoutePoint() }); var guard = roster.Guards[0];
        var target = new SavedRoutePoint { X = 100 };
        roster.DispatchOutdoorInvestigation("outdoor:" + alarm.Id, 150,
            new Dictionary<string, List<SavedRoutePoint>> { { guard.Id, new List<SavedRoutePoint> { guard.Post, target } } },
            new List<SavedRoutePoint> { target, new SavedRoutePoint { X = 120 }, target }, 1, 2, 10);
        var expected = roster.Position(guard.Id, 700);
        string json = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        var restored = new WorldStateStore(0); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        Require(json == SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot()), "Native exterior scene round trip");
        var body = restored.Snapshot().AlarmIncidents[0].Corpse;
        // Observation/player departure is not a physical guard cleanup receipt.
        // Preserve the pending body and first witness until the runtime confirms removal.
        Require(!body.Cleared && body.ClearedAt == 0 && body.Discovery.SeenAt == 150 &&
            body.Discovery.ObserverId == witness.Id && !body.Discovery.PlayerPresent,
            "Native outdoor discovery retains pending body and first witness without inventing cleanup");
        var saved = restored.GetGuards(99);
        Require(saved.Position(guard.Id, 700).X == expected.X && saved.Guards[0].SearchUntil == 4250 &&
            saved.Reports[0].FirstArrivalAt == 650, "Native outdoor patrol retains position and deadline");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_SCENE_PERSISTENCE_PASS: observation, pending body and one-hour patrol survive native serialization.");
    }
    private static void VerifyCrimeScenePersistence()
    {
        var store = new WorldStateStore(42);
        var town = store.GetOrCreate(99, new[] { 7 }, 1);
        var victim = town.Households[0].Residents[0];
        var alarm = store.RecordViolence(99, victim.Id, 100);
        store.LocateViolence(alarm.Id, 7, 1, 2, 3);
        Population.MarkDead(town, victim.Id); store.RegisterUnwitnessedScene(alarm.Id); store.ConfirmCorpse(alarm.Id, 100, 7, 4, 5, 6);
        Require(store.ObserveCrimeScenes(99, 7, 10000).Count == 0, "Unseen crime stays undiscovered while player remains inside");
        Require(store.ObserveCrimeScenes(99, -1, 10001).Count == 1, "Departure queues discovery beyond alarm expiry");
        var roster = store.GetOrCreateGuards(99, 1, new List<SavedRoutePoint> { new SavedRoutePoint() });
        var guard = roster.Guards[0];
        roster.CacheBuildingApproach(7, new SavedRoutePoint { X = 100 }, new Dictionary<string, List<SavedRoutePoint>> {
            { guard.Id, new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 100 } } } });
        roster.DispatchBuilding("discovery:" + alarm.Id, 7, 10001, 1, 2, 10, true);
        double arrival = guard.ArrivesAt;
        store.DiscoverCrimeScene(alarm.Id, arrival);
        roster.Reports[0].Warning = new CrimeSceneWarning(); roster.Reports[0].Warning.Begin();
        roster.Reports[0].Warning.Advance(4, true, false);
        Require(store.ObserveCorpses(99, -1, arrival).Count == 1, "Guard arrival preserves corpse until inspection");
        roster.EnterBuilding(guard.Id, 7, arrival, new GuardPoint3 { X=4, Y=6, Z=6 });
        store.AdvanceCrimeSceneInspection(alarm.Id, guard.Id, 2, false, false, true, arrival+24);
        string json = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        var restored = new WorldStateStore(0); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        Require(json == SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot()), "Native scene chronology round trip");
        Require(!restored.Snapshot().AlarmIncidents[0].Corpse.Cleared &&
            restored.Snapshot().AlarmIncidents[0].Corpse.InspectionRemainingRealSeconds == 3 &&
            restored.Snapshot().AlarmIncidents[0].Corpse.InspectionGuardId == guard.Id,
            "Native inspection owner and remaining time persist");
        restored.AdvanceCrimeSceneInspection(alarm.Id, guard.Id, 3, false, false, true, arrival+60);
        Require(restored.Snapshot().AlarmIncidents[0].Corpse.Cleared, "Restored inspection completes cleanup");
        var body = restored.Snapshot().AlarmIncidents[0].Corpse;
        Require(body.HasPosition && body.BuildingKey == 7 && body.X == 4 && body.Y == 5 && body.Z == 6,
            "Native corpse retains actual death position independently of first assault");
        var scene = restored.Snapshot().AlarmIncidents[0].Scene;
        var saved = restored.GetGuards(99);
        Require(saved.Reports[0].Warning.Issued && saved.Reports[0].Warning.Remaining == 6,
            "Native scene warning preserves remaining unpaused seconds");
        Require(scene.DiscoveredAt == arrival && scene.Occupied(arrival + 3599) && !scene.Occupied(arrival + 3600) &&
            saved.Reports[0].Investigation && saved.Guards[0].SearchUntil == arrival + 3600, "Native one-hour occupation survives reload");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SCENE_PERSISTENCE_PASS: confirmed home death, departure, guard arrival and one-hour investigation survive native serialization.");
    }
    private static void VerifyCivilianHitScopes()
    {
        var info = JsonUtility.FromJson<ModInfo>(File.ReadAllText("Assets/Game/Mods/PeopleOfTheIliacBay/PeopleOfTheIliacBay.dfmod.json"));
        var mod = new Mod(); mod.ModInfo.Files = info.Files;
        var types = (Type[])typeof(Mod).GetMethod("GetTypesInEditor", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(mod, null);
        var type = Array.Find(types, t => t.FullName == "PeopleOfTheIliacBay.QuestHooks.NativeCivilianHooks");
        Require(type.GetConstructor(new[] { typeof(Func<Transform, Action<Exception>>) }) != null,
            "Civilian hook supports per-hit completion scopes");
        var outer = new GameObject("Outer hit"); var inner = new GameObject("Inner hit");
        var weapon = (WeaponManager)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(WeaponManager));
        int begun = 0, ended = 0; bool failBefore = false, failAfter = false;
        var failure = new InvalidOperationException("expected before-hit failure");
        Func<Transform> current = () => (Transform)type.GetProperty("CurrentTarget").GetValue(null, null);
        IDisposable hook = null;
        try
        {
            hook = (IDisposable)Activator.CreateInstance(type, new object[] { new Func<Transform, Action<Exception>>(target => {
                begun++;
                Require(current() == target, "Prefix exposes current hit target");
                if (failBefore) throw failure;
                if (target == outer.transform)
                {
                    weapon.WeaponDamage(null, false, false, inner.transform, Vector3.zero, Vector3.forward);
                    Require(current() == target, "Nested hit restores outer scope");
                }
                return error => {
                    ended++;
                    if (failAfter) throw failure;
                    Require(current() == target, "Completion retains its own target");
                    Require((target == null) == (error != null), "Completion observes original native failure");
                };
            }) });
            weapon.WeaponDamage(null, false, false, outer.transform, Vector3.zero, Vector3.forward);
            Require(begun == 2 && ended == 2 && current() == null, "Nested successful hits complete once");
            bool threw = false;
            try { weapon.WeaponDamage(null, false, false, null, Vector3.zero, Vector3.forward); }
            catch (NullReferenceException) { threw = true; }
            Require(threw && begun == 3 && ended == 3 && current() == null, "Native failure propagates and restores scope");
            failBefore = true; threw = false;
            try { weapon.WeaponDamage(null, false, false, outer.transform, Vector3.zero, Vector3.forward); }
            catch (InvalidOperationException ex) { threw = ReferenceEquals(ex, failure); }
            Require(threw && ended == 3 && current() == null, "Prefix failure also restores scope");
            failBefore = false; failAfter = true; threw = false;
            try { weapon.WeaponDamage(null, false, false, inner.transform, Vector3.zero, Vector3.forward); }
            catch (InvalidOperationException ex) { threw = ReferenceEquals(ex, failure); }
            Require(threw && ended == 4 && current() == null, "Completion failure restores scope and propagates");
            threw = false;
            try { weapon.WeaponDamage(null, false, false, null, Vector3.zero, Vector3.forward); }
            catch (NullReferenceException) { threw = true; }
            Require(threw && ended == 5 && current() == null, "Completion cannot replace original native exception");
            hook.Dispose(); hook = null;
            int legacy = 0;
            hook = (IDisposable)Activator.CreateInstance(type, new object[] { new Action<Transform>(target => legacy++) });
            weapon.WeaponDamage(null, false, false, inner.transform, Vector3.zero, Vector3.forward);
            Require(legacy == 1 && current() == null, "Existing before-hit constructor remains compatible");
        }
        finally { if (hook != null) hook.Dispose(); UnityEngine.Object.DestroyImmediate(outer); UnityEngine.Object.DestroyImmediate(inner); }
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CIVILIAN_SCOPES_PASS: actual patched native method, nested hits, native and prefix failures.");
    }
    private static void VerifyEntryPersistence()
    {
        var store = new WorldStateStore(42);
        var town = store.GetOrCreate(99, new[] { 1, 2 }, 1);
        var citizen = town.Households[1].Residents[0].Id;
        var roster = store.GetOrCreateGuards(99, 1, new List<SavedRoutePoint> { new SavedRoutePoint() });
        var report = store.RecordEntry(99, 1, 12597431462.259386, new[] { citizen }, new string[0]);
        store.PlanEntryMessenger(report.Id, citizen, roster.Guards[0].Id, 12597431463,
            new List<SavedRoutePoint> { new SavedRoutePoint { X = 28 }, new SavedRoutePoint() });
        var json = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        var restored = new WorldStateStore(0);
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        Require(restored.AdvanceEntryMessengers(99, 12597431482.9).Count == 0, "Messenger cannot report before arrival");
        Require(restored.AdvanceEntryMessengers(99, 12597431483).Count == 1, "Messenger reports at saved arrival time");
        var copied = restored.Snapshot().EntryReports[0];
        Require(copied.Id == report.Id && copied.EnteredAt == 12597431462 && copied.InformedGuards.Count == 1,
            "Entry evidence survives native epoch rounding and later guard notification");
        Require(copied.Messenger.Delivered && copied.Messenger.ArrivesAt == 12597431483,
            "Messenger route and arrival survive native epoch rounding");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTRY_PERSISTENCE_PASS: native saved witness route and timed delivery.");
    }
    private static void VerifyConversationMemoryPersistence()
    {
        var store=new WorldStateStore(42);
        store.RememberConversation("npc-test",12597431462.259386,"6 Morning Star 405","Black Wolf Inn",
            "My sister is named Mirabelle.","I will remember that.","model");
        store.RememberObservation("npc-test",12597431462,"6 Morning Star 405","Black Wolf Inn","Met the player here.");
        var json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
        var restored=new WorldStateStore(7);
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
        Require(restored.RecallConversation("npc-test","sister",12597517862,2000).Contains("Mirabelle"),"NPC memory native save recall");
        Require(json==SaveLoadManager.Serialize(typeof(WorldState),restored.Snapshot()),"NPC memory native serializer roundtrip");
        Require(!restored.RecallConversation("other","sister",12597517862,2000).Contains("Mirabelle"),"NPC memory private identity");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_PERSISTENCE_PASS: dated NPC exchanges and observations survive native serializer.");
    }
    private static void VerifyNativePeoplePersistence()
    {
        var store = new WorldStateStore(42); store.GetOrCreate(99, new[] { 1 }, 1);
        var person = store.RegisterNativePerson(99, 2, -123, "Ysausa Masterham", ResidentSex.Female, 1, 41, 184, 18);
        Require(store.AssignNativeBed(person.Id, new HomeProfile(2, 1, 10), new HomeProfile[0]), "Keeper reserves on-site bed");
        var family = store.CreateKeeperHousehold(person.Id, new HomeProfile(2, 1, 10));
        string json = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        var restored = new WorldStateStore(0);
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        var saved = restored.NativePeopleFor(99)[0];
        var savedHome = restored.GetOrCreate(99, new[] { 1 }).Households.Find(h => h.HeadId == person.Id);
        Require(savedHome != null && savedHome.Surname == "Masterham" && savedHome.Residents.Count == 1 &&
            savedHome.Residents[0].FullName == person.Name && savedHome.Residents[0].Race == person.Race &&
            object.ReferenceEquals(savedHome.Residents[0].NativeIdentity, saved), "Native household head metadata and lifetime binding survive FullSerializer");
        Require(!json.Contains("NativeIdentity"), "Live native identity binding is not serialized twice");
        Require(saved.Id == person.Id && saved.Name == person.Name && saved.Sex == ResidentSex.Female &&
            saved.TextureArchive == 184 && saved.TextureRecord == 18 && saved.HomeKey == 2 && saved.BedSlot == 0 && saved.BedCapacity == 1,
            "Native identity, appearance and bed reservation survive FullSerializer");
        Require(restored.RegisterNativePerson(99, 2, -123, "Temporary quest label", ResidentSex.Female, 1, 41, 184, 18).Name == person.Name,
            "Repeated registration preserves saved native name");
        var workday = DailySchedule.Workday(saved.HomeKey, saved.WorkKey, new RoutePoint[0], 480, 1140, 0, 420, 1320);
        Require(workday.EvaluateSeconds(480 * 60 - 0.1).Activity == Activity.Home &&
            workday.EvaluateSeconds(480 * 60).Activity == Activity.Work &&
            workday.EvaluateSeconds(1140 * 60).Activity == Activity.Home &&
            workday.EvaluateSeconds(1320 * 60).Activity == Activity.Sleep &&
            workday.EvaluateSeconds(86400 * 1000d + 480 * 60).BuildingKey == saved.WorkKey,
            "On-site native worker schedule has no invented commute and exact business/sleep boundaries");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ONSITE_SCHEDULE_PASS: zero commute; exact opening, closing, sleep and later-day reconstruction.");
        var alarm = restored.RecordViolence(99, saved.Id, 100);
        restored.LocateViolence(alarm.Id, 2, 1, 2, 3);
        Require(Population.MarkDead(restored.GetOrCreate(99, new[] { 1 }), saved.Id), "Native head death updates both ledgers");
        restored.ConfirmCorpse(alarm.Id, 101, 2, 1, 2, 3); restored.RegisterUnwitnessedScene(alarm.Id);
        restored.ObserveCrimeScenes(99, -1, 102);
        json = SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot());
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        Require(!restored.NativePeopleFor(99)[0].IsAlive && restored.ObserveCrimeScenes(99, -1, 103).Count == 1 &&
            restored.ObserveCorpses(99, 2, 104).Count == 1, "Native head death, corpse and undiscovered scene survive FullSerializer");
        var staff = restored.RegisterNativePerson(99, 3, 11, "Staff Kinghart", ResidentSex.Male, 1, 41, 182, 1);
        var staffAlarm = restored.RecordViolence(99, staff.Id, 105);
        restored.LocateViolence(staffAlarm.Id, 3, 2, 3, 4); staff.IsAlive = false;
        restored.ConfirmCorpse(staffAlarm.Id, 106, 3, 2, 3, 4); restored.RegisterUnwitnessedScene(staffAlarm.Id);
        json = SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot());
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        Require(!restored.NativePeopleFor(99)[1].IsAlive && restored.ObserveCorpses(99, 3, 107).Count == 2,
            "Unlinked native staff death and work-site scene survive FullSerializer");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_DEATH_PERSISTENCE_PASS: household head and unlinked staff; native serializer; corpse and work-site scene.");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_PEOPLE_PERSISTENCE_PASS: named native identity, appearance and single-bed reservation.");
    }
    private static void VerifyTheftSight()
    {
        var witness = new GameObject("Theft sight witness");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            witness.transform.position = new Vector3(0, 10000, 0);
            var target = witness.transform.position + Vector3.right * 5 + Vector3.up;
            wall.transform.position = witness.transform.position + new Vector3(2.5f, 1, 0);
            wall.transform.localScale = new Vector3(1, 4, 4);
            wall.SetActive(false); Physics.SyncTransforms();
            Require(TheftWitnessSight.CanSee(witness.transform, target, true, true), "Living nearby witness can see theft");
            Require(!TheftWitnessSight.CanSee(witness.transform, target, false, true), "Dead witness never sees theft");
            Require(!TheftWitnessSight.CanSee(witness.transform, target, true, false), "Unaware sleeper never sees theft");
            Require(!TheftWitnessSight.CanSee(witness.transform, target + Vector3.right*30, true, true), "Distant NPC cannot witness theft");
            wall.SetActive(true); Physics.SyncTransforms();
            Require(!TheftWitnessSight.CanSee(witness.transform, target, true, true), "Wall blocks theft sight");
        }
        finally { UnityEngine.Object.DestroyImmediate(wall); UnityEngine.Object.DestroyImmediate(witness); }
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_THEFT_SIGHT_PASS: range, walls, death and sleeping.");
    }
    private static void VerifyTheftHook()
    {
        var info = JsonUtility.FromJson<ModInfo>(File.ReadAllText("Assets/Game/Mods/PeopleOfTheIliacBay/PeopleOfTheIliacBay.dfmod.json"));
        var mod = new Mod(); mod.ModInfo.Files = info.Files;
        var types = (Type[])typeof(Mod).GetMethod("GetTypesInEditor", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(mod, null);
        var type = Array.Find(types, t => t.FullName == "PeopleOfTheIliacBay.QuestHooks.NativeTheftHooks");
        Require(type != null, "Theft witness hook is loadable");
        var player = new PlayerEntity(null);
        int normal = DaggerfallWorkshop.Game.Formulas.FormulaHelper.CalculateShopliftingChance(player, 3, 2);
        foreach (bool? observed in new bool?[] { true, null })
            using ((IDisposable)Activator.CreateInstance(type, new object[] { new Func<bool?>(() => observed) }))
                Require(DaggerfallWorkshop.Game.Formulas.FormulaHelper.CalculateShopliftingChance(player, 3, 2) == normal,
                    "Witnessed or unknown theft retains native skill calculation");
        using ((IDisposable)Activator.CreateInstance(type, new object[] { new Func<bool?>(() => false) }))
        {
            int chance = DaggerfallWorkshop.Game.Formulas.FormulaHelper.CalculateShopliftingChance(null, 100, 100);
            Require(chance == 0, "Confirmed unobserved theft has zero detection chance");
            for (int i = 0; i < 1000; i++) Require(Dice100.FailedRoll(chance), "Zero detection always passes native theft roll");
        }
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_THEFT_HOOK_PASS: zero native detection for confirmed absence of witnesses.");
    }
    private static void VerifyNativeServiceHook()
    {
        var info = JsonUtility.FromJson<ModInfo>(File.ReadAllText("Assets/Game/Mods/PeopleOfTheIliacBay/PeopleOfTheIliacBay.dfmod.json"));
        var mod = new Mod(); mod.ModInfo.Files = info.Files;
        var types = (Type[])typeof(Mod).GetMethod("GetTypesInEditor", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(mod, null);
        var type = Array.Find(types, t => t.FullName == "PeopleOfTheIliacBay.QuestHooks.NativeServiceHooks");
        Require(type != null, "Native service response hook is loadable");
        var root = new GameObject("Native service hook probe");
        try
        {
            var talk = root.AddComponent<TalkManager>();
            var npc = root.AddComponent<StaticNPC>();
            int calls = 0;
            using ((IDisposable)Activator.CreateInstance(type, new object[] { new Func<StaticNPC, bool>(n => { calls++; return n == npc; }) }))
            {
                talk.TalkToStaticNPC(npc);
                typeof(PlayerActivate).GetMethod("StaticNPCClick", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).Invoke(root.AddComponent<PlayerActivate>(), new object[] { npc });
                Require(calls == 2, "Response blocks direct dialogue and native service activation");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_SERVICE_HOOK_PASS");
    }
    private static void VerifyHouseholdDoorHook()
    {
        var info = JsonUtility.FromJson<ModInfo>(File.ReadAllText("Assets/Game/Mods/PeopleOfTheIliacBay/PeopleOfTheIliacBay.dfmod.json"));
        var mod = new Mod(); mod.ModInfo.Files = info.Files;
        var types = (Type[])typeof(Mod).GetMethod("GetTypesInEditor", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(mod, null);
        var type = Array.Find(types, t => t.FullName == "PeopleOfTheIliacBay.QuestHooks.NativeHouseholdDoorHooks");
        Require(type != null, "Household door hook is loadable");
        var player = (PlayerActivate)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PlayerActivate));
        bool unlocked = false; int calls = 0;
        using ((IDisposable)Activator.CreateInstance(type, new object[] {
            new Func<BuildingSummary, bool?>(summary => {
                Require(summary.buildingKey == 42, "Door hook preserves building identity");
                calls++; return unlocked;
            }), new Func<int, string>(key => null) }))
        {
            var building = new BuildingSummary { buildingKey = 42 };
            Require(!player.BuildingIsUnlocked(building), "Absent household locks native door query");
            unlocked = true;
            Require(player.BuildingIsUnlocked(building), "Awake owner unlocks native door query");
            Require(calls == 2, "Every native door query reevaluates occupancy");
        }
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_DOOR_HOOK_PASS: actual patched native lock query.");
    }
    private static void VerifyGuardHooks()
    {
        var info = JsonUtility.FromJson<ModInfo>(File.ReadAllText("Assets/Game/Mods/PeopleOfTheIliacBay/PeopleOfTheIliacBay.dfmod.json"));
        var mod = new Mod(); mod.ModInfo.Files = info.Files;
        var types = (Type[])typeof(Mod).GetMethod("GetTypesInEditor", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(mod, null);
        var type = Array.Find(types, t => t.FullName == "PeopleOfTheIliacBay.QuestHooks.NativeGuardHooks");
        Require(type != null, "Production guard hooks are loadable");
        bool owns = true; int immediate = 0, delayed = 0, singles = 0;
        var actor = new GameObject("Guard hook contract actor");
        IDisposable hook = null;
        try
        {
            hook = (IDisposable)Activator.CreateInstance(type, new object[] {
                new Func<bool>(() => owns), new Action<bool>(value => { if (value) immediate++; else delayed++; }),
                new Func<Vector3, Vector3, GameObject>((position, direction) => {
                    Require(position == Vector3.one && direction == Vector3.forward, "Guard position and direction preserved");
                    singles++; return actor;
                }) });
            var player = (PlayerEntity)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PlayerEntity));
            player.SpawnCityGuards(true); player.SpawnCityGuards(false);
            var conversion = type.GetMethod("BeforePoolConversion", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Require(conversion != null && !(bool)conversion.Invoke(null, null), "Owned towns suppress automatic pooled guard conversion");
            Require(player.SpawnCityGuard(Vector3.one, Vector3.forward) == actor && singles == 1 && immediate == 1 && delayed == 1,
                "Native spawn entry points route to owned callbacks");
            owns = false;
            Require((bool)conversion.Invoke(null, null), "Unsupported towns retain pooled guard conversion");
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            Require((bool)type.GetMethod("BeforeBatch", flags).Invoke(null, new object[] { true }), "Unsupported town keeps native batch spawning");
            var args = new object[] { Vector3.one, Vector3.forward, null };
            Require((bool)type.GetMethod("BeforeSingle", flags).Invoke(null, args) && args[2] == null && singles == 1,
                "Unsupported town keeps native single spawning");
            hook.Dispose(); hook = null;
            Require((bool)type.GetMethod("BeforeBatch", flags).Invoke(null, new object[] { true }), "Disposed hooks are inactive");
            owns = true;
            hook = (IDisposable)Activator.CreateInstance(type, new object[] { new Func<bool>(() => owns),
                new Action<bool>(value => { }), new Func<Vector3, Vector3, GameObject>((position, direction) => null) });
            bool rejected = false;
            try { player.SpawnCityGuard(Vector3.one, Vector3.forward); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Owned single spawning cannot silently return null or fall back to infinite native spawning");
            hook.Dispose(); hook = null;
        }
        finally { if (hook != null) hook.Dispose(); UnityEngine.Object.DestroyImmediate(actor); }
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_HOOKS_PASS: native immediate/deferred interception, exact actor return, unsupported-town fallback and disposal.");
    }
    private static void VerifyAlarmSerialization()
    {
        var store = new WorldStateStore(42);
        var town = store.GetOrCreate(99, new[] { 1, 2, 3, 4, 5 });
        var people = new List<Resident>();
        foreach (var home in town.Households) people.AddRange(home.Residents);
        var alarm = store.RecordViolence(99, people[0].Id, 100);
        store.Witness(alarm.Id, people[1].Id, 100);
        Require(store.Warn(alarm.Id, people[1].Id, people[2].Id, 101), "First warning");
        Require(store.Warn(alarm.Id, people[1].Id, people[3].Id, 102), "Second warning");
        string json = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        store.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
        Require(json == SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot()), "Native alarm ledger round trip");
        Require(!store.Warn(alarm.Id, people[1].Id, people[4].Id, 103), "Warning budget survives native restore");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ALARM_SERIALIZATION_PASS: identities, warnings, reaction and expiry survive native serialization.");
    }
    private static void VerifySettings()
    {
        string json = File.ReadAllText("Assets/Game/Mods/PeopleOfTheIliacBay/modsettings.json");
        var data = (ModSettingsData)SaveLoadManager.Deserialize(typeof(ModSettingsData), json);
        Require(data.GetValue<bool>("General", "Enabled"), "Simulation toggle default");
        Require(data.GetValue<bool>("General", "CivilianReactions"), "Civilian reactions default");
        Require(data.GetValue<int>("DailyRoutine", "WakeHour") == 6, "Wake slider default");
        Require(data.GetValue<int>("DailyRoutine", "SleepHour") == 22, "Sleep slider default");
        Require(data.GetValue<bool>("DailyRoutine", "SleepingPoses"), "Sleeping poses default");
        Require(data.GetValue<int>("Diagnostics", "RefreshSeconds") == 1, "Refresh slider default");
        Require(data.GetValue<bool>("Diagnostics", "EnableF8Panel"), "Panel toggle default");
        Require(data.GetValue<bool>("General", "StreetPopulation"), "Street population default");
        Require(data.GetValue<bool>("General", "QuestHouseholds"), "Quest household pilot default");
        Require(data.GetValue<bool>("General", "ExperimentalExteriorGuards"), "Finite guards default on for supported-town gameplay");
        Require(data.GetValue<int>("Guards", "MaximumResponders") == 4, "Guard response limit default");
        Require(data.GetValue<int>("Guards", "ActorRadius") == 160, "Guard actor radius default");
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SETTINGS_VERIFIED: twelve native menu controls deserialize with expected types and defaults.");
    }
    private static void VerifyAdapter()
    {
        var go = new GameObject("People of the Iliac Bay integration test");
        PeopleOfTheIliacBayMod adapter = null;
        System.Reflection.MethodInfo unregisterLifecycle = null;
        bool fixtureRegisteredLifecycle = false;
        try
        {
            adapter = go.AddComponent<PeopleOfTheIliacBayMod>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var registerLifecycle = typeof(PeopleOfTheIliacBayMod).GetMethod("RegisterConversationContextLifecycle", flags);
            unregisterLifecycle = typeof(PeopleOfTheIliacBayMod).GetMethod("UnregisterConversationContextLifecycle", flags);
            var threadField = typeof(PeopleOfTheIliacBayMod).GetField("conversationContextMainThreadId", flags);
            var epochField = typeof(PeopleOfTheIliacBayMod).GetField("conversationSaveEpoch", flags);
            Require(registerLifecycle != null && unregisterLifecycle != null && threadField != null && epochField != null,
                "Conversation context lifecycle fixture bindings");

            int currentThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            int registeredThread = (int)threadField.GetValue(adapter);
            Require(registeredThread == 0 || registeredThread == currentThread,
                "Editor fixture lifecycle is not registered to another thread");
            if (registeredThread == 0)
            {
                registerLifecycle.Invoke(adapter, null);
                fixtureRegisteredLifecycle = true;
            }
            Require((int)threadField.GetValue(adapter) == currentThread,
                "Conversation context lifecycle is explicitly bound to editor main thread");

            long epoch = (long)epochField.GetValue(adapter);
            adapter.RestoreSaveData(adapter.NewSaveData());
            Require((long)epochField.GetValue(adapter) == epoch + 1, "Initial adapter restore advances save epoch");
            var saved = (WorldState)adapter.GetSaveData();
            Require(saved.Version == new WorldState().Version && saved.Settlements.Count == 0 && saved.Routines.Count == 0 && saved.QuestBindings.Count == 0, "New world state");
            bool rejected = false;
            epoch = (long)epochField.GetValue(adapter);
            try { adapter.RestoreSaveData(new WorldState { Version = 999 }); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Invalid adapter restore rejection");
            Require((long)epochField.GetValue(adapter) == epoch + 1, "Failed adapter restore still advances save epoch");
            rejected = false;
            try { adapter.GetSaveData(); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Failed restore must prevent overwriting save state");
            epoch = (long)epochField.GetValue(adapter);
            adapter.RestoreSaveData(saved);
            Require((long)epochField.GetValue(adapter) == epoch + 1, "Recovery adapter restore advances save epoch");
            Require(((WorldState)adapter.GetSaveData()).WorldSeed == saved.WorldSeed, "Recovery through valid restore");
        }
        finally
        {
            if (fixtureRegisteredLifecycle && adapter != null && unregisterLifecycle != null)
                unregisterLifecycle.Invoke(adapter, null);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
    private static void Require(bool result, string description)
    { if (!result) throw new InvalidOperationException("Integration check failed: " + description); }
}
