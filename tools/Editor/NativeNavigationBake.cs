using System;
using System.Collections.Generic;
using System.IO;
using PeopleOfTheIliacBay.DFU;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class NativeNavigationBake
{
    private static readonly List<AssetBundle> geometryBundles=new List<AssetBundle>();
    private static readonly List<Mesh> replacementMeshes=new List<Mesh>();
    private static Mesh ResolveMesh(string name,int vertices,Bounds bounds)
    {
        if(geometryBundles.Count==0)
        {
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Player/DaggerfallUnity_Data/StreamingAssets/Mods"));
            foreach(string file in Directory.GetFiles(folder,"*.dfmod"))
            {
                if(Path.GetFileName(file)=="peopleoftheiliacbay.dfmod")continue;
                var bundle=AssetBundle.LoadFromFile(file);if(!bundle)continue;geometryBundles.Add(bundle);
                replacementMeshes.AddRange(bundle.LoadAllAssets<Mesh>());
                foreach(string asset in bundle.GetAllAssetNames())
                {
                    if(!asset.EndsWith(".prefab")&&!asset.EndsWith(".fbx")&&!asset.EndsWith(".obj"))continue;
                    var model=bundle.LoadAsset<GameObject>(asset);if(!model)continue;
                    foreach(var collider in model.GetComponentsInChildren<MeshCollider>(true))if(collider.sharedMesh)replacementMeshes.Add(collider.sharedMesh);
                }
            }
        }
        foreach(var mesh in replacementMeshes)
            if(mesh && mesh.name==name && mesh.vertexCount==vertices && (mesh.bounds.center-bounds.center).sqrMagnitude<0.00001f &&
                (mesh.bounds.size-bounds.size).sqrMagnitude<0.00001f)return mesh;
        return null;
    }
    public static List<string> BakeCaptured()
    {
        string input=PeopleOfTheIliacBayPaths.Repository("work/navigation-geometry");
        const string output="Assets/Game/Mods/PeopleOfTheIliacBay/Navigation";
        Directory.CreateDirectory(output);
        string manifestPath=Path.Combine(input,"navigation-manifest.json");
        var manifest=File.Exists(manifestPath)?NativeNavigationManifest.Read(File.ReadAllText(manifestPath)):new NativeNavigationManifest();
        // Only captures keyed by native static records and exact variants may
        // ship. Runtime additionally checks the whole city's native terrain.
        manifest.modules.RemoveAll(module=>module.scene.StartsWith("rmb:",StringComparison.Ordinal) &&
            (string.IsNullOrEmpty(module.variant) || module.scene.IndexOf('#')<0));
        bool captureBuild=Environment.GetEnvironmentVariable("POTIB_NAVIGATION_CAPTURE_BUILD")=="1";
        if(manifest.modules.Count==0 && !captureBuild)
            throw new InvalidDataException("No navigation modules captured. Use the explicit capture build to collect geometry first.");
        if(captureBuild)
            manifest.modules.RemoveAll(module=>!File.Exists(output+"/"+module.asset));
        var assets=new List<string>();var seen=new HashSet<string>();int baked=0;
        foreach(var module in manifest.modules)
        {
            string asset=output+"/"+module.asset;
            if(!seen.Add(asset))continue;
            if(!File.Exists(asset))
            {
                var meshes=new List<Mesh>();NavMeshData data=null;
                try
                {
                    Bounds bounds;var sources=NativeNavigationGeometry.Read(Path.Combine(input,module.geometry),out bounds,meshes,ResolveMesh);
                    data=NavMeshBuilder.BuildNavMeshData(NativeNavigationGeometry.Settings(),sources,bounds,Vector3.zero,Quaternion.identity);
                    if(!data)throw new InvalidOperationException("Native navigation bake failed: "+module.geometry);
                    AssetDatabase.CreateAsset(data,asset);baked++;
                }
                finally {foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);}
            }
            assets.Add(asset);
        }
        string shipped=output+"/navigation-manifest.json";
        File.WriteAllText(shipped,manifest.Write());assets.Add(shipped);
        AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_BAKED: modules="+seen.Count+"; newlyBaked="+baked+"; reused="+(seen.Count-baked));
        foreach(var bundle in geometryBundles)bundle.Unload(true);geometryBundles.Clear();replacementMeshes.Clear();
        return assets;
    }
}
