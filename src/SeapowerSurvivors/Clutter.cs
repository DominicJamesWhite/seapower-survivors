using System.Collections.Generic;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Keeps what sinking ships leave on the water from piling up during a run.
/// Below 30 fps the game itself runs slow, so every object that outlives its
/// use costs game speed, and a long run sinks a lot of ships.
/// </summary>
/// <remarks>
/// Life rafts are full Vessels, hull physics and sound included, and nothing
/// removes them unless a ship picks them up, so during a run none are launched.
/// Wake bubbles are objects with their own updates, dropped by every moving
/// ship, and last 1160 s by default. Wake-homing torpedoes only need one with
/// life left (WakeSeeker.isInSensitivityRange), so they get a shorter life
/// and go with their ship. That also ends a leak: a ship's tail bubble lives
/// forever, and the game never removes it when the ship dies.
/// </remarks>
static class Clutter
{
    /// <summary>Before the game's width and sea state factors. A wake this old is kilometres behind its ship.</summary>
    const float WakeLifetimeSeconds = 200f;

    static float? stockWakeLifetime;
    static readonly List<WakeBubble> orphans = new List<WakeBubble>();

    [HarmonyPatch(typeof(Compartments), nameof(Compartments.SpawnLifeRafts))]
    static class NoRafts
    {
        static bool Prefix() => Run.Current == null;
    }

    /// <summary>
    /// The bubble reads the shared lifetime for both of its timers, so the
    /// shared value is what changes. It is put back outside a run.
    /// </summary>
    [HarmonyPatch(typeof(WakeBubble), nameof(WakeBubble.init))]
    static class ShortWakes
    {
        static void Prefix()
        {
            var parameters = Globals.WakeBubbleParameters;
            stockWakeLifetime ??= parameters._lifetime;
            parameters._lifetime = Run.Current != null
                ? Mathf.Min(stockWakeLifetime.Value, WakeLifetimeSeconds)
                : stockWakeLifetime.Value;
        }
    }

    /// <summary>Remove every wake bubble whose ship has sunk or gone. Destroying goes the way a bubble's own expiry does.</summary>
    internal static void SweepWakes()
    {
        foreach (var unit in Singleton<ObjectsManager>.Instance._listOfAllObjects)
            if (unit is WakeBubble bubble && bubble != null && (bubble._launchPlatform == null || bubble._launchPlatform.IsDestroyed))
                orphans.Add(bubble);
        foreach (var bubble in orphans)
            bubble.destroyObject(keepInFlightEffectAlive: false, score: false);
        orphans.Clear();
    }
}
