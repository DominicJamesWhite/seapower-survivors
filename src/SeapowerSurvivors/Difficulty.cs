using System;
using System.Collections.Generic;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// How hard a run is, chosen on the Survivors screen: five levels, each
/// doubling the score and each a rank of the navy sailed for (see Ranks). The first is the run as it
/// was before there was a choice. Every number is a guess, unplayed: the levels
/// above the first were made far steeper on 2026-11-12, when the top one was
/// still called too easy. From the third up the salvo limit passes the fleet's
/// defence score, so a run there can be lost whatever the build.
/// </summary>
/// <remarks>
/// A level moves what limits the director, not just how much it sends: the
/// director is built to ease off (see Pressure), so more budget alone would be
/// absorbed. It also fixes the game's own difficulty option for the run, which
/// otherwise gives two players different runs without their knowing.
/// </remarks>
sealed class Difficulty
{
    public int ScoreFactor;
    /// <summary>The game's own difficulty the run is played at, whatever the Options menu says: 2 Moderate, 3 Challenging, 4 Merciless. It sets the fleet's bonus to hit chances and to damage taken.</summary>
    public int GameDifficulty;
    /// <summary>The danger each wave may spend, against the ship's own curve (ThreatPlan.Budget).</summary>
    public float Budget = 1f;
    /// <summary>The most enemies out at once, against the ship's own curve, as far as the frame rate allows (Script.FinaleMaxAlive): more budget needs somewhere to go.</summary>
    public float Units = 1f;
    /// <summary>Most groups in one wave: a larger budget needs more of them to be spent.</summary>
    public int Groups = 4;
    /// <summary>How far the director eases off when the fleet is in trouble, 1 as at the first level to 0 not at all: the cut in pressure for a close call or a hit, the hold on pressure while the flagship is damaged, how far falling behind lowers the curve, and the relax between build-ups.</summary>
    public float Mercy = 1f;
    /// <summary>How much more damage an enemy takes to sink than at the first level.</summary>
    public float Hull = 1f;
    /// <summary>The salvo limit's share of the fleet's defence score is multiplied by this.</summary>
    public float SalvoShare = 1f;
    /// <summary>Most enemy missiles allowed at once, outside the final assault.</summary>
    public float MaxSalvo;
    /// <summary>Game seconds for pressure to climb from nothing to full.</summary>
    public float ClimbSeconds;
    /// <summary>Game seconds without new waves after the flagship is hit or an escort lost; none at 0.</summary>
    public float BreatherSeconds;
    /// <summary>Game seconds into the run at which the enemy starts firing past the game's hold-fire rule (see CombatHooks.EnemyKeepsFiring).</summary>
    public float SaturationFromSeconds;
    /// <summary>Game seconds after a missile ship fires its last anti-ship missile until it is rearmed; never at 0.</summary>
    public float ReloadSeconds;
    /// <summary>Each stage of the ship's threat plan starts this many game seconds sooner.</summary>
    public float StageShiftSeconds;
    /// <summary>Every gate is one step easier through the whole run, as it is for everyone in the final assault.</summary>
    public bool EasedGates;

    internal static readonly Difficulty[] All =
    {
        new Difficulty { ScoreFactor = 1, GameDifficulty = 2, MaxSalvo = 24f, ClimbSeconds = 120f, BreatherSeconds = 45f, SaturationFromSeconds = 480f },
        new Difficulty
        {
            ScoreFactor = 2, Budget = 2f, Units = 1.25f, Groups = 5, GameDifficulty = 3, Hull = 1.25f, SalvoShare = 1.3f, MaxSalvo = 32f, ClimbSeconds = 80f,
            Mercy = 0.75f, BreatherSeconds = 20f, SaturationFromSeconds = 240f, ReloadSeconds = 240f, StageShiftSeconds = 120f,
        },
        new Difficulty
        {
            ScoreFactor = 4, Budget = 3.5f, Units = 1.5f, Groups = 6, GameDifficulty = 4, Hull = 1.5f, SalvoShare = 1.7f, MaxSalvo = 40f, ClimbSeconds = 50f,
            Mercy = 0.5f, BreatherSeconds = 0f, SaturationFromSeconds = 120f, ReloadSeconds = 150f, StageShiftSeconds = 240f,
        },
        new Difficulty
        {
            ScoreFactor = 8, Budget = 6f, Units = 1.75f, Groups = 7, GameDifficulty = 4, Hull = 2f, SalvoShare = 2.2f, MaxSalvo = 50f, ClimbSeconds = 30f,
            Mercy = 0.25f, BreatherSeconds = 0f, SaturationFromSeconds = 0f, ReloadSeconds = 90f, StageShiftSeconds = 360f, EasedGates = true,
        },
        new Difficulty
        {
            ScoreFactor = 16, Budget = 10f, Units = 2f, Groups = 8, GameDifficulty = 4, Hull = 2.5f, SalvoShare = 3f, MaxSalvo = 64f, ClimbSeconds = 15f,
            Mercy = 0f, BreatherSeconds = 0f, SaturationFromSeconds = 0f, ReloadSeconds = 45f, StageShiftSeconds = 480f, EasedGates = true,
        },
    };

    internal int Index => Array.IndexOf(All, this);
    internal static Difficulty At(int index) => All[Mathf.Clamp(index, 0, All.Length - 1)];

    /// <summary>The damage bonus the game gives the player at Moderate, which the first level keeps.</summary>
    const float ModerateDamageBonus = 0.25f;

    /// <summary>
    /// Tougher hulls are the game's own damage bonus turned down, below nothing
    /// at the top: Blastzone.GetWeaponDamage is its only reader, for every
    /// warhead and shell of ours that hits something not ours.
    /// </summary>
    float DamageBonus => (1f + ModerateDamageBonus) / Hull - 1f;

    /// <summary>The game difficulty to go back to when the run ends; null while ours is not in force.</summary>
    static int? optionsDifficulty;

    /// <summary>
    /// Puts the level's game difficulty in force. Once a director tick, since
    /// applying the Options menu mid-run sets the game's own back.
    /// </summary>
    internal void Apply()
    {
        if (!Singleton<OptionsManager>.InstanceExists(false)) return;
        if (optionsDifficulty == null || Globals._currentDificulty != GameDifficulty)
        {
            // Not yet ours, or changed under us: either way what is there now is the player's choice, and the one to restore.
            optionsDifficulty = Globals._currentDificulty;
            Singleton<OptionsManager>.Instance.SetDifficulty(GameDifficulty);
        }
        Globals._damageBonus = DamageBonus;
    }

    /// <summary>The game difficulty as the Options menu has it, once the run is over.</summary>
    internal static void Restore()
    {
        if (optionsDifficulty is not int was) return;
        optionsDifficulty = null;
        if (Singleton<OptionsManager>.InstanceExists(false)) Singleton<OptionsManager>.Instance.SetDifficulty(was);
    }

    /// <summary>The game's names for its difficulties (language_en/ui.ini, Difficulty_2 to Difficulty_4).</summary>
    static readonly Dictionary<int, string> GameNames = new Dictionary<int, string> { [2] = "Moderate", [3] = "Challenging", [4] = "Merciless" };

    /// <summary>
    /// What choosing the level does, for its tooltip on the Survivors screen:
    /// the score and the game difficulty, then a line for each thing the player
    /// meets that differs from the first level. How the director paces itself
    /// (pressure, breathers, saturation, the salvo and unit caps) is left out.
    /// </summary>
    internal List<string> Effects()
    {
        var lines = new List<string> { $"×{ScoreFactor} score", $"{GameNames[GameDifficulty]} difficulty" };
        if (Budget > 1f) lines.Add($"×{Budget:0.#} enemy strength per wave");
        if (Hull > 1f) lines.Add($"+{(Hull - 1f) * 100f:0}% stronger enemy hulls");
        if (SalvoShare > 1f) lines.Add($"+{(SalvoShare - 1f) * 100f:0}% larger enemy salvos");
        if (ReloadSeconds >= 60f) lines.Add($"Enemy reloads after {ReloadSeconds / 60f:0.#} mins");
        else if (ReloadSeconds > 0f) lines.Add($"Enemy reloads after {ReloadSeconds:0} secs");
        if (StageShiftSeconds > 0f) lines.Add($"Heavy enemies arrive {StageShiftSeconds / 60f:0.#} min{(StageShiftSeconds == 60f ? "" : "s")} sooner");
        if (EasedGates) lines.Add("Missile ships and aircraft arrive a tier early");
        return lines;
    }
}
