using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Only the build-time complete selection is shipped. No discovery of work-in-progress art.
    public sealed class CompletedNpcArt : IDisposable
    {
        [Serializable] public sealed class Entry
        {
            public int archive,record,cellWidth,cellHeight,phases,referenceRecord;
            public float pivotX,pivotY,referenceHeight,fps;
            public string provider,action,file,sha256;
        }
        [Serializable] public sealed class Catalog {public int schemaVersion;public Entry[] entries;}
        public sealed class Atlas {public Texture2D Texture;public Entry Definition;}
        public static CompletedNpcArt Current {get;private set;}
        public static void Install(Mod mod){Shutdown();if(mod!=null)Current=new CompletedNpcArt(mod);}
        public static void Shutdown(){if(Current!=null)Current.Dispose();Current=null;}
        private readonly Mod mod;
        private readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
        private readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>();
        private readonly Dictionary<string,string> providers=new Dictionary<string,string>();
        private static string Key(int archive,int record,string action){return archive+":"+record+":"+action;}
        private CompletedNpcArt(Mod mod)
        {
            this.mod=mod;
            const string name="npc-complete-catalog.json.bytes";
            if(!mod.HasAsset(name))return;
            try
            {
                var catalog=(Catalog)SaveLoadManager.Deserialize(typeof(Catalog),mod.GetAsset<TextAsset>(name).text);
                if(catalog==null||catalog.schemaVersion!=1||catalog.entries==null)throw new ArgumentException("Invalid completed NPC catalogue");
                foreach(var e in catalog.entries)
                {
                    if(!Valid(e))throw new ArgumentException("Invalid complete action dimensions/identity");
                    entries.Add(Key(e.archive,e.record,e.action),e);
                }
            }
            catch(Exception error){entries.Clear();Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_COMPLETE_ART_UNAVAILABLE: "+error.Message);}
        }
        public static bool Valid(Entry e)
        {
            return e!=null&&e.archive>=0&&e.record>=0&&e.referenceRecord>=0&&
                (e.action=="idle"||e.action=="walk"||e.action=="run"||e.action=="melee"||e.action=="jump"||e.action=="climb"||e.action=="swim")&&
                e.phases==(e.action=="idle"?1:4)&&e.cellWidth>0&&e.cellWidth<=512&&e.cellHeight>0&&e.cellHeight<=512&&
                e.referenceHeight>0&&e.referenceHeight<=512&&e.fps>0&&e.fps<=30&&
                e.pivotX>=0&&e.pivotX<=e.cellWidth&&e.pivotY>=0&&e.pivotY<=e.cellHeight&&
                !string.IsNullOrEmpty(e.file)&&e.file.StartsWith("npc-complete-",StringComparison.Ordinal)&&
                e.file.IndexOf('/')<0&&e.file.IndexOf('\\')<0&&e.sha256!=null&&e.sha256.Length==64;
        }
        private string Provider(int archive,int record)
        {
            string key=Key(archive,record,"");string value;
            if(!providers.TryGetValue(key,out value)){value=SleepingArt.SourceProvider(archive,record);providers.Add(key,value);}
            return value;
        }
        public Atlas ForActor(ResidentActor actor,string action)
        {
            if(!actor||actor.Resident==null||string.IsNullOrEmpty(action))return null;
            int archive,record;
            if(actor.IsStationary)
            {
                if(actor.Resident.NativeIdentity==null)return null;
                archive=actor.Resident.NativeIdentity.TextureArchive;record=actor.Resident.NativeIdentity.TextureRecord;
            }
            else {archive=SittingArt.ArchiveForActor(actor);record=0;}
            Entry entry;if(archive<0||!entries.TryGetValue(Key(archive,record,action),out entry))return null;
            if(actor.IsStationary)
            {if(!NpcSpritePolicy.MatchesProvider(entry.provider,Provider(archive,record)))return null;}
            else for(int r=0;r<6;r++)if(!NpcSpritePolicy.MatchesProvider(entry.provider,Provider(archive,r)))return null;
            Texture2D texture;
            if(!textures.TryGetValue(entry.file,out texture))
            {
                textures[entry.file]=null;
                try
                {
                    if(!mod.HasAsset(entry.file))return null;
                    var bytes=mod.GetAsset<TextAsset>(entry.file).bytes;
                    using(var hash=SHA256.Create())
                        if(!string.Equals(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(),entry.sha256,StringComparison.Ordinal))
                            throw new ArgumentException("Atlas hash differs from complete selection: "+entry.file);
                    texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    if(!texture.LoadImage(bytes)||texture.width!=entry.cellWidth*entry.phases||texture.height!=entry.cellHeight*8)
                        throw new ArgumentException("Atlas dimensions differ: "+entry.file);
                    texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                    textures[entry.file]=texture;
                }
                catch(Exception error){if(texture)UnityEngine.Object.Destroy(texture);Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_COMPLETE_ART_UNAVAILABLE: "+error.Message);return null;}
            }
            return texture?new Atlas{Texture=texture,Definition=entry}:null;
        }
        public void Dispose(){foreach(var texture in textures.Values)if(texture)UnityEngine.Object.Destroy(texture);textures.Clear();entries.Clear();providers.Clear();}
    }
}
