param([Parameter(Mandatory=$true)][string]$GodotPath)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$godot = (Resolve-Path -LiteralPath $GodotPath).Path
$package = Join-Path $repo 'artifacts/windows'
$version = (Get-Content (Join-Path $repo 'VERSION') -Raw).Trim()
. (Join-Path $PSScriptRoot 'build-support.ps1')
if (Test-Path $package) { Remove-Item $package -Recurse -Force }
New-Item -ItemType Directory -Path $package -Force | Out-Null
Push-Location $repo
try {
    dotnet publish src/Arcana.Server -c Release -r win-x64 --self-contained true -p:DebugSymbols=false -p:DebugType=None -o "$package/server" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el servidor autonomo.' }
    dotnet build src/Arcana.Client/Arcana.Client.csproj -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar el cliente.' }
    & $godot --headless --editor --path src/Arcana.Client --import
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron importar los recursos.' }
    & $godot --headless --path src/Arcana.Client --export-release 'Windows Desktop' "$package/ArcanaArena.exe"
    if ($LASTEXITCODE -ne 0) { throw 'La exportacion Windows ha fallado.' }
    Test-GamePackage -Package $package
    $metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo("$package/ArcanaArena.exe")
    if ($metadata.FileVersion -ne "$version.0" -or $metadata.ProductVersion -ne "$version.0") {
        throw 'La version del ejecutable Windows no coincide con VERSION.'
    }
    Copy-Item scripts/Configurar-LAN.ps1 "$package/Configurar-LAN.ps1"
    Copy-Item docs/JUGAR-WINDOWS.txt "$package/LEEME.txt"
    Copy-Item docs/COMO-GANAR.md "$package/COMO-GANAR.md"
    Copy-Item docs/TIPOS-DE-MAGO.md "$package/TIPOS-DE-MAGO.md"
    Copy-Item docs/THIRD-PARTY-NOTICES.txt "$package/THIRD-PARTY-NOTICES.txt"
    Copy-Item docs/GODOT-COPYRIGHT.txt "$package/GODOT-COPYRIGHT.txt"
    Copy-Item docs/DOTNET-NOTICES.txt "$package/DOTNET-NOTICES.txt"
    Copy-Item VERSION "$package/VERSION.txt"
    Write-Host "PAQUETE_LISTO: $package"
} finally { Pop-Location }
