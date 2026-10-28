using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Runs the script: keeps each minute's quota of enemies alive, sends its
/// scripted spikes and mini-bosses, pays a chest for each boss sunk, and
/// declares victory when the clock runs out.
/// </summary>
/// <remarks>
/// Everything is timed in mission time, so pausing and time compression
/// behave. A run starts by itself in missions whose file name begins
/// "Survivors - ", and from the test panel anywhere else.
/// </remarks>
public class Director : MonoBehaviour
{
    enum Phase { Idle, Running, Won, Lost }

    const string AutoStartPrefix = "Survivors - ";
    const float AutoStartDelaySeconds = 10f;
    const float TickSeconds = 1f;
    const float SteerSeconds = 60f;
    /// <summary>Status line top edge at 1080p: below the game's compass tape, scaled with screen height.</summary>
    const float HudTop = 80f;
    const float AircraftLifetimeSeconds = 12f * 60f;

    internal static Director Instance;
    internal float ThreatMultiplier = 1f;

    Phase phase = Phase.Idle;
    double runStart, lastTick, lastSteer, lastSpawn;
    int minuteStarted = -1;

    readonly List<(ObjectBase unit, double spawnedAt)> spawned = new List<(ObjectBase, double)>();
    readonly List<(string name, List<ObjectBase> units)> bosses = new List<(string, List<ObjectBase>)>();
    Taskforce missionMarker;
    bool autoStartPending;
    GUIStyle hudStyle;

    internal bool Running => phase != Phase.Idle;
    double Now => GameTime.missionElapsedTime;
    float Elapsed => (float)(Now - runStart);

    void Awake() => Instance = this;

    internal void StartRun()
    {
        var player = TestPanel.PlayerShip();
        if (player == null) return;
        Reset();
        Run.Current = new Run(player);
        phase = Phase.Running;
        runStart = lastTick = lastSteer = Now;
        lastSpawn = double.MinValue;
        // The script is paced for it; the player can change it at any time.
        GameTime.SetTimeCompression(Script.TimeScale);
        TestPanel.Report($"Run started at {Script.TimeScale:F0}x time compression");
    }

    internal void StopRun()
    {
        if (!Running) return;
        Reset();
        TestPanel.Report("Run stopped");
    }

    /// <summary>Test aid: jump the clock forward one phase of the script.</summary>
    internal void SkipMinute()
    {
        if (phase == Phase.Running) runStart -= Script.PhaseSeconds;
    }

    void Reset()
    {
        // Countermeasures loosens the game's decoy pity rule; put it back.
        Globals._playerMissilesSpoofedLimit = 3;
        phase = Phase.Idle;
        minuteStarted = -1;
        spawned.Clear();
        bosses.Clear();
        Run.Current = null;
        CombatHooks.Reset();
        AaTrace.Reset();
    }

    void Update()
    {
        // A new mission builds new taskforces: forget the old run.
        if (Globals._playerTaskforce != missionMarker)
        {
            missionMarker = Globals._playerTaskforce;
            Reset();
            autoStartPending = missionMarker != null && IsSurvivorsMission();
        }

        if (autoStartPending && TestPanel.PlayerShip() != null && Now > AutoStartDelaySeconds)
        {
            autoStartPending = false;
            StartRun();
        }

        if (phase != Phase.Running || Now - lastTick < TickSeconds) return;
        lastTick = Now;

        var run = Run.Current;
        if (run.Ship == null || run.Ship.IsDestroyed)
        {
            phase = Phase.Lost;
            TestPanel.Report($"Ship lost at {Clock(Elapsed)}, level {run.Level}");
            return;
        }

        try { Tick(run.Ship); }
        catch (Exception e) { Entry.Log.LogError($"Director tick failed: {e}"); }
    }

    static bool IsSurvivorsMission()
    {
        string path = Globals.currentMissionFilePath;
        return !string.IsNullOrEmpty(path) && Path.GetFileNameWithoutExtension(path).StartsWith(AutoStartPrefix, StringComparison.Ordinal);
    }

    void Tick(ObjectBase player)
    {
        spawned.RemoveAll(s => s.unit == null || s.unit.IsDestroyed);
        RetireAircraft();
        PayBosses();

        if (Now - lastSteer >= SteerSeconds)
        {
            lastSteer = Now;
            foreach (var (unit, _) in spawned) Spawner.Steer(unit, player);
        }

        if (Elapsed >= Script.RunSeconds)
        {
            Win();
            return;
        }

        var run = Run.Current;
        var table = Script.For(run.Ship);
        float defence = Defence.Score(run);
        int minuteIndex = Mathf.Min((int)(Elapsed / Script.PhaseSeconds), table.Length - 1);
        var minute = table[minuteIndex];
        if (minuteIndex != minuteStarted)
        {
            minuteStarted = minuteIndex;
            // A scripted spike that the fleet cannot yet survive is skipped,
            // like any other group it is not ready for.
            if (minute.Event != null && Allowed(minute.Event, run, defence))
                Send(minute.Event, player, $"{minute.Event.Name} inbound");
            var boss = minute.Boss;
            if (boss != null && boss.Domain == Domain.Air && Defence.AirScore(run) < boss.MinAir)
            {
                TestPanel.Report($"{boss.Name} held back: fleet anti-air {Defence.AirScore(run):F1} of {boss.MinAir:F0}");
                boss = minute.FallbackBoss;
            }
            if (boss != null)
            {
                var units = Send(boss, player, $"Mini-boss: {boss.Name}");
                // A surface boss always comes, but holds its missiles until the fleet can face them.
                if (defence < boss.MinDefence)
                {
                    foreach (var unit in units) Disarm(unit);
                    TestPanel.Report($"{boss.Name} comes guns only: fleet defence {defence:F1} of {boss.MinDefence:F0}");
                }
                if (units.Count > 0) bosses.Add((boss.Name, units));
            }
        }

        // The quota: keep this many enemies alive, topping up at most once
        // per interval. Killing quickly therefore brings the next group
        // sooner, which is where the XP comes from.
        int quota = Mathf.CeilToInt(minute.Quota * ThreatMultiplier);
        float interval = minute.IntervalSeconds * Script.TimeScale / ThreatMultiplier;
        if (spawned.Count >= quota || spawned.Count >= Script.MaxEnemyUnits || Now - lastSpawn < interval) return;

        int airborne = spawned.Count(s => s.unit.IsAirUnit);
        bool airHeld = CombatHooks.IncomingMissiles() > Script.MissileCap || airborne >= Defence.AircraftCap(run);
        var menu = minute.Menu.Where(g => Allowed(g, run, defence) && !(airHeld && g.Domain == Domain.Air)).ToList();
        if (menu.Count == 0) return;
        Send(menu[UnityEngine.Random.Range(0, menu.Count)], player, null);
    }

    /// <summary>
    /// Missile-armed groups wait until the fleet can shoot missiles down, and
    /// stand-off raids until it has air cover to reach the bombers.
    /// </summary>
    static bool Allowed(Group group, Run run, float defence) =>
        defence >= group.MinDefence &&
        (group.Domain != Domain.Air || Defence.AirScore(run) >= group.MinAir) &&
        (group.Requires == Capability.None || run.Has.Contains(group.Requires));

    /// <summary>
    /// Takes a ship's anti-ship launchers out of action, leaving its guns.
    /// Inoperable is the flag battle damage sets; the weapon selector skips
    /// inoperable mounts.
    /// </summary>
    static void Disarm(ObjectBase unit)
    {
        foreach (var launcher in Boosts.AntiShipLaunchers(unit).ToList())
            launcher.Inoperable.Value = true;
    }

    List<ObjectBase> Send(Group group, ObjectBase player, string announce)
    {
        var made = new List<ObjectBase>();
        SpawnGroup(group, player, made);
        foreach (var unit in made) spawned.Add((unit, Now));
        lastSpawn = Now;
        if (announce != null) TestPanel.Report(announce);
        return made;
    }

    /// <summary>Each mini-boss sunk pays a chest: one level-up, or now and then three.</summary>
    void PayBosses()
    {
        for (int i = bosses.Count - 1; i >= 0; i--)
        {
            var (name, units) = bosses[i];
            if (units.Any(u => u != null && !u.IsDestroyed && spawned.Any(s => s.unit == u))) continue;
            bosses.RemoveAt(i);
            // A boss aircraft retired unshot is not a kill.
            if (!units.Any(u => u == null || u.IsDestroyed)) continue;
            int levels = UnityEngine.Random.value < Balance.BigChestChance ? 3 : 1;
            Run.Current.PendingLevels += levels;
            TestPanel.Report($"{name} destroyed: chest of {levels} level-up{(levels > 1 ? "s" : "")}");
        }
    }

    void Win()
    {
        phase = Phase.Won;
        var run = Run.Current;
        TestPanel.Report($"Survived {Clock(Script.RunSeconds)}! Level {run.Level}, {run.Kills} kills, {run.Intercepts} intercepts.");
        var mission = Singleton<MissionManager>.Instance;
        mission._missionStatus = MissionManager.MissionStatus.Victory;
        if (mission._endMissionTime < 0.0) mission._endMissionTime = Now + 20.0;
    }

    internal void SpawnGroup(Group group, ObjectBase player, List<ObjectBase> into)
    {
        var centre = Spawner.PointOnRing(player._geoPosition, group.MinNm, group.MaxNm, out float bearing);
        float heading = (bearing + 180f) % 360f;
        for (int i = 0; i < group.Units.Length; i++)
        {
            // Members of a group sit about a mile apart, abreast of the bearing.
            var at = Spawner.Offset(centre, i * 1f, (bearing + 90f) % 360f);
            ObjectBase unit = group.Domain == Domain.Air
                ? Spawner.Aircraft(group.Units[i], group.Squadron, group.Loadout, at, group.AltitudeFt, heading, Globals._enemyTaskforce)
                : Spawner.Ship(group.Units[i], at, heading, Globals._enemyTaskforce);
            if (unit == null)
            {
                Entry.Log.LogWarning($"Could not spawn {group.Units[i]} for {group.Name}");
                continue;
            }
            into.Add(unit);
        }

        // Ships are moved to the edge of the fight once they exist, since
        // that depends on their height above the horizon and the fleet's reach.
        string where = $"{group.MinNm:F0}-{group.MaxNm:F0} nm ring";
        if (group.Domain == Domain.Surface && into.Count > 0)
        {
            var fleet = Run.Current?.Fleet ?? new List<ObjectBase> { player };
            float distance = into.Min(u => Reach.SpawnDistanceNm(fleet, u));
            var front = Spawner.Offset(player._geoPosition, distance, bearing);
            for (int i = 0; i < into.Count; i++)
                Spawner.MoveTo(into[i], Spawner.Offset(front, i * 1f, (bearing + 90f) % 360f));
            double measured = into[0]._geoPosition.getDistanceInMiles(player._geoPosition);
            where = $"{distance:F1} nm (fleet reach {distance - Reach.ApproachNm:F1}, game measures {measured:F1})";
        }

        foreach (var unit in into) Spawner.Hunt(unit, player);
        Entry.Log.LogInfo($"Spawned {group.Name} ({into.Count}/{group.Units.Length}) at {where}, bearing {bearing:F0}, {Clock(Elapsed)}");
    }

    /// <summary>
    /// Bombers spawned without a home base have nowhere to return to once
    /// they have fired, so they are removed after a while, without scoring.
    /// </summary>
    void RetireAircraft()
    {
        foreach (var (unit, _) in spawned.Where(s => s.unit.IsAirUnit && Now - s.spawnedAt > AircraftLifetimeSeconds).ToList())
        {
            spawned.RemoveAll(s => s.unit == unit);
            unit.destroyObject(score: false);
        }
    }

    void OnGUI()
    {
        var run = Run.Current;
        if (run == null || TestPanel.PlayerShip() == null) return;

        string clock = phase switch
        {
            Phase.Won => "SURVIVED",
            Phase.Lost => "SHIP LOST",
            _ => $"phase {Mathf.Min((int)(Elapsed / Script.PhaseSeconds) + 1, 15)}/15, {Clock(Mathf.Max(0f, Script.RunSeconds - Elapsed))} left",
        };
        float need = Progression.XpToNext(run.Level);
        int filled = Mathf.Clamp(Mathf.FloorToInt(run.XpIntoLevel / need * 20f), 0, 20);
        string bar = new string('|', filled) + new string('.', 20 - filled);
        string text = $"{clock}    LV {run.Level} [{bar}]    hull {run.HullFraction * 100f:F0}%    " +
                      $"kills {run.Kills}   intercepts {run.Intercepts}    enemies {spawned.Count}   missiles {CombatHooks.IncomingMissiles()}   defence {Defence.Score(run):F1} / air {Defence.AirScore(run):F1}";

        hudStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        GUI.Box(new Rect(Screen.width / 2f - 420f, HudTop * Screen.height / 1080f, 840f, 32f), text, hudStyle);
    }

    static string Clock(float seconds) => $"{(int)seconds / 60:D2}:{(int)seconds % 60:D2}";
}
