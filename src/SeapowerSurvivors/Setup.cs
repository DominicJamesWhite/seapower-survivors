using System.Collections.Generic;
using System.Linq;

namespace SeapowerSurvivors;

/// <summary>
/// What the next run launches with, as last chosen on the Survivors screen
/// or the test panel: the area, the flagship, and every starting ship and
/// flight bought for its navy that has not been left ashore.
/// </summary>
static class Setup
{
    internal static Area Area(Profile profile) =>
        Areas.All.FirstOrDefault(a => a.Id == profile.AreaId) ?? Areas.All[0];

    /// <summary>The flagship last chosen, or the free one if that is not owned (after a wipe, say).</summary>
    internal static StartingShip Flagship(Profile profile) =>
        Launcher.Flagships.FirstOrDefault(s => s.Type == profile.FlagshipType && profile.Owns(Store.FlagshipId(s))) ??
        Launcher.Flagships.First(s => profile.Owns(Store.FlagshipId(s)));

    internal static List<Escort> Sailing(Profile profile) =>
        Store.OwnedEscorts(profile, Flagship(profile)).Where(e => !profile.IsAshore(Store.EscortId(e))).ToList();

    internal static Loadout Loadout(Profile profile)
    {
        var loadout = new Loadout(Flagship(profile), Area(profile));
        var fleet = Sailing(profile);
        // The Greek ensign: the first one sailing, five times, and no others.
        if (profile.HoistedEnsigns().Contains(Ensigns.FiveOfAKind) && fleet.Count > 0)
            fleet = Enumerable.Repeat(fleet[0], Ensigns.EscortCopies).ToList();
        loadout.Ships.AddRange(fleet.Where(e => !e.IsAir));
        loadout.Flights.AddRange(fleet.Where(e => e.IsAir));
        return loadout;
    }
}
