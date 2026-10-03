using System;
using System.Reflection;
using HarmonyLib;

namespace FumoMP;

/// <summary>
/// Temporary diagnostics for the race-start path. FRNetGameState.StartGame is an
/// async state machine that can wait silently, so each interesting step is logged
/// to pinpoint where it stops. Every hook is optional and guarded.
/// </summary>
internal static class StartDiag
{
    private static int _moveNext;
    private static float _lastReadyLog;
    private static FieldInfo _stateField;
    private static float _lastDump;

    internal static void Install(Harmony harmony)
    {
        Hook(harmony, typeof(FRNetGameState), "IsEveryoneReady", null, nameof(IsEveryoneReadyPost));
        Hook(harmony, typeof(FRNetGameState), "OnNetGameStart", nameof(SimplePrefix), null, "OnNetGameStart");
        Hook(harmony, typeof(FRNetGameState), "OnNetStartSequence", nameof(SimplePrefix), null, "OnNetStartSequence");
        Hook(harmony, typeof(FRNetGameState), "OnLevelStart", nameof(SimplePrefix), null, "OnLevelStart");
        Hook(harmony, typeof(FRNetGameState), "ReadyPlayer", nameof(SimplePrefix), null, "ReadyPlayer");
        Hook(harmony, typeof(FRNetGameState), "OnNetGameReady", nameof(SimplePrefix), null, "OnNetGameReady");
        Hook(harmony, typeof(FRNetGameState), "ServerUpdate", null, nameof(ServerUpdatePost));

        var inner = FindInner(typeof(FRNetGameState), "StartGame");
        if (inner != null)
        {
            // Il2CppInterop sanitises compiler-generated names: <>1__state -> __1__state
            foreach (var f in inner.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Plugin.Log.LogInfo("start diag field: " + f.Name + " : " + f.FieldType.Name);
                if (f.Name.ToLowerInvariant().Contains("state")) _stateField = f;
            }
            HookWithFinalizer(harmony, inner, "MoveNext");
        }
        else Plugin.Log.LogWarning("start diag: StartGame state machine type not found");

        Hook(harmony, typeof(FRNetworkServer), "OnServerConnected", null, nameof(ServerConnectedPost));

        // the game's own start coroutine + the racer possession callback: these
        // tell us whether the racers are ever spawned
        var gmInner = FindInner(typeof(GameManager), "StartGame");
        if (gmInner != null)
        {
            Plugin.Log.LogInfo("start diag: hooking GameManager state machine " + gmInner.Name);
            HookWithFinalizer(harmony, gmInner, "MoveNext");
        }
        else Plugin.Log.LogWarning("start diag: GameManager StartGame state machine not found");

        Hook(harmony, typeof(HumanGamePlayer), "OnPossessed", nameof(SimplePrefix), null, "HumanGamePlayer.OnPossessed");
        Hook(harmony, typeof(LevelManager), "OnLoadingLevelLoaded", nameof(SimplePrefix), null, "LevelManager.OnLoadingLevelLoaded");
        Hook(harmony, typeof(LevelManager), "LoadedWorldLevel", nameof(SimplePrefix), null, "LevelManager.LoadedWorldLevel");
    }

    private static Type FindInner(Type owner, string fragment)
    {
        try
        {
            var nested = owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < nested.Length; i++)
                if (nested[i].Name.Contains(fragment)) return nested[i];
        }
        catch (Exception e) { Plugin.Log.LogWarning("start diag inner: " + e.GetType().Name); }
        return null;
    }

    /// <summary>
    /// The plugin calls StartGame() but ignores the returned UniTask, so an
    /// exception inside the coroutine would vanish. This finalizer surfaces it.
    /// </summary>
    private static void HookWithFinalizer(Harmony harmony, Type type, string method)
    {
        try
        {
            var target = AccessTools.Method(type, method);
            if (target == null) { Plugin.Log.LogWarning("start diag: " + type.Name + "." + method + " not found"); return; }
            var pre = new HarmonyMethod(typeof(StartDiag).GetMethod(nameof(MoveNextPrefix), BindingFlags.Static | BindingFlags.NonPublic));
            var fin = new HarmonyMethod(typeof(StartDiag).GetMethod(nameof(MoveNextFinalizer), BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(target, prefix: pre, finalizer: fin);
            Plugin.Log.LogInfo("start diag hooked (with finalizer): " + type.Name + "." + method);
        }
        catch (Exception e) { Plugin.Log.LogWarning("start diag finalizer hook: " + e.Message); }
    }

    private static Exception MoveNextFinalizer(Exception __exception)
    {
        if (__exception != null)
            Plugin.Log.LogError("start diag: StartGame coroutine threw -> " + __exception.GetType().Name + ": " + __exception.Message);
        return __exception;
    }

    private static void Hook(Harmony harmony, Type type, string method, string prefixName, string postfixName, string label = null)
    {
        try
        {
            var target = AccessTools.Method(type, method);
            if (target == null) { Plugin.Log.LogWarning("start diag: " + type.Name + "." + method + " not found"); return; }

            HarmonyMethod pre = null, post = null;
            if (prefixName != null)
                pre = new HarmonyMethod(typeof(StartDiag).GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));
            if (postfixName != null)
                post = new HarmonyMethod(typeof(StartDiag).GetMethod(postfixName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));

            if (pre != null && label != null) pre = new HarmonyMethod(pre.method) { };
            harmony.Patch(target, prefix: pre, postfix: post);
            Plugin.Log.LogInfo("start diag hooked: " + type.Name + "." + method);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("start diag " + type.Name + "." + method + ": " + e.Message);
        }
    }

    // ---- hooks
    private static readonly System.Collections.Generic.HashSet<string> _seen = new System.Collections.Generic.HashSet<string>();

    private static void SimplePrefix(MethodBase __originalMethod)
    {
        // log each hook once - per-call logging flooded the log badly
        string name = __originalMethod != null ? __originalMethod.Name : "?";
        if (_seen.Add(name)) Plugin.Log.LogInfo("start diag: " + name + " called (logged once)");
    }

    private static int _gmMoveNext;

    private static void MoveNextPrefix(object __instance)
    {
        // GameManager's coroutine and FRNetGameState's both land here; tell them
        // apart by the declaring type printed once at install time
        _gmMoveNext++;
        if (_gmMoveNext <= 4)
            Plugin.Log.LogInfo("start diag: coroutine MoveNext #" + _gmMoveNext + " (" + (__instance != null ? __instance.GetType().Name : "?") + ")");
        _moveNext++;
        int state = -99;
        try { if (_stateField != null && __instance != null) state = (int)_stateField.GetValue(__instance); } catch { }
        if (_moveNext <= 20 || _moveNext % 200 == 0)
            Plugin.Log.LogInfo("start diag: StartGame.MoveNext #" + _moveNext + " state=" + state);

        // (the periodic wait dump was removed: it flooded the log)
    }

    private static void ServerConnectedPost() { }

    private static void IsEveryoneReadyPost(ref bool __result)
    {
        if (!MpFlow.RaceStartPending) return;
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now - _lastReadyLog < 1f) return;
        _lastReadyLog = now;
        Plugin.Log.LogInfo("start diag: IsEveryoneReady -> " + __result);
    }

    private static void ServerUpdatePost()
    {
        // only interesting while a start is pending
    }
}
