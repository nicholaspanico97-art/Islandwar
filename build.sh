#!/usr/bin/env bash
# Island War build helper. Needs the .NET 8 SDK. Engine commands need Godot 4.7.2 (.NET build):
# put it on PATH as `godot` or set GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64.
#
#   ./build.sh deps               download the godot_voxel GDExtension into addons/ (once per checkout)
#   ./build.sh build              compile all C# projects
#   ./build.sh test               run the Sim unit tests
#   ./build.sh server [args...]   run the headless dedicated server, e.g. ./build.sh server --port=7777
#   ./build.sh client [args...]   run a client, e.g. ./build.sh client --connect=127.0.0.1:7777
set -euo pipefail
cd "$(dirname "$0")"

GODOT="${GODOT:-godot}"

# godot_voxel GDExtension. Keep in step with build.ps1 and the README's Versions table.
VOXEL_URL="https://github.com/Zylann/godot_voxel/releases/download/v1.7x/GodotVoxelExtension.zip"
VOXEL_SHA256="600737572a5e25541ba6f503e842a3717ba19afafa5474510a6ceff995a1d2d8"

fetch_voxel() {
  local zip
  zip="$(mktemp)"
  curl -fL -o "$zip" "$VOXEL_URL"
  echo "$VOXEL_SHA256  $zip" | sha256sum -c -
  rm -rf addons/zylann.voxel
  unzip -q "$zip" -d .
  rm "$zip"
  echo "godot_voxel installed in addons/zylann.voxel. Run 'godot --headless --path . --import' once."
}

command="${1:-build}"
shift || true

case "$command" in
  deps) fetch_voxel ;;
  build) dotnet build IslandWar.sln ;;
  test) dotnet test tests/IslandWar.Sim.Tests ;;
  server) dotnet build IslandWar.csproj && "$GODOT" --headless --path . -- --server "$@" ;;
  client) dotnet build IslandWar.csproj && "$GODOT" --path . -- "$@" ;;
  *) echo "Unknown command '$command'. Use deps, build, test, server or client." >&2; exit 2 ;;
esac
