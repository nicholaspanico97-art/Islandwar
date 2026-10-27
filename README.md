# Island War

A multiplayer team game: two islands, two teams, a 2 to 4 hour match. Gather, build and hide an
industrial base, climb a big tech tree, and attack across the water with player-piloted planes, missiles
and ships. Win by landing a nuke anywhere on the enemy island. Part Minecraft tech mod, part FPS, part
MOBA: arcade, not simulation.

The full design and build order are in [docs/game-plan.md](docs/game-plan.md). Decisions are logged
in [docs/decisions.md](docs/decisions.md), and [docs/testing.md](docs/testing.md) has the exact
commands to test each milestone.

**Status:** M2, the match loop, is done (M1, the vertical slice, before it). Players join a dedicated server, see each other, build and
break blocks, fly a plane with arcade controls, and bomb craters into the island. The server is
authoritative, each client predicts its own movement (on foot or flying) and edits, and late joiners get
the current world. `build synctest` checks it with bots: one builds a tower, one bombs it, and every world
must end identical.

M2 added: two mirrored islands, Red and Blue teams with their own spawns and planes, player names, a menu
to join a server by address, and the match itself: warmup, countdown, three timed phases that unlock and
grow bombs (since replaced by eras in real matches; the creative presets keep them), a fresh island each match, a phase-3 nuke that wins by landing anywhere on the enemy island,
a concede vote, rejoining your team after a disconnect, and a Tab scoreboard. (The industrial core and its
score were removed on 2026-10-26; the time limit is a draw for now.) `build matchtest` checks a whole match, a nuke win, and a rejoin with a
concession, with bots.

M3 (economy and building) is under way. Done so far: team build protection (teammates' blocks can't be
broken by hand, and your own bombs don't crater your own half), and the big islands from the developer's
sketch (`docs/island-sketch.png`): two 600 x 300 mirrored islands with mountains full of ore, hills,
forests and grassland facing the strait, streamed in around the camera; and the foundations: the server
game moved into plain C# systems (unit-tested without the engine), generic replicated objects with
placeable structures that bombs damage and destroy, and per-team state sent only to that team.

The economy is being built era by era from the developer's draft, starting with the Stone Age. Done so far:
items, a personal backpack, hold-to-mine with block hardness, ore kinds (copper, tin, iron, coal) and clay,
team storage whose contents make the team's stock, crafting by hand and at a workbench, stone tools, and
structures that cost materials, a furnace that smelts copper and tin into ingots over time with fuel, and
per-team research (R) that moves each team through the eras: Stone Age techs unlock the stone tools, the
furnace and bronze, and Bronze working takes a team into the Bronze Age (bronze tools, stone fortifications,
prospecting). Real matches now go by eras instead of timed phases, so planes wait for WWI.

The Bronze Age has started: players have health, die and respawn (dropping what they carried), and Archery
gives a bow and arrows that hurt the other team. Structures are now built by blueprint: placing one is
free, and players haul its cost to it. Shipbuilding gives a dock at the shore that launches galleys once
players have hauled the materials, and a galley can be rowed by one player. A blast furnace (researched, then
built) takes a team into the Iron Age, where iron is smelted and Smithing gives iron tools. Next: the rest of
the Iron Age (reinforced structures, ballista and catapult, the war galley), when the developer is ready.

## Versions

| Tool | Version | Status |
| --- | --- | --- |
| Godot | 4.7.2-stable, .NET build (`4.7.2.stable.mono.official.ed1daf0bf`) | Verified: server and clients run. Still the latest stable on 2026-10-25. |
| .NET SDK | 8.0 (`net8.0`) | Verified: builds and tests pass with 8.0.132 (Linux) and 8.0.427 (Windows). |
| Physics | Jolt (built into Godot) | Enabled in `project.godot`. |
| godot_voxel | v1.7x, GDExtension edition | Verified: renders and edits from C#. Not committed; `build deps` downloads it. |
| xUnit | 2.9.2 | Sim unit tests. |

## Quick start

```sh
./build.sh deps                                # once per checkout: download godot_voxel into addons/
./build.sh test                                # Sim unit tests, no engine needed
./build.sh server --port=7777                  # headless dedicated server (add --preset=full, --dev)
./build.sh client --connect=127.0.0.1:7777     # a client, in another terminal
./build.sh client                              # the menu: join a server by address, or the sandbox
./build.sh synctest                            # automated sync test: server + three bots
./build.sh matchtest                           # automated match tests: a fast match, a nuke win, a rejoin
```

On Windows use `.\build.ps1` with the same arguments. Engine commands need Godot 4.7.2 (.NET) on `PATH`
as `godot`, or the `GODOT` environment variable pointing at it.

## Layout

```
project.godot, Main.tscn, Main.cs   Godot project and entry scene (picks server or client)
IslandWar.csproj                    Godot C# assembly: Client/, Server/, Net/
Client/                             rendering, input, UI
Server/                             dedicated server host: network port, ticks the Sim server
Net/                                replication (from M1b)
Sim/                                game rules and the server game (Sim/Server): plain C#, no Godot dependency
tests/IslandWar.Sim.Tests/          xUnit tests for Sim
data/                               tunables (JSON); never hardcode these
docs/                               plan, decisions, testing
```

Game rules go in `Sim/`, which can't reference Godot, so they stay unit-testable without a renderer.
