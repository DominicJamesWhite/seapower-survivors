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

    [HarmonyPatch(typeof(Blastzone), "OnHitUnit")]
    static class BlastHit
    {
        static void Prefix(Blastzone __instance, object __0)
        {
            if (IsEnemyWeapon(__0) && IsOurs(BlastLaunchPlatform(__instance)))
                struckMissiles.Add(((ObjectBase)__0).UniqueID);
        }
    }

    [HarmonyPatch(typeof(WeaponSystemCIWS), nameof(WeaponSystemCIWS.InterceptAirTarget))]
    static class CiwsHit
    {
        static void Prefix(WeaponSystemCIWS __instance, object __0)
        {
            if (IsEnemyWeapon(__0) && IsOurs(__instance._baseObject))
                struckMissiles.Add(((ObjectBase)__0).UniqueID);
        }
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
            if (!struckMissiles.Remove(__instance.UniqueID)) return;
            var run = Run.Current;
            if (run == null) return;
            run.Intercepts++;
            run.AddXp(Progression.MissileIntercept);
        }
    }

    internal static void Reset() => struckMissiles.Clear();

    internal static int IncomingMissiles() =>
        Singleton<ObjectsManager>.Instance._listOfAllObjects
            .Count(o => o is Missile && !o.IsDestroyed && o._taskforce == Globals._enemyTaskforce);
}
