using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace FumoMP
{
    /// <summary>
    /// The kart channel: positions, identity, ping and the race-lifecycle control
    /// messages. Deliberately separate from the game's Mirror connection.
    ///
    /// Why not Mirror: this build only exposes the generic message API
    /// (NetworkServer.RegisterHandler&lt;T&gt; / SendToAll&lt;T&gt;) and no raw message ids,
    /// and IL2CPP only has generic instantiations for the message types the game
    /// was compiled with - a message struct of ours cannot be packed at runtime.
    /// The game's own netcode also moves no racers at all (FRNetworkRacer syncs
    /// lap/trigger state and damage RPCs; race prefabs have no NetworkIdentity and
    /// no NetworkTransform), so there was nothing to reuse for transforms.
    ///
    /// Roles
    ///   host   binds 0.0.0.0:(game port + 1). Assigns each client a slot, keeps the
    ///          peer table (id, endpoint, nickname, ping), relays kart states and
    ///          is the only sender of type 3/4.
    ///   client sends its own kart to the host, applies what the host relays, and
    ///          follows the host's race start/end.
    ///
    /// Peers are keyed by a random client id, never by the source endpoint: a
    /// client whose socket is recreated (Mirror timeout, retry) gets a new
    /// ephemeral port, and endpoint keying silently sent its karts to a dead port.
    ///
    /// Packet: [0] type, [1] protocol version, [2] slot, [3..6] client id.
    ///   type 1 kart   65 bytes: [7..18] position, [19..34] rotation xyzw,
    ///                [35..38] speed, [39..42] steering, [43] grounded,
    ///                [44..47] airborne time, [48..51] lap (float),
    ///                [52..55] total time (float), [56] finished,
    ///                [57..60] trigger counter, [61..64] trigger index
    ///                (the counters the game's own ranking is built on)
    ///   type 2 hello  42 bytes: [7] character, [8] skin, [9] vehicle,
    ///                [10..41] nickname (32 bytes UTF-8, zero padded)
    ///   type 3 start  40 bytes: [7] laps, [8] item type, [9..39] map name
    ///   type 4 end     8 bytes: [7] reason (0 = race over, 1 = host left)
    ///   type 5 ping   11 bytes: [7..10] sender timestamp (float)
    ///   type 6 info   44 bytes: [7] character, [8] skin, [9] vehicle,
    ///                [10..11] ping ms, [12..43] nickname
    ///   type 7 hit    20 bytes: [2] sender slot, [3..6] sender client id,
    ///                [7] victim slot, [8..11] push direction x, [12..15] direction z,
    ///                [16..19] push strength (m/s)
    ///   type 8 weapon 24 bytes: like type 7 plus [20..23] the damage duration (s)
    ///                the victim's own game should apply
    /// </summary>
    internal static class MpNet
    {
        internal const byte Proto = 5;
        private const byte TypeKart = 1, TypeHello = 2, TypeStart = 3, TypeEnd = 4, TypePing = 5, TypeInfo = 6, TypeHit = 7, TypeWeapon = 8;
        private const int LenKart = 65, LenHello = 42, LenStart = 40, LenEnd = 8, LenPing = 11, LenInfo = 44, LenHit = 20, LenWeapon = 24;
        private const byte SlotUnassigned = 255;
        internal const int MaxPlayers = 8;

        // ---------------------------------------------------------------- types
        internal class Peer
        {
            internal int Slot;
            internal uint ClientId;
            internal IPEndPoint Endpoint;
            internal string Nick = "";
            internal int Character, Skin, Vehicle;
            internal float LastSeen, RttMs, PingSentAt;
            internal bool IsHost;
        }

        internal class RemoteKart
        {
            internal int Slot;
            internal string Nick = "";
            internal Vector3 Pos;
            internal Quaternion Rot;
            internal float Speed, Steer, Air, TotalTime;
            internal int Lap, Trigger, TriggerIndex;
            internal bool Grounded, Finished;
            internal float UpdatedAt;
        }

        // ---------------------------------------------------------------- state
        private static Socket _sock;
        private static Thread _rx;
        private static volatile bool _running;
        private static bool _server;
        private static int _port, _ownSlot = -1;
        private static IPEndPoint _hostEp, _lastFrom;
        private static readonly object Lock = new object();

        private static readonly ConcurrentQueue<byte[]> Inbox = new ConcurrentQueue<byte[]>();
        private static readonly Dictionary<uint, Peer> PeersById = new Dictionary<uint, Peer>();
        private static readonly List<Peer> PeerList = new List<Peer>();
        private static readonly RemoteKart[] Karts = new RemoteKart[MaxPlayers];

        private static float _nextSend, _nextHello, _nextPing, _nextWatchdog, _lastReport, _lastRxAt;
        private static int _sent, _recv, _relayed, _applied, _helloSent, _restarts, _dropped;
        private static byte[] _pendingStart;
        private static int _pendingStartCount;
        private static float _pendingStartLeft;
        private static bool _startSeen, _endSeen, _mismatchLogged, _hostAlive;
        private static float _graceLeft = 120f;
        private static bool _wasHosting, _wasClient;
        private static string _lastHost = "";
        private static readonly uint _clientId = (uint)new System.Random().Next(1, int.MaxValue);

        internal static bool Running { get { return _running; } }
        internal static bool IsHost { get { return _server; } }
        internal static int Port { get { return _port; } }
        internal static int OwnSlot { get { return _server ? 0 : _ownSlot; } }
        internal static int PeerCount { get { lock (Lock) return PeerList.Count; } }

        /// <summary>Seconds since the last packet arrived (for the in-race status line).</summary>
        internal static float SecondsSinceLastPacket
        {
            get { try { return Time.realtimeSinceStartup - _lastRxAt; } catch { return 0f; } }
        }

        /// <summary>Peers as the lobby and the results table want them (host first).</summary>
        internal static List<Peer> Peers
        {
            get
            {
                lock (Lock)
                {
                    var list = new List<Peer>(PeerList);
                    list.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                    return list;
                }
            }
        }

        internal static RemoteKart Kart(int slot)
        {
            if (slot < 0 || slot >= MaxPlayers) return null;
            return Karts[slot];
        }

        /// <summary>Raised on a client when the host starts a race.</summary>
        internal static Action<string, int, int> RaceStartReceived;
        /// <summary>Raised when the host ends the race or leaves (reason 0/1).</summary>
        internal static Action<int> RaceEndReceived;
        /// <summary>Raised when another machine reports that it bumped us
        /// (sender slot, push direction in world x/z, strength in m/s).</summary>
        internal static Action<int, Vector3, float> HitReceived;
        /// <summary>Raised when another machine's weapon reached us
        /// (sender slot, push direction, damage duration, extra shove).</summary>
        internal static Action<int, Vector3, float, float> WeaponHitReceived;

        // ---------------------------------------------------------------- setup
        internal static string StartHost(int gamePort)
        {
            try
            {
                Stop();
                _server = true;
                _ownSlot = 0;
                _port = gamePort + 1;
                _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _sock.Bind(new IPEndPoint(IPAddress.Any, _port));
                StartRx();
                _lastRxAt = Time.realtimeSinceStartup;
                Plugin.Log.LogInfo("net: host kart channel listening on 0.0.0.0:" + _port + " (udp, proto " + Proto + ")");
                return null;
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                Plugin.Log.LogWarning("net: host channel failed: " + inner.Message);
                Stop();
                return inner.GetType().Name + ": " + inner.Message;
            }
        }

        internal static string StartClient(string host, int gamePort)
        {
            try
            {
                Stop();
                _server = false;
                _ownSlot = -1;
                _port = gamePort + 1;
                IPAddress ip;
                if (string.IsNullOrEmpty(host)) return "no host address";
                if (!IPAddress.TryParse(host, out ip)) return "bad host address '" + host + "'";
                _hostEp = new IPEndPoint(ip, _port);
                _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _sock.Bind(new IPEndPoint(IPAddress.Any, 0));
                StartRx();
                _lastRxAt = Time.realtimeSinceStartup;
                Plugin.Log.LogInfo("net: client kart channel -> " + _hostEp + " (local port " + LocalPort
                                   + ", proto " + Proto + ")");
                return null;
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                Plugin.Log.LogWarning("net: client channel failed: " + inner.Message);
                Stop();
                return inner.GetType().Name + ": " + inner.Message;
            }
        }

        private static int LocalPort
        {
            get { try { return ((IPEndPoint)_sock.LocalEndPoint).Port; } catch { return -1; } }
        }

        internal static void Stop()
        {
            _running = false;
            try { if (_sock != null) _sock.Close(); } catch { }
            _sock = null;
            _rx = null;
            _server = false;
            _port = 0;
            _ownSlot = -1;
            lock (Lock)
            {
                PeersById.Clear();
                PeerList.Clear();
                for (int i = 0; i < MaxPlayers; i++) Karts[i] = null;
            }
            byte[] junk;
            while (Inbox.TryDequeue(out junk)) { }
            MpGhost.ClearRemote();
        }

        /// <summary>
        /// Allow the next race of the same session to be announced: the start/end
        /// flags are per race, not per session.
        /// </summary>
        internal static void ResetRaceFlags()
        {
            _startSeen = false;
            _endSeen = false;
        }

        internal static void ResetCounters()
        {
            _sent = _recv = _relayed = _applied = _helloSent = _restarts = _dropped = 0;
            _startSeen = _endSeen = false;
            _lastRxAt = Time.realtimeSinceStartup;
        }

        private static void StartRx()
        {
            _running = true;
            _rx = new Thread(RxLoop);
            _rx.IsBackground = true;
            _rx.Name = "FumoMP-KartChannel";
            _rx.Start();
        }

        private static void RxLoop()
        {
            var buf = new byte[2048];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int errors = 0;

            while (_running)
            {
                try
                {
                    var sock = _sock;
                    if (sock == null) break;
                    int n = sock.ReceiveFrom(buf, ref from);
                    errors = 0;
                    if (n < LenEnd) { _dropped++; continue; }
                    if (buf[1] != Proto)
                    {
                        _dropped++;
                        if (!_mismatchLogged)
                        {
                            _mismatchLogged = true;
                            Plugin.Log.LogWarning("net: ignoring packets from " + from + " using protocol " + buf[1]
                                                  + " (this build speaks " + Proto + ") - both machines need the same FumoMP version");
                        }
                        continue;
                    }
                    int len = Math.Min(n, LenKart);
                    var pkt = new byte[len];
                    Array.Copy(buf, pkt, len);
                    var src = (IPEndPoint)from;

                    if (!_server)
                    {
                        _hostEp = src;                      // follow the host if it moved
                        Inbox.Enqueue(pkt);
                    }
                    else
                    {
                        _lastFrom = src;
                        uint cid = GetU(pkt, 3);
                        var peer = Touch(cid, src);
                        if (peer == null) continue;
                        pkt[2] = (byte)peer.Slot;           // the host owns slot assignment
                        if (pkt[0] != TypeHello && pkt[0] != TypePing) Relay(pkt, src);
                        Inbox.Enqueue(pkt);
                    }
                }
                catch (SocketException)
                {
                    if (!_running) break;
                    if (++errors > 200) break;              // the watchdog rebuilds
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("net: rx: " + e.Message);
                    Thread.Sleep(50);
                }
            }
        }

        /// <summary>Find or create the peer for a client id, following endpoint moves.</summary>
        private static Peer Touch(uint cid, IPEndPoint src)
        {
            if (cid == 0) src.GetHashCode();                 // the host stamps its own id as 0
            lock (Lock)
            {
                Peer p;
                if (cid == 0 || !PeersById.TryGetValue(cid, out p))
                {
                    if (PeerList.Count >= MaxPlayers - 1)
                    {
                        Plugin.Log.LogWarning("net: refusing " + src + " - the session is full (" + MaxPlayers + " players)");
                        return null;
                    }
                    p = new Peer { ClientId = cid, Slot = NextFreeSlot(), Endpoint = src, LastSeen = Time.realtimeSinceStartup };
                    PeersById[cid] = p;
                    PeerList.Add(p);
                    Plugin.Log.LogWarning("net: peer joined slot " + p.Slot + " (" + src + ", id " + cid + ")");
                }
                else if (p.Endpoint == null || p.Endpoint.Port != src.Port || !p.Endpoint.Address.Equals(src.Address))
                {
                    p.Endpoint = src;
                    Plugin.Log.LogInfo("net: peer slot " + p.Slot + " moved to " + src);
                }
                p.LastSeen = Time.realtimeSinceStartup;
                return p;
            }
        }

        private static int NextFreeSlot()
        {
            for (int s = 1; s < MaxPlayers; s++)
            {
                bool used = false;
                foreach (var p in PeerList) if (p.Slot == s) { used = true; break; }
                if (!used) return s;
            }
            return MaxPlayers - 1;
        }

        private static void Relay(byte[] pkt, IPEndPoint from)
        {
            foreach (var p in Peers)
            {
                var ep = p.Endpoint;
                if (ep == null) continue;
                if (ep.Address.Equals(from.Address) && ep.Port == from.Port) continue;
                try { _sock.SendTo(pkt, ep); _relayed++; } catch { }
            }
        }

        // -------------------------------------------------------------- runtime
        internal static void Tick(float dt)
        {
            try
            {
                if (!_running)
                {
                    TryAutoStart();
                    if (!_running) return;
                }

                Drain();

                _nextSend -= dt;
                if (_nextSend <= 0f) { _nextSend = 0.05f; SendOwn(); }        // 20 Hz

                _nextHello -= dt;
                if (_nextHello <= 0f) { _nextHello = _server ? 2f : 1f; SendHello(); _helloSent++; }

                _nextPing -= dt;
                if (_nextPing <= 0f) { _nextPing = 1.5f; SendPing(); }

                if (_pendingStart != null)
                {
                    _pendingStartLeft -= dt;
                    if (_pendingStartLeft <= 0f)
                    {
                        SendRaw(_pendingStart);
                        _pendingStartCount++;
                        _pendingStartLeft = 0.3f;
                        if (_pendingStartCount >= 5)
                        {
                            _pendingStart = null;
                            Plugin.Log.LogInfo("net: race start broadcast finished (5x)");
                        }
                    }
                }

                _nextWatchdog -= dt;
                if (_nextWatchdog <= 0f)
                {
                    _nextWatchdog = 5f;
                    if (_sent > 0 && Time.realtimeSinceStartup - _lastRxAt > 15f) Restart("no packets for 15s");
                }

                float now = Time.realtimeSinceStartup;
                if (now - _lastReport > 10f)
                {
                    _lastReport = now;
                    Expire(now);
                    Plugin.Log.LogInfo("net: " + (_server ? "host" : "client") + ":" + _port
                                       + " peers=" + PeerCount + " sent=" + _sent + " recv=" + _recv
                                       + " relayed=" + _relayed + " applied=" + _applied
                                       + " hello=" + _helloSent + " drop=" + _dropped
                                       + " restarts=" + _restarts + " ghosts=" + MpGhost.Count
                                       + " | " + MpWeapons.Status());
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("net tick: " + (e.InnerException ?? e).Message); }
        }

        private static void Restart(string why)
        {
            _restarts++;
            Plugin.Log.LogWarning("net: " + why + " - rebuilding the kart channel (restart #" + _restarts + ")");
            if (_server) StartHost(_port - 1);
            else if (_hostEp != null) StartClient(_hostEp.Address.ToString(), _port - 1);
            _lastRxAt = Time.realtimeSinceStartup;
        }

        private static void Expire(float now)
        {
            var gone = new List<Peer>();
            foreach (var p in Peers)
            {
                float quiet = now - p.LastSeen;
                if (quiet > 30f) gone.Add(p);
            }
            foreach (var p in gone)
            {
                lock (Lock)
                {
                    PeersById.Remove(p.ClientId);
                    PeerList.Remove(p);
                }
                if (Karts[p.Slot] != null)
                {
                    Karts[p.Slot] = null;
                    MpGhost.RemoveRemote(p.Slot);
                    Plugin.Log.LogWarning("net: peer slot " + p.Slot + " '" + p.Nick + "' left the session");
                }
            }
        }

        private static void Drain()
        {
            byte[] pkt;
            while (Inbox.TryDequeue(out pkt))
            {
                _recv++;
                _lastRxAt = Time.realtimeSinceStartup;
                try
                {
                    switch (pkt[0])
                    {
                        case TypeKart: OnKart(pkt); break;
                        case TypeHello: OnHello(pkt); break;
                        case TypePing: OnPing(pkt); break;
                        case TypeStart: OnStart(pkt); break;
                        case TypeEnd: OnEnd(pkt); break;
                        case TypeInfo: OnInfo(pkt); break;
                        case TypeHit: OnHit(pkt); break;
                        case TypeWeapon: OnWeapon(pkt); break;
                        default: _dropped++; break;
                    }
                }
                catch (Exception e) { _dropped++; Plugin.Log.LogWarning("net: packet: " + e.Message); }
            }
        }

        private static void OnKart(byte[] b)
        {
            var st = DecodeKart(b);
            if (st == null || st.Slot < 0 || st.Slot >= MaxPlayers) { _dropped++; return; }
            if (float.IsNaN(st.Pos.x) || float.IsNaN(st.Pos.y) || float.IsNaN(st.Pos.z)
                || Mathf.Abs(st.Pos.x) > 100000f || Mathf.Abs(st.Pos.y) > 100000f || Mathf.Abs(st.Pos.z) > 100000f)
            { _dropped++; return; }                                  // never let bad data move a ghost

            var k = Karts[st.Slot];
            if (k == null) { k = new RemoteKart { Slot = st.Slot }; Karts[st.Slot] = k; }
            k.Pos = st.Pos; k.Rot = st.Rot; k.Speed = st.Speed; k.Steer = st.Steer;
            k.Air = st.Air; k.Grounded = st.Grounded; k.Lap = st.Lap;
            k.TotalTime = st.TotalTime; k.Finished = st.Finished;
            k.Trigger = st.Trigger; k.TriggerIndex = st.TriggerIndex;
            k.UpdatedAt = Time.realtimeSinceStartup;
            var p = FindPeer(st.Slot);
            if (p != null && !string.IsNullOrEmpty(p.Nick)) k.Nick = p.Nick;
            p = null;
            if (_server && st.Slot == 0) return;                     // our own kart echoed back
            MpGhost.Apply(st.Slot, st.Pos, st.Rot, st.Speed, st.Steer, st.Grounded, k.UpdatedAt);
            _applied++;
        }

        private static void OnHello(byte[] b)
        {
            uint cid = GetU(b, 3);
            string nick = ReadString(b, 10, 32);
            if (_server)
            {
                var p = Touch(cid, _lastFrom);
                if (p == null) return;
                bool first = string.IsNullOrEmpty(p.Nick);
                p.Nick = string.IsNullOrEmpty(nick) ? ("Player" + p.Slot) : nick;
                p.Character = b[7]; p.Skin = b[8]; p.Vehicle = b[9];
                if (first)
                {
                    Plugin.Log.LogInfo("net: peer slot " + p.Slot + " '" + p.Nick + "' ready (proto " + Proto + ")");
                    SendInfoTo(p);
                }
                MpGhost.SetNick(p.Slot, p.Nick);
                MpGhost.SetIdentity(p.Slot, p.Character, p.Skin, p.Vehicle);
                BroadcastInfo();
                var k = Karts[p.Slot];
                if (k == null) { k = new RemoteKart { Slot = p.Slot }; Karts[p.Slot] = k; }
                k.Nick = p.Nick;
            }
            else
            {
                _ownSlot = b[2];                            // the host tells us our slot
                var k = Karts[0];
                if (k == null) { k = new RemoteKart { Slot = 0 }; Karts[0] = k; }
                if (!string.IsNullOrEmpty(nick)) k.Nick = nick;
                MpGhost.SetNick(0, nick);
                MpGhost.SetIdentity(0, b[7], b[8], b[9]);
                _hostAlive = true;
            }
        }

        private static void OnInfo(byte[] b)
        {
            if (_server) return;
            int slot = b[2];
            if (slot < 0 || slot >= MaxPlayers) { _dropped++; return; }
            string nick = ReadString(b, 12, 32);
            int ms = b[10] | (b[11] << 8);
            var k = Karts[slot];
            if (k == null) { k = new RemoteKart { Slot = slot }; Karts[slot] = k; }
            k.Nick = nick;
            var p = FindPeer(slot);
            if (p == null)
            {
                p = new Peer { Slot = slot, Nick = nick, IsHost = slot == 0 };
                lock (Lock) PeerList.Add(p);
            }
            p.Nick = nick; p.RttMs = ms; p.LastSeen = Time.realtimeSinceStartup;
            p.Character = b[7]; p.Skin = b[8]; p.Vehicle = b[9];
            MpGhost.SetNick(slot, nick);
            if (slot != 0) MpGhost.SetIdentity(slot, p.Character, p.Skin, p.Vehicle);
        }

        private static void OnPing(byte[] b)
        {
            float stamp = GetF(b, 7);
            if (_server)
            {
                var p = FindPeer(b[2]);
                if (p != null)
                {
                    if (p.PingSentAt > 0f)
                    {
                        p.RttMs = Mathf.Max(0f, (Time.realtimeSinceStartup - p.PingSentAt) * 1000f);
                        p.PingSentAt = 0f;
                    }
                    var reply = new byte[LenPing];
                    reply[0] = TypePing; reply[1] = Proto; reply[2] = 0;
                    PutU(reply, 3, 0);
                    PutF(reply, 7, stamp);
                    if (_sock != null && p.Endpoint != null) { try { _sock.SendTo(reply, p.Endpoint); } catch { } }
                }
            }
            else
            {
                float rtt = (Time.realtimeSinceStartup - stamp) * 1000f;
                if (rtt >= 0f && rtt < 5000f)
                {
                    var p = FindPeer(0);
                    if (p == null)
                    {
                        p = new Peer { Slot = 0, IsHost = true, Nick = "Host" };
                        lock (Lock) PeerList.Add(p);
                    }
                    p.RttMs = rtt;
                    p.LastSeen = Time.realtimeSinceStartup;
                }
            }
        }

        private static void OnStart(byte[] b)
        {
            if (_server || _startSeen) return;
            _startSeen = true;
            int laps = b[7], item = b[8];
            string map = ReadString(b, 9, 31);
            Plugin.Log.LogWarning("net: host started the race - map='" + map + "' laps=" + laps + " items=" + item);
            var h = RaceStartReceived;
            if (h != null) { try { h(map, laps, item); } catch (Exception e) { Plugin.Log.LogWarning("net: start handler: " + e.Message); } }
        }

        private static void OnEnd(byte[] b)
        {
            if (_server || _endSeen) return;
            _endSeen = true;
            int reason = b[7];
            Plugin.Log.LogWarning(reason == 1 ? "net: the host left the session" : "net: the host ended the race");
            var h = RaceEndReceived;
            if (h != null) { try { h(reason); } catch (Exception e) { Plugin.Log.LogWarning("net: end handler: " + e.Message); } }
        }

        private static Peer FindPeer(int slot)
        {
            lock (Lock)
            {
                foreach (var p in PeerList) if (p.Slot == slot) return p;
            }
            return null;
        }

        /// <summary>
        /// A kart bump. Two karts cannot push each other over the network by
        /// themselves - on the other machine we are only a ghost, which is driven by
        /// packets and has no physics - so the machine that feels the hit tells the
        /// other machine to shove its own kart. The packet carries the direction (in
        /// world space, x/z) and the strength in m/s; the receiver adds it to its own
        /// vehicle with the game's own VehicleCharacter.AddForce.
        /// </summary>
        private static void OnHit(byte[] b)
        {
            int victim = b[7];
            if (victim != MySlot) return;                       // not addressed to us
            int from = b[2];
            var dir = new Vector3(GetF(b, 8), 0f, GetF(b, 12));
            float strength = GetF(b, 16);
            if (float.IsNaN(dir.x) || float.IsNaN(dir.z) || float.IsNaN(strength)) { _dropped++; return; }
            if (strength <= 0f || strength > 60f) { _dropped++; return; }
            var h = HitReceived;
            if (h != null) { try { h(from, dir, strength); } catch (Exception e) { Plugin.Log.LogWarning("net: hit handler: " + e.Message); } }
        }

        /// <summary>Our own slot in this session (the host is always 0).</summary>
        internal static int MySlot { get { return _server ? 0 : _ownSlot; } }

        /// <summary>
        /// Somebody's weapon reached us, sent by the machine that fired it. The packet
        /// is addressed to one slot; it travels the same way as a bump (client -> host
        /// -> relay, or straight from the host) and everyone else ignores it.
        /// </summary>
        private static void OnWeapon(byte[] b)
        {
            int victim = b[7];
            if (victim != MySlot) return;
            int from = b[2];
            var dir = new Vector3(GetF(b, 8), 0f, GetF(b, 12));
            float freeze = GetF(b, 16);
            float shove = GetF(b, 20);
            if (float.IsNaN(dir.x) || float.IsNaN(dir.z) || float.IsNaN(freeze) || float.IsNaN(shove)) { _dropped++; return; }
            if (freeze < 0f || freeze > 10f || shove < 0f || shove > 60f) { _dropped++; return; }
            var h = WeaponHitReceived;
            if (h != null) { try { h(from, dir, freeze, shove); } catch (Exception e) { Plugin.Log.LogWarning("net: weapon handler: " + e.Message); } }
        }

        /// <summary>Tell the player in <paramref name="victimSlot"/> that our weapon hit them.</summary>
        internal static void SendWeaponHit(int victimSlot, Vector3 dir, float freeze, float shove)
        {
            try
            {
                if (!_running || _sock == null) return;
                if (victimSlot < 0 || victimSlot >= MaxPlayers || victimSlot == MySlot) return;
                float len = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
                var b = new byte[LenWeapon];
                b[0] = TypeWeapon; b[1] = Proto; b[2] = (byte)MySlot;
                PutU(b, 3, _clientId);
                b[7] = (byte)victimSlot;
                PutF(b, 8, len > 0.0001f ? dir.x / len : 0f);
                PutF(b, 12, len > 0.0001f ? dir.z / len : 0f);
                PutF(b, 16, Mathf.Clamp(freeze, 0f, 10f));
                PutF(b, 20, Mathf.Clamp(shove, 0f, 40f));

                if (_server)
                {
                    var p = FindPeer(victimSlot);
                    if (p == null || p.Endpoint == null) return;
                    _sock.SendTo(b, p.Endpoint);
                }
                else
                {
                    var host = _hostEp;
                    if (host == null) return;
                    _sock.SendTo(b, host);
                }
                _sent++;
            }
            catch (Exception e) { Plugin.Log.LogWarning("net: send weapon: " + e.Message); }
        }

        /// <summary>Tell the player in <paramref name="victimSlot"/> to push their kart.</summary>
        internal static void SendHit(int victimSlot, Vector3 dir, float strength)
        {
            try
            {
                if (!_running || _sock == null) return;
                if (victimSlot < 0 || victimSlot >= MaxPlayers || victimSlot == MySlot) return;
                float len = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
                if (len < 0.0001f) return;
                var b = new byte[LenHit];
                b[0] = TypeHit; b[1] = Proto; b[2] = (byte)MySlot;
                PutU(b, 3, _clientId);
                b[7] = (byte)victimSlot;
                PutF(b, 8, dir.x / len);
                PutF(b, 12, dir.z / len);
                PutF(b, 16, Mathf.Clamp(strength, 1f, 40f));

                if (_server)
                {
                    // the host is not in its own relay path, so it sends the packet
                    // itself; a client's hit arrives through the normal relay
                    var p = FindPeer(victimSlot);
                    if (p == null || p.Endpoint == null) return;
                    _sock.SendTo(b, p.Endpoint);
                }
                else
                {
                    var host = _hostEp;
                    if (host == null) return;
                    _sock.SendTo(b, host);
                }
                _sent++;
            }
            catch (Exception e) { Plugin.Log.LogWarning("net: send hit: " + e.Message); }
        }

        // -------------------------------------------------------------- sending
        private static void SendRaw(byte[] pkt)
        {
            try
            {
                if (_sock == null) return;
                if (_server)
                {
                    foreach (var p in Peers)
                        if (p.Endpoint != null) { try { _sock.SendTo(pkt, p.Endpoint); _sent++; } catch { } }
                }
                else if (_hostEp != null) { _sock.SendTo(pkt, _hostEp); _sent++; }
            }
            catch { }
        }

        private static void SendOwn()
        {
            try
            {
                var local = MpDiag.LocalRacer();
                if (local == null) return;
                var vc = local.vc;
                var tr = local.transform;
                float total = 0f;
                try { total = (float)local.GetTotalTime().TotalSeconds; } catch { }

                var pkt = EncodeKart((byte)(_server ? 0 : SlotUnassigned), tr.position, tr.rotation,
                                     vc == null ? 0f : MpDiag.Num(vc, "_currentSpeed"),
                                     vc == null ? 0f : MpDiag.Num(vc, "_currentTurn"),
                                     vc != null && MpDiag.Bool(vc, "_isGrounded"),
                                     vc == null ? 0f : MpDiag.Num(vc, "_airboneTime"),
                                     local._curLap, total, local._endedRace,
                                     TriggerOf(local, "_curTrigger"), TriggerOf(local, "_curTriggerIndex"));
                SendRaw(pkt);
            }
            catch (Exception e) { Plugin.Log.LogWarning("net: send: " + e.Message); }
        }

        private static int TriggerOf(PlayerRacer r, string field)
        {
            try { return (int)MpDiag.Num(r, field); } catch { return 0; }
        }

        private static void SendHello()
        {
            try
            {
                SendRaw(EncodeHello((byte)(_server ? 0 : SlotUnassigned), MpFlow.Nickname,
                                    MpFlow.CharacterIdx, MpFlow.SkinIdx, MpFlow.VehicleIdx));
            }
            catch { }
        }

        private static void SendPing()
        {
            try
            {
                if (_sock == null) return;
                var pkt = new byte[LenPing];
                pkt[0] = TypePing; pkt[1] = Proto; pkt[2] = (byte)(_server ? 0 : SlotUnassigned);
                PutU(pkt, 3, _clientId);
                PutF(pkt, 7, Time.realtimeSinceStartup);
                if (_server)
                {
                    foreach (var p in Peers)
                    {
                        if (p.Endpoint == null) continue;
                        p.PingSentAt = Time.realtimeSinceStartup;
                        try { _sock.SendTo(pkt, p.Endpoint); } catch { }
                    }
                }
                else SendRaw(pkt);
            }
            catch { }
        }

        private static void SendInfoTo(Peer p)
        {
            try
            {
                if (p == null || p.Endpoint == null || _sock == null) return;
                _sock.SendTo(EncodeInfo((byte)p.Slot, p.Nick, p.Character, p.Skin, p.Vehicle, (int)p.RttMs), p.Endpoint);
            }
            catch { }
        }

        /// <summary>Host: tell every client who is in the session and how the ping is.</summary>
        private static void BroadcastInfo()
        {
            try
            {
                if (!_server || _sock == null) return;
                var list = Peers;
                foreach (var p in list)
                {
                    if (p.Endpoint == null) continue;
                    var me = EncodeInfo(0, MpFlow.Nickname, MpFlow.CharacterIdx, MpFlow.SkinIdx, MpFlow.VehicleIdx, 0);
                    try { _sock.SendTo(me, p.Endpoint); } catch { }
                    foreach (var q in list)
                    {
                        if (q == p || q.Endpoint == null) continue;
                        var row = EncodeInfo((byte)q.Slot, q.Nick, q.Character, q.Skin, q.Vehicle, (int)q.RttMs);
                        try { _sock.SendTo(row, p.Endpoint); } catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>Tell every client to start the same race locally.</summary>
        internal static void BroadcastRaceStart(string map, int laps, int itemType)
        {
            try
            {
                if (!_server) return;
                if (PeerCount == 0)
                {
                    Plugin.Log.LogWarning("net: race start broadcast has no peers - a joined player stays in the menu"
                                          + " (the kart channel has not heard from anybody yet)");
                    return;
                }
                _pendingStart = EncodeStart(0, map, laps, itemType);
                _pendingStartCount = 0;
                _pendingStartLeft = 0f;
                Plugin.Log.LogInfo("net: broadcasting race start map='" + map + "' laps=" + laps
                                   + " items=" + itemType + " to " + PeerCount + " peer(s)");
            }
            catch (Exception e) { Plugin.Log.LogWarning("net: broadcast start: " + e.Message); }
        }

        /// <summary>Host: the race is over, or this machine is leaving.</summary>
        internal static void BroadcastRaceEnd(int reason)
        {
            try
            {
                if (!_server) return;
                var pkt = new byte[LenEnd];
                pkt[0] = TypeEnd; pkt[1] = Proto; pkt[2] = 0;
                PutU(pkt, 3, 0);
                pkt[7] = (byte)reason;
                for (int i = 0; i < 3; i++) SendRaw(pkt);
                Plugin.Log.LogInfo("net: race end broadcast (reason " + reason + ") to " + PeerCount + " peer(s)");
            }
            catch { }
        }

        // -------------------------------------------------- automatic lifecycle
        private static void TryAutoStart()
        {
            try
            {
                bool hosting = MpFlow.IsHosting;
                bool joined = MpFlow.IsClient;
                string addr = MpFlow.Address ?? "";

                if (hosting && (!_wasHosting || _port != MpFlow.Port + 1))
                {
                    _wasHosting = true; _wasClient = false;
                    StartHost(MpFlow.Port);
                    return;
                }
                if (joined && (!_wasClient || _lastHost != addr))
                {
                    _wasHosting = false; _wasClient = true; _lastHost = addr;
                    StartClient(addr, MpFlow.Port);
                    return;
                }
                // Deliberately not torn down the moment the Mirror session drops: a
                // Mirror timeout during a slow scene load used to kill the channel
                // mid-race. Only a real leave (StopAll) or a long idle does.
                if (!hosting && !joined && (_wasHosting || _wasClient))
                {
                    _graceLeft -= 0.5f;
                    if (_graceLeft <= 0f) { _wasHosting = _wasClient = false; Stop(); }
                }
                else _graceLeft = 120f;
            }
            catch { }
        }

        // ----------------------------------------------------------------- bytes
        private class KartState
        {
            internal int Slot;
            internal Vector3 Pos;
            internal Quaternion Rot;
            internal float Speed, Steer, Air, TotalTime;
            internal int Lap, Trigger, TriggerIndex;
            internal bool Grounded, Finished;
        }

        private static byte[] EncodeKart(byte slot, Vector3 pos, Quaternion rot, float speed, float steer,
                                         bool grounded, float air, int lap, float total, bool finished,
                                         int trigger, int triggerIndex)
        {
            var b = new byte[LenKart];
            b[0] = TypeKart; b[1] = Proto; b[2] = slot;
            PutU(b, 3, _clientId);
            PutF(b, 7, pos.x); PutF(b, 11, pos.y); PutF(b, 15, pos.z);
            PutF(b, 19, rot.x); PutF(b, 23, rot.y); PutF(b, 27, rot.z); PutF(b, 31, rot.w);
            PutF(b, 35, speed);
            PutF(b, 39, steer);
            b[43] = grounded ? (byte)1 : (byte)0;
            PutF(b, 44, air);
            PutF(b, 48, lap);
            PutF(b, 52, total);
            b[56] = finished ? (byte)1 : (byte)0;
            PutF(b, 57, trigger);
            PutF(b, 61, triggerIndex);
            return b;
        }

        private static KartState DecodeKart(byte[] b)
        {
            if (b.Length < LenKart) return null;
            var st = new KartState();
            st.Slot = b[2];
            st.Pos = new Vector3(GetF(b, 7), GetF(b, 11), GetF(b, 15));
            st.Rot = new Quaternion(GetF(b, 19), GetF(b, 23), GetF(b, 27), GetF(b, 31));
            if (Mathf.Abs(st.Rot.x) < 1e-6f && Mathf.Abs(st.Rot.y) < 1e-6f
                && Mathf.Abs(st.Rot.z) < 1e-6f && Mathf.Abs(st.Rot.w) < 1e-6f) st.Rot = Quaternion.identity;
            st.Speed = GetF(b, 35);
            st.Steer = GetF(b, 39);
            st.Grounded = b[43] != 0;
            st.Air = GetF(b, 44);
            st.Lap = (int)GetF(b, 48);
            st.TotalTime = GetF(b, 52);
            st.Finished = b[56] != 0;
            st.Trigger = (int)GetF(b, 57);
            st.TriggerIndex = (int)GetF(b, 61);
            return st;
        }

        private static byte[] EncodeHello(byte slot, string nick, int character, int skin, int vehicle)
        {
            var b = new byte[LenHello];
            b[0] = TypeHello; b[1] = Proto; b[2] = slot;
            PutU(b, 3, _clientId);
            b[7] = (byte)Mathf.Clamp(character, 0, 255);
            b[8] = (byte)Mathf.Clamp(skin, 0, 255);
            b[9] = (byte)Mathf.Clamp(vehicle, 0, 255);
            WriteString(b, 10, 32, nick);
            return b;
        }

        private static byte[] EncodeStart(byte slot, string map, int laps, int itemType)
        {
            var b = new byte[LenStart];
            b[0] = TypeStart; b[1] = Proto; b[2] = slot;
            PutU(b, 3, 0);
            b[7] = (byte)Mathf.Clamp(laps, 0, 255);
            b[8] = (byte)Mathf.Clamp(itemType, 0, 255);
            WriteString(b, 9, 31, map);
            return b;
        }

        private static byte[] EncodeInfo(byte slot, string nick, int character, int skin, int vehicle, int pingMs)
        {
            var b = new byte[LenInfo];
            b[0] = TypeInfo; b[1] = Proto; b[2] = slot;
            PutU(b, 3, 0);
            b[7] = (byte)Mathf.Clamp(character, 0, 255);
            b[8] = (byte)Mathf.Clamp(skin, 0, 255);
            b[9] = (byte)Mathf.Clamp(vehicle, 0, 255);
            b[10] = (byte)(pingMs & 0xFF);
            b[11] = (byte)((pingMs >> 8) & 0xFF);
            WriteString(b, 12, 32, nick);
            return b;
        }

        private static void WriteString(byte[] b, int o, int max, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            Array.Copy(bytes, 0, b, o, Math.Min(bytes.Length, max));
        }

        private static string ReadString(byte[] b, int o, int max)
        {
            try
            {
                int n = 0;
                while (n < max && o + n < b.Length && b[o + n] != 0) n++;
                return System.Text.Encoding.UTF8.GetString(b, o, n);
            }
            catch { return ""; }
        }

        private static void PutF(byte[] b, int o, float v)
        {
            var t = BitConverter.GetBytes(v);
            b[o] = t[0]; b[o + 1] = t[1]; b[o + 2] = t[2]; b[o + 3] = t[3];
        }

        private static float GetF(byte[] b, int o)
        {
            if (o + 3 >= b.Length) return 0f;
            return BitConverter.ToSingle(b, o);
        }

        private static void PutU(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v & 0xFF); b[o + 1] = (byte)((v >> 8) & 0xFF);
            b[o + 2] = (byte)((v >> 16) & 0xFF); b[o + 3] = (byte)((v >> 24) & 0xFF);
        }

        private static uint GetU(byte[] b, int o)
        {
            if (o + 3 >= b.Length) return 0;
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        // -------------------------------------------------------------- self test
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string SelfTest()
        {
            var sb = new System.Text.StringBuilder();
            Socket host = null, c1 = null;
            try
            {
                var src = new Vector3(12.5f, -3.25f, 77.125f);
                var rot = Quaternion.Euler(3f, 218f, 359f);
                var pkt = EncodeKart(SlotUnassigned, src, rot, 72.5f, -12.25f, true, 0.75f, 2, 83.5f, true, 7, 3);
                var st = DecodeKart(pkt);
                bool ok = st != null && Math.Abs(st.Pos.x - src.x) < 0.001f && Math.Abs(st.Pos.z - src.z) < 0.001f
                          && Quaternion.Angle(st.Rot, rot) < 0.01f && Math.Abs(st.Speed - 72.5f) < 0.001f
                          && Math.Abs(st.Steer + 12.25f) < 0.001f && st.Grounded && st.Lap == 2
                          && Math.Abs(st.TotalTime - 83.5f) < 0.001f && st.Finished
                          && st.Trigger == 7 && st.TriggerIndex == 3;
                sb.Append("kart len=").Append(pkt.Length).Append(" ok=").Append(ok);

                var h = EncodeHello(SlotUnassigned, "Player123", 4, 2, 1);
                var info = EncodeInfo(3, "Player123", 4, 2, 1, 42);
                bool ok2 = ReadString(h, 10, 32) == "Player123" && h[7] == 4 && h[8] == 2 && h[9] == 1
                           && ReadString(info, 12, 32) == "Player123" && (info[10] | (info[11] << 8)) == 42
                           && info[2] == 3 && info.Length == LenInfo;
                sb.Append(" | hello/info ok=").Append(ok2);

                var s = EncodeStart(0, "Assets/Scenes/forest.unity", 3, 1);
                bool ok3 = s[7] == 3 && s[8] == 1 && ReadString(s, 9, 31) == "Assets/Scenes/forest.unity";
                sb.Append(" | start ok=").Append(ok3);

                // host + one client over real sockets, one packet each way
                int port = 28777;
                host = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                host.Bind(new IPEndPoint(IPAddress.Loopback, port));
                c1 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                c1.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                var hostEp = new IPEndPoint(IPAddress.Loopback, port);
                var p1 = EncodeKart(SlotUnassigned, src, rot, 70f, 1f, true, 0f, 1, 12f, false, 1, 2);
                c1.SendTo(p1, hostEp);
                Thread.Sleep(60);
                var buf = new byte[128];
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int n = host.ReceiveFrom(buf, ref from);
                var c1Ep = (IPEndPoint)from;
                buf[2] = 2;                                     // the host stamps the slot
                host.SendTo(buf.AsSpan(0, n).ToArray(), c1Ep);
                Thread.Sleep(60);
                var rx = new byte[128];
                EndPoint none = new IPEndPoint(IPAddress.Any, 0);
                c1.ReceiveTimeout = 500;
                int rn = 0;
                try { rn = c1.ReceiveFrom(rx, ref none); } catch (SocketException) { }
                var back = rn >= LenKart ? DecodeKart(rx) : null;
                sb.Append(" | relay n=").Append(rn).Append(back != null && back.Slot == 2 ? " ok" : " FAIL");
                return "net self test: " + sb;
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                return "net self test failed: " + inner.GetType().Name + ": " + inner.Message + " | " + sb;
            }
            finally
            {
                try { if (host != null) host.Close(); } catch { }
                try { if (c1 != null) c1.Close(); } catch { }
            }
        }
    }
}
