using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BLIND
{
    internal enum SensorMode
    {
        Color,
        FlirIronbow,
        FlirWhiteHot,
        FlirBlackHot,
        NightVision
    }

    internal sealed class SensorSuite
    {
        private static readonly FieldInfo CameraField = AccessTools.Field(typeof(TargetCam), "cam");
        private static readonly FieldInfo VolumeField = AccessTools.Field(typeof(TargetCam), "screenVolume");
        private static readonly FieldInfo IrModeField = AccessTools.Field(typeof(TargetCam), "IRMode");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");

        private struct CachedRenderer
        {
            public Renderer Renderer;
            public bool IsHotPart;
            public bool IsSmokeOrTrail;
        }

        private class CachedUnit
        {
            public Unit Unit;
            public CachedRenderer[] Renderers;
            public float NextRefreshTime;
        }

        private readonly BlindPlugin _plugin;
        private readonly List<Renderer> _modifiedRenderers = new List<Renderer>(256);
        private readonly HashSet<Material> _emissionKeywordsToRestore = new HashSet<Material>();
        private readonly Dictionary<Unit, CachedUnit> _unitCache = new Dictionary<Unit, CachedUnit>();
        private readonly List<Unit> _unitsToPurge = new List<Unit>(16);
        private readonly MaterialPropertyBlock _thermalBlock = new MaterialPropertyBlock();
        private readonly Plane[] _frustumPlanes = new Plane[6];

        private TargetCam _targetCam;
        private Camera _camera;
        private Volume _volume;
        private VolumeProfile _profile;
        private ColorAdjustments _color;
        private Bloom _bloom;
        private FilmGrain _grain;
        private ColorLookup _lookup;
        private Texture2D _ironbowLut;
        private Texture2D _whiteHotLut;
        private Texture2D _blackHotLut;
        private bool _subscribed;
        private bool _bloomOriginalActive;
        private float _bloomOriginalIntensity;
        private float _bloomOriginalThreshold;
        private bool _grainOriginalActive;
        private float _grainOriginalIntensity;
        private float _modeMessageUntil;
        private float _modeMessageStartedAt;

        internal SensorMode Mode { get; private set; }
        internal string ModeLabel
        {
            get
            {
                switch (Mode)
                {
                    case SensorMode.FlirIronbow: return "FLIR IRONBOW";
                    case SensorMode.FlirWhiteHot: return "FLIR WHITE HOT";
                    case SensorMode.FlirBlackHot: return "FLIR BLACK HOT";
                    case SensorMode.NightVision: return "NVG";
                    default: return "COLOR";
                }
            }
        }

        internal bool ShowModeMessage { get { return Time.unscaledTime < _modeMessageUntil; } }

        internal float ModeMessageAlpha
        {
            get
            {
                float now = Time.unscaledTime;
                if (now >= _modeMessageUntil) return 0f;
                float elapsed = now - _modeMessageStartedAt;
                if (elapsed < 0.18f) return Mathf.Clamp01(elapsed / 0.18f);
                float remaining = _modeMessageUntil - now;
                if (remaining < 0.45f) return Mathf.Clamp01(remaining / 0.45f);
                return 1f;
            }
        }

        internal SensorSuite(BlindPlugin plugin)
        {
            _plugin = plugin;
            Subscribe();
        }

        internal void Update(Aircraft aircraft)
        {
            if (_plugin.SensorModeKey.Value.IsDown())
            {
                Mode = (SensorMode)(((int)Mode + 1) % 5);
                _modeMessageStartedAt = Time.unscaledTime;
                _modeMessageUntil = Time.unscaledTime + 2.5f;
                BlindPlugin.LogSource.LogMessage("[B.L.I.N.D.] Sensor mode: " + ModeLabel);
            }

            TargetCam targetCam = aircraft == null ? null : aircraft.targetCam;
            if (targetCam != _targetCam || (targetCam != null && _profile == null))
            {
                Attach(targetCam);
            }

            if (_targetCam != null && _profile != null)
            {
                ApplyMode();
            }
        }

        internal void LateUpdate()
        {
            if (_targetCam != null && _profile != null)
            {
                ApplyMode();
            }
        }

        internal void Shutdown()
        {
            RestoreRenderers();
            RestoreNeutralProfile();
            if (_subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
                _subscribed = false;
            }
            DestroyLut(ref _ironbowLut);
            DestroyLut(ref _whiteHotLut);
            DestroyLut(ref _blackHotLut);
            _unitCache.Clear();
        }

        internal bool IsAtmosphereLimited(TargetCam targetCam, Camera camera)
        {
            return targetCam != null && targetCam == _targetCam && camera == _camera &&
                   (Mode == SensorMode.FlirIronbow || Mode == SensorMode.FlirWhiteHot ||
                    Mode == SensorMode.FlirBlackHot);
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            _subscribed = true;
        }

        private void Attach(TargetCam targetCam)
        {
            RestoreRenderers();
            RestoreNeutralProfile();
            _targetCam = targetCam;
            _camera = null;
            _volume = null;
            _profile = null;
            _color = null;
            _bloom = null;
            _grain = null;
            _lookup = null;

            if (_targetCam == null) return;

            _camera = CameraField == null ? null : CameraField.GetValue(_targetCam) as Camera;
            _volume = VolumeField == null ? null : VolumeField.GetValue(_targetCam) as Volume;
            _profile = _volume == null ? null : _volume.profile;
            if (_profile == null) return;

            if (!_profile.TryGet(out _color))
            {
                _color = _profile.Add<ColorAdjustments>(true);
            }
            if (!_profile.TryGet(out _bloom))
            {
                _bloom = _profile.Add<Bloom>(false);
            }
            if (!_profile.TryGet(out _grain))
            {
                _grain = _profile.Add<FilmGrain>(false);
            }
            if (!_profile.TryGet(out _lookup))
            {
                _lookup = _profile.Add<ColorLookup>(false);
            }

            _bloomOriginalActive = _bloom.active;
            _bloomOriginalIntensity = _bloom.intensity.value;
            _bloomOriginalThreshold = _bloom.threshold.value;
            _grainOriginalActive = _grain.active;
            _grainOriginalIntensity = _grain.intensity.value;
        }

        private void ApplyMode()
        {
            if (_color == null) return;

            if (Mode == SensorMode.Color)
            {
                RestoreNeutralProfile();
                float ambient = NetworkSceneSingleton<LevelInfo>.i == null
                    ? 0.2f
                    : NetworkSceneSingleton<LevelInfo>.i.GetAmbientLight();
                float daylight = Mathf.InverseLerp(0.02f, 0.4f, ambient);
                _color.postExposure.overrideState = true;
                _color.contrast.overrideState = true;
                _color.postExposure.value = Mathf.Lerp(0.5f, -1f, daylight);
                _color.contrast.value = 5f;
                if (IrModeField != null) IrModeField.SetValue(_targetCam, false);
                return;
            }

            _color.active = true;
            _color.postExposure.overrideState = true;
            _color.contrast.overrideState = true;
            _color.saturation.overrideState = true;
            _color.colorFilter.overrideState = true;

            if (Mode == SensorMode.NightVision)
            {
                float ambient = 0.1f;
                if (NetworkSceneSingleton<LevelInfo>.i != null)
                {
                    ambient = NetworkSceneSingleton<LevelInfo>.i.GetAmbientLight();
                }
                float dayBlind = Mathf.InverseLerp(0.32f, 0.65f, ambient);
                _color.postExposure.value = Mathf.Lerp(2.2f, 8f, dayBlind);
                _color.contrast.value = Mathf.Lerp(18f, -30f, dayBlind);
                _color.saturation.value = -72f;
                _color.colorFilter.value = new Color(0.48f, 1f, 0.56f, 1f);
                _lookup.active = false;
                _lookup.contribution.value = 0f;
                _grain.active = true;
                _grain.intensity.overrideState = true;
                _grain.response.overrideState = true;
                _grain.intensity.value = 0.38f;
                _grain.response.value = 0.55f;
                _bloom.active = true;
                _bloom.intensity.overrideState = true;
                _bloom.threshold.overrideState = true;
                _bloom.intensity.value = 1.15f;
                _bloom.threshold.value = 0.72f;
                if (IrModeField != null) IrModeField.SetValue(_targetCam, false);
                return;
            }

            float cloudOcclusion = 0f;
            if (NetworkSceneSingleton<LevelInfo>.i != null && _camera != null)
            {
                cloudOcclusion = NetworkSceneSingleton<LevelInfo>.i.GetCloudOcclusion(_camera.transform.position);
            }

            // Калібрування військового FLIR:
            // Для White-Hot: базова експозиція -0.75f тримає холодний фон (бетон, земля) в діапазоні 0.10-0.35,
            // дозволяючи гарячим юнітам з HDR емісією яскраво сяяти (0.7-1.0) без засвітлення неба.
            // Для Black-Hot: експозиція -0.50f забезпечує чистий світлий фон з глибоким темним силуетом техніки.
            // Індивідуальні налаштування контрасту:
            // Для White-Hot піднято контрастність (34f) та оптимізовано експозицію (-0.72f),
            // щоб техніка виділялася яскравим білим силуетом на темному тактичному фоні.
            float baseExposure;
            float baseContrast;

            if (Mode == SensorMode.FlirWhiteHot)
            {
                baseExposure = -0.72f;
                baseContrast = 34f;
            }
            else if (Mode == SensorMode.FlirBlackHot)
            {
                baseExposure = -0.65f;
                baseContrast = 26f;
            }
            else // Ironbow
            {
                baseExposure = -0.65f;
                baseContrast = 18f;
            }

            _color.postExposure.value = baseExposure - cloudOcclusion * 0.3f;
            _color.contrast.value = Mathf.Lerp(baseContrast, 10f, cloudOcclusion);
            _color.saturation.value = -100f;
            _color.colorFilter.value = Color.white;

            // Реалістичний мікрошум болометричної матриці FLIR
            _grain.active = true;
            _grain.intensity.overrideState = true;
            _grain.response.overrideState = true;
            _grain.intensity.value = Mathf.Lerp(0.06f, 0.14f, cloudOcclusion);
            _grain.response.value = 0.85f;

            // Оптичний тепловий ореол (Bloom) на соплах та вогні
            _bloom.active = true;
            _bloom.intensity.overrideState = true;
            _bloom.threshold.overrideState = true;
            _bloom.intensity.value = 0.40f;
            _bloom.threshold.value = 1.15f;

            EnsureThermalLuts();
            _lookup.active = true;
            _lookup.contribution.overrideState = true;
            _lookup.contribution.value = 1f;
            _lookup.texture.overrideState = true;
            _lookup.texture.value = Mode == SensorMode.FlirIronbow
                ? _ironbowLut
                : (Mode == SensorMode.FlirBlackHot ? _blackHotLut : _whiteHotLut);
            if (IrModeField != null) IrModeField.SetValue(_targetCam, true);
        }

        private void RestoreNeutralProfile()
        {
            if (_color != null)
            {
                _color.saturation.overrideState = false;
                _color.colorFilter.overrideState = false;
            }
            if (_lookup != null)
            {
                _lookup.active = false;
                _lookup.contribution.value = 0f;
            }
            if (_grain != null)
            {
                _grain.active = _grainOriginalActive;
                _grain.intensity.value = _grainOriginalIntensity;
            }
            if (_bloom != null)
            {
                _bloom.active = _bloomOriginalActive;
                _bloom.intensity.value = _bloomOriginalIntensity;
                _bloom.threshold.value = _bloomOriginalThreshold;
            }
        }

        private void EnsureThermalLuts()
        {
            if (_ironbowLut != null && _whiteHotLut != null && _blackHotLut != null) return;
            _ironbowLut = CreateThermalLut("BLIND_Ironbow_LUT", Ironbow);
            _whiteHotLut = CreateThermalLut("BLIND_WhiteHot_LUT", WhiteHot);
            _blackHotLut = CreateThermalLut("BLIND_BlackHot_LUT", BlackHot);
        }

        // White Hot: Висококонтрастний військовий тепловізор з чітким виділенням цілей
        private static Color WhiteHot(float value)
        {
            float v = Mathf.Clamp01(value);
            float lum;
            if (v < 0.30f)
            {
                // Холодний фон, небо, тіні: глибокий темний тон (0.02 - 0.12)
                lum = Mathf.Lerp(0.02f, 0.12f, v / 0.30f);
            }
            else if (v < 0.55f)
            {
                // Дороги, земля, бетонні стіни: спокійний приглушений темно-сірий (0.12 - 0.28)
                lum = Mathf.Lerp(0.12f, 0.28f, (v - 0.30f) / 0.25f);
            }
            else if (v < 0.78f)
            {
                // Корпус активної техніки та ракети: різкий стрибок у високу яскравість (0.28 -> 0.82)
                lum = Mathf.Lerp(0.28f, 0.82f, Mathf.Pow((v - 0.55f) / 0.23f, 0.85f));
            }
            else
            {
                // Розпечені сопла, двигуни, вибухи, траки: сліпучий білий (0.82 -> 1.0)
                lum = Mathf.Lerp(0.82f, 1.00f, (v - 0.78f) / 0.22f);
            }
            return new Color(lum, lum, lum, 1f);
        }

        // Black Hot: Висококонтрастна інверсія White-Hot
        private static Color BlackHot(float value)
        {
            float v = Mathf.Clamp01(value);
            float lum;
            if (v < 0.30f)
            {
                // Світлий холодний фон (0.98 - 0.88)
                lum = Mathf.Lerp(0.98f, 0.88f, v / 0.30f);
            }
            else if (v < 0.55f)
            {
                // Дороги, земля, бетон: світло-сірий (0.88 - 0.70)
                lum = Mathf.Lerp(0.88f, 0.70f, (v - 0.30f) / 0.25f);
            }
            else if (v < 0.78f)
            {
                // Техніка та ракети: різкий перехід у глибокий графіт (0.70 -> 0.16)
                lum = Mathf.Lerp(0.70f, 0.16f, Mathf.Pow((v - 0.55f) / 0.23f, 0.85f));
            }
            else
            {
                // Сопла, двигуни, вибухи: абсолютний чорний (0.16 -> 0.00)
                lum = Mathf.Lerp(0.16f, 0.00f, (v - 0.78f) / 0.22f);
            }
            return new Color(lum, lum, lum, 1f);
        }

        private static Texture2D CreateThermalLut(string name, Func<float, Color> palette)
        {
            int size = 32;
            Texture2D lut = new Texture2D(size * size, size, TextureFormat.RGBA32, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color[] pixels = new Color[size * size * size];
            for (int blue = 0; blue < size; blue++)
            {
                for (int green = 0; green < size; green++)
                {
                    for (int red = 0; red < size; red++)
                    {
                        float r = red / (float)(size - 1);
                        float g = green / (float)(size - 1);
                        float b = blue / (float)(size - 1);
                        float luminance = r * 0.2126f + g * 0.7152f + b * 0.0722f;
                        int x = red + blue * size;
                        int y = green;
                        pixels[y * size * size + x] = palette(Mathf.Clamp01(luminance));
                    }
                }
            }
            lut.SetPixels(pixels);
            lut.Apply(false, true);
            return lut;
        }

        private static Color Ironbow(float value)
        {
            Color black = new Color(0.01f, 0.005f, 0.02f, 1f);
            Color violet = new Color(0.22f, 0.03f, 0.38f, 1f);
            Color magenta = new Color(0.68f, 0.05f, 0.42f, 1f);
            Color orange = new Color(0.95f, 0.35f, 0.02f, 1f);
            Color yellow = new Color(1f, 0.92f, 0.15f, 1f);
            if (value < 0.25f) return Color.Lerp(black, violet, value / 0.25f);
            if (value < 0.50f) return Color.Lerp(violet, magenta, (value - 0.25f) / 0.25f);
            if (value < 0.75f) return Color.Lerp(magenta, orange, (value - 0.50f) / 0.25f);
            if (value < 0.92f) return Color.Lerp(orange, yellow, (value - 0.75f) / 0.17f);
            return Color.Lerp(yellow, Color.white, (value - 0.92f) / 0.08f);
        }

        private static void DestroyLut(ref Texture2D texture)
        {
            if (texture == null) return;
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera != _camera ||
                (Mode != SensorMode.FlirIronbow && Mode != SensorMode.FlirWhiteHot &&
                 Mode != SensorMode.FlirBlackHot))
            {
                return;
            }

            RestoreRenderers();

            GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
            Vector3 camPos = camera.transform.position;
            GlobalPosition camGlobalPos = camPos.ToGlobalPosition();

            List<Unit> allUnits = UnitRegistry.allUnits;
            int unitCount = allUnits != null ? allUnits.Count : 0;
            float now = Time.timeSinceLevelLoad;

            for (int i = 0; i < unitCount; i++)
            {
                Unit unit = allUnits[i];
                if (unit == null || unit.disabled) continue;
                if (FastMath.OutOfRange(unit.GlobalPosition(), camGlobalPos, 20000f))
                {
                    continue;
                }

                CachedUnit cached = GetOrCreateCachedUnit(unit, now);
                if (cached == null || cached.Renderers == null || cached.Renderers.Length == 0)
                {
                    continue;
                }

                float heat = GetHeat(unit);
                bool isMissile = unit is Missile;
                Missile missile = isMissile ? (Missile)unit : null;
                bool motorBurning = isMissile && (missile.EngineOn() || missile.GetThrust() > 0.05f || missile.timeSinceSpawn < 7f);

                for (int j = 0; j < cached.Renderers.Length; j++)
                {
                    Renderer renderer = cached.Renderers[j].Renderer;
                    if (renderer == null || !renderer.enabled) continue;
                    if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, renderer.bounds))
                    {
                        continue;
                    }

                    bool hotPart = cached.Renderers[j].IsHotPart;
                    bool isSmokeOrTrail = cached.Renderers[j].IsSmokeOrTrail;
                    float rendererHeat = hotPart ? Mathf.Clamp01(heat + 0.35f) : heat;

                    // Зберігаємо справжній рельєф, нормалі та геометрію моделі:
                    // BaseColor масштабує наявні текстури (не затираючи шви та затінення),
                    // а EmissionColor додає фізичне теплове самосвітіння об'єкта.
                    float baseLevel;
                    float emissionStrength;

                    if (isMissile)
                    {
                        if (hotPart)
                        {
                            // Сопло та факел ракети: розпечене сонце (Bloom)
                            baseLevel = motorBurning ? 4.8f : 3.0f;
                            emissionStrength = motorBurning ? 5.5f : 2.5f;
                        }
                        else if (isSmokeOrTrail)
                        {
                            // Дим від ракети та інверсійний слід
                            baseLevel = motorBurning ? 2.6f : 1.8f;
                            emissionStrength = motorBurning ? 2.2f : 1.0f;
                        }
                        else
                        {
                            // Корпус ракети в польоті
                            baseLevel = 2.4f;
                            emissionStrength = 1.2f;
                        }
                    }
                    else if (isSmokeOrTrail)
                    {
                        // Вихлопний дим та гази техніки
                        baseLevel = 2.0f;
                        emissionStrength = 1.1f;
                    }
                    else if (hotPart)
                    {
                        baseLevel = 2.6f + rendererHeat * 1.4f;
                        emissionStrength = 2.2f + rendererHeat * 2.5f;
                    }
                    else
                    {
                        baseLevel = 2.1f + rendererHeat * 1.1f;
                        emissionStrength = 0.65f + rendererHeat * 1.15f;
                    }

                    // Активуємо емісію на матеріалах для коректного прорахунку URP
                    Material[] materials = renderer.sharedMaterials;
                    for (int m = 0; m < materials.Length; m++)
                    {
                        Material mat = materials[m];
                        if (mat != null && !mat.IsKeywordEnabled("_EMISSION"))
                        {
                            mat.EnableKeyword("_EMISSION");
                            _emissionKeywordsToRestore.Add(mat);
                        }
                    }

                    _thermalBlock.Clear();
                    Color thermalColor = new Color(baseLevel, baseLevel, baseLevel, 1f);
                    _thermalBlock.SetColor(BaseColorId, thermalColor);
                    _thermalBlock.SetColor(LegacyColorId, thermalColor);
                    _thermalBlock.SetColor(EmissionColorId, thermalColor * emissionStrength);
                    _thermalBlock.SetColor(TintColorId, thermalColor * emissionStrength);

                    renderer.SetPropertyBlock(_thermalBlock);
                    _modifiedRenderers.Add(renderer);
                }
            }

            // Очищення кешу від знищених об'єктів
            if (_unitCache.Count > 64)
            {
                _unitsToPurge.Clear();
                foreach (KeyValuePair<Unit, CachedUnit> kvp in _unitCache)
                {
                    if (kvp.Key == null || kvp.Key.disabled)
                    {
                        _unitsToPurge.Add(kvp.Key);
                    }
                }
                for (int p = 0; p < _unitsToPurge.Count; p++)
                {
                    _unitCache.Remove(_unitsToPurge[p]);
                }
            }
        }

        private CachedUnit GetOrCreateCachedUnit(Unit unit, float now)
        {
            CachedUnit cached;
            if (_unitCache.TryGetValue(unit, out cached))
            {
                if (now < cached.NextRefreshTime) return cached;
            }
            else
            {
                cached = new CachedUnit { Unit = unit };
                _unitCache[unit] = cached;
            }

            // Для ракет оновлюємо частіше (кожні 1.5с), оскільки вони можуть створювати ефекти при пуску
            cached.NextRefreshTime = now + (unit is Missile ? 1.5f : 4f);

            Renderer[] allRenderers = unit.GetComponentsInChildren<Renderer>(true);
            int validCount = 0;
            for (int r = 0; r < allRenderers.Length; r++)
            {
                Renderer rend = allRenderers[r];
                if (rend != null && (rend is MeshRenderer || rend is SkinnedMeshRenderer ||
                                     rend is ParticleSystemRenderer || rend is TrailRenderer))
                {
                    validCount++;
                }
            }

            CachedRenderer[] cachedRenderers = new CachedRenderer[validCount];
            int idx = 0;
            for (int r = 0; r < allRenderers.Length; r++)
            {
                Renderer rend = allRenderers[r];
                if (rend != null && (rend is MeshRenderer || rend is SkinnedMeshRenderer ||
                                     rend is ParticleSystemRenderer || rend is TrailRenderer))
                {
                    cachedRenderers[idx].Renderer = rend;
                    cachedRenderers[idx].IsHotPart = IsHotPart(rend.name);
                    cachedRenderers[idx].IsSmokeOrTrail = IsSmokeOrTrail(rend.name, rend);
                    idx++;
                }
            }
            cached.Renderers = cachedRenderers;
            return cached;
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera)
            {
                RestoreRenderers();
            }
        }

        private static float GetHeat(Unit unit)
        {
            if (unit == null) return 0f;
            if (unit.disabled || unit.unitState == Unit.UnitState.Destroyed)
            {
                return 0.15f; // Остиглий корпус знищеного юніта
            }

            // Кожна активна бойова одиниця має внутрішній нагрів (генератори, гідравліка, живлення)
            float heat = (unit.unitState == Unit.UnitState.Damaged) ? 0.78f : 0.60f;

            // Нагрів від швидкості та тертя
            if (unit.speed > 1f)
            {
                heat += Mathf.Clamp01(unit.speed / 50f) * 0.25f;
            }

            // Нагрів авіаційних силових установок
            Aircraft aircraft = unit as Aircraft;
            if (aircraft != null)
            {
                if (aircraft.Ignition) heat += 0.20f;
                try
                {
                    if (aircraft.engines != null && aircraft.engines.Count > 0)
                    {
                        float totalRpm = 0f;
                        int count = 0;
                        for (int i = 0; i < aircraft.engines.Count; i++)
                        {
                            IEngine eng = aircraft.engines[i];
                            if (eng != null)
                            {
                                totalRpm += Mathf.Clamp01(eng.GetRPMRatio());
                                count++;
                            }
                        }
                        if (count > 0) heat += (totalRpm / count) * 0.45f;
                    }
                }
                catch {}
            }
            else
            {
                GroundVehicle gv = unit as GroundVehicle;
                if (gv != null)
                {
                    // Наземна техніка: бортове живлення, електроніка та двигун
                    heat += 0.22f;
                    if (unit.speed > 0.5f)
                    {
                        heat += Mathf.Clamp01(unit.speed / 20f) * 0.28f;
                    }
                }
                else if (unit is Ship)
                {
                    heat += 0.35f;
                }
                else if (unit is Missile)
                {
                    Missile missile = (Missile)unit;
                    bool motorBurning = missile.EngineOn() || missile.GetThrust() > 0.05f || missile.timeSinceSpawn < 7f;
                    heat += motorBurning ? 2.5f : 1.4f;
                }
            }

            // Робота радарних комплексів (ЗРК, оглядові станції випромінюють високе НВЧ тепло)
            try
            {
                if (unit.radar != null && unit.radar.isActiveAndEnabled)
                {
                    heat += 0.25f;
                }
            }
            catch {}

            // Нагрів стволів гармат та пускових контейнерів після ведення вогню
            try
            {
                if (unit.weaponStations != null && unit.weaponStations.Count > 0)
                {
                    float maxWeaponHeat = 0f;
                    for (int i = 0; i < unit.weaponStations.Count; i++)
                    {
                        WeaponStation ws = unit.weaponStations[i];
                        if (ws == null) continue;
                        float elapsed = Time.timeSinceLevelLoad - ws.LastFiredTime;
                        if (elapsed < 8f && elapsed >= 0f)
                        {
                            float stationHeat = Mathf.Lerp(0.55f, 0f, elapsed / 8f);
                            if (stationHeat > maxWeaponHeat) maxWeaponHeat = stationHeat;
                        }
                    }
                    heat += maxWeaponHeat;
                }
            }
            catch {}

            return Mathf.Clamp01(heat);
        }

        private static bool IsHotPart(string rendererName)
        {
            if (string.IsNullOrEmpty(rendererName)) return false;
            string name = rendererName.ToLowerInvariant();
            return name.Contains("engine") || name.Contains("motor") || name.Contains("exhaust") ||
                   name.Contains("nozzle") || name.Contains("turbine") || name.Contains("barrel") ||
                   name.Contains("gun") || name.Contains("cannon") || name.Contains("turret") ||
                   name.Contains("track") || name.Contains("wheel") || name.Contains("tread") ||
                   name.Contains("radar") || name.Contains("antenna") || name.Contains("radome") ||
                   name.Contains("pipe") || name.Contains("radiator") || name.Contains("vent") ||
                   name.Contains("grill") || name.Contains("cooler") || name.Contains("launcher") ||
                   name.Contains("afterburner") || name.Contains("flame") || name.Contains("plume") ||
                   name.Contains("fire") || name.Contains("thrust");
        }

        private static bool IsSmokeOrTrail(string rendererName, Renderer rend)
        {
            if (rend is ParticleSystemRenderer || rend is TrailRenderer) return true;
            if (string.IsNullOrEmpty(rendererName)) return false;
            string name = rendererName.ToLowerInvariant();
            return name.Contains("smoke") || name.Contains("trail") || name.Contains("plume") ||
                   name.Contains("dust") || name.Contains("exhaust") || name.Contains("particle") ||
                   name.Contains("vapor") || name.Contains("contrail");
        }

        private void RestoreRenderers()
        {
            for (int i = 0; i < _modifiedRenderers.Count; i++)
            {
                Renderer r = _modifiedRenderers[i];
                if (r != null)
                {
                    r.SetPropertyBlock(null);
                }
            }
            _modifiedRenderers.Clear();

            foreach (Material material in _emissionKeywordsToRestore)
            {
                if (material != null)
                {
                    material.DisableKeyword("_EMISSION");
                }
            }
            _emissionKeywordsToRestore.Clear();
        }
    }

    [HarmonyPatch(typeof(TargetCam), "OnBeginCameraRendering")]
    internal static class TargetCamAtmospherePatch
    {
        private static void Postfix(TargetCam __instance, Camera camera)
        {
            BlindRuntime runtime = BlindRuntime.Instance;
            if (runtime != null && runtime.Sensors.IsAtmosphereLimited(__instance, camera))
            {
                RenderSettings.fog = true;
            }
        }
    }
}