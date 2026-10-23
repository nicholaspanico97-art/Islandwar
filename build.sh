#!/usr/bin/env bash
# Island War build helper. Needs the .NET 8 SDK. Engine commands need Godot 4.7.2 (.NET build):
# put it on PATH as `godot` or set GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64.
#
#   ./build.sh build              compile all C# projects
#   ./build.sh test               run the Sim unit tests
#   ./build.sh server [args...]   run the headless dedicated server, e.g. ./build.sh server --port=7777
#   ./build.sh client [args...]   run a client, e.g. ./build.sh client --connect=127.0.0.1:7777
set -euo pipefail
cd "$(dirname "$0")"

GODOT="${GODOT:-godot}"
command="${1:-build}"
shift || true

case "$command" in
  build) dotnet build IslandWar.sln ;;
  test) dotnet test tests/IslandWar.Sim.Tests ;;
  server) dotnet build IslandWar.csproj && "$GODOT" --headless --path . -- --server "$@" ;;
  client) dotnet build IslandWar.csproj && "$GODOT" --path . -- "$@" ;;
  *) echo "Unknown command '$command'. Use build, test, server or client." >&2; exit 2 ;;
esac
