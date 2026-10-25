using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Utility;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Reuses captured RDB assets only when the live native static collision input
    // has the same content hash. A name cannot identify WorldDataReplacement or
    // custom collision geometry. No NavMesh is baked in this selection path.
    internal static class NativeDungeonNavigationModules
    {
        public static bool TrySelect(Transform root,string scene,NativeNavigationManifest manifest,
            out List<NativeNavigationModule> selected,out string reason)
        {
            selected=new List<NativeNavigationModule>();reason="scene-or-profile";
            if(!root || manifest==null || manifest.modules==null || manifest.profile!=NativeNavigationWorld.Profile())return false;
            var dungeon=root.GetComponent<DaggerfallDungeon>();
            if(!dungeon || !dungeon.StartMarker || scene!="dungeon-"+dungeon.Summary.ID ||
                !dungeon.Summary.LocationData.Loaded || !dungeon.Summary.LocationData.HasDungeon ||
                dungeon.Summary.ID!=dungeon.Summary.LocationData.MapTableData.MapId)return false;
            string variant=NativeNavigationWorld.Variant();
            if(string.IsNullOrEmpty(variant)){reason="variant-unavailable";return false;}
            var layout=dungeon.Summary.LocationData.Dungeon.Blocks;
            var loaded=root.GetComponentsInChildren<DaggerfallRDBBlock>(true);
            if(layout==null || layout.Length==0 || loaded.Length!=layout.Length)
            {reason="native-block-layout-incomplete";return false;}
            var used=new HashSet<DaggerfallRDBBlock>();
            foreach(var entry in layout)
            {
                string name=entry.BlockName;
                if(string.IsNullOrEmpty(name) || !name.EndsWith(".RDB",StringComparison.OrdinalIgnoreCase))
                {reason="native-block-name";selected.Clear();return false;}
                var position=new Vector3(entry.X*RDBLayout.RDBSide,0,entry.Z*RDBLayout.RDBSide);
                DaggerfallRDBBlock actual=null;
                foreach(var candidate in loaded)
                {
                    if(!candidate || candidate.transform.parent!=root ||
                        !string.Equals(candidate.name,"DaggerfallBlock ["+name+"]",StringComparison.OrdinalIgnoreCase) ||
                        Vector3.Distance(candidate.transform.localPosition,position)>0.05f)continue;
                    if(actual){reason="ambiguous-native-block:"+name;selected.Clear();return false;}
                    actual=candidate;
                }
                if(!actual || !used.Add(actual) || !actual.gameObject.activeInHierarchy ||
                    Quaternion.Angle(actual.transform.localRotation,Quaternion.identity)>0.01f ||
                    Vector3.Distance(actual.transform.lossyScale,Vector3.one)>0.001f)
                {reason="native-block-transform:"+name;selected.Clear();return false;}
                Vector3 localOrigin,size;string hash,providers;
                if(!TryGeometry(actual.transform,out localOrigin,out size,out hash,out providers,out reason))
                {selected.Clear();return false;}
                NativeNavigationModule template;
                if(!TryMatch(manifest.modules,name,variant,hash,localOrigin,size,out template,out reason,providers))
                {selected.Clear();return false;}
                selected.Add(new NativeNavigationModule {scene=scene,variant=variant,
                    asset=template.asset,geometry=template.geometry,collisionProviders=template.collisionProviders,
                    position=actual.transform.localPosition+actual.transform.localRotation*template.position,size=template.size});
            }
            reason=null;return true;
        }

        internal static bool TryMatch(List<NativeNavigationModule> modules,string blockName,string variant,
            string hash,Vector3 localOrigin,Vector3 size,out NativeNavigationModule selected,out string reason,
            string collisionProviders=null)
        {
            selected=null;reason="missing-block-geometry:"+blockName;
            if(modules==null || string.IsNullOrEmpty(hash) || hash.Length!=64)return false;
            string asset="nav-"+hash+".asset",geometry="nav-"+hash+".navsource";
            foreach(var module in modules)
            {
                if(module==null || module.variant!=variant || module.asset!=asset || module.geometry!=geometry ||
                    (module.collisionProviders??"")!=(collisionProviders??"") ||
                    module.position!=localOrigin || module.size!=size || !SourceBlock(module.scene,blockName))continue;
                selected=module;reason=null;return true;
            }
            return false;
        }

        private static bool SourceBlock(string scene,string blockName)
        {
            if(scene==null)return false;
            var parts=scene.Split(':');int map,ordinal;
            return parts.Length==4 && parts[0]=="dungeon-rdb" &&
                int.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out map) && map>0 &&
                int.TryParse(parts[2],NumberStyles.None,CultureInfo.InvariantCulture,out ordinal) && ordinal>=0 &&
                string.Equals(parts[3],blockName,StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryGeometry(Transform root,out Vector3 origin,out Vector3 size,out string hash,
            out string collisionProviders,out string reason)
        {
            origin=size=Vector3.zero;hash=collisionProviders=null;reason="block-geometry-unavailable";
            if(!root)return false;
            reason+=":"+root.name;
            // Match ExportRoot's bounds and coordinate normalization exactly.
            var bounds=new Bounds(Vector3.zero,Vector3.one);
            foreach(var collider in root.GetComponentsInChildren<Collider>())
            {
                if(!collider.enabled || !collider.gameObject.activeInHierarchy ||
                    NativeNavigationGeometry.IsDynamicNavigationBody(collider))continue;
                var box=collider.bounds;
                for(int corner=0;corner<8;corner++)
                    bounds.Encapsulate(root.InverseTransformPoint(box.center+Vector3.Scale(box.extents,
                        new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1))));
            }
            bounds.Expand(2);origin=bounds.min;size=bounds.size;
            var sources=NativeNavigationGeometry.Collect(root);
            if(sources.Count==0)return false;
            var providers=new List<string>();
            for(int index=0;index<sources.Count;index++)
            {
                var source=sources[index];var mesh=source.sourceObject as Mesh;
                if(source.sourceObject is TerrainData)
                {reason="unverifiable-block-collision:"+root.name;return false;}
                if(mesh && !mesh.isReadable)
                {
                    string provider;
                    if(!TryProvider(root,source.component as MeshCollider,mesh,out provider))
                    {reason="native-collision-provider-missing:"+mesh.name;return false;}
                    providers.Add(index.ToString(CultureInfo.InvariantCulture)+":"+provider);
                }
                source.transform=root.worldToLocalMatrix*source.transform;sources[index]=source;
            }
            var local=new Bounds(bounds.center-origin,size);
            using(var sha=SHA256.Create())
            {
                using(var output=new CryptoStream(Stream.Null,sha,CryptoStreamMode.Write))
                    NativeNavigationGeometry.Write(output,sources,origin,local);
                hash=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
            }
            if(providers.Count>0)
                using(var sha=SHA256.Create())collisionProviders=BitConverter.ToString(
                    sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n",providers.ToArray())))).Replace("-","").ToLowerInvariant();
            reason=null;return true;
        }

        private static readonly FieldInfo LoadedAssets=typeof(Mod).GetField("loadedAssets",BindingFlags.Instance|BindingFlags.NonPublic);

        // Native MeshReplacement clones a cached GameObject and renames its
        // root. Its descendant colliders retain the prefab sharedMesh reference.
        // Follow only that native replacement's asset names and load priority;
        // never accept an unrelated cached prefab merely sharing a mesh name.
        private static bool TryProvider(Transform block,MeshCollider live,Mesh mesh,out string identity)
        {
            identity=null;if(!live || live.sharedMesh!=mesh || !ModManager.Instance || LoadedAssets==null)return false;
            Transform owner=live.transform;
            while(owner && owner!=block && !owner.name.EndsWith(" [Replacement]",StringComparison.Ordinal))owner=owner.parent;
            if(!owner || owner==block)return false;
            string[] names=ReplacementNames(owner.name);
            if(names==null)return false;
            var path=new List<int>();
            for(var part=live.transform;part!=owner;part=part.parent)path.Insert(0,part.GetSiblingIndex());
            var liveColliders=live.GetComponents<MeshCollider>();int colliderIndex=Array.IndexOf(liveColliders,live);
            foreach(var mod in ModManager.Instance.EnumerateModsReverse())
            {
                if(!mod.Enabled)continue;
                var cache=LoadedAssets.GetValue(mod) as Dictionary<string,LoadedAsset>;
                foreach(string name in names)
                {
                    string asset=ModManager.GetAssetName(name);GameObject prefab=null;LoadedAsset cached;
                    if(cache!=null && cache.TryGetValue(asset,out cached))prefab=cached.Obj as GameObject;
                    // If native cache maintenance evicted the prefab, resolve
                    // only this known asset, as MeshReplacement itself does.
                    if(!prefab && mod.AssetBundle && mod.AssetBundle.Contains(asset))prefab=mod.GetAsset<GameObject>(asset,false);
                    if(!prefab)continue;
                    var candidate=prefab.transform;
                    foreach(int child in path)
                    {if(child>=candidate.childCount)return false;candidate=candidate.GetChild(child);}
                    var colliders=candidate.GetComponents<MeshCollider>();
                    if(colliderIndex<0 || colliderIndex>=colliders.Length || colliders[colliderIndex].sharedMesh!=mesh)return false;
                    identity=ProviderIdentity(mod.GUID,mod.ModInfo.ModVersion,asset,path.ToArray(),colliderIndex);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_RDB_COLLISION_PROVIDER: mesh="+mesh.name+"; provider="+identity);
                    return true;
                }
            }
            return false;
        }

        private static string[] ReplacementNames(string owner)
        {
            var game=GameManager.Instance;if(!game || !game.PlayerGPS)return null;
            var climate=ClimateSwaps.FromAPIClimateBase(game.PlayerGPS.ClimateSettings.ClimateType);
            return ReplacementNames(owner,climate==ClimateBases.Desert?climate.ToString():
                climate.ToString()+DaggerfallUnity.Instance.WorldTime.Now.SeasonValue);
        }

        internal static string ProviderIdentity(string guid,string version,string asset,int[] path,int colliderIndex)
        {
            var parts=new List<string>();foreach(int child in path)parts.Add(child.ToString(CultureInfo.InvariantCulture));
            return guid+":"+version+":"+asset+":"+string.Join("/",parts.ToArray())+":"+
                colliderIndex.ToString(CultureInfo.InvariantCulture);
        }

        internal static string[] ReplacementNames(string owner,string modelSuffix)
        {
            const string suffix="] [Replacement]";
            const string mesh="DaggerfallMesh [ID=";
            const string billboard="DaggerfallBillboard [TEXTURE.";
            if(owner==null || !owner.EndsWith(suffix,StringComparison.Ordinal))return null;
            if(owner.StartsWith(mesh,StringComparison.Ordinal))
            {
                string id=owner.Substring(mesh.Length,owner.Length-mesh.Length-suffix.Length);
                return DecimalId(id)?new[]{id+"_"+modelSuffix,id}:null;
            }
            if(!owner.StartsWith(billboard,StringComparison.Ordinal))return null;
            string payload=owner.Substring(billboard.Length,owner.Length-billboard.Length-suffix.Length);
            var parts=payload.Split(new[]{", Index="},StringSplitOptions.None);
            int archive,record;
            if(parts.Length!=2 || !DecimalId(parts[0]) || !DecimalId(parts[1]) ||
                !int.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out archive) ||
                !int.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out record))return null;
            return new[]{archive.ToString("000",CultureInfo.InvariantCulture)+"_"+record.ToString(CultureInfo.InvariantCulture)};
        }
        private static bool DecimalId(string value)
        {
            if(value.Length==0)return false;
            foreach(char c in value)if(c<'0' || c>'9')return false;
            return true;
        }
    }
}
