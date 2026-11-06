using System.Collections.Generic;
using System.Linq;

namespace SeapowerSurvivors;

/// <summary>
/// A naval ensign flown for a whole run: one real flag, one boost, one drawback.
/// After Soulstone Survivors' runes: unlocked between runs, chosen before one,
/// and in force from the first second to the last.
/// </summary>
sealed class Ensign
{
    public readonly string Id, Name, Navy, Boon, Cost;
    /// <summary>What it takes from the bank to unlock, once, for good.</summary>
    public readonly int Price;

    public Ensign(string id, string name, string navy, int price, string boon, string cost)
    {
        Id = id;
        Name = name;
        Navy = navy;
        Price = price;
        Boon = boon;
        Cost = cost;
    }
}

/// <summary>
/// Every ensign and its numbers. What each one does is folded into the systems
/// it touches, through the small questions at the foot of this class: Boosts
/// asks for its factors, the level-up screen for its cards and so on, while
/// EnsignCombat has the effects that act during the fight.
/// </summary>
static class Ensigns
{
    /// <summary>A run flies at most this many.</summary>
    public const int MaxHoisted = 3;

    public static readonly Ensign HeavyGuns = new Ensign("heavy-guns", "Stars and Stripes", "United States Navy", 400,
        "Guns do 150% more damage", "Guns fire 50% slower");
    public static readonly Ensign HeavyMissiles = new Ensign("heavy-missiles", "Soviet Naval Ensign", "Soviet Navy", 500,
        "All missiles do 2× damage", "All missiles fly 30% slower");
    public static readonly Ensign LongCiws = new Ensign("long-ciws", "Dutch Tricolour", "Royal Netherlands Navy", 900,
        "CIWS range ×100", "SAMs and air-to-air missiles are disabled");
    public static readonly Ensign EmpMortars = new Ensign("emp-mortars", "St Andrew's Flag", "Russian Navy", 700,
        "Mortar rounds are EMPs: radars and fire control within 2 nm go dark for 60 s", "Mortars do no damage. Soviet hulls only");
    public static readonly Ensign DoubleAir = new Ensign("double-air", "Australian White Ensign", "Royal Australian Navy", 600,
        "Starting flights come double, or a free fighter pair if you have none", "Enemy aircraft come in double");
    public static readonly Ensign GasShells = new Ensign("gas-shells", "Chilean Ensign", "Chilean Navy", 1000,
        "Guns reach 3× as far. Shells leave a corrosive cloud for 90 s that damages ships, and clouds stack", "Shells do no direct damage to ships");
    public static readonly Ensign FiveOfAKind = new Ensign("five-of-a-kind", "Greek Ensign", "Hellenic Navy", 800,
        "Your first ship or flight, from the start or a chest, comes as five", "No other escorts: the rest stay ashore, and chests hold only level-ups");
    public static readonly Ensign RareCards = new Ensign("rare-cards", "Norwegian Ensign", "Royal Norwegian Navy", 700,
        "Cards are at least Rare", "Enemy missiles fly 2× as fast and reach 1.5× as far");
    public static readonly Ensign LongRange = new Ensign("long-range", "White Ensign", "Royal Navy", 800,
        "Guns, missiles, torpedoes and mortars reach 5× as far", "All of them do 25% less damage");
    public static readonly Ensign FastTorpedoes = new Ensign("fast-torpedoes", "Italian Naval Ensign", "Italian Navy", 400,
        "Torpedoes run and turn 2× as fast, reach 2× as far, and can target ships", "Torpedoes do 25% less damage");
    public static readonly Ensign SmallGuns = new Ensign("small-guns", "Polish Naval Ensign", "Polish Navy", 400,
        "Guns under 100 mm: aiming error ÷4", "Those guns do 25% less damage");
    public static readonly Ensign Unsinkable = new Ensign("unsinkable", "Brazilian Ensign", "Brazilian Navy", 1200,
        "Missiles do your fleet no harm. Ship speed and turn rate ×2", "Everything else hits your fleet 2× as hard");
    public static readonly Ensign GreenCrews = new Ensign("green-crews", "Spanish Ensign", "Spanish Navy", 500,
        "Start with four free cards", "Crews react and aim 1.5× as badly");
    public static readonly Ensign LastStand = new Ensign("last-stand", "Dannebrog", "Royal Danish Navy", 600,
        "Once the flagship has been hit, all your damage +200%", "Until then, all your damage −25%");
    public static readonly Ensign TorpedoEcm = new Ensign("torpedo-ecm", "German Naval Ensign", "German Navy", 800,
        "Ships with torpedo tubes carry the EA-6B's jammer instead, which you aim. Torpedo cards become jammer cards", "No torpedoes, ever");
    public static readonly Ensign SlowEnemies = new Ensign("slow-enemies", "Finnish Naval Ensign", "Finnish Navy", 600,
        "Enemy ships move at half speed", "Enemy ships do 2× damage");
    public static readonly Ensign TwoCards = new Ensign("two-cards", "Irish Tricolour", "Irish Naval Service", 500,
        "Cards are at least Rare", "Only two cards to choose from");
    public static readonly Ensign WireGuided = new Ensign("wire-guided", "Swedish Naval Ensign", "Swedish Navy", 400,
        "Torpedoes stay wire-guided for half their range, and can target ships", "Held to two-thirds speed while on the wire");
    public static readonly Ensign StealthAir = new Ensign("stealth-air", "Estonian Naval Ensign", "Estonian Navy", 400,
        "Your aircraft's radar signature −95%", "Your aircraft fly 50% slower");
    public static readonly Ensign IronBombs = new Ensign("iron-bombs", "Argentine Ensign", "Argentine Navy", 500,
        "Unguided bombs do 10× damage. Start with a free strike pair", "Flights never refit to guided weapons");

    public static readonly Ensign[] All =
    {
        LongRange, HeavyGuns, GasShells, SmallGuns,
        HeavyMissiles, LongCiws, EmpMortars, FastTorpedoes,
        WireGuided, TorpedoEcm, DoubleAir, StealthAir,
        IronBombs, FiveOfAKind, Unsinkable, LastStand,
        SlowEnemies, RareCards, TwoCards, GreenCrews,
    };

    /// <summary>Pairs that cannot fly together, because one takes away what the other acts on.</summary>
    static readonly (Ensign, Ensign)[] Clashes =
    {
        (TorpedoEcm, FastTorpedoes), (TorpedoEcm, WireGuided),
        (FiveOfAKind, DoubleAir), (FiveOfAKind, IronBombs),
    };

    internal static Ensign ById(string id) => All.FirstOrDefault(e => e.Id == id);

    /// <summary>The hoisted ensign this one cannot fly with, if any.</summary>
    internal static Ensign ClashWith(Ensign ensign, IEnumerable<Ensign> hoisted) =>
        hoisted.FirstOrDefault(other => Clashes.Any(c => c == (ensign, other) || c == (other, ensign)));

    internal static bool Active(Ensign ensign) => Run.Current != null && Run.Current.Hoisted.Contains(ensign);

    // The numbers, for whatever is writing them into the game.

    internal const float EmpNm = 2f, EmpSeconds = 60f;
    internal const float GasSeconds = 90f, GasNm = 0.5f;
    internal const float WireFraction = 0.5f, WireSpeedFraction = 2f / 3f;
    internal const int BonusCards = 4, EscortCopies = 5;
    /// <summary>A quarter of the usual error: about ten ranks of the Accuracy line.</summary>
    internal const float SmallGunAimFactor = 0.25f;
    const float SmallCalibreMm = 100f;

    internal static float GunRate => Active(HeavyGuns) ? 0.5f : 1f;
    internal static float GunReach => (Active(LongRange) ? 5f : 1f) * (Active(GasShells) ? 3f : 1f);
    internal static float MissileSpeed => Active(HeavyMissiles) ? 0.7f : 1f;
    internal static float MissileReach => Active(LongRange) ? 5f : 1f;
    internal static float TorpedoSpeed => Active(FastTorpedoes) ? 2f : 1f;
    internal static float TorpedoReach => (Active(FastTorpedoes) ? 2f : 1f) * (Active(LongRange) ? 5f : 1f);
    internal static float MortarReach => Active(LongRange) ? 5f : 1f;
    internal static float CrewFactor => Active(GreenCrews) ? 1.5f : 1f;
    internal static float CiwsReach => Active(LongCiws) ? 100f : 1f;
    internal static float FleetSpeed => Active(Unsinkable) ? 2f : 1f;
    internal static bool TorpedoesTargetShips => Active(FastTorpedoes) || Active(WireGuided);

    /// <summary>Upgrade lines with nothing left to act on.</summary>
    internal static bool Bars(Upgrade line) =>
        line == Upgrades.Sams && Active(LongCiws) ||
        line == Upgrades.Torpedoes && Active(TorpedoEcm) ||
        line == Upgrades.Jammers && !Active(TorpedoEcm);

    /// <summary>How many cards a level-up deals.</summary>
    internal static int CardCount(int usual) => Active(TwoCards) ? 2 : usual;

    /// <summary>The lowest rarity a card can roll: Rare with either ensign that says so, Epic with both.</summary>
    internal static Rarity RarityFloor => (Rarity)((Active(RareCards) ? 1 : 0) + (Active(TwoCards) ? 1 : 0));

    /// <summary>True once the only escort this run may have has been taken: chests then hold level-ups alone.</summary>
    internal static bool EscortsClosed(Run run) => run.Hoisted.Contains(FiveOfAKind) && run.EscortChosen;

    /// <summary>True for a round from a gun under 100 mm. Shells are named for their calibre: wp_cal_57mm, usn_cal_127mm, usn_cal_5in38.</summary>
    internal static bool SmallCalibre(SeaPower.AmmunitionParameters ap)
    {
        var name = ap?._ammunitionFileName;
        if (name == null || !name.Contains("_cal_")) return false;
        var match = System.Text.RegularExpressions.Regex.Match(name, @"_cal_(\d+)mm");
        if (match.Success) return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) < SmallCalibreMm;
        // The inch-named: .50 cal and 3-inch are small, 5-inch is not.
        return name.Contains("_cal_05in") || name.Contains("_cal_3in");
    }
}
