#if BLIND_DIAGNOSTICS
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BLIND
{
    // Opt-in local regression runner. Entire class is absent from distributable builds.
    internal sealed class ThermalRuntimeProbe : MonoBehaviour
    {
        private IEnumerator Start()
        {
            for(int i=0;i<150;i++)
            {
                yield return new WaitForSecondsRealtime(1);
                if(Resources.FindObjectsOfTypeAll<Missile>().Length>8 && GameAssets.i!=null) break;
            }
            yield return new WaitForSecondsRealtime(8);
            try { Run(); }
            catch(Exception e) { BlindPlugin.LogSource.LogError("BLIND_RUNTIME_TEST_FAILED " + e); }
            yield return null;
            Application.Quit();
        }

        private void Run()
        {
            string output=Path.Combine(Path.GetDirectoryName(typeof(BlindPlugin).Assembly.Location),"BLIND-test-output");
            Directory.CreateDirectory(output);
            var warheadField=AccessTools.Field(typeof(Missile),"warhead");
            var effectField=AccessTools.Field(typeof(Missile.Warhead),"terrainEffect");
            GameObject prefab=null;
            string missileName="";
            foreach(var m in Resources.FindObjectsOfTypeAll<Missile>())
            {
                var warhead=warheadField.GetValue(m);
                var effect=warhead==null?null:effectField.GetValue(warhead) as GameObject;
                if(effect==null || effect.GetComponentsInChildren<ParticleSystem>(true).Length<2) continue;
                // Keep nuclear/shockwave assets out of this bounded conventional-explosion test.
                if(effect.name.ToLowerInvariant().Contains("nuclear")) continue;
                prefab=effect; missileName=m.name;
                if(m.name.ToLowerInvariant().Contains("agm")) break;
            }
            if(prefab==null) throw new Exception("No native conventional explosion prefab loaded");
            BlindPlugin.LogSource.LogInfo("BLIND_NATIVE_EFFECT "+missileName+" / "+prefab.name);
            var root=new GameObject("BLIND isolated diagnostic scene");
            root.SetActive(false);
            root.transform.position=new Vector3(10000,10000,10000);
            var effectClone=UnityEngine.Object.Instantiate(prefab,root.transform);
            effectClone.transform.localPosition=Vector3.zero;
            foreach(var script in effectClone.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled=false;
            var systems=effectClone.GetComponentsInChildren<ParticleSystem>(true);
            foreach(var ps in systems)
            {
                var main=ps.main; main.playOnAwake=false;
                if(main.simulationSpace==ParticleSystemSimulationSpace.Custom) main.customSimulationSpace=root.transform;
            }
            foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=31;
            var cameraObject=new GameObject("BLIND diagnostic sensor");
            var camera=cameraObject.AddComponent<Camera>();
            camera.enabled=false; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
            camera.cullingMask=1<<31; camera.nearClipPlane=0.2f; camera.farClipPlane=1000; camera.fieldOfView=35;
            camera.transform.position=root.transform.position+new Vector3(0,18,-100);
            camera.transform.LookAt(root.transform.position+Vector3.up*8);
            var data=cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing=false; data.requiresDepthTexture=true;
            var rt=new RenderTexture(360,240,24,RenderTextureFormat.ARGB32);
            camera.targetTexture=rt;
            var pixels=new Texture2D(360,240,TextureFormat.RGB24,false);
            var body=GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name="BLIND diagnostic vehicle"; body.layer=31;
            body.transform.position=root.transform.position+new Vector3(-8,1.5f,-4);
            body.transform.localScale=new Vector3(5,3,4);
            var bodyMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            body.GetComponent<Renderer>().sharedMaterial=bodyMaterial;
            var thermal=ThermalRenderer.Active;
            thermal.DiagnosticSurfaces.Add(body.GetComponent<Renderer>());
            float oldNoise=BlindPlugin.Instance.ThermalNoise.Value;
            BlindPlugin.Instance.ThermalNoise.Value=0;
            var previous=RenderTexture.active;
            int total=0;
            try
            {
                root.SetActive(true);
                foreach(var ps in systems) ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                thermal.RegisterEffect(effectClone);
                foreach(var r in effectClone.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var mat=r.sharedMaterial;
                    BlindPlugin.LogSource.LogInfo("BLIND_EFFECT_RENDERER "+r.name+" shader="+(mat==null?"none":mat.shader.name)+" mat="+(mat==null?"none":mat.name));
                    if(mat!=null) foreach(var key in mat.GetTexturePropertyNames())
                    {
                        var texture=mat.GetTexture(key);
                        if(texture!=null) BlindPlugin.LogSource.LogInfo("BLIND_EFFECT_TEXTURE "+r.name+" "+key+"="+texture.name+" "+texture.dimension);
                    }
                }
                foreach(var mode in new[]{SensorMode.FlirIronbow,SensorMode.FlirWhiteHot})
                {
                    thermal.SetCamera(camera,mode);
                    foreach(float time in new[]{0.05f,0.15f,0.35f,0.7f,1.5f,3f})
                    {
                        foreach(var ps in systems) { ps.Simulate(time,false,true,true); ps.Pause(false); }
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        RenderTexture.active=rt; pixels.ReadPixels(new Rect(0,0,360,240),0,0); pixels.Apply();
                        var first=pixels.GetPixels32();
                        // Same particle state rendered again must not flicker.
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        RenderTexture.active=rt; pixels.ReadPixels(new Rect(0,0,360,240),0,0); pixels.Apply();
                        var second=pixels.GetPixels32(); int changed=0, pink=0;
                        for(int i=0;i<first.Length;i++)
                        {
                            if(Math.Abs(first[i].r-second[i].r)>2 || Math.Abs(first[i].g-second[i].g)>2 || Math.Abs(first[i].b-second[i].b)>2) changed++;
                            if(second[i].r>240 && second[i].g<10 && second[i].b>240) pink++;
                        }
                        if(changed>first.Length/100 || pink>10) throw new Exception("Unstable or unsupported frame: "+changed+" changed, "+pink+" pink");
                        string file=mode+"-"+time.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".png";
                        File.WriteAllBytes(Path.Combine(output,file),pixels.EncodeToPNG());
                        BlindPlugin.LogSource.LogInfo("BLIND_NATIVE_FRAME_OK "+file+" changed="+changed);
                        total++;
                    }
                }
                File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: "+total+" native explosion frames, repeated-frame stability. Prefab: "+prefab.name+". Not a full gameplay/JTAC test.");
                BlindPlugin.LogSource.LogInfo("BLIND_RUNTIME_TEST_OK "+total+" frames "+output);
            }
            finally
            {
                BlindPlugin.Instance.ThermalNoise.Value=oldNoise;
                thermal.DiagnosticSurfaces.Clear(); thermal.SetCamera(null,SensorMode.VanillaIR);
                RenderTexture.active=previous; rt.Release();
                foreach(var obj in new UnityEngine.Object[]{root,cameraObject,body,bodyMaterial,pixels,rt}) UnityEngine.Object.Destroy(obj);
            }
        }
    }
}
#endif
