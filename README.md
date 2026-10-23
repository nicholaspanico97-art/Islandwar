# Island War

A multiplayer team game: two islands, two teams. Gather, build an industrial base, research, and
attack across the water with player-piloted planes, missiles and ships. Win by delivering a nuke
to the enemy's industrial core. Think old modded Minecraft military sandbox, arcade not simulation.

The full design and build order are in [docs/game-plan.md](docs/game-plan.md). Decisions are logged
in [docs/decisions.md](docs/decisions.md), and [docs/testing.md](docs/testing.md) has the exact
commands to test each milestone.

**Status:** M0 (project skeleton). Not yet launched in the engine; see "Versions" below.

## Versions

| Tool | Version | Status |
| --- | --- | --- |
| Godot | 4.7.2-stable, .NET build | Chosen. C# compiles against `Godot.NET.Sdk/4.7.2`; the engine itself hasn't been run yet. |
| .NET SDK | 8.0 (`net8.0`) | Verified: builds and tests pass with 8.0.132. |
| Physics | Jolt (built into Godot) | Enabled in `project.godot`. |
| godot_voxel | v1.7 is the latest release | Pending the M0 test; a custom C# mesher is the fallback. |
| xUnit | 2.9.2 | Sim unit tests. |

## Quick start

```sh
./build.sh test                                # Sim unit tests, no engine needed
./build.sh server --port=7777                  # headless dedicated server
./build.sh client --connect=127.0.0.1:7777     # a client, in another terminal
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
