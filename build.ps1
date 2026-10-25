# Island War build helper for Windows. Same commands as build.sh.
# Engine commands need Godot 4.7.2 (.NET build): put it on PATH as `godot` or set $env:GODOT.
#
#   .\build.ps1 deps     (download the godot_voxel GDExtension into addons/, once per checkout)
#   .\build.ps1 build
#   .\build.ps1 test
#   .\build.ps1 server --port=7777
#   .\build.ps1 client --connect=127.0.0.1:7777
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

switch ($Command) {
  "deps"   { Install-Voxel }
  "build"  { Invoke-Checked { dotnet build IslandWar.sln } }
  "test"   { Invoke-Checked { dotnet test tests/IslandWar.Sim.Tests } }
  "server" { Invoke-Checked { dotnet build IslandWar.csproj }; & $godot --headless --path . -- --server @GameArgs }
  "client" { Invoke-Checked { dotnet build IslandWar.csproj }; & $godot --path . -- @GameArgs }
  default  { Write-Error "Unknown command $Command. Use deps, build, test, server or client."; exit 2 }
}
