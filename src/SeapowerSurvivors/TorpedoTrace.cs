using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Follows every torpedo our ships fire or drop, from launch to whatever
/// ends it, and logs one report per torpedo: how it was launched, what its
/// seeker had to work with, each phase it went through, what it locked on
/// to, how close it came to a ship, and how it ended.
/// </summary>
public class TorpedoTrace : MonoBehaviour
{
    const float SampleEverySeconds = 0.5f;
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
        public float ClosestM = float.MaxValue;
        public string ClosestTo;
        public float ClosestToTargetM = float.MaxValue;
        public float TopKnots;
        public string Outcome;
    }

    static readonly Dictionary<Torpedo, Track> tracks = new Dictionary<Torpedo, Track>();
    static readonly AccessTools.FieldRef<Torpedo, float> Fuel = AccessTools.FieldRefAccess<Torpedo, float>("_remainingFuelFraction");
    static readonly AccessTools.FieldRef<Blastzone, WeaponBase> BlastWeapon = AccessTools.FieldRefAccess<Blastzone, WeaponBase>("_weapon");
    double nextSample;

    /// <summary>A new run: report what was still running, or a long run's only evidence is lost.</summary>
    internal static void Reset()
    {
        foreach (var pair in tracks.ToList())
        {
            try { Finish(pair.Key, pair.Value, pair.Value.Outcome ?? "still running when the run ended"); }
            catch (System.Exception e) { Entry.Log.LogError($"Torpedo trace failed at reset: {e}"); }
        }
        tracks.Clear();
    }

    static bool Ours(WeaponBase w) => w._launchPlatform != null && w._launchPlatform._taskforce == Globals._playerTaskforce;

    static string Nm(float unity) => $"{unity / UnityPerNm:0.00} nm";

    static string Name(ObjectBase o) => o == null ? "none" : o is WakeBubble b
        ? $"{(b._isPermamentTailWakeBubble ? "stern" : "wake")} of {b._launchPlatform?.Name.Value ?? "?"}"
        : o.Name.Value;

    double T(Track t) => GameTime.missionElapsedTime - t.Launched;

    [HarmonyPatch(typeof(Torpedo), "TorpedoLaunchSettings")]
    static class Launched
    {
        // After Boosts.HuntShipsBlind, so the posture logged is the one it runs with.
        [HarmonyPriority(Priority.Low)]
        static void Postfix(Torpedo __instance)
        {
            if (!Ours(__instance)) return;
            try
            {
                if (tracks.TryGetValue(__instance, out var old)) Finish(__instance, old, "relaunched before it ended");
                var ap = __instance._ap;
                var target = __instance.CurrentIntendedTargetObject;
                var pos = __instance.transform.position;
                string how = __instance._isSubmunition
                    ? $"dropped by {__instance._parentWeapon?._ap?._ammunitionFileName ?? "?"}"
                    : $"from {__instance._weaponSystem?._vwp?._systemName ?? "?"}";
                string aim = target == null
                    ? $"blind, aim point {Nm((__instance.AimPointGeoPosition.ToUnity() - pos).magnitude)} away"
                    : $"at {target.Name.Value} {Nm((target.transform.position - pos).magnitude)} away doing {target.getVelocityInKnots():0} kt, " +
                      $"aim point {Nm((__instance.AimPointGeoPosition.ToUnity() - target.transform.position).magnitude)} from it";
                tracks[__instance] = new Track
                {
                    Launched = GameTime.missionElapsedTime,
                    IntendedTarget = target,
                    Header = $"Torpedo {ap._ammunitionFileName} {how} by {__instance._launchPlatform.Name.Value}, {aim}\n" +
                             $"  guidance {__instance._seeker?._guidanceType}, posture {__instance._lockedTargetPosture}, " +
                             $"transit {__instance.TransitVelocityInKnots:0} kt, terminal {ap._terminalVelocityInKnots:0} kt, search {ap._searchVelocityInKnots:0} kt, " +
                             $"run {ap._maxLaunchRangeInMiles:0.0} nm, turn {ap._maxTurnRateDegrees:0}°/s, " +
                             $"seeker {ap._seekerActiveRange / UnityPerNm:0.00}/{ap._seekerPassiveRange / UnityPerNm:0.00} nm over {ap._seekerFOV:0}°, " +
                             $"search mode {ap.SearchModeSetting.Value}, depth {__instance._heightToMaintain * MetresPerUnit:0} m, " +
                             $"wake turn delay {ap._turnDelay:0.0} s from {ap._turnDelayStartRange * MetresPerUnit:0} m",
                };
                // Boosted torpedoes can run for twenty minutes before the report at the end.
                Entry.Log.LogInfo($"Launched: {tracks[__instance].Header}");
            }
            catch (System.Exception e) { Entry.Log.LogError($"Torpedo trace failed at launch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(Torpedo), nameof(Torpedo.destroyObject))]
    static class Ended
    {
        static void Prefix(Torpedo __instance)
        {
            if (!tracks.TryGetValue(__instance, out var t)) return;
            float fuel = Fuel(__instance);
            Finish(__instance, t, t.Outcome ?? (fuel <= 0f ? "ran out of fuel"
                : __instance._collisionNoticed ? "hit something that is not a unit"
                : $"destroyed in {__instance._flightStage} with {fuel:P0} fuel left"));
        }
    }

    [HarmonyPatch(typeof(Blastzone), "OnHitUnit")]
    static class Hit
    {
        static void Prefix(Blastzone __instance, object __0)
        {
            if (BlastWeapon(__instance) is Torpedo torpedo && tracks.TryGetValue(torpedo, out var t) && __0 is ObjectBase hit)
                t.Outcome = hit._taskforce == Globals._playerTaskforce ? $"HIT OUR OWN {hit.Name.Value}" : $"HIT {hit.Name.Value}";
        }
    }

    static void Finish(Torpedo torpedo, Track t, string outcome)
    {
        tracks.Remove(torpedo);
        var sb = new StringBuilder(t.Header);
        sb.Append($"\n  ran {GameTime.missionElapsedTime - t.Launched:0} s, top speed {t.TopKnots:0} kt, {t.Locks} lock(s)");
        if (t.ClosestTo != null) sb.Append($", closest ship {t.ClosestTo} at {t.ClosestM:0} m");
        if (t.IntendedTarget != null) sb.Append($", closest to its target {t.ClosestToTargetM:0} m");
        sb.Append(t.Events);
        sb.Append($"\n  ended: {outcome}");
        Entry.Log.LogInfo(sb.ToString());
    }

    void Update()
    {
        if (tracks.Count == 0 || GameTime.missionElapsedTime < nextSample) return;
        nextSample = GameTime.missionElapsedTime + SampleEverySeconds;
        try
        {
            var ships = Singleton<ObjectsManager>.Instance._listOfAllObjects
                .Where(o => o is Vessel && !o.IsDestroyed && o._taskforce != Globals._playerTaskforce).ToList();
            foreach (var pair in tracks.ToList())
            {
                Torpedo torpedo = pair.Key;
                Track t = pair.Value;
                if (torpedo == null || !torpedo.gameObject.activeInHierarchy)
                {
                    Finish(torpedo, t, t.Outcome ?? "vanished");
                    continue;
                }
                Sample(torpedo, t, ships);
            }
        }
        catch (System.Exception e)
        {
            Entry.Log.LogError($"Torpedo trace failed: {e}");
            tracks.Clear();
        }
    }

    void Sample(Torpedo torpedo, Track t, List<ObjectBase> ships)
    {
        var pos = torpedo.transform.position;
        t.TopKnots = Mathf.Max(t.TopKnots, torpedo._velocityInKnots);
        foreach (var ship in ships)
        {
            float m = (ship.transform.position - pos).magnitude * MetresPerUnit;
            if (m < t.ClosestM) { t.ClosestM = m; t.ClosestTo = ship.Name.Value; }
        }
        if (t.IntendedTarget != null && !t.IntendedTarget.IsDestroyed)
            t.ClosestToTargetM = Mathf.Min(t.ClosestToTargetM, (t.IntendedTarget.transform.position - pos).magnitude * MetresPerUnit);

        string stage = torpedo._flightStage.ToString();
        if (stage != t.Stage)
        {
            t.Stage = stage;
            t.Events.Append($"\n  {T(t),4:0} s  {stage}, depth {pos.y * MetresPerUnit:0} m, {torpedo._velocityInKnots:0} kt{Nearest(pos, ships)}");
        }
        var seeker = torpedo._seeker;
        if (seeker == null) return;
        bool on = seeker.isActive();
        if (on != t.SeekerOn)
        {
            t.SeekerOn = on;
            t.Events.Append($"\n  {T(t),4:0} s  seeker {(on ? "on" : "off")}{Nearest(pos, ships)}");
        }
        var locked = seeker._lockedOnTarget ?? seeker._strongestSensorEcho?._objectBase;
        if (locked != t.Locked)
        {
            if (locked != null) t.Locks++;
            t.Locked = locked;
            // Wake hops come every few seconds: log the first few and the stern.
            if (locked == null || t.Locks <= 4 || locked is WakeBubble { _isPermamentTailWakeBubble: true } || !(locked is WakeBubble))
                t.Events.Append(locked == null
                    ? $"\n  {T(t),4:0} s  lost lock{(seeker._wakeHomingTurnDelayed ? " (wake turn delay)" : "")}"
                    : $"\n  {T(t),4:0} s  locked {Name(locked)} {(locked.transform.position - pos).magnitude * MetresPerUnit:0} m away, depth {pos.y * MetresPerUnit:0} m");
        }
    }

    static string Nearest(Vector3 pos, List<ObjectBase> ships)
    {
        var ship = ships.OrderBy(s => (s.transform.position - pos).sqrMagnitude).FirstOrDefault();
        if (ship == null) return "";
        var to = ship.transform.position - pos;
        return $"; nearest ship {ship.Name.Value} {Nm(to.magnitude)} away";
    }
}
