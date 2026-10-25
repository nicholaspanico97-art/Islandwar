# Island War

A multiplayer team game: two islands, two teams. Gather, build an industrial base, research, and
attack across the water with player-piloted planes, missiles and ships. Win by delivering a nuke
to the enemy's industrial core. Think old modded Minecraft military sandbox, arcade not simulation.

The full design and build order are in [docs/game-plan.md](docs/game-plan.md). Decisions are logged
in [docs/decisions.md](docs/decisions.md), and [docs/testing.md](docs/testing.md) has the exact
commands to test each milestone.

**Status:** M0 done, M1a done. M0 verified locally on Windows 11: unit tests, build, headless server and two
connected clients. godot_voxel test passed. **M1a (single-player sandbox) is playable:** walk, jump, fly, break and place
blocks with a hotbar. Next: M1b, networked blocks.

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
./build.sh server --port=7777                  # headless dedicated server
./build.sh client --connect=127.0.0.1:7777     # a client, in another terminal
./build.sh client                              # single-player sandbox, no server
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
