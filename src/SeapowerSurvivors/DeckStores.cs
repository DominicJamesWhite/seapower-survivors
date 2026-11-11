using System.Collections.Generic;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// A Kiev's own aircraft are never refitted: the Air wing line's refit ranks
/// load them heavier instead. The game builds an aircraft's weapon list, the
/// one its AI and its weapons menu choose from, only as the aircraft is made,
/// so rounds of a new type hung on it later are never used. Instead each
/// rung is a loadout of its own, merged into the aircraft's file by
/// mod/patches, and an aircraft below its rung is swapped in the air for a
/// fresh one on it, as a refit swaps a chest flight. The fresh one stays the
/// Kiev's: it can land, and the deck takes it back as it would any of its own.
/// </summary>
static class DeckStores
{
    /// <summary>By type, the loadout for each refit rank taken; the first is the deck's own.</summary>
    static readonly Dictionary<string, string[]> Ladders = new Dictionary<string, string[]>
    {
        // Two FAB-500s; then two S-25s inboard; then four FAB-500s; then four heavy FAB-500s.
        ["wp_yak-38"] = new[] { "AntiShip", "SurvivorsAntiShip1", "SurvivorsAntiShip2", Heaviest },
    };

    /// <summary>
    /// The last rung: four FAB-500s like the one before, but each about a
    /// FAB-1500's warhead (43 to 77) over a FAB-1500's blast (25 m to 50 m).
    /// The FAB-1500 itself needs 6,000 ft to drop, which the AI never climbs to.
    /// A blast hits whatever is inside it, the Yak too, but a Yak dive-bombs
    /// and has pulled out well clear by the time its bombs land.
    /// </summary>
    const string Heaviest = "SurvivorsAntiShip3";
    internal const float HeavyDamage = 1.8f, HeavyBlast = 2f;

    /// <summary>True for a deck aircraft on the last rung, whose bombs hit harder and wider.</summary>
    internal static bool Heavy(ObjectBase aircraft) => aircraft.IsAirUnit && aircraft._obp._loadoutVariant == Heaviest;

    /// <summary>Loadouts that came out empty, never tried again: the patch file is missing or did not merge.</summary>
    static readonly HashSet<string> broken = new HashSet<string>();

    /// <summary>
    /// Swap a deck aircraft in the air for one on its rung, if it is on a
    /// ladder, below its rung and not guiding a weapon. Returns the aircraft
    /// that flies on: the fresh one, or the same one.
    /// </summary>
    internal static ObjectBase Tend(Run run, ObjectBase aircraft)
    {
        if (!Ladders.TryGetValue(aircraft._obp._objectIniName, out var ladder)) return aircraft;
        int rung = System.Array.IndexOf(ladder, aircraft._obp._loadoutVariant);
        if (rung < 0) return aircraft;
        int due = Mathf.Min(Refits.RefitRanks(run), ladder.Length - 1);
        if (rung >= due || broken.Contains(ladder[due]) || Refits.Busy(aircraft)) return aircraft;
        try { return Replace(run, aircraft, ladder[due]) ?? aircraft; }
        catch (System.Exception e)
        {
            broken.Add(ladder[due]);
            Entry.Log.LogError($"Deck stores: swapping {aircraft.Name.Value} onto {ladder[due]} failed: {e}");
            return aircraft;
        }
    }

    static ObjectBase Replace(Run run, ObjectBase old, string loadout)
    {
        string was = $"{old.Name.Value} ({old._obp._loadoutVariant})";
        var at = old._geoPosition;
        float altitudeFt = (float)(at._height / Spawner.FeetToUnity);
        var fresh = Spawner.Aircraft(old._obp._objectIniName, old._obp._objectVariantReference, loadout, at, altitudeFt,
                                     Spawner.TrueHeading(old), Globals._playerTaskforce);
        if (fresh == null)
        {
            broken.Add(loadout);
            TestPanel.Report($"Deck stores: could not spawn {old._obp._objectIniName} on {loadout}");
            return null;
        }
        // A loadout the game could not find spawns with bare pylons: keep the old one flying instead.
        int stores = fresh._obp._hardpointWeapons.Sum(h => h._weapons.Count(w => w != null));
        if (stores == 0)
        {
            broken.Add(loadout);
            fresh.destroyObject(score: false);
            TestPanel.Report($"Deck stores: {loadout} came out empty; is mod/patches/survivors_{old._obp._objectIniName}.ini deployed?");
            return null;
        }
        fresh._weaponStatus = old._weaponStatus;
        fresh.Rebase(old._homeBase);
        try { fresh.Name.Value = old.Name.Value; } catch { }
        int index = run.Fleet.IndexOf(old);
        if (index >= 0) run.Fleet[index] = fresh;
        else run.Fleet.Add(fresh);
        Pylons.Forget(old);
        Pylons.Note(fresh);
        old._homeBase?._childAircraft.Remove(old);
        old.destroyObject(score: false);
        TestPanel.Report($"Deck stores: {was} is now on {loadout} ({stores} stores)");
        return fresh;
    }
}
