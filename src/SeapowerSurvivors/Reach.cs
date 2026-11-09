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
///
/// Reach has to be the range the launcher itself will fire at, not the
/// missile's nominal range. The AI commits a weapon to a target inside the
/// nominal range, and a launcher whose target is beyond its real range waits
/// in "target too far" holding its director, guns' included. With a ring of
/// boats kept just beyond an over-estimate, a Kidd's SM-1s sat on them for
/// minutes while the missiles they fired went unanswered.
/// </remarks>
static class Reach
{
    const float UnityPerNm = 27.559496f;
    /// <summary>Margin outside the player's reach: about a minute's closing at flank speed.</summary>
    public const float ApproachNm = 0.6f;
    /// <summary>Never closer than this, whatever the weapons say.</summary>
    const float MinimumNm = 6f;

    /// <summary>
    /// The player fleet's reach against this target: the longest of its
    /// ships' anti-surface weapons that still have rounds, each as far as its
    /// launcher would fire at this target (see <see cref="WeaponReachNm"/>).
    /// </summary>
    public static float PlayerReachNm(IEnumerable<ObjectBase> fleet, ObjectBase target)
    {
        float best = 0f;
        // Ships only: a strike flight's reach is wherever it is sent.
        foreach (var ship in fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit))
        {
            float search = HorizonNm(ship, target);
            foreach (var weapon in ship._obp._weaponSystems.Where(w => w?._vwp != null && !w.Inoperable.Value))
                foreach (var ap in AntiSurfaceAmmo(weapon).Where(a => !Ensigns.Withheld(a)))
                    best = UnityEngine.Mathf.Max(best, WeaponReachNm(ship, weapon, ap, target, search));
        }
        return UnityEngine.Mathf.Max(best, MinimumNm);
    }

    public static float SpawnDistanceNm(IEnumerable<ObjectBase> fleet, ObjectBase target) =>
        PlayerReachNm(fleet, target) + ApproachNm;

    /// <summary>
    /// How far an enemy ship will fire its anti-ship missiles: the longest
    /// nominal range among its working launchers, derated as the game derates
    /// AI launches against ships (0.95). These missiles find the target
    /// themselves, so nothing caps them at the horizon once the target is on
    /// the plot (WeaponSystemLauncher.CalculateHorizon caps only semi-active
    /// and command-guided rounds). A heat-seeker with
    /// no mid-course guidance goes no further than its seeker sees, however far
    /// it could fly (AI.SelectLauncherAmmoForAutoEngage): 10 nm for an SS-N-2C
    /// listed at 50. One its director must guide (an SS-N-3 or SS-N-12) goes only
    /// as far as that director sees, and not at all if its minimum range lies
    /// beyond that. Zero for a ship that cannot fire any.
    /// </summary>
    public static float EnemyStrikeNm(ObjectBase ship, ObjectBase target) =>
        Boosts.AntiShipLaunchers(ship).Where(l => !l.Inoperable.Value)
            .SelectMany(l => Boosts.LauncherAmmo(l).Where(Boosts.AntiShipMissile).Select(ap => StrikeNm(l, ap, ship, target)))
            .DefaultIfEmpty(0f).Max();

    /// <summary>The furthest minimum launch range among a ship's working anti-ship missiles: it must hold outside this to fire them.</summary>
    public static float EnemyMinimumNm(ObjectBase ship) =>
        Boosts.AntiShipLaunchers(ship).Where(l => !l.Inoperable.Value)
            .SelectMany(Boosts.LauncherAmmo).Where(Boosts.AntiShipMissile)
            .Select(ap => ap._launchRangesInUnity.x / UnityPerNm)
            .DefaultIfEmpty(0f).Max();

    /// <summary>
    /// How far an enemy ship reaches with its guns and ship-killing torpedoes.
    /// An ASW torpedo does not count: a Stenka's SET-40s are not fired at ships.
    /// </summary>
    public static float EnemyGunReachNm(ObjectBase ship) =>
        ship._obp._weaponSystems.Where(w => w?._vwp != null && !w.Inoperable.Value)
            .SelectMany(w => (w._vwp._associatedMagazine?._ammunitionList ?? Enumerable.Empty<Ammunition>())
                .Concat(w._containers.Select(c => c._initialAmmunition)))
            .Where(a => a?._ap != null).Select(a => a._ap)
            .Where(ap => (ap._type == Ammunition.Type.Projectile || ap._type == Ammunition.Type.Torpedo) &&
                         (ap._targetType == Ammunition.Target.ASuW || (ap._hasSecondaryTargetType && ap._secondaryTargetType == Ammunition.Target.ASuW)))
            .Select(ap => UnityEngine.Mathf.Max(ap._maxLaunchRangeInMiles, ap._launchRangesInUnity.y / UnityPerNm))
            .DefaultIfEmpty(0f).Max();

    static float StrikeNm(WeaponSystemLauncher launcher, AmmunitionParameters ap, ObjectBase ship, ObjectBase target)
    {
        if (ap._requiresGuidance)
        {
            float guided = MissileReachNm(launcher, ap, ship, target, HorizonNm(ship, target));
            return guided < ap._launchRangesInUnity.x / UnityPerNm ? 0f : guided;
        }
        float range = ap._launchRangesInUnity.y / UnityPerNm * 0.95f;
        bool seekerLimited = ap._guidanceType == AmmunitionParameters.GuidanceType.IRHoming &&
                             ap._midCourseCorrection == AmmunitionParameters.MidCourseCorrection.None;
        return seekerLimited ? UnityEngine.Mathf.Min(range, ap._seekerPassiveRange / UnityPerNm) : range;
    }

    /// <summary>Anti-ship missiles an enemy unit still has loaded and working.</summary>
    public static int EnemyMissilesLeft(ObjectBase unit) =>
        Boosts.AntiShipLaunchers(unit).Where(l => !l.Inoperable.Value)
            .Sum(l => Boosts.LauncherAmmo(l).Where(Boosts.AntiShipMissile).Sum(ap => l.getLoadedAmmoCount(ap._ammunitionFileName)));

    /// <summary>
    /// How far this weapon would fire at the target, the way its launcher
    /// judges it (WeaponSystemLauncher's "target too far"): a missile's
    /// kinematic range against this target, and for one its director guides
    /// all the way (semi-active, or command-guided) no further than that
    /// director's mount can see the target. Guns, and missiles that find the
    /// target themselves, reach as far as the search radars can see it.
    /// </summary>
    static float WeaponReachNm(ObjectBase ship, WeaponSystem weapon, AmmunitionParameters ap, ObjectBase target, float searchHorizonNm)
    {
        if (ap._type != Ammunition.Type.Missile) return UnityEngine.Mathf.Min(ap._maxLaunchRangeInMiles, searchHorizonNm);

        // A missile whose minimum range is beyond what it could reach here cannot fire at all, as with
        // a Slava's SS-N-12 before the range ranks that shrink its minimum range (Boosts).
        float reach = MissileReachNm(weapon, ap, ship, target, searchHorizonNm);
        return reach < ap._launchRangesInUnity.x / UnityPerNm ? 0f : reach;
    }

    static float MissileReachNm(WeaponSystem weapon, AmmunitionParameters ap, ObjectBase ship, ObjectBase target, float searchHorizonNm)
    {
        float kinematic = ap.MaxRangeRough(ship, target.transform.position, target._velocityVecInUnity, ap.AssumeEvasiveTarget(target)) / UnityPerNm;
        var guidance = ap.GuidanceTypeForTarget(target);
        bool directorGuided = guidance == AmmunitionParameters.GuidanceType.SemiActiveRadarHoming ||
                              (guidance == AmmunitionParameters.GuidanceType.None &&
                               ap._midCourseCorrection == AmmunitionParameters.MidCourseCorrection.RadioCommand);
        if (!directorGuided) return UnityEngine.Mathf.Min(kinematic, searchHorizonNm);

        float trackedAt = ap.FcrTrackedAltitude(target), terrain = Utils.TerrainAltForHorizon(ship, target);
        float director = weapon._vwp._associatedSensors
            .Where(s => s != null && !s.Inoperable.Value && s._mountObject != null && s.CanGuideAmmo(ap))
            .Select(s => RadarCalculator.getRadarHorizon(s._mountObject.transform.position.y, trackedAt, false, terrain) / UnityPerNm)
            .DefaultIfEmpty(0f).Max();
        return UnityEngine.Mathf.Min(kinematic, director);
    }

    /// <summary>What a weapon has rounds of, loaded or in its magazine, that can be fired at a ship.</summary>
    static IEnumerable<AmmunitionParameters> AntiSurfaceAmmo(WeaponSystem weapon) =>
        (weapon._vwp._associatedMagazine?._ammunitionList ?? Enumerable.Empty<Ammunition>())
            .Concat(weapon._containers.Select(c => c._initialAmmunition))
            .Where(a => a?._ap != null && AntiSurface(a._ap))
            .GroupBy(a => a._ap._ammunitionFileName)
            .Where(g => weapon.getLoadedAmmoCount(g.Key) + weapon.getMagazineAmmoCount(g.Key) > 0)
            .Select(g => g.First()._ap);

    /// <summary>
    /// Weapons made for ships. A SAM's secondary surface mode does not count, so
    /// a ship whose anti-ship missiles cannot fire at these ranges still has
    /// enemies come into its guns. Whether the AI would use the SAMs is untested.
    /// </summary>
    static bool AntiSurface(AmmunitionParameters ap) => ap._targetType == Ammunition.Target.ASuW;

    /// <summary>The best search-radar horizon between the two, from the game's own 4/3-earth formula.</summary>
    static float HorizonNm(ObjectBase from, ObjectBase target)
    {
        float antenna = from._obp._searchRadarSystems.Count == 0
            ? from.transform.position.y
            : from._obp._searchRadarSystems.Max(r => r.SensorPosition.y);
        return RadarCalculator.getRadarHorizon(antenna, target.VisibleAltitude) / UnityPerNm;
    }
}
