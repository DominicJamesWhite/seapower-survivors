using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// The unlocks that act during the fight rather than on a weapon's numbers:
/// blasts around a kill, and what happens to a missile our chaff fools.
/// Boosts writes the ones that are only numbers.
/// </summary>
static class Abilities
{
    const float MetresToUnity = 0.0148809375f;
    const float HugeBlastM = 150f, FlakRoundsM = 25f, FlakStormM = 75f;

    /// <summary>One of our SAMs has just destroyed this target.</summary>
    internal static void SamKill(ObjectBase target)
    {
        var run = Run.Current;
        if (run == null || !Upgrades.Has(run, Upgrades.HugeBlasts)) return;
        Blast(target.transform.position, HugeBlastM, aircraft: true, target);
    }

    /// <summary>One of our CIWS mounts has just destroyed this target.</summary>
    internal static void CiwsKill(ObjectBase target)
    {
        var run = Run.Current;
        if (run == null) return;
        if (Upgrades.Has(run, Upgrades.FlakStorm)) Blast(target.transform.position, FlakStormM, aircraft: true, target);
        else if (Upgrades.Has(run, Upgrades.FlakRounds)) Blast(target.transform.position, FlakRoundsM, aircraft: false, target);
    }

    /// <summary>
    /// Destroys every enemy missile, and aircraft if asked, within this many
    /// metres. Nothing here makes a Blastzone, so a blast never sets off another.
    /// </summary>
    static void Blast(Vector3 at, float metres, bool aircraft, ObjectBase cause)
    {
        float r = metres * MetresToUnity;
        // A snapshot: destruction is deferred, but the list is the game's own.
        var caught = Singleton<ObjectsManager>.Instance._listOfAllObjects
            .Where(o => o != null && o != cause && !o.IsDestroyed && !o._externalDestructionNotified &&
                        o._taskforce == Globals._enemyTaskforce &&
                        (o is Missile || aircraft && o.IsAirUnit) &&
                        (o.transform.position - at).sqrMagnitude <= r * r)
            .ToList();
        foreach (var o in caught) CombatHooks.Destroy(o);
        if (caught.Count > 0) TestPanel.Report($"Blast {metres:0} m: {caught.Count} more destroyed");
    }

    static bool Enemy(ObjectBase o) => o != null && o._taskforce == Globals._enemyTaskforce;
    static bool OurChaff(ObjectBase o) => o is ChaffCloud && o._taskforce == Globals._playerTaskforce;

    static readonly AccessTools.FieldRef<SeekerBase, WeaponBase> SeekerAmmo = AccessTools.FieldRefAccess<SeekerBase, WeaponBase>("_ammoObject");
    static readonly AccessTools.FieldRef<SeekerBase, double> CountermeasureCheckTime = AccessTools.FieldRefAccess<SeekerBase, double>("_countermeasureCheckTime");
    static readonly AccessTools.FieldRef<WeaponSystemChaff, bool> ChaffReloadInProgress = AccessTools.FieldRefAccess<WeaponSystemChaff, bool>("_isReloadInProgress");

    /// <summary>The game lets a missile test against chaff once in this long; quick bloom shortens it.</summary>
    const double StockCheckSeconds = 35.0, QuickBloomSeconds = 5.0;
    /// <summary>A burnt-out missile chases the cloud this long first, so it is seen to be fooled.</summary>
    const double BurnOutDelay = 1.5;

    /// <summary>Spoofed missiles waiting to burn out, and when.</summary>
    static readonly Dictionary<WeaponBase, double> burning = new Dictionary<WeaponBase, double>();
    /// <summary>Spoofed missiles turned on the enemy, and the ship each now hunts.</summary>
    static readonly Dictionary<WeaponBase, ObjectBase> turncoats = new Dictionary<WeaponBase, ObjectBase>();

    internal static void Reset()
    {
        burning.Clear();
        turncoats.Clear();
    }

    /// <summary>An enemy missile our chaff has just fooled.</summary>
    static void Spoofed(SeekerBase seeker, WeaponBase missile)
    {
        var run = Run.Current;
        if (run == null || burning.ContainsKey(missile) || turncoats.ContainsKey(missile)) return;
        // Only a missile homing on its own radar can be turned; one steered
        // by its launcher's radar follows that, so it burns out instead.
        var prey = Upgrades.Has(run, Upgrades.Turncoat) && seeker is ActiveRadarSeeker ? NearestEnemyShip(missile) : null;
        if (prey != null)
        {
            turncoats[missile] = prey;
            TestPanel.Report($"{missile._ap._natoDisplayedName} turned on {prey.Name.Value}");
        }
        else if (Upgrades.Has(run, Upgrades.BurnOut) || Upgrades.Has(run, Upgrades.Turncoat))
            burning[missile] = GameTime.missionElapsedTime + BurnOutDelay;
    }

    static ObjectBase NearestEnemyShip(WeaponBase missile) =>
        Singleton<ObjectsManager>.Instance._listOfAllObjects
            .Where(o => o is Vessel && Enemy(o) && !o.IsDestroyed)
            .OrderBy(o => (o.transform.position - missile.transform.position).sqrMagnitude)
            .FirstOrDefault();

    [HarmonyPatch(typeof(SeekerBase), nameof(SeekerBase.CheckCountermeasureLock), typeof(ObjectBase))]
    static class ChaffTest
    {
        static void Prefix(SeekerBase __instance, ObjectBase __0, out ObjectBase __state)
        {
            __state = __instance._lockedOnCountermeasure;
            var run = Run.Current;
            if (run == null || !OurChaff(__0) || !Enemy(SeekerAmmo(__instance)) || !Upgrades.Has(run, Upgrades.QuickBloom)) return;
            // Make the game's 35 s wait read as already over once 5 s have gone.
            double since = GameTime.missionElapsedTime - CountermeasureCheckTime(__instance);
            if (since >= QuickBloomSeconds && since < StockCheckSeconds)
                CountermeasureCheckTime(__instance) = GameTime.missionElapsedTime - StockCheckSeconds;
        }

        static void Postfix(SeekerBase __instance, ObjectBase __0, bool __result, ObjectBase __state)
        {
            // True every frame while locked, and for anything not a decoy: only a new lock on our chaff counts.
            if (!__result || __state == __0 || __instance._lockedOnCountermeasure != __0 || !OurChaff(__0)) return;
            var missile = SeekerAmmo(__instance);
            if (Enemy(missile)) Spoofed(__instance, missile);
        }
    }

    [HarmonyPatch(typeof(SeekerBase), nameof(SeekerBase.update))]
    static class SeekerTick
    {
        static void Prefix(SeekerBase __instance)
        {
            var missile = SeekerAmmo(__instance);
            var run = Run.Current;
            if (run == null || !Enemy(missile)) return;

            if (burning.TryGetValue(missile, out double at) && GameTime.missionElapsedTime >= at)
            {
                burning.Remove(missile);
                CombatHooks.Destroy(missile);
                return;
            }

            if (turncoats.TryGetValue(missile, out var prey))
            {
                if (prey == null || prey.IsDestroyed)
                {
                    turncoats.Remove(missile);
                    return;
                }
                // Steer for the prey until the seeker finds it; our own ships
                // and chaff are taken out of what it can see.
                __instance._lockedOnCountermeasure = null;
                __instance._spoofEndTime = -1.0;
                __instance._firstLockedOnTarget = null;
                __instance._keepTarget = true;
                __instance._originalTargetPosition = prey.getGeoPosition();
                __instance._originalTargetVelocity = Vector3.zero;
                if (missile.CurrentIntendedTargetObject != prey)
                {
                    __instance._strongestSensorEcho = null;
                    missile.CurrentIntendedTargetObject = prey;
                }
                missile.AimPointGeoPosition = prey.getGeoPosition();
                return;
            }

            // Quick bloom: forget that our clouds were already ruled on, so
            // they are tested again once the shorter wait is over.
            if (Upgrades.Has(run, Upgrades.QuickBloom) &&
                GameTime.missionElapsedTime - CountermeasureCheckTime(__instance) >= QuickBloomSeconds)
                __instance._ignoredObjects.RemoveAll(OurChaff);
        }
    }

    [HarmonyPatch(typeof(ActiveRadarSeeker), "createListOfPotentialContacts")]
    static class TurncoatSight
    {
        static void Postfix(ActiveRadarSeeker __instance)
        {
            if (turncoats.ContainsKey(SeekerAmmo(__instance)))
                __instance._potentialContacts.RemoveAll(o => o != null && o._taskforce == Globals._playerTaskforce);
        }
    }

    static readonly AccessTools.FieldRef<WeaponSystemCIWS, float> CiwsSigma = AccessTools.FieldRefAccess<WeaponSystemCIWS, float>("_sigmaFcsMissileMrad");
    static readonly AccessTools.FieldRef<WeaponSystemCIWS, float> CiwsDamageMissile = AccessTools.FieldRefAccess<WeaponSystemCIWS, float>("_damageMultMissile");
    static readonly AccessTools.FieldRef<WeaponSystemCIWS, float> CiwsDamageAircraft = AccessTools.FieldRefAccess<WeaponSystemCIWS, float>("_damageMultAircraft");
    const float HeavyRoundsFactor = 3f;

    /// <summary>
    /// Heavy rounds. The CIWS fits its damage to match its intercept chance,
    /// so the boost goes on after each fit; a refit starts from fresh values,
    /// so it never compounds.
    /// </summary>
    [HarmonyPatch(typeof(WeaponSystemCIWS), "EnsureCalibrated")]
    static class HeavyRounds
    {
        static void Prefix(WeaponSystemCIWS __instance, out bool __state) => __state = CiwsSigma(__instance) < 0f;

        static void Postfix(WeaponSystemCIWS __instance, bool __state)
        {
            var run = Run.Current;
            if (!__state || CiwsSigma(__instance) < 0f || run == null || !run.Fleet.Contains(__instance._baseObject) ||
                !Upgrades.Has(run, Upgrades.HeavyRounds)) return;
            CiwsDamageMissile(__instance) *= HeavyRoundsFactor;
            CiwsDamageAircraft(__instance) *= HeavyRoundsFactor;
        }
    }

    static bool InFleet(ObjectBase o) => o != null && Run.Current != null && Run.Current.Fleet.Contains(o);
    static bool Unlocked(Unlock unlock) => Run.Current != null && Upgrades.Has(Run.Current, unlock);

    /// <summary>
    /// The game caches one fire table per ammo name, shared with enemy guns
    /// firing the same round, so our longer-ranged or faster shells get
    /// their own, rebuilt whenever their range or speed changes.
    /// </summary>
    [HarmonyPatch(typeof(GunBallistics), nameof(GunBallistics.GetTable))]
    static class OwnFireTable
    {
        sealed class Cached
        {
            public float Range, Speed;
            public GunBallistics.FireTable Table;
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AmmunitionParameters, Cached> ours =
            new System.Runtime.CompilerServices.ConditionalWeakTable<AmmunitionParameters, Cached>();
        static readonly System.Reflection.MethodInfo Build = AccessTools.Method(typeof(GunBallistics), "BuildTable");

        static bool Prefix(AmmunitionParameters __0, ref GunBallistics.FireTable __result)
        {
            var ap = __0;
            if (ap == null || !Boosts.IsBoostedShell(ap)) return true;
            var entry = ours.GetOrCreateValue(ap);
            if (entry.Table == null || entry.Range != ap._launchRangesInUnity.y || entry.Speed != ap._muzzleVelocityInUnity)
            {
                entry.Table = (GunBallistics.FireTable)Build.Invoke(null, new object[] { ap });
                entry.Range = ap._launchRangesInUnity.y;
                entry.Speed = ap._muzzleVelocityInUnity;
            }
            __result = entry.Table;
            return false;
        }
    }

    /// <summary>
    /// Snap fire: the AI skips missiles less than 5 s into their flight. The
    /// constant is swapped for a call that answers 0 for our ships.
    /// </summary>
    [HarmonyPatch(typeof(AI), "GetPossibleTargetsList")]
    static class SnapFire
    {
        static readonly AccessTools.FieldRef<AI, ObjectBase> AiShip = AccessTools.FieldRefAccess<AI, ObjectBase>("_baseObject");

        static float YoungMissileAge(AI ai) => InFleet(AiShip(ai)) && Unlocked(Upgrades.SnapFire) ? 0f : 5f;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var dt = AccessTools.Field(typeof(WeaponBase), nameof(WeaponBase._dt));
            var list = instructions.ToList();
            int swapped = 0;
            for (int i = 1; i < list.Count; i++)
            {
                if (!list[i - 1].LoadsField(dt) || !(list[i].opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && list[i].operand is float f && f == 5f)) continue;
                list[i] = new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0).MoveLabelsFrom(list[i]);
                list.Insert(i + 1, CodeInstruction.Call(typeof(SnapFire), nameof(YoungMissileAge)));
                swapped++;
            }
            if (swapped == 0) Entry.Log.LogWarning("Snap fire: the 5 s missile-age check was not found; the ability does nothing");
            return list;
        }
    }

    /// <summary>Full plot: no limit on new contacts our ships work up at once.</summary>
    [HarmonyPatch]
    static class FullPlot
    {
        const int Unlimited = 99;

        static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new[]
        {
            AccessTools.Method(typeof(ReactionTime), nameof(ReactionTime.ConcurrentOrientLimit)),
            AccessTools.Method(typeof(ReactionTime), nameof(ReactionTime.FreeOrientSlots)),
            AccessTools.Method(typeof(ReactionTime), "OrientSlots"),
        };

        static void Postfix(ObjectBase __0, ref int __result)
        {
            if (InFleet(__0) && Unlocked(Upgrades.FullPlot)) __result = Unlimited;
        }
    }

    const float CritChance = 0.2f, CritFactor = 5f;
    static readonly AccessTools.FieldRef<Blastzone, Projectile> BlastProjectile = AccessTools.FieldRefAccess<Blastzone, Projectile>("_projectile");
    /// <summary>Shell blasts on a hull that rolled a critical hit.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Blastzone, object> criticalBlasts =
        new System.Runtime.CompilerServices.ConditionalWeakTable<Blastzone, object>();

    static bool RollCritical(Projectile shell) =>
        shell != null && InFleet(shell._launchPlatform) && Unlocked(Upgrades.CriticalHits) && Random.value < CritChance;

    /// <summary>Critical hits on ships: a shell's hull hit becomes a blast, rolled once as it is made.</summary>
    [HarmonyPatch(typeof(Blastzone), MethodType.Constructor, typeof(Projectile), typeof(Vector3))]
    static class CriticalBlast
    {
        static void Postfix(Blastzone __instance, Projectile __0)
        {
            // A splash in the water makes a blast too; only a hull hit has a displacement.
            if (__0 != null && __0._hitShipDisplacement > 0f && RollCritical(__0))
                criticalBlasts.Add(__instance, null);
        }
    }

    [HarmonyPatch(typeof(Blastzone), "GetWeaponDamage")]
    static class CriticalBlastDamage
    {
        static void Postfix(Blastzone __instance, ref float __result)
        {
            if (criticalBlasts.TryGetValue(__instance, out _)) __result *= CritFactor;
        }
    }

    /// <summary>
    /// Critical hits on aircraft and missiles, whose damage is worked out
    /// inside the shell's collision. CIWS uses the same damage roll, so only
    /// a shell flagged here is boosted.
    /// </summary>
    [HarmonyPatch(typeof(Projectile), "ProcessCollision")]
    static class CriticalShell
    {
        internal static bool active;

        static void Prefix(Projectile __instance) => active = RollCritical(__instance);
        static void Finalizer() => active = false;
    }

    [HarmonyPatch(typeof(CiwsBallistics), nameof(CiwsBallistics.VariedHitDamage))]
    static class CriticalShellDamage
    {
        static void Postfix(ref float __result)
        {
            if (CriticalShell.active) __result *= CritFactor;
        }
    }

    static readonly AccessTools.FieldRef<Projectile, double> ShellFireTime = AccessTools.FieldRefAccess<Projectile, double>("_fireTime");
    /// <summary>Smart shells steer over this last stretch of flight, at most this fast.</summary>
    const float SmartShellSeconds = 2f, SmartShellSteerMs = 12f;

    /// <summary>
    /// Smart shells. A shell slides along a path to the aim point picked
    /// when it was fired, so steering moves the aim point towards where the
    /// target will be, a little each tick, as the game's laser guidance does.
    /// The shells ride the beam, as DART does: they steer only while one of
    /// their gun's radar directors still tracks the target, so a gun without
    /// a radar, a director knocked out or a mount that has moved on to
    /// another target leaves its shells in the air unguided.
    /// </summary>
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.MoveProjectile))]
    static class SmartShells
    {
        static void Prefix(Projectile __instance)
        {
            var target = __instance._targetObject;
            if (target == null || target.IsDestroyed || !InFleet(__instance._launchPlatform) || !Unlocked(Upgrades.SmartShells)) return;
            float remaining = (float)(__instance._toTargetTime - (GameTime.missionElapsedTime - ShellFireTime(__instance)));
            if (remaining <= 0f || remaining > SmartShellSeconds || !BeamOn(__instance._ws, target)) return;

            Vector3 aim = __instance._projectileAimGeoPosition.ToUnity();
            Vector3 desired = target.transform.position + target.UnityVelocityVector * remaining;
            // Against a ship, keep the height the shell was aimed at.
            if (!target.IsAirUnit) desired.y = aim.y;
            aim = Vector3.MoveTowards(aim, desired, SmartShellSteerMs * MetresToUnity * GameTime.fixedDeltaTime);
            __instance._projectileAimGeoPosition.FromUnity(aim);
        }

        static bool BeamOn(WeaponSystem gun, ObjectBase target) =>
            gun?._vwp?._associatedSensors != null &&
            gun._vwp._associatedSensors.Any(s => s is SensorSystemRadar && !s.Inoperable.Value && s.IsOn.Value && s.IsAimedAtTarget(target));
    }

    /// <summary>Overcharge: a second cloud straight after the first, from the next loaded round.</summary>
    [HarmonyPatch(typeof(WeaponSystemChaff), nameof(WeaponSystemChaff.launchChaff))]
    static class DoubleCloud
    {
        static bool launching;

        static void Prefix(WeaponSystemChaff __instance, out bool __state) => __state = ChaffReloadInProgress(__instance);

        static void Postfix(WeaponSystemChaff __instance, bool __state)
        {
            var run = Run.Current;
            if (launching || __state || !ChaffReloadInProgress(__instance) || run == null ||
                __instance._baseObject?._taskforce != Globals._playerTaskforce || !Upgrades.Has(run, Upgrades.Overcharge)) return;
            launching = true;
            try
            {
                ChaffReloadInProgress(__instance) = false;
                __instance.launchChaff();
            }
            finally
            {
                ChaffReloadInProgress(__instance) = true;
                launching = false;
            }
        }
    }
}
