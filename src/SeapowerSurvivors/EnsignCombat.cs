using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// What ensigns do during the fight, rather than to a weapon's numbers (Boosts
/// writes those): damage by who fired and at whom, corrosive gas, EMP mortars,
/// wires, and the things an ensign keeps out of action.
/// </summary>
static class EnsignCombat
{
    const float SmallGunDamage = 0.75f, LongRangeDamage = 0.75f, TorpedoDamage = 0.75f;
    const float HeavyGunDamage = 2.5f, HeavyMissileDamage = 2f, BombDamage = 10f;
    const float LastStandHit = 3f, LastStandUnhit = 0.75f;
    const float EnemyShipDamage = 2f, UnsinkableOtherDamage = 2f;

    static readonly AccessTools.FieldRef<Blastzone, Vector3> BlastPosition = AccessTools.FieldRefAccess<Blastzone, Vector3>("_blastPosition");
    static readonly AccessTools.FieldRef<Blastzone, ObjectBase> BlastPlatform = AccessTools.FieldRefAccess<Blastzone, ObjectBase>("_launchPlatform");
    static readonly AccessTools.FieldRef<Blastzone, AmmunitionParameters> BlastAmmo = AccessTools.FieldRefAccess<Blastzone, AmmunitionParameters>("_ap");
    static readonly AccessTools.FieldRef<Blastzone, Projectile> BlastShell = AccessTools.FieldRefAccess<Blastzone, Projectile>("_projectile");
    static readonly AccessTools.FieldRef<Blastzone, WeaponBase> BlastWeapon = AccessTools.FieldRefAccess<Blastzone, WeaponBase>("_weapon");

    static bool InFleet(ObjectBase o) => o != null && Run.Current != null && Run.Current.Fleet.Contains(o);
    static bool EnemyShip(ObjectBase o) => o is Vessel && !o.IsDestroyed && o._taskforce == Globals._enemyTaskforce;

    /// <summary>The factor an ensign puts on our weapons of this kind. Guns' is shared with the shell-on-aircraft path.</summary>
    static float OurDamage(AmmunitionParameters ap)
    {
        float factor = 1f;
        if (Ensigns.Active(Ensigns.LastStand)) factor *= Run.Current.FlagshipHit ? LastStandHit : LastStandUnhit;
        if (Ensigns.Active(Ensigns.LongRange)) factor *= LongRangeDamage;
        switch (ap._type)
        {
            case Ammunition.Type.Projectile:
                if (Ensigns.Active(Ensigns.HeavyGuns)) factor *= HeavyGunDamage;
                if (Ensigns.Active(Ensigns.SmallGuns) && Ensigns.SmallCalibre(ap)) factor *= SmallGunDamage;
                break;
            case Ammunition.Type.Missile:
                if (Ensigns.Active(Ensigns.HeavyMissiles)) factor *= HeavyMissileDamage;
                break;
            case Ammunition.Type.Torpedo:
                if (Ensigns.Active(Ensigns.FastTorpedoes)) factor *= TorpedoDamage;
                break;
            case Ammunition.Type.Bomb:
                if (Ensigns.Active(Ensigns.IronBombs) && ap._guidanceType == AmmunitionParameters.GuidanceType.None) factor *= BombDamage;
                break;
        }
        return factor;
    }

    /// <summary>Every warhead that reaches a ship or aircraft is worked out here, so this one place covers all of them.</summary>
    [HarmonyPatch(typeof(Blastzone), "GetWeaponDamage")]
    static class WarheadDamage
    {
        static void Postfix(Blastzone __instance, ObjectBase __0, ref float __result)
        {
            var run = Run.Current;
            if (run == null || run.Hoisted.Count == 0) return;
            var platform = BlastPlatform(__instance);
            if (InFleet(platform)) __result *= OurDamage(BlastAmmo(__instance));
            else if (platform is Vessel && platform._taskforce == Globals._enemyTaskforce && Ensigns.Active(Ensigns.SlowEnemies)) __result *= EnemyShipDamage;
            if (InFleet(__0) && Ensigns.Active(Ensigns.Unsinkable) && !IsMissile(BlastAmmo(__instance))) __result *= UnsinkableOtherDamage;
        }
    }

    static bool IsMissile(AmmunitionParameters ap) => ap._type == Ammunition.Type.Missile || ap._type == Ammunition.Type.ASROC;

    /// <summary>
    /// A blast that should not touch this unit at all: an enemy missile on an
    /// Unsinkable fleet, or a gas shell on a ship. The hit's whole calculation is
    /// skipped, so it leaves no fire or flooding either, and it records no impact
    /// for the hidden integrity drain to act on.
    /// </summary>
    [HarmonyPatch(typeof(Blastzone), "OnHitUnit")]
    static class NoHarm
    {
        // Runs first so CombatHooks' own prefix there still sees what it needs.
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Blastzone __instance, ObjectBase __0)
        {
            var run = Run.Current;
            if (run == null || run.Hoisted.Count == 0) return true;
            var ap = BlastAmmo(__instance);
            if (InFleet(__0) && Ensigns.Active(Ensigns.Unsinkable) && ap != null && IsMissile(ap) && !InFleet(BlastPlatform(__instance))) return false;
            if (__0 is Vessel && BlastShell(__instance) != null && InFleet(BlastPlatform(__instance)) && Ensigns.Active(Ensigns.GasShells)) return false;
            return true;
        }
    }

    /// <summary>Gun shells on aircraft and missiles: worked out in the shell's collision, not in a blast. CIWS shares the roll, so only a flagged shell counts.</summary>
    [HarmonyPatch(typeof(Projectile), "ProcessCollision")]
    static class ShellCollision
    {
        internal static float factor = 1f;

        static void Prefix(Projectile __instance)
        {
            factor = InFleet(__instance._launchPlatform) && __instance._pp != null ? OurDamage(__instance._pp) : 1f;
        }

        static void Finalizer() => factor = 1f;
    }

    [HarmonyPatch(typeof(CiwsBallistics), nameof(CiwsBallistics.VariedHitDamage))]
    static class ShellOnAirDamage
    {
        static void Postfix(ref float __result) => __result *= ShellCollision.factor;
    }

    // Corrosive gas.

    sealed class Cloud
    {
        public GeoPosition At;
        public double Ends;
        /// <summary>Hidden integrity a ship inside loses each second.</summary>
        public float Rate;
        public GameObject Smoke;
    }

    /// <summary>Oldest first. Bounded, as a fast gun lands several shells a second and each is a cloud.</summary>
    static readonly List<Cloud> clouds = new List<Cloud>();
    const int MaxClouds = 400;
    /// <summary>Smoke is only for seeing where the gas lies, so clouds this close share one plume.</summary>
    const float SmokeSpacingNm = 0.5f;
    static readonly string[] SmokeClasses = { "NatoRadarFire", "VehicleSmokes" };

    /// <summary>A gas shell has come down, on a hull or in the water. Flak bursting in the air makes no cloud.</summary>
    [HarmonyPatch(typeof(Blastzone), MethodType.Constructor, typeof(Projectile), typeof(Vector3))]
    static class GasShell
    {
        const float MaxHeightUnity = 1.5f;

        static void Postfix(Projectile __0, Vector3 __1)
        {
            if (__0 == null || __1.y > MaxHeightUnity || !Ensigns.Active(Ensigns.GasShells) || !InFleet(__0._launchPlatform)) return;
            var ap = __0._pp;
            if (ap == null) return;
            var at = __0._launchPlatform._geoPosition;
            at.FromUnity(__1);
            // The warhead's whole damage, in the hidden pool, over the cloud's life: any ship
            // that stays inside takes it all, with no armour and no table of shell sizes.
            Bloom(at, ap._warheadPower * ap._damageMultiplier * OurDamage(ap) / Ensigns.GasSeconds, __1);
        }
    }

    static void Bloom(GeoPosition at, float rate, Vector3 world)
    {
        if (clouds.Count >= MaxClouds) Remove(clouds[0]);
        var cloud = new Cloud { At = at, Rate = rate, Ends = GameTime.missionElapsedTime + Ensigns.GasSeconds };
        if (!clouds.Any(c => c.Smoke != null && (float)c.At.getDistanceInMiles(at) < SmokeSpacingNm))
            cloud.Smoke = SpawnSmoke(world);
        clouds.Add(cloud);
    }

    static GameObject SpawnSmoke(Vector3 at)
    {
        try
        {
            var effects = Singleton<EffectsManager>.Instance;
            foreach (var name in SmokeClasses)
            {
                var prefab = effects.getRandomEffectFromClass(name);
                if (prefab != null) return Object.Instantiate(prefab, at, Quaternion.identity);
            }
        }
        catch (System.Exception e) { Entry.Log.LogWarning($"No smoke for a gas cloud: {e.Message}"); }
        return null;
    }

    static void Remove(Cloud cloud)
    {
        if (cloud.Smoke != null) Object.Destroy(cloud.Smoke);
        clouds.Remove(cloud);
    }

    static void TickGas(float dt)
    {
        double now = GameTime.missionElapsedTime;
        foreach (var done in clouds.Where(c => now >= c.Ends).ToList()) Remove(done);
        if (clouds.Count == 0) return;
        foreach (var ship in Singleton<ObjectsManager>.Instance._listOfAllVesselObjects)
        {
            if (!EnemyShip(ship) || !(ship is Vessel vessel) || vessel._compartments == null) continue;
            float rate = clouds.Where(c => (float)c.At.getDistanceInMiles(ship._geoPosition) <= Ensigns.GasNm).Sum(c => c.Rate);
            if (rate <= 0f) continue;
            float damage = rate * dt;
            var compartments = vessel._compartments;
            // The game's own way of sinking a ship whose hidden pool has run out (Blastzone.CheckForInstakill).
            compartments._limitIntegrity -= damage;
            if (compartments._limitIntegrity < 0f && compartments._delayedSinkTime < 0.0) compartments._delayedSinkTime = 0.001;
            if (ship.Info._damageByTaskforce.ContainsKey(Globals._playerTaskforce)) ship.Info._damageByTaskforce[Globals._playerTaskforce] += damage;
            else ship.Info._damageByTaskforce.Add(Globals._playerTaskforce, damage);
        }
    }

    // EMP mortars.

    /// <summary>Ships whose radars an EMP has shut down, and when they come back.</summary>
    static readonly Dictionary<ObjectBase, double> darkened = new Dictionary<ObjectBase, double>();

    internal static bool IsDark(ObjectBase unit) => darkened.ContainsKey(unit);

    /// <summary>A mortar round of ours going off: a pulse instead of a warhead.</summary>
    [HarmonyPatch(typeof(Blastzone), nameof(Blastzone.CreateExplosion))]
    static class EmpRound
    {
        static bool Prefix(Blastzone __instance)
        {
            if (!Ensigns.Active(Ensigns.EmpMortars) || !(BlastWeapon(__instance) is RBU round) || !Boosts.Mortar(round._ap) || !InFleet(BlastPlatform(__instance))) return true;
            Pulse(round._geoPosition);
            return false;
        }
    }

    static void Pulse(GeoPosition at)
    {
        double until = GameTime.missionElapsedTime + Ensigns.EmpSeconds;
        foreach (var ship in Singleton<ObjectsManager>.Instance._listOfAllVesselObjects)
        {
            if (!EnemyShip(ship) || (float)ship._geoPosition.getDistanceInMiles(at) > Ensigns.EmpNm) continue;
            if (!darkened.ContainsKey(ship)) TestPanel.Report($"EMP: {ship.Name.Value} goes dark");
            darkened[ship] = until;
            SetDark(ship, true);
        }
    }

    static void SetDark(ObjectBase ship, bool dark)
    {
        foreach (var radar in ship._obp._searchRadarSystems.Concat(ship._obp._targetingRadarSystems).Concat(ship._obp._airSearchRadars).Concat(ship._obp._surfaceSearchRadars))
            if (radar != null) radar.Inoperable.Value = dark;
        // With its fire control down it holds fire, and Spawner will not re-alert it.
        ship._weaponStatus = dark ? ObjectBase.WeaponStatus.Hold : ObjectBase.WeaponStatus.Free;
    }

    static void TickDark()
    {
        double now = GameTime.missionElapsedTime;
        foreach (var (ship, until) in darkened.ToList())
        {
            if (ship == null || ship.IsDestroyed) darkened.Remove(ship);
            else if (now >= until)
            {
                darkened.Remove(ship);
                SetDark(ship, false);
            }
            // Damage control can bring a radar back; only the clock may.
            else SetDark(ship, true);
        }
    }

    // What the ensigns hold out of action, and the rest of the once-a-tick work.

    /// <summary>
    /// Run each director tick. Damage control can bring a launcher back, so
    /// what is disabled is disabled again each tick, as Director.KeepDisarmed does.
    /// </summary>
    internal static void Tick(Run run, float dt)
    {
        if (run.Hoisted.Count == 0) return;
        if (!run.FlagshipHit && run.Ship is Vessel flagship && flagship._compartments != null &&
            (flagship._compartments._limitIntegrity < flagship._compartments._maxLimitIntegrity ||
             flagship._compartments.TotalIntegrity < flagship._compartments._maxTotalIntegrity * 0.999f))
        {
            run.FlagshipHit = true;
            if (run.Hoisted.Contains(Ensigns.LastStand)) TestPanel.Report("Dannebrog: the flagship is hit, damage +200%");
        }

        if (run.Hoisted.Contains(Ensigns.LongCiws))
            foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit))
                foreach (var launcher in Boosts.SamLaunchers(ship))
                    launcher.Inoperable.Value = true;

        if (run.Hoisted.Contains(Ensigns.TorpedoEcm)) EcmFit.Tick(run);
        if (run.Hoisted.Contains(Ensigns.GasShells)) TickGas(dt);
        if (run.Hoisted.Contains(Ensigns.EmpMortars)) TickDark();
        if (run.Hoisted.Contains(Ensigns.WireGuided)) TickWires(run);
    }

    /// <summary>The wire is cut once a torpedo has run half its range. Stock wires only ever break.</summary>
    static void TickWires(Run run)
    {
        foreach (var torpedo in Singleton<ObjectsManager>.Instance._listOfAllWeapons.OfType<Torpedo>())
            if (torpedo != null && !torpedo.IsDestroyed && torpedo._onWire && InFleet(torpedo._launchPlatform) &&
                torpedo._remainingRangePercent <= 100f * (1f - Ensigns.WireFraction))
                torpedo.BreakWire();
    }

    /// <summary>
    /// Holds our torpedoes to the wire's speed. The game only caps the launch and
    /// picks a slower setting if it has one; a single-speed torpedo ran on at full
    /// speed and snapped its wire as soon as it passed the limit.
    /// </summary>
    [HarmonyPatch(typeof(Torpedo), nameof(Torpedo.moveForward))]
    static class HoldToWireSpeed
    {
        static void Prefix(Torpedo __instance, ref float velocityInKnots)
        {
            if (__instance._onWire && velocityInKnots > __instance._ap._maxSpeedOnWireInKnots &&
                Ensigns.Active(Ensigns.WireGuided) && InFleet(__instance._launchPlatform))
                velocityInKnots = __instance._ap._maxSpeedOnWireInKnots;
        }
    }

    /// <summary>
    /// The game fires a wire-guided torpedo singly because wires are scarce. The
    /// ensign gives ships enough wires, so ours keep the unguided salvo.
    /// </summary>
    [HarmonyPatch(typeof(AI), "AutoAttackSalvoSize")]
    static class WireSalvo
    {
        const int UnguidedTorpedoSalvo = 3;
        static readonly AccessTools.FieldRef<AI, ObjectBase> AiShip = AccessTools.FieldRefAccess<AI, ObjectBase>("_baseObject");

        static void Postfix(AI __instance, Ammunition ammo, ref int __result)
        {
            if (Ensigns.Active(Ensigns.WireGuided) && ammo?._ap != null && ammo._ap._type == Ammunition.Type.Torpedo &&
                InFleet(AiShip(__instance)))
                __result = UnguidedTorpedoSalvo;
        }
    }

    /// <summary>
    /// With the Dutch ensign up, no one in the fleet is offered a SAM or air-to-air
    /// missile to fire. A launcher can be marked out of action, but a pylon cannot.
    /// </summary>
    [HarmonyPatch(typeof(AI), nameof(AI.AmmoForTarget))]
    static class NoAirMissiles
    {
        static readonly AccessTools.FieldRef<AI, ObjectBase> AiShip = AccessTools.FieldRefAccess<AI, ObjectBase>("_baseObject");

        static void Postfix(AI __instance, List<string> __result)
        {
            if (!Ensigns.Active(Ensigns.LongCiws) || __result == null || __result.Count == 0) return;
            var ship = AiShip(__instance);
            if (!InFleet(ship)) return;
            __result.RemoveAll(name => ship.getAmmunitionByName(name)?._ap is AmmunitionParameters ap && Boosts.AirToAir(ap));
        }
    }

    /// <summary>
    /// At ×100 a CIWS's tracers would each live for minutes, by the game's sum of
    /// three times its flight to full range. Its real hits are rolled, not flown,
    /// so a short-lived tracer costs nothing but keeps the effects pool small.
    /// </summary>
    [HarmonyPatch(typeof(WeaponSystemCIWS), nameof(WeaponSystemCIWS.OnFixedUpdate))]
    static class ShortTracers
    {
        const float MaxTracerSeconds = 8f;

        static void Postfix(WeaponSystemCIWS __instance)
        {
            if (__instance._projectileLifeTime > MaxTracerSeconds && Ensigns.Active(Ensigns.LongCiws))
                __instance._projectileLifeTime = MaxTracerSeconds;
        }
    }

    // Small guns' aim: per gun, where the Accuracy line works per crew.

    /// <summary>The surface-fire error reads its crew's multiplier; for a small gun that is lent a lower one for the call.</summary>
    [HarmonyPatch(typeof(WeaponSystemGun), "DeviationCheck")]
    static class SmallGunAim
    {
        static void Prefix(WeaponSystemGun __instance, Ammunition __0, out float? __state)
        {
            __state = null;
            var crew = __instance._baseObject?._crew;
            if (crew == null || !Ensigns.Active(Ensigns.SmallGuns) || !InFleet(__instance._baseObject) || !Ensigns.SmallCalibre(__0?._ap)) return;
            __state = crew._fcErrorMultiplier;
            crew._fcErrorMultiplier *= Ensigns.SmallGunAimFactor;
        }

        static void Finalizer(WeaponSystemGun __instance, float? __state)
        {
            if (__state is float was) __instance._baseObject._crew._fcErrorMultiplier = was;
        }
    }

    /// <summary>The same for the lead against aircraft, which the crew's multiplier never reaches.</summary>
    [HarmonyPatch(typeof(WeaponSystemGun), nameof(WeaponSystemGun.BuildAaErrorBudget),
        new[] { typeof(Ammunition), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float) })]
    static class SmallGunLead
    {
        static void Postfix(WeaponSystemGun __instance, Ammunition __0, ref WeaponSystemGun.AaErrorBudget __result)
        {
            if (Ensigns.Active(Ensigns.SmallGuns) && InFleet(__instance._baseObject) && Ensigns.SmallCalibre(__0?._ap))
                __result._predictionSigma *= Ensigns.SmallGunAimFactor;
        }
    }

    internal static void Reset()
    {
        foreach (var cloud in clouds)
            if (cloud.Smoke != null) Object.Destroy(cloud.Smoke);
        clouds.Clear();
        darkened.Clear();
        EcmFit.Reset();
    }

    // Who sails, and who else comes.

    /// <summary>
    /// The flights a launched run starts with. Double air wing: each of them
    /// twice, or if there are none a free fighter pair. Iron bombs: a free strike
    /// pair if none is sailing.
    /// </summary>
    internal static List<Escort> StartingFlights(Run run, Loadout loadout)
    {
        var flights = new List<Escort>(loadout.Flights);
        var navy = Store.StartingEscorts(loadout.Flagship).Where(e => e.IsAir).ToList();
        if (run.Hoisted.Contains(Ensigns.DoubleAir))
        {
            if (flights.Count == 0)
            {
                var fighters = navy.FirstOrDefault(e => (e.Roles & Role.AirCover) != 0);
                if (fighters != null) flights.Add(fighters);
            }
            flights.AddRange(flights.ToList());
        }
        if (run.Hoisted.Contains(Ensigns.IronBombs) && !flights.Any(e => (e.Roles & Role.AirStrike) != 0))
        {
            var strike = navy.FirstOrDefault(e => (e.Roles & Role.AirStrike) != 0);
            if (strike != null) flights.Add(strike);
        }
        return flights;
    }
}
