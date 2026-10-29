using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SeaPower;
using SeapowerUI.ViewModels;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>What a run starts with: the flagship, and any ships beside it from the start.</summary>
sealed class Loadout
{
    public readonly StartingShip Flagship;
    public readonly List<Escort> Ships = new List<Escort>();

    public Loadout(StartingShip flagship) => Flagship = flagship;
}

/// <summary>A ship a run can start in.</summary>
sealed class StartingShip
{
    public readonly string Name, Type, Variant;

    public StartingShip(string name, string type, string variant)
    {
        Name = name;
        Type = type;
        Variant = variant;
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
        new StartingShip("Oliver Hazard Perry", "usn_ffg_oliver_hazard_perry", "Variant1"),
        new StartingShip("Kidd", "usn_ddg_kidd", "Variant3"),
        new StartingShip("Long Beach", "usn_cgn_long_beach_83", "Variant1"),
        new StartingShip("Iowa", "usn_bb_iowa", "Variant1"),
        new StartingShip("Sverdlov", "wp_kr_sverdlov", "Variant1"),
    };

    const double Latitude = 62.0, Longitude = -14.0;
    /// <summary>Starting ships line up abeam of the flagship, this far apart.</summary>
    const double ShipSpacingNm = 1.5;
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
        Entry.Log.LogInfo($"Launching a run in {loadout.Flagship.Name} with {loadout.Ships.Count} more ship(s): {path}");
    }

    /// <summary>The mission file: the Survivors missions' open water, with the loadout's ships in it.</summary>
    static string Mission(Loadout loadout)
    {
        string flagship = loadout.Flagship.Name;
        string enemy = loadout.Flagship.Soviet ? "NATO" : "Soviet";
        var ini = new StringBuilder();
        ini.AppendLine("; Written by Sea Power Survivors for one run; rewritten at every launch.");
        ini.AppendLine();
        ini.AppendLine("[Language_en]");
        ini.AppendLine($"Name=Survivors - {flagship}");
        ini.AppendLine($"Description=Survive 75 minutes of game time against {enemy} ships and aircraft in a {flagship}, played at 5x time compression: about fifteen real minutes.");
        ini.AppendLine("Taskforce1StartMessage=<size=40>Survivors</size>|Survive 75 minutes. The run sets 5x time compression; level-ups keep it. Kills and intercepted missiles earn XP; every level rearms the fleet.\\nA mini-boss every twelve minutes pays a chest. The attack begins in ten seconds.|Good hunting");
        ini.AppendLine("Taskforce1DefeatMessage=<color=red>Ship lost</color>|The run is over.");
        ini.AppendLine();
        ini.AppendLine("[General]");
        ini.AppendLine("Type=Scenario");
        ini.AppendLine();
        ini.AppendLine("[Environment]");
        ini.AppendLine("Date=1985,6,15");
        ini.AppendLine("Time=10,0");
        ini.AppendLine("ConvertTimeToLocal=False");
        ini.AppendLine("SeaState=2");
        ini.AppendLine("WindDirection=SW");
        ini.AppendLine("Clouds=Clear");
        ini.AppendLine(Invariant($"MapCenterLatitude={Latitude:F1}"));
        ini.AppendLine(Invariant($"MapCenterLongitude={Longitude:F1}"));
        ini.AppendLine("LoadBackgroundData=False");
        ini.AppendLine();
        ini.AppendLine("[Mission]");
        ini.AppendLine("Difficulty=2");
        ini.AppendLine("PlayerTaskforce=Taskforce1");
        ini.AppendLine("EnemyTaskforce=Taskforce2");
        ini.AppendLine($"NumberOfTaskforce1Vessels={1 + loadout.Ships.Count}");
        ini.AppendLine("NumberOfTriggers=1");

        Vessel(ini, 1, loadout.Flagship.Type, loadout.Flagship.Variant, 0);
        for (int i = 0; i < loadout.Ships.Count; i++)
        {
            // Alternately to starboard and port, stepping outwards.
            int step = i / 2 + 1;
            Vessel(ini, i + 2, loadout.Ships[i].Type, EscortVariant, i % 2 == 0 ? step : -step);
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

    /// <summary>One ship, heading north, <paramref name="abeam"/> spacings east of the flagship (west if negative).</summary>
    static void Vessel(StringBuilder ini, int index, string type, string variant, int abeam)
    {
        double nmPerDegreeLongitude = 60.0 * Math.Cos(Latitude * Math.PI / 180.0);
        double longitude = Longitude + abeam * ShipSpacingNm / nmPerDegreeLongitude;
        ini.AppendLine();
        ini.AppendLine($"[Taskforce1Vessel{index}]");
        ini.AppendLine($"Type={type}");
        ini.AppendLine($"VariantReference={variant}");
        ini.AppendLine("CrewSkill=Trained");
        ini.AppendLine(Invariant($"GeoPosition={Latitude:F6},{longitude:F6},0"));
        ini.AppendLine("Heading=0");
        ini.AppendLine("Telegraph=3");
        ini.AppendLine("RadarsActive=True");
        ini.AppendLine("WeaponStatus=Free");
    }

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
