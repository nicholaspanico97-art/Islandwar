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
- The **builder** joins, builds a 5-block tower two blocks east of the island centre and places a crate
  (a structure) four blocks west of it, then waits until the tower has been both broken and bombed.
- Once the tower is up, two more join late. The **breaker** (also Red) checks the tower and the crate are
  in the world it rebuilt (late join), tries to break the builder's top block (team build protection must refuse it), then
  adds two planks of its own and breaks the upper one (allowed).
- The **pilot** flies to the plane, gets in, takes off, flies a bombing pattern back over the island,
  drops when the bomb's predicted impact is on the tower, checks the crater took tower blocks, and gets
  out. It predicts every tick like a real client and fails if the server ever corrects it by more than
  0.05 blocks.

Each bot then asks the server for its game hash (world and structures). Expected output ends with:

```
[bot builder] PASS: game hash <h>, server <h>
[bot breaker] PASS: game hash <h2>, server <h2>
[bot pilot] bombs away at height 116.0, ... blocks short of the target
[bot pilot] bomb hit (...); tower blocks 6 -> 3
[bot pilot] got out at height ...; largest correction from the server 0.0000 blocks
[bot pilot] PASS: game hash <h>, server <h>
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
parked at ...`. Each sits on the water just off the south beach of its team's island, level with the island
centre, facing south out to sea (moved there on 2026-10-26 with the big islands). Walk there from the spawn, or start the server with `--dev` and
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
./build.sh server --port=7777 --preset=phases
./build.sh client
./build.sh client
```

`phases` runs a five-minute match (phase 2 at 1:00, phase 3 at 3:00). Since Stone Age piece 4 real matches
(`quick`, `compressed`, `full`) go by eras instead, with no planes before WWI; `phases` is creative and keeps
the timed phases for trying bombs and the nuke. Join one client to each team. Check, in order:
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

Six scenarios, one after the other:
- **flow**: a headless server on the `test` preset (a whole match in about 20 seconds) and one `watcher`
  bot per team. Each places a plank in warmup, then checks that the start reset the world (the plank
  gone, the hash matching the server's), that the stages came in order and the time limit ended it.
- **nuke** (from M2 piece 3): a server on the `nuketest` preset (phase 3 after 3 seconds, developer mode)
  with Red's `nuker` and Blue's `defender`. The nuker waits for the match to start, flies over, and drops
  Red's nuke on Blue's island; both bots must see `Red wins (NukeHit)`.
- **rejoin** (from M2 piece 4): Red's `leaver` drops out mid-match; a `returner` connects with the same
  identity token while asking for Blue under another name, and must be seated back on Red as
  `red-leaver` (developer mode would otherwise grant Blue). Then Blue's `conceder` votes to concede and
  everyone must see `Red wins (Concede)`.
- **research** (from Stone Age piece 4): a server on the `quick` preset (a real match, so research is on;
  alone on Red, so warmup never ends) and Red's `researcher`. It mines stone in reach of its spawn, one block
  per hardness, until the team has 16 Stone Age points; researches Mining and Logging; checks placing stone
  is refused (it needs Fortification). Since Bronze Age piece 2 it then puts down a depot blueprint and
  delivers its stone to it.
- **combat** (from Bronze Age piece 1): a server on the `practice` preset in developer mode (creative, so
  the bow needs nothing) with Red's `archer` and Blue's `target`. The archer flies with noclip to hover a
  few blocks from the target and shoots until it dies; the target checks it died and came back at full
  health, and the archer waits for it to reappear.
- **sail** (from Bronze Age piece 3): the `sailor` alone on Red on `practice` in developer mode. It noclips
  to the shore by its plane's mooring, puts down a dock facing the sea, asks it for a galley (creative: no
  materials), boards it, rows out for 5 s, turns, rests the oars, and gets off onto dry land. Its prediction
  must agree with the server throughout (largest correction at most 0.05 blocks).

Every bot finishes by comparing game hashes (world and structures) with the server. Expected output ends with:

```
[bot watcher] stages seen: Countdown -> phase 1 -> phase 2 -> phase 3 -> Ended
[bot watcher] PASS: game hash <h>, server <h>
...
[bot nuker] Red won by nuking the enemy island; pressing E
[bot nuker] PASS: game hash <h2>, server <h2>
[bot defender] the match ended: Red wins (NukeHit)
[bot defender] PASS: game hash <h2>, server <h2>
...
[bot returner] came back as 'red-leaver' on Red
[bot returner] the match ended: Red wins (Concede)
[bot returner] PASS: game hash <h3>, server <h3>
[bot researcher] researched mining and logging; 1 Stone Age points left
[bot researcher] placing stone was refused (needs Fortification)
[bot researcher] placed a depot blueprint at (x, y, z); delivering 17 stone
[bot researcher] the blueprint now needs 12 planks, 3 stone
[bot researcher] PASS: game hash <h4>, server <h4>
[bot archer] the target is down after 4 hits; waiting for it to come back
[bot archer] PASS: game hash <h5>, server <h5>
[bot target] respawned with 100 health
[bot target] PASS: game hash <h5>, server <h5>
[bot sailor] galley 3 launched at <x, 32, z>; going aboard
[bot sailor] rowed 25.4 blocks at up to 7.0 blocks/s; turning
[bot sailor] off the galley at <x, y, z>; largest correction from the server 0.0000 blocks
[bot sailor] PASS: game hash <h6>, server <h6>
Match test PASSED
```

It takes about a minute and a half.

## M2, piece 3: the nuke

The industrial core this piece first added was removed on 2026-10-26 (see docs/decisions.md): a nuke
landing anywhere on the enemy island wins.

```sh
./build.sh server --port=7777 --preset=phases
./build.sh client
./build.sh client
```

Join one client to each team. Check, in order:
1. The flight readout shows the nuke: `Nuke: phase 3` before it, then `NUKE READY: right click over the
   enemy island`, and `Nuke in 0:25` after a drop (`phases` has a 30-second cooldown).
2. Right click in the air in phase 3 drops the nuke: twice the size of a bomb, with a yellow band.
3. A nuke landing anywhere on the enemy island blasts a radius-9 crater and ends the match at once:
   `Red team wins` / `Red's nuke hit the Blue island.`
4. A nuke that lands in the sea only splashes, and the match carries on. One on your own island makes no
   crater (no friendly fire) and doesn't win.

## M2, piece 4: score, concede, rejoin and the scoreboard

```sh
./build.sh server --port=7777 --preset=phases
./build.sh client
./build.sh client
```

Join one client to each team (a third on Red helps with the vote). Check, in order:
1. Hold Tab: a scoreboard with both teams, each player's ping and concede votes.
2. Let the time run out (5:00 in `phases`): the banner says `Draw` / `Time's up.` (a score comes later).
3. F10 votes to concede (F10 again takes it back). With two players on a team, one vote shows
   `Your team: 1 of 2 votes to concede`; the second ends the match: `Blue team wins` / `Red conceded.`
   A team of one concedes with its single vote. Votes reset when a match starts.
4. Rejoin: quit a client mid-match and join again from the same machine (it keeps an identity token in
   `user://settings.cfg`). You're back on your team with your old name, whatever team you pick in the
   menu. The server logs `came back as '<name>' on <team>`. Seats are forgotten when the server returns
   to warmup.

## M3, piece 1: team build protection

```sh
./build.sh server --port=7777 --preset=phases
./build.sh client
./build.sh client
./build.sh client
```

Put two clients on Red and one on Blue. Check, in order:
1. A Red player builds something. The other Red player can't break it: the block stays and
   `<name> built that. Teammates' blocks are protected.` appears under the crosshair. The server logs
   `rejected ... a teammate built that`.
2. Each player can break their own blocks, natural terrain, and anything the other team built.
3. When the builder disconnects, their blocks become breakable by the team (so nothing is stuck forever).
   When they come back (same machine, so the same identity token) the blocks are theirs again.
4. In a plane, over your own half of the map the bombsight ring is white. A bomb there makes the usual
   flash but no crater, and `No friendly fire: bombs don't crater your own island.` appears; the server
   logs `landed on Red's own half: no crater`. Over the enemy half the ring is red and bombs crater as before.
5. The automated tests cover it too: the sync test's breaker hits the protection (see above), and every
   hash check now includes who owns each block.

## M3: the big islands from the sketch

```sh
./build.sh server --port=7777 --preset=practice --dev
./build.sh client
```

The map is now 1488 x 128 x 400 blocks: two 600 x 300 islands traced from `docs/island-sketch.png`,
mirror images of each other with 160 blocks of sea between them. Check, in order:
1. You spawn on open grassland in the middle of your island, in a clearing. Looking north-west you
   see the forest and the grey mountains at the far end; to the east, three rounded hills and the second
   forest, and past them the beach facing the strait.
2. Walk west (sprint off) to the mountains: about a minute from the spawn, about two minutes end to end.
   Mountain tops above sea level + 26 are bare stone with orange ore showing; ore is dense there and
   sparse (but present) under the grassland. Ore can be broken but isn't in the hotbar.
3. Trees (wood trunks, leaf crowns) stand thick in the two forests and here and there on the grassland.
   Wood and leaves are in the hotbar (slots 6 and 7).
4. Terrain streams in around you, nearest first, and fades out in fog about 220 blocks away (380 when
   flying). Fly across (F, or the plane) and the land ahead keeps loading; nothing stays missing.
5. Your plane is on the water off the south beach, in line with the spawn.
6. The bots handle the bigger map: `build synctest` and `build matchtest` pass (the bombers fly at a
   height that clears the mountains).

To watch a bot, run it in a window instead of headless: it draws the terrain around its pawn. For
example, record a nuke run:

```sh
./build.sh server --port=7801 --preset=nuketest --dev
godot --headless --path . -- --connect=127.0.0.1:7801 --bot=defender --team=blue --dev
godot --path . --resolution 960x540 --write-movie nuke.avi -- --connect=127.0.0.1:7801 --bot=nuker --team=red --dev
```

The terrain tunables are in `data/terrain.json` (outline, regions, ore, trees) and `data/view.json`
(draw distance, fog). The generator's unit tests print the generation time and the ore and tree densities:
`dotnet test tests/IslandWar.Sim.Tests --logger "console;verbosity=detailed" --filter IslandGenerator`.

## M3: foundations (server systems, structures, team state)

The server game now runs in Sim (`Sim/Server`) and is unit-tested without the engine:
`dotnet test tests/IslandWar.Sim.Tests` runs `ServerTests` and `StructureTests` (joins, rejoin, concede,
edits and protection, placing, removing and bombing structures, late join, team state privacy) with a fake
transport. The bot tests above check the same server over the network; the sync test now also places a
crate and checks a late joiner gets it.

To try structures by hand:

```sh
./build.sh server --port=7777 --preset=practice --dev
./build.sh client
./build.sh client
```

Join one client to each team. Check, in order:
1. The hotbar has three more slots after the blocks, marked with an inner frame: Crate, Depot and
   Workbench (slots 8, 9 and 0 in practice). Select one: a see-through box shows where it would go, green if it fits and red if not
   (no room, nothing to stand on, or you're standing in it). It turns with you in quarter turns.
2. Right click places it: it appears for both players, as a coloured box with a band in your team's colour
   and its name above. The server logs `peer <id> placed a Depot (object <n>) at (...) for Red`.
3. Aim at it: the prompt line shows its team, name and health. Its blocks can't be broken one by one.
   Left click takes the whole structure down; a teammate's can't be taken down while they're online.
4. Bomb it from the plane (the enemy's, over their half): each bomb takes 50 health off every structure in
   the blast, and the box darkens; the crater leaves it standing. At 0 it's destroyed, its blocks vanish
   with the crater, and the server logs `Depot <n> (Blue) was destroyed`.
5. Start a third client: it gets the structures, and its log says `plus N edits and M objects` and
   `matches the server`.
6. Hold Tab: the scoreboard's footer shows your team's structures standing and its stock (`nothing yet`
   until resources exist).

## Stone Age, piece 1: items, backpack, hold-to-mine and storage

Use a real-match preset (not practice, which is creative and builds freely):

```sh
./build.sh server --port=7777 --preset=quick --dev
./build.sh client
./build.sh client
```

Join one client to each team (the match starts after a 10-second countdown and resets the island; the
quick preset's first minute has no bombs). Check, in order:
1. The line above the hotbar says `Backpack 0 / 40`. The hotbar has only the blocks an item places (stone,
   dirt, sand, planks, log), each with a count, dimmed at 0, then the three structures.
2. Hold left click on the ground: a bar under the crosshair fills, and the block breaks when it's full
   (dirt and sand about half a second, stone 1.5 s, logs 2 s, ores 3 to 4 s). Letting go or looking away
   starts over. The item appears in the backpack line, and on the hotbar if it's placeable.
3. Right click places a block from the backpack and the count goes down. With none, `No stone in your
   backpack.` and nothing is placed.
4. Find ore: copper (orange) and coal (black) here and there, much more in the mountains, where tin (pale
   grey) and iron (rust) are. Clay (blue-grey) is in clumps in the beach sand. Ore can't be placed.
5. Fill the backpack to 40: what you mine after that is lost (the server logs `... is lost`).
6. Place a crate (slot 6) and press E on it: a window shows your backpack and the crate. `Put in` and `Take`
   move a whole stack, as much as fits; `Put everything in` empties your backpack. E, Esc or a click outside
   closes it; walking away closes it too.
7. Hold Tab: the scoreboard's footer shows your team's stock, everything in its storage added up. The other
   team's client shows only its own.
8. The other team can't open your crate (`That's the other team's storage.`). A crate with anything in it
   can't be taken down (`Empty the Crate before taking it down.`). Bomb it (from phase 2) and what it held
   is gone from the stock.
9. Quit a client mid-match and rejoin: the backpack is as you left it.

## Stone Age, piece 2: crafting, stone tools and structure costs

```sh
./build.sh server --port=7777 --preset=quick --dev
./build.sh client
./build.sh client
```

One client on each team. Check, in order:
1. Selecting a structure slot shows its cost on the prompt line, e.g. `Crate: 6 planks  (not enough)`. Right
   click with too little says `A Crate takes 6 planks.` and places nothing.
2. Mine a tree for logs, press C: the crafting window lists `4 Planks from 1 Log` with a Craft button
   (greyed when you can't afford it). Craft: the log turns into planks in your backpack.
3. Place a workbench (4 planks), aim at it: `E to craft`. E opens the window with the axe and pickaxe
   (3 stone, 2 planks each) as well as planks.
4. Hold left click on ore without a pickaxe: `You need a stone pickaxe to mine copper ore.` With one in the
   backpack, ore mines (in half the hand time for stone), and with an axe trees come down faster.
   Iron ore can't be mined yet (its pickaxe comes in the Bronze Age).
5. Put logs in a crate, empty your backpack, and craft planks: the logs come out of the crate (the
   scoreboard's stock drops). Structures are paid the same way.
6. Hold left click on your own empty crate: it comes down and its 6 planks come back into your backpack.
7. The other team's workbench answers `That's the other team's workbench.`

## Stone Age, piece 3: the furnace

```sh
./build.sh server --port=7777 --preset=quick --dev
./build.sh client
./build.sh client
```

One client on each team. Check, in order:
1. Mine stone and dig clay from the beach sand, then place a furnace (slot after the depot; 12 stone,
   4 clay). It's a tall box; aiming at it says `E to open`.
2. E opens the furnace window: your backpack on the left, with `Smelt` on things that smelt (copper and tin
   ore, logs) and `Fuel` on things that burn (logs, planks, charcoal, coal); the furnace's `To smelt`, `Fuel`
   and `Done` slots on the right, each item with `Take`.
3. Put in copper ore and no fuel: the status line says `Stopped: needs fuel`. Add a log: it says `Making
   copper ingot: 0.5 / 5 s (fuel burning 14 s)` and counts up; every 5 seconds an ingot appears in `Done`.
   The other client, opening the same furnace (same team), sees the same.
4. Logs in `To smelt` make charcoal (8 s each), which burns twice as long as a log.
5. Stone won't go in. A full `Done` slot (32) stops the furnace until it's emptied.
6. Ingots taken out and put in a crate show in the scoreboard's stock; what's in the furnace doesn't.
7. A furnace with anything in it can't be taken down; a bombed one loses everything in it.

## Stone Age, piece 4: research, eras and the Bronze Age

```sh
./build.sh server --port=7777 --preset=quick --dev
./build.sh client
./build.sh client
```

`quick` is now a 30-minute real match. Research starts again when the match starts (the island resets),
so for a long try keep one team empty: warmup never ends. Stone tools and the furnace now need research
(steps in the earlier Stone Age sections that use them come after it), and in real matches the plane is
locked until WWI. Check, in order:
1. Under your name, top right: `Stone Age: 0 research points (R)`. Chop logs or mine stone by hand: each
   block is a point for the whole team (both clients on a team see the same count; the other team's
   doesn't move). Dirt, sand and ore give none.
2. R opens the research window: `To reach the Bronze Age: research Bronze working`, then the Stone Age
   techs with cost, what each unlocks, and why it can't be researched yet (`needs 8 Stone Age points`,
   `needs Mining`). R, Esc or a click outside closes it.
3. Before Mining, the workbench says `Needs Mining research` on the stone pickaxe; before Smelting the
   furnace slot's prompt says `Furnace: needs smelting research` and right click refuses.
4. At 8 points, `Research` on Mining: the pickaxe can be crafted, and the points go down by 8. Logging the
   same for the axe; Smelting (20, after both) unlocks the furnace.
5. Bronze working costs 30 points and 6 copper ingots and 2 tin ingots (from your backpack, then the team's
   storage). Researching it shows a gold banner, `The Bronze Age`, on both of the team's clients, and the
   line under your name changes to `Bronze Age: 0 research points`.
6. In a furnace, 3 copper ingots and 1 tin ingot make 4 bronze ingots in 10 s. Before Bronze working the
   status says `Stopped: bronze ingot needs bronze working research`; with only copper, `Waiting: bronze
   ingot takes 3 copper ingot and 1 tin ingot`. Each bronze ingot made is 2 Bronze Age points.
7. Bronze Age techs: Bronze tools (bronze pickaxe and axe at the workbench, 3 bronze ingots and 2 planks;
   the bronze pickaxe mines iron ore), Fortification (placing stone blocks: until then right click with
   stone says `Placing stone: needs fortification research.`; after, each stone placed is a Bronze Age
   point) and Prospecting (two iron ore per block).
8. Walking up to the plane says `The plane needs the WWI era`, and E does nothing. The scoreboard footer
   (Tab) shows the team's era and how many techs it has.
9. A practice server (creative) has everything unlocked and shows no era; R says so.



## Bronze Age, piece 1: health, death and archery

```sh
./build.sh server --port=7777 --preset=quick --dev
./build.sh client
./build.sh client
```

One client on each team, both kept in warmup (or use `--preset=practice`, where the bow needs nothing). To
get a bow quickly in `quick`, research up to the Bronze Age and then Archery (15 Bronze Age points). Check, in
order:
1. `Health 100 / 100` bottom left, above the hotbar. The help line mentions R research.
2. After Archery, the workbench makes a bow (3 planks and a log) and arrows (a plank and a stone make 4).
   The last hotbar slot (0) is the bow; its count is your arrows. With it selected the prompt says
   `Bow: 4 arrows, left click to shoot`, and left click shoots instead of mining. Without a bow or arrows,
   a note says what's missing.
3. Arrows fly in an arc and stop at the first block. Hitting the other team's player: they lose 25 health
   and see `Hit! -25 health`; you see `Your arrow hit!`. Teammates aren't hurt. Arrows don't break blocks.
4. Four hits kill. The dead player's screen goes dark red: `You died`, `Back at your island in 5 s`. They
   can't move, build or shoot, and vanish for everyone else. After 5 s they're back at their island with
   full health.
5. What the dead player carried is in a dropped backpack where they fell (a small brown block). Anyone, of
   either team, can press E on it and take things out; nothing goes in, and it disappears once empty. It
   isn't team stock.

## Bronze Age, piece 2: building by blueprint

```sh
./build.sh server --port=7777 --preset=quick --dev
./build.sh client
```

Stay in warmup (keep the other team empty). Check, in order:
1. Select a structure in the hotbar (crate, depot, workbench, furnace once researched). The prompt says
   `Depot: right click for a blueprint, then bring 12 planks, 20 stone`. Right click puts it down even
   with an empty backpack: a pale see-through box in your team's colour, labelled `Depot (blueprint)` and
   `0% delivered`.
2. Aiming at it: `Red Depot blueprint: needs 12 planks, 20 stone, E to deliver`.
3. With some of the cost in your backpack, E delivers everything it still needs that you carry (a note
   says what went in and what's still missing). A solid block in the structure's colour fills up from the
   bottom as deliveries come in. Anything it doesn't need stays in your backpack.
4. A blueprint does nothing: E on a crate or depot blueprint delivers instead of opening storage; a
   furnace blueprint doesn't smelt; a workbench blueprint doesn't craft. Team stock leaves blueprints out.
5. The last delivery builds it: note `The depot is built!`, normal look, full health, works as before.
6. Taking a blueprint down (hold left click) gives back what was delivered so far; a built structure gives
   its whole cost as before.
7. The other team can't deliver to your blueprint (`That's the other team's ...`).
8. On `practice` (creative) structures still appear built at once.

## Bronze Age, piece 3: the dock and the galley

```sh
./build.sh server --port=7777 --preset=quick --dev
./build.sh client
```

Stay in warmup. Research up to the Bronze Age, then Shipbuilding (20 Bronze Age points). Or use
`--preset=practice` to skip straight to it (creative: the dock is built at once and E on it launches a galley
for nothing). Check, in order:
1. The dock is in the hotbar (a 3 x 6 pier). Its preview is red, with `needs open water in front`, unless
   you stand on the shore looking out to sea and the water in front of its far end is open (about 4 wide
   and 9 long). Docks build by blueprint like any structure (20 planks).
2. Aiming at the built dock: `Red Dock ... Galley: needs 30 planks, 6 logs, 2 bronze ingots, E to deliver`.
   E delivers what you carry (the backpack holds 40, so it takes more than one trip); the dock's label shows
   `Galley: N% delivered`.
3. The last delivery launches a galley into the water in front of the dock, bow out to sea: a brown hull,
   oars along both sides, a mast with a sail in your team's colour.
4. Walk to it (`Press E to board the galley`) and press E. The camera follows behind. W rows (the oars
   sweep), S backs water slowly, A/D turn (slowly even when still). The HUD shows the speed (top 7). The
   galley stops dead against land. Left and right click do nothing at sea.
5. E gets you off onto the nearest land or dock within a few blocks; in open water you drop in beside it.
6. The other team can't board your galley. Another player sees it move smoothly.
7. A new match (countdown ending) removes every galley along with the docks; the planes stay.

