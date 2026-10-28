using System.Collections.Generic;
using SeaPower;

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
        ["usn_a-7e"] = 4f,
        ["ran_a-4g"] = 3f,
        ["usmc_av-8a"] = 4f,
        ["usn_a-6e"] = 6f,
        ["usaf_b-52g"] = 15f,
    };

    public static float ForUnit(ObjectBase unit)
    {
        string type = unit._obp?._objectIniName;
        if (type != null && UnitXp.TryGetValue(type, out float xp)) return xp;
        return unit.IsAirUnit ? 3f : 5f;
    }

    /// <summary>
    /// XP from this level to the next. Cheap and quick to begin with, so the
    /// first picks arrive within a minute, then walls at 10 and 20 in the
    /// manner of the genre.
    /// </summary>
    public static float XpToNext(int level) =>
        level < 10 ? 2f * level :
        level < 20 ? 3f * level + (level == 10 ? 20f : 0f) :
        4f * level + (level == 20 ? 50f : 0f);
}
