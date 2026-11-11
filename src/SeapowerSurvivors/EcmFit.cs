using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SeaPower;
using UniRx;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// The German ensign: ships with torpedo launchers are refitted with the EA-6B
/// Prowler's AN/ALQ-99, a real game jammer, and give up their torpedoes and
/// torpedo-carrying rockets. The launchers stay in action, so one that shares
/// a magazine with SAMs still fires them. The jammer has no 3D model, only a
/// dummy mount, as a missile's jammer has. The
/// player switches it on and off and aims it as on a Prowler; the Jammers
/// upgrade line, which takes the Torpedoes line's place, widens and extends it.
/// </summary>
/// <remarks>
/// Stock, a jammer's range and arc only reach enemy radars, and only as far as
/// the horizon, while the roll that spoils a missile happens within 3 nm of
/// the missile's target. So this makes the same roll itself (ECM.jamWeapon) for
/// every enemy missile inside the aimed cone, whether or not its seeker is on
/// yet (see <see cref="JamEarly"/>), and fills in the list radars read. It replaces the stock scan, which walks every object on every fixed
/// update per jammer, with one a second. The stock 3 nm roll still works, aimed or not.
/// </remarks>
static class EcmFit
{
    const string SystemName = "AN/ALQ-99";
    const float UnityPerNm = 27.559496f;
    const float BaseReachNm = 30f, ExtremeFactor = 4f;
    const float BaseArc = 30f, MaxArc = 270f;
    const float BaseChance = 0.45f;
    const float BasePowerKw = 11f, HighPowerKw = 200f, BaseGainDb = 5f, HighGainDb = 20f;
    const double SecondLookSeconds = 10.0;
    /// <summary>The height of the dummy mount above the hull, about 30 m: only the sight line's start, as nothing checks it now.</summary>
    const float MountHeightUnity = 0.45f;

    static readonly Dictionary<ObjectBase, SensorSystemECM> fitted = new Dictionary<ObjectBase, SensorSystemECM>();
    static readonly HashSet<SensorSystemECM> ours = new HashSet<SensorSystemECM>();
    static double nextSecondLook;

    internal static void Reset()
    {
        fitted.Clear();
        ours.Clear();
        nextSecondLook = 0.0;
    }

    internal static float ReachNm(Run run, int ahead = 0) =>
        BaseReachNm * Upgrades.Total(run, Upgrades.Jammers, Stat.Range, ahead) * (Upgrades.Has(run, Upgrades.ExtremeRange) ? ExtremeFactor : 1f);

    internal static float Arc(Run run, int ahead = 0) =>
        Mathf.Min(MaxArc, BaseArc * Upgrades.Total(run, Upgrades.Jammers, Stat.Arc, ahead));

    internal static float Chance(Run run, int ahead = 0) =>
        BaseChance * Upgrades.Total(run, Upgrades.Jammers, Stat.JamChance, ahead);

    /// <summary>Run each director tick while the ensign flies.</summary>
    internal static void Tick(Run run)
    {
        var enemies = Singleton<ObjectsManager>.Instance._listOfAllObjects
            .Where(o => o != null && !o.IsDestroyed && o._taskforce == Globals._enemyTaskforce && !o.isSubmerged() && (o.isUnit() || o is Missile))
            .ToList();
        var missiles = enemies.OfType<Missile>().Where(m => !m._externalDestructionNotified).ToList();
        double now = GameTime.missionElapsedTime;
        bool secondLook = Upgrades.Has(run, Upgrades.SecondLook) && now >= nextSecondLook;
        if (secondLook) nextSecondLook = now + SecondLookSeconds;

        foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit).ToList())
        {
            var tubes = Boosts.TorpedoLaunchers(ship).ToList();
            if (tubes.Count == 0) continue;
            foreach (var tube in tubes) Strip(ship, tube);
            // A null entry is a ship whose refit already failed and was reported.
            if (!fitted.TryGetValue(ship, out var jammer)) TryFit(ship, out jammer);
            if (jammer == null) continue;
            Apply(run, jammer);
            if (secondLook) jammer._ecm._ignoredWeapons.Clear();
            Forget(jammer._ecm);
            Work(run, ship, jammer, enemies, missiles);
        }
    }

    /// <summary>
    /// Takes a launcher's torpedo rounds off the ship. Each magazine keeps its
    /// entry at a count of none: a rearm refills to the starting count, and the
    /// entry is how this ship is still known to have had torpedoes.
    /// </summary>
    static void Strip(ObjectBase ship, WeaponSystemLauncher launcher)
    {
        var magazine = launcher._vwp._associatedMagazine;
        foreach (var store in new[] { magazine, launcher._vwp._campaignMagazine })
        {
            if (store == null) continue;
            foreach (var round in store._ammunitionList.Where(a => a?._ap != null && Boosts.TorpedoRound(a._ap)))
            {
                if (!store._isCampaignMagazine && round._ap._magazineAmmoCount > 0)
                    ship.changeAmmunitionAmount(round._ap._ammunitionFileName, -round._ap._magazineAmmoCount);
                round._ap._magazineAmmoCount = 0;
                store._initialAmmoCount[round] = 0;
            }
        }

        bool unloaded = false;
        foreach (var container in launcher._containers)
        {
            if (container._weapons.Any(w => w?._ap != null && Boosts.TorpedoRound(w._ap)))
            {
                container.unload(returnAmmo: false);
                container.ClearPartialPack();
                unloaded = true;
            }
            // Without a magazine, a rearm refills the container to this count.
            if (magazine == null && container._initialAmmunition?._ap != null && Boosts.TorpedoRound(container._initialAmmunition._ap))
                container._initialAmmoCount = 0;
        }
        if (!unloaded) return;
        launcher.setOnWeaponAmmunition(null);
        // A launcher that shares its magazine loads the other rounds, as after a rearm.
        if (magazine != null && magazine.getTotalAmmunitionCount() > 0) launcher.StartReload(instantReload: true);
    }

    static bool TryFit(ObjectBase ship, out SensorSystemECM jammer)
    {
        jammer = null;
        try
        {
            var mount = new GameObject("SurvivorsJammer");
            var parent = ship._obp._dummySensorMountsParent != null ? ship._obp._dummySensorMountsParent.transform : ship.transform;
            mount.transform.SetParent(parent, false);
            mount.transform.localPosition = Vector3.up * MountHeightUnity;

            jammer = new SensorSystemECM(ship, SystemName, mount) { _sectionName = "SurvivorsJammer" };
            // The constructor leaves it at no integrity, which reads as destroyed.
            jammer._maxIntegrity = 1f;
            jammer.CurrentIntegrity = 1f;
            var obp = ship._obp;
            obp._systems.Add(jammer);
            obp._sensorSystems.Add(jammer);
            if (!obp._systemsDictionary.ContainsKey(jammer._sectionName)) obp._systemsDictionary.Add(jammer._sectionName, jammer);
            // Switches it on, adds it to the ship's ECM systems and makes its on/off command.
            jammer.init();

            // A Prowler's jammer does not cover what its enemies' missiles here use, such as Harpoon's Ku band.
            var ep = jammer._ecm._ep;
            ep._frequencies.Clear();
            ep._waveLengths.Clear();
            ep.loadFrequenciesForECM(new[] { "all" });

            fitted[ship] = jammer;
            ours.Add(jammer);
            TestPanel.Report($"{ship.Name.Value}: torpedo launchers refitted as an {SystemName} jammer");
            ShowCone(ship, jammer);
            return true;
        }
        catch (System.Exception e)
        {
            Entry.Log.LogError($"Refitting {ship.Name.Value} with a jammer failed: {e}");
            // Marked as done, so a failure is reported once and not every second.
            fitted[ship] = null;
            jammer = null;
            return false;
        }
    }

    /// <summary>
    /// The game gives a unit its jam cone only when its map marker is built, and
    /// this ship's was built before the refit, so the cone is added here, shown on
    /// the same terms: selected and jamming. A marker built later finds the jammer itself.
    /// </summary>
    static void ShowCone(ObjectBase ship, SensorSystemECM jammer)
    {
        try
        {
            var map = Globals._mainGameViewModel?.Map;
            var marker = map?.MapKnownUnits?.AllUnits?.FirstOrDefault(u => u.Unit?.BaseObject == ship);
            // Since 0.9.0 the map draws from a set of sources, and a cone joins the map's as it is made.
            if (marker == null || map.Sources == null) return;
            var cone = new SeapowerUI.JamConeViewModel(map.Sources, jammer, ship.Position);
            void Sync()
            {
                bool show = marker.Selected && jammer.Jams.Value;
                if (show && !marker.Overlay.Contains(cone)) marker.Overlay.Add(cone);
                else if (!show) marker.Overlay.Remove(cone);
            }
            marker.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(marker.Selected)) Sync(); };
            jammer.Jams.Subscribe(_ => Sync());
        }
        catch (System.Exception e)
        {
            // The jammer still works without its cone on the map.
            Entry.Log.LogError($"Adding the jam cone for {ship.Name.Value} failed: {e}");
        }
    }

    static void Apply(Run run, SensorSystemECM jammer)
    {
        var ep = jammer._ecm._ep;
        bool high = Upgrades.Has(run, Upgrades.HighPower);
        ep._maxRange = ReachNm(run) * UnityPerNm;
        ep._jamConeFov = Arc(run);
        ep._jamChance = Chance(run);
        ep._peakPower = high ? HighPowerKw : BasePowerKw;
        ep._gain = high ? HighGainDb : BaseGainDb;
        ep._gainFactor = Mathf.Pow(10f, ep._gain / 10f);
    }

    /// <summary>A jammer remembers each weapon's roll, but weapon objects are reused: a new missile must not inherit a dead one's result.</summary>
    static void Forget(ECM ecm)
    {
        ecm._ignoredWeapons.RemoveAll(w => w == null || w.IsDestroyed);
        foreach (var gone in ecm._jammedWeapons.Keys.Where(w => w == null || w.IsDestroyed).ToList())
            ecm._jammedWeapons.Remove(gone);
    }

    /// <summary>The bearing the player has aimed this jammer on, or null if it has not been aimed. A sunk target leaves the aim on its last bearing.</summary>
    static float? Axis(ObjectBase ship, SensorSystemECM jammer)
    {
        var target = jammer._associatedTarget;
        if (target != null && target.IsDestroyed)
        {
            jammer._associatedTargetPosition = target._geoPosition;
            jammer._geoPositionTargeted = true;
            jammer._associatedTarget = null;
            target = null;
        }
        if (target != null) return ship._geoPosition.HeadingRangeMeters(target._geoPosition).heading;
        return jammer._geoPositionTargeted ? ship._geoPosition.HeadingRangeMeters(jammer._associatedTargetPosition).heading : (float?)null;
    }

    static void Work(Run run, ObjectBase ship, SensorSystemECM jammer, List<ObjectBase> enemies, List<Missile> missiles)
    {
        var ep = jammer._ecm._ep;
        float? axis = jammer.IsOn.Value && !jammer.Inoperable.Value ? Axis(ship, jammer) : null;
        float reach = ep._maxRange / UnityPerNm;

        bool InCone(ObjectBase o) => axis.HasValue &&
            Spawner.Nm(ship._geoPosition, o._geoPosition) <= reach &&
            Mathf.Abs(Mathf.DeltaAngle(ship._geoPosition.HeadingRangeMeters(o._geoPosition).heading, axis.Value)) <= ep._jamConeFov / 2f;

        // What enemy radars read: a target screened by this jammer is harder to find.
        foreach (var enemy in enemies)
        {
            if (!InCone(enemy)) enemy._ECMSystemsTryingToJamMe.Remove(jammer);
            else if (!enemy._ECMSystemsTryingToJamMe.Contains(jammer)) enemy._ECMSystemsTryingToJamMe.Add(jammer);
        }

        bool hardKill = Upgrades.Has(run, Upgrades.HardKill);
        foreach (var missile in missiles)
        {
            if (missile._seeker == null || !InCone(missile)) continue;
            bool already = jammer._ecm._jammedWeapons.ContainsKey(missile);
            if (!(jammer._ecm.jamWeapon(missile) || JamEarly(jammer._ecm, missile)) || already || hardKill) continue;
            // A missile jammed is a missile stopped, and pays as one shot down: once, even if a SAM then gets it on its way into the sea.
            CombatHooks.PayIntercept(run, missile);
        }
        // Hard kill takes every missile this jammer has turned away, those jammed before it was unlocked too.
        if (hardKill)
            foreach (var jammed in jammer._ecm._jammedWeapons.Keys.Where(w => w != null && w._taskforce == Globals._enemyTaskforce).ToList())
                CombatHooks.Destroy(jammed);
    }

    static readonly System.Func<ECM, WeaponBase, bool> RollJam =
        AccessTools.MethodDelegate<System.Func<ECM, WeaponBase, bool>>(AccessTools.Method(typeof(ECM), "tryToJamAttackingWeapon"));

    /// <summary>
    /// The roll for a radar-homing missile whose seeker is not on yet. The game
    /// only rolls against a seeker that is radiating, and most switch on 10 to
    /// 20 nm from their target, so reach past that did nothing against missiles,
    /// Extreme range least of all. A jammed missile flies into the sea whatever
    /// its seeker is doing, so the same roll, made early, stops it the same way.
    /// Ours covers every band, so there is no frequency to match.
    /// </summary>
    static bool JamEarly(ECM ecm, Missile missile)
    {
        var seeker = missile._seeker;
        if (seeker.isActive() || ecm._ignoredWeapons.Contains(missile) || ecm._jammedWeapons.ContainsKey(missile)) return false;
        bool radar = seeker._guidanceType == AmmunitionParameters.GuidanceType.ActiveRadarHoming ||
                     seeker._guidanceType == AmmunitionParameters.GuidanceType.SemiActiveRadarHoming && missile._guidingRadarSystem != null;
        return radar && RollJam(ecm, missile);
    }

    /// <summary>Our jammers do the stock scan's work once a second in <see cref="Work"/>.</summary>
    [HarmonyPatch(typeof(SensorSystemECM), nameof(SensorSystemECM.OnFixedUpdate))]
    static class NoStockScan
    {
        static bool Prefix(SensorSystemECM __instance) => !ours.Contains(__instance);
    }

    /// <summary>The map's cone copies its range and arc once, when first made; ours grow during the run.</summary>
    [HarmonyPatch(typeof(SeapowerUI.JamConeViewModel), nameof(SeapowerUI.JamConeViewModel.Range), MethodType.Getter)]
    static class ConeRange
    {
        static void Postfix(SensorSystemECM ___ecmSystem, ref float __result)
        {
            if (ours.Contains(___ecmSystem)) __result = ___ecmSystem._ecm._ep._maxRange;
        }
    }

    [HarmonyPatch(typeof(SeapowerUI.JamConeViewModel), nameof(SeapowerUI.JamConeViewModel.FOV), MethodType.Getter)]
    static class ConeArc
    {
        static void Postfix(SensorSystemECM ___ecmSystem, ref float __result)
        {
            if (ours.Contains(___ecmSystem)) __result = ___ecmSystem._ecm._ep._jamConeFov;
        }
    }
}
