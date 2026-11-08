using System;
using System.Collections.Generic;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Convoys and surface action groups: targets far beyond the fleet's reach
/// that do not come to it. Convoys cross on a lane and get away if left; a
/// SAG creeps in and keeps firing from beyond the horizon until it is sunk
/// or withdraws.
/// </summary>
/// <remarks>
/// Every few minutes a contact report puts a set piece on the fleet's plot
/// for a short window. Anti-ship missiles that find the target themselves
/// (Harpoon, Granit, Basalt, Exocet) can then be fired at it at their full
/// range, since the game caps only semi-active and command-guided rounds at
/// the horizon (WeaponSystemLauncher.CalculateHorizon). A revealed track
/// lasts about ten seconds (TrackComponent.MaxAge), so the window re-reveals
/// it every few seconds and the contact fades when the window closes. The
/// fleet's own aircraft hold it as long as they see it, through the game's
/// own datalink. Otherwise the fleet steams out to it.
///
/// Rewards: every hull pays kill XP as usual. A whole convoy sunk pays a
/// level-up; a SAG's capital ship sunk pays a chest, as a mini-boss does.
/// Escorts left afloat then turn on the fleet with the rest of the enemy.
/// </remarks>
sealed class SetPieces
{
    sealed class Active
    {
        public SetPiece Def;
        public readonly List<ObjectBase> Hulls = new List<ObjectBase>(), Escorts = new List<ObjectBase>();
        /// <summary>A convoy's end of lane, each ship its own, keeping station.</summary>
        public readonly Dictionary<ObjectBase, GeoPosition> LaneEnds = new Dictionary<ObjectBase, GeoPosition>();
        public double Ends, NextReport, ReportUntil;
        public string CapitalName;
        public IEnumerable<ObjectBase> Units => Hulls.Concat(Escorts);
    }

    /// <summary>Game seconds between re-reveals while a track must be held: well inside its ten-second life.</summary>
    const float RevealSeconds = 4f;
    /// <summary>Game seconds between re-steers of a SAG onto the fleet.</summary>
    const float SteerSeconds = 60f;
    /// <summary>A convoy ship this close to the end of its lane is past and gone.</summary>
    const float ArrivedNm = 0.2f;
    /// <summary>Escorts sail this far ahead of the convoy.</summary>
    const float EscortAheadNm = 1.5f;

    readonly List<Active> active = new List<Active>();
    double lastReveal, lastSteer;

    static double Now => GameTime.missionElapsedTime;
    static bool Alive(ObjectBase u) => u != null && !u.IsDestroyed;

    /// <summary>Set-piece units afloat, for the status line's enemy count.</summary>
    internal int Count => active.Sum(a => a.Units.Count(Alive));
    internal IEnumerable<ObjectBase> Afloat => active.SelectMany(a => a.Units).Where(Alive);

    internal void Clear()
    {
        active.Clear();
        lastReveal = lastSteer = 0.0;
    }

    /// <summary>Spawn a set piece far out. False if there was no open water to put it on.</summary>
    internal bool Launch(SetPiece def, ObjectBase player, Run run, float defence)
    {
        var fleet = run.Fleet.Concat(new[] { player }).Distinct().ToList();
        // The sighting is the first contact report: its window opens now.
        var piece = new Active
        {
            Def = def,
            Ends = Now + def.LifeMinutes * 60f,
            ReportUntil = Now + Script.ReportWindowMinutes * 60f,
            NextReport = Now + Script.ReportEveryMinutes * 60f,
        };

        // The first hull is made somewhere open, so the fleet's reach against it can be measured; then it is put in place.
        float provisional = 30f;
        float firstBearing = Sea.ShipBearing(player._geoPosition, ref provisional);
        var first = Spawner.Ship(def.Hulls[0], Spawner.Offset(player._geoPosition, provisional, firstBearing), 0f, Globals._enemyTaskforce);
        if (first == null)
        {
            Entry.Log.LogWarning($"Could not spawn {def.Hulls[0]} for {def.Name}");
            return false;
        }
        float reach = Reach.PlayerReachNm(fleet, first);
        // Placed from the flagship, so it also has to be past the furthest escort, with room for its own line abreast.
        float spread = fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit)
            .Max(s => (float)player._geoPosition.getDistanceInMiles(s._geoPosition));
        float clearOfFleet = spread + Script.MinSpawnNm + (def.Hulls.Length + def.Escorts.Length) / 2f;
        float beyond = Mathf.Max(UnityEngine.Random.Range(def.MinBeyondNm, def.MaxBeyondNm), clearOfFleet - reach);

        bool placed = def.Kind == SetPieceKind.Convoy
            ? PlaceConvoy(piece, first, player, reach + beyond)
            : PlaceSag(piece, first, player, reach + beyond);
        if (!placed)
        {
            foreach (var unit in piece.Units.Append(first).Distinct().Where(Alive)) unit.destroyObject(score: false);
            Entry.Log.LogWarning($"{def.Name}: no open water for it, not sent");
            return false;
        }

        foreach (var unit in piece.Units)
        {
            Spawner.SetSpeed(unit, def.Knots);
            unit._weaponStatus = ObjectBase.WeaponStatus.Free;
            if (defence < def.MinDefence) Director.Disarm(unit);
        }
        active.Add(piece);

        var centre = Centre(piece);
        TestPanel.Report($"{def.Name} sighted, bearing {player._geoPosition.HeadingToPosition(centre):F0}, {player._geoPosition.getDistanceInMiles(centre):F0} nm" +
                         (defence < def.MinDefence ? $" (guns only)" : ""));
        Entry.Log.LogInfo($"Set piece {def.Name}: {piece.Hulls.Count} hulls, {piece.Escorts.Count} escorts, " +
                          $"fleet reach {reach:F1} nm, placed {beyond:F1} nm beyond it, {def.Knots:F0} kn, {def.LifeMinutes:F0} game min");
        return true;
    }

    /// <summary>Merchants line abreast across their lane, escorts ahead, all steaming for the far end of it.</summary>
    static bool PlaceConvoy(Active piece, Vessel first, ObjectBase player, float passNm)
    {
        var def = piece.Def;
        float length = def.Knots * def.LifeMinutes / 60f;
        if (Sea.ConvoyLane(player._geoPosition, passNm, length) is not { } lane) return false;
        var (start, end, heading) = lane;

        for (int i = 0; i < def.Hulls.Length; i++)
        {
            var at = Sea.Abreast(start, heading, i);
            var ship = i == 0 ? first : Spawner.Ship(def.Hulls[i], at, heading, Globals._enemyTaskforce);
            if (ship == null) continue;
            if (i == 0) Face(ship, at, heading);
            piece.Hulls.Add(ship);
            piece.LaneEnds[ship] = Sea.Abreast(end, heading, i);
        }
        var ahead = Spawner.Offset(start, EscortAheadNm, heading);
        var aheadEnd = Spawner.Offset(end, EscortAheadNm, heading);
        for (int j = 0; j < def.Escorts.Length; j++)
        {
            var ship = Spawner.Ship(def.Escorts[j], Sea.Abreast(ahead, heading, j), heading, Globals._enemyTaskforce);
            if (ship == null) continue;
            piece.Escorts.Add(ship);
            piece.LaneEnds[ship] = Sea.Abreast(aheadEnd, heading, j);
        }
        foreach (var unit in piece.Units) unit.setWaypointTask(piece.LaneEnds[unit]);
        return true;
    }

    /// <summary>A capital ship with its escorts abreast, bow on to the fleet, coming in slowly.</summary>
    static bool PlaceSag(Active piece, Vessel first, ObjectBase player, float distanceNm)
    {
        var def = piece.Def;
        float bearing = Sea.ShipBearing(player._geoPosition, ref distanceNm);
        var centre = Spawner.Offset(player._geoPosition, distanceNm, bearing);
        float heading = (bearing + 180f) % 360f;
        Face(first, centre, heading);
        piece.Hulls.Add(first);
        piece.CapitalName = first.Name.Value;
        for (int j = 0; j < def.Escorts.Length; j++)
        {
            var ship = Spawner.Ship(def.Escorts[j], Sea.Abreast(centre, bearing, j + 1), heading, Globals._enemyTaskforce);
            if (ship != null) piece.Escorts.Add(ship);
        }
        foreach (var unit in piece.Units) Spawner.Hunt(unit, player);
        return true;
    }

    static void Face(ObjectBase unit, GeoPosition at, float heading)
    {
        Spawner.MoveTo(unit, at);
        unit.transform.rotation = Quaternion.Euler(0f, heading, 0f);
    }

    /// <summary>Once a director tick: contact reports, a SAG's pursuit, convoys getting away, and rewards.</summary>
    /// <param name="adopt">Takes escorts left afloat once their charge is gone, to hunt the fleet with the rest.</param>
    internal void Tick(ObjectBase player, Run run, int chestTier, Action<ObjectBase> adopt)
    {
        bool reveal = Now - lastReveal >= RevealSeconds;
        if (reveal) lastReveal = Now;
        bool steer = Now - lastSteer >= SteerSeconds;
        if (steer) lastSteer = Now;
        var watchers = run.Fleet.Append(player).Where(s => Alive(s) && !s.IsAirUnit).Distinct().ToList();

        foreach (var piece in active.ToList())
        {
            var def = piece.Def;
            if (!piece.Hulls.Any(Alive))
            {
                active.Remove(piece);
                Pay(piece, run, chestTier);
                foreach (var escort in piece.Escorts.Where(Alive)) adopt(escort);
                continue;
            }

            if (Now > piece.Ends || def.Kind == SetPieceKind.Convoy && piece.Hulls.Where(Alive).Any(h => h._geoPosition.getDistanceInMiles(piece.LaneEnds[h]) < ArrivedNm))
            {
                active.Remove(piece);
                int sunk = piece.Hulls.Count(h => !Alive(h));
                foreach (var unit in piece.Units.Where(Alive).ToList()) unit.destroyObject(score: false);
                TestPanel.Report(def.Kind == SetPieceKind.Convoy
                    ? $"{def.Name} got away: {sunk} of {piece.Hulls.Count} sunk"
                    : $"{def.Name} withdraws");
                continue;
            }

            if (Now >= piece.NextReport)
            {
                piece.NextReport = Now + Script.ReportEveryMinutes * 60f;
                piece.ReportUntil = Now + Script.ReportWindowMinutes * 60f;
                var centre = Centre(piece);
                TestPanel.Report($"Contact report: {def.Name}, bearing {player._geoPosition.HeadingToPosition(centre):F0}, {player._geoPosition.getDistanceInMiles(centre):F0} nm");
                reveal = true;
            }
            if (reveal && Now < piece.ReportUntil)
                foreach (var unit in piece.Units.Where(Alive))
                    foreach (var watcher in watchers)
                        Utils.RevealContactToObject(watcher, unit, "Comms", 2);

            // A SAG is shadowing the fleet: it keeps the fleet on its plot, so it fires from beyond the horizon.
            if (def.Kind == SetPieceKind.Sag)
                foreach (var unit in piece.Units.Where(Alive))
                {
                    if (reveal) Spawner.Alert(unit, player);
                    if (steer) Spawner.Steer(unit, player);
                }
        }
    }

    static void Pay(Active piece, Run run, int chestTier)
    {
        var def = piece.Def;
        if (def.Kind == SetPieceKind.Convoy)
        {
            run.PendingLevels++;
            TestPanel.Report($"{def.Name} sunk to the last ship: a level-up");
            return;
        }
        int levels = UnityEngine.Random.value < Balance.BigChestChance ? 3 : 1;
        run.PendingLevels += levels;
        bool ship = !Ensigns.EscortsClosed(run);
        if (ship) run.ShipChests.Enqueue(chestTier);
        TestPanel.Report($"{def.Name}: {piece.CapitalName ?? "capital ship"} sunk: chest of {(ship ? "a ship and " : "")}{levels} level-up{(levels > 1 ? "s" : "")}");
    }

    static GeoPosition Centre(Active piece)
    {
        var afloat = piece.Hulls.Where(Alive).DefaultIfEmpty(piece.Units.FirstOrDefault(Alive)).Where(u => u != null).ToList();
        if (afloat.Count == 0) return default;
        return new GeoPosition
        {
            _latitude = afloat.Average(u => u._geoPosition._latitude),
            _longitude = afloat.Average(u => u._geoPosition._longitude),
        };
    }
}
