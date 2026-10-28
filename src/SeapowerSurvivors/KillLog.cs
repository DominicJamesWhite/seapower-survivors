using System.Linq;
using HarmonyLib;
using SeaPower;

namespace SeapowerSurvivors;

/// <summary>
/// Logs every unit death with the XP it would award. setDestroyedFlag is the
/// moment of death; it can run more than once per unit (a sinking ship is
/// later removed through it again), so only the first transition counts.
/// </summary>
[HarmonyPatch(typeof(ObjectBase), nameof(ObjectBase.setDestroyedFlag))]
static class KillLog
{
    internal static float Xp;

    static void Prefix(ObjectBase __instance, out bool __state) => __state = __instance.IsDestroyed;

    static void Postfix(ObjectBase __instance, bool score, bool __state)
    {
        if (__state || !__instance.IsDestroyed || !__instance.isUnit()) return;

        bool enemy = __instance._taskforce == Globals._enemyTaskforce;
        float value = __instance._obp != null ? __instance._obp._unitScoreValue : 0f;
        if (enemy && score) Xp += value;

        string damage = __instance.Info == null ? "?" : string.Join(", ",
            __instance.Info._damageByTaskforce.Select(kv => $"{kv.Key?.Side}:{kv.Value:F0}"));
        TestPanel.Report($"Destroyed: {__instance.Name.Value} ({__instance._obp?._objectIniName}), " +
            $"side {__instance._taskforce?.Side}, score={score}, value {value:F1}, damage by [{damage}], XP now {Xp:F1}");
    }
}
