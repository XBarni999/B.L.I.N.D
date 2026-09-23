using HarmonyLib;
using UnityEngine;
namespace Torpedo
{
    [HarmonyPatch(typeof(Missile), "DetectCollisions")]
    public static class Missile_DetectCollisions_TorpedoPatch
    {
        public static bool Prefix(Missile __instance)
        {
            if (!TorpedoCombatRules.IsTorpedo(__instance)) return true;
            if (__instance.rb == null) return true;
            if (__instance.GlobalPosition().y > 0f) return true;
            if (!TorpedoPhysics.IsOverWater(__instance)) return true;
            Vector3 velocity = __instance.rb.velocity;
            float distance = Mathf.Max(velocity.magnitude * Time.fixedDeltaTime * 1.5f, 1f);
            Vector3 direction = velocity.sqrMagnitude > 0.01f ? velocity.normalized : __instance.transform.forward;
            RaycastHit[] hits = Physics.SphereCastAll(
                __instance.transform.position - direction * 0.25f,
                0.6f,
                direction,
                distance,
                ~PhysicsLayers.ExclusionZonesMask.value,
                QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.isTrigger) continue;
                if (hit.collider.transform.IsChildOf(__instance.transform)) continue;

                Unit unit = hit.collider.GetComponentInParent<Unit>();
                if (unit != null)
                {
                    if (unit == __instance) continue;
                    TorpedoPhysics.DetonateAndStop(__instance, hit.normal, hitArmor: true, hitTerrain: false);
                    return false;
                }

                // If underwater, ensure terrain hit is actual terrain / seabed, not water surface plane
                if (hit.point.y < -1f || (hit.normal.y > 0.5f && __instance.GlobalPosition().y <= -1f))
                {
                    TorpedoPhysics.DetonateAndStop(__instance, hit.normal, hitArmor: false, hitTerrain: true);
                    return false;
                }
            }
            return false;
        }
    }
}
