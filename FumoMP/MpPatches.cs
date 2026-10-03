using HarmonyLib;
using UnityEngine;

namespace FumoMP;

/// <summary>
/// Intercepts MultiplayerManager.EnterMP() so clicking the main-menu
/// "Online" button opens our custom lobby instead of trying to load the
/// scene 'mpmenu' that was stripped from the build.
///
/// Also patches Input.GetKeyDown so the lobby's "Start Race" button can
/// trigger the game's own start path: FRNetGameState.ServerUpdate calls
/// Input.GetKeyDown(KeyCode.KeypadEnter) (0x10F) to start the race on the
/// host - see the decompiled ServerUpdate.
/// </summary>
[HarmonyPatch]
public static class MpPatches
{
    [HarmonyPatch(typeof(MultiplayerManager), nameof(MultiplayerManager.EnterMP))]
    [HarmonyPrefix]
    private static bool EnterMpPrefix()
    {
        Plugin.Log.LogInfo("EnterMP intercepted -> showing custom MP lobby");
        MpMenuUI.Open();
        return false; // skip original (scene 'mpmenu' does not exist)
    }

    [HarmonyPatch(typeof(MultiplayerManager), nameof(MultiplayerManager.ExitMP))]
    [HarmonyPrefix]
    private static bool ExitMpPrefix()
    {
        MpFlow.StopAll();
        MpMenuUI.Close();
        return false;
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) })]
    [HarmonyPrefix]
    private static bool GetKeyDownPrefix(KeyCode key, ref bool __result)
    {
        // When the lobby requested a start, synthesize the KeypadEnter press
        // the game's ServerUpdate waits for. Otherwise let the original run.
        if (MpFlow.StartRequested && key == KeyCode.KeypadEnter)
        {
            MpFlow.StartRequested = false;
            Plugin.Log.LogInfo("Synthetic KeypadEnter -> race start path");
            __result = true;
            return false;
        }

        // While our lobby is open it owns the keyboard: keys used for editing or
        // navigation must not reach the game (Backspace/Escape are "back" in its
        // menus, which closes them or quits the game).
        if (MpUgui.ShouldSwallowKey(key))
        {
            __result = false;
            return false;
        }
        return true;
    }

    /// <summary>
    /// FRNetGameState.OnNetGameStart throws a NullReferenceException in this build
    /// whenever the settings message arrives, and because it runs inside Mirror's
    /// message loop that failure also disrupts the rest of the start handshake.
    /// We perform the start ourselves (see MpFlow.RequestStart), so the broken
    /// handler is skipped.
    /// </summary>
    [HarmonyPatch(typeof(FRNetGameState), "OnNetGameStart")]
    [HarmonyPrefix]
    private static bool OnNetGameStartPrefix()
    {
        Plugin.Log.LogInfo("skipped the build's broken OnNetGameStart handler");
        return false;
    }

    /// <summary>
    /// Called after each scene load (patched manually from Plugin.Load so a
    /// missing target can never break the whole plugin). The plugin loads
    /// before the game's first scene, so the host object created there does not
    /// survive into the menu scene; this guarantees the OnGUI entry exists.
    /// </summary>
    internal static void AfterSceneLoaded()
    {
        Plugin.Log.LogInfo("Scene loaded -> ensuring lobby host exists");
        MpMenuUI.EnsureExists();
        // with the debug flag on, list what is clickable in this scene
        try { MpDiag.ScreenScan(); } catch { }
    }
}

/// <summary>
/// The game's countdown entry point. OnStartCircuit calls it before it raises the
/// race flag, and in this build it throws every time (it walks a network client
/// singleton chain that does not exist here), so the level loads with the cars
/// frozen and no race. Take it over: the release itself is UnFreezeRacers.
/// Applied by hand from Plugin.Load, because the interop parameter types differ
/// from the metadata ones and a bad patch must never stop the plugin loading.
/// </summary>
internal static class MpStartSequencePatch
{
    internal static void TryApply(HarmonyLib.Harmony harmony)
    {
        try
        {
            var type = HarmonyLib.AccessTools.TypeByName("GameManager");
            if (type == null) { Plugin.Log.LogWarning("countdown patch: GameManager not found"); return; }

            System.Reflection.MethodBase target = null;
            foreach (var m in type.GetMethods(System.Reflection.BindingFlags.Public
                                               | System.Reflection.BindingFlags.NonPublic
                                               | System.Reflection.BindingFlags.Static))
            {
                if (m.Name != "StartSequence") continue;
                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[1].ParameterType == typeof(double)) { target = m; break; }
            }
            if (target == null) { Plugin.Log.LogWarning("countdown patch: no StartSequence(racers, double) found"); return; }

            var prefix = new HarmonyLib.HarmonyMethod(
                typeof(MpStartSequencePatch).GetMethod(nameof(Prefix),
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
            harmony.Patch(target, prefix: prefix);
            Plugin.Log.LogInfo("countdown patch installed on " + target);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("countdown patch failed (race start will be incomplete): " + e.Message);
        }
    }

    private static bool Prefix(object[] __args)
    {
        object first = (__args != null && __args.Length > 0) ? __args[0] : null;
        double ts = 0;
        try { if (__args != null && __args.Length > 1) ts = System.Convert.ToDouble(__args[1]); } catch { }
        try
        {
            MpDiag.ManualCountdown(first as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<PlayerRacer>, ts);
        }
        catch (System.Exception e) { Plugin.Log.LogError("countdown patch: " + e); }
        return false; // the original cannot complete in this build
    }
}

/// <summary>
/// The quit buttons.
///
/// Two different things share the name: the main menu's Exit/Quit really ends the
/// process, while the in-race pause "Quit" calls GameManager.QuitRace() - which
/// needs the live session (it tears the race down over the network) and dies with
/// a NullReferenceException if the session is already gone. So: stop the session
/// first only for the app-level quits, and for the in-race one let the game run
/// and only step in if it throws.
/// </summary>
[HarmonyPatch]
public static class MpQuitPatch
{
    internal static void TryApply(HarmonyLib.Harmony harmony)
    {
        var appQuit = new HarmonyLib.HarmonyMethod(
            typeof(MpQuitPatch).GetMethod(nameof(BeforeAppQuit), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
        var raceQuit = new HarmonyLib.HarmonyMethod(
            typeof(MpQuitPatch).GetMethod(nameof(AfterRaceQuit), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));

        TryPatch(harmony, "MenuEvents", "Exit", appQuit);
        TryPatch(harmony, "QuitOption", "Quit", appQuit);
        TryPatch(harmony, "PauseScreenUI", "Quit", null, raceQuit);
    }

    private static void TryPatch(HarmonyLib.Harmony harmony, string type, string method,
                                 HarmonyLib.HarmonyMethod prefix, HarmonyLib.HarmonyMethod finalizer = null)
    {
        try
        {
            var t = HarmonyLib.AccessTools.TypeByName(type);
            if (t == null) { Plugin.Log.LogInfo("quit hook: " + type + " not found"); return; }
            var m = HarmonyLib.AccessTools.Method(t, method);
            if (m == null) { Plugin.Log.LogInfo("quit hook: " + type + "." + method + " not found"); return; }
            harmony.Patch(m, prefix: prefix, finalizer: finalizer);
            Plugin.Log.LogInfo("quit hook installed: " + type + "." + method + (prefix != null ? " (session first)" : " (recover on failure)"));
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("quit hook " + type + " failed: " + e.Message); }
    }

    private static void BeforeAppQuit()
    {
        try { MpDiag.ShutdownNetworkForQuit(); }
        catch (System.Exception e) { Plugin.Log.LogWarning("quit hook: " + e.Message); }
    }

    /// <summary>
    /// Runs after the game's own in-race quit. If that threw, the player is stuck
    /// on a finished track, so close the session and go back to the menu instead.
    /// </summary>
    private static System.Exception AfterRaceQuit(System.Exception __exception)
    {
        if (__exception == null) { Plugin.Log.LogInfo("quit race: the game handled it"); return null; }
        var inner = __exception.InnerException ?? __exception;
        Plugin.Log.LogWarning("quit race threw (" + inner.GetType().Name + ": " + inner.Message
                              + ") - closing the session and returning to the menu");
        try { MpDiag.ShutdownNetworkForQuit(); } catch { }
        try { MpDiag.StepReturnToMenu(); } catch (System.Exception e) { Plugin.Log.LogWarning("quit race: menu fallback failed: " + e.Message); }
        return null;   // swallow: we recovered
    }
}

/// <summary>
/// Keeps the player's kart visible. The game switches the whole character model
/// subtree off and on during a race; on this build that reads as the model
/// "spinning and then disappearing", so those particular deactivations are refused
/// (only the racer's own model, only while a race is running).
/// </summary>
[HarmonyPatch(typeof(UnityEngine.GameObject), nameof(UnityEngine.GameObject.SetActive))]
public static class MpModelVisibilityPatch
{
    private static bool Prefix(UnityEngine.GameObject __instance, bool value)
    {
        try
        {
            if (value) return true;                        // showing is always allowed
            if (!MpDiag.IsModelHide(__instance)) return true;
            MpDiag.LogModelHideBlocked(__instance);
            return false;                                  // keep the kart visible
        }
        catch { return true; }
    }
}
