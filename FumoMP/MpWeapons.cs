using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace FumoMP
{
    /// <summary>
    /// Weapon hits over the network.
    ///
    /// In this build an attack is a physics object that the game simulates on the
    /// machine that fired it - an ofuda (OfudaBehaviour, a Proyectile), a homing
    /// projectile, or the knockback item. Its hit test is collider driven and ends in
    /// TriggerHurt.HandleCollisionFor(Collider), which asks the object it collided
    /// with for a component (a GetComponent on the hit collider) and then calls
    /// HealthComponent.TakeDamage(...) on it. A ghost has none of that, so on this
    /// machine a hit against a ghost was never detected and never applied, and the
    /// other player is on another machine anyway: their kart can only be moved by
    /// their own game.
    ///
    /// So the hit is decided where the weapon actually is, and the *effect* is applied
    /// where the victim actually is:
    ///
    ///   attacker: the local projectile gets close to a ghost (or dies on its
    ///             collider) -> the interesting values are read off the game's own
    ///             components (TriggerHurt._damageDuration), the projectile is
    ///             destroyed with its own explosion effect, and a weapon packet is
    ///             sent to the victim;
    ///   victim:   the packet runs the game's own reaction on its own kart -
    ///             HealthComponent.TakeDamage(duration), which is exactly what the
    ///             game's TriggerHurt would have called: freeze the vehicle, hide the
    ///             character for that duration and restore it automatically.
    ///
    /// The attacker's own screen mirrors the result by hiding the ghost's character
    /// node for the same duration (that is what the victim's game just did locally).
    ///
    /// Damage is otherwise refused in multiplayer (MpDamageProbe), so a network hit
    /// has to be allowed through explicitly - see MpDamageProbe.NetworkHit.
    /// </summary>
    internal static class MpWeapons
    {
        private sealed class Shot
        {
            internal Proyectile P;
            internal GameObject Owner;
            internal float Born;
            internal bool Ours;
            internal float NextPanicLog;
        }

        private static readonly List<Shot> _shots = new List<Shot>();
        private static readonly Dictionary<int, float> _scored = new Dictionary<int, float>();   // instance -> hit time
        private static readonly float[] _knockAt = new float[MpNet.MaxPlayers];
        private static float _nextScan, _nextKnock, _nextLog;
        private static int _sent, _taken;

        private const float HitGap = 1.6f;         // metres from the body collider
        private const float KnockGap = 1.8f;

        private static bool Disabled
        {
            get
            {
                try { return System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.noweapons")); }
                catch { return false; }
            }
        }

        // ------------------------------------------------------------------ hooks
        internal static void TryApply(Harmony harmony)
        {
            try
            {
                var init = AccessTools.Method(typeof(Proyectile), "Init",
                                              new[] { typeof(GameObject), typeof(Vector3), typeof(bool) });
                if (init != null)
                {
                    harmony.Patch(init, postfix: new HarmonyMethod(AccessTools.Method(typeof(MpWeapons), nameof(OnInit))));
                    Plugin.Log.LogInfo("weapons: hooked Proyectile.Init (every thrown weapon spawns here)");
                }
                else Plugin.Log.LogWarning("weapons: Proyectile.Init not found - only the per-frame scan will find shots");

                var die = AccessTools.Method(typeof(Proyectile), "DestroyNow");
                if (die != null)
                {
                    harmony.Patch(die, prefix: new HarmonyMethod(AccessTools.Method(typeof(MpWeapons), nameof(OnDestroyNow))));
                    Plugin.Log.LogInfo("weapons: hooked Proyectile.DestroyNow (a shot that dies on a ghost still counts)");
                }
                else Plugin.Log.LogWarning("weapons: Proyectile.DestroyNow not found");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("weapons: hook failed: " + (e.InnerException ?? e).Message);
            }
        }

        private static void OnInit(Proyectile __instance)
        {
            try
            {
                if (__instance == null || Disabled) return;
                Register(__instance, true);
            }
            catch (Exception e) { Plugin.Log.LogWarning("weapons: init: " + e.Message); }
        }

        private static void OnDestroyNow(Proyectile __instance)
        {
            try
            {
                if (__instance == null || Disabled) return;
                // the game destroys a shot when it hits something (or when its lifetime
                // runs out) - check the contact once more before the object is gone
                Check(__instance, true);
            }
            catch { }
        }

        // ------------------------------------------------------------------ shots
        private static void Register(Proyectile p, bool spawned)
        {
            if (Scored(p)) return;
            for (int i = 0; i < _shots.Count; i++) if (_shots[i].P == p) return;
            var s = new Shot { P = p, Born = Time.realtimeSinceStartup, Owner = OwnerOf(p) };
            s.Ours = IsOurs(s.Owner);
            _shots.Add(s);
            if (spawned && s.Ours && Time.realtimeSinceStartup - _nextLog > 0.5f)
            {
                _nextLog = Time.realtimeSinceStartup;
                Plugin.Log.LogInfo("weapon: we fired a " + NameOf(p) + " (owner '"
                                   + (s.Owner != null ? s.Owner.name : "?") + "', freeze "
                                   + DamageDuration(p).ToString("0.0") + "s)");
            }
        }

        /// <summary>True when this shot already hit somebody (don't score it twice).</summary>
        private static bool Scored(Proyectile p)
        {
            try
            {
                int id = p.Pointer.ToInt32();
                float when;
                if (_scored.TryGetValue(id, out when) && Time.realtimeSinceStartup - when < 12f) return true;
            }
            catch { }
            return false;
        }

        private static void MarkScored(Proyectile p)
        {
            try
            {
                if (_scored.Count > 64) _scored.Clear();
                _scored[p.Pointer.ToInt32()] = Time.realtimeSinceStartup;
            }
            catch { }
        }

        private static GameObject OwnerOf(Proyectile p)
        {
            try { return MpDiag.Get(p, "_owner") as GameObject; } catch { return null; }
        }

        /// <summary>
        /// Only our own shots may damage a remote player. Everything in the scene was
        /// fired locally (each machine simulates its own player), so an unknown owner
        /// still counts as ours when there is just one racer on this machine - but it
        /// is logged, so the next test run says whether the owner really resolves.
        /// </summary>
        private static bool IsOurs(GameObject owner)
        {
            try
            {
                if (owner == null)
                {
                    var racers = GameManager.racers;
                    return racers == null || racers.Length <= 1;
                }
                var local = MpDiag.LocalRacer();
                if (local == null) return false;
                if (owner == local.gameObject) return true;
                var vc = local.vc;
                if (vc != null && owner == (vc as Component).gameObject) return true;
                if (owner.transform != null && local.transform != null &&
                    owner.transform.root == local.transform.root) return true;
            }
            catch { }
            return false;
        }

        private static string NameOf(Proyectile p)
        {
            try { return p != null ? p.GetType().Name : "?"; } catch { return "?"; }
        }

        /// <summary>How long the victim is out of control: the game's own number.</summary>
        private static float DamageDuration(Proyectile p)
        {
            float d = 0f;
            try
            {
                var hurt = MpDiag.Get(p, "_hurt");
                if (hurt != null && MpDiag.Flag(hurt, "_useDMGDuration") == "True")
                    d = MpDiag.Num(hurt, "_damageDuration");
                if (d <= 0f) d = MpDiag.Num(p, "_damagedDuration");        // OfudaBehaviour
            }
            catch { }
            if (d <= 0f) d = 1f;
            return Mathf.Clamp(d, 0.3f, 5f);
        }

        // ------------------------------------------------------------------- tick
        internal static void Tick()
        {
            try
            {
                bool inRace = false;
                try { inRace = GameManager.inRace; } catch { }
                if (!inRace || Disabled) { if (_shots.Count > 0) _shots.Clear(); return; }

                float now = Time.realtimeSinceStartup;
                if (now >= _nextScan) { _nextScan = now + 0.25f; Scan(); }

                for (int i = _shots.Count - 1; i >= 0; i--)
                {
                    var s = _shots[i];
                    if (s.P == null) { _shots.RemoveAt(i); continue; }
                    if (now - s.Born > 15f) { _shots.RemoveAt(i); continue; }
                    if (now - s.Born < 0.08f) continue;              // not on its spawn frame
                    Check(s.P, false);
                }

                KnockTick(now);
            }
            catch (Exception e) { Plugin.Log.LogWarning("weapons tick: " + e.Message); }
        }

        /// <summary>
        /// Safety net: a shot that we never saw spawn (an overridden Init that does not
        /// call the base one) is picked up here, and dead entries are dropped.
        /// </summary>
        private static void Scan()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Proyectile>();
                if (all == null) return;
                foreach (var p in all)
                {
                    if (p == null) continue;
                    if (p.gameObject == null || !p.gameObject.activeInHierarchy) continue;
                    Register(p, false);
                }
            }
            catch { }
        }

        /// <summary>Did this shot reach a ghost? Returns true when it counted as a hit.</summary>
        private static bool Check(Proyectile p, bool dying)
        {
            try
            {
                Shot s = null;
                for (int i = 0; i < _shots.Count; i++) if (_shots[i].P == p) { s = _shots[i]; break; }
                if (s != null && !s.Ours) return false;
                if (s == null) s = new Shot { P = p, Born = Time.realtimeSinceStartup, Owner = OwnerOf(p) };
                if (!s.Ours) return false;

                Vector3 at = p.transform.position;
                int slot = MpGhost.NearestTo(at, out float gap, out Vector3 gpos);
                if (slot < 0 || gap > HitGap) return false;

                float freeze = DamageDuration(p);
                Vector3 dir = gpos - at; dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                dir = dir.normalized;

                MarkScored(p);
                MpNet.SendWeaponHit(slot, dir, freeze, 0f);
                MpGhost.Flash(slot, freeze);
                Boom(p, at);
                _shots.Remove(s);
                _sent++;

                float now = Time.realtimeSinceStartup;
                if (now - _nextLog > 0.4f)
                {
                    _nextLog = now;
                    Plugin.Log.LogInfo("weapon: " + NameOf(p) + " hit slot " + slot + " (gap "
                                       + gap.ToString("0.00") + "m" + (dying ? ", on impact" : "")
                                       + ", freeze " + freeze.ToString("0.0")
                                       + "s) - told their machine"
                                       + (s.Owner != null ? "" : " (owner unknown)"));
                }
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("weapon check: " + e.Message); return false; }
        }

        /// <summary>Lose the projectile the way the game does, with its own effect.</summary>
        private static void Boom(Proyectile p, Vector3 at)
        {
            try
            {
                var fx = MpDiag.Get(p, "_explosion") as GameObject;
                if (fx != null) UnityEngine.Object.Instantiate(fx, at, Quaternion.identity);
            }
            catch { }
            try { p.DestroyNow(); } catch { }
        }

        // ------------------------------------------------------- knockback item
        /// <summary>
        /// The knockback item is a physical thing that pushes karts it runs into. On
        /// this machine it can only push a ghost, which is driven by packets - so the
        /// push is forwarded to the player who owns that kart.
        /// </summary>
        private static void KnockTick(float now)
        {
            if (now < _nextKnock) return;
            _nextKnock = now + 0.1f;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<ItemKnockback>();
                if (all == null) return;
                var local = MpDiag.LocalRacer();
                Transform localRoot = local != null ? local.transform.root : null;
                foreach (var k in all)
                {
                    if (k == null || k.gameObject == null || !k.gameObject.activeInHierarchy) continue;
                    // still held (a child of our own kart) is not a hit
                    if (localRoot != null && k.transform.root == localRoot) continue;
                    Vector3 at = k.transform.position;
                    int slot = MpGhost.NearestTo(at, out float gap, out Vector3 gpos);
                    if (slot < 0 || gap > KnockGap) continue;
                    if (slot >= _knockAt.Length) continue;
                    if (now - _knockAt[slot] < 0.6f) continue;
                    _knockAt[slot] = now;

                    Vector3 vel = Vec(k, "_velocity");
                    float strength = Mathf.Clamp(vel.magnitude * 0.5f, 6f, 25f);
                    Vector3 dir = gpos - at; dir.y = 0f;
                    if (dir.sqrMagnitude < 0.0001f) dir = vel;
                    if (dir.sqrMagnitude < 0.0001f) continue;
                    MpNet.SendHit(slot, dir.normalized, strength);
                    Plugin.Log.LogInfo("weapon: knockback item hit slot " + slot + " (speed "
                                       + vel.magnitude.ToString("0.0") + " m/s, shove "
                                       + strength.ToString("0.0") + ") - told their machine");
                }
            }
            catch { }
        }

        private static Vector3 Vec(object o, string field)
        {
            try
            {
                var v = MpDiag.Get(o, field);
                return v is Vector3 vv ? vv : Vector3.zero;
            }
            catch { return Vector3.zero; }
        }

        // -------------------------------------------------------- incoming hits
        /// <summary>
        /// Somebody's weapon reached us: run the game's own hit reaction on our own
        /// kart. TakeDamage is the call the game's TriggerHurt makes, so the result is
        /// the normal one - frozen vehicle, character hidden for the duration, then
        /// restored - and it is allowed through the multiplayer damage block.
        /// </summary>
        internal static void OnWeaponHitReceived(int fromSlot, Vector3 dir, float freeze, float shove)
        {
            try
            {
                var local = MpDiag.LocalRacer();
                var vc = local != null ? local.vc : null;
                if (vc == null) return;
                _taken++;

                string result = "no health component";
                var hc = Health(vc);
                if (hc != null && freeze > 0f)
                {
                    bool applied = false;
                    MpDamageProbe.NetworkHit = true;
                    try { applied = hc.TakeDamage(freeze); }
                    finally { MpDamageProbe.NetworkHit = false; }
                    result = applied ? ("damage applied, out of control " + freeze.ToString("0.0") + "s")
                                     : "refused by the game (invulnerable or an item blocked it)";
                }
                if (shove > 0f) { try { vc.AddForce(dir * shove, 60f); } catch { } }

                Plugin.Log.LogWarning("weapon: hit by slot " + fromSlot + "'s weapon - " + result);
            }
            catch (Exception e) { Plugin.Log.LogWarning("weapon receive: " + e.Message); }
        }

        private static HealthComponent Health(object vc)
        {
            try
            {
                var c = vc as Component;
                if (c == null) return null;
                var hc = c.GetComponent<HealthComponent>();
                if (hc != null) return hc;
            }
            catch { }
            try
            {
                var all = Resources.FindObjectsOfTypeAll<HealthComponent>();
                if (all == null) return null;
                foreach (var h in all)
                {
                    if (h == null) continue;
                    var own = MpDiag.Get(h, "_vc");
                    if (ReferenceEquals(own, vc) || (own as Component) == (vc as Component)) return h;
                }
            }
            catch { }
            return null;
        }

        /// <summary>One line for the periodic status log.</summary>
        internal static string Status()
        {
            return "weapons: shots=" + _shots.Count + " sent=" + _sent + " taken=" + _taken;
        }
    }
}
