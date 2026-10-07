using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BLIND
{
    // Launch restrictions apply only to aircraft-carried IR weapons whose primary role is anti-air.
    internal static class IrGroundAttack
    {
        private static float lastReport;
        private sealed class Acquisition
        {
            internal WeaponInfo Weapon;
            internal readonly Dictionary<Unit, float> Started = new Dictionary<Unit, float>();
            internal float Seen;
        }
        private static readonly Dictionary<Aircraft, Acquisition> locks = new Dictionary<Aircraft, Acquisition>();
        private static readonly List<Aircraft> staleOwners = new List<Aircraft>();
        private static readonly List<Unit> staleTargets = new List<Unit>();

        internal static void Shutdown() { locks.Clear(); }

        internal static void Tick()
        {
            float now = Time.timeSinceLevelLoad;
            if (BlindPlugin.Instance == null || !BlindPlugin.Instance.EnableIrGroundAttack.Value)
            { Shutdown(); return; }
            foreach (Aircraft owner in UnitRegistry.allAircraft)
            {
                if (owner == null || owner.disabled || owner.weaponManager == null) continue;
                var station = owner.weaponManager.currentWeaponStation;
                if (station == null || !IsWeapon(station.WeaponInfo)) { locks.Remove(owner); continue; }
                if (!locks.TryGetValue(owner, out var state))
                { state = new Acquisition(); locks.Add(owner, state); }
                if (state.Weapon != station.WeaponInfo || now - state.Seen > 0.35f)
                    state.Started.Clear();
                state.Weapon = station.WeaponInfo;
                state.Seen = now;
                var selected = owner.weaponManager.GetTargetList();
                staleTargets.Clear();
                foreach (var entry in state.Started)
                    if (entry.Key == null || !selected.Contains(entry.Key)) staleTargets.Add(entry.Key);
                foreach (Unit target in staleTargets) state.Started.Remove(target);
                foreach (Unit target in selected)
                {
                    if (!Applies(owner, station.WeaponInfo, target) ||
                        !Evaluate(owner, station.WeaponInfo, target, out _))
                    { if (target != null) state.Started.Remove(target); continue; }
                    if (!state.Started.ContainsKey(target)) state.Started.Add(target, now);
                }
            }
            staleOwners.Clear();
            foreach (var entry in locks)
                if (entry.Key == null || entry.Key.disabled || now - entry.Value.Seen > 0.35f)
                    staleOwners.Add(entry.Key);
            foreach (Aircraft owner in staleOwners) locks.Remove(owner);
        }

        internal static bool IsWeapon(WeaponInfo info)
        {
            return info != null && info.missile && info.weaponPrefab != null &&
                info.effectiveness.antiAir > info.effectiveness.antiSurface &&
                info.weaponPrefab.GetComponent<IRSeeker>() != null;
        }

        internal static bool IsGround(Unit target)
        {
            return target != null && (target is GroundVehicle || target is Building);
        }

        internal static bool Applies(Unit owner, WeaponInfo info, Unit target)
        {
            return BlindPlugin.Instance != null && BlindPlugin.Instance.EnableIrGroundAttack.Value &&
                owner is Aircraft && IsGround(target) && IsWeapon(info);
        }

        private static bool Evaluate(Aircraft owner, WeaponInfo info, Unit target, out string status)
        {
            status = null;
            if (owner.disabled || target.disabled || owner.NetworkHQ == null)
                status = "IR A/G: TARGET UNAVAILABLE";
            else if (!owner.NetworkHQ.TryGetKnownPosition(target, out var known) ||
                FastMath.OutOfRange(known, target.GlobalPosition(), 500f))
                status = "IR A/G: TARGET POSITION STALE";
            else
            {
                Vector3 offset = target.GlobalPosition() - owner.GlobalPosition();
                float range = offset.magnitude;
                float maxRange = info.targetRequirements.maxRange * BlindPlugin.Instance.IrGroundRangeFraction.Value;
                if (range < info.targetRequirements.minRange) status = "IR A/G: TOO CLOSE";
                else if (range > maxRange) status = "IR A/G: OUT OF RANGE";
                else if (Vector3.Angle(owner.transform.forward, offset) > info.targetRequirements.minAlignment)
                    status = "IR A/G: OUT OF ARC";
                else if (owner.speed < info.targetRequirements.minOwnerSpeed)
                    status = "IR A/G: TOO SLOW";
                else if (!target.LineOfSight(owner.transform.position, 1000f))
                    status = "IR A/G: TARGET MASKED";
            }
            return status == null;
        }

        internal static bool Check(Aircraft owner, WeaponInfo info, Unit target, out string status)
        {
            status = null;
            if (!Applies(owner, info, target)) return true;
            if (!Evaluate(owner, info, target, out status))
            {
                if (locks.TryGetValue(owner, out var invalid)) invalid.Started.Remove(target);
                return false;
            }
            float elapsed = 0f;
            if (locks.TryGetValue(owner, out var state) && state.Weapon == info &&
                Time.timeSinceLevelLoad - state.Seen <= 0.35f && state.Started.TryGetValue(target, out float started))
                elapsed = Time.timeSinceLevelLoad - started;
            float required = BlindPlugin.Instance.IrGroundLockTime.Value;
            if (elapsed < required)
            {
                status = "IR A/G: ACQUIRING " + Mathf.Clamp01(elapsed / required).ToString("P0");
                return false;
            }
            status = "IR A/G: SHOOT";
            return true;
        }
        internal static bool AllowLaunch(WeaponStation station, Unit owner, Unit target)
        {
            if (!(owner is Aircraft aircraft) || !Applies(owner, station.WeaponInfo, target)) return true;
            // Observer RPCs must replay the server's accepted launch, not revalidate stale local tracking.
            if (aircraft.remoteSim && !aircraft.IsServer) return true;
            bool allowed = Check(aircraft, station.WeaponInfo, target, out string status);
            if (!allowed && GameManager.GetLocalAircraft(out var local) && local == aircraft &&
                Time.unscaledTime - lastReport > 0.8f)
            {
                lastReport = Time.unscaledTime;
                if (SceneSingleton<AircraftActionsReport>.i != null)
                    SceneSingleton<AircraftActionsReport>.i.ReportText(status, 2f);
            }
            return allowed;
        }
    }

    [HarmonyPatch(typeof(HUDMissileState), "CalcWeaponRange")]
    internal static class IrGroundHudRangePatch
    {
        private static void Postfix(Aircraft ___aircraft, WeaponInfo ___weaponInfo,
            List<Unit> ___targetList, ref float ___maxRange, ref float ___noEscapeRange)
        {
            if (___targetList == null || ___targetList.Count == 0) return;
            foreach (Unit target in ___targetList)
                if (!IrGroundAttack.Applies(___aircraft, ___weaponInfo, target)) return;
            ___maxRange = Mathf.Min(___maxRange,
                ___weaponInfo.targetRequirements.maxRange * BlindPlugin.Instance.IrGroundRangeFraction.Value);
            ___noEscapeRange = Mathf.Min(___noEscapeRange, ___maxRange);
        }
    }

    [HarmonyPatch(typeof(HUDMissileState), "DisplayText")]
    internal static class IrGroundHudLockPatch
    {
        private static void Postfix(Aircraft ___aircraft, WeaponInfo ___weaponInfo,
            WeaponStation ___weaponStation, List<Unit> ___targetList, TextMeshProUGUI ___hint,
            Image ___noShoot, ref bool ___allRequirementsMet)
        {
            if (___weaponStation == null || ___weaponStation.Ammo <= 0 || ___targetList == null ||
                ___targetList.Count == 0 || !IrGroundAttack.Applies(___aircraft, ___weaponInfo, ___targetList[0])) return;
            if (!IrGroundAttack.Check(___aircraft, ___weaponInfo, ___targetList[0], out string status))
            {
                ___allRequirementsMet = false;
                ___noShoot.enabled = true;
                ___hint.enabled = true;
                ___hint.text = status;
            }
        }
    }
    [HarmonyPatch(typeof(WeaponStation), nameof(WeaponStation.LaunchMount))]
    internal static class IrGroundLaunchPatch
    {
        private static bool Prefix(WeaponStation __instance, Unit owner, Unit target)
        {
            return IrGroundAttack.AllowLaunch(__instance, owner, target);
        }
    }

    // Validate before the server invokes either LaunchMount or the observer RPC.
    [HarmonyPatch(typeof(Aircraft), "UserCode_CmdLaunchMissile_644415535")]
    internal static class IrGroundServerLaunchPatch
    {
        private static bool Prefix(Aircraft __instance, byte stationIndex, Unit target)
        {
            if (stationIndex >= __instance.weaponStations.Count) return true;
            return IrGroundAttack.AllowLaunch(__instance.weaponStations[stationIndex], __instance, target);
        }
    }

    [HarmonyPatch(typeof(IRSeeker), nameof(IRSeeker.Initialize))]
    internal static class IrGroundSourcePatch
    {
        private sealed class SourceLease
        {
            internal Unit Target;
            internal IRSource Source;
        }

        private static void Prefix(Missile ___missile, Unit target, out SourceLease __state)
        {
            __state = null;
            if (___missile == null || !IrGroundAttack.Applies(___missile.owner,
                ___missile.GetWeaponInfo(), target) || target.disabled || target.HasIRSignature()) return;
            // Native IRSeeker keeps this source reference. Do not add permanent/global heat to the unit.
            var part = target.GetRandomPart();
            Transform aim = part != null ? part.transform : target.transform;
            var source = new IRSource(aim, 0.35f, false);
            target.AddIRSource(source);
            __state = new SourceLease { Target = target, Source = source };
        }

        private static Exception Finalizer(Exception __exception, SourceLease __state)
        {
            if (__state != null && __state.Target != null)
                __state.Target.RemoveIRSource(__state.Source);
            return __exception;
        }
    }
}