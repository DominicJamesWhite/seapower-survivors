using System.Linq;

namespace SeapowerSurvivors;

enum Domain { Surface, Air }

/// <summary>Something the player's build has, which some groups wait for.</summary>
enum Capability { None, AirCover }

/// <summary>An enemy group: one or more units spawned together on one bearing.</summary>
sealed class Group
{
    public string Name;
    public Domain Domain;
    public string[] Units;
    public float MinNm, MaxNm;
    public string Squadron = "Squadron1";
    public string Loadout = "default";
    public float AltitudeFt = 25000f;
    /// <summary>
    /// For groups that fire anti-ship missiles: the fleet anti-missile score
    /// needed before they may be sent. A boss is sent regardless, with its
    /// anti-ship launchers out of action until the fleet reaches it.
    /// </summary>
    public float MinDefence;
    /// <summary>Each unit comes on its own bearing, evenly round the compass: a wall closing in.</summary>
    public bool IsRing;
    /// <summary>
    /// Anti-ship missiles the group fires in one salvo, none for gun ships. A
    /// bomber counts as one: its bombs kill as surely, and it has to be stopped too.
    /// </summary>
    public int Salvo => MinDefence > 0f ? Units.Sum(u => Script.Missiles(u)) : Domain == Domain.Air ? Units.Length : 0;
    /// <summary>The same, leaving out hulls that will spawn too far out to fire yet, with the fleet's reach this long.</summary>
    public int SalvoAt(float reachNm) => MinDefence > 0f ? Units.Where(u => !Script.Pending(u, reachNm)).Sum(u => Script.Missiles(u)) : Salvo;

    public static Group Ships(string name, float minNm, float maxNm, params string[] units) =>
        new Group { Name = name, Domain = Domain.Surface, MinNm = minNm, MaxNm = maxNm, Units = units };

    public static Group Raid(string name, float minNm, float maxNm, string loadout, params string[] units) =>
        new Group { Name = name, Domain = Domain.Air, MinNm = minNm, MaxNm = maxNm, Loadout = loadout, Units = units };

    public Group At(float altitudeFt)
    {
        AltitudeFt = altitudeFt;
        return this;
    }

    public Group Missiles(float minDefence)
    {
        MinDefence = minDefence;
        return this;
    }

    /// <summary>Aircraft in another squadron's markings than the type's first.</summary>
    public Group From(string squadron)
    {
        Squadron = squadron;
        return this;
    }

    public Group Ring()
    {
        IsRing = true;
        return this;
    }

    /// <summary>Boats with a missile that fires only from close in, which start far out and take their time: see Script.IsWaiting.</summary>
    public bool Waits => Units.Any(Script.IsWaiting);
}

enum SetPieceKind { Convoy, Sag }

/// <summary>
/// Something worth going out of the way for, far beyond the fleet's reach:
/// a convoy crossing on its lane, or a surface action group creeping in.
/// It does not come to the fleet. The fleet goes to it, sends aircraft, or
/// fires long-range missiles at it in the windows its contact reports open.
/// </summary>
sealed class SetPiece
{
    public string Name;
    public SetPieceKind Kind;
    /// <summary>What the reward is for: a convoy's merchants, or a SAG's capital ship first.</summary>
    public string[] Hulls;
    public string[] Escorts;
    /// <summary>How far beyond the fleet's reach: a convoy's closest pass, a SAG's spawn.</summary>
    public float MinBeyondNm, MaxBeyondNm;
    public float Knots;
    /// <summary>Game minutes until a convoy is past and gone, or a SAG withdraws.</summary>
    public float LifeMinutes;
    /// <summary>As for a boss: below this fleet defence it comes with its anti-ship launchers out of action.</summary>
    public float MinDefence;

    public static SetPiece Convoy(string name, string[] hulls, params string[] escorts) => new SetPiece
    {
        Name = name, Kind = SetPieceKind.Convoy, Hulls = hulls, Escorts = escorts,
        MinBeyondNm = 3f, MaxBeyondNm = 5f, Knots = 12f, LifeMinutes = 8f,
    };

    public static SetPiece Sag(string name, string capital, params string[] escorts) => new SetPiece
    {
        Name = name, Kind = SetPieceKind.Sag, Hulls = new[] { capital }, Escorts = escorts,
        MinBeyondNm = 4f, MaxBeyondNm = 6f, Knots = 12f, LifeMinutes = 9f,
    };

    /// <summary>Anti-ship missiles its ships fire in one salvo.</summary>
    public int Salvo => Hulls.Concat(Escorts).Sum(u => Script.MissilesIfListed(u));
    /// <summary>The same, leaving out hulls that will spawn too far out to fire yet, with the fleet's reach this long.</summary>
    public int SalvoAt(float reachNm) => Hulls.Concat(Escorts).Where(u => !Script.Pending(u, reachNm)).Sum(u => Script.MissilesIfListed(u));

    public SetPiece Missiles(float minDefence)
    {
        MinDefence = minDefence;
        return this;
    }
}

/// <summary>
/// What is scripted to arrive as one phase of the run begins (Script.PhaseSeconds
/// of game time). Everything else comes through the ship's own ThreatPlan.
/// </summary>
sealed class Minute
{
    public Group Event;
    /// <summary>A convoy or SAG that appears as the phase begins, far out.</summary>
    public SetPiece SetPiece;
}

/// <summary>
/// The enemy's groups, and what is on the clock: spikes and set pieces. Who
/// comes when, and at what danger, is per starting ship: see Threats. There is
/// one list per enemy navy; the enemy is the one the starting ship's navy is
/// paired with (see Navies). The Soviet and NATO lists are here; the Argentine,
/// British, Chinese and Japanese ones are in Rosters.
/// </summary>
/// <remarks>
/// Ships are not spawned on the rings given here: the director places each
/// surface group by its own weapons (see Director.SpawnOnBearing). Aircraft use
/// the rings: strikers a few minutes out, stand-off bombers just outside their
/// missiles' maximum range.
/// </remarks>
static class Script
{
    /// <summary>
    /// Game seconds per row of the table. The run is meant to be played at x1,
    /// where it lasts 20 minutes; everything else here is in game time too.
    /// Ships close at under half a mile a minute, so at this length surface
    /// fights start at the edge of reach, and aircraft and missiles, which
    /// close ten times as fast, carry the pace.
    /// </summary>
    public const float PhaseSeconds = 80f;
    public const float RunSeconds = 15f * PhaseSeconds;
    /// <summary>A mini-boss comes every this many rows: at 4, 8, 12 and 16 minutes.</summary>
    public const int BossEveryRows = 3;
    /// <summary>Room for set pieces on top of a ship's unit cap, so convoys cannot crowd out waves. Below 30 fps the game itself runs slow, so this is a limit on frame cost.</summary>
    public const int SetPieceRoom = 12;
    /// <summary>A missile ship starts this many game seconds' steaming outside its firing range, so it fires almost at once.</summary>
    public const float MissileApproachSeconds = 30f;
    /// <summary>A gun ship starts this many game minutes' steaming outside its own range, so it has to come in under fire.</summary>
    public const float GunApproachMinutes = 3f;
    /// <summary>Nothing spawns nearer than this to any ship of the fleet, whatever its weapons: closer looks like a glitch, and puts it inside the close-in guns at once.</summary>
    public const float MinSpawnNm = 5f;
    /// <summary>The groups of one wave come in at least this far apart in bearing, where the water allows.</summary>
    public const float WaveSpreadDeg = 60f;
    /// <summary>Missile ships hold at this share of their own missile range.</summary>
    public const float StandOffShare = 0.9f;
    /// <summary>Game minutes a missile ship holds and fires before it closes.</summary>
    public const float StandOffMinutes = 3f;
    /// <summary>Missile ships start and hold no further out than this, so a 300 nm missile cannot end a run from over the horizon, and aircraft can reach them in time.</summary>
    public const float StandOffMaxNm = 35f;
    /// <summary>Game minutes between contact reports on a set piece; the first comes as it appears.</summary>
    public const float ReportEveryMinutes = 2f;
    /// <summary>Game minutes a contact report keeps a set piece on the fleet's plot: the window to fire on it.</summary>
    public const float ReportWindowMinutes = 1f;
    /// <summary>
    /// Anti-ship missiles each hull fires in one salvo, for sizing a group
    /// against the fleet's defence before it is spawned. Round numbers, from
    /// the ship's or loadout's usual fit.
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<string, int> MissilesPerUnit = new System.Collections.Generic.Dictionary<string, int>
    {
        ["wp_ptg_komar"] = 2, ["wp_ptg_osa1"] = 4, ["wp_ptg_tarantul"] = 4, ["wp_mrk_nanuchka"] = 6,
        ["wp_bpk_kashin_mod"] = 4, ["wp_rkr_kresta1"] = 4, ["wp_em_sovremenny"] = 8,
        ["wp_su-24a"] = 2, ["wp_su-24m"] = 2, ["wp_mig-27"] = 2, ["wp_tu-16k"] = 2, ["wp_tu-22m2"] = 2,
        ["knm_ptg_hauk"] = 6, ["knm_ptg_snogg"] = 4, ["fgs_ptg_tiger"] = 4, ["usn_phm_pegasus"] = 8,
        ["usn_ff_knox"] = 8, ["usn_ddg_adams_early"] = 4, ["fgs_ddg_lutjens"] = 4, ["usn_cg_belknap"] = 8,
        ["wp_rkr_slava"] = 16, ["usn_cg_ticonderoga"] = 8, ["usn_clg_oklahoma_city"] = 3, ["usn_bb_iowa"] = 16, ["usn_a-6e"] = 2, ["usaf_b-52g"] = 12,
        // Argentine and British: four Exocets a ship, one or two an aircraft; a Nimrod's two Harpoons.
        ["ara_fs_drummond"] = 4, ["ara_dd_sumner_fram2"] = 4, ["ara_dd_comodoro_py"] = 4, ["ara_ddg_hercules"] = 4, ["fr_super_etendard"] = 2,
        ["rn_ff_leander_batch2"] = 4, ["rn_ff_amazon"] = 4, ["rn_ff_broadsword"] = 4, ["rn_ddg_county_batch2_75"] = 4, ["rn_nimrod_mr2"] = 2,
        // Chinese and Japanese.
        ["plan_ptg_type_024"] = 2, ["plan_ptg_type_021"] = 4, ["plan_ff_chengdu"] = 2, ["plan_ff_jianghu1"] = 4, ["plan_ff_jianghu3"] = 8,
        ["plan_dd_luda1"] = 6, ["pla_h-6d"] = 2,
        ["jmsdf_phm_pegasus"] = 8, ["jmsdf_dd_hatsuyuki"] = 8, ["jmsdf_dd_takatsuki_84"] = 8, ["jmsdf_ddg_tachikaze_84"] = 8, ["jasdf_f-1"] = 2,
    };

    internal static int Missiles(string type) => MissilesPerUnit.TryGetValue(type, out int n) ? n : 4;
    /// <summary>The same for a set piece's mixed hulls, where a ship not listed is an unarmed merchant or a gun ship.</summary>
    internal static int MissilesIfListed(string type) => MissilesPerUnit.TryGetValue(type, out int n) ? n : 0;

    /// <summary>
    /// Boats whose missiles fire only from their seeker's range, in nm (see
    /// Reach.EnemyStrikeNm), for judging a group before it exists. They alone
    /// still start at the edge of the fleet's reach: against a long reach, a
    /// danger for later that is easy to ignore, not part of this salvo.
    /// </summary>
    static readonly System.Collections.Generic.Dictionary<string, float> ShortFiringRangeNm = new System.Collections.Generic.Dictionary<string, float>
    {
        ["wp_ptg_tarantul"] = 10f, ["knm_ptg_hauk"] = 10f, ["knm_ptg_snogg"] = 10f,
    };
    /// <summary>A ship this much beyond its firing range is not yet counted against the salvo limit: a couple of minutes' closing.</summary>
    public const float PendingMarginNm = 1.5f;

    /// <summary>Game seconds before the end that the final assault begins. Three minutes, for a last two that are felt: a sea-skimmer takes that long to arrive.</summary>
    public const float FinaleSeconds = 180f;
    /// <summary>Game seconds between the final assault's waves, against 45 for the ordinary pulses.</summary>
    public const float FinalePulseSeconds = 20f;
    /// <summary>Each wave of the final assault spends this share of the plan's end-of-run budget, with no relaxing between them.</summary>
    public const float FinaleBudgetShare = 0.4f;
    /// <summary>The unit limit rises by this factor in the final assault, up to the most the frame rate can stand.</summary>
    public const float FinaleAliveFactor = 1.25f, FinaleMaxAlive = 40f;
    /// <summary>With fewer than this many game seconds left, nothing spawned could arrive before the run ends.</summary>
    public const float FinaleLastSpawnSeconds = 45f;
    /// <summary>What a sea-skimmer covers in a minute, and a striker.</summary>
    public const float MissileNmPerMinute = 9.5f, StrikerNmPerMinute = 8f;
    /// <summary>A missile ship in the final assault starts no further out than its missiles could cover by the end, within these limits.</summary>
    public const float FinaleMinMissileNm = 8f, FinaleMaxMissileNm = 30f;
    /// <summary>The nearest an aircraft spawns in the final assault.</summary>
    public const float FinaleMinAirNm = 8f;
    /// <summary>Gun ships in the final assault start this many minutes outside their range.</summary>
    public const float FinaleGunApproachMinutes = 1f;
    /// <summary>Stand-off raids launch this many game seconds before the end, so that what they fire lands in the last two minutes.</summary>
    public static readonly float[] RaidSecondsLeft = { 360f, 270f };
    /// <summary>Flights in one Soviet stand-off raid. A B-52 carries so many Harpoons that one flight of two is plenty.</summary>
    public const int RaidFlights = 2;
    /// <summary>Outside the missiles' 100 nm minimum range, with time to fire before flying inside it: AS-4s from here take about three minutes, AS-6s about four.</summary>
    public const float RaidBackfireNm = 125f, RaidBadgerNm = 120f;
    /// <summary>Close enough for what they carry to arrive in time: a Harpoon from 30 nm takes about three minutes.</summary>
    public const float RaidCloseNm = 30f;
    /// <summary>
    /// The AI stops firing once the missiles in the air at a ship add up to its
    /// MissilesToSaturate (see CombatHooks.EnemyKeepsFiring). That limit starts
    /// to rise some way into the run (Difficulty.SaturationFromSeconds: from
    /// minute 8 at the first difficulty), reaching this many times itself by the
    /// final assault, which has no limit at all.
    /// </summary>
    public const float SaturationLate = 2.5f;
    internal static bool IsWaiting(string type) => ShortFiringRangeNm.ContainsKey(type);
    /// <summary>Whether this hull, spawned at the edge of this reach, starts too far out to fire yet.</summary>
    internal static bool Pending(string type, float reachNm) =>
        ShortFiringRangeNm.TryGetValue(type, out float range) && reachNm > range + PendingMarginNm;

    /// <summary>The table for the navy the starting ship is fighting.</summary>
    public static Minute[] For(SeaPower.ObjectBase playerShip) => Foe(playerShip).Minutes;

    /// <summary>Test aid: the first convoy and SAG of the navy the starting ship is fighting.</summary>
    public static (SetPiece convoy, SetPiece sag) TestSetPieces(SeaPower.ObjectBase playerShip)
    {
        var foe = Foe(playerShip);
        return (foe.TestConvoy, foe.TestSag);
    }

    /// <summary>The enemy of the run's flagship, or with no run, of this ship's navy.</summary>
    static Roster Foe(SeaPower.ObjectBase playerShip) =>
        Navies.Of(Run.Current?.FlagshipType ?? playerShip._obp._objectIniName).Foe;

    const float L = Defence.Light, M = Defence.Medium, H = Defence.Heavy;

    /// <summary>Soviet ships and aircraft, against a NATO starting ship.</summary>
    internal static class SovietEnemy
    {
        // Fodder: gun and torpedo craft, then the smallest missile boats.
        // Singles and pairs, for the first steps of a run.
        internal static readonly Group OneZhuk = Group.Ships("Zhuk patrol boat", 9f, 12f, "wp_pb_zhuk");
        internal static readonly Group ZhukPair = Group.Ships("Zhuk pair", 9f, 12f, "wp_pb_zhuk", "wp_pb_zhuk");
        internal static readonly Group OneStenka = Group.Ships("Stenka boat", 9f, 12f, "wp_pt_stenka");
        internal static readonly Group Zhuks = Group.Ships("Zhuk patrol boats", 9f, 12f, "wp_pb_zhuk", "wp_pb_zhuk", "wp_pb_zhuk");
        internal static readonly Group Stenkas = Group.Ships("Stenka torpedo boats", 9f, 12f, "wp_pt_stenka", "wp_pt_stenka");
        internal static readonly Group Komars = Group.Ships("Komar missile boats", 16f, 20f, "wp_ptg_komar", "wp_ptg_komar").Missiles(L);

        // Walls: one boat on each of eight bearings.
        internal static readonly Group ZhukRing = Group.Ships("Zhuk ring", 9f, 12f, Enumerable.Repeat("wp_pb_zhuk", 8).ToArray()).Ring();
        internal static readonly Group StenkaRing = Group.Ships("Stenka ring", 9f, 12f, Enumerable.Repeat("wp_pt_stenka", 8).ToArray()).Ring();
        internal static readonly Group OsaRing = Group.Ships("Osa ring", 18f, 22f, Enumerable.Repeat("wp_ptg_osa1", 8).ToArray()).Ring().Missiles(H);

        // Threats. Bigger packs first, since the groups they enlarge refer to them.
        internal static readonly Group OsaPack = Group.Ships("Osa pack", 18f, 22f, Enumerable.Repeat("wp_ptg_osa1", 6).ToArray()).Missiles(M);
        internal static readonly Group TarantulTrio = Group.Ships("Tarantul trio", 20f, 26f, "wp_ptg_tarantul", "wp_ptg_tarantul", "wp_ptg_tarantul").Missiles(H);
        internal static readonly Group YakSwarm = Group.Raid("Yak-38 swarm", 22f, 28f, "AntiShip", Enumerable.Repeat("wp_yak-38", 6).ToArray()).At(3000f);
        internal static readonly Group FencerFour = Group.Raid("Su-24A strike four", 26f, 32f, "AntiShip", "wp_su-24a", "wp_su-24a", "wp_su-24a", "wp_su-24a").At(5000f).Missiles(H);

        internal static readonly Group Petyas = Group.Ships("Petya II light frigates", 12f, 15f, "wp_skr_petya2", "wp_skr_petya2");
        internal static readonly Group SverdlovEnemy = Group.Ships("Sverdlov cruiser", 15f, 18f, "wp_kr_sverdlov");
        internal static readonly Group Osas = Group.Ships("Osa missile boats", 18f, 22f, "wp_ptg_osa1", "wp_ptg_osa1").Missiles(L);
        internal static readonly Group Tarantuls = Group.Ships("Tarantul corvettes", 20f, 26f, "wp_ptg_tarantul", "wp_ptg_tarantul").Missiles(M);
        internal static readonly Group Nanuchka = Group.Ships("Nanuchka", 30f, 36f, "wp_mrk_nanuchka").Missiles(M);

        // Set pieces: convoys on their lane, and surface action groups.
        internal static readonly SetPiece CoastalConvoy = SetPiece.Convoy("Coastal convoy",
            new[] { "civ_ms_poltava", "civ_ms_yuniy_partizan", "civ_ms_poltava" }, "wp_skr_petya2");
        internal static readonly SetPiece KrestaSag = SetPiece.Sag("Kresta surface action group",
            "wp_rkr_kresta1", "wp_skr_petya2", "wp_skr_petya2").Missiles(M);
        internal static readonly SetPiece ReinforcementConvoy = SetPiece.Convoy("Reinforcement convoy",
            new[] { "civ_ms_kommunist", "civ_ms_murom", "civ_ms_irkutsk", "wp_bdk_alligator" }, "wp_skr_petya2", "wp_bpk_kashin_mod").Missiles(M);
        internal static readonly SetPiece SlavaSag = SetPiece.Sag("Slava surface action group",
            "wp_rkr_slava", "wp_em_sovremenny", "wp_bpk_kashin_mod").Missiles(H);
        internal static readonly SetPiece TankerConvoy = SetPiece.Convoy("Tanker convoy",
            new[] { "civ_ms_ritina", "civ_ms_kommunist", "civ_ms_murom", "civ_ms_irkutsk", "civ_ms_poltava" }, "wp_bpk_kashin_mod", "wp_em_sovremenny").Missiles(H);

        // Strikers: bombs, or missiles from inside the fleet's SAM envelope.
        internal static readonly Group Yaks = Group.Raid("Yak-38 bombers", 22f, 28f, "AntiShip", "wp_yak-38", "wp_yak-38").At(3000f);
        internal static readonly Group Badger = Group.Raid("Tu-16 bomber", 26f, 32f, "Strike", "wp_tu-16").At(15000f);
        internal static readonly Group Fencers = Group.Raid("Su-24A strike", 26f, 32f, "AntiShip", "wp_su-24a", "wp_su-24a").At(5000f).Missiles(M);
        internal static readonly Group Floggers = Group.Raid("MiG-27 strike", 24f, 30f, "AntiShip", "wp_mig-27", "wp_mig-27").At(5000f).Missiles(M);
        internal static readonly Group FencerMs = Group.Raid("Su-24M Kh-59 strike", 30f, 38f, "AntiShipPrecision", "wp_su-24m", "wp_su-24m").At(8000f).Missiles(H);

        // Stand-off raids: KSR-5 and Kh-22 cannot be fired inside 100 nm, so
        // they also wait for air cover that can go out and kill the bombers.
        internal static readonly Group BadgerKs = Group.Raid("Tu-16K stand-off raid", 160f, 175f, "Strike", "wp_tu-16k", "wp_tu-16k").Missiles(H);
        internal static readonly Group Backfires = Group.Raid("Tu-22M2 stand-off raid", 200f, 220f, "AntiShip", "wp_tu-22m2", "wp_tu-22m2").Missiles(H);

        // Mini-bosses.
        internal static readonly Group PetyaBoss = Group.Ships("Petya II trio", 12f, 15f, "wp_skr_petya2", "wp_skr_petya2", "wp_skr_petya2");
        internal static readonly Group KashinBoss = Group.Ships("Kashin destroyer", 30f, 34f, "wp_bpk_kashin_mod").Missiles(M);
        internal static readonly Group BadgerBoss = Group.Raid("Tu-16 bomber trio", 26f, 32f, "Strike", "wp_tu-16", "wp_tu-16", "wp_tu-16").At(15000f);
        internal static readonly Group KrestaBoss = Group.Ships("Kresta I cruiser", 40f, 45f, "wp_rkr_kresta1").Missiles(H);
        internal static readonly Group SovremennyBoss = Group.Ships("Sovremenny destroyer", 40f, 45f, "wp_em_sovremenny").Missiles(H);


        public static readonly Minute[] Minutes =
        {
            /* 0 */ new Minute { },
            /* 1 */ new Minute { SetPiece = CoastalConvoy },
            /* 2 */ new Minute { },
            /* 3 */ new Minute { },
            /* 4 */ new Minute { SetPiece = KrestaSag },
            /* 5 */ new Minute { Event = ZhukRing },
            /* 6 */ new Minute { },
            /* 7 */ new Minute { SetPiece = ReinforcementConvoy },
            /* 8 */ new Minute { Event = YakSwarm },
            /* 9 */ new Minute { },
            /* 10 */ new Minute { Event = StenkaRing, SetPiece = SlavaSag },
            /* 11 */ new Minute { Event = OsaPack },
            /* 12 */ new Minute { },
            /* 13 */ new Minute { Event = YakSwarm, SetPiece = TankerConvoy },
            /* 14 */ new Minute { Event = OsaRing },
        };
    }

    /// <summary>
    /// NATO ships and aircraft, against a Soviet starting ship. The early
    /// minutes are post-war gun destroyers, so a Sverdlov fights a gun war
    /// until its fleet can stop missiles.
    /// </summary>
    internal static class NatoEnemy
    {
        // Fodder: gunboats and corvettes, then Penguin boats.
        // Singles and pairs, for the first steps of a run.
        internal static readonly Group OnePgm = Group.Ships("PGM gunboat", 8f, 11f, "usn_pb_pgm-39");
        internal static readonly Group PgmPair = Group.Ships("PGM pair", 8f, 11f, "usn_pb_pgm-39", "usn_pb_pgm-39");
        internal static readonly Group OneSleipner = Group.Ships("Sleipner corvette", 10f, 13f, "knm_fs_sleipner");
        internal static readonly Group Pgms = Group.Ships("PGM gunboats", 8f, 11f, "usn_pb_pgm-39", "usn_pb_pgm-39", "usn_pb_pgm-39");
        internal static readonly Group Sleipners = Group.Ships("Sleipner corvettes", 10f, 13f, "knm_fs_sleipner", "knm_fs_sleipner");
        internal static readonly Group Hauks = Group.Ships("Hauk Penguin boats", 14f, 18f, "knm_ptg_hauk", "knm_ptg_hauk").Missiles(L);
        internal static readonly Group Snoggs = Group.Ships("Snøgg Penguin boats", 14f, 18f, "knm_ptg_snogg", "knm_ptg_snogg").Missiles(L);

        // Walls: one boat on each of eight bearings.
        internal static readonly Group PgmRing = Group.Ships("PGM ring", 8f, 11f, Enumerable.Repeat("usn_pb_pgm-39", 8).ToArray()).Ring();
        internal static readonly Group SleipnerRing = Group.Ships("Sleipner ring", 10f, 13f, Enumerable.Repeat("knm_fs_sleipner", 8).ToArray()).Ring();
        internal static readonly Group TigerRing = Group.Ships("Tiger ring", 18f, 22f, Enumerable.Repeat("fgs_ptg_tiger", 8).ToArray()).Ring().Missiles(H);

        // Threats. Bigger packs first, since the groups they enlarge refer to them.
        internal static readonly Group Flotilla = Group.Ships("Destroyer flotilla", 12f, 15f, "rn_dd_z-class", "rn_dd_c-class", "rn_dd_c-class", "rn_dd_battle");
        internal static readonly Group TigerPack = Group.Ships("Tiger pack", 18f, 22f, Enumerable.Repeat("fgs_ptg_tiger", 6).ToArray()).Missiles(H);
        internal static readonly Group SkyhawkSwarm = Group.Raid("A-4G swarm", 22f, 28f, "AntiShip", Enumerable.Repeat("ran_a-4g", 6).ToArray()).At(3000f);
        internal static readonly Group CorsairFour = Group.Raid("A-7E bomber four", 22f, 28f, "Strike", "usn_a-7e", "usn_a-7e", "usn_a-7e", "usn_a-7e").At(5000f);

        internal static readonly Group ZClass = Group.Ships("Z-class destroyer", 11f, 14f, "rn_dd_z-class");
        internal static readonly Group CClass = Group.Ships("C-class destroyers", 11f, 14f, "rn_dd_c-class", "rn_dd_c-class");
        internal static readonly Group Battles = Group.Ships("Battle-class destroyers", 12f, 15f, "rn_dd_battle", "rn_dd_battle");
        internal static readonly Group Gearings = Group.Ships("Gearing FRAM II destroyers", 12f, 15f, "usn_dd_gearing_fram2", "usn_dd_gearing_fram2");
        internal static readonly Group Tigers = Group.Ships("Tiger Exocet boats", 18f, 22f, "fgs_ptg_tiger", "fgs_ptg_tiger").Missiles(M);
        internal static readonly Group Pegasus = Group.Ships("Pegasus hydrofoils", 25f, 30f, "usn_phm_pegasus", "usn_phm_pegasus").Missiles(M);
        internal static readonly Group Knox = Group.Ships("Knox frigate", 30f, 36f, "usn_ff_knox").Missiles(M);
        internal static readonly Group Adams = Group.Ships("Adams destroyer", 30f, 36f, "usn_ddg_adams_early").Missiles(H);
        internal static readonly Group Lutjens = Group.Ships("Lütjens destroyer", 30f, 36f, "fgs_ddg_lutjens").Missiles(H);
        internal static readonly Group Belknap = Group.Ships("Belknap cruiser", 35f, 40f, "usn_cg_belknap").Missiles(H);

        // Set pieces: convoys on their lane, and surface action groups.
        internal static readonly SetPiece CoastalConvoy = SetPiece.Convoy("Coastal convoy",
            new[] { "civ_ms_freighter_a", "usn_ao_t2", "civ_ms_freighter_a" }, "usn_dd_gearing_fram2");
        internal static readonly SetPiece BelknapSag = SetPiece.Sag("Belknap surface action group",
            "usn_cg_belknap", "usn_ff_knox", "usn_ff_knox").Missiles(M);
        internal static readonly SetPiece SealiftConvoy = SetPiece.Convoy("Sealift convoy",
            new[] { "civ_ms_sealift_pacific", "civ_ms_seabee", "civ_ms_mercur", "usn_lst_newport" }, "usn_ff_knox", "usn_ddg_adams_early").Missiles(M);
        internal static readonly SetPiece TiconderogaSag = SetPiece.Sag("Ticonderoga surface action group",
            "usn_cg_ticonderoga", "usn_cg_belknap", "usn_ddg_adams_early").Missiles(H);
        internal static readonly SetPiece TankerConvoy = SetPiece.Convoy("Tanker convoy",
            new[] { "civ_ms_ritina", "usn_ao_t2", "usn_aoe_sacramento", "usn_ao_t2", "civ_ms_bulk" }, "usn_cg_belknap", "usn_ff_knox").Missiles(H);

        // Strikers.
        internal static readonly Group Corsairs = Group.Raid("A-7E bombers", 22f, 28f, "Strike", "usn_a-7e", "usn_a-7e").At(5000f);
        internal static readonly Group Skyhawks = Group.Raid("A-4G bombers", 22f, 28f, "AntiShip", "ran_a-4g", "ran_a-4g").At(3000f);
        internal static readonly Group Harriers = Group.Raid("AV-8A strike", 22f, 28f, "Strike", "usmc_av-8a", "usmc_av-8a").At(3000f);
        internal static readonly Group IntruderHarpoons = Group.Raid("A-6E Harpoon strike", 30f, 38f, "AntiShip", "usn_a-6e", "usn_a-6e").At(5000f).Missiles(M);
        internal static readonly Group B52s = Group.Raid("B-52G Harpoon raid", 85f, 95f, "AntiShipHeavy", "usaf_b-52g", "usaf_b-52g").At(25000f).Missiles(H);

        // Mini-bosses: gunships early, so a gun cruiser always has a fair fight.
        internal static readonly Group BattleBoss = Group.Ships("Battle-class pair", 12f, 15f, "rn_dd_battle", "rn_dd_battle");
        internal static readonly Group GearingBoss = Group.Ships("Gearing pair", 12f, 15f, "usn_dd_gearing_fram2", "usn_dd_gearing_fram2");
        internal static readonly Group IntruderBoss = Group.Raid("A-6E bomber trio", 24f, 30f, "Strike", "usn_a-6e", "usn_a-6e", "usn_a-6e").At(5000f);
        internal static readonly Group OklahomaCityBoss = Group.Ships("Oklahoma City cruiser", 15f, 18f, "usn_clg_oklahoma_city").Missiles(H);
        internal static readonly Group OklahomaCityAndGearings = Group.Ships("Oklahoma City and Gearing pair", 15f, 18f, "usn_clg_oklahoma_city", "usn_dd_gearing_fram2", "usn_dd_gearing_fram2").Missiles(H);
        internal static readonly Group IowaBoss = Group.Ships("Iowa battleship", 18f, 20f, "usn_bb_iowa").Missiles(H);


        public static readonly Minute[] Minutes =
        {
            /* 0 */ new Minute { },
            /* 1 */ new Minute { SetPiece = CoastalConvoy },
            /* 2 */ new Minute { },
            /* 3 */ new Minute { },
            /* 4 */ new Minute { SetPiece = BelknapSag },
            /* 5 */ new Minute { Event = PgmRing },
            /* 6 */ new Minute { },
            /* 7 */ new Minute { SetPiece = SealiftConvoy },
            /* 8 */ new Minute { Event = SkyhawkSwarm },
            /* 9 */ new Minute { },
            /* 10 */ new Minute { Event = SleipnerRing, SetPiece = TiconderogaSag },
            /* 11 */ new Minute { Event = Flotilla },
            /* 12 */ new Minute { },
            /* 13 */ new Minute { Event = SkyhawkSwarm, SetPiece = TankerConvoy },
            /* 14 */ new Minute { Event = TigerRing },
        };
    }
}
