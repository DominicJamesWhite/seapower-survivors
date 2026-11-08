using System.Collections.Generic;
using System.Linq;

namespace SeapowerSurvivors;

/// <summary>Something the bank can buy, once, for good, or a flagship that only a win unlocks.</summary>
sealed class StoreItem
{
    public readonly string Id, Name;
    public readonly int Price;
    /// <summary>For a flagship that is won, not bought: the wins it takes. Null for anything for sale.</summary>
    public readonly string WonBy;

    public StoreItem(string id, string name, int price, string wonBy = null)
    {
        Id = id;
        Name = name;
        Price = price;
        WonBy = wonBy;
    }
}

/// <summary>The wins that unlock a flagship: one in any of these ships, or with <see cref="All"/>, one in each.</summary>
sealed class WinUnlock
{
    public readonly string[] Ships;
    public readonly bool All;

    public WinUnlock(bool all, params string[] ships)
    {
        All = all;
        Ships = ships;
    }
}

/// <summary>A permanent upgrade, bought rank by rank, that every run gets: Vampire Survivors' PowerUps.</summary>
sealed class PowerUp
{
    public readonly string Id, Name, PerRank;
    public readonly int MaxRank, BasePrice;

    public PowerUp(string id, string name, int maxRank, int basePrice, string perRank)
    {
        Id = id;
        Name = name;
        MaxRank = maxRank;
        BasePrice = basePrice;
        PerRank = perRank;
    }
}

/// <summary>
/// What banked XP buys. A run's starting fleet (the ships and flights that
/// sail with the flagship, and two flagships) is bought once at fixed
/// prices; the other flagships are free or won (see FlagshipWins).
/// Permanent upgrades are bought rank by rank at Vampire
/// Survivors' prices and can be refunded. Prices are starting points for
/// tuning: a run that reaches level 10 banks 90 XP, and one that reaches
/// level 20 about 550.
/// </summary>
static class Store
{
    const string Sverdlov = "wp_kr_sverdlov", Perry = "usn_ffg_oliver_hazard_perry", Kidd = "usn_ddg_kidd",
        Sovremenny = "wp_em_sovremenny", LongBeach = "usn_cgn_long_beach_83", Slava = "wp_rkr_slava",
        Iowa = "usn_bb_iowa", BunkerHill = "usn_cg_bunker_hill", Kirov = "wp_rkr_kirov", Kiev = "wp_takr_kiev";

    /// <summary>The flagships every profile starts with.</summary>
    static readonly HashSet<string> FreeFlagships = new HashSet<string> { Sverdlov, Perry };

    /// <summary>A step up from each free ship, for a player who cannot yet win in it.</summary>
    static readonly Dictionary<string, int> FlagshipPrices = new Dictionary<string, int>
    {
        [Kidd] = 400,
        [Sovremenny] = 400,
    };

    /// <summary>Every other flagship, and the wins that unlock it. The win is its price.</summary>
    static readonly Dictionary<string, WinUnlock> FlagshipWins = new Dictionary<string, WinUnlock>
    {
        [LongBeach] = new WinUnlock(false, Perry, Kidd),
        [Slava] = new WinUnlock(false, Sverdlov, Sovremenny),
        [Iowa] = new WinUnlock(false, Sverdlov),
        [BunkerHill] = new WinUnlock(false, LongBeach),
        [Kirov] = new WinUnlock(false, Slava, Iowa),
        [Kiev] = new WinUnlock(true, BunkerHill, Kirov),
    };

    /// <summary>Starting ships are the frigates and destroyers a tier 1 chest offers.</summary>
    const int EscortTier = 1;
    const int ShipPrice = 200, FlightPrice = 300;

    public static readonly PowerUp Rerolls = new PowerUp("rerolls", "Rerolls", 3, 150, "+1 reroll a run");
    public static readonly PowerUp Banish = new PowerUp("banish", "Banish", 3, 200, "+1 banish a run: an upgrade never offered again that run");
    public static readonly PowerUp Growth = new PowerUp("growth", "Growth", 5, 300, "+10% XP earned");
    public static readonly PowerUp Luck = new PowerUp("luck", "Luck", 5, 250, "Rarer upgrade cards turn up more often");
    public static readonly PowerUp FlagTransfer = new PowerUp("flag-transfer", "Flag transfer", 2, 2000, "When the flagship sinks, an escort ship takes the flag and the run goes on, once a run");
    public static readonly PowerUp[] PowerUps = { Rerolls, Banish, Growth, Luck, FlagTransfer };

    public const float GrowthPerRank = 0.1f;
    /// <summary>Every permanent-upgrade rank owned raises the price of the next by this fraction of its base price.</summary>
    const float InflationPerRank = 0.1f;

    /// <summary>
    /// Starting escorts are bought by slot, not by navy: each Soviet one
    /// stands in for a US one, so what is bought sails with every flagship
    /// as that navy's own. Ships pair by what they bring, flights by role.
    /// </summary>
    static readonly Dictionary<string, string> UsCounterparts = new Dictionary<string, string>
    {
        ["wp_skr_petya2"] = "usn_dd_gearing_fram2",
        ["wp_skr_krivak1"] = "usn_ff_knox_84",
        ["wp_bpk_kashin_mod"] = "usn_ffg_oliver_hazard_perry",
        ["wp_bpk_kanin"] = "usn_ddg_adams_early",
        ["wp_mig-23ms"] = "usn_f-4j",
        ["wp_yak-38"] = "usn_a-7e",
        ["wp_tu-95rt"] = "usn_e-2c",
    };

    internal static string FlagshipId(StartingShip ship) => "flagship:" + ship.Type;

    /// <summary>A starting escort's store id, the same for its counterpart in the other navy; the US one's, which older profiles hold.</summary>
    internal static string EscortId(Escort escort)
    {
        if (UsCounterparts.TryGetValue(escort.Type, out var us))
            escort = Escorts.NavyOf(us).First(e => e.Type == us && e.IsAir == escort.IsAir);
        return escort.IsAir ? "flight:" + escort.Type + ":" + escort.Loadout : "escort:" + escort.Type;
    }

    internal static string EnsignId(Ensign ensign) => "ensign:" + ensign.Id;

    internal static StoreItem EnsignItem(Ensign ensign) => new StoreItem(EnsignId(ensign), ensign.Name, ensign.Price);

    internal static bool IsFree(string id) => FreeFlagships.Any(type => id == "flagship:" + type);

    internal static StoreItem Flagship(StartingShip ship) =>
        new StoreItem(FlagshipId(ship), ship.Name, FlagshipPrices.TryGetValue(ship.Type, out int price) ? price : 0,
            FlagshipWins.TryGetValue(ship.Type, out var wins) ? WinsText(wins) : null);

    /// <summary>True once the profile has the wins that unlock this flagship.</summary>
    internal static bool IsWon(Profile profile, string id) =>
        FlagshipWins.Any(f => id == "flagship:" + f.Key &&
                              (f.Value.All ? f.Value.Ships.All(profile.HasWon) : f.Value.Ships.Any(profile.HasWon)));

    /// <summary>"Win in Perry or Kidd", "Win in both Bunker Hill and Kirov".</summary>
    static string WinsText(WinUnlock wins)
    {
        var names = wins.Ships.Select(type => Launcher.Flagships.First(s => s.Type == type).Name).ToList();
        if (names.Count == 1) return $"Win in {names[0]}";
        string list = string.Join(", ", names.Take(names.Count - 1)) + (wins.All ? " and " : " or ") + names[names.Count - 1];
        return wins.All ? $"Win in {(names.Count == 2 ? "both" : "each of")} {list}" : $"Win in {list}";
    }

    /// <summary>
    /// What a flagship's navy can start a run with, owned or not: the tier 1
    /// ships, a fighter pair, a strike pair and the AWACS.
    /// </summary>
    internal static IEnumerable<Escort> StartingEscorts(StartingShip flagship) =>
        Escorts.NavyOf(flagship.Type).Where(e => e.IsAir ? e.Tier == EscortTier || e.Roles == Role.Awacs : e.Tier == EscortTier);

    internal static StoreItem EscortItem(Escort escort) => new StoreItem(EscortId(escort), escort.Name, escort.IsAir ? FlightPrice : ShipPrice);

    /// <summary>Every starting ship and flight bought sails with the flagship, as its own navy's counterpart.</summary>
    internal static List<Escort> OwnedEscorts(Profile profile, StartingShip flagship) =>
        StartingEscorts(flagship).Where(e => profile.Owns(EscortId(e))).ToList();

    /// <summary>
    /// Vampire Survivors' rule: base × the rank being bought, plus 10% of
    /// the base for every permanent-upgrade rank already owned.
    /// </summary>
    internal static int Price(Profile profile, PowerUp powerUp)
    {
        int owned = PowerUps.Sum(profile.RankOf);
        return UnityEngine.Mathf.RoundToInt(powerUp.BasePrice * (profile.RankOf(powerUp) + 1 + InflationPerRank * owned));
    }
}
