using System.Collections.Generic;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Which way the enemy can come from. Ships need open water all the way in,
/// since they are sent straight at the fleet, so a ship is only spawned on a
/// bearing clear of land out to its spawn distance. In an area, ships lean
/// towards the enemy's waters and aircraft towards its airfield; in open
/// ocean they come from anywhere.
/// </summary>
static class Sea
{
    /// <summary>Shallower than this is land as far as spawning goes: a ship spawned there runs aground.</summary>
    internal const float MinDepthM = 15f;
    const float StepNm = 1f;
    const float BearingStepDeg = 5f;
    /// <summary>How wide the enemy's approach is: the spread of bearings either side of its waters or airfield.</summary>
    const float ApproachSpreadDeg = 40f;
    /// <summary>Aircraft that come from the enemy's airfield; the rest come from anywhere, to keep the fleet looking all round.</summary>
    const float AirFromAirfield = 0.7f;
    /// <summary>A bearing well off the approach still turns up now and then, at this weight against 1 on it.</summary>
    const float OffApproachWeight = 0.1f;
    /// <summary>The bearings are measured again once the fleet has moved this far, or the spawn is further out than they were measured.</summary>
    const float RemeasureNm = 2f;
    /// <summary>Bearings are measured at least this far out, so spawns at varied distances share one measurement.</summary>
    const float MeasureNm = 40f;

    static GeoPosition measuredAt;
    static float measuredTo;
    static float[] runs;

    /// <summary>The area of the run being played, if it was launched in one.</summary>
    static Area Area => Launcher.IsLaunchedMission() ? Launcher.Current.Area : null;

    /// <summary>
    /// In metres: <see cref="TerrainUtils.GetHeightAt"/> is in Unity units of
    /// about 67 m, so its "below -20" meant a kilometre of water and failed
    /// everywhere in a shallow strait. Depths come out exaggerated, which only
    /// errs towards calling a shoal land. Off the loaded terrain it reads as
    /// deep water.
    /// </summary>
    internal static bool IsWater(GeoPosition at) => TerrainUtils.GetRealHeightInMetersAt(at) < -MinDepthM;

    /// <summary>How far open water runs from <paramref name="from"/> on this bearing, up to <paramref name="limitNm"/>.</summary>
    internal static float ClearRunNm(GeoPosition from, float bearing, float limitNm)
    {
        for (float d = StepNm; d <= limitNm; d += StepNm)
            if (!IsWater(Spawner.Offset(from, d, bearing))) return d - StepNm;
        return limitNm;
    }

    /// <summary>
    /// A bearing for ships with open water out to <paramref name="distanceNm"/>,
    /// leaning towards the enemy's waters. Hemmed in on every side, it is the
    /// longest clear run there is, and the distance comes in to fit it.
    /// </summary>
    /// <param name="avoid">Bearings already taken, which this one keeps <paramref name="spreadDeg"/> clear of if any open bearing can.</param>
    internal static float ShipBearing(GeoPosition from, ref float distanceNm, IList<float> avoid = null, float spreadDeg = 0f)
    {
        var open = OpenBearings(from, distanceNm, out float longest, out float longestRun);
        if (open.Count == 0)
        {
            distanceNm = Mathf.Max(longestRun, StepNm);
            return longest;
        }
        if (avoid != null && avoid.Count > 0)
        {
            var apart = open.Where(b => avoid.All(a => Mathf.Abs(Mathf.DeltaAngle(a, b)) >= spreadDeg)).ToList();
            if (apart.Count > 0) open = apart;
        }
        return Pick(open, Towards(from, Area?.EnemyWaters));
    }

    /// <summary>
    /// A convoy's lane: a straight run of open water <paramref name="lengthNm"/>
    /// long whose closest point to the fleet is <paramref name="passNm"/> away.
    /// In an area it runs along the enemy's side, between the fleet and the
    /// enemy's waters, as coastal shipping does; in open ocean it lies anywhere.
    /// Null when no lane tried is all water.
    /// </summary>
    internal static (GeoPosition start, GeoPosition end, float heading)? ConvoyLane(GeoPosition from, float passNm, float lengthNm)
    {
        float? towards = Towards(from, Area?.EnemyWaters);
        float first = towards ?? Random.Range(0f, 360f);
        // Out from the approach bearing, alternately either side of it.
        for (int i = 0; i < 12; i++)
        {
            float off = (i + 1) / 2 * 30f * (i % 2 == 1 ? 1f : -1f);
            float bearing = Wrap(first + off);
            var pass = Spawner.Offset(from, passNm, bearing);
            if (!IsWater(pass)) continue;
            foreach (float turn in Random.value < 0.5f ? new[] { 90f, -90f } : new[] { -90f, 90f })
            {
                float heading = Wrap(bearing + turn);
                var start = Spawner.Offset(pass, lengthNm / 2f, Wrap(heading + 180f));
                if (ClearRunNm(start, heading, lengthNm) >= lengthNm)
                    return (start, Spawner.Offset(start, lengthNm, heading), heading);
            }
        }
        return null;
    }

    /// <summary>
    /// <paramref name="count"/> bearings for a ring of ships, spread evenly over
    /// the open ones, so a wall in a strait closes from both ends of it rather
    /// than from the shore.
    /// </summary>
    internal static List<float> ShipRing(GeoPosition from, int count, float distanceNm)
    {
        var open = OpenBearings(from, distanceNm, out float longest, out _);
        if (open.Count == 0) return Enumerable.Repeat(longest, count).ToList();
        int first = Random.Range(0, open.Count);
        return Enumerable.Range(0, count).Select(i => open[(first + i * open.Count / count) % open.Count]).ToList();
    }

    /// <summary>Aircraft fly over land: mostly from the enemy's airfield, otherwise from anywhere.</summary>
    internal static float AirBearing(GeoPosition from)
    {
        float? towards = Towards(from, Area?.EnemyAir);
        if (towards is float bearing && Random.value < AirFromAirfield)
            return Wrap(bearing + Random.Range(-ApproachSpreadDeg, ApproachSpreadDeg));
        return Random.Range(0f, 360f);
    }

    /// <summary>
    /// Where a group's <paramref name="index"/>th member goes: abreast of the
    /// bearing, alternately each side of the leader a mile apart, or in line
    /// further out along it where abreast is land.
    /// </summary>
    internal static GeoPosition Abreast(GeoPosition leader, float bearing, int index)
    {
        if (index == 0) return leader;
        int step = (index + 1) / 2;
        var beside = Spawner.Offset(leader, step, Wrap(bearing + (index % 2 == 1 ? 90f : -90f)));
        return IsWater(beside) ? beside : Spawner.Offset(leader, index, bearing);
    }

    /// <summary>
    /// Moves a spawn so that all its <paramref name="members"/> are at least
    /// <paramref name="clearNm"/> from every one of <paramref name="ships"/>.
    /// Spawn distances are measured from the flagship, and an escort on station
    /// can be sitting right where one lands. A spot already clear is left alone;
    /// otherwise the nearest bearing at this distance, then further out, then
    /// nearer in. False if nowhere is clear, as in a narrow strait: the spawn
    /// then takes the clearest spot there is.
    /// </summary>
    /// <param name="water">Whether the way in from the spot must be open water, as for ships.</param>
    internal static bool KeepClear(GeoPosition from, IList<GeoPosition> ships, int members, bool water, float clearNm, ref float bearing, ref float distanceNm)
    {
        float bestGap = GroupGap(ships, Spawner.Offset(from, distanceNm, bearing), bearing, members);
        if (bestGap >= clearNm) return true;

        // Past this, a spot is clear of every ship whichever way it lies.
        float furthest = Mathf.Max(distanceNm, ships.Max(s => Spawner.Nm(from, s)) + clearNm + members);
        if (water) Measure(from, furthest);

        float startBearing = bearing, startDistance = distanceNm, bestBearing = bearing, bestDistance = distanceNm;
        var distances = Enumerable.Range(0, Mathf.CeilToInt(furthest - startDistance) + 1).Select(i => startDistance + i)
            .Concat(Enumerable.Range(1, Mathf.Max(0, Mathf.FloorToInt(startDistance) - 1)).Select(i => startDistance - i));
        int steps = Mathf.RoundToInt(180f / BearingStepDeg);
        foreach (float d in distances)
            for (int k = 0; k <= 2 * steps; k++)
            {
                // 0, +1, -1, +2, -2...: the smallest turn off the bearing first.
                float candidate = Wrap(startBearing + (k + 1) / 2 * BearingStepDeg * (k % 2 == 1 ? 1f : -1f));
                if (water && runs[Mathf.RoundToInt(candidate / BearingStepDeg) % runs.Length] < d) continue;
                var front = Spawner.Offset(from, d, candidate);
                // A group is never further from a ship than its leader is, so this skips the land checks in Abreast.
                if (Nearest(ships, front) <= bestGap) continue;
                float gap = GroupGap(ships, front, candidate, members);
                if (gap <= bestGap) continue;
                bestGap = gap;
                bestBearing = candidate;
                bestDistance = d;
                if (gap >= clearNm)
                {
                    bearing = candidate;
                    distanceNm = d;
                    return true;
                }
            }
        bearing = bestBearing;
        distanceNm = bestDistance;
        return false;
    }

    static float GroupGap(IList<GeoPosition> ships, GeoPosition front, float bearing, int members) =>
        Enumerable.Range(0, Mathf.Max(members, 1)).Min(i => Nearest(ships, Abreast(front, bearing, i)));

    static float Nearest(IList<GeoPosition> ships, GeoPosition at) =>
        ships.Min(s => Spawner.Nm(at, s));

    static List<float> OpenBearings(GeoPosition from, float distanceNm, out float longest, out float longestRun)
    {
        Measure(from, distanceNm);
        var open = new List<float>();
        longest = 0f;
        longestRun = -1f;
        for (int i = 0; i < runs.Length; i++)
        {
            float bearing = i * BearingStepDeg;
            if (runs[i] >= distanceNm) open.Add(bearing);
            if (runs[i] > longestRun)
            {
                longestRun = runs[i];
                longest = bearing;
            }
        }
        return open;
    }

    /// <summary>The clear run on every bearing, kept until the fleet moves or a spawn wants to look further.</summary>
    static void Measure(GeoPosition from, float distanceNm)
    {
        if (runs != null && distanceNm <= measuredTo && Spawner.Nm(from, measuredAt) < RemeasureNm) return;
        measuredAt = from;
        measuredTo = Mathf.Max(distanceNm, MeasureNm);
        runs = new float[Mathf.RoundToInt(360f / BearingStepDeg)];
        for (int i = 0; i < runs.Length; i++) runs[i] = ClearRunNm(from, i * BearingStepDeg, measuredTo);
    }

    static float? Towards(GeoPosition from, GeoPosition? to) =>
        to is GeoPosition target ? from.HeadingToPosition(target) : (float?)null;

    /// <summary>One of the bearings, weighted towards <paramref name="towards"/> if there is one.</summary>
    static float Pick(List<float> bearings, float? towards)
    {
        if (towards is not float approach) return bearings[Random.Range(0, bearings.Count)];
        var weights = bearings.Select(b =>
        {
            float off = Mathf.Abs(Mathf.DeltaAngle(b, approach)) / ApproachSpreadDeg;
            return OffApproachWeight + Mathf.Exp(-off * off);
        }).ToList();
        float roll = Random.value * weights.Sum();
        for (int i = 0; i < bearings.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0f) return bearings[i];
        }
        return bearings[bearings.Count - 1];
    }

    static float Wrap(float bearing) => (bearing % 360f + 360f) % 360f;
}
