using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Utility.AssetInjection;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // A flat RMB NavMesh can represent a native city only where the actual
    // terrain under every block agrees with its captured ground plane.
    internal static class NativeNavigationTerrainTemplates
    {
        private const float GroundTolerance=0.05f;
        private const float AgentMargin=0.30f;

        // RMB scenery uses location NatureSet, while replacement 3D models use
        // PlayerGPS climate and exact season from NativeNavigationWorld.Variant.
        public static string Variant()
        {
            string basis=NativeNavigationWorld.Variant();
            var game=GameManager.Instance;
            var df=DaggerfallUnity.Instance;
            if(string.IsNullOrEmpty(basis) || !game || !game.PlayerGPS ||
                !game.PlayerGPS.HasCurrentLocation || !df)return null;
            var nature=ClimateSwaps.FromAPITextureSet(game.PlayerGPS.CurrentLocation.Climate.NatureSet);
            return basis+";natureSet="+((int)nature).ToString(CultureInfo.InvariantCulture)+
                ";meshColliders="+(df.Option_AddMeshColliders?"1":"0")+
                ";combineRMB="+(df.Option_CombineRMB?"1":"0")+
                ";lightPrefabs="+(df.Option_ImportLightPrefabs?"1":"0")+
                ";groundOffset="+DaggerfallGroundPlane.GroundOffset.ToString("R",CultureInfo.InvariantCulture);
        }

        // All-or-nothing keeps the existing city module-count and seam gates.
        // The caller may prefer an exact scene capture before using this result.
        public static bool TrySelect(Transform root,string scene,NativeNavigationManifest manifest,
            out List<NativeNavigationModule> selected,out string reason)
        {
            selected=new List<NativeNavigationModule>();reason=null;
            if(!root || manifest==null || manifest.modules==null ||
                string.IsNullOrEmpty(scene) || !scene.StartsWith("city-",StringComparison.Ordinal) ||
                manifest.profile!=NativeNavigationWorld.Profile())
            {reason="scene-or-profile";return false;}
            string variant=Variant();
            if(string.IsNullOrEmpty(variant)){reason="variant-unavailable";return false;}
            var directory=root.GetComponent<BuildingDirectory>();
            if(!directory || scene!="city-"+directory.MapID)
            {reason="native-directory-missing";return false;}
            // Native block/building variants use the last location read, which
            // unrelated location queries can change after this city is loaded.
            var location=directory.LocationData;
            WorldDataVariants.SetLastLocationKeyTo(location.RegionIndex,location.LocationIndex);
            var actualBlocks=RMBLayout.GetLocationBuildingData(directory.LocationData);
            if(actualBlocks==null || actualBlocks.Length==0)
            {reason="native-block-data-missing";return false;}
            var actualKeys=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var actual in actualBlocks)
            {
                string name=actual.Name;
                if(string.IsNullOrEmpty(name)){reason="native-block-name-missing";return false;}
                string key=BlockKey(actual);
                string previous;
                if(actualKeys.TryGetValue(name,out previous) && previous!=key)
                {reason="ambiguous-native-block:"+name;return false;}
                actualKeys[name]=key;
            }
            var templates=new Dictionary<string,NativeNavigationModule>(StringComparer.OrdinalIgnoreCase);
            foreach(var module in manifest.modules)
            {
                if(module==null || module.scene==null || module.variant!=variant ||
                    !module.scene.StartsWith("rmb:",StringComparison.Ordinal))continue;
                string name=module.scene.Substring(4);
                if(templates.ContainsKey(name)){reason="duplicate-template:"+name;return false;}
                templates.Add(name,module);
            }
            var blocks=root.GetComponentsInChildren<DaggerfallRMBBlock>();
            if(blocks.Length==0 || blocks.Length!=actualBlocks.Length)
            {reason="native-block-layout-incomplete";return false;}
            foreach(var block in blocks)
            {
                string name=BlockName(block.name);
                NativeNavigationModule template;
                string key;
                if(name==null || !actualKeys.TryGetValue(name,out key) ||
                    !templates.TryGetValue(key,out template))
                {reason="missing-block-variant:"+name;selected.Clear();return false;}
                if(!block.gameObject.activeInHierarchy ||
                    Quaternion.Angle(root.rotation,block.transform.rotation)>0.01f ||
                    (block.transform.lossyScale-Vector3.one).sqrMagnitude>0.000001f)
                {reason="block-transform:"+name;selected.Clear();return false;}
                if(HasSolidStaticNpc(block))
                {reason="solid-static-npc:"+name;selected.Clear();return false;}
                if(!FlatNativeTerrain(block.transform.position,out reason))
                {if(reason==null)reason="block-transform:"+name;selected.Clear();return false;}
                selected.Add(new NativeNavigationModule {scene=scene,variant=variant,
                    asset=template.asset,geometry=template.geometry,
                    position=root.InverseTransformPoint(block.transform.position),size=template.size});
            }
            return true;
        }

        private static string BlockName(string name)
        {
            const string prefix="DaggerfallBlock [";
            return name!=null && name.StartsWith(prefix,StringComparison.Ordinal) &&
                name.EndsWith("]",StringComparison.Ordinal)
                ?name.Substring(prefix.Length,name.Length-prefix.Length-1):null;
        }

        private static bool HasSolidStaticNpc(DaggerfallRMBBlock block)
        {
            foreach(var npc in block.GetComponentsInChildren<StaticNPC>())
                foreach(var collider in npc.GetComponentsInChildren<Collider>())
                    if(collider.enabled && !collider.isTrigger)return true;
            return false;
        }

        // Native RMB data determines static layout. Location-specific building
        // names and ownership are omitted because they do not place colliders.
        // The exported .navsource SHA separately identifies resulting geometry.
        public static string BlockKey(DFBlock block)
        {
            byte[] bytes;
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream))
                {
                    writer.Write(1);
                    WriteModels(writer,block.RmbBlock.Misc3dObjectRecords);
                    WriteFlats(writer,block.RmbBlock.MiscFlatObjectRecords);
                    var scenery=block.RmbBlock.FldHeader.GroundData.GroundScenery;
                    int width=scenery==null?0:scenery.GetLength(0);
                    int height=scenery==null?0:scenery.GetLength(1);
                    writer.Write(width);writer.Write(height);
                    for(int x=0;x<width;x++)for(int z=0;z<height;z++)
                        writer.Write(scenery[x,z].TextureRecord);
                    var records=block.RmbBlock.SubRecords;
                    writer.Write(records==null?0:records.Length);
                    if(records!=null)foreach(var record in records)
                    {
                        writer.Write(record.XPos);writer.Write(record.ZPos);writer.Write(record.YRotation);
                        WriteModels(writer,record.Exterior.Block3dObjectRecords);
                        WriteFlats(writer,record.Exterior.BlockFlatObjectRecords);
                    }
                    writer.Flush();bytes=stream.ToArray();
                }
            }
            byte[] hash;
            using(var sha=SHA256.Create())hash=sha.ComputeHash(bytes);
            return block.Name.ToUpperInvariant()+"#"+BitConverter.ToString(hash).Replace("-","").ToLowerInvariant();
        }

        private static void WriteModels(BinaryWriter writer,DFBlock.RmbBlock3dObjectRecord[] records)
        {
            writer.Write(records==null?0:records.Length);
            if(records==null)return;
            foreach(var record in records)
            {
                writer.Write(record.ModelIdNum);writer.Write(record.ObjectType);
                writer.Write(record.XPos);writer.Write(record.YPos);writer.Write(record.ZPos);
                writer.Write(record.XScale);writer.Write(record.YScale);writer.Write(record.ZScale);
                writer.Write(record.XRotation);writer.Write(record.YRotation);writer.Write(record.ZRotation);
            }
        }

        private static void WriteFlats(BinaryWriter writer,DFBlock.RmbBlockFlatObjectRecord[] records)
        {
            int count=0;
            if(records!=null)foreach(var record in records)if(record.FactionID==0)count++;
            writer.Write(count);
            if(records==null)return;
            foreach(var record in records)
            {
                if(record.FactionID!=0)continue;
                writer.Write(record.TextureArchive);writer.Write(record.TextureRecord);
                writer.Write(record.XPos);writer.Write(record.YPos);writer.Write(record.ZPos);
                writer.Write(record.Flags);
            }
        }

        private static bool FlatNativeTerrain(Vector3 blockPosition,out string reason)
        {
            reason="native-terrain-missing";
            float side=RMBLayout.RMBSide;
            float minX=blockPosition.x-AgentMargin,maxX=blockPosition.x+side+AgentMargin;
            float minZ=blockPosition.z-AgentMargin,maxZ=blockPosition.z+side+AgentMargin;
            float expected=blockPosition.y+DaggerfallGroundPlane.GroundOffset*MeshReader.GlobalScale;
            foreach(var terrain in Terrain.activeTerrains)
            {
                if(!terrain || !terrain.isActiveAndEnabled || !terrain.terrainData)continue;
                var collider=terrain.GetComponent<TerrainCollider>();
                if(!collider || !collider.enabled || collider.terrainData!=terrain.terrainData)continue;
                var data=terrain.terrainData;var origin=terrain.transform.position;var size=data.size;
                if(minX<origin.x || maxX>origin.x+size.x || minZ<origin.z || maxZ>origin.z+size.z)continue;
                int resolution=data.heightmapResolution;
                int x0=Mathf.FloorToInt((minX-origin.x)/size.x*(resolution-1));
                int x1=Mathf.CeilToInt((maxX-origin.x)/size.x*(resolution-1));
                int z0=Mathf.FloorToInt((minZ-origin.z)/size.z*(resolution-1));
                int z1=Mathf.CeilToInt((maxZ-origin.z)/size.z*(resolution-1));
                if(x0<0 || z0<0 || x1>=resolution || z1>=resolution)continue;
                var heights=data.GetHeights(x0,z0,x1-x0+1,z1-z0+1);
                for(int z=0;z<heights.GetLength(0);z++)
                    for(int x=0;x<heights.GetLength(1);x++)
                        if(Mathf.Abs(origin.y+heights[z,x]*size.y-expected)>GroundTolerance)
                        {reason="terrain-not-flat-or-height-mismatch";return false;}
                reason=null;return true;
            }
            return false;
        }
    }
}
