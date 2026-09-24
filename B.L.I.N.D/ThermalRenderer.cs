using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BLIND
{
    // Rendering is isolated to the cockpit sensor. No writes to game materials/property blocks.
    internal sealed class ThermalRenderer : IDisposable
    {
        private static readonly FieldInfo SourcesField = AccessTools.Field(typeof(Unit), "IRSources");
        internal static ThermalRenderer Active;
        private readonly BlindPlugin plugin;
        private readonly Dictionary<Unit, Body> bodies = new Dictionary<Unit, Body>();
        private readonly Dictionary<Renderer, Surface> surfaces = new Dictionary<Renderer, Surface>();
        private readonly HashSet<Renderer> effects = new HashSet<Renderer>();
        private readonly List<Unit> deadBodies = new List<Unit>();
        private readonly List<Renderer> deadSurfaces = new List<Renderer>();
        private readonly Vector4[] positions = new Vector4[8], powers = new Vector4[8];
        private readonly Plane[] frustum = new Plane[6];
        private readonly HeatPass pass;
        private AssetBundle bundle;
        private Material screen;
        private Mesh exhaustQuad;
        private readonly List<Missile> burningMissiles = new List<Missile>();
        internal readonly HashSet<Missile> activeMissiles = new HashSet<Missile>();
        private Shader shader;
        private bool attempted, failed, loggedFrame;
        private float nextScan;
        private Camera camera;
        private UniversalAdditionalCameraData cameraData;
        private bool oldPost, oldDepth;
        private SensorMode mode;
#if BLIND_DIAGNOSTICS
        internal readonly List<Renderer> DiagnosticSurfaces = new List<Renderer>();
#endif

        private sealed class Body
        {
            internal Renderer[] Renderers;
            internal List<IRSource> Sources;
            internal float Heat, LastTime, NextRefresh;
        }
        private sealed class Surface
        {
            internal Material[] Materials;
            internal Material[] Originals;
            internal float EffectHeat;
            internal Mesh ParticleMesh;
            internal ParticleSystem Particles;
            internal bool Disabled;
        }

        internal ThermalRenderer(BlindPlugin plugin)
        {
            this.plugin = plugin;
            pass = new HeatPass(this);
            Active = this;
            Ready();
        }

        internal bool Ready()
        {
            if (attempted) return !failed && screen != null;
            attempted = true;
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(typeof(BlindPlugin).Assembly.Location), "blind-thermal.bundle");
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null) throw new InvalidOperationException("Cannot load " + path);
                shader = bundle.LoadAsset<Shader>("Assets/BlindThermal.shader");
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Thermal shader unsupported");
                screen = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                exhaustQuad = new Mesh { name = "BLIND sensor exhaust footprint", hideFlags = HideFlags.HideAndDontSave };
                exhaustQuad.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) };
                exhaustQuad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                exhaustQuad.triangles = new[] { 0,1,2,0,2,3 };
                exhaustQuad.RecalculateNormals();
                if (screen.passCount != 4) throw new InvalidOperationException("Expected four thermal shader passes");
                BlindPlugin.LogSource.LogInfo("[Thermal] Shader loaded, four passes; native IRSource integration enabled.");
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                BlindPlugin.LogSource.LogError("[Thermal] Disabled safely; original camera retained. " + e.Message);
                return false;
            }
        }

        internal void SetCamera(Camera value, SensorMode valueMode)
        {
            bool thermal = valueMode == SensorMode.FlirIronbow || valueMode == SensorMode.FlirWhiteHot;
            if (!thermal || value == null || !Ready()) value = null;
            if (camera != value)
            {
                if (cameraData != null) { cameraData.renderPostProcessing = oldPost; cameraData.requiresDepthTexture = oldDepth; }
                camera = value;
                cameraData = camera == null ? null : camera.GetComponent<UniversalAdditionalCameraData>();
                if (cameraData != null) { oldPost = cameraData.renderPostProcessing; oldDepth = cameraData.requiresDepthTexture; }
            }
            mode = valueMode;
            if (cameraData != null) { cameraData.renderPostProcessing = false; cameraData.requiresDepthTexture = true; }
        }

        internal void Enqueue(ScriptableRenderer renderer, Camera renderCamera)
        {
            if (!failed && camera != null && renderCamera == camera) renderer.EnqueuePass(pass);
        }

        internal void RegisterEffect(GameObject root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<ParticleSystemRenderer>(true)) effects.Add(r);
        }

        internal static void RegisterSpawnedEffect(UnityEngine.Object spawned)
        {
            var root = spawned as GameObject;
            if (Active != null) Active.RegisterEffect(root);
        }

        private void Scan(float now)
        {
            if (now < nextScan) return;
            nextScan = now + 0.6f;
            foreach (var r in UnityEngine.Object.FindObjectsOfType<ParticleSystemRenderer>()) effects.Add(r);
            deadBodies.Clear();
            foreach (var pair in bodies) if (pair.Key == null) deadBodies.Add(pair.Key);
            foreach (var key in deadBodies) bodies.Remove(key);
            deadSurfaces.Clear();
            foreach (var pair in surfaces) if (pair.Key == null) deadSurfaces.Add(pair.Key);
            foreach (var key in deadSurfaces) { DestroySurface(surfaces[key]); surfaces.Remove(key); }
            effects.RemoveWhere(r => r == null);
            activeMissiles.RemoveWhere(m => m == null || !m.gameObject.activeInHierarchy);
        }

        private Body GetBody(Unit unit, float now)
        {
            Body body;
            if (!bodies.TryGetValue(unit, out body))
            {
                body = new Body { Heat = DesiredHeat(unit), LastTime = now };
                bodies.Add(unit, body);
            }
            float desired = DesiredHeat(unit);
            float dt = Mathf.Clamp(now - body.LastTime, 0, 120);
            float rate = (unit is Missile) ? 0.3f : (desired > body.Heat ? 9f : 35f);
            body.Heat = Mathf.Lerp(body.Heat, desired, 1f - Mathf.Exp(-dt / rate));
            body.LastTime = now;
            if (body.Renderers == null || now >= body.NextRefresh)
            {
                body.Renderers = unit.GetComponentsInChildren<Renderer>(true);
                body.Sources = SourcesField == null ? null : SourcesField.GetValue(unit) as List<IRSource>;
                body.NextRefresh = now + 2f;
            }
            return body;
        }

        private static float DesiredHeat(Unit unit)
        {
            if (unit.disabled || unit.unitState == Unit.UnitState.Destroyed) return 0.50f;
            if (unit is Building) return 0.22f;
            float heat = 0.38f;
            Aircraft aircraft = unit as Aircraft;
            if (aircraft != null)
            {
                float rpm = 0;
                if (aircraft.engines != null && aircraft.engines.Count > 0)
                {
                    foreach (var engine in aircraft.engines) if (engine != null) rpm += Mathf.Clamp01(engine.GetRPMRatio());
                    rpm /= aircraft.engines.Count;
                }
                heat += 0.10f + rpm * 0.35f;
            }
            else if (unit is GroundVehicle)
            {
                // Active ground units (AA, missile launchers, tanks, IFVs) run generators and diesel/gas turbines continuously
                heat = 0.58f + Mathf.Clamp01(unit.speed / 10f) * 0.30f;
            }
            else if (unit is Ship) heat = 0.45f;
            else if (unit is Missile)
            {
                Missile m = (Missile)unit;
                heat = m.EngineOn() ? 1.8f : 0.65f;
            }
            if (unit.unitState == Unit.UnitState.Damaged) heat += 0.18f;
            return heat;
        }

        private void SetSources(CommandBuffer cmd, Unit unit, Body body)
        {
            Array.Clear(positions, 0, positions.Length);
            Array.Clear(powers, 0, powers.Length);
            int count = 0;
            float size = unit.definition == null ? 5f : Mathf.Max(1, unit.definition.length);
            if (body.Sources != null)
            {
                foreach (var source in body.Sources)
                {
                    if (source == null || source.transform == null || source.flare || source.intensity <= 0) continue;
                    if (count == positions.Length) break;
                    Vector3 p = source.transform.position;
                    float power = Mathf.Clamp(Mathf.Log(1 + source.intensity) * 0.35f, 0.3f, 3.0f);
                    Missile missile = unit as Missile;
                    if (missile != null && source.transform == unit.transform)
                        p -= unit.transform.forward * size * 0.35f;
                    if (missile != null && !missile.EngineOn()) power *= 0.15f;
                    else if (missile != null) power = Mathf.Max(power, 3.2f);
                    positions[count] = new Vector4(p.x,p.y,p.z,Mathf.Clamp(size * 0.18f,0.5f,3f));
                    powers[count++] = new Vector4(power,0,0,0);
                }
            }
            // Active missile rocket motor gets a guaranteed rear emitter if IRSource list is unpopulated.
            Missile unitMissile = unit as Missile;
            if (unitMissile != null && count == 0 && unitMissile.EngineOn())
            {
                Vector3 p = unit.transform.position - unit.transform.forward * size * 0.35f;
                positions[0] = new Vector4(p.x, p.y, p.z, Mathf.Clamp(size * 0.25f, 0.6f, 2.5f));
                powers[0] = new Vector4(3.8f, 0, 0, 0);
                count++;
            }
            // Ground vehicles without native IR emitters get an active engine compartment and generator signature.
            if (count == 0 && unit is GroundVehicle && !unit.disabled)
            {
                Vector3 p = unit.transform.TransformPoint(new Vector3(0, size*0.10f, -size*0.25f));
                positions[0] = new Vector4(p.x,p.y,p.z,Mathf.Clamp(size*0.28f,0.8f,3.0f));
                powers[0] = new Vector4(Mathf.Max(0.2f, body.Heat - 0.15f) * 2.2f, 0, 0, 0);
            }
            cmd.SetGlobalVectorArray("_BlindHeatSources", positions);
            cmd.SetGlobalVectorArray("_BlindHeatPowers", powers);
        }

        private Surface GetSurface(Renderer r)
        {
            Surface surface;
            if (surfaces.TryGetValue(r, out surface)) return surface;
            Material[] originals = r.sharedMaterials;
            surface = new Surface { Originals = originals, Materials = new Material[originals.Length] };
            string name = r.name.ToLowerInvariant();
            for (int n=0; n<originals.Length; n++)
            {
                var original = originals[n];
                if (original == null) continue;
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                Texture tex = null;
                string texProp = original.HasProperty("_BaseMap") ? "_BaseMap" : (original.HasProperty("_MainTex") ? "_MainTex" : null);
                if (texProp != null) tex = original.GetTexture(texProp);
                if (tex != null)
                {
                    material.SetTexture("_BlindDetailTex", tex);
                    material.SetTextureScale("_BlindDetailTex", original.GetTextureScale(texProp));
                    material.SetTextureOffset("_BlindDetailTex", original.GetTextureOffset(texProp));
                }
                else
                {
                    material.SetFloat("_BlindUseAlpha", 0f);
                }
                bool particle = r is ParticleSystemRenderer || r is TrailRenderer;
                material.SetFloat("_BlindUseAlpha", particle || original.IsKeywordEnabled("_ALPHATEST_ON") ? 1f : 0f);
                material.SetFloat("_BlindCutoff", particle ? 0.05f : (original.HasProperty("_Cutoff") ? original.GetFloat("_Cutoff") : 0.5f));
                material.SetFloat("_BlindDetailAmount", particle ? 0 : 0.22f);
                surface.Materials[n] = material;
                name += " " + original.name.ToLowerInvariant();
                // Alpha-blended textures use alpha; additive textures use RGB as their shape.
                bool additive = original.HasProperty("_DstBlend") && original.GetFloat("_DstBlend") == (float)BlendMode.One;
                material.SetFloat("_BlindAdditiveShape", additive ? 1f : 0f);
            }

            // Exclude non-thermal and flat ground overlays that cause Z-fighting (ground decals, optical flashes, shockwaves)
            bool isNonThermal = name.Contains("shockwave") || name.Contains("distortion") ||
                                name.Contains("refract") || name.Contains("decal") || name.Contains("crater") ||
                                name.Contains("scorch") || name.Contains("dust") || name.Contains("dirt") ||
                                name.Contains("rubble") || name.Contains("debris") || name.Contains("vapor") ||
                                name.Contains("contrail");

            if (isNonThermal)
            {
                surface.EffectHeat = 0f;
                surfaces.Add(r, surface);
                return surface;
            }

            // Real thermal emitters: game explosion fireballs, flames, burning matter
            bool isFlame = name.Contains("fire") || name.Contains("flame") || name.Contains("fireball") ||
                           name.Contains("explos") || name.Contains("blast") || name.Contains("shrapnel") ||
                           name.Contains("afterburn") || name.Contains("spark") || name.Contains("flare") ||
                           name.Contains("tracer") || name.Contains("exhaust") || name.Contains("thrust");

            bool isSmoke = name.Contains("smoke") || name.Contains("plume");
            if (isSmoke)
                surface.EffectHeat = 0.35f;
            else if (isFlame)
                surface.EffectHeat = 3.5f;
            else
                surface.EffectHeat = 0f;

            surfaces.Add(r, surface);
            if (r is ParticleSystemRenderer && surface.EffectHeat > 0)
            {
                surface.Particles = r.GetComponent<ParticleSystem>();
                surface.ParticleMesh = new Mesh { name = "BLIND particle snapshot", hideFlags = HideFlags.HideAndDontSave };
                surface.ParticleMesh.MarkDynamic();
            }
            return surface;
        }

        private bool Visible(Renderer r)
        {
            if (r == null || !r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy ||
                (camera.cullingMask & (1 << r.gameObject.layer)) == 0) return false;
            // World/custom-space particle emitters can sit at the world origin. Cull their bounds,
            // never their Transform, and never exempt effects from the sensor frustum.
            if (r.bounds.SqrDistance(camera.transform.position) > 25000f * 25000f) return false;
            return GeometryUtility.TestPlanesAABB(frustum, r.bounds);
        }

        private void Draw(CommandBuffer cmd)
        {
            float now = Time.time;
            Scan(now);
            GeometryUtility.CalculateFrustumPlanes(camera,frustum);
            var units = UnitRegistry.allUnits;
            burningMissiles.Clear();
            if (units != null) foreach (var unit in units)
            {
                if (unit == null || !unit.gameObject.activeInHierarchy) continue;
                if ((unit.transform.position-camera.transform.position).sqrMagnitude > 25000f*25000f) continue;
                Body body = GetBody(unit,now);
                Missile missile = unit as Missile;
                if (missile != null && !missile.disabled && missile.EngineOn()) burningMissiles.Add(missile);
                SetSources(cmd,unit,body);
                cmd.SetGlobalFloat("_BlindBodyHeat",body.Heat);
                cmd.SetGlobalFloat("_BlindEffect",0);
                if (body.Renderers != null)
                {
                    foreach (var r in body.Renderers)
                    {
                        if (!Visible(r)) continue;
                        if (r is ParticleSystemRenderer || r is TrailRenderer) { effects.Add(r); continue; }
                        if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                        var surface = GetSurface(r);
                        for (int sub=0; sub<surface.Materials.Length; sub++)
                            if (surface.Materials[sub] != null) cmd.DrawRenderer(r,surface.Materials[sub],sub,1);
                    }
                }
            }
            // Track active registered missiles without any expensive full-scene searching
            foreach (var m in activeMissiles)
            {
                if (m != null && !m.disabled && m.gameObject.activeInHierarchy && m.EngineOn() && !burningMissiles.Contains(m))
                {
                    burningMissiles.Add(m);
                    Body body = GetBody(m, now);
                    SetSources(cmd, m, body);
                    cmd.SetGlobalFloat("_BlindBodyHeat", body.Heat);
                    cmd.SetGlobalFloat("_BlindEffect", 0);
                    if (body.Renderers != null)
                    {
                        foreach (var r in body.Renderers)
                        {
                            if (!Visible(r)) continue;
                            if (r is ParticleSystemRenderer || r is TrailRenderer) { effects.Add(r); continue; }
                            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                            var surface = GetSurface(r);
                            for (int sub = 0; sub < surface.Materials.Length; sub++)
                                if (surface.Materials[sub] != null) cmd.DrawRenderer(r, surface.Materials[sub], sub, 1);
                        }
                    }
                }
            }
            Array.Clear(positions,0,positions.Length);
            Array.Clear(powers,0,powers.Length);
            cmd.SetGlobalVectorArray("_BlindHeatSources",positions);
            cmd.SetGlobalVectorArray("_BlindHeatPowers",powers);
#if BLIND_DIAGNOSTICS
            cmd.SetGlobalFloat("_BlindEffect",0);
            cmd.SetGlobalFloat("_BlindBodyHeat",0.6f);
            foreach(var r in DiagnosticSurfaces)
            {
                if(!Visible(r)) continue;
                var surface=GetSurface(r);
                for(int sub=0;sub<surface.Materials.Length;sub++)
                    if(surface.Materials[sub]!=null) cmd.DrawRenderer(r,surface.Materials[sub],sub,1);
            }
#endif
            cmd.SetGlobalFloat("_BlindEffect",1);
            foreach (var r in effects)
            {
                if (!Visible(r)) continue;
                var surface = GetSurface(r);
                var particleRenderer = r as ParticleSystemRenderer;
                if (surface.EffectHeat <= 0 || surface.Disabled || particleRenderer == null ||
                    surface.Particles == null || surface.Particles.particleCount == 0 || surface.ParticleMesh == null) continue;
                // Bake for this camera: stock effects may use instancing, custom streams and
                // view-facing geometry. Replaying DrawRenderer with a different shader is unsafe.
                // The bool overload remains available in the game's supported Unity runtime.
#pragma warning disable 618
                try { particleRenderer.BakeMesh(surface.ParticleMesh, camera, true); }
#pragma warning restore 618
                catch (Exception e)
                {
                    surface.Disabled = true;
                    BlindPlugin.LogSource.LogWarning("[Thermal] Unsupported effect skipped: " + r.name + ": " + e.Message);
                    continue;
                }
                if (surface.ParticleMesh.vertexCount == 0) continue;
                // BakeMesh(true) includes simulation-space rotation/scale, but not its origin.
                // Applying localToWorld again double-transforms world/Datum-space explosions.
                var main = surface.Particles.main;
                Vector3 origin = main.simulationSpace == ParticleSystemSimulationSpace.Local ? r.transform.position :
                    main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null ? main.customSimulationSpace.position : Vector3.zero;
                cmd.SetGlobalFloat("_BlindBodyHeat",surface.EffectHeat);
                for (int sub=0; sub<Mathf.Min(surface.Materials.Length,surface.ParticleMesh.subMeshCount); sub++)
                    if (surface.Materials[sub] != null)
                        cmd.DrawMesh(surface.ParticleMesh,Matrix4x4.Translate(origin),surface.Materials[sub],sub,2);
            }
            // A narrow exhaust can be smaller than one pixel on the stock 360x240 screen.
            // Resolve its radiance as a bounded sensor footprint with full core brilliance.
            cmd.SetGlobalFloat("_BlindEffect",2);
            foreach (var missile in burningMissiles)
            {
                if (missile == null) continue;
                float length = missile.definition == null ? 3f : Mathf.Max(0.5f,missile.definition.length);
                Vector3 nozzle = missile.transform.position - missile.transform.forward * length * 0.35f;
                float depth = Vector3.Dot(nozzle-camera.transform.position,camera.transform.forward);
                if (depth <= camera.nearClipPlane || depth >= camera.farClipPlane) continue;
                float pixel = 2f * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) / Mathf.Max(1,camera.pixelHeight);
                float radius = Mathf.Clamp(length*0.18f,0.4f,1.8f);
                float footprint = Mathf.Max(radius,pixel*1.2f);
                cmd.SetGlobalFloat("_BlindBodyHeat",3.2f);
                cmd.DrawMesh(exhaustQuad,Matrix4x4.TRS(nozzle,camera.transform.rotation,Vector3.one*footprint),screen,0,2);
            }
        }

        private static void DestroySurface(Surface surface)
        {
            foreach (var material in surface.Materials) if (material != null) UnityEngine.Object.Destroy(material);
            if (surface.ParticleMesh != null) UnityEngine.Object.Destroy(surface.ParticleMesh);
        }
        public void Dispose()
        {
            SetCamera(null,SensorMode.VanillaIR);
            foreach (var surface in surfaces.Values) DestroySurface(surface);
            surfaces.Clear(); bodies.Clear(); effects.Clear(); activeMissiles.Clear();
            if (screen != null) UnityEngine.Object.Destroy(screen);
            if (exhaustQuad != null) UnityEngine.Object.Destroy(exhaustQuad);
            if (bundle != null) bundle.Unload(false);
            if (Active == this) Active = null;
        }

        private sealed class HeatPass : ScriptableRenderPass
        {
            private readonly ThermalRenderer owner;
            private static readonly int HeatTarget = Shader.PropertyToID("_BLIND_HeatBuffer");
            internal HeatPass(ThermalRenderer owner)
            {
                this.owner = owner;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (owner.camera == null || owner.failed || renderingData.cameraData.camera != owner.camera) return;
                CommandBuffer cmd = CommandBufferPool.Get("BLIND thermal radiance");
                try
                {
                    var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                    descriptor.colorFormat = RenderTextureFormat.ARGBHalf;
                    descriptor.depthBufferBits = 24;
                    descriptor.msaaSamples = 1;
                    descriptor.sRGB = false;
                    descriptor.useMipMap = false;
                    descriptor.autoGenerateMips = false;
                    cmd.GetTemporaryRT(HeatTarget,descriptor,FilterMode.Bilinear);
                    var renderer = renderingData.cameraData.renderer;
                    RenderTargetIdentifier color = renderer.cameraColorTargetHandle.nameID;
                    float atmosphere = NetworkSceneSingleton<LevelInfo>.i == null ? 0 :
                        NetworkSceneSingleton<LevelInfo>.i.GetCloudOcclusion(owner.camera.transform.position);
                    cmd.SetGlobalFloat("_BlindAtmosphere",Mathf.Clamp01(atmosphere));
                    cmd.SetGlobalFloat("_BlindSpan",owner.plugin.ThermalSpan.Value);
                    cmd.SetGlobalFloat("_BlindNoise",owner.plugin.ThermalNoise.Value);
                    cmd.SetGlobalFloat("_BlindWhiteHotCeiling",owner.plugin.WhiteHotCeiling.Value);
                    cmd.SetGlobalFloat("_BlindMode",owner.mode == SensorMode.FlirIronbow ? 0 : 1);
                    cmd.SetRenderTarget(HeatTarget);
                    cmd.ClearRenderTarget(true,true,Color.black);
                    cmd.Blit(color,HeatTarget,owner.screen,0);
                    cmd.SetRenderTarget(HeatTarget);
                    cmd.SetViewProjectionMatrices(owner.camera.worldToCameraMatrix,
                        GL.GetGPUProjectionMatrix(owner.camera.projectionMatrix, true));
                    owner.Draw(cmd);
                    cmd.Blit(HeatTarget,color,owner.screen,3);
                    cmd.ReleaseTemporaryRT(HeatTarget);
                    context.ExecuteCommandBuffer(cmd);
                    if (!owner.loggedFrame)
                    {
                        owner.loggedFrame = true;
                        BlindPlugin.LogSource.LogInfo("[Thermal] First sensor frame submitted: " + descriptor.width + "x" + descriptor.height + ", " + owner.surfaces.Count + " cached renderers.");
                    }
                }
                catch (Exception e)
                {
                    owner.failed = true;
                    owner.SetCamera(null,SensorMode.VanillaIR);
                    BlindPlugin.LogSource.LogError("[Thermal] Render disabled; restored stock camera. " + e);
                }
                finally { cmd.Clear(); CommandBufferPool.Release(cmd); }
            }
        }
    }

    [HarmonyPatch(typeof(ScriptableRenderer), "AddRenderPasses")]
    internal static class ThermalPassPatch
    {
        private static void Postfix(ScriptableRenderer __instance, ref RenderingData renderingData)
        {
            if (ThermalRenderer.Active != null) ThermalRenderer.Active.Enqueue(__instance,renderingData.cameraData.camera);
        }
    }
    [HarmonyPatch(typeof(ParticleEffectManager.PrefabEffect), "Play")]
    internal static class ThermalEffectPatch
    {
        private static readonly FieldInfo Root = AccessTools.Field(typeof(ParticleEffectManager.PrefabEffect),"gameObject");
        private static void Postfix(ParticleEffectManager.PrefabEffect __instance)
        {
            if (ThermalRenderer.Active != null && Root != null) ThermalRenderer.Active.RegisterEffect(Root.GetValue(__instance) as GameObject);
        }
    }
    [HarmonyPatch(typeof(Missile), "OnEnable")]
    internal static class ThermalMissileEnablePatch
    {
        private static void Postfix(Missile __instance)
        {
            if (ThermalRenderer.Active != null && __instance != null)
                ThermalRenderer.Active.activeMissiles.Add(__instance);
        }
    }
    [HarmonyPatch(typeof(Missile.Warhead), "Detonate")]
    internal static class ThermalExplosionPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var register = AccessTools.Method(typeof(ThermalRenderer),"RegisterSpawnedEffect");
            int count = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                var method = instruction.operand as MethodInfo;
                if (instruction.opcode == OpCodes.Call && method != null &&
                    method.DeclaringType == typeof(UnityEngine.Object) && method.Name == "Instantiate" &&
                    typeof(UnityEngine.Object).IsAssignableFrom(method.ReturnType))
                {
                    yield return new CodeInstruction(OpCodes.Dup);
                    yield return new CodeInstruction(OpCodes.Call,register);
                    count++;
                }
            }
            if (count > 0)
                BlindPlugin.LogSource.LogInfo("[Thermal] Immediate explosion registration at " + count + " spawn sites.");
            else
                BlindPlugin.LogSource.LogWarning("[Thermal] Explosion spawn hook unavailable; using periodic effect discovery.");
        }
    }
}
