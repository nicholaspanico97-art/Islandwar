# Island War build helper for Windows. Same commands as build.sh.
# Engine commands need Godot 4.7.2 (.NET build): put it on PATH as `godot` or set $env:GODOT.
#
#   .\build.ps1 deps     (download the godot_voxel GDExtension into addons/, once per checkout)
#   .\build.ps1 build
#   .\build.ps1 test
#   .\build.ps1 server --port=7777
#   .\build.ps1 client --connect=127.0.0.1:7777
#   .\build.ps1 synctest  (headless server + three bot clients; fails unless every world matches)
#   .\build.ps1 matchtest (five headless scenarios: a whole fast match, a nuke win, a rejoin and concession, research, combat)
param([string]$Command = "build", [Parameter(ValueFromRemainingArguments = $true)][string[]]$GameArgs = @())
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$godot = if ($env:GODOT) { $env:GODOT } else { "godot" }

# godot_voxel GDExtension. Keep in step with build.sh and the README's Versions table.
$voxelUrl = "https://github.com/Zylann/godot_voxel/releases/download/v1.7x/GodotVoxelExtension.zip"
$voxelSha256 = "600737572a5e25541ba6f503e842a3717ba19afafa5474510a6ceff995a1d2d8"

function Invoke-Checked([scriptblock]$Block) { & $Block; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }

function Install-Voxel {
  $zip = Join-Path ([IO.Path]::GetTempPath()) "GodotVoxelExtension.zip"
  Invoke-WebRequest -Uri $voxelUrl -OutFile $zip
  if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $voxelSha256) { Remove-Item $zip; Write-Error "godot_voxel download failed its checksum."; exit 1 }
  if (Test-Path addons/zylann.voxel) { Remove-Item addons/zylann.voxel -Recurse -Force }
  Expand-Archive $zip -DestinationPath . -Force
  Remove-Item $zip
  Write-Host "godot_voxel installed in addons/zylann.voxel. Run 'godot --headless --path . --import' once."
}

# Starts headless Godot with game arguments, logging to $logs/<name>.log.
function Start-Godot([string]$Name, [string[]]$Arguments) {
  $process = Start-Process $godot -ArgumentList (@("--headless", "--path", ".", "--") + $Arguments) -PassThru -NoNewWindow `
    -RedirectStandardOutput (Join-Path $logs "$Name.log") -RedirectStandardError (Join-Path $logs "$Name.err")
  $null = $process.Handle  # keeps the exit code readable after the process ends
  return $process
}

function Wait-ForLine([string]$Name, [string]$Pattern, [int]$Seconds = 60) {
  $log = Join-Path $logs "$Name.log"
  $deadline = (Get-Date).AddSeconds($Seconds)
  while ((Get-Date) -lt $deadline) {
    if ((Test-Path $log) -and (Select-String -Path $log -Pattern $Pattern -Quiet)) { return }
    Start-Sleep -Milliseconds 100
  }
  throw "Timed out waiting for '$Pattern' in $log"
}

# The automated sync test (docs/testing.md): a headless server, a builder bot, then a breaker bot that
# joins late and a pilot bot that flies the plane. Each bot exits 0 only if its game hash matches the
# server's (and, for the pilot, the server never had to correct its prediction).
function Invoke-SyncTest {
  $script:logs = Join-Path ([IO.Path]::GetTempPath()) "islandwar-synctest"
  New-Item -ItemType Directory -Force $logs | Out-Null
  Get-ChildItem $logs | Remove-Item
  $port = 7790
  # Developer mode lets the bots pick their teams and noclip to their plane; the practice preset never
  # starts a match, so nothing resets the world under them.
  $server = Start-Godot "server" @("--server", "--port=$port", "--dev", "--preset=practice")
  $bots = @()
  try {
    Wait-ForLine "server" "listening on port"
    $bots += Start-Godot "builder" @("--connect=127.0.0.1:$port", "--bot=builder", "--team=red")
    Wait-ForLine "builder" "built the tower and the crate"
    $bots += Start-Godot "breaker" @("--connect=127.0.0.1:$port", "--bot=breaker", "--team=red")
    $bots += Start-Godot "pilot" @("--connect=127.0.0.1:$port", "--bot=pilot", "--team=blue")
    $bots | Wait-Process -Timeout 200
  } finally {
    $bots + $server | Where-Object { -not $_.HasExited } | Stop-Process -Force
  }

  Get-Content (Join-Path $logs "builder.log"), (Join-Path $logs "breaker.log"), (Join-Path $logs "pilot.log") | Select-String "^\[bot" | ForEach-Object { $_.Line }
  if (($bots | Where-Object { $_.ExitCode -ne 0 }).Count -gt 0) {
    Write-Host "Sync test FAILED. Logs: $logs"
    exit 1
  }

  Write-Host "Sync test PASSED"
}

# One match-test scenario: a server with the given arguments and one bot per team. Returns the bot
# processes after they've finished.
function Invoke-MatchScenario([string]$Name, [int]$Port, [string[]]$ServerArgs, [string]$RedBot, [string]$BlueBot) {
  $server = Start-Godot "$Name-server" (@("--server", "--port=$Port") + $ServerArgs)
  $bots = @()
  try {
    Wait-ForLine "$Name-server" "listening on port"
    $bots += Start-Godot "$Name-red" @("--connect=127.0.0.1:$Port", "--bot=$RedBot", "--team=red", "--name=red-$RedBot")
    $bots += Start-Godot "$Name-blue" @("--connect=127.0.0.1:$Port", "--bot=$BlueBot", "--team=blue", "--name=blue-$BlueBot")
    $bots | Wait-Process -Timeout 200
  } finally {
    $bots + $server | Where-Object { -not $_.HasExited } | Stop-Process -Force
  }

  # Write-Host, not plain output: anything else a PowerShell function prints becomes part of its return value.
  Get-Content (Join-Path $logs "$Name-red.log"), (Join-Path $logs "$Name-blue.log") | Select-String "^\[bot" | ForEach-Object { Write-Host $_.Line }
  return $bots
}

# The rejoin scenario: Red's player drops out mid-match and comes back with the same identity token while
# asking for Blue. Developer mode would grant Blue, so only the rejoin rule puts it back on Red. Then Blue
# concedes and both must see Red win.
function Invoke-RejoinScenario([int]$Port) {
  $token = "rejoin-test-token"
  $server = Start-Godot "rejoin-server" @("--server", "--port=$Port", "--preset=nuketest", "--dev")
  $bots = @()
  try {
    Wait-ForLine "rejoin-server" "listening on port"
    $bots += Start-Godot "rejoin-conceder" @("--connect=127.0.0.1:$Port", "--bot=conceder", "--team=blue", "--name=blue-conceder")
    $leaver = Start-Godot "rejoin-leaver" @("--connect=127.0.0.1:$Port", "--bot=leaver", "--team=red", "--name=red-leaver", "--token=$token")
    $leaver | Wait-Process -Timeout 60
    $bots += $leaver
    $bots += Start-Godot "rejoin-returner" @("--connect=127.0.0.1:$Port", "--bot=returner", "--team=blue", "--name=sneaky", "--token=$token")
    $bots | Wait-Process -Timeout 200
  } finally {
    $bots + $server | Where-Object { -not $_.HasExited } | Stop-Process -Force
  }

  foreach ($name in "rejoin-conceder", "rejoin-leaver", "rejoin-returner") {
    Get-Content (Join-Path $logs "$name.log") | Select-String "^\[bot" | ForEach-Object { Write-Host $_.Line }
  }

  return $bots
}

# The research scenario: one bot alone on Red on the quick preset (so warmup never ends), mining stone for
# research points and researching Mining and Logging.
function Invoke-ResearchScenario([int]$Port) {
  $server = Start-Godot "research-server" @("--server", "--port=$Port", "--preset=quick")
  $bots = @()
  try {
    Wait-ForLine "research-server" "listening on port"
    $bots += Start-Godot "research-red" @("--connect=127.0.0.1:$Port", "--bot=researcher", "--team=red", "--name=red-researcher")
    $bots | Wait-Process -Timeout 200
  } finally {
    $bots + $server | Where-Object { -not $_.HasExited } | Stop-Process -Force
  }

  Get-Content (Join-Path $logs "research-red.log") | Select-String "^\[bot" | ForEach-Object { Write-Host $_.Line }
  return $bots
}

# The automated match test (docs/testing.md). First a whole match on the fast test preset, followed by a
# watcher bot per team: stages, world reset and hashes. Then a nuke match: Red's nuker flies over and
# nukes Blue's core in phase 3, and both bots must see Red win by core hit.
function Invoke-MatchTest {
  $script:logs = Join-Path ([IO.Path]::GetTempPath()) "islandwar-matchtest"
  New-Item -ItemType Directory -Force $logs | Out-Null
  Get-ChildItem $logs | Remove-Item
  $bots = @(Invoke-MatchScenario "flow" 7791 @("--preset=test") "watcher" "watcher")
  # Developer mode lets the nuker noclip to its plane.
  $bots += @(Invoke-MatchScenario "nuke" 7792 @("--preset=nuketest", "--dev") "nuker" "defender")
  $bots += @(Invoke-RejoinScenario 7793)
  $bots += @(Invoke-ResearchScenario 7794)
  # Developer mode lets the archer noclip over to the other island; practice is creative (no bow needed).
  $bots += @(Invoke-MatchScenario "combat" 7795 @("--preset=practice", "--dev") "archer" "target")
  if (($bots | Where-Object { $_.ExitCode -ne 0 }).Count -gt 0) {
    Write-Host "Match test FAILED. Logs: $logs"
    exit 1
  }

  Write-Host "Match test PASSED"
}

switch ($Command) {
  "deps"     { Install-Voxel }
  "build"    { Invoke-Checked { dotnet build IslandWar.sln } }
  "test"     { Invoke-Checked { dotnet test tests/IslandWar.Sim.Tests } }
  "server"   { Invoke-Checked { dotnet build IslandWar.csproj }; & $godot --headless --path . -- --server @GameArgs }
  "client"   { Invoke-Checked { dotnet build IslandWar.csproj }; & $godot --path . -- @GameArgs }
  "synctest" { Invoke-Checked { dotnet build IslandWar.csproj }; Invoke-SyncTest }
  "matchtest" { Invoke-Checked { dotnet build IslandWar.csproj }; Invoke-MatchTest }
  default    { Write-Error "Unknown command $Command. Use deps, build, test, server, client, synctest or matchtest."; exit 2 }
}
