using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using SeaPower;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Watches for fleet ships stuck on "No fire-control channel", and for
/// launchers stuck on one engagement in one state (the OODA panel's
/// "Engaging; Aligning launcher"), and logs what holds them. The test
/// panel's "Log fire control" logs the same for every ship at once.
/// </summary>
/// <remarks>
/// The game's OODA loop gives a guided-weapon engage task a director channel
/// before it checks that any mount can fire it. A director is full when
/// the distinct targets of the tasks naming it, queued or taken by a mount,
/// reach its channels (its trackers' channels, if it has trackers). A task
/// that holds a channel and never fires keeps every other target waiting.
/// This logs the holders, with enough about each (mount, target, range,
/// age) to tell which upgrade made a task that cannot fire.
/// </remarks>
public class FireControlTrace : MonoBehaviour
{
    const float CheckEverySeconds = 5f;
    /// <summary>Game time a ship must be starved before it is reported: longer than a normal solution takes.</summary>
    const double StuckAfterSeconds = 30.0;
    /// <summary>A ship still stuck is reported again this often, not every check.</summary>
    const double RepeatAfterSeconds = 120.0;
    /// <summary>Game time a launcher may sit on one engagement in one state before it is reported.</summary>
    const double LauncherStuckAfterSeconds = 60.0;
    const float UnityPerNm = 27.559496f;
    const float MetresPerUnit = 67.200066f;

    static readonly Dictionary<ObjectBase, double> starvedSince = new Dictionary<ObjectBase, double>();
    static readonly Dictionary<ObjectBase, double> lastReported = new Dictionary<ObjectBase, double>();
    /// <summary>Each executing launcher's task and state, and since when it has had both.</summary>
    static readonly Dictionary<WeaponSystemLauncher, (EngageTask task, WeaponSystem.EngageState state, double since)> launcherState =
        new Dictionary<WeaponSystemLauncher, (EngageTask, WeaponSystem.EngageState, double)>();
    static readonly Dictionary<WeaponSystemLauncher, double> launcherReported = new Dictionary<WeaponSystemLauncher, double>();
    double nextCheck;

    internal static void Reset()
    {
        starvedSince.Clear();
        lastReported.Clear();
        launcherState.Clear();
        launcherReported.Clear();
    }

    /// <summary>The test panel's button: every fleet ship's directors and launchers, now.</summary>
    internal static void LogAll()
    {
        var run = Run.Current;
        var ships = run?.Fleet ?? new List<ObjectBase> { TestPanel.PlayerShip() };
        foreach (var ship in ships.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit))
            Entry.Log.LogInfo(Describe(run, ship, null));
    }

    void Update()
    {
        var run = Run.Current;
        if (run == null || GameTime.missionElapsedTime < nextCheck) return;
        nextCheck = GameTime.missionElapsedTime + CheckEverySeconds;
        try
        {
            foreach (var ship in run.Fleet.Where(s => s != null && !s.IsDestroyed && !s.IsAirUnit))
                Check(run, ship);
        }
        catch (System.Exception e)
        {
            Entry.Log.LogError($"Fire control trace failed: {e}");
            enabled = false;
        }
    }

    static void Check(Run run, ObjectBase ship)
    {
        double now = GameTime.missionElapsedTime;
        CheckLaunchers(run, ship, now);
        if (!Starved(ship).Any())
        {
            starvedSince.Remove(ship);
            return;
        }
        if (!starvedSince.TryGetValue(ship, out double since)) starvedSince[ship] = since = now;
        if (now - since < StuckAfterSeconds) return;
        if (lastReported.TryGetValue(ship, out double last) && now - last < RepeatAfterSeconds) return;
        lastReported[ship] = now;
        Entry.Log.LogWarning(Describe(run, ship, now - since));
    }

    static void CheckLaunchers(Run run, ObjectBase ship, double now)
    {
        foreach (var launcher in ship._obp._launcherWeapons)
        {
            if (launcher == null || !launcher._executingEngageTask || launcher._takenEngageTask == null)
            {
                if (launcher != null) launcherState.Remove(launcher);
                continue;
            }
            var task = launcher._takenEngageTask;
            if (!launcherState.TryGetValue(launcher, out var seen) || seen.task != task || seen.state != launcher._engageState)
            {
                launcherState[launcher] = (task, launcher._engageState, now);
                continue;
            }
            if (now - seen.since < LauncherStuckAfterSeconds) continue;
            if (launcherReported.TryGetValue(launcher, out double last) && now - last < RepeatAfterSeconds) continue;
            launcherReported[launcher] = now;
            Entry.Log.LogWarning($"FIRE CONTROL: {ship.Name.Value} launcher {launcher._vwp._sectionName} has sat in {launcher._engageState} " +
                                 $"on one engagement for {now - seen.since:0} s\n{Describe(run, ship, null)}");
        }
    }

    /// <summary>The OODA panel's "No fire-control channel": a guided-weapon task with a target and no director.</summary>
    static IEnumerable<EngageTask> Starved(ObjectBase ship) =>
        ship._currentEngageTasks.Where(t => t != null && t._targetObject != null && t._solutionChannelOwner == null &&
                                            (t._ammunition?._ap == null || t._ammunition._ap._requiresGuidance));

    /// <summary>The directors the ship's mounts can use, each once.</summary>
    static List<SensorSystem> Directors(ObjectBase ship) =>
        ship._obp._weaponSystems.Where(w => w?._vwp?._associatedSensors != null)
            .SelectMany(w => w._vwp._associatedSensors).Where(s => s != null).Distinct().ToList();

    /// <param name="starvedFor">How long tasks have waited for a channel, when that is why it is logged.</param>
    static string Describe(Run run, ObjectBase ship, double? starvedFor)
    {
        double now = GameTime.missionElapsedTime;
        var sb = new StringBuilder(starvedFor is double waited
            ? $"FIRE CONTROL: {ship.Name.Value} ({ship._obp._objectIniName}) has had tasks waiting for a channel for {waited:0} s, at {now / 60:0.0} min"
            : $"Fire control on {ship.Name.Value} ({ship._obp._objectIniName}) at {now / 60:0.0} min, {ship._velocityInKnots:0} kn, heading {ship.transform.eulerAngles.y:0}");
        if (run != null) sb.Append($"\n  upgrades: {string.Join(", ", run.Held.Select(u => $"{u.Name} {run.RankOf(u)}"))}");

        var mountTasks = ship._obp._weaponSystems.Where(w => w?._takenEngageTask != null).ToList();
        foreach (var director in Directors(ship))
        {
            var holders = mountTasks.Where(w => w._takenEngageTask._solutionDirector == director)
                .Select(w => (task: w._takenEngageTask, where: $"mount {w._vwp._sectionName} executing={w._executingEngageTask} reloading={w._isReloading}"))
                .Concat(ship._currentEngageTasks.Where(t => t != null && t._solutionDirector == director).Select(t => (task: t, where: "queued")))
                .Where(h => h.task._targetObject != null).ToList();
            int pending = holders.Select(h => h.task._targetObject).Distinct().Count();
            string trackers = director._targetTrackingSystems.Count == 0 ? "" :
                $", trackers [{string.Join(", ", director._targetTrackingSystems.Where(t => t != null).Select(t => $"{t._systemName} {t._targetChannels}ch on={t.IsOn.Value}"))}]";
            sb.Append($"\n  director {director._systemName}: {pending}/{director.TotalTargetChannels} solution channels held, " +
                      $"own {director._targetChannels}t/{director._weaponChannels}w, associated {director._associatedTargets.Count} targets {director._associatedWeapons.Count} weapons, " +
                      $"on={director.IsOn.Value} inoperable={director.Inoperable.Value}{trackers}");
            foreach (var (task, where) in holders)
                sb.Append($"\n    held by {where}: {Task(ship, task, now)}");
        }
        foreach (var task in Starved(ship))
            sb.Append($"\n  waiting: {Task(ship, task, now)}");
        foreach (var task in ship._currentEngageTasks.Where(t => t != null && t._solutionChannelOwner != null && t._targetObject != null))
            sb.Append($"\n  queued with a channel on {task._solutionDirector?._systemName}: {Task(ship, task, now)}");
        foreach (var launcher in ship._obp._launcherWeapons.Where(l => l != null))
            sb.Append($"\n  {Launcher(ship, launcher, now)}");
        return sb.ToString();
    }

    /// <summary>A launcher's whole engage state: what it is on, how far through, and what it has loaded.</summary>
    static string Launcher(ObjectBase ship, WeaponSystemLauncher launcher, double now)
    {
        var traverse = HarmonyLib.Traverse.Create(launcher);
        var ammo = launcher._ammoForEngage;
        string loaded = ammo == null ? "" : $", loaded {launcher.getLoadedAmmoCount(ammo._ap._ammunitionFileName)}/{launcher._vwp._maxAmmoCount} {ammo._ap._ammunitionFileName}";
        var sb = new StringBuilder($"launcher {launcher._vwp._sectionName}: executing={launcher._executingEngageTask} state={launcher._engageState} loadCycle={launcher._state} " +
                                   $"system={launcher._systemState} rest={launcher._isInRestPosition} onRail={traverse.Field("_onRail").GetValue()} " +
                                   $"reloading={launcher._isReloading} ({launcher._remainingReloadTime:0.0} s left, reload {launcher._vwp._magazineReloadTime:0.0} s){loaded} " +
                                   $"inoperable={launcher.Inoperable.Value} offline={launcher._isOffline}");
        if (!launcher._executingEngageTask) return sb.ToString();
        double blocked = traverse.Field("_blockedSinceTime").GetValue<double>();
        sb.Append($"\n    engaged {now - launcher._engageStartTime:0} s (acquisition {ReactionTime.Acquisition(ship, launcher._vwp):0.0} s), " +
                  $"blocked {(blocked < 0.0 ? "never" : $"{now - blocked:0} s")}, task {(launcher._takenEngageTask == null ? "none" : Task(ship, launcher._takenEngageTask, now))}");
        var target = launcher._targetObject;
        if (target != null)
        {
            var to = target.transform.position - ship.transform.position;
            float bearing = Mathf.DeltaAngle(ship.transform.eulerAngles.y, Quaternion.LookRotation(new Vector3(to.x, 0f, to.z)).eulerAngles.y);
            float maxLaunch = traverse.Field("_currentMaxLaunchRange").GetValue<float>(), horizon = traverse.Field("_horizonRange").GetValue<float>();
            sb.Append($"\n    target {target.Name.Value}: {to.magnitude / UnityPerNm:0.0} nm, {target.transform.position.y * MetresPerUnit:0} m up, " +
                      $"{bearing:0} deg off the bow, in firing arc {launcher.targetIsInFiringArc(target.transform.position)}, " +
                      $"destroyed={target.IsDestroyed}; launcher fires inside {maxLaunch / UnityPerNm:0.0} nm (director horizon {horizon / UnityPerNm:0.0} nm), " +
                      $"spawn reach says {Reach.PlayerReachNm(new[] { ship }, target):0.0} nm");
        }
        return sb.ToString();
    }

    internal static string Task(ObjectBase ship, EngageTask task, double now)
    {
        var target = task._targetObject;
        // Unity's null: a target destroyed since the task was made.
        if (target == null) return $"{task._ammoId} at a target already gone, age {now - task.CreationTime:0} s";
        float nm = (target.transform.position - ship.transform.position).magnitude / UnityPerNm;
        var ap = task._ammunition?._ap;
        string range = ap == null ? "" : $" (max {ap._maxLaunchRangeInMiles:0.0} nm)";
        string state = target.IsDestroyed ? "DESTROYED" : target._externalDestructionNotified ? "being removed" : "alive";
        string solution = task._solutionStartTime >= 0.0 ? $", solution for {now - task._solutionStartTime:0} s" : "";
        return $"{task._ammoId} at {target.Name.Value} ({target._obp?._objectIniName}, {state}, {nm:0.0} nm{range}, " +
               $"{(target.IsAirUnit ? "air" : target is WeaponBase ? "missile" : "surface")}), auto={task._isAutoAttack}, age {now - task.CreationTime:0} s{solution}";
    }
}

/// <summary>
/// The game's ObjectBase.HandleEngageTasks has been seen throwing "index out
/// of range" on a fleet ship every update, from the moment a SAM channel
/// upgrade was taken. The throw ends the loop at the same task each time, so
/// every task behind it never reaches a launcher, and the ship holds fire at
/// missiles closing on it. Where it throws is not yet known. Reading the
/// decompiled loop, every index looks guarded, so the likeliest cause is the
/// task list shrinking under the loop. This records which list read was out
/// of range and the first call that changed the task list mid-loop, with its
/// stack, logs those with the task the loop was on, and drops that one task
/// so the rest are handled. The AI makes its auto-attack tasks
/// afresh, so a dropped task costs at most a moment.
///
/// It is caught on every ship, not just ours. GameMain.Update runs each
/// object's update in one loop, from the end of the list back, with no
/// catch: a throw ends the loop, and every object before the thrower misses
/// that frame. An enemy throwing every frame froze our ships, spawned first,
/// systems and all, and their guns sat on "Reloading" with a fraction of a
/// second left.
/// </summary>
[HarmonyLib.HarmonyPatch(typeof(ObjectBase), nameof(ObjectBase.HandleEngageTasks))]
static class EngageLoopGuard
{
    const double ReportEverySeconds = 30.0;

    static ObjectBase ship;
    static EngageTask task;
    static string stage;
    static int iterations;
    static readonly Dictionary<ObjectBase, (double at, int swallowed)> reported = new Dictionary<ObjectBase, (double, int)>();

    static void Prefix(ObjectBase __instance, out (List<EngageTask> list, int count) __state)
    {
        ship = __instance;
        task = null;
        stage = "before the first task";
        iterations = 0;
        badRead = change = null;
        __state = (__instance._currentEngageTasks, __instance._currentEngageTasks.Count);
    }

    /// <summary>The loop asks for each task's weapons first, at the top of every iteration that gets that far.</summary>
    [HarmonyLib.HarmonyPatch(typeof(WeaponSelector), nameof(WeaponSelector.GetWeaponSystemsForAmmunition))]
    static class Systems
    {
        static void Prefix(ObjectBase obj, string ammoId, ObjectBase targetObject)
        {
            if (obj != ship) return;
            iterations++;
            task = obj._currentEngageTasks.FirstOrDefault(t => t != null && t._ammoId == ammoId && t._targetObject == targetObject);
            stage = "choosing weapons";
        }
    }

    /// <summary>Midway: the task is past its discard checks and claiming a director.</summary>
    [HarmonyLib.HarmonyPatch(typeof(ObjectBase), "UpdateSolutionChannel")]
    static class Channel
    {
        static void Prefix(ObjectBase __instance, EngageTask task) { if (__instance == ship) { EngageLoopGuard.task = task; stage = "claiming a channel"; } }
        static void Postfix(ObjectBase __instance) { if (__instance == ship) stage = "after claiming a channel"; }
    }

    /// <summary>Each of the loop's list reads, in IL order: what it reads from, with what index, and what uses the item.</summary>
    static readonly List<string> sites = new List<string>();
    /// <summary>The loop's first read out of range, and the first change to the task list made while it ran.</summary>
    static string badRead, change;

    /// <summary>
    /// Every List indexer in the loop goes through Item, so a bad index names
    /// its read. Last, so it also covers the read UnguidedBombSalvo's
    /// transpiler adds, and that transpiler still finds its pattern.
    /// </summary>
    [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.Last)]
    static IEnumerable<HarmonyLib.CodeInstruction> Transpiler(IEnumerable<HarmonyLib.CodeInstruction> instructions)
    {
        var codes = instructions.ToList();
        var output = new List<HarmonyLib.CodeInstruction>();
        var item = HarmonyLib.AccessTools.Method(typeof(EngageLoopGuard), nameof(Item));
        sites.Clear();
        for (int i = 0; i < codes.Count; i++)
        {
            var code = codes[i];
            if (code.opcode == OpCodes.Callvirt && code.operand is MethodInfo m && m.Name == "get_Item" &&
                m.DeclaringType.IsGenericType && m.DeclaringType.GetGenericTypeDefinition() == typeof(List<>) && i >= 2)
            {
                sites.Add($"{codes[i - 2]} [{codes[i - 1]}], then {(i + 1 < codes.Count ? codes[i + 1].ToString() : "nothing")}");
                // The read becomes the site number, keeping its labels so a
                // branch to it still lands first, and the call goes after.
                code.opcode = OpCodes.Ldc_I4;
                code.operand = sites.Count - 1;
                output.Add(code);
                output.Add(new HarmonyLib.CodeInstruction(OpCodes.Call, item.MakeGenericMethod(m.DeclaringType.GetGenericArguments()[0])));
                continue;
            }
            output.Add(code);
        }
        return output;
    }

    static T Item<T>(List<T> list, int index, int site)
    {
        if ((uint)index >= (uint)list.Count && badRead == null)
            badRead = $"read {site} of {sites.Count}, {sites[site]}: index {index} of {list.Count}" +
                      (ship != null && (object)list == ship._currentEngageTasks ? " in the ship's task list" : "");
        return list[index];
    }

    /// <summary>
    /// The calls that shrink or replace a ship's task list. The loop's own
    /// RemoveEngageTask(int) is followed by i--; any of these while the loop
    /// runs can leave its index past the end.
    /// </summary>
    [HarmonyLib.HarmonyPatch]
    static class Changes
    {
        const int StackLines = 20;

        static IEnumerable<MethodBase> TargetMethods() => new MethodBase[]
        {
            HarmonyLib.AccessTools.Method(typeof(ObjectBase), nameof(ObjectBase.RemoveEngageTask), new[] { typeof(EngageTask) }),
            HarmonyLib.AccessTools.Method(typeof(ObjectBase), nameof(ObjectBase.ClearEngageTasks)),
            HarmonyLib.AccessTools.Method(typeof(ObjectBase), nameof(ObjectBase.ClearEngageTasksKeepGuiding)),
            HarmonyLib.AccessTools.Method(typeof(ObjectBase), nameof(ObjectBase.CeaseFire)),
            HarmonyLib.AccessTools.Method(typeof(ObjectBase), nameof(ObjectBase.InsertEngageTask)),
        };

        static void Prefix(ObjectBase __instance, MethodBase __originalMethod)
        {
            if (ship == null || __instance != ship || change != null) return;
            var stack = System.Environment.StackTrace.Split('\n').Skip(2).Take(StackLines);
            change = $"{__originalMethod.Name} on thread {System.Threading.Thread.CurrentThread.ManagedThreadId}, from\n{string.Join("\n", stack)}";
        }
    }

    static System.Exception Finalizer(ObjectBase __instance, System.Exception __exception, (List<EngageTask> list, int count) __state)
    {
        ship = null;
        var run = Run.Current;
        if (!(__exception is System.ArgumentOutOfRangeException) || run == null) return __exception;
        try
        {
            double now = GameTime.missionElapsedTime;
            var tasks = __instance._currentEngageTasks;
            reported.TryGetValue(__instance, out var last);
            if (last.at == 0 || now - last.at >= ReportEverySeconds)
            {
                string side = run.Fleet.Contains(__instance) ? "" : " (enemy)";
                var sb = new StringBuilder($"FIRE CONTROL: {__instance.Name.Value}{side} engage loop threw at {now / 60:0.0} min, " +
                                           $"{stage}, iteration {iterations}; tasks {__state.count} before, {tasks.Count} after" +
                                           $"{(tasks != __state.list ? " (list replaced)" : "")}; {last.swallowed} more since last report");
                sb.Append($"\n  on: {(task == null ? "unknown" : FireControlTrace.Task(__instance, task, now))}");
                for (int i = 0; i < tasks.Count; i++)
                    if (tasks[i] != null)
                        sb.Append($"\n  [{i}] p{tasks[i]._priority} shots {tasks[i]._shotsToFire} channel {tasks[i]._solutionDirector?._systemName ?? "none"}: {FireControlTrace.Task(__instance, tasks[i], now)}");
                sb.Append($"\n  bad read: {badRead ?? "none of the loop's own; a method it called threw"}");
                if (change != null) sb.Append($"\n  task list changed while the loop ran: {change}");
                sb.Append($"\n  {__exception.GetType().Name}: {__exception.Message}\n{__exception.StackTrace}");
                Entry.Log.LogWarning(sb.ToString());
                reported[__instance] = (now, 0);
            }
            else reported[__instance] = (last.at, last.swallowed + 1);

            if (task != null && tasks.Remove(task) &&
                !__instance._obp._weaponSystems.Any(w => w != null && w._executingEngageTask && w._alignmentUID == task._uid))
                __instance.EndAlignment(task._uid);
        }
        catch (System.Exception e)
        {
            Entry.Log.LogError($"Engage loop guard failed: {e}");
        }
        return null;
    }
}
