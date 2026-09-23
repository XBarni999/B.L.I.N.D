using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
namespace Torpedo
{
    [BepInPlugin("neutral.torpedo", "Torpedo Balanced", "1.4.8")]
    public class TorpedoPlugin : BaseUnityPlugin
    {
        public static TorpedoPlugin Instance;
        public static ManualLogSource ModLogger;

        internal static ConfigEntry<float> SpeedScale;
        internal static ConfigEntry<float> WarheadScale;
        internal static ConfigEntry<float> PenetrationScale;
        internal static ConfigEntry<float> RangeScale;
        internal static ConfigEntry<float> AcquisitionHalfAngle;

        private void Awake()
        {
            Instance = this;
            ModLogger = base.Logger;

            SpeedScale = Config.Bind("Balance", "SpeedScale", 0.85f,
                new ConfigDescription(
                    "Additional speed multiplier applied after each torpedo variant's base speed. Requires a restart.",
                    new AcceptableValueRange<float>(0.35f, 1.25f)));
            WarheadScale = Config.Bind("Balance", "WarheadScale", 0.65f,
                new ConfigDescription(
                    "Additional blast-damage and blast-yield multiplier. Requires a restart.",
                    new AcceptableValueRange<float>(0.1f, 1.5f)));
            PenetrationScale = Config.Bind("Balance", "PenetrationScale", 0.75f,
                new ConfigDescription(
                    "Multiplier for the bonus penetration added by torpedo variants. Requires a restart.",
                    new AcceptableValueRange<float>(0.1f, 1.5f)));
            RangeScale = Config.Bind("Balance", "RangeScale", 0.60f,
                new ConfigDescription(
                    "Multiplier for AI/player launch range and physical motor endurance. Requires a restart.",
                    new AcceptableValueRange<float>(0.2f, 1.25f)));
            AcquisitionHalfAngle = Config.Bind("Balance", "AcquisitionHalfAngle", 35f,
                new ConfigDescription(
                    "Maximum off-boresight launch angle in degrees. Combat AI uses this same cone.",
                    new AcceptableValueRange<float>(5f, 120f)));

            var harmony = new Harmony("neutral.torpedo");
            harmony.PatchAll();
            ModLogger.LogInfo($"[Torpedo] Balanced torpedoes loaded: speed x{SpeedScale.Value:0.00}, " +
                              $"warhead x{WarheadScale.Value:0.00}, range x{RangeScale.Value:0.00}, " +
                              $"acquisition cone ±{AcquisitionHalfAngle.Value:0.#} degrees.");
        }
    }
}
