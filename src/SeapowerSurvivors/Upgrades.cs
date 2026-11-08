using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Weapon upgrades are the genre's weapons: one per kind of armament in the
/// fleet, each a fixed path of small ranks. Fleet upgrades are its passives,
/// acting on every weapon. Every fifth rank is an ability, so a line's
/// abilities arrive on a fixed beat and are never a side effect of a stat:
/// a weapon line is four tiers of five (20 ranks), a fleet line three (15).
/// Each line at its last rank is a little beyond the best that has really
/// been built; the enemy answers with numbers, not quality.
/// </summary>
enum Kind { Weapon, Fleet }

enum Stat
{
    Damage, RateOfFire, Reload, KillChance, Speed, Range, Rcs, Intercept, Channels, Spoof,
    /// <summary>Guns engage aircraft with proximity fuzes; the value is the burst radius factor.</summary>
    AntiAir,
    Fuze, DecoyRule, Reaction, Accuracy,
    /// <summary>Taken off every enemy SAM's chance to hit our missile, as a fraction of it.</summary>
    Evasion,
    /// <summary>Sonar seeker range and arc.</summary>
    Seeker,
    /// <summary>Nautical miles added to a mortar's range, the same for every mortar, however short its own.</summary>
    Reach,
    /// <summary>Flights rehang their pylons in the air every few minutes, not only at level-ups.</summary>
    Resupply,
    /// <summary>Strike flights move one step up their refit ladder: a better loadout, or a better aircraft.</summary>
    Refit,
    /// <summary>A rank that grants an <see cref="Unlock"/>, and nothing else.</summary>
    Ability,
    /// <summary>How wide a jammer's cone is.</summary>
    Arc,
    /// <summary>A jammer's chance of spoiling a missile.</summary>
    JamChance,
    /// <summary>Warhead power added to every shell, before factors, so a small gun gains most.</summary>
    ShellPower,
    /// <summary>Aiming error for guns alone, on top of the Accuracy line, which mortars share.</summary>
    GunAim,
    /// <summary>How hard an aircraft can turn.</summary>
    Turn,
}

/// <summary>An ability, granted by one rank of one line.</summary>
sealed class Unlock
{
    public readonly string Name, Text;

    public Unlock(string name, string text)
    {
        Name = name;
        Text = text;
    }
}

/// <summary>One rank: an effect and the text for it, with {0} for the number, and any others a blended rank gives with it.</summary>
sealed class Step
{
    public readonly Stat Stat;
    /// <summary>The factor or bonus; for a chance, the rate it approaches its cap at.</summary>
    public readonly float Value;
    public readonly string Text;
    /// <summary>For a chance, the bonus it approaches and never reaches.</summary>
    public readonly float Cap;
    /// <summary>An ability rank's name: every fifth rank. Null for a stat rank.</summary>
    public readonly string Name;
    public readonly Unlock Unlock;
    /// <summary>A blended rank's other effects.</summary>
    Step[] also = System.Array.Empty<Step>();

    /// <summary>Everything this rank does: itself, and for a blended rank the rest.</summary>
    public IEnumerable<Step> Effects => new[] { this }.Concat(also);

    /// <summary>One rank that raises several stats at once.</summary>
    public static Step Blend(Step first, params Step[] rest) =>
        new Step(first.Stat, first.Value, first.Text, cap: first.Cap) { also = rest };

    public Step(Stat stat, float value, string text, string name = null, float cap = 0f, Unlock unlock = null)
    {
        Stat = stat;
        Value = value;
        Text = text;
        Name = name;
        Cap = cap;
        Unlock = unlock;
    }

    public bool IsAbility => Name != null;

    public static Step Ability(Unlock unlock) => new Step(Stat.Ability, 1f, unlock.Text, unlock.Name, unlock: unlock);

    /// <summary>A chance: n ranks give cap × (1 − 1/(1 + rate·n)), so each rank adds a little less and the cap is never reached.</summary>
    public static Step Chance(Stat stat, float rate, float cap, string text) => new Step(stat, rate, text, cap: cap);
}

sealed class Upgrade
{
    public string Name;
    /// <summary>The name in a HUD slot.</summary>
    public string Short;
    /// <summary>The ring's icon: see <see cref="Rings"/>.</summary>
    public string Icon;
    public Kind Kind;
    Step[] path;
    /// <summary>The ranks of this line, under the ensigns flown.</summary>
    public Step[] Path { get => Variant?.Invoke() ?? path; set => path = value; }
    /// <summary>A different path for the ensigns flown, or null for the usual one. Must be as long.</summary>
    public System.Func<Step[]> Variant;
    /// <summary>The weapon systems this upgrade would boost on one ship; empty for a fleet upgrade.</summary>
    public System.Func<ObjectBase, IEnumerable<WeaponSystem>> Systems;

    public int MaxRank => Path.Length;
    public bool Fits(IEnumerable<ObjectBase> fleet) => Kind == Kind.Fleet || fleet.Any(s => s != null && !s.IsDestroyed && Systems(s).Any());
}

/// <summary>One row of a card's table: a stat of a weapon, now and after the card. A note has only a label.</summary>
readonly struct PreviewRow
{
    public readonly string Group, Label, Before, After;

    public PreviewRow(string group, string label, string before, string after)
    {
        Group = group;
        Label = label;
        Before = before;
        After = after;
    }

    public static PreviewRow Note(string text) => new PreviewRow(null, text, null, null);
    public bool IsNote => Before == null;
}

/// <summary>A card's table, and which stats it has real numbers for: the rest keep their plain sentence.</summary>
sealed class CardPreview
{
    public readonly List<PreviewRow> Rows = new List<PreviewRow>();
    public readonly HashSet<Stat> Covered = new HashSet<Stat>();
}

/// <summary>A card's rarity: how many ranks of its line it gives at once.</summary>
enum Rarity { Common, Rare, Epic, Legendary }

static class Rarities
{
    static readonly float[] Weights = { 70f, 22f, 7f, 1f };
    /// <summary>Each rank of Luck raises a rarity's weight by this fraction.</summary>
    static readonly float[] LuckPerRank = { 0f, 0.25f, 0.5f, 1f };
    /// <summary>The game's crew skill colours, to go with the crew skill marks of <see cref="Hud.RarityBadge"/>.</summary>
    static readonly Color[] Colours =
    {
        Hud.Hex(0x949AA1),
        Hud.Hex(0xD1D7DC),
        Hud.Hex(0xB8873A),
        Hud.Hex(0xF5CB4B),
    };

    /// <summary>Ranks a card gives: 1, 2, 3 or 4.</summary>
    public static int Ranks(Rarity rarity) => (int)rarity + 1;
    public static Color Colour(Rarity rarity) => Colours[(int)rarity];

    static Rarity Max(Rarity a, Rarity b) => a > b ? a : b;

    public static Rarity Roll(int luck)
    {
        float total = 0f;
        var weights = new float[Weights.Length];
        for (int i = 0; i < Weights.Length; i++) total += weights[i] = Weights[i] * (1f + LuckPerRank[i] * luck);
        float pick = Random.value * total;
        for (int i = 0; i < weights.Length; i++)
            if ((pick -= weights[i]) < 0f) return Max((Rarity)i, Ensigns.RarityFloor);
        return Max(Rarity.Common, Ensigns.RarityFloor);
    }
}

static class Upgrades
{
    // Each line's end state is set against the best real successor to the
    // 1985 weapon it boosts, then pushed somewhat past it, and spread evenly
    // over the line's stat ranks: a factor f over k ranks is f^(1/k) a rank.

    /// <summary>
    /// Shell power +124 and damage ×2.1 over the path, then Heavy shells doubles
    /// the damage again. Rate of fire stops at ×1.6, in the first four ranks,
    /// because every shell in the air costs frame rate: past those ranks the
    /// line grows by damage and aim. Every rank raises several stats together,
    /// and the line is heavily front-loaded: rank 2 is meant to be about as
    /// lethal as a stock Harpoon and SM-1 ship (see tools/balance.py). That
    /// takes aiming, which only guns get here, since hit chance is what holds a
    /// stock gun back, and flat shell power, because the game divides a shell's
    /// cut of the hidden pool by up to 10 and a factor would run away on big
    /// guns: a 406 mm shell starts at 35, a 76 mm at 2.4. After the opening
    /// each rank's aim stays below an Accuracy rank's.
    /// Rate of fire also takes the ready rack's reload with it. Shell speed
    /// is its own stat, ×3 over the path, about a railgun's 2,400 m/s: fewer
    /// shells in the air at once, and less for a moving target to do in flight.
    /// Anti-air fire makes every gun a flak gun; Wide bursts grows its fuze. A shell
    /// only scores on an aircraft if it bursts within its fuze radius, and in
    /// game the misses come from the target moving while the shell flies,
    /// which no fire control removes: 5 m to 15 m, then 30 m on a 100 mm.
    /// </summary>
    public static readonly Upgrade Guns = MakeGuns();

    static Upgrade MakeGuns()
    {
        const string Hard = "Shells hit {0}× as hard", Power = "Shell power +{0}", Aim = "Gun aiming error ÷{0}",
            Fire = "Guns fire {0}× as fast", Quick = "Shells fly {0}× as fast", Refill = "Guns reload {0}× as fast", Far = "Guns reach {0}× as far";
        var opening = Step.Blend(new Step(Stat.GunAim, 0.408f, Aim), new Step(Stat.ShellPower, 50f, Power), new Step(Stat.RateOfFire, 1.2f, Fire), new Step(Stat.Speed, 1.4f, Quick),
            new Step(Stat.Reload, 0.833f, Refill), new Step(Stat.Range, 1.06f, Far));
        var early = Step.Blend(new Step(Stat.GunAim, 0.9f, Aim), new Step(Stat.ShellPower, 12f, Power), new Step(Stat.RateOfFire, 1.05f, Fire), new Step(Stat.Speed, 1.08f, Quick),
            new Step(Stat.Reload, 0.952f, Refill), new Step(Stat.Range, 1.06f, Far));
        var late = Step.Blend(new Step(Stat.Damage, 1.1f, Hard), new Step(Stat.GunAim, 0.91f, Aim), new Step(Stat.Speed, 1.035f, Quick), new Step(Stat.Range, 1.02f, Far));
        return new Upgrade
        {
            Name = "Guns", Short = "Guns", Icon = "gun", Kind = Kind.Weapon, Systems = s => Boosts.Guns(s).Concat<WeaponSystem>(Boosts.AircraftCannons(s)),
            Path = new[]
            {
                opening, opening, early, early, new Step(Stat.AntiAir, 3f, "Every gun engages aircraft out to 15 km, with proximity fuzes and AA fire control that re-aims every second", "Anti-air fire"),
                late, late, late, late, new Step(Stat.Fuze, 2f, "Flak bursts reach 2× as far", "Wide bursts"),
                late, late, late, late, Step.Ability(HeavyShells = new Unlock("Heavy shells", "Shells hit 2× as hard, on top of earlier ranks")),
            },
        };
    }

    /// <summary>
    /// Takes the Torpedoes line's place under the German ensign: the AN/ALQ-99 fitted
    /// in place of the tubes (see <see cref="EcmFit"/>). A 30 nm, 30° cone at 0.45 grows to
    /// 135 nm, 270° and 1.35, which beats every jammable missile's resistance
    /// (SS-N-19's 0.35 is the highest), then Extreme range takes it to 540 nm. The
    /// power stays modest until High power, since loud jamming draws home-on-jam missiles.
    /// </summary>
    public static readonly Upgrade Jammers = MakeJammers();

    static Upgrade MakeJammers()
    {
        var x = new Step(Stat.Range, 1.2847f, "Jammers reach {0}× as far");
        var a = new Step(Stat.Arc, 1.4422f, "Jammer arcs {0}× as wide");
        var c = new Step(Stat.JamChance, 1.3161f, "Jam chance ×{0}");
        return new Upgrade
        {
            Name = "Jammers", Short = "Jammers", Icon = "ecm", Kind = Kind.Weapon, Systems = s => Boosts.TorpedoLaunchers(s),
            Path = new[]
            {
                x, a, c, x, Step.Ability(HighPower = new Unlock("High power", "Jammers radiate 200 kW, up from 11, blinding radars in the cone from farther off")),
                a, x, c, a, Step.Ability(SecondLook = new Unlock("Second look", "A missile that resists is tried again every 10 s")),
                x, a, c, x, Step.Ability(HardKill = new Unlock("Hard kill", "A jammed missile is destroyed at once")),
                a, x, c, a, Step.Ability(ExtremeRange = new Unlock("Extreme range", "Jammer reach ×4")),
            },
        };
    }

    /// <summary>
    /// Reload ÷8, hit chance +45 points and four more targets at once over
    /// the path: a single-arm Mk 13's 8 s a round down to a VLS cell's 1 s,
    /// and Aegis time-sharing its illuminators over many missiles in flight.
    /// Hit chance approaches +70 and the game multiplies it down for hard
    /// targets, so points past 100% still land. Targets at once are the SAM's
    /// rate of fire: a director with one channel engages one target, however
    /// fast its launcher reloads.
    /// </summary>
    public static readonly Upgrade Sams = MakeSams();

    static Upgrade MakeSams()
    {
        var r = new Step(Stat.Reload, 0.878f, "SAM launchers reload {0}× as fast");
        var h = Step.Chance(Stat.KillChance, 0.1125f, 0.7f, "SAM hit chance +{0} points");
        var c = new Step(Stat.Channels, 1f, "Each director guides SAMs at {0} more target at once");
        var both = Step.Blend(r, h);
        var three = Step.Blend(r, h, c);
        return new Upgrade
        {
            Name = "Surface-to-air missiles", Short = "SAMs", Icon = "sam", Kind = Kind.Weapon,
            Systems = s => Boosts.SamLaunchers(s).Concat<WeaponSystem>(Boosts.Hardpoints(s, Boosts.AirToAir)),
            Path = new[]
            {
                both, both, three, both, Step.Ability(SeaSkimmerKillers = new Unlock("Sea-skimmer killers", "No low-altitude penalty, and SAMs engage missiles below their usual floor")),
                both, both, three, both, Step.Ability(Hypersonic = new Unlock("Hypersonic", "SAMs fly 2.5× as fast")),
                both, both, three, both, Step.Ability(LongReach = new Unlock("Long reach", "SAM range ×2.5")),
                both, both, three, both, Step.Ability(HugeBlasts = new Unlock("Huge blasts", "Every intercept destroys all enemy missiles and aircraft within 150 m")),
            },
        };
    }

    /// <summary>
    /// Damage ×3, speed ×3, range ×1.75 and enemy SAMs hitting 45% less often
    /// over the path: Harpoon's Mach 0.85 to BrahMos's 2.8, and its 221 kg
    /// warhead to a P-700's 750 kg. A missile that is not shot down needs far less damage,
    /// so the line is mostly about getting through: the game multiplies a
    /// SAM's chance by (1 − the missile's defence bonus), down to a 5% floor,
    /// and the abilities take on chaff, the SAM's altitude floor and CIWS.
    /// </summary>
    public static readonly Upgrade AntiShip = MakeAntiShip();

    static Upgrade MakeAntiShip()
    {
        var all = Step.Blend(new Step(Stat.Speed, 1.0724f, "Anti-ship missiles fly {0}× as fast"),
            Step.Chance(Stat.Evasion, 0.1125f, 0.7f, "Evasive manoeuvres: enemy SAMs hit our anti-ship missiles {0}% less often"),
            new Step(Stat.Damage, 1.0724f, "Anti-ship warheads hit {0}× as hard"),
            new Step(Stat.Range, 1.0356f, "Anti-ship missiles reach {0}× as far and can fire {0}× closer"));
        return new Upgrade
        {
            Name = "Anti-ship missiles", Short = "Anti-ship", Icon = "asm", Kind = Kind.Weapon,
            Systems = sh => Boosts.AntiShipLaunchers(sh).Concat<WeaponSystem>(Boosts.Hardpoints(sh, Boosts.AntiShipMissile)),
            Path = new[]
            {
                all, all, all, all, Step.Ability(SmartSeekers = new Unlock("Smart seekers", "Anti-ship missiles ignore chaff and jamming")),
                all, all, all, all, Step.Ability(WaveTop = new Unlock("Wave-top", "Anti-ship missiles skim and dive in at 10 ft: under almost every enemy SAM's floor")),
                all, all, all, all, Step.Ability(Armoured = new Unlock("Armoured", "Anti-ship missiles take 4× the CIWS hits to bring down")),
                all, all, all, all, Step.Ability(Ghost = new Unlock("Ghost", "Anti-ship missiles' radar signature ×0.1: enemy radars see them far later")),
            },
        };
    }

    /// <summary>
    /// Front-loaded: torpedoes start so far behind missiles (the Mk 46 runs
    /// 5 nm at 45 kt, the SS-N-14's AT-2 searches at 23 kt) that small ranks
    /// left them useless until late. The first four ranks take them to
    /// Shkval-like speed (×4, 180 kt for a Mk 46) and SAM-like reach (×6,
    /// 30 nm); the rest of the path refines: damage ×4, sonar ×2.5, range
    /// ×1.46 more, and the abilities. Supercavitating doubles speed again.
    /// The Mk 44, the early Mk 46 and every Soviet ship torpedo are
    /// anti-submarine only, so the first rank gives ours all ships as a
    /// second target too (see Boosts). So does every rocket or missile that
    /// carries one (ASROC, VLA, SS-N-14, SS-N-16). The carrier takes the
    /// line's speed and range, and the torpedo it drops gets the whole line.
    /// </summary>
    public static readonly Upgrade Torpedoes = MakeTorpedoes();

    static Upgrade MakeTorpedoes()
    {
        var first = new Step(Stat.Speed, 2.5f, "Torpedoes run {0}× as fast, and anti-submarine torpedoes and the rockets that carry them can target ships");
        var reach = new Step(Stat.Range, 3f, "Torpedoes run {0}× as far");
        var burst = new Step(Stat.Speed, 1.6f, "Torpedoes run {0}× as fast");
        var reach2 = new Step(Stat.Range, 2f, "Torpedoes run {0}× as far");
        var k = new Step(Stat.Seeker, 1.26f, "Torpedo sonars hear {0}× as far, over a {0}× wider arc");
        var r = new Step(Stat.Range, 1.1f, "Torpedoes run {0}× as far");
        var d = new Step(Stat.Damage, 1.4142f, "Torpedo warheads hit {0}× as hard");
        return new Upgrade
        {
            Name = "Torpedoes", Short = "Torpedoes", Icon = "torpedo", Kind = Kind.Weapon, Systems = sh => Boosts.TorpedoLaunchers(sh),
            Path = new[]
            {
                first, reach, burst, reach2, Step.Ability(Hunter = new Unlock("Hunter", "Torpedo sonars hear 2× as far over a 2× wider arc, ignore decoys, and circle back if they lose the target")),
                d, k, r, d, Step.Ability(SureShot = new Unlock("Sure shot", "Torpedo seekers and warheads never fail")),
                k, r, d, k, Step.Ability(KeelBreaker = new Unlock("Keel breaker", "Torpedo damage ×3, on top of earlier ranks")),
                r, d, k, r, Step.Ability(Supercavitating = new Unlock("Supercavitating", "Torpedoes run 2× as fast again")),
            },
        };
    }

    /// <summary>
    /// Reach +60 nm, speed ×8, reload ÷4 and damage ×2 over the path, every
    /// rank raising all four, sized so a full RBU-6000 hull stays below anti-ship
    /// missiles' rate (see tools/balance.py), since mortars outrange them and
    /// Deep magazines never runs out. With
    /// Bombardment doubling the reach to about 125 nm. No real mortar comes
    /// close: this line is meant not to be realistic, an RBU-6000 out-ranging
    /// a Harpoon. Reach adds rather than multiplies so that a half-mile RBU-1000
    /// and a 3 nm RBU-6000 both get there; the RBU-1000 keeps its heavier
    /// warhead. Speed keeps a salvo's flight to one or two minutes at every
    /// rank. Stock, a full RBU-6000 salvo that lands drains about 370 of a
    /// ship's hidden integrity (a Krivak has 184), so damage is the smallest
    /// part of the line. Rounds are unguided until Hunting rounds, and carry a
    /// gun's aiming error, which grows with range, so the Accuracy line is what
    /// makes the far end of this one land. The rest is in Mortars.cs.
    /// </summary>
    public static readonly Upgrade Mortars = MakeMortars();

    static Upgrade MakeMortars()
    {
        var all = Step.Blend(new Step(Stat.Reach, 3.75f, "Mortars reach {0} nm farther"), new Step(Stat.Speed, 1.139f, "Mortar rounds fly {0}× as fast"),
            new Step(Stat.Reload, 0.917f, "Mortars reload {0}× as fast"), new Step(Stat.Damage, 1.044f, "Mortar rounds hit {0}× as hard"));
        return new Upgrade
        {
            Name = "Rocket mortars", Short = "Mortars", Icon = "mortar", Kind = Kind.Weapon, Systems = sh => Boosts.MortarLaunchers(sh),
            Path = new[]
            {
                all, all, all, all, Step.Ability(HuntingRounds = new Unlock("Hunting rounds", "Rounds steer onto their target in flight, and if it sinks, onto the nearest enemy ship within 15 nm")),
                all, all, all, all, Step.Ability(Spotters = new Unlock("Spotters", "Enemy ships within mortar reach go on the plot, seen or not")),
                all, all, all, all, Step.Ability(DeepMagazines = new Unlock("Deep magazines", "Mortar magazines never run out")),
                all, all, all, all, Step.Ability(Bombardment = new Unlock("Bombardment", "Mortar reach ×2")),
            },
        };
    }

    /// <summary>
    /// Intercept chance +45 points (approaching +70, and the CIWS itself is
    /// held below 100%) and rate of fire ×2.2 over the path: Phalanx went
    /// from 3,000 to 4,500 rounds a minute, and AK-630 from 5,000 to the Duet's 10,000.
    /// </summary>
    public static readonly Upgrade PointDefence = MakePointDefence();

    static Upgrade MakePointDefence()
    {
        var i = Step.Chance(Stat.Intercept, 0.225f, 70f, "CIWS missile intercept chance +{0} points");
        var f = new Step(Stat.RateOfFire, 1.1026f, "CIWS mounts fire {0}× as fast");
        return new Upgrade
        {
            Name = "Point defence", Short = "Point defence", Icon = "pd", Kind = Kind.Weapon, Systems = s => s._obp._ciwsWeapons,
            Path = new[]
            {
                i, f, i, f, Step.Ability(HeavyRounds = new Unlock("Heavy rounds", "CIWS damage ×3: armoured missiles go down in far fewer hits")),
                i, f, i, f, Step.Ability(FlakRounds = new Unlock("Flak rounds", "Each CIWS kill destroys other enemy missiles within 25 m")),
                i, f, i, f, Step.Ability(Overdrive = new Unlock("Overdrive", "CIWS rate of fire ×2, on top of earlier ranks")),
                i, f, i, f, Step.Ability(FlakStorm = new Unlock("Flak storm", "CIWS bursts reach 75 m and destroy aircraft too")),
            },
        };
    }

    /// <summary>
    /// Spoof chance +40 points (approaching +60) and reload ÷4, the step from
    /// chaff to an active decoy such as Nulka, and the game's rule that
    /// forces a decoy to fail after three successes in a row lifted.
    /// Survival is stopping missiles, and a decoyed missile is stopped.
    /// </summary>
    public static readonly Upgrade Countermeasures = MakeCountermeasures();

    static Upgrade MakeCountermeasures()
    {
        var p = Step.Chance(Stat.Spoof, 0.25f, 0.6f, "Chaff spoof chance +{0} points");
        var r = new Step(Stat.Reload, 0.8203f, "Chaff launchers reload {0}× as fast");
        var rule = new Step(Stat.DecoyRule, 1f, "Decoys can succeed every time: no more forced failure after three in a row");
        return new Upgrade
        {
            Name = "Countermeasures", Short = "Decoys", Icon = "chaff", Kind = Kind.Weapon, Systems = s => s._obp._chaffWeapons,
            Path = new[]
            {
                p, r, p, r, Step.Ability(QuickBloom = new Unlock("Quick bloom", "Missiles test against chaff every 5 s instead of every 35 s")),
                p, r, p, r, Step.Ability(BurnOut = new Unlock("Burn-out", "A spoofed missile is destroyed, and pays intercept XP")),
                p, r, p, rule, Step.Ability(Turncoat = new Unlock("Turncoat", "A spoofed missile turns round and homes on the nearest enemy ship")),
                p, r, p, r, Step.Ability(Overcharge = new Unlock("Overcharge", "Chaff reload ×4 again, and every launch fires two clouds")),
            },
        };
    }

    /// <summary>
    /// Three refits, air resupply every minute and every weapon an aircraft
    /// fires ×4 over the path, on top of what the anti-ship, SAM and gun lines
    /// give the same rounds. Refits take strike flights from unguided bombs to
    /// guided ones to Harpoons or AS-4s (see <see cref="Refits"/>). A flight
    /// with no carrier is useless once its pylons are empty, so the rest of
    /// the line keeps them full. Offered for chest and starting flights only:
    /// a Kiev's own aircraft rearm on its deck, and no refit reaches them,
    /// though the line's damage does.
    /// Under the Argentine ensign flights keep their iron bombs, so the
    /// refits give way to the bombs and the airframes that carry them.
    /// </summary>
    public static readonly Upgrade AirWing = MakeAirWing();

    static Upgrade MakeAirWing()
    {
        var d = new Step(Stat.Damage, 1.1892f, "Every weapon your aircraft fire hits {0}× as hard");
        var r = new Step(Stat.Reload, 0.8409f, "Air resupply comes {0}× as often");
        var resupply = new Step(Stat.Resupply, 1f, "Flights rearm in the air every 4 minutes, not only at level-ups", "Air resupply");

        var bombs = Step.Blend(
            new Step(Stat.Damage, 1.1892f, "Bombs and rockets hit {0}× as hard"),
            new Step(Stat.Accuracy, 0.8691f, "Bombs land {0}× as close"));
        var agile = Step.Blend(r, new Step(Stat.Turn, 1.1f, "Strike aircraft turn {0}× as hard"));
        var decoys = Step.Blend(r, Step.Chance(Stat.Spoof, 0.25f, 0.6f, "Aircraft chaff spoof chance +{0} points"));
        Step[] ironBombs =
        {
            bombs, bombs, bombs, bombs, new Step(Stat.Speed, 1.25f, "Strike aircraft fly 1.25× as fast", "Uprated engines"),
            bombs, bombs, bombs, bombs, resupply,
            agile, agile, agile, agile, new Step(Stat.Rcs, 0.5f, "Strike aircraft have half the radar signature", "Radar-absorbent paint"),
            decoys, decoys, decoys, decoys, new Step(Stat.Speed, 1.2f, "Strike aircraft fly 1.2× as fast again", "Stripped airframe"),
        };

        return new Upgrade
        {
            Name = "Air wing", Short = "Air wing", Icon = "air", Kind = Kind.Weapon,
            Systems = s => Run.Current != null && Run.Current.AirWing.ContainsKey(s) ? Boosts.Hardpoints(s, _ => true) : Enumerable.Empty<WeaponSystemHardpoint>(),
            Variant = () => Ensigns.Active(Ensigns.IronBombs) ? ironBombs : null,
            Path = new[]
            {
                d, d, d, d, new Step(Stat.Refit, 1f, "A-7Es stay, with Walleye guided bombs; Yak-38s become Su-24As with AS-14s", "Guided weapons refit"),
                d, d, d, d, resupply,
                r, r, r, r, new Step(Stat.Refit, 1f, "A-7Es become A-6Es with two Harpoons; Su-24As become Su-24Ms with AS-14s and KAB-500Ls", "New aircraft refit"),
                r, r, r, r, new Step(Stat.Refit, 1f, "A-6Es and Hornets carry four Harpoons; Soviet flights become Tu-22M2s with three AS-4s", "Heavy loadout refit"),
            },
        };
    }

    /// <summary>
    /// Crews react ×5.4 as fast over the path: contacts, alignment,
    /// engagement checks, acquisition and fire-control solutions, as
    /// automated fire control cut reaction from tens of seconds to a few.
    /// </summary>
    public static readonly Upgrade Reaction = MakeReaction();

    static Upgrade MakeReaction()
    {
        var q = new Step(Stat.Reaction, 0.8691f, "Crews react {0}× as fast: spotting, aiming and opening fire, across the fleet");
        return new Upgrade
        {
            Name = "Reaction", Short = "Reaction", Icon = "react", Kind = Kind.Fleet, Systems = _ => Enumerable.Empty<WeaponSystem>(),
            Path = new[]
            {
                q, q, q, q, Step.Ability(SnapFire = new Unlock("Snap fire", "Incoming missiles are engaged at once, not ignored for their first 5 s")),
                q, q, q, q, Step.Ability(FullPlot = new Unlock("Full plot", "No limit on new contacts processed at once")),
                q, q, q, q, Step.Ability(RippleFire = new Unlock("Ripple fire", "Launchers skip the random engage delay and the gap between launches")),
            },
        };
    }

    /// <summary>Gun and mortar aiming error ÷5.4 over the path: fire-control error, and for guns the lead error against aircraft.</summary>
    public static readonly Upgrade Accuracy = MakeAccuracy();

    static Upgrade MakeAccuracy()
    {
        var a = new Step(Stat.Accuracy, 0.8691f, "Gun and mortar aiming error ÷{0}, across the fleet");
        return new Upgrade
        {
            Name = "Accuracy", Short = "Accuracy", Icon = "acc", Kind = Kind.Fleet, Systems = _ => Enumerable.Empty<WeaponSystem>(),
            Path = new[]
            {
                a, a, a, a, Step.Ability(LongShots = new Unlock("Long shots", "Gun range ×1.5")),
                a, a, a, a, Step.Ability(CriticalHits = new Unlock("Critical hits", "1 shell hit in 5 deals ×5 damage")),
                a, a, a, a, Step.Ability(SmartShells = new Unlock("Smart shells", "Shells ride the radar beam: they steer onto a target your fire-control radar is tracking, in the last seconds of flight")),
            },
        };
    }

    // Assigned as the lines above are built, so abilities can be asked for by name.
    public static Unlock HeavyShells;
    public static Unlock SeaSkimmerKillers, Hypersonic, LongReach, HugeBlasts;
    public static Unlock SmartSeekers, WaveTop, Armoured, Ghost;
    public static Unlock Hunter, SureShot, KeelBreaker, Supercavitating;
    public static Unlock HuntingRounds, Spotters, DeepMagazines, Bombardment;
    public static Unlock HeavyRounds, FlakRounds, Overdrive, FlakStorm;
    public static Unlock QuickBloom, BurnOut, Turncoat, Overcharge;
    public static Unlock SnapFire, FullPlot, RippleFire;
    public static Unlock LongShots, CriticalHits, SmartShells;
    public static Unlock HighPower, SecondLook, HardKill, ExtremeRange;

    public static readonly Upgrade[] All = { Guns, Sams, AntiShip, Torpedoes, Jammers, Mortars, PointDefence, Countermeasures, AirWing, Reaction, Accuracy };

    /// <summary>A run holds at most this many weapon upgrades; fleet upgrades do not count.</summary>
    public const int WeaponCap = 4;

    /// <summary>
    /// Upgrades that could be offered now: in the fleet's armament, not
    /// maxed, not banished, and within the cap. Once four weapon upgrades
    /// are held, only those four can rank up.
    /// </summary>
    public static List<Upgrade> Eligible(Run run)
    {
        bool full = All.Count(u => u.Kind == Kind.Weapon && run.RankOf(u) > 0) >= WeaponCap;
        return All.Where(u => run.RankOf(u) < u.MaxRank && u.Fits(run.Fleet) && !run.Banished.Contains(u) && !Ensigns.Bars(u) &&
                              !(full && u.Kind == Kind.Weapon && run.RankOf(u) == 0)).ToList();
    }

    /// <summary>Chances, which stack hyperbolically towards their cap.</summary>
    static bool Chance(Stat stat) => stat == Stat.KillChance || stat == Stat.Intercept || stat == Stat.Spoof || stat == Stat.Evasion;
    /// <summary>Counts, which add.</summary>
    static bool Additive(Stat stat) => stat == Stat.Channels || stat == Stat.Reach || stat == Stat.ShellPower;
    /// <summary>Factors below one, where smaller is better: shown as their inverse.</summary>
    static bool Shrinks(Stat stat) => stat == Stat.Reload || stat == Stat.Rcs || stat == Stat.Reaction || stat == Stat.Accuracy || stat == Stat.GunAim;
    /// <summary>Fractions shown as points or percent.</summary>
    static bool Fraction(Stat stat) => stat == Stat.KillChance || stat == Stat.Spoof || stat == Stat.Evasion;

    /// <summary>The ranks a card of this rarity would give: fewer near the end of the line.</summary>
    public static int RanksFor(Run run, Upgrade line, Rarity rarity) =>
        Mathf.Min(Rarities.Ranks(rarity), line.MaxRank - run.RankOf(line));

    /// <summary>The combined effect on one stat of a line's first <paramref name="ranks"/> ranks.</summary>
    public static float TotalAt(Upgrade line, Stat stat, int ranks)
    {
        int n = 0;
        float product = 1f, sum = 0f;
        Step any = null;
        for (int i = 0; i < ranks && i < line.Path.Length; i++)
        {
            foreach (var step in line.Path[i].Effects)
            {
                if (step.Stat != stat) continue;
                any = step;
                n++;
                product *= step.Value;
                sum += step.Value;
            }
        }
        if (Chance(stat)) return any == null ? 0f : any.Cap * (1f - 1f / (1f + any.Value * n));
        return Additive(stat) ? sum : product;
    }

    /// <summary>
    /// The combined effect on one stat of every rank taken on a line, or of
    /// those and <paramref name="ahead"/> more, for a card's before and after.
    /// </summary>
    public static float Total(Run run, Upgrade line, Stat stat, int ahead = 0) => TotalAt(line, stat, run.RankOf(line) + ahead);

    /// <summary>What ranks [from, to) add to a stat, the way a player reads it: a factor, or points.</summary>
    static string Gain(Upgrade line, Stat stat, int from, int to)
    {
        float before = TotalAt(line, stat, from), after = TotalAt(line, stat, to);
        float shown = Chance(stat) || Additive(stat) ? after - before : after / before;
        if (Shrinks(stat)) shown = 1f / shown;
        if (Fraction(stat)) shown *= 100f;
        return shown.ToString(Chance(stat) ? "0" : "0.##", CultureInfo.InvariantCulture);
    }

    static string Text(Step step, string value) => step.IsAbility ? step.Text : string.Format(CultureInfo.InvariantCulture, step.Text, value);

    /// <summary>What one rank does, by its index on the path (rank 1 is index 0).</summary>
    public static string Describe(Upgrade line, int index)
    {
        var step = line.Path[index];
        return step.IsAbility ? $"{step.Name}: {step.Text}"
            : string.Join(", ", step.Effects.Select(e => Text(e, Gain(line, e.Stat, index, index + 1))));
    }

    /// <summary>
    /// What the next <paramref name="count"/> ranks of a line do: one sentence per
    /// stat they raise, with the ranks' gains combined, and each ability's name and text.
    /// </summary>
    public static (List<(Stat stat, string text)> stats, List<(string name, string text)> abilities) CardText(Run run, Upgrade line, int count)
    {
        int from = run.RankOf(line), to = Mathf.Min(line.MaxRank, from + count);
        var stats = new List<(Stat, string)>();
        var abilities = new List<(string, string)>();
        var seen = new HashSet<Stat>();
        // Under the St Andrew's flag every rank grows the pulse, and warheads are gone.
        bool emp = line == Mortars && Ensigns.Active(Ensigns.EmpMortars);
        if (emp) stats.Add((Stat.Ability, $"EMP pulse reaches {Ensigns.EmpNm(to):0.#} nm and lasts {Ensigns.EmpSeconds(to):0} s"));
        for (int i = from; i < to; i++)
        {
            var step = line.Path[i];
            if (step.IsAbility) abilities.Add((step.Name, step.Text));
            else
                foreach (var effect in step.Effects)
                {
                    if (emp && effect.Stat == Stat.Damage) continue;
                    if (seen.Add(effect.Stat)) stats.Add((effect.Stat, Text(effect, Gain(line, effect.Stat, from, to))));
                }
        }
        return (stats, abilities);
    }

    /// <summary>The stats ranks [from, to) of a line touch, in path order, the switches among them; not pure abilities.</summary>
    public static IEnumerable<Stat> StatsIn(Upgrade line, int from, int to) =>
        line.Path.Skip(from).Take(to - from).SelectMany(s => s.Effects).Where(s => s.Stat != Stat.Ability).Select(s => s.Stat).Distinct();

    /// <summary>The first ability at or after <paramref name="from"/> (an index), and how many ranks away it is; null past the last.</summary>
    public static (Step step, int ranks)? NextAbility(Upgrade line, int from)
    {
        for (int i = from; i < line.Path.Length; i++)
            if (line.Path[i].IsAbility) return (line.Path[i], i + 1 - from);
        return null;
    }

    /// <summary>A line's abilities already taken.</summary>
    public static IEnumerable<Step> Earned(Run run, Upgrade line) => line.Path.Take(run.RankOf(line)).Where(s => s.IsAbility);

    /// <summary>What a line's taken ranks add up to, a line per stat: the HUD's "in effect".</summary>
    public static List<string> Summary(Run run, Upgrade line)
    {
        int rank = run.RankOf(line);
        var lines = new List<string>();
        foreach (var stat in StatsIn(line, 0, rank))
        {
            float total = TotalAt(line, stat, rank);
            string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            string text = stat switch
            {
                Stat.Damage => $"Damage ×{F(total)}",
                Stat.RateOfFire => $"Rate of fire ×{F(total)}",
                Stat.Reload => line == AirWing ? $"Resupply ×{F(1f / total)} as often" : $"Reload ×{F(1f / total)} as fast",
                Stat.KillChance => $"Hit chance +{total * 100f:0} points",
                Stat.Speed => $"Speed ×{F(total)}",
                Stat.Range => $"Range ×{F(total)}",
                Stat.Arc => $"Arc ×{F(total)}",
                Stat.JamChance => $"Jam chance ×{F(total)}",
                Stat.Intercept => $"Intercept chance +{total:0} points",
                Stat.Channels => $"+{total:0} targets per director",
                Stat.Spoof => $"Spoof chance +{total * 100f:0} points",
                Stat.Evasion => $"Enemy SAMs hit {total * 100f:0}% less often",
                Stat.Seeker => $"Seeker range and arc ×{F(total)}",
                Stat.Reach => $"Reach +{total:0} nm",
                Stat.Reaction => $"Crews react ×{F(1f / total)} as fast",
                Stat.Accuracy => line == AirWing ? $"Bomb scatter ÷{F(1f / total)}" : $"Aiming error ÷{F(1f / total)}",
                Stat.Turn => $"Turn rate ×{F(total)}",
                Stat.Rcs => $"Radar signature ÷{F(1f / total)}",
                Stat.GunAim => $"Gun aiming error ÷{F(1f / total)}",
                Stat.ShellPower => $"Shell power +{total:0}",
                Stat.DecoyRule => "Decoys are never forced to fail",
                _ => null,
            };
            if (text != null) lines.Add(text);
        }
        return lines;
    }

    public static bool Has(Run run, Unlock unlock) =>
        All.Any(line => line.Path.Take(run.RankOf(line)).Any(s => s.Unlock == unlock));

    /// <summary>True once a rank with this stat has been taken: for steps that switch something on.</summary>
    public static bool Has(Run run, Upgrade line, Stat stat) =>
        line.Path.Take(run.RankOf(line)).Any(s => s.Effects.Any(e => e.Stat == stat));
}
