using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using LivingCities.Core;
using LivingCities.DFU;
using UnityEditor;
using UnityEngine;

public static class LivingCitiesBuild
{
    public static void VerifyAndBuild()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        VerifySerialization();
        Require(typeof(IHasModSaveData).IsAssignableFrom(typeof(LivingCitiesMod)), "Save adapter contract");
        Require(typeof(BuildingDirectory).GetMethod("GetBuildingsOfType") != null, "Building enumeration hook");
        Require(typeof(CityNavigation).GetMethod("GetNavGridWeightLocal", new[] { typeof(int), typeof(int) }) != null, "Street grid hook");
        Require(typeof(PopulationManager).GetEvent("OnMobileNPCEnable") != null, "Mobile lifecycle hook");
        Require(typeof(PlayerEntity).GetMethod("SpawnCityGuards") != null, "Guard spawn entry");
        Require(!typeof(PlayerEntity).GetMethod("SpawnCityGuards").IsVirtual, "Guard spawn limitation changed; reassess interception");
        VerifyAdapter();

        const string manifestPath = "Assets/Game/Mods/LivingCities/LivingCities.dfmod.json";
        var info = JsonUtility.FromJson<ModInfo>(File.ReadAllText(manifestPath));
        const string staging = "Assets/Game/Mods/LivingCities/BuildText";
        Directory.CreateDirectory(staging);
        var assets = new List<string> { manifestPath };
        foreach (string source in info.Files)
        {
            Require(source.EndsWith(".cs") && !source.Contains("/Editor/"), "Only runtime sources may ship");
            string target = staging + "/" + Path.GetFileName(source) + ".txt";
            File.Copy(source, target, true); assets.Add(target);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string output = Path.Combine(root, "LivingCities/dist");
        Directory.CreateDirectory(output);
        var build = new AssetBundleBuild { assetBundleName = "livingcities.dfmod", assetNames = assets.ToArray() };
        Require(BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64) != null, "Bundle build");
        string package = Path.Combine(output, "livingcities.dfmod");
        AssetBundle bundle = AssetBundle.LoadFromFile(package);
        Require(bundle != null, "Bundle reopen");
        try
        {
            Require(bundle.GetAllAssetNames().Length == assets.Count, "Bundle asset count");
            foreach (string asset in assets) Require(bundle.LoadAsset<TextAsset>(asset) != null, "Missing bundled source: " + asset);
        }
        finally { bundle.Unload(true); }
        File.Copy(package, Path.Combine(root, "Player/DaggerfallUnity_Data/StreamingAssets/Mods/livingcities.dfmod"), true);
        string evidence = Path.Combine(root, "LivingCities/work");
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "unity-build-result.json"),
            "{\"fullSerializerRoundTrip\":true,\"adapterSaveLifecycle\":true,\"publicHookContracts\":true,\"assetCount\":" + assets.Count +
            ",\"installed\":true,\"physicalActors\":false}");
        Debug.Log("LIVING_CITIES_BUILD_SUCCESS: serializer, adapter lifecycle, contracts and bundle verified; installed.");
    }
    private static void VerifySerialization()
    {
        var store = new WorldStateStore(uint.MaxValue);
        var town = store.GetOrCreate(100, new[] { 10, 20 });
        Population.MarkDead(town, town.Households[0].Residents[0].Id);
        string serialized = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        var restored = new WorldStateStore(0);
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), serialized));
        Require(serialized == SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot()), "FullSerializer round trip");
        Require(!restored.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0].IsAlive, "Death persistence");
    }
    private static void VerifyAdapter()
    {
        var go = new GameObject("Living Cities integration test");
        try
        {
            var adapter = go.AddComponent<LivingCitiesMod>();
            adapter.RestoreSaveData(adapter.NewSaveData());
            var saved = (WorldState)adapter.GetSaveData();
            Require(saved.Version == 1 && saved.Settlements.Count == 0, "New world state");
            bool rejected = false;
            try { adapter.RestoreSaveData(new WorldState { Version = 999 }); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Invalid adapter restore rejection");
            rejected = false;
            try { adapter.GetSaveData(); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Failed restore must prevent overwriting save state");
            adapter.RestoreSaveData(saved);
            Require(((WorldState)adapter.GetSaveData()).WorldSeed == saved.WorldSeed, "Recovery through valid restore");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    private static void Require(bool result, string description)
    { if (!result) throw new InvalidOperationException("Integration check failed: " + description); }
}
