# Handoff: M0 continues on a local machine

From: a cloud Claude Code session (2026-10-23). To: a Claude Code session running on the developer's
own computer, with a screen. Read `docs/game-plan.md` first; it's the source of truth. This file only
says where things stand and what to do next.

## Why this moved to a local session

The cloud container's network policy blocked downloading the Godot engine (github.com release
downloads were denied), so nothing has been run inside Godot yet. The plan also expects M0 and M1 to
run locally, because they end with "launch two clients and play".

## Getting the code

The only branch is `claude/sleepy-ritchie-cc495m` (there is no `main` yet):

```sh
git clone https://github.com/nicholaspanico97-art/Islandwar.git
cd Islandwar
git checkout claude/sleepy-ritchie-cc495m
```

Ask the developer whether to keep working on that branch or create `main` from it before
committing anything.

## Done, and how sure we are

Verified in the cloud session:
- `docs/game-plan.md` saved verbatim (plan step 1).
- `docs/decisions.md` seeded with every [REV] decision plus the M0 decisions.
- `Sim/` (plain C#, no Godot): chunked `VoxelWorld`, `ChunkDeltas` (edits per chunk, for late join),
  `IslandGenerator` (seeded, integer-only math), `Craters.Blast`. Settings in `data/terrain.json`.
- `tests/IslandWar.Sim.Tests`: 25 xUnit tests, all passing with .NET SDK 8.0.132 (`./build.sh test`).
- The Godot C# assembly (`IslandWar.csproj`, `Godot.NET.Sdk/4.7.2`) **compiles** via `dotnet build`.

Written but **never run in the engine**. Expect small fixes here:
- `project.godot` and `Main.tscn` were written by hand. Godot may rewrite them on first open
  (e.g. adding a `uid`); commit whatever it produces.
- `Main.cs` picks `Server/ServerMain.cs` or `Client/ClientMain.cs` from the arguments after `--`
  (`--server`, `--port=N`, `--seed=N`, `--connect=HOST[:PORT]`; parsed in `LaunchArgs.cs`).
- The server loads `res://data/terrain.json` with `Godot.FileAccess`, generates the island, logs its
  hash, and listens with ENet on port 7777. The client shows a status label and connects.
- `build.sh` and `build.ps1` haven't been tried with a real Godot binary.

## What to do, in order

1. **Toolchain (plan step 2).** Check for, or help the developer install:
   - Godot **4.7.2-stable, .NET ("mono") build**. This is the latest stable tag at the time of
     writing; check for a newer 4.7.x patch, and if you pick a different version, update
     `IslandWar.csproj` (`Godot.NET.Sdk/<version>`), the README and `docs/decisions.md`.
   - .NET 8 SDK (`dotnet --version`).
   - Report the exact versions to the developer, and pin them in the README's Versions table.
2. **Launch check (plan step 3, part 1).** Follow `docs/testing.md` → "M0": unit tests, build, open
   once in the editor, headless server, two clients connecting. Fix whatever breaks. Mark the README
   status and the Godot decision line as verified.
3. **godot_voxel + C# test, one day max (plan step 3, part 2).** What the cloud session learned from the
   godot_voxel docs (v1.7, released 2026-08-20; see its `doc/source/getting_the_module.md`):
   - **GDExtension edition:** works with the official Godot build (4.4.1 or later). C# can only use it
     through `ClassDB.Instantiate`, `Call`, `Get` and `Set` by string name, with no type checking. Try
     this first, because it needs no engine build. Check that a GDExtension release exists for v1.7
     and supports Godot 4.7.
   - **Module edition:** needs Godot compiled from source with the module, and the C# SDK generated
     and served from a local NuGet feed. Heavy; only worth it if the GDExtension route fails and the
     developer wants it.
   - **Fallback:** a small blocky chunk mesher in C# that reads `Sim/Voxels/VoxelWorld` directly.
     The map is two bounded islands, so this is a modest job.
   - Pass criteria: a voxel terrain renders, and C# can place and break blocks in it. Note how painful
     the string-name API felt.
   - Already decided: `Sim/Voxels/VoxelWorld` is the authoritative block data, and the renderer is a
     view of it. So the test is really "is godot_voxel a good renderer for our data?" If it turns out
     godot_voxel must own the data, raise that with the developer and update `decisions.md`.
   - Record the outcome as one line in `docs/decisions.md`, replacing the PENDING line.
4. **M1a, local sandbox (plan step 4).** One island from `IslandGenerator`, rendered. Player
   controller: walk, jump, place a block, break a block. Single player. Block edits go through
   `VoxelWorld.ApplyEdit` so M1b can replicate them. Keep the UI to a crosshair and hotbar.
   **Stop and demo** to the developer when it works; only start M1b after they've played it.

## Conventions to keep

- Game rules go in `Sim/`, which must not reference Godot. Add unit tests there for new rules.
- Tunables live in `data/` (JSON), never hardcoded.
- Commit small and often, as `M0: ...` or `M1: ...`.
- Log every decision in `docs/decisions.md`, one line each, with date and reason.
- Add the "how to test this" steps for each milestone to `docs/testing.md`.
- Look up Godot and godot_voxel APIs rather than guessing; both move.
