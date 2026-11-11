using System;
using System.Collections.Generic;
using System.Linq;

namespace SeapowerSurvivors;

/// <summary>
/// A navy a run can be sailed for: its flagships, the ships and flights its
/// chests offer, and the navy it fights. The pairings are US and Soviet,
/// British and Argentine, Chinese and Japanese.
/// </summary>
sealed class Navy
{
    public readonly string Id, Name;
    /// <summary>The enemy as the mission briefing names it.</summary>
    public readonly string FoeName;
    readonly Func<Escort[]> pool;
    readonly Func<Roster> foe;
    readonly string[] prefixes;
    /// <summary>What each of the Air wing line's three refits does to this navy's flights.</summary>
    public readonly string[] Refits;

    public Navy(string id, string name, string foeName, Func<Escort[]> pool, Func<Roster> foe, string[] prefixes, params string[] refits)
    {
        Id = id;
        Name = name;
        FoeName = foeName;
        this.pool = pool;
        this.foe = foe;
        this.prefixes = prefixes;
        Refits = refits;
    }

    /// <summary>The ships and flights a chest can offer, and the starting escorts among them.</summary>
    public Escort[] Pool => pool();
    /// <summary>The enemy's ships, aircraft and timetable.</summary>
    public Roster Foe => foe();
    public IEnumerable<StartingShip> Flagships => Launcher.Flagships.Where(s => s.NavyId == Id);

    internal bool Has(string type) => Flagships.Any(s => s.Type == type) || Pool.Any(e => e.Type == type);
    internal bool HasPrefix(string type) => prefixes.Any(p => type.StartsWith(p, StringComparison.Ordinal));
}

static class Navies
{
    const string NoRefit = "no further refit for this navy's flights";

    internal static readonly Navy Us = new Navy("us", "United States", "Soviet", () => Escorts.Us, () => Rosters.Soviet, new[] { "usn_", "usaf_", "usmc_" },
        "A-7Es carry Walleye guided bombs; F-4Js become F-14As",
        "A-7Es become A-6Es with two Harpoons; F-14As carry a Phoenix",
        "A-6Es and Hornets carry four Harpoons; F-14As carry four Phoenix");

    internal static readonly Navy Soviet = new Navy("ussr", "Soviet Union", "NATO", () => Escorts.Soviet, () => Rosters.Nato, new[] { "wp_" },
        "Yak-38s become Su-24As with AS-14s; MiG-23MSs become MiG-23MLDs",
        "Su-24As become Su-24Ms with AS-14s and KAB-500Ls; MiG-23MLDs become MiG-25PDs",
        "a Tu-22M2 with three AS-4s joins for each strike aircraft; MiG-25PDs fly 1.4× as fast and turn 1.5× as hard");

    internal static readonly Navy Britain = new Navy("uk", "United Kingdom", "Argentine", () => Escorts.British, () => Rosters.Argentine, new[] { "rn_", "raf_" },
        "Harrier GR.3s carry five 1,000 lb bombs",
        "Harrier GR.3s become Nimrod MR.2s with two Harpoons",
        NoRefit);

    internal static readonly Navy Argentina = new Navy("argentina", "Argentina", "British", () => Escorts.Argentine, () => Rosters.British, new[] { "ara_" },
        "A-4s carry three 1,000 lb bombs; Daggers become Mirage IIIs with R.530s",
        "A-4Qs become Super Étendards with an Exocet",
        "Super Étendards carry two Exocets");

    internal static readonly Navy China = new Navy("china", "China", "Japanese", () => Escorts.Chinese, () => Rosters.Japanese, new[] { "plan_", "pla_" },
        "J-7s carry four PL-2s",
        "Q-5s become H-6Ds with two YJ-6s",
        NoRefit);

    internal static readonly Navy Japan = new Navy("japan", "Japan", "Chinese", () => Escorts.Japanese, () => Rosters.Chinese, new[] { "jmsdf_", "jasdf_" },
        "F-1s carry two ASM-1s",
        NoRefit,
        NoRefit);

    internal static readonly Navy[] All = { Us, Soviet, Britain, Argentina, China, Japan };

    internal static Navy ById(string id) => All.FirstOrDefault(n => n.Id == id) ?? Us;

    static readonly Dictionary<string, Navy> byType = new Dictionary<string, Navy>();

    /// <summary>
    /// The navy a ship or aircraft type sails for: the one that lists it, since
    /// Argentina flies French aircraft and a British helicopter, and failing
    /// that the one its name says. Anything else counts as American.
    /// </summary>
    internal static Navy Of(string type)
    {
        if (string.IsNullOrEmpty(type)) return Us;
        if (byType.TryGetValue(type, out var navy)) return navy;
        navy = All.FirstOrDefault(n => n.Flagships.Any(s => s.Type == type))
               ?? All.FirstOrDefault(n => n.HasPrefix(type))
               ?? All.FirstOrDefault(n => n.Has(type))
               ?? Us;
        return byType[type] = navy;
    }

    internal static Navy Of(StartingShip flagship) => ById(flagship.NavyId);
}
