using System;
using System.Collections.Generic;
using UnityEngine;
namespace Torpedo
{
    public static class TorpedoWake
    {
        private static readonly Dictionary<Missile, float> _nextSplashTime = new Dictionary<Missile, float>();
        private const float SplashLifetime = 16f;
        public static void UpdateWake(Missile missile, bool active)
        {
            if (!active)
            {
                RemoveWake(missile);
                return;
            }
            if (GameAssets.i == null || GameAssets.i.splash_large == null) return;
            GlobalPosition gp = missile.GlobalPosition();
            gp.y = 0f; 
            Vector3 position = gp.ToLocalPosition();
            Vector3 vel = missile.rb.velocity;
            float speed = new Vector3(vel.x, 0f, vel.z).magnitude;
            // Frequent overlapping surface splashes form a clearly visible foam/bubble trail
            // when viewed from an aircraft, without exposing the torpedo as a HUD marker.
            float dynamicInterval = Mathf.Clamp(5f / Mathf.Max(1f, speed), 0.04f, 0.35f);
            if (!_nextSplashTime.TryGetValue(missile, out float nextTime) || Time.time >= nextTime)
            {
                _nextSplashTime[missile] = Time.time + dynamicInterval;
                Quaternion splashRotation = Quaternion.LookRotation(Vector3.up + new Vector3(vel.x, 0f, vel.z) * 0.1f);
                GameObject splash = UnityEngine.Object.Instantiate(GameAssets.i.splash_large, position, splashRotation);
                splash.transform.localScale *= 0.65f;
                foreach (var ps in splash.GetComponentsInChildren<ParticleSystem>())
                {
                    var main = ps.main;
                    main.simulationSpeed *= 1.35f;
                    main.startLifetimeMultiplier *= 0.8f;
                }
                UnityEngine.Object.Destroy(splash, SplashLifetime);
            }
        }
        public static void RemoveWake(Missile missile)
        {
            _nextSplashTime.Remove(missile);
        }
    }
}
