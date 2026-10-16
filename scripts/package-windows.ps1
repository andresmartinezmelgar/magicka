param([Parameter(Mandatory=$true)][string]$GodotPath, [IO.FileStream]$BuildLease)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$godot = (Resolve-Path -LiteralPath $GodotPath).Path
# El wrapper _console espera a todos los descendientes, incluidos los servidores de .NET.
# Usar el editor directamente conserva stdout en Invoke-Tool sin retener ese Job Object.
# https://github.com/godotengine/godot/issues/110101
if ($godot -match '_console[.]exe$') {
    $nativeEditor = $godot -replace '_console[.]exe$', '.exe'
    if (!(Test-Path -LiteralPath $nativeEditor)) { throw "No se encuentra el editor Godot: $nativeEditor" }
    $godot = $nativeEditor
}
$package = Join-Path $repo 'artifacts/windows'
. (Join-Path $PSScriptRoot 'build-support.ps1')
. (Join-Path $PSScriptRoot 'tool-support.ps1')
. (Join-Path $PSScriptRoot 'build-lock.ps1')
. (Join-Path $PSScriptRoot 'version-support.ps1')
$ownsLease = $null -eq $BuildLease
if ($ownsLease) { $BuildLease = Enter-ArcanaBuildLock -RepositoryRoot $repo }
$locationPushed = $false
$previousBuildEnvironment = @{}
foreach ($buildVariable in @('UseSharedCompilation', 'MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_USE_MSBUILD_SERVER')) {
    $previousBuildEnvironment[$buildVariable] = [Environment]::GetEnvironmentVariable($buildVariable, 'Process')
}
$presetPath = Join-Path $repo 'src/Arcana.Client/export_presets.cfg'
$presetBackup = $null
try {
    Push-Location $repo; $locationPushed = $true
    $buildVersion = Get-ArcanaBuildVersion -RepositoryRoot $repo
    $presetBackup = [IO.File]::ReadAllBytes($presetPath)
    $presets = Update-ArcanaExportVersion -Presets ([IO.File]::ReadAllText($presetPath)) -Version $buildVersion
    [IO.File]::WriteAllText($presetPath, $presets, (New-Object System.Text.UTF8Encoding($false)))
    $env:UseSharedCompilation = 'false'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    if (Test-Path $package) { Remove-Item $package -Recurse -Force }
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    dotnet publish src/Arcana.Server -c Release -r win-x64 --self-contained true -p:DebugSymbols=false -p:DebugType=None -o "$package/server" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el servidor autonomo.' }
    dotnet build src/Arcana.Client/Arcana.Client.csproj -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar el cliente.' }
    Invoke-Tool $godot @('--headless', '--editor', '--path', 'src/Arcana.Client', '--import') -CheckGodotErrors | Out-Null
    Invoke-Tool $godot @('--headless', '--path', 'src/Arcana.Client', '--export-release', 'Windows Desktop', "$package/ArcanaArena.exe") -CheckGodotErrors | Out-Null
    Test-GamePackage -Package $package
    $metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo("$package/ArcanaArena.exe")
    Assert-ArcanaWindowsVersion -Metadata $metadata -Version $buildVersion
    Copy-Item scripts/Configurar-LAN.ps1 "$package/Configurar-LAN.ps1"
    Copy-Item docs/JUGAR-WINDOWS.txt "$package/LEEME.txt"
    Copy-Item docs/COMO-GANAR.md "$package/COMO-GANAR.md"
    Copy-Item docs/TIPOS-DE-MAGO.md "$package/TIPOS-DE-MAGO.md"
    Copy-Item docs/THIRD-PARTY-NOTICES.txt "$package/THIRD-PARTY-NOTICES.txt"
    Copy-Item docs/GODOT-COPYRIGHT.txt "$package/GODOT-COPYRIGHT.txt"
    Copy-Item docs/DOTNET-NOTICES.txt "$package/DOTNET-NOTICES.txt"
    Copy-Item VERSION "$package/VERSION.txt"
    # Configuracion OAuth de una aplicacion instalada (no Firebase Admin).
    # Se incluye automaticamente si se ha provisionado en GitHub Actions.
    $desktopOauth = $env:ARCANA_GOOGLE_DESKTOP_OAUTH_BASE64
    if (![string]::IsNullOrWhiteSpace($desktopOauth)) {
        $bytes = [Convert]::FromBase64String($desktopOauth)
        if ($bytes.Length -gt 8192) { throw 'JSON OAuth de escritorio demasiado grande.' }
        $json = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
        if (!$json.installed.client_id -or $json.installed.client_id -notmatch '^[a-zA-Z0-9_-]+[.]apps[.]googleusercontent[.]com$') {
            throw 'El JSON OAuth no contiene un installed.client_id valido.'
        }
        if ($json.installed.project_id -and $json.installed.project_id -ne 'arcana-arena-f2997') {
            throw 'El cliente OAuth de Windows pertenece a otro proyecto.'
        }
        [IO.File]::WriteAllBytes((Join-Path $package 'google-desktop-oauth.json'), $bytes)
        Write-Host 'GOOGLE_DESKTOP_OAUTH_PACKAGED'
    } else {
        Write-Host 'GOOGLE_DESKTOP_OAUTH_NOT_CONFIGURED: se importara desde el juego'
    }
    & (Join-Path $PSScriptRoot 'write-update-manifest.ps1') -Package $package -Version $buildVersion.Name
    Write-Host "PAQUETE_LISTO: $package"
} finally {
    try {
        if ($null -ne $presetBackup) { [IO.File]::WriteAllBytes($presetPath, $presetBackup) }
        foreach ($buildVariable in $previousBuildEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($buildVariable, $previousBuildEnvironment[$buildVariable], 'Process')
        }
        if ($locationPushed) { Pop-Location }
    }
    finally { if ($ownsLease) { Exit-ArcanaBuildLock -Lease $BuildLease } }
}
