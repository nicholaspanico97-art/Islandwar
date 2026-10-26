#!/usr/bin/env bash
# Island War build helper. Needs the .NET 8 SDK. Engine commands need Godot 4.7.2 (.NET build):
# put it on PATH as `godot` or set GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64.
#
#   ./build.sh deps               download the godot_voxel GDExtension into addons/ (once per checkout)
#   ./build.sh build              compile all C# projects
#   ./build.sh test               run the Sim unit tests
#   ./build.sh server [args...]   run the headless dedicated server, e.g. ./build.sh server --port=7777
#   ./build.sh client [args...]   run a client, e.g. ./build.sh client --connect=127.0.0.1:7777
#   ./build.sh synctest           headless server + two bot clients; fails unless every world matches
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

# Waits up to a minute for a line matching $2 in log file $1.
wait_for_line() {
  for _ in $(seq 600); do
    grep -q "$2" "$1" 2>/dev/null && return 0
    sleep 0.1
  done
  echo "Timed out waiting for '$2' in $1" >&2
  return 1
}

# The automated sync test (docs/testing.md): a headless server, a builder bot, then a breaker bot that
# joins late. Each bot exits 0 only if its world hash matches the server's.
synctest() {
  dotnet build IslandWar.csproj
  local logs port=7790 builder breaker builder_status=0 breaker_status=0
  logs="$(mktemp -d)"
  "$GODOT" --headless --path . -- --server --port=$port >"$logs/server.log" 2>&1 &
  server_pid=$!  # global: the EXIT trap runs after this function returns
  trap 'kill "$server_pid" 2>/dev/null || true' EXIT

  wait_for_line "$logs/server.log" "listening on port"
  "$GODOT" --headless --path . -- --connect=127.0.0.1:$port --bot=builder >"$logs/builder.log" 2>&1 &
  builder=$!
  wait_for_line "$logs/builder.log" "built the tower"
  "$GODOT" --headless --path . -- --connect=127.0.0.1:$port --bot=breaker >"$logs/breaker.log" 2>&1 &
  breaker=$!
  wait "$builder" || builder_status=$?
  wait "$breaker" || breaker_status=$?

  grep -h "^\[bot" "$logs/builder.log" "$logs/breaker.log" || true
  if [ "$builder_status" -ne 0 ] || [ "$breaker_status" -ne 0 ]; then
    echo "Sync test FAILED. Logs: $logs"
    exit 1
  fi
  echo "Sync test PASSED"
}

command="${1:-build}"
shift || true

case "$command" in
  deps) fetch_voxel ;;
  build) dotnet build IslandWar.sln ;;
  test) dotnet test tests/IslandWar.Sim.Tests ;;
  server) dotnet build IslandWar.csproj && "$GODOT" --headless --path . -- --server "$@" ;;
  client) dotnet build IslandWar.csproj && "$GODOT" --path . -- "$@" ;;
  synctest) synctest ;;
  *) echo "Unknown command '$command'. Use deps, build, test, server, client or synctest." >&2; exit 2 ;;
esac
