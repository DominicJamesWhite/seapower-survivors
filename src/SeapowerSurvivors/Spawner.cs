using System;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Creating units mid-mission and pointing them at the player. Ships follow
/// the game's own CivilianRoute.SpawnUnit; aircraft follow it too, plus the
/// giveControl call SeapowerMultiplayer's replicator makes.
/// </summary>
static class Spawner
{
    const float KnotsToUnity = 0.0076554087f;
    internal const double FeetToUnity = 0.004535709857940674;
    /// <summary>Full ahead. A run is fifteen minutes; there is no time to arrive at a third of it.</summary>
    const int FlankTelegraph = 5;

    internal static Vessel Ship(string type, GeoPosition at, float heading, Taskforce side, string fit = "default")
    {
        var objects = Singleton<ObjectsManager>.Instance;
        var ship = objects.createShip("random", fit, type, objects._listOfAllObjects.Count + 1,
            Singleton<SceneCreator>.Instance.GeoCenterPosition, at, FlankTelegraph, new Vector3(0f, heading, 0f), side);
        if (ship == null) return null;

        Place(ship, at);
        if (side == Globals._enemyTaskforce) Boosts.ApplyEnsignsToEnemy(ship);
        ship._velocityInKnots = ship._telegraph * ship.MaxForwardSpeedInKnots / 5f;
        ship._velocityInUnity = ship._velocityInKnots * KnotsToUnity;
        return ship;
    }

    /// <summary>Reposition a unit just spawned, keeping its speed.</summary>
    internal static void MoveTo(ObjectBase unit, GeoPosition at)
    {
        at._height = unit._geoPosition._height;
        Place(unit, at);
    }

    internal static Aircraft Aircraft(string type, string squadron, string loadout, GeoPosition at, float altitudeFt, float heading, Taskforce side)
    {
        at._height = altitudeFt * FeetToUnity;
        var objects = Singleton<ObjectsManager>.Instance;
        var aircraft = objects.createAircraft(squadron, loadout, null, null, type, objects._listOfAllObjects.Count + 1,
            Singleton<SceneCreator>.Instance.GeoCenterPosition, at, new Vector3(0f, heading, 0f), side,
            createMinimapSymbol: true, loadWeaponSystems: true);
        if (aircraft == null) return null;

        aircraft.DesiredAltitude.Value = aircraft.transform.position.y;
        if (aircraft._obp._codeAnimations.TryGetValue("Gear_Extend", out var gear)) gear.setClosedState();
        aircraft.ImmediateStartEngines();
        aircraft.setTelegraph(3);
        aircraft.SetFlightPhysicsModel(isStartingAirborne: true);
        Place(aircraft, at);
        aircraft.giveControl(aircraft.SpeedCommand.Value.GetDemandedSpeedInKnots(aircraft));
        if (side == Globals._enemyTaskforce) Boosts.ApplyEnsignsToEnemy(aircraft);
        return aircraft;
    }

    /// <summary>
    /// Spawned units neither know the player exists nor close on their own:
    /// reveal the player to them, free their weapons and send them at it.
    /// </summary>
    internal static void Hunt(ObjectBase unit, ObjectBase player)
    {
        Alert(unit, player);
        Steer(unit, player);
    }

    /// <summary>
    /// Reveal the player and free the unit's weapons, without sending it
    /// anywhere. A revealed track lasts about ten seconds (TrackComponent's
    /// MaxAge) unless the unit's own sensors hold it, so a unit kept beyond
    /// the horizon has to be told again to keep firing.
    /// </summary>
    internal static void Alert(ObjectBase unit, ObjectBase player)
    {
        // Blinded by an EMP mortar: nothing can tell it where to shoot until it recovers.
        if (EnsignCombat.IsDark(unit)) return;
        Utils.RevealContactToObject(unit, player, "Comms", 3);
        unit._weaponStatus = ObjectBase.WeaponStatus.Free;
    }

    /// <summary>Hold a ship at this speed, the way the game's own formation keeping does.</summary>
    internal static void SetSpeed(ObjectBase unit, float knots)
    {
        unit.SetSpeedCommand(new ConstantSpeed(knots, unit));
        unit._velocityInKnots = knots;
        unit._velocityInUnity = knots * KnotsToUnity;
    }

    /// <summary>
    /// Mark every unknown contact that is in fact hostile as hostile on the
    /// player's plot, as the contact menu's Hostile button does
    /// (MapUnitViewModel). A contact exists only once detected, so this runs
    /// on every tick rather than at spawn; identified contacts are left alone.
    /// </summary>
    internal static void MarkHostiles()
    {
        var player = Globals._playerTaskforce;
        if (player == null) return;
        foreach (var unit in Singleton<ObjectsManager>.Instance._listOfAllObjects)
        {
            if (unit == null || unit.IsDestroyed || !unit.isUnit() || unit._taskforce == null) continue;
            if (player.RelationshipTo(unit._taskforce) != RelationsState.Hostile) continue;
            var contact = unit.GetMapVehicle(player);
            if (contact != null && contact.CurrentRelationship() == RelationsState.Unknown)
                contact.OverrideRelationship(RelationsState.Hostile);
        }
    }

    /// <summary>Re-aim at the player's current position; it keeps moving.</summary>
    internal static void Steer(ObjectBase unit, ObjectBase player)
    {
        var target = player._geoPosition;
        if (unit.IsAirUnit) target._height = unit._geoPosition._height;
        unit.setWaypointTask(target);
    }

    /// <summary>
    /// A point at this distance and bearing. Sea Power's world is a flat
    /// latitude/longitude grid, where a degree of longitude is 60 nm at every
    /// latitude (GeoPosition.GetDistanceInDeg, Utils.longLatToLocalV3), so no
    /// cos(latitude) correction: adding one stretched east-west offsets 2.1x
    /// at 62 N and put "16.8 nm" spawns 30 nm away.
    /// </summary>
    internal static GeoPosition Offset(GeoPosition from, float distanceNm, float bearingDeg)
    {
        var to = from.GeoOffsetByDistanceOnHeading(distanceNm, bearingDeg);
        to._height = 0.0;
        return to;
    }

    static void Place(ObjectBase unit, GeoPosition at)
    {
        unit.transform.position = Utils.longLatToLocalV3(at, Globals._currentCenterTile);
        Physics.SyncTransforms();
        unit._geoPosition = at;
        unit.DesiredAltitude.Value = unit.transform.position.y;
    }
}
