using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BLIND
{
    internal sealed class JtacDesignationSystem
    {
        internal sealed class Designation
        {
            internal Unit Observer;
            internal Unit Target;
            internal FactionHQ Hq;
            internal float StartedAt;
        }

        private readonly BlindPlugin _plugin;
        private readonly Dictionary<Unit, Designation> _active = new Dictionary<Unit, Designation>();
        private readonly Dictionary<int, float> _cooldownUntil = new Dictionary<int, float>();
        private float _nextScan;
        private bool _authorityWarningLogged;

        internal string DiagnosticStatus { get; private set; }
        internal float DiagnosticUntil { get; private set; }
        internal bool ShowDiagnostic
        {
            get { return !string.IsNullOrEmpty(DiagnosticStatus) && Time.unscaledTime < DiagnosticUntil; }
        }

        internal JtacDesignationSystem(BlindPlugin plugin)
        {
            _plugin = plugin;
        }

        internal void Update(Aircraft localAircraft)
        {
            if (!_plugin.EnableJtac.Value || localAircraft == null || localAircraft.NetworkHQ == null)
            {
                ClearAll();
                SetDiagnostic(string.Empty);
                return;
            }

            if (!localAircraft.IsServer)
            {
                ClearAll();
                SetDiagnostic("JTAC • HOST ONLY");
                if (!_authorityWarningLogged)
                {
                    _authorityWarningLogged = true;
                    BlindPlugin.LogSource.LogWarning(
                        "[B.L.I.N.D.] JTAC is host-authoritative. Sensor modes remain available on clients.");
                }
                return;
            }

            _authorityWarningLogged = false;
            if (Time.timeSinceLevelLoad < _nextScan) return;
            _nextScan = Time.timeSinceLevelLoad + 0.25f;
            Scan(localAircraft);
        }

        internal bool TryGetBestCue(Aircraft aircraft, out Unit target, out float range)
        {
            target = null;
            range = float.MaxValue;
            if (aircraft == null) return false;

            foreach (Designation designation in _active.Values)
            {
                if (!IsAlive(designation.Target)) continue;
                float distance = FastMath.Distance(aircraft.GlobalPosition(), designation.Target.GlobalPosition());
                if (distance <= _plugin.AircraftReceiveRange.Value && distance < range)
                {
                    target = designation.Target;
                    range = distance;
                }
            }
            return target != null;
        }

        internal void GetActiveCues(Aircraft aircraft, List<Designation> results)
        {
            results.Clear();
            if (aircraft == null) return;

            foreach (Designation designation in _active.Values)
            {
                if (!IsAlive(designation.Target)) continue;
                float distance = FastMath.Distance(aircraft.GlobalPosition(), designation.Target.GlobalPosition());
                if (distance <= _plugin.AircraftReceiveRange.Value)
                {
                    results.Add(designation);
                }
            }
        }

        internal bool IsTargetDesignated(Unit unit)
        {
            return unit != null && _active.Values.Any(value => value.Target == unit);
        }

        internal bool TryGetDesignatedTargetInView(Transform seeker, float maxAngle, float maxRange, out Unit target)
        {
            target = null;
            float bestAngle = maxAngle;
            foreach (Designation designation in _active.Values)
            {
                Unit candidate = designation.Target;
                if (!IsAlive(candidate) ||
                    FastMath.OutOfRange(candidate.transform.position, seeker.position, maxRange))
                {
                    continue;
                }
                Vector3 direction = candidate.transform.position - seeker.position;
                float angle = Vector3.Angle(seeker.forward, direction);
                if (angle <= bestAngle && candidate.LineOfSight(seeker.position, 1000f))
                {
                    bestAngle = angle;
                    target = candidate;
                }
            }
            return target != null;
        }

        internal void Shutdown()
        {
            ClearAll();
            _cooldownUntil.Clear();
        }

        private void Scan(Aircraft aircraft)
        {
            FactionHQ friendlyHq = aircraft.NetworkHQ;
            Unit[] units = UnitRegistry.allUnits.Where(IsAlive).ToArray();
            Unit[] observers = units
                .Where(unit => unit.NetworkHQ == friendlyHq && IsValidDesignatorUnit(unit))
                .ToArray();

            HashSet<Unit> validObservers = new HashSet<Unit>(observers);

            // 1. Maintain or prune existing active designations
            foreach (Unit observer in _active.Keys.ToArray())
            {
                Designation designation = _active[observer];
                bool missileInFlight = LaserSeekerPersistence.HasActiveMissileTracking(designation.Target);
                if (missileInFlight)
                {
                    // Refresh designation lifespan while missile is actively tracking
                    designation.StartedAt = Mathf.Max(designation.StartedAt, Time.timeSinceLevelLoad - (_plugin.MaxDesignationTime.Value * 0.5f));
                }

                bool expired = !missileInFlight && (Time.timeSinceLevelLoad - designation.StartedAt >= _plugin.MaxDesignationTime.Value);
                bool valid = validObservers.Contains(observer) &&
                             IsValidPair(observer, designation.Target, aircraft, false);
                if (!valid || expired)
                {
                    Release(observer);
                    if (expired && observer != null)
                    {
                        _cooldownUntil[observer.GetInstanceID()] =
                            Time.timeSinceLevelLoad + _plugin.DesignatorCooldown.Value;
                    }
                }
            }

            // 2. Fetch all selected hostile surface/naval targets from aircraft weapon manager
            List<Unit> selectedTargets = GetSelectedSurfaceTargets(aircraft, friendlyHq);
            if (selectedTargets.Count == 0)
            {
                return;
            }

            // If all selected targets are already lased, clear diagnostic banner
            bool allDesignated = selectedTargets.All(IsTargetDesignated);
            if (allDesignated)
            {
                SetDiagnostic(string.Empty);
                return;
            }

            // 3. Multi-target assignment: 1 observer designates exactly 1 target.
            // When multiple targets are selected, assign available observers greedily
            // by best proximity/LOS. If there aren't enough allies, designate as many as possible.
            int successfullyAssigned = 0;
            string lastReason = string.Empty;

            foreach (Unit target in selectedTargets)
            {
                if (IsTargetDesignated(target))
                {
                    successfullyAssigned++;
                    continue;
                }

                if (_active.Count >= _plugin.MaxConcurrentDesignations.Value)
                {
                    lastReason = "JTAC • DESIGNATORS MAXED (" + _plugin.MaxConcurrentDesignations.Value + ")";
                    break;
                }

                if (FastMath.OutOfRange(aircraft.GlobalPosition(), target.GlobalPosition(), _plugin.AircraftReceiveRange.Value))
                {
                    lastReason = "JTAC • TARGET OUT OF DATALINK RANGE";
                    continue;
                }

                Unit[] available = observers.Where(IsObserverAvailable).ToArray();
                if (available.Length == 0)
                {
                    lastReason = observers.Length == 0 ? "JTAC • NO ALLIED DESIGNATORS" : "JTAC • ALLIES BUSY / COOLING";
                    break;
                }

                // Check observers in laser range of this specific target
                Unit[] inRange = available
                    .Where(obs => FastMath.InRange(obs.GlobalPosition(), target.GlobalPosition(), GetMaxDesignationRange(obs, target)))
                    .OrderBy(obs => FastMath.SquareDistance(obs.GlobalPosition(), target.GlobalPosition()))
                    .ToArray();

                if (inRange.Length == 0)
                {
                    float maxR = (target is Ship) ? _plugin.NavalLaserRange.Value : _plugin.GroundLaserRange.Value;
                    lastReason = "JTAC • NO ALLY IN RANGE (" + (maxR * 0.001f).ToString("0.0") + " KM)";
                    continue;
                }

                Unit observerWithLos = inRange.FirstOrDefault(obs => HasLineOfSight(obs, target));
                if (observerWithLos == null)
                {
                    lastReason = "JTAC • NO LINE OF SIGHT TO TARGET";
                    continue;
                }

                // Found matching available observer!
                Acquire(observerWithLos, target, friendlyHq);
                successfullyAssigned++;
            }

            if (successfullyAssigned > 0)
            {
                SetDiagnostic(string.Empty);
            }
            else if (!string.IsNullOrEmpty(lastReason))
            {
                SetDiagnostic(lastReason);
            }
        }

        private List<Unit> GetSelectedSurfaceTargets(Aircraft aircraft, FactionHQ friendlyHq)
        {
            List<Unit> result = new List<Unit>();
            if (aircraft == null || aircraft.weaponManager == null) return result;

            List<Unit> targetList = aircraft.weaponManager.GetTargetList();
            if (targetList == null || targetList.Count == 0)
            {
                SetDiagnostic(string.Empty);
                return result;
            }

            for (int i = 0; i < targetList.Count; i++)
            {
                Unit candidate = targetList[i];
                if (!IsAlive(candidate)) continue;
                if (candidate.NetworkHQ == null || candidate.NetworkHQ == friendlyHq) continue;

                // Support Ground Vehicles, Buildings, and Naval Ships
                if (candidate is GroundVehicle || candidate is Building || candidate is Ship)
                {
                    if (!result.Contains(candidate))
                    {
                        result.Add(candidate);
                    }
                }
            }

            if (result.Count == 0 && targetList.Count > 0)
            {
                Unit first = targetList[0];
                if (first != null && (first.NetworkHQ == null || first.NetworkHQ == friendlyHq))
                {
                    SetDiagnostic("JTAC • SELECT A HOSTILE TARGET");
                }
                else
                {
                    SetDiagnostic("JTAC • AIR TARGETS CANNOT BE GROUND-LASED");
                }
            }

            return result;
        }

        private bool IsObserverAvailable(Unit observer)
        {
            if (_active.ContainsKey(observer)) return false;
            float cooldown;
            return !_cooldownUntil.TryGetValue(observer.GetInstanceID(), out cooldown) ||
                   Time.timeSinceLevelLoad >= cooldown;
        }

        private float GetMaxDesignationRange(Unit observer, Unit target)
        {
            // Ships and naval targets have elevated optics / open sea horizon: 15km range
            if (observer is Ship || target is Ship)
            {
                return _plugin.NavalLaserRange.Value;
            }
            return _plugin.GroundLaserRange.Value;
        }

        private bool IsValidPair(Unit observer, Unit target, Aircraft aircraft, bool requireAircraftRange)
        {
            if (!IsAlive(observer) || !IsAlive(target) || aircraft == null) return false;
            if (!IsValidDesignatorUnit(observer) || observer.NetworkHQ == null || target.NetworkHQ == observer.NetworkHQ)
            {
                return false;
            }
            if (!(target is GroundVehicle) && !(target is Building) && !(target is Ship)) return false;

            float maxRange = GetMaxDesignationRange(observer, target);
            if (FastMath.OutOfRange(observer.GlobalPosition(), target.GlobalPosition(), maxRange))
            {
                return false;
            }
            if (requireAircraftRange && FastMath.OutOfRange(aircraft.GlobalPosition(), target.GlobalPosition(),
                    _plugin.AircraftReceiveRange.Value))
            {
                return false;
            }
            return HasLineOfSight(observer, target);
        }

        private static bool HasLineOfSight(Unit observer, Unit target)
        {
            if (observer == null || target == null) return false;
            float height = observer.definition == null ? 2.5f : observer.definition.height;
            // Elevate sensor origin based on unit type (ships have high superstructures/masts)
            float eyeOffset = (observer is Ship) ? Mathf.Max(6f, height * 0.7f) : Mathf.Max(1.8f, height * 0.55f);
            Vector3 origin = observer.transform.position + Vector3.up * eyeOffset;
            return target.LineOfSight(origin, 1000f);
        }

        private static bool IsValidDesignatorUnit(Unit unit)
        {
            // Allied ground vehicles, coastal/ground defense buildings, and naval combat ships
            return unit is GroundVehicle || unit is Building || unit is Ship;
        }

        private void Acquire(Unit observer, Unit target, FactionHQ hq)
        {
            _active.Add(observer, new Designation
            {
                Observer = observer,
                Target = target,
                Hq = hq,
                StartedAt = Time.timeSinceLevelLoad
            });
            hq.UpdateLasedState(target, true);
            BlindPlugin.LogSource.LogMessage("[B.L.I.N.D.] " + observer.unitName +
                                             " is designating hostile " + target.unitName + " for " +
                                             _plugin.MaxDesignationTime.Value.ToString("0") + "s.");
        }

        private void Release(Unit observer)
        {
            Designation designation;
            if (!_active.TryGetValue(observer, out designation)) return;
            if (designation.Hq != null && designation.Target != null)
            {
                designation.Hq.UpdateLasedState(designation.Target, false);
            }
            _active.Remove(observer);
            BlindPlugin.LogSource.LogMessage("[B.L.I.N.D.] Ground designation by " + observer.unitName + " ended.");
        }

        private void ClearAll()
        {
            foreach (Unit observer in _active.Keys.ToArray())
            {
                Release(observer);
            }
        }

        private void SetDiagnostic(string message)
        {
            DiagnosticStatus = message;
            DiagnosticUntil = string.IsNullOrEmpty(message) ? 0f : Time.unscaledTime + 0.75f;
        }

        private static bool IsAlive(Unit unit)
        {
            return unit != null && !unit.disabled && unit.gameObject.activeInHierarchy;
        }
    }
}
