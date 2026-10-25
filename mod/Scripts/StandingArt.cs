using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Eight authored views, including the unchanged native front. No mirroring.
    public sealed class StandingArt : IDisposable
    {
        [Serializable] public sealed class Manifest
        {
            public int archive;
            public int[] nativeIdleSize,cellSize;
        }
        public sealed class Atlas
        {
            public Texture2D Texture;
            public Manifest Definition;
            public readonly Rect[] Views=new Rect[8];
        }
        public static StandingArt Current {get;private set;}
        public static void Install(Mod mod){Shutdown();if(mod!=null)Current=new StandingArt(mod);}
        public static void Shutdown(){if(Current!=null)Current.Dispose();Current=null;}
        private readonly Mod mod;
        private readonly Dictionary<int,Atlas> atlases=new Dictionary<int,Atlas>();
        private StandingArt(Mod mod){this.mod=mod;}
        public Atlas ForActor(ResidentActor actor)
        {
            int archive=SittingArt.ArchiveForActor(actor);
            if(archive<0)return null;
            Atlas known;if(atlases.TryGetValue(archive,out known))return known;
            atlases[archive]=null;
            // A replacement appearance must keep its own art rather than change
            // identity whenever it stops. Unknown appearances use native fallback.
            if(SleepingArt.SourceProvider(archive,0)!=""||SleepingArt.SourceProvider(archive,5)!="")return null;
            Texture2D texture=null;
            try
            {
                string name="idle-"+archive;
                if(!mod.HasAsset(name+".json")||!mod.HasAsset(name+".png.bytes"))return null;
                var definition=ReadManifest(mod.GetAsset<TextAsset>(name+".json").text,archive);
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                if(!texture.LoadImage(mod.GetAsset<TextAsset>(name+".png.bytes").bytes)||
                    texture.width!=definition.cellSize[0]*4||texture.height!=definition.cellSize[1]*2)
                    throw new ArgumentException("Standing atlas dimensions do not match manifest.");
                texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                var result=new Atlas{Texture=texture,Definition=definition};
                for(int i=0;i<8;i++)result.Views[i]=new Rect(i%4*0.25f,0.5f-i/4*0.5f,0.25f,0.5f);
                atlases[archive]=result;return result;
            }
            catch(Exception error)
            {if(texture)UnityEngine.Object.Destroy(texture);Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_STANDING_ART_UNAVAILABLE: archive="+archive+"; "+error.Message);return null;}
        }
        public static Manifest ReadManifest(string json,int archive)
        {
            var value=(Manifest)SaveLoadManager.Deserialize(typeof(Manifest),json);
            if(value==null||value.archive!=archive||value.nativeIdleSize==null||value.nativeIdleSize.Length!=2||
                value.cellSize==null||value.cellSize.Length!=2||value.nativeIdleSize[0]<1||value.nativeIdleSize[1]<1||
                value.cellSize[0]<value.nativeIdleSize[0]||value.cellSize[0]>512||value.cellSize[1]>512||
                value.cellSize[1]!=value.nativeIdleSize[1])throw new ArgumentException("Invalid native-size standing atlas.");
            return value;
        }
        public void Dispose(){foreach(var value in atlases.Values)if(value!=null&&value.Texture)UnityEngine.Object.Destroy(value.Texture);atlases.Clear();}
    }
}
