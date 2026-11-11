using System.Collections.Generic;
using System.Linq;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>What a ship or flight brings to the fleet, for choosing offers that fill a gap.</summary>
[System.Flags]
enum Role { None = 0, Guns = 1, Sams = 2, AntiShip = 4, Ciws = 8, AirCover = 16, Awacs = 32, AirStrike = 64 }

/// <summary>A ship, or a flight of aircraft, that a boss chest can offer.</summary>
sealed class Escort
{
    public string Name, Type;
    public int Tier;
    public Role Roles;
    /// <summary>For a flight: its loadout, how many aircraft and at what height. Null for a ship.</summary>
    public string Loadout;
    public int Count = 1;
    public float AltitudeFt;
    /// <summary>Service year and weapons for the Survivors screen, read off the game's data for the fit it joins with. Only the starting escorts are shown there.</summary>
    public int Year;
    public string Weapons;

    /// <summary>For a flight: the squadron whose markings it wears. Argentina's French aircraft have their own.</summary>
    public string Squadron = "Squadron1";
    /// <summary>
    /// For a starting escort that stands in for a US one: that one's store id,
    /// so what is bought sails with every navy as its own. Null for one bought on its own.
    /// </summary>
    public string Slot;

    public bool IsAir => Loadout != null;

    public Escort Described(int year, string weapons)
    {
        Year = year;
        Weapons = weapons;
        return this;
    }

    public Escort For(string slot)
    {
        Slot = slot;
        return this;
    }

    public Escort From(string squadron)
    {
        Squadron = squadron;
        return this;
    }

    public Escort(string name, string type, int tier, Role roles)
    {
        Name = name;
        Type = type;
        Tier = tier;
        Roles = roles;
    }

    public static Escort Flight(string name, string type, int tier, Role roles, string loadout, int count, float altitudeFt) =>
        new Escort(name, type, tier, roles) { Loadout = loadout, Count = count, AltitudeFt = altitudeFt };
}

/// <summary>
/// Ships and aircraft that join the fleet from boss chests: three offers from
/// the player's own navy, drawn from one pool of ships and flights, of a tier
/// that rises through the run, with at least one that brings something the
/// fleet lacks. They join the player's task force under the player's command,
/// and every upgrade taken applies to them too. Only the starting ship's loss
/// ends the run.
/// </summary>
/// <remarks>
/// Flights have no carrier to go home to, so they fly on unlimited fuel and
/// are left to the game's own routing when idle. With no home base the
/// game never sends them back for fuel or for running out of weapons; level-ups
/// rearm them in the air with the rest of the fleet (see <see cref="Pylons"/>).
/// </remarks>
static class Escorts
{
    const int Offers = 3;
    /// <summary>Joining flights appear this far from the flagship, off its beam.</summary>
    const float JoinDistanceNm = 1.5f;
    /// <summary>Game time between air resupplies once the Air wing line has it, before its ranks shorten it.</summary>
    internal const float ResupplySeconds = 240f;
    const string StandOffBomber = "wp_tu-22m2";
    /// <summary>An AS-4's minimum launch range, as the game gives it, before the Anti-ship line shrinks it.</summary>
    const float StandOffMinimumNm = 100f;
    /// <summary>Clear of that minimum by this much, so the target is still outside it when the crew has a solution.</summary>
    const float StandOffMarginNm = 25f;
    const float StandOffSearchStepNm = 10f, StandOffSearchNm = 300f, StandOffBearingStep = 15f;
    /// <summary>An AS-4 will not launch below 10,000 ft, so Backfires join well above it.</summary>
    internal const float StandOffAltitudeFt = 30000f;
    const float UnityPerNm = 27.559496f;

    // The four ships and three flights every navy starts with stand in for
    // one another: each is bought once, under the US one's store id.
    const string GunSlot = "escort:usn_dd_gearing_fram2", FrigateSlot = "escort:usn_ff_knox_84", MissileSlot = "escort:usn_ffg_oliver_hazard_perry",
        SamSlot = "escort:usn_ddg_adams_early", FighterSlot = "flight:usn_f-4j:AirToAir", StrikeSlot = "flight:usn_a-7e:Strike", ScoutSlot = "flight:usn_e-2c:AEW";

    internal static readonly Escort[] Us =
    {
        new Escort("Gearing FRAM II destroyer", "usn_dd_gearing_fram2", 1, Role.Guns).Described(1959, "127 mm · Mk-44").For(GunSlot),
        new Escort("Knox frigate", "usn_ff_knox_84", 1, Role.Guns | Role.AntiShip | Role.Ciws).Described(1983, "Harpoon · 127 mm · Phalanx · ASROC · Mk-46").For(FrigateSlot),
        new Escort("Oliver Hazard Perry frigate", "usn_ffg_oliver_hazard_perry", 1, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws).Described(1977, "Harpoon · SM-1MR · 76 mm · Phalanx · Mk-46").For(MissileSlot),
        new Escort("Charles F. Adams destroyer", "usn_ddg_adams_early", 1, Role.Sams | Role.Guns).Described(1980, "Harpoon · SM-1MR · 127 mm · ASROC · Mk-46").For(SamSlot),
        new Escort("Pegasus hydrofoil", "usn_phm_pegasus", 1, Role.Guns | Role.AntiShip).Described(1977, "Harpoon · 76 mm"),
        new Escort("Garcia frigate", "usn_ff_garcia", 1, Role.Guns).Described(1964, "127 mm · ASROC · Mk-46"),
        new Escort("Brooke frigate", "usn_ffg_brooke", 1, Role.Sams | Role.Guns).Described(1972, "SM-1MR · 127 mm · ASROC · Mk-46"),
        new Escort("Spruance destroyer", "usn_dd_spruance", 2, Role.Guns | Role.Sams | Role.AntiShip | Role.Ciws),
        new Escort("Kidd destroyer", "usn_ddg_kidd", 2, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Belknap cruiser", "usn_cg_belknap", 2, Role.Sams | Role.Guns | Role.AntiShip),
        new Escort("Leahy cruiser", "usn_cg_leahy", 2, Role.Sams | Role.AntiShip),
        new Escort("Coontz destroyer", "usn_ddg_coontz", 2, Role.Sams | Role.Guns | Role.AntiShip),
        new Escort("Mahan destroyer", "usn_ddg_mahan", 2, Role.Sams | Role.Guns | Role.AntiShip),
        new Escort("Charles F. Adams destroyer, late fit", "usn_ddg_adams_late", 2, Role.Sams | Role.Guns | Role.AntiShip),
        new Escort("Ticonderoga cruiser", "usn_cg_ticonderoga", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Virginia cruiser", "usn_cgn_virginia", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Long Beach cruiser", "usn_cgn_long_beach_83", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Iowa battleship", "usn_bb_iowa", 3, Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("California cruiser", "usn_cgn_california", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Alaska strike cruiser", "usn_cgn_alaska", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        Escort.Flight("F-4J Phantom CAP pair", "usn_f-4j", 1, Role.AirCover, "AirToAir", 2, 20000f).Described(1966, "Sparrow · Sidewinder").For(FighterSlot),
        Escort.Flight("A-7E Corsair strike pair", "usn_a-7e", 1, Role.AirStrike, "Strike", 2, 10000f).Described(1969, "Mk-82 · Sidewinder · 20 mm").For(StrikeSlot),
        Escort.Flight("E-2C Hawkeye AWACS", "usn_e-2c", 2, Role.Awacs, "AEW", 1, 25000f).Described(1973, "Unarmed").For(ScoutSlot),
        Escort.Flight("F-14A Tomcat CAP pair", "usn_f-14a", 2, Role.AirCover, "AirToAir", 2, 25000f),
        Escort.Flight("A-6E Intruder Harpoon pair", "usn_a-6e", 2, Role.AirStrike, "AntiShip", 2, 10000f),
        Escort.Flight("F-14A Tomcat Phoenix flight", "usn_f-14a", 3, Role.AirCover, "AirToAirLongRange", 4, 30000f),
        Escort.Flight("A-6E Intruder Harpoon flight", "usn_a-6e", 3, Role.AirStrike, "AntiShip", 4, 10000f),
        Escort.Flight("F/A-18A Hornet strike flight", "usn_fa-18a", 3, Role.AirStrike, "AntiShip", 4, 15000f),
    };

    internal static readonly Escort[] Soviet =
    {
        new Escort("Petya II light frigate", "wp_skr_petya2", 1, Role.Guns).Described(1964, "76 mm · RBU-6000 · SET-40").For(GunSlot),
        new Escort("Krivak I frigate", "wp_skr_krivak1", 1, Role.Sams | Role.Guns).Described(1970, "SA-N-4 · 76 mm · SS-N-14 · RBU-6000 · SET-65").For(FrigateSlot),
        new Escort("Kashin destroyer", "wp_bpk_kashin_mod", 1, Role.Sams | Role.Guns | Role.AntiShip).Described(1964, "SS-N-2 · SA-N-1 · 76 mm · AK-630 · RBU-6000 · SET-65").For(MissileSlot),
        new Escort("Kanin destroyer", "wp_bpk_kanin", 1, Role.Sams | Role.Guns).Described(1960, "SA-N-1 · 57 mm · AK-230 · RBU-6000 · SET-65").For(SamSlot),
        new Escort("Osa II missile boat", "wp_ptg_osa2", 1, Role.AntiShip).Described(1965, "SS-N-2 · AK-230"),
        new Escort("Tarantul corvette", "wp_ptg_tarantul", 1, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws).Described(1979, "SS-N-2 · SA-N-10 · 76 mm · AK-630"),
        new Escort("Nanuchka corvette", "wp_mrk_nanuchka", 1, Role.Sams | Role.Guns | Role.AntiShip).Described(1970, "SS-N-9 · SA-N-4 · 57 mm"),
        new Escort("Grisha III corvette", "wp_mpk_grisha3", 1, Role.Sams | Role.Guns | Role.Ciws).Described(1970, "SA-N-4 · 57 mm · AK-630 · RBU-6000 · 533 mm torpedoes"),
        new Escort("Krivak II frigate", "wp_skr_krivak2", 1, Role.Sams | Role.Guns).Described(1975, "SA-N-4 · 100 mm · SS-N-14 · RBU-6000 · SET-65"),
        new Escort("Kynda cruiser", "wp_rkr_kynda", 2, Role.Sams | Role.Guns | Role.AntiShip),
        new Escort("Kresta I cruiser", "wp_rkr_kresta1", 2, Role.Sams | Role.Guns | Role.AntiShip),
        new Escort("Sovremenny destroyer", "wp_em_sovremenny", 2, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Udaloy destroyer", "wp_bpk_udaloy", 2, Role.Sams | Role.Guns | Role.Ciws),
        new Escort("Kresta II cruiser", "wp_bpk_kresta2", 2, Role.Sams | Role.Guns | Role.Ciws),
        new Escort("Kashin destroyer, four SA-N-1 launchers", "wp_bpk_kashin", 2, Role.Sams | Role.Guns),
        new Escort("Kara cruiser", "wp_bpk_kara", 3, Role.Sams | Role.Guns | Role.Ciws),
        new Escort("Slava cruiser", "wp_rkr_slava", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Kirov battlecruiser", "wp_rkr_kirov", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Sverdlov cruiser", "wp_kr_sverdlov", 3, Role.Guns | Role.Ciws),
        Escort.Flight("MiG-23MS CAP pair", "wp_mig-23ms", 1, Role.AirCover, "AirToAir", 2, 20000f).Described(1973, "AA-2 · 23 mm").For(FighterSlot),
        Escort.Flight("Yak-38 strike pair", "wp_yak-38", 1, Role.AirStrike, "AntiShip", 2, 5000f).Described(1976, "FAB-500").For(StrikeSlot),
        Escort.Flight("Tu-95RT Bear-D scout", "wp_tu-95rt", 2, Role.Awacs, "Recon", 1, 30000f).Described(1963, "23 mm").For(ScoutSlot),
        Escort.Flight("MiG-23MLD CAP pair", "wp_mig-23mld", 2, Role.AirCover, "AirToAir", 2, 25000f),
        Escort.Flight("Su-24A strike pair", "wp_su-24a", 2, Role.AirStrike, "AntiShip", 2, 8000f),
        Escort.Flight("MiG-25PD interceptor flight", "wp_mig-25pd", 3, Role.AirCover, "AirToAirLongRange", 4, 35000f),
        Escort.Flight("Su-24M strike flight", "wp_su-24m", 3, Role.AirStrike, "AntiShip", 4, 8000f),
        Escort.Flight("Tu-22M2 Backfire pair", "wp_tu-22m2", 3, Role.AirStrike, "AntiShip", 2, 30000f),
    };

    internal static readonly Escort[] British =
    {
        new Escort("Rothesay frigate", "rn_ff_rothesay", 1, Role.Sams | Role.Guns).Described(1960, "Seacat · 114 mm · 20 mm · Limbo · Mk-44").For(GunSlot),
        new Escort("Leander frigate, Exocet", "rn_ff_leander_batch2", 1, Role.Sams | Role.AntiShip).Described(1974, "Exocet · Seacat · 40 mm · Mk-44").For(FrigateSlot),
        new Escort("Amazon frigate", "rn_ff_amazon", 1, Role.Sams | Role.Guns | Role.AntiShip).Described(1974, "Exocet · Seacat · 114 mm · 20 mm · Mk-44").For(MissileSlot),
        new Escort("Sheffield destroyer", "rn_ddg_sheffield", 1, Role.Sams | Role.Guns).Described(1976, "Sea Dart · 114 mm · 20 mm · Mk-44").For(SamSlot),
        new Escort("Leander frigate, Ikara", "rn_ff_leander_batch1", 1, Role.Sams | Role.Guns).Described(1973, "Seacat · 40 mm · Ikara · Limbo"),
        new Escort("Leander frigate, gun", "rn_ff_leander", 1, Role.Sams | Role.Guns).Described(1967, "Seacat · 114 mm · 20 mm · Limbo · Mk-44"),
        new Escort("County destroyer", "rn_ddg_county_batch1", 2, Role.Sams | Role.Guns),
        new Escort("County destroyer, Exocet", "rn_ddg_county_batch2_75", 2, Role.Sams | Role.Guns | Role.AntiShip),
        new Escort("Sheffield destroyer, Batch 2", "rn_ddg_sheffield_batch2", 2, Role.Sams | Role.Guns),
        new Escort("Bristol destroyer", "rn_ddg_bristol", 3, Role.Sams | Role.Guns),
        new Escort("Broadsword frigate", "rn_ff_broadsword", 3, Role.Sams | Role.Guns | Role.AntiShip),
        Escort.Flight("Sea Harrier FRS.1 CAP pair", "rn_frs1", 1, Role.AirCover, "AirToAir", 2, 20000f).Described(1980, "Sidewinder · 30 mm").For(FighterSlot),
        Escort.Flight("Harrier GR.3 strike pair", "raf_gr3", 1, Role.AirStrike, "Strike", 2, 10000f).Described(1969, "1,000 lb bombs · 30 mm").For(StrikeSlot),
        Escort.Flight("Nimrod MR.2 patrol", "rn_nimrod_mr2", 2, Role.Awacs, "Recon", 1, 25000f).Described(1979, "Sidewinder").For(ScoutSlot),
        Escort.Flight("Sea Harrier FRS.1 CAP flight", "rn_frs1", 2, Role.AirCover, "AirToAir", 4, 20000f),
        Escort.Flight("Harrier GR.3 heavy strike pair", "raf_gr3", 2, Role.AirStrike, "StrikeHeavy", 2, 10000f),
        Escort.Flight("Nimrod MR.2 Harpoon pair", "rn_nimrod_mr2", 3, Role.AirStrike, "AntiShip", 2, 15000f),
        Escort.Flight("Vulcan B.2 bomber pair", "raf_vulcan", 3, Role.AirStrike, "StrikeHeavy", 2, 15000f),
        Escort.Flight("Harrier GR.3 heavy strike flight", "raf_gr3", 3, Role.AirStrike, "StrikeHeavy", 4, 10000f),
    };

    /// <summary>Argentina's Super Étendards and Mirages are French aircraft in Argentine squadrons.</summary>
    internal const string ArgentineEtendards = "Squadron2", ArgentineMirages = "Squadron6";

    internal static readonly Escort[] Argentine =
    {
        new Escort("Sumner FRAM II destroyer", "ara_dd_sumner_fram2", 1, Role.Guns | Role.AntiShip).Described(1972, "Exocet · 127 mm · Hedgehog · A-244").For(GunSlot),
        new Escort("Drummond corvette", "ara_fs_drummond", 1, Role.Guns | Role.AntiShip).Described(1978, "Exocet · 100 mm · 40 mm · Mk-44").For(FrigateSlot),
        new Escort("Comodoro Py destroyer", "ara_dd_comodoro_py", 1, Role.Guns | Role.AntiShip).Described(1973, "Exocet · 127 mm · Hedgehog · A-244").For(MissileSlot),
        new Escort("Hércules destroyer", "ara_ddg_hercules", 1, Role.Sams | Role.Guns | Role.AntiShip).Described(1976, "Exocet · Sea Dart · 114 mm · 20 mm · A-244").For(SamSlot),
        new Escort("General Belgrano cruiser", "ara_cl_belgrano", 2, Role.Sams | Role.Guns),
        new Escort("Santísima Trinidad destroyer", "ara_ddg_hercules", 3, Role.Sams | Role.Guns | Role.AntiShip),
        Escort.Flight("Dagger CAP pair", "ara_dagger", 1, Role.AirCover, "AirToAir", 2, 20000f).Described(1978, "Sidewinder · 30 mm").For(FighterSlot),
        Escort.Flight("A-4Q Skyhawk strike pair", "ara_a-4q", 1, Role.AirStrike, "Strike", 2, 10000f).Described(1972, "1,000 lb bombs · 20 mm").For(StrikeSlot),
        Escort.Flight("Boeing 707 scout", "ara_b-707", 2, Role.Awacs, "Recon", 1, 30000f).Described(1982, "Unarmed · weather radar only").For(ScoutSlot),
        Escort.Flight("Super Étendard Exocet pair", "fr_super_etendard", 2, Role.AirStrike, "AntiShip", 2, 10000f).From(ArgentineEtendards),
        Escort.Flight("Mirage III CAP pair", "fr_mirage_iii", 2, Role.AirCover, "AirToAir", 2, 25000f).From(ArgentineMirages),
        Escort.Flight("A-4P Skyhawk strike flight", "ara_a-4p", 2, Role.AirStrike, "Strike", 4, 10000f),
        Escort.Flight("Super Étendard twin-Exocet pair", "fr_super_etendard", 3, Role.AirStrike, "AntiShipHeavy", 2, 10000f).From(ArgentineEtendards),
        Escort.Flight("Dagger CAP flight", "ara_dagger", 3, Role.AirCover, "AirToAir", 4, 20000f),
        Escort.Flight("A-4Q Skyhawk heavy strike flight", "ara_a-4q", 3, Role.AirStrike, "StrikeHeavy", 4, 10000f),
    };

    internal static readonly Escort[] Chinese =
    {
        new Escort("Chengdu frigate", "plan_ff_chengdu", 1, Role.Guns | Role.AntiShip).Described(1957, "HY-1 · 100 mm · 37 mm").For(GunSlot),
        new Escort("Jianghu I frigate", "plan_ff_jianghu1", 1, Role.Guns | Role.AntiShip).Described(1975, "HY-1 · 100 mm · 37 mm").For(FrigateSlot),
        new Escort("Jiangdong frigate", "plan_ffg_jiangdong", 1, Role.Sams | Role.Guns).Described(1975, "HQ-61 · 100 mm · 37 mm").For(MissileSlot),
        new Escort("Jianghu II frigate", "plan_ff_jianghu2", 1, Role.Sams | Role.Guns | Role.AntiShip).Described(1982, "HY-1 · PL-5 · 100 mm · 37 mm").For(SamSlot),
        new Escort("Huangfeng missile boat", "plan_ptg_type_021", 1, Role.AntiShip).Described(1965, "SS-N-2 · AK-230 · 25 mm"),
        new Escort("Hainan corvette", "plan_k_type_037", 1, Role.Guns).Described(1961, "57 mm · 25 mm · anti-submarine rockets"),
        new Escort("Jianghu III frigate", "plan_ff_jianghu3", 2, Role.Guns | Role.AntiShip),
        new Escort("Luda destroyer", "plan_dd_luda1", 3, Role.Guns | Role.AntiShip),
        Escort.Flight("J-7 CAP pair", "pla_j-7c", 1, Role.AirCover, "AirToAir", 2, 20000f).Described(1980, "PL-2 · 30 mm").For(FighterSlot),
        Escort.Flight("Q-5 strike pair", "pla_q-5a", 1, Role.AirStrike, "Strike", 2, 10000f).Described(1970, "250 kg bombs · PL-5 · 23 mm").For(StrikeSlot),
        Escort.Flight("SH-5 flying boat patrol", "plan_sh-5", 2, Role.Awacs, "ASW", 1, 15000f).Described(1986, "A-244 torpedoes").For(ScoutSlot),
        Escort.Flight("H-6D missile pair", "pla_h-6d", 2, Role.AirStrike, "AntiShip", 2, 20000f),
        Escort.Flight("J-7 CAP flight", "pla_j-7c", 2, Role.AirCover, "AirToAir", 4, 20000f),
        Escort.Flight("H-6D missile flight", "pla_h-6d", 3, Role.AirStrike, "AntiShip", 4, 20000f),
        Escort.Flight("H-6A bomber pair", "pla_h-6a", 3, Role.AirStrike, "StrikeHeavy", 2, 15000f),
        Escort.Flight("Q-5 strike flight", "pla_q-5a", 3, Role.AirStrike, "Strike", 4, 10000f),
    };

    internal static readonly Escort[] Japanese =
    {
        new Escort("Yamagumo destroyer", "jmsdf_dd_yamagumo", 1, Role.Guns).Described(1966, "76 mm · ASROC · 375 mm rockets").For(GunSlot),
        new Escort("Takatsuki destroyer", "jmsdf_dd_takatsuki", 1, Role.Guns).Described(1967, "127 mm · ASROC · 375 mm rockets · Mk-46").For(FrigateSlot),
        new Escort("Hatsuyuki destroyer", "jmsdf_dd_hatsuyuki", 1, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws).Described(1982, "Harpoon · Sea Sparrow · 76 mm · Phalanx · ASROC · Mk-46").For(MissileSlot),
        new Escort("Tachikaze destroyer", "jmsdf_ddg_tachikaze", 1, Role.Sams | Role.Guns | Role.AntiShip).Described(1976, "Harpoon · SM-1MR · 127 mm · ASROC · Mk-46").For(SamSlot),
        new Escort("Minegumo destroyer", "jmsdf_dd_minegumo", 1, Role.Guns).Described(1968, "76 mm · 375 mm rockets · Mk-46"),
        new Escort("Takatsuki destroyer, 1984 refit", "jmsdf_dd_takatsuki_84", 2, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Haruna helicopter destroyer", "jmsdf_ddh_haruna", 2, Role.Guns),
        new Escort("Tachikaze destroyer, 1984 refit", "jmsdf_ddg_tachikaze_84", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        Escort.Flight("F-4EJ Phantom CAP pair", "jasdf_f-4ej", 1, Role.AirCover, "AirToAir", 2, 20000f).Described(1971, "Sparrow · Sidewinder").For(FighterSlot),
        Escort.Flight("F-1 strike pair", "jasdf_f-1", 1, Role.AirStrike, "Strike", 2, 10000f).Described(1977, "750 lb bombs · Sidewinder").For(StrikeSlot),
        Escort.Flight("PS-1 flying boat patrol", "jmsdf_ps-1", 2, Role.Awacs, "ASW", 1, 10000f).Described(1971, "Zuni rockets").For(ScoutSlot),
        Escort.Flight("F-1 ASM-1 pair", "jasdf_f-1", 2, Role.AirStrike, "AntiShip", 2, 10000f),
        Escort.Flight("F-4EJ Phantom CAP flight", "jasdf_f-4ej", 2, Role.AirCover, "AirToAir", 4, 20000f),
        Escort.Flight("F-1 ASM-1 flight", "jasdf_f-1", 3, Role.AirStrike, "AntiShip", 4, 10000f),
        Escort.Flight("F-4EJ Phantom long-range flight", "jasdf_f-4ej", 3, Role.AirCover, "AirToAirLongRange", 4, 25000f),
    };

    /// <summary>Chests at phases 3, 6, 9 and 12 offer tiers 1, 2, 2 and 3.</summary>
    internal static int TierFor(int phase) => phase < 5 ? 1 : phase < 10 ? 2 : 3;

    /// <summary>What the fleet afloat and aloft can already do.</summary>
    internal static Role FleetRoles(Run run)
    {
        var roles = Role.None;
        foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed))
        {
            if (run.AirWing.TryGetValue(ship, out var flight))
            {
                roles |= flight;
                continue;
            }
            if (Boosts.Guns(ship).Any()) roles |= Role.Guns;
            if (Boosts.SamLaunchers(ship).Any()) roles |= Role.Sams;
            if (Boosts.AntiShipLaunchers(ship).Any()) roles |= Role.AntiShip;
            if (ship._obp._ciwsWeapons.Any()) roles |= Role.Ciws;
        }
        return roles;
    }

    /// <summary>The ships and flights of the flagship's own navy.</summary>
    internal static Escort[] NavyOf(string flagshipType) => Navies.Of(flagshipType).Pool;

    /// <summary>
    /// Three ships or flights of the tier, the first chosen to bring something
    /// the fleet lacks, if any does. A reroll passes the cards it replaces, to lean away from them.
    /// </summary>
    internal static List<Escort> Offer(Run run, int tier, ICollection<Escort> shown = null)
    {
        var pool = LevelUpScreen.Shuffled(NavyOf(run.FlagshipType).Where(e => e.Tier == tier).Select(WithEnsigns).Where(e => e != null), shown).ToList();
        var missing = Ensigns.Usable(Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws | Role.AirCover | Role.Awacs | Role.AirStrike);
        missing &= ~FleetRoles(run);
        var offer = new List<Escort>();
        var filler = pool.FirstOrDefault(e => (e.Roles & missing) != 0);
        if (filler != null) offer.Add(filler);
        offer.AddRange(pool.Where(e => e != filler).Take(Offers - offer.Count));
        return offer;
    }

    /// <summary>The bomb-armed strike flights the Argentine and Dutch ensigns offer, by aircraft: the same aircraft and number, on its own unguided loadout. Tu-22M2s, H-6Ds and Nimrods have none.</summary>
    static readonly Dictionary<string, (string loadout, string weapon)> BombLoadouts = new Dictionary<string, (string, string)>
    {
        ["usn_a-6e"] = ("Strike", "Mk 83 bombs"),
        ["usn_fa-18a"] = ("Strike", "Mk 83 bombs"),
        ["wp_su-24a"] = ("Strike", "FAB-250 bombs"),
        ["wp_su-24m"] = ("Strike", "FAB-250 bombs"),
        ["fr_super_etendard"] = ("Strike", "250 kg bombs"),
        ["jasdf_f-1"] = ("Strike", "750 lb bombs"),
    };

    /// <summary>The same object each time, as a reroll leans away from cards by identity.</summary>
    static readonly Dictionary<Escort, Escort> bombVersions = new Dictionary<Escort, Escort>();

    /// <summary>
    /// What a chest offers in place of this escort under the ensigns flown: itself,
    /// a bomb-armed version, or null if it cannot be offered. Flights on a Strike
    /// loadout, and Yak-38s, already carry only iron bombs, and every other flight
    /// here is not for strike.
    /// </summary>
    static Escort WithEnsigns(Escort escort)
    {
        bool bombsOnly = Ensigns.BombsOnly;
        if (!bombsOnly || (escort.Roles & Role.AirStrike) == 0 || escort.Loadout.StartsWith("Strike") || escort.Type == "wp_yak-38") return escort;
        if (!BombLoadouts.TryGetValue(escort.Type, out var bombs)) return null;
        if (!bombVersions.TryGetValue(escort, out var version))
        {
            string name = escort.Name.Replace("Harpoon", "strike").Replace("twin-Exocet", "strike").Replace("Exocet", "strike").Replace("ASM-1", "strike") + ", " + bombs.weapon;
            bombVersions[escort] = version = Escort.Flight(name, escort.Type, escort.Tier, escort.Roles, bombs.loadout, escort.Count, escort.AltitudeFt).From(escort.Squadron);
        }
        return version;
    }

    /// <summary>A ship or flight taken from a chest: twice over, under the New Zealand ensign.</summary>
    internal static void JoinFromChest(Run run, Escort escort)
    {
        int copies = run.Hoisted.Contains(Ensigns.ChestsTwice) ? Ensigns.ChestCopies : 1;
        for (int i = 0; i < copies; i++) Join(run, escort, i);
    }

    /// <summary>What a card says the ship would add.</summary>
    /// <summary>What an escort would bring that the fleet has none of.</summary>
    internal static Role Missing(Run run, Escort escort) => Ensigns.Usable(escort.Roles) & ~FleetRoles(run);

    /// <summary>Roles as a card lists them.</summary>
    internal static string RoleList(Role roles)
    {
        string list = Describe(roles).Replace("anti-ship", "anti-ship missiles");
        return char.ToUpperInvariant(list[0]) + list.Substring(1);
    }

    internal static string Describe(Role roles) =>
        roles.ToString().Replace("Sams", "SAMs").Replace("AntiShip", "anti-ship").Replace("Ciws", "CIWS")
            .Replace("AirCover", "air cover").Replace("Awacs", "AWACS").Replace("AirStrike", "air strike");

    /// <summary>Spawn the ship on an open station in the flagship's formation, or the flight beside it, under the player's command, with every upgrade the run has taken.</summary>
    /// <param name="copy">Which of several flights joining together this is, so each has its own air. Ships find their own water.</param>
    internal static void Join(Run run, Escort escort, int copy = 0)
    {
        // The Greek ensign: the first escort taken comes as five, and is the last.
        if (run.Hoisted.Contains(Ensigns.FiveOfAKind) && !run.EscortChosen)
        {
            run.EscortChosen = true;
            run.ShipChests.Clear();
            for (int i = 0; i < Ensigns.EscortCopies; i++) Join(run, escort, i);
            return;
        }
        if (escort.IsAir)
        {
            JoinFlight(run, escort, copy);
            return;
        }
        var flagship = run.Ship;
        var formation = Formations.Of(run);
        var slot = Formations.FindStation(formation);
        var ship = Spawner.Ship(escort.Type, slot.At, Spawner.TrueHeading(flagship), Globals._playerTaskforce, Launcher.FitOf(escort.Type) ?? "default");
        if (ship == null)
        {
            TestPanel.Report($"Could not spawn {escort.Name} ({escort.Type})");
            return;
        }
        if (flagship is Vessel vessel) ship.setTelegraph(vessel._telegraph);
        ship._weaponStatus = flagship._weaponStatus;
        Formations.Take(formation, ship, slot);
        run.Fleet.Add(ship);
        Boosts.ApplyFleet(run);
        TestPanel.Report($"{escort.Name} {ship.Name.Value} joins the fleet");
    }

    /// <param name="size">How many join, for a flight sized by something other than its card and the ensigns.</param>
    internal static void JoinFlight(Run run, Escort escort, int copy, int? size = null)
    {
        var flagship = run.Ship;
        float heading = Spawner.TrueHeading(flagship);
        int joined = 0, stores = 0;
        // The Australian ensign: every flight, from the start or a card, has twice the aircraft.
        int count = size ?? escort.Count * Ensigns.AircraftFactor(run.Hoisted, escort);
        for (int i = 0; i < count; i++)
        {
            // Abreast, a mile apart, off the flagship's starboard beam.
            var at = StandsOff(escort.Type)
                ? StandOffPosition(run, i + copy * count)
                : Spawner.Offset(flagship._geoPosition, JoinDistanceNm + i + copy * count, (heading + 90f) % 360f);
            var aircraft = Spawner.Aircraft(escort.Type, escort.Squadron, escort.Loadout, at, escort.AltitudeFt, heading, Globals._playerTaskforce);
            if (aircraft == null) continue;
            aircraft._unlimitedFuel = true;
            aircraft._weaponStatus = flagship._weaponStatus;
            run.Fleet.Add(aircraft);
            run.AirWing[aircraft] = escort.Roles;
            stores += Pylons.Note(aircraft);
            joined++;
        }
        if (joined == 0)
        {
            TestPanel.Report($"Could not spawn {escort.Name} ({escort.Type}, {escort.Loadout})");
            return;
        }
        // Fighters are what reach stand-off bombers, so their raids may now come.
        if ((Ensigns.Usable(escort.Roles) & Role.AirCover) != 0) run.Has.Add(Capability.AirCover);
        Boosts.ApplyFleet(run);
        TestPanel.Report($"{escort.Name} joins the fleet ({joined}/{count} aircraft, {stores} stores)");
    }

    /// <summary>True for an aircraft whose missiles cannot be fired from anywhere near the fleet.</summary>
    internal static bool StandsOff(string type) => type == StandOffBomber;

    /// <summary>
    /// Where a stand-off bomber joins: outside its missiles' minimum range of
    /// every enemy ship, and of the flagship, since new enemies appear around
    /// it. The nearest such ring, and on it the bearing closest to an enemy,
    /// so the first shot comes soon. With no enemy afloat, astern of the fleet.
    /// </summary>
    /// <param name="slot">Which aircraft of the group this is: they fly abreast, a mile apart.</param>
    internal static GeoPosition StandOffPosition(Run run, int slot)
    {
        var flagship = run.Ship;
        float clearNm = StandOffMinimumNm / (Upgrades.Total(run, Upgrades.AntiShip, Stat.Range) * Ensigns.MissileReach) + StandOffMarginNm;
        var enemies = Singleton<ObjectsManager>.Instance._listOfAllNonPlayerVesselObjects
            .Where(v => v != null && !v.IsDestroyed).Select(v => v._geoPosition).ToList();
        float astern = Spawner.TrueHeading(flagship) + 180f;

        float NearestNm(GeoPosition at) =>
            enemies.Count == 0 ? float.MaxValue : enemies.Min(e => (float)at.GetDistance(e) / UnityPerNm);

        float chosen = astern, range = clearNm + StandOffSearchNm;
        bool found = false;
        for (float distance = clearNm; !found && distance <= clearNm + StandOffSearchNm; distance += StandOffSearchStepNm)
        {
            float closest = float.MaxValue;
            for (float offset = 0f; offset < 360f; offset += StandOffBearingStep)
            {
                float bearing = (astern + offset) % 360f;
                float nearest = NearestNm(Spawner.Offset(flagship._geoPosition, distance, bearing));
                if (nearest < clearNm || nearest >= closest) continue;
                closest = nearest;
                chosen = bearing;
                range = distance;
                found = true;
            }
        }
        return Spawner.Offset(Spawner.Offset(flagship._geoPosition, range, chosen), 1f + slot, (chosen + 90f) % 360f);
    }

    internal static void Tend(Run run)
    {
        Resupply(run);
        Refits.Tend(run);
        TendDeckAircraft(run);
    }

    /// <summary>
    /// Aircraft the player launches from a fleet ship's own deck (a carrier's)
    /// are fleet as they appear, so every upgrade reaches them as it does a
    /// chest flight. They are not the air wing: they still burn fuel and go
    /// home, and the game hangs fresh rounds on them there, so the upgrades
    /// are written in again every tick. In the air, a level-up rearms and
    /// refuels them as it does the air wing. Their pylons are noted at each
    /// takeoff, since the deck may give them a different loadout each sortie.
    /// </summary>
    static void TendDeckAircraft(Run run)
    {
        run.Fleet.RemoveAll(s => s == null);
        var deck = DeckAircraft(run).ToList();
        if (deck.Count == 0) return;
        foreach (var aircraft in deck)
        {
            if (!run.Fleet.Contains(aircraft))
            {
                run.Fleet.Add(aircraft);
                Entry.Log.LogInfo($"{aircraft.Name.Value} ({aircraft._obp._objectIniName}) from {aircraft._homeBase.Name.Value}'s deck joins the fleet");
            }
            if (!InFlight(aircraft))
            {
                Pylons.Forget(aircraft);
                continue;
            }
            if (!Pylons.Noted(aircraft)) Pylons.Note(aircraft);
            DeckStores.Tend(run, aircraft);
        }
        // A swap on to heavier stores leaves a fresh aircraft in the old one's place.
        Boosts.ApplyTo(run, DeckAircraft(run).ToList());
    }

    static IEnumerable<ObjectBase> DeckAircraft(Run run) =>
        Singleton<ObjectsManager>.Instance._listOfAllPlayerAircraftObjects
            .Where(a => a != null && !a.IsDestroyed && FromDeck(run, a));

    /// <summary>An aircraft from a fleet ship's own deck, not the air wing.</summary>
    internal static bool FromDeck(Run run, ObjectBase aircraft) =>
        aircraft.IsAirUnit && !run.AirWing.ContainsKey(aircraft) && aircraft._homeBase != null && run.Fleet.Contains(aircraft._homeBase);

    /// <summary>A ship whose own aircraft fly from its deck: a Kiev, Invincible, Hermes or Veinticinco de Mayo.</summary>
    internal static bool IsCarrier(ObjectBase ship) => ship._obp != null && Launcher.ByType(ship._obp._objectIniName)?.Carrier == true;
    /// <summary>The carrier a run started in, whose Air wing line is her own aircraft's from the start; null for any other flagship.</summary>
    internal static StartingShip CarrierOf(Run run) => run != null && Launcher.ByType(run.FlagshipType) is StartingShip { Carrier: true } ship ? ship : null;

    static bool InFlight(ObjectBase aircraft) =>
        aircraft is Aircraft { _isInFlight: true } || aircraft is Helicopter { _isInFlight: true };

    /// <summary>
    /// Fill the tanks of every deck aircraft in the air, as the deck's own
    /// fuel line does: fuel mass first, the range read from it. Returns how many.
    /// </summary>
    internal static int RefuelDeckAircraft(Run run)
    {
        int filled = 0;
        foreach (var a in DeckAircraft(run).Where(InFlight))
        {
            if (a is Aircraft plane)
            {
                plane.Ap._currentFuelMass = plane.Ap._maxFuel;
                plane.RangeInKm.Value = plane.Ap._maxRangeInKm;
            }
            else if (a is Helicopter helo)
            {
                helo._hp._currentFuelMass = helo._hp._maxFuel;
                helo.RangeInKm.Value = helo._hp._maxRangeInKm;
            }
            else continue;
            filled++;
        }
        return filled;
    }

    /// <summary>
    /// Air resupply, from the Air wing line: every few minutes the pylons of
    /// the air wing and of deck aircraft in the air are rehung as a level-up
    /// rehangs them, with the run's upgrades written into the fresh rounds,
    /// and deck aircraft are refuelled. The first comes as soon as it is taken.
    /// </summary>
    static void Resupply(Run run)
    {
        if (!Upgrades.Has(run, Upgrades.AirWing, Stat.Resupply)) return;
        double now = SeaPower.GameTime.missionElapsedTime;
        if (now - run.LastResupply < ResupplySeconds * Upgrades.Total(run, Upgrades.AirWing, Stat.Reload)) return;
        run.LastResupply = now;
        // Only noted aircraft load anything: the air wing, and deck aircraft in the air.
        int stores = run.Fleet.Concat(run.AirWing.Keys).Distinct().Sum(Pylons.Reload);
        int refuelled = RefuelDeckAircraft(run);
        if (stores == 0 && refuelled == 0) return;
        if (stores > 0) Boosts.ApplyFleet(run);
        TestPanel.Report($"Air resupply: {stores} store(s) hung on aircraft pylons, {refuelled} deck aircraft refuelled");
    }
}
