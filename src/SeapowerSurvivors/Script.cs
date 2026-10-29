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
    public Capability Requires = Capability.None;
    /// <summary>
    /// For groups that fire anti-ship missiles: the fleet anti-missile score
    /// needed before they may be sent. A boss is sent regardless, with its
    /// anti-ship launchers out of action until the fleet reaches it.
    /// </summary>
    public float MinDefence;
    /// <summary>For air groups: the fleet anti-air score needed before they may be sent.</summary>
    public float MinAir;
    /// <summary>What the threat budget pays to send it. Fodder is free.</summary>
    public float Cost;
    /// <summary>A bigger pack of the same, bought instead whenever the budget can afford it.</summary>
    public Group Bigger;
    /// <summary>Each unit comes on its own bearing, evenly round the compass: a wall closing in.</summary>
    public bool IsRing;

    public static Group Ships(string name, float minNm, float maxNm, params string[] units) =>
        new Group { Name = name, Domain = Domain.Surface, MinNm = minNm, MaxNm = maxNm, Units = units };

    /// <summary>Air groups default to needing anti-air in proportion to their size.</summary>
    public static Group Raid(string name, float minNm, float maxNm, string loadout, params string[] units) =>
        new Group
        {
            Name = name, Domain = Domain.Air, MinNm = minNm, MaxNm = maxNm, Loadout = loadout, Units = units,
            MinAir = units.Length <= 1 ? 0f : units.Length <= 2 ? Defence.AirLight : units.Length <= 3 ? Defence.AirMedium : Defence.AirHeavy,
        };

    public Group At(float altitudeFt)
    {
        AltitudeFt = altitudeFt;
        return this;
    }

    public Group Needs(Capability capability)
    {
        Requires = capability;
        return this;
    }

    public Group Missiles(float minDefence)
    {
        MinDefence = minDefence;
        return this;
    }

    public Group Costs(float credits)
    {
        Cost = credits;
        return this;
    }

    public Group Or(Group bigger)
    {
        Bigger = bigger;
        return this;
    }

    public Group Ring()
    {
        IsRing = true;
        return this;
    }
}

/// <summary>
/// One phase of the run (Script.PhaseSeconds of game time), in the manner of a
/// Vampire Survivors wave: a floor of cheap fodder kept alive at all times and
/// what it is made of, the dangerous groups the threat budget may buy, and
/// anything scripted to arrive as the phase begins.
/// </summary>
sealed class Minute
{
    /// <summary>Fodder units kept alive; below it, more are sent straight away.</summary>
    public int Floor;
    public Group[] Fodder;
    /// <summary>What the threat budget buys from, each at its Cost.</summary>
    public Group[] Threats;
    public Group Event;
    public Group Boss;
    /// <summary>Sent instead when the boss is an air group the fleet cannot yet face.</summary>
    public Group FallbackBoss;
}

/// <summary>
/// The run, minute by minute, in the manner of a survivors stage table: the
/// cast changes every minute, scripted spikes land at set times, and a
/// mini-boss every three minutes pays out a chest. Surviving to the end
/// clears it.
/// </summary>
/// <remarks>
/// Enemies come in two streams, after Vampire Survivors and Risk of Rain 2:
///
/// - Fodder (Vampire Survivors' wave minimum). Each minute keeps a floor of
///   cheap boats alive, sending more the moment it drops below. The floor
///   climbs from 6 to 28, dipping at boss minutes for breath, and what
///   counts as fodder moves up the ladder: patrol boats, then torpedo boats,
///   then missile boats.
/// - Threats (Risk of Rain 2's combat director). A budget of credits accrues
///   with the clock alone, compounding each minute, and every wave it buys
///   dangerous groups at their cost, each on its own bearing, so their
///   salvos land together. When it can afford a group's bigger pack it buys
///   that instead, the way the director buys elites. Kills do not speed it
///   up: the budget is the clock, and a strong fleet simply stays ahead of it.
///
/// There is one table per enemy navy; the enemy is whichever side the
/// starting ship is not. Every minute offers at least one group armed only
/// with guns or bombs, so a fleet that cannot yet stop missiles still has
/// something to fight while the missile groups wait for it.
///
/// Ships are not spawned on these rings: the director moves each surface group
/// to just outside the fleet's reach (see Reach), so the fight starts within
/// a minute. The rings matter only for aircraft: strike aircraft five or six
/// minutes out, stand-off bombers just outside their missiles' maximum range.
/// Air groups also wait for the fleet's anti-air score. First-guess numbers,
/// to be tuned in play.
/// </remarks>
static class Script
{
    /// <summary>
    /// Game time per row of the table. Ships close at under half a mile a
    /// minute, so a 15-row script needs far more than 15 game minutes for
    /// fights to happen. At this scale a run is 75 minutes of game time:
    /// fifteen real minutes at 5x, which is one of the game's own compression
    /// steps (1, 2, 3, 5, 10...). Intervals scale with it.
    /// </summary>
    public const float TimeScale = 5f;
    public const float PhaseSeconds = 60f * TimeScale;
    public const float RunSeconds = 15f * PhaseSeconds;
    /// <summary>Risk of Rain 2 stops spawning at 40 monsters on the map; so do we.</summary>
    public const int MaxEnemyUnits = 40;
    public const int MissileCap = 14;

    /// <summary>Fodder is topped up at most this often, in real seconds at the intended compression.</summary>
    public const float FodderSeconds = 3f;
    /// <summary>The threat budget is spent in waves this far apart, in real seconds.</summary>
    public const float WaveSeconds = 30f;
    /// <summary>Most groups one wave buys, as Risk of Rain 2 caps a wave.</summary>
    public const int MaxGroupsPerWave = 6;
    /// <summary>
    /// Budget income per minute of the script: 12 credits in the first,
    /// compounding by a fifth each minute (Risk of Rain 2 compounds 15% per
    /// stage), so the last minute earns about thirteen times the first.
    /// </summary>
    public const float FirstMinuteCredits = 12f, CreditGrowth = 1.2f;
    /// <summary>Unspent credits are kept up to this many minutes of income, so a held-back wave cannot bank forever.</summary>
    public const float BankMinutes = 1.5f;

    /// <summary>Credits earned per second of game time, at this point of the run.</summary>
    public static float CreditsPerSecond(float elapsedSeconds) =>
        FirstMinuteCredits * UnityEngine.Mathf.Pow(CreditGrowth, elapsedSeconds / PhaseSeconds) / PhaseSeconds;

    /// <summary>The table for the navy the starting ship is fighting.</summary>
    public static Minute[] For(SeaPower.ObjectBase playerShip) =>
        playerShip._obp._objectIniName.StartsWith("wp_") ? NatoEnemy.Minutes : SovietEnemy.Minutes;

    const float L = Defence.Light, M = Defence.Medium, H = Defence.Heavy;

    /// <summary>Soviet ships and aircraft, against a NATO starting ship.</summary>
    internal static class SovietEnemy
    {
        // Fodder: gun and torpedo craft, then the smallest missile boats.
        static readonly Group Zhuks = Group.Ships("Zhuk patrol boats", 9f, 12f, "wp_pb_zhuk", "wp_pb_zhuk", "wp_pb_zhuk");
        static readonly Group Stenkas = Group.Ships("Stenka torpedo boats", 9f, 12f, "wp_pt_stenka", "wp_pt_stenka");
        static readonly Group Komars = Group.Ships("Komar missile boats", 16f, 20f, "wp_ptg_komar", "wp_ptg_komar").Missiles(L).Costs(6f);

        // Walls: one boat on each of eight bearings.
        static readonly Group ZhukRing = Group.Ships("Zhuk ring", 9f, 12f, Enumerable.Repeat("wp_pb_zhuk", 8).ToArray()).Ring();
        static readonly Group StenkaRing = Group.Ships("Stenka ring", 9f, 12f, Enumerable.Repeat("wp_pt_stenka", 8).ToArray()).Ring();
        static readonly Group OsaRing = Group.Ships("Osa ring", 18f, 22f, Enumerable.Repeat("wp_ptg_osa1", 8).ToArray()).Ring().Missiles(H);

        // Threats. Bigger packs first, since the groups they enlarge refer to them.
        static readonly Group OsaPack = Group.Ships("Osa pack", 18f, 22f, Enumerable.Repeat("wp_ptg_osa1", 6).ToArray()).Missiles(M).Costs(24f);
        static readonly Group TarantulTrio = Group.Ships("Tarantul trio", 20f, 26f, "wp_ptg_tarantul", "wp_ptg_tarantul", "wp_ptg_tarantul").Missiles(H).Costs(18f);
        static readonly Group YakSwarm = Group.Raid("Yak-38 swarm", 40f, 50f, "AntiShip", Enumerable.Repeat("wp_yak-38", 6).ToArray()).At(3000f).Costs(18f);
        static readonly Group FencerFour = Group.Raid("Su-24A strike four", 50f, 60f, "AntiShip", "wp_su-24a", "wp_su-24a", "wp_su-24a", "wp_su-24a").At(5000f).Missiles(H).Costs(20f);

        static readonly Group Petyas = Group.Ships("Petya II light frigates", 12f, 15f, "wp_skr_petya2", "wp_skr_petya2").Costs(10f);
        static readonly Group SverdlovEnemy = Group.Ships("Sverdlov cruiser", 15f, 18f, "wp_kr_sverdlov").Costs(25f);
        static readonly Group Osas = Group.Ships("Osa missile boats", 18f, 22f, "wp_ptg_osa1", "wp_ptg_osa1").Missiles(L).Costs(8f).Or(OsaPack);
        static readonly Group Tarantuls = Group.Ships("Tarantul corvettes", 20f, 26f, "wp_ptg_tarantul", "wp_ptg_tarantul").Missiles(M).Costs(12f).Or(TarantulTrio);
        static readonly Group Nanuchka = Group.Ships("Nanuchka", 30f, 36f, "wp_mrk_nanuchka").Missiles(M).Costs(8f);

        // Strikers: bombs, or missiles from inside the fleet's SAM envelope.
        internal static readonly Group Yaks = Group.Raid("Yak-38 bombers", 40f, 50f, "AntiShip", "wp_yak-38", "wp_yak-38").At(3000f).Costs(6f).Or(YakSwarm);
        internal static readonly Group Badger = Group.Raid("Tu-16 bomber", 50f, 60f, "Strike", "wp_tu-16").At(15000f).Costs(8f);
        internal static readonly Group Fencers = Group.Raid("Su-24A strike", 50f, 60f, "AntiShip", "wp_su-24a", "wp_su-24a").At(5000f).Missiles(M).Costs(10f).Or(FencerFour);
        static readonly Group Floggers = Group.Raid("MiG-27 strike", 45f, 55f, "AntiShip", "wp_mig-27", "wp_mig-27").At(5000f).Missiles(M).Costs(8f);
        static readonly Group FencerMs = Group.Raid("Su-24M Kh-59 strike", 55f, 65f, "AntiShipPrecision", "wp_su-24m", "wp_su-24m").At(8000f).Missiles(H).Costs(12f);

        // Stand-off raids: KSR-5 and Kh-22 cannot be fired inside 100 nm, so
        // they also wait for air cover that can go out and kill the bombers.
        internal static readonly Group BadgerKs = Group.Raid("Tu-16K stand-off raid", 160f, 175f, "Strike", "wp_tu-16k", "wp_tu-16k").Needs(Capability.AirCover).Missiles(H).Costs(16f);
        static readonly Group Backfires = Group.Raid("Tu-22M2 stand-off raid", 200f, 220f, "AntiShip", "wp_tu-22m2", "wp_tu-22m2").Needs(Capability.AirCover).Missiles(H).Costs(24f);

        // Mini-bosses.
        static readonly Group PetyaBoss = Group.Ships("Petya II trio", 12f, 15f, "wp_skr_petya2", "wp_skr_petya2", "wp_skr_petya2");
        static readonly Group KashinBoss = Group.Ships("Kashin destroyer", 30f, 34f, "wp_bpk_kashin_mod").Missiles(M);
        static readonly Group BadgerBoss = Group.Raid("Tu-16 bomber trio", 50f, 60f, "Strike", "wp_tu-16", "wp_tu-16", "wp_tu-16").At(15000f);
        static readonly Group KrestaBoss = Group.Ships("Kresta I cruiser", 40f, 45f, "wp_rkr_kresta1").Missiles(H);
        static readonly Group SovremennyBoss = Group.Ships("Sovremenny destroyer", 40f, 45f, "wp_em_sovremenny").Missiles(H);

        public static readonly Minute[] Minutes =
        {
            /* 0 */ new Minute { Floor = 6, Fodder = new[] { Zhuks }, Threats = new[] { Komars, Petyas } },
            /* 1 */ new Minute { Floor = 8, Fodder = new[] { Zhuks, Stenkas }, Threats = new[] { Komars, Petyas, Yaks } },
            /* 2 */ new Minute { Floor = 10, Fodder = new[] { Zhuks, Stenkas }, Threats = new[] { Komars, Osas, Petyas, Yaks } },
            /* 3 */ new Minute { Floor = 6, Fodder = new[] { Zhuks, Stenkas }, Threats = new[] { Osas, Petyas }, Boss = KashinBoss },
            /* 4 */ new Minute { Floor = 14, Fodder = new[] { Zhuks, Stenkas, Komars }, Threats = new[] { Osas, Yaks, Petyas, Badger } },
            /* 5 */ new Minute { Floor = 16, Fodder = new[] { Zhuks, Stenkas, Komars }, Threats = new[] { Osas, Yaks, Badger, Tarantuls }, Event = ZhukRing },
            /* 6 */ new Minute { Floor = 10, Fodder = new[] { Stenkas, Komars }, Threats = new[] { Osas, Yaks, Fencers }, Boss = BadgerBoss, FallbackBoss = PetyaBoss },
            /* 7 */ new Minute { Floor = 18, Fodder = new[] { Stenkas, Komars, Osas }, Threats = new[] { Fencers, Tarantuls, Yaks, BadgerKs, Osas } },
            /* 8 */ new Minute { Floor = 20, Fodder = new[] { Stenkas, Komars, Osas }, Threats = new[] { Tarantuls, Fencers, Osas, Nanuchka }, Event = YakSwarm },
            /* 9 */ new Minute { Floor = 12, Fodder = new[] { Komars, Osas }, Threats = new[] { Tarantuls, Nanuchka, Fencers }, Boss = KrestaBoss },
            /* 10 */ new Minute { Floor = 22, Fodder = new[] { Stenkas, Komars, Osas }, Threats = new[] { Nanuchka, Floggers, Tarantuls, Badger, SverdlovEnemy }, Event = StenkaRing },
            /* 11 */ new Minute { Floor = 24, Fodder = new[] { Komars, Osas }, Threats = new[] { Tarantuls, Floggers, Fencers, Backfires, Yaks }, Event = OsaPack },
            /* 12 */ new Minute { Floor = 14, Fodder = new[] { Komars, Osas }, Threats = new[] { Nanuchka, FencerMs, Tarantuls }, Boss = SovremennyBoss },
            /* 13 */ new Minute { Floor = 26, Fodder = new[] { Stenkas, Komars, Osas }, Threats = new[] { FencerMs, Floggers, Tarantuls, Osas, Yaks }, Event = YakSwarm },
            /* 14 */ new Minute { Floor = 28, Fodder = new[] { Zhuks, Stenkas, Komars, Osas }, Threats = new[] { FencerMs, Floggers, Tarantuls, Nanuchka, BadgerKs, SverdlovEnemy }, Event = OsaRing },
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
        static readonly Group Pgms = Group.Ships("PGM gunboats", 8f, 11f, "usn_pb_pgm-39", "usn_pb_pgm-39", "usn_pb_pgm-39");
        static readonly Group Sleipners = Group.Ships("Sleipner corvettes", 10f, 13f, "knm_fs_sleipner", "knm_fs_sleipner");
        static readonly Group Hauks = Group.Ships("Hauk Penguin boats", 14f, 18f, "knm_ptg_hauk", "knm_ptg_hauk").Missiles(L).Costs(6f);
        static readonly Group Snoggs = Group.Ships("Snøgg Penguin boats", 14f, 18f, "knm_ptg_snogg", "knm_ptg_snogg").Missiles(L).Costs(6f);

        // Walls: one boat on each of eight bearings.
        static readonly Group PgmRing = Group.Ships("PGM ring", 8f, 11f, Enumerable.Repeat("usn_pb_pgm-39", 8).ToArray()).Ring();
        static readonly Group SleipnerRing = Group.Ships("Sleipner ring", 10f, 13f, Enumerable.Repeat("knm_fs_sleipner", 8).ToArray()).Ring();
        static readonly Group TigerRing = Group.Ships("Tiger ring", 18f, 22f, Enumerable.Repeat("fgs_ptg_tiger", 8).ToArray()).Ring().Missiles(H);

        // Threats. Bigger packs first, since the groups they enlarge refer to them.
        static readonly Group Flotilla = Group.Ships("Destroyer flotilla", 12f, 15f, "rn_dd_z-class", "rn_dd_c-class", "rn_dd_c-class", "rn_dd_battle").Costs(24f);
        static readonly Group TigerPack = Group.Ships("Tiger pack", 18f, 22f, Enumerable.Repeat("fgs_ptg_tiger", 6).ToArray()).Missiles(H).Costs(24f);
        static readonly Group SkyhawkSwarm = Group.Raid("A-4G swarm", 40f, 50f, "AntiShip", Enumerable.Repeat("ran_a-4g", 6).ToArray()).At(3000f).Costs(18f);
        static readonly Group CorsairFour = Group.Raid("A-7E bomber four", 40f, 50f, "Strike", "usn_a-7e", "usn_a-7e", "usn_a-7e", "usn_a-7e").At(5000f).Costs(16f);

        static readonly Group ZClass = Group.Ships("Z-class destroyer", 11f, 14f, "rn_dd_z-class").Costs(6f);
        static readonly Group CClass = Group.Ships("C-class destroyers", 11f, 14f, "rn_dd_c-class", "rn_dd_c-class").Costs(10f).Or(Flotilla);
        static readonly Group Battles = Group.Ships("Battle-class destroyers", 12f, 15f, "rn_dd_battle", "rn_dd_battle").Costs(12f);
        static readonly Group Gearings = Group.Ships("Gearing FRAM II destroyers", 12f, 15f, "usn_dd_gearing_fram2", "usn_dd_gearing_fram2").Costs(14f);
        static readonly Group Tigers = Group.Ships("Tiger Exocet boats", 18f, 22f, "fgs_ptg_tiger", "fgs_ptg_tiger").Missiles(M).Costs(8f).Or(TigerPack);
        static readonly Group Pegasus = Group.Ships("Pegasus hydrofoils", 25f, 30f, "usn_phm_pegasus", "usn_phm_pegasus").Missiles(M).Costs(10f);
        static readonly Group Knox = Group.Ships("Knox frigate", 30f, 36f, "usn_ff_knox").Missiles(M).Costs(15f);
        static readonly Group Adams = Group.Ships("Adams destroyer", 30f, 36f, "usn_ddg_adams_early").Missiles(H).Costs(20f);
        static readonly Group Lutjens = Group.Ships("Lütjens destroyer", 30f, 36f, "fgs_ddg_lutjens").Missiles(H).Costs(20f);
        static readonly Group Belknap = Group.Ships("Belknap cruiser", 35f, 40f, "usn_cg_belknap").Missiles(H).Costs(25f);

        // Strikers.
        internal static readonly Group Corsairs = Group.Raid("A-7E bombers", 40f, 50f, "Strike", "usn_a-7e", "usn_a-7e").At(5000f).Costs(8f).Or(CorsairFour);
        static readonly Group Skyhawks = Group.Raid("A-4G bombers", 40f, 50f, "AntiShip", "ran_a-4g", "ran_a-4g").At(3000f).Costs(6f).Or(SkyhawkSwarm);
        static readonly Group Harriers = Group.Raid("AV-8A strike", 40f, 50f, "Strike", "usmc_av-8a", "usmc_av-8a").At(3000f).Costs(8f);
        static readonly Group IntruderHarpoons = Group.Raid("A-6E Harpoon strike", 55f, 65f, "AntiShip", "usn_a-6e", "usn_a-6e").At(5000f).Missiles(M).Costs(12f);
        static readonly Group B52s = Group.Raid("B-52G Harpoon raid", 85f, 95f, "AntiShipHeavy", "usaf_b-52g", "usaf_b-52g").At(25000f).Needs(Capability.AirCover).Missiles(H).Costs(24f);

        // Mini-bosses: gunships early, so a gun cruiser always has a fair fight.
        static readonly Group BattleBoss = Group.Ships("Battle-class pair", 12f, 15f, "rn_dd_battle", "rn_dd_battle");
        static readonly Group GearingBoss = Group.Ships("Gearing pair", 12f, 15f, "usn_dd_gearing_fram2", "usn_dd_gearing_fram2");
        static readonly Group IntruderBoss = Group.Raid("A-6E bomber trio", 45f, 55f, "Strike", "usn_a-6e", "usn_a-6e", "usn_a-6e").At(5000f);
        static readonly Group OklahomaCityBoss = Group.Ships("Oklahoma City cruiser", 15f, 18f, "usn_clg_oklahoma_city").Missiles(H);
        static readonly Group IowaBoss = Group.Ships("Iowa battleship", 18f, 20f, "usn_bb_iowa").Missiles(H);

        public static readonly Minute[] Minutes =
        {
            /* 0 */ new Minute { Floor = 6, Fodder = new[] { Pgms }, Threats = new[] { ZClass } },
            /* 1 */ new Minute { Floor = 8, Fodder = new[] { Pgms, Sleipners }, Threats = new[] { ZClass, Corsairs } },
            /* 2 */ new Minute { Floor = 10, Fodder = new[] { Pgms, Sleipners }, Threats = new[] { ZClass, CClass, Hauks, Corsairs } },
            /* 3 */ new Minute { Floor = 6, Fodder = new[] { Pgms, Sleipners }, Threats = new[] { ZClass, Hauks }, Boss = BattleBoss },
            /* 4 */ new Minute { Floor = 14, Fodder = new[] { Pgms, Sleipners, Hauks }, Threats = new[] { CClass, Corsairs, Snoggs } },
            /* 5 */ new Minute { Floor = 16, Fodder = new[] { Pgms, Sleipners, Hauks }, Threats = new[] { CClass, Corsairs, Skyhawks, Snoggs }, Event = PgmRing },
            /* 6 */ new Minute { Floor = 10, Fodder = new[] { Sleipners, Hauks }, Threats = new[] { CClass, Skyhawks, Tigers }, Boss = IntruderBoss, FallbackBoss = GearingBoss },
            /* 7 */ new Minute { Floor = 18, Fodder = new[] { Sleipners, Hauks, Snoggs }, Threats = new[] { Battles, Corsairs, Tigers, Pegasus, B52s } },
            /* 8 */ new Minute { Floor = 20, Fodder = new[] { Sleipners, Hauks, Snoggs }, Threats = new[] { Gearings, Battles, Pegasus, IntruderHarpoons }, Event = SkyhawkSwarm },
            /* 9 */ new Minute { Floor = 12, Fodder = new[] { Hauks, Snoggs }, Threats = new[] { Gearings, Knox, Skyhawks }, Boss = OklahomaCityBoss },
            /* 10 */ new Minute { Floor = 22, Fodder = new[] { Sleipners, Hauks, Snoggs }, Threats = new[] { Gearings, Knox, IntruderHarpoons, Corsairs }, Event = SleipnerRing },
            /* 11 */ new Minute { Floor = 24, Fodder = new[] { Hauks, Snoggs, Tigers }, Threats = new[] { Battles, Adams, Tigers, Harriers }, Event = Flotilla },
            /* 12 */ new Minute { Floor = 14, Fodder = new[] { Hauks, Snoggs }, Threats = new[] { Gearings, Lutjens, IntruderHarpoons, Skyhawks }, Boss = IowaBoss },
            /* 13 */ new Minute { Floor = 26, Fodder = new[] { Sleipners, Hauks, Snoggs, Tigers }, Threats = new[] { Belknap, Adams, IntruderHarpoons, Gearings, Corsairs }, Event = SkyhawkSwarm },
            /* 14 */ new Minute { Floor = 28, Fodder = new[] { Pgms, Sleipners, Hauks, Snoggs, Tigers }, Threats = new[] { Belknap, Lutjens, Knox, Gearings, Battles, Harriers, B52s }, Event = TigerRing },
        };
    }
}
