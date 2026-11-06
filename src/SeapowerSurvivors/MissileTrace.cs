using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Follows every anti-ship missile our fleet fires and logs one report per
/// round: the values it actually flies with against stock, whether the
/// upgrade loop can see that round at all, each phase, what it locked, how
/// close it came, and how it ended. For telling apart why rounds miss in-game.
/// </summary>
public class MissileTrace : MonoBehaviour
{
    const float ScanEverySeconds = 0.25f;
    const float ShipsEverySeconds = 0.5f;
    const float MetresPerUnit = 67.200066f;
    const float UnityPerNm = 27.559496f;

    sealed class Track
    {
        public double Launched;
        public string Header;
        public ObjectBase IntendedTarget;
        public readonly StringBuilder Events = new StringBuilder();
        public string Stage;
        public bool SeekerOn;
        public ObjectBase Locked;
        public int Locks;
        public Vector3 Last;
        public float ClosestM = float.MaxValue;
        public string ClosestTo;
        public float ClosestToTargetM = float.MaxValue;
        public float TopKnots;
        public float LowestM = float.MaxValue;
        public string Outcome;
    }

    static readonly Dictionary<Missile, Track> tracks = new Dictionary<Missile, Track>();
    static readonly AccessTools.FieldRef<Blastzone, WeaponBase> BlastWeapon = AccessTools.FieldRefAccess<Blastzone, WeaponBase>("_weapon");
    double nextScan, nextShips;
    List<ObjectBase> ships = new List<ObjectBase>();

    internal static void Reset() => tracks.Clear();

    static bool Ours(WeaponBase w) => w._launchPlatform != null && w._launchPlatform._taskforce == Globals._playerTaskforce;

    static string Header(Missile m)
    {
        var ap = m._ap;
        var target = m.CurrentIntendedTargetObject;
        var pos = m.transform.position;
        var launcher = m._weaponSystem as WeaponSystemLauncher;
        string seen = launcher == null ? "no launcher (pylon?)"
            : Boosts.LauncherAmmo(launcher).Contains(ap) ? "in the launcher's upgrade list"
            : "NOT in the launcher's upgrade list";
        string aim = target == null
            ? "blind"
            : $"at {target.Name.Value} ({target._obp?._objectIniName}) {(target.transform.position - pos).magnitude / UnityPerNm:0.0} nm away doing {target.getVelocityInKnots():0} kt";
        return $"Missile {ap._ammunitionFileName} from {m._weaponSystem?._vwp?._systemName ?? "?"} by {m._launchPlatform.Name.Value}, {aim}\n" +
               $"  {seen}; {Boosts.MissileStats(ap)}\n" +
               $"  seeker {ap._seekerActiveRange / UnityPerNm:0.0} nm, {ap._seekerFOV:0}° arc, frame {Time.deltaTime * 1000f:0} ms";
    }

    [HarmonyPatch(typeof(Blastzone), "OnHitUnit")]
    static class Hit
    {
        static void Prefix(Blastzone __instance, object __0)
        {
            if (BlastWeapon(__instance) is Missile missile && tracks.TryGetValue(missile, out var t) && __0 is ObjectBase hit)
                t.Outcome = hit._taskforce == Globals._playerTaskforce ? $"HIT OUR OWN {hit.Name.Value}" : $"HIT {hit.Name.Value}";
        }
    }

    [HarmonyPatch(typeof(WeaponBase), nameof(WeaponBase.destroyObject))]
    static class Ended
    {
        static void Prefix(WeaponBase __instance)
        {
            if (!(__instance is Missile missile) || !tracks.TryGetValue(missile, out var t)) return;
            Finish(missile, t, t.Outcome ?? (__instance.transform.position.y < 0f ? "into the sea"
                : __instance._collisionNoticed ? "hit something that is not a unit"
                : $"destroyed in {__instance._flightStage}"));
        }
    }

    static void Finish(Missile missile, Track t, string outcome)
    {
        tracks.Remove(missile);
        var sb = new StringBuilder(t.Header);
        sb.Append($"\n  flew {GameTime.missionElapsedTime - t.Launched:0.0} s, top speed {t.TopKnots:0} kt, lowest {t.LowestM:0} m, {t.Locks} lock(s)");
        if (t.ClosestTo != null) sb.Append($", closest enemy ship {t.ClosestTo} at {t.ClosestM:0} m");
        if (t.IntendedTarget != null) sb.Append($", closest to its target {t.ClosestToTargetM:0} m");
        sb.Append(t.Events);
        sb.Append($"\n  ended: {outcome}");
        Entry.Log.LogInfo(sb.ToString());
    }

    void Update()
    {
        try
        {
            double now = GameTime.missionElapsedTime;
            if (now >= nextScan)
            {
                nextScan = now + ScanEverySeconds;
                foreach (var o in Singleton<ObjectsManager>.Instance._listOfAllObjects)
                {
                    if (o is Missile m && !m.IsDestroyed && !tracks.ContainsKey(m) && Ours(m) && m._ap != null && Boosts.AntiShipMissile(m._ap))
                    {
                        tracks[m] = new Track
                        {
                            Launched = now,
                            Header = Header(m),
                            IntendedTarget = m.CurrentIntendedTargetObject,
                            Last = m.transform.position,
                        };
                    }
                }
            }
            if (tracks.Count == 0) return;
            if (now >= nextShips)
            {
                nextShips = now + ShipsEverySeconds;
                ships = Singleton<ObjectsManager>.Instance._listOfAllObjects
                    .Where(o => o is Vessel && !o.IsDestroyed && o._taskforce != Globals._playerTaskforce).ToList();
            }
            foreach (var pair in tracks.ToList())
            {
                if (pair.Key == null || !pair.Key.gameObject.activeInHierarchy)
                {
                    Finish(pair.Key, pair.Value, pair.Value.Outcome ?? "vanished");
                    continue;
                }
                Sample(pair.Key, pair.Value, now);
            }
        }
        catch (System.Exception e)
        {
            Entry.Log.LogError($"Missile trace failed: {e}");
            tracks.Clear();
        }
    }

    /// <summary>Distance to the line flown since the last frame: at low frame rates a fast round moves tens of metres a frame.</summary>
    static float PassM(Vector3 from, Vector3 to, Vector3 point)
    {
        var path = to - from;
        float along = path.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(point - from, path) / path.sqrMagnitude) : 0f;
        return (from + path * along - point).magnitude * MetresPerUnit;
    }

    void Sample(Missile m, Track t, double now)
    {
        var pos = m.transform.position;
        t.TopKnots = Mathf.Max(t.TopKnots, m._velocityInKnots);
        t.LowestM = Mathf.Min(t.LowestM, pos.y * MetresPerUnit);
        foreach (var ship in ships)
        {
            if (ship == null) continue;
            float d = PassM(t.Last, pos, ship.transform.position);
            if (d < t.ClosestM) { t.ClosestM = d; t.ClosestTo = ship.Name.Value; }
        }
        if (t.IntendedTarget != null && !t.IntendedTarget.IsDestroyed)
            t.ClosestToTargetM = Mathf.Min(t.ClosestToTargetM, PassM(t.Last, pos, t.IntendedTarget.transform.position));
        t.Last = pos;

        string stage = m._flightStage.ToString();
        if (stage != t.Stage)
        {
            t.Stage = stage;
            t.Events.Append($"\n  {now - t.Launched,5:0.0} s  {stage}, {pos.y * MetresPerUnit:0} m up, {m._velocityInKnots:0} kt{ToTarget(m, t, pos)}");
        }
        var seeker = m._seeker;
        if (seeker == null) return;
        bool on = seeker.isActive();
        if (on != t.SeekerOn)
        {
            t.SeekerOn = on;
            t.Events.Append($"\n  {now - t.Launched,5:0.0} s  seeker {(on ? "on" : "off")}{ToTarget(m, t, pos)}");
        }
        var locked = seeker._lockedOnTarget ?? seeker._strongestSensorEcho?._objectBase;
        if (locked != t.Locked)
        {
            if (locked != null) t.Locks++;
            t.Locked = locked;
            t.Events.Append(locked == null
                ? $"\n  {now - t.Launched,5:0.0} s  lost lock"
                : $"\n  {now - t.Launched,5:0.0} s  locked {locked.Name.Value} {(locked.transform.position - pos).magnitude / UnityPerNm:0.0} nm away, {Off(m, pos, locked):0}° off the nose");
        }
    }

    static float Off(Missile m, Vector3 pos, ObjectBase o)
    {
        var to = o.transform.position - pos;
        return Vector3.Angle(new Vector3(m.transform.forward.x, 0f, m.transform.forward.z), new Vector3(to.x, 0f, to.z));
    }

    static string ToTarget(Missile m, Track t, Vector3 pos) =>
        t.IntendedTarget == null || t.IntendedTarget.IsDestroyed ? ""
        : $"; target {(t.IntendedTarget.transform.position - pos).magnitude / UnityPerNm:0.0} nm, {Off(m, pos, t.IntendedTarget):0}° off the nose";
}
