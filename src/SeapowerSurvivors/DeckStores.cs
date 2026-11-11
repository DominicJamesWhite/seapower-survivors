using System.Collections.Generic;
using System.Linq;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>One step of a deck aircraft's ladder: the loadout, what the mod makes its bombs do, and what the card says it carries.</summary>
sealed class DeckRung
{
    public readonly string Loadout, Text;
    public readonly float Damage, Blast;

    public DeckRung(string loadout, string text = null, float damage = 1f, float blast = 1f)
    {
        Loadout = loadout;
        Text = text;
        Damage = damage;
        Blast = blast;
    }
}

/// <summary>
/// A carrier's own aircraft are never refitted: the Air wing line's refit
/// ranks load her strike aircraft heavier instead. The game builds an
/// aircraft's weapon list, the one its AI and its weapons menu choose from,
/// only as the aircraft is made, so rounds of a new type hung on it later are
/// never used. Instead each rung is a loadout of its own, the mod's merged
/// into the aircraft's file by mod/patches, and an aircraft below its rung is
/// swapped in the air for a fresh one on it, as a refit swaps a chest flight.
/// The fresh one stays the carrier's: it can land, and the deck takes it back
/// as it would any of its own.
/// </summary>
/// <remarks>
/// A Yak-38 has empty stations to fill, so its rungs hang more on it. A Sea
/// Harrier's and a Skyhawk's bomb loads are already all their stations are
/// known to take, so their later rungs carry the same bombs and the mod makes
/// them hit harder (see <see cref="Bombs"/>).
/// </remarks>
static class DeckStores
{
    /// <summary>
    /// The last Yak-38 rung: four FAB-500s like the one before, but each about a
    /// FAB-1500's warhead (43 to 77) over a FAB-1500's blast (25 m to 50 m).
    /// The FAB-1500 itself needs 6,000 ft to drop, which the AI never climbs to.
    /// A blast hits whatever is inside it, the Yak too, but a Yak dive-bombs
    /// and has pulled out well clear by the time its bombs land.
    /// </summary>
    const float HeavyDamage = 1.8f, HeavyBlast = 2f;

    /// <summary>By type, the rung for each refit rank taken; the first is the deck's own loadout.</summary>
    static readonly Dictionary<string, DeckRung[]> Ladders = new Dictionary<string, DeckRung[]>
    {
        ["wp_yak-38"] = new[]
        {
            new DeckRung("AntiShip"),
            new DeckRung("SurvivorsAntiShip1", "two S-25 rockets inboard of their two FAB-500s"),
            new DeckRung("SurvivorsAntiShip2", "four FAB-500s"),
            new DeckRung("SurvivorsAntiShip3", "four FAB-500s that hit 1.8× as hard over twice the blast radius", HeavyDamage, HeavyBlast),
        },
        ["rn_frs1"] = new[]
        {
            new DeckRung("Strike"),
            new DeckRung("SurvivorsStrike1", "1,000 lb bombs that hit 1.4× as hard", 1.4f),
            new DeckRung("SurvivorsStrike2", "1,000 lb bombs that hit twice as hard", 2f),
            new DeckRung("SurvivorsStrike3", "1,000 lb bombs that hit 3× as hard over twice the blast radius", 3f, HeavyBlast),
        },
        // The first step is the Skyhawk's own heavier loadout: a third bomb where the tank hung.
        ["ara_a-4q"] = new[]
        {
            new DeckRung("Strike"),
            new DeckRung("StrikeHeavy", "three 1,000 lb bombs, not two and a tank"),
            new DeckRung("SurvivorsStrike2", "three 1,000 lb bombs that hit 1.5× as hard", 1.5f),
            new DeckRung("SurvivorsStrike3", "three 1,000 lb bombs that hit 2.5× as hard over twice the blast radius", 2.5f, HeavyBlast),
        },
    };

    static DeckRung RungOf(ObjectBase aircraft) =>
        aircraft.IsAirUnit && Ladders.TryGetValue(aircraft._obp._objectIniName, out var ladder)
            ? ladder.FirstOrDefault(r => r.Loadout == aircraft._obp._loadoutVariant)
            : null;

    /// <summary>What the mod makes of the bombs on this aircraft's loadout: damage and blast radius, as factors. Only the mod's own loadouts change either.</summary>
    internal static (float damage, float blast) Bombs(ObjectBase aircraft) =>
        RungOf(aircraft) is DeckRung rung ? (rung.Damage, rung.Blast) : (1f, 1f);

    /// <summary>What a refit rank's card says of the run's carrier: null in any other flagship.</summary>
    internal static string Reading(Run run, int refits)
    {
        var carrier = Escorts.CarrierOf(run);
        if (carrier?.DeckStrike == null || !Ladders.TryGetValue(carrier.DeckStrike, out var ladder)) return null;
        return $"{carrier.Name}'s {carrier.DeckStrikeName} carry {ladder[Mathf.Min(refits, ladder.Length - 1)].Text}";
    }

    /// <summary>Loadouts that came out empty, never tried again: the patch file is missing or did not merge.</summary>
    static readonly HashSet<string> broken = new HashSet<string>();

    /// <summary>
    /// Swap a deck aircraft in the air for one on its rung, if it is on a
    /// ladder, below its rung and not guiding a weapon. Returns the aircraft
    /// that flies on: the fresh one, or the same one.
    /// </summary>
    internal static ObjectBase Tend(Run run, ObjectBase aircraft)
    {
        string type = aircraft._obp._objectIniName;
        if (!Ladders.TryGetValue(type, out var ladder)) return aircraft;
        int rung = System.Array.FindIndex(ladder, r => r.Loadout == aircraft._obp._loadoutVariant);
        if (rung < 0) return aircraft;
        int due = Mathf.Min(Refits.RefitRanks(run), ladder.Length - 1);
        // A rung whose loadout would not spawn is passed over for the best one below it that will.
        while (due > rung && broken.Contains(Key(type, ladder[due].Loadout))) due--;
        if (rung >= due || Refits.Busy(aircraft)) return aircraft;
        try { return Replace(run, aircraft, ladder[due].Loadout) ?? aircraft; }
        catch (System.Exception e)
        {
            broken.Add(Key(type, ladder[due].Loadout));
            Entry.Log.LogError($"Deck stores: swapping {aircraft.Name.Value} onto {ladder[due].Loadout} failed: {e}");
            return aircraft;
        }
    }

    static string Key(string type, string loadout) => type + ":" + loadout;

    static ObjectBase Replace(Run run, ObjectBase old, string loadout)
    {
        string type = old._obp._objectIniName;
        string was = $"{old.Name.Value} ({old._obp._loadoutVariant})";
        var at = old._geoPosition;
        float altitudeFt = (float)(at._height / Spawner.FeetToUnity);
        var fresh = Spawner.Aircraft(type, old._obp._objectVariantReference, loadout, at, altitudeFt,
                                     Spawner.TrueHeading(old), Globals._playerTaskforce);
        if (fresh == null)
        {
            broken.Add(Key(type, loadout));
            TestPanel.Report($"Deck stores: could not spawn {type} on {loadout}");
            return null;
        }
        // A loadout the game could not find spawns with bare pylons: keep the old one flying instead.
        int stores = fresh._obp._hardpointWeapons.Sum(h => h._weapons.Count(w => w != null));
        if (stores == 0)
        {
            broken.Add(Key(type, loadout));
            fresh.destroyObject(score: false);
            TestPanel.Report($"Deck stores: {loadout} came out empty; is mod/patches/survivors_{type}.ini deployed?");
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
