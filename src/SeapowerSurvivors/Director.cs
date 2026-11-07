using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Runs the ramp: one tiny boat first, then pulses of danger by the starting
/// ship's own plan (see Threats), building to a peak and relaxing. Sends the
/// scripted spikes, set pieces and mini-bosses, pays a chest for each boss sunk,
/// and declares victory when the clock runs out.
/// </summary>
/// <remarks>
/// Everything is timed in mission time, so pausing and time compression
/// behave. A run starts by itself in missions whose file name begins
/// "Survivors - " and in launched ones: the mission is held paused until the
/// player unpauses, so loading costs none of the run, and the run starts
/// then. Anywhere else it starts from the test panel.
/// </remarks>
public class Director : MonoBehaviour
{
    enum Phase { Idle, Running, Won, Lost }

    const string AutoStartPrefix = "Survivors - ";
    /// <summary>
    /// Once loading is done the pause is held this long in real time before
    /// an unpause counts, in case the game's own start-up sets the clock
    /// running again a frame or two late.
    /// </summary>
    const float HoldSettleSeconds = 1f;
    const float TickSeconds = 1f;
    const float SteerSeconds = 60f;
    /// <summary>Status panel top edge at 1080p, scaled with screen height.</summary>
    const float HudTop = 40f;
    const float AircraftLifetimeSeconds = 6f * 60f;

    internal static Director Instance;
    internal float ThreatMultiplier = 1f;

    Phase phase = Phase.Idle;
    double runStart, lastTick, lastSteer;
    int minuteStarted = -1;
    readonly Pressure pressure = new Pressure();
    /// <summary>Ships whose anti-ship launchers are held out of action: they count for no missiles.</summary>
    static readonly HashSet<ObjectBase> disarmed = new HashSet<ObjectBase>();
    /// <summary>Enemy anti-ship missiles in the air and still on the rails, and how many are allowed; both refreshed each tick.</summary>
    int committed;
    float salvoLimit;
    /// <summary>Missiles on ships still too far out to fire them: not counted against the limit.</summary>
    int pendingMissiles;
    /// <summary>The edge of the fleet's reach at the last surface spawn, the estimate used to judge the next group.</summary>
    float lastEdgeNm;
    /// <summary>Who this run's starting ship faces, and when.</summary>
    ThreatPlan plan;

    /// <summary>Game seconds between pulses of new enemies.</summary>
    const float PulseSeconds = 45f;
    /// <summary>Each pulse's share of the budget through one build-up: the last is the peak.</summary>
    static readonly float[] PulseShare = { 0.75f, 1f, 1.25f, 1.5f };
    /// <summary>Most groups in one pulse, on bearings well apart.</summary>
    const int MaxGroupsPerPulse = 4;
    /// <summary>After a peak, the fight is given this long to play out before the relax starts, if it does not thin out first.</summary>
    const float PeakFadeSeconds = 45f;
    const float RelaxMinSeconds = 30f, RelaxMaxSeconds = 45f;
    /// <summary>How far kills and losses may move a ship's curve.</summary>
    const float AdjustMin = 0.6f, AdjustMax = 1.4f;
    /// <summary>Killing this share of a pulse's budget by the next pulse holds the curve steady; more raises it, less lowers it.</summary>
    const float KeepingUpShare = 0.75f;
    const float AdjustRate = 0.1f;
    /// <summary>What a breather takes off the curve.</summary>
    const float BreatherAdjust = 0.1f;
    /// <summary>Saved budget is limited to this many pulses' worth, so a quiet spell cannot bank a flood.</summary>
    const float WalletPulses = 3f;
    /// <summary>A third of the unit limit may be boats waiting far out, and always a small group.</summary>
    const int MinWaiting = 3;
    /// <summary>With only bombers allowed, one pair at a time.</summary>
    const int BomberPair = 2;

    float wallet, adjust, killedDanger, lastPulseBudget, peakUnits;
    double nextPulse, peakFadeUntil, relaxUntil, nextOpening;
    int openingStep, pulseInCycle;
    bool breatherTaken;
    readonly List<ObjectBase> openingUnits = new List<ObjectBase>();

    /// <summary>What the director has out, and what each cost in danger: kills pay that back into the ramp.</summary>
    readonly List<(ObjectBase unit, double spawnedAt, int danger)> spawned = new List<(ObjectBase, double, int)>();
    readonly List<(string name, List<ObjectBase> units)> bosses = new List<(string, List<ObjectBase>)>();
    readonly SetPieces setPieces = new SetPieces();
    /// <summary>Stand-off ships holding beyond the fleet's reach: where, and until when they hold before closing.</summary>
    readonly Dictionary<ObjectBase, (GeoPosition station, double until)> standOff = new Dictionary<ObjectBase, (GeoPosition, double)>();
    double lastStandOffAlert;
    /// <summary>Game seconds between re-reveals of the fleet to ships holding beyond the horizon, inside a track's ten-second life.</summary>
    const float StandOffAlertSeconds = 4f;
    Taskforce missionMarker;
    bool autoStartPending;
    /// <summary>Real time from which an unpause starts the run; null until loading is done.</summary>
    float? holdSettledAt;
    GUIStyle holdTitle, holdHint;
    float holdScale;
    GUIStyle hudLabel, hudValue;
    float hudScale;

    internal bool Running => phase != Phase.Idle;
    double Now => GameTime.missionElapsedTime;
    float Elapsed => (float)(Now - runStart);

    void Awake() => Instance = this;

    /// <param name="counts">Whether the run's XP is banked when it ends: true for runs that start by themselves.</param>
    internal void StartRun(bool counts = false)
    {
        var player = Flagship();
        if (player == null) return;
        Reset();
        Run.Current = new Run(player) { Counts = counts };
        // Ships the mission starts with beside the flagship are fleet from the first second.
        foreach (var ship in Singleton<ObjectsManager>.Instance._listOfAllPlayerVesselObjects)
            if (ship != null && ship != player && !ship.IsDestroyed) Run.Current.Fleet.Add(ship);
        // Starting flights join airborne now, the way chest flights do, with no carrier or airfield to start from.
        // The Greek ensign's five have already sailed, or are being flown on now.
        if (Launcher.IsLaunchedMission() && Launcher.Current.Ships.Count + Launcher.Current.Flights.Count > 0) Run.Current.EscortChosen = true;
        if (Launcher.IsLaunchedMission())
        {
            int copy = 0;
            foreach (var flight in EnsignCombat.StartingFlights(Run.Current, Launcher.Current))
            {
                try { Escorts.Join(Run.Current, flight, copy++); }
                catch (Exception e) { Entry.Log.LogError($"Starting flight {flight.Name} failed to join: {e}"); }
            }
        }
        phase = Phase.Running;
        runStart = lastTick = lastSteer = Now;
        plan = Threats.For(Run.Current.FlagshipType);
        wallet = killedDanger = lastPulseBudget = peakUnits = 0f;
        adjust = 1f;
        nextPulse = peakFadeUntil = relaxUntil = nextOpening = 0.0;
        openingStep = pulseInCycle = 0;
        breatherTaken = false;
        openingUnits.Clear();
        pressure.Reset();
        lastEdgeNm = 0f;
        if (Run.Current.Hoisted.Count > 0)
        {
            Boosts.ApplyFleet(Run.Current);
            TestPanel.Report($"Flying: {string.Join(", ", Run.Current.Hoisted.Select(e => e.Name))}");
        }
        TestPanel.Report("Run started");
    }

    /// <summary>
    /// Keep the mission paused while it loads and until the player unpauses
    /// it, then start the run. Pausing is the game's own, so the player
    /// unpauses the usual way.
    /// </summary>
    void HoldForStart()
    {
        bool loaded = Singleton<SceneCreator>.InstanceExists(false) && Singleton<SceneCreator>.Instance.IsLoadingDone;
        if (!loaded || TestPanel.PlayerShip() == null)
        {
            if (!GameTime.IsPaused()) GameTime.Pause();
            return;
        }
        holdSettledAt ??= Time.realtimeSinceStartup + HoldSettleSeconds;
        if (Time.realtimeSinceStartup < holdSettledAt)
        {
            if (!GameTime.IsPaused()) GameTime.Pause();
            return;
        }
        if (GameTime.IsPaused()) return;
        autoStartPending = false;
        holdSettledAt = null;
        StartRun(counts: true);
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

    /// <summary>
    /// Bank the run's XP, once: when it is won or lost, or when it is left
    /// some other way, by quitting the mission or stopping it.
    /// </summary>
    static void Bank(Run run)
    {
        if (run == null || !run.Counts || run.Banked) return;
        run.Banked = true;
        int xp = Mathf.FloorToInt(run.Xp);
        Profile.Current.Deposit(xp);
        TestPanel.Report($"{xp} XP banked; {Profile.Current.Bank} XP to spend");
    }

    void Reset()
    {
        Bank(Run.Current);
        // Countermeasures loosens the game's decoy pity rule; put it back.
        Globals._playerMissilesSpoofedLimit = 3;
        phase = Phase.Idle;
        minuteStarted = -1;
        spawned.Clear();
        bosses.Clear();
        setPieces.Clear();
        standOff.Clear();
        disarmed.Clear();
        Run.Current = null;
        CombatHooks.Reset();
        Abilities.Reset();
        EnsignCombat.Reset();
        AaTrace.Reset();
        TorpedoTrace.Reset();
        MissileTrace.Reset();
        FireControlTrace.Reset();
    }

    void Update()
    {
        // Quit to the menu mid-run: bank it now, so the store already shows it.
        if (Run.Current != null && !Singleton<MissionManager>.InstanceExists(false)) Reset();
        if (Run.Current != null) Wrecks.Sweep();

        // A new mission builds new taskforces: forget the old run.
        if (Globals._playerTaskforce != missionMarker)
        {
            missionMarker = Globals._playerTaskforce;
            Reset();
            if (missionMarker != null) Launcher.Launching = false;
            autoStartPending = missionMarker != null && (IsSurvivorsMission() || Launcher.IsLaunchedMission());
            holdSettledAt = null;
        }

        if (autoStartPending) HoldForStart();

        if (phase != Phase.Running || Now - lastTick < TickSeconds) return;
        lastTick = Now;

        var run = Run.Current;
        if ((run.Ship == null || run.Ship.IsDestroyed) && run.FlagTransfers > 0)
        {
            // The shop's Flag transfer: the largest escort ship afloat takes the flag.
            var heir = run.Fleet.Where(s => s != null && !s.IsDestroyed && s is Vessel)
                .OrderByDescending(s => (s as Vessel)._compartments?._maxLimitIntegrity ?? 0f).FirstOrDefault();
            if (heir != null)
            {
                run.FlagTransfers--;
                TestPanel.Report($"{run.Ship?.Name.Value ?? "Flagship"} lost: the flag passes to {heir.Name.Value} ({run.FlagTransfers} transfer(s) left)");
                run.Ship = heir;
                Formations.PassFlag(heir);
            }
        }
        if (run.Ship == null || run.Ship.IsDestroyed)
        {
            phase = Phase.Lost;
            TestPanel.Report($"Ship lost at {Clock(Elapsed)}, level {run.Level}");
            Bank(run);
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
        killedDanger += spawned.Where(s => s.unit == null || s.unit.IsDestroyed).Sum(s => s.danger);
        spawned.RemoveAll(s => s.unit == null || s.unit.IsDestroyed);
        RetireAircraft();
        PayBosses();
        Escorts.Tend(Run.Current);
        Mortars.Tick(Run.Current);
        EnsignCombat.Tick(Run.Current, TickSeconds);
        Spawner.MarkHostiles();
        Clutter.SweepWakes();

        foreach (var gone in standOff.Keys.Where(u => u == null || u.IsDestroyed).ToList()) standOff.Remove(gone);
        bool alert = Now - lastStandOffAlert >= StandOffAlertSeconds;
        if (alert) lastStandOffAlert = Now;
        foreach (var (unit, (_, until)) in standOff.ToList())
        {
            if (Now > until)
            {
                standOff.Remove(unit);
                Spawner.Steer(unit, player);
                Entry.Log.LogInfo($"{unit.Name.Value} ends its stand-off and closes");
            }
            // Held beyond the horizon, it needs telling where the fleet is to keep firing.
            else if (alert) Spawner.Alert(unit, player);
        }

        if (Now - lastSteer >= SteerSeconds)
        {
            lastSteer = Now;
            foreach (var (unit, _, _) in spawned)
                if (!standOff.ContainsKey(unit)) Spawner.Steer(unit, player);
        }

        if (Elapsed >= Script.RunSeconds)
        {
            Win();
            return;
        }

        var run = Run.Current;
        run.Elapsed = Elapsed;
        var table = Script.For(run.Ship);
        float defence = Defence.Score(run);
        KeepDisarmed();
        var afloat = spawned.Select(s => s.unit).Concat(setPieces.Afloat).Distinct().ToList();
        committed = CombatHooks.IncomingAntiShip() + afloat.Where(u => !OutOfFiringRange(u, run)).Sum(MissilesCommitted);
        pendingMissiles = afloat.Where(u => OutOfFiringRange(u, run)).Sum(MissilesCommitted);
        CombatHooks.LogEnemyLaunches(player);
        float closestEnemy = afloat.Where(u => !u.IsAirUnit).Select(u => (float)u._geoPosition.getDistanceInMiles(player._geoPosition)).DefaultIfEmpty(float.PositiveInfinity).Min();
        pressure.Tick(run, Elapsed, defence, committed, pendingMissiles, closestEnemy);
        // An open gate promises the fleet can face those missiles, so the score alone cannot shut them out, but pressure can still cut them back.
        salvoLimit = Mathf.Max(pressure.SalvoLimit(defence, Elapsed), plan.SalvoFloor(run) * Mathf.Lerp(0.5f, 1f, pressure.Value * 2f));
        int minuteIndex = Mathf.Min((int)(Elapsed / Script.PhaseSeconds), table.Length - 1);
        var minute = table[minuteIndex];
        if (minuteIndex != minuteStarted)
        {
            minuteStarted = minuteIndex;
            // A scripted spike the plan has no place for yet is skipped.
            if (minute.Event != null && !pressure.Breather && EventFits(minute.Event, run))
            {
                Send(minute.Event, player, $"{minute.Event.Name} inbound");
                committed += minute.Event.SalvoAt(lastEdgeNm);
            }
            SendBoss(minuteIndex, player, run);
            if (minute.SetPiece != null) LaunchSetPiece(minute.SetPiece);
        }
        setPieces.Tick(player, run, ChestTier, Adopt);

        Ramp(player, run);
    }

    /// <summary>
    /// The opening, then the pulses: build to a peak, let the fight play out,
    /// relax, build again. After Left 4 Dead's director: constant combat is
    /// fatiguing, and long inactivity is boring.
    /// </summary>
    void Ramp(ObjectBase player, Run run)
    {
        if (pressure.Breather)
        {
            if (!breatherTaken) adjust = Mathf.Clamp(adjust - BreatherAdjust, AdjustMin, AdjustMax);
            breatherTaken = true;
            return;
        }
        breatherTaken = false;

        if (openingStep <= plan.Opening.Length)
        {
            if (Now < nextOpening && openingUnits.Any(u => u != null && !u.IsDestroyed)) return;
            if (openingStep < plan.Opening.Length)
            {
                openingUnits.Clear();
                openingUnits.AddRange(Send(plan.Opening[openingStep], player, null));
                nextOpening = Now + plan.OpeningSeconds;
            }
            else nextPulse = Now;
            openingStep++;
            return;
        }

        if (peakFadeUntil > 0.0)
        {
            if (Now < peakFadeUntil && spawned.Count > peakUnits / 2f) return;
            peakFadeUntil = 0.0;
            relaxUntil = Now + UnityEngine.Random.Range(RelaxMinSeconds, RelaxMaxSeconds);
            nextPulse = relaxUntil;
            Entry.Log.LogInfo($"Relax at {Clock(Elapsed)}, {spawned.Count} enemies out");
        }
        if (Now >= nextPulse) Pulse(player, run);
    }

    void Pulse(ObjectBase player, Run run)
    {
        nextPulse = Now + PulseSeconds;

        // Keeping up with what is sent raises the curve, and falling behind lowers it faster.
        if (lastPulseBudget > 0f)
        {
            float change = AdjustRate * (Mathf.Min(killedDanger / lastPulseBudget, 2f) - KeepingUpShare);
            if (change < 0f || !pressure.Warned) adjust = Mathf.Clamp(adjust + change, AdjustMin, AdjustMax);
        }
        killedDanger = 0f;

        float budget = plan.BudgetAt(Elapsed) * adjust * ThreatMultiplier;
        lastPulseBudget = budget;
        float cap = budget * WalletPulses;
        wallet = Mathf.Min(wallet + budget * PulseShare[pulseInCycle], cap);

        var bought = new List<string>();
        var bearings = new List<float>();
        for (int i = 0; i < MaxGroupsPerPulse; i++)
        {
            int room = Room;
            var menu = plan.StageAt(Elapsed).Where(g => g.Units.Length <= room && plan.Cost(g) <= cap && MayCome(g, run)).ToList();
            if (menu.Count == 0) break;
            // Dearer groups come more often, so the danger rises as the budget does. One it cannot
            // yet pay for is saved up for, or missile boats would never beat the cheap gun boats to it.
            var group = PickByCost(menu);
            int cost = plan.Cost(group);
            if (cost > wallet) break;
            wallet -= cost;
            Send(group, player, null, wave: bearings);
            committed += group.SalvoAt(lastEdgeNm);
            bought.Add(group.Name);
        }
        if (bought.Count > 0)
            Entry.Log.LogInfo($"Wave at {Clock(Elapsed)}: {string.Join(", ", bought)}; {wallet:F0} credits left, budget {budget:F1}, adjust {adjust:F2}, {spawned.Count} of {Limit:F0} out, missiles {committed} of {salvoLimit:F1}");
        else
            Entry.Log.LogInfo($"No wave at {Clock(Elapsed)}: {wallet:F0} credits, budget {budget:F1}, adjust {adjust:F2}, {spawned.Count} of {Limit:F0} out, missiles {committed} of {salvoLimit:F1}");

        if (++pulseInCycle >= PulseShare.Length)
        {
            pulseInCycle = 0;
            peakFadeUntil = Now + PeakFadeSeconds;
            peakUnits = spawned.Count;
        }
    }

    Group PickByCost(List<Group> menu)
    {
        float roll = UnityEngine.Random.value * menu.Sum(g => plan.Cost(g));
        foreach (var group in menu)
        {
            roll -= plan.Cost(group);
            if (roll <= 0f) return group;
        }
        return menu[menu.Count - 1];
    }

    /// <summary>The ship's gates, the salvo limit, and the limits on waiting boats and lone bombers.</summary>
    bool MayCome(Group group, Run run)
    {
        if (!plan.Allows(group.Units, run)) return false;
        if (group.Salvo > 0 && (pressure.Breather || committed + group.SalvoAt(lastEdgeNm) > salvoLimit)) return false;
        if (group.Waits && spawned.Count(s => Script.IsWaiting(s.unit._obp._objectIniName)) + group.Units.Length > Mathf.Max(MinWaiting, Limit / 3f)) return false;
        if (group.Domain == Domain.Air && !plan.Open(Gate.A2, run) && spawned.Count(s => s.unit.IsAirUnit) + group.Units.Length > BomberPair) return false;
        return true;
    }

    bool EventFits(Group group, Run run) =>
        group.Units.Length <= Room && plan.Cost(group) <= plan.BudgetAt(Elapsed) * adjust * ThreatMultiplier * WalletPulses && MayCome(group, run);

    /// <summary>Mini-bosses stay on the clock, whatever the ramp is doing, in the form this ship can face.</summary>
    void SendBoss(int minuteIndex, ObjectBase player, Run run)
    {
        if (minuteIndex == 0 || minuteIndex % Script.BossEveryRows != 0) return;
        int which = minuteIndex / Script.BossEveryRows - 1;
        if (which >= plan.Bosses.Length) return;
        var choice = plan.Bosses[which];
        var boss = choice.Pick(run);
        // Decided first: one without missiles comes in as a gun ship, not out at missile range.
        bool armed = boss.Salvo == 0 || boss.Domain == Domain.Air || (!choice.GunsOnly && !pressure.Breather &&
                                         plan.MissilesAllowed(boss.Units, run) && committed + boss.SalvoAt(lastEdgeNm) <= salvoLimit);
        var units = Send(boss, player, $"Mini-boss: {boss.Name}", armed: armed);
        if (armed) committed += boss.SalvoAt(lastEdgeNm);
        else TestPanel.Report($"{boss.Name} comes guns only");
        if (units.Count > 0) bosses.Add((boss.Name, units));
    }

    /// <summary>The most units out at this point in the run, set pieces apart. Bosses come regardless, but take up places.</summary>
    float Limit => Mathf.Max(1f, plan.AliveAt(Elapsed) * ThreatMultiplier);

    /// <summary>How many more units may be sent: set pieces have room of their own, and hold back only the overall limit.</summary>
    int Room => Mathf.FloorToInt(Mathf.Min(Limit - spawned.Count, Limit + Script.SetPieceRoom - spawned.Count - setPieces.Count));

    /// <summary>
    /// What a unit may yet fire at the fleet, as far as the salvo limit is
    /// concerned. A launcher's count can run far over what one salvo is (a Knox
    /// reads about 36, not 8), so a listed ship is capped at its salvo. A bomber
    /// counts as one even with bombs alone.
    /// </summary>
    static int MissilesCommitted(ObjectBase unit)
    {
        if (disarmed.Contains(unit)) return 0;
        int left = Reach.EnemyMissilesLeft(unit);
        if (unit.IsAirUnit) return Mathf.Max(1, left);
        int salvo = Script.MissilesIfListed(unit._obp._objectIniName);
        return salvo > 0 ? Mathf.Min(left, salvo) : left;
    }

    /// <summary>
    /// Still too far out to fire its missiles, so a danger for later rather than
    /// part of this salvo: a Tarantul fires from 10 nm, and can be ignored until
    /// it gets there. A ship that cannot fire them from anywhere, such as an
    /// SS-N-12 whose minimum range lies beyond its director's horizon, never counts.
    /// </summary>
    static bool OutOfFiringRange(ObjectBase unit, Run run)
    {
        if (unit.IsAirUnit) return false;
        float strike = Reach.EnemyStrikeNm(unit, run.Ship);
        if (strike <= 0f) return true;
        float nearest = run.Fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit)
            .Select(s => (float)unit._geoPosition.getDistanceInMiles(s._geoPosition))
            .DefaultIfEmpty(0f).Min();
        return nearest > strike + Script.PendingMarginNm;
    }

    /// <summary>Damage control can bring a launcher back, so ships kept out of the missile war are disarmed again each tick.</summary>
    static void KeepDisarmed()
    {
        disarmed.RemoveWhere(u => u == null || u.IsDestroyed);
        foreach (var unit in disarmed)
            foreach (var launcher in Boosts.AntiShipLaunchers(unit))
                launcher.Inoperable.Value = true;
    }

    int ChestTier => Escorts.TierFor((int)(Elapsed / Script.PhaseSeconds));

    /// <summary>Send a convoy or SAG now: at its minute, or from the test panel.</summary>
    internal void LaunchSetPiece(SetPiece piece)
    {
        var run = Run.Current;
        if (run?.Ship == null) return;
        // As with a boss, it comes regardless, but armed only if the salvo limit has room for it.
        int salvo = piece.SalvoAt(lastEdgeNm);
        bool armed = !pressure.Breather && plan.MissilesAllowed(piece.Hulls.Concat(piece.Escorts), run) && committed + salvo <= salvoLimit;
        if (armed) committed += salvo;
        try { setPieces.Launch(piece, run.Ship, run, armed ? Defence.Score(run) : 0f); }
        catch (Exception e) { Entry.Log.LogError($"Set piece {piece.Name} failed: {e}"); }
    }

    /// <summary>A set piece's escort, its charge sunk: it hunts the fleet with the rest.</summary>
    void Adopt(ObjectBase unit)
    {
        spawned.Add((unit, Now, 0));
        Spawner.Hunt(unit, Run.Current.Ship);
    }

    internal static bool IsDisarmed(ObjectBase unit) => disarmed.Contains(unit);

    /// <summary>
    /// Takes a ship's anti-ship launchers out of action, leaving its guns.
    /// Inoperable is the flag battle damage sets; the weapon selector skips
    /// inoperable mounts. Its SAMs stay up against aircraft, but CombatHooks
    /// stops them being fired at ships.
    /// </summary>
    internal static void Disarm(ObjectBase unit)
    {
        disarmed.Add(unit);
        foreach (var launcher in Boosts.AntiShipLaunchers(unit).ToList())
            launcher.Inoperable.Value = true;
    }

    /// <param name="wave">Bearings the wave's groups have taken so far; null outside a wave.</param>
    List<ObjectBase> Send(Group group, ObjectBase player, string announce, List<float> wave = null, bool armed = true)
    {
        var made = new List<ObjectBase>();
        SpawnGroup(group, player, made, wave, armed);
        foreach (var unit in made) spawned.Add((unit, Now, plan.DangerOf(unit._obp._objectIniName)));
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
            bool ship = !Ensigns.EscortsClosed(Run.Current);
            if (ship) Run.Current.ShipChests.Enqueue(ChestTier);
            TestPanel.Report($"{name} destroyed: chest of {(ship ? "a ship and " : "")}{levels} level-up{(levels > 1 ? "s" : "")}");
        }
    }

    void Win()
    {
        phase = Phase.Won;
        var run = Run.Current;
        TestPanel.Report($"Survived {Clock(Script.RunSeconds)}! Level {run.Level}, {run.Kills} kills, {run.Intercepts} intercepts.");
        // Banked runs only: a test-panel run unlocks nothing, as it banks nothing.
        if (run.Counts)
            foreach (var unlocked in Profile.Current.RecordWin(run.FlagshipType))
                TestPanel.Report($"{unlocked.Name} unlocked");
        Bank(run);
        var mission = Singleton<MissionManager>.Instance;
        mission._missionStatus = MissionManager.MissionStatus.Victory;
        if (mission._endMissionTime < 0.0) mission._endMissionTime = Now + 20.0;
    }

    internal void SpawnGroup(Group group, ObjectBase player, List<ObjectBase> into, List<float> wave = null, bool armed = true)
    {
        if (!group.IsRing)
        {
            SpawnOnBearing(group, group.Units, player, into, null, wave, armed);
            return;
        }
        // A wall: one unit on each bearing, evenly round the compass, or for
        // ships evenly over the bearings that have open water.
        List<float> bearings;
        if (group.Domain == Domain.Surface)
            bearings = Sea.ShipRing(player._geoPosition, group.Units.Length, group.MaxNm);
        else
        {
            float first = UnityEngine.Random.Range(0f, 360f);
            bearings = Enumerable.Range(0, group.Units.Length).Select(i => (first + i * 360f / group.Units.Length) % 360f).ToList();
        }
        for (int i = 0; i < group.Units.Length; i++)
            SpawnOnBearing(group, new[] { group.Units[i] }, player, into, bearings[i], null, armed);
    }

    void SpawnOnBearing(Group group, string[] types, ObjectBase player, List<ObjectBase> into, float? onBearing, List<float> wave = null, bool armed = true)
    {
        float distance = UnityEngine.Random.Range(group.MinNm, group.MaxNm);
        float bearing = onBearing ?? (group.Domain == Domain.Surface
            ? Sea.ShipBearing(player._geoPosition, ref distance, wave, Script.WaveSpreadDeg)
            : Sea.AirBearing(player._geoPosition));
        var centre = Spawner.Offset(player._geoPosition, distance, bearing);
        float heading = (bearing + 180f) % 360f;
        var made = new List<ObjectBase>();
        for (int i = 0; i < types.Length; i++)
        {
            // Members of a group sit about a mile apart, abreast of the bearing.
            var at = Sea.Abreast(centre, bearing, i);
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

        if (!armed)
            foreach (var unit in made) Disarm(unit);

        // Ships are placed once they exist, since that depends on their own
        // weapons: a missile ship 30 seconds outside its firing range, at most
        // 35 nm out; a gun ship 3 minutes outside its range; a boat that waits,
        // at the edge of the fleet's reach. The bearing may not be open water
        // all the way, so a fresh one is found; a ring member keeps its
        // bearing and comes in to where the water is.
        string where = $"{group.MinNm:F0}-{group.MaxNm:F0} nm ring";
        float? holdNm = null;
        if (group.Domain == Domain.Surface && made.Count > 0)
        {
            var fleet = Run.Current?.Fleet ?? new List<ObjectBase> { player };
            float edge = made.Min(u => Reach.SpawnDistanceNm(fleet, u));
            lastEdgeNm = edge;
            float knots = made.Min(u => u.MaxForwardSpeedInKnots);
            float strike = made.Select(u => Reach.EnemyStrikeNm(u, player)).Where(r => r > 0f).DefaultIfEmpty(0f).Min();
            string how;
            if (group.Waits && strike > 0f)
            {
                distance = edge;
                how = $", waiting: missiles reach {strike:F1}";
            }
            else if (strike > 0f)
            {
                // Holding means the fleet is re-revealed to it, so it fires from beyond the horizon.
                distance = Mathf.Min(strike, Script.StandOffMaxNm) + knots * Script.MissileApproachSeconds / 3600f;
                float hold = Mathf.Max(Mathf.Min(strike * Script.StandOffShare, Script.StandOffMaxNm), made.Max(Reach.EnemyMinimumNm) + 0.5f);
                holdNm = hold;
                how = $", holding at {hold:F1} nm (missiles reach {strike:F1})";
            }
            else
            {
                float guns = made.Min(Reach.EnemyGunReachNm);
                distance = guns + knots * Script.GunApproachMinutes / 60f;
                how = $", guns reach {guns:F1}";
            }
            distance = Mathf.Max(distance, Script.MinSpawnNm);
            float clear = Sea.ClearRunNm(player._geoPosition, bearing, distance);
            // Hunt turns them towards the fleet whichever way they now face.
            if (clear < distance)
            {
                if (onBearing == null) bearing = Sea.ShipBearing(player._geoPosition, ref distance, wave, Script.WaveSpreadDeg);
                else distance = Mathf.Max(clear, 1f);
            }
            if (holdNm is float h && h >= distance) holdNm = Mathf.Max(1f, distance - 1f);
            var front = Spawner.Offset(player._geoPosition, distance, bearing);
            for (int i = 0; i < made.Count; i++)
                Spawner.MoveTo(made[i], Sea.Abreast(front, bearing, i));
            double measured = made[0]._geoPosition.getDistanceInMiles(player._geoPosition);
            where = $"{distance:F1} nm (fleet reach {edge - Reach.ApproachNm:F1}{how}, game measures {measured:F1})";
        }
        wave?.Add(bearing);

        if (holdNm is float holdAt)
        {
            // Held on a fixed station, not a distance from the fleet, so a fleet that steams at it can close.
            var station = Spawner.Offset(player._geoPosition, holdAt, bearing);
            double until = Now + Script.StandOffMinutes * 60f;
            for (int i = 0; i < made.Count; i++)
            {
                var at = Sea.Abreast(station, bearing, i);
                standOff[made[i]] = (at, until);
                Spawner.Alert(made[i], player);
                made[i].setWaypointTask(at);
            }
        }
        else foreach (var unit in made) Spawner.Hunt(unit, player);
        into.AddRange(made);
        Entry.Log.LogInfo($"Spawned {group.Name} ({made.Count}/{types.Length}) at {where}, bearing {bearing:F0}, {Clock(Elapsed)}");
    }

    /// <summary>While the mission is held for the start: what it is waiting for, where the status line will be.</summary>
    void DrawHold()
    {
        float scale = Hud.Scale;
        if (holdScale != scale)
        {
            holdScale = scale;
            holdTitle = Hud.Text(Mathf.RoundToInt(16 * scale), Hud.Value, TextAnchor.MiddleCenter);
            holdHint = Hud.Text(Mathf.RoundToInt(12 * scale), Hud.Label, TextAnchor.MiddleCenter);
        }
        const string title = "SURVIVORS", hint = "Unpause to begin the run.";
        float pad = 16f * scale;
        float titleHeight = holdTitle.CalcSize(new GUIContent(title)).y, hintHeight = holdHint.CalcSize(new GUIContent(hint)).y;
        float width = Mathf.Max(holdTitle.CalcSize(new GUIContent(title)).x, holdHint.CalcSize(new GUIContent(hint)).x) + 2f * pad;
        var panel = new Rect((Screen.width - width) / 2f, HudTop * scale, width, titleHeight + hintHeight + 1.2f * pad);
        Hud.Box(panel);
        GUI.Label(new Rect(panel.x, panel.y + 0.6f * pad, panel.width, titleHeight), title, holdTitle);
        GUI.Label(new Rect(panel.x, panel.y + 0.6f * pad + titleHeight, panel.width, hintHeight), hint, holdHint);
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
        if (autoStartPending && holdSettledAt != null)
        {
            DrawHold();
            return;
        }
        var run = Run.Current;
        if (run == null || TestPanel.PlayerShip() == null) return;

        string clock = phase switch
        {
            Phase.Won => "SURVIVED",
            Phase.Lost => "SHIP LOST",
            _ => Clock(Mathf.Max(0f, Script.RunSeconds - Elapsed)),
        };
        float toNext = Progression.XpToNext(run.Level);
        var cells = new (string label, string value, Color colour)[]
        {
            ("TIME LEFT", clock, phase == Phase.Lost ? Hud.Danger : Hud.Value),
            ("LEVEL", run.Level.ToString(), Hud.Accent),
            ("XP", $"{run.XpIntoLevel:F0} / {toNext:F0}", Hud.Value),
            ("ENEMIES", (spawned.Count + setPieces.Count).ToString(), Hud.Value),
            ("KILLS", run.Kills.ToString(), Hud.Value),
            ("INTERCEPTS", run.Intercepts.ToString(), Hud.Value),
        };

        // Sized for 1080p and scaled with the screen, like the game's own UI.
        // Rows are as tall as the font says, so no text is cut or wrapped.
        float scale = Hud.Scale;
        if (hudScale != scale)
        {
            hudScale = scale;
            hudLabel = Hud.Text(Mathf.RoundToInt(12 * scale), Hud.Label);
            hudValue = Hud.Text(Mathf.RoundToInt(16 * scale), Hud.Value, style: FontStyle.Bold);
            hudLabel.wordWrap = hudValue.wordWrap = false;
        }
        float pad = 16f * scale, gap = 28f * scale, barHeight = 4f * scale;
        float labelHeight = hudLabel.CalcSize(new GUIContent("XP")).y, valueHeight = hudValue.CalcSize(new GUIContent("0")).y;
        float height = pad * 0.6f + labelHeight + valueHeight + pad * 0.6f + barHeight;
        var widths = cells.Select(c => Mathf.Max(hudLabel.CalcSize(new GUIContent(c.label)).x, hudValue.CalcSize(new GUIContent(c.value)).x)).ToArray();
        float width = widths.Sum() + gap * (cells.Length - 1) + 2f * pad;

        // The ensigns flown, at the panel's right-hand end.
        float flagsWidth = EnsignHud.Width(run, scale);
        if (flagsWidth > 0f) width += gap + flagsWidth;

        // The slots: a ring for each weapon slot, then each fleet line taken, wide enough to name them.
        width = Mathf.Max(width, Rings.RowWidth(run, scale));
        var panel = new Rect((Screen.width - width) / 2f, HudTop * scale, width, height);
        Hud.Box(panel);

        float x = panel.x + pad, top = panel.y + pad * 0.6f;
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0) Hud.Fill(new Rect(x - gap / 2f, top, 1f, labelHeight + valueHeight), Hud.Edge);
            GUI.Label(new Rect(x, top, widths[i], labelHeight), cells[i].label, hudLabel);
            hudValue.normal.textColor = cells[i].colour;
            GUI.Label(new Rect(x, top + labelHeight, widths[i], valueHeight), cells[i].value, hudValue);
            x += widths[i] + gap;
        }

        // Progress to the next level, along the panel's foot.
        float progress = Mathf.Clamp01(run.XpIntoLevel / toNext);
        var track = new Rect(panel.x + 1f, panel.yMax - barHeight - 1f, panel.width - 2f, barHeight);
        Hud.Fill(track, Hud.Dark);
        Hud.Fill(new Rect(track.x, track.y, track.width * progress, track.height), Hud.Red);

        // The slots: a row of rings under the panel, as wide as it. Hovering one opens its panel below.
        var slots = new Rect(panel.x, panel.yMax + 6f * scale, panel.width, Rings.SlotHeight(scale));
        Rings.DrawSlots(slots, run, scale);
        if (flagsWidth > 0f)
            EnsignHud.Draw(new Rect(panel.xMax - pad - flagsWidth, top, flagsWidth, labelHeight + valueHeight), run, slots, scale);
    }

    static string Clock(float seconds) => $"{(int)seconds / 60:D2}:{(int)seconds % 60:D2}";
}
