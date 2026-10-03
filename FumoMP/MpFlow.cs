using UnityEngine;
using Mirror;

namespace FumoMP;

/// <summary>Explicit session state - the lobby UI is a pure function of this.</summary>
public enum Session
{
    Idle,            // nothing running
    StartingHost,    // StartHost() issued, waiting for the server to come up
    Hosting,         // we are the server
    StartingClient,  // StartClient() issued
    Connecting,      // client socket up, handshake in progress
    Connected,       // client is connected to the host
    Failed           // last attempt failed - LastError explains why
}

/// <summary>
/// Drives the game's Mirror stack. Everything goes through an explicit state
/// machine instead of fire-and-forget calls, because:
///   * NetworkManager.StopHost/StopClient are asynchronous - starting again too
///     early silently does nothing (the previous build kept reporting "started"
///     while mode stayed Offline), so a start request waits for the stop first;
///   * the real network state must be polled back, and failures must surface
///     instead of being swallowed.
/// </summary>
public static class MpFlow
{
    public static string Nickname = "";
    public static string Address = "127.0.0.1";
    public static ushort Port = 7777;
    public static string SelectedMap = "Assets/Scenes/forest.unity";

    /// <summary>Chosen Fumo (character/skin/vehicle ids sent in the connect message).</summary>
    /// <summary>0 = no items, 1 = the standard item pool, 2 = a custom pool.
    /// The game's race setup only builds items for 1 or 2, so a default of 0
    /// means item boxes do nothing and the item/skill HUD stays empty.</summary>
    public static int ItemType = 1;

    /// <summary>Laps per race (single player uses the same field).</summary>
    public static int Laps = 3;

    /// <summary>AI opponents; multi-player races are humans only.</summary>
    public static int AiCount = 0;

    public static int CharacterIdx;

    /// <summary>
    /// Harness-only: FumoMP.character=&lt;n&gt;[.&lt;skin&gt;[.&lt;vehicle&gt;]] forces this
    /// machine's character, so two instances can be made to look different and the
    /// ghost model swap can be verified on one computer.
    /// </summary>
    internal static void LoadIdentityOverride()
    {
        try
        {
            string f = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.character");
            if (!System.IO.File.Exists(f)) return;
            var parts = System.IO.File.ReadAllText(f).Trim().Split('.');
            if (parts.Length > 0) { int v; if (int.TryParse(parts[0], out v)) CharacterIdx = v; }
            if (parts.Length > 1) { int v; if (int.TryParse(parts[1], out v)) SkinIdx = v; }
            if (parts.Length > 2) { int v; if (int.TryParse(parts[2], out v)) VehicleIdx = v; }
            Plugin.Log.LogWarning("character override: character=" + CharacterIdx + " skin=" + SkinIdx + " vehicle=" + VehicleIdx);
        }
        catch { }
    }
    public static int SkinIdx;
    public static int VehicleIdx;

    /// <summary>Set by the lobby's "Start race" button; consumed by the Input.GetKeyDown patch.</summary>
    public static bool StartRequested;

    /// <summary>Free-form line for the UI (kept for compatibility).</summary>
    public static string Status = "";

    public static Session State { get; private set; } = Session.Idle;
    public static string LastError { get; private set; } = "";

    private static float _stateSince;
    private static int _pending;          // 0 = none, 1 = host, 2 = client

    public static bool InSession =>
        State == Session.StartingHost || State == Session.Hosting ||
        State == Session.StartingClient || State == Session.Connecting || State == Session.Connected;

    public static bool IsHosting => State == Session.Hosting || State == Session.StartingHost;
    public static bool IsClient =>
        State == Session.StartingClient || State == Session.Connecting || State == Session.Connected;

    public static bool IsBusy => State == Session.StartingHost || State == Session.StartingClient || State == Session.Connecting;

    public static int PlayerCount
    {
        get { try { return MpDiag.PlayerCountValue(); } catch { return 0; } }
    }

    public static void ClearError()
    {
        if (State == Session.Failed) SetState(Session.Idle, null);
    }

    private static void SetState(Session s, string error)
    {
        State = s;
        LastError = error ?? "";
        _stateSince = Time.realtimeSinceStartup;
        Plugin.Log.LogInfo("session -> " + s + (string.IsNullOrEmpty(error) ? "" : (" (" + error + ")")));
    }

    // ------------------------------------------------------------- requests

    /// <summary>Host: stops anything running first, then starts when it is safe.</summary>
    public static void RequestHost()
    {
        if (Nickname.Length == 0) { SetState(Session.Failed, "nickname required"); return; }
        if (!Prepare()) return;
        StopInternal();
        MpDiag.HardenTransport();
        _pending = 1;
        SetState(Session.StartingHost, null);
    }

    /// <summary>Join: same, for the client.</summary>
    public static void RequestJoin()
    {
        if (Nickname.Length == 0) { SetState(Session.Failed, "nickname required"); return; }
        if (!Prepare()) return;
        StopInternal();
        MpDiag.HardenTransport();
        _pending = 2;
        SetState(Session.StartingClient, null);
    }

    /// <summary>Leave the current session (or cancel a pending one).</summary>
    public static void StopAll()
    {
        try { if (IsHosting) MpNet.BroadcastRaceEnd(1); } catch { }   // tell the clients the host is gone
        StopInternal();
        MpNet.Stop();          // the kart channel goes away only when the session does
        StartRequested = false;
        _pending = 0;
        if (State != Session.Idle) SetState(Session.Idle, null);
    }

    private static string LeafName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "forest";
        int i = path.LastIndexOf('/');
        string leaf = i >= 0 ? path.Substring(i + 1) : path;
        return leaf.Replace(".unity", "");
    }

    private static bool Prepare()
    {
        var nm = NetworkManager.singleton;
        if (nm == null) { SetState(Session.Failed, "network components not ready"); return false; }
        if (!_diagnosed)
        {
            _diagnosed = true;
            try { MpDiag.Run(); } catch (System.Exception e) { Plugin.Log.LogWarning("diag: " + e.Message); }
        }
        return true;
    }

    private static void StopInternal()
    {
        try
        {
            var nm = NetworkManager.singleton;
            if (nm == null) return;
            if (nm.isNetworkActive)
            {
                if (nm.mode == NetworkManagerMode.Host || nm.mode == NetworkManagerMode.ServerOnly) nm.StopHost();
                else if (nm.mode == NetworkManagerMode.ClientOnly) nm.StopClient();
                else nm.StopServer();
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("stop: " + e.GetType().Name); }
    }

    /// <summary>Push our settings into the game right before starting.</summary>
    private static void ApplySettings()
    {
        Step("nickname", () => MpDiag.StepNickname());
        Step("racerInfo", () => MpDiag.StepSetRacerInfo(CharacterIdx, SkinIdx, VehicleIdx));
        Step("port", () => MpDiag.StepPort(Port));
        Step("map", () => MpDiag.StepSetMap());
    }

    // ----------------------------------------------------------------- tick

    /// <summary>Called every frame by the lobby host object.</summary>
    public static void Tick()
    {
        // harness-only: start the game's own single-player race, for comparison with
        // the multiplayer path (placed first: the flow below returns early offline)
        if (!_spStarted)
        {
            if (!_spLogged)
            {
                _spLogged = true;
                Plugin.Log.LogInfo("SP: flag=" + MpDiag.SpRequested + " t=" + Time.realtimeSinceStartup.ToString("0.0"));
            }
            if (MpDiag.SpRequested && Time.realtimeSinceStartup > 20f)
            {
                _spStarted = true;
                Step("StartSinglePlayer", () => MpDiag.StepStartSinglePlayer());
            }
        }

        float now = Time.realtimeSinceStartup;

        // ---- everything that belongs to the race itself runs even when the
        // Mirror manager is gone: it is destroyed on some transitions, and this
        // used to return here, which silently stopped the airborne guard, the
        // fall watchdog, the ghosts and the result table mid-race
        if (!_identityLoaded) { _identityLoaded = true; LoadIdentityOverride(); }
        MpDiag.TickCountdown(Time.deltaTime);
        MpSpin.Tick(Time.deltaTime);
        MpAirGuard.Tick(Time.deltaTime);
        MpNet.Tick(Time.deltaTime);
        MpRank.Tick(Time.deltaTime);
        MpGhost.Tick(Time.deltaTime);
        MpDiag.ModelWatch(Time.deltaTime);
        MpDiag.AnimWatch();

        // harness-only: prove the racer channel's encode/send/relay/decode path
        if (!_netTested && MpDiag.NetTestRequested)
        {
            _netTested = true;
            Step("NetSelfTest", () => { Plugin.Log.LogWarning(MpNet.SelfTest()); return null; });
        }

        var nm = NetworkManager.singleton;
        if (nm == null) return;

        // ---- the game-mode asset load, then the race it starts
        if (_modePending)
        {
            var mode = MpDiag.TakeLoadedMode();
            if (mode == null && !MpDiag.ModeLoadInFlight)
            {
                // previous key failed: continue with the next candidate
                MpDiag.BeginModeLoad();
            }
            if (mode != null)
            {
                _modePending = false;
                var merr = Step("StartViaGameMode", () => MpDiag.StepStartViaGameMode());
                if (merr != null)
                {
                    Plugin.Log.LogWarning("game mode start failed (" + merr + ") - starting directly instead");
                    Step("LocalStartGame", () => MpDiag.StepLocalStartGame());
                }
            }
        }

        // ---- a start request waits until the previous session is fully down
        if (_pending != 0)
        {
            if (nm.isNetworkActive) return;             // StopHost/StopClient is async
            bool host = _pending == 1;
            _pending = 0;
            ApplySettings();
            try
            {
                if (host)
                {
                    // listen on every interface so LAN, Tailscale and any other
                    // adapter can reach us; the UI then shows the usable addresses
                    Step("address", () => MpDiag.StepAddress("0.0.0.0"));
                    nm.StartHost();
                    Plugin.Log.LogInfo("StartHost() issued");
                }
                else
                {
                    Step("address", () => MpDiag.StepAddress(Address));
                    nm.StartClient();
                    Plugin.Log.LogInfo("StartClient() issued -> " + Address + ":" + Port);
                }
            }
            catch (System.Exception e)
            {
                SetState(Session.Failed, e.GetType().Name + ": " + e.Message);
            }
            return;
        }

        // ---- verify the real network state and surface failures
        switch (State)
        {
            case Session.StartingHost:
                if (nm.isNetworkActive && (nm.mode == NetworkManagerMode.Host || nm.mode == NetworkManagerMode.ServerOnly))
                    SetState(Session.Hosting, null);
                else if (now - _stateSince > 4f)
                    SetState(Session.Failed, "host failed - port " + Port + " may already be in use");
                break;

            case Session.Hosting:
                if (!nm.isNetworkActive)
                    SetState(Session.Failed, "server stopped");
                break;

            case Session.StartingClient:
                if (nm.isNetworkActive) SetState(Session.Connecting, null);
                else if (now - _stateSince > 4f)
                    SetState(Session.Failed, "client failed to start");
                break;

            case Session.Connecting:
                bool connected = false;
                try { connected = NetworkClient.isConnected; } catch { }
                if (connected) SetState(Session.Connected, null);
                else if (now - _stateSince > 20f)
                {
                    StopInternal();
                    SetState(Session.Failed, "connect timed out - check IP / port / firewall");
                }
                break;

            case Session.Connected:
                bool still = false;
                try { still = NetworkClient.isConnected; } catch { }
                if (!still)
                {
                    StopInternal();
                    SetState(Session.Failed, "disconnected from host");
                }
                break;
        }

        // ---- race start, phase 2: only announce the sequence once the circuit
        //      has loaded and the racers exist, otherwise StartSequence(racers)
        //      receives a null array and throws
        if (_raceRequested && _startPhase == 1)
        {
            int racers = MpDiag.RacerCount();
            bool inRace = false;
            try { inRace = GameManager.inRace; } catch { }
            bool onTrack = MpUgui.InRaceScenePublic;

            if (onTrack && racers == 0 && !_circuitStartTried)
            {
                // the circuit is up but nobody spawned: fire the game's own circuit
                // start path (this is what was never wired up for multiplayer)
                _circuitStartTried = true;
                Plugin.Log.LogInfo("no racers yet - invoking the circuit start path");
                var cerr = Step("StartCircuit", () => MpDiag.StepStartCircuitViaBehaviour());
                if (cerr != null) Plugin.Log.LogWarning("circuit start: " + cerr);
            }
            else if (onTrack && racers > 0)
            {
                // the game's own circuit start path publishes the racers and, with
                // auto start enabled, has already begun the countdown itself
                Step("BroadcastStartSequence", () => MpDiag.StepBroadcastStartSequence());
                if (inRace)
                {
                    _startPhase = 2;
                    _raceStartedAt = now;
                    Plugin.Log.LogInfo("race started by the game itself (racers=" + racers + ")");
                }
                else
                {
                    Plugin.Log.LogInfo("racers spawned (" + racers + ") - issuing the start sequence");
                    var serr = Step("LocalStartSequence", () => MpDiag.StepLocalStartSequence());
                    if (serr == null)
                    {
                        _startPhase = 2;
                        Plugin.Log.LogInfo("start sequence issued");
                    }
                }
            }
            else if (now - _lastRacerLog > 3f)
            {
                _lastRacerLog = now;
                string sc = "";
                try { sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
                Plugin.Log.LogInfo("waiting for the circuit (scene=" + sc + ", racers=" + racers + ", inRace=" + inRace + ")");
            }
            else if (now - _raceRequestedAt > 30f)
            {
                _raceRequested = false;
                _startPhase = 0;
                StartRequested = false;
                SetState(Session.Failed, "the game did not load the circuit in time");
            }
        }

        // ---- did the requested race actually start?
        if (_raceRequested)
        {
            // keep vouching for readiness while the game's coroutine waits on it
            if (now - _lastReadyPush > 0.2f)
            {
                _lastReadyPush = now;
                Step("MarkPlayersReady", () => MpDiag.StepMarkPlayersReady());
            }
            // and send the ready message the joining client is meant to send
            // (a handful of times is enough - spamming it floods the log)
            if (_readySends < 4 && now - _lastReadyMsg > 0.5f)
            {
                _readySends++;
                _lastReadyMsg = now;
                var rerr = Step("SendReady", () => MpDiag.StepSendReady());
                if (rerr == null && !_readySentLogged)
                {
                    _readySentLogged = true;
                    Plugin.Log.LogInfo("sent NetGameReady (host acts as its own client)");
                }
            }

            if (_startPhase == 2 && !_inputProbed)
            {
                _inputProbed = true;
                Step("InputProbe", () => MpDiag.InputProbe());
                Step("ClockProbe", () => MpDiag.ClockProbe());
                Step("HudProbe", () => MpDiag.HudProbe());
                Step("HudScaleProbe", () => MpDiag.HudScaleProbe());
                MpDiag.EnsureHudCamera();
                Step("RacerPose", () => MpDiag.RacerPose());
                Step("HierarchyProbe", () => MpDiag.HierarchyProbe());
                Step("MeshPaths", () => MpDiag.MeshPaths());
                Step("MotionProbe", () => MpDiag.MotionProbe());
            }
            if (MpUgui.InRaceScenePublic && _startPhase == 2)
            {
                _raceRequested = false;
                _startPhase = 0;
                StartRequested = false;
                Plugin.Log.LogInfo("race running - start confirmed (racers=" + MpDiag.RacerCount() + ")");
            Step("LapProbe", () => { MpDiag.LapProbe(); return null; });
            Step("ModelProbe3", () => { MpDiag.ModelProbe3(); return null; });
                MpUgui.HideLobby();
            }
            else
            {
                // loading the circuit takes a while; only complain if nothing at
                // all is happening (no level load, still in the menu scene)
                string scene = "";
                try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
                bool loading = scene == "loading" || scene == "selection";
                if (!loading && now - _raceRequestedAt > 25f)
                {
                    _raceRequested = false;
                    _startPhase = 0;
                    StartRequested = false;
                    // keep the session: the host stays connected and can retry Start
                    Plugin.Log.LogWarning("race did not start (scene=" + scene + ", " + MpDiag.SettingsProbe() + ")");
                    Status = "the race did not start - you are still hosting, press Start to retry";
                }
            }
        }

        // ---- which UI is on screen (debug only, once every few seconds)
        // harness-only: start the game's own single-player race for comparison
        // ---- which UI is on screen (debug only, once every few seconds)
        // harness-only: start the game's own single-player race for comparison
        // ---- which UI is on screen (debug only, once every few seconds)
        // harness-only: start the game's own single-player race for comparison
        // ---- which UI is on screen (debug only, once every few seconds)
        // harness-only: start the game's own single-player race for comparison
        if (!_spLogged)
        {
            _spLogged = true;
            Plugin.Log.LogInfo("SP: flag=" + MpDiag.SpRequested + " now=" + now.ToString("0.0"));
        }
        if (!_spStarted && MpDiag.SpRequested && now > 20f)
        {
            _spStarted = true;
            Step("StartSinglePlayer", () => MpDiag.StepStartSinglePlayer());
        }

        if (MpDiag.DebugLogging && now - _lastScreenScan > 6f)
        {
            _lastScreenScan = now;
            Step("ScreenScan", () => MpDiag.ScreenScan());
        }

        // ---- race telemetry: keeps sampling for the whole race, not just the start
        bool racingNow = false;
        try { racingNow = GameManager.inRace; } catch { }
        // ---- a finished race must not leave the game stuck on the track
        if (racingNow && MpUgui.InRaceScenePublic && _raceStartedAt > 0f && now - _raceStartedAt > 10f
            && MpDiag.RaceFinishedLocally())
        {
            if (_endSeenAt == 0f)
            {
                _endSeenAt = now;
                Plugin.Log.LogInfo("race finished - showing our own result table");
                if (IsHosting) Step("BroadcastEnd", () => { MpNet.BroadcastRaceEnd(0); return null; });
                Step("EndStateProbe", () => MpDiag.EndStateProbe());
                Step("EndScreen", () => { MpDiag.EndScreenDump(); return null; });
                if (MpDiag.FinishGameRequested) Step("FinishGame", () => MpDiag.StepFinishGame());
                Step("HideEndScreen", () => { MpDiag.HideGameEndScreens("race finished"); return null; });
                Step("ShowResults", () => { MpUgui.ShowResults("RACE RESULTS", MpDiag.ResultsBody(),
                    "the game's own result screen stays empty in this build - this one is ours. "
                    + "returns to the menu by itself in 30s"); return null; });
                _endScreenHides = 10;            // the overlay can appear a moment later
            }
            else if (_endScreenHides > 0 && now - _lastEndHide > 0.7f)
            {
                _lastEndHide = now;
                _endScreenHides--;
                Step("HideEndScreen", () => { MpDiag.HideGameEndScreens("still up"); return null; });
                // Safety net: the overlay hunt runs a few more times after the panel
                // is up, and a name match on our own objects once switched the panel
                // off a moment after it appeared. If that ever happens again, the log
                // says so and the panel comes straight back.
                Step("ResultsCheck", () => { MpUgui.KeepResultsUp(); return null; });
            }
            else if (!_endHandled && now - _endSeenAt > 30f)
            {
                _endHandled = true;
                Step("ReturnToMenu", () => MpDiag.StepReturnToMenu());
            }
        }
        else if (!racingNow && _endSeenAt != 0f)
        {
            _endSeenAt = 0f; _endHandled = false;   // back in the menus, ready for the next race
            _startSeenByHost = false;               // the next race must be accepted again
            MpNet.ResetRaceFlags();
            Step("HideResults", () => { MpUgui.HideResults(); return null; });
            Step("ResetRaceState", () => { MpDiag.ResetRaceState("back in the menus"); return null; });
        }

        // ---- one view per machine: the extra humans exist so the game spawns a
        // racer for every player, but their cameras/HUDs would split the screen
        if (racingNow && !_viewsDone)
        {
            _viewsDone = true;
            Step("RemoteViews", () => { Plugin.Log.LogInfo("views: " + MpDiag.SuppressRemoteViews() + MpDiag.RacerList()); return null; });
            // the ghosts themselves are created on demand by MpNet/MpGhost when a
            // remote kart's first packet arrives, so a player who is still loading
            // never shows up as a kart standing at the start line
        }
        else if (!racingNow && _viewsDone)
        {
            _viewsDone = false;
        }

        // The build leaves inRace true and its overlay up after a race; keep the
        // session clean whenever we are in a menu scene, so a second round can start
        // without restarting the game.
        if (!racingNow && !MpUgui.InRaceScenePublic && _raceStartedAt > 0f && now - _lastStateReset > 5f)
        {
            _lastStateReset = now;
            Step("RaceState", () => { MpDiag.ResetRaceState("in the menus"); return null; });
        }

        if (!_quitTested && MpDiag.QuitRaceTestRequested && racingNow && now - _raceStartedAt > 20f)
        {
            _quitTested = true;
            Step("QuitRaceTest", () => MpDiag.StepQuitRace());
        }

        // harness-only: force the end-of-race chain once the race has been running
        if (!_forcedEnd && MpDiag.ForceEndRequested && racingNow && now - _raceStartedAt > 25f)
        {
            _forcedEnd = true;
            Step("ForceRaceEnd", () => MpDiag.StepForceRaceEnd());
        }
        if (_forcedEnd && now - _lastEndLog > 3f)
        {
            _lastEndLog = now;
            Step("EndStateProbe", () => MpDiag.EndStateProbe());
        }

        if (MpDiag.DebugLogging && (_startPhase == 2 || racingNow) && now - _lastPoseLog > 5f)
        {
            _lastPoseLog = now;
            Step("MotionProbe", () => MpDiag.MotionProbe());
            Step("ModelProbe", () => MpDiag.ModelProbe());
            if (MpGhost.Count > 0) Step("GhostProbe", () => { Plugin.Log.LogInfo("ghost: " + MpGhost.Visibility()); return null; });
            if (MpRank.Count > 0) Step("RankProbe", () => { Plugin.Log.LogInfo("rank: pos " + MpRank.LocalPosition + "/" + MpRank.Count + "\n" + MpRank.Board()); return null; });
            if (MpDiag.DebugLogging && _driveProbes < 6 && _startPhase == 2)
            {
                _driveProbes++;
                Step("DriveProbe", () => MpDiag.DriveProbe());
                if (_driveProbes == 1) Step("LocalInputLate", () => MpDiag.StepLocalInputLate());
            }
        }

        // ---- multiplayer subsystem upkeep (independent of the session state)
        if (!_mpSceneRequested) return;
        if (Time.realtimeSinceStartup - _lastHideAttempt > 2f)
        {
            _lastHideAttempt = Time.realtimeSinceStartup;
            Step("HideGameOnlineButton", () => MpDiag.StepHideGameOnlineButton());
        }
        if (_mpReady) return;
        try
        {
            if (MpDiag.MpComponentsPresent()) { _mpReady = true; return; }
        }
        catch { return; }
        if (Time.realtimeSinceStartup - _lastMpAttempt < 2f) return;
        _lastMpAttempt = Time.realtimeSinceStartup;
        Step("ActivateMultiplayer", () => MpDiag.StepActivateMultiplayer());
    }

    // ------------------------------------------------------------- helpers

    private static bool _mpSceneRequested;
    private static bool _mpReady;
    private static float _lastMpAttempt = -10f;
    private static float _lastHideAttempt = -10f;
    private static bool _diagnosed;

    /// <summary>Make sure the net subsystem is live (called when the lobby opens).</summary>
    public static void EnsureMpScene()
    {
        if (_mpSceneRequested) return;
        _mpSceneRequested = true;
        Step("ActivateMultiplayer", () => MpDiag.StepActivateMultiplayer());
        Step("HideGameOnlineButton", () => MpDiag.StepHideGameOnlineButton());
    }

    /// <summary>Ask the game to start the race (host only).</summary>
    private static bool _raceRequested;
    private static float _raceRequestedAt;
    private static float _lastReadyPush;
    private static float _lastReadyMsg;
    private static int _startPhase;
    private static bool _inputProbed;
    private static float _lastPoseLog;
    private static int _driveProbes;
    private static bool _forcedEnd;
    private static float _raceStartedAt;
    private static float _lastEndLog;
    private static float _endSeenAt;
    private static bool _endHandled;
    private static bool _viewsDone;
    private static bool _netTested;
    private static bool _identityLoaded;
    private static int _endScreenHides;
    private static float _lastEndHide, _lastStateReset;
    private static bool _quitTested;
    private static float _lastScreenScan;
    private static bool _spStarted;
    private static bool _spLogged;
    private static float _lastRacerLog;
    private static bool _localStartDone;
    private static bool _modePending;
    private static bool _noArgTried;
    private static bool _circuitStartTried;
    private static bool _readySentLogged;
    private static int _readySends;

    /// <summary>True between asking the game to start and the race scene loading.</summary>
    public static bool RaceStartPending => _raceRequested;

    /// <summary>
    /// Start the race (host only) by calling the game's own entry point. The
    /// keypress route does not work: FRNetGameState.ServerUpdate reads the key
    /// through an internal Input helper that IL2CPP inlined, so a Harmony patch on
    /// the public Input.GetKeyDown never saw it. StartGame(GameSettings) is public
    /// and receives the same settings object the game itself passes.
    /// </summary>
    public static void RequestStart() { RequestStart(false); }

    /// <summary>Connect the racer channel's control messages to the flow.</summary>
    public static void HookNetControl()
    {
        MpNet.RaceStartReceived = (map, laps, item) =>
        {
            if (!string.IsNullOrEmpty(map) && map != SelectedMap)
            {
                SelectedMap = map;
                Plugin.Log.LogInfo("net: map from host -> " + SelectedMap);
            }
            Laps = laps > 0 ? laps : Laps;
            ItemType = item;
            RequestStartFromHost();
        };

        // The host says the race is over (or that it is leaving). A client that is
        // still driving stops scoring and gets the same result table as everybody
        // else instead of being left alone on the track.
        MpNet.RaceEndReceived = (reason) =>
        {
            bool racing = false;
            try { racing = GameManager.inRace; } catch { }
            if (reason == 1 || !racing)
            {
                Plugin.Log.LogWarning("net: leaving the race because the host is gone");
                EndRaceEverywhere("the host left the session");
                return;
            }
            Plugin.Log.LogInfo("net: host ended the race - showing the results");
            EndRaceEverywhere("the host finished the race");
        };
    }

    /// <summary>Show the result table and give the game a moment, then go to the menu.</summary>
    private static void EndRaceEverywhere(string why)
    {
        try
        {
            if (_endSeenAt == 0f) _endSeenAt = Time.realtimeSinceStartup;
            Step("ShowResults", () =>
            {
                MpUgui.ShowResults("RACE RESULTS", MpDiag.ResultsBody(), why
                    + " - returns to the menu by itself in 30s");
                return null;
            });
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("end race: " + e.Message); }
    }

    /// <summary>
    /// The host telling this machine to start its own copy of the same race. Both
    /// machines then run the identical local start path - the game's own netcode
    /// never loads a scene for a client (our host starts through the Addressables
    /// game mode, not through Mirror's scene management), so this is what makes a
    /// joined player actually end up on the track.
    /// </summary>
    public static void RequestStartFromHost()
    {
        if (_startSeenByHost) { Plugin.Log.LogInfo("start from host ignored: already handled"); return; }
        _startSeenByHost = true;
        RequestStart(true);
    }

    private static bool _startSeenByHost;

    private static void RequestStart(bool fromHost)
    {
        if (!IsHosting && !fromHost)
        {
            Status = "only the host can start the race";
            return;
        }

        // A second start in the same session spawns a second kart, camera, HUD and
        // input object on top of the first (the game's start path is not
        // re-entrant): two karts fight over one spot, everything renders twice and
        // the session can never be cleaned up again. Refuse it.
        // Not GameManager.inRace: after a race this build leaves it true, and the
        // guard then refuses every later start ("a race is already in progress"),
        // which is what forced a game restart between rounds. The loaded scene is the
        // honest answer.
        bool alreadyRacing = MpUgui.InRaceScenePublic;
        if (_raceRequested || _startPhase != 0 || alreadyRacing)
        {
            Plugin.Log.LogWarning("start ignored: a race is already starting or running (phase=" + _startPhase
                                  + " requested=" + _raceRequested + " inRace=" + alreadyRacing + ")");
            Status = "a race is already in progress";
            return;
        }

        StartRequested = true;   // kept as a secondary trigger
        _noArgTried = false;
        _circuitStartTried = false;

        // The game's own start sequence needs the circuit to exist first: its
        // racer spawn happens on the loaded track, and StartSequence(racers) then
        // throws if no racers were spawned. So: participants -> load circuit ->
        // (on the track) let the game spawn racers -> announce the sequence.
        var perr = Step("BuildPlayers", () => MpDiag.StepBuildPlayers());
        if (perr != null)
        {
            StartRequested = false;
            SetState(Session.Failed, "could not prepare the race: " + perr);
            return;
        }

        Plugin.Log.LogInfo("settings before start: " + MpDiag.SettingsProbe());
        // single player's path: let a GameMode object own the race start (and its
        // end); fall back to the direct start only if that is unavailable
        // Single player starts the race through a GameMode object (its Init wires the
        // HUD, the finish handling and the level load). That prefab lives in
        // Addressables, so the load is asynchronous: start it now and finish in Tick.
        Step("ModeAssets", () => MpDiag.StepProbeModeAssets());
        var begin = Step("BeginModeLoad", () => MpDiag.BeginModeLoad());
        if (begin == null)
        {
            _modePending = true;
            Plugin.Log.LogInfo("waiting for the game mode asset");
        }
        else
        {
            Plugin.Log.LogWarning("game mode asset unavailable (" + begin + ") - starting directly");
            Step("LocalStartGame", () => MpDiag.StepLocalStartGame());
        }
        _localStartDone = true;
        _raceRequested = true;
        _raceRequestedAt = Time.realtimeSinceStartup;
        _startPhase = 1;
        _readySentLogged = false;
        _readySends = 0;
        Plugin.Log.LogInfo("race start requested (game mode path)");
        if (IsHosting) MpNet.BroadcastRaceStart(SelectedMap, Laps, ItemType);
        return;

        _raceRequested = true;
        _raceRequestedAt = Time.realtimeSinceStartup;
        _startPhase = 1;              // waiting for the racers to spawn
        _readySentLogged = false;
        _readySends = 0;
        Plugin.Log.LogInfo("race start requested (FRNetGameState.StartGame)");
    }

    private static string Step(string label, System.Func<string> func)
    {
        try
        {
            var problem = func();
            if (problem != null) Plugin.Log.LogWarning(label + ": " + problem);
            return problem;
        }
        catch (System.Exception e)
        {
            string msg = label + ": " + e.GetType().Name + ": " + e.Message;
            Plugin.Log.LogError(msg);
            return e.GetType().Name + ": " + e.Message;
        }
    }
}
