using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// The pick-one-of-three screen. It pauses the game for each boss chest's
/// ship, then each banked level, one after another, and holds the pause
/// until the last pick is made.
/// </summary>
public class LevelUpScreen : MonoBehaviour
{
    const int Choices = 3;
    const float CardWidth = 260f, CardHeight = 150f, Gap = 16f;

    /// <summary>True while the screen owns the pause; the time-compression patches below defer to it.</summary>
    internal static bool Holding;
    static bool resuming;
    static float resumeCompression = 1f;

    List<Upgrade> offer = new List<Upgrade>();
    List<Escort> ships = new List<Escort>();
    /// <summary>True while the cards are a boss chest's ships rather than upgrades.</summary>
    bool choosingShip;
    GUIStyle title, card;

    void Update()
    {
        var run = Run.Current;
        if (run == null || TestPanel.PlayerShip() == null) return;

        if (run.RearmsPending > 0)
        {
            run.RearmsPending = 0;
            try
            {
                run.RearmFleet();
                TestPanel.Report($"Level {run.Level}: fleet rearmed");
            }
            catch (System.Exception e) { Entry.Log.LogError($"Rearm failed: {e}"); }
        }

        if (Holding) return;
        // Nothing left to rank up: the levels still rearmed the fleet, which is reward enough.
        if (run.PendingLevels > 0 && Upgrades.Eligible(run).Count == 0) run.PendingLevels = 0;
        if (run.PendingLevels > 0 || run.ShipChests.Count > 0) Open(run);
    }

    void Open(Run run)
    {
        Holding = true;
        // Runs are played compressed; remember the rate to resume at it.
        resumeCompression = GameTime.TimeCompression > 0.05f ? GameTime.TimeCompression : 1f;
        GameTime.Pause(true);
        SetMouseCaptured(true);
        Roll(run);
    }

    void Close()
    {
        Holding = false;
        SetMouseCaptured(false);
        // The game's own resume sequence, from MissionManager's message dialog.
        resuming = true;
        try
        {
            GameTime.StopTimeCompression();
            if (resumeCompression > 1f) GameTime.SetTimeCompression(resumeCompression);
            Singleton<InputHandler>.Instance.IsInPauseMode.Value = false;
            Singleton<InputHandler>.Instance.IsInMessagePauseMode = false;
        }
        finally { resuming = false; }
    }

    void Roll(Run run)
    {
        choosingShip = run.ShipChests.Count > 0;
        if (choosingShip) ships = Escorts.Offer(run, run.ShipChests.Peek());
        else offer = Upgrades.Eligible(run).OrderBy(_ => Random.value).Take(Choices).ToList();
    }

    void Pick(Run run, Upgrade upgrade)
    {
        run.Take(upgrade);
        Next(run);
    }

    void Pick(Run run, Escort escort)
    {
        try { Escorts.Join(run, escort); }
        catch (System.Exception e) { Entry.Log.LogError($"Joining {escort.Type} failed: {e}"); }
        Next(run);
    }

    void Next(Run run)
    {
        if (choosingShip) run.ShipChests.Dequeue();
        else run.PendingLevels--;
        if (run.ShipChests.Count > 0 || (run.PendingLevels > 0 && Upgrades.Eligible(run).Count > 0)) Roll(run);
        else
        {
            run.PendingLevels = 0;
            Close();
        }
    }

    void OnGUI()
    {
        var run = Run.Current;
        if (!Holding || run == null) return;

        title ??= new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        card ??= new GUIStyle(GUI.skin.button) { fontSize = 15, alignment = TextAnchor.UpperLeft, wordWrap = true, padding = new RectOffset(12, 12, 12, 12) };

        float width = Choices * CardWidth + (Choices - 1) * Gap;
        float left = (Screen.width - width) / 2f;
        float top = Screen.height / 2f - CardHeight;

        GUI.Box(new Rect(left - 24f, top - 80f, width + 48f, CardHeight + 170f), GUIContent.none);
        if (choosingShip)
        {
            GUI.Label(new Rect(left, top - 70f, width, 44f), "CHEST: REINFORCEMENTS JOIN YOUR FLEET", title);
            for (int i = 0; i < ships.Count; i++)
            {
                var escort = ships[i];
                string adds = Escorts.Adds(run, escort);
                string kind = escort.IsAir ? (escort.Count > 1 ? $"{escort.Count} aircraft" : "Aircraft") : "Ship";
                string footer = escort.IsAir ? "Unlimited fuel; circles the fleet when idle" : "Under your command; takes every upgrade";
                string text = $"<b>{escort.Name}</b>  tier {escort.Tier}\n\n{kind}: {Escorts.Describe(escort.Roles)}" +
                              (adds.Length > 0 ? $"\n\n{adds}" : "") + $"\n\n{footer}";
                if (GUI.Button(new Rect(left + i * (CardWidth + Gap), top, CardWidth, CardHeight), text, card))
                    Pick(run, escort);
            }
        }
        else
        {
            int shownLevel = run.Level - run.PendingLevels + 1;
            string banked = run.PendingLevels > 1 ? $"   (+{run.PendingLevels - 1} more)" : "";
            GUI.Label(new Rect(left, top - 70f, width, 44f), $"LEVEL {shownLevel}{banked}", title);

            for (int i = 0; i < offer.Count; i++)
            {
                var upgrade = offer[i];
                int next = run.RankOf(upgrade) + 1;
                string rank = next == 1 ? "  NEW" : $"  rank {next}/{upgrade.MaxRank}";
                var systems = run.Fleet.Where(s => s != null && !s.IsDestroyed).SelectMany(upgrade.Systems).Distinct().ToList();
                string touches = upgrade.Kind == Kind.Crew ? "\n\nCrew" : systems.Count > 0 ? $"\n\n{string.Join(", ", systems)}" : "";
                string text = $"<b>{upgrade.Name}</b>{rank}\n\n{upgrade.Describe(next)}{touches}";
                if (GUI.Button(new Rect(left + i * (CardWidth + Gap), top, CardWidth, CardHeight), text, card))
                    Pick(run, upgrade);
            }
        }

        float row = top + CardHeight + 20f;
        GUI.enabled = run.Rerolls > 0;
        if (GUI.Button(new Rect(left, row, 160f, 36f), $"Reroll ({run.Rerolls})"))
        {
            run.Rerolls--;
            Roll(run);
        }
        GUI.enabled = true;
        if (GUI.Button(new Rect(left + width - 160f, row, 160f, 36f), "Skip")) Next(run);
    }

    static void SetMouseCaptured(bool captured)
    {
        if (Singleton<MouseControlState>.InstanceExists(false))
            Singleton<MouseControlState>.Instance.setMouseIsOverUIWindow(captured);
    }

    /// <summary>
    /// While the screen is open, nothing else may restart time: not the pause
    /// key, not the time-compression buttons, not another mod.
    /// </summary>
    [HarmonyPatch]
    static class HoldPause
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            new[] { "UnPause", "StopTimeCompression", "SetTimeCompression", "IncreaseTimeCompression", "DecreaseTimeCompression" }
                .Select(name => AccessTools.Method(typeof(GameTime), name))
                .Where(m => m != null);

        static bool Prefix() => !Holding || resuming;
    }
}
