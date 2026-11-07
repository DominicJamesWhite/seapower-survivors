using System.Collections.Generic;
using System.Linq;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>What a ship or flight brings to the fleet, for choosing offers that fill a gap.</summary>
[System.Flags]
enum Role { None = 0, Guns = 1, Sams = 2, AntiShip = 4, Ciws = 8, AirCover = 16, Awacs = 32, AirStrike = 64 }

/// <summary>A ship, or a flight of aircraft, that a boss chest can offer.</summary>
sealed class Escort
{
    public string Name, Type;
    public int Tier;
    public Role Roles;
    /// <summary>For a flight: its loadout, how many aircraft and at what height. Null for a ship.</summary>
    public string Loadout;
    public int Count = 1;
    public float AltitudeFt;

    public bool IsAir => Loadout != null;

    public Escort(string name, string type, int tier, Role roles)
    {
        Name = name;
        Type = type;
        Tier = tier;
        Roles = roles;
    }

    public static Escort Flight(string name, string type, int tier, Role roles, string loadout, int count, float altitudeFt) =>
        new Escort(name, type, tier, roles) { Loadout = loadout, Count = count, AltitudeFt = altitudeFt };
}

/// <summary>
/// Ships and aircraft that join the fleet from boss chests: three offers from
/// the player's own navy, drawn from one pool of ships and flights, of a tier
/// that rises through the run, with at least one that brings something the
/// fleet lacks. They join the player's task force under the player's command,
/// and every upgrade taken applies to them too. Only the starting ship's loss
/// ends the run.
/// </summary>
/// <remarks>
/// Flights have no carrier to go home to, so they fly on unlimited fuel and
/// are left to the game's own routing when idle. With no home base the
/// game never sends them back for fuel or for running out of weapons; level-ups
/// rearm them in the air with the rest of the fleet (see <see cref="Pylons"/>).
/// </remarks>
static class Escorts
{
    const int Offers = 3;
    /// <summary>Joining flights appear this far from the flagship, off its beam.</summary>
    const float JoinDistanceNm = 1.5f;
    /// <summary>Game time between air resupplies once the Air wing line has it, before its ranks shorten it.</summary>
    internal const float ResupplySeconds = 240f;

    static readonly Escort[] Us =
    {
        new Escort("Gearing FRAM II destroyer", "usn_dd_gearing_fram2", 1, Role.Guns),
        new Escort("Knox frigate", "usn_ff_knox_84", 1, Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Oliver Hazard Perry frigate", "usn_ffg_oliver_hazard_perry", 1, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Charles F. Adams destroyer", "usn_ddg_adams_early", 1, Role.Sams | Role.Guns),
        new Escort("Spruance destroyer", "usn_dd_spruance", 2, Role.Guns | Role.Sams | Role.AntiShip | Role.Ciws),
        new Escort("Kidd destroyer", "usn_ddg_kidd", 2, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Belknap cruiser", "usn_cg_belknap", 2, Role.Sams | Role.Guns | Role.AntiShip),
        new Escort("Leahy cruiser", "usn_cg_leahy", 2, Role.Sams | Role.AntiShip),
        new Escort("Ticonderoga cruiser", "usn_cg_ticonderoga", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Virginia cruiser", "usn_cgn_virginia", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Long Beach cruiser", "usn_cgn_long_beach_83", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Iowa battleship", "usn_bb_iowa", 3, Role.Guns | Role.AntiShip | Role.Ciws),
        Escort.Flight("F-4J Phantom CAP pair", "usn_f-4j", 1, Role.AirCover, "AirToAir", 2, 20000f),
        Escort.Flight("A-7E Corsair strike pair", "usn_a-7e", 1, Role.AirStrike, "Strike", 2, 10000f),
        Escort.Flight("E-2C Hawkeye AWACS", "usn_e-2c", 2, Role.Awacs, "AEW", 1, 25000f),
        Escort.Flight("F-14A Tomcat CAP pair", "usn_f-14a", 2, Role.AirCover, "AirToAir", 2, 25000f),
        Escort.Flight("A-6E Intruder Harpoon pair", "usn_a-6e", 2, Role.AirStrike, "AntiShip", 2, 10000f),
        Escort.Flight("F-14A Tomcat Phoenix flight", "usn_f-14a", 3, Role.AirCover, "AirToAirLongRange", 4, 30000f),
        Escort.Flight("A-6E Intruder Harpoon flight", "usn_a-6e", 3, Role.AirStrike, "AntiShip", 4, 10000f),
        Escort.Flight("F/A-18A Hornet strike flight", "usn_fa-18a", 3, Role.AirStrike, "AntiShip", 4, 15000f),
    };

    static readonly Escort[] Soviet =
    {
        new Escort("Petya II light frigate", "wp_skr_petya2", 1, Role.Guns),
        new Escort("Krivak I frigate", "wp_skr_krivak1", 1, Role.Sams | Role.Guns),
        new Escort("Kashin destroyer", "wp_bpk_kashin_mod", 1, Role.Sams | Role.Guns | Role.AntiShip),
        new Escort("Kanin destroyer", "wp_bpk_kanin", 1, Role.Sams | Role.Guns),
        new Escort("Kynda cruiser", "wp_rkr_kynda", 2, Role.Sams | Role.Guns | Role.AntiShip),
        new Escort("Kresta I cruiser", "wp_rkr_kresta1", 2, Role.Sams | Role.Guns | Role.AntiShip),
        new Escort("Sovremenny destroyer", "wp_em_sovremenny", 2, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Udaloy destroyer", "wp_bpk_udaloy", 2, Role.Sams | Role.Guns | Role.Ciws),
        new Escort("Kara cruiser", "wp_bpk_kara", 3, Role.Sams | Role.Guns | Role.Ciws),
        new Escort("Slava cruiser", "wp_rkr_slava", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Kirov battlecruiser", "wp_rkr_kirov", 3, Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws),
        new Escort("Sverdlov cruiser", "wp_kr_sverdlov", 3, Role.Guns | Role.Ciws),
        Escort.Flight("MiG-23MS CAP pair", "wp_mig-23ms", 1, Role.AirCover, "AirToAir", 2, 20000f),
        Escort.Flight("Yak-38 strike pair", "wp_yak-38", 1, Role.AirStrike, "AntiShip", 2, 5000f),
        Escort.Flight("Tu-95RT Bear-D scout", "wp_tu-95rt", 2, Role.Awacs, "Recon", 1, 30000f),
        Escort.Flight("MiG-23MLD CAP pair", "wp_mig-23mld", 2, Role.AirCover, "AirToAir", 2, 25000f),
        Escort.Flight("Su-24A strike pair", "wp_su-24a", 2, Role.AirStrike, "AntiShip", 2, 8000f),
        Escort.Flight("MiG-25PD interceptor flight", "wp_mig-25pd", 3, Role.AirCover, "AirToAirLongRange", 4, 35000f),
        Escort.Flight("Su-24M strike flight", "wp_su-24m", 3, Role.AirStrike, "AntiShip", 4, 8000f),
        Escort.Flight("Tu-22M2 Backfire pair", "wp_tu-22m2", 3, Role.AirStrike, "AntiShip", 2, 30000f),
    };

    /// <summary>Chests at phases 3, 6, 9 and 12 offer tiers 1, 2, 2 and 3.</summary>
    internal static int TierFor(int phase) => phase < 5 ? 1 : phase < 10 ? 2 : 3;

    /// <summary>What the fleet afloat and aloft can already do.</summary>
    internal static Role FleetRoles(Run run)
    {
        var roles = Role.None;
        foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed))
        {
            if (run.AirWing.TryGetValue(ship, out var flight))
            {
                roles |= flight;
                continue;
            }
            if (Boosts.Guns(ship).Any()) roles |= Role.Guns;
            if (Boosts.SamLaunchers(ship).Any()) roles |= Role.Sams;
            if (Boosts.AntiShipLaunchers(ship).Any()) roles |= Role.AntiShip;
            if (ship._obp._ciwsWeapons.Any()) roles |= Role.Ciws;
        }
        return roles;
    }

    /// <summary>The ships and flights of the flagship's own navy.</summary>
    internal static Escort[] NavyOf(string flagshipType) => flagshipType.StartsWith("wp_") ? Soviet : Us;

    /// <summary>
    /// Three ships or flights of the tier, the first chosen to bring something
    /// the fleet lacks, if any does. A reroll passes the cards it replaces, to lean away from them.
    /// </summary>
    internal static List<Escort> Offer(Run run, int tier, ICollection<Escort> shown = null)
    {
        var pool = LevelUpScreen.Shuffled(NavyOf(run.Ship._obp._objectIniName).Where(e => e.Tier == tier).Select(WithEnsigns).Where(e => e != null), shown).ToList();
        var missing = Role.Sams | Role.Guns | Role.AntiShip | Role.Ciws | Role.AirCover | Role.Awacs | Role.AirStrike;
        missing &= ~FleetRoles(run);
        var offer = new List<Escort>();
        var filler = pool.FirstOrDefault(e => (e.Roles & missing) != 0);
        if (filler != null) offer.Add(filler);
        offer.AddRange(pool.Where(e => e != filler).Take(Offers - offer.Count));
        return offer;
    }

    /// <summary>The Argentine ensign's strike flights, by aircraft: the same aircraft and number, on its own unguided loadout. Tu-22M2s have none.</summary>
    static readonly Dictionary<string, (string loadout, string weapon)> BombLoadouts = new Dictionary<string, (string, string)>
    {
        ["usn_a-6e"] = ("Strike", "Mk 83 bombs"),
        ["usn_fa-18a"] = ("Strike", "Mk 83 bombs"),
        ["wp_su-24a"] = ("Strike", "FAB-250 bombs"),
        ["wp_su-24m"] = ("Strike", "FAB-250 bombs"),
    };

    /// <summary>The same object each time, as a reroll leans away from cards by identity.</summary>
    static readonly Dictionary<Escort, Escort> bombVersions = new Dictionary<Escort, Escort>();

    /// <summary>
    /// What a chest offers in place of this escort under the ensigns flown: itself,
    /// a bomb-armed version, or null if it cannot be offered. A-7Es and Yak-38s
    /// already carry only iron bombs, and every other flight here is not for strike.
    /// </summary>
    static Escort WithEnsigns(Escort escort)
    {
        if (!Ensigns.Active(Ensigns.IronBombs) || (escort.Roles & Role.AirStrike) == 0 || escort.Type == "usn_a-7e" || escort.Type == "wp_yak-38") return escort;
        if (!BombLoadouts.TryGetValue(escort.Type, out var bombs)) return null;
        if (!bombVersions.TryGetValue(escort, out var version))
        {
            string name = escort.Name.Replace("Harpoon", "strike") + ", " + bombs.weapon;
            bombVersions[escort] = version = Escort.Flight(name, escort.Type, escort.Tier, escort.Roles, bombs.loadout, escort.Count, escort.AltitudeFt);
        }
        return version;
    }

    /// <summary>A ship or flight taken from a chest: twice over, under the New Zealand ensign.</summary>
    internal static void JoinFromChest(Run run, Escort escort)
    {
        int copies = run.Hoisted.Contains(Ensigns.ChestsTwice) ? Ensigns.ChestCopies : 1;
        for (int i = 0; i < copies; i++) Join(run, escort, i);
    }

    /// <summary>What a card says the ship would add.</summary>
    /// <summary>What an escort would bring that the fleet has none of.</summary>
    internal static Role Missing(Run run, Escort escort) => escort.Roles & ~FleetRoles(run);

    /// <summary>Roles as a card lists them.</summary>
    internal static string RoleList(Role roles)
    {
        string list = Describe(roles).Replace("anti-ship", "anti-ship missiles");
        return char.ToUpperInvariant(list[0]) + list.Substring(1);
    }

    internal static string Describe(Role roles) =>
        roles.ToString().Replace("Sams", "SAMs").Replace("AntiShip", "anti-ship").Replace("Ciws", "CIWS")
            .Replace("AirCover", "air cover").Replace("Awacs", "AWACS").Replace("AirStrike", "air strike");

    /// <summary>Spawn the ship on an open station in the flagship's formation, or the flight beside it, under the player's command, with every upgrade the run has taken.</summary>
    /// <param name="copy">Which of several flights joining together this is, so each has its own air. Ships find their own water.</param>
    internal static void Join(Run run, Escort escort, int copy = 0)
    {
        // The Greek ensign: the first escort taken comes as five, and is the last.
        if (run.Hoisted.Contains(Ensigns.FiveOfAKind) && !run.EscortChosen)
        {
            run.EscortChosen = true;
            run.ShipChests.Clear();
            for (int i = 0; i < Ensigns.EscortCopies; i++) Join(run, escort, i);
            return;
        }
        if (escort.IsAir)
        {
            JoinFlight(run, escort, copy);
            return;
        }
        var flagship = run.Ship;
        var formation = Formations.Of(run);
        var slot = Formations.FindStation(formation);
        var ship = Spawner.Ship(escort.Type, slot.At, flagship.transform.eulerAngles.y, Globals._playerTaskforce, Launcher.FitOf(escort.Type) ?? "default");
        if (ship == null)
        {
            TestPanel.Report($"Could not spawn {escort.Name} ({escort.Type})");
            return;
        }
        if (flagship is Vessel vessel) ship.setTelegraph(vessel._telegraph);
        ship._weaponStatus = flagship._weaponStatus;
        Formations.Take(formation, ship, slot);
        run.Fleet.Add(ship);
        Boosts.ApplyFleet(run);
        TestPanel.Report($"{escort.Name} {ship.Name.Value} joins the fleet");
    }

    static void JoinFlight(Run run, Escort escort, int copy)
    {
        var flagship = run.Ship;
        float heading = flagship.transform.eulerAngles.y;
        int joined = 0, stores = 0;
        for (int i = 0; i < escort.Count; i++)
        {
            // Abreast, a mile apart, off the flagship's starboard beam.
            var at = Spawner.Offset(flagship._geoPosition, JoinDistanceNm + i + copy * escort.Count, (heading + 90f) % 360f);
            var aircraft = Spawner.Aircraft(escort.Type, "Squadron1", escort.Loadout, at, escort.AltitudeFt, heading, Globals._playerTaskforce);
            if (aircraft == null) continue;
            aircraft._unlimitedFuel = true;
            aircraft._weaponStatus = flagship._weaponStatus;
            run.Fleet.Add(aircraft);
            run.AirWing[aircraft] = escort.Roles;
            stores += Pylons.Note(aircraft);
            joined++;
        }
        if (joined == 0)
        {
            TestPanel.Report($"Could not spawn {escort.Name} ({escort.Type}, {escort.Loadout})");
            return;
        }
        // Fighters are what reach stand-off bombers, so their raids may now come.
        if ((escort.Roles & Role.AirCover) != 0) run.Has.Add(Capability.AirCover);
        Boosts.ApplyFleet(run);
        TestPanel.Report($"{escort.Name} joins the fleet ({joined}/{escort.Count} aircraft, {stores} stores)");
    }

    internal static void Tend(Run run)
    {
        Resupply(run);
        Refits.Tend(run);
        TendDeckAircraft(run);
    }

    /// <summary>
    /// Aircraft the player launches from a fleet ship's own deck (a Kiev's)
    /// are fleet as they appear, so every upgrade reaches them as it does a
    /// chest flight. They are not the air wing: they go home to rearm, and
    /// the game hangs fresh rounds on them there, so the upgrades are written
    /// in again every tick.
    /// </summary>
    static void TendDeckAircraft(Run run)
    {
        run.Fleet.RemoveAll(s => s == null);
        var deck = Singleton<ObjectsManager>.Instance._listOfAllPlayerAircraftObjects
            .Where(a => a != null && !a.IsDestroyed && !run.AirWing.ContainsKey(a) && a._homeBase != null && run.Fleet.Contains(a._homeBase))
            .ToList();
        if (deck.Count == 0) return;
        foreach (var aircraft in deck.Where(a => !run.Fleet.Contains(a)))
        {
            run.Fleet.Add(aircraft);
            Entry.Log.LogInfo($"{aircraft.Name.Value} ({aircraft._obp._objectIniName}) from {aircraft._homeBase.Name.Value}'s deck joins the fleet");
        }
        Boosts.ApplyTo(run, deck);
    }

    /// <summary>
    /// Air resupply, from the Air wing line: every few minutes the air wing's
    /// pylons are rehung as a level-up rehangs them, with the run's upgrades
    /// written into the fresh rounds. The first comes as soon as it is taken.
    /// </summary>
    static void Resupply(Run run)
    {
        if (!Upgrades.Has(run, Upgrades.AirWing, Stat.Resupply)) return;
        double now = SeaPower.GameTime.missionElapsedTime;
        if (now - run.LastResupply < ResupplySeconds * Upgrades.Total(run, Upgrades.AirWing, Stat.Reload)) return;
        run.LastResupply = now;
        int stores = run.AirWing.Keys.Sum(Pylons.Reload);
        if (stores == 0) return;
        Boosts.ApplyFleet(run);
        TestPanel.Report($"Air resupply: {stores} store(s) hung on the air wing's pylons");
    }
}
