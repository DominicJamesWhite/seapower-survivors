using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// Clears wrecks away during a run: a ship or aircraft, either side's, is
/// removed this long after it is destroyed. Until the game removes it
/// itself, which for a ship waits for the hull to go fully under, a wreck
/// goes on costing physics, flooding and fire, and a run makes a lot of them.
/// </summary>
/// <remarks>
/// The flagship is left alone, so the camera and the defeat screen still
/// have it. Removing goes through the game's own destroyObject, without
/// scoring, the way the director retires bombers.
/// </remarks>
static class Wrecks
{
    /// <summary>Game time, so time compression clears them sooner in real time.</summary>
    const double ClearAfterSeconds = 30.0;

    static readonly Dictionary<ObjectBase, double> destroyedAt = new Dictionary<ObjectBase, double>();

    [HarmonyPatch(typeof(ObjectBase), nameof(ObjectBase.setDestroyedFlag))]
    static class NoteDestroyed
    {
        static void Prefix(ObjectBase __instance, out bool __state) => __state = __instance.IsDestroyed;

        static void Postfix(ObjectBase __instance, bool __state)
        {
            if (__state || !__instance.IsDestroyed || Run.Current == null) return;
            if (!(__instance is Vessel || __instance is Submarine || __instance.IsAirUnit)) return;
            destroyedAt[__instance] = GameTime.missionElapsedTime;
        }
    }

    /// <summary>Remove every wreck that has had its time.</summary>
    internal static void Sweep()
    {
        if (destroyedAt.Count == 0) return;
        double now = GameTime.missionElapsedTime;
        foreach (var (unit, at) in destroyedAt.ToList())
        {
            // A missionElapsedTime behind the note means a new mission: drop it.
            if (unit != null && now >= at && now - at < ClearAfterSeconds) continue;
            destroyedAt.Remove(unit);
            if (unit == null || now < at || unit == Run.Current?.Ship) continue;
            unit.destroyObject(score: false);
        }
    }
}
