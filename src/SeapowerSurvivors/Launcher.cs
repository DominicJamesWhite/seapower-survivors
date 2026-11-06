using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SeaPower;
using SeapowerUI.ViewModels;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>What a run starts with: where, the flagship, and any ships and flights beside it from the start.</summary>
sealed class Loadout
{
    public readonly StartingShip Flagship;
    public readonly Area Area;
    /// <summary>Written into the mission beside the flagship.</summary>
    public readonly List<Escort> Ships = new List<Escort>();
    /// <summary>Spawned airborne as the run starts, the way chest flights join.</summary>
    public readonly List<Escort> Flights = new List<Escort>();

    public Loadout(StartingShip flagship, Area area)
    {
        Flagship = flagship;
        Area = area;
    }
}

/// <summary>A ship a run can start in.</summary>
sealed class StartingShip
{
    public readonly string Name, Type, Variant, Fit;

    public StartingShip(string name, string type, string variant, string fit = null)
    {
        Name = name;
        Type = type;
        Variant = variant;
        Fit = fit;
    }

    /// <summary>The run fights the other navy, so a Soviet flagship faces NATO.</summary>
    public bool Soviet => Type.StartsWith("wp_", StringComparison.Ordinal);
}

/// <summary>
/// Starts a run from the main menu without a scenario: writes a mission file
/// for the chosen loadout and loads it the way the Scenarios menu's Play
/// button does, by pointing <c>Globals.currentMissionFilePath</c> at it and
/// calling <c>MissionManager.DoLoad</c>.
/// </summary>
/// <remarks>
/// <c>DoLoad</c> loads the mission scene on top of the menu's background
/// scene, so it only works from the main menu. The file lives in the game's
/// persistent data folder, where the Scenarios menu does not list it, and is
/// rewritten for every launch.
/// </remarks>
static class Launcher
{
    internal static readonly StartingShip[] Flagships =
    {
        new StartingShip("Sverdlov", "wp_kr_sverdlov", "Variant1"),
        new StartingShip("Oliver Hazard Perry", "usn_ffg_oliver_hazard_perry", "Variant1"),
        new StartingShip("Kidd", "usn_ddg_kidd", "Variant3"),
        new StartingShip("Sovremenny", "wp_em_sovremenny", "Variant1", "Late"),
        new StartingShip("Long Beach", "usn_cgn_long_beach_83", "Variant1"),
        new StartingShip("Slava", "wp_rkr_slava", "Variant1"),
        new StartingShip("Iowa", "usn_bb_iowa", "Variant1"),
        new StartingShip("Bunker Hill", "usn_cg_bunker_hill", "Variant1", "Late"),
        new StartingShip("Kirov", "wp_rkr_kirov", "Variant1"),
        // Its air group comes from its own ini; the player launches it.
        new StartingShip("Kiev", "wp_takr_kiev", "Variant1"),
    };

    /// <summary>The weapon fit our ships of this type use, whichever way they join; null for the game's default.</summary>
    internal static string FitOf(string type) => Flagships.FirstOrDefault(s => s.Type == type)?.Fit;

    /// <summary>Starting ships line up abeam of the flagship, this far apart.</summary>
    const float ShipSpacingNm = 1.5f;
    /// <summary>The run starts at this local hour wherever it is fought.</summary>
    const int LocalHour = 10;
    /// <summary>Ships other than the flagship are placed in the ini by variant; every ship offered has one.</summary>
    const string EscortVariant = "Variant1";

    /// <summary>The loadout of the run being launched or played. Null when the mission is not ours.</summary>
    internal static Loadout Current { get; private set; }
    /// <summary>True from the click until the mission's taskforces exist, so one click launches once.</summary>
    internal static bool Launching;

    static string MissionPath => Path.Combine(Application.persistentDataPath, "SeapowerSurvivors", "Survivors run.ini");

    internal static bool AtMainMenu =>
        Singleton<MenuSystemViewModel>.InstanceExists(false) && !Singleton<MissionManager>.InstanceExists(false);

    /// <summary>True when the mission loaded is one this launcher wrote.</summary>
    internal static bool IsLaunchedMission()
    {
        string path = Globals.currentMissionFilePath;
        return Current != null && !string.IsNullOrEmpty(path) &&
               string.Equals(Path.GetFullPath(path), Path.GetFullPath(MissionPath), StringComparison.OrdinalIgnoreCase);
    }

    internal static void Launch(Loadout loadout)
    {
        if (Launching || !AtMainMenu) return;
        string path = MissionPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, Mission(loadout));
        // The briefing, camera and terrain read the mission through the ini
        // cache, which would otherwise still hold the last launch's file.
        IniHandler.invalidateCache(path);

        Current = loadout;
        Launching = true;
        Globals.currentMissionFilePath = path;
        try { MissionManager.DoLoad(); }
        catch
        {
            Launching = false;
            throw;
        }
        Entry.Log.LogInfo($"Launching a run in {loadout.Area.Name} in {loadout.Flagship.Name} with {loadout.Ships.Count} more ship(s) and {loadout.Flights.Count} flight(s): {path}");
    }

    /// <summary>The mission file: the loadout's ships in the middle of its area's deployment box, facing the enemy's waters.</summary>
    static string Mission(Loadout loadout)
    {
        string flagship = loadout.Flagship.Name;
        string enemy = loadout.Flagship.Soviet ? "NATO" : "Soviet";
        var area = loadout.Area;
        var start = area.PlayerStart;
        float heading = start.HeadingToPosition(area.EnemyWaters);
        // The game's clock is Zulu: an hour for every 15 degrees of longitude.
        int zulu = ((LocalHour - (int)Math.Round(start._longitude / 15.0)) % 24 + 24) % 24;
        var ini = new StringBuilder();
        ini.AppendLine("; Written by Sea Power Survivors for one run; rewritten at every launch.");
        ini.AppendLine();
        ini.AppendLine("[Language_en]");
        ini.AppendLine($"Name=Survivors - {flagship}, {area.Name}");
        ini.AppendLine($"Description=Survive 20 minutes of game time in the {area.Name} against {enemy} ships and aircraft in a {flagship}.");
        ini.AppendLine("Taskforce1StartMessage=<size=40>Survivors</size>|Survive 20 minutes. Kills and intercepted missiles earn XP; every level rearms the fleet.\\nA mini-boss every twelve minutes pays a chest. The run begins when you unpause.|Good hunting");
        ini.AppendLine("Taskforce1DefeatMessage=<color=red>Ship lost</color>|The run is over.");
        ini.AppendLine();
        ini.AppendLine("[General]");
        ini.AppendLine("Type=Scenario");
        ini.AppendLine();
        ini.AppendLine("[Environment]");
        ini.AppendLine("Date=1985,6,15");
        ini.AppendLine($"Time={zulu},0");
        ini.AppendLine("ConvertTimeToLocal=False");
        ini.AppendLine("SeaState=2");
        ini.AppendLine("WindDirection=SW");
        ini.AppendLine("Clouds=Clear");
        ini.AppendLine(Invariant($"MapCenterLatitude={start._latitude:F4}"));
        ini.AppendLine(Invariant($"MapCenterLongitude={start._longitude:F4}"));
        ini.AppendLine("LoadBackgroundData=False");
        ini.AppendLine();
        ini.AppendLine("[Mission]");
        ini.AppendLine("Difficulty=2");
        ini.AppendLine("PlayerTaskforce=Taskforce1");
        ini.AppendLine("EnemyTaskforce=Taskforce2");
        ini.AppendLine($"NumberOfTaskforce1Vessels={1 + loadout.Ships.Count}");
        ini.AppendLine("NumberOfTriggers=1");

        Vessel(ini, 1, loadout.Flagship.Type, loadout.Flagship.Variant, start, heading, 0);
        for (int i = 0; i < loadout.Ships.Count; i++)
        {
            // Alternately to starboard and port, stepping outwards.
            int step = i / 2 + 1;
            Vessel(ini, i + 2, loadout.Ships[i].Type, EscortVariant, start, heading, i % 2 == 0 ? step : -step);
        }

        // The director ends the run when the flagship sinks; this only
        // catches the whole fleet going down at once.
        ini.AppendLine();
        ini.AppendLine("[Trigger1]");
        ini.AppendLine("Name=Own ship destroyed");
        ini.AppendLine("Condition_Condition1_Type=HasNoUnitsOfType");
        ini.AppendLine("Condition_Condition1_Taskforce=Taskforce1");
        ini.AppendLine("Condition_Condition1_UnitType=Vessel");
        ini.AppendLine("ConditionsCompleted=<Condition1>");
        ini.AppendLine("Action_Taskforce1_Message=DefeatMessage");
        ini.AppendLine("Action_EndMission=True");
        ini.AppendLine("Action_Victory=Taskforce2");
        return ini.ToString();
    }

    /// <summary>One ship on <paramref name="heading"/>, <paramref name="abeam"/> spacings to starboard of the flagship (port if negative).</summary>
    static void Vessel(StringBuilder ini, int index, string type, string variant, GeoPosition flagship, float heading, int abeam)
    {
        var at = abeam == 0 ? flagship : Spawner.Offset(flagship, Math.Abs(abeam) * ShipSpacingNm, (heading + (abeam > 0 ? 90f : 270f)) % 360f);
        ini.AppendLine();
        ini.AppendLine($"[Taskforce1Vessel{index}]");
        ini.AppendLine($"Type={type}");
        ini.AppendLine($"VariantReference={variant}");
        if (FitOf(type) != null) ini.AppendLine($"LoadoutVariant={FitOf(type)}");
        ini.AppendLine("CrewSkill=Trained");
        ini.AppendLine(Invariant($"GeoPosition={at._latitude:F6},{at._longitude:F6},0"));
        ini.AppendLine(Invariant($"Heading={heading:F0}"));
        ini.AppendLine("Telegraph=3");
        ini.AppendLine("RadarsActive=True");
        ini.AppendLine("WeaponStatus=Tight");
    }

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
