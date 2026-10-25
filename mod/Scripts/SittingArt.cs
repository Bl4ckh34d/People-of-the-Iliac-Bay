using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Utility.AssetInjection;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Optional art. Missing or unsupported appearances retain their native sprite.
    public sealed class SittingArt : IDisposable
    {
        // The seated drawings fit a native canvas but their bodies are smaller
        // inside it. Art calibration is independent of chair height and bearing.
        public const double BodyScale = 1.25;
        [Serializable] public sealed class Frame
        {
            public int orientation;
            public int[] rect;
            public float[] groundPivot,seatPivotEstimate;
        }
        [Serializable] public sealed class Manifest
        {
            public int archive,framesPerDirection;
            public bool animationLoop;
            public int[] nativeIdleSize,nativeIdleScale,cellSize,atlasSize;
            public Frame[] frames;
        }
        public sealed class Atlas
        {
            public Texture2D Texture;
            public Manifest Definition;
            public Frame[] Frames;
            public Rect[] Views;
            public double PixelScale;
        }
        public static SittingArt Current {get;private set;}
        public static void Install(Mod mod) {Shutdown();if(mod!=null)Current=new SittingArt(mod);}
        public static void Shutdown() {if(Current!=null)Current.Dispose();Current=null;}
        private readonly Mod mod;
        private static readonly int[][] Male={new[]{385,386,391,394},new[]{381,382,383,384},new[]{387,388,389,390}};
        private static readonly int[][] Female={new[]{453,454,455,456},new[]{395,396,397,398},new[]{392,393,451,452}};
        private readonly Dictionary<int,Atlas> atlases=new Dictionary<int,Atlas>();
        public SittingArt(Mod mod) {this.mod=mod;}
        public static int ArchiveForActor(ResidentActor actor)
        {
            if(!actor||!actor.Person||!(actor.Person.Asset is MobilePersonBillboard)||actor.Person.IsGuard||actor.Resident.AgeGroup!=ResidentAgeGroup.Adult)return -1;
            int variant=actor.Person.PersonOutfitVariant;if(variant<0||variant>3)return -1;
            int race=actor.Person.Race==Races.Redguard?1:actor.Person.Race==Races.Nord?2:0;
            return (actor.Person.Gender==Genders.Male?Male:Female)[race][variant];
        }
        public Atlas ForActor(ResidentActor actor)
        {
            int archive=ArchiveForActor(actor);if(archive<0)return null;
            Atlas known;if(atlases.TryGetValue(archive,out known))return known;
            atlases[archive]=null;
            if(SleepingArt.SourceProvider(archive,0)!=""||SleepingArt.SourceProvider(archive,5)!="")return null;
            Texture2D texture=null;
            try
            {
                string imageName="sit-"+archive+".png.bytes",manifestName="sit-"+archive+".json";
                if(!mod.HasAsset(imageName)||!mod.HasAsset(manifestName))return null;
                var image=mod.GetAsset<TextAsset>(imageName);var manifest=mod.GetAsset<TextAsset>(manifestName);
                if(!image||!manifest)return null;
                var definition=ReadManifest(manifest.text,archive);
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                if(!texture.LoadImage(image.bytes)||texture.width!=definition.atlasSize[0]||texture.height!=definition.atlasSize[1])throw new ArgumentException("Sitting atlas dimensions do not match manifest.");
                texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                var nativePixels=new Vector2(
                    (float)(definition.nativeIdleSize[0]*SittingView.NativePixelScale(definition.nativeIdleSize[0],definition.nativeIdleScale[0],BlocksFile.ScaleDivisor,1)),
                    (float)(definition.nativeIdleSize[1]*SittingView.NativePixelScale(definition.nativeIdleSize[1],definition.nativeIdleScale[1],BlocksFile.ScaleDivisor,1)));
                // Match DFU's XML-only size overrides as well as base texture scale.
                TextureReplacement.SetBillboardScale(archive,5,ref nativePixels);
                var result=new Atlas {Texture=texture,Definition=definition,Frames=new Frame[8],Views=new Rect[8],
                    PixelScale=nativePixels.y*MeshReader.GlobalScale/definition.nativeIdleSize[1]*BodyScale};
                foreach(var frame in definition.frames)
                {
                    int i=frame.orientation;result.Frames[i]=frame;
                    result.Views[i]=new Rect(frame.rect[0]/(float)texture.width,1-(frame.rect[1]+frame.rect[3])/(float)texture.height,frame.rect[2]/(float)texture.width,frame.rect[3]/(float)texture.height);
                }
                atlases[archive]=result;return result;
            }
            catch(Exception error)
            {if(texture)UnityEngine.Object.Destroy(texture);Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SITTING_ART_UNAVAILABLE: archive="+archive+"; "+error.Message);return null;}
        }
        public static Manifest ReadManifest(string json,int archive)
        {
            // JsonUtility loses nested Frame[] on types loaded from DFU's runtime
            // mod DLL even though the same type works in the editor assembly.
            var definition=(Manifest)SaveLoadManager.Deserialize(typeof(Manifest),json);
            Validate(definition,archive);
            return definition;
        }
        public static void Validate(Manifest definition,int archive)
        {
            if(definition==null||definition.archive!=archive||definition.framesPerDirection!=1||definition.animationLoop||
                definition.cellSize==null||definition.cellSize.Length!=2||definition.atlasSize==null||definition.atlasSize.Length!=2||
                definition.nativeIdleSize==null||definition.nativeIdleSize.Length!=2||definition.nativeIdleSize[0]!=definition.cellSize[0]||definition.nativeIdleSize[1]!=definition.cellSize[1]||
                definition.nativeIdleScale==null||definition.nativeIdleScale.Length!=2||
                definition.cellSize[0]<1||definition.cellSize[1]<1||definition.cellSize[0]>512||definition.cellSize[1]>512||
                definition.atlasSize[0]!=definition.cellSize[0]*4||definition.atlasSize[1]!=definition.cellSize[1]*2||
                definition.frames==null||definition.frames.Length!=8)throw new ArgumentException("Sitting art requires eight single frames in a four by two native-pixel atlas.");
            var seen=new HashSet<int>();
            foreach(var frame in definition.frames)
            {
                if(frame==null||frame.orientation<0||frame.orientation>7||!seen.Add(frame.orientation)||frame.rect==null||frame.rect.Length!=4||
                    frame.groundPivot==null||frame.groundPivot.Length!=2||frame.seatPivotEstimate==null||frame.seatPivotEstimate.Length!=2||
                    frame.rect[0]!=frame.orientation%4*definition.cellSize[0]||frame.rect[1]!=frame.orientation/4*definition.cellSize[1]||
                    frame.rect[2]!=definition.cellSize[0]||frame.rect[3]!=definition.cellSize[1])throw new ArgumentException("Invalid sitting frame rectangle or pivots.");
                SittingView.Align(definition.cellSize[0],definition.cellSize[1],frame.seatPivotEstimate[0],frame.seatPivotEstimate[1],frame.groundPivot[1],0.5,
                    SittingView.NativePixelScale(definition.nativeIdleSize[1],definition.nativeIdleScale[1],BlocksFile.ScaleDivisor,MeshReader.GlobalScale));
            }
        }
        public void Dispose() {foreach(var atlas in atlases.Values)if(atlas!=null&&atlas.Texture)UnityEngine.Object.Destroy(atlas.Texture);atlases.Clear();}
    }
}
