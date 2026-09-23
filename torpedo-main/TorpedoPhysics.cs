using UnityEngine;

namespace Torpedo
{
    public static class TorpedoPhysics
    {
        public static bool IsOverWater(Missile missile)
        {
            return missile.GlobalPosition().y <= 2f;
        }

        public static bool IsUnderWater(Missile missile)
        {
            return missile.GlobalPosition().y <= 0f;
        }

        public static bool InCruisePhase(Missile missile)
        {
            return missile.GlobalPosition().y <= 1f;
        }

        public static void DetonateAndStop(Missile missile, Vector3 normal, bool hitArmor, bool hitTerrain)
        {
            missile.Detonate(normal, hitArmor, hitTerrain);
            if (missile.rb != null && !missile.rb.isKinematic) missile.rb.velocity = Vector3.zero;
        }

        public static void OnWaterEntry(Missile missile, float hoverAltitude)
        {
            if (missile == null || missile.rb == null || missile.rb.isKinematic) return;

            // A cruise-missile donor can enter the sea at several hundred metres per
            // second. Water removes almost all vertical momentum immediately; otherwise
            // one physics frame is enough to tunnel below the seabed.
            Vector3 velocity = missile.rb.velocity;
            velocity.y = Mathf.Clamp(velocity.y * 0.04f, -8f, 3f);
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            float cruiseSpeed = GetCruiseSpeed(missile);
            if (horizontal.magnitude > cruiseSpeed)
            {
                horizontal = horizontal.normalized * cruiseSpeed;
                velocity.x = horizontal.x;
                velocity.z = horizontal.z;
            }
            missile.rb.velocity = velocity;

            float globalY = (float)missile.GlobalPosition().y;
            if (globalY < hoverAltitude - 15f)
            {
                Vector3 corrected = missile.rb.position;
                corrected.y += hoverAltitude - globalY;
                missile.rb.position = corrected;
                missile.transform.position = corrected;
            }
        }

        public static void ApplyTorpedoPhysics(Missile missile, float targetGlobalY)
        {
            TorpedoCombatRules.ActiveTorpedoes.Add(missile);
            if (missile.rb == null || missile.rb.isKinematic) return;

            float liveGlobalY = (float)missile.GlobalPosition().y;
            float yError = targetGlobalY - liveGlobalY;
            float springK = 50f;
            float dampK = 10f;
            float forceY = yError * springK - missile.rb.velocity.y * dampK;
            if (liveGlobalY <= 0f && yError < 0f && missile.rb.velocity.y <= 0f)
            {
                forceY = Mathf.Max(forceY, -9.8f);
            }
            missile.rb.AddForce(new Vector3(0f, forceY, 0f), ForceMode.Acceleration);

            // Never allow an underwater torpedo to accumulate an unrecoverable vertical
            // velocity. Horizontal cruise speed remains independently controlled below.
            Vector3 boundedVelocity = missile.rb.velocity;
            boundedVelocity.y = Mathf.Clamp(boundedVelocity.y, -12f, 8f);
            missile.rb.velocity = boundedVelocity;

            if (liveGlobalY >= targetGlobalY && missile.rb.velocity.y > 0f)
            {
                missile.rb.velocity = new Vector3(missile.rb.velocity.x, 0f, missile.rb.velocity.z);
            }

            Vector3 tiltAxis = Vector3.Cross(missile.transform.up, Vector3.up);
            if (tiltAxis.sqrMagnitude > 0.0001f)
            {
                missile.rb.AddTorque(tiltAxis * 50f, ForceMode.Acceleration);
            }
            missile.rb.AddTorque(-missile.rb.angularVelocity * 1f, ForceMode.Acceleration);

            Vector3 horizVel = new Vector3(missile.rb.velocity.x, 0f, missile.rb.velocity.z);
            float currentSpeed = horizVel.magnitude;

            // Maintain steady underwater torpedo cruise speed from encyclopedia (scaled by speed multiplier)
            float targetSpeed = GetCruiseSpeed(missile);

            if (currentSpeed > targetSpeed)
            {
                float clampedSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, 80f * Time.fixedDeltaTime);
                missile.rb.velocity = new Vector3(
                    missile.rb.velocity.x * (clampedSpeed / currentSpeed),
                    missile.rb.velocity.y,
                    missile.rb.velocity.z * (clampedSpeed / currentSpeed)
                );
            }
            else if (currentSpeed < targetSpeed * 0.95f)
            {
                Vector3 forward = missile.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
                forward.Normalize();
                missile.rb.AddForce(forward * 20f, ForceMode.Acceleration);
            }
        }

        private static float GetCruiseSpeed(Missile missile)
        {
            if (missile != null && missile.definition != null &&
                TorpedoMounts_Patch.CruiseSpeedByName.TryGetValue(missile.definition.jsonKey, out float speed) &&
                speed > 1f && speed < 500f)
                return speed;
            return 85f;
        }
    }
}
