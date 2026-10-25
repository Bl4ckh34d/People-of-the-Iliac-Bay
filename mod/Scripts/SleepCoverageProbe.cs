using System;
using System.IO;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Utility.AssetInjection;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in native acceptance. Uses DFU's actual asset query as the precedence oracle.
    public static class SleepCoverageProbe
    {
        public static void Verify(StreetPopulation streets)
        {
            var art = (SleepingArt)typeof(StreetPopulation).GetField("sleepingArt", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(streets);
            if (art == null) throw new InvalidOperationException("Sleeping art is not initialized.");
            VerifyOne(art, 182, 33);
            VerifyOne(art, 1200, 14);
            if (SleepArtCatalog.Find(182, 33, "unknown-provider") != null ||
                art.ForNative(new NativePerson { TextureArchive = 99998, TextureRecord = 99998 }) != null)
                throw new InvalidOperationException("Unsupported sleeping appearance received a substitute.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SLEEP_PROVIDER_PASS: native enabled-provider precedence; replaced vanilla and overlapping imported archives; actual atlas decode/nonempty UV; repeated atlas reuse; unsupported=no fallback.");
        }

        private static void VerifyOne(SleepingArt art, int archive, int record)
        {
            string expected = NativeProvider(archive, record);
            string actual = SleepingArt.SourceProvider(archive, record);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sleeping provider differs from native asset precedence: " + archive + "/" + record);
            var person = new NativePerson { TextureArchive = archive, TextureRecord = record };
            var entry = SleepArtCatalog.Find(archive, record, actual);
            var atlas = art.ForNative(person);
            if (entry == null)
            {
                if (atlas != null) throw new InvalidOperationException("Unknown imported sleeper received a fallback.");
            }
            else
            {
                if (atlas == null || !atlas.Texture || atlas.Views == null || atlas.Views.Length != 1 ||
                    atlas.Views[0].width <= 0 || atlas.Views[0].height <= 0)
                    throw new InvalidOperationException("Supported imported sleeper has no loaded alpha footprint.");
                for (int i = 0; i < 10; i++)
                    if (!object.ReferenceEquals(atlas, art.ForNative(person)))
                        throw new InvalidOperationException("Repeated sleeper lookup did not reuse its atlas.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SLEEP_PROVIDER_CHECK: source=" + archive + "/" + record +
                "; provider=" + actual + "; file=" + (entry == null ? "unsupported" : entry.File) + "; nativePrecedence=pass");
        }

        private static string NativeProvider(int archive, int record)
        {
            if (!DaggerfallUnity.Settings.AssetInjection) return "";
            string name = archive + "_" + record + "-0.png";
            if (File.Exists(Path.Combine(TextureReplacement.TexturesPath, name))) return "unsupported-loose-file";
            Texture2D winner;
            var manager = ModManager.Instance;
            if (manager == null || !manager.TryGetAsset<Texture2D>(name, null, out winner)) return "";
            foreach (var source in manager.Mods)
                if (source.Enabled && source.HasAsset(name) && object.ReferenceEquals(source.GetAsset<Texture2D>(name, false), winner))
                    return source.ModInfo.GUID;
            throw new InvalidOperationException("Native imported texture has no identifiable enabled provider: " + name);
        }
    }
}
