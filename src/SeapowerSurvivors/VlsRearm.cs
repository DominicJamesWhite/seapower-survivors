using HarmonyLib;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// Keeps a rearm from emptying a VLS magazine. A per-cell launcher such as the
/// Mk 41 fires straight from its magazine and never seats rounds. But the rearm
/// ends in a reload, and a finished reload loads the first magazine type into
/// every cell, which takes all its rounds out of the magazine: a Bunker Hill's
/// 49 and 41 SM-2s. The launcher checks the magazine, finds none, and drops each
/// engagement as out of ammunition, so after the first level-up its SAMs flick
/// between Ready and Engaging and never fire.
/// </summary>
[HarmonyPatch(typeof(WeaponContainer), nameof(WeaponContainer.load), new[] { typeof(Ammunition), typeof(bool) })]
static class VlsRearm
{
    static bool Prefix(WeaponParameters ____vwp) => !____vwp._perContainerReload;
}
