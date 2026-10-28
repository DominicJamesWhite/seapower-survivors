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
}

/// <summary>
/// One phase of the run (Script.PhaseSeconds of game time): which groups may
/// be sent, how many enemy units the director keeps alive, how often it may
/// send another group, and anything scripted to arrive as the phase begins.
/// IntervalSeconds is in real seconds at the intended compression; the director scales it.
/// </summary>
sealed class Minute
{
    public int Quota;
    public float IntervalSeconds;
    public Group[] Menu;
    public Group Event;
    public Group Boss;
    /// <summary>Sent instead when the boss is an air group the fleet cannot yet face.</summary>
    public Group FallbackBoss;
}

/// <summary>
/// The run, minute by minute, in the manner of a survivors stage table: the
/// cast changes every minute, a quota of live enemies is topped up as they
/// die, scripted spikes land at set times, and a mini-boss every three
/// minutes pays out a chest. Surviving to the end clears it.
/// </summary>
/// <remarks>
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
    public const int MaxEnemyUnits = 16;
    public const int MissileCap = 14;

    /// <summary>The table for the navy the starting ship is fighting.</summary>
    public static Minute[] For(SeaPower.ObjectBase playerShip) =>
        playerShip._obp._objectIniName.StartsWith("wp_") ? NatoEnemy.Minutes : SovietEnemy.Minutes;

    const float L = Defence.Light, M = Defence.Medium, H = Defence.Heavy;

    /// <summary>Soviet ships and aircraft, against a NATO starting ship.</summary>
    internal static class SovietEnemy
    {
        // Gun and torpedo craft.
        static readonly Group Zhuks = Group.Ships("Zhuk patrol boats", 9f, 12f, "wp_pb_zhuk", "wp_pb_zhuk", "wp_pb_zhuk");
        static readonly Group Stenkas = Group.Ships("Stenka torpedo boats", 9f, 12f, "wp_pt_stenka", "wp_pt_stenka");
        static readonly Group Petyas = Group.Ships("Petya II light frigates", 12f, 15f, "wp_skr_petya2", "wp_skr_petya2");
        static readonly Group SverdlovEnemy = Group.Ships("Sverdlov cruiser", 15f, 18f, "wp_kr_sverdlov");

        // Missile craft.
        static readonly Group Komars = Group.Ships("Komar missile boats", 16f, 20f, "wp_ptg_komar", "wp_ptg_komar").Missiles(L);
        static readonly Group Osas = Group.Ships("Osa missile boats", 18f, 22f, "wp_ptg_osa1", "wp_ptg_osa1").Missiles(L);
        static readonly Group OsaPack = Group.Ships("Osa pack", 18f, 22f, "wp_ptg_osa1", "wp_ptg_osa1", "wp_ptg_osa1", "wp_ptg_osa1", "wp_ptg_osa1", "wp_ptg_osa1").Missiles(M);
        static readonly Group Tarantuls = Group.Ships("Tarantul corvettes", 20f, 26f, "wp_ptg_tarantul", "wp_ptg_tarantul").Missiles(M);
        static readonly Group Nanuchka = Group.Ships("Nanuchka", 30f, 36f, "wp_mrk_nanuchka").Missiles(M);

        // Strikers: bombs, or missiles from inside the fleet's SAM envelope.
        internal static readonly Group Yaks = Group.Raid("Yak-38 bombers", 40f, 50f, "AntiShip", "wp_yak-38", "wp_yak-38").At(3000f);
        static readonly Group YakSwarm = Group.Raid("Yak-38 swarm", 40f, 50f, "AntiShip", "wp_yak-38", "wp_yak-38", "wp_yak-38", "wp_yak-38", "wp_yak-38", "wp_yak-38").At(3000f);
        internal static readonly Group Badger = Group.Raid("Tu-16 bomber", 50f, 60f, "Strike", "wp_tu-16").At(15000f);
        internal static readonly Group Fencers = Group.Raid("Su-24A strike", 50f, 60f, "AntiShip", "wp_su-24a", "wp_su-24a").At(5000f).Missiles(M);
        static readonly Group Floggers = Group.Raid("MiG-27 strike", 45f, 55f, "AntiShip", "wp_mig-27", "wp_mig-27").At(5000f).Missiles(M);
        static readonly Group FencerMs = Group.Raid("Su-24M Kh-59 strike", 55f, 65f, "AntiShipPrecision", "wp_su-24m", "wp_su-24m").At(8000f).Missiles(H);

        // Stand-off raids: KSR-5 and Kh-22 cannot be fired inside 100 nm, so
        // they also wait for air cover that can go out and kill the bombers.
        internal static readonly Group BadgerKs = Group.Raid("Tu-16K stand-off raid", 160f, 175f, "Strike", "wp_tu-16k", "wp_tu-16k").Needs(Capability.AirCover).Missiles(H);
        static readonly Group Backfires = Group.Raid("Tu-22M2 stand-off raid", 200f, 220f, "AntiShip", "wp_tu-22m2", "wp_tu-22m2").Needs(Capability.AirCover).Missiles(H);

        // Mini-bosses.
        static readonly Group PetyaBoss = Group.Ships("Petya II trio", 12f, 15f, "wp_skr_petya2", "wp_skr_petya2", "wp_skr_petya2");
        static readonly Group KashinBoss = Group.Ships("Kashin destroyer", 30f, 34f, "wp_bpk_kashin_mod").Missiles(M);
        static readonly Group BadgerBoss = Group.Raid("Tu-16 bomber trio", 50f, 60f, "Strike", "wp_tu-16", "wp_tu-16", "wp_tu-16").At(15000f);
        static readonly Group KrestaBoss = Group.Ships("Kresta I cruiser", 40f, 45f, "wp_rkr_kresta1").Missiles(H);
        static readonly Group SovremennyBoss = Group.Ships("Sovremenny destroyer", 40f, 45f, "wp_em_sovremenny").Missiles(H);

        public static readonly Minute[] Minutes =
        {
            /* 0 */ new Minute { Quota = 4, IntervalSeconds = 8f, Menu = new[] { Zhuks } },
            /* 1 */ new Minute { Quota = 5, IntervalSeconds = 8f, Menu = new[] { Zhuks, Stenkas } },
            /* 2 */ new Minute { Quota = 6, IntervalSeconds = 8f, Menu = new[] { Zhuks, Komars, Petyas } },
            /* 3 */ new Minute { Quota = 6, IntervalSeconds = 9f, Menu = new[] { Komars, Osas, Petyas }, Boss = KashinBoss },
            /* 4 */ new Minute { Quota = 7, IntervalSeconds = 9f, Menu = new[] { Osas, Yaks, Petyas } },
            /* 5 */ new Minute { Quota = 6, IntervalSeconds = 10f, Menu = new[] { Osas, Yaks, Stenkas }, Event = OsaPack },
            /* 6 */ new Minute { Quota = 6, IntervalSeconds = 10f, Menu = new[] { Osas, Badger, Komars, Petyas }, Boss = BadgerBoss, FallbackBoss = PetyaBoss },
            /* 7 */ new Minute { Quota = 7, IntervalSeconds = 10f, Menu = new[] { Osas, Fencers, Yaks, BadgerKs } },
            /* 8 */ new Minute { Quota = 7, IntervalSeconds = 9f, Menu = new[] { Tarantuls, Fencers, Osas, Petyas }, Event = YakSwarm },
            /* 9 */ new Minute { Quota = 8, IntervalSeconds = 9f, Menu = new[] { Tarantuls, Nanuchka, Fencers, SverdlovEnemy }, Boss = KrestaBoss },
            /* 10 */ new Minute { Quota = 8, IntervalSeconds = 8f, Menu = new[] { Nanuchka, Floggers, Osas, Badger } },
            /* 11 */ new Minute { Quota = 9, IntervalSeconds = 8f, Menu = new[] { Tarantuls, Floggers, Fencers, Backfires, Yaks }, Event = OsaPack },
            /* 12 */ new Minute { Quota = 9, IntervalSeconds = 7f, Menu = new[] { Nanuchka, FencerMs, Tarantuls, SverdlovEnemy }, Boss = SovremennyBoss },
            /* 13 */ new Minute { Quota = 10, IntervalSeconds = 6f, Menu = new[] { FencerMs, Floggers, Tarantuls, Osas, Yaks }, Event = YakSwarm },
            /* 14 */ new Minute { Quota = 12, IntervalSeconds = 5f, Menu = new[] { FencerMs, Floggers, Tarantuls, Nanuchka, BadgerKs, Badger, SverdlovEnemy } },
        };
    }

    /// <summary>
    /// NATO ships and aircraft, against a Soviet starting ship. The early
    /// minutes are post-war gun destroyers, so a Sverdlov fights a gun war
    /// until its fleet can stop missiles.
    /// </summary>
    internal static class NatoEnemy
    {
        // Gunships.
        static readonly Group Pgms = Group.Ships("PGM gunboats", 8f, 11f, "usn_pb_pgm-39", "usn_pb_pgm-39", "usn_pb_pgm-39");
        static readonly Group Sleipners = Group.Ships("Sleipner corvettes", 10f, 13f, "knm_fs_sleipner", "knm_fs_sleipner");
        static readonly Group ZClass = Group.Ships("Z-class destroyer", 11f, 14f, "rn_dd_z-class");
        static readonly Group CClass = Group.Ships("C-class destroyers", 11f, 14f, "rn_dd_c-class", "rn_dd_c-class");
        static readonly Group Battles = Group.Ships("Battle-class destroyers", 12f, 15f, "rn_dd_battle", "rn_dd_battle");
        static readonly Group Gearings = Group.Ships("Gearing FRAM II destroyers", 12f, 15f, "usn_dd_gearing_fram2", "usn_dd_gearing_fram2");
        static readonly Group Flotilla = Group.Ships("Destroyer flotilla", 12f, 15f, "rn_dd_z-class", "rn_dd_c-class", "rn_dd_c-class", "rn_dd_battle");

        // Missile craft and ships.
        static readonly Group Hauks = Group.Ships("Hauk Penguin boats", 14f, 18f, "knm_ptg_hauk", "knm_ptg_hauk").Missiles(L);
        static readonly Group Snoggs = Group.Ships("Snøgg Penguin boats", 14f, 18f, "knm_ptg_snogg", "knm_ptg_snogg").Missiles(L);
        static readonly Group Tigers = Group.Ships("Tiger Exocet boats", 18f, 22f, "fgs_ptg_tiger", "fgs_ptg_tiger").Missiles(M);
        static readonly Group Pegasus = Group.Ships("Pegasus hydrofoils", 25f, 30f, "usn_phm_pegasus", "usn_phm_pegasus").Missiles(M);
        static readonly Group Knox = Group.Ships("Knox frigate", 30f, 36f, "usn_ff_knox").Missiles(M);
        static readonly Group Adams = Group.Ships("Adams destroyer", 30f, 36f, "usn_ddg_adams_early").Missiles(H);
        static readonly Group Lutjens = Group.Ships("Lütjens destroyer", 30f, 36f, "fgs_ddg_lutjens").Missiles(H);
        static readonly Group Belknap = Group.Ships("Belknap cruiser", 35f, 40f, "usn_cg_belknap").Missiles(H);

        // Strikers.
        internal static readonly Group Corsairs = Group.Raid("A-7E bombers", 40f, 50f, "Strike", "usn_a-7e", "usn_a-7e").At(5000f);
        static readonly Group Skyhawks = Group.Raid("A-4G bombers", 40f, 50f, "AntiShip", "ran_a-4g", "ran_a-4g").At(3000f);
        static readonly Group SkyhawkSwarm = Group.Raid("A-4G swarm", 40f, 50f, "AntiShip", "ran_a-4g", "ran_a-4g", "ran_a-4g", "ran_a-4g", "ran_a-4g", "ran_a-4g").At(3000f);
        static readonly Group Harriers = Group.Raid("AV-8A strike", 40f, 50f, "Strike", "usmc_av-8a", "usmc_av-8a").At(3000f);
        static readonly Group IntruderHarpoons = Group.Raid("A-6E Harpoon strike", 55f, 65f, "AntiShip", "usn_a-6e", "usn_a-6e").At(5000f).Missiles(M);
        static readonly Group B52s = Group.Raid("B-52G Harpoon raid", 85f, 95f, "AntiShipHeavy", "usaf_b-52g", "usaf_b-52g").At(25000f).Needs(Capability.AirCover).Missiles(H);

        // Mini-bosses: gunships early, so a gun cruiser always has a fair fight.
        static readonly Group BattleBoss = Group.Ships("Battle-class pair", 12f, 15f, "rn_dd_battle", "rn_dd_battle");
        static readonly Group GearingBoss = Group.Ships("Gearing pair", 12f, 15f, "usn_dd_gearing_fram2", "usn_dd_gearing_fram2");
        static readonly Group IntruderBoss = Group.Raid("A-6E bomber trio", 45f, 55f, "Strike", "usn_a-6e", "usn_a-6e", "usn_a-6e").At(5000f);
        static readonly Group OklahomaCityBoss = Group.Ships("Oklahoma City cruiser", 15f, 18f, "usn_clg_oklahoma_city").Missiles(H);
        static readonly Group IowaBoss = Group.Ships("Iowa battleship", 18f, 20f, "usn_bb_iowa").Missiles(H);

        public static readonly Minute[] Minutes =
        {
            /* 0 */ new Minute { Quota = 4, IntervalSeconds = 8f, Menu = new[] { Pgms } },
            /* 1 */ new Minute { Quota = 5, IntervalSeconds = 8f, Menu = new[] { Pgms, Sleipners } },
            /* 2 */ new Minute { Quota = 6, IntervalSeconds = 8f, Menu = new[] { Pgms, Sleipners, ZClass } },
            /* 3 */ new Minute { Quota = 6, IntervalSeconds = 9f, Menu = new[] { Sleipners, ZClass, Hauks }, Boss = BattleBoss },
            /* 4 */ new Minute { Quota = 7, IntervalSeconds = 9f, Menu = new[] { ZClass, Corsairs, Hauks } },
            /* 5 */ new Minute { Quota = 6, IntervalSeconds = 10f, Menu = new[] { CClass, Corsairs, Snoggs }, Event = Flotilla },
            /* 6 */ new Minute { Quota = 6, IntervalSeconds = 10f, Menu = new[] { CClass, Skyhawks, Tigers }, Boss = IntruderBoss, FallbackBoss = GearingBoss },
            /* 7 */ new Minute { Quota = 7, IntervalSeconds = 10f, Menu = new[] { Battles, Corsairs, Tigers, Pegasus, B52s } },
            /* 8 */ new Minute { Quota = 7, IntervalSeconds = 9f, Menu = new[] { Gearings, Battles, Pegasus, IntruderHarpoons }, Event = SkyhawkSwarm },
            /* 9 */ new Minute { Quota = 8, IntervalSeconds = 9f, Menu = new[] { Gearings, Knox, Skyhawks }, Boss = OklahomaCityBoss },
            /* 10 */ new Minute { Quota = 8, IntervalSeconds = 8f, Menu = new[] { Gearings, Knox, IntruderHarpoons, Corsairs } },
            /* 11 */ new Minute { Quota = 9, IntervalSeconds = 8f, Menu = new[] { Battles, Adams, Tigers, Harriers }, Event = Flotilla },
            /* 12 */ new Minute { Quota = 9, IntervalSeconds = 7f, Menu = new[] { Gearings, Lutjens, IntruderHarpoons, Skyhawks }, Boss = IowaBoss },
            /* 13 */ new Minute { Quota = 10, IntervalSeconds = 6f, Menu = new[] { Belknap, Adams, IntruderHarpoons, Gearings, Corsairs }, Event = SkyhawkSwarm },
            /* 14 */ new Minute { Quota = 12, IntervalSeconds = 5f, Menu = new[] { Belknap, Lutjens, Knox, Gearings, Battles, Harriers, B52s } },
        };
    }
}
