using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Diagnostic for a SAM launcher flicking between Ready and Engaging: names
/// what keeps ending a fleet launcher's missile engagement. Ends that repeat
/// within a window are logged with who called, the launcher's state and the
/// ship's task queue. The game's own abort messages are compiled out.
/// </summary>
[HarmonyPatch(typeof(WeaponSystem), nameof(WeaponSystem.EndEngageTask))]
static class EngageFlicker
{
    const float WindowSeconds = 10f;
    const int ReportFrom = 4;
    const float UnityPerNm = 27.559496f;

    sealed class Seen
    {
        public int Count;
        public string Sample;
    }

    static readonly Dictionary<string, Seen> window = new Dictionary<string, Seen>();
    static float windowEnds;
    static bool failed;

    static void Prefix(WeaponSystem __instance)
    {
        if (failed) return;
        try
        {
            if (!(__instance is WeaponSystemLauncher) || !__instance._executingEngageTask) return;
            var run = Run.Current;
            var ship = __instance._baseObject;
            if (run == null || ship == null || !run.Fleet.Contains(ship)) return;
            var ap = __instance._ammoForEngage?._ap;
            if (ap == null || ap._type != Ammunition.Type.Missile) return;

            Flush();
            var target = __instance._targetObject;
            string kind = target == null ? "no target" : target.isWeapon() ? "missile" : target.isAirTarget() ? "aircraft" : "surface";
            string key = $"{__instance._vwp._sectionName} {ap._ammunitionFileName} at {kind}, state {__instance._engageState}, from {Callers()}";
            if (!window.TryGetValue(key, out var seen)) window[key] = seen = new Seen { Sample = Sample(ship, __instance) };
            seen.Count++;
        }
        catch (System.Exception e)
        {
            failed = true;
            SeapowerSurvivors.Entry.Log.LogError($"Engage flicker trace failed: {e}");
        }
    }

    static void Flush()
    {
        float now = Time.realtimeSinceStartup;
        if (now < windowEnds) return;
        windowEnds = now + WindowSeconds;
        foreach (var pair in window.Where(p => p.Value.Count >= ReportFrom))
            SeapowerSurvivors.Entry.Log.LogWarning($"ENGAGE FLICKER: {pair.Value.Count} ends in {WindowSeconds:0} s: {pair.Key}\n{pair.Value.Sample}");
        window.Clear();
    }

    /// <summary>The methods that led here, nearest first, past Harmony's wrappers' own frame.</summary>
    static string Callers()
    {
        var names = new StackTrace(2, false).GetFrames()?
            .Select(f => f.GetMethod())
            .Where(m => m != null)
            .Select(m => $"{m.DeclaringType?.Name}.{m.Name}")
            .Take(4);
        return names == null ? "unknown" : string.Join(" < ", names);
    }

    static string Sample(ObjectBase ship, WeaponSystem launcher)
    {
        double now = GameTime.missionElapsedTime;
        var sb = new StringBuilder($"  at {now / 60:0.00} min, status {ship._weaponStatus}, taken task " +
                                   (launcher._takenEngageTask == null ? "none" : FireControlTrace.Task(ship, launcher._takenEngageTask, now)) +
                                   $", priority {launcher._engageTaskPriority}");
        var target = launcher._targetObject;
        if (target != null)
            sb.Append($"\n  target {target.Name.Value}: {(target.transform.position - ship.transform.position).magnitude / UnityPerNm:0.0} nm, " +
                      $"{target.transform.position.y:0.0} units up, on the map {target.GetMapVehicle(ship._taskforce) != null}, destroyed {target.IsDestroyed}");
        foreach (var task in ship._currentEngageTasks.Where(t => t != null))
            sb.Append($"\n  queued p{task._priority} shots {task._shotsToFire}: {FireControlTrace.Task(ship, task, now)}");
        foreach (var other in ship._obp._launcherWeapons.Where(l => l != null && l._executingEngageTask))
            sb.Append($"\n  executing {other._vwp._sectionName}, state {other._engageState}, " +
                      (other._takenEngageTask == null ? "no task" : FireControlTrace.Task(ship, other._takenEngageTask, now)));
        return sb.ToString();
    }
}
