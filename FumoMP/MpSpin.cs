using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace FumoMP;

/// <summary>
/// Root-cause instrumentation for "the kart tumbles as soon as you steer".
///
/// Two things were missing from the earlier watchdog:
///   1. it only measured yaw (eulerAngles.y), so a pitch/roll tumble was invisible
///      and a spinning ancestor was blamed on whichever leaf happened to move;
///   2. it never said who *wrote* the rotation.
///
/// This file measures the full world rotation delta of every transform under the
/// racer, works out which node is the shallowest one that actually rotates (its
/// parent standing still means the rotation is generated there), reports the axis
/// of the delta in the parent's frame (so yaw/pitch/roll are distinguishable),
/// and - while a tumble is happening - logs what VehicleCharacter.Update does to
/// the root transform plus the ground/turn/wheelie state that drives it.
///
/// It only reads. The single exception is the optional synthetic driver below,
/// which exists so the bug can be reproduced on a machine that cannot deliver
/// keyboard input: it is off unless FumoMP.drive exists in the plugin folder.
/// </summary>
internal static class MpSpin
{
    private static bool FlagFile(string name)
    {
        try { return System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.PluginPath, name)); }
        catch { return false; }
    }

    internal static bool DriveArmed { get { return FlagFile("FumoMP.drive"); } }
    private static bool Pinning { get { return FlagFile("FumoMP.pin"); } }

    // ------------------------------------------------------------------ nodes
    private sealed class N
    {
        public Transform t;
        public string path;
        public int depth;
        public int up = -1;
        public Quaternion prev;
        public float acc;      // accumulated angle over the window
        public float win;      // window length
        public Vector3 axis;   // accumulated delta axis (parent frame)
        public float axisN;
    }

    private static readonly List<N> _nodes = new List<N>();
    private static Transform _root;
    private static float _reportAt;
    private static float _traceUntil;
    private static int _traceLines;
    private static float _nextTrace = -1f;
    private static bool _announced;

    // the four nodes the tumble has been seen on, sampled every frame
    private static readonly string[] _fastPaths = { "", "/Character", "/Character/VehiclePivot", "/Character/VehiclePivot/AnimPivot" };

    internal static void Tick(float dt)
    {
        try
        {
            bool inRace = false;
            try { inRace = GameManager.inRace; } catch { }
            var racers = GameManager.racers;
            if (!inRace || racers == null || racers.Length == 0 || racers[0] == null)
            {
                if (_nodes.Count > 0) { _nodes.Clear(); _root = null; _announced = false; }
                return;
            }
            if (_root != racers[0].transform) { _nodes.Clear(); _root = racers[0].transform; _announced = false; }

            if (_nodes.Count == 0)
            {
                Build(_root, "", 0, -1, 14);
                if (!_announced)
                {
                    _announced = true;
                    Plugin.Log.LogInfo("spin probe: " + _nodes.Count + " transforms under '" + _root.name
                                       + "' (rotation axis is reported in the parent's frame)");
                }
            }

            FastSample(dt);
            WindowSample(dt);
            StateLog(dt);
            Drive(dt);
            MpDamageProbe.MaybeTestHurt();
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("spin probe: " + e.Message); }
    }

    private static void Build(Transform t, string path, int depth, int up, int maxDepth)
    {
        if (t == null || depth > maxDepth) return;
        var n = new N { t = t, path = path, depth = depth, up = up, prev = SafeRot(t) };
        _nodes.Add(n);
        int idx = _nodes.Count - 1;
        int c = 0;
        try { c = t.childCount; } catch { }
        for (int i = 0; i < c; i++)
        {
            Transform ch = null;
            try { ch = t.GetChild(i); } catch { }
            if (ch == null) continue;
            string name = "";
            try { name = ch.name; } catch { }
            Build(ch, path + "/" + name, depth + 1, idx, maxDepth);
        }
    }

    private static Quaternion SafeRot(Transform t)
    {
        try { return t.rotation; } catch { return Quaternion.identity; }
    }

    /// <summary>
    /// Fast trigger: the four nodes the tumble shows up on. A per-frame step over
    /// ~4 degrees (240 deg/s) means something is spinning the model right now.
    /// </summary>
    private static void FastSample(float dt)
    {
        if (dt <= 0f) return;
        float worst = 0f; string worstPath = null; float worstStep = 0f;
        for (int i = 0; i < _fastPaths.Length; i++)
        {
            N n = Find(_fastPaths[i]);
            if (n == null) continue;
            var q = SafeRot(n.t);
            float step = Quaternion.Angle(n.prev, q);
            n.prev = q;
            if (step > worstStep) { worstStep = step; worst = step / dt; worstPath = n.path.Length == 0 ? "(root)" : n.path; }
        }
        if (worst > 240f && Time.time >= _nextTrace)
        {
            _nextTrace = Time.time + 4f;
            _traceUntil = Time.time + 1.5f;
            _traceLines = 0;
            Plugin.Log.LogWarning("TUMBLE: '" + worstPath + "' turned " + worst.ToString("0")
                                  + " deg/s in one frame - tracing who writes the rotation");
        }
    }

    private static N Find(string path)
    {
        for (int i = 0; i < _nodes.Count; i++) if (_nodes[i].path == path) return _nodes[i];
        return null;
    }

    /// <summary>
    /// Full hierarchy pass over a one second window: rate per node, and the axis of
    /// the world delta expressed in the parent's local frame.
    /// </summary>
    private static void WindowSample(float dt)
    {
        float worstRate = 0f;
        for (int i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            if (n.t == null) continue;
            var q = SafeRot(n.t);
            float a = Quaternion.Angle(n.prev, q);
            if (a > 0.0001f)
            {
                var dq = q * Quaternion.Inverse(n.prev);       // world delta
                Vector3 ax;
                float ang;
                dq.ToAngleAxis(out ang, out ax);
                n.axis += ax * ang;
                n.axisN += ang;
            }
            n.prev = q;
            n.acc += a;
            n.win += dt;
        }

        // is any window finished?
        bool ready = false;
        for (int i = 0; i < _nodes.Count; i++) if (_nodes[i].win >= 1f) { ready = true; break; }
        if (!ready) return;

        int best = -1;
        for (int i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            float rate = n.win > 0f ? n.acc / n.win : 0f;
            if (rate > worstRate) { worstRate = rate; best = i; }
            n.acc = 0f; n.win = 0f;
            n.axis = Vector3.zero; n.axisN = 0f;
        }

        if (best < 0 || worstRate < 120f) return;
        var w = _nodes[best];
        if (Time.time - _reportAt < 2f) return;
        _reportAt = Time.time;

        // the shallowest node on that branch whose own parent is standing still
        N source = w;
        while (source.up >= 0)
        {
            var p = _nodes[source.up];
            if (p.acc > 0f || p.win > 0f) { }               // windows were just reset; recompute below
            break;
        }
        source = Source(w);

        var sb = new System.Text.StringBuilder();
        sb.Append("ROT: worst='").Append(w.path).Append("' ").Append(worstRate.ToString("0")).Append(" deg/s");
        sb.Append("\n   source='").Append(source == null ? "?" : (source.path.Length == 0 ? "(root)" : source.path))
          .Append("' depth=").Append(source == null ? -1 : source.depth);
        if (source != null && source.axisN > 0.001f)
        {
            var ax = source.axis / source.axisN;
            sb.Append(" axis(parent frame)=(").Append(ax.x.ToString("0.00")).Append(",").Append(ax.y.ToString("0.00"))
              .Append(",").Append(ax.z.ToString("0.00")).Append(")");
            sb.Append(" kind=").Append(Kind(ax));
        }
        // chain of names from the root down to the worst node
        var chain = new List<N>();
        for (N p = w; p != null; p = p.up >= 0 ? _nodes[p.up] : null) chain.Add(p);
        chain.Reverse();
        sb.Append("\n   chain:");
        for (int i = 0; i < chain.Count && i < 12; i++)
            sb.Append(' ').Append(chain[i].path.Length == 0 ? "(root)" : chain[i].path.Substring(chain[i].path.LastIndexOf('/') + 1));
        sb.Append("\n   ").Append(VehicleState());
        sb.Append("\n   ").Append(RotState());
        sb.Append(AnimDump());
        Plugin.Log.LogWarning(sb.ToString());

        _traceUntil = Time.time + 1.5f;
        _traceLines = 0;
    }

    /// <summary>Walk up while the parent's window rate stays far below the child's.</summary>
    private static N Source(N w)
    {
        // the accumulated axis of every ancestor was reset with the window, so the
        // decision uses the per-frame step of the fast nodes when available
        N cur = w;
        while (cur.up >= 0)
        {
            var p = _nodes[cur.up];
            float a = Quaternion.Angle(p.prev, SafeRot(p.t));
            float b = Quaternion.Angle(cur.prev, SafeRot(cur.t));
            if (b <= a * 1.5f + 0.01f) cur = p; else break;
        }
        return cur;
    }

    private static string Kind(Vector3 ax)
    {
        if (Mathf.Abs(ax.y) > 0.9f) return "YAW";
        if (Mathf.Abs(ax.x) > 0.9f) return "PITCH";
        if (Mathf.Abs(ax.z) > 0.9f) return "ROLL";
        return "mixed";
    }

    private static string RotState()
    {
        try
        {
            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0 || racers[0] == null) return "no racer";
            var vc = racers[0].vc;
            if (vc == null) return "no vehicle";
            var sb = new System.Text.StringBuilder();
            var tr = (vc as Component) != null ? (vc as Component).transform : null;
            if (tr != null)
                sb.Append("root euler=").Append(Fmt(tr.eulerAngles))
                  .Append(" local=").Append(Fmt(tr.localEulerAngles)).Append("  ");
            sb.Append("up=").Append(Vec(vc, "_upNormal"))
              .Append(" hit=").Append(Vec(vc, "_hitNormal"))
              .Append(" groundAngle=").Append(MpDiag.Fnum(vc, "_groundAngle"))
              .Append(" grounded=").Append(MpDiag.Flag(vc, "_isGrounded"))
              .Append(" afterGrounded=").Append(MpDiag.Flag(vc, "_afterGrounded"))
              .Append(" airTime=").Append(MpDiag.Fnum(vc, "_airboneTime"))
              .Append(" turnedOnAir=").Append(MpDiag.Fnum(vc, "_turnedOnAir"))
              .Append(" flips=").Append(MpDiag.Fnum(vc, "_flips"))
              .Append(" wheeling=").Append(MpDiag.Flag(vc, "_wheeling"))
              .Append(" wheelT=").Append(MpDiag.Fnum(vc, "_wheelingT"))
              .Append(" wheelInput=").Append(MpDiag.Fnum(vc, "_wheelingInput"))
              .Append(" wheelDir=").Append(MpDiag.Fnum(vc, "_wheelingDir"))
              .Append(" extraSteer=").Append(MpDiag.Fnum(vc, "_extraSteer"))
              .Append(" wishTurnAdd=").Append(MpDiag.Fnum(vc, "_wishTurnAdd"))
              .Append(" smooth=").Append(MpDiag.Fnum(vc, "_smooth"))
              .Append(" turnAirMult=").Append(MpDiag.Fnum(vc, "_turnAirMult"))
              .Append(" turnVelAirMult=").Append(MpDiag.Fnum(vc, "_turnVelAirMult"))
              .Append(" turnLowMult=").Append(MpDiag.Fnum(vc, "_turnLowMult"))
              .Append(" turnMax=").Append(MpDiag.Fnum(vc, "_turnMax"))
              .Append(" turnForce=").Append(MpDiag.Fnum(vc, "_turnForce"))
              .Append(" reverse=").Append(MpDiag.Fnum(vc, "_reversed"))
              .Append(" slopeLimit=").Append(SlopeLimit(vc));
            return sb.ToString();
        }
        catch (System.Exception e) { return "rot state: " + e.GetType().Name; }
    }

    /// <summary>Reads a nested member (object field then one more member on it).</summary>
    private static string Fmt(Vector3 v) { return "(" + v.x.ToString("0") + "," + v.y.ToString("0") + "," + v.z.ToString("0") + ")"; }

    private static string Vec(object o, string field)
    {
        try
        {
            var m = MpDiag.Member(o, field);
            object v = m is System.Reflection.PropertyInfo p ? p.GetValue(o) : (m is System.Reflection.FieldInfo f ? f.GetValue(o) : null);
            if (v is Vector3 vv) return Fmt(vv);
            return v == null ? "?" : "?";
        }
        catch { return "?"; }
    }

    /// <summary>The CharacterController's slope limit decides what counts as ground.</summary>
    private static string SlopeLimit(object vc)
    {
        try
        {
            var m = MpDiag.Member(vc, "_body");
            object body = m is System.Reflection.PropertyInfo p ? p.GetValue(vc) : (m is System.Reflection.FieldInfo f ? f.GetValue(vc) : null);
            if (body == null) return "?";
            var sm = MpDiag.Member(body, "slopeLimit");
            object v = sm is System.Reflection.PropertyInfo sp ? sp.GetValue(body) : (sm is System.Reflection.FieldInfo sf ? sf.GetValue(body) : null);
            return v == null ? "?" : System.Convert.ToDouble(v).ToString("0.0");
        }
        catch { return "?"; }
    }

    private static string VehicleState()    {
        try
        {
            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0 || racers[0] == null) return "no racer";
            var vc = racers[0].vc;
            if (vc == null) return "no vehicle";
            return "vehicle: wishTurn=" + MpDiag.Fnum(vc, "_wishTurn")
                 + " currentTurn=" + MpDiag.Fnum(vc, "_currentTurn")
                 + " speed=" + MpDiag.Fnum(vc, "_currentSpeed")
                 + " wishAccel=" + MpDiag.Fnum(vc, "_wishAccel")
                 + " driftXDir=" + MpDiag.Fnum(vc, "_driftXDir")
                 + " wishSpin=" + MpDiag.Flag(vc, "_wishSpin")
                 + " wishJump=" + MpDiag.Flag(vc, "_wishJump")
                 + " wishbreak=" + MpDiag.Flag(vc, "_wishbreak");
        }
        catch (System.Exception e) { return "vehicle: " + e.GetType().Name; }
    }

    // ------------------------------------------------- VehicleCharacter.Update
    private static Quaternion _preRot;
    private static Vector3 _preUp, _preHit;
    private static object _preObj;

    internal static void OnVcUpdatePre(object inst)
    {
        if (Time.time > _traceUntil || inst == null) return;
        _preObj = inst;
        try
        {
            var tr = (inst as Component) != null ? (inst as Component).transform : null;
            _preRot = tr != null ? tr.rotation : Quaternion.identity;
        }
        catch { _preRot = Quaternion.identity; }
        _preUp = RawVec(inst, "_upNormal");
        _preHit = RawVec(inst, "_hitNormal");
    }

    internal static void OnVcUpdatePost(object inst)
    {
        if (Time.time > _traceUntil || inst == null || !ReferenceEquals(inst, _preObj)) return;
        if (_traceLines >= 8) { _traceUntil = 0f; return; }
        _traceLines++;
        try
        {
            var tr = (inst as Component) != null ? (inst as Component).transform : null;
            float d = tr != null ? Quaternion.Angle(_preRot, tr.rotation) : 0f;
            Plugin.Log.LogWarning("VCTRACE step=" + d.ToString("0.0") + "deg"
                + " rot=" + (tr != null ? Fmt(tr.eulerAngles) : "?")
                + " up " + Fmt(_preUp) + "->" + Fmt(RawVec(inst, "_upNormal"))
                + " hit " + Fmt(_preHit) + "->" + Fmt(RawVec(inst, "_hitNormal"))
                + "\n   " + RotState());
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("VCTRACE: " + e.Message); }
    }

    private static Vector3 RawVec(object o, string field)
    {
        try
        {
            var m = MpDiag.Member(o, field);
            object v = m is System.Reflection.PropertyInfo p ? p.GetValue(o) : (m is System.Reflection.FieldInfo f ? f.GetValue(o) : null);
            return v is Vector3 vv ? vv : Vector3.zero;
        }
        catch { return Vector3.zero; }
    }

    // ------------------------------------------------------- synthetic driver
    private static float _driveAt;
    private static int _driveStep;


    /// <summary>
    /// What every animator under the racer is playing. A model that tumbles on its
    /// own is usually a skeletal animation state, and this names it.
    /// </summary>
    private static string AnimDump()
    {
        var sb = new System.Text.StringBuilder();
        int shown = 0;
        for (int i = 0; i < _nodes.Count && shown < 4; i++)
        {
            var n = _nodes[i];
            if (n.t == null) continue;
            Animator a = null;
            try { a = n.t.GetComponent<Animator>(); } catch { }
            if (a == null) continue;
            shown++;
            try
            {
                sb.Append("\n   anim '").Append(n.path.Length == 0 ? "(root)" : n.path).Append("'")
                  .Append(" enabled=").Append(a.enabled)
                  .Append(" speed=").Append(a.speed.ToString("0.00"))
                  .Append(" rootMotion=").Append(a.applyRootMotion)
                  .Append(" cull=").Append(a.cullingMode.ToString())
                  .Append(" layers=").Append(a.layerCount);
                if (a.layerCount > 0)
                {
                    var st = a.GetCurrentAnimatorStateInfo(0);
                    sb.Append(" state(0) norm=").Append(st.normalizedTime.ToString("0.00"))
                      .Append(" len=").Append(st.length.ToString("0.00"))
                      .Append(" loop=").Append(st.loop)
                      .Append(" speed=").Append(st.speed.ToString("0.00"));
                    if (a.IsInTransition(0))
                    {
                        var nx = a.GetNextAnimatorStateInfo(0);
                        sb.Append(" ->transition norm=").Append(nx.normalizedTime.ToString("0.00"));
                    }
                    var clips = a.GetCurrentAnimatorClipInfo(0);
                    if (clips != null)
                    {
                        sb.Append(" clips=[");
                        for (int c = 0; c < clips.Length && c < 4; c++)
                        {
                            string cn = "?";
                            try { cn = clips[c].clip == null ? "null" : clips[c].clip.name; } catch { }
                            sb.Append(cn).Append(":").Append(clips[c].weight.ToString("0.00")).Append(" ");
                        }
                        sb.Append("]");
                    }
                }
            }
            catch (System.Exception e) { sb.Append(" anim err:").Append(e.GetType().Name); }
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------- state dump
    private static bool _stateArmed;
    private static int _stateLines;
    private static int _stateFrame;
    private static int _stateUpdSeen;
    private static System.Text.StringBuilder _stateBuf;

    internal static void CountUpdateCall() { _stateUpdSeen++; }

    /// <summary>
    /// One frame per line with everything Update branches on. This is what turns
    /// "it does not turn" into a specific branch and a specific flag.
    /// Inert unless FumoMP.state exists.
    /// </summary>
    private static void StateLog(float dt)
    {
        if (!FlagFile("FumoMP.state")) return;
        if (!_stateArmed)
        {
            _stateArmed = true;
            _stateBuf = new System.Text.StringBuilder();
            Plugin.Log.LogInfo("state trace armed (FumoMP.state): " + (++_stateDumps) + ". dump");
        }
        if (_stateLines >= 900) return;

        try
        {
            // drive the racer this machine's player owns - the racer array is
            // ordered the other way round from the human slots (racers[last] is
            // human 0), so index 0 is somebody else's kart in a full field
            var local = MpDiag.LocalRacer();
            if (local == null) return;
            var vc = local.vc;
            if (vc == null) return;
            var tr = (vc as Component) != null ? (vc as Component).transform : null;

            _stateFrame++;
            _stateLines++;
            _stateBuf.Append("\n  f").Append(_stateFrame)
                .Append(" dt=").Append(dt.ToString("0.0000"))
                .Append(" scale=").Append(Time.timeScale.ToString("0.00"))
                .Append(" upd=").Append(_stateUpdSeen)
                .Append(" freeze=").Append(MpDiag.Flag(vc, "_isFreeze"))
                .Append(" grnd=").Append(MpDiag.Flag(vc, "_isGrounded"))
                .Append(" valid=").Append(MpDiag.Flag(vc, "_validGround"))
                .Append(" flags=").Append(Flags(vc))
                .Append(" ccGrnd=").Append(CcGrounded(vc))
                .Append(" crashT=").Append(MpDiag.Fnum(vc, "_crashT"))
                .Append(" air=").Append(MpDiag.Fnum(vc, "_airboneTime"))
                .Append(" after=").Append(MpDiag.Flag(vc, "_afterGrounded"))
                .Append(" turn=").Append(MpDiag.Fnum(vc, "_currentTurn"))
                .Append(" wishTurn=").Append(MpDiag.Fnum(vc, "_wishTurn"))
                .Append(" spd=").Append(MpDiag.Fnum(vc, "_currentSpeed"))
                .Append(" acc=").Append(MpDiag.Fnum(vc, "_wishAccel"))
                .Append(" rot=").Append(tr != null ? Fmt(tr.eulerAngles) : "?")
                .Append(" pos=").Append(tr != null ? Fmt(tr.position) : "?")
                .Append(" active=").Append(Active(vc))
                .Append(" en=").Append(Enabled(vc));
            if (_stateLines % 20 == 0)
            {
                Plugin.Log.LogInfo("STATE:" + _stateBuf);
                _stateBuf.Length = 0;
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("state: " + e.Message); }
    }

    private static int _stateDumps;

    private static string Active(object vc)
    {
        try { var c = vc as Component; return c == null ? "?" : c.gameObject.activeInHierarchy.ToString(); }
        catch { return "?"; }
    }

    private static string Enabled(object vc)
    {
        try { var b = vc as Behaviour; return b == null ? "?" : b.enabled.ToString(); }
        catch { return "?"; }
    }

    /// <summary>CharacterController.collisionFlags - bit 4 is "below" (on the ground).</summary>
    private static string Flags(object vc)
    {
        try
        {
            var m = MpDiag.Member(vc, "flags");
            object v = m is System.Reflection.PropertyInfo p ? p.GetValue(vc) : (m is System.Reflection.FieldInfo f ? f.GetValue(vc) : null);
            return v == null ? "?" : System.Convert.ToInt32(v).ToString();
        }
        catch { return "?"; }
    }

    private static string CcGrounded(object vc)
    {
        try
        {
            var m = MpDiag.Member(vc, "_body");
            object body = m is System.Reflection.PropertyInfo p ? p.GetValue(vc) : (m is System.Reflection.FieldInfo f ? f.GetValue(vc) : null);
            if (body == null) return "?";
            var gm = MpDiag.Member(body, "isGrounded");
            object v = gm is System.Reflection.PropertyInfo gp ? gp.GetValue(body) : (gm is System.Reflection.FieldInfo gf ? gf.GetValue(body) : null);
            return v == null ? "?" : v.ToString();
        }
        catch { return "?"; }
    }

    /// <summary>
    /// Drives the local kart without any input device: this container cannot deliver
    /// keystrokes to the game (the new Input System ignores them here), so the only
    /// way to reproduce "steer and it tumbles" locally is to call the two methods the
    /// game's own input callbacks call. Inert unless FumoMP.drive exists.
    /// </summary>
    private static void Drive(float dt)
    {
        try
        {
            if (!DriveArmed) return;
            try { Application.runInBackground = true; } catch { }
            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0 || racers[0] == null) return;
            var vc = racers[0].vc;
            if (vc == null) return;

            var inp = GameManager.players;
            if (inp == null || inp.Length == 0) return;

            _driveAt += dt;
            if (_driveAt < 1f) return;          // let the countdown finish
            _driveStep = (int)((_driveAt - 1f) / 4f) % 2;
            float x = _driveStep == 0 ? 1f : -1f;
            TurnAccel(vc, x, 1f);
            if ((int)(_driveAt) % 5 == 0 && (int)(_driveAt - dt) % 5 != 0)
                Plugin.Log.LogInfo("drive: synthetic input x=" + x + " accel=1 (FumoMP.drive)");
            AirTest(vc, dt);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("drive: " + e.Message); }
    }

    /// <summary>
    /// Harness-only: keeps the kart off the ground so the airborne guard can be
    /// exercised without a ramp (inert unless FumoMP.airtest exists).
    /// </summary>
    internal static bool AirTestArmed { get { return FlagFile("FumoMP.airtest"); } }

    private static void AirTest(object vc, float dt)
    {
        try
        {
            if (!AirTestArmed) return;
            if (_airTestAt == 0f) _airTestAt = _driveAt;
            float t = _driveAt - _airTestAt;
            if (t < 5f) return;
            if (_lastAirState == 0f || t - _lastAirState > 1f) { _lastAirState = t; AirTestState(vc); }
            // second phase: tilt the kart hard while it drives, to exercise the
            // self-righting safety (the "it never comes back" case)
            if (_driveAt > 16f && !_tilted)
            {
                _tilted = true;
                var c = (Component)vc;
                var e = c.transform.eulerAngles;
                // reproduce the reported case: physically on the track, but the game's
                // airborne flag is wrong, so its own ground alignment never runs
                MpDiag.SetNum(vc, "_airboneTime", 2.5f);
                MpDiag.SetBool(vc, "_isGrounded", false);
                c.transform.rotation = Quaternion.Euler(e.x, e.y, 62f);
                Plugin.Log.LogWarning("airtest: tilted the kart to roll 62 deg at t=" + _driveAt.ToString("F1")
                                      + "s - it must level itself out");
            }
            object cc = null;
            try { cc = ((VehicleCharacter)vc).cc; } catch (System.Exception e) { Plugin.Log.LogWarning("airtest: cc: " + e.Message); }
            if (cc == null) { if (!_airTestLogged) { _airTestLogged = true; Plugin.Log.LogWarning("airtest: no CharacterController on the racer"); } return; }
            var move = AccessTools.Method(cc.GetType(), "Move", new[] { typeof(Vector3) });
            if (move == null) { Plugin.Log.LogWarning("airtest: cc.Move not found"); return; }
            move.Invoke(cc, new object[] { new Vector3(0f, 150f, 0f) });
            _airTestAt = _driveAt;   // one big lift, then let it fall
            _airTestLift = (_airTestLift ?? 0f) + 1f;
            var comp = (Component)vc;
            Plugin.Log.LogWarning("airtest: lifted '" + comp.gameObject.name + "' to y="
                + comp.transform.position.y.ToString("F1") + " air=" + MpDiag.Num(vc, "_airboneTime").ToString("F2")
                + " grounded=" + MpDiag.Bool(vc, "_isGrounded"));
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("airtest: " + e.Message); }
    }

    private static float? _airTestLift;
    private static bool _tilted;

    private static void AirTestState(object vc)
    {
        try
        {
            var comp = (Component)vc;
            Plugin.Log.LogWarning("airtest state: y=" + comp.transform.position.y.ToString("F1")
                + " air=" + MpDiag.Num(vc, "_airboneTime").ToString("F2")
                + " grounded=" + MpDiag.Bool(vc, "_isGrounded")
                + " speed=" + MpDiag.Num(vc, "_currentSpeed").ToString("F2"));
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("airtest: " + e.Message); }
    }

    private static float _airTestAt;
    private static float _lastAirState;
    private static bool _airTestLogged;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TurnAccel(object vc, float x, float accel)
    {
        // isolated: a missing member throws while this method is JIT-compiled
        var m = AccessTools.Method(vc.GetType(), "Turn", new[] { typeof(float), typeof(bool) });
        if (m != null)
        {
            m.Invoke(vc, new object[] { x, false });
            // The game ramps the steer angle with turnForce * deltaTime, which on a
            // 3 fps software renderer overshoots the max angle in one frame and leaves
            // the kart unable to steer at all. absolute=true writes the angle straight
            // into the vehicle, so this drive test works at any frame rate.
            m.Invoke(vc, new object[] { x * 25f, true });
        }
        var a = AccessTools.Method(vc.GetType(), "Accelerate", new[] { typeof(float) });
        if (a != null) a.Invoke(vc, new object[] { accel });
    }

    internal static bool WantPin { get { return Pinning; } }
}

/// <summary>Hooks VehicleCharacter.Update while a tumble is being traced.</summary>
internal static class MpVcTracePatch
{
    internal static void TryApply(Harmony harmony)
    {
        try
        {
            var t = AccessTools.TypeByName("VehicleCharacter");
            if (t == null) { Plugin.Log.LogWarning("vc trace: VehicleCharacter not found"); return; }
            var m = AccessTools.Method(t, "Update", Type.EmptyTypes);
            if (m == null) { Plugin.Log.LogWarning("vc trace: VehicleCharacter.Update not found"); return; }
            harmony.Patch(m,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(MpVcTracePatch), nameof(Pre))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(MpVcTracePatch), nameof(Post))));
            Plugin.Log.LogInfo("vc trace hooked: VehicleCharacter.Update");
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("vc trace: " + (e.InnerException ?? e).Message); }
    }

    private static void Pre(object __instance) { try { MpSpin.CountUpdateCall(); MpSpin.OnVcUpdatePre(__instance); } catch { } }
    private static void Post(object __instance) { try { MpSpin.OnVcUpdatePost(__instance); } catch { } }
}

/// <summary>
/// Names whatever damages the racer. Being damaged runs
/// HealthComponent.TakeDamage -> the cart is frozen, the model object is
/// deactivated for the hurt duration and the animator plays the damage state -
/// which is exactly "it tumbles and then disappears by itself". Every caller of
/// TakeDamage in this build is hooked so the log says which one fired and what
/// it was: a track TriggerHurt, an item (frog/ofuda), a lag hitbox, the drift
/// bar overheating or a network RPC.
/// </summary>
internal static class MpDamageProbe
{
    internal static readonly string[] Reason = new string[1];

    internal static void TryApply(Harmony harmony)
    {
        int ok = 0;
        ok += Patch(harmony, "HealthComponent", "TakeDamage", new[] { typeof(float) }, nameof(OnDamage));
        ok += Patch(harmony, "HealthComponent", "TakeDamage", Type.EmptyTypes, nameof(OnDamage));
        ok += Patch(harmony, "DriftCapModule", "UpdateModule", null, nameof(OnDriftCap));
        ok += Patch(harmony, "TriggerHurt", "OnTriggerEnter", null, nameof(OnTriggerHurt));
        ok += Patch(harmony, "TriggerHurt", "HandleCollisionFor", null, nameof(OnTriggerHurtFor));
        ok += Patch(harmony, "LagHitbox", "TakeDamage", null, nameof(OnLagHitbox));
        ok += Patch(harmony, "FrogItemBehaviour", "HandlePlayerCollision", null, nameof(OnFrog));
        ok += Patch(harmony, "OfudaBehaviour", "HandlePlayerCollision", null, nameof(OnOfuda));
        Plugin.Log.LogInfo("damage probe: " + ok + " hook(s) installed");
    }

    private static int Patch(Harmony harmony, string type, string method, Type[] args, string prefixName)
    {
        try
        {
            var t = AccessTools.TypeByName(type);
            if (t == null) return 0;
            System.Reflection.MethodBase m = args == null
                ? AccessTools.Method(t, method)
                : AccessTools.Method(t, method, args);
            if (m == null) { Plugin.Log.LogWarning("damage probe: " + type + "." + method + " not found"); return 0; }
            harmony.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(MpDamageProbe), prefixName)));
            return 1;
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("damage probe " + type + "." + method + ": " + (e.InnerException ?? e).Message); return 0; }
    }

    private static void Note(string s) { Reason[0] = s; }

    // --- caller hooks: they only record why the damage is about to happen ---
    private static void OnDriftCap(object __instance)
    {
        try
        {
            float d = Num(__instance, "_drifting");
            float max = Num(__instance, "_driftMax");
            if (max > 0f && d + 0.0001f >= max)
                Note("DriftCapModule: the drift bar filled up (" + d.ToString("0.00") + "/" + max.ToString("0.00") + ")");
        }
        catch { }
    }

    private static void OnTriggerHurt(object __instance, object other)
    {
        try { Note("TriggerHurt trigger '" + NameOf(__instance) + "' hit by '" + NameOf(other) + "'"); } catch { }
    }

    private static void OnTriggerHurtFor(object __instance, object other)
    {
        try { Note("TriggerHurt '" + NameOf(__instance) + "' handling '" + NameOf(other) + "'"); } catch { }
    }

    private static void OnLagHitbox(object __instance, float info)
    {
        try { Note("LagHitbox on '" + NameOf(__instance) + "' (info=" + info.ToString("0.0") + ")"); } catch { }
    }

    private static void OnFrog(object __instance, object item, object other)
    {
        try { Note("frog item on '" + NameOf(__instance) + "' hit '" + NameOf(other) + "'"); } catch { }
    }

    private static void OnOfuda(object __instance)
    {
        try { Note("ofuda item '" + NameOf(__instance) + "'"); } catch { }
    }

    private static bool OnDamage(object __instance)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("HURT: ").Append(Reason[0] ?? "unattributed");
            Reason[0] = null;
            sb.Append("\n   health: inv=").Append(MpDiag.Flag(__instance, "_inv"))
              .Append(" working=").Append(MpDiag.Flag(__instance, "_working"))
              .Append(" toHide='").Append(NameOf(MpDiag.Get(__instance, "_toHide")))
              .Append("' active=").Append(ActiveOf(MpDiag.Get(__instance, "_toHide")));
            var vc = MpDiag.Get(__instance, "_vc");
            if (vc != null)
            {
                sb.Append("\n   vehicle: isDrifting=").Append(MpDiag.Flag(vc, "_isGrounded") != null ? InvokeBool(vc, "isDrifting") : "?")
                  .Append(" driftXDir=").Append(MpDiag.Fnum(vc, "_driftXDir"))
                  .Append(" wishbreak=").Append(MpDiag.Flag(vc, "_wishbreak"))
                  .Append(" speed=").Append(MpDiag.Fnum(vc, "_currentSpeed"))
                  .Append(" turn=").Append(MpDiag.Fnum(vc, "_currentTurn"))
                  .Append(" grounded=").Append(MpDiag.Flag(vc, "_isGrounded"))
                  .Append(" wheeling=").Append(MpDiag.Flag(vc, "_wheeling"))
                  .Append(" onGrass=").Append(InvokeBool(vc, "isOnGrass"))
                  .Append(" grass=").Append(MpDiag.Flag(vc, "_isOnGrass"))
                  .Append(" freeze=").Append(MpDiag.Flag(vc, "_isFreeze"));
                // the drift bar module keeps the number that decides the overheat
                var mods = MpDiag.Get(vc, "_modules");
                var list = mods as System.Collections.IEnumerable;
                if (list != null)
                {
                    foreach (var mo in list)
                    {
                        if (mo == null) continue;
                        string tn = mo.GetType().Name;
                        if (tn.Contains("Drift"))
                            sb.Append("\n   ").Append(tn).Append(": drifting=").Append(MpDiag.Fnum(mo, "_drifting"))
                              .Append("/max=").Append(MpDiag.Fnum(mo, "_driftMax"))
                              .Append(" hurt=").Append(MpDiag.Fnum(mo, "_hurtTime"));
                    }
                }
            }
            if (Blocking)
            {
                sb.Append("\n   -> refused: multiplayer races run without damage (no netcode for it yet)");
                Plugin.Log.LogWarning(sb.ToString());
                return false;                      // skip the original: no freeze, no hidden model
            }
            Plugin.Log.LogWarning(sb.ToString());
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("HURT: probe failed: " + e.Message); }
        return true;
    }

    /// <summary>Damage in our races is off: the game's reaction freezes the kart and
    /// deactivates the whole visible model ("it tumbles and then disappears").</summary>
    private static bool Blocking
    {
        get
        {
            bool inRace = false;
            try { inRace = GameManager.inRace; } catch { }
            return inRace;
        }
    }

    private static string InvokeBool(object o, string prop)
    {
        try
        {
            var m = MpDiag.Member(o, prop);
            if (m is System.Reflection.PropertyInfo p) return p.GetValue(o)?.ToString() ?? "?";
            if (m is System.Reflection.FieldInfo f) return f.GetValue(o)?.ToString() ?? "?";
            return "?";
        }
        catch { return "?"; }
    }

    private static string NameOf(object o)
    {
        try { var c = o as Component; return c == null ? (o == null ? "null" : o.GetType().Name) : c.gameObject.name; }
        catch { return "?"; }
    }

    private static string ActiveOf(object o)
    {
        try { var c = o as Component; return c == null ? "?" : c.gameObject.activeInHierarchy.ToString(); }
        catch { return "?"; }
    }

    private static float Num(object o, string field)
    {
        try
        {
            var m = MpDiag.Member(o, field);
            object v = m is System.Reflection.PropertyInfo p ? p.GetValue(o) : (m is System.Reflection.FieldInfo f ? f.GetValue(o) : null);
            return v == null ? 0f : System.Convert.ToSingle(v);
        }
        catch { return 0f; }
    }

    // manual trigger for the container: FumoMP.hurt makes the local racer take damage once
    private static bool _hurtDone;
    internal static void MaybeTestHurt()
    {
        if (_hurtDone) return;
        try
        {
            if (!System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.hurt"))) return;
            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0 || racers[0] == null) return;
            var vc = racers[0].vc;
            if (vc == null) return;
            // wait until the countdown freeze is over and the kart is actually moving
            if (MpDiag.Flag(vc, "_isFreeze") == "True") return;
            if (MpDiag.Fnum(vc, "_currentSpeed") == "0.00") return;
            HealthComponent hc = null;
            var all = Resources.FindObjectsOfTypeAll<HealthComponent>();
            if (all != null)
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    try { if (all[i]._vc != null && all[i]._vc.Pointer == (vc as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.Pointer) { hc = all[i]; break; } }
                    catch { if (hc == null) hc = all[i]; }
                }
            if (hc == null) return;
            _hurtDone = true;
            Note("manual test trigger (FumoMP.hurt)");
            var m = AccessTools.Method(hc.GetType(), "TakeDamage", new[] { typeof(float) });
            if (m != null) { m.Invoke(hc, new object[] { 4f }); Plugin.Log.LogWarning("hurt test: TakeDamage(4) fired on the local racer"); }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("hurt test: " + e.Message); }
    }
}

/// <summary>Logs every freeze/unfreeze/fall of the racer: a frozen vehicle stops
/// updating entirely, which looks like "the model died".</summary>
internal static class MpFreezeProbe
{
    internal static void TryApply(Harmony harmony)
    {
        int ok = 0;
        ok += P(harmony, "VehicleCharacter", "Freeze", Type.EmptyTypes, nameof(OnFreeze));
        ok += P(harmony, "VehicleCharacter", "UnFreeze", Type.EmptyTypes, nameof(OnUnFreeze));
        ok += P(harmony, "PlayerRacer", "Fall", Type.EmptyTypes, nameof(OnFall));
        ok += P(harmony, "PlayerRacer", "TeleportBack", Type.EmptyTypes, nameof(OnFall));
        // Freeze() ends with "if airborne: transform.rotation = LookRotation(velocity)",
        // which snaps the whole kart onto its velocity vector - a tumble the player
        // sees and that nothing undoes. Capture the orientation on the way in and
        // restore it on the way out while a race is running. TeleportBack is allowed
        // to rotate freely: that is a deliberate respawn.
        ok += Ppair(harmony, "VehicleCharacter", "Freeze", Type.EmptyTypes, nameof(PreFreeze), nameof(PostFreeze));
        Plugin.Log.LogInfo("freeze probe: " + ok + " hook(s) installed");
    }

    private static readonly Dictionary<IntPtr, Quaternion> PreFreezeRot = new Dictionary<IntPtr, Quaternion>();

    private static int Ppair(Harmony harmony, string type, string method, Type[] args, string prefix, string postfix)
    {
        try
        {
            var t = AccessTools.TypeByName(type);
            if (t == null) return 0;
            var m = args == null ? AccessTools.Method(t, method) : AccessTools.Method(t, method, args);
            if (m == null) return 0;
            harmony.Patch(m,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(MpFreezeProbe), prefix)),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(MpFreezeProbe), postfix)));
            return 1;
        }
        catch { return 0; }
    }

    private static void PreFreeze(object __instance)
    {
        try
        {
            var c = __instance as Component;
            if (c == null) return;
            PreFreezeRot[c.Pointer] = c.transform.rotation;
        }
        catch { }
    }

    private static void PostFreeze(object __instance)
    {
        try
        {
            var c = __instance as Component;
            if (c == null) return;
            Quaternion pre;
            if (!PreFreezeRot.TryGetValue(c.Pointer, out pre)) return;
            PreFreezeRot.Remove(c.Pointer);
            bool racing = false;
            try { racing = GameManager.inRace; } catch { }
            if (!racing) return;
            if (Quaternion.Angle(pre, c.transform.rotation) < 2f) return;
            Plugin.Log.LogWarning("FREEZE: keeping the kart's orientation (the game wanted to snap it "
                                  + Quaternion.Angle(pre, c.transform.rotation).ToString("F0") + " deg)");
            c.transform.rotation = pre;
        }
        catch { }
    }

    private static int P(Harmony harmony, string type, string method, Type[] args, string prefix)
    {
        try
        {
            var t = AccessTools.TypeByName(type);
            if (t == null) return 0;
            var m = args == null ? AccessTools.Method(t, method) : AccessTools.Method(t, method, args);
            if (m == null) return 0;
            harmony.Patch(m, prefix: new HarmonyMethod(AccessTools.Method(typeof(MpFreezeProbe), prefix)));
            return 1;
        }
        catch { return 0; }
    }

    private static void OnFreeze(object __instance)
    {
        try { Plugin.Log.LogWarning("FREEZE: VehicleCharacter.Freeze on '" + Nm(__instance) + "'  " + St()); } catch { }
    }

    private static void OnUnFreeze(object __instance)
    {
        try { Plugin.Log.LogInfo("FREEZE: UnFreeze on '" + Nm(__instance) + "'  " + St()); } catch { }
    }

    private static void OnFall(object __instance)
    {
        try { Plugin.Log.LogWarning("FALL: " + __instance.GetType().Name + " on '" + Nm(__instance) + "'  " + St()); } catch { }
    }

    private static string Nm(object o)
    {
        try { var c = o as Component; return c == null ? "?" : c.gameObject.name; } catch { return "?"; }
    }

    private static string St()
    {
        try
        {
            var r = GameManager.racers;
            if (r == null || r.Length == 0 || r[0] == null) return "";
            var vc = r[0].vc;
            if (vc == null) return "";
            var m = MpDiag.Member(vc, "_body");
            return "speed=" + MpDiag.Fnum(vc, "_currentSpeed")
                 + " freeze=" + MpDiag.Flag(vc, "_isFreeze")
                 + " grounded=" + MpDiag.Flag(vc, "_isGrounded")
                 + " driftXDir=" + MpDiag.Fnum(vc, "_driftXDir")
                 + " turn=" + MpDiag.Fnum(vc, "_currentTurn");
        }
        catch { return ""; }
    }
}
