using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Utility;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class NativeNavigationWorld : MonoBehaviour
    {
        public static NativeNavigationWorld Current {get;private set;}
        private Mod mod;
        private NativeNavigationManifest manifest;
        private Transform owner;
        private string scene;
        private string variant;
        private Vector3 origin;
        private int blockCount=-1;
        private float checkAt;
        private float bodiesAt;
        private readonly List<NavMeshDataInstance> instances=new List<NavMeshDataInstance>();
        private readonly List<NavMeshLinkInstance> links=new List<NavMeshLinkInstance>();
        public bool Ready {get{return instances.Count>0;}}
        public string Scene {get{return scene;}}
        public int ModuleCount {get{return instances.Count;}}
        public void Install(Mod source)
        {
            Current=this;mod=source;
            var text=mod.GetAsset<TextAsset>("navigation-manifest.json");
            manifest=text?NativeNavigationManifest.Read(text.text):new NativeNavigationManifest();
            if(manifest.version!=1)throw new InvalidDataException("Navigation manifest version.");
        }
        public static string Profile()
        {
            var names=new List<string>();
            foreach(var entry in ModManager.Instance.Mods)
                if(entry.Enabled && entry.ModInfo.ModTitle!="People of the Iliac Bay")names.Add(entry.ModInfo.GUID+":"+entry.ModInfo.ModVersion);
            return string.Join("|",names.ToArray());
        }
        public static string Variant()
        {
            var game=GameManager.Instance;
            if(!game || !game.PlayerGPS || !game.PlayerGPS.HasCurrentLocation || !game.StreamingWorld)return null;
            var climate=ClimateSwaps.FromAPIClimateBase(game.PlayerGPS.ClimateSettings.ClimateType);
            var season=DaggerfallUnity.Instance.WorldTime.Now.SeasonValue;
            return "climate="+((int)climate).ToString(CultureInfo.InvariantCulture)+
                ";season="+((int)season).ToString(CultureInfo.InvariantCulture)+
                ";assetInjection="+(DaggerfallUnity.Settings.AssetInjection?"1":"0")+
                ";terrainScale="+game.StreamingWorld.TerrainScale.ToString("R",CultureInfo.InvariantCulture);
        }
        private static bool Context(out Transform root,out string key)
        {
            root=null;key=null;var game=GameManager.Instance;
            if(!game || !game.PlayerEnterExit || !game.StreamingWorld)return false;
            if(game.PlayerEnterExit.IsPlayerInsideDungeon)
            {var dungeon=game.PlayerEnterExit.Dungeon;if(!dungeon)return false;root=dungeon.transform;key="dungeon-"+dungeon.Summary.ID;return true;}
            if(game.PlayerEnterExit.IsPlayerInside)
            {
                var interior=game.PlayerEnterExit.Interior;
                if(!interior || !game.PlayerGPS.HasCurrentLocation)return false;
                root=interior.transform;
                key="interior-"+game.PlayerGPS.CurrentLocation.MapTableData.MapId+"-"+interior.EntryDoor.buildingKey;
            }
            else
            {
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                if(!directory)return false;
                root=directory.transform;key="city-"+directory.MapID;
            }
            return true;
        }
        private void Update()
        {
            if(Time.unscaledTime<checkAt)return;checkAt=Time.unscaledTime+0.5f;
            Transform root;string key;
            if(!Context(out root,out key)){Clear();owner=null;scene=null;variant=null;return;}
            if(Time.unscaledTime>=bodiesAt)
            {
                bodiesAt=Time.unscaledTime+2;
                foreach(var person in UnityEngine.Object.FindObjectsOfType<MobilePersonNPC>())
                    if(!person.GetComponent<NativeNavigationBody>())person.gameObject.AddComponent<NativeNavigationBody>();
                var player=GameManager.Instance.PlayerMotor;
                if(player&&!player.GetComponent<NativeNavigationBody>())player.gameObject.AddComponent<NativeNavigationBody>();
            }
            int count=key.StartsWith("city-",StringComparison.Ordinal)?root.GetComponentsInChildren<DaggerfallRMBBlock>().Length:0;
            string currentVariant=Variant();
            string cacheVariant=key.StartsWith("city-",StringComparison.Ordinal)
                ?currentVariant+"|"+NativeNavigationTerrainTemplates.Variant():
                key.StartsWith("interior-",StringComparison.Ordinal)
                    ?currentVariant+"|"+NativeInteriorNavigationModules.Variant():currentVariant;
            if(root==owner && key==scene && cacheVariant==variant && root.position==origin && count==blockCount)return;
            Clear();owner=root;scene=key;variant=cacheVariant;origin=root.position;blockCount=count;
            if(manifest.profile!=Profile())
            {Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_PROFILE_MISMATCH: rebake changed mod geometry.");return;}
            if(string.IsNullOrEmpty(currentVariant))
            {Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_VARIANT_UNAVAILABLE: scene="+scene);return;}
            var selected=new List<NativeNavigationModule>();
            bool templatesOnly=Environment.GetEnvironmentVariable("POTIB_NAVMESH_TEMPLATES_ONLY")=="1";
            bool modulesOnly=Environment.GetEnvironmentVariable("POTIB_NAVMESH_MODULES_ONLY")=="1";
            foreach(var module in manifest.modules)
                if(module.scene==scene && module.variant==currentVariant && !templatesOnly && !modulesOnly)selected.Add(module);
            // Exact captures take precedence. A reusable city assembly requires
            // every native block variant and matching flat native terrain.
            bool templateAssembly=false;
            if(selected.Count==0 && scene.StartsWith("city-",StringComparison.Ordinal))
            {
                string refusal;
                if(!NativeNavigationTerrainTemplates.TrySelect(root,scene,manifest,out selected,out refusal))
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_TEMPLATE_REFUSED: scene="+scene+"; reason="+refusal);
                else templateAssembly=true;
            }
            if(selected.Count==0 && scene.StartsWith("interior-",StringComparison.Ordinal))
            {
                string refusal;
                if(!NativeInteriorNavigationModules.TrySelect(root,scene,manifest,out selected,out refusal))
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_TEMPLATE_REFUSED: scene="+scene+"; reason="+refusal);
                else templateAssembly=true;
            }
            if(selected.Count==0 && scene.StartsWith("dungeon-",StringComparison.Ordinal))
            {
                string refusal;
                if(!NativeDungeonNavigationModules.TrySelect(root,scene,manifest,out selected,out refusal))
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_RDB_REFUSED: scene="+scene+"; reason="+refusal);
                else templateAssembly=true;
            }
            if(selected.Count==0 && manifest.modules.Exists(module=>module.scene==scene))
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_VARIANT_MISMATCH: scene="+scene+"; variant="+currentVariant);
            if(scene.StartsWith("city-",StringComparison.Ordinal) &&
                selected.Count!=root.GetComponentsInChildren<DaggerfallRMBBlock>().Length)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INCOMPLETE: scene="+scene+"; modules="+selected.Count);
                return;
            }
            var loaded=new List<NativeNavigationModule>();
            foreach(var module in selected)
            {
                var data=mod.GetAsset<NavMeshData>(module.asset);
                if(!data)continue;
                instances.Add(NavMesh.AddNavMeshData(data,owner.TransformPoint(module.position),owner.rotation));loaded.Add(module);
            }
            if(loaded.Count!=selected.Count)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_ASSET_MISSING: scene="+scene+"; loaded="+loaded.Count+"; expected="+selected.Count);
                Clear();return;
            }
            if(Ready)
            {
                if(scene.StartsWith("dungeon-",StringComparison.Ordinal) && templateAssembly)
                    foreach(var link in NativeDungeonNavigationSeams.Collect(owner))links.Add(NavMesh.AddLink(link));
                else Connect(loaded);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_LOADED: scene="+scene+"; modules="+instances.Count+"; links="+links.Count+
                    "; runtimeBake=false; reusableTemplates="+templateAssembly);
            }
        }
        private static string BlockName(string name)
        {
            const string prefix="DaggerfallBlock [";
            return name.StartsWith(prefix,StringComparison.Ordinal) && name.EndsWith("]",StringComparison.Ordinal)
                ?name.Substring(prefix.Length,name.Length-prefix.Length-1):null;
        }
        private void Connect(List<NativeNavigationModule> modules)
        {
            modules.Sort((a,b)=>{int x=a.position.x.CompareTo(b.position.x);return x!=0?x:a.position.z.CompareTo(b.position.z);});
            for(int i=0;i<modules.Count;i++)for(int j=i+1;j<modules.Count;j++)
            {
                var a=modules[i];var b=modules[j];var difference=b.position-a.position;
                bool east=Mathf.Abs(difference.x-a.size.x)<0.2f&&Mathf.Abs(difference.z)<0.2f;
                bool north=Mathf.Abs(difference.z-a.size.z)<0.2f&&Mathf.Abs(difference.x)<0.2f;
                if(!east&&!north)continue;
                float length=east?a.size.z:a.size.x;
                for(float along=2;along<length;along+=4)
                {
                    var middle=owner.TransformPoint(a.position+(east?new Vector3(a.size.x,0,along):new Vector3(along,0,a.size.z)));
                    var across=owner.TransformDirection(east?Vector3.right:Vector3.forward);
                    NavMeshHit start,end;
                    // Street height comes from the existing native ground query.
                    RaycastHit floor;if(Physics.Raycast(middle+Vector3.up*60,Vector3.down,out floor,120,~0,QueryTriggerInteraction.Ignore))middle.y=floor.point.y;
                    if(!NavMesh.SamplePosition(middle-across*0.55f,out start,1,NavMesh.AllAreas)||
                        !NavMesh.SamplePosition(middle+across*0.55f,out end,1,NavMesh.AllAreas)||Mathf.Abs(start.position.y-end.position.y)>0.5f)continue;
                    if(Vector3.Distance(start.position,end.position)>2 || !ClearSeam(start.position,end.position))continue;
                    var link=new NavMeshLinkData {startPosition=start.position,endPosition=end.position,width=1.5f,bidirectional=true,
                        costModifier=-1,agentTypeID=NativeNavigationGeometry.Settings().agentTypeID,area=0};
                    links.Add(NavMesh.AddLink(link));
                }
            }
        }
        private static bool ClearSeam(Vector3 from,Vector3 to)
        {
            var delta=to-from;
            foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*0.8f,from+Vector3.up*1.6f,0.30f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(hit.collider && !(hit.collider is TerrainCollider) && !hit.collider.GetComponentInParent<MobilePersonNPC>())return false;
            return true;
        }
        private void Clear()
        {
            foreach(var link in links)if(link.valid)link.Remove();links.Clear();
            foreach(var instance in instances)if(instance.valid)instance.Remove();instances.Clear();
        }
        private void OnDestroy(){Clear();if(Current==this)Current=null;}
        public static IEnumerator ExportLoadedScene(string folder)
        {
            Transform root;string sceneKey;if(!Context(out root,out sceneKey))throw new InvalidOperationException("No loaded navigation scene.");
            yield return ExportRoot(root,sceneKey,folder,Variant());
        }
        // Export the same native NavMesh input from a temporary, scoped root.
        // Only exact scene keys are selected by the normal runtime loader.
        public static IEnumerator ExportRoot(Transform root,string sceneKey,string folder,string variant)
        {
            bool validVariant=variant==Variant() || sceneKey!=null &&
                sceneKey.StartsWith("interior-template:",StringComparison.Ordinal) &&
                variant==NativeInteriorNavigationModules.Variant();
            if(!root || string.IsNullOrWhiteSpace(sceneKey) || string.IsNullOrWhiteSpace(folder) ||
                string.IsNullOrWhiteSpace(variant) || !validVariant)
                throw new InvalidOperationException("Navigation export requires a root, scene, folder, and current geometry variant.");
            Directory.CreateDirectory(folder);
            string manifestFile=Path.Combine(folder,"navigation-manifest.json");
            var result=File.Exists(manifestFile)?NativeNavigationManifest.Read(File.ReadAllText(manifestFile)):new NativeNavigationManifest();
            string profile=Profile();if(result.modules.Count>0&&result.profile!=profile)throw new InvalidOperationException("Navigation export mixes geometry mod profiles.");
            result.profile=profile;result.modules.RemoveAll(m=>m.scene==sceneKey &&
                (m.variant==variant || string.IsNullOrEmpty(m.variant)));
            var blocks=root.GetComponentsInChildren<DaggerfallRMBBlock>();
            bool city=blocks.Length>0 && sceneKey.StartsWith("city-",StringComparison.Ordinal);
            var origins=new List<Vector3>();var sizes=new List<Vector3>();
            if(city)
                foreach(var block in blocks){origins.Add(block.transform.position);sizes.Add(new Vector3(DaggerfallWorkshop.Utility.RMBLayout.RMBSide,128,DaggerfallWorkshop.Utility.RMBLayout.RMBSide));}
            else
            {
                // Interiors rotate with their native entrance. Store one module
                // in root-local coordinates; the loader applies that rotation once.
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
                bounds.Expand(2);origins.Add(bounds.min);sizes.Add(bounds.size);
            }
            for(int i=0;i<origins.Count;i++)
            {
                var size=sizes[i];var bounds=new Bounds(origins[i]+new Vector3(size.x/2,0,size.z/2),size);
                if(!city)bounds=new Bounds(origins[i]+size/2,size);
                var local=new Bounds(bounds.center-origins[i],bounds.size);
                string temporary=Path.Combine(folder,"module-input.tmp");
                var sources=city?NativeNavigationGeometry.Collect(bounds):NativeNavigationGeometry.Collect(root);
                if(sources.Count==0)throw new InvalidOperationException("Navigation export has no native collider sources: "+sceneKey);
                if(!city)
                    for(int sourceIndex=0;sourceIndex<sources.Count;sourceIndex++)
                    {
                        var source=sources[sourceIndex];
                        source.transform=root.worldToLocalMatrix*source.transform;
                        sources[sourceIndex]=source;
                    }
                NativeNavigationGeometry.Write(temporary,sources,origins[i],local);
                string hash;using(var sha=SHA256.Create())using(var stream=File.OpenRead(temporary))hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
                string name="nav-"+hash;string geometry=name+".navsource";
                File.Copy(temporary,Path.Combine(folder,geometry),true);File.Delete(temporary);
                result.modules.Add(new NativeNavigationModule {scene=sceneKey,variant=variant,asset=name+".asset",geometry=geometry,
                    position=city?root.InverseTransformPoint(origins[i]):origins[i],size=size});
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_EXPORT_MODULE: scene="+sceneKey+"; module="+(i+1)+"/"+origins.Count+"; asset="+name);
                yield return null;
            }
            File.WriteAllText(manifestFile,result.Write(),new UTF8Encoding(false));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_EXPORT_PASS: scene="+sceneKey+"; modules="+origins.Count+"; folder="+folder);
        }
    }
}
