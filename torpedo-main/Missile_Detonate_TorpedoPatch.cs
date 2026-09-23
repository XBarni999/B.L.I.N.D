using HarmonyLib;
using UnityEngine;
namespace Torpedo
{
    [HarmonyPatch(typeof(Missile), "Detonate")]
    public static class Missile_Detonate_TorpedoPatch
    {
        public static bool Prefix(Missile __instance, Vector3 normal, bool hitArmor, bool hitTerrain)
        {
            if (!TorpedoCombatRules.IsTorpedo(__instance)) return true;

            // If this is an authorized gun kill or an impact with armor/target, allow detonation
            if (Missile_TakeDamage_TorpedoPatch.InProgress.Contains(__instance) || hitArmor)
            {
                TorpedoWake.RemoveWake(__instance);
                return true;
            }

            // If hitting actual solid terrain/seabed (not hitting water surface)
            if (hitTerrain && (!TorpedoPhysics.IsOverWater(__instance) || __instance.GlobalPosition().y <= -1f))
            {
                TorpedoWake.RemoveWake(__instance);
                return true;
            }

            // Prevent self-destruction from seeker timeouts, proximity near-misses, or low speed
            return false;
        }
    }
}
