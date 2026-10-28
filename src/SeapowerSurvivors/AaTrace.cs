using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Follows every shell our ships fire at an aircraft, from the muzzle to
/// whatever ends it, and logs a tally every ten seconds of game time: how
/// many each gun fired, how close they came, and what stopped them. It also
/// remembers which weapon last hit each enemy aircraft, for the kill line.
/// </summary>
public class AaTrace : MonoBehaviour
{
    const float MetresPerUnit = 67.200066f;
    const float ReportEverySeconds = 10f;
    /// <summary>Below this a shell that ends without a collision has gone into the sea.</summary>
    const float SeaLevelUnity = 0.1f;

    sealed class Shot
    {
        public string Gun;
        public float RangeM, AltitudeM, FuzeM;
        public float ClosestM = float.MaxValue;
        public string Outcome;
    }

    static readonly AccessTools.FieldRef<Projectile, Vector3> CurrentPos = AccessTools.FieldRefAccess<Projectile, Vector3>("_currentPos");
    static readonly AccessTools.FieldRef<Projectile, Vector3> NewPos = AccessTools.FieldRefAccess<Projectile, Vector3>("_newPos");
    static readonly AccessTools.FieldRef<Projectile, Projectile.State> State = AccessTools.FieldRefAccess<Projectile, Projectile.State>("_state");

    static readonly Dictionary<Projectile, Shot> flying = new Dictionary<Projectile, Shot>();
    static readonly List<Shot> finished = new List<Shot>();
    /// <summary>Enemy aircraft by id, and the weapon of ours that last hit each one.</summary>
    static readonly Dictionary<int, string> lastHitBy = new Dictionary<int, string>();
    static double nextReport;

    internal static string LastHitBy(ObjectBase unit) => lastHitBy.TryGetValue(unit.UniqueID, out var gun) ? gun : null;

    internal static void Reset()
    {
        flying.Clear();
        finished.Clear();
        lastHitBy.Clear();
    }

    static bool Ours(Projectile p) => p._launchPlatform != null && p._launchPlatform._taskforce == Globals._playerTaskforce;

    void Update()
    {
        if (GameTime.missionElapsedTime < nextReport) return;
        nextReport = GameTime.missionElapsedTime + ReportEverySeconds;
        if (finished.Count == 0) return;
        try { Entry.Log.LogInfo(Summarise()); }
        catch (System.Exception e) { Entry.Log.LogError($"AA trace failed: {e}"); }
        finished.Clear();
    }

    static string Summarise()
    {
        var sb = new StringBuilder($"AA shells, last {ReportEverySeconds:0} s at {GameTime.missionElapsedTime / 60:0.0} min:");
        foreach (var gun in finished.GroupBy(s => s.Gun))
        {
            var closest = gun.Select(s => s.ClosestM).Where(d => d < float.MaxValue).OrderBy(d => d).ToList();
            string spread = closest.Count == 0 ? "no approach measured" :
                $"closest approach min {closest[0]:0} m / median {closest[closest.Count / 2]:0} m / 90% {closest[closest.Count * 9 / 10]:0} m";
            var outcomes = string.Join(", ", gun.GroupBy(s => s.Outcome).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {g.Key}"));
            sb.Append($"\n  {gun.Key}: {gun.Count()} fired, fuze {gun.First().FuzeM:0} m, {spread}" +
                      $"\n    at range {Median(gun.Select(s => s.RangeM)) / 1000f:0.0} km, altitude {Median(gun.Select(s => s.AltitudeM)):0} m (medians)" +
                      $"\n    ended: {outcomes}");
        }
        return sb.ToString();
    }

    static float Median(IEnumerable<float> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0f : sorted[sorted.Count / 2];
    }

    static void Finish(Projectile p, string outcome)
    {
        if (!flying.TryGetValue(p, out var shot)) return;
        flying.Remove(p);
        shot.Outcome ??= outcome;
        finished.Add(shot);
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.fire))]
    static class Fired
    {
        static void Prefix(Projectile __instance)
        {
            // A pooled shell refired before we saw it end.
            Finish(__instance, "lost track");
            var target = __instance._targetObject;
            if (!__instance._isAirTarget || !Ours(__instance) || target == null) return;
            flying[__instance] = new Shot
            {
                Gun = __instance._ws?._vwp?._systemName ?? __instance._pp._ammunitionFileName,
                RangeM = (target.transform.position - __instance._launchPlatform.transform.position).magnitude * MetresPerUnit,
                AltitudeM = target.transform.position.y * MetresPerUnit,
                FuzeM = __instance._pp.HasProximitySensing ? __instance._pp._fuzeProximityDistance * MetresPerUnit : 0f,
            };
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.MoveProjectile))]
    static class Moved
    {
        static void Postfix(Projectile __instance)
        {
            if (!flying.TryGetValue(__instance, out var shot)) return;
            var target = __instance._targetObject;
            if (target != null && !target.IsDestroyed)
            {
                // Closest point of this step's path to the target, so a fast
                // shell cannot jump past it between samples.
                Vector3 a = CurrentPos(__instance), b = NewPos(__instance), t = target.transform.position;
                Vector3 ab = b - a;
                float along = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(t - a, ab) / ab.sqrMagnitude) : 0f;
                shot.ClosestM = Mathf.Min(shot.ClosestM, (a + ab * along - t).magnitude * MetresPerUnit);
            }
            if (State(__instance) != Projectile.State.ToBeDestroyed) return;
            string why = NewPos(__instance).y < SeaLevelUnity ? "into the sea"
                : __instance._isProximityFuseActive ? "self-destructed past the aim point" : "ended before the fuze armed";
            Finish(__instance, why);
        }
    }

    [HarmonyPatch(typeof(Projectile), "ProcessCollision")]
    static class Collided
    {
        static void Prefix(Projectile __instance, RaycastHit __0)
        {
            if (!Ours(__instance) || __0.transform == null) return;
            var hit = __0.transform.root.GetComponentInChildren<ObjectBase>();
            if (hit != null && hit.IsAirUnit && hit._taskforce == Globals._enemyTaskforce)
                lastHitBy[hit.UniqueID] = __instance._ws?._vwp?._systemName ?? __instance._pp._ammunitionFileName;
            if (!flying.TryGetValue(__instance, out var shot)) return;
            shot.Outcome = hit == null ? $"hit {__0.transform.root.name}"
                : hit == __instance._targetObject ? "burst on its target"
                : hit.IsAirUnit ? "burst on another aircraft"
                : $"hit {hit.Name.Value}";
        }
    }
}
