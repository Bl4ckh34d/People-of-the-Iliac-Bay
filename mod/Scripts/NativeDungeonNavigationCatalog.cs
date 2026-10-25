using System;
using System.Collections;
using System.IO;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Development capture of each block already placed by native dungeon layout.
    // Keys retain capture provenance. Reuse additionally requires the runtime
    // selector to verify the current native static collision content hash.
    internal static class NativeDungeonNavigationCatalog
    {
        public static IEnumerator ExportLoaded(string folder)
        {
            var game=GameManager.Instance;
            if(string.IsNullOrWhiteSpace(folder) || !game || game.PlayerEntity==null ||
                !game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal) ||
                !game.PlayerEnterExit || !game.PlayerEnterExit.IsPlayerInsideDungeon)
                throw new InvalidOperationException("Dungeon block export requires an owned loaded native dungeon and folder.");

            var dungeon=game.PlayerEnterExit.Dungeon;
            if(!dungeon || !dungeon.StartMarker || !dungeon.Summary.LocationData.Loaded ||
                !dungeon.Summary.LocationData.HasDungeon ||
                dungeon.Summary.ID!=dungeon.Summary.LocationData.MapTableData.MapId)
                throw new InvalidOperationException("Native dungeon block layout is unavailable.");
            var blocks=dungeon.Summary.LocationData.Dungeon.Blocks;
            if(blocks==null || blocks.Length==0)
                throw new InvalidOperationException("Native dungeon contains no RDB blocks.");
            string variant=NativeNavigationWorld.Variant();
            if(string.IsNullOrWhiteSpace(variant))
                throw new InvalidOperationException("Dungeon block export needs the current geometry variant.");

            // Native LayoutDungeon creates direct block children in summary order.
            // Include inactive children so an incomplete layout cannot look valid.
            var loaded=dungeon.GetComponentsInChildren<DaggerfallRDBBlock>(true);
            if(loaded.Length!=blocks.Length)
                throw new InvalidOperationException("Loaded RDB block count differs from native dungeon layout: "+
                    loaded.Length+"/"+blocks.Length);
            // Validate the entire layout before writing its first module.
            for(int index=0;index<blocks.Length;index++)
            {
                DFLocation.DungeonBlock block=blocks[index];
                if(string.IsNullOrWhiteSpace(block.BlockName) ||
                    !block.BlockName.EndsWith(".RDB",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Invalid native RDB name at block "+index);
                string name="DaggerfallBlock ["+block.BlockName+"]";
                var candidate=loaded[index];
                Vector3 position=new Vector3(block.X*RDBLayout.RDBSide,0,block.Z*RDBLayout.RDBSide);
                if(!candidate || candidate.transform.parent!=dungeon.transform ||
                    !candidate.gameObject.activeInHierarchy ||
                    !string.Equals(candidate.name,name,StringComparison.OrdinalIgnoreCase) ||
                    Vector3.Distance(candidate.transform.localPosition,position)>0.05f ||
                    Quaternion.Angle(candidate.transform.localRotation,Quaternion.identity)>0.01f ||
                    Vector3.Distance(candidate.transform.localScale,Vector3.one)>0.001f)
                    throw new InvalidOperationException("Native RDB block differs from summary ordinal or native local transform: "+
                        index+" "+block.BlockName+" "+position);
                if(NativeNavigationGeometry.Collect(candidate.transform).Count==0)
                    throw new InvalidOperationException("Native RDB block has no collision geometry: "+block.BlockName);
            }
            for(int index=0;index<blocks.Length;index++)
            {
                if(!game.PlayerEnterExit.IsPlayerInsideDungeon || game.PlayerEnterExit.Dungeon!=dungeon)
                    throw new InvalidOperationException("Loaded dungeon changed during RDB block export.");
                DFLocation.DungeonBlock block=blocks[index];
                var root=loaded[index].transform;
                string key="dungeon-rdb:"+dungeon.Summary.ID+":"+index+":"+block.BlockName.ToUpperInvariant();
                // ExportRoot stores root-local sources and content hashes. The
                // key retains the native summary ordinal and block name; local
                // placement is checked above against that exact summary entry.
                yield return NativeNavigationWorld.ExportRoot(root,key,folder,variant);
                Vector3 geometryOrigin,geometrySize;string hash,providers,refusal;
                if(!NativeDungeonNavigationModules.TryGeometry(root,out geometryOrigin,out geometrySize,out hash,out providers,out refusal))
                    throw new InvalidOperationException("Native RDB collision provenance capture failed: "+refusal);
                string manifestFile=Path.Combine(folder,"navigation-manifest.json");
                var manifest=NativeNavigationManifest.Read(File.ReadAllText(manifestFile));
                var module=manifest.modules.Find(value=>value.scene==key && value.variant==variant);
                if(module==null || module.asset!="nav-"+hash+".asset" || module.geometry!="nav-"+hash+".navsource" ||
                    module.position!=geometryOrigin || module.size!=geometrySize)
                    throw new InvalidOperationException("Native RDB geometry changed during collision provenance capture: "+key);
                module.collisionProviders=providers;
                File.WriteAllText(manifestFile,manifest.Write());
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_RDB_MODULE_PASS: scene="+key+
                    "; blockPosition="+root.localPosition+"; blockRotation="+root.localRotation+
                    "; variant="+variant+"; profile="+NativeNavigationWorld.Profile()+
                    "; collisionProviders="+(providers??"readable-source-vertices"));
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_RDB_CATALOG_PASS: dungeon="+
                dungeon.Summary.ID+"; blocks="+blocks.Length+"; runtimeReuse=false");
        }
    }
}
