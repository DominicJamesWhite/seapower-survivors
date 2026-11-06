using System;
using System.Collections.Generic;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Ctrl+F6 opens a window of buttons, one per risky hook, with a live readout
/// of the stats each one should change. Nothing here is gameplay; it exists
/// to prove the hooks and will be thrown away.
/// </summary>
/// <remarks>
/// Ctrl+F6 because the game binds Ctrl+F3/F5/F7, QuickStart takes Ctrl+F8,
/// SeapowerMultiplayer Ctrl+F9-F11, and Windows swallows Ctrl+Shift+0.
/// </remarks>
public class TestPanel : MonoBehaviour
{
    static readonly (string label, string type)[] SovietShips =
    {
        ("Osa I", "wp_ptg_osa1"),
        ("Sovremenny", "wp_em_sovremenny"),
        ("Slava", "wp_rkr_slava"),
    };
    static readonly (string label, string type)[] NatoShips =
    {
        ("Z-class", "rn_dd_z-class"),
        ("Battle", "rn_dd_battle"),
        ("Knox", "usn_ff_knox"),
    };
    static readonly float[] Distances = { 10f, 20f, 25f, 40f };
    static readonly float[] ThreatMultipliers = { 0.5f, 1f, 2f };
    static readonly (string label, Group group)[] AirGroups =
    {
        ("Yak-38", Script.SovietEnemy.Yaks),
        ("Tu-16", Script.SovietEnemy.Badger),
        ("Su-24A", Script.SovietEnemy.Fencers),
        ("Tu-16K stand-off", Script.SovietEnemy.BadgerKs),
    };
    const int MaxEvents = 8;

    static readonly List<string> events = new List<string>();

    static readonly string[] Tabs = { "Detection", "Weapons", "Upgrades" };
    const float MastStepMetres = 30f;
    const float RefreshSeconds = 1f;

    bool visible;
    /// <summary>Placed and sized on first show, at 1080p sizes scaled with the screen; GUILayout fits the height.</summary>
    Rect window;
    /// <summary>Where the panel is on screen, in GUI coordinates; empty while it is hidden.</summary>
    static Rect shown;
    Vector2 scroll;
    float distanceNm = 20f;
    bool mouseOver;
    int tab;
    List<string> detection = new List<string>();
    float nextRefresh;
    Vector2 launcherScroll;
    /// <summary>How tall the store's list was when last drawn, so its scroll view can fit it next time.</summary>
    float launcherContent = LauncherHeight * 2f;
    const float LauncherHeight = 380f, ReadoutHeight = 300f;
    GUIStyle heading, note, filler, launch;
    float stylesScale;

    /// <summary>Log a line and keep it on the panel's event feed.</summary>
    internal static void Report(string line)
    {
        Entry.Log.LogInfo(line);
        events.Add($"[{GameTime.missionElapsedTime / 60:F0}m] {line}");
        if (events.Count > MaxEvents) events.RemoveAt(0);
    }

    void Update()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (ctrl && Input.GetKeyDown(KeyCode.F6)) visible = !visible;

        // Keep clicks on the panel from also reaching the map. The game's
        // setter is expensive, so only call it when the state changes.
        Vector2 mouse = Input.mousePosition;
        bool over = visible && window.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
        if (over != mouseOver && Singleton<MouseControlState>.InstanceExists(false))
        {
            mouseOver = over;
            Singleton<MouseControlState>.Instance.setMouseIsOverUIWindow(over);
        }

        // OnGUI runs several times a frame; the detection pass is too heavy
        // for that, so it is rebuilt here once a second instead.
        if (visible && Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            var ship = PlayerShip();
            try { detection = ship == null ? new List<string>() : DetectionReadout.Build(ship); }
            catch (Exception e) { detection = new List<string> { $"Detection readout failed: {e.Message}" }; }
        }
    }

    void OnGUI()
    {
        if (!visible)
        {
            shown = Rect.zero;
            return;
        }
        float s = Hud.Scale;
        if (window.width == 0f) window = new Rect(40f * s, 80f * s, 640f * s, 0f);
        var skin = GUI.skin;
        GUI.skin = Hud.Skin;
        if (stylesScale != s)
        {
            stylesScale = s;
            heading = Hud.Text(Mathf.RoundToInt(12 * s), Hud.Label, style: FontStyle.Bold);
            heading.margin = new RectOffset(Mathf.RoundToInt(3 * s), 0, Mathf.RoundToInt(12 * s), Mathf.RoundToInt(2 * s));
            note = Hud.Text(Mathf.RoundToInt(12 * s), Hud.Label);
            note.wordWrap = true;
            note.margin = new RectOffset(Mathf.RoundToInt(6 * s), Mathf.RoundToInt(6 * s), 0, Mathf.RoundToInt(6 * s));
            // An invisible button-sized gap, to pad out a grid's last row.
            filler = new GUIStyle(GUIStyle.none) { stretchWidth = true, margin = GUI.skin.button.margin };
            launch = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(18 * s), fontStyle = FontStyle.Bold, fixedHeight = 44f * s };
            launch.normal = launch.onNormal;
            launch.margin = new RectOffset(launch.margin.left, launch.margin.right, Mathf.RoundToInt(10 * s), launch.margin.bottom);
        }
        window = GUILayout.Window(GetInstanceID(), window, Draw, "SURVIVORS  ·  TEST PANEL        Ctrl+F6");
        GUI.skin = skin;
        shown = window;
    }

    /// <summary>A section's title, in the HUD's small grey capitals.</summary>
    void Heading(string text) => GUILayout.Label(text.ToUpperInvariant(), heading);

    static GUILayoutOption Width(float px) => GUILayout.Width(px * Hud.Scale);
    static GUILayoutOption Height(float px) => GUILayout.Height(px * Hud.Scale);

    /// <summary>
    /// The game's UI is NoesisGUI, and each NoesisView's OnGUI uses up any
    /// mouse press that lands on its content before IMGUI sees it. The main
    /// menu covers the whole screen, so there it ate every click on the
    /// panel. Presses over the panel are kept from Noesis instead, the way
    /// SeapowerMultiplayer keeps its overlay's clicks from the map.
    /// </summary>
    [HarmonyLib.HarmonyPatch]
    static class KeepClicksFromNoesis
    {
        static System.Reflection.MethodBase TargetMethod() =>
            HarmonyLib.AccessTools.Method(HarmonyLib.AccessTools.TypeByName("NoesisView"), "ProcessEvent");

        static bool Prefix(Event __0)
        {
            bool mouse = __0.type == EventType.MouseDown || __0.type == EventType.MouseUp || __0.type == EventType.ScrollWheel;
            return !(mouse && shown.Contains(__0.mousePosition));
        }
    }

    void Draw(int id)
    {
        var ship = PlayerShip();
        if (ship == null)
        {
            if (Launcher.AtMainMenu) DrawLauncher();
            else GUILayout.Label("No player ship: start a mission (Scenarios > Survivors (test)).");
            GUI.DragWindow();
            return;
        }

        var run = global::SeapowerSurvivors.Run.Current;
        GUILayout.Label($"{ship.Name.Value} ({ship._obp._objectIniName})   t={GameTime.missionElapsedTime / 60:F1} min" +
            (run == null ? "   no run" : $"   level {run.Level}, XP {run.Xp:F0}"));

        var director = Director.Instance;
        GUILayout.BeginHorizontal();
        if (!director.Running) { if (GUILayout.Button("Start run")) Try(() => director.StartRun()); }
        else
        {
            if (GUILayout.Button("Stop run")) Try(director.StopRun);
            if (GUILayout.Button("Skip phase")) Try(director.SkipMinute);
            if (GUILayout.Button("+1 level")) Try(() => run.PendingLevels++);
            for (int tier = 1; tier <= 3; tier++)
            {
                int t = tier;
                if (GUILayout.Button($"Chest T{t}")) Try(() => run.ShipChests.Enqueue(t));
            }
            var (convoy, sag) = Script.TestSetPieces(ship);
            if (GUILayout.Button("Convoy")) Try(() => director.LaunchSetPiece(convoy));
            if (GUILayout.Button("SAG")) Try(() => director.LaunchSetPiece(sag));
        }
        GUILayout.Label("Threat", GUILayout.ExpandWidth(false));
        foreach (float m in ThreatMultipliers)
            if (GUILayout.Toggle(Mathf.Approximately(director.ThreatMultiplier, m), $"x{m}", "Button")) director.ThreatMultiplier = m;
        if (run != null)
        {
            bool airCover = run.Has.Contains(Capability.AirCover);
            if (GUILayout.Toggle(airCover, "Pretend air cover", "Button") != airCover)
            {
                if (airCover) run.Has.Remove(Capability.AirCover);
                else run.Has.Add(Capability.AirCover);
                Report(airCover ? "Air cover off: no stand-off raids" : "Air cover on: stand-off raids can spawn");
            }
        }
        GUILayout.EndHorizontal();

        Heading("Spawn enemy, sent toward you");
        GUILayout.BeginHorizontal();
        foreach (float d in Distances)
            if (GUILayout.Toggle(Mathf.Approximately(distanceNm, d), $"{d:F0} nm", "Button")) distanceNm = d;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        foreach (var (label, type) in ship._obp._objectIniName.StartsWith("wp_") ? NatoShips : SovietShips)
            if (GUILayout.Button(label)) Try(() => SpawnEnemy(ship, type, distanceNm));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Air, at their wave range:", GUILayout.ExpandWidth(false));
        foreach (var (label, group) in AirGroups)
            if (GUILayout.Button(label)) Try(() => SpawnAir(ship, group));
        GUILayout.EndHorizontal();

        Heading("Apply to your ship");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"Mast +{MastStepMetres:F0} m")) Try(() => RaiseMast(ship));
        if (GUILayout.Button("Low-alt search")) Try(() => LowAltitudeSearch(ship));
        if (GUILayout.Button("Radar gain +3 dB")) Try(() => RaiseGain(ship));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Launcher reload /2")) Try(() => HalveLauncherReload(ship));
        if (GUILayout.Button("Rearm")) Try(() => Rearm(ship));
        if (GUILayout.Button("AA report to log")) Try(() => AaReport.Log(ship));
        if (GUILayout.Button("Log fire control")) Try(FireControlTrace.LogAll);
        GUILayout.EndHorizontal();

        GUILayout.Space(8f * Hud.Scale);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < Tabs.Length; i++)
            if (GUILayout.Toggle(tab == i, Tabs[i], "Button")) tab = i;
        GUILayout.EndHorizontal();
        scroll = GUILayout.BeginScrollView(scroll, Height(ReadoutHeight));
        if (tab == 0)
        {
            foreach (string line in detection) GUILayout.Label(line);
        }
        else if (tab == 1)
        {
            foreach (var gun in ship._obp._gunWeapons)
                GUILayout.Label($"Gun {gun._vwp._systemName}: {gun._vwp._fireRatePerMinute} rpm listed, {(Boosts.ShotsPerCycle(gun) > 1 ? $"{Boosts.ShotsPerCycle(gun)}-round bursts every" : "a shell every")} {Boosts.Cycle(gun):F2} s");
            foreach (var ciws in ship._obp._ciwsWeapons)
                GUILayout.Label($"CIWS {ciws._vwp._systemName}: {ciws._fireRatePerMinute} rpm, missile intercept {ciws._missileInterceptChance:F0}");
            foreach (var launcher in ship._obp._launcherWeapons)
            {
                int loaded = launcher._containers.Sum(c => c._weapons.Count);
                int cells = launcher._containers.Sum(c => c._initialAmmoCount);
                GUILayout.Label($"Launcher {launcher._vwp._systemName}: reload {launcher._vwp._magazineReloadTime:F1} s, loaded {loaded}/{cells}");
            }
            foreach (var magazine in ship._obp._magazineSystems)
                foreach (var ammo in magazine._ammunitionList.Where(a => a != null))
                {
                    var ap = ammo._ap;
                    GUILayout.Label($"Magazine {ap._ammunitionFileName}: {ap._magazineAmmoCount}/{magazine._initialAmmoCount[ammo]}, " +
                        $"power {ap._warheadPower:F1}, Pk {ap._killProbability:F2}, {ap._maxVelocityInKnots:F0} kn, {ap._maxLaunchRangeInMiles:F0} nm, RCS {ap._maxRCS:F2}");
                }
        }
        else
        {
            if (run == null) GUILayout.Label("Start a run to rank up lines.");
            else
                foreach (var line in Upgrades.All)
                {
                    GUILayout.BeginHorizontal();
                    int rank = run.RankOf(line);
                    GUILayout.Label($"{line.Name} ({line.Kind}): rank {rank}/{line.MaxRank}" + (line.Fits(run.Fleet) ? "" : "  (no such weapons)"));
                    GUI.enabled = rank < line.MaxRank;
                    if (GUILayout.Button("+1 rank", Width(80))) Try(() => run.Take(line, Rarity.Common));
                    if (GUILayout.Button("+1 Legendary", Width(110))) Try(() => run.Take(line, Rarity.Legendary));
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                    var unlocked = Upgrades.Earned(run, line).Select(s => s.Name).ToList();
                    if (unlocked.Count > 0) GUILayout.Label($"    Unlocked: {string.Join(", ", unlocked)}");
                }
        }
        GUILayout.EndScrollView();

        Heading("Events");
        foreach (string line in events) GUILayout.Label(line);

        GUI.DragWindow();
    }

    static void Try(Action action)
    {
        try { action(); }
        catch (Exception e) { Report($"Failed: {e.Message}"); Entry.Log.LogError(e); }
    }

    /// <summary>
    /// Test aid beside the main menu's Survivors screen, sharing its choices:
    /// spend banked XP in the store, pick an area and a loadout from what is
    /// owned, and launch a run without a scenario. Anything not yet owned
    /// shows its price and buys on a click.
    /// </summary>
    void DrawLauncher()
    {
        var profile = Profile.Current;
        Heading("Bank");
        GUILayout.Label($"{profile.Bank} XP      earned {profile.Earned} over {profile.Runs} runs");
        // As tall as the list, until that would run the panel off the screen;
        // the buttons are laid out to fit the width, so it never scrolls sideways.
        float s = Hud.Scale;
        float room = Mathf.Max(200f * s, Screen.height - window.y - (300f + 28f * events.Count) * s);
        launcherScroll = GUILayout.BeginScrollView(launcherScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar,
            GUILayout.Height(Mathf.Min(launcherContent, room)));

        var area = Setup.Area(profile);
        var flagship = Setup.Flagship(profile);
        Heading("Area");
        Grid(Areas.All.Length, 2, i =>
        {
            if (GUILayout.Toggle(area == Areas.All[i], Areas.All[i].Name, "Button")) area = Areas.All[i];
        });

        Heading("Flagship");
        Grid(Launcher.Flagships.Length, 3, i =>
        {
            var item = Store.Flagship(Launcher.Flagships[i]);
            if (!profile.Owns(item.Id)) BuyButton(profile, item);
            else if (GUILayout.Toggle(flagship == Launcher.Flagships[i], item.Name, "Button")) flagship = Launcher.Flagships[i];
        });
        profile.Choose(area, flagship);

        Heading("Starting ships and flights: lit ones sail");
        var escorts = Store.StartingEscorts(flagship).ToList();
        Grid(escorts.Count, 2, i =>
        {
            var item = Store.EscortItem(escorts[i]);
            if (!profile.Owns(item.Id)) BuyButton(profile, item);
            else profile.SetAshore(item.Id, !GUILayout.Toggle(!profile.IsAshore(item.Id), item.Name, "Button"));
        });

        var fleet = Setup.Sailing(profile);
        GUILayout.Label(fleet.Count == 0 ? $"{flagship.Name} alone" : $"{flagship.Name} with {string.Join(", ", fleet.Select(e => e.Name))}");

        Heading("Permanent upgrades: every run gets them");
        foreach (var powerUp in Store.PowerUps)
        {
            int rank = profile.RankOf(powerUp);
            GUI.enabled = profile.CanBuyRank(powerUp);
            string price = rank >= powerUp.MaxRank ? "max" : $"{Store.Price(profile, powerUp)} XP";
            if (GUILayout.Button($"{powerUp.Name}   {rank}/{powerUp.MaxRank}   ·   {price}") && profile.TryBuyRank(powerUp))
                Report($"Bought {powerUp.Name} rank {rank + 1}");
            GUI.enabled = true;
            GUILayout.Label(powerUp.PerRank, note);
        }
        if (GUILayout.Button("Refund permanent upgrades", GUILayout.ExpandWidth(false)))
            Report($"Refunded {profile.Refund()} XP");
        if (Event.current.type == EventType.Repaint)
            launcherContent = GUILayoutUtility.GetLastRect().yMax + 8f * s;
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        GUILayout.Label("PROFILE", heading, GUILayout.ExpandWidth(false));
        if (GUILayout.Button("+500 XP", GUILayout.ExpandWidth(false))) profile.Grant(500);
        // Test aid: a win in the chosen flagship, as if a run in it were survived.
        if (GUILayout.Button("+Win", GUILayout.ExpandWidth(false)))
        {
            var unlocked = profile.RecordWin(flagship.Type);
            Report(unlocked.Count == 0 ? $"Win in {flagship.Name}: nothing new unlocked"
                : $"Win in {flagship.Name}: {string.Join(", ", unlocked.Select(u => u.Name))} unlocked");
        }
        if (GUILayout.Button("Wipe", GUILayout.ExpandWidth(false)))
        {
            profile.Wipe();
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button(Launcher.Launching ? "LOADING..." : "LAUNCH", launch))
            Try(() => Launcher.Launch(Setup.Loadout(profile)));
        Heading("Events");
        foreach (var line in events) GUILayout.Label(line);
    }

    /// <summary>Lay out <paramref name="count"/> buttons <paramref name="columns"/> to a row, padding the last row so its buttons are no wider.</summary>
    void Grid(int count, int columns, Action<int> draw)
    {
        for (int start = 0; start < count; start += columns)
        {
            GUILayout.BeginHorizontal();
            for (int i = start; i < start + columns; i++)
            {
                if (i < count) draw(i);
                else GUILayout.Label(GUIContent.none, filler);
            }
            GUILayout.EndHorizontal();
        }
    }

    /// <summary>A store item not yet owned: its price, greyed out until it can be bought.</summary>
    static void BuyButton(Profile profile, StoreItem item)
    {
        GUI.enabled = profile.CanBuy(item);
        if (GUILayout.Button(item.WonBy != null ? $"{item.Name}: {item.WonBy}" : $"{item.Name}: {item.Price} XP") && profile.TryBuy(item))
            Report($"Bought {item.Name} for {item.Price} XP");
        GUI.enabled = true;
    }

    internal static ObjectBase PlayerShip()
    {
        if (Globals._playerTaskforce == null) return null;
        if (!Singleton<ObjectsManager>.InstanceExists(false)) return null;
        var objects = Singleton<ObjectsManager>.Instance;
        var active = objects.getActiveObject();
        if (active != null && active.IsPlayerUnit && !active.IsDestroyed && active is Vessel) return active;
        return objects._listOfAllPlayerVesselObjects.FirstOrDefault(o => o != null && !o.IsDestroyed);
    }

    static void SpawnEnemy(ObjectBase player, string type, float distanceNm)
    {
        float bearing = UnityEngine.Random.Range(0f, 360f);
        var ship = Spawner.Ship(type, Spawner.Offset(player._geoPosition, distanceNm, bearing),
            (bearing + 180f) % 360f, Globals._enemyTaskforce);
        if (ship == null)
        {
            Report($"createShip returned null for {type}");
            return;
        }
        Spawner.Hunt(ship, player);
        Report($"Spawned {type} '{ship.Name.Value}' {distanceNm:F0} nm, bearing {bearing:F0}");
    }

    static void SpawnAir(ObjectBase player, Group group)
    {
        var made = new List<ObjectBase>();
        Director.Instance.SpawnGroup(group, player, made);
        Report(made.Count > 0
            ? $"Spawned {made.Count}x {group.Name} ({group.Loadout}) at {group.MinNm:F0}-{group.MaxNm:F0} nm, {group.AltitudeFt:F0} ft, {made[0]._velocityInKnots:F0} kn"
            : $"{group.Name} failed to spawn: see the log");
    }

    static void RaiseMast(ObjectBase ship)
    {
        // The radar horizon is the real limit against ships and sea-skimmers,
        // and it grows with the square root of antenna height. _sensorOffset
        // is added to the mount's height in SensorSystem.SensorPosition.
        foreach (var radar in ship._obp._searchRadarSystems)
        {
            float before = radar.SensorPosition.y * DetectionReadout.MetresPerUnit;
            radar._sensorOffset += MastStepMetres / DetectionReadout.MetresPerUnit;
            Entry.Log.LogInfo($"Radar {radar._systemName}: antenna {before:F0} -> {radar.SensorPosition.y * DetectionReadout.MetresPerUnit:F0} m");
        }
        Report($"Search radar antennas raised {MastStepMetres:F0} m");
    }

    static void LowAltitudeSearch(ObjectBase ship)
    {
        // Air-search radars ignore anything below _minAltitude outright, which
        // is what hides sea-skimmers from them. Surface radars are left alone:
        // the game pins their window at -10 so that they never see air targets.
        foreach (var radar in ship._obp._searchRadarSystems)
        {
            var rp = radar._radar._rp;
            if (rp._role == RadarParameters.Role.Surface) continue;
            Entry.Log.LogInfo($"Radar {radar._systemName}: min altitude {rp._minAltitude * DetectionReadout.MetresPerUnit:F0} -> 0 m");
            rp._minAltitude = 0f;
        }
        Report("Air-search radars now see down to the sea surface");
    }

    static void RaiseGain(ObjectBase ship)
    {
        // Range goes as gainFactor^(1/2) but enemy ESM exposure too, where
        // peak power would buy range at power^(1/4) for ESM at power^(1/2).
        // _gain in dB is only converted to _gainFactor in the constructor.
        foreach (var radar in ship._obp._searchRadarSystems)
        {
            var rp = radar._radar._rp;
            float before = rp._gainFactor;
            rp._gainFactor *= 2f;
            Entry.Log.LogInfo($"Radar {radar._systemName}: gain factor {before:F0} -> {rp._gainFactor:F0}");
        }
        Report("Search radar gain +3 dB");
    }

    static void HalveLauncherReload(ObjectBase ship)
    {
        foreach (var launcher in ship._obp._launcherWeapons)
        {
            float before = launcher._vwp._magazineReloadTime;
            launcher._vwp._magazineReloadTime *= 0.5f;
            Entry.Log.LogInfo($"Launcher {launcher._vwp._systemName}: reload {before:F1} -> {launcher._vwp._magazineReloadTime:F1} s");
        }
        Report("Launcher reloads halved");
    }

    static void Rearm(ObjectBase ship)
    {
        // The game's own rearm, as used by the mission trigger action.
        ship.RearmAll();
        Report("RearmAll() called");
    }
}
