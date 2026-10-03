using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using Mirror;
using TMPro;

namespace FumoMP;

/// <summary>
/// Isolated probes and steps for every interop member MpFlow touches. A member
/// missing from the generated interop assembly throws MissingMethod/Field/
/// TypeLoadException while the *calling* method is JIT-compiled, so an inner
/// try/catch cannot see it — each access therefore lives in its own method and
/// the caller (Probe/Step) catches it.
/// </summary>
internal static class MpDiag
{
    // ------------------------------------------------------------- diagnostics
    internal static void Run()
    {
        Plugin.Log.LogInfo("---- MpFlow diagnostics ----");
        Probe("NetworkManager.singleton", () => {
            var nm = NetworkManager.singleton;
            return nm == null ? "null" : "ok (" + nm.name + ")";
        });
        Probe("nm.mode", () => {
            var nm = NetworkManager.singleton;
            return nm == null ? "null" : nm.mode.ToString();
        });
        Probe("nm.numPlayers", () => {
            var nm = NetworkManager.singleton;
            return nm == null ? "null" : nm.numPlayers.ToString();
        });
        Probe("nm.isNetworkActive", () => {
            var nm = NetworkManager.singleton;
            return nm == null ? "null" : nm.isNetworkActive.ToString();
        });
        Probe("nm.networkAddress(read)", () => {
            var nm = NetworkManager.singleton;
            return nm == null ? "null" : (nm.networkAddress ?? "<null>");
        });
        Probe("nm.networkAddress(write)", () => {
            var nm = NetworkManager.singleton;
            if (nm == null) return "null";
            nm.networkAddress = nm.networkAddress;
            return "ok";
        });
        Probe("KcpTransport on nm", () => {
            var nm = NetworkManager.singleton;
            if (nm == null) return "null";
            var k = nm.gameObject.GetComponent<kcp2k.KcpTransport>();
            return k == null ? "no transport" : "ok";
        });
        Probe("KcpTransport.Port read/write", () => {
            var nm = NetworkManager.singleton;
            if (nm == null) return "null";
            var k = nm.gameObject.GetComponent<kcp2k.KcpTransport>();
            if (k == null) return "no transport";
            var p = k.Port;
            k.Port = p;
            return "ok port=" + p;
        });
        Probe("FRNetGameState.instance", () => {
            var gs = FRNetGameState.instance;
            return gs == null ? "null" : "ok";
        });
        Probe("FRNetGameState.instance.map read", () => {
            var gs = FRNetGameState.instance;
            if (gs == null) return "null";
            return gs.map ?? "<null>";
        });
        Probe("FRNetGameState.instance.map write", () => {
            var gs = FRNetGameState.instance;
            if (gs == null) return "null";
            gs.map = MpFlow.SelectedMap;
            return "ok -> " + MpFlow.SelectedMap;
        });
        Probe("FRNetworkClient.instance", () => {
            var c = FRNetworkClient.instance;
            return c == null ? "null" : "ok";
        });
        Probe("FRNetworkClient.SetNickname", () => {
            var c = FRNetworkClient.instance;
            if (c == null) return "null";
            c.SetNickname(MpFlow.Nickname);
            return "ok";
        });
        Probe("FRNetworkServer.instance", () => {
            var s = FRNetworkServer.instance;
            return s == null ? "null" : "ok";
        });
        Probe("GameManager.inRace", () => GameManager.inRace.ToString());
        Probe("FindObjectsOfType<MultiplayerManager>", () => {
            var a = UnityEngine.Object.FindObjectsOfType<MultiplayerManager>();
            return a == null ? "null" : ("count=" + a.Length);
        });
        Probe("FindObjectOfType<FRNetworkManager>", () => {
            var m = UnityEngine.Object.FindObjectOfType<FRNetworkManager>();
            return m == null ? "null" : ("ok(" + m.name + ")");
        });
        Probe("FindObjectOfType<MainMenuManager>", () => {
            var m = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
            return m == null ? "null" : ("ok(" + m.name + ")");
        });
        Probe("FindObjectOfType<EventSystem>", () => {
            var m = UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            return m == null ? "null" : ("ok(" + m.name + ")");
        });
        int roster = 0;
        Probe("PlayerRoster", () => { var r = PlayerRoster(out roster); return "count=" + roster + " [" + (r ?? "").Replace("\n", " ") + "]"; });
        Plugin.Log.LogInfo("---- end diagnostics ----");
    }

    private static void Probe(string label, Func<string> f)
    {
        try { Plugin.Log.LogInfo("  probe " + label + " => " + f()); }
        catch (Exception e) { Plugin.Log.LogError("  probe " + label + " => FAILED " + e.GetType().Name + ": " + e.Message); }
    }

    // ------------------------------------------------------------ flow steps
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepNickname()
    {
        var c = FRNetworkClient.instance;
        if (c == null) return "FRNetworkClient.instance == null";
        c.SetNickname(MpFlow.Nickname);
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepSetMap()
    {
        var gs = FRNetGameState.instance;
        if (gs == null) return "FRNetGameState.instance == null";
        gs.map = MpFlow.SelectedMap;
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepAddress(string addr)
    {
        var nm = NetworkManager.singleton;
        if (nm == null) return "NetworkManager.singleton == null";
        nm.networkAddress = addr;
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepStartHost()
    {
        var nm = NetworkManager.singleton;
        if (nm == null) return "NetworkManager.singleton == null";
        nm.StartHost();
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepStartClient()
    {
        var nm = NetworkManager.singleton;
        if (nm == null) return "NetworkManager.singleton == null";
        nm.StartClient();
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepStop()
    {
        var nm = NetworkManager.singleton;
        if (nm == null) return "NetworkManager.singleton == null";
        if (nm.mode == NetworkManagerMode.Host) nm.StopHost();
        else if (nm.mode == NetworkManagerMode.ClientOnly) nm.StopClient();
        else if (nm.mode == NetworkManagerMode.ServerOnly) nm.StopServer();
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepPort(ushort port)
    {
        var nm = NetworkManager.singleton;
        if (nm == null) return "NetworkManager.singleton == null";
        var k = nm.gameObject.GetComponent<kcp2k.KcpTransport>();
        if (k == null) return "no KcpTransport";
        if (k.Port != port) k.Port = port;
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string ModeName()
    {
        var nm = NetworkManager.singleton;
        return nm == null ? "none" : nm.mode.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string NetActive()
    {
        var nm = NetworkManager.singleton;
        return nm == null ? "none" : nm.isNetworkActive.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int PlayerCountValue()
    {
        var nm = NetworkManager.singleton;
        return nm == null ? 0 : nm.numPlayers;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsHostingValue()
    {
        var nm = NetworkManager.singleton;
        if (nm == null) return false;
        var m = nm.mode;
        return m == NetworkManagerMode.Host || m == NetworkManagerMode.ServerOnly;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsClientValue()
    {
        var nm = NetworkManager.singleton;
        if (nm == null) return false;
        return nm.mode == NetworkManagerMode.ClientOnly;
    }

    // ------------------------------------------------- multiplayer subsystem
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool MpComponentsPresent()
    {
        return UnityEngine.Object.FindObjectOfType<FRNetworkManager>() != null;
    }

    /// <summary>
    /// Load the game's multiplayer scene. The main menu only carries the broken
    /// MultiplayerManager; the net components live in a scene the original
    /// EnterMP never loads because it asks for the stripped 'mpmenu'.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepEnterMpScene()
    {
        if (MpComponentsPresent()) return null;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.name == "menu2" || scene.name == "mpmenu") return null; // already there
        var lm = LevelManager.instance;
        if (lm == null) return "LevelManager.instance == null";
        Plugin.Log.LogInfo("Loading multiplayer scene 'menu2' via LevelManager.ChangeLevel (from '" + scene.name + "')");
        lm.ChangeLevel("menu2");
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Transform FindInactiveDeep(Transform t, string name, int depth)
    {
        if (t == null || depth > 14) return null;
        if (t.name == name) return t;
        int n = t.childCount;
        for (int i = 0; i < n; i++)
        {
            var hit = FindInactiveDeep(t.GetChild(i), name, depth + 1);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>Activate a GameObject and every inactive ancestor (root first).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ActivateWithAncestors(GameObject go)
    {
        if (go == null) return 0;
        var chain = new System.Collections.Generic.List<Transform>();
        var t = go.transform;
        while (t != null) { chain.Add(t); t = t.parent; }

        int changed = 0;
        for (int i = chain.Count - 1; i >= 0; i--) // root -> leaf
        {
            var g = chain[i].gameObject;
            if (g != null && !g.activeSelf)
            {
                Plugin.Log.LogInfo("  activating '" + chain[i].name + "' (depth " + i + " from root)");
                g.SetActive(true);
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// The shipped build keeps the net subsystem switched off: the "Multiplayer"
    /// object is inactive, so every MonoSingleton instance is null. Find the
    /// components (Unity can search inactive objects) and switch on the whole
    /// ancestor chain.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepActivateMultiplayer()
    {
        if (MpComponentsPresent()) return null;

        int totalChanged = 0;
        string firstName = "?";
        try
        {
            var all = UnityEngine.Object.FindObjectsOfType<FRNetworkManager>(true);
            if (all != null && all.Length > 0)
            {
                firstName = all[0].gameObject.name;
                for (int i = 0; i < all.Length; i++)
                {
                    var go = all[i].gameObject;
                    if (go != null && !go.activeInHierarchy)
                    {
                        Plugin.Log.LogInfo("Enabling net object '" + go.name + "'");
                        totalChanged += ActivateWithAncestors(go);
                    }
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("FindObjectsOfType(inactive): " + e.GetType().Name); }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Plugin.Log.LogInfo("Activation pass: scene=" + scene.name + " first=" + firstName + " activated=" + totalChanged);

        if (MpComponentsPresent()) return null;
        return "net components still missing after activating (scene=" + scene.name + ", activated=" + totalChanged + ")";
    }

    // ----------------------------------------------------------- player roster
    /// <summary>
    /// Lobby roster. FRNetworkServer.GetPlayers() only has data on the host, but
    /// FRNetworkPlayer objects are spawned and synced on both sides, so
    /// FindObjectsOfType works for host and client alike; nicknames come from the
    /// synced SFumoClient info and rtt is the live ping the game tracks.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string PlayerRoster(out int count)
    {
        count = 0;
        var arr = UnityEngine.Object.FindObjectsOfType<FRNetworkPlayer>(true);
        if (arr == null || arr.Length == 0)
        {
            // The game spawns its own player objects from code that is part of the
            // unfinished netcode, so after a session restart they can be missing
            // even though the host is connected and running. Fall back to what the
            // transport knows, so the host always sees itself in the list.
            return TransportRoster(out count);
        }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < arr.Length; i++)
        {
            var p = arr[i];
            if (p == null) continue;
            string nick = null;
            try { nick = p.nickName; } catch { }
            if (string.IsNullOrEmpty(nick)) nick = "Player" + (i + 1);
            double rtt = 0;
            try { rtt = p.rtt; } catch { }
            bool isLocal = false;
            try { isLocal = (FRNetworkPlayer.localPlayer == p); } catch { }
            sb.Append("  - ").Append(nick);
            if (isLocal) sb.Append("  (you)");
            if (rtt > 0.5) sb.Append("   ").Append((int)rtt).Append("ms");
            sb.Append('\n');
            count++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Connection slot of a player on the host (the game's NetworkConnection has
    /// no usable 'address' member in this build - it was stripped - so we show the
    /// connection id instead, which is what the host can actually distinguish).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string PlayerConnOf(FRNetworkPlayer target)
    {
        try
        {
            var srv = FRNetworkServer.instance;
            if (srv == null || target == null) return null;
            var table = srv.GetPlayers();
            if (table == null) return null;
            var it = table.GetEnumerator();
            while (it.MoveNext())
            {
                var kv = it.Current;
                if (kv.Value == null) continue;
                bool same = false;
                try { same = (kv.Value.Pointer == target.Pointer); } catch { }
                if (!same) continue;
                var conn = kv.Key;
                if (conn == null) return null;
                return "#" + conn.connectionId;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("player conn: " + e.GetType().Name); }
        return null;
    }

    /// <summary>
    /// Roster rows with TMP markup: name column, role tag and a right-aligned
    /// ping that is coloured green/yellow/red. Host and client both see the same
    /// data because the player objects are synced.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string PlayerRosterRich(out int count)
    {
        count = 0;

        // The kart channel is the authoritative roster for a running session: it
        // knows the nickname and the ping of every machine, including its own, and
        // unlike the game's own (unfinished) player objects it survives the scene
        // loads. Falls back to the game's objects when the channel is down.
        try
        {
            var peers = MpNet.Peers;
            if (MpNet.Running && peers.Count > 0)
            {
                bool hosting0 = MpFlow.IsHosting;
                var sb0 = new System.Text.StringBuilder();
                var mine = new MpNet.Peer { Slot = MpNet.OwnSlot, Nick = MpFlow.Nickname, IsHost = hosting0 };
                var all = new System.Collections.Generic.List<MpNet.Peer>();
                all.Add(mine);
                foreach (var p in peers) if (p.Slot != MpNet.OwnSlot) all.Add(p);
                all.Sort((a, b) => a.Slot.CompareTo(b.Slot));

                foreach (var p in all)
                {
                    string nick = string.IsNullOrEmpty(p.Nick) ? ("Player" + p.Slot) : p.Nick;
                    if (nick.Length > 20) nick = nick.Substring(0, 20);
                    bool me = p.Slot == MpNet.OwnSlot;
                    float rtt = me ? 0f : p.RttMs;
                    string tag = me ? (hosting0 ? "HOST" : "ME") : (p.IsHost ? "HOST" : "");
                    string ping = me ? "-" : (rtt > 0.5f ? ((int)rtt + "ms") : "...");
                    string col = me ? "#8C93A6" : (rtt <= 0.5f ? "#8C93A6" : (rtt < 60f ? "#6FE38C" : (rtt < 150f ? "#F2D95E" : "#F06A6A")));
                    sb0.Append("<color=#E8EDFA>").Append(nick.PadRight(16)).Append("</color>")
                       .Append("<color=").Append(me ? (hosting0 ? "#6FE38C" : "#8FB6FF") : "#6FE38C").Append('>')
                       .Append(tag.PadRight(6)).Append("</color>")
                       .Append("  <color=").Append(col).Append('>').Append(ping).Append("</color>")
                       .Append("  <color=#8C93A6>slot ").Append(p.Slot).Append("</color>");
                    if (count + 1 < all.Count) sb0.Append('\n');
                    count++;
                }
                return sb0.ToString();
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("roster (channel): " + e.Message); }

        var arr = UnityEngine.Object.FindObjectsOfType<FRNetworkPlayer>(true);
        if (arr == null || arr.Length == 0) return "";

        bool hosting = false;
        try { hosting = MpFlow.IsHosting; } catch { }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < arr.Length; i++)
        {
            var p = arr[i];
            if (p == null) continue;
            string nick = null;
            try { nick = p.nickName; } catch { }
            if (string.IsNullOrEmpty(nick)) nick = "Player" + (i + 1);
            if (nick.Length > 20) nick = nick.Substring(0, 20);

            double rtt = 0;
            try { rtt = p.rtt; } catch { }
            bool isLocal = false;
            try { isLocal = (FRNetworkPlayer.localPlayer == p); } catch { }

            string tag = isLocal ? (hosting ? "HOST" : "ME") : "";
            string ping = rtt > 0.5 ? ((int)rtt + "ms") : "-";
            string pingCol = rtt <= 0.5 ? "#8C93A6" : (rtt < 60 ? "#6FE38C" : (rtt < 150 ? "#F2D95E" : "#F06A6A"));

            string ip = null;
            try { ip = PlayerConnOf(p); } catch { }
            string ipCol = string.IsNullOrEmpty(ip) ? "-" : ip;

            sb.Append("<color=#E8EDFA>").Append(nick.PadRight(16)).Append("</color>")
              .Append("<color=#9FB4D8>").Append(ipCol.PadRight(16)).Append("</color>")
              .Append("<color=#FFD766>").Append(tag.PadRight(6)).Append("</color>")
              .Append("<color=").Append(pingCol).Append('>').Append(ping.PadLeft(6)).Append("</color>")
              .Append('\n');
            count++;
        }
        return sb.ToString();
    }


    // -------------------------------------------------------- local addresses
    /// <summary>
    /// Local IPv4 addresses, ranked by how likely a friend can actually reach them.
    /// Tailscale / Hyper-V / WSL / Docker adapters are common and useless to a
    /// friend on the same LAN, so they are labelled and pushed to the bottom.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] EnumerateIpsCore()
    {
        var ranked = new System.Collections.Generic.List<string>();
        var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
        if (nics != null)
        {
            for (int i = 0; i < nics.Length; i++)
            {
                var ni = nics[i];
                if (ni == null) continue;
                try
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    var props = ni.GetIPProperties();
                    if (props == null) continue;
                    var addrs = props.UnicastAddresses;
                    if (addrs == null) continue;
                    string name = ni.Name ?? "";
                    for (int j = 0; j < addrs.Count; j++)
                    {
                        var ua = addrs[j];
                        if (ua == null || ua.Address == null) continue;
                        if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("127.")) continue;                 // loopback: useless to share
                        string kind = ClassifyIp(ip, name);
                        if (kind == "virtual") continue;                     // docker/Hyper-V/WSL bridges
                        ranked.Add(kind + "|" + ip + "|" + name);
                    }
                }
                catch { }
            }
        }
        ranked.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
        var outp = new System.Collections.Generic.List<string>();
        for (int i = 0; i < ranked.Count; i++)
        {
            var parts = ranked[i].Split('|');
            outp.Add(parts[1] + "|" + parts[2] + "|" + parts[0]);
        }
        return outp.ToArray();
    }

    private static int Rank(string entry)
    {
        if (entry.StartsWith("lan|")) return 0;
        if (entry.StartsWith("vpn|")) return 1;
        return 2;
    }

    private static string ClassifyIp(string ip, string adapter)
    {
        string a = (adapter ?? "").ToLowerInvariant();
        if (a.Contains("docker") || a.Contains("veth") || a.Contains("wsl")
            || a.Contains("hyper-v") || a.Contains("vmware") || a.Contains("virtualbox")
            || a.Contains("loopback") || a.Contains("tap") || a.Contains("tun"))
            return a.Contains("tailscale") ? "vpn" : "virtual";
        if (a.Contains("tailscale") || a.Contains("zerotier") || a.Contains("wireguard")) return "vpn";
        if (ip.StartsWith("100.")) return "vpn";                 // CGNAT range used by Tailscale
        if (ip.StartsWith("192.168.") || ip.StartsWith("10.")) return "lan";
        if (ip.StartsWith("172."))
        {
            int second = 0;
            var seg = ip.Split('.');
            if (seg.Length > 1) int.TryParse(seg[1], out second);
            if (second >= 16 && second <= 31) return "lan";
            return "virtual";                                     // docker / bridge range
        }
        return "other";
    }

    internal static string[] LocalIps()
    {
        try { return EnumerateIpsCore(); }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("ip enum: " + e.GetType().Name);
            return new string[0];
        }
    }

    /// <summary>First ranked address (best candidate for a friend to dial).</summary>
    internal static string FirstLanIp(out string label)
    {
        label = null;
        var all = LocalIps();
        if (all == null || all.Length == 0) return null;
        var p = all[0].Split('|');
        label = p.Length > 1 ? p[1] : null;
        return p[0];
    }

    // ------------------------------------------------------------ racer info
    /// <summary>
    /// The original build sends the player's character/skin/vehicle inside the
    /// connection message (SFumoClient._racerInfo) but only ever implemented
    /// SetNickname(), so there was no way to choose them online. We write the
    /// struct straight through: value-type fields come back as copies, so each
    /// level is read, modified and written back.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepSetRacerInfo(int character, int skin, int vehicle)
    {
        var c = FRNetworkClient.instance;
        if (c == null) return "FRNetworkClient.instance == null";
        var info = c._clientInfo;
        var ri = info._racerInfo;
        ri._character = character;
        ri._skin = skin;
        ri._vehicle = vehicle;
        info._racerInfo = ri;
        c._clientInfo = info;

        // read back: struct writes must be verified, a silent failure would mean
        // players spawn as the wrong character
        var check = c._clientInfo._racerInfo;
        if (check._character != character || check._skin != skin || check._vehicle != vehicle)
            return "racerInfo write did not stick (read back char=" + check._character + " skin=" + check._skin + ")";
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string RacerInfoText()
    {
        var c = FRNetworkClient.instance;
        if (c == null) return "(no client)";
        var info = c._clientInfo;
        var ri = info._racerInfo;
        return "char=" + ri._character + " skin=" + ri._skin + " vehicle=" + ri._vehicle;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int CharacterCount()
    {
        try { return GameManager.charactersCount; } catch { return 0; }
    }

    /// <summary>
    /// Character display name. The game's character table is a *protected static*
    /// field, and reading it through interop crashed the real client, so we only
    /// expose the public charactersCount and let the UI show a plain index.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string CharacterName(int idx)
    {
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int PlayerCountLive()
    {
        var arr = UnityEngine.Object.FindObjectsOfType<FRNetworkPlayer>(true);
        return arr == null ? 0 : arr.Length;
    }

    // ------------------------------------------------------------- race start
    /// <summary>
    /// Start the race by calling the game's own entry point. Simulating a keypress
    /// was not enough: FRNetGameState.ServerUpdate reads the key through the
    /// internal Input helper, which IL2CPP inlined, so a Harmony patch on the
    /// public Input.GetKeyDown never saw it. StartGame(GameSettings) is public and
    /// takes the very settings the game itself passes, so calling it runs exactly
    /// the original code path (collect racers -> broadcast settings -> spawn sync).
    /// </summary>
    /// <summary>
    /// What the StartGame coroutine may be waiting on: the spawned game-sync
    /// object, connection count, spawned network objects and the race flag.
    /// </summary>
    /// <summary>
    /// Send the readiness message a joining client is supposed to send. The
    /// StartGame coroutine waits for the ready handshake, and in this build the
    /// client-side half of it never runs, so the host (which is also a client)
    /// sends it to itself.
    /// </summary>
    /// <summary>
    /// The game's own local start, exactly what the single-player "Race!" button
    /// calls: it loads the chosen circuit and spawns the racers (which the netcode
    /// picks up through its spawn callbacks). The multiplayer coroutine stalls
    /// before this point in this build, so the host drives it directly.
    /// </summary>
    /// <summary>
    /// Compare the settings the netcode holds with the ones the game itself kept
    /// from the last local race. The netcode copy is what gets handed to the
    /// loader, so if its _level is empty the circuit can never load.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string SettingsProbe()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var gs = FRNetGameState.instance;
            var s = gs._settings;
            sb.Append("netSettings{level='").Append(s._level ?? "NULL")
              .Append("' players=").Append(s._players == null ? "null" : s._players.Length.ToString())
              .Append(" laps=").Append(s._laps)
              .Append(" itemType=").Append(s._itemType)
              .Append(" noAuto=").Append(s._disableAutoStart).Append('}');
        }
        catch (System.Exception e) { sb.Append("netSettings err:").Append(e.GetType().Name); }

        try
        {
            var last = GameManager.lastSettings;
            sb.Append(" lastSettings{level='").Append(last._level ?? "NULL")
              .Append("' players=").Append(last._players == null ? "null" : last._players.Length.ToString())
              .Append(" laps=").Append(last._laps)
              .Append(" noAuto=").Append(last._disableAutoStart).Append('}');
        }
        catch (System.Exception e) { sb.Append(" lastSettings err:").Append(e.GetType().Name); }

        try { sb.Append(" map='").Append(FRNetGameState.instance.map ?? "NULL").Append('\''); } catch { }
        return sb.ToString();
    }

    /// <summary>
    /// Fill in the participants. The netcode's GameSettings carries a level and a
    /// lap count but a null _players array, and the game's loader then has nobody
    /// to spawn, so it parks in the loading scene forever. One GamePlayer per
    /// connected player is built here (the host is player 0).
    /// </summary>
    /// <summary>
    /// Ask LevelManager for the circuit directly. GameManager's own start did
    /// begin a load but never finished it, so the menu's loader is tried as the
    /// explicit alternative (bare scene name, the form the menu uses).
    /// </summary>
    /// <summary>Start the countdown on this machine (the game's own StartSequence).</summary>
    /// <summary>The parameterless overload - in single player this is what spawns
    /// the racers and begins the countdown.</summary>
    /// <summary>
    /// Give the race its racers. The circuit scene's CircuitBehaviour holds the
    /// start points and normally fires onStartCircuit, which GameManager turns
    /// into OnStartCircuit -> SpawnPlayers -> StartSequence. That hand-off does not
    /// happen in this build, so the same entry point is invoked explicitly with
    /// the points read from the loaded circuit.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepStartCircuitViaBehaviour()
    {
        if (_spawnDone) return null;

        var cb = UnityEngine.Object.FindObjectOfType<CircuitBehaviour>();
        if (cb == null) return "no CircuitBehaviour in the loaded circuit";

        var points = cb._startPoints;
        if (points == null) return "circuit has no _startPoints";
        if (points.Length == 0) return "circuit _startPoints is empty";

        var trigger = TriggerCircuit.instance;
        var gs = FRNetGameState.instance;
        if (gs == null) return "no FRNetGameState";
        var settings = gs._settings;

        var pl = settings._players;
        if (pl != null)
        {
            for (int i = 0; i < pl.Length; i++)
            {
                var plr = pl[i];
                Plugin.Log.LogInfo("  player " + i + ": " + (plr == null ? "NULL"
                    : plr.GetType().Name + " char=" + plr.character + " skin=" + plr.skin
                      + " vehicle=" + plr.vehicle + " humanIndex=" + GameManager.GetHumanIndex(plr)));
            }
        }

        // The game's own hand-off. It spawns the racers, gives each one a camera
        // and a HUD at its human index, publishes the racer array the countdown
        // reads and raises the race flag, so it has to be the first choice: the
        // pieces it leaves behind are what StartSequence dereferences.
        var m = HarmonyLib.AccessTools.Method(typeof(GameManager), "OnStartCircuit");
        if (m != null)
        {
            Plugin.Log.LogInfo("OnStartCircuit(points=" + points.Length + ", trigger=" + (trigger != null) + ")");
            try
            {
                m.Invoke(null, new object[] { points, trigger, settings });
                _spawnDone = true;
                int n = 0;
                bool inRace = false;
                try { n = GameManager.racers == null ? 0 : GameManager.racers.Length; } catch { }
                try { inRace = GameManager.inRace; } catch { }
                Plugin.Log.LogInfo("OnStartCircuit ok: racers=" + n + " inRace=" + inRace);
                return null;
            }
            catch (System.Exception e)
            {
                var inner = e.InnerException ?? e;
                Plugin.Log.LogError("OnStartCircuit threw: " + inner.GetType().Name + ": " + inner.Message
                                    + "\n" + (inner.StackTrace ?? ""));
            }
        }
        else return "GameManager.OnStartCircuit not found";

        // Fallback: spawn through the netcode's own wrapper, then publish the
        // racer array (StartSequence reads the static one) and run the countdown.
        var sp = HarmonyLib.AccessTools.Method(typeof(GameManager), "SpawnPlayers");
        if (sp != null && pl != null && pl.Length > 0)
        {
            var args = new object[] { points, pl, null, settings };
            Plugin.Log.LogInfo("SpawnPlayers(points=" + points.Length + ", players=" + pl.Length + ")");
            try
            {
                sp.Invoke(null, args);
                var spawned = args[2] as PlayerRacer[];
                Plugin.Log.LogInfo("SpawnPlayers -> racers=" + (spawned == null ? "null" : spawned.Length.ToString()));
                if (spawned != null && spawned.Length > 0)
                {
                    PublishRacers(spawned);
                    _spawnDone = true;
                    double ts = 0;
                    try { ts = Mirror.NetworkTime.time; } catch { }
                    GameManager.StartSequence(spawned, ts);
                    Plugin.Log.LogInfo("StartSequence(racers=" + spawned.Length + ", ts=" + ts + ") done");
                    return null;
                }
            }
            catch (System.Exception e)
            {
                var inner = e.InnerException ?? e;
                Plugin.Log.LogError("SpawnPlayers threw: " + inner.GetType().Name + ": " + inner.Message
                                    + "\n" + (inner.StackTrace ?? ""));
            }
        }

        return "no racer could be spawned";
    }

    private static bool _spawnDone;

    /// <summary>True once racers exist, so the start path is never run twice.</summary>
    internal static bool SpawnDone { get { return _spawnDone; } }

    /// <summary>
    /// StartSequence(racers, ts) reads GameManager's static racer array, which
    /// OnStartCircuit normally assigns; the fallback spawn path has to do it.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PublishRacers(PlayerRacer[] racers)
    {
        try
        {
            var f = HarmonyLib.AccessTools.Field(typeof(GameManager), "_racers");
            if (f != null) { f.SetValue(null, racers); Plugin.Log.LogInfo("published " + racers.Length + " racer(s) to GameManager.racers"); }
            else Plugin.Log.LogWarning("GameManager._racers field not found");
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("could not publish racers: " + e.Message); }
    }

    /// <summary>
    /// Give the local human player an input device.
    ///
    /// The game hands each local player an instance of its own input prefab
    /// ("Playerracing" + index) from the character-select screen; that object
    /// carries a PlayerInput (Unity Input System) plus the PlayerRacingController
    /// that reads its actions. Our lobby replaces that screen, so the prefab was
    /// never instantiated: the car spawned with player.input == null and no key
    /// could reach it. Instantiate the prefab here, pair it with the keyboard and
    /// hand it to the player.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepLocalInput()
    {
        var all = Resources.FindObjectsOfTypeAll<UnityEngine.InputSystem.PlayerInput>();
        if (all == null || all.Length == 0) return "no PlayerInput prefab loaded";

        // the prefab asset is the one that belongs to no scene
        UnityEngine.InputSystem.PlayerInput prefab = null;
        for (int i = 0; i < all.Length; i++)
        {
            var p = all[i];
            if (p == null) continue;
            string sc = "";
            try { sc = p.gameObject.scene.name; } catch { }
            if (string.IsNullOrEmpty(sc)) { prefab = p; break; }
            if (prefab == null) prefab = p;
        }
        if (prefab == null) return "no usable PlayerInput";

        var humans = GameManager.players;
        if (humans == null || humans.Length == 0) return "no human players registered";

        // reuse the instance from an earlier start instead of stacking a second one
        for (int i = 0; i < all.Length; i++)
        {
            var existing = all[i];
            if (existing == null) continue;
            string en = "";
            try { en = existing.gameObject.name; } catch { }
            if (en == "Playerracing0") { Plugin.Log.LogInfo("local input: reusing the existing " + en); return null; }
        }

        var kb = UnityEngine.InputSystem.Keyboard.current;
        UnityEngine.InputSystem.PlayerInput pi = null;
        try
        {
            pi = UnityEngine.InputSystem.PlayerInput.Instantiate(prefab.gameObject, 0, "KeyboardControls", -1, kb);
        }
        catch (System.Exception e)
        {
            var inner = e.InnerException ?? e;
            Plugin.Log.LogWarning("PlayerInput.Instantiate failed: " + inner.Message + " - falling back to a clone");
        }
        if (pi == null)
        {
            var go = UnityEngine.Object.Instantiate(prefab.gameObject);
            pi = go.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        }
        if (pi == null) return "could not create a PlayerInput";

        try { pi.gameObject.name = "Playerracing0"; } catch { }
        try { UnityEngine.Object.DontDestroyOnLoad(pi.gameObject); } catch { }
        try
        {
            // "Custom" is one of the asset's schemes but carries the rebindable
            // bindings, which are empty until the player sets them; the keyboard
            // scheme is the one the game drives with
            if (kb != null && pi.currentControlScheme != "KeyboardControls")
                pi.SwitchCurrentControlScheme("KeyboardControls", kb);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("control scheme: " + (e.InnerException ?? e).Message); }

        int bound = 0;
        for (int i = 0; i < humans.Length; i++)
        {
            var h = humans[i];
            if (h == null) continue;
            try
            {
                h.SetInput(pi);
                bound++;
                var hpi = pi.gameObject.GetComponent<HumanPlayerInput>();
                if (hpi != null) hpi.Assign(h);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("SetInput(" + i + ") failed: " + (e.InnerException ?? e).Message); }
        }
        Plugin.Log.LogInfo("local input: " + pi.gameObject.name + " playerIndex=" + pi.playerIndex
                           + " scheme='" + (pi.currentControlScheme ?? "-") + "' bound=" + bound
                           + " actions=" + (pi.actions == null ? "NULL" : pi.actions.name));
        return bound > 0 ? null : "no player accepted the input";
    }

    /// <summary>
    /// What the local player's input looks like once the race is running. The
    /// game hands each human a PlayerInput (Unity Input System) created by the
    /// character-select flow, and the racing controller reads its actions; when
    /// the player has none, the car is spawned but nothing can drive it.
    /// </summary>

    private static string SceneOf(UnityEngine.GameObject go)
    {
        try { return go.scene.name; } catch { return "?"; }
    }

    private static string Path(UnityEngine.GameObject go)
    {
        try
        {
            string p = go.name;
            var t = go.transform.parent;
            int guard = 0;
            while (t != null && guard++ < 8) { p = t.name + "/" + p; t = t.parent; }
            return p;
        }
        catch { return "?"; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string InputProbe()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var pl = GameManager.players;
            sb.Append("players=").Append(pl == null ? "null" : pl.Length.ToString());
            if (pl != null && pl.Length > 0 && pl[0] != null)
            {
                var inp = pl[0].input;
                sb.Append(" player0.input=").Append(inp == null ? "NULL" : ("ok(" + inp.name + ")"));
            }
        }
        catch (System.Exception e) { sb.Append(" players err:").Append(e.GetType().Name).Append(':').Append(e.Message); }

        try
        {
            var all = Resources.FindObjectsOfTypeAll<UnityEngine.InputSystem.PlayerInput>();
            sb.Append(" PlayerInput(all)=").Append(all == null ? "0" : all.Length.ToString());
            if (all != null)
            {
                for (int i = 0; i < all.Length && i < 5; i++)
                {
                    var pi = all[i];
                    if (pi == null) continue;
                    string an = "?";
                    try { an = pi.actions == null ? "noactions" : pi.actions.name; } catch { }
                    sb.Append(" [").Append(pi.name).Append('/').Append(an)
                      .Append("/active=").Append(pi.gameObject.activeInHierarchy).Append(']');
                }
            }
        }
        catch (System.Exception e) { sb.Append(" PlayerInput err:").Append(e.GetType().Name); }

        try
        {
            var ctrls = UnityEngine.Object.FindObjectsOfType<PlayerRacingController>();
            sb.Append(" PlayerRacingController(active)=").Append(ctrls == null ? "0" : ctrls.Length.ToString());
            var allc = Resources.FindObjectsOfTypeAll<PlayerRacingController>();
            sb.Append(" (all)=").Append(allc == null ? "0" : allc.Length.ToString());
            if (allc != null) foreach (var c in allc)
                sb.Append(" [").Append(c == null ? "null" : (c.name + "@" + SceneOf(c.gameObject) + " act=" + c.gameObject.activeInHierarchy)).Append(']');
        }
        catch (System.Exception e) { sb.Append(" ctrl err:").Append(e.GetType().Name).Append(':').Append(e.Message); }

        try
        {
            var hp = UnityEngine.Object.FindObjectsOfType<HumanPlayerInput>();
            sb.Append(" HumanPlayerInput(active)=").Append(hp == null ? "0" : hp.Length.ToString());
        }
        catch (System.Exception e) { sb.Append(" hpinput err:").Append(e.GetType().Name); }

        // what is on that PlayerInput object, and where does it live
        try
        {
            var all = Resources.FindObjectsOfTypeAll<UnityEngine.InputSystem.PlayerInput>();
            if (all != null)
            {
                foreach (var pi in all)
                {
                    if (pi == null) continue;
                    var go = pi.gameObject;
                    sb.Append("\n   obj '").Append(Path(go)).Append("' scene=").Append(SceneOf(go))
                      .Append(" actSelf=").Append(go.activeSelf).Append(" actHier=").Append(go.activeInHierarchy)
                      .Append(" scheme='").Append(pi.currentControlScheme ?? "-").Append("' def='").Append(pi.defaultControlScheme ?? "-")
                      .Append(" idx=").Append(pi.playerIndex).Append(" neverSwitch=").Append(pi.neverAutoSwitchControlSchemes);
                    var comps = go.GetComponents<Component>();
                    sb.Append(" comps=[");
                    if (comps != null) for (int i = 0; i < comps.Length; i++)
                        sb.Append(i > 0 ? "," : "").Append(comps[i] == null ? "null" : comps[i].GetType().Name);
                    sb.Append(']');
                }
            }
        }
        catch (System.Exception e) { sb.Append(" pi detail err:").Append(e.GetType().Name); }

        return sb.ToString();
    }

    /// <summary>
    /// Late retry: the same input prefab instance, but created while the circuit
    /// is already loaded. A controller that binds to its racer when it starts up
    /// would find nobody if the object was made back in the menu.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepLocalInputLate()
    {
        var ctrls = Resources.FindObjectsOfTypeAll<PlayerRacingController>();
        bool unbound = true;
        if (ctrls != null)
        {
            foreach (var c in ctrls)
            {
                if (c == null) continue;
                if (c._racer != null && c._character != null) unbound = false;
            }
        }
        if (!unbound) return null;                 // already wired

        Plugin.Log.LogInfo("no bound racing controller on the circuit - rebuilding the input in place");
        return StepLocalInput();
    }

    /// <summary>
    /// The value the game is being given for one action. The interop surface has
    /// no non-generic reader, so the generic ReadValue is closed over float by
    /// reflection. A control that reads non-zero while nothing is pressed is what
    /// drives a car on its own.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ReadActionFloat(UnityEngine.InputSystem.InputAction a)
    {
        try
        {
            foreach (var m in a.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (m.Name != "ReadValue" || !m.IsGenericMethodDefinition) continue;
                if (m.GetParameters().Length != 0 || m.GetGenericArguments().Length != 1) continue;
                var closed = m.MakeGenericMethod(typeof(float));
                var v = closed.Invoke(a, null);
                if (v is float f) return f.ToString("0.00");
                return v == null ? "null" : v.ToString();
            }
            return "no-reader";
        }
        catch (System.Exception e) { return "err:" + (e.InnerException ?? e).GetType().Name; }
    }

    /// <summary>
    /// The binding the driving controller ended up with, plus what the input
    /// actions are reading right now. A car that does not move can be an unbound
    /// controller (no racer/character) or actions that never fire (wrong scheme).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string DriveProbe()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var ctrls = Resources.FindObjectsOfTypeAll<PlayerRacingController>();
            sb.Append("ctrls=").Append(ctrls == null ? "0" : ctrls.Length.ToString());
            if (ctrls != null) foreach (var c in ctrls)
            {
                if (c == null) continue;
                try
                {
                    var inp = c.input;
                    sb.Append("\n   ").Append(c.gameObject.name)
                      .Append(" humanindex=").Append(c.humanindex)
                      .Append(" input=").Append(inp == null ? "NULL" : inp.name)
                      .Append(" racer=").Append(c._racer == null ? "NULL" : c._racer.name)
                      .Append(" character=").Append(c._character == null ? "NULL" : c._character.name)
                      .Append(" enabled=").Append(c.enabled).Append(" active=").Append(c.gameObject.activeInHierarchy);
                }
                catch (System.Exception e) { sb.Append("\n   ").Append(c.gameObject.name).Append(" unbound(").Append(e.GetType().Name).Append(')'); }
            }
        }
        catch (System.Exception e) { sb.Append(" ctrl err:").Append(e.GetType().Name).Append(':').Append(e.Message); }

        try
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            sb.Append("\n   keyboard=").Append(kb == null ? "NULL" : kb.displayName);
            try
            {
                sb.Append(" legacy(Up=").Append(UnityEngine.Input.GetKey(UnityEngine.KeyCode.UpArrow))
                  .Append(",W=").Append(UnityEngine.Input.GetKey(UnityEngine.KeyCode.W)).Append(')');
                if (kb != null) sb.Append(" isUp=").Append(kb.upArrowKey.isPressed);
            }
            catch (System.Exception e) { sb.Append(" key err:").Append(e.GetType().Name); }
            var pl = GameManager.players;
            sb.Append(" player0.input=").Append(pl == null || pl.Length == 0 || pl[0] == null || pl[0].input == null ? "NULL" : pl[0].input.name);
            var pi = (pl != null && pl.Length > 0 && pl[0] != null) ? pl[0].input : null;
            if (pi != null)
            {
                sb.Append(" scheme='").Append(pi.currentControlScheme ?? "-").Append("'");
                try
                {
                    var asset = pi.actions;
                    if (asset != null)
                    {
                        var sch = asset.controlSchemes;
                        sb.Append(" schemes=").Append(sch.Count).Append('[');
                        for (int k = 0; k < sch.Count && k < 6; k++) sb.Append(k > 0 ? "," : "").Append(sch[k].name);
                        sb.Append(']');
                    }
                }
                catch (System.Exception e) { sb.Append(" schemes err:").Append(e.GetType().Name); }
                var map = pi.currentActionMap;
                sb.Append(" map=").Append(map == null ? "NULL" : (map.name + "/enabled=" + map.enabled));
                if (map != null)
                {
                    var acts = map.actions;
                    sb.Append(" actions=").Append(acts == null ? "0" : acts.Count.ToString()).Append('[');
                    for (int i = 0; i < (acts == null ? 0 : acts.Count) && i < 12; i++)
                    {
                        var a = acts[i];
                        sb.Append(i > 0 ? ", " : "").Append(a.name).Append('=')
                          .Append(a.enabled ? ReadActionFloat(a) : "off");
                    }
                    sb.Append(']');
                }
            }
        }
        catch (System.Exception e) { sb.Append(" action err:").Append(e.GetType().Name).Append(':').Append(e.Message); }
        return sb.ToString();
    }

    /// <summary>
    /// Motion of the local racer. A car that tumbles and then disappears shows up
    /// here as a spinning euler angle with a falling position, plus the game's own
    /// falling/ended flags; two racing controllers on one human index would mean
    /// something is driving the same car twice.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string MotionProbe()
    {
        try
        {
            var r = GameManager.racers;
            if (r == null || r.Length == 0 || r[0] == null) return "no racer (racers=" + (r == null ? "null" : r.Length.ToString()) + ")";
            var racer = r[0];
            var t = racer.transform;
            var p = t.position;
            var e = t.eulerAngles;
            var sb = new System.Text.StringBuilder();
            sb.Append("racer0 pos=(").Append(p.x.ToString("0.0")).Append(", ").Append(p.y.ToString("0.0")).Append(", ").Append(p.z.ToString("0.0")).Append(')')
              .Append(" euler=(").Append(e.x.ToString("0")).Append(", ").Append(e.y.ToString("0")).Append(", ").Append(e.z.ToString("0")).Append(')')
              .Append(" racers=").Append(r.Length)
              .Append(" t=").Append(GameManager.timeSinceRaceStarted.ToString("0.0"));
            try { sb.Append(" falling=").Append(racer._falling).Append(" ended=").Append(racer._endedRace); } catch { }
            try { sb.Append(" lap=").Append(racer._curLap); } catch { }
            try
            {
                var ctrls = Resources.FindObjectsOfTypeAll<PlayerRacingController>();
                int bound = 0;
                if (ctrls != null) foreach (var c in ctrls)
                {
                    if (c == null) continue;
                    try { if (c._racer != null) bound++; } catch { }
                }
                sb.Append(" controllers=").Append(bound);
            }
            catch { }
            try { sb.Append(" inRace=").Append(GameManager.inRace); } catch { }
            try
            {
                var vc = racer.vc;
                if (vc != null)
                    sb.Append(" grounded=").Append(Flag(vc, "_isGrounded"))
                      .Append(" validGround=").Append(Flag(vc, "_validGround"))
                      .Append(" speed=").Append(Fnum(vc, "_currentSpeed"))
                      .Append(" wishTurn=").Append(Fnum(vc, "_wishTurn"));
            }
            catch { }
            // a second, network-driven copy of the same kart would fight the local
            // one over the transform and can be despawned under us
            try { sb.Append(" netRacer=").Append(racer.GetComponent<FRNetworkRacer>() != null); } catch { }
            try { sb.Append(" netIdentity=").Append(racer.GetComponent<Mirror.NetworkIdentity>() != null); } catch { }
            try
            {
                var objs = UnityEngine.Object.FindObjectsOfType<PlayerRacer>();
                sb.Append(" racerObjects=").Append(objs == null ? 0 : objs.Length);
                if (objs != null && objs.Length > 1)
                {
                    sb.Append('[');
                    for (int i = 0; i < objs.Length && i < 4; i++)
                    {
                        var q = objs[i] == null ? null : objs[i].transform;
                        if (q == null) { sb.Append("null,"); continue; }
                        var v = q.position;
                        sb.Append(i > 0 ? ", " : "").Append(objs[i].name).Append('@').Append(v.x.ToString("0")).Append('/').Append(v.z.ToString("0"));
                    }
                    sb.Append(']');
                }
            }
            catch { }
            return sb.ToString();
        }
        catch (System.Exception e2) { return "motion err:" + e2.GetType().Name + ":" + e2.Message; }
    }

    /// <summary>Where the local racer actually is, so a key press can be measured.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string RacerPose()
    {
        try
        {
            var r = GameManager.racers;
            if (r == null || r.Length == 0 || r[0] == null) return "no racer";
            var t = r[0].transform;
            var p = t.position;
            return "racer0 pos=(" + p.x.ToString("0.00") + ", " + p.y.ToString("0.00") + ", " + p.z.ToString("0.00") + ")"
               + " timeSinceStart=" + GameManager.timeSinceRaceStarted.ToString("0.0") + "s";
        }
        catch (System.Exception e) { return "pose err:" + e.GetType().Name; }
    }

    /// <summary>
    /// The countdown the game runs for a race. In this build StartSequence only
    /// reaches it through a network client singleton chain that is not built, so
    /// it throws before the cars are ever released - and because OnStartCircuit
    /// calls it before raising the race flag, that one throw leaves the level
    /// loaded with frozen cars and inRace false. The pieces that actually matter
    /// are the two the countdown ends with: the race timestamps and UnFreeze.
    /// </summary>
    /// <summary>
    /// The countdown coroutine. Matched by name and shape because the interop
    /// parameter types (Il2CppReferenceArray) do not match the metadata ones.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static System.Reflection.MethodInfo FindInitSequence()
    {
        try
        {
            foreach (var m in typeof(StartingSequence).GetMethods(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                         | System.Reflection.BindingFlags.Instance))
            {
                if (m.Name != "InitSequence") continue;
                if (m.GetParameters().Length == 4) return m;
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("InitSequence lookup: " + e.Message); }
        return null;
    }

    /// <summary>
    /// The countdown component. This build leaves parts of the race setup parked
    /// on inactive objects (the netcode root had to be woken the same way), so a
    /// plain FindObjectOfType can miss it: search everything loaded and switch the
    /// object and its parents on.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartingSequence FindStartingSequence()
    {
        var seq = UnityEngine.Object.FindObjectOfType<StartingSequence>();
        if (seq != null) return seq;

        var all = Resources.FindObjectsOfTypeAll<StartingSequence>();
        if (all == null || all.Length == 0)
        {
            Plugin.Log.LogWarning("countdown: StartingSequence not loaded at all");
            return null;
        }
        StartingSequence best = null;
        foreach (var s in all)
        {
            if (s == null) continue;
            var go = s.gameObject;
            string sc = "";
            try { sc = go.scene.name; } catch { }
            Plugin.Log.LogInfo("countdown: StartingSequence '" + Path(go) + "' scene='" + sc
                               + "' activeSelf=" + go.activeSelf + " activeInHierarchy=" + go.activeInHierarchy);
            if (string.IsNullOrEmpty(sc)) continue;                 // prefab asset, keep looking
            if (best == null) best = s;
            if (go.activeInHierarchy) { best = s; break; }
        }
        if (best == null) return null;
        try
        {
            var chain = new System.Collections.Generic.List<UnityEngine.Transform>();
            var t = best.transform;
            while (t != null) { chain.Add(t); t = t.parent; }
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                try { if (!chain[i].gameObject.activeSelf) chain[i].gameObject.SetActive(true); } catch { }
            }
            Plugin.Log.LogInfo("countdown: activated '" + Path(best.gameObject) + "' -> activeInHierarchy=" + best.gameObject.activeInHierarchy);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("countdown: could not activate: " + e.Message); }
        return best;
    }

    /// <summary>How long the cars are held on the line before they are released.</summary>
    internal const float CountdownSeconds = 3f;

    private static bool _pending;
    private static float _pendingLeft;
    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<PlayerRacer> _pendingRacers;
    private static double _pendingTimestamp;

    /// <summary>
    /// Runs the countdown to its end and releases the cars with the game's own
    /// release call, which also sets the race timestamps.
    /// </summary>
    internal static void TickCountdown(float dt)
    {
        if (!_pending) return;
        _pendingLeft -= dt;
        if (_pendingLeft > 0f) return;
        _pending = false;
        try
        {
            GameManager.UnFreezeRacers(_pendingRacers, _pendingTimestamp);
            Plugin.Log.LogInfo("countdown: go - racers released");
        }
        catch (System.Exception e)
        {
            var inner = e.InnerException ?? e;
            Plugin.Log.LogError("countdown: release failed: " + inner.GetType().Name + ": " + inner.Message);
        }
    }

    /// <summary>
    /// The local player's HUD and camera. Both are per-player objects created by
    /// the same spawn path, and the racing controller steers relative to its
    /// camera - an unbound camera is both a missing HUD and a kart that can spin
    /// on its own.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string HudProbe()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var hud = GameManager.GetHUD(0);
            sb.Append("GetHUD(0)=").Append(hud == null ? "NULL" : (hud.name + " active=" + hud.gameObject.activeInHierarchy));
            if (hud != null)
            {
                try { var c = hud.canvas; sb.Append(" canvas=").Append(c == null ? "NULL" : ("enabled=" + c.enabled + " active=" + c.gameObject.activeInHierarchy)); } catch { }
            }
        }
        catch (System.Exception e) { sb.Append(" hud err:").Append(e.GetType().Name); }
        try
        {
            var cam = GameManager.GetPlayerCamera(0);
            sb.Append(" GetPlayerCamera(0)=").Append(cam == null ? "NULL" : (cam.name + " active=" + cam.gameObject.activeInHierarchy));
        }
        catch (System.Exception e) { sb.Append(" cam err:").Append(e.GetType().Name); }
        try
        {
            var huds = UnityEngine.Object.FindObjectsOfType<PlayerHUD>();
            sb.Append(" huds=").Append(huds == null ? 0 : huds.Length);
        }
        catch { }
        try
        {
            var cams = UnityEngine.Object.FindObjectsOfType<PlayerCamera>();
            sb.Append(" cams=").Append(cams == null ? 0 : cams.Length);
            if (cams != null) foreach (var c in cams)
                if (c != null) sb.Append(" [").Append(c.name).Append(" active=").Append(c.gameObject.activeInHierarchy).Append(']');
        }
        catch { }
        try
        {
            var ctrls = Resources.FindObjectsOfTypeAll<PlayerRacingController>();
            if (ctrls != null) foreach (var c in ctrls)
            {
                if (c == null) continue;
                try { if (c._racer != null) sb.Append(" ctrl.plrCam=").Append(c._plrCam == null ? "NULL" : c._plrCam.name); } catch { }
            }
        }
        catch { }
        return sb.ToString();
    }

    /// <summary>
    /// Harness-only: pretend the local player crossed the line, so the game's own
    /// end-of-race chain runs (the container never drives, so it would never get
    /// there on its own). Gated behind a flag file.
    /// </summary>
    internal static bool ForceEndRequested
    {
        get
        {
            try { return System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.forceend")); }
            catch { return false; }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepForceRaceEnd()
    {
        try
        {
            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0) return "no racers";
            // mark the racer finished first: that is the state a player reaches by
            // crossing the line, and RaceEndedFor ignores a racer that has not ended
            try
            {
                var f = HarmonyLib.AccessTools.Property(typeof(PlayerRacer), "_endedRace")
                        ?? (System.Reflection.MemberInfo)HarmonyLib.AccessTools.Field(typeof(PlayerRacer), "_endedRace");
                if (f is System.Reflection.PropertyInfo pi && pi.CanWrite) { pi.SetValue(racers[0], true); Plugin.Log.LogInfo("forced race end: racer._endedRace = true (property)"); }
                else if (f is System.Reflection.FieldInfo fi) { fi.SetValue(racers[0], true); Plugin.Log.LogInfo("forced race end: racer._endedRace = true (field)"); }
                else Plugin.Log.LogWarning("forced race end: could not set racer._endedRace");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("forced race end: " + e.Message); }
            Plugin.Log.LogInfo("forced race end: calling RaceEndedFor(" + racers[0].name + ", 0)");
            GameManager.instance.RaceEndedFor(racers[0], 0);
            Plugin.Log.LogInfo("forced race end: returned");
            return null;
        }
        catch (System.Exception e)
        {
            var inner = e.InnerException ?? e;
            Plugin.Log.LogError("forced race end threw: " + inner.GetType().Name + ": " + inner.Message + "\n" + (inner.StackTrace ?? ""));
            return inner.GetType().Name + ": " + inner.Message;
        }
    }

    /// <summary>
    /// What the game itself puts on screen the moment a race ends. The build's own
    /// result table (CeremonyResults) fills its rows from the racer list, and with
    /// fewer racers than rows it fills nothing - so the player is left looking at
    /// an empty mask. Dumping the active buttons and labels says exactly which
    /// screen that is and whether a "Next" button is waiting for a click.
    /// </summary>
    internal static void EndScreenDump()
    {
        try
        {
            var sb = new System.Text.StringBuilder("end screen:");
            string scene = "";
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            sb.Append(" scene=").Append(scene);
            int n = 0;
            var buttons = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>(true);
            if (buttons != null)
                foreach (var b in buttons)
                {
                    if (b == null || b.gameObject == null || !b.gameObject.activeInHierarchy) continue;
                    string label = "";
                    try { label = CollectText(b.gameObject); } catch { }
                    if (label != null && label.Length > 32) label = label.Substring(0, 32);
                    sb.Append("\n   btn '").Append(b.gameObject.name).Append("' '").Append(label).Append('\'');
                    if (++n > 24) break;
                }
            var texts = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>(true);
            if (texts != null)
                foreach (var t in texts)
                {
                    if (t == null || t.gameObject == null || !t.gameObject.activeInHierarchy) continue;
                    string s = null;
                    try { s = t.text; } catch { }
                    if (string.IsNullOrEmpty(s)) continue;
                    s = s.Replace('\n', ' ');
                    if (s.Length > 40) s = s.Substring(0, 40);
                    sb.Append("\n   txt '").Append(s).Append('\'');
                    if (++n > 60) break;
                }
            Plugin.Log.LogWarning(sb.ToString());
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("end screen dump: " + e.GetType().Name); }
    }

    /// <summary>
    /// The game's own "race is over, go to the ceremony" path. Unused unless
    /// FumoMP.finish exists, because it is the call that is supposed to produce the
    /// ranking screen and it has never been exercised from our session.
    /// </summary>
    internal static bool FinishGameRequested
    {
        get
        {
            try { return System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.finish")); }
            catch { return false; }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepFinishGame()
    {
        try
        {
            Plugin.Log.LogWarning("end: calling GameManager.FinishGame()");
            GameManager.FinishGame();
            Plugin.Log.LogWarning("end: FinishGame returned");
            return null;
        }
        catch (System.Exception e)
        {
            var inner = e.InnerException ?? e;
            Plugin.Log.LogWarning("end: FinishGame threw " + inner.GetType().Name + ": " + inner.Message);
            return inner.GetType().Name;
        }
    }

    /// <summary>
    /// The ranking, as plain lines. Built from the racer list rather than from the
    /// game's result table, so it is correct even when that table never fills.
    /// </summary>
    internal static string ResultsBody()
    {
        var rows = new System.Collections.Generic.List<string[]>();
        var sortKey = new System.Collections.Generic.List<double>();

        try
        {
            var racers = GameManager.racers;
            var names = NickNames();
            if (racers != null)
                for (int i = 0; i < racers.Length; i++)
                {
                    var r = racers[i];
                    if (r == null) continue;
                    int pidx = -1;
                    try { if (r.plr != null) pidx = GameManager.GetPlayerIndex(r.plr); } catch { }
                    string who = pidx == 0 ? MpFlow.Nickname
                               : (pidx > 0 && pidx < names.Count ? names[pidx] : "Player" + (pidx + 1));
                    if (string.IsNullOrEmpty(who)) who = "Local player";
                    int lap = 0; try { lap = r._curLap; } catch { }
                    double secs = -1; try { secs = r.GetTotalTime().TotalSeconds; } catch { }
                    bool ended = false; try { ended = r._endedRace; } catch { }
                    AddRow(rows, sortKey, who, lap, secs, ended, true);
                }

            // the other machines, as the kart channel sees them
            if (MpNet.Running)
                foreach (var p in MpNet.Peers)
                {
                    var k = MpNet.Kart(p.Slot);
                    string who = p.Nick;
                    if (string.IsNullOrEmpty(who) && k != null) who = k.Nick;
                    if (string.IsNullOrEmpty(who)) who = "Player" + p.Slot;
                    who += p.RttMs > 0.5f ? " (" + (int)p.RttMs + "ms)" : "";
                    AddRow(rows, sortKey, who, k == null ? 0 : k.Lap, k == null ? -1 : k.TotalTime,
                           k != null && k.Finished, false);
                }
        }
        catch (System.Exception e) { return "results failed: " + e.GetType().Name; }

        if (rows.Count == 0) return "<mspace=0.55em>no karts in this session</mspace>";

        // order: finished first, then the most laps, then the fastest
        var order = new System.Collections.Generic.List<int>();
        for (int i = 0; i < rows.Count; i++) order.Add(i);
        order.Sort((a, b) => sortKey[a].CompareTo(sortKey[b]));

        var sb = new System.Text.StringBuilder();
        int place = 0;
        foreach (int i in order)
        {
            var row = rows[i];
            sb.Append(Pad((place + 1) + ".", 4)).Append(Pad(row[0], 20)).Append(Pad("lap " + row[1], 8))
              .Append(Pad(row[2], 12)).Append(row[3]).Append('\n');
            place++;
        }
        return "<mspace=0.55em>" + sb.ToString().TrimEnd('\n') + "</mspace>";
    }

    /// <summary>name / lap / time / state, plus the sort key.</summary>
    private static void AddRow(System.Collections.Generic.List<string[]> rows,
                               System.Collections.Generic.List<double> keys,
                               string who, int lap, double secs, bool finished, bool local)
    {
        if (string.IsNullOrEmpty(who)) who = "?";
        if (who.Length > 19) who = who.Substring(0, 19);
        string time = secs >= 0.05 ? Fmt(secs) : "--:--.---";
        string state = finished ? (local ? "finished" : "finished (net)") : "racing";
        rows.Add(new[] { who, lap.ToString(), time, state });
        double key = (finished ? 0.0 : 1.0) * 1e12 - lap * 1e9 + (secs >= 0.05 ? secs : 1e6);
        keys.Add(key);
    }

    /// <summary>Sort key: finished racers first, then the most laps, then the fastest.</summary>
    private static double Rank(PlayerRacer[] racers, int i)
    {
        try
        {
            var r = racers[i];
            double laps = r._curLap;
            double secs = 1e9;
            try { var ts = r.GetTotalTime(); if (ts.TotalSeconds > 0.05) secs = ts.TotalSeconds; } catch { }
            double ended = r._endedRace ? 0.0 : 1.0;
            return ended * 1e12 - laps * 1e9 + secs;
        }
        catch { return 9e12; }
    }

    private static string Pad(string s, int width)
    {
        if (s == null) s = "";
        if (s.Length >= width) return s.Substring(0, width);
        return s + new string(' ', width - s.Length);
    }

    private static string Fmt(double totalSeconds)
    {
        try
        {
            int m = (int)(totalSeconds / 60.0);
            double rest = totalSeconds - m * 60.0;
            int s = (int)rest;
            int ms = (int)((rest - s) * 1000.0);
            return m + ":" + s.ToString("00") + "." + ms.ToString("000");
        }
        catch { return "--:--.---"; }
    }

    /// <summary>Nicknames in player-slot order, local player first.</summary>
    private static System.Collections.Generic.List<string> NickNames()
    {
        var list = new System.Collections.Generic.List<string>();
        try
        {
            var arr = UnityEngine.Object.FindObjectsOfType<FRNetworkPlayer>(true);
            if (arr != null)
                foreach (var p in arr)
                {
                    if (p == null) continue;
                    string n = null;
                    try { n = p.nickName; } catch { }
                    bool local = false;
                    try { local = (FRNetworkPlayer.localPlayer == p); } catch { }
                    if (local) list.Insert(0, string.IsNullOrEmpty(n) ? MpFlow.Nickname : n);
                    else list.Add(string.IsNullOrEmpty(n) ? "Player" + (list.Count + 1) : n);
                }
        }
        catch { }
        return list;
    }

    /// <summary>The end-of-race bookkeeping, so a stuck transition can be read off the log.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]    internal static string EndStateProbe()
    {
        var sb = new System.Text.StringBuilder();
        try { sb.Append("endedRace=").Append(GameManager.endedRace).Append(" inRace=").Append(GameManager.inRace); } catch { }
        try
        {
            var pl = GameManager.players;
            sb.Append(" players=").Append(pl == null ? "null" : pl.Length.ToString());
            if (pl != null) for (int i = 0; i < pl.Length; i++)
            {
                var p = pl[i];
                if (p == null) { sb.Append(" [").Append(i).Append("=null]"); continue; }
                string r = "no-racer";
                try { var ra = p.racer; r = ra == null ? "null" : (ra.name + " ended=" + ra._endedRace); } catch { }
                sb.Append(" [").Append(i).Append(" joined=").Append(p._joined).Append(' ').Append(r).Append(']');
            }
        }
        catch (System.Exception e) { sb.Append(" players err:").Append(e.GetType().Name); }
        try
        {
            var racers = GameManager.racers;
            sb.Append(" racers=").Append(racers == null ? "null" : racers.Length.ToString());
            if (racers != null) for (int i = 0; i < racers.Length; i++)
                if (racers[i] != null) sb.Append(" [").Append(i).Append(" ended=").Append(racers[i]._endedRace).Append(" lap=").Append(racers[i]._curLap).Append(']');
        }
        catch { }
        try { sb.Append(" nm=").Append(Mirror.NetworkManager.singleton == null ? "NULL" : Mirror.NetworkManager.singleton.mode.ToString()); } catch { }
        try { sb.Append(" scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name); } catch { }
        return sb.ToString();
    }

    /// <summary>
    /// A HUD canvas that renders through a camera draws nothing while that camera
    /// is unset, which looks exactly like "there is no HUD" even though the object
    /// is enabled. Point it at the local player's camera when it has none.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void EnsureHudCamera()
    {
        try
        {
            var hud = GameManager.GetHUD(0);
            if (hud == null) { Plugin.Log.LogInfo("hud: none yet"); return; }
            var canvas = hud.canvas;
            if (canvas == null) { Plugin.Log.LogInfo("hud: no canvas"); return; }

            var cam = GameManager.GetPlayerCamera(0);
            UnityEngine.Camera camComp = null;
            if (cam != null)
            {
                try { camComp = cam.GetComponent<UnityEngine.Camera>(); } catch { }
                if (camComp == null) { try { camComp = cam.GetComponentInChildren<UnityEngine.Camera>(true); } catch { } }
                if (camComp == null) { try { camComp = cam.GetComponentInChildren<UnityEngine.Camera>(); } catch { } }
            }
            var main = UnityEngine.Camera.main;
            Plugin.Log.LogInfo("hud: playerCamera='" + (cam == null ? "NULL" : cam.name) + "' cameraComp="
                               + (camComp == null ? "NULL" : camComp.name) + " mainCamera="
                               + (main == null ? "NULL" : main.name));
            if (camComp == null) return;

            string mode = canvas.renderMode.ToString();
            var current = canvas.worldCamera;
            if (canvas.renderMode == UnityEngine.RenderMode.ScreenSpaceOverlay) return;

            if (current == null)
            {
                canvas.worldCamera = camComp;
                Plugin.Log.LogInfo("hud: canvas had no camera (renderMode=" + mode + ") - bound to " + camComp.name);
                return;
            }

            // A ScreenSpaceCamera canvas draws relative to the camera it names. If
            // that is not the camera the race is being viewed through, every HUD
            // element lands somewhere off the visible picture - the HUD "exists"
            // (enabled canvas, live widgets) and is still invisible.
            bool sameObject = false;
            try { sameObject = current.gameObject == camComp.gameObject; } catch { }
            Plugin.Log.LogInfo("hud: renderMode=" + mode + " canvasCamera=" + current.name
                               + " playerCamera=" + camComp.name + " same=" + sameObject);
            if (!sameObject)
            {
                canvas.worldCamera = camComp;
                Plugin.Log.LogInfo("hud: canvas camera switched to " + camComp.name);
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("hud camera: " + (e.InnerException ?? e).Message); }
    }

    /// <summary>
    /// Per-frame telemetry over a whole race is off unless this flag file exists:
    /// it costs a little every sample and it buries the log.
    /// </summary>
    internal static bool DebugLogging
    {
        get
        {
            try { return System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.debug")); }
            catch { return false; }
        }
    }

    /// <summary>
    /// Leave the network session before the game quits. A Mirror host that was
    /// never fully brought up keeps shutdown waiting, which is what "quit does
    /// nothing" looks like.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ShutdownNetworkForQuit()
    {
        try
        {
            var nm = NetworkManager.singleton;
            Plugin.Log.LogInfo("quit: stopping the network session first (mode=" + (nm == null ? "none" : nm.mode.ToString()) + ")");
            if (nm != null)
            {
                try { nm.StopHost(); } catch { }
                try { nm.StopClient(); } catch { }
            }
            try { Mirror.NetworkServer.Shutdown(); } catch { }
            try { Mirror.NetworkClient.Shutdown(); } catch { }
            Plugin.Log.LogInfo("quit: network stopped");
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("quit: network shutdown failed: " + (e.InnerException ?? e).Message); }
    }

    // ------------------------------------------------------- model watchdog
    private sealed class WatchNode
    {
        public UnityEngine.Transform t;
        public string path;
        public float prevY;
        public float accum;
        public float window;
        public UnityEngine.Quaternion rest;
        public bool vanished;
        public UnityEngine.Vector3 prevPos;
        public float reportedAt;
    }

    private static readonly System.Collections.Generic.List<WatchNode> _watch = new System.Collections.Generic.List<WatchNode>();
    private static bool _watchBuilt;
    private static float _watchLogAt;

    /// <summary>
    /// Watches every transform under the local racer. The visible model of this game
    /// hangs several levels down (roots of Fumo / VehiclePivot / Character / ...),
    /// and a runaway rotation can sit on any of them - watching one node only was
    /// why an earlier build reported nothing while the model visibly spun.
    /// It also reports a node that goes inactive or drifts away from the kart, which
    /// is what "it spins and then disappears" looks like from the inside.
    /// </summary>
    internal static void ModelWatch(float dt)
    {
        try
        {
            bool inRace = false;
            try { inRace = GameManager.inRace; } catch { }
            var racers = GameManager.racers;
            if (!inRace || racers == null || racers.Length == 0 || racers[0] == null)
            {
                if (_watchBuilt) { _watch.Clear(); _watchBuilt = false; }
                return;
            }

            if (!_watchBuilt)
            {
                BuildWatch(racers[0].transform, "", 0, 14);
                _watchBuilt = true;
                Plugin.Log.LogInfo("model watch: tracking " + _watch.Count + " nodes under " + racers[0].name);
                return;
            }

            // harness self-test: spin one node so the watchdog can be verified
            try
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.spintest")))
                {
                    for (int i = 0; i < _watch.Count; i++)
                    {
                        var n = _watch[i];
                        if (n.t != null && n.path.EndsWith("/Character"))
                        { n.t.Rotate(0f, 400f * dt, 0f, UnityEngine.Space.Self); break; }
                    }
                }
            }
            catch { }

            var root = racers[0].transform;
            WatchNode worst = null;
            float worstRate = 0f;
            for (int i = 0; i < _watch.Count; i++)
            {
                var n = _watch[i];
                if (n.t == null) continue;

                // vanished?
                bool active = true;
                try { active = n.t.gameObject.activeInHierarchy; } catch { }
                if (!active && !n.vanished)
                {
                    n.vanished = true;
                    Plugin.Log.LogWarning("MODEL: node '" + n.path + "' went inactive"
                                          + "\n   vehicle: " + VehicleState());
                }
                else if (active && n.vanished)
                {
                    n.vanished = false;
                    Plugin.Log.LogInfo("MODEL: node '" + n.path + "' came back");
                }

                // flew away?
                try
                {
                    var wp = n.t.position;
                    float dist = UnityEngine.Vector3.Distance(wp, root.position);
                    if (dist > 60f && UnityEngine.Time.time - n.reportedAt > 5f)
                    {
                        n.reportedAt = UnityEngine.Time.time;
                        Plugin.Log.LogWarning("MODEL: node '" + n.path + "' is " + dist.ToString("0")
                                              + " units from the kart (it has come loose)"
                                              + "\n   vehicle: " + VehicleState());
                    }
                    n.prevPos = wp;
                }
                catch { }

                // rotation rate, measured in WORLD space so a spinning parent counts too
                float y = n.t.eulerAngles.y;
                float d = UnityEngine.Mathf.DeltaAngle(n.prevY, y);
                n.prevY = y;
                n.accum += UnityEngine.Mathf.Abs(d);
                n.window += dt;
                if (n.window >= 1f)
                {
                    float rate = n.accum / n.window;
                    n.accum = 0f; n.window = 0f;
                    if (rate > worstRate) { worstRate = rate; worst = n; }
                }
            }

            // wheel torque bones rotate with the kart's speed - that is normal
            bool wheel = worst != null && (worst.path.Contains("/Wheel") || worst.path.Contains("Torque"));
            float limit = wheel ? 4000f : 120f;
            if (worst != null && worstRate >= limit && UnityEngine.Time.time - _watchLogAt > 2f)
            {
                _watchLogAt = UnityEngine.Time.time;
                Plugin.Log.LogWarning("SPIN: node '" + worst.path + "' turning " + worstRate.ToString("0") + " deg/s"
                                      + "\n   local was " + Fmt(worst.rest.eulerAngles) + " now " + Fmt(worst.t.localEulerAngles)
                                      + "\n   controller: " + ControllerState()
                                      + "\n   vehicle: " + VehicleState()
                                      + "\n   input: " + InputValues()
                                      + "\n   bindings:" + ActionBindings()
                                      + "\n   anim: " + AnimState(worst.t));
                // writing the node back only ever masked the symptom, and it fights
                // whatever is really turning it - opt in with FumoMP.pin
                if (MpSpin.WantPin)
                {
                    PinNode(worst);
                    _pinned = worst;
                    _pinUntil = UnityEngine.Time.time + 10f;
                }
            }

            // keep the offending node still for a while
            if (_pinned != null && UnityEngine.Time.time < _pinUntil) PinNode(_pinned);
            else if (_pinned != null && UnityEngine.Time.time >= _pinUntil)
            {
                Plugin.Log.LogInfo("SPIN: releasing the pinned node '" + _pinned.path + "'");
                _pinned = null;
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("model watch: " + e.Message); }
    }

    private static WatchNode _pinned;
    private static float _pinUntil;

    /// <summary>What the model's animator is playing - a spin can be skeletal.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string AnimState(UnityEngine.Transform t)
    {
        try
        {
            var an = t.GetComponentInChildren<UnityEngine.Animator>();
            if (an == null) return "no animator";
            string clip = "none";
            try
            {
                var info = an.GetCurrentAnimatorClipInfo(0);
                if (info != null && info.Length > 0 && info[0].clip != null) clip = info[0].clip.name;
            }
            catch { }
            string st = "?";
            try { st = an.GetCurrentAnimatorStateInfo(0).shortNameHash.ToString(); } catch { }
            return an.gameObject.name + " clip=" + clip + " stateHash=" + st + " speed=" + an.speed.ToString("0.00")
                   + " enabled=" + an.enabled + " ctrl=" + (an.runtimeAnimatorController == null ? "NULL" : an.runtimeAnimatorController.name);
        }
        catch (System.Exception e) { return "anim err:" + e.GetType().Name; }
    }

    /// <summary>
    /// Where the visible meshes hang. If the kart body sits under a wheel bone, the
    /// body turns with the wheel - a model that spins while the kart drives straight.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string MeshPaths()
    {
        try
        {
            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0 || racers[0] == null) return "no racer";
            var sb = new System.Text.StringBuilder("meshes under the racer:");
            int n = 0;
            CollectMeshes(racers[0].transform, "", sb, ref n, 0);
            return sb.ToString();
        }
        catch (System.Exception e) { return "mesh paths: " + e.GetType().Name; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectMeshes(UnityEngine.Transform t, string prefix, System.Text.StringBuilder sb, ref int n, int depth)
    {
        if (t == null || depth > 14 || n > 24) return;
        string path = prefix.Length == 0 ? t.name : prefix + "/" + t.name;
        bool mesh = false;
        try { mesh = t.GetComponent<UnityEngine.MeshRenderer>() != null || t.GetComponent<UnityEngine.SkinnedMeshRenderer>() != null; } catch { }
        if (mesh)
        {
            n++;
            sb.Append("\n   [").Append(n).Append("] ").Append(path);
        }
        for (int i = 0; i < t.childCount && i < 80; i++) CollectMeshes(t.GetChild(i), path, sb, ref n, depth + 1);
    }

    // ------------------------------------------------------------ animation watch
    private static UnityEngine.Animator _anim;
    private static string _animClip;
    private static float _animNextCheck;

    /// <summary>
    /// Reports every change of the character model's animation clip. A spin that is
    /// played by the skinned animator leaves every transform untouched, so the clip
    /// name (and its speed) is the only way to see it.
    /// </summary>
    internal static void AnimWatch()
    {
        try
        {
            if (UnityEngine.Time.realtimeSinceStartup < _animNextCheck) return;
            _animNextCheck = UnityEngine.Time.realtimeSinceStartup + 0.2f;

            bool inRace = false;
            try { inRace = GameManager.inRace; } catch { }
            if (!inRace) { _anim = null; _animClip = null; return; }

            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0 || racers[0] == null) return;
            if (_anim == null)
            {
                try { _anim = racers[0].GetComponentInChildren<UnityEngine.Animator>(true); } catch { }
                if (_anim == null) return;
            }

            string clip = "none";
            float speed = 0f;
            float norm = 0f;
            try
            {
                speed = _anim.speed;
                var info = _anim.GetCurrentAnimatorClipInfo(0);
                if (info != null && info.Length > 0 && info[0].clip != null) clip = info[0].clip.name;
                norm = _anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
            }
            catch { }
            if (clip == _animClip) return;
            _animClip = clip;
            Plugin.Log.LogInfo("ANIM: clip -> '" + clip + "' speed=" + speed.ToString("0.00")
                               + " t=" + norm.ToString("0.00")
                               + " state=" + VehicleState());
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("anim watch: " + e.Message); }
    }

    private static void PinNode(WatchNode n)
    {
        try { if (n != null && n.t != null) n.t.localRotation = n.rest; } catch { }
    }

    private static string Fmt(UnityEngine.Vector3 v) { return "(" + v.x.ToString("0") + "," + v.y.ToString("0") + "," + v.z.ToString("0") + ")"; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BuildWatch(UnityEngine.Transform t, string prefix, int depth, int maxDepth)
    {
        if (t == null || depth > maxDepth) return;
        // cosmetic particle nodes turn on and off all the time; they are not what
        // "the model spins" is about, so they only add noise
        string lower = t.name.ToLowerInvariant();
        if (lower.Contains("flash") || lower.Contains("powerfx") || lower.Contains("grassfx") ||
            lower.Contains("dust") || lower.Contains("smoke") || lower.Contains("spark") ||
            lower.Contains("trail") || lower == "sfx") return;
        var n = new WatchNode();
        n.t = t;
        n.path = prefix.Length == 0 ? t.name : prefix + "/" + t.name;
        n.prevY = t.eulerAngles.y;
        n.rest = t.localRotation;
        try
        {
            var an = t.GetComponent<UnityEngine.Animator>();
            if (an != null)
            {
                string clip = "none";
                try
                {
                    var info = an.GetCurrentAnimatorClipInfo(0);
                    if (info != null && info.Length > 0 && info[0].clip != null) clip = info[0].clip.name;
                }
                catch { }
                Plugin.Log.LogInfo("model watch: animator on '" + n.path + "' controller="
                                   + (an.runtimeAnimatorController == null ? "NULL" : an.runtimeAnimatorController.name)
                                   + " clip=" + clip + " speed=" + an.speed.ToString("0.00")
                                   + " enabled=" + an.enabled);
            }
        }
        catch { }
        _watch.Add(n);
        for (int i = 0; i < t.childCount && i < 80; i++) BuildWatch(t.GetChild(i), n.path, depth + 1, maxDepth);
    }

    // -------------------------------------------------- model visibility guard
    private static UnityEngine.Transform _modelRoot;
    private static float _lastHideLog;

    /// <summary>The node whose visibility decides whether the player sees a kart.</summary>
    private static UnityEngine.Transform ModelRoot()
    {
        try
        {
            if (_modelRoot != null) return _modelRoot;
            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0 || racers[0] == null) return null;
            var t = racers[0].transform;
            var c = t.Find("Character");
            if (c != null) { _modelRoot = c; return _modelRoot; }
            var vc = racers[0].vc;
            if (vc != null && vc._vehiclepivot != null) { _modelRoot = vc._vehiclepivot; return _modelRoot; }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// True when hiding this object would hide the player's kart. The game switches
    /// the whole character subtree off and on during a race (that is the model
    /// "disappearing"), so those calls are refused while a race is running.
    /// </summary>
    internal static bool IsModelHide(UnityEngine.GameObject go)
    {
        try
        {
            bool inRace = false;
            try { inRace = GameManager.inRace; } catch { }
            if (!inRace || go == null) return false;
            var root = ModelRoot();
            if (root == null) return false;
            var t = go.transform;
            if (t == root) return true;
            return root.IsChildOf(t);
        }
        catch { return false; }
    }

    internal static void LogModelHideBlocked(UnityEngine.GameObject go)
    {
        try
        {
            _modelRoot = null;                      // re-resolve next time
            if (UnityEngine.Time.time - _lastHideLog < 1f) return;
            _lastHideLog = UnityEngine.Time.time;
            Plugin.Log.LogWarning("MODEL: refused to hide '" + go.name + "' during the race"
                                  + "\n   vehicle: " + VehicleState()
                                  + "\n   input: " + InputValues());
        }
        catch { }
    }

    /// <summary>
    /// Every clickable UI element on screen with its label and position. Used to
    /// drive the game's own menus (for single-player comparison) without seeing the
    /// screen. Only active while the debug flag file exists.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string ScreenScan()
    {
        if (!DebugLogging) return null;
        try
        {
            var sb = new System.Text.StringBuilder("screen scan:");
            string scene = "";
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch { }
            sb.Append(" scene=").Append(scene);
            int n = 0;
            var buttons = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>(true);
            if (buttons != null)
            {
                foreach (var b in buttons)
                {
                    if (b == null) continue;
                    var go = b.gameObject;
                    if (go == null || !go.activeInHierarchy) continue;
                    string label = "";
                    try { label = CollectText(go); } catch { }
                    if (label != null && label.Length > 28) label = label.Substring(0, 28);
                    sb.Append("\n   '").Append(go.name).Append("' '").Append(label).Append("'");
                    try
                    {
                        var rt = go.GetComponent<UnityEngine.RectTransform>();
                        if (rt != null) sb.Append(ClickPoint(rt));
                    }
                    catch { }
                    n++;
                    if (n > 40) break;
                }
            }
            Plugin.Log.LogInfo(sb.ToString());
            return null;
        }
        catch (System.Exception e) { return "screen scan: " + e.GetType().Name; }
    }

    /// <summary>Screen position of a UI element as xdotool would need it (y from the top).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string ClickPoint(UnityEngine.RectTransform rt)
    {
        try
        {
            if (rt == null) return "";
            UnityEngine.Vector2 sp;
            UnityEngine.Canvas c = null;
            try { c = rt.GetComponentInParent<UnityEngine.Canvas>(); } catch { }
            if (c != null && c.renderMode == UnityEngine.RenderMode.ScreenSpaceOverlay)
            {
                // overlay canvases live in screen space already: no camera projection
                sp = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, rt.position);
            }
            else
            {
                UnityEngine.Camera cam = c != null ? c.worldCamera : null;
                if (cam == null) cam = UnityEngine.Camera.main;
                sp = UnityEngine.RectTransformUtility.WorldToScreenPoint(cam, rt.position);
            }
            int x = UnityEngine.Mathf.RoundToInt(sp.x);
            int y = UnityEngine.Screen.height - UnityEngine.Mathf.RoundToInt(sp.y);
            return " click=" + x + "," + y;
        }
        catch { return ""; }
    }

    /// <summary>
    /// Harness-only: start the game's own single-player race so it can be compared
    /// with the multiplayer one (mouse input cannot drive the game's menus here).
    /// </summary>
    internal static bool SpRequested
    {
        get
        {
            try { return System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.sp")); }
            catch { return false; }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepStartSinglePlayer()
    {
        try
        {
            Plugin.Log.LogInfo("SP: calling MenuEvents.GoQuickRace()");
            MenuEvents.GoQuickRace();
            Plugin.Log.LogInfo("SP: GoQuickRace returned");
            return null;
        }
        catch (System.Exception e)
        {
            var inner = e.InnerException ?? e;
            Plugin.Log.LogError("SP: GoQuickRace threw " + inner.GetType().Name + ": " + inner.Message);
            return inner.GetType().Name;
        }
    }

    /// <summary>Which resource name the game uses for its game-mode prefabs.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepProbeModeAssets()
    {
        var sb = new System.Text.StringBuilder();
        string[] names = { "QuickRace", "quickrace", "Quick race", "GameModes/QuickRace", "gamemodes/quickrace",
                           "TimeTrials", "GrandPrix", "CustomRace", "GameMode", "QuickRaceMode" };
        foreach (var n in names)
        {
            try
            {
                var o = Resources.Load<GameMode>(n);
                if (o != null) sb.Append(" [").Append(n).Append(" -> ").Append(o.GetType().Name).Append(']');
            }
            catch { }
        }
        try
        {
            var all = Resources.FindObjectsOfTypeAll<GameMode>();
            sb.Append(" GameMode objects=").Append(all == null ? 0 : all.Length);
            if (all != null) foreach (var g in all)
                if (g != null) sb.Append(" [").Append(g.name).Append('/').Append(g.GetType().Name).Append(']');
        }
        catch { }
        Plugin.Log.LogInfo("mode assets:" + (sb.Length == 0 ? " none found" : sb.ToString()));
        return null;
    }

    /// <summary>The mode prefabs the menu assigns (MenuEvents statics).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static GameMode ReadMenuMode()
    {
        try
        {
            var q = MenuEvents._quickrace;
            if (q != null) { Plugin.Log.LogInfo("game mode: MenuEvents._quickrace = '" + q.name + "'"); return q; }
            Plugin.Log.LogInfo("game mode: MenuEvents._quickrace is null");
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("game mode: _quickrace read failed: " + e.Message); }
        try
        {
            var t = MenuEvents._timeTrials;
            if (t != null) { Plugin.Log.LogInfo("game mode: falling back to _timeTrials"); return t; }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Start the race the way single player does.
    ///
    /// The game does not start races directly: it instantiates a GameMode object and
    /// calls Init(properties). That Init resets the players' scores, wires the HUD,
    /// subscribes GameModeManager.onFinishGame (the race-end handling) and only then
    /// starts the level. Our flow previously called GameManager.StartGame itself, so
    /// currentGameMode stayed null - hence a race that never finished and a QuitRace
    /// that threw.
    /// </summary>
    // The game-mode prefabs were moved out of Resources into Addressables
    // ("Assets/Resources_moved/GameModes/QuickRace.prefab"), so they are absent
    // until something loads them - single player loads them from its selection
    // screen. Load the same asset here.
    private static readonly string[] ModeKeys = {
        "QuickRace",
        "GameModes/QuickRace",
        "QuickRace.prefab",
        "Assets/Resources_moved/GameModes/QuickRace.prefab",
        "Assets/Resources_moved/GameModes/TimeTrials.prefab",
        "TimeTrials"
    };
    private static bool _modeLoadStarted;
    private static int _modeKeyIndex;
    private static bool _addrInitStarted;
    private static UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle _addrInitHandle;
    private static UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.GameObject> _modeHandle;
    private static GameMode _modePrefab;
    private static float _modeLoadAt;

    /// <summary>Kick off the asset load (the game may already have it in memory).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string BeginModeLoad()
    {
        if (_modePrefab != null) return null;
        var inMemory = ReadMenuMode();
        if (inMemory != null) { _modePrefab = inMemory; return null; }
        // Addressables has to be initialised before anything can be loaded from it;
        // the menu normally does that, and our flow never opens that screen.
        if (!_addrInitStarted)
        {
            try
            {
                _addrInitHandle = UnityEngine.AddressableAssets.Addressables.InitializeAsync();
                _addrInitStarted = true;
                Plugin.Log.LogInfo("game mode: initialising Addressables");
                return null;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("game mode: Addressables init failed: " + (e.InnerException ?? e).Message);
            }
        }
        if (_modeLoadStarted) return null;
        while (_modeKeyIndex < ModeKeys.Length)
        {
            string key = ModeKeys[_modeKeyIndex];
            try
            {
                _modeHandle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<UnityEngine.GameObject>(key);
                _modeLoadStarted = true;
                _modeLoadAt = UnityEngine.Time.realtimeSinceStartup;
                Plugin.Log.LogInfo("game mode: loading '" + key + "' via Addressables");
                return null;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("game mode: Addressables load of '" + key + "' failed: " + (e.InnerException ?? e).Message);
                _modeKeyIndex++;
            }
        }
        return "no game mode asset could be requested";
    }

    /// <summary>True while an Addressables request is outstanding.</summary>
    internal static bool ModeLoadInFlight { get { return _modeLoadStarted; } }

    /// <summary>The mode prefab once its load has finished, else null.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static GameMode TakeLoadedMode()
    {
        if (_modePrefab != null) return _modePrefab;
        // wait for Addressables itself first
        if (_addrInitStarted && !_modeLoadStarted)
        {
            try
            {
                if (!_addrInitHandle.IsDone) return null;
                if (_addrInitHandle.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                {
                    Plugin.Log.LogWarning("game mode: Addressables init failed: " + _addrInitHandle.OperationException);
                    return null;
                }
                Plugin.Log.LogInfo("game mode: Addressables ready");
                BeginModeLoad();
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("game mode: init wait failed: " + e.Message); }
            return null;
        }
        if (!_modeLoadStarted) return null;
        try
        {
            if (!_modeHandle.IsDone)
            {
                if (UnityEngine.Time.realtimeSinceStartup - _modeLoadAt > 20f)
                {
                    Plugin.Log.LogWarning("game mode: asset load timed out for '" + ModeKeys[_modeKeyIndex] + "'");
                    _modeLoadStarted = false; _modeKeyIndex++;
                }
                return null;
            }
            bool ok = false;
            string err = "";
            try { ok = _modeHandle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded; } catch (System.Exception e) { err = e.Message; }
            if (!ok)
            {
                try { err = _modeHandle.OperationException == null ? err : _modeHandle.OperationException.ToString(); } catch { }
                Plugin.Log.LogWarning("game mode: '" + ModeKeys[_modeKeyIndex] + "' could not be loaded: " + err);
                _modeLoadStarted = false;
                _modeKeyIndex++;
                return null;
            }
            var go = _modeHandle.Result;
            _modeLoadStarted = false;
            if (go == null)
            {
                Plugin.Log.LogWarning("game mode: '" + ModeKeys[_modeKeyIndex] + "' loaded but is null");
                _modeKeyIndex++;
                return null;
            }
            GameMode gm = null;
            try { gm = go.GetComponent<GameMode>(); } catch { }
            if (gm == null) { try { gm = go.GetComponentInChildren<GameMode>(true); } catch { } }
            if (gm == null) { try { gm = go.GetComponentInChildren<GameMode>(); } catch { } }
            Plugin.Log.LogInfo("game mode: loaded '" + go.name + "' mode=" + (gm == null ? "NULL" : gm.GetType().Name));
            _modePrefab = gm;
            return gm;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("game mode: waiting for the asset failed: " + (e.InnerException ?? e).Message);
            return null;
        }
    }

    /// <summary>Start the race with the asset the game itself would use.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepStartViaGameMode()
    {
        try
        {
            var gm = GameModeManager.instance;
            if (gm == null) return "no GameModeManager";

            GameMode prefab = _modePrefab;
            if (prefab == null) prefab = ReadMenuMode();
            if (prefab == null) return "the game mode asset is not loaded yet";
            Plugin.Log.LogInfo("game mode: using prefab '" + prefab.name + "'");

            // the map, as the selection screen would hand it over
            CircuitData map = null;
            var roots = GameManager.CircuitCount;
            string want = LeafOf(MpFlow.SelectedMap);
            for (int i = 0; i < roots; i++)
            {
                var c = GameManager.GetCircuit(i);
                if (c == null) continue;
                string sc = null;
                try { sc = c._scene; } catch { }
                if (sc != null && LeafOf(sc) == want) { map = c; break; }
                if (map == null) map = c;
            }
            if (map == null) return "no CircuitData for '" + want + "'";

            var humans = GameManager.players;
            int humanCount = 0;
            if (humans != null) for (int i = 0; i < humans.Length; i++) if (humans[i] != null) humanCount++;
            if (humanCount == 0) return "no human players registered";

            var maps = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<CircuitData>(1);
            maps[0] = map;

            var props = new GameModeManager.SGameModeProperties();
            props._cup = null;
            props._maps = maps;
            props._itemType = MpFlow.ItemType;
            props._laps = MpFlow.Laps;
            props._maxPlayers = humanCount + MpFlow.AiCount;   // AI opponents, 0 for multiplayer
            props._humans = humans;

            Plugin.Log.LogInfo("game mode: StartGame(QuickRace, map='" + want + "' laps=" + props._laps
                               + " itemType=" + props._itemType + " maxPlayers=" + props._maxPlayers
                               + " humans=" + humanCount + ")");
            gm.StartGame(prefab, props);   // the interop proxy already is a GameMode; a C# cast would throw
            ApplyLapsToMode(gm);
            Plugin.Log.LogInfo("game mode: currentGameMode=" + (gm.currentGameMode == null ? "NULL" : gm.currentGameMode.GetType().Name));
            return null;
        }
        catch (System.Exception e)
        {
            var inner = e.InnerException ?? e;
            Plugin.Log.LogError("game mode start failed: " + inner.GetType().Name + ": " + inner.Message + "\n" + (inner.StackTrace ?? ""));
            return inner.GetType().Name + ": " + inner.Message;
        }
    }

    // ---- shared state readers -------------------------------------------------
    internal static System.Reflection.MemberInfo Member(object o, string name)
    {
        try
        {
            var t = o.GetType();
            var p = HarmonyLib.AccessTools.Property(t, name);
            if (p != null && p.CanRead) return p;
            return HarmonyLib.AccessTools.Field(t, name);
        }
        catch { return null; }
    }

    internal static object Get(object o, string field)
    {
        try
        {
            var m = Member(o, field);
            if (m is System.Reflection.PropertyInfo p) return p.GetValue(o);
            if (m is System.Reflection.FieldInfo f) return f.GetValue(o);
            return null;
        }
        catch { return null; }
    }

    internal static string Flag(object o, string field)
    {
        try
        {
            var m = Member(o, field);
            if (m is System.Reflection.PropertyInfo p) return p.GetValue(o)?.ToString() ?? "null";
            if (m is System.Reflection.FieldInfo f) return f.GetValue(o)?.ToString() ?? "null";
            return "?";
        }
        catch { return "?"; }
    }

    /// <summary>Numeric read of a member (Flag/Fnum return formatted strings).</summary>
    internal static float Num(object o, string field)
    {
        try
        {
            var m = Member(o, field);
            object v = m is System.Reflection.PropertyInfo p ? p.GetValue(o)
                     : (m is System.Reflection.FieldInfo f ? f.GetValue(o) : null);
            return v == null ? 0f : System.Convert.ToSingle(v);
        }
        catch { return 0f; }
    }

    /// <summary>Numeric write of a member (harness diagnostics).</summary>
    internal static bool SetNum(object o, string field, float value)
    {
        try { SetValue(o, field, value); return true; } catch { return false; }
    }

    /// <summary>Boolean write of a member (harness diagnostics).</summary>
    internal static bool SetBool(object o, string field, bool value)
    {
        try { SetValue(o, field, value); return true; } catch { return false; }
    }

    /// <summary>Boolean read of a member.</summary>
    internal static bool Bool(object o, string field)
    {
        try
        {
            var m = Member(o, field);
            object v = m is System.Reflection.PropertyInfo p ? p.GetValue(o)
                     : (m is System.Reflection.FieldInfo f ? f.GetValue(o) : null);
            return v != null && System.Convert.ToBoolean(v);
        }
        catch { return false; }
    }

    internal static string Fnum(object o, string field)
    {        try
        {
            var m = Member(o, field);
            object v = m is System.Reflection.PropertyInfo p ? p.GetValue(o) : (m is System.Reflection.FieldInfo f ? f.GetValue(o) : null);
            return v == null ? "?" : System.Convert.ToDouble(v).ToString("0.00");
        }
        catch { return "?"; }
    }

    private static void SetValue(object o, string field, object value)
    {
        try
        {
            var m = Member(o, field);
            if (m is System.Reflection.PropertyInfo p) { if (p.CanWrite) p.SetValue(o, value); }
            else if (m is System.Reflection.FieldInfo f) f.SetValue(o, value);
        }
        catch { }
    }

    /// <summary>The latched state of the driving controller.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ControllerState()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var ctrls = Resources.FindObjectsOfTypeAll<PlayerRacingController>();
            if (ctrls == null) return "none";
            foreach (var c in ctrls)
            {
                if (c == null) continue;
                try { if (c._racer == null) continue; } catch { continue; }
                sb.Append(c.gameObject.name).Append(" humanindex=").Append(c.humanindex)
                  .Append(" spin=").Append(Flag(c, "_spin"))
                  .Append(" jumpHold=").Append(Flag(c, "_jumpHold"))
                  .Append(" brakingHold=").Append(Flag(c, "_brakingHold"))
                  .Append(" upwards=").Append(Flag(c, "_upwards"))
                  .Append(" wheelieinputed=").Append(Flag(c, "_wheelieinputed"))
                  .Append(" lastWInput=").Append(Fnum(c, "_lastWInput"))
                  .Append(" wheelieSeq=").Append(Fnum(c, "_wheelieCurSeq"));
            }
            return sb.Length == 0 ? "no bound controller" : sb.ToString();
        }
        catch (System.Exception e) { return "err:" + e.GetType().Name; }
    }

    /// <summary>The values that drive the character model on the vehicle.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string VehicleState()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0 || racers[0] == null) return "no racer";
            var vc = racers[0].vc;
            if (vc == null) return "no vehicle";
            sb.Append("driftXDir=").Append(Fnum(vc, "_driftXDir"))
              .Append(" driftAngle=").Append(Fnum(vc, "_driftAngle"))
              .Append(" wishSpin=").Append(Flag(vc, "_wishSpin"))
              .Append(" wishbreak=").Append(Flag(vc, "_wishbreak"))
              .Append(" wishTurn=").Append(Fnum(vc, "_wishTurn"))
              .Append(" currentTurn=").Append(Fnum(vc, "_currentTurn"))
              .Append(" wishAccel=").Append(Fnum(vc, "_wishAccel"))
              .Append(" currentSpeed=").Append(Fnum(vc, "_currentSpeed"))
              .Append(" grounded=").Append(Flag(vc, "_isGrounded"))
              .Append(" validGround=").Append(Flag(vc, "_validGround"));
        }
        catch (System.Exception e) { sb.Append("err:").Append(e.GetType().Name); }
        return sb.ToString();
    }

    /// <summary>
    /// Which keys/controls each action is bound to, and what it reads. Several
    /// actions reading 1.00 at once (Ok together with Accelerate) means they share a
    /// control in the active scheme.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string ActionBindings()
    {
        try
        {
            var pl = GameManager.players;
            if (pl == null || pl.Length == 0 || pl[0] == null) return "no player";
            var pi = pl[0].input;
            if (pi == null) return "no input object";
            var map = pi.currentActionMap;
            if (map == null) return "no action map";
            var acts = map.actions;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < acts.Count && i < 12; i++)
            {
                var a = acts[i];
                sb.Append("\n   ").Append(a.name).Append(" = ").Append(ReadActionFloat(a));
                try
                {
                    var bs = a.bindings;
                    for (int k = 0; k < bs.Count && k < 4; k++)
                    {
                        string path = null;
                        try { path = bs[k].path; } catch { }
                        if (string.IsNullOrEmpty(path)) { try { path = bs[k].name; } catch { } }
                        if (!string.IsNullOrEmpty(path)) sb.Append("  [").Append(path).Append(']');
                    }
                }
                catch { }
            }
            return sb.ToString();
        }
        catch (System.Exception e) { return "bindings err:" + e.GetType().Name; }
    }

    /// <summary>What the driving actions read right now.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string InputValues()
    {
        try
        {
            var pl = GameManager.players;
            if (pl == null || pl.Length == 0 || pl[0] == null) return "no player";
            var pi = pl[0].input;
            if (pi == null) return "no input object";
            var map = pi.currentActionMap;
            if (map == null) return "no action map";
            var acts = map.actions;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < acts.Count && i < 12; i++)
            {
                var a = acts[i];
                sb.Append(i > 0 ? ", " : "").Append(a.name).Append('=').Append(ReadActionFloat(a));
            }
            return sb.ToString();
        }
        catch (System.Exception e) { return "err:" + e.GetType().Name; }
    }

    /// <summary>Release the latched hold flags so the model stops spinning.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearHolds()
    {
        try
        {
            var ctrls = Resources.FindObjectsOfTypeAll<PlayerRacingController>();
            if (ctrls == null) return;
            foreach (var c in ctrls)
            {
                if (c == null) continue;
                try { if (c._racer == null) continue; } catch { continue; }
                foreach (var name in new[] { "_spin", "_jumpHold", "_brakingHold", "_wheelieinputed", "_upwards" })
                    SetValue(c, name, false);
                try
                {
                    var vc = c._character;
                    if (vc != null)
                    {
                        SetValue(vc, "_wishSpin", false);
                        SetValue(vc, "_wishbreak", false);
                        SetValue(vc, "_driftXDir", 0f);
                    }
                }
                catch { }
                Plugin.Log.LogInfo("SPIN: cleared flags on " + c.gameObject.name);
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("SPIN: clear failed: " + e.Message); }
    }

    /// <summary>Which node carries which component (diagnostics).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string HierarchyProbe()
    {
        try
        {
            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0 || racers[0] == null) return "no racer";
            var sb = new System.Text.StringBuilder();
            var root = racers[0].transform;
            sb.Append("root=").Append(root.name);
            try
            {
                var vc = racers[0].vc;
                sb.Append(" vc=").Append(vc == null ? "NULL" : (vc.gameObject.name + " onRoot=" + (vc.transform == root)));
            }
            catch { }
            try
            {
                var vc = racers[0].vc;
                if (vc != null)
                {
                    sb.Append("\n   field targets: _pivot=").Append(NameOf(vc._pivot))
                      .Append(" _vehiclepivot=").Append(NameOf(vc._vehiclepivot))
                      .Append(" _driftPivot=").Append(NameOf(vc._driftPivot))
                      .Append(" _charSpeedPivot=").Append(NameOf(vc._charSpeedPivot));
                }
            }
            catch { }
            DumpNode(root, sb, 0, 2);
            return sb.ToString();
        }
        catch (System.Exception e) { return "hierarchy err:" + e.GetType().Name; }
    }

    private static string NameOf(UnityEngine.Transform t)
    {
        try { return t == null ? "NULL" : t.name; } catch { return "?"; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DumpNode(UnityEngine.Transform t, System.Text.StringBuilder sb, int depth, int maxDepth)
    {
        if (t == null || depth > maxDepth) return;
        sb.Append("\n   ").Append(new string(' ', depth * 2)).Append(t.name);
        for (int i = 0; i < t.childCount && i < 24; i++) DumpNode(t.GetChild(i), sb, depth + 1, maxDepth);
    }

    /// <summary>
    /// Roster straight from Mirror when the game's player objects are missing:
    /// the host plus one line per connection.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string TransportRoster(out int count)
    {
        count = 0;
        var sb = new System.Text.StringBuilder();
        try
        {
            bool hosting = MpFlow.IsHosting;
            bool connected = false;
            try { connected = Mirror.NetworkClient.active && Mirror.NetworkClient.isConnected; } catch { }
            int conns = 0;
            try { if (Mirror.NetworkServer.active) conns = Mirror.NetworkServer.connections.Count; } catch { }

            if (!hosting && !connected && conns == 0) return "";

            string me = "You";
            try { if (!string.IsNullOrEmpty(MpFlow.Nickname)) me = MpFlow.Nickname; } catch { }
            sb.Append("<color=#E8EDFA>").Append(me.PadRight(16)).Append("</color>")
              .Append("<color=#6FE38C>").Append(hosting ? "HOST" : "ME").Append("</color>")
              .Append("  ").Append("<color=#8C93A6>").Append("local").Append("</color>");
            count = 1;

            for (int i = 1; i < conns; i++)
            {
                sb.Append('\n').Append("<color=#E8EDFA>").Append(("Player " + (i + 1)).PadRight(16)).Append("</color>")
                  .Append("<color=#8C93A6>").Append("     ").Append("</color>")
                  .Append("  ").Append("<color=#8C93A6>").Append("connection " + i).Append("</color>");
                count++;
            }
            if (count == 1 && !hosting) sb.Append('\n').Append("<color=#8C93A6>").Append("waiting for the host to start...").Append("</color>");
            return sb.ToString();
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("transport roster: " + e.Message); return ""; }
    }

    /// <summary>
    /// How many racers the race must contain: one per player in the session, not
    /// one per local gamepad slot. Everything downstream (participants, cameras,
    /// HUDs, the game mode's maxPlayers) is sized from this, and the ceremony /
    /// result screens need as many racers as they have rows.
    /// FumoMP.extraplayers=&lt;n&gt; forces a value so a single machine can see what a
    /// two or four player race actually does.
    /// </summary>
    internal static int SessionPlayerCount()
    {
        int count = 1;
        try
        {
            int net = 0;
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<FRNetworkPlayer>(true);
                if (arr != null) net = arr.Length;
            }
            catch { }
            int conns = 0;
            try { if (Mirror.NetworkServer.active) conns = Mirror.NetworkServer.connections.Count; } catch { }
            count = Math.Max(1, Math.Max(net, conns));
        }
        catch { }

        try
        {
            string f = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.extraplayers");
            if (System.IO.File.Exists(f))
            {
                int forced = 0;
                int.TryParse(System.IO.File.ReadAllText(f).Trim(), out forced);
                if (forced > count && forced <= 8)
                {
                    Plugin.Log.LogWarning("extraplayers: forcing " + forced + " participants (harness flag)");
                    count = forced;
                }
            }
        }
        catch { }
        return count;
    }

    /// <summary>
    /// Only the local player has a view: the extra humans exist so the game spawns
    /// a racer for every player, but their camera and HUD would turn the screen
    /// into splitscreen. Returns a one line summary for the log.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string SuppressRemoteViews()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            int hidden = 0;
            try
            {
                var cams = GameManager._cameras;
                if (cams != null)
                    for (int i = 1; i < cams.Length; i++)
                    {
                        var c = cams[i];
                        if (c == null) continue;
                        var go = c.gameObject;
                        if (go != null && go.activeSelf) { go.SetActive(false); hidden++; }
                    }
            }
            catch { }
            try
            {
                var huds = GameManager._huds;
                if (huds != null)
                    for (int i = 1; i < huds.Length; i++)
                    {
                        var hh = huds[i];
                        if (hh == null) continue;
                        var go = hh.gameObject;
                        if (go != null && go.activeSelf) { go.SetActive(false); hidden++; }
                    }
            }
            catch { }
        }
        catch (System.Exception e) { sb.Append("view suppress: ").Append(e.GetType().Name).Append("; "); }
        return sb.ToString();
    }

    /// <summary>Every racer with the human slot it belongs to, for the log.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string RacerList()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var racers = GameManager.racers;
            if (racers == null) return "racers=null";
            sb.Append("racers=").Append(racers.Length);
            for (int i = 0; i < racers.Length; i++)
            {
                var r = racers[i];
                if (r == null) { sb.Append(" [").Append(i).Append("=null]"); continue; }
                int human = -1;
                try { human = GameManager.GetHumanIndex(r.plr); } catch { }
                string pos = "?";
                try { var t = r.transform; pos = t.position.x.ToString("F0") + "," + t.position.y.ToString("F0") + "," + t.position.z.ToString("F0"); } catch { }
                sb.Append(" [").Append(i).Append(' ').Append(r.name).Append(" human=").Append(human)
                  .Append(" pos=").Append(pos).Append(']');
            }
        }
        catch (System.Exception e) { sb.Append("racer list: ").Append(e.GetType().Name); }
        return sb.ToString();
    }

    /// <summary>
    /// Find out where the HUD's lap total really lives: it showed 1/5 while the
    /// lobby was set to 3 laps, and the GameMode has no _laps member. This scans
    /// the objects that could own it and logs every lap-ish member with its value,
    /// plus the same for the selected circuit.
    /// </summary>
    internal static void LapProbe()
    {
        if (!DebugLogging) return;
        var sb = new System.Text.StringBuilder("laps probe (lobby=" + MpFlow.Laps + "):");
        object[] subjects = new object[5];
        string[] labels = new string[5];
        try { subjects[0] = GameManager.instance; labels[0] = "GameManager.instance"; } catch { }
        try { subjects[1] = GameModeManager.instance; labels[1] = "GameModeManager.instance"; } catch { }
        try { subjects[2] = GameModeManager.instance.currentGameMode; labels[2] = "currentGameMode"; } catch { }
        try { subjects[3] = FRNetGameState.instance; labels[3] = "FRNetGameState.instance"; } catch { }
        try { subjects[4] = LevelManager.instance; labels[4] = "LevelManager.instance"; } catch { }

        for (int i = 0; i < subjects.Length; i++)
        {
            var o = subjects[i];
            if (o == null) { sb.Append("\n   ").Append(labels[i]).Append(" = null"); continue; }
            try
            {
                var t = o.GetType();
                int found = 0;
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    if (f.Name.IndexOf("lap", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    object v = null; try { v = f.GetValue(o); } catch { }
                    sb.Append("\n   ").Append(labels[i]).Append('.').Append(f.Name).Append(" = ").Append(v == null ? "null" : v.ToString());
                    found++;
                }
                foreach (var pr in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    if (pr.Name.IndexOf("lap", StringComparison.OrdinalIgnoreCase) < 0 || !pr.CanRead) continue;
                    object v = null; try { v = pr.GetValue(o); } catch { continue; }
                    sb.Append("\n   ").Append(labels[i]).Append('.').Append(pr.Name).Append(" = ").Append(v == null ? "null" : v.ToString());
                    found++;
                }
                if (found == 0) sb.Append("\n   ").Append(labels[i]).Append(": no lap-ish member");
                // plus every small int field, so the value the HUD shows (5 with the
                // lobby set to 3) can be found even when its name says nothing
                // The HUD's "x/y" comes from a field on this object with value
                // total-1 (= 4 for a 5 lap race), so print every numeric member
                // holding 2..8 with its type - the name is what we need.
                int ints = 0;
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    object v = null; try { v = f.GetValue(o); } catch { continue; }
                    if (v == null) continue;
                    int iv;
                    try { iv = System.Convert.ToInt32(v); } catch { continue; }
                    if (iv < 2 || iv > 8) continue;
                    sb.Append("\n      ").Append(f.FieldType.Name).Append(' ').Append(labels[i]).Append('.')
                      .Append(f.Name).Append(" = ").Append(iv);
                    if (++ints > 18) break;
                }
            }
            catch (System.Exception e) { sb.Append("\n   ").Append(labels[i]).Append(": ").Append(e.GetType().Name); }
        }
        Plugin.Log.LogInfo(sb.ToString());
    }

    /// <summary>
    /// The HUD reads its "x/y" lap total from the GameMode object, not from the
    /// properties we hand to StartGame, so a QuickRace set to 3 laps still showed
    /// "1/5" (the prefab's own value). Write the lobby's setting onto the mode as
    /// well; the field is looked up by name and this is skipped if it is absent.
    /// </summary>
    private static void ApplyLapsToMode(GameModeManager gm)
    {
        try
        {
            var mode = gm == null ? null : gm.currentGameMode;
            if (mode == null) { Plugin.Log.LogInfo("laps: no game mode to write to"); return; }

            // The HUD prints "<lap>/<total>" where the total is GameMode._defaultLaps
            // (measured with the lap probe: the field was 5 while the lobby said 3).
            // QuickRace never copies the requested laps into it, so the lobby setting
            // was simply ignored. Write both names: _defaultLaps exists on the base
            // GameMode, _laps only on some modes.
            string wrote = "";
            foreach (string name in new[] { "_defaultLaps", "_laps" })
            {
                var m = Member(mode, name);
                if (m == null) continue;
                object before = m is System.Reflection.PropertyInfo p ? p.GetValue(mode)
                              : (m is System.Reflection.FieldInfo f ? f.GetValue(mode) : null);
                SetValue(mode, name, MpFlow.Laps);
                object after = m is System.Reflection.PropertyInfo p2 ? p2.GetValue(mode)
                            : (m is System.Reflection.FieldInfo f2 ? f2.GetValue(mode) : null);
                wrote += name + " " + (before == null ? "?" : before.ToString())
                       + "->" + (after == null ? "?" : after.ToString()) + "  ";
            }
            Plugin.Log.LogInfo("laps: game mode " + (wrote.Length == 0 ? "no writable lap member" : wrote)
                               + "(lobby " + MpFlow.Laps + ")");
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("laps: " + e.Message); }
    }

    /// <summary>
    /// Mirror's disconnect timeout is 30 s by default, and a client that spends
    /// longer than that loading the circuit (this build is slow, and the container
    /// runs at a few fps) is dropped mid-race even though it is healthy. Give the
    /// connection room to breathe; the kart channel does not depend on it anyway.
    /// </summary>
    internal static void HardenTransport()
    {
        try
        {
            var nm = NetworkManager.singleton;
            if (nm == null) return;
            // the interop assembly does not expose this member, so it is set by
            // reflection on whatever the runtime object really is
            var m = Member(nm, "disconnectTimeout");
            if (m == null) { Plugin.Log.LogInfo("net: disconnectTimeout not found (leaving the default)"); return; }
            object cur = m is System.Reflection.PropertyInfo p ? p.GetValue(nm)
                       : (m is System.Reflection.FieldInfo f ? f.GetValue(nm) : null);
            float now = cur == null ? 0f : System.Convert.ToSingle(cur);
            if (now < 60f)
            {
                SetValue(nm, "disconnectTimeout", 60f);
                object after = m is System.Reflection.PropertyInfo p2 ? p2.GetValue(nm)
                             : (m is System.Reflection.FieldInfo f2 ? f2.GetValue(nm) : null);
                Plugin.Log.LogInfo("net: disconnect timeout " + now.ToString("F0") + "s -> "
                                   + (after == null ? "?" : System.Convert.ToSingle(after).ToString("F0") + "s"));
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("harden transport: " + e.Message); }
    }

    /// <summary>True when the kart channel has heard nothing for a while.</summary>
    internal static bool NetQuiet
    {
        get
        {
            try { return MpNet.Running && MpNet.PeerCount > 0 && MpNet.SecondsSinceLastPacket > 3f; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Find out how this build builds a kart's visible model, so a ghost can wear
    /// the *remote* player's character instead of a copy of your own. CharacterData
    /// exposes charModel / kartModel / GetModel(skinIndex), and GameManager keeps
    /// the list of characters loaded from Addressables.
    /// </summary>
    internal static void ModelProbe3()
    {
        var sb = new System.Text.StringBuilder("model probe:");
        try
        {
            // 1. where the character list lives
            var gt = typeof(GameManager);
            foreach (var f in gt.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
            {
                string tn = f.FieldType.Name;
                if (tn.IndexOf("Character", StringComparison.OrdinalIgnoreCase) < 0
                    && tn.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) < 0
                    && tn.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) < 0) continue;
                object v = null; try { v = f.GetValue(null); } catch { }
                sb.Append("\n   static ").Append(tn).Append(' ').Append(f.Name).Append(" = ").Append(v == null ? "null" : v.ToString());
            }
            foreach (var f in gt.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            {
                string tn = f.FieldType.Name;
                if (tn.IndexOf("Character", StringComparison.OrdinalIgnoreCase) < 0
                    && tn.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) < 0
                    && tn.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) < 0) continue;
                object v = null; try { v = f.GetValue(GameManager.instance); } catch { }
                sb.Append("\n   ").Append(tn).Append(' ').Append(f.Name).Append(" = ").Append(v == null ? "null" : v.ToString());
            }

            // 2. the character list itself
            var chars = Characters();
            sb.Append("\n   characters=").Append(chars == null ? "null" : chars.Count.ToString());
            if (chars != null)
                for (int i = 0; i < chars.Count && i < 12; i++)
                {
                    var cd = chars[i];
                    if (cd == null) { sb.Append("\n   [").Append(i).Append("]=null"); continue; }
                    string dn = null; try { dn = cd.displayName; } catch { }
                    var cm = cd.charModel; var km = cd.kartModel;
                    var m0 = cd.GetModel(0);
                    int skins = 0, vehicles = 0;
                    try { skins = cd.skins == null ? 0 : cd.skins.Count; } catch { }
                    try { vehicles = cd.vehicles == null ? 0 : cd.vehicles.Count; } catch { }
                    sb.Append("\n   [").Append(i).Append("] '").Append(dn).Append("' charModel=").Append(cm == null ? "null" : cm.name)
                      .Append(" kartModel=").Append(km == null ? "null" : km.name)
                      .Append(" GetModel(0)=").Append(m0 == null ? "null" : m0.name)
                      .Append(" skins=").Append(skins).Append(" vehicles=").Append(vehicles);
                }

            // 3. where the models sit on a live kart
            var local = LocalRacer();
            if (local != null)
            {
                var t = local.transform;
                var fumo = t.Find("Character/VehiclePivot/AnimPivot/AccelPivot/Accel2Pivot/Fumo");
                var kart = t.Find("Character/VehiclePivot/AnimPivot/Kart");
                sb.Append("\n   fumo node=").Append(fumo == null ? "MISSING" : Path(fumo));
                if (fumo != null) for (int i = 0; i < fumo.childCount; i++) sb.Append("  child[").Append(i).Append("]=").Append(fumo.GetChild(i).name);
                sb.Append("\n   kart node=").Append(kart == null ? "MISSING" : Path(kart));
                if (kart != null) for (int i = 0; i < kart.childCount; i++) sb.Append("  child[").Append(i).Append("]=").Append(kart.GetChild(i).name);
                sb.Append("\n   my character=").Append(MpFlow.CharacterIdx).Append(" skin=").Append(MpFlow.SkinIdx)
                  .Append(" vehicle=").Append(MpFlow.VehicleIdx);
            }
        }
        catch (System.Exception e) { sb.Append("\n   probe failed: ").Append(e.GetType().Name).Append(' ').Append(e.Message); }
        Plugin.Log.LogInfo(sb.ToString());
    }

    /// <summary>The characters the game loaded (GameManager keeps them from Addressables).</summary>
    internal static System.Collections.Generic.IList<CharacterData> Characters()
    {
        try
        {
            var gt = typeof(GameManager);
            string[] names = { "_characters", "characters", "_chars", "_characterList", "_allCharacters" };
            foreach (string n in names)
            {
                var f = gt.GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
                if (f == null) continue;
                object v = null;
                try { v = f.GetValue(f.IsStatic ? null : GameManager.instance); } catch { continue; }
                if (v == null) continue;
                var arr = v as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<CharacterData>;
                if (arr != null) return arr;
                var list = v as Il2CppSystem.Collections.Generic.List<CharacterData>;
                if (list != null)
                {
                    var copy = new System.Collections.Generic.List<CharacterData>();
                    for (int i = 0; i < list.Count; i++) copy.Add(list[i]);
                    return copy;
                }
            }
        }
        catch { }
        return null;
    }

    private static string Path(Transform t)
    {
        try
        {
            string p = t.name;
            var cur = t.parent;
            for (int i = 0; i < 12 && cur != null; i++) { p = cur.name + "/" + p; cur = cur.parent; }
            return p;
        }
        catch { return "?"; }
    }

    /// <summary>
    /// The game's own post-race overlay ("Next", a dark panel with a big portrait)
    /// stays on screen and swallows the session: it is drawn by the build's result
    /// prefab, which is never dismissed because nothing subscribes to its button. It
    /// is not a TMP label (a text dump at race end only showed the HUD), so it is
    /// found by object name instead and switched off. Names are logged, so a log
    /// tells exactly which object was hidden.
    /// </summary>
    internal static void HideGameEndScreens(string why)
    {
        try
        {
            string[] patterns = { "next", "result", "ceremony", "podium", "scoreboard", "ranking", "endrace", "raceend" };
            int hidden = 0;
            var sb = new System.Text.StringBuilder();

            // anything that could draw it
            var gos = UnityEngine.Resources.FindObjectsOfTypeAll<GameObject>();
            if (gos != null)
                foreach (var go in gos)
                {
                    if (go == null) continue;
                    string n = go.name;
                    if (string.IsNullOrEmpty(n)) continue;
                    if (IsOurUi(go)) continue;                             // never our own UI
                    string low = n.ToLowerInvariant();
                    bool match = false;
                    for (int i = 0; i < patterns.Length; i++) if (low.Contains(patterns[i])) { match = true; break; }
                    if (!match) continue;
                    if (!go.activeInHierarchy) continue;
                    try
                    {
                        go.SetActive(false);
                        hidden++;
                        if (sb.Length < 400) sb.Append(" | ").Append(PathOf(go.transform));
                    }
                    catch { }
                }

            // Second pass: whatever draws the word. The overlay in this build is not a
            // TMP label (a text dump at race end only ever showed the HUD), so legacy
            // UnityEngine.UI.Text and world-space TextMesh are checked as well, and the
            // object that owns the text plus a few of its ancestors are switched off.
            try
            {
                var lts = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>();
                if (lts != null)
                    foreach (var t in lts)
                    {
                        if (t == null || t.gameObject == null || !t.gameObject.activeInHierarchy) continue;
                        string txt = null;
                        try { txt = t.text; } catch { }
                        if (string.IsNullOrEmpty(txt)) continue;
                        string low = txt.ToLowerInvariant();
                        if (low.IndexOf("next") < 0 && low.IndexOf("result") < 0 && low.IndexOf("continue") < 0) continue;
                        hidden += HideAncestors(t.gameObject, 3, sb, "text '" + txt + "'");
                    }
                var meshes = UnityEngine.Resources.FindObjectsOfTypeAll<TextMesh>();
                if (meshes != null)
                    foreach (var m in meshes)
                    {
                        if (m == null || m.gameObject == null || !m.gameObject.activeInHierarchy) continue;
                        string txt = null;
                        try { txt = m.text; } catch { }
                        if (string.IsNullOrEmpty(txt)) continue;
                        string low = txt.ToLowerInvariant();
                        if (low.IndexOf("next") < 0 && low.IndexOf("result") < 0) continue;
                        hidden += HideAncestors(m.gameObject, 2, sb, "mesh text '" + txt + "'");
                    }
            }
            catch { }

            if (hidden > 0)
                Plugin.Log.LogWarning("end screen: hid " + hidden + " game overlay object(s) (" + why + "):" + sb);
            else
                Plugin.Log.LogInfo("end screen: no game overlay found (" + why + ")");
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("end screen: " + e.GetType().Name + " " + e.Message); }
    }

    /// <summary>Read a static GameManager member by name (debug string).</summary>
    private static string ReadStatic(string member)
    {
        try
        {
            var t = typeof(GameManager);
            var p = t.GetProperty(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (p != null) { var v = p.GetValue(null); return v == null ? "null" : v.ToString(); }
            var f = t.GetField(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (f != null) { var v = f.GetValue(null); return v == null ? "null" : v.ToString(); }
        }
        catch { }
        return "?";
    }

    /// <summary>Write a static GameManager member by name; false when it does not exist.</summary>
    private static bool WriteStatic(string member, object value)
    {
        try
        {
            var t = typeof(GameManager);
            var p = t.GetProperty(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (p != null && p.CanWrite) { p.SetValue(null, value); return true; }
            var f = t.GetField(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (f != null) { f.SetValue(null, value); return true; }
        }
        catch { }
        return false;
    }

    /// <summary>Switch off the object and up to <paramref name="levels"/> of its parents.</summary>
    private static int HideAncestors(GameObject go, int levels, System.Text.StringBuilder sb, string why)
    {
        int n = 0;
        try
        {
            if (IsOurUi(go)) return 0;                                 // never our own UI
            var t = go.transform;
            for (int i = 0; i <= levels && t != null; i++)
            {
                var cur = t.gameObject;
                if (cur == null) break;
                if (IsOurUi(cur)) break;                               // never our own UI
                var canvas = cur.GetComponent<Canvas>();
                bool isRootCanvas = canvas != null && cur.transform.parent == null;
                if (!isRootCanvas && cur.activeSelf)
                {
                    cur.SetActive(false);
                    n++;
                    if (sb.Length < 500) sb.Append(" | ").Append(PathOf(cur.transform)).Append(" [").Append(why).Append(']');
                    break;                                             // one level is normally enough
                }
                t = t.parent;
            }
        }
        catch { }
        return n;
    }

    /// <summary>
    /// True when the object is part of our own UI. The check has to walk the whole
    /// ancestor chain: our results panel is 'FumoMP_Canvas/ResultsDim/Results', and
    /// matching on the object's own name alone hid the panel a moment after it was
    /// shown (the leaf names 'Results' / 'ResultsBackBtn' look exactly like the game's
    /// result screen). Every UI root we create is named 'FumoMP*'.
    /// </summary>
    private static bool IsOurUi(GameObject go)
    {
        try
        {
            var t = go != null ? go.transform : null;
            for (int i = 0; i < 24 && t != null; i++)
            {
                string n = t.name;
                if (!string.IsNullOrEmpty(n) && n.StartsWith("FumoMP", System.StringComparison.OrdinalIgnoreCase)) return true;
                t = t.parent;
            }
        }
        catch { }
        return false;
    }

    private static string PathOf(Transform t)
    {
        try
        {
            string p = t.name;
            var cur = t.parent;
            for (int i = 0; i < 8 && cur != null; i++) { p = cur.name + "/" + p; cur = cur.parent; }
            return p;
        }
        catch { return "?"; }
    }

    /// <summary>
    /// Put the game back into a state where another race can start. After a race the
    /// build leaves GameManager.inRace true and its end overlay up, and RequestStart
    /// then refuses with "a race is already in progress" - which is why a second round
    /// needed a game restart. Clearing the flags here (only once we are really out of
    /// the race scene) makes the session reusable.
    /// </summary>
    internal static void ResetRaceState(string why)
    {
        try
        {
            bool inRaceScene = MpUgui.InRaceScenePublic;
            if (inRaceScene) { Plugin.Log.LogInfo("race state: still in a race scene, not resetting (" + why + ")"); return; }

            // inRace / endedRace are static on this build (an earlier probe already
            // reported "Could not find field for type GameManager and name _inRace"),
            // so both the static and the instance member are attempted.
            string before = ReadStatic("inRace") + "/" + ReadStatic("endedRace");
            bool ok1 = WriteStatic("inRace", false);
            bool ok2 = WriteStatic("endedRace", false);
            object gm = null;
            try { gm = GameManager.instance; } catch { }
            if (gm != null)
            {
                SetValue(gm, "inRace", false);
                SetValue(gm, "endedRace", false);
            }
            string after = ReadStatic("inRace") + "/" + ReadStatic("endedRace");
            Plugin.Log.LogInfo("race state: inRace/endedRace " + before + " -> " + after
                               + " (static writes ok=" + (ok1 && ok2) + ", " + why + ")");
            HideGameEndScreens(why);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("race state reset: " + e.Message); }
    }

    /// <summary>Harness-only: run the racer channel's own round trip / relay test.</summary>
    internal static bool NetTestRequested
    {
        get
        {
            try { return System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.nettest")); }
            catch { return false; }
        }
    }

    /// <summary>Harness-only: exercise the game's own in-race quit path.</summary>
    internal static bool QuitRaceTestRequested
    {
        get
        {
            try { return System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.quittest")); }
            catch { return false; }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepQuitRace()
    {
        try
        {
            Plugin.Log.LogInfo("test: calling GameManager.QuitRace()");
            GameManager.QuitRace();
            Plugin.Log.LogInfo("test: QuitRace returned normally");
            return null;
        }
        catch (System.Exception e)
        {
            var inner = e.InnerException ?? e;
            Plugin.Log.LogError("test: QuitRace threw " + inner.GetType().Name + ": " + inner.Message);
            return inner.GetType().Name;
        }
    }

    /// <summary>The racer that belongs to this machine's player (human slot 0).</summary>
    internal static PlayerRacer LocalRacer()
    {
        try
        {
            var racers = GameManager.racers;
            if (racers == null) return null;
            for (int i = 0; i < racers.Length; i++)
            {
                var r = racers[i];
                if (r == null) continue;
                int h = -1;
                try { h = GameManager.GetHumanIndex(r.plr); } catch { }
                if (h == 0) return r;
            }
            // single racer races have no human mapping to read
            if (racers.Length == 1) return racers[0];
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Is this machine's race over? The game's own endedRace only rises once
    /// *every* racer in the field has finished, and in a multiplayer race the
    /// players on the other machines are still driving, so the local player must be
    /// able to finish without waiting for them.
    /// </summary>
    internal static bool RaceFinishedLocally()
    {
        try
        {
            if (GameManager.endedRace) return true;
            var local = LocalRacer();
            if (local != null && local._endedRace) return true;
            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0) return false;
            for (int i = 0; i < racers.Length; i++)
            {
                if (racers[i] == null) continue;
                if (!racers[i]._endedRace) return false;
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Fires when the race is over. In this build EndRace() only raises
    /// onRaceEnded, and the objects subscribed to it in these scenes are the HUD,
    /// the music and the minimap - nothing sends the player back to the menu, so
    /// the game sits on the finished track forever (and shows no way out).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepReturnToMenu()
    {
        try
        {
            var lm = LevelManager.instance;
            if (lm == null) return "no LevelManager";
            Plugin.Log.LogInfo("race over with no transition - returning to the main menu");
            lm.LoadMainMenu();
            return null;
        }
        catch (System.Exception e)
        {
            var inner = e.InnerException ?? e;
            Plugin.Log.LogError("return to menu failed: " + inner.GetType().Name + ": " + inner.Message);
            return inner.Message;
        }
    }

    /// <summary>
    /// Why the HUD may be invisible although it exists: what the canvas is doing
    /// against the actual framebuffer size.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string HudScaleProbe()
    {
        var sb = new System.Text.StringBuilder();
        try { sb.Append("screen=").Append(UnityEngine.Screen.width).Append('x').Append(UnityEngine.Screen.height).Append(" fullscreen=").Append(UnityEngine.Screen.fullScreen); } catch { }
        try
        {
            var hud = GameManager.GetHUD(0);
            if (hud == null) { sb.Append(" no HUD"); return sb.ToString(); }
            var c = hud.canvas;
            if (c == null) { sb.Append(" no canvas"); return sb.ToString(); }
            sb.Append(" renderMode=").Append(c.renderMode).Append(" scaleFactor=").Append(c.scaleFactor.ToString("0.00"))
              .Append(" sortingOrder=").Append(c.sortingOrder).Append(" overrideSorting=").Append(c.overrideSorting);
            try
            {
                var pr = c.pixelRect;
                sb.Append(" pixelRect=(").Append(pr.x.ToString("0")).Append(',').Append(pr.y.ToString("0")).Append(' ')
                  .Append(pr.width.ToString("0")).Append('x').Append(pr.height.ToString("0")).Append(')');
            }
            catch { }
            try
            {
                var rt = c.rootCanvas == null ? null : c.rootCanvas.GetComponent<UnityEngine.RectTransform>();
                if (rt != null) { var r = rt.rect; sb.Append(" canvasRect=").Append(r.width.ToString("0")).Append('x').Append(r.height.ToString("0")); }
            }
            catch { }
            try
            {
                var sc = c.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (sc != null)
                    sb.Append(" scaler=").Append(sc.uiScaleMode).Append(" ref=").Append(sc.referenceResolution.x.ToString("0")).Append('x').Append(sc.referenceResolution.y.ToString("0"))
                      .Append(" match=").Append(sc.matchWidthOrHeight.ToString("0.00")).Append(" screenMatch=").Append(sc.screenMatchMode);
                else sb.Append(" scaler=none");
            }
            catch { }
            try { var go = hud.gameObject; sb.Append(" hudLayer=").Append(go.layer).Append(" cam=").Append(hud.canvas.worldCamera == null ? "NULL" : hud.canvas.worldCamera.name); } catch { }
        }
        catch (System.Exception e) { sb.Append(" hud err:").Append(e.GetType().Name).Append(':').Append(e.Message); }
        return sb.ToString();
    }

    /// <summary>
    /// What actually spins: the racer's transform children with their local
    /// rotations, so a model rotating on its own shows up as one child's angles
    /// running away while the wheels and the body stay put.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string ModelProbe()
    {
        try
        {
            var racers = GameManager.racers;
            if (racers == null || racers.Length == 0 || racers[0] == null) return "no racer";
            var t = racers[0].transform;
            var sb = new System.Text.StringBuilder();
            var e0 = t.eulerAngles; sb.Append("root euler=(").Append(e0.x.ToString("0")).Append(',').Append(e0.y.ToString("0")).Append(',').Append(e0.z.ToString("0")).Append(')');
            sb.Append(" children=").Append(t.childCount).Append('[');
            for (int i = 0; i < t.childCount && i < 8; i++)
            {
                var c = t.GetChild(i);
                var l = c.localEulerAngles;
                sb.Append(i > 0 ? " | " : "").Append(c.name).Append(" local=(")
                  .Append(l.x.ToString("0")).Append(',').Append(l.y.ToString("0")).Append(',').Append(l.z.ToString("0")).Append(')');
                int gc = c.childCount;
                if (gc > 0) { var g = c.GetChild(0); var gl = g.localEulerAngles; sb.Append(" > ").Append(g.name).Append("=(").Append(gl.y.ToString("0")).Append(')'); }
            }
            sb.Append(']');
            try
            {
                var vc = racers[0].vc;
                if (vc == null) sb.Append(" vc=NULL");
                else
                {
                    var d = vc._data;
                    sb.Append(" vehicle=").Append(d == null ? "NULL" : d.name)
                      .Append(" turnForce=").Append(vc._turnForce.ToString("0.0"))
                      .Append(" turnMax=").Append(vc._turnMax.ToString("0.0"))
                      .Append(" accel=").Append(vc._acceleration.ToString("0.0"))
                      .Append(" topSpeed=").Append(vc._topSpeed.ToString("0.0"))
                      .Append(" driftGain=").Append(vc.driftSteerGain.ToString("0.00"));
                }
            }
            catch (System.Exception e) { sb.Append(" vc err:").Append(e.GetType().Name); }
            return sb.ToString();
        }
        catch (System.Exception e) { return "model err:" + e.GetType().Name + ":" + e.Message; }
    }

    /// <summary>Why the race clock never ticks: the GameManager that advances it.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string ClockProbe()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var gm = GameManager.instance;
            sb.Append("GameManager.inst=").Append(gm == null ? "NULL" : "ok");
            if (gm != null)
                sb.Append(" enabled=").Append(gm.enabled).Append(" active=").Append(gm.gameObject.activeInHierarchy);
        }
        catch (System.Exception e) { sb.Append(" gm err:").Append(e.GetType().Name); }
        try { sb.Append(" timeScale=").Append(UnityEngine.Time.timeScale.ToString("0.00")).Append(" time=").Append(UnityEngine.Time.time.ToString("0.0")); } catch { }
        try { sb.Append(" GetTime=").Append(GameManager.GetTime().ToString("0.00")); } catch (System.Exception e) { sb.Append(" GetTime err:").Append(e.GetType().Name); }
        try { sb.Append(" timeSince=").Append(GameManager.timeSinceRaceStarted.ToString("0.00")); } catch { }
        try { sb.Append(" inRace=").Append(GameManager.inRace); } catch { }
        try { sb.Append(" netTime=").Append(Mirror.NetworkTime.time.ToString("0.00")); } catch { }
        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string ManualCountdown(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<PlayerRacer> racers,
                                           double starttimestamp)
    {
        if (racers == null || racers.Length == 0)
        {
            // OnStartCircuit has already published the racers, so the game's own
            // array is the reliable source when the argument cannot be read
            try { racers = GameManager.racers; } catch { }
        }
        if (racers == null || racers.Length == 0)
        {
            Plugin.Log.LogWarning("countdown: no racers to release");
            return "no racers";
        }

        bool running = false;
        double since = 0;
        try { running = GameManager.inRace; since = GameManager.timeSinceRaceStarted; } catch { }
        if (running && since > 0.0)
        {
            Plugin.Log.LogInfo("countdown: race already under way (" + since.ToString("0.0") + "s), nothing to do");
            return null;
        }

        Plugin.Log.LogInfo("countdown: taking over for " + racers.Length + " racer(s), ts=" + starttimestamp);

        // Nothing may drive before the start, so hold the cars on the line first;
        // the game's own StartingSequence cannot be used (the component does not
        // exist in this build and the chain that would create it is the broken
        // one), so the countdown is run here: freeze, wait, release.
        try
        {
            GameManager.FreezeRacers(racers);
            Plugin.Log.LogInfo("countdown: racers frozen for " + CountdownSeconds.ToString("0.0") + "s");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning("countdown: freeze failed: " + (e.InnerException ?? e).Message);
        }

        _pendingRacers = racers;
        _pendingTimestamp = starttimestamp;
        _pendingLeft = CountdownSeconds;
        _pending = true;
        string result = null;

        // the race flag lives past that call in OnStartCircuit; when the countdown
        // is entered from anywhere else it has to be set here
        bool inRace = false;
        try { inRace = GameManager.inRace; } catch { }
        if (!inRace)
        {
            try
            {
                var f = HarmonyLib.AccessTools.Field(typeof(GameManager), "_inRace")
                        ?? (System.Reflection.MemberInfo)HarmonyLib.AccessTools.Property(typeof(GameManager), "_inRace");
                if (f is System.Reflection.FieldInfo fi) { fi.SetValue(null, true); Plugin.Log.LogInfo("countdown: race flag raised"); }
                else if (f is System.Reflection.PropertyInfo pi && pi.CanWrite) { pi.SetValue(null, true); Plugin.Log.LogInfo("countdown: race flag raised"); }
                else Plugin.Log.LogInfo("countdown: race flag is not writable from here (the game raises it itself)");
            }
            catch (System.Exception e) { Plugin.Log.LogInfo("countdown: race flag left to the game (" + e.GetType().Name + ")"); }
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepLocalStartSequenceNoArgs()
    {
        Plugin.Log.LogInfo("GameManager.StartSequence() [no args]");
        GameManager.StartSequence();
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepLocalStartSequence()
    {
        double ts = 0;
        try { ts = Mirror.NetworkTime.time; } catch { }
        if (ts <= 0) { try { ts = UnityEngine.Time.realtimeSinceStartup; } catch { } }
        Plugin.Log.LogInfo("GameManager.StartSequence(" + ts + ")");
        GameManager.StartSequence(ts);
        return null;
    }

    private static string LeafOf(string path)
    {
        if (string.IsNullOrEmpty(path)) return "forest";
        int i = path.LastIndexOf('/');
        string leaf = i >= 0 ? path.Substring(i + 1) : path;
        return leaf.Replace(".unity", "");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepLoadCircuit(string mapName)
    {
        var lm = LevelManager.instance;
        if (lm == null) return "LevelManager.instance == null";
        Plugin.Log.LogInfo("LoadCircuit('" + mapName + "')");
        lm.LoadCircuit(mapName);
        return null;
    }

    /// <summary>
    /// The menu sizes the human-player bookkeeping before a race: a slot array for
    /// the players themselves plus the matching camera and HUD arrays that the
    /// spawn code indexes with the player's human index. Report what is there, and
    /// grow the player array through the game's own setter when it is too small.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static HumanGamePlayer[] EnsureHumanSlots(int count)
    {
        HumanGamePlayer[] humans = null;
        try
        {
            humans = GameManager.players;
            Plugin.Log.LogInfo("slot check: players=" + (humans == null ? "null" : humans.Length.ToString())
                               + " cameras=" + (GameManager._cameras == null ? "null" : GameManager._cameras.Length.ToString())
                               + " huds=" + (GameManager._huds == null ? "null" : GameManager._huds.Length.ToString())
                               + " racerPrefab=" + (GameManager._racerPrefab != null)
                               + " cameraPrefab=" + (GameManager._cameraPrefab != null)
                               + " hudPrefab=" + (GameManager._hudPrefab != null));
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("slot check failed: " + e.GetType().Name + " " + e.Message); }

        if (humans == null || humans.Length < count)
        {
            try
            {
                humans = new HumanGamePlayer[count];
                GameManager.players = humans;
                Plugin.Log.LogInfo("players array (re)sized to " + count);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("could not size the players array: " + e.Message); }
        }

        // the camera and HUD arrays are indexed by the same human index; when the
        // menu never ran they can be null or empty, which the spawn code turns
        // into cameras[i] on a zero-length array
        try
        {
            if (GameManager._cameras == null || GameManager._cameras.Length < count)
            {
                GameManager._cameras = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<PlayerCamera>(count);
                Plugin.Log.LogInfo("cameras array (re)sized to " + count);
            }
            if (GameManager._huds == null || GameManager._huds.Length < count)
            {
                GameManager._huds = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<PlayerHUD>(count);
                Plugin.Log.LogInfo("huds array (re)sized to " + count);
            }
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("could not size camera/hud arrays: " + e.Message); }

        return humans;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepBuildPlayers()
    {
        var gs = FRNetGameState.instance;
        if (gs == null) return "FRNetGameState.instance == null";

        // Exactly one local participant per machine. Every machine races its own
        // kart and the other players are ghost karts (MpGhost): the game treats
        // extra humans as local splitscreen players - they get a camera, a HUD and
        // an input slot, and a participant without an input device makes the game's
        // own start sequence hang (measured with 3 humans in the container).
        // FumoMP.extraplayers=<n> still forces more for experiments.
        int count = 1;
        try
        {
            string f = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "FumoMP.extraplayers");
            if (System.IO.File.Exists(f))
            {
                int forced = 0;
                int.TryParse(System.IO.File.ReadAllText(f).Trim(), out forced);
                if (forced > 1 && forced <= 8) { count = forced; Plugin.Log.LogWarning("extraplayers: forcing " + forced + " local participants (harness flag)"); }
            }
        }
        catch { }

        // The spawn path does not take the participants from the settings alone:
        // it resolves each racer's human index with GameManager.GetHumanIndex,
        // which is a linear search of the *static* GameManager._players array and
        // returns -1 when the player is not in it. That -1 is then used as an
        // index into the static _cameras array, i.e. cameras[-1] and an
        // IndexOutOfRangeException before a single racer exists. So the menu's own
        // registration path has to be used: size the human bookkeeping, then
        // AddHuman(i) each participant and keep the objects it returns.
        var humans = EnsureHumanSlots(count);
        var players = new GamePlayer[count];
        for (int i = 0; i < count; i++)
        {
            HumanGamePlayer h = null;
            try { h = GameManager.AddHuman(i); } catch (System.Exception e) { Plugin.Log.LogWarning("AddHuman(" + i + ") failed: " + e.Message); }
            if (h == null)
            {
                h = GameManager.CreateHumanPlayer();
                if (h == null) h = new HumanGamePlayer();
                if (humans != null) humans[i] = h;
            }
            h.character = i == 0 ? MpFlow.CharacterIdx : 0;
            h.skin = i == 0 ? MpFlow.SkinIdx : 0;
            h.vehicle = i == 0 ? MpFlow.VehicleIdx : 0;
            players[i] = h;
        }

        var settings = gs._settings;
        settings._players = players;
        // the loader wants the bare scene name ("forest"); the value coming from
        // the addressables-style map path ("Assets/Scenes/forest.unity") makes the
        // game's own load sit in the loading scene forever
        // items: the race setup only builds them for itemType 1 or 2 (0 disables
        // the whole item system, which also empties the item/skill part of the HUD)
        if (settings._itemType != MpFlow.ItemType)
        {
            Plugin.Log.LogInfo("itemType " + settings._itemType + " -> " + MpFlow.ItemType);
            settings._itemType = MpFlow.ItemType;
        }

        string bare = LeafOf(MpFlow.SelectedMap);
        if (settings._level != bare)
        {
            Plugin.Log.LogInfo("level '" + (settings._level ?? "NULL") + "' -> '" + bare + "'");
            settings._level = bare;
        }
        gs._settings = settings;
        try { gs.map = bare; } catch { }
        Plugin.Log.LogInfo("built " + count + " participant(s) for the race");

        var ierr = StepLocalInput();
        if (ierr != null) Plugin.Log.LogWarning("local input: " + ierr);
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepLocalStartGame()
    {
        var gs = FRNetGameState.instance;
        if (gs == null) return "FRNetGameState.instance == null";
        var settings = gs._settings;
        if (settings == null) return "no settings on the game state";
        bool ok = GameManager.StartGame(settings);
        Plugin.Log.LogInfo("GameManager.StartGame(settings) -> " + ok);
        return ok ? null : "GameManager.StartGame returned false";
    }

    /// <summary>Tell every client to run its own local start sequence.</summary>
    /// <summary>How many racers the game has spawned (0 until the circuit loads).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int RacerCount()
    {
        try
        {
            var r = GameManager.racers;
            return r == null ? 0 : r.Length;
        }
        catch { return -1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepBroadcastStartSequence()
    {
        try
        {
            if (!Mirror.NetworkServer.active) return "server not active";
            var msg = new FRNetGameState.NetGameStartSequence();
            Mirror.NetworkServer.SendToAll(msg);
            return null;
        }
        catch (System.Exception e) { return e.GetType().Name + ": " + e.Message; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepSendReady()
    {
        try
        {
            if (!NetworkClient.active) return "client not active";
            NetworkClient.Send(new FRNetGameState.NetGameReady());
            return null;
        }
        catch (System.Exception e)
        {
            return e.GetType().Name + ": " + e.Message;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StartWaitProbe()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var gs = FRNetGameState.instance;
            if (gs == null) sb.Append("state=null");
            else
            {
                sb.Append("syncPrefab=").Append(gs._gameSyncPrefab == null ? "NULL" : "ok");
                sb.Append(" settings=").Append(gs._settings == null ? "NULL" : "ok");
                sb.Append(" syncInstance=").Append(gs._gameSyncInstance == null ? "null" : "ok");
                sb.Append(" connections=");
                var conns = gs._connections;
                sb.Append(conns == null ? "null" : conns.Length.ToString());
            }
        }
        catch (System.Exception e) { sb.Append("gsErr:").Append(e.GetType().Name); }

        try
        {
            var nm = NetworkManager.singleton;
            if (nm != null)
            {
                sb.Append(" mode=").Append(nm.mode);
                sb.Append(" numPlayers=").Append(nm.numPlayers);
            }
        }
        catch { }

        try { sb.Append(" spawned=").Append(Mirror.NetworkServer.spawned == null ? -1 : Mirror.NetworkServer.spawned.Count); } catch { }
        try { sb.Append(" inRace=").Append(GameManager.inRace); } catch { }
        int players = 0;
        try { players = PlayerCountLive(); } catch { }
        sb.Append(" frPlayers=").Append(players);
        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepInvokeStartGame()
    {
        var gs = FRNetGameState.instance;
        if (gs == null) return "FRNetGameState.instance == null";
        var settings = gs._settings;
        if (settings == null) return "FRNetGameState._settings == null";
        gs.StartGame(settings);
        return null;
    }

    /// <summary>
    /// Vouch for the readiness handshake. The game's StartGame coroutine waits on
    /// IsEveryoneReady(), which returns true only when every FRNetworkPlayer has
    /// _isServerReady set - and that flag is normally raised by the client's
    /// NetGameReady message, a path this build never completes. Setting the public
    /// isServerReady property lets the host's own start sequence proceed.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepMarkPlayersReady()
    {
        var arr = UnityEngine.Object.FindObjectsOfType<FRNetworkPlayer>(true);
        if (arr == null || arr.Length == 0) return "no players yet";
        int changed = 0;
        for (int i = 0; i < arr.Length; i++)
        {
            var p = arr[i];
            if (p == null) continue;
            try
            {
                if (!p.isServerReady) { p.isServerReady = true; changed++; }
            }
            catch (System.Exception e)
            {
                return "isServerReady: " + e.GetType().Name;
            }
        }
        if (changed > 0) Plugin.Log.LogInfo("marked " + changed + " player(s) ready for the start handshake");
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string GameStateInfo()
    {
        try
        {
            var gs = FRNetGameState.instance;
            if (gs == null) return "no state";
            return "map=" + (gs.map ?? "?");
        }
        catch (System.Exception e) { return "err:" + e.GetType().Name; }
    }

    // ------------------------------------------------------- game menu buttons
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string CollectText(GameObject go)
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var texts = go.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (texts != null)
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i];
                    if (t == null || string.IsNullOrEmpty(t.text)) continue;
                    if (sb.Length > 0) sb.Append(" | ");
                    sb.Append(t.text.Trim());
                }
            }
        }
        catch { }
        return sb.ToString();
    }

    /// <summary>
    /// Inventory every MenuUIButton (inactive included) and switch off the game's
    /// broken online entry so it cannot be confused with our lobby button. The
    /// game's OnlineButton sits under inactive parents, which is why a plain
    /// by-name search misses it — searching by component type with includeInactive
    /// finds it wherever it is.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string StepHideGameOnlineButton()
    {
        var btns = UnityEngine.Object.FindObjectsOfType<MenuUIButton>(true);
        if (btns == null) return "no MenuUIButton type";

        var log = new System.Text.StringBuilder("menu button scan:");
        int hidden = 0;
        for (int i = 0; i < btns.Length; i++)
        {
            var b = btns[i];
            if (b == null) continue;
            var go = b.gameObject;
            if (go == null) continue;
            string label = CollectText(go);
            bool isOnline =
                go.name.IndexOf("Online", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Online", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Multiplayer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("Touhou", StringComparison.OrdinalIgnoreCase) >= 0;
            log.Append("\n   '").Append(go.name).Append("' active=").Append(go.activeInHierarchy)
               .Append(" text='").Append(label).Append("'");
            // where it sits on screen, so a single-player race can be started by hand
            if (go.activeInHierarchy &&
                (go.name == "RaceButton" || go.name == "QuickRaceButton" || go.name == "SoloButton" ||
                 go.name == "GrandPrixButton" || go.name == "OnlineButton" || go.name == "SelectionButton"))
            {
                try
                {
                    var rt = b.GetComponent<UnityEngine.RectTransform>();
                    if (rt == null) rt = go.GetComponent<UnityEngine.RectTransform>();
                    if (rt != null) log.Append(ClickPoint(rt));
                }
                catch { }
            }
            if (isOnline && go.activeSelf)
            {
                go.SetActive(false);
                hidden++;
                log.Append("   <== HIDDEN");
            }
        }
        if (hidden > 0) Plugin.Log.LogInfo(log.ToString() + "\n   hidden=" + hidden);
        return null;   // silence when there was nothing to hide (runs on a throttle)
    }
}
