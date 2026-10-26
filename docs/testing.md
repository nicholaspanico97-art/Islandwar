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

No server needed. The log prints `[client] local sandbox: seed 1337, ... hash 8c8109c14d2c2e39`, the
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

Starts a headless server and three headless bots on port 7790. The builder joins and builds a 5-block
tower two blocks east of the island centre. Once the tower is up, two more join late: the breaker
checks the tower is in the world it rebuilt and breaks the top two blocks, and the pilot (M1c) flies
to the plane, gets in, takes off, climbs 15 blocks and gets out. The pilot predicts every tick like a
real client and fails if the server ever corrects it by more than 0.05 blocks. Each bot then asks the
server for its world hash. Expected output ends with:

```
[bot builder] PASS: world hash <h>, server <h>
[bot breaker] PASS: world hash <h>, server <h>
[bot pilot] got out at height ...; largest correction from the server 0.0000 blocks
[bot pilot] PASS: world hash <h>, server <h>
Sync test PASSED
```

It exits 1 and prints the log folder if anything differs or a bot times out (60 s). M1d adds a bomb.

## M1c: the plane

Planes are server-side, so use a server and a client (the single-player sandbox has no plane):

```sh
./build.sh server --port=7777
./build.sh client --connect=127.0.0.1:7777
```

The server logs `plane 1 parked at ...`. The plane sits on the water just off the south beach, facing
out to sea. From the spawn, turn round (it's behind you, to the south) and walk or fly (F) to the beach.

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

