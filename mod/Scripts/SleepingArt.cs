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
        private Atlas maleBreton0;
        public SleepingArt(Mod mod) { this.mod = mod; }
        public Atlas ForResident(Resident resident)
        {
            // Explicit identity coverage. Never borrow another person's sleeping face.
            if (resident.AgeGroup != ResidentAgeGroup.Adult || resident.Sex != ResidentSex.Male ||
                ((resident.AppearanceSeed >> 1) % 4) != 0) return null;
            if (maleBreton0 != null) return maleBreton0;
            var asset = mod.GetAsset<TextAsset>("sleep-mobile-385.png.bytes");
            if (!asset) throw new InvalidOperationException("Sleeping atlas missing from mod bundle.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(asset.bytes)) throw new InvalidOperationException("Sleeping atlas is not a valid PNG.");
                texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
                maleBreton0 = new Atlas { Texture = texture, Views = FindViews(texture) };
                return maleBreton0;
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
        { if (maleBreton0 != null) UnityEngine.Object.Destroy(maleBreton0.Texture); maleBreton0 = null; }
    }
}
