using HarmonyLib;
namespace Torpedo
{
    [HarmonyPatch(typeof(Unit), "DisableUnit")]
    public static class Unit_DisableUnit_TorpedoPatch
    {
        public static void Prefix(Unit __instance)
        {
            if (__instance is Missile missile && TorpedoCombatRules.IsTorpedo(missile))
                TorpedoWake.RemoveWake(missile);
        }
    }
}
