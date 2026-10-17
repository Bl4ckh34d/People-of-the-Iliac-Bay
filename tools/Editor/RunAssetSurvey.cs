using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using UnityEngine;

// Read-only native source extraction for the archive 385 running-art pilot.
public static class RunAssetSurvey
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        string arena = Path.Combine(root, "GameFiles/arena2");
        string output = Path.Combine(root, "LivingCities/art/run/pilot-385/source");
        Directory.CreateDirectory(output);
        var source = new TextureFile(Path.Combine(arena, "TEXTURE.385"), FileUsage.UseMemory, true);
        var reader = new TextureReader(arena);
        var rows = new List<string> { "record,frames,width,height,scaleX,scaleY" };
        for (int record = 0; record <= 5; record++)
        {
            var size = source.GetSize(record);
            var scale = source.GetScale(record);
            rows.Add(string.Join(",", record, source.GetFrameCount(record), size.Width, size.Height, scale.Width, scale.Height));
            for (int frame = 0; frame < source.GetFrameCount(record); frame++)
            {
                var texture = reader.GetTexture2D(new GetTextureSettings { archive = 385, record = record,
                    frame = frame, alphaIndex = 0, stayReadable = true }).albedoMap;
                File.WriteAllBytes(Path.Combine(output, "385-" + record + "-" + frame + ".png"), texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        File.WriteAllLines(Path.Combine(output, "records.csv"), rows);
        Debug.Log("LIVING_CITIES_RUN_SOURCE_PASS: archive=385; records=6");
    }
}
