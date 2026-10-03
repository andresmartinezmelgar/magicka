param([Parameter(Mandatory=$true)][string]$Godot)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root "src/Arcana.Client/Arcana.Client.csproj") -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $Godot --path (Join-Path $root "src/Arcana.Client")
exit $LASTEXITCODE
