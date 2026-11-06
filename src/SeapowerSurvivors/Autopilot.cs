using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Plays a run with nobody at the keyboard, for testing: launches the saved
/// loadout from the main menu, unpauses, takes the first card at every
/// level-up, logs any fleet gun stuck on "Reloading", and quits the game
/// after a set time so the log is complete.
/// </summary>
/// <remarks>
/// Only on when <c>&lt;persistent data&gt;/SeapowerSurvivors/autopilot.txt</c>
/// exists at start-up. It is deleted at launch, so the next start is a
/// normal one. Its one optional line, <c>minutes=N</c>, is how many real
/// minutes the run plays before the game quits.
/// </remarks>
public class Autopilot : MonoBehaviour
{
    const float DefaultMinutes = 5f;
    /// <summary>Real seconds at the main menu before launching, so the menu is done setting up.</summary>
    const float MenuSettleSeconds = 5f;
    const float CheckEverySeconds = 10f;
    /// <summary>Game seconds a gun may sit on one reload before it counts as stuck; the longest stock rack reload is 30 s.</summary>
    const double StuckAfterSeconds = 60.0;

    internal static bool Active { get; private set; }

    static string TriggerPath => Path.Combine(Application.persistentDataPath, "SeapowerSurvivors", "autopilot.txt");

    float minutes = DefaultMinutes;
    float? menuSince, quitAt;
    float nextCheck;
    bool launched;
    /// <summary>Each fleet gun's remaining reload when last seen, and since when (game time) it has not changed.</summary>
    readonly Dictionary<WeaponSystemGun, (float remaining, double since)> reloads = new Dictionary<WeaponSystemGun, (float, double)>();

    void Awake()
    {
        try
        {
            if (!File.Exists(TriggerPath)) return;
            foreach (var line in File.ReadAllLines(TriggerPath))
            {
                var parts = line.Split('=');
                if (parts.Length == 2 && parts[0].Trim() == "minutes" &&
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m))
                    minutes = m;
            }
            Active = true;
            Entry.Log.LogInfo($"Autopilot on: one run of {minutes:0.#} real minutes, then quit");
        }
        catch (Exception e) { Entry.Log.LogError($"Autopilot failed to start: {e}"); }
    }

    void Update()
    {
        if (!Active) return;
        try
        {
            if (!launched) Launch();
            else Play();
        }
        catch (Exception e)
        {
            Entry.Log.LogError($"Autopilot failed: {e}");
            Quit("it failed");
        }
    }

    void Launch()
    {
        if (!Launcher.AtMainMenu || Launcher.Launching)
        {
            menuSince = null;
            return;
        }
        menuSince ??= Time.realtimeSinceStartup;
        if (Time.realtimeSinceStartup - menuSince < MenuSettleSeconds) return;
        File.Delete(TriggerPath);
        launched = true;
        Launcher.Launch(Setup.Loadout(Profile.Current));
    }

    void Play()
    {
        var run = Run.Current;
        // Held for the player at the start: the unpause is what starts the run.
        if (run == null && Director.Instance != null && !Director.Instance.Running && TestPanel.PlayerShip() != null &&
            GameTime.IsPaused() && !LevelUpScreen.Holding)
        {
            GameTime.UnPause();
            Singleton<InputHandler>.Instance.IsInPauseMode.Value = false;
            Singleton<InputHandler>.Instance.IsInMessagePauseMode = false;
        }
        if (run == null) return;
        quitAt ??= Time.realtimeSinceStartup + minutes * 60f;
        if (Time.realtimeSinceStartup >= quitAt) Quit("its time is up");
        else if (TestPanel.PlayerShip() == null) Quit("the flagship was lost");
        else if (Time.realtimeSinceStartup >= nextCheck)
        {
            nextCheck = Time.realtimeSinceStartup + CheckEverySeconds;
            CheckGuns(run);
        }
    }

    /// <summary>A gun's remaining reload only moves while the gun updates; one that holds still above zero is frozen.</summary>
    void CheckGuns(Run run)
    {
        double now = GameTime.missionElapsedTime;
        var stuck = new List<string>();
        foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed))
            foreach (var gun in ship._obp._gunWeapons.Where(g => g != null))
            {
                if (!gun._isReloading || gun._remainingReloadTime <= 0f)
                {
                    reloads.Remove(gun);
                    continue;
                }
                if (!reloads.TryGetValue(gun, out var seen) || seen.remaining != gun._remainingReloadTime)
                {
                    reloads[gun] = (gun._remainingReloadTime, now);
                    continue;
                }
                if (now - seen.since >= StuckAfterSeconds)
                    stuck.Add($"{ship.Name.Value} {gun._vwp._sectionName} ({gun._remainingReloadTime:0.00} s left for {now - seen.since:0} s)");
            }
        if (stuck.Count > 0) Entry.Log.LogWarning($"Autopilot: guns stuck on Reloading at {now / 60:0.0} min: {string.Join(", ", stuck)}");
    }

    void Quit(string why)
    {
        Active = false;
        var run = Run.Current;
        Entry.Log.LogInfo($"Autopilot: quitting because {why}" +
                          (run == null ? "" : $", at {GameTime.missionElapsedTime / 60:0.0} min, level {run.Level}, {run.Fleet.Count} in the fleet"));
        Application.Quit();
    }
}
