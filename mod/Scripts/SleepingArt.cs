using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class SleepingArt : IDisposable
    {
        public sealed class Atlas { public Texture2D Texture; public Rect[] Views; }
        private readonly Mod mod;
        private readonly Dictionary<int, Atlas> atlases = new Dictionary<int, Atlas>();
        public SleepingArt(Mod mod) { this.mod = mod; }
        public Atlas ForNative(NativePerson person)
        {
            if (person.Sex != ResidentSex.Male || person.TextureArchive != 182 || (person.TextureRecord != 0 && person.TextureRecord != 44)) return null;
            return LoadAtlas(182000 + person.TextureRecord, "sleep-flat-182-" + person.TextureRecord + ".png.bytes", true);
        }
        public Atlas ForResident(Resident resident)
        {
            // Explicit identity coverage. Never borrow another person's sleeping face.
            if (resident.AgeGroup != ResidentAgeGroup.Adult ||
                ((resident.AppearanceSeed >> 1) % 4) != 0) return null;
            // ResidentActor currently uses Breton billboards; variant zero is
            // archive 385 for men and 453 for women in DFU's native mapping.
            int archive = resident.Sex == ResidentSex.Male ? 385 : 453;
            return LoadAtlas(archive, "sleep-mobile-" + archive + ".png.bytes");
        }
        private Atlas LoadAtlas(int archive, string filename, bool headFirst = false)
        {
            Atlas atlas;
            if (atlases.TryGetValue(archive, out atlas)) return atlas;
            var asset = mod.GetAsset<TextAsset>(filename);
            if (!asset) throw new InvalidOperationException("Sleeping atlas missing from mod bundle.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(asset.bytes)) throw new InvalidOperationException("Sleeping atlas is not a valid PNG.");
                texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
                var views = FindViews(texture);
                // SleepView expects foot-to-head ordering. Native atlases were generated head first.
                if (headFirst) Array.Reverse(views);
                atlas = new Atlas { Texture = texture, Views = views };
                atlases.Add(archive, atlas);
                return atlas;
            }
            catch { UnityEngine.Object.Destroy(texture); throw; }
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
        public void Dispose()
        {
            foreach (var atlas in atlases.Values) UnityEngine.Object.Destroy(atlas.Texture);
            atlases.Clear();
        }
    }
}
