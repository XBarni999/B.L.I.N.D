using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BLIND
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("NuclearOption.exe")]
    public sealed class BlindPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ua.ncmod.blind";
        public const string PluginName = "B.L.I.N.D. - Best Luminescence & Infrared Navigation Device";
        public const string PluginVersion = "0.5.0";

        internal static ManualLogSource LogSource;
        internal static BlindPlugin Instance;

        internal ConfigEntry<KeyboardShortcut> SensorModeKey;
        internal ConfigEntry<float> ThermalSpan;
        internal ConfigEntry<float> ThermalNoise;
        internal ConfigEntry<float> WhiteHotCeiling;
        internal ConfigEntry<bool> EnableJtac;
        internal ConfigEntry<float> GroundLaserRange;
        internal ConfigEntry<float> NavalLaserRange;
        internal ConfigEntry<float> AircraftReceiveRange;
        internal ConfigEntry<float> MaxDesignationTime;
        internal ConfigEntry<float> DesignatorCooldown;
        internal ConfigEntry<int> MaxConcurrentDesignations;
        internal ConfigEntry<bool> EnableExplosionOverhaul;
        internal ConfigEntry<float> SmokePersistenceMultiplier;
        internal ConfigEntry<bool> EnableShockwaveDistortion;
        internal ConfigEntry<float> ShockwaveIntensity;

        private Harmony _harmony;
        private BlindRuntime _runtime;

        private void Awake()
        {
            Instance = this;
            LogSource = Logger;

            SensorModeKey = Config.Bind("Sensors", "CycleModeKey", new KeyboardShortcut(KeyCode.F7),
                "Cycle STANDARD IR, LONGBOW and IR BLACK on the cockpit target camera.");
            ThermalSpan = Config.Bind("Sensors", "ThermalSpan", 1.25f,
                new ConfigDescription("Fixed thermal display span. Lower values increase contrast; fixed gain prevents flashes darkening the whole scene.", new AcceptableValueRange<float>(0.5f, 4f)));
            ThermalNoise = Config.Bind("Sensors", "ThermalNoise", 0.008f,
                new ConfigDescription("Thermal detector noise amplitude.", new AcceptableValueRange<float>(0f, 0.04f)));
            WhiteHotCeiling = Config.Bind("Sensors", "WhiteHotCeiling", 0.86f,
                new ConfigDescription("Maximum display brightness in WHITE HOT, with a soft highlight shoulder. Does not change Ironbow.", new AcceptableValueRange<float>(0.5f, 1f)));
            EnableJtac = Config.Bind("JTAC", "Enabled", true,
                "Allow allied ground vehicles and naval ships to designate hostile surface/naval targets. Host authority is required.");
            GroundLaserRange = Config.Bind("JTAC", "GroundLaserRange", 4000f,
                new ConfigDescription("Maximum observer-to-target designation range for ground vehicles in metres.",
                    new AcceptableValueRange<float>(500f, 8000f)));
            NavalLaserRange = Config.Bind("JTAC", "NavalLaserRange", 15000f,
                new ConfigDescription("Maximum observer-to-target designation range for naval ships in metres.",
                    new AcceptableValueRange<float>(2000f, 25000f)));
            AircraftReceiveRange = Config.Bind("JTAC", "AircraftReceiveRange", 18000f,
                new ConfigDescription("Maximum aircraft-to-designated-target cue range in metres.",
                    new AcceptableValueRange<float>(2000f, 35000f)));
            MaxDesignationTime = Config.Bind("JTAC", "MaxDesignationTime", 20f,
                new ConfigDescription("Maximum uninterrupted designation time in seconds.",
                    new AcceptableValueRange<float>(3f, 120f)));
            DesignatorCooldown = Config.Bind("JTAC", "DesignatorCooldown", 12f,
                new ConfigDescription("Cooldown after a full designation cycle in seconds.",
                    new AcceptableValueRange<float>(1f, 120f)));
            MaxConcurrentDesignations = Config.Bind("JTAC", "MaxConcurrentDesignations", 4,
                new ConfigDescription("Maximum simultaneous ground designations for the local faction.",
                    new AcceptableValueRange<int>(1, 16)));
            EnableExplosionOverhaul = Config.Bind("Explosions", "Enabled", true,
                "Overhaul explosion VFX with dynamic scaling and prolonged lingering smoke.");
            SmokePersistenceMultiplier = Config.Bind("Explosions", "SmokePersistenceMultiplier", 2.8f,
                new ConfigDescription("Lifetime multiplier for lingering smoke and dust clouds.",
                    new AcceptableValueRange<float>(1.0f, 4.0f)));
            EnableShockwaveDistortion = Config.Bind("Explosions", "ShockwaveDistortion", true,
                "Spawn a procedural optical refraction shockwave at the blast epicenter.");
            ShockwaveIntensity = Config.Bind("Explosions", "ShockwaveIntensity", 1.0f,
                new ConfigDescription("Intensity of optical screen distortion for shockwaves.",
                    new AcceptableValueRange<float>(0.2f, 2.5f)));

            _runtime = gameObject.AddComponent<BlindRuntime>();
            _runtime.Initialize(this);

            _harmony = new Harmony(PluginGuid);
            try { _harmony.PatchAll(typeof(BlindPlugin).Assembly); }
            catch (System.Exception error)
            {
                // Never leave half of the rendering/gameplay patches installed after an API mismatch.
                _harmony.UnpatchSelf();
                _runtime.Shutdown();
                _runtime.enabled = false;
                Logger.LogError("BLIND initialization failed; its patches and sensor state were restored. " + error);
                return;
            }
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded for Nuclear Option 0.34.x.");
#if BLIND_DIAGNOSTICS
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--blind-render-test") >= 0)
            {
                Application.runInBackground = true;
                gameObject.AddComponent<ThermalRuntimeProbe>();
            }
#endif
        }

        private void OnDestroy()
        {
            if (_runtime != null)
            {
                _runtime.Shutdown();
            }
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }
            Instance = null;
        }
    }
}
