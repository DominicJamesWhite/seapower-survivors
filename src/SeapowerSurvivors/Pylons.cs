using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Rearms flights in the air. The game rearms aircraft only on deck:
/// RearmAll refills magazines and launchers, but the hardpoints aircraft
/// carry their stores on have no refill. So each flight's stores are noted
/// as it joins, pylon by pylon, and a level-up hangs a fresh round wherever
/// one has gone, through the same call the hardpoint loads them with.
/// </summary>
static class Pylons
{
    sealed class Station
    {
        public WeaponSystemHardpoint Hardpoint;
        public string Ammo;
        public GameObject Parent;
        public Vector3 Position;
        public Quaternion Rotation;
        /// <summary>The round hanging there now; once it leaves the hardpoint's list, it has been fired.</summary>
        public WeaponBase Round;
    }

    static readonly ConditionalWeakTable<ObjectBase, List<Station>> stations = new ConditionalWeakTable<ObjectBase, List<Station>>();

    /// <summary>
    /// Takes the ammunition file, the station's position and rotation, its
    /// parent and the parameters to add the round's mass to. The position is
    /// worked out from the airframe's pose as built, so it is set again after.
    /// </summary>
    static readonly MethodInfo CreateRound = AccessTools.Method(typeof(WeaponSystemHardpoint), "createAmmunitionObjectInstance");

    /// <summary>Note every round on the aircraft's pylons, where it hangs. Returns how many.</summary>
    internal static int Note(ObjectBase aircraft)
    {
        var list = new List<Station>();
        foreach (var hardpoint in aircraft._obp._weaponSystems.OfType<WeaponSystemHardpoint>())
            foreach (var round in hardpoint._weapons.Where(w => w != null))
                list.Add(new Station
                {
                    Hardpoint = hardpoint,
                    Ammo = round._ap._ammunitionFileName,
                    Parent = round.transform.parent != null ? round.transform.parent.gameObject : null,
                    Position = round.transform.localPosition,
                    Rotation = round.transform.localRotation,
                    Round = round,
                });
        stations.Remove(aircraft);
        stations.Add(aircraft, list);
        return list.Count;
    }

    internal static bool Noted(ObjectBase aircraft) => stations.TryGetValue(aircraft, out _);

    /// <summary>
    /// Drop what was noted for the aircraft. A deck aircraft is rearmed on deck
    /// with whatever loadout it is given there, so it is noted again at takeoff.
    /// </summary>
    internal static void Forget(ObjectBase aircraft) => stations.Remove(aircraft);

    /// <summary>Hang a fresh round on every station that has fired. Returns how many.</summary>
    internal static int Reload(ObjectBase aircraft)
    {
        if (aircraft == null || aircraft.IsDestroyed || !stations.TryGetValue(aircraft, out var list)) return 0;
        // Fired rounds go back to the pool and can be handed out again, so
        // which stations are empty is settled before any is refilled.
        var empty = list.Where(s => !s.Hardpoint.Inoperable.Value && !s.Hardpoint._weapons.Contains(s.Round)).ToList();
        int loaded = 0;
        foreach (var station in empty)
        {
            var hardpoint = station.Hardpoint;
            int before = hardpoint._weapons.Count;
            CreateRound.Invoke(hardpoint, new object[] { station.Ammo, Vector3.zero, Vector3.zero, station.Parent, aircraft._obp });
            if (hardpoint._weapons.Count == before) continue;
            var round = hardpoint._weapons[hardpoint._weapons.Count - 1];
            round.transform.localPosition = station.Position;
            round.transform.localRotation = station.Rotation;
            station.Round = round;
            hardpoint._isEmpty = false;
            loaded++;
        }
        // The AI picks targets by what the aircraft carries, and the game
        // only works that out again when an engagement ends, which an empty
        // aircraft never starts.
        if (loaded > 0) aircraft._currentLoadoutCapabilities = aircraft.GetLoadoutCapabilities();
        return loaded;
    }
}
