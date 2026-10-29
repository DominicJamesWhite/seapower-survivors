using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Runs the script: keeps each minute's floor of fodder alive, spends the
/// threat budget in waves, sends the scripted spikes and mini-bosses, pays a
/// chest for each boss sunk, and declares victory when the clock runs out.
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
    /// <summary>Status panel top edge at 1080p, scaled with screen height.</summary>
    const float HudTop = 30f;
    const float AircraftLifetimeSeconds = 12f * 60f;

    internal static Director Instance;
    internal float ThreatMultiplier = 1f;

    Phase phase = Phase.Idle;
    double runStart, lastTick, lastSteer, lastFodder, lastWave;
    int minuteStarted = -1;
    /// <summary>The threat budget, earned with the clock and spent in waves.</summary>
    float credits;

    readonly List<(ObjectBase unit, double spawnedAt, bool fodder)> spawned = new List<(ObjectBase, double, bool)>();
    readonly List<(string name, List<ObjectBase> units)> bosses = new List<(string, List<ObjectBase>)>();
    Taskforce missionMarker;
    bool autoStartPending;
    GUIStyle hudLabel, hudValue;

    internal bool Running => phase != Phase.Idle;
    double Now => GameTime.missionElapsedTime;
    float Elapsed => (float)(Now - runStart);

    void Awake() => Instance = this;

    internal void StartRun()
    {
        var player = Flagship();
        if (player == null) return;
        Reset();
        Run.Current = new Run(player);
        // Ships the mission starts with beside the flagship are fleet from the first second.
        foreach (var ship in Singleton<ObjectsManager>.Instance._listOfAllPlayerVesselObjects)
            if (ship != null && ship != player && !ship.IsDestroyed) Run.Current.Fleet.Add(ship);
        phase = Phase.Running;
        runStart = lastTick = lastSteer = lastWave = Now;
        lastFodder = double.MinValue;
        credits = 0f;
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
            if (missionMarker != null) Launcher.Launching = false;
            autoStartPending = missionMarker != null && (IsSurvivorsMission() || Launcher.IsLaunchedMission());
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
            // Escorts do not carry the run on, and the mission's own trigger
            // waits for every player ship to sink, so end it here.
            var mission = Singleton<MissionManager>.Instance;
            mission._missionStatus = MissionManager.MissionStatus.Defeat;
            if (mission._endMissionTime < 0.0) mission._endMissionTime = Now + 20.0;
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

    /// <summary>
    /// The ship the run is lost with. In a launched mission that is the
    /// loadout's flagship, whichever ship is selected; elsewhere, the player's ship.
    /// </summary>
    static ObjectBase Flagship()
    {
        if (Launcher.IsLaunchedMission())
        {
            var flagship = Singleton<ObjectsManager>.Instance._listOfAllPlayerVesselObjects
                .FirstOrDefault(s => s != null && !s.IsDestroyed && s._obp._objectIniName == Launcher.Current.Flagship.Type);
            if (flagship != null) return flagship;
        }
        return TestPanel.PlayerShip();
    }

    void Tick(ObjectBase player)
    {
        spawned.RemoveAll(s => s.unit == null || s.unit.IsDestroyed);
        RetireAircraft();
        PayBosses();
        Escorts.Tend(Run.Current);

        if (Now - lastSteer >= SteerSeconds)
        {
            lastSteer = Now;
            foreach (var (unit, _, _) in spawned) Spawner.Steer(unit, player);
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

        // The budget accrues with the clock alone, kept to a bank of a minute and a half's income.
        float income = Script.CreditsPerSecond(Elapsed) * ThreatMultiplier;
        credits = Mathf.Min(credits + income * TickSeconds, income * Script.BankMinutes * Script.PhaseSeconds);

        SendFodder(minute, player, run, defence);
        if (Now - lastWave >= Script.WaveSeconds * Script.TimeScale)
        {
            lastWave = Now;
            SendWave(minute, player, run, defence);
        }
    }

    /// <summary>
    /// Vampire Survivors' wave minimum: below the floor, another fodder group
    /// is sent every few seconds, each on its own bearing, until it is met.
    /// </summary>
    void SendFodder(Minute minute, ObjectBase player, Run run, float defence)
    {
        int floor = Mathf.CeilToInt(minute.Floor * ThreatMultiplier);
        if (Now - lastFodder < Script.FodderSeconds * Script.TimeScale) return;
        if (spawned.Count(s => s.fodder) >= floor || spawned.Count >= Script.MaxEnemyUnits) return;
        var menu = minute.Fodder.Where(g => Allowed(g, run, defence)).ToList();
        if (menu.Count == 0) return;
        lastFodder = Now;
        Send(menu[UnityEngine.Random.Range(0, menu.Count)], player, null, fodder: true);
    }

    /// <summary>
    /// Risk of Rain 2's combat director: buy groups at their cost until the
    /// budget runs short, each on its own bearing so the salvos land together,
    /// taking a group's bigger pack whenever that is affordable. What it
    /// cannot afford it saves for the next wave.
    /// </summary>
    void SendWave(Minute minute, ObjectBase player, Run run, float defence)
    {
        var bought = new List<string>();
        for (int i = 0; i < Script.MaxGroupsPerWave; i++)
        {
            int airborne = spawned.Count(s => s.unit.IsAirUnit);
            bool airHeld = CombatHooks.IncomingMissiles() > Script.MissileCap || airborne >= Defence.AircraftCap(run);
            int room = Script.MaxEnemyUnits - spawned.Count;
            bool Fits(Group g) => g.Cost <= credits && g.Units.Length <= room && Allowed(g, run, defence) && !(airHeld && g.Domain == Domain.Air);

            var menu = minute.Threats.Where(Fits).ToList();
            if (menu.Count == 0) break;
            var group = menu[UnityEngine.Random.Range(0, menu.Count)];
            if (group.Bigger != null && Fits(group.Bigger)) group = group.Bigger;
            credits -= group.Cost;
            Send(group, player, null);
            bought.Add(group.Name);
        }
        if (bought.Count > 0)
            Entry.Log.LogInfo($"Wave at {Clock(Elapsed)}: {string.Join(", ", bought)}; {credits:F0} credits left");
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

    List<ObjectBase> Send(Group group, ObjectBase player, string announce, bool fodder = false)
    {
        var made = new List<ObjectBase>();
        SpawnGroup(group, player, made);
        foreach (var unit in made) spawned.Add((unit, Now, fodder));
        if (announce != null) TestPanel.Report(announce);
        return made;
    }

    /// <summary>Each mini-boss sunk pays a chest: a ship to join the fleet, and one level-up, or now and then three.</summary>
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
            Run.Current.ShipChests.Enqueue(Escorts.TierFor((int)(Elapsed / Script.PhaseSeconds)));
            TestPanel.Report($"{name} destroyed: chest of a ship and {levels} level-up{(levels > 1 ? "s" : "")}");
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
        if (!group.IsRing)
        {
            SpawnOnBearing(group, group.Units, player, into, null);
            return;
        }
        // A wall: one unit on each bearing, evenly round the compass.
        float first = UnityEngine.Random.Range(0f, 360f);
        for (int i = 0; i < group.Units.Length; i++)
            SpawnOnBearing(group, new[] { group.Units[i] }, player, into, (first + i * 360f / group.Units.Length) % 360f);
    }

    void SpawnOnBearing(Group group, string[] types, ObjectBase player, List<ObjectBase> into, float? onBearing)
    {
        float bearing;
        GeoPosition centre;
        if (onBearing is float fixedBearing)
        {
            bearing = fixedBearing;
            centre = Spawner.Offset(player._geoPosition, UnityEngine.Random.Range(group.MinNm, group.MaxNm), bearing);
        }
        else centre = Spawner.PointOnRing(player._geoPosition, group.MinNm, group.MaxNm, out bearing);
        float heading = (bearing + 180f) % 360f;
        var made = new List<ObjectBase>();
        for (int i = 0; i < types.Length; i++)
        {
            // Members of a group sit about a mile apart, abreast of the bearing.
            var at = Spawner.Offset(centre, i * 1f, (bearing + 90f) % 360f);
            ObjectBase unit = group.Domain == Domain.Air
                ? Spawner.Aircraft(types[i], group.Squadron, group.Loadout, at, group.AltitudeFt, heading, Globals._enemyTaskforce)
                : Spawner.Ship(types[i], at, heading, Globals._enemyTaskforce);
            if (unit == null)
            {
                Entry.Log.LogWarning($"Could not spawn {types[i]} for {group.Name}");
                continue;
            }
            made.Add(unit);
        }

        // Ships are moved to the edge of the fight once they exist, since
        // that depends on their height above the horizon and the fleet's reach.
        string where = $"{group.MinNm:F0}-{group.MaxNm:F0} nm ring";
        if (group.Domain == Domain.Surface && made.Count > 0)
        {
            var fleet = Run.Current?.Fleet ?? new List<ObjectBase> { player };
            float distance = made.Min(u => Reach.SpawnDistanceNm(fleet, u));
            var front = Spawner.Offset(player._geoPosition, distance, bearing);
            for (int i = 0; i < made.Count; i++)
                Spawner.MoveTo(made[i], Spawner.Offset(front, i * 1f, (bearing + 90f) % 360f));
            double measured = made[0]._geoPosition.getDistanceInMiles(player._geoPosition);
            where = $"{distance:F1} nm (fleet reach {distance - Reach.ApproachNm:F1}, game measures {measured:F1})";
        }

        foreach (var unit in made) Spawner.Hunt(unit, player);
        into.AddRange(made);
        Entry.Log.LogInfo($"Spawned {group.Name} ({made.Count}/{types.Length}) at {where}, bearing {bearing:F0}, {Clock(Elapsed)}");
    }

    /// <summary>
    /// Bombers spawned without a home base have nowhere to return to once
    /// they have fired, so they are removed after a while, without scoring.
    /// </summary>
    void RetireAircraft()
    {
        foreach (var (unit, _, _) in spawned.Where(s => s.unit.IsAirUnit && Now - s.spawnedAt > AircraftLifetimeSeconds).ToList())
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
            _ => Clock(Mathf.Max(0f, Script.RunSeconds - Elapsed)),
        };
        int phaseShown = Mathf.Min((int)(Elapsed / Script.PhaseSeconds) + 1, 15);
        float hull = run.HullFraction;
        var cells = new (string label, string value, Color colour)[]
        {
            ($"PHASE {phaseShown}/15", clock, phase == Phase.Lost ? Hud.Danger : Hud.Value),
            ("LEVEL", run.Level.ToString(), Hud.Accent),
            ("HULL", $"{hull * 100f:F0}%", hull < 0.5f ? Hud.Danger : Hud.Value),
            ("KILLS", run.Kills.ToString(), Hud.Value),
            ("INTERCEPTS", run.Intercepts.ToString(), Hud.Value),
            ("ENEMIES", $"{spawned.Count}  ({spawned.Count(s => s.fodder)} fodder)", Hud.Value),
            ("INBOUND MISSILES", CombatHooks.IncomingMissiles().ToString(), CombatHooks.IncomingMissiles() > 0 ? Hud.Danger : Hud.Value),
            ("DEFENCE / AIR", $"{Defence.Score(run):F0} / {Defence.AirScore(run):F0}", Hud.Value),
            ("BUDGET", credits.ToString("F0"), Hud.Value),
        };

        // Sized for 1080p and scaled with the screen, like the game's own UI.
        float scale = Screen.height / 1080f;
        hudLabel ??= Hud.Text(Mathf.RoundToInt(12 * scale), Hud.Label);
        hudValue ??= Hud.Text(Mathf.RoundToInt(20 * scale), Hud.Value, style: FontStyle.Bold);
        float pad = 16f * scale, gap = 22f * scale, height = 56f * scale, barHeight = 4f * scale;
        var widths = cells.Select(c => Mathf.Max(hudLabel.CalcSize(new GUIContent(c.label)).x, hudValue.CalcSize(new GUIContent(c.value)).x)).ToArray();
        float width = widths.Sum() + gap * (cells.Length - 1) + 2f * pad;
        var panel = new Rect((Screen.width - width) / 2f, HudTop * scale, width, height);
        Hud.Box(panel);

        float x = panel.x + pad;
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0) Hud.Fill(new Rect(x - gap / 2f, panel.y + 10f * scale, 1f, height - 20f * scale), Hud.Edge);
            GUI.Label(new Rect(x, panel.y + 8f * scale, widths[i], 14f * scale), cells[i].label, hudLabel);
            hudValue.normal.textColor = cells[i].colour;
            GUI.Label(new Rect(x, panel.y + 22f * scale, widths[i], 26f * scale), cells[i].value, hudValue);
            x += widths[i] + gap;
        }

        // Progress to the next level, along the panel's foot.
        float progress = Mathf.Clamp01(run.XpIntoLevel / Progression.XpToNext(run.Level));
        var track = new Rect(panel.x + 1f, panel.yMax - barHeight - 1f, panel.width - 2f, barHeight);
        Hud.Fill(track, new Color(1f, 1f, 1f, 0.08f));
        Hud.Fill(new Rect(track.x, track.y, track.width * progress, track.height), Hud.Accent);
    }

    static string Clock(float seconds) => $"{(int)seconds / 60:D2}:{(int)seconds % 60:D2}";
}
