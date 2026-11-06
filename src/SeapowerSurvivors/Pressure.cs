using System.Collections.Generic;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// How hard the director pushes right now, judged from how close the enemy is
/// getting. It sets how much of the fleet's missile defence the enemy may test
/// at once, and it never asks for all of it.
/// </summary>
/// <remarks>
/// The aim is danger through the whole run without a guaranteed loss, so a
/// fleet built and fought well must stay able to win. Ships are fragile: most
/// hits kill, so damage comes too late to steer by, and one missile through can
/// end the run. So the warning is a close call, not a hit:
///
/// - The salvo limit stays well under the fleet's score (at most 70%), so a
///   stronger fleet meets larger salvos but keeps a margin for leaks.
/// - Pressure climbs only while no enemy missile has come near the fleet, and
///   a close call knocks it back before anything has been hit.
/// - A hit a ship survives, or a ship lost, knocks it back further. If the
///   flagship is hit, or an escort lost, there is a breather: no new waves or
///   missile boats for a while. A cooldown stops it being used to sit out the run.
/// - Pressure can never be higher than the flagship's health out of 100.
/// - Ships lost and magazines emptied lower the score, and so the limit.
///
/// Kills and XP do not feed it, so a strong build still gets its rewards.
/// </remarks>
sealed class Pressure
{
    const float Start = 0.3f;
    /// <summary>Game seconds for pressure to climb from nothing to full.</summary>
    const float ClimbSeconds = 120f;
    /// <summary>Game seconds after a close call or hit before pressure climbs again.</summary>
    const float QuietSeconds = 30f;
    /// <summary>An enemy anti-ship missile this close to a fleet ship has got through the outer defences.</summary>
    const float CloseCallNm = 3f;
    const float CloseCallPenalty = 0.15f, HitPenalty = 0.4f;
    /// <summary>Warnings this close together count as one salvo, for the penalty.</summary>
    const float SalvoSeconds = 8f;
    const float BreatherSeconds = 45f, BreatherCooldownSeconds = 90f;
    /// <summary>A drop in health smaller than this is not a hit.</summary>
    const float HitPoints = 0.5f;
    const float LogSeconds = 30f;

    /// <summary>The share of the fleet's score allowed at no pressure, from the run's start to its end.</summary>
    const float EasyShareStart = 0.35f, EasyShareEnd = 0.45f;
    /// <summary>The same at full pressure. Well below 1: one leak can end the run.</summary>
    const float HardShareStart = 0.55f, HardShareEnd = 0.7f;
    /// <summary>Most enemy missiles allowed at once, whatever the fleet: more than this costs too many frames.</summary>
    const float MaxSalvo = 24f;

    float value = Start;
    double lastTick, lastWarning = double.MinValue, breatherUntil = double.MinValue, nextBreather = double.MinValue, nextLog;
    int closeCalls, hits;
    readonly Dictionary<ObjectBase, float> health = new Dictionary<ObjectBase, float>();

    static double Now => GameTime.missionElapsedTime;

    internal bool Breather => Now < breatherUntil;
    /// <summary>How hard the director is pushing, 0 to 1.</summary>
    internal float Value => value;
    /// <summary>A close call or hit was recent: the fleet is not on top of things, so the ramp does not climb.</summary>
    internal bool Warned => Now - lastWarning < QuietSeconds;

    internal void Reset()
    {
        value = Start;
        lastTick = nextLog = Now;
        lastWarning = breatherUntil = nextBreather = double.MinValue;
        closeCalls = hits = 0;
        health.Clear();
    }

    /// <summary>Hull health out of 100: the worse of integrity lost and water taken on.</summary>
    static float Health(ObjectBase ship) =>
        ship is Vessel { _compartments: { } c } ? Mathf.Min(c.IntegrityPercentage, 100f - c.FloodingPercentage) : 100f;

    internal float Share(float elapsedSeconds)
    {
        float t = Mathf.Clamp01(elapsedSeconds / Script.RunSeconds);
        return Mathf.Lerp(Mathf.Lerp(EasyShareStart, EasyShareEnd, t), Mathf.Lerp(HardShareStart, HardShareEnd, t), value);
    }

    /// <summary>Enemy missiles allowed in the air and on the rails at once.</summary>
    internal float SalvoLimit(float defence, float elapsedSeconds) =>
        Mathf.Min(defence * Share(elapsedSeconds), MaxSalvo);

    void Warn(float penalty)
    {
        if (Now - lastWarning >= SalvoSeconds) value = Mathf.Max(0f, value - penalty);
        lastWarning = Now;
    }

    void StartBreather(string why)
    {
        if (Now < nextBreather) return;
        breatherUntil = Now + BreatherSeconds;
        nextBreather = breatherUntil + BreatherCooldownSeconds;
        value = 0f;
        TestPanel.Report($"Enemy regrouping: {why}");
    }

    /// <summary>Once a director tick.</summary>
    internal void Tick(Run run, float elapsedSeconds, float defence, int committed, int pending, float closestEnemyNm)
    {
        float seconds = (float)(Now - lastTick);
        lastTick = Now;
        if (seconds <= 0f) return;

        var ships = run.Fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit).ToList();
        float closest = CombatHooks.ClosestAntiShipNm(ships);
        if (closest <= CloseCallNm)
        {
            if (Now - lastWarning >= SalvoSeconds)
            {
                closeCalls++;
                Entry.Log.LogInfo($"Close call: enemy missile {closest:F1} nm from the fleet, pressure {Mathf.Max(0f, value - CloseCallPenalty):F2}");
            }
            Warn(CloseCallPenalty);
        }

        foreach (var ship in ships)
        {
            float now = Health(ship);
            if (health.TryGetValue(ship, out float before) && before - now >= HitPoints)
            {
                hits++;
                Warn(HitPenalty);
                Entry.Log.LogInfo($"Fleet hit: {ship.Name.Value} {before:F0} -> {now:F0}, pressure {value:F2}");
                if (ship == run.Ship) StartBreather($"{ship.Name.Value} hit, at {now:F0}");
            }
            health[ship] = now;
        }
        foreach (var gone in health.Keys.Where(s => s == null || s.IsDestroyed).ToList())
        {
            health.Remove(gone);
            hits++;
            Warn(HitPenalty);
            Entry.Log.LogInfo($"Fleet ship lost, pressure {value:F2}");
            StartBreather("a ship of the fleet lost");
        }

        if (Now - lastWarning >= QuietSeconds)
            value = Mathf.Min(1f, value + seconds / ClimbSeconds);
        // A wounded flagship stays wounded, so pressure stays down with it: another hit could end the run.
        if (run.Ship != null) value = Mathf.Min(value, Health(run.Ship) / 100f);

        if (Now >= nextLog)
        {
            nextLog = Now + LogSeconds;
            Entry.Log.LogInfo($"Pressure {value:F2} at {elapsedSeconds / 60f:F1} min: score {defence:F1}, limit {SalvoLimit(defence, elapsedSeconds):F1}, " +
                              $"missiles committed {committed}, close calls {closeCalls}, hits {hits}, fleet {ships.Count}, pending {pending}, closest enemy {closestEnemyNm:F1} nm{(Breather ? ", breather" : "")}");
        }
    }
}
