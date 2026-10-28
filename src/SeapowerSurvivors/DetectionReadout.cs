using System;
using System.Collections.Generic;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// For each enemy unit and missile, which of the player's search radars can
/// see it and, if none can, what stops them. Mirrors the gates in
/// Radar.IsDetectableTarget_Conditional and RadarCalculator.createRadarEcho:
/// role, altitude window, max range, horizon and line of sight, then the
/// radar equation. Jamming and the optional low-altitude clutter model are
/// left out.
/// </summary>
/// <remarks>
/// The radar equation is recomputed here rather than calling createRadarEcho,
/// because that draws from Globals._sensorEchoes, a ring buffer the game's own
/// sensor pass uses, and calling it from outside that pass could overwrite
/// echoes still in use.
/// </remarks>
static class DetectionReadout
{
    const int MaxTargets = 12;
    internal const float MetresPerUnit = 67.200065f;

    internal static List<string> Build(ObjectBase ship)
    {
        var lines = new List<string>();
        var radars = ship._obp._searchRadarSystems;

        foreach (var radar in radars)
        {
            var rp = radar._radar._rp;
            string state = radar.Inoperable.Value ? "BROKEN" : radar.IsOn.Value ? "on" : "OFF";
            string alt = rp._role == RadarParameters.Role.Surface ? "surface only"
                : $"min alt {rp._minAltitude * MetresPerUnit:F0} m";
            lines.Add($"{radar._systemName} [{state}] {rp._role}, antenna {radar.SensorPosition.y * MetresPerUnit:F0} m, " +
                $"max {Nm(rp._maxRange):F0} nm, {alt}, gain x{rp._gainFactor:F0}");
        }

        var targets = Singleton<ObjectsManager>.Instance._listOfAllObjects
            .Where(o => o != null && !o.IsDestroyed && o._taskforce == Globals._enemyTaskforce && (o.isUnit() || o is Missile))
            .OrderBy(o => (o.transform.position - ship.transform.position).magnitude)
            .Take(MaxTargets);

        foreach (var target in targets)
        {
            float distance = (target.transform.position - ship.transform.position).magnitude;
            string label = target is Missile ? $"missile {target._obp?._objectIniName}" : target.Name.Value;
            var seenBy = new List<string>();
            var blocked = new List<string>();
            foreach (var radar in radars)
            {
                string reason = Blocker(ship, radar, target);
                if (reason == null) seenBy.Add(radar._systemName);
                else blocked.Add($"{radar._systemName}: {reason}");
            }
            lines.Add(seenBy.Count > 0
                ? $"SEEN    {label} {Nm(distance):F1} nm, alt {target.VisibleAltitude * MetresPerUnit:F0} m, by {string.Join(", ", seenBy)}"
                : $"HIDDEN  {label} {Nm(distance):F1} nm, alt {target.VisibleAltitude * MetresPerUnit:F0} m: {string.Join("; ", blocked)}");
        }
        return lines;
    }

    /// <summary>Why this radar cannot see the target, or null if it can.</summary>
    static string Blocker(ObjectBase ship, SensorSystemRadar radar, ObjectBase target)
    {
        var rp = radar._radar._rp;
        if (radar.Inoperable.Value) return "broken";
        if (!radar.IsOn.Value) return "off";

        bool air = target.isAirTarget();
        if (air && rp._role == RadarParameters.Role.Surface) return "surface-only radar";
        if (!air && rp._role == RadarParameters.Role.Air) return "air-only radar";
        if (air && (target.AltitudeOverClutter < rp._minAltitude || target.transform.position.y > rp._maxAltitude))
            return $"outside altitude window (min {rp._minAltitude * MetresPerUnit:F0} m)";

        Vector3 antenna = radar.SensorPosition;
        float distance = (target.transform.position - antenna).magnitude;
        if (distance > rp._maxRange) return $"beyond max range {Nm(rp._maxRange):F0} nm";

        var los = Singleton<UnitSensorCache>.Instance.UnitsHaveRadarlLOS(ship, target, antenna.y, distance);
        if (distance > los._horizon) return $"beyond horizon {Nm(los._horizon):F1} nm";
        if (!los._los) return "terrain in the way";

        float power = PowerRangeUnity(rp, target);
        if (distance > power) return $"too weak, reaches {Nm(power):F1} nm";
        return null;
    }

    /// <summary>
    /// RadarCalculator.BaseMaxDetectionRangeMeter, with the target's frontal
    /// RCS (the pessimistic aspect) and the sea-state penalty for ships.
    /// </summary>
    static float PowerRangeUnity(RadarParameters rp, ObjectBase target)
    {
        var data = target.getSensorData();
        if (data == null) return float.MaxValue;
        float rcs = data._minRCS > 0f ? data._minRCS : data._maxRCS;
        double metres = Math.Pow(
            rp._peakPower * 1000.0 * Math.Pow(rp._gainFactor, 2) * Math.Pow(rp._waveLength, 2) * rcs /
            (Globals.RcsValues._minDetectableReturnPowerRadar * 1000.0 * Math.Pow(Math.PI * 4.0, 3)), 0.25);
        int seaState = Singleton<SeaPower.Environment>.Instance.SeaState;
        if (seaState > 1 && target is Vessel) metres *= 1.0 - 0.05 * seaState;
        return (float)(metres * 0.0148809375);
    }

    internal static float Nm(float unity) => unity / 27.559496f;
}
