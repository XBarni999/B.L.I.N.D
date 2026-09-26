using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BLIND
{
    internal enum SensorMode
    {
        VanillaIR,
        Ironbow
    }

    // Both modes use the game's own target camera and IR image. Ironbow only
    // changes the final color grade of that image in its local VolumeProfile.
    internal sealed class SensorSuite
    {
        private static readonly FieldInfo CameraField = AccessTools.Field(typeof(TargetCam), "cam");
        private static readonly FieldInfo VolumeField = AccessTools.Field(typeof(TargetCam), "screenVolume");
        private static readonly FieldInfo IrModeField = AccessTools.Field(typeof(TargetCam), "IRMode");
        private readonly BlindPlugin plugin;
        private TargetCam targetCam;
        private Volume volume;
        private VolumeProfile originalProfile;
        private VolumeProfile profile;
        private ColorAdjustments color;
        private ColorLookup lookup;
        private Texture2D ironbowLut;
        private float messageUntil;
        private float messageStarted;
        private bool warnedLut;

        internal SensorMode Mode { get; private set; }
        internal string ModeLabel { get { return Mode == SensorMode.Ironbow ? "IRONBOW" : "STANDARD IR"; } }
        internal float ModeMessageAlpha
        {
            get
            {
                float now = Time.unscaledTime;
                if (now >= messageUntil) return 0f;
                float elapsed = now - messageStarted;
                if (elapsed < 0.18f) return Mathf.Clamp01(elapsed / 0.18f);
                float remaining = messageUntil - now;
                return remaining < 0.45f ? Mathf.Clamp01(remaining / 0.45f) : 1f;
            }
        }

        internal SensorSuite(BlindPlugin plugin) { this.plugin = plugin; }

        internal void Update(Aircraft aircraft)
        {
            if (plugin.SensorModeKey.Value.IsDown())
            {
                Mode = Mode == SensorMode.VanillaIR ? SensorMode.Ironbow : SensorMode.VanillaIR;
                messageStarted = Time.unscaledTime;
                messageUntil = messageStarted + 2.5f;
                BlindPlugin.LogSource.LogMessage("[B.L.I.N.D.] Sensor mode: " + ModeLabel);
            }

            TargetCam next = aircraft == null ? null : aircraft.targetCam;
            if (next != targetCam || (next != null && profile == null)) Attach(next);
            ApplyMode();
        }

        internal void LateUpdate() { ApplyMode(); }

        internal void Shutdown()
        {
            ReleaseProfile();
            if (ironbowLut != null) UnityEngine.Object.Destroy(ironbowLut);
            ironbowLut = null;
            targetCam = null;
        }

        private void Attach(TargetCam next)
        {
            ReleaseProfile();
            targetCam = next;
            if (targetCam == null) return;
            volume = VolumeField == null ? null : VolumeField.GetValue(targetCam) as Volume;
            originalProfile = volume == null ? null : volume.profile;
            if (originalProfile == null) return;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            foreach (VolumeComponent component in originalProfile.components)
                profile.components.Add(UnityEngine.Object.Instantiate(component));
            volume.profile = profile;
            if (!profile.TryGet(out color)) color = profile.Add<ColorAdjustments>(true);
            if (!profile.TryGet(out lookup)) lookup = profile.Add<ColorLookup>(false);
        }

        private void ApplyMode()
        {
            if (targetCam == null || profile == null || color == null) return;

            // These are the same camera settings for both modes. No scene mesh,
            // particle, depth, or IRSource is re-rendered by BLIND.
            float ambient = NetworkSceneSingleton<LevelInfo>.i == null
                ? 0.2f : NetworkSceneSingleton<LevelInfo>.i.GetAmbientLight();
            float daylight = Mathf.InverseLerp(0.02f, 0.4f, ambient);
            color.active = true;
            color.saturation.overrideState = true;
            color.saturation.value = -100f;
            color.contrast.overrideState = true;
            color.contrast.value = 1f;
            color.postExposure.overrideState = true;
            color.postExposure.value = Mathf.Lerp(3f, -0.5f, daylight);
            if (IrModeField != null) IrModeField.SetValue(targetCam, true);

            if (lookup == null) return;
            bool ironbow = Mode == SensorMode.Ironbow && EnsureLut();
            lookup.active = ironbow;
            lookup.texture.overrideState = ironbow;
            lookup.texture.value = ironbow ? ironbowLut : null;
            lookup.contribution.overrideState = ironbow;
            lookup.contribution.value = ironbow ? 1f : 0f;
        }

        private bool EnsureLut()
        {
            UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;
            if (asset == null)
            {
                if (!warnedLut)
                {
                    warnedLut = true;
                    BlindPlugin.LogSource.LogWarning("[Ironbow] URP asset unavailable; keeping native IR.");
                }
                return false;
            }
            int size = asset.colorGradingLutSize;
            if (ironbowLut != null && ironbowLut.height == size) return true;
            if (ironbowLut != null) UnityEngine.Object.Destroy(ironbowLut);
            ironbowLut = new Texture2D(size * size, size, TextureFormat.RGBA32, false, true)
            {
                name = "BLIND Ironbow color lookup",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color[] pixels = new Color[size * size * size];
            float step = 1f / (size - 1f);
            for (int b = 0; b < size; b++)
                for (int g = 0; g < size; g++)
                    for (int r = 0; r < size; r++)
                    {
                        float brightness = (0.2126f * r + 0.7152f * g + 0.0722f * b) * step;
                        int x = b * size + r;
                        pixels[g * size * size + x] = Ironbow(brightness);
                    }
            ironbowLut.SetPixels(pixels);
            ironbowLut.Apply(false, true);
            BlindPlugin.LogSource.LogInfo("[Ironbow] Native IR color lookup ready: " + size + " levels.");
            return true;
        }

        private static Color Ironbow(float t)
        {
            Color black = new Color(0.015f, 0.008f, 0.035f);
            Color violet = new Color(0.18f, 0.025f, 0.30f);
            Color red = new Color(0.62f, 0.04f, 0.22f);
            Color orange = new Color(0.95f, 0.30f, 0.025f);
            Color yellow = new Color(1f, 0.77f, 0.15f);
            Color white = new Color(1f, 0.99f, 0.94f);
            if (t < 0.2f) return Color.Lerp(black, violet, t / 0.2f);
            if (t < 0.4f) return Color.Lerp(violet, red, (t - 0.2f) / 0.2f);
            if (t < 0.65f) return Color.Lerp(red, orange, (t - 0.4f) / 0.25f);
            if (t < 0.85f) return Color.Lerp(orange, yellow, (t - 0.65f) / 0.2f);
            return Color.Lerp(yellow, white, (t - 0.85f) / 0.15f);
        }

        private void ReleaseProfile()
        {
            if (volume != null && originalProfile != null) volume.profile = originalProfile;
            if (profile != null)
            {
                foreach (VolumeComponent component in profile.components)
                    UnityEngine.Object.Destroy(component);
                UnityEngine.Object.Destroy(profile);
            }
            volume = null;
            originalProfile = null;
            profile = null;
            color = null;
            lookup = null;
        }
    }
}
