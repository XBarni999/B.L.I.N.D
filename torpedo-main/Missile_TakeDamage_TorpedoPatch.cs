using System.Collections.Generic;
using HarmonyLib;
namespace Torpedo
{
    [HarmonyPatch(typeof(Missile), "TakeDamage")]
    public static class Missile_TakeDamage_TorpedoPatch
    {
        internal static readonly HashSet<Missile> InProgress = new HashSet<Missile>();
        [HarmonyPrefix]
        public static bool Prefix(Missile __instance, ref float pierceDamage, ref float blastDamage, ref float amountAffected, ref float fireDamage, ref float impactDamage, PersistentID dealerID, ref bool __state)
        {
            __state = false;
            if (!TorpedoCombatRules.IsTorpedo(__instance)) return true;

            // Only direct kinetic bullet hit (pierceDamage >= 15f) is allowed to harm torpedoes, ignore proximity blast/splash/shrapnel
            if (pierceDamage < 15f)
                return false;

            if (!TorpedoCombatRules.ConsumeAuthorizedGunHit(__instance))
                return false;

            // Scale down bullet damage so it requires multiple direct hits from guns to kill a heavy torpedo
            pierceDamage *= 0.5f;
            blastDamage = 0f;
            fireDamage = 0f;
            impactDamage = 0f;

            InProgress.Add(__instance);
            __state = true;
            return true;
        }
        [HarmonyPostfix]
        public static void Postfix(Missile __instance, bool __state)
        {
            if (__state) InProgress.Remove(__instance);
        }
    }

    [HarmonyPatch(typeof(Missile), "TakeShockwave")]
    public static class Missile_TakeShockwave_TorpedoPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Missile __instance)
        {
            // Submerged torpedoes are immune to near-miss shockwaves from air/water surface flak
            if (TorpedoCombatRules.IsTorpedo(__instance))
            {
                return false;
            }
            return true;
        }
    }
}
