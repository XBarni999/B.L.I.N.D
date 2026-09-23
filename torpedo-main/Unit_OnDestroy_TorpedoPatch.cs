using HarmonyLib;
namespace Torpedo
{
    [HarmonyPatch(typeof(Unit), "OnDestroy")]
    public static class Unit_OnDestroy_TorpedoPatch
    {
        public static void Prefix(Unit __instance)
        {
            if (!(__instance is Missile missile)) return;
            if (!TorpedoCombatRules.IsTorpedo(missile)) return;
            TorpedoCombatRules.ActiveTorpedoes.Remove(missile);
            TorpedoWake.RemoveWake(missile);
        }
    }
}
