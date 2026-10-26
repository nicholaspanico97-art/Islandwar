# How to test

Every milestone adds a section here with the exact commands. Run them from the repository root.

You need:
- .NET 8 SDK (`dotnet --version` should print 8.x).
- Godot 4.7.2-stable, **.NET build** (the zip with `mono` in its name). Put it on `PATH` as `godot`, or set `GODOT` to the binary,
  e.g. `export GODOT=~/bin/Godot_v4.7.2-stable_mono_linux.x86_64` (Windows: `$env:GODOT = "C:\Godot\Godot_v4.7.2-stable_mono_win64.exe"`).
- godot_voxel in `addons/zylann.voxel`. It isn't committed: fetch it once per checkout with
  `./build.sh deps`, then do the first launch below so Godot registers it.

On Windows, use `.\build.ps1` wherever this page says `./build.sh`.

## M0: project skeleton

### Unit tests (no engine needed)

```sh
./build.sh test
```

Expected: `Passed!` with 0 failed.

### Compile everything

```sh
./build.sh build
```

The first build downloads the Godot C# SDK from NuGet.

### First launch in the editor

Open the project once in the Godot editor (`godot --path . --editor`, or import `project.godot` from the
project manager) so it creates the `.godot/` import cache. Close it again. You only need to do this once
per checkout. Without a window, `godot --headless --path . --import` does the same; it ends with a harmless
`Unable to start the timer` error from Godot's own C# editor tools.

Godot may re-indent C# files with tabs when it imports them. Revert that (`git checkout -- '*.cs'`); the
code uses spaces. Do commit the `.cs.uid` files it creates.

### Headless server

```sh
./build.sh server --port=7777
```

Expected output:

```
[server] generated island: seed 1337, 128x64x128 blocks, hash <16 hex digits>
[server] listening on port 7777
```

Leave it running.

### Clients

In two more terminals:

```sh
./build.sh client --connect=127.0.0.1:7777
```

Each client window shows `Connected to 127.0.0.1:7777 as peer <id>.`, and the server logs
`[server] peer <id> connected` once per client. Closing a client logs `peer <id> disconnected`.

### Smoke test without a window

Godot's `--quit-after <frames>` exits on its own, which is handy in scripts:

```sh
godot --headless --path . --quit-after 120 -- --server
```

## M1a: local sandbox

```sh
./build.sh client
```

Click **Sandbox** in the menu. No server needed. The log prints `[client] local sandbox: seed 1337, ... hash 8c8109c14d2c2e39`, the
same hash as the server. Add `--seed=N` for a different island.

Check, in order:
1. You spawn standing on the hilltop, with the mouse captured, a crosshair, a help line and a hotbar
   of five blocks along the bottom.
2. WASD walks, Shift sprints, Space jumps. A one-block step can be climbed by jumping; holding Space
   keeps hopping. Walking off the beach into the sea drops you to the seabed.
3. A thin dark outline marks the block under the crosshair when it's within reach (6 blocks).
4. Left click breaks that block. The bottom layer (y = 0) can't be broken.
5. 1-5 or the mouse wheel picks a block; the selected slot gets a white border. Right click places it
   against the face you're aiming at. You can't place a block inside yourself (look straight down and
   right click: nothing happens).
6. F toggles flying: Space and Ctrl go up and down, collisions are off, and you can place blocks
   anywhere in reach. F again drops you back to walking.
7. Fall off the map's edge (fly out, press F) and you respawn on the hilltop.
8. Esc frees the mouse; clicking the window captures it again without breaking anything.

To check rendering with nobody at the keyboard, record a few seconds and pull out the last frame:

```sh
godot --path . --resolution 1280x720 --write-movie shot.avi --quit-after 240
ffmpeg -sseof -0.2 -i shot.avi -frames:v 1 shot.png
```

## M1b: networked blocks

### Server and two clients

```sh
./build.sh server --port=7777
./build.sh client --connect=127.0.0.1:7777
./build.sh client --connect=127.0.0.1:7777
```

Each client logs `rebuilt the world: seed 1337 plus N edits, hash ... matches the server`, then
`Playing on 127.0.0.1:7777 as peer <id>`. Check, in order:
1. Each window shows the other player as a coloured box with a head, a dark visor on the side they're
   facing, and a `Player <id>` label. They move smoothly and tilt their head when that player looks up or down.
2. Your own movement feels exactly like the sandbox: no lag, no rubber-banding.
3. A block broken or placed in one window appears in the other within a blink. The server logs
   `peer <id> set (x, y, z) to <Block>` for each.
4. You can't place a block inside the other player: the block you predicted disappears again, and the
   server logs `rejected ... can't place there`.
5. **Late join:** build something, then start a third client. It shows everything built so far, and its
   log says `plus N edits` with N > 0 and `matches the server`.
6. Close a client: its avatar disappears from the other window, and the server logs `peer <id> disconnected`.

### Automated bot sync test

```sh
./build.sh synctest
```

The plan's sync test: bot A builds a tower, bot B bombs it, and every world must end identical. It
starts a headless server (developer mode, `practice` preset, so no match starts and resets the world) and
three headless bots on port 7790:
- The **builder** joins and builds a 5-block tower two blocks east of the island centre, then waits
  until the tower has been both broken and bombed.
- Once the tower is up, two more join late. The **breaker** checks the tower is in the world it
  rebuilt (late join) and breaks the top two blocks.
- The **pilot** flies to the plane, gets in, takes off, flies a bombing pattern back over the island,
  drops when the bomb's predicted impact is on the tower, checks the crater took tower blocks, and gets
  out. It predicts every tick like a real client and fails if the server ever corrects it by more than
  0.05 blocks.

Each bot then asks the server for its world hash. Expected output ends with:

```
[bot builder] PASS: world hash <h>, server <h>
[bot breaker] PASS: world hash <h2>, server <h2>
[bot pilot] bombs away at height 52.0, ... blocks short of the tower
[bot pilot] bomb hit (...); tower blocks 3 -> 0
[bot pilot] got out at height ...; largest correction from the server 0.0000 blocks
[bot pilot] PASS: world hash <h>, server <h>
Sync test PASSED
```

The builder's and pilot's hashes are the same (both checked after the bomb); the breaker's is earlier.
It takes about 30 seconds, and exits 1 with the log folder if anything differs or a bot times out (90 s).

## M1c: the plane

Planes are server-side, so use a server and a client (the single-player sandbox has no plane):

```sh
./build.sh server --port=7777
./build.sh client --connect=127.0.0.1:7777
```

Since M2 each team has its own plane: the server logs `Red plane 1 parked at ...` and `Blue plane 2
parked at ...`. Each sits on the water just off the far beach of its team's island (Red's to the west,
Blue's to the east), facing out to sea. Walk there from the spawn, or start the server with `--dev` and
fly (F). Only your own team's plane lets you in.

Check, in order:
1. Near the plane, `Press E to fly the plane` appears above the hotbar. E puts you in; the view
   switches to a chase camera behind the plane and the HUD shows throttle, speed and altitude.
2. Shift raises the throttle; the propeller speeds up and the plane rolls along the water. A/D steer
   on the water; Space brakes.
3. Past the stall speed (18), hold S to lift the nose: the plane takes off. W/S pitch, A/D roll, and a
   banked plane turns by itself. The mouse looks around; let go and the view drifts back behind the tail.
4. Slow below 18 in the air and the HUD turns red with `STALL`; the plane sinks until you nose down
   or add throttle.
5. Land on the water or on flat ground: cut the throttle, descend gently, then brake with Space.
   Flying into a hillside stops the plane dead (no damage yet).
6. E gets you out beside the left wing, on the ground or in mid-air (you fall).
7. With a second client: it sees the plane move and turn smoothly, the pilot's body hidden in the
   seat, and only one player can fly it at a time.

Flight tuning lives in `data/plane.json`.

## M1d: the bomb

```sh
./build.sh server --port=7777
./build.sh client --connect=127.0.0.1:7777
./build.sh client --connect=127.0.0.1:7777
```

Fly the plane (see M1c) and check, in order:
1. On the water the HUD says `Bombs: take off first`; left click does nothing.
2. In the air a ring appears below and ahead of the plane: where a bomb dropped now would land, sized
   to the crater. Red over land, white over the sea.
3. Left click drops a bomb. The HUD shows `Bomb bay reloading...` for a second, then `Bomb ready`.
4. The bomb falls in an arc, carried forward by the plane's speed, and lands in the ring.
5. On land: a flash, a fireball, flying debris, rising smoke, a camera shake if you're near, and a
   crater (radius 4) in both windows at once. The server logs `bomb <n> hit (x, y, z): crater of N blocks`.
6. In the sea: a white splash and no crater (`splashed into the sea`).
7. The bottom block layer survives any crater.
8. Late join after a few craters: the new client sees every crater, and its log says `matches the server`.

Bomb tuning lives in `data/weapons.json`.

## M2, piece 1: two islands, two teams, names and the lobby

```sh
./build.sh server --port=7777
./build.sh client
./build.sh client
```

Add `--preset=full` to the server for the 12-a-side preset (`compressed`, 4 a side, is the default), and
`--dev` for developer mode (noclip flying, and any player may pick any team).

Check, in order:
1. A client opens on the menu: Name, Server (remembered from last time) and Team (whichever needs
   players, Red or Blue). Join, Sandbox and Quit work; Enter in a field joins.
2. A bad address, or no server running, comes back to the menu with the reason.
3. The server logs `peer <id> joined as '<name>' on Red|Blue`. The top-right corner shows your name and
   team in its colour.
4. Two islands, mirror images of each other, with 80 blocks of sea between them. Red spawns on the west
   island, Blue on the east one.
5. Other players show in their team's colour with their name above them. Two players asking for the
   same name become `Nick` and `Nick 2`.
6. Team choice is honoured unless it would put that team 2 players ahead; then you join the other team.
   A full server (4 a side for `compressed`) turns you away with `The server is full`.
7. Each team's plane has team-coloured wingtips and fin, and only that team can get in (E does nothing
   at the other team's plane).
8. Without `--dev`, F (noclip) does nothing.

Quick check of the join rules without a screen: set `max_players_per_team` to 1 in the compressed preset,
start a server, and connect three `--bot=builder --team=red --name=Nick` clients. The server should log
`Nick` on Red, `Nick 2` on Blue, and `kicking peer ...: The server is full (1 per team).`

## M2, piece 2: match clock, phases, warmup, end and reset

```sh
./build.sh server --port=7777 --preset=quick
./build.sh client
./build.sh client
```

`quick` runs a five-minute match (phase 2 at 1:00, phase 3 at 3:00); `compressed` is the real 45-minute
one. Join one client to each team. Check, in order:
1. With one team empty, the top of the screen says `Warmup: the match starts when both teams have 1`.
   Anything goes in warmup, bombs included.
2. When both teams have a player: `Match starts in 10 s`, counting down. Leaving cancels it (the server
   logs `back to warmup`).
3. At zero the island resets: everything built or blasted in warmup is gone, everyone is back on foot at
   their spawn, the planes are back on their moorings. A banner says `The match has started`.
4. Phase 1: the line reads `Phase 1 of 3: scouting, no bombs | phase 2 in 0:59`. In a plane the HUD says
   `Bombs unlock in phase 2`, there's no bombsight, and left click does nothing.
5. Phase 2 (banner `Bombs unlocked: crater radius 3`): bombs work with radius-3 craters, and the bombsight
   ring is that size. Phase 3 (banner `Heavy bombs`): radius 5.
6. At the time limit: a `Draw` banner (`The time limit was reached`; scoring comes in piece 4), then
   `next warmup in ...`, then back to warmup, and with both teams still there the countdown starts again.
7. A client joining mid-match sees the current phase and clock at once.

### Automated match test

```sh
./build.sh matchtest
```

Two scenarios, one after the other:
- **flow**: a headless server on the `test` preset (a whole match in about 20 seconds) and one `watcher`
  bot per team. Each places a plank in warmup, then checks that the start reset the world (the plank
  gone, the hash matching the server's), that the stages came in order and the time limit ended it.
- **nuke** (from M2 piece 3): a server on the `nuketest` preset (phase 3 after 3 seconds, developer mode)
  with Red's `nuker` and Blue's `defender`. The nuker waits for the match to start, flies over, and drops
  Red's nuke on Blue's core; both bots must see `Red wins (CoreDestroyed)`.

Every bot finishes by comparing world hashes with the server. Expected output ends with:

```
[bot watcher] stages seen: Countdown -> phase 1 -> phase 2 -> phase 3 -> Ended
[bot watcher] PASS: world hash <h>, server <h>
...
[bot nuker] Red won by nuking the core; pressing E
[bot nuker] PASS: world hash <h2>, server <h2>
[bot defender] the match ended: Red wins (CoreDestroyed)
[bot defender] PASS: world hash <h2>, server <h2>
Match test PASSED
```

It takes about a minute.

## M2, piece 3: industrial cores and the nuke

```sh
./build.sh server --port=7777 --preset=quick
./build.sh client
./build.sh client
```

Join one client to each team. Check, in order:
1. Each island has an orange 7 x 5 x 7 core in the middle on a stone foundation; players spawn on a ring
   around it. Under the match line: `Red core 100%    Blue core 100%`.
2. Core blocks can't be broken or placed by hand, and the hotbar has no core block.
3. In phase 2 or 3, bomb a core directly: it loses some blocks but not most (bombs only reach core blocks
   within half their crater radius). The percentage drops for both players; the server logs
   `<Team> core: N/245 blocks`.
4. The flight readout shows the nuke: `Nuke: phase 3` before it, then `NUKE READY: right click over the
   enemy core`, and `Nuke in 0:25` after a drop (`quick` has a 30-second cooldown).
5. Right click in the air in phase 3 drops the nuke: twice the size of a bomb, with a yellow band.
6. A nuke landing on (or within 3 blocks of) the enemy core blasts a radius-9 crater, takes the core to
   about 0%, and ends the match at once: `Red team wins` / `Red's nuke hit the Blue core`.
7. A nuke that misses only makes a big crater; the match carries on.

