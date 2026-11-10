using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// Where the run hears about the fight: unit kills and missile intercepts,
/// both paid out as XP.
/// </summary>
/// <remarks>
/// Patched methods take positional arguments (__0) because the decompiled
/// parameter names are not guaranteed to match the shipped assembly.
/// </remarks>
static class CombatHooks
{
    static readonly AccessTools.FieldRef<Blastzone, ObjectBase> BlastLaunchPlatform =
        AccessTools.FieldRefAccess<Blastzone, ObjectBase>("_launchPlatform");

    /// <summary>
    /// Enemy missiles something of ours has hit: SAM blast, CIWS or gun shell. A missile's destruction does
    /// not say why it died, so a hit is remembered here and paid out when the
    /// missile goes; one that strikes us or is decoyed into the sea pays nothing.
    /// </summary>
    static readonly HashSet<int> struckMissiles = new HashSet<int>();

    /// <summary>Enemy missiles already paid for while still flying: one a jammer has sent into the sea. Nothing pays for them twice.</summary>
    static readonly HashSet<int> paidMissiles = new HashSet<int>();

    /// <summary>Pays for a missile stopped without being destroyed, and remembers it until it is gone.</summary>
    internal static void PayIntercept(Run run, WeaponBase missile)
    {
        if (!paidMissiles.Add(missile.UniqueID)) return;
        run.Intercepts++;
        run.AddXp(Progression.MissileIntercept);
    }

    static bool IsEnemyWeapon(object o) => o is WeaponBase w && w._taskforce == Globals._enemyTaskforce;

    static bool IsOurs(ObjectBase o) => o != null && o._taskforce == Globals._playerTaskforce;

    [HarmonyPatch(typeof(ObjectBase), nameof(ObjectBase.setDestroyedFlag))]
    static class UnitKilled
    {
        static void Prefix(ObjectBase __instance, out bool __state) => __state = __instance.IsDestroyed;

        static void Postfix(ObjectBase __instance, bool __0, bool __state)
        {
            bool score = __0;
            if (__state || !__instance.IsDestroyed || !score || !__instance.isUnit()) return;
            if (__instance._taskforce != Globals._enemyTaskforce) return;
            // A sunk ship's life raft counts as a unit, but sinking it is no feat.
            if (__instance._obp?._objectIniName?.Contains("life_raft") == true) return;

            DamageLog.Sunk(__instance);
            var run = Run.Current;
            float xp = Progression.ForUnit(__instance);
            if (run != null)
            {
                run.Kills++;
                run.AddXp(xp);
            }
            string by = __instance.IsAirUnit ? AaTrace.LastHitBy(__instance) : null;
            Entry.Log.LogInfo($"Destroyed: {__instance.Name.Value} ({__instance._obp?._objectIniName}), +{xp:F0} XP" +
                              (__instance.IsAirUnit ? $", last shell hit by {by ?? "none of our guns"}" : ""));
        }
    }

    static readonly AccessTools.FieldRef<Blastzone, WeaponBase> BlastWeapon =
        AccessTools.FieldRefAccess<Blastzone, WeaponBase>("_weapon");

    static bool Alive(ObjectBase o) => o != null && !o.IsDestroyed && !o._externalDestructionNotified;

    /// <summary>True for a blast from one of our SAMs: a missile fired at an air target.</summary>
    static bool OurSam(Blastzone blast) =>
        IsOurs(BlastLaunchPlatform(blast)) && BlastWeapon(blast) is Missile m && m._ap._targetType == Ammunition.Target.AAW;

    /// <summary>
    /// Weapons a blast destroys: a SAM's intercept, and its splash. OnHitUnit
    /// only ever sees ships, aircraft and the like, so missiles land here.
    /// </summary>
    [HarmonyPatch(typeof(Blastzone), "OnHitWeapon")]
    static class BlastHitWeapon
    {
        static void Prefix(Blastzone __instance, WeaponBase __0, out bool __state)
        {
            __state = Alive(__0);
            if (__state && IsEnemyWeapon(__0) && IsOurs(BlastLaunchPlatform(__instance)))
                struckMissiles.Add(__0.UniqueID);
        }

        static void Postfix(Blastzone __instance, WeaponBase __0, bool __state)
        {
            if (__state && !Alive(__0) && IsEnemyWeapon(__0) && OurSam(__instance)) Abilities.SamKill(__0);
        }
    }

    /// <summary>An aircraft our SAM brings down: the huge blast goes off there too.</summary>
    [HarmonyPatch(typeof(Blastzone), "OnHitUnit")]
    static class BlastHitUnit
    {
        static void Prefix(object __0, out bool __state) => __state = __0 is ObjectBase o && o.IsAirUnit && Alive(o);

        static void Postfix(Blastzone __instance, object __0, bool __state)
        {
            var target = (ObjectBase)__0;
            if (__state && !Alive(target) && target._taskforce == Globals._enemyTaskforce && OurSam(__instance))
                Abilities.SamKill(target);
        }
    }

    [HarmonyPatch(typeof(WeaponSystemCIWS), nameof(WeaponSystemCIWS.InterceptAirTarget))]
    static class CiwsHit
    {
        static void Prefix(WeaponSystemCIWS __instance, object __0, out bool __state)
        {
            var target = __0 as ObjectBase;
            // The game returns early for a target already dying; only a real kill counts.
            __state = Alive(target) && IsOurs(__instance._baseObject) && target._taskforce == Globals._enemyTaskforce;
            if (__state && IsEnemyWeapon(target)) struckMissiles.Add(target.UniqueID);
        }

        static void Postfix(object __0, bool __state)
        {
            if (__state) Abilities.CiwsKill((ObjectBase)__0);
        }
    }

    /// <summary>
    /// Destroys an enemy missile or aircraft the way the game's own kills do.
    /// A missile pays intercept XP when it goes; an aircraft pays through
    /// UnitKilled, as any other kill.
    /// </summary>
    internal static void Destroy(ObjectBase target)
    {
        if (!Alive(target)) return;
        if (target is WeaponBase) struckMissiles.Add(target.UniqueID);
        target.notifyOfExternalDestruction();
    }

    /// <summary>
    /// Gun shells, flak bursts included: a 100 mm round fused near a missile
    /// lands here, not in a Blastzone, so it needs its own hook.
    /// </summary>
    [HarmonyPatch(typeof(Projectile), "ProcessCollision")]
    static class ShellHit
    {
        static void Prefix(Projectile __instance, UnityEngine.RaycastHit __0)
        {
            if (!IsOurs(__instance._launchPlatform) || __0.transform == null) return;
            var hit = __0.transform.root.GetComponentInChildren<ObjectBase>();
            if (IsEnemyWeapon(hit)) struckMissiles.Add(hit.UniqueID);
        }
    }

    [HarmonyPatch(typeof(WeaponBase), nameof(WeaponBase.destroyObject))]
    static class WeaponGone
    {
        static void Prefix(WeaponBase __instance)
        {
            bool paid = paidMissiles.Remove(__instance.UniqueID);
            if (!struckMissiles.Remove(__instance.UniqueID) || paid) return;
            var run = Run.Current;
            if (run == null) return;
            run.Intercepts++;
            run.AddXp(Progression.MissileIntercept);
        }
    }

    internal static void Reset()
    {
        struckMissiles.Clear();
        paidMissiles.Clear();
        launchesSeen.Clear();
    }

    static readonly System.Collections.Generic.HashSet<Missile> launchesSeen = new System.Collections.Generic.HashSet<Missile>();

    /// <summary>Logs each enemy anti-ship missile once, when first seen: the only record of what the enemy really fired.</summary>
    internal static void LogEnemyLaunches(ObjectBase flagship)
    {
        launchesSeen.RemoveWhere(m => m == null || m.IsDestroyed);
        foreach (var missile in AntiShipInFlight().ToList())
            if (launchesSeen.Add(missile))
                Entry.Log.LogInfo($"Enemy launch: {missile._ap._ammunitionFileName} from {missile._launchPlatform?.Name.Value ?? "unknown"}, " +
                                  $"{missile._geoPosition.getDistanceInMiles(flagship._geoPosition):F1} nm from the flagship, {launchesSeen.Count} in the air");
    }

    /// <summary>
    /// Enemy missiles in the air and aimed at ships: anti-ship missiles, and SAMs
    /// fired in their surface mode, which the game lets the enemy use freely.
    /// </summary>
    static System.Collections.Generic.IEnumerable<Missile> AntiShipInFlight() =>
        Singleton<ObjectsManager>.Instance._listOfAllObjects.OfType<Missile>()
            .Where(m => !m.IsDestroyed && m._taskforce == Globals._enemyTaskforce && !Abilities.Turned(m) && (Boosts.AntiShipMissile(m._ap) || SamAtShip(m)));

    static bool SamAtShip(Missile missile)
    {
        var target = missile.CurrentIntendedTargetObject;
        return missile._ap._hasSecondaryTargetType && missile._ap._secondaryTargetType == Ammunition.Target.ASuW &&
               target != null && !target.isAirTarget() && !(target is WeaponBase);
    }

    /// <summary>
    /// A ship held to guns only keeps its SAMs for aircraft, but may not fire them
    /// at ships. The game's rule against that applies only to the player's ships,
    /// so a disarmed Belknap would otherwise still throw Standards at a gun cruiser.
    /// </summary>
    [HarmonyPatch(typeof(AI), "ShipSAMAntiSurfaceAutoEngagementBlocked")]
    static class NoSamsAtShipsWhenDisarmed
    {
        static void Postfix(ObjectBase ____baseObject, AmmunitionParameters ap, ObjectBase targetObject, ref bool __result)
        {
            if (__result || ap == null || targetObject == null || targetObject.isAirTarget() || targetObject is WeaponBase) return;
            if (ap._type == Ammunition.Type.Missile && ap._targetType == Ammunition.Target.AAW && Director.IsDisarmed(____baseObject))
                __result = true;
        }
    }

    /// <summary>
    /// The game's AI stops engaging a ship once it counts the ship as dealt with:
    /// the missiles in the air at it add up to its MissilesToSaturate (6 for a
    /// Sverdlov, 8 for a Kidd), or a salvo fired more than 20 seconds ago is
    /// still flying, which holds back every ship in the taskforce until the last
    /// missile is gone. All the enemy is one taskforce, so a single slow Styx
    /// could silence the sea for two minutes. Late in a run that limit is raised
    /// (Director.EnemySaturation), and the final assault has none.
    /// </summary>
    [HarmonyPatch(typeof(AI), "CheckIfAlreadyHandled")]
    static class EnemyKeepsFiring
    {
        static void Postfix(ObjectBase ____baseObject, ObjectBase __0, ref bool __result)
        {
            if (!__result || __0 == null || __0.IsAirUnit || !__0.isUnit() || Run.Current == null) return;
            float saturation = Director.Instance != null ? Director.Instance.EnemySaturation : 1f;
            if (saturation <= 1f) return;
            if (____baseObject == null || ____baseObject._taskforce != Globals._enemyTaskforce || __0._taskforce != Globals._playerTaskforce) return;
            float inAir = 0f;
            foreach (var weapon in __0._incomingWeapons)
                if (weapon is Missile && !weapon.IsDefeatedShot) inAir += weapon._ap._saturationValue;
            if (inAir < __0.MissilesToSaturateValue * saturation) __result = false;
        }
    }

    internal static int IncomingAntiShip() => AntiShipInFlight().Count();

    /// <summary>How near any enemy anti-ship missile is to any of these ships; infinite with none in the air.</summary>
    internal static float ClosestAntiShipNm(System.Collections.Generic.List<ObjectBase> ships)
    {
        float closest = float.PositiveInfinity;
        foreach (var missile in AntiShipInFlight())
            foreach (var ship in ships)
                closest = System.Math.Min(closest, (float)missile._geoPosition.getDistanceInMiles(ship._geoPosition));
        return closest;
    }

    internal static int IncomingMissiles() =>
        Singleton<ObjectsManager>.Instance._listOfAllObjects
            .Count(o => o is Missile && !o.IsDestroyed && o._taskforce == Globals._enemyTaskforce);
}
