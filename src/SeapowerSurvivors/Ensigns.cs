using System.Collections.Generic;
using System.Linq;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// A naval ensign flown for a whole run: one real flag and one effect, with no
/// penalty attached. After Soulstone Survivors' runes: unlocked between runs,
/// chosen before one, and limited by a budget of points rather than by a drawback.
/// </summary>
sealed class Ensign
{
    public readonly string Id, Name, Navy, Boon;
    /// <summary>What it takes from the bank to unlock, once, for good.</summary>
    public readonly int Price;
    /// <summary>What it takes from the budget of <see cref="Ensigns.Budget"/> to fly.</summary>
    public readonly int Points;

    public Ensign(string id, string name, string navy, int price, int points, string boon)
    {
        Id = id;
        Name = name;
        Navy = navy;
        Price = price;
        Points = points;
        Boon = boon;
    }
}

/// <summary>
/// Every ensign and its numbers. What each one does is folded into the systems
/// it touches, through the small questions at the foot of this class: Boosts
/// asks for its factors, the level-up screen for its cards and so on, while
/// EnsignCombat has the effects that act during the fight.
/// </summary>
/// <remarks>
/// Ids are what profiles hold. An ensign that replaced an older one keeps that
/// one's id (gas-shells, small-guns, unsinkable, slow-enemies, two-cards), so a
/// flag already unlocked stays paid for.
/// </remarks>
static class Ensigns
{
    /// <summary>The points a run has to fly ensigns with.</summary>
    public const int Budget = 20;

    public static readonly Ensign HeavyGuns = new Ensign("heavy-guns", "Stars and Stripes", "United States Navy", 400, 5,
        "Guns do 4x more damage, but rate of fire is cut by 50%");
    public static readonly Ensign HeavyMissiles = new Ensign("heavy-missiles", "Soviet Naval Ensign", "Soviet Navy", 500, 7,
        "All SAMs, ASMs and AAMs do 2× damage");
    public static readonly Ensign LongCiws = new Ensign("long-ciws", "Dutch Tricolour", "Royal Netherlands Navy", 900, 8,
        "CIWS range ×10. Guns aim 4× as well, and fire and reload 2× as fast. All missiles are disabled");
    public static readonly Ensign EmpMortars = new Ensign("emp-mortars", "St Andrew's Flag", "Russian Navy", 700, 5,
        "Mortar rounds are EMPs instead of warheads: radars and fire control go dark. Each mortar rank widens the pulse and makes it last longer. Sovremenny, Slava, Kirov and Kiev only");
    public static readonly Ensign DoubleAir = new Ensign("double-air", "Australian White Ensign", "Royal Australian Navy", 600, 4,
        "Double the number of aircraft you get per card or escort.");
    public static readonly Ensign FastAsms = new Ensign("gas-shells", "Indian Naval Ensign", "Indian Navy", 1000, 5,
        "Your ASMs move twice as fast.");
    public static readonly Ensign FiveOfAKind = new Ensign("five-of-a-kind", "Greek Ensign", "Hellenic Navy", 800, 7,
        "You can only select one type of escort, but you get 5 of them.");
    public static readonly Ensign RareCards = new Ensign("rare-cards", "Norwegian Ensign", "Royal Norwegian Navy", 700, 10,
        "Cards are at least Rare");
    public static readonly Ensign LongRange = new Ensign("long-range", "White Ensign", "Royal Navy", 800, 8,
        "Guns, missiles, torpedoes and mortars reach 3× further");
    public static readonly Ensign FastTorpedoes = new Ensign("fast-torpedoes", "Italian Naval Ensign", "Italian Navy", 400, 5,
        "Torpedoes run and turn 3× as fast, reach 3× as far, hit 1.5× as hard, and can target ships");
    public static readonly Ensign PointBlank = new Ensign("small-guns", "Icelandic Coast Guard Ensign", "Icelandic Coast Guard", 400, 5,
        "Guns do up to 6× damage to ships as they get closer");
    public static readonly Ensign HitProof = new Ensign("unsinkable", "Turkish Naval Ensign", "Turkish Naval Forces", 1200, 7,
        "The flagship ignores one missile, bomb or torpedo, and one more every 5 levels");
    public static readonly Ensign FreeCards = new Ensign("green-crews", "Spanish Ensign", "Spanish Navy", 500, 4,
        "Start with four free cards");
    public static readonly Ensign LastStand = new Ensign("last-stand", "Dannebrog", "Royal Danish Navy", 600, 4,
        "Each time an enemy attack gets within 3nm of your fleet, your damage is increased by 20% for the rest of the run. This effect can only occur once per 10s.");
    public static readonly Ensign TorpedoEcm = new Ensign("torpedo-ecm", "German Naval Ensign", "German Navy", 800, 6,
        "All torpedo tubes are replaced by the EA-6B's ECM jammer. Any Torpedo bonuses are replaced by Jammer bonuses.");
    public static readonly Ensign ChestsTwice = new Ensign("slow-enemies", "New Zealand White Ensign", "Royal New Zealand Navy", 600, 6,
        "Ship and Aircraft escort cards awarded during a run provide you with 2 of the selected escort.");
    public static readonly Ensign MoreChoice = new Ensign("two-cards", "Irish Tricolour", "Irish Naval Service", 500, 5,
        "A fourth card at every level-up, 3 more rerolls and 3 more banishes");
    public static readonly Ensign WireGuided = new Ensign("wire-guided", "Swedish Naval Ensign", "Swedish Navy", 400, 3,
        "All torpedoes are wire-guided for half their range, and can target ships");
    public static readonly Ensign StealthAir = new Ensign("stealth-air", "Estonian Naval Ensign", "Estonian Navy", 400, 3,
        "Your aircraft's radar signatures are reduced by 95%");
    public static readonly Ensign IronBombs = new Ensign("iron-bombs", "Argentine Ensign", "Argentine Navy", 500, 6,
        "Unguided bombs do 10× damage and are 10x more accurate. Strike aircraft carry only those, with a quarter of the radar signature. Start with a free strike pair");

    public static readonly Ensign[] All =
    {
        LongRange, HeavyGuns, PointBlank, HeavyMissiles,
        FastAsms, LongCiws, EmpMortars, FastTorpedoes,
        WireGuided, TorpedoEcm, DoubleAir, StealthAir,
        IronBombs, FiveOfAKind, ChestsTwice, HitProof,
        LastStand, RareCards, MoreChoice, FreeCards,
    };

    /// <summary>Pairs that cannot fly together, because one takes away what the other acts on.</summary>
    static readonly (Ensign, Ensign)[] Clashes =
    {
        (TorpedoEcm, FastTorpedoes), (TorpedoEcm, WireGuided),
        (FiveOfAKind, DoubleAir), (FiveOfAKind, IronBombs), (FiveOfAKind, ChestsTwice),
        (LongCiws, DoubleAir), (LongCiws, HeavyMissiles), (LongCiws, FastAsms),
    };

    /// <summary>The flagships with rocket mortars, for the one ensign that needs them.</summary>
    static readonly string[] MortarFlagships = { "wp_em_sovremenny", "wp_rkr_slava", "wp_rkr_kirov", "wp_takr_kiev" };

    internal static Ensign ById(string id) => All.FirstOrDefault(e => e.Id == id);

    /// <summary>The hoisted ensign this one cannot fly with, if any.</summary>
    internal static Ensign ClashWith(Ensign ensign, IEnumerable<Ensign> hoisted) =>
        hoisted.FirstOrDefault(other => Clashes.Any(c => c == (ensign, other) || c == (other, ensign)));

    /// <summary>False for an ensign that would do nothing on this flagship.</summary>
    internal static bool Fits(Ensign ensign, string flagshipType) =>
        ensign != EmpMortars || MortarFlagships.Contains(flagshipType);

    internal static int PointsFlown(IEnumerable<Ensign> hoisted) => hoisted.Sum(e => e.Points);

    internal static bool Active(Ensign ensign) => Run.Current != null && Run.Current.Hoisted.Contains(ensign);

    // The numbers, for whatever is writing them into the game.

    internal const float WireFraction = 0.5f;
    internal const int BonusCards = 4, EscortCopies = 5, ChestCopies = 2, AirCopies = 2, ExtraRerolls = 3, ExtraBanishes = 3;
    /// <summary>The Dannebrog: how near enemy fire must come to a ship of ours, how often that can count, and what each time adds to our damage.</summary>
    internal const float CloseCallNm = 3f, CloseCallSeconds = 10f;
    internal const int CloseCallPercent = 20;
    /// <summary>The flagship gets another ignored hit each time its level reaches a multiple of this.</summary>
    internal const int LevelsPerIgnoredHit = 5;
    /// <summary>A quarter of the usual error: about ten ranks of the Accuracy line.</summary>
    internal const float DutchAimFactor = 0.25f;
    /// <summary>Applied to each unguided bomb's own scatter, and each strike aircraft's aim and moving-target penalty.</summary>
    internal const float IronBombScatter = 0.1f;
    internal const float StrikeRcsFactor = 0.25f;

    const float EmpBaseNm = 2f, EmpNmPerRank = 0.2f, EmpBaseSeconds = 60f, EmpSecondsPerRank = 6f;

    /// <summary>How far a pulse reaches at this many ranks of the Mortars line: 2 nm, up to 6 at the last rank.</summary>
    internal static float EmpNm(int rank) => EmpBaseNm + EmpNmPerRank * rank;
    /// <summary>How long a pulse lasts at this many ranks: 60 s, up to 3 minutes at the last rank.</summary>
    internal static float EmpSeconds(int rank) => EmpBaseSeconds + EmpSecondsPerRank * rank;

    internal static float GunRate => (Active(HeavyGuns) ? 0.5f : 1f) * (Active(LongCiws) ? 2f : 1f);
    internal static float GunReach => Active(LongRange) ? 3f : 1f;
    internal static float MissileReach => Active(LongRange) ? 3f : 1f;
    internal static float AsmSpeed => Active(FastAsms) ? 2f : 1f;
    internal static float TorpedoSpeed => Active(FastTorpedoes) ? 3f : 1f;
    internal static float TorpedoReach => (Active(FastTorpedoes) ? 3f : 1f) * (Active(LongRange) ? 3f : 1f);
    internal static float MortarReach => Active(LongRange) ? 3f : 1f;
    internal static float CiwsReach => Active(LongCiws) ? 10f : 1f;
    internal static bool TorpedoesTargetShips => Active(FastTorpedoes) || Active(WireGuided);

    /// <summary>Upgrade lines with nothing left to act on.</summary>
    internal static bool Bars(Upgrade line) =>
        (line == Upgrades.Sams || line == Upgrades.AntiShip) && Active(LongCiws) ||
        line == Upgrades.Torpedoes && Active(TorpedoEcm) ||
        line == Upgrades.Jammers && !Active(TorpedoEcm);

    /// <summary>The Dutch ensign: a round no one in the fleet may fire. Rocket-thrown torpedoes, mortars, rockets and bombs are not missiles to the game.</summary>
    internal static bool Withheld(AmmunitionParameters ap) => Active(LongCiws) && ap._type == Ammunition.Type.Missile;

    /// <summary>The roles an escort can actually fill under the ensigns flown.</summary>
    internal static Role Usable(Role roles) => Active(LongCiws) ? roles & ~(Role.Sams | Role.AntiShip | Role.AirCover) : roles;

    /// <summary>What the Australian ensign multiplies a flight by, wherever it joins from. Ships are not touched.</summary>
    internal static int AircraftFactor(IEnumerable<Ensign> hoisted, Escort escort) =>
        escort.IsAir && hoisted.Contains(DoubleAir) ? AirCopies : 1;

    /// <summary>How many times over a chest's escort joins: the Greek ensign's first five, or the New Zealand and Australian factors together.</summary>
    internal static int ChestMultiple(Run run, Escort escort)
    {
        if (run.Hoisted.Contains(FiveOfAKind) && !run.EscortChosen) return EscortCopies;
        return (run.Hoisted.Contains(ChestsTwice) ? ChestCopies : 1) * AircraftFactor(run.Hoisted, escort);
    }

    /// <summary>A multiple as a name shows it, or nothing for one of them.</summary>
    internal static string Times(int multiple) => multiple > 1 ? $" ×{multiple}" : "";

    /// <summary>How many cards a level-up deals.</summary>
    internal static int CardCount(int usual) => Active(MoreChoice) ? usual + 1 : usual;

    /// <summary>The lowest rarity a card can roll.</summary>
    internal static Rarity RarityFloor => Active(RareCards) ? Rarity.Rare : Rarity.Common;

    /// <summary>What an ensign has built up during the run: the count on its flag and the line in its panel, or null for one that builds nothing.</summary>
    internal static (string count, string line)? Tally(Run run, Ensign ensign)
    {
        if (ensign == HitProof) return (run.HitsToIgnore.ToString(), run.HitsToIgnore == 1 ? "1 left to ignore" : $"{run.HitsToIgnore} left to ignore");
        if (ensign == LastStand) return (run.CloseCalls.ToString(), $"Current bonus: +{run.CloseCalls * CloseCallPercent}% damage");
        return null;
    }

    /// <summary>True once the only escort this run may have has been taken: chests then hold level-ups alone.</summary>
    internal static bool EscortsClosed(Run run) => run.Hoisted.Contains(FiveOfAKind) && run.EscortChosen;
}
