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

        // Argentine.
        ["ara_fs_drummond"] = 8f,
        ["ara_dd_sumner_fram2"] = 12f,
        ["ara_dd_comodoro_py"] = 12f,
        ["ara_ddg_hercules"] = 20f,
        ["ara_cl_belgrano"] = 40f,
        ["ara_cv_veinticinco_de_mayo"] = 60f,
        ["ara_pucara"] = 2f,
        ["ara_p-95"] = 2f,
        ["ara_a-4p"] = 3f,
        ["ara_a-4q"] = 3f,
        ["ara_dagger"] = 4f,
        ["fr_super_etendard"] = 6f,

        // British.
        ["rn_lsl_roundtable"] = 4f,
        ["rn_lpd_fearless"] = 14f,
        ["rn_ao_leaf"] = 14f,
        ["rn_ff_rothesay"] = 10f,
        ["rn_ff_leander"] = 10f,
        ["rn_ff_leander_batch2"] = 12f,
        ["rn_ff_amazon"] = 15f,
        ["rn_ff_broadsword"] = 18f,
        ["rn_ddg_sheffield"] = 18f,
        ["rn_ddg_county_batch2_75"] = 25f,
        ["rn_ddg_bristol"] = 30f,
        ["rn_cvs_invincible"] = 60f,
        ["rn_frs1"] = 4f,
        ["raf_gr3"] = 4f,
        ["raf_vulcan"] = 10f,
        ["rn_nimrod_mr2"] = 10f,

        // Chinese.
        ["plan_pt_shanghai2"] = 2f,
        ["plan_k_type_037"] = 3f,
        ["plan_ptg_type_024"] = 3f,
        ["plan_ptg_type_021"] = 4f,
        ["plan_ff_chengdu"] = 8f,
        ["plan_ff_jianghu1"] = 12f,
        ["plan_ffg_jiangdong"] = 12f,
        ["plan_ff_jianghu3"] = 15f,
        ["plan_dd_luda1"] = 25f,
        ["plan_ap_qiongsha"] = 14f,
        ["plan_lst_yukan"] = 14f,
        ["pla_q-5a"] = 4f,
        ["pla_h-6a"] = 8f,
        ["pla_h-6d"] = 8f,

        // Japanese.
        ["jmsdf_dd_yamagumo"] = 8f,
        ["jmsdf_dd_minegumo"] = 8f,
        ["jmsdf_dd_takatsuki"] = 12f,
        ["jmsdf_ddh_haruna"] = 20f,
        ["jmsdf_dd_hatsuyuki"] = 18f,
        ["jmsdf_dd_takatsuki_84"] = 20f,
        ["jmsdf_ddg_tachikaze_84"] = 25f,
        ["jasdf_f-1"] = 5f,

        // US hulls the mod sails under other flags (mod/vessels).
        ["ara_pb_pgm-39"] = 2f,
        ["rn_pb_pgm-39"] = 2f,
        ["jmsdf_pb_pgm-39"] = 2f,
        ["jmsdf_phm_pegasus"] = 5f,

        // The merchants the newer navies' convoys add.
        ["civ_ms_freighter_b"] = 12f,
        ["civ_ms_freighter_d"] = 12f,
        ["civ_ms_roro_a"] = 14f,
        ["civ_ms_roro_b"] = 14f,
        ["civ_ms_car_carrier_a"] = 16f,

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
    public static float ForUnit(ObjectBase unit) => Mathf.Max(1f, Mathf.Round(1.6f * Mathf.Sqrt(Worth(unit))));

    /// <summary>The table's worth of a unit, before XP's compression.</summary>
    static float Worth(ObjectBase unit)
    {
        string type = unit._obp?._objectIniName;
        return type != null && UnitXp.TryGetValue(type, out float xp) ? xp : unit.IsAirUnit ? 3f : 5f;
    }

    /// <summary>
    /// Sea Power's own mission score, worked out as its debrief does
    /// (<c>MissionDebriefViewModel.CalculateEndMissionData</c>): enemy losses
    /// less ours, by each unit's <c>UnitScoreValue</c>, times ten. Kept line
    /// for line with the game, including that a damaged unit's share replaces
    /// its side's last one rather than adding to it, so the two always agree.
    /// </summary>
    public static int MissionScore()
    {
        var damaged = new Dictionary<Taskforce, float>();
        var neutralsHit = new Dictionary<Taskforce, float>();
        var civiliansHit = new Dictionary<Taskforce, float>();
        float Of(Dictionary<Taskforce, float> scores, Taskforce side) => scores.TryGetValue(side, out float value) ? value : 0f;

        foreach (var unit in Singleton<ObjectsManager>.Instance._listOfAllUnits)
        {
            if (unit == null || unit.Disabled || unit.IsDestroyed || unit._obp == null || unit._taskforce == null
                || unit._obp._unitRoles.Contains(ObjectBaseParameters.UnitRoles.Deco)
                || unit._obp._unitRoles.Contains(ObjectBaseParameters.UnitRoles.SeaMine))
                continue;
            float damage = unit.Compartments != null ? 1f - unit.Compartments.IntegrityPercentage / 100f
                : unit.IsAirUnit || unit._totalDamagePoints < 0.1f ? 0f
                : unit._overallDamage / unit._totalDamagePoints;
            if (damage < 0.11f) continue;
            float share = unit._obp._unitScoreValue * Mathf.Min(damage, 1f);
            damaged[unit._taskforce] = share;

            Taskforce attacker = null;
            float most = -1f;
            foreach (var hit in unit.Info._damageByTaskforce)
                if (hit.Value > most) { most = hit.Value; attacker = hit.Key; }
            if (attacker == null || unit._taskforce != Globals._neutralTaskforce) continue;
            var tally = unit._obp._isCivilian ? civiliansHit : neutralsHit;
            tally[attacker] = Of(tally, attacker) + share;
        }

        float enemyLoss = 0f, enemyCivilianLoss = 0f, friendlyLoss = 0f, playerCivilianLoss = 0f;
        foreach (var side in Singleton<TaskforceManager>.Instance._taskForces)
        {
            float loss = side._lossScore + Of(damaged, side) + Of(neutralsHit, side) * (float)side._roe;
            float civilian = (side._neutralMilitaryKillScore + Of(civiliansHit, side) + side._civKillScore) * (float)side._roe;
            switch (Globals._playerTaskforce.RelationshipTo(side))
            {
                case RelationsState.Hostile: enemyLoss += loss; enemyCivilianLoss += civilian; break;
                case RelationsState.Friendly: friendlyLoss += loss; break;
            }
            if (side == Globals._playerTaskforce) playerCivilianLoss += civilian;
        }
        foreach (var objective in Singleton<MissionManager>.Instance.Objectives)
        {
            if (objective._isCanceled) continue;
            if (objective._isFailed) friendlyLoss -= objective._failedScore;
            else if (objective._isCompleted) friendlyLoss -= objective._completedScore;
            else if (objective._failOnMissionEnd) friendlyLoss -= objective._failedScore;
            else if (objective._completeOnMissionEnd) friendlyLoss -= objective._completedScore;
        }
        return Mathf.FloorToInt((enemyLoss + enemyCivilianLoss - friendlyLoss - playerCivilianLoss) * 10f);
    }

    /// <summary>Surviving the full run doubles the score.</summary>
    public const int WinScoreFactor = 2;

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
