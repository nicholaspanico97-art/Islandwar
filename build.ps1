# Island War build helper for Windows. Same commands as build.sh.
# Engine commands need Godot 4.7.2 (.NET build): put it on PATH as `godot` or set $env:GODOT.
#
#   .\build.ps1 build
#   .\build.ps1 test
#   .\build.ps1 server --port=7777
#   .\build.ps1 client --connect=127.0.0.1:7777
param([string]$Command = "build", [Parameter(ValueFromRemainingArguments = $true)][string[]]$GameArgs = @())
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$godot = if ($env:GODOT) { $env:GODOT } else { "godot" }

function Invoke-Checked([scriptblock]$Block) { & $Block; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }

switch ($Command) {
  "build"  { Invoke-Checked { dotnet build IslandWar.sln } }
  "test"   { Invoke-Checked { dotnet test tests/IslandWar.Sim.Tests } }
  "server" { Invoke-Checked { dotnet build IslandWar.csproj }; & $godot --headless --path . -- --server @GameArgs }
  "client" { Invoke-Checked { dotnet build IslandWar.csproj }; & $godot --path . -- @GameArgs }
  default  { Write-Error "Unknown command $Command. Use build, test, server or client."; exit 2 }
}
