# Island War (working title)

Multiplayer team game. Two islands, two teams, 12v12 to start. Players gather resources, build an industrial base, research, and attack each other across the water with piloted aircraft, missiles, and ships. Matches last 2–4 hours. The win condition is researching, building, and successfully delivering a nuke to the enemy's industrial core.

The feel we want: old modded Minecraft (Flan's, ICBM) military-industrial sandbox, without the frustration of logging in to a cratered base. Building is the joy; destruction is the drama. Arcade, not simulation.

Solo/small-team project. Built with Claude Code as the primary implementer. Experience is a goal in itself, so prefer clear, maintainable code over clever code.

> **Revision note (2026-10-23):** This plan was reviewed and revised. Changes from the original are marked **[REV]**. The reasoning behind them is in "Review notes" at the end. Record each one in `docs/decisions.md` when the repo is set up.

---

## Design pillars (do not violate without discussion)

1. **The clock drives escalation, not just tech.** Weapon range and payload scale partly with match time (think Factorio evolution factor). Research decides *what* you get within a phase, not *whether* you reach the next phase. This prevents early leads from snowballing into a 90-minute formality.
2. **Rebuilding is cheaper than destroying for most of the match.** Early and mid game, bombed structures come back in minutes. Late game this flips so damage sticks and the match can end.
3. **Fuel is the chokepoint.** Planes, ships, missiles, and the enrichment plant all burn fuel. Oil depots and supply lines are the primary conventional targets. Hitting them costs the enemy tempo, not buildings.
4. **The nuke is a pipeline, not a weapon.** Research → enrichment plant → warhead assembly → player-flown delivery to the enemy core. Every stage is a physical structure the enemy can scout and hit. Enrichment is slow, loud, and detectable.
5. **Recon has a job before bombing.** Fog of war. Scouts find depots, AA, and the enrichment plant. AA has limited range and sectors, so placement is a puzzle and recon is the answer key. Decoys and camouflage are legitimate play.
6. **Weak bunkers, strong repair.** Turtling must not be the dominant strategy.
7. **All vehicles are player-piloted.** Pilot count is a real cost at 12 players.
8. **[REV] Fun at small scale.** The game must be fun at 4v4 in about 45 minutes, not only at 12v12 over 3 hours. Finding 24 players for an afternoon is the hardest problem this project has. A small match is a real mode, not just a test setup.

---

## Key design decisions (settled)

- Team size: 12v12 target. Design and test at 2v2 first; nothing should assume more than 24 players.
- **[REV]** Two match presets from the start, both defined in data: **Full** (12v12, 2–4 h) and **Compressed** (4v4, about 45 min). Phase timings, resource rates, and repair costs scale per preset.
- **[REV]** Drop-in/drop-out. Players can join and leave mid-match. A late joiner gets the full world state, team assignment, and current phase. This shapes the netcode, so design for it from M1.
- **[REV]** Guaranteed match end. The nuke is the primary win. If no nuke lands by a hard time limit, the match ends on a score (core health, industrial output, or similar; decide during M2). A team can also vote to concede.
- **[REV]** Team build protection. Teammates cannot freely destroy each other's builds (protection rules or permissions; pick the simplest option before M3). One griefer must not be able to ruin three hours for eleven people.
- Research costs resources (same ore/fuel pool as weapons), so build-vs-research is a real tradeoff.
- Nuke must hit the enemy industrial core structure, not anywhere on the island, so AA matters.
- Defense gets a slight edge late game (AA out-ranges the bomber drop point) to avoid coin-flip double-nuke finishes. **[REV]** Attackers must still have counters (destroying radar first, overwhelming AA, escorts, decoys) so the late game can't become a permanent stalemate.
- Conventional raids need satisfying soft targets: fuel depots, parked planes, ore stockpiles, docked ships. Things that burn visibly. **[REV]** Losing a depot must hurt for real. With pillars 1, 2, and 6 all pushing against snowballing, raids risk feeling pointless.
- Oil depot destruction should be big, loud, and burn for minutes. It's the set piece.
- Early bombers have tiny payloads: scouts with a grudge, not base-killers.

Open questions (decide during prototyping, don't block on them):
- Exact phase timings. Starting guess: Phase 1 (0–45 min) nothing crosses water but scouts; Phase 2 (45–120) ships, short-range rockets, light bombers; Phase 3 (120+) full payloads, nuke pipeline unlockable, repair costs rise. **[REV]** Concern: 45 minutes with only scouts crossing the water may be too quiet. Start playtests with a Phase 1 of about 20 minutes and lengthen it only if players are still busy building.
- Whether oil comes from a fixed depot, an offshore rig, or must be transported (pipes/trucks/tankers). Transport creates supply-line gameplay but adds scope.
- Research tree contents.
- **[REV]** Score formula for the time-limit ending.

---

## Tech stack (settled)

- **Engine:** Godot 4.x (latest stable). Chosen for text-based scenes, headless builds, open source, no licensing risk, and because it iterates well with Claude Code.
- **Language:** C# for simulation, networking, and voxel/vehicle code. GDScript acceptable for UI and glue.
- **Voxel terrain/building:** `godot_voxel` (Zylann). Blocky Minecraft-style. **[REV]** *Provisional, to be confirmed by the M0 test below.* godot_voxel ships as an engine module (requires compiling Godot from source, and a .NET-enabled build adds work) or as a GDExtension (historically awkward to call from C#, often by string name without type checking). Check the current docs. If the integration fights us, the fallback is a minimal blocky chunk mesher in C#. That's a small job, because the world is two bounded islands, not an infinite world. Either way, voxel-edit networking is our own code.
- **Physics:** Jolt (built into Godot since 4.4; no plugin needed). Vehicles are kinematic with hand-tuned arcade flight/sailing models, not rigid-body realism.
- **Networking:** Godot high-level multiplayer over ENet. Dedicated server, fully authoritative. Clients send inputs only. Server owns world state, voxel edits, vehicle state, damage, and match state. Never trust the client.
  - **[REV] Client-side prediction:** the client predicts its own character and vehicle, and the server reconciles. All other entities are interpolated between server snapshots. Without this, flying feels laggy at normal ping. The server stays authoritative for damage, voxel edits, and match state.
  - **[REV]** Godot's `MultiplayerSynchronizer`/`MultiplayerSpawner` are fine for M1. Expect to replace hot paths with custom snapshot/delta replication by M3 (24 players, projectiles, voxel deltas).
  - **[REV] Late-join sync:** world state = terrain seed + voxel edit deltas per chunk, so a joining client can rebuild the current world.
- **Target platform:** Windows and Linux desktop. Steam later, maybe. Distribute early builds via itch.io.

Before writing engine-specific code, check the current Godot 4 and godot_voxel docs/versions. APIs move. Pin versions in the README once chosen.

---

## Build order

Build in this order. Do not start a later milestone until the earlier one is playable and fun to mess with. Resist building the research tree before cratering feels good.

### M0 — Project skeleton
- Godot project with C# enabled, Jolt enabled.
- **[REV] godot_voxel + C# test (one day max).** Get a voxel terrain rendering and editable from C#. Record the result in `decisions.md`: godot_voxel (module or GDExtension) or the custom C# mesher fallback.
- Headless dedicated server build and client build from one project. `--server` flag.
- Runs from the command line. CI-less is fine; a `build.sh`/`build.ps1` is enough.
- Git, `.gitignore`, README with exact engine/module versions.

### M1 — Vertical slice (the only thing that matters right now)
Goal: two players on a network, one small island, place and destroy blocks, fly one plane, drop one bomb that craters blocks. Ten minutes of fun.

**[REV]** Split into four demos. Each one is playable before the next starts:
- **M1a — Local sandbox.** One small flat-ish island with simple noise terrain. Player controller: walk, jump, place block, break block. Single player.
- **M1b — Networked blocks.** Server-authoritative voxel edits replicated to clients. Client-side prediction for the player character. A client joining late sees the current world.
- **M1c — Plane.** One aircraft: arcade flight model, take off from ground, land. Enter/exit. Networked, with prediction for the pilot.
- **M1d — Bomb.** Drop from plane, server resolves impact, sphere of voxels removed, clients see it.

Plus:
- Simple sync test: two clients, one server, one person builds a tower, the other bombs it.
- **[REV] Automated version of the sync test:** headless server + scripted bot clients. Bot A builds a tower, bot B bombs it, assert both clients end with identical voxel state. Claude Code can run this without a screen; humans judge feel.
- No teams, no resources, no UI beyond a crosshair and a hotbar.

### M2 — Match loop
- Two islands, two teams, spawn points, team assignment.
- Match timer with three phases. Phase number is readable by all systems.
- **[REV]** Match presets (Full / Compressed) loaded from `data/`.
- Industrial core structure per team. Match ends when a nuke hits the enemy core (stub the nuke as a special bomb for now).
- **[REV]** Time-limit ending with score, and concede vote.
- **[REV]** Drop-in/drop-out: join mid-match, rejoin after disconnect.
- Lobby, join by IP, player names.

### M3 — Economy and building
- **[REV]** Team build protection (from Key design decisions).
- Resources: ore, fuel. Gathering (mine ore, pump oil).
- Structures as multi-block entities: oil depot, ore stockpile, factory, airfield, AA gun, radar.
- Repair system with cost that scales by phase (cheap early, expensive late).
- Fuel consumption for aircraft. Depot stores fuel; empty depot means no sorties.
- Depot destruction: large explosion, persistent fire effect, fuel lost.

### M4 — Research and the nuke pipeline
- Research station; research costs ore/fuel; unlocks gated by phase.
- Enrichment plant (slow, emits detectable signal), warhead assembly, nuke as bomber payload.
- Nuke loss if the carrying plane is shot down.
- Win condition for real.

### M5 — Recon, AA, fog of war
- Server-side visibility: enemy structures only revealed when scouted (plane line-of-sight, radar range).
- AA gun: player-operated, limited range and firing arc, tracer visibility.
- Radar structure extends detection. Enrichment plant shows up on radar/visibly.
- Decoy structures (cheap, look like real ones).

### M6 — Ships and second aircraft type
- One ship: transport/escort with deck gun. One heavier bomber.
- Only after everything above holds together in a 4v4.

### Later / not now
- Steam integration, matchmaking, persistence, cosmetics, more vehicle variety, supply-line transport, sound design beyond placeholders.
- **[REV]** AI bots to fill empty slots (would help a lot with the player-count problem; revisit after M5).

---

## Engineering conventions

- Server is authoritative for everything. If a client could cheat by editing a value, that value lives on the server.
- Separate `Sim/` (game rules, no Godot node dependencies where practical), `Net/` (replication), `Client/` (rendering, input, UI), `Server/` (entry, match orchestration). Game rules should be testable without a renderer. **[REV]** "Testable" means pure functions and plain C# classes with unit tests. It does **not** mean lockstep determinism. Don't rely on bit-identical floating-point math across machines.
- Tunables (phase timings, fuel burn rates, repair costs, AA range, **[REV]** match presets) live in data files (`.tres` or JSON) under `data/`, never hardcoded. These will be tweaked constantly during playtests.
- Every milestone ends with a written "how to test this" in `docs/testing.md`: exact commands to launch a server and two clients locally. **[REV]** Plus the command to run the automated bot sync test.
- Keep a `docs/decisions.md` log. One line per decision with date and reason.
- Commit small and often. Message format: `M1: add server-authoritative block placement`.
- When unsure about a Godot/godot_voxel API, look it up rather than guess. When a design question is open, pick the simplest option, note it in `decisions.md`, and keep moving.

---

## First session instructions for Claude Code

**[REV]** Run Claude Code locally (Claude Desktop app or `claude remote-control` in the repo folder), not in a cloud session. Most milestones end with "launch two clients and play", which needs a real screen. Cloud sessions are fine later for headless work: Sim code, tests, docs.

1. Read this file fully. Save it to the repo as `docs/game-plan.md`.
2. Verify the local toolchain: Godot 4 (.NET build), .NET SDK, godot_voxel availability for that Godot version. Report versions.
3. Set up M0, including the one-day godot_voxel + C# test. Record the result in `docs/decisions.md`, and seed that file with the **[REV]** decisions above. Confirm a headless server and a client both launch from the command line.
4. Begin M1a (player controller, local block placement), then M1b (networked block placement). Stop and demo after each working piece.

---

## Review notes (why the [REV] changes)

1. **Finding players is the biggest risk, not the code.** Getting 24 people together for a 3-hour match is very hard for an unknown indie game, and a lack of players is what usually kills small multiplayer projects. The system interactions that make this design work (pilot count, AA coverage, Phase 1 pacing) only show up at scale. Hence pillar 8, the Compressed preset, drop-in/drop-out, and bots under "Later".
2. **Matches need a guaranteed end.** A nuke-only win, plus a defense edge late game, can produce a stalemate with no ending. Hence the time limit, score, and concede vote.
3. **Griefing.** In a shared building game, one teammate who breaks your blocks can ruin a long match for everyone else on the team. Hence build protection before M3.
4. **godot_voxel + C# is an unknown.** Test it on day one rather than discovering problems in M1. The bounded map makes a custom-mesher fallback cheap.
5. **Server-authoritative flight needs prediction.** "Clients send inputs only" is the right security model. Taken literally, the pilot would wait a full round trip for the plane to respond, and flying would feel laggy. Predict locally, reconcile with the server.
6. **Phase 1 may be too quiet.** Old modded Minecraft worked because building was the whole point on a persistent server. In a timed match, people want something at stake sooner. Start short and tune.
7. **Watch for "nothing matters".** Three pillars push against snowballing. The fuel-depot set piece is the counterweight, so make it really hurt.
8. **Claude Code can't play the game.** It can build and test the simulation without a screen and run scripted bot clients. Judging how flying and cratering feel stays with a human every iteration. Hence the automated sync test, and the M1 split into small demos.
