using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mirage;
namespace Torpedo
{
    [HarmonyPatch(typeof(Encyclopedia), "AfterLoad", new Type[] { })]
    [HarmonyPriority(Priority.Last)]
    public static class TorpedoMounts_Patch
    {
        private struct VariantInfo
        {
            public string SourceMissile;
            public string NewName;
            public string DisplayName;
            public string ShortName;
            public float Mass;
            public float Cost;
            public string Description;
            public float HoverAltitude;
            public float SpeedMultiplier;
            public float CruiseSpeed;
            public float ExplosiveMultiplier;
            public float PenetrationBonus;
            public float MaxRange;
        }
        private static readonly VariantInfo[] Variants = new[]
        {
            new VariantInfo { SourceMissile = "AShM1", NewName = "TorpedoFast",
                DisplayName = "SCT-350 'Mako'", ShortName = "TORP-FAST", Mass = 300f, Cost = 18f, HoverAltitude = -1f, SpeedMultiplier = 0.10f, CruiseSpeed = 140f, ExplosiveMultiplier = 0.55f, PenetrationBonus = 75f, MaxRange = 60000f,
                Description = "A super-cavitating interceptor engineered for pure kinetic urgency. By generating a localized gas-bubble envelope to negate hydrodynamic drag, the Mako closes the distance to its targets with predatory velocity. It can also be used as a counter-torpedo, but not very effective against its own kind" },
            new VariantInfo { SourceMissile = "AShM2", NewName = "TorpedoLight",
                DisplayName = "Type-88 'Lemon'", ShortName = "TORP-LIGHT", Mass = 250f, Cost = 14f, HoverAltitude = -1f, SpeedMultiplier = 0.088f, CruiseSpeed = 85f, ExplosiveMultiplier = 0.25f, PenetrationBonus = 0f, MaxRange = 35000f,
                Description = "Light, compact, agile, and deceptively lethal. While its 'Lemon' designation suggests a dud, it is anything but, a single Darkreach can launch 32 Lemons at once. Even if it's not the fastest, the deadliest, nor stealthiest, it's the cheapest, most reliable, and most accessible torpedo on the menu." },
            new VariantInfo { SourceMissile = "CruiseMissile1", NewName = "TorpedoBig",
                DisplayName = "HT-200 'Hammerhead'", ShortName = "TORP-BIG", Mass = 450f, Cost = 30f, HoverAltitude = -8f, SpeedMultiplier = 0.04f, CruiseSpeed = 55f, ExplosiveMultiplier = 1.0f, PenetrationBonus = 300f, MaxRange = 90000f,
                Description = "Heavy, slow, and deadly. Just one Hammerhead has a warhead capable of splitting a capital ship in half, but requiring a patience to see the devastating outcome as it is classified as a marathon torpedo. The same distance travesed by 'Mako' will take twice as long for 'Hammerhead'." },
            new VariantInfo { SourceMissile = "CruiseMissile20kt", NewName = "Torpedo20kt",
                DisplayName = "NT-2 'Megalodon' (20kt)", ShortName = "TORP-20KT", Mass = 450f, Cost = 80f, HoverAltitude = -20f, SpeedMultiplier = 0.059f, CruiseSpeed = 65f, ExplosiveMultiplier = 1f, PenetrationBonus = 0f, MaxRange = 120000f,
                Description = "Strategic nuclear torpedo. Designed for total annihilation of anything unlucky enough to be within its blast radius. It is the most elusive as it swims the deepest and silent, but is also the slowest torpedo in the menu. Best used as a last resort, or when you have plenty of time." },
        };
                 private static readonly HashSet<string> _createdMissiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _createdMounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, float> HoverAltitudeByName =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, float> CruiseSpeedByName =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> CreatedMountNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, WeaponInfo> _infoByMountName =
            new Dictionary<string, WeaponInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<(WeaponMount Mount, string MountName, string SourceMountName)> _mountPairs =
            new List<(WeaponMount, string, string)>();
        private static bool _initialized;
        static TorpedoMounts_Patch()
        {
            foreach (var variant in Variants)
            {
                HoverAltitudeByName[variant.NewName] = variant.HoverAltitude;
            }
        }
        [HarmonyPostfix]
        public static void Postfix(Encyclopedia __instance)
        {
            if (_initialized || __instance == null) return;
            try
            {
                AddMissingMounts(__instance);
                _initialized = true;
            }
            catch (Exception ex) { TorpedoPlugin.ModLogger.LogError("[Torpedo] TorpedoMounts Postfix failed: " + ex); }
        }
        private static void AddMissingMounts(Encyclopedia __instance)
        {
            // Snapshot Unity's global object registry once. Repeating FindObjectsOfTypeAll for
            // every torpedo variant creates large temporary arrays during the busiest load phase.
            WeaponMount[] allMounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            WeaponManager[] allManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();
            var usedMountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WeaponManager wm in allManagers)
            {
                if (wm == null || wm.hardpointSets == null) continue;
                foreach (var set in wm.hardpointSets)
                {
                    if (set == null || set.weaponOptions == null) continue;
                    foreach (WeaponMount option in set.weaponOptions)
                        if (option != null) usedMountNames.Add(option.name);
                }
            }
            foreach (var variant in Variants)
            {
                MissileDefinition missileDefinition = __instance.missiles.FirstOrDefault(m => m != null && m.name == variant.NewName);
                WeaponInfo info = null;
                GameObject missileClone = null;
                if (missileDefinition == null)
                {
                    if (_createdMissiles.Contains(variant.NewName)) continue;
                    var result = CreateMissileVariant(__instance, variant);
                    if (result == null) continue;
                    (missileDefinition, info, missileClone) = result.Value;
                    _createdMissiles.Add(variant.NewName);
                    TorpedoPlugin.ModLogger.LogInfo($"[Torpedo] TorpedoMounts: added missile {variant.NewName} (from {variant.SourceMissile})");
                }
                else
                {
                    info = ResourceLookupWeaponInfo(missileDefinition, variant);
                    missileClone = missileDefinition.unitPrefab;
                    if (info == null || missileClone == null) continue;
                }
                string prefix = variant.SourceMissile + "_";
                var sourceMounts = allMounts
                    .Where(m => m != null && m.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Where(m => usedMountNames.Contains(m.name))
                    .GroupBy(m => m.name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First());
                foreach (var sourceMount in sourceMounts)
                {
                    string suffix = sourceMount.name.Substring(variant.SourceMissile.Length);
                    string newMountName = variant.NewName + suffix;
                    WeaponMount mount = __instance.weaponMounts.FirstOrDefault(m => m != null && m.name == newMountName);
                    if (mount == null)
                    {
                        if (_createdMounts.Contains(newMountName)) continue;
                        mount = CreateMountVariant(__instance, sourceMount, newMountName, variant, info, missileClone);
                        if (mount == null) continue;
                        _createdMounts.Add(newMountName);
                        CreatedMountNames.Add(newMountName);
                        TorpedoPlugin.ModLogger.LogInfo($"[Torpedo] TorpedoMounts: added {newMountName} (from {sourceMount.name})");
                    }
                    RegisterOnHardpointsCarrying(mount, newMountName, sourceMount.name);
                    if (!_mountPairs.Any(p => p.MountName == newMountName))
                        _mountPairs.Add((mount, newMountName, sourceMount.name));
                }
            }
        }
        internal static void RegisterOnWeaponManager(WeaponManager wm)
        {
            if (wm == null || wm.hardpointSets == null) return;
            foreach (var hardpointSet in wm.hardpointSets)
            {
                if (hardpointSet == null || hardpointSet.weaponOptions == null) continue;
                foreach (var (mount, mountName, sourceMountName) in _mountPairs)
                {
                    if (mount == null) continue;
                    if (!hardpointSet.weaponOptions.Any(m => m != null && m.name == sourceMountName)) continue;
                    if (hardpointSet.weaponOptions.Contains(mount)) continue;
                    hardpointSet.weaponOptions.Add(mount);
                    TorpedoPlugin.ModLogger.LogInfo($"[Torpedo] TorpedoMounts: registered {mountName} on {wm.gameObject.name} hardpoint '{hardpointSet.name}' (WeaponManager.Awake)");
                }
            }
        }
        private static WeaponInfo ResourceLookupWeaponInfo(MissileDefinition missileDefinition, VariantInfo variant)
        {
            GameObject prefab = missileDefinition.unitPrefab;
            Missile missile = prefab != null ? prefab.GetComponent<Missile>() : null;
            return missile != null ? missile.GetWeaponInfo() : null;
        }
        private static (MissileDefinition, WeaponInfo, GameObject)? CreateMissileVariant(Encyclopedia enc, VariantInfo variant)
        {
            GameObject sourceMissileGO = Resources.FindObjectsOfTypeAll<GameObject>()
                .FirstOrDefault(go => go != null && go.transform.parent == null && go.name == variant.SourceMissile && go.GetComponent<Missile>() != null);
            if (sourceMissileGO == null) return null;
            Missile sourceMissile = sourceMissileGO.GetComponent<Missile>();
            WeaponInfo sourceWeaponInfo = sourceMissile.GetWeaponInfo();
            MissileDefinition sourceMissileDefinition = sourceMissile.definition as MissileDefinition;
            if (sourceWeaponInfo == null || sourceMissileDefinition == null) return null;
            GameObject missileClone = CleanClone(sourceMissileGO, variant.NewName);
            if (missileClone.GetComponent<TorpedoIdentity>() == null)
                missileClone.AddComponent<TorpedoIdentity>();
            if (missileClone.GetComponent<TorpedoRuntimeController>() == null)
                missileClone.AddComponent<TorpedoRuntimeController>();
            Missile clonedMissile = missileClone.GetComponent<Missile>();
            float speedMultiplier = variant.SpeedMultiplier * TorpedoPlugin.SpeedScale.Value;
            float sourceMaxRange = Mathf.Max(1f, sourceWeaponInfo.targetRequirements.maxRange);
            float balancedMaxRange = variant.MaxRange;
            float physicalRangeScale = balancedMaxRange / sourceMaxRange;
            ApplySpeedMultiplier(clonedMissile, speedMultiplier, physicalRangeScale);
            MissileDefinition missileDefinition = UnityEngine.Object.Instantiate(sourceMissileDefinition);
            missileDefinition.name = variant.NewName;
            missileDefinition.jsonKey = variant.NewName;
            missileDefinition.unitName = variant.DisplayName;
            missileDefinition.description = variant.Description;
            missileDefinition.unitPrefab = missileClone;
            missileDefinition.dontAutomaticallyAddToEncyclopedia = false;
            Traverse.Create(clonedMissile).Field("definition").SetValue(missileDefinition);
            WeaponInfo info = UnityEngine.Object.Instantiate(sourceWeaponInfo);
            info.name = $"{variant.NewName}_info";
            info.weaponName = variant.DisplayName;
            info.shortName = variant.ShortName;
            info.massPerRound = variant.Mass;
            info.costPerRound = variant.Cost;
            info.description = variant.Description;
            info.weaponPrefab = missileClone;
            ConfigureAiRoles(info, variant.NewName);
            // The Megalodon is explicitly a 20 kt weapon: never scale its nuclear yield.
            float explosiveMultiplier = variant.NewName == "Torpedo20kt"
                ? 1f
                : variant.ExplosiveMultiplier * TorpedoPlugin.WarheadScale.Value;
            float penetrationBonus = variant.PenetrationBonus * TorpedoPlugin.PenetrationScale.Value;
            info.blastDamage *= explosiveMultiplier;
            info.pierceDamage += penetrationBonus;
            TargetRequirements requirements = info.targetRequirements;
            requirements.maxRange = balancedMaxRange;
            requirements.minRange = 500f;
            requirements.minAlignment = TorpedoPlugin.AcquisitionHalfAngle.Value;
            requirements.lineOfSight = false;
            info.targetRequirements = requirements;
            // Force AI interception calculations to recalculate from the balanced prefab speed.
            info.maxSpeed = -1f;
            Traverse.Create(clonedMissile).Field("info").SetValue(info);
            Traverse blastYieldTraverse = Traverse.Create(clonedMissile).Field("blastYield");
            blastYieldTraverse.SetValue(blastYieldTraverse.GetValue<float>() * explosiveMultiplier);
            Traverse pierceDamageTraverse = Traverse.Create(clonedMissile).Field("pierceDamage");
            pierceDamageTraverse.SetValue(pierceDamageTraverse.GetValue<float>() + penetrationBonus);
            float targetCruiseSpeed = variant.CruiseSpeed;
            CruiseSpeedByName[variant.NewName] = targetCruiseSpeed;
            TorpedoPlugin.ModLogger.LogInfo(
                $"[Torpedo] {variant.NewName}: speed x{speedMultiplier:0.000} (cruise {targetCruiseSpeed:0.0} m/s), " +
                $"warhead x{explosiveMultiplier:0.00}, range {requirements.maxRange:0} m, " +
                $"AI/player cone ±{requirements.minAlignment:0.#}°");
            enc.missiles.Add(missileDefinition);
            return (missileDefinition, info, missileClone);
        }
        private static WeaponMount CreateMountVariant(Encyclopedia enc, WeaponMount sourceMount, string newMountName, VariantInfo variant, WeaponInfo info, GameObject missileClone)
        {
            if (sourceMount.prefab == null) return null;
            GameObject mountClone = CleanClone(sourceMount.prefab, newMountName);
            foreach (var mounted in mountClone.GetComponentsInChildren<MountedMissile>(true))
                Traverse.Create(mounted).Field("info").SetValue(info);
            foreach (var cargo in mountClone.GetComponentsInChildren<MountedCargo>(true))
                Traverse.Create(cargo).Field("info").SetValue(info);
            WeaponMount mount = UnityEngine.Object.Instantiate(sourceMount);
            mount.name = newMountName;
            mount.jsonKey = newMountName;
            mount.mountName = variant.DisplayName;
            mount.prefab = mountClone;
            mount.info = info;
            mount.dontAutomaticallyAddToEncyclopedia = false;
            try { mount.Initialize(); }
            catch (Exception ex) { TorpedoPlugin.ModLogger.LogWarning("[Torpedo] WeaponMount.Initialize failed for " + newMountName + ": " + ex.Message); }
            enc.weaponMounts.Add(mount);
            _infoByMountName[newMountName] = info;
            return mount;
        }
        internal static bool TryGetBalancedInfo(string mountName, out WeaponInfo info)
        {
            return _infoByMountName.TryGetValue(mountName ?? string.Empty, out info) && info != null;
        }
        public static bool IsBalancedTorpedoInfo(WeaponInfo info)
        {
            if (info == null) return false;
            foreach (var balanced in _infoByMountName.Values)
            {
                if (ReferenceEquals(info, balanced))
                    return true;
            }

            if (info.weaponPrefab != null && HoverAltitudeByName.ContainsKey(info.weaponPrefab.name))
                return true;

            if (!string.IsNullOrEmpty(info.name))
            {
                foreach (var variant in Variants)
                {
                    if (info.name.StartsWith(variant.NewName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
        private static void ConfigureAiRoles(WeaponInfo info, string variantName)
        {
            RoleIdentity roles = info.effectiveness;
            if (variantName == "TorpedoFast")
            {
                roles.antiSurface = 0.85f;
                roles.antiMissile = 1.0f;
                roles.antiAir = 0f;
                info.pK = 0.72f;
            }
            else if (variantName == "TorpedoLight")
            {
                roles.antiSurface = 1.0f;
                roles.antiMissile = 0.55f;
                roles.antiAir = 0f;
                info.pK = 0.50f;
            }
            else if (variantName == "TorpedoBig")
            {
                roles.antiSurface = 1.0f;
                roles.antiMissile = 0f;
                roles.antiAir = 0f;
                info.pK = 0.85f;
            }
            else if (variantName == "Torpedo20kt")
            {
                roles.antiSurface = 1.0f;
                roles.antiMissile = 0f;
                roles.antiAir = 0f;
                info.pK = 0.95f;
            }
            info.effectiveness = roles;
        }
        private static void ApplySpeedMultiplier(Missile missile, float speedMultiplier, float rangeMultiplier)
        {
            float thrustMultiplier = Mathf.Sqrt(speedMultiplier);
            object motorsObj = Traverse.Create(missile).Field("motors").GetValue();
            if (!(motorsObj is Array motorsArray)) return;
            foreach (object motor in motorsArray)
            {
                if (motor == null) continue;
                Traverse motorTraverse = Traverse.Create(motor);
                motorTraverse.Field("thrust").SetValue(motorTraverse.Field("thrust").GetValue<float>() * thrustMultiplier);
                motorTraverse.Field("topSpeed").SetValue(motorTraverse.Field("topSpeed").GetValue<float>() * speedMultiplier);
                // Dividing by speed keeps the original source range; RangeScale then shortens
                // both physical endurance and the AI/player engagement envelope by the same ratio.
                motorTraverse.Field("burnTime").SetValue(
                    motorTraverse.Field("burnTime").GetValue<float>() / speedMultiplier * rangeMultiplier);
            }
        }
        private static GameObject CleanClone(GameObject original, string newName)
        {
            GameObject clone = UnityEngine.Object.Instantiate(original);
            clone.name = newName;
            clone.transform.SetParent(null);
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.SetActive(false);
            NetworkIdentity networkIdentity = clone.GetComponentInChildren<NetworkIdentity>();
            if (networkIdentity != null)
            {
                Traverse identityTraverse = Traverse.Create(networkIdentity);
                networkIdentity.PrefabHash = newName.GetHashCode();
                identityTraverse.Field("_hasSpawned").SetValue(false);
                identityTraverse.Method("NetworkReset", Array.Empty<object>()).GetValue();
            }
            UnityEngine.Object.DontDestroyOnLoad(clone);
            return clone;
        }
        private static void RegisterOnHardpointsCarrying(WeaponMount mount, string mountName, string sourceMountName)
        {
            var weaponManagers = Resources.FindObjectsOfTypeAll<WeaponManager>();
            foreach (var wm in weaponManagers)
            {
                if (wm.hardpointSets == null) continue;
                foreach (var hardpointSet in wm.hardpointSets)
                {
                    if (hardpointSet == null || hardpointSet.weaponOptions == null) continue;
                    if (!hardpointSet.weaponOptions.Any(m => m != null && m.name == sourceMountName)) continue;
                    if (hardpointSet.weaponOptions.Contains(mount)) continue;
                    hardpointSet.weaponOptions.Add(mount);
                    TorpedoPlugin.ModLogger.LogInfo($"[Torpedo] TorpedoMounts: registered {mountName} on {wm.gameObject.name} hardpoint '{hardpointSet.name}'");
                }
            }
        }
    }
    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    [HarmonyPriority(Priority.Last)]
    public static class TorpedoMounts_WeaponManagerAwake_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(WeaponManager __instance)
        {
            try { TorpedoMounts_Patch.RegisterOnWeaponManager(__instance); }
            catch (Exception ex) { TorpedoPlugin.ModLogger.LogError("[Torpedo] TorpedoMounts WeaponManager.Awake postfix failed: " + ex); }
        }
    }
    [HarmonyPatch(typeof(WeaponChecker), "VetLoadout")]
    public static class TorpedoMounts_VetLoadout_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(AircraftDefinition definition)
        {
            try
            {
                WeaponManager wm = definition?.unitPrefab?.GetComponent<Aircraft>()?.weaponManager;
                if (wm != null) TorpedoMounts_Patch.RegisterOnWeaponManager(wm);
            }
            catch (Exception ex) { TorpedoPlugin.ModLogger.LogError("[Torpedo] TorpedoMounts VetLoadout prefix failed: " + ex); }
        }
    }
}
