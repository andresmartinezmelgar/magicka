param([ValidateSet("duel", "arena", "warfare")][string]$Mode="warfare", [int]$Bots=0, [int]$Port=27015)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:GAME_MODE = $Mode
$env:BOTS = $Bots.ToString()
$env:GAME_PORT = $Port.ToString()
dotnet run --project (Join-Path $root "src/Arcana.Server") -c Release
exit $LASTEXITCODE
