using System.IO;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using UnityEngine;

// Read-only reference extraction: preserve native mobile idle art and inspect walking poses.
public static class StandingIdleSurvey20261016
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        string output = Path.Combine(root, "LivingCities/art/standing/source");
        string idleOutput = Path.Combine(output, "native-idle");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(idleOutput);
        var reader = new TextureReader(Path.Combine(root, "GameFiles/arena2"));
        string arena = Path.Combine(root, "GameFiles/arena2");
        int[] archives = {381,382,383,384,385,386,387,388,389,390,391,392,393,394,395,396,397,398,451,452,453,454,455,456};
        var idleManifest = new List<string> { "archive,record,frame,width,height,scaleX,scaleY,file" };
        foreach (int archive in archives)
        {
            for (int record = 0; record < 5; record++)
                for (int frame = 0; frame < 4; frame++)
                {
                    var texture = reader.GetTexture2D(new GetTextureSettings { archive = archive,
                        record = record, frame = frame, alphaIndex = 0, stayReadable = true }).albedoMap;
                    File.WriteAllBytes(Path.Combine(output, archive + "-" + record + "-" + frame + ".png"), texture.EncodeToPNG());
                    Object.DestroyImmediate(texture);
                }

            var source = new TextureFile(Path.Combine(arena, "TEXTURE." + archive), FileUsage.UseMemory, true);
            int idleRecord = 5;
            for (int frame = 0; frame < source.GetFrameCount(idleRecord); frame++)
            {
                string file = archive + "-" + frame + ".png";
                var texture = reader.GetTexture2D(new GetTextureSettings { archive = archive,
                    record = idleRecord, frame = frame, alphaIndex = 0, stayReadable = true }).albedoMap;
                File.WriteAllBytes(Path.Combine(idleOutput, file), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                var size = source.GetSize(idleRecord);
                var scale = source.GetScale(idleRecord);
                idleManifest.Add(string.Join(",", archive, idleRecord, frame, size.Width, size.Height,
                    scale.Width, scale.Height, "native-idle/" + file));
            }
        }
        File.WriteAllLines(Path.Combine(output, "native-idle.csv"), idleManifest);
        Debug.Log("LIVING_CITIES_STANDING_SOURCE_PASS: 24 archives; 5 walking directions; native idle record 5 extracted");
    }
}
