using System.Collections.Generic;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// One run's state: XP and level, the upgrade lines taken and their ranks,
/// the fleet they apply to, and what the build has unlocked. Null between runs.
/// </summary>
sealed class Run
{
    internal static Run Current;

    /// <summary>The ship the run started with. Losing it ends the run.</summary>
    internal readonly ObjectBase Ship;
    /// <summary>Every ship the upgrades apply to; the starting ship first.</summary>
    internal readonly List<ObjectBase> Fleet = new List<ObjectBase>();

    internal float Xp;
    internal float XpIntoLevel;
    internal int Level = 1;
    internal int PendingLevels;
    /// <summary>Levels gained but not yet rearmed for. Rearming waits for the main thread.</summary>
    internal int RearmsPending;
    internal int Rerolls = Balance.RerollsPerRun;
    internal int Kills;
    internal int Intercepts;

    internal readonly Dictionary<Upgrade, int> Ranks = new Dictionary<Upgrade, int>();
    internal readonly HashSet<Capability> Has = new HashSet<Capability>();

    internal Run(ObjectBase ship)
    {
        Ship = ship;
        Fleet.Add(ship);
    }

    internal int RankOf(Upgrade upgrade) => Ranks.TryGetValue(upgrade, out int rank) ? rank : 0;

    /// <summary>
    /// Called from combat hooks, mid-simulation, so it only counts: the rearm
    /// and the card screen happen on the next frame.
    /// </summary>
    internal void AddXp(float amount)
    {
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

    internal void Take(Upgrade upgrade)
    {
        int rank = RankOf(upgrade) + 1;
        Ranks[upgrade] = rank;
        Boosts.ApplyFleet(this);
        TestPanel.Report($"{upgrade.Name} rank {rank}: {upgrade.Describe(rank)}");
    }

    /// <summary>
    /// Every level-up refills every ship: magazines, launchers, canisters and
    /// guns, through the game's own rearm. Level-ups are the ammunition supply.
    /// Rearming makes fresh rounds, so the upgrades are written into them again.
    /// </summary>
    internal void RearmFleet()
    {
        foreach (var ship in Fleet)
            if (ship != null && !ship.IsDestroyed) ship.RearmAll();
        Boosts.ApplyFleet(this);
    }

    /// <summary>
    /// What is left of the hidden integrity pool, the ship's real HP bar.
    /// Shown, never topped up: the ship survives by stopping missiles, not by
    /// absorbing them, so the hull stays as lethal as stock Sea Power.
    /// </summary>
    internal float HullFraction
    {
        get
        {
            var hull = (Ship as Vessel)?._compartments;
            return hull == null || hull._maxLimitIntegrity <= 0f ? 0f : hull._limitIntegrity / hull._maxLimitIntegrity;
        }
    }
}

/// <summary>The tuning knobs that are not wave content.</summary>
static class Balance
{
    public const int RerollsPerRun = 3;

    /// <summary>Chance a boss chest holds three level-ups rather than one.</summary>
    public const float BigChestChance = 0.2f;
}
