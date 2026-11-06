using System.Linq;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// How well the fleet can shoot missiles down, as one number: roughly the
/// missiles it can stop in one salvo. Enemy groups that fire anti-ship missiles
/// wait until the fleet reaches their threshold, and Pressure holds the missiles
/// committed at once under a share of it, so a gun cruiser fights a gun war
/// until its upgrades, or a ship that joins, give it a real chance against a salvo.
/// </summary>
static class Defence
{
    /// <summary>Thresholds for missile-armed groups: boats with a few small missiles, medium salvos, heavy salvos.</summary>
    public const float Light = 3f, Medium = 5f, Heavy = 8f;

    const float PerSamLauncher = 3f;
    /// <summary>Small: twenty 5-inch guns on an Iowa are not a missile defence either.</summary>
    const float PerAntiAirGun = 0.05f;
    /// <summary>A CIWS mount at Phalanx's 40% missile intercept chance.</summary>
    const float ReferenceCiwsIntercept = 40f, PerReferenceCiws = 1f;

    public static float Score(Run run)
    {
        float score = 0f;
        bool antiAirGuns = false;
        // Ships only: a fighter's missiles are on its pylons, not between the fleet and a sea-skimmer.
        foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit))
        {
            // The Dutch ensign keeps them out of action, so they stop nothing.
            if (!Ensigns.Active(Ensigns.LongCiws)) score += Boosts.SamLaunchers(ship).Count(Boosts.HasSamsLeft) * PerSamLauncher;
            score += ship._obp._ciwsWeapons.Sum(CiwsWorth);
            int guns = Boosts.Guns(ship).Count(CanEngageAir);
            score += guns * PerAntiAirGun;
            antiAirGuns |= guns > 0;
        }

        // Upgrades count too, heavily, since six ranks of a line are already
        // overwhelming: they are what lets a gun ship earn its way to
        // facing missiles. Point defence ranks already show in the CIWS
        // intercept chance; gun and accuracy ranks count only if the guns can
        // shoot upwards. Ranks are half-size steps, so each counts half.
        score += 0.6f * run.RankOf(Upgrades.Sams) + 0.3f * run.RankOf(Upgrades.Countermeasures);
        if (antiAirGuns) score += 0.45f * run.RankOf(Upgrades.Guns) + 0.3f * run.RankOf(Upgrades.Accuracy);
        return score;
    }

    /// <summary>
    /// Weighted by the square of the live intercept chance, so a weak mount
    /// counts for little however many there are: Phalanx 1, AK-630 0.25,
    /// AK-230 0.06. A Sverdlov's eight AK-230s are not a missile defence.
    /// </summary>
    static float CiwsWorth(WeaponSystemCIWS ciws)
    {
        float relative = ciws._missileInterceptChance / ReferenceCiwsIntercept;
        return PerReferenceCiws * relative * relative;
    }

    internal static bool CanEngageAir(WeaponSystemGun gun) =>
        (gun._vwp._associatedMagazine?._ammunitionList ?? Enumerable.Empty<Ammunition>())
            .Any(a => a?._ap != null && (a._ap._targetType == Ammunition.Target.AAW ||
                                         (a._ap._hasSecondaryTargetType && a._ap._secondaryTargetType == Ammunition.Target.AAW)));
}
