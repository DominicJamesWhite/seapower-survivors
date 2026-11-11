using System.Collections.Generic;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>Where a starting ship sails relative to the flagship, as placed on the Survivors screen. Starboard and ahead are positive.</summary>
sealed class FleetStation
{
    public string Key, Name;
    public float Right, Ahead;
    /// <summary>Its saved station is on land in this area, so it starts on the nearest clear water instead.</summary>
    public bool MovedOffLand;
}

/// <summary>
/// The fleet as one of the game's own Loose formations, led by the flagship,
/// so escorts keep station on it and the player can edit them in the game's
/// Formation Editor. The slowest ship limits the flagship's speed, as in any
/// formation. A run's first ships are written into the mission where the
/// player placed them (SceneCreator.setUpFormation gives a Loose ship its
/// station from where it starts); ships from chests take a clear station here.
/// </summary>
static class Formations
{
    /// <summary>Ships are spaced this far apart by default, as the game's own missions space a task group.</summary>
    internal const float SpacingNm = 1.5f;
    /// <summary>The game's Task Force editor stops at this range.</summary>
    internal const float MaxStationNm = 10f;
    /// <summary>
    /// Two stations are at least this far apart. The game's collision avoidance
    /// starts at four hull lengths, about 0.6 nm for the longest hulls.
    /// </summary>
    internal const float MinStationNm = 0.6f;
    /// <summary>A ship joining from a chest is at least this far from every other.</summary>
    const float ClearanceNm = 1f;
    /// <summary>A ship joining from a chest has deep water this far all round it: one cell of the game's detailed terrain.</summary>
    const float SeaRoomNm = 0.5f;
    /// <summary>And this far ahead, along the course it spawns on.</summary>
    const float SeaRoomAheadNm = 1f;
    /// <summary>The last resort, with no water on any ring: this far astern of the leader.</summary>
    const float AsternNm = 0.5f;
    /// <summary>Unity units to the nautical mile (UnitFormation's own constant).</summary>
    const float NmToUnity = 27.559496f;
    const string Name = "Task Group";
    const int Rings = 6;
    /// <summary>Beams first, as ships have always joined; dead ahead and astern, in the flagship's path and wake, last.</summary>
    static readonly float[] RingBearings = { 90f, 270f, 45f, 315f, 135f, 225f, 0f, 180f };

    /// <summary>Where a station is on the map, for a flagship at <paramref name="flagship"/> on this heading.</summary>
    internal static GeoPosition StationAt(GeoPosition flagship, float heading, float right, float ahead)
    {
        float range = FormationEditor.Distance(right, ahead);
        if (range == 0f) return flagship;
        float bearing = heading + (float)(System.Math.Atan2(right, ahead) * 180.0 / System.Math.PI);
        return Spawner.Offset(flagship, range, (bearing % 360f + 360f) % 360f);
    }

    /// <summary>The mission line for the ships a run starts with: members, control mode, name, pattern, spacing and whether stations turn with the leader.</summary>
    internal static string MissionLine(int ships, bool turnWithFlagship, string spacing)
    {
        var members = Enumerable.Range(1, ships).Select(i => $"Taskforce1Vessel{i}");
        string turn = turnWithFlagship ? "|RotateStationsWithLeader" : "";
        return $"Taskforce1_Formation1={string.Join(",", members)}|Local|{Name}|Loose|{spacing}{turn}";
    }

    /// <summary>
    /// The flagship's formation, formed now if it has none: a run that starts
    /// alone does not sail in one until a ship joins, since a formation of one
    /// would still cost its leader a knot.
    /// </summary>
    internal static UnitFormation Of(Run run)
    {
        var flagship = run.Ship;
        var formation = flagship.Formation;
        if (formation != null)
        {
            var leader = formation.LeaderStation?.UnitObject;
            if (leader == null || leader.IsDestroyed) formation.SwapLeader(flagship);
            return formation;
        }
        return new UnitFormation(new UnitFormationParameters
        {
            _name = Name,
            _leaderObject = flagship,
            _pattern = UnitFormation.FormationPattern.Loose,
            _spacing = SpacingNm,
            _rotateStationsAroundLeader = Launcher.IsLaunchedMission() && Launcher.Current.TurnWithFlagship,
        });
    }

    /// <summary>The flag has passed: the formation forms on the new flagship, not whichever ship the game chose.</summary>
    internal static void PassFlag(ObjectBase heir)
    {
        var formation = heir.Formation;
        if (formation != null && formation.LeaderStation?.UnitObject != heir) formation.SwapLeader(heir);
    }

    /// <summary>Where a joining ship spawns, and the station that holds it there.</summary>
    internal sealed class Slot
    {
        public GeoPosition At;
        public Vector3 World, Station;
    }

    /// <summary>The nearest of the widening rings around the leader with room to float and sail on, and clear of every ship and every station.</summary>
    /// <remarks>
    /// The game numbers a new station by how many stations there are, and a
    /// ship that sinks gives its station up, so <see cref="UnitFormation.AddUnit"/>
    /// alone can assign one that is taken. <see cref="Take"/> writes this over it.
    /// </remarks>
    internal static Slot FindStation(UnitFormation formation)
    {
        var slots = Candidates(formation).ToList();
        var taken = Occupied(formation);
        var leader = formation.LeaderStation.UnitObject;
        float course = Spawner.TrueHeading(leader);
        float clearance = ClearanceNm * NmToUnity;
        bool IsClear(Slot slot) => taken.All(p => Flat(p - slot.World).sqrMagnitude >= clearance * clearance);
        bool HasRoom(Slot slot) => HasSeaRoom(slot.At, course);

        var open = slots.FirstOrDefault(s => HasRoom(s) && IsClear(s));
        if (open != null) return open;
        Entry.Log.LogWarning($"No clear station within {Rings * SpacingNm} nm of {leader.Name.Value}: joining as close to clear as there is");
        // Hemmed in: better near another ship than aground, and as far from the others as the water allows.
        var water = slots.Where(HasRoom).ToList();
        if (water.Count == 0) water = slots.Where(s => IsSea(s.At)).ToList();
        if (water.Count > 0) return water.OrderByDescending(s => taken.Min(p => Flat(p - s.World).sqrMagnitude)).First();
        // No water on any ring: just astern, where the leader has floated and most likely just sailed.
        return ToSlot(formation, new Vector3(0f, 0f, -AsternNm * NmToUnity));
    }

    /// <summary>Deep enough by the loaded terrain, which reads as deep where it has not loaded, and by the terrain files, which always answer.</summary>
    static bool IsSea(GeoPosition at) => Sea.IsWater(at) && Chart.IsWater(at);

    /// <summary>
    /// Deep enough here and all round, for a hull that is not a point, and on
    /// ahead along the course it spawns on and carries way along.
    /// </summary>
    static bool HasSeaRoom(GeoPosition at, float course)
    {
        if (!IsSea(at) || !IsSea(Spawner.Offset(at, SeaRoomAheadNm, course))) return false;
        for (float bearing = 0f; bearing < 360f; bearing += 45f)
            if (!IsSea(Spawner.Offset(at, SeaRoomNm, bearing))) return false;
        return true;
    }

    /// <summary>Put a ship just spawned at <paramref name="slot"/> on that station.</summary>
    internal static void Take(UnitFormation formation, ObjectBase ship, Slot slot)
    {
        formation.AddUnit(ship);
        formation.ChangeStationPosition(formation.GetStationForUnit(ship), slot.Station);
    }

    static IEnumerable<Slot> Candidates(UnitFormation formation)
    {
        for (int ring = 1; ring <= Rings; ring++)
            foreach (float bearing in RingBearings)
            {
                float radius = ring * SpacingNm * NmToUnity;
                float radians = bearing * Mathf.Deg2Rad;
                yield return ToSlot(formation, new Vector3(Mathf.Sin(radians) * radius, 0f, Mathf.Cos(radians) * radius));
            }
    }

    /// <param name="offset">From the leader, in its own frame: x to starboard, z ahead.</param>
    static Slot ToSlot(UnitFormation formation, Vector3 offset)
    {
        var leader = formation.LeaderStation.UnitObject.transform;
        var turned = Quaternion.AngleAxis(leader.eulerAngles.y, Vector3.up) * offset;
        var world = leader.position + turned;
        var at = Utils.worldPositionFromUnityToLongLat(world, Globals._currentCenterTile);
        at._height = 0.0;
        // A station that does not rotate with the leader is kept in world axes (UnitFormation.AddUnit).
        return new Slot { At = at, World = world, Station = formation.RotateStationsAroundLeader ? offset : turned };
    }

    /// <summary>Every player ship where it is now, and every station where a ship is bound.</summary>
    static List<Vector3> Occupied(UnitFormation formation)
    {
        var taken = Singleton<ObjectsManager>.Instance._listOfAllPlayerVesselObjects
            .Where(s => s != null && !s.IsDestroyed).Select(s => s.transform.position).ToList();
        var leader = formation.LeaderStation.UnitObject.transform;
        var turn = Quaternion.AngleAxis(leader.eulerAngles.y, Vector3.up);
        foreach (var station in formation.Stations)
            if (station.UnitObject != null && station != formation.LeaderStation)
                taken.Add(leader.position + (formation.RotateStationsAroundLeader ? turn * station.StationPosition : station.StationPosition));
        return taken;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
