using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SeaPower;
using Mathf = UnityEngine.Mathf;

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
    // A gun with a burst count above 1 never reads the shell reload: it waits
    // the shot gap inside a burst and the burst gap between them, and the
    // burst gap is most of its cycle (an AK-726's is 1.22 s to the shot gap's 0.11).
    static readonly AccessTools.FieldRef<WeaponSystemGun, float> ShotGap = AccessTools.FieldRefAccess<WeaponSystemGun, float>("_delayBetweenShotsinBurst");
    static readonly AccessTools.FieldRef<WeaponSystemGun, float> BurstGap = AccessTools.FieldRefAccess<WeaponSystemGun, float>("_burstDelay");
    static readonly AccessTools.FieldRef<WeaponSystemGun, int> BurstCount = AccessTools.FieldRefAccess<WeaponSystemGun, int>("_burstCount");
    static readonly AccessTools.FieldRef<WeaponSystemChaff, double> ChaffReload = AccessTools.FieldRefAccess<WeaponSystemChaff, double>("_reloadTime");
    /// <summary>A missile's own drag factor; below zero, the game works one out from its range and caches it by ammo name.</summary>
    static readonly AccessTools.FieldRef<AmmunitionParameters, float> DragCoefficient = AccessTools.FieldRefAccess<AmmunitionParameters, float>("_dragCoefficient");
    /// <summary>The game's pity rule: after this many player decoy successes in a row, the next is forced to fail.</summary>
    const int StockSpoofLimit = 3;
    /// <summary>A percentage; the CIWS hit model's calibration needs it below 100.</summary>
    const float MaxCiwsIntercept = 99f;

    // What the unlocks multiply, on top of the ranks.
    const float HypersonicFactor = 2.5f, LongReachFactor = 2.5f;
    const float OverdriveFactor = 2f;
    const float OverchargeReloadFactor = 4f;
    const float LongShotsFactor = 1.5f, HeavyShellsFactor = 2f;
    /// <summary>Shell speed is the rate of fire's cube root: ×1.4 fire is ×1.12 speed, ×2.75 is ×1.4.</summary>
    static float ShellSpeedFactor(float rateOfFire) => Mathf.Pow(rateOfFire, 1f / 3f);
    const float GhostRcsFactor = 0.1f, ArmouredHealthFactor = 4f;
    /// <summary>The most a seeker can take: SeekerBase.CheckCountermeasureLock fails every contact above this. The game still leaves a 1% spoof chance.</summary>
    const float MaxAntiChaff = 0.999f;
    const float KeelBreakerFactor = 3f, SupercavitatingFactor = 2f;
    /// <summary>Hunter: sonar range and arc ×2, and +20 dB of gain to pick a ship out of the sea's noise.</summary>
    const float HunterSeekerFactor = 2f, HunterGainDb = 20f;
    const float WaveTopFeet = 10f, MaxSeekerArc = 360f;
    /// <summary>What the game sets a wake-homing torpedo's turn rate to on its final run.</summary>
    const float WakeTerminalTurnRate = 20f;
    const float BombardmentFactor = 2f;
    const float UnityPerMile = 27.559496f;
    /// <summary>Below this an anti-ship missile's minimum range rarely matters, and the card leaves it out.</summary>
    const float MinimumWorthShowingNm = 5f;

    /// <summary>Each torpedo clone's own speed settings, as the game first gave them.</summary>
    static readonly ConditionalWeakTable<AmmunitionParameters, List<UnityEngine.Vector2>> stockSpeedSettings =
        new ConditionalWeakTable<AmmunitionParameters, List<UnityEngine.Vector2>>();

    static float ReactionFactor(Run run, int ahead = 0) => Upgrades.Total(run, Upgrades.Reaction, Stat.Reaction, ahead);
    static float AimErrorFactor(Run run, int ahead = 0) => Upgrades.Total(run, Upgrades.Accuracy, Stat.Accuracy, ahead);
    static readonly AccessTools.FieldRef<WeaponSystemCIWS, float> CiwsCalibration = AccessTools.FieldRefAccess<WeaponSystemCIWS, float>("_sigmaFcsMissileMrad");

    /// <summary>Remember a field's original value the first time it is touched, and return it.</summary>
    static float Base(object owner, string field, float current)
    {
        var map = bases.GetOrCreateValue(owner);
        if (!map.TryGetValue(field, out float original)) map[field] = original = current;
        return original;
    }

    internal static void ApplyFleet(Run run) => ApplyTo(run, run.Fleet);

    internal static void ApplyTo(Run run, IEnumerable<ObjectBase> units)
    {
        foreach (var ship in units.Where(s => s != null && !s.IsDestroyed))
        {
            try { Apply(run, ship); }
            catch (Exception e) { Entry.Log.LogError($"Applying upgrades to {ship.Name.Value} failed: {e}"); }
        }
    }

    static void Apply(Run run, ObjectBase ship)
    {
        float gunRof = Upgrades.Total(run, Upgrades.Guns, Stat.RateOfFire);
        float gunDamage = Upgrades.Total(run, Upgrades.Guns, Stat.Damage) * (Upgrades.Has(run, Upgrades.HeavyShells) ? HeavyShellsFactor : 1f);
        float gunRange = (Upgrades.Has(run, Upgrades.LongShots) ? LongShotsFactor : 1f) * Ensigns.GunReach;
        // Shell speed follows the line's ranks alone: the Stars and Stripes slows the gun, not its shell.
        float shellSpeed = ShellSpeedFactor(gunRof);
        gunRof *= Ensigns.GunRate;
        foreach (var gun in Guns(ship))
        {
            ShellReload(gun) = Base(gun, "shellReload", ShellReload(gun)) / gunRof;
            ShotGap(gun) = Base(gun, "shotGap", ShotGap(gun)) / gunRof;
            BurstGap(gun) = Base(gun, "burstGap", BurstGap(gun)) / gunRof;
            // The ready rack's refill from the magazine, for guns that have one.
            gun._vwp._magazineReloadTime = Base(gun, "reload", gun._vwp._magazineReloadTime) / gunRof;
            foreach (var ap in MagazineAmmo(gun))
            {
                ap._warheadPower = Base(ap, "power", ap._warheadPower) * gunDamage;
                // Faster shells and Long shots. The fitted gravity sets how
                // far a shell can reach (v0²/g): it grows with the speed, so
                // the reach stays put, and shrinks as the range grows. The
                // fire table is rebuilt to match in OwnFireTable.
                ap._muzzleVelocityInUnity = Base(ap, "muzzleVelocity", ap._muzzleVelocityInUnity) * shellSpeed;
                ap._muzzleVelocityInMeterPerSecond = Base(ap, "muzzleVelocityMs", ap._muzzleVelocityInMeterPerSecond) * shellSpeed;
                ap._launchRangesInUnity.y = Base(ap, "rangeUnity", ap._launchRangesInUnity.y) * gunRange;
                ap._maxLaunchRangeInMiles = Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles) * gunRange;
                ap._gravity = Base(ap, "gravity", ap._gravity) * shellSpeed * shellSpeed / gunRange;
                ApplyFlak(run, ap);
            }
        }

        float samReload = Upgrades.Total(run, Upgrades.Sams, Stat.Reload);
        float samKill = Upgrades.Total(run, Upgrades.Sams, Stat.KillChance);
        int samChannels = (int)Upgrades.Total(run, Upgrades.Sams, Stat.Channels);
        bool seaSkimmers = Upgrades.Has(run, Upgrades.SeaSkimmerKillers);
        float samSpeed = (Upgrades.Has(run, Upgrades.Hypersonic) ? HypersonicFactor : 1f) * Ensigns.MissileSpeed;
        float samRange = (Upgrades.Has(run, Upgrades.LongReach) ? LongReachFactor : 1f) * Ensigns.MissileReach;
        foreach (var director in SamDirectors(ship))
        {
            director._targetChannels = (int)Base(director, "targets", director._targetChannels) + samChannels;
            director._weaponChannels = (int)Base(director, "weapons", director._weaponChannels) + samChannels;
        }
        foreach (var launcher in SamLaunchers(ship))
            launcher._vwp._magazineReloadTime = Base(launcher, "reload", launcher._vwp._magazineReloadTime) * samReload;
        // A fighter's air-to-air missiles are SAMs for this line, every rank that reaches the round.
        foreach (var ap in SamLaunchers(ship).SelectMany(LauncherAmmo).Where(AirToAir).Concat(PylonAmmo(ship).Where(AirToAir)))
        {
            // Not capped: the game multiplies it down for hard targets, so points past 100% still land.
            ap._killProbability = Base(ap, "kill", ap._killProbability) + samKill;
            // First, while the range is still the one the game calibrates drag from.
            ApplyMissileSpeed(ap, samSpeed, ship.IsAirUnit);

            // Sea-skimmer killers: a floor of zero passes the engage check
            // for anything above the waves, and no penalty for flying low.
            ap._attackAltitudesInUnity.x = seaSkimmers ? 0f : Base(ap, "floor", ap._attackAltitudesInUnity.x);
            ap._outOfAltitudePenalty = seaSkimmers ? 0f : Base(ap, "altitudePenalty", ap._outOfAltitudePenalty);
            ap._maxLaunchRangeInMiles = Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles) * samRange;
            ap._launchRangesInUnity.y = Base(ap, "rangeUnity", ap._launchRangesInUnity.y) * samRange;
            ap._maxFlightTime = Base(ap, "flightTime", ap._maxFlightTime) * samRange;
        }

        // Air wing: bombs and rockets. Missiles on the pylons go through the lines above.
        float strikeDamage = Upgrades.Total(run, Upgrades.AirWing, Stat.Damage);
        foreach (var ap in PylonAmmo(ship).Where(StrikeStore))
            ap._warheadPower = Base(ap, "power", ap._warheadPower) * strikeDamage;

        float asmDamage = Upgrades.Total(run, Upgrades.AntiShip, Stat.Damage);
        float asmSpeed = Upgrades.Total(run, Upgrades.AntiShip, Stat.Speed) * Ensigns.MissileSpeed;
        float asmRange = Upgrades.Total(run, Upgrades.AntiShip, Stat.Range) * Ensigns.MissileReach;
        float asmEvasion = Upgrades.Total(run, Upgrades.AntiShip, Stat.Evasion);
        bool smartSeekers = Upgrades.Has(run, Upgrades.SmartSeekers);
        bool waveTop = Upgrades.Has(run, Upgrades.WaveTop);
        bool armoured = Upgrades.Has(run, Upgrades.Armoured);
        float asmRcs = Upgrades.Has(run, Upgrades.Ghost) ? GhostRcsFactor : 1f;
        foreach (var ap in AntiShipLaunchers(ship).SelectMany(LauncherAmmo).Concat(PylonAmmo(ship)).Where(AntiShipMissile).Distinct())
        {
            ApplyMissileSpeed(ap, asmSpeed, ship.IsAirUnit);
            ap._warheadPower = Base(ap, "power", ap._warheadPower) * asmDamage;
            ap._maxLaunchRangeInMiles = Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles) * asmRange;
            ap._launchRangesInUnity.y = Base(ap, "rangeUnity", ap._launchRangesInUnity.y) * asmRange;
            // The launcher reads the first of these and the seeker's go-active
            // delay the second, so a shot inside the old minimum needs both.
            ap._minLaunchRangeInMiles = Base(ap, "minRangeMiles", ap._minLaunchRangeInMiles) / asmRange;
            ap._launchRangesInUnity.x = Base(ap, "minRangeUnity", ap._launchRangesInUnity.x) / asmRange;
            // Faster and farther both need longer to get there.
            ap._maxFlightTime = Base(ap, "flightTime", ap._maxFlightTime) * asmRange;
            ap._maxRCS = Base(ap, "rcs", ap._maxRCS) * asmRcs;
            // Past 1 the SAM's chance only goes negative, and the game's floor catches it.
            ap._samDefenceBonus = Mathf.Min(1f, Base(ap, "samDefence", ap._samDefenceBonus) + asmEvasion);

            // Smart seekers: both bonuses are taken off the decoy's or jammer's chance.
            // Above 0.999 the jammer gives up, but the seeker rejects every
            // contact, ships included, and never locks: chaff stops at the limit.
            ap._antiChaffBonus = smartSeekers ? MaxAntiChaff : Base(ap, "antiChaff", ap._antiChaffBonus);
            ap._antiJammerBonus = smartSeekers ? 1f : Base(ap, "antiJam", ap._antiJammerBonus);

            ApplyWaveTop(ap, waveTop);

            // Armoured. Health below zero means the CIWS works it out from dry mass.
            float health = Base(ap, "ciwsHealth", ap._ciwsTargetHealth);
            if (armoured && health < 0f) health = CiwsBallistics.MassScaledMissileHealth(ap._massInKg - ap._fuelMassInKg);
            ap._ciwsTargetHealth = armoured ? health * ArmouredHealthFactor : health;
        }

        float torpedoSpeed = Upgrades.Total(run, Upgrades.Torpedoes, Stat.Speed) *
                             (Upgrades.Has(run, Upgrades.Supercavitating) ? SupercavitatingFactor : 1f) * Ensigns.TorpedoSpeed;
        float torpedoRange = Upgrades.Total(run, Upgrades.Torpedoes, Stat.Range) * Ensigns.TorpedoReach;
        float torpedoDamage = Upgrades.Total(run, Upgrades.Torpedoes, Stat.Damage) *
                              (Upgrades.Has(run, Upgrades.KeelBreaker) ? KeelBreakerFactor : 1f);
        bool hunter = Upgrades.Has(run, Upgrades.Hunter);
        float torpedoSeeker = Upgrades.Total(run, Upgrades.Torpedoes, Stat.Seeker) * (hunter ? HunterSeekerFactor : 1f);
        bool sureShot = Upgrades.Has(run, Upgrades.SureShot);
        bool antiSurface = run.RankOf(Upgrades.Torpedoes) > 0 || Ensigns.TorpedoesTargetShips;
        // ASROC, VLA, SS-N-14 and SS-N-16 are anti-submarine only too, so
        // without this they never fire at all.
        foreach (var ap in TorpedoCarriers(ship))
        {
            AllowShips(ap, antiSurface);
            // An SS-N-14B is an anti-ship missile first, and that line boosts it.
            if (!AntiShipMissile(ap)) ApplyCarrierFlight(ap, torpedoSpeed, torpedoRange, ship.IsAirUnit);
        }
        foreach (var ap in ShipTorpedoes(ship))
        {
            AllowShips(ap, antiSurface);

            ap._warheadPower = Base(ap, "power", ap._warheadPower) * torpedoDamage;

            // Speed and range are a table of settings, (knots, range) at
            // each; the AI and the torpedo's fuel both read it. The list
            // is shared with the master copy, so it is replaced, never edited.
            if (!stockSpeedSettings.TryGetValue(ap, out var stock))
                stockSpeedSettings.Add(ap, stock = new List<UnityEngine.Vector2>(ap._torpedoSpeedSettings));
            ap._torpedoSpeedSettings = stock.Select(s => new UnityEngine.Vector2(s.x * torpedoSpeed, s.y * torpedoRange)).ToList();
            ap._maxVelocityInKnots = Base(ap, "speed", ap._maxVelocityInKnots) * torpedoSpeed;
            ap._terminalVelocityInKnots = Base(ap, "terminalSpeed", ap._terminalVelocityInKnots) * torpedoSpeed;
            ap._searchVelocityInKnots = Base(ap, "searchSpeed", ap._searchVelocityInKnots) * torpedoSpeed;
            // So it reaches its new speed in the same time.
            ap._acceleration = Base(ap, "acceleration", ap._acceleration) * torpedoSpeed;
            KeepTurningCircle(ap, torpedoSpeed);
            // A circling reattack turns at a fixed rate, so it too grew wider with speed.
            ap._reattackCircleDegreesPerSecond = Base(ap, "reattackTurnRate", ap._reattackCircleDegreesPerSecond) * torpedoSpeed;
            // Without a reattack mode a torpedo that loses its target runs
            // straight on until its fuel is gone.
            ap._reattackMode = hunter
                ? AmmunitionParameters.ReattackMode.Circle
                : (AmmunitionParameters.ReattackMode)(int)Base(ap, "reattackMode", (float)ap._reattackMode);
            // A wire-guided torpedo is held to this while on the wire;
            // unset, it is float.MaxValue.
            float wire = Base(ap, "wireSpeed", ap._maxSpeedOnWireInKnots);
            ap._maxSpeedOnWireInKnots = wire < float.MaxValue ? wire * torpedoSpeed : wire;
            if (Ensigns.Active(Ensigns.WireGuided)) ApplyWire(ship, ap);
            ap._maxLaunchRangeInMiles = Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles) * torpedoRange;
            ap._launchRangesInUnity.y = Base(ap, "rangeUnity", ap._launchRangesInUnity.y) * torpedoRange;
            ap._horizonRangesInUnity.x = Base(ap, "horizonNear", ap._horizonRangesInUnity.x) * torpedoRange;
            ap._horizonRangesInUnity.y = Base(ap, "horizonFar", ap._horizonRangesInUnity.y) * torpedoRange;

            ap._seekerActiveRange = Base(ap, "seekerActive", ap._seekerActiveRange) * torpedoSeeker;
            ap._seekerPassiveRange = Base(ap, "seekerPassive", ap._seekerPassiveRange) * torpedoSeeker;
            ap._seekerFOV = Mathf.Min(MaxSeekerArc, Base(ap, "seekerArc", ap._seekerFOV) * torpedoSeeker);
            // Every launch sets the arc again from one of these, the narrow
            // one when fired at a target and the wide one when fired blind,
            // so without them the arc ranks are lost the moment it fires.
            ap._narrowSeekerFOV = Mathf.Min(MaxSeekerArc, Base(ap, "seekerArcNarrow", ap._narrowSeekerFOV) * torpedoSeeker);
            ap._wideSeekerFOV = Mathf.Min(MaxSeekerArc, Base(ap, "seekerArcWide", ap._wideSeekerFOV) * torpedoSeeker);
            // Range alone is not enough: an echo must also beat the sea's
            // noise, and the seeker's gain is what it has against it.
            ap._seekerGain = Base(ap, "seekerGain", ap._seekerGain) + (hunter ? HunterGainDb : 0f);
            ap._gainFactor = Mathf.Pow(10f, ap._seekerGain / 10f);
            // Earlier builds switched this to wake homing; put the stock seeker back.
            ap._guidanceType = (AmmunitionParameters.GuidanceType)(int)Base(ap, "guidance", (float)ap._guidanceType);

            // Spiral search only ever sees contacts inside its depth band, so it
            // can never see a ship. Snake is what the ship-hunting torpedoes use.
            var stockSearch = (AmmunitionParameters.SearchMode)(int)Base(ap, "searchMode", (float)ap.SearchModeSetting.Value);
            ap.SearchModeSetting.Value = CanTakeShips(ap) && stockSearch == AmmunitionParameters.SearchMode.Spiral
                ? AmmunitionParameters.SearchMode.Snake
                : stockSearch;

            if (ap._guidanceType == AmmunitionParameters.GuidanceType.WakeHoming)
            {
                // Each wake crossing holds the turn for a fixed time, and the seeker
                // reaches 0.25 nm at most: a fast torpedo ran out of its reach and lost the wake.
                ap._turnDelay = Base(ap, "turnDelay", ap._turnDelay) / torpedoSpeed;
                // The game sets this on the final run; see KeepWakeTurnRate.
                ap._maxTurnRateDegrees = WakeTerminalTurnRate * torpedoSpeed;
            }

            ap._seekerReliability = sureShot ? 1f : Base(ap, "seekerReliability", ap._seekerReliability);
            ap._warheadReliability = sureShot ? 1f : Base(ap, "warheadReliability", ap._warheadReliability);
        }

        float mortarReload = Upgrades.Total(run, Upgrades.Mortars, Stat.Reload);
        foreach (var launcher in MortarLaunchers(ship))
        {
            launcher._vwp._magazineReloadTime = Base(launcher, "reload", launcher._vwp._magazineReloadTime) * mortarReload;
            Mortars.SetLoadSpeed(launcher, 1f / mortarReload);
            foreach (var ap in LauncherAmmo(launcher).Where(Mortar))
                ApplyMortar(run, ap);
        }

        float ciwsIntercept = Upgrades.Total(run, Upgrades.PointDefence, Stat.Intercept);
        float ciwsRof = Upgrades.Total(run, Upgrades.PointDefence, Stat.RateOfFire) *
                        (Upgrades.Has(run, Upgrades.Overdrive) ? OverdriveFactor : 1f);
        foreach (var ciws in ship._obp._ciwsWeapons)
        {
            float intercept = Mathf.Min(MaxCiwsIntercept, Base(ciws, "intercept", ciws._missileInterceptChance) + ciwsIntercept);
            int rof = (int)(Base(ciws, "rof", ciws._fireRatePerMinute) * ciwsRof);
            // Heavy rounds go on at the refit, so a newly unlocked one needs one too.
            bool heavy = Upgrades.Has(run, Upgrades.HeavyRounds);
            if (intercept != ciws._missileInterceptChance || rof != ciws._fireRatePerMinute || heavy != Base(ciws, "heavy", 0f) > 0f)
            {
                ciws._missileInterceptChance = intercept;
                ciws._fireRatePerMinute = rof;
                bases.GetOrCreateValue(ciws)["heavy"] = heavy ? 1f : 0f;
                // The hit model is calibrated once against the intercept
                // chance; clearing it makes the CIWS refit to the new values.
                CiwsCalibration(ciws) = -1f;
            }
            ApplyCiwsReach(ciws);
        }

        float spoof = Upgrades.Total(run, Upgrades.Countermeasures, Stat.Spoof);
        float chaffReload = Upgrades.Total(run, Upgrades.Countermeasures, Stat.Reload) /
                            (Upgrades.Has(run, Upgrades.Overcharge) ? OverchargeReloadFactor : 1f);
        foreach (var chaff in ship._obp._chaffWeapons)
        {
            ChaffReload(chaff) = Base(chaff, "reload", (float)ChaffReload(chaff)) * chaffReload;
            foreach (var ap in MagazineAmmo(chaff).Append(chaff._ammoInUse?._ap).Where(a => a != null).Distinct())
                // Not capped either: the game subtracts the missile's anti-chaff bonus from it.
                ap._decoySpoofChance = Base(ap, "spoof", ap._decoySpoofChance) + spoof;
        }
        // Global, but only ever consulted for the player's own decoys.
        Globals._playerMissilesSpoofedLimit = Upgrades.Has(run, Upgrades.Countermeasures, Stat.DecoyRule) ? int.MaxValue : StockSpoofLimit;

        // Ripple fire: no random wait before engaging, no pause before launch,
        // and no gap between launches from a shared magazine.
        bool ripple = Upgrades.Has(run, Upgrades.RippleFire);
        foreach (var launcher in ship._obp._launcherWeapons)
        {
            launcher._vwp._maxReactiontime = ripple ? 0f : Base(launcher._vwp, "reaction", launcher._vwp._maxReactiontime);
            launcher._vwp._preLaunchDelay = ripple ? 0f : Base(launcher._vwp, "preLaunch", launcher._vwp._preLaunchDelay);
            if (ripple) launcher._engageDelay = 0f;
        }
        var shared = ship._sharedLaunchIntervals;
        if (shared != null)
            foreach (var key in shared.Keys.ToList())
                shared[key] = ripple ? 0f : Base(shared, key, shared[key]);

        var crew = ship._crew;
        if (crew != null)
        {
            float reaction = ReactionFactor(run) * Ensigns.CrewFactor;
            crew._newContactProcessTime = Base(crew, "contact", crew._newContactProcessTime) * reaction;
            crew._endAlignmentDelay = Base(crew, "align", crew._endAlignmentDelay) * reaction;
            crew._autoEngageCheckMultiplier = Base(crew, "engageCheck", crew._autoEngageCheckMultiplier) * reaction;
            crew._targetAquisitionSpeed = Base(crew, "acquire", crew._targetAquisitionSpeed) / reaction;
            crew._gunTargetAquisitionSpeed = Base(crew, "gunAcquire", crew._gunTargetAquisitionSpeed) / reaction;

            crew._fcSolutionRateMultiplier = Base(crew, "fcRate", crew._fcSolutionRateMultiplier) / reaction;

            float accuracy = AimErrorFactor(run) * Ensigns.CrewFactor;
            crew._fcErrorMultiplier = Base(crew, "fcError", crew._fcErrorMultiplier) * accuracy;
        }

        if (ship is Vessel vessel) ApplyVesselSpeed(vessel, Ensigns.FleetSpeed);
        if (ship is Aircraft aircraft) ApplyAirWing(aircraft);
    }

    static readonly AccessTools.FieldRef<WeaponSystemCIWS, float> CiwsRangeSquared = AccessTools.FieldRefAccess<WeaponSystemCIWS, float>("_maxRangeSq");
    const float StallMargin = 1.2f, SlowAircraftFactor = 0.5f, StealthRcsFactor = 0.05f;
    const float EnemyMissileSpeed = 2f, EnemyMissileReach = 1.5f, EnemyShipSpeed = 0.5f;
    /// <summary>More than any torpedo launcher's own wires.</summary>
    const int ManyWires = 8;
    /// <summary>A speed no launching ship reaches, so steering at flank never breaks the wire.</summary>
    const float NoWireBreakKnots = 999f;

    /// <summary>
    /// The Dutch ensign. A CIWS works out its reach once from its round's range
    /// and fits its hit model to it, so both are cleared to be done again.
    /// </summary>
    static void ApplyCiwsReach(WeaponSystemCIWS ciws)
    {
        float reach = Ensigns.CiwsReach;
        foreach (var ap in MagazineAmmo(ciws))
        {
            float range = Base(ap, "rangeUnity", ap._launchRangesInUnity.y) * reach;
            if (Mathf.Approximately(ap._launchRangesInUnity.y, range)) continue;
            ap._launchRangesInUnity.y = range;
            ap._maxLaunchRangeInMiles = Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles) * reach;
            CiwsRangeSquared(ciws) = -1f;
            CiwsCalibration(ciws) = -1f;
        }
    }

    /// <summary>
    /// The Swedish ensign: the torpedo is steered down a wire, and held to
    /// two-thirds speed while on it. The ship gets enough wires that one is
    /// always free, and a wire no longer breaks at the ship's own speed.
    /// EnsignCombat cuts it at half the range.
    /// </summary>
    static void ApplyWire(ObjectBase ship, AmmunitionParameters ap)
    {
        ap._midCourseCorrection = AmmunitionParameters.MidCourseCorrection.WireGuided;
        ap._wireBreakSpeedInKnots = NoWireBreakKnots;
        ap._maxSpeedOnWireInKnots = ap._maxVelocityInKnots * Ensigns.WireSpeedFraction;
        ship._maxWireCount = Mathf.Max(ship._maxWireCount, ManyWires);
    }

    /// <summary>
    /// A ship's top speed and how hard it turns. The speed limit is read from the
    /// telegraph table, and the top speed also sets how far a rudder slows it.
    /// A ship's parameters are its own, loaded for it as it is made.
    /// </summary>
    static void ApplyVesselSpeed(Vessel vessel, float factor)
    {
        var vp = vessel.VP;
        vp._maxForwardVelocityInKnots = Base(vp, "maxForward", vp._maxForwardVelocityInKnots) * factor;
        vp._rudderForce = Base(vp, "rudderForce", vp._rudderForce) * factor;
        vp._rudderTurnRate = Base(vp, "rudderTurn", vp._rudderTurnRate) * factor;
        vp._maxAccelerationFactor = Base(vp, "acceleration", vp._maxAccelerationFactor) * factor;
        for (int i = 0; i < vp._telegraphVelocities.Length; i++)
            vp._telegraphVelocities[i] = Base(vp, "telegraph" + i, vp._telegraphVelocities[i]) * factor;
    }

    /// <summary>The Estonian ensign. A jet held below stall falls out of the sky, so speed stops a little above it.</summary>
    static void ApplyAirWing(Aircraft aircraft)
    {
        var data = aircraft._sensorData;
        if (data != null)
        {
            float rcs = Ensigns.Active(Ensigns.StealthAir) ? StealthRcsFactor : 1f;
            data._maxRCS = Base(data, "maxRcs", data._maxRCS) * rcs;
            data._minRCS = Base(data, "minRcs", data._minRCS) * rcs;
        }
        var ap = aircraft.Ap;
        float slow = Ensigns.Active(Ensigns.StealthAir) ? SlowAircraftFactor : 1f;
        float seaLevel = Base(ap, "maxSpeedSeaLevel", ap._maxSpeedSeaLevel);
        float floor = Mathf.Min(seaLevel, ap._stallSpeed * StallMargin);
        ap._maxSpeedSeaLevel = Mathf.Max(floor, seaLevel * slow);
        ap._machLimit = Base(ap, "machLimit", ap._machLimit) * slow;
    }

    /// <summary>
    /// What ensigns do to an enemy as it is made, before its reach and spawn
    /// distance are worked out from its weapons, so those allow for it.
    /// </summary>
    internal static void ApplyEnsignsToEnemy(ObjectBase unit)
    {
        try
        {
            if (Ensigns.Active(Ensigns.RareCards))
            {
                var missiles = unit._obp._launcherWeapons.SelectMany(LauncherAmmo).Concat(PylonAmmo(unit))
                    .Where(ap => ap._type == Ammunition.Type.Missile).Distinct();
                foreach (var ap in missiles)
                {
                    ApplyMissileSpeed(ap, EnemyMissileSpeed, unit.IsAirUnit);
                    ap._maxLaunchRangeInMiles = Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles) * EnemyMissileReach;
                    ap._launchRangesInUnity.y = Base(ap, "rangeUnity", ap._launchRangesInUnity.y) * EnemyMissileReach;
                    ap._maxFlightTime = Base(ap, "flightTime", ap._maxFlightTime) * EnemyMissileReach;
                }
            }
            if (unit is Vessel vessel && Ensigns.Active(Ensigns.SlowEnemies)) ApplyVesselSpeed(vessel, EnemyShipSpeed);
        }
        catch (Exception e) { Entry.Log.LogError($"Applying ensigns to {unit.Name.Value} failed: {e}"); }
    }

    /// <summary>
    /// The numbers on one mortar round. Its flight needs no more than this to
    /// reach: the game fits a round's gravity to the range and the speed, and
    /// its launcher reads the same range. The launch phase shrinks with the
    /// speed, or a fast round would fly a full second past a close target, and
    /// a round has no thrust after it, so its acceleration goes up by the
    /// speed twice: once for the higher top speed, once for the shorter time.
    /// Its saturation value goes to zero with the line: see Mortars.
    /// </summary>
    internal static void ApplyMortar(Run run, AmmunitionParameters ap)
    {
        float speed = Upgrades.Total(run, Upgrades.Mortars, Stat.Speed);
        float reach = Upgrades.Total(run, Upgrades.Mortars, Stat.Reach);
        float factor = (Upgrades.Has(run, Upgrades.Bombardment) ? BombardmentFactor : 1f) * Ensigns.MortarReach;
        ap._warheadPower = Base(ap, "power", ap._warheadPower) * Upgrades.Total(run, Upgrades.Mortars, Stat.Damage);
        ap._maxVelocityInKnots = Base(ap, "speed", ap._maxVelocityInKnots) * speed;
        ap._acceleration = Base(ap, "acceleration", ap._acceleration) * speed * speed;
        ap._initialFlightPhaseDuration = Base(ap, "armedAfter", ap._initialFlightPhaseDuration) / speed;
        ap._maxLaunchRangeInMiles = (Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles) + reach) * factor;
        ap._launchRangesInUnity.y = (Base(ap, "rangeUnity", ap._launchRangesInUnity.y) + reach * UnityPerMile) * factor;
        float saturation = Base(ap, "saturation", ap._saturationValue);
        ap._saturationValue = run.RankOf(Upgrades.Mortars) > 0 ? 0f : saturation;
    }

    /// <summary>
    /// The flight of a rocket or missile that carries a torpedo, on the
    /// Torpedoes line's speed and range. Two flight models:
    /// <list type="bullet">
    /// <item>Ballistic (ASROC, VLA, SS-N-16): a boost, then an arc whose
    /// gravity the game fits to the distance and speed, as with a mortar. The
    /// boost shrinks with the speed, or a fast round would overfly a close
    /// target during it, so acceleration goes up by the speed twice.</item>
    /// <item>Missile (SS-N-14A, Ikara): flies as any other missile.</item>
    /// </list>
    /// </summary>
    static void ApplyCarrierFlight(AmmunitionParameters ap, float speed, float range, bool airLaunched)
    {
        if (ap._type == Ammunition.Type.ASROC)
        {
            ap._maxVelocityInKnots = Base(ap, "speed", ap._maxVelocityInKnots) * speed;
            ap._acceleration = Base(ap, "acceleration", ap._acceleration) * speed * (ap._waterEntryLaunch ? 1f : speed);
            // An SS-N-16 runs underwater for this phase, and a shorter one could end
            // before it turns for the surface, so it keeps the stock boost.
            if (!ap._waterEntryLaunch) ap._initialFlightPhaseDuration = Base(ap, "armedAfter", ap._initialFlightPhaseDuration) / speed;
        }
        else
        {
            // First, while the range is still the one the game calibrates drag from.
            ApplyMissileSpeed(ap, speed, airLaunched);
            ap._maxFlightTime = Base(ap, "flightTime", ap._maxFlightTime) * range;
        }
        ap._maxLaunchRangeInMiles = Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles) * range;
        ap._launchRangesInUnity.y = Base(ap, "rangeUnity", ap._launchRangesInUnity.y) * range;
    }

    /// <summary>
    /// The first rank gives ships as the second target type, as the Mk 46
    /// Mod 5 has. The Mk 44, the early Mk 46 and every Soviet ship torpedo
    /// are anti-submarine only, so a Gearing's or a Kirov's tubes would never
    /// fire, and the enemy has no submarines.
    /// </summary>
    static void AllowShips(AmmunitionParameters ap, bool on)
    {
        var secondary = (Ammunition.Target)(int)Base(ap, "secondaryTarget", (float)ap._secondaryTargetType);
        bool hadSecondary = Base(ap, "hasSecondary", ap._hasSecondaryTargetType ? 1f : 0f) > 0f;
        bool surface = on && AntiSubmarineOnly(ap._targetType, hadSecondary);
        ap._secondaryTargetType = surface ? Ammunition.Target.ASuW : secondary;
        ap._hasSecondaryTargetType = surface || hadSecondary;
    }

    /// <summary>
    /// The torpedo a carrier of ours drops. The game drops the one its pooled
    /// weapon was built with, whose parameters every carrier of that kind
    /// shares, the enemy's included, so ours each get their own clone to
    /// boost, swapped in as the torpedo is dropped.
    /// </summary>
    static readonly ConditionalWeakTable<AmmunitionParameters, AmmunitionParameters> payloads =
        new ConditionalWeakTable<AmmunitionParameters, AmmunitionParameters>();

    static AmmunitionParameters Payload(AmmunitionParameters carrier) =>
        payloads.GetValue(carrier, c => AmmunitionParameters.Create(c._submunitionFileName, 0, null));

    [HarmonyPatch(typeof(Torpedo), nameof(Torpedo.ASROC_Launch))]
    static class DropOurPayload
    {
        static void Prefix(WeaponBase parentWeapon, ref AmmunitionParameters ap)
        {
            if (parentWeapon?._ap != null && payloads.TryGetValue(parentWeapon._ap, out var ours)) ap = ours;
        }
    }

    /// <summary>
    /// A torpedo of ours launched without a target object hunts ships. The
    /// game locks a blind torpedo that can take both submarines and ships to
    /// submarines alone: its seeker then passes over every surface contact,
    /// and it is held below 30 ft. That is every torpedo the Torpedoes line
    /// lets fire at ships, dropped by a missile whose target was sunk or lost
    /// on the way, or fired down a bearing. The enemy has no submarines.
    /// </summary>
    [HarmonyPatch(typeof(Torpedo), "TorpedoLaunchSettings")]
    static class HuntShipsBlind
    {
        static void Postfix(Torpedo __instance)
        {
            var platform = __instance._launchPlatform;
            if (__instance._lockedTargetPosture != Torpedo.TargetPosture.Subsurface ||
                platform == null || platform._taskforce != Globals._playerTaskforce) return;
            var ap = __instance._ap;
            if (ap._targetType != Ammunition.Target.ASuW && ap._secondaryTargetType != Ammunition.Target.ASuW) return;
            var target = __instance.CurrentIntendedTargetObject;
            if (target == null || !target.isSubmerged())
                __instance._lockedTargetPosture = Torpedo.TargetPosture.Surface;
        }
    }

    /// <summary>
    /// Hunter: our sonar torpedoes never take a noisemaker or decoy as a
    /// contact, so they cannot be lured off the ship. Each seeker builds its
    /// list of contacts afresh every update; this takes the decoys out.
    /// </summary>
    [HarmonyPatch]
    static class HunterIgnoresDecoys
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            new[] { typeof(ActiveSonarSeeker), typeof(PassiveSonarSeeker), typeof(ActivePassiveSonarSeeker) }
                .Select(t => (System.Reflection.MethodBase)AccessTools.Method(t, "createListOfPotentialContacts"));

        static readonly AccessTools.FieldRef<SeekerBase, WeaponBase> AmmoObject =
            AccessTools.FieldRefAccess<SeekerBase, WeaponBase>("_ammoObject");

        static void Postfix(SeekerBase __instance)
        {
            if (!(AmmoObject(__instance) is Torpedo torpedo) || !TorpedoIsOurs(torpedo)) return;
            var run = Run.Current;
            if (run == null || !Upgrades.Has(run, Upgrades.Hunter)) return;
            __instance._potentialContacts.RemoveAll(o => o is Noisemaker || o != null && o._type == ObjectBase.ObjectType.Decoy);
        }
    }

    static bool CanTakeShips(AmmunitionParameters ap) =>
        ap._targetType == Ammunition.Target.ASuW || ap._secondaryTargetType == Ammunition.Target.ASuW;

    /// <summary>
    /// An active-sonar seeker only takes submerged contacts, whatever its torpedo
    /// may be fired at: a SET-65 or Mk 44 let loose on a ship never locked it and
    /// searched until its fuel ran out. Ours hear ships as the active-passive ones do.
    /// </summary>
    [HarmonyPatch(typeof(SeekerBase), "isDetectable")]
    static class ActiveSonarHearsShips
    {
        static readonly AccessTools.FieldRef<SeekerBase, WeaponBase> AmmoObject =
            AccessTools.FieldRefAccess<SeekerBase, WeaponBase>("_ammoObject");

        static void Postfix(SeekerBase __instance, ObjectBase target, ref bool __result)
        {
            if (__result || __instance._guidanceType != AmmunitionParameters.GuidanceType.ActiveSonarHoming ||
                target == null || target._type != ObjectBase.ObjectType.Vessel) return;
            if (!(AmmoObject(__instance) is Torpedo torpedo) || !TorpedoIsOurs(torpedo) || !CanTakeShips(torpedo._ap)) return;
            __result = __instance.isInRange(target.getUnityPosition()) &&
                       Singleton<UnitSensorCache>.Instance.UnitsHaveDirectlLOS(torpedo, target,
                           (torpedo.transform.position - target.transform.position).magnitude);
        }
    }

    /// <summary>
    /// A ship can hold its own search mode for a torpedo, which overrides the
    /// one Apply sets. Spiral still cannot see a ship.
    /// </summary>
    [HarmonyPatch(typeof(Torpedo), "TorpedoLaunchSettings")]
    static class NoSpiralAtShips
    {
        static void Prefix(Torpedo __instance)
        {
            var platform = __instance._launchPlatform;
            if (platform == null || !TorpedoIsOurs(__instance) || !CanTakeShips(__instance._ap)) return;
            if (platform._searchModeSettings.TryGetValue(__instance._ap._ammunitionFileName, out var mode) &&
                mode.Value == AmmunitionParameters.SearchMode.Spiral)
                mode.Value = AmmunitionParameters.SearchMode.Snake;
        }
    }

    /// <summary>
    /// The game writes a flat 20°/s into a wake-homing torpedo's parameters as
    /// it starts its final run, which undid the turn rate that keeps a fast
    /// one's turning circle the same. Parameters are shared, so it stayed lost
    /// for every later shot too.
    /// </summary>
    [HarmonyPatch(typeof(SeaPower.Torpedo_States.TerminalApproach), "onEnter")]
    static class KeepWakeTurnRate
    {
        static void Prefix(Torpedo ___torpedo, out float __state) => __state = ___torpedo._ap._maxTurnRateDegrees;

        static void Postfix(Torpedo ___torpedo, float __state)
        {
            if (TorpedoIsOurs(___torpedo)) ___torpedo._ap._maxTurnRateDegrees = __state;
        }
    }

    static bool TorpedoIsOurs(Torpedo t) => t._launchPlatform != null && t._launchPlatform._taskforce == Globals._playerTaskforce;

    /// <summary>
    /// A torpedo-carrying missile of ours drops its torpedo on ships too. The
    /// game only drops it on a submerged target: at a ship an SS-N-14 flew in
    /// as a missile and hit with the warhead it doesn't have.
    /// </summary>
    [HarmonyPatch(typeof(AmmunitionParameters), nameof(AmmunitionParameters.IsTorpedoDeliveryShot))]
    static class DeliverToShips
    {
        static void Postfix(AmmunitionParameters __instance, ref bool __result)
        {
            if (!__result && TorpedoCarrier(__instance) && payloads.TryGetValue(__instance, out _)) __result = true;
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
        bool antiAir = Upgrades.Has(run, Upgrades.Guns, Stat.AntiAir);
        var fuze = (AmmunitionParameters.FuzeType)(int)Base(ap, "fuzeType", (float)ap._fuzeType);
        float gate = Base(ap, "fuzeGate", ap._fuzeGateSeconds);
        float radius = Base(ap, "fuzeRadius", ap._fuzeProximityDistance);
        float aaRange = Base(ap, "aaRange", ap._maxAARangeInUnity);
        // Without an AA range the game treats a gun as direct fire against
        // aircraft: it holds fire whenever the shot needs more than 10° of
        // loft, which rules out most of the sky. Only three shells in the
        // game have one (US 5"/38 and 40 mm, Soviet 57 mm); AA fire-control
        // tables give the rest one, as the 5"/38 has, at 60% of max range.
        ap._maxAARangeInUnity = antiAir && aaRange <= 0f ? AaRangeFraction * ap._launchRangesInUnity.y : aaRange;
        if (antiAir)
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
        ap._fuzeProximityDistance = radius * FuzeFactor(run);

        // Barrage fire: surface-only shells take aircraft as a second target type.
        var secondary = (Ammunition.Target)(int)Base(ap, "secondaryTarget", (float)ap._secondaryTargetType);
        bool hadSecondary = Base(ap, "hasSecondary", ap._hasSecondaryTargetType ? 1f : 0f) > 0f;
        bool barrage = antiAir &&
                       ap._targetType == Ammunition.Target.ASuW && secondary == Ammunition.Target.UNKNOWN;
        ap._secondaryTargetType = barrage ? Ammunition.Target.AAW : secondary;
        ap._hasSecondaryTargetType = barrage || hadSecondary;
    }

    const float MetresToUnity = 0.0148809375f;

    /// <summary>
    /// Faster without turning wider. The turn rate is in degrees a second, so
    /// the turning circle (speed ÷ rate) grew with every speed rank: at ×6.5 a
    /// Harpoon's was 7 km, and it could no longer make the last correction
    /// onto a ship once its seeker locked. Turn rate × speed keeps the stock
    /// circle; the g of that turn grows with speed², and so does the g limit,
    /// which would otherwise cut the rate back down. Everything else timed in
    /// seconds that shapes the path goes with them.
    /// </summary>
    static void KeepTurningCircle(AmmunitionParameters ap, float speed)
    {
        ap._maxTurnRateDegrees = Base(ap, "turnRate", ap._maxTurnRateDegrees) * speed;
        ap._toBearingTurnRateDegrees = Base(ap, "bearingTurnRate", ap._toBearingTurnRateDegrees) * speed;
        // Steers both pitch and course on the terminal run, and defaults to
        // the stock turn rate: left alone, a fast missile on the final
        // correction turned wide and flew into the sea or past the ship.
        float terminal = Base(ap, "terminalTurnRate", ap._terminalVerticalTurnRate);
        ap._terminalVerticalTurnRate = terminal > 0f ? terminal * speed : terminal;
        ap._maxTurnG = Base(ap, "turnG", ap._maxTurnG) * speed * speed;
        // Search patterns: a ladder turns at a fixed rate and a snake weaves
        // on a fixed period, so both grew wider with speed.
        ap._ladderTurnDegreesPerSecond = Base(ap, "ladderTurnRate", ap._ladderTurnDegreesPerSecond) * speed;
        ap._snakeSearchTurnCycleTime = Base(ap, "snakeCycle", ap._snakeSearchTurnCycleTime) / speed;
    }

    /// <summary>
    /// A missile flies this many times as fast all the way, not just with a
    /// higher limit. The game has two flight models:
    /// <list type="bullet">
    /// <item>Legacy (Harpoon, Soviet anti-ship missiles): the motor
    /// accelerates it up to its top speed and holds it there. More thrust
    /// gets it there in the stock time.</item>
    /// <item>Kinematic (every SAM and air-to-air missile, some air-launched
    /// anti-ship missiles): speed is thrust less drag, which grows with
    /// speed², and the top speed is never consulted. A higher top speed
    /// alone did nothing, so Hypersonic was a no-op. Thrust × s with drag ÷ s
    /// makes every moment of the flight s times as fast.</item>
    /// </list>
    /// Kinematic drag is calibrated from the missile's range and cached by
    /// ammo name, shared with the enemy's identical rounds, so ours always
    /// carries its own: worked out once from the stock values, before any
    /// range boost, then divided.
    /// </summary>
    static void ApplyMissileSpeed(AmmunitionParameters ap, float speed, bool airLaunched)
    {
        ap._maxVelocityInKnots = Base(ap, "speed", ap._maxVelocityInKnots) * speed;
        // The speed a locked missile runs in at; stock, it slowed back down for the attack.
        ap._terminalVelocityInKnots = Base(ap, "terminalSpeed", ap._terminalVelocityInKnots) * speed;
        // The cruise at loft altitude (stock, the top speed unless set) and a
        // snake search (Tomahawk) each have their own speed.
        ap._maxLoftVelocityInKnots = Base(ap, "loftSpeed", ap._maxLoftVelocityInKnots) * speed;
        ap._searchVelocityInKnots = Base(ap, "searchSpeed", ap._searchVelocityInKnots) * speed;
        ap._acceleration = Base(ap, "acceleration", ap._acceleration) * speed;
        ap._deceleration = Base(ap, "deceleration", ap._deceleration) * speed;
        // Below zero means no sustainer.
        float sustainer = Base(ap, "sustainer", ap._sustainerBurnAcceleration);
        ap._sustainerBurnAcceleration = sustainer > 0f ? sustainer * speed : sustainer;
        if (ap.Kinematics == AmmunitionParameters.KinematicsLevel.Full)
            DragCoefficient(ap) = Base(ap, "drag", StockDrag(ap, airLaunched)) / speed;
        KeepTurningCircle(ap, speed);
    }

    /// <summary>A round's flight values now and as the game gave them, for the missile trace.</summary>
    internal static string MissileStats(AmmunitionParameters ap)
    {
        string Row(string label, string key, float now) =>
            bases.TryGetValue(ap, out var map) && map.TryGetValue(key, out float stock)
                ? $"{label} {now:0.#} (stock {stock:0.#})"
                : $"{label} {now:0.#} (never boosted)";
        return string.Join(", ",
            Row("speed", "speed", ap._maxVelocityInKnots),
            Row("terminal speed", "terminalSpeed", ap._terminalVelocityInKnots),
            Row("loft speed", "loftSpeed", ap._maxLoftVelocityInKnots),
            Row("search speed", "searchSpeed", ap._searchVelocityInKnots),
            Row("acceleration", "acceleration", ap._acceleration),
            Row("turn", "turnRate", ap._maxTurnRateDegrees),
            Row("terminal turn", "terminalTurnRate", ap._terminalVerticalTurnRate),
            Row("turn g", "turnG", ap._maxTurnG),
            Row("range nm", "rangeMiles", ap._maxLaunchRangeInMiles),
            Row("min range nm", "minRangeMiles", ap._minLaunchRangeInMiles),
            Row("anti-chaff", "antiChaff", ap._antiChaffBonus)) + $", kinematics {ap.Kinematics}";
    }

    /// <summary>How many times its stock top speed a weapon's round now flies; 1 for anything we never boosted.</summary>
    static float SpeedFactor(AmmunitionParameters ap) =>
        ap != null && bases.TryGetValue(ap, out var map) && map.TryGetValue("speed", out float stock) && stock > 0f
            ? ap._maxVelocityInKnots / stock
            : 1f;

    /// <summary>
    /// A weapon can't hit anything for its first second, so it doesn't strike
    /// its own launcher. At ×5 a missile covers five times the stock distance
    /// in that second, and passed through a close target. A boosted round is
    /// armed after 1/speed seconds: the launch time is moved back for the
    /// check alone, and put back after.
    /// </summary>
    [HarmonyPatch(typeof(WeaponBase), nameof(WeaponBase.CheckCollision))]
    static class ArmAtSpeed
    {
        const double StockArmSeconds = 1.0;

        static void Prefix(WeaponBase __instance, out double __state)
        {
            __state = __instance._launchTime;
            double age = GameTime.missionElapsedTime - __instance._launchTime;
            if (age >= StockArmSeconds) return;
            float speed = SpeedFactor(__instance._ap);
            if (speed > 1f && age >= StockArmSeconds / speed)
                __instance._launchTime = GameTime.missionElapsedTime - StockArmSeconds;
        }

        static void Postfix(WeaponBase __instance, double __state) => __instance._launchTime = __state;
    }

    /// <summary>Only asked for on first touch, while the clone still has the game's own values.</summary>
    static float StockDrag(AmmunitionParameters ap, bool airLaunched) =>
        bases.TryGetValue(ap, out var map) && map.TryGetValue("drag", out float drag) ? drag
        : DragCoefficient(ap) > -0.1f ? DragCoefficient(ap)
        : KinematicsCache.Instance.CalcDragFactor(ap, airLaunched);

    /// <summary>
    /// Wave-top: skimming, final-phase and terminal heights come down to
    /// 10 ft, never up. A SAM rolls about 5% against a target below its
    /// floor, and most Soviet naval SAMs' floors are 30–80 ft; a height
    /// below zero means the missile has none set, and is left alone.
    /// </summary>
    static void ApplyWaveTop(AmmunitionParameters ap, bool on)
    {
        float low = Utils.heightInFeetToUnity(WaveTopFeet);
        float skimFeet = Base(ap, "skimFeet", ap._seaSkimmingAltFeet);
        float skim = Base(ap, "skim", ap._seaSkimmingAltUnity);
        float final = Base(ap, "finalAlt", ap._finalFlightPhaseAltUnity);
        float terminal = Base(ap, "terminalAlt", ap._terminalAltUnity);
        ap._seaSkimmingAltFeet = on ? Mathf.Min(skimFeet, WaveTopFeet) : skimFeet;
        ap._seaSkimmingAltUnity = on ? Mathf.Min(skim, low) : skim;
        ap._finalFlightPhaseAltUnity = on && final > low ? low : final;
        ap._terminalAltUnity = on && terminal > low ? low : terminal;
    }

    /// <summary>
    /// Accuracy's lead error against aircraft. The AA error budget is built fresh for every
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
            __result._predictionSigma *= AimErrorFactor(run);
        }
    }

    static readonly AccessTools.FieldRef<WeaponSystem, double> ReloadStart = AccessTools.FieldRefAccess<WeaponSystem, double>("_reloadStartTime");
    /// <summary>The longest a reload is held for its arm, past the launcher's stock reload time.</summary>
    const double ArmHoldMarginSeconds = 10.0;

    /// <summary>
    /// A fast reload waits for the launcher's arm. A magazine launcher's load
    /// cycle (swing to the magazine, load, back to idle) runs beside the
    /// reload timer, and for most launchers the timer alone ends the reload.
    /// The game makes the timer wait for the arm only after 2 s, which a
    /// reload ×6 never reaches: the reload ended with the arm still on its
    /// way down, the engagement swung it back at the target while the load
    /// cycle swung it at the magazine, and the launcher sat on "Aligning
    /// launcher" until something reloaded it again. This holds our launchers'
    /// reloads until the cycle is idle, as the game already does for RBUs and
    /// lower-deck launchers, so the arm's cycle sets the pace once the timer
    /// is faster than it.
    /// </summary>
    [HarmonyPatch(typeof(WeaponSystem), nameof(WeaponSystem.OnUpdate))]
    static class ReloadWaitsForArm
    {
        static void Prefix(WeaponSystem __instance, out float? __state)
        {
            __state = null;
            if (!(__instance is WeaponSystemLauncher launcher) || !launcher._isReloading ||
                launcher._vwp._associatedMagazine == null || !LoadCycleBusy(launcher._state)) return;
            var run = Run.Current;
            if (run == null || !run.Fleet.Contains(launcher._baseObject)) return;

            float reload = launcher._vwp._magazineReloadTime;
            double elapsed = GameTime.missionElapsedTime - ReloadStart(launcher);
            if (elapsed > Base(launcher, "reload", reload) + ArmHoldMarginSeconds || elapsed < reload - 0.5) return;
            // Just long enough that this frame's timer check finds it unfinished.
            __state = reload;
            launcher._vwp._magazineReloadTime = (float)elapsed + 0.5f;
        }

        /// <summary>A finalizer, not a postfix, so the reload time is put back even if the update throws.</summary>
        static void Finalizer(WeaponSystem __instance, float? __state)
        {
            if (__state is float reload) __instance._vwp._magazineReloadTime = reload;
        }

        /// <summary>The states the load cycle moves on from by itself; the others are idle or the shutdown's.</summary>
        static bool LoadCycleBusy(WeaponSystemLauncher.State state) =>
            state != WeaponSystemLauncher.State.Idle &&
            state != WeaponSystemLauncher.State.MoveToRestPosition &&
            state != WeaponSystemLauncher.State.CheckForLoadedAmmunition;
    }

    /// <summary>Flak's burst radius, times the shell's own: the anti-air rank's factor and the burst radius ranks'.</summary>
    static float FuzeFactor(Run run) =>
        Upgrades.Has(run, Upgrades.Guns, Stat.AntiAir)
            ? Upgrades.Total(run, Upgrades.Guns, Stat.AntiAir) * Upgrades.Total(run, Upgrades.Guns, Stat.Fuze)
            : 1f;

    const int MaxPreviewRows = 4;

    /// <summary>
    /// A card's before → after: what its next <paramref name="ranks"/> ranks
    /// would change, stat by stat, in the fleet's own weapons and crews.
    /// Worked out from the values the game first gave them, the same way
    /// Apply sets them.
    /// </summary>
    internal static List<string> Preview(Run run, Upgrade line, int ranks)
    {
        var fleet = run.Fleet.Where(s => s != null && !s.IsDestroyed).ToList();
        var rows = new List<string>();
        int rank = run.RankOf(line);
        foreach (var stat in Upgrades.StatsIn(line, rank, rank + ranks))
            PreviewStat(run, line, stat, ranks, fleet, rows);

        rows = rows.Distinct().ToList();
        if (rows.Count > MaxPreviewRows)
        {
            int more = rows.Count - (MaxPreviewRows - 1);
            rows = rows.Take(MaxPreviewRows - 1).Append($"and {more} more").ToList();
        }
        // After the cap, so a card full of numbers cannot push it out.
        if (Upgrades.StatsIn(line, rank, rank + ranks).Contains(Stat.Refit))
            rows.Add("Delayed while a plane is guiding a weapon in flight");
        return rows;
    }

    static void PreviewStat(Run run, Upgrade line, Stat stat, int ranks, List<ObjectBase> fleet, List<string> rows)
    {
        float now = Upgrades.Total(run, line, stat), next = Upgrades.Total(run, line, stat, ranks);
        if (line == Upgrades.Guns)
            foreach (var gun in fleet.SelectMany(Guns))
            {
                string name = gun._vwp._systemName;
                var ap = MagazineAmmo(gun).FirstOrDefault();
                switch (stat)
                {
                    case Stat.Damage when ap != null:
                        float power = Base(ap, "power", ap._warheadPower);
                        rows.Add($"{name}: {F(power * now)} → {F(power * next)} damage a shell");
                        break;
                    case Stat.RateOfFire:
                        bool bursts = Bursts(gun);
                        float reload = bursts ? Base(gun, "burstGap", BurstGap(gun)) : Base(gun, "shellReload", ShellReload(gun));
                        string row = $"{name}: {F(reload / now)} s → {F(reload / next)} s between {(bursts ? "bursts" : "shots")}";
                        float rack = Base(gun, "reload", gun._vwp._magazineReloadTime);
                        if (gun._vwp._associatedMagazine != null && rack > 0f) row += $", {F(rack / now)} s → {F(rack / next)} s to reload";
                        if (ap != null)
                        {
                            float speed = Base(ap, "muzzleVelocityMs", ap._muzzleVelocityInMeterPerSecond);
                            row += $", {speed * ShellSpeedFactor(now):0} → {speed * ShellSpeedFactor(next):0} m/s";
                        }
                        rows.Add(row);
                        break;
                    case Stat.AntiAir when ap != null:
                        float burst = Mathf.Max(Base(ap, "fuzeRadius", ap._fuzeProximityDistance), MinFuzeRadiusM * MetresToUnity) *
                                      next * Upgrades.Total(run, Upgrades.Guns, Stat.Fuze);
                        rows.Add($"{name}: {(Defence.CanEngageAir(gun) ? "engages aircraft" : "surface targets only")} → engages aircraft, bursts within {burst / MetresToUnity:0} m");
                        break;
                    case Stat.Fuze when ap != null:
                        float radius = Mathf.Max(Base(ap, "fuzeRadius", ap._fuzeProximityDistance), MinFuzeRadiusM * MetresToUnity) *
                                       Upgrades.Total(run, Upgrades.Guns, Stat.AntiAir);
                        rows.Add($"{name}: bursts within {radius * now / MetresToUnity:0} m → {radius * next / MetresToUnity:0} m");
                        break;
                }
            }
        else if (line == Upgrades.Sams)
            foreach (var ship in fleet.Where(s => SamLaunchers(s).Any() || PylonAmmo(s).Any(AirToAir)))
                switch (stat)
                {
                    case Stat.Reload:
                        foreach (var launcher in SamLaunchers(ship))
                        {
                            float reload = Base(launcher, "reload", launcher._vwp._magazineReloadTime);
                            if (reload > 0f) rows.Add($"{launcher._vwp._systemName}: {F(reload * now)} s → {F(reload * next)} s to reload");
                        }
                        break;
                    case Stat.KillChance:
                        foreach (var ap in SamLaunchers(ship).SelectMany(LauncherAmmo).Concat(PylonAmmo(ship)).Where(AirToAir))
                        {
                            float pk = Base(ap, "kill", ap._killProbability);
                            rows.Add($"{AmmoName(ap)}: {Percent(pk + now)} → {Percent(pk + next)} hit chance");
                        }
                        break;
                    case Stat.Channels:
                        foreach (var director in SamDirectors(ship))
                        {
                            int targets = (int)Base(director, "targets", director._targetChannels);
                            rows.Add($"{director._systemName}: {targets + (int)now} → {targets + (int)next} targets at once");
                        }
                        break;
                }
        else if (line == Upgrades.AntiShip)
            foreach (var ap in fleet.SelectMany(s => AntiShipLaunchers(s).SelectMany(LauncherAmmo).Concat(PylonAmmo(s))).Where(AntiShipMissile))
            {
                string name = AmmoName(ap);
                switch (stat)
                {
                    case Stat.Damage:
                        float power = Base(ap, "power", ap._warheadPower);
                        rows.Add($"{name}: {F(power * now)} → {F(power * next)} damage");
                        break;
                    case Stat.Speed:
                        float speed = Base(ap, "speed", ap._maxVelocityInKnots);
                        rows.Add($"{name}: {speed * now:0} → {speed * next:0} knots");
                        break;
                    case Stat.Range:
                        float range = Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles);
                        float minimum = Base(ap, "minRangeMiles", ap._minLaunchRangeInMiles);
                        string closer = minimum >= MinimumWorthShowingNm ? $", min {F(minimum / now)} → {F(minimum / next)} nm" : "";
                        rows.Add($"{name}: {range * now:0} → {range * next:0} nm{closer}");
                        break;
                    case Stat.Evasion:
                        // What is left of an enemy SAM's chance, before the game's 5% floor.
                        float bonus = Base(ap, "samDefence", ap._samDefenceBonus);
                        rows.Add($"{name}: enemy SAMs hit at {Percent(1f - Mathf.Min(1f, bonus + now))} → {Percent(1f - Mathf.Min(1f, bonus + next))} of their chance");
                        break;
                }
            }
        else if (line == Upgrades.Torpedoes)
            foreach (var ap in fleet.SelectMany(s => TorpedoCarriers(s).Concat(ShipTorpedoes(s))))
            {
                string name = AmmoName(ap);
                if (run.RankOf(line) == 0 &&
                    AntiSubmarineOnly(ap._targetType, Base(ap, "hasSecondary", ap._hasSecondaryTargetType ? 1f : 0f) > 0f))
                    rows.Add($"{name}: submarines only → ships too");
                // A carrier has only its flight on this line: no warhead or seeker of its own.
                if (TorpedoCarrier(ap) && (AntiShipMissile(ap) || stat != Stat.Speed && stat != Stat.Range)) continue;
                switch (stat)
                {
                    case Stat.Damage:
                        float power = Base(ap, "power", ap._warheadPower);
                        rows.Add($"{name}: {F(power * now)} → {F(power * next)} damage");
                        break;
                    case Stat.Speed:
                        float speed = Base(ap, "speed", ap._maxVelocityInKnots);
                        rows.Add($"{name}: {speed * now:0} → {speed * next:0} knots");
                        break;
                    case Stat.Range:
                        float range = Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles);
                        rows.Add($"{name}: {F(range * now)} → {F(range * next)} nm");
                        break;
                    case Stat.Seeker:
                        // Seeker ranges are kept in game units, 27.56 to the mile.
                        float hears = Mathf.Max(Base(ap, "seekerActive", ap._seekerActiveRange), Base(ap, "seekerPassive", ap._seekerPassiveRange)) / UnityPerMile;
                        rows.Add($"{name}: seeker hears {F(hears * now)} → {F(hears * next)} nm");
                        break;
                }
            }
        else if (line == Upgrades.Jammers)
            switch (stat)
            {
                case Stat.Range:
                    rows.Add($"Jammer reach: {EcmFit.ReachNm(run):0} → {EcmFit.ReachNm(run, ranks):0} nm");
                    break;
                case Stat.Arc:
                    rows.Add($"Jammer arc: {EcmFit.Arc(run):0}° → {EcmFit.Arc(run, ranks):0}°");
                    break;
                case Stat.JamChance:
                    rows.Add($"Jam chance: {F(EcmFit.Chance(run))} → {F(EcmFit.Chance(run, ranks))}, less each missile's resistance");
                    break;
            }
        else if (line == Upgrades.Mortars)
            foreach (var launcher in fleet.SelectMany(MortarLaunchers))
            {
                string name = launcher._vwp._systemName;
                var ap = LauncherAmmo(launcher).FirstOrDefault(Mortar);
                if (ap == null) continue;
                switch (stat)
                {
                    case Stat.Reach:
                        float range = Base(ap, "rangeMiles", ap._maxLaunchRangeInMiles);
                        float bombardment = Upgrades.Has(run, Upgrades.Bombardment) ? BombardmentFactor : 1f;
                        rows.Add($"{name}: {F((range + now) * bombardment)} → {F((range + next) * bombardment)} nm");
                        break;
                    case Stat.Speed:
                        float speed = Base(ap, "speed", ap._maxVelocityInKnots);
                        rows.Add($"{name}: {speed * now:0} → {speed * next:0} knots");
                        break;
                    case Stat.Reload:
                        float reload = Base(launcher, "reload", launcher._vwp._magazineReloadTime);
                        rows.Add($"{name}: {F(reload * now)} s → {F(reload * next)} s to reload");
                        break;
                    case Stat.Damage:
                        float power = Base(ap, "power", ap._warheadPower);
                        rows.Add($"{name}: {F(power * now)} → {F(power * next)} damage a round");
                        break;
                }
            }
        else if (line == Upgrades.PointDefence)
            foreach (var ciws in fleet.SelectMany(s => s._obp._ciwsWeapons))
            {
                string name = ciws._vwp._systemName;
                if (stat == Stat.Intercept)
                {
                    float intercept = Base(ciws, "intercept", ciws._missileInterceptChance);
                    rows.Add($"{name}: {Mathf.Min(MaxCiwsIntercept, intercept + now):0}% → {Mathf.Min(MaxCiwsIntercept, intercept + next):0}% missile intercept chance");
                }
                else
                {
                    float rof = Base(ciws, "rof", ciws._fireRatePerMinute);
                    rows.Add($"{name}: {rof * now:0} → {rof * next:0} rounds a minute");
                }
            }
        else if (line == Upgrades.Countermeasures)
            foreach (var chaff in fleet.SelectMany(s => s._obp._chaffWeapons))
            {
                string name = chaff._vwp._systemName;
                switch (stat)
                {
                    case Stat.Spoof:
                        foreach (var ap in MagazineAmmo(chaff).Append(chaff._ammoInUse?._ap).Where(a => a != null).Distinct())
                        {
                            float spoof = Base(ap, "spoof", ap._decoySpoofChance);
                            rows.Add($"{name}: {Percent(spoof + now)} → {Percent(spoof + next)} spoof chance");
                        }
                        break;
                    case Stat.Reload:
                        float reload = Base(chaff, "reload", (float)ChaffReload(chaff));
                        rows.Add($"{name}: {F(reload * now)} s → {F(reload * next)} s to reload");
                        break;
                    case Stat.DecoyRule:
                        rows.Add($"Decoys forced to fail after {StockSpoofLimit} in a row → never");
                        break;
                }
            }
        else if (line == Upgrades.AirWing)
            switch (stat)
            {
                case Stat.Damage:
                    foreach (var ap in fleet.SelectMany(PylonAmmo).Where(StrikeStore))
                    {
                        float power = Base(ap, "power", ap._warheadPower);
                        rows.Add($"{AmmoName(ap)}: {F(power * now)} → {F(power * next)} damage");
                    }
                    break;
                case Stat.Resupply:
                    rows.Add($"Pylons rearmed at level-ups → every {F(Escorts.ResupplySeconds / 60f)} min too");
                    break;
                case Stat.Refit:
                    int standard = Refits.Standard(run, ranks);
                    foreach (var aircraft in run.AirWing.Keys.Where(a => a != null && !a.IsDestroyed))
                    {
                        var to = Refits.Next(aircraft, standard);
                        if (to != null) rows.Add($"{Refits.Current(aircraft).Name} → {to.Name}");
                    }
                    if (rows.Count == 0) rows.Add("No strike flight to refit yet: those that join later are refitted as they arrive");
                    break;
                case Stat.Reload:
                    rows.Add($"Air resupply every {F(Escorts.ResupplySeconds * now / 60f)} min → {F(Escorts.ResupplySeconds * next / 60f)} min");
                    break;
            }
        else if (line == Upgrades.Reaction)
            foreach (var ship in fleet.Where(s => s._crew != null))
            {
                float contact = Base(ship._crew, "contact", ship._crew._newContactProcessTime);
                rows.Add($"{ship.Name.Value}: new contacts processed in {F(contact * ReactionFactor(run))} s → {F(contact * ReactionFactor(run, ranks))} s");
            }
        else if (line == Upgrades.Accuracy)
            rows.Add($"Gun and mortar aiming error: {AimErrorFactor(run) * 100f:0.#}% → {AimErrorFactor(run, ranks) * 100f:0.#}% of normal");
    }

    static string F(float value) => value.ToString(value < 10f ? "0.##" : "0.#", System.Globalization.CultureInfo.InvariantCulture);
    static string Percent(float fraction) => $"{fraction * 100f:0}%";
    static string AmmoName(AmmunitionParameters ap) => string.IsNullOrEmpty(ap._natoDisplayedName) ? ap._ammunitionFileName : ap._natoDisplayedName;

    /// <summary>True for one of our shell clones given a longer range by Long shots, or a faster shell by rate of fire.</summary>
    internal static bool IsBoostedShell(AmmunitionParameters ap) =>
        bases.TryGetValue(ap, out var map) &&
        (map.TryGetValue("rangeUnity", out float range) && ap._launchRangesInUnity.y > range ||
         map.TryGetValue("muzzleVelocity", out float speed) && ap._muzzleVelocityInUnity > speed);

    internal static IEnumerable<WeaponSystemGun> Guns(ObjectBase ship) => ship._obp._gunWeapons;

    static bool Bursts(WeaponSystemGun gun) => BurstCount(gun) > 1;

    /// <summary>Seconds from one shot to the next, or one burst to the next for a gun that fires in bursts.</summary>
    internal static float Cycle(WeaponSystemGun gun) => Bursts(gun) ? BurstGap(gun) : ShellReload(gun);

    internal static int ShotsPerCycle(WeaponSystemGun gun) => Bursts(gun) ? BurstCount(gun) : 1;

    internal static IEnumerable<WeaponSystemLauncher> SamLaunchers(ObjectBase ship) =>
        ship._obp._launcherWeapons.Where(l => LauncherAmmo(l).Any(AirToAir));

    /// <summary>A SAM launcher with rounds left, on the rails or in its magazine: an empty one stops nothing.</summary>
    internal static bool HasSamsLeft(WeaponSystemLauncher launcher) =>
        LauncherAmmo(launcher).Where(AirToAir).Any(ap =>
            launcher.getLoadedAmmoCount(ap._ammunitionFileName) + launcher.getMagazineAmmoCount(ap._ammunitionFileName) > 0);

    /// <summary>
    /// The fire-control radars the SAM launchers name as theirs. Not every
    /// director aboard: a Kirov's eight AK-630s and a Kiev's SS-N-12s have
    /// their own, and channels there would do nothing for its SAMs.
    /// </summary>
    internal static IEnumerable<SensorSystemRadar> SamDirectors(ObjectBase ship)
    {
        var named = new HashSet<SensorSystem>(SamLaunchers(ship).SelectMany(l => l._vwp._associatedSensors).Where(s => s != null));
        return ship._obp._targetingRadarSystems.Where(named.Contains);
    }

    internal static IEnumerable<WeaponSystemLauncher> AntiShipLaunchers(ObjectBase ship) =>
        ship._obp._launcherWeapons.Where(l => l._weaponType != WeaponSystem.WeaponType.Torpedo && LauncherAmmo(l).Any(AntiShipMissile));

    /// <summary>
    /// Torpedo tubes on a ship, and launchers with rockets or missiles that
    /// carry a torpedo. From the line's first rank every one of ours can be
    /// fired at ships: Apply makes sure of it.
    /// </summary>
    internal static IEnumerable<WeaponSystemLauncher> TorpedoLaunchers(ObjectBase ship) =>
        ship._obp._launcherWeapons.Where(l =>
            l._weaponType == WeaponSystem.WeaponType.Torpedo && LauncherAmmo(l).Any(ShipTorpedo) || LauncherAmmo(l).Any(TorpedoCarrier));

    /// <summary>The rockets and missiles that carry a torpedo: ASROC, VLA, SS-N-14, SS-N-16.</summary>
    static IEnumerable<AmmunitionParameters> TorpedoCarriers(ObjectBase ship) =>
        TorpedoLaunchers(ship).SelectMany(LauncherAmmo).Where(TorpedoCarrier).Distinct();

    /// <summary>Every torpedo the Torpedoes line boosts: those in the tubes, and those the carriers drop.</summary>
    static IEnumerable<AmmunitionParameters> ShipTorpedoes(ObjectBase ship) =>
        TorpedoLaunchers(ship).SelectMany(LauncherAmmo).Where(ShipTorpedo)
            .Concat(TorpedoCarriers(ship).Select(Payload))
            .Distinct();

    /// <summary>Launchers firing anti-submarine rocket mortars: RBU-1000 to RBU-6000, Limbo and the like.</summary>
    internal static IEnumerable<WeaponSystemLauncher> MortarLaunchers(ObjectBase ship) =>
        ship._obp._launcherWeapons.Where(l => l._weaponType == WeaponSystem.WeaponType.RBU && LauncherAmmo(l).Any(Mortar));

    /// <summary>A mortar round. Multiple rocket launchers share the RBU type, told apart by subtype, and are not anti-submarine.</summary>
    internal static bool Mortar(AmmunitionParameters ap) =>
        ap._type == Ammunition.Type.RBU && ap._subType != Ammunition.Type.MLRS && ap._targetType == Ammunition.Target.ASW;

    /// <summary>An aircraft's hardpoints with a round of this kind hanging on them now.</summary>
    internal static IEnumerable<WeaponSystemHardpoint> Hardpoints(ObjectBase ship, Func<AmmunitionParameters, bool> carrying) =>
        ship.IsAirUnit
            ? ship._obp._hardpointWeapons.Where(h => h._weapons.Any(w => w != null && w._ap != null && carrying(w._ap)))
            : Enumerable.Empty<WeaponSystemHardpoint>();

    /// <summary>Every round on an aircraft's pylons. Each round has its own parameter clone, and a rehung round a fresh one.</summary>
    static IEnumerable<AmmunitionParameters> PylonAmmo(ObjectBase ship) =>
        ship.IsAirUnit
            ? ship._obp._hardpointWeapons.SelectMany(h => h._weapons).Where(w => w != null).Select(w => w._ap).Where(ap => ap != null).Distinct()
            : Enumerable.Empty<AmmunitionParameters>();

    internal static bool AirToAir(AmmunitionParameters ap) => ap._targetType == Ammunition.Target.AAW;

    internal static bool AntiShipMissile(AmmunitionParameters ap) =>
        ap._targetType == Ammunition.Target.ASuW && ap._type != Ammunition.Type.Torpedo;

    static bool ShipTorpedo(AmmunitionParameters ap) => ap._type == Ammunition.Type.Torpedo;

    static bool TorpedoCarrier(AmmunitionParameters ap) =>
        ap._hasSubmunition && ap._submunitionType == Ammunition.Type.Torpedo && !string.IsNullOrEmpty(ap._submunitionFileName);

    /// <summary>A torpedo that, as the game gave it, can only be fired at submarines.</summary>
    static bool AntiSubmarineOnly(Ammunition.Target target, bool hadSecondary) =>
        target == Ammunition.Target.ASW && !hadSecondary;

    /// <summary>Bombs and rockets, not drop tanks: the Air wing line's damage.</summary>
    static bool StrikeStore(AmmunitionParameters ap) =>
        ap._type == Ammunition.Type.Bomb && ap._subType != Ammunition.Type.Fueltank || ap._type == Ammunition.Type.AerialRocket;

    /// <summary>
    /// Every parameter object a launcher's rounds use: its magazine's stock,
    /// and for magazine-less launchers such as Harpoon canisters, the per-slot
    /// clones of the rounds loaded and of the reload type.
    /// </summary>
    internal static IEnumerable<AmmunitionParameters> LauncherAmmo(WeaponSystemLauncher launcher) =>
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
