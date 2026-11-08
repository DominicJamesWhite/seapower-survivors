using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SeaPower;
using UnityEngine;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace SeapowerSurvivors;

/// <summary>
/// What makes a rocket mortar work at range, beyond its numbers (see
/// <see cref="Boosts.ApplyMortar"/>). Once the line is taken our mortars aim as
/// guns do and have a fire control of their own, because the game's AI cannot
/// use them as long-range weapons, in three ways:
/// <list type="bullet">
/// <item>It holds a mortar's fire past 10 nm, whatever its range.</item>
/// <item>It counts ships as a mortar's second target type, so it fires them
/// only when every missile launcher is busy or empty.</item>
/// <item>It treats a target with a mortar round in flight or tasked as dealt
/// with, for the whole taskforce: harmless over 3 nm, but at 60 nm one salvo
/// would hold back every Harpoon in the fleet for a minute or more.</item>
/// </list>
/// Launches go through the game's own engage tasks, so arcs, alignment and
/// reloads are the launcher's as usual. Mortars still answer torpedoes the
/// way the game has them do. Without an upgrade they are not fired at ships
/// at all: see <see cref="AiLeavesMortarsAlone"/>.
/// </summary>
static class Mortars
{
    const double CheckSeconds = 1.0;
    /// <summary>How often Spotters puts ships in reach on the plot.</summary>
    const double SpotSeconds = 10.0;
    /// <summary>Launchers fire this far inside their reach, so they never wait on "target too far".</summary>
    const float ReachMargin = 0.95f;
    const float UnityPerNm = 27.559496f;
    /// <summary>How far from its sunk target a hunting round looks for another.</summary>
    const float HuntingNm = 15f;
    /// <summary>A hunting round's course correction; enough for any ship, gentle enough to keep the salvo's pattern.</summary>
    const float SteerDegreesPerSecond = 10f;
    /// <summary>Under this, in game units (about 70 m), a round is on its way down and stops steering.</summary>
    const float SteerStopDistance = 1f;

    static double nextCheck, nextSpot;

    /// <summary>Mortars are under this fire control once the line is taken, or from the start under an ensign that improves them: the St Andrew's flag, whose whole effect they are, or the White Ensign's reach.</summary>
    internal static bool InLine(Run run) => run != null &&
        (run.RankOf(Upgrades.Mortars) > 0 || run.Hoisted.Contains(Ensigns.EmpMortars) || run.Hoisted.Contains(Ensigns.LongRange));
    static bool InFleet(Run run, ObjectBase o) => o != null && run.Fleet.Contains(o);
    static bool EnemyShip(ObjectBase o) =>
        o != null && !o.IsDestroyed && o is Vessel && o._taskforce == Globals._enemyTaskforce;

    internal static void Tick(Run run)
    {
        if (!InLine(run)) return;
        double now = GameTime.missionElapsedTime;
        if (now < nextCheck && now >= nextCheck - CheckSeconds) return;
        nextCheck = now + CheckSeconds;

        if (Upgrades.Has(run, Upgrades.Spotters) && (now >= nextSpot || now < nextSpot - SpotSeconds))
        {
            nextSpot = now + SpotSeconds;
            Spot(run);
        }
        bool deep = Upgrades.Has(run, Upgrades.DeepMagazines);
        foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit))
            Engage(ship, deep);
    }

    static void Engage(ObjectBase ship, bool deep)
    {
        var launchers = Boosts.MortarLaunchers(ship).Where(l => !l.Inoperable.Value && !l._isOffline).ToList();
        if (launchers.Count == 0 || ship._ai == null) return;
        if (deep) Refill(launchers);
        // Weapon status works as for every other weapon. The AI's own target list
        // already applies it: on free, hostile ships; on tight, ships that are a
        // threat within 10 nm. On hold the AI stops refreshing that list and stands
        // down, unless the ship has been ordered to destroy something.
        if (ship._weaponStatus == ObjectBase.WeaponStatus.Hold && ship._ai._objectToDestroy == null) return;

        var plot = ship._ai._possibleTargetsWithPriorities;
        var targets = plot.Keys.Where(EnemyShip).ToList();
        if (targets.Count == 0) return;

        // A task still queued belongs to the next idle launcher of its ammunition.
        var queued = ship._currentEngageTasks.Where(t => t?._ammoId != null)
            .GroupBy(t => t._ammoId).ToDictionary(g => g.Key, g => g.Count());
        var chosen = new HashSet<ObjectBase>();
        foreach (var launcher in launchers)
        {
            if (launcher._executingEngageTask || launcher._isReloading) continue;
            var ap = Boosts.LauncherAmmo(launcher)
                .FirstOrDefault(a => Boosts.Mortar(a) && launcher.getLoadedAmmoCount(a._ammunitionFileName) > 0);
            if (ap == null) continue;
            string ammo = ap._ammunitionFileName;
            if (queued.TryGetValue(ammo, out int waiting) && waiting > 0)
            {
                queued[ammo] = waiting - 1;
                continue;
            }

            var target = Pick(ship, launcher, ap, targets, chosen);
            if (target == null) continue;
            chosen.Add(target);
            ship.InsertEngageTask(ammo, target, Vector3.zero, 1, plot[target], autoAttack: true);
        }
    }

    /// <summary>The nearest ship in reach and arc, preferring one no other salvo is already falling on.</summary>
    static ObjectBase Pick(ObjectBase ship, WeaponSystemLauncher launcher, AmmunitionParameters ap, List<ObjectBase> targets, HashSet<ObjectBase> chosen)
    {
        ObjectBase best = null;
        bool bestBusy = true;
        float bestRange = float.MaxValue;
        float near = ap._launchRangesInUnity.x, far = ap._launchRangesInUnity.y * ReachMargin;
        foreach (var target in targets)
        {
            var at = target.transform.position;
            float range = (at - ship.transform.position).magnitude;
            if (range <= near || range > far || !launcher.targetIsInFiringArc(at)) continue;
            bool busy = chosen.Contains(target) || target._incomingWeapons.Any(w => w is RBU);
            if (best != null && (busy && !bestBusy || busy == bestBusy && range >= bestRange)) continue;
            (best, bestBusy, bestRange) = (target, busy, range);
        }
        return best;
    }

    /// <summary>Deep magazines: topped up before they can run dry. The rounds are the same objects, so nothing needs boosting again.</summary>
    static void Refill(IEnumerable<WeaponSystemLauncher> launchers)
    {
        foreach (var launcher in launchers)
        {
            var magazine = launcher._vwp._associatedMagazine;
            if (magazine == null) continue;
            int salvo = Mathf.Max(1, launcher._vwp._salvoFireAmount);
            if (Boosts.LauncherAmmo(launcher).Where(Boosts.Mortar)
                .Any(a => magazine.getAmmunitionCount(a._ammunitionFileName) < salvo))
                magazine.RefillAmmo();
        }
    }

    /// <summary>Spotters: every enemy ship inside a mortar's reach goes on that ship's plot, and so the fleet's.</summary>
    static void Spot(Run run)
    {
        var enemies = Singleton<ObjectsManager>.Instance._listOfAllVesselObjects.Where(EnemyShip).ToList();
        foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit))
        {
            float reach = Boosts.MortarLaunchers(ship).Where(l => !l.Inoperable.Value)
                .SelectMany(Boosts.LauncherAmmo).Where(Boosts.Mortar)
                .Select(a => a._launchRangesInUnity.y).DefaultIfEmpty(0f).Max();
            if (reach <= 0f) continue;
            foreach (var enemy in enemies)
                if ((enemy.transform.position - ship.transform.position).sqrMagnitude <= reach * reach)
                    Utils.RevealContactToObject(ship, enemy, "Comms", 3);
        }
    }

    /// <summary>How many times its stock speed each launcher's load animation plays.</summary>
    sealed class LoadSpeed { public float Factor = 1f; }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ObjectCodeAnimation, LoadSpeed> loadSpeeds =
        new System.Runtime.CompilerServices.ConditionalWeakTable<ObjectCodeAnimation, LoadSpeed>();

    internal static void SetLoadSpeed(WeaponSystemLauncher launcher, float factor)
    {
        if (launcher._loadAmmunitionAnimation != null)
            loadSpeeds.GetOrCreateValue(launcher._loadAmmunitionAnimation).Factor = factor;
    }

    /// <summary>
    /// A mortar reloads as fast as its animation plays, whatever the timer
    /// says: the new rounds are spawned by a step of the animation, 122 s in on
    /// an RBU-6000 and about 60 s on an RBU-1000, and the game holds the reload
    /// open until it ends. So the reload ranks speed it up too, by moving its
    /// start time back a little each frame: each launcher has its own copy.
    /// </summary>
    [HarmonyPatch(typeof(ObjectCodeAnimation), nameof(ObjectCodeAnimation.update))]
    static class LoadAnimationAtSpeed
    {
        static readonly AccessTools.FieldRef<ObjectCodeAnimation, double> StartTime =
            AccessTools.FieldRefAccess<ObjectCodeAnimation, double>("_animationStartTime");

        static void Prefix(ObjectCodeAnimation __instance)
        {
            if (loadSpeeds.TryGetValue(__instance, out var speed) && speed.Factor > 1f)
                StartTime(__instance) -= (speed.Factor - 1f) * GameTime.deltaTime;
        }
    }

    /// <summary>
    /// Keeps the game's AI off mortars against surface ships, on both sides, for
    /// the whole of a run. Left in, any ship with a mortar takes a mortar task on
    /// a ship inside 10 nm, whatever the mortar's range, and its alignment then
    /// steers straight at the target to get there: a 3 nm RBU-6000 or a half-mile
    /// RBU-1000 sends it charging. An upgraded fleet's are fired by
    /// <see cref="Engage"/>, only at ships already in reach and arc, so it never
    /// has to close. Submarines and torpedoes are left to the game.
    /// </summary>
    [HarmonyPatch(typeof(AI), nameof(AI.AmmoForTarget))]
    static class AiLeavesMortarsAlone
    {
        static readonly AccessTools.FieldRef<AI, ObjectBase> AiShip = AccessTools.FieldRefAccess<AI, ObjectBase>("_baseObject");

        static void Postfix(AI __instance, ObjectBase targetObject, List<string> __result)
        {
            if (Run.Current == null || __result.Count == 0 || !(targetObject is Vessel)) return;
            var ship = AiShip(__instance);
            __result.RemoveAll(name => ship.getAmmunitionByName(name)?._ap is AmmunitionParameters ap && Boosts.Mortar(ap));
        }
    }

    /// <summary>
    /// A mortar round no longer closes a ship's file for the rest of our fleet.
    /// The method returns "handled" at the sight of a mortar in four places,
    /// two by class and two by ammunition type. All four are swapped for calls
    /// that let our fleet's checks run past them; the check then goes on to add
    /// up the round's saturation value, which <see cref="Boosts.ApplyMortar"/> sets to zero.
    /// </summary>
    [HarmonyPatch(typeof(AI), "CheckIfAlreadyHandled")]
    static class MortarsDoNotHoldFire
    {
        const int ExpectedSites = 4;
        static readonly AccessTools.FieldRef<AI, ObjectBase> AiShip = AccessTools.FieldRefAccess<AI, ObjectBase>("_baseObject");

        /// <summary>Set for each check: the method is only ever run on the main thread, and calls nothing that re-enters it.</summary>
        static bool ignoreMortars;

        static void Prefix(AI __instance, ObjectBase targetObject)
        {
            var run = Run.Current;
            ignoreMortars = InLine(run) && targetObject != null && targetObject.isUnit() && InFleet(run, AiShip(__instance));
        }

        static object MortarRound(object weapon) => ignoreMortars ? null : weapon as RBU;

        /// <summary>An ammunition type no round has, when mortars are to be ignored.</summary>
        static int MortarType() => ignoreMortars ? -1 : (int)Ammunition.Type.RBU;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var typeField = AccessTools.Field(typeof(AmmunitionParameters), nameof(AmmunitionParameters._type));
            var list = instructions.ToList();
            int swapped = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Isinst && (System.Type)list[i].operand == typeof(RBU))
                {
                    list[i] = CodeInstruction.Call(typeof(MortarsDoNotHoldFire), nameof(MortarRound)).MoveLabelsFrom(list[i]);
                    swapped++;
                }
                else if (i > 0 && list[i - 1].LoadsField(typeField) && list[i].LoadsConstant((long)Ammunition.Type.RBU))
                {
                    list[i] = CodeInstruction.Call(typeof(MortarsDoNotHoldFire), nameof(MortarType)).MoveLabelsFrom(list[i]);
                    swapped++;
                }
            }
            if (swapped != ExpectedSites)
                Entry.Log.LogWarning($"Mortars: found {swapped} of {ExpectedSites} mortar checks in CheckIfAlreadyHandled; mortars may hold back the fleet's other weapons");
            return list;
        }
    }

    /// <summary>
    /// Our rounds fly as shells do: aimed once, with the error a gun has, then
    /// left alone. This runs once a round, as its launch phase ends. The game's
    /// own aim will not do at range: it leads as if the round flew flat at full
    /// speed when it climbs at about 35°, and the launch phase, which shrinks
    /// with speed, is too short to finish yawing onto the lead. So the lead is
    /// worked out from the speed the round really makes over the water, from the
    /// plot's track of the target, and the round is pointed along it. The game's
    /// range trim then works to that point for the rest of the flight.
    /// <para>
    /// The error is the game's surface gunnery formula (WeaponSystemGun.DeviationCheck),
    /// for a gun that has found the range: mortars have no spotting, so this
    /// is as good as they get. It scales with the crew's fire-control error,
    /// which the Accuracy line sets. Each round has its own, so a salvo thins
    /// out with range rather than all landing or all missing.
    /// </para>
    /// </summary>
    [HarmonyPatch]
    static class AimAtLaunch
    {
        /// <summary>The range error a gun keeps once its solution is complete (1 − 0.7).</summary>
        const float SettledRangeError = 0.3f;
        const float UnityPerSecondPerKnot = 0.0076554087f;

        static readonly AccessTools.FieldRef<WeaponBase, Vector3> TrackVelocity = AccessTools.FieldRefAccess<WeaponBase, Vector3>("_estimatedInterceptTargetVelocityVector");
        static readonly System.Action<WeaponBase> ReadTrack = AccessTools.MethodDelegate<System.Action<WeaponBase>>(
            AccessTools.Method(typeof(WeaponBase), "calculateEstimatedInterceptTargetPositionAndVelocity"));

        static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.Inner(typeof(RBU), "Launch"), "takeAction");

        static void Postfix(RBU rbu)
        {
            var run = Run.Current;
            if (!InLine(run) || rbu == null || rbu.IsDestroyed || !InFleet(run, rbu._launchPlatform)) return;
            if (rbu._flightStage != WeaponBase.FlightStage.TerminalApproach || !Boosts.Mortar(rbu._ap)) return;
            var target = rbu.CurrentIntendedTargetObject;
            if (target == null || target.IsDestroyed) return;

            ReadTrack(rbu);
            var here = rbu.transform.position;
            var flat = rbu.transform.forward;
            flat.y = 0f;
            float overWater = rbu._velocityInKnots * UnityPerSecondPerKnot * flat.magnitude;
            if (overWater <= 0f) return;

            var at = rbu._estimatedInterceptTargetPosition;
            at.y = 0f;
            var lead = Utils.CalculateInterceptPosition(here, overWater, at, TrackVelocity(rbu)).interceptPosition;
            lead.y = 0f;

            var line = lead - here;
            line.y = 0f;
            rbu._deviationVector += AimError(rbu._launchPlatform, line);
            var aim = lead + rbu._deviationVector;
            aim.y = 0f;
            rbu._estimatedInterceptTargetPosition = aim;
            rbu._estimatedInterceptTargetPosition2.FromUnity(aim);

            var course = aim - here;
            course.y = 0f;
            float turn = Vector3.SignedAngle(flat, course, Vector3.up);
            rbu.transform.forward = Quaternion.AngleAxis(turn, Vector3.up) * rbu.transform.forward;
        }

        static Vector3 AimError(ObjectBase ship, Vector3 line)
        {
            float range = line.magnitude;
            if (range <= 0f) return Vector3.zero;
            float crew = ship._crew != null ? ship._crew._fcErrorMultiplier : 1f;
            float alongSigma = Mathf.Max(Globals._gunFCBaseRangeErrorFloorUnity, range * Globals._gunFCBaseRangeErrorPercent * 0.01f) * SettledRangeError * crew;
            float acrossSigma = Globals._gunFCBaseBearingErrorMils * 0.001f * range * crew;
            var along = line / range;
            return along * (GunBallistics.Gauss() * alongSigma) + Vector3.Cross(Vector3.up, along) * (GunBallistics.Gauss() * acrossSigma);
        }
    }

    /// <summary>
    /// Hunting rounds: steered all the way down, as Smart shells steer a gun's.
    /// Each update takes the time left from the speed the round makes over the
    /// water, moves the aim point the game's range trim works to, and turns the
    /// round onto it. The aiming error stays in, so Accuracy still counts. A round
    /// whose target has sunk takes the nearest enemy ship instead.
    /// </summary>
    [HarmonyPatch]
    static class HuntingRounds
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.Inner(typeof(RBU), "TerminalApproach"), "takeAction");

        static void Postfix(RBU rbu)
        {
            var run = Run.Current;
            if (run == null || !Upgrades.Has(run, Upgrades.HuntingRounds) || rbu == null || rbu.IsDestroyed || !InFleet(run, rbu._launchPlatform)) return;
            if (rbu._flightStage != WeaponBase.FlightStage.TerminalApproach || !Boosts.Mortar(rbu._ap)) return;

            var target = rbu.CurrentIntendedTargetObject;
            if (target == null || target.IsDestroyed)
            {
                target = NearestShip(rbu._estimatedInterceptTargetPosition);
                if (target == null) return;
                rbu.CurrentIntendedTargetObject = target;
                target._incomingWeapons.Add(rbu);
            }

            var here = rbu.transform.position;
            var moved = here - rbu._lastPos;
            moved.y = 0f;
            float overWater = moved.magnitude / GameTime.fixedDeltaTime;
            if (overWater <= 0f) return;

            var toTarget = target.transform.position - here;
            toTarget.y = 0f;
            var aim = target.transform.position + target.UnityVelocityVector * (toTarget.magnitude / overWater) + rbu._deviationVector;
            aim.y = 0f;
            rbu._estimatedInterceptTargetPosition = aim;

            var course = aim - here;
            course.y = 0f;
            if (course.magnitude < SteerStopDistance) return;
            var forward = rbu.transform.forward;
            var flat = forward;
            flat.y = 0f;
            float limit = SteerDegreesPerSecond * GameTime.fixedDeltaTime;
            float turn = Mathf.Clamp(Vector3.SignedAngle(flat, course, Vector3.up), -limit, limit);
            rbu.transform.forward = Quaternion.AngleAxis(turn, Vector3.up) * forward;
        }

        static ObjectBase NearestShip(Vector3 from)
        {
            float best = HuntingNm * UnityPerNm;
            best *= best;
            ObjectBase nearest = null;
            foreach (var ship in Singleton<ObjectsManager>.Instance._listOfAllVesselObjects)
            {
                if (!EnemyShip(ship)) continue;
                var offset = ship.transform.position - from;
                offset.y = 0f;
                if (offset.sqrMagnitude < best) (best, nearest) = (offset.sqrMagnitude, ship);
            }
            return nearest;
        }
    }
}
