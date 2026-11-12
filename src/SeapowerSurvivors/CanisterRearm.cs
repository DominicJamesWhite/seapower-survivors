using System.Linq;
using HarmonyLib;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// Ends the reload a rearm leaves running on a launcher with no magazine. Such
/// a launcher (an SS-N-2 or HY-1 canister, a boxed ASROC, a deck torpedo tube)
/// starts its reload timer when its last round goes, though it has nothing to
/// reload from: in the game the timer only marks it spent. The game's rearm
/// puts the rounds back but leaves the timer, so the launcher sits loaded and
/// "reloading" for what is left of it: up to five game minutes on an SS-N-2 or
/// HY-1, ten on an ASROC. The same for an enemy missile ship the director
/// rearms on the harder difficulties (Director.ReloadEnemies).
/// </summary>
[HarmonyPatch(typeof(WeaponSystemLauncher), nameof(WeaponSystemLauncher.RefillAmmo))]
static class CanisterRearm
{
    static void Postfix(WeaponSystemLauncher __instance)
    {
        var run = Run.Current;
        if (run == null || !__instance._isReloading || __instance._vwp._associatedMagazine != null ||
            !(run.Fleet.Contains(__instance._baseObject) || __instance._baseObject?._taskforce == Globals._enemyTaskforce)) return;
        // A launcher still empty has nothing to fire, and one mid-shot is not waiting on the timer.
        if (!__instance._containers.Any(c => c.IsFull())) return;

        __instance._isReloading = false;
        __instance._remainingReloadTime = 0f;
    }
}
