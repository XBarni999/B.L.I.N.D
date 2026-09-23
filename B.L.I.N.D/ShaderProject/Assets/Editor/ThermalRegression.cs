using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ThermalRegression
{
    public static void Run()
    {
        BuildThermal.ValidatePalettes();
        ValidateParticleSpace();
        ValidateEffectPixels();
        BuildThermal.Build();
        Debug.Log("BLIND_REGRESSION_OK: palettes, local/world/custom particle transforms, alpha edges, finite pixels, depth occlusion");
    }

    static void ValidateParticleSpace()
    {
        var root = new GameObject("BLIND bake regression");
        var camObject = new GameObject("BLIND bake camera");
        var custom = new GameObject("BLIND simulation origin");
        var camera = camObject.AddComponent<Camera>();
        camera.enabled=false;
        var ps=root.AddComponent<ParticleSystem>();
        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;
        main.playOnAwake=false; main.startSpeed=0; main.startLifetime=100;
        root.transform.SetPositionAndRotation(new Vector3(50,20,-30),Quaternion.Euler(10,40,0));
        root.transform.localScale=new Vector3(1.7f,1.7f,1.7f);
        custom.transform.SetPositionAndRotation(new Vector3(-40,30,15),Quaternion.Euler(0,30,0));
        var renderer=root.GetComponent<ParticleSystemRenderer>();
        var particleMaterial = new Material(Shader.Find("Particles/Standard Unlit"));
        renderer.sharedMaterial=particleMaterial;
        var cameraTarget = new RenderTexture(128,128,24);
        camera.targetTexture=cameraTarget;
        var mesh=new Mesh();
        var particles=new ParticleSystem.Particle[1];
        try
        {
            foreach(var space in new[] {ParticleSystemSimulationSpace.Local,ParticleSystemSimulationSpace.World,ParticleSystemSimulationSpace.Custom})
            {
                main.simulationSpace=space;
                if(space==ParticleSystemSimulationSpace.Custom) main.customSimulationSpace=custom.transform;
                Vector3 point=new Vector3(1,2,3);
                Vector3 expected=space==ParticleSystemSimulationSpace.Local ? root.transform.TransformPoint(point) :
                    space==ParticleSystemSimulationSpace.Custom ? custom.transform.TransformPoint(point) : point;
                camera.transform.position=expected+new Vector3(0,0,-20);
                camera.transform.LookAt(expected);
                ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(false);
                ps.Emit(1);
                ps.GetParticles(particles);
                particles[0].position=point;
                particles[0].startSize=2;
                particles[0].startColor=Color.white;
                particles[0].startLifetime=100;
                particles[0].remainingLifetime=100;
                ps.SetParticles(particles,1);
                ps.Pause(false);
                camera.Render();
                renderer.BakeMesh(mesh,camera,false);
                mesh.RecalculateBounds();
                Debug.Log("BLIND_BAKE_DIAGNOSTIC "+space+" particles="+ps.particleCount+" vertices="+mesh.vertexCount+" bounds="+mesh.bounds);
                renderer.BakeMesh(mesh,camera,true);
                mesh.RecalculateBounds();
                Debug.Log("BLIND_BAKE_TRUE "+space+" bounds="+mesh.bounds+" expected="+expected);
                Vector3 origin=space==ParticleSystemSimulationSpace.Local ? root.transform.position :
                    space==ParticleSystemSimulationSpace.Custom ? custom.transform.position : Vector3.zero;
                Vector3 actual=origin+mesh.bounds.center;
                if(Vector3.Distance(expected,actual)>0.02f)
                    throw new Exception("Particle transform mismatch "+space+": expected "+expected+", actual "+actual+", raw "+mesh.bounds.center);
                if(mesh.vertexCount!=4) throw new Exception("Expected one baked billboard quad");
                Debug.Log("BLIND_BAKE_SPACE_OK "+space+" "+actual);
            }
        }
        finally { cameraTarget.Release(); UnityEngine.Object.DestroyImmediate(cameraTarget); UnityEngine.Object.DestroyImmediate(particleMaterial); UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(camObject); UnityEngine.Object.DestroyImmediate(custom); }
    }

    static void ValidateEffectPixels()
    {
        const int size=128;
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/BlindThermal.shader");
        var material=new Material(shader);
        var texture=new Texture2D(32,32,TextureFormat.RGBA32,false,true);
        var depth=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
        var output=new RenderTexture(size,size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var readback=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true);
        var mesh=new Mesh();
        var cameraObject=new GameObject("BLIND pixel camera");
        var camera=cameraObject.AddComponent<Camera>();
        camera.enabled=false; camera.nearClipPlane=0.1f; camera.farClipPlane=100; camera.fieldOfView=60; camera.aspect=1;
        camera.transform.position=new Vector3(0,0,-10);
        camera.targetTexture=output;
        mesh.vertices=new[]{new Vector3(-2,-2,0),new Vector3(2,-2,0),new Vector3(2,2,0),new Vector3(-2,2,0)};
        mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
        mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};
        mesh.normals=new[]{Vector3.zero,Vector3.zero,Vector3.zero,Vector3.zero};
        mesh.triangles=new[]{0,1,2,0,2,3};
        for(int y=0;y<32;y++) for(int x=0;x<32;x++)
        {
            float r=new Vector2((x+0.5f)/16-1,(y+0.5f)/16-1).magnitude;
            texture.SetPixel(x,y,new Color(1,0.5f,0,Mathf.Clamp01((0.8f-r)*4)));
        }
        texture.Apply(); texture.wrapMode=TextureWrapMode.Clamp;
        material.SetTexture("_BlindDetailTex",texture); material.SetFloat("_BlindAdditiveShape",0);
        material.SetFloat("_BlindEffect",1); material.SetFloat("_BlindBodyHeat",2); material.SetFloat("_BlindAtmosphere",0);
        var previous=RenderTexture.active;
        var cmd=new CommandBuffer();
        try
        {
            float n=camera.nearClipPlane,f=camera.farClipPlane;
            Vector4 z=SystemInfo.usesReversedZBuffer ? new Vector4(f/n-1,1,(f/n-1)/f,1/f) : new Vector4(1-f/n,f/n,(1-f/n)/f,1/n);
            Matrix4x4 projection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true);
            foreach(bool blocked in new[]{false,true})
            {
                var projected=projection*new Vector4(0,0,-(blocked?5:50),1);
                depth.SetPixel(0,0,new Color(projected.z/projected.w,0,0,1)); depth.Apply();
                cmd.Clear(); cmd.SetRenderTarget(output); cmd.ClearRenderTarget(true,true,new Color(0.07f,0.07f,0.07f,1));
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,projection);
                cmd.SetGlobalVector("_ProjectionParams",new Vector4(-1,n,f,1/f)); cmd.SetGlobalVector("_ZBufferParams",z);
                cmd.SetGlobalTexture("_CameraDepthTexture",depth);
                cmd.DrawMesh(mesh,Matrix4x4.identity,material,0,2);
                Graphics.ExecuteCommandBuffer(cmd);
                RenderTexture.active=output; readback.ReadPixels(new Rect(0,0,size,size),0,0); readback.Apply();
                int lit=0; var pixels=readback.GetPixels();
                foreach(var p in pixels) { if(float.IsNaN(p.r)||float.IsInfinity(p.r)) throw new Exception("Nonfinite particle pixels"); if(p.r>0.08f) lit++; }
                if(blocked && lit!=0) throw new Exception("Effect shows through occluder: "+lit);
                if(!blocked && (lit<20 || lit>size*size/4)) throw new Exception("Effect missing or spills outside footprint: "+lit);
                for(int x=0;x<size;x++) if(pixels[x].r>0.08f || pixels[(size-1)*size+x].r>0.08f) throw new Exception("Fullscreen effect spill");
                Debug.Log("BLIND_PARTICLE_PIXELS_OK blocked="+blocked+" pixels="+lit);
            }
        }
        finally
        {
            RenderTexture.active=previous; cmd.Release(); output.Release();
            foreach(var obj in new UnityEngine.Object[]{material,texture,depth,output,readback,mesh,cameraObject}) UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}

