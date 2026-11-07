using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// What ensigns do during the fight, rather than to a weapon's numbers (Boosts
/// writes those): damage by who fired and at whom, ignored hits, EMP mortars,
/// wires, and the things an ensign keeps out of action.
/// </summary>
static class EnsignCombat
{
    const float HeavyGunDamage = 4f, HeavyMissileDamage = 2f, BombDamage = 10f, TorpedoDamage = 1.5f;
    const float DamagePerCloseCall = 0.1f, PointBlankDamage = 3f;

    static readonly AccessTools.FieldRef<Blastzone, ObjectBase> BlastPlatform = AccessTools.FieldRefAccess<Blastzone, ObjectBase>("_launchPlatform");
    static readonly AccessTools.FieldRef<Blastzone, AmmunitionParameters> BlastAmmo = AccessTools.FieldRefAccess<Blastzone, AmmunitionParameters>("_ap");
    static readonly AccessTools.FieldRef<Blastzone, WeaponBase> BlastWeapon = AccessTools.FieldRefAccess<Blastzone, WeaponBase>("_weapon");

    static bool InFleet(ObjectBase o) => o != null && Run.Current != null && Run.Current.Fleet.Contains(o);
    static bool EnemyShip(ObjectBase o) => o is Vessel && !o.IsDestroyed && o._taskforce == Globals._enemyTaskforce;

    /// <summary>The factor an ensign puts on our weapons of this kind. Guns' is shared with the shell-on-aircraft path.</summary>
    static float OurDamage(AmmunitionParameters ap)
    {
        float factor = 1f;
        if (Ensigns.Active(Ensigns.LastStand)) factor *= 1f + DamagePerCloseCall * Run.Current.CloseCalls;
        switch (ap._type)
        {
            case Ammunition.Type.Projectile:
                if (Ensigns.Active(Ensigns.HeavyGuns)) factor *= HeavyGunDamage;
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
            if (!InFleet(platform)) return;
            var ap = BlastAmmo(__instance);
            __result *= OurDamage(ap);
            if (__0 is Vessel && ap._type == Ammunition.Type.Projectile && Ensigns.Active(Ensigns.PointBlank))
                __result *= PointBlankFactor(platform, __0, ap);
        }
    }

    /// <summary>The Icelandic ensign: the full factor at no distance, falling evenly to none at the gun's greatest range, whatever has stretched it.</summary>
    static float PointBlankFactor(ObjectBase gun, ObjectBase target, AmmunitionParameters ap)
    {
        float range = ap._maxLaunchRangeInMiles;
        if (range <= 0f) return 1f;
        return Mathf.Lerp(PointBlankDamage, 1f, (float)gun._geoPosition.getDistanceInMiles(target._geoPosition) / range);
    }

    static bool SpendsCharge(AmmunitionParameters ap) =>
        ap != null && (ap._type == Ammunition.Type.Missile || ap._type == Ammunition.Type.Bomb || ap._type == Ammunition.Type.Torpedo);

    /// <summary>The last blast the flagship ignored: one explosion can reach it more than once, and is only one hit.</summary>
    static Blastzone ignored;

    /// <summary>
    /// The Turkish ensign: an enemy missile, bomb or torpedo the flagship has a
    /// charge to ignore, whether it strikes the hull or goes off close enough to
    /// harm it. Shells do not spend one. The hit's whole calculation is skipped,
    /// so it leaves no fire or flooding either, and it records no impact for the
    /// hidden integrity drain to act on.
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
            if (__0 != run.Ship || !run.Hoisted.Contains(Ensigns.HitProof) || InFleet(BlastPlatform(__instance))) return true;
            if (__instance == ignored) return false;
            if (run.HitsToIgnore <= 0 || !SpendsCharge(BlastAmmo(__instance))) return true;
            run.HitsToIgnore--;
            ignored = __instance;
            TestPanel.Report($"{Ensigns.HitProof.Name}: the flagship ignores a hit, {run.HitsToIgnore} left");
            return false;
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
        int rank = Run.Current.RankOf(Upgrades.Mortars);
        double until = GameTime.missionElapsedTime + Ensigns.EmpSeconds(rank);
        float reach = Ensigns.EmpNm(rank);
        foreach (var ship in Singleton<ObjectsManager>.Instance._listOfAllVesselObjects)
        {
            if (!EnemyShip(ship) || (float)ship._geoPosition.getDistanceInMiles(at) > reach) continue;
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
        if (run.Hoisted.Contains(Ensigns.LongCiws))
            foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit))
                // A launcher that also holds Harpoons, ASROC or Tomahawks stays in action: NoAirMissiles keeps its SAMs from being fired.
                foreach (var launcher in Boosts.SamLaunchers(ship).Where(l => Boosts.LauncherAmmo(l).All(Boosts.AirToAir)))
                    launcher.Inoperable.Value = true;

        if (run.Hoisted.Contains(Ensigns.TorpedoEcm)) EcmFit.Tick(run);
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
            if (__instance._projectileLifeTime > MaxTracerSeconds && Ensigns.Active(Ensigns.LongCiws) && InFleet(__instance._baseObject))
                __instance._projectileLifeTime = MaxTracerSeconds;
        }
    }

    // The Dutch ensign's guns: fleet guns only, where the Accuracy line works per crew and so also reaches anything else it steers.

    /// <summary>The surface-fire error reads its crew's multiplier; for a fleet gun that is lent a lower one for the call.</summary>
    [HarmonyPatch(typeof(WeaponSystemGun), "DeviationCheck")]
    static class DutchGunAim
    {
        static void Prefix(WeaponSystemGun __instance, out float? __state)
        {
            __state = null;
            var crew = __instance._baseObject?._crew;
            if (crew == null || !Ensigns.Active(Ensigns.LongCiws) || !InFleet(__instance._baseObject)) return;
            __state = crew._fcErrorMultiplier;
            crew._fcErrorMultiplier *= Ensigns.DutchAimFactor;
        }

        static void Finalizer(WeaponSystemGun __instance, float? __state)
        {
            if (__state is float was) __instance._baseObject._crew._fcErrorMultiplier = was;
        }
    }

    /// <summary>The same for the lead against aircraft, which the crew's multiplier never reaches.</summary>
    [HarmonyPatch(typeof(WeaponSystemGun), nameof(WeaponSystemGun.BuildAaErrorBudget),
        new[] { typeof(Ammunition), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float) })]
    static class DutchGunLead
    {
        static void Postfix(WeaponSystemGun __instance, ref WeaponSystemGun.AaErrorBudget __result)
        {
            if (!Ensigns.Active(Ensigns.LongCiws) || !InFleet(__instance._baseObject)) return;
            // The director's errors. The gun's own dispersion and the target's evasion are not aim.
            __result._angularSigma *= Ensigns.DutchAimFactor;
            __result._laySigma *= Ensigns.DutchAimFactor;
            __result._rangeSigma *= Ensigns.DutchAimFactor;
            __result._predictionSigma *= Ensigns.DutchAimFactor;
            __result._solutionBiasSigma *= Ensigns.DutchAimFactor;
        }
    }

    /// <summary>
    /// The Indian ensign: twice the anti-ship missiles at each launch. The AI's
    /// auto-attack fires them one at a time, so this makes two.
    /// </summary>
    [HarmonyPatch(typeof(AI), "AutoAttackSalvoSize")]
    static class DoubleSalvo
    {
        static readonly AccessTools.FieldRef<AI, ObjectBase> AiShip = AccessTools.FieldRefAccess<AI, ObjectBase>("_baseObject");

        static void Postfix(AI __instance, Ammunition ammo, ref int __result)
        {
            // The target type alone would take in bombs, rockets and shells too.
            if (Ensigns.Active(Ensigns.Salvos) && ammo?._ap != null && ammo._ap._type == Ammunition.Type.Missile && Boosts.AntiShipMissile(ammo._ap) && InFleet(AiShip(__instance)))
                __result *= 2;
        }
    }

    internal static void Reset()
    {
        ignored = null;
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
