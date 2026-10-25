using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Utility.AssetInjection;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit development export of reusable, model-only RMB interiors.
    internal static class NativeInteriorNavigationCatalog
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;

        public static IEnumerator Export(string folder,string blockName,int recordIndex)
        {
            var dfu=DaggerfallUnity.Instance;
            var game=GameManager.Instance;
            if(string.IsNullOrEmpty(folder) || string.IsNullOrWhiteSpace(blockName) ||
                !blockName.EndsWith(".RMB",StringComparison.OrdinalIgnoreCase) || recordIndex<0 ||
                !dfu || !dfu.IsReady || !game || !game.PlayerGPS || !game.PlayerGPS.HasCurrentLocation ||
                game.PlayerEntity==null ||
                !game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal))
                throw new InvalidOperationException("Interior template export requires an RMB, subrecord, and owned native test fixture.");

            // Use the same location-resolved block that native DoLayout used.
            // A fresh GetBlock(name) can select a different world-data variant.
            var loaded=game.PlayerEnterExit && game.PlayerEnterExit.IsPlayerInside &&
                !game.PlayerEnterExit.IsPlayerInsideDungeon ? game.PlayerEnterExit.Interior : null;
            if(!loaded || loaded.EntryDoor.recordIndex!=recordIndex)
                throw new InvalidOperationException("Interior template needs the matching loaded native interior.");
            DFBlock block;
            if(!NativeInteriorNavigationModules.TryGetBlock(loaded,out block) ||
                !string.Equals(block.Name,blockName,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("RMB subrecord has no exact native interior: "+blockName+" #"+recordIndex);

            string scene=NativeInteriorNavigationModules.InteriorKey(block,recordIndex);
            string variant=NativeInteriorNavigationModules.Variant();
            if(string.IsNullOrEmpty(variant))throw new InvalidOperationException("Interior template needs the current native geometry variant.");
            yield return ExportRecord(folder,block,recordIndex,scene,variant,
                ClimateSwaps.FromAPIClimateBase(game.PlayerGPS.ClimateSettings.ClimateType));
        }

        public static IEnumerator ExportLoadedLocation(string folder)
        {
            var dfu=DaggerfallUnity.Instance;
            var game=GameManager.Instance;
            if(string.IsNullOrWhiteSpace(folder) || !dfu || !dfu.IsReady || !game ||
                !game.PlayerGPS || !game.PlayerGPS.HasCurrentLocation || !game.StreamingWorld ||
                game.PlayerEntity==null ||
                !game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal))
                throw new InvalidOperationException("Interior location export requires an owned native test fixture and loaded location.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            if(!directory || directory.MapID!=game.PlayerGPS.CurrentLocation.MapTableData.MapId)
                throw new InvalidOperationException("Interior location export requires the matching loaded native building directory.");
            var location=directory.LocationData;
            string variant=NativeInteriorNavigationModules.Variant();
            string profile=NativeNavigationWorld.Profile();
            if(string.IsNullOrEmpty(variant))throw new InvalidOperationException("Interior location export requires a native geometry variant.");
            WorldDataVariants.SetLastLocationKeyTo(location.RegionIndex,location.LocationIndex);
            var blocks=RMBLayout.GetLocationBuildingData(location);
            if(blocks==null || blocks.Length==0)
                throw new InvalidOperationException("Loaded native location has no cached RMB records.");
            // GetLocationBuildingData returns the location cache used for native
            // layout, including world-data and building replacements. Preserve it.
            var records=UniqueInteriors(blocks);
            string manifestFile=Path.Combine(folder,"navigation-manifest.json");
            var manifest=File.Exists(manifestFile)?NativeNavigationManifest.Read(File.ReadAllText(manifestFile)):
                new NativeNavigationManifest();
            if(manifest.modules.Count>0 && manifest.profile!=profile)
                throw new InvalidOperationException("Interior location export mixes geometry mod profiles.");
            var climate=ClimateSwaps.FromAPIClimateBase(game.PlayerGPS.ClimateSettings.ClimateType);
            int added=0,existing=0;
            foreach(var record in records)
            {
                if(!game.PlayerGPS.HasCurrentLocation ||
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId!=location.MapTableData.MapId ||
                    NativeInteriorNavigationModules.Variant()!=variant || NativeNavigationWorld.Profile()!=profile)
                    throw new InvalidOperationException("Native location or geometry profile changed during interior location export.");
                string scene=NativeInteriorNavigationModules.InteriorKey(record.Key,record.Value);
                if(manifest.modules.Exists(module=>module!=null && module.scene==scene && module.variant==variant &&
                    !string.IsNullOrEmpty(module.geometry) && File.Exists(Path.Combine(folder,module.geometry))))
                {existing++;continue;}
                yield return ExportRecord(folder,record.Key,record.Value,scene,variant,climate);
                added++;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_CATALOG_MODULE: scene="+scene+
                    "; added="+added+"; candidates="+records.Count);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_CATALOG_PASS: map="+location.MapTableData.MapId+
                "; blocks="+blocks.Length+"; uniqueInteriors="+records.Count+"; added="+added+"; existing="+existing+
                "; modelOnly=true; source=loaded-location-cache");
        }

        private static List<KeyValuePair<DFBlock,int>> UniqueInteriors(DFBlock[] blocks)
        {
            var result=new List<KeyValuePair<DFBlock,int>>();
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var block in blocks)
            {
                if(block.RmbBlock.SubRecords==null)continue;
                for(int recordIndex=0;recordIndex<block.RmbBlock.SubRecords.Length;recordIndex++)
                {
                    string scene=NativeInteriorNavigationModules.InteriorKey(block,recordIndex);
                    if(scene!=null && seen.Add(scene))result.Add(new KeyValuePair<DFBlock,int>(block,recordIndex));
                }
            }
            return result;
        }

        private static IEnumerator ExportRecord(string folder,DFBlock block,int recordIndex,string scene,
            string variant,ClimateBases climate)
        {
            var root=new GameObject("People of the Iliac Bay interior navigation template");
            var random=UnityEngine.Random.state;
            bool randomRestored=false;
            try
            {
                var interior=root.AddComponent<DaggerfallInterior>();
                var door=new StaticDoor {doorType=DoorTypes.Building,blockIndex=block.Index,
                    recordIndex=recordIndex,buildingMatrix=Matrix4x4.identity,
                    ownerPosition=Vector3.zero,ownerRotation=Quaternion.identity};
                LayoutModels(interior,root.transform,door,climate,block);
                UnityEngine.Random.state=random;
                randomRestored=true;
                // AddModels assigns world positions to some individual models.
                // Build at origin, then move the complete native hierarchy away.
                root.transform.position=Vector3.up*1000;
                foreach(var renderer in root.GetComponentsInChildren<Renderer>())renderer.enabled=false;
                Physics.SyncTransforms();
                yield return NativeNavigationWorld.ExportRoot(root.transform,scene,folder,variant);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_TEMPLATE_PASS: scene="+scene+
                    "; variant="+variant+"; modelOnly=true; source=loaded-location-block; recordIndex="+recordIndex);
            }
            finally
            {
                if(!randomRestored)UnityEngine.Random.state=random;
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
            }
        }

        private static void LayoutModels(DaggerfallInterior interior,Transform owner,StaticDoor door,
            ClimateBases climate,DFBlock block)
        {
            var type=typeof(DaggerfallInterior);
            Set(type,interior,"dfUnity",DaggerfallUnity.Instance);
            Set(type,interior,"climateBase",climate);
            Set(type,interior,"entryDoor",door);
            Set(type,interior,"doorOwner",owner);
            Set(type,interior,"blockData",block);
            Set(type,interior,"recordData",block.RmbBlock.SubRecords[door.recordIndex]);
            var models=type.GetMethod("AddModels",Private);
            if(models==null)throw new MissingMethodException(type.FullName,"AddModels");
            // Native building type changes furniture action components only;
            // it does not change model selection, placement or collision meshes.
            // AllValid keeps this model-only capture free of loot/save objects.
            models.Invoke(interior,new object[]{new PlayerGPS.DiscoveredBuilding {
                buildingType=DFLocation.BuildingTypes.AllValid},true});
        }

        private static void Set(Type type,DaggerfallInterior interior,string name,object value)
        {
            var field=type.GetField(name,Private);
            if(field==null)throw new MissingFieldException(type.FullName,name);
            field.SetValue(interior,value);
        }
    }
}
