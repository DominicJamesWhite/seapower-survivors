using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
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
    const float PointBlankDamage = 6f;
    const float UnityPerNm = 27.559496f;

    static readonly AccessTools.FieldRef<Blastzone, ObjectBase> BlastPlatform = AccessTools.FieldRefAccess<Blastzone, ObjectBase>("_launchPlatform");
    static readonly AccessTools.FieldRef<Blastzone, AmmunitionParameters> BlastAmmo = AccessTools.FieldRefAccess<Blastzone, AmmunitionParameters>("_ap");
    static readonly AccessTools.FieldRef<Blastzone, WeaponBase> BlastWeapon = AccessTools.FieldRefAccess<Blastzone, WeaponBase>("_weapon");

    static bool InFleet(ObjectBase o) => o != null && Run.Current != null && Run.Current.Fleet.Contains(o);
    static bool EnemyShip(ObjectBase o) => o is Vessel && !o.IsDestroyed && o._taskforce == Globals._enemyTaskforce;

    /// <summary>The factor an ensign puts on our weapons of this kind. Guns' is shared with the shell-on-aircraft path.</summary>
    static float OurDamage(AmmunitionParameters ap)
    {
        float factor = 1f;
        if (Ensigns.Active(Ensigns.LastStand)) factor *= 1f + Ensigns.CloseCallPercent / 100f * Run.Current.CloseCalls;
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
    /// <summary>The radars a pulse switched off, so its end brings back those and not ones battle damage took out.</summary>
    static readonly Dictionary<ObjectBase, HashSet<SensorSystemRadar>> pulsedRadars = new Dictionary<ObjectBase, HashSet<SensorSystemRadar>>();

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
        if (!pulsedRadars.TryGetValue(ship, out var pulsed)) pulsedRadars[ship] = pulsed = new HashSet<SensorSystemRadar>();
        if (dark)
        {
            foreach (var radar in ship._obp._searchRadarSystems.Concat(ship._obp._targetingRadarSystems).Concat(ship._obp._airSearchRadars).Concat(ship._obp._surfaceSearchRadars))
                if (radar != null && !radar.Inoperable.Value)
                {
                    radar.Inoperable.Value = true;
                    pulsed.Add(radar);
                }
        }
        else
        {
            foreach (var radar in pulsed) radar.Inoperable.Value = false;
            pulsedRadars.Remove(ship);
        }
        // With its fire control down it holds fire, and Spawner will not re-alert it.
        ship._weaponStatus = dark ? ObjectBase.WeaponStatus.Hold : ObjectBase.WeaponStatus.Free;
    }

    static void TickDark()
    {
        double now = GameTime.missionElapsedTime;
        foreach (var (ship, until) in darkened.ToList())
        {
            if (ship == null || ship.IsDestroyed)
            {
                darkened.Remove(ship);
                pulsedRadars.Remove(ship);
            }
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
                // A launcher that also holds ASROC stays in action: NoMissiles keeps its missiles from being fired.
                foreach (var launcher in ship._obp._launcherWeapons.Where(Boosts.MissilesOnly))
                    launcher.Inoperable.Value = true;
        if (run.Hoisted.Contains(Ensigns.LongCiws)) ReportMounts(run);

        if (run.Hoisted.Contains(Ensigns.TorpedoEcm)) EcmFit.Tick(run);
        if (run.Hoisted.Contains(Ensigns.EmpMortars)) TickDark();
        if (run.Hoisted.Contains(Ensigns.WireGuided)) TickWires(run);
        if (run.Hoisted.Contains(Ensigns.LastStand)) TickCloseCalls(run);
    }

    static readonly AccessTools.FieldRef<Projectile, Vector3> ShellPosition = AccessTools.FieldRefAccess<Projectile, Vector3>("_currentPos");

    static double nextCloseCall;

    /// <summary>The Dannebrog: any enemy weapon near a ship of ours counts, gun rounds included, so a small gunboat let in close is worth the risk.</summary>
    static void TickCloseCalls(Run run)
    {
        double now = GameTime.missionElapsedTime;
        if (now < nextCloseCall) return;
        var ships = run.Fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit).ToList();
        if (!EnemyFireNear(ships)) return;
        nextCloseCall = now + Ensigns.CloseCallSeconds;
        run.CloseCalls++;
        TestPanel.Report($"{Ensigns.LastStand.Name}: close call, damage +{run.CloseCalls * Ensigns.CloseCallPercent}%");
    }

    static bool EnemyFireNear(List<ObjectBase> ships)
    {
        float reach = Ensigns.CloseCallNm * UnityPerNm;
        float reachSquared = reach * reach;
        bool Near(Vector3 at) => ships.Any(s => (s.transform.position - at).sqrMagnitude <= reachSquared);

        foreach (var weapon in Singleton<ObjectsManager>.Instance._listOfAllWeapons)
            if (weapon != null && !weapon.IsDestroyed && weapon._taskforce == Globals._enemyTaskforce && IsAttack(weapon) && Near(weapon.transform.position))
                return true;
        // Shells are not objects the game lists with the weapons.
        foreach (var shell in Singleton<ProjectileManager>.Instance._activeProjectiles)
            if (shell._taskforce == Globals._enemyTaskforce && shell.getState() == Projectile.State.Traveling && Near(ShellPosition(shell)))
                return true;
        return false;
    }

    /// <summary>False for the decoys and sonobuoys the game also keeps among its weapons.</summary>
    static bool IsAttack(WeaponBase weapon) =>
        !(weapon is ChaffCloud) && !(weapon is Noisemaker) && !(weapon is Bomb bomb && bomb._ap._subType == Ammunition.Type.Sonobuoy);

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
    /// With the Dutch ensign up, the AI is offered no missile to fire, so it picks
    /// what it can use instead. A launcher can be marked out of action, but a
    /// pylon cannot.
    /// </summary>
    [HarmonyPatch(typeof(AI), nameof(AI.AmmoForTarget))]
    static class NoMissiles
    {
        static readonly AccessTools.FieldRef<AI, ObjectBase> AiShip = AccessTools.FieldRefAccess<AI, ObjectBase>("_baseObject");

        static void Postfix(AI __instance, List<string> __result)
        {
            if (!Ensigns.Active(Ensigns.LongCiws) || __result == null || __result.Count == 0) return;
            var ship = AiShip(__instance);
            if (!InFleet(ship)) return;
            __result.RemoveAll(name => ship.getAmmunitionByName(name)?._ap is AmmunitionParameters ap && Ensigns.Withheld(ap));
        }
    }

    /// <summary>The same for orders from anywhere else: the player's own, and the game's strike missions, which pick their rounds themselves.</summary>
    [HarmonyPatch(typeof(ObjectBase), nameof(ObjectBase.AddEngageTask))]
    static class NoMissileOrders
    {
        static bool Prefix(ObjectBase __instance, EngageTask engageTask) =>
            !(engageTask?._ammunition?._ap is AmmunitionParameters ap && Ensigns.Withheld(ap) && InFleet(__instance));
    }

    /// <summary>
    /// At ×10 a CIWS's tracers would each live for minutes, by the game's sum of
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

    /// <summary>A ship's CIWS under the Dutch ensign; an aircraft's cannon keeps its own reach and rules.</summary>
    static bool LongReachMount(WeaponSystemCIWS ciws) =>
        Ensigns.Active(Ensigns.LongCiws) && !ciws._baseObject.IsAirUnit && InFleet(ciws._baseObject);

    /// <summary>
    /// The game holds a mount's fire at aircraft and missiles until they are close.
    /// With the range stretched ×10 that leaves it reading "Engaging" and silent
    /// for as long as the target stays out at range, so it fires once in reach.
    /// </summary>
    [HarmonyPatch(typeof(WeaponSystemCIWS), "HoldFireRangeSqUnity")]
    static class FireOnceInReach
    {
        static void Postfix(WeaponSystemCIWS __instance, ref float __result)
        {
            if (LongReachMount(__instance)) __result = float.MaxValue;
        }
    }

    /// <summary>
    /// A mount drops everything for a missile coming at its ship within twice its
    /// range, so at ×10 one distant missile held it for most of its flight. The
    /// game's "four times the squared range" is scaled back to the stock reach.
    /// </summary>
    [HarmonyPatch(typeof(WeaponSystemCIWS), "AquireTarget")]
    static class OnlyNearMissilesTakePriority
    {
        const float StockFactor = 4f;

        static float ThreatFactor(WeaponSystemCIWS ciws) =>
            LongReachMount(ciws) ? StockFactor / (Ensigns.CiwsReach * Ensigns.CiwsReach) : StockFactor;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var rangeField = AccessTools.Field(typeof(WeaponSystemCIWS), "_maxRangeSq");
            var list = instructions.ToList();
            for (int i = 1; i < list.Count; i++)
            {
                if (!list[i - 1].LoadsField(rangeField) || !list[i].LoadsConstant(StockFactor)) continue;
                list[i] = new CodeInstruction(OpCodes.Ldarg_0);
                list.Insert(i + 1, CodeInstruction.Call(typeof(OnlyNearMissilesTakePriority), nameof(ThreatFactor)));
                return list;
            }
            Entry.Log.LogWarning("Dutch ensign: missile priority reach not found in AquireTarget; distant missiles will still hold the CIWS");
            return list;
        }
    }

    const double MountReportSeconds = 30.0;
    static double nextMountReport;

    /// <summary>What each CIWS is doing, every 30 s of game time, as nothing else in the log says.</summary>
    static void ReportMounts(Run run)
    {
        double now = GameTime.missionElapsedTime;
        if (now < nextMountReport) return;
        nextMountReport = now + MountReportSeconds;
        foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit && s._obp._ciwsWeapons.Count > 0))
            Entry.Log.LogInfo($"CIWS on {ship.getUIDAndName()}: {string.Join("; ", ship._obp._ciwsWeapons.Select(DescribeMount))}");
    }

    static string DescribeMount(WeaponSystemCIWS ciws)
    {
        string ammo = ciws._ammoForEngage?._ap?._ammunitionFileName;
        int rounds = ammo == null ? 0 : ciws.getLoadedAmmoCount(ammo);
        var target = ciws._currentClosestTarget;
        string aim = target == null ? "no target"
            : $"{target.getUIDAndName()} at {Vector3.Distance(target.transform.position, ciws._mountObject.transform.position) / UnityPerNm:0.0} nm";
        return $"{ciws._vwp._systemName} {(ciws._isFiring ? "firing" : "not firing")}, {aim}, {rounds} rounds, status {ciws.GetStatus()}";
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
            var run = Run.Current;
            if (crew == null || run == null || !InFleet(__instance._baseObject)) return;
            // The Guns line's own aiming shares this hook: mortars read the crew's multiplier, so it can only be raised while a gun aims.
            float factor = Boosts.GunAimFactor(run) * (Ensigns.Active(Ensigns.LongCiws) ? Ensigns.DutchAimFactor : 1f);
            if (factor == 1f) return;
            __state = crew._fcErrorMultiplier;
            crew._fcErrorMultiplier *= factor;
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

    internal static void Reset()
    {
        ignored = null;
        nextCloseCall = 0.0;
        nextMountReport = 0.0;
        darkened.Clear();
        pulsedRadars.Clear();
        EcmFit.Reset();
    }

    // Who sails, and who else comes.

    /// <summary>
    /// The flights a launched run starts with. Iron bombs: a free strike pair if
    /// none is sailing.
    /// </summary>
    internal static List<Escort> StartingFlights(Run run, Loadout loadout)
    {
        var flights = new List<Escort>(loadout.Flights);
        if (FreeStrikeFlight(run.Hoisted, flights, loadout.Flagship) is Escort strike) flights.Add(strike);
        return flights;
    }

    /// <summary>The Argentine ensign's free strike pair, or null if the ensign is not flying or a strike flight already sails.</summary>
    internal static Escort FreeStrikeFlight(IEnumerable<Ensign> hoisted, IEnumerable<Escort> flights, StartingShip flagship)
    {
        if (!hoisted.Contains(Ensigns.IronBombs) || flights.Any(e => e.IsAir && (e.Roles & Role.AirStrike) != 0)) return null;
        return Store.StartingEscorts(flagship).FirstOrDefault(e => e.IsAir && (e.Roles & Role.AirStrike) != 0);
    }
}
