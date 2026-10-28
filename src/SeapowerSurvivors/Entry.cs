using System;
using AnchorChain;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SeapowerSurvivors;

[ACPlugin(Guid, "Sea Power Survivors", "0.1.0")]
public class Entry : IAnchorChainMod
{
    public const string Guid = "com.dominicjameswhite.seapowersurvivors";

    internal static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Survivors");

    public void TriggerEntryPoint()
    {
        try
        {
            new Harmony(Guid).PatchAll(typeof(Entry).Assembly);
            var host = new GameObject("SeapowerSurvivors");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<TestPanel>();
            Log.LogInfo("Loaded. Ctrl+F6 opens the test panel.");
        }
        catch (Exception e)
        {
            Log.LogError($"Failed to initialise: {e}");
        }
    }
}
