using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallConnect;
using LivingCities.Core;
using LivingCities.DFU;
using UnityEditor;
using UnityEngine;

public static class LivingCitiesBuild
{
    public static void BuildGuardProbe()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        const string staging = "Assets/Game/Mods/LivingCitiesGuardProbe";
        Directory.CreateDirectory(staging);
        File.Copy(Path.Combine(root, "LivingCities/work/Harmony/net472/0Harmony.dll"), staging + "/0Harmony.dll.bytes", true);
        File.Copy(Path.Combine(root, "LivingCities/probes/guard/bin/Release/net472/GuardProbe.dll"), staging + "/GuardProbe.dll.bytes", true);
        var info = new ModInfo { ModTitle = "Living Cities Guard Probe", ModVersion = "0.0.1", ModAuthor = "Daniel",
            DFUnity_Version = "1.1.1", ModDescription = "Temporary opt-in feasibility test. Not gameplay.",
            GUID = "88cbbd21-cad5-4f82-b360-2046b18845d4" };
        File.WriteAllText(staging + "/GuardProbe.dfmod.json", JsonUtility.ToJson(info, true));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string output = Path.Combine(root, "LivingCities/work/GuardProbeBuild");
        Directory.CreateDirectory(output);
        var build = new AssetBundleBuild { assetBundleName = "livingcitiesguardprobe.dfmod", assetNames = new[] {
            staging + "/0Harmony.dll.bytes", staging + "/GuardProbe.dll.bytes", staging + "/GuardProbe.dfmod.json" } };
        Require(BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64) != null, "Guard probe bundle");
        File.Copy(Path.Combine(output, "livingcitiesguardprobe.dfmod"),
            Path.Combine(root, "Player/DaggerfallUnity_Data/StreamingAssets/Mods/livingcitiesguardprobe.dfmod"), true);
        Debug.Log("LIVING_CITIES_GUARD_PROBE_BUILT");
    }

    public static void VerifyAndBuild()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        VerifySerialization();
        VerifySettings();
        VerifyHousing();
        Require(!StreetPopulation.IsVisitDestination(DaggerfallConnect.DFLocation.BuildingTypes.House1), "House1 is not an open shop destination");
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
            if (source.EndsWith("/modsettings.json")) { assets.Add(source); continue; }
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
            ",\"nativeSettings\":true,\"installed\":true,\"physicalActors\":false}");
        Debug.Log("LIVING_CITIES_BUILD_SUCCESS: serializer, adapter lifecycle, contracts and bundle verified; installed.");
    }
    public static void VerifySaveState()
    {
        VerifySerialization();
        VerifyAdapter();
        Debug.Log("LIVING_CITIES_SAVE_STATE_VERIFIED: FullSerializer, family migration and adapter lifecycle passed; no bundle installed.");
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
        Debug.Log("LIVING_CITIES_HOUSING_VERIFIED: original bed census, building keys, order and footprint passed.");
    }
    private static void VerifySerialization()
    {
        var store = new WorldStateStore(uint.MaxValue);
        var town = store.GetOrCreate(100, new[] { 10, 20 });
        store.SetRoutines(new TownRoutineState { SettlementId = 100, Residents = new List<ResidentRoutine> {
            new ResidentRoutine { ResidentId = town.Households[0].Residents[0].Id, DestinationKey = 30, OpenMinute = 600, CloseMinute = 1000,
                Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 10.5, Z = 20 } } } } });
        Population.MarkDead(town, town.Households[0].Residents[0].Id);
        string serialized = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        var restored = new WorldStateStore(0);
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), serialized));
        Require(serialized == SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot()), "FullSerializer round trip");
        Require(!restored.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0].IsAlive, "Death persistence");

        var profiles = new List<HomeProfile>();
        for (int key = 1; key <= 100; key++) profiles.Add(new HomeProfile(key, 4, 200));
        var familyTown = store.GetOrCreateFamilies(200, profiles);
        var family = familyTown.Households.Find(home => home.Residents.Count == 5);
        Require(family != null, "Five-person family fixture");
        family.Residents[0].IsAlive = false;
        serialized = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), serialized));
        Require(serialized == SaveLoadManager.Serialize(typeof(WorldState), restored.Snapshot()), "Family FullSerializer round trip");
        var savedFamily = restored.GetOrCreateFamilies(200, profiles, 1).Households.Find(home => home.BuildingKey == family.BuildingKey);
        Require(savedFamily.Residents.Count == 5 && savedFamily.Residents[2].FatherId == family.Residents[0].Id &&
            savedFamily.Residents[2].AgeGroup == ResidentAgeGroup.Child && !savedFamily.Residents[0].IsAlive &&
            savedFamily.Residents[1].PartnerId == family.Residents[0].Id && savedFamily.SurnameSeed == family.SurnameSeed,
            "Persistent family roles and deceased parent");
        for (int version = 1; version <= 2; version++)
        {
            string legacy = "{\"Version\":" + version + ",\"WorldSeed\":42,\"Settlements\":[{\"SettlementId\":100,\"Households\":[{\"BuildingKey\":10,\"Residents\":[{\"Id\":\"legacy\",\"HomeKey\":10,\"AppearanceSeed\":3,\"IsAlive\":false}]}]}],\"Routines\":[]}";
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), legacy));
            var migrated = restored.Snapshot(); var home = migrated.Settlements[0].Households[0]; var resident = home.Residents[0];
            Require(migrated.Version == 3 && resident.Id == "legacy" && resident.AppearanceSeed == 3 && !resident.IsAlive &&
                resident.AgeGroup == ResidentAgeGroup.Adult && resident.Sex == ResidentSex.Female && resident.PartnerId == null &&
                home.SurnameSeed == StableSeed.Combine(42, 100, 10, 0xfffffffe), "Legacy FullSerializer migration " + version);
        }
    }
    private static void VerifySettings()
    {
        string json = File.ReadAllText("Assets/Game/Mods/LivingCities/modsettings.json");
        var data = (ModSettingsData)SaveLoadManager.Deserialize(typeof(ModSettingsData), json);
        Require(data.GetValue<bool>("General", "Enabled"), "Simulation toggle default");
        Require(data.GetValue<int>("Population", "HouseholdLimit") == 5, "Household slider default");
        Require(data.GetValue<int>("DailyRoutine", "WakeHour") == 6, "Wake slider default");
        Require(data.GetValue<int>("DailyRoutine", "SleepHour") == 22, "Sleep slider default");
        Require(data.GetValue<int>("Diagnostics", "RefreshSeconds") == 1, "Refresh slider default");
        Require(data.GetValue<bool>("Diagnostics", "EnableF8Panel"), "Panel toggle default");
        Require(data.GetValue<bool>("General", "StreetPopulation"), "Street population default");
        Debug.Log("LIVING_CITIES_SETTINGS_VERIFIED: seven native menu controls deserialize with expected types and defaults.");
    }
    private static void VerifyAdapter()
    {
        var go = new GameObject("Living Cities integration test");
        try
        {
            var adapter = go.AddComponent<LivingCitiesMod>();
            adapter.RestoreSaveData(adapter.NewSaveData());
            var saved = (WorldState)adapter.GetSaveData();
            Require(saved.Version == 3 && saved.Settlements.Count == 0 && saved.Routines.Count == 0, "New world state");
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
