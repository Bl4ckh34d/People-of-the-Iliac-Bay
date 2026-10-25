using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Utility.AssetInjection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Development-only export of reusable native city blocks. The shipped game
    // loads their baked NavMeshData; it does not construct these temporary roots.
    internal static class NativeNavigationCatalog
    {
        public static IEnumerator ExportRmb(string folder)
        {
            if(!DaggerfallUnity.Instance.IsReady)throw new InvalidOperationException("DFU block reader is not ready.");
            string variant=NativeNavigationTerrainTemplates.Variant();
            if(string.IsNullOrEmpty(variant))throw new InvalidOperationException("RMB catalog requires a loaded native location and geometry variant.");
            var game=GameManager.Instance;
            var captureLocation=game.PlayerGPS.CurrentLocation;
            var nature=ClimateSwaps.FromAPITextureSet(game.PlayerGPS.CurrentLocation.Climate.NatureSet);
            var season=DaggerfallUnity.Instance.WorldTime.Now.SeasonValue==DaggerfallDateTime.Seasons.Winter
                ?ClimateSeason.Winter:ClimateSeason.Summer;
            Directory.CreateDirectory(folder);
            string manifestFile=Path.Combine(folder,"navigation-manifest.json");
            var manifest=File.Exists(manifestFile)?NativeNavigationManifest.Read(File.ReadAllText(manifestFile)):new NativeNavigationManifest();
            if(manifest.modules.Count>0 && manifest.profile!=NativeNavigationWorld.Profile())
                throw new InvalidOperationException("Navigation catalog mixes geometry mod profiles.");
            manifest.profile=NativeNavigationWorld.Profile();
            var blocks=DaggerfallUnity.Instance.ContentReader.BlockFileReader;
            var names=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool loadedOnly=Environment.GetEnvironmentVariable("POTIB_NAVMESH_CATALOG_LOADED_ONLY")=="1";
            var loadedBlocks=new Dictionary<string,DFBlock>(StringComparer.OrdinalIgnoreCase);
            if(loadedOnly)
            {
                WorldDataVariants.SetLastLocationKeyTo(captureLocation.RegionIndex,captureLocation.LocationIndex);
                var actual=RMBLayout.GetLocationBuildingData(captureLocation);
                if(actual==null || actual.Length==0)throw new InvalidOperationException("Loaded native location has no RMB blocks.");
                // Preserve the same cached records used to build this town. A
                // fresh raw lookup can resolve a different replacement variant.
                foreach(var block in actual)
                {
                    string identity=NativeNavigationTerrainTemplates.BlockKey(block);
                    if(!seen.Add(identity))continue;
                    loadedBlocks.Add(identity,block);names.Add(identity);
                }
            }
            else
            {
                for(int index=0;index<blocks.Count;index++)
                {
                    string name=blocks.GetBlockName(index);
                    if(name!=null && name.EndsWith(".RMB",StringComparison.OrdinalIgnoreCase) && seen.Add(name))names.Add(name);
                }
            }
            int added=0,limit=0;
            int.TryParse(Environment.GetEnvironmentVariable("POTIB_NAVMESH_CATALOG_LIMIT"),out limit);
            string only=Environment.GetEnvironmentVariable("POTIB_NAVMESH_CATALOG_BLOCK");
            foreach(string candidate in names)
            {
                DFBlock captured;
                bool fromLoaded=loadedBlocks.TryGetValue(candidate,out captured);
                string name=fromLoaded?captured.Name:candidate;
                if(!string.IsNullOrEmpty(only) && !name.Equals(only,StringComparison.OrdinalIgnoreCase))continue;
                if(!game.PlayerGPS.HasCurrentLocation ||
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId!=captureLocation.MapTableData.MapId ||
                    NativeNavigationTerrainTemplates.Variant()!=variant)
                    throw new InvalidOperationException("Native location or template geometry variant changed during RMB catalog export.");
                WorldDataVariants.SetLastLocationKeyTo(captureLocation.RegionIndex,captureLocation.LocationIndex);
                string legacyKey="rmb:"+name.ToUpperInvariant();
                DFBlock block=fromLoaded?captured:blocks.GetBlock(name);
                string key="rmb:"+NativeNavigationTerrainTemplates.BlockKey(block);
                if(manifest.modules.Exists(module=>module.scene==key && module.variant==variant))continue;
                GameObject root=null;
                var random=UnityEngine.Random.state;
                try
                {
                    if(!string.IsNullOrEmpty(only))InspectModels(block);
                    block=WithoutMissingModels(block);
                    root=RMBLayout.CreateBaseGameObject(ref block,0,0,DaggerfallUnity.Instance.Option_CityBlockPrefab);
                    if(!root)throw new InvalidOperationException("Cannot build native RMB geometry: "+name);
                    var ground=RMBLayout.AddGroundPlane(ref block,root.transform);
                    if(!ground)throw new InvalidOperationException("RMB ground missing: "+name);
                    var box=ground.GetComponent<BoxCollider>();if(box)box.enabled=false;
                    var mesh=ground.GetComponent<MeshFilter>().sharedMesh;
                    if(!mesh)throw new InvalidOperationException("RMB ground mesh missing: "+name);
                    ground.AddComponent<MeshCollider>().sharedMesh=mesh;
                    var flats=new GameObject("Flats");flats.transform.SetParent(root.transform,false);
                    var lights=new GameObject("Lights");lights.transform.SetParent(root.transform,false);
                    RMBLayout.AddLights(ref block,flats.transform,lights.transform);
                    RMBLayout.AddNatureFlats(ref block,flats.transform,null,nature,season);
                    RMBLayout.AddMiscBlockFlats(ref block,flats.transform,0,0);
                    RMBLayout.AddExteriorBlockFlats(ref block,flats.transform,lights.transform,0,0,nature,season);
                    var sources=NativeNavigationGeometry.Collect(root.transform);
                    sources.RemoveAll(source=>source.component is Collider && ((Collider)source.component).isTrigger);
                    if(sources.Count==0)throw new InvalidOperationException("RMB lacks walkable ground: "+name);
                    string temporary=Path.Combine(folder,"module-input.tmp");
                    var size=new Vector3(RMBLayout.RMBSide,128,RMBLayout.RMBSide);
                    NativeNavigationGeometry.Write(temporary,sources,Vector3.zero,new Bounds(new Vector3(size.x/2,0,size.z/2),size));
                    string hash;using(var sha=SHA256.Create())using(var stream=File.OpenRead(temporary))
                        hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
                    string asset="nav-"+hash;
                    File.Copy(temporary,Path.Combine(folder,asset+".navsource"),true);File.Delete(temporary);
                    manifest.modules.RemoveAll(module=>module.scene==key &&
                        (module.variant==variant || string.IsNullOrEmpty(module.variant)) ||
                        module.scene==legacyKey && string.IsNullOrEmpty(module.variant));
                    manifest.modules.Add(new NativeNavigationModule {scene=key,variant=variant,asset=asset+".asset",geometry=asset+".navsource",
                        position=Vector3.zero,size=size});
                    File.WriteAllText(manifestFile,manifest.Write());
                    added++;
                }
                finally
                {
                    UnityEngine.Random.state=random;
                    if(root)UnityEngine.Object.Destroy(root);
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_CATALOG_MODULE: "+name+"; exported="+added);
                if(limit>0 && added>=limit)break;
                yield return null;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_CATALOG_PASS: rmb="+manifest.modules.FindAll(module=>module.scene.StartsWith("rmb:",StringComparison.Ordinal)).Count+
                "; added="+added+"; totalClassic="+names.Count+"; loadedOnly="+loadedOnly);
        }
        private static DFBlock WithoutMissingModels(DFBlock block)
        {
            // The installed native layout does not guard failed GetModelData.
            // Clone referenced arrays: the content reader owns the cached block.
            block.RmbBlock.SubRecords=(DFBlock.RmbSubRecord[])block.RmbBlock.SubRecords.Clone();
            block.RmbBlock.MiscFlatObjectRecords=StaticFlats(block.RmbBlock.MiscFlatObjectRecords);
            for(int i=0;i<block.RmbBlock.SubRecords.Length;i++)
            {
                block.RmbBlock.SubRecords[i].Exterior.Block3dObjectRecords=ExistingModels(
                    block.RmbBlock.SubRecords[i].Exterior.Block3dObjectRecords,block.Name,"exterior "+i);
                block.RmbBlock.SubRecords[i].Exterior.BlockFlatObjectRecords=StaticFlats(
                    block.RmbBlock.SubRecords[i].Exterior.BlockFlatObjectRecords);
            }
            block.RmbBlock.Misc3dObjectRecords=ExistingModels(block.RmbBlock.Misc3dObjectRecords,block.Name,"misc");
            return block;
        }
        private static DFBlock.RmbBlockFlatObjectRecord[] StaticFlats(DFBlock.RmbBlockFlatObjectRecord[] records)
        {
            var result=new List<DFBlock.RmbBlockFlatObjectRecord>();
            if(records!=null)
                foreach(var record in records)if(record.FactionID==0)result.Add(record);
            return result.ToArray();
        }
        private static DFBlock.RmbBlock3dObjectRecord[] ExistingModels(DFBlock.RmbBlock3dObjectRecord[] records,string block,string part)
        {
            var result=new List<DFBlock.RmbBlock3dObjectRecord>();
            foreach(var record in records)
            {
                ModelData data;
                if(DaggerfallUnity.Instance.MeshReader.GetModelData(record.ModelIdNum,out data) || HasReplacement(record.ModelIdNum))
                    result.Add(record);
                else Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_MISSING_MODEL: block="+block+"; part="+part+"; id="+record.ModelIdNum);
            }
            return result.ToArray();
        }
        private static bool HasReplacement(uint id)
        {
            if(!DaggerfallUnity.Settings.AssetInjection || !ModManager.Instance)return false;
            string name=id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var names=new[]{name};
            if(GameManager.HasInstance)
            {
                var climate=ClimateSwaps.FromAPIClimateBase(GameManager.Instance.PlayerGPS.ClimateSettings.ClimateType);
                names=new[]{climate==ClimateBases.Desert?name+"_"+climate:
                    name+"_"+climate+DaggerfallUnity.Instance.WorldTime.Now.SeasonValue,name};
            }
            GameObject prefab;
            return ModManager.Instance.TryGetAsset<GameObject>(names,false,out prefab);
        }
        private static void InspectModels(DFBlock block)
        {
            var ids=new HashSet<uint>();
            foreach(var sub in block.RmbBlock.SubRecords)
                foreach(var model in sub.Exterior.Block3dObjectRecords)ids.Add(model.ModelIdNum);
            foreach(var model in block.RmbBlock.Misc3dObjectRecords)ids.Add(model.ModelIdNum);
            foreach(uint id in ids)
            {
                ModelData data;bool found=DaggerfallUnity.Instance.MeshReader.GetModelData(id,out data);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_MODEL: block="+block.Name+"; id="+id+"; found="+found+
                    "; submeshes="+(data.SubMeshes==null?-1:data.SubMeshes.Length)+"; vertices="+(data.Vertices==null?-1:data.Vertices.Length)+
                    "; normals="+(data.Normals==null?-1:data.Normals.Length)+"; uv="+(data.UVs==null?-1:data.UVs.Length)+
                    "; indices="+(data.Indices==null?-1:data.Indices.Length));
            }
        }
    }
}
