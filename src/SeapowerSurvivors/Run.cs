using System.Collections.Generic;
using System.Linq;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// One run's state: XP and level, the upgrade lines taken and their ranks,
/// the fleet they apply to, and what the build has unlocked. Null between runs.
/// </summary>
sealed class Run
{
    internal static Run Current;

    /// <summary>The flagship: the ship the run started with, or the escort it transferred to. Losing it ends the run.</summary>
    internal ObjectBase Ship;
    /// <summary>The type of the ship the run started in, which a win is counted for even after a flag transfer.</summary>
    internal readonly string FlagshipType;
    /// <summary>Every ship and aircraft the upgrades apply to; the starting ship first.</summary>
    internal readonly List<ObjectBase> Fleet = new List<ObjectBase>();
    /// <summary>The fleet's aircraft, with what each flight was chosen for.</summary>
    internal readonly Dictionary<ObjectBase, Role> AirWing = new Dictionary<ObjectBase, Role>();

    internal float Xp;
    internal float XpIntoLevel;
    internal int Level = 1;
    internal int PendingLevels;
    /// <summary>Levels gained but not yet rearmed for. Rearming waits for the main thread.</summary>
    internal int RearmsPending;
    /// <summary>Mission time of the last air resupply, from the Air wing line.</summary>
    internal double LastResupply = double.MinValue;
    internal int Rerolls;
    internal int Banishes;
    /// <summary>Upgrades banished from the cards for the rest of the run.</summary>
    internal readonly HashSet<Upgrade> Banished = new HashSet<Upgrade>();
    /// <summary>Times the flag can still pass to an escort when the flagship sinks.</summary>
    internal int FlagTransfers;
    /// <summary>XP earned is multiplied by this: the shop's Growth.</summary>
    internal float XpFactor = 1f;
    /// <summary>Seconds of run time so far, kept by the director; drives the early XP boost.</summary>
    internal float Elapsed;
    /// <summary>The shop's Luck, which makes rarer cards more likely.</summary>
    internal int Luck;
    /// <summary>Boss chests still to open, by the tier of ship or flight each offers.</summary>
    internal readonly Queue<int> ShipChests = new Queue<int>();
    internal int Kills;
    internal int Intercepts;
    /// <summary>True for a real run, whose XP goes to the profile's bank; false for one started from the test panel.</summary>
    internal bool Counts;
    internal bool Banked;
    /// <summary>The ensigns flown this run, fixed when it starts.</summary>
    internal readonly List<Ensign> Hoisted;
    /// <summary>The flagship has taken damage at some point this run: what the Dannebrog waits for.</summary>
    internal bool FlagshipHit;
    /// <summary>The Greek ensign's one escort has been taken.</summary>
    internal bool EscortChosen;

    /// <summary>Ranks taken on each upgrade, in the order the upgrades were first taken.</summary>
    readonly Dictionary<Upgrade, int> ranks = new Dictionary<Upgrade, int>();
    internal readonly HashSet<Capability> Has = new HashSet<Capability>();

    internal Run(ObjectBase ship)
    {
        Ship = ship;
        FlagshipType = ship._obp._objectIniName;
        Fleet.Add(ship);
        // What the shop's permanent upgrades give every run.
        var profile = Profile.Current;
        Rerolls = Balance.RerollsPerRun + profile.RankOf(Store.Rerolls);
        Banishes = profile.RankOf(Store.Banish);
        FlagTransfers = profile.RankOf(Store.FlagTransfer);
        XpFactor = 1f + Store.GrowthPerRank * profile.RankOf(Store.Growth);
        Luck = profile.RankOf(Store.Luck);
        Hoisted = profile.HoistedEnsigns();
        if (Hoisted.Contains(Ensigns.GreenCrews)) PendingLevels = Ensigns.BonusCards;
    }

    internal int RankOf(Upgrade upgrade) => ranks.TryGetValue(upgrade, out int rank) ? rank : 0;
    /// <summary>The weapon upgrades held, in the order they were first taken: what fills the HUD's slots.</summary>
    internal List<Upgrade> WeaponSlots() => ranks.Keys.Where(u => u.Kind == Kind.Weapon).ToList();
    /// <summary>Every upgrade held, in the order first taken.</summary>
    internal IEnumerable<Upgrade> Held => ranks.Keys;

    /// <summary>
    /// Called from combat hooks, mid-simulation, so it only counts: the rearm
    /// and the card screen happen on the next frame. Early in the run XP is
    /// boosted, so a slow-killing hull still levels; no one award is worth
    /// more than a couple of levels.
    /// </summary>
    internal void AddXp(float amount)
    {
        amount = System.Math.Min(amount * XpFactor * Progression.EarlyBoost(Elapsed),
                                 Progression.MaxLevelsPerAward * Progression.XpToNext(Level));
        Xp += amount;
        XpIntoLevel += amount;
        while (XpIntoLevel >= Progression.XpToNext(Level))
        {
            XpIntoLevel -= Progression.XpToNext(Level);
            Level++;
            PendingLevels++;
            RearmsPending++;
        }
    }

    /// <summary>Take a card: its rarity's ranks of the line, one after another along the path.</summary>
    internal void Take(Upgrade upgrade, Rarity rarity)
    {
        int from = RankOf(upgrade), to = from + Upgrades.RanksFor(this, upgrade, rarity);
        ranks[upgrade] = to;
        Boosts.ApplyFleet(this);
        for (int i = from; i < to; i++)
            TestPanel.Report($"{upgrade.Name} rank {i + 1} ({rarity}): {Upgrades.Describe(upgrade, i)}");
    }

    /// <summary>
    /// Every level-up refills every ship: magazines, launchers, canisters and
    /// guns, through the game's own rearm. Level-ups are the ammunition supply.
    /// Rearming makes fresh rounds, so the upgrades are written into them again.
    /// Aircraft pylons have no refill in the game, so they are reloaded apart.
    /// </summary>
    internal void RearmFleet()
    {
        foreach (var ship in Fleet)
            if (ship != null && !ship.IsDestroyed) ship.RearmAll();
        int stores = AirWing.Keys.Sum(Pylons.Reload);
        if (stores > 0) Entry.Log.LogInfo($"Rearm: {stores} store(s) hung on the air wing's pylons");
        Boosts.ApplyFleet(this);
    }
}

/// <summary>The tuning knobs that are not wave content.</summary>
static class Balance
{
    public const int RerollsPerRun = 3;

    /// <summary>Chance a boss chest holds three level-ups rather than one.</summary>
    public const float BigChestChance = 0.2f;
}
