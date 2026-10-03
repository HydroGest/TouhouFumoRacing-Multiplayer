# FumoMP — online multiplayer for *Touhou Fumo Racing 021*

The original game ships an "Online" menu that never worked: its netcode registers no
racers, so nothing moves and the mode is unusable. FumoMP is a BepInEx 6 plugin that
adds a working multiplayer mode on top of the untouched game: **host a room, share your
IP, a friend joins, both race on the same track, each seeing the other's kart — with that
player's own character, a nameplate above it, and a live standings board.**

This repository contains the plugin source only. No game files, no decompiled dumps.
The ready-to-play build is attached to the [releases](../../releases) page.

---

## Status

| | |
|---|---|
| Game build targeted | *Touhou Fumo Racing 021* (the `0.2.1` build — Unity 2022.1.23f1, IL2CPP x64, Windows) |
| Plugin loader | BepInEx 6.0.0-be.788 (IL2CPP) + HarmonyX |
| Current version | `0.6.0` — published as a **pre-release**, see below |
| Wire protocol | must match on every machine (0.4.x cannot talk to 0.5.0+; 0.5.0 and 0.6.0 do, 0.5.0 simply ignores weapon packets) |
| Players per room | up to 8 (protocol limit); developed and tested with 2 |

### What is verified

Verified on a two-instance rig (two copies of the game on one machine, one hosting and one
joining, both driven by automated input at ~3 fps software rendering):

* Host/join over a custom UDP channel, including the race-start handshake
  (the host starts the race, the client follows into the track).
* Remote players appear as ghost karts wearing **their own character and kart**,
  with a world-space **nameplate** showing their nickname.
* A **live standings board** (top right), sorted by lap → checkpoint → time, with the
  distance to the kart ahead.
* Network counters and per-ghost state lines in the log (`net: …`, `ghost: …`, `rank: …`).
* The airborne-rotation fix and the game's end-of-race overlay removal.

### What is *not* verified

* **Versions 0.5.0 and 0.6.0 have not been run at all.** They add kart-vs-kart collision,
  the networked shove and networked weapon hits — reasoning and decompilation work, not
  measured behaviour.
* Version 0.4.2 was also never run: it is a code-only fix for one bug found in a player
  log (the plugin's own results panel was hidden by its own end-screen cleaner, which made
  it flash by).
* Second race without restarting the game (added in 0.4.1) — implemented, untested.
* Anything beyond LAN / same-machine: internet play is expected to work over a VPN such as
  Tailscale, but has not been tested. There is no NAT punch-through and no relay server.
* Rooms with more than two players, and sessions longer than a few minutes.

### Known limitations

* **Weapon hits (0.6.0) are attacker-authoritative and only partly visible.** The projectile
  exists on the shooter's machine only, so the victim sees the *effect* (kart frozen, their
  character hidden for the damage duration, restored automatically) but not the shot flying
  in. A victim can therefore be hit "out of nowhere". Damage that the game itself would
  refuse (invulnerability window, an item that blocks damage — `PlayerItems.CanTakeDamage`)
  is still refused, which is intended.
* **Bumping is new and unmeasured (0.5.0).** Karts are blocked by each other and both sides
  get a shove, but the strength values are guesses; `FumoMP.nobump` turns the shove off.
* **Lap and position counting is local to each machine.** The game's own `FRNetworkRacer`
  does sync lap/checkpoint/damage, but this build's race prefabs carry no `NetworkIdentity`,
  so that path is unused. Only the *display* of the standings is shared, not the results.
* **The in-race HUD lap total is whatever the track default is.** The lobby's lap setting is
  ignored by the game (writing the game-mode field does not change the HUD).
* **A player who joins mid-race watches that race and plays the next one.**
* **Damage is disabled in multiplayer** (the game has no netcode for it; the plugin refuses
  it deliberately rather than desyncing).
* **One local player per machine.** Faking extra local players makes the game hang in its
  own start sequence, so each machine gets exactly one real kart and everybody else is a ghost.
* If a character has no separate kart model, the ghost keeps the local kart mesh
  (the character itself is still correct).

---

## Playing with a friend

Both machines need the same package from [releases](../../releases).

1. **Host** — start the game, open the lobby with the **FumoMP Lobby** button in the
   bottom-right corner (or press **F8**). Fill in a name, pick a character/map, press **Host**.
   The lobby then shows a *“Friends connect to:”* block: give your friend the **LAN** address
   for a local network, or the **VPN** one (e.g. Tailscale `100.x.y.z`).
2. **Guest** — start the game, open the lobby, type a name, put the host's address in
   **HOST IP**, press **Join**. The status line turns into "connected to the host, waiting
   for the host to start the race".
3. **Host** — press **Start race** (or the **Keypad Enter** key). Both machines load the same
   track at the same time.
4. Race. Each machine sees the other player as a kart with their character and name above it,
   and shows the standings in the top-right corner.

**Firewall:** allow **two** inbound UDP ports on the host:

| Port | Used by |
|---|---|
| `7777` | the game itself (connection, lobby roster, ping) |
| `7778` | kart positions — this plugin (game port + 1) |

**Over the internet:** the simplest option is a VPN such as Tailscale (no port forwarding);
otherwise forward both UDP ports on the host's router.

---

## Install

Grab `FumoMP-v<version>-win64.zip` from [releases](../../releases) and either

* run `install.bat` (menu-driven install/uninstall; it finds the game copies it can see and
  also accepts a folder path you give it), or
* extract the archive into the folder that contains `TouhouFumoRacing.exe` — the archive
  already has the right layout (`winhttp.dll`, `doorstop_config.ini`, `BepInEx/`, `dotnet/`).

The package bundles BepInEx 6.0.0-be.788 and the .NET runtime it needs, so no separate
BepInEx install is required. The bundled README and installer UI are in Chinese; this
document is the English one.

---

## Building from source

You need the .NET 6 SDK and a copy of the game.

```sh
git clone https://github.com/HydroGest/TouhouFumoRacing-Multiplayer
cd TouhouFumoRacing-Multiplayer
dotnet build FumoMP/FumoMP.csproj -p:GameDir="/path/to/TouhouFumoRacing" -p:InteropDir="/path/to/DummyDll"
```

* `GameDir` is the folder holding `TouhouFumoRacing.exe` and `BepInEx/` — the build takes
  `BepInEx.Core`, `BepInEx.Unity.IL2CPP`, `Il2CppInterop.*` and `0Harmony` from there.
* `InteropDir` must contain the **DummyDll** assemblies for this game, produced by
  [Il2CppDumper](https://github.com/Perfare/Il2CppDumper) run against the game's
  `GameAssembly.dll` + `global-metadata.dat` (GameAssembly.dll is next to the executable,
  the metadata file under `TouhouFumoRacing_Data/il2cpp_data/Metadata/`).
  Those DLLs are generated from the game and are intentionally **not** in this repository.

The build fails early with a readable message if either path is wrong.

Output: `FumoMP/bin/Debug/FumoMP.dll`. Drop it into `<game>/BepInEx/plugins/`.

---

## How it works

The game's own netcode is not usable for kart positions. In this build Mirror exposes only
generic handler APIs (`NetworkServer.RegisterHandler<T>`, `SendToAll<T>`) with no raw message
ids, and IL2CPP only compiled the message types the game itself uses — so a new message type
cannot be packed at runtime. The race prefabs have no `NetworkIdentity`/`NetworkTransform`
either: the author's design was "every machine simulates every car locally", which would
require every remote player to be a real local player (each with its own camera, HUD and input
device — i.e. splitscreen). Forcing extra local players makes the game hang in its own start
sequence.

So FumoMP does two things:

**1. Its own UDP channel** (`MpNet.cs`) on **game port + 1**. The host listens on `0.0.0.0:7778`,
assigns a slot to whoever says hello first (host = 0, then 1, 2, …), and relays everybody's
kart to everybody else. Clients send their own kart ~20 times per second. Peers are keyed by a
stable client id (not by endpoint, so a client whose ephemeral port changes keeps its slot),
expire after 30 s of silence, and a receive watchdog restarts the channel if it goes quiet.

**2. Ghost karts** (`MpGhost.cs`). A remote player is a **clone of your own kart** with all the
gameplay parts removed (`VehicleCharacter`, `PlayerRacer`, `PlayerRacingController`,
`HealthComponent`, `PlayerItems`, every collider) — so it cannot hit you, take damage, or touch
race state. It is a plain transform driven by the network, interpolated between the two most
recent samples and rendered ~0.12 s in the past. The two model nodes under the clone are
replaced with the remote player's own character and kart (looked up through the game's
`CharacterData` assets), its effects children (`DriftFX`, `DirtFX`, …) are kept, and a
world-space TMP nameplate is attached above it, billboarded to the local camera and hidden
beyond 90 m.

Everything else the game gets wrong in multiplayer is patched:

* `MpAirGuard.cs` — the game rotates the whole kart root while airborne, which looks like a
  tumble and never recovers. Every orientation change made while `!_isGrounded` is undone for
  that frame, `VehicleCharacter.Freeze()` no longer snaps the kart to its velocity direction,
  and if the game *thinks* you are airborne while a raycast says there is ground right under
  you and the kart is tilted, it is levelled back out against the surface normal.
* `MpGhost.cs` also owns **kart-vs-kart bumping**. A ghost keeps one body collider, so the
  local `CharacterController` is blocked by it, and a ghost keeps a *disabled*
  `VehicleCharacter` because the game's own crash response looks that component up on
  whatever it hit. Since a ghost cannot push anybody (on the other machine we are the
  ghost), the machine that feels the contact calls the game's own
  `VehicleCharacter.AddForce` on **its own** kart and sends a `hit` packet, so the other
  machine pushes its own kart the same way — each machine only ever moves its own vehicle.
* `MpWeapons.cs` — weapon hits. The shot is simulated by the machine that fired it, so the
  hit is decided there (every `Proyectile` is registered through a hook on `Init`, checked
  against the ghosts each frame, and `DestroyNow` is hooked so a shot that explodes on impact
  still counts) and the **effect** is applied by the victim: the weapon packet carries the
  direction, the damage duration read from the game's own `TriggerHurt._damageDuration` and a
  shove, and the receiver calls `HealthComponent.TakeDamage(duration)` on its own kart — the
  same call the game's `TriggerHurt` makes, so the freeze/hide/auto-restore is the original
  behaviour. Damage is otherwise blocked in multiplayer; a network hit is allowed through
  explicitly. The knockback item (`ItemKnockback`) is forwarded as a plain shove.
* `MpFlow.cs` — race lifecycle: follow the host's start, broadcast the start to peers, run the
  race, and after the finish show **our own results panel** (the game's is empty because nothing
  dismisses it). It also resets the stale `inRace`/`endedRace` flags once you are back in a menu
  scene, so a second race starts without restarting the game.
* `MpDiag.cs` — the probes that made all of the above possible, plus the routine that switches
  off the game's own post-race overlay (matched by object name *and* by the text it draws —
  and never anything under a `FumoMP*` object).
* `MpUgui.cs` / `MpMenuUI.cs` — the lobby (host/join, nickname, character, map, the address
  block you share) and the results panel, drawn with plain uGUI, plus the top-right standings.

ImGui is not used; everything is flat uGUI drawn by the plugin itself.

### Repository layout

```
FumoMP/
  Plugin.cs          entry point, Harmony setup, version
  MpFlow.cs          race lifecycle / session state machine
  MpNet.cs           the custom UDP channel (protocol v4)
  MpGhost.cs         ghost karts, model swap, nameplates
  MpRank.cs          local standings + distance-to-kart-ahead
  MpAirGuard.cs      rotation fixes and the fall watchdog
  MpUgui.cs          lobby, results panel, standings board
  MpMenuUI.cs        menu hook (the game's Online button)
  MpPatches.cs       Harmony patches (broken EnterMP, exit, input)
  MpStartDiag.cs     start-sequence diagnostics
  MpSpin.cs          rotation instrumentation (+ optional synthetic driver)
  MpDiag.cs          probes, end-screen cleanup, race-state reset
```

---

## Debug and test flags

Create an empty file in `<game>/BepInEx/plugins/` with one of these names:

| Flag | Effect |
|---|---|
| `FumoMP.debug` | verbose probes and periodic dumps in the log |
| `FumoMP.drive` | synthetic driver: drives the local kart without input hardware |
| `FumoMP.airtest` | lifts the kart and tilts it, to exercise the airborne guard |
| `FumoMP.ghosts=N` | spawn N ghost karts orbiting you (visual test, no netplay) |
| `FumoMP.character=<c>[.<skin>[.<vehicle>]]` | force the local character, for two-instance tests |
| `FumoMP.extraplayers=N` | pretend there are N participants (harness only; can hang the start) |
| `FumoMP.nettest` | run the channel's own encode/send/relay/decode self-test |
| `FumoMP.forceend` | force the end-of-race chain a while into a race |
| `FumoMP.trick` | keep the original airborne tricks (disable the air guard) |
| `FumoMP.nobump` | keep the ghost colliders but do not shove anybody (bump tuning) |
| `FumoMP.noweapons` | disable networked weapon hits entirely (debug) |

---

## Version history

* **0.6.0** — weapon hits: an attack is decided on the machine that fired it (the shot only
  exists there) and applied on the machine that got hit, through the game's own
  `HealthComponent.TakeDamage(duration)` — freeze, hide the character, auto-restore. The
  knockback item is forwarded as a shove. *Code only, not verified.*
* **0.5.0** — kart-vs-kart collision: ghosts keep one body collider (you can no longer drive
  through another player) and a new `hit` packet pushes both karts apart using the game's own
  `AddForce`. Protocol raised to 5, so **all machines must run the same version**.
  *Code only, not verified.*
* **0.4.2** — fix: the plugin's own results panel was matched by the end-screen cleaner
  (its leaves are named `Results`/`ResultsBackBtn`) and switched off right after it appeared,
  so the results flashed by. The "is this ours?" test now walks the whole ancestor chain, and a
  safety net re-shows the panel (with a log line) if anything hides it again.
* **0.4.1** — airborne rotation changes cancelled wholesale (pitch/roll too, not just yaw);
  `Freeze()` keeps the kart's orientation; raycast-based levelling when the game wrongly
  reports "airborne"; the game's post-race overlay is hidden; a second race works without
  restarting the game. *Code only, not verified on the rig.*
* **0.4.0** — ghosts wear the **remote player's** character and kart; world-space nameplates;
  live standings board from lap + checkpoint data.
* **0.3.7** — kart position sync over the custom channel (20 Hz, host relays, interpolated).
* **0.3.6** — ghost karts (a stripped clone of your own kart) become visible at all.
* **0.3.0–0.3.5** — race start/end driven by the game's own `GameMode` object instead of calling
  `StartGame` directly (which left the game mode null and broke finishing and quitting);
  rotation instrumentation and the first rotation fixes.
* **0.1.x** — first working host/join: karts spawn, keyboard input works.

---

## Credits and licensing

* Plugin code: MIT — see [LICENSE](LICENSE).
* [BepInEx](https://github.com/BepInEx/BepInEx) 6.0.0-be.788 (LGPL-2.1) and
  [HarmonyX](https://github.com/BepInEx/HarmonyX) (MIT) are bundled in the release archive;
  their licenses are their own.
* *Touhou Fumo Racing 021* and all of its assets belong to its authors. This repository
  contains no game code or assets — only patches that require you to own and install the game.
  No game files are redistributed in the release either.
* Unofficial fan project, not affiliated with the game's authors.
