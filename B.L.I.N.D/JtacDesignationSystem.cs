using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BLIND
{
    internal sealed class JtacDesignationSystem
    {
        private sealed class Designation
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
                .Where(unit => unit.NetworkHQ == friendlyHq && IsGroundDesignator(unit))
                .ToArray();

            HashSet<Unit> validObservers = new HashSet<Unit>(observers);
            foreach (Unit observer in _active.Keys.ToArray())
            {
                Designation designation = _active[observer];
                bool expired = Time.timeSinceLevelLoad - designation.StartedAt >= _plugin.MaxDesignationTime.Value;
                bool valid = validObservers.Contains(observer) &&
                             IsValidPair(observer, designation.Target, aircraft, true);
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

            Unit selectedTarget = GetSelectedSurfaceTarget(aircraft, friendlyHq);
            if (selectedTarget == null)
            {
                return;
            }
            if (IsTargetDesignated(selectedTarget))
            {
                SetDiagnostic(string.Empty);
                return;
            }
            if (FastMath.OutOfRange(aircraft.GlobalPosition(), selectedTarget.GlobalPosition(),
                    _plugin.AircraftReceiveRange.Value))
            {
                SetDiagnostic("JTAC • TARGET OUT OF DATALINK RANGE");
                return;
            }
            if (_active.Count >= _plugin.MaxConcurrentDesignations.Value)
            {
                SetDiagnostic("JTAC • DESIGNATORS BUSY");
                return;
            }

            Unit[] available = observers.Where(IsObserverAvailable).ToArray();
            if (available.Length == 0)
            {
                SetDiagnostic(observers.Length == 0
                    ? "JTAC • NO ALLIED GROUND DESIGNATOR"
                    : "JTAC • DESIGNATORS BUSY / COOLING");
                return;
            }

            Unit[] inRange = available
                .Where(observer => FastMath.InRange(observer.GlobalPosition(), selectedTarget.GlobalPosition(),
                    _plugin.GroundLaserRange.Value))
                .OrderBy(observer => FastMath.SquareDistance(observer.GlobalPosition(), selectedTarget.GlobalPosition()))
                .ToArray();
            if (inRange.Length == 0)
            {
                SetDiagnostic("JTAC • NO DESIGNATOR WITHIN " +
                              (_plugin.GroundLaserRange.Value * 0.001f).ToString("0.0") + " KM");
                return;
            }

            Unit observerWithLos = inRange.FirstOrDefault(observer => HasLineOfSight(observer, selectedTarget));
            if (observerWithLos == null)
            {
                SetDiagnostic("JTAC • NO GROUND LINE OF SIGHT");
                return;
            }

            Acquire(observerWithLos, selectedTarget, friendlyHq);
            SetDiagnostic(string.Empty);
        }

        private Unit GetSelectedSurfaceTarget(Aircraft aircraft, FactionHQ friendlyHq)
        {
            List<Unit> targetList = aircraft.weaponManager.GetTargetList();
            if (targetList == null || targetList.Count == 0)
            {
                SetDiagnostic(string.Empty);
                return null;
            }

            Unit selected = targetList[0];
            if (!IsAlive(selected)) return null;
            if (selected.NetworkHQ == null || selected.NetworkHQ == friendlyHq)
            {
                SetDiagnostic("JTAC • SELECT A HOSTILE TARGET");
                return null;
            }
            if (!(selected is GroundVehicle) && !(selected is Building))
            {
                SetDiagnostic("JTAC • TARGET IS NOT A SURFACE UNIT / BUILDING");
                return null;
            }
            return selected;
        }

        private bool IsObserverAvailable(Unit observer)
        {
            if (_active.ContainsKey(observer)) return false;
            float cooldown;
            return !_cooldownUntil.TryGetValue(observer.GetInstanceID(), out cooldown) ||
                   Time.timeSinceLevelLoad >= cooldown;
        }

        private bool IsValidPair(Unit observer, Unit target, Aircraft aircraft, bool requireAircraftRange)
        {
            if (!IsAlive(observer) || !IsAlive(target) || aircraft == null) return false;
            if (!IsGroundDesignator(observer) || observer.NetworkHQ == null || target.NetworkHQ == observer.NetworkHQ)
            {
                return false;
            }
            if (!(target is GroundVehicle) && !(target is Building)) return false;
            if (FastMath.OutOfRange(observer.GlobalPosition(), target.GlobalPosition(), _plugin.GroundLaserRange.Value))
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
            float height = observer.definition == null ? 2f : observer.definition.height;
            Vector3 origin = observer.transform.position + Vector3.up * Mathf.Max(1.5f, height * 0.55f);
            return target.LineOfSight(origin, 1000f);
        }

        private static bool IsGroundDesignator(Unit unit)
        {
            return unit is GroundVehicle || unit is Building;
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
                                             " is designating selected target " + target.unitName + " for " +
                                             _plugin.MaxDesignationTime.Value.ToString("0") + " seconds.");
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
