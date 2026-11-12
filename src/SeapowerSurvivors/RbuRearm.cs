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
/// cycle is hurried through the rest of it (see <see cref="FinishLoadCycle"/>):
/// a level-up leaves every mortar ready, as it does every other weapon.
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
        if (__instance._state != WeaponSystemLauncher.State.Idle)
        {
            hurried.Add(__instance);
            return false;
        }

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

    /// <summary>Launchers rearmed part-way through a load cycle, until they are idle again.</summary>
    static readonly HashSet<WeaponSystemLauncher> hurried = new HashSet<WeaponSystemLauncher>();

    /// <summary>
    /// Finishes the load cycle of a launcher rearmed in the middle of one. The
    /// cycle is left to the game, so the rounds are seated and the loader's
    /// parts end where they should, but its waits are skipped: the unload and
    /// load animations are run to their end at once and the reload timer is
    /// put past its end. What is left is the mount's own travel to its loading
    /// position and back, a few seconds.
    /// </summary>
    [HarmonyPatch(typeof(WeaponSystemLauncher), nameof(WeaponSystemLauncher.updateLoadSystem))]
    static class FinishLoadCycle
    {
        /// <summary>More steps than any animation has; a looping one would never end.</summary>
        const int MaxSteps = 1000;

        static readonly AccessTools.FieldRef<WeaponSystem, double> ReloadStart =
            AccessTools.FieldRefAccess<WeaponSystem, double>("_reloadStartTime");
        static readonly AccessTools.FieldRef<ObjectCodeAnimation, double> AnimationStart =
            AccessTools.FieldRefAccess<ObjectCodeAnimation, double>("_animationStartTime");

        static void Prefix(WeaponSystemLauncher __instance)
        {
            if (hurried.Count == 0) return;
            if (Run.Current == null)
            {
                hurried.Clear();
                return;
            }
            if (!hurried.Contains(__instance)) return;
            if (__instance._state == WeaponSystemLauncher.State.Idle)
            {
                hurried.Remove(__instance);
                return;
            }

            double expired = GameTime.missionElapsedTime - __instance._vwp._magazineReloadTime - 1.0;
            if (ReloadStart(__instance) > expired) ReloadStart(__instance) = expired;
            RunToEnd(__instance._unloadAmmunitionAnimation);
            RunToEnd(__instance._loadAmmunitionAnimation);
        }

        /// <summary>An animation takes one step an update, so it is updated until it stops: the launcher sees it finished on its next look.</summary>
        static void RunToEnd(ObjectCodeAnimation animation)
        {
            if (animation == null || !animation.isPlaying()) return;
            AnimationStart(animation) -= animation.getDuration() + 1f;
            for (int i = 0; i < MaxSteps && animation.update(); i++) { }
        }
    }
}
