using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// Writes the run's upgrades into the fleet's live game objects.
/// </summary>
/// <remarks>
/// Every value is set as (the value the game first gave it) × (the run's
/// total), never multiplied in place. That makes applying idempotent: it can
/// run after every pick, after every rearm (which creates fresh rounds) and
/// for a ship that has just joined, without compounding.
///
/// Each ship holds its own copies of these parameters (AmmunitionParameters
/// is a per-ship clone of a shared master), so boosting the fleet never boosts
/// the enemy's identical weapons.
/// </remarks>
static class Boosts
{
    static readonly ConditionalWeakTable<object, Dictionary<string, float>> bases = new ConditionalWeakTable<object, Dictionary<string, float>>();

    static readonly AccessTools.FieldRef<WeaponSystemGun, float> ShellReload = AccessTools.FieldRefAccess<WeaponSystemGun, float>("_shellReloadTime");
    static readonly AccessTools.FieldRef<WeaponSystemGun, float> BurstDelay = AccessTools.FieldRefAccess<WeaponSystemGun, float>("_delayBetweenShotsinBurst");
    static readonly AccessTools.FieldRef<WeaponSystemChaff, double> ChaffReload = AccessTools.FieldRefAccess<WeaponSystemChaff, double>("_reloadTime");
    /// <summary>The game's pity rule: after this many player decoy successes in a row, the next is forced to fail.</summary>
    const int StockSpoofLimit = 3;
    const float MaxCiwsIntercept = 99f;
    static readonly AccessTools.FieldRef<WeaponSystemCIWS, float> CiwsCalibration = AccessTools.FieldRefAccess<WeaponSystemCIWS, float>("_sigmaFcsMissileMrad");

    /// <summary>Remember a field's original value the first time it is touched, and return it.</summary>
    static float Base(object owner, string field, float current)
    {
        var map = bases.GetOrCreateValue(owner);
        if (!map.TryGetValue(field, out float original)) map[field] = original = current;
        return original;
    }

    internal static void ApplyFleet(Run run)
    {
        foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed))
        {
            try { Apply(run, ship); }
            catch (Exception e) { Entry.Log.LogError($"Applying upgrades to {ship.Name.Value} failed: {e}"); }
        }
    }

    static void Apply(Run run, ObjectBase ship)
    {
        float gunRof = Upgrades.Total(run, Upgrades.Guns, Stat.RateOfFire);
        float gunDamage = Upgrades.Total(run, Upgrades.Guns, Stat.Damage);
        foreach (var gun in Guns(ship))
        {
            ShellReload(gun) = Base(gun, "shellReload", ShellReload(gun)) / gunRof;
            BurstDelay(gun) = Base(gun, "burstDelay", BurstDelay(gun)) / gunRof;
            foreach (var ap in MagazineAmmo(gun))
            {
                ap._warheadPower = Base(ap, "power", ap._warheadPower) * gunDamage;
                ApplyFlak(run, ap);
            }
        }

        float samReload = Upgrades.Total(run, Upgrades.Sams, Stat.Reload);
        float samKill = Upgrades.Total(run, Upgrades.Sams, Stat.KillChance);
        int samChannels = (int)Upgrades.Total(run, Upgrades.Sams, Stat.Channels);
        if (SamLaunchers(ship).Any())
            foreach (var director in ship._obp._targetingRadarSystems)
            {
                director._targetChannels = (int)Base(director, "targets", director._targetChannels) + samChannels;
                director._weaponChannels = (int)Base(director, "weapons", director._weaponChannels) + samChannels;
            }
        foreach (var launcher in SamLaunchers(ship))
        {
            launcher._vwp._magazineReloadTime = Base(launcher, "reload", launcher._vwp._magazineReloadTime) * samReload;
            foreach (var ap in LauncherAmmo(launcher).Where(a => a._targetType == Ammunition.Target.AAW))
                ap._killProbability = UnityEngine.Mathf.Min(1f, Base(ap, "kill", ap._killProbability) + samKill);
        }

        float asmDamage = Upgrades.Total(run, Upgrades.AntiShip, Stat.Damage);
        float asmSpeed = Upgrades.Total(run, Upgrades.AntiShip, Stat.Speed);
        float asmRange = Upgrades.Total(run, Upgrades.AntiShip, Stat.Range);
        float asmRcs = Upgrades.Total(run, Upgrades.AntiShip, Stat.Rcs);
        foreach (var launcher in AntiShipLaunchers(ship))
            foreach (var ap in LauncherAmmo(launcher).Where(a => a._targetType == Ammunition.Target.ASuW))
            {
                ap._warheadPower = Base(ap, "power", ap._warheadPower) * asmDamage;
                ap._maxVelocityInKnots = Base(ap, "speed", ap._maxVelocityInKnots) * asmSpeed;
                ap._maxLaunchRangeInMiles = Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles) * asmRange;
                ap._launchRangesInUnity.y = Base(ap, "rangeUnity", ap._launchRangesInUnity.y) * asmRange;
                // Faster and farther both need longer to get there.
                ap._maxFlightTime = Base(ap, "flightTime", ap._maxFlightTime) * asmRange;
                ap._maxRCS = Base(ap, "rcs", ap._maxRCS) * asmRcs;
            }

        float ciwsIntercept = Upgrades.Total(run, Upgrades.PointDefence, Stat.Intercept);
        float ciwsRof = Upgrades.Total(run, Upgrades.PointDefence, Stat.RateOfFire);
        foreach (var ciws in ship._obp._ciwsWeapons)
        {
            // A percentage; the hit model's calibration needs it below 100.
            float intercept = UnityEngine.Mathf.Min(MaxCiwsIntercept, Base(ciws, "intercept", ciws._missileInterceptChance) + ciwsIntercept);
            int rof = (int)(Base(ciws, "rof", ciws._fireRatePerMinute) * ciwsRof);
            if (intercept != ciws._missileInterceptChance || rof != ciws._fireRatePerMinute)
            {
                ciws._missileInterceptChance = intercept;
                ciws._fireRatePerMinute = rof;
                // The hit model is calibrated once against the intercept
                // chance; clearing it makes the CIWS refit to the new values.
                CiwsCalibration(ciws) = -1f;
            }
        }

        float spoof = Upgrades.Total(run, Upgrades.Countermeasures, Stat.Spoof);
        float chaffReload = Upgrades.Total(run, Upgrades.Countermeasures, Stat.Reload);
        foreach (var chaff in ship._obp._chaffWeapons)
        {
            ChaffReload(chaff) = Base(chaff, "reload", (float)ChaffReload(chaff)) * chaffReload;
            foreach (var ap in MagazineAmmo(chaff).Append(chaff._ammoInUse?._ap).Where(a => a != null).Distinct())
                ap._decoySpoofChance = UnityEngine.Mathf.Min(0.95f, Base(ap, "spoof", ap._decoySpoofChance) + spoof);
        }
        // Global, but only ever consulted for the player's own decoys.
        int cm = run.RankOf(Upgrades.Countermeasures);
        Globals._playerMissilesSpoofedLimit = cm >= Upgrades.Countermeasures.MaxRank ? int.MaxValue : StockSpoofLimit + 3 * cm;

        var crew = ship._crew;
        if (crew != null)
        {
            float reaction = Upgrades.Total(run, Upgrades.Reaction, Stat.Reaction);
            crew._newContactProcessTime = Base(crew, "contact", crew._newContactProcessTime) * reaction;
            crew._endAlignmentDelay = Base(crew, "align", crew._endAlignmentDelay) * reaction;
            crew._autoEngageCheckMultiplier = Base(crew, "engageCheck", crew._autoEngageCheckMultiplier) * reaction;
            crew._targetAquisitionSpeed = Base(crew, "acquire", crew._targetAquisitionSpeed) / reaction;
            crew._gunTargetAquisitionSpeed = Base(crew, "gunAcquire", crew._gunTargetAquisitionSpeed) / reaction;

            float gunnery = Upgrades.Total(run, Upgrades.Gunnery, Stat.Gunnery);
            crew._fcErrorMultiplier = Base(crew, "fcError", crew._fcErrorMultiplier) * gunnery;
            crew._fcSolutionRateMultiplier = Base(crew, "fcRate", crew._fcSolutionRateMultiplier) / gunnery;
        }
    }

    /// <summary>The fuze radius proximity fuzes get when the shell had none: an impact-fuzed 152 mm, say.</summary>
    const float MinFuzeRadiusM = 5f;
    const float AaRangeFraction = 0.6f;

    /// <summary>
    /// Fuzes and target types on one of our shell clones. The fuze only acts
    /// against aircraft (GunBarrel arms it for air targets alone), so none of
    /// this changes surface fire.
    /// </summary>
    static void ApplyFlak(Run run, AmmunitionParameters ap)
    {
        int rank = run.RankOf(Upgrades.Flak);
        var fuze = (AmmunitionParameters.FuzeType)(int)Base(ap, "fuzeType", (float)ap._fuzeType);
        float gate = Base(ap, "fuzeGate", ap._fuzeGateSeconds);
        float radius = Base(ap, "fuzeRadius", ap._fuzeProximityDistance);
        float aaRange = Base(ap, "aaRange", ap._maxAARangeInUnity);
        // Without an AA range the game treats a gun as direct fire against
        // aircraft: it holds fire whenever the shot needs more than 10° of
        // loft, which rules out most of the sky. Only three shells in the
        // game have one (US 5"/38 and 40 mm, Soviet 57 mm); AA fire-control
        // tables give the rest one, as the 5"/38 has, at 60% of max range.
        ap._maxAARangeInUnity = rank >= 1 && aaRange <= 0f ? AaRangeFraction * ap._launchRangesInUnity.y : aaRange;
        if (rank >= 1)
        {
            // A timed fuze bursts at the predicted range, so range error alone
            // misses; a proximity fuze bursts wherever the shell passes close.
            ap._fuzeType = AmmunitionParameters.FuzeType.Proximity;
            ap._fuzeGateSeconds = -1f;
            radius = UnityEngine.Mathf.Max(radius, MinFuzeRadiusM * MetresToUnity);
        }
        else
        {
            ap._fuzeType = fuze;
            ap._fuzeGateSeconds = gate;
        }
        ap._fuzeProximityDistance = radius * Upgrades.Total(run, Upgrades.Flak, Stat.Fuze);

        // Barrage fire: surface-only shells take aircraft as a second target type.
        var secondary = (Ammunition.Target)(int)Base(ap, "secondaryTarget", (float)ap._secondaryTargetType);
        bool hadSecondary = Base(ap, "hasSecondary", ap._hasSecondaryTargetType ? 1f : 0f) > 0f;
        bool barrage = Upgrades.Total(run, Upgrades.Flak, Stat.Barrage) > 0f &&
                       ap._targetType == Ammunition.Target.ASuW && secondary == Ammunition.Target.UNKNOWN;
        ap._secondaryTargetType = barrage ? Ammunition.Target.AAW : secondary;
        ap._hasSecondaryTargetType = barrage || hadSecondary;
    }

    const float MetresToUnity = 0.0148809375f;

    /// <summary>
    /// Flak's prediction step. The AA error budget is built fresh for every
    /// salvo, and its prediction term (target speed × time of flight) is the
    /// one the crew's fire-control error never touches, so it is scaled on
    /// the way out, for our guns only. Both overloads funnel into this one.
    /// </summary>
    [HarmonyPatch(typeof(WeaponSystemGun), nameof(WeaponSystemGun.BuildAaErrorBudget),
        new[] { typeof(Ammunition), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float) })]
    static class AaPrediction
    {
        static void Postfix(WeaponSystemGun __instance, ref WeaponSystemGun.AaErrorBudget __result)
        {
            var run = Run.Current;
            if (run == null || !run.Fleet.Contains(__instance._baseObject)) return;
            __result._predictionSigma *= Upgrades.Total(run, Upgrades.Flak, Stat.Prediction);
        }
    }

    internal static IEnumerable<WeaponSystemGun> Guns(ObjectBase ship) => ship._obp._gunWeapons;

    internal static IEnumerable<WeaponSystemLauncher> SamLaunchers(ObjectBase ship) =>
        ship._obp._launcherWeapons.Where(l => LauncherAmmo(l).Any(a => a._targetType == Ammunition.Target.AAW));

    internal static IEnumerable<WeaponSystemLauncher> AntiShipLaunchers(ObjectBase ship) =>
        ship._obp._launcherWeapons.Where(l => LauncherAmmo(l).Any(a => a._targetType == Ammunition.Target.ASuW));

    /// <summary>
    /// Every parameter object a launcher's rounds use: its magazine's stock,
    /// and for magazine-less launchers such as Harpoon canisters, the per-slot
    /// clones of the rounds loaded and of the reload type.
    /// </summary>
    static IEnumerable<AmmunitionParameters> LauncherAmmo(WeaponSystemLauncher launcher) =>
        (launcher._vwp._associatedMagazine?._ammunitionList ?? Enumerable.Empty<Ammunition>())
            .Where(a => a != null).Select(a => a._ap)
            .Concat(launcher._containers.Select(c => c._initialAmmunition).Where(a => a != null).Select(a => a._ap))
            .Concat(launcher._containers.SelectMany(c => c._weapons).Where(w => w != null).Select(w => w._ap))
            .Where(ap => ap != null)
            .Distinct();

    static IEnumerable<AmmunitionParameters> MagazineAmmo(WeaponSystem gun) =>
        (gun._vwp._associatedMagazine?._ammunitionList ?? Enumerable.Empty<Ammunition>())
            .Where(a => a != null).Select(a => a._ap).Where(ap => ap != null).Distinct();
}
