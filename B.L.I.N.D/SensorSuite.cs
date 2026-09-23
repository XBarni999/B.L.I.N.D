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
        private readonly BlindPlugin _plugin;
        private readonly ThermalRenderer _thermal;
        private VolumeProfile _originalProfile;
        private TargetCam _targetCam;
        private Camera _camera;
        private Volume _volume;
        private VolumeProfile _profile;
        private ColorAdjustments _color;
        private Bloom _bloom;
        private FilmGrain _grain;
        private ColorLookup _lookup;
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
            _thermal = new ThermalRenderer(plugin);
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
            _thermal.SetCamera(null, SensorMode.Color);
            RestoreNeutralProfile();
            if (_subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                _subscribed = false;
            }
            _thermal.Dispose();
            ReleaseProfile();
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
            _subscribed = true;
        }

        private void Attach(TargetCam targetCam)
        {
            _thermal.SetCamera(null, SensorMode.Color);
            RestoreNeutralProfile();
            ReleaseProfile();
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
            _originalProfile = _volume == null ? null : _volume.profile;
            if (_originalProfile != null)
            {
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                foreach (var component in _originalProfile.components)
                    _profile.components.Add(UnityEngine.Object.Instantiate(component));
            }
            if (_volume != null && _profile != null) _volume.profile = _profile;
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
            _thermal.SetCamera(_camera, Mode);

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
                _color.postExposure.value = Mathf.Lerp(2.2f, 3.8f, dayBlind);
                _color.contrast.value = Mathf.Lerp(18f, -12f, dayBlind);
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

            // The thermal pass supplies its own radiance/palette and bypasses visible-light postprocessing.
            RestoreNeutralProfile();
            if (IrModeField != null) IrModeField.SetValue(_targetCam, _thermal.Ready());
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

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera) ApplyMode();
        }

        private void ReleaseProfile()
        {
            if (_volume != null && _originalProfile != null) _volume.profile = _originalProfile;
            if (_profile != null)
            {
                foreach (var component in _profile.components) UnityEngine.Object.Destroy(component);
                UnityEngine.Object.Destroy(_profile);
            }
            _profile = null;
            _originalProfile = null;
        }
    }
}
