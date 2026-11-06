using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Logs frame rate next to what the mission holds, every ten seconds of
/// real time while a mission runs, so a run that slows to a crawl shows
/// what grew with it: units, weapons in flight, rafts, particle systems,
/// garbage collections, or messages logged. The game's own loops are timed
/// too: what is left of a frame after them is physics, jobs, rendering and UI.
/// </summary>
/// <remarks>
/// A frame slower than <see cref="SpikeMs"/> is logged on its own, once a
/// second at most. Every Unity message is counted, from any thread, since
/// each goes to Player.log, the game's HTML log, Sentry and BepInEx, and a
/// warning or exception a frame is a cost of its own.
/// </remarks>
public class PerfMonitor : MonoBehaviour
{
    const float ReportEverySeconds = 10f;
    const float SpikeMs = 500f;
    const int TopMessages = 3;

    readonly List<float> frameMs = new List<float>();
    float windowStart = -1f, lastSpikeReport;
    int gcAtStart;

    static readonly object logLock = new object();
    static readonly Dictionary<string, int> messages = new Dictionary<string, int>();
    static int warnings, errors, exceptions;

    static long updateTicks, fixedTicks, lateTicks;
    static int fixedSteps;

    static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    [HarmonyPatch(typeof(GameMain), "Update")]
    static class TimeUpdate
    {
        static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();
        static void Postfix(long __state) => updateTicks += Stopwatch.GetTimestamp() - __state;
    }

    [HarmonyPatch(typeof(GameMain), "PerformFixedUpdate")]
    static class TimeFixedUpdate
    {
        static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();
        static void Postfix(long __state)
        {
            fixedTicks += Stopwatch.GetTimestamp() - __state;
            fixedSteps++;
        }
    }

    [HarmonyPatch(typeof(GameMain), "LateUpdate")]
    static class TimeLateUpdate
    {
        static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();
        static void Postfix(long __state) => lateTicks += Stopwatch.GetTimestamp() - __state;
    }

    void OnEnable() => Application.logMessageReceivedThreaded += Count;
    void OnDisable() => Application.logMessageReceivedThreaded -= Count;

    static void Count(string message, string stackTrace, LogType type)
    {
        lock (logLock)
        {
            switch (type)
            {
                case LogType.Warning: warnings++; break;
                case LogType.Exception: exceptions++; break;
                case LogType.Error: case LogType.Assert: errors++; break;
            }
            if (type == LogType.Log) return;
            string key = message.Length > 80 ? message.Substring(0, 80) : message;
            messages[key] = messages.TryGetValue(key, out int n) ? n + 1 : 1;
        }
    }

    void Update()
    {
        if (!Singleton<ObjectsManager>.InstanceExists(false))
        {
            windowStart = -1f;
            return;
        }
        float now = Time.realtimeSinceStartup;
        if (windowStart < 0f) StartWindow(now);

        float ms = Time.unscaledDeltaTime * 1000f;
        frameMs.Add(ms);
        if (ms >= SpikeMs && now - lastSpikeReport >= 1f)
        {
            lastSpikeReport = now;
            Entry.Log.LogWarning($"PERF spike: one frame took {ms:0} ms at {GameTime.missionElapsedTime / 60:0.0} min; {Census(false)}");
        }

        if (now - windowStart < ReportEverySeconds) return;
        try { Entry.Log.LogInfo(Summarise(now)); }
        catch (System.Exception e) { Entry.Log.LogError($"Perf report failed: {e}"); }
        StartWindow(now);
    }

    void StartWindow(float now)
    {
        windowStart = now;
        frameMs.Clear();
        gcAtStart = System.GC.CollectionCount(0);
        updateTicks = fixedTicks = lateTicks = 0;
        fixedSteps = 0;
        lock (logLock)
        {
            messages.Clear();
            warnings = errors = exceptions = 0;
        }
    }

    string Summarise(float now)
    {
        float seconds = now - windowStart;
        var sorted = frameMs.OrderBy(f => f).ToList();
        float Pct(float p) => sorted.Count == 0 ? 0f : sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * p))];
        var sb = new StringBuilder(
            $"PERF at {GameTime.missionElapsedTime / 60:0.0} min, x{GameTime.TimeCompression:0.#} time: " +
            $"{sorted.Count / seconds:0.0} fps, frame median {Pct(0.5f):0} ms / 95% {Pct(0.95f):0} ms / worst {Pct(1f):0} ms, " +
            $"{System.GC.CollectionCount(0) - gcAtStart} GCs, heap {System.GC.GetTotalMemory(false) / (1024 * 1024)} MB");
        sb.Append("\n  ").Append(Loops(seconds, sorted.Count));
        sb.Append("\n  ").Append(Census(true));
        lock (logLock)
        {
            sb.Append($"\n  logged: {warnings} warnings, {errors} errors, {exceptions} exceptions");
            foreach (var m in messages.OrderByDescending(kv => kv.Value).Take(TopMessages))
                sb.Append($"\n    {m.Value}x {m.Key}");
        }
        return sb.ToString();
    }

    /// <summary>Where an average frame went: the game's update loops, and what they leave for everything else.</summary>
    static string Loops(float seconds, int frames)
    {
        if (frames == 0) return "no frames";
        double update = TicksToMs(updateTicks) / frames, late = TicksToMs(lateTicks) / frames, fixedMs = TicksToMs(fixedTicks) / frames;
        double perStep = fixedSteps == 0 ? 0.0 : TicksToMs(fixedTicks) / fixedSteps;
        double rest = seconds * 1000.0 / frames - update - late - fixedMs;
        return $"frame average: update {update:0.0} ms, fixed {fixedMs:0.0} ms ({(float)fixedSteps / frames:0.0} steps at {perStep:0.0} ms), " +
               $"late {late:0.0} ms, physics, jobs, rendering and UI {rest:0.0} ms";
    }

    /// <summary>What the mission holds now. Particle systems are counted only in the full report: finding them walks the scene.</summary>
    static string Census(bool particles)
    {
        var om = Singleton<ObjectsManager>.Instance;
        var weapons = om._listOfAllWeapons;
        int inFlight = weapons.Count(w => w != null && !w.IsDestroyed);
        int wrecks = om._listOfAllUnits.Count(u => u != null && u.IsDestroyed);
        var shells = Singleton<ProjectileManager>.InstanceExists(false) ? Singleton<ProjectileManager>.Instance._activeProjectiles : null;
        int ourShells = shells?.Count(p => p?._launchPlatform != null && p._launchPlatform._taskforce == Globals._playerTaskforce) ?? 0;
        int wakes = 0, tails = 0, orphanWakes = 0;
        foreach (var unit in om._listOfAllObjects)
        {
            if (!(unit is WakeBubble bubble) || bubble == null) continue;
            wakes++;
            if (bubble._isPermamentTailWakeBubble) tails++;
            if (bubble._launchPlatform == null || bubble._launchPlatform.IsDestroyed) orphanWakes++;
        }
        string census =
            $"objects {om._listOfAllObjects.Count}, ships {om._listOfAllVesselObjects.Count}, aircraft {om._listOfAllAircraftObjects.Count}, " +
            $"weapons {weapons.Count} ({inFlight} live), shells {shells?.Count ?? 0} ({ourShells} ours), rafts {om._listOfAllRafts.Count}, wrecks {wrecks}, " +
            $"wake bubbles {wakes} ({tails} tails, {orphanWakes} of sunk ships)";
        if (!particles) return census;
        var systems = FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int playing = 0, alive = 0;
        foreach (var ps in systems)
        {
            if (!ps.isPlaying) continue;
            playing++;
            alive += ps.particleCount;
        }
        return census + $", particle systems {systems.Length} ({playing} playing, {alive} particles)";
    }
}
