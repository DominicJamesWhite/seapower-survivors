using System.Linq;
using System.Text;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Why a gun does or does not hurt aircraft, written to the log: whether the
/// AI would engage at all, and the game's own hit model against the nearest
/// enemy aircraft. Follows the game's gun dispersion test (DM_TestTools
/// LogAaEngagement), which only writes to the in-game event log.
/// </summary>
static class AaReport
{
    const float MetresPerUnit = 67.200066f, UnityPerMetre = 0.0148809375f;
    /// <summary>The game's reference target when nothing is airborne: 1500 m, 250 m/s.</summary>
    const float ReferenceAltitudeM = 1500f, ReferenceSpeedMs = 250f;

    internal static void Log(ObjectBase ship)
    {
        var target = Singleton<ObjectsManager>.Instance._listOfAllObjects
            .Where(o => o != null && !o.IsDestroyed && o.IsAirUnit && o._taskforce == Globals._enemyTaskforce)
            .OrderBy(o => (o.transform.position - ship.transform.position).sqrMagnitude)
            .FirstOrDefault();
        var sb = new StringBuilder();
        sb.AppendLine($"AA report for {ship.Name.Value}, vs " + (target == null
            ? $"reference aircraft at {ReferenceAltitudeM:0} m, {ReferenceSpeedMs:0} m/s"
            : $"{target.getUIDAndName()} at {(target.transform.position - ship.transform.position).magnitude * MetresPerUnit / 1000f:0.0} km, " +
              $"{target.transform.position.y * MetresPerUnit:0} m, {target.UnityVelocityVector.magnitude * MetresPerUnit:0} m/s"));

        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var gun in ship._obp._gunWeapons)
        {
            var ammo = gun._ammoForEngage ?? gun._vwp._associatedMagazine?._ammunitionList.FirstOrDefault(a => a != null);
            if (ammo?._ap == null) { sb.AppendLine($"  {gun._vwp._systemName}: no ammunition"); continue; }
            var ap = ammo._ap;
            string name = ap._ammunitionFileName;
            var registered = ship.getAmmunitionByName(name);

            // Engagement: what the AI checks before it gives a gun an air target.
            string engage = target == null ? "" :
                $" | matches target {ship.DoesAmmoMatchTarget(name, target)}, usable {WeaponSelector.isWeaponSystemUsable(gun, name, target, ignoreSensors: true, ignoreExecuting: true)}, " +
                $"in arc {gun.targetIsInFiringArc(target.transform.position)}, engaging {(gun._executingEngageTask ? gun._targetObject?.getUIDAndName() ?? "?" : "nothing")}";
            sb.AppendLine($"  {gun._vwp._systemName} [{name}]: fuze {ap._fuzeType} {ap._fuzeProximityDistance * MetresPerUnit:0.#} m, " +
                $"targets {ap._targetType}/{ap._secondaryTargetType}, AA range {(ap._maxAARangeInUnity > 0 ? $"{ap._maxAARangeInUnity * MetresPerUnit / 1000f:0.0} km" : "none (direct fire)")}, " +
                $"power {ap._warheadPower:0.#}, AI reads {(registered == null ? "nothing" : ReferenceEquals(registered._ap, ap) ? "this copy" : $"another copy: fuze {registered._ap._fuzeType}, targets {registered._ap._targetType}/{registered._ap._secondaryTargetType}")}" + engage);
            if (!seen.Add(gun._vwp._systemName + name)) continue;

            try { Envelope(sb, gun, ammo, target); }
            catch (System.Exception e) { sb.AppendLine($"    hit model failed: {e.Message}"); }
        }
        Entry.Log.LogInfo(sb.ToString());
        TestPanel.Report("AA report written to the log");
    }

    static void Envelope(StringBuilder sb, WeaponSystemGun gun, Ammunition ammo, ObjectBase target)
    {
        var ap = ammo._ap;
        float altitudeM = target != null ? target.transform.position.y * MetresPerUnit : ReferenceAltitudeM;
        float speed = target != null ? target.UnityVelocityVector.magnitude : ReferenceSpeedMs * UnityPerMetre;
        float jink = target != null ? CiwsBallistics.EvasiveLateralAccelMs2(target) : 0f;
        CiwsBallistics.Durability durability = target != null ? CiwsBallistics.GetDurability(target) : new CiwsBallistics.Durability { _health = 4f };
        float area = target != null ? CiwsBallistics.GetPresentedAreaM2(target, target.transform.forward) : 8f;
        float bodyRadius = Mathf.Sqrt(Mathf.Max(area, 0.01f) / Mathf.PI);
        float fuzeRadius = ap.HasProximitySensing ? ap._fuzeProximityDistance * MetresPerUnit : 0f;
        float damage = ap.HasProximitySensing ? CiwsBallistics.BurstEffectiveDamage(ap, in durability) : CiwsBallistics.EffectiveDamage(ap, in durability);
        float envelope = GunBallistics.DirectFireRangeUnity(ap, altitudeM * UnityPerMetre, Globals._gunAADirectFireMaxElevationDeg);
        var director = gun.GetFireControlSensor();
        sb.AppendLine($"    director {(director != null ? director._systemName : "none")}, envelope {envelope * MetresPerUnit / 1000f:0.0} km at {altitudeM:0} m, " +
            $"capture {fuzeRadius + bodyRadius:0.#} m, {durability._health:0.0} HP, damage/burst {damage:0.00}, crew error x{gun._baseObject._crew?._fcErrorMultiplier ?? 1f:0.00}");
        if (envelope < 0.0001f) return;

        sb.AppendLine("     slant   TOF | angular  lay range predict  bias  disp evade = total | hit%  shells/kill");
        float altitude = altitudeM * UnityPerMetre;
        foreach (float f in new[] { 0.25f, 0.5f, 0.75f, 1f })
        {
            float ground = envelope * f;
            float slant = Mathf.Sqrt(ground * ground + altitude * altitude);
            var b = gun.BuildAaErrorBudget(ammo, slant, speed, jink, 1000f, altitude);
            WeaponSystemGun.SimulateAaShells(in b, fuzeRadius + bodyRadius, bodyRadius, fuzeRadius, damage, out float hit, out float perShell, 300, WeaponSystemGun.AaRangeErrorKills(ap));
            string kill = perShell > 1e-6f ? $"{durability._health / perShell:0}" : "never";
            float m = MetresPerUnit;
            sb.AppendLine($"    {slant * m / 1000f,5:0.0}km {b._timeOfFlight,4:0.0}s | {b._angularSigma * m,6:0} {b._laySigma * m,4:0} {b._rangeSigma * m,5:0} {b._predictionSigma * m,7:0} " +
                $"{b._solutionBiasSigma * m,5:0} {b._dispersionSigma * m,5:0} {b._evasionSigma * m,5:0} = {b.TotalSigma * m,5:0} | {hit * 100f,5:0.00} {kill,8}");
        }
    }
}
