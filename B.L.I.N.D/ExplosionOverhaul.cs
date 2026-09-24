using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BLIND
{
    /// <summary>
    /// Harmony hooks for Missile.Warhead.Detonate to scale explosion particles,
    /// prolong lingering smoke/dust, and spawn procedural optical shockwaves.
    /// </summary>
    [HarmonyPatch(typeof(Missile.Warhead), "Detonate")]
    internal static class ExplosionOverhaulPatch
    {
        [ThreadStatic]
        private static float _pendingYield;

        [ThreadStatic]
        private static Vector3 _pendingPos;

        [ThreadStatic]
        private static bool _isDetonating;

        [HarmonyPrefix]
        private static void Prefix(float blastYield, Vector3 position)
        {
            _pendingYield = blastYield;
            _pendingPos = position;
            _isDetonating = true;

            if (BlindPlugin.Instance != null && !BlindPlugin.Instance.EnableExplosionOverhaul.Value)
                return;

            if (BlindPlugin.Instance == null || BlindPlugin.Instance.EnableShockwaveDistortion.Value)
            {
                float intensity = BlindPlugin.Instance != null ? BlindPlugin.Instance.ShockwaveIntensity.Value : 1.0f;
                ShockwaveDistortionManager.SpawnShockwave(position, blastYield, intensity);
            }
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            _isDetonating = false;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo hookMethod = AccessTools.Method(typeof(ExplosionOverhaulPatch), nameof(OnSpawnedExplosionEffect));
            int hookCount = 0;
            int destroyExtendedCount = 0;

            foreach (var instruction in instructions)
            {
                // 1. Extend vanilla Destroy(vfx, 30f) to 120f so lingering smoke (which persists up to 60-90s)
                // is not abruptly destroyed while fading out.
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float delay && Mathf.Approximately(delay, 30f))
                {
                    instruction.operand = 120f;
                    destroyExtendedCount++;
                }

                yield return instruction;

                // 2. Intercept Object.Instantiate to modify child ParticleSystems immediately upon spawn
                var method = instruction.operand as MethodInfo;
                if (instruction.opcode == OpCodes.Call && method != null &&
                    method.DeclaringType == typeof(UnityEngine.Object) && method.Name == "Instantiate" &&
                    typeof(UnityEngine.Object).IsAssignableFrom(method.ReturnType))
                {
                    yield return new CodeInstruction(OpCodes.Dup);
                    yield return new CodeInstruction(OpCodes.Call, hookMethod);
                    hookCount++;
                }
            }

            if (hookCount > 0)
            {
                BlindPlugin.LogSource?.LogInfo("[ExplosionOverhaul] Hooked " + hookCount + " explosion spawn sites, extended " + destroyExtendedCount + " destruction timers.");
            }
            else
            {
                BlindPlugin.LogSource?.LogWarning("[ExplosionOverhaul] Could not locate Instantiate in Warhead.Detonate.");
            }
        }

        public static void OnSpawnedExplosionEffect(UnityEngine.Object spawned)
        {
            if (!_isDetonating || spawned == null) return;
            var root = spawned as GameObject;
            if (root == null) return;

            if (BlindPlugin.Instance != null && !BlindPlugin.Instance.EnableExplosionOverhaul.Value)
                return;

            float smokeMult = BlindPlugin.Instance != null ? BlindPlugin.Instance.SmokePersistenceMultiplier.Value : 2.8f;
            ExplosionEffectModifier.ProcessExplosionVFX(root, _pendingYield, _pendingPos, smokeMult);
        }
    }

    /// <summary>
    /// Dynamic particle scaling and prolonged smoke/dust logic.
    /// </summary>
    internal static class ExplosionEffectModifier
    {
        public static void ProcessExplosionVFX(GameObject explosionObj, float blastYield, Vector3 position, float smokeLifetimeMultiplier)
        {
            if (explosionObj == null) return;

            // Hopkinson-Cranz cube root scaling law: R ~ Y^(1/3)
            // Normalized relative to a baseline 250 kg TNT warhead
            float safeYield = Mathf.Max(blastYield, 1f);
            float yieldScale = Mathf.Pow(safeYield / 250f, 0.3333333f);
            yieldScale = Mathf.Clamp(yieldScale, 0.45f, 4.2f);

            ParticleSystem[] particleSystems = explosionObj.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem ps = particleSystems[i];
                if (ps == null) continue;

                var main = ps.main;
                string name = ps.name.ToLowerInvariant();

                bool isSmokeOrDust = name.Contains("smoke") ||
                                     name.Contains("dust") ||
                                     name.Contains("cloud") ||
                                     name.Contains("debris") ||
                                     name.Contains("linger") ||
                                     main.startLifetimeMultiplier >= 3.5f;

                bool isFlashOrShock = name.Contains("flash") ||
                                      name.Contains("shock") ||
                                      name.Contains("burst") ||
                                      name.Contains("fireball");

                if (isSmokeOrDust)
                {
                    // Scale particle size based on TNT yield
                    main.startSizeMultiplier *= yieldScale;

                    // Prolong lingering smoke duration (2.5x to 3.0x default ~2.8x)
                    float dynamicBonus = Mathf.Lerp(smokeLifetimeMultiplier * 0.9f, smokeLifetimeMultiplier * 1.1f, Mathf.Clamp01(yieldScale / 2.5f));
                    main.startLifetimeMultiplier *= dynamicBonus;

                    // Ensure smooth alpha fade to prevent abrupt pop-out
                    ApplySmoothAlphaFade(ps);

                    // Expand emission shape radius slightly with yield
                    var shape = ps.shape;
                    if (shape.enabled)
                    {
                        shape.radius *= Mathf.Lerp(1.0f, yieldScale, 0.5f);
                    }
                }
                else if (isFlashOrShock)
                {
                    // High-energy flashes expand with yield but dissipate rapidly
                    main.startSizeMultiplier *= yieldScale;
                    main.startLifetimeMultiplier *= Mathf.Clamp(Mathf.Pow(yieldScale, 0.35f), 0.75f, 1.35f);
                }
                else
                {
                    // General particle systems (sparks, shrapnel, secondary ejecta)
                    main.startSizeMultiplier *= yieldScale;
                }
            }
        }

        private static void ApplySmoothAlphaFade(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;

            // Gradient: full alpha during main billowing phase,
            // followed by a smooth cubic fade-out to zero over the last 40% of lifetime
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0.0f),
                    new GradientColorKey(Color.white, 1.0f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.0f, 0.0f),
                    new GradientAlphaKey(1.0f, 0.12f),
                    new GradientAlphaKey(1.0f, 0.60f),
                    new GradientAlphaKey(0.0f, 1.0f)
                }
            );

            col.color = new ParticleSystem.MinMaxGradient(gradient);
        }
    }

    /// <summary>
    /// Procedural inverted mesh sphere generator, lightweight URP-compatible distortion shader setup,
    /// and allocation-free runtime controller.
    /// </summary>
    internal static class ShockwaveDistortionManager
    {
        private static Mesh _cachedInvertedSphereMesh;
        private static Material _cachedDistortionMaterial;
        private static Texture2D _cachedNormalMap;

        public static void SpawnShockwave(Vector3 position, float blastYield, float intensity)
        {
            EnsureResourcesInitialized();

            // Calculate yield-based expansion radius & lifespan (0.35s to 0.60s)
            float safeYield = Mathf.Max(blastYield, 1f);
            float yieldScale = Mathf.Pow(safeYield / 250f, 0.3333333f);
            float maxRadius = Mathf.Clamp(42f * yieldScale, 15f, 450f);
            float lifespan = Mathf.Clamp(0.35f + Mathf.Log10(safeYield + 1f) * 0.045f, 0.35f, 0.60f);

            GameObject shockwaveObj = new GameObject("BLIND_ShockwaveDistortion");

            // Attach to Nuclear Option's Floating Origin root if active
            if (Datum.origin != null)
            {
                shockwaveObj.transform.SetParent(Datum.origin, false);
            }
            shockwaveObj.transform.position = position;

            var filter = shockwaveObj.AddComponent<MeshFilter>();
            filter.sharedMesh = _cachedInvertedSphereMesh;

            var renderer = shockwaveObj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _cachedDistortionMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var controller = shockwaveObj.AddComponent<ShockwaveOpticalDistortion>();
            controller.Initialize(lifespan, maxRadius, intensity);
        }

        private static void EnsureResourcesInitialized()
        {
            if (_cachedInvertedSphereMesh == null)
            {
                _cachedInvertedSphereMesh = CreateInvertedSphereMesh(18, 24);
            }

            if (_cachedNormalMap == null)
            {
                _cachedNormalMap = CreateProceduralWaveNormalMap(64, 64);
            }

            if (_cachedDistortionMaterial == null)
            {
                _cachedDistortionMaterial = CreateDistortionMaterial(_cachedNormalMap);
            }
        }

        private static Mesh CreateInvertedSphereMesh(int rings, int sectors)
        {
            Mesh mesh = new Mesh { name = "BLIND_ProceduralInvertedSphere" };

            int vertexCount = (rings + 1) * (sectors + 1);
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[rings * sectors * 6];

            float rStep = Mathf.PI / rings;
            float sStep = (Mathf.PI * 2f) / sectors;

            int vIndex = 0;
            for (int r = 0; r <= rings; r++)
            {
                float phi = r * rStep;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                for (int s = 0; s <= sectors; s++)
                {
                    float theta = s * sStep;
                    float x = sinPhi * Mathf.Cos(theta);
                    float y = cosPhi;
                    float z = sinPhi * Mathf.Sin(theta);

                    Vector3 pos = new Vector3(x, y, z);
                    vertices[vIndex] = pos;
                    // Inward-facing normals for rim refraction from any perspective
                    normals[vIndex] = -pos;
                    uvs[vIndex] = new Vector2((float)s / sectors, (float)r / rings);
                    vIndex++;
                }
            }

            int tIndex = 0;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < sectors; s++)
                {
                    int current = r * (sectors + 1) + s;
                    int next = current + sectors + 1;

                    // Clockwise winding for interior face visibility
                    triangles[tIndex++] = current;
                    triangles[tIndex++] = current + 1;
                    triangles[tIndex++] = next;

                    triangles[tIndex++] = next;
                    triangles[tIndex++] = current + 1;
                    triangles[tIndex++] = next + 1;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Texture2D CreateProceduralWaveNormalMap(int width, int height)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "BLIND_ProceduralShockwaveNormals"
            };

            Color[] pixels = new Color[width * height];
            float cx = (width - 1) * 0.5f;
            float cy = (height - 1) * 0.5f;
            float maxR = Mathf.Min(cx, cy);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = (x - cx) / maxR;
                    float dy = (y - cy) / maxR;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Radial steep shock front curve
                    float wave = Mathf.Sin(dist * Mathf.PI * 4f) * Mathf.Exp(-dist * 2.5f);
                    float nx = dx * wave;
                    float ny = dy * wave;
                    float nz = Mathf.Sqrt(Mathf.Clamp01(1f - (nx * nx + ny * ny)));

                    pixels[y * width + x] = new Color(nx * 0.5f + 0.5f, ny * 0.5f + 0.5f, nz * 0.5f + 0.5f, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Material CreateDistortionMaterial(Texture2D normalMap)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                            Shader.Find("Universal Render Pipeline/Particles/Simple Lit") ??
                            Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Particles/Standard Unlit");

            Material mat = new Material(shader) { name = "M_BLIND_ShockwaveDistortion" };

            // URP Transparent Rendering parameters
            mat.SetFloat("_Surface", 1f); // 1 = Transparent
            mat.SetFloat("_Blend", 0f);   // 0 = Alpha Blend
            mat.SetFloat("_Cull", 0f);    // 0 = Cull Off (double-sided distortion)
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)RenderQueue.Transparent + 100;

            // Optical refraction settings
            mat.EnableKeyword("_DISTORTION_ON");
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_BumpMap", normalMap);
            mat.SetFloat("_DistortionStrength", 55f);
            mat.SetFloat("_DistortionBlend", 0.75f);
            mat.SetColor("_BaseColor", new Color(0.95f, 0.98f, 1.0f, 0.25f));

            return mat;
        }
    }

    /// <summary>
    /// Runtime shockwave expansion controller.
    /// Incurrs 0 B of GC memory allocations per frame in Update loop.
    /// </summary>
    public class ShockwaveOpticalDistortion : MonoBehaviour
    {
        private float _lifespan;
        private float _maxRadius;
        private float _intensity;
        private float _elapsed;

        private MeshRenderer _meshRenderer;
        private MaterialPropertyBlock _propBlock;

        private static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int PropColor = Shader.PropertyToID("_Color");
        private static readonly int PropDistortionStrength = Shader.PropertyToID("_DistortionStrength");

        public void Initialize(float lifespan, float maxRadius, float intensity)
        {
            _lifespan = Mathf.Max(0.1f, lifespan);
            _maxRadius = maxRadius;
            _intensity = Mathf.Max(0.1f, intensity);
            _elapsed = 0f;

            _meshRenderer = GetComponent<MeshRenderer>();
            _propBlock = new MaterialPropertyBlock();

            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed / _lifespan;

            if (t >= 1.0f)
            {
                Destroy(gameObject);
                return;
            }

            // Supersonic blast wave expansion: Rapid cubic ease-out
            // Shock front decelerates rapidly as ambient air resistance increases
            float invT = 1.0f - t;
            float expansionFactor = 1.0f - (invT * invT * invT);
            float currentDiameter = (_maxRadius * expansionFactor) * 2f;

            // Zero-allocation transform scaling via struct assignment
            transform.localScale = new Vector3(currentDiameter, currentDiameter, currentDiameter);

            // Shimmer alpha curve: Onset spike followed by smooth decay to 0 at t=1.0
            float alpha = Mathf.Sin(t * Mathf.PI) * (1.0f - (t * 0.65f));
            alpha = Mathf.Clamp01(alpha * 0.75f * _intensity);

            if (_meshRenderer != null)
            {
                _meshRenderer.GetPropertyBlock(_propBlock);
                Color shimmerColor = new Color(0.95f, 0.98f, 1.0f, alpha);
                _propBlock.SetColor(PropBaseColor, shimmerColor);
                _propBlock.SetColor(PropColor, shimmerColor);
                _propBlock.SetFloat(PropDistortionStrength, alpha * 65f * _intensity);
                _meshRenderer.SetPropertyBlock(_propBlock);
            }
        }
    }
}
