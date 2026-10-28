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
    static readonly (string label, string type)[] Enemies =
    {
        ("Osa I", "wp_ptg_osa1"),
        ("Sovremenny", "wp_em_sovremenny"),
        ("Slava", "wp_rkr_slava"),
    };
    static readonly float[] Distances = { 10f, 20f, 40f };
    const int MaxEvents = 8;

    static readonly List<string> events = new List<string>();

    static readonly string[] Tabs = { "Detection", "Weapons" };
    const float MastStepMetres = 30f;
    const float RefreshSeconds = 1f;

    bool visible;
    Rect window = new Rect(40, 80, 640, 640);
    Vector2 scroll;
    float distanceNm = 20f;
    bool mouseOver;
    int tab;
    List<string> detection = new List<string>();
    float nextRefresh;

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
        if (!visible) return;
        window = GUILayout.Window(GetInstanceID(), window, Draw, "Survivors test panel  (Ctrl+F6)");
    }

    void Draw(int id)
    {
        var ship = PlayerShip();
        if (ship == null)
        {
            GUILayout.Label("No player ship: start a mission (Scenarios > Survivors (test)).");
            GUI.DragWindow();
            return;
        }

        GUILayout.Label($"{ship.Name.Value} ({ship._obp._objectIniName})   t={GameTime.missionElapsedTime / 60:F1} min   XP {KillLog.Xp:F1}");

        GUILayout.Label("Spawn enemy, sent toward you:");
        GUILayout.BeginHorizontal();
        foreach (float d in Distances)
            if (GUILayout.Toggle(Mathf.Approximately(distanceNm, d), $"{d:F0} nm", "Button")) distanceNm = d;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        foreach (var (label, type) in Enemies)
            if (GUILayout.Button(label)) Run(() => SpawnEnemy(ship, type, distanceNm));
        GUILayout.EndHorizontal();

        GUILayout.Label("Apply to your ship:");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"Mast +{MastStepMetres:F0} m")) Run(() => RaiseMast(ship));
        if (GUILayout.Button("Low-alt search")) Run(() => LowAltitudeSearch(ship));
        if (GUILayout.Button("Radar gain +3 dB")) Run(() => RaiseGain(ship));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Launcher reload /2")) Run(() => HalveLauncherReload(ship));
        if (GUILayout.Button("Rearm")) Run(() => Rearm(ship));
        GUILayout.EndHorizontal();

        tab = GUILayout.Toolbar(tab, Tabs);
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(300));
        if (tab == 0)
        {
            foreach (string line in detection) GUILayout.Label(line);
        }
        else
        {
            foreach (var launcher in ship._obp._launcherWeapons)
            {
                int loaded = launcher._containers.Sum(c => c._weapons.Count);
                int cells = launcher._containers.Sum(c => c._initialAmmoCount);
                GUILayout.Label($"Launcher {launcher._vwp._systemName}: reload {launcher._vwp._magazineReloadTime:F1} s, loaded {loaded}/{cells}");
            }
            foreach (var magazine in ship._obp._magazineSystems)
                foreach (var ammo in magazine._ammunitionList.Where(a => a != null))
                    GUILayout.Label($"Magazine {ammo._ap._ammunitionFileName}: {ammo._ap._magazineAmmoCount}/{magazine._initialAmmoCount[ammo]}");
        }
        GUILayout.EndScrollView();

        GUILayout.Label("Events:");
        foreach (string line in events) GUILayout.Label(line);

        GUI.DragWindow();
    }

    static void Run(Action action)
    {
        try { action(); }
        catch (Exception e) { Report($"Failed: {e.Message}"); Entry.Log.LogError(e); }
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
        var at = Offset(player._geoPosition, distanceNm, bearing);
        float headingToPlayer = (bearing + 180f) % 360f;

        var objects = Singleton<ObjectsManager>.Instance;
        var ship = objects.createShip("random", "default", type, objects._listOfAllObjects.Count + 1,
            Singleton<SceneCreator>.Instance.GeoCenterPosition, at, 3,
            new Vector3(0f, headingToPlayer, 0f), Globals._enemyTaskforce);
        if (ship == null)
        {
            Report($"createShip returned null for {type}");
            return;
        }

        // The same post-spawn setup the game's own CivilianRoute performs.
        ship.transform.position = Utils.longLatToLocalV3(at, Globals._currentCenterTile);
        Physics.SyncTransforms();
        ship._geoPosition = at;
        ship.DesiredAltitude.Value = ship.transform.position.y;
        ship._velocityInKnots = ship._telegraph * ship.MaxForwardSpeedInKnots / 5f;
        ship._velocityInUnity = ship._velocityInKnots * 0.0076554087f;

        // Spawned ships do not close on their own, and do not know we exist.
        ship.setWaypointTask(player._geoPosition);
        Utils.RevealContactToObject(ship, player, "Comms", 3);
        ship._weaponStatus = ObjectBase.WeaponStatus.Free;

        Report($"Spawned {type} '{ship.Name.Value}' {distanceNm:F0} nm, bearing {bearing:F0}");
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

    /// <summary>
    /// GeoPosition.GeoOffsetByDistanceOnHeading ignores cos(latitude) for
    /// longitude, which stretches east-west offsets away from the equator.
    /// </summary>
    static GeoPosition Offset(GeoPosition from, float distanceNm, float bearingDeg)
    {
        double rad = bearingDeg * Math.PI / 180.0;
        double dLat = distanceNm / 60.0 * Math.Cos(rad);
        double cosLat = Math.Max(0.01, Math.Cos(from._latitude * Math.PI / 180.0));
        double dLon = distanceNm / 60.0 * Math.Sin(rad) / cosLat;
        return new GeoPosition(from._latitude + dLat, from._longitude + dLon, 0.0);
    }
}
