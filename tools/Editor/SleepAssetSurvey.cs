using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using UnityEngine;

// Read-only extraction of installed source assets; not a runtime mod asset.
public static class SleepAssetSurvey
{
    [Serializable] private class Geometry { public uint model; public Vector3[] vertices, normals; public int[] triangles; }
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        string arena = Path.Combine(root, "GameFiles/arena2");
        string output = Path.Combine(root, "LivingCities/work/sleep-source");
        Directory.CreateDirectory(output);
        var flats = new FlatsFile(Path.Combine(arena, FlatsFile.Filename), FileUsage.UseMemory, true);
        var textures = new TextureReader(arena);
        var rows = new List<string> { "kind,archive,record,width,height,child,gender,caption,sleepStatus" };
        foreach (var flat in flats.FlatsDict.Values.OrderBy(f => f.archive).ThenBy(f => f.record))
        {
            var texture = ReadTexture(textures, flat.archive, flat.record);
            if (texture == null) throw new InvalidOperationException("Unreadable flat " + flat.archive + "/" + flat.record);
            rows.Add(string.Join(",", "flat", flat.archive, flat.record, texture.width, texture.height,
                TextureReader.IsChildNPCTexture(flat.archive, flat.record), flat.gender,
                "\"" + flat.caption.Replace("\"", "\"\"") + "\"", "pending"));
            File.WriteAllBytes(Path.Combine(output, "flat-" + flat.archive + "-" + flat.record + ".png"), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
        int[] mobile = { 381,382,383,384,395,396,397,398,387,388,389,390,392,393,451,452,385,386,391,394,453,454,455,456,399 };
        foreach (int archive in mobile)
        {
            int record = archive == 399 ? 15 : 5;
            var texture = ReadTexture(textures, archive, record);
            rows.Add(string.Join(",", "mobile", archive, record, texture.width, texture.height, false, "", "classic mobile appearance", "pending"));
            File.WriteAllBytes(Path.Combine(output, "mobile-" + archive + ".png"), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
        File.WriteAllLines(Path.Combine(output, "appearances.csv"), rows);
        var meshes = DaggerfallUnity.Instance.MeshReader;
        for (uint id = 41000; id <= 41002; id++)
        {
            ModelData data;
            if (!meshes.GetModelData(id, out data)) throw new InvalidOperationException("Bed model missing: " + id);
            File.WriteAllText(Path.Combine(output, "bed-" + id + ".json"), JsonUtility.ToJson(new Geometry {
                model = id, vertices = data.Vertices, normals = data.Normals, triangles = data.Indices }, true));
        }
        Debug.Log("LIVING_CITIES_SLEEP_SURVEY_PASS: flatReferences=" + flats.FlatsDict.Count + "; mobileReferences=" + mobile.Length + "; beds=3");
    }
    private static Texture2D ReadTexture(TextureReader reader, int archive, int record)
    {
        return reader.GetTexture2D(new GetTextureSettings { archive = archive, record = record,
            alphaIndex = 0, stayReadable = true }).albedoMap;
    }
}
