using System;
using System.Linq;
using AnchorChain;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SeapowerSurvivors;

[ACPlugin(Guid, "Sea Power Survivors", "0.2.0")]
public class Entry : IAnchorChainMod
{
    public const string Guid = "com.dominicjameswhite.seapowersurvivors";

    internal static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Survivors");

    public void TriggerEntryPoint()
    {
        try
        {
            var host = new GameObject("SeapowerSurvivors");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<Director>();
            host.AddComponent<LevelUpScreen>();
            host.AddComponent<TestPanel>();
            host.AddComponent<AaTrace>();
        }
        catch (Exception e)
        {
            Log.LogError($"Failed to initialise: {e}");
            return;
        }

        // One patch class at a time, so a hook that no longer fits the game
        // after an update costs only its own feature, not every other one.
        var harmony = new Harmony(Guid);
        int failed = 0;
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Entry).Assembly)
                     .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e)
            {
                failed++;
                Log.LogError($"Patch {type.FullName} failed: {e.Message}");
            }
        }
        Log.LogInfo(failed == 0 ? "Loaded. Ctrl+F6 opens the test panel." : $"Loaded with {failed} failed patch(es); see above.");
    }
}
