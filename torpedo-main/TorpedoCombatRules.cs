using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace Torpedo
{
    internal static class TorpedoCombatRules
    {
        internal const float DetectionRange = 7000f;
        private static readonly Dictionary<int, float> AuthorizedGunHits = new Dictionary<int, float>();
        internal static readonly HashSet<Missile> ActiveTorpedoes = new HashSet<Missile>();
        private static readonly Regex CaliberPattern = new Regex(@"(?<!\d)(\d+(?:[\.,]\d+)?)\s*mm", RegexOptions.IgnoreCase);
        private static readonly Regex BareCaliberPattern = new Regex(@"(?<!\d)(\d{2,3})(?!\d)");
        private static readonly Regex InchCaliberPattern = new Regex("(?<!\\d)(\\d+(?:[\\.,]\\d+)?)\\s*(?:in|inch|\")", RegexOptions.IgnoreCase);

        internal static bool IsTorpedo(Unit unit)
        {
            Missile missile = unit as Missile;
            if (missile == null) return false;
            if (missile.GetComponent<TorpedoIdentity>() != null) return true;

            // Mirage can instantiate a network object without preserving third-party
            // MonoBehaviours added to the source prefab. The cloned definition is unique
            // to this mod and is therefore the authoritative runtime fallback.
            return missile.definition != null &&
                   TorpedoMounts_Patch.HoverAltitudeByName.ContainsKey(missile.definition.jsonKey);
        }

        internal static bool TryGetHoverAltitude(Missile missile, out float altitude)
        {
            altitude = 0f;
            return IsTorpedo(missile) && missile.definition != null &&
                   TorpedoMounts_Patch.HoverAltitudeByName.TryGetValue(missile.definition.jsonKey, out altitude);
        }

        internal static void RegisterDirectGunHit(Unit target, WeaponInfo weaponInfo)
        {
            if (!IsTorpedo(target) || !IsGunOver30Mm(weaponInfo)) return;
            AuthorizedGunHits[target.GetInstanceID()] = Time.unscaledTime + 3f;
        }

        internal static bool ConsumeAuthorizedGunHit(Missile missile)
        {
            if (missile == null) return false;
            int id = missile.GetInstanceID();
            float expires;
            if (!AuthorizedGunHits.TryGetValue(id, out expires)) return false;
            AuthorizedGunHits.Remove(id);
            return Time.unscaledTime <= expires;
        }

        internal static bool IsGunOver30Mm(WeaponInfo info)
        {
            if (info == null || !info.gun) return false;
            string text = (info.weaponName ?? string.Empty) + " " +
                          (info.shortName ?? string.Empty) + " " +
                          (info.description ?? string.Empty);
            Match match = CaliberPattern.Match(text);
            float caliber;
            if (match.Success && float.TryParse(match.Groups[1].Value.Replace(',', '.'),
                       System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out caliber)) return caliber > 30f;
            match = InchCaliberPattern.Match(text);
            if (match.Success && float.TryParse(match.Groups[1].Value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out caliber)) return caliber * 25.4f > 30f;
            match = BareCaliberPattern.Match((info.weaponName ?? string.Empty) + " " + (info.shortName ?? string.Empty));
            return match.Success && float.TryParse(match.Groups[1].Value,
                       System.Globalization.NumberStyles.Integer,
                       System.Globalization.CultureInfo.InvariantCulture, out caliber) && caliber > 30f;
        }

        internal static bool IsWithinTorpedoLaunchRange(WeaponInfo info, Unit owner, Unit target, GlobalPosition aimpoint)
        {
            if (!TorpedoMounts_Patch.IsBalancedTorpedoInfo(info) || owner == null) return true;
            float maxRange = info.targetRequirements.maxRange;
            if (maxRange <= 0f) return false;
            if (target != null)
                return FastMath.InRange(owner.GlobalPosition(), target.GlobalPosition(), maxRange);
            return FastMath.InRange(owner.GlobalPosition(), aimpoint, maxRange);
        }
    }

    // Let ship sensors report submerged torpedoes through the normal detector event path.
    // DetectTarget updates HQ tracking before TargetDetector raises its end-of-scan event,
    // so turrets never receive an orphaned target ID.
    [HarmonyPatch(typeof(TargetDetector), "TargetSearch")]
    internal static class TorpedoShipDetectionPatch
    {
        private static void Postfix(TargetDetector __instance)
        {
            AddNearbyTorpedoes(__instance);
        }

        internal static void AddNearbyTorpedoes(TargetDetector __instance)
        {
            Unit observer = __instance != null ? __instance.GetAttachedUnit() : null;
            if (!(observer is Ship) || observer.disabled || observer.NetworkHQ == null) return;

            TorpedoCombatRules.ActiveTorpedoes.RemoveWhere(m => m == null || m.disabled);
            foreach (Missile torpedo in TorpedoCombatRules.ActiveTorpedoes)
            {
                if (torpedo == null || torpedo.disabled || torpedo.NetworkHQ == null ||
                    torpedo.NetworkHQ == observer.NetworkHQ ||
                    __instance.detectedTargets.Contains(torpedo)) continue;
                if (!FastMath.InRange(observer.GlobalPosition(), torpedo.GlobalPosition(), TorpedoCombatRules.DetectionRange))
                    continue;
                __instance.DetectTarget(torpedo);
            }
        }
    }

    // Radar overrides TargetSearch, so patch the override as well as the base visual detector.
    [HarmonyPatch(typeof(Radar), "TargetSearch")]
    internal static class TorpedoShipRadarDetectionPatch
    {
        private static void Postfix(Radar __instance)
        {
            TorpedoShipDetectionPatch.AddNearbyTorpedoes(__instance);
        }
    }

    [HarmonyPatch(typeof(TargetDetector), "DetectTarget")]
    internal static class TorpedoDetectionRangePatch
    {
        private static bool Prefix(TargetDetector __instance, Unit target)
        {
            if (!TorpedoCombatRules.IsTorpedo(target)) return true;
            Unit observer = __instance != null ? __instance.GetAttachedUnit() : null;
            return observer is Ship && observer.NetworkHQ != target.NetworkHQ &&
                   FastMath.InRange(observer.GlobalPosition(), target.GlobalPosition(), TorpedoCombatRules.DetectionRange);
        }
    }

    // Torpedoes are valid targets only for direct-fire guns over 30 mm. This both
    // prevents missile expenditure and makes suitable naval guns strongly prefer them.
    [HarmonyPatch(typeof(Turret), "AssessTargetPriority")]
    internal static class TorpedoTurretPriorityPatch
    {
        private static readonly AccessTools.FieldRef<Turret, Unit> AttachedUnit =
            AccessTools.FieldRefAccess<Turret, Unit>("attachedUnit");
        private static readonly AccessTools.FieldRef<Turret, WeaponStation[]> Stations =
            AccessTools.FieldRefAccess<Turret, WeaponStation[]>("weaponStations");
        private static readonly AccessTools.FieldRef<Turret, Unit> Target =
            AccessTools.FieldRefAccess<Turret, Unit>("target");
        private static readonly AccessTools.FieldRef<Turret, WeaponStation> CurrentStation =
            AccessTools.FieldRefAccess<Turret, WeaponStation>("currentWeaponStation");

        private static bool Prefix(Turret __instance, Unit targetCandidate, ref float priorityThreshold)
        {
            if (!TorpedoCombatRules.IsTorpedo(targetCandidate)) return true;
            Unit owner = AttachedUnit(__instance);
            if (owner == null || owner.NetworkHQ == null || targetCandidate.disabled ||
                owner.NetworkHQ.GetTrackingData(targetCandidate.persistentID) == null) return false;

            float distance = Vector3.Distance(owner.transform.position, targetCandidate.transform.position);
            WeaponStation selected = null;
            foreach (WeaponStation station in Stations(__instance))
            {
                if (station == null || station.WeaponInfo == null || station.Ammo <= 0 ||
                    !TorpedoCombatRules.IsGunOver30Mm(station.WeaponInfo)) continue;
                TargetRequirements requirements = station.WeaponInfo.targetRequirements;
                if (distance < requirements.minRange || distance > Mathf.Min(requirements.maxRange, TorpedoCombatRules.DetectionRange))
                    continue;
                selected = station;
                if (!station.Reloading) break;
            }
            if (selected == null) return false;

            float priority = selected.Reloading ? 10000f : 100000f;
            if (priority > priorityThreshold)
            {
                Target(__instance) = targetCandidate;
                CurrentStation(__instance) = selected;
                priorityThreshold = priority;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "Fire")]
    internal static class TorpedoMissileFireGuardPatch
    {
        private static bool Prefix(WeaponStation __instance, Unit owner, Unit target)
        {
            if (__instance == null) return true;
            if (TorpedoCombatRules.IsTorpedo(target) && !TorpedoCombatRules.IsGunOver30Mm(__instance.WeaponInfo))
                return false;
            return TorpedoCombatRules.IsWithinTorpedoLaunchRange(
                __instance.WeaponInfo, owner,
                target, default(GlobalPosition));
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "LaunchMount")]
    internal static class TorpedoMountedMissileFireGuardPatch
    {
        private static bool Prefix(WeaponStation __instance, Unit owner, Unit target, GlobalPosition aimpoint)
        {
            if (__instance == null) return true;
            if (TorpedoCombatRules.IsTorpedo(target) && !TorpedoCombatRules.IsGunOver30Mm(__instance.WeaponInfo))
                return false;
            return TorpedoCombatRules.IsWithinTorpedoLaunchRange(__instance.WeaponInfo, owner, target, aimpoint);
        }
    }

    [HarmonyPatch(typeof(MountedMissile), "Fire")]
    internal static class TorpedoMountedMissileRangePatch
    {
        private static bool Prefix(MountedMissile __instance, Unit owner, Unit target, GlobalPosition aimpoint)
        {
            return __instance == null || TorpedoCombatRules.IsWithinTorpedoLaunchRange(__instance.info, owner, target, aimpoint);
        }
    }

    [HarmonyPatch(typeof(MissileLauncher), "Fire")]
    internal static class TorpedoMissileLauncherRangePatch
    {
        private static bool Prefix(MissileLauncher __instance, Unit owner, Unit target, GlobalPosition aimpoint)
        {
            return __instance == null || TorpedoCombatRules.IsWithinTorpedoLaunchRange(__instance.info, owner, target, aimpoint);
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "RegisterWeapon")]
    internal static class TorpedoWeaponRegistrationPatch
    {
        private static readonly AccessTools.FieldRef<Weapon, WeaponInfo> WeaponInfoField =
            AccessTools.FieldRefAccess<Weapon, WeaponInfo>("info");

        private static void Prefix(Weapon weapon, WeaponMount weaponMount)
        {
            WeaponInfo balanced;
            if (weapon == null || weaponMount == null ||
                !TorpedoMounts_Patch.TryGetBalancedInfo(weaponMount.name, out balanced)) return;
            WeaponInfoField(weapon) = balanced;
            weaponMount.info = balanced;
        }
    }

    [HarmonyPatch(typeof(Unit), "RegisterHit")]
    internal static class TorpedoDirectGunHitPatch
    {
        private static bool Prefix(Unit hitUnit, WeaponInfo weaponInfo)
        {
            if (!TorpedoCombatRules.IsTorpedo(hitUnit)) return true;
            if (!TorpedoCombatRules.IsGunOver30Mm(weaponInfo)) return false;
            TorpedoCombatRules.RegisterDirectGunHit(hitUnit, weaponInfo);
            return true;
        }
    }
}
