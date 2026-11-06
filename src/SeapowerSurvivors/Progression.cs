using System.Collections.Generic;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// The XP economy. A naval battle never produces thousands of kills, so the
/// steady stream comes from shooting down missiles, which are this game's
/// swarm, and ships and aircraft pay in larger lumps.
/// </summary>
static class Progression
{
    public const float MissileIntercept = 1f;

    /// <summary>XP per kill. The game's own score values are far too small to drive a level curve.</summary>
    static readonly Dictionary<string, float> UnitXp = new Dictionary<string, float>
    {
        ["wp_pb_zhuk"] = 2f,
        ["wp_pt_stenka"] = 3f,
        ["wp_ptg_komar"] = 3f,
        ["wp_ptg_osa1"] = 4f,
        ["wp_ptg_osa2"] = 4f,
        ["wp_ptg_tarantul"] = 6f,
        ["wp_mrk_nanuchka"] = 8f,
        ["wp_bpk_kashin_mod"] = 15f,
        ["wp_rkr_kresta1"] = 20f,
        ["wp_em_sovremenny"] = 30f,
        ["wp_rkr_slava"] = 40f,
        ["wp_rkr_kirov"] = 60f,
        ["wp_takr_kiev"] = 60f,
        ["wp_yak-38"] = 3f,
        ["wp_mig-27"] = 4f,
        ["wp_su-24a"] = 5f,
        ["wp_su-24m"] = 6f,
        ["wp_tu-16"] = 8f,
        ["wp_tu-16k"] = 8f,
        ["wp_tu-22m2"] = 12f,
        ["wp_skr_petya2"] = 8f,
        ["wp_kr_sverdlov"] = 40f,

        ["usn_pb_pgm-39"] = 2f,
        ["knm_fs_sleipner"] = 4f,
        ["knm_ptg_hauk"] = 3f,
        ["knm_ptg_snogg"] = 3f,
        ["fgs_ptg_tiger"] = 4f,
        ["usn_phm_pegasus"] = 5f,
        ["rn_dd_z-class"] = 10f,
        ["rn_dd_c-class"] = 10f,
        ["rn_dd_battle"] = 12f,
        ["usn_dd_gearing_fram2"] = 12f,
        ["usn_ff_knox"] = 15f,
        ["usn_ddg_adams_early"] = 18f,
        ["fgs_ddg_lutjens"] = 18f,
        ["usn_cg_belknap"] = 25f,
        ["usn_clg_oklahoma_city"] = 35f,
        ["usn_bb_iowa"] = 60f,
        ["usn_cg_ticonderoga"] = 40f,

        // Convoys: worth more than their guns suggest, since sinking them is the point.
        ["civ_ms_poltava"] = 12f,
        ["civ_ms_yuniy_partizan"] = 10f,
        ["civ_ms_kommunist"] = 12f,
        ["civ_ms_murom"] = 12f,
        ["civ_ms_irkutsk"] = 12f,
        ["wp_bdk_alligator"] = 14f,
        ["civ_ms_freighter_a"] = 12f,
        ["civ_ms_sealift_pacific"] = 14f,
        ["civ_ms_seabee"] = 14f,
        ["civ_ms_mercur"] = 12f,
        ["usn_lst_newport"] = 14f,
        ["usn_ao_t2"] = 14f,
        ["usn_aoe_sacramento"] = 20f,
        ["civ_ms_ritina"] = 20f,
        ["civ_ms_bulk"] = 16f,

        ["usn_a-7e"] = 4f,
        ["ran_a-4g"] = 3f,
        ["usmc_av-8a"] = 4f,
        ["usn_a-6e"] = 6f,
        ["usaf_b-52g"] = 15f,
    };

    /// <summary>
    /// XP for a kill: the table's worth, compressed to 1.6 × its square root,
    /// so big ships still pay more but never a lump: a Zhuk 2, a Kashin 6, a
    /// Kirov 12. A boss's real reward is its chest.
    /// </summary>
    public static float ForUnit(ObjectBase unit)
    {
        string type = unit._obp?._objectIniName;
        float worth = type != null && UnitXp.TryGetValue(type, out float xp) ? xp : unit.IsAirUnit ? 3f : 5f;
        return Mathf.Max(1f, Mathf.Round(1.6f * Mathf.Sqrt(worth)));
    }

    /// <summary>No single award pays more than this many of the current level's XP.</summary>
    public const float MaxLevelsPerAward = 2f;

    /// <summary>Early XP is multiplied by this much at the start, fading to nothing over EarlyBoostMinutes.</summary>
    public const float EarlyBoostStart = 3f;
    public const float EarlyBoostMinutes = 7f;

    /// <summary>
    /// The early-run multiplier: ×3 at the start, falling evenly to ×1 at
    /// 7 minutes. Early kills are few and small, and a gun cruiser makes
    /// fewer still, so without it the first boss arrives before the fleet
    /// has a handful of picks.
    /// </summary>
    public static float EarlyBoost(float elapsedSeconds) =>
        Mathf.Lerp(EarlyBoostStart, 1f, Mathf.Clamp01(elapsedSeconds / (EarlyBoostMinutes * 60f)));

    /// <summary>
    /// XP from this level to the next: very cheap at first, so the first
    /// kills each give a level, then rising with the square of the level:
    /// level 10 costs 18, level 20 64, level 30 140. A 20-minute run is meant
    /// to reach about level 25-30, about 40 cards with chests: enough to fill
    /// two or three lines, not all of them, so picks matter to the end.
    /// </summary>
    public static float XpToNext(int level) => 1.5f + 0.1f * level + 0.15f * level * level;
}
