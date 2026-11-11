using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>What an enemy waits for before it may come at all.</summary>
enum Gate { None, A1, A2, A3, M1, M2, M3 }

/// <summary>One of a ship's four mini-bosses: what comes, and what comes instead if the fleet is not ready.</summary>
sealed class BossPlan
{
    public readonly Group Group, Otherwise;
    public readonly Func<Run, bool> When;
    /// <summary>Never carries its missiles against this ship, whatever gates are open.</summary>
    public readonly bool GunsOnly;

    public BossPlan(Group group, bool gunsOnly = false, Func<Run, bool> when = null, Group otherwise = null)
    {
        Group = group;
        GunsOnly = gunsOnly;
        When = when;
        Otherwise = otherwise;
    }

    public Group Pick(Run run) => When == null || When(run) ? Group : Otherwise;
}

/// <summary>Who one starting ship faces and when. The tables are set by hand.</summary>
sealed class ThreatPlan
{
    /// <summary>Budget and unit limit are given at these minutes, and run straight between them.</summary>
    const float CurveStepSeconds = 300f;
    /// <summary>Stages of the run end at these game seconds.</summary>
    static readonly float[] StageEnds = { 180f, 480f, 840f };

    public Dictionary<string, int> Danger;
    public Dictionary<Gate, Func<Run, bool>> Gates;
    public float[] Budget;
    public float[] Alive;
    /// <summary>The first steps: each comes when the last has been sunk, or after OpeningSeconds.</summary>
    public Group[] Opening;
    public float OpeningSeconds = 60f;
    public Group[][] Stages;
    public BossPlan[] Bosses;
    /// <summary>Swarms and packs that come only in the final assault, on top of every stage's groups.</summary>
    public Group[] FinaleGroups;
    /// <summary>Stand-off raids timed to land in the final assault, in the order to try them, and how far out each is released.</summary>
    public (Group group, float nm, int flights)[] Raids;

    /// <param name="finale">In the final assault every gate is one step easier: the enemy throws in the next tier up from what the fleet has earned, so a fleet with no missile defence meets only the small missile boats and bombers.</param>
    public bool Open(Gate gate, Run run, bool finale = false) =>
        gate == Gate.None || Gates[gate](run) || (finale && Open(StepDown(gate), run));

    static Gate StepDown(Gate gate) => gate switch
    {
        Gate.A2 => Gate.A1,
        Gate.A3 => Gate.A2,
        Gate.M2 => Gate.M1,
        Gate.M3 => Gate.M2,
        _ => Gate.None,
    };

    public int DangerOf(string type) => Danger.TryGetValue(type, out int danger) ? danger : 0;

    /// <summary>What the group costs of this ship's budget.</summary>
    public int Cost(Group group) => group.Units.Sum(DangerOf);

    /// <summary>Whether every hull is one this ship faces, and every gate is open.</summary>
    public bool Allows(string[] units, Run run, bool finale = false) =>
        units.All(u => DangerOf(u) > 0 && Open(Threats.GateOf(u), run, finale));

    /// <summary>Whether a ship's own missiles may be used, for a boss or set piece that comes regardless.</summary>
    public bool MissilesAllowed(IEnumerable<string> units, Run run, bool finale = false) =>
        units.All(u => Open(Threats.GateOf(u), run, finale));

    /// <summary>A group that spawns this far out carries missiles that cannot be fired from any nearer, so it comes only as a timed raid.</summary>
    const float StandOffRingNm = 100f;

    /// <summary>Everything the final assault may buy from: all the stages at once, and its own swarms, but not stand-off bombers, which would spawn inside their own minimum range.</summary>
    public IEnumerable<Group> FinaleMenu => Stages.SelectMany(s => s).Concat(FinaleGroups).Where(g => g.MinNm < StandOffRingNm).Distinct();

    /// <summary>The hand-set curves are multiplied up to this by the end of the run, from minute 5: as set, they left the fleet untouched.</summary>
    const float BudgetHarden = 1.6f, AliveHarden = 1.4f;
    const float HardenFromSeconds = 300f;

    static float Harden(float seconds, float most) => Mathf.Lerp(1f, most, Mathf.InverseLerp(HardenFromSeconds, Script.RunSeconds, seconds));

    public float BudgetAt(float seconds) => Curve(Budget, seconds) * Harden(seconds, BudgetHarden);
    public float AliveAt(float seconds) => Curve(Alive, seconds) * Harden(seconds, AliveHarden);
    public Group[] StageAt(float seconds)
    {
        int stage = Array.FindIndex(StageEnds, end => seconds < end);
        return Stages[stage < 0 ? Stages.Length - 1 : stage];
    }

    static float Curve(float[] points, float seconds)
    {
        float at = Mathf.Clamp(seconds / CurveStepSeconds, 0f, points.Length - 1f);
        int low = Mathf.Min((int)at, points.Length - 2);
        return Mathf.Lerp(points[low], points[low + 1], at - low);
    }

    /// <summary>The floor under the salvo limit from the highest missile gate open: the gate is the promise that the fleet can face these.</summary>
    public float SalvoFloor(Run run, bool finale = false) =>
        Open(Gate.M3, run, finale) ? 12f : Open(Gate.M2, run, finale) ? 8f : Open(Gate.M1, run, finale) ? 4f : 0f;
}

/// <summary>
/// The hand-set tables. Danger is per ship, because the
/// same enemy is a different problem for each, and gates are per ship, because
/// a score cannot tell an old SAM from a good one.
/// </summary>
static class Threats
{
    static readonly string[] SovietHulls =
    {
        "wp_pb_zhuk", "wp_pt_stenka", "wp_skr_petya2", "wp_kr_sverdlov", "wp_ptg_komar", "wp_ptg_osa1", "wp_ptg_tarantul", "wp_mrk_nanuchka",
        "wp_yak-38", "wp_tu-16", "wp_su-24a", "wp_mig-27", "wp_su-24m", "wp_tu-16k", "wp_tu-22m2",
    };

    static readonly string[] NatoHulls =
    {
        "usn_pb_pgm-39", "knm_fs_sleipner", "rn_dd_z-class", "rn_dd_c-class", "rn_dd_battle", "usn_dd_gearing_fram2", "knm_ptg_hauk", "knm_ptg_snogg", "fgs_ptg_tiger",
        "usn_phm_pegasus", "usn_ff_knox", "usn_ddg_adams_early", "fgs_ddg_lutjens", "usn_cg_belknap", "usn_a-7e", "ran_a-4g", "usmc_av-8a", "usn_a-6e", "usaf_b-52g",
    };

    static readonly Dictionary<string, Gate> Gates = new Dictionary<string, Gate>
    {
        ["wp_ptg_komar"] = Gate.M1, ["knm_ptg_hauk"] = Gate.M1, ["knm_ptg_snogg"] = Gate.M1,
        ["wp_ptg_osa1"] = Gate.M2, ["wp_ptg_tarantul"] = Gate.M2, ["wp_mrk_nanuchka"] = Gate.M2, ["wp_bpk_kashin_mod"] = Gate.M2,
        ["fgs_ptg_tiger"] = Gate.M2, ["usn_phm_pegasus"] = Gate.M2, ["usn_ff_knox"] = Gate.M2,
        ["wp_rkr_kresta1"] = Gate.M3, ["wp_em_sovremenny"] = Gate.M3, ["wp_rkr_slava"] = Gate.M3,
        ["usn_ddg_adams_early"] = Gate.M3, ["fgs_ddg_lutjens"] = Gate.M3, ["usn_cg_belknap"] = Gate.M3, ["usn_cg_ticonderoga"] = Gate.M3,
        ["usn_clg_oklahoma_city"] = Gate.M3, ["usn_bb_iowa"] = Gate.M3,
        ["wp_yak-38"] = Gate.A1, ["wp_tu-16"] = Gate.A1, ["usn_a-7e"] = Gate.A1, ["ran_a-4g"] = Gate.A1, ["usmc_av-8a"] = Gate.A1,
        ["wp_su-24a"] = Gate.A2, ["wp_mig-27"] = Gate.A2, ["wp_su-24m"] = Gate.A2, ["usn_a-6e"] = Gate.A2,
        ["wp_tu-16k"] = Gate.A3, ["wp_tu-22m2"] = Gate.A3, ["usaf_b-52g"] = Gate.A3,

        // Argentine: bombers first, Exocet ships by the size of their salvo, Super Étendards with the heavier aircraft.
        ["ara_pucara"] = Gate.A1, ["ara_p-95"] = Gate.A1, ["ara_a-4p"] = Gate.A1, ["ara_a-4q"] = Gate.A1,
        ["ara_dagger"] = Gate.A2, ["fr_super_etendard"] = Gate.A2,
        ["ara_fs_drummond"] = Gate.M1, ["ara_dd_comodoro_py"] = Gate.M2, ["ara_dd_sumner_fram2"] = Gate.M2, ["ara_ddg_hercules"] = Gate.M3,
        // British.
        ["rn_frs1"] = Gate.A1, ["raf_gr3"] = Gate.A1, ["raf_vulcan"] = Gate.A2, ["rn_nimrod_mr2"] = Gate.A2,
        ["rn_ff_leander_batch2"] = Gate.M1, ["rn_ff_amazon"] = Gate.M2, ["rn_ff_broadsword"] = Gate.M2,
        ["rn_ddg_county_batch2_75"] = Gate.M3,
        // Chinese.
        ["pla_q-5a"] = Gate.A1, ["pla_h-6a"] = Gate.A1, ["pla_h-6d"] = Gate.A2,
        ["plan_ptg_type_024"] = Gate.M1, ["plan_ff_chengdu"] = Gate.M1, ["plan_ptg_type_021"] = Gate.M2, ["plan_ff_jianghu1"] = Gate.M2,
        ["plan_ff_jianghu3"] = Gate.M3, ["plan_dd_luda1"] = Gate.M3,
        // Japanese: the F-1 comes with bombs first; its missile flights wait on the fleet's defence, as every missile group does.
        ["jasdf_f-1"] = Gate.A1,
        ["jmsdf_phm_pegasus"] = Gate.M2, ["jmsdf_dd_hatsuyuki"] = Gate.M2, ["jmsdf_dd_takatsuki_84"] = Gate.M3, ["jmsdf_ddg_tachikaze_84"] = Gate.M3,
    };

    internal static Gate GateOf(string type) => Gates.TryGetValue(type, out var gate) ? gate : Gate.None;

    // What opens a gate. E is an escort ship with SAMs that still have rounds.
    static int E(Run run) => run.Fleet.Count(s => s != null && !s.IsDestroyed && !s.IsAirUnit && s != run.Ship &&
                                                  Boosts.SamLaunchers(s).Any(Boosts.HasSamsLeft));
    /// <summary>A fighter flight still flying. A run from the test panel may also just say it has one.</summary>
    static bool F(Run run) => run.Has.Contains(Capability.AirCover) &&
                              (!run.Counts || run.AirWing.Any(w => w.Key != null && !w.Key.IsDestroyed && (w.Value & Role.AirCover) != 0));
    static bool G(Run run, int rank) => run.RankOf(Upgrades.Guns) >= rank && run.RankOf(Upgrades.Accuracy) >= rank;
    static bool S(Run run, int rank) => run.RankOf(Upgrades.Sams) >= rank;
    static bool P(Run run, int rank) => run.RankOf(Upgrades.PointDefence) >= rank && run.RankOf(Upgrades.Countermeasures) >= rank;

    static readonly Func<Run, bool> Start = _ => true;

    static Dictionary<Gate, Func<Run, bool>> GateRow(Func<Run, bool> a1, Func<Run, bool> a2, Func<Run, bool> a3, Func<Run, bool> m1, Func<Run, bool> m2, Func<Run, bool> m3) =>
        new Dictionary<Gate, Func<Run, bool>> { [Gate.A1] = a1, [Gate.A2] = a2, [Gate.A3] = a3, [Gate.M1] = m1, [Gate.M2] = m2, [Gate.M3] = m3 };

    // The gun ships: nothing is open until escorts or upgrades open it.
    static readonly Dictionary<Gate, Func<Run, bool>> SverdlovGates = GateRow(
        r => E(r) >= 1 || F(r) || G(r, 10),
        r => E(r) >= 2 || (E(r) >= 1 && F(r)) || G(r, 15),
        r => (E(r) >= 2 || (E(r) >= 1 && F(r)) || G(r, 15)) && F(r),
        r => E(r) >= 1 || G(r, 10) || P(r, 8),
        r => E(r) >= 2 || (E(r) >= 1 && S(r, 5)) || G(r, 15),
        r => E(r) >= 2 && S(r, 5));

    static readonly Dictionary<Gate, Func<Run, bool>> IowaGates = GateRow(
        r => E(r) >= 1 || F(r) || G(r, 10),
        r => E(r) >= 2 || (E(r) >= 1 && F(r)) || G(r, 15),
        r => (E(r) >= 2 || (E(r) >= 1 && F(r)) || G(r, 15)) && F(r),
        r => E(r) >= 1 || G(r, 10) || P(r, 4),
        r => E(r) >= 2 || (E(r) >= 1 && S(r, 5)) || G(r, 15) || P(r, 8),
        r => E(r) >= 2 && S(r, 5));

    static readonly Dictionary<Gate, Func<Run, bool>> PerryGates = GateRow(
        Start,
        r => E(r) >= 1 || F(r) || S(r, 5),
        r => (E(r) >= 1 || S(r, 5)) && F(r),
        Start,
        r => E(r) >= 1 || S(r, 5),
        r => E(r) >= 2 || (E(r) >= 1 && S(r, 5)) || S(r, 10));

    static readonly Dictionary<Gate, Func<Run, bool>> KievGates = GateRow(
        Start, Start, F,
        Start,
        r => E(r) >= 1 || S(r, 5),
        r => E(r) >= 2 || (E(r) >= 1 && S(r, 5)) || S(r, 10));

    // The missile ships: only the heaviest salvos and stand-off raids wait.
    static readonly Dictionary<Gate, Func<Run, bool>> MissileShipGates = GateRow(
        Start, Start, F,
        Start, Start,
        r => E(r) >= 1 || S(r, 5));

    static ThreatPlan Soviet(int[] danger, Dictionary<Gate, Func<Run, bool>> gates, float[] budget, float[] alive, Group[][] stages, BossPlan[] bosses, Group[] opening = null) =>
        Build(SovietHulls, danger, gates, budget, alive, stages, bosses,
            opening ?? new[] { Script.SovietEnemy.OneZhuk, Script.SovietEnemy.ZhukPair, Script.SovietEnemy.OneStenka },
            new[] { Script.SovietEnemy.YakSwarm, Script.SovietEnemy.FencerFour, Script.SovietEnemy.OsaPack, Script.SovietEnemy.TarantulTrio, Script.SovietEnemy.StenkaRing },
            // Slowest missiles first, so both raids land in the last two minutes.
            new[] { (Script.SovietEnemy.BadgerKs, Script.RaidBadgerNm, Script.RaidFlights), (Script.SovietEnemy.Backfires, Script.RaidBackfireNm, Script.RaidFlights), (Script.SovietEnemy.FencerMs, Script.RaidCloseNm, Script.RaidFlights) });

    static ThreatPlan Nato(int[] danger, Dictionary<Gate, Func<Run, bool>> gates, float[] budget, float[] alive, Group[][] stages, BossPlan[] bosses, Group[] opening = null) =>
        Build(NatoHulls, danger, gates, budget, alive, stages, bosses,
            opening ?? new[] { Script.NatoEnemy.OnePgm, Script.NatoEnemy.PgmPair, Script.NatoEnemy.OneSleipner },
            new[] { Script.NatoEnemy.SkyhawkSwarm, Script.NatoEnemy.CorsairFour, Script.NatoEnemy.TigerPack, Script.NatoEnemy.SleipnerRing },
            new[] { (Script.NatoEnemy.B52s, Script.RaidCloseNm, 1), (Script.NatoEnemy.IntruderHarpoons, Script.RaidCloseNm, Script.RaidFlights) });

    static ThreatPlan Build(string[] hulls, int[] danger, Dictionary<Gate, Func<Run, bool>> gates, float[] budget, float[] alive, Group[][] stages, BossPlan[] bosses, Group[] opening, Group[] finaleGroups, (Group, float, int)[] raids) =>
        new ThreatPlan
        {
            Danger = hulls.Zip(danger, (hull, value) => (hull, value)).Where(p => p.value > 0).ToDictionary(p => p.hull, p => p.value),
            Gates = gates, Budget = budget, Alive = alive, Stages = stages, Bosses = bosses, Opening = opening,
            FinaleGroups = finaleGroups, Raids = raids,
        };

    static BossPlan Boss(Group group) => new BossPlan(group);
    static BossPlan GunsOnly(Group group) => new BossPlan(group, gunsOnly: true);

    static readonly Lazy<Dictionary<string, ThreatPlan>> Plans = new Lazy<Dictionary<string, ThreatPlan>>(MakePlans);

    static Dictionary<string, ThreatPlan> MakePlans()
    {
        // Short names for the two navies' groups, used for every table below.
        var kashin = Script.SovietEnemy.KashinBoss;
        var badgers = Script.SovietEnemy.BadgerBoss;
        var petyas = Script.SovietEnemy.PetyaBoss;
        var kresta = Script.SovietEnemy.KrestaBoss;
        var sovremenny = Script.SovietEnemy.SovremennyBoss;
        var battles = Script.NatoEnemy.BattleBoss;
        var intruders = Script.NatoEnemy.IntruderBoss;
        var gearings = Script.NatoEnemy.GearingBoss;
        var oklahoma = Script.NatoEnemy.OklahomaCityBoss;
        var iowa = Script.NatoEnemy.IowaBoss;
        var oklahomaGearings = Script.NatoEnemy.OklahomaCityAndGearings;

        return new Dictionary<string, ThreatPlan>
        {
            ["usn_ffg_oliver_hazard_perry"] = Soviet(
                new[] { 1, 2, 3, 12, 3, 6, 5, 8, 3, 4, 7, 8, 12, 15, 0 }, PerryGates,
                new[] { 1f, 4f, 8f, 12f, 16f }, new[] { 2f, 4f, 7f, 10f, 12f },
                new[]
                {
                    new[] { Script.SovietEnemy.OneZhuk, Script.SovietEnemy.ZhukPair },
                    new[] { Script.SovietEnemy.ZhukPair, Script.SovietEnemy.Zhuks, Script.SovietEnemy.OneStenka, Script.SovietEnemy.Stenkas, Script.SovietEnemy.Komars, Script.SovietEnemy.Yaks },
                    new[] { Script.SovietEnemy.Stenkas, Script.SovietEnemy.Petyas, Script.SovietEnemy.Komars, Script.SovietEnemy.Osas, Script.SovietEnemy.Tarantuls, Script.SovietEnemy.Yaks, Script.SovietEnemy.Badger },
                    new[] { Script.SovietEnemy.Petyas, Script.SovietEnemy.Osas, Script.SovietEnemy.Tarantuls, Script.SovietEnemy.Yaks, Script.SovietEnemy.Badger, Script.SovietEnemy.Fencers, Script.SovietEnemy.Floggers },
                },
                new[] { Boss(petyas), Boss(Script.SovietEnemy.Badger), GunsOnly(kashin), GunsOnly(kresta) },
                new[] { Script.SovietEnemy.OneZhuk, Script.SovietEnemy.ZhukPair }),

            ["usn_ddg_kidd"] = Soviet(
                new[] { 1, 1, 2, 8, 2, 4, 4, 5, 2, 3, 5, 5, 8, 10, 14 }, MissileShipGates,
                new[] { 1f, 6f, 12f, 18f, 24f }, new[] { 3f, 6f, 11f, 16f, 20f },
                new[]
                {
                    new[] { Script.SovietEnemy.OneZhuk, Script.SovietEnemy.ZhukPair, Script.SovietEnemy.OneStenka },
                    new[] { Script.SovietEnemy.Zhuks, Script.SovietEnemy.Stenkas, Script.SovietEnemy.Petyas, Script.SovietEnemy.Komars, Script.SovietEnemy.Osas, Script.SovietEnemy.Yaks, Script.SovietEnemy.Badger },
                    new[] { Script.SovietEnemy.Petyas, Script.SovietEnemy.SverdlovEnemy, Script.SovietEnemy.Osas, Script.SovietEnemy.Tarantuls, Script.SovietEnemy.Nanuchka, Script.SovietEnemy.Fencers, Script.SovietEnemy.Floggers },
                    new[] { Script.SovietEnemy.SverdlovEnemy, Script.SovietEnemy.OsaPack, Script.SovietEnemy.TarantulTrio, Script.SovietEnemy.Nanuchka, Script.SovietEnemy.FencerMs, Script.SovietEnemy.BadgerKs, Script.SovietEnemy.Backfires },
                },
                new[] { Boss(kashin), Boss(badgers), Boss(kresta), Boss(sovremenny) }),

            ["usn_cgn_long_beach_83"] = Soviet(
                new[] { 1, 1, 3, 9, 3, 4, 4, 5, 2, 3, 4, 4, 7, 9, 12 }, MissileShipGates,
                new[] { 1f, 6f, 12f, 18f, 24f }, new[] { 3f, 6f, 10f, 14f, 18f },
                new[]
                {
                    new[] { Script.SovietEnemy.OneZhuk, Script.SovietEnemy.ZhukPair, Script.SovietEnemy.OneStenka },
                    new[] { Script.SovietEnemy.Zhuks, Script.SovietEnemy.Stenkas, Script.SovietEnemy.Petyas, Script.SovietEnemy.Komars, Script.SovietEnemy.Osas, Script.SovietEnemy.Yaks, Script.SovietEnemy.Badger },
                    new[] { Script.SovietEnemy.Petyas, Script.SovietEnemy.Osas, Script.SovietEnemy.Tarantuls, Script.SovietEnemy.Nanuchka, Script.SovietEnemy.Fencers, Script.SovietEnemy.Floggers, Script.SovietEnemy.FencerMs },
                    new[] { Script.SovietEnemy.Petyas, Script.SovietEnemy.SverdlovEnemy, Script.SovietEnemy.OsaPack, Script.SovietEnemy.TarantulTrio, Script.SovietEnemy.Nanuchka, Script.SovietEnemy.Fencers, Script.SovietEnemy.Floggers, Script.SovietEnemy.FencerMs, Script.SovietEnemy.BadgerKs, Script.SovietEnemy.Backfires },
                },
                new[] { Boss(kashin), Boss(badgers), Boss(kresta), Boss(sovremenny) }),

            ["usn_bb_iowa"] = Soviet(
                new[] { 1, 1, 1, 3, 6, 10, 5, 10, 2, 3, 6, 6, 10, 14, 18 }, IowaGates,
                new[] { 1f, 5f, 10f, 16f, 22f }, new[] { 3f, 6f, 11f, 16f, 20f },
                new[]
                {
                    new[] { Script.SovietEnemy.OneZhuk, Script.SovietEnemy.ZhukPair, Script.SovietEnemy.OneStenka, Script.SovietEnemy.Zhuks, Script.SovietEnemy.Stenkas },
                    new[] { Script.SovietEnemy.Zhuks, Script.SovietEnemy.Stenkas, Script.SovietEnemy.Petyas, Script.SovietEnemy.Komars, Script.SovietEnemy.Yaks },
                    new[] { Script.SovietEnemy.Petyas, Script.SovietEnemy.SverdlovEnemy, Script.SovietEnemy.Komars, Script.SovietEnemy.Tarantuls, Script.SovietEnemy.Osas, Script.SovietEnemy.Yaks, Script.SovietEnemy.Badger },
                    new[] { Script.SovietEnemy.Petyas, Script.SovietEnemy.SverdlovEnemy, Script.SovietEnemy.Komars, Script.SovietEnemy.Tarantuls, Script.SovietEnemy.Osas, Script.SovietEnemy.Nanuchka, Script.SovietEnemy.Yaks, Script.SovietEnemy.Badger, Script.SovietEnemy.Fencers, Script.SovietEnemy.Floggers },
                },
                new[] { Boss(kashin), new BossPlan(badgers, when: r => IowaGates[Gate.A1](r), otherwise: petyas), Boss(kresta), Boss(sovremenny) }),

            ["usn_cg_bunker_hill"] = Soviet(
                new[] { 1, 1, 2, 6, 2, 3, 3, 4, 2, 3, 4, 4, 6, 8, 10 }, MissileShipGates,
                new[] { 1f, 8f, 16f, 26f, 36f }, new[] { 4f, 10f, 16f, 22f, 25f },
                new[]
                {
                    new[] { Script.SovietEnemy.Zhuks, Script.SovietEnemy.Stenkas, Script.SovietEnemy.Petyas, Script.SovietEnemy.Komars, Script.SovietEnemy.Yaks },
                    new[] { Script.SovietEnemy.Petyas, Script.SovietEnemy.SverdlovEnemy, Script.SovietEnemy.Osas, Script.SovietEnemy.Tarantuls, Script.SovietEnemy.Nanuchka, Script.SovietEnemy.Badger, Script.SovietEnemy.Fencers, Script.SovietEnemy.Floggers },
                    new[] { Script.SovietEnemy.SverdlovEnemy, Script.SovietEnemy.OsaPack, Script.SovietEnemy.Nanuchka, Script.SovietEnemy.FencerMs, Script.SovietEnemy.BadgerKs },
                    new[] { Script.SovietEnemy.SverdlovEnemy, Script.SovietEnemy.OsaPack, Script.SovietEnemy.TarantulTrio, Script.SovietEnemy.Nanuchka, Script.SovietEnemy.FencerMs, Script.SovietEnemy.Backfires },
                },
                new[] { Boss(kashin), Boss(badgers), Boss(kresta), Boss(sovremenny) }),

            ["wp_kr_sverdlov"] = Nato(
                new[] { 1, 1, 2, 2, 3, 3, 6, 6, 10, 12, 14, 16, 16, 18, 4, 4, 5, 12, 0 }, SverdlovGates,
                new[] { 1f, 4f, 8f, 12f, 18f }, new[] { 2f, 4f, 7f, 11f, 16f },
                new[]
                {
                    new[] { Script.NatoEnemy.OnePgm, Script.NatoEnemy.PgmPair, Script.NatoEnemy.OneSleipner },
                    new[] { Script.NatoEnemy.Pgms, Script.NatoEnemy.Sleipners, Script.NatoEnemy.ZClass, Script.NatoEnemy.Hauks, Script.NatoEnemy.Corsairs },
                    new[] { Script.NatoEnemy.ZClass, Script.NatoEnemy.CClass, Script.NatoEnemy.Battles, Script.NatoEnemy.Hauks, Script.NatoEnemy.Snoggs, Script.NatoEnemy.Tigers, Script.NatoEnemy.Corsairs, Script.NatoEnemy.Skyhawks, Script.NatoEnemy.IntruderHarpoons },
                    new[] { Script.NatoEnemy.Battles, Script.NatoEnemy.Gearings, Script.NatoEnemy.Flotilla, Script.NatoEnemy.Tigers, Script.NatoEnemy.Pegasus, Script.NatoEnemy.Knox, Script.NatoEnemy.Corsairs, Script.NatoEnemy.Skyhawks, Script.NatoEnemy.Harriers, Script.NatoEnemy.IntruderHarpoons },
                },
                new[]
                {
                    Boss(battles),
                    new BossPlan(intruders, when: r => SverdlovGates[Gate.A2](r), otherwise: gearings),
                    GunsOnly(oklahoma),
                    // Long shots puts its 6-inch guns level with her 16-inch ones. Before that she outranges it by nearly 5 nm.
                    new BossPlan(iowa, when: r => Upgrades.Has(r, Upgrades.LongShots), otherwise: oklahomaGearings),
                }),

            ["wp_em_sovremenny"] = Nato(
                new[] { 1, 1, 2, 2, 2, 3, 2, 2, 4, 5, 6, 7, 7, 9, 2, 2, 2, 5, 10 }, MissileShipGates,
                new[] { 1f, 6f, 11f, 17f, 22f }, new[] { 3f, 6f, 10f, 14f, 18f },
                new[]
                {
                    new[] { Script.NatoEnemy.OnePgm, Script.NatoEnemy.PgmPair, Script.NatoEnemy.Pgms, Script.NatoEnemy.Sleipners },
                    new[] { Script.NatoEnemy.Sleipners, Script.NatoEnemy.ZClass, Script.NatoEnemy.Hauks, Script.NatoEnemy.Snoggs, Script.NatoEnemy.Corsairs, Script.NatoEnemy.Skyhawks },
                    new[] { Script.NatoEnemy.ZClass, Script.NatoEnemy.CClass, Script.NatoEnemy.Battles, Script.NatoEnemy.Tigers, Script.NatoEnemy.Pegasus, Script.NatoEnemy.Harriers, Script.NatoEnemy.IntruderHarpoons },
                    new[] { Script.NatoEnemy.Gearings, Script.NatoEnemy.Flotilla, Script.NatoEnemy.Knox, Script.NatoEnemy.Adams, Script.NatoEnemy.Lutjens, Script.NatoEnemy.Belknap, Script.NatoEnemy.Harriers, Script.NatoEnemy.IntruderHarpoons, Script.NatoEnemy.B52s },
                },
                new[] { Boss(battles), Boss(intruders), Boss(oklahoma), Boss(iowa) }),

            ["wp_rkr_slava"] = Nato(
                new[] { 1, 2, 3, 3, 4, 4, 3, 3, 5, 5, 6, 7, 7, 8, 2, 2, 2, 4, 8 }, MissileShipGates,
                new[] { 1f, 5f, 9f, 14f, 18f }, new[] { 2f, 5f, 8f, 12f, 16f },
                new[]
                {
                    new[] { Script.NatoEnemy.OnePgm, Script.NatoEnemy.PgmPair },
                    new[] { Script.NatoEnemy.Pgms, Script.NatoEnemy.Sleipners, Script.NatoEnemy.Hauks, Script.NatoEnemy.Snoggs, Script.NatoEnemy.Corsairs, Script.NatoEnemy.Skyhawks },
                    new[] { Script.NatoEnemy.Sleipners, Script.NatoEnemy.ZClass, Script.NatoEnemy.Tigers, Script.NatoEnemy.Pegasus, Script.NatoEnemy.Harriers, Script.NatoEnemy.IntruderHarpoons },
                    new[] { Script.NatoEnemy.CClass, Script.NatoEnemy.Battles, Script.NatoEnemy.Knox, Script.NatoEnemy.Adams, Script.NatoEnemy.Lutjens, Script.NatoEnemy.Belknap, Script.NatoEnemy.Harriers, Script.NatoEnemy.IntruderHarpoons, Script.NatoEnemy.B52s },
                },
                new[] { Boss(battles), Boss(intruders), GunsOnly(oklahoma), GunsOnly(iowa) },
                new[] { Script.NatoEnemy.OnePgm, Script.NatoEnemy.PgmPair }),

            ["wp_rkr_kirov"] = Nato(
                new[] { 1, 2, 3, 3, 4, 4, 2, 2, 4, 4, 5, 6, 6, 7, 1, 1, 2, 3, 7 }, MissileShipGates,
                new[] { 1f, 6f, 12f, 18f, 24f }, new[] { 3f, 6f, 10f, 14f, 18f },
                new[]
                {
                    new[] { Script.NatoEnemy.OnePgm, Script.NatoEnemy.PgmPair, Script.NatoEnemy.Corsairs },
                    new[] { Script.NatoEnemy.Pgms, Script.NatoEnemy.Sleipners, Script.NatoEnemy.Hauks, Script.NatoEnemy.Snoggs, Script.NatoEnemy.Corsairs, Script.NatoEnemy.Skyhawks, Script.NatoEnemy.Harriers },
                    new[] { Script.NatoEnemy.Sleipners, Script.NatoEnemy.ZClass, Script.NatoEnemy.Tigers, Script.NatoEnemy.Pegasus, Script.NatoEnemy.Knox, Script.NatoEnemy.IntruderHarpoons },
                    new[] { Script.NatoEnemy.CClass, Script.NatoEnemy.Battles, Script.NatoEnemy.Adams, Script.NatoEnemy.Lutjens, Script.NatoEnemy.Belknap, Script.NatoEnemy.IntruderHarpoons, Script.NatoEnemy.B52s },
                },
                new[] { Boss(battles), Boss(intruders), Boss(oklahoma), Boss(iowa) },
                new[] { Script.NatoEnemy.OnePgm, Script.NatoEnemy.PgmPair }),

            ["wp_takr_kiev"] = KievPlan(battles, intruders, oklahoma, oklahomaGearings),
        };
    }

    static ThreatPlan KievPlan(Group battles, Group intruders, Group oklahoma, Group oklahomaGearings)
    {
        var plan = Nato(
            new[] { 2, 2, 4, 4, 5, 5, 3, 3, 6, 6, 8, 9, 9, 10, 1, 1, 2, 3, 7 }, KievGates,
            new[] { 1f, 4f, 8f, 12f, 16f }, new[] { 2f, 4f, 7f, 10f, 14f },
            new[]
            {
                new[] { Script.NatoEnemy.OnePgm },
                new[] { Script.NatoEnemy.Pgms, Script.NatoEnemy.Sleipners, Script.NatoEnemy.Hauks, Script.NatoEnemy.Snoggs, Script.NatoEnemy.Corsairs, Script.NatoEnemy.Skyhawks },
                new[] { Script.NatoEnemy.Sleipners, Script.NatoEnemy.ZClass, Script.NatoEnemy.Hauks, Script.NatoEnemy.Snoggs, Script.NatoEnemy.Tigers, Script.NatoEnemy.Harriers, Script.NatoEnemy.IntruderHarpoons },
                new[] { Script.NatoEnemy.CClass, Script.NatoEnemy.Battles, Script.NatoEnemy.Tigers, Script.NatoEnemy.Pegasus, Script.NatoEnemy.Knox, Script.NatoEnemy.Harriers, Script.NatoEnemy.IntruderHarpoons, Script.NatoEnemy.B52s },
            },
            new[] { Boss(battles), Boss(intruders), GunsOnly(oklahoma), GunsOnly(oklahomaGearings) },
            new[] { Script.NatoEnemy.OnePgm });
        // Nothing more until the first kill or minute 3: its aircraft need time to launch.
        plan.OpeningSeconds = 180f;
        return plan;
    }

    static readonly Dictionary<string, ThreatPlan> generic = new Dictionary<string, ThreatPlan>();

    /// <summary>
    /// The plan for a starting ship. A US or Soviet one not listed, as from the
    /// test panel, gets a middling plan for its side; a ship of any other navy
    /// gets one built from its enemy's roster and its own class.
    /// </summary>
    internal static ThreatPlan For(string type)
    {
        if (Plans.Value.TryGetValue(type, out var plan)) return plan;
        var navy = Navies.Of(type);
        if (navy.Foe.Hulls == null) return Plans.Value[navy == Navies.Soviet ? "wp_em_sovremenny" : "usn_ddg_kidd"];
        if (!generic.TryGetValue(type, out plan)) generic[type] = plan = Generic(navy.Foe, Launcher.ByType(type));
        return plan;
    }

    /// <summary>
    /// A plan from a roster, for the navies with no hand-set tables. The
    /// roster's danger is what each hull costs a missile ship; a ship of
    /// another class pays by what she can least stand, in the proportions the
    /// hand-set plans settled on: a gun ship 2.5× for missile ships and 2× for
    /// aircraft, a frigate 1.5× for everything, a carrier 2× for gun ships and
    /// little for aircraft, which her own fighters meet. Gates, budget and unit
    /// limit are those of the hand-set ship of the same class: Kidd,
    /// Perry, Sverdlov or Kiev. A ship weak against missiles meets the last
    /// two mini-bosses without theirs, and a carrier is given time to launch.
    /// </summary>
    static ThreatPlan Generic(Roster foe, StartingShip ship)
    {
        var shipClass = ship?.Class ?? ShipClass.Missile;
        var (gates, budget, alive, gun, missile, air) = shipClass switch
        {
            ShipClass.Frigate => (PerryGates, new[] { 1f, 4f, 8f, 12f, 16f }, new[] { 2f, 4f, 7f, 10f, 12f }, 1.5f, 1.5f, 1.5f),
            ShipClass.GunShip => (SverdlovGates, new[] { 1f, 4f, 8f, 12f, 18f }, new[] { 2f, 4f, 7f, 11f, 16f }, 1f, 2.5f, 2f),
            ShipClass.Carrier => (KievGates, new[] { 1f, 4f, 8f, 12f, 16f }, new[] { 2f, 4f, 7f, 10f, 14f }, 2f, 1.4f, 0.6f),
            _ => (MissileShipGates, new[] { 1f, 6f, 12f, 18f, 24f }, new[] { 3f, 6f, 11f, 16f, 20f }, 1f, 1f, 1f),
        };
        float Factor(string type) => GateOf(type) switch
        {
            Gate.None => gun,
            Gate.A1 or Gate.A2 or Gate.A3 => air,
            _ => missile,
        };
        bool light = shipClass != ShipClass.Missile;
        var b = foe.Bosses;
        var plan = new ThreatPlan
        {
            Danger = foe.Hulls.ToDictionary(h => h.type, h => Mathf.Max(1, Mathf.RoundToInt(h.danger * Factor(h.type)))),
            Gates = gates, Budget = budget, Alive = alive, Stages = foe.Stages, Opening = foe.Opening,
            FinaleGroups = foe.FinaleGroups, Raids = foe.Raids,
            Bosses = new[]
            {
                Boss(b[0]),
                // Bombers only once something aboard can reach them; until then the gun ships come again.
                new BossPlan(b[1], when: r => gates[Gate.A1](r), otherwise: b[0]),
                light ? GunsOnly(b[2]) : Boss(b[2]),
                light ? GunsOnly(b[3]) : Boss(b[3]),
            },
        };
        // Nothing more until the first kill or minute 3: a carrier's aircraft need time to launch.
        if (ship?.Carrier == true) plan.OpeningSeconds = 180f;
        return plan;
    }
}
