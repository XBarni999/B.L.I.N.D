using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace BLIND
{
    internal sealed class JtacSeekerLock
    {
        internal Unit Target;
        internal bool Logged;
    }

    internal static class LaserSeekerPersistence
    {
        private static readonly FieldInfo MissileField = AccessTools.Field(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo TargetUnitField = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo MaxSeekerAngleField = AccessTools.Field(typeof(LaserSeeker), "maxSeekerAngle");
        private static readonly ConditionalWeakTable<LaserSeeker, JtacSeekerLock> Locks =
            new ConditionalWeakTable<LaserSeeker, JtacSeekerLock>();
        private static readonly HashSet<LaserSeeker> ActiveSeekers = new HashSet<LaserSeeker>();

        internal static Unit GetTarget(LaserSeeker seeker)
        {
            return TargetUnitField == null ? null : TargetUnitField.GetValue(seeker) as Unit;
        }

        internal static Missile GetMissile(LaserSeeker seeker)
        {
            return MissileField == null ? null : MissileField.GetValue(seeker) as Missile;
        }

        internal static float GetMaxAngle(LaserSeeker seeker)
        {
            return MaxSeekerAngleField == null ? 10f : (float)MaxSeekerAngleField.GetValue(seeker);
        }

        internal static JtacSeekerLock GetOrCreateLock(LaserSeeker seeker)
        {
            return Locks.GetOrCreateValue(seeker);
        }

        internal static bool HasActiveMissileTracking(Unit target)
        {
            if (target == null) return false;
            ActiveSeekers.RemoveWhere(s => s == null);
            foreach (LaserSeeker seeker in ActiveSeekers.ToArray())
            {
                Missile missile = GetMissile(seeker);
                if (missile == null || missile.disabled || !missile.gameObject.activeInHierarchy)
                {
                    ActiveSeekers.Remove(seeker);
                    continue;
                }
                JtacSeekerLock seekerLock;
                if (Locks.TryGetValue(seeker, out seekerLock) && seekerLock.Target == target)
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool TryRememberJtacTarget(LaserSeeker seeker)
        {
            BlindRuntime runtime = BlindRuntime.Instance;
            if (runtime == null) return false;

            Unit target = GetTarget(seeker);
            if (target == null || !runtime.IsJtacDesignated(target))
            {
                if (!runtime.TryGetJtacTargetInView(seeker.transform, GetMaxAngle(seeker), 15000f, out target))
                {
                    return false;
                }
                if (TargetUnitField != null) TargetUnitField.SetValue(seeker, target);
            }

            JtacSeekerLock seekerLock = GetOrCreateLock(seeker);
            seekerLock.Target = target;
            ActiveSeekers.Add(seeker);
            if (!seekerLock.Logged)
            {
                seekerLock.Logged = true;
                Missile missile = GetMissile(seeker);
                BlindPlugin.LogSource.LogMessage("[B.L.I.N.D.] Laser seeker " +
                                                 (missile == null ? "missile" : missile.unitName) +
                                                 " memorized JTAC target " + target.unitName + ".");
            }
            return true;
        }

        internal static bool RestoreRememberedTarget(LaserSeeker seeker)
        {
            BlindRuntime runtime = BlindRuntime.Instance;
            JtacSeekerLock seekerLock;
            if (runtime == null || !Locks.TryGetValue(seeker, out seekerLock) ||
                seekerLock.Target == null || seekerLock.Target.disabled ||
                !seekerLock.Target.gameObject.activeInHierarchy)
            {
                return false;
            }
            if (!runtime.IsJtacDesignated(seekerLock.Target))
            {
                return false;
            }
            if (TargetUnitField != null && GetTarget(seeker) != seekerLock.Target)
            {
                TargetUnitField.SetValue(seeker, seekerLock.Target);
            }
            return true;
        }

        internal static Unit GetRememberedTarget(LaserSeeker seeker)
        {
            JtacSeekerLock seekerLock;
            return Locks.TryGetValue(seeker, out seekerLock) ? seekerLock.Target : null;
        }
    }

    [HarmonyPatch(typeof(LaserSeeker), "Initialize")]
    internal static class LaserSeekerInitializeJtacPatch
    {
        private static void Postfix(LaserSeeker __instance)
        {
            LaserSeekerPersistence.TryRememberJtacTarget(__instance);
        }
    }

    [HarmonyPatch(typeof(LaserSeeker), "Seek")]
    internal static class LaserSeekerSeekJtacPatch
    {
        private static void Prefix(LaserSeeker __instance)
        {
            if (!LaserSeekerPersistence.RestoreRememberedTarget(__instance))
            {
                LaserSeekerPersistence.TryRememberJtacTarget(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(LaserSeeker), "TrackLaser")]
    internal static class LaserSeekerTrackJtacPatch
    {
        private static void Postfix(LaserSeeker __instance, ref bool __result)
        {
            if (__result || !LaserSeekerPersistence.RestoreRememberedTarget(__instance)) return;

            Unit target = LaserSeekerPersistence.GetRememberedTarget(__instance);
            if (target == null || target.disabled || !target.gameObject.activeInHierarchy) return;
            if (Vector3.Angle(target.transform.position - __instance.transform.position,
                    __instance.transform.forward) > LaserSeekerPersistence.GetMaxAngle(__instance))
            {
                return;
            }

            __result = target.LineOfSight(__instance.transform.position, 1000f);
        }
    }
}
