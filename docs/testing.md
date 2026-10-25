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

### Automated bot sync test

Not built yet. It arrives with M1b (see docs/game-plan.md). The Sim test
`LateJoinerRebuildsTheSameWorldFromSeedAndDeltas` already covers the world-state half: build a
tower, crater it, and check that a world rebuilt from seed + deltas has the same hash.

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
