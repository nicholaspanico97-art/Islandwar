#!/usr/bin/env bash
# Island War build helper. Needs the .NET 8 SDK. Engine commands need Godot 4.7.2 (.NET build):
# put it on PATH as `godot` or set GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64.
#
#   ./build.sh deps               download the godot_voxel GDExtension into addons/ (once per checkout)
#   ./build.sh build              compile all C# projects
#   ./build.sh test               run the Sim unit tests
#   ./build.sh server [args...]   run the headless dedicated server, e.g. ./build.sh server --port=7777
#   ./build.sh client [args...]   run a client, e.g. ./build.sh client --connect=127.0.0.1:7777
#   ./build.sh synctest           headless server + three bot clients; fails unless every world matches
#   ./build.sh matchtest          six headless scenarios: a whole fast match, a nuke win, a rejoin and concession, research, combat, sailing
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
# joins late and a pilot bot that flies the plane. Each bot exits 0 only if its game hash matches the
# server's (and, for the pilot, the server never had to correct its prediction).
synctest() {
  dotnet build IslandWar.csproj
  local logs port=7790 builder breaker pilot builder_status=0 breaker_status=0 pilot_status=0
  logs="$(mktemp -d)"
  # Developer mode lets the bots pick their teams and noclip to their plane; the practice preset never
  # starts a match, so nothing resets the world under them.
  "$GODOT" --headless --path . -- --server --port=$port --dev --preset=practice >"$logs/server.log" 2>&1 &
  server_pid=$!  # global: the EXIT trap runs after this function returns
  trap 'kill "$server_pid" 2>/dev/null || true' EXIT

  wait_for_line "$logs/server.log" "listening on port"
  "$GODOT" --headless --path . -- --connect=127.0.0.1:$port --bot=builder --team=red >"$logs/builder.log" 2>&1 &
  builder=$!
  wait_for_line "$logs/builder.log" "built the tower and the crate"
  "$GODOT" --headless --path . -- --connect=127.0.0.1:$port --bot=breaker --team=red >"$logs/breaker.log" 2>&1 &
  breaker=$!
  "$GODOT" --headless --path . -- --connect=127.0.0.1:$port --bot=pilot --team=blue >"$logs/pilot.log" 2>&1 &
  pilot=$!
  wait "$builder" || builder_status=$?
  wait "$breaker" || breaker_status=$?
  wait "$pilot" || pilot_status=$?

  grep -h "^\[bot" "$logs/builder.log" "$logs/breaker.log" "$logs/pilot.log" || true
  if [ "$builder_status" -ne 0 ] || [ "$breaker_status" -ne 0 ] || [ "$pilot_status" -ne 0 ]; then
    echo "Sync test FAILED. Logs: $logs"
    exit 1
  fi
  echo "Sync test PASSED"
}

# One match-test scenario: name, port, red bot, blue bot, then server arguments. Sets scenario_failed=1 if
# either bot fails.
match_scenario() {
  local name=$1 port=$2 red_bot=$3 blue_bot=$4 red blue red_status=0 blue_status=0
  shift 4
  "$GODOT" --headless --path . -- --server --port=$port "$@" >"$logs/$name-server.log" 2>&1 &
  server_pid=$!  # global: the EXIT trap runs after this function returns
  trap 'kill "$server_pid" 2>/dev/null || true' EXIT

  wait_for_line "$logs/$name-server.log" "listening on port"
  "$GODOT" --headless --path . -- --connect=127.0.0.1:$port --bot=$red_bot --team=red --name=red-$red_bot >"$logs/$name-red.log" 2>&1 &
  red=$!
  "$GODOT" --headless --path . -- --connect=127.0.0.1:$port --bot=$blue_bot --team=blue --name=blue-$blue_bot >"$logs/$name-blue.log" 2>&1 &
  blue=$!
  wait "$red" || red_status=$?
  wait "$blue" || blue_status=$?
  kill "$server_pid" 2>/dev/null || true

  grep -h "^\[bot" "$logs/$name-red.log" "$logs/$name-blue.log" || true
  if [ "$red_status" -ne 0 ] || [ "$blue_status" -ne 0 ]; then
    scenario_failed=1
  fi
}

# The rejoin scenario: Red's player drops out mid-match and comes back with the same identity token while
# asking for Blue. Developer mode would grant Blue, so only the rejoin rule puts it back on Red. Then Blue
# concedes and both must see Red win.
rejoin_scenario() {
  local port=$1 token=rejoin-test-token conceder returner leaver_status=0 conceder_status=0 returner_status=0
  "$GODOT" --headless --path . -- --server --port=$port --preset=nuketest --dev >"$logs/rejoin-server.log" 2>&1 &
  server_pid=$!  # global: the EXIT trap runs after this function returns
  trap 'kill "$server_pid" 2>/dev/null || true' EXIT

  wait_for_line "$logs/rejoin-server.log" "listening on port"
  "$GODOT" --headless --path . -- --connect=127.0.0.1:$port --bot=conceder --team=blue --name=blue-conceder >"$logs/rejoin-conceder.log" 2>&1 &
  conceder=$!
  "$GODOT" --headless --path . -- --connect=127.0.0.1:$port --bot=leaver --team=red --name=red-leaver --token=$token >"$logs/rejoin-leaver.log" 2>&1 || leaver_status=$?
  "$GODOT" --headless --path . -- --connect=127.0.0.1:$port --bot=returner --team=blue --name=sneaky --token=$token >"$logs/rejoin-returner.log" 2>&1 &
  returner=$!
  wait "$conceder" || conceder_status=$?
  wait "$returner" || returner_status=$?
  kill "$server_pid" 2>/dev/null || true

  grep -h "^\[bot" "$logs/rejoin-conceder.log" "$logs/rejoin-leaver.log" "$logs/rejoin-returner.log" || true
  if [ "$leaver_status" -ne 0 ] || [ "$conceder_status" -ne 0 ] || [ "$returner_status" -ne 0 ]; then
    scenario_failed=1
  fi
}

# A one-bot scenario: name, port, bot (alone on Red), then server arguments; for example the researcher on
# the quick preset (so warmup never ends) or the sailor on practice. Sets scenario_failed=1 if it fails.
solo_scenario() {
  local name=$1 port=$2 bot=$3 bot_status=0
  shift 3
  "$GODOT" --headless --path . -- --server --port=$port "$@" >"$logs/$name-server.log" 2>&1 &
  server_pid=$!  # global: the EXIT trap runs after this function returns
  trap 'kill "$server_pid" 2>/dev/null || true' EXIT

  wait_for_line "$logs/$name-server.log" "listening on port"
  "$GODOT" --headless --path . -- --connect=127.0.0.1:$port --bot=$bot --team=red --name=red-$bot >"$logs/$name-red.log" 2>&1 || bot_status=$?
  kill "$server_pid" 2>/dev/null || true

  grep -h "^\[bot" "$logs/$name-red.log" || true
  if [ "$bot_status" -ne 0 ]; then
    scenario_failed=1
  fi
}

# The automated match test (docs/testing.md). First a whole match on the fast test preset, followed by a
# watcher bot per team: stages, world reset and hashes. Then a nuke match: Red's nuker flies over and
# nukes Blue's core in phase 3, and both bots must see Red win by core hit.
matchtest() {
  dotnet build IslandWar.csproj
  logs="$(mktemp -d)"
  scenario_failed=0
  match_scenario flow 7791 watcher watcher --preset=test
  # Developer mode lets the nuker noclip to its plane.
  match_scenario nuke 7792 nuker defender --preset=nuketest --dev
  rejoin_scenario 7793
  solo_scenario research 7794 researcher --preset=quick
  # Developer mode lets the archer noclip over to the other island; practice is creative (no bow needed).
  match_scenario combat 7795 archer target --preset=practice --dev
  # Practice is creative (the dock and galley need nothing); developer mode lets the sailor noclip to the shore.
  solo_scenario sail 7796 sailor --preset=practice --dev
  if [ "$scenario_failed" -ne 0 ]; then
    echo "Match test FAILED. Logs: $logs"
    exit 1
  fi
  echo "Match test PASSED"
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
  matchtest) matchtest ;;
  *) echo "Unknown command '$command'. Use deps, build, test, server, client, synctest or matchtest." >&2; exit 2 ;;
esac
