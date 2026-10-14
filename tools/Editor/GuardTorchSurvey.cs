using System.IO;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using UnityEngine;

// Read-only references for choosing an actual classic torch rather than a brazier.
public static class GuardTorchSurvey
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        string output = Path.Combine(root,"LivingCities/work/torch-source");
        Directory.CreateDirectory(output);
        var reader = new TextureReader(Path.Combine(root,"GameFiles/arena2"));
        foreach (int record in new[] { 0,1,6,16,17,18,19,20 })
        {
            var texture = reader.GetTexture2D(new GetTextureSettings { archive=210,record=record,
                alphaIndex=0,stayReadable=true }).albedoMap;
            File.WriteAllBytes(Path.Combine(output,"torch-210-"+record+".png"),texture.EncodeToPNG());
            Debug.Log("LIVING_CITIES_TORCH_REFERENCE: record="+record+"; size="+texture.width+"x"+texture.height);
            Object.DestroyImmediate(texture);
        }
        Debug.Log("LIVING_CITIES_TORCH_SURVEY_PASS: eight native light references extracted.");
    }
}
