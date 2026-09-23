using HarmonyLib;
namespace Torpedo
{
    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "Initialize")]
    public static class OpticalSeekerCruiseMissile_Initialize_TorpedoPatch
    {
        private static readonly AccessTools.FieldRef<MissileSeeker, Missile> missileRef =
            AccessTools.FieldRefAccess<MissileSeeker, Missile>("missile");
        private static readonly AccessTools.FieldRef<OpticalSeekerCruiseMissile, float> altitudeTargetRef =
            AccessTools.FieldRefAccess<OpticalSeekerCruiseMissile, float>("altitudeTarget");
        public static void Postfix(OpticalSeekerCruiseMissile __instance)
        {
            Missile missile = missileRef(__instance);
            if (!TorpedoCombatRules.TryGetHoverAltitude(missile, out float hoverAltitude))
                return;
            TorpedoCombatRules.ActiveTorpedoes.Add(missile);
            altitudeTargetRef(__instance) = hoverAltitude;
        }
    }
}
