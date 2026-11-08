using System;
using System.Collections.Generic;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>One step of a refit ladder: an aircraft type and loadout, and the air wing standard that brings it.</summary>
sealed class Rung
{
    public readonly int Standard;
    public readonly string Type, Loadout, Name;

    public Rung(int standard, string type, string loadout, string name)
    {
        Standard = standard;
        Type = type;
        Loadout = loadout;
        Name = name;
    }
}

/// <summary>
/// The Air wing line's refits. Each Refit rank raises the air wing's
/// standard by one, and every strike aircraft below it moves up its ladder
/// to the best rung the standard allows: now, or as soon as it joins.
/// </summary>
/// <remarks>
/// A loadout is built into an aircraft as it is made: each hardpoint reads
/// its stations, racks and sensors from the loadout's section once, and
/// nothing reads it again. So there is no changing one in the air; a refit
/// replaces the aircraft with a new one where it flies, through the same
/// spawn chest flights use, and removes the old one without scoring. An
/// aircraft waits for its refit only while a weapon it guides is still flying,
/// since a Walleye or a Sparrow would be lost with it. A Harpoon flies on
/// without it, and waiting for those takes minutes: a flight in action
/// nearly always has one in the air.
/// </remarks>
static class Refits
{
    static readonly Rung[][] Ladders =
    {
        // US: unguided bombs, TV-guided bombs, then Harpoons, which the A-7E cannot carry.
        new[]
        {
            new Rung(0, "usn_a-7e", "Strike", "A-7E Corsair, Mk 82 bombs"),
            new Rung(1, "usn_a-7e", "StrikePrecision", "A-7E Corsair, Walleye guided bombs"),
            new Rung(2, "usn_a-6e", "AntiShip", "A-6E Intruder, two Harpoons"),
            new Rung(3, "usn_a-6e", "AntiShipHeavy", "A-6E Intruder, four Harpoons"),
        },
        new[]
        {
            new Rung(2, "usn_fa-18a", "AntiShip", "F/A-18A Hornet, two Harpoons"),
            new Rung(3, "usn_fa-18a", "AntiShipHeavy", "F/A-18A Hornet, four Harpoons"),
        },
        // Soviet: the Yak-38's FAB-500s, the Su-24's TV- and laser-guided weapons, then the Backfire's AS-4s.
        new[]
        {
            new Rung(0, "wp_yak-38", "AntiShip", "Yak-38, FAB-500 bombs"),
            new Rung(1, "wp_su-24a", "AntiShip", "Su-24A, AS-14 TV-guided missiles"),
            new Rung(2, "wp_su-24m", "AntiShip", "Su-24M, AS-14 and KAB-500L guided bombs"),
            new Rung(3, "wp_tu-22m2", "AntiShipHeavy", "Tu-22M2 Backfire, three AS-4s"),
        },
        new[]
        {
            new Rung(2, "wp_tu-22m2", "AntiShip", "Tu-22M2 Backfire, two AS-4s"),
            new Rung(3, "wp_tu-22m2", "AntiShipHeavy", "Tu-22M2 Backfire, three AS-4s"),
        },
    };

    /// <summary>Rungs that failed to spawn, never tried again: a bad loadout would otherwise be retried every frame.</summary>
    static readonly HashSet<Rung> broken = new HashSet<Rung>();

    /// <summary>The air wing standard: the Refit ranks taken, and those in the next <paramref name="ahead"/> ranks too.</summary>
    internal static int Standard(Run run, int ahead = 0) =>
        Upgrades.AirWing.Path.Take(run.RankOf(Upgrades.AirWing) + ahead).Count(s => s.Stat == Stat.Refit);

    /// <summary>The ladder an aircraft is on and its rung, or null for one with no ladder: fighters, AWACS.</summary>
    static (Rung[] ladder, int index) Find(ObjectBase aircraft)
    {
        foreach (var ladder in Ladders)
        {
            int index = Array.FindIndex(ladder, r => r.Type == aircraft._obp._objectIniName && r.Loadout == aircraft._obp._loadoutVariant);
            if (index >= 0) return (ladder, index);
        }
        return (null, -1);
    }

    internal static Rung Current(ObjectBase aircraft)
    {
        var (ladder, index) = Find(aircraft);
        return ladder?[index];
    }

    /// <summary>What the aircraft would be refitted as at this standard, or null if it is there already.</summary>
    internal static Rung Next(ObjectBase aircraft, int standard)
    {
        var (ladder, index) = Find(aircraft);
        if (ladder == null) return null;
        int best = Array.FindLastIndex(ladder, r => r.Standard <= standard && !broken.Contains(r));
        return best > index ? ladder[best] : null;
    }

    /// <summary>Refit every aircraft below the standard that has nothing of its own still flying.</summary>
    internal static void Tend(Run run)
    {
        int standard = Standard(run);
        if (standard == 0) return;
        var due = run.AirWing.Keys
            .Where(a => a != null && !a.IsDestroyed && Next(a, standard) != null && !Busy(a))
            .ToList();
        if (due.Count == 0) return;
        for (int i = 0; i < due.Count; i++)
        {
            try { Replace(run, due[i], Next(due[i], standard), i); }
            catch (Exception e) { Entry.Log.LogError($"Refitting {due[i].Name.Value} failed: {e}"); }
        }
        Boosts.ApplyFleet(run);
    }

    /// <summary>True while a weapon that needs it for guidance is still flying. Rounds on its pylons do not count.</summary>
    static bool Busy(ObjectBase aircraft)
    {
        var onPylons = new HashSet<WeaponBase>(aircraft._obp._hardpointWeapons.SelectMany(h => h._weapons));
        return Singleton<ObjectsManager>.Instance._listOfAllWeapons.Any(w =>
            w != null && !w.IsDestroyed && w.gameObject.activeInHierarchy && w._launchPlatform == aircraft && w._ap != null && w._ap._requiresGuidance && !onPylons.Contains(w));
    }

    /// <summary>
    /// A new aircraft of the rung where the old one flies, with its orders'
    /// weapons status and its place in the fleet. A stand-off bomber appears
    /// out where its missiles can be fired instead.
    /// </summary>
    /// <param name="slot">Which of the aircraft refitted together this is, so stand-off bombers each have their own air.</param>
    static void Replace(Run run, ObjectBase old, Rung rung, int slot)
    {
        string was = $"{old.Name.Value} ({Current(old)?.Name})";
        var at = old._geoPosition;
        float altitudeFt = (float)(at._height / Spawner.FeetToUnity);
        if (Escorts.StandsOff(rung.Type))
        {
            at = Escorts.StandOffPosition(run, slot);
            altitudeFt = Mathf.Max(altitudeFt, Escorts.StandOffAltitudeFt);
        }
        var fresh = Spawner.Aircraft(rung.Type, "Squadron1", rung.Loadout, at, altitudeFt, old.transform.eulerAngles.y, Globals._playerTaskforce);
        if (fresh == null)
        {
            broken.Add(rung);
            TestPanel.Report($"Could not refit {was} as {rung.Name} ({rung.Type}, {rung.Loadout})");
            return;
        }
        fresh._unlimitedFuel = true;
        fresh._weaponStatus = old._weaponStatus;
        run.Fleet[run.Fleet.IndexOf(old)] = fresh;
        run.AirWing[fresh] = run.AirWing[old];
        run.AirWing.Remove(old);
        int stores = Pylons.Note(fresh);
        old.destroyObject(score: false);
        TestPanel.Report($"Refit: {was} is now {fresh.Name.Value}, {rung.Name} ({stores} stores)");
    }
}
