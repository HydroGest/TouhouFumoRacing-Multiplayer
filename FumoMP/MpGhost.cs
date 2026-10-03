using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FumoMP
{
    /// <summary>
    /// The other players' karts.
    ///
    /// The game's own netcode never moves a racer: FRNetworkRacer syncs
    /// lap/trigger state and damage RPCs, the race prefabs have no NetworkIdentity
    /// and no NetworkTransform. Its intended design was "every machine simulates
    /// every racer", which needs each extra participant to be a real local player -
    /// and the game then gives each of them a camera, a HUD and an input slot. That
    /// is splitscreen, and on this build the start sequence hangs outright when a
    /// participant has no input device (measured: 3 fake humans -> no kart ever
    /// moved and every managed Update stopped right after the race start).
    ///
    /// So remote players are represented visually: one ghost kart per remote
    /// player, cloned from this machine's own kart (same meshes, animator,
    /// materials) with every piece of game logic removed. A ghost cannot collide,
    /// cannot take damage and cannot touch race state - it is a transform that the
    /// network layer drives.
    ///
    /// Ghost index == the network slot (host 0, clients 1..7), which keeps the
    /// mapping stable while players come and go. A ghost is created hidden and only
    /// becomes visible on its first state packet, so nobody sees a kart parked at a
    /// wrong spot while a player is still loading.
    /// </summary>
    internal static class MpGhost
    {
        private class Ghost
        {
            internal GameObject Go;
            internal bool Visible;
            internal Vector3 P0, P1;
            internal Quaternion R0, R1;
            internal float T0, T1;
            internal float Speed, Steer;
            internal bool Grounded;
            internal float LastApply;
            internal int Character = -1, Skin, Vehicle;
            internal bool ModelBuilt;
            internal GameObject Nameplate;
            internal string Nick = "";
        }

        private static readonly Dictionary<int, Ghost> Map = new Dictionary<int, Ghost>();

        internal static int Count { get { return Map.Count; } }

        /// <summary>Harness-only: orbit N test ghosts around the local kart.</summary>
        internal static int TestCount
        {
            get
            {
                try
                {
                    string f = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.ghosts");
                    if (!System.IO.File.Exists(f)) return 0;
                    int n = 0;
                    int.TryParse(System.IO.File.ReadAllText(f).Trim(), out n);
                    return n < 0 ? 0 : (n > 6 ? 6 : n);
                }
                catch { return 0; }
            }
        }

        // ----------------------------------------------------------------- spawn
        /// <summary>Create the ghost for a slot if it does not exist yet (hidden).</summary>
        internal static GameObject Ensure(int slot)
        {
            Ghost g;
            if (Map.TryGetValue(slot, out g) && g.Go != null) return g.Go;

            try
            {
                var local = MpDiag.LocalRacer();
                if (local == null) return null;                 // no kart to clone yet
                var lp = local.transform.position;

                var go = UnityEngine.Object.Instantiate(local.gameObject, lp, Quaternion.identity);
                if (go == null) return null;
                go.name = "FumoMP_Ghost" + slot;

                int stripped = Strip(go);
                go.transform.position = lp;
                go.SetActive(true);

                g = new Ghost { Go = go, Visible = false };
                Map[slot] = g;
                SetRenderers(go, false);                        // invisible until real data arrives

                BuildNameplate(g, slot);

                int renderers = 0;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r != null) renderers++;
                Plugin.Log.LogInfo("ghost: slot " + slot + " created (renderers=" + renderers
                                   + ", components removed=" + stripped + "), waiting for its first packet");
                return go;
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                Plugin.Log.LogWarning("ghost: spawn failed for slot " + slot + ": " + inner.Message + "\n" + (inner.StackTrace ?? ""));
                return null;
            }
        }

        /// <summary>
        /// Take the game logic off the clone: vehicle physics, racer/lap state,
        /// input controller, health, items. What is left is the visible kart.
        /// </summary>
        private static int Strip(GameObject go)
        {
            int removed = 0;
            try
            {
                removed += Kill(go, "VehicleCharacter");
                removed += Kill(go, "PlayerRacer");
                removed += Kill(go, "PlayerRacingController");
                removed += Kill(go, "HealthComponent");
                removed += Kill(go, "PlayerItems");
                try
                {
                    foreach (var c in go.GetComponentsInChildren<Collider>(true))
                        if (c != null) { c.enabled = false; removed++; }
                }
                catch { }
                try
                {
                    foreach (var cam in go.GetComponentsInChildren<Camera>(true))
                        if (cam != null) { cam.enabled = false; removed++; }
                }
                catch { }
            }
            catch (Exception e) { Plugin.Log.LogWarning("ghost: strip: " + e.Message); }
            return removed;
        }

        private static int Kill(GameObject go, string typeName)
        {
            int n = 0;
            try
            {
                var t = HarmonyLib.AccessTools.TypeByName(typeName);
                if (t == null) return 0;
                var it = Il2CppInterop.Runtime.Il2CppType.From(t);
                if (it == null) return 0;
                foreach (var c in go.GetComponentsInChildren(it, true))
                {
                    if (c == null) continue;
                    UnityEngine.Object.Destroy(c);
                    n++;
                }
            }
            catch { }
            return n;
        }

        private static void SetRenderers(GameObject go, bool on)
        {
            try
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (r != null && r.enabled != on) r.enabled = on;
            }
            catch { }
        }

        /// <summary>
        /// The remote player's identity (character / skin / vehicle indices, as sent
        /// in the hello and info messages). Triggers the model swap so the ghost
        /// wears *their* fumo instead of a copy of yours.
        /// </summary>
        internal static void SetIdentity(int slot, int character, int skin, int vehicle)
        {
            try
            {
                var g = Get(slot);
                if (g == null) return;
                if (g.ModelBuilt && g.Character == character && g.Skin == skin && g.Vehicle == vehicle) return;
                g.Character = character; g.Skin = skin; g.Vehicle = vehicle;
                RebuildModel(g, slot);
            }
            catch (Exception e) { Plugin.Log.LogWarning("ghost identity: " + e.Message); }
        }

        /// <summary>The remote player's nickname, shown above their kart.</summary>
        internal static void SetNick(int slot, string nick)
        {
            try
            {
                var g = Get(slot);
                if (g == null || string.IsNullOrEmpty(nick) || g.Nick == nick) return;
                g.Nick = nick;
                if (g.Nameplate != null)
                {
                    var tmp = g.Nameplate.GetComponent<TMPro.TextMeshPro>();
                    if (tmp != null) tmp.text = nick;
                    Plugin.Log.LogInfo("ghost: slot " + slot + " nameplate '" + nick + "'");
                }
                if (g.ModelBuilt) Plugin.Log.LogInfo("ghost: slot " + slot + " is '" + nick + "'");
            }
            catch (Exception e) { Plugin.Log.LogWarning("ghost nick: " + e.Message); }
        }

        /// <summary>
        /// Replace the cloned character/kart models with the ones the remote player
        /// actually picked. CharacterData (the ScriptableObjects the game loads from
        /// Addressables) exposes charModel / kartModel / GetModel(skinIndex), so the
        /// swap is a matter of instantiating those under the same two nodes the game
        /// uses: .../AnimPivot/AccelPivot/Accel2Pivot/Fumo and .../AnimPivot/Kart.
        /// Falls back to the clone (your own model) when the data cannot be found.
        /// </summary>
        private static void RebuildModel(Ghost g, int slot)
        {
            try
            {
                var cds = UnityEngine.Resources.FindObjectsOfTypeAll<CharacterData>();
                if (cds == null || cds.Length == 0)
                {
                    Plugin.Log.LogWarning("ghost: no CharacterData loaded, slot " + slot + " keeps the cloned model");
                    return;
                }

                CharacterData cd = null;
                for (int i = 0; i < cds.Length; i++)
                {
                    var c = cds[i];
                    if (c == null) continue;
                    int idx = -1;
                    try { idx = GameManager.GetCharacterIndex(c); } catch { }
                    if (idx == g.Character) { cd = c; break; }
                }
                if (cd == null)
                {
                    // fall back to the loaded order when the game cannot map it
                    if (g.Character >= 0 && g.Character < cds.Length) cd = cds[g.Character];
                }
                if (cd == null)
                {
                    Plugin.Log.LogWarning("ghost: character index " + g.Character + " not found among "
                                          + cds.Length + " loaded characters; slot " + slot + " keeps the cloned model");
                    return;
                }

                string charName = null;
                try { charName = cd.displayName; } catch { }

                var root = g.Go.transform;
                var fumoNode = root.Find("Character/VehiclePivot/AnimPivot/AccelPivot/Accel2Pivot/Fumo");
                var kartNode = root.Find("Character/VehiclePivot/AnimPivot/Kart");

                // ---- the fumo itself
                GameObject newFumo = null;
                try { newFumo = cd.GetModel(g.Skin); } catch { }
                if (newFumo == null) { try { newFumo = cd.charModel; } catch { } }
                if (newFumo != null && fumoNode != null)
                {
                    for (int i = fumoNode.childCount - 1; i >= 0; i--)
                        UnityEngine.Object.Destroy(fumoNode.GetChild(i).gameObject);
                    var inst = UnityEngine.Object.Instantiate(newFumo, fumoNode);
                    if (inst != null) inst.transform.localPosition = Vector3.zero;
                }

                // ---- the kart: replace the model child, keep the FX children
                GameObject newKart = null;
                try { newKart = cd.kartModel; } catch { }
                if (newKart == null)
                {
                    try
                    {
                        var vs = cd.vehicles;
                        if (vs != null && vs.Count > 0)
                        {
                            int vi = Mathf.Clamp(g.Vehicle, 0, vs.Count - 1);
                            var vd = vs[vi];
                            if (vd != null) newKart = VehicleModel(vd);
                        }
                    }
                    catch { }
                }
                if (newKart != null && kartNode != null)
                {
                    for (int i = kartNode.childCount - 1; i >= 0; i--)
                    {
                        var child = kartNode.GetChild(i);
                        string n = child.name;
                        if (n.EndsWith("FX") || child.name.IndexOf("FX", StringComparison.OrdinalIgnoreCase) >= 0) continue;  // keep the effects
                        UnityEngine.Object.Destroy(child.gameObject);
                    }
                    var inst = UnityEngine.Object.Instantiate(newKart, kartNode);
                    if (inst != null) inst.transform.localPosition = Vector3.zero;
                }

                g.ModelBuilt = true;
                Plugin.Log.LogWarning("ghost: slot " + slot + " now wears character " + g.Character
                                      + " '" + charName + "' (skin " + g.Skin + "): fumo="
                                      + (newFumo == null ? "clone kept" : newFumo.name)
                                      + " kart=" + (newKart == null ? "clone kept" : newKart.name));
                SetRenderers(g.Go, g.Visible);
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                Plugin.Log.LogWarning("ghost model swap failed for slot " + slot + ": " + inner.Message);
            }
        }

        /// <summary>VehicleData keeps its model in a member whose name varies; look for it.</summary>
        private static GameObject VehicleModel(VehicleData vd)
        {
            try
            {
                foreach (string n in new[] { "model", "kartModel", "prefab", "_model", "_prefab" })
                {
                    var o = MpDiag.Get(vd, n);
                    var go = o as GameObject;
                    if (go != null) return go;
                }
            }
            catch { }
            return null;
        }

        // ----------------------------------------------------------- nameplate
        /// <summary>A billboard label above the kart, so you know who is who.</summary>
        private static void BuildNameplate(Ghost g, int slot)
        {
            try
            {
                var go = new GameObject("FumoMP_Nameplate");
                go.transform.SetParent(g.Go.transform, false);
                go.transform.localPosition = new Vector3(0f, 3.1f, 0f);
                go.transform.localRotation = Quaternion.identity;

                var tmp = go.AddComponent<TMPro.TextMeshPro>();
                tmp.text = "Player" + slot;
                tmp.fontSize = 3.2f;
                tmp.color = Color.white;
                tmp.alignment = TMPro.TextAlignmentOptions.Center;
                tmp.enableWordWrapping = false;
                var f = MpUgui.GetFont();
                if (f != null) tmp.font = f;
                tmp.rectTransform.sizeDelta = new Vector2(9f, 1.6f);
                tmp.rectTransform.localPosition = Vector3.zero;

                g.Nameplate = go;
                go.SetActive(false);            // shown with the kart
            }
            catch (Exception e) { Plugin.Log.LogWarning("ghost nameplate: " + (e.InnerException ?? e).Message); }
        }

        /// <summary>Keep every nameplate facing this machine's camera, hide it when far.</summary>
        private static void UpdateNameplates()
        {
            try
            {
                Transform cam = null;
                var c = Camera.main;
                if (c != null) cam = c.transform;

                foreach (var kv in Map)
                {
                    var g = kv.Value;
                    if (g.Nameplate == null) continue;
                    bool on = g.Visible && g.Go != null;
                    if (on && cam != null)
                    {
                        float d = Vector3.Distance(cam.position, g.Go.transform.position);
                        if (d > 90f) on = false;
                        g.Nameplate.transform.rotation = cam.rotation;
                    }
                    else on = false;
                    if (g.Nameplate.activeSelf != on) g.Nameplate.SetActive(on);
                }
            }
            catch { }
        }

        // --------------------------------------------------------------- network
        /// <summary>A kart state arrived for that slot: buffer it and show the ghost.</summary>
        internal static void Apply(int slot, Vector3 pos, Quaternion rot, float speed, float steer,
                                   bool grounded, float now)
        {
            try
            {
                var g = Get(slot);
                if (g == null) return;

                if (!g.Visible)
                {
                    g.P0 = g.P1 = pos;
                    g.R0 = g.R1 = rot;
                    g.T0 = g.T1 = now;
                    g.Go.transform.position = pos;
                    g.Go.transform.rotation = rot;
                    g.Visible = true;
                    SetRenderers(g.Go, true);
                    Plugin.Log.LogInfo("ghost: slot " + slot + " visible at " + Fmt(pos)
                                       + " (first packet, " + Map.Count + " ghost(s) in the session)");
                }
                else
                {
                    g.P0 = g.P1; g.R0 = g.R1; g.T0 = g.T1;
                    g.P1 = pos; g.R1 = rot; g.T1 = now;
                    if (g.T1 - g.T0 > 1.5f) { g.P0 = pos; g.R0 = rot; g.T0 = now; }   // long gap: no coasting
                }
                g.Speed = speed;
                g.Steer = steer;
                g.Grounded = grounded;
                g.LastApply = now;
            }
            catch (Exception e) { Plugin.Log.LogWarning("ghost apply: " + e.Message); }
        }

        private static Ghost Get(int slot)
        {
            Ghost g;
            if (Map.TryGetValue(slot, out g) && g.Go != null) return g;
            Ensure(slot);
            return Map.TryGetValue(slot, out g) ? g : null;
        }

        /// <summary>Destroy the ghost of a player who left (or timed out).</summary>
        internal static void RemoveRemote(int slot)
        {
            Ghost g;
            if (!Map.TryGetValue(slot, out g)) return;
            try { if (g.Go != null) UnityEngine.Object.Destroy(g.Go); } catch { }
            Map.Remove(slot);
            Plugin.Log.LogInfo("ghost: slot " + slot + " removed");
        }

        internal static void ClearRemote()
        {
            foreach (var kv in Map)
                try { if (kv.Value.Go != null) UnityEngine.Object.Destroy(kv.Value.Go); } catch { }
            Map.Clear();
        }

        // ------------------------------------------------------------------ tick
        /// <summary>
        /// Drives every ghost. Network states arrive at 20 Hz while the game runs
        /// faster, so the transform is rendered slightly in the past and
        /// interpolated between the two newest samples, coasting at the last known
        /// velocity for up to a quarter of a second when a packet is late.
        /// </summary>
        internal static void Tick(float dt)
        {
            try
            {
                int test = TestCount;
                if (test > 0) Orbit(test);

                float now = Time.realtimeSinceStartup;
                const float delay = 0.12f;
                float renderT = now - delay;
                List<int> stale = null;

                foreach (var kv in Map)
                {
                    var g = kv.Value;
                    if (g.Go == null) { (stale ?? (stale = new List<int>())).Add(kv.Key); continue; }
                    if (!g.Visible) continue;

                    float span = g.T1 - g.T0;
                    Vector3 pos;
                    Quaternion rot;

                    if (span > 0.0001f && renderT > g.T0 && renderT < g.T1)
                    {
                        float u = (renderT - g.T0) / span;
                        pos = Vector3.Lerp(g.P0, g.P1, u);
                        rot = Quaternion.Slerp(g.R0, g.R1, u);
                    }
                    else
                    {
                        float ahead = Mathf.Clamp(renderT - g.T1, 0f, 0.25f);
                        Vector3 vel = span > 0.0001f ? (g.P1 - g.P0) / span : Vector3.zero;
                        if (vel.magnitude > 60f) vel = vel.normalized * 60f;      // 216 km/h sanity cap
                        pos = g.P1 + vel * ahead;
                        rot = g.R1;
                    }

                    g.Go.transform.position = pos;
                    g.Go.transform.rotation = rot;

                    float quiet = now - g.LastApply;
                    if (quiet > 2.5f && g.Visible)
                    {
                        // a hitch of a few seconds should blink the kart out rather
                        // than leave a frozen one on the track; it comes straight
                        // back on the next packet
                        g.Visible = false;
                        SetRenderers(g.Go, false);
                        Plugin.Log.LogInfo("ghost: slot " + kv.Key + " hidden after " + quiet.ToString("F1") + "s of silence");
                    }
                    if (quiet > 25f) (stale ?? (stale = new List<int>())).Add(kv.Key);
                }

                UpdateNameplates();

                if (stale != null)
                    foreach (var slot in stale)
                    {
                        Ghost g;
                        if (!Map.TryGetValue(slot, out g)) continue;
                        Plugin.Log.LogWarning("ghost: slot " + slot + " has not sent anything for 25s - removing it");
                        try { if (g.Go != null) UnityEngine.Object.Destroy(g.Go); } catch { }
                        Map.Remove(slot);
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning("ghost tick: " + e.Message); }
        }

        /// <summary>Harness-only orbit, so a single machine can prove the ghost renders.</summary>
        private static void Orbit(int count)
        {
            try
            {
                var local = MpDiag.LocalRacer();
                if (local == null) return;
                var lp = local.transform.position;
                for (int slot = 0; slot < count; slot++)
                {
                    var g = Get(slot);
                    if (g == null || g.Go == null) continue;
                    float a = (Time.realtimeSinceStartup * 0.7f) + slot * Mathf.PI;
                    var want = lp + new Vector3(Mathf.Sin(a) * 8f, 0.05f, Mathf.Cos(a) * 8f);
                    var look = lp - want; look.y = 0f;
                    g.Visible = true;
                    SetRenderers(g.Go, true);
                    g.Go.transform.position = want;
                    if (look.sqrMagnitude > 0.01f) g.Go.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
                    g.LastApply = Time.realtimeSinceStartup;
                }
            }
            catch { }
        }

        /// <summary>Log line: is every ghost actually in front of a camera?</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Visibility()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                foreach (var kv in Map)
                {
                    var g = kv.Value;
                    if (g == null || g.Go == null) { sb.Append("slot").Append(kv.Key).Append("=destroyed "); continue; }
                    int total = 0, vis = 0;
                    foreach (var r in g.Go.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        total++;
                        if (r.isVisible) vis++;
                    }
                    var k = MpNet.Kart(kv.Key);
                    sb.Append("slot").Append(kv.Key)
                      .Append(" '").Append(k != null && !string.IsNullOrEmpty(k.Nick) ? k.Nick : "?").Append('\'')
                      .Append(g.Visible ? " shown" : " hidden")
                      .Append(" renderers=").Append(vis).Append('/').Append(total)
                      .Append(" pos=").Append(Fmt(g.Go.transform.position))
                      .Append(" net=").Append((Time.realtimeSinceStartup - g.LastApply).ToString("F2")).Append("s")
                      .Append(" speed=").Append(g.Speed.ToString("F1"))
                      .Append("  ");
                }
            }
            catch (Exception e) { sb.Append("visibility: ").Append(e.GetType().Name); }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        private static string Fmt(Vector3 v)
        {
            return v.x.ToString("F0") + "," + v.y.ToString("F0") + "," + v.z.ToString("F0");
        }
    }
}
