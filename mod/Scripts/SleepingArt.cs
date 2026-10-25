using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Utility.AssetInjection;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class SleepingArt : IDisposable
    {
        public sealed class Atlas { public Texture2D Texture; public Rect[] Views; }
        private readonly Mod mod;
        private readonly Dictionary<SleepArtCatalog.Entry, Atlas> atlases = new Dictionary<SleepArtCatalog.Entry, Atlas>();
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Rect[]> footprints = new Dictionary<string, Rect[]>();
        // DFU fixes enabled mods and load order for the running game. Resolve each
        // source once for this art owner's lifetime, including unsupported sources.
        private readonly Dictionary<long, SleepArtCatalog.Entry> resolvedEntries = new Dictionary<long, SleepArtCatalog.Entry>();
        public SleepingArt(Mod mod) { this.mod = mod; }
        public Atlas ForNative(NativePerson person)
        {
            return person==null?null:LoadEntry(Resolve(person.TextureArchive,person.TextureRecord));
        }
        public Atlas ForResident(Resident resident)
        {
            if (resident == null) return null;
            if (resident.NativeIdentity != null) return ForNative(resident.NativeIdentity);
            var original = SleepArtCatalog.ForResident(resident);
            return original == null ? null : LoadEntry(Resolve(original.Archive, original.Record));
        }
        private SleepArtCatalog.Entry Resolve(int archive, int record)
        {
            long key = ((long)archive << 32) | (uint)record;
            SleepArtCatalog.Entry entry;
            if (resolvedEntries.TryGetValue(key, out entry)) return entry;
            entry = SleepArtCatalog.Find(archive, record, SourceProvider(archive, record));
            resolvedEntries.Add(key, entry);
            return entry;
        }
        // Match native texture import precedence: loose files first, then enabled mods
        // in reverse load order. Unknown replacement art has no substitute face.
        public static string SourceProvider(int archive, int record)
        {
            if (!DaggerfallUnity.Settings.AssetInjection) return "";
            string name = archive + "_" + record + "-0.png";
            if (File.Exists(Path.Combine(TextureReplacement.TexturesPath, name))) return "unsupported-loose-file";
            if (ModManager.Instance == null) return "";
            foreach (var source in ModManager.Instance.EnumerateModsReverse())
            {
                if (!source.Enabled || !source.HasAsset(name)) continue;
                // HasAsset can also contain a same-named non-texture asset.
                if (source.GetAsset<Texture2D>(name, false) != null) return source.ModInfo.GUID;
            }
            return "";
        }
        private Atlas LoadEntry(SleepArtCatalog.Entry entry)
        {
            if(entry==null) return null;
            Atlas atlas;
            if (atlases.TryGetValue(entry, out atlas)) return atlas;
            Texture2D texture;
            if(!textures.TryGetValue(entry.File,out texture))
            {
                var asset=mod.GetAsset<TextAsset>(entry.File);
                if(!asset) throw new InvalidOperationException("Sleeping atlas missing: "+entry.File);
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                try
                {
                    if(!texture.LoadImage(asset.bytes)) throw new InvalidOperationException("Invalid sleeping PNG: "+entry.File);
                    texture.filterMode=FilterMode.Point; texture.wrapMode=TextureWrapMode.Clamp;
                    textures.Add(entry.File,texture);
                }
                catch { UnityEngine.Object.Destroy(texture); throw; }
            }
            Rect[] bounds;
            if(!footprints.TryGetValue(entry.File,out bounds))
            {
                bounds=entry.Cell<0?new[]{Footprint(texture)}:GridFootprints(texture);
                footprints.Add(entry.File,bounds);
            }
            var footprint=bounds[entry.Cell<0?0:entry.Cell];
            if(footprint.width==0) throw new InvalidOperationException("Sleeping atlas cell is empty: "+entry.Cell);
            atlas=new Atlas {Texture=texture,Views=new[]{footprint}};
            atlases.Add(entry,atlas);
            return atlas;
        }
        public static Rect GridFootprint(Texture2D texture,int cell)
        {
            if(cell<0 || cell>=8) throw new ArgumentOutOfRangeException("cell");
            var result=GridFootprints(texture)[cell];
            if(result.width==0) throw new InvalidOperationException("Sleeping atlas cell is empty: "+cell);
            return result;
        }
        // Scan a shared sheet once; the final sheet may contain unused cells.
        public static Rect[] GridFootprints(Texture2D texture)
        {
            var pixels=texture.GetPixels32();
            var results=new Rect[8];
            for(int cell=0;cell<8;cell++)
                results[cell]=CellFootprint(texture,pixels,cell);
            return results;
        }
        private static Rect CellFootprint(Texture2D texture,Color32[] pixels,int cell)
        {
            int x0=(cell%4)*texture.width/4,x1=(cell%4+1)*texture.width/4;
            int y0=(1-cell/4)*texture.height/2,y1=(2-cell/4)*texture.height/2;
            int left=x1,right=-1,bottom=y1,top=-1;
            for(int y=y0;y<y1;y++) for(int x=x0;x<x1;x++)
                if(pixels[y*texture.width+x].a>=128)
                {left=Math.Min(left,x);right=Math.Max(right,x);bottom=Math.Min(bottom,y);top=Math.Max(top,y);}
            if(right-left<5 || top-bottom<5) return new Rect();
            return new Rect((float)left/texture.width,(float)bottom/texture.height,
                (float)(right-left+1)/texture.width,(float)(top-bottom+1)/texture.height);
        }
        // Read atlas transparency to locate separated views. UV selection does not
        // modify/resample the generated PNG. Returned order is top row to bottom.
        public static Rect[] FindViews(Texture2D texture)
        {
            var pixels = texture.GetPixels32(); var views = new List<Rect>();
            int start = -1, left = texture.width, right = -1;
            for (int y = 0; y <= texture.height; y++)
            {
                bool opaque = false;
                if (y < texture.height)
                    for (int x = 0; x < texture.width; x++)
                        if (pixels[y * texture.width + x].a >= 128)
                        { opaque = true; left = Math.Min(left, x); right = Math.Max(right, x); }
                if (opaque && start < 0) start = y;
                if (!opaque && start >= 0)
                {
                    if (y - start >= 5 && right - left >= 5)
                        views.Add(new Rect((float)left / texture.width, (float)start / texture.height,
                            (float)(right - left + 1) / texture.width, (float)(y - start) / texture.height));
                    start = -1; left = texture.width; right = -1;
                }
            }
            if (views.Count != 5) throw new InvalidOperationException("Sleeping atlas must contain five separated opaque views; found " + views.Count);
            views.Reverse(); return views.ToArray();
        }
        public static Rect Footprint(Texture2D texture)
        {
            var pixels=texture.GetPixels32(); int left=texture.width,right=-1,bottom=texture.height,top=-1;
            for (int y=0;y<texture.height;y++) for (int x=0;x<texture.width;x++)
                if (pixels[y*texture.width+x].a>=128)
                { left=Mathf.Min(left,x); right=Mathf.Max(right,x); bottom=Mathf.Min(bottom,y); top=Mathf.Max(top,y); }
            if (right-left<5 || top-bottom<5) throw new InvalidOperationException("Overhead sleeper has no visible footprint.");
            return new Rect((float)left/texture.width,(float)bottom/texture.height,(float)(right-left+1)/texture.width,(float)(top-bottom+1)/texture.height);
        }
        public void Dispose()
        {
            foreach (var texture in textures.Values) UnityEngine.Object.Destroy(texture);
            textures.Clear();
            footprints.Clear();
            atlases.Clear();
            resolvedEntries.Clear();
        }
    }
}
