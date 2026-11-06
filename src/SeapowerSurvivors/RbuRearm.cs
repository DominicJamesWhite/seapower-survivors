using System.Collections.Generic;
using HarmonyLib;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// Keeps a rearm from emptying our fleet's RBUs. The game's rearm destroys the
/// rounds on the launcher and ends the reload at once, and a launcher with a
/// load animation reseats nothing when a reload ends. It still believes it holds
/// the rounds, so the next fire order plays the unload animation and then the
/// load animation: over four game minutes on an RBU-6000, with the expired reload
/// timer held at 1 s throughout. Until then our own fire control passes it over
/// as empty.
/// <para>
/// So an idle launcher gets its rounds back on the spot. One already in its load
/// cycle keeps its place, with its timer running: the magazine is all it needs.
/// </para>
/// </summary>
[HarmonyPatch(typeof(WeaponSystemLauncher), nameof(WeaponSystemLauncher.RefillAmmo))]
static class RbuRearm
{
    static bool Prefix(WeaponSystemLauncher __instance, List<WeaponMagazineSystem> processedMagazines)
    {
        var run = Run.Current;
        var magazine = __instance._vwp._associatedMagazine;
        if (run == null || magazine == null || __instance._weaponType != WeaponSystem.WeaponType.RBU ||
            !run.Fleet.Contains(__instance._baseObject)) return true;

        Refill(magazine, processedMagazines);
        Refill(__instance._vwp._campaignMagazine, processedMagazines);
        if (__instance._state != WeaponSystemLauncher.State.Idle) return false;

        var ammo = __instance._ammunitionOnWeapon ?? magazine.getPreloadAmmunition(__instance._vwp._preloadAmmunitionIndex - 1);
        foreach (var container in __instance._containers)
        {
            container.ClearPartialPack();
            container.unload(returnAmmo: false);
            if (ammo != null) container.load(ammo);
        }
        __instance._isReloading = false;
        __instance._remainingReloadTime = 0f;
        return false;
    }

    static void Refill(WeaponMagazineSystem magazine, List<WeaponMagazineSystem> processed)
    {
        if (magazine == null || (processed != null && processed.Contains(magazine))) return;
        magazine.RefillAmmo();
        processed?.Add(magazine);
    }
}
