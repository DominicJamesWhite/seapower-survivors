using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// What each of the fleet's weapons did to enemy ships this run, measured from
/// the game's own damage, for balancing against tools/balance.py. It records
/// shots at ships, blasts that touched one, what each took from the hidden
/// integrity pool and the structure, and which weapon last hit each ship that sank.
/// Rows are split by the ranks held at the time, so hit rate and damage per hit
/// come out per rank from any run. See docs/balance.md for the files.
/// Measuring only: nothing here changes the fight, and a failure in it must not reach the game.
/// </summary>
static class DamageLog
{
    const float UnityPerNm = 27.559496f;
    const double TimelineSeconds = 60.0;
    const float FlushSeconds = 60f;
    /// <summary>Upper edges of the range bands, in nautical miles; the last band is open.</summary>
    static readonly int[] Bands = { 5, 10, 15, 20, 30, 45, 60, 90, 125 };

    readonly struct Key : IEquatable<Key>
    {
        public readonly string Line, Ammo, Target;
        public readonly int LineRank, AccuracyRank, Band;

        public Key(string line, string ammo, string target, int lineRank, int accuracyRank, int band)
        {
            Line = line;
            Ammo = ammo;
            Target = target;
            LineRank = lineRank;
            AccuracyRank = accuracyRank;
            Band = band;
        }

        public bool Equals(Key o) => Band == o.Band && LineRank == o.LineRank && AccuracyRank == o.AccuracyRank &&
                                     Ammo == o.Ammo && Target == o.Target && Line == o.Line;
        public override bool Equals(object obj) => obj is Key k && Equals(k);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = Ammo.GetHashCode() * 397 ^ Target.GetHashCode();
                return (h * 397 ^ LineRank) * 397 ^ AccuracyRank * 31 ^ Band;
            }
        }
    }

    sealed class Tally
    {
        public long Shots, Hits;
        public double Drain, Structure;
        public int Kills;
        public double First = -1.0, Last;

        public void Saw(double now)
        {
            if (First < 0.0) First = now;
            Last = now;
        }

        public void Add(Tally o)
        {
            Shots += o.Shots;
            Hits += o.Hits;
            Drain += o.Drain;
            Structure += o.Structure;
            Kills += o.Kills;
            if (o.First >= 0.0 && (First < 0.0 || o.First < First)) First = o.First;
            if (o.Last > Last) Last = o.Last;
        }
    }

    /// <summary>One ship the fleet has hit: enough to say what sank it, and how.</summary>
    sealed class Victim
    {
        public double FirstHit;
        public Key Last;
        public float LastRangeNm;
        public readonly Dictionary<string, Tally> Lines = new Dictionary<string, Tally>();
    }

    /// <summary>A ship's state as a blast first reached it.</summary>
    struct Before
    {
        public ObjectBase Ship;
        public float Pool, Structure;
    }

    static readonly Dictionary<Key, Tally> tallies = new Dictionary<Key, Tally>();
    static readonly Dictionary<ObjectBase, Victim> victims = new Dictionary<ObjectBase, Victim>();
    static readonly Dictionary<Blastzone, List<Before>> open = new Dictionary<Blastzone, List<Before>>();
    static Run logged;
    static string stem;
    static double nextTimeline;
    static float nextFlush;
    static bool dirty, failed;

    static readonly AccessTools.FieldRef<Blastzone, ObjectBase> BlastPlatform = AccessTools.FieldRefAccess<Blastzone, ObjectBase>("_launchPlatform");
    static readonly AccessTools.FieldRef<Blastzone, AmmunitionParameters> BlastAmmo = AccessTools.FieldRefAccess<Blastzone, AmmunitionParameters>("_ap");
    static readonly AccessTools.FieldRef<Blastzone, Vector3> BlastPosition = AccessTools.FieldRefAccess<Blastzone, Vector3>("_blastPosition");

    static void Fail(Exception e)
    {
        if (failed) return;
        failed = true;
        Entry.Log.LogError($"Damage log stopped: {e}");
    }

    static bool Ours(Run run, ObjectBase platform) => run != null && platform != null && !failed && run.Fleet.Contains(platform);
    static bool EnemyShip(ObjectBase o) => o is Vessel && o._taskforce == Globals._enemyTaskforce;
    static string TypeOf(ObjectBase o) => o._obp?._objectIniName ?? "unknown";

    static int Band(float nm)
    {
        for (int i = 0; i < Bands.Length; i++)
            if (nm < Bands[i]) return i;
        return Bands.Length;
    }

    static string BandName(int band) =>
        band >= Bands.Length ? $"{Bands[Bands.Length - 1]}+" : $"{(band == 0 ? 0 : Bands[band - 1])}-{Bands[band]}";

    /// <summary>The upgrade line a round belongs to, and with it the rank that shaped it.</summary>
    static (string name, Upgrade line) LineOf(ObjectBase platform, AmmunitionParameters ap)
    {
        if (platform.IsAirUnit) return ("Air wing", Upgrades.AirWing);
        switch (ap._type)
        {
            case Ammunition.Type.Projectile: return ("Guns", Upgrades.Guns);
            case Ammunition.Type.RBU: return ("Mortars", Upgrades.Mortars);
            case Ammunition.Type.Torpedo:
            case Ammunition.Type.ASROC: return ("Torpedoes", Upgrades.Torpedoes);
            default:
                return ap._targetType == Ammunition.Target.AAW ? ("SAMs", Upgrades.Sams) : ("Anti-ship", Upgrades.AntiShip);
        }
    }

    static Key KeyFor(Run run, ObjectBase platform, AmmunitionParameters ap, ObjectBase target, float rangeNm)
    {
        var (name, line) = LineOf(platform, ap);
        return new Key(name, ap._ammunitionFileName, TypeOf(target), run.RankOf(line), run.RankOf(Upgrades.Accuracy), Band(rangeNm));
    }

    static Tally TallyFor(Key key)
    {
        if (!tallies.TryGetValue(key, out var tally)) tallies[key] = tally = new Tally();
        return tally;
    }

    static void Shot(Run run, ObjectBase platform, AmmunitionParameters ap, ObjectBase target, int count)
    {
        if (ap == null || !EnemyShip(target) || target.IsDestroyed) return;
        float nm = (target.transform.position - platform.transform.position).magnitude / UnityPerNm;
        var tally = TallyFor(KeyFor(run, platform, ap, target, nm));
        tally.Shots += count;
        tally.Saw(GameTime.missionElapsedTime);
        dirty = true;
    }

    /// <summary>A gun's shells, counted as each barrel fires. A single barrel with several muzzles fires them all at once.</summary>
    [HarmonyPatch(typeof(WeaponSystemGun), "fireBarrel")]
    static class GunShot
    {
        static void Postfix(WeaponSystemGun __instance)
        {
            try
            {
                var run = Run.Current;
                var platform = __instance._baseObject;
                if (!Ours(run, platform)) return;
                int count = __instance._barrels.Count == 1 && !__instance._fireSequential && __instance._spawnPositions.Count > 1
                    ? __instance._spawnPositions.Count : 1;
                Shot(run, platform, __instance._ammoForEngage?._ap, __instance._targetObject, count);
            }
            catch (Exception e) { Fail(e); }
        }
    }

    /// <summary>Missiles, mortar rounds and torpedoes, as each is launched.</summary>
    [HarmonyPatch(typeof(WeaponBase), nameof(WeaponBase.CommonLaunchSettings))]
    static class WeaponShot
    {
        static void Postfix(WeaponBase __instance, ObjectBase targetObject, bool isSubmunition)
        {
            try
            {
                var run = Run.Current;
                if (isSubmunition || !Ours(run, __instance._launchPlatform)) return;
                Shot(run, __instance._launchPlatform, __instance._ap, targetObject, 1);
            }
            catch (Exception e) { Fail(e); }
        }
    }

    /// <summary>A ship this blast reaches, read before it takes any damage. A blast calls this once a collider.</summary>
    [HarmonyPatch(typeof(Blastzone), "OnHitUnit")]
    static class BlastReaches
    {
        static void Prefix(Blastzone __instance, ObjectBase hitObject)
        {
            try
            {
                if (!EnemyShip(hitObject) || hitObject.Compartments == null || !Ours(Run.Current, BlastPlatform(__instance))) return;
                if (!open.TryGetValue(__instance, out var ships)) open[__instance] = ships = new List<Before>(1);
                foreach (var seen in ships)
                    if (seen.Ship == hitObject) return;
                var c = hitObject.Compartments;
                ships.Add(new Before { Ship = hitObject, Pool = c._limitIntegrity, Structure = c.TotalIntegrity });
            }
            catch (Exception e) { Fail(e); }
        }
    }

    /// <summary>The blast is over: what it took from each ship, secondary explosions it set off included.</summary>
    [HarmonyPatch(typeof(Blastzone), nameof(Blastzone.CreateExplosion))]
    static class BlastDone
    {
        static void Postfix(Blastzone __instance)
        {
            try
            {
                if (open.Count == 0 || !open.TryGetValue(__instance, out var ships)) return;
                open.Remove(__instance);
                var run = Run.Current;
                var platform = BlastPlatform(__instance);
                var ap = BlastAmmo(__instance);
                if (run == null || platform == null || ap == null) return;
                double now = GameTime.missionElapsedTime;
                float nm = (BlastPosition(__instance) - platform.transform.position).magnitude / UnityPerNm;
                foreach (var before in ships)
                    Land(run, platform, ap, before, nm, now);
                dirty = true;
            }
            catch (Exception e) { Fail(e); }
        }
    }

    static void Land(Run run, ObjectBase platform, AmmunitionParameters ap, Before before, float nm, double now)
    {
        var ship = before.Ship;
        var c = ship != null ? ship.Compartments : null;
        if (c == null) return;
        float drain = Mathf.Max(0f, before.Pool - c._limitIntegrity);
        float structure = Mathf.Max(0f, before.Structure - c.TotalIntegrity);
        var key = KeyFor(run, platform, ap, ship, nm);
        var tally = TallyFor(key);
        tally.Hits++;
        tally.Drain += drain;
        tally.Structure += structure;
        tally.Saw(now);

        if (!victims.TryGetValue(ship, out var victim)) victims[ship] = victim = new Victim { FirstHit = now };
        victim.Last = key;
        victim.LastRangeNm = nm;
        if (!victim.Lines.TryGetValue(key.Line, out var share)) victim.Lines[key.Line] = share = new Tally();
        share.Hits++;
        share.Drain += drain;
        share.Structure += structure;
    }

    /// <summary>An enemy ship has gone down: credit the last weapon to touch it, and record how it died.</summary>
    internal static void Sunk(ObjectBase ship)
    {
        try
        {
            if (failed || Run.Current == null || !(ship is Vessel) || ship.Compartments == null) return;
            victims.TryGetValue(ship, out var victim);
            if (victim != null) TallyFor(victim.Last).Kills++;
            var c = ship.Compartments;
            double now = GameTime.missionElapsedTime;
            Append("kills", "time_s,target,pool_max,pool_left,structure_max,structure_left,route,seconds_from_first_hit,last_line,last_ammo,last_range_nm,by_line",
                string.Join(",", F(now), TypeOf(ship), F(c._maxLimitIntegrity), F(c._limitIntegrity), F(c._maxTotalIntegrity), F(c.TotalIntegrity),
                    c._limitIntegrity < 0f ? "pool" : "structure_fire_flood",
                    victim != null ? F(now - victim.FirstHit) : "",
                    victim != null ? victim.Last.Line : "not_ours", victim != null ? victim.Last.Ammo : "",
                    victim != null ? F(victim.LastRangeNm) : "",
                    victim == null ? "" : string.Join(";", victim.Lines.Select(p => $"{p.Key}:{p.Value.Hits}/{F(p.Value.Drain)}/{F(p.Value.Structure)}"))));
            victims.Remove(ship);
            dirty = true;
        }
        catch (Exception e) { Fail(e); }
    }

    /// <summary>Called with the director's other per-tick work.</summary>
    internal static void Tick(Run run)
    {
        if (run == null || failed) return;
        try
        {
            if (!ReferenceEquals(run, logged)) Begin(run);
            double now = GameTime.missionElapsedTime;
            if (now >= nextTimeline)
            {
                nextTimeline = now + TimelineSeconds;
                Timeline(run, now);
            }
            if (dirty && Time.unscaledTime >= nextFlush)
            {
                nextFlush = Time.unscaledTime + FlushSeconds;
                Flush();
            }
        }
        catch (Exception e) { Fail(e); }
    }

    /// <summary>Called as a run ends, before its state goes.</summary>
    internal static void Finish()
    {
        if (logged == null) return;
        try
        {
            Flush();
            Entry.Log.LogInfo(Summary());
        }
        catch (Exception e) { Fail(e); }
        logged = null;
    }

    static void Begin(Run run)
    {
        logged = run;
        tallies.Clear();
        victims.Clear();
        open.Clear();
        nextTimeline = 0.0;
        dirty = false;
        string flagship = run.FlagshipType ?? "unknown";
        stem = Path.Combine(Application.persistentDataPath, "SeapowerSurvivors", "damage",
            $"{DateTime.Now:yyyyMMdd-HHmmss} {flagship}{(run.Counts ? "" : " test")}");
        Directory.CreateDirectory(Path.GetDirectoryName(stem));
        Entry.Log.LogInfo($"Damage log: {stem}");
    }

    /// <summary>Running totals by line once a game minute: the share each line has of the fight, and how that moves.</summary>
    static void Timeline(Run run, double now)
    {
        foreach (var line in tallies.GroupBy(p => p.Key.Line))
        {
            var total = new Tally();
            foreach (var p in line) total.Add(p.Value);
            int rank = line.OrderByDescending(p => p.Value.Last).First().Key.LineRank;
            Append("timeline", "time_s,level,line,rank,shots,hits,pool_drain,structure,kills",
                string.Join(",", F(now), run.Level, line.Key, rank, total.Shots, total.Hits, F(total.Drain), F(total.Structure), total.Kills));
        }
    }

    static void Flush()
    {
        dirty = false;
        if (stem == null) return;
        var sb = new StringBuilder("line,ammo,target,line_rank,accuracy_rank,range_nm,shots,hits,pool_drain,structure,kills,first_s,last_s\n");
        foreach (var p in tallies.OrderBy(p => p.Key.Line).ThenBy(p => p.Key.Ammo).ThenBy(p => p.Key.LineRank).ThenBy(p => p.Key.AccuracyRank).ThenBy(p => p.Key.Band))
        {
            var k = p.Key;
            var t = p.Value;
            sb.Append(string.Join(",", k.Line, k.Ammo, k.Target, k.LineRank, k.AccuracyRank, BandName(k.Band),
                t.Shots, t.Hits, F(t.Drain), F(t.Structure), t.Kills, F(t.First), F(t.Last))).Append('\n');
        }
        File.WriteAllText(stem + " hits.csv", sb.ToString());
    }

    static void Append(string file, string header, string row)
    {
        if (stem == null) return;
        string path = $"{stem} {file}.csv";
        File.AppendAllText(path, (File.Exists(path) ? "" : header + "\n") + row + "\n");
    }

    /// <summary>The run so far by round, for the log and the test panel.</summary>
    internal static string Summary()
    {
        var sb = new StringBuilder("Damage by round (hit % can pass 100 where one blast reaches several ships):\n");
        sb.AppendLine("  line        round                   shots    hits   hit%  drain/hit  struct/hit     drain/s  kills");
        foreach (var group in tallies.GroupBy(p => (p.Key.Line, p.Key.Ammo)).OrderByDescending(g => g.Sum(p => p.Value.Drain)))
        {
            var t = new Tally();
            foreach (var p in group) t.Add(p.Value);
            double span = t.Last - t.First;
            string hit = t.Shots > 0 ? (100.0 * t.Hits / t.Shots).ToString("0", CultureInfo.InvariantCulture) : "-";
            double hits = Math.Max(1L, t.Hits);
            sb.AppendLine($"  {group.Key.Line,-11} {group.Key.Ammo,-22} {t.Shots,6} {t.Hits,7} {hit,6} {t.Drain / hits,10:0.0} {t.Structure / hits,11:0.0} {(span > 1.0 ? t.Drain / span : 0.0),11:0.0} {t.Kills,6}");
        }
        return sb.ToString();
    }

    static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
