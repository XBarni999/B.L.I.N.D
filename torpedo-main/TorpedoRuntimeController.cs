using System;
using HarmonyLib;
using UnityEngine;

namespace Torpedo
{
    // This component exists only on cloned torpedo prefabs. Keeping launch cutoff,
    // underwater propulsion and wake here avoids patching Missile.FixedUpdate or
    // MotorThrust for every vanilla projectile in the game.
    internal sealed class TorpedoRuntimeController : MonoBehaviour
    {
        private const float BoosterDuration = 1.5f;
        private Missile missile;
        private bool launchMotorStopped;
        private bool enteredWater;
        private float lastPhysicsTick = float.NegativeInfinity;
        private float elapsedLifetime;

        private void Awake()
        {
            missile = GetComponent<Missile>();
        }

        private void FixedUpdate()
        {
            Tick();
        }

        internal void Tick()
        {
            if (missile == null) missile = GetComponent<Missile>();
            if (missile == null || missile.disabled) return;
            if (Mathf.Approximately(lastPhysicsTick, Time.fixedTime)) return;
            lastPhysicsTick = Time.fixedTime;
            elapsedLifetime += Time.fixedDeltaTime;

            bool underwater = TorpedoPhysics.IsUnderWater(missile);
            if (underwater && !enteredWater)
            {
                enteredWater = true;
                TorpedoPhysics.OnWaterEntry(missile, hoverAltitude: GetHoverAltitude());
            }
            if (!launchMotorStopped && (underwater || elapsedLifetime >= BoosterDuration))
                StopLaunchMotor();

            if (!TorpedoCombatRules.TryGetHoverAltitude(missile, out float hoverAltitude)) return;
            if (underwater)
                TorpedoPhysics.ApplyTorpedoPhysics(missile, hoverAltitude);
            TorpedoWake.UpdateWake(missile, underwater);
        }

        private float GetHoverAltitude()
        {
            return TorpedoCombatRules.TryGetHoverAltitude(missile, out float altitude) ? altitude : -1f;
        }

        internal static void EnsureAndTick(Missile liveMissile)
        {
            if (liveMissile == null) return;
            TorpedoRuntimeController controller = liveMissile.GetComponent<TorpedoRuntimeController>();
            if (controller == null)
                controller = liveMissile.gameObject.AddComponent<TorpedoRuntimeController>();
            controller.missile = liveMissile;
            controller.Tick();
        }

        private void StopLaunchMotor()
        {
            launchMotorStopped = true;
            try { Traverse.Create(missile).Field("engineCurrentThrust").SetValue(0f); }
            catch { }

            object motorsObject = Traverse.Create(missile).Field("motors").GetValue();
            if (motorsObject is Array motors)
            {
                foreach (object motor in motors)
                {
                    if (motor == null) continue;
                    Traverse motorTraverse = Traverse.Create(motor);
                    motorTraverse.Field("fuelMass").SetValue(0f);
                    try { motorTraverse.Method("Burnout", new object[] { true }).GetValue(); }
                    catch { }
                }
            }

            // Ship launchers can carry a detachable VLS booster. Burnout is its normal,
            // self-contained separation path and this object belongs only to this torpedo.
            VLSBooster booster = missile.GetComponentInChildren<VLSBooster>();
            if (booster != null && missile.boosterIsAttached)
            {
                try { booster.Burnout(); }
                catch { }
            }
        }

        private void OnDestroy()
        {
            if (missile != null) TorpedoWake.RemoveWake(missile);
        }
    }
}
