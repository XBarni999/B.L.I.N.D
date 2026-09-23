using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace Torpedo
{
    [HarmonyPatch(typeof(Missile), "SetAimpoint")]
    public static class Missile_SetAimpoint_TorpedoPatch
    {
        private static readonly AccessTools.FieldRef<Missile, MissileSeeker> seekerRef =
            AccessTools.FieldRefAccess<Missile, MissileSeeker>("seeker");
        private static readonly AccessTools.FieldRef<OpticalSeekerCruiseMissile, float> altitudeTargetRef =
            AccessTools.FieldRefAccess<OpticalSeekerCruiseMissile, float>("altitudeTarget");
        private static readonly AccessTools.FieldRef<OpticalSeekerCruiseMissile, JinkEvasion> jinkRef =
            AccessTools.FieldRefAccess<OpticalSeekerCruiseMissile, JinkEvasion>("jinkEvasion");
        private static readonly AccessTools.FieldRef<OpticalSeekerCruiseMissile, TopAttack> topAttackRef =
            AccessTools.FieldRefAccess<OpticalSeekerCruiseMissile, TopAttack>("topAttack");
        private static readonly AccessTools.FieldRef<OpticalSeekerCruiseMissile, TerminalBoost> terminalBoostRef =
            AccessTools.FieldRefAccess<OpticalSeekerCruiseMissile, TerminalBoost>("terminalBoost");
        private static readonly AccessTools.FieldRef<OpticalSeekerCruiseMissile, float> finDelayRef =
            AccessTools.FieldRefAccess<OpticalSeekerCruiseMissile, float>("finDelay");
        private static readonly AccessTools.FieldRef<OpticalSeekerCruiseMissile, float> terminalRangeRef =
            AccessTools.FieldRefAccess<OpticalSeekerCruiseMissile, float>("terminalRange");
        private static readonly AccessTools.FieldRef<OpticalSeekerCruiseMissile, bool> terminalModeRef =
            AccessTools.FieldRefAccess<OpticalSeekerCruiseMissile, bool>("terminalMode");
        private static readonly AccessTools.FieldRef<MissileSeeker, Unit> targetUnitRef =
            AccessTools.FieldRefAccess<MissileSeeker, Unit>("targetUnit");
        private static readonly AccessTools.FieldRef<OpticalSeekerCruiseMissile, Transform> targetPartRef =
            AccessTools.FieldRefAccess<OpticalSeekerCruiseMissile, Transform>("targetPart");
        private static readonly HashSet<OpticalSeekerCruiseMissile> _neutered = new HashSet<OpticalSeekerCruiseMissile>();
        public static bool Prefix(Missile __instance, ref GlobalPosition aimPoint, ref Vector3 targetVel)
        {
            if (!TorpedoCombatRules.TryGetHoverAltitude(__instance, out float hoverAltitude))
                return true;

            if (seekerRef(__instance) is OpticalSeekerCruiseMissile cSeeker && _neutered.Add(cSeeker))
            {
                JinkEvasion jink = jinkRef(cSeeker);
                if (jink != null) jink.amount = 0f;
                TopAttack topAttack = topAttackRef(cSeeker);
                if (topAttack != null)
                {
                    topAttack.Amount = 0f;
                    topAttack.Active = false;
                }
                TerminalBoost terminalBoost = terminalBoostRef(cSeeker);
                if (terminalBoost != null)
                {
                    terminalBoost.Amount = 0f;
                    terminalBoost.Active = false;
                }
                // Keep cruise-missile wings folded. Their large aerodynamic area made an
                // air-dropped torpedo glide for kilometres instead of entering the water.
                finDelayRef(cSeeker) = float.MaxValue;
                terminalRangeRef(cSeeker) = float.MaxValue;
            }

            // Network spawning may omit an added prefab component on some clients. Ensure
            // the torpedo-only controller exists on the live missile before doing anything.
            TorpedoRuntimeController.EnsureAndTick(__instance);
            if (seekerRef(__instance) is OpticalSeekerCruiseMissile seeker)
            {
                altitudeTargetRef(seeker) = hoverAltitude;
                terminalModeRef(seeker) = true;
                if (targetPartRef(seeker) == null)
                {
                    Unit targetUnit = targetUnitRef(seeker);
                    if (targetUnit != null)
                    {
                        Transform targetPart = targetUnit.GetRandomPart();
                        targetPartRef(seeker) = targetPart;
                        __instance.SetProxyFuse(targetPart, targetUnit.rb);
                    }
                }
            }
            if (__instance.GlobalPosition().y > 0f)
            {
                // In the air (during booster phase): guide down towards the water in the direction of the target
                aimPoint.y = 0f;
            }
            else
            {
                aimPoint.y = __instance.GlobalPosition().y;
            }
            bool cruising = TorpedoPhysics.InCruisePhase(__instance);
            if (cruising)
            {
                if (!TorpedoPhysics.IsOverWater(__instance))
                {
                    TorpedoPhysics.DetonateAndStop(__instance, Vector3.up, hitArmor: false, hitTerrain: true);
                    return false;
                }
            }
            return true;
        }
    }
}
