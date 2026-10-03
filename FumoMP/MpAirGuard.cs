using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace FumoMP
{
    /// <summary>
    /// Two problems that only show up once a race actually runs:
    ///
    /// 1. The airborne branch of VehicleCharacter.Update spins the racer root:
    ///    Transform.Rotate(0, |_wishTurn| * _turnForce * _turnAirMult * dt * ramp, 0)
    ///    with turnForce=125 and turnAirMult=3.75 that is 469 deg/s, and it is
    ///    applied to the whole visible model. In a normal race you are in the air
    ///    for a moment (a ramp), so you see a trick; when the racer stays airborne
    ///    - long jumps, falling off the mountains track, the frame after a
    ///    respawn - the model keeps turning at 469 deg/s and never comes back to
    ///    its heading, which is the "it spins and never recovers" the player sees.
    ///    Measured from a real log: turnedOnAir grew 47 deg in 0.10 s and the yaw
    ///    went 235 -> 282 in the same frame, i.e. exactly _turnForce * _turnAirMult.
    ///
    ///    Fix: while a race is running and the racer is off the ground, undo the
    ///    yaw change the frame applied and keep pitch/roll (so the kart still
    ///    follows the air, it just does not pirouette). Create FumoMP.trick in the
    ///    plugins folder to keep the original tricks.
    ///
    /// 2. When the racer ends up under the world (fell through the track, was
    ///    respawned into geometry, got pushed off a cliff) nothing brings it back
    ///    on its own: air time keeps growing and the model is simply gone. Fix: a
    ///    watchdog that, after 4 s of air time or 60 m below the world origin,
    ///    calls the game's own PlayerRacer.Fall() (freeze -> TeleportBack to the
    ///    last checkpoint), the same path a fall trigger uses.
    /// </summary>
    internal static class MpAirGuard
    {
        private static readonly Dictionary<IntPtr, Quaternion> PreRot = new Dictionary<IntPtr, Quaternion>();
        private static readonly Dictionary<IntPtr, float> Suppressed = new Dictionary<IntPtr, float>();
        private static readonly Dictionary<IntPtr, float> AirSince = new Dictionary<IntPtr, float>();
        private static readonly Dictionary<IntPtr, float> LastRecover = new Dictionary<IntPtr, float>();

        private static bool _loggedSuppress;
        private static bool _loggedTrickOptIn;

        internal static bool TricksAllowed
        {
            get
            {
                try { return System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.trick")); }
                catch { return false; }
            }
        }

        internal static void TryApply(Harmony harmony)
        {
            try
            {
                var t = AccessTools.TypeByName("VehicleCharacter");
                if (t == null) { Plugin.Log.LogWarning("air guard: VehicleCharacter not found"); return; }
                var m = AccessTools.Method(t, "Update", Type.EmptyTypes);
                if (m == null) { Plugin.Log.LogWarning("air guard: VehicleCharacter.Update not found"); return; }
                harmony.Patch(m,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(MpAirGuard), nameof(Pre))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(MpAirGuard), nameof(Post))));
                Plugin.Log.LogInfo("air guard hooked: VehicleCharacter.Update");
            }
            catch (Exception e) { Plugin.Log.LogWarning("air guard: " + (e.InnerException ?? e).Message); }
        }

        private static void Pre(object __instance)
        {
            try
            {
                if (!InRace) return;
                var vc = __instance as Component;
                if (vc == null) return;
                PreRot[vc.Pointer] = vc.transform.rotation;
            }
            catch { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Post(object __instance)
        {
            try
            {
                if (!InRace) return;
                var vc = __instance as Component;
                if (vc == null) return;
                var id = vc.Pointer;

                Quaternion pre;
                if (!PreRot.TryGetValue(id, out pre)) return;

                bool grounded = MpDiag.Bool(__instance, "_isGrounded");
                float air = MpDiag.Num(__instance, "_airboneTime");
                if (grounded || air <= 0.02f)
                {
                    // landed - report what this flight cost, once per landing
                    float acc;
                    if (Suppressed.TryGetValue(id, out acc) && acc > 20f)
                        Plugin.Log.LogWarning("AIR: landing after " + acc.ToString("F0") + " deg of airborne spin suppressed");
                    Suppressed[id] = 0f;
                    AirSince.Remove(id);
                    return;
                }

                if (TricksAllowed)
                {
                    if (!_loggedTrickOptIn) { _loggedTrickOptIn = true; Plugin.Log.LogInfo("air guard: FumoMP.trick present - original air tricks kept"); }
                    return;
                }

                var tr = vc.transform;
                Quaternion cur = tr.rotation;

                // Everything the airborne branch does to the *orientation* is undone,
                // not just the yaw: it also applies a velocity based rotation and, via
                // Freeze, a LookRotation(velocity) snap, and any of those left the kart
                // flying (or landing) tilted. Keeping pitch/roll "because the kart
                // should follow the air" is what left a crooked kart on screen.
                float d = Quaternion.Angle(pre, cur);
                if (d < 0.5f) return;                        // ordinary frame, leave it alone
                tr.rotation = pre;

                float sum;
                Suppressed.TryGetValue(id, out sum);
                Suppressed[id] = sum + d;

                if (!_loggedSuppress)
                {
                    _loggedSuppress = true;
                    Plugin.Log.LogInfo("air guard: airborne rotation is cancelled ("
                        + d.ToString("F1") + " deg this frame; the trick rate would be "
                        + MpDiag.Num(__instance, "_turnForce").ToString("F0") + " x "
                        + MpDiag.Num(__instance, "_turnAirMult").ToString("F2") + " deg/s)"
                        + " - create FumoMP.trick to allow tricks again");
                }
            }
            catch { }
        }

        private static float YawDelta(Quaternion a, Quaternion b)
        {
            Vector3 fa = a * Vector3.forward; fa.y = 0f;
            Vector3 fb = b * Vector3.forward; fb.y = 0f;
            if (fa.sqrMagnitude < 1e-6f || fb.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(fa.normalized, fb.normalized, Vector3.up);
        }

        /// <summary>
        /// Brings a racer that stays airborne (fell through the world, respawned
        /// into geometry) back to the track with the game's own respawn path.
        /// </summary>
        internal static void Tick(float dt)
        {
            try
            {
                if (!InRace) { AirSince.Clear(); return; }
                var racers = GameManager.racers;
                if (racers == null) return;
                float now = Time.realtimeSinceStartup;

                for (int i = 0; i < racers.Length; i++)
                {
                    var r = racers[i];
                    if (r == null) continue;
                    var vc = r.vc;
                    if (vc == null) continue;

                    var id = vc.Pointer;
                    bool grounded = MpDiag.Bool(vc, "_isGrounded");
                    float air = MpDiag.Num(vc, "_airboneTime");
                    float y = vc.transform.position.y;

                    // the game's own air clock resets on landing, so it is the
                    // honest measure of "this racer has been off the ground"
                    if (grounded || air <= 0.05f) { AirSince.Remove(id); LevelOut(vc, id, now); continue; }
                    LevelOut(vc, id, now);          // the flag says airborne: make sure it really is

                    bool lost = air > 1.5f && y < -60f;      // under the world
                    bool tooLong = air > 4f;                 // never came down
                    if (!lost && !tooLong) continue;
                    if (r._endedRace) continue;

                    float last;
                    if (LastRecover.TryGetValue(id, out last) && now - last < 8f) continue;
                    LastRecover[id] = now;

                    Plugin.Log.LogWarning("AIR: '" + vc.gameObject.name + "' airborne "
                        + air.ToString("F1") + "s (y=" + y.ToString("F1") + (lost ? ", below the world" : "")
                        + ") - calling the game's own respawn (freeze + teleport back)");
                    try { r.Fall(); }
                    catch (Exception e) { Plugin.Log.LogWarning("AIR: Fall() failed: " + (e.InnerException ?? e).Message); }
                }
            }
            catch { }
        }

        /// <summary>
        /// Last line of defence, and the fix for "it stays crooked and never comes
        /// back": a kart that is physically standing on the track but whose
        /// *_isGrounded* flag says otherwise gets no ground alignment from the game at
        /// all (that alignment lives in the grounded branch of Update), so whatever
        /// tilt it picked up on the way there simply stays. The test is physical, not
        /// the game's flag: a downward ray must find the ground within ~2.5 m.
        /// The kart is then eased onto its wheels using the surface normal.
        /// </summary>
        private static void LevelOut(Component vc, IntPtr id, float now)
        {
            try
            {
                var tr = vc.transform;
                RaycastHit hit = default(RaycastHit);
                Vector3 origin = tr.position + Vector3.up * 1.5f;
                bool found = false;
                try { found = Physics.Raycast(origin, Vector3.down, out hit, 4f); } catch { found = false; }

                // Another player's kart is solid now (it has a collider so karts can
                // bump), and it must never be mistaken for the ground: driving over a
                // ghost would otherwise "level" the kart onto it.
                if (found && IsGhostCollider(hit.collider)) found = false;

                // no ground under it: genuinely flying, leave it alone
                if (!found || hit.distance > 2.6f) { TiltSince.Remove(id); return; }

                float tilt = Vector3.Angle(tr.up, hit.normal);
                if (tilt < 22f) { TiltSince.Remove(id); return; }

                float since;
                if (!TiltSince.TryGetValue(id, out since) || since <= 0f) { TiltSince[id] = now; return; }
                if (now - since < 0.5f) return;

                var flat = Vector3.ProjectOnPlane(tr.forward, hit.normal);
                if (flat.sqrMagnitude < 0.0001f) flat = Vector3.ProjectOnPlane(tr.right, hit.normal);
                if (flat.sqrMagnitude < 0.0001f) { TiltSince.Remove(id); return; }

                var want = Quaternion.LookRotation(flat.normalized, hit.normal);
                tr.rotation = Quaternion.Slerp(tr.rotation, want, 0.15f);
                TiltSince[id] = now - 0.5f;               // keep working while it is crooked

                if (!_levelLogged.Contains(id))
                {
                    _levelLogged.Add(id);
                    Plugin.Log.LogWarning("AIR: kart sat " + tilt.ToString("F0")
                                          + " deg off the ground under it (game says airborne="
                                          + MpDiag.Bool(vc, "_isGrounded") + ") - levelling it back out");
                }
            }
            catch { }
        }

        private static readonly Dictionary<IntPtr, float> TiltSince = new Dictionary<IntPtr, float>();
        private static readonly List<IntPtr> _levelLogged = new List<IntPtr>();

        /// <summary>Is this collider part of one of our ghost karts?</summary>
        private static bool IsGhostCollider(Collider c)
        {
            try
            {
                var t = c != null ? c.transform : null;
                for (int i = 0; i < 6 && t != null; i++)
                {
                    string n = t.name;
                    if (!string.IsNullOrEmpty(n) && n.StartsWith("FumoMP_Ghost")) return true;
                    t = t.parent;
                }
            }
            catch { }
            return false;
        }

        private static bool InRace
        {
            get { try { return GameManager.inRace; } catch { return false; } }
        }
    }
}
