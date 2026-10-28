using System.Collections.Generic;
using System.Linq;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// How far apart two ships start shooting at each other, so surface groups
/// can be spawned at the edge of the fight rather than at a fixed ring.
/// </summary>
/// <remarks>
/// A fixed ring is wrong for most pairings. Spawned ships close at 20 to 30
/// knots, under half a mile a minute, so a ring set by the enemy's missiles
/// puts a gun cruiser's targets an hour's steaming away: no kills, no XP, no
/// levels. Spawning just outside the player's reach instead means the player
/// opens fire within a minute, and a short-ranged enemy is shot at the whole
/// way in, which is the genre's march under fire.
/// </remarks>
static class Reach
{
    const float UnityPerNm = 27.559496f;
    /// <summary>Margin outside the player's reach: about a minute's closing at flank speed.</summary>
    public const float ApproachNm = 0.6f;
    /// <summary>Never closer than this, whatever the weapons say.</summary>
    const float MinimumNm = 6f;

    /// <summary>
    /// The player fleet's reach against this target: its longest anti-surface
    /// weapon, but no further than it can see the target, since nothing is
    /// fired at a contact below the radar horizon. That caps a Long Beach's
    /// Tomahawks at the horizon unless escorts can see further.
    /// </summary>
    public static float PlayerReachNm(IEnumerable<ObjectBase> fleet, ObjectBase target)
    {
        float best = 0f;
        foreach (var ship in fleet.Where(s => s != null && !s.IsDestroyed))
        {
            float weapon = AntiSurfaceRangeNm(ship);
            float horizon = HorizonNm(ship, target);
            best = UnityEngine.Mathf.Max(best, UnityEngine.Mathf.Min(weapon, horizon));
        }
        return UnityEngine.Mathf.Max(best, MinimumNm);
    }

    public static float SpawnDistanceNm(IEnumerable<ObjectBase> fleet, ObjectBase target) =>
        PlayerReachNm(fleet, target) + ApproachNm;

    /// <summary>Longest range of anything aboard that can be fired at a ship.</summary>
    public static float AntiSurfaceRangeNm(ObjectBase ship)
    {
        float best = 0f;
        foreach (var ap in AllAmmo(ship))
        {
            bool antiSurface = ap._targetType == Ammunition.Target.ASuW ||
                               (ap._hasSecondaryTargetType && ap._secondaryTargetType == Ammunition.Target.ASuW);
            if (antiSurface) best = UnityEngine.Mathf.Max(best, ap._maxLaunchRangeInMiles);
        }
        return best;
    }

    /// <summary>The best search-radar horizon between the two, from the game's own 4/3-earth formula.</summary>
    static float HorizonNm(ObjectBase from, ObjectBase target)
    {
        float antenna = from._obp._searchRadarSystems.Count == 0
            ? from.transform.position.y
            : from._obp._searchRadarSystems.Max(r => r.SensorPosition.y);
        return RadarCalculator.getRadarHorizon(antenna, target.VisibleAltitude) / UnityPerNm;
    }

    static IEnumerable<AmmunitionParameters> AllAmmo(ObjectBase ship) =>
        ship._obp._magazineSystems.SelectMany(m => m._ammunitionList)
            .Concat(ship._obp._weaponSystems.SelectMany(w => w._containers).Select(c => c._initialAmmunition))
            .Where(a => a?._ap != null)
            .Select(a => a._ap);
}
