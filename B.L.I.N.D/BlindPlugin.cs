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
        public const string PluginVersion = "0.4.1";

        internal static ManualLogSource LogSource;
        internal static BlindPlugin Instance;

        internal ConfigEntry<KeyboardShortcut> SensorModeKey;
        internal ConfigEntry<float> ThermalSpan;
        internal ConfigEntry<float> ThermalNoise;
        internal ConfigEntry<float> WhiteHotCeiling;
        internal ConfigEntry<bool> EnableJtac;
        internal ConfigEntry<float> GroundLaserRange;
        internal ConfigEntry<float> AircraftReceiveRange;
        internal ConfigEntry<float> MaxDesignationTime;
        internal ConfigEntry<float> DesignatorCooldown;
        internal ConfigEntry<int> MaxConcurrentDesignations;

        private Harmony _harmony;
        private BlindRuntime _runtime;

        private void Awake()
        {
            Instance = this;
            LogSource = Logger;

            SensorModeKey = Config.Bind("Sensors", "CycleModeKey", new KeyboardShortcut(KeyCode.F7),
                "Cycle COLOR, FLIR IRONBOW, FLIR WHITE HOT, FLIR BLACK HOT and NVG on the cockpit target camera.");
            ThermalSpan = Config.Bind("Sensors", "ThermalSpan", 1.25f,
                new ConfigDescription("Fixed thermal display span. Lower values increase contrast; fixed gain prevents flashes darkening the whole scene.", new AcceptableValueRange<float>(0.5f, 4f)));
            ThermalNoise = Config.Bind("Sensors", "ThermalNoise", 0.008f,
                new ConfigDescription("Thermal detector noise amplitude.", new AcceptableValueRange<float>(0f, 0.04f)));
            WhiteHotCeiling = Config.Bind("Sensors", "WhiteHotCeiling", 0.86f,
                new ConfigDescription("Maximum display brightness in WHITE HOT, with a soft highlight shoulder. Does not change Ironbow or Black Hot.", new AcceptableValueRange<float>(0.5f, 1f)));
            EnableJtac = Config.Bind("JTAC", "Enabled", true,
                "Allow allied ground vehicles to designate hostile surface targets. Host authority is required.");
            GroundLaserRange = Config.Bind("JTAC", "GroundLaserRange", 4000f,
                new ConfigDescription("Maximum observer-to-target designation range in metres.",
                    new AcceptableValueRange<float>(500f, 8000f)));
            AircraftReceiveRange = Config.Bind("JTAC", "AircraftReceiveRange", 15000f,
                new ConfigDescription("Maximum aircraft-to-designated-target cue range in metres.",
                    new AcceptableValueRange<float>(2000f, 30000f)));
            MaxDesignationTime = Config.Bind("JTAC", "MaxDesignationTime", 20f,
                new ConfigDescription("Maximum uninterrupted designation time in seconds.",
                    new AcceptableValueRange<float>(3f, 120f)));
            DesignatorCooldown = Config.Bind("JTAC", "DesignatorCooldown", 12f,
                new ConfigDescription("Cooldown after a full designation cycle in seconds.",
                    new AcceptableValueRange<float>(1f, 120f)));
            MaxConcurrentDesignations = Config.Bind("JTAC", "MaxConcurrentDesignations", 4,
                new ConfigDescription("Maximum simultaneous ground designations for the local faction.",
                    new AcceptableValueRange<int>(1, 16)));

            _runtime = gameObject.AddComponent<BlindRuntime>();
            _runtime.Initialize(this);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(BlindPlugin).Assembly);
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded for Nuclear Option 0.34.x.");
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
