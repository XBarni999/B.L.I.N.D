using System.IO;
using UnityEditor;
using UnityEngine;
public static class BuildThermal
{
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Assets"));
        Directory.CreateDirectory(output);
        var build = new AssetBundleBuild {
            assetBundleName = "blind-thermal.bundle",
            assetNames = new[] { "Assets/BlindThermal.shader" }
        };
        var result = BuildPipeline.BuildAssetBundles(output, new[] { build },
            BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);
        if (result == null) throw new System.Exception("Thermal shader bundle build failed");
        Debug.Log("BLIND_THERMAL_BUILD_OK " + output);
    }

    // Real GPU checks for palette isolation and highlight headroom (run without -nographics).
    public static void ValidatePalettes()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/BlindThermal.shader");
        var material = new Material(shader);
        var input = new Texture2D(6,1,TextureFormat.RGBAFloat,false,true);
        var output = new RenderTexture(6,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var readback = new Texture2D(6,1,TextureFormat.RGBAFloat,false,true);
        var previous = RenderTexture.active;
        float[] heat = { 0.10f,0.4f,0.8f,1.25f,2f,3f };
        try
        {
            for (int i=0;i<heat.Length;i++) input.SetPixel(i,0,new Color(heat[i],heat[i],heat[i],1));
            input.filterMode=FilterMode.Point;
            input.Apply();
            material.SetFloat("_Noise",0);
            material.SetFloat("_Span",1.25f);
            material.SetFloat("_WhiteHotCeiling",0.86f);
            Color[][] rows = new Color[3][];
            for (int mode=0;mode<3;mode++)
            {
                material.SetFloat("_Mode",mode);
                Graphics.Blit(input,output,material,3);
                RenderTexture.active=output;
                readback.ReadPixels(new Rect(0,0,6,1),0,0);
                readback.Apply();
                rows[mode]=readback.GetPixels();
            }
            for (int i=1;i<heat.Length;i++)
            {
                if (!(rows[1][i].r>rows[1][i-1].r)) throw new System.Exception("White Hot loses heat ordering/highlight detail");
                if (rows[2][i].r>rows[2][i-1].r+0.001f) throw new System.Exception("Black Hot polarity changed");
            }
            if (rows[1][5].r>=0.73f || rows[1][0].r>=0.03f) throw new System.Exception("White Hot background/highlight exceeds limit");
            if (rows[0][5].r<0.98f || rows[0][0].b<=rows[0][0].g) throw new System.Exception("Ironbow endpoints changed");
            // Ceiling changes must affect only White Hot.
            material.SetFloat("_WhiteHotCeiling",0.5f);
            foreach (int mode in new[] {0,2})
            {
                material.SetFloat("_Mode",mode);
                Graphics.Blit(input,output,material,3);
                RenderTexture.active=output;
                readback.ReadPixels(new Rect(0,0,6,1),0,0);
                readback.Apply();
                var actual=readback.GetPixels();
                for(int i=0;i<6;i++) if (((Vector4)actual[i]-(Vector4)rows[mode][i]).sqrMagnitude>0.000001f)
                    throw new System.Exception("White Hot setting leaked into another palette");
            }
            Debug.Log("BLIND_PALETTE_GPU_TEST_OK: monotonic heat, retained highlights, dark background, isolated palettes");
        }
        finally
        {
            RenderTexture.active=previous;
            output.Release();
            Object.DestroyImmediate(output); Object.DestroyImmediate(input);
            Object.DestroyImmediate(readback); Object.DestroyImmediate(material);
        }
    }
}
