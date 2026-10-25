# Decisions

One line per decision: date, decision, reason. Newest at the bottom. "PENDING" marks a decision
that's waiting on a test.

## From the revised game plan (docs/game-plan.md)

- 2026-10-23: Pillar 8, the game must be fun at 4v4 in about 45 minutes. Reason: finding 24 players for a 3-hour match is the project's biggest risk.
- 2026-10-23: Two match presets, Full (12v12, 2–4 h) and Compressed (4v4, about 45 min), both defined in `data/`. Reason: a small match is a real mode, not a test setup.
- 2026-10-23: Drop-in/drop-out, so late joiners get the full world state, team and phase. Reason: player count. It shapes the netcode, so design for it from M1.
- 2026-10-23: Guaranteed match end: a nuke win, otherwise a score at a hard time limit, plus a concede vote. Reason: a nuke-only win with a late defence edge can stalemate.
- 2026-10-23: Team build protection before M3. Reason: one griefer must not ruin a long match for eleven people.
- 2026-10-23: The late-game defence edge stays, but attackers get counters (kill radar, overwhelm AA, escorts, decoys). Reason: avoid a permanent stalemate.
- 2026-10-23: Losing a fuel depot must hurt for real. Reason: three pillars push against snowballing, so raids risk feeling pointless.
- 2026-10-23: Start Phase 1 playtests at about 20 minutes and lengthen it only if players are still busy building. Reason: 45 minutes of scouts only may be too quiet.
- 2026-10-23: godot_voxel is provisional until the M0 test. The fallback is a custom C# chunk mesher. Reason: godot_voxel + C# integration is an unknown, and the bounded map makes the fallback cheap.
- 2026-10-23: Client-side prediction for the local character and vehicle; everything else interpolates between snapshots. Reason: server-authoritative flight feels laggy without it.
- 2026-10-23: MultiplayerSynchronizer/Spawner for M1; expect custom snapshot/delta replication by M3. Reason: 24 players, projectiles and voxel deltas will outgrow them.
- 2026-10-23: Late-join world sync is the terrain seed plus voxel edit deltas per chunk. Reason: lets a joining client rebuild the current world.
- 2026-10-23: An automated bot sync test (headless server plus scripted clients, asserting identical voxel state). Reason: Claude Code can't play the game but can run this.
- 2026-10-23: "Testable" means plain C# and unit tests, not lockstep determinism. Reason: floating-point results aren't bit-identical across machines.

## M0

- 2026-10-23: Target Godot 4.7.2-stable, .NET build. Reason: latest stable tag of godotengine/godot at setup time. **Verified 2026-10-25** on Windows 11 with .NET SDK 8.0.427: tests, build, headless server and two clients connecting all work; 4.7.2 is still the latest stable.
- 2026-10-23: .NET 8 SDK, `net8.0`. Reason: `Godot.NET.Sdk/4.7.2` builds with it; it's the LTS release Godot 4.x supports.
- 2026-10-25: godot_voxel v1.7x, GDExtension edition, renders the terrain (M0 test passed in well under a day). Our `VoxelWorld` stays authoritative: a `VoxelTerrain` with automatic loading off receives each chunk through `try_set_block_data` and each edit through `VoxelTool.set_voxel`, and builds the collision shapes. Reason: it rendered and edited from C# on the first run with no engine build. The string-name API (`ClassDB.Instantiate`/`Set`/`Call`) was tolerable: about 15 calls, all in `Client/Voxels/GodotVoxelView.cs`, and typos fail loudly at runtime. godot_voxel did not need to own the data. Fallback stays the custom C# mesher if the GDExtension (its author warns it has had little testing) causes trouble.
- 2026-10-23: The Sim voxel world (`Sim/Voxels/VoxelWorld`) is the authoritative block data; whatever renders it is a view. Reason: the server stays headless and unit-testable, voxel networking is our own code anyway, and the godot_voxel decision becomes a rendering choice only. Revisit if the M0 test shows godot_voxel needs to own the data.
- 2026-10-23: Terrain generation uses integer (fixed-point) math only. Reason: every client regenerates terrain from the seed, which must give identical blocks on every machine; floats aren't guaranteed to (see the "testable" decision above).
- 2026-10-23: Water is a flat plane at sea level, not blocks, for M1. Reason: simplest option; no fluid simulation needed for the vertical slice.
- 2026-10-23: Block ids are a `ushort` enum; new types are only ever appended. Reason: ids go over the network and into stored deltas.
- 2026-10-23: Layout: `IslandWar.csproj` (Godot assembly: Client/, Server/, Net/), `Sim/IslandWar.Sim.csproj` (plain class library), `tests/IslandWar.Sim.Tests` (xUnit). Reason: the plan's Sim/Net/Client/Server split, with the compiler enforcing that Sim has no Godot dependency.
- 2026-10-23: Game arguments come after a bare `--`: `--server`, `--port=N`, `--seed=N`, `--connect=HOST[:PORT]`. Reason: Godot hands those to the game via `OS.GetCmdlineUserArgs()` and keeps its own flags (`--headless`, `--path`) separate.
- 2026-10-23: Default port 7777. Reason: arbitrary, unprivileged, commonly free.
- 2026-10-25: The godot_voxel binaries (134 MB) are not committed. `./build.sh deps` / `.\build.ps1 deps` downloads the pinned release and checks its SHA-256. Reason: keeps the repository small while everyone runs the same build.
- 2026-10-25: Block picking uses our own grid raycast in Sim (`VoxelRaycast`), not godot_voxel's. Reason: the server must check edits without a renderer, and client and server should agree on what was aimed at.
- 2026-10-25: A client started without `--connect` runs the single-player sandbox (M1a). Reason: simplest way to play locally; `--connect` keeps the networked path.
- 2026-10-25: Player tunables (reach, speeds, gravity, look sensitivity) live in `data/player.json`. Reason: the data-file convention; reach is a rule the server will enforce.

## M1

- 2026-10-25: Build rules live in `Sim/Players/BuildRules` (can break, can place, in reach). Reason: the client checks them before an edit and the M1b server must re-check the same rules.
- 2026-10-25: The bottom block layer (y = 0) is unbreakable. Reason: simplest way to stop players digging out of the world.
- 2026-10-25: The player is a box collider (`width` x `height` x `width` from `data/player.json`) on a `CharacterBody3D`, walking on godot_voxel's collision mesh. No auto-step: jumping clears one block. Reason: a box matches the block grid and the placement rule; auto-step can come later if climbing feels fiddly.
- 2026-10-25: Physics interpolation is on, and the camera is top-level, following the interpolated body each frame. Reason: smooth view on high-refresh monitors with 60 Hz physics, without lagging mouse look.
- 2026-10-25: F toggles flying with collisions off (noclip). Reason: a debug and building aid during M1; revisit before matches have rules.
- 2026-10-25: Input actions are registered from code (`Client/InputActions.cs`) rather than in `project.godot`. Reason: readable bindings in one place; project.godot stores them as serialized objects.
- 2026-10-25: The hotbar holds every non-air block type. Reason: no inventory or resources until M3.
- 2026-10-25: A sand-coloured plane continues the seabed outside the map. Reason: otherwise the map's square edge shows through the water.

