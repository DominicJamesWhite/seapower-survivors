using System.Linq;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// How well the fleet can shoot missiles down, as one number. Enemy groups
/// that fire anti-ship missiles wait until the fleet reaches their threshold,
/// so a gun cruiser fights a gun war until its upgrades, or a ship that joins,
/// give it a real chance against a salvo.
/// </summary>
static class Defence
{
    /// <summary>Thresholds for missile-armed groups: boats with a few small missiles, medium salvos, heavy salvos.</summary>
    public const float Light = 3f, Medium = 5f, Heavy = 8f;

    const float PerSamLauncher = 3f;
    const float PerAntiAirGun = 0.25f;
    /// <summary>A CIWS mount at Phalanx's 40% missile intercept chance.</summary>
    const float ReferenceCiwsIntercept = 40f, PerReferenceCiws = 2f;

    public static float Score(Run run)
    {
        float score = 0f;
        bool antiAirGuns = false;
        foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed))
        {
            score += Boosts.SamLaunchers(ship).Count() * PerSamLauncher;
            score += ship._obp._ciwsWeapons.Sum(CiwsWorth);
            int guns = Boosts.Guns(ship).Count(CanEngageAir);
            score += guns * PerAntiAirGun;
            antiAirGuns |= guns > 0;
        }

        // Upgrades count too, heavily, since three ranks of a line are already
        // overwhelming: they are what lets a gun ship earn its way to
        // facing missiles. Point defence ranks already show in the CIWS
        // intercept chance; gun ranks count only if the guns can shoot upwards.
        score += 2f * run.RankOf(Upgrades.Sams) + run.RankOf(Upgrades.Countermeasures);
        if (antiAirGuns) score += run.RankOf(Upgrades.Guns) + run.RankOf(Upgrades.Gunnery) + run.RankOf(Upgrades.Flak);
        return score;
    }

    /// <summary>
    /// Weighted by the square of the live intercept chance, so a weak mount
    /// counts for little however many there are: Phalanx 2, AK-630 0.5,
    /// AK-230 0.125. A Sverdlov's eight AK-230s are not a missile defence.
    /// </summary>
    static float CiwsWorth(WeaponSystemCIWS ciws)
    {
        float relative = ciws._missileInterceptChance / ReferenceCiwsIntercept;
        return PerReferenceCiws * relative * relative;
    }

    /// <summary>Thresholds for air groups: a pair of bombers, a strike package, a heavy raid.</summary>
    public const float AirLight = 3f, AirMedium = 6f, AirHeavy = 10f;

    /// <summary>
    /// How well the fleet can shoot aircraft down. Aircraft are bigger and
    /// slower than missiles, so guns and CIWS count for more than in Score,
    /// but CIWS only reaches a couple of miles and counts for less than a SAM.
    /// </summary>
    public static float AirScore(Run run)
    {
        float score = 0f;
        bool antiAirGuns = false;
        foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed))
        {
            score += Boosts.SamLaunchers(ship).Count() * 4f;
            // Mounts outnumber directors on old gun ships (a Sverdlov has six
            // 100 mm mounts on two Round Tops), so each mount counts for little.
            score += ship._obp._ciwsWeapons.Sum(c => 0.25f * c._aircraftInterceptChance / 45f);
            int guns = Boosts.Guns(ship).Count(CanEngageAir);
            score += guns * 0.25f;
            antiAirGuns |= guns > 0;
        }
        score += 2f * run.RankOf(Upgrades.Sams) + run.RankOf(Upgrades.PointDefence);
        if (antiAirGuns) score += run.RankOf(Upgrades.Guns) + run.RankOf(Upgrades.Gunnery) + 2f * run.RankOf(Upgrades.Flak);
        return score;
    }

    /// <summary>Aircraft allowed airborne at once: one per two points of air score, at least one.</summary>
    public static int AircraftCap(Run run) => System.Math.Max(1, (int)(AirScore(run) / 2f));

    static bool CanEngageAir(WeaponSystemGun gun) =>
        (gun._vwp._associatedMagazine?._ammunitionList ?? Enumerable.Empty<Ammunition>())
            .Any(a => a?._ap != null && (a._ap._targetType == Ammunition.Target.AAW ||
                                         (a._ap._hasSecondaryTargetType && a._ap._secondaryTargetType == Ammunition.Target.AAW)));
}
