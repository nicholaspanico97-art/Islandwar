# Island War build helper for Windows. Same commands as build.sh.
# Engine commands need Godot 4.7.2 (.NET build): put it on PATH as `godot` or set $env:GODOT.
#
#   .\build.ps1 deps     (download the godot_voxel GDExtension into addons/, once per checkout)
#   .\build.ps1 build
#   .\build.ps1 test
#   .\build.ps1 server --port=7777
#   .\build.ps1 client --connect=127.0.0.1:7777
#   .\build.ps1 synctest  (headless server + three bot clients; fails unless every world matches)
#   .\build.ps1 matchtest (headless server on the fast test preset + two bots following a whole match)
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
# joins late and a pilot bot that flies the plane. Each bot exits 0 only if its world hash matches the
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
    Wait-ForLine "builder" "built the tower"
    $bots += Start-Godot "breaker" @("--connect=127.0.0.1:$port", "--bot=breaker", "--team=red")
    $bots += Start-Godot "pilot" @("--connect=127.0.0.1:$port", "--bot=pilot", "--team=blue")
    $bots | Wait-Process -Timeout 120
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

# The automated match test (docs/testing.md): a whole match on the fast test preset, followed by one
# watcher bot per team. Each exits 0 only if the stages, the world reset and the hashes all check out.
function Invoke-MatchTest {
  $script:logs = Join-Path ([IO.Path]::GetTempPath()) "islandwar-matchtest"
  New-Item -ItemType Directory -Force $logs | Out-Null
  Get-ChildItem $logs | Remove-Item
  $port = 7791
  $server = Start-Godot "server" @("--server", "--port=$port", "--preset=test")
  $bots = @()
  try {
    Wait-ForLine "server" "listening on port"
    $bots += Start-Godot "red" @("--connect=127.0.0.1:$port", "--bot=watcher", "--team=red", "--name=red-watcher")
    $bots += Start-Godot "blue" @("--connect=127.0.0.1:$port", "--bot=watcher", "--team=blue", "--name=blue-watcher")
    $bots | Wait-Process -Timeout 120
  } finally {
    $bots + $server | Where-Object { -not $_.HasExited } | Stop-Process -Force
  }

  Get-Content (Join-Path $logs "red.log"), (Join-Path $logs "blue.log") | Select-String "^\[bot" | ForEach-Object { $_.Line }
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
