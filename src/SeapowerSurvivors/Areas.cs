using System.Linq;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// Somewhere a run can be fought: where the fleet starts, and which way the
/// enemy comes from. Ships come from the enemy's waters; aircraft from its
/// airfield, or from its waters when it has none.
/// </summary>
sealed class Area
{
    public readonly string Id, Name, Region, Blurb;
    /// <summary>The fleet's deployment box and the enemy's, as corners in order round each.</summary>
    public readonly GeoPosition[] PlayerBox, EnemyBox;
    public readonly GeoPosition? EnemyAirfield;

    public Area(string id, string name, string region, string blurb, double[,] playerBox, double[,] enemyBox, double[] enemyAirfield = null)
    {
        Id = id;
        Name = name;
        Region = region;
        Blurb = blurb;
        PlayerBox = Corners(playerBox);
        EnemyBox = Corners(enemyBox);
        if (enemyAirfield != null) EnemyAirfield = At(enemyAirfield[0], enemyAirfield[1]);
    }

    public GeoPosition PlayerStart => Centre(PlayerBox);
    public GeoPosition EnemyWaters => Centre(EnemyBox);
    public GeoPosition EnemyAir => EnemyAirfield ?? EnemyWaters;

    /// <summary>Quick Start's painting of the area, found only while that mod is enabled.</summary>
    public string Art => $"campaigns/qs-quick-battles/art/{Id}.png";

    /// <summary>
    /// Where a local copy of Quick Start's paintings is looked for when that
    /// mod is disabled: an art folder in this mod's install folder.
    /// </summary>
    public static string LocalArtFolder =>
        System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, "SeapowerSurvivors", "art");

    static GeoPosition[] Corners(double[,] corners) =>
        Enumerable.Range(0, corners.GetLength(0)).Select(i => At(corners[i, 0], corners[i, 1])).ToArray();

    static GeoPosition Centre(GeoPosition[] corners) =>
        At(corners.Average(c => c._latitude), corners.Average(c => c._longitude));

    static GeoPosition At(double latitude, double longitude) =>
        new GeoPosition { _latitude = latitude, _longitude = longitude };
}

/// <summary>
/// The areas a run can be launched in. The deployment boxes and airfields are
/// the first fight of the same areas in Quick Start's battle_areas.json, by
/// the Quick Start mod's author, so the two mods agree on where these seas are.
/// </summary>
static class Areas
{
    internal static readonly Area[] All =
    {
        new Area("giuk_gap", "GIUK Gap", "North Atlantic",
            "Deep water and long sightlines between Iceland and the Faroes. Nothing to hide behind.",
            new[,] { { 58.534, -23.8333 }, { 58.534, -24.4333 }, { 57.466, -24.4333 }, { 57.466, -23.8333 } },
            new[,] { { 58.534, -21.5667 }, { 58.534, -22.1667 }, { 57.466, -22.1667 }, { 57.466, -21.5667 } }),
        new Area("norwegian_sea_bergen", "Norwegian Sea (Bergen)", "Off Bergen",
            "The shelf edge off Norway, with the coastal leads close by and enemy aircraft based at Stavanger.",
            new[,] { { 60.934, 1.9667 }, { 60.934, 1.3667 }, { 59.866, 1.3667 }, { 59.866, 1.9667 } },
            new[,] { { 60.934, 4.2333 }, { 60.934, 3.6333 }, { 59.866, 3.6333 }, { 59.866, 4.2333 } },
            new[] { 58.877, 5.638 }),
        new Area("barents_sea_murmansk", "Barents Sea (Murmansk)", "Off the Kola Inlet",
            "On the Northern Fleet's doorstep, with shore-based air minutes away.",
            new[,] { { 71.9265, 33.6833 }, { 71.9265, 33.0833 }, { 70.8585, 33.0833 }, { 70.8585, 33.6833 } },
            new[,] { { 69.1987, 36.1231 }, { 69.7306, 36.1231 }, { 69.7306, 34.9231 }, { 69.1987, 34.9231 } },
            new[] { 68.152, 33.464 }),
        new Area("virginia_capes_norfolk", "Virginia Capes (Norfolk)", "Off the Chesapeake",
            "Open Atlantic off the Atlantic Fleet's home port.",
            new[,] { { 37.434, -74.0333 }, { 37.434, -74.6333 }, { 36.366, -74.6333 }, { 36.366, -74.0333 } },
            new[,] { { 37.434, -71.7667 }, { 37.434, -72.3667 }, { 36.366, -72.3667 }, { 36.366, -71.7667 } }),
        new Area("falkland_islands", "Falkland Islands", "South Atlantic",
            "Cold, empty sea north of the islands.",
            new[,] { { -51.2417, -57.3767 }, { -50.4437, -57.3767 }, { -50.4437, -59.1767 }, { -51.2417, -59.1767 } },
            new[,] { { -52.6563, -61.1733 }, { -52.6563, -62.0733 }, { -54.2583, -62.0733 }, { -54.2583, -61.1733 } }),
        new Area("sea_of_japan_niigata", "Sea of Japan (Niigata)", "Off Niigata",
            "A closed sea ringed by airfields, Hokkaido's among them.",
            new[,] { { 37.7282, 137.537 }, { 38.2825, 137.307 }, { 37.8738, 136.32 }, { 37.3195, 136.55 } },
            new[,] { { 40.133, 139.371 }, { 40.133, 138.771 }, { 39.065, 138.771 }, { 39.065, 139.371 } },
            new[] { 41.77, 140.822 }),
        new Area("taiwan_strait", "Taiwan Strait", "West of Penghu",
            "Shallow water between two coasts, with the mainland's air force close.",
            new[,] { { 24.9328, 120.669 }, { 25.1364, 120.178 }, { 24.0277, 119.719 }, { 23.8241, 120.21 } },
            new[,] { { 25.0014, 119.432 }, { 25.205, 118.94 }, { 24.0964, 118.481 }, { 23.8928, 118.972 } },
            new[] { 24.9, 118.55 }),
        new Area("aegean_sea", "Aegean Sea", "Central Aegean",
            "A maze of islands and radar shadows.",
            new[,] { { 36.834, 24.1218 }, { 36.834, 23.5218 }, { 35.766, 23.5218 }, { 35.766, 24.1218 } },
            new[,] { { 36.034, 26.9782 }, { 36.566, 26.9782 }, { 36.566, 25.7782 }, { 36.034, 25.7782 } },
            new[] { 36.716, 28.793 }),
        new Area("straits_of_hormuz", "Straits of Hormuz", "The Narrows",
            "A narrow bend of shallow water between mountains and desert.",
            new[,] { { 25.8569, 55.7063 }, { 26.01, 56.0758 }, { 26.7491, 55.7697 }, { 26.596, 55.4001 } },
            new[,] { { 26.2777, 56.7303 }, { 26.4308, 57.0999 }, { 27.1699, 56.7937 }, { 27.0168, 56.4242 } },
            new[] { 25.5, 60.7 }),
        new Area("the_slot_solomons", "The Slot (Solomons)", "New Georgia Sound",
            "A straight run of deep water walled by jungle islands.",
            new[,] { { -10.1011, 161.088 }, { -9.7249, 161.464 }, { -8.8764, 160.616 }, { -9.2526, 160.239 } },
            new[,] { { -8.9144, 159.161 }, { -8.5382, 159.537 }, { -7.6897, 158.688 }, { -8.0659, 158.312 } },
            new[] { -8.3, 157.3 }),
    };
}
