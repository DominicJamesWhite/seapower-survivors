using System.Linq;

namespace SeapowerSurvivors;

/// <summary>
/// One enemy navy: what is on the clock against it, and for the navies added
/// after the first two, everything a threat plan is built from. The Soviet
/// and NATO enemies have a plan set by hand for each flagship that meets them
/// (see Threats), so theirs carry only the timetable.
/// </summary>
sealed class Roster
{
    public string Name;
    public Minute[] Minutes;
    /// <summary>Test aids: the first convoy and surface action group.</summary>
    public SetPiece TestConvoy, TestSag;

    /// <summary>
    /// Every hull that comes in waves and what it costs a missile ship of the
    /// budget. Other classes of flagship scale it: see Threats.Generic.
    /// </summary>
    public (string type, int danger)[] Hulls;
    public Group[] Opening;
    /// <summary>What the waves buy from in each of the run's four stages.</summary>
    public Group[][] Stages;
    /// <summary>The four mini-bosses in order: gun ships, aircraft, a missile ship, the capital ship's group.</summary>
    public Group[] Bosses;
    public Group[] FinaleGroups;
    public (Group group, float nm, int flights)[] Raids;
}

/// <summary>
/// The enemy navies. The four here in full are a first pass, built from each
/// navy's ships in the game and shaped like the Soviet and NATO tables; none
/// has been balanced in play.
/// </summary>
/// <remarks>
/// A run opens against gunboats, which can be sunk before anything aboard is
/// upgraded and pay the first levels. The game has none for Argentina,
/// Britain or Japan, so the mod sails the US PGM-39 gunboat under each of
/// their flags, and the Pegasus hydrofoil under Japan's: mod/vessels copies
/// the US hull and gives it variants with the other flag.
/// </remarks>
static class Rosters
{
    const float L = Defence.Light, M = Defence.Medium, H = Defence.Heavy;

    static string[] Of(int count, string type) => Enumerable.Repeat(type, count).ToArray();

    internal static readonly Roster Soviet = new Roster
    {
        Name = "Soviet", Minutes = Script.SovietEnemy.Minutes,
        TestConvoy = Script.SovietEnemy.CoastalConvoy, TestSag = Script.SovietEnemy.KrestaSag,
    };

    internal static readonly Roster Nato = new Roster
    {
        Name = "NATO", Minutes = Script.NatoEnemy.Minutes,
        TestConvoy = Script.NatoEnemy.CoastalConvoy, TestSag = Script.NatoEnemy.BelknapSag,
    };

    /// <summary>
    /// Argentina, against a British flagship: gunboats first, then Pucarás and
    /// Skyhawks with bombs, its five surface combatants, Daggers, and Super
    /// Étendards with Exocet.
    /// </summary>
    internal static readonly Roster Argentine = MakeArgentine();

    static Roster MakeArgentine()
    {
        const string Pgm = "ara_pb_pgm-39";
        const string Pucara = "ara_pucara", Patrol = "ara_p-95", A4P = "ara_a-4p", A4Q = "ara_a-4q", Dagger = "ara_dagger", Etendard = "fr_super_etendard";
        const string Drummond = "ara_fs_drummond", Sumner = "ara_dd_sumner_fram2", Py = "ara_dd_comodoro_py", Hercules = "ara_ddg_hercules",
            Belgrano = "ara_cl_belgrano", Carrier = "ara_cv_veinticinco_de_mayo";

        var onePgm = Group.Ships("PGM gunboat", 8f, 11f, Pgm);
        var pgmPair = Group.Ships("PGM pair", 8f, 11f, Pgm, Pgm);
        var pgms = Group.Ships("PGM gunboats", 8f, 11f, Of(3, Pgm));
        var pgmRing = Group.Ships("PGM ring", 8f, 11f, Of(8, Pgm)).Ring();
        var onePucara = Group.Raid("Pucará", 18f, 24f, "Strike", Pucara).At(3000f);
        var pucaraPair = Group.Raid("Pucará pair", 18f, 24f, "Strike", Pucara, Pucara).At(3000f);
        var patrol = Group.Raid("EMB-111 patrol aircraft", 18f, 24f, "Strike", Patrol).At(3000f);
        var pucaras = Group.Raid("Pucará flight", 18f, 24f, "Strike", Of(3, Pucara)).At(3000f);
        var skyhawkPs = Group.Raid("A-4P bombers", 22f, 28f, "Strike", A4P, A4P).At(3000f);
        var skyhawkQs = Group.Raid("A-4Q bombers", 22f, 28f, "Strike", A4Q, A4Q).At(3000f);
        var skyhawkFour = Group.Raid("A-4P bomber four", 22f, 28f, "StrikeHeavy", Of(4, A4P)).At(3000f);
        var skyhawkSwarm = Group.Raid("A-4 swarm", 22f, 28f, "Strike", A4P, A4P, A4P, A4Q, A4Q, A4Q).At(3000f);
        var daggers = Group.Raid("Dagger strike", 24f, 30f, "Strike", Dagger, Dagger).At(5000f);
        var daggerFour = Group.Raid("Dagger strike four", 24f, 30f, "Strike", Of(4, Dagger)).At(5000f);
        var etendards = Group.Raid("Super Étendard Exocet strike", 30f, 38f, "AntiShip", Etendard, Etendard).From(Escorts.ArgentineEtendards).At(3000f).Missiles(M);
        var etendardsHeavy = Group.Raid("Super Étendard twin-Exocet strike", 30f, 38f, "AntiShipHeavy", Etendard, Etendard).From(Escorts.ArgentineEtendards).At(3000f).Missiles(H);

        var oneDrummond = Group.Ships("Drummond corvette", 18f, 22f, Drummond).Missiles(L);
        var drummonds = Group.Ships("Drummond corvettes", 18f, 22f, Drummond, Drummond).Missiles(M);
        var drummondTrio = Group.Ships("Drummond trio", 18f, 22f, Of(3, Drummond)).Missiles(H);
        var sumners = Group.Ships("Sumner FRAM II destroyers", 18f, 22f, Sumner, Sumner).Missiles(M);
        var py = Group.Ships("Comodoro Py destroyer", 18f, 22f, Py).Missiles(L);
        var hercules = Group.Ships("Hércules destroyer", 18f, 22f, Hercules).Missiles(M);
        var belgrano = Group.Ships("General Belgrano cruiser", 15f, 18f, Belgrano);

        var supplyConvoy = SetPiece.Convoy("Supply convoy", new[] { "civ_ms_freighter_a", "civ_ms_freighter_b", "civ_ms_freighter_a" }, Drummond);
        var belgranoGroup = SetPiece.Sag("General Belgrano group", Belgrano, Sumner, Py).Missiles(M);
        var garrisonConvoy = SetPiece.Convoy("Garrison convoy", new[] { "civ_ms_freighter_d", "civ_ms_roro_a", "civ_ms_freighter_b", "civ_ms_freighter_a" }, Sumner, Drummond).Missiles(M);
        var carrierGroup = SetPiece.Sag("Veinticinco de Mayo group", Carrier, Hercules, Drummond).Missiles(H);
        var tankerConvoy = SetPiece.Convoy("Tanker convoy", new[] { "civ_ms_ritina", "civ_ms_bulk", "civ_ms_freighter_d", "civ_ms_freighter_a", "civ_ms_freighter_b" }, Hercules, Py).Missiles(H);

        return new Roster
        {
            Name = "Argentine",
            Hulls = new[]
            {
                (Pgm, 1), (Pucara, 1), (Patrol, 1), (A4P, 2), (A4Q, 2), (Dagger, 3), (Etendard, 6),
                (Drummond, 4), (Py, 5), (Sumner, 5), (Hercules, 8), (Belgrano, 8),
            },
            Opening = new[] { onePgm, pgmPair, pgms },
            Stages = new[]
            {
                new[] { onePgm, pgmPair },
                new[] { pgmPair, pgms, onePucara, pucaraPair, patrol, pucaras, skyhawkPs, skyhawkQs, oneDrummond },
                new[] { skyhawkPs, skyhawkQs, daggers, oneDrummond, drummonds, py, sumners, belgrano, etendards },
                new[] { skyhawkFour, daggers, daggerFour, drummonds, sumners, hercules, belgrano, etendards, etendardsHeavy },
            },
            Bosses = new[]
            {
                Group.Ships("Sumner FRAM II pair", 18f, 22f, Sumner, Sumner).Missiles(M),
                Group.Raid("A-4Q bomber trio", 22f, 28f, "StrikeHeavy", Of(3, A4Q)).At(3000f),
                Group.Ships("Hércules and Santísima Trinidad", 18f, 22f, Hercules, Hercules).Missiles(H),
                Group.Ships("General Belgrano and her destroyers", 15f, 18f, Belgrano, Sumner, Py).Missiles(H),
            },
            FinaleGroups = new[] { skyhawkSwarm, daggerFour, drummondTrio, etendardsHeavy, pgmRing },
            Raids = new[] { (etendardsHeavy, Script.RaidCloseNm, Script.RaidFlights), (etendards, Script.RaidCloseNm, Script.RaidFlights) },
            TestConvoy = supplyConvoy, TestSag = belgranoGroup,
            Minutes = Timetable(supplyConvoy, belgranoGroup, pgmRing, garrisonConvoy, skyhawkSwarm, daggerFour, carrierGroup, drummondTrio, tankerConvoy, skyhawkSwarm),
        };
    }

    /// <summary>
    /// The British task force, against an Argentine flagship: gunboats, then
    /// gun frigates, Sea Harriers and Harriers with bombs, Exocet frigates,
    /// and Nimrods with Harpoon at the end. Its landing ships are unarmed, so
    /// they sail only in convoys, as merchants do.
    /// </summary>
    internal static readonly Roster British = MakeBritish();

    static Roster MakeBritish()
    {
        const string Pgm = "rn_pb_pgm-39", Lsl = "rn_lsl_roundtable", Rothesay = "rn_ff_rothesay", Leander = "rn_ff_leander", ExocetLeander = "rn_ff_leander_batch2",
            Amazon = "rn_ff_amazon", Sheffield = "rn_ddg_sheffield", Broadsword = "rn_ff_broadsword", County = "rn_ddg_county_batch2_75",
            Bristol = "rn_ddg_bristol", Invincible = "rn_cvs_invincible";
        const string SeaHarrier = "rn_frs1", Harrier = "raf_gr3", Vulcan = "raf_vulcan", Nimrod = "rn_nimrod_mr2";

        var onePgm = Group.Ships("PGM gunboat", 8f, 11f, Pgm);
        var pgmPair = Group.Ships("PGM pair", 8f, 11f, Pgm, Pgm);
        var pgms = Group.Ships("PGM gunboats", 8f, 11f, Of(3, Pgm));
        var pgmRing = Group.Ships("PGM ring", 8f, 11f, Of(8, Pgm)).Ring();
        var frigateFlotilla = Group.Ships("Frigate flotilla", 11f, 14f, Rothesay, Leander, Rothesay, Leander);
        var oneRothesay = Group.Ships("Rothesay frigate", 11f, 14f, Rothesay);
        var rothesays = Group.Ships("Rothesay frigates", 11f, 14f, Rothesay, Rothesay);
        var leanders = Group.Ships("Leander frigates", 11f, 14f, Leander, Leander);
        var frigateRing = Group.Ships("Frigate ring", 11f, 14f, Rothesay, Leander, Rothesay, Leander, Rothesay, Leander, Rothesay, Leander).Ring();
        var sheffield = Group.Ships("Sheffield destroyer", 12f, 15f, Sheffield);
        var exocetLeanders = Group.Ships("Exocet Leanders", 18f, 22f, ExocetLeander, ExocetLeander).Missiles(L);
        var exocetLeanderTrio = Group.Ships("Exocet Leander trio", 18f, 22f, Of(3, ExocetLeander)).Missiles(H);
        var amazons = Group.Ships("Amazon frigates", 18f, 22f, Amazon, Amazon).Missiles(M);
        var broadswords = Group.Ships("Broadsword frigates", 18f, 22f, Broadsword, Broadsword).Missiles(M);
        var county = Group.Ships("County destroyer", 18f, 22f, County).Missiles(M);

        var seaHarriers = Group.Raid("Sea Harrier bombers", 22f, 28f, "Strike", SeaHarrier, SeaHarrier).At(3000f);
        var seaHarrierFour = Group.Raid("Sea Harrier bomber four", 22f, 28f, "Strike", Of(4, SeaHarrier)).At(3000f);
        var harriers = Group.Raid("Harrier GR.3 strike", 22f, 28f, "StrikeHeavy", Harrier, Harrier).At(3000f);
        var harrierSwarm = Group.Raid("Harrier swarm", 22f, 28f, "Strike", Of(6, Harrier)).At(3000f);
        var vulcan = Group.Raid("Vulcan bomber", 26f, 32f, "StrikeHeavy", Vulcan).At(15000f);
        var nimrods = Group.Raid("Nimrod Harpoon strike", 30f, 38f, "AntiShip", Nimrod, Nimrod).At(5000f).Missiles(M);

        var landingForce = SetPiece.Convoy("Landing force", new[] { Lsl, Lsl, "rn_lpd_fearless" }, Rothesay);
        var countyGroup = SetPiece.Sag("County surface action group", County, Amazon, Amazon).Missiles(M);
        var supplyConvoy = SetPiece.Convoy("Task force supply convoy", new[] { "civ_ms_roro_a", "rn_ao_leaf", "civ_ms_freighter_a", Lsl }, Amazon, Sheffield).Missiles(M);
        var carrierGroup = SetPiece.Sag("Invincible carrier group", Invincible, Broadsword, Sheffield).Missiles(H);
        var tankerConvoy = SetPiece.Convoy("Tanker convoy", new[] { "rn_ao_leaf", "rn_ao_leaf", "civ_ms_ritina", "civ_ms_roro_b", "rn_ao_leaf" }, County, Broadsword).Missiles(H);

        return new Roster
        {
            Name = "British",
            Hulls = new[]
            {
                (Pgm, 1), (Rothesay, 2), (Leander, 2), (Sheffield, 3), (ExocetLeander, 4), (Amazon, 5), (Broadsword, 5), (County, 8),
                (SeaHarrier, 2), (Harrier, 2), (Vulcan, 5), (Nimrod, 6),
            },
            Opening = new[] { onePgm, pgmPair, oneRothesay },
            Stages = new[]
            {
                new[] { onePgm, pgmPair, oneRothesay },
                new[] { pgms, oneRothesay, rothesays, leanders, exocetLeanders, seaHarriers },
                new[] { rothesays, leanders, sheffield, exocetLeanders, amazons, seaHarriers, harriers, vulcan },
                new[] { leanders, sheffield, amazons, broadswords, county, seaHarrierFour, harriers, vulcan, nimrods },
            },
            Bosses = new[]
            {
                Group.Ships("Rothesay and Leander pair", 11f, 14f, Rothesay, Leander),
                Group.Raid("Sea Harrier bomber trio", 22f, 28f, "Strike", Of(3, SeaHarrier)).At(3000f),
                Group.Ships("County destroyer and Amazon", 18f, 22f, County, Amazon).Missiles(H),
                Group.Ships("Bristol and her Broadswords", 18f, 22f, Bristol, Broadsword, Broadsword).Missiles(H),
            },
            FinaleGroups = new[] { harrierSwarm, seaHarrierFour, exocetLeanderTrio, frigateRing },
            Raids = new[] { (nimrods, Script.RaidCloseNm, Script.RaidFlights) },
            TestConvoy = landingForce, TestSag = countyGroup,
            Minutes = Timetable(landingForce, countyGroup, pgmRing, supplyConvoy, harrierSwarm, frigateFlotilla, carrierGroup, exocetLeanderTrio, tankerConvoy, harrierSwarm),
        };
    }

    /// <summary>
    /// China, against a Japanese flagship: gunboats and Styx boats in numbers,
    /// as the Soviet navy sends them, then frigates and H-6 bombers.
    /// </summary>
    internal static readonly Roster Chinese = MakeChinese();

    static Roster MakeChinese()
    {
        const string Shanghai = "plan_pt_shanghai2", Hainan = "plan_k_type_037", Heku = "plan_ptg_type_024", Huangfeng = "plan_ptg_type_021",
            Chengdu = "plan_ff_chengdu", Jianghu = "plan_ff_jianghu1", Jiangdong = "plan_ffg_jiangdong", JianghuIII = "plan_ff_jianghu3", Luda = "plan_dd_luda1";
        const string Fantan = "pla_q-5a", Bomber = "pla_h-6a", MissileBomber = "pla_h-6d";

        var oneShanghai = Group.Ships("Shanghai gunboat", 9f, 12f, Shanghai);
        var shanghaiPair = Group.Ships("Shanghai pair", 9f, 12f, Shanghai, Shanghai);
        var shanghais = Group.Ships("Shanghai gunboats", 9f, 12f, Of(3, Shanghai));
        var shanghaiRing = Group.Ships("Shanghai ring", 9f, 12f, Of(8, Shanghai)).Ring();
        var oneHainan = Group.Ships("Hainan corvette", 9f, 12f, Hainan);
        var hainans = Group.Ships("Hainan corvettes", 9f, 12f, Hainan, Hainan);
        var hainanRing = Group.Ships("Hainan ring", 9f, 12f, Of(8, Hainan)).Ring();
        var jiangdong = Group.Ships("Jiangdong frigate", 12f, 15f, Jiangdong);
        var hekus = Group.Ships("Heku missile boats", 16f, 20f, Heku, Heku).Missiles(L);
        var huangfengs = Group.Ships("Huangfeng missile boats", 18f, 22f, Huangfeng, Huangfeng).Missiles(L);
        var huangfengPack = Group.Ships("Huangfeng pack", 18f, 22f, Of(6, Huangfeng)).Missiles(M);
        var huangfengRing = Group.Ships("Huangfeng ring", 18f, 22f, Of(8, Huangfeng)).Ring().Missiles(H);
        var chengdus = Group.Ships("Chengdu frigates", 20f, 26f, Chengdu, Chengdu).Missiles(L);
        var jianghus = Group.Ships("Jianghu frigates", 20f, 26f, Jianghu, Jianghu).Missiles(M);
        var luda = Group.Ships("Luda destroyer", 30f, 36f, Luda).Missiles(M);

        var fantans = Group.Raid("Q-5 bombers", 22f, 28f, "Strike", Fantan, Fantan).At(3000f);
        var fantanFour = Group.Raid("Q-5 bomber four", 22f, 28f, "Strike", Of(4, Fantan)).At(3000f);
        var fantanSwarm = Group.Raid("Q-5 swarm", 22f, 28f, "Strike", Of(6, Fantan)).At(3000f);
        var bomber = Group.Raid("H-6A bomber", 26f, 32f, "Strike", Bomber).At(15000f);
        var missileBombers = Group.Raid("H-6D missile strike", 40f, 48f, "AntiShip", MissileBomber, MissileBomber).At(15000f).Missiles(H);

        var coastalConvoy = SetPiece.Convoy("Coastal convoy", new[] { "civ_ms_freighter_a", "civ_ms_freighter_b", "civ_ms_freighter_a" }, Hainan);
        var jianghuGroup = SetPiece.Sag("Jianghu surface action group", JianghuIII, Jianghu, Jianghu).Missiles(M);
        var landingConvoy = SetPiece.Convoy("Landing convoy", new[] { "plan_ap_qiongsha", "plan_lst_yukan", "plan_lst_yukan", "civ_ms_freighter_d" }, Jiangdong, Jianghu).Missiles(M);
        var ludaGroup = SetPiece.Sag("Luda surface action group", Luda, Luda, JianghuIII).Missiles(H);
        var tankerConvoy = SetPiece.Convoy("Tanker convoy", new[] { "civ_ms_ritina", "civ_ms_bulk", "civ_ms_freighter_d", "civ_ms_freighter_a", "civ_ms_freighter_b" }, Luda, Jiangdong).Missiles(H);

        return new Roster
        {
            Name = "Chinese",
            Hulls = new[]
            {
                (Shanghai, 1), (Hainan, 1), (Jiangdong, 3), (Heku, 2), (Huangfeng, 4), (Chengdu, 4), (Jianghu, 5), (Luda, 8),
                (Fantan, 2), (Bomber, 3), (MissileBomber, 6),
            },
            Opening = new[] { oneShanghai, shanghaiPair, oneHainan },
            Stages = new[]
            {
                new[] { oneShanghai, shanghaiPair, oneHainan },
                new[] { shanghais, hainans, jiangdong, hekus, huangfengs, fantans, bomber },
                new[] { hainans, jiangdong, huangfengs, chengdus, jianghus, luda, fantans, bomber },
                new[] { jiangdong, huangfengPack, jianghus, luda, fantans, fantanFour, bomber, missileBombers },
            },
            Bosses = new[]
            {
                Group.Ships("Jiangdong frigate and Hainan pair", 12f, 15f, Jiangdong, Hainan, Hainan),
                Group.Raid("H-6A bomber trio", 26f, 32f, "Strike", Of(3, Bomber)).At(15000f),
                Group.Ships("Jianghu III frigate", 30f, 36f, JianghuIII).Missiles(H),
                Group.Ships("Luda destroyer and Jianghu pair", 30f, 36f, Luda, Jianghu, Jianghu).Missiles(H),
            },
            FinaleGroups = new[] { fantanSwarm, fantanFour, huangfengPack, hainanRing },
            Raids = new[] { (missileBombers, 45f, Script.RaidFlights) },
            TestConvoy = coastalConvoy, TestSag = jianghuGroup,
            Minutes = Timetable(coastalConvoy, jianghuGroup, shanghaiRing, landingConvoy, fantanSwarm, hainanRing, ludaGroup, huangfengPack, tankerConvoy, huangfengRing),
        };
    }

    /// <summary>
    /// Japan, against a Chinese flagship: gunboats, then gun destroyers, F-1s
    /// with bombs and then ASM-1s, Harpoon hydrofoils, and the Harpoon
    /// destroyers last.
    /// </summary>
    internal static readonly Roster Japanese = MakeJapanese();

    static Roster MakeJapanese()
    {
        const string Pgm = "jmsdf_pb_pgm-39", Pegasus = "jmsdf_phm_pegasus";
        const string Yamagumo = "jmsdf_dd_yamagumo", Minegumo = "jmsdf_dd_minegumo", Takatsuki = "jmsdf_dd_takatsuki", Haruna = "jmsdf_ddh_haruna",
            Hatsuyuki = "jmsdf_dd_hatsuyuki", Takatsuki84 = "jmsdf_dd_takatsuki_84", Tachikaze = "jmsdf_ddg_tachikaze_84";
        const string F1 = "jasdf_f-1";

        var onePgm = Group.Ships("PGM gunboat", 8f, 11f, Pgm);
        var pgmPair = Group.Ships("PGM pair", 8f, 11f, Pgm, Pgm);
        var pgms = Group.Ships("PGM gunboats", 8f, 11f, Of(3, Pgm));
        var pgmRing = Group.Ships("PGM ring", 8f, 11f, Of(8, Pgm)).Ring();
        var pegasus = Group.Ships("Pegasus hydrofoils", 25f, 30f, Pegasus, Pegasus).Missiles(M);
        var oneYamagumo = Group.Ships("Yamagumo destroyer", 11f, 14f, Yamagumo);
        var oneMinegumo = Group.Ships("Minegumo destroyer", 11f, 14f, Minegumo);
        var yamagumos = Group.Ships("Yamagumo destroyers", 11f, 14f, Yamagumo, Yamagumo);
        var minegumos = Group.Ships("Minegumo destroyers", 11f, 14f, Minegumo, Minegumo);
        var takatsukis = Group.Ships("Takatsuki destroyers", 12f, 15f, Takatsuki, Takatsuki);
        var haruna = Group.Ships("Haruna helicopter destroyer", 12f, 15f, Haruna);
        var flotilla = Group.Ships("Destroyer flotilla", 12f, 15f, Yamagumo, Minegumo, Minegumo, Takatsuki);
        var destroyerRing = Group.Ships("Destroyer ring", 11f, 14f, Yamagumo, Minegumo, Yamagumo, Minegumo, Yamagumo, Minegumo, Yamagumo, Minegumo).Ring();
        var hatsuyuki = Group.Ships("Hatsuyuki destroyer", 30f, 36f, Hatsuyuki).Missiles(M);
        var hatsuyukis = Group.Ships("Hatsuyuki destroyers", 30f, 36f, Hatsuyuki, Hatsuyuki).Missiles(H);
        var takatsuki84 = Group.Ships("Takatsuki destroyer, Harpoon", 30f, 36f, Takatsuki84).Missiles(M);
        var tachikaze = Group.Ships("Tachikaze destroyer", 30f, 36f, Tachikaze).Missiles(M);

        var bombers = Group.Raid("F-1 bombers", 22f, 28f, "Strike", F1, F1).At(5000f);
        var bomberFour = Group.Raid("F-1 bomber four", 22f, 28f, "Strike", Of(4, F1)).At(5000f);
        var bomberSwarm = Group.Raid("F-1 swarm", 22f, 28f, "StrikeHeavy", Of(6, F1)).At(3000f);
        var missiles = Group.Raid("F-1 ASM-1 strike", 24f, 27f, "AntiShip", F1, F1).At(5000f).Missiles(M);
        var missileFour = Group.Raid("F-1 ASM-1 strike four", 24f, 27f, "AntiShip", Of(4, F1)).At(5000f).Missiles(H);

        var coastalConvoy = SetPiece.Convoy("Coastal convoy", new[] { "civ_ms_car_carrier_a", "civ_ms_freighter_b", "civ_ms_freighter_a" }, Yamagumo);
        var harunaGroup = SetPiece.Sag("Haruna escort group", Haruna, Takatsuki, Takatsuki);
        var tradeConvoy = SetPiece.Convoy("Trade convoy", new[] { "civ_ms_bulk", "civ_ms_car_carrier_a", "civ_ms_roro_a", "civ_ms_freighter_d" }, Hatsuyuki, Minegumo).Missiles(M);
        var tachikazeGroup = SetPiece.Sag("Tachikaze surface action group", Tachikaze, Hatsuyuki, Hatsuyuki).Missiles(H);
        var tankerConvoy = SetPiece.Convoy("Tanker convoy", new[] { "civ_ms_ritina", "civ_ms_bulk", "civ_ms_ritina", "civ_ms_freighter_d", "civ_ms_car_carrier_a" }, Takatsuki84, Hatsuyuki).Missiles(H);

        return new Roster
        {
            Name = "Japanese",
            Hulls = new[]
            {
                (Pgm, 1), (Pegasus, 5), (Yamagumo, 2), (Minegumo, 2), (Takatsuki, 3), (Haruna, 3), (Hatsuyuki, 6), (Takatsuki84, 7), (Tachikaze, 8),
                (F1, 3),
            },
            Opening = new[] { onePgm, pgmPair, oneYamagumo },
            Stages = new[]
            {
                new[] { onePgm, pgmPair, pgms },
                new[] { pgms, oneYamagumo, oneMinegumo, yamagumos, minegumos, bombers },
                new[] { yamagumos, minegumos, takatsukis, haruna, pegasus, hatsuyuki, bombers, missiles },
                new[] { takatsukis, haruna, flotilla, hatsuyuki, hatsuyukis, takatsuki84, tachikaze, bombers, bomberFour, missiles },
            },
            Bosses = new[]
            {
                Group.Ships("Takatsuki pair", 12f, 15f, Takatsuki, Takatsuki),
                Group.Raid("F-1 bomber trio", 22f, 28f, "Strike", Of(3, F1)).At(5000f),
                Group.Ships("Tachikaze destroyer", 30f, 36f, Tachikaze).Missiles(H),
                Group.Ships("Tachikaze and Hatsuyuki pair", 30f, 36f, Tachikaze, Hatsuyuki, Hatsuyuki).Missiles(H),
            },
            FinaleGroups = new[] { bomberSwarm, bomberFour, missileFour, destroyerRing },
            Raids = new[] { (missileFour, 25f, 1), (missiles, 25f, Script.RaidFlights) },
            TestConvoy = coastalConvoy, TestSag = harunaGroup,
            Minutes = Timetable(coastalConvoy, harunaGroup, pgmRing, tradeConvoy, bomberSwarm, flotilla, tachikazeGroup, hatsuyukis, tankerConvoy, bomberSwarm),
        };
    }

    /// <summary>The clock every enemy keeps: set pieces at phases 1, 4, 7, 10 and 13, spikes between them.</summary>
    static Minute[] Timetable(SetPiece firstConvoy, SetPiece firstSag, Group firstRing, SetPiece secondConvoy, Group firstSwarm,
        Group secondRing, SetPiece secondSag, Group pack, SetPiece lastConvoy, Group lastSpike) => new[]
    {
        /* 0 */ new Minute { },
        /* 1 */ new Minute { SetPiece = firstConvoy },
        /* 2 */ new Minute { },
        /* 3 */ new Minute { },
        /* 4 */ new Minute { SetPiece = firstSag },
        /* 5 */ new Minute { Event = firstRing },
        /* 6 */ new Minute { },
        /* 7 */ new Minute { SetPiece = secondConvoy },
        /* 8 */ new Minute { Event = firstSwarm },
        /* 9 */ new Minute { },
        /* 10 */ new Minute { Event = secondRing, SetPiece = secondSag },
        /* 11 */ new Minute { Event = pack },
        /* 12 */ new Minute { },
        /* 13 */ new Minute { Event = firstSwarm, SetPiece = lastConvoy },
        /* 14 */ new Minute { Event = lastSpike },
    };
}
