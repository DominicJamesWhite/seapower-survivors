using System.Collections.Generic;
using System.Linq;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// Weapon lines are the genre's weapons: one per kind of armament in the
/// fleet, each with a fixed path of ranks. Crew lines are its passives.
/// Every boost is direct and stacks, so a line at its last rank is far beyond
/// anything real; the enemy answers with numbers, not quality.
/// </summary>
enum Kind { Weapon, Crew }

enum Stat { Damage, RateOfFire, Reload, KillChance, Speed, Range, Rcs, Intercept, Channels, Spoof, Fuze, Barrage, Prediction, Reaction, Gunnery }

/// <summary>One rank on a line: one or more effects. Factors multiply, except the stats Total adds.</summary>
sealed class Step
{
    public (Stat stat, float value)[] Effects;
    public string Text;

    public Step(string text, params (Stat stat, float value)[] effects)
    {
        Text = text;
        Effects = effects;
    }
}

sealed class Upgrade
{
    public string Name;
    public Kind Kind;
    public Step[] Path;
    /// <summary>The weapon systems this line would boost, by name, so a card can say what it touches.</summary>
    public System.Func<ObjectBase, IEnumerable<string>> Systems;

    public int MaxRank => Path.Length;
    public string Describe(int rank) => Path[rank - 1].Text;
    public bool Fits(IEnumerable<ObjectBase> fleet) => Kind == Kind.Crew || fleet.Any(s => Systems(s).Any());
}

static class Upgrades
{
    // Arcade, not simulation: three ranks of a line should already feel
    // overpowered, and the rest of the path takes it somewhere absurd. Every
    // rank changes the fight; most carry two effects.

    /// <summary>Rate of fire and damage ×12 by rank 3, ×384 by rank 8.</summary>
    public static readonly Upgrade Guns = new Upgrade
    {
        Name = "Guns", Kind = Kind.Weapon, Systems = s => Boosts.Guns(s).Select(g => g._vwp._systemName),
        Path = new[]
        {
            new Step("Rate of fire and shell damage tripled", (Stat.RateOfFire, 3f), (Stat.Damage, 3f)),
            new Step("Rate of fire and shell damage doubled", (Stat.RateOfFire, 2f), (Stat.Damage, 2f)),
            new Step("Rate of fire and shell damage doubled", (Stat.RateOfFire, 2f), (Stat.Damage, 2f)),
            new Step("Rate of fire and shell damage doubled", (Stat.RateOfFire, 2f), (Stat.Damage, 2f)),
            new Step("Rate of fire and shell damage doubled", (Stat.RateOfFire, 2f), (Stat.Damage, 2f)),
            new Step("Rate of fire and shell damage doubled", (Stat.RateOfFire, 2f), (Stat.Damage, 2f)),
            new Step("Rate of fire and shell damage doubled", (Stat.RateOfFire, 2f), (Stat.Damage, 2f)),
            new Step("Rate of fire and shell damage doubled", (Stat.RateOfFire, 2f), (Stat.Damage, 2f)),
        },
    };

    /// <summary>
    /// Guns against aircraft. A shell only scores if it bursts within its fuze
    /// radius of the airframe, and in game the misses come from the target
    /// moving while the shell flies, which no fire control removes, so the
    /// fuze radius is what grows: 5 m to 80 m by rank 3, 180 m by rank 6.
    /// </summary>
    public static readonly Upgrade Flak = new Upgrade
    {
        Name = "Flak", Kind = Kind.Weapon, Systems = s => Boosts.Guns(s).Select(g => g._vwp._systemName),
        Path = new[]
        {
            new Step("Proximity fuzes and AA fire-control tables on every gun; fuze radius ×4", (Stat.Fuze, 4f)),
            new Step("Barrage fire: guns built for surface targets also engage aircraft; fuze radius doubled", (Stat.Barrage, 1f), (Stat.Fuze, 2f)),
            new Step("AA prediction error quartered; fuze radius doubled", (Stat.Prediction, 0.25f), (Stat.Fuze, 2f)),
            new Step("Fuze radius +50%", (Stat.Fuze, 1.5f)),
            new Step("AA prediction error halved; fuze radius +25%", (Stat.Prediction, 0.5f), (Stat.Fuze, 1.25f)),
            new Step("Fuze radius +20%", (Stat.Fuze, 1.2f)),
        },
    };

    /// <summary>
    /// Reload ×0.06, kill probability +0.5 and two more guidance channels by
    /// rank 3. Channels are the SAM's rate of fire: a director with one
    /// channel engages one target, however fast it reloads.
    /// </summary>
    public static readonly Upgrade Sams = new Upgrade
    {
        Name = "Surface-to-air missiles", Kind = Kind.Weapon, Systems = s => Boosts.SamLaunchers(s).Select(l => l._vwp._systemName),
        Path = new[]
        {
            new Step("Launcher reload quartered; kill probability +20", (Stat.Reload, 0.25f), (Stat.KillChance, 0.2f)),
            new Step("+2 target and missile channels on every director", (Stat.Channels, 2f)),
            new Step("Launcher reload quartered; kill probability +30", (Stat.Reload, 0.25f), (Stat.KillChance, 0.3f)),
            new Step("+2 target and missile channels on every director", (Stat.Channels, 2f)),
            new Step("Launcher reload halved; kill probability +20", (Stat.Reload, 0.5f), (Stat.KillChance, 0.2f)),
            new Step("+2 target and missile channels on every director", (Stat.Channels, 2f)),
            new Step("Launcher reload halved", (Stat.Reload, 0.5f)),
            new Step("+2 target and missile channels on every director; kill probability +20", (Stat.Channels, 2f), (Stat.KillChance, 0.2f)),
        },
    };

    /// <summary>Damage ×9, speed ×2, range ×2 and half the radar cross-section by rank 3.</summary>
    public static readonly Upgrade AntiShip = new Upgrade
    {
        Name = "Anti-ship missiles", Kind = Kind.Weapon, Systems = s => Boosts.AntiShipLaunchers(s).Select(l => l._vwp._systemName),
        Path = new[]
        {
            new Step("Warhead damage tripled", (Stat.Damage, 3f)),
            new Step("Missile speed doubled; radar cross-section halved", (Stat.Speed, 2f), (Stat.Rcs, 0.5f)),
            new Step("Warhead damage tripled; range doubled", (Stat.Damage, 3f), (Stat.Range, 2f)),
            new Step("Radar cross-section halved again", (Stat.Rcs, 0.5f)),
            new Step("Missile speed +50%", (Stat.Speed, 1.5f)),
            new Step("Warhead damage doubled", (Stat.Damage, 2f)),
            new Step("Range +50%", (Stat.Range, 1.5f)),
            new Step("Warhead damage doubled; radar cross-section halved again", (Stat.Damage, 2f), (Stat.Rcs, 0.5f)),
        },
    };

    /// <summary>Intercept chance +80 and rate of fire ×8 by rank 3.</summary>
    public static readonly Upgrade PointDefence = new Upgrade
    {
        Name = "Point defence", Kind = Kind.Weapon, Systems = s => s._obp._ciwsWeapons.Select(c => c._vwp._systemName),
        Path = new[]
        {
            new Step("Missile intercept chance +30; rate of fire doubled", (Stat.Intercept, 30f), (Stat.RateOfFire, 2f)),
            new Step("Missile intercept chance +25; rate of fire doubled", (Stat.Intercept, 25f), (Stat.RateOfFire, 2f)),
            new Step("Missile intercept chance +25; rate of fire doubled", (Stat.Intercept, 25f), (Stat.RateOfFire, 2f)),
            new Step("Rate of fire +50%", (Stat.RateOfFire, 1.5f)),
            new Step("Rate of fire +50%", (Stat.RateOfFire, 1.5f)),
            new Step("Rate of fire doubled", (Stat.RateOfFire, 2f)),
        },
    };

    /// <summary>
    /// Chaff spoof chance +0.65 and reload ×0.125 by rank 3, and the game's
    /// rule that forces a decoy to fail after three successes in a row is
    /// relaxed each rank, then lifted. Survival is stopping missiles, and a
    /// decoyed missile is stopped.
    /// </summary>
    public static readonly Upgrade Countermeasures = new Upgrade
    {
        Name = "Countermeasures", Kind = Kind.Weapon, Systems = s => s._obp._chaffWeapons.Select(c => c._vwp._systemName),
        Path = new[]
        {
            new Step("Chaff spoof chance +25; reload halved", (Stat.Spoof, 0.25f), (Stat.Reload, 0.5f)),
            new Step("Chaff spoof chance +20; reload halved", (Stat.Spoof, 0.2f), (Stat.Reload, 0.5f)),
            new Step("Chaff spoof chance +20; reload halved", (Stat.Spoof, 0.2f), (Stat.Reload, 0.5f)),
            new Step("Chaff reload halved", (Stat.Reload, 0.5f)),
            new Step("Decoys never forced to fail", (Stat.Spoof, 0.1f)),
        },
    };

    /// <summary>Each rank quarters the OODA loop: contacts, alignment and engagement checks.</summary>
    public static readonly Upgrade Reaction = new Upgrade
    {
        Name = "Reaction", Kind = Kind.Crew, Systems = _ => Enumerable.Empty<string>(),
        Path = Enumerable.Repeat(new Step("Contacts processed and targets acquired four times as fast, across the fleet", (Stat.Reaction, 0.25f)), 5).ToArray(),
    };

    /// <summary>Each rank quarters fire-control error and makes solutions four times as fast.</summary>
    public static readonly Upgrade Gunnery = new Upgrade
    {
        Name = "Gunnery", Kind = Kind.Crew, Systems = _ => Enumerable.Empty<string>(),
        Path = Enumerable.Repeat(new Step("Fire-control error quartered, solutions four times as fast, across the fleet", (Stat.Gunnery, 0.25f)), 5).ToArray(),
    };

    public static readonly Upgrade[] All = { Guns, Flak, Sams, AntiShip, PointDefence, Countermeasures, Reaction, Gunnery };

    /// <summary>Lines that could be offered now: in the fleet's armament, and not maxed.</summary>
    public static List<Upgrade> Eligible(Run run) =>
        All.Where(u => run.RankOf(u) < u.MaxRank && u.Fits(run.Fleet)).ToList();

    /// <summary>The combined effect on one stat of everything taken on a line.</summary>
    public static float Total(Run run, Upgrade line, Stat stat)
    {
        bool additive = stat == Stat.KillChance || stat == Stat.Intercept || stat == Stat.Channels || stat == Stat.Spoof || stat == Stat.Barrage;
        float total = additive ? 0f : 1f;
        for (int i = 0; i < run.RankOf(line); i++)
            foreach (var (s, value) in line.Path[i].Effects)
            {
                if (s != stat) continue;
                total = additive ? total + value : total * value;
            }
        return total;
    }
}
