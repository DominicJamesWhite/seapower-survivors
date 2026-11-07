using System.Collections.Generic;
using System.Linq;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// What the next run launches with, as last chosen on the Survivors screen
/// or the test panel: the area, the flagship, every starting ship and flight
/// bought for its navy that has not been left ashore, and where the ships sail.
/// </summary>
static class Setup
{
    internal static Area Area(Profile profile) =>
        Areas.All.FirstOrDefault(a => a.Id == profile.AreaId) ?? Areas.All[0];

    /// <summary>The flagship last chosen, or the free one if that is not owned (after a wipe, say).</summary>
    internal static StartingShip Flagship(Profile profile) =>
        Launcher.Flagships.FirstOrDefault(s => s.Type == profile.FlagshipType && profile.Owns(Store.FlagshipId(s))) ??
        Launcher.Flagships.First(s => profile.Owns(Store.FlagshipId(s)));

    /// <summary>The escorts chosen to sail, each once. The Greek ensign: only the one picked, or if none has been, the first that would otherwise sail.</summary>
    internal static List<Escort> Sailing(Profile profile)
    {
        var owned = Store.OwnedEscorts(profile, Flagship(profile));
        var sailing = owned.Where(e => !profile.IsAshore(Store.EscortId(e))).ToList();
        if (!OneEscortOnly(profile)) return sailing;
        if (profile.LoneEscort == Profile.NoEscort) return new List<Escort>();
        var lone = owned.FirstOrDefault(e => Store.EscortId(e) == profile.LoneEscort) ?? sailing.FirstOrDefault();
        return lone == null ? new List<Escort>() : new List<Escort> { lone };
    }

    internal static bool OneEscortOnly(Profile profile) => profile.HoistedEnsigns().Contains(Ensigns.FiveOfAKind);

    /// <summary>Sends an escort to sea or leaves it ashore. Under the Greek ensign, sending one replaces the one picked before.</summary>
    internal static void SetSails(Profile profile, Escort escort, bool sails)
    {
        string id = Store.EscortId(escort);
        if (!OneEscortOnly(profile)) profile.SetAshore(id, !sails);
        else if (sails != Sailing(profile).Contains(escort)) profile.SetLoneEscort(sails ? id : Profile.NoEscort);
    }

    /// <summary>What actually sails. The Greek ensign: the one sailing, five times.</summary>
    static List<Escort> Fleet(Profile profile)
    {
        var fleet = Sailing(profile);
        return OneEscortOnly(profile) && fleet.Count > 0
            ? Enumerable.Repeat(fleet[0], Ensigns.EscortCopies).ToList()
            : fleet;
    }

    /// <summary>
    /// Every starting ship's station: where the player put it, or else the
    /// next clear place abeam of the flagship, alternately to starboard and
    /// port and stepping outwards.
    /// </summary>
    internal static List<FleetStation> Stations(Profile profile)
    {
        var seen = new Dictionary<string, int>();
        var stations = new List<FleetStation>();
        var unplaced = new List<FleetStation>();
        foreach (var ship in Fleet(profile).Where(e => !e.IsAir))
        {
            // Copies under the Greek ensign each keep their own station.
            string id = Store.EscortId(ship);
            seen.TryGetValue(id, out int copy);
            seen[id] = copy + 1;
            var station = new FleetStation { Key = copy == 0 ? id : $"{id}#{copy}", Name = ship.Name };
            if (!profile.TryGetStation(station.Key, out station.Right, out station.Ahead)) unplaced.Add(station);
            stations.Add(station);
        }
        int abeam = 0;
        foreach (var station in unplaced)
        {
            do
            {
                int step = abeam / 2 + 1;
                station.Right = (abeam % 2 == 0 ? step : -step) * Formations.SpacingNm;
                station.Ahead = 0f;
                abeam++;
            } while (stations.Except(unplaced).Any(s => FormationEditor.Distance(s.Right - station.Right, s.Ahead - station.Ahead) < Formations.SpacingNm));
        }
        KeepOffLand(stations, Area(profile));
        return stations;
    }

    /// <summary>
    /// A station is saved for every area, so what is open water in one can be
    /// a hillside in another. Those ships start on the nearest clear water; the
    /// saved station stands for the areas where it is good.
    /// </summary>
    static void KeepOffLand(List<FleetStation> stations, Area area)
    {
        var start = area.PlayerStart;
        float heading = start.HeadingToPosition(area.EnemyWaters);
        foreach (var station in stations)
        {
            if (Chart.IsWater(Formations.StationAt(start, heading, station.Right, station.Ahead))) continue;
            if (NearestClear(station, stations, start, heading) is (float right, float ahead))
            {
                station.Right = right;
                station.Ahead = ahead;
                station.MovedOffLand = true;
            }
            else Entry.Log.LogWarning($"No clear water near {station.Name}'s station in {area.Name}: it may start aground");
        }
    }

    static (float right, float ahead)? NearestClear(FleetStation station, List<FleetStation> stations, GeoPosition start, float heading)
    {
        const float StepNm = 0.5f;
        const int Bearings = 24;
        float limit = Formations.MaxStationNm;
        for (float radius = StepNm; radius <= 2f * limit; radius += StepNm)
            for (int i = 0; i < Bearings; i++)
            {
                double radians = i * 2.0 * System.Math.PI / Bearings;
                float right = FormationEditor.Round(station.Right + radius * (float)System.Math.Sin(radians));
                float ahead = FormationEditor.Round(station.Ahead + radius * (float)System.Math.Cos(radians));
                if (System.Math.Abs(right) > limit || System.Math.Abs(ahead) > limit) continue;
                if (FormationEditor.Distance(right, ahead) < Formations.MinStationNm) continue;
                if (stations.Any(s => s != station && FormationEditor.Distance(s.Right - right, s.Ahead - ahead) < Formations.MinStationNm)) continue;
                if (Chart.IsWater(Formations.StationAt(start, heading, right, ahead))) return (right, ahead);
            }
        return null;
    }

    internal static Loadout Loadout(Profile profile)
    {
        var loadout = new Loadout(Flagship(profile), Area(profile)) { TurnWithFlagship = profile.StationsTurn };
        var fleet = Fleet(profile);
        loadout.Ships.AddRange(fleet.Where(e => !e.IsAir));
        loadout.Flights.AddRange(fleet.Where(e => e.IsAir));
        loadout.Stations.AddRange(Stations(profile));
        return loadout;
    }
}
