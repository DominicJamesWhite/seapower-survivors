#if MINIMUM_RANGE // Held out while the SAM flicker is traced; not yet asked for.
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Stops a fleet ship jamming a launcher on a target inside its missile's
/// minimum range. The game's auto-engage only rejects a surface target more
/// than 5 nm inside the minimum, and prefers its longest-ranged missile, so a
/// Bunker Hill gave boats at 15-20 nm a Tomahawk (minimum 20 nm). Its launcher
/// then sat on "target too close" for as long as the boat lived, and only a
/// higher-priority task could take it back. Orders the player gave stand.
/// </summary>
static class MinimumRange
{
    static bool InFleet(ObjectBase o) => o != null && Run.Current != null && Run.Current.Fleet.Contains(o);

    /// <summary>The check auto-engage asks of each ammunition, which is true when that ammunition is off the table.</summary>
    [HarmonyPatch(typeof(AI), "ShipAntiShipMissileAutoEngagementBlocked")]
    static class TooCloseIsNotOffered
    {
        static readonly AccessTools.FieldRef<AI, ObjectBase> AiShip = AccessTools.FieldRefAccess<AI, ObjectBase>("_baseObject");

        static void Postfix(AI __instance, AmmunitionParameters ap, ObjectBase targetObject, ref bool __result)
        {
            if (__result || ap == null || ap._type != Ammunition.Type.Missile || !(targetObject is Vessel)) return;
            var ship = AiShip(__instance);
            if (!InFleet(ship)) return;
            __result = Vector3.Distance(ship.transform.position, targetObject.transform.position) <= ap._launchRangesInUnity.x;
        }
    }

    /// <summary>A target that closes inside the minimum after the launcher took the task: let the launcher go.</summary>
    [HarmonyPatch(typeof(WeaponSystemLauncher), nameof(WeaponSystemLauncher.OnUpdate))]
    static class TooCloseIsDropped
    {
        static void Postfix(WeaponSystemLauncher __instance)
        {
            if (__instance._engageState != WeaponSystem.EngageState.TargetTooClose || !__instance._executingEngageTask) return;
            if (__instance._takenEngageTask == null || !__instance._takenEngageTask._isAutoAttack) return;
            if (!(__instance._targetObject is Vessel) || !InFleet(__instance._baseObject)) return;
            __instance.EndEngageTask();
        }
    }
}
#endif
