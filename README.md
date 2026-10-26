# Island War

A multiplayer team game: two islands, two teams. Gather, build an industrial base, research, and
attack across the water with player-piloted planes, missiles and ships. Win by delivering a nuke
to the enemy's industrial core. Think old modded Minecraft military sandbox, arcade not simulation.

The full design and build order are in [docs/game-plan.md](docs/game-plan.md). Decisions are logged
in [docs/decisions.md](docs/decisions.md), and [docs/testing.md](docs/testing.md) has the exact
commands to test each milestone.

**Status:** M1, the vertical slice, is done. Players join a dedicated server, see each other, build and
break blocks, fly a plane with arcade controls, and bomb craters into the island. The server is
authoritative, each client predicts its own movement (on foot or flying) and edits, and late joiners get
the current world. `build synctest` checks it with bots: one builds a tower, one bombs it, and every world
must end identical.

M2 (the match loop) is under way. Done so far: two mirrored islands, Red and Blue teams with their own
spawns and planes, player names, a menu to join a server by address, and the match itself: warmup,
countdown, three timed phases that unlock and grow bombs, a time limit, and a fresh island each match;
and an industrial core on each island that bombs chip and a phase-3 nuke wins the match by hitting.
`build matchtest` checks a whole match, and a nuke win, with bots.

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
./build.sh matchtest                           # automated match tests: a fast match, then a nuke win
```

On Windows use `.\build.ps1` with the same arguments. Engine commands need Godot 4.7.2 (.NET) on `PATH`
as `godot`, or the `GODOT` environment variable pointing at it.

## Layout

```
project.godot, Main.tscn, Main.cs   Godot project and entry scene (picks server or client)
IslandWar.csproj                    Godot C# assembly: Client/, Server/, Net/
Client/                             rendering, input, UI
Server/                             dedicated server entry, match orchestration
Net/                                replication (from M1b)
Sim/                                game rules: plain C#, no Godot dependency
tests/IslandWar.Sim.Tests/          xUnit tests for Sim
data/                               tunables (JSON); never hardcode these
docs/                               plan, decisions, testing
```

Game rules go in `Sim/`, which can't reference Godot, so they stay unit-testable without a renderer.
