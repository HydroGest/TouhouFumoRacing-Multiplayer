using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace FumoMP;

[BepInPlugin(GUID, "Fumo Multiplayer", VERSION)]
[BepInProcess("TouhouFumoRacing.exe")]
public class Plugin : BasePlugin
{
    public const string GUID = "dev.fumo.multimod";
    public const string VERSION = "0.5.0";

    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo($"FumoMP loading (EnterMP patch + custom MP menu)");

        // Intercept the broken EnterMP() (loads missing 'mpmenu' scene).
        Harmony harmony = new Harmony(GUID);
        harmony.PatchAll();

        // Inject our OnGUI lobby behaviour into the game object hierarchy.
        // NOTE: this runs before the game's first scene is loaded, so the object
        // is also re-created after every scene load (see TryPatchSceneHooks).
        ClassInjector.RegisterTypeInIl2Cpp<MpMenuUI>();
        MpMenuUI.EnsureExists();

        TryPatchSceneHooks(harmony);
        MpStartSequencePatch.TryApply(harmony);
        MpVcTracePatch.TryApply(harmony);
        MpDamageProbe.TryApply(harmony);
        MpFreezeProbe.TryApply(harmony);
        MpAirGuard.TryApply(harmony);
        MpFlow.HookNetControl();
        MpQuitPatch.TryApply(harmony);
        StartDiag.Install(harmony);

        Log.LogInfo("FumoMP loaded (debug telemetry = " + MpDiag.DebugLogging + ")");
    }

    /// <summary>
    /// Hook the scene-loading paths so the lobby host object survives scene
    /// changes. Every patch is optional and guarded: if a target is missing on
    /// some game build, the plugin must still load.
    /// </summary>
    private static void TryPatchSceneHooks(Harmony harmony)
    {
        var postfix = new HarmonyMethod(
            typeof(MpPatches).GetMethod(nameof(MpPatches.AfterSceneLoaded),
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));

        // 1) Unity's own scene-loaded callback.
        TryPatch(harmony, "UnityEngine.SceneManagement.SceneManager", "Internal_SceneLoaded", postfix);

        // 2) The game's main-menu manager (belt and braces).
        TryPatch(harmony, "MainMenuManager", "OnSceneLoaded", postfix);
    }

    private static void TryPatch(Harmony harmony, string typeName, string methodName, HarmonyMethod postfix)
    {
        try
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                Log.LogWarning($"scene hook: type '{typeName}' not found (skipped)");
                return;
            }
            var target = AccessTools.Method(type, methodName);
            if (target == null)
            {
                Log.LogWarning($"scene hook: {typeName}.{methodName} not found (skipped)");
                return;
            }
            harmony.Patch(target, postfix: postfix);
            Log.LogInfo($"scene hook installed: {typeName}.{methodName}");
        }
        catch (System.Exception e)
        {
            Log.LogWarning($"scene hook {typeName}.{methodName} failed: {e.Message}");
        }
    }
}
